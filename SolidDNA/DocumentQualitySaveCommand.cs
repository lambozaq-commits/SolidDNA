using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using static CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// Compact document utilities shown directly on the Cabin Tools CommandManager tab.
    ///
    /// Image quality:
    /// - Low    = 10
    /// - Medium = 50
    /// - High   = 100
    ///
    /// Advanced Save normalizes the active document before saving:
    /// section view off, FeatureManager Design Tree active, Shaded With Edges,
    /// Hide All Types, normal rebuild, force rebuild, collapsed tree,
    /// isometric + zoom fit for parts/assemblies, drawing-sheet zoom fit for drawings,
    /// then a silent SOLIDWORKS save.
    /// </summary>
    internal static class DocumentQualitySaveCommand
    {
        private const int LowQuality = 10;
        private const int MediumQuality = 50;
        private const int HighQuality = 100;

        public static void SetLowImageQuality()
        {
            SetImageQuality(LowQuality);
        }

        public static void SetMediumImageQuality()
        {
            SetImageQuality(MediumQuality);
        }

        public static void SetHighImageQuality()
        {
            SetImageQuality(HighQuality);
        }

        /// <summary>
        /// Main one-click document cleanup/rebuild/save workflow.
        /// </summary>
        public static void AdvancedSave()
        {
            IModelDoc2 model = CabinCustomPropertyStore.GetActiveModelDocument();
            if (model == null)
            {
                ShowWarning("Open a SOLIDWORKS document first.");
                return;
            }

            int documentType = model.GetType();
            bool isPart = documentType == (int)swDocumentTypes_e.swDocPART;
            bool isAssembly = documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
            bool isDrawing = documentType == (int)swDocumentTypes_e.swDocDRAWING;

            if (!isPart && !isAssembly && !isDrawing)
            {
                ShowWarning("Advanced Save supports parts, assemblies, and drawings.");
                return;
            }

            try
            {
                // 1. Turn model Section View off when one is active.
                //    For drawings this is a harmless best-effort call; drawing section
                //    views themselves are not deleted or modified.
                RemoveActiveSectionView(model);

                // 2. Rebuild exactly as requested: Ctrl+B equivalent, then Ctrl+Q equivalent.
                model.EditRebuild3();
                model.ForceRebuild3(false);

                // 3. Normalize the FeatureManager and visibility state.
                ShowFeatureManagerDesignTree(model);
                HideAllTypes(model);

                // 4. Normalize display style. Parts/assemblies use the active model view.
                //    Drawings apply Shaded With Edges to every model view on every sheet.
                if (isPart || isAssembly)
                    SetModelShadedWithEdges(model);
                else
                    SetAllDrawingViewsShadedWithEdges(model);

                // 5. Final orientation and fit. Isometric is meaningful for model documents;
                //    drawings keep their designed view orientations and every sheet is fit instead.
                if (isPart || isAssembly)
                {
                    model.ShowNamedView2("*Isometric", 7);
                    model.ViewZoomtofit2();
                }
                else
                {
                    ZoomAllDrawingSheetsAndReturnToFirst(model);
                }

                // 6. Leave the left pane on FeatureManager, keep the document root
                //    open, and collapse every expandable item beneath it.
                //    Use the FeatureManager tree API instead of SendKeys so the result
                //    does not depend on which SOLIDWORKS control currently has focus.
                ShowFeatureManagerDesignTree(model);
                CollapseFeatureManagerTree(model);

                // 7. Re-fit the final part/assembly after tree collapse.
                if (isPart || isAssembly)
                    model.ViewZoomtofit2();

                model.GraphicsRedraw2();

                // 8. Save only after every cleanup operation is complete.
                int errors = 0;
                int warnings = 0;
                bool saved = model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref errors,
                    ref warnings);

                if (!saved || errors != 0)
                {
                    ShowWarning(
                        "Advanced Save completed the cleanup, but SOLIDWORKS did not save the document.\r\n\r\n" +
                        "Save error code: " + errors +
                        (warnings != 0 ? "\r\nWarning code: " + warnings : string.Empty));
                }
            }
            catch (Exception ex)
            {
                ShowWarning("Advanced Save stopped.\r\n\r\n" + ex.Message);
            }
        }

        // Backward-compatible alias so an older toolbar registration does not fail
        // if SOLIDWORKS temporarily keeps an old command binding in the registry.
        public static void ModifiedRebuildAndSave()
        {
            AdvancedSave();
        }

        private static void SetImageQuality(int quality)
        {
            IModelDoc2 model = CabinCustomPropertyStore.GetActiveModelDocument();
            if (model == null)
            {
                ShowWarning("Open a part or assembly first.");
                return;
            }

            int documentType = model.GetType();
            if (documentType != (int)swDocumentTypes_e.swDocPART &&
                documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                ShowWarning("Image Quality is available for parts and assemblies.");
                return;
            }

            try
            {
                bool restoreApplyToReferenced = false;
                bool previousApplyToReferenced = false;
                IModelDocExtension extension = model.Extension;

                if (documentType == (int)swDocumentTypes_e.swDocASSEMBLY && extension != null)
                {
                    try
                    {
                        previousApplyToReferenced = extension.GetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swImageQualityApplyToAllReferencedPartDoc,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);

                        extension.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swImageQualityApplyToAllReferencedPartDoc,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                            true);

                        restoreApplyToReferenced = true;
                    }
                    catch
                    {
                        restoreApplyToReferenced = false;
                    }
                }

                model.SetTessellationQuality(quality);
                model.GraphicsRedraw2();

                if (restoreApplyToReferenced && extension != null)
                {
                    try
                    {
                        extension.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swImageQualityApplyToAllReferencedPartDoc,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                            previousApplyToReferenced);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                ShowWarning("Could not set image quality.\r\n\r\n" + ex.Message);
            }
        }

        private static void RemoveActiveSectionView(IModelDoc2 model)
        {
            if (model == null)
                return;

            try
            {
                IModelViewManager viewManager = model.ModelViewManager;
                if (viewManager != null)
                    viewManager.RemoveSectionView();
            }
            catch
            {
                // No active model section view, or this document type does not expose one.
            }
        }

        private static void ShowFeatureManagerDesignTree(IModelDoc2 model)
        {
            if (model == null)
                return;

            try
            {
                IFeatureManager featureManager = model.FeatureManager;
                if (featureManager != null)
                {
                    featureManager.EnableFeatureTree = true;
                    featureManager.EnableFeatureTreeWindow = true;
                }

                IModelViewManager viewManager = model.ModelViewManager;
                if (viewManager == null)
                    return;

                int tabIndex = viewManager.GetFeatureManagerTreeTabIndex();
                viewManager.ActiveFeatureManagerTabIndex = tabIndex;
            }
            catch
            {
            }
        }

        private static void HideAllTypes(IModelDoc2 model)
        {
            if (model == null)
                return;

            try
            {
                IModelDocExtension extension = model.Extension;
                if (extension != null)
                {
                    extension.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swViewDisplayHideAllTypes,
                        (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                        true);
                }
            }
            catch
            {
            }
        }

        private static void SetModelShadedWithEdges(IModelDoc2 model)
        {
            if (model == null)
                return;

            try
            {
                IModelView modelView = model.ActiveView as IModelView;
                if (modelView == null)
                    return;

                modelView.DisplayMode =
                    (int)swViewDisplayMode_e.swViewDisplayMode_ShadedWithEdges;

                model.GraphicsRedraw2();
            }
            catch
            {
            }
        }

        private static void SetAllDrawingViewsShadedWithEdges(IModelDoc2 model)
        {
            IDrawingDoc drawing = model as IDrawingDoc;
            if (drawing == null)
                return;

            List<string> sheetNames = GetSheetNames(drawing);
            if (sheetNames.Count == 0)
                return;

            string firstSheet = sheetNames[0];

            foreach (string sheetName in sheetNames)
            {
                try
                {
                    if (!drawing.ActivateSheet(sheetName))
                        continue;

                    IView sheetView = drawing.GetFirstView() as IView;
                    IView view = sheetView == null ? null : sheetView.GetNextView() as IView;

                    while (view != null)
                    {
                        try
                        {
                            // SOLIDWORKS documents Shaded + Edges=true as the
                            // reliable way to request Shaded With Edges for drawing views.
                            view.SetDisplayMode3(
                                false,
                                (int)swDisplayMode_e.swSHADED,
                                false,
                                true);
                        }
                        catch
                        {
                        }

                        view = view.GetNextView() as IView;
                    }
                }
                catch
                {
                }
            }

            try
            {
                drawing.ActivateSheet(firstSheet);
            }
            catch
            {
            }
        }

        private static void CollapseFeatureManagerTree(IModelDoc2 model)
        {
            if (model == null)
                return;

            try
            {
                ShowFeatureManagerDesignTree(model);
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(50);

                IFeatureManager featureManager = model.FeatureManager;
                if (featureManager == null)
                    return;

                // Run SOLIDWORKS' native "Collapse All Items" command first.
                // This is command 2555 (Shift+C) in the SOLIDWORKS 2026
                // swCommands type library. It collapses folders/components that
                // are not always represented by a currently expanded API node.
                bool nativeCollapseRan = false;
                try
                {
                    ISldWorks application =
                        IApplication.UnsafeObject;

                    const int collapseAllItemsCommand = 2555;

                    if (application != null &&
                        application.IsCommandEnabled(
                            collapseAllItemsCommand))
                    {
                        nativeCollapseRan =
                            application.RunCommand(
                                collapseAllItemsCommand,
                                string.Empty);
                    }
                }
                catch
                {
                    nativeCollapseRan = false;
                }

                // Keep the direct tree walk as a fallback and as coverage for a
                // split FeatureManager pane. The native command also collapses
                // the document root, so the direct pass reopens that one node
                // after collapsing every descendant.
                CollapseFeatureManagerPane(
                    featureManager,
                    (int)swFeatMgrPane_e.swFeatMgrPaneBottom);

                CollapseFeatureManagerPane(
                    featureManager,
                    (int)swFeatMgrPane_e.swFeatMgrPaneTop);

                System.Windows.Forms.Application.DoEvents();

                if (!nativeCollapseRan)
                {
                    // Give SOLIDWORKS one UI cycle after the fallback recursion;
                    // large assembly trees update asynchronously.
                    Thread.Sleep(75);
                    System.Windows.Forms.Application.DoEvents();
                }
            }
            catch
            {
                // Tree collapse is visual cleanup only and must not block Save.
            }
        }

        private static void CollapseFeatureManagerPane(
            IFeatureManager featureManager,
            int pane)
        {
            if (featureManager == null)
                return;

            ITreeControlItem rootNode =
                featureManager.GetFeatureTreeRootItem2(pane) as ITreeControlItem;

            if (rootNode == null)
                return;

            ITreeControlItem child = rootNode.GetFirstChild() as ITreeControlItem;
            while (child != null)
            {
                ITreeControlItem next = child.GetNext() as ITreeControlItem;
                CollapseFeatureManagerNode(child);
                child = next;
            }

            // Leave the document node open so History, Sensors, Annotations,
            // planes, Origin, components, mates, and top-level features remain
            // visible. Every expandable item below them stays collapsed.
            rootNode.Expanded = true;
        }

        private static void CollapseFeatureManagerNode(ITreeControlItem node)
        {
            if (node == null)
                return;

            ITreeControlItem child = node.GetFirstChild() as ITreeControlItem;

            while (child != null)
            {
                // Capture the next sibling before collapsing this branch because
                // changing Expanded can refresh the visible tree.
                ITreeControlItem next = child.GetNext() as ITreeControlItem;

                CollapseFeatureManagerNode(child);
                child = next;
            }

            // Collapse children first, then their parent. The document root is
            // deliberately handled by CollapseFeatureManagerPane and reopened.
            node.Expanded = false;
        }

        private static void ZoomAllDrawingSheetsAndReturnToFirst(IModelDoc2 model)
        {
            IDrawingDoc drawing = model as IDrawingDoc;
            if (drawing == null)
                return;

            List<string> sheetNames = GetSheetNames(drawing);
            if (sheetNames.Count == 0)
                return;

            string firstSheet = sheetNames[0];

            foreach (string sheetName in sheetNames)
            {
                try
                {
                    if (!drawing.ActivateSheet(sheetName))
                        continue;

                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(60);
                    model.ViewZoomtofit2();
                }
                catch
                {
                }
            }

            try
            {
                if (drawing.ActivateSheet(firstSheet))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(60);
                    model.ViewZoomtofit2();
                }
            }
            catch
            {
            }
        }

        private static List<string> GetSheetNames(IDrawingDoc drawing)
        {
            List<string> names = new List<string>();
            if (drawing == null)
                return names;

            try
            {
                object raw = drawing.GetSheetNames();
                Array array = raw as Array;
                if (array == null)
                    return names;

                foreach (object item in array)
                {
                    string name = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }
            catch
            {
            }

            return names;
        }

        private static void ShowWarning(string message)
        {
            MessageBox.Show(
                message,
                "Cabin Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
