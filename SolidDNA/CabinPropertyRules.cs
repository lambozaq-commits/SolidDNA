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

    internal sealed class PropertyCheckResult
    {
        public CabinNamingValues NamingValues { get; set; }
        public List<CustomPropertySnapshot> Properties { get; set; }
        public List<string> MissingPromptProperties { get; set; }

        public string ExpectedTitle2 { get; set; }
        public string ExpectedTitle3 { get; set; }
        public string CurrentTitle2 { get; set; }
        public string CurrentTitle3 { get; set; }

        public bool Title2Synchronized { get; set; }
        public bool Title3Synchronized { get; set; }
        public bool PriorityOrderCorrect { get; set; }

        public Dictionary<string, int> CurrentPositions { get; set; }
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
                "General custom-property count: " +
                Properties.Count);

            report.AppendLine();

            report.AppendLine("PDF / TITLE SOURCE PROPERTIES");
            report.AppendLine(
                "DrwNumber: " +
                CabinPropertyRules.DisplayValue(
                    NamingValues.DrwNumber));

            report.AppendLine(
                "Revision: " +
                CabinPropertyRules.DisplayValue(
                    NamingValues.Revision));

            report.AppendLine(
                "Cabin type description: " +
                CabinPropertyRules.DisplayValue(
                    NamingValues.CabinTypeDescription));

            report.AppendLine(
                "Cabin type defined: " +
                CabinPropertyRules.DisplayValue(
                    NamingValues.CabinTypeDefined));

            report.AppendLine(
                "Layout Type: " +
                CabinPropertyRules.DisplayValue(
                    NamingValues.LayoutType));

            report.AppendLine();

            if (MissingPromptProperties.Count == 0)
            {
                report.AppendLine(
                    "Required priority input properties: OK");
            }
            else
            {
                report.AppendLine(
                    "Missing or blank priority input properties:");

                foreach (string propertyName in
                    MissingPromptProperties)
                {
                    report.AppendLine("  - " + propertyName);
                }
            }

            report.AppendLine();
            report.AppendLine("DERIVED TITLE PROPERTIES");

            report.AppendLine(
                "Title2 expected: " +
                CabinPropertyRules.DisplayValue(
                    ExpectedTitle2));

            report.AppendLine(
                "Title2 current:  " +
                CabinPropertyRules.DisplayValue(
                    CurrentTitle2));

            report.AppendLine(
                "Title2 state: " +
                (Title2Synchronized
                    ? "Synchronized"
                    : "Needs repair"));

            report.AppendLine();

            report.AppendLine(
                "Title3 expected: " +
                CabinPropertyRules.DisplayValue(
                    ExpectedTitle3));

            report.AppendLine(
                "Title3 current:  " +
                CabinPropertyRules.DisplayValue(
                    CurrentTitle3));

            report.AppendLine(
                "Title3 state: " +
                (Title3Synchronized
                    ? "Synchronized"
                    : "Needs repair"));

            report.AppendLine();
            report.AppendLine("REQUESTED PRIORITY ORDER");

            for (int i = 0;
                i < CabinPropertyRules.PriorityPropertyNames.Length;
                i++)
            {
                string propertyName =
                    CabinPropertyRules.PriorityPropertyNames[i];

                int position = -1;

                if (CurrentPositions.ContainsKey(propertyName))
                    position = CurrentPositions[propertyName];

                string positionText =
                    position >= 0
                        ? (position + 1).ToString()
                        : "missing";

                report.AppendLine(
                    (i + 1) + ". " +
                    propertyName +
                    " — current position: " +
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
                report.AppendLine(
                    "Repair status: Ready.");

                report.AppendLine(
                    "Reorder + Repair will ask for missing values, " +
                    "create a local backup, recreate the general " +
                    "custom-property list, and apply the requested order.");
            }
            else
            {
                report.AppendLine(
                    "Repair status: Blocked.");

                report.AppendLine(RepairBlockReason);
            }

            report.AppendLine();
            report.AppendLine(
                "Configuration-specific custom properties are not changed.");

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
        public const string LayoutTypeProperty = "Layout Type";

        public const string Title2Property = "Title2";
        public const string Title3Property = "Title3";

        public const string DescriptionProperty = "Description";
        public const string CheckedByProperty = "CheckedBy";
        public const string CheckedDateProperty = "CheckedDate";
        public const string ApprovedByProperty = "ApprovedBy";
        public const string ClientProperty = "Client";
        public const string ProjectProperty = "PROJECT";
        public const string ProjectNumberProperty = "Project Number";
        public const string ProjectTypeProperty = "Project_type";
        public const string StatusProperty = "Status";
        public const string Approved00Property = "APPROVED_00";
        public const string Date00Property = "DATE_00";
        public const string Date00AppProperty = "DATE_00_APP";
        public const string DocIdProperty = "Doc_ID";
        public const string SwFileNameProperty = "SW-File Name";

        public static readonly string[] PdfSourcePropertyNames =
        {
            DrwNumberProperty,
            RevisionProperty,
            CabinTypeDescriptionProperty,
            CabinTypeDefinedProperty,
            LayoutTypeProperty
        };

        // These are the fields shown in the missing-property prompt.
        // Title2 and Title3 are intentionally excluded because the add-in derives them.
        public static readonly string[] PromptPropertyNames =
        {
            DrwNumberProperty,
            RevisionProperty,
            CabinTypeDescriptionProperty,
            CabinTypeDefinedProperty,
            LayoutTypeProperty,
            DescriptionProperty,
            CheckedByProperty,
            CheckedDateProperty,
            ApprovedByProperty,
            ClientProperty,
            ProjectProperty,
            ProjectNumberProperty,
            ProjectTypeProperty,
            StatusProperty,
            Approved00Property,
            Date00Property,
            Date00AppProperty,
            DocIdProperty,
            SwFileNameProperty
        };

        public static readonly string[] PriorityPropertyNames =
        {
            DrwNumberProperty,
            RevisionProperty,
            CabinTypeDescriptionProperty,
            CabinTypeDefinedProperty,
            LayoutTypeProperty,
            Title2Property,
            Title3Property,
            DescriptionProperty,
            CheckedByProperty,
            CheckedDateProperty,
            ApprovedByProperty,
            ClientProperty,
            ProjectProperty,
            ProjectNumberProperty,
            ProjectTypeProperty,
            StatusProperty,
            Approved00Property,
            Date00Property,
            Date00AppProperty,
            DocIdProperty,
            SwFileNameProperty
        };

        public static string BuildTitle2(
            string cabinTypeDescription)
        {
            return Clean(cabinTypeDescription);
        }

        public static string BuildTitle3(
            string cabinTypeDefined,
            string layoutType)
        {
            string definedValue = Clean(cabinTypeDefined);
            string layoutValue = Clean(layoutType);

            if (string.IsNullOrWhiteSpace(definedValue))
                return layoutValue;

            if (string.IsNullOrWhiteSpace(layoutValue))
                return definedValue;

            return definedValue + " - " + layoutValue;
        }

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

        public static List<string> GetMissingPdfSourceProperties(
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

            if (string.IsNullOrWhiteSpace(
                values.LayoutType))
            {
                missing.Add(LayoutTypeProperty);
            }

            return missing;
        }

        public static string MakeSafeFileName(string fileName)
        {
            char[] invalidCharacters =
                Path.GetInvalidFileNameChars();

            foreach (char invalidCharacter in
                invalidCharacters)
            {
                fileName = fileName.Replace(
                    invalidCharacter,
                    '_');
            }

            return fileName.Trim();
        }

        public static string DisplayValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<blank>";

            return value;
        }

        public static string Clean(string value)
        {
            return value == null
                ? string.Empty
                : value.Trim();
        }
    }

    internal static class CabinPropertyService
    {
        public static CabinNamingValues ReadSourceValues(
            IModelDoc2 drawingDoc)
        {
            return new CabinNamingValues
            {
                DrwNumber = ReadResolvedProperty(
                    drawingDoc,
                    CabinPropertyRules.DrwNumberProperty),

                Revision = ReadResolvedProperty(
                    drawingDoc,
                    CabinPropertyRules.RevisionProperty),

                CabinTypeDescription = ReadResolvedProperty(
                    drawingDoc,
                    CabinPropertyRules
                        .CabinTypeDescriptionProperty),

                CabinTypeDefined = ReadResolvedProperty(
                    drawingDoc,
                    CabinPropertyRules
                        .CabinTypeDefinedProperty),

                LayoutType = ReadResolvedProperty(
                    drawingDoc,
                    CabinPropertyRules.LayoutTypeProperty)
            };
        }

        public static void WriteSourceValues(
            IModelDoc2 drawingDoc,
            CabinNamingValues values)
        {
            WritePropertyValuePreservingType(
                drawingDoc,
                CabinPropertyRules.DrwNumberProperty,
                values.DrwNumber);

            WritePropertyValuePreservingType(
                drawingDoc,
                CabinPropertyRules.RevisionProperty,
                values.Revision);

            WritePropertyValuePreservingType(
                drawingDoc,
                CabinPropertyRules.CabinTypeDescriptionProperty,
                values.CabinTypeDescription);

            WritePropertyValuePreservingType(
                drawingDoc,
                CabinPropertyRules.CabinTypeDefinedProperty,
                values.CabinTypeDefined);

            WritePropertyValuePreservingType(
                drawingDoc,
                CabinPropertyRules.LayoutTypeProperty,
                values.LayoutType);
        }

        public static bool SynchronizeDerivedTitleProperties(
            IModelDoc2 drawingDoc)
        {
            CabinNamingValues values =
                ReadSourceValues(drawingDoc);

            string expectedTitle2 =
                CabinPropertyRules.BuildTitle2(
                    values.CabinTypeDescription);

            string expectedTitle3 =
                CabinPropertyRules.BuildTitle3(
                    values.CabinTypeDefined,
                    values.LayoutType);

            bool title2Changed = WriteTextPropertyIfChanged(
                drawingDoc,
                CabinPropertyRules.Title2Property,
                expectedTitle2);

            bool title3Changed = WriteTextPropertyIfChanged(
                drawingDoc,
                CabinPropertyRules.Title3Property,
                expectedTitle3);

            return title2Changed || title3Changed;
        }

        public static Dictionary<string, string>
            GetSuggestedMissingValues(
                IModelDoc2 drawingDoc,
                List<string> missingProperties)
        {
            Dictionary<string, string> suggestions =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string propertyName in missingProperties)
            {
                string suggestedValue =
                    ReadResolvedProperty(
                        drawingDoc,
                        propertyName);

                if (string.IsNullOrWhiteSpace(suggestedValue) &&
                    string.Equals(
                        propertyName,
                        CabinPropertyRules.SwFileNameProperty,
                        StringComparison.OrdinalIgnoreCase))
                {
                    string drawingPath =
                        drawingDoc.GetPathName();

                    if (!string.IsNullOrWhiteSpace(drawingPath))
                    {
                        suggestedValue =
                            Path.GetFileNameWithoutExtension(
                                drawingPath);
                    }
                }

                suggestions[propertyName] =
                    suggestedValue ?? string.Empty;
            }

            return suggestions;
        }

        public static PropertyCheckResult Analyze(
            IModelDoc2 drawingDoc)
        {
            List<CustomPropertySnapshot> properties =
                ReadAllProperties(drawingDoc);

            CabinNamingValues values =
                ReadSourceValues(drawingDoc);

            string expectedTitle2 =
                CabinPropertyRules.BuildTitle2(
                    values.CabinTypeDescription);

            string expectedTitle3 =
                CabinPropertyRules.BuildTitle3(
                    values.CabinTypeDefined,
                    values.LayoutType);

            string currentTitle2 = ReadResolvedProperty(
                drawingDoc,
                CabinPropertyRules.Title2Property);

            string currentTitle3 = ReadResolvedProperty(
                drawingDoc,
                CabinPropertyRules.Title3Property);

            Dictionary<string, int> positions =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < properties.Count; i++)
            {
                if (!positions.ContainsKey(properties[i].Name))
                    positions.Add(properties[i].Name, i);
            }

            bool priorityOrderCorrect = true;

            for (int i = 0;
                i < CabinPropertyRules.PriorityPropertyNames.Length;
                i++)
            {
                string propertyName =
                    CabinPropertyRules.PriorityPropertyNames[i];

                if (!positions.ContainsKey(propertyName) ||
                    positions[propertyName] != i)
                {
                    priorityOrderCorrect = false;
                    break;
                }
            }

            bool hasTitle2 = positions.ContainsKey(
                CabinPropertyRules.Title2Property);

            bool hasTitle3 = positions.ContainsKey(
                CabinPropertyRules.Title3Property);

            List<string> missingPromptProperties =
                new List<string>();

            foreach (string propertyName in
                CabinPropertyRules.PromptPropertyNames)
            {
                string propertyValue =
                    ReadResolvedProperty(
                        drawingDoc,
                        propertyName);

                if (string.IsNullOrWhiteSpace(propertyValue))
                    missingPromptProperties.Add(propertyName);
            }

            return new PropertyCheckResult
            {
                NamingValues = values,
                Properties = properties,
                MissingPromptProperties =
                    missingPromptProperties,

                ExpectedTitle2 = expectedTitle2,
                ExpectedTitle3 = expectedTitle3,

                CurrentTitle2 = currentTitle2,
                CurrentTitle3 = currentTitle3,

                Title2Synchronized =
                    hasTitle2 &&
                    string.Equals(
                        currentTitle2,
                        expectedTitle2,
                        StringComparison.Ordinal),

                Title3Synchronized =
                    hasTitle3 &&
                    string.Equals(
                        currentTitle3,
                        expectedTitle3,
                        StringComparison.Ordinal),

                PriorityOrderCorrect = priorityOrderCorrect,
                CurrentPositions = positions,
                RepairBlockReason =
                    GetRepairBlockReason(drawingDoc)
            };
        }

        public static PropertyRepairResult RepairAndReorder(
            IModelDoc2 drawingDoc,
            Dictionary<string, string> suppliedValues)
        {
            string repairBlockReason =
                GetRepairBlockReason(drawingDoc);

            if (!string.IsNullOrWhiteSpace(repairBlockReason))
            {
                throw new InvalidOperationException(
                    repairBlockReason);
            }

            if (suppliedValues == null)
            {
                suppliedValues =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
            }

            List<CustomPropertySnapshot> originalProperties =
                ReadAllProperties(drawingDoc);

            List<CustomPropertySnapshot> workingProperties =
                CloneProperties(originalProperties);

            List<string> addedProperties =
                new List<string>();

            MergeSuppliedValuesIntoSnapshots(
                workingProperties,
                suppliedValues,
                addedProperties);

            CabinNamingValues namingValues =
                ReadSourceValues(drawingDoc);

            ApplySuppliedValuesToNamingValues(
                namingValues,
                suppliedValues);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.DrwNumberProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.RevisionProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.CabinTypeDescriptionProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.CabinTypeDefinedProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.LayoutTypeProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.DescriptionProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.CheckedByProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.CheckedDateProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.ApprovedByProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.ClientProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.ProjectProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.ProjectNumberProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.ProjectTypeProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.StatusProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.Approved00Property,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.Date00Property,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.Date00AppProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.DocIdProperty,
                string.Empty,
                addedProperties);

            EnsureSnapshot(
                workingProperties,
                CabinPropertyRules.SwFileNameProperty,
                string.Empty,
                addedProperties);

            string expectedTitle2 =
                CabinPropertyRules.BuildTitle2(
                    namingValues.CabinTypeDescription);

            string expectedTitle3 =
                CabinPropertyRules.BuildTitle3(
                    namingValues.CabinTypeDefined,
                    namingValues.LayoutType);

            EnsureAndUpdateTextSnapshot(
                workingProperties,
                CabinPropertyRules.Title2Property,
                expectedTitle2,
                addedProperties);

            EnsureAndUpdateTextSnapshot(
                workingProperties,
                CabinPropertyRules.Title3Property,
                expectedTitle3,
                addedProperties);

            List<CustomPropertySnapshot> orderedProperties =
                BuildDesiredOrder(workingProperties);

            string backupFilePath = CreateBackupFile(
                drawingDoc,
                originalProperties,
                orderedProperties);

            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(drawingDoc);

            try
            {
                DeleteProperties(
                    propertyManager,
                    originalProperties);

                AddProperties(
                    propertyManager,
                    orderedProperties);

                VerifyPropertyOrder(
                    drawingDoc,
                    orderedProperties);

                drawingDoc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                bool restored = TryRestoreOriginalProperties(
                    drawingDoc,
                    originalProperties);

                string restoreMessage = restored
                    ? " The original property list was restored."
                    : " Automatic restoration failed. Use the backup report.";

                throw new InvalidOperationException(
                    "Property reorder failed." +
                    restoreMessage +
                    "\n\nBackup: " +
                    backupFilePath +
                    "\n\nTechnical detail: " +
                    ex.Message);
            }

            return new PropertyRepairResult
            {
                BackupFilePath = backupFilePath,
                AddedProperties = addedProperties,
                ReorderedPropertyCount =
                    orderedProperties.Count
            };
        }

        public static string ReadResolvedProperty(
            IModelDoc2 drawingDoc,
            string propertyName)
        {
            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(drawingDoc);

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

        private static void ApplySuppliedValuesToNamingValues(
            CabinNamingValues namingValues,
            Dictionary<string, string> suppliedValues)
        {
            string value;

            if (suppliedValues.TryGetValue(
                CabinPropertyRules.DrwNumberProperty,
                out value))
            {
                namingValues.DrwNumber = value;
            }

            if (suppliedValues.TryGetValue(
                CabinPropertyRules.RevisionProperty,
                out value))
            {
                namingValues.Revision = value;
            }

            if (suppliedValues.TryGetValue(
                CabinPropertyRules.CabinTypeDescriptionProperty,
                out value))
            {
                namingValues.CabinTypeDescription = value;
            }

            if (suppliedValues.TryGetValue(
                CabinPropertyRules.CabinTypeDefinedProperty,
                out value))
            {
                namingValues.CabinTypeDefined = value;
            }

            if (suppliedValues.TryGetValue(
                CabinPropertyRules.LayoutTypeProperty,
                out value))
            {
                namingValues.LayoutType = value;
            }
        }

        private static void MergeSuppliedValuesIntoSnapshots(
            List<CustomPropertySnapshot> properties,
            Dictionary<string, string> suppliedValues,
            List<string> addedProperties)
        {
            foreach (KeyValuePair<string, string> pair in
                suppliedValues)
            {
                CustomPropertySnapshot existing =
                    FindSnapshot(properties, pair.Key);

                if (existing == null)
                {
                    properties.Add(
                        new CustomPropertySnapshot
                        {
                            Name = pair.Key,
                            Type =
                                (int)swCustomInfoType_e
                                    .swCustomInfoText,
                            RawValue = pair.Value ?? string.Empty,
                            OriginalIndex = int.MaxValue
                        });

                    addedProperties.Add(pair.Key);
                }
                else
                {
                    existing.RawValue =
                        pair.Value ?? string.Empty;
                }
            }
        }

        private static void WritePropertyValuePreservingType(
            IModelDoc2 drawingDoc,
            string propertyName,
            string propertyValue)
        {
            List<CustomPropertySnapshot> properties =
                ReadAllProperties(drawingDoc);

            CustomPropertySnapshot existing =
                FindSnapshot(properties, propertyName);

            int propertyType =
                existing == null
                    ? (int)swCustomInfoType_e.swCustomInfoText
                    : existing.Type;

            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(drawingDoc);

            propertyManager.Add3(
                propertyName,
                propertyType,
                propertyValue ?? string.Empty,
                (int)swCustomPropertyAddOption_e
                    .swCustomPropertyReplaceValue);
        }

        private static bool WriteTextPropertyIfChanged(
            IModelDoc2 drawingDoc,
            string propertyName,
            string expectedValue)
        {
            List<CustomPropertySnapshot> properties =
                ReadAllProperties(drawingDoc);

            CustomPropertySnapshot existing =
                FindSnapshot(properties, propertyName);

            if (existing != null &&
                existing.Type ==
                    (int)swCustomInfoType_e.swCustomInfoText &&
                string.Equals(
                    existing.RawValue ?? string.Empty,
                    expectedValue ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(drawingDoc);

            propertyManager.Add3(
                propertyName,
                (int)swCustomInfoType_e.swCustomInfoText,
                expectedValue ?? string.Empty,
                (int)swCustomPropertyAddOption_e
                    .swCustomPropertyReplaceValue);

            return true;
        }

        private static List<CustomPropertySnapshot>
            ReadAllProperties(IModelDoc2 drawingDoc)
        {
            ICustomPropertyManager propertyManager =
                GetGeneralPropertyManager(drawingDoc);

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

            foreach (CustomPropertySnapshot property in
                properties)
            {
                clones.Add(property.Clone());
            }

            return clones;
        }

        private static void EnsureSnapshot(
            List<CustomPropertySnapshot> properties,
            string propertyName,
            string defaultValue,
            List<string> addedProperties)
        {
            if (FindSnapshot(properties, propertyName) != null)
                return;

            properties.Add(
                new CustomPropertySnapshot
                {
                    Name = propertyName,
                    Type =
                        (int)swCustomInfoType_e.swCustomInfoText,
                    RawValue = defaultValue ?? string.Empty,
                    OriginalIndex = int.MaxValue
                });

            addedProperties.Add(propertyName);
        }

        private static void EnsureAndUpdateTextSnapshot(
            List<CustomPropertySnapshot> properties,
            string propertyName,
            string propertyValue,
            List<string> addedProperties)
        {
            CustomPropertySnapshot snapshot =
                FindSnapshot(properties, propertyName);

            if (snapshot == null)
            {
                snapshot =
                    new CustomPropertySnapshot
                    {
                        Name = propertyName,
                        Type =
                            (int)swCustomInfoType_e
                                .swCustomInfoText,
                        RawValue = propertyValue ?? string.Empty,
                        OriginalIndex = int.MaxValue
                    };

                properties.Add(snapshot);
                addedProperties.Add(propertyName);
                return;
            }

            snapshot.Type =
                (int)swCustomInfoType_e.swCustomInfoText;

            snapshot.RawValue = propertyValue ?? string.Empty;
        }

        private static List<CustomPropertySnapshot>
            BuildDesiredOrder(
                List<CustomPropertySnapshot> properties)
        {
            List<CustomPropertySnapshot> ordered =
                new List<CustomPropertySnapshot>();

            foreach (string priorityPropertyName in
                CabinPropertyRules.PriorityPropertyNames)
            {
                CustomPropertySnapshot snapshot =
                    FindSnapshot(
                        properties,
                        priorityPropertyName);

                if (snapshot != null)
                    ordered.Add(snapshot);
            }

            foreach (CustomPropertySnapshot snapshot in
                properties)
            {
                if (!ContainsSnapshot(ordered, snapshot))
                    ordered.Add(snapshot);
            }

            return ordered;
        }

        private static bool ContainsSnapshot(
            List<CustomPropertySnapshot> properties,
            CustomPropertySnapshot snapshot)
        {
            foreach (CustomPropertySnapshot property in
                properties)
            {
                if (object.ReferenceEquals(property, snapshot))
                    return true;
            }

            return false;
        }

        private static CustomPropertySnapshot FindSnapshot(
            List<CustomPropertySnapshot> properties,
            string propertyName)
        {
            foreach (CustomPropertySnapshot property in
                properties)
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
            foreach (CustomPropertySnapshot property in
                properties)
            {
                propertyManager.Delete2(property.Name);
            }
        }

        private static void AddProperties(
            ICustomPropertyManager propertyManager,
            List<CustomPropertySnapshot> properties)
        {
            foreach (CustomPropertySnapshot property in
                properties)
            {
                propertyManager.Add3(
                    property.Name,
                    property.Type,
                    property.RawValue ?? string.Empty,
                    0);
            }
        }

        private static void VerifyPropertyOrder(
            IModelDoc2 drawingDoc,
            List<CustomPropertySnapshot> expectedProperties)
        {
            List<CustomPropertySnapshot> actualProperties =
                ReadAllProperties(drawingDoc);

            if (actualProperties.Count != expectedProperties.Count)
            {
                throw new InvalidOperationException(
                    "The recreated property count does not match " +
                    "the expected count.");
            }

            for (int i = 0; i < expectedProperties.Count; i++)
            {
                if (!string.Equals(
                    actualProperties[i].Name,
                    expectedProperties[i].Name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS did not keep the requested " +
                        "custom-property order.");
                }
            }
        }

        private static bool TryRestoreOriginalProperties(
            IModelDoc2 drawingDoc,
            List<CustomPropertySnapshot> originalProperties)
        {
            try
            {
                ICustomPropertyManager propertyManager =
                    GetGeneralPropertyManager(drawingDoc);

                List<CustomPropertySnapshot> currentProperties =
                    ReadAllProperties(drawingDoc);

                DeleteProperties(
                    propertyManager,
                    currentProperties);

                AddProperties(
                    propertyManager,
                    originalProperties);

                drawingDoc.ForceRebuild3(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CreateBackupFile(
            IModelDoc2 drawingDoc,
            List<CustomPropertySnapshot> originalProperties,
            List<CustomPropertySnapshot> reorderedProperties)
        {
            string backupDirectory = Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.MyDocuments),
                "CabinTools",
                "PropertyBackups");

            Directory.CreateDirectory(backupDirectory);

            string drawingPath = drawingDoc.GetPathName();

            string drawingName =
                string.IsNullOrWhiteSpace(drawingPath)
                    ? "UnsavedDrawing"
                    : Path.GetFileNameWithoutExtension(
                        drawingPath);

            drawingName =
                CabinPropertyRules.MakeSafeFileName(
                    drawingName);

            string backupFileName =
                drawingName +
                "_PropertyBackup_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                ".txt";

            string backupFilePath = Path.Combine(
                backupDirectory,
                backupFileName);

            StringBuilder backup = new StringBuilder();

            backup.AppendLine(
                "Cabin Tools Property Reorder Backup");

            backup.AppendLine(
                "Created: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            backup.AppendLine(
                "Drawing: " + drawingPath);

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
                CustomPropertySnapshot property =
                    properties[i];

                string safeValue =
                    (property.RawValue ?? string.Empty)
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");

                builder.AppendLine(
                    (i + 1) + ". " +
                    property.Name +
                    " | Type: " +
                    property.Type +
                    " | Value: " +
                    safeValue);
            }
        }

        private static string GetRepairBlockReason(
            IModelDoc2 drawingDoc)
        {
            string drawingPath = drawingDoc.GetPathName();

            if (string.IsNullOrWhiteSpace(drawingPath))
            {
                return
                    "Save the drawing first. Reorder + Repair is " +
                    "blocked for unsaved drawings.";
            }

            if (!File.Exists(drawingPath))
            {
                return
                    "The drawing file does not exist at its current " +
                    "path. Save or reopen it before repairing.";
            }

            FileAttributes attributes =
                File.GetAttributes(drawingPath);

            if ((attributes & FileAttributes.ReadOnly) ==
                FileAttributes.ReadOnly)
            {
                return
                    "The drawing file is read-only. In SOLIDWORKS PDM, " +
                    "check out the drawing before using Reorder + Repair.";
            }

            return string.Empty;
        }

        private static ICustomPropertyManager
            GetGeneralPropertyManager(
                IModelDoc2 drawingDoc)
        {
            if (drawingDoc == null)
            {
                throw new InvalidOperationException(
                    "No drawing document is available.");
            }

            IModelDocExtension extension =
                drawingDoc.Extension;

            if (extension == null)
            {
                throw new InvalidOperationException(
                    "Could not access the drawing document extension.");
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
