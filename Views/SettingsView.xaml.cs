using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PictureDay.Models;
using PictureDay.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using ListBox = System.Windows.Controls.ListBox;
using Button = System.Windows.Controls.Button;

namespace PictureDay.Views
{
    public partial class SettingsView : UserControl
    {
        private ConfigManager? _configManager;
        private StorageManager? _storageManager;
        private List<string> _tempBlockedApps = new List<string>();

        public event EventHandler? SettingsSaved;

        private bool _isInitialized = false;

        public SettingsView()
        {
            InitializeComponent();
            _isInitialized = true;
        }

        public void Initialize(ConfigManager configManager)
        {
            _configManager = configManager;
            _storageManager = (StorageManager?)System.Windows.Application.Current.Resources["StorageManager"];
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (_configManager == null) return;

            _tempBlockedApps = new List<string>(_configManager.Config.BlockedApplications);
            RefreshBlockedAppsList();

            StartWithWindowsCheckBox.IsChecked = _configManager.Config.StartWithWindows;
            DirectoryTextBox.Text = _configManager.Config.ScreenshotDirectory;

            QualitySlider.Value = _configManager.Config.Quality;
            QualityValueTextBlock.Text = _configManager.Config.Quality.ToString();

            foreach (ComboBoxItem item in FormatComboBox.Items)
            {
                if (item.Tag?.ToString() == _configManager.Config.ImageFormat)
                {
                    FormatComboBox.SelectedItem = item;
                    break;
                }
            }

            UpdateQualityUI();

            ScheduleModeComboBox.SelectedIndex = (int)_configManager.Config.ScheduleMode;
            UpdateScheduleUI();

            if (_configManager.Config.FixedScheduledTime.HasValue)
            {
                FixedTimeTextBox.Text = _configManager.Config.FixedScheduledTime.Value.ToString(@"hh\:mm");
            }

            if (_configManager.Config.ScheduleRangeStart.HasValue)
            {
                RangeStartTimeTextBox.Text = _configManager.Config.ScheduleRangeStart.Value.ToString(@"hh\:mm");
            }

            if (_configManager.Config.ScheduleRangeEnd.HasValue)
            {
                RangeEndTimeTextBox.Text = _configManager.Config.ScheduleRangeEnd.Value.ToString(@"hh\:mm");
            }

            LoadMonitorSettings();
        }

        private void LoadMonitorSettings()
        {
            Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            MonitorComboBox.Items.Clear();

            foreach (Screen screen in screens)
            {
                string displayName = $"Monitor {MonitorComboBox.Items.Count + 1} ({screen.Bounds.Width}x{screen.Bounds.Height})";
                MonitorComboBox.Items.Add(displayName);
            }

            if (_configManager != null)
            {
                if (_configManager.Config.CaptureAllMonitors)
                {
                    AllMonitorsRadio.IsChecked = true;
                    MonitorComboBox.IsEnabled = false;
                }
                else if (_configManager.Config.SelectedMonitorIndex > 0 &&
                         _configManager.Config.SelectedMonitorIndex < MonitorComboBox.Items.Count)
                {
                    SpecificMonitorRadio.IsChecked = true;
                    MonitorComboBox.SelectedIndex = _configManager.Config.SelectedMonitorIndex;
                    MonitorComboBox.IsEnabled = true;
                }
                else
                {
                    PrimaryMonitorRadio.IsChecked = true;
                    MonitorComboBox.IsEnabled = false;
                }
            }
        }

        private void MonitorSelection_Changed(object sender, RoutedEventArgs e)
        {
            if (SpecificMonitorRadio.IsChecked == true)
            {
                MonitorComboBox.IsEnabled = true;
                if (MonitorComboBox.SelectedIndex < 0 && MonitorComboBox.Items.Count > 0)
                {
                    MonitorComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                MonitorComboBox.IsEnabled = false;
            }
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void ScheduleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateScheduleUI();
        }

        private void UpdateScheduleUI()
        {
            if (ScheduleModeComboBox.SelectedItem is ComboBoxItem item)
            {
                string? mode = item.Tag?.ToString();
                FixedTimePanel.Visibility = mode == "FixedTime" ? Visibility.Visible : Visibility.Collapsed;
                TimeRangePanel.Visibility = mode == "TimeRange" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || QualityValueTextBlock == null)
            {
                return;
            }
            QualityValueTextBlock.Text = ((int)e.NewValue).ToString();
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }
            UpdateQualityUI();
        }

        private void UpdateQualityUI()
        {
            if (QualitySlider == null || FormatComboBox == null)
            {
                return;
            }

            if (FormatComboBox.SelectedItem is ComboBoxItem item)
            {
                bool isJpeg = item.Tag?.ToString() == "JPEG";
                QualitySlider.IsEnabled = isJpeg;
            }
            else
            {
                QualitySlider.IsEnabled = true;
            }
        }

        private void RefreshBlockedAppsList()
        {
            BlockedAppsListBox.Items.Clear();
            foreach (string app in _tempBlockedApps)
            {
                BlockedAppsListBox.Items.Add(app);
            }
        }

