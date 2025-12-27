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
        public const string Version = "1.0.0";

        private NotifyIcon? _notifyIcon;
        private ConfigManager? _configManager;
        private DailyScheduler? _dailyScheduler;

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
                var storageManager = new StorageManager(
                    _configManager.Config.ScreenshotDirectory,
                    _configManager.Config.Quality);

                Console.WriteLine("Initializing ActivityMonitor...");
                var activityMonitor = new ActivityMonitor();
                Console.WriteLine("Initializing PrivacyFilter...");
                var privacyFilter = new PrivacyFilter(_configManager);
                Console.WriteLine("Initializing ScreenshotService...");
                var screenshotService = new ScreenshotService(storageManager);
                Console.WriteLine("Initializing DailyScheduler...");
                _dailyScheduler = new DailyScheduler(
                    _configManager,
                    activityMonitor,
                    privacyFilter,
                    screenshotService,
                    storageManager);
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
                Resources["StorageManager"] = storageManager;
                Resources["DailyScheduler"] = _dailyScheduler;
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

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _dailyScheduler?.Stop();
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
