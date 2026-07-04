using System;
using System.Windows.Forms;

namespace SolidDNA
{
    internal sealed class PdfPropertyForm : Form
    {
        private readonly TextBox drwNumberTextBox;
        private readonly TextBox revisionTextBox;
        private readonly TextBox cabinTypeDescriptionTextBox;
        private readonly TextBox cabinTypeDefinedTextBox;
        private readonly TextBox layoutTypeTextBox;
        private readonly Label previewLabel;

        public string DrwNumber
        {
            get { return drwNumberTextBox.Text.Trim(); }
        }

        public string Revision
        {
            get { return revisionTextBox.Text.Trim(); }
        }

        public string CabinTypeDescription
        {
            get { return cabinTypeDescriptionTextBox.Text.Trim(); }
        }

        public string CabinTypeDefined
        {
            get { return cabinTypeDefinedTextBox.Text.Trim(); }
        }

        public string LayoutType
        {
            get { return layoutTypeTextBox.Text.Trim(); }
        }

        public PdfPropertyForm(
            string drwNumber,
            string revision,
            string cabinTypeDescription,
            string cabinTypeDefined,
            string layoutType)
        {
            Text = "Cabin Tools - Assign PDF Filename Values";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 720;
            Height = 390;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.RowCount = 8;

            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 190));

            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            drwNumberTextBox = AddTextRow(
                layout,
                0,
                "DrwNumber:",
                drwNumber);

            revisionTextBox = AddTextRow(
                layout,
                1,
                "Revision:",
                revision);

            cabinTypeDescriptionTextBox = AddTextRow(
                layout,
                2,
                "Cabin type description:",
                cabinTypeDescription);

            cabinTypeDefinedTextBox = AddTextRow(
                layout,
                3,
                "Cabin type defined:",
                cabinTypeDefined);

            layoutTypeTextBox = AddTextRow(
                layout,
                4,
                "Layout type:",
                layoutType);

            Label previewCaption = new Label();
            previewCaption.Text = "Filename preview:";
            previewCaption.AutoSize = true;
            previewCaption.Anchor = AnchorStyles.Left;

            layout.Controls.Add(previewCaption, 0, 5);

            previewLabel = new Label();
            previewLabel.AutoSize = false;
            previewLabel.Height = 48;
            previewLabel.Dock = DockStyle.Fill;
            previewLabel.BorderStyle = BorderStyle.FixedSingle;
            previewLabel.Padding = new Padding(6);

            layout.Controls.Add(previewLabel, 1, 5);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.FlowDirection =
                FlowDirection.RightToLeft;
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.AutoSize = true;

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AutoSize = true;

            Button continueButton = new Button();
            continueButton.Text = "Continue to Save";
            continueButton.DialogResult = DialogResult.OK;
            continueButton.AutoSize = true;

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(continueButton);

            layout.Controls.Add(buttonPanel, 1, 7);

            Controls.Add(layout);

            AcceptButton = continueButton;
            CancelButton = cancelButton;

            drwNumberTextBox.TextChanged += UpdatePreview;
            revisionTextBox.TextChanged += UpdatePreview;
            cabinTypeDescriptionTextBox.TextChanged += UpdatePreview;
            cabinTypeDefinedTextBox.TextChanged += UpdatePreview;
            layoutTypeTextBox.TextChanged += UpdatePreview;

            UpdatePreview(null, EventArgs.Empty);
        }

        private TextBox AddTextRow(
            TableLayoutPanel layout,
            int row,
            string labelText,
            string initialValue)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;

            TextBox textBox = new TextBox();
            textBox.Text = initialValue ?? string.Empty;
            textBox.Dock = DockStyle.Fill;

            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(textBox, 1, row);

            return textBox;
        }

        private void UpdatePreview(
            object sender,
            EventArgs e)
        {
            previewLabel.Text =
                DrwNumber + "_" +
                Revision + " " +
                CabinTypeDescription + " " +
                CabinTypeDefined + " - " +
                LayoutType + ".pdf";
        }
    }
}
