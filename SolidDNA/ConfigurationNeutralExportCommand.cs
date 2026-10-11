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
    /// File names use a configurable sequence of document/configuration metadata
    /// and separators. Individual rows may override the automatic name.
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
            public bool IsManualName { get; set; }
            public Dictionary<string, string> Metadata { get; set; }
        }

        private sealed class NamingField
        {
            public string Key { get; set; }
            public string Label { get; set; }
        }

        private sealed class NamingPart
        {
            public string FieldKey { get; set; }
            public string SeparatorAfter { get; set; }
        }

        private sealed class NamingTemplate
        {
            public List<NamingPart> Parts { get; private set; }

            public NamingTemplate()
            {
                Parts = new List<NamingPart>();
            }

            public static NamingTemplate Default()
            {
                NamingTemplate result = new NamingTemplate();
                result.Parts.Add(new NamingPart { FieldKey = "document.description", SeparatorAfter = ", " });
                result.Parts.Add(new NamingPart { FieldKey = "configuration.name", SeparatorAfter = string.Empty });
                return result;
            }
        }

        private sealed class ExportRowState
        {
            public string FileName { get; set; }
            public string Status { get; set; }
            public bool IsManualName { get; set; }
            public bool Export { get; set; }
        }

        private sealed class ExportFormState
        {
            public NamingTemplate NamingTemplate { get; set; }
            public List<ExportRowState> Rows { get; set; }
        }

        private delegate string BuildNamePreview(NamingTemplate template,
            ConfigurationExportRow row, out string error);

        private sealed class ConfigurationNeutralExportForm : Form
        {
            private readonly IModelDoc2 modelDoc;
            private readonly string documentDescription;
            private readonly List<ConfigurationExportRow> rows;
            private readonly List<NamingField> availableFields = new List<NamingField>();
            private NamingTemplate namingTemplate;

            private DataGridView grid;
            private Label documentLabel;
            private Button namingButton;
            private Button undoButton;
            private Button resetFileNamesButton;
            private Button checkAllButton;
            private Button uncheckDerivedButton;
            private Button exportButton;
            private Button closeButton;

            private bool propagatingCheckState;
            private readonly List<int> checkboxSelectionSnapshot = new List<int>();
            private readonly Stack<ExportFormState> undoStack = new Stack<ExportFormState>();
            private ExportFormState lastCommittedState;
            private bool restoringUndo;

            public ConfigurationNeutralExportForm(
                IModelDoc2 modelDoc,
                string documentDescription,
                IList<string> configurationNames)
            {
                this.modelDoc = modelDoc;
                this.documentDescription = documentDescription ?? string.Empty;
                this.rows = new List<ConfigurationExportRow>();
                namingTemplate = ExportSettings.LoadNamingTemplate();
                AddBuiltInNamingFields();

                foreach (string name in configurationNames)
                {
                    rows.Add(
                        new ConfigurationExportRow
                        {
                            ConfigurationName = name,
                            Status = "Ready",
                            IsDerived = IsDerivedConfiguration(modelDoc, name),
                            Metadata = ReadNamingMetadata(modelDoc, this.documentDescription, name, true)
                        });
                }
                EnsureSavedFieldsAreAvailable();
                RefreshAutomaticNames();

                Text = "Cabin Tools - Export Configurations";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 980;
                Height = 680;
                MinimumSize = new Size(760, 500);
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadRows();
                lastCommittedState = CaptureFormState();
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

                namingButton = new Button();
                namingButton.Text = "Change Auto Naming Setting";
                namingButton.Width = 222;
                namingButton.Height = 28;
                namingButton.Left = ClientSize.Width - 240;
                namingButton.Top = 16;
                namingButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                namingButton.Click += delegate { ChangeAutoNamingSetting(); };
                Controls.Add(namingButton);

                undoButton = new Button();
                undoButton.Text = "Undo";
                undoButton.Width = 80;
                undoButton.Height = 28;
                undoButton.Left = ClientSize.Width - 560;
                undoButton.Top = 66;
                undoButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                undoButton.Enabled = false;
                undoButton.Click += delegate { UndoLastAction(); };
                Controls.Add(undoButton);

                resetFileNamesButton = new Button();
                resetFileNamesButton.Text = "Reset file names";
                resetFileNamesButton.Width = 125;
                resetFileNamesButton.Height = 28;
                resetFileNamesButton.Left = ClientSize.Width - 470;
                resetFileNamesButton.Top = 66;
                resetFileNamesButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                resetFileNamesButton.Click += delegate { ResetFileNames(); };
                Controls.Add(resetFileNamesButton);

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
                fileNameColumn.ReadOnly = false;
                fileNameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                fileNameColumn.MinimumWidth = 260;
                grid.Columns.Add(fileNameColumn);
                grid.CellEndEdit += Grid_CellEndEdit;
                grid.CellMouseUp += Grid_CellMouseUp;

                DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
                statusColumn.Name = "Status";
                statusColumn.HeaderText = "Status";
                statusColumn.ReadOnly = true;
                statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                statusColumn.Width = 160;
                statusColumn.MinimumWidth = 80;
                statusColumn.Resizable = DataGridViewTriState.True;
                grid.Columns.Add(statusColumn);

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
                    row.Cells["FileName"].ToolTipText = "Edit this name for this export. Right-click to restore the automatic name.";
                    row.Cells["Status"].Value = item.Status;
                    SetRowStatus(row, item.Status);
                }

            }

            private void AddBuiltInNamingFields()
            {
                AddNamingField("document.name", "Document name");
                AddNamingField("document.description", "Document Description (or document name if empty)");
                AddNamingField("configuration.name", "Configuration name");
                AddNamingField("configuration.description", "Configuration description");
                AddNamingField("configuration.alternate", "Configuration alternate name");
                AddNamingField("configuration.displaystates", "Display state name(s)");
                AddNamingField("configuration.property:Description", "Configuration property: Description");
            }

            private void AddNamingField(string key, string label)
            {
                if (!availableFields.Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase)))
                    availableFields.Add(new NamingField { Key = key, Label = label });
            }

            private void EnsureSavedFieldsAreAvailable()
            {
                foreach (NamingPart part in namingTemplate.Parts)
                    AddNamingField(part.FieldKey, GetFieldLabel(part.FieldKey) + " (unavailable in this document)");
            }

            private string GetFieldLabel(string key)
            {
                NamingField field = availableFields.FirstOrDefault(item =>
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (field != null) return field.Label;
                if (key.StartsWith("document.property:", StringComparison.OrdinalIgnoreCase))
                    return "Document property: " + key.Substring("document.property:".Length);
                if (key.StartsWith("configuration.property:", StringComparison.OrdinalIgnoreCase))
                    return "Configuration property: " + key.Substring("configuration.property:".Length);
                return key;
            }

            private Dictionary<string, string> ReadNamingMetadata(
                IModelDoc2 document, string description, string configurationName, bool useCached)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string fileName = GetDisplayDocumentName(document);
                values["document.name"] = Path.GetFileNameWithoutExtension(fileName);
                values["document.description"] = description;
                values["configuration.name"] = configurationName;

                try
                {
                    IConfiguration configuration = document.GetConfigurationByName(configurationName) as IConfiguration;
                    if (configuration != null)
                    {
                        values["configuration.description"] = configuration.Description ?? string.Empty;
                        values["configuration.alternate"] = configuration.AlternateName ?? string.Empty;
                        Array displayStates = configuration.GetDisplayStates() as Array;
                        if (displayStates != null)
                        {
                            values["configuration.displaystates"] = string.Join(" + ", displayStates.Cast<object>()
                                .Select(state => Convert.ToString(state))
                                .Where(state => !string.IsNullOrWhiteSpace(state)).ToArray());
                        }
                    }
                }
                catch { /* Unavailable metadata remains blank in the preview. */ }

                try
                {
                    IModelDocExtension extension = document.Extension;
                    if (extension != null)
                    {
                        ReadPropertyValues(extension.CustomPropertyManager[string.Empty], "document.property:", values, useCached);
                        ReadPropertyValues(extension.CustomPropertyManager[configurationName], "configuration.property:", values, useCached);
                    }
                }
                catch { /* A missing property is reported by the naming preview. */ }
                return values;
            }

            private void ReadPropertyValues(ICustomPropertyManager manager, string prefix,
                Dictionary<string, string> values, bool useCached)
            {
                if (manager == null) return;
                Array names = null;
                try { names = manager.GetNames() as Array; } catch { }
                if (names == null) return;
                foreach (object item in names)
                {
                    string name = Convert.ToString(item);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string key = prefix + name;
                    AddNamingField(key, (prefix == "document.property:" ? "Document property: " : "Configuration property: ") + name);
                    try
                    {
                        string raw, resolved;
                        bool wasResolved, linked;
                        manager.Get6(name, useCached, out raw, out resolved, out wasResolved, out linked);
                        values[key] = !string.IsNullOrWhiteSpace(resolved) ? resolved.Trim() :
                            !string.IsNullOrWhiteSpace(raw) && raw.IndexOf('$') < 0 ? raw.Trim() : string.Empty;
                    }
                    catch { values[key] = string.Empty; }
                }
            }

            private void RefreshAutomaticNames()
            {
                foreach (ConfigurationExportRow info in rows)
                    if (!info.IsManualName) UpdateAutomaticName(info);
            }

            private void UpdateAutomaticName(ConfigurationExportRow info)
            {
                string error;
                info.FileName = BuildAutomaticName(namingTemplate, info, out error);
                info.Status = error == null ? "Ready" : "! " + error;
            }

            private string BuildAutomaticName(NamingTemplate template, ConfigurationExportRow info, out string error)
            {
                error = null;
                if (template == null || template.Parts.Count == 0)
                {
                    error = "Choose at least one naming field.";
                    return string.Empty;
                }
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < template.Parts.Count; i++)
                {
                    NamingPart part = template.Parts[i];
                    string value;
                    if (string.IsNullOrWhiteSpace(part.FieldKey) ||
                        !info.Metadata.TryGetValue(part.FieldKey, out value) || string.IsNullOrWhiteSpace(value))
                    {
                        error = "Missing " + GetFieldLabel(part.FieldKey ?? string.Empty);
                        return string.Empty;
                    }
                    result.Append(SanitizeFileNamePart(value));
                    if (i < template.Parts.Count - 1)
                        result.Append(part.SeparatorAfter ?? string.Empty);
                }
                string name = result.ToString().Trim();
                error = GetFileNameError(name);
                return name;
            }

            private static NamingTemplate CopyNamingTemplate(NamingTemplate source)
            {
                NamingTemplate copy = new NamingTemplate();
                foreach (NamingPart part in source.Parts)
                    copy.Parts.Add(new NamingPart
                    {
                        FieldKey = part.FieldKey,
                        SeparatorAfter = part.SeparatorAfter
                    });
                return copy;
            }

            private ExportFormState CaptureFormState()
            {
                ExportFormState state = new ExportFormState
                {
                    NamingTemplate = CopyNamingTemplate(namingTemplate),
                    Rows = new List<ExportRowState>()
                };
                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                    state.Rows.Add(new ExportRowState
                    {
                        FileName = info.FileName,
                        Status = info.Status,
                        IsManualName = info.IsManualName,
                        Export = ToBool(gridRow.Cells["Export"].Value)
                    });
                }
                return state;
            }

            private static bool SameUserChoices(ExportFormState left, ExportFormState right)
            {
                if (left.Rows.Count != right.Rows.Count ||
                    left.NamingTemplate.Parts.Count != right.NamingTemplate.Parts.Count)
                    return false;
                for (int i = 0; i < left.NamingTemplate.Parts.Count; i++)
                    if (!string.Equals(left.NamingTemplate.Parts[i].FieldKey,
                            right.NamingTemplate.Parts[i].FieldKey, StringComparison.Ordinal) ||
                        !string.Equals(left.NamingTemplate.Parts[i].SeparatorAfter,
                            right.NamingTemplate.Parts[i].SeparatorAfter, StringComparison.Ordinal))
                        return false;
                for (int i = 0; i < left.Rows.Count; i++)
                    if (!string.Equals(left.Rows[i].FileName, right.Rows[i].FileName, StringComparison.Ordinal) ||
                        left.Rows[i].IsManualName != right.Rows[i].IsManualName ||
                        left.Rows[i].Export != right.Rows[i].Export)
                        return false;
                return true;
            }

            private void RecordFormAction()
            {
                if (restoringUndo || lastCommittedState == null) return;
                ExportFormState current = CaptureFormState();
                if (!SameUserChoices(current, lastCommittedState))
                {
                    undoStack.Push(lastCommittedState);
                    lastCommittedState = current;
                    undoButton.Enabled = true;
                }
            }

            private void UndoLastAction()
            {
                grid.EndEdit();
                if (undoStack.Count == 0) return;
                ExportFormState previous = undoStack.Pop();
                bool templateChanged = !SameNamingTemplate(namingTemplate, previous.NamingTemplate);
                restoringUndo = true;
                try
                {
                    namingTemplate = CopyNamingTemplate(previous.NamingTemplate);
                    for (int i = 0; i < grid.Rows.Count && i < previous.Rows.Count; i++)
                    {
                        DataGridViewRow gridRow = grid.Rows[i];
                        ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                        ExportRowState saved = previous.Rows[i];
                        info.FileName = saved.FileName;
                        info.Status = saved.Status;
                        info.IsManualName = saved.IsManualName;
                        gridRow.Cells["FileName"].Value = saved.FileName;
                        gridRow.Cells["Export"].Value = saved.Export;
                        SetRowStatus(gridRow, saved.Status);
                    }
                }
                finally { restoringUndo = false; }
                lastCommittedState = CaptureFormState();
                undoButton.Enabled = undoStack.Count > 0;
                if (templateChanged && !ExportSettings.SaveNamingTemplate(namingTemplate))
                    MessageBox.Show(this, "The previous naming setting was restored here but could not be remembered.",
                        "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            private static bool SameNamingTemplate(NamingTemplate left, NamingTemplate right)
            {
                if (left.Parts.Count != right.Parts.Count) return false;
                for (int i = 0; i < left.Parts.Count; i++)
                    if (!string.Equals(left.Parts[i].FieldKey, right.Parts[i].FieldKey, StringComparison.Ordinal) ||
                        !string.Equals(left.Parts[i].SeparatorAfter, right.Parts[i].SeparatorAfter, StringComparison.Ordinal))
                        return false;
                return true;
            }

            private void ResetFileNames()
            {
                grid.EndEdit();
                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                    if (info == null) continue;
                    info.IsManualName = false;
                    UpdateAutomaticName(info);
                    gridRow.Cells["FileName"].Value = info.FileName;
                    SetRowStatus(gridRow, info.Status);
                }
                RecordFormAction();
            }

            private void ChangeAutoNamingSetting()
            {
                grid.EndEdit();
                using (AutoNamingDialog dialog = new AutoNamingDialog(
                    availableFields, namingTemplate, rows, BuildAutomaticName))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    namingTemplate = dialog.Result;
                    RefreshAutomaticNames();
                    foreach (DataGridViewRow gridRow in grid.Rows)
                    {
                        ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                        if (info == null || info.IsManualName) continue;
                        gridRow.Cells["FileName"].Value = info.FileName;
                        SetRowStatus(gridRow, info.Status);
                    }
                    RecordFormAction();
                    if (!ExportSettings.SaveNamingTemplate(namingTemplate))
                        MessageBox.Show(this, "The naming setting is active for this session but could not be remembered.",
                            "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "FileName")
                    return;
                DataGridViewRow gridRow = grid.Rows[e.RowIndex];
                ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                if (info == null) return;
                string editedName = Convert.ToString(gridRow.Cells["FileName"].Value) ?? string.Empty;
                if (!info.IsManualName && string.Equals(editedName, info.FileName, StringComparison.Ordinal))
                    return;
                info.FileName = editedName;
                info.IsManualName = true;
                string error = GetFileNameError(info.FileName);
                info.Status = error == null ? "Ready" : "! " + error;
                SetRowStatus(gridRow, info.Status);
                RecordFormAction();
            }

            private void Grid_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0 ||
                    grid.Columns[e.ColumnIndex].Name != "FileName") return;
                DataGridViewRow row = grid.Rows[e.RowIndex];
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Use automatic name", null, delegate
                {
                    grid.EndEdit();
                    ConfigurationExportRow info = row.Tag as ConfigurationExportRow;
                    if (info == null) return;
                    info.IsManualName = false;
                    UpdateAutomaticName(info);
                    row.Cells["FileName"].Value = info.FileName;
                    SetRowStatus(row, info.Status);
                    RecordFormAction();
                });
                menu.Closed += delegate { menu.Dispose(); };
                menu.Show(grid, grid.PointToClient(Cursor.Position));
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

                List<int> clickedSelection = checkboxSelectionSnapshot.ToList();
                checkboxSelectionSnapshot.Clear();
                BeginInvoke(
                    (MethodInvoker)delegate
                    {
                        bool target = ToBool(grid.Rows[e.RowIndex].Cells["Export"].Value);

                        if (clickedSelection.Count > 1 && clickedSelection.Contains(e.RowIndex))
                        {
                            PropagateCheckStateToRows(clickedSelection, target);
                        }
                        RecordFormAction();
                    });
            }

            private void Grid_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space || grid.SelectedRows.Count == 0 ||
                    grid.IsCurrentCellInEditMode || grid.CurrentCell == null ||
                    grid.CurrentCell.OwningColumn.Name != "Export")
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
                RecordFormAction();
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

            }

            private void ToggleAllRows()
            {
                grid.EndEdit();
                GridCheckBehavior.ToggleSelectedThenAll(
                    grid,
                    row => ToBool(row.Cells["Export"].Value),
                    (row, value) => row.Cells["Export"].Value = value);
                RecordFormAction();

            }

            private void UncheckDerivedRows()
            {
                grid.EndEdit();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    ConfigurationExportRow info = row.Tag as ConfigurationExportRow;
                    if (info != null && info.IsDerived)
                        row.Cells["Export"].Value = false;
                }
                RecordFormAction();

            }

            private void ExportSelectedConfigurations()
            {
                grid.EndEdit();
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

                if (!RefreshSelectedAutomaticNames(selectedRows)) return;
                lastCommittedState = CaptureFormState();

                bool invalidName = false;
                foreach (DataGridViewRow gridRow in selectedRows)
                {
                    ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                    if (info == null) continue;
                    string error = !info.IsManualName && info.Status.StartsWith("!", StringComparison.Ordinal)
                        ? info.Status.Substring(1).Trim() : GetFileNameError(info.FileName);
                    if (error == null) continue;
                    info.Status = "! " + error;
                    SetRowStatus(gridRow, info.Status);
                    invalidName = true;
                }
                if (invalidName)
                {
                    MessageBox.Show(this, "Resolve the file names marked in Status. You can edit a name directly or change the automatic naming setting.",
                        "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    HashSet<string> duplicates = new HashSet<string>(
                        outputPaths.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                            .Where(group => group.Count() > 1).Select(group => group.Key),
                        StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < selectedRows.Count; i++)
                        if (duplicates.Contains(outputPaths[i]))
                            SetRowStatus(selectedRows[i], "! Duplicate file name");
                    MessageBox.Show(
                        this,
                        "Two configurations would create the same file name. Edit a file name or change the automatic naming setting.",
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

            private bool RefreshSelectedAutomaticNames(IList<DataGridViewRow> selectedRows)
            {
                string original = string.Empty;
                bool refreshed = true;
                bool restored = true;
                try
                {
                    IConfiguration active = modelDoc.ConfigurationManager.ActiveConfiguration;
                    original = active == null ? string.Empty : active.Name;
                    foreach (DataGridViewRow gridRow in selectedRows)
                    {
                        ConfigurationExportRow info = gridRow.Tag as ConfigurationExportRow;
                        if (info == null || info.IsManualName) continue;
                        string activationError;
                        if (!ActivateConfigurationSafely(modelDoc, info.ConfigurationName, out activationError))
                        {
                            info.Status = "! " + activationError;
                            SetRowStatus(gridRow, info.Status);
                            continue;
                        }
                        info.Metadata = ReadNamingMetadata(modelDoc, documentDescription, info.ConfigurationName, false);
                        UpdateAutomaticName(info);
                        gridRow.Cells["FileName"].Value = info.FileName;
                        SetRowStatus(gridRow, info.Status);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not refresh configuration names: " + ex.Message,
                        "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    refreshed = false;
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(original))
                    {
                        string restoreError;
                        if (!ActivateConfigurationSafely(modelDoc, original, out restoreError))
                        {
                            restored = false;
                            MessageBox.Show(this, "Could not restore the original configuration: " + restoreError,
                                "Cabin Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
                return refreshed && restored;
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
                grid.Enabled = false;
                namingButton.Enabled = false;
                undoButton.Enabled = false;
                resetFileNamesButton.Enabled = false;
                checkAllButton.Enabled = false;
                uncheckDerivedButton.Enabled = false;
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

                    WriteReport(reportLines);

                    Cursor = previousCursor;
                    exportButton.Enabled = true;
                    closeButton.Enabled = true;
                    grid.Enabled = true;
                    namingButton.Enabled = true;
                    resetFileNamesButton.Enabled = true;
                    checkAllButton.Enabled = true;
                    uncheckDerivedButton.Enabled = true;
                    undoStack.Clear();
                    lastCommittedState = CaptureFormState();
                    undoButton.Enabled = false;

                    if (exportedCount > 0 || skippedCount > 0 || failedCount > 0)
                    {
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
                row.Cells["Status"].ToolTipText = status;

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

        private sealed class AutoNamingDialog : Form
        {
            private readonly DataGridView fields = new DataGridView();
            private readonly ComboBox previewConfiguration = new ComboBox();
            private readonly Label preview = new Label();
            private readonly IList<ConfigurationExportRow> rows;
            private readonly BuildNamePreview buildName;
            private bool updatingPreview;
            public NamingTemplate Result { get; private set; }

            public AutoNamingDialog(IList<NamingField> choices, NamingTemplate current,
                IList<ConfigurationExportRow> rows, BuildNamePreview buildName)
            {
                this.rows = rows;
                this.buildName = buildName;
                Text = "Change Auto Naming Setting";
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(590, 410);
                Size = new Size(680, 490);
                Font = new Font("Segoe UI", 9F);

                Label guidance = new Label
                {
                    Text = "Choose fields in order and type any separator after each. The last separator is ignored.",
                    Dock = DockStyle.Top,
                    Height = 48,
                    Padding = new Padding(12, 12, 12, 0)
                };

                fields.Dock = DockStyle.Fill;
                fields.AllowUserToAddRows = false;
                fields.AllowUserToDeleteRows = false;
                fields.AllowUserToResizeRows = false;
                fields.RowHeadersVisible = false;
                fields.MultiSelect = false;
                fields.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                fields.EditMode = DataGridViewEditMode.EditOnEnter;
                fields.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                fields.BackgroundColor = Color.White;
                DataGridViewComboBoxColumn property = new DataGridViewComboBoxColumn
                {
                    Name = "Property", HeaderText = "Property", FillWeight = 75,
                    DataSource = choices.ToList(), DisplayMember = "Label", ValueMember = "Key",
                    FlatStyle = FlatStyle.Flat, SortMode = DataGridViewColumnSortMode.NotSortable
                };
                fields.Columns.Add(property);
                fields.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Separator", HeaderText = "Separator after", FillWeight = 25,
                    MinimumWidth = 125, SortMode = DataGridViewColumnSortMode.NotSortable
                });
                fields.CurrentCellDirtyStateChanged += delegate
                {
                    if (fields.IsCurrentCellDirty) fields.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                fields.CellValueChanged += delegate { UpdatePreview(); };
                fields.CellEndEdit += delegate { UpdatePreview(); };
                fields.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(fields, 0, 0);

                FlowLayoutPanel rowActions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6)
                };
                Button add = new Button { Text = "Add field", AutoSize = true };
                add.Click += delegate
                {
                    int index = fields.Rows.Add("configuration.name", string.Empty);
                    fields.CurrentCell = fields.Rows[index].Cells["Separator"];
                    fields.Focus();
                    UpdatePreview();
                    fields.BeginEdit(true);
                };
                Button remove = new Button { Text = "Remove field", AutoSize = true };
                remove.Click += delegate
                {
                    if (fields.CurrentRow != null) fields.Rows.Remove(fields.CurrentRow);
                    UpdatePreview();
                };
                Button up = new Button { Text = "Move up", AutoSize = true };
                up.Click += delegate { MoveRow(-1); };
                Button down = new Button { Text = "Move down", AutoSize = true };
                down.Click += delegate { MoveRow(1); };
                rowActions.Controls.Add(add);
                rowActions.Controls.Add(remove);
                rowActions.Controls.Add(up);
                rowActions.Controls.Add(down);
                layout.Controls.Add(rowActions, 0, 1);

                TableLayoutPanel previewArea = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2,
                    Padding = new Padding(10, 8, 10, 8)
                };
                previewArea.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                previewArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                previewArea.Controls.Add(new Label
                {
                    Text = "Preview configuration", AutoSize = true, Margin = new Padding(0, 6, 8, 0)
                }, 0, 0);
                previewConfiguration.DropDownStyle = ComboBoxStyle.DropDownList;
                previewConfiguration.Width = 220;
                foreach (ConfigurationExportRow row in rows) previewConfiguration.Items.Add(row.ConfigurationName);
                previewConfiguration.SelectedIndexChanged += delegate { UpdatePreview(); };
                previewArea.Controls.Add(previewConfiguration, 1, 0);
                preview.AutoSize = true;
                preview.MaximumSize = new Size(590, 0);
                preview.Margin = new Padding(0, 12, 0, 6);
                previewArea.Controls.Add(preview, 0, 1);
                previewArea.SetColumnSpan(preview, 2);
                layout.Controls.Add(previewArea, 0, 2);

                FlowLayoutPanel footer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8, 5, 8, 5)
                };
                Button apply = new Button { Text = "Apply", Width = 85, Height = 30 };
                Button cancel = new Button { Text = "Cancel", Width = 85, Height = 30, DialogResult = DialogResult.Cancel };
                apply.Click += delegate { Apply(); };
                footer.Controls.Add(cancel);
                footer.Controls.Add(apply);
                TableLayoutPanel shell = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3
                };
                shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                shell.Controls.Add(guidance, 0, 0);
                shell.Controls.Add(layout, 0, 1);
                shell.Controls.Add(footer, 0, 2);
                Controls.Add(shell);
                AcceptButton = apply;
                CancelButton = cancel;

                foreach (NamingPart part in current.Parts)
                    fields.Rows.Add(part.FieldKey, part.SeparatorAfter);
                if (previewConfiguration.Items.Count > 0) previewConfiguration.SelectedIndex = 0;
                if (fields.Rows.Count > 0)
                    fields.CurrentCell = fields.Rows[0].Cells["Separator"];
                UpdatePreview();
            }

            private NamingTemplate ReadTemplate()
            {
                fields.EndEdit();
                NamingTemplate template = new NamingTemplate();
                foreach (DataGridViewRow row in fields.Rows)
                    template.Parts.Add(new NamingPart
                    {
                        FieldKey = Convert.ToString(row.Cells["Property"].Value),
                        SeparatorAfter = Convert.ToString(row.Cells["Separator"].Value) ?? string.Empty
                    });
                return template;
            }

            private void MoveRow(int offset)
            {
                if (fields.CurrentRow == null) return;
                int from = fields.CurrentRow.Index;
                int to = from + offset;
                if (to < 0 || to >= fields.Rows.Count) return;
                fields.EndEdit();
                object key = fields.Rows[from].Cells["Property"].Value;
                object separator = fields.Rows[from].Cells["Separator"].Value;
                fields.Rows[from].Cells["Property"].Value = fields.Rows[to].Cells["Property"].Value;
                fields.Rows[from].Cells["Separator"].Value = fields.Rows[to].Cells["Separator"].Value;
                fields.Rows[to].Cells["Property"].Value = key;
                fields.Rows[to].Cells["Separator"].Value = separator;
                fields.CurrentCell = fields.Rows[to].Cells["Property"];
                UpdatePreview();
            }

            private void UpdatePreview()
            {
                if (updatingPreview || previewConfiguration.SelectedIndex < 0 || rows == null || buildName == null) return;
                updatingPreview = true;
                try
                {
                    NamingTemplate template = ReadTemplate();
                    string error;
                    string name = buildName(template, rows[previewConfiguration.SelectedIndex], out error);
                    preview.ForeColor = error == null ? Color.FromArgb(35, 45, 55) : Color.DarkRed;
                    preview.Text = error == null ? "Preview: " + name :
                        "Preview: " + (string.IsNullOrWhiteSpace(name) ? error : name + " — " + error);
                }
                finally { updatingPreview = false; }
            }

            private void Apply()
            {
                NamingTemplate template = ReadTemplate();
                if (template.Parts.Count == 0 || template.Parts.Any(part => string.IsNullOrWhiteSpace(part.FieldKey)))
                {
                    MessageBox.Show(this, "Choose at least one property for each row.", "Cabin Tools",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Result = template;
                DialogResult = DialogResult.OK;
                Close();
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

        private static string GetFileNameError(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "File name is blank.";
            if (value != value.Trim() || value.EndsWith(".", StringComparison.Ordinal))
                return "Remove leading/trailing spaces and the final period.";
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                return "Windows file names cannot contain \\ / : * ? \" < > |.";
            string stem = value.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
                "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
                "LPT8", "LPT9" }.Contains(stem))
                return "That file name is reserved by Windows.";
            if (new[] { ".step", ".igs", ".sat" }.Any(ext => value.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return "Enter the file name without a STEP, IGES, or ACIS extension.";
            return null;
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

            private static string NamingSettingsPath
            {
                get { return Path.Combine(SettingsFolder, "ConfigurationNeutralExport.naming.txt"); }
            }

            public static NamingTemplate LoadNamingTemplate()
            {
                try
                {
                    if (!File.Exists(NamingSettingsPath)) return NamingTemplate.Default();
                    string[] lines = File.ReadAllLines(NamingSettingsPath, Encoding.UTF8);
                    if (lines.Length < 2 || lines.Length > 51 || lines[0] != "CabinToolsNamingV1")
                        return NamingTemplate.Default();
                    NamingTemplate template = new NamingTemplate();
                    foreach (string line in lines.Skip(1))
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length != 2) return NamingTemplate.Default();
                        string key = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                        string separator = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                        if (string.IsNullOrWhiteSpace(key)) return NamingTemplate.Default();
                        template.Parts.Add(new NamingPart { FieldKey = key, SeparatorAfter = separator });
                    }
                    return template.Parts.Count > 0 ? template : NamingTemplate.Default();
                }
                catch { return NamingTemplate.Default(); }
            }

            public static bool SaveNamingTemplate(NamingTemplate template)
            {
                try
                {
                    if (template == null || template.Parts.Count == 0) return false;
                    Directory.CreateDirectory(SettingsFolder);
                    List<string> lines = new List<string> { "CabinToolsNamingV1" };
                    foreach (NamingPart part in template.Parts)
                        lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(part.FieldKey ?? string.Empty)) + "\t" +
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(part.SeparatorAfter ?? string.Empty)));
                    File.WriteAllLines(NamingSettingsPath, lines.ToArray(), Encoding.UTF8);
                    return true;
                }
                catch { return false; }
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
