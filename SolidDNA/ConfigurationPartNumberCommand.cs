using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Configuration property editor.
    ///
    /// Edits:
    /// - Configuration name.
    /// - Configuration-specific custom property Description, equivalent to design-table $PRP@Description.
    /// - SOLIDWORKS BOM part number field: IConfiguration.AlternateName + UseAlternateNameInBOM.
    /// - Selected configuration-specific custom properties, including Specification1.
    ///
    /// Empty cells are not written. Use Clear Column to intentionally blank a value.
    /// </summary>
    internal static class ConfigurationPartNumberCommand
    {
        private const string ApplyColumnName = "__Apply";
        private const string ConfigurationColumnName = "__Configuration";
        private const string DescriptionColumnName = "__Description";
        private const string BomPartNumberColumnName = "__BomPartNumber";
        private const string DerivedColumnName = "__Derived";
        private const string StatusColumnName = "__Status";

        private enum RowTargetScope
        {
            Cancelled,
            CheckedRows,
            AllRows
        }

        public static void ShowConfigurationPartNumberForm()
        {
            try
            {
                IModelDoc2 modelDoc = CabinCustomPropertyStore.GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage("Open a part or assembly before editing configuration properties.", MessageBoxIcon.Warning);
                    return;
                }

                int documentType = modelDoc.GetType();
                if (documentType != (int)swDocumentTypes_e.swDocPART && documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    ShowMessage("Configuration property editing works only for parts and assemblies.", MessageBoxIcon.Warning);
                    return;
                }

                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(modelDoc);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(writeBlockReason, MessageBoxIcon.Warning);
                    return;
                }

                using (ConfigurationPropertyEditorForm form = new ConfigurationPropertyEditorForm(modelDoc))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Cabin Tools could not open the configuration property editor.\r\n\r\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        internal static string GetConfigurationBomPartNumber(IConfiguration configuration)
        {
            if (configuration == null)
                return string.Empty;

            try
            {
                if (configuration.UseAlternateNameInBOM)
                    return configuration.AlternateName == null ? string.Empty : configuration.AlternateName.Trim();
            }
            catch
            {
            }

            return string.Empty;
        }

        internal static string GetConfigurationDescription(IConfiguration configuration)
        {
            if (configuration == null)
                return string.Empty;
            try { return configuration.Description == null ? string.Empty : configuration.Description.Trim(); }
            catch { return string.Empty; }
        }

        internal static bool IsReservedGridPropertyName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            return
                string.Equals(propertyName, "Description", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, "BOM Part Number", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, "Configuration", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, "Derived", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, "Status", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsDerivedConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
                return false;
            try
            {
                object parent = configuration.GetType().InvokeMember("GetParent", System.Reflection.BindingFlags.InvokeMethod, null, configuration, null);
                return parent != null;
            }
            catch
            {
                return false;
            }
        }

        internal static ICustomPropertyManager GetConfigurationPropertyManager(IModelDoc2 modelDoc, string configurationName)
        {
            if (modelDoc == null || string.IsNullOrWhiteSpace(configurationName))
                return null;

            try
            {
                IModelDocExtension extension = modelDoc.Extension;
                if (extension == null)
                    return null;
                return extension.CustomPropertyManager[configurationName];
            }
            catch
            {
                return null;
            }
        }

        internal static string ReadTextProperty(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            string rawValue;
            string resolvedValue;
            bool wasResolved;
            bool linked;

            try
            {
                int result = propertyManager.Get6(propertyName, false, out rawValue, out resolvedValue, out wasResolved, out linked);
                if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                    return string.Empty;
                if (!string.IsNullOrWhiteSpace(resolvedValue))
                    return resolvedValue.Trim();
                if (!string.IsNullOrWhiteSpace(rawValue))
                    return rawValue.Trim();
            }
            catch
            {
            }

            return string.Empty;
        }

        internal static List<string> GetPropertyNames(ICustomPropertyManager propertyManager)
        {
            List<string> names = new List<string>();
            if (propertyManager == null)
                return names;

            try
            {
                object namesObject = propertyManager.GetNames();
                Array namesArray = namesObject as Array;
                if (namesArray == null)
                    return names;

                foreach (object nameObject in namesArray)
                {
                    string name = nameObject == null ? string.Empty : Convert.ToString(nameObject).Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !ContainsIgnoreCase(names, name))
                        names.Add(name);
                }
            }
            catch
            {
            }
            return names;
        }

        internal static void SetTextProperty(ICustomPropertyManager propertyManager, string propertyName, string value)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            propertyManager.Add3(
                propertyName.Trim(),
                (int)swCustomInfoType_e.swCustomInfoText,
                value ?? string.Empty,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(message, "Cabin Tools - Configuration Property Editor", MessageBoxButtons.OK, icon);
        }

        internal static bool ContainsIgnoreCase(IList<string> values, string value)
        {
            if (values == null || value == null)
                return false;
            foreach (string current in values)
            {
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal sealed class ConfigurationInfo
        {
            public string Name = string.Empty;
            public string PendingName = string.Empty;
            public string OriginalName = string.Empty;
            public IConfiguration Configuration;
            public ICustomPropertyManager PropertyManager;
            public string Description = string.Empty;
            public string BomPartNumber = string.Empty;
            public bool IsDerived;
            public Dictionary<string, string> CustomProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class ConfigurationPropertyEditorForm : Form
        {
            private readonly IModelDoc2 modelDoc;
            private readonly List<ConfigurationInfo> configurations = new List<ConfigurationInfo>();
            private readonly List<string> allCustomPropertyNames = new List<string>();
            private readonly List<string> visibleCustomPropertyNames = new List<string>();
            private readonly CutListProfilePropertyCommand.PropertyDropdownSettings dropdownSettings;

            private DataGridView grid;
            private Label statusLabel;

            public ConfigurationPropertyEditorForm(IModelDoc2 modelDoc)
            {
                this.modelDoc = modelDoc;
                this.dropdownSettings = CutListProfilePropertyCommand.PropertyDropdownSettings.Load();

                Text = "Cabin Tools - Configuration Property Editor";
                Width = 1320;
                Height = 780;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadConfigurationData();
                BuildGrid();
            }

            private void BuildLayout()
            {
                MinimumSize = new Size(980, 560);

                Button editButton = new Button();
                editButton.Text = "Edit...";
                editButton.Left = 12;
                editButton.Top = 14;
                editButton.Width = 105;
                editButton.Click += delegate { ShowEditMenu(); };
                Controls.Add(editButton);

                Button numberButton = new Button();
                numberButton.Text = "Number...";
                numberButton.Left = 125;
                numberButton.Top = 14;
                numberButton.Width = 105;
                numberButton.Click += delegate { ShowNumberDialog(); };
                Controls.Add(numberButton);

                Button orderButton = new Button();
                orderButton.Text = "Order...";
                orderButton.Left = 238;
                orderButton.Top = 14;
                orderButton.Width = 100;
                orderButton.Click += delegate { ShowOrderDialog(); };
                Controls.Add(orderButton);

                Button toggleCheckButton = new Button();
                toggleCheckButton.Text = "Check / uncheck all";
                toggleCheckButton.Left = 346;
                toggleCheckButton.Top = 14;
                toggleCheckButton.Width = 145;
                toggleCheckButton.Click += delegate { ToggleAllApply(); };
                Controls.Add(toggleCheckButton);

                grid = new DataGridView();
                grid.Left = 12;
                grid.Top = 54;
                grid.Width = 1280;
                grid.Height = 558;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.MultiSelect = true;
                grid.RowHeadersVisible = false;
                grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                grid.EditMode = DataGridViewEditMode.EditOnEnter;
                grid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e)
                {
                    if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name != ApplyColumnName)
                        SetRowApply(e.RowIndex, true);
                };
                grid.CurrentCellDirtyStateChanged += delegate
                {
                    if (grid.IsCurrentCellDirty)
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                grid.DataError += delegate { };
                Controls.Add(grid);

                statusLabel = new Label();
                statusLabel.Left = 12;
                statusLabel.Top = 620;
                statusLabel.Width = 900;
                statusLabel.Height = 34;
                statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;
                Controls.Add(statusLabel);

                Button applyButton = new Button();
                applyButton.Text = "Apply";
                applyButton.Left = 1182;
                applyButton.Top = 620;
                applyButton.Width = 110;
                applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                applyButton.Click += delegate { ApplyRowsSmart(); };
                Controls.Add(applyButton);
            }

            private void LoadConfigurationData()
            {
                configurations.Clear();
                allCustomPropertyNames.Clear();

                object namesObject = modelDoc.GetConfigurationNames();
                Array namesArray = namesObject as Array;
                if (namesArray == null)
                    return;

                foreach (object nameObject in namesArray)
                {
                    string configurationName = nameObject == null ? string.Empty : Convert.ToString(nameObject).Trim();
                    if (string.IsNullOrWhiteSpace(configurationName))
                        continue;

                    IConfiguration configuration = null;
                    try { configuration = modelDoc.GetConfigurationByName(configurationName) as IConfiguration; }
                    catch { configuration = null; }

                    ICustomPropertyManager propertyManager = GetConfigurationPropertyManager(modelDoc, configurationName);
                    ConfigurationInfo info = new ConfigurationInfo();
                    info.Name = configurationName;
                    info.PendingName = configurationName;
                    info.OriginalName = configurationName;
                    info.Configuration = configuration;
                    info.PropertyManager = propertyManager;
                    info.Description = ReadTextProperty(propertyManager, "Description");
                    info.BomPartNumber = GetConfigurationBomPartNumber(configuration);
                    info.IsDerived = IsDerivedConfiguration(configuration);

                    foreach (string propertyName in GetPropertyNames(propertyManager))
                    {
                        if (IsReservedGridPropertyName(propertyName))
                            continue;

                        if (!ContainsIgnoreCase(allCustomPropertyNames, propertyName))
                            allCustomPropertyNames.Add(propertyName);
                        info.CustomProperties[propertyName] = ReadTextProperty(propertyManager, propertyName);
                    }

                    configurations.Add(info);
                }

                for (int i = visibleCustomPropertyNames.Count - 1; i >= 0; i--)
                {
                    if (IsReservedGridPropertyName(visibleCustomPropertyNames[i]))
                        visibleCustomPropertyNames.RemoveAt(i);
                }

                EnsureCustomPropertyNameAvailable("Specification1");

                if (visibleCustomPropertyNames.Count == 0)
                {
                    foreach (string name in dropdownSettings.GetVisibleColumns("Configuration"))
                    {
                        if (!IsReservedGridPropertyName(name) && ContainsIgnoreCase(allCustomPropertyNames, name) && !ContainsIgnoreCase(visibleCustomPropertyNames, name))
                            visibleCustomPropertyNames.Add(name);
                    }
                }

                // Specification1 is a project-standard configuration custom property.
                // Keep it visible by default so existing values are easy to inspect and edit.
                if (ContainsIgnoreCase(allCustomPropertyNames, "Specification1") && !ContainsIgnoreCase(visibleCustomPropertyNames, "Specification1"))
                    visibleCustomPropertyNames.Add("Specification1");
            }

            private void EnsureCustomPropertyNameAvailable(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName) || IsReservedGridPropertyName(propertyName))
                    return;

                if (!ContainsIgnoreCase(allCustomPropertyNames, propertyName))
                    allCustomPropertyNames.Add(propertyName);

                foreach (ConfigurationInfo info in configurations)
                {
                    if (info == null || info.CustomProperties.ContainsKey(propertyName))
                        continue;

                    info.CustomProperties[propertyName] = ReadTextProperty(info.PropertyManager, propertyName);
                }
            }

            private void BuildGrid()
            {
                grid.Columns.Clear();
                grid.Rows.Clear();

                DataGridViewCheckBoxColumn applyColumn = new DataGridViewCheckBoxColumn();
                applyColumn.Name = ApplyColumnName;
                applyColumn.HeaderText = "Apply";
                applyColumn.Width = 55;
                grid.Columns.Add(applyColumn);

                AddTextColumn(ConfigurationColumnName, "Configuration", 250, false);
                AddTextColumn(DescriptionColumnName, "Description ($PRP)", 230, false);
                AddTextColumn(BomPartNumberColumnName, "BOM Part Number", 155, false);

                foreach (string propertyName in visibleCustomPropertyNames)
                {
                    if (IsReservedGridPropertyName(propertyName) || grid.Columns.Contains(propertyName))
                        continue;
                    AddTextColumn(propertyName, propertyName, 160, false);
                }

                AddTextColumn(StatusColumnName, "Status", 240, true);

                foreach (ConfigurationInfo info in configurations)
                {
                    int rowIndex = grid.Rows.Add();
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = info;
                    row.Cells[ApplyColumnName].Value = false;
                    row.Cells[ConfigurationColumnName].Value = string.IsNullOrWhiteSpace(info.PendingName) ? info.Name : info.PendingName;
                    row.Cells[DescriptionColumnName].Value = info.Description;
                    row.Cells[BomPartNumberColumnName].Value = info.BomPartNumber;
                    row.Cells[StatusColumnName].Value = "Ready";

                    if (info.IsDerived)
                    {
                        row.Cells[ConfigurationColumnName].Style.Padding = new Padding(18, 0, 0, 0);
                        row.Cells[ConfigurationColumnName].Style.ForeColor = Color.DimGray;
                    }

                    foreach (string propertyName in visibleCustomPropertyNames)
                    {
                        string value;
                        info.CustomProperties.TryGetValue(propertyName, out value);
                        row.Cells[propertyName].Value = value ?? string.Empty;
                    }
                }

                statusLabel.Text = configurations.Count + " configuration(s).";
            }

            private void ShowEditMenu()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Edit";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(420, 118);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Button columns = new Button();
                    columns.Text = "Columns";
                    columns.Left = 18;
                    columns.Top = 34;
                    columns.Width = 116;
                    columns.Height = 34;
                    columns.Click += delegate { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
                    dialog.Controls.Add(columns);

                    Button bulk = new Button();
                    bulk.Text = "Bulk values";
                    bulk.Left = 151;
                    bulk.Top = 34;
                    bulk.Width = 116;
                    bulk.Height = 34;
                    bulk.Click += delegate { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                    dialog.Controls.Add(bulk);

                    Button clear = new Button();
                    clear.Text = "Clear property";
                    clear.Left = 284;
                    clear.Top = 34;
                    clear.Width = 116;
                    clear.Height = 34;
                    clear.Click += delegate { dialog.DialogResult = DialogResult.Retry; dialog.Close(); };
                    dialog.Controls.Add(clear);

                    DialogResult result = dialog.ShowDialog(this);
                    if (result == DialogResult.Yes)
                        ShowColumnsMenu();
                    else if (result == DialogResult.No)
                        ShowBulkEditDialog();
                    else if (result == DialogResult.Retry)
                        ShowClearPropertyDialog();
                }
            }

            private void ShowClearPropertyDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Clear Property";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(390, 160);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    ComboBox propertyBox = new ComboBox();
                    propertyBox.Left = 24;
                    propertyBox.Top = 26;
                    propertyBox.Width = 340;
                    propertyBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    propertyBox.Items.Add("Description");
                    propertyBox.Items.Add("BOM Part Number");
                    foreach (string name in visibleCustomPropertyNames)
                    {
                        if (!propertyBox.Items.Contains(name))
                            propertyBox.Items.Add(name);
                    }
                    if (propertyBox.Items.Count > 0)
                        propertyBox.SelectedIndex = 0;
                    dialog.Controls.Add(propertyBox);

                    Button checkedButton = new Button();
                    checkedButton.Text = "Checked Only";
                    checkedButton.Left = 70;
                    checkedButton.Top = 88;
                    checkedButton.Width = 110;
                    checkedButton.Click += delegate
                    {
                        ClearPropertyRows(Convert.ToString(propertyBox.SelectedItem), false);
                        dialog.Close();
                    };
                    dialog.Controls.Add(checkedButton);

                    Button allButton = new Button();
                    allButton.Text = "All";
                    allButton.Left = 198;
                    allButton.Top = 88;
                    allButton.Width = 90;
                    allButton.Click += delegate
                    {
                        ClearPropertyRows(Convert.ToString(propertyBox.SelectedItem), true);
                        dialog.Close();
                    };
                    dialog.Controls.Add(allButton);
                }
            }

            private void ClearPropertyRows(string displayName, bool allRows)
            {
                string columnName = GetGridColumnNameFromDisplayName(displayName);
                if (string.IsNullOrWhiteSpace(displayName) || !grid.Columns.Contains(columnName))
                    return;

                int changed = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;

                    row.Cells[columnName].Value = string.Empty;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Ready to clear";
                    changed++;
                }

                statusLabel.Text = changed + " configuration(s) marked for clearing.";
            }

            private void ShowColumnsMenu()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Columns";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(330, 120);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Button choose = new Button();
                    choose.Text = "Choose columns";
                    choose.Left = 18;
                    choose.Top = 30;
                    choose.Width = 135;
                    choose.Height = 34;
                    choose.Click += delegate { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
                    dialog.Controls.Add(choose);

                    Button add = new Button();
                    add.Text = "Add property";
                    add.Left = 167;
                    add.Top = 30;
                    add.Width = 135;
                    add.Height = 34;
                    add.Click += delegate { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                    dialog.Controls.Add(add);

                    DialogResult result = dialog.ShowDialog(this);
                    if (result == DialogResult.Yes)
                        ChooseColumns();
                    else if (result == DialogResult.No)
                        AddNewPropertyColumn();
                }
            }

            private void ShowBulkEditDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Bulk Edit";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(460, 185);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Label propertyLabel = new Label();
                    propertyLabel.Text = "Property";
                    propertyLabel.Left = 24;
                    propertyLabel.Top = 18;
                    propertyLabel.Width = 180;
                    dialog.Controls.Add(propertyLabel);

                    ComboBox propertyBox = new ComboBox();
                    propertyBox.Left = 24;
                    propertyBox.Top = 40;
                    propertyBox.Width = 190;
                    propertyBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    propertyBox.Items.Add("Description");
                    propertyBox.Items.Add("BOM Part Number");
                    foreach (string name in visibleCustomPropertyNames)
                        if (!propertyBox.Items.Contains(name)) propertyBox.Items.Add(name);
                    if (propertyBox.Items.Count > 0) propertyBox.SelectedIndex = 0;
                    dialog.Controls.Add(propertyBox);

                    Label valueLabel = new Label();
                    valueLabel.Text = "Value";
                    valueLabel.Left = 232;
                    valueLabel.Top = 18;
                    valueLabel.Width = 180;
                    dialog.Controls.Add(valueLabel);

                    ComboBox valueBox = new ComboBox();
                    valueBox.Left = 232;
                    valueBox.Top = 40;
                    valueBox.Width = 200;
                    valueBox.DropDownStyle = ComboBoxStyle.DropDown;
                    dialog.Controls.Add(valueBox);

                    propertyBox.SelectedIndexChanged += delegate
                    {
                        valueBox.Items.Clear();
                        string property = Convert.ToString(propertyBox.SelectedItem);
                        foreach (string option in dropdownSettings.GetOptions(property))
                            valueBox.Items.Add(option);
                        valueBox.Text = string.Empty;
                    };
                    valueBox.Items.Clear();
                    foreach (string option in dropdownSettings.GetOptions(Convert.ToString(propertyBox.SelectedItem)))
                        valueBox.Items.Add(option);

                    Label scopeLabel = new Label();
                    scopeLabel.Text = "Apply to";
                    scopeLabel.Left = 24;
                    scopeLabel.Top = 86;
                    scopeLabel.Width = 80;
                    dialog.Controls.Add(scopeLabel);

                    ComboBox scopeBox = new ComboBox();
                    scopeBox.Left = 92;
                    scopeBox.Top = 81;
                    scopeBox.Width = 160;
                    scopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    scopeBox.Items.AddRange(new object[] { "Selected rows", "Checked rows", "All rows" });
                    scopeBox.SelectedIndex = GetSelectedGridRows().Count > 1 ? 0 : 1;
                    dialog.Controls.Add(scopeBox);

                    Button apply = new Button();
                    apply.Text = "Apply";
                    apply.Left = 262;
                    apply.Top = 126;
                    apply.Width = 80;
                    apply.DialogResult = DialogResult.OK;
                    dialog.Controls.Add(apply);

                    Button cancel = new Button();
                    cancel.Text = "Cancel";
                    cancel.Left = 352;
                    cancel.Top = 126;
                    cancel.Width = 80;
                    cancel.DialogResult = DialogResult.Cancel;
                    dialog.Controls.Add(cancel);

                    dialog.AcceptButton = apply;
                    dialog.CancelButton = cancel;

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    string displayName = Convert.ToString(propertyBox.SelectedItem);
                    string columnName = GetGridColumnNameFromDisplayName(displayName);
                    string value = valueBox.Text == null ? string.Empty : valueBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(value))
                        return;

                    List<DataGridViewRow> targetRows = GetRowsByScope(scopeBox.SelectedIndex);
                    if (targetRows.Count == 0)
                    {
                        MessageBox.Show("No rows are selected for the bulk edit.", "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    foreach (DataGridViewRow row in targetRows)
                    {
                        row.Cells[columnName].Value = value;
                        row.Cells[ApplyColumnName].Value = true;
                        row.Cells[StatusColumnName].Value = "Ready to apply";
                    }

                    dropdownSettings.LearnOption(displayName, value);
                    dropdownSettings.Save();
                }
            }

            private void ShowNumberDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - BOM Numbering";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(410, 220);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Label startLabel = new Label();
                    startLabel.Text = "Start";
                    startLabel.Left = 24;
                    startLabel.Top = 20;
                    startLabel.Width = 80;
                    dialog.Controls.Add(startLabel);

                    TextBox startBox = new TextBox();
                    startBox.Left = 24;
                    startBox.Top = 42;
                    startBox.Width = 120;
                    startBox.Text = "0001";
                    dialog.Controls.Add(startBox);

                    Button maxButton = new Button();
                    maxButton.Text = "Use max + 1";
                    maxButton.Left = 158;
                    maxButton.Top = 40;
                    maxButton.Width = 110;
                    maxButton.Click += delegate { startBox.Text = GetMaxPlusOneText(); };
                    dialog.Controls.Add(maxButton);

                    CheckBox overwriteBox = new CheckBox();
                    overwriteBox.Text = "Overwrite existing numbers";
                    overwriteBox.Left = 24;
                    overwriteBox.Top = 82;
                    overwriteBox.Width = 220;
                    dialog.Controls.Add(overwriteBox);

                    CheckBox includeDerivedBox = new CheckBox();
                    includeDerivedBox.Text = "Include derived configurations";
                    includeDerivedBox.Left = 24;
                    includeDerivedBox.Top = 108;
                    includeDerivedBox.Width = 240;
                    dialog.Controls.Add(includeDerivedBox);

                    Label scopeLabel = new Label();
                    scopeLabel.Text = "Number";
                    scopeLabel.Left = 24;
                    scopeLabel.Top = 142;
                    scopeLabel.Width = 70;
                    dialog.Controls.Add(scopeLabel);

                    ComboBox scopeBox = new ComboBox();
                    scopeBox.Left = 92;
                    scopeBox.Top = 137;
                    scopeBox.Width = 165;
                    scopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    scopeBox.Items.AddRange(new object[] { "Selected rows", "Checked rows", "All rows" });
                    scopeBox.SelectedIndex = GetSelectedGridRows().Count > 1 ? 0 : 1;
                    dialog.Controls.Add(scopeBox);

                    Button ok = new Button();
                    ok.Text = "Number";
                    ok.Left = 228;
                    ok.Top = 176;
                    ok.Width = 80;
                    ok.DialogResult = DialogResult.OK;
                    dialog.Controls.Add(ok);

                    Button cancel = new Button();
                    cancel.Text = "Cancel";
                    cancel.Left = 318;
                    cancel.Top = 176;
                    cancel.Width = 70;
                    cancel.DialogResult = DialogResult.Cancel;
                    dialog.Controls.Add(cancel);

                    dialog.AcceptButton = ok;
                    dialog.CancelButton = cancel;

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    List<DataGridViewRow> targetRows = GetRowsByScope(scopeBox.SelectedIndex);
                    FillBomNumbersForRows(targetRows, startBox.Text, overwriteBox.Checked, includeDerivedBox.Checked);
                }
            }

            private void ShowOrderDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Configuration Order";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(320, 120);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Button up = new Button();
                    up.Text = "Move up";
                    up.Left = 30;
                    up.Top = 32;
                    up.Width = 120;
                    up.Click += delegate { MoveSelectedRows(-1); dialog.Close(); };
                    dialog.Controls.Add(up);

                    Button down = new Button();
                    down.Text = "Move down";
                    down.Left = 168;
                    down.Top = 32;
                    down.Width = 120;
                    down.Click += delegate { MoveSelectedRows(1); dialog.Close(); };
                    dialog.Controls.Add(down);
                }
            }

            private List<DataGridViewRow> GetRowsByScope(int scopeIndex)
            {
                List<DataGridViewRow> result = new List<DataGridViewRow>();
                if (scopeIndex == 0)
                {
                    result = GetSelectedGridRows();
                }
                else if (scopeIndex == 1)
                {
                    foreach (DataGridViewRow row in grid.Rows)
                        if (!row.IsNewRow && IsRowChecked(row)) result.Add(row);
                }
                else
                {
                    foreach (DataGridViewRow row in grid.Rows)
                        if (!row.IsNewRow) result.Add(row);
                }
                return result;
            }

            private void FillBomNumbersForRows(List<DataGridViewRow> targetRows, string startText, bool overwriteExisting, bool includeDerived)
            {
                if (targetRows == null || targetRows.Count == 0)
                {
                    MessageBox.Show("No rows are selected for numbering.", "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string cleanStart = (startText ?? string.Empty).Trim();
                int current;
                if (string.IsNullOrWhiteSpace(cleanStart) || !int.TryParse(cleanStart, out current))
                {
                    MessageBox.Show("Start must contain digits only.", "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int width = cleanStart.Length;
                targetRows.Sort(delegate(DataGridViewRow a, DataGridViewRow b) { return a.Index.CompareTo(b.Index); });

                foreach (DataGridViewRow row in targetRows)
                {
                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info != null && info.IsDerived && !includeDerived)
                    {
                        row.Cells[StatusColumnName].Value = "Skipped - derived";
                        continue;
                    }

                    if (!overwriteExisting && !string.IsNullOrWhiteSpace(CellText(row, BomPartNumberColumnName)))
                        continue;

                    row.Cells[BomPartNumberColumnName].Value = current.ToString(new string('0', width));
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Ready to apply";
                    current++;
                }
            }

            private string GetMaxPlusOneText()
            {
                int max = 0;
                int width = 1;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    string text = CellText(row, BomPartNumberColumnName);
                    int number;
                    if (int.TryParse(text, out number))
                    {
                        if (number > max) max = number;
                        if (text.Length > width) width = text.Length;
                    }
                }
                return (max + 1).ToString(new string('0', width));
            }

            private void CaptureGridValuesToInfos()
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info == null) continue;
                    info.PendingName = CellText(row, ConfigurationColumnName);
                    info.Description = CellText(row, DescriptionColumnName);
                    info.BomPartNumber = CellText(row, BomPartNumberColumnName);
                    foreach (string propertyName in visibleCustomPropertyNames)
                        info.CustomProperties[propertyName] = CellText(row, propertyName);
                }
            }

            private void MoveSelectedRows(int direction)
            {
                List<DataGridViewRow> selected = GetSelectedGridRows();
                if (selected.Count == 0)
                    return;

                CaptureGridValuesToInfos();
                List<ConfigurationInfo> selectedInfos = new List<ConfigurationInfo>();
                foreach (DataGridViewRow row in selected)
                {
                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info != null && !selectedInfos.Contains(info)) selectedInfos.Add(info);
                }

                if (direction < 0)
                {
                    for (int i = 1; i < configurations.Count; i++)
                    {
                        if (selectedInfos.Contains(configurations[i]) && !selectedInfos.Contains(configurations[i - 1]))
                        {
                            ConfigurationInfo temp = configurations[i - 1];
                            configurations[i - 1] = configurations[i];
                            configurations[i] = temp;
                        }
                    }
                }
                else
                {
                    for (int i = configurations.Count - 2; i >= 0; i--)
                    {
                        if (selectedInfos.Contains(configurations[i]) && !selectedInfos.Contains(configurations[i + 1]))
                        {
                            ConfigurationInfo temp = configurations[i + 1];
                            configurations[i + 1] = configurations[i];
                            configurations[i] = temp;
                        }
                    }
                }

                BuildGrid();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info != null && selectedInfos.Contains(info)) row.Selected = true;
                }
                statusLabel.Text = "Editor order updated. Bulk numbering follows this order.";
            }

            private void AddTextColumn(string name, string header, int width, bool readOnly)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = name;
                column.HeaderText = header;
                column.Width = width;
                column.ReadOnly = readOnly;
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                grid.Columns.Add(column);
            }

            private void AddCheckColumn(string name, string header, int width, bool readOnly)
            {
                DataGridViewCheckBoxColumn column = new DataGridViewCheckBoxColumn();
                column.Name = name;
                column.HeaderText = header;
                column.Width = width;
                column.ReadOnly = readOnly;
                grid.Columns.Add(column);
            }
