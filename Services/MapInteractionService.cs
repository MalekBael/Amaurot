using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPoint = System.Windows.Point;
using WpfImage = System.Windows.Controls.Image;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfCursors = System.Windows.Input.Cursors;

namespace map_editor
{
    public class MapInteractionService
    {
        private WpfPoint _lastMousePosition; // Fix: Use WpfPoint alias
        private bool _isDragging = false;
        private readonly Action<string> _logDebug;

        public MapInteractionService(Action<string> logDebug)
        {
            _logDebug = logDebug;
        }

        public void HandleMouseWheel(MouseWheelEventArgs e, Canvas mapCanvas, WpfImage mapImageControl, // Fix: Use WpfImage alias
            ref double currentScale, Canvas overlayCanvas, Action syncOverlayAction, Action refreshMarkersAction)
        {
            if (mapImageControl.Source == null) return;

            var mousePos = e.GetPosition(mapCanvas);

            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            double newScale = currentScale * zoomFactor;
            newScale = Math.Clamp(newScale, 0.1, 2.0);

            var transformGroup = mapImageControl.RenderTransform as TransformGroup;
            if (transformGroup == null)
            {
                transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform());
                transformGroup.Children.Add(new TranslateTransform());
                mapImageControl.RenderTransform = transformGroup;
            }

            var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
            var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();

            if (scaleTransform != null && translateTransform != null)
            {
                double scaleDelta = newScale / currentScale;
                translateTransform.X = mousePos.X - (mousePos.X - translateTransform.X) * scaleDelta;
                translateTransform.Y = mousePos.Y - (mousePos.Y - translateTransform.Y) * scaleDelta;
                scaleTransform.ScaleX = newScale;
                scaleTransform.ScaleY = newScale;
            }

            currentScale = newScale;
            syncOverlayAction?.Invoke();
            refreshMarkersAction?.Invoke();
            e.Handled = true;
        }

        public void HandleMouseLeftButtonDown(MouseButtonEventArgs e, Canvas mapCanvas, WpfImage mapImageControl) // Fix: Use WpfImage alias
        {
            if (mapImageControl.Source != null)
            {
                _lastMousePosition = e.GetPosition(mapCanvas);
                _isDragging = true;
                mapCanvas.Cursor = WpfCursors.Hand; // Fix: Use WpfCursors alias
                mapCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        public void HandleMouseLeftButtonUp(MouseButtonEventArgs e, Canvas mapCanvas)
        {
            if (_isDragging)
            {
                _isDragging = false;
                mapCanvas.Cursor = WpfCursors.Arrow; // Fix: Use WpfCursors alias
                mapCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        public void HandleMouseMove(WpfMouseEventArgs e, Canvas mapCanvas, WpfImage mapImageControl, Action syncOverlayAction) // Fix: Use aliases
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed && mapImageControl.Source != null)
            {
                var currentPosition = e.GetPosition(mapCanvas);
                var deltaX = currentPosition.X - _lastMousePosition.X;
                var deltaY = currentPosition.Y - _lastMousePosition.Y;

                var transformGroup = mapImageControl.RenderTransform as TransformGroup;
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
                syncOverlayAction?.Invoke();
            }
        }

        public void CalculateAndApplyInitialScale(BitmapSource bitmapSource, Canvas mapCanvas, 
            WpfImage mapImageControl, ref double currentScale) // Fix: Use WpfImage alias
        {
            double canvasWidth = mapCanvas.ActualWidth;
            double canvasHeight = mapCanvas.ActualHeight;

            if (canvasWidth <= 1 || canvasHeight <= 1)
            {
                _logDebug("Invalid canvas dimensions, using fallback values");
                canvasWidth = 800;
                canvasHeight = 600;
            }

            double imageWidth = bitmapSource.PixelWidth;
            double imageHeight = bitmapSource.PixelHeight;
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double fitScale = Math.Min(scaleX, scaleY);
            currentScale = fitScale * 0.9;

            double centeredX = (canvasWidth - (imageWidth * currentScale)) / 2;
            double centeredY = (canvasHeight - (imageHeight * currentScale)) / 2;

            mapImageControl.Width = imageWidth;
            mapImageControl.Height = imageHeight;
            Canvas.SetLeft(mapImageControl, 0);
            Canvas.SetTop(mapImageControl, 0);
            Canvas.SetZIndex(mapImageControl, 0);
            mapImageControl.Visibility = Visibility.Visible;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(currentScale, currentScale));
            transformGroup.Children.Add(new TranslateTransform(centeredX, centeredY));
            mapImageControl.RenderTransform = transformGroup;
            mapImageControl.RenderTransformOrigin = new WpfPoint(0, 0); // Fix: Use WpfPoint alias

            _logDebug($"Map scaled to {currentScale:F2} and positioned via transform at ({centeredX:F1}, {centeredY:F1})");
        }
    }
}