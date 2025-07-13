using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Xiv;
using SaintCoinach.Xiv.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

using WpfPanel = System.Windows.Controls.Panel;
using WpfColor = System.Windows.Media.Color;
using System.Drawing.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace map_editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Update class fields
        private ARealmReversed? _realm;
        private MapService? _mapService;
        private MapRenderer? _mapRenderer;
        private SaintCoinach.Xiv.Map? _currentMap;
        private TerritoryInfo? _currentTerritory;
        private List<MapMarker> _currentMapMarkers = new();
        private double _currentScale = 1.0;
        private System.Windows.Point _lastMousePosition;
        private bool _isDragging = false;

        private bool _debugLoggingEnabled = false; // Add this field at the class level

        public ObservableCollection<TerritoryInfo> Territories { get; set; } = new ObservableCollection<TerritoryInfo>();

        private const int MaxLogLines = 500; // Maximum number of log lines to keep

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Fix constructor to use the XAML elements correctly
            _mapRenderer = new MapRenderer(_realm);

            // Set up debug logging
            RedirectDebugOutput();
            LogDebug("Application started");

            // Subscribe to the Loaded event
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Simply pass the overlay canvas from the XAML to the renderer
            if (_mapRenderer != null)
            {
                _mapRenderer.SetOverlayCanvas(OverlayCanvas);
                _mapRenderer.SetMainWindow(this); // Add this line
                LogDebug("OverlayCanvas from XAML is now set in MapRenderer.");
            }

            // Ensure the overlay canvas doesn't block mouse events to child elements
            if (OverlayCanvas != null)
            {
                OverlayCanvas.IsHitTestVisible = true;
                OverlayCanvas.Background = null; // Transparent background
            }

            // If a map was somehow loaded before this point, ensure it's scaled correctly
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

        private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MapImageControl.Source == null || _mapService == null || _mapRenderer == null)
                return;

            System.Windows.Point clickPoint = e.GetPosition(MapCanvas);
            var imagePosition = new System.Windows.Point(0, 0); // Always (0,0) since we use transforms

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

                StatusText.Text = $"Loaded {Territories.Count} territories from: {gameDirectory}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading data: {ex.Message}";
                LogDebug($"Error loading FFXIV data: {ex.Message}\n{ex.StackTrace}");
                WpfMessageBox.Show($"Failed to load FFXIV data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MapImageControl.Source == null) return;

            // Get mouse position relative to the canvas
            var mousePos = e.GetPosition(MapCanvas);

            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            double newScale = _currentScale * zoomFactor;
            newScale = Math.Clamp(newScale, 0.1, 10.0);

            // Get the current transform
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
                // Calculate zoom to mouse position
                double scaleDelta = newScale / _currentScale;

                // Adjust translation to zoom towards mouse position
                translateTransform.X = mousePos.X - (mousePos.X - translateTransform.X) * scaleDelta;
                translateTransform.Y = mousePos.Y - (mousePos.Y - translateTransform.Y) * scaleDelta;

                // Apply new scale
                scaleTransform.ScaleX = newScale;
                scaleTransform.ScaleY = newScale;
            }

            _currentScale = newScale;
            StatusText.Text = $"Zoom: {_currentScale:P0}";
            SyncOverlayWithMap();
            e.Handled = true;

            // Refresh markers if needed
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
                
                // Only sync the overlay transform, don't refresh markers during drag
                SyncOverlayWithMap();
                // REMOVED: RefreshMarkers();
            }
            else if (!_isDragging)
            {
                // Hover detection logic
                var mousePosition = e.GetPosition(MapCanvas);
                
                // Pass mouse position to MapRenderer for hover detection
                _mapRenderer?.HandleMouseMove(mousePosition, _currentMap);
                
                // For debugging: log cursor position occasionally 
                if (_debugLoggingEnabled && DateTime.Now.Millisecond < 50) // ~5% of the time
                {
                    // Get the image position from the transform
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
            Territories.Clear();

            try
            {
                if (_realm != null)
                {
                    LogDebug("Loading territories from SaintCoinach...");
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                    foreach (var territory in territorySheet)
                    {
                        try
                        {
                            // Defensively get all required properties.
                            string placeName;
                            uint placeNameId = 0;

                            if (territory.PlaceName != null && !string.IsNullOrWhiteSpace(territory.PlaceName.Name))
                            {
                                placeName = territory.PlaceName.Name.ToString();
                                placeNameId = (uint)territory.PlaceName.Key;
                            }
                            else
                            {
                                // Provide a fallback for territories without a proper name
                                placeName = $"[Territory ID: {territory.Key}]";
                                // placeNameId remains 0
                            }

                            string territoryNameId = territory.Name?.ToString() ?? string.Empty;

                            // This is the critical fix: Defensively access RegionPlaceName,
                            // and fall back to ZonePlaceName for instanced content.
                            string regionName = "Unknown";
                            uint regionId = 0;
                            bool regionFound = false;

                            // 1. Try to get RegionPlaceName first.
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
                            catch (KeyNotFoundException) { /* This is expected, so we continue to the fallback. */ }

                            // 2. If no region was found, fall back to ZonePlaceName.
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
                                            LogDebug($"Territory {territory.Key} ('{placeName}') using ZonePlaceName '{regionName}' as fallback.");
                                        }
                                    }
                                }
                                catch (KeyNotFoundException)
                                {
                                    // This can also fail, in which case we stick with "Unknown".
                                    LogDebug($"Territory {territory.Key} ('{placeName}') has no Region or Zone. Defaulting to 'Unknown'.");
                                }
                            }

                            // Defensively get the map key.
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
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Failed to process territory with key {territory.Key}. Error: {ex.Message}");
                        }
                    }
                }

                if (Territories.Count == 0)
                {
                    LogDebug("No territories loaded from SaintCoinach, trying CSV files...");
                    LoadTerritoriesFromCsv();
                }

                if (Territories.Any())
                {
                    var sortedTerritories = Territories.OrderBy(t => t.Id).ToList();
                    Territories.Clear();
                    foreach (var t in sortedTerritories)
                    {
                        Territories.Add(t);
                    }

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

                    Territories.Add(new TerritoryInfo
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
                        Source = "CSV"
                    });
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
                    // Show loading indication
                    StatusText.Text = $"Loading map for {selectedTerritory.PlaceName}...";

                    // Use the async version
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

                // Load the map image
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

                        // Set image source and ensure it's part of the canvas
                        MapImageControl.Source = bitmapSource;

                        if (!MapCanvas.Children.Contains(MapImageControl))
                        {
                            MapCanvas.Children.Insert(0, MapImageControl);
                            LogDebug("Added MapImageControl to MapCanvas");
                        }

                        // Apply scaling and positioning
                        CalculateAndApplyInitialScale(bitmapSource);

                        // Hide placeholder text
                        MapPlaceholderText.Visibility = Visibility.Collapsed;

                        StatusText.Text = $"Map loaded for {territory.PlaceName}. Loading markers...";

                        // Load markers on a background thread
                        List<MapMarker> markers = await Task.Run(() =>
                            _mapService?.LoadMapMarkers(territory.MapId) ?? new List<MapMarker>()
                        );

                        // Back on UI thread
                        _currentMapMarkers = markers;

                        // Always use (0,0) as image position since positioning is handled by transform
                        var imagePosition = new System.Windows.Point(0, 0);
                        var imageSize = new System.Windows.Size(
                            bitmapSource.PixelWidth,
                            bitmapSource.PixelHeight);

                        // Display markers
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

            // Calculate scale to fit the image within the canvas
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double fitScale = Math.Min(scaleX, scaleY);
            _currentScale = fitScale * 0.9; // Slightly smaller to leave margin

            // Center the image within the canvas using transforms ONLY
            double centeredX = (canvasWidth - (imageWidth * _currentScale)) / 2;
            double centeredY = (canvasHeight - (imageHeight * _currentScale)) / 2;

            // Configure map image - Set position to 0,0 and use transform for centering
            MapImageControl.Width = imageWidth;
            MapImageControl.Height = imageHeight;
            Canvas.SetLeft(MapImageControl, 0); // Always 0
            Canvas.SetTop(MapImageControl, 0);  // Always 0
            Canvas.SetZIndex(MapImageControl, 0); // Ensure map is behind markers
            MapImageControl.Visibility = Visibility.Visible;

            // Create transform for scaling AND positioning
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_currentScale, _currentScale));
            transformGroup.Children.Add(new TranslateTransform(centeredX, centeredY)); // Use transform for centering
            MapImageControl.RenderTransform = transformGroup;
            MapImageControl.RenderTransformOrigin = new System.Windows.Point(0, 0);

            LogDebug($"Map scaled to {_currentScale:F2} and positioned via transform at ({centeredX:F1}, {centeredY:F1})");
        }

        private void RefreshMarkers()
        {
            if (_mapRenderer == null || _currentMap == null) return;

            // Always use (0,0) as image position since positioning is handled by transform
            var imagePosition = new System.Windows.Point(0, 0);

            if (MapImageControl.Source is BitmapSource bitmapSource)
            {
                var imageSize = new System.Windows.Size(
                    bitmapSource.PixelWidth,
                    bitmapSource.PixelHeight
                );

                _mapRenderer?.DisplayMapMarkers(_currentMapMarkers, _currentMap, _currentScale, imagePosition, imageSize);
                SyncOverlayWithMap();

                // Add a debug message to confirm markers were refreshed
                LogDebug($"Refreshed {_currentMapMarkers.Count} markers on map");
            }
        }

        // Add this method to diagnose map display issues
        private void DiagnoseMapDisplay()
        {
            LogDebug("=== MAP DISPLAY DIAGNOSTIC ===");

            // Check map image
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

            // Check if the transform is applied correctly
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

            // Fix the ambiguous Panel references in DiagnoseMapVisibility method
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

            // Force MapImageControl to be visible if it isn't already
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

            // Check if overlay is attached to the visual tree
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

            // Check transform
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
            // Intentionally left blank
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
        public string Source { get; set; } = string.Empty;
        public uint PlaceNameIdTerr { get; set; }
        public uint RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;

        public override string ToString() => $"{Id} {TerritoryNameId} {PlaceName}";
    }
}