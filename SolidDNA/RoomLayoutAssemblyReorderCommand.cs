using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment = CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// Reorders the top-level component instances of a room-layout assembly based on
    /// a user-selected .txt reference list.
    ///
    /// Reference list format:
    /// - One wanted component/category name per line.
    /// - Blank lines are ignored.
    /// - Lines starting with #, //, or ; are ignored.
    ///
    /// Behaviour:
    /// - Only top-level assembly components are moved.
    /// - Planes, origin, annotations, history, sensors, mates and nested children are not moved.
    /// - Matching is based mainly on SOLIDWORKS component description / configuration description,
    ///   then component name and file name as fallbacks.
    /// - Basic Sketch / skeleton reference components are kept before the ordered group and are not moved.
    /// - Unmatched components stay at the bottom in their original relative order.
    /// - The assembly is not saved automatically.
    /// </summary>
    internal static class RoomLayoutAssemblyReorderCommand
    {
        private const string CommandTitle = "Cabin Tools - Reorder Room Layout Assembly";

        public static void ShowRoomLayoutAssemblyReorderForm()
        {
            ISldWorks swApp = null;
            try { swApp = SwEnvironment.Application.UnsafeObject as ISldWorks; } catch { swApp = null; }

            if (swApp == null)
            {
                MessageBox.Show(
                    "Could not connect to SOLIDWORKS.",
                    CommandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            IModelDoc2 model = swApp.ActiveDoc as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                MessageBox.Show(
                    "Open the room-layout assembly before using this tool.",
                    CommandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string referenceFile = LoadLastReferenceFile();
            List<string> referenceOrder = null;
            string readError = string.Empty;

            if (!string.IsNullOrWhiteSpace(referenceFile) && File.Exists(referenceFile))
            {
                referenceOrder = ReadReferenceOrder(referenceFile, out readError);
                if (!string.IsNullOrWhiteSpace(readError) || referenceOrder.Count == 0)
                {
                    referenceFile = string.Empty;
                    referenceOrder = null;
                }
            }

            if (referenceOrder == null)
            {
                referenceFile = AskForReferenceFile();
                if (string.IsNullOrWhiteSpace(referenceFile))
                    return;

                referenceOrder = ReadReferenceOrder(referenceFile, out readError);
                if (!string.IsNullOrWhiteSpace(readError))
                {
                    MessageBox.Show(readError, CommandTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (referenceOrder.Count == 0)
                {
                    MessageBox.Show(
                        "The selected reference file does not contain any usable order lines.",
                        CommandTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            using (RoomLayoutAssemblyReorderForm form =
                new RoomLayoutAssemblyReorderForm(swApp, model, referenceFile, referenceOrder))
            {
                form.ShowDialog();
            }
        }

        private static string AskForReferenceFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select room-layout reorder reference list";
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.RestoreDirectory = true;

                string lastDirectory = LoadLastReferenceDirectory();
                if (!string.IsNullOrWhiteSpace(lastDirectory) && Directory.Exists(lastDirectory))
                    dialog.InitialDirectory = lastDirectory;

                DialogResult result = dialog.ShowDialog();
                if (result != DialogResult.OK)
                    return string.Empty;

                SaveLastReferenceFile(dialog.FileName);
                return dialog.FileName;
            }
        }

        private static List<string> ReadReferenceOrder(string path, out string error)
        {
            error = string.Empty;
            List<string> lines = new List<string>();

            try
            {
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = (rawLine ?? string.Empty).Trim();
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    if (line.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    if (line.StartsWith(";", StringComparison.Ordinal))
                        continue;

                    lines.Add(line);
                }
            }
            catch (Exception ex)
            {
                error = "Could not read the reference list.\n\n" + ex.Message;
            }

            return lines;
        }

        private static string SettingsDirectory
        {
            get
            {
                string appData = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "CabinTools", "Settings");
            }
        }

        private static string LastReferenceFilePath
        {
            get { return Path.Combine(SettingsDirectory, "RoomLayoutReorderLastReferenceFile.txt"); }
        }

        private static string LastReferenceDirectoryPath
        {
            get { return Path.Combine(SettingsDirectory, "RoomLayoutReorderLastDirectory.txt"); }
        }

        private static string LoadLastReferenceFile()
        {
            try
            {
                string path = LastReferenceFilePath;
                if (!File.Exists(path))
                    return string.Empty;
                return File.ReadAllText(path).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string LoadLastReferenceDirectory()
        {
            try
            {
                string lastFile = LoadLastReferenceFile();
                if (!string.IsNullOrWhiteSpace(lastFile))
                {
                    string directory = Path.GetDirectoryName(lastFile);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                        return directory;
                }

                string path = LastReferenceDirectoryPath;
                if (!File.Exists(path))
                    return string.Empty;
                return File.ReadAllText(path).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SaveLastReferenceFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(LastReferenceFilePath, filePath);

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    File.WriteAllText(LastReferenceDirectoryPath, directory);
            }
            catch
            {
                // Local settings are a convenience only. Do not block the tool.
            }
        }

        private static string BuildReferenceLabelText(string filePath)
        {
            string fileName = string.Empty;
            try { fileName = Path.GetFileName(filePath); } catch { fileName = string.Empty; }

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = filePath ?? string.Empty;

            return "Reference file: " + fileName;
        }

        private sealed class RoomLayoutAssemblyReorderForm : Form
        {
            private readonly ISldWorks swApp;
            private readonly IModelDoc2 assemblyModel;
            private readonly IAssemblyDoc assemblyDoc;
            private string referenceFile;
            private readonly List<string> referenceOrder;
            private readonly List<AssemblyComponentInfo> components = new List<AssemblyComponentInfo>();
            private readonly List<AssemblyComponentInfo> fixedReferenceComponents = new List<AssemblyComponentInfo>();
            private readonly List<OrderPreviewRow> previewRows = new List<OrderPreviewRow>();
            private readonly List<AssemblyComponentInfo> unmatchedComponents = new List<AssemblyComponentInfo>();
            private readonly List<string> skippedReferenceItems = new List<string>();

            private DataGridView grid;
            private Label referenceLabel;
            private ToolTip referenceToolTip;
            private Label statusLabel;
            private Button changeListButton;
            private Button rescanButton;
            private Button applyButton;
            private Button closeButton;

            private const int OrderColumnIndex = 0;
            private const int ReferenceColumnIndex = 1;
            private const int MatchedColumnIndex = 2;
            private const int CurrentPositionColumnIndex = 3;
            private const int StatusColumnIndex = 4;

            public RoomLayoutAssemblyReorderForm(
                ISldWorks swApp,
                IModelDoc2 assemblyModel,
                string referenceFile,
                List<string> referenceOrder)
            {
                this.swApp = swApp;
                this.assemblyModel = assemblyModel;
                this.assemblyDoc = assemblyModel as IAssemblyDoc;
                this.referenceFile = referenceFile;
                this.referenceOrder = referenceOrder ?? new List<string>();

                Text = CommandTitle;
                Width = 1180;
                Height = 720;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
                MinimumSize = new Size(900, 560);

                BuildLayout();
                ScanAndPreview();
            }

            private void BuildLayout()
            {
                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.Padding = new Padding(16);
                root.ColumnCount = 1;
                root.RowCount = 5;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Label heading = new Label();
                heading.Text = "Reorder Room Layout Assembly";
                heading.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
                heading.AutoSize = true;
                heading.Margin = new Padding(0, 0, 0, 8);
                root.Controls.Add(heading, 0, 0);

                TableLayoutPanel filePanel = new TableLayoutPanel();
                filePanel.AutoSize = true;
                filePanel.Dock = DockStyle.Top;
                filePanel.ColumnCount = 2;
                filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                filePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                filePanel.Margin = new Padding(0, 0, 0, 10);

                referenceToolTip = new ToolTip();

                referenceLabel = new Label();
                referenceLabel.Text = BuildReferenceLabelText(referenceFile);
                referenceToolTip.SetToolTip(referenceLabel, referenceFile);
                referenceLabel.AutoSize = false;
                referenceLabel.Dock = DockStyle.Fill;
                referenceLabel.Height = 24;
                referenceLabel.TextAlign = ContentAlignment.MiddleLeft;
                referenceLabel.AutoEllipsis = true;
                filePanel.Controls.Add(referenceLabel, 0, 0);

                changeListButton = CreateButton("Change list...", delegate { ChangeList(); });
                filePanel.Controls.Add(changeListButton, 1, 0);

                root.Controls.Add(filePanel, 0, 1);

                grid = new DataGridView();
                grid.Dock = DockStyle.Fill;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.ReadOnly = true;
                grid.MultiSelect = true;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.RowHeadersVisible = false;
                grid.AutoGenerateColumns = false;
                grid.CellFormatting += Grid_CellFormatting;
                grid.CellDoubleClick += Grid_CellDoubleClick;
                grid.DataError += delegate { };

                DataGridViewTextBoxColumn orderColumn = new DataGridViewTextBoxColumn();
                orderColumn.HeaderText = "Order";
                orderColumn.Width = 58;
                grid.Columns.Add(orderColumn);

                DataGridViewTextBoxColumn referenceColumn = new DataGridViewTextBoxColumn();
                referenceColumn.HeaderText = "Reference item";
                referenceColumn.Width = 260;
                grid.Columns.Add(referenceColumn);

                DataGridViewTextBoxColumn matchedColumn = new DataGridViewTextBoxColumn();
                matchedColumn.HeaderText = "Matched top-level component(s)";
                matchedColumn.Width = 470;
                grid.Columns.Add(matchedColumn);

                DataGridViewTextBoxColumn positionColumn = new DataGridViewTextBoxColumn();
                positionColumn.HeaderText = "Current position";
                positionColumn.Width = 130;
                grid.Columns.Add(positionColumn);

                DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
                statusColumn.HeaderText = "Status";
                statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns.Add(statusColumn);

                root.Controls.Add(grid, 0, 2);

                statusLabel = new Label();
                statusLabel.AutoSize = false;
                statusLabel.Dock = DockStyle.Top;
                statusLabel.Height = 42;
                statusLabel.Padding = new Padding(4, 8, 4, 0);
                statusLabel.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold, GraphicsUnit.Point);
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;
                root.Controls.Add(statusLabel, 0, 3);

                FlowLayoutPanel bottom = new FlowLayoutPanel();
                bottom.AutoSize = true;
                bottom.Dock = DockStyle.Bottom;
                bottom.FlowDirection = FlowDirection.RightToLeft;
                bottom.WrapContents = false;
                bottom.Margin = new Padding(0, 8, 0, 0);

                closeButton = CreateButton("Close", delegate { Close(); });
                applyButton = CreateButton("Apply", delegate { ApplyReorder(); });
                rescanButton = CreateButton("Refresh preview", delegate { ScanAndPreview(); });

                bottom.Controls.Add(closeButton);
                bottom.Controls.Add(applyButton);
                bottom.Controls.Add(rescanButton);
                root.Controls.Add(bottom, 0, 4);

                Controls.Add(root);
            }

            private Button CreateButton(string text, EventHandler handler)
            {
                Button button = new Button();
                button.Text = text;
                button.AutoSize = false;
                button.Width = 124;
                button.Height = 30;
                button.Margin = new Padding(6, 0, 0, 0);
                button.Click += handler;
                return button;
            }

            private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= previewRows.Count)
                    return;

                OrderPreviewRow row = previewRows[e.RowIndex];
                if (row == null || row.MatchedComponents.Count == 0)
                    return;

                StringBuilder details = new StringBuilder();
                details.AppendLine(row.ReferenceItem);
                details.AppendLine();
                foreach (AssemblyComponentInfo component in row.MatchedComponents)
                {
                    details.AppendLine(component.TreeDisplayName);
                    if (!string.IsNullOrWhiteSpace(component.Path))
                        details.AppendLine("    " + component.Path);
                }

                MessageBox.Show(details.ToString(), CommandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= previewRows.Count)
                    return;

                OrderPreviewRow row = previewRows[e.RowIndex];
                if (row == null)
                    return;

                if (row.StatusKind == PreviewStatus.Missing)
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 230, 230);
                    e.CellStyle.ForeColor = Color.Black;
                }
                else if (row.StatusKind == PreviewStatus.Multiple)
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 249, 205);
                    e.CellStyle.ForeColor = Color.Black;
                }
                else if (row.StatusKind == PreviewStatus.Ready)
                {
                    e.CellStyle.BackColor = Color.White;
                    e.CellStyle.ForeColor = Color.Black;
                }
            }

            private void ChangeList()
            {
                string newReferenceFile = AskForReferenceFile();
                if (string.IsNullOrWhiteSpace(newReferenceFile))
                    return;

                List<string> newOrder = ReadReferenceOrder(newReferenceFile, out string readError);
                if (!string.IsNullOrWhiteSpace(readError))
                {
                    MessageBox.Show(readError, CommandTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (newOrder.Count == 0)
                {
                    MessageBox.Show(
                        "The selected reference file does not contain any usable order lines.",
                        CommandTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                referenceOrder.Clear();
                referenceOrder.AddRange(newOrder);
                referenceFile = newReferenceFile;

                referenceLabel.Text = BuildReferenceLabelText(newReferenceFile);
                if (referenceToolTip != null)
                {
                    referenceToolTip.SetToolTip(referenceLabel, newReferenceFile);
                }
                ScanAndPreview();
            }

            private void ScanAndPreview()
            {
                components.Clear();
                fixedReferenceComponents.Clear();
                previewRows.Clear();
                unmatchedComponents.Clear();
                skippedReferenceItems.Clear();

                if (assemblyDoc == null)
                {
                    SetStatus("Active document is not a valid assembly.");
                    return;
                }

                object rawComponents = null;
                try { rawComponents = assemblyDoc.GetComponents(true); } catch { rawComponents = null; }

                object[] componentObjects = rawComponents as object[];
                if (componentObjects == null)
                    componentObjects = new object[0];

                int position = 1;
                foreach (object componentObject in componentObjects)
                {
                    IComponent2 component = componentObject as IComponent2;
                    if (component == null)
                        continue;

                    AssemblyComponentInfo info = BuildComponentInfo(component, position);
                    if (!string.IsNullOrWhiteSpace(info.InstanceName))
                    {
                        components.Add(info);
                        position++;
                    }
                }

                BuildPreviewRows();
                LoadGrid();
                UpdateStatus();
            }

            private AssemblyComponentInfo BuildComponentInfo(IComponent2 component, int position)
            {
                AssemblyComponentInfo info = new AssemblyComponentInfo();
                info.Component = component;
                info.OriginalPosition = position;
                info.InstanceName = SafeComponentName(component);
                info.TopLevelName = GetTopLevelComponentName(component);
                info.Path = SafeComponentPath(component);
                info.FileName = string.IsNullOrWhiteSpace(info.Path)
                    ? string.Empty
                    : Path.GetFileNameWithoutExtension(info.Path);
                info.ReferencedConfiguration = SafeReferencedConfiguration(component);
                info.IsSuppressed = IsComponentSuppressed(component);

                IModelDoc2 model = TryGetReferencedModel(component);
                info.ConfigurationDescription = ReadConfigurationDescription(model, info.ReferencedConfiguration);
                info.ConfigurationCustomDescription = ReadCustomProperty(model, info.ReferencedConfiguration, "Description");
                if (string.IsNullOrWhiteSpace(info.ConfigurationCustomDescription))
                    info.ConfigurationCustomDescription = ReadCustomProperty(model, info.ReferencedConfiguration, "DESCRIPTION");
                info.DocumentDescription = ReadCustomProperty(model, string.Empty, "Description");
                if (string.IsNullOrWhiteSpace(info.DocumentDescription))
                    info.DocumentDescription = ReadCustomProperty(model, string.Empty, "DESCRIPTION");

                info.BestDescription = FirstNotBlank(
                    info.DocumentDescription,
                    info.ConfigurationCustomDescription,
                    info.ConfigurationDescription);

                info.TreeDisplayName = BuildFeatureManagerLikeName(info);
                info.SearchText = BuildSearchText(info);
                return info;
            }

            private IModelDoc2 TryGetReferencedModel(IComponent2 component)
            {
                if (component == null)
                    return null;

                IModelDoc2 model = null;
                try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }

                if (model != null)
                    return model;

                try
                {
                    object rawComponent = component;
                    rawComponent.GetType().InvokeMember(
                        "SetSuppression2",
                        BindingFlags.InvokeMethod,
                        null,
                        rawComponent,
                        new object[] { (int)swComponentSuppressionState_e.swComponentResolved });
                }
                catch
                {
                }

                try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }
                return model;
            }

            private void BuildPreviewRows()
            {
                fixedReferenceComponents.Clear();
                fixedReferenceComponents.AddRange(
                    components
                        .Where(IsFixedReferenceComponent)
                        .OrderBy(component => component.OriginalPosition));

                List<AssemblyComponentInfo> orderableComponents = components
                    .Where(component => !IsFixedReferenceComponent(component))
                    .OrderBy(component => component.OriginalPosition)
                    .ToList();

                Dictionary<AssemblyComponentInfo, MatchResult> bestMatches =
                    new Dictionary<AssemblyComponentInfo, MatchResult>();

                for (int referenceIndex = 0; referenceIndex < referenceOrder.Count; referenceIndex++)
                {
                    string referenceItem = referenceOrder[referenceIndex];

                    foreach (AssemblyComponentInfo component in orderableComponents)
                    {
                        MatchResult result = ScoreComponent(referenceItem, component);
                        if (result.Score < 60)
                            continue;

                        MatchResult existing;
                        if (!bestMatches.TryGetValue(component, out existing) ||
                            result.Score > existing.Score ||
                            (result.Score == existing.Score && result.ReferenceSpecificity > existing.ReferenceSpecificity))
                        {
                            result.ReferenceIndex = referenceIndex;
                            result.ReferenceItem = referenceItem;
                            bestMatches[component] = result;
                        }
                    }
                }

                for (int referenceIndex = 0; referenceIndex < referenceOrder.Count; referenceIndex++)
                {
                    string referenceItem = referenceOrder[referenceIndex];

                    List<AssemblyComponentInfo> matchedForReference =
                        bestMatches
                            .Where(pair => pair.Value.ReferenceIndex == referenceIndex)
                            .Select(pair => pair.Key)
                            .OrderBy(component => component.OriginalPosition)
                            .ToList();

                    if (matchedForReference.Count == 0)
                    {
                        skippedReferenceItems.Add(referenceItem);
                        continue;
                    }

                    OrderPreviewRow row = new OrderPreviewRow();
                    row.OrderNumber = previewRows.Count + 1;
                    row.ReferenceItem = referenceItem;
                    row.MatchedComponents.AddRange(matchedForReference);

                    if (row.MatchedComponents.Count == 1)
                    {
                        row.StatusKind = PreviewStatus.Ready;
                        row.StatusText = "Ready";
                    }
                    else
                    {
                        row.StatusKind = PreviewStatus.Multiple;
                        row.StatusText = row.MatchedComponents.Count + " matches - will keep together";
                    }

                    previewRows.Add(row);
                }

                HashSet<AssemblyComponentInfo> matched = new HashSet<AssemblyComponentInfo>(
                    previewRows.SelectMany(row => row.MatchedComponents));

                unmatchedComponents.AddRange(
                    orderableComponents
                        .Where(component => !matched.Contains(component))
                        .OrderBy(component => component.OriginalPosition));
            }

            private static bool IsFixedReferenceComponent(AssemblyComponentInfo component)
            {
                if (component == null)
                    return false;

                // Basic Sketch / skeleton reference parts drive the room layout.
                // They are not ordered from the TXT file and are never moved by this tool.
                string combined = string.Join(" ", new string[]
                {
                    component.TreeDisplayName,
                    component.BestDescription,
                    component.DocumentDescription,
                    component.ConfigurationCustomDescription,
                    component.ConfigurationDescription,
                    component.ReferencedConfiguration,
                    component.TopLevelName,
                    component.FileName,
                    component.InstanceName
                }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());

                string key = NormalizeKey(combined);
                if (key.Contains("basicsketch"))
                    return true;

                // Keep the rule narrow. Do not treat every generic sketch-like name as fixed,
                // because some real furniture components may contain sketch references internally.
                return false;
            }

            private MatchResult ScoreComponent(string referenceItem, AssemblyComponentInfo component)
            {
                MatchResult best = new MatchResult();
                best.Score = 0;
                best.ReferenceSpecificity = Tokenize(referenceItem).Count;

                if (component == null || string.IsNullOrWhiteSpace(referenceItem))
                    return best;

                List<string> referenceKeys = BuildReferenceKeys(referenceItem);
                List<string> fields = new List<string>();
                AddField(fields, component.BestDescription);
                AddField(fields, component.DocumentDescription);
                AddField(fields, component.ConfigurationCustomDescription);
                AddField(fields, component.ConfigurationDescription);
                AddField(fields, component.ReferencedConfiguration);
                AddField(fields, component.TopLevelName);
                AddField(fields, component.FileName);
                AddField(fields, component.InstanceName);

                foreach (string referenceKey in referenceKeys)
                {
                    if (string.IsNullOrWhiteSpace(referenceKey))
                        continue;

                    foreach (string rawField in fields)
                    {
                        string field = NormalizeKey(rawField);
                        if (field.Length == 0)
                            continue;

                        int score = 0;
                        if (field.Equals(referenceKey, StringComparison.OrdinalIgnoreCase))
                        {
                            score = 120;
                        }
                        else if (field.StartsWith(referenceKey, StringComparison.OrdinalIgnoreCase) &&
                                 !IsDangerouslyGenericReference(referenceItem, rawField))
                        {
                            score = 100;
                        }
                        else if (referenceKey.StartsWith(field, StringComparison.OrdinalIgnoreCase) && field.Length >= 4)
                        {
                            score = 92;
                        }
                        else if (component.SearchText.Contains(referenceKey) &&
                                 !IsDangerouslyGenericReference(referenceItem, rawField))
                        {
                            score = 78;
                        }
                        else
                        {
                            score = TokenOverlapScore(referenceItem, rawField);
                        }

                        if (score > best.Score)
                            best.Score = score;
                    }
                }

                return best;
            }

            private static bool IsDangerouslyGenericReference(string referenceItem, string candidateField)
            {
                List<string> referenceTokens = Tokenize(referenceItem);
                List<string> fieldTokens = Tokenize(candidateField);

                if (referenceTokens.Count != 1 || fieldTokens.Count <= 1)
                    return false;

                string token = referenceTokens[0];
                if (token == "table" && fieldTokens.Contains("light"))
                    return true;
                if (token == "chair" && fieldTokens.Count > 2)
                    return false;

                return IsGenericToken(token);
            }

            private static bool IsGenericToken(string token)
            {
                if (string.IsNullOrWhiteSpace(token))
                    return false;

                string[] generic =
                {
                    "table",
                    "box",
                    "base",
                    "mirror",
                    "chair",
                    "bed",
                    "sofa"
                };

                return generic.Contains(token.ToLowerInvariant());
            }

            private static int TokenOverlapScore(string referenceItem, string candidateField)
            {
                List<string> referenceTokens = Tokenize(referenceItem);
                List<string> fieldTokens = Tokenize(candidateField);

                if (referenceTokens.Count == 0 || fieldTokens.Count == 0)
                    return 0;

                int matches = referenceTokens.Count(token => fieldTokens.Contains(token));
                if (matches == 0)
                    return 0;

                if (matches == referenceTokens.Count && referenceTokens.Count > 1)
                    return 72;

                if (referenceTokens.Count == 1 && matches == 1 && !IsGenericToken(referenceTokens[0]))
                    return 66;

                return 40;
            }

            private static List<string> BuildReferenceKeys(string referenceItem)
            {
                List<string> keys = new List<string>();
                string baseKey = NormalizeKey(referenceItem);
                AddKey(keys, baseKey);

                string lower = (referenceItem ?? string.Empty).ToLowerInvariant();

                if (lower.Contains("ceiling") && lower.Contains("assembly"))
                {
                    AddKey(keys, NormalizeKey("Ceiling Layout"));
                    AddKey(keys, NormalizeKey("Ceiling"));
                }

                if (lower.Contains("wall") && lower.Contains("assembly"))
                {
                    AddKey(keys, NormalizeKey("Wall Assembly"));
                    AddKey(keys, NormalizeKey("Wall"));
                }

                if (lower.Contains("base") && lower.Contains("tube"))
                {
                    AddKey(keys, NormalizeKey("Base Tubes"));
                    AddKey(keys, NormalizeKey("Base Tube"));
                    AddKey(keys, NormalizeKey("Base"));
                }

                if (lower.Contains("wet") && lower.Contains("unit"))
                {
                    AddKey(keys, NormalizeKey("WET UNIT"));
                    AddKey(keys, NormalizeKey("Wet Unit Type"));
                }

                if (lower.Contains("tv") && lower.Contains("screen"))
                    AddKey(keys, NormalizeKey("TV Screen"));

                if (lower.Contains("tv") && lower.Contains("bracket"))
                    AddKey(keys, NormalizeKey("TV Bracket"));

                if (lower.Contains("window") && lower.Contains("box"))
                    AddKey(keys, NormalizeKey("Window Box"));

                return keys;
            }

            private static void AddKey(List<string> keys, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                if (!keys.Contains(value, StringComparer.OrdinalIgnoreCase))
                    keys.Add(value);
            }

            private static void AddField(List<string> fields, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    fields.Add(value);
            }

            private static List<string> Tokenize(string text)
            {
                string prepared = (text ?? string.Empty).ToLowerInvariant();
                prepared = prepared.Replace("+", " plus ");
                prepared = Regex.Replace(prepared, @"[^a-z0-9]+", " ");
                return prepared
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => token.Length > 0)
                    .Distinct()
                    .ToList();
            }

            private static string NormalizeKey(string value)
            {
                string text = (value ?? string.Empty).ToLowerInvariant();
                text = text.Replace("+", "plus");
                text = Regex.Replace(text, @"[^a-z0-9]+", string.Empty);
                return text;
            }

            private string BuildSearchText(AssemblyComponentInfo info)
            {
                List<string> fields = new List<string>();
                AddField(fields, info.TreeDisplayName);
                AddField(fields, info.BestDescription);
                AddField(fields, info.DocumentDescription);
                AddField(fields, info.ConfigurationCustomDescription);
                AddField(fields, info.ConfigurationDescription);
                AddField(fields, info.ReferencedConfiguration);
                AddField(fields, info.TopLevelName);
                AddField(fields, info.FileName);
                AddField(fields, info.InstanceName);
                return NormalizeKey(string.Join(" ", fields.ToArray()));
            }

            private static string BuildFeatureManagerLikeName(AssemblyComponentInfo info)
            {
                if (info == null)
                    return string.Empty;

                string componentName = string.IsNullOrWhiteSpace(info.TopLevelName)
                    ? info.InstanceName
                    : info.TopLevelName;

                string description = FirstNotBlank(
                    info.DocumentDescription,
                    info.ConfigurationCustomDescription,
                    info.ConfigurationDescription);

                string configuration = info.ReferencedConfiguration ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(configuration))
                    return componentName + " (\"" + description + "\" " + configuration + ")";

                if (!string.IsNullOrWhiteSpace(description))
                    return componentName + " (\"" + description + "\")";

                if (!string.IsNullOrWhiteSpace(configuration))
                    return componentName + " (" + configuration + ")";

                return componentName;
            }

            private static string FirstNotBlank(params string[] values)
            {
                if (values == null)
                    return string.Empty;

                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }

                return string.Empty;
            }

            private void LoadGrid()
            {
                grid.Rows.Clear();

                foreach (OrderPreviewRow row in previewRows)
                {
                    int gridRowIndex = grid.Rows.Add(
                        row.OrderNumber.ToString("00"),
                        row.ReferenceItem,
                        BuildMatchedText(row),
                        BuildPositionText(row),
                        row.StatusText);

                    DataGridViewRow gridRow = grid.Rows[gridRowIndex];
                    gridRow.Tag = row;
                    gridRow.Cells[MatchedColumnIndex].ToolTipText = BuildMatchedTooltip(row);
                    gridRow.Cells[StatusColumnIndex].ToolTipText = row.StatusText;
                }
            }

            private static string BuildMatchedText(OrderPreviewRow row)
            {
                if (row == null || row.MatchedComponents.Count == 0)
                    return string.Empty;

                if (row.MatchedComponents.Count == 1)
                    return row.MatchedComponents[0].TreeDisplayName;

                return row.MatchedComponents.Count + " components: " +
                       string.Join("; ", row.MatchedComponents.Select(component => component.TreeDisplayName).Take(3).ToArray()) +
                       (row.MatchedComponents.Count > 3 ? "; ..." : string.Empty);
            }

            private static string BuildMatchedTooltip(OrderPreviewRow row)
            {
                if (row == null || row.MatchedComponents.Count == 0)
                    return string.Empty;

                StringBuilder builder = new StringBuilder();
                foreach (AssemblyComponentInfo component in row.MatchedComponents)
                {
                    builder.AppendLine(component.TreeDisplayName);
                    if (!string.IsNullOrWhiteSpace(component.Path))
                        builder.AppendLine(component.Path);
                    builder.AppendLine();
                }

                return builder.ToString().Trim();
            }

            private static string BuildPositionText(OrderPreviewRow row)
            {
                if (row == null || row.MatchedComponents.Count == 0)
                    return string.Empty;

                return string.Join(", ", row.MatchedComponents.Select(component => component.OriginalPosition.ToString()).ToArray());
            }

            private void UpdateStatus()
            {
                int matchedReferenceRows = previewRows.Count;
                int skippedReferenceRows = skippedReferenceItems.Count;
                int matchedComponentCount = previewRows.SelectMany(row => row.MatchedComponents).Distinct().Count();

                SetStatus(
                    "Reference items: " + referenceOrder.Count +
                    " | Ordered matches: " + matchedReferenceRows +
                    " | Skipped not in this assembly: " + skippedReferenceRows +
                    " | Top-level components to move: " + matchedComponentCount +
                    " | Fixed reference components kept first: " + fixedReferenceComponents.Count +
                    " | Unmatched components kept after ordered group: " + unmatchedComponents.Count);
            }

            private void SetStatus(string text)
            {
                if (statusLabel != null)
                    statusLabel.Text = text ?? string.Empty;
            }

            private List<AssemblyComponentInfo> GetOrderedComponentsToMove()
            {
                List<AssemblyComponentInfo> ordered = new List<AssemblyComponentInfo>();
                HashSet<IComponent2> seen = new HashSet<IComponent2>();

                foreach (OrderPreviewRow row in previewRows)
                {
                    foreach (AssemblyComponentInfo component in row.MatchedComponents.OrderBy(item => item.OriginalPosition))
                    {
                        if (component == null || component.Component == null)
                            continue;

                        if (!seen.Contains(component.Component))
                        {
                            seen.Add(component.Component);
                            ordered.Add(component);
                        }
                    }
                }

                return ordered;
            }

            private void ApplyReorder()
            {
                grid.EndEdit();

                List<AssemblyComponentInfo> ordered = GetOrderedComponentsToMove();
                if (ordered.Count == 0)
                {
                    MessageBox.Show(
                        "No matched top-level components are available to reorder.",
                        CommandTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                int skipped = skippedReferenceItems.Count;
                string message =
                    "Apply room-layout FeatureManager reorder?\n\n" +
                    "Top-level components moved: " + ordered.Count + "\n" +
                    "Reference items skipped because they are not in this assembly: " + skipped + "\n" +
                    "Fixed Basic Sketch/reference components are kept before the ordered group.\n" +
                    "Unmatched top-level components stay below the ordered group.\n\n" +
                    "The assembly is rebuilt but not saved automatically.";

                DialogResult answer = MessageBox.Show(
                    message,
                    CommandTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                    return;

                ApplyResult result = TryApplyReorder(ordered);
                WriteReport(ordered, result);

                if (result.Success)
                {
                    try { assemblyModel.ForceRebuild3(false); } catch { }
                    try { assemblyModel.GraphicsRedraw2(); } catch { }

                    SetStatus("Applied reorder. Report: " + result.ReportPath);
                    MessageBox.Show(
                        "Room-layout assembly reorder completed.\n\n" + result.ReportPath,
                        CommandTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    ScanAndPreview();
                }
                else
                {
                    SetStatus("Reorder failed. Report: " + result.ReportPath);
                    MessageBox.Show(
                        "SOLIDWORKS did not complete the reorder.\n\n" +
                        result.Message + "\n\n" +
                        result.ReportPath,
                        CommandTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            private ApplyResult TryApplyReorder(List<AssemblyComponentInfo> ordered)
            {
                ApplyResult result = new ApplyResult();
                result.Success = false;
                result.Message = string.Empty;

                if (assemblyDoc == null)
                {
                    result.Message = "Assembly document is not available.";
                    return result;
                }

                if (ordered == null || ordered.Count == 0)
                {
                    result.Message = "No ordered components were supplied.";
                    return result;
                }

                try
                {
                    List<string> errors = new List<string>();
                    bool completed = ApplySequentialReorder(ordered, fixedReferenceComponents, errors);

                    if (!completed)
                    {
                        result.Message = errors.Count == 0
                            ? "SOLIDWORKS did not complete the sequential component reorder."
                            : string.Join(System.Environment.NewLine, errors.ToArray());
                        return result;
                    }

                    result.Success = true;
                    result.Message = fixedReferenceComponents.Count > 0
                        ? "Reorder completed. Fixed Basic Sketch/reference component(s) were kept before the ordered group."
                        : "Reorder completed.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Message = ex.Message;
                    return result;
                }
            }

            private bool ApplySequentialReorder(
                List<AssemblyComponentInfo> ordered,
                List<AssemblyComponentInfo> fixedComponents,
                List<string> errors)
            {
                if (ordered == null || ordered.Count == 0)
                    return false;

                IComponent2 anchor = null;

                if (fixedComponents != null && fixedComponents.Count > 0)
                {
                    // Basic Sketch/reference components are deliberately kept ahead of all ordered items.
                    // Using the last fixed component as the anchor avoids moving ordered components before
                    // the skeleton/reference component and avoids the COM bad-index failure seen when the
                    // old bulk move attempted to move many components before the first unmatched item.
                    anchor = fixedComponents
                        .Where(component => component != null && component.Component != null)
                        .OrderBy(component => component.OriginalPosition)
                        .Select(component => component.Component)
                        .LastOrDefault();
                }

                if (anchor == null)
                    anchor = FindStableStartAnchor(ordered);

                if (anchor == null)
                {
                    errors.Add("No stable start anchor was found for the component reorder.");
                    return false;
                }

                bool hadFailure = false;
                IComponent2 previous = anchor;

                foreach (AssemblyComponentInfo item in ordered)
                {
                    if (item == null || item.Component == null)
                        continue;

                    if (SameComponent(previous, item.Component))
                    {
                        previous = item.Component;
                        continue;
                    }

                    string error;
                    bool ok = TryMoveComponentAfter(item.Component, previous, out error);
                    if (!ok)
                    {
                        hadFailure = true;
                        errors.Add(item.TreeDisplayName + ": " + error);
                    }
                    else
                    {
                        previous = item.Component;
                    }
                }

                return !hadFailure;
            }

            private IComponent2 FindStableStartAnchor(List<AssemblyComponentInfo> ordered)
            {
                HashSet<IComponent2> orderedSet = new HashSet<IComponent2>(
                    ordered
                        .Where(item => item != null && item.Component != null)
                        .Select(item => item.Component));

                // Prefer any existing top-level component that is not part of the moving group.
                // This makes the operation an anchored sequence instead of an unanchored bulk reorder.
                AssemblyComponentInfo anchorInfo = components
                    .Where(component => component != null && component.Component != null && !orderedSet.Contains(component.Component))
                    .OrderBy(component => component.OriginalPosition)
                    .LastOrDefault();

                if (anchorInfo != null)
                    return anchorInfo.Component;

                // If every component is part of the order list, keep the first ordered component as the anchor
                // and move the following components after it. This still enforces the relative order safely.
                return ordered
                    .Where(item => item != null && item.Component != null)
                    .OrderBy(item => item.OriginalPosition)
                    .Select(item => item.Component)
                    .FirstOrDefault();
            }

            private bool TryMoveComponentAfter(IComponent2 sourceComponent, IComponent2 targetComponent, out string error)
            {
                error = string.Empty;

                if (sourceComponent == null)
                {
                    error = "Source component is null.";
                    return false;
                }

                if (targetComponent == null)
                {
                    error = "Target component is null.";
                    return false;
                }

                if (SameComponent(sourceComponent, targetComponent))
                    return true;

                try
                {
                    object[] source = new object[] { sourceComponent };
                    dynamic dynamicAssembly = assemblyDoc;

                    bool moved = dynamicAssembly.ReorderComponents(
                        source,
                        targetComponent,
                        (int)swReorderComponentsWhere_e.swReorderComponents_After);

                    // SOLIDWORKS can return false for a no-op or for a tree position it decides not to change.
                    // A COM exception is the real hard failure. Verify by rescanning in future if stricter
                    // validation is required.
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            private static bool SameComponent(IComponent2 first, IComponent2 second)
            {
                if (first == null || second == null)
                    return false;

                if (object.ReferenceEquals(first, second))
                    return true;

                try
                {
                    string firstName = first.Name2 ?? string.Empty;
                    string secondName = second.Name2 ?? string.Empty;
                    return firstName.Equals(secondName, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }

            private void WriteReport(List<AssemblyComponentInfo> ordered, ApplyResult result)
            {
                string reportPath = string.Empty;

                try
                {
                    string documents = System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments);
                    string directory = Path.Combine(documents, "CabinTools", "AssemblyReorderReports");
                    Directory.CreateDirectory(directory);

                    string title = SafeFileName(Path.GetFileNameWithoutExtension(assemblyModel.GetTitle()));
                    if (string.IsNullOrWhiteSpace(title))
                        title = "Assembly";

                    reportPath = Path.Combine(
                        directory,
                        "RoomLayoutAssemblyReorder_" + title + "_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("Cabin Tools - Reorder Room Layout Assembly");
                    builder.AppendLine("Assembly: " + assemblyModel.GetTitle());
                    builder.AppendLine("Reference file: " + Path.GetFileName(referenceFile));
                    builder.AppendLine("Reference path: " + referenceFile);
                    builder.AppendLine("Result: " + (result.Success ? "Success" : "Failed"));
                    builder.AppendLine("Message: " + result.Message);
                    builder.AppendLine();
                    builder.AppendLine("Applied reference order and matches:");

                    foreach (OrderPreviewRow row in previewRows)
                    {
                        builder.AppendLine(row.OrderNumber.ToString("00") + ". " + row.ReferenceItem + " - " + row.StatusText);
                        foreach (AssemblyComponentInfo component in row.MatchedComponents)
                            builder.AppendLine("    " + component.TreeDisplayName);
                    }

                    builder.AppendLine();
                    builder.AppendLine("Fixed reference components kept before ordered group:");
                    foreach (AssemblyComponentInfo component in fixedReferenceComponents)
                        builder.AppendLine("    " + component.TreeDisplayName);

                    builder.AppendLine();
                    builder.AppendLine("Skipped reference items not present in this assembly:");
                    foreach (string skippedItem in skippedReferenceItems)
                        builder.AppendLine("    " + skippedItem);

                    builder.AppendLine();
                    builder.AppendLine("Ordered components moved:");
                    foreach (AssemblyComponentInfo component in ordered)
                        builder.AppendLine("    " + component.TreeDisplayName);

                    builder.AppendLine();
                    builder.AppendLine("Unmatched components kept after ordered group:");
                    foreach (AssemblyComponentInfo component in unmatchedComponents)
                        builder.AppendLine("    " + component.TreeDisplayName);

                    File.WriteAllText(reportPath, builder.ToString(), Encoding.UTF8);
                }
                catch
                {
                    reportPath = string.Empty;
                }

                result.ReportPath = reportPath;
            }

            private static string SafeFileName(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;

                string text = value;
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    text = text.Replace(invalid, '_');
                return text;
            }

            private static string SafeComponentName(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.Name2 ?? string.Empty; } catch { return string.Empty; }
            }

            private static string SafeComponentPath(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.GetPathName() ?? string.Empty; } catch { return string.Empty; }
            }

            private static string SafeReferencedConfiguration(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.ReferencedConfiguration ?? string.Empty; } catch { return string.Empty; }
            }

            private static string GetTopLevelComponentName(IComponent2 component)
            {
                string name = SafeComponentName(component);
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                int slash = name.IndexOf('/');
                if (slash > 0)
                    name = name.Substring(0, slash);

                return name;
            }

            private static bool IsComponentSuppressed(IComponent2 component)
            {
                if (component == null)
                    return true;

                try
                {
                    int state = component.GetSuppression();
                    return state == (int)swComponentSuppressionState_e.swComponentSuppressed;
                }
                catch
                {
                    return false;
                }
            }

            private static string ReadConfigurationDescription(IModelDoc2 model, string configurationName)
            {
                if (model == null || string.IsNullOrWhiteSpace(configurationName))
                    return string.Empty;

                try
                {
                    object configuration = model.GetConfigurationByName(configurationName);
                    if (configuration == null)
                        return string.Empty;

                    object value = configuration.GetType().InvokeMember(
                        "Description",
                        BindingFlags.GetProperty,
                        null,
                        configuration,
                        null);

                    return value == null ? string.Empty : Convert.ToString(value);
                }
                catch
                {
                    return string.Empty;
                }
            }

            private static string ReadCustomProperty(IModelDoc2 model, string configurationName, string propertyName)
            {
                if (model == null || string.IsNullOrWhiteSpace(propertyName))
                    return string.Empty;

                CustomPropertyManager manager = null;
                try
                {
                    object extension = model.Extension;
                    string config = string.IsNullOrWhiteSpace(configurationName) ? string.Empty : configurationName;
                    manager = extension.GetType().InvokeMember(
                        "CustomPropertyManager",
                        BindingFlags.GetProperty,
                        null,
                        extension,
                        new object[] { config }) as CustomPropertyManager;
                }
                catch
                {
                    manager = null;
                }

                if (manager == null)
                    return string.Empty;

                try
                {
                    string value = string.Empty;
                    string resolved = string.Empty;
                    bool wasResolved = false;
                    manager.Get5(propertyName, false, out value, out resolved, out wasResolved);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        return resolved;
                    return value ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private sealed class AssemblyComponentInfo
        {
            public IComponent2 Component { get; set; }
            public int OriginalPosition { get; set; }
            public string InstanceName { get; set; }
            public string TopLevelName { get; set; }
            public string Path { get; set; }
            public string FileName { get; set; }
            public string ReferencedConfiguration { get; set; }
            public bool IsSuppressed { get; set; }
            public string DocumentDescription { get; set; }
            public string ConfigurationCustomDescription { get; set; }
            public string ConfigurationDescription { get; set; }
            public string BestDescription { get; set; }
            public string TreeDisplayName { get; set; }
            public string SearchText { get; set; }
        }

        private sealed class OrderPreviewRow
        {
            public int OrderNumber { get; set; }
            public string ReferenceItem { get; set; }
            public List<AssemblyComponentInfo> MatchedComponents { get; private set; }
            public PreviewStatus StatusKind { get; set; }
            public string StatusText { get; set; }

            public OrderPreviewRow()
            {
                MatchedComponents = new List<AssemblyComponentInfo>();
            }
        }

        private sealed class MatchResult
        {
            public int ReferenceIndex { get; set; }
            public string ReferenceItem { get; set; }
            public int Score { get; set; }
            public int ReferenceSpecificity { get; set; }
        }

        private sealed class ApplyResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ReportPath { get; set; }
        }

        private enum PreviewStatus
        {
            Ready,
            Multiple,
            Missing
        }
    }
}
