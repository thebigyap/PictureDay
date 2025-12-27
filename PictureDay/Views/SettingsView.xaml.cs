using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
        private List<string> _tempBlockedApps = new List<string>();

        public event EventHandler? SettingsSaved;

        public SettingsView()
        {
            InitializeComponent();
        }

        public void Initialize(ConfigManager configManager)
        {
            _configManager = configManager;
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (_configManager == null) return;

            _tempBlockedApps = new List<string>(_configManager.Config.BlockedApplications);
            RefreshBlockedAppsList();

            StartWithWindowsCheckBox.IsChecked = _configManager.Config.StartWithWindows;
            DirectoryTextBox.Text = _configManager.Config.ScreenshotDirectory;
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

                _configManager.SaveConfig();

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

