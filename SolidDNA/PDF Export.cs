using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// Determines how the output PDF file name is generated.
    /// </summary>
    internal enum PdfExportNamingMode
    {
        AutomaticFromProperties,
        ManualFileNames
    }

    /// <summary>
    /// Determines whether the user exports only checked rows or all valid rows.
    /// </summary>
    internal enum PdfBatchExportScope
    {
        CheckedRows,
        AllValidRows
    }

    /// <summary>
    /// Safe default is Skip. Overwrite is explicit and affects PDF files only.
    /// </summary>
    internal enum PdfExistingFileBehavior
    {
        Skip,
        Overwrite
    }

    /// <summary>
    /// Controls whether the form applies one common destination folder or
    /// stores a destination independently on each drawing row.
    /// </summary>
    internal enum PdfOutputFolderMode
    {
        CommonFolder,
        IndividualFolders
    }

    /// <summary>
    /// User-selected export settings supplied by PdfBatchExportForm.
    /// </summary>
    internal sealed class PdfBatchExportOptions
    {
        public PdfExportNamingMode NamingMode { get; set; }
        public PdfBatchExportScope Scope { get; set; }
        public PdfExistingFileBehavior ExistingFileBehavior { get; set; }
        public PdfOutputFolderMode FolderMode { get; set; }
        public bool LeaveDrawingsOpenedByCabinToolsOpen { get; set; }
        public bool OpenExportedPdfFilesAfterExport { get; set; }
    }

    /// <summary>
    /// One selected SOLIDWORKS drawing and its PDF export metadata.
    /// </summary>
    internal sealed class PdfBatchExportItem
    {
        public bool SelectedForExport { get; set; }
        public string SourcePath { get; set; }
        public string DrawingName { get; set; }
        public string OutputFolder { get; set; }
        public string AutoFileName { get; set; }
        public string ManualFileName { get; set; }
        public CabinNamingValues NamingValues { get; set; }
        public List<string> MissingNamingProperties { get; set; }
        public string PreflightError { get; set; }
        public bool PropertiesRead { get; set; }
        public string Status { get; set; }

        public PdfBatchExportItem()
        {
            MissingNamingProperties = new List<string>();
            SourcePath = string.Empty;
            DrawingName = string.Empty;
            OutputFolder = string.Empty;
            AutoFileName = string.Empty;
            ManualFileName = string.Empty;
            PreflightError = string.Empty;
            PropertiesRead = false;
            Status = string.Empty;
        }

        public bool HasMissingNamingProperties
        {
            get
            {
                return MissingNamingProperties != null &&
                       MissingNamingProperties.Count > 0;
            }
        }

        public bool HasPreflightError
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PreflightError);
            }
        }

        public void UpdateDisplayStatus(PdfExportNamingMode namingMode)
        {
            if (!PropertiesRead)
            {
                if (namingMode == PdfExportNamingMode.AutomaticFromProperties)
                {
                    Status =
                        "Pending property check - click Refresh properties or export to read naming values.";
                }
                else
                {
                    Status =
                        "Ready for manual naming. Properties have not been checked.";
                }

                return;
            }

            string reason = PdfExportCommand.GetExportBlockReason(
                this,
                namingMode);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Status = "! " + reason;
                return;
            }

            if (HasMissingNamingProperties &&
                namingMode == PdfExportNamingMode.ManualFileNames)
            {
                Status =
                    "! Manual export allowed. Auto-name properties missing: " +
                    string.Join(", ", MissingNamingProperties);

                return;
            }

            Status = "Ready";
        }
    }

    /// <summary>
    /// One result from one batch row. The source drawing is never saved.
    /// </summary>
    internal sealed class PdfBatchExportResult
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public bool Succeeded { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Small per-user preference store for the PDF batch form.
    /// This stores interface choices only; it never writes to SOLIDWORKS files.
    /// </summary>
    internal sealed class PdfBatchExportUserPreferences
    {
        private const string SettingsFileName = "PdfBatchExportSettings.txt";

        public bool LeaveDrawingsOpen { get; set; }
        public bool OpenExportedPdfFiles { get; set; }
        public PdfOutputFolderMode FolderMode { get; set; }
        public string LastBatchOutputFolder { get; set; }

        public PdfBatchExportUserPreferences()
        {
            // Defaults requested for the first use. Later changes are remembered.
            LeaveDrawingsOpen = true;
            OpenExportedPdfFiles = true;
            FolderMode = PdfOutputFolderMode.CommonFolder;
            LastBatchOutputFolder = string.Empty;
        }

        public static PdfBatchExportUserPreferences Load()
        {
            PdfBatchExportUserPreferences preferences =
                new PdfBatchExportUserPreferences();

            try
            {
                string path = GetSettingsPath();

                if (!File.Exists(path))
                    return preferences;

                foreach (string line in File.ReadAllLines(path))
                {
                    int separator = line.IndexOf('=');

                    if (separator <= 0)
                        continue;

                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);

                    if (string.Equals(key, "LeaveDrawingsOpen", StringComparison.OrdinalIgnoreCase))
                    {
                        preferences.LeaveDrawingsOpen = value == "1";
                    }
                    else if (string.Equals(key, "OpenExportedPdfFiles", StringComparison.OrdinalIgnoreCase))
                    {
                        preferences.OpenExportedPdfFiles = value == "1";
                    }
                    else if (string.Equals(key, "FolderMode", StringComparison.OrdinalIgnoreCase))
                    {
                        PdfOutputFolderMode folderMode;

                        if (Enum.TryParse(value, true, out folderMode))
                        {
                            preferences.FolderMode = folderMode;
                        }
                    }
                    else if (string.Equals(key, "LastBatchOutputFolder", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            preferences.LastBatchOutputFolder = Encoding.UTF8.GetString(
                                Convert.FromBase64String(value));
                        }
                        catch
                        {
                            preferences.LastBatchOutputFolder = string.Empty;
                        }
                    }
                }
            }
            catch
            {
                // Retain safe in-memory defaults if the settings file cannot be read.
            }

            return preferences;
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                string directory = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string encodedFolder = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(LastBatchOutputFolder ?? string.Empty));

                File.WriteAllLines(path, new string[]
                {
                    "LeaveDrawingsOpen=" + (LeaveDrawingsOpen ? "1" : "0"),
                    "OpenExportedPdfFiles=" + (OpenExportedPdfFiles ? "1" : "0"),
                    "FolderMode=" + FolderMode.ToString(),
                    "LastBatchOutputFolder=" + encodedFolder
                });
            }
            catch
            {
                // A settings-file failure must never block a PDF export.
            }
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData),
                "CabinTools",
                "Settings",
                SettingsFileName);
        }
    }

    /// <summary>
    /// Batch PDF export for SOLIDWORKS drawings.
    ///
    /// Safety rules:
    /// - Reads naming properties only. It never writes custom properties.
    /// - Never writes source custom properties.
    /// - Never saves the source drawing.
    /// - Opens drawings silently only when necessary and closes only drawings
    ///   that Cabin Tools opened, unless the user requests that they remain open.
    /// - Existing PDFs are skipped by default.
    /// </summary>
    public static class PdfExportCommand
    {
        /// <summary>
        /// Backward-compatible entry point used by the earlier taskpane.
        /// It now opens the multi-drawing automatic-naming form.
        /// </summary>
        public static void ExportActiveDrawingToPdf()
        {
            ShowAutoNamedBatchExport();
        }

        public static void ShowAutoNamedBatchExport()
        {
            ShowBatchExport(
                PdfExportNamingMode.AutomaticFromProperties);
        }

        public static void ShowManualNamedBatchExport()
        {
            ShowBatchExport(
                PdfExportNamingMode.ManualFileNames);
        }

        internal static string GetActiveSavedDrawingPath(
            ISldWorks swApp)
        {
            try
            {
                if (swApp == null)
                    return string.Empty;

                IModelDoc2 activeDocument =
                    swApp.ActiveDoc as IModelDoc2;

                if (!CabinPropertyService.IsDrawing(
                        activeDocument))
                {
                    return string.Empty;
                }

                string drawingPath = activeDocument.GetPathName();

                return !string.IsNullOrWhiteSpace(drawingPath) &&
                       File.Exists(drawingPath)
                    ? drawingPath
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static PdfBatchExportItem CreateBatchItem(
            ISldWorks swApp,
            string drawingPath,
            PdfExportNamingMode namingMode)
        {
            PdfBatchExportItem item = new PdfBatchExportItem();

            item.SelectedForExport = true;
            item.SourcePath = NormalizePath(drawingPath);
            item.DrawingName = Path.GetFileName(item.SourcePath);
            item.OutputFolder = GetSourceDrawingDirectory(item.SourcePath);
            item.ManualFileName = BuildDefaultManualFileName(
                item.SourcePath);

            // Selecting files must not open them in SOLIDWORKS. Property values
            // are loaded only on explicit Refresh properties or immediately before
            // automatic export.
            item.PropertiesRead = false;
            item.UpdateDisplayStatus(namingMode);

            return item;
        }

        internal static void RefreshBatchItemPreflight(
            ISldWorks swApp,
            PdfBatchExportItem item,
            PdfExportNamingMode namingMode)
        {
            if (item == null)
                return;

            item.PreflightError = string.Empty;
            item.NamingValues = null;
            item.AutoFileName = string.Empty;
            item.MissingNamingProperties = new List<string>();
            item.PropertiesRead = false;

            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                !File.Exists(item.SourcePath))
            {
                item.PreflightError =
                    "Source drawing was not found.";
                item.PropertiesRead = true;

                item.UpdateDisplayStatus(namingMode);
                return;
            }

            IModelDoc2 drawing = null;
            bool openedByCabinTools = false;

            try
            {
                drawing = GetOpenDrawing(
                    swApp,
                    item.SourcePath);

                if (drawing == null)
                {
                    drawing = OpenDrawingSilently(
                        swApp,
                        item.SourcePath);

                    openedByCabinTools = true;
                }

                if (!CabinPropertyService.IsDrawing(drawing))
                {
                    throw new InvalidOperationException(
                        "The selected file is not a SOLIDWORKS drawing.");
                }

                CabinNamingValues values =
                    CabinPropertyService.ReadNamingValues(drawing);

                item.NamingValues = values;
                item.MissingNamingProperties =
                    CabinPropertyRules.GetMissingPdfNamingProperties(
                        values);

                if (!item.HasMissingNamingProperties)
                {
                    item.AutoFileName =
                        CabinPropertyRules.BuildPdfFileName(values);
                }

                if (string.IsNullOrWhiteSpace(item.ManualFileName))
                {
                    item.ManualFileName =
                        BuildDefaultManualFileName(item.SourcePath);
                }
            }
            catch (Exception ex)
            {
                item.PreflightError = ex.Message;
            }
            finally
            {
                if (openedByCabinTools)
                {
                    CloseDrawingOpenedByCabinTools(
                        swApp,
                        drawing);
                }
            }

            item.PropertiesRead = true;
            item.UpdateDisplayStatus(namingMode);
        }

        internal static string GetExportBlockReason(
            PdfBatchExportItem item,
            PdfExportNamingMode namingMode)
        {
            if (item == null)
                return "No drawing row is available.";

            if (item.HasPreflightError)
                return "Could not read drawing properties: " +
                       item.PreflightError;

            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                !File.Exists(item.SourcePath))
            {
                return "Source drawing was not found.";
            }

            if (string.IsNullOrWhiteSpace(item.OutputFolder))
            {
                return "Output folder is not selected.";
            }

            if (!Directory.Exists(item.OutputFolder))
            {
                return "Output folder does not exist.";
            }

            if (namingMode ==
                PdfExportNamingMode.AutomaticFromProperties)
            {
                if (!item.PropertiesRead)
                {
                    return "Properties have not been read. Click Refresh properties.";
                }

                if (item.HasMissingNamingProperties)
                {
                    return "Missing PDF naming properties: " +
                           string.Join(", ",
                               item.MissingNamingProperties);
                }

                if (string.IsNullOrWhiteSpace(item.AutoFileName))
                {
                    return "Automatic PDF filename could not be generated.";
                }
            }
            else
            {
                string manualNameReason =
                    GetManualFileNameError(item.ManualFileName);

                if (!string.IsNullOrWhiteSpace(manualNameReason))
                {
                    return manualNameReason;
                }
            }

            return string.Empty;
        }

        internal static string GetOutputPath(
            PdfBatchExportItem item,
            PdfExportNamingMode namingMode)
        {
            if (item == null)
                return string.Empty;

            string fileName = namingMode ==
                PdfExportNamingMode.AutomaticFromProperties
                ? item.AutoFileName
                : EnsurePdfExtension(item.ManualFileName);

            if (string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(item.OutputFolder))
            {
                return string.Empty;
            }

            return Path.Combine(item.OutputFolder, fileName);
        }

        internal static string GetManualFileNameError(
            string manualFileName)
        {
            string fileName = manualFileName == null
                ? string.Empty
                : manualFileName.Trim();

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Enter a manual PDF filename.";
            }

            if (fileName.IndexOf('\\') >= 0 ||
                fileName.IndexOf('/') >= 0)
            {
                return "Manual PDF filename must not include a folder path.";
            }

            fileName = EnsurePdfExtension(fileName);

            if (fileName.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0)
            {
                return "Manual PDF filename contains invalid characters.";
            }

            if (string.Equals(fileName, ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Enter a PDF filename before the .pdf extension.";
            }

            return string.Empty;
        }

        private static void ShowBatchExport(
            PdfExportNamingMode namingMode)
        {
            ISldWorks swApp = GetSolidWorksApplication();

            if (swApp == null)
            {
                ShowError("SOLIDWORKS connection is not available.");
                return;
            }

            try
            {
                string activeDrawingPath =
                    GetActiveSavedDrawingPath(swApp);

                using (PdfBatchExportForm form =
                    new PdfBatchExportForm(
                        swApp,
                        namingMode,
                        activeDrawingPath))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }

                    List<PdfBatchExportResult> results =
                        ExportBatch(
                            swApp,
                            form.DrawingItems,
                            form.ExportOptions);

                    string reportPath = WriteExportReport(
                        results,
                        form.ExportOptions);

                    ShowResults(
                        results,
                        reportPath,
                        form.ExportOptions);
                }
            }
            catch (Exception ex)
            {
                ShowError(
                    "Batch PDF export failed.\n\n" +
                    ex.Message);
            }
            finally
            {
                CabinToolsTaskpaneHost.RefreshActiveDocument();
            }
        }

        private static ISldWorks GetSolidWorksApplication()
        {
            try
            {
                return SwEnvironment.Application
                    .UnsafeObject as ISldWorks;
            }
            catch
            {
                return null;
            }
        }

        private static List<PdfBatchExportResult> ExportBatch(
            ISldWorks swApp,
            IList<PdfBatchExportItem> items,
            PdfBatchExportOptions options)
        {
            List<PdfBatchExportResult> results =
                new List<PdfBatchExportResult>();

            if (items == null || items.Count == 0)
            {
                return results;
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            List<PdfBatchExportItem> exportCandidates =
                new List<PdfBatchExportItem>();

            foreach (PdfBatchExportItem item in items)
            {
                bool requested = options.Scope ==
                    PdfBatchExportScope.AllValidRows ||
                    item.SelectedForExport;

                if (!requested)
                    continue;

                string blockReason = GetExportBlockReason(
                    item,
                    options.NamingMode);

                if (!string.IsNullOrWhiteSpace(blockReason))
                {
                    results.Add(CreateSkippedResult(
                        item,
                        string.Empty,
                        blockReason));

                    continue;
                }

                exportCandidates.Add(item);
            }

            Dictionary<string, List<PdfBatchExportItem>>
                itemsByOutputPath =
                    BuildOutputPathLookup(
                        exportCandidates,
                        options.NamingMode);

            List<PdfBatchExportItem> uniqueCandidates =
                new List<PdfBatchExportItem>();

            foreach (PdfBatchExportItem item in exportCandidates)
            {
                string outputPath = GetOutputPath(
                    item,
                    options.NamingMode);

                List<PdfBatchExportItem> sameOutputItems =
                    itemsByOutputPath[outputPath];

                if (sameOutputItems.Count > 1)
                {
                    results.Add(CreateSkippedResult(
                        item,
                        outputPath,
                        "Duplicate PDF output path in the selected batch."));

                    continue;
                }

                uniqueCandidates.Add(item);
            }

            foreach (PdfBatchExportItem item in uniqueCandidates)
            {
                string outputPath = GetOutputPath(
                    item,
                    options.NamingMode);

                if (File.Exists(outputPath) &&
                    options.ExistingFileBehavior ==
                        PdfExistingFileBehavior.Skip)
                {
                    results.Add(CreateSkippedResult(
                        item,
                        outputPath,
                        "PDF already exists. Existing-file setting is Skip."));

                    continue;
                }

                PdfBatchExportResult result =
                    new PdfBatchExportResult();

                result.SourcePath = item.SourcePath;
                result.OutputPath = outputPath;

                string temporaryPdfPath = string.Empty;

                try
                {
                    string exportPath = outputPath;

                    if (File.Exists(outputPath) &&
                        options.ExistingFileBehavior ==
                            PdfExistingFileBehavior.Overwrite)
                    {
                        // Preserve the existing PDF until SOLIDWORKS has
                        // successfully generated a complete replacement.
                        temporaryPdfPath =
                            BuildTemporaryPdfPath(outputPath);

                        exportPath = temporaryPdfPath;
                    }

                    ExportOneDrawingToPdf(
                        swApp,
                        item.SourcePath,
                        exportPath,
                        options.LeaveDrawingsOpenedByCabinToolsOpen);

                    if (!string.IsNullOrWhiteSpace(temporaryPdfPath))
                    {
                        File.Copy(
                            temporaryPdfPath,
                            outputPath,
                            true);

                        File.Delete(temporaryPdfPath);
                        temporaryPdfPath = string.Empty;
                    }

                    result.Succeeded = true;
                    result.Skipped = false;
                    result.Message = "PDF exported.";

                    if (options.OpenExportedPdfFilesAfterExport)
                    {
                        string openMessage = TryOpenPdf(outputPath);

                        if (!string.IsNullOrWhiteSpace(openMessage))
                        {
                            result.Message += " " + openMessage;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Succeeded = false;
                    result.Skipped = false;
                    result.Message = ex.Message;
                }
                finally
                {
                    TryDeleteTemporaryPdf(temporaryPdfPath);
                }

                results.Add(result);
            }

            return results;
        }

        private static Dictionary<string, List<PdfBatchExportItem>>
            BuildOutputPathLookup(
                IList<PdfBatchExportItem> items,
                PdfExportNamingMode namingMode)
        {
            Dictionary<string, List<PdfBatchExportItem>> lookup =
                new Dictionary<string, List<PdfBatchExportItem>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (PdfBatchExportItem item in items)
            {
                string outputPath = GetOutputPath(item, namingMode);

                List<PdfBatchExportItem> sameOutputItems;

                if (!lookup.TryGetValue(
                        outputPath,
                        out sameOutputItems))
                {
                    sameOutputItems =
                        new List<PdfBatchExportItem>();

                    lookup.Add(outputPath, sameOutputItems);
                }

                sameOutputItems.Add(item);
            }

            return lookup;
        }

        private static string BuildTemporaryPdfPath(
            string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            string baseName = Path.GetFileNameWithoutExtension(outputPath);

            return Path.Combine(
                directory,
                baseName +
                ".__CabinTools_" +
                Guid.NewGuid().ToString("N") +
                ".pdf");
        }

        private static void TryDeleteTemporaryPdf(
            string temporaryPdfPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(temporaryPdfPath) &&
                    File.Exists(temporaryPdfPath))
                {
                    File.Delete(temporaryPdfPath);
                }
            }
            catch
            {
                // A temporary cleanup failure must not hide the export result.
            }
        }

        private static PdfBatchExportResult CreateSkippedResult(
            PdfBatchExportItem item,
            string outputPath,
            string message)
        {
            return new PdfBatchExportResult
            {
                SourcePath = item == null
                    ? string.Empty
                    : item.SourcePath,
                OutputPath = outputPath ?? string.Empty,
                Succeeded = false,
                Skipped = true,
                Message = message
            };
        }

        private static void ExportOneDrawingToPdf(
            ISldWorks swApp,
            string drawingPath,
            string pdfPath,
            bool leaveDrawingsOpenedByCabinToolsOpen)
        {
            string originalActiveDocumentTitle = GetActiveDocumentTitle(swApp);

            IModelDoc2 drawing = GetOpenDrawing(
                swApp,
                drawingPath);

            bool openedByCabinTools = false;

            if (drawing == null)
            {
                drawing = OpenDrawingSilently(
                    swApp,
                    drawingPath);

                openedByCabinTools = true;

                // SOLIDWORKS must load a drawing to create a PDF. Hide documents
                // opened by Cabin Tools immediately so the user does not have to
                // work through one drawing tab after another during batch export.
                SetDrawingVisibility(drawing, false);
            }

            if (!CabinPropertyService.IsDrawing(drawing))
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS could not open the selected drawing.\n\n" +
                    drawingPath);
            }

            try
            {
                IModelDocExtension extension = drawing.Extension;

                if (extension == null)
                {
                    throw new InvalidOperationException(
                        "Could not access the SOLIDWORKS drawing extension.");
                }

                IExportPdfData pdfData =
                    SwEnvironment.Application.GetPdfExportData();

                if (pdfData == null)
                {
                    throw new InvalidOperationException(
                        "Could not create SOLIDWORKS PDF export data.");
                }

                pdfData.SetSheets(
                    (int)swExportDataSheetsToExport_e
                        .swExportData_ExportAllSheets,
                    null);

                int errors = 0;
                int warnings = 0;

                bool exported = extension.SaveAs(
                    pdfPath,
                    (int)swSaveAsVersion_e
                        .swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e
                        .swSaveAsOptions_Silent,
                    pdfData,
                    ref errors,
                    ref warnings);

                if (!exported)
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS could not export the PDF.\n\n" +
                        "Error code: " + errors.ToString() +
                        "\nWarning code: " + warnings.ToString());
                }

                if (!File.Exists(pdfPath))
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS reported a successful PDF export, but the PDF file was not found.\n\n" +
                        pdfPath);
                }
            }
            finally
            {
                if (openedByCabinTools)
                {
                    if (leaveDrawingsOpenedByCabinToolsOpen)
                    {
                        SetDrawingVisibility(drawing, true);
                    }
                    else
                    {
                        CloseDrawingOpenedByCabinTools(
                            swApp,
                            drawing);
                    }
                }

                RestoreActiveDocument(
                    swApp,
                    originalActiveDocumentTitle);
            }
        }

        private static void SetDrawingVisibility(
            IModelDoc2 drawing,
            bool visible)
        {
            try
            {
                if (drawing != null)
                {
                    drawing.Visible = visible;
                }
            }
            catch
            {
                // Some SOLIDWORKS states can reject visibility changes. The
                // export remains safe because it was still opened silently.
            }
        }

        private static string GetActiveDocumentTitle(
            ISldWorks swApp)
        {
            try
            {
                IModelDoc2 activeDocument = swApp == null
                    ? null
                    : swApp.ActiveDoc as IModelDoc2;

                return activeDocument == null
                    ? string.Empty
                    : activeDocument.GetTitle() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void RestoreActiveDocument(
            ISldWorks swApp,
            string documentTitle)
        {
            try
            {
                if (swApp == null ||
                    string.IsNullOrWhiteSpace(documentTitle))
                {
                    return;
                }

                int errors = 0;

                swApp.ActivateDoc3(
                    documentTitle,
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref errors);
            }
            catch
            {
                // Restoring the prior active document is a user-experience
                // improvement only. It must not hide a completed export.
            }
        }

        private static IModelDoc2 GetOpenDrawing(
            ISldWorks swApp,
            string drawingPath)
        {
            try
            {
                return swApp.GetOpenDocumentByName(
                    drawingPath) as IModelDoc2;
            }
            catch
            {
                return null;
            }
        }

        private static IModelDoc2 OpenDrawingSilently(
            ISldWorks swApp,
            string drawingPath)
        {
            if (swApp == null)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS connection is not available.");
            }

            int errors = 0;
            int warnings = 0;

            IModelDoc2 drawing = swApp.OpenDoc6(
                drawingPath,
                (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e
                    .swOpenDocOptions_Silent,
                string.Empty,
                ref errors,
                ref warnings) as IModelDoc2;

            if (drawing == null)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS could not open the drawing.\n\n" +
                    drawingPath +
                    "\n\nError code: " + errors.ToString() +
                    "\nWarning code: " + warnings.ToString());
            }

            SetDrawingVisibility(drawing, false);

            return drawing;
        }

        private static void CloseDrawingOpenedByCabinTools(
            ISldWorks swApp,
            IModelDoc2 drawing)
        {
            try
            {
                if (swApp == null || drawing == null)
                    return;

                string title = drawing.GetTitle();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    swApp.CloseDoc(title);
                }
            }
            catch
            {
                // Source drawings are never saved by this tool. A closing
                // failure must not hide an already-completed export result.
            }
        }

        private static string TryOpenPdf(string pdfPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(pdfPath)
                {
                    UseShellExecute = true
                });

                return "PDF opened.";
            }
            catch (Exception ex)
            {
                return "PDF was exported but could not be opened: " +
                       ex.Message;
            }
        }

        private static string WriteExportReport(
            IList<PdfBatchExportResult> results,
            PdfBatchExportOptions options)
        {
            try
            {
                string folder = Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments),
                    "CabinTools",
                    "PdfExportReports");

                Directory.CreateDirectory(folder);

                string reportPath = Path.Combine(
                    folder,
                    "PdfExport_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                List<string> lines = new List<string>();

                lines.Add("Cabin Tools - Batch PDF Export");
                lines.Add("Created: " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("Naming mode: " +
                    GetNamingModeDisplayName(options.NamingMode));
                lines.Add("Scope: " +
                    GetScopeDisplayName(options.Scope));
                lines.Add("Existing PDF behavior: " +
                    options.ExistingFileBehavior.ToString());
                lines.Add("Output folder mode: " +
                    options.FolderMode.ToString());
                lines.Add("Leave drawings opened by Cabin Tools open: " +
                    options.LeaveDrawingsOpenedByCabinToolsOpen.ToString());
                lines.Add("Open exported PDFs after export: " +
                    options.OpenExportedPdfFilesAfterExport.ToString());
                lines.Add("Rows processed: " + results.Count.ToString());
                lines.Add(string.Empty);

                foreach (PdfBatchExportResult result in results)
                {
                    lines.Add("Succeeded: " + result.Succeeded.ToString());
                    lines.Add("Skipped: " + result.Skipped.ToString());
                    lines.Add("Source drawing: " + result.SourcePath);
                    lines.Add("Output PDF: " + result.OutputPath);
                    lines.Add("Result: " + result.Message);
                    lines.Add(string.Empty);
                }

                File.WriteAllLines(
                    reportPath,
                    lines.ToArray(),
                    Encoding.UTF8);

                return reportPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ShowResults(
            IList<PdfBatchExportResult> results,
            string reportPath,
            PdfBatchExportOptions options)
        {
            int succeeded = 0;
            int skipped = 0;
            int failed = 0;
            StringBuilder failedDetails = new StringBuilder();

            foreach (PdfBatchExportResult result in results)
            {
                if (result.Succeeded)
                {
                    succeeded++;
                }
                else if (result.Skipped)
                {
                    skipped++;
                }
                else
                {
                    failed++;

                    if (failedDetails.Length < 1200)
                    {
                        failedDetails.AppendLine(
                            Path.GetFileName(result.SourcePath) +
                            ": " + result.Message);
                    }
                }
            }

            StringBuilder message = new StringBuilder();

            message.AppendLine("Batch PDF export completed.");
            message.AppendLine();
            message.AppendLine("Naming mode: " +
                GetNamingModeDisplayName(options.NamingMode));
            message.AppendLine("Exported: " + succeeded.ToString());
            message.AppendLine("Skipped: " + skipped.ToString());
            message.AppendLine("Failed: " + failed.ToString());

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                message.AppendLine();
                message.AppendLine("Export report:");
                message.AppendLine(reportPath);
            }

            if (failedDetails.Length > 0)
            {
                message.AppendLine();
                message.AppendLine("Failed rows:");
                message.Append(failedDetails.ToString());
            }

            SolidWorksMessageBoxIcon icon = failed > 0
                ? SolidWorksMessageBoxIcon.Warning
                : SolidWorksMessageBoxIcon.Information;

            ShowMessage(message.ToString(), icon);
        }

        internal static string EnsurePdfExtension(string fileName)
        {
            string value = fileName == null
                ? string.Empty
                : fileName.Trim();

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.EndsWith(
                ".pdf",
                StringComparison.OrdinalIgnoreCase)
                ? value
                : value + ".pdf";
        }

        private static string BuildDefaultManualFileName(
            string sourcePath)
        {
            string sourceFileName =
                Path.GetFileNameWithoutExtension(sourcePath);

            if (string.IsNullOrWhiteSpace(sourceFileName))
            {
                sourceFileName = "Drawing";
            }

            return CabinPropertyRules.MakeSafeFileName(
                sourceFileName) + ".pdf";
        }

        private static string GetSourceDrawingDirectory(
            string sourcePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(sourcePath);

                return !string.IsNullOrWhiteSpace(directory) &&
                       Directory.Exists(directory)
                    ? directory
                    : System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.MyDocuments);
            }
            catch
            {
                return System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.MyDocuments);
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path ?? string.Empty);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static string GetNamingModeDisplayName(
            PdfExportNamingMode namingMode)
        {
            return namingMode ==
                PdfExportNamingMode.AutomaticFromProperties
                ? "Automatic from drawing properties"
                : "Manual PDF filenames";
        }

        private static string GetScopeDisplayName(
            PdfBatchExportScope scope)
        {
            return scope == PdfBatchExportScope.AllValidRows
                ? "All valid rows"
                : "Checked rows";
        }

        private static void ShowError(string message)
        {
            ShowMessage(message, SolidWorksMessageBoxIcon.Stop);
        }

        private static void ShowMessage(
            string message,
            SolidWorksMessageBoxIcon icon)
        {
            try
            {
                SwEnvironment.Application.ShowMessageBox(
                    message,
                    icon);
            }
            catch
            {
                MessageBox.Show(
                    message,
                    "Cabin Tools",
                    MessageBoxButtons.OK,
                    icon == SolidWorksMessageBoxIcon.Stop
                        ? MessageBoxIcon.Error
                        : MessageBoxIcon.Information);
            }
        }
    }
}
