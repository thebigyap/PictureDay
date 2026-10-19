using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using PictureDay.Services;
using PictureDay.Views;
using Application = System.Windows.Application;

namespace PictureDay
{
	public partial class App : Application
	{
		public const string Version = "2.7.1";

		private NotifyIcon? _notifyIcon;
		private NotificationService? _notificationService;
		private ConfigManager? _configManager;
		private DailyScheduler? _dailyScheduler;
		private ScreenshotService? _screenshotService;
		private PrivacyFilter? _privacyFilter;
		private StorageManager? _storageManager;
		private UpdateService? _updateService;
		private static bool _isShuttingDownForUpdate = false;
		private bool _manualCaptureInProgress;

		[DllImport("kernel32.dll")]
		private static extern bool AllocConsole();

		private void DebugWriteLine(string message)
		{
		#if DEBUG
			Console.WriteLine(message);
		#endif
		}

		private void InitializeDebugConsole()
		{
		#if DEBUG
			try
			{
				AllocConsole();
				DebugWriteLine("Debug console allocated.");
			}
			catch
			{
			}
		#endif
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			try
			{
				InitializeDebugConsole();
				DebugWriteLine("PictureDay starting...");
				DebugWriteLine("Initializing ConfigManager...");
				_configManager = new ConfigManager();

				string theme = _configManager.Config.Theme ?? "Light";
				ApplyTheme(theme);
				DebugWriteLine($"Config loaded. Screenshot directory: {_configManager.Config.ScreenshotDirectory}");
				DebugWriteLine("Initializing StorageManager...");
				_storageManager = new StorageManager(
					_configManager.Config.ScreenshotDirectory,
					_configManager.Config.Quality,
					_configManager.Config.ImageFormat);

				DebugWriteLine("Initializing ActivityMonitor...");
				var activityMonitor = new ActivityMonitor();
				DebugWriteLine("Initializing PrivacyFilter...");
				_privacyFilter = new PrivacyFilter(_configManager);
				DebugWriteLine("Initializing ScreenshotService...");
				_screenshotService = new ScreenshotService(_storageManager, _configManager);
				DebugWriteLine("Initializing DailyScheduler...");
				_dailyScheduler = new DailyScheduler(
					_configManager,
					activityMonitor,
					_privacyFilter,
					_screenshotService,
					_storageManager);

				DebugWriteLine("Setting up system tray...");
				SetupSystemTray();
				DebugWriteLine("System tray setup complete.");

				if (_notifyIcon != null)
				{
					_notificationService = new NotificationService(_notifyIcon);
				}

				_dailyScheduler.PhotosProcessed += (s, e) =>
				{
					Application.Current.Dispatcher.BeginInvoke(new Action(() =>
					{
						if (MainWindow is MainWindow mainWin)
						{
							mainWin.RefreshGallery();
							DebugWriteLine("Photo gallery refreshed after midnight processing.");
						}
						ShrinkOldPhotosInBackground();
					}));
				};

				_dailyScheduler.MainPhotoTaken += _ =>
				{
					Application.Current.Dispatcher.BeginInvoke(new Action(() =>
					{
						_notificationService?.ShowMainPhotoTaken();
						if (MainWindow is MainWindow mainWin) { mainWin.RefreshGallery(); mainWin.UpdateCaptureStatus(); }
					}));
				};

				_dailyScheduler.ScheduledTimeChanged += (s, e) =>
				{
					Application.Current.Dispatcher.BeginInvoke(new Action(() =>
					{
						_notificationService?.ShowScheduledMainPhotoTimeUpdated(_dailyScheduler.ScheduledTime);
						if (MainWindow is MainWindow mainWin) mainWin.UpdateCaptureStatus();
					}));
				};

				_dailyScheduler.Start();
				DebugWriteLine("DailyScheduler started.");

				if (_notifyIcon != null)
				{
					if (_dailyScheduler.IsDayCompleted)
					{
						_notificationService?.ShowMainPhotoAlreadyTaken();
					}
					else if (_configManager.Config.TodayScheduledTime.HasValue &&
					         _configManager.Config.ScheduledTimeDate?.Date == DateTime.Today)
					{
						_notificationService?.ShowScheduledMainPhotoTime(_configManager.Config.TodayScheduledTime.Value);
					}
					else
					{
						_notificationService?.ShowScheduledMainPhotoTime(_dailyScheduler.ScheduledTime);
					}
				}

				if (MainWindow != null)
				{
					MainWindow.WindowState = WindowState.Minimized;
					MainWindow.Hide();
				}
				Resources["ConfigManager"] = _configManager;
				Resources["StorageManager"] = _storageManager;
				Resources["DailyScheduler"] = _dailyScheduler;
				Resources["ScreenshotService"] = _screenshotService;
				Resources["PrivacyFilter"] = _privacyFilter;
				MainWindow = new MainWindow { WindowState = WindowState.Minimized };
				MainWindow.Show();
				MainWindow.Hide();
				ShrinkOldPhotosInBackground();

				DebugWriteLine("Initializing UpdateService...");
				_updateService = new UpdateService();
				string appDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
				_ = Task.Run(async () =>
				{
					await Task.Delay(3000);
					if (!_updateService.IsUpdaterRunning())
					{
						await _updateService.StartUpdaterAsync(appDirectory);
					}
				});
				Resources["UpdateService"] = _updateService;

				DebugWriteLine("PictureDay started successfully!");
				DebugWriteLine("Press any key to close this console (app will continue running)...");
			}
			catch (Exception ex)
			{
				string errorMsg = $"Fatal error during startup: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
				DebugWriteLine(errorMsg);
				System.Windows.MessageBox.Show(errorMsg, "PictureDay Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
				Shutdown();
			}
		}

		private void SetupSystemTray()
		{
			Icon? trayIcon = null;
			try
			{
				string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Camera.ico");
				if (System.IO.File.Exists(iconPath))
				{
					trayIcon = new Icon(iconPath);
				}
			}
			catch
			{
			}

			_notifyIcon = new NotifyIcon
			{
				Icon = trayIcon ?? SystemIcons.Application,
				Visible = true,
				Text = "PictureDay"
			};

			_notifyIcon.DoubleClick += (sender, e) =>
			{
				if (!MainWindow.IsVisible || MainWindow.WindowState == WindowState.Minimized)
				{
					MainWindow.Show();
					MainWindow.WindowState = WindowState.Normal;
					MainWindow.Activate();
				}
				else
				{
					MainWindow.Hide();
					MainWindow.WindowState = WindowState.Minimized;
				}
			};

			ContextMenuStrip contextMenu = new ContextMenuStrip();
			contextMenu.Items.Add("Show PictureDay", null, (s, e) =>
			{
				MainWindow.Show();
				MainWindow.WindowState = WindowState.Normal;
				MainWindow.Activate();
			});
			contextMenu.Items.Add("-");
			contextMenu.Items.Add("Take Screenshot Now", null, async (s, e) => await TakeManualScreenshotAsync());
			contextMenu.Items.Add("Settings", null, (s, e) =>
			{
				MainWindow.Show();
				MainWindow.WindowState = WindowState.Normal;
				MainWindow.Activate();
				if (MainWindow is MainWindow mainWin)
				{
					mainWin.ShowSettingsTab();
				}
			});
			contextMenu.Items.Add("Check for Updates", null, (s, e) => CheckForUpdates());
			contextMenu.Items.Add("-");
			contextMenu.Items.Add("Exit", null, (s, e) => Shutdown());
			_notifyIcon.ContextMenuStrip = contextMenu;
		}

		/// <summary>Runs at startup, after midnight processing, and after settings are saved.</summary>
		public async void ShrinkOldPhotosInBackground()
		{
			if (_storageManager is not { } storage || _configManager?.Config.ShrinkOldPhotos != true) return;
			try
			{
				int shrunk = await Task.Run(() => storage.ShrinkPhotosOlderThan(DateTime.Today.AddMonths(-3)));
				if (shrunk > 0 && MainWindow is MainWindow mainWin) mainWin.RefreshGallery();
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Shrinking old photos failed: {ex}"); }
		}

		public async Task TakeManualScreenshotAsync()
		{
			if (_manualCaptureInProgress || _screenshotService == null || _privacyFilter == null) return;
			_manualCaptureInProgress = true;
			var window = MainWindow as MainWindow;
			bool restoreWindow = window?.IsVisible == true && window.WindowState != WindowState.Minimized;
			WindowState previousState = window?.WindowState ?? WindowState.Normal;
			string message = "Could not capture your screen. Check privacy settings and the photo folder.";
			bool success = false;
			window?.SetCaptureBusy(true);
			try
			{
				if (await Task.Run(() => _privacyFilter.ShouldBlockScreenshot()))
				{
					message = "Capture blocked by a privacy check. Your screen was not saved.";
				}
				else
				{
					if (restoreWindow) { window!.Hide(); await Task.Delay(250); }
					string? path = await Task.Run(() => _screenshotService.CaptureScreen(isUser: true));
					success = !string.IsNullOrEmpty(path);
					if (success) { message = "Screenshot saved to your photo folder"; window?.RefreshGallery(); }
				}
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
			finally
			{
				_manualCaptureInProgress = false;
				window?.SetCaptureBusy(false);
				if (restoreWindow && !Dispatcher.HasShutdownStarted)
				{
					window!.Show();
					window.WindowState = previousState;
					window.Activate();
				}
			}
			if (window?.IsVisible == true) window.ShowToast(message, success);
			else _notifyIcon?.ShowBalloonTip(3000, "PictureDay", message, success ? ToolTipIcon.Info : ToolTipIcon.Warning);
		}

		private void CheckForUpdates()
		{
			if (_updateService == null)
			{
				System.Windows.MessageBox.Show("Update service not available.", "Error",
					System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
				return;
			}

			UpdateWindow updateWindow = new UpdateWindow();
			updateWindow.Initialize(_updateService);
			updateWindow.Show();

			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(1000);
					string appDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

					if (!_updateService.IsConnected())
					{
						if (!_updateService.IsUpdaterRunning())
						{
							if (!await _updateService.StartUpdaterAsync(appDirectory))
							{
								return;
							}
							await Task.Delay(1500);
						}
						else
						{
							if (!await _updateService.ConnectToUpdaterAsync())
							{
								if (!await _updateService.StartUpdaterAsync(appDirectory))
								{
									return;
								}
								await Task.Delay(1500);
							}
						}
					}

					await _updateService.CheckForUpdatesAsync();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Error in CheckForUpdates: {ex.Message}");
				}
			});
		}

		public void ApplyTheme(string theme)
		{
			var mergedDicts = Resources.MergedDictionaries;
			mergedDicts.Clear();

			ResourceDictionary themeDict = new ResourceDictionary();
			if (theme == "Dark")
			{
				themeDict.Source = new Uri("/PictureDay;component/Themes/DarkTheme.xaml", UriKind.Relative);
			}
			else
			{
				themeDict.Source = new Uri("/PictureDay;component/Themes/LightTheme.xaml", UriKind.Relative);
			}

			mergedDicts.Add(themeDict);
		}

		protected override void OnExit(System.Windows.ExitEventArgs e)
		{
			_dailyScheduler?.Stop();

			if (!_isShuttingDownForUpdate)
			{
				_updateService?.KillUpdater();
			}

			_updateService?.Dispose();
			_notifyIcon?.Dispose();

			base.OnExit(e);
		}

		public static void SetShuttingDownForUpdate(bool value)
		{
			_isShuttingDownForUpdate = value;
		}
	}
}
