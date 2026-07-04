
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace SolidDNA
{
    /// <summary>
    /// Allows the user to select a persistent local default folder for
    /// SOLIDWORKS sheet formats and apply one or more .slddrt files.
    /// </summary>
    public sealed class SheetFormatForm : Form
    {
        private const string SettingsFolderName =
            "CabinTools";

        private const string SettingsFileName =
            "SheetFormatSettings.txt";

        private readonly List<SheetFormatAssignment> assignments;

        private RadioButton allSheetsRadioButton;
        private RadioButton individualSheetsRadioButton;
        private TextBox defaultFolderTextBox;
        private Button selectDefaultFolderButton;
        private TextBox allSheetsTemplateTextBox;
        private Button browseAllSheetsButton;
        private DataGridView sheetGrid;
        private TextBox warningTextBox;
        private Button applyButton;
        private Button cancelButton;

        private string defaultSheetFormatFolder;

        public bool ApplyOneFormatToAllSheets
        {
            get
            {
                return allSheetsRadioButton.Checked;
            }
        }

        public string AllSheetsTemplatePath
        {
            get
            {
                return allSheetsTemplateTextBox.Text.Trim();
            }
        }

        public List<SheetFormatAssignment> Assignments
        {
            get
            {
                return assignments;
            }
        }

        public SheetFormatForm(
            List<SheetFormatAssignment> sourceAssignments)
        {
            if (sourceAssignments == null)
            {
                throw new ArgumentNullException(
                    "sourceAssignments");
            }

            assignments =
                CopyAssignments(sourceAssignments);

            defaultSheetFormatFolder =
                LoadDefaultSheetFormatFolder();

            BuildForm();
            LoadRows();
            UpdateMode();
            UpdateDefaultFolderDisplay();
        }

        private static List<SheetFormatAssignment>
            CopyAssignments(
                List<SheetFormatAssignment> sourceAssignments)
        {
            List<SheetFormatAssignment> copied =
                new List<SheetFormatAssignment>();

            foreach (SheetFormatAssignment assignment in
                sourceAssignments)
            {
                copied.Add(
                    new SheetFormatAssignment
                    {
                        SheetName = assignment.SheetName,
                        CurrentTemplatePath =
                            assignment.CurrentTemplatePath,
                        NewTemplatePath =
                            assignment.NewTemplatePath
                    });
            }

            return copied;
        }

        private void BuildForm()
        {
            Text = "Apply Drawing Sheet Format";
            Width = 1000;
            Height = 650;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 6;
            mainLayout.Padding = new Padding(10);
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 95));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            Controls.Add(mainLayout);

            GroupBox defaultFolderGroupBox =
                new GroupBox();

            defaultFolderGroupBox.Text =
                "Default sheet format folder (saved locally for this Windows user)";
            defaultFolderGroupBox.Dock = DockStyle.Fill;
            defaultFolderGroupBox.Height = 78;

            TableLayoutPanel defaultFolderLayout =
                new TableLayoutPanel();

            defaultFolderLayout.Dock = DockStyle.Fill;
            defaultFolderLayout.ColumnCount = 2;
            defaultFolderLayout.RowCount = 1;
            defaultFolderLayout.Padding = new Padding(8);
            defaultFolderLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            defaultFolderLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 150));

            defaultFolderTextBox =
                new TextBox();

            defaultFolderTextBox.Dock = DockStyle.Fill;
            defaultFolderTextBox.ReadOnly = true;

            selectDefaultFolderButton =
                new Button();

            selectDefaultFolderButton.Text =
                "Select Folder...";
            selectDefaultFolderButton.Dock = DockStyle.Fill;
            selectDefaultFolderButton.Click +=
                SelectDefaultFolderButton_Click;

            defaultFolderLayout.Controls.Add(
                defaultFolderTextBox,
                0,
                0);

            defaultFolderLayout.Controls.Add(
                selectDefaultFolderButton,
                1,
                0);

            defaultFolderGroupBox.Controls.Add(
                defaultFolderLayout);

            mainLayout.Controls.Add(
                defaultFolderGroupBox,
                0,
                0);

            GroupBox modeGroupBox =
                new GroupBox();

            modeGroupBox.Text = "Apply mode";
            modeGroupBox.Dock = DockStyle.Fill;
            modeGroupBox.Height = 75;

            FlowLayoutPanel modePanel =
                new FlowLayoutPanel();

            modePanel.Dock = DockStyle.Fill;
            modePanel.FlowDirection =
                FlowDirection.LeftToRight;
            modePanel.WrapContents = false;
            modePanel.Padding = new Padding(8);

            allSheetsRadioButton =
                new RadioButton();

            allSheetsRadioButton.Text =
                "Apply one selected sheet format to all sheets";

            allSheetsRadioButton.Width = 330;
            allSheetsRadioButton.Checked = true;
            allSheetsRadioButton.CheckedChanged +=
                ModeChanged;

            individualSheetsRadioButton =
                new RadioButton();

            individualSheetsRadioButton.Text =
                "Select a sheet format individually for each sheet";

            individualSheetsRadioButton.Width = 350;
            individualSheetsRadioButton.CheckedChanged +=
                ModeChanged;

            modePanel.Controls.Add(allSheetsRadioButton);
            modePanel.Controls.Add(individualSheetsRadioButton);

            modeGroupBox.Controls.Add(modePanel);

            mainLayout.Controls.Add(
                modeGroupBox,
                0,
                1);

            GroupBox allSheetsGroupBox =
                new GroupBox();

            allSheetsGroupBox.Text =
                "New sheet format for all sheets";
            allSheetsGroupBox.Dock = DockStyle.Fill;
            allSheetsGroupBox.Height = 78;

            TableLayoutPanel allSheetsLayout =
                new TableLayoutPanel();

            allSheetsLayout.Dock = DockStyle.Fill;
            allSheetsLayout.ColumnCount = 2;
            allSheetsLayout.RowCount = 1;
            allSheetsLayout.Padding = new Padding(8);
            allSheetsLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            allSheetsLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 120));

            allSheetsTemplateTextBox =
                new TextBox();

            allSheetsTemplateTextBox.Dock = DockStyle.Fill;
            allSheetsTemplateTextBox.ReadOnly = true;

            browseAllSheetsButton =
                new Button();

            browseAllSheetsButton.Text = "Browse...";
            browseAllSheetsButton.Dock = DockStyle.Fill;
            browseAllSheetsButton.Click +=
                BrowseAllSheetsButton_Click;

            allSheetsLayout.Controls.Add(
                allSheetsTemplateTextBox,
                0,
                0);

            allSheetsLayout.Controls.Add(
                browseAllSheetsButton,
                1,
                0);

            allSheetsGroupBox.Controls.Add(allSheetsLayout);

            mainLayout.Controls.Add(
                allSheetsGroupBox,
                0,
                2);

            sheetGrid =
                new DataGridView();

            sheetGrid.Dock = DockStyle.Fill;
            sheetGrid.AllowUserToAddRows = false;
            sheetGrid.AllowUserToDeleteRows = false;
            sheetGrid.AllowUserToResizeRows = false;
            sheetGrid.RowHeadersVisible = false;
            sheetGrid.SelectionMode =
                DataGridViewSelectionMode.FullRowSelect;
            sheetGrid.MultiSelect = false;
            sheetGrid.AutoSizeColumnsMode =
                DataGridViewAutoSizeColumnsMode.Fill;
            sheetGrid.CellContentClick +=
                SheetGrid_CellContentClick;

            DataGridViewTextBoxColumn sheetColumn =
                new DataGridViewTextBoxColumn();

            sheetColumn.HeaderText = "Sheet";
            sheetColumn.ReadOnly = true;
            sheetColumn.FillWeight = 20;

            DataGridViewTextBoxColumn currentColumn =
                new DataGridViewTextBoxColumn();

            currentColumn.HeaderText =
                "Current sheet format";
            currentColumn.ReadOnly = true;
            currentColumn.FillWeight = 38;

            DataGridViewTextBoxColumn newColumn =
                new DataGridViewTextBoxColumn();

            newColumn.HeaderText =
                "New replacement sheet format";
            newColumn.ReadOnly = true;
            newColumn.FillWeight = 42;

            DataGridViewButtonColumn browseColumn =
                new DataGridViewButtonColumn();

            browseColumn.HeaderText = "Select";
            browseColumn.Text = "Browse...";
            browseColumn.UseColumnTextForButtonValue = true;
            browseColumn.FillWeight = 12;

            sheetGrid.Columns.Add(sheetColumn);
            sheetGrid.Columns.Add(currentColumn);
            sheetGrid.Columns.Add(newColumn);
            sheetGrid.Columns.Add(browseColumn);

            mainLayout.Controls.Add(
                sheetGrid,
                0,
                3);

            warningTextBox =
                new TextBox();

            warningTextBox.Dock = DockStyle.Fill;
            warningTextBox.Multiline = true;
            warningTextBox.ReadOnly = true;
            warningTextBox.ScrollBars =
                ScrollBars.Vertical;
            warningTextBox.Text =
                "Important:\r\n" +
                "The selected .slddrt replaces the previous sheet format. Existing sheet-format note changes are not retained.\r\n\r\n" +
                "When 'Apply one selected sheet format to all sheets' is selected, the individual sheet selection table is disabled intentionally.\r\n\r\n" +
                "The drawing is rebuilt but is not saved automatically. Test first on a copied drawing or checked-out test drawing.";

            mainLayout.Controls.Add(
                warningTextBox,
                0,
                4);

            FlowLayoutPanel buttonPanel =
                new FlowLayoutPanel();

            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection =
                FlowDirection.RightToLeft;
            buttonPanel.Height = 42;

            applyButton =
                new Button();

            applyButton.Text = "Apply";
            applyButton.Width = 100;
            applyButton.Click +=
                ApplyButton_Click;

            cancelButton =
                new Button();

            cancelButton.Text = "Cancel";
            cancelButton.Width = 100;
            cancelButton.Click +=
                CancelButton_Click;

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(applyButton);

            mainLayout.Controls.Add(
                buttonPanel,
                0,
                5);
        }

        private void LoadRows()
        {
            sheetGrid.Rows.Clear();

            foreach (SheetFormatAssignment assignment in
                assignments)
            {
                sheetGrid.Rows.Add(
                    assignment.SheetName,
                    assignment.CurrentTemplatePath,
                    assignment.NewTemplatePath,
                    "Browse...");
            }
        }

        private void ModeChanged(
            object sender,
            EventArgs e)
        {
            UpdateMode();
        }

        private void UpdateMode()
        {
            bool applyOneToAll =
                allSheetsRadioButton.Checked;

            // Requirement: when one format is selected for all sheets,
            // individual per-sheet selection is visibly disabled/greyed out.
            sheetGrid.Enabled = !applyOneToAll;

            allSheetsTemplateTextBox.Enabled =
                applyOneToAll;

            browseAllSheetsButton.Enabled =
                applyOneToAll;
        }

        private void SelectDefaultFolderButton_Click(
            object sender,
            EventArgs e)
        {
            SelectAndSaveDefaultSheetFormatFolder();
        }

        private void BrowseAllSheetsButton_Click(
            object sender,
            EventArgs e)
        {
            if (!EnsureDefaultFolderSelected())
            {
                return;
            }

            string selectedPath =
                SelectSheetFormatFile(
                    allSheetsTemplateTextBox.Text);

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                allSheetsTemplateTextBox.Text =
                    selectedPath;
            }
        }

        private void SheetGrid_CellContentClick(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (!individualSheetsRadioButton.Checked)
            {
                return;
            }

            if (e.RowIndex < 0 ||
                e.ColumnIndex != 3)
            {
                return;
            }

            if (!EnsureDefaultFolderSelected())
            {
                return;
            }

            DataGridViewRow row =
                sheetGrid.Rows[e.RowIndex];

            string existingPath =
                Convert.ToString(
                    row.Cells[2].Value);

            string selectedPath =
                SelectSheetFormatFile(existingPath);

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            row.Cells[2].Value = selectedPath;

            assignments[e.RowIndex].NewTemplatePath =
                selectedPath;
        }

        private bool EnsureDefaultFolderSelected()
        {
            if (!string.IsNullOrWhiteSpace(
                defaultSheetFormatFolder) &&
                Directory.Exists(
                    defaultSheetFormatFolder))
            {
                return true;
            }

            MessageBox.Show(
                "Select the default folder that contains your SOLIDWORKS sheet format files (.slddrt).\n\n" +
                "Cabin Tools stores this folder locally for this Windows user and reuses it until you change it.",
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return SelectAndSaveDefaultSheetFormatFolder();
        }

        private bool SelectAndSaveDefaultSheetFormatFolder()
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                dialog.Description =
                    "Select the default folder containing SOLIDWORKS sheet format files (.slddrt)";

                if (!string.IsNullOrWhiteSpace(
                    defaultSheetFormatFolder) &&
                    Directory.Exists(
                        defaultSheetFormatFolder))
                {
                    dialog.SelectedPath =
                        defaultSheetFormatFolder;
                }

                if (dialog.ShowDialog() !=
                    DialogResult.OK)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(
                    dialog.SelectedPath) ||
                    !Directory.Exists(
                        dialog.SelectedPath))
                {
                    return false;
                }

                defaultSheetFormatFolder =
                    dialog.SelectedPath;

                SaveDefaultSheetFormatFolder(
                    defaultSheetFormatFolder);

                UpdateDefaultFolderDisplay();

                return true;
            }
        }

        private void UpdateDefaultFolderDisplay()
        {
            if (string.IsNullOrWhiteSpace(
                defaultSheetFormatFolder) ||
                !Directory.Exists(
                    defaultSheetFormatFolder))
            {
                defaultFolderTextBox.Text =
                    "No default sheet format folder selected.";

                return;
            }

            defaultFolderTextBox.Text =
                defaultSheetFormatFolder;
        }

        private string SelectSheetFormatFile(
            string currentPath)
        {
            using (OpenFileDialog dialog =
                new OpenFileDialog())
            {
                dialog.Title =
                    "Select SOLIDWORKS Sheet Format";

                dialog.Filter =
                    "SOLIDWORKS sheet format (*.slddrt)|*.slddrt";

                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory =
                    GetInitialFolder(currentPath);

                if (dialog.ShowDialog() !=
                    DialogResult.OK)
                {
                    return string.Empty;
                }

                return dialog.FileName;
            }
        }

        private string GetInitialFolder(
            string currentPath)
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                string currentFolder =
                    Path.GetDirectoryName(currentPath);

                if (!string.IsNullOrWhiteSpace(
                    currentFolder) &&
                    Directory.Exists(currentFolder))
                {
                    return currentFolder;
                }
            }

            if (!string.IsNullOrWhiteSpace(
                defaultSheetFormatFolder) &&
                Directory.Exists(
                    defaultSheetFormatFolder))
            {
                return defaultSheetFormatFolder;
            }

            return System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.MyDocuments);
        }

        private void ApplyButton_Click(
            object sender,
            EventArgs e)
        {
            if (!ValidateSelections())
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(
            object sender,
            EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private bool ValidateSelections()
        {
            if (ApplyOneFormatToAllSheets)
            {
                if (string.IsNullOrWhiteSpace(
                    AllSheetsTemplatePath))
                {
                    ShowValidationError(
                        "Select one .slddrt file to replace the sheet format on all sheets.");

                    return false;
                }

                if (!File.Exists(
                    AllSheetsTemplatePath))
                {
                    ShowValidationError(
                        "The selected .slddrt sheet format file does not exist.");

                    return false;
                }

                foreach (SheetFormatAssignment assignment in
                    assignments)
                {
                    assignment.NewTemplatePath =
                        AllSheetsTemplatePath;
                }

                return true;
            }

            for (int rowIndex = 0;
                 rowIndex < sheetGrid.Rows.Count;
                 rowIndex++)
            {
                string sheetName =
                    Convert.ToString(
                        sheetGrid.Rows[rowIndex]
                            .Cells[0].Value);

                string newPath =
                    Convert.ToString(
                        sheetGrid.Rows[rowIndex]
                            .Cells[2].Value);

                if (string.IsNullOrWhiteSpace(newPath))
                {
                    ShowValidationError(
                        "Select a .slddrt file for sheet: " +
                        sheetName);

                    return false;
                }

                if (!File.Exists(newPath))
                {
                    ShowValidationError(
                        "The selected .slddrt file does not exist for sheet: " +
                        sheetName);

                    return false;
                }

                assignments[rowIndex].NewTemplatePath =
                    newPath;
            }

            return true;
        }

        private static string LoadDefaultSheetFormatFolder()
        {
            try
            {
                string settingsFilePath =
                    GetSettingsFilePath();

                if (!File.Exists(settingsFilePath))
                {
                    return string.Empty;
                }

                string savedFolder =
                    File.ReadAllText(
                        settingsFilePath)
                        .Trim();

                return Directory.Exists(savedFolder)
                    ? savedFolder
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SaveDefaultSheetFormatFolder(
            string folderPath)
        {
            try
            {
                string settingsFilePath =
                    GetSettingsFilePath();

                string directory =
                    Path.GetDirectoryName(
                        settingsFilePath);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    settingsFilePath,
                    folderPath ?? string.Empty);
            }
            catch
            {
                // Persisting the local default folder must never block the tool.
            }
        }

        private static string GetSettingsFilePath()
        {
            string folder =
                Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder
                            .LocalApplicationData),
                    SettingsFolderName);

            return Path.Combine(
                folder,
                SettingsFileName);
        }

        private void ShowValidationError(string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
