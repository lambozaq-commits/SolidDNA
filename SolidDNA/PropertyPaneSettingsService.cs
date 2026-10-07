using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SolidDNA
{
    internal sealed class PropertyPaneSettingsService
    {
        private const string FileName = "PropertyPaneProfile.xml";
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(PropertyPaneProfile));
        private readonly PropertyPaneWorkbookService workbookService = new PropertyPaneWorkbookService();

        public bool SharedWorkbookLoaded { get; private set; }
        public string SharedWorkbookStatus { get; private set; }

        public string SettingsFolder
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "CabinTools",
                    "Settings");
            }
        }

        public string ProfilePath
        {
            get { return Path.Combine(SettingsFolder, FileName); }
        }

        public PropertyPaneProfile Load()
        {
            Directory.CreateDirectory(SettingsFolder);

            PropertyPaneProfile profile;

            if (!File.Exists(ProfilePath))
            {
                profile = PropertyPaneDefaults.Create();
                Save(profile);
            }
            else
            {
                try
                {
                    using (FileStream stream = File.OpenRead(ProfilePath))
                    {
                        profile = serializer.Deserialize(stream) as PropertyPaneProfile;
                        Validate(profile);
                        Normalize(profile);
                    }
                }
                catch (Exception ex)
                {
                    string backup = Backup("invalid");
                    PropertyPaneLogger.Write("Settings load failed; defaults restored. " + ex.Message + " Backup: " + backup);
                    profile = PropertyPaneDefaults.Create();
                    Save(profile);
                }
            }

            ApplySharedWorkbook(profile);
            return profile;
        }

        public PropertyPaneWorkbookLoadResult CheckSharedWorkbook(string configuredPath)
        {
            PropertyPaneProfile testProfile = PropertyPaneDefaults.Create();
            return workbookService.Apply(configuredPath, testProfile);
        }

        private void ApplySharedWorkbook(PropertyPaneProfile profile)
        {
            SharedWorkbookLoaded = false;
            SharedWorkbookStatus = "Lists: local profile";

            if (profile == null || !profile.UseSharedWorkbook)
                return;

            try
            {
                PropertyPaneWorkbookLoadResult result = workbookService.Apply(profile.SharedWorkbookPath, profile);
                SharedWorkbookLoaded = true;
                SharedWorkbookStatus = "Lists: shared Excel (" + result.ListCount + " lists)";
                PropertyPaneLogger.Write("Shared workbook loaded: " + result.ResolvedPath + ". " +
                    result.ListCount + " lists, " + result.MappingCount + " drawing mappings.");
            }
            catch (Exception ex)
            {
                SharedWorkbookStatus = "Shared Excel unavailable; local fallback";
                PropertyPaneLogger.Write("Shared workbook load failed; local profile retained. " + ex.Message);
            }
        }

        public void Save(PropertyPaneProfile profile)
        {
            Validate(profile);
            Normalize(profile);
            Directory.CreateDirectory(SettingsFolder);

            string temporaryPath = ProfilePath + ".tmp";
            using (FileStream stream = File.Create(temporaryPath))
            {
                serializer.Serialize(stream, profile);
            }

            if (File.Exists(ProfilePath))
                File.Replace(temporaryPath, ProfilePath, null);
            else
                File.Move(temporaryPath, ProfilePath);
        }

        public PropertyPaneProfile Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("The selected profile does not exist.", sourcePath);

            PropertyPaneProfile profile;
            using (FileStream stream = File.OpenRead(sourcePath))
            {
                profile = serializer.Deserialize(stream) as PropertyPaneProfile;
            }

            Validate(profile);
            Normalize(profile);
            Backup("before-import");
            Save(profile);
            return profile;
        }

        public void Export(PropertyPaneProfile profile, string destinationPath)
        {
            Validate(profile);
            using (FileStream stream = File.Create(destinationPath))
            {
                serializer.Serialize(stream, profile);
            }
        }

        public PropertyPaneProfile Reset()
        {
            Backup("before-reset");
            PropertyPaneProfile profile = PropertyPaneDefaults.Create();
            Save(profile);
            return profile;
        }

        private string Backup(string reason)
        {
            if (!File.Exists(ProfilePath))
                return string.Empty;

            string folder = Path.Combine(SettingsFolder, "Backups");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder,
                Path.GetFileNameWithoutExtension(FileName) + "_" + reason + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".xml");
            File.Copy(ProfilePath, path, false);
            return path;
        }

        private static void Validate(PropertyPaneProfile profile)
        {
            if (profile == null)
                throw new InvalidDataException("The profile is empty.");
            if (profile.SchemaVersion != PropertyPaneDefaults.CurrentSchemaVersion)
                throw new InvalidDataException("Unsupported property-pane profile schema " + profile.SchemaVersion + ".");
            if (profile.Layouts == null || profile.Layouts.Count == 0)
                throw new InvalidDataException("The profile contains no layouts.");

            foreach (PropertyPaneLayout layout in profile.Layouts)
            {
                if (layout.Groups == null)
                    throw new InvalidDataException(layout.DocumentKind + " layout has no group collection.");
                foreach (PropertyPaneField field in layout.Groups.SelectMany(g => g.Fields ?? new List<PropertyPaneField>()))
                {
                    if (string.IsNullOrWhiteSpace(field.Id) || string.IsNullOrWhiteSpace(field.PropertyName))
                        throw new InvalidDataException("Every field requires a stable ID and property name.");
                }
            }
        }

        private static void Normalize(PropertyPaneProfile profile)
        {
            profile.Lists = profile.Lists ?? new List<PropertyPaneNamedList>();
            profile.SharedWorkbookPath = profile.SharedWorkbookPath ?? string.Empty;
            profile.DrawingNumberMappings = profile.DrawingNumberMappings ?? new List<DrawingNumberMap>();
            profile.Dimensions = profile.Dimensions ?? new List<DimensionMap>();
            profile.PropertyCatalog = profile.PropertyCatalog ?? new List<string>();

            foreach (PropertyPaneLayout layout in profile.Layouts)
            {
                layout.Groups = layout.Groups ?? new List<PropertyPaneGroup>();
                layout.Groups = layout.Groups.OrderBy(g => g.Order).ToList();
                foreach (PropertyPaneGroup group in layout.Groups)
                {
                    group.Fields = (group.Fields ?? new List<PropertyPaneField>())
                        .OrderBy(f => f.Order).ToList();
                    foreach (PropertyPaneField field in group.Fields)
                    {
                        field.AllowedScopes = field.AllowedScopes == null || field.AllowedScopes.Count == 0
                            ? new List<PropertyPaneScope> { PropertyPaneScope.Document }
                            : field.AllowedScopes.Distinct().ToList();
                    }
                }
            }
        }
    }
}
