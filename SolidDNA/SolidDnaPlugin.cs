using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using CADBooster.SolidDna;

namespace SolidDNA
{
    [Guid("A6B1C5FD-39B5-4D53-B747-25C3F3B5F1AA")]
    [ComVisible(true)]
    public class SolidDnaPlugin : SolidPlugIn
    {
        private const int CommandTabId = 180001;

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
                return "Cabin drawing and property utilities.";
            }
        }

        public override void ConnectedToSolidWorks()
        {
            CreateCommandTab();
        }

        public override void DisconnectedFromSolidWorks()
        {
        }

        private void CreateCommandTab()
        {
            var commandManager =
                SolidDnaAddIn.Instance.CommandManager;

            List<ICommandManagerItem> commands =
                new List<ICommandManagerItem>
                {
                    new CommandManagerItem
                    {
                        Name = "Property Checker",
                        Tooltip =
                            "Check and reorder general custom properties.",
                        Hint =
                            "Read the property order from Properties.txt, " +
                            "check missing values, then reorder safely.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = true,
                        VisibleForParts = true,

                        OnClick =
                            PropertyOrganizerCommand.ShowOrganizer,

                        OnStateCheck = args =>
                            args.Result =
                                CommandManagerItemState
                                    .DeselectedEnabled
                    },

                    new CommandManagerItem
                    {
                        Name = "Export PDF",
                        Tooltip =
                            "Export the active drawing to PDF.",
                        Hint =
                            "Synchronize drawing title properties and " +
                            "export all sheets to one PDF.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = false,
                        VisibleForParts = false,

                        OnClick =
                            PdfExportCommand.ExportActiveDrawingToPdf,

                        OnStateCheck = args =>
                            args.Result =
                                CommandManagerItemState
                                    .DeselectedEnabled
                    },

                    new CommandManagerItem
                    {
                        Name = "Apply Sheet Format",
                        Tooltip =
                            "Replace drawing sheet formats.",
                        Hint =
                            "Replace the existing .slddrt sheet format on all " +
                            "sheets or select one replacement format per sheet.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = false,
                        VisibleForParts = false,

                        OnClick =
                            SheetFormatCommand.ShowSheetFormatForm,

                        OnStateCheck = args =>
                            args.Result =
                                CommandManagerItemState
                                    .DeselectedEnabled
                    },

                    new CommandManagerItem
                    {
                        Name = "Save Drawings as SW2025",
                        Tooltip =
                            "Create SOLIDWORKS 2025 copies of up to five drawings.",
                        Hint =
                            "Select up to five drawing files, rename each output " +
                            "file, then create previous-version copies outside PDM.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = true,
                        VisibleForParts = true,

                        OnClick =
                            PreviousVersionDrawingCommand
                                .ShowSaveDrawingsForm,

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
                            "Show a Cabin Tools connection test message.",
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

            commandManager.CreateCommandTab(
                title: "Cabin Tools",
                id: CommandTabId,
                commandManagerItems: commands);
        }

        private void ShowTestConnection()
        {
            MessageBox.Show(
                "Cabin Tools SolidDNA command tab is working.",
                "Cabin Tools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
