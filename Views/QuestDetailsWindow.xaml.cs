using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Amaurot.Services;

// Add proper using statements to resolve QuestInfo and QuestNpcInfo errors
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using QuestNpcInfo = Amaurot.Services.Entities.QuestNpcInfo;

namespace Amaurot
{
    public partial class QuestDetailsWindow : Window
    {
        private QuestInfo _questInfo;
        private QuestScriptService? _questScriptService;
        private Action<string>? _logDebug; // Add this field

        public QuestDetailsWindow(QuestInfo questInfo, Window? owner = null, QuestScriptService? questScriptService = null)
        {
            InitializeComponent();

            _questScriptService = questScriptService;
            
            // Initialize the debug logging action
            _logDebug = owner is MainWindow mainWindow ? mainWindow.LogDebug : null;

            // ✅ FIX: Don't set Owner to prevent minimization issues
            // Comment out the owner setting logic
            /*
            if (owner != null)
            {
                this.Owner = owner;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                // Find the main window automatically
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null && mainWindow != this)
                {
                    this.Owner = mainWindow;
                    this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            */

            // ✅ FIX: Manual positioning without owner relationship
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (owner != null)
            {
                // Position relative to owner without setting Owner property
                this.Left = owner.Left + (owner.Width - this.Width) / 2;
                this.Top = owner.Top + (owner.Height - this.Height) / 2;
            }

            // ✅ FIX: Critical properties to prevent app minimization
            this.ShowInTaskbar = false;
            this.Topmost = false;
            this.WindowState = WindowState.Normal;

            _questInfo = questInfo;
            PopulateQuestDetails(questInfo);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // ✅ FIX: Simple close without owner manipulation
            this.Close();
        }

