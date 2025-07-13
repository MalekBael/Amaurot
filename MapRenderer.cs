using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.IO;
using SaintCoinach;
using SaintCoinach.Xiv;
using Application = System.Windows.Application;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfBrushes = System.Windows.Media.Brushes;

namespace map_editor
{
    public class MapRenderer
    {
        private readonly List<UIElement> _markerElements = new();
        private ARealmReversed? _realm;
        private Canvas? _overlayCanvas;
        private Map? _currentMap;
        private bool _isUpdatingMarkers = false;
        private DispatcherTimer? _markerUpdateTimer;
        private List<MapMarker>? _pendingMarkers;
        private WpfPoint _lastImagePosition;
        private WpfSize _lastImageSize;
        private double _lastScale;

        // Add debugging options to control verbosity
        private bool _verboseLogging = false;
        private bool _showDebugVisuals = false;
        private int _debugLogCounter = 0;
        private const int LoggingThreshold = 50; // Only log every Nth marker

        private long _lastUpdateTimestamp = 0;
        private readonly object _updateLock = new object();

        // Add these fields to the MapRenderer class
        private MapMarker? _hoveredMarker;
        private UIElement? _hoveredElement;
        private bool _isHoverEnabled = true;
        private double _lastHoverCheckTime;
        private const double HOVER_CHECK_THROTTLE_MS = 50; // Only check every 50ms to improve performance

        // Add a reference to the main window for updating the UI
        private Window? _mainWindow;

        public MapRenderer(ARealmReversed? realm = null)
        {
            _realm = realm;
            _markerUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _markerUpdateTimer.Tick += OnMarkerUpdateTimerTick;
        }

        // Add methods to control debug features
        public void EnableVerboseLogging(bool enable) => _verboseLogging = enable;
        public void EnableDebugVisuals(bool enable) => _showDebugVisuals = enable;

        private void OnMarkerUpdateTimerTick(object? sender, EventArgs e)
        {
            if (_markerUpdateTimer != null)
            {
                _markerUpdateTimer.Stop();
            }

            if (_pendingMarkers != null && _currentMap != null && !_isUpdatingMarkers)
            {
                UpdateMarkersInternal(_pendingMarkers, _currentMap, _lastScale, _lastImagePosition, _lastImageSize);
                _pendingMarkers = null;
            }
        }

        public void SetOverlayCanvas(Canvas canvas)
        {
            _overlayCanvas = canvas;
        }

        public void UpdateRealm(ARealmReversed realm)
        {
            _realm = realm;
        }

        public void UpdateMap(Map map)
        {
            _currentMap = map;
        }

