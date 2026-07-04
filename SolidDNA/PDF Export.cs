using System;
using System.IO;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment =
    CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    public static class PdfExportCommand
    {
        public static void ExportActiveDrawingToPdf()
        {
            try
            {
                IModelDoc2 drawingDoc = GetActiveDrawing();

                if (drawingDoc == null)
                    return;

                CabinNamingValues values =
                    CabinPropertyService.ReadNamingValues(
                        drawingDoc);

                if (CabinPropertyRules
                    .GetMissingPdfNamingProperties(values)
                    .Count > 0)
                {
                    using (PdfPropertyForm propertyForm =
                        new PdfPropertyForm(
                            values.DrwNumber,
                            values.Revision,
                            values.CabinTypeDescription,
                            values.CabinTypeDefined,
                            values.LayoutType))
                    {
                        if (propertyForm.ShowDialog() !=
                            DialogResult.OK)
                        {
                            return;
                        }

                        values.DrwNumber =
                            propertyForm.DrwNumber;

                        values.Revision =
                            propertyForm.Revision;

                        values.CabinTypeDescription =
                            propertyForm.CabinTypeDescription;

                        values.CabinTypeDefined =
                            propertyForm.CabinTypeDefined;

                        values.LayoutType =
                            propertyForm.LayoutType;
                    }

                    if (CabinPropertyRules
                        .GetMissingPdfNamingProperties(values)
                        .Count > 0)
                    {
                        ShowError(
                            "All PDF filename values are required " +
                            "before export.");

                        return;
                    }

                    CabinPropertyService.WriteNamingValues(
                        drawingDoc,
                        values);
                }

                bool titlePropertiesChanged =
                    CabinPropertyService
                        .SynchronizeDerivedTitleProperties(
                            drawingDoc);

                if (titlePropertiesChanged)
                    drawingDoc.ForceRebuild3(false);

                string suggestedFileName =
                    CabinPropertyRules.BuildPdfFileName(
                        values);

                using (SaveFileDialog saveDialog =
                    new SaveFileDialog())
                {
                    saveDialog.Title =
                        "Export Drawing to PDF";

                    saveDialog.Filter =
                        "PDF files (*.pdf)|*.pdf";

                    saveDialog.DefaultExt = "pdf";
                    saveDialog.AddExtension = true;
                    saveDialog.OverwritePrompt = true;
                    saveDialog.FileName =
                        suggestedFileName;

                    saveDialog.InitialDirectory =
                        GetInitialDirectory(drawingDoc);

                    if (saveDialog.ShowDialog() !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    string pdfPath = saveDialog.FileName;

                    if (!pdfPath.EndsWith(
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        pdfPath += ".pdf";
                    }

                    ExportDrawingToPdf(
                        drawingDoc,
                        pdfPath);

                    SwEnvironment.Application.ShowMessageBox(
                        "PDF exported successfully.\n\n" +
                        pdfPath,
                        SolidWorksMessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError(
                    "PDF export failed.\n\n" +
                    ex.Message);
            }
        }

        private static IModelDoc2 GetActiveDrawing()
        {
            ISldWorks swApp =
                SwEnvironment.Application.UnsafeObject;

            if (swApp == null)
            {
                ShowError(
                    "SOLIDWORKS connection is not available.");

                return null;
            }

            IModelDoc2 activeDoc =
                swApp.ActiveDoc as IModelDoc2;

            if (activeDoc == null)
            {
                ShowError("No active document is open.");
                return null;
            }

            if (activeDoc.GetType() !=
                (int)swDocumentTypes_e.swDocDRAWING)
            {
                ShowError(
                    "Export PDF works only when a drawing is active.");

                return null;
            }

            return activeDoc;
        }

        private static void ExportDrawingToPdf(
            IModelDoc2 drawingDoc,
            string pdfPath)
        {
            IModelDocExtension extension =
                drawingDoc.Extension;

            if (extension == null)
            {
                throw new InvalidOperationException(
                    "Could not access the drawing document extension.");
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
                    "Error code: " + errors.ToString() + "\n" +
                    "Warning code: " + warnings.ToString());
            }
        }

        private static string GetInitialDirectory(
            IModelDoc2 drawingDoc)
        {
            string drawingPath =
                drawingDoc.GetPathName();

            if (!string.IsNullOrWhiteSpace(drawingPath))
            {
                string directory =
                    Path.GetDirectoryName(drawingPath);

                if (!string.IsNullOrWhiteSpace(directory) &&
                    Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.MyDocuments);
        }

        private static void ShowError(string message)
        {
            SwEnvironment.Application.ShowMessageBox(
                message,
                SolidWorksMessageBoxIcon.Stop);
        }
    }
}