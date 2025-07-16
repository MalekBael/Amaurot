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

namespace map_editor
{
    public partial class MainWindow : Window
    {
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

        private bool _debugLoggingEnabled = false;

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
        private const int MaxLogLines = 500;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            QuestList.ItemsSource = _filteredQuests;
            BNpcList.ItemsSource = _filteredBNpcs;
            TerritoryList.ItemsSource = _filteredTerritories;
            EventList.ItemsSource = _filteredEvents; 
            FateList.ItemsSource = _filteredFates;   

            _mapRenderer = new MapRenderer(_realm);

            RedirectDebugOutput();
            LogDebug("Application started");

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
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

        private async void LoadGameData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select FFXIV game installation folder",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                var gamePath = dialog.SelectedPath;
                if (Directory.Exists(gamePath))
                {
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

                _mapService = new MapService(_realm);
                LogDebug("Initialized map service");

                if (_realm != null)
                {
                    _mapRenderer?.UpdateRealm(_realm);
                }

                var loadingTasks = new List<Task>
                {
                    Task.Run(() =>
                    {
                        LogDebug("Loading territories...");
                        LoadTerritories();
                    }),
                    Task.Run(() =>
                    {
                        LogDebug("Loading quests...");
                        LoadQuests();
                    }),
                    Task.Run(() =>
                    {
                        LogDebug("Loading BNpcs...");
                        LoadBNpcs();
                    }),
                    Task.Run(() =>
                    {
                        LogDebug("Loading events...");
                        LoadEvents();
                    }),
                    Task.Run(() =>
                    {
                        LogDebug("Loading fates...");
                        LoadFates();
                    })
                };

                var loadingTask = Task.WhenAll(loadingTasks);
                while (!loadingTask.IsCompleted)
                {
                    await Task.Delay(500);
                    StatusText.Text = $"Loading FFXIV data... Territories: {Territories.Count}, Quests: {Quests.Count}, NPCs: {BNpcs.Count}";
                }

                await loadingTask;

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
                if (_debugLoggingEnabled)
                {
                    LogDebug($"Hide duplicates checkbox changed to: {_hideDuplicateTerritories}");
                }
                ApplyTerritoryFilters();
            }
        }

        private void UpdateQuestCount()
        {
            if (QuestCountText != null)
            {
                QuestCountText.Text = $"({_filteredQuests.Count})";
            }
        }

        private void UpdateBNpcCount()
        {
            if (BNpcCountText != null)
            {
                BNpcCountText.Text = $"({_filteredBNpcs.Count})";
            }
        }

        private void UpdateTerritoryCount()
        {
            if (TerritoryCountText != null)
            {
                TerritoryCountText.Text = $"({_filteredTerritories.Count})";
            }
        }

        private void UpdateEventCount()
        {
            if (EventCountText != null)
            {
                EventCountText.Text = $"({_filteredEvents.Count})";
            }
        }

        private void UpdateFateCount()
        {
            if (FateCountText != null)
            {
                FateCountText.Text = $"({_filteredFates.Count})";
            }
        }

        public ObservableCollection<TerritoryInfo> FilteredTerritories => _filteredTerritories;

        private void LoadFFXIVData(string gameDirectory)
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

                _realm = new ARealmReversed(gameDirectory, SaintCoinach.Ex.Language.English);
                LogDebug($"Initialized realm: {(_realm != null ? "Success" : "Failed")}");

                _mapService = new MapService(_realm);
                LogDebug("Initialized map service");

                if (_realm != null)
                {
                    _mapRenderer?.UpdateRealm(_realm);
                }

                LogDebug("Loading territories...");
                LoadTerritories();

                LogDebug("Loading quests...");
                LoadQuests();

                LogDebug("Loading BNpcs...");
                LoadBNpcs();

                LogDebug("Loading events...");
                LoadEvents(); 

                LogDebug("Loading fates...");
                LoadFates();

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

