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
    /// Replaces the current external sheet format (.slddrt) on selected
    /// drawing sheets. The drawing is rebuilt but never saved automatically.
    /// </summary>
    public static class SheetFormatCommand
    {
        public static void ShowSheetFormatForm()
        {
            try
            {
                IModelDoc2 drawingModel;
                IDrawingDoc drawing =
                    GetActiveDrawing(out drawingModel);

                if (drawing == null ||
                    drawingModel == null)
                {
                    return;
                }

                List<SheetFormatAssignment> assignments =
                    ReadSheetAssignments(drawing);

                if (assignments.Count == 0)
                {
                    ShowError(
                        "No drawing sheets were found.");

                    return;
                }

                using (SheetFormatForm form =
                    new SheetFormatForm(assignments))
                {
                    if (form.ShowDialog() !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    WriteBackupReport(
                        drawingModel,
                        form.Assignments,
                        form.ApplyOneFormatToAllSheets);

                    ApplySheetFormats(
                        drawingModel,
                        drawing,
                        form);

                    ShowInformation(
                        "The selected sheet format replaced the previous format on the selected sheet(s).\n\n" +
                        "The drawing was rebuilt but not saved automatically. Review the title block, borders, zones, notes, sheet size, and drawing views before saving.");
                }
            }
            catch (Exception ex)
            {
                ShowError(
                    "Apply Sheet Format failed.\n\n" +
                    ex.Message);
            }
        }

        private static void ApplySheetFormats(
            IModelDoc2 drawingModel,
            IDrawingDoc drawing,
            SheetFormatForm form)
        {
            string activeSheetName =
                GetCurrentSheetName(drawing);

            foreach (SheetFormatAssignment assignment in
                form.Assignments)
            {
                string templatePath =
                    form.ApplyOneFormatToAllSheets
                        ? form.AllSheetsTemplatePath
                        : assignment.NewTemplatePath;

                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    throw new InvalidOperationException(
                        "No sheet format was selected for sheet: " +
                        assignment.SheetName);
                }

                if (!File.Exists(templatePath))
                {
                    throw new FileNotFoundException(
                        "Sheet format file was not found.",
                        templatePath);
                }

                bool activated =
                    drawing.ActivateSheet(
                        assignment.SheetName);

                if (!activated)
                {
                    throw new InvalidOperationException(
                        "Could not activate drawing sheet: " +
                        assignment.SheetName);
                }

                ISheet sheet =
                    drawing.GetCurrentSheet() as ISheet;

                if (sheet == null)
                {
                    throw new InvalidOperationException(
                        "Could not access drawing sheet: " +
                        assignment.SheetName);
                }

                ReplaceSheetFormat(
                    sheet,
                    templatePath);

                assignment.NewTemplatePath =
                    templatePath;
            }

            drawingModel.ForceRebuild3(false);

            if (!string.IsNullOrWhiteSpace(activeSheetName))
            {
                drawing.ActivateSheet(activeSheetName);
            }
        }

        private static void ReplaceSheetFormat(
            ISheet sheet,
            string sheetFormatPath)
        {
            double[] properties =
                ToDoubleArray(
                    sheet.GetProperties2());

            if (properties == null ||
                properties.Length < 8)
            {
                throw new InvalidOperationException(
                    "Could not read the existing properties for sheet: " +
                    sheet.GetName());
            }

            // Set the sheet to use a custom format, while retaining the current
            // paper size, scale, projection, and sheet-level custom-property setting.
            sheet.SetProperties2(
                Convert.ToInt32(properties[0]),
                (int)swDwgTemplates_e.swDwgTemplateCustom,
                properties[2],
                properties[3],
                Convert.ToBoolean(properties[4]),
                properties[5],
                properties[6],
                Convert.ToBoolean(properties[7]));

            // Assign the selected external .slddrt file.
            sheet.SetTemplateName(
                sheetFormatPath);

            // False intentionally replaces the previous sheet-format content
            // instead of retaining prior sheet-format note modifications.
            sheet.ReloadTemplate(false);
        }

        private static List<SheetFormatAssignment>
            ReadSheetAssignments(
                IDrawingDoc drawing)
        {
            List<SheetFormatAssignment> assignments =
                new List<SheetFormatAssignment>();

            string activeSheetName =
                GetCurrentSheetName(drawing);

            List<string> sheetNames =
                ToStringList(
                    drawing.GetSheetNames());

            foreach (string sheetName in sheetNames)
            {
                bool activated =
                    drawing.ActivateSheet(sheetName);

                if (!activated)
                {
                    continue;
                }

                ISheet sheet =
                    drawing.GetCurrentSheet() as ISheet;

                if (sheet == null)
                {
                    continue;
                }

                string currentTemplatePath =
                    sheet.GetTemplateName();

                if (string.IsNullOrWhiteSpace(
                    currentTemplatePath))
                {
                    currentTemplatePath =
                        "(Embedded or no external .slddrt path)";
                }

                assignments.Add(
                    new SheetFormatAssignment
                    {
                        SheetName = sheetName,
                        CurrentTemplatePath =
                            currentTemplatePath,
                        NewTemplatePath =
                            string.Empty
                    });
            }

            if (!string.IsNullOrWhiteSpace(activeSheetName))
            {
                drawing.ActivateSheet(activeSheetName);
            }

            return assignments;
        }

        private static IDrawingDoc GetActiveDrawing(
            out IModelDoc2 drawingModel)
        {
            drawingModel = null;

            ISldWorks swApp =
                SwEnvironment.Application
                    .UnsafeObject as ISldWorks;

            if (swApp == null)
            {
                ShowError(
                    "SOLIDWORKS connection is not available.");

                return null;
            }

            IModelDoc2 activeDocument =
                swApp.ActiveDoc as IModelDoc2;

            if (activeDocument == null)
            {
                ShowError(
                    "No active document is open.");

                return null;
            }

            if (activeDocument.GetType() !=
                (int)swDocumentTypes_e.swDocDRAWING)
            {
                ShowError(
                    "Apply Sheet Format works only when a drawing is active.");

                return null;
            }

            IDrawingDoc drawing =
                activeDocument as IDrawingDoc;

            if (drawing == null)
            {
                ShowError(
                    "SOLIDWORKS could not access the active drawing interface.");

                return null;
            }

            drawingModel = activeDocument;

            return drawing;
        }

        private static string GetCurrentSheetName(
            IDrawingDoc drawing)
        {
            try
            {
                ISheet sheet =
                    drawing.GetCurrentSheet() as ISheet;

                return sheet == null
                    ? string.Empty
                    : sheet.GetName();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void WriteBackupReport(
            IModelDoc2 drawing,
            IList<SheetFormatAssignment> assignments,
            bool oneFormatForAllSheets)
        {
            try
            {
                string folder =
                    Path.Combine(
                        System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder
                                .MyDocuments),
                        "CabinTools",
                        "SheetFormatBackups");

                Directory.CreateDirectory(folder);

                string drawingName =
                    Path.GetFileNameWithoutExtension(
                        drawing.GetPathName());

                if (string.IsNullOrWhiteSpace(drawingName))
                {
                    drawingName = "UnsavedDrawing";
                }

                string reportPath =
                    Path.Combine(
                        folder,
                        drawingName +
                        "_SheetFormatBackup_" +
                        DateTime.Now.ToString(
                            "yyyyMMdd_HHmmss") +
                        ".txt");

                List<string> lines =
                    new List<string>();

                lines.Add(
                    "Cabin Tools - Sheet Format Backup Report");

                lines.Add(
                    "Drawing: " +
                    drawing.GetPathName());

                lines.Add(
                    "Created: " +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss"));

                lines.Add(
                    "Apply one format to all sheets: " +
                    oneFormatForAllSheets);

                lines.Add(string.Empty);

                foreach (SheetFormatAssignment assignment in
                    assignments)
                {
                    lines.Add(
                        "Sheet: " +
                        assignment.SheetName);

                    lines.Add(
                        "Previous format: " +
                        assignment.CurrentTemplatePath);

                    lines.Add(
                        "New format: " +
                        assignment.NewTemplatePath);

                    lines.Add(string.Empty);
                }

                File.WriteAllLines(
                    reportPath,
                    lines.ToArray(),
                    Encoding.UTF8);
            }
            catch
            {
                // Backup-report writing must never block a sheet-format update.
            }
        }

        private static double[] ToDoubleArray(
            object values)
        {
            Array array =
                values as Array;

            if (array == null)
            {
                return null;
            }

            double[] results =
                new double[array.Length];

            for (int index = 0;
                 index < array.Length;
                 index++)
            {
                results[index] =
                    Convert.ToDouble(
                        array.GetValue(index));
            }

            return results;
        }

        private static List<string> ToStringList(
            object values)
        {
            List<string> results =
                new List<string>();

            Array array =
                values as Array;

            if (array == null)
            {
                return results;
            }

            foreach (object value in array)
            {
                string text =
                    value as string;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(text);
                }
            }

            return results;
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static void ShowInformation(string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    public sealed class SheetFormatAssignment
    {
        public string SheetName { get; set; }
        public string CurrentTemplatePath { get; set; }
        public string NewTemplatePath { get; set; }
    }
}
