using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using PictureDay.Services;
using Application = System.Windows.Application;

namespace PictureDay
{
    public partial class App : Application
    {
        public const string Version = "1.1.0";

        private NotifyIcon? _notifyIcon;
        private ConfigManager? _configManager;
        private DailyScheduler? _dailyScheduler;
        private ScreenshotService? _screenshotService;
        private PrivacyFilter? _privacyFilter;
        private StorageManager? _storageManager;

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                AllocateConsole();

                Console.WriteLine("PictureDay starting...");
                Console.WriteLine("Initializing ConfigManager...");
                _configManager = new ConfigManager();
                Console.WriteLine($"Config loaded. Screenshot directory: {_configManager.Config.ScreenshotDirectory}");

                Console.WriteLine("Initializing StorageManager...");
                _storageManager = new StorageManager(
                    _configManager.Config.ScreenshotDirectory,
                    _configManager.Config.Quality,
                    _configManager.Config.ImageFormat);

                Console.WriteLine("Initializing ActivityMonitor...");
                var activityMonitor = new ActivityMonitor();
                Console.WriteLine("Initializing PrivacyFilter...");
                _privacyFilter = new PrivacyFilter(_configManager);
                Console.WriteLine("Initializing ScreenshotService...");
                _screenshotService = new ScreenshotService(_storageManager, _configManager);
                Console.WriteLine("Initializing DailyScheduler...");
                _dailyScheduler = new DailyScheduler(
                    _configManager,
                    activityMonitor,
                    _privacyFilter,
                    _screenshotService,
                    _storageManager);
                _dailyScheduler.Start();
                Console.WriteLine("DailyScheduler started.");
                Console.WriteLine("Setting up system tray...");
                SetupSystemTray();
                Console.WriteLine("System tray setup complete.");
                if (MainWindow != null)
                {
                    MainWindow.WindowState = WindowState.Minimized;
                    MainWindow.Hide();
                }
                Resources["ConfigManager"] = _configManager;
                Resources["StorageManager"] = _storageManager;
                Resources["DailyScheduler"] = _dailyScheduler;
                Resources["ScreenshotService"] = _screenshotService;
                Console.WriteLine("PictureDay started successfully!");
                Console.WriteLine("Press any key to close this console (app will continue running)...");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Fatal error during startup: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                Console.WriteLine(errorMsg);
                System.Windows.MessageBox.Show(errorMsg, "PictureDay Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void AllocateConsole()
        {
            try
            {
                AllocConsole();
                Console.WriteLine("Debug console allocated.");
            }
            catch
            {
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
            contextMenu.Items.Add("Take Screenshot Now", null, (s, e) => TakeManualScreenshot());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Show PictureDay", null, (s, e) =>
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            });
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

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _dailyScheduler?.Stop();
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