        private void LoadQuests()
        {
            var tempQuests = new List<QuestInfo>();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading quests from SaintCoinach...");
                    var questSheet = _realm.GameData.GetSheet<Quest>();

                    int processedCount = 0;
                    int totalCount = questSheet.Count();

                    foreach (var quest in questSheet)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(quest.Name)) continue;

                            var questInfo = new QuestInfo
                            {
                                Id = (uint)quest.Key,
                                Name = quest.Name?.ToString() ?? "",
                                JournalGenre = quest.JournalGenre?.Name?.ToString() ?? "",
                                ClassJobCategoryId = 0,
                                ClassJobLevelRequired = 0,
                                ClassJobCategoryName = "",
                                IsMainScenarioQuest = quest.JournalGenre?.Name?.ToString().Contains("Main Scenario") == true,
                                IsFeatureQuest = quest.JournalGenre?.Name?.ToString().Contains("Feature") == true,
                                PreviousQuestId = 0,
                                ExpReward = 0,
                                GilReward = 0
                            };

                            try
                            {
                                var classJobLevel = quest.AsInt32("ClassJobLevelRequired");
                                questInfo.ClassJobLevelRequired = (uint)Math.Max(0, classJobLevel);
                            }
                            catch { }

                            try
                            {
                                var expReward = quest.AsInt32("ExpReward");
                                questInfo.ExpReward = (uint)Math.Max(0, expReward);
                            }
                            catch { }

                            try
                            {
                                var gilReward = quest.AsInt32("GilReward");
                                questInfo.GilReward = (uint)Math.Max(0, gilReward);
                            }
                            catch { }

                            tempQuests.Add(questInfo);

