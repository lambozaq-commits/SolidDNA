using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SolidDNA
{
    internal sealed class MissingPropertiesForm : Form
    {
        private readonly Dictionary<string, TextBox> inputBoxes;

        public Dictionary<string, string> Values
        {
            get
            {
                Dictionary<string, string> values =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, TextBox> pair in
                    inputBoxes)
                {
                    values[pair.Key] =
                        pair.Value.Text.Trim();
                }

                return values;
            }
        }

        public MissingPropertiesForm(
            List<string> missingProperties,
            Dictionary<string, string> suggestedValues)
        {
            inputBoxes =
                new Dictionary<string, TextBox>(
                    StringComparer.OrdinalIgnoreCase);

            Text = "Cabin Tools - Missing Property Values";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 760;
            Height = 580;

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Padding = new Padding(12);
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 3;

            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100));

            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            Label instructionLabel = new Label();
            instructionLabel.AutoSize = true;
            instructionLabel.Text =
                "Enter any values currently available. You may leave " +
                "fields blank. Blank priority properties will still be " +
                "created or retained blank and moved into the requested " +
                "order. Existing properties are not overwritten when " +
                "their entry is left blank.";

            mainLayout.Controls.Add(
                instructionLabel,
                0,
                0);

            Panel scrollPanel = new Panel();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.AutoScroll = true;

            TableLayoutPanel inputLayout =
                new TableLayoutPanel();

            inputLayout.Dock = DockStyle.Top;
            inputLayout.AutoSize = true;
            inputLayout.Padding = new Padding(0, 10, 0, 0);
            inputLayout.ColumnCount = 2;
            inputLayout.RowCount = missingProperties.Count;

            inputLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 220));

            inputLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            for (int i = 0; i < missingProperties.Count; i++)
            {
                string propertyName = missingProperties[i];

                Label propertyLabel = new Label();
                propertyLabel.Text = propertyName + ":";
                propertyLabel.AutoSize = true;
                propertyLabel.Anchor = AnchorStyles.Left;
                propertyLabel.Margin =
                    new Padding(0, 6, 8, 6);

                TextBox propertyTextBox = new TextBox();
                propertyTextBox.Dock = DockStyle.Fill;
                propertyTextBox.Margin =
                    new Padding(0, 3, 0, 3);

                string suggestedValue = string.Empty;

                if (suggestedValues != null &&
                    suggestedValues.ContainsKey(propertyName))
                {
                    suggestedValue =
                        suggestedValues[propertyName] ??
                        string.Empty;
                }

                propertyTextBox.Text = suggestedValue;

                inputBoxes.Add(
                    propertyName,
                    propertyTextBox);

                inputLayout.Controls.Add(
                    propertyLabel,
                    0,
                    i);

                inputLayout.Controls.Add(
                    propertyTextBox,
                    1,
                    i);
            }

            scrollPanel.Controls.Add(inputLayout);

            mainLayout.Controls.Add(
                scrollPanel,
                0,
                1);

            FlowLayoutPanel buttonPanel =
                new FlowLayoutPanel();

            buttonPanel.FlowDirection =
                FlowDirection.RightToLeft;

            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.AutoSize = true;

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;

            Button continueButton = new Button();
            continueButton.Text =
                "Reorder with Available Values";
            continueButton.AutoSize = true;
            continueButton.Click += ContinueButton_Click;

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(continueButton);

            mainLayout.Controls.Add(
                buttonPanel,
                0,
                2);

            Controls.Add(mainLayout);

            CancelButton = cancelButton;
            AcceptButton = continueButton;
        }

        private void ContinueButton_Click(
            object sender,
            EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
