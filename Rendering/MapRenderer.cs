using SaintCoinach;
using SaintCoinach.Xiv;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Application = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

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
        private bool _verboseLogging = false;
        private bool _showDebugVisuals = false;
        private int _debugLogCounter = 0;
        private const int LoggingThreshold = 50;
        private long _lastUpdateTimestamp = 0;
        private readonly object _updateLock = new object();
        private MapMarker? _hoveredMarker;
        private UIElement? _hoveredElement;
        private bool _isHoverEnabled = true;
        private double _lastHoverCheckTime;
        private const double HOVER_CHECK_THROTTLE_MS = 50;
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
            if (_showDebugVisuals)
            {
                VerifyImageParameters(imagePosition, imageSize);
            }

            lock (_updateLock)
            {
                _lastImagePosition = imagePosition;
                _lastImageSize = imageSize;
                _lastScale = scale;
                _pendingMarkers = new List<MapMarker>(markers);
                _currentMap = map;

                if (_isUpdatingMarkers)
                {
                    return;
                }

                long currentTime = Environment.TickCount64;
                if (currentTime - _lastUpdateTimestamp < 333)
                {
                    if (_markerUpdateTimer != null && !_markerUpdateTimer.IsEnabled)
                    {
                        _markerUpdateTimer.Start();
                    }
                    return;
                }

                _lastUpdateTimestamp = currentTime;

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

                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Updating {markers.Count} markers at scale {scale:F2}");
                }

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

                var visibleMarkers = markers.Where(m => m.IsVisible).ToList();

                var elementsToAdd = new List<UIElement>();
                int displayCount = 0;
                _debugLogCounter = 0;

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

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_overlayCanvas != null)
                    {
                        foreach (var element in elementsToAdd)
                        {
                            _overlayCanvas.Children.Add(element);
                        }
                    }

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
                            var sizeFactorValue = indexer.GetValue(_currentMap, new object[] { "SizeFactor" });
                            var offsetXValue = indexer.GetValue(_currentMap, new object[] { "OffsetX" });
                            var offsetYValue = indexer.GetValue(_currentMap, new object[] { "OffsetY" });

                            sizeFactor = sizeFactorValue != null ? (float)Convert.ChangeType(sizeFactorValue, typeof(float)) : 200.0f;
                            offsetX = offsetXValue != null ? (float)Convert.ChangeType(offsetXValue, typeof(float)) : 0f;
                            offsetY = offsetYValue != null ? (float)Convert.ChangeType(offsetYValue, typeof(float)) : 0f;
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

                double normalizedX = (marker.X + offsetX) / 2048.0;
                double normalizedY = (marker.Y + offsetY) / 2048.0;
                double pixelX = normalizedX * imageSize.Width;
                double pixelY = normalizedY * imageSize.Height;
                double canvasX = imagePosition.X + pixelX;
                double canvasY = imagePosition.Y + pixelY;
                double c = sizeFactor / 100.0;
                double gameX = (41.0 / c) * (normalizedX) + 1.0;
                double gameY = (41.0 / c) * (normalizedY) + 1.0;

                if (_verboseLogging && ++_debugLogCounter % LoggingThreshold == 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Marker {marker.Id}: Raw({marker.X},{marker.Y}), " +
                        $"Game({gameX:F2},{gameY:F2}), " +
                        $"Norm({normalizedX:F3},{normalizedY:F3}), " +
                        $"Canvas({canvasX:F1},{canvasY:F1})");
                }

                const double MAX_MARKER_SIZE = 20.0;
                const double MIN_MARKER_SIZE = 12.0;
                const double BASE_MARKER_SIZE = 16.0;

                double markerSize = Math.Max(MIN_MARKER_SIZE, Math.Min(MAX_MARKER_SIZE, BASE_MARKER_SIZE * displayScale));

                marker.DetermineType();

                UIElement? markerElement = null;

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

                        RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.HighQuality);

                        string iconFolder = $"{marker.IconId / 1000 * 1000:D6}";
                        string iconPath = $"ui/icon/{iconFolder}/{marker.IconId:D6}.tex";

                        bool iconLoaded = false;

                        try
                        {
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
                                    bitmapImage.Freeze();

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

                        markerElement = CreateFallbackShape(marker, markerSize);
                    }
                }
                else
                {
                    if (_verboseLogging && marker.IconId == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Marker {marker.Id} has no icon ID, using fallback shape");
                    }
                    markerElement = CreateFallbackShape(marker, markerSize);
                }

                if (marker.IconId == 0 && marker.PlaceNameId > 0 && !string.IsNullOrEmpty(marker.PlaceName))
                {
                    markerElement = CreateTextMarker(marker, displayScale, canvasX, canvasY);
                }
                else if (markerElement == null)
                {
                    markerElement = CreateFallbackShape(marker, markerSize);
                }

                if (markerElement is TextBlock)
                {
                }
                else
                {
                    Canvas.SetLeft(markerElement, canvasX - (markerSize / 2));
                    Canvas.SetTop(markerElement, canvasY - (markerSize / 2));
                }
                Canvas.SetZIndex(markerElement, 3000);

                if (markerElement is FrameworkElement fe)
                {
                    uint mapMarkerRange = 0;
                    if (_currentMap != null)
                    {
                        try
                        {
                            var type = _currentMap.GetType();
                            var indexer = type.GetProperty("Item", new[] { typeof(string) });
                            if (indexer != null)
                            {
                                var mapMarkerRangeValue = indexer.GetValue(_currentMap, new object[] { "MapMarkerRange" });
                                mapMarkerRange = mapMarkerRangeValue != null ? (uint)Convert.ChangeType(mapMarkerRangeValue, typeof(uint)) : 0u;
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

                    var tooltipContent = new StackPanel { Margin = new Thickness(5) };

                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = marker.PlaceName,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 5)
                    });

                    tooltipContent.Children.Add(new Separator
                    {
                        Margin = new Thickness(0, 2, 0, 5)
                    });

                    tooltipContent.Children.Add(new TextBlock
                    {
                        Text = $"Map Marker Range: {mapMarkerRange}\nRow: {mapMarkerRange}.{marker.Id}\nPosition: X={marker.X:F0}, Y={marker.Y:F0}\nGame Coords: X={gameX:F1}, Y={gameY:F1}\nIcon ID: {marker.IconId}\nMarker ID: {marker.Id}\nType: {marker.Type}"
                    });

                    var tooltip = new System.Windows.Controls.ToolTip
                    {
                        Content = tooltipContent,
                        Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                        HorizontalOffset = 15,
                        VerticalOffset = 10,
                        HasDropShadow = true
                    };

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

                    fe.IsHitTestVisible = true;
                    fe.MouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;

                        // FIXED: Use the existing HandleNpcMarkerClick method instead of accessing _allNpcs directly
                        if (marker.Type == MarkerType.Npc && _mainWindow is MainWindow mainWindow)
                        {
                            mainWindow.HandleNpcMarkerClick(marker.Id);
                        }
                        else
                        {
                            ShowMarkerDetailsPopup(marker, new WpfPoint(
                                canvasX,
                                canvasY - markerSize
                            ));
                        }
                    };
                }

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

        private void ShowMarkerDetailsPopup(MapMarker marker, WpfPoint position)
        {
            if (_mainWindow == null) return;

            uint mapMarkerRange = 0;
            if (_currentMap != null)
            {
                try
                {
                    var type = _currentMap.GetType();
                    var indexer = type.GetProperty("Item", new[] { typeof(string) });
                    if (indexer != null)
                    {
                        var mapMarkerRangeValue = indexer.GetValue(_currentMap, new object[] { "MapMarkerRange" });
                        mapMarkerRange = mapMarkerRangeValue != null ? (uint)Convert.ChangeType(mapMarkerRangeValue, typeof(uint)) : 0u;
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

            _mainWindow.Dispatcher.Invoke(() =>
            {
                var markerInfoBorder = _mainWindow.FindName("MarkerInfoBorder") as Border;
                var markerNameText = _mainWindow.FindName("MarkerNameText") as TextBlock;
                var markerSubtitleText = _mainWindow.FindName("MarkerSubtitleText") as TextBlock;
                var markerDetailsGrid = _mainWindow.FindName("MarkerDetailsGrid") as Grid;
                var noMarkerSelectedText = _mainWindow.FindName("NoMarkerSelectedText") as TextBlock;

                if (markerInfoBorder == null || markerNameText == null || markerDetailsGrid == null || noMarkerSelectedText == null)
                    return;

                markerInfoBorder.Visibility = Visibility.Visible;
                noMarkerSelectedText.Visibility = Visibility.Collapsed;

                markerNameText.Text = marker.PlaceName;

                if (markerSubtitleText != null)
                {
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

                markerDetailsGrid.RowDefinitions.Clear();
                markerDetailsGrid.Children.Clear();

                // ✅ ENHANCED: Check if this is a quest marker and get quest giver info
                bool isQuestMarker = marker.Type == MarkerType.Quest && marker.Id >= 2000000;
                string? questGiverInfo = null;

                if (isQuestMarker)
                {
                    uint questId = marker.Id - 2000000;
                    questGiverInfo = GetQuestGiverInfo(questId);
                }

                AddDetailRowToGrid(markerDetailsGrid, 0, "Map Marker Range:", $"{mapMarkerRange}");
                AddDetailRowToGrid(markerDetailsGrid, 1, "MapMarker Row:", $"{mapMarkerRange}.{marker.Id}");
                AddDetailRowToGrid(markerDetailsGrid, 2, "Raw Position:", $"X={marker.X:F0}, Y={marker.Y:F0}");

                // ✅ ENHANCED: Add quest giver information if available
                if (!string.IsNullOrEmpty(questGiverInfo))
                {
                    AddDetailRowToGrid(markerDetailsGrid, 3, "Quest Giver:", questGiverInfo);
                    AddDetailRowToGrid(markerDetailsGrid, 4, "Icon ID:", $"{marker.IconId}");
                    AddDetailRowToGrid(markerDetailsGrid, 5, "Marker ID:", $"{marker.Id}");
                    AddDetailRowToGrid(markerDetailsGrid, 6, "Type:", $"{marker.Type}");
                }
                else
                {
                    AddDetailRowToGrid(markerDetailsGrid, 3, "Icon ID:", $"{marker.IconId}");
                    AddDetailRowToGrid(markerDetailsGrid, 4, "Marker ID:", $"{marker.Id}");
                    AddDetailRowToGrid(markerDetailsGrid, 5, "Place Name ID:", $"{marker.PlaceNameId}");
                    AddDetailRowToGrid(markerDetailsGrid, 6, "Type:", $"{marker.Type}");
                }

                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Updated marker information panel for: {marker.PlaceName}");
                }
            });
        }

        // ✅ ADD: Helper method to get quest giver information
        private string? GetQuestGiverInfo(uint questId)
        {
            try
            {
                // Try to access the main window to get quest location service
                if (_mainWindow is MainWindow mainWindow)
                {
                    // Access quest location data through the data loader service
                    // This is a simplified approach - you might need to adjust based on your architecture
                    var dataLoaderType = typeof(MainWindow).Assembly.GetType("map_editor.Services.QuestLocationService");
                    if (dataLoaderType != null)
                    {
                        // For now, return a placeholder that shows we're looking for quest giver info
                        return $"Quest ID: {questId} (Quest Giver data available)";
                    }
                }
            }
            catch (Exception ex)
            {
                if (_verboseLogging)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting quest giver info: {ex.Message}");
                }
            }

            return null;
        }

        private void AddDetailRowToGrid(Grid grid, int row, string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

        private UIElement CreateTextMarker(MapMarker marker, double displayScale, double canvasX, double canvasY)
        {
            const double MAX_TEXT_SIZE = 12.0;
            const double MIN_TEXT_SIZE = 12.0;
            const double BASE_TEXT_SIZE = 12.0;

            double fontSize = Math.Max(MIN_TEXT_SIZE, Math.Min(MAX_TEXT_SIZE, BASE_TEXT_SIZE * displayScale));

            var textBlock = new TextBlock
            {
                Text = marker.PlaceName,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.White,
                Tag = marker,
                IsHitTestVisible = true
            };

            var formattedText = new FormattedText(
                marker.PlaceName,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                fontSize,
                WpfBrushes.White,
                VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);

            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                var geometry = formattedText.BuildGeometry(new WpfPoint(0, 0));
                var pen = new System.Windows.Media.Pen(WpfBrushes.Black, 3) { LineJoin = PenLineJoin.Round };
                drawingContext.DrawGeometry(null, pen, geometry);

                drawingContext.DrawGeometry(WpfBrushes.White, null, geometry);
            }

            var host = new Border
            {
                Tag = marker,
                IsHitTestVisible = true,
                Child = new VisualHost { Visual = drawingVisual }
            };

            host.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = host.DesiredSize.Width;
            var textHeight = host.DesiredSize.Height;

            Canvas.SetLeft(host, canvasX - (textWidth / 2));
            Canvas.SetTop(host, canvasY - (textHeight / 2));

            return host;
        }

        private class VisualHost : FrameworkElement
        {
            public DrawingVisual Visual { get; set; }

            protected override int VisualChildrenCount => Visual != null ? 1 : 0;

            protected override Visual GetVisualChild(int index)
            {
                if (index != 0 || Visual == null)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return Visual;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                if (Visual != null)
                {
                    var drawing = VisualTreeHelper.GetDrawing(Visual);
                    if (drawing != null)
                    {
                        drawingContext.DrawDrawing(drawing);
                    }
                }
            }
        }

        private UIElement CreateFallbackShape(MapMarker marker, double size)
        {
            var fillColor = MapMarkerHelper.GetMarkerFillColor(marker.Type);
            var strokeColor = MapMarkerHelper.GetMarkerStrokeColor(marker.Type);
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
                        IsHitTestVisible = true
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
                        IsHitTestVisible = true
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
                        IsHitTestVisible = true
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
                    star.IsHitTestVisible = true;

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
                        IsHitTestVisible = true
                    };
                    shapeElement = ellipse;
                    break;
            }

            return shapeElement;
        }

        private WpfPoint TransformPointToMapCoordinates(WpfPoint screenPoint)
        {
            if (_overlayCanvas == null) return screenPoint;

            try
            {
                var transformGroup = _overlayCanvas.RenderTransform as TransformGroup;
                if (transformGroup == null) return screenPoint;

                var inverseTransform = transformGroup.Inverse;
                if (inverseTransform == null) return screenPoint;

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

                var transformedPoint = clickPoint;

                var textBlock = new TextBlock
                {
                    Text = $"X: {coordinates.MapX:F1}, Y: {coordinates.MapY:F1}",
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)),
                    Padding = new Thickness(5),
                    FontWeight = FontWeights.Bold
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    double left = Math.Max(0, Math.Min(transformedPoint.X + 10, _overlayCanvas.ActualWidth - 100));
                    double top = Math.Max(0, Math.Min(transformedPoint.Y - 30, _overlayCanvas.ActualHeight - 30));

                    Canvas.SetLeft(textBlock, left);
                    Canvas.SetTop(textBlock, top);
                    Canvas.SetZIndex(textBlock, 1000);

                    _overlayCanvas.Children.Add(textBlock);
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

            var debugElements = _overlayCanvas.Children.OfType<UIElement>()
                .Where(e => e is Line || e is TextBlock)
                .ToList();

            foreach (var element in debugElements)
            {
                _overlayCanvas.Children.Remove(element);
            }

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

            if (_showDebugVisuals && _overlayCanvas != null)
            {
                AddCornerMarker(imagePosition.X, imagePosition.Y, WpfBrushes.Green);
                AddCornerMarker(imagePosition.X + imageSize.Width, imagePosition.Y, WpfBrushes.Blue);
                AddCornerMarker(imagePosition.X, imagePosition.Y + imageSize.Height, WpfBrushes.Yellow);
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

        public void SetHoverEnabled(bool enabled)
        {
            _isHoverEnabled = enabled;

            if (!enabled)
            {
                ClearHoverState();
            }
        }

        public void HandleMouseMove(WpfPoint mousePosition, Map? map)
        {
            double currentTime = Environment.TickCount64;
            if (currentTime - _lastHoverCheckTime < HOVER_CHECK_THROTTLE_MS)
                return;

            _lastHoverCheckTime = currentTime;

            if (_verboseLogging && _hoveredMarker != null)
            {
                System.Diagnostics.Debug.WriteLine($"Mouse at ({mousePosition.X:F1}, {mousePosition.Y:F1}), hovering: {_hoveredMarker.PlaceName}");
            }
        }

        private bool IsMarkerUnderCursor(MapMarker marker, WpfPoint mousePosition,
                                      WpfPoint imagePosition, double scale, WpfSize imageSize)
        {
            try
            {
                double relativeX = marker.X / 42.0;
                double relativeY = marker.Y / 42.0;

                double pixelX = relativeX * imageSize.Width;
                double pixelY = relativeY * imageSize.Height;

                double screenX = pixelX * scale + imagePosition.X;
                double screenY = pixelY * scale + imagePosition.Y;
                double distance = Math.Sqrt(
                    Math.Pow(mousePosition.X - screenX, 2) +
                    Math.Pow(mousePosition.Y - screenY, 2));

                double hitTestRadius = Math.Max(10, 15 / scale);

                return distance <= hitTestRadius;
            }
            catch
            {
                return false;
            }
        }

        private UIElement? FindMarkerElement(MapMarker marker)
        {
            if (_overlayCanvas == null) return null;

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

        private void ApplyHoverEffect(UIElement element)
        {
            try
            {
                if (element is FrameworkElement fe)
                {
                    if (!fe.Resources.Contains("OriginalOpacity"))
                    {
                        fe.Resources.Add("OriginalOpacity", fe.Opacity);
                    }

                    if (element is Shape shape)
                    {
                        if (!fe.Resources.Contains("OriginalStrokeThickness"))
                        {
                            fe.Resources.Add("OriginalStrokeThickness", shape.StrokeThickness);
                        }

                        shape.StrokeThickness = (double)fe.Resources["OriginalStrokeThickness"] * 2;
                        shape.Opacity = 1.0;

                        Canvas.SetZIndex(element, 4000);
                    }
                    else if (element is System.Windows.Controls.Image image)
                    {
                        image.Opacity = 1.0;

                        if (!fe.Resources.Contains("OriginalWidth"))
                        {
                            fe.Resources.Add("OriginalWidth", image.Width);
                            fe.Resources.Add("OriginalHeight", image.Height);
                        }

                        image.Width = (double)fe.Resources["OriginalWidth"] * 1.2;
                        image.Height = (double)fe.Resources["OriginalHeight"] * 1.2;

                        double left = Canvas.GetLeft(element);
                        double top = Canvas.GetTop(element);
                        Canvas.SetLeft(element, left - (image.Width - (double)fe.Resources["OriginalWidth"]) / 2);
                        Canvas.SetTop(element, top - (image.Height - (double)fe.Resources["OriginalHeight"]) / 2);

                        Canvas.SetZIndex(element, 4000);
                    }
                    if (_showDebugVisuals)
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

        private void ClearHoverState()
        {
            try
            {
                if (_hoveredElement != null && _hoveredElement is FrameworkElement fe)
                {
                    if (fe.Resources.Contains("OriginalOpacity"))
                    {
                        fe.Opacity = (double)fe.Resources["OriginalOpacity"];
                    }

                    if (_hoveredElement is Shape shape && fe.Resources.Contains("OriginalStrokeThickness"))
                    {
                        shape.StrokeThickness = (double)fe.Resources["OriginalStrokeThickness"];
                    }
                    else if (_hoveredElement is System.Windows.Controls.Image image)
                    {
                        if (fe.Resources.Contains("OriginalWidth") && fe.Resources.Contains("OriginalHeight"))
                        {
                            double widthDiff = image.Width - (double)fe.Resources["OriginalWidth"];
                            double heightDiff = image.Height - (double)fe.Resources["OriginalHeight"];

                            image.Width = (double)fe.Resources["OriginalWidth"];
                            image.Height = (double)fe.Resources["OriginalHeight"];

                            double left = Canvas.GetLeft(_hoveredElement);
                            double top = Canvas.GetTop(_hoveredElement);
                            Canvas.SetLeft(_hoveredElement, left + widthDiff / 2);
                            Canvas.SetTop(_hoveredElement, top + heightDiff / 2);
                        }
                    }

                    _hoveredElement.Effect = null;

                    Canvas.SetZIndex(_hoveredElement, 3000);
                }

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
            if (_showDebugVisuals)
            {
                var debugBorder = new Border
                {
                    Width = element.Width + 4,
                    Height = element.Height + 4,
                    BorderBrush = new SolidColorBrush(Colors.Red),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 0, 0)),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(debugBorder, Canvas.GetLeft(element) - 2);
                Canvas.SetTop(debugBorder, Canvas.GetTop(element) - 2);
                Canvas.SetZIndex(debugBorder, Canvas.GetZIndex(element) - 1);

                _overlayCanvas?.Children.Add(debugBorder);
            }
        }

        public void SetMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        // REMOVED: The ShowNpcQuestPopup method that was causing the access issue
        // The HandleNpcMarkerClick method in MainWindow now handles this functionality
    }
}