using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiCursorApp
{
    public partial class MainWindow : Window
    {
        private InputManager? _inputManager;
        private OverlayWindow? _overlayWindow;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _inputManager = new InputManager(this);
            _inputManager.SecondaryMouseMoved += OnSecondaryMouseMoved;

            _overlayWindow = new OverlayWindow();
            _overlayWindow.Show();
            
            RefreshDeviceLists();
        }

        // --- Custom Title Bar Handlers ---
        private bool _isDragging = false;
        private Point _dragStartPoint;

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                CustomTitleBar.CaptureMouse();
            }
        }

        private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                this.Left += currentPoint.X - _dragStartPoint.X;
                this.Top += currentPoint.Y - _dragStartPoint.Y;
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                CustomTitleBar.ReleaseMouseCapture();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _inputManager?.Dispose();
            _overlayWindow?.Close();
        }

        private void RefreshMiceBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceLists();
        }

        private void RefreshDeviceLists()
        {
            var mice = InputManager.GetConnectedMice();
            
            string? currentPrimary = PrimaryMouseCombo.SelectedItem as string;
            string? currentSecondary = SecondaryMouseCombo.SelectedItem as string;

            PrimaryMouseCombo.ItemsSource = null;
            SecondaryMouseCombo.ItemsSource = null;
            
            PrimaryMouseCombo.ItemsSource = mice;
            SecondaryMouseCombo.ItemsSource = mice;

            if (currentPrimary != null && mice.Contains(currentPrimary))
                PrimaryMouseCombo.SelectedItem = currentPrimary;
            else if (mice.Count > 0)
                PrimaryMouseCombo.SelectedIndex = 0;

            if (currentSecondary != null && mice.Contains(currentSecondary))
                SecondaryMouseCombo.SelectedItem = currentSecondary;
            else if (mice.Count > 1)
                SecondaryMouseCombo.SelectedIndex = 1;
            else if (mice.Count > 0)
                SecondaryMouseCombo.SelectedIndex = 0;
        }

        private void PrimaryMouseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inputManager != null && PrimaryMouseCombo.SelectedItem != null)
            {
                _inputManager.PrimaryMouseHandle = PrimaryMouseCombo.SelectedItem.ToString() ?? "";
            }
        }

        private void SecondaryMouseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inputManager != null && SecondaryMouseCombo.SelectedItem != null)
            {
                _inputManager.SecondaryMouseHandle = SecondaryMouseCombo.SelectedItem.ToString() ?? "";
            }
        }

        private void SwitchMiceBtn_Click(object sender, RoutedEventArgs e)
        {
            int pIndex = PrimaryMouseCombo.SelectedIndex;
            PrimaryMouseCombo.SelectedIndex = SecondaryMouseCombo.SelectedIndex;
            SecondaryMouseCombo.SelectedIndex = pIndex;
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_inputManager == null) return;

            if (_inputManager.PrimaryMouseHandle == "" || _inputManager.SecondaryMouseHandle == "")
            {
                MessageBox.Show("Please assign both Primary and Secondary mice first!");
                return;
            }

            _inputManager.IsEnabled = !_inputManager.IsEnabled;
            if (_inputManager.IsEnabled)
            {
                ToggleBtn.Content = "Disable Multi-Cursor";
                ToggleBtn.Background = new SolidColorBrush(Colors.LightPink);
                _overlayWindow!.Visibility = Visibility.Visible;
            }
            else
            {
                ToggleBtn.Content = "Enable Multi-Cursor";
                ToggleBtn.Background = new SolidColorBrush(Colors.LightGreen);
            }
        }

        private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_overlayWindow == null) return;
            string? colorName = (ColorComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            
            Color color = Colors.Red;
            switch(colorName)
            {
                case "Blue": color = Colors.Blue; break;
                case "Green": color = Colors.Green; break;
                case "Yellow": color = Colors.Yellow; break;
                case "Purple": color = Colors.Purple; break;
            }
            _overlayWindow.SetCursorColor(color);
        }


        private void OnSecondaryMouseMoved(int screenX, int screenY)
        {
            // Update overlay cursor rendering on UI thread
            // Using BeginInvoke (async) for better performance — no blocking
            Dispatcher.BeginInvoke(() =>
            {
                _overlayWindow?.SetCursorScreenPosition(screenX, screenY);
            });
        }
    }
}