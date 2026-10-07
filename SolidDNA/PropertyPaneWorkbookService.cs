using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace SolidDNA
{
    internal sealed class PropertyPaneWorkbookLoadResult
    {
        public string ResolvedPath { get; set; }
        public int ListCount { get; set; }
        public int MappingCount { get; set; }
    }

    /// <summary>
    /// Reads list values and drawing-number mappings from an XLSX workbook.
    /// The workbook is opened read-only with sharing enabled so PDM/Excel retains
    /// control of checkout, versioning, and edits.
    /// </summary>
    internal sealed class PropertyPaneWorkbookService
    {
        private static readonly XNamespace SpreadsheetNamespace =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRelationshipNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        public PropertyPaneWorkbookLoadResult Apply(string configuredPath, PropertyPaneProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");

            string path = ResolvePath(configuredPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The shared Excel workbook was not found.", path);

            List<WorkbookTable> tables = ReadWorkbook(path);
            int listCount = ApplyLists(profile, tables);
            int mappingCount = ApplyMappings(profile, tables);

            if (listCount == 0 && mappingCount == 0)
                throw new InvalidDataException("No recognized list or drawing-map headers were found in the workbook.");

            return new PropertyPaneWorkbookLoadResult
            {
                ResolvedPath = path,
                ListCount = listCount,
                MappingCount = mappingCount
            };
        }

        public static string ResolvePath(string configuredPath)
        {
            string value = (configuredPath ?? string.Empty).Trim().Trim('"');
            if (value.Length == 0)
                throw new InvalidDataException("Select the shared Excel workbook first.");

            value = System.Environment.ExpandEnvironmentVariables(value);
            if (!Path.IsPathRooted(value))
                throw new InvalidDataException("Use an absolute, UNC, or environment-variable workbook path.");

            return Path.GetFullPath(value);
        }

        private static int ApplyLists(PropertyPaneProfile profile, List<WorkbookTable> tables)
        {
            Dictionary<string, PropertyPaneNamedList> known = profile.Lists
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Name))
                .GroupBy(l => Normalize(l.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            WorkbookTable best = null;
            int bestScore = 0;
            foreach (WorkbookTable table in tables)
            {
                string[] headers = table.Header;
                int score = headers.Count(h => known.ContainsKey(Normalize(h)));
                if (score > bestScore)
                {
                    best = table;
                    bestScore = score;
                }
            }

            if (best == null || bestScore == 0)
                return 0;

            int updated = 0;
            for (int column = 0; column < best.Header.Length; column++)
            {
                PropertyPaneNamedList list;
                if (!known.TryGetValue(Normalize(best.Header[column]), out list))
                    continue;

                List<string> values = best.Rows
                    .Select(row => column < row.Length ? (row[column] ?? string.Empty).Trim() : string.Empty)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (values.Count == 0)
                    continue;

                list.Values = values;
                updated++;
            }

            return updated;
        }

        private static int ApplyMappings(PropertyPaneProfile profile, List<WorkbookTable> tables)
        {
            MappingCandidate best = null;
            foreach (WorkbookTable table in tables)
            {
                MappingCandidate candidate = CreateMappingCandidate(table);
                if (candidate != null && (best == null || candidate.Rows.Count > best.Rows.Count))
                    best = candidate;
            }

            if (best == null || best.Rows.Count == 0)
                return 0;

            profile.DrawingNumberMappings = best.Rows;
            ReplaceList(profile, "Cabin type description", best.Rows.Select(r => r.CabinDescription));
            ReplaceList(profile, "Cabin type defined", best.Rows.Select(r => r.CabinDefined));
            ReplaceList(profile, "Layout Type", best.Rows.Select(r => r.LayoutType));
            return best.Rows.Count;
        }

        private static MappingCandidate CreateMappingCandidate(WorkbookTable table)
        {
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < table.Header.Length; i++)
                columns[Normalize(table.Header[i])] = i;

            int cabin;
            int defined;
            int layout;
            int drawing;
            if (!columns.TryGetValue("cabintypedescription", out cabin) ||
                !columns.TryGetValue("cabintypedefined", out defined) ||
                !columns.TryGetValue("layouttype", out layout) ||
                !(columns.TryGetValue("drwnumber", out drawing) || columns.TryGetValue("drawingnumber", out drawing)))
                return null;

            List<DrawingNumberMap> rows = new List<DrawingNumberMap>();
            foreach (string[] row in table.Rows)
            {
                string cabinValue = Cell(row, cabin);
                string definedValue = Cell(row, defined);
                string layoutValue = Cell(row, layout);
                string drawingValue = Cell(row, drawing);
                if (cabinValue.Length == 0 || definedValue.Length == 0 ||
                    layoutValue.Length == 0 || drawingValue.Length == 0)
                    continue;

                rows.Add(new DrawingNumberMap
                {
                    CabinDescription = cabinValue,
                    CabinDefined = definedValue,
                    LayoutType = layoutValue,
                    DrawingNumber = drawingValue
                });
            }

            return new MappingCandidate { Rows = rows };
        }

        private static void ReplaceList(PropertyPaneProfile profile, string name, IEnumerable<string> values)
        {
            PropertyPaneNamedList list = profile.Lists.FirstOrDefault(l =>
                string.Equals(Normalize(l.Name), Normalize(name), StringComparison.OrdinalIgnoreCase));
            if (list == null)
            {
                list = new PropertyPaneNamedList { Name = name };
                profile.Lists.Add(list);
            }

            list.Values = values.Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Cell(string[] row, int column)
        {
            return column >= 0 && column < row.Length
                ? (row[column] ?? string.Empty).Trim()
                : string.Empty;
        }

        private static List<WorkbookTable> ReadWorkbook(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                List<string> sharedStrings = ReadSharedStrings(archive);
                XDocument workbook = LoadXml(archive, "xl/workbook.xml");
                XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                Dictionary<string, string> targets = relationships
                    .Descendants(PackageRelationshipNamespace + "Relationship")
                    .Where(r => r.Attribute("Id") != null && r.Attribute("Target") != null)
                    .ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target"));

                List<WorkbookTable> tables = new List<WorkbookTable>();
                foreach (XElement sheet in workbook.Descendants(SpreadsheetNamespace + "sheet"))
                {
                    string relationshipId = (string)sheet.Attribute(OfficeRelationshipNamespace + "id");
                    string target;
                    if (string.IsNullOrWhiteSpace(relationshipId) || !targets.TryGetValue(relationshipId, out target))
                        continue;

                    string entryPath = NormalizePartPath("xl", target);
                    ZipArchiveEntry entry = archive.GetEntry(entryPath);
                    if (entry == null)
                        continue;

                    using (Stream sheetStream = entry.Open())
                    {
                        WorkbookTable table = ReadTable((string)sheet.Attribute("name"),
                            XDocument.Load(sheetStream), sharedStrings);
                        if (table != null)
                            tables.Add(table);
                    }
                }

                return tables;
            }
        }

        private static WorkbookTable ReadTable(string name, XDocument document, List<string> sharedStrings)
        {
            List<string[]> rows = new List<string[]>();
            foreach (XElement row in document.Descendants(SpreadsheetNamespace + "row"))
            {
                Dictionary<int, string> values = new Dictionary<int, string>();
                foreach (XElement cell in row.Elements(SpreadsheetNamespace + "c"))
                {
                    int column = ColumnIndex((string)cell.Attribute("r"));
                    if (column >= 0)
                        values[column] = ReadCell(cell, sharedStrings);
                }

                if (values.Count == 0)
                    continue;
                int last = values.Keys.Max();
                string[] result = new string[last + 1];
                for (int i = 0; i <= last; i++)
                    result[i] = values.ContainsKey(i) ? values[i] : string.Empty;
                rows.Add(result);
            }

            int headerIndex = rows.FindIndex(r => r.Any(v => !string.IsNullOrWhiteSpace(v)));
            if (headerIndex < 0)
                return null;

            return new WorkbookTable
            {
                Name = name ?? string.Empty,
                Header = rows[headerIndex],
                Rows = rows.Skip(headerIndex + 1).ToList()
            };
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return new List<string>();

            using (Stream stream = entry.Open())
            {
                XDocument document = XDocument.Load(stream);
                return document.Descendants(SpreadsheetNamespace + "si")
                    .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t")
                        .Select(text => text.Value)))
                    .ToList();
            }
        }

        private static string ReadCell(XElement cell, List<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t") ?? string.Empty;
            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
                return string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(t => t.Value));

            XElement value = cell.Element(SpreadsheetNamespace + "v");
            string raw = value == null ? string.Empty : value.Value;
            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                int index;
                return int.TryParse(raw, out index) && index >= 0 && index < sharedStrings.Count
                    ? sharedStrings[index]
                    : string.Empty;
            }

            return raw;
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path);
            if (entry == null)
                throw new InvalidDataException("The workbook is missing " + path + ".");
            using (Stream stream = entry.Open())
                return XDocument.Load(stream);
        }

        private static string NormalizePartPath(string baseFolder, string target)
        {
            string combined = target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : baseFolder.TrimEnd('/') + "/" + target;
            Stack<string> parts = new Stack<string>();
            foreach (string part in combined.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == "..")
                {
                    if (parts.Count > 0)
                        parts.Pop();
                }
                else
                {
                    parts.Push(part);
                }
            }
            return string.Join("/", parts.Reverse().ToArray());
        }

        private static int ColumnIndex(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return -1;
            int value = 0;
            foreach (char character in reference)
            {
                if (!char.IsLetter(character))
                    break;
                value = value * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
            }
            return value - 1;
        }

        private static string Normalize(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private sealed class WorkbookTable
        {
            public string Name;
            public string[] Header;
            public List<string[]> Rows;
        }

        private sealed class MappingCandidate
        {
            public List<DrawingNumberMap> Rows;
        }
    }
}