private string GetGridColumnNameFromDisplayName(string displayName)
            {
                if (string.Equals(displayName, "Description", StringComparison.OrdinalIgnoreCase))
                    return DescriptionColumnName;
                if (string.Equals(displayName, "BOM Part Number", StringComparison.OrdinalIgnoreCase))
                    return BomPartNumberColumnName;
                return displayName;
            }
private void ApplyRowsSmart()
            {
                RowTargetScope scope = ResolveRowTargetScope("Apply");
                if (scope == RowTargetScope.Cancelled)
                    return;
                ApplyRows(scope == RowTargetScope.AllRows);
            }
private RowTargetScope ResolveRowTargetScope(string operationName)
            {
                int totalRows = CountRealRows();
                if (totalRows == 0)
                    return RowTargetScope.Cancelled;

                int checkedRows = CountCheckedRows();

                // If every row is checked, Checked Only and All are equivalent.
                if (checkedRows == totalRows)
                    return RowTargetScope.CheckedRows;

                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - " + operationName;
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;
                    dialog.ClientSize = new Size(360, 142);

                    Label messageLabel = new Label();
                    messageLabel.Left = 14;
                    messageLabel.Top = 14;
                    messageLabel.Width = 330;
                    messageLabel.Height = 46;
                    messageLabel.Text = operationName + " will affect which configurations?";
                    dialog.Controls.Add(messageLabel);

                    RowTargetScope selectedScope = RowTargetScope.Cancelled;

                    Button allButton = new Button();
                    allButton.Text = "All";
                    allButton.Left = 18;
                    allButton.Top = 82;
                    allButton.Width = 96;
                    allButton.DialogResult = DialogResult.OK;
                    allButton.Click += delegate { selectedScope = RowTargetScope.AllRows; };
                    dialog.Controls.Add(allButton);

                    Button checkedButton = new Button();
                    checkedButton.Text = "Checked Only";
                    checkedButton.Left = 126;
                    checkedButton.Top = 82;
                    checkedButton.Width = 116;
                    checkedButton.Enabled = checkedRows > 0;
                    checkedButton.DialogResult = DialogResult.OK;
                    checkedButton.Click += delegate { selectedScope = RowTargetScope.CheckedRows; };
                    dialog.Controls.Add(checkedButton);

                    Button cancelButton = new Button();
                    cancelButton.Text = "Cancel";
                    cancelButton.Left = 254;
                    cancelButton.Top = 82;
                    cancelButton.Width = 88;
                    cancelButton.DialogResult = DialogResult.Cancel;
                    cancelButton.Click += delegate { selectedScope = RowTargetScope.Cancelled; };
                    dialog.Controls.Add(cancelButton);

                    dialog.AcceptButton = allButton;
                    dialog.CancelButton = cancelButton;

                    dialog.ShowDialog(this);
                    return selectedScope;
                }
            }

            private int CountRealRows()
            {
                int count = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow)
                        count++;
                }
                return count;
            }

            private int CountCheckedRows()
            {
                int count = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow && IsRowChecked(row))
                        count++;
                }
                return count;
            }

            private void ToggleAllApply()
            {
                GridCheckBehavior.ToggleSelectedThenAll(
                    grid,
                    IsRowChecked,
                    (row, value) => row.Cells[ApplyColumnName].Value = value);
            }
