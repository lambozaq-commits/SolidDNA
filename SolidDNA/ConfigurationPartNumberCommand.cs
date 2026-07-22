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
    /// Manually assigns SOLIDWORKS BOM part numbers to part or assembly
    /// configurations in bulk.
    ///
    /// The command writes only the SOLIDWORKS configuration BOM part number by setting:
    /// - IConfiguration.AlternateName
    /// - IConfiguration.UseAlternateNameInBOM = true
    ///
    /// It does not write a separate configuration-specific custom property.
    /// The SOLIDWORKS source document is modified but never saved automatically.
    /// </summary>
    internal static class ConfigurationPartNumberCommand
    {
        public static void ShowConfigurationPartNumberForm()
        {
            try
            {
                IModelDoc2 modelDoc =
                    CabinCustomPropertyStore.GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage(
                        "Open a part or assembly before assigning configuration part numbers.",
                        MessageBoxIcon.Warning);
                    return;
                }

                int documentType = modelDoc.GetType();

                if (documentType != (int)swDocumentTypes_e.swDocPART &&
                    documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    ShowMessage(
                        "Configuration part-number assignment works only for parts and assemblies.",
                        MessageBoxIcon.Warning);
                    return;
                }

                string writeBlockReason =
                    CabinCustomPropertyStore.GetWriteBlockReason(modelDoc);

                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(
                        writeBlockReason,
                        MessageBoxIcon.Warning);
                    return;
                }

                using (ConfigurationPartNumberForm form =
                    new ConfigurationPartNumberForm(modelDoc))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowMessage(
                    "Cabin Tools could not open the configuration part-number tool.\r\n\r\n" +
                    ex.Message,
                    MessageBoxIcon.Error);
            }
        }

        internal static List<ConfigurationPartNumberRow> ReadConfigurationRows(
            IModelDoc2 modelDoc)
        {
            List<ConfigurationPartNumberRow> rows =
                new List<ConfigurationPartNumberRow>();

            if (modelDoc == null)
                return rows;

            object namesObject = modelDoc.GetConfigurationNames();
            Array namesArray = namesObject as Array;

            if (namesArray == null)
                return rows;

            int originalIndex = 0;

            foreach (object nameObject in namesArray)
            {
                string configurationName =
                    nameObject == null
                        ? string.Empty
                        : Convert.ToString(nameObject);

                if (string.IsNullOrWhiteSpace(configurationName))
                    continue;

                IConfiguration configuration = null;

                try
                {
                    configuration =
                        modelDoc.GetConfigurationByName(configurationName)
                            as IConfiguration;
                }
                catch
                {
                    configuration = null;
                }

                ConfigurationPartNumberRow row =
                    new ConfigurationPartNumberRow();

                row.Apply = false;
                row.OriginalIndex = originalIndex;
                row.ConfigurationName = configurationName;
                row.ConfigurationDescription =
                    GetConfigurationDescription(configuration);
                row.CurrentBomPartNumber =
                    GetConfigurationBomPartNumber(configuration);
                row.NewPartNumber = string.Empty;
                row.IsDerivedConfiguration =
                    IsDerivedConfiguration(configuration);
                row.Status = "Ready";

                rows.Add(row);
                originalIndex++;
            }

            return rows;
        }

        internal static ConfigurationPartNumberWriteReport ApplyRows(
            IModelDoc2 modelDoc,
            IList<ConfigurationPartNumberRow> rows,
            bool allowOverwriteExistingPartNumbers)
        {
            if (modelDoc == null)
            {
                throw new InvalidOperationException(
                    "No SOLIDWORKS part or assembly document was supplied.");
            }

            int documentType = modelDoc.GetType();

            if (documentType != (int)swDocumentTypes_e.swDocPART &&
                documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                throw new InvalidOperationException(
                    "The active document must be a part or assembly.");
            }

            CabinCustomPropertyStore.EnsureCanWrite(modelDoc);

            ConfigurationPartNumberWriteReport report =
                new ConfigurationPartNumberWriteReport();

            report.DocumentTitle = modelDoc.GetTitle() ?? string.Empty;
            report.DocumentPath = modelDoc.GetPathName() ?? string.Empty;
            report.AllowOverwriteExistingPartNumbers =
                allowOverwriteExistingPartNumbers;
            if (rows == null || rows.Count == 0)
            {
                report.GeneralMessages.Add(
                    "No configuration rows were supplied.");
                report.ReportPath = WriteReport(modelDoc, report);
                return report;
            }

            ValidateRequestedRows(
                rows,
                allowOverwriteExistingPartNumbers,
                report);

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row == null)
                    continue;

                if (!row.Apply)
                {
                    row.Status = "Skipped";
                    report.SkippedRows.Add(row.CloneForReport());
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.ValidationError))
                {
                    row.Status = "! " + row.ValidationError;
                    report.FailedRows.Add(row.CloneForReport());
                    continue;
                }

                IConfiguration configuration = null;

                try
                {
                    configuration =
                        modelDoc.GetConfigurationByName(
                            row.ConfigurationName) as IConfiguration;
                }
                catch
                {
                    configuration = null;
                }

                if (configuration == null)
                {
                    row.Status = "! Configuration not found";
                    report.FailedRows.Add(row.CloneForReport());
                    continue;
                }

                try
                {
                    string newPartNumber =
                        row.NewPartNumber == null
                            ? string.Empty
                            : row.NewPartNumber.Trim();

                    configuration.AlternateName = newPartNumber;
                    configuration.UseAlternateNameInBOM = true;


                    row.Status = "Updated";
                    report.UpdatedRows.Add(row.CloneForReport());
                }
                catch (Exception ex)
                {
                    row.Status = "! " + ex.Message;
                    report.FailedRows.Add(row.CloneForReport());
                }
            }

            try
            {
                modelDoc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                report.GeneralMessages.Add(
                    "Force rebuild after configuration part-number update failed: " +
                    ex.Message);
            }

            report.ReportPath = WriteReport(modelDoc, report);
            return report;
        }

        internal static string FormatPartNumber(
            int number,
            int digits,
            string prefix,
            string suffix)
        {
            if (digits < 1)
                digits = 1;

            if (digits > 12)
                digits = 12;

            string numeric =
                number.ToString(
                    new string('0', digits));

            return (prefix ?? string.Empty) +
                   numeric +
                   (suffix ?? string.Empty);
        }

        internal static int FindNextNumber(
            IList<ConfigurationPartNumberRow> rows,
            string prefix,
            string suffix)
        {
            int maximum = 0;

            if (rows == null)
                return 1;

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row == null)
                    continue;

                int parsed;

                if (TryParseFormattedNumber(
                        row.CurrentBomPartNumber,
                        prefix,
                        suffix,
                        out parsed))
                {
                    if (parsed > maximum)
                        maximum = parsed;
                }


                if (TryParseFormattedNumber(
                        row.NewPartNumber,
                        prefix,
                        suffix,
                        out parsed))
                {
                    if (parsed > maximum)
                        maximum = parsed;
                }
            }

            return maximum + 1;
        }

        internal static bool TryParseFormattedNumber(
            string partNumber,
            string prefix,
            string suffix,
            out int number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(partNumber))
                return false;

            string value = partNumber.Trim();
            string safePrefix = prefix ?? string.Empty;
            string safeSuffix = suffix ?? string.Empty;

            if (!string.IsNullOrEmpty(safePrefix) &&
                !value.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(safeSuffix) &&
                !value.EndsWith(safeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string numeric = value;

            if (!string.IsNullOrEmpty(safePrefix))
            {
                numeric = numeric.Substring(safePrefix.Length);
            }

            if (!string.IsNullOrEmpty(safeSuffix))
            {
                numeric = numeric.Substring(
                    0,
                    numeric.Length - safeSuffix.Length);
            }

            numeric = numeric.Trim();

            if (numeric.Length == 0)
                return false;

            for (int i = 0; i < numeric.Length; i++)
            {
                if (!char.IsDigit(numeric[i]))
                    return false;
            }

            return int.TryParse(numeric, out number);
        }

        private static string GetConfigurationBomPartNumber(
            IConfiguration configuration)
        {
            if (configuration == null)
                return string.Empty;

            try
            {
                bool useAlternateName =
                    configuration.UseAlternateNameInBOM;

                string alternateName =
                    configuration.AlternateName ?? string.Empty;

                if (useAlternateName)
                    return alternateName.Trim();
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string GetConfigurationDescription(
            IConfiguration configuration)
        {
            if (configuration == null)
                return string.Empty;

            try
            {
                return configuration.Description == null
                    ? string.Empty
                    : configuration.Description.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsDerivedConfiguration(
            IConfiguration configuration)
        {
            if (configuration == null)
                return false;

            try
            {
                object parent =
                    configuration.GetType().InvokeMember(
                        "GetParent",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        configuration,
                        null);

                return parent != null;
            }
            catch
            {
                return false;
            }
        }

        private static void ValidateRequestedRows(
            IList<ConfigurationPartNumberRow> rows,
            bool allowOverwriteExistingPartNumbers,
            ConfigurationPartNumberWriteReport report)
        {
            Dictionary<string, ConfigurationPartNumberRow> requestedPartNumbers =
                new Dictionary<string, ConfigurationPartNumberRow>(
                    StringComparer.OrdinalIgnoreCase);

            HashSet<string> reservedExistingPartNumbers =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row == null)
                    continue;

                row.ValidationError = string.Empty;

                if (!row.Apply)
                {
                    if (!string.IsNullOrWhiteSpace(row.CurrentBomPartNumber))
                    {
                        reservedExistingPartNumbers.Add(row.CurrentBomPartNumber.Trim());
                    }
                }
            }

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row == null || !row.Apply)
                    continue;

                string newPartNumber =
                    row.NewPartNumber == null
                        ? string.Empty
                        : row.NewPartNumber.Trim();

                if (string.IsNullOrWhiteSpace(newPartNumber))
                {
                    row.ValidationError = "New part number is blank";
                    continue;
                }

                if (!allowOverwriteExistingPartNumbers &&
                    !string.IsNullOrWhiteSpace(row.CurrentBomPartNumber))
                {
                    row.ValidationError =
                        "Existing BOM part number is protected";
                    continue;
                }

                if (reservedExistingPartNumbers.Contains(newPartNumber))
                {
                    row.ValidationError =
                        "New part number is already used by an unchecked configuration";
                    continue;
                }

                ConfigurationPartNumberRow existingRequestedRow;

                if (requestedPartNumbers.TryGetValue(
                        newPartNumber,
                        out existingRequestedRow))
                {
                    row.ValidationError =
                        "Duplicate new part number in this batch";

                    if (existingRequestedRow != null &&
                        string.IsNullOrWhiteSpace(
                            existingRequestedRow.ValidationError))
                    {
                        existingRequestedRow.ValidationError =
                            "Duplicate new part number in this batch";
                    }

                    continue;
                }

                requestedPartNumbers.Add(
                    newPartNumber,
                    row);
            }
        }

        private static string WriteReport(
            IModelDoc2 modelDoc,
            ConfigurationPartNumberWriteReport report)
        {
            try
            {
                string reportFolder =
                    Path.Combine(
                        System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder.MyDocuments),
                        "CabinTools",
                        "ConfigurationPartNumberReports");

                Directory.CreateDirectory(reportFolder);

                string documentTitle =
                    modelDoc == null
                        ? string.Empty
                        : modelDoc.GetTitle() ?? string.Empty;

                string fileBaseName =
                    "ConfigurationPartNumbers_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss");

                if (!string.IsNullOrWhiteSpace(documentTitle))
                {
                    fileBaseName +=
                        "_" +
                        SanitizeFileName(
                            Path.GetFileNameWithoutExtension(documentTitle));
                }

                string reportPath =
                    Path.Combine(
                        reportFolder,
                        fileBaseName + ".txt");

                File.WriteAllText(
                    reportPath,
                    BuildReportText(report),
                    Encoding.UTF8);

                return reportPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildReportText(
            ConfigurationPartNumberWriteReport report)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine(
                "Cabin Tools - Configuration part-number assignment report");
            builder.AppendLine(
                "Generated: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(
                "Document title: " +
                (report.DocumentTitle ?? string.Empty));
            builder.AppendLine(
                "Document path: " +
                (report.DocumentPath ?? string.Empty));
            builder.AppendLine(
                "Allow overwrite existing BOM part numbers: " +
                report.AllowOverwriteExistingPartNumbers);
            builder.AppendLine();
            builder.AppendLine("Summary:");
            builder.AppendLine(
                "Updated rows: " + report.UpdatedRows.Count);
            builder.AppendLine(
                "Skipped rows: " + report.SkippedRows.Count);
            builder.AppendLine(
                "Failed rows: " + report.FailedRows.Count);
            builder.AppendLine();

            if (report.GeneralMessages.Count > 0)
            {
                builder.AppendLine("General messages:");

                foreach (string message in report.GeneralMessages)
                {
                    builder.AppendLine("- " + message);
                }

                builder.AppendLine();
            }

            AppendRows(
                builder,
                "Updated rows",
                report.UpdatedRows);
            AppendRows(
                builder,
                "Skipped rows",
                report.SkippedRows);
            AppendRows(
                builder,
                "Failed rows",
                report.FailedRows);

            return builder.ToString();
        }

        private static void AppendRows(
            StringBuilder builder,
            string heading,
            IList<ConfigurationPartNumberRow> rows)
        {
            builder.AppendLine(heading + ":");

            if (rows == null || rows.Count == 0)
            {
                builder.AppendLine("- None");
                builder.AppendLine();
                return;
            }

            foreach (ConfigurationPartNumberRow row in rows)
            {
                builder.AppendLine(
                    "- " + DisplayValue(row.ConfigurationName));
                builder.AppendLine(
                    "  Current BOM part number: " +
                    DisplayValue(row.CurrentBomPartNumber));
                builder.AppendLine(
                    "  New part number: " +
                    DisplayValue(row.NewPartNumber));
                builder.AppendLine(
                    "  Status: " + DisplayValue(row.Status));
            }

            builder.AppendLine();
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Document";

            string sanitized = value;

            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized.Trim();
        }

        private static void ShowMessage(
            string message,
            MessageBoxIcon icon)
        {
            MessageBox.Show(
                message,
                "Cabin Tools - Configuration Part Numbers",
                MessageBoxButtons.OK,
                icon);
        }
    }

    internal sealed class ConfigurationPartNumberRow
    {
        public bool Apply;
        public int OriginalIndex;
        public string ConfigurationName = string.Empty;
        public string ConfigurationDescription = string.Empty;
        public string CurrentBomPartNumber = string.Empty;
        public string NewPartNumber = string.Empty;
        public bool IsDerivedConfiguration;
        public string Status = string.Empty;
        public string ValidationError = string.Empty;

        public ConfigurationPartNumberRow CloneForReport()
        {
            return new ConfigurationPartNumberRow
            {
                Apply = Apply,
                OriginalIndex = OriginalIndex,
                ConfigurationName = ConfigurationName,
                ConfigurationDescription = ConfigurationDescription,
                CurrentBomPartNumber = CurrentBomPartNumber,
                NewPartNumber = NewPartNumber,
                IsDerivedConfiguration = IsDerivedConfiguration,
                Status = Status,
                ValidationError = ValidationError
            };
        }
    }

    internal sealed class ConfigurationPartNumberWriteReport
    {
        public string DocumentTitle = string.Empty;
        public string DocumentPath = string.Empty;
        public string ReportPath = string.Empty;
        public bool AllowOverwriteExistingPartNumbers;
        public List<string> GeneralMessages = new List<string>();
        public List<ConfigurationPartNumberRow> UpdatedRows =
            new List<ConfigurationPartNumberRow>();
        public List<ConfigurationPartNumberRow> SkippedRows =
            new List<ConfigurationPartNumberRow>();
        public List<ConfigurationPartNumberRow> FailedRows =
            new List<ConfigurationPartNumberRow>();
    }

    internal sealed class ConfigurationPartNumberForm : Form
    {
        private readonly IModelDoc2 modelDoc;
        private readonly List<ConfigurationPartNumberRow> rows =
            new List<ConfigurationPartNumberRow>();

        private DataGridView grid;
        private TextBox startNumberTextBox;
        private TextBox digitsTextBox;
        private TextBox prefixTextBox;
        private TextBox suffixTextBox;
        private CheckBox allowOverwriteCheckBox;
        private CheckBox includeDerivedWhenFillingCheckBox;
        private Label statusLabel;
        private bool suppressGridEvents;

        private const int ColumnApply = 0;
        private const int ColumnConfiguration = 1;
        private const int ColumnDescription = 2;
        private const int ColumnCurrentBomPartNumber = 3;
        private const int ColumnNewPartNumber = 4;
        private const int ColumnDerived = 5;
        private const int ColumnStatus = 6;

        public ConfigurationPartNumberForm(IModelDoc2 activeModelDoc)
        {
            modelDoc = activeModelDoc;
            InitializeComponent();
            RefreshRowsFromDocument();
        }

        private void InitializeComponent()
        {
            Text = "Cabin Tools - Configuration Part Numbers";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1320;
            Height = 780;
            MinimumSize = new Size(1050, 620);

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.Padding = new Padding(10);
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(mainLayout);

            Label headerLabel = new Label();
            headerLabel.AutoSize = true;
            headerLabel.MaximumSize = new Size(1260, 0);
            headerLabel.Text =
                "Assign the SOLIDWORKS configuration BOM part number in bulk. " +
                "This writes the value shown in Configuration Properties > Bill of Materials Options > User Specified Name. " +
                "Existing part numbers are protected by default. The document is modified but not saved automatically.";
            headerLabel.Padding = new Padding(0, 0, 0, 6);
            mainLayout.Controls.Add(headerLabel, 0, 0);

            mainLayout.Controls.Add(CreateControlsPanel(), 0, 1);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoGenerateColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.DataError += Grid_DataError;

            CreateGridColumns();
            mainLayout.Controls.Add(grid, 0, 2);

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(0, 8, 0, 8);
            statusLabel.Text = "Ready.";
            mainLayout.Controls.Add(statusLabel, 0, 3);

            mainLayout.Controls.Add(CreateBottomButtonsPanel(), 0, 4);
        }

        private Control CreateControlsPanel()
        {
            GroupBox group = new GroupBox();
            group.Text = "Number generation and safety";
            group.Dock = DockStyle.Top;
            group.AutoSize = true;
            group.Padding = new Padding(8);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel numberPanel = new FlowLayoutPanel();
            numberPanel.Dock = DockStyle.Fill;
            numberPanel.AutoSize = true;
            numberPanel.WrapContents = true;

            numberPanel.Controls.Add(CreateLabel("Start:"));
            startNumberTextBox = CreateTextBox(80, "1001");
            numberPanel.Controls.Add(startNumberTextBox);

            numberPanel.Controls.Add(CreateLabel("Digits:"));
            digitsTextBox = CreateTextBox(45, "4");
            numberPanel.Controls.Add(digitsTextBox);

            numberPanel.Controls.Add(CreateLabel("Prefix:"));
            prefixTextBox = CreateTextBox(80, string.Empty);
            numberPanel.Controls.Add(prefixTextBox);

            numberPanel.Controls.Add(CreateLabel("Suffix:"));
            suffixTextBox = CreateTextBox(80, string.Empty);
            numberPanel.Controls.Add(suffixTextBox);

            Button useNextNumberButton = CreateButton(
                "Use max + 1",
                UseNextNumberButton_Click);
            numberPanel.Controls.Add(useNextNumberButton);

            Button fillCheckedButton = CreateButton(
                "Fill checked",
                FillCheckedButton_Click);
            numberPanel.Controls.Add(fillCheckedButton);

            Button fillBlankButton = CreateButton(
                "Fill blank/unassigned",
                FillBlankButton_Click);
            numberPanel.Controls.Add(fillBlankButton);

            Button fillAllButton = CreateButton(
                "Fill all rows",
                FillAllButton_Click);
            numberPanel.Controls.Add(fillAllButton);

            layout.Controls.Add(numberPanel, 0, 0);

            FlowLayoutPanel safetyPanel = new FlowLayoutPanel();
            safetyPanel.Dock = DockStyle.Fill;
            safetyPanel.AutoSize = true;
            safetyPanel.WrapContents = true;

            allowOverwriteCheckBox = new CheckBox();
            allowOverwriteCheckBox.AutoSize = true;
            allowOverwriteCheckBox.Text =
                "Allow overwrite of existing BOM part numbers";
            allowOverwriteCheckBox.Checked = false;
            safetyPanel.Controls.Add(allowOverwriteCheckBox);


            includeDerivedWhenFillingCheckBox = new CheckBox();
            includeDerivedWhenFillingCheckBox.AutoSize = true;
            includeDerivedWhenFillingCheckBox.Text =
                "Include derived configurations when filling numbers";
            includeDerivedWhenFillingCheckBox.Checked = true;
            safetyPanel.Controls.Add(includeDerivedWhenFillingCheckBox);

            layout.Controls.Add(safetyPanel, 0, 1);

            FlowLayoutPanel orderPanel = new FlowLayoutPanel();
            orderPanel.Dock = DockStyle.Fill;
            orderPanel.AutoSize = true;
            orderPanel.WrapContents = true;

            orderPanel.Controls.Add(
                CreateButton("Check all", CheckAllButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Uncheck all", UncheckAllButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Check blank only", CheckBlankOnlyButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Sort by configuration name", SortByNameButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Restore SOLIDWORKS order", RestoreOriginalOrderButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Move selected up", MoveSelectedUpButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Move selected down", MoveSelectedDownButton_Click));
            orderPanel.Controls.Add(
                CreateButton("Refresh configurations", RefreshButton_Click));

            layout.Controls.Add(orderPanel, 0, 2);

            group.Controls.Add(layout);
            return group;
        }

        private Control CreateBottomButtonsPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoSize = true;
            panel.FlowDirection = FlowDirection.RightToLeft;

            Button closeButton = CreateButton("Close", CloseButton_Click);
            panel.Controls.Add(closeButton);

            Button applyAllButton = CreateButton(
                "Apply all rows",
                ApplyAllButton_Click);
            panel.Controls.Add(applyAllButton);

            Button applyCheckedButton = CreateButton(
                "Apply checked rows",
                ApplyCheckedButton_Click);
            panel.Controls.Add(applyCheckedButton);

            CancelButton = closeButton;
            return panel;
        }

        private void CreateGridColumns()
        {
            DataGridViewCheckBoxColumn applyColumn =
                new DataGridViewCheckBoxColumn();
            applyColumn.HeaderText = "Apply";
            applyColumn.Width = 55;
            grid.Columns.Add(applyColumn);

            DataGridViewTextBoxColumn configColumn =
                new DataGridViewTextBoxColumn();
            configColumn.HeaderText = "Configuration";
            configColumn.ReadOnly = true;
            configColumn.Width = 260;
            grid.Columns.Add(configColumn);

            DataGridViewTextBoxColumn descriptionColumn =
                new DataGridViewTextBoxColumn();
            descriptionColumn.HeaderText = "Description";
            descriptionColumn.ReadOnly = true;
            descriptionColumn.Width = 220;
            grid.Columns.Add(descriptionColumn);

            DataGridViewTextBoxColumn currentBomColumn =
                new DataGridViewTextBoxColumn();
            currentBomColumn.HeaderText = "Current BOM part number";
            currentBomColumn.ReadOnly = true;
            currentBomColumn.Width = 160;
            grid.Columns.Add(currentBomColumn);


            DataGridViewTextBoxColumn newColumn =
                new DataGridViewTextBoxColumn();
            newColumn.HeaderText = "New part number";
            newColumn.Width = 160;
            grid.Columns.Add(newColumn);

            DataGridViewCheckBoxColumn derivedColumn =
                new DataGridViewCheckBoxColumn();
            derivedColumn.HeaderText = "Derived";
            derivedColumn.ReadOnly = true;
            derivedColumn.Width = 65;
            grid.Columns.Add(derivedColumn);

            DataGridViewTextBoxColumn statusColumn =
                new DataGridViewTextBoxColumn();
            statusColumn.HeaderText = "Status";
            statusColumn.ReadOnly = true;
            statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            statusColumn.MinimumWidth = 220;
            grid.Columns.Add(statusColumn);
        }

        private void RefreshRowsFromDocument()
        {
            rows.Clear();
            grid.Rows.Clear();

            List<ConfigurationPartNumberRow> loadedRows =
                ConfigurationPartNumberCommand.ReadConfigurationRows(modelDoc);

            foreach (ConfigurationPartNumberRow row in loadedRows)
            {
                rows.Add(row);
            }

            ReloadGridFromRows();
            UpdateStatus();
        }

        private void ReloadGridFromRows()
        {
            grid.Rows.Clear();

            foreach (ConfigurationPartNumberRow row in rows)
            {
                int index = grid.Rows.Add();
                DataGridViewRow gridRow = grid.Rows[index];
                gridRow.Tag = row;
                WriteGridRowFromModel(gridRow, row);
            }

            UpdateStatus();
        }

        private void WriteGridRowFromModel(
            DataGridViewRow gridRow,
            ConfigurationPartNumberRow row)
        {
            suppressGridEvents = true;

            try
            {
            gridRow.Cells[ColumnApply].Value = row.Apply;
            gridRow.Cells[ColumnConfiguration].Value = row.ConfigurationName;
            gridRow.Cells[ColumnDescription].Value = row.ConfigurationDescription;
            gridRow.Cells[ColumnCurrentBomPartNumber].Value =
                Display(row.CurrentBomPartNumber);
            gridRow.Cells[ColumnNewPartNumber].Value = row.NewPartNumber;
            gridRow.Cells[ColumnDerived].Value = row.IsDerivedConfiguration;
            gridRow.Cells[ColumnStatus].Value = row.Status;

            ApplyRowStyle(gridRow, row);
            }
            finally
            {
                suppressGridEvents = false;
            }
        }

        private void ReadGridRowToModel(
            DataGridViewRow gridRow,
            ConfigurationPartNumberRow row)
        {
            row.Apply = Convert.ToBoolean(
                gridRow.Cells[ColumnApply].Value ?? false);

            row.NewPartNumber =
                Convert.ToString(
                    gridRow.Cells[ColumnNewPartNumber].Value) ??
                string.Empty;
        }

        private void ApplyRowStyle(
            DataGridViewRow gridRow,
            ConfigurationPartNumberRow row)
        {
            gridRow.DefaultCellStyle.BackColor = Color.White;
            gridRow.DefaultCellStyle.ForeColor = Color.Black;

            if (!string.IsNullOrWhiteSpace(row.Status) &&
                row.Status.StartsWith("! ", StringComparison.Ordinal))
            {
                gridRow.Cells[ColumnStatus].Style.BackColor = Color.MistyRose;
                gridRow.Cells[ColumnStatus].Style.ForeColor = Color.DarkRed;
            }
            else if (!string.IsNullOrWhiteSpace(row.CurrentBomPartNumber))
            {
                gridRow.Cells[ColumnCurrentBomPartNumber].Style.BackColor =
                    Color.Honeydew;
            }
            else
            {
                gridRow.Cells[ColumnCurrentBomPartNumber].Style.BackColor =
                    Color.LemonChiffon;
            }

            if (row.IsDerivedConfiguration)
            {
                gridRow.Cells[ColumnDerived].Style.BackColor = Color.Gainsboro;
            }
        }

        private void Grid_CurrentCellDirtyStateChanged(
            object sender,
            EventArgs e)
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellValueChanged(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (suppressGridEvents)
                return;

            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                return;

            DataGridViewRow gridRow = grid.Rows[e.RowIndex];
            ConfigurationPartNumberRow row =
                gridRow.Tag as ConfigurationPartNumberRow;

            if (row == null)
                return;

            ReadGridRowToModel(gridRow, row);

            if (e.ColumnIndex == ColumnNewPartNumber)
            {
                if (!string.IsNullOrWhiteSpace(row.NewPartNumber))
                    row.Apply = true;
            }

            row.Status = "Ready";
            WriteGridRowFromModel(gridRow, row);
            UpdateStatus();
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void UseNextNumberButton_Click(object sender, EventArgs e)
        {
            int nextNumber =
                ConfigurationPartNumberCommand.FindNextNumber(
                    rows,
                    prefixTextBox.Text,
                    suffixTextBox.Text);

            startNumberTextBox.Text = nextNumber.ToString();
            statusLabel.Text =
                "Start number set to max existing + 1: " +
                nextNumber.ToString();
        }

        private void FillCheckedButton_Click(object sender, EventArgs e)
        {
            FillRows(FillMode.CheckedRows);
        }

        private void FillBlankButton_Click(object sender, EventArgs e)
        {
            FillRows(FillMode.BlankRowsOnly);
        }

        private void FillAllButton_Click(object sender, EventArgs e)
        {
            FillRows(FillMode.AllRows);
        }

        private void FillRows(FillMode fillMode)
        {
            int startNumber;
            int digits;

            if (!int.TryParse(startNumberTextBox.Text.Trim(), out startNumber))
            {
                MessageBox.Show(
                    "Enter a valid numeric start number.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(digitsTextBox.Text.Trim(), out digits))
            {
                digits = 4;
                digitsTextBox.Text = "4";
            }

            int number = startNumber;
            int filled = 0;

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row == null)
                    continue;

                if (row.IsDerivedConfiguration &&
                    !includeDerivedWhenFillingCheckBox.Checked)
                {
                    continue;
                }

                if (fillMode == FillMode.CheckedRows && !row.Apply)
                    continue;

                if (fillMode == FillMode.BlankRowsOnly &&
                    !string.IsNullOrWhiteSpace(row.CurrentBomPartNumber))
                {
                    continue;
                }

                if (fillMode == FillMode.BlankRowsOnly &&
                    !string.IsNullOrWhiteSpace(row.NewPartNumber))
                {
                    continue;
                }

                row.NewPartNumber =
                    ConfigurationPartNumberCommand.FormatPartNumber(
                        number,
                        digits,
                        prefixTextBox.Text,
                        suffixTextBox.Text);

                row.Apply = true;
                row.Status = "Ready";
                number++;
                filled++;
            }

            ReloadGridFromRows();

            statusLabel.Text =
                "Filled " + filled.ToString() +
                " configuration row(s). Review the New part number column before applying.";
        }

        private void CheckAllButton_Click(object sender, EventArgs e)
        {
            foreach (ConfigurationPartNumberRow row in rows)
            {
                row.Apply = true;
            }

            ReloadGridFromRows();
        }

        private void UncheckAllButton_Click(object sender, EventArgs e)
        {
            foreach (ConfigurationPartNumberRow row in rows)
            {
                row.Apply = false;
            }

            ReloadGridFromRows();
        }

        private void CheckBlankOnlyButton_Click(object sender, EventArgs e)
        {
            foreach (ConfigurationPartNumberRow row in rows)
            {
                row.Apply =
                    string.IsNullOrWhiteSpace(row.CurrentBomPartNumber);
            }

            ReloadGridFromRows();
        }

        private void SortByNameButton_Click(object sender, EventArgs e)
        {
            rows.Sort(
                delegate(
                    ConfigurationPartNumberRow left,
                    ConfigurationPartNumberRow right)
                {
                    return string.Compare(
                        left.ConfigurationName,
                        right.ConfigurationName,
                        StringComparison.OrdinalIgnoreCase);
                });

            ReloadGridFromRows();
        }

        private void RestoreOriginalOrderButton_Click(object sender, EventArgs e)
        {
            rows.Sort(
                delegate(
                    ConfigurationPartNumberRow left,
                    ConfigurationPartNumberRow right)
                {
                    return left.OriginalIndex.CompareTo(right.OriginalIndex);
                });

            ReloadGridFromRows();
        }

        private void MoveSelectedUpButton_Click(object sender, EventArgs e)
        {
            MoveSelectedRows(-1);
        }

        private void MoveSelectedDownButton_Click(object sender, EventArgs e)
        {
            MoveSelectedRows(1);
        }

        private void MoveSelectedRows(int direction)
        {
            if (grid.SelectedRows.Count == 0)
                return;

            List<int> selectedIndexes = GetSelectedRowIndexes();

            if (direction < 0)
            {
                selectedIndexes.Sort();
            }
            else
            {
                selectedIndexes.Sort();
                selectedIndexes.Reverse();
            }

            foreach (int index in selectedIndexes)
            {
                int newIndex = index + direction;

                if (newIndex < 0 || newIndex >= rows.Count)
                    continue;

                ConfigurationPartNumberRow temp = rows[index];
                rows[index] = rows[newIndex];
                rows[newIndex] = temp;
            }

            ReloadGridFromRows();

            foreach (int index in selectedIndexes)
            {
                int newIndex = index + direction;

                if (newIndex >= 0 && newIndex < grid.Rows.Count)
                {
                    grid.Rows[newIndex].Selected = true;
                }
            }
        }

        private List<int> GetSelectedRowIndexes()
        {
            List<int> indexes = new List<int>();

            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (row.Index >= 0 && row.Index < rows.Count)
                    indexes.Add(row.Index);
            }

            return indexes;
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshRowsFromDocument();
        }

        private void ApplyCheckedButton_Click(object sender, EventArgs e)
        {
            ApplyRows(false);
        }

        private void ApplyAllButton_Click(object sender, EventArgs e)
        {
            foreach (ConfigurationPartNumberRow row in rows)
            {
                row.Apply = true;
            }

            ReloadGridFromRows();
            ApplyRows(true);
        }

        private void ApplyRows(bool allRowsRequested)
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                ConfigurationPartNumberRow row =
                    gridRow.Tag as ConfigurationPartNumberRow;

                if (row != null)
                {
                    ReadGridRowToModel(gridRow, row);
                }
            }

            int applyCount = 0;

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (row.Apply)
                    applyCount++;
            }

            if (applyCount == 0)
            {
                MessageBox.Show(
                    "No configuration rows are checked for update.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Apply configuration part numbers to " +
                applyCount.ToString() +
                " configuration row(s)?\r\n\r\n" +
                "The SOLIDWORKS document will be modified but not saved automatically.",
                "Cabin Tools - Confirm Configuration Part Numbers",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmation != DialogResult.Yes)
                return;

            try
            {
                ConfigurationPartNumberWriteReport report =
                    ConfigurationPartNumberCommand.ApplyRows(
                        modelDoc,
                        rows,
                        allowOverwriteCheckBox.Checked);

                ReloadGridFromRows();

                string message =
                    "Configuration part-number update completed.\r\n\r\n" +
                    "Updated: " + report.UpdatedRows.Count.ToString() + "\r\n" +
                    "Skipped: " + report.SkippedRows.Count.ToString() + "\r\n" +
                    "Failed: " + report.FailedRows.Count.ToString();

                if (!string.IsNullOrWhiteSpace(report.ReportPath))
                {
                    message += "\r\n\r\nReport:\r\n" + report.ReportPath;
                }

                message +=
                    "\r\n\r\nReview the configurations, then save the SOLIDWORKS file manually.";

                MessageBox.Show(
                    message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    report.FailedRows.Count > 0
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Configuration part-number update failed.\r\n\r\n" +
                    ex.Message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void UpdateStatus()
        {
            int assigned = 0;
            int blank = 0;
            int checkedCount = 0;

            foreach (ConfigurationPartNumberRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.CurrentBomPartNumber))
                    assigned++;
                else
                    blank++;

                if (row.Apply)
                    checkedCount++;
            }

            statusLabel.Text =
                "Configurations: " + rows.Count.ToString() +
                " | Existing BOM part numbers: " + assigned.ToString() +
                " | Blank/unassigned: " + blank.ToString() +
                " | Checked: " + checkedCount.ToString();
        }

        private static Label CreateLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(6, 6, 2, 2);
            return label;
        }

        private static TextBox CreateTextBox(int width, string text)
        {
            TextBox textBox = new TextBox();
            textBox.Width = width;
            textBox.Text = text ?? string.Empty;
            textBox.Margin = new Padding(2, 3, 8, 2);
            return textBox;
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;

            if (handler != null)
                button.Click += handler;

            return button;
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }

        private enum FillMode
        {
            CheckedRows,
            BlankRowsOnly,
            AllRows
        }
    }
}
