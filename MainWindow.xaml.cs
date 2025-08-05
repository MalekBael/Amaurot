using Bitmap = System.Drawing.Bitmap;
using map_editor.Services;
using SaintCoinach.Ex;
using SaintCoinach.Xiv.Items;
using SaintCoinach.Xiv;
using SaintCoinach;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System;
using WfColor = System.Windows.Media.Color;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace map_editor
{
    public partial class MainWindow : Window
    {
        private ARealmReversed? _realm;
        private bool _hideDuplicateTerritories = false;
        private bool _isDragging = false;
        private DataLoaderService? _dataLoaderService;
        private DebugHelper? _debugHelper;
        private double _currentScale = 1.0;
        private List<MapMarker> _allNpcMarkers = new List<MapMarker>();
        private List<MapMarker> _allQuestMarkers = new List<MapMarker>();
        private List<MapMarker> _currentMapMarkers = [];
        private List<NpcInfo> _allNpcs = new List<NpcInfo>();
        private List<NpcInfo> _filteredNpcs = new List<NpcInfo>();
        private MapInteractionService? _mapInteractionService;
        private MapRenderer? _mapRenderer;
        private MapService? _mapService;
        private NpcService? _npcService;
        private QuestLocationService? _questLocationService;
        private QuestMarkerService? _questMarkerService;
        private readonly ObservableCollection<NpcInfo> _filteredNpcsCollection = [];
        public ObservableCollection<NpcInfo> FilteredNpcs => _filteredNpcsCollection;
        private readonly ObservableCollection<BNpcInfo> _filteredBNpcs = [];
        private readonly ObservableCollection<EventInfo> _filteredEvents = [];
        private readonly ObservableCollection<FateInfo> _filteredFates = [];
        private readonly ObservableCollection<QuestInfo> _filteredQuests = [];
        private readonly ObservableCollection<TerritoryInfo> _filteredTerritories = [];
        private SaintCoinach.Xiv.Map? _currentMap;
        private SearchFilterService? _searchFilterService;
        private SettingsService? _settingsService;
        private System.Diagnostics.Process? _questExtractionProcess;
        private System.Threading.CancellationTokenSource? _extractionCancellationSource;
        private System.Windows.Point _lastMousePosition;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
        private TerritoryInfo? _currentTerritory;
        private UIUpdateService? _uiUpdateService;
        public double CurrentScale => _currentScale;
        public ObservableCollection<BNpcInfo> BNpcs { get; set; } = [];
        public ObservableCollection<BNpcInfo> FilteredBNpcs => _filteredBNpcs;
        public ObservableCollection<EventInfo> Events { get; set; } = [];
        public ObservableCollection<FateInfo> Fates { get; set; } = [];
        public ObservableCollection<FateInfo> FilteredFates => _filteredFates;
        public ObservableCollection<QuestInfo> FilteredQuests => _filteredQuests;
        public ObservableCollection<QuestInfo> Quests { get; set; } = [];
        public ObservableCollection<TerritoryInfo> FilteredTerritories => _filteredTerritories;
        public ObservableCollection<TerritoryInfo> Territories { get; set; } = [];

        private QuestScriptService? _questScriptService;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _settingsService = new SettingsService();

            InitializeServices();
            this.Loaded += MainWindow_Loaded;
        }

        private List<MapMarker> CreateNpcMarkers(uint mapId)
        {
            var npcMarkers = new List<MapMarker>();

            var npcsForMap = _allNpcs.Where(npc => npc.MapId == mapId && npc.QuestCount > 0).ToList();

            foreach (var npc in npcsForMap)
            {
                var marker = new MapMarker
                {
                    Id = npc.NpcId,
                    MapId = mapId,
                    PlaceNameId = 0,
                    PlaceName = $"{npc.NpcName} ({npc.QuestCount} quest{(npc.QuestCount != 1 ? "s" : "")})",
                    X = npc.MapX,
                    Y = npc.MapY,
                    Z = npc.MapZ,
                    IconId = 61411,  // Use a generic NPC icon ID, or you could vary by quest type
                    Type = MarkerType.Npc,
                    IsVisible = true
                };

                npcMarkers.Add(marker);
            }

            LogDebug($"Created {npcMarkers.Count} NPC markers for map {mapId}");
            return npcMarkers;
        }

        private void InitializeServices()
        {
            try
            {
                _debugHelper = new DebugHelper(this);
                _searchFilterService = new SearchFilterService(LogDebug);
                _mapInteractionService = new MapInteractionService(LogDebug);
                _uiUpdateService = new UIUpdateService();
                _mapRenderer = new MapRenderer(_realm);

                LogDebug("Basic services initialized successfully");
            }
            catch (Exception ex)
            {
                LogDebug($"Error initializing services: {ex.Message}");
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogDebug("Application exit requested by user");
                WpfApplication.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LogDebug($"Error during application exit: {ex.Message}");
                // Force exit if normal shutdown fails
                Environment.Exit(0);
            }
        }

        private void OpenSapphireRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService != null && _settingsService.IsValidSapphireServerPath())
                {
                    _settingsService.OpenSapphireServerPath();
                    LogDebug("Opened Sapphire Server repository path");
                }
                else
                {
                    WpfMessageBox.Show("Sapphire Server path is not configured or invalid.\n\nPlease configure it in Settings first.",
                        "Sapphire Path Not Set", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Open settings to configure the path
                    OpenSettings_Click(sender, e);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening Sapphire Server repository: {ex.Message}");
                WpfMessageBox.Show($"Error opening Sapphire Server repository: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExtractQuestFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if quest_parse.exe exists
                var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                var questParseExe = Path.Combine(toolsDir, "quest_parse.exe");

                if (!File.Exists(questParseExe))
                {
                    WpfMessageBox.Show($"Quest extraction tool not found at:\n{questParseExe}\n\nPlease ensure the quest_parse.exe is available in the Tools folder.",
                        "Quest Parse Tool Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the FFXIV game path from settings
                string? gamePath = _settingsService?.Settings.GameInstallationPath;

                if (string.IsNullOrEmpty(gamePath) || !_settingsService.IsValidGamePath())
                {
                    WpfMessageBox.Show("FFXIV game path is not configured or invalid.\n\nPlease configure it in Settings first.",
                        "Game Path Not Set", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Open settings to configure the path
                    OpenSettings_Click(sender, e);
                    return;
                }

                var sqpackPath = Path.Combine(gamePath, "game", "sqpack");
                if (!Directory.Exists(sqpackPath))
                {
                    WpfMessageBox.Show($"FFXIV sqpack directory not found at:\n{sqpackPath}\n\nPlease verify your game installation path.",
                        "Sqpack Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LogDebug($"Starting quest file extraction from: {sqpackPath}");
                await RunQuestParseToolWithProgressAsync(questParseExe, sqpackPath);
            }
            catch (Exception ex)
            {
                LogDebug($"Error in quest file extraction: {ex.Message}");
                WpfMessageBox.Show($"Error in quest file extraction: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This method handles when a quest is selected in the quest list
            // We don't need to do anything special here as double-click handles quest details
            if (QuestList.SelectedItem is QuestInfo selectedQuest)
            {
                LogDebug($"Quest selected: {selectedQuest.Name} (ID: {selectedQuest.Id})");
            }
        }

        private void QuestDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the quest from the button's Tag property
                if (sender is System.Windows.Controls.Button button && button.Tag is QuestInfo selectedQuest)
                {
                    LogDebug($"Opening quest details for: {selectedQuest.Name} (ID: {selectedQuest.Id})");
                    ShowQuestDetails(selectedQuest);
                }
                else
                {
                    WpfMessageBox.Show("Please select a quest from the list first.",
                        "No Quest Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening quest details: {ex.Message}");
                WpfMessageBox.Show($"Error opening quest details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowQuestDetails(QuestInfo questInfo)
        {
            try
            {
                var questDetailsWindow = new QuestDetailsWindow(questInfo, this, _questScriptService);
                questDetailsWindow.Show();
                LogDebug($"Opened quest details window for: {questInfo.Name}");
            }
            catch (Exception ex)
            {
                LogDebug($"Error showing quest details: {ex.Message}");
                WpfMessageBox.Show($"Error showing quest details: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle NPC selection if needed
            if (sender is System.Windows.Controls.ListBox npcListBox && npcListBox.SelectedItem is NpcInfo selectedNpc)
            {
                LogDebug($"NPC selected: {selectedNpc.NpcName} (ID: {selectedNpc.NpcId})");
            }
        }

        private void NpcList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.ListBox npcListBox && npcListBox.SelectedItem is NpcInfo selectedNpc)
                {
                    LogDebug($"Double-clicked NPC: {selectedNpc.NpcName} (ID: {selectedNpc.NpcId})");

                    // If the NPC has quests, show the quest popup
                    if (selectedNpc.QuestCount > 0)
                    {
                        var questPopup = new NpcQuestPopupWindow(selectedNpc, this);
                        questPopup.Show();
                        LogDebug($"Opened quest popup for NPC: {selectedNpc.NpcName} ({selectedNpc.QuestCount} quests)");
                    }
                    else
                    {
                        WpfMessageBox.Show($"NPC '{selectedNpc.NpcName}' has no associated quests.",
                            "No Quests", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error handling NPC double-click: {ex.Message}");
                WpfMessageBox.Show($"Error opening NPC details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BNpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle BNpc (Battle NPC/Monster) selection if needed
            if (sender is System.Windows.Controls.ListBox bnpcListBox && bnpcListBox.SelectedItem is BNpcInfo selectedBNpc)
            {
                LogDebug($"BNpc selected: {selectedBNpc.BNpcName} (ID: {selectedBNpc.BNpcNameId})");
            }
        }

        private void FateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle FATE selection if needed
            if (sender is System.Windows.Controls.ListBox fateListBox && fateListBox.SelectedItem is FateInfo selectedFate)
            {
                LogDebug($"FATE selected: {selectedFate.Name} (ID: {selectedFate.FateId})");
            }
        }

        private void OpenSapphireBuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService != null && _settingsService.IsValidSapphireBuildPath())
                {
                    _settingsService.OpenSapphireBuildPath();
                    LogDebug("Opened Sapphire Server build path");
                }
                else
                {
                    WpfMessageBox.Show("Sapphire Server build path is not configured or invalid.\n\nPlease configure it in Settings first.",
                        "Sapphire Build Path Not Set", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Open settings to configure the path
                    OpenSettings_Click(sender, e);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening Sapphire Server build path: {ex.Message}");
                WpfMessageBox.Show($"{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeRealmDependentServices()
        {
            try
            {
                if (_realm != null)
                {
                    _dataLoaderService = new DataLoaderService(_realm, LogDebug);
                    _questMarkerService = new QuestMarkerService(_realm, LogDebug);

                    // ✅ ADD: Initialize quest location service
                    _questLocationService = new QuestLocationService(_realm, LogDebug);

                    LogDebug("Realm-dependent services initialized successfully");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error initializing realm-dependent services: {ex.Message}");
            }
        }

        private async void OnDebugModeChanged()
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                if (QuestList?.ItemsSource != null)
                {
                    var itemsSource = QuestList.ItemsSource;
                    QuestList.ItemsSource = null;
                    QuestList.ItemsSource = itemsSource;
                }
            });
        }

        // ✅ FIXED: Updated LGB Parser event handlers with proper type resolution
        private async void RunLgbParser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if LGB Parser executable exists
                var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                var lgbParserExe = Path.Combine(toolsDir, "lgb-parser.exe");

                if (!File.Exists(lgbParserExe))
                {
                    WpfMessageBox.Show($"LGB Parser not found at:\n{lgbParserExe}\n\nPlease ensure the LGB-Parser project is built and the executable is copied to the Tools folder.",
                        "LGB Parser Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the FFXIV game path from settings
                string? gamePath = _settingsService?.Settings.GameInstallationPath;

                if (string.IsNullOrEmpty(gamePath) || !_settingsService.IsValidGamePath())
                {
                    var result = WpfMessageBox.Show("FFXIV game path is not configured or invalid.\n\nWould you like to:\n\n" +
                                                   "Yes - Open LGB Parser in standalone console window\n" +
                                                   "No - Configure game path first\n" +
                                                   "Cancel - Exit",
                        "Game Path Not Set", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    switch (result)
                    {
                        case MessageBoxResult.Yes:
                            // Open LGB Parser in standalone console window
                            OpenLgbParserInConsole(lgbParserExe, new string[0]);
                            return;

                        case MessageBoxResult.No:
                            // Open settings
                            OpenSettings_Click(sender, e);
                            return;

                        default:
                            return;
                    }
                }

                // ✅ UPDATED: Modified options dialog to include batch parsing
                var optionsResult = WpfMessageBox.Show($"LGB Parser Options:\n\n" +
                                                     $"Yes - Parse all LGB files from client (batch mode)\n" +
                                                     $"No - List available zones\n" +
                                                     $"Cancel - Show help\n\n" +
                                                     $"Game Path: {gamePath}",
                                                     "LGB Parser", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                switch (optionsResult)
                {
                    case MessageBoxResult.Yes:
                        // ✅ NEW: Parse all LGB files using batch command
                        await RunLgbParserBatchModeAsync(lgbParserExe, gamePath);
                        break;

                    case MessageBoxResult.No:
                        // List available zones in console
                        OpenLgbParserInConsole(lgbParserExe, new[] { "--game", gamePath, "--list-zones" });
                        break;

                    default:
                        // Show help in console
                        OpenLgbParserInConsole(lgbParserExe, new string[0]);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error running LGB Parser: {ex.Message}");
                WpfMessageBox.Show($"Failed to run LGB Parser:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✅ ADD: Missing OpenLgbParserInConsole method
        private void OpenLgbParserInConsole(string lgbParserExe, string[] arguments)
        {
            try
            {
                LogDebug($"Opening LGB Parser in console window: {lgbParserExe}");
                LogDebug($"Arguments: {string.Join(" ", arguments.Select(a => $"\"{a}\""))}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"\"{lgbParserExe}\" {string.Join(" ", arguments.Select(a => $"\"{a}\""))}\"",
                    WorkingDirectory = Path.GetDirectoryName(lgbParserExe),
                    UseShellExecute = true, // ✅ This allows the console window to stay open
                    CreateNoWindow = false  // ✅ Show the console window
                };

                System.Diagnostics.Process.Start(processInfo);

                StatusText.Text = "LGB Parser opened in console window";
                LogDebug("LGB Parser console window opened successfully");
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening LGB Parser console: {ex.Message}");
                WpfMessageBox.Show($"Failed to open LGB Parser console:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✅ FIXED: Updated method with better console handling (make it synchronous to avoid ENC0085 error)
        private async Task RunLgbParserProcessAsync(string lgbParserExe, string[] arguments)
        {
            try
            {
                LogDebug($"Starting LGB Parser: {lgbParserExe}");
                LogDebug($"Arguments: {string.Join(" ", arguments.Select(a => $"\"{a}\""))}");

                // If no arguments, open in console window instead
                if (arguments.Length == 0)
                {
                    OpenLgbParserInConsole(lgbParserExe, arguments);
                    return;
                }

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = lgbParserExe,
                    Arguments = string.Join(" ", arguments.Select(a => $"\"{a}\"")),
                    WorkingDirectory = Path.GetDirectoryName(lgbParserExe),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true // Keep this for background processing
                };

                using var process = new System.Diagnostics.Process { StartInfo = processInfo };

                // Capture output for logging
                var outputLines = new List<string>();
                var errorLines = new List<string>();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[LGB Parser] {e.Data}"));
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[LGB Parser Error] {e.Data}"));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for completion
                await Task.Run(() => process.WaitForExit());

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    if (process.ExitCode == 0)
                    {
                        LogDebug("LGB Parser completed successfully");
                        StatusText.Text = $"LGB Parser completed successfully - {outputLines.Count} lines of output";

                        // ✅ For commands that produce a lot of output, show results in a window
                        if (outputLines.Count > 50)
                        {
                            ShowLgbParserResults(outputLines, arguments);
                        }
                    }
                    else
                    {
                        LogDebug($"LGB Parser exited with code: {process.ExitCode}");
                        StatusText.Text = $"LGB Parser exited with code: {process.ExitCode}";

                        if (errorLines.Count > 0)
                        {
                            var errorMessage = string.Join("\n", errorLines.Take(10));
                            WpfMessageBox.Show($"LGB Parser errors:\n\n{errorMessage}",
                                "LGB Parser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"Exception during LGB Parser execution: {ex.Message}");
                    StatusText.Text = "LGB Parser execution failed";
                    WpfMessageBox.Show($"An error occurred running LGB Parser:\n\n{ex.Message}",
                        "LGB Parser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ShowLgbParserResults(List<string> outputLines, string[] arguments)
        {
            try
            {
                var resultsWindow = new Window
                {
                    Title = $"LGB Parser Results - {string.Join(" ", arguments)}",
                    Width = 800,
                    Height = 600,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = string.Join("\n", outputLines),
                    IsReadOnly = true,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch // ✅ FIXED: Fully qualified namespace
                };

                scrollViewer.Content = textBox;
                resultsWindow.Content = scrollViewer;

                resultsWindow.Show();
            }
            catch (Exception ex)
            {
                LogDebug($"Error showing LGB Parser results: {ex.Message}");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_mapRenderer != null)
            {
                _mapRenderer.SetOverlayCanvas(OverlayCanvas);
                _mapRenderer.SetMainWindow(this);
                LogDebug("OverlayCanvas from XAML is now set in MapRenderer.");
            }

            if (OverlayCanvas != null)
            {
                OverlayCanvas.IsHitTestVisible = true;
                OverlayCanvas.Background = null;
            }

            if (MapImageControl.Source is BitmapSource bitmapSource)
            {
                CalculateAndApplyInitialScale(bitmapSource);
            }

            ApplySavedSettings();

            if (_settingsService?.Settings.AutoLoadGameData == true && _settingsService.IsValidGamePath())
            {
                LogDebug("Auto-loading game data from saved path...");
                await LoadFFXIVDataAsync(_settingsService.Settings.GameInstallationPath);
            }

            // CHANGED: Connect NPC marker checkbox events instead of quest markers
            if (ShowNpcMarkersCheckBox != null)
            {
                ShowNpcMarkersCheckBox.Checked += ShowNpcMarkersCheckBox_Checked;
                ShowNpcMarkersCheckBox.Unchecked += ShowNpcMarkersCheckBox_Unchecked;
                ShowNpcMarkersCheckBox.IsChecked = true; // Default to showing NPC markers
                LogDebug("NPC marker checkbox events connected and set to checked");
            }
        }

        private void ApplySavedSettings()
        {
            if (_settingsService == null) return;

            var settings = _settingsService.Settings;

            if (DebugModeCheckBox != null)
            {
                DebugModeCheckBox.IsChecked = settings.DebugMode;

                DebugModeManager.IsDebugModeEnabled = settings.DebugMode;

                _mapService?.SetVerboseDebugMode(settings.DebugMode);
            }

            if (HideDuplicateTerritoriesCheckBox != null)
            {
                HideDuplicateTerritoriesCheckBox.IsChecked = settings.HideDuplicateTerritories;
                _hideDuplicateTerritories = settings.HideDuplicateTerritories;
            }

            if (AutoLoadMenuItem != null)
            {
                AutoLoadMenuItem.IsChecked = settings.AutoLoadGameData;
            }

            LogDebug($"Applied saved settings - Auto-load: {settings.AutoLoadGameData}, Debug: {settings.DebugMode}");
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService != null)
                {
                    var settingsWindow = new SettingsWindow(_settingsService, LogDebug)
                    {
                        Owner = this
                    };

                    if (settingsWindow.ShowDialog() == true)
                    {
                        ApplySavedSettings();
                        LogDebug("Settings updated and applied");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening settings window: {ex.Message}");
                WpfMessageBox.Show($"Error opening settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoLoadMenuItem_Changed(object sender, RoutedEventArgs e)
        {
            if (_settingsService != null && AutoLoadMenuItem != null)
            {
                _settingsService.UpdateAutoLoad(AutoLoadMenuItem.IsChecked);
                LogDebug($"Auto-load setting changed to: {AutoLoadMenuItem.IsChecked}");
            }
        }

        private void MarkerVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (_mapRenderer == null || _currentMapMarkers == null || _currentMapMarkers.Count == 0)
                return;

            var visibleMarkers = new List<MapMarker>();

            foreach (var marker in _currentMapMarkers)
            {
                bool shouldShow = marker.Type switch
                {
                    MarkerType.Aetheryte => ShowAetheryteMarkersCheckBox?.IsChecked == true,
                    MarkerType.Npc => ShowNpcMarkersCheckBox?.IsChecked == true,
                    MarkerType.Shop => ShowShopMarkersCheckBox?.IsChecked == true,
                    MarkerType.Landmark => ShowLandmarkMarkersCheckBox?.IsChecked == true,
                    MarkerType.Fate => ShowFateMarkersCheckBox?.IsChecked == true,
                    MarkerType.Entrance => ShowEntranceMarkersCheckBox?.IsChecked == true,
                    _ => ShowGenericMarkersCheckBox?.IsChecked == true
                };

                if (shouldShow)
                {
                    visibleMarkers.Add(marker);
                }
            }

            RefreshMarkersWithFiltered(visibleMarkers);

            LogDebug($"Marker visibility changed - showing {visibleMarkers.Count} of {_currentMapMarkers.Count} total markers");
        }

        private void RefreshMarkersWithFiltered(List<MapMarker> visibleMarkers)
        {
            if (_mapRenderer == null || _currentMap == null) return;

            var imagePosition = new System.Windows.Point(0, 0);

            if (MapImageControl.Source is BitmapSource bitmapSource)
            {
                var imageSize = new System.Windows.Size(
                    bitmapSource.PixelWidth,
                    bitmapSource.PixelHeight
                );

                _mapRenderer?.DisplayMapMarkers(visibleMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                SyncOverlayWithMap();

                LogDebug($"Refreshed with {visibleMarkers.Count} filtered markers on map");
            }
        }

        private void SyncOverlayWithMap()
        {
            if (OverlayCanvas != null && MapImageControl != null)
            {
                OverlayCanvas.RenderTransform = MapImageControl.RenderTransform;
                OverlayCanvas.RenderTransformOrigin = MapImageControl.RenderTransformOrigin;
                LogDebug("Overlay synchronized with map transform");
            }
        }

        private void ShowProgressOverlay(string operationName = "quest extraction", string initialMessage = "Initializing...")
        {
            ProgressOverlay.Visibility = Visibility.Visible;
            ProgressStatusText.Text = initialMessage;
            QuestExtractionProgressBar.IsIndeterminate = true;
            CancelExtractionButton.IsEnabled = true;

            var titleText = FindProgressTitleTextBlock(ProgressOverlay);
            if (titleText != null)
            {
                titleText.Text = operationName switch
                {
                    "LGB parsing" => "Parsing LGB Files",
                    "quest extraction" => "Extracting Quest Files",
                    _ => "Processing Files"
                };
                LogDebug($"Updated progress title to: {titleText.Text}");
            }
            else
            {
                LogDebug("Could not find progress title TextBlock - title will remain as default");
            }

            _extractionCancellationSource = new System.Threading.CancellationTokenSource();

            LogDebug($"Progress overlay shown for {operationName}");
        }

        private TextBlock? FindProgressTitleTextBlock(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock textBlock &&
                    textBlock.FontSize == 18 &&
                    textBlock.FontWeight == FontWeights.Bold)
                {
                    return textBlock;
                }

                var result = FindProgressTitleTextBlock(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void HideProgressOverlay()
        {
            ProgressOverlay.Visibility = Visibility.Collapsed;
            _questExtractionProcess = null;
            _extractionCancellationSource?.Dispose();
            _extractionCancellationSource = null;

            LogDebug("Progress overlay hidden");
        }

        private void UpdateProgressStatus(string message)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                ProgressStatusText.Text = message;
                StatusText.Text = message;
                LogDebug($"Progress: {message}");
            });
        }

        private void CancelExtraction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogDebug("User requested cancellation of operation");

                if (_questExtractionProcess != null && !_questExtractionProcess.HasExited)
                {
                    _questExtractionProcess.Kill();
                    LogDebug("Process terminated by user");
                }

                _extractionCancellationSource?.Cancel();

                HideProgressOverlay();
                StatusText.Text = "Operation cancelled by user";

                WpfMessageBox.Show("Operation has been cancelled.", "Operation Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogDebug($"Error cancelling operation: {ex.Message}");
                HideProgressOverlay();
            }
        }

        private async Task RunQuestParseToolWithProgressAsync(string questParseExe, string sqpackPath)
        {
            try
            {
                UpdateProgressStatus("Starting quest_parse.exe...");
                LogDebug($"Starting quest_parse.exe: {questParseExe}");
                LogDebug($"Arguments: \"{sqpackPath}\"");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = questParseExe,
                    Arguments = $"\"{sqpackPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(questParseExe),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _questExtractionProcess = new System.Diagnostics.Process { StartInfo = processInfo };

                bool hasReceivedOutput = false;
                var startTime = DateTime.Now;
                int outputLineCount = 0;
                bool processCompleted = false;

                _questExtractionProcess.Exited += (sender, e) =>
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        LogDebug($"quest_parse.exe process exited with code: {_questExtractionProcess.ExitCode}");
                        processCompleted = true;
                    });
                };

                _questExtractionProcess.EnableRaisingEvents = true;

                _questExtractionProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            hasReceivedOutput = true;
                            outputLineCount++;
                            LogDebug($"[quest_parse #{outputLineCount}] {e.Data}");

                            string cleanOutput = e.Data.Trim();
                            if (!string.IsNullOrEmpty(cleanOutput))
                            {
                                if (cleanOutput.Length > 60)
                                {
                                    cleanOutput = string.Concat(cleanOutput.AsSpan(0, 57), "...");
                                }
                                UpdateProgressStatus($"[{outputLineCount}] {cleanOutput}");
                            }
                        });
                    }
                };

                _questExtractionProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                        {
                            hasReceivedOutput = true;
                            LogDebug($"[quest_parse ERROR] {e.Data}");

                            string errorMsg = $"Error: {e.Data.Trim()}";
                            if (errorMsg.Length > 60)
                            {
                                errorMsg = string.Concat(errorMsg.AsSpan(0, 57), "...");
                            }
                            UpdateProgressStatus(errorMsg);
                        });
                    }
                };

                _questExtractionProcess.Start();
                _questExtractionProcess.BeginOutputReadLine();
                _questExtractionProcess.BeginErrorReadLine();

                LogDebug($"Process started. PID: {_questExtractionProcess.Id}");
                UpdateProgressStatus("Quest extraction started - initializing...");

                await Task.Run(async () =>
                {
                    int secondsElapsed = 0;

                    while (!_questExtractionProcess.HasExited)
                    {
                        if (_extractionCancellationSource?.Token.IsCancellationRequested == true)
                        {
                            LogDebug("Cancellation requested during extraction");
                            return;
                        }

                        if (secondsElapsed % 5 == 0)
                        {
                            var elapsed = DateTime.Now - startTime;
                            WpfApplication.Current.Dispatcher.Invoke(() =>
                            {
                                if (hasReceivedOutput)
                                {
                                    UpdateProgressStatus($"Processing... ({outputLineCount} lines, {elapsed.Minutes:D2}:{elapsed.Seconds:D2})");
                                }
                                else
                                {
                                    UpdateProgressStatus($"Processing... (no output yet, {elapsed.Minutes:D2}:{elapsed.Seconds:D2})");
                                }
                            });
                        }

                        await Task.Delay(1000);
                        secondsElapsed++;
                    }

                    LogDebug("Process has exited, waiting for final cleanup...");
                    await Task.Delay(2000);
                }, _extractionCancellationSource?.Token ?? System.Threading.CancellationToken.None);

                if (_extractionCancellationSource?.Token.IsCancellationRequested == true)
                {
                    LogDebug("Extraction was cancelled");
                    return;
                }

                if (!_questExtractionProcess.HasExited)
                {
                    LogDebug("Waiting for process to fully exit...");
                    _questExtractionProcess.WaitForExit(5000);
                }

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    var totalTime = DateTime.Now - startTime;
                    UpdateProgressStatus($"Finishing... ({outputLineCount} lines, took {totalTime.Minutes:D2}:{totalTime.Seconds:D2})");
                });

                await Task.Delay(1000);

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    HideProgressOverlay();

                    LogDebug($"Final process state - HasExited: {_questExtractionProcess.HasExited}, ExitCode: {_questExtractionProcess.ExitCode}");
                    LogDebug($"Output lines received: {outputLineCount}");
                    LogDebug($"Process completed flag: {processCompleted}");

                    if (_questExtractionProcess.ExitCode == 0)
                    {
                        LogDebug("✅ quest_parse.exe completed successfully with exit code 0");
                        StatusText.Text = "Quest file extraction completed successfully";
                        WpfMessageBox.Show($"Quest file extraction completed successfully!\n\nquest_parse.exe finished processing the game data.\n\nOutput lines received: {outputLineCount}",
                            "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogDebug($"❌ quest_parse.exe failed with exit code: {_questExtractionProcess.ExitCode}");
                        StatusText.Text = $"Quest extraction failed (Exit code: {_questExtractionProcess.ExitCode})";
                        WpfMessageBox.Show($"quest_parse.exe failed with exit code: {_questExtractionProcess.ExitCode}\n\nCheck the debug log for details.\n\nOutput lines received: {outputLineCount}",
                            "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (System.OperationCanceledException)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug("quest_parse.exe operation was cancelled");
                    HideProgressOverlay();
                    StatusText.Text = "Quest extraction cancelled";
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"❌ Exception during quest_parse.exe execution: {ex.Message}");
                    LogDebug($"Exception stack trace: {ex.StackTrace}");
                    HideProgressOverlay();
                    StatusText.Text = "Quest extraction failed";
                    WpfMessageBox.Show($"An error occurred during quest extraction:\n\n{ex.Message}",
                        "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async void LoadGameData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select FFXIV game installation folder",
                UseDescriptionForTitle = true,
                SelectedPath = _settingsService?.Settings.GameInstallationPath ?? ""
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                var gamePath = dialog.SelectedPath;
                if (Directory.Exists(gamePath))
                {
                    _settingsService?.UpdateGamePath(gamePath);
                    LogDebug($"Game path saved to settings: {gamePath}");

                    await LoadFFXIVDataAsync(gamePath);
                }
                else
                {
                    WpfMessageBox.Show("Invalid game folder selected.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadFFXIVDataAsync(string gameDirectory)
        {
            try
            {
                StatusText.Text = "Loading FFXIV data...";

                if (!ValidateGameDirectory(gameDirectory))
                {
                    WpfMessageBox.Show(
                        "The selected folder doesn't appear to be a valid FFXIV installation.\n\n" +
                        "Please select the main FFXIV installation folder that contains the 'game' and 'boot' folders.",
                        "Invalid Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await Task.Run(() =>
                {
                    _realm = new ARealmReversed(gameDirectory, SaintCoinach.Ex.Language.English);
                });

                LogDebug($"Initialized realm: {(_realm != null ? "Success" : "Failed")}");

                // ✅ Initialize realm-dependent services after _realm is created
                InitializeRealmDependentServices();

                // ✅ Initialize quest script service
                if (_settingsService != null)
                {
                    _questScriptService = new QuestScriptService(_settingsService, LogDebug);
                    LogDebug("Quest script service initialized");
                }

                _mapService = new MapService(_realm, LogDebug);
                LogDebug("Initialized map service");

                if (_realm != null)
                {
                    _mapRenderer?.UpdateRealm(_realm);
                }

                // ✅ Load data using the new simplified approach
                LogDebug("Starting to load all data...");

                // Load territories
                StatusText.Text = "Loading territories...";
                var territories = await _dataLoaderService!.LoadTerritoriesAsync();
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    Territories.Clear();
                    _filteredTerritories.Clear();
                    foreach (var territory in territories)
                    {
                        Territories.Add(territory);
                        _filteredTerritories.Add(territory);
                    }
                    _uiUpdateService?.UpdateTerritoryCount(_filteredTerritories, TerritoryCountText);
                });

                // Load quests
                StatusText.Text = "Loading quests...";
                var quests = await _dataLoaderService.LoadQuestsAsync();
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    Quests.Clear();
                    _filteredQuests.Clear();
                    foreach (var quest in quests)
                    {
                        Quests.Add(quest);
                        _filteredQuests.Add(quest);
                    }
                    _uiUpdateService?.UpdateQuestCount(_filteredQuests, QuestCountText);
                });

                // Load BNpcS
                StatusText.Text = "Loading NPCs...";
                var bnpcs = await _dataLoaderService.LoadBNpcsAsync();
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    BNpcs.Clear();
                    _filteredBNpcs.Clear();
                    foreach (var bnpc in bnpcs)
                    {
                        BNpcs.Add(bnpc);
                        _filteredBNpcs.Add(bnpc);
                    }
                    _uiUpdateService?.UpdateBNpcCount(_filteredBNpcs, BNpcCountText);
                });

                StatusText.Text = "Loading fates...";
                var fates = await _dataLoaderService.LoadFatesAsync();
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    Fates.Clear();
                    _filteredFates.Clear();
                    foreach (var fate in fates)
                    {
                        Fates.Add(fate);
                        _filteredFates.Add(fate);
                    }
                    _uiUpdateService?.UpdateFateCount(_filteredFates, FateCountText);
                });

                // ✅ UPDATED: Log message to reflect that Events are no longer loaded
                LogDebug($"Basic data loading complete - Territories: {Territories.Count}, Quests: {Quests.Count}, NPCs: {BNpcs.Count}, Fates: {Fates.Count}");

                StatusText.Text = "Extracting quest markers from Level sheet...";
                if (_questMarkerService != null)
                {
                    _allQuestMarkers = await _questMarkerService.ExtractAllQuestMarkersAsync();
                    LogDebug($"🎯 Extracted {_allQuestMarkers.Count} quest markers from Level sheet data");
                }

                if (Territories.Count > 0)
                {
                    LogDebug("Applying initial territory filters...");
                    ApplyTerritoryFilters();
                }

                StatusText.Text = "Extracting quest locations using Libra Eorzea...";
                await _dataLoaderService.LoadQuestLocationsAsync(Quests.ToList());

                if (_settingsService?.Settings.DebugMode == true)
                {
                    _dataLoaderService.SetVerboseDebugMode(true);
                }

                // ✅ FIX: Add the missing call to LoadNpcsAsync()
                await LoadNpcsAsync();

                StatusText.Text = $"Loaded {Territories.Count} territories, {Quests.Count} quests, {BNpcs.Count} NPCs, {Fates.Count} fates, {_allQuestMarkers.Count} quest markers, {_allNpcs.Count} interactive NPCs from: {gameDirectory}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading data: {ex.Message}";
                LogDebug($"Error loading FFXIV data: {ex.Message}\n{ex.StackTrace}");
                WpfMessageBox.Show($"Failed to load FFXIV data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadNpcsAsync()
        {
            try
            {
                if (_realm != null)
                {
                    LogDebug("🧙 Initializing Saint Coinach-based NPC service...");

                    _npcService = new NpcService(_realm, LogDebug);

                    LogDebug("🧙 Loading NPCs from Saint Coinach ENpcResident sheet...");

                    _allNpcs = await _npcService.ExtractNpcsWithPositionsAsync();

                    FilterNpcs();

                    Dispatcher.Invoke(() =>
                    {
                        if (NpcCountText != null)
                        {
                            NpcCountText.Text = $"({_allNpcs.Count})";
                        }

                        if (NpcList != null)
                        {
                            NpcList.ItemsSource = _filteredNpcsCollection;
                        }
                    });

                    LogDebug($"🧙 Loaded {_allNpcs.Count} NPCs from Saint Coinach");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"❌ Error loading NPCs from Saint Coinach: {ex.Message}");
            }
        }

        private async Task LoadDataAsync<T>(Func<Task<List<T>>> loadFunction,
            ObservableCollection<T> sourceCollection,
            ObservableCollection<T> filteredCollection,

            Action<ObservableCollection<T>, TextBlock?> updateCountAction,
            TextBlock? countTextBlock)
        {
            try
            {
                LogDebug($"LoadDataAsync starting for type: {typeof(T).Name}");
                var data = await loadFunction();
                LogDebug($"LoadDataAsync received {data?.Count ?? 0} items of type: {typeof(T).Name}");

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    sourceCollection.Clear();
                    filteredCollection.Clear();
                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            sourceCollection.Add(item);
                            filteredCollection.Add(item);
                        }
                    }
                    updateCountAction(filteredCollection, countTextBlock);
                    LogDebug($"LoadDataAsync completed UI update for {sourceCollection.Count} items of type: {typeof(T).Name}");
                });
            }
            catch (Exception ex)
            {
                LogDebug($"LoadDataAsync error for type {typeof(T).Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source == null || _mapService == null || _mapRenderer == null)
                return;

            System.Windows.Point clickPoint = e.GetPosition(MapCanvas);
            var imagePosition = new System.Windows.Point(0, 0);

            // ✅ Use pattern matching
            if (MapImageControl.Source is not System.Windows.Media.Imaging.BitmapSource bitmapSource)
                return;

            var imageSize = new System.Windows.Size(
                bitmapSource.PixelWidth,
                bitmapSource.PixelHeight
            );

            var coordinates = _mapService.ConvertMapToGameCoordinates(
                clickPoint, imagePosition, _currentScale, imageSize, _currentMap);

            _mapRenderer.ShowCoordinateInfo(coordinates, clickPoint);
            e.Handled = true;
        }

        private void HideDuplicateTerritoriesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool newValue = HideDuplicateTerritoriesCheckBox?.IsChecked == true;

            if (_hideDuplicateTerritories != newValue)
            {
                _hideDuplicateTerritories = newValue;
                LogDebug($"Hide duplicates checkbox changed to: {_hideDuplicateTerritories}");
                ApplyTerritoryFilters();
            }
        }

        private void QuestSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilterService?.FilterQuests(QuestSearchBox.Text ?? "", Quests, _filteredQuests);
            _uiUpdateService?.UpdateQuestCount(_filteredQuests, QuestCountText);
        }

        private void BNpcSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = BNpcSearchBox.Text ?? "";  // ✅ Remove ToLower()

            _filteredBNpcs.Clear();

            foreach (var bnpc in BNpcs)
            {
                // ✅ Use StringComparison.OrdinalIgnoreCase instead of ToLower()
                bool matches = string.IsNullOrEmpty(searchText) ||
                              bnpc.BNpcName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                              bnpc.BNpcBaseId.ToString().Contains(searchText) ||
                              bnpc.TribeName.Contains(searchText, StringComparison.OrdinalIgnoreCase);

                if (matches)
                {
                    _filteredBNpcs.Add(bnpc);
                }
            }

            _uiUpdateService?.UpdateBNpcCount(_filteredBNpcs, BNpcCountText);
        }

        private void TerritorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                ApplyTerritoryFilters();
            };

            _searchDebounceTimer.Start();
        }

        private void ApplyTerritoryFilters()
        {
            LogDebug($"=== ApplyTerritoryFilters START ===");
            LogDebug($"Source Territories count: {Territories.Count}");
            LogDebug($"Hide duplicates: {_hideDuplicateTerritories}");
            LogDebug($"Search text: '{TerritorySearchBox?.Text}'");

            string searchText = TerritorySearchBox?.Text ?? "";  // ✅ Remove ToLower()

            Task.Run(() =>
            {
                var filteredTerritories = Territories.AsEnumerable();

                if (!string.IsNullOrEmpty(searchText))
                {
                    filteredTerritories = filteredTerritories.Where(territory =>
                        territory.PlaceName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||  // ✅ Use StringComparison
                        territory.Id.ToString().Contains(searchText) ||
                        territory.Region.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        territory.TerritoryNameId.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                }

                if (_hideDuplicateTerritories)
                {
                    var seenPlaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var territoriesToKeep = new List<TerritoryInfo>();

                    foreach (var territory in filteredTerritories)
                    {
                        string placeName = territory.PlaceName ?? territory.Name ?? "";

                        if (placeName.StartsWith("[Territory ID:") || string.IsNullOrEmpty(placeName))
                        {
                            territoriesToKeep.Add(territory);
                            continue;
                        }

                        // ✅ Remove Contains check - HashSet.Add returns false if already exists
                        seenPlaceNames.Add(placeName);
                        territoriesToKeep.Add(territory);
                    }

                    filteredTerritories = territoriesToKeep;
                }

                var finalResults = filteredTerritories.ToList();
                LogDebug($"Filtered results count: {finalResults.Count}");

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"Clearing _filteredTerritories (current count: {_filteredTerritories.Count})");
                    _filteredTerritories.Clear();
                    foreach (var territory in finalResults)
                    {
                        _filteredTerritories.Add(territory);
                    }
                    LogDebug($"Added {_filteredTerritories.Count} territories to _filteredTerritories");
                    _uiUpdateService?.UpdateTerritoryCount(_filteredTerritories, TerritoryCountText);
                });

                LogDebug($"=== ApplyTerritoryFilters END ===");
            });
        }

        // ✅ FIXED: EventSearchBox text changed event - ensure proper filtering
        private void NpcSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ✅ Add debouncing to prevent excessive filtering during typing
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                FilterNpcs(); // ✅ Use the fixed FilterNpcs method
            };

            _searchDebounceTimer.Start();
        }

        private void FateSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = FateSearchBox.Text ?? "";  // ✅ Remove ToLower()

            _filteredFates.Clear();

            foreach (var fate in Fates)
            {
                // ✅ Use StringComparison.OrdinalIgnoreCase
                bool matches = string.IsNullOrEmpty(searchText) ||
                              fate.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                              fate.FateId.ToString().Contains(searchText);

                if (matches)
                {
                    _filteredFates.Add(fate);
                }
            }

            _uiUpdateService?.UpdateFateCount(_filteredFates, FateCountText);
        }

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _mapInteractionService?.HandleMouseWheel(e, MapCanvas, (WpfImage)MapImageControl,
                ref _currentScale, OverlayCanvas, SyncOverlayWithMap, RefreshMarkers);
            StatusText.Text = $"Zoom: {_currentScale:P0}";
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source != null)
            {
                _lastMousePosition = e.GetPosition(MapCanvas);
                _isDragging = true;
                MapCanvas.Cursor = System.Windows.Input.Cursors.Hand;
                MapCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
                MapCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed && MapImageControl.Source != null)
            {
                var currentPosition = e.GetPosition(MapCanvas);
                var deltaX = currentPosition.X - _lastMousePosition.X;
                var deltaY = currentPosition.Y - _lastMousePosition.Y;

                // ✅ Use pattern matching
                if (MapImageControl.RenderTransform is TransformGroup transformGroup)
                {
                    var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                    if (translateTransform != null)
                    {
                        translateTransform.X += deltaX;
                        translateTransform.Y += deltaY;
                    }
                }

                _lastMousePosition = currentPosition;
                SyncOverlayWithMap();
            }
            // Removed the debug logging section since _debugLoggingEnabled is always false
        }

        private async void TerritoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TerritoryList.SelectedItem is TerritoryInfo selectedTerritory)
            {
                try
                {
                    StatusText.Text = $"Loading map for {selectedTerritory.PlaceName}...";

                    bool success = await LoadTerritoryMapAsync(selectedTerritory);

                    if (!success)
                    {
                        StatusText.Text = $"Failed to load map for {selectedTerritory.PlaceName}";
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error loading territory: {ex.Message}");
                    StatusText.Text = "Error loading territory map";
                }
            }

            // ✅ ADD: Filter NPCs when territory changes
            if (_allNpcs.Count > 0)
            {
                FilterNpcs();
            }
        }

        private async Task<bool> LoadTerritoryMapAsync(TerritoryInfo territory)
        {
            try
            {
                _currentTerritory = territory;
                _currentMap = null;

                MapInfoText.Text = $"Region: {territory.Region}\n" +
                                   $"Place Name: {territory.PlaceName}\n" +
                                   $"Territory ID: {territory.Id}\n" +
                                   $"Territory Name ID: {territory.TerritoryNameId}\n" +
                                   $"Map ID: {territory.MapId}";

                MapImageControl.Source = null;
                _mapRenderer?.ClearMarkers();

                if (_realm == null)
                {
                    StatusText.Text = "Game data not loaded.";
                    return false;
                }

                var mapSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.Map>();
                _currentMap = mapSheet.FirstOrDefault(m => m.Key == territory.MapId);

                if (_currentMap == null)
                {
                    StatusText.Text = $"Map not found for MapId: {territory.MapId}";
                    return false;
                }

                LogDebug($"Loading map image for Map ID: {_currentMap.Key}");
                var mapImage = _currentMap.MediumImage;
                if (mapImage == null)
                {
                    StatusText.Text = $"Could not load map image for {territory.PlaceName}.";
                    LogDebug($"FAILURE: mapImage is null for Map ID: {_currentMap.Key}");
                    return false;
                }

                using (var bitmap = new Bitmap(mapImage))
                {
                    IntPtr handle = bitmap.GetHbitmap();
                    try
                    {
                        var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                        MapImageControl.Source = bitmapSource;

                        if (!MapCanvas.Children.Contains(MapImageControl))
                        {
                            MapCanvas.Children.Insert(0, MapImageControl);
                            LogDebug("Added MapImageControl to MapCanvas");
                        }

                        CalculateAndApplyInitialScale(bitmapSource);

                        MapPlaceholderText.Visibility = Visibility.Collapsed;

                        StatusText.Text = $"Map loaded for {territory.PlaceName}. Loading markers...";

                        // Load original map markers
                        List<MapMarker> originalMarkers = await Task.Run(() =>
                            _mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>()
                        );

                        // CHANGED: Create NPC markers instead of quest markers
                        var npcMarkersForThisMap = CreateNpcMarkers(territory.MapId);

                        LogDebug($"🧙 NPC MARKER DEBUG for Map {territory.MapId}:");
                        LogDebug($"  NPCs with quests for this map: {npcMarkersForThisMap.Count}");

                        if (npcMarkersForThisMap.Count > 0)
                        {
                            LogDebug($"  First 3 NPC markers for this map:");
                            foreach (var nm in npcMarkersForThisMap.Take(3))
                            {
                                LogDebug($"    - ID:{nm.Id}, Name:'{nm.PlaceName}', Coords:({nm.X:F1},{nm.Y:F1}), Type:{nm.Type}");
                            }
                        }

                        // Combine all markers
                        _currentMapMarkers.Clear();
                        _currentMapMarkers.AddRange(originalMarkers);
                        _currentMapMarkers.AddRange(npcMarkersForThisMap);

                        LogDebug($"Total markers for {territory.PlaceName}: {_currentMapMarkers.Count} (original: {originalMarkers.Count}, npc: {npcMarkersForThisMap.Count})");

                        // DEBUG: Check marker visibility filtering
                        var npcMarkersVisible = _currentMapMarkers.Where(m => m.Type == MarkerType.Npc).Count();
                        var npcCheckboxChecked = ShowNpcMarkersCheckBox?.IsChecked == true;

                        LogDebug($"🔍 MARKER VISIBILITY DEBUG:");
                        LogDebug($"  NPC markers in _currentMapMarkers: {npcMarkersVisible}");
                        LogDebug($"  ShowNpcMarkersCheckBox exists: {ShowNpcMarkersCheckBox != null}");
                        LogDebug($"  ShowNpcMarkersCheckBox checked: {npcCheckboxChecked}");

                        var imagePosition = new System.Windows.Point(0, 0);
                        var imageSize = new System.Windows.Size(
                            bitmapSource.PixelWidth,
                            bitmapSource.PixelHeight);

                        // Apply marker filtering before displaying
                        var visibleMarkers = new List<MapMarker>();
                        foreach (var marker in _currentMapMarkers)
                        {
                            bool shouldShow = marker.Type switch
                            {
                                MarkerType.Aetheryte => ShowAetheryteMarkersCheckBox?.IsChecked == true,
                                MarkerType.Npc => ShowNpcMarkersCheckBox?.IsChecked == true,  // CHANGED
                                MarkerType.Shop => ShowShopMarkersCheckBox?.IsChecked == true,
                                MarkerType.Landmark => ShowLandmarkMarkersCheckBox?.IsChecked == true,
                                MarkerType.Fate => ShowFateMarkersCheckBox?.IsChecked == true,
                                MarkerType.Entrance => ShowEntranceMarkersCheckBox?.IsChecked == true,
                                _ => ShowGenericMarkersCheckBox?.IsChecked == true
                            };

                            if (shouldShow)
                            {
                                visibleMarkers.Add(marker);
                            }
                        }

                        LogDebug($"🔍 FINAL MARKER COUNT:");
                        LogDebug($"  Total markers: {_currentMapMarkers.Count}");
                        LogDebug($"  Visible markers after filtering: {visibleMarkers.Count}");
                        LogDebug($"  NPC markers visible: {visibleMarkers.Count(m => m.Type == MarkerType.Npc)}");

                        _mapRenderer?.DisplayMapMarkers(visibleMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                        SyncOverlayWithMap();
                        DebugFateMarkerPositions();

                        _mapRenderer?.AddDebugGridAndBorders();
                        DiagnoseOverlayCanvas();

                        StatusText.Text = $"Map loaded for '{territory.PlaceName}' - {_currentMapMarkers.Count} markers found ({npcMarkersForThisMap.Count} NPC markers, {visibleMarkers.Count} visible).";
                        return true;
                    }
                    finally
                    {
                        DeleteObject(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                MapInfoText.Text = $"Error loading map: {ex.Message}";
                LogDebug($"Error in LoadTerritoryMapAsync: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void CalculateAndApplyInitialScale(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            _isDragging = false;

            double canvasWidth = MapCanvas.ActualWidth;
            double canvasHeight = MapCanvas.ActualHeight;

            if (canvasWidth <= 1 || canvasHeight <= 1)
            {
                LogDebug("Invalid canvas dimensions, using fallback values");
                canvasWidth = 800;
                canvasHeight = 600;
            }

            double imageWidth = bitmapSource.PixelWidth;
            double imageHeight = bitmapSource.PixelHeight;
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double fitScale = Math.Min(scaleX, scaleY);
            _currentScale = fitScale * 0.9;

            double centeredX = (canvasWidth - (imageWidth * _currentScale)) / 2;
            double centeredY = (canvasHeight - (imageHeight * _currentScale)) / 2;

            MapImageControl.Width = imageWidth;
            MapImageControl.Height = imageHeight;
            Canvas.SetLeft(MapImageControl, 0);
            Canvas.SetTop(MapImageControl, 0);
            Canvas.SetZIndex(MapImageControl, 0);
            MapImageControl.Visibility = Visibility.Visible;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_currentScale, _currentScale));
            transformGroup.Children.Add(new TranslateTransform(centeredX, centeredY));
            MapImageControl.RenderTransform = transformGroup;
            MapImageControl.RenderTransformOrigin = new System.Windows.Point(0, 0);

            LogDebug($"Map scaled to {_currentScale:F2} and positioned via transform at ({centeredX:F1}, {centeredY:F1})");
        }

        private void RefreshMarkers()
        {
            if (_mapRenderer == null || _currentMap == null) return;

            var imagePosition = new System.Windows.Point(0, 0);

            if (MapImageControl.Source is BitmapSource bitmapSource)
            {
                var imageSize = new System.Windows.Size(
                    bitmapSource.PixelWidth,
                    bitmapSource.PixelHeight
                );

                _mapRenderer?.DisplayMapMarkers(_currentMapMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                SyncOverlayWithMap();

                LogDebug($"Refreshed {_currentMapMarkers.Count} markers on map");
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            DebugTextBox.Clear();
        }

        private void SaveLogButton_Click(object sender, RoutedEventArgs e)
        {
            WpfSaveFileDialog saveFileDialog = new WpfSaveFileDialog
            {
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".log",
                FileName = $"MapEditor_Log_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveFileDialog.FileName, DebugTextBox.Text);
                    StatusText.Text = $"Log saved to {saveFileDialog.FileName}";
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show($"Error saving log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ToggleDebugMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                bool debugEnabled = checkBox.IsChecked == true;

                // Update the static debug mode manager
                DebugModeManager.IsDebugModeEnabled = debugEnabled;

                _mapService?.SetVerboseDebugMode(debugEnabled);
                _dataLoaderService?.SetVerboseDebugMode(debugEnabled);

                // Save the setting
                if (_settingsService != null)
                {
                    _settingsService.UpdateDebugMode(debugEnabled);
                }

                LogDebug($"Debug mode {(debugEnabled ? "enabled" : "disabled")}");
            }
        }

        public void HandleNpcMarkerClick(uint npcId)
        {
            try
            {
                var npcInfo = _allNpcs.FirstOrDefault(npc => npc.NpcId == npcId);

                if (npcInfo != null && npcInfo.QuestCount > 0)
                {
                    var questPopup = new NpcQuestPopupWindow(npcInfo, this);
                    questPopup.Show();

                    LogDebug($"Opened quest popup for NPC: {npcInfo.NpcName} ({npcInfo.QuestCount} quests)");
                }
                else
                {
                    LogDebug($"No quest data found for NPC ID: {npcId}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error handling NPC marker click: {ex.Message}");
            }
        }

        public void ToggleNpcMarkers(bool visible)
        {
            try
            {
                var npcMarkers = _currentMapMarkers.Where(m => m.Type == MarkerType.Npc).ToList();

                foreach (var marker in npcMarkers)
                {
                    marker.IsVisible = visible;
                }

                RefreshMarkers();
                LogDebug($"NPC markers {(visible ? "shown" : "hidden")}: {npcMarkers.Count} markers affected");
            }
            catch (Exception ex)
            {
                LogDebug($"Error toggling NPC markers: {ex.Message}");
            }
        }

        // ✅ Keep only ONE AddCustomMarker method (this one should already exist)
        public void AddCustomMarker(MapMarker marker)
        {
            try
            {
                // Remove any existing marker with the same ID
                _currentMapMarkers.RemoveAll(m => m.Id == marker.Id);

                // Add the new marker
                _currentMapMarkers.Add(marker);

                // Refresh the display
                RefreshMarkers();

                LogDebug($"Added custom marker: {marker.PlaceName} at ({marker.X:F1}, {marker.Y:F1})");
            }
            catch (Exception ex)
            {
                LogDebug($"Error adding custom marker: {ex.Message}");
            }
        }

        // Delegated methods to DebugHelper
        public void LogDebug(string message)
        {
            _debugHelper?.LogDebug(message);
        }

        private bool ValidateGameDirectory(string directory)
        {
            return _debugHelper?.ValidateGameDirectory(directory) ?? false;
        }

        private void DebugFateMarkerPositions()
        {
            _debugHelper?.DebugFateMarkerPositions(_currentMapMarkers);
        }

        private void DiagnoseOverlayCanvas()
        {
            _debugHelper?.DiagnoseOverlayCanvas();
        }

        private void RedirectDebugOutput()
        {
            if (_debugHelper != null)
            {
                System.Diagnostics.Trace.Listeners.Add(new TextBoxTraceListener(_debugHelper));
            }
        }

        private async void QuestList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (QuestList.SelectedItem is QuestInfo selectedQuest)
            {
                try
                {
                    LogDebug($"Double-clicked quest: {selectedQuest.Name} (ID: {selectedQuest.Id})");

                    // Check if the quest has valid map data
                    if (selectedQuest.MapId > 0)
                    {
                        // Find the territory that corresponds to this quest's map
                        var targetTerritory = FindTerritoryByMapId(selectedQuest.MapId);

                        if (targetTerritory != null)
                        {
                            LogDebug($"Switching to map for quest '{selectedQuest.Name}' - Territory: {targetTerritory.PlaceName} (MapId: {selectedQuest.MapId})");

                            // Update the territory selection in the UI
                            TerritoryList.SelectedItem = targetTerritory;

                            // Load the map for this territory
                            StatusText.Text = $"Loading map for quest '{selectedQuest.Name}' in {targetTerritory.PlaceName}...";
                            bool success = await LoadTerritoryMapAsync(targetTerritory);

                            if (success)
                            {
                                StatusText.Text = $"Map loaded for quest '{selectedQuest.Name}' in {targetTerritory.PlaceName}";
                            }
                            else
                            {
                                StatusText.Text = $"Failed to load map for quest '{selectedQuest.Name}'";
                            }
                        }
                        else
                        {
                            LogDebug($"No territory found for quest MapId: {selectedQuest.MapId}");
                            WpfMessageBox.Show($"Could not find territory for quest '{selectedQuest.Name}'.\n\nMapId: {selectedQuest.MapId}",
                                              "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else if (!string.IsNullOrEmpty(selectedQuest.PlaceName))
                    {
                        // Try to find territory by PlaceName if MapId is not available
                        var targetTerritory = FindTerritoryByPlaceName(selectedQuest.PlaceName);

                        if (targetTerritory != null)
                        {
                            LogDebug($"Switching to map for quest '{selectedQuest.Name}' by PlaceName - Territory: {targetTerritory.PlaceName}");

                            // Update the territory selection in the UI
                            TerritoryList.SelectedItem = targetTerritory;

                            // Load the map for this territory
                            StatusText.Text = $"Loading map for quest '{selectedQuest.Name}' in {targetTerritory.PlaceName}...";
                            bool success = await LoadTerritoryMapAsync(targetTerritory);

                            if (success)
                            {
                                StatusText.Text = $"Map loaded for quest '{selectedQuest.Name}' in {targetTerritory.PlaceName}";
                            }
                            else
                            {
                                StatusText.Text = $"Failed to load map for quest '{selectedQuest.Name}'";
                            }
                        }
                        else
                        {
                            LogDebug($"No territory found for quest PlaceName: {selectedQuest.PlaceName}");
                            WpfMessageBox.Show($"Could not find territory for quest '{selectedQuest.Name}'.\n\nLocation: {selectedQuest.PlaceName}",
                                              "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        LogDebug($"Quest '{selectedQuest.Name}' has no location data (MapId: {selectedQuest.MapId}, PlaceName: '{selectedQuest.PlaceName}')");
                        WpfMessageBox.Show($"Quest '{selectedQuest.Name}' doesn't have location data.\n\nCannot switch to map.",
                                          "No Location Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error switching to quest map: {ex.Message}");
                    WpfMessageBox.Show($"Error switching to quest map: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private TerritoryInfo? FindTerritoryByMapId(uint mapId)
        {
            return Territories.FirstOrDefault(t => t.MapId == mapId);
        }

        private TerritoryInfo? FindTerritoryByPlaceName(string placeName)
        {
            return Territories.FirstOrDefault(t =>
                string.Equals(t.PlaceName, placeName, StringComparison.OrdinalIgnoreCase));
        }

        private void FilterNpcs()
        {
            try
            {
                string searchText = NpcSearchBox?.Text ?? "";
                uint currentTerritoryMapId = _currentTerritory?.MapId ?? 0;

                // Clear the ObservableCollection instead of the List
                _filteredNpcsCollection.Clear();

                var npcsToShow = _allNpcs.Where(npc =>
                {
                    // Territory filter: only show NPCs from current territory if one is selected
                    bool territoryMatch = currentTerritoryMapId == 0 || npc.MapId == currentTerritoryMapId;

                    // Search filter
                    bool searchMatch = string.IsNullOrEmpty(searchText) ||
                                      npc.NpcName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                      npc.NpcId.ToString().Contains(searchText);

                    return territoryMatch && searchMatch;
                }).ToList();

                // Add to the ObservableCollection instead of replacing the List
                foreach (var npc in npcsToShow)
                {
                    _filteredNpcsCollection.Add(npc);
                }

                Dispatcher.Invoke(() =>
                {
                    if (NpcCountText != null)
                    {
                        NpcCountText.Text = $"({_filteredNpcsCollection.Count})";
                    }
                });

                LogDebug($"Filtered NPCs: {_filteredNpcsCollection.Count} of {_allNpcs.Count} total (Territory: {_currentTerritory?.PlaceName ?? "All"}, Search: '{searchText}')");
            }
            catch (Exception ex)
            {
                LogDebug($"Error filtering NPCs: {ex.Message}");
            }
        }

        // Remove the duplicate _questScriptService declaration at line 1813
        // Keep only the one at the top with other fields (around line 30)

        // Add these missing methods to MainWindow.xaml.cs:

        private void ShowNpcMarkersCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            MarkerVisibility_Changed(sender, e);
        }

        private void ShowNpcMarkersCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MarkerVisibility_Changed(sender, e);
        }

        private async Task RunLgbParserBatchModeAsync(string lgbParserExe, string gamePath)
        {
            try
            {
                ShowProgressOverlay("LGB parsing", "Starting batch LGB parsing...");

                // ✅ CHANGED: Add "json" format argument to default to JSON output when run from map editor
                var arguments = new[] { "--game", gamePath, "--batch", "lgb_output", "json" };
                await RunLgbParserProcessWithProgressAsync(lgbParserExe, arguments);

                HideProgressOverlay();
                StatusText.Text = "LGB batch parsing completed";
            }
            catch (Exception ex)
            {
                HideProgressOverlay();
                LogDebug($"Error in batch LGB parsing: {ex.Message}");
                WpfMessageBox.Show($"Error in batch LGB parsing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RunLgbParserProcessWithProgressAsync(string lgbParserExe, string[] arguments)
        {
            try
            {
                UpdateProgressStatus("Starting LGB parser...");
                LogDebug($"Starting LGB parser: {lgbParserExe}");
                LogDebug($"Arguments: {string.Join(" ", arguments.Select(a => $"\"{a}\""))}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = lgbParserExe,
                    Arguments = string.Join(" ", arguments.Select(a => $"\"{a}\"")),
                    WorkingDirectory = Path.GetDirectoryName(lgbParserExe),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = processInfo };

                var outputLines = new List<string>();
                var errorLines = new List<string>();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LogDebug($"[LGB Parser] {e.Data}");
                            UpdateProgressStatus($"Processing: {e.Data}");
                        });
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[LGB Parser Error] {e.Data}"));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    if (process.ExitCode == 0)
                    {
                        LogDebug("LGB Parser completed successfully");
                        StatusText.Text = $"LGB Parser completed - {outputLines.Count} lines processed";
                    }
                    else
                    {
                        LogDebug($"LGB Parser exited with code: {process.ExitCode}");
                        StatusText.Text = $"LGB Parser failed with exit code: {process.ExitCode}";

                        if (errorLines.Count > 0)
                        {
                            var errorMessage = string.Join("\n", errorLines.Take(10));
                            WpfMessageBox.Show($"LGB Parser errors:\n\n{errorMessage}",
                                "LGB Parser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"Exception during LGB Parser execution: {ex.Message}");
                    StatusText.Text = "LGB Parser execution failed";
                    WpfMessageBox.Show($"An error occurred running LGB Parser:\n\n{ex.Message}",
                        "LGB Parser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }

    public class TerritoryInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryNameId { get; set; } = string.Empty;
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public string Region { get; set; } = string.Empty;
        public uint PlaceNameIdTerr { get; set; }
        public uint RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(PlaceName) && !PlaceName.StartsWith("[Territory ID:"))
            {
                return $"{Id} - {PlaceName}";
            }
            else
            {
                return $"{Id} - Territory {Id}";
            }
        }
    }
}