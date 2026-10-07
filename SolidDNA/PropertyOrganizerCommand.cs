using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using CADBooster.SolidDna;
using static CADBooster.SolidDna.SolidWorksEnvironment;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

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
                IApplication.UnsafeObject;

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
            IApplication.ShowMessageBox(
                message,
                SolidWorksMessageBoxIcon.Stop);
        }
    }

    internal sealed class PropertyOrganizerForm : Form
    {
        private readonly IModelDoc2 activeDocument;
        private readonly Label sourceLabel;
        private readonly Label summaryLabel;
        private readonly Button reorderButton;
        private readonly ToolTip toolTip;

        private PropertyOrderDefinition sourceDefinition;

        public PropertyOrganizerForm(IModelDoc2 modelDoc)
        {
            activeDocument = modelDoc;

            Text = "Cabin Tools - Property Checker";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Width = 610;
            Height = 218;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            toolTip = new ToolTip();

            Label heading = new Label();
            heading.Text = "Property Checker";
            heading.Font = new System.Drawing.Font(Font.FontFamily, 11F, System.Drawing.FontStyle.Bold);
            heading.AutoSize = true;
            heading.Left = 18;
            heading.Top = 18;
            Controls.Add(heading);

            Label sourceCaption = new Label();
            sourceCaption.Text = "Reference file:";
            sourceCaption.AutoSize = true;
            sourceCaption.Left = 18;
            sourceCaption.Top = 62;
            Controls.Add(sourceCaption);

            sourceLabel = new Label();
            sourceLabel.AutoEllipsis = true;
            sourceLabel.Left = 110;
            sourceLabel.Top = 60;
            sourceLabel.Width = 330;
            sourceLabel.Height = 24;
            sourceLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            Controls.Add(sourceLabel);

            Button changeSourceButton = new Button();
            changeSourceButton.Text = "Change...";
            changeSourceButton.Left = 458;
            changeSourceButton.Top = 56;
            changeSourceButton.Width = 105;
            changeSourceButton.Height = 28;
            changeSourceButton.Click += SelectSourceButton_Click;
            Controls.Add(changeSourceButton);

            summaryLabel = new Label();
            summaryLabel.Left = 18;
            summaryLabel.Top = 100;
            summaryLabel.Width = 545;
            summaryLabel.Height = 28;
            summaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            Controls.Add(summaryLabel);

            reorderButton = new Button();
            reorderButton.Text = "Reorder...";
            reorderButton.Left = 458;
            reorderButton.Top = 142;
            reorderButton.Width = 105;
            reorderButton.Height = 30;
            reorderButton.Click += ReorderButton_Click;
            Controls.Add(reorderButton);

            AcceptButton = reorderButton;
            Load += delegate { RefreshState(); };
        }

        private void SelectSourceButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog())
            {
                openDialog.Title = "Select Properties.txt";
                openDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                openDialog.CheckFileExists = true;
                openDialog.Multiselect = false;

                string savedPath = PropertyOrderSettings.GetSavedSourceFilePath();
                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    string savedDirectory = Path.GetDirectoryName(savedPath);
                    if (!string.IsNullOrWhiteSpace(savedDirectory) && Directory.Exists(savedDirectory))
                        openDialog.InitialDirectory = savedDirectory;
                }

                if (openDialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    PropertyOrderDefinition definition = PropertyOrderSource.LoadDefinition(openDialog.FileName);
                    PropertyOrderSettings.SaveSourceFilePath(definition.SourcePath);
                    RefreshState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "The selected file could not be used.\r\n\r\n" + ex.Message,
                        "Cabin Tools - Source File",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void ReorderButton_Click(object sender, EventArgs e)
        {
            if (sourceDefinition == null)
            {
                MessageBox.Show(
                    "Select a valid property reference file first.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            PropertyCheckResult currentCheck;
            try
            {
                currentCheck = CabinPropertyService.Analyze(activeDocument, sourceDefinition);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not analyze properties.\r\n\r\n" + ex.Message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RefreshState();
                return;
            }

            if (!currentCheck.CanRepair)
            {
                MessageBox.Show(
                    currentCheck.RepairBlockReason,
                    "Cabin Tools - Reorder Blocked",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RefreshState();
                return;
            }

            Dictionary<string, string> suppliedValues =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                    if (missingForm.ShowDialog(this) != DialogResult.OK)
                        return;
                    suppliedValues = missingForm.Values;
                }
            }

            DialogResult confirmation = MessageBox.Show(
                "Reorder general custom properties using " +
                Path.GetFileName(sourceDefinition.SourcePath) + "?\r\n\r\n" +
                "A local backup report will be created.",
                "Cabin Tools - Reorder Properties",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            try
            {
                PropertyRepairResult result = CabinPropertyService.RepairAndReorder(
                    activeDocument,
                    sourceDefinition,
                    suppliedValues);

                MessageBox.Show(
                    "Property reorder completed.\r\n\r\n" +
                    "Added: " + result.AddedProperties.Count +
                    "\r\nReordered: " + result.ReorderedPropertyCount +
                    "\r\n\r\nReview the document and save manually.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                RefreshState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Cabin Tools - Property Reorder Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RefreshState();
            }
        }

        private void RefreshState()
        {
            try
            {
                sourceDefinition = PropertyOrderSource.LoadSavedDefinition();
                PropertyCheckResult result = CabinPropertyService.Analyze(activeDocument, sourceDefinition);

                string fileName = Path.GetFileName(sourceDefinition.SourcePath);
                sourceLabel.Text = fileName;
                toolTip.SetToolTip(sourceLabel, sourceDefinition.SourcePath);

                int missing = result.MissingOrBlankProperties == null
                    ? 0
                    : result.MissingOrBlankProperties.Count;

                summaryLabel.Text = missing == 0
                    ? "Ready"
                    : missing + " value" + (missing == 1 ? "" : "s") + " required";

                reorderButton.Enabled = result.CanRepair;
            }
            catch (Exception ex)
            {
                sourceDefinition = null;
                sourceLabel.Text = "Not selected";
                toolTip.SetToolTip(sourceLabel, string.Empty);
                summaryLabel.Text = "Select a reference file";
                reorderButton.Enabled = false;

                if (!string.IsNullOrWhiteSpace(ex.Message))
                    toolTip.SetToolTip(summaryLabel, ex.Message);
            }
        }
    }
}