private void ApplyRows(bool allRows)
            {
                CabinCustomPropertyStore.EnsureCanWrite(modelDoc);

                int updated = 0;
                int skipped = 0;
                int failed = 0;
                StringBuilder report = new StringBuilder();
                report.AppendLine("Cabin Tools - Configuration Property Editor Report");
                report.AppendLine("Document: " + (modelDoc.GetPathName() ?? modelDoc.GetTitle()));
                report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine();

                HashSet<string> requestedBomNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    bool shouldApply = allRows || IsRowChecked(row);
                    if (!shouldApply)
                    {
                        skipped++;
                        row.Cells[StatusColumnName].Value = "Skipped";
                        continue;
                    }

                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info == null || info.Configuration == null)
                    {
                        failed++;
                        row.Cells[StatusColumnName].Value = "! Configuration unavailable";
                        continue;
                    }

                    try
                    {
                        int writes = 0;
                        string newConfigurationName = CellText(row, ConfigurationColumnName);
                        string description = CellText(row, DescriptionColumnName);
                        string bomPartNumber = CellText(row, BomPartNumberColumnName);

                        if (string.IsNullOrWhiteSpace(newConfigurationName))
                            throw new InvalidOperationException("Configuration name cannot be blank.");

                        if (!string.Equals(newConfigurationName, info.Name, StringComparison.Ordinal))
                        {
                            IConfiguration existingConfiguration = null;
                            try { existingConfiguration = modelDoc.GetConfigurationByName(newConfigurationName) as IConfiguration; }
                            catch { existingConfiguration = null; }

                            if (existingConfiguration != null)
                                throw new InvalidOperationException("Configuration name already exists: " + newConfigurationName);

                            info.Configuration.Name = newConfigurationName;
                            info.Name = newConfigurationName;
                            info.PendingName = newConfigurationName;
                            info.PropertyManager = GetConfigurationPropertyManager(modelDoc, newConfigurationName);
                            row.Cells[ConfigurationColumnName].Value = newConfigurationName;
                            writes++;
                        }

                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            SetTextProperty(info.PropertyManager, "Description", description);
                            dropdownSettings.LearnOption("Description", description);
                            writes++;
                        }

                        if (!string.IsNullOrWhiteSpace(bomPartNumber))
                        {
                            if (!requestedBomNumbers.Add(bomPartNumber))
                                throw new InvalidOperationException("Duplicate requested BOM part number: " + bomPartNumber);

                            info.Configuration.AlternateName = bomPartNumber;
                            info.Configuration.UseAlternateNameInBOM = true;
                            writes++;
                        }

                        foreach (string propertyName in visibleCustomPropertyNames)
                        {
                            string value = CellText(row, propertyName);
                            if (string.IsNullOrWhiteSpace(value))
                                continue;
                            SetTextProperty(info.PropertyManager, propertyName, value);
                            dropdownSettings.LearnOption(propertyName, value);
                            writes++;
                        }

                        if (writes == 0)
                        {
                            skipped++;
                            row.Cells[StatusColumnName].Value = "Skipped - no non-empty values";
                        }
                        else
                        {
                            updated++;
                            row.Cells[StatusColumnName].Value = "Updated " + writes + " value(s)";
                            report.AppendLine(info.Name + ": updated " + writes + " value(s).");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        row.Cells[StatusColumnName].Value = "! " + ex.Message;
                        report.AppendLine(info.Name + ": FAILED - " + ex.Message);
                    }
                }

                dropdownSettings.SetVisibleColumns("Configuration", visibleCustomPropertyNames);
                dropdownSettings.Save();
                ForceRebuild();

                report.AppendLine();
                report.AppendLine("Updated configurations: " + updated);
                report.AppendLine("Skipped configurations: " + skipped);
                report.AppendLine("Failed configurations: " + failed);
                string reportPath = WriteReport(report.ToString());
                statusLabel.Text = "Updated " + updated + " configuration(s), skipped " + skipped + ", failed " + failed + ". Report: " + reportPath;
            }

            private string CellText(DataGridViewRow row, string columnName)
            {
                object value = row.Cells[columnName].Value;
                return value == null ? string.Empty : Convert.ToString(value).Trim();
            }
