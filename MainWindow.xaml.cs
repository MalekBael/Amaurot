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
using WpfColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfApplication = System.Windows.Application;
using WpfImage = System.Windows.Controls.Image;
using WpfButton = System.Windows.Controls.Button;

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
        private List<MapMarker> _currentMapMarkers = new();
        private double _currentScale = 1.0;
        private System.Windows.Point _lastMousePosition;
        private bool _isDragging = false;
        private bool _hideDuplicateTerritories = false;

        public ObservableCollection<TerritoryInfo> Territories { get; set; } = new ObservableCollection<TerritoryInfo>();
        public ObservableCollection<QuestInfo> Quests { get; set; } = new ObservableCollection<QuestInfo>();
        public ObservableCollection<BNpcInfo> BNpcs { get; set; } = new ObservableCollection<BNpcInfo>();
        public ObservableCollection<EventInfo> Events { get; set; } = new ObservableCollection<EventInfo>();
        public ObservableCollection<FateInfo> Fates { get; set; } = new ObservableCollection<FateInfo>();

        private ObservableCollection<QuestInfo> _filteredQuests = new ObservableCollection<QuestInfo>();
        private ObservableCollection<BNpcInfo> _filteredBNpcs = new ObservableCollection<BNpcInfo>();
        private ObservableCollection<TerritoryInfo> _filteredTerritories = new ObservableCollection<TerritoryInfo>();
        private ObservableCollection<EventInfo> _filteredEvents = new ObservableCollection<EventInfo>();
        private ObservableCollection<FateInfo> _filteredFates = new ObservableCollection<FateInfo>();

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

        private void InitializeServices()
        {
            _debugHelper = new DebugHelper(this);
            _uiUpdateService = new UIUpdateService();
            _mapInteractionService = new MapInteractionService(LogDebug);
            _searchFilterService = new SearchFilterService(LogDebug);
            _mapRenderer = new MapRenderer(null); // Will update realm later
                                                  // _dataLoaderService will be initialized when _realm is available

            // Subscribe to debug mode changes to refresh the quest list UI
            DebugModeManager.DebugModeChanged += OnDebugModeChanged;

            RedirectDebugOutput();
            LogDebug("Application started");
        }

        private void OnDebugModeChanged()
        {
            // Refresh the quest list UI to update pin visibility
            WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                // Trigger a refresh of the quest list bindings
                if (QuestList?.ItemsSource != null)
                {
                    var itemsSource = QuestList.ItemsSource;
                    QuestList.ItemsSource = null;
                    QuestList.ItemsSource = itemsSource;
                }
            });
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
        }

        private void ApplySavedSettings()
        {
            if (_settingsService == null) return;

            var settings = _settingsService.Settings;

            if (DebugModeCheckBox != null)
            {
                DebugModeCheckBox.IsChecked = settings.DebugMode;

                // Initialize the debug mode manager
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

            // Create a filtered list based on checkbox states
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

        // ✅ ADD these new methods for progress bar functionality

        private void ShowProgressOverlay()
        {
            ProgressOverlay.Visibility = Visibility.Visible;
            ProgressStatusText.Text = "Initializing quest_parse.exe...";
            QuestExtractionProgressBar.IsIndeterminate = true;
            CancelExtractionButton.IsEnabled = true;

            // Create cancellation token
            _extractionCancellationSource = new System.Threading.CancellationTokenSource();

            LogDebug("Progress overlay shown");
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
                LogDebug("User requested cancellation of quest extraction");

                // Kill the process if it's running
                if (_questExtractionProcess != null && !_questExtractionProcess.HasExited)
                {
                    _questExtractionProcess.Kill();
                    LogDebug("quest_parse.exe process terminated");
                }

                // Cancel the operation
                _extractionCancellationSource?.Cancel();

                HideProgressOverlay();
                StatusText.Text = "Quest extraction cancelled by user";

                WpfMessageBox.Show("Quest extraction has been cancelled.", "Extraction Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogDebug($"Error cancelling extraction: {ex.Message}");
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

                // Capture output for logging and progress updates
                _questExtractionProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LogDebug($"[quest_parse] {e.Data}");

                            // Update progress based on output keywords
                            if (e.Data.Contains("Loading"))
                            {
                                UpdateProgressStatus("Loading game data files...");
                            }
                            else if (e.Data.Contains("Processing"))
                            {
                                UpdateProgressStatus("Processing quest data...");
                            }
                            else if (e.Data.Contains("Writing") || e.Data.Contains("Saving"))
                            {
                                UpdateProgressStatus("Saving extracted files...");
                            }
                            else if (e.Data.Contains("Complete") || e.Data.Contains("Done"))
                            {
                                UpdateProgressStatus("Finalizing extraction...");
                            }
                        });
                    }
                };

                _questExtractionProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                            LogDebug($"[quest_parse Error] {e.Data}"));
                    }
                };

                _questExtractionProcess.Start();
                _questExtractionProcess.BeginOutputReadLine();
                _questExtractionProcess.BeginErrorReadLine();

                UpdateProgressStatus("Quest extraction in progress...");

                // Wait for completion with cancellation support
                await Task.Run(() =>
                {
                    while (!_questExtractionProcess.HasExited)
                    {
                        if (_extractionCancellationSource?.Token.IsCancellationRequested == true)
                        {
                            return; // Exit if cancelled
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                }, _extractionCancellationSource?.Token ?? System.Threading.CancellationToken.None);

                // Check if we were cancelled
                if (_extractionCancellationSource?.Token.IsCancellationRequested == true)
                {
                    return;
                }

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    HideProgressOverlay();

                    if (_questExtractionProcess.ExitCode == 0)
                    {
                        LogDebug("quest_parse.exe completed successfully");
                        StatusText.Text = "Quest file extraction completed successfully";
                        WpfMessageBox.Show("Quest file extraction completed successfully!\n\nquest_parse.exe finished processing the game data.",
                            "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogDebug($"quest_parse.exe failed with exit code: {_questExtractionProcess.ExitCode}");
                        StatusText.Text = $"Quest extraction failed (Exit code: {_questExtractionProcess.ExitCode})";
                        WpfMessageBox.Show($"quest_parse.exe failed with exit code: {_questExtractionProcess.ExitCode}\n\nCheck the debug log for details.",
                            "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (System.OperationCanceledException)
            {
                // Operation was cancelled
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    HideProgressOverlay();
                    StatusText.Text = "Quest extraction cancelled";
                });
            }
            catch (Exception ex)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    LogDebug($"Exception during quest_parse.exe execution: {ex.Message}");
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
                    System.Windows.MessageBox.Show("Invalid game folder selected.",
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

                _mapService = new MapService(_realm, LogDebug); 
                LogDebug("Initialized map service");

                if (_realm != null)
                {
                    _mapRenderer?.UpdateRealm(_realm);
                }

                _dataLoaderService = new DataLoaderService(_realm, LogDebug);
                LogDebug("Initialized data loader service");

                LogDebug("Starting to load all data...");

                var loadingTasks = new List<Task>
                {
                    LoadDataAsync(_dataLoaderService.LoadTerritoriesAsync, Territories, _filteredTerritories, _uiUpdateService!.UpdateTerritoryCount, TerritoryCountText),
                    LoadDataAsync(_dataLoaderService.LoadQuestsAsync, Quests, _filteredQuests, _uiUpdateService!.UpdateQuestCount, QuestCountText),
                    LoadDataAsync(_dataLoaderService.LoadBNpcsAsync, BNpcs, _filteredBNpcs, _uiUpdateService!.UpdateBNpcCount, BNpcCountText),
                    LoadDataAsync(_dataLoaderService.LoadEventsAsync, Events, _filteredEvents, _uiUpdateService!.UpdateEventCount, EventCountText),
                    LoadDataAsync(_dataLoaderService.LoadFatesAsync, Fates, _filteredFates, _uiUpdateService!.UpdateFateCount, FateCountText)
                };

                var loadingTask = Task.WhenAll(loadingTasks);
                while (!loadingTask.IsCompleted)
                {
                    await Task.Delay(500);
                    StatusText.Text = $"Loading FFXIV data... Territories: {Territories.Count}, Quests: {Quests.Count}, NPCs: {BNpcs.Count}, Events: {Events.Count}, Fates: {Fates.Count}";
                }

                await loadingTask;
                LogDebug($"Loading complete - Territories: {Territories.Count}, Quests: {Quests.Count}, NPCs: {BNpcs.Count}, Events: {Events.Count}, Fates: {Fates.Count}");

                if (Territories.Count > 0)
                {
                    LogDebug("Applying initial territory filters...");
                    ApplyTerritoryFilters();
                }

                // After loading basic data, parse LGB files for quest locations
                StatusText.Text = "Parsing quest locations from LGB files...";
                await _dataLoaderService.LoadQuestLocationsFromLgbAsync(Quests.ToList());

                // Also set debug mode if enabled
                if (_settingsService?.Settings.DebugMode == true)
                {
                    _dataLoaderService.SetVerboseDebugMode(true);
                }

                StatusText.Text = $"Loaded {Territories.Count} territories, {Quests.Count} quests, {BNpcs.Count} NPCs, {Events.Count} events, {Fates.Count} fates from: {gameDirectory}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading data: {ex.Message}";
                LogDebug($"Error loading FFXIV data: {ex.Message}\n{ex.StackTrace}");
                WpfMessageBox.Show($"Failed to load FFXIV data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

            var bitmapSource = MapImageControl.Source as System.Windows.Media.Imaging.BitmapSource;
            if (bitmapSource == null) return;

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
            string searchText = BNpcSearchBox.Text?.ToLower() ?? "";

            _filteredBNpcs.Clear();

            foreach (var bnpc in BNpcs)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              bnpc.BNpcName.ToLower().Contains(searchText) ||
                              bnpc.BNpcBaseId.ToString().Contains(searchText) ||
                              bnpc.TribeName.ToLower().Contains(searchText);

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

            string searchText = TerritorySearchBox?.Text?.ToLower() ?? "";

            Task.Run(() =>
            {
                var filteredTerritories = Territories.AsEnumerable();

                if (!string.IsNullOrEmpty(searchText))
                {
                    filteredTerritories = filteredTerritories.Where(territory =>
                        territory.PlaceName.ToLower().Contains(searchText) ||
                        territory.Id.ToString().Contains(searchText) ||
                        territory.Region.ToLower().Contains(searchText) ||
                        territory.TerritoryNameId.ToLower().Contains(searchText));
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

                        if (!seenPlaceNames.Contains(placeName))
                        {
                            seenPlaceNames.Add(placeName);
                            territoriesToKeep.Add(territory);
                        }
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

        private void EventSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = EventSearchBox.Text?.ToLower() ?? "";

            _filteredEvents.Clear();

            foreach (var evt in Events)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              evt.Name.ToLower().Contains(searchText) ||
                              evt.EventId.ToString().Contains(searchText);

                if (matches)
                {
                    _filteredEvents.Add(evt);
                }
            }

            _uiUpdateService?.UpdateEventCount(_filteredEvents, EventCountText);
        }

        private void FateSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = FateSearchBox.Text?.ToLower() ?? "";

            _filteredFates.Clear();

            foreach (var fate in Fates)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              fate.Name.ToLower().Contains(searchText) ||
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

                var transformGroup = MapImageControl.RenderTransform as TransformGroup;
                if (transformGroup != null)
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

                        List<MapMarker> markers = await Task.Run(() =>
                            _mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>()
                        );

                        _currentMapMarkers = markers;

                        var imagePosition = new System.Windows.Point(0, 0);
                        var imageSize = new System.Windows.Size(
                            bitmapSource.PixelWidth,
                            bitmapSource.PixelHeight);

                        _mapRenderer?.DisplayMapMarkers(_currentMapMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                        SyncOverlayWithMap();
                        DebugFateMarkerPositions();

                        _mapRenderer?.AddDebugGridAndBorders();
                        DiagnoseOverlayCanvas();

                        StatusText.Text = $"Map loaded for {territory.PlaceName} - {_currentMapMarkers.Count} markers found.";
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
            if (sender is WpfButton button && button.Tag is QuestInfo selectedQuest) // Use WpfButton instead of Button
            {
                try
                {
                    var questDetailsWindow = new QuestDetailsWindow(selectedQuest)
                    {
                        Owner = this
                    };
                    
                    LogDebug($"Opening quest details for: {selectedQuest.Name} (ID: {selectedQuest.Id})");
                    questDetailsWindow.Show();
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

        private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be empty or you can add event selection logic here
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
                        "Invalid Build Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_settingsService.IsValidGamePath())
                {
                    WpfMessageBox.Show("The configured FFXIV game path appears to be invalid.\n\nPlease check your settings.",
                        "Invalid Game Path", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                                               "This may take some time to complete. Continue?",
                                               "Extract Quest Files", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                // ✅ Show progress overlay
                ShowProgressOverlay();

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

        private async Task RunQuestParseToolAsync(string questParseExe, string sqpackPath)
        {
            try
            {
                LogDebug($"Starting quest_parse.exe: {questParseExe}");
                LogDebug($"Arguments: \"{sqpackPath}\"");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = questParseExe,
                    Arguments = $"\"{sqpackPath}\"", // Pass the sqpack path as argument
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
                    LogDebug($"Exception during quest_parse.exe execution: {ex.Message}");
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