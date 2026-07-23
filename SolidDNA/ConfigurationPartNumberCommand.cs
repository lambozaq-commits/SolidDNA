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
    /// - Configuration description.
    /// - SOLIDWORKS BOM part number field: IConfiguration.AlternateName + UseAlternateNameInBOM.
    /// - Selected configuration-specific custom properties.
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
            private ComboBox propertySelector;
            private ComboBox valueSelector;
            private TextBox startNumberTextBox;
            private NumericUpDown digitCountBox;
            private TextBox prefixTextBox;
            private TextBox suffixTextBox;
            private CheckBox overwriteBomNumbersCheckBox;

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
                Label helpLabel = new Label();
                helpLabel.Text = "Edit configuration description, SOLIDWORKS BOM part number, and selected configuration-specific custom-property columns. Empty cells are not written.";
                helpLabel.Left = 12;
                helpLabel.Top = 12;
                helpLabel.Width = 1230;
                helpLabel.Height = 24;
                Controls.Add(helpLabel);

                Button chooseColumnsButton = new Button();
                chooseColumnsButton.Text = "Choose custom-property columns";
                chooseColumnsButton.Left = 12;
                chooseColumnsButton.Top = 42;
                chooseColumnsButton.Width = 210;
                chooseColumnsButton.Click += delegate { ChooseColumns(); };
                Controls.Add(chooseColumnsButton);

                Button addColumnButton = new Button();
                addColumnButton.Text = "Add new custom-property column";
                addColumnButton.Left = 230;
                addColumnButton.Top = 42;
                addColumnButton.Width = 220;
                addColumnButton.Click += delegate { AddNewPropertyColumn(); };
                Controls.Add(addColumnButton);

                Label propertyLabel = new Label();
                propertyLabel.Text = "Column:";
                propertyLabel.Left = 470;
                propertyLabel.Top = 47;
                propertyLabel.Width = 60;
                Controls.Add(propertyLabel);

                propertySelector = new ComboBox();
                propertySelector.Left = 530;
                propertySelector.Top = 42;
                propertySelector.Width = 180;
                propertySelector.DropDownStyle = ComboBoxStyle.DropDownList;
                propertySelector.SelectedIndexChanged += delegate { UpdateValueOptions(); };
                Controls.Add(propertySelector);

                Label valueLabel = new Label();
                valueLabel.Text = "Value:";
                valueLabel.Left = 720;
                valueLabel.Top = 47;
                valueLabel.Width = 45;
                Controls.Add(valueLabel);

                valueSelector = new ComboBox();
                valueSelector.Left = 770;
                valueSelector.Top = 42;
                valueSelector.Width = 185;
                valueSelector.DropDownStyle = ComboBoxStyle.DropDown;
                Controls.Add(valueSelector);

                Button setCheckedButton = new Button();
                setCheckedButton.Text = "Set value to checked";
                setCheckedButton.Left = 965;
                setCheckedButton.Top = 42;
                setCheckedButton.Width = 140;
                setCheckedButton.Click += delegate { SetSelectedColumnValue(false); };
                Controls.Add(setCheckedButton);

                Button setAllButton = new Button();
                setAllButton.Text = "Set value to all";
                setAllButton.Left = 1110;
                setAllButton.Top = 42;
                setAllButton.Width = 120;
                setAllButton.Click += delegate { SetSelectedColumnValue(true); };
                Controls.Add(setAllButton);

                Label startLabel = new Label();
                startLabel.Text = "Start:";
                startLabel.Left = 12;
                startLabel.Top = 82;
                startLabel.Width = 45;
                Controls.Add(startLabel);

                startNumberTextBox = new TextBox();
                startNumberTextBox.Left = 60;
                startNumberTextBox.Top = 77;
                startNumberTextBox.Width = 80;
                startNumberTextBox.Text = "1001";
                Controls.Add(startNumberTextBox);

                Label digitsLabel = new Label();
                digitsLabel.Text = "Digits:";
                digitsLabel.Left = 150;
                digitsLabel.Top = 82;
                digitsLabel.Width = 45;
                Controls.Add(digitsLabel);

                digitCountBox = new NumericUpDown();
                digitCountBox.Left = 198;
                digitCountBox.Top = 77;
                digitCountBox.Width = 55;
                digitCountBox.Minimum = 1;
                digitCountBox.Maximum = 12;
                digitCountBox.Value = 4;
                Controls.Add(digitCountBox);

                Label prefixLabel = new Label();
                prefixLabel.Text = "Prefix:";
                prefixLabel.Left = 265;
                prefixLabel.Top = 82;
                prefixLabel.Width = 45;
                Controls.Add(prefixLabel);

                prefixTextBox = new TextBox();
                prefixTextBox.Left = 315;
                prefixTextBox.Top = 77;
                prefixTextBox.Width = 80;
                Controls.Add(prefixTextBox);

                Label suffixLabel = new Label();
                suffixLabel.Text = "Suffix:";
                suffixLabel.Left = 405;
                suffixLabel.Top = 82;
                suffixLabel.Width = 45;
                Controls.Add(suffixLabel);

                suffixTextBox = new TextBox();
                suffixTextBox.Left = 455;
                suffixTextBox.Top = 77;
                suffixTextBox.Width = 80;
                Controls.Add(suffixTextBox);

                Button useMaxButton = new Button();
                useMaxButton.Text = "Use max + 1";
                useMaxButton.Left = 545;
                useMaxButton.Top = 76;
                useMaxButton.Width = 100;
                useMaxButton.Click += delegate { SetStartToMaxPlusOne(); };
                Controls.Add(useMaxButton);

                Button fillCheckedButton = new Button();
                fillCheckedButton.Text = "Fill BOM checked blanks";
                fillCheckedButton.Left = 655;
                fillCheckedButton.Top = 76;
                fillCheckedButton.Width = 155;
                fillCheckedButton.Click += delegate { FillBomNumbers(false, true); };
                Controls.Add(fillCheckedButton);

                Button fillAllBlankButton = new Button();
                fillAllBlankButton.Text = "Fill BOM all blanks";
                fillAllBlankButton.Left = 815;
                fillAllBlankButton.Top = 76;
                fillAllBlankButton.Width = 140;
                fillAllBlankButton.Click += delegate { FillBomNumbers(true, true); };
                Controls.Add(fillAllBlankButton);

                Button fillAllButton = new Button();
                fillAllButton.Text = "Fill BOM all rows";
                fillAllButton.Left = 960;
                fillAllButton.Top = 76;
                fillAllButton.Width = 120;
                fillAllButton.Click += delegate { FillBomNumbers(true, false); };
                Controls.Add(fillAllButton);

                overwriteBomNumbersCheckBox = new CheckBox();
                overwriteBomNumbersCheckBox.Text = "Allow overwrite existing BOM numbers";
                overwriteBomNumbersCheckBox.Left = 1090;
                overwriteBomNumbersCheckBox.Top = 80;
                overwriteBomNumbersCheckBox.Width = 220;
                Controls.Add(overwriteBomNumbersCheckBox);

                Button clearCheckedButton = new Button();
                clearCheckedButton.Text = "Clear selected column checked";
                clearCheckedButton.Left = 12;
                clearCheckedButton.Top = 108;
                clearCheckedButton.Width = 190;
                clearCheckedButton.Click += delegate { ClearSelectedColumn(false); };
                Controls.Add(clearCheckedButton);

                Button clearAllButton = new Button();
                clearAllButton.Text = "Clear selected column all";
                clearAllButton.Left = 210;
                clearAllButton.Top = 108;
                clearAllButton.Width = 170;
                clearAllButton.Click += delegate { ClearSelectedColumn(true); };
                Controls.Add(clearAllButton);

                Button checkAllButton = new Button();
                checkAllButton.Text = "Check all";
                checkAllButton.Left = 390;
                checkAllButton.Top = 108;
                checkAllButton.Width = 90;
                checkAllButton.Click += delegate { SetAllApply(true); };
                Controls.Add(checkAllButton);

                Button uncheckAllButton = new Button();
                uncheckAllButton.Text = "Uncheck all";
                uncheckAllButton.Left = 485;
                uncheckAllButton.Top = 108;
                uncheckAllButton.Width = 100;
                uncheckAllButton.Click += delegate { SetAllApply(false); };
                Controls.Add(uncheckAllButton);

                Button refreshButton = new Button();
                refreshButton.Text = "Refresh configurations";
                refreshButton.Left = 595;
                refreshButton.Top = 108;
                refreshButton.Width = 145;
                refreshButton.Click += delegate { LoadConfigurationData(); BuildGrid(); };
                Controls.Add(refreshButton);

                grid = new DataGridView();
                grid.Left = 12;
                grid.Top = 142;
                grid.Width = 1280;
                grid.Height = 525;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.RowHeadersVisible = false;
                grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
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
                Controls.Add(grid);

                statusLabel = new Label();
                statusLabel.Left = 12;
                statusLabel.Top = 675;
                statusLabel.Width = 780;
                statusLabel.Height = 50;
                statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                Controls.Add(statusLabel);

                Button applyCheckedButton = new Button();
                applyCheckedButton.Text = "Apply checked rows";
                applyCheckedButton.Left = 880;
                applyCheckedButton.Top = 710;
                applyCheckedButton.Width = 140;
                applyCheckedButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                applyCheckedButton.Click += delegate { ApplyRows(false); };
                Controls.Add(applyCheckedButton);

                Button applyAllButton = new Button();
                applyAllButton.Text = "Apply all rows";
                applyAllButton.Left = 1025;
                applyAllButton.Top = 710;
                applyAllButton.Width = 120;
                applyAllButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                applyAllButton.Click += delegate { ApplyRows(true); };
                Controls.Add(applyAllButton);

                Button closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Left = 1150;
                closeButton.Top = 710;
                closeButton.Width = 100;
                closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                closeButton.Click += delegate { Close(); };
                Controls.Add(closeButton);
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
                    info.Configuration = configuration;
                    info.PropertyManager = propertyManager;
                    info.Description = GetConfigurationDescription(configuration);
                    info.BomPartNumber = GetConfigurationBomPartNumber(configuration);
                    info.IsDerived = IsDerivedConfiguration(configuration);

                    foreach (string propertyName in GetPropertyNames(propertyManager))
                    {
                        if (!ContainsIgnoreCase(allCustomPropertyNames, propertyName))
                            allCustomPropertyNames.Add(propertyName);
                        info.CustomProperties[propertyName] = ReadTextProperty(propertyManager, propertyName);
                    }

                    configurations.Add(info);
                }

                if (visibleCustomPropertyNames.Count == 0)
                {
                    foreach (string name in dropdownSettings.GetVisibleColumns("Configuration"))
                    {
                        if (ContainsIgnoreCase(allCustomPropertyNames, name) && !ContainsIgnoreCase(visibleCustomPropertyNames, name))
                            visibleCustomPropertyNames.Add(name);
                    }
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

                AddTextColumn(ConfigurationColumnName, "Configuration", 240, true);
                AddTextColumn(DescriptionColumnName, "Description", 220, false);
                AddTextColumn(BomPartNumberColumnName, "BOM Part Number", 145, false);
                AddCheckColumn(DerivedColumnName, "Derived", 70, true);

                foreach (string propertyName in visibleCustomPropertyNames)
                    AddTextColumn(propertyName, propertyName, 150, false);

                AddTextColumn(StatusColumnName, "Status", 220, true);

                foreach (ConfigurationInfo info in configurations)
                {
                    int rowIndex = grid.Rows.Add();
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = info;
                    row.Cells[ApplyColumnName].Value = false;
                    row.Cells[ConfigurationColumnName].Value = info.Name;
                    row.Cells[DescriptionColumnName].Value = info.Description;
                    row.Cells[BomPartNumberColumnName].Value = info.BomPartNumber;
                    row.Cells[DerivedColumnName].Value = info.IsDerived;
                    row.Cells[StatusColumnName].Value = "Ready";

                    foreach (string propertyName in visibleCustomPropertyNames)
                    {
                        string value;
                        info.CustomProperties.TryGetValue(propertyName, out value);
                        row.Cells[propertyName].Value = value ?? string.Empty;
                    }
                }

                ReloadPropertySelector();
                statusLabel.Text = "Loaded " + configurations.Count + " configuration(s). Visible custom-property columns: " + string.Join(", ", visibleCustomPropertyNames.ToArray());
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

            private void ReloadPropertySelector()
            {
                string current = propertySelector.SelectedItem == null ? string.Empty : propertySelector.SelectedItem.ToString();
                propertySelector.Items.Clear();
                propertySelector.Items.Add("Description");
                propertySelector.Items.Add("BOM Part Number");
                foreach (string name in visibleCustomPropertyNames)
                    propertySelector.Items.Add(name);

                if (!string.IsNullOrWhiteSpace(current) && propertySelector.Items.Contains(current))
                    propertySelector.SelectedItem = current;
                else if (propertySelector.Items.Count > 0)
                    propertySelector.SelectedIndex = 0;
                UpdateValueOptions();
            }

            private void UpdateValueOptions()
            {
                valueSelector.Items.Clear();
                string propertyName = GetSelectedPropertyDisplayName();
                foreach (string option in dropdownSettings.GetOptions(propertyName))
                    valueSelector.Items.Add(option);
                valueSelector.Text = string.Empty;
            }

            private string GetSelectedPropertyDisplayName()
            {
                return propertySelector.SelectedItem == null ? string.Empty : propertySelector.SelectedItem.ToString();
            }

            private string GetGridColumnNameFromDisplayName(string displayName)
            {
                if (string.Equals(displayName, "Description", StringComparison.OrdinalIgnoreCase))
                    return DescriptionColumnName;
                if (string.Equals(displayName, "BOM Part Number", StringComparison.OrdinalIgnoreCase))
                    return BomPartNumberColumnName;
                return displayName;
            }

            private void SetSelectedColumnValue(bool allRows)
            {
                string displayName = GetSelectedPropertyDisplayName();
                string columnName = GetGridColumnNameFromDisplayName(displayName);
                string value = valueSelector.Text == null ? string.Empty : valueSelector.Text.Trim();
                if (string.IsNullOrWhiteSpace(displayName) || !grid.Columns.Contains(columnName))
                    return;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;
                    row.Cells[columnName].Value = value;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Edited";
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    dropdownSettings.AddOption(displayName, value);
                    dropdownSettings.Save();
                }
            }

            private void ClearSelectedColumn(bool allRows)
            {
                string displayName = GetSelectedPropertyDisplayName();
                string columnName = GetGridColumnNameFromDisplayName(displayName);
                if (string.IsNullOrWhiteSpace(displayName) || !grid.Columns.Contains(columnName))
                    return;

                DialogResult result = MessageBox.Show(
                    "This will set column '" + displayName + "' to a blank value.\r\n\r\nRows affected: " + (allRows ? "all rows" : "checked rows") + ".",
                    "Cabin Tools - Clear Configuration Column",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.OK)
                    return;

                int cleared = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;

                    ConfigurationInfo info = row.Tag as ConfigurationInfo;
                    if (info == null)
                        continue;

                    try
                    {
                        if (columnName == DescriptionColumnName)
                        {
                            if (info.Configuration != null)
                                info.Configuration.Description = string.Empty;
                        }
                        else if (columnName == BomPartNumberColumnName)
                        {
                            if (info.Configuration != null)
                            {
                                info.Configuration.AlternateName = string.Empty;
                                info.Configuration.UseAlternateNameInBOM = false;
                            }
                        }
                        else
                        {
                            SetTextProperty(info.PropertyManager, displayName, string.Empty);
                        }

                        row.Cells[columnName].Value = string.Empty;
                        row.Cells[StatusColumnName].Value = "Cleared " + displayName;
                        cleared++;
                    }
                    catch (Exception ex)
                    {
                        row.Cells[StatusColumnName].Value = "! " + ex.Message;
                    }
                }

                ForceRebuild();
                statusLabel.Text = "Cleared " + displayName + " on " + cleared + " configuration(s).";
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
                        string description = CellText(row, DescriptionColumnName);
                        string bomPartNumber = CellText(row, BomPartNumberColumnName);

                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            info.Configuration.Description = description;
                            writes++;
                        }

                        if (!string.IsNullOrWhiteSpace(bomPartNumber))
                        {
                            if (!overwriteBomNumbersCheckBox.Checked && !string.IsNullOrWhiteSpace(info.BomPartNumber) &&
                                !string.Equals(info.BomPartNumber, bomPartNumber, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException("Existing BOM part number is protected. Enable overwrite to replace it.");
                            }

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

            private void SetStartToMaxPlusOne()
            {
                int max = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    string text = CellText(row, BomPartNumberColumnName);
                    int number;
                    if (TryParseDigits(text, out number) && number > max)
                        max = number;
                }
                startNumberTextBox.Text = (max + 1).ToString();
            }

            private void FillBomNumbers(bool allRows, bool blanksOnly)
            {
                int current;
                if (!int.TryParse(startNumberTextBox.Text.Trim(), out current))
                {
                    MessageBox.Show("Start number must be numeric.", "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int digits = (int)digitCountBox.Value;
                string prefix = prefixTextBox.Text ?? string.Empty;
                string suffix = suffixTextBox.Text ?? string.Empty;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;
                    if (blanksOnly && !string.IsNullOrWhiteSpace(CellText(row, BomPartNumberColumnName)))
                        continue;

                    string number = prefix + current.ToString(new string('0', digits)) + suffix;
                    row.Cells[BomPartNumberColumnName].Value = number;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "BOM number filled";
                    current++;
                }
            }

            private bool TryParseDigits(string text, out int number)
            {
                number = 0;
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                StringBuilder digits = new StringBuilder();
                foreach (char c in text)
                {
                    if (char.IsDigit(c))
                        digits.Append(c);
                }
                return digits.Length > 0 && int.TryParse(digits.ToString(), out number);
            }

            private void ChooseColumns()
            {
                using (CutListProfilePropertyCommand.ColumnSelectorForm form = new CutListProfilePropertyCommand.ColumnSelectorForm(allCustomPropertyNames, visibleCustomPropertyNames))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                        return;
                    visibleCustomPropertyNames.Clear();
                    foreach (string name in form.SelectedColumns)
                        visibleCustomPropertyNames.Add(name);
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
