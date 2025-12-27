using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PictureDay.Services
{
    public class ScreenshotService
    {
        private readonly StorageManager _storageManager;
        private readonly ConfigManager? _configManager;

        public ScreenshotService(StorageManager storageManager, ConfigManager? configManager = null)
        {
            _storageManager = storageManager;
            _configManager = configManager;
        }

        public string? CaptureScreen(bool isBackup = false)
        {
            try
            {
                Bitmap bitmap;

                if (_configManager != null && _configManager.Config.CaptureAllMonitors)
                {
                    bitmap = CaptureAllMonitors();
                }
                else
                {
                    Screen? targetScreen = GetTargetScreen();
                    if (targetScreen == null)
                    {
                        return null;
                    }
                    bitmap = CaptureScreen(targetScreen);
                }

                if (bitmap == null)
                {
                    return null;
                }

                string filePath = _storageManager.SaveScreenshot(bitmap, isBackup);
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing screenshot: {ex.Message}");
                return null;
            }
        }

        private Screen? GetTargetScreen()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length == 0)
            {
                return Screen.PrimaryScreen;
            }

            if (_configManager != null && _configManager.Config.SelectedMonitorIndex >= 0 &&
                _configManager.Config.SelectedMonitorIndex < screens.Length)
            {
                return screens[_configManager.Config.SelectedMonitorIndex];
            }

            return Screen.PrimaryScreen;
        }

        private Bitmap CaptureScreen(Screen screen)
        {
            Rectangle bounds = screen.Bounds;
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            return bitmap;
        }

        private Bitmap CaptureAllMonitors()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length == 0)
            {
                return CaptureScreen(Screen.PrimaryScreen!);
            }

            int minX = screens.Min(s => s.Bounds.X);
            int minY = screens.Min(s => s.Bounds.Y);
            int maxX = screens.Max(s => s.Bounds.Right);
            int maxY = screens.Max(s => s.Bounds.Bottom);

            int totalWidth = maxX - minX;
            int totalHeight = maxY - minY;

            Bitmap bitmap = new Bitmap(totalWidth, totalHeight);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(minX, minY, 0, 0, new Size(totalWidth, totalHeight));

            return bitmap;
        }
    }
}

