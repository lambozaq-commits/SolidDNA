using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    internal sealed class CabinNamingValues
    {
        public string DrwNumber { get; set; }
        public string Revision { get; set; }
        public string CabinTypeDescription { get; set; }
        public string CabinTypeDefined { get; set; }
        public string LayoutType { get; set; }
    }

    internal sealed class CustomPropertySnapshot
    {
        public string Name { get; set; }
        public int Type { get; set; }
        public string RawValue { get; set; }
        public int OriginalIndex { get; set; }

        public CustomPropertySnapshot Clone()
        {
            return new CustomPropertySnapshot
            {
                Name = Name,
                Type = Type,
                RawValue = RawValue,
                OriginalIndex = OriginalIndex
            };
        }
    }

    internal sealed class PropertyOrderDefinition
    {
        public string SourcePath { get; set; }
        public DateTime SourceLastWriteTime { get; set; }
        public List<string> PriorityPropertyNames { get; set; }
    }

    internal sealed class PropertyCheckResult
    {
        public string DocumentTypeName { get; set; }
        public PropertyOrderDefinition Definition { get; set; }
        public List<CustomPropertySnapshot> Properties { get; set; }
        public List<string> MissingOrBlankProperties { get; set; }
        public Dictionary<string, int> CurrentPositions { get; set; }
        public bool PriorityOrderCorrect { get; set; }
        public string RepairBlockReason { get; set; }

        public bool CanRepair
        {
            get
            {
                return string.IsNullOrWhiteSpace(RepairBlockReason);
            }
        }

        public string BuildReport()
        {
            StringBuilder report = new StringBuilder();

            report.AppendLine("PROPERTY CHECKER - CHECK ONLY");
            report.AppendLine("No properties have been changed.");
            report.AppendLine();

            report.AppendLine(
                "Document type: " + DocumentTypeName);

            report.AppendLine(
                "Scope: General custom properties only.");

            report.AppendLine(
                "Source file: " + Definition.SourcePath);

            report.AppendLine(
                "Source file updated: " +
                Definition.SourceLastWriteTime.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

            report.AppendLine(
                "General custom-property count: " +
                Properties.Count);

            report.AppendLine();

            if (MissingOrBlankProperties.Count == 0)
            {
                report.AppendLine(
                    "Priority properties: all have values.");
            }
            else
            {
                report.AppendLine(
                    "Missing or blank priority properties:");

                foreach (string propertyName in
                    MissingOrBlankProperties)
                {
                    report.AppendLine("  - " + propertyName);
                }

                report.AppendLine();
                report.AppendLine(
                    "Reorder + Repair will let you enter any " +
                    "available values. Blank entries are allowed.");
            }

            report.AppendLine();
            report.AppendLine("REQUESTED PROPERTY ORDER");

            for (int i = 0;
                i < Definition.PriorityPropertyNames.Count;
                i++)
            {
                string propertyName =
                    Definition.PriorityPropertyNames[i];

                int position = -1;

                if (CurrentPositions.ContainsKey(propertyName))
                    position = CurrentPositions[propertyName];

                string positionText =
                    position >= 0
                        ? (position + 1).ToString()
                        : "missing";

                report.AppendLine(
                    (i + 1).ToString() + ". " +
                    propertyName +
                    " - current position: " +
                    positionText);
            }

            report.AppendLine();

            report.AppendLine(
                "Priority order state: " +
                (PriorityOrderCorrect
                    ? "Correct"
                    : "Needs reorder"));

            report.AppendLine();

            if (CanRepair)
            {
                report.AppendLine("Repair status: Ready.");
                report.AppendLine(
                    "Reorder + Repair will create a local backup, " +
                    "add missing priority properties as blank text " +
                    "properties, move the priority properties to the " +
                    "top, and keep all other general properties in " +
                    "their current relative order.");
            }
            else
            {
                report.AppendLine("Repair status: Blocked.");
                report.AppendLine(RepairBlockReason);
            }

            report.AppendLine();
            report.AppendLine(
                "Configuration-specific and cut-list properties " +
                "are not changed.");

            return report.ToString();
        }
    }

    internal sealed class PropertyRepairResult
    {
        public string BackupFilePath { get; set; }
        public List<string> AddedProperties { get; set; }
        public int ReorderedPropertyCount { get; set; }
    }

    internal static class CabinPropertyRules
    {
        public const string DrwNumberProperty = "DrwNumber";
        public const string RevisionProperty = "Revision";
        public const string CabinTypeDescriptionProperty =
            "Cabin type description";
        public const string CabinTypeDefinedProperty =
            "Cabin type defined";
        public const string LayoutTypeProperty = "Layout type";

        public static string BuildPdfFileName(
            CabinNamingValues values)
        {
            string fileName =
                Clean(values.DrwNumber) + "_" +
                Clean(values.Revision) + " " +
                Clean(values.CabinTypeDescription) + " " +
                Clean(values.CabinTypeDefined) + " - " +
                Clean(values.LayoutType) + ".pdf";

            return MakeSafeFileName(fileName);
        }

        public static List<string> GetMissingPdfNamingProperties(
            CabinNamingValues values)
        {
            List<string> missing = new List<string>();

            if (string.IsNullOrWhiteSpace(values.DrwNumber))
                missing.Add(DrwNumberProperty);

            if (string.IsNullOrWhiteSpace(values.Revision))
                missing.Add(RevisionProperty);

            if (string.IsNullOrWhiteSpace(
                values.CabinTypeDescription))
            {
                missing.Add(CabinTypeDescriptionProperty);
            }

            if (string.IsNullOrWhiteSpace(
                values.CabinTypeDefined))
            {
                missing.Add(CabinTypeDefinedProperty);
            }

            if (string.IsNullOrWhiteSpace(values.LayoutType))
                missing.Add(LayoutTypeProperty);

            return missing;
        }

        public static string MakeSafeFileName(string fileName)
        {
            char[] invalidCharacters =
                Path.GetInvalidFileNameChars();

            foreach (char invalidCharacter in invalidCharacters)
            {
                fileName = fileName.Replace(
                    invalidCharacter,
                    '_');
            }

            return fileName.Trim();
        }

        public static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }

        public static string Clean(string value)
        {
            return value == null
                ? string.Empty
                : value.Trim();
        }
    }

    internal static class PropertyOrderSettings
    {
        private const string SettingsFolderName = "CabinTools";
        private const string SettingsFileName =
            "PropertyCheckerSettings.txt";

        public static string GetSavedSourceFilePath()
        {
            string settingsFilePath = GetSettingsFilePath();

            if (!File.Exists(settingsFilePath))
                return string.Empty;

            try
            {
                return File.ReadAllText(settingsFilePath).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void SaveSourceFilePath(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new ArgumentException(
                    "The property order source path is empty.");
            }

            string settingsDirectory =
                Path.GetDirectoryName(GetSettingsFilePath());

            Directory.CreateDirectory(settingsDirectory);

            File.WriteAllText(
                GetSettingsFilePath(),
                Path.GetFullPath(sourceFilePath));
        }

        private static string GetSettingsFilePath()
        {
            string settingsDirectory = Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.LocalApplicationData),
                SettingsFolderName);

            return Path.Combine(
                settingsDirectory,
                SettingsFileName);
        }
    }

    internal static class PropertyOrderSource
    {
        private const string StartMarker =
            "[CabinToolsPriority]";

        private const string EndMarker =
            "[/CabinToolsPriority]";

        private const int FallbackPriorityCount = 19;

        public static PropertyOrderDefinition LoadSavedDefinition()
        {
            string sourceFilePath =
                PropertyOrderSettings.GetSavedSourceFilePath();

            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new InvalidOperationException(
                    "No Properties.txt source file has been selected. " +
                    "Click 'Select Properties.txt...' and select the " +
                    "file from the PDM template folder.");
            }

            return LoadDefinition(sourceFilePath);
        }

        public static PropertyOrderDefinition LoadDefinition(
            string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new InvalidOperationException(
                    "The Properties.txt source path is empty.");
            }

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException(
                    "The configured Properties.txt source file could " +
                    "not be found. In PDM, get the latest version of " +
                    "the file and then select it again.",
                    sourceFilePath);
            }

            string[] lines = File.ReadAllLines(sourceFilePath);

            List<string> propertyNames =
                ExtractPriorityPropertyNames(lines);

            if (propertyNames.Count == 0)
            {
                throw new InvalidOperationException(
                    "Properties.txt contains no usable priority " +
                    "property names.");
            }

            return new PropertyOrderDefinition
            {
                SourcePath = sourceFilePath,
                SourceLastWriteTime =
                    File.GetLastWriteTime(sourceFilePath),
                PriorityPropertyNames = propertyNames
            };
        }

        private static List<string> ExtractPriorityPropertyNames(
            string[] lines)
        {
            List<string> names = new List<string>();

            bool startMarkerFound = false;
            bool insideMarkedSection = false;

            foreach (string rawLine in lines)
            {
                string line = NormalizeLine(rawLine);

                if (string.Equals(
                    line,
                    StartMarker,
                    StringComparison.OrdinalIgnoreCase))
                {
                    startMarkerFound = true;
                    insideMarkedSection = true;
                    continue;
                }

                if (string.Equals(
                    line,
                    EndMarker,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (insideMarkedSection)
                        break;

                    continue;
                }

                if (startMarkerFound && !insideMarkedSection)
                    continue;

                if (!startMarkerFound &&
                    names.Count >= FallbackPriorityCount)
                {
                    break;
                }

                if (IsIgnoredLine(line))
                    continue;

                AddUniqueName(names, line);
            }

            if (startMarkerFound && names.Count == 0)
            {
                throw new InvalidOperationException(
                    "The [CabinToolsPriority] section in Properties.txt " +
                    "contains no property names.");
            }

            return names;
        }

        private static void AddUniqueName(
            List<string> names,
            string propertyName)
        {
            foreach (string existingName in names)
            {
                if (string.Equals(
                    existingName,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            names.Add(propertyName);
        }

        private static string NormalizeLine(string line)
        {
            if (line == null)
                return string.Empty;

            return line
                .Trim()
                .TrimStart('\uFEFF');
        }

        private static bool IsIgnoredLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            return line.StartsWith("#") ||
                   line.StartsWith("//") ||
                   line.StartsWith(";");
        }
    }

    internal static class CabinPropertyService
    {
        public static bool IsSupportedDocument(
            IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return false;

            int documentType = modelDoc.GetType();

            return documentType ==
                       (int)swDocumentTypes_e.swDocPART ||
                   documentType ==
                       (int)swDocumentTypes_e.swDocASSEMBLY ||
                   documentType ==
                       (int)swDocumentTypes_e.swDocDRAWING;
        }

        public static string GetDocumentTypeName(
            IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return "Unknown";

            int documentType = modelDoc.GetType();

            if (documentType ==
                (int)swDocumentTypes_e.swDocPART)
            {
                return "Part";
            }

            if (documentType ==
                (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return "Assembly";
            }

            if (documentType ==
                (int)swDocumentTypes_e.swDocDRAWING)
            {
                return "Drawing";
            }

            return "Unsupported";
        }

        public static bool IsDrawing(IModelDoc2 modelDoc)
        {
            return modelDoc != null &&
                   modelDoc.GetType() ==
                       (int)swDocumentTypes_e.swDocDRAWING;
        }

        public static CabinNamingValues ReadNamingValues(
            IModelDoc2 modelDoc)
        {
            return new CabinNamingValues
            {
                DrwNumber = ReadResolvedProperty(
                    modelDoc,
                    CabinPropertyRules.DrwNumberProperty),

                Revision = ReadResolvedProperty(
                    modelDoc,
                    CabinPropertyRules.RevisionProperty),

                CabinTypeDescription = ReadResolvedProperty(
                    modelDoc,
                    CabinPropertyRules.CabinTypeDescriptionProperty),

                CabinTypeDefined = ReadResolvedProperty(
                    modelDoc,
                    CabinPropertyRules.CabinTypeDefinedProperty),

                LayoutType = ReadResolvedProperty(
                    modelDoc,
                    CabinPropertyRules.LayoutTypeProperty)
            };
        }

        public static void WriteNamingValues(
            IModelDoc2 modelDoc,
            CabinNamingValues values)
        {
            WriteTextProperty(
                modelDoc,
                CabinPropertyRules.DrwNumberProperty,
                values.DrwNumber);

            WriteTextProperty(
                modelDoc,
                CabinPropertyRules.RevisionProperty,
                values.Revision);

            WriteTextProperty(
                modelDoc,
                CabinPropertyRules.CabinTypeDescriptionProperty,
                values.CabinTypeDescription);

            WriteTextProperty(
                modelDoc,
                CabinPropertyRules.CabinTypeDefinedProperty,
                values.CabinTypeDefined);

            WriteTextProperty(
                modelDoc,
                CabinPropertyRules.LayoutTypeProperty,
                values.LayoutType);
        }

        public static PropertyCheckResult Analyze(
            IModelDoc2 modelDoc,
            PropertyOrderDefinition definition)
        {
            if (modelDoc == null)
            {
                throw new InvalidOperationException(
                    "No SOLIDWORKS document is available.");
            }

            if (definition == null ||
                definition.PriorityPropertyNames == null ||
                definition.PriorityPropertyNames.Count == 0)
            {
                throw new InvalidOperationException(
                    "The property order definition is empty.");
            }

            List<CustomPropertySnapshot> properties =
                ReadAllGeneralProperties(modelDoc);

            Dictionary<string, int> positions =
                BuildPositionLookup(properties);

            List<string> missingOrBlank =
                GetMissingOrBlankProperties(
                    modelDoc,
                    definition.PriorityPropertyNames);

            bool priorityOrderCorrect =
                IsPriorityOrderCorrect(
                    definition.PriorityPropertyNames,
                    positions);

            return new PropertyCheckResult
            {
                DocumentTypeName = GetDocumentTypeName(modelDoc),
                Definition = definition,
                Properties = properties,
                MissingOrBlankProperties = missingOrBlank,
                CurrentPositions = positions,
                PriorityOrderCorrect = priorityOrderCorrect,
                RepairBlockReason = GetRepairBlockReason(modelDoc)
            };
        }

        public static Dictionary<string, string> GetSuggestedValues(
            IModelDoc2 modelDoc,
            List<string> propertyNames)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string propertyName in propertyNames)
            {
                values[propertyName] =
                    ReadResolvedProperty(
                        modelDoc,
                        propertyName);
            }

            return values;
        }

        public static PropertyRepairResult RepairAndReorder(
            IModelDoc2 modelDoc,
            PropertyOrderDefinition definition,
            Dictionary<string, string> suppliedValues)
        {
            string repairBlockReason =
                GetRepairBlockReason(modelDoc);

            if (!string.IsNullOrWhiteSpace(repairBlockReason))
            {
                throw new InvalidOperationException(
                    repairBlockReason);
            }

            List<CustomPropertySnapshot> originalProperties =
                ReadAllGeneralProperties(modelDoc);

            List<CustomPropertySnapshot> workingProperties =
                CloneProperties(originalProperties);

            List<string> addedProperties =
                new List<string>();

            EnsurePriorityProperties(
                workingProperties,
                definition.PriorityPropertyNames,
                addedProperties);

            ApplyNonBlankSuppliedValues(
                workingProperties,
                suppliedValues);

            List<CustomPropertySnapshot> orderedProperties =
                BuildDesiredOrder(
                    workingProperties,
                    definition.PriorityPropertyNames);

            string backupFilePath = CreateBackupFile(
                modelDoc,
                definition,
                originalProperties,
                orderedProperties);

            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(modelDoc);

            try
            {
                DeleteProperties(
                    propertyManager,
                    originalProperties);

                AddProperties(
                    propertyManager,
                    orderedProperties);

                VerifyPriorityOrder(
                    modelDoc,
                    definition.PriorityPropertyNames);

                modelDoc.ForceRebuild3(false);

                return new PropertyRepairResult
                {
                    BackupFilePath = backupFilePath,
                    AddedProperties = addedProperties,
                    ReorderedPropertyCount = orderedProperties.Count
                };
            }
            catch (Exception ex)
            {
                bool restored =
                    TryRestoreOriginalProperties(
                        modelDoc,
                        originalProperties);

                string restoreMessage = restored
                    ? " The original property list was restored."
                    : " Automatic restoration failed. Use the backup file.";

                throw new InvalidOperationException(
                    "Property reorder failed." +
                    restoreMessage +
                    "\n\nBackup: " + backupFilePath +
                    "\n\nTechnical detail: " + ex.Message);
            }
        }

        public static string ReadResolvedProperty(
            IModelDoc2 modelDoc,
            string propertyName)
        {
            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(modelDoc);

            string value;
            string resolvedValue;
            bool wasResolved;
            bool linked;

            propertyManager.Get6(
                propertyName,
                false,
                out value,
                out resolvedValue,
                out wasResolved,
                out linked);

            if (!string.IsNullOrWhiteSpace(resolvedValue))
                return resolvedValue.Trim();

            return value == null
                ? string.Empty
                : value.Trim();
        }

        private static void WriteTextProperty(
            IModelDoc2 modelDoc,
            string propertyName,
            string propertyValue)
        {
            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(modelDoc);

            propertyManager.Add3(
                propertyName,
                (int)swCustomInfoType_e.swCustomInfoText,
                propertyValue ?? string.Empty,
                (int)swCustomPropertyAddOption_e
                    .swCustomPropertyReplaceValue);
        }

        private static List<string> GetMissingOrBlankProperties(
            IModelDoc2 modelDoc,
            List<string> priorityPropertyNames)
        {
            List<string> missingOrBlank =
                new List<string>();

            foreach (string propertyName in priorityPropertyNames)
            {
                string propertyValue =
                    ReadResolvedProperty(
                        modelDoc,
                        propertyName);

                if (string.IsNullOrWhiteSpace(propertyValue))
                {
                    missingOrBlank.Add(propertyName);
                }
            }

            return missingOrBlank;
        }

        private static Dictionary<string, int> BuildPositionLookup(
            List<CustomPropertySnapshot> properties)
        {
            Dictionary<string, int> positions =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < properties.Count; i++)
            {
                if (!positions.ContainsKey(properties[i].Name))
                {
                    positions.Add(properties[i].Name, i);
                }
            }

            return positions;
        }

        private static bool IsPriorityOrderCorrect(
            List<string> priorityPropertyNames,
            Dictionary<string, int> currentPositions)
        {
            for (int i = 0; i < priorityPropertyNames.Count; i++)
            {
                string propertyName = priorityPropertyNames[i];

                if (!currentPositions.ContainsKey(propertyName) ||
                    currentPositions[propertyName] != i)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<CustomPropertySnapshot>
            ReadAllGeneralProperties(IModelDoc2 modelDoc)
        {
            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(modelDoc);

            List<CustomPropertySnapshot> properties =
                new List<CustomPropertySnapshot>();

            object namesObject = propertyManager.GetNames();

            string[] names = namesObject as string[];

            if (names == null)
                return properties;

            for (int i = 0; i < names.Length; i++)
            {
                string propertyName = names[i];

                string rawValue;
                string resolvedValue;
                bool wasResolved;
                bool linked;

                propertyManager.Get6(
                    propertyName,
                    false,
                    out rawValue,
                    out resolvedValue,
                    out wasResolved,
                    out linked);

                int propertyType =
                    propertyManager.GetType2(propertyName);

                if (propertyType < 0)
                {
                    propertyType =
                        (int)swCustomInfoType_e.swCustomInfoText;
                }

                properties.Add(
                    new CustomPropertySnapshot
                    {
                        Name = propertyName,
                        Type = propertyType,
                        RawValue = rawValue ?? string.Empty,
                        OriginalIndex = i
                    });
            }

            return properties;
        }

        private static List<CustomPropertySnapshot>
            CloneProperties(
                List<CustomPropertySnapshot> properties)
        {
            List<CustomPropertySnapshot> clones =
                new List<CustomPropertySnapshot>();

            foreach (CustomPropertySnapshot property in properties)
            {
                clones.Add(property.Clone());
            }

            return clones;
        }

        private static void EnsurePriorityProperties(
            List<CustomPropertySnapshot> properties,
            List<string> priorityPropertyNames,
            List<string> addedProperties)
        {
            foreach (string propertyName in priorityPropertyNames)
            {
                if (FindSnapshot(properties, propertyName) != null)
                    continue;

                properties.Add(
                    new CustomPropertySnapshot
                    {
                        Name = propertyName,
                        Type =
                            (int)swCustomInfoType_e.swCustomInfoText,
                        RawValue = string.Empty,
                        OriginalIndex = int.MaxValue
                    });

                addedProperties.Add(propertyName);
            }
        }

        private static void ApplyNonBlankSuppliedValues(
            List<CustomPropertySnapshot> properties,
            Dictionary<string, string> suppliedValues)
        {
            if (suppliedValues == null)
                return;

            foreach (KeyValuePair<string, string> pair in
                suppliedValues)
            {
                string suppliedValue =
                    pair.Value == null
                        ? string.Empty
                        : pair.Value.Trim();

                if (string.IsNullOrWhiteSpace(suppliedValue))
                    continue;

                CustomPropertySnapshot snapshot =
                    FindSnapshot(properties, pair.Key);

                if (snapshot == null)
                {
                    snapshot = new CustomPropertySnapshot
                    {
                        Name = pair.Key,
                        Type =
                            (int)swCustomInfoType_e.swCustomInfoText,
                        RawValue = suppliedValue,
                        OriginalIndex = int.MaxValue
                    };

                    properties.Add(snapshot);
                }
                else
                {
                    snapshot.Type =
                        (int)swCustomInfoType_e.swCustomInfoText;

                    snapshot.RawValue = suppliedValue;
                }
            }
        }

        private static List<CustomPropertySnapshot>
            BuildDesiredOrder(
                List<CustomPropertySnapshot> properties,
                List<string> priorityPropertyNames)
        {
            List<CustomPropertySnapshot> ordered =
                new List<CustomPropertySnapshot>();

            foreach (string priorityPropertyName in
                priorityPropertyNames)
            {
                CustomPropertySnapshot snapshot =
                    FindSnapshot(
                        properties,
                        priorityPropertyName);

                if (snapshot != null)
                    ordered.Add(snapshot);
            }

            foreach (CustomPropertySnapshot snapshot in properties)
            {
                if (!ContainsReference(ordered, snapshot))
                    ordered.Add(snapshot);
            }

            return ordered;
        }

        private static bool ContainsReference(
            List<CustomPropertySnapshot> properties,
            CustomPropertySnapshot target)
        {
            foreach (CustomPropertySnapshot property in properties)
            {
                if (object.ReferenceEquals(property, target))
                    return true;
            }

            return false;
        }

        private static CustomPropertySnapshot FindSnapshot(
            List<CustomPropertySnapshot> properties,
            string propertyName)
        {
            foreach (CustomPropertySnapshot property in properties)
            {
                if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return property;
                }
            }

            return null;
        }

        private static void DeleteProperties(
            ICustomPropertyManager propertyManager,
            List<CustomPropertySnapshot> properties)
        {
            foreach (CustomPropertySnapshot property in properties)
            {
                propertyManager.Delete2(property.Name);
            }
        }

        private static void AddProperties(
            ICustomPropertyManager propertyManager,
            List<CustomPropertySnapshot> properties)
        {
            foreach (CustomPropertySnapshot property in properties)
            {
                propertyManager.Add3(
                    property.Name,
                    property.Type,
                    property.RawValue ?? string.Empty,
                    0);
            }
        }

        private static void VerifyPriorityOrder(
            IModelDoc2 modelDoc,
            List<string> priorityPropertyNames)
        {
            List<CustomPropertySnapshot> actualProperties =
                ReadAllGeneralProperties(modelDoc);

            if (actualProperties.Count < priorityPropertyNames.Count)
            {
                throw new InvalidOperationException(
                    "The recreated property list has fewer properties " +
                    "than the source priority list.");
            }

            for (int i = 0; i < priorityPropertyNames.Count; i++)
            {
                if (!string.Equals(
                    actualProperties[i].Name,
                    priorityPropertyNames[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS did not keep the requested " +
                        "priority-property order.");
                }
            }
        }

        private static bool TryRestoreOriginalProperties(
            IModelDoc2 modelDoc,
            List<CustomPropertySnapshot> originalProperties)
        {
            try
            {
                ICustomPropertyManager propertyManager =
                    GetGeneralPropertyManager(modelDoc);

                List<CustomPropertySnapshot> currentProperties =
                    ReadAllGeneralProperties(modelDoc);

                DeleteProperties(
                    propertyManager,
                    currentProperties);

                AddProperties(
                    propertyManager,
                    originalProperties);

                modelDoc.ForceRebuild3(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CreateBackupFile(
            IModelDoc2 modelDoc,
            PropertyOrderDefinition definition,
            List<CustomPropertySnapshot> originalProperties,
            List<CustomPropertySnapshot> reorderedProperties)
        {
            string backupDirectory = Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.MyDocuments),
                "CabinTools",
                "PropertyBackups");

            Directory.CreateDirectory(backupDirectory);

            string documentPath = modelDoc.GetPathName();

            string documentName =
                string.IsNullOrWhiteSpace(documentPath)
                    ? "UnsavedDocument"
                    : Path.GetFileNameWithoutExtension(documentPath);

            documentName =
                CabinPropertyRules.MakeSafeFileName(documentName);

            string backupFileName =
                documentName +
                "_PropertyBackup_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                ".txt";

            string backupFilePath = Path.Combine(
                backupDirectory,
                backupFileName);

            StringBuilder backup = new StringBuilder();

            backup.AppendLine(
                "Cabin Tools Property Checker Backup");

            backup.AppendLine(
                "Created: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            backup.AppendLine(
                "Document: " + documentPath);

            backup.AppendLine(
                "Property-order source: " +
                definition.SourcePath);

            backup.AppendLine();

            backup.AppendLine(
                "ORIGINAL GENERAL PROPERTY ORDER");

            AppendProperties(
                backup,
                originalProperties);

            backup.AppendLine();

            backup.AppendLine(
                "NEW GENERAL PROPERTY ORDER");

            AppendProperties(
                backup,
                reorderedProperties);

            File.WriteAllText(
                backupFilePath,
                backup.ToString());

            return backupFilePath;
        }

        private static void AppendProperties(
            StringBuilder builder,
            List<CustomPropertySnapshot> properties)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                CustomPropertySnapshot property = properties[i];

                string safeValue =
                    (property.RawValue ?? string.Empty)
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");

                builder.AppendLine(
                    (i + 1).ToString() + ". " +
                    property.Name +
                    " | Type: " +
                    property.Type.ToString() +
                    " | Value: " +
                    safeValue);
            }
        }

        private static string GetRepairBlockReason(
            IModelDoc2 modelDoc)
        {
            string documentPath = modelDoc.GetPathName();

            if (string.IsNullOrWhiteSpace(documentPath))
            {
                return
                    "Save the document first. Reorder + Repair is " +
                    "blocked for unsaved documents.";
            }

            if (!File.Exists(documentPath))
            {
                return
                    "The document file does not exist at its current " +
                    "path. Save or reopen it before repairing.";
            }

            FileAttributes attributes =
                File.GetAttributes(documentPath);

            if ((attributes & FileAttributes.ReadOnly) ==
                FileAttributes.ReadOnly)
            {
                return
                    "The document file is read-only. In SOLIDWORKS PDM, " +
                    "check out the file before using Reorder + Repair.";
            }

            return string.Empty;
        }

        private static ICustomPropertyManager
            GetGeneralPropertyManager(
                IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
            {
                throw new InvalidOperationException(
                    "No SOLIDWORKS document is available.");
            }

            IModelDocExtension extension =
                modelDoc.Extension;

            if (extension == null)
            {
                throw new InvalidOperationException(
                    "Could not access the document extension.");
            }

            ICustomPropertyManager propertyManager =
                extension.CustomPropertyManager[""];

            if (propertyManager == null)
            {
                throw new InvalidOperationException(
                    "Could not access general custom properties.");
            }

            return propertyManager;
        }
    }
}
