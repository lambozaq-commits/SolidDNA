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
    /// Manually assigns DESCRIPTION, BRAND, MODEL, and optional custom
    /// properties to the active configuration's visible weldment cut-list
    /// folders.
    ///
    /// The cut-list grid is intentionally populated from the direct,
    /// body-containing folders displayed below the SOLIDWORKS cut-list node.
    /// It does not recursively add historical/internal CutListFolder features.
    /// </summary>
    internal static class CutListProfilePropertyCommand
    {
        internal const string DescriptionPropertyName = "DESCRIPTION";
        internal const string BrandPropertyName = "BRAND";
        internal const string ModelPropertyName = "MODEL";

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
                    CabinCustomPropertyStore.GetActiveModelDocument();

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
                    CabinCustomPropertyStore.GetWriteBlockReason(modelDoc);

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

        /// <summary>
        /// Returns one row for each active, body-containing cut-list folder
        /// shown in the FeatureManager cut-list node.
        ///
        /// The previous implementation recursively traversed all features and
        /// subfeatures. That can return stale or internal CutListFolder
        /// features and create more rows than SOLIDWORKS displays.
        /// </summary>
        internal static List<CutListItemInfo> GetCutListItems(
            IModelDoc2 modelDoc,
            List<string> messages)
        {
            List<CutListItemInfo> result =
                new List<CutListItemInfo>();

            if (modelDoc == null ||
                modelDoc.GetType() !=
                    (int)swDocumentTypes_e.swDocPART)
            {
                return result;
            }

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
                AddMessage(
                    messages,
                    "Force rebuild before reading cut list failed: " +
                    ex.Message);
            }

            List<Feature> visibleCutListFeatures =
                FindDisplayedCutListFeatures(modelDoc, messages);

            List<CustomPropertyManager> configurationManagers =
                GetConfigurationCutListPropertyManagers(
                    modelDoc,
                    messages);

            bool useConfigurationManagers =
                configurationManagers.Count ==
                    visibleCutListFeatures.Count &&
                configurationManagers.Count > 0;

            for (int index = 0;
                 index < visibleCutListFeatures.Count;
                 index++)
            {
                Feature feature =
                    visibleCutListFeatures[index];

                CustomPropertyManager propertyManager = null;

                if (useConfigurationManagers)
                {
                    propertyManager =
                        configurationManagers[index];
                }

                if (propertyManager == null)
                {
                    propertyManager =
                        GetFeaturePropertyManager(feature);
                }

                CutListItemInfo item =
                    CreateCutListItemInfo(
                        feature,
                        propertyManager);

                if (item != null)
                {
                    result.Add(item);
                }
            }

            if (result.Count == 0)
            {
                AddMessage(
                    messages,
                    "No active body-containing cut-list folders were found.");
            }
            else if (configurationManagers.Count > 0 &&
                     configurationManagers.Count !=
                         visibleCutListFeatures.Count)
            {
                AddMessage(
                    messages,
                    "The configuration API returned " +
                    configurationManagers.Count +
                    " cut-list item(s), while the FeatureManager displays " +
                    visibleCutListFeatures.Count +
                    " body-containing cut-list folder(s). " +
                    "The displayed FeatureManager folders were used.");
            }

            return result;
        }

        private static List<Feature> FindDisplayedCutListFeatures(
            IModelDoc2 modelDoc,
            List<string> messages)
        {
            List<Feature> features =
                new List<Feature>();

            HashSet<string> keys =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            Feature current =
                modelDoc.FirstFeature() as Feature;

            while (current != null)
            {
                string typeName =
                    SafeFeatureTypeName(current);

                if (IsCutListFolder(typeName))
                {
                    TryAddBodyContainingCutListFeature(
                        current,
                        features,
                        keys);
                }
                else if (IsCutListContainer(typeName))
                {
                    AddDirectCutListSubFeatures(
                        current,
                        features,
                        keys);
                }

                current =
                    current.GetNextFeature() as Feature;
            }

            if (features.Count > 0)
                return features;

            // Fallback for uncommon feature-tree layouts. This fallback still
            // ignores empty folders and deduplicates folders by name/body set.
            Feature root =
                modelDoc.FirstFeature() as Feature;

            CollectBodyContainingCutListFeaturesFallback(
                root,
                false,
                features,
                keys);

            if (features.Count > 0)
            {
                AddMessage(
                    messages,
                    "The cut-list container was not found directly. " +
                    "A filtered fallback traversal was used.");
            }

            return features;
        }

        private static void AddDirectCutListSubFeatures(
            Feature container,
            List<Feature> features,
            HashSet<string> keys)
        {
            if (container == null)
                return;

            Feature subFeature = null;

            try
            {
                subFeature =
                    container.GetFirstSubFeature() as Feature;
            }
            catch
            {
                subFeature = null;
            }

            while (subFeature != null)
            {
                string typeName =
                    SafeFeatureTypeName(subFeature);

                if (IsCutListFolder(typeName))
                {
                    TryAddBodyContainingCutListFeature(
                        subFeature,
                        features,
                        keys);
                }
                else if (IsCutListContainer(typeName))
                {
                    // Some versions place a CutListFolder container one level
                    // below SolidBodyFolder. Read only its direct children.
                    Feature nested = null;

                    try
                    {
                        nested =
                            subFeature.GetFirstSubFeature()
                                as Feature;
                    }
                    catch
                    {
                        nested = null;
                    }

                    while (nested != null)
                    {
                        if (IsCutListFolder(
                                SafeFeatureTypeName(nested)))
                        {
                            TryAddBodyContainingCutListFeature(
                                nested,
                                features,
                                keys);
                        }

                        nested =
                            nested.GetNextSubFeature()
                                as Feature;
                    }
                }

                subFeature =
                    subFeature.GetNextSubFeature()
                        as Feature;
            }
        }

        private static void CollectBodyContainingCutListFeaturesFallback(
            Feature feature,
            bool featureIsSubFeature,
            List<Feature> features,
            HashSet<string> keys)
        {
            Feature current = feature;

            while (current != null)
            {
                string typeName =
                    SafeFeatureTypeName(current);

                if (IsCutListFolder(typeName))
                {
                    TryAddBodyContainingCutListFeature(
                        current,
                        features,
                        keys);

                    // Do not recurse into a cut-list item. Internal children
                    // are not independent grid rows.
                }
                else
                {
                    Feature child = null;

                    try
                    {
                        child =
                            current.GetFirstSubFeature()
                                as Feature;
                    }
                    catch
                    {
                        child = null;
                    }

                    if (child != null)
                    {
                        CollectBodyContainingCutListFeaturesFallback(
                            child,
                            true,
                            features,
                            keys);
                    }
                }

                current =
                    featureIsSubFeature
                        ? current.GetNextSubFeature() as Feature
                        : current.GetNextFeature() as Feature;
            }
        }

        private static void TryAddBodyContainingCutListFeature(
            Feature feature,
            List<Feature> features,
            HashSet<string> keys)
        {
            if (feature == null)
                return;

            List<string> bodyNames =
                GetCutListBodyNames(feature);

            if (bodyNames.Count == 0)
                return;

            string key =
                BuildCutListFeatureKey(
                    feature,
                    bodyNames);

            if (keys.Add(key))
            {
                features.Add(feature);
            }
        }

        private static string BuildCutListFeatureKey(
            Feature feature,
            IList<string> bodyNames)
        {
            StringBuilder builder =
                new StringBuilder();

            builder.Append(
                SafeFeatureName(feature));
            builder.Append("|");

            if (bodyNames != null)
            {
                for (int index = 0;
                     index < bodyNames.Count;
                     index++)
                {
                    if (index > 0)
                        builder.Append(";");

                    builder.Append(
                        bodyNames[index] ?? string.Empty);
                }
            }

            return builder.ToString();
        }

        private static bool IsCutListFolder(
            string typeName)
        {
            return string.Equals(
                typeName,
                "CutListFolder",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCutListContainer(
            string typeName)
        {
            return string.Equals(
                       typeName,
                       "SolidBodyFolder",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       typeName,
                       "Weldment",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       typeName,
                       "WeldmentFeature",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static List<CustomPropertyManager>
            GetConfigurationCutListPropertyManagers(
                IModelDoc2 modelDoc,
                List<string> messages)
        {
            List<CustomPropertyManager> managers =
                new List<CustomPropertyManager>();

            Configuration activeConfiguration = null;

            try
            {
                ConfigurationManager configurationManager =
                    modelDoc.ConfigurationManager;

                if (configurationManager != null)
                {
                    activeConfiguration =
                        configurationManager.ActiveConfiguration;
                }
            }
            catch (Exception ex)
            {
                AddMessage(
                    messages,
                    "Could not obtain the active configuration: " +
                    ex.Message);
                return managers;
            }

            if (activeConfiguration == null)
                return managers;

            object cutListItemsObject = null;

            try
            {
                cutListItemsObject =
                    activeConfiguration.GetCutListItems();
            }
            catch (Exception ex)
            {
                AddMessage(
                    messages,
                    "The active configuration cut-list API could not be read: " +
                    ex.Message);
                return managers;
            }

            Array cutListItems =
                cutListItemsObject as Array;

            if (cutListItems == null)
                return managers;

            foreach (object rawItem in cutListItems)
            {
                ICutListItem cutListItem =
                    rawItem as ICutListItem;

                if (cutListItem == null)
                    continue;

                try
                {
                    CustomPropertyManager manager =
                        cutListItem.CustomPropertyManager;

                    if (manager != null)
                    {
                        managers.Add(manager);
                    }
                }
                catch (Exception ex)
                {
                    AddMessage(
                        messages,
                        "A configuration-specific cut-list property manager " +
                        "could not be read: " +
                        ex.Message);
                }
            }

            return managers;
        }

        private static CutListItemInfo CreateCutListItemInfo(
            Feature feature,
            CustomPropertyManager propertyManager)
        {
            if (feature == null)
                return null;

            CutListItemInfo item =
                new CutListItemInfo();

            item.Feature = feature;
            item.PropertyManager = propertyManager;
            item.FeatureName =
                SafeFeatureName(feature);
            item.FeatureTypeName =
                SafeFeatureTypeName(feature);
            item.FeatureDepth = 0;
            item.BodyNames =
                GetCutListBodyNames(feature);

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

            return item;
        }

        private static List<string> GetCutListBodyNames(
            Feature feature)
        {
            List<string> bodyNames =
                new List<string>();

            if (feature == null)
                return bodyNames;

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

            BodyFolder bodyFolder =
                specificFeature as BodyFolder;

            if (bodyFolder == null)
                return bodyNames;

            object bodiesObject = null;

            try
            {
                bodiesObject =
                    bodyFolder.GetBodies();
            }
            catch
            {
                bodiesObject = null;
            }

            Array bodies =
                bodiesObject as Array;

            if (bodies == null)
                return bodyNames;

            foreach (object rawBody in bodies)
            {
                Body2 body =
                    rawBody as Body2;

                if (body == null)
                    continue;

                string bodyName =
                    SafeBodyName(body);

                if (string.IsNullOrWhiteSpace(bodyName))
                {
                    bodyName =
                        "Body " +
                        (bodyNames.Count + 1).ToString();
                }

                bodyNames.Add(bodyName);
            }

            return bodyNames;
        }

        private static string SafeBodyName(
            Body2 body)
        {
            if (body == null)
                return string.Empty;

            try
            {
                return body.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static CustomPropertyManager
            GetFeaturePropertyManager(
                Feature feature)
        {
            if (feature == null)
                return null;

            try
            {
                return feature.CustomPropertyManager;
            }
            catch
            {
                return null;
            }
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

            if (modelDoc.GetType() !=
                (int)swDocumentTypes_e.swDocPART)
            {
                throw new InvalidOperationException(
                    "The active document is not a SOLIDWORKS part.");
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

                report.ReportPath =
                    WriteReport(modelDoc, report);

                return report;
            }

            foreach (CutListGridRow row in rows)
            {
                if (row == null)
                    continue;

                if (!row.Apply)
                {
                    row.Status = "Skipped";
                    report.SkippedRows.Add(
                        row.CloneForReport());
                    continue;
                }

                if (row.Item == null ||
                    row.Item.PropertyManager == null)
                {
                    row.Status =
                        "Failed: no cut-list property manager";
                    report.FailedRows.Add(
                        row.CloneForReport());
                    continue;
                }

                try
                {
                    bool wroteValue = false;

                    string profileType =
                        NormalizeProfileType(
                            row.ProfileType);

                    if (!string.Equals(
                            profileType,
                            SkipProfileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(
                                row.Description))
                        {
                            row.Status =
                                "! DESCRIPTION is blank";

                            report.FailedRows.Add(
                                row.CloneForReport());
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

                        wroteValue = true;
                    }

                    List<KeyValuePair<string, string>>
                        extraProperties =
                            ParseExtraProperties(
                                row.ExtraProperties);

                    foreach (
                        KeyValuePair<string, string> property
                        in extraProperties)
                    {
                        SetCutListTextProperty(
                            row.Item.PropertyManager,
                            property.Key,
                            property.Value);

                        wroteValue = true;
                    }

                    if (!wroteValue)
                    {
                        row.Status =
                            "Skipped: no property values selected";

                        report.SkippedRows.Add(
                            row.CloneForReport());
                        continue;
                    }

                    row.ExistingDescription =
                        ReadPropertyText(
                            row.Item.PropertyManager,
                            DescriptionPropertyName,
                            "Description");

                    row.ExistingBrand =
                        ReadPropertyText(
                            row.Item.PropertyManager,
                            BrandPropertyName,
                            "Brand");

                    row.ExistingModel =
                        ReadPropertyText(
                            row.Item.PropertyManager,
                            ModelPropertyName,
                            "Model");

                    row.Status = "Updated";

                    report.UpdatedRows.Add(
                        row.CloneForReport());
                }
                catch (Exception ex)
                {
                    row.Status =
                        "Failed: " + ex.Message;

                    report.FailedRows.Add(
                        row.CloneForReport());
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

        internal static void ApplyProfilePreset(
            CutListGridRow row,
            string selectedPreset,
            string description,
            string brand,
            string model)
        {
            if (row == null)
                return;

            string preset =
                NormalizeProfileType(selectedPreset);

            row.ProfileType = preset;

            if (string.Equals(
                    preset,
                    SkipProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            row.Description =
                description ?? string.Empty;
            row.Brand =
                brand ?? string.Empty;
            row.Model =
                model ?? string.Empty;
        }

        internal static string NormalizeProfileType(
            string profileType)
        {
            if (string.Equals(
                    profileType,
                    TopProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return TopProfileName;
            }

            if (string.Equals(
                    profileType,
                    BottomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BottomProfileName;
            }

            if (string.Equals(
                    profileType,
                    CustomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CustomProfileName;
            }

            return SkipProfileName;
        }

        internal static List<KeyValuePair<string, string>>
            ParseExtraProperties(
                string extraPropertiesText)
        {
            List<KeyValuePair<string, string>> result =
                new List<KeyValuePair<string, string>>();

            if (string.IsNullOrWhiteSpace(
                    extraPropertiesText))
            {
                return result;
            }

            string[] entries =
                extraPropertiesText
                    .Replace("\r\n", ";")
                    .Replace("\n", ";")
                    .Replace("\r", ";")
                    .Split(
                        new[] { ';' },
                        StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in entries)
            {
                string trimmed =
                    entry == null
                        ? string.Empty
                        : entry.Trim();

                int separatorIndex =
                    trimmed.IndexOf('=');

                if (separatorIndex <= 0)
                    continue;

                string propertyName =
                    trimmed.Substring(
                        0,
                        separatorIndex).Trim();

                string propertyValue =
                    trimmed.Substring(
                        separatorIndex + 1).Trim();

                if (string.IsNullOrWhiteSpace(
                        propertyName))
                {
                    continue;
                }

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
                if (string.IsNullOrWhiteSpace(
                        propertyName))
                {
                    continue;
                }

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

                    if (!string.IsNullOrWhiteSpace(
                            resolvedValue))
                    {
                        return resolvedValue.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(
                            rawValue))
                    {
                        return rawValue.Trim();
                    }
                }
                catch
                {
                    // Some legacy cut-list items reject a property read.
                    // Continue with the next accepted spelling.
                }
            }

            return string.Empty;
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

            Feature root =
                modelDoc.FirstFeature() as Feature;

            TryUpdateCutListFromFeatures(
                root,
                false,
                ref updateAttempted,
                ref updateSucceeded,
                messages);

            if (!updateAttempted)
            {
                AddMessage(
                    messages,
                    "No SolidBodyFolder/CutListFolder update method was found " +
                    SafeContextText(context) +
                    ". The command continued after rebuild.");
            }
            else if (!updateSucceeded)
            {
                AddMessage(
                    messages,
                    "SOLIDWORKS cut-list update was attempted but did not " +
                    "report success " +
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
            Feature current = feature;

            while (current != null)
            {
                string typeName =
                    SafeFeatureTypeName(current);

                if (IsCutListContainer(typeName) ||
                    IsCutListFolder(typeName))
                {
                    TryInvokeUpdateCutList(
                        current,
                        ref updateAttempted,
                        ref updateSucceeded,
                        messages);
                }

                Feature child = null;

                try
                {
                    child =
                        current.GetFirstSubFeature()
                            as Feature;
                }
                catch
                {
                    child = null;
                }

                if (child != null)
                {
                    TryUpdateCutListFromFeatures(
                        child,
                        true,
                        ref updateAttempted,
                        ref updateSucceeded,
                        messages);
                }

                current =
                    featureIsSubFeature
                        ? current.GetNextSubFeature() as Feature
                        : current.GetNextFeature() as Feature;
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
                        updateSucceeded ||
                        (bool)result;
                }
                else
                {
                    updateSucceeded = true;
                }
            }
            catch (MissingMethodException)
            {
                // Not every body-folder object exposes UpdateCutList.
            }
            catch (TargetInvocationException ex)
            {
                AddMessage(
                    messages,
                    "UpdateCutList failed for feature '" +
                    SafeFeatureName(feature) +
                    "': " +
                    (ex.InnerException == null
                        ? ex.Message
                        : ex.InnerException.Message));
            }
            catch (Exception ex)
            {
                AddMessage(
                    messages,
                    "UpdateCutList failed for feature '" +
                    SafeFeatureName(feature) +
                    "': " + ex.Message);
            }
        }

        private static string SafeContextText(
            string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                return string.Empty;

            return "(" + context.Trim() + ")";
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
                return feature.GetTypeName2() ??
                       string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddMessage(
            List<string> messages,
            string message)
        {
            if (messages == null ||
                string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            messages.Add(message);
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

            Directory.CreateDirectory(
                reportFolder);

            string fileBaseName =
                "CutListProfileManualUpdate_" +
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss");

            string documentTitle =
                modelDoc == null
                    ? string.Empty
                    : modelDoc.GetTitle() ??
                      string.Empty;

            if (!string.IsNullOrWhiteSpace(
                    documentTitle))
            {
                fileBaseName +=
                    "_" +
                    SanitizeFileName(
                        Path.GetFileNameWithoutExtension(
                            documentTitle));
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
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine(
                "Document title: " +
                (report.DocumentTitle ??
                 string.Empty));
            builder.AppendLine(
                "Document path: " +
                (report.DocumentPath ??
                 string.Empty));
            builder.AppendLine();
            builder.AppendLine(
                "Summary:");
            builder.AppendLine(
                "Updated rows: " +
                report.UpdatedRows.Count);
            builder.AppendLine(
                "Skipped rows: " +
                report.SkippedRows.Count);
            builder.AppendLine(
                "Failed rows: " +
                report.FailedRows.Count);
            builder.AppendLine();

            if (report.GeneralMessages.Count > 0)
            {
                builder.AppendLine(
                    "General messages:");

                foreach (string message
                         in report.GeneralMessages)
                {
                    builder.AppendLine(
                        "- " + message);
                }

                builder.AppendLine();
            }

            AppendReportSection(
                builder,
                "Updated rows:",
                report.UpdatedRows);

            builder.AppendLine();

            AppendReportSection(
                builder,
                "Skipped rows:",
                report.SkippedRows);

            builder.AppendLine();

            AppendReportSection(
                builder,
                "Failed rows:",
                report.FailedRows);

            return builder.ToString();
        }

        private static void AppendReportSection(
            StringBuilder builder,
            string heading,
            IList<CutListGridRow> rows)
        {
            builder.AppendLine(heading);

            if (rows == null || rows.Count == 0)
            {
                builder.AppendLine("- None");
                return;
            }

            foreach (CutListGridRow row in rows)
            {
                AppendRowReport(
                    builder,
                    row);
            }
        }

        private static void AppendRowReport(
            StringBuilder builder,
            CutListGridRow row)
        {
            builder.AppendLine(
                "- " +
                DisplayValue(row.FeatureName));
            builder.AppendLine(
                "  Apply: " + row.Apply);
            builder.AppendLine(
                "  Profile type: " +
                DisplayValue(row.ProfileType));
            builder.AppendLine(
                "  DESCRIPTION: " +
                DisplayValue(row.Description));
            builder.AppendLine(
                "  BRAND: " +
                DisplayValue(row.Brand));
            builder.AppendLine(
                "  MODEL: " +
                DisplayValue(row.Model));
            builder.AppendLine(
                "  Extra properties: " +
                DisplayValue(row.ExtraProperties));
            builder.AppendLine(
                "  Status: " +
                DisplayValue(row.Status));
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

            foreach (char invalidCharacter
                     in Path.GetInvalidFileNameChars())
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
            public string FeatureName =
                string.Empty;
            public string FeatureTypeName =
                string.Empty;
            public int FeatureDepth;
            public string ExistingDescription =
                string.Empty;
            public string ExistingBrand =
                string.Empty;
            public string ExistingModel =
                string.Empty;
            public List<string> BodyNames =
                new List<string>();
        }

        internal sealed class CutListGridRow
        {
            public bool Apply;
            public CutListItemInfo Item;
            public string FeatureName =
                string.Empty;
            public string ExistingDescription =
                string.Empty;
            public string ExistingBrand =
                string.Empty;
            public string ExistingModel =
                string.Empty;
            public string ProfileType =
                SkipProfileName;
            public string Description =
                string.Empty;
            public string Brand =
                string.Empty;
            public string Model =
                string.Empty;
            public string ExtraProperties =
                string.Empty;
            public string Status =
                string.Empty;

            public CutListGridRow CloneForReport()
            {
                return new CutListGridRow
                {
                    Apply = Apply,
                    Item = null,
                    FeatureName = FeatureName,
                    ExistingDescription =
                        ExistingDescription,
                    ExistingBrand =
                        ExistingBrand,
                    ExistingModel =
                        ExistingModel,
                    ProfileType = ProfileType,
                    Description = Description,
                    Brand = Brand,
                    Model = Model,
                    ExtraProperties =
                        ExtraProperties,
                    Status = Status
                };
            }
        }

        internal sealed class CutListWriteReport
        {
            public string DocumentTitle =
                string.Empty;
            public string DocumentPath =
                string.Empty;
            public string ReportPath =
                string.Empty;

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

    internal sealed class CutListProfilePropertyForm :
        Form
    {
        private readonly IModelDoc2 modelDoc;

        private readonly List<
            CutListProfilePropertyCommand.CutListGridRow>
            rows =
                new List<
                    CutListProfilePropertyCommand.CutListGridRow>();

        private DataGridView grid;
        private ComboBox presetComboBox;
        private TextBox presetDescriptionTextBox;
        private TextBox presetBrandTextBox;
        private TextBox presetModelTextBox;
        private TextBox customPropertyNameTextBox;
        private TextBox customPropertyValueTextBox;
        private Label statusLabel;

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
            Text =
                "Cabin Tools - Cut List Profile Properties";

            StartPosition =
                FormStartPosition.CenterScreen;

            Width = 1300;
            Height = 760;

            MinimumSize =
                new Size(1050, 600);

            TableLayoutPanel mainLayout =
                new TableLayoutPanel();

            mainLayout.Dock =
                DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100F));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.AutoSize));

            mainLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.AutoSize));

            Controls.Add(mainLayout);

            Label headerLabel =
                new Label();

            headerLabel.Dock =
                DockStyle.Fill;
            headerLabel.AutoSize = true;
            headerLabel.Padding =
                new Padding(10, 10, 10, 4);

            headerLabel.Text =
                "Select Top Profile, Bottom Profile, Custom, or Skip for each active cut-list item. " +
                "Only body-containing folders displayed in the active cut list are loaded.";

            mainLayout.Controls.Add(
                headerLabel,
                0,
                0);

            mainLayout.Controls.Add(
                CreateEditorPanel(),
                0,
                1);

            grid =
                new DataGridView();

            grid.Dock =
                DockStyle.Fill;
            grid.AllowUserToAddRows =
                false;
            grid.AllowUserToDeleteRows =
                false;
            grid.AutoGenerateColumns =
                false;
            grid.SelectionMode =
                DataGridViewSelectionMode
                    .FullRowSelect;
            grid.MultiSelect =
                true;
            grid.RowHeadersVisible =
                false;
            grid.AutoSizeRowsMode =
                DataGridViewAutoSizeRowsMode
                    .None;

            grid.ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode
                    .AutoSize;

            grid.CellValueChanged +=
                Grid_CellValueChanged;

            grid.CurrentCellDirtyStateChanged +=
                Grid_CurrentCellDirtyStateChanged;

            grid.DataError +=
                Grid_DataError;

            CreateGridColumns();

            mainLayout.Controls.Add(
                grid,
                0,
                2);

            statusLabel =
                new Label();

            statusLabel.Dock =
                DockStyle.Fill;
            statusLabel.AutoSize =
                true;
            statusLabel.Padding =
                new Padding(10, 6, 10, 6);
            statusLabel.Text =
                "Ready.";

            mainLayout.Controls.Add(
                statusLabel,
                0,
                3);

            mainLayout.Controls.Add(
                CreateBottomPanel(),
                0,
                4);
        }

        private Control CreateEditorPanel()
        {
            TableLayoutPanel editorPanel =
                new TableLayoutPanel();

            editorPanel.Dock =
                DockStyle.Fill;
            editorPanel.AutoSize =
                true;
            editorPanel.ColumnCount =
                1;
            editorPanel.RowCount =
                2;
            editorPanel.Padding =
                new Padding(10, 0, 10, 4);

            editorPanel.Controls.Add(
                CreatePresetPanel(),
                0,
                0);

            editorPanel.Controls.Add(
                CreateCustomPropertyPanel(),
                0,
                1);

            return editorPanel;
        }

        private Control CreatePresetPanel()
        {
            FlowLayoutPanel panel =
                new FlowLayoutPanel();

            panel.Dock =
                DockStyle.Fill;
            panel.AutoSize =
                true;
            panel.WrapContents =
                true;

            panel.Controls.Add(
                CreateLabel("Profile preset:"));

            presetComboBox =
                new ComboBox();

            presetComboBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            presetComboBox.Width =
                150;

            presetComboBox.Items.AddRange(
                new object[]
                {
                    CutListProfilePropertyCommand
                        .TopProfileName,
                    CutListProfilePropertyCommand
                        .BottomProfileName,
                    CutListProfilePropertyCommand
                        .CustomProfileName,
                    CutListProfilePropertyCommand
                        .SkipProfileName
                });

            presetComboBox.SelectedItem =
                CutListProfilePropertyCommand
                    .BottomProfileName;

            presetComboBox.SelectedIndexChanged +=
                PresetComboBox_SelectedIndexChanged;

            panel.Controls.Add(
                presetComboBox);

            panel.Controls.Add(
                CreateLabel("DESCRIPTION:"));

            presetDescriptionTextBox =
                new TextBox();

            presetDescriptionTextBox.Width =
                150;

            panel.Controls.Add(
                presetDescriptionTextBox);

            panel.Controls.Add(
                CreateLabel("BRAND:"));

            presetBrandTextBox =
                new TextBox();

            presetBrandTextBox.Width =
                100;

            panel.Controls.Add(
                presetBrandTextBox);

            panel.Controls.Add(
                CreateLabel("MODEL:"));

            presetModelTextBox =
                new TextBox();

            presetModelTextBox.Width =
                120;

            panel.Controls.Add(
                presetModelTextBox);

            Button applyPresetCheckedButton =
                new Button();

            applyPresetCheckedButton.AutoSize =
                true;
            applyPresetCheckedButton.Text =
                "Apply preset to checked";

            applyPresetCheckedButton.Click +=
                ApplyPresetCheckedButton_Click;

            panel.Controls.Add(
                applyPresetCheckedButton);

            Button applyPresetAllButton =
                new Button();

            applyPresetAllButton.AutoSize =
                true;
            applyPresetAllButton.Text =
                "Apply preset to all";

            applyPresetAllButton.Click +=
                ApplyPresetAllButton_Click;

            panel.Controls.Add(
                applyPresetAllButton);

            ApplyPresetToTextBoxes();

            return panel;
        }

        private Control CreateCustomPropertyPanel()
        {
            FlowLayoutPanel panel =
                new FlowLayoutPanel();

            panel.Dock =
                DockStyle.Fill;
            panel.AutoSize =
                true;
            panel.WrapContents =
                true;

            panel.Controls.Add(
                CreateLabel(
                    "Custom cut-list property:"));

            customPropertyNameTextBox =
                new TextBox();

            customPropertyNameTextBox.Width =
                180;

            panel.Controls.Add(
                customPropertyNameTextBox);

            panel.Controls.Add(
                CreateLabel("="));

            customPropertyValueTextBox =
                new TextBox();

            customPropertyValueTextBox.Width =
                180;

            panel.Controls.Add(
                customPropertyValueTextBox);

            Button addCustomCheckedButton =
                new Button();

            addCustomCheckedButton.AutoSize =
                true;
            addCustomCheckedButton.Text =
                "Add/update property to checked";

            addCustomCheckedButton.Click +=
                AddCustomCheckedButton_Click;

            panel.Controls.Add(
                addCustomCheckedButton);

            Button addCustomAllButton =
                new Button();

            addCustomAllButton.AutoSize =
                true;
            addCustomAllButton.Text =
                "Add/update property to all";

            addCustomAllButton.Click +=
                AddCustomAllButton_Click;

            panel.Controls.Add(
                addCustomAllButton);

            Label extraHelp =
                CreateLabel(
                    "Extra properties per row: PROPERTY=VALUE; PROPERTY2=VALUE2");

            extraHelp.AutoSize =
                true;

            panel.Controls.Add(
                extraHelp);

            return panel;
        }

        private static Label CreateLabel(
            string text)
        {
            Label label =
                new Label();

            label.AutoSize =
                true;
            label.Text =
                text;
            label.Margin =
                new Padding(3, 7, 3, 0);

            return label;
        }

        private Control CreateBottomPanel()
        {
            FlowLayoutPanel panel =
                new FlowLayoutPanel();

            panel.Dock =
                DockStyle.Fill;
            panel.FlowDirection =
                FlowDirection.RightToLeft;
            panel.AutoSize =
                true;
            panel.Padding =
                new Padding(10);

            Button closeButton =
                new Button();

            closeButton.Text =
                "Close";
            closeButton.AutoSize =
                true;
            closeButton.Click +=
                CloseButton_Click;

            panel.Controls.Add(
                closeButton);

            Button applyCheckedButton =
                new Button();

            applyCheckedButton.Text =
                "Apply checked rows";
            applyCheckedButton.AutoSize =
                true;
            applyCheckedButton.Click +=
                ApplyCheckedButton_Click;

            panel.Controls.Add(
                applyCheckedButton);

            Button applyAllButton =
                new Button();

            applyAllButton.Text =
                "Apply all rows";
            applyAllButton.AutoSize =
                true;
            applyAllButton.Click +=
                ApplyAllButton_Click;

            panel.Controls.Add(
                applyAllButton);

            Button checkAllButton =
                new Button();

            checkAllButton.Text =
                "Check all";
            checkAllButton.AutoSize =
                true;
            checkAllButton.Click +=
                CheckAllButton_Click;

            panel.Controls.Add(
                checkAllButton);

            Button uncheckAllButton =
                new Button();

            uncheckAllButton.Text =
                "Uncheck all";
            uncheckAllButton.AutoSize =
                true;
            uncheckAllButton.Click +=
                UncheckAllButton_Click;

            panel.Controls.Add(
                uncheckAllButton);

            Button refreshButton =
                new Button();

            refreshButton.Text =
                "Refresh cut-list items";
            refreshButton.AutoSize =
                true;
            refreshButton.Click +=
                RefreshButton_Click;

            panel.Controls.Add(
                refreshButton);

            return panel;
        }

        private void CreateGridColumns()
        {
            DataGridViewCheckBoxColumn applyColumn =
                new DataGridViewCheckBoxColumn();

            applyColumn.HeaderText =
                "Apply";
            applyColumn.Width =
                45;

            grid.Columns.Add(
                applyColumn);

            DataGridViewTextBoxColumn nameColumn =
                new DataGridViewTextBoxColumn();

            nameColumn.HeaderText =
                "Cut-list item";
            nameColumn.Width =
                180;
            nameColumn.ReadOnly =
                true;

            grid.Columns.Add(
                nameColumn);

            DataGridViewComboBoxColumn profileColumn =
                new DataGridViewComboBoxColumn();

            profileColumn.HeaderText =
                "Profile type";
            profileColumn.Width =
                130;
            profileColumn.FlatStyle =
                FlatStyle.Flat;

            profileColumn.Items.AddRange(
                new object[]
                {
                    CutListProfilePropertyCommand
                        .TopProfileName,
                    CutListProfilePropertyCommand
                        .BottomProfileName,
                    CutListProfilePropertyCommand
                        .CustomProfileName,
                    CutListProfilePropertyCommand
                        .SkipProfileName
                });

            grid.Columns.Add(
                profileColumn);

            DataGridViewTextBoxColumn descriptionColumn =
                new DataGridViewTextBoxColumn();

            descriptionColumn.HeaderText =
                "Description";
            descriptionColumn.Width =
                150;

            grid.Columns.Add(
                descriptionColumn);

            DataGridViewTextBoxColumn brandColumn =
                new DataGridViewTextBoxColumn();

            brandColumn.HeaderText =
                "Brand";
            brandColumn.Width =
                90;

            grid.Columns.Add(
                brandColumn);

            DataGridViewTextBoxColumn modelColumn =
                new DataGridViewTextBoxColumn();

            modelColumn.HeaderText =
                "Model";
            modelColumn.Width =
                110;

            grid.Columns.Add(
                modelColumn);

            DataGridViewTextBoxColumn extraColumn =
                new DataGridViewTextBoxColumn();

            extraColumn.HeaderText =
                "Extra properties";
            extraColumn.Width =
                250;

            grid.Columns.Add(
                extraColumn);

            DataGridViewTextBoxColumn existingColumn =
                new DataGridViewTextBoxColumn();

            existingColumn.HeaderText =
                "Existing values";
            existingColumn.Width =
                300;
            existingColumn.ReadOnly =
                true;

            grid.Columns.Add(
                existingColumn);

            DataGridViewTextBoxColumn statusColumn =
                new DataGridViewTextBoxColumn();

            statusColumn.HeaderText =
                "Status";
            statusColumn.Width =
                150;
            statusColumn.ReadOnly =
                true;

            grid.Columns.Add(
                statusColumn);
        }

        private void RefreshCutListRows()
        {
            try
            {
                ReadAllGridRowsToModels();

                rows.Clear();
                grid.Rows.Clear();

                List<string> messages =
                    new List<string>();

                List<
                    CutListProfilePropertyCommand
                        .CutListItemInfo>
                    items =
                        CutListProfilePropertyCommand
                            .GetCutListItems(
                                modelDoc,
                                messages);

                foreach (
                    CutListProfilePropertyCommand
                        .CutListItemInfo item
                    in items)
                {
                    CutListProfilePropertyCommand
                        .CutListGridRow row =
                            new CutListProfilePropertyCommand
                                .CutListGridRow();

                    row.Apply =
                        false;
                    row.Item =
                        item;
                    row.FeatureName =
                        item.FeatureName;
                    row.ExistingDescription =
                        item.ExistingDescription;
                    row.ExistingBrand =
                        item.ExistingBrand;
                    row.ExistingModel =
                        item.ExistingModel;
                    row.ProfileType =
                        CutListProfilePropertyCommand
                            .SkipProfileName;
                    row.Description =
                        item.ExistingDescription;
                    row.Brand =
                        item.ExistingBrand;
                    row.Model =
                        item.ExistingModel;
                    row.Status =
                        "Ready";

                    rows.Add(row);

                    int rowIndex =
                        grid.Rows.Add();

                    DataGridViewRow gridRow =
                        grid.Rows[rowIndex];

                    gridRow.Tag =
                        row;

                    WriteGridRowFromModel(
                        gridRow,
                        row);
                }

                statusLabel.Text =
                    "Loaded " +
                    rows.Count +
                    " active cut-list item(s).";

                if (messages.Count > 0)
                {
                    statusLabel.Text +=
                        " " +
                        string.Join(
                            " ",
                            messages.ToArray());
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    "Failed to load cut-list items: " +
                    ex.Message;

                MessageBox.Show(
                    "Cabin Tools could not load the cut-list items.\r\n\r\n" +
                    ex.Message,
                    "Cabin Tools - Cut List Properties",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void WriteGridRowFromModel(
            DataGridViewRow gridRow,
            CutListProfilePropertyCommand
                .CutListGridRow row)
        {
            if (gridRow == null ||
                row == null)
            {
                return;
            }

            gridRow.Cells[ColumnApply].Value =
                row.Apply;

            gridRow.Cells[ColumnFeatureName].Value =
                row.FeatureName;

            gridRow.Cells[ColumnProfileType].Value =
                CutListProfilePropertyCommand
                    .NormalizeProfileType(
                        row.ProfileType);

            gridRow.Cells[ColumnDescription].Value =
                row.Description;

            gridRow.Cells[ColumnBrand].Value =
                row.Brand;

            gridRow.Cells[ColumnModel].Value =
                row.Model;

            gridRow.Cells[ColumnExtraProperties].Value =
                row.ExtraProperties;

            gridRow.Cells[ColumnExisting].Value =
                BuildExistingValueText(row);

            gridRow.Cells[ColumnStatus].Value =
                row.Status;

            ApplyRowStatusStyle(
                gridRow,
                row.Status);
        }

        private static string BuildExistingValueText(
            CutListProfilePropertyCommand
                .CutListGridRow row)
        {
            return
                "Description=" +
                (row.ExistingDescription ??
                 string.Empty) +
                "; Brand=" +
                (row.ExistingBrand ??
                 string.Empty) +
                "; Model=" +
                (row.ExistingModel ??
                 string.Empty);
        }

        private void Grid_CurrentCellDirtyStateChanged(
            object sender,
            EventArgs e)
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(
                    DataGridViewDataErrorContexts
                        .Commit);
            }
        }

        private void Grid_CellValueChanged(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            DataGridViewRow gridRow =
                grid.Rows[e.RowIndex];

            CutListProfilePropertyCommand
                .CutListGridRow row =
                    gridRow.Tag
                    as CutListProfilePropertyCommand
                        .CutListGridRow;

            if (row == null)
                return;

            ReadGridRowToModel(
                gridRow,
                row);

            row.Status =
                "Ready";

            gridRow.Cells[ColumnStatus].Value =
                row.Status;

            ApplyRowStatusStyle(
                gridRow,
                row.Status);
        }

        private void Grid_DataError(
            object sender,
            DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException =
                false;
        }

        private void ReadGridRowToModel(
            DataGridViewRow gridRow,
            CutListProfilePropertyCommand
                .CutListGridRow row)
        {
            row.Apply =
                Convert.ToBoolean(
                    gridRow.Cells[ColumnApply]
                        .Value ??
                    false);

            row.ProfileType =
                Convert.ToString(
                    gridRow.Cells[ColumnProfileType]
                        .Value) ??
                CutListProfilePropertyCommand
                    .SkipProfileName;

            row.Description =
                Convert.ToString(
                    gridRow.Cells[ColumnDescription]
                        .Value) ??
                string.Empty;

            row.Brand =
                Convert.ToString(
                    gridRow.Cells[ColumnBrand]
                        .Value) ??
                string.Empty;

            row.Model =
                Convert.ToString(
                    gridRow.Cells[ColumnModel]
                        .Value) ??
                string.Empty;

            row.ExtraProperties =
                Convert.ToString(
                    gridRow.Cells[ColumnExtraProperties]
                        .Value) ??
                string.Empty;
        }

        private void ReadAllGridRowsToModels()
        {
            if (grid == null)
                return;

            foreach (DataGridViewRow gridRow
                     in grid.Rows)
            {
                CutListProfilePropertyCommand
                    .CutListGridRow row =
                        gridRow.Tag
                        as CutListProfilePropertyCommand
                            .CutListGridRow;

                if (row != null)
                {
                    ReadGridRowToModel(
                        gridRow,
                        row);
                }
            }
        }

        private void ApplyPresetToTextBoxes()
        {
            string selectedPreset =
                Convert.ToString(
                    presetComboBox.SelectedItem) ??
                CutListProfilePropertyCommand
                    .BottomProfileName;

            if (string.Equals(
                    selectedPreset,
                    CutListProfilePropertyCommand
                        .TopProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                presetDescriptionTextBox.Text =
                    CutListProfilePropertyCommand
                        .TopDescriptionValue;

                presetBrandTextBox.Text =
                    CutListProfilePropertyCommand
                        .StandardBrandValue;

                presetModelTextBox.Text =
                    CutListProfilePropertyCommand
                        .StandardModelValue;
            }
            else if (string.Equals(
                    selectedPreset,
                    CutListProfilePropertyCommand
                        .BottomProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                presetDescriptionTextBox.Text =
                    CutListProfilePropertyCommand
                        .BottomDescriptionValue;

                presetBrandTextBox.Text =
                    CutListProfilePropertyCommand
                        .StandardBrandValue;

                presetModelTextBox.Text =
                    CutListProfilePropertyCommand
                        .StandardModelValue;
            }
            else if (string.Equals(
                    selectedPreset,
                    CutListProfilePropertyCommand
                        .SkipProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                presetDescriptionTextBox.Text =
                    string.Empty;
                presetBrandTextBox.Text =
                    string.Empty;
                presetModelTextBox.Text =
                    string.Empty;
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

        private void ApplyPresetToRows(
            bool checkedOnly)
        {
            ReadAllGridRowsToModels();

            string selectedPreset =
                Convert.ToString(
                    presetComboBox.SelectedItem) ??
                CutListProfilePropertyCommand
                    .BottomProfileName;

            int count = 0;

            foreach (DataGridViewRow gridRow
                     in grid.Rows)
            {
                CutListProfilePropertyCommand
                    .CutListGridRow row =
                        gridRow.Tag
                        as CutListProfilePropertyCommand
                            .CutListGridRow;

                if (row == null)
                    continue;

                if (checkedOnly &&
                    !row.Apply)
                {
                    continue;
                }

                row.Apply =
                    true;

                CutListProfilePropertyCommand
                    .ApplyProfilePreset(
                        row,
                        selectedPreset,
                        presetDescriptionTextBox.Text,
                        presetBrandTextBox.Text,
                        presetModelTextBox.Text);

                row.Status =
                    "Ready";

                WriteGridRowFromModel(
                    gridRow,
                    row);

                count++;
            }

            statusLabel.Text =
                "Applied preset to " +
                count +
                " row(s).";
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

        private void AddCustomPropertyToRows(
            bool checkedOnly)
        {
            string propertyName =
                customPropertyNameTextBox.Text ==
                    null
                    ? string.Empty
                    : customPropertyNameTextBox
                        .Text.Trim();

            string propertyValue =
                customPropertyValueTextBox.Text ??
                string.Empty;

            if (string.IsNullOrWhiteSpace(
                    propertyName))
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

            foreach (DataGridViewRow gridRow
                     in grid.Rows)
            {
                CutListProfilePropertyCommand
                    .CutListGridRow row =
                        gridRow.Tag
                        as CutListProfilePropertyCommand
                            .CutListGridRow;

                if (row == null)
                    continue;

                if (checkedOnly &&
                    !row.Apply)
                {
                    continue;
                }

                row.Apply =
                    true;

                row.ExtraProperties =
                    AddOrReplaceExtraPropertyText(
                        row.ExtraProperties,
                        propertyName,
                        propertyValue);

                row.Status =
                    "Ready";

                WriteGridRowFromModel(
                    gridRow,
                    row);

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

            List<KeyValuePair<string, string>>
                parsed =
                    CutListProfilePropertyCommand
                        .ParseExtraProperties(
                            existing);

            foreach (
                KeyValuePair<string, string> pair
                in parsed)
            {
                values[pair.Key] =
                    pair.Value;
            }

            values[propertyName] =
                propertyValue ??
                string.Empty;

            StringBuilder builder =
                new StringBuilder();

            foreach (
                KeyValuePair<string, string> pair
                in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(
                    pair.Key);
                builder.Append("=");
                builder.Append(
                    pair.Value);
            }

            return builder.ToString();
        }

        private void ApplyCheckedButton_Click(
            object sender,
            EventArgs e)
        {
            ApplyRows(false);
        }

        private void ApplyAllButton_Click(
            object sender,
            EventArgs e)
        {
            ApplyRows(true);
        }

        private void ApplyRows(
            bool forceAllRows)
        {
            try
            {
                ReadAllGridRowsToModels();

                List<
                    CutListProfilePropertyCommand
                        .CutListGridRow>
                    rowsToApply =
                        new List<
                            CutListProfilePropertyCommand
                                .CutListGridRow>();

                foreach (
                    CutListProfilePropertyCommand
                        .CutListGridRow row
                    in rows)
                {
                    if (forceAllRows)
                    {
                        row.Apply =
                            true;
                    }

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

                DialogResult confirmation =
                    MessageBox.Show(
                        "This will write cut-list properties to " +
                        rowsToApply.Count +
                        " cut-list row(s).\r\n\r\n" +
                        "The part will be rebuilt but not saved automatically. Continue?",
                        "Cabin Tools - Apply Cut List Properties",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                if (confirmation !=
                    DialogResult.Yes)
                {
                    return;
                }

                CutListProfilePropertyCommand
                    .CutListWriteReport report =
                        CutListProfilePropertyCommand
                            .ApplyRows(
                                modelDoc,
                                rowsToApply);

                foreach (DataGridViewRow gridRow
                         in grid.Rows)
                {
                    CutListProfilePropertyCommand
                        .CutListGridRow row =
                            gridRow.Tag
                            as CutListProfilePropertyCommand
                                .CutListGridRow;

                    if (row != null)
                    {
                        WriteGridRowFromModel(
                            gridRow,
                            row);
                    }
                }

                statusLabel.Text =
                    "Updated " +
                    report.UpdatedRows.Count +
                    " row(s), skipped " +
                    report.SkippedRows.Count +
                    ", failed " +
                    report.FailedRows.Count +
                    ". Report: " +
                    report.ReportPath;

                MessageBox.Show(
                    "Cut-list property update complete.\r\n\r\n" +
                    "Updated rows: " +
                    report.UpdatedRows.Count +
                    "\r\n" +
                    "Skipped rows: " +
                    report.SkippedRows.Count +
                    "\r\n" +
                    "Failed rows: " +
                    report.FailedRows.Count +
                    "\r\n\r\n" +
                    "The part was rebuilt but not saved automatically.\r\n\r\n" +
                    "Report:\r\n" +
                    report.ReportPath,
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

        private void CheckAllButton_Click(
            object sender,
            EventArgs e)
        {
            SetCheckedStateForAllRows(true);
        }

        private void UncheckAllButton_Click(
            object sender,
            EventArgs e)
        {
            SetCheckedStateForAllRows(false);
        }

        private void SetCheckedStateForAllRows(
            bool isChecked)
        {
            foreach (DataGridViewRow gridRow
                     in grid.Rows)
            {
                CutListProfilePropertyCommand
                    .CutListGridRow row =
                        gridRow.Tag
                        as CutListProfilePropertyCommand
                            .CutListGridRow;

                if (row == null)
                    continue;

                row.Apply =
                    isChecked;

                WriteGridRowFromModel(
                    gridRow,
                    row);
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

        private static void ApplyRowStatusStyle(
            DataGridViewRow gridRow,
            string status)
        {
            if (gridRow == null)
                return;

            DataGridViewCell statusCell =
                gridRow.Cells[ColumnStatus];

            statusCell.Style.BackColor =
                Color.White;
            statusCell.Style.ForeColor =
                Color.Black;

            if (status == null)
                return;

            if (status.StartsWith(
                    "!",
                    StringComparison.OrdinalIgnoreCase) ||
                status.IndexOf(
                    "failed",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf(
                    "blank",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor =
                    Color.MistyRose;
                statusCell.Style.ForeColor =
                    Color.DarkRed;
            }
            else if (status.IndexOf(
                         "updated",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor =
                    Color.Honeydew;
                statusCell.Style.ForeColor =
                    Color.DarkGreen;
            }
            else if (status.IndexOf(
                         "skipped",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusCell.Style.BackColor =
                    Color.LightYellow;
                statusCell.Style.ForeColor =
                    Color.DarkGoldenrod;
            }
        }
    }
}