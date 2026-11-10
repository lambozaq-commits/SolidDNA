using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SolidDNA
{
    internal static class DrawingChecklistCommand
    {
        private static DrawingChecklistForm activeForm;

        public static void Show()
        {
            try
            {
                IModelDoc2 drawing = CabinCustomPropertyStore.GetActiveModelDocument();
                if (!CabinCustomPropertyStore.IsDrawing(drawing))
                {
                    MessageBox.Show("Open a drawing to use the checklist.", "Drawing Checklist");
                    return;
                }

                string path = drawing.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show("Save the drawing first so its checklist can be identified later.", "Drawing Checklist");
                    return;
                }

                path = Path.GetFullPath(path);
                if (activeForm != null && !activeForm.IsDisposed)
                {
                    if (string.Equals(activeForm.DrawingPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        activeForm.Show();
                        activeForm.WindowState = FormWindowState.Normal;
                        activeForm.BringToFront();
                        return;
                    }
                    activeForm.Close();
                    if (!activeForm.IsDisposed) return; // The user kept unsaved answers.
                }

                activeForm = new DrawingChecklistForm(path);
                activeForm.FormClosed += delegate { activeForm = null; };
                activeForm.Show(); // Modeless: keep the drawing usable beside the checklist.
            }
            catch (Exception ex)
            {
                MessageBox.Show("The checklist could not be opened.\n\n" + ex.Message,
                    "Drawing Checklist", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ChecklistPoint
    {
        public string Id;
        public string Title;
        public string Guidance;
        public string Example;
        public string Pages;
    }

    internal sealed class ChecklistAnswer
    {
        public string Id { get; set; }
        public string Result { get; set; }
        public string Note { get; set; }
    }

    internal sealed class ChecklistResponseDocument
    {
        public string DrawingKey { get; set; }
        public string TemplateVersion { get; set; }
        public string UpdatedBy { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<ChecklistAnswer> Answers { get; set; }
    }

    internal sealed class ChecklistSettings
    {
        public string SharedFolder = "";
        public string DrawingRoot = "";
        public string DefinitionsFile = "";
        public string ReferencePdf = "";

        private static string FilePath
        {
            get
            {
                return Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData),
                    "CabinTools", "DrawingChecklistSettings.json");
            }
        }

        public static ChecklistSettings Load()
        {
            if (!File.Exists(FilePath)) return new ChecklistSettings();
            Dictionary<string, object> data = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath, Encoding.UTF8));
            return new ChecklistSettings
            {
                SharedFolder = Read(data, "SharedFolder"),
                DrawingRoot = Read(data, "DrawingRoot"),
                DefinitionsFile = Read(data, "DefinitionsFile"),
                ReferencePdf = Read(data, "ReferencePdf")
            };
        }

        private static string Read(IDictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value) : "";
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string json = new JavaScriptSerializer().Serialize(new Dictionary<string, string>
            {
                { "SharedFolder", SharedFolder }, { "DrawingRoot", DrawingRoot },
                { "DefinitionsFile", DefinitionsFile }, { "ReferencePdf", ReferencePdf }
            });
            File.WriteAllText(FilePath, json, Encoding.UTF8);
        }
    }

    internal sealed class ChecklistStore
    {
        private readonly string drawingPath;
        private readonly ChecklistSettings settings;
        private DateTime observedWriteTimeUtc;
        private bool observedExists;
        private string observedContentHash;

        public string DrawingKey { get; private set; }
        public string ResponsePath { get; private set; }
        public bool Shared { get; private set; }
        public string TemplateVersion { get; private set; }
        public List<ChecklistPoint> Points { get; private set; }

        public static string ResourcePath(string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(typeof(ChecklistStore).Assembly.Location),
                "Resources", fileName);
        }

        public ChecklistStore(string drawingPath, ChecklistSettings settings)
        {
            this.drawingPath = drawingPath;
            this.settings = settings;
            string root = settings.DrawingRoot.Trim();
            string shared = settings.SharedFolder.Trim();
            string fullRoot = root.Length == 0 ? "" :
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            Shared = shared.Length > 0 && fullRoot.Length > 0 &&
                drawingPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            DrawingKey = Shared ? drawingPath.Substring(fullRoot.Length).Replace('\\', '/') :
                drawingPath.Replace('\\', '/');
            string folder = Shared ? Path.GetFullPath(shared) :
                Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData),
                    "CabinTools", "DrawingChecklist", "Responses");
            string stem = Path.GetFileNameWithoutExtension(drawingPath);
            foreach (char invalid in Path.GetInvalidFileNameChars())
                stem = stem.Replace(invalid, '_');
            if (stem.Length > 55) stem = stem.Substring(0, 55);
            ResponsePath = Path.Combine(folder, stem + "-" + Hash(DrawingKey.ToUpperInvariant()) + ".json");
            Points = LoadPoints();
        }

        private static string Hash(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < 12; i++) result.Append(bytes[i].ToString("x2"));
                return result.ToString();
            }
        }

        private List<ChecklistPoint> LoadPoints()
        {
            string definitionPath = settings.DefinitionsFile.Trim();
            if (definitionPath.Length == 0)
                definitionPath = ResourcePath("CeilingLayoutChecklist.json");
            if (!File.Exists(definitionPath))
                throw new FileNotFoundException("Checklist definitions were not found. Check Settings or reinstall the Resources folder.", definitionPath);

            Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(definitionPath, Encoding.UTF8));
            object version, items;
            if (data == null || !data.TryGetValue("version", out version) ||
                !data.TryGetValue("items", out items) || !(items is IEnumerable))
                throw new InvalidDataException("The checklist definition must contain a version and items array.");
            TemplateVersion = Convert.ToString(version);
            List<ChecklistPoint> points = new List<ChecklistPoint>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object entry in (IEnumerable)items)
            {
                Dictionary<string, object> row = entry as Dictionary<string, object>;
                if (row == null) throw new InvalidDataException("An item in the checklist is invalid.");
                object pageValue;
                row.TryGetValue("source_pages", out pageValue);
                List<string> pages = new List<string>();
                if (pageValue is IEnumerable)
                    foreach (object page in (IEnumerable)pageValue) pages.Add(Convert.ToString(page));
                ChecklistPoint point = new ChecklistPoint
                {
                    Id = Required(row, "id"), Title = Required(row, "title"),
                    Guidance = Required(row, "guidance"),
                    Example = Optional(row, "example_from_review"),
                    Pages = string.Join(", ", pages.ToArray())
                };
                if (!ids.Add(point.Id)) throw new InvalidDataException("Duplicate checklist ID: " + point.Id);
                points.Add(point);
            }
            if (points.Count == 0) throw new InvalidDataException("The checklist has no items.");
            return points;
        }

        private static string Optional(Dictionary<string, object> row, string name)
        {
            object value;
            return row.TryGetValue(name, out value) && value != null ? Convert.ToString(value) : "";
        }

        private static string Required(Dictionary<string, object> row, string name)
        {
            string value = Optional(row, name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Checklist item is missing " + name + ".");
            return value;
        }

        public Dictionary<string, ChecklistAnswer> LoadAnswers()
        {
            observedExists = File.Exists(ResponsePath);
            observedWriteTimeUtc = observedExists ? File.GetLastWriteTimeUtc(ResponsePath) : DateTime.MinValue;
            Dictionary<string, ChecklistAnswer> answers =
                new Dictionary<string, ChecklistAnswer>(StringComparer.OrdinalIgnoreCase);
            if (!observedExists)
            {
                observedContentHash = "";
                return answers;
            }
            string content = File.ReadAllText(ResponsePath, Encoding.UTF8);
            observedContentHash = Hash(content);
            ChecklistResponseDocument stored = new JavaScriptSerializer().Deserialize<ChecklistResponseDocument>(
                content);
            if (stored == null || stored.DrawingKey == null ||
                !string.Equals(stored.DrawingKey, DrawingKey, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The response file belongs to a different drawing: " + ResponsePath);
            if (stored.Answers != null)
                foreach (ChecklistAnswer answer in stored.Answers)
                    if (answer != null && !string.IsNullOrWhiteSpace(answer.Id))
                        answers[answer.Id] = answer;
            return answers;
        }

        public void SaveAnswers(List<ChecklistAnswer> answers)
        {
            bool existsNow = File.Exists(ResponsePath);
            if (existsNow != observedExists || (existsNow &&
                (File.GetLastWriteTimeUtc(ResponsePath) != observedWriteTimeUtc ||
                 Hash(File.ReadAllText(ResponsePath, Encoding.UTF8)) != observedContentHash)))
                throw new IOException("Someone changed this checklist since it was opened. Close and reopen it before saving so their responses are not overwritten.");

            Directory.CreateDirectory(Path.GetDirectoryName(ResponsePath));
            ChecklistResponseDocument document = new ChecklistResponseDocument
            {
                DrawingKey = DrawingKey, TemplateVersion = TemplateVersion,
                UpdatedBy = System.Environment.UserName, UpdatedUtc = DateTime.UtcNow,
                Answers = answers
            };
            string temp = ResponsePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(document), Encoding.UTF8);
                if (existsNow)
                    File.Replace(temp, ResponsePath, ResponsePath + ".bak");
                else
                    File.Move(temp, ResponsePath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            observedExists = true;
            observedWriteTimeUtc = File.GetLastWriteTimeUtc(ResponsePath);
            observedContentHash = Hash(File.ReadAllText(ResponsePath, Encoding.UTF8));
        }
    }

    internal sealed class DrawingChecklistForm : Form
    {
        private readonly string drawingPath;
        private ChecklistSettings settings;
        private ChecklistStore store;
        private FlowLayoutPanel list;
        private Label progress;
        private Label location;
        private readonly List<ChecklistRow> rows = new List<ChecklistRow>();
        private Dictionary<string, ChecklistAnswer> loadedAnswers;
        private bool dirty;
        public string DrawingPath { get { return drawingPath; } }

        public DrawingChecklistForm(string drawingPath)
        {
            this.drawingPath = drawingPath;
            settings = ChecklistSettings.Load();
            store = new ChecklistStore(drawingPath, settings);
            Build();
            LoadRows();
            FormClosing += OnClosing;
        }

        private void Build()
        {
            Text = "Drawing Checklist";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 720);
            MinimumSize = new Size(540, 460);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
                Padding = new Padding(12), BackColor = Color.FromArgb(247, 248, 250)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill };
            Label name = new Label
            {
                Text = Path.GetFileName(drawingPath), Dock = DockStyle.Top,
                Height = 28, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true
            };
            location = new Label { Dock = DockStyle.Bottom, Height = 25, ForeColor = Color.DimGray };
            Button settingsButton = new Button { Text = "Settings...", Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Width = 100, Height = 29 };
            header.Controls.Add(name);
            header.Controls.Add(location);
            header.Controls.Add(settingsButton);
            header.Resize += delegate { settingsButton.Left = header.ClientSize.Width - settingsButton.Width; };
            settingsButton.Click += delegate { ChangeSettings(); };
            layout.Controls.Add(header, 0, 0);

            list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.White, Padding = new Padding(4)
            };
            list.Resize += delegate { ResizeRows(); };
            layout.Controls.Add(list, 0, 1);

            Panel footer = new Panel { Dock = DockStyle.Fill };
            progress = new Label { Dock = DockStyle.Left, Width = 265,
                TextAlign = ContentAlignment.MiddleLeft };
            Button save = new Button { Text = "Save", Width = 105, Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Top = 10 };
            footer.Controls.Add(progress);
            footer.Controls.Add(save);
            footer.Resize += delegate { save.Left = footer.ClientSize.Width - save.Width; };
            save.Click += delegate { SaveRows(); };
            layout.Controls.Add(footer, 0, 2);
        }

        private void LoadRows()
        {
            Dictionary<string, ChecklistAnswer> saved = store.LoadAnswers();
            loadedAnswers = saved;
            rows.Clear();
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (ChecklistPoint point in store.Points)
            {
                ChecklistAnswer answer;
                saved.TryGetValue(point.Id, out answer);
                string referencePdf = string.IsNullOrWhiteSpace(settings.ReferencePdf)
                    ? ChecklistStore.ResourcePath("CeilingLayoutChecklistReference.pdf")
                    : settings.ReferencePdf;
                ChecklistRow row = new ChecklistRow(point, answer, referencePdf);
                row.Changed += delegate { dirty = true; UpdateProgress(); };
                rows.Add(row);
                list.Controls.Add(row);
            }
            list.ResumeLayout();
            ResizeRows();
            dirty = false;
            location.Text = store.Shared ? "Shared checklist" : "Local checklist · Set shared folders in Settings";
            UpdateProgress();
        }

        private void ResizeRows()
        {
            if (list == null) return;
            int width = Math.Max(460, list.ClientSize.Width - 27);
            foreach (ChecklistRow row in rows) row.Width = width;
        }

        private void UpdateProgress()
        {
            int reviewed = 0;
            foreach (ChecklistRow row in rows)
                if (row.Result != "Not reviewed") reviewed++;
            progress.Text = reviewed + " / " + rows.Count + " reviewed" + (dirty ? " · Unsaved" : "");
        }

        private void SaveRows()
        {
            try
            {
                List<ChecklistAnswer> answers = new List<ChecklistAnswer>();
                foreach (ChecklistRow row in rows) answers.Add(row.GetAnswer());
                // Retain answers for IDs temporarily removed from a later template.
                foreach (KeyValuePair<string, ChecklistAnswer> entry in loadedAnswers)
                {
                    bool visible = false;
                    foreach (ChecklistRow row in rows)
                        if (string.Equals(row.GetAnswer().Id, entry.Key,
                            StringComparison.OrdinalIgnoreCase)) { visible = true; break; }
                    if (!visible) answers.Add(entry.Value);
                }
                store.SaveAnswers(answers);
                loadedAnswers = new Dictionary<string, ChecklistAnswer>(StringComparer.OrdinalIgnoreCase);
                foreach (ChecklistAnswer answer in answers) loadedAnswers[answer.Id] = answer;
                dirty = false;
                UpdateProgress();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Checklist responses were not saved.\n\n" + ex.Message,
                    "Drawing Checklist", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangeSettings()
        {
            if (dirty && MessageBox.Show("Discard unsaved responses to change settings?",
                "Drawing Checklist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            using (ChecklistSettingsForm dialog = new ChecklistSettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if ((dialog.Value.SharedFolder.Length == 0) !=
                        (dialog.Value.DrawingRoot.Length == 0))
                        throw new InvalidOperationException("Set both Shared response folder and Drawing root folder, or leave both empty for local use.");
                    ChecklistStore next = new ChecklistStore(drawingPath, dialog.Value);
                    if (dialog.Value.SharedFolder.Length > 0 && !next.Shared)
                        throw new InvalidOperationException("This drawing is outside the selected Drawing root folder.");
                    // Validate new response data before committing settings.
                    next.LoadAnswers();
                    dialog.Value.Save();
                    settings = dialog.Value;
                    store = next;
                    LoadRows();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Settings were not applied.\n\n" + ex.Message,
                        "Drawing Checklist", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (dirty && MessageBox.Show("Discard unsaved checklist changes?",
                "Drawing Checklist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        }
    }

    internal sealed class ChecklistRow : Panel
    {
        private readonly ChecklistPoint point;
        private readonly ComboBox result;
        private readonly TextBox note;
        public event EventHandler Changed;
        public string Result { get { return Convert.ToString(result.SelectedItem); } }

        public ChecklistRow(ChecklistPoint point, ChecklistAnswer saved, string referencePdf)
        {
            this.point = point;
            Height = 43;
            Margin = new Padding(2, 3, 2, 3);
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Color.White;
            Button expand = new Button { Text = "+  " + point.Id + "  " + point.Title,
                FlatStyle = FlatStyle.Flat, Height = 38, Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft };
            expand.FlatAppearance.BorderSize = 0;
            result = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 139, Height = 25, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Top = 6 };
            result.Items.AddRange(new object[] { "Not reviewed", "Pass", "Needs work", "Not applicable" });
            result.SelectedItem = saved != null && result.Items.Contains(saved.Result)
                ? saved.Result : "Not reviewed";
            Label guidance = new Label { Text = point.Guidance, AutoSize = false,
                Dock = DockStyle.Top, Height = 53, Padding = new Padding(8, 8, 8, 0) };
            Label example = new Label { Text = point.Example + "  (Source pages: " + point.Pages + ")",
                Dock = DockStyle.Top, Height = 49, ForeColor = Color.DimGray,
                Padding = new Padding(8, 5, 8, 0) };
            Label noteLabel = new Label { Text = "Note / action", Dock = DockStyle.Top,
                Height = 21, Padding = new Padding(8, 2, 0, 0) };
            note = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top, Height = 68, Text = saved == null ? "" : (saved.Note ?? "") };
            Button sourceButton = new Button { Text = "Open reference PDF", Dock = DockStyle.Top,
                Height = 29, Enabled = !string.IsNullOrWhiteSpace(referencePdf) };
            sourceButton.Click += delegate
            {
                if (!File.Exists(referencePdf))
                    MessageBox.Show("Reference PDF not found. Update its path in Settings.", "Drawing Checklist");
                else
                    System.Diagnostics.Process.Start(referencePdf);
            };
            // Dock.Top displays controls in reverse insertion order.
            Controls.Add(sourceButton);
            Controls.Add(note);
            Controls.Add(noteLabel);
            Controls.Add(example);
            Controls.Add(guidance);
            Controls.Add(expand);
            Controls.Add(result);
            result.BringToFront();
            Resize += delegate { result.Left = ClientSize.Width - result.Width - 8; };
            expand.Click += delegate
            {
                bool open = Height < 200;
                Height = open ? 270 : 43;
                expand.Text = (open ? "−  " : "+  ") + point.Id + "  " + point.Title;
            };
            result.SelectedIndexChanged += delegate { if (Changed != null) Changed(this, EventArgs.Empty); };
            note.TextChanged += delegate { if (Changed != null) Changed(this, EventArgs.Empty); };
        }

        public ChecklistAnswer GetAnswer()
        {
            return new ChecklistAnswer { Id = point.Id, Result = Result, Note = note.Text };
        }
    }

    internal sealed class ChecklistSettingsForm : Form
    {
        private readonly TextBox shared = new TextBox();
        private readonly TextBox root = new TextBox();
        private readonly TextBox definitions = new TextBox();
        private readonly TextBox reference = new TextBox();
        public ChecklistSettings Value { get; private set; }

        public ChecklistSettingsForm(ChecklistSettings current)
        {
            Text = "Drawing Checklist Settings";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 258);
            MinimumSize = new Size(540, 290);
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            Value = new ChecklistSettings();
            shared.Text = current.SharedFolder;
            root.Text = current.DrawingRoot;
            definitions.Text = current.DefinitionsFile;
            reference.Text = current.ReferencePdf;

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 6
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
            for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            Controls.Add(grid);
            AddPath(grid, 0, "Shared response folder", shared, true);
            AddPath(grid, 1, "Drawing root folder", root, true);
            AddPath(grid, 2, "Checklist JSON", definitions, false);
            AddPath(grid, 3, "Reference PDF", reference, false);
            Label hint = new Label { Text = "For shared responses, set both folders. Use the same drawing root structure on each computer.",
                Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            grid.Controls.Add(hint, 0, 4);
            grid.SetColumnSpan(hint, 3);
            Button ok = new Button { Text = "Apply", Dock = DockStyle.Right, Width = 94 };
            grid.Controls.Add(ok, 1, 5);
            ok.Click += delegate
            {
                Value.SharedFolder = shared.Text.Trim();
                Value.DrawingRoot = root.Text.Trim();
                Value.DefinitionsFile = definitions.Text.Trim();
                Value.ReferencePdf = reference.Text.Trim();
                DialogResult = DialogResult.OK;
            };
            AcceptButton = ok;
        }

        private static void AddPath(TableLayoutPanel grid, int row, string caption,
            TextBox field, bool folder)
        {
            grid.Controls.Add(new Label { Text = caption, TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill }, 0, row);
            field.Dock = DockStyle.Fill;
            grid.Controls.Add(field, 1, row);
            Button browse = new Button { Text = "Browse...", Dock = DockStyle.Fill };
            grid.Controls.Add(browse, 2, row);
            browse.Click += delegate
            {
                if (folder)
                {
                    using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                        if (dialog.ShowDialog() == DialogResult.OK) field.Text = dialog.SelectedPath;
                }
                else
                {
                    using (OpenFileDialog dialog = new OpenFileDialog())
                    {
                        dialog.Filter = row == 2 ? "JSON files (*.json)|*.json" : "PDF files (*.pdf)|*.pdf";
                        if (dialog.ShowDialog() == DialogResult.OK) field.Text = dialog.FileName;
                    }
                }
            };
        }
    }
}
