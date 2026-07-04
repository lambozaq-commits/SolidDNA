using System;
using System.Collections.Generic;
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
    /// Creates up to five SOLIDWORKS 2025 copies of selected drawing files.
    ///
    /// Safety:
    /// - Drawings only.
    /// - The original drawings are never overwritten.
    /// - Output file names are entered and controlled by the user.
    /// - Existing output files are never overwritten.
    /// - No PDM check-in, check-out, vault, or reference action is performed.
    /// </summary>
    public static class PreviousVersionDrawingCommand
    {
        private const int TargetSolidWorksYear = 2025;

        // SOLIDWORKS SaveAsPreviousVersion code:
        // SW2022 = 15000, then +1000 for each later release.
        private const int TargetPreviousVersionCode = 18000;

        public static void ShowSaveDrawingsForm()
        {
            ISldWorks swApp =
                SwEnvironment.Application
                    .UnsafeObject as ISldWorks;

            if (swApp == null)
            {
                ShowError(
                    "SOLIDWORKS connection is not available.");

                return;
            }

            try
            {
                string activeDrawingPath =
                    GetActiveSavedDrawingPath(swApp);

                using (PreviousVersionDrawingForm form =
                    new PreviousVersionDrawingForm(
                        TargetSolidWorksYear,
                        activeDrawingPath))
                {
                    if (form.ShowDialog() !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    List<PreviousVersionDrawingResult> results =
                        SaveSelectedDrawings(
                            swApp,
                            form.DrawingItems);

                    string reportPath =
                        WriteExportReport(
                            results);

                    ShowResults(
                        results,
                        reportPath);
                }
            }
            catch (Exception ex)
            {
                ShowError(
                    "Save Drawings as SOLIDWORKS " +
                    TargetSolidWorksYear.ToString() +
                    " failed.\n\n" +
                    ex.Message);
            }
        }

        private static string GetActiveSavedDrawingPath(
            ISldWorks swApp)
        {
            try
            {
                IModelDoc2 activeDocument =
                    swApp.ActiveDoc as IModelDoc2;

                if (activeDocument == null)
                {
                    return string.Empty;
                }

                if (activeDocument.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    return string.Empty;
                }

                string drawingPath =
                    activeDocument.GetPathName();

                if (string.IsNullOrWhiteSpace(
                    drawingPath) ||
                    !File.Exists(drawingPath))
                {
                    return string.Empty;
                }

                return drawingPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<PreviousVersionDrawingResult>
            SaveSelectedDrawings(
                ISldWorks swApp,
                IList<PreviousVersionDrawingItem> drawingItems)
        {
            List<PreviousVersionDrawingResult> results =
                new List<PreviousVersionDrawingResult>();

            foreach (PreviousVersionDrawingItem item in
                drawingItems)
            {
                PreviousVersionDrawingResult result =
                    new PreviousVersionDrawingResult();

                result.SourcePath = item.SourcePath;
                result.DestinationPath = item.DestinationPath;

                try
                {
                    SaveOneDrawingCopyAsPreviousVersion(
                        swApp,
                        item);

                    result.Succeeded = true;
                    result.Message =
                        "Previous-version drawing copy created.";
                }
                catch (Exception ex)
                {
                    result.Succeeded = false;
                    result.Message = ex.Message;
                }

                results.Add(result);
            }

            return results;
        }

        private static void SaveOneDrawingCopyAsPreviousVersion(
            ISldWorks swApp,
            PreviousVersionDrawingItem item)
        {
            IModelDoc2 drawing =
                GetOpenDrawing(
                    swApp,
                    item.SourcePath);

            bool closeAfterExport = false;

            if (drawing == null)
            {
                drawing = OpenDrawingSilently(
                    swApp,
                    item.SourcePath);

                closeAfterExport = true;
            }

            if (drawing == null)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS could not open the drawing.\n\n" +
                    item.SourcePath);
            }

            try
            {
                if (drawing.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    throw new InvalidOperationException(
                        "The selected file is not a SOLIDWORKS drawing.\n\n" +
                        item.SourcePath);
                }

                IModelDocExtension extension =
                    drawing.Extension;

                if (extension == null)
                {
                    throw new InvalidOperationException(
                        "Could not access the SOLIDWORKS document extension.");
                }

                AdvancedSaveAsOptions advancedOptions =
                    (AdvancedSaveAsOptions)
                        extension.GetAdvancedSaveAsOptions(
                            (int)swSaveWithReferencesOptions_e
                                .swSaveWithReferencesOptions_None);

                if (advancedOptions == null)
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS could not create the advanced save options required for previous-version saving.");
                }

                advancedOptions.SaveAsPreviousVersion =
                    TargetPreviousVersionCode;

                advancedOptions.SaveAllAsCopy = true;

                int errors = 0;
                int warnings = 0;

                bool saved =
                    extension.SaveAs3(
                        item.DestinationPath,
                        (int)swSaveAsVersion_e
                            .swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e
                            .swSaveAsOptions_Silent,
                        null,
                        advancedOptions,
                        ref errors,
                        ref warnings);

                if (!saved)
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS could not create the previous-version drawing copy.\n\n" +
                        "Error code: " +
                        errors.ToString() +
                        "\nWarning code: " +
                        warnings.ToString());
                }

                if (!File.Exists(item.DestinationPath))
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS reported a successful save, but the expected copied drawing was not found.\n\n" +
                        item.DestinationPath);
                }
            }
            finally
            {
                if (closeAfterExport)
                {
                    CloseDrawingOpenedByCabinTools(
                        swApp,
                        drawing);
                }
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
            int errors = 0;
            int warnings = 0;

            return swApp.OpenDoc6(
                drawingPath,
                (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e
                    .swOpenDocOptions_Silent,
                string.Empty,
                ref errors,
                ref warnings) as IModelDoc2;
        }

        private static void CloseDrawingOpenedByCabinTools(
            ISldWorks swApp,
            IModelDoc2 drawing)
        {
            try
            {
                if (drawing == null)
                {
                    return;
                }

                string title =
                    drawing.GetTitle();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    swApp.CloseDoc(title);
                }
            }
            catch
            {
                // The source drawing is never saved by this tool.
                // A closing failure must not hide the export result.
            }
        }

        private static string WriteExportReport(
            IList<PreviousVersionDrawingResult> results)
        {
            try
            {
                string folder =
                    Path.Combine(
                        System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder
                                .MyDocuments),
                        "CabinTools",
                        "PreviousVersionExports");

                Directory.CreateDirectory(folder);

                string reportPath =
                    Path.Combine(
                        folder,
                        "SaveDrawingsAsSW" +
                        TargetSolidWorksYear.ToString() +
                        "_" +
                        DateTime.Now.ToString(
                            "yyyyMMdd_HHmmss") +
                        ".txt");

                List<string> lines =
                    new List<string>();

                lines.Add(
                    "Cabin Tools - Save Drawings as Previous Version");

                lines.Add(
                    "Created: " +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss"));

                lines.Add(
                    "Target SOLIDWORKS version: " +
                    TargetSolidWorksYear.ToString());

                lines.Add(
                    "Selected drawings: " +
                    results.Count.ToString());

                lines.Add(string.Empty);

                foreach (PreviousVersionDrawingResult result in
                    results)
                {
                    lines.Add(
                        "Succeeded: " +
                        result.Succeeded.ToString());

                    lines.Add(
                        "Source drawing: " +
                        result.SourcePath);

                    lines.Add(
                        "Destination drawing: " +
                        result.DestinationPath);

                    lines.Add(
                        "Result: " +
                        result.Message);

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
            IList<PreviousVersionDrawingResult> results,
            string reportPath)
        {
            int succeededCount = 0;
            int failedCount = 0;

            StringBuilder message =
                new StringBuilder();

            message.AppendLine(
                "SOLIDWORKS " +
                TargetSolidWorksYear.ToString() +
                " drawing copy results:");

            message.AppendLine();

            foreach (PreviousVersionDrawingResult result in
                results)
            {
                if (result.Succeeded)
                {
                    succeededCount++;
                }
                else
                {
                    failedCount++;
                }

                message.AppendLine(
                    result.Succeeded
                        ? "SUCCESS: " +
                          Path.GetFileName(
                              result.DestinationPath)
                        : "FAILED: " +
                          Path.GetFileName(
                              result.SourcePath));

                if (!result.Succeeded)
                {
                    message.AppendLine(
                        result.Message);
                }
            }

            message.AppendLine();
            message.AppendLine(
                "Successful: " +
                succeededCount.ToString());

            message.AppendLine(
                "Failed: " +
                failedCount.ToString());

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                message.AppendLine();
                message.AppendLine(
                    "Report:");
                message.AppendLine(reportPath);
            }

            MessageBox.Show(
                message.ToString(),
                "Cabin Tools",
                MessageBoxButtons.OK,
                failedCount == 0
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public sealed class PreviousVersionDrawingItem
    {
        public string SourcePath { get; set; }
        public string OutputFileName { get; set; }
        public string DestinationPath { get; set; }
    }

    public sealed class PreviousVersionDrawingResult
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public bool Succeeded { get; set; }
        public string Message { get; set; }
    }
}
