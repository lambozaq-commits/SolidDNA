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

using SwEnvironment =
    CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// Shows a controlled form for assigning cut-list properties to profile
    /// cut-list items. This command deliberately does not auto-detect Top
    /// Profile or Bottom Profile. The user explicitly chooses the profile type
    /// per cut-list item or applies one selection to all/checked rows.
    /// </summary>
    internal static class CutListProfilePropertyCommand
    {
        internal const string DescriptionPropertyName = "Description";
        internal const string BrandPropertyName = "Brand";
        internal const string ModelPropertyName = "Model";

        internal const string TopProfileName = "Top Profile";
        internal const string BottomProfileName = "Bottom Profile";
        internal const string CustomProfileName = "Custom";
        internal const string SkipProfileName = "Skip";

        internal const string TopDescriptionValue = "Top Profile";
        internal const string BottomDescriptionValue = "Bottom Profile";
        internal const string StandardBrandValue = "SBA";
        internal const string StandardModelValue = "Type 121";

        public static void UpdateActivePartTopBottomProfiles()
        {
            ShowCutListProfilePropertyForm();
        }

        public static void ShowCutListProfilePropertyForm()
        {
            try
            {
                IModelDoc2 modelDoc =
                    CabinCustomPropertyStore
                        .GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage(
                        "Open the weldment/profile part before running this command.",
                        MessageBoxIcon.Warning);
                    return;
                }

                if (modelDoc.GetType() !=
                    (int)swDocumentTypes_e.swDocPART)
                {
                    ShowMessage(
                        "This command works only on an active part document.\r\n\r\n" +
                        "Open the wall/ceiling profile weldment part, then run the command again.",
                        MessageBoxIcon.Warning);
                    return;
                }

                string writeBlockReason =
                    CabinCustomPropertyStore
                        .GetWriteBlockReason(modelDoc);

                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(
                        writeBlockReason,
                        MessageBoxIcon.Warning);
                    return;
                }

                using (CutListProfilePropertyForm form =
                    new CutListProfilePropertyForm(modelDoc))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowMessage(
                    "Cabin Tools could not open the cut-list property tool.\r\n\r\n" +
                    ex.Message,
                    MessageBoxIcon.Error);
            }
        }

        internal static List<CutListItemInfo> GetCutListItems(
            IModelDoc2 modelDoc,
            List<string> messages)
        {
            List<CutListItemInfo> cutListItems =
                new List<CutListItemInfo>();

            if (modelDoc == null)
                return cutListItems;

            PartDoc partDoc =
                modelDoc as PartDoc;

            if (partDoc == null)
                return cutListItems;

            TryUpdateCutList(
                modelDoc,
                messages,
                "before reading cut-list items");

            try
            {
                modelDoc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                if (messages != null)
                {
                    messages.Add(
                        "Force rebuild before reading cut list failed: " +
                        ex.Message);
                }
            }

            Feature rootFeature =
                modelDoc.FirstFeature() as Feature;

            TraverseFeatures(
                rootFeature,
                cutListItems,
                0,
                false);

            return cutListItems;
        }

        internal static CutListWriteReport ApplyRows(
            IModelDoc2 modelDoc,
            IList<CutListGridRow> rows)
        {
            if (modelDoc == null)
            {
                throw new InvalidOperationException(
                    "No active SOLIDWORKS part document was supplied.");
            }

            PartDoc partDoc =
                modelDoc as PartDoc;

            if (partDoc == null)
            {
                throw new InvalidOperationException(
                    "The active document is not a valid SOLIDWORKS part document.");
            }

            CutListWriteReport report =
                new CutListWriteReport();

            report.DocumentTitle =
                modelDoc.GetTitle() ?? string.Empty;
            report.DocumentPath =
                modelDoc.GetPathName() ?? string.Empty;

            if (rows == null || rows.Count == 0)
            {
                report.GeneralMessages.Add(
                    "No cut-list rows were supplied for update.");
                return report;
            }

            foreach (CutListGridRow row in rows)
            {
                if (row == null)
                    continue;

                if (!row.Apply)
                {
                    row.Status = "Skipped";
                    report.SkippedRows.Add(row.CloneForReport());
                    continue;
                }

                if (row.Item == null ||
                    row.Item.PropertyManager == null)
                {
                    row.Status = "No property manager";
                    report.FailedRows.Add(row.CloneForReport());
                    continue;
                }

                bool writesSomething = false;

                string profileType =
                    NormalizeProfileType(row.ProfileType);

                if (!string.Equals(
                        profileType,
                        SkipProfileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(row.Description))
                    {
                        row.Status = "! DESCRIPTION is blank";
                        report.FailedRows.Add(row.CloneForReport());
                        continue;
                    }

                    SetCutListTextProperty(
                        row.Item.PropertyManager,
                        DescriptionPropertyName,
                        row.Description);

                    SetCutListTextProperty(
                        row.Item.PropertyManager,
                        BrandPropertyName,
                        row.Brand);

                    SetCutListTextProperty(
                        row.Item.PropertyManager,
                        ModelPropertyName,
                        row.Model);

                    writesSomething = true;
                }

                List<KeyValuePair<string, string>> extraProperties =
                    ParseExtraProperties(
                        row.ExtraProperties,
                        row,
                        report);

                foreach (KeyValuePair<string, string> property in
                    extraProperties)
                {
                    SetCutListTextProperty(
                        row.Item.PropertyManager,
                        property.Key,
                        property.Value);

                    writesSomething = true;
                }

                if (writesSomething)
                {
                    row.Status = "Updated";
                    report.UpdatedRows.Add(row.CloneForReport());
                }
                else
                {
                    row.Status = "Skipped - no values selected";
                    report.SkippedRows.Add(row.CloneForReport());
                }
            }

            TryUpdateCutList(
                modelDoc,
                report.GeneralMessages,
                "after writing cut-list properties");

            try
            {
                modelDoc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                report.GeneralMessages.Add(
                    "Force rebuild after writing properties failed: " +
                    ex.Message);
            }

            report.ReportPath =
                WriteReport(modelDoc, report);

            return report;
        }

        internal static string NormalizeProfileType(
            string profileType)
        {
            if (string.IsNullOrWhiteSpace(profileType))
                return SkipProfileName;

            string value =
                profileType.Trim();

            if (string.Equals(value, TopProfileName,
                    StringComparison.OrdinalIgnoreCase))
                return TopProfileName;

            if (string.Equals(value, BottomProfileName,
                    StringComparison.OrdinalIgnoreCase))
                return BottomProfileName;

            if (string.Equals(value, CustomProfileName,
                    StringComparison.OrdinalIgnoreCase))
                return CustomProfileName;

            return SkipProfileName;
        }

        internal static void ApplyProfilePreset(
            CutListGridRow row,
            string profileType,
            string description,
            string brand,
            string model)
        {
            if (row == null)
                return;

            string normalizedProfileType =
                NormalizeProfileType(profileType);

            row.ProfileType = normalizedProfileType;

            if (string.Equals(
                    normalizedProfileType,
                    TopProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.Description = TopDescriptionValue;
                row.Brand = StandardBrandValue;
                row.Model = StandardModelValue;
                return;
            }

            if (string.Equals(
                    normalizedProfileType,
                    BottomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.Description = BottomDescriptionValue;
                row.Brand = StandardBrandValue;
                row.Model = StandardModelValue;
                return;
            }

            if (string.Equals(
                    normalizedProfileType,
                    CustomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.Description = description ?? string.Empty;
                row.Brand = brand ?? string.Empty;
                row.Model = model ?? string.Empty;
                return;
            }
        }

        private static void TryUpdateCutList(
            IModelDoc2 modelDoc,
            List<string> messages,
            string context)
        {
            if (modelDoc == null)
                return;

            bool updateAttempted = false;
            bool updateSucceeded = false;

            try
            {
                Feature rootFeature =
                    modelDoc.FirstFeature() as Feature;

                TryUpdateCutListFromFeatures(
                    rootFeature,
                    false,
                    ref updateAttempted,
                    ref updateSucceeded,
                    messages);
            }
            catch (Exception ex)
            {
                if (messages != null)
                {
                    messages.Add(
                        "Cut-list feature scan failed " +
                        SafeContextText(context) +
                        ": " + ex.Message);
                }
            }

            try
            {
                modelDoc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                if (messages != null)
                {
                    messages.Add(
                        "Force rebuild failed " +
                        SafeContextText(context) +
                        ": " + ex.Message);
                }
            }

            if (messages != null && !updateAttempted)
            {
                messages.Add(
                    "No SolidBodyFolder/CutListFolder update method was found " +
                    SafeContextText(context) +
                    ". The command continued after rebuild.");
            }
            else if (messages != null && updateAttempted && !updateSucceeded)
            {
                messages.Add(
                    "SOLIDWORKS cut-list update was attempted but did not report success " +
                    SafeContextText(context) +
                    ". The command continued after rebuild.");
            }
        }

        private static void TryUpdateCutListFromFeatures(
            Feature feature,
            bool featureIsSubFeature,
            ref bool updateAttempted,
            ref bool updateSucceeded,
            List<string> messages)
        {
            Feature currentFeature = feature;

            while (currentFeature != null)
            {
                string typeName =
                    SafeFeatureTypeName(currentFeature);

                if (string.Equals(
                        typeName,
                        "SolidBodyFolder",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        typeName,
                        "CutListFolder",
                        StringComparison.OrdinalIgnoreCase))
                {
                    TryInvokeUpdateCutList(
                        currentFeature,
                        ref updateAttempted,
                        ref updateSucceeded,
                        messages);
                }

                Feature subFeature =
                    currentFeature.GetFirstSubFeature()
                        as Feature;

                if (subFeature != null)
                {
                    TryUpdateCutListFromFeatures(
                        subFeature,
                        true,
                        ref updateAttempted,
                        ref updateSucceeded,
                        messages);
                }

                currentFeature =
                    featureIsSubFeature
                        ? currentFeature.GetNextSubFeature() as Feature
                        : currentFeature.GetNextFeature() as Feature;
            }
        }

        private static void TryInvokeUpdateCutList(
            Feature feature,
            ref bool updateAttempted,
            ref bool updateSucceeded,
            List<string> messages)
        {
            if (feature == null)
                return;

            object specificFeature = null;

            try
            {
                specificFeature =
                    feature.GetSpecificFeature2();
            }
            catch
            {
                specificFeature = null;
            }

            if (specificFeature == null)
                return;

            try
            {
                updateAttempted = true;

                object result =
                    specificFeature
                        .GetType()
                        .InvokeMember(
                            "UpdateCutList",
                            BindingFlags.InvokeMethod,
                            null,
                            specificFeature,
                            null);

                if (result is bool)
                {
                    updateSucceeded =
                        updateSucceeded || (bool)result;
                }
                else
                {
                    updateSucceeded = true;
                }
            }
            catch (MissingMethodException)
            {
                // Some SOLIDWORKS interop versions do not expose UpdateCutList
                // on every body-folder object. Rebuild still refreshes many
                // cut-list states, so this is not fatal.
            }
            catch (Exception ex)
            {
                if (messages != null)
                {
                    messages.Add(
                        "UpdateCutList failed for feature '" +
                        SafeFeatureName(feature) +
                        "': " + ex.Message);
                }
            }
        }

        private static string SafeContextText(
            string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                return string.Empty;

            return "(" + context.Trim() + ")";
        }

        private static void TraverseFeatures(
            Feature feature,
            List<CutListItemInfo> cutListItems,
            int depth,
            bool featureIsSubFeature)
        {
            Feature currentFeature = feature;

            while (currentFeature != null)
            {
                string typeName =
                    SafeFeatureTypeName(currentFeature);

                if (string.Equals(
                        typeName,
                        "CutListFolder",
                        StringComparison.OrdinalIgnoreCase))
                {
                    CutListItemInfo item =
                        CreateCutListItemInfo(currentFeature);

                    if (item != null)
                    {
                        item.FeatureDepth = depth;
                        cutListItems.Add(item);
                    }
                }

                Feature subFeature =
                    currentFeature.GetFirstSubFeature()
                        as Feature;

                if (subFeature != null)
                {
                    TraverseFeatures(
                        subFeature,
                        cutListItems,
                        depth + 1,
                        true);
                }

                currentFeature =
                    featureIsSubFeature
                        ? currentFeature.GetNextSubFeature() as Feature
                        : currentFeature.GetNextFeature() as Feature;
            }
        }

        private static CutListItemInfo CreateCutListItemInfo(
            Feature cutListFeature)
        {
            if (cutListFeature == null)
                return null;

            CustomPropertyManager propertyManager =
                cutListFeature.CustomPropertyManager;

            if (propertyManager == null)
                return null;

            CutListItemInfo item =
                new CutListItemInfo();

            item.Feature = cutListFeature;
            item.PropertyManager = propertyManager;
            item.FeatureName =
                cutListFeature.Name ?? string.Empty;
            item.FeatureTypeName =
                SafeFeatureTypeName(cutListFeature);

            item.ExistingDescription =
                ReadPropertyText(
                    propertyManager,
                    DescriptionPropertyName,
                    "Description");

            item.ExistingBrand =
                ReadPropertyText(
                    propertyManager,
                    BrandPropertyName,
                    "Brand");

            item.ExistingModel =
                ReadPropertyText(
                    propertyManager,
                    ModelPropertyName,
                    "Model");

            BodyFolder bodyFolder = null;

            try
            {
                bodyFolder =
                    cutListFeature.GetSpecificFeature2()
                        as BodyFolder;
            }
            catch
            {
                bodyFolder = null;
            }

            if (bodyFolder != null)
            {
                object[] bodies = null;

                try
                {
                    bodies = bodyFolder.GetBodies()
                        as object[];
                }
                catch
                {
                    bodies = null;
                }

                if (bodies != null)
                {
                    foreach (object bodyObject in bodies)
                    {
                        Body2 body =
                            bodyObject as Body2;

                        if (body == null)
                            continue;

                        string bodyName =
                            string.Empty;

                        try
                        {
                            bodyName =
                                body.Name ?? string.Empty;
                        }
                        catch
                        {
                            bodyName = string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(bodyName))
                        {
                            item.BodyNames.Add(bodyName);
                        }
                    }
                }
            }

            return item;
        }

        private static List<KeyValuePair<string, string>> ParseExtraProperties(
            string extraPropertiesText,
            CutListGridRow row,
            CutListWriteReport report)
        {
            List<KeyValuePair<string, string>> result =
                new List<KeyValuePair<string, string>>();

            if (string.IsNullOrWhiteSpace(extraPropertiesText))
                return result;

            string normalized =
                extraPropertiesText
                    .Replace("\r\n", ";")
                    .Replace("\n", ";")
                    .Replace("\r", ";");

            string[] entries =
                normalized.Split(
                    new[] { ';' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in entries)
            {
                string trimmed =
                    entry == null
                        ? string.Empty
                        : entry.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                int separatorIndex =
                    trimmed.IndexOf('=');

                if (separatorIndex <= 0)
                {
                    if (report != null)
                    {
                        report.GeneralMessages.Add(
                            "Ignored extra property entry for " +
                            (row == null ? "<unknown>" : row.FeatureName) +
                            ": " + trimmed +
                            " . Use PROPERTY=VALUE format.");
                    }

                    continue;
                }

                string propertyName =
                    trimmed.Substring(0, separatorIndex).Trim();

                string propertyValue =
                    trimmed.Substring(separatorIndex + 1).Trim();

                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                result.Add(
                    new KeyValuePair<string, string>(
                        propertyName,
                        propertyValue));
            }

            return result;
        }

        private static void SetCutListTextProperty(
            CustomPropertyManager propertyManager,
            string propertyName,
            string value)
        {
            if (propertyManager == null ||
                string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            propertyManager.Add3(
                propertyName.Trim(),
                (int)swCustomInfoType_e.swCustomInfoText,
                value ?? string.Empty,
                (int)swCustomPropertyAddOption_e
                    .swCustomPropertyReplaceValue);
        }

        private static string ReadPropertyText(
            CustomPropertyManager propertyManager,
            params string[] possibleNames)
        {
            if (propertyManager == null ||
                possibleNames == null)
            {
                return string.Empty;
            }

            foreach (string propertyName in possibleNames)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                string rawValue;
                string resolvedValue;
                bool wasResolved;
                bool linked;

                try
                {
                    int result =
                        propertyManager.Get6(
                            propertyName,
                            false,
                            out rawValue,
                            out resolvedValue,
                            out wasResolved,
                            out linked);

                    if (result ==
                            (int)swCustomInfoGetResult_e
                                .swCustomInfoGetResult_NotPresent)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(resolvedValue))
                        return resolvedValue.Trim();

                    if (!string.IsNullOrWhiteSpace(rawValue))
                        return rawValue.Trim();
                }
                catch
                {
                    // Some legacy cut-list items can reject a property read.
                    // Ignore and test the next accepted spelling.
                }
            }

            return string.Empty;
        }

        private static string SafeFeatureName(
            Feature feature)
        {
            if (feature == null)
                return string.Empty;

            try
            {
                return feature.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeFeatureTypeName(
            Feature feature)
        {
            if (feature == null)
                return string.Empty;

            try
            {
                return feature.GetTypeName2() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string WriteReport(
            IModelDoc2 modelDoc,
            CutListWriteReport report)
        {
            string reportFolder =
                Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments),
                    "CabinTools",
                    "CutListReports");

            Directory.CreateDirectory(reportFolder);

            string fileBaseName =
                "CutListProfileManualUpdate_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string documentTitle =
                modelDoc == null
                    ? string.Empty
                    : modelDoc.GetTitle() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(documentTitle))
            {
                fileBaseName +=
                    "_" +
                    SanitizeFileName(
                        Path.GetFileNameWithoutExtension(documentTitle));
            }

            string reportPath =
                Path.Combine(
                    reportFolder,
                    fileBaseName + ".txt");

            File.WriteAllText(
                reportPath,
                BuildReportText(report),
                Encoding.UTF8);

            return reportPath;
        }

        private static string BuildReportText(
            CutListWriteReport report)
        {
            StringBuilder builder =
                new StringBuilder();

            builder.AppendLine(
                "Cabin Tools - Manual cut-list property update report");
            builder.AppendLine(
                "Generated: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(
                "Document title: " +
                (report.DocumentTitle ?? string.Empty));
            builder.AppendLine(
                "Document path: " +
                (report.DocumentPath ?? string.Empty));
            builder.AppendLine();
            builder.AppendLine(
                "Standard profile presets:");
            builder.AppendLine(
                "Top Profile: DESCRIPTION = Top Profile, BRAND = SBA, MODEL = Type 121");
            builder.AppendLine(
                "Bottom Profile: DESCRIPTION = Bottom Profile, BRAND = SBA, MODEL = Type 121");
            builder.AppendLine();
            builder.AppendLine(
                "Summary:");
            builder.AppendLine(
                "Updated rows: " + report.UpdatedRows.Count);
            builder.AppendLine(
                "Skipped rows: " + report.SkippedRows.Count);
            builder.AppendLine(
                "Failed rows: " + report.FailedRows.Count);
            builder.AppendLine();

            if (report.GeneralMessages.Count > 0)
            {
                builder.AppendLine("General messages:");

                foreach (string message in report.GeneralMessages)
                {
                    builder.AppendLine("- " + message);
                }

                builder.AppendLine();
            }

            builder.AppendLine("Updated rows:");

            if (report.UpdatedRows.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (CutListGridRow row in report.UpdatedRows)
                {
                    AppendRowReport(builder, row);
                }
            }

            builder.AppendLine();
            builder.AppendLine("Skipped rows:");

            if (report.SkippedRows.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (CutListGridRow row in report.SkippedRows)
                {
                    AppendRowReport(builder, row);
                }
            }

            builder.AppendLine();
            builder.AppendLine("Failed rows:");

            if (report.FailedRows.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (CutListGridRow row in report.FailedRows)
                {
                    AppendRowReport(builder, row);
                }
            }

            return builder.ToString();
        }

        private static void AppendRowReport(
            StringBuilder builder,
            CutListGridRow row)
        {
            builder.AppendLine(
                "- " + DisplayValue(row.FeatureName));
            builder.AppendLine(
                "  Apply: " + row.Apply);
            builder.AppendLine(
                "  Profile type: " + DisplayValue(row.ProfileType));
            builder.AppendLine(
                "  Description: " + DisplayValue(row.Description));
            builder.AppendLine(
                "  Brand: " + DisplayValue(row.Brand));
            builder.AppendLine(
                "  Model: " + DisplayValue(row.Model));
            builder.AppendLine(
                "  Extra properties: " + DisplayValue(row.ExtraProperties));
            builder.AppendLine(
                "  Status: " + DisplayValue(row.Status));
        }

        private static string DisplayValue(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }

        private static string SanitizeFileName(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Part";

            string sanitized = value;

            foreach (char invalidCharacter in
                Path.GetInvalidFileNameChars())
            {
                sanitized =
                    sanitized.Replace(
                        invalidCharacter,
                        '_');
            }

            return sanitized.Trim();
        }

        private static void ShowMessage(
            string message,
            MessageBoxIcon icon)
        {
            try
            {
                MessageBox.Show(
                    message,
                    "Cabin Tools - Cut List Properties",
                    MessageBoxButtons.OK,
                    icon);
            }
            catch
            {
                SwEnvironment.Application.ShowMessageBox(
                    message,
                    icon == MessageBoxIcon.Error
                        ? SolidWorksMessageBoxIcon.Stop
                        : icon == MessageBoxIcon.Warning
                            ? SolidWorksMessageBoxIcon.Warning
                            : SolidWorksMessageBoxIcon.Information);
            }
        }

        internal sealed class CutListItemInfo
        {
            public Feature Feature;
            public CustomPropertyManager PropertyManager;
            public string FeatureName = string.Empty;
            public string FeatureTypeName = string.Empty;
            public int FeatureDepth;
            public string ExistingDescription = string.Empty;
            public string ExistingBrand = string.Empty;
            public string ExistingModel = string.Empty;
            public List<string> BodyNames =
                new List<string>();
        }

        internal sealed class CutListGridRow
        {
            public bool Apply;
            public CutListItemInfo Item;
            public string FeatureName = string.Empty;
            public string ExistingDescription = string.Empty;
            public string ExistingBrand = string.Empty;
            public string ExistingModel = string.Empty;
            public string ProfileType = SkipProfileName;
            public string Description = string.Empty;
            public string Brand = string.Empty;
            public string Model = string.Empty;
            public string ExtraProperties = string.Empty;
            public string Status = string.Empty;

            public CutListGridRow CloneForReport()
            {
                return new CutListGridRow
                {
                    Apply = Apply,
                    Item = null,
                    FeatureName = FeatureName,
                    ExistingDescription = ExistingDescription,
                    ExistingBrand = ExistingBrand,
                    ExistingModel = ExistingModel,
                    ProfileType = ProfileType,
                    Description = Description,
                    Brand = Brand,
                    Model = Model,
                    ExtraProperties = ExtraProperties,
                    Status = Status
                };
            }
        }

        internal sealed class CutListWriteReport
        {
            public string DocumentTitle = string.Empty;
            public string DocumentPath = string.Empty;
            public string ReportPath = string.Empty;
            public List<string> GeneralMessages =
                new List<string>();
            public List<CutListGridRow> UpdatedRows =
                new List<CutListGridRow>();
            public List<CutListGridRow> SkippedRows =
                new List<CutListGridRow>();
            public List<CutListGridRow> FailedRows =
                new List<CutListGridRow>();
        }
    }

    internal sealed class CutListProfilePropertyForm : Form
    {
        private readonly IModelDoc2 modelDoc;
        private readonly List<CutListProfilePropertyCommand.CutListGridRow>
            rows = new List<CutListProfilePropertyCommand.CutListGridRow>();

        private DataGridView grid;
        private ComboBox presetComboBox;
        private TextBox presetDescriptionTextBox;
        private TextBox presetBrandTextBox;
        private TextBox presetModelTextBox;
        private TextBox customPropertyNameTextBox;
        private TextBox customPropertyValueTextBox;
        private Label statusLabel;
        private Button applyCheckedButton;
        private Button applyAllButton;
        private Button closeButton;
        private bool suppressGridEvents;

        private const int ColumnApply = 0;
        private const int ColumnFeatureName = 1;
        private const int ColumnProfileType = 2;
        private const int ColumnDescription = 3;
        private const int ColumnBrand = 4;
        private const int ColumnModel = 5;
        private const int ColumnExtraProperties = 6;
        private const int ColumnExisting = 7;
        private const int ColumnStatus = 8;

        public CutListProfilePropertyForm(
            IModelDoc2 activeModelDoc)
        {
            modelDoc = activeModelDoc;
            InitializeComponent();
            RefreshCutListRows();
        }

        private void InitializeComponent()
        {
            Text = "Cabin Tools - Cut List Profile Properties";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1300;
            Height = 760;
            MinimumSize = new Size(1050, 600);

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));
            Controls.Add(mainLayout);

            Label headerLabel = new Label();
            headerLabel.Dock = DockStyle.Fill;
            headerLabel.AutoSize = true;
            headerLabel.Padding = new Padding(10, 10, 10, 4);
            headerLabel.Text =
                "Select Top Profile or Bottom Profile manually for each cut-list item. " +
                "No automatic detection, naming convention, or role property is used.";
            mainLayout.Controls.Add(headerLabel, 0, 0);

            mainLayout.Controls.Add(
                CreatePresetPanel(),
                0,
                1);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoGenerateColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged +=
                Grid_CurrentCellDirtyStateChanged;
            grid.DataError += Grid_DataError;

            CreateGridColumns();

            mainLayout.Controls.Add(grid, 0, 2);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoSize = true;
            statusLabel.Padding = new Padding(10, 6, 10, 6);
            statusLabel.Text = "Ready.";
            mainLayout.Controls.Add(statusLabel, 0, 3);

            FlowLayoutPanel bottomPanel =
                new FlowLayoutPanel();
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.FlowDirection = FlowDirection.RightToLeft;
            bottomPanel.AutoSize = true;
            bottomPanel.Padding = new Padding(10);

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += CloseButton_Click;
            bottomPanel.Controls.Add(closeButton);

            applyCheckedButton = new Button();
            applyCheckedButton.Text = "Apply checked rows";
            applyCheckedButton.AutoSize = true;
            applyCheckedButton.Click += ApplyCheckedButton_Click;
            bottomPanel.Controls.Add(applyCheckedButton);

            applyAllButton = new Button();
            applyAllButton.Text = "Apply all rows";
            applyAllButton.AutoSize = true;
            applyAllButton.Click += ApplyAllButton_Click;
            bottomPanel.Controls.Add(applyAllButton);

            Button selectAllButton = new Button();
            selectAllButton.Text = "Check all";
            selectAllButton.AutoSize = true;
            selectAllButton.Click += SelectAllButton_Click;
            bottomPanel.Controls.Add(selectAllButton);

            Button clearAllButton = new Button();
            clearAllButton.Text = "Uncheck all";
            clearAllButton.AutoSize = true;
            clearAllButton.Click += ClearAllButton_Click;
            bottomPanel.Controls.Add(clearAllButton);

            Button refreshButton = new Button();
            refreshButton.Text = "Refresh cut-list items";
            refreshButton.AutoSize = true;
            refreshButton.Click += RefreshButton_Click;
            bottomPanel.Controls.Add(refreshButton);

            mainLayout.Controls.Add(bottomPanel, 0, 4);
        }

        private Control CreatePresetPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;
            panel.Padding = new Padding(10, 4, 10, 8);
            panel.ColumnCount = 1;
            panel.RowCount = 3;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel profilePanel = new FlowLayoutPanel();
            profilePanel.Dock = DockStyle.Fill;
            profilePanel.AutoSize = true;
            profilePanel.WrapContents = true;

            profilePanel.Controls.Add(
                CreateLabel("Profile preset:"));

            presetComboBox = new ComboBox();
            presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetComboBox.Width = 150;
            presetComboBox.Items.Add(
                CutListProfilePropertyCommand.TopProfileName);
            presetComboBox.Items.Add(
                CutListProfilePropertyCommand.BottomProfileName);
            presetComboBox.Items.Add(
                CutListProfilePropertyCommand.CustomProfileName);
            presetComboBox.SelectedIndex = 0;
            presetComboBox.SelectedIndexChanged +=
                PresetComboBox_SelectedIndexChanged;
            profilePanel.Controls.Add(presetComboBox);

            presetDescriptionTextBox = CreateTextBox(160);
            presetBrandTextBox = CreateTextBox(90);
            presetModelTextBox = CreateTextBox(110);
            ApplyPresetToTextBoxes();

            profilePanel.Controls.Add(CreateLabel("DESCRIPTION:"));
            profilePanel.Controls.Add(presetDescriptionTextBox);
            profilePanel.Controls.Add(CreateLabel("BRAND:"));
            profilePanel.Controls.Add(presetBrandTextBox);
            profilePanel.Controls.Add(CreateLabel("MODEL:"));
            profilePanel.Controls.Add(presetModelTextBox);

            Button applyPresetCheckedButton = new Button();
            applyPresetCheckedButton.Text = "Apply preset to checked";
            applyPresetCheckedButton.AutoSize = true;
            applyPresetCheckedButton.Click +=
                ApplyPresetCheckedButton_Click;
            profilePanel.Controls.Add(applyPresetCheckedButton);

            Button applyPresetAllButton = new Button();
            applyPresetAllButton.Text = "Apply preset to all";
            applyPresetAllButton.AutoSize = true;
            applyPresetAllButton.Click +=
                ApplyPresetAllButton_Click;
            profilePanel.Controls.Add(applyPresetAllButton);

            panel.Controls.Add(profilePanel, 0, 0);

            FlowLayoutPanel customPanel = new FlowLayoutPanel();
            customPanel.Dock = DockStyle.Fill;
            customPanel.AutoSize = true;
            customPanel.WrapContents = true;
            customPanel.Padding = new Padding(0, 4, 0, 0);

            customPanel.Controls.Add(
                CreateLabel("Custom cut-list property:"));

            customPropertyNameTextBox = CreateTextBox(170);
            customPropertyNameTextBox.Text = "";
            customPanel.Controls.Add(customPropertyNameTextBox);

            customPanel.Controls.Add(CreateLabel("="));

            customPropertyValueTextBox = CreateTextBox(220);
            customPanel.Controls.Add(customPropertyValueTextBox);

            Button addCustomCheckedButton = new Button();
            addCustomCheckedButton.Text = "Add/update property to checked";
            addCustomCheckedButton.AutoSize = true;
            addCustomCheckedButton.Click +=
                AddCustomCheckedButton_Click;
            customPanel.Controls.Add(addCustomCheckedButton);

            Button addCustomAllButton = new Button();
            addCustomAllButton.Text = "Add/update property to all";
            addCustomAllButton.AutoSize = true;
            addCustomAllButton.Click +=
                AddCustomAllButton_Click;
            customPanel.Controls.Add(addCustomAllButton);

            panel.Controls.Add(customPanel, 0, 1);

            Label noteLabel = new Label();
            noteLabel.AutoSize = true;
            noteLabel.Padding = new Padding(0, 4, 0, 0);
            noteLabel.Text =
                "Extra properties can also be edited per row using: PROPERTY=VALUE; PROPERTY2=VALUE2";
            panel.Controls.Add(noteLabel, 0, 2);

            return panel;
        }

        private void CreateGridColumns()
        {
            DataGridViewCheckBoxColumn applyColumn =
                new DataGridViewCheckBoxColumn();
            applyColumn.HeaderText = "Apply";
            applyColumn.Width = 55;
            grid.Columns.Add(applyColumn);

            DataGridViewTextBoxColumn featureColumn =
                new DataGridViewTextBoxColumn();
            featureColumn.HeaderText = "Cut-list item";
            featureColumn.ReadOnly = true;
            featureColumn.Width = 210;
            grid.Columns.Add(featureColumn);

            DataGridViewComboBoxColumn profileColumn =
                new DataGridViewComboBoxColumn();
            profileColumn.HeaderText = "Profile type";
            profileColumn.Width = 130;
            profileColumn.Items.Add(
                CutListProfilePropertyCommand.SkipProfileName);
            profileColumn.Items.Add(
                CutListProfilePropertyCommand.TopProfileName);
            profileColumn.Items.Add(
                CutListProfilePropertyCommand.BottomProfileName);
            profileColumn.Items.Add(
                CutListProfilePropertyCommand.CustomProfileName);
            grid.Columns.Add(profileColumn);

            DataGridViewTextBoxColumn descriptionColumn =
                new DataGridViewTextBoxColumn();
            descriptionColumn.HeaderText = "Description";
            descriptionColumn.Width = 150;
            grid.Columns.Add(descriptionColumn);

            DataGridViewTextBoxColumn brandColumn =
                new DataGridViewTextBoxColumn();
            brandColumn.HeaderText = "Brand";
            brandColumn.Width = 90;
            grid.Columns.Add(brandColumn);

            DataGridViewTextBoxColumn modelColumn =
                new DataGridViewTextBoxColumn();
            modelColumn.HeaderText = "Model";
            modelColumn.Width = 110;
            grid.Columns.Add(modelColumn);

            DataGridViewTextBoxColumn extraColumn =
                new DataGridViewTextBoxColumn();
            extraColumn.HeaderText = "Extra properties";
            extraColumn.Width = 260;
            grid.Columns.Add(extraColumn);

            DataGridViewTextBoxColumn existingColumn =
                new DataGridViewTextBoxColumn();
            existingColumn.HeaderText = "Existing values";
            existingColumn.ReadOnly = true;
            existingColumn.Width = 260;
            grid.Columns.Add(existingColumn);

            DataGridViewTextBoxColumn statusColumn =
                new DataGridViewTextBoxColumn();
            statusColumn.HeaderText = "Status";
            statusColumn.ReadOnly = true;
            statusColumn.Width = 180;
            grid.Columns.Add(statusColumn);
        }

        private static Label CreateLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(6, 6, 2, 2);
            return label;
        }

        private static TextBox CreateTextBox(int width)
        {
            TextBox textBox = new TextBox();
            textBox.Width = width;
            textBox.Margin = new Padding(2, 3, 8, 2);
            return textBox;
        }

        private void RefreshCutListRows()
        {
            rows.Clear();
            grid.Rows.Clear();

            List<string> messages = new List<string>();
            List<CutListProfilePropertyCommand.CutListItemInfo> items =
                CutListProfilePropertyCommand.GetCutListItems(
                    modelDoc,
                    messages);

            foreach (CutListProfilePropertyCommand.CutListItemInfo item in items)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    new CutListProfilePropertyCommand.CutListGridRow();

                row.Apply = false;
                row.Item = item;
                row.FeatureName = item.FeatureName;
                row.ExistingDescription = item.ExistingDescription;
                row.ExistingBrand = item.ExistingBrand;
                row.ExistingModel = item.ExistingModel;
                row.ProfileType =
                    CutListProfilePropertyCommand.SkipProfileName;
                row.Description = item.ExistingDescription;
                row.Brand = item.ExistingBrand;
                row.Model = item.ExistingModel;
                row.Status = "Ready";

                rows.Add(row);
                AddGridRow(row);
            }

            AutoSizeStatusCells();

            if (items.Count == 0)
            {
                statusLabel.Text =
                    "No cut-list items were found. Confirm this is a weldment or multibody part and update the cut list.";
                return;
            }

            if (messages.Count > 0)
            {
                statusLabel.Text =
                    "Loaded " + items.Count +
                    " cut-list item(s). Messages: " +
                    string.Join(" | ", messages.ToArray());
                return;
            }

            statusLabel.Text =
                "Loaded " + items.Count +
                " cut-list item(s). Select Top Profile or Bottom Profile manually, then apply checked rows.";
        }

        private void AddGridRow(
            CutListProfilePropertyCommand.CutListGridRow row)
        {
            int index = grid.Rows.Add();
            DataGridViewRow gridRow = grid.Rows[index];
            gridRow.Tag = row;

            WriteGridRowFromModel(gridRow, row);
        }

        private void WriteGridRowFromModel(
            DataGridViewRow gridRow,
            CutListProfilePropertyCommand.CutListGridRow row)
        {
            bool previousSuppressGridEvents = suppressGridEvents;
            suppressGridEvents = true;

            try
            {
                gridRow.Cells[ColumnApply].Value = row.Apply;
                gridRow.Cells[ColumnFeatureName].Value = row.FeatureName;
                gridRow.Cells[ColumnProfileType].Value = row.ProfileType;
                gridRow.Cells[ColumnDescription].Value = row.Description;
                gridRow.Cells[ColumnBrand].Value = row.Brand;
                gridRow.Cells[ColumnModel].Value = row.Model;
                gridRow.Cells[ColumnExtraProperties].Value = row.ExtraProperties;
                gridRow.Cells[ColumnExisting].Value =
                    "Description=" + Display(row.ExistingDescription) +
                    "; Brand=" + Display(row.ExistingBrand) +
                    "; Model=" + Display(row.ExistingModel);
                gridRow.Cells[ColumnStatus].Value = row.Status;

                ApplyRowStatusStyle(gridRow, row.Status);
            }
            finally
            {
                suppressGridEvents = previousSuppressGridEvents;
            }
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<blank>"
                : value;
        }

        private void Grid_CurrentCellDirtyStateChanged(
            object sender,
            EventArgs e)
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellValueChanged(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (suppressGridEvents)
                return;

            if (e.RowIndex < 0 ||
                e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            DataGridViewRow gridRow = grid.Rows[e.RowIndex];
            CutListProfilePropertyCommand.CutListGridRow row =
                gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;

            if (row == null)
                return;

            ReadGridRowToModel(gridRow, row);

            if (e.ColumnIndex == ColumnProfileType)
            {
                CutListProfilePropertyCommand.ApplyProfilePreset(
                    row,
                    row.ProfileType,
                    row.Description,
                    row.Brand,
                    row.Model);

                if (string.Equals(row.ProfileType,
                        CutListProfilePropertyCommand.CustomProfileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    row.Description =
                        Convert.ToString(
                            gridRow.Cells[ColumnDescription].Value) ??
                        string.Empty;
                    row.Brand =
                        Convert.ToString(
                            gridRow.Cells[ColumnBrand].Value) ??
                        string.Empty;
                    row.Model =
                        Convert.ToString(
                            gridRow.Cells[ColumnModel].Value) ??
                        string.Empty;
                }

                if (string.Equals(row.ProfileType,
                        CutListProfilePropertyCommand.SkipProfileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    row.Apply = false;
                    row.Status = "Skipped";
                }
                else
                {
                    row.Apply = true;
                    row.Status = "Ready - checked for update";
                }

                WriteGridRowFromModel(gridRow, row);
                return;
            }

            if (e.ColumnIndex == ColumnDescription ||
                e.ColumnIndex == ColumnBrand ||
                e.ColumnIndex == ColumnModel ||
                e.ColumnIndex == ColumnExtraProperties)
            {
                if (!string.IsNullOrWhiteSpace(row.Description) ||
                    !string.IsNullOrWhiteSpace(row.Brand) ||
                    !string.IsNullOrWhiteSpace(row.Model) ||
                    !string.IsNullOrWhiteSpace(row.ExtraProperties))
                {
                    row.Apply = true;
                    row.Status = "Ready - checked for update";
                    WriteGridRowFromModel(gridRow, row);
                }
            }
        }

        private void Grid_DataError(
            object sender,
            DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void ReadGridRowToModel(
            DataGridViewRow gridRow,
            CutListProfilePropertyCommand.CutListGridRow row)
        {
            row.Apply =
                Convert.ToBoolean(
                    gridRow.Cells[ColumnApply].Value ?? false);

            row.ProfileType =
                Convert.ToString(
                    gridRow.Cells[ColumnProfileType].Value) ??
                CutListProfilePropertyCommand.SkipProfileName;

            row.Description =
                Convert.ToString(
                    gridRow.Cells[ColumnDescription].Value) ??
                string.Empty;

            row.Brand =
                Convert.ToString(
                    gridRow.Cells[ColumnBrand].Value) ??
                string.Empty;

            row.Model =
                Convert.ToString(
                    gridRow.Cells[ColumnModel].Value) ??
                string.Empty;

            row.ExtraProperties =
                Convert.ToString(
                    gridRow.Cells[ColumnExtraProperties].Value) ??
                string.Empty;
        }

        private void ReadAllGridRowsToModels()
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;

                if (row != null)
                {
                    ReadGridRowToModel(gridRow, row);
                }
            }
        }

        private void ApplyPresetToTextBoxes()
        {
            string selectedPreset =
                Convert.ToString(presetComboBox.SelectedItem) ??
                CutListProfilePropertyCommand.TopProfileName;

            if (string.Equals(
                    selectedPreset,
                    CutListProfilePropertyCommand.TopProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                presetDescriptionTextBox.Text =
                    CutListProfilePropertyCommand.TopDescriptionValue;
                presetBrandTextBox.Text =
                    CutListProfilePropertyCommand.StandardBrandValue;
                presetModelTextBox.Text =
                    CutListProfilePropertyCommand.StandardModelValue;
            }
            else if (string.Equals(
                    selectedPreset,
                    CutListProfilePropertyCommand.BottomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                presetDescriptionTextBox.Text =
                    CutListProfilePropertyCommand.BottomDescriptionValue;
                presetBrandTextBox.Text =
                    CutListProfilePropertyCommand.StandardBrandValue;
                presetModelTextBox.Text =
                    CutListProfilePropertyCommand.StandardModelValue;
            }
        }

        private void PresetComboBox_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            ApplyPresetToTextBoxes();
        }

        private void ApplyPresetCheckedButton_Click(
            object sender,
            EventArgs e)
        {
            ApplyPresetToRows(true);
        }

        private void ApplyPresetAllButton_Click(
            object sender,
            EventArgs e)
        {
            ApplyPresetToRows(false);
        }

        private void ApplyPresetToRows(bool checkedOnly)
        {
            ReadAllGridRowsToModels();

            string selectedPreset =
                Convert.ToString(presetComboBox.SelectedItem) ??
                CutListProfilePropertyCommand.TopProfileName;

            int count = 0;

            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;

                if (row == null)
                    continue;

                if (checkedOnly && !row.Apply)
                    continue;

                row.Apply = true;

                CutListProfilePropertyCommand.ApplyProfilePreset(
                    row,
                    selectedPreset,
                    presetDescriptionTextBox.Text,
                    presetBrandTextBox.Text,
                    presetModelTextBox.Text);

                row.Status = "Ready";
                WriteGridRowFromModel(gridRow, row);
                count++;
            }

            statusLabel.Text =
                "Applied preset to " + count + " row(s).";
        }

        private void AddCustomCheckedButton_Click(
            object sender,
            EventArgs e)
        {
            AddCustomPropertyToRows(true);
        }

        private void AddCustomAllButton_Click(
            object sender,
            EventArgs e)
        {
            AddCustomPropertyToRows(false);
        }

        private void AddCustomPropertyToRows(bool checkedOnly)
        {
            string propertyName =
                customPropertyNameTextBox.Text == null
                    ? string.Empty
                    : customPropertyNameTextBox.Text.Trim();

            string propertyValue =
                customPropertyValueTextBox.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                MessageBox.Show(
                    "Enter the custom property name first.",
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ReadAllGridRowsToModels();

            int count = 0;

            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;

                if (row == null)
                    continue;

                if (checkedOnly && !row.Apply)
                    continue;

                row.Apply = true;
                row.ExtraProperties =
                    AddOrReplaceExtraPropertyText(
                        row.ExtraProperties,
                        propertyName,
                        propertyValue);
                row.Status = "Ready";

                WriteGridRowFromModel(gridRow, row);
                count++;
            }

            statusLabel.Text =
                "Added/updated custom property for " +
                count +
                " row(s).";
        }

        private static string AddOrReplaceExtraPropertyText(
            string existing,
            string propertyName,
            string propertyValue)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(existing))
            {
                string[] entries = existing
                    .Replace("\r\n", ";")
                    .Replace("\n", ";")
                    .Replace("\r", ";")
                    .Split(new[] { ';' },
                        StringSplitOptions.RemoveEmptyEntries);

                foreach (string entry in entries)
                {
                    int separatorIndex = entry.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string name =
                        entry.Substring(0, separatorIndex).Trim();
                    string value =
                        entry.Substring(separatorIndex + 1).Trim();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        values[name] = value;
                    }
                }
            }

            values[propertyName] = propertyValue ?? string.Empty;

            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, string> pair in values)
            {
                if (builder.Length > 0)
                    builder.Append("; ");

                builder.Append(pair.Key);
                builder.Append("=");
                builder.Append(pair.Value);
            }

            return builder.ToString();
        }

        private void ApplyCheckedButton_Click(
            object sender,
            EventArgs e)
        {
            try
            {
                ReadAllGridRowsToModels();

                List<CutListProfilePropertyCommand.CutListGridRow>
                    rowsToApply =
                        new List<CutListProfilePropertyCommand.CutListGridRow>();

                foreach (CutListProfilePropertyCommand.CutListGridRow row in rows)
                {
                    if (row.Apply)
                    {
                        rowsToApply.Add(row);
                    }
                }

                if (rowsToApply.Count == 0)
                {
                    MessageBox.Show(
                        "No rows are checked. Check at least one cut-list row before applying.",
                        "Cabin Tools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DialogResult confirmation = MessageBox.Show(
                    "This will write cut-list properties to " +
                    rowsToApply.Count +
                    " checked cut-list row(s).\r\n\r\n" +
                    "The part will be rebuilt but not saved automatically. Continue?",
                    "Cabin Tools - Apply Cut List Properties",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirmation != DialogResult.Yes)
                    return;

                CutListProfilePropertyCommand.CutListWriteReport report =
                    CutListProfilePropertyCommand.ApplyRows(
                        modelDoc,
                        rowsToApply);

                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    CutListProfilePropertyCommand.CutListGridRow row =
                        gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;
                    if (row != null)
                    {
                        WriteGridRowFromModel(gridRow, row);
                    }
                }

                statusLabel.Text =
                    "Updated " + report.UpdatedRows.Count +
                    " row(s), skipped " + report.SkippedRows.Count +
                    ", failed " + report.FailedRows.Count +
                    ". Report: " + report.ReportPath;

                MessageBox.Show(
                    "Cut-list property update complete.\r\n\r\n" +
                    "Updated rows: " + report.UpdatedRows.Count + "\r\n" +
                    "Skipped rows: " + report.SkippedRows.Count + "\r\n" +
                    "Failed rows: " + report.FailedRows.Count + "\r\n\r\n" +
                    "The part was rebuilt but not saved automatically.\r\n\r\n" +
                    "Report:\r\n" + report.ReportPath,
                    "Cabin Tools - Cut List Properties",
                    MessageBoxButtons.OK,
                    report.FailedRows.Count > 0
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Cabin Tools could not apply the cut-list properties.\r\n\r\n" +
                    ex.Message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ApplyAllButton_Click(
            object sender,
            EventArgs e)
        {
            SetCheckedStateForAllRows(true);
            ApplyCheckedButton_Click(sender, e);
        }

        private void SelectAllButton_Click(
            object sender,
            EventArgs e)
        {
            SetCheckedStateForAllRows(true);
        }

        private void ClearAllButton_Click(
            object sender,
            EventArgs e)
        {
            SetCheckedStateForAllRows(false);
        }

        private void SetCheckedStateForAllRows(bool isChecked)
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;

                if (row == null)
                    continue;

                row.Apply = isChecked;
                WriteGridRowFromModel(gridRow, row);
            }
        }

        private void RefreshButton_Click(
            object sender,
            EventArgs e)
        {
            RefreshCutListRows();
        }

        private void CloseButton_Click(
            object sender,
            EventArgs e)
        {
            Close();
        }

        private void AutoSizeStatusCells()
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                CutListProfilePropertyCommand.CutListGridRow row =
                    gridRow.Tag as CutListProfilePropertyCommand.CutListGridRow;
                if (row != null)
                {
                    ApplyRowStatusStyle(gridRow, row.Status);
                }
            }
        }

        private static void ApplyRowStatusStyle(
            DataGridViewRow gridRow,
            string status)
        {
            if (gridRow == null)
                return;

            DataGridViewCell statusCell =
                gridRow.Cells[ColumnStatus];

            statusCell.Style.BackColor = Color.White;
            statusCell.Style.ForeColor = Color.Black;

            if (status == null)
                return;

            if (status.StartsWith("!",
                    StringComparison.OrdinalIgnoreCase) ||
                status.IndexOf("failed",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("blank",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor = Color.MistyRose;
                statusCell.Style.ForeColor = Color.DarkRed;
            }
            else if (status.IndexOf("updated",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor = Color.Honeydew;
                statusCell.Style.ForeColor = Color.DarkGreen;
            }
            else if (status.IndexOf("skipped",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor = Color.LightYellow;
                statusCell.Style.ForeColor = Color.DarkGoldenrod;
            }
        }
    }
}