        private void PopulateQuestDetails(QuestInfo questInfo)
        {
            // Set the title
            QuestTitleText.Text = questInfo.Name;

            // Enhanced subtitle with location if available
            string subtitle = $"Quest ID: {questInfo.Id}";
            if (!string.IsNullOrEmpty(questInfo.PlaceName))
            {
                subtitle += $" • Location: {questInfo.PlaceName}";
            }
            QuestSubtitleText.Text = subtitle;

            // Show appropriate badges
            if (questInfo.IsMainScenarioQuest)
            {
                MSQBadge.Visibility = Visibility.Visible;
            }

            if (questInfo.IsFeatureQuest)
            {
                FeatureBadge.Visibility = Visibility.Visible;
            }

            // Set level badge
            if (questInfo.ClassJobLevelRequired > 0)
            {
                LevelText.Text = $"LEVEL {questInfo.ClassJobLevelRequired}";
            }
            else
            {
                LevelBadge.Visibility = Visibility.Collapsed;
            }

            // Clear existing details
            QuestDetailsGrid.RowDefinitions.Clear();
            QuestDetailsGrid.Children.Clear();

            int row = 0;

            // Basic Information Section
            AddSectionHeader("Basic Information", row++);
            AddDetailRow("Quest ID:", questInfo.Id.ToString(), row++);

            if (!string.IsNullOrEmpty(questInfo.QuestIdString))
            {
                AddQuestIdentifierRowWithScriptButtons("Quest Identifier:", questInfo.QuestIdString, row++);
            }

            AddDetailRow("Name:", questInfo.Name, row++);
            AddDetailRow("Quest Type:", GetQuestTypeDescription(questInfo), row++);

            // Start NPCs Section
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

            // Location Information Section
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

            // Requirements Section
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

            // Rewards Section
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

            // Additional Information Section
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

        /// <summary>
        /// ✅ NEW: Adds a quest identifier row with script editing buttons
        /// </summary>
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

            // Quest identifier text
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

            // Get extended script information including import status
            if (_questScriptService != null)
            {
                var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(questIdString);

                // Import button if script can be imported
                if (scriptInfo.CanImport)
                {
                    var importButton = new System.Windows.Controls.Button
                    {
                        Content = "📥 Import Script",
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)), // Orange
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

                // Regular editor buttons if script exists
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

                // Status information
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
                        Text = "⚠ Script available in generated output",
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
                // Quest script service not available
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
            QuestDetailsGrid.Children.Add(valuePanel);
        }

        /// <summary>
        /// NEW: Handles importing a quest script from generated output to repository
        /// </summary>
        private void ImportQuestScript(QuestScriptInfoExtended scriptInfo)
        {
            if (_questScriptService == null || string.IsNullOrEmpty(scriptInfo.GeneratedScriptPath))
                return;

            try
            {
                // Show confirmation dialog
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

                // Perform the import
                var importResult = _questScriptService.ImportQuestScript(
                    scriptInfo.QuestIdString, 
                    scriptInfo.GeneratedScriptPath);

                if (importResult.Success)
                {
                    // Show success message
                    System.Windows.MessageBox.Show(
                        $"✅ {importResult.Message}\n\n" +
                        $"The script is now available in your Sapphire repository and can be opened for editing.",
                        "Import Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Refresh the UI to show the new script status
                    RefreshQuestDetails();
                }
                else
                {
                    // Show error message
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

        /// <summary>
        /// NEW: Refreshes the quest details display
        /// </summary>
        private void RefreshQuestDetails()
        {
            PopulateQuestDetails(_questInfo);
        }

        /// <summary>
        /// ✅ Updated OpenScript method to work with extended script info
        /// </summary>
        private void OpenScript(QuestScriptInfoExtended scriptInfo, bool useVSCode)
        {
            if (_questScriptService == null)
                return;

            // Get all script files (C++ and Lua)
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
                    // Open multiple files in VSCode
                    success = _questScriptService.OpenMultipleInVSCode(scriptFiles);
                }
                else
                {
                    // Single file
                    success = _questScriptService.OpenInVSCode(scriptFiles[0]);
                }
            }
            else
            {
                // Visual Studio - open files one by one
                success = true;
                foreach (var file in scriptFiles)
                {
                    var fileSuccess = _questScriptService.OpenInVisualStudio(file);
                    if (!fileSuccess)
                    {
                        success = false;
                        break;
                    }
                    
                    // Small delay between opening files in Visual Studio
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
        }

        // ✅ Fixed namespace conflicts with explicit types
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

        // ✅ Fixed namespace conflicts
        private void ShowQuestGiverOnMap_Click(QuestNpcInfo questGiver)
        {
            try
            {
                // Get the main window
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    var message = "Could not access main window for map navigation";
                    System.Windows.MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Enhanced coordinate validation
                bool hasValidMapId = questGiver.MapId > 0;
                bool hasValidCoordinates = questGiver.MapX != 0 || questGiver.MapY != 0;

                if (!hasValidMapId)
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
                    return;
                }

                if (!hasValidCoordinates)
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

                // Find the territory for this quest giver
                var targetTerritory = mainWindow.Territories?.FirstOrDefault(t => t.MapId == questGiver.MapId);
                if (targetTerritory == null)
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
                    return;
                }

                // Switch to the territory
                mainWindow.TerritoryList.SelectedItem = targetTerritory;

                // Create and add quest marker for this quest giver
                AddQuestGiverMarkerToMap(questGiver, mainWindow);

                var successMessage = $"Map Updated Successfully!\n\n" +
                                   $"• Switched to: {targetTerritory.PlaceName}\n" +
                                   $"• Added marker for: {questGiver.NpcName}\n" +
                                   $"• Map ID: {questGiver.MapId}\n" +
                                   $"• Coordinates: ({questGiver.MapX:F1}, {questGiver.MapY:F1})\n" +
                                   $"• Marker ID: {1000000 + questGiver.NpcId}";

                System.Windows.MessageBox.Show(successMessage, "Debug: Map Navigation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                var errorMessage = $"Error in ShowQuestGiverOnMap_Click:\n\n" +
                                  $"• Exception: {ex.GetType().Name}\n" +
                                  $"• Message: {ex.Message}\n" +
                                  $"• Quest: {_questInfo?.Name ?? "Unknown"}\n" +
                                  $"• Quest Giver: {questGiver?.NpcName ?? "Unknown"}";

                System.Windows.MessageBox.Show(errorMessage, "Debug: Error in Show on Map", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✅ Enhanced quest giver marker with script-based icon selection
        // ✅ Enhanced quest giver marker with script-based icon selection
        private void AddQuestGiverMarkerToMap(QuestNpcInfo questGiver, MainWindow mainWindow)
        {
            try
            {
                // Check if quest script exists in repository
                bool hasQuestScript = false;
                uint iconId = 71031; // Default: script not found icon
                string iconPath = "ui/icon/071000/071031.tex";
                string markerDescription = "Quest Giver (No Script)";

                if (_questScriptService != null && !string.IsNullOrEmpty(_questInfo.QuestIdString))
                {
                    var scriptInfo = _questScriptService.GetQuestScriptInfoExtended(_questInfo.QuestIdString);
                    hasQuestScript = scriptInfo.ExistsInRepo || scriptInfo.CanImport;

                    if (hasQuestScript)
                    {
                        iconId = 61411; // Script available icon
                        iconPath = "ui/icon/061000/061411.tex";
                        markerDescription = scriptInfo.ExistsInRepo ?
                            "Quest Giver (Script Available)" :
                            "Quest Giver (Script Can Be Imported)";
                    }
                }

                // Create a quest marker for this quest giver using the appropriate icon
                var questMarker = new MapMarker
                {
                    Id = 1000000 + questGiver.NpcId, // High ID to distinguish custom markers
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

                // Add to current map markers using AddCustomMarker (the method that exists)
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