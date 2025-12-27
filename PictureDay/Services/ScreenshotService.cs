using System;
using System.Drawing;
using System.Windows.Forms;

namespace PictureDay.Services
{
    public class ScreenshotService
    {
        private readonly StorageManager _storageManager;

        public ScreenshotService(StorageManager storageManager)
        {
            _storageManager = storageManager;
        }

        public string? CaptureScreen(bool isBackup = false)
        {
            try
            {
                Screen? primaryScreen = Screen.PrimaryScreen;
                if (primaryScreen == null)
                {
                    return null;
                }
                Rectangle bounds = primaryScreen.Bounds;

                using Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

                Bitmap bitmapToSave = new Bitmap(bitmap);
                string filePath = _storageManager.SaveScreenshot(bitmapToSave, isBackup);
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing screenshot: {ex.Message}");
                return null;
            }
        }
    }
}

