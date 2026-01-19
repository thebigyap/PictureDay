using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PictureDay.Services;
using PictureDay.Views;
using Application = System.Windows.Application;

namespace PictureDay
{
	public partial class MainWindow : Window
	{
		private ConfigManager? _configManager;
		private StorageManager? _storageManager;
		private PrivacyFilter? _privacyFilter;
		private ScreenshotService? _screenshotService;

		public MainWindow()
		{
			InitializeComponent();
			Title = $"PictureDay - Version: {App.Version}";
			Loaded += MainWindow_Loaded;
			SettingsViewControl.SettingsSaved += SettingsViewControl_SettingsSaved;
			KeyDown += MainWindow_KeyDown;
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			_configManager = (ConfigManager?)Application.Current.Resources["ConfigManager"];
			_storageManager = (StorageManager?)Application.Current.Resources["StorageManager"];

			if (_configManager != null)
			{
				_privacyFilter = new PrivacyFilter(_configManager);
			}

			if (_storageManager != null && _configManager != null)
			{
				_screenshotService = new ScreenshotService(_storageManager, _configManager);
				PhotoGalleryViewControl.Initialize(_storageManager);
			}

			if (_configManager != null)
			{
				SettingsViewControl.Initialize(_configManager);
			}
		}

		private void SettingsViewControl_SettingsSaved(object? sender, EventArgs e)
		{
			if (_configManager != null)
			{
				_privacyFilter?.RefreshBlockedApplications();
			}

			if (_storageManager != null)
			{
				PhotoGalleryViewControl.Initialize(_storageManager);
			}
		}

		public void ShowSettingsTab()
		{
			MainTabControl.SelectedIndex = 1;
		}

		public void RefreshGallery()
		{
			if (_storageManager != null)
			{
				PhotoGalleryViewControl.Initialize(_storageManager);
			}
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			e.Cancel = true;
			WindowState = WindowState.Minimized;
			Hide();
		}

		private void TakeScreenshotButton_Click(object sender, RoutedEventArgs e)
		{
			TakeScreenshot();
		}

		private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.F12 || (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control))
			{
				TakeScreenshot();
				e.Handled = true;
			}
		}

		private void TakeScreenshot()
		{
			if (_screenshotService == null || _privacyFilter == null)
			{
				System.Windows.MessageBox.Show("Screenshot service not available.", "Error",
					System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
				return;
			}

			if (_privacyFilter.ShouldBlockScreenshot())
			{
				System.Windows.MessageBox.Show("Screenshot blocked: Privacy filter detected blocked applications or private browsing mode.",
					"Privacy Protection", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
				return;
			}

			string? screenshotPath = _screenshotService.CaptureScreen(isBackup: false, isUser: true);
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				System.Windows.MessageBox.Show($"Screenshot saved: {screenshotPath}", "Success",
					System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

				if (_storageManager != null)
				{
					PhotoGalleryViewControl.Initialize(_storageManager);
				}
			}
			else
			{
				System.Windows.MessageBox.Show("Failed to capture screenshot.", "Error",
					System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
			}
		}

		private void OpenPhotosButton_Click(object sender, RoutedEventArgs e)
		{
			if (_storageManager != null)
			{
				string photosDirectory = _storageManager.GetBaseDirectory();
				if (Directory.Exists(photosDirectory))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = photosDirectory,
						UseShellExecute = true
					});
				}
				else
				{
					System.Windows.MessageBox.Show($"Photos directory not found: {photosDirectory}", "Error",
						System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
				}
			}
		}

		private void DonateButton_Click(object sender, RoutedEventArgs e)
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://buymeacoffee.com/bigyap",
				UseShellExecute = true
			});
		}
	}
}
