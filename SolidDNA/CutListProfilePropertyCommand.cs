using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using CADBooster.SolidDna;
using static CADBooster.SolidDna.SolidWorksEnvironment;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Editable cut-list custom-property table.
    ///
    /// Main rules:
    /// - The tool scans all cut-list items and all property names.
    /// - The user chooses which property names are shown as columns.
    /// - Empty cells are not written. Existing values remain unchanged.
    /// - Clearing a property requires the explicit Clear Column command.
    ///     /// - Description, Brand, and Model have editable dropdown presets.
    /// - New dropdown values typed by the user are saved for future sessions.
    /// - Linked cut-list properties are written using replace, unlink, and delete-add fallback paths.
    /// </summary>
    internal static class CutListProfilePropertyCommand
    {
        internal const string DescriptionPropertyName = "Description";
        internal const string BrandPropertyName = "Brand";
        internal const string ModelPropertyName = "Model";

        private static CutListPropertyEditorForm activeCutListForm;

        public static void UpdateActivePartTopBottomProfiles()
        {
            ShowCutListProfilePropertyForm();
        }

        public static void ShowCutListProfilePropertyForm()
        {
            try
            {
                IModelDoc2 modelDoc = CabinCustomPropertyStore.GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage("Open a weldment/profile part before running this command.", MessageBoxIcon.Warning);
                    return;
                }

                if (modelDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    ShowMessage("This command works only on an active part document.", MessageBoxIcon.Warning);
                    return;
                }

                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(modelDoc);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(writeBlockReason, MessageBoxIcon.Warning);
                    return;
                }

                if (activeCutListForm != null && !activeCutListForm.IsDisposed)
                {
                    activeCutListForm.Activate();
                    activeCutListForm.RefreshFromSolidWorks();
                    return;
                }

                activeCutListForm = new CutListPropertyEditorForm(modelDoc);
                activeCutListForm.FormClosed += delegate { activeCutListForm = null; };
                activeCutListForm.Show();
            }
            catch (Exception ex)
            {
                ShowMessage("Cabin Tools could not open the cut-list property editor.\r\n\r\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        internal static List<CutListItemInfo> GetCutListItems(IModelDoc2 modelDoc, List<string> messages)
        {
            List<CutListItemInfo> result = new List<CutListItemInfo>();

            if (modelDoc == null)
                return result;

            TryUpdateCutList(modelDoc, messages, "before reading cut-list items");

            Feature rootFeature = null;
            try { rootFeature = modelDoc.FirstFeature() as Feature; }
            catch { rootFeature = null; }

            TraverseFeatures(rootFeature, false, result);
            return result;
        }

        internal static void SetTextProperty(ICustomPropertyManager propertyManager, string propertyName, string value)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            string cleanName = propertyName.Trim();
            string cleanValue = value ?? string.Empty;
            string actualName = FindExistingPropertyName(propertyManager, cleanName);
            string writeName = string.IsNullOrWhiteSpace(actualName) ? cleanName : actualName;

            // Weldment cut-list properties can be linked to the weldment profile / cut-list source.
            // First try the normal replace path. If SOLIDWORKS still reads a different value, use a
            // delete-and-add fallback. This is more effective for linked weldment profile properties
            // such as Description, Brand, and Model.
            TryUnlinkProperty(propertyManager, writeName);
            TryAddTextProperty(propertyManager, writeName, cleanValue, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            TrySetExistingProperty(propertyManager, writeName, cleanValue);

            string valueAfterReplace = ReadTextProperty(propertyManager, cleanName);
            if (string.Equals(valueAfterReplace, cleanValue, StringComparison.Ordinal))
                return;

            TryUnlinkProperty(propertyManager, writeName);
            TryDeleteProperty(propertyManager, writeName);
            TryAddTextProperty(propertyManager, cleanName, cleanValue, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
            TrySetExistingProperty(propertyManager, cleanName, cleanValue);
        }

        private static void TryAddTextProperty(ICustomPropertyManager propertyManager, string propertyName, string value, int addOption)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                propertyManager.Add3(
                    propertyName.Trim(),
                    (int)swCustomInfoType_e.swCustomInfoText,
                    value ?? string.Empty,
                    addOption);
            }
            catch
            {
                // The caller also attempts Set2. Some SOLIDWORKS interop versions return failures
                // differently for cut-list properties, so keep this path non-fatal.
            }
        }

        private static void TryDeleteProperty(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                propertyManager.GetType().InvokeMember(
                    "Delete2",
                    BindingFlags.InvokeMethod,
                    null,
                    propertyManager,
                    new object[] { propertyName.Trim() });
            }
            catch
            {
                try
                {
                    propertyManager.GetType().InvokeMember(
                        "Delete",
                        BindingFlags.InvokeMethod,
                        null,
                        propertyManager,
                        new object[] { propertyName.Trim() });
                }
                catch
                {
                }
            }
        }

        private static string FindExistingPropertyName(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            List<string> names = GetPropertyNames(propertyManager);
            foreach (string name in names)
            {
                if (string.Equals(name, propertyName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            return string.Empty;
        }

        private static void TryUnlinkProperty(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                propertyManager.GetType().InvokeMember(
                    "LinkProperty",
                    BindingFlags.InvokeMethod,
                    null,
                    propertyManager,
                    new object[] { propertyName.Trim(), false });
            }
            catch
            {
                // Older interop versions or non-linked property managers may not expose LinkProperty.
                // In that case Add3/Set2 below is still attempted.
            }
        }

        private static void TrySetExistingProperty(ICustomPropertyManager propertyManager, string propertyName, string value)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                propertyManager.GetType().InvokeMember(
                    "Set2",
                    BindingFlags.InvokeMethod,
                    null,
                    propertyManager,
                    new object[] { propertyName.Trim(), value ?? string.Empty });
            }
            catch
            {
                // Add3 already attempted the write. Set2 is only an extra compatibility path.
            }
        }

        internal static string ReadTextProperty(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            string lookupName = FindExistingPropertyName(propertyManager, propertyName);
            if (string.IsNullOrWhiteSpace(lookupName))
                lookupName = propertyName.Trim();

            string rawValue;
            string resolvedValue;
            bool wasResolved;
            bool linked;

            try
            {
                int result = propertyManager.Get6(
                    lookupName,
                    false,
                    out rawValue,
                    out resolvedValue,
                    out wasResolved,
                    out linked);

                if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                    return string.Empty;

                if (!string.IsNullOrWhiteSpace(resolvedValue))
                    return resolvedValue.Trim();

                if (!string.IsNullOrWhiteSpace(rawValue))
                    return rawValue.Trim();
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        internal static List<string> GetPropertyNames(ICustomPropertyManager propertyManager)
        {
            List<string> names = new List<string>();

            if (propertyManager == null)
                return names;

            try
            {
                object namesObject = propertyManager.GetNames();
                Array namesArray = namesObject as Array;
                if (namesArray == null)
                    return names;

                foreach (object nameObject in namesArray)
                {
                    string name = nameObject == null ? string.Empty : Convert.ToString(nameObject).Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !ContainsIgnoreCase(names, name))
                        names.Add(name);
                }
            }
            catch
            {
                // Some old or empty property managers return no names.
            }

            return names;
        }

        internal static string WriteReport(IModelDoc2 modelDoc, string content)
        {
            try
            {
                string root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "CabinTools", "CutListReports");
                Directory.CreateDirectory(root);
                string safeTitle = SanitizeFileName(modelDoc == null ? "Part" : (modelDoc.GetTitle() ?? "Part"));
                string path = Path.Combine(root, "CutListPropertyEditor_" + safeTitle + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void TraverseFeatures(Feature feature, bool featureIsSubFeature, List<CutListItemInfo> result)
        {
            Feature current = feature;
            while (current != null)
            {
                string typeName = SafeFeatureTypeName(current);
                if (string.Equals(typeName, "CutListFolder", StringComparison.OrdinalIgnoreCase))
                {
                    CutListItemInfo item = CreateCutListItemInfo(current);
                    if (item != null && !ContainsCutListItem(result, item))
                        result.Add(item);
                }

                Feature subFeature = null;
                try { subFeature = current.GetFirstSubFeature() as Feature; }
                catch { subFeature = null; }

                if (subFeature != null)
                    TraverseFeatures(subFeature, true, result);

                try
                {
                    current = featureIsSubFeature ? current.GetNextSubFeature() as Feature : current.GetNextFeature() as Feature;
                }
                catch
                {
                    current = null;
                }
            }
        }

        private static CutListItemInfo CreateCutListItemInfo(Feature feature)
        {
            if (feature == null)
                return null;

            int bodyCount = 0;
            string bodySignature = string.Empty;
            if (!TryGetCutListBodyInformation(feature, out bodyCount, out bodySignature))
            {
                // Do not show empty/generated cut-list folders.
                // These folders can appear as internal subfeatures and were the reason
                // the editor listed more rows than the real visible cut-list item count.
                return null;
            }

            if (bodyCount <= 0)
                return null;

            ICustomPropertyManager propertyManager = null;
            try { propertyManager = feature.CustomPropertyManager; }
            catch { propertyManager = null; }

            if (propertyManager == null)
                return null;

            CutListItemInfo item = new CutListItemInfo();
            item.Feature = feature;
            item.PropertyManager = propertyManager;
            item.FeatureName = SafeFeatureName(feature);
            item.BodyCount = bodyCount;
            item.BodySignature = bodySignature;
            if (!string.IsNullOrWhiteSpace(bodySignature))
            {
                string[] bodyNames = bodySignature.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string bodyName in bodyNames)
                {
                    if (!string.IsNullOrWhiteSpace(bodyName) && !ContainsIgnoreCase(item.BodyNames, bodyName))
                        item.BodyNames.Add(bodyName.Trim());
                }
            }

            List<string> names = GetPropertyNames(propertyManager);
            foreach (string name in names)
            {
                if (!item.Properties.ContainsKey(name))
                    item.Properties.Add(name, ReadTextProperty(propertyManager, name));
            }

            return item;
        }

        private static bool TryGetCutListBodyInformation(Feature feature, out int bodyCount, out string bodySignature)
        {
            bodyCount = 0;
            bodySignature = string.Empty;

            if (feature == null)
                return false;

            object specificFeature = null;
            try { specificFeature = feature.GetSpecificFeature2(); }
            catch { specificFeature = null; }

            if (specificFeature == null)
                return false;

            bool gotBodyCount = false;

            try
            {
                object countObject = specificFeature.GetType().InvokeMember(
                    "GetBodyCount",
                    BindingFlags.InvokeMethod,
                    null,
                    specificFeature,
                    null);

                if (countObject != null)
                {
                    bodyCount = Convert.ToInt32(countObject);
                    gotBodyCount = true;
                }
            }
            catch
            {
                gotBodyCount = false;
            }

            try
            {
                object bodiesObject = specificFeature.GetType().InvokeMember(
                    "GetBodies",
                    BindingFlags.InvokeMethod,
                    null,
                    specificFeature,
                    null);

                Array bodiesArray = bodiesObject as Array;
                if (bodiesArray != null)
                {
                    List<string> bodyNames = new List<string>();
                    int nonNullBodies = 0;

                    foreach (object bodyObject in bodiesArray)
                    {
                        if (bodyObject == null)
                            continue;

                        nonNullBodies++;
                        string bodyName = SafeBodyName(bodyObject);
                        if (!string.IsNullOrWhiteSpace(bodyName) && !ContainsIgnoreCase(bodyNames, bodyName))
                            bodyNames.Add(bodyName);
                    }

                    bodyCount = nonNullBodies;
                    bodyNames.Sort(StringComparer.OrdinalIgnoreCase);
                    bodySignature = string.Join("|", bodyNames.ToArray());
                    return true;
                }
            }
            catch
            {
                // Some SOLIDWORKS versions expose GetBodyCount but fail on GetBodies
                // until after a rebuild. In that case the verified count is enough.
            }

            if (gotBodyCount)
                return true;

            return false;
        }

        private static string SafeBodyName(object bodyObject)
        {
            if (bodyObject == null)
                return string.Empty;

            try
            {
                object nameObject = bodyObject.GetType().InvokeMember(
                    "Name",
                    BindingFlags.GetProperty,
                    null,
                    bodyObject,
                    null);

                return nameObject == null ? string.Empty : Convert.ToString(nameObject);
            }
            catch
            {
            }

            try
            {
                object nameObject = bodyObject.GetType().InvokeMember(
                    "GetName",
                    BindingFlags.InvokeMethod,
                    null,
                    bodyObject,
                    null);

                return nameObject == null ? string.Empty : Convert.ToString(nameObject);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ContainsCutListItem(List<CutListItemInfo> existingItems, CutListItemInfo candidate)
        {
            if (existingItems == null || candidate == null)
                return false;

            foreach (CutListItemInfo existing in existingItems)
            {
                if (existing == null)
                    continue;

                if (object.ReferenceEquals(existing.Feature, candidate.Feature))
                    return true;

                if (!string.IsNullOrWhiteSpace(existing.BodySignature) &&
                    !string.IsNullOrWhiteSpace(candidate.BodySignature) &&
                    string.Equals(existing.BodySignature, candidate.BodySignature, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryUpdateCutList(IModelDoc2 modelDoc, List<string> messages, string context)
        {
            if (modelDoc == null)
                return;

            bool updateAttempted = false;
            bool updateSucceeded = false;

            try
            {
                Feature rootFeature = modelDoc.FirstFeature() as Feature;
                TryUpdateCutListFromFeatures(rootFeature, false, ref updateAttempted, ref updateSucceeded, messages);
            }
            catch (Exception ex)
            {
                if (messages != null)
                    messages.Add("Cut-list feature scan failed " + SafeContextText(context) + ": " + ex.Message);
            }

            try { modelDoc.ForceRebuild3(false); }
            catch (Exception ex)
            {
                if (messages != null)
                    messages.Add("Force rebuild failed " + SafeContextText(context) + ": " + ex.Message);
            }
        }

        private static void TryUpdateCutListFromFeatures(Feature feature, bool featureIsSubFeature, ref bool attempted, ref bool succeeded, List<string> messages)
        {
            Feature current = feature;
            while (current != null)
            {
                string typeName = SafeFeatureTypeName(current);
                if (string.Equals(typeName, "SolidBodyFolder", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(typeName, "CutListFolder", StringComparison.OrdinalIgnoreCase))
                {
                    TryInvokeUpdateCutList(current, ref attempted, ref succeeded, messages);
                }

                Feature subFeature = null;
                try { subFeature = current.GetFirstSubFeature() as Feature; }
                catch { subFeature = null; }

                if (subFeature != null)
                    TryUpdateCutListFromFeatures(subFeature, true, ref attempted, ref succeeded, messages);

                try
                {
                    current = featureIsSubFeature ? current.GetNextSubFeature() as Feature : current.GetNextFeature() as Feature;
                }
                catch
                {
                    current = null;
                }
            }
        }

        private static void TryInvokeUpdateCutList(Feature feature, ref bool attempted, ref bool succeeded, List<string> messages)
        {
            object specificFeature = null;
            try { specificFeature = feature.GetSpecificFeature2(); }
            catch { specificFeature = null; }

            if (specificFeature == null)
                return;

            try
            {
                attempted = true;
                object result = specificFeature.GetType().InvokeMember("UpdateCutList", BindingFlags.InvokeMethod, null, specificFeature, null);
                if (result is bool)
                    succeeded = succeeded || (bool)result;
                else
                    succeeded = true;
            }
            catch (MissingMethodException)
            {
            }
            catch (Exception ex)
            {
                if (messages != null)
                    messages.Add("UpdateCutList failed for feature '" + SafeFeatureName(feature) + "': " + ex.Message);
            }
        }

        private static string SafeFeatureTypeName(Feature feature)
        {
            if (feature == null)
                return string.Empty;

            try { return feature.GetTypeName2() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeFeatureName(Feature feature)
        {
            if (feature == null)
                return string.Empty;

            try { return feature.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeContextText(string context)
        {
            return string.IsNullOrWhiteSpace(context) ? string.Empty : "(" + context.Trim() + ")";
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Part";

            string sanitized = value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(invalid, '_');
            return sanitized.Trim();
        }

        internal static bool ContainsIgnoreCase(IList<string> values, string value)
        {
            if (values == null || value == null)
                return false;

            foreach (string current in values)
            {
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            try
            {
                MessageBox.Show(message, "Cabin Tools - Cut-List Property Editor", MessageBoxButtons.OK, icon);
            }
            catch
            {
                IApplication.ShowMessageBox(
                    message,
                    icon == MessageBoxIcon.Error ? SolidWorksMessageBoxIcon.Stop :
                    icon == MessageBoxIcon.Warning ? SolidWorksMessageBoxIcon.Warning :
                    SolidWorksMessageBoxIcon.Information);
            }
        }

        internal sealed class CutListItemInfo
        {
            public Feature Feature;
            public ICustomPropertyManager PropertyManager;
            public string FeatureName = string.Empty;
            public int BodyCount;
            public string BodySignature = string.Empty;
            public List<string> BodyNames = new List<string>();
            public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class CutListPropertyEditorForm : Form
        {
            private readonly IModelDoc2 modelDoc;
            private readonly List<CutListItemInfo> items = new List<CutListItemInfo>();
            private readonly List<string> allPropertyNames = new List<string>();
            private readonly List<string> visiblePropertyNames = new List<string>();
            private readonly PropertyDropdownSettings dropdownSettings;

            private DataGridView grid;
            private Label statusLabel;

            private const string ApplyColumnName = "__Apply";
            private const string ItemColumnName = "__Item";
            private const string ShowColumnName = "__Show";
            private const string StatusColumnName = "__Status";

            private enum RowTargetScope
            {
                Cancelled,
                CheckedRows,
                AllRows
            }

            public CutListPropertyEditorForm(IModelDoc2 modelDoc)
            {
                this.modelDoc = modelDoc;
                this.dropdownSettings = PropertyDropdownSettings.Load();

                Text = "Cabin Tools - Cut-List Property Editor";
                Width = 1280;
                Height = 760;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadCutListData();
                BuildGrid();
            }

            private void BuildLayout()
            {
                MinimumSize = new Size(900, 560);

                Button editButton = new Button();
                editButton.Text = "Edit...";
                editButton.Left = 12;
                editButton.Top = 14;
                editButton.Width = 105;
                editButton.Click += delegate { ShowEditMenu(); };
                Controls.Add(editButton);

                Button toggleCheckButton = new Button();
                toggleCheckButton.Text = "Check / uncheck all";
                toggleCheckButton.Left = 125;
                toggleCheckButton.Top = 14;
                toggleCheckButton.Width = 145;
                toggleCheckButton.Click += delegate { ToggleAllApply(); };
                Controls.Add(toggleCheckButton);

                grid = new DataGridView();
                grid.Left = 12;
                grid.Top = 54;
                grid.Width = 1240;
                grid.Height = 484;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.RowHeadersVisible = false;
                grid.MultiSelect = true;
                grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
                grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
                {
                    if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name == ShowColumnName)
                    {
                        CutListItemInfo item = grid.Rows[e.RowIndex].Tag as CutListItemInfo;
                        ShowCutListItem(item);
                    }
                };
                grid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e)
                {
                    if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name != ApplyColumnName)
                        SetRowApply(e.RowIndex, true);
                };
                grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
                {
                    if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name != ApplyColumnName)
                        SetRowApply(e.RowIndex, true);
                };
                grid.CurrentCellDirtyStateChanged += delegate
                {
                    if (grid.IsCurrentCellDirty)
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                grid.EditingControlShowing += GridEditingControlShowing;
                Controls.Add(grid);

                statusLabel = new Label();
                statusLabel.Left = 12;
                statusLabel.Top = 548;
                statusLabel.Width = 980;
                statusLabel.Height = 38;
                statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;
                Controls.Add(statusLabel);

                Button applyButton = new Button();
                applyButton.Text = "Apply";
                applyButton.Left = 1142;
                applyButton.Top = 552;
                applyButton.Width = 110;
                applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                applyButton.Click += delegate { ApplyRowsSmart(); };
                Controls.Add(applyButton);
            }

            private void ShowEditMenu()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Edit";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(420, 118);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Button columns = new Button();
                    columns.Text = "Columns";
                    columns.Left = 18;
                    columns.Top = 34;
                    columns.Width = 116;
                    columns.Height = 34;
                    columns.Click += delegate { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
                    dialog.Controls.Add(columns);

                    Button bulk = new Button();
                    bulk.Text = "Bulk values";
                    bulk.Left = 151;
                    bulk.Top = 34;
                    bulk.Width = 116;
                    bulk.Height = 34;
                    bulk.Click += delegate { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                    dialog.Controls.Add(bulk);

                    Button clear = new Button();
                    clear.Text = "Clear property";
                    clear.Left = 284;
                    clear.Top = 34;
                    clear.Width = 116;
                    clear.Height = 34;
                    clear.Click += delegate { dialog.DialogResult = DialogResult.Retry; dialog.Close(); };
                    dialog.Controls.Add(clear);

                    DialogResult result = dialog.ShowDialog(this);
                    if (result == DialogResult.Yes) ShowColumnsMenu();
                    else if (result == DialogResult.No) ShowBulkEditDialog();
                    else if (result == DialogResult.Retry) ShowClearPropertyDialog();
                }
            }

            private void ShowColumnsMenu()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Columns";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(455, 120);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    Button choose = new Button();
                    choose.Text = "Choose";
                    choose.Left = 18;
                    choose.Top = 32;
                    choose.Width = 125;
                    choose.Click += delegate { dialog.DialogResult = DialogResult.Yes; dialog.Close(); };
                    dialog.Controls.Add(choose);

                    Button add = new Button();
                    add.Text = "Add property";
                    add.Left = 156;
                    add.Top = 32;
                    add.Width = 125;
                    add.Click += delegate { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                    dialog.Controls.Add(add);

                    Button clear = new Button();
                    clear.Text = "Clear property...";
                    clear.Left = 294;
                    clear.Top = 32;
                    clear.Width = 135;
                    clear.Click += delegate { dialog.DialogResult = DialogResult.Retry; dialog.Close(); };
                    dialog.Controls.Add(clear);

                    DialogResult result = dialog.ShowDialog(this);
                    if (result == DialogResult.Yes) ChooseColumns();
                    else if (result == DialogResult.No) AddNewPropertyColumn();
                    else if (result == DialogResult.Retry) ShowClearPropertyDialog();
                }
            }

            private void ShowBulkEditDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Bulk Edit";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(610, 250);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    ComboBox description = CreateValueCombo(dialog, "Description", 18, 42, 180, DescriptionPropertyName);
                    ComboBox brand = CreateValueCombo(dialog, "Brand", 214, 42, 150, BrandPropertyName);
                    ComboBox model = CreateValueCombo(dialog, "Model", 380, 42, 190, ModelPropertyName);

                    Label extraLabel = new Label();
                    extraLabel.Text = "Extra property";
                    extraLabel.Left = 18;
                    extraLabel.Top = 92;
                    extraLabel.Width = 170;
                    dialog.Controls.Add(extraLabel);

                    ComboBox extraProperty = new ComboBox();
                    extraProperty.Left = 18;
                    extraProperty.Top = 114;
                    extraProperty.Width = 180;
                    extraProperty.DropDownStyle = ComboBoxStyle.DropDown;
                    foreach (string name in allPropertyNames)
                        if (!IsRemovedOrderingColumnName(name)) extraProperty.Items.Add(name);
                    dialog.Controls.Add(extraProperty);

                    ComboBox extraValue = new ComboBox();
                    extraValue.Left = 214;
                    extraValue.Top = 114;
                    extraValue.Width = 356;
                    extraValue.DropDownStyle = ComboBoxStyle.DropDown;
                    dialog.Controls.Add(extraValue);

                    extraProperty.TextChanged += delegate
                    {
                        extraValue.Items.Clear();
                        foreach (string option in dropdownSettings.GetOptions(extraProperty.Text))
                            extraValue.Items.Add(option);
                    };

                    Label scopeLabel = new Label();
                    scopeLabel.Text = "Apply to";
                    scopeLabel.Left = 18;
                    scopeLabel.Top = 162;
                    scopeLabel.Width = 70;
                    dialog.Controls.Add(scopeLabel);

                    ComboBox scopeBox = new ComboBox();
                    scopeBox.Left = 92;
                    scopeBox.Top = 157;
                    scopeBox.Width = 160;
                    scopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    scopeBox.Items.AddRange(new object[] { "Checked rows", "All rows" });
                    scopeBox.SelectedIndex = 0;
                    dialog.Controls.Add(scopeBox);

                    Button apply = new Button();
                    apply.Text = "Set values";
                    apply.Left = 392;
                    apply.Top = 202;
                    apply.Width = 90;
                    apply.DialogResult = DialogResult.OK;
                    dialog.Controls.Add(apply);

                    Button cancel = new Button();
                    cancel.Text = "Cancel";
                    cancel.Left = 492;
                    cancel.Top = 202;
                    cancel.Width = 78;
                    cancel.DialogResult = DialogResult.Cancel;
                    dialog.Controls.Add(cancel);

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    AddBulkValue(values, DescriptionPropertyName, description.Text);
                    AddBulkValue(values, BrandPropertyName, brand.Text);
                    AddBulkValue(values, ModelPropertyName, model.Text);
                    if (!string.IsNullOrWhiteSpace(extraProperty.Text) && !string.IsNullOrWhiteSpace(extraValue.Text))
                        AddBulkValue(values, extraProperty.Text.Trim(), extraValue.Text.Trim());

                    if (values.Count == 0)
                        return;

                    SetBulkPropertyValues(scopeBox.SelectedIndex == 1, values);
                }
            }

            private ComboBox CreateValueCombo(Form dialog, string caption, int left, int top, int width, string propertyName)
            {
                Label label = new Label();
                label.Text = caption;
                label.Left = left;
                label.Top = top - 22;
                label.Width = width;
                dialog.Controls.Add(label);

                ComboBox combo = new ComboBox();
                combo.Left = left;
                combo.Top = top;
                combo.Width = width;
                combo.DropDownStyle = ComboBoxStyle.DropDown;
                foreach (string option in dropdownSettings.GetOptions(propertyName))
                    combo.Items.Add(option);
                dialog.Controls.Add(combo);
                return combo;
            }

            private void ShowClearPropertyDialog()
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Clear Property";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.ClientSize = new Size(390, 160);
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;

                    ComboBox propertyBox = new ComboBox();
                    propertyBox.Left = 24;
                    propertyBox.Top = 28;
                    propertyBox.Width = 340;
                    propertyBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    foreach (string name in visiblePropertyNames) propertyBox.Items.Add(name);
                    if (propertyBox.Items.Count > 0) propertyBox.SelectedIndex = 0;
                    dialog.Controls.Add(propertyBox);

                    Button checkedButton = new Button();
                    checkedButton.Text = "Checked Only";
                    checkedButton.Left = 70;
                    checkedButton.Top = 88;
                    checkedButton.Width = 110;
                    checkedButton.Click += delegate
                    {
                        ClearPropertyByName(Convert.ToString(propertyBox.SelectedItem), false);
                        dialog.Close();
                    };
                    dialog.Controls.Add(checkedButton);

                    Button allButton = new Button();
                    allButton.Text = "All";
                    allButton.Left = 198;
                    allButton.Top = 88;
                    allButton.Width = 90;
                    allButton.Click += delegate
                    {
                        ClearPropertyByName(Convert.ToString(propertyBox.SelectedItem), true);
                        dialog.Close();
                    };
                    dialog.Controls.Add(allButton);
                }
            }

            private void ClearPropertyByName(string propertyName, bool allRows)
            {
                if (string.IsNullOrWhiteSpace(propertyName) || !grid.Columns.Contains(propertyName))
                    return;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    if (!allRows && !IsRowChecked(row)) continue;
                    row.Cells[propertyName].Value = string.Empty;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Ready to clear " + propertyName;
                }
                statusLabel.Text = "Clear " + propertyName + " is ready. Click Apply to write to SOLIDWORKS.";
            }

            private void LoadCutListData()
            {
                items.Clear();
                allPropertyNames.Clear();

                List<string> messages = new List<string>();
                items.AddRange(GetCutListItems(modelDoc, messages));

                AddPropertyName(DescriptionPropertyName);
                AddPropertyName(BrandPropertyName);
                AddPropertyName(ModelPropertyName);

                foreach (CutListItemInfo item in items)
                {
                    foreach (KeyValuePair<string, string> pair in item.Properties)
                    {
                        if (IsRemovedOrderingColumnName(pair.Key))
                            continue;

                        AddPropertyName(pair.Key);
                        dropdownSettings.LearnOption(pair.Key, pair.Value);
                    }
                }

                if (visiblePropertyNames.Count == 0)
                {
                    List<string> saved = dropdownSettings.GetVisibleColumns("CutList");
                    if (saved.Count > 0)
                    {
                        foreach (string name in saved)
                        {
                            if (IsRemovedOrderingColumnName(name))
                                continue;

                            if (ContainsIgnoreCase(allPropertyNames, name) && !ContainsIgnoreCase(visiblePropertyNames, name))
                                visiblePropertyNames.Add(name);
                        }
                    }
                }

                if (visiblePropertyNames.Count == 0)
                {
                    visiblePropertyNames.Add(DescriptionPropertyName);
                    visiblePropertyNames.Add(BrandPropertyName);
                    visiblePropertyNames.Add(ModelPropertyName);
                }

            }

            private void BuildGrid()
            {
                grid.Columns.Clear();
                grid.Rows.Clear();

                DataGridViewCheckBoxColumn applyColumn = new DataGridViewCheckBoxColumn();
                applyColumn.Name = ApplyColumnName;
                applyColumn.HeaderText = "Apply";
                applyColumn.Width = 55;
                grid.Columns.Add(applyColumn);

                DataGridViewTextBoxColumn itemColumn = new DataGridViewTextBoxColumn();
                itemColumn.Name = ItemColumnName;
                itemColumn.HeaderText = "Cut-list item name";
                itemColumn.Width = 230;
                itemColumn.ReadOnly = false;
                grid.Columns.Add(itemColumn);

                DataGridViewButtonColumn showColumn = new DataGridViewButtonColumn();
                showColumn.Name = ShowColumnName;
                showColumn.HeaderText = "Show";
                showColumn.Text = "Show";
                showColumn.UseColumnTextForButtonValue = true;
                showColumn.Width = 70;
                grid.Columns.Add(showColumn);

                foreach (string propertyName in visiblePropertyNames)
                    AddPropertyGridColumn(propertyName);

                DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
                statusColumn.Name = StatusColumnName;
                statusColumn.HeaderText = "Status";
                statusColumn.Width = 220;
                statusColumn.ReadOnly = true;
                grid.Columns.Add(statusColumn);

                foreach (CutListItemInfo item in items)
                {
                    int rowIndex = grid.Rows.Add();
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = item;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[ItemColumnName].Value = item.FeatureName;
                    row.Cells[StatusColumnName].Value = "Ready";

                    foreach (string propertyName in visiblePropertyNames)
                    {
                        string value;
                        item.Properties.TryGetValue(propertyName, out value);
                        row.Cells[propertyName].Value = value ?? string.Empty;
                    }
                }

                statusLabel.Text = items.Count + " cut-list item(s).";
            }

            private void AddPropertyGridColumn(string propertyName)
            {
                if (IsDropdownProperty(propertyName))
                {
                    DataGridViewComboBoxColumn comboColumn = new DataGridViewComboBoxColumn();
                    comboColumn.Name = propertyName;
                    comboColumn.HeaderText = propertyName;
                    comboColumn.Width = GetColumnWidth(propertyName);
                    comboColumn.FlatStyle = FlatStyle.Standard;
                    comboColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox;
                    comboColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

                    foreach (string option in dropdownSettings.GetOptions(propertyName))
                    {
                        if (!comboColumn.Items.Contains(option))
                            comboColumn.Items.Add(option);
                    }

                    foreach (CutListItemInfo item in items)
                    {
                        string value;
                        if (item.Properties.TryGetValue(propertyName, out value) && !string.IsNullOrWhiteSpace(value) && !comboColumn.Items.Contains(value))
                            comboColumn.Items.Add(value);
                    }

                    grid.Columns.Add(comboColumn);
                    return;
                }

                DataGridViewTextBoxColumn textColumn = new DataGridViewTextBoxColumn();
                textColumn.Name = propertyName;
                textColumn.HeaderText = propertyName;
                textColumn.Width = GetColumnWidth(propertyName);
                textColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
                grid.Columns.Add(textColumn);
            }

            private int GetColumnWidth(string propertyName)
            {
                if (string.Equals(propertyName, DescriptionPropertyName, StringComparison.OrdinalIgnoreCase)) return 180;
                if (string.Equals(propertyName, BrandPropertyName, StringComparison.OrdinalIgnoreCase)) return 115;
                if (string.Equals(propertyName, ModelPropertyName, StringComparison.OrdinalIgnoreCase)) return 135;
                return 140;
            }

            private void GridEditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                ComboBox comboBox = e.Control as ComboBox;
                if (comboBox == null)
                    return;

                comboBox.DropDownStyle = ComboBoxStyle.DropDown;
                comboBox.Validating -= ComboBoxValidating;
                comboBox.Validating += ComboBoxValidating;
            }

            private void ComboBoxValidating(object sender, System.ComponentModel.CancelEventArgs e)
            {
                ComboBox comboBox = sender as ComboBox;
                if (comboBox == null || grid.CurrentCell == null)
                    return;

                string text = comboBox.Text == null ? string.Empty : comboBox.Text.Trim();
                if (text.Length == 0)
                    return;

                string propertyName = grid.Columns[grid.CurrentCell.ColumnIndex].Name;
                if (!comboBox.Items.Contains(text))
                    comboBox.Items.Add(text);

                DataGridViewComboBoxColumn column = grid.Columns[propertyName] as DataGridViewComboBoxColumn;
                if (column != null && !column.Items.Contains(text))
                    column.Items.Add(text);

                dropdownSettings.AddOption(propertyName, text);
                dropdownSettings.Save();
            }

            private bool IsDropdownProperty(string propertyName)
            {
                return string.Equals(propertyName, DescriptionPropertyName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(propertyName, BrandPropertyName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(propertyName, ModelPropertyName, StringComparison.OrdinalIgnoreCase);
            }
private bool IsEditablePropertyColumn(string columnName)
            {
                if (string.IsNullOrWhiteSpace(columnName))
                    return false;

                if (string.Equals(columnName, ApplyColumnName, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(columnName, ItemColumnName, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(columnName, ShowColumnName, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(columnName, StatusColumnName, StringComparison.OrdinalIgnoreCase)) return false;

                return ContainsIgnoreCase(visiblePropertyNames, columnName);
            }
private void CommitGridEdits()
            {
                try
                {
                    if (grid != null)
                    {
                        if (grid.IsCurrentCellInEditMode)
                            grid.EndEdit();

                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    }
                }
                catch
                {
                    // A failed commit should not crash the editor. The apply routine will still
                    // use whatever value is already committed to the cell.
                }
            }
private void ApplyRowsSmart()
            {
                RowTargetScope scope = ResolveRowTargetScope("Apply");
                if (scope == RowTargetScope.Cancelled)
                    return;
                ApplyRows(scope == RowTargetScope.AllRows);
            }

            private RowTargetScope ResolveRowTargetScope(string operationName)
            {
                int totalRows = CountRealRows();
                if (totalRows == 0)
                    return RowTargetScope.Cancelled;

                int checkedRows = CountCheckedRows();

                // If every row is already checked, Checked Only and All are equivalent.
                // Do not interrupt the user with a choice dialog in this case.
                if (checkedRows == totalRows)
                    return RowTargetScope.CheckedRows;

                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - " + operationName;
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;
                    dialog.ClientSize = new Size(360, 142);

                    Label messageLabel = new Label();
                    messageLabel.Left = 14;
                    messageLabel.Top = 14;
                    messageLabel.Width = 330;
                    messageLabel.Height = 46;
                    messageLabel.Text = operationName + " will affect which rows?";
                    dialog.Controls.Add(messageLabel);

                    RowTargetScope selectedScope = RowTargetScope.Cancelled;

                    Button allButton = new Button();
                    allButton.Text = "All";
                    allButton.Left = 18;
                    allButton.Top = 82;
                    allButton.Width = 96;
                    allButton.DialogResult = DialogResult.OK;
                    allButton.Click += delegate { selectedScope = RowTargetScope.AllRows; };
                    dialog.Controls.Add(allButton);

                    Button checkedButton = new Button();
                    checkedButton.Text = "Checked Only";
                    checkedButton.Left = 126;
                    checkedButton.Top = 82;
                    checkedButton.Width = 116;
                    checkedButton.Enabled = checkedRows > 0;
                    checkedButton.DialogResult = DialogResult.OK;
                    checkedButton.Click += delegate { selectedScope = RowTargetScope.CheckedRows; };
                    dialog.Controls.Add(checkedButton);

                    Button cancelButton = new Button();
                    cancelButton.Text = "Cancel";
                    cancelButton.Left = 254;
                    cancelButton.Top = 82;
                    cancelButton.Width = 88;
                    cancelButton.DialogResult = DialogResult.Cancel;
                    cancelButton.Click += delegate { selectedScope = RowTargetScope.Cancelled; };
                    dialog.Controls.Add(cancelButton);

                    dialog.AcceptButton = allButton;
                    dialog.CancelButton = cancelButton;

                    dialog.ShowDialog(this);
                    return selectedScope;
                }
            }

            private int CountRealRows()
            {
                int count = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow)
                        count++;
                }
                return count;
            }

            private int CountCheckedRows()
            {
                int count = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow && IsRowChecked(row))
                        count++;
                }
                return count;
            }

            private void ToggleAllApply()
            {
                GridCheckBehavior.ToggleSelectedThenAll(
                    grid,
                    IsRowChecked,
                    (row, value) => row.Cells[ApplyColumnName].Value = value);
            }
private void AddBulkValue(Dictionary<string, string> values, string propertyName, string value)
            {
                if (values == null)
                    return;

                string cleanName = propertyName == null ? string.Empty : propertyName.Trim();
                string cleanValue = value == null ? string.Empty : value.Trim();

                if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(cleanValue))
                    return;

                if (IsRemovedOrderingColumnName(cleanName))
                    return;

                if (values.ContainsKey(cleanName))
                    values[cleanName] = cleanValue;
                else
                    values.Add(cleanName, cleanValue);
            }

            private void SetBulkPropertyValues(bool allRows, Dictionary<string, string> values)
            {
                CommitGridEdits();

                if (values == null || values.Count == 0)
                    return;

                foreach (KeyValuePair<string, string> pair in values)
                    EnsurePropertyColumnVisibleInGrid(pair.Key);

                int editedRows = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;

                    foreach (KeyValuePair<string, string> pair in values)
                    {
                        if (!grid.Columns.Contains(pair.Key))
                            continue;

                        DataGridViewComboBoxColumn comboColumn = grid.Columns[pair.Key] as DataGridViewComboBoxColumn;
                        if (comboColumn != null && !comboColumn.Items.Contains(pair.Value))
                            comboColumn.Items.Add(pair.Value);

                        row.Cells[pair.Key].Value = pair.Value;
                        dropdownSettings.AddOption(pair.Key, pair.Value);
                    }

                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Edited " + values.Count + " bulk value" + (values.Count == 1 ? string.Empty : "s");
                    editedRows++;
                }

                dropdownSettings.SetVisibleColumns("CutList", visiblePropertyNames);
                dropdownSettings.Save();
                statusLabel.Text = "Set " + values.Count + " bulk value" + (values.Count == 1 ? string.Empty : "s") + " on " + editedRows + " row(s). Click Apply to write to SOLIDWORKS.";
            }

            private void EnsurePropertyColumnVisibleInGrid(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;

                propertyName = propertyName.Trim();
                if (IsRemovedOrderingColumnName(propertyName))
                    return;

                AddPropertyName(propertyName);
                if (!ContainsIgnoreCase(visiblePropertyNames, propertyName))
                    visiblePropertyNames.Add(propertyName);

                if (grid.Columns.Contains(propertyName))
                    return;

                DataGridViewColumn statusColumn = grid.Columns[StatusColumnName];
                AddPropertyGridColumn(propertyName);
                DataGridViewColumn newColumn = grid.Columns[propertyName];
                if (newColumn != null && statusColumn != null)
                    newColumn.DisplayIndex = statusColumn.DisplayIndex;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    CutListItemInfo item = row.Tag as CutListItemInfo;
                    string currentValue = string.Empty;
                    if (item != null && item.Properties != null)
                        item.Properties.TryGetValue(propertyName, out currentValue);
                    row.Cells[propertyName].Value = currentValue ?? string.Empty;
                }
            }
private void ApplyRows(bool allRows)
            {
                CommitGridEdits();
                CabinCustomPropertyStore.EnsureCanWrite(modelDoc);

                int updated = 0;
                int skipped = 0;
                int failed = 0;
                StringBuilder report = new StringBuilder();
                report.AppendLine("Cabin Tools - Cut-List Property Editor Report");
                report.AppendLine("Document: " + (modelDoc.GetPathName() ?? modelDoc.GetTitle()));
                report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine();

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    if (!allRows && !IsRowChecked(row))
                    {
                        skipped++;
                        row.Cells[StatusColumnName].Value = "Skipped";
                        continue;
                    }

                    CutListItemInfo item = row.Tag as CutListItemInfo;
                    if (item == null || item.PropertyManager == null)
                    {
                        failed++;
                        row.Cells[StatusColumnName].Value = "! No property manager";
                        continue;
                    }

                    try
                    {
                        int writes = 0;

                        string requestedFeatureName = row.Cells[ItemColumnName].Value == null ? string.Empty : Convert.ToString(row.Cells[ItemColumnName].Value).Trim();
                        if (!string.IsNullOrWhiteSpace(requestedFeatureName) &&
                            !string.Equals(requestedFeatureName, item.FeatureName, StringComparison.Ordinal))
                        {
                            item.Feature.Name = requestedFeatureName;
                            item.FeatureName = requestedFeatureName;
                            writes++;
                        }

                        foreach (string propertyName in visiblePropertyNames)
                        {
                            object cellValueObject = row.Cells[propertyName].Value;
                            string cellValue = cellValueObject == null ? string.Empty : Convert.ToString(cellValueObject).Trim();

                            if (string.IsNullOrWhiteSpace(cellValue))
                                continue;

                            SetTextProperty(item.PropertyManager, propertyName, cellValue);

                            string valueAfterWrite = ReadTextProperty(item.PropertyManager, propertyName);
                            if (!string.Equals(valueAfterWrite, cellValue, StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    "Property '" + propertyName + "' did not keep the written value. " +
                                    "Wanted '" + cellValue + "', but SOLIDWORKS currently reads '" + valueAfterWrite + "'. " +
                                    "The property may still be linked or controlled by the weldment profile/cut-list update.");
                            }

                            if (!item.Properties.ContainsKey(propertyName))
                                item.Properties.Add(propertyName, cellValue);
                            else
                                item.Properties[propertyName] = cellValue;

                            dropdownSettings.LearnOption(propertyName, cellValue);
                            writes++;
                        }

                        if (writes == 0)
                        {
                            skipped++;
                            row.Cells[StatusColumnName].Value = "Skipped - no non-empty values";
                        }
                        else
                        {
                            updated++;
                            row.Cells[StatusColumnName].Value = "Updated " + writes + " propert" + (writes == 1 ? "y" : "ies");
                            report.AppendLine(item.FeatureName + ": updated " + writes + " property value(s).");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        row.Cells[StatusColumnName].Value = "! " + ex.Message;
                        report.AppendLine(item.FeatureName + ": FAILED - " + ex.Message);
                    }
                }

                dropdownSettings.SetVisibleColumns("CutList", visiblePropertyNames);
                dropdownSettings.Save();
                ForceRebuild();

                report.AppendLine();
                report.AppendLine("Updated rows: " + updated);
                report.AppendLine("Skipped rows: " + skipped);
                report.AppendLine("Failed rows: " + failed);
                string path = WriteReport(modelDoc, report.ToString());
                statusLabel.Text = "Updated " + updated + " row(s), skipped " + skipped + ", failed " + failed + ". Report: " + path;
            }

            public void RefreshFromSolidWorks()
            {
                LoadCutListData();
                BuildGrid();
            }

            private void ShowSelectedCutListItem()
            {
                if (grid.CurrentRow == null)
                    return;

                CutListItemInfo item = grid.CurrentRow.Tag as CutListItemInfo;
                ShowCutListItem(item);
            }

            private void ShowCutListItem(CutListItemInfo item)
            {
                if (item == null)
                    return;

                try
                {
                    modelDoc.ClearSelection2(true);
                    int selected = SelectBodiesForItem(item);
                    if (selected == 0 && item.Feature != null)
                    {
                        try { item.Feature.Select2(false, -1); selected = 1; }
                        catch { }
                    }

                    try { modelDoc.ViewZoomToSelection(); }
                    catch { }

                    statusLabel.Text = selected > 0 ? "Selected " + item.FeatureName + " in SOLIDWORKS." : "Could not select bodies for " + item.FeatureName + ".";
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "Show failed: " + ex.Message;
                }
            }

            private int SelectBodiesForItem(CutListItemInfo item)
            {
                if (item == null || item.Feature == null)
                    return 0;

                object specificFeature = null;
                try { specificFeature = item.Feature.GetSpecificFeature2(); }
                catch { specificFeature = null; }

                if (specificFeature == null)
                    return 0;

                object bodiesObject = null;
                try
                {
                    bodiesObject = specificFeature.GetType().InvokeMember(
                        "GetBodies",
                        BindingFlags.InvokeMethod,
                        null,
                        specificFeature,
                        null);
                }
                catch
                {
                    bodiesObject = null;
                }

                Array bodiesArray = bodiesObject as Array;
                if (bodiesArray == null)
                    return 0;

                int selected = 0;
                foreach (object body in bodiesArray)
                {
                    if (body == null)
                        continue;

                    bool append = selected > 0;
                    bool ok = TrySelectBody(body, append);
                    if (ok)
                        selected++;
                }

                return selected;
            }

            private bool TrySelectBody(object body, bool append)
            {
                if (body == null)
                    return false;

                try
                {
                    object result = body.GetType().InvokeMember(
                        "Select2",
                        BindingFlags.InvokeMethod,
                        null,
                        body,
                        new object[] { append, null });

                    if (result is bool)
                        return (bool)result;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private void ForceRebuild()
            {
                try { modelDoc.ForceRebuild3(false); }
                catch { }
            }

            private bool IsRowChecked(DataGridViewRow row)
            {
                object value = row.Cells[ApplyColumnName].Value;
                return value is bool && (bool)value;
            }

            private void SetRowApply(int rowIndex, bool value)
            {
                if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
                    return;
                grid.Rows[rowIndex].Cells[ApplyColumnName].Value = value;
            }

            private List<DataGridViewRow> GetSelectedGridRows()
            {
                List<DataGridViewRow> selectedRows = new List<DataGridViewRow>();

                if (grid == null)
                    return selectedRows;

                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                foreach (DataGridViewCell cell in grid.SelectedCells)
                {
                    if (cell == null || cell.RowIndex < 0 || cell.RowIndex >= grid.Rows.Count)
                        continue;

                    DataGridViewRow row = grid.Rows[cell.RowIndex];
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                return selectedRows;
            }

            private void SetAllApply(bool value)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow)
                        row.Cells[ApplyColumnName].Value = value;
                }
            }

            private bool IsRemovedOrderingColumnName(string propertyName)
            {
                return string.Equals(propertyName, "Order", StringComparison.OrdinalIgnoreCase);
            }

            private void AddPropertyName(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;

                if (IsRemovedOrderingColumnName(propertyName))
                    return;

                if (!ContainsIgnoreCase(allPropertyNames, propertyName))
                    allPropertyNames.Add(propertyName.Trim());
            }

            private void ChooseColumns()
            {
                using (ColumnSelectorForm form = new ColumnSelectorForm(allPropertyNames, visiblePropertyNames))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                        return;

                    visiblePropertyNames.Clear();
                    foreach (string name in form.SelectedColumns)
                        visiblePropertyNames.Add(name);

                    dropdownSettings.SetVisibleColumns("CutList", visiblePropertyNames);
                    dropdownSettings.Save();
                    BuildGrid();
                }
            }

            private void AddNewPropertyColumn()
            {
                string propertyName = PromptForText("New cut-list property column", "Property name:", string.Empty);
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;

                propertyName = propertyName.Trim();
                if (IsRemovedOrderingColumnName(propertyName))
                {
                    statusLabel.Text = "Ordering was removed. The Order column is no longer supported by Cabin Tools.";
                    return;
                }

                AddPropertyName(propertyName);
                if (!ContainsIgnoreCase(visiblePropertyNames, propertyName))
                    visiblePropertyNames.Add(propertyName);

                dropdownSettings.SetVisibleColumns("CutList", visiblePropertyNames);
                dropdownSettings.Save();
                BuildGrid();
            }
        }

        private sealed class BulkPropertyInputRow : IDisposable
        {
            private readonly Panel parentPanel;
            private readonly ComboBox propertyComboBox;
            private readonly ComboBox valueComboBox;
            private readonly Button removeButton;

            public event EventHandler RemoveRequested;

            public BulkPropertyInputRow(Panel parentPanel, int index, IList<string> propertyNames, PropertyDropdownSettings settings)
            {
                this.parentPanel = parentPanel;

                propertyComboBox = new ComboBox();
                propertyComboBox.Width = 175;
                propertyComboBox.DropDownStyle = ComboBoxStyle.DropDown;
                propertyComboBox.SelectedIndexChanged += delegate { ReloadValueOptions(settings); };
                propertyComboBox.Validating += delegate { ReloadValueOptions(settings); };
                parentPanel.Controls.Add(propertyComboBox);

                valueComboBox = new ComboBox();
                valueComboBox.Width = 225;
                valueComboBox.DropDownStyle = ComboBoxStyle.DropDown;
                parentPanel.Controls.Add(valueComboBox);

                removeButton = new Button();
                removeButton.Text = "Remove";
                removeButton.Width = 75;
                removeButton.Click += delegate
                {
                    EventHandler handler = RemoveRequested;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                };
                parentPanel.Controls.Add(removeButton);

                ReloadPropertyOptions(propertyNames);
                SetIndex(index);
            }

            public string PropertyName
            {
                get { return propertyComboBox.Text == null ? string.Empty : propertyComboBox.Text.Trim(); }
            }

            public string Value
            {
                get { return valueComboBox.Text == null ? string.Empty : valueComboBox.Text.Trim(); }
            }

            public void SetIndex(int index)
            {
                int top = 4 + (index * 28);
                propertyComboBox.Left = 130;
                propertyComboBox.Top = top;
                valueComboBox.Left = 315;
                valueComboBox.Top = top;
                removeButton.Left = 550;
                removeButton.Top = top - 1;
            }

            public void ReloadPropertyOptions(IList<string> propertyNames)
            {
                string current = propertyComboBox.Text ?? string.Empty;
                propertyComboBox.Items.Clear();
                if (propertyNames != null)
                {
                    foreach (string propertyName in propertyNames)
                    {
                        if (!string.IsNullOrWhiteSpace(propertyName) && !propertyComboBox.Items.Contains(propertyName))
                            propertyComboBox.Items.Add(propertyName);
                    }
                }
                propertyComboBox.Text = current;
            }

            public void ReloadValueOptions(PropertyDropdownSettings settings)
            {
                if (settings == null)
                    return;

                string current = valueComboBox.Text ?? string.Empty;
                valueComboBox.Items.Clear();
                foreach (string option in settings.GetOptions(PropertyName))
                {
                    if (!valueComboBox.Items.Contains(option))
                        valueComboBox.Items.Add(option);
                }
                valueComboBox.Text = current;
            }

            public void Dispose()
            {
                if (parentPanel != null)
                {
                    parentPanel.Controls.Remove(propertyComboBox);
                    parentPanel.Controls.Remove(valueComboBox);
                    parentPanel.Controls.Remove(removeButton);
                }

                propertyComboBox.Dispose();
                valueComboBox.Dispose();
                removeButton.Dispose();
            }
        }

        internal sealed class ColumnSelectorForm : Form
        {
            private readonly CheckedListBox list = new CheckedListBox();
            public List<string> SelectedColumns = new List<string>();

            public ColumnSelectorForm(IList<string> allColumns, IList<string> visibleColumns)
            {
                Text = "Choose property columns";
                Width = 420;
                Height = 520;
                StartPosition = FormStartPosition.CenterParent;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                list.Left = 12;
                list.Top = 12;
                list.Width = 380;
                list.Height = 410;
                list.CheckOnClick = true;
                Controls.Add(list);

                foreach (string column in allColumns)
                {
                    int index = list.Items.Add(column);
                    list.SetItemChecked(index, ContainsIgnoreCase(visibleColumns, column));
                }

                Button ok = new Button();
                ok.Text = "OK";
                ok.Left = 215;
                ok.Top = 432;
                ok.Width = 80;
                ok.DialogResult = DialogResult.OK;
                ok.Click += delegate
                {
                    SelectedColumns.Clear();
                    foreach (object item in list.CheckedItems)
                        SelectedColumns.Add(Convert.ToString(item));
                };
                Controls.Add(ok);

                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.Left = 305;
                cancel.Top = 432;
                cancel.Width = 80;
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);

                AcceptButton = ok;
                CancelButton = cancel;
            }
        }

        internal sealed class PropertyDropdownSettings
        {
            private readonly Dictionary<string, List<string>> options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> visibleColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            private static string SettingsDirectory
            {
                get { return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData), "CabinTools", "Settings"); }
            }

            private static string SettingsPath
            {
                get { return Path.Combine(SettingsDirectory, "PropertyDropdownOptions.txt"); }
            }

            public static PropertyDropdownSettings Load()
            {
                PropertyDropdownSettings settings = new PropertyDropdownSettings();
                settings.AddDefaults();

                try
                {
                    if (!File.Exists(SettingsPath))
                        return settings;

                    foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        int equalsIndex = line.IndexOf('=');
                        if (equalsIndex <= 0)
                            continue;

                        string key = line.Substring(0, equalsIndex).Trim();
                        string valuesText = line.Substring(equalsIndex + 1);
                        string[] values = valuesText.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

                        if (key.StartsWith("Visible.", StringComparison.OrdinalIgnoreCase))
                        {
                            string scope = key.Substring("Visible.".Length);
                            foreach (string value in values)
                                settings.AddVisibleColumn(scope, Decode(value));
                        }
                        else
                        {
                            foreach (string value in values)
                                settings.AddOption(key, Decode(value));
                        }
                    }
                }
                catch
                {
                }

                settings.AddDefaults();
                return settings;
            }

            public void Save()
            {
                try
                {
                    Directory.CreateDirectory(SettingsDirectory);
                    StringBuilder builder = new StringBuilder();

                    foreach (KeyValuePair<string, List<string>> pair in options)
                        builder.AppendLine(pair.Key + "=" + EncodeList(pair.Value));

                    foreach (KeyValuePair<string, List<string>> pair in visibleColumns)
                        builder.AppendLine("Visible." + pair.Key + "=" + EncodeList(pair.Value));

                    File.WriteAllText(SettingsPath, builder.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }

            public List<string> GetOptions(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    return new List<string>();

                List<string> list;
                if (options.TryGetValue(propertyName, out list))
                    return new List<string>(list);

                return new List<string>();
            }

            public void LearnOption(string propertyName, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                AddOption(propertyName, value.Trim());
            }

            public void AddOption(string propertyName, string value)
            {
                if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
                    return;

                List<string> list;
                if (!options.TryGetValue(propertyName, out list))
                {
                    list = new List<string>();
                    options[propertyName] = list;
                }

                if (!ContainsIgnoreCase(list, value))
                    list.Add(value.Trim());
            }

            public List<string> GetVisibleColumns(string scope)
            {
                List<string> list;
                if (visibleColumns.TryGetValue(scope, out list))
                    return new List<string>(list);
                return new List<string>();
            }

            public void SetVisibleColumns(string scope, IList<string> columns)
            {
                visibleColumns[scope] = new List<string>();
                if (columns == null)
                    return;
                foreach (string column in columns)
                    AddVisibleColumn(scope, column);
            }

            private void AddVisibleColumn(string scope, string column)
            {
                if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(column))
                    return;

                List<string> list;
                if (!visibleColumns.TryGetValue(scope, out list))
                {
                    list = new List<string>();
                    visibleColumns[scope] = list;
                }
                if (!ContainsIgnoreCase(list, column))
                    list.Add(column.Trim());
            }

            private void AddDefaults()
            {
                AddOption(DescriptionPropertyName, "Top Profile");
                AddOption(DescriptionPropertyName, "Bottom Profile");
                AddOption(DescriptionPropertyName, "Ceiling Profile");
                AddOption(DescriptionPropertyName, "Decorative Profile");

                AddOption(BrandPropertyName, "SBA");
                AddOption(BrandPropertyName, "Banco");

                AddOption(ModelPropertyName, "Type 121");
                AddOption(ModelPropertyName, "TPL 27");
                AddOption(ModelPropertyName, "TPL 28");
                AddOption(ModelPropertyName, "TDB 22");
                AddOption(ModelPropertyName, "TDB 23");
            }

            private static string EncodeList(IList<string> values)
            {
                List<string> encoded = new List<string>();
                foreach (string value in values)
                    encoded.Add(Encode(value));
                return string.Join("|", encoded.ToArray());
            }

            private static string Encode(string value)
            {
                return (value ?? string.Empty).Replace("%", "%25").Replace("|", "%7C").Replace("=", "%3D");
            }

            private static string Decode(string value)
            {
                return (value ?? string.Empty).Replace("%3D", "=").Replace("%7C", "|").Replace("%25", "%");
            }
        }

        internal static string PromptForText(string title, string label, string defaultValue)
        {
            using (Form form = new Form())
            using (Label labelControl = new Label())
            using (TextBox textBox = new TextBox())
            using (Button okButton = new Button())
            using (Button cancelButton = new Button())
            {
                form.Text = title;
                form.Width = 430;
                form.Height = 150;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                labelControl.Text = label;
                labelControl.Left = 12;
                labelControl.Top = 15;
                labelControl.Width = 390;
                form.Controls.Add(labelControl);

                textBox.Left = 12;
                textBox.Top = 40;
                textBox.Width = 390;
                textBox.Text = defaultValue ?? string.Empty;
                form.Controls.Add(textBox);

                okButton.Text = "OK";
                okButton.Left = 225;
                okButton.Top = 75;
                okButton.Width = 80;
                okButton.DialogResult = DialogResult.OK;
                form.Controls.Add(okButton);

                cancelButton.Text = "Cancel";
                cancelButton.Left = 315;
                cancelButton.Top = 75;
                cancelButton.Width = 80;
                cancelButton.DialogResult = DialogResult.Cancel;
                form.Controls.Add(cancelButton);

                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : string.Empty;
            }
        }
    }
}
