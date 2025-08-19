using Amaurot.Services;
using System.Windows.Forms;
using Microsoft.Win32;
using SaintCoinach;
using SaintCoinach.Ex;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BNpcInfo = Amaurot.Services.Entities.BNpcInfo;
using EntityNpcInfo = Amaurot.Services.Entities.NpcInfo;
using EntityNpcQuestInfo = Amaurot.Services.Entities.NpcQuestInfo;
using FateInfo = Amaurot.Services.Entities.FateInfo;
using InstanceContentInfo = Amaurot.Services.Entities.InstanceContentInfo;
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using ServiceNpcInfo = Amaurot.Services.NpcInfo;
using ServiceNpcQuestInfo = Amaurot.Services.NpcQuestInfo;
using TerritoryInfo = Amaurot.Services.Entities.TerritoryInfo;

namespace Amaurot
{
    public partial class MainWindow : Window
    {
        #region Fields

        private readonly ServiceContainer _services = new();
        private readonly EntityCollectionManager _entities = new();
        private readonly MapStateManager _mapState = new();
        private readonly ProgressManager _progressManager;

        private ARealmReversed? _realm;
        private bool _isLoadingData;
        private DispatcherTimer? _searchDebounceTimer;
        private FilterService? _filterService;

        #endregion Fields

        #region Properties

        public ObservableCollection<TerritoryInfo> Territories => _entities.Territories;
        public ObservableCollection<QuestInfo> Quests => _entities.Quests;
        public ObservableCollection<EntityNpcInfo> FilteredNpcs => _entities.FilteredNpcs;
        public ObservableCollection<BNpcInfo> BNpcs => _entities.BNpcs;
        public ObservableCollection<FateInfo> Fates => _entities.Fates;
        public ObservableCollection<TerritoryInfo> FilteredTerritories => _entities.FilteredTerritories;
        public ObservableCollection<QuestInfo> FilteredQuests => _entities.FilteredQuests;
        public ObservableCollection<BNpcInfo> FilteredBNpcs => _entities.FilteredBNpcs;
        public ObservableCollection<FateInfo> FilteredFates => _entities.FilteredFates;
        public ObservableCollection<InstanceContentInfo> FilteredInstanceContents => _entities.FilteredInstanceContents;
        public double CurrentScale => _mapState.CurrentScale;

        #endregion Properties

        public MainWindow()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);

            InitializeComponent();
            DataContext = this;
            _progressManager = new ProgressManager(this);
            InitializeServices();
            Loaded += OnWindowLoaded;

