using System;
using System.IO;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment =
    CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// Explicitly selects which SOLIDWORKS custom-property layer is used.
    ///
    /// Document scope means general file custom properties.
    /// ActiveConfiguration scope means configuration-specific custom properties
    /// for the configuration that is currently active in the model.
    /// </summary>
    internal enum CabinPropertyScope
    {
        Document = 0,
        ActiveConfiguration = 1
    }

    /// <summary>
    /// Read-only snapshot of a SOLIDWORKS custom property.
    /// </summary>
    internal sealed class CabinCustomPropertyValue
    {
        public string Name { get; set; }
        public string ScopeName { get; set; }
        public string RawValue { get; set; }
        public string ResolvedValue { get; set; }
        public bool WasResolved { get; set; }
        public bool IsLinked { get; set; }
        public int Type { get; set; }
    }

    /// <summary>
    /// Safe, non-destructive custom-property access layer for Cabin Tools.
    ///
    /// This class deliberately keeps general and configuration-specific
    /// properties separate. This is essential where both layers use the same
    /// property name, for example "Description".
    /// </summary>
    internal static class CabinCustomPropertyStore
    {
        public const string DescriptionPropertyName = "Description";

        public static IModelDoc2 GetActiveModelDocument()
        {
            try
            {
                ISldWorks application =
                    SwEnvironment.Application.UnsafeObject;

                if (application == null)
                    return null;

                return application.ActiveDoc as IModelDoc2;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsSupportedDocument(IModelDoc2 modelDoc)
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

        public static bool IsDrawing(IModelDoc2 modelDoc)
        {
            return modelDoc != null &&
                   modelDoc.GetType() ==
                       (int)swDocumentTypes_e.swDocDRAWING;
        }

        public static bool SupportsConfigurationProperties(
            IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return false;

            int documentType = modelDoc.GetType();

            return documentType ==
                       (int)swDocumentTypes_e.swDocPART ||
                   documentType ==
                       (int)swDocumentTypes_e.swDocASSEMBLY;
        }

        public static string GetDocumentTypeName(IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return "No active document";

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

        public static string GetActiveConfigurationName(
            IModelDoc2 modelDoc)
        {
            if (!SupportsConfigurationProperties(modelDoc))
                return string.Empty;

            IConfigurationManager configurationManager =
                modelDoc.ConfigurationManager;

            if (configurationManager == null)
                return string.Empty;

            IConfiguration activeConfiguration =
                configurationManager.ActiveConfiguration;

            if (activeConfiguration == null)
                return string.Empty;

            return activeConfiguration.Name ?? string.Empty;
        }

        public static CabinCustomPropertyValue Read(
            IModelDoc2 modelDoc,
            string propertyName,
            CabinPropertyScope scope)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException(
                    "A custom property name is required.",
                    "propertyName");
            }

            ICustomPropertyManager propertyManager =
                GetPropertyManager(modelDoc, scope);

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

            return new CabinCustomPropertyValue
            {
                Name = propertyName,
                ScopeName = GetScopeDisplayName(modelDoc, scope),
                RawValue = rawValue ?? string.Empty,
                ResolvedValue = resolvedValue ?? string.Empty,
                WasResolved = wasResolved,
                IsLinked = linked,
                Type = propertyType
            };
        }

        public static string ReadText(
            IModelDoc2 modelDoc,
            string propertyName,
            CabinPropertyScope scope,
            bool resolved)
        {
            CabinCustomPropertyValue property =
                Read(modelDoc, propertyName, scope);

            string value =
                resolved &&
                !string.IsNullOrWhiteSpace(
                    property.ResolvedValue)
                    ? property.ResolvedValue
                    : property.RawValue;

            return value == null
                ? string.Empty
                : value.Trim();
        }

        public static void SetText(
            IModelDoc2 modelDoc,
            string propertyName,
            string propertyValue,
            CabinPropertyScope scope)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException(
                    "A custom property name is required.",
                    "propertyName");
            }

            EnsureCanWrite(modelDoc);

            ICustomPropertyManager propertyManager =
                GetPropertyManager(modelDoc, scope);

            propertyManager.Add3(
                propertyName,
                (int)swCustomInfoType_e.swCustomInfoText,
                propertyValue ?? string.Empty,
                (int)swCustomPropertyAddOption_e
                    .swCustomPropertyReplaceValue);
        }

        public static string GetWriteBlockReason(
            IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
            {
                return "No SOLIDWORKS document is active.";
            }

            string documentPath =
                modelDoc.GetPathName();

            // SOLIDWORKS allows custom properties to be edited in an
            // unsaved document. The user must still save afterwards.
            if (string.IsNullOrWhiteSpace(documentPath))
                return string.Empty;

            if (!File.Exists(documentPath))
            {
                return
                    "The active document path does not exist. " +
                    "Save or reopen the document before editing properties.";
            }

            FileAttributes attributes =
                File.GetAttributes(documentPath);

            if ((attributes & FileAttributes.ReadOnly) ==
                FileAttributes.ReadOnly)
            {
                return
                    "The active document is read-only. " +
                    "In SOLIDWORKS PDM, check out the file before editing properties.";
            }

            return string.Empty;
        }

        public static void EnsureCanWrite(IModelDoc2 modelDoc)
        {
            string blockReason =
                GetWriteBlockReason(modelDoc);

            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                throw new InvalidOperationException(
                    blockReason);
            }
        }

        public static string GetScopeDisplayName(
            IModelDoc2 modelDoc,
            CabinPropertyScope scope)
        {
            if (scope == CabinPropertyScope.Document)
            {
                return "Document / general custom properties";
            }

            string configurationName =
                GetActiveConfigurationName(modelDoc);

            return string.IsNullOrWhiteSpace(configurationName)
                ? "Active configuration"
                : "Configuration: " + configurationName;
        }

        private static ICustomPropertyManager GetPropertyManager(
            IModelDoc2 modelDoc,
            CabinPropertyScope scope)
        {
            if (!IsSupportedDocument(modelDoc))
            {
                throw new InvalidOperationException(
                    "The active document must be a part, assembly, or drawing.");
            }

            if (scope ==
                CabinPropertyScope.ActiveConfiguration &&
                !SupportsConfigurationProperties(modelDoc))
            {
                throw new InvalidOperationException(
                    "Configuration-specific custom properties are available only " +
                    "for parts and assemblies.");
            }

            IModelDocExtension extension =
                modelDoc.Extension;

            if (extension == null)
            {
                throw new InvalidOperationException(
                    "Could not access the SOLIDWORKS document extension.");
            }

            string configurationName = string.Empty;

            if (scope ==
                CabinPropertyScope.ActiveConfiguration)
            {
                configurationName =
                    GetActiveConfigurationName(modelDoc);

                if (string.IsNullOrWhiteSpace(configurationName))
                {
                    throw new InvalidOperationException(
                        "The active configuration could not be determined.");
                }
            }

            ICustomPropertyManager propertyManager =
                extension.CustomPropertyManager[
                    configurationName];

            if (propertyManager == null)
            {
                throw new InvalidOperationException(
                    "Could not access " +
                    GetScopeDisplayName(modelDoc, scope) +
                    ".");
            }

            return propertyManager;
        }
    }
}
