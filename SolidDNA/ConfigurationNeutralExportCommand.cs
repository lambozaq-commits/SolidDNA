using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CADBooster.SolidDna;
using static CADBooster.SolidDna.SolidWorksEnvironment;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Exports selected configurations of the active part or assembly as
    /// individual STEP AP203, IGES, or ACIS files.
    ///
    /// File-name convention:
    ///     &lt;Document Description&gt;, &lt;Configuration Name&gt;.&lt;ext&gt;
    ///
    /// The Description is read from the document-level custom property,
    /// not from the configuration-specific Description property.
    /// </summary>
    public static class ConfigurationNeutralExportCommand
    {
        public static void ShowConfigurationNeutralExportForm()
        {
            IModelDoc2 modelDoc = GetActivePartOrAssembly();
            if (modelDoc == null)
            {
                ShowWarning("Open a part or assembly first.");
                return;
            }

            List<string> configurationNames = GetConfigurationNames(modelDoc);
            if (configurationNames.Count == 0)
            {
                ShowWarning("No configurations found.");
                return;
            }

            string documentDescription = ReadDocumentDescription(modelDoc);
            if (string.IsNullOrWhiteSpace(documentDescription))
            {
                documentDescription = Path.GetFileNameWithoutExtension(modelDoc.GetPathName());
                if (string.IsNullOrWhiteSpace(documentDescription))
                    documentDescription = Path.GetFileNameWithoutExtension(modelDoc.GetTitle());
            }

            using (ConfigurationNeutralExportForm form =
                new ConfigurationNeutralExportForm(
                    modelDoc,
                    documentDescription,
                    configurationNames))
            {
                form.ShowDialog();
            }
        }

        private static IModelDoc2 GetActivePartOrAssembly()
        {
            try
            {
                ISldWorks swApp = IApplication.UnsafeObject;
                IModelDoc2 modelDoc = swApp == null ? null : swApp.ActiveDoc as IModelDoc2;
                if (modelDoc == null)
                    return null;

                int type = modelDoc.GetType();
                if (type != (int)swDocumentTypes_e.swDocPART &&
                    type != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    return null;
                }

                return modelDoc;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> GetConfigurationNames(IModelDoc2 modelDoc)
        {
            List<string> result = new List<string>();

            try
            {
                Array names = modelDoc.GetConfigurationNames() as Array;
                if (names == null)
                    return result;

                foreach (object item in names)
                {
                    string name = Convert.ToString(item) ?? string.Empty;
                    name = name.Trim();
                    if (name.Length > 0)
                        result.Add(name);
                }
            }
            catch
            {
                // Return whatever was collected.
            }

            return result;
        }

        private static string ReadDocumentDescription(IModelDoc2 modelDoc)
        {
            try
            {
                ICustomPropertyManager manager =
                    modelDoc.Extension.CustomPropertyManager[string.Empty];

                if (manager == null)
                    return string.Empty;

                string raw;
                string resolved;
                bool wasResolved;
                bool linked;

                manager.Get6(
                    "Description",
                    false,
                    out raw,
                    out resolved,
                    out wasResolved,
                    out linked);

                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved.Trim();

                if (!string.IsNullOrWhiteSpace(raw))
                    return raw.Trim();

                manager.Get6(
                    "DESCRIPTION",
                    false,
                    out raw,
                    out resolved,
                    out wasResolved,
                    out linked);

                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved.Trim();

                return string.IsNullOrWhiteSpace(raw)
                    ? string.Empty
                    : raw.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsDerivedConfiguration(
            IModelDoc2 modelDoc,
            string configurationName)
        {
            if (modelDoc == null || string.IsNullOrWhiteSpace(configurationName))
                return false;

            try
            {
                IConfiguration configuration =
                    modelDoc.GetConfigurationByName(configurationName) as IConfiguration;
                if (configuration == null)
                    return false;

                object parent = configuration.GetType().InvokeMember(
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

        private static void ShowWarning(string message)
        {
            try
            {
                IApplication.ShowMessageBox(
                    message,
                    SolidWorksMessageBoxIcon.Warning);
            }
            catch
            {
                MessageBox.Show(
                    message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private enum NeutralExportFormat
        {
            StepAp203,
            Iges,
            Acis
        }

        private enum ExistingFileAction
        {
            Cancel,
            Skip,
            Overwrite
        }

        private sealed class ConfigurationExportRow
        {
            public string ConfigurationName { get; set; }
            public string FileName { get; set; }
            public string Status { get; set; }
            public bool IsDerived { get; set; }
        }

        private sealed class ConfigurationNeutralExportForm : Form
        {
            private readonly IModelDoc2 modelDoc;
            private readonly string documentDescription;
            private readonly List<ConfigurationExportRow> rows;

            private DataGridView grid;
            private Label documentLabel;
            private Label descriptionLabel;
            private Label summaryLabel;
            private Button checkAllButton;
            private Button uncheckDerivedButton;
            private Button exportButton;
            private Button closeButton;

            private bool propagatingCheckState;
            private readonly List<int> checkboxSelectionSnapshot = new List<int>();

            public ConfigurationNeutralExportForm(
                IModelDoc2 modelDoc,
                string documentDescription,
                IList<string> configurationNames)
            {
                this.modelDoc = modelDoc;
                this.documentDescription = documentDescription ?? string.Empty;
                this.rows = new List<ConfigurationExportRow>();

                foreach (string name in configurationNames)
                {
                    rows.Add(
                        new ConfigurationExportRow
                        {
                            ConfigurationName = name,
                            FileName = BuildBaseFileName(this.documentDescription, name),
                            Status = "Ready",
                            IsDerived = IsDerivedConfiguration(modelDoc, name)
                        });
                }

                Text = "Cabin Tools - Export Configurations";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 980;
                Height = 680;
                MinimumSize = new Size(760, 500);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadRows();
            }

            private void BuildLayout()
            {
                Label title = new Label();
                title.Text = "Export Configurations";
                title.Font = new Font(Font, FontStyle.Bold);
                title.Left = 18;
                title.Top = 16;
                title.Width = 300;
                title.Height = 24;
                Controls.Add(title);

                documentLabel = new Label();
                documentLabel.Left = 18;
                documentLabel.Top = 47;
                documentLabel.Width = 900;
                documentLabel.Height = 22;
                documentLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                documentLabel.Text = "Document: " + GetDisplayDocumentName(modelDoc);
                Controls.Add(documentLabel);

                descriptionLabel = new Label();
                descriptionLabel.Left = 18;
                descriptionLabel.Top = 70;
                descriptionLabel.Width = Math.Max(300, ClientSize.Width - 390);
                descriptionLabel.Height = 22;
                descriptionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                descriptionLabel.Text = "Description: " + documentDescription;
                Controls.Add(descriptionLabel);

                checkAllButton = new Button();
                checkAllButton.Text = "Check / uncheck all";
                checkAllButton.Width = 145;
                checkAllButton.Height = 28;
                checkAllButton.Left = ClientSize.Width - 325;
                checkAllButton.Top = 66;
                checkAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                checkAllButton.Click += delegate { ToggleAllRows(); };
                Controls.Add(checkAllButton);

                uncheckDerivedButton = new Button();
                uncheckDerivedButton.Text = "Uncheck derived";
                uncheckDerivedButton.Width = 145;
                uncheckDerivedButton.Height = 28;
                uncheckDerivedButton.Left = ClientSize.Width - 170;
                uncheckDerivedButton.Top = 66;
                uncheckDerivedButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                uncheckDerivedButton.Click += delegate { UncheckDerivedRows(); };
                Controls.Add(uncheckDerivedButton);

                grid = new DataGridView();
                grid.Left = 18;
                grid.Top = 108;
                grid.Width = ClientSize.Width - 36;
                grid.Height = ClientSize.Height - 186;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.AllowUserToResizeColumns = true;
                grid.MultiSelect = true;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.RowHeadersVisible = false;
                grid.AutoGenerateColumns = false;
                grid.BackgroundColor = SystemColors.ControlDark;
                grid.BorderStyle = BorderStyle.FixedSingle;
                grid.EditMode = DataGridViewEditMode.EditOnEnter;
                grid.CellContentClick += Grid_CellContentClick;
                grid.CellMouseDown += Grid_CellMouseDown;
                grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
                grid.KeyDown += Grid_KeyDown;
                Controls.Add(grid);

                DataGridViewCheckBoxColumn exportColumn = new DataGridViewCheckBoxColumn();
                exportColumn.Name = "Export";
                exportColumn.HeaderText = "Export";
                exportColumn.Width = 58;
                exportColumn.Resizable = DataGridViewTriState.False;
                grid.Columns.Add(exportColumn);

                DataGridViewTextBoxColumn configurationColumn = new DataGridViewTextBoxColumn();
                configurationColumn.Name = "Configuration";
                configurationColumn.HeaderText = "Configuration";
                configurationColumn.ReadOnly = true;
                configurationColumn.Width = 300;
                grid.Columns.Add(configurationColumn);

                DataGridViewTextBoxColumn fileNameColumn = new DataGridViewTextBoxColumn();
                fileNameColumn.Name = "FileName";
                fileNameColumn.HeaderText = "File name";
                fileNameColumn.ReadOnly = true;
                fileNameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                fileNameColumn.MinimumWidth = 260;
                grid.Columns.Add(fileNameColumn);

                DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
                statusColumn.Name = "Status";
                statusColumn.HeaderText = "Status";
                statusColumn.ReadOnly = true;
                statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                statusColumn.Width = 160;
                statusColumn.MinimumWidth = 80;
                statusColumn.Resizable = DataGridViewTriState.True;
                grid.Columns.Add(statusColumn);

                summaryLabel = new Label();
                summaryLabel.Left = 18;
                summaryLabel.Top = ClientSize.Height - 66;
                summaryLabel.Width = 500;
                summaryLabel.Height = 24;
                summaryLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                summaryLabel.Font = new Font(Font, FontStyle.Bold);
                Controls.Add(summaryLabel);

                exportButton = new Button();
                exportButton.Text = "Export...";
                exportButton.Width = 110;
                exportButton.Height = 30;
                exportButton.Left = ClientSize.Width - 250;
                exportButton.Top = ClientSize.Height - 70;
                exportButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                exportButton.Click += delegate { ExportSelectedConfigurations(); };
                Controls.Add(exportButton);

                closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Width = 110;
                closeButton.Height = 30;
                closeButton.Left = ClientSize.Width - 128;
                closeButton.Top = ClientSize.Height - 70;
                closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                closeButton.Click += delegate { Close(); };
                Controls.Add(closeButton);
            }

            private void LoadRows()
            {
                grid.Rows.Clear();

                foreach (ConfigurationExportRow item in rows)
                {
                    int rowIndex = grid.Rows.Add();
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = item;
                    row.Cells["Export"].Value = true;
                    row.Cells["Configuration"].Value = item.ConfigurationName;
                    if (item.IsDerived)
                    {
                        row.Cells["Configuration"].Style.Padding = new Padding(22, 0, 0, 0);
                        row.Cells["Configuration"].ToolTipText = "Derived configuration";
                    }
                    row.Cells["FileName"].Value = item.FileName;
                    row.Cells["Status"].Value = item.Status;
                }

                UpdateSummary();
            }

            private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
            {
                checkboxSelectionSnapshot.Clear();

                if (e.Button != MouseButtons.Left ||
                    e.RowIndex < 0 ||
                    e.ColumnIndex < 0 ||
                    grid.Columns[e.ColumnIndex].Name != "Export")
                {
                    return;
                }

                DataGridViewRow clickedRow = grid.Rows[e.RowIndex];
                if (!clickedRow.Selected || grid.SelectedRows.Count <= 1)
                    return;

                foreach (DataGridViewRow selectedRow in grid.SelectedRows)
                    checkboxSelectionSnapshot.Add(selectedRow.Index);
            }

            private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
            {
                if (grid.IsCurrentCellDirty &&
                    grid.CurrentCell != null &&
                    grid.CurrentCell.OwningColumn != null &&
                    grid.CurrentCell.OwningColumn.Name == "Export")
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            }

            private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
            {
                if (propagatingCheckState || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;

                if (grid.Columns[e.ColumnIndex].Name != "Export")
                    return;

                BeginInvoke(
                    (MethodInvoker)delegate
                    {
                        bool target = ToBool(grid.Rows[e.RowIndex].Cells["Export"].Value);

                        if (checkboxSelectionSnapshot.Count > 1 &&
                            checkboxSelectionSnapshot.Contains(e.RowIndex))
                        {
                            PropagateCheckStateToRows(checkboxSelectionSnapshot, target);
                        }
                        else
                        {
                            UpdateSummary();
                        }

                        checkboxSelectionSnapshot.Clear();
                    });
            }

            private void Grid_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space || grid.SelectedRows.Count == 0)
                    return;

                bool allChecked = true;
                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (!ToBool(row.Cells["Export"].Value))
                    {
                        allChecked = false;
                        break;
                    }
                }

                PropagateCheckStateToSelectedRows(!allChecked);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            private void PropagateCheckStateToSelectedRows(bool value)
            {
                List<int> indices = new List<int>();
                foreach (DataGridViewRow row in grid.SelectedRows)
                    indices.Add(row.Index);

                PropagateCheckStateToRows(indices, value);
            }

            private void PropagateCheckStateToRows(IEnumerable<int> rowIndices, bool value)
            {
                propagatingCheckState = true;
                try
                {
                    foreach (int rowIndex in rowIndices.Distinct().ToList())
                    {
                        if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
                            grid.Rows[rowIndex].Cells["Export"].Value = value;
                    }
                }
                finally
                {
                    propagatingCheckState = false;
                }

                UpdateSummary();
            }

            private void ToggleAllRows()
            {
                GridCheckBehavior.ToggleSelectedThenAll(
                    grid,
                    row => ToBool(row.Cells["Export"].Value),
                    (row, value) => row.Cells["Export"].Value = value);

                UpdateSummary();
            }

            private void UncheckDerivedRows()
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    ConfigurationExportRow info = row.Tag as ConfigurationExportRow;
                    if (info != null && info.IsDerived)
                        row.Cells["Export"].Value = false;
                }

                UpdateSummary();
            }

            private void UpdateSummary()
            {
                int checkedCount = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (ToBool(row.Cells["Export"].Value))
                        checkedCount++;
                }

                summaryLabel.Text =
                    rows.Count.ToString() + " configuration(s) | " +
                    checkedCount.ToString() + " selected";
            }

            private void ExportSelectedConfigurations()
            {
                List<DataGridViewRow> selectedRows =
                    grid.Rows
                        .Cast<DataGridViewRow>()
                        .Where(r => ToBool(r.Cells["Export"].Value))
                        .ToList();

                if (selectedRows.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "Select at least one configuration.",
                        "Cabin Tools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                NeutralExportFormat? format = ChooseFormat();
                if (!format.HasValue)
                    return;

                string outputFolder = ChooseOutputFolder();
                if (string.IsNullOrWhiteSpace(outputFolder))
                    return;

                string extension = GetExtension(format.Value);
                List<string> outputPaths = new List<string>();
                foreach (DataGridViewRow gridRow in selectedRows)
                {
                    ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                    outputPaths.Add(
                        Path.Combine(
                            outputFolder,
                            info.FileName + extension));
                }

                if (HasDuplicateOutputPaths(outputPaths))
                {
                    MessageBox.Show(
                        this,
                        "Two configurations would create the same file name. Rename the configuration or document description first.",
                        "Cabin Tools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                ExistingFileAction existingFileAction = ExistingFileAction.Overwrite;
                if (outputPaths.Any(File.Exists))
                {
                    existingFileAction = ChooseExistingFileAction();
                    if (existingFileAction == ExistingFileAction.Cancel)
                        return;
                }

                RunExport(
                    selectedRows,
                    outputFolder,
                    format.Value,
                    existingFileAction);
            }

            private NeutralExportFormat? ChooseFormat()
            {
                using (ExportFormatDialog dialog = new ExportFormatDialog())
                {
                    DialogResult result = dialog.ShowDialog(this);
                    if (result != DialogResult.OK)
                        return null;

                    return dialog.SelectedFormat;
                }
            }

            private string ChooseOutputFolder()
            {
                string rememberedFolder = ExportSettings.LoadLastFolder();

                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Select export folder";
                    if (!string.IsNullOrWhiteSpace(rememberedFolder) &&
                        Directory.Exists(rememberedFolder))
                    {
                        dialog.SelectedPath = rememberedFolder;
                    }
                    else
                    {
                        string sourcePath = modelDoc.GetPathName();
                        if (!string.IsNullOrWhiteSpace(sourcePath))
                        {
                            string sourceFolder = Path.GetDirectoryName(sourcePath);
                            if (Directory.Exists(sourceFolder))
                                dialog.SelectedPath = sourceFolder;
                        }
                    }

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return string.Empty;

                    string folder = dialog.SelectedPath;
                    ExportSettings.SaveLastFolder(folder);
                    return folder;
                }
            }

            private ExistingFileAction ChooseExistingFileAction()
            {
                using (ExistingFilesDialog dialog = new ExistingFilesDialog())
                {
                    dialog.ShowDialog(this);
                    return dialog.SelectedAction;
                }
            }

            private void RunExport(
                IList<DataGridViewRow> selectedRows,
                string outputFolder,
                NeutralExportFormat format,
                ExistingFileAction existingFileAction)
            {
                exportButton.Enabled = false;
                closeButton.Enabled = false;
                Cursor previousCursor = Cursor;
                Cursor = Cursors.WaitCursor;

                ISldWorks swApp = null;
                string originalConfiguration = string.Empty;
                int originalStepAp = 0;
                bool stepPreferenceCaptured = false;
                int exportedCount = 0;
                int skippedCount = 0;
                int failedCount = 0;
                string fatalErrorMessage = string.Empty;
                List<string> reportLines = new List<string>();

                try
                {
                    swApp = IApplication.UnsafeObject;
                    if (swApp == null)
                        throw new InvalidOperationException("SOLIDWORKS is not available.");

                    IConfiguration activeConfiguration =
                        modelDoc.ConfigurationManager == null
                            ? null
                            : modelDoc.ConfigurationManager.ActiveConfiguration;
                    originalConfiguration =
                        activeConfiguration == null
                            ? string.Empty
                            : activeConfiguration.Name;

                    if (format == NeutralExportFormat.StepAp203)
                    {
                        originalStepAp = swApp.GetUserPreferenceIntegerValue(
                            (int)swUserPreferenceIntegerValue_e.swStepAP);
                        stepPreferenceCaptured = true;

                        bool setStep = swApp.SetUserPreferenceIntegerValue(
                            (int)swUserPreferenceIntegerValue_e.swStepAP,
                            203);

                        if (!setStep)
                        {
                            throw new InvalidOperationException(
                                "SOLIDWORKS did not accept STEP AP203 as the export format.");
                        }
                    }

                    reportLines.Add("Cabin Tools - Export Configurations");
                    reportLines.Add("Document: " + GetDisplayDocumentName(modelDoc));
                    reportLines.Add("Description: " + documentDescription);
                    reportLines.Add("Format: " + GetFormatDisplayName(format));
                    reportLines.Add("Output folder: " + outputFolder);
                    reportLines.Add(string.Empty);

                    foreach (DataGridViewRow gridRow in selectedRows)
                    {
                        ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                        if (info == null)
                            continue;

                        string outputPath = Path.Combine(
                            outputFolder,
                            info.FileName + GetExtension(format));

                        if (File.Exists(outputPath) &&
                            existingFileAction == ExistingFileAction.Skip)
                        {
                            SetRowStatus(gridRow, "Skipped - exists");
                            skippedCount++;
                            reportLines.Add(
                                info.ConfigurationName + " | Skipped - exists | " + outputPath);
                            continue;
                        }

                        SetRowStatus(gridRow, "Exporting...");
                        System.Windows.Forms.Application.DoEvents();

                        try
                        {
                            string activationError;
                            if (!ActivateConfigurationSafely(
                                    modelDoc,
                                    info.ConfigurationName,
                                    out activationError))
                            {
                                throw new InvalidOperationException(activationError);
                            }

                            modelDoc.ForceRebuild3(false);
                            modelDoc.ClearSelection2(true);

                            IModelDocExtension extension = modelDoc.Extension;
                            if (extension == null)
                                throw new InvalidOperationException("Model extension is not available.");

                            int errors = 0;
                            int warnings = 0;

                            bool saved = extension.SaveAs(
                                outputPath,
                                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                                null,
                                ref errors,
                                ref warnings);

                            if (!saved || !File.Exists(outputPath))
                            {
                                throw new InvalidOperationException(
                                    "Export failed. Error " + errors.ToString() +
                                    ", warning " + warnings.ToString() + ".");
                            }

                            SetRowStatus(gridRow, "Exported");
                            exportedCount++;
                            reportLines.Add(
                                info.ConfigurationName + " | Exported | " + outputPath);
                        }
                        catch (Exception ex)
                        {
                            SetRowStatus(gridRow, "! " + ex.Message);
                            failedCount++;
                            reportLines.Add(
                                info.ConfigurationName + " | FAILED | " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    fatalErrorMessage = ex.Message;
                    reportLines.Add("FATAL | " + ex.Message);
                }
                finally
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(originalConfiguration))
                        {
                            string restoreError;
                            if (ActivateConfigurationSafely(
                                    modelDoc,
                                    originalConfiguration,
                                    out restoreError))
                            {
                                modelDoc.ForceRebuild3(false);
                            }
                        }
                    }
                    catch
                    {
                        // Do not hide the export result because restoring the active
                        // configuration is a user-interface convenience only.
                    }

                    if (stepPreferenceCaptured && swApp != null)
                    {
                        try
                        {
                            swApp.SetUserPreferenceIntegerValue(
                                (int)swUserPreferenceIntegerValue_e.swStepAP,
                                originalStepAp);
                        }
                        catch
                        {
                            // Best effort: preserve the user's original STEP setting.
                        }
                    }

                    reportLines.Add(string.Empty);
                    reportLines.Add("Exported: " + exportedCount.ToString());
                    reportLines.Add("Skipped: " + skippedCount.ToString());
                    reportLines.Add("Failed: " + failedCount.ToString());
                    reportLines.Add("Original configuration restored: " + originalConfiguration);

                    string reportPath = WriteReport(reportLines);

                    Cursor = previousCursor;
                    exportButton.Enabled = true;
                    closeButton.Enabled = true;

                    summaryLabel.Text =
                        exportedCount.ToString() + " exported | " +
                        skippedCount.ToString() + " skipped | " +
                        failedCount.ToString() + " failed";

                    if (exportedCount > 0 || skippedCount > 0 || failedCount > 0)
                    {
                        string result = exportedCount.ToString() + " exported";
                        if (skippedCount > 0)
                            result += ", " + skippedCount.ToString() + " skipped";
                        if (failedCount > 0)
                            result += ", " + failedCount.ToString() + " failed";

                        if (!string.IsNullOrWhiteSpace(reportPath))
                            result += ".";

                        using (ExportResultDialog resultDialog =
                            new ExportResultDialog(
                                exportedCount,
                                skippedCount,
                                failedCount,
                                fatalErrorMessage))
                        {
                            resultDialog.ShowDialog(this);
                        }
                    }
                }
            }

            private static bool ActivateConfigurationSafely(
                IModelDoc2 modelDoc,
                string configurationName,
                out string errorMessage)
            {
                errorMessage = string.Empty;

                if (modelDoc == null)
                {
                    errorMessage = "Model is not available.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(configurationName))
                {
                    errorMessage = "Configuration name is blank.";
                    return false;
                }

                try
                {
                    IConfiguration existing = modelDoc.GetConfigurationByName(configurationName) as IConfiguration;
                    if (existing == null)
                    {
                        errorMessage = "Configuration no longer exists: " + configurationName;
                        return false;
                    }

                    IConfiguration active = modelDoc.ConfigurationManager == null
                        ? null
                        : modelDoc.ConfigurationManager.ActiveConfiguration;

                    if (active != null &&
                        string.Equals(
                            active.Name,
                            configurationName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    bool showResult = modelDoc.ShowConfiguration2(configurationName);

                    active = modelDoc.ConfigurationManager == null
                        ? null
                        : modelDoc.ConfigurationManager.ActiveConfiguration;

                    if (active != null &&
                        string.Equals(
                            active.Name,
                            configurationName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    errorMessage = showResult
                        ? "SOLIDWORKS reported configuration activation, but the active configuration did not change to '" + configurationName + "'."
                        : "Could not activate configuration '" + configurationName + "'.";
                    return false;
                }
                catch (Exception ex)
                {
                    errorMessage = "Could not activate configuration '" +
                        configurationName + "'. " + ex.Message;
                    return false;
                }
            }

            private static void SetRowStatus(DataGridViewRow row, string status)
            {
                row.Cells["Status"].Value = status;

                if (status.StartsWith("!", StringComparison.Ordinal))
                {
                    row.Cells["Status"].Style.BackColor = Color.MistyRose;
                    row.Cells["Status"].Style.ForeColor = Color.DarkRed;
                }
                else
                {
                    row.Cells["Status"].Style.BackColor = Color.Empty;
                    row.Cells["Status"].Style.ForeColor = Color.Empty;
                }
            }

            private static string WriteReport(IList<string> lines)
            {
                try
                {
                    string documents = System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments);
                    string folder = Path.Combine(
                        documents,
                        "CabinTools",
                        "ConfigurationExportReports");
                    Directory.CreateDirectory(folder);

                    string path = Path.Combine(
                        folder,
                        "ConfigurationExport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
                    return path;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private sealed class ExportResultDialog : Form
        {
            public ExportResultDialog(
                int exportedCount,
                int skippedCount,
                int failedCount,
                string fatalErrorMessage)
            {
                bool hasErrors = failedCount > 0;

                Text = "Cabin Tools - Export Result";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(430, hasErrors ? 175 : 145);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                if (hasErrors)
                    BackColor = Color.MistyRose;

                Label title = new Label();
                title.Text = hasErrors ? "Export completed with errors" : "Export complete";
                title.Font = new Font(Font, FontStyle.Bold);
                title.TextAlign = ContentAlignment.MiddleCenter;
                title.Left = 20;
                title.Top = 18;
                title.Width = 390;
                title.Height = 26;
                Controls.Add(title);

                Label summary = new Label();
                summary.Text =
                    exportedCount.ToString() + " exported | " +
                    skippedCount.ToString() + " skipped | " +
                    failedCount.ToString() + " failed";
                summary.TextAlign = ContentAlignment.MiddleCenter;
                summary.Left = 20;
                summary.Top = 52;
                summary.Width = 390;
                summary.Height = 24;
                Controls.Add(summary);

                if (hasErrors)
                {
                    Label errorHint = new Label();
                    errorHint.Text = string.IsNullOrWhiteSpace(fatalErrorMessage)
                        ? "Check the Status column for the failed configuration(s)."
                        : fatalErrorMessage;
                    errorHint.ForeColor = Color.DarkRed;
                    errorHint.TextAlign = ContentAlignment.MiddleCenter;
                    errorHint.Left = 24;
                    errorHint.Top = 80;
                    errorHint.Width = 382;
                    errorHint.Height = 42;
                    errorHint.AutoEllipsis = true;
                    Controls.Add(errorHint);
                }

                Button okButton = new Button();
                okButton.Text = "OK";
                okButton.Width = 100;
                okButton.Height = 30;
                okButton.Left = 165;
                okButton.Top = hasErrors ? 132 : 95;
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);

                AcceptButton = okButton;
                CancelButton = okButton;
            }
        }

        private sealed class ExportFormatDialog : Form
        {
            public NeutralExportFormat SelectedFormat { get; private set; }

            public ExportFormatDialog()
            {
                Text = "Cabin Tools - Export";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(450, 135);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                Label label = new Label();
                label.Text = "Export format";
                label.Font = new Font(Font, FontStyle.Bold);
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Left = 20;
                label.Top = 18;
                label.Width = 410;
                label.Height = 25;
                Controls.Add(label);

                Button stepButton = CreateChoiceButton("STEP AP203", 18, 65, 105);
                stepButton.Click += delegate
                {
                    SelectedFormat = NeutralExportFormat.StepAp203;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(stepButton);

                Button igesButton = CreateChoiceButton("IGES", 131, 65, 90);
                igesButton.Click += delegate
                {
                    SelectedFormat = NeutralExportFormat.Iges;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(igesButton);

                Button acisButton = CreateChoiceButton("ACIS", 229, 65, 90);
                acisButton.Click += delegate
                {
                    SelectedFormat = NeutralExportFormat.Acis;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(acisButton);

                Button cancelButton = CreateChoiceButton("Cancel", 327, 65, 105);
                cancelButton.Click += delegate
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                Controls.Add(cancelButton);

                CancelButton = cancelButton;
            }
        }

        private sealed class ExistingFilesDialog : Form
        {
            public ExistingFileAction SelectedAction { get; private set; }

            public ExistingFilesDialog()
            {
                SelectedAction = ExistingFileAction.Cancel;

                Text = "Cabin Tools - Existing Files";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(390, 135);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                Label label = new Label();
                label.Text = "Existing files found";
                label.Font = new Font(Font, FontStyle.Bold);
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Left = 20;
                label.Top = 18;
                label.Width = 350;
                label.Height = 25;
                Controls.Add(label);

                Button overwriteButton = CreateChoiceButton("Overwrite", 24, 65, 105);
                overwriteButton.Click += delegate
                {
                    SelectedAction = ExistingFileAction.Overwrite;
                    Close();
                };
                Controls.Add(overwriteButton);

                Button skipButton = CreateChoiceButton("Skip", 142, 65, 105);
                skipButton.Click += delegate
                {
                    SelectedAction = ExistingFileAction.Skip;
                    Close();
                };
                Controls.Add(skipButton);

                Button cancelButton = CreateChoiceButton("Cancel", 260, 65, 105);
                cancelButton.Click += delegate
                {
                    SelectedAction = ExistingFileAction.Cancel;
                    Close();
                };
                Controls.Add(cancelButton);

                CancelButton = cancelButton;
            }
        }

        private static Button CreateChoiceButton(
            string text,
            int left,
            int top,
            int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Left = left;
            button.Top = top;
            button.Width = width;
            button.Height = 32;
            return button;
        }

        private static string BuildBaseFileName(
            string documentDescription,
            string configurationName)
        {
            string description = SanitizeFileNamePart(documentDescription);
            string configuration = SanitizeFileNamePart(configurationName);

            if (string.IsNullOrWhiteSpace(description))
                return configuration;

            if (string.IsNullOrWhiteSpace(configuration))
                return description;

            return description + ", " + configuration;
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string result = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');

            result = result.Trim().TrimEnd('.');
            return result;
        }

        private static string GetDisplayDocumentName(IModelDoc2 modelDoc)
        {
            string path = modelDoc.GetPathName();
            if (!string.IsNullOrWhiteSpace(path))
                return Path.GetFileName(path);

            return modelDoc.GetTitle();
        }

        private static string GetExtension(NeutralExportFormat format)
        {
            switch (format)
            {
                case NeutralExportFormat.StepAp203:
                    return ".step";
                case NeutralExportFormat.Iges:
                    return ".igs";
                case NeutralExportFormat.Acis:
                    return ".sat";
                default:
                    throw new ArgumentOutOfRangeException("format");
            }
        }

        private static string GetFormatDisplayName(NeutralExportFormat format)
        {
            switch (format)
            {
                case NeutralExportFormat.StepAp203:
                    return "STEP AP203";
                case NeutralExportFormat.Iges:
                    return "IGES";
                case NeutralExportFormat.Acis:
                    return "ACIS";
                default:
                    return format.ToString();
            }
        }

        private static bool HasDuplicateOutputPaths(IList<string> paths)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (!seen.Add(path))
                    return true;
            }

            return false;
        }

        private static bool ToBool(object value)
        {
            if (value is bool)
                return (bool)value;

            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        private static class ExportSettings
        {
            private static string SettingsFolder
            {
                get
                {
                    string appData = System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.ApplicationData);
                    return Path.Combine(appData, "CabinTools");
                }
            }

            private static string SettingsPath
            {
                get
                {
                    return Path.Combine(
                        SettingsFolder,
                        "ConfigurationNeutralExport.settings.txt");
                }
            }

            public static string LoadLastFolder()
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                        return string.Empty;

                    return File.ReadAllText(SettingsPath).Trim();
                }
                catch
                {
                    return string.Empty;
                }
            }

            public static void SaveLastFolder(string folder)
            {
                try
                {
                    Directory.CreateDirectory(SettingsFolder);
                    File.WriteAllText(SettingsPath, folder ?? string.Empty);
                }
                catch
                {
                    // Remembering the folder is a convenience only.
                }
            }
        }
    }
}
