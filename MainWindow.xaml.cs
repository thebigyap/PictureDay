using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PictureDay.Services;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;

namespace PictureDay
{
    public partial class MainWindow : Window
    {
        private ConfigManager? _configManager;
        private StorageManager? _storageManager;
        private DailyScheduler? _scheduler;
        private bool _initialized;
        private bool _checkingNavigation;
        private bool _captureBusy;
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

        public MainWindow()
        {
            InitializeComponent();
            Title = $"PictureDay - {App.Version}";
            VersionText.Text = $"v{App.Version}";
            Loaded += MainWindow_Loaded;
            SettingsViewControl.SettingsSaved += SettingsViewControl_SettingsSaved;
            SettingsViewControl.RequestNavigateHome += (s, e) => GalleryNav.IsChecked = true;
            PreviewKeyDown += MainWindow_KeyDown;
            _statusTimer.Tick += (s, e) => UpdateCaptureStatus();
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                ToastHost.Visibility = Visibility.Collapsed;
            };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) { UpdateCaptureStatus(); _statusTimer.Start(); }
                else { _statusTimer.Stop(); _toastTimer.Stop(); ToastHost.Visibility = Visibility.Collapsed; }
            };
            Closed += (s, e) => { _statusTimer.Stop(); _toastTimer.Stop(); };
        }

        private void GalleryNav_Checked(object sender, RoutedEventArgs e)
        {
            if (_checkingNavigation || SettingsViewControl == null || !SettingsViewControl.HasUnsavedChanges) return;
            _checkingNavigation = true;
            try
            {
                var choice = SettingsViewControl.PromptUnsavedChanges();
                if (choice == MessageBoxResult.No) SettingsViewControl.DiscardChanges();
                else if (choice != MessageBoxResult.Yes || !SettingsViewControl.SaveSettings())
                    SettingsNav.IsChecked = true;
            }
            finally { _checkingNavigation = false; }
        }

        private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        }

        private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;
            _configManager = Application.Current.Resources["ConfigManager"] as ConfigManager;
            _storageManager = Application.Current.Resources["StorageManager"] as StorageManager;
            _scheduler = Application.Current.Resources["DailyScheduler"] as DailyScheduler;
            if (_storageManager != null) PhotoGalleryViewControl.Initialize(_storageManager);
            if (_configManager != null) SettingsViewControl.Initialize(_configManager);
            UpdateCaptureStatus();
        }

        private void SettingsViewControl_SettingsSaved(object? sender, EventArgs e)
        {
            RefreshGallery();
            GalleryNav.IsChecked = true;
            UpdateCaptureStatus();
            ShowToast("Settings saved");
        }

        public void UpdateCaptureStatus()
        {
            if (_captureBusy) return;
            var config = _configManager?.Config;
            if (config == null) return;
            if (_scheduler?.IsDayCompleted == true)
            {
                CaptureStatusText.Text = "Today's photo is saved";
                CaptureStatusDetail.Text = "You can still take a manual photo.";
            }
            else if (config.TodayScheduledTime is TimeSpan time)
            {
                var scheduled = (config.ScheduledTimeDate?.Date ?? DateTime.Today) + time;
                if (scheduled < DateTime.Now.AddMinutes(-2.5)) scheduled = DateTime.Today.AddDays(1) + time;
                string day = scheduled.Date == DateTime.Today ? "Today" : scheduled.Date == DateTime.Today.AddDays(1) ? "Tomorrow" : scheduled.ToString("MMM d");
                CaptureStatusText.Text = $"{day} at {scheduled:t}";
                CaptureStatusDetail.Text = "Captures while you're active and privacy checks pass.";
            }
            else
            {
                CaptureStatusText.Text = "Preparing your schedule";
                CaptureStatusDetail.Text = "Your screenshots stay on this PC.";
            }
        }

        public void SetCaptureBusy(bool busy)
        {
            _captureBusy = busy;
            TakeScreenshotButton.IsEnabled = !busy;
            SettingsViewControl.IsEnabled = !busy;
            TakeScreenshotLabel.Text = busy ? "Capturing…" : "Take Screenshot";
            if (busy) CaptureStatusText.Text = "Capturing your screen…";
            else UpdateCaptureStatus();
        }

        public void ShowToast(string message, bool success = true)
        {
            ToastText.Text = message;
            ToastIconBackground.Background = new SolidColorBrush(success ? Color.FromRgb(36, 148, 77) : Color.FromRgb(180, 105, 0));
            ToastIcon.Data = Geometry.Parse(success ? "M5 11 L9 15 L17 6" : "M11 4 L11 12 M11 16 L11 17");
            ToastHost.Visibility = Visibility.Visible;
            ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)));
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        public void ShowSettingsTab() => SettingsNav.IsChecked = true;
        public void RefreshGallery()
        {
            if (_storageManager != null) PhotoGalleryViewControl.Initialize(_storageManager);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private async void TakeScreenshotButton_Click(object sender, RoutedEventArgs e) => await TakeScreenshotAsync();
        private Task TakeScreenshotAsync() => Application.Current is App app ? app.TakeManualScreenshotAsync() : Task.CompletedTask;

        private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && SettingsNav.IsChecked == true)
            {
                e.Handled = true;
                if (!_captureBusy) SettingsViewControl.SaveSettings();
            }
            else if (e.Key == Key.F12 || (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control))
            {
                e.Handled = true;
                await TakeScreenshotAsync();
            }
        }

        private void OpenPhotosButton_Click(object sender, RoutedEventArgs e)
        {
            if (_storageManager != null) OpenLocation(_storageManager.GetBaseDirectory());
        }

        private void OpenLocation(string location)
        {
            try { Process.Start(new ProcessStartInfo(location) { UseShellExecute = true }); }
            catch (Exception ex) { ShowToast($"Could not open location: {ex.Message}", false); }
        }

        private void DonateButton_Click(object sender, RoutedEventArgs e) => OpenLocation("https://buymeacoffee.com/bigyap");
        private void PatreonButton_Click(object sender, RoutedEventArgs e) => OpenLocation("https://patreon.com/bigyap");
    }
}
