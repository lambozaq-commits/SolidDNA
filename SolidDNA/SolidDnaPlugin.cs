using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CADBooster.SolidDna;
using SolidWorks.Interop.swconst;

using static CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// Main Cabin Tools plug-in. It owns the CommandManager tab, flyout menus,
    /// and the permanent Cabin Tools taskpane.
    /// </summary>
    [Guid("A6B1C5FD-39B5-4D53-B747-25C3F3B5F1AA")]
    [ComVisible(true)]
    public class SolidDnaPlugin : SolidPlugIn
    {
        // Keep this ID stable. SolidDNA clears the previous command-group
        // customization by default when it recreates this tab.
        private const int CommandTabId = 180001;

        private static SolidDnaPlugin currentInstance;

        private TaskpaneIntegration<
            CabinToolsTaskpaneHost,
            SolidDnaAddIn> cabinToolsTaskpane;

        public override string AddInTitle
        {
            get
            {
                return "Cabin Tools SolidDNA";
            }
        }

        public override string AddInDescription
        {
            get
            {
                return
                    "Cabin drawing, cut-list, custom-property, and document utilities.";
            }
        }

        public override void ConnectedToSolidWorks()
        {
            currentInstance = this;
            CreateCommandTab();
            CreateTaskpane();
        }

        public override void DisconnectedFromSolidWorks()
        {
            if (object.ReferenceEquals(currentInstance, this))
            {
                currentInstance = null;
            }

            // TaskpaneIntegration automatically removes the taskpane when the
            // parent SolidAddIn disconnects. Do not dispose it here a second
            // time.
        }

        internal static void EnsureCabinToolsTaskpane()
        {
            SolidDnaPlugin instance = currentInstance;

            if (instance == null)
            {
                WriteStartupWarning(
                    "Cabin Tools taskpane cannot be created because the plug-in is not connected.");
                return;
            }

            if (instance.cabinToolsTaskpane != null &&
                CabinToolsTaskpaneHost.Instance == null)
            {
                try
                {
                    instance.cabinToolsTaskpane.RemoveFromTaskpane();
                }
                catch
                {
                    // A stale taskpane view can already be gone in SOLIDWORKS.
                }

                instance.cabinToolsTaskpane = null;
            }

            instance.CreateTaskpane();
            CabinToolsTaskpaneHost.RefreshActiveDocument();
        }

        private void CreateCommandTab()
        {
            CADBooster.SolidDna.CommandManager commandManager =
                SolidDnaAddIn.Instance.CommandManager;

            string resourcesDirectory =
                Path.Combine(
                    this.AssemblyDirectoryPath(),
                    "Resources");

            string commandIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "CommandIcons{0}.png");

            string mainIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "CabinToolsMain{0}.png");

            string propertiesFlyoutIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "PropertiesFlyout{0}.png");

            string exportFlyoutIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "ExportFlyout{0}.png");

            string drawingFlyoutIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "DrawingFlyout{0}.png");

            string utilitiesFlyoutIconPathFormat =
                Path.Combine(
                    resourcesDirectory,
                    "UtilitiesFlyout{0}.png");

            if (!HasCompleteIconSet(
                    commandIconPathFormat,
                    mainIconPathFormat,
                    propertiesFlyoutIconPathFormat,
                    exportFlyoutIconPathFormat,
                    drawingFlyoutIconPathFormat,
                    utilitiesFlyoutIconPathFormat))
            {
                // Keep the add-in usable if a deployment misses resource files.
                // Flyouts require icon-list resources, so fall back to a simple
                // direct-button tab instead of failing during add-in startup.
                commandManager.CreateCommandTab(
                    title: "Cabin Tools",
                    id: CommandTabId,
                    commandManagerItems:
                        CreateFallbackCommandItems());

                return;
            }

            CommandManagerFlyout propertiesFlyout =
                commandManager.CreateFlyoutGroup2(
                    title: "Properties",
                    items: CreatePropertyCommands(),
                    mainIconPathFormat:
                        propertiesFlyoutIconPathFormat,
                    iconListsPathFormat:
                        commandIconPathFormat,
                    tooltip: "Custom property tools",
                    hint:
                        "Check and reorder general custom properties.",
                    tabView:
                        CommandManagerItemTabView
                            .IconWithTextBelow,
                    type:
                        CommandManagerFlyoutType
                            .ExpandOnly);

            CommandManagerFlyout exportFlyout =
                commandManager.CreateFlyoutGroup2(
                    title: "Export",
                    items: CreateExportCommands(),
                    mainIconPathFormat:
                        exportFlyoutIconPathFormat,
                    iconListsPathFormat:
                        commandIconPathFormat,
                    tooltip: "Export tools",
                    hint:
                        "Export PDFs and create previous-version drawing copies.",
                    tabView:
                        CommandManagerItemTabView
                            .IconWithTextBelow,
                    type:
                        CommandManagerFlyoutType
                            .ExpandOnly);

            CommandManagerFlyout drawingFlyout =
                commandManager.CreateFlyoutGroup2(
                    title: "Drawing",
                    items: CreateDrawingCommands(),
                    mainIconPathFormat:
                        drawingFlyoutIconPathFormat,
                    iconListsPathFormat:
                        commandIconPathFormat,
                    tooltip: "Drawing tools",
                    hint:
                        "Apply approved external sheet formats to drawings.",
                    tabView:
                        CommandManagerItemTabView
                            .IconWithTextBelow,
                    type:
                        CommandManagerFlyoutType
                            .ExpandOnly);

            CommandManagerFlyout utilitiesFlyout =
                commandManager.CreateFlyoutGroup2(
                    title: "Utilities",
                    items: CreateUtilityCommands(),
                    mainIconPathFormat:
                        utilitiesFlyoutIconPathFormat,
                    iconListsPathFormat:
                        commandIconPathFormat,
                    tooltip: "Cabin Tools utilities",
                    hint:
                        "Refresh the Cabin Tools taskpane and verify the add-in connection.",
                    tabView:
                        CommandManagerItemTabView
                            .IconWithTextBelow,
                    type:
                        CommandManagerFlyoutType
                            .ExpandOnly);

            commandManager.CreateCommandTab(
                title: "Cabin Tools",
                id: CommandTabId,
                commandManagerItems:
                    new List<ICommandManagerItem>
                    {
                        propertiesFlyout,
                        new CommandManagerSeparator(),
                        exportFlyout,
                        new CommandManagerSeparator(),
                        drawingFlyout,
                        new CommandManagerSeparator(),
                        utilitiesFlyout
                    },
                mainIconPathFormat:
                    mainIconPathFormat,
                iconListsPathFormat:
                    commandIconPathFormat);
        }

        private void CreateTaskpane()
        {
            try
            {
                if (cabinToolsTaskpane != null)
                {
                    CabinToolsTaskpaneHost.RefreshActiveDocument();
                    return;
                }

                string taskpaneIconPath =
                    Path.Combine(
                        this.AssemblyDirectoryPath(),
                        "Resources",
                        "TaskpaneIcon.bmp");

                if (!File.Exists(taskpaneIconPath))
                {
                    WriteStartupWarning(
                        "Cabin Tools taskpane icon was not found.\n\n" +
                        taskpaneIconPath);
                    return;
                }

                cabinToolsTaskpane =
                    new TaskpaneIntegration<
                        CabinToolsTaskpaneHost,
                        SolidDnaAddIn>
                    {
                        Icon = taskpaneIconPath
                    };

                cabinToolsTaskpane.AddToTaskpaneAsync();
            }
            catch (Exception ex)
            {
                // CommandManager tools must remain available even if SOLIDWORKS
                // rejects the taskpane host. Avoid a startup exception that could
                // disable the whole add-in.
                cabinToolsTaskpane = null;

                WriteStartupWarning(
                    "Cabin Tools taskpane was not created.\n\n" +
                    ex.Message +
                    "\n\nClose SOLIDWORKS, rebuild, then re-register the add-in so Windows registers the Cabin Tools taskpane COM host.");
            }
        }

        private static bool HasCompleteIconSet(
            params string[] iconPathFormats)
        {
            int[] requiredSizes =
                new int[]
                {
                    20,
                    32,
                    40,
                    64,
                    96,
                    128
                };

            foreach (string pathFormat in
                iconPathFormats)
            {
                foreach (int iconSize in
                    requiredSizes)
                {
                    string iconPath =
                        string.Format(
                            pathFormat,
                            iconSize);

                    if (!File.Exists(iconPath))
                        return false;
                }
            }

            return true;
        }

        private static List<ICommandManagerItem>
            CreateFallbackCommandItems()
        {
            List<ICommandManagerItem> items =
                new List<ICommandManagerItem>();

            AddItems(
                items,
                CreatePropertyCommands());

            items.Add(
                new CommandManagerSeparator());

            AddItems(
                items,
                CreateExportCommands());

            items.Add(
                new CommandManagerSeparator());

            AddItems(
                items,
                CreateDrawingCommands());

            items.Add(
                new CommandManagerSeparator());

            AddItems(
                items,
                CreateUtilityCommands());

            return items;
        }

        private static void AddItems(
            List<ICommandManagerItem> target,
            List<CommandManagerItem> source)
        {
            foreach (CommandManagerItem item in source)
            {
                // The fallback tab deliberately has no image strip.
                // Keep all items on the default image index so the
                // CommandManager does not reject a missing icon index.
                item.ImageIndex = 0;
                target.Add(item);
            }
        }

        private static List<CommandManagerItem>
            CreatePropertyCommands()
        {
            return new List<CommandManagerItem>
            {
                new CommandManagerItem
                {
                    Name = "Property Checker",
                    Tooltip =
                        "Check and reorder general custom properties.",
                    Hint =
                        "Read Properties.txt, check values, and create a backup before reorder and repair.",
                    ImageIndex = 0,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        PropertyOrganizerCommand.ShowOrganizer,
                    OnStateCheck = args =>
                        args.Result =
                            CabinToolsCommandState
                                .ForSupportedDocument()
                },

                new CommandManagerItem
                {
                    Name = "Assign Configuration Part Numbers",
                    Tooltip =
                        "Assign SOLIDWORKS BOM part numbers to configurations in bulk.",
                    Hint =
                        "Fill, review, and apply configuration part numbers. Existing numbers are protected unless overwrite is enabled.",
                    ImageIndex = 1,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        ConfigurationPartNumberCommand
                            .ShowConfigurationPartNumberForm,
                    OnStateCheck = args =>
                        args.Result =
                            CabinToolsCommandState
                                .ForPartOrAssembly()
                },

                new CommandManagerItem
                {
                    Name = "Set Cut-List Profile Properties",
                    Tooltip =
                        "Manually assign Top Profile, Bottom Profile, or custom cut-list properties.",
                    Hint =
                        "Select cut-list rows and write DESCRIPTION, BRAND, MODEL, plus optional custom properties.",
                    ImageIndex = 1,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        CutListProfilePropertyCommand
                            .UpdateActivePartTopBottomProfiles,
                    OnStateCheck = args =>
                        args.Result =
                            CabinToolsCommandState
                                .ForPart()
                }
            };
        }

        private static List<CommandManagerItem>
            CreateExportCommands()
        {
            return new List<CommandManagerItem>
            {
                new CommandManagerItem
                {
                    Name = "Export PDFs - Auto Name",
                    Tooltip =
                        "Export one or more drawings to automatically named PDFs.",
                    Hint =
                        "Select multiple drawings. PDF names are generated from drawing properties; rows with missing values are marked ! and skipped.",
                    ImageIndex = 2,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        PdfExportCommand
                            .ShowAutoNamedBatchExport,
                    OnStateCheck = args =>
                        args.Result =
                            CommandManagerItemState
                                .DeselectedEnabled
                },

                new CommandManagerItem
                {
                    Name = "Export PDFs - Manual Name",
                    Tooltip =
                        "Export one or more drawings to manually named PDFs.",
                    Hint =
                        "Select multiple drawings, edit each PDF filename, and export checked rows or all valid rows. Missing drawing properties are shown but do not block manual naming.",
                    ImageIndex = 2,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        PdfExportCommand
                            .ShowManualNamedBatchExport,
                    OnStateCheck = args =>
                        args.Result =
                            CommandManagerItemState
                                .DeselectedEnabled
                },

                new CommandManagerItem
                {
                    Name = "Save Drawings as SW2025",
                    Tooltip =
                        "Create SOLIDWORKS 2025 drawing copies.",
                    Hint =
                        "Create previous-version drawing copies outside PDM. The source drawing is never overwritten.",
                    ImageIndex = 3,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        PreviousVersionDrawingCommand
                            .ShowSaveDrawingsForm,
                    OnStateCheck = args =>
                        args.Result =
                            CabinToolsCommandState
                                .ForSupportedDocument()
                }
            };
        }

        private static List<CommandManagerItem>
            CreateDrawingCommands()
        {
            return new List<CommandManagerItem>
            {
                new CommandManagerItem
                {
                    Name = "Apply Sheet Format",
                    Tooltip =
                        "Replace drawing sheet formats.",
                    Hint =
                        "Replace the external .slddrt format on all sheets or choose a format per sheet. The drawing is rebuilt but not saved automatically.",
                    ImageIndex = 4,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        SheetFormatCommand
                            .ShowSheetFormatForm,
                    OnStateCheck = args =>
                        args.Result =
                            CabinToolsCommandState
                                .ForDrawing()
                }
            };
        }

        private static List<CommandManagerItem>
            CreateUtilityCommands()
        {
            return new List<CommandManagerItem>
            {
                new CommandManagerItem
                {
                    Name = "Refresh Cabin Tools",
                    Tooltip =
                        "Refresh the Cabin Tools taskpane.",
                    Hint =
                        "Read the active document, configuration, and Description values again.",
                    ImageIndex = 5,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick =
                        CabinToolsTaskpaneHost
                            .RefreshActiveDocument,
                    OnStateCheck = args =>
                        args.Result =
                            CommandManagerItemState
                                .DeselectedEnabled
                },

                new CommandManagerItem
                {
                    Name = "Show / Refresh Cabin Tools Taskpane",
                    Tooltip =
                        "Create or refresh the Cabin Tools taskpane.",
                    Hint =
                        "Retry taskpane creation and refresh the active document information.",
                    ImageIndex = 5,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick = EnsureCabinToolsTaskpane,
                    OnStateCheck = args =>
                        args.Result =
                            CommandManagerItemState
                                .DeselectedEnabled
                },

                new CommandManagerItem
                {
                    Name = "Test Connection",
                    Tooltip =
                        "Confirm that Cabin Tools is connected.",
                    Hint =
                        "Show a Cabin Tools connection message.",
                    ImageIndex = 6,
                    VisibleForDrawings = true,
                    VisibleForAssemblies = true,
                    VisibleForParts = true,
                    OnClick = ShowTestConnection,
                    OnStateCheck = args =>
                        args.Result =
                            CommandManagerItemState
                                .DeselectedEnabled
                }
            };
        }

        private static void ShowTestConnection()
        {
            IApplication.ShowMessageBox(
                "Cabin Tools SolidDNA is connected.\n\n" +
                "CommandManager flyouts, taskpane hosting, " +
                "active-document refresh, and the custom-property " +
                "wrapper are available.",
                SolidWorksMessageBoxIcon.Information);
        }

        private static void WriteStartupWarning(
            string message)
        {
            try
            {
                IApplication.ShowMessageBox(
                    message,
                    SolidWorksMessageBoxIcon.Warning);
            }
            catch
            {
                // SOLIDWORKS can be closing or can have rejected the add-in
                // before the message box service is ready.
            }
        }
    }

    /// <summary>
    /// Centralized CommandManager enabled/disabled rules.
    /// The buttons are visible across document environments so the Cabin Tools
    /// tab stays structurally consistent, but commands are disabled unless the
    /// active document is valid for the operation.
    /// </summary>
    internal static class CabinToolsCommandState
    {
        public static CommandManagerItemState
            ForSupportedDocument()
        {
            return CabinCustomPropertyStore
                .IsSupportedDocument(
                    CabinCustomPropertyStore
                        .GetActiveModelDocument())
                ? CommandManagerItemState
                    .DeselectedEnabled
                : CommandManagerItemState
                    .DeselectedDisabled;
        }

        public static CommandManagerItemState ForPart()
        {
            SolidWorks.Interop.sldworks.IModelDoc2 modelDoc =
                CabinCustomPropertyStore
                    .GetActiveModelDocument();

            return modelDoc != null &&
                   modelDoc.GetType() ==
                       (int)swDocumentTypes_e.swDocPART
                ? CommandManagerItemState
                    .DeselectedEnabled
                : CommandManagerItemState
                    .DeselectedDisabled;
        }

        public static CommandManagerItemState ForPartOrAssembly()
        {
            SolidWorks.Interop.sldworks.IModelDoc2 modelDoc =
                CabinCustomPropertyStore
                    .GetActiveModelDocument();

            if (modelDoc == null)
            {
                return CommandManagerItemState
                    .DeselectedDisabled;
            }

            int documentType = modelDoc.GetType();

            return documentType ==
                       (int)swDocumentTypes_e.swDocPART ||
                   documentType ==
                       (int)swDocumentTypes_e.swDocASSEMBLY
                ? CommandManagerItemState
                    .DeselectedEnabled
                : CommandManagerItemState
                    .DeselectedDisabled;
        }

        public static CommandManagerItemState ForDrawing()
        {
            return CabinCustomPropertyStore.IsDrawing(
                    CabinCustomPropertyStore
                        .GetActiveModelDocument())
                ? CommandManagerItemState
                    .DeselectedEnabled
                : CommandManagerItemState
                    .DeselectedDisabled;
        }
    }
}
