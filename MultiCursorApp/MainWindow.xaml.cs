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
            
            UpdateModeDescription();
            RefreshDeviceLists();
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

        private void ClickModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inputManager == null) return;
            string? mode = (ClickModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (mode != null && mode.StartsWith("Teleport"))
                _inputManager.CurrentClickMode = InputManager.ClickMode.Teleport;
            else
                _inputManager.CurrentClickMode = InputManager.ClickMode.SendMessage;

            UpdateModeDescription();
        }

        private void UpdateModeDescription()
        {
            if (ModeDescriptionLabel == null || _inputManager == null) return;
            if (_inputManager.CurrentClickMode == InputManager.ClickMode.Teleport)
            {
                ModeDescriptionLabel.Text = "Teleport: Instantly moves the primary cursor to the secondary position, clicks, and moves back. Very reliable, works everywhere.";
            }
            else
            {
                ModeDescriptionLabel.Text = "SendMessage: Sends a click message directly to the window under the secondary cursor. Primary cursor does not move. May not work in all apps/games.";
            }
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