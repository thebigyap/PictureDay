using System;
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
        public const string Version = "2.0.0";

        private NotifyIcon? _notifyIcon;
        private ConfigManager? _configManager;
        private DailyScheduler? _dailyScheduler;
        private ScreenshotService? _screenshotService;
        private PrivacyFilter? _privacyFilter;
        private StorageManager? _storageManager;
        private UpdateService? _updateService;

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
                _dailyScheduler.Start();
                DebugWriteLine("DailyScheduler started.");
                DebugWriteLine("Setting up system tray...");
                SetupSystemTray();
                DebugWriteLine("System tray setup complete.");
                if (MainWindow != null)
                {
                    MainWindow.WindowState = WindowState.Minimized;
                    MainWindow.Hide();
                }
                Resources["ConfigManager"] = _configManager;
                Resources["StorageManager"] = _storageManager;
                Resources["DailyScheduler"] = _dailyScheduler;
                Resources["ScreenshotService"] = _screenshotService;

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
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "PictureDay"
            };

            _notifyIcon.DoubleClick += (sender, e) =>
            {
                if (MainWindow.WindowState == WindowState.Minimized)
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
            contextMenu.Items.Add("Take Screenshot Now", null, (s, e) => TakeManualScreenshot());
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

        private void TakeManualScreenshot()
        {
            if (_screenshotService == null || _privacyFilter == null || _storageManager == null)
            {
                System.Windows.MessageBox.Show("Screenshot service not available.", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (_privacyFilter.ShouldBlockScreenshot())
            {
                _notifyIcon?.ShowBalloonTip(3000, "PictureDay",
                    "Screenshot blocked: Privacy filter detected blocked applications or private browsing mode.",
                    System.Windows.Forms.ToolTipIcon.Info);
                return;
            }

            string? screenshotPath = _screenshotService.CaptureScreen(isBackup: false);
            if (!string.IsNullOrEmpty(screenshotPath))
            {
                _notifyIcon?.ShowBalloonTip(3000, "PictureDay",
                    $"Screenshot saved successfully!", System.Windows.Forms.ToolTipIcon.Info);

                if (MainWindow is MainWindow mainWin)
                {
                    mainWin.RefreshGallery();
                }
            }
            else
            {
                _notifyIcon?.ShowBalloonTip(3000, "PictureDay",
                    "Failed to capture screenshot.", System.Windows.Forms.ToolTipIcon.Error);
            }
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
                await Task.Delay(1000);
                if (_updateService.IsUpdaterRunning())
                {
                    await _updateService.CheckForUpdatesAsync();
                }
                else
                {
                    string appDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    if (await _updateService.StartUpdaterAsync(appDirectory))
                    {
                        await Task.Delay(1000);
                        await _updateService.CheckForUpdatesAsync();
                    }
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
                themeDict.Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
            }
            else
            {
                themeDict.Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
            }

            mergedDicts.Add(themeDict);
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _dailyScheduler?.Stop();
            _updateService?.Dispose();
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
