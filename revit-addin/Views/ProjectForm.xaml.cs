using System.Windows;
using System.Windows.Controls;

namespace BuildScope.Views
{
    public partial class ProjectForm : UserControl
    {
        private readonly MainPanel _mainPanel;
        private readonly ProjectManager _projectManager;

        public ProjectForm(MainPanel mainPanel, ProjectManager projectManager)
        {
            _mainPanel = mainPanel;
            _projectManager = projectManager;
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _mainPanel.NavigateToChat();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var name = NameInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowError("Please enter a project name.");
                return;
            }

            if (ClassDropdown.SelectedItem == null)
            {
                ShowError("Please select a building class.");
                return;
            }

            if (StateDropdown.SelectedItem == null)
            {
                ShowError("Please select a state.");
                return;
            }

            if (TypeDropdown.SelectedItem == null)
            {
                ShowError("Please select a construction type.");
                return;
            }

            var existing = _projectManager.LoadProject(name);
            if (existing != null)
            {
                ShowError("A project with this name already exists.");
                return;
            }

            var project = new ProjectContext
            {
                Name = name,
                BuildingClass = ((ComboBoxItem)ClassDropdown.SelectedItem).Content.ToString() ?? "",
                State = ((ComboBoxItem)StateDropdown.SelectedItem).Content.ToString() ?? "",
                ConstructionType = ((ComboBoxItem)TypeDropdown.SelectedItem).Content.ToString() ?? ""
            };

            _projectManager.CreateProject(project);
            _projectManager.SetCurrentProject(project);
            _mainPanel.NavigateToChat();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