        private void AddAppButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = NewAppTextBox.Text.Trim();
            if (string.IsNullOrEmpty(appName))
            {
                MessageBox.Show("Please enter an application name.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                appName = appName + ".exe";
            }

            if (!_tempBlockedApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
            {
                _tempBlockedApps.Add(appName);
                RefreshBlockedAppsList();
                NewAppTextBox.Clear();
            }
            else
            {
                MessageBox.Show("This application is already in the list.", "Duplicate Entry",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (BlockedAppsListBox.SelectedItem is string selectedApp)
            {
                _tempBlockedApps.Remove(selectedApp);
                RefreshBlockedAppsList();
            }
        }

        private void BrowseProcessesButton_Click(object sender, RoutedEventArgs e)
        {
            Window processWindow = new Window
            {
                Title = "Select Running Process",
                Width = 400,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            ListBox processListBox = new ListBox
            {
                Margin = new Thickness(10)
            };

            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .Select(p => p.ProcessName + ".exe")
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                foreach (string process in processes)
                {
                    processListBox.Items.Add(process);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading processes: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Button selectButton = new Button
            {
                Content = "Select",
                Width = 100,
                Height = 30,
                Margin = new Thickness(10),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            selectButton.Click += (s, args) =>
            {
                if (processListBox.SelectedItem is string selectedProcess)
                {
                    if (!_tempBlockedApps.Contains(selectedProcess, StringComparer.OrdinalIgnoreCase))
                    {
                        _tempBlockedApps.Add(selectedProcess);
                        RefreshBlockedAppsList();
                    }
                    processWindow.Close();
                }
            };

            StackPanel panel = new StackPanel();
            panel.Children.Add(processListBox);
            panel.Children.Add(selectButton);

            processWindow.Content = panel;
            processWindow.ShowDialog();
        }

        private void BrowseDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = DirectoryTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DirectoryTextBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager == null) return;

            try
            {
                _configManager.Config.BlockedApplications.Clear();
                foreach (string app in _tempBlockedApps)
                {
                    _configManager.Config.BlockedApplications.Add(app);
                }

                _configManager.Config.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;

                string newDirectory = DirectoryTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newDirectory) && Directory.Exists(newDirectory))
                {
                    _configManager.Config.ScreenshotDirectory = newDirectory;
                }

                _configManager.Config.Quality = (int)QualitySlider.Value;

                if (FormatComboBox.SelectedItem is ComboBoxItem formatItem)
                {
                    _configManager.Config.ImageFormat = formatItem.Tag?.ToString() ?? "JPEG";
                }

                if (AllMonitorsRadio.IsChecked == true)
                {
                    _configManager.Config.CaptureAllMonitors = true;
                    _configManager.Config.SelectedMonitorIndex = 0;
                }
                else if (SpecificMonitorRadio.IsChecked == true)
                {
                    _configManager.Config.CaptureAllMonitors = false;
                    _configManager.Config.SelectedMonitorIndex = MonitorComboBox.SelectedIndex >= 0 ? MonitorComboBox.SelectedIndex : 0;
                }
                else
                {
                    _configManager.Config.CaptureAllMonitors = false;
                    _configManager.Config.SelectedMonitorIndex = 0;
                }

                if (ScheduleModeComboBox.SelectedItem is ComboBoxItem item)
                {
                    string? mode = item.Tag?.ToString();
                    if (mode == "Random")
                    {
                        _configManager.Config.ScheduleMode = ScheduleMode.Random;
                    }
                    else if (mode == "FixedTime")
                    {
                        _configManager.Config.ScheduleMode = ScheduleMode.FixedTime;
                        if (TimeSpan.TryParse(FixedTimeTextBox.Text, out TimeSpan fixedTime))
                        {
                            _configManager.Config.FixedScheduledTime = fixedTime;
                        }
                        else
                        {
                            MessageBox.Show("Invalid fixed time format. Please use HH:mm format (e.g., 14:00).", "Invalid Input",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else if (mode == "TimeRange")
                    {
                        _configManager.Config.ScheduleMode = ScheduleMode.TimeRange;
                        if (TimeSpan.TryParse(RangeStartTimeTextBox.Text, out TimeSpan startTime) &&
                            TimeSpan.TryParse(RangeEndTimeTextBox.Text, out TimeSpan endTime))
                        {
                            if (startTime >= endTime)
                            {
                                MessageBox.Show("Start time must be before end time.", "Invalid Input",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            _configManager.Config.ScheduleRangeStart = startTime;
                            _configManager.Config.ScheduleRangeEnd = endTime;
                        }
                        else
                        {
                            MessageBox.Show("Invalid time range format. Please use HH:mm format (e.g., 09:00).", "Invalid Input",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                }

                _configManager.SaveConfig();

                if (_storageManager != null)
                {
                    _storageManager.UpdateSettings(_configManager.Config.Quality, _configManager.Config.ImageFormat);
                }

                SettingsSaved?.Invoke(this, EventArgs.Empty);

                MessageBox.Show("Settings saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }
    }
}