            this.Closing += MainWindow_Closing;
        }

        private void SafeShutdown()
        {
            try
            {
                LogDebug("Application shutting down - disposing resources...");

                _filterService?.Dispose();

                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer = null;
                }

                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LogDebug($"Error during shutdown: {ex.Message}");
                Environment.Exit(0);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SafeShutdown();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            SafeShutdown();
        }

        #region Initialization

        private void InitializeServices()
        {
            try
            {
                _services.Register(new SettingsService());
                
                // ✅ CORRECT: Initialize DebugModeManager instead of registering DebugHelper
                DebugModeManager.Initialize(this);
                
                _services.Register(new MapRenderer(_realm));
                _services.Register(new UIUpdateService());

                _filterService = new FilterService(LogDebug);
                _services.Register(_filterService);

                _services.Register(new MapInteractionService(LogDebug));
                _services.Register<IFileDialogService>(new CrossPlatformFileDialogService());

                LogDebug("Services initialized successfully");
            }
            catch (Exception ex)
            {
                LogDebug($"Error initializing services: {ex.Message}");
            }
        }

        private void InitializeRealmDependentServices()
        {
            if (_realm == null) return;

            _services.Register(new DataLoaderService(_realm, LogDebug));
            _services.Register(new QuestMarkerService(_realm, LogDebug));
            _services.Register(new QuestLocationService(_realm, LogDebug));
            _services.Register(new MapService(_realm, LogDebug));
            _services.Register(new NpcService(_realm, LogDebug));
            _services.Register(new QuestScriptService(_services.Get<SettingsService>()!, LogDebug));
            _services.Register(new InstanceScriptService(_services.Get<SettingsService>()!, LogDebug));

            var mapRenderer = _services.Get<MapRenderer>();
            if (mapRenderer != null && _realm != null)
            {
                mapRenderer.UpdateRealm(_realm);
            }
            LogDebug("Realm-dependent services initialized");
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            InitializeUI();
            ApplySavedSettings();

            var settings = _services.Get<SettingsService>();
            if (settings?.Settings.AutoLoadGameData == true && settings.IsValidGamePath())
            {
                LogDebug("Auto-loading game data...");
                await LoadFFXIVDataAsync(settings.Settings.GameInstallationPath);
            }
        }

        private void InitializeUI()
        {
            var renderer = _services.Get<MapRenderer>();
            if (renderer != null)
            {
                if (FindName("OverlayCanvas") is Canvas overlayCanvas)
                {
                    renderer.SetOverlayCanvas(overlayCanvas);
                }
                renderer.SetMainWindow(this);
            }

            if (FindName("OverlayCanvas") is Canvas overlayCanvas2)
            {
                overlayCanvas2.IsHitTestVisible = true;
                overlayCanvas2.Background = null;
            }

            if (FindName("MapImageControl") is System.Windows.Controls.Image mapImageControl &&
                mapImageControl.Source is BitmapSource bitmapSource)
            {
                CalculateAndApplyInitialScale(bitmapSource);
            }

            UpdatePanelAreaVisibility();
            InitializeDynamicPanelLayout();

            if (FindName("ShowNpcMarkersCheckBox") is System.Windows.Controls.CheckBox showNpcMarkersCheckBox)
            {
                showNpcMarkersCheckBox.IsChecked = true;
            }
        }

        private void InitializeDynamicPanelLayout()
        {
            ReorganizePanels();
            LogDebug("Dynamic panel layout initialized");
        }

        private void ApplySavedSettings()
        {
            var settings = _services.Get<SettingsService>()?.Settings;
            if (settings == null) return;

            if (FindName("DebugModeCheckBox") is System.Windows.Controls.CheckBox debugModeCheckBox)
            {
                debugModeCheckBox.IsChecked = settings.DebugMode;
                DebugModeManager.IsDebugModeEnabled = settings.DebugMode;
            }

            if (FindName("HideDuplicateTerritoriesCheckBox") is System.Windows.Controls.CheckBox hideDuplicateTerritoriesCheckBox)
            {
                hideDuplicateTerritoriesCheckBox.IsChecked = settings.HideDuplicateTerritories;
                _mapState.HideDuplicateTerritories = settings.HideDuplicateTerritories;
            }

            if (FindName("AutoLoadMenuItem") is MenuItem autoLoadMenuItem)
            {
                autoLoadMenuItem.IsChecked = settings.AutoLoadGameData;
            }

            LogDebug($"Settings applied - Auto-load: {settings.AutoLoadGameData}, Debug: {settings.DebugMode}");
        }

        #endregion Initialization

        #region Data Loading

        private async void LoadGameData_Click(object sender, RoutedEventArgs e)
        {
            // REPLACE: Microsoft.Win32.OpenFileDialog with IFileDialogService
            var fileDialogService = _services.Get<IFileDialogService>();
            if (fileDialogService == null)
            {
                LogDebug("File dialog service not available");
                return;
            }

            var selectedPath = fileDialogService.SelectFolder(
                "Select FFXIV game installation folder",
                _services.Get<SettingsService>()?.Settings.GameInstallationPath ??
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            );

            if (!string.IsNullOrEmpty(selectedPath))
            {
                _services.Get<SettingsService>()?.UpdateGamePath(selectedPath);
                await LoadFFXIVDataAsync(selectedPath);
            }
        }

        private async Task LoadFFXIVDataAsync(string gameDirectory)
        {
            try
            {
                _isLoadingData = true;
                StatusText.Text = "Loading FFXIV data...";

                if (!ValidateGameDirectory(gameDirectory))
                {
                    System.Windows.MessageBox.Show(
                        "Invalid FFXIV installation folder.\nPlease select the folder containing 'game' and 'boot' folders.",
                        "Invalid Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await InitializeRealm(gameDirectory);
                InitializeRealmDependentServices();

                await LoadAllGameData();

                StatusText.Text = $"Data loaded from: {gameDirectory}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                LogDebug($"Error loading data: {ex.Message}\n{ex.StackTrace}");
                System.Windows.MessageBox.Show($"Failed to load data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingData = false;
                ReorganizePanels();
            }
        }

        private async Task InitializeRealm(string gameDirectory)
        {
            await Task.Run(() =>
            {
                _realm = new ARealmReversed(gameDirectory, SaintCoinach.Ex.Language.English);
            });

            var mapRenderer = _services.Get<MapRenderer>();
            if (mapRenderer != null && _realm != null)
            {
                mapRenderer.UpdateRealm(_realm);
            }
            LogDebug($"Realm initialized: {_realm != null}");
        }

        private async Task LoadAllGameData()
        {
            var dataLoader = _services.Get<DataLoaderService>();
            if (dataLoader == null) return;

            await LoadEntityData<TerritoryInfo>(
                () => dataLoader.LoadTerritoriesAsync(),
                _entities.Territories,
                _entities.FilteredTerritories,
                "Loading territories...");

            await LoadEntityData<QuestInfo>(
                () => dataLoader.LoadQuestsAsync(),
                _entities.Quests,
                _entities.FilteredQuests,
                "Loading quests...");

            await LoadEntityData<BNpcInfo>(
                () => dataLoader.LoadBNpcsAsync(),
                _entities.BNpcs,
                _entities.FilteredBNpcs,
                "Loading NPCs...");

            await LoadEntityData<FateInfo>(
                () => dataLoader.LoadFatesAsync(),
                _entities.Fates,
                _entities.FilteredFates,
                "Loading fates...");

            await LoadEntityData<InstanceContentInfo>(
                () => dataLoader.LoadInstanceContentsAsync(),
                _entities.InstanceContents,
                _entities.FilteredInstanceContents,
                "Loading instance content...");

            await LoadNpcsAsync();

            StatusText.Text = "Extracting quest markers...";
            var questMarkers = await _services.Get<QuestMarkerService>()?.ExtractAllQuestMarkersAsync();
            if (questMarkers != null)
            {
                _mapState.AllQuestMarkers.AddRange(questMarkers);
                LogDebug($"Extracted {questMarkers.Count} quest markers");
            }

            StatusText.Text = "Loading quest locations...";
            await dataLoader.LoadQuestLocationsAsync(_entities.Quests.ToList());

            if (_entities.Territories.Count > 0)
            {
                ApplyTerritoryFilters();
            }
        }

        private async Task LoadEntityData<T>(
            Func<Task<List<T>>> loadFunc,
            ObservableCollection<T> sourceCollection,
            ObservableCollection<T> filteredCollection,
            string statusMessage) where T : class
        {
            StatusText.Text = statusMessage;

            try
            {
                var data = await loadFunc();

                await Dispatcher.InvokeAsync(() =>
                {
                    sourceCollection.Clear();
                    filteredCollection.Clear();

                    foreach (var item in data)
                    {
                        sourceCollection.Add(item);
                        filteredCollection.Add(item);
                    }

                    UpdateEntityCount<T>();
                });

                LogDebug($"Loaded {data.Count} {typeof(T).Name} items");
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading {typeof(T).Name}: {ex.Message}");
            }
        }

        private async Task LoadNpcsAsync()
        {
            try
            {
                if (_realm == null) return;

                var npcService = _services.Get<NpcService>();
                if (npcService == null) return;

                LogDebug("Loading NPCs from Saint Coinach...");
                var npcs = await npcService.ExtractNpcsWithPositionsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    _entities.AllNpcs.Clear();

                    var entityNpcs = npcs.Select(serviceNpc => new EntityNpcInfo
                    {
                        Id = serviceNpc.NpcId,
                        Name = serviceNpc.NpcName,
                        NpcName = serviceNpc.NpcName,
                        MapId = serviceNpc.MapId,
                        MapX = serviceNpc.MapX,
                        MapY = serviceNpc.MapY,
                        MapZ = serviceNpc.MapZ,
                        TerritoryId = serviceNpc.TerritoryId,
                        TerritoryName = serviceNpc.TerritoryName,
                        WorldX = serviceNpc.WorldX,
                        WorldY = serviceNpc.WorldY,
                        WorldZ = serviceNpc.WorldZ,
                        QuestCount = serviceNpc.QuestCount,
                        Quests = serviceNpc.Quests.Select(q => new EntityNpcQuestInfo
                        {
                            QuestId = q.QuestId,
                            QuestName = q.QuestName,
                            MapId = q.MapId,
                            TerritoryId = q.TerritoryId,
                            MapX = q.MapX,
                            MapY = q.MapY,
                            MapZ = q.MapZ,
                            JournalGenre = q.JournalGenre,
                            LevelRequired = q.LevelRequired,
                            IsMainScenario = q.IsMainScenario,
                            IsFeatureQuest = q.IsFeatureQuest,
                            ExpReward = q.ExpReward,
                            GilReward = q.GilReward,
                            PlaceNameId = q.PlaceNameId,
                            PlaceName = q.PlaceName
                        }).ToList()
                    }).ToList();

                    _entities.AllNpcs.AddRange(entityNpcs);
                    _entities.FilteredNpcs.Clear();

                    foreach (var npc in entityNpcs)
                    {
                        _entities.FilteredNpcs.Add(npc);
                    }

                    UpdateEntityCount<EntityNpcInfo>();
                });

                LogDebug($"Loaded {npcs.Count} NPCs");
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading NPCs: {ex.Message}");
            }
        }

        #endregion Data Loading

        #region Map Operations

        private async void TerritoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TerritoryList.SelectedItem is TerritoryInfo territory)
            {
                try
                {
                    StatusText.Text = $"Loading map for {territory.PlaceName}...";
                    bool success = await LoadTerritoryMapAsync(territory);

                    if (!success)
                    {
                        StatusText.Text = $"Failed to load map for {territory.PlaceName}";
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error loading territory: {ex.Message}");
                    StatusText.Text = "Error loading territory map";
                }
            }

            if (_entities.AllNpcs.Count > 0 && TerritoryList.SelectedItem != null)
            {
                FilterNpcs();
            }
        }

        private async Task<bool> LoadTerritoryMapAsync(TerritoryInfo territory)
        {
            try
            {
                _mapState.CurrentTerritory = territory;
                _mapState.CurrentMap = null;

                UpdateMapInfo(territory);
                MapImageControl.Source = null;
                _services.Get<MapRenderer>()?.ClearMarkers();

                if (_realm == null)
                {
                    StatusText.Text = "Game data not loaded.";
                    return false;
                }

                var map = await LoadMapData(territory);
                if (map == null) return false;

                _mapState.CurrentMap = map;

                var mapImage = await LoadMapImage(map);
                if (mapImage == null) return false;

                await DisplayMap(mapImage, territory);
                return true;
            }
            catch (Exception ex)
            {
                MapInfoText.Text = $"Error: {ex.Message}";
                LogDebug($"Error loading map: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private void UpdateMapInfo(TerritoryInfo territory)
        {
            MapInfoText.Text = $"""
                Region: {territory.Region}
                PlaceNameZone: {territory.PlaceNameZone}
                Place Name: {territory.PlaceName}
                Territory ID: {territory.Id}
                Territory Name ID: {territory.TerritoryNameId}
                Map ID: {territory.MapId}
                bg: {territory.Bg}
                """;
        }

        private async Task<SaintCoinach.Xiv.Map?> LoadMapData(TerritoryInfo territory)
        {
            return await Task.Run(() =>
            {
                var mapSheet = _realm?.GameData.GetSheet<SaintCoinach.Xiv.Map>();
                return mapSheet?.FirstOrDefault(m => m.Key == territory.MapId);
            });
        }

        private async Task<System.Drawing.Image?> LoadMapImage(SaintCoinach.Xiv.Map map)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var image = map.MediumImage;
                    if (image == null)
                    {
                        LogDebug($"Failed to load map image for Map ID: {map.Key}");
                    }
                    return image;
                }
                catch (Exception ex)
                {
                    LogDebug($"Exception loading map image for Map ID: {map.Key}, Error: {ex.Message}");
                    return null;
                }
            });
        }

        private async Task DisplayMap(System.Drawing.Image mapImage, TerritoryInfo territory)
        {
            if (mapImage == null)
            {
                LogDebug("Map image is null, cannot display");
                return;
            }

            using var bitmap = new System.Drawing.Bitmap(mapImage);

            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Position = 0;

            var bitmapSource = new BitmapImage();
            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.OnLoad;
            bitmapSource.StreamSource = memoryStream;
            bitmapSource.EndInit();
            bitmapSource.Freeze();

            MapImageControl.Source = bitmapSource;

            if (!MapCanvas.Children.Contains(MapImageControl))
            {
                MapCanvas.Children.Insert(0, MapImageControl);
            }

            CalculateAndApplyInitialScale(bitmapSource);
            MapPlaceholderText.Visibility = Visibility.Collapsed;

            await LoadMapMarkers(territory);
        }

        private async Task LoadMapMarkers(TerritoryInfo territory)
        {
            StatusText.Text = $"Loading markers for {territory.PlaceName}...";

            var mapService = _services.Get<MapService>();

            // Load both marker types in parallel
            var originalMarkersTask = Task.Run(() =>
                mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>());

            var npcMarkersTask = CreateNpcMarkersWithScriptCheckingAsync(territory.MapId);

            // Wait for both to complete
            var originalMarkers = await originalMarkersTask;
            var npcMarkers = await npcMarkersTask;

            _mapState.CurrentMapMarkers.Clear();
            _mapState.CurrentMapMarkers.AddRange(originalMarkers);
            _mapState.CurrentMapMarkers.AddRange(npcMarkers);

            LogDebug($"Loaded {_mapState.CurrentMapMarkers.Count} markers " +
                     $"({originalMarkers.Count} original, {npcMarkers.Count} NPC)");

            RefreshMarkerDisplay();

            StatusText.Text = $"Map loaded for '{territory.PlaceName}' - " +
                              $"{_mapState.CurrentMapMarkers.Count} markers found";
        }

        // Fixed async method that doesn't cause deadlocks
        private async Task<List<MapMarker>> CreateNpcMarkersWithScriptCheckingAsync(uint mapId)
        {
            return await Task.Run(() =>
            {
                var markers = new List<MapMarker>();
                var npcsForMap = _entities.AllNpcs.Where(npc =>
                    npc.MapId == mapId && npc.QuestCount > 0).ToList();

                var questScriptService = _services?.Get<QuestScriptService>();
                LogDebug($"Creating NPC markers for map {mapId} with {npcsForMap.Count} NPCs");

                if (questScriptService == null)
                {
                    // No script service - create all markers with default icon
                    foreach (var npc in npcsForMap)
                    {
                        markers.Add(CreateNpcMarker(npc, 71031, "ui/icon/071000/071031.tex", 0, npc.QuestCount));
                    }
                    LogDebug($"Created {markers.Count} NPC markers (no script service available)");
                    return markers;
                }

                // Build script cache efficiently
                var scriptCache = BuildOptimizedScriptCacheForMap(npcsForMap, questScriptService);

                foreach (var npc in npcsForMap)
                {
                    uint iconId = 71031; // Default: no scripts found
                    string iconPath = "ui/icon/071000/071031.tex";
                    int scriptsAvailable = 0;

                    // Check cached script availability for this NPC's quests
                    foreach (var quest in npc.Quests)
                    {
                        var fullQuestInfo = _entities.Quests.FirstOrDefault(q => q.Id == quest.QuestId);
                        if (fullQuestInfo != null && !string.IsNullOrEmpty(fullQuestInfo.QuestIdString))
                        {
                            if (scriptCache.TryGetValue(fullQuestInfo.QuestIdString, out bool hasScript) && hasScript)
                            {
                                scriptsAvailable++;
                            }
                        }
                    }

                    // Use script-available icon if any quest has scripts
                    if (scriptsAvailable > 0)
                    {
                        iconId = 61411;
                        iconPath = "ui/icon/061000/061411.tex";
                    }

                    markers.Add(CreateNpcMarker(npc, iconId, iconPath, scriptsAvailable, npc.QuestCount));
                }

                LogDebug($"Created {markers.Count} NPC markers for map {mapId}. " +
                         $"Icons: {markers.Count(m => m.IconId == 61411)} with scripts, " +
                         $"{markers.Count(m => m.IconId == 71031)} without scripts");
                return markers;
            });
        }

        // Optimized cache building with limits to prevent excessive file system calls
        private Dictionary<string, bool> BuildOptimizedScriptCacheForMap(List<EntityNpcInfo> npcsForMap, QuestScriptService questScriptService)
        {
            var scriptCache = new Dictionary<string, bool>();

            try
            {
                var uniqueQuestIds = npcsForMap
                    .SelectMany(npc => npc.Quests)
                    .Select(q => _entities.Quests.FirstOrDefault(quest => quest.Id == q.QuestId))
                    .Where(quest => quest != null && !string.IsNullOrEmpty(quest.QuestIdString))
                    .Select(quest => quest.QuestIdString!)
                    .Distinct()
                    .Take(100) // Reasonable limit to prevent excessive calls
                    .ToList();

                LogDebug($"Checking script availability for {uniqueQuestIds.Count} unique quests on this map");

                // Use the cached method from QuestScriptService
                foreach (var questIdString in uniqueQuestIds)
                {
                    try
                    {
                        // This method already has caching built-in, so it's fast on subsequent calls
                        bool hasScript = questScriptService.HasQuestScriptInRepo(questIdString);
                        scriptCache[questIdString] = hasScript;
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error checking script for {questIdString}: {ex.Message}");
                        scriptCache[questIdString] = false;
                    }
                }

                var scriptsFound = scriptCache.Values.Count(v => v);
                LogDebug($"Script cache built: {scriptsFound}/{uniqueQuestIds.Count} quests have scripts");
            }
            catch (Exception ex)
            {
                LogDebug($"Error building script cache: {ex.Message}");
            }

            return scriptCache;
        }

        private MapMarker CreateNpcMarker(EntityNpcInfo npc, uint iconId, string iconPath, int scriptsAvailable, int questCount)
        {
            string markerText = scriptsAvailable > 0
                ? $"{npc.NpcName} ({questCount} quest{(questCount != 1 ? "s" : "")}, {scriptsAvailable} script{(scriptsAvailable != 1 ? "s" : "")})"
                : $"{npc.NpcName} ({questCount} quest{(questCount != 1 ? "s" : "")}, no scripts)";

            return new MapMarker
            {
                Id = npc.NpcId,
                MapId = npc.MapId,
                PlaceNameId = 0,
                PlaceName = markerText,
                X = npc.MapX,
                Y = npc.MapY,
                Z = npc.MapZ,
                IconId = iconId,
                IconPath = iconPath,
                Type = MarkerType.Npc,
                IsVisible = true
            };
        }

        private void RefreshMarkerDisplay()
        {
            var visibleMarkers = GetVisibleMarkers();

            if (_mapState.CurrentMap != null && MapImageControl.Source is BitmapSource bitmapSource)
            {
                var imageSize = new System.Windows.Size(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                var renderer = _services.Get<MapRenderer>();

                renderer?.DisplayMapMarkers(
                    visibleMarkers,
                    _mapState.CurrentMap,
                    _mapState.CurrentScale,
                    new System.Windows.Point(0, 0),
                    imageSize);

                SyncOverlayWithMap();
            }
        }

        private List<MapMarker> GetVisibleMarkers()
        {
            return _mapState.CurrentMapMarkers.Where(marker =>
            {
                return marker.Type switch
                {
                    MarkerType.Aetheryte => ShowAetheryteMarkersCheckBox?.IsChecked == true,
                    MarkerType.Npc => ShowNpcMarkersCheckBox?.IsChecked == true,
                    MarkerType.Shop => ShowShopMarkersCheckBox?.IsChecked == true,
                    MarkerType.Landmark => ShowLandmarkMarkersCheckBox?.IsChecked == true,
                    MarkerType.Fate => ShowFateMarkersCheckBox?.IsChecked == true,
                    MarkerType.Entrance => ShowEntranceMarkersCheckBox?.IsChecked == true,
                    _ => ShowGenericMarkersCheckBox?.IsChecked == true
                };
            }).ToList();
        }

        #endregion Map Operations

        #region Map Interaction

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var service = _services.Get<MapInteractionService>();
            var scale = _mapState.CurrentScale;
            service?.HandleMouseWheel(e, MapCanvas, MapImageControl,
                ref scale, OverlayCanvas, SyncOverlayWithMap, RefreshMarkers);
            _mapState.SetCurrentScale(scale);
            StatusText.Text = $"Zoom: {_mapState.CurrentScale:P0}";
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source != null)
            {
                _mapState.LastMousePosition = e.GetPosition(MapCanvas);
                _mapState.IsDragging = true;
                MapCanvas.Cursor = System.Windows.Input.Cursors.Hand;
                MapCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mapState.IsDragging)
            {
                _mapState.IsDragging = false;
                MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
                MapCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_mapState.IsDragging && e.LeftButton == MouseButtonState.Pressed &&
                MapImageControl.Source != null)
            {
                var currentPosition = e.GetPosition(MapCanvas);
                var deltaX = currentPosition.X - _mapState.LastMousePosition.X;
                var deltaY = currentPosition.Y - _mapState.LastMousePosition.Y;

                if (MapImageControl.RenderTransform is TransformGroup transformGroup)
                {
                    var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                    if (translateTransform != null)
                    {
                        translateTransform.X += deltaX;
                        translateTransform.Y += deltaY;
                    }
                }

                _mapState.LastMousePosition = currentPosition;
                SyncOverlayWithMap();
            }
        }

        private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source == null || _mapState.CurrentMap == null) return;

            var clickPoint = e.GetPosition(MapCanvas);

            if (MapImageControl.Source is BitmapSource bitmapSource)
            {
                var imageSize = new System.Windows.Size(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                var mapService = _services.Get<MapService>();

                var coordinates = mapService?.ConvertMapToGameCoordinates(
                    clickPoint, new System.Windows.Point(0, 0), _mapState.CurrentScale, imageSize, _mapState.CurrentMap);

                var mapRenderer = _services.Get<MapRenderer>();
                if (mapRenderer != null && coordinates != null)
                {
                    mapRenderer.ShowCoordinateInfo(coordinates, clickPoint);
                }
                e.Handled = true;
            }
        }

        #endregion Map Interaction

        #region Search and Filtering

        private void DebouncedSearch(System.Action searchAction)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                searchAction();
            };
            _searchDebounceTimer.Start();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox searchBox) return;

            var searchText = searchBox.Text ?? "";
            var filterService = _services.Get<FilterService>();

            switch (searchBox.Name)
            {
                case "QuestSearchBox":
                    filterService?.FilterQuests(searchText, _entities.Quests, _entities.FilteredQuests);
                    UpdateEntityCount<QuestInfo>();
                    break;

                case "BNpcSearchBox":
                    filterService?.FilterBNpcs(searchText, _entities.BNpcs, _entities.FilteredBNpcs);
                    UpdateEntityCount<BNpcInfo>();
                    break;

                case "FateSearchBox":
                    filterService?.FilterFates(searchText, _entities.Fates, _entities.FilteredFates);
                    UpdateEntityCount<FateInfo>();
                    break;

                case "InstanceContentSearchBox":
                    filterService?.FilterInstanceContents(searchText, _entities.InstanceContents, _entities.FilteredInstanceContents);
                    UpdateEntityCount<InstanceContentInfo>();
                    break;

                case "TerritorySearchBox":
                    DebouncedSearch(() => ApplyTerritoryFilters());
                    break;

                case "NpcSearchBox":
                    DebouncedSearch(() => FilterNpcs());
                    break;

                case "EobjSearchBox":
                    LogDebug($"{searchBox.Name.Replace("SearchBox", "")} search: '{searchText}' (feature not yet implemented)");
                    break;
            }
        }

        private void ApplyTerritoryFilters()
        {
            var territorySearchBox = FindName("TerritorySearchBox") as System.Windows.Controls.TextBox;
            var searchText = territorySearchBox?.Text ?? "";

            Task.Run(() =>
            {
                var filtered = _entities.Territories.AsEnumerable();

                if (!string.IsNullOrEmpty(searchText))
                {
                    filtered = filtered.Where(t =>
                        t.PlaceName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        t.Id.ToString().Contains(searchText) ||
                        t.Region.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                }

                if (_mapState.HideDuplicateTerritories)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    filtered = filtered.Where(t =>
                    {
                        var placeName = t.PlaceName ?? t.Name ?? "";
                        return placeName.StartsWith("[Territory ID:") ||
                               string.IsNullOrEmpty(placeName) ||
                               seen.Add(placeName);
                    });
                }

                var results = filtered.ToList();

                Dispatcher.Invoke(() =>
                {
                    _entities.FilteredTerritories.Clear();
                    foreach (var territory in results)
                    {
                        _entities.FilteredTerritories.Add(territory);
                    }
                    UpdateEntityCount<TerritoryInfo>();
                });
            });
        }

        private void NpcSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            DebouncedSearch(() => FilterNpcs());
        }

        private void FilterNpcs()
        {
            try
            {
                var npcSearchBox = FindName("NpcSearchBox") as System.Windows.Controls.TextBox;
                var searchText = npcSearchBox?.Text ?? "";
                var currentMapId = _mapState.CurrentTerritory?.MapId;

                var filterService = _services.Get<FilterService>();
                filterService?.FilterEntitiesWithTerritoryFilter(
                    searchText,
                    _entities.AllNpcs,
                    _entities.FilteredNpcs,
                    currentMapId,
                    (npc, text) => npc.NpcName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                  npc.NpcId.ToString().Contains(text));

                UpdateEntityCount<EntityNpcInfo>();
            }
            catch (Exception ex)
            {
                LogDebug($"Error filtering NPCs: {ex.Message}");
            }
        }

        #endregion Search and Filtering

        #region UI Updates

        private static readonly Dictionary<Type, (string TextBlockName, Func<EntityCollectionManager, int> GetCount)> EntityCountConfig = new()
        {
            [typeof(TerritoryInfo)] = ("TerritoryCountText", e => e.FilteredTerritories.Count),
            [typeof(QuestInfo)] = ("QuestCountText", e => e.FilteredQuests.Count),
            [typeof(EntityNpcInfo)] = ("NpcCountText", e => e.FilteredNpcs.Count),
            [typeof(BNpcInfo)] = ("BNpcCountText", e => e.FilteredBNpcs.Count),
            [typeof(FateInfo)] = ("FateCountText", e => e.FilteredFates.Count),
            [typeof(InstanceContentInfo)] = ("InstanceContentCountText", e => e.FilteredInstanceContents.Count)
        };

        private void UpdateEntityCount<T>()
        {
            if (EntityCountConfig.TryGetValue(typeof(T), out var config))
            {
                var count = config.GetCount(_entities);
                if (FindName(config.TextBlockName) is TextBlock textBlock)
                {
                    textBlock.Text = $"({count})";
                }
            }
        }

        private void CalculateAndApplyInitialScale(BitmapSource bitmapSource)
        {
            _mapState.IsDragging = false;

            double canvasWidth = MapCanvas.ActualWidth;
            double canvasHeight = MapCanvas.ActualHeight;

            if (canvasWidth <= 1 || canvasHeight <= 1)
            {
                canvasWidth = 800;
                canvasHeight = 600;
            }

            double imageWidth = bitmapSource.PixelWidth;
            double imageHeight = bitmapSource.PixelHeight;

            double defaultScale = 0.39;
            _mapState.SetCurrentScale(defaultScale);

            double centeredX = (canvasWidth - imageWidth * defaultScale) / 2;
            double centeredY = (canvasHeight - imageHeight * defaultScale) / 2;

            MapImageControl.Width = imageWidth;
            MapImageControl.Height = imageHeight;
            Canvas.SetLeft(MapImageControl, 0);
            Canvas.SetTop(MapImageControl, 0);
            Canvas.SetZIndex(MapImageControl, 0);
            MapImageControl.Visibility = Visibility.Visible;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(defaultScale, defaultScale));
            transformGroup.Children.Add(new TranslateTransform(centeredX, centeredY));
            MapImageControl.RenderTransform = transformGroup;
            MapImageControl.RenderTransformOrigin = new System.Windows.Point(0, 0);

            LogDebug($"Map scaled to {defaultScale:F2} at ({centeredX:F1}, {centeredY:F1})");
        }

        private void SyncOverlayWithMap()
        {
            if (OverlayCanvas != null && MapImageControl != null)
            {
                OverlayCanvas.RenderTransform = MapImageControl.RenderTransform;
                OverlayCanvas.RenderTransformOrigin = MapImageControl.RenderTransformOrigin;
            }
        }

        private void RefreshMarkers()
        {
            if (_mapState.CurrentMap == null) return;
            RefreshMarkerDisplay();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = _services.Get<SettingsService>();
                if (settings != null)
                {
                    var window = new SettingsWindow(settings, LogDebug) { Owner = this };
                    if (window.ShowDialog() == true)
                    {
                        ApplySavedSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening settings: {ex.Message}");
                System.Windows.MessageBox.Show($"Error opening settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleDebugMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                bool enabled = checkBox.IsChecked == true;
                DebugModeManager.IsDebugModeEnabled = enabled;

                _services.Get<MapService>()?.SetVerboseDebugMode(enabled);
                _services.Get<DataLoaderService>()?.SetVerboseDebugMode(enabled);
                _services.Get<SettingsService>()?.UpdateDebugMode(enabled);

                LogDebug($"Debug mode {(enabled ? "enabled" : "disabled")}");
            }
        }

        private void MarkerVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (_mapState.CurrentMapMarkers.Count == 0) return;
            RefreshMarkerDisplay();
        }

        private void ShowNpcMarkersCheckBox_Checked(object sender, RoutedEventArgs e) => ToggleNpcMarkers(true);

        private void ShowNpcMarkersCheckBox_Unchecked(object sender, RoutedEventArgs e) => ToggleNpcMarkers(false);

        private void HideDuplicateTerritoriesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _mapState.HideDuplicateTerritories = HideDuplicateTerritoriesCheckBox?.IsChecked == true;
            LogDebug($"Hide duplicates: {_mapState.HideDuplicateTerritories}");
            ApplyTerritoryFilters();
        }

        private void AutoLoadMenuItem_Changed(object sender, RoutedEventArgs e)
        {
            _services.Get<SettingsService>()?.UpdateAutoLoad(AutoLoadMenuItem?.IsChecked == true);
        }

        private void PanelVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                var panelName = checkBox.Name?.Replace("Show", "").Replace("CheckBox", "") + "Panel";
                if (FindName(panelName) is DockPanel panel)
                {
                    panel.Visibility = checkBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                    UpdatePanelAreaVisibility();
                }
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            DebugTextBox.Clear();
        }

        private void SaveLogButton_Click(object sender, RoutedEventArgs e)
        {
            var fileDialogService = _services.Get<IFileDialogService>();
            if (fileDialogService == null)
            {
                LogDebug("File dialog service not available");
                return;
            }

            var fileName = fileDialogService.SaveFile(
                "Save Log File",
                "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"MapEditor_Log_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            );

            if (!string.IsNullOrEmpty(fileName))
            {
                try
                {
                    File.WriteAllText(fileName, DebugTextBox.Text);
                    StatusText.Text = $"Log saved to {fileName}";
                    LogDebug($"Log saved to: {fileName}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error saving log: {ex.Message}");
                    System.Windows.MessageBox.Show($"Error saving log: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion UI Updates

        #region List Event Handlers

        private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((FindName("QuestList") as System.Windows.Controls.ListBox)?.SelectedItem is QuestInfo quest)
            {
                LogDebug($"Quest selected: {quest.Name} (ID: {quest.Id})");
            }
        }

        private async void QuestList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((FindName("QuestList") as System.Windows.Controls.ListBox)?.SelectedItem is QuestInfo quest)
            {
                await NavigateToQuestLocation(quest);
            }
        }

        private void NpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox npcListBox && npcListBox.SelectedItem is EntityNpcInfo selectedNpc)
            {
                LogDebug($"NPC selected: {selectedNpc.NpcName} (ID: {selectedNpc.Id})");
            }
        }

        private void BNpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox bnpcListBox && bnpcListBox.SelectedItem is BNpcInfo selectedBNpc)
            {
                LogDebug($"BNpc selected: {selectedBNpc.BNpcName} (ID: {selectedBNpc.BNpcNameId})");
            }
        }

        private void FateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox fateListBox && fateListBox.SelectedItem is FateInfo selectedFate)
            {
                LogDebug($"FATE selected: {selectedFate.Name} (ID: {selectedFate.FateId})");
            }
        }

        private void NpcList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (NpcList.SelectedItem is Amaurot.Services.Entities.NpcInfo selectedNpc)
            {
                var questPopup = new NpcQuestPopupWindow(selectedNpc, this);
                questPopup.Show();
                LogDebug($"Opened quest popup for NPC: {selectedNpc.NpcName}");
            }
        }

        private void QuestDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is QuestInfo quest)
            {
                ShowQuestDetails(quest);
            }
        }

        private void InstanceContentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox instanceContentListBox && instanceContentListBox.SelectedItem is InstanceContentInfo selectedInstance)
            {
                LogDebug($"Instance Content selected: {selectedInstance.InstanceName} (ID: {selectedInstance.Id})");
            }
        }

        private void InstanceContentDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is InstanceContentInfo instanceContent)
            {
                ShowInstanceContentDetails(instanceContent);
            }
        }

        private async void ExtractQuestFiles_Click(object sender, RoutedEventArgs e) =>
            await RunConfiguredTool("quest_parse.exe");

        private async void RunLgbParser_Click(object sender, RoutedEventArgs e) =>
            await RunConfiguredTool("lgb-parser.exe");

        private void EntityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox listBox || listBox.SelectedItem == null) return;

            var selectedItem = listBox.SelectedItem;
            var itemType = selectedItem.GetType().Name;

            var logMessage = selectedItem switch
            {
                QuestInfo quest => $"Quest selected: {quest.Name} (ID: {quest.Id})",
                EntityNpcInfo npc => $"NPC selected: {npc.NpcName} (ID: {npc.Id})",
                BNpcInfo bnpc => $"BNpc selected: {bnpc.BNpcName} (ID: {bnpc.BNpcNameId})",
                FateInfo fate => $"FATE selected: {fate.Name} (ID: {fate.FateId})",
                InstanceContentInfo instance => $"Instance Content selected: {instance.InstanceName} (ID: {instance.Id})",
                _ => $"Unknown item selected: {itemType}"
            };

            LogDebug(logMessage);
        }

        #endregion List Event Handlers

        #region Tool Methods

        private static readonly Dictionary<string, ToolConfig> ToolConfigurations = new()
        {
            ["quest_parse.exe"] = new("Quest extraction", (gamePath) => new[] { $"\"{gamePath}\"", "1" }),
            ["lgb-parser.exe"] = new("LGB Parser", (gamePath) => [])
        };

        private record ToolConfig(string DisplayName, Func<string, string[]> GetArguments);

        private async Task RunConfiguredTool(string toolName)
        {
            if (!ToolConfigurations.TryGetValue(toolName, out var config))
            {
                LogDebug($"Unknown tool: {toolName}");
                return;
            }

            await RunTool(toolName, config.DisplayName, async (toolPath) =>
            {
                if (toolName == "quest_parse.exe")
                {
                    var buildPath = _services.Get<SettingsService>()?.Settings.SapphireBuildPath;
                    if (string.IsNullOrEmpty(buildPath))
                    {
                        System.Windows.MessageBox.Show("Sapphire build path not configured in settings.\n\nPlease configure it first.",
                            "Build Path Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var questParserPath = Path.Combine(buildPath, "tools", "quest_parse.exe");
                    if (!File.Exists(questParserPath))
                    {
                        System.Windows.MessageBox.Show($"Quest parser not found at:\n{questParserPath}\n\nPlease build Sapphire Server first.",
                            "Quest Parser Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var gamePath = _services.Get<SettingsService>()?.Settings.GameInstallationPath;
                    if (string.IsNullOrEmpty(gamePath))
                    {
                        System.Windows.MessageBox.Show("Game installation path not configured in settings.\n\nPlease configure it first.",
                            "Game Path Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var sqpackPath = Path.Combine(gamePath, "game", "sqpack");
                    if (!Directory.Exists(sqpackPath))
                    {
                        System.Windows.MessageBox.Show($"Game sqpack directory not found at:\n{sqpackPath}\n\nPlease check your game installation path.",
                            "Sqpack Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var args = config.GetArguments(sqpackPath);
                    await RunToolWithProgress(questParserPath, string.Join(" ", args), config.DisplayName);
                }
                else if (toolName == "lgb-parser.exe")
                {
                    var gamePath = _services.Get<SettingsService>()?.Settings.GameInstallationPath;
                    if (string.IsNullOrEmpty(gamePath))
                    {
                        System.Windows.MessageBox.Show("Game path not configured", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var choice = System.Windows.MessageBox.Show(
                        "LGB Parser Options:\n\n" +
                        "Yes - Parse all LGB files (batch mode) - This will take a long time!\n" +
                        "No - List available zones\n" +
                        "Cancel - Open tool in console window for manual commands",
                        "LGB Parser", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    switch (choice)
                    {
                        case MessageBoxResult.Yes:
                            var confirmBatch = System.Windows.MessageBox.Show(
                                "Batch mode will process ALL LGB files in the game.\n" +
                                "Output will be saved as JSON files in 'lgb_output' folder.\n" +
                                "This can take 10+ minutes and use significant disk space.\n\n" +
                                "Continue?",
                                "Confirm Batch Operation",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (confirmBatch == MessageBoxResult.Yes)
                            {
                                // ✅ SIMPLE: Always use JSON format when run from tools menu
                                await RunToolWithProgress(toolPath, $"--game \"{gamePath}\" --batch lgb_output json", "LGB Batch Processing (JSON)");
                            }
                            break;

                        case MessageBoxResult.No:
                            await RunToolWithProgress(toolPath, $"--game \"{gamePath}\" --list-zones", "Listing LGB Zones");
                            break;

                        case MessageBoxResult.Cancel:
                            OpenToolInConsole(toolPath, ["--game", $"\"{gamePath}\"", "--help"]);
                            break;
                    }
                }
            });
        }

        private async Task RunTool(string toolName, string displayName, Func<string, Task> runAction)
        {
            string toolPath;

            if (toolName == "quest_parse.exe")
            {
                var buildPath = _services.Get<SettingsService>()?.Settings.SapphireBuildPath;
                if (string.IsNullOrEmpty(buildPath))
                {
                    System.Windows.MessageBox.Show("Sapphire build path not configured in settings.\n\nPlease configure it first.",
                        "Build Path Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                toolPath = Path.Combine(buildPath, "tools", toolName);
            }
            else
            {
                toolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", toolName);
            }

            if (!File.Exists(toolPath))
            {
                System.Windows.MessageBox.Show($"{displayName} not found at:\n{toolPath}",
                    $"{displayName} Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await runAction(toolPath);
        }

        private async Task RunToolWithProgress(string toolPath, string arguments, string operationName)
        {
            _progressManager.Show(operationName);
            LogDebug($"Starting {operationName} with arguments: {arguments}");

            System.Diagnostics.Process? process = null;

            try
            {
                await Task.Run(() =>
                {
                    process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = toolPath,
                            Arguments = arguments,
                            WorkingDirectory = Path.GetDirectoryName(toolPath),
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    LogDebug($"Tool path: {toolPath}");
                    LogDebug($"Arguments: {arguments}");

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogDebug($"Tool output: {e.Data}");
                            Dispatcher.InvokeAsync(() => _progressManager.UpdateStatus(e.Data));
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogDebug($"Tool error: {e.Data}");
                            Dispatcher.InvokeAsync(() => _progressManager.UpdateStatus($"Error: {e.Data}"));
                        }
                    };

                    _progressManager.SetCurrentProcess(process);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    LogDebug($"Process started with ID: {process.Id}");

                    while (!process.WaitForExit(1000))
                    {
                        if (_progressManager.CancellationToken.IsCancellationRequested)
                        {
                            LogDebug("Cancellation requested - killing process");
                            try
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(entireProcessTree: true);
                                    LogDebug($"Process {process.Id} killed successfully");
                                }
                            }
                            catch (Exception killEx)
                            {
                                LogDebug($"Error killing process: {killEx.Message}");
                            }
                            throw new OperationCanceledException("Operation was cancelled by user");
                        }
                    }

                    LogDebug($"Process exited with code: {process.ExitCode}");

                    if (process.ExitCode != 0 && !_progressManager.CancellationToken.IsCancellationRequested)
                    {
                        throw new Exception($"Tool exited with code {process.ExitCode}");
                    }
                });

                if (!_progressManager.CancellationToken.IsCancellationRequested)
                {
                    StatusText.Text = $"{operationName} completed successfully";
                    LogDebug($"{operationName} completed successfully");
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = $"{operationName} was cancelled";
                LogDebug($"{operationName} was cancelled by user");
            }
            catch (Exception ex)
            {
                LogDebug($"Error running {operationName}: {ex.Message}");
                StatusText.Text = $"Error running {operationName}: {ex.Message}";
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    process?.Dispose();
                }
                catch (Exception ex)
                {
                    LogDebug($"Error disposing process: {ex.Message}");
                }

                _progressManager.Hide();
            }
        }

        private void OpenToolInConsole(string toolPath, string[] arguments)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"\"{toolPath}\" {string.Join(" ", arguments.Select(a => $"\"{a}\""))}\"",
                    WorkingDirectory = Path.GetDirectoryName(toolPath),
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                StatusText.Text = "Tool opened in console window";
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening console: {ex.Message}");
                System.Windows.MessageBox.Show($"Failed to open console: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelExtraction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogDebug("User requested cancellation of operation");
                _progressManager.RequestCancellation();
                StatusText.Text = "Cancelling operation...";
            }
            catch (Exception ex)
            {
                LogDebug($"Error cancelling operation: {ex.Message}");
                System.Windows.MessageBox.Show($"Error cancelling operation: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSapphireRepo_Click(object sender, RoutedEventArgs e)
        {
            OpenPath("Sapphire Server repository",
                () => _services.Get<SettingsService>()?.IsValidSapphireServerPath() == true,
                () => _services.Get<SettingsService>()?.OpenSapphireServerPath());
        }

        private void OpenSapphireBuild_Click(object sender, RoutedEventArgs e)
        {
            OpenPath("Sapphire Server build",
                () => _services.Get<SettingsService>()?.IsValidSapphireBuildPath() == true,
                () => _services.Get<SettingsService>()?.OpenSapphireBuildPath());
        }

        private void OpenPath(string pathName, Func<bool> isValid, System.Action? openAction)
        {
            try
            {
                if (isValid())
                {
                    openAction?.Invoke();
                    LogDebug($"Opened {pathName} path");
                }
                else
                {
                    System.Windows.MessageBox.Show($"{pathName} path is not configured or invalid.\n\nPlease configure it in Settings first.",
                        $"{pathName} Not Set", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenSettings_Click(null!, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error opening {pathName}: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion Tool Methods

        #region Helper Methods

        // ✅ CORRECT: Use DebugModeManager directly
        public void LogDebug(string message)
        {
            DebugModeManager.LogDebug(message);
        }

        // ✅ CORRECT: Use DebugModeManager directly  
        private bool ValidateGameDirectory(string directory)
        {
            return DebugModeManager.ValidateGameDirectory(directory);
        }

        public void HandleNpcMarkerClick(uint npcId)
        {
            try
            {
                var targetNpc = _entities.FilteredNpcs.FirstOrDefault(n => n.Id == npcId);
                if (targetNpc != null)
                {
                    // ✅ PERFORMANCE FIX: Create popup asynchronously to avoid UI blocking
                    Task.Run(() =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            var questPopup = new NpcQuestPopupWindow(targetNpc, this);
                            questPopup.Show();
                            LogDebug($"Opened quest popup from map click for NPC: {targetNpc.NpcName} (ID: {npcId})");
                        });
                    });
                }
                else
                {
                    LogDebug($"Could not find NPC with ID: {npcId}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error handling NPC marker click: {ex.Message}");
            }
        }

        // ✅ ADD: Performance-optimized method to get QuestScriptService without reflection
        public QuestScriptService? GetQuestScriptService()
        {
            return _services?.Get<QuestScriptService>();
        }

        public void ToggleNpcMarkers(bool visible)
        {
            var npcMarkers = _mapState.CurrentMapMarkers.Where(m => m.Type == MarkerType.Npc).ToList();
            foreach (var marker in npcMarkers)
            {
                marker.IsVisible = visible;
            }
            RefreshMarkers();
            LogDebug($"NPC markers {(visible ? "shown" : "hidden")}: {npcMarkers.Count} markers");
        }

        public void AddCustomMarker(MapMarker marker)
        {
            _mapState.CurrentMapMarkers.RemoveAll(m => m.Id == marker.Id);
            _mapState.CurrentMapMarkers.Add(marker);
            RefreshMarkers();
            LogDebug($"Added custom marker: {marker.PlaceName} at ({marker.X:F1}, {marker.Y:F1})");
        }

        private void ShowQuestDetails(Amaurot.Services.Entities.QuestInfo quest)
        {
            try
            {
                var questScriptService = _services.Get<QuestScriptService>();
                var window = new QuestDetailsWindow(quest, this, questScriptService);
                window.Show();
                LogDebug($"Opened quest details for: {quest.Name}");
            }
            catch (Exception ex)
            {
                LogDebug($"Error showing quest details: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowInstanceContentDetails(InstanceContentInfo instanceContent)
        {
            try
            {
                var instanceScriptService = _services.Get<InstanceScriptService>();
                var window = new InstanceContentDetailsWindow(instanceContent, this, instanceScriptService);
                window.Show();
                LogDebug($"Opened instance content details for: {instanceContent.InstanceName}");
            }
            catch (Exception ex)
            {
                LogDebug($"Error showing instance content details: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task NavigateToQuestLocation(QuestInfo quest)
        {
            try
            {
                TerritoryInfo? territory = null;

                if (quest.MapId > 0)
                {
                    territory = _entities.Territories.FirstOrDefault(t => t.MapId == quest.MapId);
                }
                else if (!string.IsNullOrEmpty(quest.PlaceName))
                {
                    territory = _entities.Territories.FirstOrDefault(t =>
                        string.Equals(t.PlaceName, quest.PlaceName, StringComparison.OrdinalIgnoreCase));
                }

                if (territory != null)
                {
                    TerritoryList.SelectedItem = territory;
                    StatusText.Text = $"Loading map for quest '{quest.Name}'...";
                    await LoadTerritoryMapAsync(territory);
                }
                else
                {
                    System.Windows.MessageBox.Show($"Could not find territory for quest '{quest.Name}'",
                        "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error navigating to quest: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static readonly PanelConfig[] PanelConfigurations = [
            new("QuestsPanel", "ShowQuestsCheckBox", 1),
            new("NpcsPanel", "ShowNpcsCheckBox", 2),
            new("FatesPanel", "ShowFatesCheckBox", 3),
            new("BNpcsPanel", "ShowBNpcsCheckBox", 4),
            new("InstanceContentPanel", "ShowInstanceContentCheckBox", 5),
            new("EobjsPanel", "ShowEobjsCheckBox", 6),
        ];

        private record PanelConfig(string PanelName, string CheckBoxName, int Priority);

        private void UpdatePanelAreaVisibility()
        {
            try
            {
                var visiblePanels = PanelConfigurations
                    .Select(config => FindName(config.PanelName) as DockPanel)
                    .Where(p => p?.Visibility == Visibility.Visible)
                    .ToList();

                bool anyVisible = visiblePanels.Any();

                if (FindName("PanelAreaColumn") is ColumnDefinition panelAreaColumn && !anyVisible)
                {
                    panelAreaColumn.Width = new GridLength(0);
                }

                if (FindName("PanelGridArea") is UIElement panelGridArea)
                {
                    panelGridArea.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
                }

                if (!_isLoadingData)
                {
                    ReorganizePanels();
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error updating panel visibility: {ex.Message}");
            }
        }

        private void ReorganizePanels()
        {
            try
            {
                var visiblePanels = PanelConfigurations
                    .Select(config => (panel: FindName(config.PanelName) as DockPanel, priority: config.Priority))
                    .Where(p => p.panel?.Visibility == Visibility.Visible)
                    .OrderBy(p => p.priority)
                    .Select(p => p.panel!)
                    .ToList();

                if (FindName("DynamicPanelGrid") is not Grid grid)
                    return;

                grid.Children.Clear();
                grid.RowDefinitions.Clear();
                grid.ColumnDefinitions.Clear();

                if (visiblePanels.Count == 0)
                {
                    if (FindName("PanelAreaColumn") is ColumnDefinition panelAreaColumn)
                    {
                        panelAreaColumn.Width = new GridLength(0);
                    }
                    return;
                }

                int columns = visiblePanels.Count <= 4 ? 2 : 3;
                int rows = 2;
                double width = columns == 2 ? 500 : 750;

                if (FindName("PanelAreaColumn") is ColumnDefinition panelAreaColumn2)
                {
                    panelAreaColumn2.Width = new GridLength(width);
                }

                for (int col = 0; col < columns; col++)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    if (col < columns - 1)
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                }

                for (int row = 0; row < rows; row++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    if (row < rows - 1)
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                }

                int panelIndex = 0;
                for (int row = 0; row < rows && panelIndex < visiblePanels.Count; row++)
                {
                    for (int col = 0; col < columns && panelIndex < visiblePanels.Count; col++)
                    {
                        var panel = visiblePanels[panelIndex++];
                        if (panel.Parent is System.Windows.Controls.Panel parent)
                            parent.Children.Remove(panel);
                        Grid.SetRow(panel, row * 2);
                        Grid.SetColumn(panel, col * 2);
                        grid.Children.Add(panel);
                    }
                }

                AddGridSplitters(grid, rows, columns);

                LogDebug($"Reorganized {visiblePanels.Count} panels into {columns}×{rows} grid");
            }
            catch (Exception ex)
            {
                LogDebug($"Error reorganizing panels: {ex.Message}");
            }
        }

        private static void AddGridSplitters(Grid grid, int rows, int columns)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                var splitter = new GridSplitter
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Height = 5,
                    Background = new SolidColorBrush(Colors.LightGray)
                };
                Grid.SetRow(splitter, (row * 2) + 1);
                Grid.SetColumn(splitter, 0);
                Grid.SetColumnSpan(splitter, columns * 2 - 1);
                grid.Children.Add(splitter);
            }

            for (int col = 0; col < columns - 1; col++)
            {
                var splitter = new GridSplitter
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Width = 5,
                    Background = new SolidColorBrush(Colors.LightGray)
                };
                Grid.SetRow(splitter, 0);
                Grid.SetColumn(splitter, (col * 2) + 1);
                Grid.SetRowSpan(splitter, rows * 2 - 1);
                grid.Children.Add(splitter);
            }
        }

        #endregion Helper Methods
    }

    #region Helper Classes - ONLY ONE DEFINITION

    public class ServiceContainer
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Register<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
        }

        public T? Get<T>() where T : class
        {
            return _services.TryGetValue(typeof(T), out var service) ? service as T : null;
        }
    }

    public class EntityCollectionManager
    {
        public ObservableCollection<TerritoryInfo> Territories { get; } = new();
        public ObservableCollection<QuestInfo> Quests { get; } = new();
        public ObservableCollection<BNpcInfo> BNpcs { get; } = new();
        public ObservableCollection<FateInfo> Fates { get; } = new();
        public ObservableCollection<InstanceContentInfo> InstanceContents { get; } = new();

        public ObservableCollection<TerritoryInfo> FilteredTerritories { get; } = new();
        public ObservableCollection<QuestInfo> FilteredQuests { get; } = new();
        public ObservableCollection<EntityNpcInfo> FilteredNpcs { get; } = new();
        public ObservableCollection<BNpcInfo> FilteredBNpcs { get; } = new();
        public ObservableCollection<FateInfo> FilteredFates { get; } = new();
        public ObservableCollection<InstanceContentInfo> FilteredInstanceContents { get; } = new();

        public List<EntityNpcInfo> AllNpcs { get; } = new();

        public ObservableCollection<EntityNpcInfo> Npcs => FilteredNpcs;
    }

    public class MapStateManager
    {
        private double _currentScale = 1.0;
        public double CurrentScale => _currentScale;

        public TerritoryInfo? CurrentTerritory { get; set; }
        public SaintCoinach.Xiv.Map? CurrentMap { get; set; }
        public List<MapMarker> CurrentMapMarkers { get; } = new();
        public List<MapMarker> AllQuestMarkers { get; } = new();

        public bool IsDragging { get; set; }
        public bool HideDuplicateTerritories { get; set; }
        public System.Windows.Point LastMousePosition { get; set; }

        public void SetCurrentScale(double scale)
        {
            _currentScale = scale;
        }
    }

    public class ProgressManager
    {
        private readonly MainWindow _window;
        private CancellationTokenSource? _cancellationSource;
        private System.Diagnostics.Process? _currentProcess;

        public ProgressManager(MainWindow window)
        {
            _window = window;
        }

        public void Show(string operation)
        {
            _window.ProgressOverlay.Visibility = Visibility.Visible;
            _window.ProgressStatusText.Text = $"Starting {operation}...";
            _window.QuestExtractionProgressBar.IsIndeterminate = true;
            _window.CancelExtractionButton.IsEnabled = true;

            if (_window.FindName("ProgressTitleText") is TextBlock titleText)
            {
                titleText.Text = operation;
            }

            if (_window.FindName("OperationTypeText") is TextBlock operationText)
            {
                operationText.Text = operation;
            }

            _cancellationSource = new CancellationTokenSource();
            _currentProcess = null;
        }

        public void UpdateStatus(string message)
        {
            var displayMessage = message.Length > 120 ? message.Substring(0, 117) + "..." : message;
            
            _window.ProgressStatusText.Text = displayMessage;
            _window.StatusText.Text = displayMessage;
        }

        public void Hide()
        {
            _window.ProgressOverlay.Visibility = Visibility.Collapsed;
            
            _window.QuestExtractionProgressBar.IsIndeterminate = true;
            _window.QuestExtractionProgressBar.Value = 0;
            _window.QuestExtractionProgressBar.Foreground = new SolidColorBrush(Colors.Green);
            
            _cancellationSource?.Dispose();
            _cancellationSource = null;
            _currentProcess = null;
        }

        public void SetCurrentProcess(System.Diagnostics.Process process)
        {
            _currentProcess = process;
        }

        public void RequestCancellation()
        {
            _cancellationSource?.Cancel();

            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill(entireProcessTree: true);
                    _window.LogDebug($"Process killed due to user cancellation");
                }
                catch (Exception ex)
                {
                    _window.LogDebug($"Error killing process: {ex.Message}");
                }
            }
        }

        public CancellationToken CancellationToken => _cancellationSource?.Token ?? CancellationToken.None;
    }

    #endregion Helper Classes
}