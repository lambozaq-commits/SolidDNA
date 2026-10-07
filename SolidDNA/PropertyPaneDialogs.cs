using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SolidDNA
{
    internal sealed class ConfigurationSelectionDialog : Form
    {
        private readonly DataGridView list = new DataGridView();
        public List<string> SelectedConfigurations { get; private set; }

        public ConfigurationSelectionDialog(IEnumerable<string> configurations, IEnumerable<string> selected, string title = "Select configurations")
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(380, 430);

            HashSet<string> current = new HashSet<string>(selected ?? new string[0], StringComparer.OrdinalIgnoreCase);
            list.Dock = DockStyle.Fill;
            list.AllowUserToAddRows = false;
            list.AllowUserToDeleteRows = false;
            list.RowHeadersVisible = false;
            list.MultiSelect = true;
            list.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            list.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            list.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            list.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Configuration", ReadOnly = true });
            foreach (string name in configurations ?? new string[0])
                list.Rows.Add(current.Contains(name), name);
            list.CurrentCellDirtyStateChanged += delegate { if (list.IsCurrentCellDirty) list.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            list.ClearSelection();

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6)
            };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button toggle = new Button { Text = "Check / uncheck all", AutoSize = true };
            toggle.Click += delegate
            {
                GridCheckBehavior.ToggleSelectedThenAll(list,
                    row => Convert.ToBoolean(row.Cells[0].Value ?? false),
                    (row, check) => row.Cells[0].Value = check);
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(toggle);
            Controls.Add(list);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += delegate
            {
                list.EndEdit();
                SelectedConfigurations = list.Rows.Cast<DataGridViewRow>()
                    .Where(row => Convert.ToBoolean(row.Cells[0].Value ?? false))
                    .Select(row => Convert.ToString(row.Cells[1].Value)).ToList();
            };
        }
    }

    internal sealed class PropertyPaneSettingsForm : Form
    {
        private readonly PropertyPaneSettingsService service;
        private PropertyPaneProfile profile;
        private readonly ComboBox documentKind = new ComboBox();
        private readonly ListBox groups = new ListBox();
        private readonly DataGridView fields = Grid();
        private readonly ComboBox listName = new ComboBox();
        private readonly TextBox listValues = new TextBox();
        private readonly DataGridView mappings = Grid();
        private readonly DataGridView dimensions = Grid();
        private readonly CheckBox allowOverride = new CheckBox();
        private readonly CheckBox useSharedWorkbook = new CheckBox();
        private readonly TextBox workbookPath = new TextBox();
        private readonly Label workbookStatus = new Label();
        private readonly Button browseWorkbookButton = new Button();
        private readonly Button checkWorkbookButton = new Button();
        private FlowLayoutPanel listActions;

        public bool ProfileChanged { get; private set; }

        public PropertyPaneSettingsForm(PropertyPaneProfile current, PropertyPaneSettingsService settingsService)
        {
            profile = current;
            service = settingsService;
            Text = "Property pane settings";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 560);
            Size = new Size(980, 700);

            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildLayoutTab());
            tabs.TabPages.Add(BuildSharedWorkbookTab());
            tabs.TabPages.Add(BuildListsTab());
            tabs.TabPages.Add(BuildMappingTab());
            tabs.TabPages.Add(BuildDimensionTab());

            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.RightToLeft
            };
            Button close = Button("Close", delegate { DialogResult = DialogResult.Cancel; Close(); });
            Button save = Button("Save", SaveAndClose);
            Button import = Button("Import", ImportProfile);
            Button export = Button("Export", ExportProfile);
            Button reset = Button("Reset", ResetProfile);
            footer.Controls.Add(close);
            footer.Controls.Add(save);
            footer.Controls.Add(export);
            footer.Controls.Add(import);
            footer.Controls.Add(reset);

            Controls.Add(tabs);
            Controls.Add(footer);
            LoadProfile();
        }

        private TabPage BuildLayoutTab()
        {
            TabPage page = new TabPage("Layout");
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230 };

            documentKind.DropDownStyle = ComboBoxStyle.DropDownList;
            documentKind.Dock = DockStyle.Top;
            documentKind.DataSource = Enum.GetValues(typeof(PropertyPaneDocumentKind));
            documentKind.SelectedIndexChanged += delegate { BindGroups(); };

            groups.Dock = DockStyle.Fill;
            groups.DisplayMember = "Label";
            groups.SelectedIndexChanged += delegate { BindFields(); };
            FlowLayoutPanel groupButtons = Toolbar(
                Button("Add", AddGroup), Button("Remove", RemoveGroup),
                Button("Up", delegate { MoveGroup(-1); }), Button("Down", delegate { MoveGroup(1); }));
            split.Panel1.Controls.Add(groups);
            split.Panel1.Controls.Add(groupButtons);
            split.Panel1.Controls.Add(documentKind);

            fields.Dock = DockStyle.Fill;
            fields.AutoGenerateColumns = true;
            FlowLayoutPanel fieldButtons = Toolbar(
                Button("Add", AddField), Button("Remove", RemoveField),
                Button("Up", delegate { MoveField(-1); }), Button("Down", delegate { MoveField(1); }));
            split.Panel2.Controls.Add(fields);
            split.Panel2.Controls.Add(fieldButtons);
            page.Controls.Add(split);
            return page;
        }

        private TabPage BuildListsTab()
        {
            TabPage page = new TabPage("Lists");
            listName.Dock = DockStyle.Top;
            listName.DropDownStyle = ComboBoxStyle.DropDownList;
            listName.SelectedIndexChanged += delegate { BindListValues(); };
            listValues.Dock = DockStyle.Fill;
            listValues.Multiline = true;
            listValues.ScrollBars = ScrollBars.Both;
            listValues.AcceptsReturn = true;
            Label hint = new Label { Text = "One value per line. When shared Excel is enabled, edit the workbook through PDM.", Dock = DockStyle.Top, Height = 24 };
            listActions = Toolbar(Button("Update", UpdateList), Button("Add list", AddList), Button("Remove", RemoveList));
            page.Controls.Add(listValues);
            page.Controls.Add(listActions);
            page.Controls.Add(hint);
            page.Controls.Add(listName);
            return page;
        }

        private TabPage BuildSharedWorkbookTab()
        {
            TabPage page = new TabPage("Shared Excel");
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(16)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            useSharedWorkbook.Text = "Use shared Excel lists and drawing map";
            useSharedWorkbook.AutoSize = true;
            useSharedWorkbook.CheckedChanged += delegate { UpdateWorkbookMode(); };
            layout.Controls.Add(useSharedWorkbook, 0, 0);
            layout.SetColumnSpan(useSharedWorkbook, 3);

            workbookPath.Dock = DockStyle.Fill;
            browseWorkbookButton.Text = "Browse";
            browseWorkbookButton.AutoSize = true;
            browseWorkbookButton.Click += BrowseWorkbook;
            layout.Controls.Add(new Label { Text = "Workbook", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(workbookPath, 1, 1);
            layout.Controls.Add(browseWorkbookButton, 2, 1);

            checkWorkbookButton.Text = "Check";
            checkWorkbookButton.AutoSize = true;
            checkWorkbookButton.Click += CheckWorkbook;
            workbookStatus.AutoSize = true;
            workbookStatus.Anchor = AnchorStyles.Left;
            layout.Controls.Add(checkWorkbookButton, 1, 2);
            layout.Controls.Add(workbookStatus, 2, 2);

            Label explanation = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Margin = new Padding(0, 16, 0, 0),
                Text = "Cabin Tools reads the locally cached PDM workbook without changing it. " +
                       "Use normal PDM checkout to edit the workbook, Get Latest on other computers, then click Sync in the task pane. " +
                       "The loader recognizes list columns by header and drawing mappings by Cabin type description, " +
                       "Cabin type defined, Layout type, and Drw Number."
            };
            layout.Controls.Add(explanation, 0, 3);
            layout.SetColumnSpan(explanation, 3);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildMappingTab()
        {
            TabPage page = new TabPage("Drawing map");
            mappings.Dock = DockStyle.Fill;
            mappings.AutoGenerateColumns = true;
            allowOverride.Text = "Allow manual drawing-number override";
            allowOverride.Dock = DockStyle.Top;
            page.Controls.Add(mappings);
            page.Controls.Add(allowOverride);
            return page;
        }

        private TabPage BuildDimensionTab()
        {
            TabPage page = new TabPage("Dimensions");
            dimensions.Dock = DockStyle.Fill;
            dimensions.AutoGenerateColumns = true;
            page.Controls.Add(dimensions);
            return page;
        }

        private void LoadProfile()
        {
            allowOverride.Checked = profile.AllowDrawingNumberOverride;
            useSharedWorkbook.Checked = profile.UseSharedWorkbook;
            workbookPath.Text = profile.SharedWorkbookPath ?? string.Empty;
            mappings.DataSource = new BindingList<DrawingNumberMap>(profile.DrawingNumberMappings);
            dimensions.DataSource = new BindingList<DimensionMap>(profile.Dimensions);
            documentKind.SelectedItem = PropertyPaneDocumentKind.Part;
            BindGroups();
            BindLists();
            UpdateWorkbookMode();
        }

        private void BindGroups()
        {
            PropertyPaneLayout layout = CurrentLayout();
            groups.DataSource = null;
            groups.DataSource = layout == null ? null : new BindingList<PropertyPaneGroup>(layout.Groups);
        }

        private void BindFields()
        {
            PropertyPaneGroup group = groups.SelectedItem as PropertyPaneGroup;
            fields.DataSource = group == null ? null : new BindingList<PropertyPaneField>(group.Fields);
            HideCollectionColumns(fields);
        }

        private void BindLists()
        {
            listName.DataSource = null;
            listName.DataSource = profile.Lists;
            listName.DisplayMember = "Name";
            BindListValues();
        }

        private void BindListValues()
        {
            PropertyPaneNamedList selected = listName.SelectedItem as PropertyPaneNamedList;
            listValues.Text = selected == null ? string.Empty : string.Join(Environment.NewLine, selected.Values.ToArray());
        }

        private PropertyPaneLayout CurrentLayout()
        {
            if (!(documentKind.SelectedItem is PropertyPaneDocumentKind))
                return null;
            PropertyPaneDocumentKind kind = (PropertyPaneDocumentKind)documentKind.SelectedItem;
            return profile.Layouts.FirstOrDefault(l => l.DocumentKind == kind);
        }

        private void AddGroup(object sender, EventArgs e)
        {
            PropertyPaneLayout layout = CurrentLayout();
            if (layout == null)
                return;
            string label = TextEntryDialog.Ask(this, "Add group", "Group name:");
            if (string.IsNullOrWhiteSpace(label))
                return;
            layout.Groups.Add(new PropertyPaneGroup { Id = Slug(label), Label = label.Trim(), Order = layout.Groups.Count });
            BindGroups();
            groups.SelectedIndex = layout.Groups.Count - 1;
        }

        private void RemoveGroup(object sender, EventArgs e)
        {
            PropertyPaneLayout layout = CurrentLayout();
            PropertyPaneGroup group = groups.SelectedItem as PropertyPaneGroup;
            if (layout == null || group == null)
                return;
            if (MessageBox.Show(this, "Remove group '" + group.Label + "'?", "Cabin Tools",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            layout.Groups.Remove(group);
            Renumber(layout.Groups, g => g.Order, (g, i) => g.Order = i);
            BindGroups();
        }

        private void MoveGroup(int direction)
        {
            PropertyPaneLayout layout = CurrentLayout();
            PropertyPaneGroup item = groups.SelectedItem as PropertyPaneGroup;
            if (layout == null || item == null)
                return;
            int index = layout.Groups.IndexOf(item);
            int target = index + direction;
            if (target < 0 || target >= layout.Groups.Count)
                return;
            layout.Groups.RemoveAt(index);
            layout.Groups.Insert(target, item);
            Renumber(layout.Groups, g => g.Order, (g, i) => g.Order = i);
            BindGroups();
            groups.SelectedIndex = target;
        }

        private void AddField(object sender, EventArgs e)
        {
            PropertyPaneGroup group = groups.SelectedItem as PropertyPaneGroup;
            if (group == null)
                return;
            using (CatalogFieldDialog dialog = new CatalogFieldDialog(profile.PropertyCatalog))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                group.Fields.Add(new PropertyPaneField
                {
                    Id = Slug(dialog.PropertyName),
                    Label = dialog.LabelText,
                    PropertyName = dialog.PropertyName,
                    Order = group.Fields.Count,
                    ControlType = PropertyPaneControlType.Text,
                    DefaultScope = PropertyPaneScope.Document,
                    Visible = true,
                    AllowedScopes = new List<PropertyPaneScope> { PropertyPaneScope.Document,
                        PropertyPaneScope.ActiveConfiguration, PropertyPaneScope.SelectedConfigurations,
                        PropertyPaneScope.AllConfigurations }
                });
            }
            BindFields();
        }

        private void RemoveField(object sender, EventArgs e)
        {
            PropertyPaneGroup group = groups.SelectedItem as PropertyPaneGroup;
            PropertyPaneField field = fields.CurrentRow == null ? null : fields.CurrentRow.DataBoundItem as PropertyPaneField;
            if (group == null || field == null)
                return;
            group.Fields.Remove(field);
            Renumber(group.Fields, f => f.Order, (f, i) => f.Order = i);
            BindFields();
        }

        private void MoveField(int direction)
        {
            PropertyPaneGroup group = groups.SelectedItem as PropertyPaneGroup;
            PropertyPaneField item = fields.CurrentRow == null ? null : fields.CurrentRow.DataBoundItem as PropertyPaneField;
            if (group == null || item == null)
                return;
            int index = group.Fields.IndexOf(item);
            int target = index + direction;
            if (target < 0 || target >= group.Fields.Count)
                return;
            group.Fields.RemoveAt(index);
            group.Fields.Insert(target, item);
            Renumber(group.Fields, f => f.Order, (f, i) => f.Order = i);
            BindFields();
            fields.Rows[target].Selected = true;
        }

        private void UpdateList(object sender, EventArgs e)
        {
            if (useSharedWorkbook.Checked)
                return;
            PropertyPaneNamedList selected = listName.SelectedItem as PropertyPaneNamedList;
            if (selected == null)
                return;
            selected.Values = listValues.Lines.Select(v => v.Trim()).Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void AddList(object sender, EventArgs e)
        {
            string name = TextEntryDialog.Ask(this, "Add list", "List name:");
            if (string.IsNullOrWhiteSpace(name) || profile.Lists.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
                return;
            profile.Lists.Add(new PropertyPaneNamedList { Name = name.Trim() });
            BindLists();
            listName.SelectedIndex = profile.Lists.Count - 1;
        }

        private void RemoveList(object sender, EventArgs e)
        {
            PropertyPaneNamedList selected = listName.SelectedItem as PropertyPaneNamedList;
            if (selected == null)
                return;
            profile.Lists.Remove(selected);
            BindLists();
        }

        private void SaveAndClose(object sender, EventArgs e)
        {
            UpdateList(null, EventArgs.Empty);
            fields.EndEdit();
            mappings.EndEdit();
            dimensions.EndEdit();
            profile.AllowDrawingNumberOverride = allowOverride.Checked;
            profile.UseSharedWorkbook = useSharedWorkbook.Checked;
            profile.SharedWorkbookPath = workbookPath.Text.Trim();
            service.Save(profile);
            ProfileChanged = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BrowseWorkbook(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    workbookPath.Text = dialog.FileName;
                    workbookStatus.Text = string.Empty;
                }
            }
        }

        private void CheckWorkbook(object sender, EventArgs e)
        {
            try
            {
                PropertyPaneWorkbookLoadResult result = service.CheckSharedWorkbook(workbookPath.Text);
                workbookStatus.ForeColor = Color.DarkGreen;
                workbookStatus.Text = result.ListCount + " lists, " + result.MappingCount + " mappings";
            }
            catch (Exception ex)
            {
                workbookStatus.ForeColor = Color.DarkRed;
                workbookStatus.Text = ex.Message;
            }
        }

        private void UpdateWorkbookMode()
        {
            bool shared = useSharedWorkbook.Checked;
            workbookPath.Enabled = shared;
            browseWorkbookButton.Enabled = shared;
            checkWorkbookButton.Enabled = shared;
            listValues.ReadOnly = shared;
            if (listActions != null)
                listActions.Enabled = !shared;
            mappings.ReadOnly = shared;
            mappings.AllowUserToAddRows = !shared;
            mappings.AllowUserToDeleteRows = !shared;
            if (!shared)
            {
                workbookStatus.ForeColor = SystemColors.ControlText;
                workbookStatus.Text = "Local profile lists are active.";
            }
        }

        private void ImportProfile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Cabin Tools profile (*.xml)|*.xml|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try { profile = service.Import(dialog.FileName); LoadProfile(); ProfileChanged = true; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExportProfile(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Cabin Tools profile (*.xml)|*.xml", FileName = "PropertyPaneProfile.xml" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                service.Export(profile, dialog.FileName);
            }
        }

        private void ResetProfile(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Reset the property-pane profile to built-in defaults? A backup will be created.",
                    "Cabin Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            profile = service.Reset();
            LoadProfile();
            ProfileChanged = true;
        }

        private static DataGridView Grid()
        {
            return new DataGridView
            {
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = SystemColors.Window,
                RowHeadersVisible = false
            };
        }

        private static void HideCollectionColumns(DataGridView grid)
        {
            if (grid.Columns.Contains("AllowedScopes"))
                grid.Columns["AllowedScopes"].Visible = false;
        }

        private static FlowLayoutPanel Toolbar(params Button[] buttons)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(3) };
            panel.Controls.AddRange(buttons);
            return panel;
        }

        private static Button Button(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true };
            button.Click += click;
            return button;
        }

        private static void Renumber<T>(IList<T> items, Func<T, int> read, Action<T, int> write)
        {
            for (int i = 0; i < items.Count; i++)
                write(items[i], i);
        }

        private static string Slug(string value)
        {
            return new string((value ?? "field").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        }
    }

    internal sealed class CatalogFieldDialog : Form
    {
        private readonly ComboBox property = new ComboBox();
        private readonly TextBox label = new TextBox();
        public string PropertyName { get { return Convert.ToString(property.Text).Trim(); } }
        public string LabelText { get { return label.Text.Trim(); } }

        public CatalogFieldDialog(IEnumerable<string> catalog)
        {
            Text = "Add field";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Size = new Size(420, 180);
            property.Dock = DockStyle.Top;
            property.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            property.AutoCompleteSource = AutoCompleteSource.ListItems;
            property.Items.AddRange((catalog ?? new string[0]).Cast<object>().ToArray());
            label.Dock = DockStyle.Top;
            property.TextChanged += delegate { if (string.IsNullOrWhiteSpace(label.Text)) label.Text = property.Text; };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            Controls.Add(label); Controls.Add(new Label { Text = "Label", Dock = DockStyle.Top });
            Controls.Add(property); Controls.Add(new Label { Text = "Property", Dock = DockStyle.Top }); Controls.Add(buttons);
            AcceptButton = ok; CancelButton = cancel;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (DialogResult == DialogResult.OK && (string.IsNullOrWhiteSpace(PropertyName) || string.IsNullOrWhiteSpace(LabelText)))
                { e.Cancel = true; MessageBox.Show(this, "Property and label are required.", "Cabin Tools"); }
            };
        }
    }

    internal sealed class TextEntryDialog : Form
    {
        private readonly TextBox input = new TextBox();
        private TextEntryDialog(string title, string prompt)
        {
            Text = title; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            Size = new Size(390, 150); MinimizeBox = false; MaximizeBox = false;
            input.Dock = DockStyle.Top;
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            Controls.Add(input); Controls.Add(new Label { Text = prompt, Dock = DockStyle.Top, Height = 24 }); Controls.Add(buttons);
            AcceptButton = ok; CancelButton = cancel;
        }
        public static string Ask(IWin32Window owner, string title, string prompt)
        {
            using (TextEntryDialog dialog = new TextEntryDialog(title, prompt))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.input.Text : null;
        }
    }
}
