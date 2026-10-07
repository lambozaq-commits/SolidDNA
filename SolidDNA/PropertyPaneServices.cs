using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    internal sealed class PropertyPaneReadValue
    {
        public string RawValue { get; set; }
        public string ResolvedValue { get; set; }
        public bool IsInherited { get; set; }
        public bool IsMixed { get; set; }
    }

    internal sealed class PropertyPaneEdit
    {
        public string FieldId { get; set; }
        public string PropertyName { get; set; }
        public PropertyPaneScope Scope { get; set; }
        public List<string> SelectedConfigurations { get; set; }
        public string Value { get; set; }
        public bool Clear { get; set; }
    }

    internal sealed class PropertyPaneApplyResult
    {
        public int Applied { get; set; }
        public int Skipped { get; set; }
        public bool RolledBack { get; set; }
        public List<string> Errors { get; private set; }

        public PropertyPaneApplyResult()
        {
            Errors = new List<string>();
        }

        public string Summary
        {
            get
            {
                string text = Applied + " change" + (Applied == 1 ? "" : "s") + " applied";
                if (Skipped > 0)
                    text += ", " + Skipped + " skipped";
                if (RolledBack)
                    text += "; changes were rolled back";
                if (Errors.Count > 0)
                    text += ". " + string.Join(" ", Errors.ToArray());
                return text + ". The SOLIDWORKS file was not saved.";
            }
        }
    }

    internal static class PropertyPaneLogger
    {
        public static void Write(string message)
        {
            try
            {
                string folder = Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.ApplicationData),
                    "CabinTools", "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "PropertyPane.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " - " + message +
                    System.Environment.NewLine);
            }
            catch
            {
                // Logging must never interfere with SOLIDWORKS.
            }
        }
    }

    internal static class DrawingNumberResolver
    {
        public static bool TryResolve(PropertyPaneProfile profile, string cabinDescription,
            string cabinDefined, string layoutType, out string drawingNumber, out string reason)
        {
            drawingNumber = string.Empty;
            reason = string.Empty;

            List<DrawingNumberMap> matches = (profile.DrawingNumberMappings ?? new List<DrawingNumberMap>())
                .Where(m => Same(m.CabinDescription, cabinDescription) &&
                            Same(m.CabinDefined, cabinDefined) &&
                            Same(m.LayoutType, layoutType))
                .ToList();

            if (matches.Count == 0)
            {
                reason = "No drawing-number mapping matches the three drawing fields.";
                return false;
            }

            string[] numbers = matches.Select(m => (m.DrawingNumber ?? string.Empty).Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (numbers.Length != 1 || string.IsNullOrWhiteSpace(numbers[0]))
            {
                reason = "The drawing-number mapping is ambiguous.";
                return false;
            }

            drawingNumber = numbers[0];
            return true;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class PropertyPanePropertyService
    {
        private sealed class PropertySnapshot
        {
            public ICustomPropertyManager Manager { get; set; }
            public string Name { get; set; }
            public bool Existed { get; set; }
            public string Value { get; set; }
            public int Type { get; set; }
        }

        public static string[] GetConfigurationNames(IModelDoc2 model)
        {
            if (!CabinCustomPropertyStore.SupportsConfigurationProperties(model))
                return new string[0];

            object names = model.GetConfigurationNames();
            object[] items = names as object[];
            if (items != null)
                return items.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture))
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            string[] strings = names as string[];
            return strings ?? new string[0];
        }

        public static PropertyPaneReadValue Read(IModelDoc2 model, string propertyName,
            PropertyPaneScope scope, IList<string> selectedConfigurations)
        {
            List<string> targets = ResolveTargets(model, scope, selectedConfigurations);
            List<CabinCustomPropertyValue> values = targets.Select(t => ReadFromManager(model, propertyName, t)).ToList();

            string[] rawValues = values.Select(v => (v.RawValue ?? string.Empty).Trim())
                .Distinct(StringComparer.Ordinal).ToArray();
            string[] resolvedValues = values.Select(v => string.IsNullOrWhiteSpace(v.ResolvedValue)
                    ? (v.RawValue ?? string.Empty).Trim()
                    : v.ResolvedValue.Trim())
                .Distinct(StringComparer.Ordinal).ToArray();

            return new PropertyPaneReadValue
            {
                RawValue = rawValues.Length == 1 ? rawValues[0] : "<varies>",
                ResolvedValue = resolvedValues.Length == 1 ? resolvedValues[0] : "<varies>",
                IsMixed = rawValues.Length > 1 || resolvedValues.Length > 1
            };
        }

        public static PropertyPaneReadValue ReadDescriptionEffective(IModelDoc2 model,
            PropertyPaneScope scope, IList<string> selectedConfigurations)
        {
            PropertyPaneReadValue value = Read(model, CabinCustomPropertyStore.DescriptionPropertyName,
                scope, selectedConfigurations);

            if (scope != PropertyPaneScope.Document && !value.IsMixed && string.IsNullOrWhiteSpace(value.RawValue))
            {
                PropertyPaneReadValue documentValue = Read(model,
                    CabinCustomPropertyStore.DescriptionPropertyName,
                    PropertyPaneScope.Document, null);
                documentValue.IsInherited = true;
                return documentValue;
            }

            return value;
        }

        public static PropertyPaneApplyResult Apply(IModelDoc2 model, IEnumerable<PropertyPaneEdit> edits)
        {
            PropertyPaneApplyResult result = new PropertyPaneApplyResult();
            List<PropertySnapshot> snapshots = new List<PropertySnapshot>();
            List<PropertyPaneEdit> requested = (edits ?? Enumerable.Empty<PropertyPaneEdit>()).ToList();

            CabinCustomPropertyStore.EnsureCanWrite(model);
            try
            {
                foreach (PropertyPaneEdit edit in requested)
                {
                    if (!edit.Clear && string.IsNullOrWhiteSpace(edit.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    foreach (string target in ResolveTargets(model, edit.Scope, edit.SelectedConfigurations))
                    {
                        ICustomPropertyManager manager = GetManager(model, target);
                        PropertySnapshot snapshot = Capture(manager, edit.PropertyName);
                        snapshots.Add(snapshot);

                        if (edit.Clear)
                        {
                            if (snapshot.Existed)
                            {
                                int deleteResult = manager.Delete2(edit.PropertyName);
                                if (deleteResult != 0)
                                    throw new InvalidOperationException("Could not clear '" + edit.PropertyName + "' in " + ScopeName(target) + ".");
                            }
                            else
                            {
                                result.Skipped++;
                                continue;
                            }
                        }
                        else
                        {
                            int type = snapshot.Existed && snapshot.Type > 0
                                ? snapshot.Type
                                : (int)swCustomInfoType_e.swCustomInfoText;
                            int addResult = manager.Add3(edit.PropertyName, type, edit.Value ?? string.Empty,
                                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                            if (addResult != 0)
                                throw new InvalidOperationException("Could not write '" + edit.PropertyName + "' in " + ScopeName(target) + ".");
                        }

                        result.Applied++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                result.RolledBack = RollBack(snapshots, result.Errors);
                PropertyPaneLogger.Write("Property apply failed. " + result.Summary);
                return result;
            }

            PropertyPaneLogger.Write("Property apply succeeded. " + result.Summary);
            return result;
        }

        private static bool RollBack(IEnumerable<PropertySnapshot> snapshots, IList<string> errors)
        {
            bool success = true;
            foreach (PropertySnapshot snapshot in snapshots.Reverse())
            {
                try
                {
                    if (!snapshot.Existed)
                    {
                        snapshot.Manager.Delete2(snapshot.Name);
                    }
                    else
                    {
                        int type = snapshot.Type > 0
                            ? snapshot.Type
                            : (int)swCustomInfoType_e.swCustomInfoText;
                        snapshot.Manager.Add3(snapshot.Name, type, snapshot.Value ?? string.Empty,
                            (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    errors.Add("Rollback failed for '" + snapshot.Name + "': " + ex.Message);
                }
            }
            return success;
        }

        private static PropertySnapshot Capture(ICustomPropertyManager manager, string name)
        {
            string raw;
            string resolved;
            bool wasResolved;
            bool linked;
            int getResult = manager.Get6(name, false, out raw, out resolved, out wasResolved, out linked);
            return new PropertySnapshot
            {
                Manager = manager,
                Name = name,
                // swCustomInfoGetResult_NotPresent is 1. Cached (0) and
                // resolved (2) both mean that the property exists.
                Existed = getResult != 1,
                Value = raw ?? string.Empty,
                Type = manager.GetType2(name)
            };
        }

        private static CabinCustomPropertyValue ReadFromManager(IModelDoc2 model, string name, string configuration)
        {
            ICustomPropertyManager manager = GetManager(model, configuration);
            string raw;
            string resolved;
            bool wasResolved;
            bool linked;
            manager.Get6(name, false, out raw, out resolved, out wasResolved, out linked);
            return new CabinCustomPropertyValue
            {
                Name = name,
                ScopeName = ScopeName(configuration),
                RawValue = raw ?? string.Empty,
                ResolvedValue = resolved ?? string.Empty,
                WasResolved = wasResolved,
                IsLinked = linked,
                Type = manager.GetType2(name)
            };
        }

        private static ICustomPropertyManager GetManager(IModelDoc2 model, string configuration)
        {
            IModelDocExtension extension = model == null ? null : model.Extension;
            if (extension == null)
                throw new InvalidOperationException("Could not access the SOLIDWORKS document extension.");
            ICustomPropertyManager manager = extension.CustomPropertyManager[configuration ?? string.Empty];
            if (manager == null)
                throw new InvalidOperationException("Could not access " + ScopeName(configuration) + ".");
            return manager;
        }

        private static List<string> ResolveTargets(IModelDoc2 model, PropertyPaneScope scope,
            IList<string> selectedConfigurations)
        {
            if (scope == PropertyPaneScope.Document)
                return new List<string> { string.Empty };
            if (!CabinCustomPropertyStore.SupportsConfigurationProperties(model))
                throw new InvalidOperationException("Configuration scope is available only for parts and assemblies.");

            if (scope == PropertyPaneScope.ActiveConfiguration)
            {
                string active = CabinCustomPropertyStore.GetActiveConfigurationName(model);
                if (string.IsNullOrWhiteSpace(active))
                    throw new InvalidOperationException("The active configuration could not be determined.");
                return new List<string> { active };
            }

            string[] available = GetConfigurationNames(model);
            if (scope == PropertyPaneScope.AllConfigurations)
                return available.ToList();

            HashSet<string> valid = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
            List<string> selected = (selectedConfigurations ?? new string[0])
                .Where(s => valid.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("Select at least one configuration.");
            return selected;
        }

        private static string ScopeName(string configuration)
        {
            return string.IsNullOrWhiteSpace(configuration)
                ? "document properties"
                : "configuration '" + configuration + "'";
        }
    }

    internal sealed class DimensionEdit
    {
        public DimensionMap Map { get; set; }
        public double Millimetres { get; set; }
        public PropertyPaneScope Scope { get; set; }
        public List<string> SelectedConfigurations { get; set; }
    }

    internal static class PropertyPaneDimensionService
    {
        private const int ThisConfiguration = 1;
        private const int AllConfigurations = 2;
        private const int SpecificConfigurations = 3;
        private const int SetValueSuccessful = 0;

        private sealed class DimensionSnapshot
        {
            public IDimension Dimension { get; set; }
            public string[] Configurations { get; set; }
            public double[] Values { get; set; }
        }

        public static bool TryReadMillimetres(IModelDoc2 model, DimensionMap map,
            out double millimetres, out string error)
        {
            bool mixed;
            return TryReadMillimetres(model, map, PropertyPaneScope.ActiveConfiguration,
                null, out millimetres, out mixed, out error);
        }

        public static bool TryReadMillimetres(IModelDoc2 model, DimensionMap map,
            PropertyPaneScope scope, IList<string> selectedConfigurations,
            out double millimetres, out bool mixed, out string error)
        {
            millimetres = 0;
            mixed = false;
            error = string.Empty;
            try
            {
                IDimension dimension = FindDimension(model, map);
                string[] targets = ResolveTargetConfigurations(model, scope, selectedConfigurations);
                double[] values = ReadValues(dimension, targets);
                if (values.Length == 0)
                    throw new InvalidOperationException("No dimension value was returned for " + map.Label + ".");
                mixed = values.Any(v => Math.Abs(v - values[0]) > 0.000000001);
                millimetres = values[0] * 1000.0;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static PropertyPaneApplyResult Apply(IModelDoc2 model, IEnumerable<DimensionEdit> edits)
        {
            PropertyPaneApplyResult result = new PropertyPaneApplyResult();
            CabinCustomPropertyStore.EnsureCanWrite(model);
            List<DimensionSnapshot> snapshots = new List<DimensionSnapshot>();

            foreach (DimensionEdit edit in edits ?? Enumerable.Empty<DimensionEdit>())
            {
                if (edit.Map == null)
                    continue;
                if (!edit.Map.AllowNegative && edit.Millimetres <= 0)
                {
                    result.Errors.Add(edit.Map.Label + " must be greater than 0 mm.");
                    continue;
                }

                try
                {
                    IDimension dimension = FindDimension(model, edit.Map);
                    string[] targets = ResolveTargetConfigurations(model, edit.Scope, edit.SelectedConfigurations);
                    snapshots.Add(new DimensionSnapshot
                    {
                        Dimension = dimension,
                        Configurations = targets,
                        Values = ReadValues(dimension, targets)
                    });
                    int option;
                    object names;
                    ResolveSetScope(model, edit.Scope, edit.SelectedConfigurations, out option, out names);
                    int status = dimension.SetSystemValue3(edit.Millimetres / 1000.0, option, names);
                    if (status != SetValueSuccessful)
                        throw new InvalidOperationException(edit.Map.Label + " returned SOLIDWORKS status " + status + ".");
                    result.Applied++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(ex.Message);
                    result.RolledBack = RollBack(snapshots, result.Errors);
                    break;
                }
            }

            PropertyPaneLogger.Write("Dimension apply completed. " + result.Summary);
            return result;
        }

        private static bool RollBack(IEnumerable<DimensionSnapshot> snapshots, IList<string> errors)
        {
            bool success = true;
            foreach (DimensionSnapshot snapshot in snapshots.Reverse())
            {
                int count = Math.Min(snapshot.Configurations.Length, snapshot.Values.Length);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        int status = snapshot.Dimension.SetSystemValue3(
                            snapshot.Values[i], SpecificConfigurations,
                            new[] { snapshot.Configurations[i] });
                        if (status != SetValueSuccessful)
                            throw new InvalidOperationException("SOLIDWORKS status " + status + ".");
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        errors.Add("Dimension rollback failed for configuration '" +
                            snapshot.Configurations[i] + "': " + ex.Message);
                    }
                }
            }
            return success;
        }

        private static IDimension FindDimension(IModelDoc2 model, DimensionMap map)
        {
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Cabin dimensions are available only in part documents.");

            IPartDoc part = model as IPartDoc;
            if (part == null)
                throw new InvalidOperationException("Could not access the active part document.");

            IFeature feature = FindFeatureByName(model, map.FeatureName);
            if (feature == null)
                throw new InvalidOperationException("Required reference plane '" + map.FeatureName + "' was not found.");
            if (!string.Equals(feature.GetTypeName2(), "RefPlane", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Feature '" + map.FeatureName + "' is not a reference plane.");

            IFeature fromPlane = FindFeatureByName(model, map.FromPlane);
            if (fromPlane == null)
                throw new InvalidOperationException("Required reference plane '" + map.FromPlane + "' was not found.");
            if (!string.Equals(fromPlane.GetTypeName2(), "RefPlane", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Feature '" + map.FromPlane + "' is not a reference plane.");

            IDisplayDimension display = feature.GetFirstDisplayDimension() as IDisplayDimension;
            IDimension dimension = display == null ? null : display.GetDimension2(0) as IDimension;
            if (dimension == null)
                throw new InvalidOperationException("Reference plane '" + map.FeatureName + "' has no editable offset dimension.");
            return dimension;
        }

        private static IFeature FindFeatureByName(IModelDoc2 model, string featureName)
        {
            if (model == null || string.IsNullOrWhiteSpace(featureName))
                return null;

            IFeature feature = null;
            try { feature = model.FirstFeature() as IFeature; }
            catch { feature = null; }

            while (feature != null)
            {
                string currentName = string.Empty;
                try { currentName = feature.Name ?? string.Empty; }
                catch { currentName = string.Empty; }

                if (string.Equals(currentName, featureName, StringComparison.OrdinalIgnoreCase))
                    return feature;

                try { feature = feature.GetNextFeature() as IFeature; }
                catch { feature = null; }
            }

            return null;
        }

        private static void ResolveSetScope(IModelDoc2 model, PropertyPaneScope scope,
            IList<string> selectedConfigurations, out int option, out object names)
        {
            names = null;
            if (scope == PropertyPaneScope.AllConfigurations)
            {
                option = AllConfigurations;
                return;
            }
            if (scope == PropertyPaneScope.ActiveConfiguration)
            {
                option = ThisConfiguration;
                return;
            }
            if (scope != PropertyPaneScope.SelectedConfigurations)
                throw new InvalidOperationException("Dimensions require Active, Selected, or All configurations.");

            string[] available = PropertyPanePropertyService.GetConfigurationNames(model);
            HashSet<string> valid = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
            string[] selected = (selectedConfigurations ?? new string[0])
                .Where(s => valid.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException("Select at least one configuration for dimensions.");
            option = SpecificConfigurations;
            names = selected;
        }

        private static string[] ResolveTargetConfigurations(IModelDoc2 model,
            PropertyPaneScope scope, IList<string> selectedConfigurations)
        {
            string[] available = PropertyPanePropertyService.GetConfigurationNames(model);
            if (scope == PropertyPaneScope.ActiveConfiguration)
            {
                string active = CabinCustomPropertyStore.GetActiveConfigurationName(model);
                if (string.IsNullOrWhiteSpace(active))
                    throw new InvalidOperationException("The active configuration could not be determined.");
                return new[] { active };
            }
            if (scope == PropertyPaneScope.AllConfigurations)
                return available;
            if (scope != PropertyPaneScope.SelectedConfigurations)
                throw new InvalidOperationException("Dimensions require Active, Selected, or All configurations.");

            HashSet<string> valid = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
            string[] selected = (selectedConfigurations ?? new string[0])
                .Where(s => valid.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException("Select at least one configuration for dimensions.");
            return selected;
        }

        private static double[] ReadValues(IDimension dimension, string[] configurations)
        {
            object raw = dimension.GetSystemValue3(SpecificConfigurations, configurations);
            Array array = raw as Array;
            if (array != null)
            {
                List<double> values = new List<double>();
                foreach (object item in array)
                    values.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
                return values.ToArray();
            }

            if (raw != null)
                return new[] { Convert.ToDouble(raw, CultureInfo.InvariantCulture) };
            return new double[0];
        }
    }
}
