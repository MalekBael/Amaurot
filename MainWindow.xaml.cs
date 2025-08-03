using map_editor.Services;
using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Xiv;
using SaintCoinach.Xiv.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WfColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace map_editor
{
    public partial class MainWindow : Window
    {
        private DataLoaderService? _dataLoaderService;
        private SearchFilterService? _searchFilterService;
        private MapInteractionService? _mapInteractionService;
        private UIUpdateService? _uiUpdateService;
        private DebugHelper? _debugHelper;
        private SettingsService? _settingsService;
        private ARealmReversed? _realm;
        private MapService? _mapService;
        private MapRenderer? _mapRenderer;
        private SaintCoinach.Xiv.Map? _currentMap;
        private TerritoryInfo? _currentTerritory;
        private List<MapMarker> _currentMapMarkers = [];
        private double _currentScale = 1.0;
        private System.Windows.Point _lastMousePosition;
        private bool _isDragging = false;
        private bool _hideDuplicateTerritories = false;

        // ✅ Add to class fields
        private QuestMarkerService? _questMarkerService;
        private List<MapMarker> _allQuestMarkers = new List<MapMarker>();

        // ✅ ADD: Add this missing field declaration
        private QuestLocationService? _questLocationService;

        // ✅ ADD: Add these private fields to the MainWindow class
        private NpcService? _npcService;
        private List<NpcInfo> _allNpcs = new List<NpcInfo>();
        private List<NpcInfo> _filteredNpcs = new List<NpcInfo>();


        public ObservableCollection<TerritoryInfo> Territories { get; set; } = [];
        public ObservableCollection<QuestInfo> Quests { get; set; } = [];
        public ObservableCollection<BNpcInfo> BNpcs { get; set; } = [];
        public ObservableCollection<EventInfo> Events { get; set; } = [];
        public ObservableCollection<FateInfo> Fates { get; set; } = [];

        private readonly ObservableCollection<QuestInfo> _filteredQuests = [];
        private readonly ObservableCollection<BNpcInfo> _filteredBNpcs = [];
        private readonly ObservableCollection<TerritoryInfo> _filteredTerritories = [];
        private readonly ObservableCollection<EventInfo> _filteredEvents = [];
        private readonly ObservableCollection<FateInfo> _filteredFates = [];

        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

        public double CurrentScale => _currentScale;
        public ObservableCollection<TerritoryInfo> FilteredTerritories => _filteredTerritories;
        public ObservableCollection<BNpcInfo> FilteredBNpcs => _filteredBNpcs;
        public ObservableCollection<FateInfo> FilteredFates => _filteredFates;
        public ObservableCollection<QuestInfo> FilteredQuests => _filteredQuests;

        private System.Diagnostics.Process? _questExtractionProcess;
        private System.Threading.CancellationTokenSource? _extractionCancellationSource;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _settingsService = new SettingsService();

            InitializeServices();
            this.Loaded += MainWindow_Loaded;
        }

        // ✅ Fix the InitializeServices method - provide required parameters
        private void InitializeServices()
        {
            try
            {
                _debugHelper = new DebugHelper(this); // ✅ Pass MainWindow instance
                _searchFilterService = new SearchFilterService(LogDebug); // ✅ Pass LogDebug method
                _mapInteractionService = new MapInteractionService(LogDebug); // ✅ Pass LogDebug method
                _uiUpdateService = new UIUpdateService();
                _mapRenderer = new MapRenderer(_realm);

                // Initialize these services when _realm is available (after game data loads)
                // Don't initialize DataLoaderService and QuestMarkerService here since _realm is null

                LogDebug("Basic services initialized successfully");
            }
            catch (Exception ex)
            {
                LogDebug($"Error initializing services: {ex.Message}");
            }
        }

        // ✅ Add method to initialize realm-dependent services after game data loads
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

            // ✅ FIX: Ensure quest marker checkbox events are connected
            if (ShowQuestMarkersCheckBox != null)
            {
                ShowQuestMarkersCheckBox.Checked += ShowQuestMarkersCheckBox_Checked;
                ShowQuestMarkersCheckBox.Unchecked += ShowQuestMarkersCheckBox_Unchecked;
                ShowQuestMarkersCheckBox.IsChecked = true; // Default to showing quest markers
                LogDebug("Quest marker checkbox events connected and set to checked");
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
                    MarkerType.Quest => ShowQuestMarkersCheckBox?.IsChecked == true, // ✅ This will now work
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

        // ✅ UPDATED: Make ShowProgressOverlay configurable for different operations
        private void ShowProgressOverlay(string operationName = "quest extraction", string initialMessage = "Initializing quest_parse.exe...")
        {
            ProgressOverlay.Visibility = Visibility.Visible;
            ProgressStatusText.Text = initialMessage;
            QuestExtractionProgressBar.IsIndeterminate = true;
            CancelExtractionButton.IsEnabled = true;

            // Create cancellation token
            _extractionCancellationSource = new System.Threading.CancellationTokenSource();

            LogDebug($"Progress overlay shown for {operationName}");
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
                        NpcCountText.Text = $"({_allNpcs.Count})";
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

                        // ✅ Load original map markers
                        List<MapMarker> originalMarkers = await Task.Run(() =>
                            _mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>()
                        );

                        // ✅ NEW: Add quest markers for this map
                        var questMarkersForThisMap = _allQuestMarkers.Where(q => q.MapId == territory.MapId).ToList();

                        // ✅ DEBUG: Log detailed information about quest markers
                        LogDebug($"🔍 QUEST MARKER DEBUG for Map {territory.MapId}:");
                        LogDebug($"  Total quest markers available: {_allQuestMarkers.Count}");
                        LogDebug($"  Quest markers for this map: {questMarkersForThisMap.Count}");

                        if (questMarkersForThisMap.Count > 0)
                        {
                            LogDebug($"  First 3 quest markers for this map:");
                            foreach (var qm in questMarkersForThisMap.Take(3))
                            {
                                LogDebug($"    - ID:{qm.Id}, Name:'{qm.PlaceName}', Coords:({qm.X:F1},{qm.Y:F1}), Type:{qm.Type}, Visible:{qm.IsVisible}");
                            }
                        }

                        // ✅ Combine all markers
                        _currentMapMarkers.Clear();
                        _currentMapMarkers.AddRange(originalMarkers);
                        _currentMapMarkers.AddRange(questMarkersForThisMap);

                        LogDebug($"Total markers for {territory.PlaceName}: {_currentMapMarkers.Count} (original: {originalMarkers.Count}, quest: {questMarkersForThisMap.Count})");

                        // ✅ DEBUG: Check marker visibility filtering
                        var questMarkersVisible = _currentMapMarkers.Where(m => m.Type == MarkerType.Quest).Count();
                        var questCheckboxChecked = ShowQuestMarkersCheckBox?.IsChecked == true;

                        LogDebug($"🔍 MARKER VISIBILITY DEBUG:");
                        LogDebug($"  Quest markers in _currentMapMarkers: {questMarkersVisible}");
                        LogDebug($"  ShowQuestMarkersCheckBox exists: {ShowQuestMarkersCheckBox != null}");
                        LogDebug($"  ShowQuestMarkersCheckBox checked: {questCheckboxChecked}");

                        var imagePosition = new System.Windows.Point(0, 0);
                        var imageSize = new System.Windows.Size(
                            bitmapSource.PixelWidth,
                            bitmapSource.PixelHeight);

                        // ✅ Apply marker filtering before displaying
                        var visibleMarkers = new List<MapMarker>();
                        foreach (var marker in _currentMapMarkers)
                        {
                            bool shouldShow = marker.Type switch
                            {
                                MarkerType.Aetheryte => ShowAetheryteMarkersCheckBox?.IsChecked == true,
                                MarkerType.Quest => ShowQuestMarkersCheckBox?.IsChecked == true,
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
                        LogDebug($"  Quest markers visible: {visibleMarkers.Count(m => m.Type == MarkerType.Quest)}");

                        _mapRenderer?.DisplayMapMarkers(visibleMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                        SyncOverlayWithMap();
                        DebugFateMarkerPositions();

                        _mapRenderer?.AddDebugGridAndBorders();
                        DiagnoseOverlayCanvas();

                        StatusText.Text = $"Map loaded for quest '{territory.PlaceName}' - {_currentMapMarkers.Count} markers found ({questMarkersForThisMap.Count} quest markers, {visibleMarkers.Count} visible).";
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

        // ✅ Add method to toggle quest marker visibility
        public void ToggleQuestMarkers(bool visible)
        {
            try
            {
                var questMarkers = _currentMapMarkers.Where(m => m.Type == MarkerType.Quest).ToList();

                foreach (var marker in questMarkers)
                {
                    marker.IsVisible = visible;
                }

                RefreshMarkers();
                LogDebug($"Quest markers {(visible ? "shown" : "hidden")}: {questMarkers.Count} markers affected");
            }
            catch (Exception ex)
            {
                LogDebug($"Error toggling quest markers: {ex.Message}");
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

        // Add this new method for the cogwheel button click
        private void QuestDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton button && button.Tag is QuestInfo selectedQuest)
            {
                try
                {
                    LogDebug($"Opening quest details for: {selectedQuest.Name} (ID: {selectedQuest.Id})");

                    var questDetailsWindow = new QuestDetailsWindow(selectedQuest, this);

                    // ✅ FIX: Use ShowDialog() instead of Show() to prevent app minimization
                    questDetailsWindow.ShowDialog();  // Modal dialog prevents minimization issues

                    LogDebug($"Quest details window closed for: {selectedQuest.Name}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error opening quest details window: {ex.Message}");
                    WpfMessageBox.Show($"Error opening quest details: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Helpers
        private TerritoryInfo? FindTerritoryByMapId(uint mapId)
        {
            try
            {
                // Search in the filtered territories first (what user can see)
                var territory = _filteredTerritories.FirstOrDefault(t => t.MapId == mapId);
                if (territory != null)
                {
                    return territory;
                }

                // If not found in filtered, search in all territories
                territory = Territories.FirstOrDefault(t => t.MapId == mapId);
                if (territory != null)
                {
                    LogDebug($"Found territory {territory.PlaceName} (ID: {territory.Id}) for MapId {mapId} in full list");
                    return territory;
                }

                LogDebug($"No territory found for MapId: {mapId}");
                return null;
            }
            catch (Exception ex)
            {
                LogDebug($"Error finding territory by MapId {mapId}: {ex.Message}");
                return null;
            }
        }

        private TerritoryInfo? FindTerritoryByPlaceName(string placeName)
        {
            try
            {
                if (string.IsNullOrEmpty(placeName))
                    return null;

                // Search in the filtered territories first (what user can see)
                var territory = _filteredTerritories.FirstOrDefault(t =>
                    string.Equals(t.PlaceName, placeName, StringComparison.OrdinalIgnoreCase));
                if (territory != null)
                {
                    return territory;
                }

                // If not found in filtered, search in all territories
                territory = Territories.FirstOrDefault(t =>
                    string.Equals(t.PlaceName, placeName, StringComparison.OrdinalIgnoreCase));
                if (territory != null)
                {
                    LogDebug($"Found territory {territory.PlaceName} (ID: {territory.Id}) for PlaceName '{placeName}' in full list");
                    return territory;
                }

                LogDebug($"No territory found for PlaceName: '{placeName}'");
                return null;
            }
            catch (Exception ex)
            {
                LogDebug($"Error finding territory by PlaceName '{placeName}': {ex.Message}");
                return null;
            }
        }

        // Add these missing event handlers
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            WpfApplication.Current.Shutdown();
        }

        private void OpenSapphireRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService != null && !string.IsNullOrEmpty(_settingsService.Settings.SapphireServerPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _settingsService.Settings.SapphireServerPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    LogDebug($"Opened Sapphire Server repository: {_settingsService.Settings.SapphireServerPath}");
                }
                else
                {
                    WpfMessageBox.Show("Sapphire Server path not configured. Please set it in Settings.",
                        "Path Not Set", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening Sapphire Server repository: {ex.Message}");
                WpfMessageBox.Show($"Failed to open Sapphire Server repository: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSapphireBuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService != null && !string.IsNullOrEmpty(_settingsService.Settings.SapphireBuildPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _settingsService.Settings.SapphireBuildPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    LogDebug($"Opened Sapphire Server build directory: {_settingsService.Settings.SapphireBuildPath}");
                }
                else
                {
                    WpfMessageBox.Show("Sapphire Server build path not configured. Please set it in Settings.",
                        "Path Not Set", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening Sapphire Server build directory: {ex.Message}");
                WpfMessageBox.Show($"Failed to open Sapphire Server build directory: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be empty or you can add quest selection logic here
            // The main quest functionality is in QuestList_MouseDoubleClick
        }

        private void BNpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be empty or you can add BNpc selection logic here
        }

        private void FateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be empty or you can add fate selection logic here
        }

        private async void ExtractQuestFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if Sapphire build path is configured
                if (_settingsService == null || string.IsNullOrEmpty(_settingsService.Settings.SapphireBuildPath))
                {
                    WpfMessageBox.Show("Sapphire Server build path is not configured.\n\nPlease set it in Settings first.",
                        "Build Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check if FFXIV game path is configured
                if (string.IsNullOrEmpty(_settingsService.Settings.GameInstallationPath))
                {
                    WpfMessageBox.Show("FFXIV game installation path is not configured.\n\nPlease set it in Settings first.",
                        "Game Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_settingsService.IsValidSapphireBuildPath())
                {
                    WpfMessageBox.Show("The configured Sapphire Server build path appears to be invalid.\n\nPlease check your settings.",
                        "Invalid Build Path", MessageBoxButton.OK, MessageBoxImage.Warning); // ✅ Fixed: Warning instead of WARNING
                    return;
                }

                if (!_settingsService.IsValidGamePath())
                {
                    WpfMessageBox.Show("The configured FFXIV game path appears to be invalid.\n\nPlease check your settings.",
                        "Invalid Game Path", MessageBoxButton.OK, MessageBoxImage.Warning); // ✅ Fixed: Warning instead of WARNING
                    return;
                }

                // Look for quest_parse.exe in the tools directory
                string toolsDir = Path.Combine(_settingsService.Settings.SapphireBuildPath, "tools");
                string questParseExe = Path.Combine(toolsDir, "quest_parse.exe");

                if (!Directory.Exists(toolsDir))
                {
                    WpfMessageBox.Show($"Tools directory not found at:\n{toolsDir}\n\nPlease ensure you have a complete Sapphire Server build.",
                        "Tools Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(questParseExe))
                {
                    // Show available tools for debugging
                    var availableFiles = Directory.GetFiles(toolsDir, "*.exe");
                    string availableList = availableFiles.Length > 0
                        ? string.Join("\n", availableFiles.Select(Path.GetFileName))
                        : "No .exe files found";

                    WpfMessageBox.Show($"quest_parse.exe not found in tools directory.\n\n" +
                                     $"Expected location: {questParseExe}\n\n" +
                                     $"Available executables:\n{availableList}\n\n" +
                                     $"Please ensure quest_parse.exe is built and available.",
                        "quest_parse.exe Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Construct the sqpack path by appending \game\sqpack to the game installation path
                string sqpackPath = Path.Combine(_settingsService.Settings.GameInstallationPath, "game", "sqpack");

                if (!Directory.Exists(sqpackPath))
                {
                    WpfMessageBox.Show($"Game sqpack directory not found at:\n{sqpackPath}\n\nPlease ensure your FFXIV installation is complete.",
                        "Sqpack Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LogDebug($"Found quest_parse.exe: {questParseExe}");
                LogDebug($"Using sqpack path: {sqpackPath}");

                // Show confirmation dialog
                var result = WpfMessageBox.Show($"This will run quest_parse.exe with the following parameters:\n\n" +
                                               $"Tool: {Path.GetFileName(questParseExe)}\n" +
                                               $"Sqpack Path: {sqpackPath}\n\n" +
                                               $"This may take some time to complete. Continue?",
                                               "Extract Quest Files", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                // ✅ UPDATED: Show progress overlay with quest-specific messages
                ShowProgressOverlay("quest extraction", "Initializing quest_parse.exe...");

                // Run the tool asynchronously
                await RunQuestParseToolWithProgressAsync(questParseExe, sqpackPath);
            }
            catch (Exception ex)
            {
                LogDebug($"Error running quest_parse.exe: {ex.Message}");
                WpfMessageBox.Show($"Failed to run quest_parse.exe:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                HideProgressOverlay();
            }
        }

        // ✅ NEW: Add method to run LGB Parser in batch mode with progress tracking
        private async Task RunLgbParserBatchModeAsync(string lgbParserExe, string gamePath)
        {
            try
            {
                // Ask user for output directory
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LGB_ParseResults");

                var confirmResult = WpfMessageBox.Show($"This will parse ALL LGB files from the FFXIV client.\n\n" +
                                                     $"Game Path: {gamePath}\n" +
                                                     $"Output Directory: {outputDir}\n\n" +
                                                     $"This may take several minutes and will process thousands of files.\n\n" +
                                                     $"Continue with batch parsing?",
                                                     "Confirm LGB Batch Parse", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    return;
                }

                // Create output directory
                Directory.CreateDirectory(outputDir);

                LogDebug($"Starting LGB Parser batch mode...");
                LogDebug($"Game Path: {gamePath}");
                LogDebug($"Output Directory: {outputDir}");

                // Show progress overlay (reuse existing progress UI)
                ShowProgressOverlay("LGB parsing", "Initializing LGB Parser batch mode...");

                // Prepare arguments for batch command
                var arguments = new[] { "--game", gamePath, "--batch", outputDir };

                // Run the batch parser with progress tracking
                await RunLgbParserProcessWithProgressAsync(lgbParserExe, arguments);
            }
            catch (Exception ex)
            {
                LogDebug($"Error in LGB Parser batch mode: {ex.Message}");
                WpfMessageBox.Show($"Failed to run LGB Parser batch mode:\n\n{ex.Message}",
                    "Batch Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
                HideProgressOverlay();
            }
        }

        // ✅ UPDATED: Enhanced version of RunLgbParserProcessAsync with progress tracking for batch operations
        private async Task RunLgbParserProcessWithProgressAsync(string lgbParserExe, string[] arguments)
        {
            try
            {
                LogDebug($"Starting LGB Parser with progress tracking: {lgbParserExe}");
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

                // Progress tracking variables
                var outputLines = new List<string>();
                var errorLines = new List<string>();
                var startTime = DateTime.Now;
                int processedFiles = 0;
                int totalFiles = 0;
                bool hasEstimatedTotal = false;

                // ✅ Enhanced output processing for batch operations
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LogDebug($"[LGB Parser] {e.Data}");

                            // Track progress from output
                            var line = e.Data.Trim();

                            // Look for file processing indicators
                            if (line.Contains("Found ") && line.Contains(" LGB files"))
                            {
                                // Extract total file count
                                var match = System.Text.RegularExpressions.Regex.Match(line, @"Found (\d+) LGB files");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                                {
                                    totalFiles = count;
                                    hasEstimatedTotal = true;
                                    UpdateProgressStatus($"Found {totalFiles} LGB files to process...");
                                }
                            }
                            else if (line.Contains("✅ Processed:") || line.Contains("✓ Successfully parsed:"))
                            {
                                processedFiles++;
                                var elapsed = DateTime.Now - startTime;

                                if (hasEstimatedTotal && totalFiles > 0)
                                {
                                    var percentage = (double)processedFiles / totalFiles * 100;
                                    var estimatedTimeRemaining = processedFiles > 0
                                        ? TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / processedFiles * (totalFiles - processedFiles))
                                        : TimeSpan.Zero;

                                    UpdateProgressStatus($"Processing LGB files... {processedFiles}/{totalFiles} ({percentage:F1}%) - ETA: {estimatedTimeRemaining:mm\\:ss}");
                                }
                                else
                                {
                                    UpdateProgressStatus($"Processing LGB files... {processedFiles} files completed ({elapsed:mm\\:ss} elapsed)");
                                }
                            }
                            else if (line.Contains("processing complete!"))
                            {
                                UpdateProgressStatus($"LGB parsing complete! Processed {processedFiles} files");
                            }
                            else if (line.Length > 0 && !line.Contains("❌") && !line.Contains("✗"))
                            {
                                // Show current file being processed (truncated for UI)
                                var displayLine = line.Length > 50 ? line.Substring(0, 47) + "..." : line;
                                UpdateProgressStatus($"Processing: {displayLine}");
                            }
                        });
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorLines.Add(e.Data);
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LogDebug($"[LGB Parser Error] {e.Data}");
                            UpdateProgressStatus($"Warning: {e.Data.Substring(0, Math.Min(60, e.Data.Length))}...");
                        });
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for completion
                await Task.Run(() => process.WaitForExit());

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    HideProgressOverlay();

                    var totalTime = DateTime.Now - startTime;

                    if (process.ExitCode == 0)
                    {
                        LogDebug("✅ LGB Parser batch mode completed successfully");
                        StatusText.Text = $"LGB batch parsing completed - {processedFiles} files processed in {totalTime:mm\\:ss}";

                        // Show detailed results
                        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LGB_ParseResults");
                        WpfMessageBox.Show($"LGB Parser batch mode completed successfully!\n\n" +
                                         $"Files processed: {processedFiles}\n" +
                                         $"Total time: {totalTime:mm\\:ss}\n" +
                                         $"Output directory: {outputDir}\n\n" +
                                         $"The parsed LGB files are now available for analysis.",
                            "LGB Batch Parse Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Ask if user wants to open output directory
                        var openResult = WpfMessageBox.Show("Would you like to open the output directory to view the parsed files?",
                            "Open Results", MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (openResult == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = outputDir,
                                    UseShellExecute = true,
                                    Verb = "open"
                                });
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Failed to open output directory: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        LogDebug($"❌ LGB Parser batch mode failed with exit code: {process.ExitCode}");
                        StatusText.Text = $"LGB batch parsing failed (Exit code: {process.ExitCode})";

                        var errorSummary = errorLines.Count > 0
                            ? string.Join("\n", errorLines.Take(5))
                            : "Check debug log for details";

                        WpfMessageBox.Show($"LGB Parser batch mode failed with exit code: {process.ExitCode}\n\n" +
                                         $"Files processed before failure: {processedFiles}\n" +
                                         $"Errors:\n{errorSummary}",
                            "LGB Batch Parse Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"❌ Exception during LGB Parser batch execution: {ex.Message}");
                    HideProgressOverlay();
                    StatusText.Text = "LGB batch parsing failed";
                    WpfMessageBox.Show($"An error occurred during LGB batch parsing:\n\n{ex.Message}",
                        "LGB Batch Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task RunQuestParseToolAsync(string questParseExe, string sqpackPath)
        {
            try
            {
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

                using var process = new System.Diagnostics.Process { StartInfo = processInfo };

                // Capture output for logging
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[quest_parse] {e.Data}"));
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[quest_parse Error] {e.Data}"));
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
                        LogDebug("quest_parse.exe completed successfully");
                        StatusText.Text = "Quest file extraction completed successfully";
                        WpfMessageBox.Show("Quest file extraction completed successfully!\n\nquest_parse.exe finished processing the game data.",
                            "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogDebug($"quest_parse.exe failed with exit code: {process.ExitCode}");
                        StatusText.Text = $"Quest extraction failed (Exit code: {process.ExitCode})";
                        WpfMessageBox.Show($"quest_parse.exe failed with exit code: {process.ExitCode}\n\nCheck the debug log for details.",
                            "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"❌ Exception during quest_parse.exe execution: {ex.Message}");
                    HideProgressOverlay();
                    StatusText.Text = "Quest extraction failed";
                    WpfMessageBox.Show($"An error occurred during quest extraction:\n\n{ex.Message}",
                        "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task RunQuestExtractionToolAsync(string toolPath)
        {
            try
            {
                LogDebug($"Starting quest extraction tool: {toolPath}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = toolPath,
                    WorkingDirectory = Path.GetDirectoryName(toolPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = processInfo };

                // Capture output for logging
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[Quest Tool] {e.Data}"));
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[Quest Tool Error] {e.Data}"));
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
                        LogDebug("Quest extraction completed successfully");
                        StatusText.Text = "Quest file extraction completed successfully";
                        WpfMessageBox.Show("Quest file extraction completed successfully!",
                            "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogDebug($"Quest extraction failed with exit code: {process.ExitCode}");
                        StatusText.Text = $"Quest extraction failed (Exit code: {process.ExitCode})";
                        WpfMessageBox.Show($"Quest extraction failed with exit code: {process.ExitCode}\n\nCheck the debug log for details.",
                            "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"Exception during quest extraction: {ex.Message}");
                    StatusText.Text = "Quest extraction failed";
                    WpfMessageBox.Show($"An error occurred during quest extraction:\n\n{ex.Message}",
                        "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }


        // ✅ ADD: NPC filtering and UI methods
        private void FilterNpcs()
        {
            try
            {
                var searchText = "";
                Dispatcher.Invoke(() =>
                {
                    if (NpcSearchBox != null)  // ✅ RENAMED: EventSearchBox -> NpcSearchBox
                        searchText = NpcSearchBox.Text?.Trim() ?? "";
                });

                List<NpcInfo> baseList;

                // ✅ FIX: Always start with all NPCs when filtering, not the previously filtered list
                if (string.IsNullOrEmpty(searchText))
                {
                    // ✅ FIXED: When search is empty, show all NPCs for current territory OR all NPCs if no territory selected
                    if (_currentTerritory != null)
                    {
                        baseList = _allNpcs.Where(n => n.MapId == _currentTerritory.MapId).ToList();
                    }
                    else
                    {
                        baseList = new List<NpcInfo>(_allNpcs);
                    }
                }
                else
                {
                    // ✅ FIXED: When searching, start from all NPCs and apply search filter
                    baseList = _allNpcs.Where(n =>
                        n.NpcName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        n.NpcId.ToString().Contains(searchText) ||
                        n.TerritoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    ).ToList();

                    // ✅ FIXED: Apply territory filter AFTER search filter if territory is selected
                    if (_currentTerritory != null)
                    {
                        baseList = baseList.Where(n => n.MapId == _currentTerritory.MapId).ToList();
                    }
                }

                _filteredNpcs = baseList;

                Dispatcher.Invoke(() =>
                {
                    if (NpcList != null)  // ✅ RENAMED: EventList -> NpcList
                    {
                        NpcList.ItemsSource = null; // ✅ Clear first to force refresh
                        NpcList.ItemsSource = _filteredNpcs;
                        NpcCountText.Text = $"({_filteredNpcs.Count})";  // ✅ RENAMED: EventCountText -> NpcCountText
                    }
                });

                LogDebug($"🧙 NPC filtering complete: {_filteredNpcs.Count} NPCs shown (search: '{searchText}', territory: {_currentTerritory?.PlaceName ?? "None"})");
            }
            catch (Exception ex)
            {
                LogDebug($"❌ Error filtering NPCs: {ex.Message}");
            }
        }

        // ✅ ADD: Handle double-click on NPC to show quest details
        private void NpcList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)  // ✅ RENAMED: EventList_MouseDoubleClick -> NpcList_MouseDoubleClick
        {
            if (NpcList.SelectedItem is NpcInfo selectedNpc)  // ✅ RENAMED: EventList -> NpcList
            {
                try
                {
                    var npcDetailsWindow = new NpcDetailsWindow(selectedNpc, this);

                    // ✅ FIX: Use ShowDialog() instead of Show() to prevent app minimization
                    npcDetailsWindow.ShowDialog();  // Modal dialog prevents minimization issues
                }
                catch (Exception ex)
                {
                    LogDebug($"❌ Error opening NPC details window: {ex.Message}");
                    WpfMessageBox.Show($"Error opening NPC details: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ShowQuestMarkersCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ToggleQuestMarkers(true);
        }

        private void ShowQuestMarkersCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleQuestMarkers(false);
        }

        private void NpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be empty or you can add BNpc selection logic here
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