        public void ClearMarkers()
        {
            if (_overlayCanvas != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _overlayCanvas.Children.Clear();
                    _markerElements.Clear();
                }, DispatcherPriority.Background);
            }
        }

        public void DisplayMapMarkers(List<MapMarker> markers, Map map, double scale,
                                      WpfPoint imagePosition, WpfSize imageSize)
        {
            // Only verify parameters if debug visuals are enabled
            if (_showDebugVisuals)
            {
                VerifyImageParameters(imagePosition, imageSize);
            }

            lock (_updateLock)
            {
                // Store latest values
                _lastImagePosition = imagePosition;
                _lastImageSize = imageSize;
                _lastScale = scale;

                // Store markers and map
                _pendingMarkers = new List<MapMarker>(markers);
                _currentMap = map;

                // If we're already updating markers, skip this update request
                if (_isUpdatingMarkers)
                {
                    return;
                }

                // Throttle updates to no more than 3 per second during panning/zooming
                long currentTime = Environment.TickCount64;
                if (currentTime - _lastUpdateTimestamp < 333) // ~3 updates per second max
                {
                    // Schedule update via timer instead
                    if (_markerUpdateTimer != null && !_markerUpdateTimer.IsEnabled)
                    {
                        _markerUpdateTimer.Start();
                    }
                    return;
                }

                _lastUpdateTimestamp = currentTime;

                // Process the update
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    UpdateMarkersInternal(markers, map, scale, imagePosition, imageSize);
                }, DispatcherPriority.Background);
            }
        }

        private void UpdateMarkersInternal(List<MapMarker> markers, Map map, double scale,
                                          WpfPoint imagePosition, WpfSize imageSize)
        {
            try
            {
                _isUpdatingMarkers = true;

                // Only log this information if verbose logging is enabled
                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Updating {markers.Count} markers at scale {scale:F2}");
                }

                // Clear markers on UI thread but don't wait
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_overlayCanvas != null)
                    {
                        _overlayCanvas.Children.Clear();
                        _markerElements.Clear();
                    }
                }, DispatcherPriority.Background);

                if (_overlayCanvas == null || markers == null || !markers.Any())
                {
                    return;
                }

                // Filter to visible markers first for efficiency
                var visibleMarkers = markers.Where(m => m.IsVisible).ToList();

                // Prepare a batch of elements to add
                var elementsToAdd = new List<UIElement>();
                int displayCount = 0;
                _debugLogCounter = 0;

                // Create markers but limit the number during extreme zooms
                int maxMarkers = scale < 0.5 ? Math.Min(visibleMarkers.Count, 50) : visibleMarkers.Count;
                foreach (var marker in visibleMarkers.Take(maxMarkers))
                {
                    try
                    {
                        var markerElement = CreateMarkerElement(marker, scale, imagePosition, imageSize);
                        if (markerElement != null)
                        {
                            elementsToAdd.Add(markerElement);
                            _markerElements.Add(markerElement);
                            displayCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verboseLogging)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error with marker {marker.Id}: {ex.Message}");
                        }
                    }
                }

                // Add all elements in a single UI batch
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_overlayCanvas != null)
                    {
                        foreach (var element in elementsToAdd)
                        {
                            _overlayCanvas.Children.Add(element);
                        }
                    }

                    // Log summary information instead of detailed per-marker logs
                    if (_verboseLogging || displayCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Added {displayCount} markers to overlay");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating markers: {ex.Message}");
            }
            finally
            {
                _isUpdatingMarkers = false;
            }
        }

        private UIElement? CreateMarkerElement(MapMarker marker, double displayScale,
    WpfPoint imagePosition, WpfSize imageSize)
        {
            try
            {
                // Get map-specific scale values and offsets from FFXIV data
                float sizeFactor = 200.0f;
                float offsetX = 0;
                float offsetY = 0;

                if (_currentMap != null)
                {
                    try
                    {
                        var type = _currentMap.GetType();
                        var indexer = type.GetProperty("Item", new[] { typeof(string) });

                        if (indexer != null)
                        {
                            sizeFactor = (float)Convert.ChangeType(indexer.GetValue(_currentMap, new object[] { "SizeFactor" }), typeof(float));
                            offsetX = (float)Convert.ChangeType(indexer.GetValue(_currentMap, new object[] { "OffsetX" }), typeof(float));
                            offsetY = (float)Convert.ChangeType(indexer.GetValue(_currentMap, new object[] { "OffsetY" }), typeof(float));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verboseLogging)
                        {
                            System.Diagnostics.Debug.WriteLine($"Property access error: {ex.Message}");
                        }
                    }
                }

                // Coordinate calculations
                double normalizedX = (marker.X + offsetX) / 2048.0;
                double normalizedY = (marker.Y + offsetY) / 2048.0;
                double pixelX = normalizedX * imageSize.Width;
                double pixelY = normalizedY * imageSize.Height;
                double canvasX = imagePosition.X + pixelX;
                double canvasY = imagePosition.Y + pixelY;
                double c = sizeFactor / 100.0;
                double gameX = (41.0 / c) * (normalizedX) + 1.0;
                double gameY = (41.0 / c) * (normalizedY) + 1.0;

                // Only log occasionally when verbose logging is enabled
                if (_verboseLogging && ++_debugLogCounter % LoggingThreshold == 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Marker {marker.Id}: Raw({marker.X},{marker.Y}), " +
                        $"Game({gameX:F2},{gameY:F2}), " +
                        $"Norm({normalizedX:F3},{normalizedY:F3}), " +
                        $"Canvas({canvasX:F1},{canvasY:F1})");
                }

                // Create visible marker - appropriate size based on zoom level
                double markerSize = Math.Max(30, 15 * displayScale);

                // Ensure marker type is determined
                marker.DetermineType();

                // Initialize markerElement to null to avoid CS0165
                UIElement? markerElement = null;

                // Try to create an icon-based marker if the icon ID is valid
                if (marker.IconId > 0 && _realm != null && _currentMap != null)
                {
                    try
                    {
                        var iconImage = new System.Windows.Controls.Image
                        {
                            Width = markerSize,
                            Height = markerSize,
                            Stretch = Stretch.Uniform,
                            Tag = marker,
                            Opacity = 1.0
                        };

                        // Apply high-quality rendering
                        RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.HighQuality);

                        // Format icon path
                        string iconFolder = $"{marker.IconId / 1000 * 1000:D6}";
                        string iconPath = $"ui/icon/{iconFolder}/{marker.IconId:D6}.tex";

                        // Try to access the icon file through the map's collection
                        bool iconLoaded = false;

                        try
                        {
                            // Use _currentMap to access the file if possible
                            if (_currentMap.Sheet?.Collection?.PackCollection != null)
                            {
                                var packCollection = _currentMap.Sheet.Collection.PackCollection;
                                if (packCollection.TryGetFile(iconPath, out var file))
                                {
                                    var imageFile = new SaintCoinach.Imaging.ImageFile(file.Pack, file.CommonHeader);
                                    using var stream = new MemoryStream();
                                    var img = imageFile.GetImage();
                                    img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                                    stream.Position = 0;

                                    var bitmapImage = new BitmapImage();
                                    bitmapImage.BeginInit();
                                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmapImage.StreamSource = stream;
                                    bitmapImage.EndInit();
                                    bitmapImage.Freeze(); // Important for cross-thread use

                                    iconImage.Source = bitmapImage;
                                    markerElement = iconImage;
                                    iconLoaded = true;
                                    
                                    if (_verboseLogging)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Successfully loaded icon {marker.IconId} for marker {marker.Id}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_verboseLogging)
                            {
                                System.Diagnostics.Debug.WriteLine($"Icon loading error for marker {marker.Id}: {ex.Message}");
                            }
                        }

                        // If icon wasn't loaded, create fallback shape
                        if (!iconLoaded)
                        {
                            if (_verboseLogging)
                            {
                                System.Diagnostics.Debug.WriteLine($"Using fallback shape for marker {marker.Id} (icon {marker.IconId} not found)");
                            }
                            markerElement = CreateFallbackShape(marker, markerSize);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verboseLogging)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to load icon for marker {marker.Id}: {ex.Message}");
                        }

                        // Use fallback shape on any error
                        markerElement = CreateFallbackShape(marker, markerSize);
                    }
                }
                else
                {
                    // No icon ID or realm, use fallback
                    if (_verboseLogging && marker.IconId == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Marker {marker.Id} has no icon ID, using fallback shape");
                    }
                    markerElement = CreateFallbackShape(marker, markerSize);
                }

                // Make sure markerElement is always assigned before using it
                if (markerElement == null)
                {
                    markerElement = CreateFallbackShape(marker, markerSize);
                }

                // Position centered on coordinates
                Canvas.SetLeft(markerElement, canvasX - (markerSize / 2));
                Canvas.SetTop(markerElement, canvasY - (markerSize / 2));
                Canvas.SetZIndex(markerElement, 3000);

                // Enhanced tooltip with more information
                if (markerElement is FrameworkElement fe)
                {
                    // Get the actual map marker range from the Map data
                    uint mapMarkerRange = 0;
                    if (_currentMap != null)
                    {
                        try
                        {
                            var type = _currentMap.GetType();
                            var indexer = type.GetProperty("Item", new[] { typeof(string) });
                            if (indexer != null)
                            {
                                mapMarkerRange = (uint)Convert.ChangeType(indexer.GetValue(_currentMap, new object[] { "MapMarkerRange" }), typeof(uint));
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_verboseLogging)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to get MapMarkerRange: {ex.Message}");
                            }
                        }
                    }

                    // Create rich tooltip content
                    var tooltipContent = new StackPanel { Margin = new Thickness(5) };

                    // Add marker name with bold formatting
                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = marker.PlaceName,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 5)
                    });

                    // Add divider
                    tooltipContent.Children.Add(new Separator
                    {
                        Margin = new Thickness(0, 2, 0, 5)
                    });

                    // Add detailed information
                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = $"Map Marker Range: {mapMarkerRange}\nRow: {mapMarkerRange}.{marker.Id}\nPosition: X={marker.X:F0}, Y={marker.Y:F0}\nGame Coords: X={gameX:F1}, Y={gameY:F1}\nIcon ID: {marker.IconId}\nMarker ID: {marker.Id}\nType: {marker.Type}"
                    });

                    // Make tooltips appear with mouse - important changes here:
                    var tooltip = new System.Windows.Controls.ToolTip
                    {
                        Content = tooltipContent,
                        Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                        HorizontalOffset = 15,
                        VerticalOffset = 10,
                        HasDropShadow = true
                    };

                    // IMPORTANT: Add mouse event handlers for hover detection
                    fe.MouseEnter += (s, e) =>
                    {
                        if (_isHoverEnabled && !_isUpdatingMarkers)
                        {
                            _hoveredMarker = marker;
                            _hoveredElement = fe;
                            ApplyHoverEffect(fe);

                            if (_verboseLogging)
                            {
                                System.Diagnostics.Debug.WriteLine($"Mouse entered marker: {marker.PlaceName}");
                            }
                        }
                    };

                    fe.MouseLeave += (s, e) =>
                    {
                        if (_isHoverEnabled && _hoveredElement == fe)
                        {
                            ClearHoverState();

                            if (_verboseLogging)
                            {
                                System.Diagnostics.Debug.WriteLine($"Mouse left marker: {marker.PlaceName}");
                            }
                        }
                    };

                    // Ensure the element can receive mouse events
                    fe.IsHitTestVisible = true;

                    // Add click handler to show more details
                    fe.MouseLeftButtonDown += (s, e) =>
                    {
                        // Prevent the event from bubbling up to the canvas
                        e.Handled = true;

                        // Show a more detailed popup or window with marker info
                        ShowMarkerDetailsPopup(marker, new WpfPoint(
                            canvasX,
                            canvasY - markerSize
                        ));
                    };
                }

                // Add a debug border to visualize the hit-test area if enabled
                if (markerElement is FrameworkElement frameElement)
                {
                    AddDebugBorderToElement(frameElement, marker);
                }

                return markerElement;
            }
            catch (Exception ex)
            {
                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating marker {marker.Id}: {ex.Message}");
                }
                return null;
            }
        }

        // Add this new method to show a detailed popup when a marker is clicked
        private void ShowMarkerDetailsPopup(MapMarker marker, WpfPoint position)
        {
            if (_mainWindow == null) return;

            // Get the actual map marker range from the Map data
            uint mapMarkerRange = 0;
            if (_currentMap != null)
            {
                try
                {
                    var type = _currentMap.GetType();
                    var indexer = type.GetProperty("Item", new[] { typeof(string) });
                    if (indexer != null)
                    {
                        mapMarkerRange = (uint)Convert.ChangeType(indexer.GetValue(_currentMap, new object[] { "MapMarkerRange" }), typeof(uint));
                    }
                }
                catch (Exception ex)
                {
                    if (_verboseLogging)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to get MapMarkerRange: {ex.Message}");
                    }
                }
            }

            // Instead of creating a popup, update the marker information panel
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // Find the UI elements by name
                var markerInfoBorder = _mainWindow.FindName("MarkerInfoBorder") as Border;
                var markerNameText = _mainWindow.FindName("MarkerNameText") as TextBlock;
                var markerSubtitleText = _mainWindow.FindName("MarkerSubtitleText") as TextBlock;
                var markerDetailsGrid = _mainWindow.FindName("MarkerDetailsGrid") as Grid;
                var noMarkerSelectedText = _mainWindow.FindName("NoMarkerSelectedText") as TextBlock;

                if (markerInfoBorder == null || markerNameText == null || markerDetailsGrid == null || noMarkerSelectedText == null)
                    return;

                // Show the marker info panel and hide the "no marker selected" text
                markerInfoBorder.Visibility = Visibility.Visible;
                noMarkerSelectedText.Visibility = Visibility.Collapsed;

                // Update the marker name
                markerNameText.Text = marker.PlaceName;

                // Show subtitle if needed (you can customize this based on your data)
                if (markerSubtitleText != null)
                {
                    // Example: Show marker type as subtitle
                    if (marker.Type != MarkerType.Generic)
                    {
                        markerSubtitleText.Text = $"({marker.Type})";
                        markerSubtitleText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        markerSubtitleText.Visibility = Visibility.Collapsed;
                    }
                }

                // Clear and rebuild the details grid
                markerDetailsGrid.RowDefinitions.Clear();
                markerDetailsGrid.Children.Clear();

                // Add detail rows
                AddDetailRowToGrid(markerDetailsGrid, 0, "Map Marker Range:", $"{mapMarkerRange}");
                AddDetailRowToGrid(markerDetailsGrid, 1, "MapMarker Row:", $"{mapMarkerRange}.{marker.Id}");
                AddDetailRowToGrid(markerDetailsGrid, 2, "Raw Position:", $"X={marker.X:F0}, Y={marker.Y:F0}");
                AddDetailRowToGrid(markerDetailsGrid, 3, "Icon ID:", $"{marker.IconId}");
                AddDetailRowToGrid(markerDetailsGrid, 4, "Marker ID:", $"{marker.Id}");
                AddDetailRowToGrid(markerDetailsGrid, 5, "Place Name ID:", $"{marker.PlaceNameId}");
                AddDetailRowToGrid(markerDetailsGrid, 6, "Type:", $"{marker.Type}");

                // Log if verbose logging is enabled
                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Updated marker information panel for: {marker.PlaceName}");
                }
            });
        }

        // Helper method to add rows to the grid
        private void AddDetailRowToGrid(Grid grid, int row, string label, string value)
        {
            // Add row definition
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Create label
            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            // Create value
            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
        }

        private UIElement CreateFallbackShape(MapMarker marker, double size)
        {
            // Get colors from the helper
            var fillColor = MapMarkerHelper.GetMarkerFillColor(marker.Type);
            var strokeColor = MapMarkerHelper.GetMarkerStrokeColor(marker.Type);

            // Create shape based on type
            string shapeType = MapMarkerHelper.GetMarkerShapeType(marker.Type);

            UIElement shapeElement;

            switch (shapeType)
            {
                case "Rectangle":
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = size,
                        Height = size,
                        Fill = new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 2,
                        Tag = marker,
                        Opacity = 0.9,
                        RadiusX = size / 5,
                        RadiusY = size / 5,
                        IsHitTestVisible = true  // Ensure hit testing is enabled
                    };
                    shapeElement = rect;
                    break;

                case "Diamond":
                    var diamond = new Polygon
                    {
                        Points = new PointCollection(new[] {
                            new System.Windows.Point(size/2, 0),
                            new System.Windows.Point(size, size/2),
                            new System.Windows.Point(size/2, size),
                            new System.Windows.Point(0, size/2)
                        }),
                        Fill = new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 2,
                        Tag = marker,
                        Opacity = 0.9,
                        IsHitTestVisible = true  // Ensure hit testing is enabled
                    };
                    shapeElement = diamond;
                    break;

                case "Triangle":
                    var triangle = new Polygon
                    {
                        Points = new PointCollection(new[] {
                            new System.Windows.Point(size/2, 0),
                            new System.Windows.Point(size, size),
                            new System.Windows.Point(0, size)
                        }),
                        Fill = new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 2,
                        Tag = marker,
                        Opacity = 0.9,
                        IsHitTestVisible = true  // Ensure hit testing is enabled
                    };
                    shapeElement = triangle;
                    break;

                case "Star":
                    var star = new Polygon();
                    var points = new PointCollection();

                    for (int i = 0; i < 5; i++)
                    {
                        double angle = i * 2 * Math.PI / 5 - Math.PI / 2;
                        points.Add(new System.Windows.Point(
                            size / 2 + size / 2 * Math.Cos(angle),
                            size / 2 + size / 2 * Math.Sin(angle)
                        ));

                        double innerAngle = angle + Math.PI / 5;
                        points.Add(new System.Windows.Point(
                            size / 2 + size / 4 * Math.Cos(innerAngle),
                            size / 2 + size / 4 * Math.Sin(innerAngle)
                        ));
                    }

                    star.Points = points;
                    star.Fill = new SolidColorBrush(fillColor);
                    star.Stroke = new SolidColorBrush(strokeColor);
                    star.StrokeThickness = 2;
                    star.Tag = marker;
                    star.Opacity = 0.9;
                    star.IsHitTestVisible = true;  // Ensure hit testing is enabled

                    shapeElement = star;
                    break;

                case "Ellipse":
                default:
                    var ellipse = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = new SolidColorBrush(fillColor),
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 2,
                        Tag = marker,
                        Opacity = 0.9,
                        IsHitTestVisible = true  // Ensure hit testing is enabled
                    };
                    shapeElement = ellipse;
                    break;
            }

            return shapeElement;
        }

        // Add this method to transform screen coordinates to map coordinates
        private WpfPoint TransformPointToMapCoordinates(WpfPoint screenPoint)
        {
            if (_overlayCanvas == null) return screenPoint;

            try
            {
                // Get the transform from the overlay canvas (which should match the map transform)
                var transformGroup = _overlayCanvas.RenderTransform as TransformGroup;
                if (transformGroup == null) return screenPoint;

                // Get the inverse transform to convert from screen to map coordinates
                var inverseTransform = transformGroup.Inverse;
                if (inverseTransform == null) return screenPoint;

                // Apply the inverse transform to convert from screen to map coordinates
                return inverseTransform.Transform(screenPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error transforming coordinates: {ex.Message}");
                return screenPoint;
            }
        }

        public void ShowCoordinateInfo(MapCoordinate coordinates, WpfPoint clickPoint)
        {
            try
            {
                if (coordinates == null || _overlayCanvas == null) return;

                // Transform the click point to account for map scaling and translation
                var transformedPoint = clickPoint; // Use the original point since it's already in screen space



                var textBlock = new TextBlock
                {
                    Text = $"X: {coordinates.MapX:F1}, Y: {coordinates.MapY:F1}",
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)),
                    Padding = new Thickness(5),
                    FontWeight = FontWeights.Bold
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Position the text near the actual click point
                    double left = Math.Max(0, Math.Min(transformedPoint.X + 10, _overlayCanvas.ActualWidth - 100));
                    double top = Math.Max(0, Math.Min(transformedPoint.Y - 30, _overlayCanvas.ActualHeight - 30));

                    Canvas.SetLeft(textBlock, left);
                    Canvas.SetTop(textBlock, top);
                    Canvas.SetZIndex(textBlock, 1000);

                    _overlayCanvas.Children.Add(textBlock);

                    // Remove the coordinate display after 3 seconds
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3)
                    };

                    timer.Tick += (s, e) =>
                    {
                        try
                        {
                            if (_overlayCanvas.Children.Contains(textBlock)
)
                                _overlayCanvas.Children.Remove(textBlock);
                        }
                        catch
                        {
                            // Suppress any errors if the element was already removed
                        }
                        timer.Stop();
                    };

                    timer.Start();
                });

                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Displayed coordinates at ({clickPoint.X:F1}, {clickPoint.Y:F1}): {coordinates.MapX:F1}, {coordinates.MapY:F1}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing coordinates: {ex.Message}");
            }
        }

        public void AddDebugGridAndBorders()
        {
            if (_overlayCanvas == null || !_showDebugVisuals) return;

            // Clear existing elements
            var debugElements = _overlayCanvas.Children.OfType<UIElement>()
                .Where(e => e is Line || e is TextBlock)
                .ToList();

            foreach (var element in debugElements)
            {
                _overlayCanvas.Children.Remove(element);
            }

            // Add border around the canvas
            var border = new System.Windows.Shapes.Rectangle
            {
                Width = _overlayCanvas.ActualWidth,
                Height = _overlayCanvas.ActualHeight,
                Stroke = WpfBrushes.Lime,
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };

            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            Canvas.SetZIndex(border, 500);
            _overlayCanvas.Children.Add(border);

            // Add a cross at the center of the canvas
            double centerX = _overlayCanvas.ActualWidth / 2;
            double centerY = _overlayCanvas.ActualHeight / 2;

            var hLine = new Line
            {
                X1 = centerX - 50,
                Y1 = centerY,
                X2 = centerX + 50,
                Y2 = centerY,
                Stroke = WpfBrushes.Magenta,
                StrokeThickness = 3
            };

            var vLine = new Line
            {
                X1 = centerX,
                Y1 = centerY - 50,
                X2 = centerX,
                Y2 = centerY + 50,
                Stroke = WpfBrushes.Magenta,
                StrokeThickness = 3
            };

            Canvas.SetZIndex(hLine, 500);
            Canvas.SetZIndex(vLine, 500);
            _overlayCanvas.Children.Add(hLine);
            _overlayCanvas.Children.Add(vLine);

            // Add text label at center
            var centerLabel = new TextBlock
            {
                Text = "CENTER",
                Foreground = WpfBrushes.Magenta,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0))
            };

            Canvas.SetLeft(centerLabel, centerX + 5);
            Canvas.SetTop(centerLabel, centerY + 5);
            Canvas.SetZIndex(centerLabel, 500);
            _overlayCanvas.Children.Add(centerLabel);

            if (_verboseLogging)
            {
                System.Diagnostics.Debug.WriteLine("Added debug grid and borders to overlay");
            }
        }

        public void VerifyImageParameters(WpfPoint imagePosition, WpfSize imageSize)
        {
            if (_verboseLogging)
            {
                System.Diagnostics.Debug.WriteLine("=== MAP IMAGE PARAMETERS ===");
                System.Diagnostics.Debug.WriteLine($"Image Position: ({imagePosition.X:F1}, {imagePosition.Y:F1})");
                System.Diagnostics.Debug.WriteLine($"Image Size: {imageSize.Width:F1}x{imageSize.Height:F1}");

                if (_overlayCanvas != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Canvas Size: {_overlayCanvas.ActualWidth:F1}x{_overlayCanvas.ActualHeight:F1}");
                }
            }

            // Only add visual indicators when debug visuals are enabled
            if (_showDebugVisuals && _overlayCanvas != null)
            {
                // Top-left corner
                AddCornerMarker(imagePosition.X, imagePosition.Y, WpfBrushes.Green);

                // Top-right corner
                AddCornerMarker(imagePosition.X + imageSize.Width, imagePosition.Y, WpfBrushes.Blue);

                // Bottom-left corner
                AddCornerMarker(imagePosition.X, imagePosition.Y + imageSize.Height, WpfBrushes.Yellow);

                // Bottom-right corner
                AddCornerMarker(imagePosition.X + imageSize.Width, imagePosition.Y + imageSize.Height, WpfBrushes.Red);
            }
        }

        private void AddCornerMarker(double x, double y, System.Windows.Media.Brush color)
        {
            var marker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = color,
                Stroke = WpfBrushes.White,
                StrokeThickness = 2
            };

            Canvas.SetLeft(marker, x - 5);
            Canvas.SetTop(marker, y - 5);
            Canvas.SetZIndex(marker, 5000);

            _overlayCanvas?.Children.Add(marker);
        }

        // Add this method to enable/disable hover functionality
        public void SetHoverEnabled(bool enabled)
        {
            _isHoverEnabled = enabled;

            // Clear hover state when disabling
            if (!enabled)
            {
                ClearHoverState();
            }
        }

        // You can simplify or remove the HandleMouseMove method since we're using direct events
        public void HandleMouseMove(WpfPoint mousePosition, Map? map)
        {
            // This method is now optional - the hover detection is handled by MouseEnter/MouseLeave events
            // You can keep it for debugging purposes or remove it entirely

            if (_verboseLogging && _hoveredMarker != null)
            {
                System.Diagnostics.Debug.WriteLine($"Mouse at ({mousePosition.X:F1}, {mousePosition.Y:F1}), hovering: {_hoveredMarker.PlaceName}");
            }
        }

        // Helper to check if a marker is under the cursor
        private bool IsMarkerUnderCursor(MapMarker marker, WpfPoint mousePosition,
                                      WpfPoint imagePosition, double scale, WpfSize imageSize)
        {
            try
            {
                // Convert marker game coordinates to screen coordinates
                double relativeX = marker.X / 42.0; // Assuming 42.0 is the game coordinate range
                double relativeY = marker.Y / 42.0;

                double pixelX = relativeX * imageSize.Width;
                double pixelY = relativeY * imageSize.Height;

                double screenX = pixelX * scale + imagePosition.X;
                double screenY = pixelY * scale + imagePosition.Y;

                // Calculate distance between mouse and marker
                double distance = Math.Sqrt(
                    Math.Pow(mousePosition.X - screenX, 2) +
                    Math.Pow(mousePosition.Y - screenY, 2));

                // Use a hit test radius scaled inversely with zoom
                double hitTestRadius = Math.Max(10, 15 / scale);

                return distance <= hitTestRadius;
            }
            catch
            {
                return false;
            }
        }

        // Find the UIElement for a specific marker
        private UIElement? FindMarkerElement(MapMarker marker)
        {
            if (_overlayCanvas == null) return null;

            // Look through all child elements with the marker Tag
            foreach (var element in _markerElements)
            {
                if (element is FrameworkElement fe && fe.Tag is MapMarker markerTag)
                {
                    if (markerTag.Id == marker.Id && markerTag.MapId == marker.MapId)
                    {
                        return element;
                    }
                }
            }

            return null;
        }

        // Apply visual effect to hovered marker
        private void ApplyHoverEffect(UIElement element)
        {
            try
            {
                if (element is FrameworkElement fe)
                {
                    // Store original opacity if needed
                    if (!fe.Resources.Contains("OriginalOpacity"))
                    {
                        fe.Resources.Add("OriginalOpacity", fe.Opacity);
                    }

                    // For shapes
                    if (element is Shape shape)
                    {
                        // Store original stroke thickness
                        if (!fe.Resources.Contains("OriginalStrokeThickness"))
                        {
                            fe.Resources.Add("OriginalStrokeThickness", shape.StrokeThickness);
                        }

                        // Increase stroke thickness and change opacity
                        shape.StrokeThickness = (double)fe.Resources["OriginalStrokeThickness"] * 2;
                        shape.Opacity = 1.0;

                        // Bring to front
                        Canvas.SetZIndex(element, 4000);
                    }
                    // For images
                    else if (element is System.Windows.Controls.Image image)
                    {
                        // Make fully opaque and slightly larger
                        image.Opacity = 1.0;

                        // Store original size
                        if (!fe.Resources.Contains("OriginalWidth"))
                        {
                            fe.Resources.Add("OriginalWidth", image.Width);
                            fe.Resources.Add("OriginalHeight", image.Height);
                        }

                        // Make slightly larger (but not too much)
                        image.Width = (double)fe.Resources["OriginalWidth"] * 1.2;
                        image.Height = (double)fe.Resources["OriginalHeight"] * 1.2;

                        // Recenter the element
                        double left = Canvas.GetLeft(element);
                        double top = Canvas.GetTop(element);
                        Canvas.SetLeft(element, left - (image.Width - (double)fe.Resources["OriginalWidth"]) / 2);
                        Canvas.SetTop(element, top - (image.Height - (double)fe.Resources["OriginalHeight"]) / 2);

                        // Bring to front
                        Canvas.SetZIndex(element, 4000);
                    }

                    // Add a glow effect (optional, may impact performance)
                    if (_showDebugVisuals) // Only when debug visuals are enabled
                    {
                        var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = Colors.White,
                            ShadowDepth = 0,
                            BlurRadius = 15,
                            Opacity = 0.7
                        };
                        element.Effect = dropShadow;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying hover effect: {ex.Message}");
            }
        }


        // Clear hover state and restore original appearance
        private void ClearHoverState()
        {
            try
            {
                if (_hoveredElement != null && _hoveredElement is FrameworkElement fe)
                {
                    // Restore original opacity
                    if (fe.Resources.Contains("OriginalOpacity"))
                    {
                        fe.Opacity = (double)fe.Resources["OriginalOpacity"];
                    }

                    // For shapes
                    if (_hoveredElement is Shape shape && fe.Resources.Contains("OriginalStrokeThickness"))
                    {
                        shape.StrokeThickness = (double)fe.Resources["OriginalStrokeThickness"];
                    }
                    // For images
                    else if (_hoveredElement is System.Windows.Controls.Image image)
                    {
                        // Restore original size
                        if (fe.Resources.Contains("OriginalWidth") && fe.Resources.Contains("OriginalHeight"))
                        {
                            // Calculate position adjustment to keep centered
                            double widthDiff = image.Width - (double)fe.Resources["OriginalWidth"];
                            double heightDiff = image.Height - (double)fe.Resources["OriginalHeight"];

                            // Restore size
                            image.Width = (double)fe.Resources["OriginalWidth"];
                            image.Height = (double)fe.Resources["OriginalHeight"];

                            // Restore position
                            double left = Canvas.GetLeft(_hoveredElement);
                            double top = Canvas.GetTop(_hoveredElement);
                            Canvas.SetLeft(_hoveredElement, left + widthDiff / 2);
                            Canvas.SetTop(_hoveredElement, top + heightDiff / 2);
                        }
                    }

                    // Remove any effect
                    _hoveredElement.Effect = null;

                    // Restore Z-index (use base value for marker type)
                    Canvas.SetZIndex(_hoveredElement, 3000);
                }

                // Clear the references
                _hoveredMarker = null;
                _hoveredElement = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing hover state: {ex.Message}");
            }
        }

        private void AddDebugBorderToElement(FrameworkElement element, MapMarker marker)
        {
            // Add a semi-transparent border to visualize the hit-test area CAN BE REMOVED
            if (_showDebugVisuals)
            {
                var debugBorder = new Border
                {
                    Width = element.Width + 4,
                    Height = element.Height + 4,
                    BorderBrush = new SolidColorBrush(Colors.Red),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 0, 0)),
                    IsHitTestVisible = false // Don't interfere with the actual element
                };

                // Position the debug border
                Canvas.SetLeft(debugBorder, Canvas.GetLeft(element) - 2);
                Canvas.SetTop(debugBorder, Canvas.GetTop(element) - 2);
                Canvas.SetZIndex(debugBorder, Canvas.GetZIndex(element) - 1);

                _overlayCanvas?.Children.Add(debugBorder);
            }
        }

        // Add method to set the main window reference
        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }
    }
}