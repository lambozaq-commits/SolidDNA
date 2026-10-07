using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Modeless, selected-configuration assembly view. The component inventory is
    /// the union across configurations, so suppressed instances remain visible.
    /// Presence edits are staged and only written when Apply is pressed.
    /// </summary>
    internal sealed class AssemblyConfigurationMatrixForm : Form
    {
        private readonly ISldWorks swApp;
        private readonly IModelDoc2 model;
        private readonly IAssemblyDoc assembly;
        private readonly string documentPath;
        private readonly string documentTitle;
        private readonly DataGridView grid = new DataGridView();
        private readonly ComboBox configurationPicker = new ComboBox();
        private readonly Label documentLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Button applyButton = new Button();
        private readonly Button discardButton = new Button();
        private readonly List<string> configurations = new List<string>();
        private readonly Dictionary<string, ComponentInfo> inventory = new Dictionary<string, ComponentInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> attemptedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, bool>> pending = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> pendingReferences = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private bool loading;
        private bool rendering;
        private bool hasRealDesignTable;

        private const int InstanceColumn = 0;
        private const int ReferencedConfigurationColumn = 1;
        private const int PresenceColumn = 2;
        private const int ActionsColumn = 3;

        private sealed class ComponentInfo
        {
            public string Key;
            public string Instance;
            public string Path;
            public Dictionary<string, string> ReferencedConfigurations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> SuppressionStates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<string> AvailableConfigurations = new List<string>();
        }

        private sealed class SuppressionEdit
        {
            public string ComponentKey;
            public string Configuration;
            public bool Suppressed;
            public string Reference;
        }

        public AssemblyConfigurationMatrixForm(ISldWorks application, IModelDoc2 assemblyModel)
        {
            swApp = application;
            model = assemblyModel;
            assembly = assemblyModel as IAssemblyDoc;
            documentPath = SafePath(assemblyModel);
            documentTitle = SafeTitle(assemblyModel);

            Text = "Assembly Configuration Matrix";
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(710, 430);
            Size = new Size(800, 650);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(244, 246, 248);
            PlaceBesideActiveScreen();
            BuildLayout();
            FormClosing += Matrix_FormClosing;
            LoadInventory();
        }

        public bool BelongsTo(IModelDoc2 candidate)
        {
            if (candidate == null) return false;
            string candidatePath = SafePath(candidate);
            return !string.IsNullOrWhiteSpace(documentPath)
                ? string.Equals(documentPath, candidatePath, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrWhiteSpace(candidatePath) && string.Equals(documentTitle, SafeTitle(candidate), StringComparison.OrdinalIgnoreCase);
        }

        private void PlaceBesideActiveScreen()
        {
            try
            {
                Rectangle area = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                Width = Math.Min(800, Math.Max(710, area.Width / 2));
                Height = Math.Min(760, Math.Max(430, area.Height - 60));
                Location = new Point(area.Right - Width - 12, area.Top + 30);
            }
            catch { StartPosition = FormStartPosition.CenterScreen; }
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel heading = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.White,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            documentLabel.AutoSize = true;
            documentLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            heading.Controls.Add(documentLabel, 0, 0);
            root.Controls.Add(heading, 0, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            actions.Controls.Add(new Label { Text = "Assembly configuration", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
            configurationPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            configurationPicker.Width = 190;
            configurationPicker.SelectedIndexChanged += ConfigurationPicker_SelectedIndexChanged;
            actions.Controls.Add(configurationPicker);
            Button refreshButton = CreateButton("Refresh", false);
            refreshButton.Click += delegate { if (ConfirmDiscard()) LoadInventory(); };
            actions.Controls.Add(refreshButton);
            root.Controls.Add(actions, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 237, 242);
            grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(217, 234, 249);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 40, 50);
            grid.RowTemplate.Height = 29;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellClick += Grid_CellClick;
            grid.SelectionChanged += Grid_SelectionChanged;

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ComponentInstance",
                HeaderText = "Component instance",
                Frozen = true,
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 260,
                MinimumWidth = 180,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "ReferencedConfiguration",
                HeaderText = "Part / subassembly configuration",
                FlatStyle = FlatStyle.Flat,
                FillWeight = 115,
                MinimumWidth = 155,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            DataGridViewComboBoxColumn presenceColumn = new DataGridViewComboBoxColumn
            {
                Name = "Presence",
                HeaderText = "State in selected assembly config",
                FillWeight = 75,
                MinimumWidth = 145,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            presenceColumn.Items.AddRange("Unsuppressed", "Suppressed");
            grid.Columns.Add(presenceColumn);
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "Actions",
                HeaderText = "Actions",
                Text = "Suppress...",
                UseColumnTextForButtonValue = true,
                FillWeight = 75,
                MinimumWidth = 130,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            root.Controls.Add(grid, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLabel.AutoSize = true;
            statusLabel.BorderStyle = BorderStyle.FixedSingle;
            statusLabel.BackColor = Color.White;
            statusLabel.Padding = new Padding(8, 6, 8, 6);
            statusLabel.Margin = new Padding(0, 0, 8, 0);
            footer.Controls.Add(statusLabel, 0, 0);
            FlowLayoutPanel bottomActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            discardButton.Text = "Discard";
            discardButton.AutoSize = true;
            discardButton.Click += delegate { DiscardPending(); };
            StyleButton(discardButton, false);
            applyButton.Text = "Apply";
            applyButton.AutoSize = true;
            applyButton.Click += delegate { ApplyPending(); };
            StyleButton(applyButton, true);
            bottomActions.Controls.Add(discardButton);
            bottomActions.Controls.Add(applyButton);
            footer.Controls.Add(bottomActions, 1, 0);
            footer.SetColumnSpan(statusLabel, 1);
            root.Controls.Add(footer, 0, 3);

            Controls.Add(root);
        }

        private static Button CreateButton(string text, bool primary)
        {
            Button button = new Button { Text = text, AutoSize = true };
            StyleButton(button, primary);
            return button;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 198, 206);
            button.Padding = new Padding(8, 3, 8, 3);
            button.Margin = new Padding(0, 0, 7, 5);
            if (primary)
            {
                button.BackColor = Color.FromArgb(0, 103, 184);
                button.ForeColor = Color.White;
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Color.FromArgb(35, 45, 55);
            }
        }

        private void LoadInventory()
        {
            if (loading || IsDisposed) return;
            loading = true;
            Cursor previousCursor = Cursor;
            string originalConfiguration = ActiveConfigurationName();
            List<string> scanErrors = new List<string>();
            try
            {
                ValidatePinnedDocument();
                configurations.Clear();
                configurations.AddRange(ReadConfigurationNames(model));
                if (configurations.Count == 0) throw new InvalidOperationException("No assembly configurations were found.");

                inventory.Clear();
                attemptedReferences.Clear();
                pending.Clear();
                pendingReferences.Clear();
                hasRealDesignTable = HasDesignTableFeature(model);
                foreach (string configuration in configurations)
                {
                    if (!Activate(configuration))
                    {
                        scanErrors.Add(configuration + ": could not activate");
                        continue;
                    }
                    foreach (IComponent2 component in ReadTopLevelComponents())
                    {
                        string instance = SafeComponentName(component);
                        if (string.IsNullOrWhiteSpace(instance)) continue;
                        string path = SafeComponentPath(component);
                        string key = MakeComponentKey(instance, path);
                        ComponentInfo info;
                        if (!inventory.TryGetValue(key, out info))
                        {
                            info = new ComponentInfo { Key = key, Instance = instance, Path = path };
                            inventory.Add(key, info);
                        }
                        string referenced = SafeReferencedConfiguration(component);
                        info.ReferencedConfigurations[configuration] = referenced;
                        if (!string.IsNullOrWhiteSpace(referenced) && !info.AvailableConfigurations.Contains(referenced))
                            info.AvailableConfigurations.Add(referenced);
                        if (attemptedReferences.Add(key)) PopulateAvailableConfigurations(info, component);
                        info.SuppressionStates[configuration] = SafeSuppression(component);
                    }
                }

                if (!string.IsNullOrWhiteSpace(originalConfiguration) && !Activate(originalConfiguration))
                    throw new InvalidOperationException("The original assembly configuration could not be restored after scanning.");

                configurationPicker.BeginUpdate();
                try
                {
                    configurationPicker.Items.Clear();
                    foreach (string configuration in configurations) configurationPicker.Items.Add(configuration);
                    configurationPicker.SelectedItem = configurations.Contains(originalConfiguration) ? originalConfiguration : configurations[0];
                }
                finally { configurationPicker.EndUpdate(); }

                ShowCurrentConfigurationRows();
                documentLabel.Text = AssemblyHeading();
                SetStatus(scanErrors.Count == 0
                    ? inventory.Count + " component instances"
                    : "Inventory incomplete: " + string.Join("; ", scanErrors.ToArray()), scanErrors.Count != 0);
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalConfiguration) && !string.Equals(ActiveConfigurationName(), originalConfiguration, StringComparison.Ordinal))
                {
                    if (!Activate(originalConfiguration)) SetStatus("Refresh ended before the original configuration could be restored. Check SOLIDWORKS' active configuration.", true);
                }
                Cursor = previousCursor;
                loading = false;
            }
        }

        private void ShowCurrentConfigurationRows()
        {
            string configuration = configurationPicker.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(configuration)) return;
            rendering = true;
            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear();
                List<ComponentInfo> ordered = new List<ComponentInfo>(inventory.Values);
                ordered.Sort(delegate(ComponentInfo a, ComponentInfo b) { return string.Compare(a.Instance, b.Instance, StringComparison.OrdinalIgnoreCase); });
                foreach (ComponentInfo info in ordered)
                {
                    string reference;
                    if (!info.ReferencedConfigurations.TryGetValue(configuration, out reference)) reference = null;
                    string stagedReference;
                    bool referenceStaged = TryGetPendingReference(info.Key, configuration, out stagedReference);
                    if (referenceStaged) reference = stagedReference;
                    int suppression;
                    if (!info.SuppressionStates.TryGetValue(configuration, out suppression)) suppression = -1;
                    bool staged;
                    bool stagedValue = TryGetPending(info.Key, configuration, out staged);
                    string state = staged ? (stagedValue ? "Suppressed" : "Unsuppressed") : StateLabel(suppression);
                    int row = grid.Rows.Add();
                    DataGridViewRow gridRow = grid.Rows[row];
                    gridRow.Tag = info;
                    gridRow.Cells[InstanceColumn].Value = info.Instance;
                    DataGridViewComboBoxCell referenceCell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                    foreach (string available in info.AvailableConfigurations) referenceCell.Items.Add(available);
                    if (!string.IsNullOrWhiteSpace(reference) && !referenceCell.Items.Contains(reference)) referenceCell.Items.Add(reference);
                    referenceCell.Value = string.IsNullOrWhiteSpace(reference) ? null : reference;
                    referenceCell.ReadOnly = referenceCell.Items.Count == 0 || hasRealDesignTable;
                    gridRow.Cells[ReferencedConfigurationColumn] = referenceCell;
                    gridRow.Cells[PresenceColumn].Value = state;
                    gridRow.Cells[ActionsColumn].Value = "Suppress...";
                    gridRow.Cells[InstanceColumn].ToolTipText = info.Path;
                    gridRow.Cells[ReferencedConfigurationColumn].ToolTipText = referenceCell.Items.Count == 0
                        ? "Could not read the component document's configurations. Resolve the component and refresh."
                        : "Choose the referenced configuration for this assembly configuration.";
                    gridRow.Cells[PresenceColumn].ToolTipText = hasRealDesignTable
                        ? "A real design-table feature is present. Presence edits are disabled here until the table binding can be verified."
                        : "Choose Unsuppressed or Suppressed to stage a change for this assembly configuration.";
                    if (hasRealDesignTable) gridRow.Cells[PresenceColumn].ReadOnly = true;
                    if (staged)
                    {
                        gridRow.Cells[PresenceColumn].Style.BackColor = Color.FromArgb(255, 245, 204);
                        gridRow.Cells[PresenceColumn].ToolTipText += " Pending change; Apply to write it.";
                    }
                    if (referenceStaged) referenceCell.Style.BackColor = Color.FromArgb(255, 245, 204);
                }
            }
            finally { grid.ResumeLayout(); rendering = false; }
            documentLabel.Text = AssemblyHeading();
            grid.ClearSelection();
        }

        private void ConfigurationPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading) return;
            string name = configurationPicker.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                ValidatePinnedDocument();
                if (!Activate(name)) throw new InvalidOperationException("SOLIDWORKS could not activate configuration '" + name + "'.");
                ShowCurrentConfigurationRows();
                SetStatus(inventory.Count + " component instances", false);
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell != null &&
                (grid.CurrentCell.ColumnIndex == PresenceColumn || grid.CurrentCell.ColumnIndex == ReferencedConfigurationColumn))
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || rendering || e.RowIndex < 0 || (e.ColumnIndex != PresenceColumn && e.ColumnIndex != ReferencedConfigurationColumn)) return;
            ComponentInfo info = grid.Rows[e.RowIndex].Tag as ComponentInfo;
            string configuration = configurationPicker.SelectedItem as string;
            if (info == null || string.IsNullOrWhiteSpace(configuration)) return;
            if (e.ColumnIndex == ReferencedConfigurationColumn)
            {
                string reference = Convert.ToString(grid.Rows[e.RowIndex].Cells[ReferencedConfigurationColumn].Value);
                if (!info.AvailableConfigurations.Contains(reference)) return;
                StageReference(info.Key, configuration, reference);
                grid.Rows[e.RowIndex].Cells[ReferencedConfigurationColumn].Style.BackColor = Color.FromArgb(255, 245, 204);
                SetStatus(inventory.Count + " component instances", false);
                return;
            }
            string value = Convert.ToString(grid.Rows[e.RowIndex].Cells[PresenceColumn].Value);
            if (value != "Unsuppressed" && value != "Suppressed") return;
            if (hasRealDesignTable)
            {
                SetStatus("This assembly has an Excel design-table feature. Presence editing is blocked until the exact table binding and update lifecycle are implemented; no model change was made.", true);
                ShowCurrentConfigurationRows();
                return;
            }
            Stage(info.Key, configuration, value == "Suppressed");
            grid.Rows[e.RowIndex].Cells[PresenceColumn].Style.BackColor = Color.FromArgb(255, 245, 204);
            SetStatus(inventory.Count + " component instances", false);
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ActionsColumn) return;
            if (grid.Rows[e.RowIndex].Tag is ComponentInfo)
            {
                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                ShowSuppressionActions();
            }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (loading || rendering || grid.SelectedRows.Count == 0) return;
            ComponentInfo info = grid.SelectedRows[0].Tag as ComponentInfo;
            if (info == null) return;
            try
            {
                string configuration = configurationPicker.SelectedItem as string;
                int suppression;
                if (configuration == null || !info.SuppressionStates.TryGetValue(configuration, out suppression) || suppression == (int)swComponentSuppressionState_e.swComponentSuppressed)
                {
                    return;
                }
                IComponent2 component = FindLiveComponent(info);
                if (component == null)
                {
                    return;
                }
                component.Select4(false, null, false);
            }
            catch (Exception ex) { SetStatus("Selection highlight failed: " + ex.Message, true); }
        }

        private void ShowSuppressionActions()
        {
            ComponentInfo info = SelectedComponent();
            if (info == null) return;
            using (Form dialog = new Form())
            {
                dialog.Text = "Suppress " + info.Instance;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(310, 105);
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                Button specify = CreateButton("Specify...", false);
                Button elsewhere = CreateButton("Suppress elsewhere", false);
                specify.SetBounds(12, 25, 110, 42);
                elsewhere.SetBounds(132, 25, 165, 42);
                specify.Click += delegate { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
                elsewhere.Click += delegate { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                dialog.Controls.Add(specify);
                dialog.Controls.Add(elsewhere);
                DialogResult action = dialog.ShowDialog(this);
                if (action == DialogResult.Yes) SpecifyConfigurationsToSuppress();
                else if (action == DialogResult.No) StagePresentHereAndSuppressOthers();
            }
        }

        private void StagePresentHereAndSuppressOthers()
        {
            ComponentInfo info = SelectedComponent();
            string current = configurationPicker.SelectedItem as string;
            if (info == null || string.IsNullOrWhiteSpace(current)) { SetStatus("Select a component row first.", true); return; }
            if (hasRealDesignTable) { SetStatus("Presence edits are blocked for this design-table assembly until source binding is implemented.", true); return; }
            Stage(info.Key, current, false);
            foreach (string configuration in configurations)
                if (!string.Equals(configuration, current, StringComparison.OrdinalIgnoreCase)) Stage(info.Key, configuration, true);
            ShowCurrentConfigurationRows();
            Reselect(info.Key);
            SetStatus(inventory.Count + " component instances", false);
        }

        private void SpecifyConfigurationsToSuppress()
        {
            ComponentInfo info = SelectedComponent();
            if (info == null) { SetStatus("Select a component row first.", true); return; }
            if (hasRealDesignTable) { SetStatus("Presence edits are blocked for this design-table assembly until source binding is implemented.", true); return; }

            List<string> checkedNames = new List<string>();
            foreach (string configuration in configurations)
                if (IsSuppressedInViewOrPending(info, configuration)) checkedNames.Add(configuration);
            using (ConfigurationSelectionDialog dialog = new ConfigurationSelectionDialog(
                configurations, checkedNames, "Specify configurations to suppress"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                HashSet<string> selected = new HashSet<string>(dialog.SelectedConfigurations, StringComparer.OrdinalIgnoreCase);
                foreach (string configuration in configurations)
                    Stage(info.Key, configuration, selected.Contains(configuration));
                ShowCurrentConfigurationRows();
                Reselect(info.Key);
                SetStatus(inventory.Count + " component instances", false);
            }
        }

        private void ApplyPending()
        {
            if (pending.Count == 0 && pendingReferences.Count == 0) { SetStatus(inventory.Count + " component instances", false); return; }
            try
            {
                ValidatePinnedDocument();
                if (hasRealDesignTable)
                {
                    SetStatus("Apply is blocked because this assembly has a real design table and its presence bindings have not been verified. Pending values were kept.", true);
                    return;
                }
                string writeBlock = CabinCustomPropertyStore.GetWriteBlockReason(model);
                if (!string.IsNullOrWhiteSpace(writeBlock))
                {
                    SetStatus(writeBlock + " Pending values were kept; check out or make a writable test copy before applying.", true);
                    return;
                }

                List<SuppressionEdit> edits = FlattenPending();
                string original = ActiveConfigurationName();
                int applied = 0, unchanged = 0, failed = 0;
                List<string> failures = new List<string>();
                string currentConfig = null;
                try
                {
                    foreach (SuppressionEdit edit in edits)
                    {
                        if (!string.Equals(currentConfig, edit.Configuration, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!Activate(edit.Configuration))
                            {
                                failed++;
                                failures.Add(edit.Configuration + ": configuration could not be activated");
                                currentConfig = null;
                                continue;
                            }
                            currentConfig = edit.Configuration;
                        }
                        ComponentInfo info;
                        IComponent2 component;
                        if (!inventory.TryGetValue(edit.ComponentKey, out info) || (component = FindLiveComponent(info)) == null)
                        {
                            failed++;
                            failures.Add((info == null ? edit.ComponentKey : info.Instance) + " / " + edit.Configuration + ": instance not found");
                            continue;
                        }
                        int expected = edit.Suppressed
                            ? (int)swComponentSuppressionState_e.swComponentSuppressed
                            : (int)swComponentSuppressionState_e.swComponentResolved;
                        int before = SafeSuppression(component);
                        if (before == expected) { unchanged++; RemovePending(edit.ComponentKey, edit.Configuration); continue; }
                        try
                        {
                            InvokeSetSuppression(component, expected);
                            try { model.EditRebuild3(); } catch { }
                            IComponent2 verify = FindLiveComponent(info);
                            int actual = verify == null ? -1 : SafeSuppression(verify);
                            bool matches = edit.Suppressed
                                ? actual == (int)swComponentSuppressionState_e.swComponentSuppressed
                                : actual != (int)swComponentSuppressionState_e.swComponentSuppressed && actual >= 0;
                            if (matches)
                            {
                                info.SuppressionStates[edit.Configuration] = actual;
                                applied++;
                                RemovePending(edit.ComponentKey, edit.Configuration);
                            }
                            else
                            {
                                failed++;
                                failures.Add(info.Instance + " / " + edit.Configuration + ": requested " + (edit.Suppressed ? "Suppressed" : "Unsuppressed") + ", read back " + (StateLabel(actual) ?? "unavailable"));
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failures.Add(info.Instance + " / " + edit.Configuration + ": " + ex.Message);
                        }
                    }
                    foreach (SuppressionEdit edit in FlattenPendingReferences())
                    {
                        if (!string.Equals(currentConfig, edit.Configuration, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!Activate(edit.Configuration))
                            {
                                failed++;
                                failures.Add(edit.Configuration + ": configuration could not be activated");
                                currentConfig = null;
                                continue;
                            }
                            currentConfig = edit.Configuration;
                        }
                        ComponentInfo info;
                        IComponent2 component;
                        if (!inventory.TryGetValue(edit.ComponentKey, out info) || (component = FindLiveComponent(info)) == null)
                        {
                            failed++;
                            failures.Add(edit.ComponentKey + " / " + edit.Configuration + ": instance not found");
                            continue;
                        }
                        if (SafeSuppression(component) == (int)swComponentSuppressionState_e.swComponentSuppressed)
                        {
                            failed++;
                            failures.Add(info.Instance + " / " + edit.Configuration + ": unsuppress before changing its referenced configuration");
                            continue;
                        }
                        string before = SafeReferencedConfiguration(component);
                        if (string.Equals(before, edit.Reference, StringComparison.OrdinalIgnoreCase))
                        {
                            unchanged++;
                            RemovePendingReference(edit.ComponentKey, edit.Configuration);
                            continue;
                        }
                        try
                        {
                            component.ReferencedConfiguration = edit.Reference;
                            try { model.EditRebuild3(); } catch { }
                            IComponent2 verify = FindLiveComponent(info);
                            string actual = verify == null ? null : SafeReferencedConfiguration(verify);
                            if (string.Equals(actual, edit.Reference, StringComparison.OrdinalIgnoreCase))
                            {
                                info.ReferencedConfigurations[edit.Configuration] = actual;
                                applied++;
                                RemovePendingReference(edit.ComponentKey, edit.Configuration);
                            }
                            else
                            {
                                failed++;
                                failures.Add(info.Instance + " / " + edit.Configuration + ": referenced configuration did not read back as " + edit.Reference);
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failures.Add(info.Instance + " / " + edit.Configuration + ": " + ex.Message);
                        }
                    }
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(original) && !Activate(original))
                        failures.Add("Could not restore original configuration " + original + ".");
                }

                ShowCurrentConfigurationRows();
                string result = "Applied " + applied + ", unchanged " + unchanged + ", failed " + failed + ". Save the assembly when ready.";
                if (failures.Count > 0) result += " Details: " + string.Join("; ", failures.ToArray());
                SetStatus(failures.Count == 0 ? inventory.Count + " component instances" : result, failures.Count > 0 || failed > 0);
                MessageBox.Show(this, result, "Assembly Configuration Matrix", MessageBoxButtons.OK,
                    failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex) { SetStatus("Apply stopped safely: " + ex.Message + " Pending values were kept where not verified.", true); }
        }

        private List<SuppressionEdit> FlattenPending()
        {
            List<SuppressionEdit> result = new List<SuppressionEdit>();
            foreach (KeyValuePair<string, Dictionary<string, bool>> component in pending)
                foreach (KeyValuePair<string, bool> state in component.Value)
                    result.Add(new SuppressionEdit { ComponentKey = component.Key, Configuration = state.Key, Suppressed = state.Value });
            result.Sort(delegate(SuppressionEdit a, SuppressionEdit b)
            {
                int configOrder = configurations.IndexOf(a.Configuration).CompareTo(configurations.IndexOf(b.Configuration));
                return configOrder != 0 ? configOrder : string.Compare(a.ComponentKey, b.ComponentKey, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private List<SuppressionEdit> FlattenPendingReferences()
        {
            List<SuppressionEdit> result = new List<SuppressionEdit>();
            foreach (KeyValuePair<string, Dictionary<string, string>> component in pendingReferences)
                foreach (KeyValuePair<string, string> reference in component.Value)
                    result.Add(new SuppressionEdit { ComponentKey = component.Key, Configuration = reference.Key, Reference = reference.Value });
            result.Sort(delegate(SuppressionEdit a, SuppressionEdit b)
            {
                int order = configurations.IndexOf(a.Configuration).CompareTo(configurations.IndexOf(b.Configuration));
                return order != 0 ? order : string.Compare(a.ComponentKey, b.ComponentKey, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private void DiscardPending()
        {
            pending.Clear();
            pendingReferences.Clear();
            ShowCurrentConfigurationRows();
            SetStatus(inventory.Count + " component instances", false);
        }

        private void Matrix_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (pending.Count == 0 && pendingReferences.Count == 0) return;
            DialogResult answer = MessageBox.Show(
                this,
                "There are pending matrix edits that have not been applied. Close and discard them?",
                "Assembly Configuration Matrix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) e.Cancel = true;
            else { pending.Clear(); pendingReferences.Clear(); }
        }

        private bool ConfirmDiscard()
        {
            return pending.Count == 0 && pendingReferences.Count == 0 || MessageBox.Show(this,
                "Refresh and discard pending matrix edits?", "Assembly Configuration Matrix",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void StageReference(string key, string configuration, string reference)
        {
            Dictionary<string, string> edits;
            if (!pendingReferences.TryGetValue(key, out edits))
            {
                edits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                pendingReferences.Add(key, edits);
            }
            ComponentInfo info;
            string actual;
            if (inventory.TryGetValue(key, out info) && info.ReferencedConfigurations.TryGetValue(configuration, out actual) &&
                string.Equals(actual, reference, StringComparison.OrdinalIgnoreCase))
            {
                edits.Remove(configuration);
                if (edits.Count == 0) pendingReferences.Remove(key);
            }
            else edits[configuration] = reference;
        }

        private bool TryGetPendingReference(string key, string configuration, out string reference)
        {
            reference = null;
            Dictionary<string, string> edits;
            return pendingReferences.TryGetValue(key, out edits) && edits.TryGetValue(configuration, out reference);
        }

        private void RemovePendingReference(string key, string configuration)
        {
            Dictionary<string, string> edits;
            if (!pendingReferences.TryGetValue(key, out edits)) return;
            edits.Remove(configuration);
            if (edits.Count == 0) pendingReferences.Remove(key);
        }

        private void Stage(string componentKey, string configuration, bool suppressed)
        {
            Dictionary<string, bool> states;
            if (!pending.TryGetValue(componentKey, out states))
            {
                states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                pending.Add(componentKey, states);
            }
            int actual;
            ComponentInfo info;
            if (inventory.TryGetValue(componentKey, out info) && info.SuppressionStates.TryGetValue(configuration, out actual))
            {
                bool actualSuppressed = actual == (int)swComponentSuppressionState_e.swComponentSuppressed;
                if (actual >= 0 && actualSuppressed == suppressed) { states.Remove(configuration); if (states.Count == 0) pending.Remove(componentKey); return; }
            }
            states[configuration] = suppressed;
        }

        private bool TryGetPending(string componentKey, string configuration, out bool suppressed)
        {
            suppressed = false;
            Dictionary<string, bool> states;
            return pending.TryGetValue(componentKey, out states) && states.TryGetValue(configuration, out suppressed);
        }

        private void RemovePending(string componentKey, string configuration)
        {
            Dictionary<string, bool> states;
            if (!pending.TryGetValue(componentKey, out states)) return;
            states.Remove(configuration);
            if (states.Count == 0) pending.Remove(componentKey);
        }

        private bool IsSuppressedInViewOrPending(ComponentInfo info, string configuration)
        {
            bool pendingValue;
            if (TryGetPending(info.Key, configuration, out pendingValue)) return pendingValue;
            int state;
            return info.SuppressionStates.TryGetValue(configuration, out state) && state == (int)swComponentSuppressionState_e.swComponentSuppressed;
        }

        private ComponentInfo SelectedComponent()
        {
            return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as ComponentInfo;
        }

        private void Reselect(string key)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                ComponentInfo info = row.Tag as ComponentInfo;
                if (info != null && string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    break;
                }
            }
        }

        private bool Activate(string name)
        {
            try { return model.ShowConfiguration2(name); }
            catch { return false; }
        }

        private IComponent2 FindLiveComponent(ComponentInfo info)
        {
            if (info == null) return null;
            foreach (IComponent2 component in ReadTopLevelComponents())
                if (string.Equals(MakeComponentKey(SafeComponentName(component), SafeComponentPath(component)), info.Key, StringComparison.OrdinalIgnoreCase)) return component;
            return null;
        }

        private void PopulateAvailableConfigurations(ComponentInfo info, IComponent2 component)
        {
            IModelDoc2 referencedModel = null;
            bool opened = false;
            string openedTitle = null;
            try { referencedModel = component.GetModelDoc2() as IModelDoc2; } catch { }
            if (referencedModel == null && !string.IsNullOrWhiteSpace(info.Path))
            {
                try { referencedModel = swApp.GetOpenDocumentByName(info.Path) as IModelDoc2; } catch { }
                if (referencedModel == null && File.Exists(info.Path))
                {
                    int errors = 0, warnings = 0;
                    int type = Path.GetExtension(info.Path).Equals(".sldasm", StringComparison.OrdinalIgnoreCase)
                        ? (int)swDocumentTypes_e.swDocASSEMBLY : (int)swDocumentTypes_e.swDocPART;
                    try
                    {
                        referencedModel = swApp.OpenDoc6(info.Path, type,
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly,
                            string.Empty, ref errors, ref warnings) as IModelDoc2;
                        opened = referencedModel != null;
                        if (opened) openedTitle = referencedModel.GetTitle();
                    }
                    catch { }
                }
            }
            try
            {
                if (referencedModel != null)
                    foreach (string name in ReadConfigurationNames(referencedModel))
                        if (!info.AvailableConfigurations.Contains(name)) info.AvailableConfigurations.Add(name);
            }
            finally
            {
                if (opened)
                {
                    try { swApp.CloseDoc(openedTitle); } catch { }
                    try { int error = 0; swApp.ActivateDoc2(documentTitle, false, ref error); } catch { }
                }
            }
        }

        private IEnumerable<IComponent2> ReadTopLevelComponents()
        {
            object[] values = null;
            try { values = assembly.GetComponents(true) as object[]; } catch { }
            if (values == null) yield break;
            foreach (object value in values)
            {
                IComponent2 component = value as IComponent2;
                if (component != null) yield return component;
            }
        }

        private static void InvokeSetSuppression(IComponent2 component, int state)
        {
            if (component == null) throw new InvalidOperationException("The component instance is no longer available.");
            object raw = component;
            try
            {
                raw.GetType().InvokeMember("SetSuppression2", BindingFlags.InvokeMethod, null, raw, new object[] { state });
            }
            catch (MissingMethodException)
            {
                raw.GetType().InvokeMember("SetSuppression", BindingFlags.InvokeMethod, null, raw, new object[] { state });
            }
        }

        private void ValidatePinnedDocument()
        {
            IModelDoc2 active = null;
            try { active = swApp.ActiveDoc as IModelDoc2; } catch { }
            if (model == null || assembly == null || active == null) throw new InvalidOperationException("The assembly this matrix belongs to is no longer active. Close and reopen it for the active assembly.");
            string activePath = SafePath(active);
            bool same = !string.IsNullOrWhiteSpace(documentPath)
                ? string.Equals(documentPath, activePath, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrWhiteSpace(activePath) && string.Equals(documentTitle, SafeTitle(active), StringComparison.OrdinalIgnoreCase);
            if (!same) throw new InvalidOperationException("The assembly this matrix belongs to is no longer active. Close and reopen it for the active assembly.");
        }

        private static bool HasDesignTableFeature(IModelDoc2 document)
        {
            try
            {
                IFeature feature = document.FirstFeature() as IFeature;
                int guard = 0;
                while (feature != null && guard++ < 5000)
                {
                    string type = feature.GetTypeName2() ?? string.Empty;
                    if (type.IndexOf("DesignTable", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    feature = feature.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return false;
        }

        private string AssemblyHeading()
        {
            string description = string.Empty;
            try
            {
                PropertyPaneReadValue value = PropertyPanePropertyService.ReadDescriptionEffective(
                    model, PropertyPaneScope.ActiveConfiguration, null);
                description = value == null ? string.Empty : value.ResolvedValue;
            }
            catch { }
            return SafeTitle(model) + (string.IsNullOrWhiteSpace(description) ? string.Empty : "  ·  " + description);
        }

        private static List<string> ReadConfigurationNames(IModelDoc2 document)
        {
            List<string> result = new List<string>();
            object[] values = null;
            try { values = document.GetConfigurationNames() as object[]; } catch { }
            if (values == null) return result;
            foreach (object value in values)
            {
                string name = Convert.ToString(value);
                if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name)) result.Add(name);
            }
            return result;
        }

        private string ActiveConfigurationName()
        {
            try
            {
                ConfigurationManager manager = model.ConfigurationManager as ConfigurationManager;
                IConfiguration configuration = manager == null ? null : manager.ActiveConfiguration as IConfiguration;
                return configuration == null ? string.Empty : configuration.Name ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static int SafeSuppression(IComponent2 component)
        {
            try { return component.GetSuppression(); } catch { return -1; }
        }

        private static string SafeReferencedConfiguration(IComponent2 component)
        {
            try { return component.ReferencedConfiguration ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeComponentName(IComponent2 component)
        {
            try { return component.Name2 ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeComponentPath(IComponent2 component)
        {
            try { return component.GetPathName() ?? string.Empty; } catch { return string.Empty; }
        }

        private static string MakeComponentKey(string instance, string path)
        {
            return (instance ?? string.Empty) + "|" + (path ?? string.Empty);
        }

        private static string SafePath(IModelDoc2 document)
        {
            try { return document.GetPathName() ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeTitle(IModelDoc2 document)
        {
            try { return document.GetTitle() ?? "Assembly"; } catch { return "Assembly"; }
        }

        private static string StateLabel(int suppression)
        {
            if (suppression < 0) return null;
            return suppression == (int)swComponentSuppressionState_e.swComponentSuppressed ? "Suppressed" : "Unsuppressed";
        }

        private void SetStatus(string text, bool error)
        {
            statusLabel.Text = text ?? string.Empty;
            statusLabel.ForeColor = error ? Color.DarkRed : Color.FromArgb(35, 45, 55);
        }
    }
}
