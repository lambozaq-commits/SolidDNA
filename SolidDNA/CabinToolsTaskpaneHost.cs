using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;

using static CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// WinForms host control created by SOLIDWORKS through COM and placed in
    /// the Cabin Tools taskpane.
    ///
    /// Keep the constructor parameterless. SOLIDWORKS creates this control by
    /// ProgId and cannot supply constructor parameters.
    /// </summary>
    [Guid("F8F0D651-4F3A-4202-A5F0-923E4F00EABA")]
    [ProgId(TaskpaneProgId)]
    [ComVisible(true)]
    public sealed class CabinToolsTaskpaneHost :
        UserControl,
        ITaskpaneControl
    {
        private const string TaskpaneProgId =
            "CabinTools.SolidDNA.TaskpaneHost";

        private Label documentTypeValueLabel;
        private Label documentNameValueLabel;
        private Label documentPathValueLabel;
        private Label activeConfigurationValueLabel;
        private Label documentDescriptionScopeLabel;
        private Label configurationDescriptionScopeLabel;
        private TextBox documentDescriptionTextBox;
        private TextBox configurationDescriptionTextBox;
        private Label statusLabel;
        private Button refreshButton;
        private Button saveDescriptionsButton;
        private Button propertyCheckerButton;
        private Button exportAutoPdfButton;
        private Button exportManualPdfButton;

        private string loadedDocumentPath;
        private string loadedDocumentTitle;
        private string loadedConfigurationName;
        private bool eventsSubscribed;

        public static CabinToolsTaskpaneHost Instance
        {
            get;
            private set;
        }

        public string ProgId
        {
            get
            {
                return TaskpaneProgId;
            }
        }

        public CabinToolsTaskpaneHost()
        {
            // Some SOLIDWORKS taskpane hosts do not raise the regular WinForms
            // Load event reliably. Keep the control instance available from
            // construction, then initialize once the handle exists.
            Instance = this;

            BuildUserInterface();
            WriteTaskpaneDiagnostic("Taskpane host constructed.");

            HandleCreated += CabinToolsTaskpaneHost_HandleCreated;
            Disposed += CabinToolsTaskpaneHost_Disposed;
        }

        public static void RefreshActiveDocument()
        {
            CabinToolsTaskpaneHost instance = Instance;

            if (instance == null)
                return;

            instance.ScheduleRefresh();
        }

        private void CabinToolsTaskpaneHost_HandleCreated(
            object sender,
            EventArgs e)
        {
            Instance = this;

            try
            {
                SubscribeToSolidWorksEvents();
            }
            catch (Exception ex)
            {
                WriteTaskpaneDiagnostic(
                    "Active-document event subscription failed: " + ex.Message);
            }

            WriteTaskpaneDiagnostic("Taskpane host handle created.");
            RefreshFromActiveDocument();
        }

        private void CabinToolsTaskpaneHost_Disposed(
            object sender,
            EventArgs e)
        {
            UnsubscribeFromSolidWorksEvents();

            if (object.ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private void SubscribeToSolidWorksEvents()
        {
            if (eventsSubscribed)
                return;

            IApplication.ActiveModelInformationChanged +=
                IApplication_ActiveModelInformationChanged;

            eventsSubscribed = true;
        }

        private void UnsubscribeFromSolidWorksEvents()
        {
            if (!eventsSubscribed)
                return;

            try
            {
                IApplication.ActiveModelInformationChanged -=
                    IApplication_ActiveModelInformationChanged;
            }
            catch
            {
                // SOLIDWORKS can already be shutting down. The taskpane is
                // still safe to dispose in that case.
            }

            eventsSubscribed = false;
        }

        private void IApplication_ActiveModelInformationChanged(
            Model model)
        {
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (IsDisposed ||
                Disposing ||
                !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(
                        new MethodInvoker(
                            RefreshFromActiveDocument));
                }
                else
                {
                    RefreshFromActiveDocument();
                }
            }
            catch (InvalidOperationException)
            {
                // The control handle may be closing while SOLIDWORKS unloads
                // the add-in.
            }
        }

        private void BuildUserInterface()
        {
            BackColor = SystemColors.Control;

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock = DockStyle.Fill;
            mainLayout.AutoScroll = true;
            mainLayout.Padding = new Padding(10);
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));

            Controls.Add(mainLayout);

            Label headingLabel =
                new Label();

            headingLabel.AutoSize = true;
            headingLabel.Font = new Font(
                Font,
                FontStyle.Bold);
            headingLabel.Text = "Cabin Tools";
            headingLabel.Margin =
                new Padding(0, 0, 0, 8);

            mainLayout.Controls.Add(
                headingLabel,
                0,
                0);

            mainLayout.Controls.Add(
                BuildDocumentContextGroup(),
                0,
                1);

            mainLayout.Controls.Add(
                BuildDescriptionGroup(),
                0,
                2);

            mainLayout.Controls.Add(
                BuildCommandButtons(),
                0,
                3);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.BorderStyle = BorderStyle.FixedSingle;
            statusLabel.Padding = new Padding(7);
            statusLabel.TextAlign =
                ContentAlignment.TopLeft;
            statusLabel.Text =
                "Cabin Tools taskpane host loaded. Click Refresh to read the active SOLIDWORKS document.";

            mainLayout.Controls.Add(
                statusLabel,
                0,
                4);
        }

        private Control BuildDocumentContextGroup()
        {
            GroupBox contextGroup =
                new GroupBox();

            contextGroup.Text = "Active document";
            contextGroup.Dock = DockStyle.Top;
            contextGroup.AutoSize = true;
            contextGroup.Padding = new Padding(8);

            TableLayoutPanel contextLayout =
                new TableLayoutPanel();

            contextLayout.Dock = DockStyle.Top;
            contextLayout.AutoSize = true;
            contextLayout.ColumnCount = 2;
            contextLayout.RowCount = 4;
            contextLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Absolute,
                    105));
            contextLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    100));

            contextLayout.Controls.Add(
                CreateCaptionLabel("Type:"),
                0,
                0);

            documentTypeValueLabel =
                CreateValueLabel();

            contextLayout.Controls.Add(
                documentTypeValueLabel,
                1,
                0);

            contextLayout.Controls.Add(
                CreateCaptionLabel("Document:"),
                0,
                1);

            documentNameValueLabel =
                CreateValueLabel();

            contextLayout.Controls.Add(
                documentNameValueLabel,
                1,
                1);

            contextLayout.Controls.Add(
                CreateCaptionLabel("Path:"),
                0,
                2);

            documentPathValueLabel =
                CreateValueLabel();

            contextLayout.Controls.Add(
                documentPathValueLabel,
                1,
                2);

            contextLayout.Controls.Add(
                CreateCaptionLabel("Configuration:"),
                0,
                3);

            activeConfigurationValueLabel =
                CreateValueLabel();

            contextLayout.Controls.Add(
                activeConfigurationValueLabel,
                1,
                3);

            contextGroup.Controls.Add(contextLayout);

            return contextGroup;
        }

        private Control BuildDescriptionGroup()
        {
            GroupBox descriptionGroup =
                new GroupBox();

            descriptionGroup.Text =
                "Description property layers";
            descriptionGroup.Dock = DockStyle.Top;
            descriptionGroup.AutoSize = true;
            descriptionGroup.Padding = new Padding(8);
            descriptionGroup.Margin =
                new Padding(0, 8, 0, 0);

            TableLayoutPanel descriptionLayout =
                new TableLayoutPanel();

            descriptionLayout.Dock = DockStyle.Top;
            descriptionLayout.AutoSize = true;
            descriptionLayout.ColumnCount = 1;
            descriptionLayout.RowCount = 4;
            descriptionLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    100));

            documentDescriptionScopeLabel =
                CreateScopeLabel();

            documentDescriptionScopeLabel.Text =
                "Document Description (general custom property)";

            descriptionLayout.Controls.Add(
                documentDescriptionScopeLabel,
                0,
                0);

            documentDescriptionTextBox =
                new TextBox();

            documentDescriptionTextBox.Dock = DockStyle.Top;
            documentDescriptionTextBox.Margin =
                new Padding(0, 2, 0, 8);

            descriptionLayout.Controls.Add(
                documentDescriptionTextBox,
                0,
                1);

            configurationDescriptionScopeLabel =
                CreateScopeLabel();

            configurationDescriptionScopeLabel.Text =
                "Configuration Description (configuration-specific property)";

            descriptionLayout.Controls.Add(
                configurationDescriptionScopeLabel,
                0,
                2);

            configurationDescriptionTextBox =
                new TextBox();

            configurationDescriptionTextBox.Dock = DockStyle.Top;
            configurationDescriptionTextBox.Margin =
                new Padding(0, 2, 0, 0);

            descriptionLayout.Controls.Add(
                configurationDescriptionTextBox,
                0,
                3);

            descriptionGroup.Controls.Add(
                descriptionLayout);

            return descriptionGroup;
        }

        private Control BuildCommandButtons()
        {
            FlowLayoutPanel buttonPanel =
                new FlowLayoutPanel();

            buttonPanel.Dock = DockStyle.Top;
            buttonPanel.AutoSize = true;
            buttonPanel.FlowDirection =
                FlowDirection.LeftToRight;
            buttonPanel.WrapContents = true;
            buttonPanel.Margin =
                new Padding(0, 8, 0, 8);

            refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.AutoSize = true;
            refreshButton.Click += RefreshButton_Click;

            saveDescriptionsButton = new Button();
            saveDescriptionsButton.Text =
                "Save descriptions";
            saveDescriptionsButton.AutoSize = true;
            saveDescriptionsButton.Click +=
                SaveDescriptionsButton_Click;

            propertyCheckerButton = new Button();
            propertyCheckerButton.Text =
                "Property Checker";
            propertyCheckerButton.AutoSize = true;
            propertyCheckerButton.Click +=
                PropertyCheckerButton_Click;

            exportAutoPdfButton = new Button();
            exportAutoPdfButton.Text = "Export PDFs (Auto)";
            exportAutoPdfButton.AutoSize = true;
            exportAutoPdfButton.Click +=
                ExportAutoPdfButton_Click;

            exportManualPdfButton = new Button();
            exportManualPdfButton.Text = "Export PDFs (Manual)";
            exportManualPdfButton.AutoSize = true;
            exportManualPdfButton.Click +=
                ExportManualPdfButton_Click;

            buttonPanel.Controls.Add(refreshButton);
            buttonPanel.Controls.Add(saveDescriptionsButton);
            buttonPanel.Controls.Add(propertyCheckerButton);
            buttonPanel.Controls.Add(exportAutoPdfButton);
            buttonPanel.Controls.Add(exportManualPdfButton);

            return buttonPanel;
        }

        private static Label CreateCaptionLabel(
            string text)
        {
            Label label = new Label();

            label.Text = text;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.TopLeft;
            label.Margin = new Padding(0, 2, 8, 2);

            return label;
        }

        private static Label CreateValueLabel()
        {
            Label label = new Label();

            label.AutoSize = true;
            label.MaximumSize = new Size(300, 0);
            label.Text = "-";
            label.Margin = new Padding(0, 2, 0, 2);

            return label;
        }

        private static Label CreateScopeLabel()
        {
            Label label = new Label();

            label.AutoSize = true;
            label.Font = new Font(
                SystemFonts.DefaultFont,
                FontStyle.Bold);

            return label;
        }

        private void RefreshFromActiveDocument()
        {
            IModelDoc2 activeDocument =
                CabinCustomPropertyStore
                    .GetActiveModelDocument();

            if (!CabinCustomPropertyStore
                .IsSupportedDocument(activeDocument))
            {
                ShowNoSupportedDocumentState();
                return;
            }

            string documentType =
                CabinCustomPropertyStore
                    .GetDocumentTypeName(activeDocument);

            string documentTitle =
                activeDocument.GetTitle() ?? string.Empty;

            string documentPath =
                activeDocument.GetPathName() ?? string.Empty;

            bool supportsConfiguration =
                CabinCustomPropertyStore
                    .SupportsConfigurationProperties(
                        activeDocument);

            string configurationName =
                supportsConfiguration
                    ? CabinCustomPropertyStore
                        .GetActiveConfigurationName(
                            activeDocument)
                    : string.Empty;

            loadedDocumentTitle = documentTitle;
            loadedDocumentPath = documentPath;
            loadedConfigurationName = configurationName;

            documentTypeValueLabel.Text = documentType;
            documentNameValueLabel.Text =
                string.IsNullOrWhiteSpace(documentTitle)
                    ? "<untitled>"
                    : documentTitle;

            documentPathValueLabel.Text =
                string.IsNullOrWhiteSpace(documentPath)
                    ? "<unsaved document>"
                    : documentPath;

            activeConfigurationValueLabel.Text =
                supportsConfiguration
                    ? DisplayValue(configurationName)
                    : "Not applicable";

            try
            {
                documentDescriptionTextBox.Text =
                    CabinCustomPropertyStore.ReadText(
                        activeDocument,
                        CabinCustomPropertyStore
                            .DescriptionPropertyName,
                        CabinPropertyScope.Document,
                        true);

                configurationDescriptionScopeLabel.Enabled =
                    supportsConfiguration;

                configurationDescriptionTextBox.Enabled =
                    supportsConfiguration;

                if (supportsConfiguration)
                {
                    configurationDescriptionScopeLabel.Text =
                        "Configuration Description (" +
                        CabinCustomPropertyStore
                            .GetScopeDisplayName(
                                activeDocument,
                                CabinPropertyScope
                                    .ActiveConfiguration) +
                        ")";

                    configurationDescriptionTextBox.Text =
                        CabinCustomPropertyStore.ReadText(
                            activeDocument,
                            CabinCustomPropertyStore
                                .DescriptionPropertyName,
                            CabinPropertyScope
                                .ActiveConfiguration,
                            true);
                }
                else
                {
                    configurationDescriptionScopeLabel.Text =
                        "Configuration Description " +
                        "(not available for drawings)";

                    configurationDescriptionTextBox.Text =
                        string.Empty;
                }

                exportAutoPdfButton.Enabled = true;
                exportManualPdfButton.Enabled = true;

                saveDescriptionsButton.Enabled = true;

                string writeBlockReason =
                    CabinCustomPropertyStore
                        .GetWriteBlockReason(
                            activeDocument);

                statusLabel.Text =
                    string.IsNullOrWhiteSpace(
                        writeBlockReason)
                        ? "Ready. The taskpane reads Description from " +
                          "both property layers separately. " +
                          "Saving changes does not save the SOLIDWORKS file automatically."
                        : "Read-only status: " +
                          writeBlockReason;
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    "Could not read custom properties.\r\n\r\n" +
                    ex.Message;

                saveDescriptionsButton.Enabled = false;
                exportAutoPdfButton.Enabled = true;
                exportManualPdfButton.Enabled = true;
            }
        }

        private void ShowNoSupportedDocumentState()
        {
            loadedDocumentTitle = string.Empty;
            loadedDocumentPath = string.Empty;
            loadedConfigurationName = string.Empty;

            documentTypeValueLabel.Text =
                "No supported document";
            documentNameValueLabel.Text = "-";
            documentPathValueLabel.Text = "-";
            activeConfigurationValueLabel.Text = "-";

            documentDescriptionTextBox.Text =
                string.Empty;

            configurationDescriptionTextBox.Text =
                string.Empty;

            configurationDescriptionTextBox.Enabled =
                false;

            configurationDescriptionScopeLabel.Enabled =
                false;

            configurationDescriptionScopeLabel.Text =
                "Configuration Description " +
                "(not available)";

            saveDescriptionsButton.Enabled = false;
            exportAutoPdfButton.Enabled = true;
            exportManualPdfButton.Enabled = true;

            statusLabel.Text =
                "Open a SOLIDWORKS part, assembly, or drawing to use property tools. Batch PDF export can still select drawings from disk.";
        }

        private void RefreshButton_Click(
            object sender,
            EventArgs e)
        {
            RefreshFromActiveDocument();
        }

        private void SaveDescriptionsButton_Click(
            object sender,
            EventArgs e)
        {
            IModelDoc2 activeDocument =
                CabinCustomPropertyStore
                    .GetActiveModelDocument();

            if (!CabinCustomPropertyStore
                .IsSupportedDocument(activeDocument))
            {
                ShowNoSupportedDocumentState();
                return;
            }

            string activeTitle =
                activeDocument.GetTitle() ?? string.Empty;

            string activePath =
                activeDocument.GetPathName() ?? string.Empty;

            bool supportsConfiguration =
                CabinCustomPropertyStore
                    .SupportsConfigurationProperties(
                        activeDocument);

            string activeConfiguration =
                supportsConfiguration
                    ? CabinCustomPropertyStore
                        .GetActiveConfigurationName(
                            activeDocument)
                    : string.Empty;

            if (!string.Equals(
                    loadedDocumentTitle ?? string.Empty,
                    activeTitle,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    loadedDocumentPath ?? string.Empty,
                    activePath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    loadedConfigurationName ?? string.Empty,
                    activeConfiguration,
                    StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "The active document or configuration changed since the " +
                    "taskpane was last refreshed. The descriptions were not " +
                    "written. Review the current values and click Refresh first.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                RefreshFromActiveDocument();
                return;
            }

            try
            {
                CabinCustomPropertyStore.EnsureCanWrite(
                    activeDocument);

                CabinCustomPropertyStore.SetText(
                    activeDocument,
                    CabinCustomPropertyStore
                        .DescriptionPropertyName,
                    documentDescriptionTextBox.Text,
                    CabinPropertyScope.Document);

                if (supportsConfiguration)
                {
                    CabinCustomPropertyStore.SetText(
                        activeDocument,
                        CabinCustomPropertyStore
                            .DescriptionPropertyName,
                        configurationDescriptionTextBox.Text,
                        CabinPropertyScope
                            .ActiveConfiguration);
                }

                activeDocument.ForceRebuild3(false);

                statusLabel.Text =
                    "Description properties were written to the active " +
                    "SOLIDWORKS document. The file was not saved automatically.";

                RefreshFromActiveDocument();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Cabin Tools could not save the description properties.\r\n\r\n" +
                    ex.Message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void PropertyCheckerButton_Click(
            object sender,
            EventArgs e)
        {
            PropertyOrganizerCommand.ShowOrganizer();
            RefreshFromActiveDocument();
        }

        private void ExportAutoPdfButton_Click(
            object sender,
            EventArgs e)
        {
            PdfExportCommand.ShowAutoNamedBatchExport();
            RefreshFromActiveDocument();
        }

        private void ExportManualPdfButton_Click(
            object sender,
            EventArgs e)
        {
            PdfExportCommand.ShowManualNamedBatchExport();
            RefreshFromActiveDocument();
        }

        private static void WriteTaskpaneDiagnostic(
            string message)
        {
            try
            {
                string folder = Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.ApplicationData),
                    "CabinTools",
                    "Logs");

                Directory.CreateDirectory(folder);

                File.AppendAllText(
                    Path.Combine(folder, "Taskpane.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " - " + message +
                    System.Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never block the SOLIDWORKS taskpane.
            }
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }
    }
}
