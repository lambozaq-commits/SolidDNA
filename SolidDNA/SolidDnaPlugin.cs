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
        // Change this ID from the previous version so SOLIDWORKS
        // refreshes the CommandManager definition after reinstall.
        private const int CommandTabId = 180002;

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
                    },

                    new CommandManagerItem
                    {
                        Name = "Property Checker",
                        Tooltip =
                            "Check missing values, title consistency, " +
                            "and custom-property order.",
                        Hint =
                            "Check or repair drawing general custom properties.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = false,
                        VisibleForParts = false,

                        OnClick =
                            PropertyOrganizerCommand
                                .ShowPropertyChecker,

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
                            "Synchronize Title2 and Title3, then export " +
                            "all drawing sheets to one PDF.",
                        VisibleForDrawings = true,
                        VisibleForAssemblies = false,
                        VisibleForParts = false,

                        OnClick =
                            PdfExportCommand.ExportActiveDrawingToPdf,

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
