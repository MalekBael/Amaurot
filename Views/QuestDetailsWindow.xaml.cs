using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Amaurot.Services;

using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using QuestNpcInfo = Amaurot.Services.Entities.QuestNpcInfo;

namespace Amaurot
{
    public partial class QuestDetailsWindow : Window
    {
        private QuestInfo _questInfo;
        private QuestScriptService? _questScriptService;
        private Action<string>? _logDebug;    

        public QuestDetailsWindow(QuestInfo questInfo, Window? owner = null, QuestScriptService? questScriptService = null)
        {
            InitializeComponent();

            _questScriptService = questScriptService;
            
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

            _questInfo = questInfo;
            PopulateQuestDetails(questInfo);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PopulateQuestDetails(QuestInfo questInfo)
        {
            QuestTitleText.Text = questInfo.Name;

            string subtitle = $"Quest ID: {questInfo.Id}";
            if (!string.IsNullOrEmpty(questInfo.PlaceName))
            {
                subtitle += $" • Location: {questInfo.PlaceName}";
            }
            QuestSubtitleText.Text = subtitle;

            if (questInfo.IsMainScenarioQuest)
            {
                MSQBadge.Visibility = Visibility.Visible;
            }

            if (questInfo.IsFeatureQuest)
            {
                FeatureBadge.Visibility = Visibility.Visible;
            }

            if (questInfo.ClassJobLevelRequired > 0)
            {
                LevelText.Text = $"LEVEL {questInfo.ClassJobLevelRequired}";
            }
            else
            {
                LevelBadge.Visibility = Visibility.Collapsed;
            }

            QuestDetailsGrid.RowDefinitions.Clear();
            QuestDetailsGrid.Children.Clear();

            int row = 0;

            AddSectionHeader("Basic Information", row++);
            AddDetailRow("Quest ID:", questInfo.Id.ToString(), row++);

            if (!string.IsNullOrEmpty(questInfo.QuestIdString))
            {
                AddQuestIdentifierRowWithScriptButtons("Quest Identifier:", questInfo.QuestIdString, row++);
            }

            AddDetailRow("Name:", questInfo.Name, row++);
            AddDetailRow("Quest Type:", GetQuestTypeDescription(questInfo), row++);

            if (questInfo.StartNpcs.Any())
            {
                AddSectionHeader("Start NPCs", row++);

                foreach (var startNpc in questInfo.StartNpcs)
                {
                    string npcDetails = $"{startNpc.NpcName}";
                    if (!string.IsNullOrEmpty(startNpc.TerritoryName))
                    {
                        npcDetails += $" - {startNpc.TerritoryName}";
                    }
                    if (startNpc.MapX > 0 || startNpc.MapY > 0)
                    {
                        npcDetails += $" ({startNpc.MapX:F1}, {startNpc.MapY:F1})";
                    }

                    AddDetailRowWithButton("Start NPC:", npcDetails, row++,
                        () => ShowQuestGiverOnMap_Click(startNpc), "Show on Map");
                }
            }

            if (!string.IsNullOrEmpty(questInfo.PlaceName) || questInfo.MapId > 0)
            {
                AddSectionHeader("Location Information", row++);

                if (!string.IsNullOrEmpty(questInfo.PlaceName))
                {
                    AddDetailRow("Location:", questInfo.PlaceName, row++);
                }

                if (questInfo.MapId > 0)
                {
                    AddDetailRow("Map ID:", questInfo.MapId.ToString(), row++);
                }

                if (questInfo.PlaceNameId > 0)
                {
                    AddDetailRow("Place Name ID:", questInfo.PlaceNameId.ToString(), row++);
                }
            }

            if (questInfo.ClassJobLevelRequired > 0 || questInfo.ClassJobCategoryId > 0 || questInfo.PreviousQuestId > 0)
            {
                AddSectionHeader("Requirements", row++);

                if (questInfo.ClassJobLevelRequired > 0)
                {
                    AddDetailRow("Required Level:", questInfo.ClassJobLevelRequired.ToString(), row++);
                }

                if (questInfo.ClassJobCategoryId > 0)
                {
                    AddDetailRow("Class/Job Category ID:", questInfo.ClassJobCategoryId.ToString(), row++);
                }

                if (!string.IsNullOrEmpty(questInfo.ClassJobCategoryName))
                {
                    AddDetailRow("Class/Job Category:", questInfo.ClassJobCategoryName, row++);
                }

                if (questInfo.PreviousQuestId > 0)
                {
                    AddDetailRow("Previous Quest ID:", questInfo.PreviousQuestId.ToString(), row++);
                }
            }

            if (questInfo.ExpReward > 0 || questInfo.GilReward > 0)
            {
                AddSectionHeader("Rewards", row++);

                if (questInfo.ExpReward > 0)
                {
                    AddDetailRow("EXP Reward:", questInfo.ExpReward.ToString(), row++);
                }

                if (questInfo.GilReward > 0)
                {
                    AddDetailRow("Gil Reward:", questInfo.GilReward.ToString(), row++);
                }
            }

            AddSectionHeader("Additional Information", row++);

            if (!string.IsNullOrEmpty(questInfo.JournalGenre))
            {
                AddDetailRow("Journal Genre:", questInfo.JournalGenre, row++);
            }

            if (questInfo.IconId > 0)
            {
                AddDetailRow("Icon ID:", questInfo.IconId.ToString(), row++);
            }

            if (questInfo.IsRepeatable)
            {
                AddDetailRow("Repeatable:", "Yes", row++);
            }
        }

        private void AddQuestIdentifierRowWithScriptButtons(string label, string questIdString, int row)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestDetailsGrid.Children.Add(labelBlock);

            var valuePanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var valueBlock = new TextBlock
            {
                Text = questIdString,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
                FontWeight = FontWeights.SemiBold
            };
            valuePanel.Children.Add(valueBlock);

            if (_questScriptService != null)
            {
                var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(questIdString);

                if (scriptInfo.CanImport)
                {
                    var importButton = new System.Windows.Controls.Button
                    {
                        Content = " Import Script",
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)),  
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 120, 0)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 5, 0),
                        ToolTip = $"Import {questIdString}.cpp from generated output to Sapphire repository"
                    };
                    importButton.Click += (s, e) => ImportQuestScript(scriptInfo);
                    valuePanel.Children.Add(importButton);
                }

                if (scriptInfo.Exists || scriptInfo.HasLuaScript)
                {
                    if (scriptInfo.CanOpenInVSCode)
                    {
                        var filesCount = (scriptInfo.Exists ? 1 : 0) + (scriptInfo.HasLuaScript ? 1 : 0);
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
                            ToolTip = $"Open {filesCount} file(s) for {questIdString} (C++ & Lua) in Visual Studio Code"
                        };
                        vscodeButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: true);
                        valuePanel.Children.Add(vscodeButton);
                    }

                    if (scriptInfo.CanOpenInVisualStudio)
                    {
                        var filesCount = (scriptInfo.Exists ? 1 : 0) + (scriptInfo.HasLuaScript ? 1 : 0);
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
                            ToolTip = $"Open {filesCount} file(s) for {questIdString} (C++ & Lua) in Visual Studio"
                        };
                        vsButton.Click += (s, e) => OpenScript(scriptInfo, useVSCode: false);
                        valuePanel.Children.Add(vsButton);
                    }
                }

                var statusParts = new List<string>();
                if (scriptInfo.ExistsInRepo) statusParts.Add("C++ (Repo)");
                if (scriptInfo.ExistsInGenerated && !scriptInfo.ExistsInRepo) statusParts.Add("C++ (Generated)");
                if (scriptInfo.HasLuaScript) statusParts.Add("Lua");

                if (statusParts.Count > 0)
                {
                    var infoText = new TextBlock
                    {
                        Text = $"✓ Found: {string.Join(" & ", statusParts)}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Green),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    };
                    valuePanel.Children.Add(infoText);
                }
                else if (scriptInfo.ExistsInGenerated)
                {
                    var infoText = new TextBlock
                    {
                        Text = " Script available in generated output",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Orange),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0),
                        ToolTip = "Script exists in generated output but not in repository. Click Import to add it."
                    };
                    valuePanel.Children.Add(infoText);
                }
                else
                {
                    var infoText = new TextBlock
                    {
                        Text = "✗ No scripts found",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0),
                        ToolTip = $"Could not find {questIdString}.cpp in repository or generated output"
                    };
                    valuePanel.Children.Add(infoText);
                }
            }
            else
            {
                var infoText = new TextBlock
                {
                    Text = " Sapphire path not configured",
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
            QuestDetailsGrid.Children.Add(valuePanel);
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
                        $" {importResult.Message}\n\n" +
                        $"The script is now available in your Sapphire repository and can be opened for editing.",
                        "Import Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshQuestDetails();
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $" Import failed:\n\n{importResult.ErrorMessage}",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $" An error occurred during import:\n\n{ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshQuestDetails()
        {
            PopulateQuestDetails(_questInfo);
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
                _logDebug?.Invoke($"Opened {fileCount} file(s) for {scriptInfo.QuestIdString} in {editorShortName}");
            }
        }

        private void AddDetailRowWithButton(string label, string value, int row, System.Action buttonAction, string buttonText)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestDetailsGrid.Children.Add(labelBlock);

            var valuePanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            valuePanel.Children.Add(valueBlock);

            var showButton = new System.Windows.Controls.Button
            {
                Content = buttonText,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Colors.LightBlue),
                BorderBrush = new SolidColorBrush(Colors.Blue),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            showButton.Click += (s, e) => buttonAction();
            valuePanel.Children.Add(showButton);

            Grid.SetRow(valuePanel, row);
            Grid.SetColumn(valuePanel, 1);
            QuestDetailsGrid.Children.Add(valuePanel);
        }

        private void ShowQuestGiverOnMap_Click(QuestNpcInfo questGiver)
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

                bool hasValidMapId = questGiver.MapId > 0;
                bool hasValidCoordinates = questGiver.MapX != 0 || questGiver.MapY != 0;

                if (!hasValidMapId)
                {
                    if (DebugModeManager.IsDebugModeEnabled)
                    {
                        var message = $"Quest Giver has invalid Map ID.\n\n" +
                                     $"Debug Info:\n" +
                                     $"• Quest: {_questInfo.Name} (ID: {_questInfo.Id})\n" +
                                     $"• NPC: {questGiver.NpcName} (ID: {questGiver.NpcId})\n" +
                                     $"• Territory: {questGiver.TerritoryName} (ID: {questGiver.TerritoryId})\n" +
                                     $"• Map ID: {questGiver.MapId} (INVALID - should be > 0)\n" +
                                     $"• Coordinates: ({questGiver.MapX:F1}, {questGiver.MapY:F1})\n\n" +
                                     $"This indicates the LGB parser didn't find location data for this quest.";

                        System.Windows.MessageBox.Show(message, "Debug: No Valid Map Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        _logDebug?.Invoke($"Quest Giver '{questGiver.NpcName}' has invalid Map ID: {questGiver.MapId}");
                    }
                    return;
                }

                if (!hasValidCoordinates)
                {
                    if (DebugModeManager.IsDebugModeEnabled)
                    {
                        var message = $"Quest Giver has Map ID but invalid coordinates.\n\n" +
                                     $"Debug Info:\n" +
                                     $"• Quest: {_questInfo.Name} (ID: {_questInfo.Id})\n" +
                                     $"• NPC: {questGiver.NpcName} (ID: {questGiver.NpcId})\n" +
                                     $"• Map ID: {questGiver.MapId} (Valid)\n" +
                                     $"• Coordinates: ({questGiver.MapX:F1}, {questGiver.MapY:F1}) (INVALID - both are 0)\n\n" +
                                     $"This indicates coordinate conversion failed.";

                        System.Windows.MessageBox.Show(message, "Debug: Invalid Coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        _logDebug?.Invoke($"Quest Giver '{questGiver.NpcName}' has invalid coordinates: ({questGiver.MapX:F1}, {questGiver.MapY:F1})");
                    }
                }

                var targetTerritory = mainWindow.Territories?.FirstOrDefault(t => t.MapId == questGiver.MapId);
                if (targetTerritory == null)
                {
                    if (DebugModeManager.IsDebugModeEnabled)
                    {
                        var availableMapIds = mainWindow.Territories?.Take(10).Select(t => t.MapId.ToString()).ToArray() ?? new string[0];
                        var message = $"Could not find territory for Map ID: {questGiver.MapId}\n\n" +
                                     $"Debug Info:\n" +
                                     $"• Quest Giver: {questGiver.NpcName}\n" +
                                     $"• Looking for Map ID: {questGiver.MapId}\n" +
                                     $"• Available Territories: {mainWindow.Territories?.Count ?? 0}\n" +
                                     $"• Sample Map IDs: {string.Join(", ", availableMapIds)}\n\n" +
                                     $"This indicates a mismatch between the quest data and territory data.";

                        System.Windows.MessageBox.Show(message, "Debug: Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        _logDebug?.Invoke($"Could not find territory for Quest Giver '{questGiver.NpcName}' with Map ID: {questGiver.MapId}");
                    }
                    return;
                }

                mainWindow.TerritoryList.SelectedItem = targetTerritory;

                AddQuestGiverMarkerToMap(questGiver, mainWindow);

                if (DebugModeManager.IsDebugModeEnabled)
                {
                    var successMessage = $"Map Updated Successfully!\n\n" +
                                       $"• Switched to: {targetTerritory.PlaceName}\n" +
                                       $"• Added marker for: {questGiver.NpcName}\n" +
                                       $"• Map ID: {questGiver.MapId}\n" +
                                       $"• Coordinates: ({questGiver.MapX:F1}, {questGiver.MapY:F1})\n" +
                                       $"• Marker ID: {1000000 + questGiver.NpcId}";

                    System.Windows.MessageBox.Show(successMessage, "Debug: Map Navigation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _logDebug?.Invoke($"Navigated to {targetTerritory.PlaceName} and added marker for {questGiver.NpcName}");
                }
            }
            catch (System.Exception ex)
            {
                if (DebugModeManager.IsDebugModeEnabled)
                {
                    var errorMessage = $"Error in ShowQuestGiverOnMap_Click:\n\n" +
                                      $"• Exception: {ex.GetType().Name}\n" +
                                      $"• Message: {ex.Message}\n" +
                                      $"• Quest: {_questInfo?.Name ?? "Unknown"}\n" +
                                      $"• Quest Giver: {questGiver?.NpcName ?? "Unknown"}";

                    System.Windows.MessageBox.Show(errorMessage, "Debug: Error in Show on Map", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    _logDebug?.Invoke($"Error showing quest giver on map: {ex.Message}");
                }
            }
        }

        private void AddQuestGiverMarkerToMap(QuestNpcInfo questGiver, MainWindow mainWindow)
        {
            try
            {
                bool hasQuestScript = false;
                uint iconId = 71031;      
                string iconPath = "ui/icon/071000/071031.tex";
                string markerDescription = "Quest Giver (No Script)";

                if (_questScriptService != null && !string.IsNullOrEmpty(_questInfo.QuestIdString))
                {
                    var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(_questInfo.QuestIdString);
                    hasQuestScript = scriptInfo.ExistsInRepo || scriptInfo.CanImport;

                    if (hasQuestScript)
                    {
                        iconId = 61411;    
                        iconPath = "ui/icon/061000/061411.tex";
                        markerDescription = scriptInfo.ExistsInRepo ?
                            "Quest Giver (Script Available)" :
                            "Quest Giver (Script Can Be Imported)";
                    }
                }

                var questMarker = new MapMarker
                {
                    Id = 1000000 + questGiver.NpcId,       
                    MapId = questGiver.MapId,
                    PlaceNameId = 0,
                    PlaceName = $"{questGiver.NpcName} ({markerDescription})",
                    X = questGiver.MapX,
                    Y = questGiver.MapY,
                    Z = questGiver.MapZ,
                    IconId = iconId,
                    IconPath = iconPath,
                    Type = MarkerType.Quest,
                    IsVisible = true
                };

                mainWindow.AddCustomMarker(questMarker);

                _logDebug?.Invoke($"Added quest marker for {questGiver.NpcName} with icon {iconId} (hasScript: {hasQuestScript})");
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Error adding quest marker: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddDetailRow(string label, string value, int row)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestDetailsGrid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            QuestDetailsGrid.Children.Add(valueBlock);
        }

        private void AddSectionHeader(string title, int row)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            QuestDetailsGrid.Children.Add(headerBlock);
        }

        private string GetQuestTypeDescription(QuestInfo questInfo)
        {
            if (questInfo.IsMainScenarioQuest)
                return "Main Scenario Quest";
            else if (questInfo.IsFeatureQuest)
                return "Feature Quest";
            else
                return "Side Quest";
        }
    }
}