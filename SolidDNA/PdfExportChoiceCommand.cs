using System.Drawing;
using System.Windows.Forms;

namespace SolidDNA
{
    /// <summary>
    /// Opens a keyboard-friendly selector for the existing automatic-name
    /// and manual-name PDF export workflows.
    /// </summary>
    internal static class PdfExportChoiceCommand
    {
        public static void ShowChoice()
        {
            using (PdfExportChoiceForm form =
                new PdfExportChoiceForm())
            {
                DialogResult result =
                    form.ShowDialog();

                if (result == DialogResult.Yes)
                {
                    PdfExportCommand
                        .ShowAutoNamedBatchExport();

                    return;
                }

                if (result == DialogResult.No)
                {
                    PdfExportCommand
                        .ShowManualNamedBatchExport();
                }
            }
        }
    }

    internal sealed class PdfExportChoiceForm : Form
    {
        public PdfExportChoiceForm()
        {
            Text = "Export PDFs";
            StartPosition =
                FormStartPosition.CenterScreen;
            FormBorderStyle =
                FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(424, 154);
            AutoScaleMode = AutoScaleMode.Font;

            Label instructionLabel =
                new Label
                {
                    AutoSize = false,
                    Text =
                        "How should the PDF filenames be created?",
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    Location = new Point(22, 18),
                    Size = new Size(380, 30)
                };

            Button automaticButton =
                new Button
                {
                    Text = "&Automatic",
                    DialogResult =
                        DialogResult.Yes,
                    Location = new Point(22, 72),
                    Size = new Size(118, 38)
                };

            Button manualButton =
                new Button
                {
                    Text = "&Manual",
                    DialogResult =
                        DialogResult.No,
                    Location = new Point(153, 72),
                    Size = new Size(118, 38)
                };

            Button cancelButton =
                new Button
                {
                    Text = "Cancel",
                    DialogResult =
                        DialogResult.Cancel,
                    Location = new Point(284, 72),
                    Size = new Size(118, 38)
                };

            Controls.Add(instructionLabel);
            Controls.Add(automaticButton);
            Controls.Add(manualButton);
            Controls.Add(cancelButton);

            AcceptButton = automaticButton;
            CancelButton = cancelButton;
        }
    }
}
