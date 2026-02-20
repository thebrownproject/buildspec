using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildScope.Views
{
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? UserTemplate { get; set; }
        public DataTemplate? AssistantTemplate { get; set; }
        public DataTemplate? LoadingTemplate { get; set; }
        public DataTemplate? WelcomeTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is ChatMessage msg)
            {
                return msg.Type switch
                {
                    MessageType.User => UserTemplate,
                    MessageType.Assistant => AssistantTemplate,
                    MessageType.Loading => LoadingTemplate,
                    MessageType.Welcome => WelcomeTemplate,
                    _ => AssistantTemplate
                };
            }
            return base.SelectTemplate(item, container);
        }
    }

    public partial class ChatPanel : UserControl
    {
        private readonly MainPanel _mainPanel;
        private readonly ProjectManager _projectManager;
        private readonly ObservableCollection<ChatMessage> _messages = new();
        private bool _isProcessing;
        private bool _suppressDropdownEvent;

        public ChatPanel(MainPanel mainPanel, ProjectManager projectManager)
        {
            _mainPanel = mainPanel;
            _projectManager = projectManager;

            InitializeComponent();

            var selector = new ChatMessageTemplateSelector
            {
                UserTemplate = (DataTemplate)Resources["UserMessageTemplate"],
                AssistantTemplate = (DataTemplate)Resources["AssistantMessageTemplate"],
                LoadingTemplate = (DataTemplate)Resources["LoadingMessageTemplate"],
                WelcomeTemplate = (DataTemplate)Resources["WelcomeMessageTemplate"]
            };

            MessagesPanel.ItemTemplateSelector = selector;
            MessagesPanel.ItemsSource = _messages;

            RefreshProjects();
            ShowWelcomeOrChat();
        }

        public void RefreshProjects()
        {
            _suppressDropdownEvent = true;

            var projects = _projectManager.ListProjects();
            ProjectDropdown.Items.Clear();

            foreach (var project in projects)
                ProjectDropdown.Items.Add(project);

            ProjectDropdown.DisplayMemberPath = "";

            // Select current project if set
            if (_projectManager.CurrentProject != null)
            {
                for (int i = 0; i < ProjectDropdown.Items.Count; i++)
                {
                    if (ProjectDropdown.Items[i] is ProjectContext p &&
                        p.Name == _projectManager.CurrentProject.Name)
                    {
                        ProjectDropdown.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Add "New Project" option
            ProjectDropdown.Items.Add("+ New Project");

            // Show/hide project bar based on whether projects exist
            ProjectBar.Visibility = projects.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            _suppressDropdownEvent = false;
        }

        private void ShowWelcomeOrChat()
        {
            _messages.Clear();
            if (_projectManager.CurrentProject == null)
            {
                _messages.Add(new ChatMessage { Type = MessageType.Welcome });
            }
            else
            {
                var project = _projectManager.CurrentProject;
                _messages.Add(new ChatMessage
                {
                    Type = MessageType.Assistant,
                    Content = $"Ask me anything about NCC compliance for your " +
                              $"Class {project.BuildingClass} building in {project.State}."
                });
            }
        }

        private void ProjectDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDropdownEvent) return;

            if (ProjectDropdown.SelectedItem is string s && s == "+ New Project")
            {
                _mainPanel.NavigateToProjectForm();
                return;
            }

            if (ProjectDropdown.SelectedItem is ProjectContext selected)
            {
                _projectManager.SetCurrentProject(selected);
                ShowWelcomeOrChat();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            _mainPanel.NavigateToSettings();
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            _mainPanel.NavigateToProjectForm();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private async void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isProcessing &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await SendMessage();
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public async Task SendMessage()
        {
            string input = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(input) || _isProcessing)
                return;

            if (_projectManager.CurrentProject == null)
                return;

            // Remove welcome message on first interaction
            var welcome = _messages.FirstOrDefault(m => m.Type == MessageType.Welcome);
            if (welcome != null)
                _messages.Remove(welcome);

            var userMsg = new ChatMessage { Content = input, Type = MessageType.User };
            _messages.Add(userMsg);
            InputBox.Text = "";
            _isProcessing = true;
            SendButton.IsEnabled = false;

            var loadingMsg = new ChatMessage { Type = MessageType.Loading };
            _messages.Add(loadingMsg);
            MessagesScroll.ScrollToBottom();

            try
            {
                var url = Config.GetSupabaseUrl();
                var key = Config.GetApiKey();
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
                {
                    _messages.Remove(loadingMsg);
                    _messages.Add(new ChatMessage
                    {
                        Type = MessageType.Assistant,
                        Content = "Please configure your Supabase URL and API Key in Settings."
                    });
                    return;
                }

                var service = new BuildScopeService(url, key);
                var history = _messages
                    .Where(m => m.Type is MessageType.User or MessageType.Assistant)
                    .ToList();

                var response = await Task.Run(() =>
                    service.QueryAsync(input, _projectManager.CurrentProject, history));

                _messages.Remove(loadingMsg);

                _messages.Add(new ChatMessage
                {
                    Type = MessageType.Assistant,
                    Content = response.Answer,
                    References = response.References
                });
            }
            catch (Exception ex)
            {
                _messages.Remove(loadingMsg);
                _messages.Add(new ChatMessage
                {
                    Type = MessageType.Assistant,
                    Content = $"Error: {ex.Message}"
                });
            }
            finally
            {
                _isProcessing = false;
                SendButton.IsEnabled = true;
                MessagesScroll.ScrollToBottom();
            }
        }

        // Render markdown content when AssistantMessageTemplate loads
        internal void MarkdownContent_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not StackPanel panel) return;
            if (panel.DataContext is not ChatMessage msg) return;

            panel.Children.Clear();
            var lines = MarkdownParser.Parse(msg.Content);

            foreach (var line in lines)
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEC)),
                    LineHeight = 20
                };

                switch (line.Type)
                {
                    case LineType.Header:
                        tb.FontSize = line.HeaderLevel == 1 ? 16 : line.HeaderLevel == 2 ? 14.5 : 13.5;
                        tb.FontWeight = FontWeights.Bold;
                        tb.Margin = new Thickness(0, 6, 0, 4);
                        break;
                    case LineType.Bullet:
                        tb.Margin = new Thickness(12, 2, 0, 2);
                        tb.Inlines.Add(new Run("\u2022  ")
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0x94, 0x4C))
                        });
                        tb.FontSize = 13.5;
                        break;
                    default:
                        tb.FontSize = 13.5;
                        tb.Margin = new Thickness(0, 2, 0, 2);
                        break;
                }

                foreach (var seg in line.Segments)
                {
                    var run = new Run(seg.Text);
                    if (seg.Type == SegmentType.Bold)
                        run.FontWeight = FontWeights.Bold;
                    tb.Inlines.Add(run);
                }

                panel.Children.Add(tb);
            }
        }

        // Render references footer when AssistantMessageTemplate loads
        internal void ReferencesPanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not StackPanel panel) return;
            if (panel.DataContext is not ChatMessage msg) return;

            panel.Children.Clear();

            if (msg.References == null || msg.References.Count == 0)
                return;

            // Separator
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
                Margin = new Thickness(0, 10, 0, 8)
            });

            // "References:" label
            panel.Children.Add(new TextBlock
            {
                Text = "References:",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (var reference in msg.References)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"\u00A7 {reference.Section} - {reference.Title}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0x94, 0x4C)),
                    FontSize = 11.5,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(4, 1, 0, 1)
                });
            }
        }
    }
}
