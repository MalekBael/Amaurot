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
// Add these missing using statements
using SaintCoinach;
using SaintCoinach.Xiv;
// Fix the ambiguous Application reference
using Application = System.Windows.Application;
// Add back the aliases that were in the original file
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

        private long _lastUpdateTimestamp = 0;
        private readonly object _updateLock = new object();

        public MapRenderer(ARealmReversed? realm = null)
        {
            _realm = realm;
            _markerUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _markerUpdateTimer.Tick += OnMarkerUpdateTimerTick;
        }

        private void OnMarkerUpdateTimerTick(object? sender, EventArgs e)
        {
            if (_markerUpdateTimer != null)
            {
                _markerUpdateTimer.Stop();
            }
            
            // Fix null check
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
            // Add this line to verify image parameters
            VerifyImageParameters(imagePosition, imageSize);
            
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
                Application.Current.Dispatcher.BeginInvoke(() => {
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

                // Only log basic info for performance
                System.Diagnostics.Debug.WriteLine($"Updating {markers.Count} markers at scale {scale:F2}");

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
                        System.Diagnostics.Debug.WriteLine($"Error with marker {marker.Id}: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"Added {displayCount} markers to overlay");
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
                        System.Diagnostics.Debug.WriteLine($"Property access error: {ex.Message}");
                    }
                }

                // Corrected Coordinate Calculation
                // 1. Normalize raw coordinates to a 0-1 range based on the map's texture size (2048).
                double normalizedX = (marker.X + offsetX) / 2048.0;
                double normalizedY = (marker.Y + offsetY) / 2048.0;

                // 2. Scale normalized coordinates to the image's dimensions.
                double pixelX = normalizedX * imageSize.Width;
                double pixelY = normalizedY * imageSize.Height;

                // 3. Add the image's position on the canvas to get the final coordinates.
                double canvasX = imagePosition.X + pixelX;
                double canvasY = imagePosition.Y + pixelY;

                // Also, calculate game coordinates for the tooltip using the correct formula.
                double c = sizeFactor / 100.0;
                double gameX = (41.0 / c) * (normalizedX) + 1.0;
                double gameY = (41.0 / c) * (normalizedY) + 1.0;

                System.Diagnostics.Debug.WriteLine(
                    $"Marker {marker.Id}: Raw({marker.X},{marker.Y}), " +
                    $"Game({gameX:F2},{gameY:F2}), " +
                    $"Norm({normalizedX:F3},{normalizedY:F3}), " +
                    $"Canvas({canvasX:F1},{canvasY:F1})");
        
                // Create visible marker
                double markerSize = Math.Max(20, 30 * displayScale);
        
                var markerElement = new Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = WpfBrushes.Red,
                    Stroke = WpfBrushes.Yellow, 
                    StrokeThickness = 2,
                    Tag = marker,
                    Opacity = 0.9
                };
        
                // Position centered on coordinates
                Canvas.SetLeft(markerElement, canvasX - (markerSize / 2));
                Canvas.SetTop(markerElement, canvasY - (markerSize / 2));
                Canvas.SetZIndex(markerElement, 3000);
        
                // Add tooltip with game coordinates
                if (markerElement is FrameworkElement fe)
                {
                    fe.ToolTip = $"{marker.PlaceName}\nID: {marker.Id}\nIcon: {marker.IconId}\nX: {gameX:F1}, Y: {gameY:F1}";
                }
        
                return markerElement;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating marker {marker.Id}: {ex.Message}");
                return null;
            }
        }

        // Fix SafeGetProperty to handle nullable objects
        private T SafeGetProperty<T>(object? obj, string propertyName, T defaultValue = default)
        {
            if (obj == null) return defaultValue;
            
            try
            {
                // First try using reflection to get the property value directly
                var property = obj.GetType().GetProperty(propertyName);
                if (property != null)
                {
                    var value = property.GetValue(obj);
                    if (value != null)
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                }
                
                // If that fails, try using the indexer (for dictionary-like objects)
                var indexerMethod = obj.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (indexerMethod != null)
                {
                    var value = indexerMethod.Invoke(obj, new object[] { propertyName });
                    if (value != null)
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting property {propertyName}: {ex.Message}");
            }
            
            return defaultValue;
        }

        public void ShowCoordinateInfo(MapCoordinate coordinates, WpfPoint clickPoint)
        {
            // Display the coordinates in a textblock
            var textBlock = new TextBlock
            {
                Text = $"X: {coordinates.MapX:F1}, Y: {coordinates.MapY:F1}",
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)),
                Padding = new Thickness(5),
                FontWeight = FontWeights.Bold
            };

            if (_overlayCanvas != null)
            {
                Canvas.SetLeft(textBlock, clickPoint.X + 10);
                Canvas.SetTop(textBlock, clickPoint.Y - 30);
                Canvas.SetZIndex(textBlock, 1000);

                _overlayCanvas.Children.Add(textBlock);

                // Remove the coordinate display after 3 seconds
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };

                timer.Tick += (s, e) =>
                {
                    _overlayCanvas.Children.Remove(textBlock);
                    timer.Stop();
                };

                timer.Start();
            }
        }

        // Add this new method to MapRenderer.cs
        public void AddCoordinateGrid()
        {
            if (_overlayCanvas == null) return;

            // Add a reference grid to help with debugging
            for (int x = 0; x <= 40; x += 10)
            {
                for (int y = 0; y <= 40; y += 10)
                {
                    // Create a small point marker at each 10-unit interval
                    var gridMarker = new Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Fill = WpfBrushes.Blue,
                        Opacity = 0.5
                    };

                    double pixelX = (x / 41.0) * _overlayCanvas.ActualWidth;
                    double pixelY = (y / 41.0) * _overlayCanvas.ActualHeight;

                    Canvas.SetLeft(gridMarker, pixelX - 2.5);
                    Canvas.SetTop(gridMarker, pixelY - 2.5);

                    _overlayCanvas.Children.Add(gridMarker);

                    // Add coordinate label
                    var label = new TextBlock
                    {
                        Text = $"{x},{y}",
                        FontSize = 10,
                        Foreground = WpfBrushes.Blue,
                        Background = new SolidColorBrush(Colors.White) { Opacity = 0.7 }
                    };

                    Canvas.SetLeft(label, pixelX + 3);
                    Canvas.SetTop(label, pixelY + 3);
                    Canvas.SetZIndex(label, 100);

                    _overlayCanvas.Children.Add(label);
                }
            }

            System.Diagnostics.Debug.WriteLine("Added coordinate grid to overlay");
        }

        // Add this method to MapRenderer
        public void AddDebugGridAndBorders()
        {
            if (_overlayCanvas == null) return;

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

            System.Diagnostics.Debug.WriteLine("Added debug grid and borders to overlay");
        }

        public void VerifyImageParameters(WpfPoint imagePosition, WpfSize imageSize)
        {
            System.Diagnostics.Debug.WriteLine("=== MAP IMAGE PARAMETERS ===");
            System.Diagnostics.Debug.WriteLine($"Image Position: ({imagePosition.X:F1}, {imagePosition.Y:F1})");
            System.Diagnostics.Debug.WriteLine($"Image Size: {imageSize.Width:F1}x{imageSize.Height:F1}");
            
            if (_overlayCanvas != null)
            {
                System.Diagnostics.Debug.WriteLine($"Canvas Size: {_overlayCanvas.ActualWidth:F1}x{_overlayCanvas.ActualHeight:F1}");
            }
            
            // Add visual indicator at image corners to verify position
            if (_overlayCanvas != null)
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
    }
}