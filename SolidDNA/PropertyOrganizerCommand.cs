using System;
using System.Collections.Generic;
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
        public static void ShowPropertyChecker()
        {
            try
            {
                IModelDoc2 drawingDoc = GetActiveDrawing();

                if (drawingDoc == null)
                    return;

                using (PropertyCheckerForm form =
                    new PropertyCheckerForm(drawingDoc))
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

        private static IModelDoc2 GetActiveDrawing()
        {
            ISldWorks swApp =
                SwEnvironment.Application.UnsafeObject;

            if (swApp == null)
            {
                ShowError(
                    "SOLIDWORKS connection is not available.");

                return null;
            }

            IModelDoc2 activeDoc =
                swApp.ActiveDoc as IModelDoc2;

            if (activeDoc == null)
            {
                ShowError("No active document is open.");
                return null;
            }

            if (activeDoc.GetType() !=
                (int)swDocumentTypes_e.swDocDRAWING)
            {
                ShowError(
                    "Property Checker works only when " +
                    "a drawing is active.");

                return null;
            }

            return activeDoc;
        }

        private static void ShowError(string message)
        {
            SwEnvironment.Application.ShowMessageBox(
                message,
                SolidWorksMessageBoxIcon.Stop);
        }
    }

    internal sealed class PropertyCheckerForm : Form
    {
        private readonly IModelDoc2 drawingDoc;
        private readonly TextBox reportTextBox;
        private readonly Button checkOnlyButton;
        private readonly Button repairButton;
        private readonly Label statusLabel;

        public PropertyCheckerForm(IModelDoc2 activeDrawing)
        {
            drawingDoc = activeDrawing;

            Text = "Cabin Tools - Property Checker";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 900;
            Height = 700;
            MinimizeBox = false;

            TableLayoutPanel layout =
                new TableLayoutPanel();

            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12);
            layout.ColumnCount = 1;
            layout.RowCount = 3;

            layout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            layout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100));

            layout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Text =
                "Use Check Only first. Reorder + Repair modifies " +
                "the general custom-property list.";

            layout.Controls.Add(
                statusLabel,
                0,
                0);

            reportTextBox = new TextBox();
            reportTextBox.Multiline = true;
            reportTextBox.ReadOnly = true;
            reportTextBox.ScrollBars =
                ScrollBars.Both;
            reportTextBox.WordWrap = false;
            reportTextBox.Dock = DockStyle.Fill;

            layout.Controls.Add(
                reportTextBox,
                0,
                1);

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

            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(repairButton);
            buttonPanel.Controls.Add(checkOnlyButton);

            layout.Controls.Add(
                buttonPanel,
                0,
                2);

            Controls.Add(layout);

            CancelButton = closeButton;
            AcceptButton = checkOnlyButton;

            Load += PropertyCheckerForm_Load;
        }

        private void PropertyCheckerForm_Load(
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
            PropertyCheckResult checkResult;

            try
            {
                checkResult =
                    CabinPropertyService.Analyze(
                        drawingDoc);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Property check failed.\n\n" +
                    ex.Message,
                    "Cabin Tools - Property Checker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (!checkResult.CanRepair)
            {
                MessageBox.Show(
                    checkResult.RepairBlockReason,
                    "Cabin Tools - Property Checker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                RefreshReport();
                return;
            }

            Dictionary<string, string> suppliedValues =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (checkResult.MissingPromptProperties.Count > 0)
            {
                Dictionary<string, string> suggestions =
                    CabinPropertyService.GetSuggestedMissingValues(
                        drawingDoc,
                        checkResult.MissingPromptProperties);

                using (MissingPropertiesForm missingForm =
                    new MissingPropertiesForm(
                        checkResult.MissingPromptProperties,
                        suggestions))
                {
                    if (missingForm.ShowDialog() !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    suppliedValues =
                        missingForm.Values;
                }
            }

            DialogResult confirmation =
                MessageBox.Show(
                    "Reorder + Repair will:\n\n" +
                    "• Add missing priority properties\n" +
                    "• Apply the values entered in the missing-property form\n" +
                    "• Regenerate Title2 and Title3\n" +
                    "• Delete and recreate the GENERAL custom-property list\n" +
                    "• Apply the requested 21-property priority order\n" +
                    "• Keep all other general properties after that list\n" +
                    "• Create a local backup report\n\n" +
                    "Configuration-specific properties are not changed.\n" +
                    "The drawing will NOT be saved automatically.\n\n" +
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
                        drawingDoc,
                        suppliedValues);

                string addedText =
                    result.AddedProperties.Count == 0
                        ? "No new properties were required."
                        : "Added: " +
                          string.Join(
                              ", ",
                              result.AddedProperties);

                MessageBox.Show(
                    "Property reorder and repair completed.\n\n" +
                    addedText +
                    "\n\nGeneral properties reordered: " +
                    result.ReorderedPropertyCount +
                    "\n\nBackup report:\n" +
                    result.BackupFilePath +
                    "\n\nReview the drawing and custom properties, " +
                    "then save manually.",
                    "Cabin Tools - Property Checker",
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
                PropertyCheckResult result =
                    CabinPropertyService.Analyze(
                        drawingDoc);

                reportTextBox.Text =
                    result.BuildReport();

                repairButton.Enabled =
                    result.CanRepair;

                statusLabel.Text =
                    result.CanRepair
                        ? "Check completed. Reorder + Repair is available."
                        : "Check completed. Reorder + Repair is blocked: " +
                          result.RepairBlockReason;
            }
            catch (Exception ex)
            {
                reportTextBox.Text =
                    "Check failed.\r\n\r\n" +
                    ex.Message;

                repairButton.Enabled = false;

                statusLabel.Text =
                    "Check failed.";
            }
        }
    }
}
