using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SolidDNA
{
    internal sealed class PdfNamingPart
    {
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    internal sealed class PdfNamingPattern
    {
        public List<PdfNamingPart> Parts { get; private set; }

        public PdfNamingPattern() { Parts = new List<PdfNamingPart>(); }

        public static PdfNamingPattern Default()
        {
            PdfNamingPattern pattern = new PdfNamingPattern();
            pattern.Parts.Add(new PdfNamingPart { Field = "drawing:DrwNumber", Before = "", After = "_" });
            pattern.Parts.Add(new PdfNamingPart { Field = "drawing:Revision", Before = "", After = " " });
            pattern.Parts.Add(new PdfNamingPart { Field = "drawing:cabinname", Before = "", After = " - " });
            pattern.Parts.Add(new PdfNamingPart { Field = "drawing:layoutclean", Before = "", After = "" });
            return pattern;
        }
    }

    internal static class PdfAutoNaming
    {
        private static readonly string SettingsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "CabinTools", "Settings", "PdfAutoNaming.txt");

        internal static PdfNamingPattern Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return PdfNamingPattern.Default();
                string[] lines = File.ReadAllLines(SettingsPath, Encoding.UTF8);
                if (lines.Length < 2 || lines[0] != "CabinToolsPdfNamingV1")
                    return PdfNamingPattern.Default();
                PdfNamingPattern pattern = new PdfNamingPattern();
                foreach (string line in lines.Skip(1))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3) return PdfNamingPattern.Default();
                    pattern.Parts.Add(new PdfNamingPart
                    {
                        Field = Decode(parts[0]), Before = Decode(parts[1]), After = Decode(parts[2])
                    });
                }
                return pattern.Parts.Count > 0 && pattern.Parts.All(p => !string.IsNullOrWhiteSpace(p.Field))
                    ? pattern : PdfNamingPattern.Default();
            }
            catch { return PdfNamingPattern.Default(); }
        }

        internal static bool Save(PdfNamingPattern pattern)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                List<string> lines = new List<string> { "CabinToolsPdfNamingV1" };
                foreach (PdfNamingPart part in pattern.Parts)
                    lines.Add(Encode(part.Field) + "\t" + Encode(part.Before) + "\t" + Encode(part.After));
                File.WriteAllLines(SettingsPath, lines.ToArray(), Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        internal static Dictionary<string, string> ReadMetadata(IModelDoc2 drawing, CabinNamingValues values)
        {
            Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            data["drawing:name"] = Path.GetFileNameWithoutExtension(drawing.GetPathName());
            data["drawing:cabinname"] = CabinPropertyRules.BuildCabinNameForPdf(
                values.CabinTypeDescription, values.CabinTypeDefined);
            data["drawing:layoutclean"] = CabinPropertyRules.CleanLayoutTypeForPdf(values.LayoutType);
            ReadProperties(drawing.Extension.CustomPropertyManager[""], "drawing:", data);

            // The first model view, across all sheets, determines model fields.
            IDrawingDoc drawingDoc = drawing as IDrawingDoc;
            object[] sheets = null;
            try { sheets = drawingDoc == null ? null : drawingDoc.GetViews() as object[]; }
            catch { /* Drawing-only properties remain usable. */ }
            if (sheets != null)
            {
                foreach (object sheet in sheets)
                {
                    object[] views = sheet as object[];
                    if (views == null) continue;
                    foreach (object viewObject in views)
                    {
                        IView view = viewObject as IView;
                        if (view == null) continue;
                        try
                        {
                            IModelDoc2 model = view.ReferencedDocument as IModelDoc2;
                            if (model == null) continue;
                            data["model:name"] = Path.GetFileNameWithoutExtension(model.GetPathName());
                            string configuration = view.ReferencedConfiguration ?? "";
                            data["model:configuration"] = configuration;
                            ReadProperties(model.Extension.CustomPropertyManager[""], "model:", data);
                            if (!string.IsNullOrWhiteSpace(configuration))
                                ReadProperties(model.Extension.CustomPropertyManager[configuration],
                                    "modelconfig:", data);
                            return data;
                        }
                        catch { /* Try the next drawing view. */ }
                    }
                }
            }
            return data;
        }

        private static void ReadProperties(ICustomPropertyManager manager, string prefix,
            Dictionary<string, string> data)
        {
            if (manager == null) return;
            Array names;
            try { names = manager.GetNames() as Array; }
            catch { return; }
            if (names == null) return;
            foreach (object item in names)
            {
                string name = Convert.ToString(item);
                if (string.IsNullOrWhiteSpace(name)) continue;
                string raw, resolved;
                bool wasResolved, linked;
                try
                {
                    manager.Get6(name, false, out raw, out resolved, out wasResolved, out linked);
                    data[prefix + name] = !string.IsNullOrWhiteSpace(resolved)
                        ? resolved.Trim() : (raw ?? "").Trim();
                }
                catch { data[prefix + name] = ""; }
            }
        }

        internal static string Label(string key)
        {
            if (key == "drawing:name") return "Drawing name";
            if (key == "drawing:cabinname") return "Cabin name (normalized)";
            if (key == "drawing:layoutclean") return "Layout type (normalized)";
            if (key == "model:name") return "First referenced model name";
            if (key == "model:configuration") return "First referenced configuration";
            if (key.StartsWith("drawing:", StringComparison.OrdinalIgnoreCase))
                return "Drawing property: " + key.Substring(8);
            if (key.StartsWith("modelconfig:", StringComparison.OrdinalIgnoreCase))
                return "First model configuration property: " + key.Substring(12);
            if (key.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                return "First model property: " + key.Substring(6);
            return key;
        }

        internal static string Build(PdfNamingPattern pattern, Dictionary<string, string> data,
            out List<string> missing, out string error)
        {
            missing = new List<string>();
            error = "";
            if (pattern == null || pattern.Parts.Count == 0)
            {
                error = "Choose at least one naming field.";
                return "";
            }
            StringBuilder name = new StringBuilder();
            foreach (PdfNamingPart part in pattern.Parts)
            {
                string value;
                if (string.Equals(part.Field, "drawing:cabinname", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string required in new[] { "drawing:Cabin type description", "drawing:Cabin type defined" })
                    {
                        string requiredValue;
                        if (data == null || !data.TryGetValue(required, out requiredValue) ||
                            string.IsNullOrWhiteSpace(requiredValue))
                        {
                            string label = Label(required);
                            if (!missing.Contains(label)) missing.Add(label);
                        }
                    }
                    if (missing.Count > 0) continue;
                }
                if (string.IsNullOrWhiteSpace(part.Field) || data == null ||
                    !data.TryGetValue(part.Field, out value) || string.IsNullOrWhiteSpace(value))
                {
                    string label = Label(part.Field ?? "");
                    if (!missing.Contains(label)) missing.Add(label);
                    continue;
                }
                name.Append(part.Before ?? "");
                name.Append(CabinPropertyRules.MakeSafeFileName(value));
                name.Append(part.After ?? "");
            }
            string stem = name.ToString();
            if (missing.Count > 0) return "";
            if (string.IsNullOrWhiteSpace(stem)) error = "Filename is blank.";
            else if (stem != stem.Trim() || stem.EndsWith(".", StringComparison.Ordinal))
                error = "Remove leading/trailing spaces and the final period.";
            else if (stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                     stem.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                error = "Windows file names cannot contain \\ / : * ? \" < > |.";
            else if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
                .Contains(stem.Split('.')[0].ToUpperInvariant()))
                error = "That filename is reserved by Windows.";
            else if (stem.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                error = "The .pdf extension is added automatically.";
            return stem + ".pdf";
        }
    }

    internal sealed class PdfAutoNamingDialog : Form
    {
        private sealed class Choice { public string Key { get; set; } public string Label { get; set; } }
        private readonly DataGridView fields = new DataGridView();
        private readonly ComboBox sample = new ComboBox();
        private readonly Label preview = new Label();
        private readonly IList<PdfBatchExportItem> items;
        private bool updating;
        internal PdfNamingPattern Result { get; private set; }

        internal PdfAutoNamingDialog(PdfNamingPattern current, IList<PdfBatchExportItem> items)
        {
            this.items = items;
            Text = "Change Auto Naming Setting";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(740, 490);
            MinimumSize = new Size(650, 400);
            Font = new Font("Segoe UI", 9F);

            Dictionary<string, string> choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "drawing:name", "drawing:DrwNumber", "drawing:Revision",
                "drawing:Cabin type description", "drawing:Cabin type defined", "drawing:cabinname",
                "drawing:Layout type", "drawing:layoutclean", "model:name", "model:configuration" })
                choices[key] = PdfAutoNaming.Label(key);
            foreach (PdfBatchExportItem item in items)
                if (item.NamingMetadata != null)
                    foreach (string key in item.NamingMetadata.Keys)
                        choices[key] = PdfAutoNaming.Label(key);
            foreach (PdfNamingPart part in current.Parts)
                if (!choices.ContainsKey(part.Field)) choices[part.Field] = PdfAutoNaming.Label(part.Field);

            fields.Dock = DockStyle.Fill;
            fields.AllowUserToAddRows = false;
            fields.AllowUserToDeleteRows = false;
            fields.RowHeadersVisible = false;
            fields.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            fields.EditMode = DataGridViewEditMode.EditOnEnter;
            fields.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            fields.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Field", HeaderText = "Property", FillWeight = 58,
                DataSource = choices.OrderBy(pair => pair.Value).Select(pair =>
                    new Choice { Key = pair.Key, Label = pair.Value }).ToList(),
                DisplayMember = "Label", ValueMember = "Key", FlatStyle = FlatStyle.Flat
            });
            fields.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Before", HeaderText = "Text before (prefix)", FillWeight = 21 });
            fields.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "After", HeaderText = "Text after (suffix)", FillWeight = 21 });
            fields.CurrentCellDirtyStateChanged += delegate
            { if (fields.IsCurrentCellDirty) fields.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            fields.CellValueChanged += delegate { UpdatePreview(); };
            fields.CellEndEdit += delegate { UpdatePreview(); };
            fields.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(fields, 0, 0);
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            Button add = new Button { Text = "Add field", AutoSize = true };
            add.Click += delegate { fields.Rows.Add("drawing:name", "", ""); UpdatePreview(); };
            Button remove = new Button { Text = "Remove field", AutoSize = true };
            remove.Click += delegate { if (fields.CurrentRow != null) fields.Rows.Remove(fields.CurrentRow); UpdatePreview(); };
            Button up = new Button { Text = "Move up", AutoSize = true };
            up.Click += delegate { MoveField(-1); };
            Button down = new Button { Text = "Move down", AutoSize = true };
            down.Click += delegate { MoveField(1); };
            actions.Controls.Add(add); actions.Controls.Add(remove); actions.Controls.Add(up); actions.Controls.Add(down);
            body.Controls.Add(actions, 0, 1);
            FlowLayoutPanel sampleArea = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                Padding = new Padding(8), WrapContents = true };
            sampleArea.Controls.Add(new Label { Text = "Preview drawing", AutoSize = true,
                Margin = new Padding(0, 6, 8, 0) });
            sample.DropDownStyle = ComboBoxStyle.DropDownList;
            sample.Width = 220;
            foreach (PdfBatchExportItem item in items) sample.Items.Add(item.DrawingName);
            sample.SelectedIndexChanged += delegate { UpdatePreview(); };
            sampleArea.Controls.Add(sample);
            preview.AutoSize = true;
            preview.MaximumSize = new Size(650, 0);
            preview.Margin = new Padding(8, 7, 0, 0);
            sampleArea.Controls.Add(preview);
            body.Controls.Add(sampleArea, 0, 2);

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45,
                FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { Text = "Cancel", Width = 85, DialogResult = DialogResult.Cancel };
            Button apply = new Button { Text = "Apply", Width = 85 };
            apply.Click += delegate
            {
                PdfNamingPattern pattern = ReadPattern();
                if (pattern.Parts.Count == 0 || pattern.Parts.Any(p => string.IsNullOrWhiteSpace(p.Field)))
                {
                    MessageBox.Show(this, "Choose at least one property for each row.", "Cabin Tools");
                    return;
                }
                Result = pattern;
                DialogResult = DialogResult.OK;
                Close();
            };
            footer.Controls.Add(cancel); footer.Controls.Add(apply);
            Controls.Add(body); Controls.Add(footer);
            AcceptButton = apply; CancelButton = cancel;
            foreach (PdfNamingPart part in current.Parts)
                fields.Rows.Add(part.Field, part.Before, part.After);
            if (sample.Items.Count > 0) sample.SelectedIndex = 0;
            UpdatePreview();
        }

        private PdfNamingPattern ReadPattern()
        {
            fields.EndEdit();
            PdfNamingPattern result = new PdfNamingPattern();
            foreach (DataGridViewRow row in fields.Rows)
                result.Parts.Add(new PdfNamingPart
                {
                    Field = Convert.ToString(row.Cells["Field"].Value),
                    Before = Convert.ToString(row.Cells["Before"].Value) ?? "",
                    After = Convert.ToString(row.Cells["After"].Value) ?? ""
                });
            return result;
        }

        private void MoveField(int offset)
        {
            if (fields.CurrentRow == null) return;
            int from = fields.CurrentRow.Index, to = from + offset;
            if (to < 0 || to >= fields.Rows.Count) return;
            fields.EndEdit();
            foreach (string column in new[] { "Field", "Before", "After" })
            {
                object temp = fields.Rows[from].Cells[column].Value;
                fields.Rows[from].Cells[column].Value = fields.Rows[to].Cells[column].Value;
                fields.Rows[to].Cells[column].Value = temp;
            }
            fields.CurrentCell = fields.Rows[to].Cells["Field"];
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (updating) return;
            updating = true;
            try
            {
                if (sample.SelectedIndex < 0 || items[sample.SelectedIndex].NamingMetadata == null)
                {
                    preview.Text = "Check Properties to preview a drawing.";
                    return;
                }
                List<string> missing; string error;
                string filename = PdfAutoNaming.Build(ReadPattern(),
                    items[sample.SelectedIndex].NamingMetadata, out missing, out error);
                preview.ForeColor = missing.Count == 0 && error.Length == 0 ? Color.Black : Color.DarkRed;
                preview.Text = missing.Count > 0 ? "Missing: " + string.Join(", ", missing) :
                    error.Length > 0 ? filename + " — " + error : filename;
            }
            finally { updating = false; }
        }
    }
}
