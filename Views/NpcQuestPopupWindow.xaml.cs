using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Amaurot.Services;
using NpcInfo = Amaurot.Services.Entities.NpcInfo;
using NpcQuestInfo = Amaurot.Services.Entities.NpcQuestInfo;
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
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
                var servicesField = typeof(MainWindow).GetField("_services",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (servicesField?.GetValue(_mainWindow) is not object servicesContainer)
                {
                    _mainWindow.LogDebug("Could not get services container from MainWindow");
                    return null;
                }

                var getMethod = servicesContainer.GetType().GetMethod("Get");
                if (getMethod == null)
                {
                    _mainWindow.LogDebug("Could not find Get method on services container");
                    return null;
                }

                var genericGetMethod = getMethod.MakeGenericMethod(typeof(QuestScriptService));
                var questScriptService = genericGetMethod.Invoke(servicesContainer, null) as QuestScriptService;

                if (questScriptService != null)
                {
                    _mainWindow.LogDebug("Successfully retrieved QuestScriptService from MainWindow");
                }
                else
                {
                    _mainWindow.LogDebug("QuestScriptService not found in MainWindow services");
                }

                return questScriptService;
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Error getting QuestScriptService: {ex.Message}");
                return null;
            }
        }

        private void InitializeWindow()
        {
            this.Title = $"Quests - {_npcInfo.NpcName}";
            this.Width = 650;
            this.Height = 450;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // ✅ FIX: Don't set Owner to prevent minimization issues
            // this.Owner = _mainWindow;
            this.ShowInTaskbar = false;
            
            // ✅ FIX: Position window manually relative to main window
            if (_mainWindow != null)
            {
                this.Left = _mainWindow.Left + (_mainWindow.Width - this.Width) / 2;
                this.Top = _mainWindow.Top + (_mainWindow.Height - this.Height) / 2;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // ✅ FIX: Simple close without owner manipulation
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // ✅ FIX: Ensure clean closing without affecting parent
            base.OnClosing(e);
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
                        System.Windows.MessageBox.Show($"Could not find full quest details for quest ID {npcQuestInfo.QuestId}",
                            "Quest Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    _mainWindow.LogDebug($"Error opening quest details: {ex.Message}");
                    System.Windows.MessageBox.Show($"Error opening quest details: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenScript(QuestScriptInfo scriptInfo, bool useVSCode)
        {
            if (_questScriptService == null || string.IsNullOrEmpty(scriptInfo.ScriptPath))
                return;

            bool success = useVSCode 
                ? _questScriptService.OpenInVSCode(scriptInfo.ScriptPath)
                : _questScriptService.OpenInVisualStudio(scriptInfo.ScriptPath);

            if (!success)
            {
                string editorName = useVSCode ? "Visual Studio Code" : "Visual Studio";
                string commandHint = useVSCode 
                    ? "Please ensure Visual Studio Code is installed and accessible via the 'code' command."
                    : "Please ensure Visual Studio is installed and accessible.";

                WpfMessageBox.Show($"Failed to open {scriptInfo.QuestIdString}.cpp in {editorName}.\n\n{commandHint}",
                               "Error Opening Script", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                string editorShortName = useVSCode ? "VS Code" : "Visual Studio";
                _mainWindow.LogDebug($"Opened {scriptInfo.QuestIdString}.cpp in {editorShortName}");
            }
        }

        // ✅ FIX: Remove async since no await is used
        private void NavigateToQuest(NpcQuestInfo questInfo)
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

        private ListBoxItem CreateQuestListItem(NpcQuestInfo npcQuestInfo)
        {
            var listItem = new ListBoxItem
            {
                Margin = new Thickness(2, 2, 2, 2),
                Tag = npcQuestInfo
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });             

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
                    Foreground = new SolidColorBrush(Colors.Gray),       
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                });
            }

            if (npcQuestInfo.GilReward > 0)
            {
                rewardsPanel.Children.Add(new TextBlock
                {
                    Text = $"{npcQuestInfo.GilReward:N0} Gil",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),       
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                });
            }

            Grid.SetColumn(rewardsPanel, 2);
            mainGrid.Children.Add(rewardsPanel);

            var scriptButtonsPanel = CreateScriptEditingButtons(npcQuestInfo);
            scriptButtonsPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(scriptButtonsPanel, 3);
            mainGrid.Children.Add(scriptButtonsPanel);

            listItem.Content = mainGrid;

            listItem.MouseDoubleClick += (s, e) => NavigateToQuest(npcQuestInfo);

            return listItem;
        }

        private StackPanel CreateScriptEditingButtons(NpcQuestInfo npcQuestInfo)
        {
            var mainPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var fullQuestInfo = _mainWindow.Quests?.FirstOrDefault(q => q.Id == npcQuestInfo.QuestId);

            if (fullQuestInfo != null && !string.IsNullOrEmpty(fullQuestInfo.QuestIdString) && _questScriptService != null)
            {
                var scriptInfo = _questScriptService.GetQuestScriptInfo(fullQuestInfo.QuestIdString);

                if (scriptInfo.Exists)
                {
                    var buttonPanel = new StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    };

                    if (scriptInfo.CanOpenInVSCode)
                    {
                        var vscodeButton = new System.Windows.Controls.Button
                        {
                            Content = "VSCode",         
                            Padding = new Thickness(8, 4, 8, 4),         
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)),
                            FontSize = 11,          
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 5, 0),         
                            ToolTip = $"Open {fullQuestInfo.QuestIdString}.cpp in VS Code",
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        vscodeButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: true);
                        buttonPanel.Children.Add(vscodeButton);
                    }

                    if (scriptInfo.CanOpenInVisualStudio)
                    {
                        var vsButton = new System.Windows.Controls.Button
                        {
                            Content = "Visual Studio",         
                            Padding = new Thickness(8, 4, 8, 4),         
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 33, 122)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 23, 102)),
                            FontSize = 11,          
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 5, 0),         
                            ToolTip = $"Open {fullQuestInfo.QuestIdString}.cpp in Visual Studio",
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        vsButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: false);
                        buttonPanel.Children.Add(vsButton);
                    }

                    mainPanel.Children.Add(buttonPanel);

                    var statusIcon = new TextBlock
                    {
                        Text = "✓ Script found",          
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Green),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 5, 0, 0),       
                        ToolTip = $"Script found: {fullQuestInfo.QuestIdString}.cpp"
                    };
                    mainPanel.Children.Add(statusIcon);
                }
                else
                {
                    var statusIcon = new TextBlock
                    {
                        Text = "✗ Script not found",          
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 5, 0, 0),
                        ToolTip = $"Script not found: {fullQuestInfo.QuestIdString}.cpp"
                    };
                    mainPanel.Children.Add(statusIcon);
                }
            }
            else
            {
                var placeholderIcon = new TextBlock
                {
                    Text = "⚠ Sapphire path not configured",          
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0),
                    ToolTip = "Configure Sapphire Server path in Settings to enable script editing"
                };
                mainPanel.Children.Add(placeholderIcon);
            }

            return mainPanel;
        }
    }
}