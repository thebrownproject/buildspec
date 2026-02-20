using Autodesk.Revit.UI;
using System.Windows.Controls;

namespace BuildScope.Views
{
    public partial class MainPanel : Page, IDockablePaneProvider
    {
        private readonly ProjectManager _projectManager = new();
        private ChatPanel? _chatPanel;

        public MainPanel()
        {
            InitializeComponent();
            ShowInitialView();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }

        public void NavigateToChat()
        {
            _chatPanel ??= new ChatPanel(this, _projectManager);
            _chatPanel.RefreshProjects();
            ViewHost.Content = _chatPanel;
        }

        public void NavigateToProjectForm()
        {
            ViewHost.Content = new ProjectForm(this, _projectManager);
        }

        public void NavigateToSettings()
        {
            ViewHost.Content = new SettingsPanel(this);
        }

        private void ShowInitialView()
        {
            var projects = _projectManager.ListProjects();
            if (projects.Count > 0)
            {
                _projectManager.SetCurrentProject(projects[0]);
                NavigateToChat();
            }
            else
            {
                NavigateToChat();
            }
        }
    }
}
