using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        private readonly Dictionary<uint, QuestInfo?> _questLookupCache = new();
        private readonly Dictionary<string, QuestScriptInfoExtended> _scriptInfoCache = new();
        private readonly List<QuestDisplayInfo> _questDisplayData = new();

        public NpcQuestPopupWindow(NpcInfo npcInfo, MainWindow mainWindow)
        {
            InitializeComponent();
            _npcInfo = npcInfo;
            _mainWindow = mainWindow;

            _questScriptService = _mainWindow.GetQuestScriptService();

            InitializeWindow();

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            NpcNameText.Text = _npcInfo.NpcName;
            NpcLocationText.Text = $"{_npcInfo.TerritoryName} ({_npcInfo.MapX:F1}, {_npcInfo.MapY:F1})";
            QuestCountText.Text = $"{_npcInfo.QuestCount} Quest{(_npcInfo.QuestCount != 1 ? "s" : "")}";

            await Task.Run(() => PrecomputeQuestData());

            await Dispatcher.InvokeAsync(() =>
            {
                CreateOptimizedQuestList();
                // 🎯 DYNAMIC SIZING: Adjust window size based on content
                ApplyDynamicSizing();
            });
        }

        private void PrecomputeQuestData()
        {
            foreach (var npcQuest in _npcInfo.Quests)
            {
                if (!_questLookupCache.ContainsKey(npcQuest.QuestId))
                {
                    var fullQuest = _mainWindow.Quests?.FirstOrDefault(q => q.Id == npcQuest.QuestId);
                    _questLookupCache[npcQuest.QuestId] = fullQuest;
                }

                var questInfo = _questLookupCache[npcQuest.QuestId];

                var displayInfo = new QuestDisplayInfo
                {
                    NpcQuest = npcQuest,
                    FullQuest = questInfo,
                    InternalQuestName = questInfo?.QuestIdString ?? $"Quest_{npcQuest.QuestId}",
                    HasScript = false,
                    ScriptInfo = null
                };

                if (questInfo != null && !string.IsNullOrEmpty(questInfo.QuestIdString) && _questScriptService != null)
                {
                    if (!_scriptInfoCache.ContainsKey(questInfo.QuestIdString))
                    {
                        try
                        {
                            var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(questInfo.QuestIdString);
                            _scriptInfoCache[questInfo.QuestIdString] = scriptInfo;
                        }
                        catch (Exception ex)
                        {
                            _mainWindow.LogDebug($"Error caching script info for {questInfo.QuestIdString}: {ex.Message}");
                            _scriptInfoCache[questInfo.QuestIdString] = new QuestScriptInfoExtended 
                            { 
                                QuestIdString = questInfo.QuestIdString, 
                                Exists = false 
                            };
                        }
                    }

                    displayInfo.ScriptInfo = _scriptInfoCache[questInfo.QuestIdString];
                    displayInfo.HasScript = displayInfo.ScriptInfo.Exists || displayInfo.ScriptInfo.CanImport;
                }

                _questDisplayData.Add(displayInfo);
            }

            _questDisplayData.Sort((a, b) =>
            {
                var levelCompare = a.NpcQuest.LevelRequired.CompareTo(b.NpcQuest.LevelRequired);
                return levelCompare != 0 ? levelCompare : 
                       string.Compare(a.NpcQuest.QuestName, b.NpcQuest.QuestName, StringComparison.OrdinalIgnoreCase);
            });
        }

        // 🎯 DYNAMIC SIZING: Calculate optimal window size based on quest count
        private void ApplyDynamicSizing()
        {
            try
            {
                var questCount = _questDisplayData.Count;

                // Calculate optimal height based on quest count
                const double baseHeight = 180; // Header + padding + close button
                const double questItemHeight = 85; // Height per quest item
                const double maxListHeight = 400; // Maximum list height before scrolling
                const double minListHeight = 100; // Minimum list height

                // Calculate required list height
                var requiredListHeight = Math.Max(minListHeight, questCount * questItemHeight);
                var actualListHeight = Math.Min(maxListHeight, requiredListHeight);

                // Set the ListBox height directly
                QuestListBox.Height = actualListHeight;

                // Calculate total window height
                var targetHeight = baseHeight + actualListHeight;

                // Apply constraints
                var finalHeight = Math.Max(250, Math.Min(700, targetHeight));

                // Set window size
                this.Height = finalHeight;
                this.Width = 500; // ✅ CHANGED: Increased from 450 to 500 for better spacing

                // Enable vertical scrolling only if content exceeds max height
                ScrollViewer.SetVerticalScrollBarVisibility(QuestListBox,
                    requiredListHeight > maxListHeight ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden);

                _mainWindow.LogDebug($"🎯 Dynamic sizing applied: {questCount} quests → {finalHeight}px height (ListBox: {actualListHeight}px)");
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Error applying dynamic sizing: {ex.Message}");
                // Fallback to reasonable default
                this.Height = 300;
                this.Width = 500; // ✅ CHANGED: Updated fallback width too
            }
        }

        private void InitializeWindow()
        {
            this.Title = $"Quests - {_npcInfo.NpcName}";
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ShowInTaskbar = false;

            if (_mainWindow != null)
            {
                this.Left = _mainWindow.Left + (_mainWindow.Width - this.Width) / 2;
                this.Top = _mainWindow.Top + (_mainWindow.Height - this.Height) / 2;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        private void OpenQuestDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is QuestDisplayInfo displayInfo)
            {
                try
                {
                    if (displayInfo.FullQuest != null)
                    {
                        var questDetailsWindow = new QuestDetailsWindow(displayInfo.FullQuest, this, _questScriptService);
                        questDetailsWindow.Show();

                        _mainWindow.LogDebug($"Opened quest details for: {displayInfo.FullQuest.Name} (ID: {displayInfo.FullQuest.Id})");
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"Could not find full quest details for quest ID {displayInfo.NpcQuest.QuestId}",
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

        private void ImportQuestScript(QuestScriptInfoExtended scriptInfo)
        {
            if (_questScriptService == null || string.IsNullOrEmpty(scriptInfo.GeneratedScriptPath))
                return;

            try
            {
                var result = System.Windows.MessageBox.Show(
                    $"Import quest script for {scriptInfo.QuestIdString}?\n\n" +
                    $"This will copy the generated C++ script to your Sapphire repository:\n\n" +
                    $"From: {Path.GetFileName(scriptInfo.GeneratedScriptPath)}\n" +
                    $"To: src/scripts/quest/{scriptInfo.QuestIdString}.cpp\n\n" +
                    $"Continue with import?",
                    "Import Quest Script",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                var importResult = _questScriptService.ImportQuestScript(
                    scriptInfo.QuestIdString,
                    scriptInfo.GeneratedScriptPath);

                if (importResult.Success)
                {
                    System.Windows.MessageBox.Show(
                        $"✅ {importResult.Message}\n\n" +
                        $"The script is now available in your Sapphire repository and can be opened for editing.",
                        "Import Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshScriptInfo(scriptInfo.QuestIdString);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"❌ Import failed:\n\n{importResult.ErrorMessage}",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"❌ An error occurred during import:\n\n{ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshScriptInfo(string questIdString)
        {
            if (_questScriptService == null) return;

            try
            {
                var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(questIdString);
                _scriptInfoCache[questIdString] = scriptInfo;

                var displayInfo = _questDisplayData.FirstOrDefault(d => d.FullQuest?.QuestIdString == questIdString);
                if (displayInfo != null)
                {
                    displayInfo.ScriptInfo = scriptInfo;
                    displayInfo.HasScript = scriptInfo.Exists || scriptInfo.CanImport;
                }

                CreateOptimizedQuestList();
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Error refreshing script info for {questIdString}: {ex.Message}");
            }
        }

        private void OpenScript(QuestScriptInfoExtended scriptInfo, bool useVSCode)
        {
            if (_questScriptService == null)
                return;

            var scriptFiles = _questScriptService.FindQuestScriptFiles(scriptInfo.QuestIdString);

            if (scriptFiles.Length == 0)
            {
                System.Windows.MessageBox.Show($"No script files found for {scriptInfo.QuestIdString}.\n\nLooked for both C++ (.cpp) and Lua (.lua) files.",
                           "No Scripts Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool success;
            if (useVSCode)
            {
                if (scriptFiles.Length > 1)
                {
                    success = _questScriptService.OpenMultipleInVSCode(scriptFiles);
                }
                else
                {
                    success = _questScriptService.OpenInVSCode(scriptFiles[0]);
                }
            }
            else
            {
                // ✅ CHANGED: Use OpenMultipleInVisualStudio for multiple files to open in same instance
                if (scriptFiles.Length > 1)
                {
                    success = _questScriptService.OpenMultipleInVisualStudio(scriptFiles);
                }
                else
                {
                    success = _questScriptService.OpenInVisualStudio(scriptFiles[0]);
                }
            }

            if (!success)
            {
                string editorName = useVSCode ? "Visual Studio Code" : "Visual Studio";
                string commandHint = useVSCode
                    ? "Please ensure Visual Studio Code is installed and accessible via the 'code' command."
                    : "Please ensure Visual Studio is installed and accessible.";

                System.Windows.MessageBox.Show($"Failed to open script files in {editorName}.\n\n{commandHint}",
                               "Error Opening Scripts", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                string editorShortName = useVSCode ? "VS Code" : "Visual Studio";
                var fileCount = scriptFiles.Length;
                _mainWindow.LogDebug($"Opened {fileCount} file(s) for {scriptInfo.QuestIdString} in {editorShortName}");
            }
        }

        private void NavigateToQuest(QuestDisplayInfo displayInfo)
        {
            try
            {
                var targetTerritory = _mainWindow.Territories.FirstOrDefault(t => t.MapId == displayInfo.NpcQuest.MapId);

                if (targetTerritory != null)
                {
                    this.Close();

                    _mainWindow.TerritoryList.SelectedItem = targetTerritory;

                    _mainWindow.LogDebug($"Navigated to quest '{displayInfo.NpcQuest.QuestName}' in {targetTerritory.PlaceName}");
                }
                else
                {
                    WpfMessageBox.Show($"Could not find territory for quest '{displayInfo.NpcQuest.QuestName}'",
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

        private void CreateOptimizedQuestList()
        {
            QuestListBox.Items.Clear();

            foreach (var displayInfo in _questDisplayData)
            {
                var listItem = CreateOptimizedQuestListItem(displayInfo);
                QuestListBox.Items.Add(listItem);
            }
        }

        private ListBoxItem CreateOptimizedQuestListItem(QuestDisplayInfo displayInfo)
        {
            var npcQuest = displayInfo.NpcQuest;
            
            var listItem = new ListBoxItem
            {
                Margin = new Thickness(2, 2, 2, 2),
                Tag = displayInfo       
            };

            // ✅ CHANGED: Adjusted column widths to fix spacing issues
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Level column
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // Quest details column (flexible)
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); // Script buttons column (reduced from 280 to 200)

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
                Text = npcQuest.LevelRequired.ToString(),
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
                Text = npcQuest.QuestName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap // ✅ ADDED: Allow text wrapping for long quest names
            };
            detailsPanel.Children.Add(questNameText);

            var questIdText = new TextBlock
            {
                Text = $"Quest ID: {npcQuest.QuestId}",
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

            // Internal quest name with text wrapping
            typePanel.Children.Add(new TextBlock
            {
                Text = displayInfo.InternalQuestName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.DarkGreen),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = $"Internal Quest Name: {displayInfo.InternalQuestName}",
                TextWrapping = TextWrapping.Wrap // ✅ ADDED: Allow wrapping for long internal names
            });

            if (npcQuest.IsMainScenario)
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

            if (npcQuest.IsFeatureQuest)
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

            var scriptButtonsPanel = CreateOptimizedScriptEditingButtons(displayInfo);
            scriptButtonsPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(scriptButtonsPanel, 2);
            mainGrid.Children.Add(scriptButtonsPanel);

            listItem.Content = mainGrid;

            listItem.MouseDoubleClick += (s, e) => NavigateToQuest(displayInfo);

            return listItem;
        }

        private StackPanel CreateOptimizedScriptEditingButtons(QuestDisplayInfo displayInfo)
        {
            var mainPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            if (displayInfo.FullQuest != null && !string.IsNullOrEmpty(displayInfo.FullQuest.QuestIdString) && 
                _questScriptService != null && displayInfo.ScriptInfo != null)
            {
                var scriptInfo = displayInfo.ScriptInfo;

                var buttonPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                if (scriptInfo.CanImport)
                {
                    var importButton = new System.Windows.Controls.Button
                    {
                        Content = "📥 Import",
                        Padding = new Thickness(6, 3, 6, 3),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)),
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 120, 0)),
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 3, 0),
                        ToolTip = $"Import {displayInfo.FullQuest.QuestIdString}.cpp"
                    };
                    importButton.Click += (s, e) => ImportQuestScript(scriptInfo);
                    buttonPanel.Children.Add(importButton);
                }

                if ((scriptInfo.Exists || scriptInfo.HasLuaScript) && scriptInfo.CanOpenInVSCode)
                {
                    var filesCount = (scriptInfo.Exists ? 1 : 0) + (scriptInfo.HasLuaScript ? 1 : 0);
                    var vscodeButton = new System.Windows.Controls.Button
                    {
                        Content = "VSCode",
                        Padding = new Thickness(6, 3, 6, 3),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)),
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 3, 0),
                        ToolTip = $"Open {filesCount} file(s) in VS Code"
                    };
                    vscodeButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: true);
                    buttonPanel.Children.Add(vscodeButton);
                }

                if ((scriptInfo.Exists || scriptInfo.HasLuaScript) && scriptInfo.CanOpenInVisualStudio)
                {
                    var filesCount = (scriptInfo.Exists ? 1 : 0) + (scriptInfo.HasLuaScript ? 1 : 0);
                    var vsButton = new System.Windows.Controls.Button
                    {
                        Content = "VS",
                        Padding = new Thickness(6, 3, 6, 3),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 33, 122)),
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 23, 102)),
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 3, 0),
                        ToolTip = $"Open {filesCount} file(s) in Visual Studio"
                    };
                    vsButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: false);
                    buttonPanel.Children.Add(vsButton);
                }

                if (buttonPanel.Children.Count > 0)
                {
                    mainPanel.Children.Add(buttonPanel);
                }

                var statusParts = new List<string>();
                if (scriptInfo.ExistsInRepo) statusParts.Add("C++");
                if (scriptInfo.ExistsInGenerated && !scriptInfo.ExistsInRepo) statusParts.Add("C++ (Gen)");
                if (scriptInfo.HasLuaScript) statusParts.Add("Lua");

                TextBlock statusIcon;
                if (statusParts.Count > 0)
                {
                    statusIcon = new TextBlock
                    {
                        Text = $"✓ {string.Join(" & ", statusParts)}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Green),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        ToolTip = $"Found: {string.Join(" & ", statusParts)}"
                    };
                }
                else if (scriptInfo.ExistsInGenerated)
                {
                    statusIcon = new TextBlock
                    {
                        Text = "⚠ Script available",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Orange),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        ToolTip = "Script available for import"
                    };
                }
                else
                {
                    statusIcon = new TextBlock
                    {
                        Text = "✗ No scripts found",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        ToolTip = $"No scripts found for {displayInfo.InternalQuestName}"
                    };
                }

                mainPanel.Children.Add(statusIcon);
            }
            else
            {
                var placeholderIcon = new TextBlock
                {
                    Text = "⚠ Sapphire path not configured",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    ToolTip = "Configure Sapphire Server path in Settings"
                };
                mainPanel.Children.Add(placeholderIcon);
            }

            return mainPanel;
        }

        private class QuestDisplayInfo
        {
            public NpcQuestInfo NpcQuest { get; set; } = null!;
            public QuestInfo? FullQuest { get; set; }
            public string InternalQuestName { get; set; } = string.Empty;
            public bool HasScript { get; set; }
            public QuestScriptInfoExtended? ScriptInfo { get; set; }
        }
    }
}