using System.Windows;
using System.Windows.Controls;

namespace BuildScope.Views
{
    public partial class SettingsPanel : UserControl
    {
        private readonly MainPanel _mainPanel;

        public SettingsPanel(MainPanel mainPanel)
        {
            _mainPanel = mainPanel;
            InitializeComponent();
            LoadConfig();
        }

        private void LoadConfig()
        {
            UrlInput.Text = Config.GetSupabaseUrl() ?? "";
            KeyInput.Text = Config.GetApiKey() ?? "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlInput.Text.Trim();
            var key = KeyInput.Text.Trim();

            Config.Save(url, key);

            StatusText.Text = "Settings saved.";
            StatusText.Visibility = Visibility.Visible;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _mainPanel.NavigateToChat();
        }
    }
}
