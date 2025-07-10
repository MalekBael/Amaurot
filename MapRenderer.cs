using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using SaintCoinach; // Add this for ARealmReversed
using SaintCoinach.Xiv; // Add this for Map and other game data classes
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfImage = System.Windows.Controls.Image;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;
using WpfClipboard = System.Windows.Clipboard;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfColor = System.Windows.Media.Color;

namespace map_editor
{
    public class MapRenderer
    {
        private readonly Canvas _canvas;
        private readonly WpfImage _mapImage;
        private readonly List<UIElement> _markerElements = new();
        private readonly Dictionary<string, BitmapImage> _iconCache = new();
        private ARealmReversed? _realm;

        // Add a static shared cache to avoid reloading icons across different maps
        private static readonly Dictionary<uint, BitmapImage> _globalIconCache = new();

        public event EventHandler<MapClickEventArgs>? MapClicked;

        public MapRenderer(Canvas canvas, WpfImage mapImage, ARealmReversed? realm = null)
        {
            _canvas = canvas;
            _mapImage = mapImage;
            _realm = realm;
        }

        // Add a method to update the realm reference
        public void UpdateRealm(ARealmReversed realm)
        {
            _realm = realm;
            // Clear the icon cache when realm changes
            _iconCache.Clear();
        }

        // Display map markers on the canvas
        public void DisplayMapMarkers(List<MapMarker> markers, double scale, WpfPoint imagePosition, WpfSize imageSize)
        {
            // Clear existing markers
            ClearMarkers();

            if (markers == null || markers.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No markers to display");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Processing {markers.Count} markers to display");

            // Process markers in batches for better performance
            const int batchSize = 10;
            int totalMarkers = markers.Count;
            int processedCount = 0;
            int displayCount = 0;

            // Get visible markers
            var visibleMarkers = markers.Where(m => m.IsVisible).ToList();

            // Process markers in batches
            for (int i = 0; i < visibleMarkers.Count; i += batchSize)
            {
                var batch = visibleMarkers.Skip(i).Take(batchSize);

                foreach (var marker in batch)
                {
                    try
                    {
                        var markerElement = CreateMarkerElement(marker, scale, imagePosition, imageSize);
                        if (markerElement != null)
                        {
                            _canvas.Children.Add(markerElement);
                            _markerElements.Add(markerElement);
                            displayCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error displaying marker {marker.Id}: {ex.Message}");
                    }

                    processedCount++;
                }

                // Allow UI to refresh between batches (not needed if already on background thread)
                // System.Windows.Forms.Application.DoEvents();
            }

            System.Diagnostics.Debug.WriteLine($"Displayed {displayCount}/{totalMarkers} markers on canvas");
        }

        // Display map markers on the canvas with map data
        public void DisplayMapMarkers(List<MapMarker> markers, Map map, double scale, WpfPoint imagePosition, WpfSize imageSize)
        {
            // Clear existing markers
            ClearMarkers();

            if (markers == null || markers.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No markers to display");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Processing {markers.Count} markers to display");

            // Get visible markers
            var visibleMarkers = markers.Where(m => m.IsVisible).ToList();
            int displayCount = 0;

            // Process all markers
            foreach (var marker in visibleMarkers)
            {
                try
                {
                    var markerElement = CreateMarkerElement(marker, scale, imagePosition, imageSize, map);
                    if (markerElement != null)
                    {
                        _canvas.Children.Add(markerElement);
                        _markerElements.Add(markerElement);
                        displayCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error displaying marker {marker.Id}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Displayed {displayCount}/{markers.Count} markers on canvas");
        }

        // Create a visual element for a map marker
        private UIElement? CreateMarkerElement(MapMarker marker, double scale, WpfPoint imagePosition, WpfSize imageSize)
        {
            // Assume marker.X/Y are in 0-100 map units, scale to image size
            double canvasX = imagePosition.X + (marker.X / 100.0 * imageSize.Width * scale);
            double canvasY = imagePosition.Y + (marker.Y / 100.0 * imageSize.Height * scale);

            System.Diagnostics.Debug.WriteLine($"Creating marker: ID={marker.Id}, Type={marker.Type}, X={marker.X}, Y={marker.Y}, IconPath={marker.IconPath}");
            System.Diagnostics.Debug.WriteLine($"  Mapped to canvas: X={canvasX}, Y={canvasY}");

            // Try to load icon from game data if available
            UIElement markerElement;
            if (_realm != null && !string.IsNullOrEmpty(marker.IconPath))
            {
                markerElement = TryLoadIconFromGameData(marker.IconPath, marker.IconId) ??
                                CreateFallbackMarker(marker.Type, marker.IconId);
            }
            else
            {
                markerElement = CreateFallbackMarker(marker.Type, marker.IconId);
            }

            // Set position and tooltip
            Canvas.SetLeft(markerElement, canvasX - 12); // Center the marker (icon size is 24x24)
            Canvas.SetTop(markerElement, canvasY - 12);
            Canvas.SetZIndex(markerElement, 100);

            if (markerElement is FrameworkElement fe)
            {
                fe.ToolTip = $"{marker.PlaceName}\nID: {marker.Id}\nIcon: {marker.IconId}\nX: {marker.X:F1}, Y: {marker.Y:F1}";
                fe.MouseLeftButtonDown += (s, e) =>
                {
                    OnMarkerClicked(marker, new WpfPoint(canvasX, canvasY));
                    e.Handled = true;
                };
            }

            return markerElement;
        }

        // Create a visual element for a map marker with map data
        private UIElement? CreateMarkerElement(MapMarker marker, double scale, WpfPoint imagePosition, WpfSize imageSize, Map map)
        {
            double pixelX = marker.X * map.SizeFactor / 100.0;
            double pixelY = marker.Y * map.SizeFactor / 100.0;
            double canvasX = imagePosition.X + (pixelX * scale);
            double canvasY = imagePosition.Y + (pixelY * scale);

            UIElement markerElement;
            if (_realm != null && !string.IsNullOrEmpty(marker.IconPath))
            {
                markerElement = TryLoadIconFromGameData(marker.IconPath, marker.IconId) ??
                                CreateFallbackMarker(marker.Type, marker.IconId);
            }
            else
            {
                markerElement = CreateFallbackMarker(marker.Type, marker.IconId);
            }

            Canvas.SetLeft(markerElement, canvasX - 12);
            Canvas.SetTop(markerElement, canvasY - 12);
            Canvas.SetZIndex(markerElement, 100);

            if (markerElement is FrameworkElement fe)
            {
                fe.ToolTip = $"{marker.PlaceName}\nID: {marker.Id}\nIcon: {marker.IconId}\nX: {marker.X:F1}, Y: {marker.Y:F1}";
                fe.MouseLeftButtonDown += (s, e) =>
                {
                    OnMarkerClicked(marker, new WpfPoint(canvasX, canvasY));
                    e.Handled = true;
                };
            }

            return markerElement;
        }

        // Try to load icon from game data
        private UIElement? TryLoadIconFromGameData(string iconPath, uint iconId)
        {
            try
            {
                // Check global cache first for the iconId
                if (_globalIconCache.TryGetValue(iconId, out var cachedIcon))
                {
                    System.Diagnostics.Debug.WriteLine($"Using globally cached icon for {iconId}");
                    return CreateImageElement(cachedIcon);
                }
                
                // Then check the local path cache
                if (_iconCache.TryGetValue(iconPath, out var cachedImage))
                {
                    System.Diagnostics.Debug.WriteLine($"Using cached icon for {iconPath}");
                    return CreateImageElement(cachedImage);
                }

                if (_realm == null)
                {
                    System.Diagnostics.Debug.WriteLine("Realm is null, can't load icons");
                    return null;
                }

                // Use the path format we know works based on diagnostics
                string correctPath = $"ui/icon/{iconId / 1000 * 1000:D6}/{iconId:D6}.tex";
                System.Diagnostics.Debug.WriteLine($"Trying to load icon from path: {correctPath}");

                if (_realm.Packs.FileExists(correctPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Found icon file at: {correctPath}");
                    var file = _realm.Packs.GetFile(correctPath);

                    // Try to get image via GetImage method (which we know exists from the diagnostics)
                    var imageMethod = file.GetType().GetMethod("GetImage");
                    if (imageMethod != null)
                    {
                        var gameImage = imageMethod.Invoke(file, null) as System.Drawing.Image;
                        if (gameImage != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Successfully loaded icon from {correctPath} using GetImage method");
                            return ConvertToImageElementAndCache(gameImage, correctPath, iconId);
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Failed to load icon with ID {iconId}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading icon {iconPath}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // New method to both convert and cache icons
        private UIElement ConvertToImageElementAndCache(System.Drawing.Image gameImage, string path, uint iconId)
        {
            try
            {
                using (var memStream = new MemoryStream())
                {
                    gameImage.Save(memStream, System.Drawing.Imaging.ImageFormat.Png);
                    memStream.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it thread-safe

                    // Cache by both path and ID
                    _iconCache[path] = bitmapImage;
                    _globalIconCache[iconId] = bitmapImage;

                    return CreateImageElement(bitmapImage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting image: {ex.Message}");
                return null;
            }
        }

        // Helper method to convert System.Drawing.Image to WPF image element
        private UIElement ConvertToImageElement(System.Drawing.Image gameImage, string path)
        {
            try
            {
                using (var memStream = new MemoryStream())
                {
                    gameImage.Save(memStream, System.Drawing.Imaging.ImageFormat.Png);
                    memStream.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it thread-safe

                    // Cache the image
                    _iconCache[path] = bitmapImage;

                    return CreateImageElement(bitmapImage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting image: {ex.Message}");
                return null;
            }
        }

        private UIElement CreateImageElement(BitmapImage image)
        {
            return new WpfImage
            {
                Source = image,
                Width = 24,
                Height = 24,
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransformOrigin = new WpfPoint(0.5, 0.5) // Use WpfPoint to avoid ambiguity
            };
        }

        // Create fallback marker visuals based on type
        private UIElement CreateFallbackMarker(MarkerType markerType, uint iconId)
        {
            return markerType switch
            {
                MarkerType.Aetheryte => CreateAetheryteMarker(),
                MarkerType.Quest => CreateQuestMarker(),
                MarkerType.Shop => CreateShopMarker(),
                MarkerType.Landmark => CreateLandmarkMarker(),
                MarkerType.Entrance => CreateEntranceMarker(),
                MarkerType.Custom => CreateCustomMarker(),
                MarkerType.Symbol => CreateSymbolMarker(iconId),
                _ => CreateGenericMarker(iconId)
            };
        }

        // Create a specific marker for symbols
        private UIElement CreateSymbolMarker(uint iconId)
        {
            return new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0)), // Use WpfColor to avoid ambiguity
                Stroke = WpfBrushes.DarkGoldenrod,
                StrokeThickness = 2,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        // Create different marker types
        private UIElement CreateGenericMarker(uint iconId)
        {
            // Use different colors based on icon ID ranges for better visibility
            WpfBrush fill;
            WpfBrush stroke;

            if (iconId < 10)
            {
                fill = WpfBrushes.Red;
                stroke = WpfBrushes.White;
            }
            else if (iconId < 100)
            {
                fill = WpfBrushes.Orange;
                stroke = WpfBrushes.White;
            }
            else if (iconId < 1000)
            {
                fill = WpfBrushes.Yellow;
                stroke = WpfBrushes.Black;
            }
            else
            {
                fill = WpfBrushes.Cyan;
                stroke = WpfBrushes.Black;
            }

            return new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private UIElement CreateAetheryteMarker()
        {
            return new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = WpfBrushes.Blue,
                Stroke = WpfBrushes.LightBlue,
                StrokeThickness = 2,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private UIElement CreateQuestMarker()
        {
            var polygon = new Polygon
            {
                Points = new PointCollection { new WpfPoint(6, 0), new WpfPoint(12, 12), new WpfPoint(0, 12) },
                Fill = WpfBrushes.Gold,
                Stroke = WpfBrushes.Orange,
                StrokeThickness = 1,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return polygon;
        }

        private UIElement CreateShopMarker()
        {
            return new WpfRectangle
            {
                Width = 12,
                Height = 12,
                Fill = WpfBrushes.Green,
                Stroke = WpfBrushes.DarkGreen,
                StrokeThickness = 1,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private UIElement CreateLandmarkMarker()
        {
            return new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = WpfBrushes.Purple,
                Stroke = WpfBrushes.MediumPurple,
                StrokeThickness = 2,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private UIElement CreateEntranceMarker()
        {
            var polygon = new Polygon
            {
                Points = new PointCollection {
                    new WpfPoint(0, 12),
                    new WpfPoint(6, 0),
                    new WpfPoint(12, 12),
                    new WpfPoint(6, 8)
                },
                Fill = WpfBrushes.DarkBlue,
                Stroke = WpfBrushes.LightBlue,
                StrokeThickness = 1,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return polygon;
        }

        private UIElement CreateCustomMarker()
        {
            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = WpfBrushes.Magenta,
                Stroke = WpfBrushes.White,
                StrokeThickness = 2,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return ellipse;
        }

        // Clear all markers from canvas
        public void ClearMarkers()
        {
            foreach (var element in _markerElements)
            {
                _canvas.Children.Remove(element);
            }
            _markerElements.Clear();
        }

        // Handle marker click events
        private void OnMarkerClicked(MapMarker marker, WpfPoint position)
        {
            System.Diagnostics.Debug.WriteLine($"Marker clicked: {marker.PlaceName} at {position}");

            // Show more detailed information
            var contextMenu = new ContextMenu();

            var idMenuItem = new MenuItem
            {
                Header = $"Marker ID: {marker.Id}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            contextMenu.Items.Add(idMenuItem);

            var nameMenuItem = new MenuItem
            {
                Header = $"Name: {marker.PlaceName}",
                IsEnabled = false
            };
            contextMenu.Items.Add(nameMenuItem);

            var iconMenuItem = new MenuItem
            {
                Header = $"Icon: {marker.IconId}",
                IsEnabled = false
            };
            contextMenu.Items.Add(iconMenuItem);

            var coordsMenuItem = new MenuItem
            {
                Header = $"Position: X={marker.X:F1}, Y={marker.Y:F1}",
                IsEnabled = false
            };
            contextMenu.Items.Add(coordsMenuItem);

            // Add the context menu to the clicked element and open it
            if (_canvas.ContextMenu != null)
            {
                _canvas.ContextMenu = null;
            }

            _canvas.ContextMenu = contextMenu;
            _canvas.ContextMenu.IsOpen = true;
        }

        // Show coordinate information
        public void ShowCoordinateInfo(MapCoordinate coordinate, WpfPoint canvasPosition)
        {
            var contextMenu = new ContextMenu();

            var coordMenuItem = new MenuItem
            {
                Header = "Coordinates",
                IsEnabled = false
            };
            contextMenu.Items.Add(coordMenuItem);

            var mapCoordMenuItem = new MenuItem
            {
                Header = $"Map: {coordinate.MapX:F1}, {coordinate.MapY:F1}",
                IsEnabled = false
            };
            contextMenu.Items.Add(mapCoordMenuItem);

            var clientCoordMenuItem = new MenuItem
            {
                Header = $"Client: {coordinate.ClientX:F1}, {coordinate.ClientY:F1}",
                IsEnabled = false
            };
            contextMenu.Items.Add(clientCoordMenuItem);

            contextMenu.Items.Add(new Separator());

            var copyMenuItem = new MenuItem
            {
                Header = "Copy Coordinates"
            };
            copyMenuItem.Click += (s, args) =>
            {
                var coordText = $"Map: {coordinate.MapX:F1}, {coordinate.MapY:F1} | Client: {coordinate.ClientX:F1}, {coordinate.ClientY:F1}";
                WpfClipboard.SetText(coordText);
            };
            contextMenu.Items.Add(copyMenuItem);

            // Add custom marker option
            var addMarkerMenuItem = new MenuItem
            {
                Header = "Add Custom Marker"
            };
            addMarkerMenuItem.Click += (s, args) =>
            {
                OnAddCustomMarker(coordinate, canvasPosition);
            };
            contextMenu.Items.Add(addMarkerMenuItem);

            _canvas.ContextMenu = contextMenu;
        }

        // Handle adding custom markers
        private void OnAddCustomMarker(MapCoordinate coordinate, WpfPoint canvasPosition)
        {
            // Use a default icon ID for custom markers
            uint defaultIconId = 60453; // Using a known working icon ID

            var customMarker = new MapMarker
            {
                Id = (uint)DateTime.Now.Ticks, // Simple ID generation
                X = coordinate.MapX,
                Y = coordinate.MapY,
                Z = coordinate.ClientZ,
                PlaceName = "Custom Marker",
                Type = MarkerType.Custom,
                IsVisible = true,
                IconId = defaultIconId,
                IconPath = $"ui/icon/{defaultIconId / 1000 * 1000:D6}/{defaultIconId:D6}.tex"
            };

            System.Diagnostics.Debug.WriteLine($"Would add custom marker at {coordinate.MapX:F1}, {coordinate.MapY:F1}");
            // You can fire an event here to notify the main window to add this marker
        }

        // Fire map click event
        public void OnMapClicked(MapClickEventArgs args)
        {
            MapClicked?.Invoke(this, args);
        }
    }
}