private void ChooseColumns()
            {
                List<string> selectableNames = new List<string>();
                foreach (string name in allCustomPropertyNames)
                {
                    if (!IsReservedGridPropertyName(name) && !ContainsIgnoreCase(selectableNames, name))
                        selectableNames.Add(name);
                }

                List<string> selectedNames = new List<string>();
                foreach (string name in visibleCustomPropertyNames)
                {
                    if (!IsReservedGridPropertyName(name) && !ContainsIgnoreCase(selectedNames, name))
                        selectedNames.Add(name);
                }

                using (CutListProfilePropertyCommand.ColumnSelectorForm form = new CutListProfilePropertyCommand.ColumnSelectorForm(selectableNames, selectedNames))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                        return;
                    CaptureGridValuesToInfos();
                    visibleCustomPropertyNames.Clear();
                    foreach (string name in form.SelectedColumns)
                    {
                        if (!IsReservedGridPropertyName(name) && !ContainsIgnoreCase(visibleCustomPropertyNames, name))
                            visibleCustomPropertyNames.Add(name);
                    }
                    dropdownSettings.SetVisibleColumns("Configuration", visibleCustomPropertyNames);
                    dropdownSettings.Save();
                    BuildGrid();
                }
            }

            private void AddNewPropertyColumn()
            {
                string propertyName = CutListProfilePropertyCommand.PromptForText("New configuration property column", "Property name:", string.Empty);
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;
                propertyName = propertyName.Trim();
                if (IsReservedGridPropertyName(propertyName))
                {
                    MessageBox.Show("That property is already handled by a fixed column.", "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                CaptureGridValuesToInfos();
                if (!ContainsIgnoreCase(allCustomPropertyNames, propertyName))
                    allCustomPropertyNames.Add(propertyName);
                if (!ContainsIgnoreCase(visibleCustomPropertyNames, propertyName))
                    visibleCustomPropertyNames.Add(propertyName);
                dropdownSettings.SetVisibleColumns("Configuration", visibleCustomPropertyNames);
                dropdownSettings.Save();
                BuildGrid();
            }

            private bool IsRowChecked(DataGridViewRow row)
            {
                object value = row.Cells[ApplyColumnName].Value;
                return value is bool && (bool)value;
            }

            private void SetRowApply(int rowIndex, bool value)
            {
                if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
                    grid.Rows[rowIndex].Cells[ApplyColumnName].Value = value;
            }

            private List<DataGridViewRow> GetSelectedGridRows()
            {
                List<DataGridViewRow> selectedRows = new List<DataGridViewRow>();

                if (grid == null)
                    return selectedRows;

                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                foreach (DataGridViewCell cell in grid.SelectedCells)
                {
                    if (cell == null || cell.RowIndex < 0 || cell.RowIndex >= grid.Rows.Count)
                        continue;

                    DataGridViewRow row = grid.Rows[cell.RowIndex];
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                return selectedRows;
            }

            private void SetAllApply(bool value)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow)
                        row.Cells[ApplyColumnName].Value = value;
                }
            }

            private void ForceRebuild()
            {
                try { modelDoc.ForceRebuild3(false); }
                catch { }
            }

            private string WriteReport(string content)
            {
                try
                {
                    string root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "CabinTools", "ConfigurationPropertyReports");
                    Directory.CreateDirectory(root);
                    string safeTitle = SanitizeFileName(modelDoc.GetTitle() ?? "Document");
                    string path = Path.Combine(root, "ConfigurationPropertyEditor_" + safeTitle + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
                    return path;
                }
                catch
                {
                    return string.Empty;
                }
            }

            private string SanitizeFileName(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "Document";
                string sanitized = value;
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    sanitized = sanitized.Replace(invalid, '_');
                return sanitized.Trim();
            }
        }
    }
}
