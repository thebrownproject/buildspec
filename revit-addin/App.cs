using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Reflection;
using BuildScope.Views;

namespace BuildScope
{
    [Transaction(TransactionMode.Manual)]
    public class App : IExternalApplication
    {
        private static readonly Guid PaneGuid = new("E8F1A3B5-2D4C-6E9F-B7A0-1C3D5E7F9A2B");

        public Result OnStartup(UIControlledApplication application)
        {
            var paneId = new DockablePaneId(PaneGuid);
            var mainPanel = new MainPanel();
            application.RegisterDockablePane(paneId, "BuildScope", mainPanel);

            string tabName = "BuildScope";
            application.CreateRibbonTab(tabName);

            var panel = application.CreateRibbonPanel(tabName, "Commands");

            var buttonData = new PushButtonData(
                "ShowChatPanel",
                "Chat\nPanel",
                Assembly.GetExecutingAssembly().Location,
                "BuildScope.ShowChatPanelCommand"
            );
            buttonData.ToolTip = "Toggle the BuildScope compliance panel";

            panel.AddItem(buttonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public static DockablePaneId GetPaneId() => new DockablePaneId(PaneGuid);
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowChatPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var paneId = App.GetPaneId();
            var pane = commandData.Application.GetDockablePane(paneId);
            if (pane != null)
            {
                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();
            }
            return Result.Succeeded;
        }
    }
}
