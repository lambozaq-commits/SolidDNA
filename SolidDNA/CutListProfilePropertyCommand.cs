using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment = CADBooster.SolidDna.SolidWorksEnvironment;

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
    /// - Description, Brand, and Model have editable dropdown presets.
    /// - New dropdown values typed by the user are saved for future sessions.
    /// </summary>
    internal static class CutListProfilePropertyCommand
    {
        internal const string DescriptionPropertyName = "Description";
        internal const string BrandPropertyName = "Brand";
        internal const string ModelPropertyName = "Model";

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

                using (CutListPropertyEditorForm form = new CutListPropertyEditorForm(modelDoc))
                {
                    form.ShowDialog();
                }
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

            propertyManager.Add3(
                propertyName.Trim(),
                (int)swCustomInfoType_e.swCustomInfoText,
                value ?? string.Empty,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        internal static string ReadTextProperty(ICustomPropertyManager propertyManager, string propertyName)
        {
            if (propertyManager == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            string rawValue;
            string resolvedValue;
            bool wasResolved;
            bool linked;

            try
            {
                int result = propertyManager.Get6(
                    propertyName,
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
                SwEnvironment.Application.ShowMessageBox(
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
            private ComboBox propertySelector;
            private ComboBox valueSelector;
            private Label statusLabel;

            private const string ApplyColumnName = "__Apply";
            private const string ItemColumnName = "__Item";
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
                MinimumSize = new Size(900, 520);

                Label helpLabel = new Label();
                helpLabel.Text = "Choose which cut-list properties are shown as columns. Empty cells are not written. Use Clear Column to intentionally blank a property.";
                helpLabel.Left = 12;
                helpLabel.Top = 12;
                helpLabel.Width = 1180;
                helpLabel.Height = 24;
                Controls.Add(helpLabel);

                Button chooseColumnsButton = new Button();
                chooseColumnsButton.Text = "Choose columns";
                chooseColumnsButton.Left = 12;
                chooseColumnsButton.Top = 42;
                chooseColumnsButton.Width = 135;
                chooseColumnsButton.Click += delegate { ChooseColumns(); };
                Controls.Add(chooseColumnsButton);

                Button addColumnButton = new Button();
                addColumnButton.Text = "Add property column";
                addColumnButton.Left = 155;
                addColumnButton.Top = 42;
                addColumnButton.Width = 145;
                addColumnButton.Click += delegate { AddNewPropertyColumn(); };
                Controls.Add(addColumnButton);

                Label propertyLabel = new Label();
                propertyLabel.Text = "Property:";
                propertyLabel.Left = 320;
                propertyLabel.Top = 47;
                propertyLabel.Width = 60;
                Controls.Add(propertyLabel);

                propertySelector = new ComboBox();
                propertySelector.Left = 385;
                propertySelector.Top = 42;
                propertySelector.Width = 160;
                propertySelector.DropDownStyle = ComboBoxStyle.DropDownList;
                propertySelector.SelectedIndexChanged += delegate { UpdateValueSelectorOptions(); };
                Controls.Add(propertySelector);

                Label valueLabel = new Label();
                valueLabel.Text = "Value:";
                valueLabel.Left = 555;
                valueLabel.Top = 47;
                valueLabel.Width = 45;
                Controls.Add(valueLabel);

                valueSelector = new ComboBox();
                valueSelector.Left = 605;
                valueSelector.Top = 42;
                valueSelector.Width = 205;
                valueSelector.DropDownStyle = ComboBoxStyle.DropDown;
                Controls.Add(valueSelector);

                Button setValueButton = new Button();
                setValueButton.Text = "Set value";
                setValueButton.Left = 820;
                setValueButton.Top = 42;
                setValueButton.Width = 105;
                setValueButton.Click += delegate { SetSelectedPropertyValueSmart(); };
                Controls.Add(setValueButton);

                Button clearColumnButton = new Button();
                clearColumnButton.Text = "Clear column";
                clearColumnButton.Left = 12;
                clearColumnButton.Top = 74;
                clearColumnButton.Width = 115;
                clearColumnButton.Click += delegate { ClearSelectedColumnSmart(); };
                Controls.Add(clearColumnButton);

                Button toggleCheckButton = new Button();
                toggleCheckButton.Text = "Check / uncheck all";
                toggleCheckButton.Left = 135;
                toggleCheckButton.Top = 74;
                toggleCheckButton.Width = 140;
                toggleCheckButton.Click += delegate { ToggleAllApply(); };
                Controls.Add(toggleCheckButton);

                Button refreshButton = new Button();
                refreshButton.Text = "Refresh cut-list items";
                refreshButton.Left = 285;
                refreshButton.Top = 74;
                refreshButton.Width = 150;
                refreshButton.Click += delegate { LoadCutListData(); BuildGrid(); };
                Controls.Add(refreshButton);

                grid = new DataGridView();
                grid.Left = 12;
                grid.Top = 108;
                grid.Width = 1240;
                grid.Height = 430;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.RowHeadersVisible = false;
                grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
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
                statusLabel.Width = 760;
                statusLabel.Height = 38;
                statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                Controls.Add(statusLabel);

                Button applyButton = new Button();
                applyButton.Text = "Apply";
                applyButton.Left = 880;
                applyButton.Top = 552;
                applyButton.Width = 110;
                applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                applyButton.Click += delegate { ApplyRowsSmart(); };
                Controls.Add(applyButton);

                Button closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Left = 1000;
                closeButton.Top = 552;
                closeButton.Width = 100;
                closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                closeButton.Click += delegate { Close(); };
                Controls.Add(closeButton);
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
                itemColumn.HeaderText = "Cut-list item";
                itemColumn.Width = 230;
                itemColumn.ReadOnly = true;
                grid.Columns.Add(itemColumn);

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

                ReloadPropertySelector();
                statusLabel.Text = "Loaded " + items.Count + " cut-list item(s). Visible property columns: " + string.Join(", ", visiblePropertyNames.ToArray());
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

            private void ReloadPropertySelector()
            {
                string current = propertySelector.SelectedItem == null ? string.Empty : propertySelector.SelectedItem.ToString();
                propertySelector.Items.Clear();
                foreach (string name in visiblePropertyNames)
                    propertySelector.Items.Add(name);
                if (!string.IsNullOrWhiteSpace(current) && propertySelector.Items.Contains(current))
                    propertySelector.SelectedItem = current;
                else if (propertySelector.Items.Count > 0)
                    propertySelector.SelectedIndex = 0;
                UpdateValueSelectorOptions();
            }

            private void UpdateValueSelectorOptions()
            {
                string propertyName = GetSelectedPropertyName();
                valueSelector.Items.Clear();
                foreach (string option in dropdownSettings.GetOptions(propertyName))
                    valueSelector.Items.Add(option);
                valueSelector.Text = string.Empty;
            }

            private string GetSelectedPropertyName()
            {
                return propertySelector.SelectedItem == null ? string.Empty : propertySelector.SelectedItem.ToString();
            }

            private void SetSelectedPropertyValueSmart()
            {
                RowTargetScope scope = ResolveRowTargetScope("Set Value");
                if (scope == RowTargetScope.Cancelled)
                    return;
                SetSelectedPropertyValue(scope == RowTargetScope.AllRows);
            }

            private void ClearSelectedColumnSmart()
            {
                RowTargetScope scope = ResolveRowTargetScope("Clear Column");
                if (scope == RowTargetScope.Cancelled)
                    return;
                ClearSelectedColumn(scope == RowTargetScope.AllRows);
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
                int totalRows = CountRealRows();
                int checkedRows = CountCheckedRows();
                bool newValue = !(totalRows > 0 && checkedRows == totalRows);
                SetAllApply(newValue);
            }

            private void SetSelectedPropertyValue(bool allRows)
            {
                string propertyName = GetSelectedPropertyName();
                string value = valueSelector.Text == null ? string.Empty : valueSelector.Text.Trim();
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;
                if (!ContainsIgnoreCase(visiblePropertyNames, propertyName))
                    return;

                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;
                    row.Cells[propertyName].Value = value;
                    row.Cells[ApplyColumnName].Value = true;
                    row.Cells[StatusColumnName].Value = "Edited";
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    dropdownSettings.AddOption(propertyName, value);
                    dropdownSettings.Save();
                }
            }

            private void ClearSelectedColumn(bool allRows)
            {
                string propertyName = GetSelectedPropertyName();
                if (string.IsNullOrWhiteSpace(propertyName))
                    return;

                DialogResult result = MessageBox.Show(
                    "This will set property '" + propertyName + "' to a blank value.\r\n\r\n" +
                    "Rows affected: " + (allRows ? "all rows" : "checked rows") + ".",
                    "Cabin Tools - Clear Cut-List Property Column",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.OK)
                    return;

                int updated = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow)
                        continue;
                    if (!allRows && !IsRowChecked(row))
                        continue;

                    CutListItemInfo item = row.Tag as CutListItemInfo;
                    if (item == null || item.PropertyManager == null)
                        continue;

                    try
                    {
                        SetTextProperty(item.PropertyManager, propertyName, string.Empty);
                        row.Cells[propertyName].Value = string.Empty;
                        row.Cells[StatusColumnName].Value = "Cleared " + propertyName;
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        row.Cells[StatusColumnName].Value = "! " + ex.Message;
                    }
                }

                ForceRebuild();
                statusLabel.Text = "Cleared " + propertyName + " on " + updated + " row(s).";
            }

            private void ApplyRows(bool allRows)
            {
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
                        foreach (string propertyName in visiblePropertyNames)
                        {
                            object cellValueObject = row.Cells[propertyName].Value;
                            string cellValue = cellValueObject == null ? string.Empty : Convert.ToString(cellValueObject).Trim();

                            if (string.IsNullOrWhiteSpace(cellValue))
                                continue;

                            SetTextProperty(item.PropertyManager, propertyName, cellValue);
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

            private void SetAllApply(bool value)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.IsNewRow)
                        row.Cells[ApplyColumnName].Value = value;
                }
            }

            private void AddPropertyName(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
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
                AddPropertyName(propertyName);
                if (!ContainsIgnoreCase(visiblePropertyNames, propertyName))
                    visiblePropertyNames.Add(propertyName);

                dropdownSettings.SetVisibleColumns("CutList", visiblePropertyNames);
                dropdownSettings.Save();
                BuildGrid();
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
