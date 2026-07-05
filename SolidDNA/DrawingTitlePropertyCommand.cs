using System;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;

namespace SolidDNA
{
    /// <summary>
    /// Synchronizes drawing-only derived title properties from the cabin
    /// naming properties. This command never saves the drawing automatically.
    /// </summary>
    internal static class DrawingTitlePropertyCommand
    {
        public static void SynchronizeActiveDrawingTitles()
        {
            try
            {
                IModelDoc2 activeDocument =
                    CabinCustomPropertyStore
                        .GetActiveModelDocument();

                if (!CabinCustomPropertyStore.IsDrawing(
                        activeDocument))
                {
                    ShowStop(
                        "Title synchronization works only when a drawing is active.");

                    return;
                }

                CabinCustomPropertyStore.EnsureCanWrite(
                    activeDocument);

                bool changed =
                    CabinPropertyService
                        .SynchronizeDerivedTitleProperties(
                            activeDocument);

                if (changed)
                {
                    activeDocument.ForceRebuild3(false);
                }

                CabinToolsTaskpaneHost
                    .RefreshActiveDocument();

                ShowInformation(
                    changed
                        ? "Title2 and Title3 were synchronized from the drawing naming properties.\n\n" +
                          "The drawing was rebuilt but not saved automatically."
                        : "Title2 and Title3 already match the drawing naming properties.\n\n" +
                          "No change was required.");
            }
            catch (Exception ex)
            {
                ShowStop(
                    "Title synchronization failed.\n\n" +
                    ex.Message);
            }
        }

        private static void ShowInformation(string message)
        {
            CADBooster.SolidDna
                .SolidWorksEnvironment
                .Application
                .ShowMessageBox(
                    message,
                    SolidWorksMessageBoxIcon.Information);
        }

        private static void ShowStop(string message)
        {
            CADBooster.SolidDna
                .SolidWorksEnvironment
                .Application
                .ShowMessageBox(
                    message,
                    SolidWorksMessageBoxIcon.Stop);
        }
    }
}
