using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MultiCursorApp
{
    public partial class OverlayWindow : Window
    {
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        // Screen bounds in physical pixels
        private int _physScreenLeft, _physScreenTop, _physScreenWidth, _physScreenHeight;

        public OverlayWindow()
        {
            InitializeComponent();
            this.Loaded += OverlayWindow_Loaded;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get DPI scale factor
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            // Get physical screen bounds
            _physScreenLeft = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            _physScreenTop = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            _physScreenWidth = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            _physScreenHeight = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

            // Set window to cover all monitors using DIP values
            this.Left = _physScreenLeft / _dpiScaleX;
            this.Top = _physScreenTop / _dpiScaleY;
            this.Width = _physScreenWidth / _dpiScaleX;
            this.Height = _physScreenHeight / _dpiScaleY;

            // Make the window click-through
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, 
                extendedStyle | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW);

            // Initialize cursor at center
            SetCursorScreenPosition(_physScreenLeft + _physScreenWidth / 2, 
                                     _physScreenTop + _physScreenHeight / 2);
        }

        /// <summary>
        /// Set the secondary cursor position using physical screen pixel coordinates.
        /// Converts to WPF DIPs for rendering.
        /// </summary>
        public void SetCursorScreenPosition(int screenX, int screenY)
        {
            // Convert physical screen pixels to WPF DIP canvas coordinates
            // Canvas coords = (screenPixel - screenOrigin) / dpiScale
            double canvasX = (screenX - _physScreenLeft) / _dpiScaleX;
            double canvasY = (screenY - _physScreenTop) / _dpiScaleY;

            CursorTransform.X = canvasX;
            CursorTransform.Y = canvasY;
        }

        public void SetCursorColor(Color color)
        {
            CursorPath.Fill = new SolidColorBrush(color);
        }
    }
}
