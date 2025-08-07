using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Amaurot.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace Amaurot
{
    public partial class NpcQuestPopupWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly NpcInfo _npcInfo;
        private QuestScriptService? _questScriptService;

        public NpcQuestPopupWindow(NpcInfo npcInfo, MainWindow mainWindow)
        {
            InitializeComponent();
            _npcInfo = npcInfo;
            _mainWindow = mainWindow;

            _questScriptService = GetQuestScriptServiceFromMainWindow();

            InitializeWindow();
            PopulateNpcQuests();
        }

        private QuestScriptService? GetQuestScriptServiceFromMainWindow()
        {
            try
            {
                var fieldInfo = typeof(MainWindow).GetField("_questScriptService",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return fieldInfo?.GetValue(_mainWindow) as QuestScriptService;
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Could not get QuestScriptService: {ex.Message}");
                return null;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenQuestDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is NpcQuestInfo npcQuestInfo)
            {
                try
                {
                    var fullQuestInfo = _mainWindow.Quests?.FirstOrDefault(q => q.Id == npcQuestInfo.QuestId);

                    if (fullQuestInfo != null)
                    {
                        var questDetailsWindow = new QuestDetailsWindow(fullQuestInfo, this, _questScriptService);
                        questDetailsWindow.Show();

                        _mainWindow.LogDebug($"Opened quest details for: {fullQuestInfo.Name} (ID: {fullQuestInfo.Id})");
                    }
                    else
                    {
                        WpfMessageBox.Show($"Could not find full quest details for quest ID {npcQuestInfo.QuestId}",
                            "Quest Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    _mainWindow.LogDebug($"Error opening quest details: {ex.Message}");
                    WpfMessageBox.Show($"Error opening quest details: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenScriptInVSCode(QuestScriptInfo scriptInfo)
        {
            if (_questScriptService == null || string.IsNullOrEmpty(scriptInfo.ScriptPath))
                return;

            bool success = _questScriptService.OpenInVSCode(scriptInfo.ScriptPath);

            if (!success)
            {
                WpfMessageBox.Show($"Failed to open {scriptInfo.QuestIdString}.cpp in Visual Studio Code.\n\n" +
                               "Please ensure Visual Studio Code is installed and accessible via the 'code' command.",
                               "Error Opening Script", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _mainWindow.LogDebug($"Opened {scriptInfo.QuestIdString}.cpp in VS Code");
            }
        }

        private void OpenScriptInVisualStudio(QuestScriptInfo scriptInfo)
        {
            if (_questScriptService == null || string.IsNullOrEmpty(scriptInfo.ScriptPath))
                return;

            bool success = _questScriptService.OpenInVisualStudio(scriptInfo.ScriptPath);

            if (!success)
            {
                WpfMessageBox.Show($"Failed to open {scriptInfo.QuestIdString}.cpp in Visual Studio.\n\n" +
                               "Please ensure Visual Studio is installed and accessible.",
                               "Error Opening Script", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _mainWindow.LogDebug($"Opened {scriptInfo.QuestIdString}.cpp in Visual Studio");
            }
        }

        private void InitializeWindow()
        {
            this.Title = $"Quests - {_npcInfo.NpcName}";
            this.Width = 650;
            this.Height = 450;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.Owner = _mainWindow;
            this.ShowInTaskbar = false;
        }

        private async void NavigateToQuest(NpcQuestInfo questInfo)
        {
            try
            {
                var targetTerritory = _mainWindow.Territories.FirstOrDefault(t => t.MapId == questInfo.MapId);

                if (targetTerritory != null)
                {
                    this.Close();

                    _mainWindow.TerritoryList.SelectedItem = targetTerritory;

                    _mainWindow.LogDebug($"Navigated to quest '{questInfo.QuestName}' in {targetTerritory.PlaceName}");
                }
                else
                {
                    WpfMessageBox.Show($"Could not find territory for quest '{questInfo.QuestName}'",
                        "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Error navigating to quest: {ex.Message}");
                WpfMessageBox.Show($"Error navigating to quest: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateNpcQuests()
        {
            NpcNameText.Text = _npcInfo.NpcName;
            NpcLocationText.Text = $"{_npcInfo.TerritoryName} ({_npcInfo.MapX:F1}, {_npcInfo.MapY:F1})";
            QuestCountText.Text = $"{_npcInfo.QuestCount} Quest{(_npcInfo.QuestCount != 1 ? "s" : "")}";

            CreateEnhancedQuestList();
        }

        private void CreateEnhancedQuestList()
        {
            QuestListBox.Items.Clear();

            foreach (var quest in _npcInfo.Quests.OrderBy(q => q.LevelRequired).ThenBy(q => q.QuestName))
            {
                var listItem = CreateQuestListItem(quest);
                QuestListBox.Items.Add(listItem);
            }
        }

        /// <summary>
        /// ✅ NEW: Creates individual quest list item with integrated script buttons
        /// </summary>
        private ListBoxItem CreateQuestListItem(NpcQuestInfo npcQuestInfo)
        {
            var listItem = new ListBoxItem
            {
                Margin = new Thickness(2, 2, 2, 2),
                Tag = npcQuestInfo
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Level
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Details
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Rewards
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Script buttons

            // Level badge
            var levelBorder = new Border
            {
                Background = new SolidColorBrush(Colors.DarkBlue),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            var levelText = new TextBlock
            {
                Text = npcQuestInfo.LevelRequired.ToString(),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            levelBorder.Child = levelText;
            Grid.SetColumn(levelBorder, 0);
            mainGrid.Children.Add(levelBorder);

            // Quest details panel
            var detailsPanel = new StackPanel
            {
                Margin = new Thickness(10, 5, 10, 5)
            };

            var questNameText = new TextBlock
            {
                Text = npcQuestInfo.QuestName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            };
            detailsPanel.Children.Add(questNameText);

            var questIdText = new TextBlock
            {
                Text = $"Quest ID: {npcQuestInfo.QuestId}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.DarkBlue),
                Margin = new Thickness(0, 2, 0, 0)
            };
            detailsPanel.Children.Add(questIdText);

            var typePanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 0)
            };

            if (!string.IsNullOrEmpty(npcQuestInfo.JournalGenre))
            {
                typePanel.Children.Add(new TextBlock
                {
                    Text = npcQuestInfo.JournalGenre,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Margin = new Thickness(0, 0, 10, 0)
                });
            }

            if (npcQuestInfo.IsMainScenario)
            {
                typePanel.Children.Add(new TextBlock
                {
                    Text = "[MSQ]",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Red),
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }

            if (npcQuestInfo.IsFeatureQuest)
            {
                typePanel.Children.Add(new TextBlock
                {
                    Text = "[Feature]",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Blue)
                });
            }

            detailsPanel.Children.Add(typePanel);
            Grid.SetColumn(detailsPanel, 1);
            mainGrid.Children.Add(detailsPanel);

            // ✅ UPDATED: Rewards panel with gray text colors
            var rewardsPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            if (npcQuestInfo.ExpReward > 0)
            {
                rewardsPanel.Children.Add(new TextBlock
                {
                    Text = $"{npcQuestInfo.ExpReward:N0} EXP",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray), // ✅ CHANGED: From Green to Gray
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                });
            }

            if (npcQuestInfo.GilReward > 0)
            {
                rewardsPanel.Children.Add(new TextBlock
                {
                    Text = $"{npcQuestInfo.GilReward:N0} Gil",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray), // ✅ CHANGED: From Gold to Gray
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                });
            }

            Grid.SetColumn(rewardsPanel, 2);
            mainGrid.Children.Add(rewardsPanel);

            // ✅ UPDATED: Script editing buttons with consistent alignment
            var scriptButtonsPanel = CreateScriptEditingButtons(npcQuestInfo);
            scriptButtonsPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(scriptButtonsPanel, 3);
            mainGrid.Children.Add(scriptButtonsPanel);

            listItem.Content = mainGrid;

            // Handle double-click for navigation
            listItem.MouseDoubleClick += (s, e) => NavigateToQuest(npcQuestInfo);

            return listItem;
        }

        /// <summary>
        /// ✅ UPDATED: Creates script editing buttons with consistent alignment
        /// </summary>
        private StackPanel CreateScriptEditingButtons(NpcQuestInfo npcQuestInfo)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical, // ✅ CHANGED: Vertical for better alignment
                Margin = new Thickness(5, 0, 5, 0), // ✅ UPDATED: Consistent margins
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            // Try to get the full quest info to access QuestIdString
            var fullQuestInfo = _mainWindow.Quests?.FirstOrDefault(q => q.Id == npcQuestInfo.QuestId);

            if (fullQuestInfo != null && !string.IsNullOrEmpty(fullQuestInfo.QuestIdString) && _questScriptService != null)
            {
                var scriptInfo = _questScriptService.GetQuestScriptInfo(fullQuestInfo.QuestIdString);

                if (scriptInfo.Exists)
                {
                    // ✅ UPDATED: VSCode button with consistent sizing
                    if (scriptInfo.CanOpenInVSCode)
                    {
                        var vscodeButton = new System.Windows.Controls.Button
                        {
                            Content = "VSCode",
                            Width = 60, // ✅ ADDED: Fixed width for alignment
                            Height = 22, // ✅ ADDED: Fixed height for alignment
                            Padding = new Thickness(4, 2, 4, 2),
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)),
                            FontSize = 10,
                            Margin = new Thickness(0, 1, 0, 1), // ✅ UPDATED: Consistent vertical spacing
                            ToolTip = $"Open {fullQuestInfo.QuestIdString}.cpp in VS Code",
                            Cursor = System.Windows.Input.Cursors.Hand,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch // ✅ ADDED: Stretch for alignment
                        };
                        vscodeButton.Click += (s, e) => OpenScriptInVSCode(scriptInfo);
                        buttonPanel.Children.Add(vscodeButton);
                    }

                    // ✅ UPDATED: Visual Studio button with consistent sizing
                    if (scriptInfo.CanOpenInVisualStudio)
                    {
                        var vsButton = new System.Windows.Controls.Button
                        {
                            Content = "VS 2022",
                            Width = 60, // ✅ ADDED: Fixed width for alignment
                            Height = 22, // ✅ ADDED: Fixed height for alignment
                            Padding = new Thickness(4, 2, 4, 2),
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 33, 122)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 23, 102)),
                            FontSize = 10,
                            Margin = new Thickness(0, 1, 0, 1), // ✅ UPDATED: Consistent vertical spacing
                            ToolTip = $"Open {fullQuestInfo.QuestIdString}.cpp in Visual Studio",
                            Cursor = System.Windows.Input.Cursors.Hand,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch // ✅ ADDED: Stretch for alignment
                        };
                        vsButton.Click += (s, e) => OpenScriptInVisualStudio(scriptInfo);
                        buttonPanel.Children.Add(vsButton);
                    }

                    // ✅ UPDATED: Script status indicator with consistent alignment
                    var statusIcon = new TextBlock
                    {
                        Text = "⚙",
                        FontSize = 12, // ✅ INCREASED: Slightly larger for visibility
                        Foreground = new SolidColorBrush(Colors.Green),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center, // ✅ ADDED: Center alignment
                        Margin = new Thickness(0, 2, 0, 0),
                        ToolTip = $"Script found: {fullQuestInfo.QuestIdString}.cpp"
                    };
                    buttonPanel.Children.Add(statusIcon);
                }
                else
                {
                    // ✅ UPDATED: Script not found indicator with consistent alignment
                    var statusIcon = new TextBlock
                    {
                        Text = "⚙",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0),
                        ToolTip = $"Script not found: {fullQuestInfo.QuestIdString}.cpp"
                    };
                    buttonPanel.Children.Add(statusIcon);
                }
            }
            else
            {
                // ✅ NEW: Add placeholder when no script info available to maintain consistent spacing
                var placeholderIcon = new TextBlock
                {
                    Text = "—",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.LightGray),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    ToolTip = "No script information available"
                };
                buttonPanel.Children.Add(placeholderIcon);
            }

            return buttonPanel;
        }
    }
}