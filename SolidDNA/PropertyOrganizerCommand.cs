using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment =
    CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    public static class PropertyOrganizerCommand
    {
        public static void ShowOrganizer()
        {
            try
            {
                IModelDoc2 activeDocument =
                    GetActiveSupportedDocument();

                if (activeDocument == null)
                    return;

                using (PropertyOrganizerForm form =
                    new PropertyOrganizerForm(activeDocument))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowError(
                    "Property Checker failed.\n\n" +
                    ex.Message);
            }
        }

        private static IModelDoc2 GetActiveSupportedDocument()
        {
            ISldWorks swApp =
                SwEnvironment.Application.UnsafeObject;

            if (swApp == null)
            {
                ShowError(
                    "SOLIDWORKS connection is not available.");

                return null;
            }

            IModelDoc2 activeDocument =
                swApp.ActiveDoc as IModelDoc2;

            if (activeDocument == null)
            {
                ShowError("No active document is open.");
                return null;
            }

            if (!CabinPropertyService.IsSupportedDocument(
                activeDocument))
            {
                ShowError(
                    "Property Checker supports only parts, " +
                    "assemblies, and drawings.");

                return null;
            }

            return activeDocument;
        }

        private static void ShowError(string message)
        {
            SwEnvironment.Application.ShowMessageBox(
                message,
                SolidWorksMessageBoxIcon.Stop);
        }
    }

    internal sealed class PropertyOrganizerForm : Form
    {
        private readonly IModelDoc2 activeDocument;
        private readonly Label sourceLabel;
        private readonly Label statusLabel;
        private readonly TextBox reportTextBox;
        private readonly Button selectSourceButton;
        private readonly Button reloadButton;
        private readonly Button checkOnlyButton;
        private readonly Button repairButton;

        private PropertyOrderDefinition sourceDefinition;

        public PropertyOrganizerForm(IModelDoc2 modelDoc)
        {
            activeDocument = modelDoc;

            Text = "Cabin Tools - Property Checker";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 950;
            Height = 720;
            MinimizeBox = false;

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Padding = new Padding(12);
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 4;

            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100));

            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            sourceLabel = new Label();
            sourceLabel.AutoSize = true;
            sourceLabel.Text =
                "Property-order source: not configured.";

            mainLayout.Controls.Add(
                sourceLabel,
                0,
                0);

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Text =
                "Scope: General custom properties in the active document.";

            mainLayout.Controls.Add(
                statusLabel,
                0,
                1);

            reportTextBox = new TextBox();
            reportTextBox.Multiline = true;
            reportTextBox.ReadOnly = true;
            reportTextBox.ScrollBars = ScrollBars.Both;
            reportTextBox.WordWrap = false;
            reportTextBox.Dock = DockStyle.Fill;

            mainLayout.Controls.Add(
                reportTextBox,
                0,
                2);

            FlowLayoutPanel buttonPanel =
                new FlowLayoutPanel();

            buttonPanel.FlowDirection =
                FlowDirection.RightToLeft;

            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.AutoSize = true;

            Button closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.DialogResult = DialogResult.Cancel;

            repairButton = new Button();
            repairButton.Text = "Reorder + Repair";
            repairButton.AutoSize = true;
            repairButton.Click += RepairButton_Click;

            checkOnlyButton = new Button();
            checkOnlyButton.Text = "Check Only";
            checkOnlyButton.AutoSize = true;
            checkOnlyButton.Click += CheckOnlyButton_Click;

            reloadButton = new Button();
            reloadButton.Text = "Reload Source";
            reloadButton.AutoSize = true;
            reloadButton.Click += ReloadButton_Click;

            selectSourceButton = new Button();
            selectSourceButton.Text = "Select Properties.txt...";
            selectSourceButton.AutoSize = true;
            selectSourceButton.Click += SelectSourceButton_Click;

            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(repairButton);
            buttonPanel.Controls.Add(checkOnlyButton);
            buttonPanel.Controls.Add(reloadButton);
            buttonPanel.Controls.Add(selectSourceButton);

            mainLayout.Controls.Add(
                buttonPanel,
                0,
                3);

            Controls.Add(mainLayout);

            CancelButton = closeButton;
            AcceptButton = checkOnlyButton;

            Load += PropertyOrganizerForm_Load;
        }

        private void PropertyOrganizerForm_Load(
            object sender,
            EventArgs e)
        {
            RefreshReport();
        }

        private void SelectSourceButton_Click(
            object sender,
            EventArgs e)
        {
            using (OpenFileDialog openDialog =
                new OpenFileDialog())
            {
                openDialog.Title =
                    "Select Cabin Tools Properties.txt";

                openDialog.Filter =
                    "Text files (*.txt)|*.txt|All files (*.*)|*.*";

                openDialog.CheckFileExists = true;
                openDialog.Multiselect = false;

                string savedPath =
                    PropertyOrderSettings.GetSavedSourceFilePath();

                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    string savedDirectory =
                        Path.GetDirectoryName(savedPath);

                    if (!string.IsNullOrWhiteSpace(savedDirectory) &&
                        Directory.Exists(savedDirectory))
                    {
                        openDialog.InitialDirectory =
                            savedDirectory;
                    }
                }

                if (openDialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    PropertyOrderDefinition definition =
                        PropertyOrderSource.LoadDefinition(
                            openDialog.FileName);

                    PropertyOrderSettings.SaveSourceFilePath(
                        definition.SourcePath);

                    RefreshReport();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "The selected file could not be used.\n\n" +
                        ex.Message,
                        "Cabin Tools - Source File Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void ReloadButton_Click(
            object sender,
            EventArgs e)
        {
            RefreshReport();
        }

        private void CheckOnlyButton_Click(
            object sender,
            EventArgs e)
        {
            RefreshReport();
        }

        private void RepairButton_Click(
            object sender,
            EventArgs e)
        {
            if (sourceDefinition == null)
            {
                MessageBox.Show(
                    "Select a valid Properties.txt source file first.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            PropertyCheckResult currentCheck;

            try
            {
                currentCheck =
                    CabinPropertyService.Analyze(
                        activeDocument,
                        sourceDefinition);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not analyze properties.\n\n" +
                    ex.Message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                RefreshReport();
                return;
            }

            if (!currentCheck.CanRepair)
            {
                MessageBox.Show(
                    currentCheck.RepairBlockReason,
                    "Cabin Tools - Reorder Blocked",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                RefreshReport();
                return;
            }

            Dictionary<string, string> suppliedValues =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (currentCheck.MissingOrBlankProperties.Count > 0)
            {
                Dictionary<string, string> suggestedValues =
                    CabinPropertyService.GetSuggestedValues(
                        activeDocument,
                        currentCheck.MissingOrBlankProperties);

                using (MissingPropertiesForm missingForm =
                    new MissingPropertiesForm(
                        currentCheck.MissingOrBlankProperties,
                        suggestedValues))
                {
                    if (missingForm.ShowDialog() !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    suppliedValues = missingForm.Values;
                }
            }

            DialogResult confirmation =
                MessageBox.Show(
                    "Reorder + Repair will:\n\n" +
                    "• Read the priority order from the selected " +
                    "Properties.txt file\n" +
                    "• Add missing priority properties as blank " +
                    "text properties\n" +
                    "• Apply any non-blank values entered in the " +
                    "previous form\n" +
                    "• Delete and recreate only GENERAL custom " +
                    "properties\n" +
                    "• Move source-file properties to the top\n" +
                    "• Keep all other general properties in their " +
                    "current relative order\n" +
                    "• Create a local backup report\n\n" +
                    "Configuration-specific and cut-list properties " +
                    "are not changed.\n\n" +
                    "Continue?",
                    "Cabin Tools - Confirm Property Reorder",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

            if (confirmation != DialogResult.Yes)
                return;

            try
            {
                PropertyRepairResult result =
                    CabinPropertyService.RepairAndReorder(
                        activeDocument,
                        sourceDefinition,
                        suppliedValues);

                string addedText =
                    result.AddedProperties.Count == 0
                        ? "No new priority properties were required."
                        : "Added: " +
                          string.Join(
                              ", ",
                              result.AddedProperties);

                MessageBox.Show(
                    "Property reorder completed.\n\n" +
                    addedText +
                    "\n\nGeneral properties reordered: " +
                    result.ReorderedPropertyCount.ToString() +
                    "\n\nBackup report:\n" +
                    result.BackupFilePath +
                    "\n\nThe document is now modified. " +
                    "Review it, then save manually.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                RefreshReport();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Cabin Tools - Property Reorder Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                RefreshReport();
            }
        }

        private void RefreshReport()
        {
            try
            {
                sourceDefinition =
                    PropertyOrderSource.LoadSavedDefinition();

                PropertyCheckResult result =
                    CabinPropertyService.Analyze(
                        activeDocument,
                        sourceDefinition);

                sourceLabel.Text =
                    "Property-order source: " +
                    sourceDefinition.SourcePath +
                    "  (updated " +
                    sourceDefinition.SourceLastWriteTime.ToString(
                        "yyyy-MM-dd HH:mm:ss") +
                    ")";

                reportTextBox.Text = result.BuildReport();

                repairButton.Enabled =
                    result.CanRepair;

                checkOnlyButton.Enabled = true;
                reloadButton.Enabled = true;

                statusLabel.Text =
                    result.CanRepair
                        ? "Scope: General custom properties in the active " +
                          result.DocumentTypeName +
                          ". Reorder + Repair is available."
                        : "Scope: General custom properties in the active " +
                          result.DocumentTypeName +
                          ". Reorder + Repair is blocked: " +
                          result.RepairBlockReason;
            }
            catch (Exception ex)
            {
                sourceDefinition = null;

                sourceLabel.Text =
                    "Property-order source: not available.";

                reportTextBox.Text =
                    "Property Checker cannot load the configured " +
                    "Properties.txt source.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nClick 'Select Properties.txt...' and choose " +
                    "the source file from the local PDM template folder. " +
                    "Use PDM Get Latest on the file when the source order " +
                    "has been updated.";

                repairButton.Enabled = false;
                checkOnlyButton.Enabled = true;
                reloadButton.Enabled = true;

                statusLabel.Text =
                    "Select a valid Properties.txt source file to enable " +
                    "Reorder + Repair.";
            }
        }
    }
}