                            processedCount++;
                            if (processedCount % 500 == 0)
                            {
                                LogDebug($"Processed {processedCount}/{totalCount} quests...");
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                    tempQuests.Sort((a, b) => a.Id.CompareTo(b.Id));

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        Quests.Clear();
                        _filteredQuests.Clear();

                        foreach (var quest in tempQuests)
                        {
                            Quests.Add(quest);
                            _filteredQuests.Add(quest);
                        }

                        LogDebug($"Loaded {Quests.Count} quests");
                        UpdateQuestCount();
                    });
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading quests: {ex.Message}");
            }
        }

        private void QuestSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = QuestSearchBox.Text?.ToLower() ?? "";

            _filteredQuests.Clear();

            foreach (var quest in Quests)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              quest.Name.ToLower().Contains(searchText) ||
                              quest.Id.ToString().Contains(searchText) ||
                              quest.JournalGenre.ToLower().Contains(searchText);

                if (matches)
                {
                    _filteredQuests.Add(quest);
                }
            }

            UpdateQuestCount();
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

            UpdateBNpcCount();
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
            if (_debugLoggingEnabled)
            {
                LogDebug($"=== ApplyTerritoryFilters START ===");
                LogDebug($"Hide duplicates: {_hideDuplicateTerritories}");
                LogDebug($"Search text: '{TerritorySearchBox?.Text}'");
            }

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

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    _filteredTerritories.Clear();
                    foreach (var territory in finalResults)
                    {
                        _filteredTerritories.Add(territory);
                    }
                    UpdateTerritoryCount();
                });

                if (_debugLoggingEnabled)
                {
                    LogDebug($"Filtered territories count: {finalResults.Count}");
                    LogDebug($"=== ApplyTerritoryFilters END ===");
                }
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

            UpdateEventCount();
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

            UpdateFateCount();
        }

        private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuestList.SelectedItem is QuestInfo selectedQuest)
            {
                LogDebug($"Selected quest: {selectedQuest.Name} (ID: {selectedQuest.Id})");
                // We can add more functionality here later, such as showing quest details
                // or highlighting quest-related markers on the map
            }
        }

        private void BNpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BNpcList.SelectedItem is BNpcInfo selectedBNpc)
            {
                LogDebug($"Selected BNpc: {selectedBNpc.BNpcName} (Base ID: {selectedBNpc.BNpcBaseId})");
                // We can add more functionality here later, such as showing NPC details
                // or highlighting NPC-related markers on the map
            }
        }

        private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EventList.SelectedItem is EventInfo selectedEvent)
            {
                LogDebug($"Selected Event: {selectedEvent.Name} (ID: {selectedEvent.EventId})");
                // We can add more functionality here later, such as showing event details
                // or highlighting event-related markers on the map
            }
        }

        private void FateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FateList.SelectedItem is FateInfo selectedFate)
            {
                LogDebug($"Selected Fate: {selectedFate.Name} (ID: {selectedFate.FateId})");
                // We can add more functionality here later, such as showing fate details
                // or highlighting fate-related markers on the map
            }
        }

        private void LoadBNpcs()
        {
            var tempBNpcs = new List<BNpcInfo>();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading BNpcs from SaintCoinach...");

                    SaintCoinach.Xiv.IXivSheet<SaintCoinach.Xiv.BNpcName>? bnpcNameSheet = null;

                    try
                    {
                        bnpcNameSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.BNpcName>();
                        if (bnpcNameSheet != null)
                        {
                            LogDebug($"Found BNpcName sheet with {bnpcNameSheet.Count} entries");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Failed to load BNpcName sheet: {ex.Message}");
                        return;
                    }

                    if (bnpcNameSheet == null)
                    {
                        LogDebug("ERROR: BNpcName sheet is null!");
                        return;
                    }

                    int processedCount = 0;
                    int skippedCount = 0;
                    int totalCount = bnpcNameSheet.Count;
                    LogDebug($"Processing {totalCount} BNpcName entries...");

                    foreach (var bnpcName in bnpcNameSheet)
                    {
                        try
                        {
                            string name = bnpcName.Singular ?? "";

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                skippedCount++;
                                continue;
                            }

                            uint nameId = (uint)bnpcName.Key;

                            var bnpcInfo = new BNpcInfo
                            {
                                BNpcNameId = nameId,
                                BNpcName = name,
                                BNpcBaseId = 0,
                                Title = "",
                                TribeId = 0,
                                TribeName = ""
                            };

                            tempBNpcs.Add(bnpcInfo);
                            processedCount++;
                            if (processedCount % 1000 == 0)
                            {
                                LogDebug($"Processed {processedCount}/{totalCount} BNpc names (skipped {skippedCount} empty)...");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_debugLoggingEnabled)
                            {
                                LogDebug($"Error processing BNpcName {bnpcName.Key}: {ex.Message}");
                            }
                        }
                    }

                    LogDebug($"Finished processing BNpcs: {processedCount} processed, {skippedCount} skipped");

                    tempBNpcs.Sort((a, b) => string.Compare(a.BNpcName, b.BNpcName, StringComparison.OrdinalIgnoreCase));
                    LogDebug($"Sorted {tempBNpcs.Count} BNpcs");

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        BNpcs.Clear();
                        _filteredBNpcs.Clear();

                        foreach (var bnpc in tempBNpcs)
                        {
                            BNpcs.Add(bnpc);
                            _filteredBNpcs.Add(bnpc);
                        }

                        LogDebug($"Loaded {BNpcs.Count} BNpcs to UI");
                        UpdateBNpcCount();
                    });
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading BNpcs: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void LoadEvents()
        {
            var tempEvents = new List<EventInfo>();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading events from SaintCoinach...");
                    var eventSheet = _realm.GameData.GetSheet("Event");

                    if (eventSheet != null)
                    {
                        foreach (var eventRow in eventSheet)
                        {
                            try
                            {
                                string eventName = eventRow.AsString("Name") ?? "";
                                if (string.IsNullOrWhiteSpace(eventName)) continue;

                                var eventInfo = new EventInfo
                                {
                                    Id = (uint)eventRow.Key,
                                    EventId = (uint)eventRow.Key,
                                    Name = eventName,
                                    EventType = "Event", // Default type
                                    Description = eventRow.AsString("Description") ?? "",
                                    TerritoryId = 0, // Events may not have territory association
                                    TerritoryName = ""
                                };

                                tempEvents.Add(eventInfo);
                            }
                            catch (Exception ex)
                            {
                                if (_debugLoggingEnabled)
                                {
                                    LogDebug($"Failed to process event with key {eventRow.Key}. Error: {ex.Message}");
                                }
                            }
                        }
                    }

                    tempEvents.Sort((a, b) => a.EventId.CompareTo(b.EventId));

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        Events.Clear();
                        _filteredEvents.Clear();

                        foreach (var evt in tempEvents)
                        {
                            Events.Add(evt);
                            _filteredEvents.Add(evt);
                        }

                        LogDebug($"Loaded {Events.Count} events");
                        UpdateEventCount();
                    });
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading events: {ex.Message}");
            }
        }

        private void LoadFates()
        {
            var tempFates = new List<FateInfo>();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading fates from SaintCoinach...");
                    var fateSheet = _realm.GameData.GetSheet("Fate");

                    if (fateSheet != null)
                    {
                        foreach (var fateRow in fateSheet)
                        {
                            try
                            {
                                string fateName = fateRow.AsString("Name") ?? "";
                                if (string.IsNullOrWhiteSpace(fateName)) continue;

                                var classJobLevel = 0;
                                try
                                {
                                    classJobLevel = fateRow.AsInt32("ClassJobLevel");
                                }
                                catch { }

                                var iconId = 0;
                                try
                                {
                                    iconId = fateRow.AsInt32("Icon");
                                }
                                catch { }

                                var fateInfo = new FateInfo
                                {
                                    Id = (uint)fateRow.Key,
                                    FateId = (uint)fateRow.Key,
                                    Name = fateName,
                                    Description = fateRow.AsString("Description") ?? "",
                                    Level = (uint)Math.Max(0, classJobLevel),
                                    ClassJobLevel = (uint)Math.Max(0, classJobLevel),
                                    TerritoryId = 0, // Will need to determine from location data
                                    TerritoryName = "",
                                    IconId = (uint)Math.Max(0, iconId)
                                };

                                tempFates.Add(fateInfo);
                            }
                            catch (Exception ex)
                            {
                                if (_debugLoggingEnabled)
                                {
                                    LogDebug($"Failed to process fate with key {fateRow.Key}. Error: {ex.Message}");
                                }
                            }
                        }
                    }

                    tempFates.Sort((a, b) => a.FateId.CompareTo(b.FateId));

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        Fates.Clear();
                        _filteredFates.Clear();

                        foreach (var fate in tempFates)
                        {
                            Fates.Add(fate);
                            _filteredFates.Add(fate);
                        }

                        LogDebug($"Loaded {Fates.Count} fates");
                        UpdateFateCount();
                    });
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading fates: {ex.Message}");
            }
        }

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MapImageControl.Source == null) return;

            var mousePos = e.GetPosition(MapCanvas);

            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            double newScale = _currentScale * zoomFactor;
            newScale = Math.Clamp(newScale, 0.1, 2.0);

            var transformGroup = MapImageControl.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform());
                transformGroup.Children.Add(new TranslateTransform());
                MapImageControl.RenderTransform = transformGroup;
            }

            var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
            var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();

            if (scaleTransform != null && translateTransform != null)
            {
                double scaleDelta = newScale / _currentScale;
                translateTransform.X = mousePos.X - (mousePos.X - translateTransform.X) * scaleDelta;
                translateTransform.Y = mousePos.Y - (mousePos.Y - translateTransform.Y) * scaleDelta;
                scaleTransform.ScaleX = newScale;
                scaleTransform.ScaleY = newScale;
            }

            _currentScale = newScale;
            StatusText.Text = $"Zoom: {_currentScale:P0}";
            SyncOverlayWithMap();
            e.Handled = true;

            if (_currentMap != null && _currentMapMarkers != null)
            {
                RefreshMarkers();
            }
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
            else if (!_isDragging)
            {
                var mousePosition = e.GetPosition(MapCanvas);
                _mapRenderer?.HandleMouseMove(mousePosition, _currentMap);

                if (_debugLoggingEnabled && DateTime.Now.Millisecond < 50) // ~5% of the time
                {
                    var transformGroup = MapImageControl.RenderTransform as TransformGroup;
                    var translateTransform = transformGroup?.Children.OfType<TranslateTransform>().FirstOrDefault();
                    var imagePosition = new System.Windows.Point(
                        translateTransform?.X ?? 0,
                        translateTransform?.Y ?? 0
                    );

                    if (MapImageControl.Source is BitmapSource bitmapSource)
                    {
                        _mapService?.LogCursorPosition(
                            mousePosition,
                            imagePosition,
                            _currentScale,
                            new System.Windows.Size(bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                            _currentMap);
                    }
                }
            }
        }

        private void LoadTerritories()
        {
            LogDebug("Starting to load territories...");

            var tempTerritories = new List<TerritoryInfo>();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading territories from SaintCoinach...");
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                    int processedCount = 0;
                    int totalCount = territorySheet.Count();
                    LogDebug($"Found {totalCount} territories in game data");

                    foreach (var territory in territorySheet)
                    {
                        try
                        {
                            string placeName;
                            uint placeNameId = 0;

                            if (territory.PlaceName != null && !string.IsNullOrWhiteSpace(territory.PlaceName.Name))
                            {
                                placeName = territory.PlaceName.Name.ToString();
                                placeNameId = (uint)territory.PlaceName.Key;
                            }
                            else
                            {
                                placeName = $"[Territory ID: {territory.Key}]";
                            }

                            string territoryNameId = territory.Name?.ToString() ?? string.Empty;
                            string regionName = "Unknown";
                            uint regionId = 0;
                            bool regionFound = false;

                            try
                            {
                                if (territory.RegionPlaceName != null && territory.RegionPlaceName.Key != 0)
                                {
                                    string? name = territory.RegionPlaceName.Name?.ToString();
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        regionName = name;
                                        regionId = (uint)territory.RegionPlaceName.Key;
                                        regionFound = true;
                                    }
                                }
                            }
                            catch (KeyNotFoundException) {}


                            if (!regionFound)
                            {
                                try
                                {
                                    if (territory.ZonePlaceName != null && territory.ZonePlaceName.Key != 0)
                                    {
                                        string? name = territory.ZonePlaceName.Name?.ToString();
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            regionName = name;
                                            regionId = (uint)territory.ZonePlaceName.Key;
                                        }
                                    }
                                }
                                catch (KeyNotFoundException)
                                {
                                }
                            }


                            uint mapId = 0;
                            try
                            {
                                if (territory.Map != null)
                                {
                                    mapId = (uint)territory.Map.Key;
                                }
                            }
                            catch (KeyNotFoundException)
                            {
                                LogDebug($"Territory {territory.Key} ('{placeName}') has no Map. Defaulting to 0.");
                            }

                            var territoryInfo = new TerritoryInfo
                            {
                                Id = (uint)territory.Key,
                                Name = placeName,
                                TerritoryNameId = territoryNameId,
                                PlaceNameId = placeNameId,
                                PlaceName = placeName,
                                RegionId = regionId,
                                RegionName = regionName,
                                Region = regionName,
                                MapId = mapId,
                            };

                            tempTerritories.Add(territoryInfo);

                            processedCount++;
                            if (processedCount % 100 == 0)
                            {
                                LogDebug($"Processed {processedCount}/{totalCount} territories...");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to process territory with key {territory.Key}. Error: {ex.Message}");
                        }
                    }

                    LogDebug($"Finished processing {tempTerritories.Count} territories");
                }

                if (tempTerritories.Count == 0)
                {
                    LogDebug("No territories loaded from SaintCoinach, trying CSV files...");
                    LoadTerritoriesFromCsv();
                    return; 
                }

                tempTerritories.Sort((a, b) => a.Id.CompareTo(b.Id));

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    Territories.Clear();
                    _filteredTerritories.Clear();

                    foreach (var territory in tempTerritories)
                    {
                        Territories.Add(territory);
                    }

                    LogDebug($"Territory list updated with {Territories.Count} entries");

                    ApplyTerritoryFilters();
                });
            }
            catch (Exception ex)
            {
                LogDebug($"Critical error loading territories: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void LoadTerritoriesFromCsv()
        {
            try
            {
                var territoryData = LoadCsvFile("Sheets/TerritoryType.csv");
                var placeNameData = LoadCsvFile("Sheets/PlaceName.csv");

                if (territoryData == null || placeNameData == null)
                {
                    LogDebug("Failed to load CSV files.");
                    return;
                }

                var placeNameLookup = placeNameData.Skip(3)
                    .Where(row => row.Length > 1 && uint.TryParse(row[0], out _))
                    .ToDictionary(row => uint.Parse(row[0]), row => row[1]);

                foreach (var row in territoryData.Skip(3))
                {
                    if (row.Length < 8 || !uint.TryParse(row[0], out uint territoryId)) continue;

                    uint.TryParse(row.ElementAtOrDefault(4), out uint regionNameId);
                    uint.TryParse(row.ElementAtOrDefault(6), out uint placeNameId);
                    uint.TryParse(row.ElementAtOrDefault(7), out uint mapId);

                    var territoryInfo = new TerritoryInfo
                    {
                        Id = territoryId,
                        Name = placeNameLookup.GetValueOrDefault(placeNameId, "Unknown"),
                        TerritoryNameId = row.ElementAtOrDefault(1) ?? "",
                        PlaceNameId = placeNameId,
                        PlaceName = placeNameLookup.GetValueOrDefault(placeNameId, "Unknown"),
                        RegionId = regionNameId,
                        RegionName = placeNameLookup.GetValueOrDefault(regionNameId, "Unknown"),
                        Region = placeNameLookup.GetValueOrDefault(regionNameId, "Unknown"),
                        MapId = mapId,
                    };

                    Territories.Add(territoryInfo);
                    _filteredTerritories.Add(territoryInfo);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading territories from CSV: {ex.Message}");
            }
        }

        private List<string[]>? LoadCsvFile(string filePath)
        {
            try
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
                if (!File.Exists(fullPath))
                {
                    StatusText.Text = $"CSV file not found: {filePath}";
                    return null;
                }
                return File.ReadAllLines(fullPath).Select(line => line.Split(',')).ToList();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading CSV file {filePath}: {ex.Message}";
                return null;
            }
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

        private void DiagnoseMapDisplay()
        {
            LogDebug("=== MAP DISPLAY DIAGNOSTIC ===");

            if (MapImageControl.Source is BitmapSource bmp)
            {
                LogDebug($"Map image: {bmp.PixelWidth}x{bmp.PixelHeight} pixels");

                var transformGroup = MapImageControl.RenderTransform as TransformGroup;
                if (transformGroup != null)
                {
                    var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
                    var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();

                    LogDebug($"Map transforms: Scale={scaleTransform?.ScaleX:F2}, " +
                             $"Position=({translateTransform?.X:F1},{translateTransform?.Y:F1})");
                }
            }
            else
            {
                LogDebug("Map image source is null");
            }

            // Check markers
            LogDebug($"Canvas size: {MapCanvas.ActualWidth}x{MapCanvas.ActualHeight}");
            LogDebug($"Canvas children count: {MapCanvas.Children.Count}");
            LogDebug($"Current scale: {_currentScale:F2}");

            LogDebug("==============================");
        }

        private void DiagnoseMapVisibility()
        {
            if (MapImageControl.Source == null)
            {
                LogDebug("Map image source is null!");
                return;
            }

            LogDebug($"Map image visibility: {MapImageControl.Visibility}");
            LogDebug($"Map image opacity: {MapImageControl.Opacity}");
            LogDebug($"Map image actual size: {MapImageControl.ActualWidth}x{MapImageControl.ActualHeight}");
            LogDebug($"Map image position: Left={Canvas.GetLeft(MapImageControl)}, Top={Canvas.GetTop(MapImageControl)}");

            var transform = MapImageControl.RenderTransform as TransformGroup;
            if (transform != null)
            {
                var scaleTransform = transform.Children.OfType<ScaleTransform>().FirstOrDefault();
                var translateTransform = transform.Children.OfType<TranslateTransform>().FirstOrDefault();

                LogDebug($"Map image scale: {(scaleTransform != null ? $"{scaleTransform.ScaleX},{scaleTransform.ScaleY}" : "none")}");
                LogDebug($"Map image translate: {(translateTransform != null ? $"{translateTransform.X},{translateTransform.Y}" : "none")}");
            }
            else
            {
                LogDebug("Map image has no transform!");
            }

            LogDebug($"Z-Index: MapImageControl={System.Windows.Controls.Panel.GetZIndex(MapImageControl)}, " +
                     $"MapCanvas={System.Windows.Controls.Panel.GetZIndex(MapCanvas)}, " +
                     $"OverlayCanvas={System.Windows.Controls.Panel.GetZIndex(OverlayCanvas ?? new Canvas())}");
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

        private void RedirectDebugOutput()
        {
            System.Diagnostics.Trace.Listeners.Add(new TextBoxTraceListener(this));
        }

        private void DebugCanvasHierarchy()
        {
            LogDebug("=== CANVAS HIERARCHY DEBUG ===");
            LogDebug($"MapCanvas in visual tree: {MapCanvas != null}");
            LogDebug($"MapImageControl in visual tree: {MapImageControl != null}");
            LogDebug($"OverlayCanvas in visual tree: {OverlayCanvas != null}");

            if (MapCanvas != null)
            {
                LogDebug($"MapCanvas children count: {MapCanvas.Children.Count}");
                LogDebug($"MapCanvas contains MapImageControl: {MapCanvas.Children.Contains(MapImageControl)}");

                for (int i = 0; i < MapCanvas.Children.Count; i++)
                {
                    var child = MapCanvas.Children[i];
                    LogDebug($"  Child {i}: Type={child.GetType().Name}, Z-Index={System.Windows.Controls.Panel.GetZIndex(child)}");
                }
            }

            if (MapCanvas?.Parent is Border border && border.Parent is Grid parentGrid)
            {
                LogDebug($"Parent Grid children count: {parentGrid.Children.Count}");
                for (int i = 0; i < parentGrid.Children.Count; i++)
                {
                    var child = parentGrid.Children[i];
                    LogDebug($"  Child {i}: Type={child.GetType().Name}");
                }
            }

            LogDebug("==============================");
        }

        private void VerifyMapImageState()
        {
            LogDebug("VERIFYING MAP IMAGE STATE:");
            LogDebug($"- MapImageControl source null? {MapImageControl.Source == null}");
            LogDebug($"- MapImageControl dimensions: {MapImageControl.Width}x{MapImageControl.Height}");
            LogDebug($"- MapImageControl actual size: {MapImageControl.ActualWidth}x{MapImageControl.ActualHeight}");
            LogDebug($"- MapImageControl position: Left={Canvas.GetLeft(MapImageControl)}, Top={Canvas.GetTop(MapImageControl)}");
            LogDebug($"- MapImageControl in Canvas: {MapCanvas.Children.Contains(MapImageControl)}");
            LogDebug($"- MapImageControl visibility: {MapImageControl.Visibility}");
            LogDebug($"- MapImageControl z-index: {Canvas.GetZIndex(MapImageControl)}");

            if (MapImageControl.Visibility != Visibility.Visible)
            {
                MapImageControl.Visibility = Visibility.Visible;
                LogDebug("- Visibility forced to Visible");
            }
        }

        public void LogDebug(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogDebug(message));
                return;
            }

            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            DebugTextBox.AppendText(timestampedMessage + Environment.NewLine);

            if (DebugTextBox.LineCount > MaxLogLines)
            {
                int charsToRemove = DebugTextBox.GetCharacterIndexFromLineIndex(DebugTextBox.LineCount - MaxLogLines);
                DebugTextBox.Text = DebugTextBox.Text.Substring(charsToRemove);
            }

            if (AutoScrollCheckBox?.IsChecked == true)
            {
                DebugScrollViewer.ScrollToEnd();
            }
        }

        private void ToggleDebugMode_Click(object sender, RoutedEventArgs e)
        {
            if (_mapRenderer != null)
            {
                bool isEnabled = DebugModeCheckBox.IsChecked == true;
                _debugLoggingEnabled = isEnabled;
                _mapRenderer.EnableVerboseLogging(isEnabled);
                _mapRenderer.EnableDebugVisuals(isEnabled);
                LogDebug($"Debug mode {(isEnabled ? "enabled" : "disabled")}");
            }
        }

        private bool ValidateGameDirectory(string directory)
        {
            LogDebug($"Validating directory: '{directory}'");
            try
            {
                bool gameFolderExists = Directory.Exists(Path.Combine(directory, "game"));
                bool bootFolderExists = Directory.Exists(Path.Combine(directory, "boot"));
                return gameFolderExists && bootFolderExists;
            }
            catch (Exception ex)
            {
                LogDebug($"Error during directory validation: {ex.Message}");
                return false;
            }
        }

        private void DiagnoseOverlayCanvas()
        {
            if (OverlayCanvas == null)
            {
                LogDebug("ERROR: OverlayCanvas is NULL!");
                return;
            }

            LogDebug("=== OVERLAY CANVAS DIAGNOSTIC ===");
            LogDebug($"OverlayCanvas exists: Yes");
            LogDebug($"OverlayCanvas size: {OverlayCanvas.ActualWidth}x{OverlayCanvas.ActualHeight}");
            LogDebug($"OverlayCanvas visibility: {OverlayCanvas.Visibility}");
            LogDebug($"OverlayCanvas z-index: {WpfPanel.GetZIndex(OverlayCanvas)}");
            LogDebug($"OverlayCanvas children count: {OverlayCanvas.Children.Count}");

            bool isAttachedToVisualTree = false;
            DependencyObject parent = OverlayCanvas;
            while (parent != null)
            {
                if (parent is Window)
                {
                    isAttachedToVisualTree = true;
                    break;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }

            LogDebug($"OverlayCanvas attached to window: {isAttachedToVisualTree}");

            var transform = OverlayCanvas.RenderTransform as TransformGroup;
            if (transform != null)
            {
                var scaleTransform = transform.Children.OfType<ScaleTransform>().FirstOrDefault();
                var translateTransform = transform.Children.OfType<TranslateTransform>().FirstOrDefault();

                LogDebug($"OverlayCanvas scale transform: {(scaleTransform != null ? $"{scaleTransform.ScaleX:F2}" : "none")}");
                LogDebug($"OverlayCanvas translate transform: {(translateTransform != null ? $"({translateTransform.X:F1}, {translateTransform.Y:F1})" : "none")}");
            }
            else
            {
                LogDebug("OverlayCanvas has no transform!");
            }

            LogDebug("Children types:");
            foreach (var child in OverlayCanvas.Children)
            {
                LogDebug($"  - {child.GetType().Name}, Z-Index={WpfPanel.GetZIndex((UIElement)child)}");
            }

            LogDebug("==============================");
        }
    }

    public class TextBoxTraceListener : System.Diagnostics.TraceListener
    {
        private readonly MainWindow _window;

        public TextBoxTraceListener(MainWindow window)
        {
            _window = window;
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                _window.LogDebug(message);
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