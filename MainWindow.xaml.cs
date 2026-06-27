using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
			VersionText.Text = $"v{App.Version}";
			Loaded += MainWindow_Loaded;
			SettingsViewControl.SettingsSaved += SettingsViewControl_SettingsSaved;
			SettingsViewControl.RequestNavigateHome += (s, e) => GalleryNav.IsChecked = true;
			KeyDown += MainWindow_KeyDown;
		}

		private void GalleryNav_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			// Intercept navigating away from Settings while there are unsaved changes
			if (SettingsNav.IsChecked == true && SettingsViewControl.HasUnsavedChanges)
			{
				e.Handled = true; // block the nav until the user decides

				MessageBoxResult choice = SettingsViewControl.PromptUnsavedChanges();
				if (choice == MessageBoxResult.Yes)
				{
					// Save() navigates home + shows the toast on success (via SettingsSaved)
					SettingsViewControl.SaveSettings();
				}
				else if (choice == MessageBoxResult.No)
				{
					SettingsViewControl.DiscardChanges();
					GalleryNav.IsChecked = true;
				}
				// Cancel: stay on Settings
			}
		}

		private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed)
			{
				DragMove();
			}
		}

		private void MinimizeButton_Click(object sender, RoutedEventArgs e)
		{
			WindowState = WindowState.Minimized;
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e)
		{
			// Match the close behavior: keep running in the tray
			WindowState = WindowState.Minimized;
			Hide();
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

			// Saving closes Settings and confirms with a toast
			GalleryNav.IsChecked = true;
			ShowToast("Settings saved");
		}

		private DispatcherTimer? _toastTimer;

		public void ShowToast(string message)
		{
			ToastText.Text = message;
			ToastHost.Visibility = Visibility.Visible;

			var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)));
			var slideUp = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(280)))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};
			ToastHost.BeginAnimation(OpacityProperty, fadeIn);
			ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);

			_toastTimer?.Stop();
			_toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
			_toastTimer.Tick += (s, e) =>
			{
				_toastTimer!.Stop();
				var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(320)));
				fadeOut.Completed += (s2, e2) => ToastHost.Visibility = Visibility.Collapsed;
				ToastHost.BeginAnimation(OpacityProperty, fadeOut);
			};
			_toastTimer.Start();
		}

		public void ShowSettingsTab()
		{
			SettingsNav.IsChecked = true;
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

		private void PatreonButton_Click(object sender, RoutedEventArgs e)
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://patreon.com/bigyap",
				UseShellExecute = true
			});
		}
	}
}
