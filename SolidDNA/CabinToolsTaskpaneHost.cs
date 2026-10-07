using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using static CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    [Guid("F8F0D651-4F3A-4202-A5F0-923E4F00EABA")]
    [ProgId(TaskpaneProgId)]
    [ComVisible(true)]
    public sealed class CabinToolsTaskpaneHost : UserControl, ITaskpaneControl
    {
        private const string TaskpaneProgId = "CabinTools.SolidDNA.TaskpaneHost";
        private readonly PropertyPaneSettingsService settingsService = new PropertyPaneSettingsService();
        private readonly Dictionary<string, FieldEditorState> fieldEditors = new Dictionary<string, FieldEditorState>();
        private readonly Dictionary<string, DimensionEditorState> dimensionEditors = new Dictionary<string, DimensionEditorState>();
        private readonly Timer refreshTimer = new Timer();
        private readonly FlowLayoutPanel content = new FlowLayoutPanel();
        private readonly Label fileLabel = new Label();
        private readonly Label configurationLabel = new Label();
        private readonly Label accessLabel = new Label();
        private readonly Label sourceLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Button applyButton = new Button();
        private readonly Button resetButton = new Button();
        private readonly Button syncButton = new Button();
        private readonly Button settingsButton = new Button();
        private readonly Button configurationButton = new Button();
        private readonly ToolTip toolTips = new ToolTip();

        private PropertyPaneProfile profile;
        private IModelDoc2 loadedModel;
        private string loadedPath;
        private string loadedTitle;
        private string loadedConfiguration;
        private bool eventsSubscribed;
        private bool loading;
        private bool dirty;
        private bool resizingChildren;

        public static CabinToolsTaskpaneHost Instance { get; private set; }
        public string ProgId { get { return TaskpaneProgId; } }

        public CabinToolsTaskpaneHost()
        {
            Instance = this;
            refreshTimer.Interval = 350;
            refreshTimer.Tick += RefreshTimer_Tick;
            profile = settingsService.Load();
            BuildShell();
            HandleCreated += HandleCreatedHandler;
            Disposed += DisposedHandler;
            PropertyPaneLogger.Write("Taskpane host constructed.");
        }

        public static void RefreshActiveDocument()
        {
            if (Instance != null)
                Instance.ScheduleRefresh();
        }

        private void BuildShell()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.FromArgb(244, 246, 248);
            content.Dock = DockStyle.Fill;
            content.AutoScroll = true;
            content.BackColor = BackColor;
            content.FlowDirection = FlowDirection.TopDown;
            content.WrapContents = false;
            content.Padding = new Padding(10);
            content.SizeChanged += delegate { ResizeChildren(); };

            TableLayoutPanel header = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel context = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };
            fileLabel.AutoSize = true;
            fileLabel.Font = new Font(Font.FontFamily, Font.Size + 1.0f, FontStyle.Bold);
            configurationLabel.AutoSize = true;
            accessLabel.AutoSize = true;
            sourceLabel.AutoSize = true;
            sourceLabel.Font = new Font(Font, FontStyle.Italic);
            context.Controls.Add(fileLabel);
            context.Controls.Add(configurationLabel);
            context.Controls.Add(accessLabel);
            context.Controls.Add(sourceLabel);

            FlowLayoutPanel headerActions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };
            syncButton.Text = "Sync";
            syncButton.AutoSize = true;
            syncButton.Click += SyncButton_Click;
            StyleSecondaryButton(syncButton);
            settingsButton.Text = "Settings";
            settingsButton.AutoSize = true;
            settingsButton.Click += SettingsButton_Click;
            StyleSecondaryButton(settingsButton);
            configurationButton.Text = "Matrix";
            configurationButton.AutoSize = true;
            configurationButton.Click += delegate { AssemblyConfigurationManagerCommand.ShowMatrix(); };
            StyleSecondaryButton(configurationButton);
            configurationButton.Visible = false;
            headerActions.Controls.Add(syncButton);
            headerActions.Controls.Add(configurationButton);
            headerActions.Controls.Add(settingsButton);
            toolTips.SetToolTip(syncButton, "Reload the local profile and shared Excel lists.");
            toolTips.SetToolTip(settingsButton, "Configure fields, lists, mappings, dimensions, and shared Excel.");
            header.Controls.Add(context, 0, 0);
            header.Controls.Add(headerActions, 1, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 6),
                Padding = new Padding(2)
            };
            applyButton.Text = "Apply";
            applyButton.AutoSize = true;
            applyButton.Click += ApplyButton_Click;
            applyButton.BackColor = Color.FromArgb(0, 103, 184);
            applyButton.ForeColor = Color.White;
            applyButton.FlatStyle = FlatStyle.Flat;
            applyButton.FlatAppearance.BorderSize = 0;
            applyButton.Padding = new Padding(10, 3, 10, 3);
            resetButton.Text = "Reload";
            resetButton.AutoSize = true;
            resetButton.Click += delegate { RefreshFromActiveDocument(true); };
            StyleSecondaryButton(resetButton);
            resetButton.Padding = new Padding(8, 3, 8, 3);
            toolTips.SetToolTip(resetButton, "Discard pending edits and reload the active document.");
            actions.Controls.Add(applyButton);
            actions.Controls.Add(resetButton);

            statusLabel.AutoSize = true;
            statusLabel.BorderStyle = BorderStyle.FixedSingle;
            statusLabel.Padding = new Padding(9);
            statusLabel.Margin = new Padding(0, 6, 0, 6);
            statusLabel.BackColor = Color.White;

            content.Controls.Add(header);
            content.Controls.Add(actions);
            content.Controls.Add(statusLabel);
            Controls.Add(content);
            UpdateSourceIndicator();
            ResizeChildren();
        }

        private static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 198, 206);
            button.BackColor = Color.FromArgb(248, 249, 250);
            button.Padding = new Padding(6, 2, 6, 2);
        }

        private void ResizeChildren()
        {
            if (resizingChildren || content.IsDisposed)
                return;

            int width = Math.Max(220, content.ClientSize.Width - content.Padding.Horizontal - 22);
            resizingChildren = true;
            content.SuspendLayout();
            try
            {
                foreach (Control control in content.Controls)
                {
                    // FlowLayoutPanel asks AutoSize controls for their preferred width.
                    // A GroupBox containing a percent-sized TableLayoutPanel can report a
                    // near-zero preferred width, which makes the entire editor disappear.
                    // Keep height automatic while pinning every section to the pane width.
                    control.MinimumSize = new Size(width, 0);
                    control.MaximumSize = new Size(width, 0);
                    control.Width = width;
                }
            }
            finally
            {
                try { content.ResumeLayout(true); }
                finally { resizingChildren = false; }
            }
        }

        private void HandleCreatedHandler(object sender, EventArgs e)
        {
            Instance = this;
            SubscribeEvents();
            RefreshFromActiveDocument(true);
        }

        private void DisposedHandler(object sender, EventArgs e)
        {
            refreshTimer.Stop();
            UnsubscribeEvents();
            toolTips.Dispose();
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private void SubscribeEvents()
        {
            if (eventsSubscribed)
                return;
            try
            {
                IApplication.ActiveModelInformationChanged += ActiveModelInformationChanged;
                eventsSubscribed = true;
            }
            catch (Exception ex)
            {
                PropertyPaneLogger.Write("Event subscription failed: " + ex.Message);
            }
        }

        private void UnsubscribeEvents()
        {
            if (!eventsSubscribed)
                return;
            try { IApplication.ActiveModelInformationChanged -= ActiveModelInformationChanged; }
            catch { }
            eventsSubscribed = false;
        }

        private void ActiveModelInformationChanged(Model model)
        {
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(new MethodInvoker(RestartRefreshTimer));
                else
                    RestartRefreshTimer();
            }
            catch (InvalidOperationException) { }
        }

        private void RestartRefreshTimer()
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            refreshTimer.Stop();
            RefreshFromActiveDocument(false);
        }

        private void RefreshFromActiveDocument(bool discardEdits)
        {
            IModelDoc2 model = CabinCustomPropertyStore.GetActiveModelDocument();
            if (!CabinCustomPropertyStore.IsSupportedDocument(model))
            {
                if (dirty && !discardEdits)
                {
                    SetStatus("The active document changed. Click Reset to load it; current edits are retained.", true);
                    return;
                }
                ShowNoDocument();
                return;
            }

            if (dirty && !discardEdits && !IsLoadedIdentity(model))
            {
                SetStatus("The active document or configuration changed. Click Reset to load it; current edits are retained.", true);
                return;
            }

            loading = true;
            try
            {
                loadedModel = model;
                loadedPath = model.GetPathName() ?? string.Empty;
                loadedTitle = model.GetTitle() ?? string.Empty;
                loadedConfiguration = CabinCustomPropertyStore.GetActiveConfigurationName(model);
                dirty = false;
                configurationButton.Visible = model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;

                fileLabel.Text = string.IsNullOrWhiteSpace(loadedTitle) ? "<untitled>" : loadedTitle;
                configurationLabel.Text = CabinCustomPropertyStore.SupportsConfigurationProperties(model)
                    ? "Config: " + Display(loadedConfiguration)
                    : "Drawing";
                string block = CabinCustomPropertyStore.GetWriteBlockReason(model);
                accessLabel.Text = string.IsNullOrWhiteSpace(block) ? "Editable" : "Read-only";
                accessLabel.ForeColor = string.IsNullOrWhiteSpace(block) ? Color.DarkGreen : Color.DarkRed;

                RemoveDynamicControls();
                BuildDocumentEditors(model);
                bool canWrite = string.IsNullOrWhiteSpace(block);
                applyButton.Enabled = canWrite;
                SetEditingEnabled(canWrite);
                SetStatus(canWrite
                    ? "Ready. Blank fields are ignored; use Clear to remove a property. Apply does not save the file."
                    : block, !canWrite);
            }
            catch (Exception ex)
            {
                applyButton.Enabled = false;
                SetStatus("Could not load the active document: " + ex.Message, true);
                PropertyPaneLogger.Write("Refresh failed: " + ex);
            }
            finally
            {
                loading = false;
                ResizeChildren();
            }
        }

        private void ShowNoDocument()
        {
            loading = true;
            RemoveDynamicControls();
            loadedModel = null;
            loadedPath = loadedTitle = loadedConfiguration = string.Empty;
            dirty = false;
            configurationButton.Visible = false;
            fileLabel.Text = "No supported document";
            configurationLabel.Text = "Open a part, assembly, or drawing";
            accessLabel.Text = string.Empty;
            applyButton.Enabled = false;
            resetButton.Enabled = true;
            SetStatus("The property pane is ready when a supported document is active.", false);
            loading = false;
        }

        private void RemoveDynamicControls()
        {
            fieldEditors.Clear();
            dimensionEditors.Clear();
            List<Control> remove = content.Controls.Cast<Control>()
                .Where(c => c.Tag as string == "dynamic").ToList();
            foreach (Control control in remove)
            {
                content.Controls.Remove(control);
                control.Dispose();
            }
        }

        private void BuildDocumentEditors(IModelDoc2 model)
        {
            PropertyPaneDocumentKind kind = GetDocumentKind(model);
            PropertyPaneLayout layout = profile.Layouts.FirstOrDefault(l => l.DocumentKind == kind);
            if (layout == null)
                throw new InvalidOperationException("No " + kind + " layout is configured.");

            int insertAt = 1;
            foreach (PropertyPaneGroup definition in layout.Groups.OrderBy(g => g.Order))
            {
                GroupBox group = new GroupBox
                {
                    Text = definition.Label,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(7),
                    Margin = new Padding(0, 0, 0, 8),
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(35, 45, 55),
                    Tag = "dynamic"
                };
                TableLayoutPanel table = new TableLayoutPanel
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    ColumnCount = 4,
                    GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                    Padding = new Padding(3, 5, 3, 5),
                    BackColor = Color.White
                };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                int row = 0;
                foreach (PropertyPaneField field in definition.Fields.Where(f => f.Visible).OrderBy(f => f.Order))
                {
                    FieldEditorState state = CreateFieldEditor(model, field);
                    fieldEditors[field.Id] = state;
                    Label label = new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 5, 3) };
                    label.Text = state.Caption.Text;
                    label.ForeColor = state.Caption.ForeColor;
                    state.Caption = label;
                    table.Controls.Add(label, 0, row);
                    table.Controls.Add(state.Editor, 1, row);
                    table.Controls.Add(state.Scope, 2, row);
                    table.Controls.Add(state.ClearButton, 3, row);
                    row++;
                }
                group.Controls.Add(table);
                content.Controls.Add(group);
                content.Controls.SetChildIndex(group, insertAt++);
            }

            if (kind == PropertyPaneDocumentKind.Part)
            {
                Control dimensions = BuildDimensionGroup(model);
                content.Controls.Add(dimensions);
                content.Controls.SetChildIndex(dimensions, insertAt);
            }
            else if (kind == PropertyPaneDocumentKind.Drawing)
            {
                UpdateDrawingDerivedFields();
            }
        }

        private FieldEditorState CreateFieldEditor(IModelDoc2 model, PropertyPaneField field)
        {
            FieldEditorState state = new FieldEditorState { Field = field, SelectedConfigurations = new List<string>() };
            Control editor;
            if (field.ControlType == PropertyPaneControlType.List)
            {
                ComboBox combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat };
                PropertyPaneNamedList list = profile.Lists.FirstOrDefault(l => string.Equals(l.Name, field.ListName, StringComparison.OrdinalIgnoreCase));
                if (list != null)
                    combo.Items.AddRange(list.Values.Cast<object>().ToArray());
                combo.TextChanged += delegate { EditorChanged(state); };
                editor = combo;
            }
            else if (field.ControlType == PropertyPaneControlType.Date)
            {
                PropertyDatePicker date = new PropertyDatePicker { Dock = DockStyle.Fill };
                date.PropertyValueChanged += delegate { EditorChanged(state); };
                editor = date;
            }
            else
            {
                TextBox text = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
                text.TextChanged += delegate { EditorChanged(state); };
                editor = text;
            }
            state.Editor = editor;

            ComboBox scope = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            foreach (PropertyPaneScope allowed in field.AllowedScopes)
            {
                if (allowed == PropertyPaneScope.Document || CabinCustomPropertyStore.SupportsConfigurationProperties(model))
                    scope.Items.Add(allowed);
            }
            scope.Format += delegate(object sender, ListControlConvertEventArgs e) { e.Value = ScopeLabel((PropertyPaneScope)e.ListItem); };
            scope.SelectedItem = scope.Items.Contains(field.DefaultScope) ? (object)field.DefaultScope : scope.Items[0];
            scope.SelectedIndexChanged += delegate { ScopeChanged(state); };
            state.Scope = scope;

            Button clear = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(3, 0, 0, 0) };
            StyleSecondaryButton(clear);
            clear.Click += delegate { MarkClear(state); };
            state.ClearButton = clear;

            bool drawingNumberOverride =
                field.AutomaticValue == "DrawingNumber" &&
                profile.AllowDrawingNumberOverride;
            bool readOnly =
                (field.ReadOnly || field.ControlType == PropertyPaneControlType.ReadOnly) &&
                !drawingNumberOverride;
            editor.Enabled = !readOnly;
            scope.Enabled = !readOnly && scope.Items.Count > 1;
            clear.Enabled = !readOnly;
            state.Caption = new Label { Text = field.Label };
            LoadFieldValue(model, state);
            return state;
        }

        private void LoadFieldValue(IModelDoc2 model, FieldEditorState state)
        {
            PropertyPaneScope scope = state.Scope.SelectedItem is PropertyPaneScope
                ? (PropertyPaneScope)state.Scope.SelectedItem : PropertyPaneScope.Document;
            PropertyPaneReadValue value;
            if (state.Field.PropertyName == CabinCustomPropertyStore.DescriptionPropertyName && scope != PropertyPaneScope.Document)
                value = PropertyPanePropertyService.ReadDescriptionEffective(model, scope, state.SelectedConfigurations);
            else
                value = PropertyPanePropertyService.Read(model, state.Field.PropertyName, scope, state.SelectedConfigurations);

            string display = string.IsNullOrWhiteSpace(value.ResolvedValue) ? value.RawValue : value.ResolvedValue;
            if (state.Field.ReadOnly && string.IsNullOrWhiteSpace(display))
                display = GetAutomaticDisplay(model, state.Field);
            SetEditorText(state.Editor, display);
            state.Baseline = display ?? string.Empty;
            state.ClearRequested = false;
            state.Caption.ForeColor = value.IsInherited ? Color.DarkOrange : SystemColors.ControlText;
            state.Caption.Text = state.Field.Label + (value.IsInherited ? " (inherited)" : string.Empty);
        }

        private string GetAutomaticDisplay(IModelDoc2 model, PropertyPaneField field)
        {
            if (field.AutomaticValue == "FileName")
                return Path.GetFileNameWithoutExtension(model.GetPathName() ?? model.GetTitle());
            if (field.AutomaticValue == "Title2")
                return CurrentValue("cabin-description");
            if (field.AutomaticValue == "Title3")
                return (CurrentValue("cabin-defined") + " - " + CurrentValue("layout-type")).Trim(' ', '-');
            if (field.AutomaticValue == "DrawingNumber")
            {
                string number;
                string reason;
                return DrawingNumberResolver.TryResolve(profile, CurrentValue("cabin-description"),
                    CurrentValue("cabin-defined"), CurrentValue("layout-type"), out number, out reason)
                    ? number : reason;
            }
            return field.DefaultExpression ?? string.Empty;
        }

        private Control BuildDimensionGroup(IModelDoc2 model)
        {
            GroupBox group = new GroupBox
            {
                Text = "Dimensions (mm)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(7),
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 45, 55),
                Tag = "dynamic"
            };
            TableLayoutPanel table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top,
                ColumnCount = 4, Padding = new Padding(3, 5, 3, 5), BackColor = Color.White };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0;
            foreach (DimensionMap map in profile.Dimensions.OrderBy(d => d.Order))
            {
                DimensionEditorState state = new DimensionEditorState { Map = map, SelectedConfigurations = new List<string>() };
                TextBox value = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
                value.TextChanged += delegate { if (!loading) { state.Dirty = true; MarkDirty(); ValidateDimension(state); } };
                ComboBox scope = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
                scope.Items.AddRange(new object[] { PropertyPaneScope.ActiveConfiguration, PropertyPaneScope.SelectedConfigurations, PropertyPaneScope.AllConfigurations });
                scope.Format += delegate(object sender, ListControlConvertEventArgs e) { e.Value = ScopeLabel((PropertyPaneScope)e.ListItem); };
                scope.SelectedItem = PropertyPaneScope.ActiveConfiguration;
                scope.SelectedIndexChanged += delegate
                {
                    if (loading) return;
                    if ((PropertyPaneScope)scope.SelectedItem == PropertyPaneScope.SelectedConfigurations &&
                        !SelectConfigurations(state.SelectedConfigurations))
                    {
                        loading = true;
                        scope.SelectedItem = PropertyPaneScope.ActiveConfiguration;
                        loading = false;
                    }
                    LoadDimensionValue(model, state);
                };
                Label validation = new Label { AutoSize = true, ForeColor = Color.DarkRed, Anchor = AnchorStyles.Left };
                state.Editor = value; state.Scope = scope; state.Validation = validation;
                LoadDimensionValue(model, state);
                dimensionEditors[map.Id] = state;
                table.Controls.Add(new Label { Text = map.Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                table.Controls.Add(value, 1, row);
                table.Controls.Add(scope, 2, row);
                table.Controls.Add(validation, 3, row);
                row++;
            }
            group.Controls.Add(table);
            return group;
        }

        private void LoadDimensionValue(IModelDoc2 model, DimensionEditorState state)
        {
            bool wasLoading = loading;
            loading = true;
            try
            {
                double millimetres;
                bool mixed;
                string error;
                PropertyPaneScope scope = state.Scope.SelectedItem is PropertyPaneScope
                    ? (PropertyPaneScope)state.Scope.SelectedItem
                    : PropertyPaneScope.ActiveConfiguration;
                if (PropertyPaneDimensionService.TryReadMillimetres(model, state.Map, scope,
                    state.SelectedConfigurations, out millimetres, out mixed, out error))
                {
                    state.Editor.Enabled = true;
                    state.Scope.Enabled = true;
                    toolTips.SetToolTip(state.Validation, string.Empty);
                    state.Editor.Text = mixed
                        ? "<varies>"
                        : millimetres.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
                    state.Baseline = state.Editor.Text;
                    state.Validation.Text = mixed ? "Mixed" : string.Empty;
                    state.Dirty = false;
                }
                else
                {
                    state.Editor.Enabled = false;
                    state.Scope.Enabled = false;
                    state.Validation.Text = IsReferencePlaneMissing(error)
                        ? "Not Found"
                        : error;
                    toolTips.SetToolTip(state.Validation, error);
                }
            }
            finally
            {
                loading = wasLoading;
            }
        }

        private static bool IsReferencePlaneMissing(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.IndexOf("Required reference plane", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("is not a reference plane", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EditorChanged(FieldEditorState state)
        {
            if (loading)
                return;
            state.ClearRequested = false;
            state.ClearButton.BackColor = SystemColors.Control;
            MarkDirty();
            if (GetDocumentKind(loadedModel) == PropertyPaneDocumentKind.Drawing &&
                (state.Field.Id == "cabin-description" || state.Field.Id == "cabin-defined" || state.Field.Id == "layout-type"))
                UpdateDrawingDerivedFields();
        }

        private void ScopeChanged(FieldEditorState state)
        {
            if (loading || loadedModel == null)
                return;
            PropertyPaneScope scope = (PropertyPaneScope)state.Scope.SelectedItem;
            if (scope == PropertyPaneScope.SelectedConfigurations && !SelectConfigurations(state.SelectedConfigurations))
            {
                loading = true;
                state.Scope.SelectedItem = state.Field.DefaultScope;
                loading = false;
                return;
            }
            loading = true;
            try { LoadFieldValue(loadedModel, state); }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            finally { loading = false; }
        }

        private bool SelectConfigurations(List<string> selected)
        {
            using (ConfigurationSelectionDialog dialog = new ConfigurationSelectionDialog(
                PropertyPanePropertyService.GetConfigurationNames(loadedModel), selected))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                selected.Clear();
                selected.AddRange(dialog.SelectedConfigurations);
                return selected.Count > 0;
            }
        }

        private void MarkClear(FieldEditorState state)
        {
            if (MessageBox.Show(this, "Clear '" + state.Field.PropertyName + "' in the selected scope when Apply is clicked?",
                    "Cabin Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            state.ClearRequested = true;
            state.ClearButton.BackColor = Color.MistyRose;
            loading = true;
            SetEditorText(state.Editor, string.Empty);
            loading = false;
            MarkDirty();
        }

        private void UpdateDrawingDerivedFields()
        {
            loading = true;
            try
            {
                UpdateAutomaticField("title2");
                UpdateAutomaticField("title3");
                UpdateAutomaticField("drwnumber");
            }
            finally { loading = false; }
        }

        private void UpdateAutomaticField(string id)
        {
            FieldEditorState state;
            if (fieldEditors.TryGetValue(id, out state) &&
                !(id == "drwnumber" && profile.AllowDrawingNumberOverride))
                SetEditorText(state.Editor, GetAutomaticDisplay(loadedModel, state.Field));
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            IModelDoc2 active = CabinCustomPropertyStore.GetActiveModelDocument();
            if (loadedModel == null || !IsLoadedIdentity(active))
            {
                MessageBox.Show(this, "The active document or configuration changed. Click Reset and review the values before applying.",
                    "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<PropertyPaneEdit> edits = BuildPropertyEdits();
            if (GetDocumentKind(active) == PropertyPaneDocumentKind.Drawing &&
                !DrawingNumberWasManuallyOverridden())
            {
                string number;
                string reason;
                if (!DrawingNumberResolver.TryResolve(profile, CurrentValue("cabin-description"), CurrentValue("cabin-defined"),
                        CurrentValue("layout-type"), out number, out reason))
                {
                    SetStatus(reason + " Drawing properties were not applied.", true);
                    return;
                }
                edits.RemoveAll(e2 => string.Equals(e2.PropertyName, "DrwNumber", StringComparison.OrdinalIgnoreCase));
                FieldEditorState drawingNumberState;
                string existingDrawingNumber = fieldEditors.TryGetValue("drwnumber", out drawingNumberState)
                    ? drawingNumberState.Baseline ?? string.Empty
                    : string.Empty;
                if (!string.Equals(existingDrawingNumber.Trim(), number.Trim(), StringComparison.Ordinal))
                {
                    edits.Add(new PropertyPaneEdit { FieldId = "drwnumber", PropertyName = "DrwNumber",
                        Scope = PropertyPaneScope.Document, Value = number, SelectedConfigurations = new List<string>() });
                }
            }

            List<DimensionEdit> dimensionEdits = new List<DimensionEdit>();
            foreach (DimensionEditorState state in dimensionEditors.Values.Where(s => s.Dirty && s.Editor.Enabled))
            {
                double millimetres;
                if (!TryParseDimension(state, out millimetres))
                {
                    SetStatus("Correct the highlighted dimension values before applying.", true);
                    return;
                }
                dimensionEdits.Add(new DimensionEdit { Map = state.Map, Millimetres = millimetres,
                    Scope = (PropertyPaneScope)state.Scope.SelectedItem, SelectedConfigurations = state.SelectedConfigurations });
            }

            try
            {
                PropertyPaneApplyResult propertyResult = PropertyPanePropertyService.Apply(active, edits);
                if (propertyResult.Errors.Count > 0)
                {
                    SetStatus(propertyResult.Summary, true);
                    return;
                }
                PropertyPaneApplyResult dimensionResult = PropertyPaneDimensionService.Apply(active, dimensionEdits);
                bool rebuild = active.ForceRebuild3(false);
                string summary = propertyResult.Summary;
                if (dimensionEdits.Count > 0)
                    summary += " " + dimensionResult.Summary;
                if (!rebuild)
                    summary += " SOLIDWORKS reported a rebuild error.";
                RefreshFromActiveDocument(true);
                SetStatus(summary, dimensionResult.Errors.Count > 0 || !rebuild);
            }
            catch (Exception ex)
            {
                SetStatus("Apply failed: " + ex.Message, true);
                PropertyPaneLogger.Write("Apply failed: " + ex);
            }
        }

        private List<PropertyPaneEdit> BuildPropertyEdits()
        {
            List<PropertyPaneEdit> edits = new List<PropertyPaneEdit>();
            foreach (FieldEditorState state in fieldEditors.Values)
            {
                bool manualDrawingNumber =
                    state.Field.AutomaticValue == "DrawingNumber" &&
                    profile.AllowDrawingNumberOverride;

                if ((state.Field.ReadOnly ||
                     state.Field.ControlType == PropertyPaneControlType.ReadOnly) &&
                    !manualDrawingNumber)
                    continue;
                string value = GetEditorText(state.Editor).Trim();
                bool changed = !string.Equals(value, state.Baseline ?? string.Empty, StringComparison.Ordinal);
                if (!changed && !state.ClearRequested)
                    continue;
                if (string.IsNullOrWhiteSpace(value) && !state.ClearRequested)
                    continue;
                edits.Add(new PropertyPaneEdit
                {
                    FieldId = state.Field.Id,
                    PropertyName = state.Field.PropertyName,
                    Scope = (PropertyPaneScope)state.Scope.SelectedItem,
                    SelectedConfigurations = state.SelectedConfigurations,
                    Value = value,
                    Clear = state.ClearRequested
                });
            }
            return edits;
        }

        private bool DrawingNumberWasManuallyOverridden()
        {
            if (!profile.AllowDrawingNumberOverride)
                return false;

            FieldEditorState state;
            if (!fieldEditors.TryGetValue("drwnumber", out state))
                return false;

            return !string.Equals(
                GetEditorText(state.Editor).Trim(),
                state.Baseline ?? string.Empty,
                StringComparison.Ordinal);
        }

        private void ValidateDimension(DimensionEditorState state)
        {
            double value;
            TryParseDimension(state, out value);
        }

        private bool TryParseDimension(DimensionEditorState state, out double value)
        {
            bool parsed = double.TryParse(state.Editor.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out value);
            if (!parsed)
                state.Validation.Text = "Invalid number";
            else if (!state.Map.AllowNegative && value <= 0)
                state.Validation.Text = "> 0 required";
            else
                state.Validation.Text = string.Empty;
            return parsed && (state.Map.AllowNegative || value > 0);
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            using (PropertyPaneSettingsForm form = new PropertyPaneSettingsForm(profile, settingsService))
            {
                form.ShowDialog(this);
                if (form.ProfileChanged)
                {
                    profile = settingsService.Load();
                    UpdateSourceIndicator();
                    RefreshFromActiveDocument(true);
                }
            }
        }

        private void SyncButton_Click(object sender, EventArgs e)
        {
            if (dirty && MessageBox.Show(this,
                    "Discard pending edits and reload the profile and shared Excel lists?",
                    "Cabin Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            profile = settingsService.Load();
            UpdateSourceIndicator();
            RefreshFromActiveDocument(true);

            if (profile.UseSharedWorkbook && !settingsService.SharedWorkbookLoaded)
                SetStatus(settingsService.SharedWorkbookStatus + ". Check the workbook path and PDM cache.", true);
        }

        private void UpdateSourceIndicator()
        {
            sourceLabel.Text = settingsService.SharedWorkbookStatus ?? "Lists: local profile";
            sourceLabel.ForeColor = settingsService.SharedWorkbookLoaded
                ? Color.FromArgb(0, 92, 153)
                : profile != null && profile.UseSharedWorkbook
                    ? Color.DarkRed
                    : Color.DimGray;
            toolTips.SetToolTip(sourceLabel, profile != null && profile.UseSharedWorkbook
                ? "The shared workbook is read from the local PDM cache. Use Sync after Get Latest."
                : "List values are loaded from this computer's local profile.");
        }

        private bool IsLoadedIdentity(IModelDoc2 model)
        {
            if (model == null)
                return false;
            return string.Equals(loadedPath ?? string.Empty, model.GetPathName() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(loadedTitle ?? string.Empty, model.GetTitle() ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(loadedConfiguration ?? string.Empty, CabinCustomPropertyStore.GetActiveConfigurationName(model), StringComparison.Ordinal);
        }

        private void MarkDirty()
        {
            dirty = true;
            SetStatus("Pending changes. Apply writes only changed fields and does not save the file.", false);
        }

        private void SetEditingEnabled(bool enabled)
        {
            foreach (FieldEditorState state in fieldEditors.Values)
            {
                bool manualDrawingNumber =
                    state.Field.AutomaticValue == "DrawingNumber" &&
                    profile.AllowDrawingNumberOverride;
                bool readOnly =
                    (state.Field.ReadOnly ||
                     state.Field.ControlType == PropertyPaneControlType.ReadOnly) &&
                    !manualDrawingNumber;
                state.Editor.Enabled = enabled && !readOnly;
                state.Scope.Enabled = enabled && !readOnly && state.Scope.Items.Count > 1;
                state.ClearButton.Enabled = enabled && !readOnly;
            }

            foreach (DimensionEditorState state in dimensionEditors.Values)
            {
                bool available = state.Editor.Enabled;
                state.Editor.Enabled = enabled && available;
                state.Scope.Enabled = enabled && available;
            }
        }

        private void SetStatus(string text, bool error)
        {
            statusLabel.Text = text;
            statusLabel.BackColor = error ? Color.MistyRose : Color.FromArgb(245, 249, 252);
            statusLabel.ForeColor = error ? Color.DarkRed : Color.FromArgb(35, 45, 55);
        }

        private string CurrentValue(string id)
        {
            FieldEditorState state;
            return fieldEditors.TryGetValue(id, out state) ? GetEditorText(state.Editor).Trim() : string.Empty;
        }

        private static string GetEditorText(Control editor)
        {
            PropertyDatePicker date = editor as PropertyDatePicker;
            if (date != null)
                return date.PropertyText;
            ComboBox combo = editor as ComboBox;
            return combo != null ? combo.Text ?? string.Empty : editor.Text ?? string.Empty;
        }

        private static void SetEditorText(Control editor, string value)
        {
            PropertyDatePicker date = editor as PropertyDatePicker;
            if (date != null)
            {
                date.SetPropertyText(value);
                return;
            }
            ComboBox combo = editor as ComboBox;
            if (combo != null) combo.Text = value ?? string.Empty;
            else editor.Text = value ?? string.Empty;
        }

        private static string ScopeLabel(PropertyPaneScope scope)
        {
            switch (scope)
            {
                case PropertyPaneScope.Document: return "Document";
                case PropertyPaneScope.ActiveConfiguration: return "Active config";
                case PropertyPaneScope.SelectedConfigurations: return "Selected...";
                case PropertyPaneScope.AllConfigurations: return "All configs";
                default: return scope.ToString();
            }
        }

        private static PropertyPaneDocumentKind GetDocumentKind(IModelDoc2 model)
        {
            if (model == null)
                throw new InvalidOperationException("No SOLIDWORKS document is active.");
            if (model.GetType() == (int)swDocumentTypes_e.swDocPART) return PropertyPaneDocumentKind.Part;
            if (model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) return PropertyPaneDocumentKind.Assembly;
            if (model.GetType() == (int)swDocumentTypes_e.swDocDRAWING) return PropertyPaneDocumentKind.Drawing;
            throw new InvalidOperationException("Unsupported SOLIDWORKS document type.");
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<blank>" : value;
        }

        private sealed class FieldEditorState
        {
            public PropertyPaneField Field;
            public Label Caption;
            public Control Editor;
            public ComboBox Scope;
            public Button ClearButton;
            public string Baseline;
            public bool ClearRequested;
            public List<string> SelectedConfigurations;
        }

        private sealed class DimensionEditorState
        {
            public DimensionMap Map;
            public TextBox Editor;
            public ComboBox Scope;
            public Label Validation;
            public string Baseline;
            public bool Dirty;
            public List<string> SelectedConfigurations;
        }

        private sealed class PropertyDatePicker : DateTimePicker
        {
            private const string DisplayFormat = "dd.MM.yyyy";
            private const string BlankFormat = " ";
            private bool hasValue;
            private bool settingValue;
            private string unparsedValue = string.Empty;

            public event EventHandler PropertyValueChanged;

            public PropertyDatePicker()
            {
                Format = DateTimePickerFormat.Custom;
                CustomFormat = BlankFormat;
            }

            public string PropertyText
            {
                get
                {
                    return hasValue
                        ? Value.ToString(DisplayFormat, System.Globalization.CultureInfo.InvariantCulture)
                        : unparsedValue;
                }
            }

            public void SetPropertyText(string value)
            {
                string text = (value ?? string.Empty).Trim();
                DateTime parsed;
                bool parsedSuccessfully = TryParseDate(text, out parsed) &&
                    parsed >= MinDate && parsed <= MaxDate;

                settingValue = true;
                try
                {
                    if (parsedSuccessfully)
                    {
                        Value = parsed;
                        hasValue = true;
                        unparsedValue = string.Empty;
                        CustomFormat = DisplayFormat;
                    }
                    else
                    {
                        hasValue = false;
                        unparsedValue = text;
                        CustomFormat = BlankFormat;
                    }
                }
                finally
                {
                    settingValue = false;
                }
                Invalidate();
            }

            protected override void OnValueChanged(EventArgs eventargs)
            {
                base.OnValueChanged(eventargs);
                if (settingValue)
                    return;
                hasValue = true;
                unparsedValue = string.Empty;
                CustomFormat = DisplayFormat;
                RaisePropertyValueChanged();
            }

            protected override void OnCloseUp(EventArgs eventargs)
            {
                base.OnCloseUp(eventargs);
                if (!hasValue)
                {
                    hasValue = true;
                    unparsedValue = string.Empty;
                    CustomFormat = DisplayFormat;
                    RaisePropertyValueChanged();
                }
            }

            private void RaisePropertyValueChanged()
            {
                EventHandler handler = PropertyValueChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }

            private static bool TryParseDate(string value, out DateTime date)
            {
                string[] formats = { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
                return DateTime.TryParseExact(value, formats,
                           System.Globalization.CultureInfo.InvariantCulture,
                           System.Globalization.DateTimeStyles.None, out date) ||
                       DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture,
                           System.Globalization.DateTimeStyles.None, out date) ||
                       DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                           System.Globalization.DateTimeStyles.None, out date);
            }
        }
    }
}
