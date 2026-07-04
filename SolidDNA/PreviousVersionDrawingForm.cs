using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace SolidDNA
{
    /// <summary>
    /// Selects up to five drawing files, lets the user rename each output
    /// drawing, and selects one shared output folder.
    /// </summary>
    public sealed class PreviousVersionDrawingForm : Form
    {
        private const int MaximumDrawingCount = 5;

        private readonly int targetSolidWorksYear;

        private readonly List<PreviousVersionDrawingItem>
            drawingItems;

        private TextBox outputFolderTextBox;
        private Button browseOutputFolderButton;
        private Button addDrawingsButton;
        private Button removeSelectedButton;
        private Button clearButton;
        private Label selectedCountLabel;
        private DataGridView drawingGrid;
        private TextBox warningTextBox;
        private Button createCopiesButton;
        private Button cancelButton;

        public List<PreviousVersionDrawingItem> DrawingItems
        {
            get
            {
                return drawingItems;
            }
        }

        public PreviousVersionDrawingForm(
            int targetYear,
            string activeDrawingPath)
        {
            targetSolidWorksYear = targetYear;

            drawingItems =
                new List<PreviousVersionDrawingItem>();

            BuildForm();

            if (!string.IsNullOrWhiteSpace(activeDrawingPath))
            {
                AddDrawingPath(
                    activeDrawingPath,
                    false);
            }

            RefreshGrid();
        }

        private void BuildForm()
        {
            Text =
                "Save Drawings as SOLIDWORKS " +
                targetSolidWorksYear.ToString();

            Width = 1120;
            Height = 650;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.Padding = new Padding(10);
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 125));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            Controls.Add(mainLayout);

            GroupBox outputGroupBox =
                new GroupBox();

            outputGroupBox.Text =
                "Shared output folder for SOLIDWORKS " +
                targetSolidWorksYear.ToString() +
                " copies";

            outputGroupBox.Dock = DockStyle.Fill;
            outputGroupBox.Height = 75;

            TableLayoutPanel outputLayout =
                new TableLayoutPanel();

            outputLayout.Dock = DockStyle.Fill;
            outputLayout.ColumnCount = 2;
            outputLayout.RowCount = 1;
            outputLayout.Padding = new Padding(8);
            outputLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            outputLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 130));

            outputFolderTextBox =
                new TextBox();

            outputFolderTextBox.Dock = DockStyle.Fill;
            outputFolderTextBox.ReadOnly = true;
            outputFolderTextBox.Text =
                GetDefaultOutputFolder();

            browseOutputFolderButton =
                new Button();

            browseOutputFolderButton.Text =
                "Browse...";
            browseOutputFolderButton.Dock = DockStyle.Fill;
            browseOutputFolderButton.Click +=
                BrowseOutputFolderButton_Click;

            outputLayout.Controls.Add(
                outputFolderTextBox,
                0,
                0);

            outputLayout.Controls.Add(
                browseOutputFolderButton,
                1,
                0);

            outputGroupBox.Controls.Add(outputLayout);

            mainLayout.Controls.Add(
                outputGroupBox,
                0,
                0);

            TableLayoutPanel selectionLayout =
                new TableLayoutPanel();

            selectionLayout.Dock = DockStyle.Fill;
            selectionLayout.ColumnCount = 1;
            selectionLayout.RowCount = 2;
            selectionLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            selectionLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));

            FlowLayoutPanel selectionButtons =
                new FlowLayoutPanel();

            selectionButtons.Dock = DockStyle.Fill;
            selectionButtons.FlowDirection =
                FlowDirection.LeftToRight;
            selectionButtons.AutoSize = true;

            addDrawingsButton =
                new Button();

            addDrawingsButton.Text =
                "Add Drawings...";
            addDrawingsButton.Width = 125;
            addDrawingsButton.Click +=
                AddDrawingsButton_Click;

            removeSelectedButton =
                new Button();

            removeSelectedButton.Text =
                "Remove Selected";
            removeSelectedButton.Width = 135;
            removeSelectedButton.Click +=
                RemoveSelectedButton_Click;

            clearButton =
                new Button();

            clearButton.Text = "Clear";
            clearButton.Width = 85;
            clearButton.Click +=
                ClearButton_Click;

            selectedCountLabel =
                new Label();

            selectedCountLabel.AutoSize = true;
            selectedCountLabel.Padding =
                new Padding(15, 8, 0, 0);

            selectionButtons.Controls.Add(
                addDrawingsButton);

            selectionButtons.Controls.Add(
                removeSelectedButton);

            selectionButtons.Controls.Add(
                clearButton);

            selectionButtons.Controls.Add(
                selectedCountLabel);

            drawingGrid =
                new DataGridView();

            drawingGrid.Dock = DockStyle.Fill;
            drawingGrid.AllowUserToAddRows = false;
            drawingGrid.AllowUserToDeleteRows = false;
            drawingGrid.AllowUserToResizeRows = false;
            drawingGrid.RowHeadersVisible = false;
            drawingGrid.SelectionMode =
                DataGridViewSelectionMode.FullRowSelect;
            drawingGrid.MultiSelect = true;
            drawingGrid.AutoSizeColumnsMode =
                DataGridViewAutoSizeColumnsMode.Fill;
            drawingGrid.CellEndEdit +=
                DrawingGrid_CellEndEdit;

            DataGridViewTextBoxColumn sourceColumn =
                new DataGridViewTextBoxColumn();

            sourceColumn.HeaderText =
                "Selected drawing";
            sourceColumn.ReadOnly = true;
            sourceColumn.FillWeight = 62;

            DataGridViewTextBoxColumn outputNameColumn =
                new DataGridViewTextBoxColumn();

            outputNameColumn.HeaderText =
                "Output file name (editable)";
            outputNameColumn.ReadOnly = false;
            outputNameColumn.FillWeight = 38;

            drawingGrid.Columns.Add(sourceColumn);
            drawingGrid.Columns.Add(outputNameColumn);

            selectionLayout.Controls.Add(
                selectionButtons,
                0,
                0);

            selectionLayout.Controls.Add(
                drawingGrid,
                0,
                1);

            mainLayout.Controls.Add(
                selectionLayout,
                0,
                1);

            warningTextBox =
                new TextBox();

            warningTextBox.Dock = DockStyle.Fill;
            warningTextBox.Multiline = true;
            warningTextBox.ReadOnly = true;
            warningTextBox.ScrollBars =
                ScrollBars.Vertical;

            warningTextBox.Text =
                "Batch rules:\r\n" +
                "- Select up to five saved SOLIDWORKS drawings (.slddrw).\r\n" +
                "- Edit each output file name directly in the table. The .slddrw extension is required.\r\n" +
                "- Existing output files are never overwritten. Rename the output file or choose another folder.\r\n" +
                "- Select an output folder outside the PDM vault/cache and outside each source drawing folder.\r\n" +
                "- Each selected drawing is copied as SOLIDWORKS " +
                targetSolidWorksYear.ToString() +
                ".\r\n" +
                "- This action converts drawing files only. Referenced part and assembly files are not converted in this stage.\r\n" +
                "- Cabin Tools does not check files in or out and does not change PDM references.\r\n" +
                "- Save or close selected drawings before running the batch so you know exactly which revision is copied.";

            mainLayout.Controls.Add(
                warningTextBox,
                0,
                2);

            Label noteLabel =
                new Label();

            noteLabel.AutoSize = true;
            noteLabel.Text =
                "The active saved drawing is added automatically when this form opens. You may remove it or add other drawings.";

            mainLayout.Controls.Add(
                noteLabel,
                0,
                3);

            FlowLayoutPanel bottomButtons =
                new FlowLayoutPanel();

            bottomButtons.Dock = DockStyle.Fill;
            bottomButtons.FlowDirection =
                FlowDirection.RightToLeft;

            createCopiesButton =
                new Button();

            createCopiesButton.Text =
                "Create Copies";
            createCopiesButton.Width = 125;
            createCopiesButton.Click +=
                CreateCopiesButton_Click;

            cancelButton =
                new Button();

            cancelButton.Text =
                "Cancel";
            cancelButton.Width = 100;
            cancelButton.Click +=
                CancelButton_Click;

            bottomButtons.Controls.Add(cancelButton);
            bottomButtons.Controls.Add(createCopiesButton);

            mainLayout.Controls.Add(
                bottomButtons,
                0,
                4);
        }

        private void AddDrawingsButton_Click(
            object sender,
            EventArgs e)
        {
            int remainingSlots =
                MaximumDrawingCount -
                drawingItems.Count;

            if (remainingSlots <= 0)
            {
                ShowValidationError(
                    "A maximum of " +
                    MaximumDrawingCount.ToString() +
                    " drawings can be converted in one batch.");

                return;
            }

            using (OpenFileDialog dialog =
                new OpenFileDialog())
            {
                dialog.Title =
                    "Select up to " +
                    remainingSlots.ToString() +
                    " SOLIDWORKS drawing files";

                dialog.Filter =
                    "SOLIDWORKS drawings (*.slddrw)|*.slddrw";

                dialog.Multiselect = true;
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() !=
                    DialogResult.OK)
                {
                    return;
                }

                string[] selectedPaths =
                    dialog.FileNames;

                if (selectedPaths == null ||
                    selectedPaths.Length == 0)
                {
                    return;
                }

                if (selectedPaths.Length > remainingSlots)
                {
                    ShowValidationError(
                        "You selected " +
                        selectedPaths.Length.ToString() +
                        " drawings, but only " +
                        remainingSlots.ToString() +
                        " slot(s) remain.\n\n" +
                        "Select no more than " +
                        remainingSlots.ToString() +
                        " drawing(s) in this selection.");

                    return;
                }

                foreach (string selectedPath in
                    selectedPaths)
                {
                    AddDrawingPath(
                        selectedPath,
                        true);
                }

                RefreshGrid();
            }
        }

        private void AddDrawingPath(
            string drawingPath,
            bool showDuplicateMessage)
        {
            if (string.IsNullOrWhiteSpace(drawingPath))
            {
                return;
            }

            if (!File.Exists(drawingPath))
            {
                return;
            }

            if (!string.Equals(
                Path.GetExtension(drawingPath),
                ".slddrw",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (ContainsSourcePath(drawingPath))
            {
                if (showDuplicateMessage)
                {
                    ShowValidationError(
                        "This drawing is already in the list:\n\n" +
                        drawingPath);
                }

                return;
            }

            if (drawingItems.Count >=
                MaximumDrawingCount)
            {
                return;
            }

            drawingItems.Add(
                new PreviousVersionDrawingItem
                {
                    SourcePath = drawingPath,
                    OutputFileName =
                        BuildDefaultOutputFileName(
                            drawingPath),
                    DestinationPath = string.Empty
                });
        }

        private bool ContainsSourcePath(
            string drawingPath)
        {
            foreach (PreviousVersionDrawingItem item in
                drawingItems)
            {
                if (string.Equals(
                    item.SourcePath,
                    drawingPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildDefaultOutputFileName(
            string drawingPath)
        {
            string name =
                Path.GetFileNameWithoutExtension(
                    drawingPath);

            return name +
                   "_SW" +
                   targetSolidWorksYear.ToString() +
                   ".slddrw";
        }

        private void RemoveSelectedButton_Click(
            object sender,
            EventArgs e)
        {
            if (drawingGrid.SelectedRows.Count == 0)
            {
                return;
            }

            List<int> indexesToRemove =
                new List<int>();

            foreach (DataGridViewRow row in
                drawingGrid.SelectedRows)
            {
                indexesToRemove.Add(row.Index);
            }

            indexesToRemove.Sort();
            indexesToRemove.Reverse();

            foreach (int index in indexesToRemove)
            {
                if (index >= 0 &&
                    index < drawingItems.Count)
                {
                    drawingItems.RemoveAt(index);
                }
            }

            RefreshGrid();
        }

        private void ClearButton_Click(
            object sender,
            EventArgs e)
        {
            drawingItems.Clear();
            RefreshGrid();
        }

        private void DrawingGrid_CellEndEdit(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.ColumnIndex != 1 ||
                e.RowIndex >= drawingItems.Count)
            {
                return;
            }

            string outputName =
                Convert.ToString(
                    drawingGrid.Rows[e.RowIndex]
                        .Cells[1].Value);

            drawingItems[e.RowIndex].OutputFileName =
                outputName == null
                    ? string.Empty
                    : outputName.Trim();
        }

        private void RefreshGrid()
        {
            drawingGrid.Rows.Clear();

            foreach (PreviousVersionDrawingItem item in
                drawingItems)
            {
                drawingGrid.Rows.Add(
                    item.SourcePath,
                    item.OutputFileName);
            }

            selectedCountLabel.Text =
                "Selected: " +
                drawingItems.Count.ToString() +
                " / " +
                MaximumDrawingCount.ToString();
        }

        private void BrowseOutputFolderButton_Click(
            object sender,
            EventArgs e)
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                dialog.Description =
                    "Select an output folder outside the PDM vault/cache";

                if (Directory.Exists(
                    outputFolderTextBox.Text))
                {
                    dialog.SelectedPath =
                        outputFolderTextBox.Text;
                }

                if (dialog.ShowDialog() ==
                    DialogResult.OK)
                {
                    outputFolderTextBox.Text =
                        dialog.SelectedPath;
                }
            }
        }

        private string GetDefaultOutputFolder()
        {
            string documentsFolder =
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder
                        .MyDocuments);

            return Path.Combine(
                documentsFolder,
                "SOLIDWORKS_Exports",
                "SW" +
                targetSolidWorksYear.ToString());
        }

        private void CreateCopiesButton_Click(
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
            if (drawingItems.Count == 0)
            {
                ShowValidationError(
                    "Add at least one SOLIDWORKS drawing.");

                return false;
            }

            if (drawingItems.Count >
                MaximumDrawingCount)
            {
                ShowValidationError(
                    "A maximum of " +
                    MaximumDrawingCount.ToString() +
                    " drawings can be converted in one batch.");

                return false;
            }

            string outputFolder =
                outputFolderTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                ShowValidationError(
                    "Select an output folder first.");

                return false;
            }

            if (!ValidateAndBuildOutputPaths(
                outputFolder))
            {
                return false;
            }

            return true;
        }

        private bool ValidateAndBuildOutputPaths(
            string outputFolder)
        {
            HashSet<string> outputNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (PreviousVersionDrawingItem item in
                drawingItems)
            {
                if (!File.Exists(item.SourcePath))
                {
                    ShowValidationError(
                        "The selected drawing no longer exists:\n\n" +
                        item.SourcePath);

                    return false;
                }

                if (!string.Equals(
                    Path.GetExtension(item.SourcePath),
                    ".slddrw",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ShowValidationError(
                        "Only .slddrw drawing files are allowed:\n\n" +
                        item.SourcePath);

                    return false;
                }

                string outputName =
                    EnsureDrawingExtension(
                        item.OutputFileName);

                if (!IsValidOutputFileName(outputName))
                {
                    ShowValidationError(
                        "Enter a valid .slddrw output file name for:\n\n" +
                        item.SourcePath);

                    return false;
                }

                if (!outputNames.Add(outputName))
                {
                    ShowValidationError(
                        "Each output file name must be unique.\n\nDuplicate name:\n" +
                        outputName);

                    return false;
                }

                string sourceFolder =
                    Path.GetDirectoryName(
                        item.SourcePath);

                if (FoldersMatch(
                    sourceFolder,
                    outputFolder))
                {
                    ShowValidationError(
                        "Choose an output folder different from every source drawing folder.\n\n" +
                        "Conflicting drawing:\n" +
                        item.SourcePath);

                    return false;
                }

                string destinationPath =
                    Path.Combine(
                        outputFolder,
                        outputName);

                if (File.Exists(destinationPath))
                {
                    ShowValidationError(
                        "Cabin Tools will not overwrite an existing file.\n\n" +
                        "Rename the output file or choose another folder:\n" +
                        destinationPath);

                    return false;
                }

                item.OutputFileName = outputName;
                item.DestinationPath = destinationPath;
            }

            return true;
        }

        private static string EnsureDrawingExtension(
            string outputFileName)
        {
            string value =
                (outputFileName ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!value.EndsWith(
                ".slddrw",
                StringComparison.OrdinalIgnoreCase))
            {
                if (Path.HasExtension(value))
                {
                    return string.Empty;
                }

                value += ".slddrw";
            }

            return value;
        }

        private static bool IsValidOutputFileName(
            string outputFileName)
        {
            if (string.IsNullOrWhiteSpace(outputFileName))
            {
                return false;
            }

            if (!outputFileName.EndsWith(
                ".slddrw",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (outputFileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            if (outputFileName.IndexOf(
                Path.DirectorySeparatorChar) >= 0 ||
                outputFileName.IndexOf(
                    Path.AltDirectorySeparatorChar) >= 0)
            {
                return false;
            }

            return true;
        }

        private static bool FoldersMatch(
            string firstFolder,
            string secondFolder)
        {
            if (string.IsNullOrWhiteSpace(firstFolder) ||
                string.IsNullOrWhiteSpace(secondFolder))
            {
                return false;
            }

            string first =
                Path.GetFullPath(firstFolder)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string second =
                Path.GetFullPath(secondFolder)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            return string.Equals(
                first,
                second,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ShowValidationError(
            string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
