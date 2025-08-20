using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Amaurot.Services;
using QuestBattleInfo = Amaurot.Services.Entities.QuestBattleInfo;

namespace Amaurot
{
    public partial class QuestBattleDetailsWindow : Window
    {
        private readonly QuestBattleInfo _questBattleInfo;
        private QuestBattleScriptService? _questBattleScriptService;
        private Action<string>? _logDebug;

        public QuestBattleDetailsWindow(QuestBattleInfo questBattleInfo, Window? owner = null, QuestBattleScriptService? questBattleScriptService = null)
        {
            InitializeComponent();

            _questBattleScriptService = questBattleScriptService;

            _logDebug = owner is MainWindow mainWindow ? mainWindow.LogDebug : null;

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (owner != null)
            {
                this.Left = owner.Left + (owner.Width - this.Width) / 2;
                this.Top = owner.Top + (owner.Height - this.Height) / 2;
            }

            this.ShowInTaskbar = false;
            this.Topmost = false;
            this.WindowState = WindowState.Normal;

            _questBattleInfo = questBattleInfo;
            PopulateQuestBattleDetails(questBattleInfo);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PopulateQuestBattleDetails(QuestBattleInfo questBattleInfo)
        {
            QuestBattleTitleText.Text = questBattleInfo.QuestBattleName;

            string subtitle = $"Quest Battle ID: {questBattleInfo.QuestBattleId}";
            if (!string.IsNullOrEmpty(questBattleInfo.TerritoryName))
            {
                subtitle += $" • Territory: {questBattleInfo.TerritoryName}";
            }
            QuestBattleSubtitleText.Text = subtitle;

            QuestBattleDetailsGrid.RowDefinitions.Clear();
            QuestBattleDetailsGrid.Children.Clear();

            int row = 0;

            AddSectionHeader("Basic Information", row++);
            AddDetailRow("Quest Battle ID:", questBattleInfo.QuestBattleId.ToString(), row++);
            AddDetailRow("Name:", questBattleInfo.QuestBattleName, row++);
            AddDetailRow("Layer Name:", questBattleInfo.LayerName, row++);
            AddDetailRow("Asset Type:", questBattleInfo.AssetType, row++);

            if (!string.IsNullOrEmpty(questBattleInfo.Source))
            {
                AddSectionHeader("Script Information", row++);
                AddQuestBattleScriptRowWithButtons("Script File:", questBattleInfo.Source, questBattleInfo, row++);
            }

            if (questBattleInfo.TerritoryId > 0 || !string.IsNullOrEmpty(questBattleInfo.TerritoryName))
            {
                AddSectionHeader("Territory Information", row++);

                if (questBattleInfo.TerritoryId > 0)
                {
                    AddDetailRow("Territory ID:", questBattleInfo.TerritoryId.ToString(), row++);
                }

                if (!string.IsNullOrEmpty(questBattleInfo.TerritoryName))
                {
                    AddDetailRow("Territory Name:", questBattleInfo.TerritoryName, row++);
                }

                if (questBattleInfo.MapId > 0)
                {
                    AddDetailRow("Map ID:", questBattleInfo.MapId.ToString(), row++);
                }
            }

            AddSectionHeader("Technical Information", row++);

            if (questBattleInfo.IconId > 0)
            {
                AddDetailRow("Icon ID:", questBattleInfo.IconId.ToString(), row++);
            }

            if (!string.IsNullOrEmpty(questBattleInfo.IconPath))
            {
                AddDetailRow("Icon Path:", questBattleInfo.IconPath, row++);
            }

            if (questBattleInfo.MapX != 0 || questBattleInfo.MapY != 0 || questBattleInfo.MapZ != 0)
            {
                AddSectionHeader("Location", row++);
                AddDetailRow("Coordinates:", $"({questBattleInfo.MapX:F1}, {questBattleInfo.MapY:F1}, {questBattleInfo.MapZ:F1})", row++);
            }
        }

        private void AddQuestBattleScriptRowWithButtons(string label, string scriptFileName, QuestBattleInfo questBattleInfo, int row)
        {
            QuestBattleDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestBattleDetailsGrid.Children.Add(labelBlock);

            var valuePanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var valueBlock = new TextBlock
            {
                Text = scriptFileName,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
                FontWeight = FontWeights.SemiBold
            };
            valuePanel.Children.Add(valueBlock);

            if (_questBattleScriptService != null)
            {
                var scriptInfo = _questBattleScriptService.GetQuestBattleScriptInfoExtended(
                    questBattleInfo.QuestBattleName, questBattleInfo.QuestBattleId);

                if (scriptInfo.Exists)
                {
                    if (scriptInfo.CanOpenInVSCode)
                    {
                        var vscodeButton = new System.Windows.Controls.Button
                        {
                            Content = "Open in VSCode",
                            Padding = new Thickness(8, 4, 8, 4),
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)),
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Margin = new Thickness(0, 0, 5, 0),
                            ToolTip = $"Open {questBattleInfo.QuestBattleName} script in Visual Studio Code"
                        };
                        vscodeButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: true);
                        valuePanel.Children.Add(vscodeButton);
                    }

                    if (scriptInfo.CanOpenInVisualStudio)
                    {
                        var vsButton = new System.Windows.Controls.Button
                        {
                            Content = "Open in Visual Studio",
                            Padding = new Thickness(8, 4, 8, 4),
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 33, 122)),
                            Foreground = new SolidColorBrush(Colors.White),
                            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 23, 102)),
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Margin = new Thickness(0, 0, 5, 0),
                            ToolTip = $"Open {questBattleInfo.QuestBattleName} script in Visual Studio"
                        };
                        vsButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: false);
                        valuePanel.Children.Add(vsButton);
                    }

                    if (questBattleInfo.MapId > 0)
                    {
                        var mapButton = new System.Windows.Controls.Button
                        {
                            Content = "Show on Map",
                            Padding = new Thickness(8, 4, 8, 4),
                            Background = new SolidColorBrush(Colors.LightBlue),
                            BorderBrush = new SolidColorBrush(Colors.Blue),
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Margin = new Thickness(0, 0, 5, 0),
                            ToolTip = $"Navigate to {questBattleInfo.TerritoryName} on the map"
                        };
                        mapButton.Click += (s, e) => ShowQuestBattleOnMap(questBattleInfo);
                        valuePanel.Children.Add(mapButton);
                    }
                }

                if (scriptInfo.Exists)
                {
                    var infoText = new TextBlock
                    {
                        Text = "✓ Script found",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Green),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    };
                    valuePanel.Children.Add(infoText);
                }
                else
                {
                    var infoText = new TextBlock
                    {
                        Text = "✗ Script not found",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0),
                        ToolTip = $"Could not find {questBattleInfo.QuestBattleName}.cpp in Sapphire repository"
                    };
                    valuePanel.Children.Add(infoText);
                }
            }
            else
            {
                var infoText = new TextBlock
                {
                    Text = "⚠ Sapphire path not configured",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0),
                    ToolTip = "Configure Sapphire Server path in Settings to enable script editing"
                };
                valuePanel.Children.Add(infoText);
            }

            Grid.SetRow(valuePanel, row);
            Grid.SetColumn(valuePanel, 1);
            QuestBattleDetailsGrid.Children.Add(valuePanel);
        }

        private void OpenScript(QuestBattleScriptInfoExtended scriptInfo, bool useVSCode)
        {
            if (_questBattleScriptService == null)
                return;

            var scriptFiles = _questBattleScriptService.FindQuestBattleScriptFiles(
                scriptInfo.QuestBattleName, scriptInfo.QuestBattleId);

            if (scriptFiles.Length == 0)
            {
                System.Windows.MessageBox.Show($"No script files found for {scriptInfo.QuestBattleName}.",
                                   "No Scripts Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool success;
            if (useVSCode)
            {
                if (scriptFiles.Length > 1)
                {
                    success = _questBattleScriptService.OpenMultipleInVSCode(scriptFiles);
                }
                else
                {
                    success = _questBattleScriptService.OpenInVSCode(scriptFiles[0]);
                }
            }
            else
            {
                success = true;
                foreach (var file in scriptFiles)
                {
                    var fileSuccess = _questBattleScriptService.OpenInVisualStudio(file);
                    if (!fileSuccess)
                    {
                        success = false;
                        break;
                    }

                    if (scriptFiles.Length > 1)
                    {
                        System.Threading.Thread.Sleep(500);
                    }
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
                _logDebug?.Invoke($"Opened {fileCount} file(s) for quest battle {scriptInfo.QuestBattleName} in {editorShortName}");
            }
        }

        private void ShowQuestBattleOnMap(QuestBattleInfo questBattleInfo)
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    var message = "Could not access main window for map navigation";

                    if (DebugModeManager.IsDebugModeEnabled)
                    {
                        System.Windows.MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        _logDebug?.Invoke(message);
                    }
                    return;
                }

                if (questBattleInfo.MapId == 0)
                {
                    System.Windows.MessageBox.Show($"Quest Battle '{questBattleInfo.QuestBattleName}' has no map location data.",
                        "No Map Location", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var targetTerritory = mainWindow.Territories?.FirstOrDefault(t => t.MapId == questBattleInfo.MapId);
                if (targetTerritory == null)
                {
                    if (DebugModeManager.IsDebugModeEnabled)
                    {
                        var availableMapIds = mainWindow.Territories?.Take(10).Select(t => t.MapId.ToString()).ToArray() ?? new string[0];
                        var message = $"Could not find territory for Map ID: {questBattleInfo.MapId}\n\n" +
                                     $"Debug Info:\n" +
                                     $"• Quest Battle: {questBattleInfo.QuestBattleName}\n" +
                                     $"• Looking for Map ID: {questBattleInfo.MapId}\n" +
                                     $"• Available Territories: {mainWindow.Territories?.Count ?? 0}\n" +
                                     $"• Sample Map IDs: {string.Join(", ", availableMapIds)}\n\n" +
                                     $"This indicates a mismatch between the quest battle data and territory data.";

                        System.Windows.MessageBox.Show(message, "Debug: Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        _logDebug?.Invoke($"Could not find territory for Quest Battle '{questBattleInfo.QuestBattleName}' with Map ID: {questBattleInfo.MapId}");
                    }
                    return;
                }

                mainWindow.TerritoryList.SelectedItem = targetTerritory;

                AddQuestBattleMarkerToMap(questBattleInfo, mainWindow);

                if (DebugModeManager.IsDebugModeEnabled)
                {
                    var successMessage = $"Map Updated Successfully!\n\n" +
                                       $"• Switched to: {targetTerritory.PlaceName}\n" +
                                       $"• Added marker for: {questBattleInfo.QuestBattleName}\n" +
                                       $"• Map ID: {questBattleInfo.MapId}\n" +
                                       $"• Marker ID: {2000000 + questBattleInfo.QuestBattleId}";

                    System.Windows.MessageBox.Show(successMessage, "Debug: Map Navigation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _logDebug?.Invoke($"Navigated to {targetTerritory.PlaceName} and added marker for {questBattleInfo.QuestBattleName}");
                }
            }
            catch (System.Exception ex)
            {
                if (DebugModeManager.IsDebugModeEnabled)
                {
                    var errorMessage = $"Error in ShowQuestBattleOnMap:\n\n" +
                                      $"• Exception: {ex.GetType().Name}\n" +
                                      $"• Message: {ex.Message}\n" +
                                      $"• Quest Battle: {questBattleInfo?.QuestBattleName ?? "Unknown"}";

                    System.Windows.MessageBox.Show(errorMessage, "Debug: Error in Show on Map", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    _logDebug?.Invoke($"Error showing quest battle on map: {ex.Message}");
                }
            }
        }

        private void AddQuestBattleMarkerToMap(QuestBattleInfo questBattleInfo, MainWindow mainWindow)
        {
            try
            {
                var questBattleMarker = new MapMarker
                {
                    Id = 2000000 + questBattleInfo.QuestBattleId,        
                    MapId = questBattleInfo.MapId,
                    PlaceNameId = 0,
                    PlaceName = $"{questBattleInfo.QuestBattleName} (Quest Battle)",
                    X = questBattleInfo.MapX,
                    Y = questBattleInfo.MapY,
                    Z = questBattleInfo.MapZ,
                    IconId = questBattleInfo.IconId,
                    IconPath = questBattleInfo.IconPath,
                    Type = MarkerType.QuestBattle,
                    IsVisible = true
                };

                mainWindow.AddCustomMarker(questBattleMarker);

                _logDebug?.Invoke($"Added quest battle marker for {questBattleInfo.QuestBattleName} with icon {questBattleInfo.IconId}");
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Error adding quest battle marker: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddDetailRow(string label, string value, int row)
        {
            QuestBattleDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestBattleDetailsGrid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            QuestBattleDetailsGrid.Children.Add(valueBlock);
        }

        private void AddSectionHeader(string title, int row)
        {
            QuestBattleDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerBlock = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.DarkBlue),
                Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 5),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(headerBlock, row);
            Grid.SetColumn(headerBlock, 0);
            Grid.SetColumnSpan(headerBlock, 2);
            QuestBattleDetailsGrid.Children.Add(headerBlock);
        }
    }
}