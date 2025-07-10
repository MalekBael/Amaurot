using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using WinForms = System.Windows.Forms;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Xiv;
using System.Reflection;
using System.Drawing;
using System.Windows.Input; // Add this for MouseWheelEventArgs
using System.Windows.Media; // Add this for ScaleTransform

using Lumina;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;

namespace map_editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ARealmReversed? _realm;
        public ObservableCollection<TerritoryInfo> Territories { get; set; } = new();
        
        // Add these new fields for map functionality
        private MapService? _mapService;
        private MapRenderer? _mapRenderer;
        private SaintCoinach.Xiv.Map? _currentMap;
        private TerritoryInfo? _currentTerritory;
        private List<MapMarker> _currentMapMarkers = new();

        private double _currentScale = 1.0;
        private const double _zoomIncrement = 0.1;
        private const double _minZoom = 0.5;
        private const double _maxZoom = 5.0;

        private System.Windows.Point _lastMousePosition;
        private bool _isDragging = false;

        // Maximum number of lines to keep in the log
        private const int MaxLogLines = 1000;

        private Canvas OverlayCanvas;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Don't create OverlayCanvas here - initialize it in Loaded event instead
            
            // Subscribe to the canvas size changed event to recalculate scale if needed
            MapCanvas.SizeChanged += MapCanvas_SizeChanged;

            // Subscribe to the Loaded event to ensure initial scaling works
            this.Loaded += MainWindow_Loaded;

            // Initialize map renderer with realm (will be null initially)
            _mapRenderer = new MapRenderer(MapCanvas, MapImageControl, _realm);

            // Set up debug logging
            RedirectDebugOutput();
            LogDebug("Application started");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the overlay canvas after the visual tree is loaded
            InitializeOverlayCanvas();

            // This ensures the window and all its controls are fully loaded
            // If we have a pending image that needs scaling, apply it now
            if (MapImageControl.Source != null)
            {
                var bitmapSource = MapImageControl.Source as System.Windows.Media.Imaging.BitmapSource;
                if (bitmapSource != null)
                {
                    CalculateAndApplyInitialScale(bitmapSource);
                }
            }
        }

        private void InitializeOverlayCanvas()
        {
            try
            {
                // Create and add the overlay canvas
                OverlayCanvas = new Canvas();
                
                // Match size with MapCanvas
                OverlayCanvas.Width = MapCanvas.Width;
                OverlayCanvas.Height = MapCanvas.Height;
                OverlayCanvas.HorizontalAlignment = MapCanvas.HorizontalAlignment;
                OverlayCanvas.VerticalAlignment = MapCanvas.VerticalAlignment;
                
                // Make sure parent exists
                var parent = MapCanvas.Parent as System.Windows.Controls.Panel;
                if (parent != null)
                {
                    // Set ZIndex to ensure it's above the map canvas but below UI elements
                    System.Windows.Controls.Panel.SetZIndex(OverlayCanvas, 5);
                    
                    // Add it to the same parent as MapCanvas
                    parent.Children.Add(OverlayCanvas);
                    
                    LogDebug("OverlayCanvas successfully initialized");
                }
                else
                {
                    LogDebug("ERROR: MapCanvas.Parent is not a Panel or is null");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ERROR initializing OverlayCanvas: {ex.Message}");
            }
        }

        // Simplified canvas size changed handler - no longer needed but kept for compatibility
        private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Since we're using a fixed canvas size, we don't need to recalculate on size changes
            // This method can be empty or removed entirely
        }

        private void LoadGameData_Click(object sender, RoutedEventArgs e)
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
                    LoadFFXIVData(gamePath);
                }
                else
                {
                    System.Windows.MessageBox.Show("Invalid game folder selected.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Add right-click handler for coordinates
        private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source == null || _mapService == null || _mapRenderer == null)
                return;

            System.Windows.Point clickPoint = e.GetPosition(MapCanvas);
            var imagePosition = new System.Windows.Point(
                Canvas.GetLeft(MapImageControl),
                Canvas.GetTop(MapImageControl)
            );

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

        private void LoadFFXIVData(string gameDirectory)
        {
            try
            {
                StatusText.Text = "Loading FFXIV data...";

                // Validate that this is actually an FFXIV installation directory
                if (!ValidateGameDirectory(gameDirectory))
                {
                    WpfMessageBox.Show(
                        "The selected folder doesn't appear to be a valid FFXIV installation.\n\n" +
                        "Please select the main FFXIV installation folder that contains the 'game' and 'boot' folders.",
                        "Invalid Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Initialize SaintCoinach with the game directory
                _realm = new ARealmReversed(gameDirectory, SaintCoinach.Ex.Language.English);
                LogDebug($"Initialized realm: {(_realm != null ? "Success" : "Failed")}");

                // Initialize map service
                _mapService = new MapService(_realm);
                LogDebug("Initialized map service");

                // Run diagnostics on a few common icon IDs
                LogDebug("Running icon path diagnostics...");
                _mapService.DiagnoseIconPaths(60453); // Aetheryte
                _mapService.DiagnoseIconPaths(60442); // Another common icon

                // Update MapRenderer with realm
                if (_mapRenderer != null)
                {
                    LogDebug("Updating MapRenderer with realm reference");
                    _mapRenderer.UpdateRealm(_realm);
                }

                // Load territory data
                LogDebug("Loading territories...");
                LoadTerritories();

                // Update status bar with directory information
                StatusText.Text = $"Loaded {Territories.Count} territories from: {gameDirectory}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading data: {ex.Message}";
                LogDebug($"Error loading FFXIV data: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                WpfMessageBox.Show($"Failed to load FFXIV data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateGameDirectory(string directory)
        {
            // Add detailed logging
            System.Diagnostics.Debug.WriteLine($"Validating directory: '{directory}'");

            try
            {
                // Check for alternative folder names and structures
                string[] possibleGameFolders = { "game", "Game", "GAME", "ffxivgame" };
                string[] possibleBootFolders = { "boot", "Boot", "BOOT" };

                // Look for any valid game/boot folder combination
                string? foundGameFolder = null;  // Add ? to make nullable
                string? foundBootFolder = null;  // Add ? to make nullable

                foreach (var gameFolder in possibleGameFolders)
                {
                    string gamePath = Path.Combine(directory, gameFolder);
                    if (Directory.Exists(gamePath))
                    {
                        foundGameFolder = gamePath;
                        System.Diagnostics.Debug.WriteLine($"Found game folder at: '{gamePath}'");
                        break;
                    }
                }

                foreach (var bootFolder in possibleBootFolders)
                {
                    string bootPath = Path.Combine(directory, bootFolder);
                    if (Directory.Exists(bootPath))
                    {
                        foundBootFolder = bootPath;
                        System.Diagnostics.Debug.WriteLine($"Found boot folder at: '{bootPath}'");
                        break;
                    }
                }

                // Check all directory contents at the root level
                System.Diagnostics.Debug.WriteLine("Directory contents:");
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    System.Diagnostics.Debug.WriteLine($"- {Path.GetFileName(dir)}");
                }

                bool isValid = foundGameFolder != null && foundBootFolder != null;

                System.Diagnostics.Debug.WriteLine($"Directory validation result: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during directory validation: {ex.Message}");
                return false;
            }
        }

        private void LoadTerritories()
        {
            LogDebug("Starting to load territories...");
            Territories.Clear();

            try
            {
                // First, try loading from SaintCoinach if available
                if (_realm != null)
                {
                    LogDebug("Loading territories from SaintCoinach...");

                    try
                    {
                        var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                        if (territorySheet != null)
                        {
                            LogDebug($"Found SaintCoinach territory sheet with {territorySheet.Count()} entries");

                            // Let's examine what properties TerritoryType actually has
                            var firstTerritory = territorySheet.FirstOrDefault();
                            if (firstTerritory != null)
                            {
                                var properties = firstTerritory.GetType().GetProperties();
                                LogDebug($"TerritoryType properties: {string.Join(", ", properties.Select(p => p.Name))}");
                            }

                            foreach (var propertyName in new[] { "PlaceName", "RegionPlaceName", "ZonePlaceName", "Map", "Name" })
                            {
                                var prop = firstTerritory.GetType().GetProperty(propertyName);
                                LogDebug($"{propertyName}: {(prop != null ? "FOUND" : "NOT FOUND")}");
                            }

                            int processed = 0;
                            int added = 0;

                            foreach (var territory in territorySheet)
                            {
                                processed++;
                                try
                                {
                                    // Defensive: PlaceName
                                    string placeName = "Unknown";
                                    uint placeNameId = 0;
                                    try
                                    {
                                        var placeNameObj = territory.GetType().GetProperty("PlaceName")?.GetValue(territory);
                                        if (placeNameObj is SaintCoinach.Xiv.PlaceName pn)
                                        {
                                            placeName = SafeGetPlaceName(pn);
                                            placeNameId = (uint)pn.Key;
                                        }
                                        else if (placeNameObj != null)
                                        {
                                            var nameProp = placeNameObj.GetType().GetProperty("Name");
                                            var keyProp = placeNameObj.GetType().GetProperty("Key");
                                            if (nameProp != null)
                                                placeName = nameProp.GetValue(placeNameObj)?.ToString() ?? "Unknown";
                                            object? keyValue = keyProp?.GetValue(placeNameObj);
                                            if (keyValue is int intKey)
                                                placeNameId = (uint)intKey;
                                            else if (keyValue is uint uintKey)
                                                placeNameId = uintKey;
                                            else
                                                placeNameId = 0;
                                        }
                                    }
                                    catch (TargetInvocationException ex) when (ex.InnerException is KeyNotFoundException)
                                    {
                                        LogDebug($"Territory {territory.Key} has missing PlaceName: {ex.InnerException.Message}");
                                        continue; // Skip this territory
                                    }
                                    catch (KeyNotFoundException ex)
                                    {
                                        LogDebug($"Territory {territory.Key} has missing PlaceName: {ex.Message}");
                                        continue; // Skip this territory
                                    }

                                    // Defestring territoryNameId = territory.GetType().GetProperty("Name")?.GetValue(territory)?.ToString() ?? "";nsive: RegionPlaceName by index (index 3)
                                    string regionName = "Unknown";
                                    uint regionId = 0;
                                    try
                                    {
                                        // Try to get the value by index directly
                                        var regionObj = (territory as SaintCoinach.Ex.Relational.IRelationalRow)?[3];
                                        if (regionObj is SaintCoinach.Xiv.PlaceName rpn)
                                        {
                                            regionName = SafeGetPlaceName(rpn);
                                            regionId = (uint)rpn.Key;
                                        }
                                        else if (regionObj != null)
                                        {
                                            var nameProp = regionObj.GetType().GetProperty("Name");
                                            var keyProp = regionObj.GetType().GetProperty("Key");
                                            if (nameProp != null)
                                                regionName = nameProp.GetValue(regionObj)?.ToString() ?? "Unknown";
                                            object? regionKeyValue = keyProp?.GetValue(regionObj);
                                            if (regionKeyValue is int intKey2)
                                                regionId = (uint)intKey2;
                                            else if (regionKeyValue is uint uintKey2)
                                                regionId = uintKey2;
                                            else
                                                regionId = 0;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Log the available keys/columns for this row
                                        if (territory is SaintCoinach.Ex.Relational.IRelationalRow relRow)
                                        {
                                            var keys = string.Join(", ", relRow.Sheet.Header.Columns.Select(c => $"{c.Index}:{c.Name}"));
                                            LogDebug($"Territory {territory.Key} has missing RegionPlaceName at index 3: {ex.InnerException.Message}. Available columns: {keys}");
                                        }
                                        else
                                        {
                                            LogDebug($"Territory {territory.Key} has missing RegionPlaceName at index 3: {ex.InnerException.Message}");
                                        }
                                        continue; // Skip this territory
                                    }

                                    // Map
                                    uint mapId = 0;
                                    var mapObj = territory.GetType().GetProperty("Map")?.GetValue(territory);
                                    if (mapObj != null)
                                    {
                                        var keyProp = mapObj.GetType().GetProperty("Key");
                                        if (keyProp != null)
                                        {
                                            object? mapKeyValue = keyProp.GetValue(mapObj);
                                            if (mapKeyValue is int intMapKey)
                                                mapId = (uint)intMapKey;
                                            else if (mapKeyValue is uint uintMapKey)
                                                mapId = uintMapKey;
                                            else
                                                mapId = 0;
                                        }
                                    }

                                    // Territory Name ID
                                    string territoryNameId = "";
                                    try
                                    {
                                        var relRow = territory as SaintCoinach.Ex.Relational.IRelationalRow;
                                        if (relRow != null)
                                            territoryNameId = relRow[0]?.ToString() ?? "";
                                    }
                                    catch (Exception ex)
                                    {
                                        if (territory is SaintCoinach.Ex.Relational.IRelationalRow row)
                                        {
                                            var keys = string.Join(", ", row.Sheet.Header.Columns.Select(c => $"{c.Index}:{c.Name}"));
                                            LogDebug($"Territory {territory.Key} has missing Name at index 0: {ex.Message}. Available columns: {keys}");
                                        }
                                        else
                                        {
                                            LogDebug($"Territory {territory.Key} has missing Name at index 0: {ex.Message}");
                                        }
                                    }

                                    Territories.Add(new TerritoryInfo
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
                                        Source = "SaintCoinach"
                                    });

                                    added++;
                                }
                                catch (Exception ex)
                                {
                                    LogDebug($"Error processing territory {territory.Key}: {ex.Message}");
                                    if (ex.InnerException != null)
                                        LogDebug($"Inner exception: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                                    LogDebug($"Stack trace: {ex.StackTrace}");
                                }
                            }
                            LogDebug($"Processed {processed} territories, added {added} to list");
                        }
                        else
                        {
                            LogDebug("SaintCoinach territory sheet is null");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error loading territories from SaintCoinach: {ex.Message}");
                        LogDebug($"Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    LogDebug("Realm is null, falling back to CSV loading");
                }

                // If we couldn't load territories from SaintCoinach, try CSV loading
                if (Territories.Count == 0)
                {
                    LogDebug("No territories loaded from SaintCoinach, trying CSV files...");
                    LoadTerritoriesFromCsv();
                }

                // Sort and update UI
                if (Territories.Count > 0)
                {
                    var sorted = Territories.OrderBy(t => t.Id).ToList();
                    Territories.Clear();
                    foreach (var item in sorted)
                    {
                        Territories.Add(item);
                    }

                    // Force UI refresh
                    TerritoryList.ItemsSource = null;
                    TerritoryList.ItemsSource = Territories;

                    LogDebug($"Territory list updated with {Territories.Count} entries");
                }
                else
                {
                    LogDebug("WARNING: No territories loaded from any source!");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Critical error loading territories: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
            }
        }

        private void LoadTerritoriesFromCsv()
        {
            try
            {
                // Load CSV files directly from Sheets directory
                var territoryData = LoadCsvFile("Sheets/TerritoryType.csv");
                var placeNameData = LoadCsvFile("Sheets/PlaceName.csv");

                if (territoryData != null && placeNameData != null)
                {
                    LogDebug($"Loaded TerritoryType.csv with {territoryData.Count} rows");
                    LogDebug($"Loaded PlaceName.csv with {placeNameData.Count} rows");
                    
                    // Create lookup dictionary for place names for faster access
                    var placeNameLookup = new Dictionary<uint, string>();
                    foreach (var row in placeNameData.Skip(3)) // Skip header rows
                    {
                        if (row.Length > 1 && uint.TryParse(row[0], out uint id) && !string.IsNullOrEmpty(row[1]))
                        {
                            placeNameLookup[id] = row[1];
                        }
                    }
                    
                    LogDebug($"Created place name lookup with {placeNameLookup.Count} entries");

                    // Process territories
                    int added = 0;
                    foreach (var row in territoryData.Skip(3)) // Skip header rows
                    {
                        try
                        {
                            if (row.Length < 8 || !uint.TryParse(row[0], out uint territoryId))
                                continue;

                            string territoryNameId = row.Length > 1 ? row[1] : "";

                            // Get Region PlaceName ID from column 5 (index 4)
                            uint regionNameId = 0;
                            if (row.Length > 4 && uint.TryParse(row[4], out regionNameId)) { }

                            // Get PlaceNameId from column 7 (index 6)
                            uint placeNameId = 0;
                            if (row.Length > 6 && uint.TryParse(row[6], out placeNameId)) { }

                            // Get Map ID from column 8 (index 7)
                            uint mapId = 0;
                            if (row.Length > 7 && uint.TryParse(row[7], out mapId)) { }

                            // Look up place name
                            string placeName = "Unknown";
                            if (placeNameLookup.TryGetValue(placeNameId, out string? name) && name != null)
                            {
                                placeName = name;
                            }

                            // Look up region name
                            string regionName = "Unknown";
                            if (placeNameLookup.TryGetValue(regionNameId, out string? region) && region != null)
                            {
                                regionName = region;
                            }

                            // Add territory to collection
                            Territories.Add(new TerritoryInfo
                            {
                                Id = territoryId,
                                Name = placeName,
                                TerritoryNameId = territoryNameId,
                                PlaceNameId = placeNameId,
                                PlaceName = placeName,
                                RegionId = regionNameId,
                                RegionName = regionName,
                                Region = regionName,
                                MapId = mapId,
                                Source = "CSV",
                                PlaceNameIdTerr = mapId
                            });
                            
                            added++;
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Error processing territory row: {ex.Message}");
                        }
                    }
                    
                    LogDebug($"Added {added} territories from CSV files");
                }
                else
                {
                    LogDebug("Failed to load CSV files");
                    if (territoryData == null) LogDebug("TerritoryType.csv could not be loaded");
                    if (placeNameData == null) LogDebug("PlaceName.csv could not be loaded");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading territories from CSV: {ex.Message}");
            }
        }

        // Helper method to load CSV file - Fix for CS8603 warning
        private List<string[]>? LoadCsvFile(string filePath)
        {
            try
            {
                var result = new List<string[]>();

                // Get full path from application directory
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);
                if (!File.Exists(fullPath))
                {
                    // Try relative path
                    fullPath = filePath;
                    if (!File.Exists(fullPath))
                    {
                        StatusText.Text = $"CSV file not found: {filePath}";
                        return null; // This is intentionally returning null
                    }
                }

                using (var reader = new StreamReader(fullPath))
                {
                    string? line; // Add nullable annotation
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Handle special case for CSV with quoted fields containing commas
                        List<string> fields = new List<string>();
                        bool inQuotes = false;
                        int startIndex = 0;

                        for (int i = 0; i < line.Length; i++)
                        {
                            if (line[i] == '"')
                                inQuotes = !inQuotes;
                            else if (line[i] == ',' && !inQuotes)
                            {
                                fields.Add(line.Substring(startIndex, i - startIndex).Trim('"'));
                                startIndex = i + 1;
                            }
                        }

                        // Add the last field
                        fields.Add(line.Substring(startIndex).Trim('"'));

                        result.Add(fields.ToArray());
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading CSV file {filePath}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error loading CSV file {filePath}: {ex.Message}");
                return null; // This is intentionally returning null
            }
        }

        private void TerritoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TerritoryList.SelectedItem is TerritoryInfo selectedTerritory)
            {
                LoadTerritoryMap(selectedTerritory);
            }
        }

        // Fix for the LoadTerritoryMap method to handle nullable variables properly
        // Update the LoadTerritoryMap method to include marker loading
        private bool LoadTerritoryMap(TerritoryInfo territory)
        {
            try
            {
                _currentTerritory = territory;

                // Display territory information from our CSV-loaded data
                MapInfoText.Text = $"Region: {territory.Region}\n" +
                                  $"Place Name: {territory.PlaceName}\n" +
                                  $"Territory ID: {territory.Id}\n" +
                                  $"Territory Name ID: {territory.TerritoryNameId}\n" +
                                  $"Place Name ID: {territory.PlaceNameId}\n" +
                                  $"Map ID: {territory.MapId}\n" +
                                  $"Territory Place Name ID: {territory.PlaceNameIdTerr}";

                // Clear any previous image
                MapImageControl.Source = null;
                _currentMap = null;

                // If SaintCoinach is available, try to display the map image
                if (_realm != null)
                {
                    try
                    {
                        // First try standard SaintCoinach Map approach
                        var mapSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.Map>();
                        var map = mapSheet.FirstOrDefault(m => m.Key == territory.MapId);

                        if (map == null)
                        {
                            var availableKeys = string.Join(", ", mapSheet.Select(m => m.Key));
                            LogDebug($"Map with ID {territory.MapId} not found. Available Map keys: {availableKeys}");
                            StatusText.Text = $"Map not found for MapId: {territory.MapId}";
                            return false;
                        }

                        if (map is SaintCoinach.Ex.Relational.IRelationalRow relRow)
                        {
                            var columns = string.Join(", ", relRow.Sheet.Header.Columns.Select(c => $"{c.Index}:{c.Name}"));
                            LogDebug($"Map columns: {columns}");
                        }

                        System.Drawing.Image? mapImage = null;
                        try
                        {
                            MethodInfo? imageMethod = map.GetType().GetMethod("GetImage",
                                BindingFlags.Public | BindingFlags.Instance,
                                null, Type.EmptyTypes, null);

                            if (imageMethod != null)
                            {
                                mapImage = imageMethod.Invoke(map, null) as System.Drawing.Image;
                                System.Diagnostics.Debug.WriteLine("Got image from Map.GetImage()");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Error invoking GetImage for map {map.Key}: {ex.Message}");
                        }

                        // If we still don't have an image, try direct texture loading
                        if (mapImage == null && !string.IsNullOrEmpty(territory.TerritoryNameId))
                        {
                            System.Diagnostics.Debug.WriteLine($"Trying to load texture directly for {territory.TerritoryNameId}");
                            mapImage = LoadMapTexture(territory.TerritoryNameId);
                        }

                        // Display the image if we found it
                        if (mapImage != null)
                        {
                            // Convert Image to Bitmap to use GetHbitmap
                            using (var bitmap = new System.Drawing.Bitmap(mapImage))
                            {
                                IntPtr handle = bitmap.GetHbitmap();
                                try
                                {
                                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                        handle,
                                        IntPtr.Zero,
                                        System.Windows.Int32Rect.Empty,
                                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                                    MapImageControl.Source = bitmapSource;
                                    StatusText.Text = $"Map loaded for {territory.PlaceName}";

                                    // Apply initial scale immediately to display the map
                                    CalculateAndApplyInitialScale(bitmapSource);

                                    // Show loading indicator
                                    StatusText.Text = $"Map loaded for {territory.PlaceName} - Loading markers...";

                                    // Load markers asynchronously
                                    Task.Run(() =>
                                    {
                                        // Load markers in background
                                        var markers = _mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>();

                                        // Switch to UI thread to update display
                                        Dispatcher.BeginInvoke(() =>
                                        {
                                            _currentMapMarkers = markers;

                                            var imagePosition = new System.Windows.Point(
                                                Canvas.GetLeft(MapImageControl),
                                                Canvas.GetTop(MapImageControl)
                                            );

                                            var bitmapSource = MapImageControl.Source as System.Windows.Media.Imaging.BitmapSource;
                                            if (bitmapSource == null)
                                                return; // Fixed: just return, don't return false

                                            var imageSize = new System.Windows.Size(
                                                bitmapSource.PixelWidth,
                                                bitmapSource.PixelHeight
                                            );

                                            // Use the markers loaded above
                                            _mapRenderer?.DisplayMapMarkers(_currentMapMarkers, map, _currentScale, imagePosition, imageSize);

                                            // Update status
                                            StatusText.Text = $"Map loaded for {territory.PlaceName} - {_currentMapMarkers.Count} markers";
                                        });
                                    });
                                }
                                finally
                                {
                                    DeleteObject(handle);
                                }
                            }
                        }
                        else
                        {
                            StatusText.Text = $"Could not load map for {territory.TerritoryNameId}";
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading map image: {ex.Message}");
                        StatusText.Text = $"Error loading map: {ex.Message}";
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MapInfoText.Text = $"Error loading map: {ex.Message}";
                return false;
            }
        }

        // Simplified method to calculate and apply initial scale to fit the fixed canvas
        private void CalculateAndApplyInitialScale(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            // Reset dragging state
            _isDragging = false;

            // Fixed canvas dimensions
            const double canvasWidth = 800;
            const double canvasHeight = 600;

            // Get image dimensions
            double imageWidth = bitmapSource.PixelWidth;
            double imageHeight = bitmapSource.PixelHeight;

            System.Diagnostics.Debug.WriteLine($"Fixed Canvas: {canvasWidth}x{canvasHeight}, Image: {imageWidth}x{imageHeight}");

            // Calculate scale factors for both dimensions
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;

            // Use the smaller scale to ensure the image fits entirely within the canvas
            double fitScale = Math.Min(scaleX, scaleY);

            // Apply padding (90% of fit scale) to ensure image fits comfortably
            _currentScale = fitScale * 0.9;

            System.Diagnostics.Debug.WriteLine($"ScaleX: {scaleX:F3}, ScaleY: {scaleY:F3}, FitScale: {fitScale:F3}, Applied: {_currentScale:F3}");

            // Create a new transform group with the calculated scale
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(_currentScale, _currentScale);
            var translateTransform = new TranslateTransform(0, 0);
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            MapImageControl.RenderTransform = transformGroup;

            // Clear any explicit sizing
            MapImageControl.Width = double.NaN;
            MapImageControl.Height = double.NaN;

            // Center the image in the fixed canvas
            CenterImageInFixedCanvas(bitmapSource, canvasWidth, canvasHeight);

            // Update status to show the initial scale
            StatusText.Text = $"Map loaded - Zoom: {_currentScale:P0} (Fit: {fitScale:P0})";
        }

        // Simplified method to center the image in the fixed canvas
        private void CenterImageInFixedCanvas(System.Windows.Media.Imaging.BitmapSource bitmapSource, double canvasWidth, double canvasHeight)
        {
            // Calculate the actual image dimensions (not scaled)
            double imageWidth = bitmapSource.PixelWidth;
            double imageHeight = bitmapSource.PixelHeight;
            
            // Calculate the scaled image dimensions
            double scaledImageWidth = imageWidth * _currentScale;
            double scaledImageHeight = imageHeight * _currentScale;
            
            // Calculate center position within the canvas
            double centerX = (canvasWidth - scaledImageWidth) / 2;
            double centerY = (canvasHeight - scaledImageHeight) / 2;
            
            System.Diagnostics.Debug.WriteLine($"Canvas: {canvasWidth}x{canvasHeight}");
            System.Diagnostics.Debug.WriteLine($"Image: {imageWidth}x{imageHeight}, Scaled: {scaledImageWidth:F1}x{scaledImageHeight:F1}");
            System.Diagnostics.Debug.WriteLine($"Center position: {centerX:F1}, {centerY:F1}");
            
            // Update Canvas position - this positions the top-left corner of the image
            Canvas.SetLeft(MapImageControl, Math.Max(0, centerX));
            Canvas.SetTop(MapImageControl, Math.Max(0, centerY));
            
            // ADD THIS: Ensure overlay canvas is positioned identically
            Canvas.SetLeft(OverlayCanvas, Canvas.GetLeft(MapImageControl));
            Canvas.SetTop(OverlayCanvas, Canvas.GetTop(MapImageControl));
            
            System.Diagnostics.Debug.WriteLine($"Final position: Left={Math.Max(0, centerX):F1}, Top={Math.Max(0, centerY):F1}");
        }

        // Simplified mouse wheel method for fixed canvas
        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MapImageControl.Source == null)
                return;

            // Calculate new scale based on wheel direction
            double zoomFactor = e.Delta > 0 ? _zoomIncrement : -_zoomIncrement;
            double newScale = _currentScale + zoomFactor;

            // Apply bounds to the scale - allow zooming out to 5% and up to 500%
            newScale = Math.Max(0.05, Math.Min(5.0, newScale));

            // If scale hasn't changed (due to min/max bounds), return
            if (Math.Abs(newScale - _currentScale) < 0.01)
                return;

            // Update the current scale
            _currentScale = newScale;
            
            // Use the ApplyZoomAndPan method to keep canvases in sync
            if (newScale <= 1.0)
            {
                // Re-center the image when zoomed out
                if (MapImageControl.Source is System.Windows.Media.Imaging.BitmapSource bitmapSource)
                {
                    CenterImageInFixedCanvas(bitmapSource, 800, 600);
                }
            }
            else
            {
                // Just update transforms for both canvases
                ApplyZoomAndPan(_currentScale, new System.Windows.Point(0, 0));
                
                // Ensure overlay canvas position matches map canvas
                Canvas.SetLeft(OverlayCanvas, Canvas.GetLeft(MapImageControl));
                Canvas.SetTop(OverlayCanvas, Canvas.GetTop(MapImageControl));
            }

            // Update status text to show current zoom level
            StatusText.Text = $"Zoom: {_currentScale:P0}";

            e.Handled = true;
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source != null && _currentScale > 0.15)
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
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point currentPosition = e.GetPosition(MapCanvas);

                // Calculate the movement delta
                Vector delta = currentPosition - _lastMousePosition;

                // Get the TranslateTransform from the transform group
                if (MapImageControl.RenderTransform is TransformGroup transformGroup)
                {
                    var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                    if (translateTransform != null)
                    {
                        // Apply the translation
                        translateTransform.X += delta.X;
                        translateTransform.Y += delta.Y;
                    }
                }
                else if (MapImageControl.RenderTransform is TranslateTransform translateTransform)
                {
                    // Apply the translation directly if it's a single transform
                    translateTransform.X += delta.X;
                    translateTransform.Y += delta.Y;
                }

                // ADD THIS: Ensure overlay canvas position/transform matches the map
                if (OverlayCanvas.RenderTransform is TransformGroup overlayTransformGroup)
                {
                    var overlayTranslateTransform = overlayTransformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                    if (overlayTranslateTransform != null)
                    {
                        overlayTranslateTransform.X += delta.X;
                        overlayTranslateTransform.Y += delta.Y;
                    }
                }
                else if (OverlayCanvas.RenderTransform is TranslateTransform overlayTranslateTransform)
                {
                    overlayTranslateTransform.X += delta.X;
                    overlayTranslateTransform.Y += delta.Y;
                }
                
                // Also synchronize the Canvas.Left/Top properties
                Canvas.SetLeft(OverlayCanvas, Canvas.GetLeft(MapImageControl));
                Canvas.SetTop(OverlayCanvas, Canvas.GetTop(MapImageControl));
                
                // Save the current position for the next move
                _lastMousePosition = currentPosition;
                e.Handled = true;
            }
        }

        // Fix for CS8603 warnings in LoadMapTexture
        private System.Drawing.Image? LoadMapTexture(string territoryNameId)
        {
            if (_realm == null) return null;

            try
            {
                // Define the possible subdirectories to check
                string[] subDirectories = { "00", "01", "02" };

                // Try each subdirectory systematically
                foreach (string subDir in subDirectories)
                {
                    string mapPath = $"ui/map/{territoryNameId}/{subDir}/{territoryNameId}{subDir}_m.tex";
                    System.Diagnostics.Debug.WriteLine($"Trying to load map texture from: {mapPath}");

                    // Check if file exists and try to load it
                    if (_realm.Packs.FileExists(mapPath))
                    {
                        var file = _realm.Packs.GetFile(mapPath);
                        System.Diagnostics.Debug.WriteLine($"Found map file: {file}");

                        // SaintCoinach should handle the conversion of the texture
                        var imageField = file.GetType().GetProperty("Image") ??
                                        file.GetType().GetProperty("GetImage");

                        if (imageField != null)
                        {
                            var image = imageField.GetValue(file) as System.Drawing.Image;
                            if (image != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded texture from {mapPath}");
                                return image;
                            }
                        }

                        // Try using GetImage method instead of a property
                        var imageMethod = file.GetType().GetMethod("GetImage");
                        if (imageMethod != null)
                        {
                            var image = imageMethod.Invoke(file, null) as System.Drawing.Image;
                            if (image != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded texture from {mapPath}");
                                return image;
                            }
                        }

                        System.Diagnostics.Debug.WriteLine("Could not find Image property or GetImage method");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Map texture file not found at: {mapPath}");
                    }
                }

                // If no texture found in the standard directories, try alternative fallback paths
                System.Diagnostics.Debug.WriteLine("Trying fallback alternative paths...");
                string[] alternativePaths = {
                    $"ui/map/{territoryNameId}/{territoryNameId}_m.tex",
                    $"ui/map/{territoryNameId}.tex",
                    $"ui/map/{territoryNameId}/m.tex"
                };

                foreach (var path in alternativePaths)
                {
                    System.Diagnostics.Debug.WriteLine($"Trying alternative path: {path}");
                    if (_realm.Packs.FileExists(path))
                    {
                        var file = _realm.Packs.GetFile(path);
                        System.Diagnostics.Debug.WriteLine($"Found file at alternative path: {file}");

                        // Try getting image
                        var imageMethod = file.GetType().GetMethod("GetImage");
                        if (imageMethod != null)
                        {
                            var image = imageMethod.Invoke(file, null) as System.Drawing.Image;
                            if (image != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded texture from alternative path {path}");
                                return image;
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"No texture found for territory: {territoryNameId}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading map texture: {ex.Message}");
                return null;
            }
        }

        // Add this P/Invoke declaration at the class level
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // Add right-click handler for coordinates
        public void LogDebug(string message)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogDebug(message));
                return;
            }

            // Add timestamp to message
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            
            // Append the message with a newline
            DebugTextBox.AppendText(timestampedMessage + Environment.NewLine);
            
            // Trim log if it gets too large
            if (DebugTextBox.LineCount > MaxLogLines)
            {
                int linesToRemove = DebugTextBox.LineCount - MaxLogLines;
                int charsToRemove = DebugTextBox.GetCharacterIndexFromLineIndex(linesToRemove);
                DebugTextBox.Text = DebugTextBox.Text.Substring(charsToRemove);
            }
            
            // Auto-scroll to the end if enabled
            if (AutoScrollCheckBox != null && AutoScrollCheckBox.IsChecked == true)
            {
                DebugScrollViewer.ScrollToEnd();
            }
        }
        
        // Button handlers
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

        // Add this method to hook into Debug.WriteLine calls
        private void RedirectDebugOutput()
        {
            // Create a trace listener that logs to our debug box
            System.Diagnostics.Trace.Listeners.Add(new TextBoxTraceListener(this));
        }

        string SafeGetPlaceName(SaintCoinach.Xiv.PlaceName? pn)
        {
            if (pn == null) return "Unknown";
            try
            {
                // Access the name by index 0 in the PlaceName sheet
                if (pn is SaintCoinach.Ex.Relational.IRelationalRow relRow)
                {
                    var nameValue = relRow[0];
                    return nameValue?.ToString() ?? "Unknown";
                }
                // Fallback to property if not a relational row
                return pn.Name ?? "Unknown";
            }
            catch (Exception ex)
            {
                if (pn is SaintCoinach.Ex.Relational.IRelationalRow relRow)
                {
                    var columns = string.Join(", ", relRow.Sheet.Header.Columns.Select(c => $"{c.Index}:{c.Name}"));
                    LogDebug($"Map columns: {columns}");
                }
                return $"[Missing Name for PlaceName {pn.Key}]";
            }
        }
         
        private void ApplyZoomAndPan(double scale, System.Windows.Point position)
        {
            // Apply same transform to both canvases
            ScaleTransform scaleTransform = new ScaleTransform(scale, scale);
            TranslateTransform translateTransform = new TranslateTransform(position.X, position.Y);
            
            TransformGroup transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            
            MapCanvas.RenderTransform = transformGroup;
            OverlayCanvas.RenderTransform = transformGroup;
        }
    }

    // Custom trace listener to redirect Debug.WriteLine to our TextBox
    public class TextBoxTraceListener : System.Diagnostics.TraceListener
    {
        private readonly MainWindow _window;
        
        public TextBoxTraceListener(MainWindow window)
        {
            _window = window;
        }
        
        public override void Write(string message)
        {
            // Accumulate message (will be written on WriteLine)
        }
        
        public override void WriteLine(string message)
        {
            _window.LogDebug(message);
        }
    }

    // Helper class to represent territory information
    public class TerritoryInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TerritoryNameId { get; set; } = string.Empty;
        public uint PlaceNameId { get; set; }  // New property to store the numeric ID
        public string PlaceName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public string Region { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public uint PlaceNameIdTerr { get; set; } // Territory's place name ID from column 7
        public uint RegionId { get; set; } // Add region ID
        public string RegionName { get; set; } = string.Empty; // Add region name

        // Format with territory ID and territory name ID for the list
        public override string ToString() => $"{Id} {TerritoryNameId} {PlaceName}";
    }
}