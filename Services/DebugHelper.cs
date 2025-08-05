using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPanel = System.Windows.Controls.Panel;

namespace Amaurot
{
    public class DebugHelper
    {
        private readonly MainWindow _mainWindow;
        private const int MaxLogLines = 500;

        public DebugHelper(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void LogDebug(string message)
        {
            if (!_mainWindow.Dispatcher.CheckAccess())
            {
                _mainWindow.Dispatcher.Invoke(() => LogDebug(message));
                return;
            }

            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _mainWindow.DebugTextBox.AppendText(timestampedMessage + Environment.NewLine);

            if (_mainWindow.DebugTextBox.LineCount > MaxLogLines)
            {
                int charsToRemove = _mainWindow.DebugTextBox.GetCharacterIndexFromLineIndex(_mainWindow.DebugTextBox.LineCount - MaxLogLines);
                _mainWindow.DebugTextBox.Text = _mainWindow.DebugTextBox.Text.Substring(charsToRemove);
            }

            if (_mainWindow.AutoScrollCheckBox?.IsChecked == true)
            {
                _mainWindow.DebugScrollViewer.ScrollToEnd();
            }
        }

        public void DiagnoseMapDisplay()
        {
            LogDebug("=== MAP DISPLAY DIAGNOSTIC ===");

            if (_mainWindow.MapImageControl.Source is BitmapSource bmp)
            {
                LogDebug($"Map image: {bmp.PixelWidth}x{bmp.PixelHeight} pixels");

                var transformGroup = _mainWindow.MapImageControl.RenderTransform as TransformGroup;
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

            LogDebug($"Canvas size: {_mainWindow.MapCanvas.ActualWidth}x{_mainWindow.MapCanvas.ActualHeight}");
            LogDebug($"Canvas children count: {_mainWindow.MapCanvas.Children.Count}");
            LogDebug($"Current scale: {_mainWindow.CurrentScale:F2}");

            LogDebug("==============================");
        }

        public void DiagnoseMapVisibility()
        {
            if (_mainWindow.MapImageControl.Source == null)
            {
                LogDebug("Map image source is null!");
                return;
            }

            LogDebug($"Map image visibility: {_mainWindow.MapImageControl.Visibility}");
            LogDebug($"Map image opacity: {_mainWindow.MapImageControl.Opacity}");
            LogDebug($"Map image actual size: {_mainWindow.MapImageControl.ActualWidth}x{_mainWindow.MapImageControl.ActualHeight}");
            LogDebug($"Map image position: Left={Canvas.GetLeft(_mainWindow.MapImageControl)}, Top={Canvas.GetTop(_mainWindow.MapImageControl)}");

            var transform = _mainWindow.MapImageControl.RenderTransform as TransformGroup;
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

            LogDebug($"Z-Index: MapImageControl={WpfPanel.GetZIndex(_mainWindow.MapImageControl)}, " +
                     $"MapCanvas={WpfPanel.GetZIndex(_mainWindow.MapCanvas)}, " +
                     $"OverlayCanvas={WpfPanel.GetZIndex(_mainWindow.OverlayCanvas ?? new Canvas())}");
        }

        public void DebugCanvasHierarchy()
        {
            LogDebug("=== CANVAS HIERARCHY DEBUG ===");
            LogDebug($"MapCanvas in visual tree: {_mainWindow.MapCanvas != null}");
            LogDebug($"MapImageControl in visual tree: {_mainWindow.MapImageControl != null}");
            LogDebug($"OverlayCanvas in visual tree: {_mainWindow.OverlayCanvas != null}");

            if (_mainWindow.MapCanvas != null)
            {
                LogDebug($"MapCanvas children count: {_mainWindow.MapCanvas.Children.Count}");
                LogDebug($"MapCanvas contains MapImageControl: {_mainWindow.MapCanvas.Children.Contains(_mainWindow.MapImageControl)}");

                for (int i = 0; i < _mainWindow.MapCanvas.Children.Count; i++)
                {
                    var child = _mainWindow.MapCanvas.Children[i];
                    LogDebug($"  Child {i}: Type={child.GetType().Name}, Z-Index={WpfPanel.GetZIndex(child)}");
                }
            }

            if (_mainWindow.MapCanvas?.Parent is Border border && border.Parent is Grid parentGrid)
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

        public void VerifyMapImageState()
        {
            LogDebug("VERIFYING MAP IMAGE STATE:");
            LogDebug($"- MapImageControl source null? {_mainWindow.MapImageControl.Source == null}");
            LogDebug($"- MapImageControl dimensions: {_mainWindow.MapImageControl.Width}x{_mainWindow.MapImageControl.Height}");
            LogDebug($"- MapImageControl actual size: {_mainWindow.MapImageControl.ActualWidth}x{_mainWindow.MapImageControl.ActualHeight}");
            LogDebug($"- MapImageControl position: Left={Canvas.GetLeft(_mainWindow.MapImageControl)}, Top={Canvas.GetTop(_mainWindow.MapImageControl)}");
            LogDebug($"- MapImageControl in Canvas: {_mainWindow.MapCanvas.Children.Contains(_mainWindow.MapImageControl)}");
            LogDebug($"- MapImageControl visibility: {_mainWindow.MapImageControl.Visibility}");
            LogDebug($"- MapImageControl z-index: {Canvas.GetZIndex(_mainWindow.MapImageControl)}");

            if (_mainWindow.MapImageControl.Visibility != Visibility.Visible)
            {
                _mainWindow.MapImageControl.Visibility = Visibility.Visible;
                LogDebug("- Visibility forced to Visible");
            }
        }

        public void DiagnoseOverlayCanvas()
        {
            if (_mainWindow.OverlayCanvas == null)
            {
                LogDebug("ERROR: OverlayCanvas is NULL!");
                return;
            }

            LogDebug("=== OVERLAY CANVAS DIAGNOSTIC ===");
            LogDebug($"OverlayCanvas exists: Yes");
            LogDebug($"OverlayCanvas size: {_mainWindow.OverlayCanvas.ActualWidth}x{_mainWindow.OverlayCanvas.ActualHeight}");
            LogDebug($"OverlayCanvas visibility: {_mainWindow.OverlayCanvas.Visibility}");
            LogDebug($"OverlayCanvas z-index: {WpfPanel.GetZIndex(_mainWindow.OverlayCanvas)}");
            LogDebug($"OverlayCanvas children count: {_mainWindow.OverlayCanvas.Children.Count}");

            bool isAttachedToVisualTree = false;
            DependencyObject parent = _mainWindow.OverlayCanvas;
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

            var transform = _mainWindow.OverlayCanvas.RenderTransform as TransformGroup;
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
            foreach (var child in _mainWindow.OverlayCanvas.Children)
            {
                LogDebug($"  - {child.GetType().Name}, Z-Index={WpfPanel.GetZIndex((UIElement)child)}");
            }

            LogDebug("==============================");
        }

        public void DebugFateMarkerPositions(List<MapMarker> currentMapMarkers)
        {
            if (currentMapMarkers == null) return;

            var fateMarkers = currentMapMarkers.Where(m => m.Type == MarkerType.Fate).ToList();
            LogDebug($"=== FATE MARKER POSITIONS DEBUG ===");
            LogDebug($"Total FATE markers: {fateMarkers.Count}");

            var positionGroups = fateMarkers.GroupBy(m => $"{m.X.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},{m.Y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}").ToList();
            LogDebug($"Unique positions: {positionGroups.Count}");

            foreach (var group in positionGroups.Take(5))
            {
                LogDebug($"Position ({group.Key}): {group.Count()} FATEs");
                foreach (var fate in group.Take(3))
                {
                    LogDebug($"  - {fate.PlaceName} (Icon: {fate.IconId})");
                }
            }
            LogDebug($"=====================================");
        }

        public bool ValidateGameDirectory(string directory)
        {
            LogDebug($"Validating directory: '{directory}'");
            try
            {
                bool gameFolderExists = System.IO.Directory.Exists(System.IO.Path.Combine(directory, "game"));
                bool bootFolderExists = System.IO.Directory.Exists(System.IO.Path.Combine(directory, "boot"));
                return gameFolderExists && bootFolderExists;
            }
            catch (Exception ex)
            {
                LogDebug($"Error during directory validation: {ex.Message}");
                return false;
            }
        }

        public void DebugFateLoadingProcess(uint mapId, uint territoryId, int fateCount, List<FateInfo> fates)
        {
            LogDebug($"=== FATE LOADING DEBUG ===");
            LogDebug($"Map ID: {mapId}, Territory ID: {territoryId}");
            LogDebug($"Total FATEs found: {fateCount}");

            if (fates.Any())
            {
                LogDebug("Sample FATEs (first 5):");
                foreach (var fate in fates.Take(5))
                {
                    LogDebug($"  - {fate.Name} (ID: {fate.FateId}, Icon: {fate.IconId})");
                    LogDebug($"    Position: ({fate.X:F1}, {fate.Y:F1}, {fate.Z:F1})");
                }
            }
            else
            {
                LogDebug("No FATEs found for this map/territory");
            }

            LogDebug("=========================");
        }

        public void DebugMarkerConversion(string markerType, int sourceCount, int convertedCount)
        {
            LogDebug($"Converting {markerType}: {sourceCount} items -> {convertedCount} markers");
        }

        public void DebugLevelData(dynamic levelData, uint mapId)
        {
            try
            {
                LogDebug($"Level data for map {mapId}:");
                LogDebug($"  EventId: {levelData.EventId}");
                LogDebug($"  Position: ({levelData.X:F1}, {levelData.Y:F1}, {levelData.Z:F1})");
                LogDebug($"  Territory: {levelData.TerritoryId}, Map: {levelData.MapId}");
            }
            catch (Exception ex)
            {
                LogDebug($"Error debugging level data: {ex.Message}");
            }
        }
    }

    public class TextBoxTraceListener : System.Diagnostics.TraceListener
    {
        private readonly DebugHelper _debugHelper;

        public TextBoxTraceListener(DebugHelper debugHelper)
        {
            _debugHelper = debugHelper;
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                _debugHelper.LogDebug(message);
            }
        }
    }
}