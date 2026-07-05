using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SolidDNA
{
    /// <summary>
    /// Multi-drawing PDF export user interface.
    ///
    /// Automatic mode reads five drawing properties when the user explicitly
    /// refreshes them or begins automatic export. Adding files itself never
    /// opens drawings in SOLIDWORKS. Manual mode can export using a typed PDF
    /// name even when automatic naming properties are incomplete.
    /// </summary>
    internal sealed class PdfBatchExportForm : Form
    {
        private readonly ISldWorks swApp;
        private readonly PdfExportNamingMode namingMode;
        private readonly BindingList<PdfBatchExportItem> drawingItems;

        private PdfBatchExportUserPreferences userPreferences;
        private bool suppressPreferenceEvents;
        private bool folderActionPending;

        private DataGridView drawingsGrid;
        private DataGridViewTextBoxColumn outputFolderColumn;
        private DataGridViewTextBoxColumn statusColumn;

        private TextBox batchOutputFolderTextBox;
        private ComboBox existingPdfBehaviorComboBox;
        private CheckBox leaveDrawingsOpenCheckBox;
        private CheckBox openPdfAfterExportCheckBox;
        private RadioButton commonFolderModeRadioButton;
        private RadioButton individualFoldersModeRadioButton;
        private Label folderModeHintLabel;
        private Button browseBatchFolderButton;
        private Button applyFolderToCheckedButton;
        private Button applyFolderToAllButton;
        private Button useSourceFoldersButton;
        private Button chooseFolderForSelectedRowsButton;
        private Button exportCheckedButton;
        private Button exportAllValidButton;
        private Label statusLabel;

        public IList<PdfBatchExportItem> DrawingItems
        {
            get
            {
                return new List<PdfBatchExportItem>(drawingItems);
            }
        }

        public PdfBatchExportOptions ExportOptions { get; private set; }

        public PdfBatchExportForm(
            ISldWorks swApp,
            PdfExportNamingMode namingMode,
            string initialActiveDrawingPath)
        {
            this.swApp = swApp;
            this.namingMode = namingMode;
            drawingItems = new BindingList<PdfBatchExportItem>();
            userPreferences = PdfBatchExportUserPreferences.Load();

            BuildUserInterface();
            LoadUserPreferencesIntoForm();

            if (!string.IsNullOrWhiteSpace(initialActiveDrawingPath))
            {
                AddDrawingPath(initialActiveDrawingPath);
            }

            UpdateFormStatus();
        }

        private PdfOutputFolderMode FolderMode
        {
            get
            {
                return commonFolderModeRadioButton != null &&
                       commonFolderModeRadioButton.Checked
                    ? PdfOutputFolderMode.CommonFolder
                    : PdfOutputFolderMode.IndividualFolders;
            }
        }

        private void BuildUserInterface()
        {
            Text = namingMode ==
                PdfExportNamingMode.AutomaticFromProperties
                ? "Cabin Tools - Batch PDF Export (Automatic Names)"
                : "Cabin Tools - Batch PDF Export (Manual Names)";

            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 640);
            Width = 1440;
            Height = 820;
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildIntroductionPanel(), 0, 0);
            root.Controls.Add(BuildDrawingActionsPanel(), 0, 1);
            root.Controls.Add(BuildOutputAndOptionsPanel(), 0, 2);

            drawingsGrid = BuildDrawingsGrid();
            root.Controls.Add(drawingsGrid, 0, 3);

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(0, 8, 0, 8);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(statusLabel, 0, 4);

            root.Controls.Add(BuildBottomButtonPanel(), 0, 5);

            Controls.Add(root);
        }

        private Control BuildIntroductionPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;

            Label heading = new Label();
            heading.AutoSize = true;
            heading.Font = new Font(Font, FontStyle.Bold);
            heading.Text = namingMode ==
                PdfExportNamingMode.AutomaticFromProperties
                ? "Automatic PDF naming from drawing custom properties"
                : "Manual PDF naming";

            Label details = new Label();
            details.AutoSize = true;
            details.MaximumSize = new Size(1320, 0);
            details.Top = heading.Bottom + 4;
            details.Left = 0;
            details.Text = namingMode ==
                PdfExportNamingMode.AutomaticFromProperties
                ? "The automatic filename uses DrwNumber, Revision, Cabin type description, Cabin type defined, and Layout type. Missing values are shown in the Status column as ! text on a red background. Adding drawings does not open them; click Refresh properties or begin export to read their custom properties."
                : "Enter a PDF filename for each row. The Status column still reports incomplete automatic naming properties when they have been read, but those values do not block manual export.";

            panel.Controls.Add(heading);
            panel.Controls.Add(details);

            return panel;
        }

        private Control BuildDrawingActionsPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;
            panel.WrapContents = true;
            panel.Margin = new Padding(0, 8, 0, 8);

            Button addDrawingsButton = CreateButton(
                "Add drawings...",
                AddDrawingsButton_Click);

            Button addActiveDrawingButton = CreateButton(
                "Add active drawing",
                AddActiveDrawingButton_Click);

            Button removeSelectedRowsButton = CreateButton(
                "Remove selected rows",
                RemoveSelectedRowsButton_Click);

            Button clearListButton = CreateButton(
                "Clear list",
                ClearListButton_Click);

            Button refreshPropertiesButton = CreateButton(
                "Refresh properties",
                RefreshPropertiesButton_Click);

            Button selectReadyButton = CreateButton(
                "Select ready rows",
                SelectReadyButton_Click);

            Button clearExportSelectionButton = CreateButton(
                "Clear export selection",
                ClearExportSelectionButton_Click);

            panel.Controls.Add(addDrawingsButton);
            panel.Controls.Add(addActiveDrawingButton);
            panel.Controls.Add(removeSelectedRowsButton);
            panel.Controls.Add(clearListButton);
            panel.Controls.Add(refreshPropertiesButton);
            panel.Controls.Add(selectReadyButton);
            panel.Controls.Add(clearExportSelectionButton);

            return panel;
        }

        private Control BuildOutputAndOptionsPanel()
        {
            GroupBox group = new GroupBox();
            group.Text = "Output folders and export behaviour";
            group.Dock = DockStyle.Top;
            group.AutoSize = true;
            group.Padding = new Padding(10);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.ColumnCount = 6;
            layout.RowCount = 5;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel folderModePanel = new FlowLayoutPanel();
            folderModePanel.AutoSize = true;
            folderModePanel.WrapContents = true;
            folderModePanel.Dock = DockStyle.Fill;

            commonFolderModeRadioButton = new RadioButton();
            commonFolderModeRadioButton.AutoSize = true;
            commonFolderModeRadioButton.Text = "One common folder for all drawings";
            commonFolderModeRadioButton.CheckedChanged += FolderModeRadioButton_CheckedChanged;

            individualFoldersModeRadioButton = new RadioButton();
            individualFoldersModeRadioButton.AutoSize = true;
            individualFoldersModeRadioButton.Text = "Different folders per drawing";
            individualFoldersModeRadioButton.CheckedChanged += FolderModeRadioButton_CheckedChanged;

            folderModePanel.Controls.Add(commonFolderModeRadioButton);
            folderModePanel.Controls.Add(individualFoldersModeRadioButton);

            layout.SetColumnSpan(folderModePanel, 6);
            layout.Controls.Add(folderModePanel, 0, 0);

            Label outputFolderLabel = new Label();
            outputFolderLabel.Text = "Common output folder:";
            outputFolderLabel.AutoSize = true;
            outputFolderLabel.Anchor = AnchorStyles.Left;

            batchOutputFolderTextBox = new TextBox();
            batchOutputFolderTextBox.Dock = DockStyle.Fill;
            batchOutputFolderTextBox.TextChanged += BatchOutputFolderTextBox_TextChanged;

            browseBatchFolderButton = CreateButton(
                "Browse...",
                BrowseFolderButton_Click);

            applyFolderToCheckedButton = CreateButton(
                "Apply to checked",
                ApplyFolderToCheckedButton_Click);

            applyFolderToAllButton = CreateButton(
                "Apply to all",
                ApplyFolderToAllButton_Click);

            useSourceFoldersButton = CreateButton(
                "Use source folders",
                UseSourceFoldersButton_Click);

            layout.Controls.Add(outputFolderLabel, 0, 1);
            layout.Controls.Add(batchOutputFolderTextBox, 1, 1);
            layout.Controls.Add(browseBatchFolderButton, 2, 1);
            layout.Controls.Add(applyFolderToCheckedButton, 3, 1);
            layout.Controls.Add(applyFolderToAllButton, 4, 1);
            layout.Controls.Add(useSourceFoldersButton, 5, 1);

            Label individualFolderLabel = new Label();
            individualFolderLabel.Text = "Individual folders:";
            individualFolderLabel.AutoSize = true;
            individualFolderLabel.Anchor = AnchorStyles.Left;

            folderModeHintLabel = new Label();
            folderModeHintLabel.AutoSize = true;
            folderModeHintLabel.Anchor = AnchorStyles.Left;

            chooseFolderForSelectedRowsButton = CreateButton(
                "Choose folder for selected row(s)...",
                ChooseFolderForSelectedRowsButton_Click);

            layout.Controls.Add(individualFolderLabel, 0, 2);
            layout.Controls.Add(folderModeHintLabel, 1, 2);
            layout.SetColumnSpan(chooseFolderForSelectedRowsButton, 3);
            layout.Controls.Add(chooseFolderForSelectedRowsButton, 3, 2);

            Label existingLabel = new Label();
            existingLabel.Text = "Existing PDFs:";
            existingLabel.AutoSize = true;
            existingLabel.Anchor = AnchorStyles.Left;

            existingPdfBehaviorComboBox = new ComboBox();
            existingPdfBehaviorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            existingPdfBehaviorComboBox.Width = 250;
            existingPdfBehaviorComboBox.Items.Add("Skip existing PDFs (safe default)");
            existingPdfBehaviorComboBox.Items.Add("Overwrite existing PDFs");
            existingPdfBehaviorComboBox.SelectedIndex = 0;

            leaveDrawingsOpenCheckBox = new CheckBox();
            leaveDrawingsOpenCheckBox.AutoSize = true;
            leaveDrawingsOpenCheckBox.Text =
                "Leave drawings opened by Cabin Tools open in SOLIDWORKS after export";
            leaveDrawingsOpenCheckBox.CheckedChanged += ExportPreferenceControl_Changed;

            layout.Controls.Add(existingLabel, 0, 3);
            layout.Controls.Add(existingPdfBehaviorComboBox, 1, 3);
            layout.SetColumnSpan(leaveDrawingsOpenCheckBox, 4);
            layout.Controls.Add(leaveDrawingsOpenCheckBox, 2, 3);

            openPdfAfterExportCheckBox = new CheckBox();
            openPdfAfterExportCheckBox.AutoSize = true;
            openPdfAfterExportCheckBox.Text = "Open exported PDFs after export";
            openPdfAfterExportCheckBox.CheckedChanged += ExportPreferenceControl_Changed;

            layout.SetColumnSpan(openPdfAfterExportCheckBox, 5);
            layout.Controls.Add(openPdfAfterExportCheckBox, 1, 4);

            group.Controls.Add(layout);

            return group;
        }

        private DataGridView BuildDrawingsGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.DataSource = drawingItems;
            grid.CellEndEdit += DrawingsGrid_CellEndEdit;
            grid.CellContentClick += DrawingsGrid_CellContentClick;
            grid.CellFormatting += DrawingsGrid_CellFormatting;
            grid.CurrentCellDirtyStateChanged += DrawingsGrid_CurrentCellDirtyStateChanged;
            grid.CellMouseEnter += DrawingsGrid_CellMouseEnter;
            grid.DataError += DrawingsGrid_DataError;

            DataGridViewCheckBoxColumn exportColumn =
                new DataGridViewCheckBoxColumn();
            exportColumn.DataPropertyName = "SelectedForExport";
            exportColumn.HeaderText = "Export";
            exportColumn.Width = 54;
            exportColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

            DataGridViewTextBoxColumn drawingColumn =
                new DataGridViewTextBoxColumn();
            drawingColumn.DataPropertyName = "DrawingName";
            drawingColumn.HeaderText = "Drawing";
            drawingColumn.Width = 220;
            drawingColumn.ReadOnly = true;

            DataGridViewTextBoxColumn fileNameColumn =
                new DataGridViewTextBoxColumn();

            if (namingMode == PdfExportNamingMode.AutomaticFromProperties)
            {
                fileNameColumn.DataPropertyName = "AutoFileName";
                fileNameColumn.HeaderText = "Automatic PDF filename";
                fileNameColumn.ReadOnly = true;
            }
            else
            {
                fileNameColumn.DataPropertyName = "ManualFileName";
                fileNameColumn.HeaderText = "PDF filename";
                fileNameColumn.ReadOnly = false;
            }

            fileNameColumn.Width = 300;

            outputFolderColumn = new DataGridViewTextBoxColumn();
            outputFolderColumn.DataPropertyName = "OutputFolder";
            outputFolderColumn.HeaderText = "Output folder";
            outputFolderColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            outputFolderColumn.MinimumWidth = 230;
            outputFolderColumn.ReadOnly = true;

            DataGridViewButtonColumn folderButtonColumn =
                new DataGridViewButtonColumn();
            folderButtonColumn.Name = "ChooseOutputFolder";
            folderButtonColumn.HeaderText = "Folder";
            folderButtonColumn.Text = "Choose...";
            folderButtonColumn.UseColumnTextForButtonValue = true;
            folderButtonColumn.Width = 78;

            statusColumn = new DataGridViewTextBoxColumn();
            statusColumn.DataPropertyName = "Status";
            statusColumn.HeaderText = "Status";
            statusColumn.Width = 420;
            statusColumn.ReadOnly = true;

            grid.Columns.Add(exportColumn);
            grid.Columns.Add(drawingColumn);
            grid.Columns.Add(fileNameColumn);
            grid.Columns.Add(outputFolderColumn);
            grid.Columns.Add(folderButtonColumn);
            grid.Columns.Add(statusColumn);

            return grid;
        }

        private Control BuildBottomButtonPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.WrapContents = false;

            Button cancelButton = CreateButton("Cancel", null);
            cancelButton.DialogResult = DialogResult.Cancel;

            exportAllValidButton = CreateButton(
                namingMode == PdfExportNamingMode.AutomaticFromProperties
                    ? "Export all ready"
                    : "Export all valid names",
                ExportAllValidButton_Click);

            exportCheckedButton = CreateButton(
                "Export checked",
                ExportCheckedButton_Click);

            panel.Controls.Add(cancelButton);
            panel.Controls.Add(exportAllValidButton);
            panel.Controls.Add(exportCheckedButton);

            CancelButton = cancelButton;

            return panel;
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;

            if (handler != null)
            {
                button.Click += handler;
            }

            return button;
        }

        private void LoadUserPreferencesIntoForm()
        {
            suppressPreferenceEvents = true;

            try
            {
                leaveDrawingsOpenCheckBox.Checked = userPreferences.LeaveDrawingsOpen;
                openPdfAfterExportCheckBox.Checked = userPreferences.OpenExportedPdfFiles;

                string preferredFolder = userPreferences.LastBatchOutputFolder;

                batchOutputFolderTextBox.Text =
                    !string.IsNullOrWhiteSpace(preferredFolder) &&
                    Directory.Exists(preferredFolder)
                        ? preferredFolder
                        : System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder.MyDocuments);

                commonFolderModeRadioButton.Checked =
                    userPreferences.FolderMode ==
                    PdfOutputFolderMode.CommonFolder;

                individualFoldersModeRadioButton.Checked =
                    !commonFolderModeRadioButton.Checked;
            }
            finally
            {
                suppressPreferenceEvents = false;
            }

            UpdateFolderModeUi();
            SetFolderActionHighlight(false);
        }

        private void AddDrawingsButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog())
            {
                openDialog.Title = "Select SOLIDWORKS drawings for PDF export";
                openDialog.Filter = "SOLIDWORKS drawings (*.slddrw)|*.slddrw";
                openDialog.Multiselect = true;
                openDialog.CheckFileExists = true;
                openDialog.CheckPathExists = true;

                string currentFolder = batchOutputFolderTextBox.Text.Trim();

                if (Directory.Exists(currentFolder))
                {
                    openDialog.InitialDirectory = currentFolder;
                }

                if (openDialog.ShowDialog() != DialogResult.OK)
                    return;

                UseWaitCursor = true;

                try
                {
                    foreach (string drawingPath in openDialog.FileNames)
                    {
                        AddDrawingPath(drawingPath);
                    }
                }
                finally
                {
                    UseWaitCursor = false;
                }
            }

            UpdateFormStatus();
        }

        private void AddActiveDrawingButton_Click(object sender, EventArgs e)
        {
            string activeDrawingPath = PdfExportCommand.GetActiveSavedDrawingPath(swApp);

            if (string.IsNullOrWhiteSpace(activeDrawingPath))
            {
                MessageBox.Show(
                    "There is no saved drawing active in SOLIDWORKS.\r\n\r\nUse Add drawings... to select drawing files from disk.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            AddDrawingPath(activeDrawingPath);
            UpdateFormStatus();
        }

        private void RemoveSelectedRowsButton_Click(object sender, EventArgs e)
        {
            List<PdfBatchExportItem> itemsToRemove = GetSelectedGridItems();

            foreach (PdfBatchExportItem item in itemsToRemove)
            {
                drawingItems.Remove(item);
            }

            UpdateFormStatus();
        }

        private void ClearListButton_Click(object sender, EventArgs e)
        {
            if (drawingItems.Count == 0)
                return;

            DialogResult response = MessageBox.Show(
                "Remove all drawings from this PDF export list?",
                "Cabin Tools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (response != DialogResult.Yes)
                return;

            drawingItems.Clear();
            UpdateFormStatus();
        }

        private void RefreshPropertiesButton_Click(object sender, EventArgs e)
        {
            RefreshPropertiesForItems(drawingItems);
        }

        private void RefreshPropertiesForItems(
            IEnumerable<PdfBatchExportItem> items)
        {
            if (items == null)
                return;

            UseWaitCursor = true;

            try
            {
                foreach (PdfBatchExportItem item in items)
                {
                    PdfExportCommand.RefreshBatchItemPreflight(
                        swApp,
                        item,
                        namingMode);
                }
            }
            finally
            {
                UseWaitCursor = false;
            }

            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void SelectReadyButton_Click(object sender, EventArgs e)
        {
            foreach (PdfBatchExportItem item in drawingItems)
            {
                item.SelectedForExport =
                    string.IsNullOrWhiteSpace(
                        PdfExportCommand.GetExportBlockReason(
                            item,
                            namingMode));
            }

            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void ClearExportSelectionButton_Click(object sender, EventArgs e)
        {
            foreach (PdfBatchExportItem item in drawingItems)
            {
                item.SelectedForExport = false;
            }

            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            string folder = ShowFolderDialog(
                "Select one common PDF output folder",
                batchOutputFolderTextBox.Text.Trim());

            if (string.IsNullOrWhiteSpace(folder))
                return;

            batchOutputFolderTextBox.Text = folder;
            userPreferences.LastBatchOutputFolder = folder;
            SaveUserPreferences();
            SetFolderActionHighlight(true);
        }

        private void ApplyFolderToCheckedButton_Click(object sender, EventArgs e)
        {
            int checkedCount = 0;

            foreach (PdfBatchExportItem item in drawingItems)
            {
                if (item.SelectedForExport)
                    checkedCount++;
            }

            if (checkedCount == 0)
            {
                MessageBox.Show(
                    "Check one or more Export boxes first.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            ApplyBatchFolder(false);
        }

        private void ApplyFolderToAllButton_Click(object sender, EventArgs e)
        {
            ApplyBatchFolder(true);
        }

        private void ChooseFolderForSelectedRowsButton_Click(object sender, EventArgs e)
        {
            List<PdfBatchExportItem> selectedItems = GetSelectedGridItems();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more drawing rows first. Use Ctrl or Shift to select multiple rows.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            string initialFolder = selectedItems[0].OutputFolder;
            string folder = ShowFolderDialog(
                "Select an output folder for selected drawing row(s)",
                initialFolder);

            if (string.IsNullOrWhiteSpace(folder))
                return;

            foreach (PdfBatchExportItem item in selectedItems)
            {
                item.OutputFolder = folder;
                item.UpdateDisplayStatus(namingMode);
            }

            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void UseSourceFoldersButton_Click(object sender, EventArgs e)
        {
            suppressPreferenceEvents = true;

            try
            {
                individualFoldersModeRadioButton.Checked = true;
            }
            finally
            {
                suppressPreferenceEvents = false;
            }

            foreach (PdfBatchExportItem item in drawingItems)
            {
                item.OutputFolder = GetSourceDirectory(item.SourcePath);
                item.UpdateDisplayStatus(namingMode);
            }

            UpdateFolderModeUi();
            SetFolderActionHighlight(false);
            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void ExportCheckedButton_Click(object sender, EventArgs e)
        {
            BeginExport(PdfBatchExportScope.CheckedRows);
        }

        private void ExportAllValidButton_Click(object sender, EventArgs e)
        {
            BeginExport(PdfBatchExportScope.AllValidRows);
        }

        private void DrawingsGrid_CellEndEdit(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= drawingItems.Count)
                return;

            PdfBatchExportItem item = drawingItems[e.RowIndex];
            item.UpdateDisplayStatus(namingMode);

            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void DrawingsGrid_CellContentClick(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= drawingItems.Count)
                return;

            if (drawingsGrid.Columns[e.ColumnIndex].Name != "ChooseOutputFolder")
                return;

            if (FolderMode != PdfOutputFolderMode.IndividualFolders)
            {
                MessageBox.Show(
                    "Choose Different folders per drawing before assigning a folder to one row.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            PdfBatchExportItem item = drawingItems[e.RowIndex];
            string folder = ShowFolderDialog(
                "Select an output folder for " + item.DrawingName,
                item.OutputFolder);

            if (string.IsNullOrWhiteSpace(folder))
                return;

            item.OutputFolder = folder;
            item.UpdateDisplayStatus(namingMode);
            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void DrawingsGrid_CurrentCellDirtyStateChanged(
            object sender,
            EventArgs e)
        {
            if (drawingsGrid.IsCurrentCellDirty)
            {
                drawingsGrid.CommitEdit(
                    DataGridViewDataErrorContexts.Commit);

                UpdateFormStatus();
            }
        }

        private void DrawingsGrid_CellFormatting(
            object sender,
            DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.RowIndex >= drawingItems.Count ||
                statusColumn == null ||
                e.ColumnIndex != statusColumn.Index)
            {
                return;
            }

            PdfBatchExportItem item = drawingItems[e.RowIndex];
            item.UpdateDisplayStatus(namingMode);
            e.Value = item.Status;

            if (!string.IsNullOrWhiteSpace(item.Status) &&
                item.Status.StartsWith("! ", StringComparison.Ordinal))
            {
                e.CellStyle.BackColor = Color.MistyRose;
                e.CellStyle.ForeColor = Color.DarkRed;
                e.CellStyle.SelectionBackColor = Color.IndianRed;
                e.CellStyle.SelectionForeColor = Color.White;
            }
            else if (!item.PropertiesRead &&
                     namingMode ==
                     PdfExportNamingMode.AutomaticFromProperties)
            {
                e.CellStyle.BackColor = Color.LemonChiffon;
                e.CellStyle.ForeColor = Color.DarkGoldenrod;
            }
        }

        private void DrawingsGrid_CellMouseEnter(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.RowIndex >= drawingItems.Count ||
                e.ColumnIndex < 0)
            {
                return;
            }

            PdfBatchExportItem item = drawingItems[e.RowIndex];

            drawingsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText =
                "Source: " + item.SourcePath +
                "\r\n\r\nStatus: " + item.Status;
        }

        private void DrawingsGrid_DataError(
            object sender,
            DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void FolderModeRadioButton_CheckedChanged(
            object sender,
            EventArgs e)
        {
            if (suppressPreferenceEvents)
                return;

            UpdateFolderModeUi();
            SaveUserPreferences();
        }

        private void BatchOutputFolderTextBox_TextChanged(
            object sender,
            EventArgs e)
        {
            if (suppressPreferenceEvents)
                return;

            userPreferences.LastBatchOutputFolder =
                batchOutputFolderTextBox.Text.Trim();

            SaveUserPreferences();
            SetFolderActionHighlight(true);
        }

        private void ExportPreferenceControl_Changed(
            object sender,
            EventArgs e)
        {
            if (!suppressPreferenceEvents)
            {
                SaveUserPreferences();
            }
        }

        private void AddDrawingPath(string drawingPath)
        {
            string normalizedPath = NormalizePath(drawingPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))
                return;

            if (!string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".slddrw",
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Only SOLIDWORKS drawing files (*.slddrw) can be added.\r\n\r\n" +
                    normalizedPath,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            foreach (PdfBatchExportItem existingItem in drawingItems)
            {
                if (string.Equals(
                        existingItem.SourcePath,
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            PdfBatchExportItem item = PdfExportCommand.CreateBatchItem(
                swApp,
                normalizedPath,
                namingMode);

            if (FolderMode == PdfOutputFolderMode.CommonFolder &&
                Directory.Exists(batchOutputFolderTextBox.Text.Trim()))
            {
                item.OutputFolder = batchOutputFolderTextBox.Text.Trim();
            }

            item.UpdateDisplayStatus(namingMode);
            drawingItems.Add(item);
        }

        private void ApplyBatchFolder(bool applyToAll)
        {
            if (FolderMode != PdfOutputFolderMode.CommonFolder)
            {
                MessageBox.Show(
                    "Choose One common folder for all drawings before using this action.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            string folder = batchOutputFolderTextBox.Text.Trim();

            if (!Directory.Exists(folder))
            {
                MessageBox.Show(
                    "Select an existing output folder before applying it.\r\n\r\n" +
                    folder,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            foreach (PdfBatchExportItem item in drawingItems)
            {
                if (!applyToAll && !item.SelectedForExport)
                    continue;

                item.OutputFolder = folder;
                item.UpdateDisplayStatus(namingMode);
            }

            userPreferences.LastBatchOutputFolder = folder;
            SaveUserPreferences();
            SetFolderActionHighlight(false);
            drawingsGrid.Refresh();
            UpdateFormStatus();
        }

        private void BeginExport(PdfBatchExportScope scope)
        {
            if (drawingItems.Count == 0)
            {
                MessageBox.Show(
                    "Add one or more SOLIDWORKS drawings before exporting.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (FolderMode == PdfOutputFolderMode.CommonFolder &&
                folderActionPending)
            {
                DialogResult folderResponse = MessageBox.Show(
                    "A common output folder was selected but has not been applied to the drawing rows.\r\n\r\nApply it to all rows now?",
                    "Cabin Tools - Apply Output Folder",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (folderResponse == DialogResult.Yes)
                {
                    ApplyBatchFolder(true);
                }
            }

            if (namingMode == PdfExportNamingMode.AutomaticFromProperties)
            {
                List<PdfBatchExportItem> itemsToRefresh =
                    GetRequestedItems(scope, true);

                if (itemsToRefresh.Count > 0)
                {
                    RefreshPropertiesForItems(itemsToRefresh);
                }
            }

            int requestedCount = 0;
            int validCount = 0;
            int blockedCount = 0;

            foreach (PdfBatchExportItem item in drawingItems)
            {
                bool requested = scope == PdfBatchExportScope.AllValidRows ||
                                 item.SelectedForExport;

                if (!requested)
                    continue;

                requestedCount++;

                if (string.IsNullOrWhiteSpace(
                        PdfExportCommand.GetExportBlockReason(
                            item,
                            namingMode)))
                {
                    validCount++;
                }
                else
                {
                    blockedCount++;
                }
            }

            if (requestedCount == 0)
            {
                MessageBox.Show(
                    "Check one or more Export boxes, or use Export all ready / valid.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (validCount == 0)
            {
                MessageBox.Show(
                    namingMode == PdfExportNamingMode.AutomaticFromProperties
                        ? "No requested drawings have all required naming properties and a valid output folder. Status rows beginning with ! are not exported automatically."
                        : "No requested drawings have a valid source file, manual PDF filename, and output folder.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            if (blockedCount > 0)
            {
                DialogResult response = MessageBox.Show(
                    requestedCount.ToString() +
                    " row(s) requested. " +
                    validCount.ToString() +
                    " row(s) are ready and " +
                    blockedCount.ToString() +
                    " row(s) will be skipped.\r\n\r\nContinue?",
                    "Cabin Tools - Confirm PDF Export",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (response != DialogResult.Yes)
                    return;
            }

            ExportOptions = new PdfBatchExportOptions
            {
                NamingMode = namingMode,
                Scope = scope,
                ExistingFileBehavior =
                    existingPdfBehaviorComboBox.SelectedIndex == 1
                        ? PdfExistingFileBehavior.Overwrite
                        : PdfExistingFileBehavior.Skip,
                FolderMode = FolderMode,
                LeaveDrawingsOpenedByCabinToolsOpen =
                    leaveDrawingsOpenCheckBox.Checked,
                OpenExportedPdfFilesAfterExport =
                    openPdfAfterExportCheckBox.Checked
            };

            SaveUserPreferences();
            DialogResult = DialogResult.OK;
            Close();
        }

        private List<PdfBatchExportItem> GetRequestedItems(
            PdfBatchExportScope scope,
            bool onlyItemsWithoutProperties)
        {
            List<PdfBatchExportItem> items = new List<PdfBatchExportItem>();

            foreach (PdfBatchExportItem item in drawingItems)
            {
                bool requested = scope == PdfBatchExportScope.AllValidRows ||
                                 item.SelectedForExport;

                if (!requested)
                    continue;

                if (onlyItemsWithoutProperties && item.PropertiesRead)
                    continue;

                items.Add(item);
            }

            return items;
        }

        private void UpdateFolderModeUi()
        {
            bool commonMode = FolderMode == PdfOutputFolderMode.CommonFolder;

            batchOutputFolderTextBox.Enabled = commonMode;
            browseBatchFolderButton.Enabled = commonMode;
            applyFolderToCheckedButton.Enabled = commonMode;
            applyFolderToAllButton.Enabled = commonMode;
            useSourceFoldersButton.Enabled = true;
            chooseFolderForSelectedRowsButton.Enabled = !commonMode;

            if (outputFolderColumn != null)
            {
                outputFolderColumn.ReadOnly = commonMode;
            }

            folderModeHintLabel.Text = commonMode
                ? "Select a folder, then apply it to checked rows or all rows."
                : "Select row(s), then choose a folder. You can also type a folder path directly in the Output folder column.";

            userPreferences.FolderMode = FolderMode;
            SaveUserPreferences();
            drawingsGrid?.Refresh();
        }

        private void SetFolderActionHighlight(bool highlight)
        {
            folderActionPending = highlight;

            Color actionColor = highlight
                ? Color.Khaki
                : SystemColors.Control;

            if (applyFolderToCheckedButton != null)
                applyFolderToCheckedButton.BackColor = actionColor;

            if (applyFolderToAllButton != null)
                applyFolderToAllButton.BackColor = actionColor;

            if (useSourceFoldersButton != null)
                useSourceFoldersButton.BackColor = actionColor;
        }

        private void UpdateFormStatus()
        {
            int total = drawingItems.Count;
            int ready = 0;
            int attention = 0;
            int pendingProperties = 0;
            int checkedRows = 0;

            foreach (PdfBatchExportItem item in drawingItems)
            {
                item.UpdateDisplayStatus(namingMode);

                if (item.SelectedForExport)
                    checkedRows++;

                if (!item.PropertiesRead &&
                    namingMode == PdfExportNamingMode.AutomaticFromProperties)
                {
                    pendingProperties++;
                }

                if (!string.IsNullOrWhiteSpace(item.Status) &&
                    item.Status.StartsWith("! ", StringComparison.Ordinal))
                {
                    attention++;
                }
                else if (string.IsNullOrWhiteSpace(
                            PdfExportCommand.GetExportBlockReason(
                                item,
                                namingMode)))
                {
                    ready++;
                }
            }

            if (total == 0)
            {
                statusLabel.Text =
                    "No drawings selected. Add drawings... supports multi-selection and does not open drawings. The source drawings are never saved by this tool.";
            }
            else
            {
                statusLabel.Text =
                    "Drawings: " + total.ToString() +
                    " | Ready: " + ready.ToString() +
                    " | Needs attention: " + attention.ToString() +
                    " | Pending property read: " + pendingProperties.ToString() +
                    " | Checked for export: " + checkedRows.ToString() +
                    "\r\nStatus beginning with ! is highlighted red. Automatic export skips missing naming properties. PDF creation still requires SOLIDWORKS to load the drawing internally, but Cabin Tools opens selected files silently, hides them, and closes them unless Leave drawings open is selected.";
            }

            exportCheckedButton.Enabled = total > 0;
            exportAllValidButton.Enabled = total > 0;
        }

        private List<PdfBatchExportItem> GetSelectedGridItems()
        {
            List<PdfBatchExportItem> selectedItems = new List<PdfBatchExportItem>();

            if (drawingsGrid == null)
                return selectedItems;

            foreach (DataGridViewRow selectedRow in drawingsGrid.SelectedRows)
            {
                PdfBatchExportItem item =
                    selectedRow.DataBoundItem as PdfBatchExportItem;

                if (item != null && !selectedItems.Contains(item))
                {
                    selectedItems.Add(item);
                }
            }

            return selectedItems;
        }

        private string ShowFolderDialog(string description, string initialFolder)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = description;
                folderDialog.ShowNewFolderButton = true;

                if (Directory.Exists(initialFolder))
                {
                    folderDialog.SelectedPath = initialFolder;
                }

                return folderDialog.ShowDialog() == DialogResult.OK
                    ? folderDialog.SelectedPath
                    : string.Empty;
            }
        }

        private void SaveUserPreferences()
        {
            if (userPreferences == null ||
                leaveDrawingsOpenCheckBox == null ||
                openPdfAfterExportCheckBox == null ||
                batchOutputFolderTextBox == null)
            {
                return;
            }

            userPreferences.LeaveDrawingsOpen =
                leaveDrawingsOpenCheckBox.Checked;
            userPreferences.OpenExportedPdfFiles =
                openPdfAfterExportCheckBox.Checked;
            userPreferences.FolderMode = FolderMode;
            userPreferences.LastBatchOutputFolder =
                batchOutputFolderTextBox.Text.Trim();
            userPreferences.Save();
        }

        private static string GetSourceDirectory(string sourcePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(sourcePath);

                return !string.IsNullOrWhiteSpace(directory) &&
                       Directory.Exists(directory)
                    ? directory
                    : System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments);
            }
            catch
            {
                return System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.MyDocuments);
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path ?? string.Empty);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }
    }
}
