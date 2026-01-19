using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PictureDay.Utils;

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

	public string? CaptureScreen(bool isBackup = false, bool isQuarter = false, bool isUser = false)
	{
		DateTime now = DateTime.Now;
		string photoType = isUser ? "USER" : (isBackup ? "BACKUP" : (isQuarter ? "QUARTER" : "MAIN"));
		Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ScreenshotService.CaptureScreen called - Type: {photoType}");

			List<IntPtr> minimizedWindows = new List<IntPtr>();

			try
			{
				if (_configManager != null && _configManager.Config.DesktopOnly)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Minimizing windows for desktop-only capture...");
					minimizedWindows = MinimizeAllWindows();
					Thread.Sleep(500);
					System.Windows.Forms.Application.DoEvents();
				}

				Bitmap bitmap;

				if (_configManager != null && _configManager.Config.CaptureAllMonitors)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Capturing all monitors...");
					bitmap = CaptureAllMonitors();
				}
				else
				{
					Screen? targetScreen = GetTargetScreen();
					if (targetScreen == null)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: Target screen is null");
						RestoreWindows(minimizedWindows);
						return null;
					}
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Capturing single screen: {targetScreen.Bounds}");
					bitmap = CaptureScreen(targetScreen);
				}

				if (bitmap == null)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: Bitmap is null after capture");
					RestoreWindows(minimizedWindows);
					return null;
				}

		Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Bitmap captured: {bitmap.Width}x{bitmap.Height}, saving...");
		string filePath = _storageManager.SaveScreenshot(bitmap, isBackup, isQuarter, isUser);
		RestoreWindows(minimizedWindows);

				if (!string.IsNullOrEmpty(filePath))
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Screenshot saved to: {filePath}");
				}
				else
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: SaveScreenshot returned null or empty path");
				}

				return filePath;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR capturing screenshot: {ex.Message}");
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Stack trace: {ex.StackTrace}");
				System.Diagnostics.Debug.WriteLine($"Error capturing screenshot: {ex.Message}");
				RestoreWindows(minimizedWindows);
				return null;
			}
		}

		private List<IntPtr> MinimizeAllWindows()
		{
			List<IntPtr> minimizedWindows = new List<IntPtr>();

			WindowHelper.EnumWindows((hWnd, lParam) =>
			{
				if (!WindowHelper.IsWindowVisible(hWnd))
				{
					return true;
				}

				string? windowTitle = WindowHelper.GetWindowTitle(hWnd);
				if (string.IsNullOrEmpty(windowTitle))
				{
					return true;
				}

				if (windowTitle.Equals("Program Manager", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}

				if (!WindowHelper.IsMinimized(hWnd))
				{
					WindowHelper.MinimizeWindow(hWnd);
					minimizedWindows.Add(hWnd);
				}

				return true;
			}, IntPtr.Zero);

			return minimizedWindows;
		}

		private void RestoreWindows(List<IntPtr> windows)
		{
			foreach (IntPtr hWnd in windows)
			{
				try
				{
					WindowHelper.RestoreWindow(hWnd);
				}
				catch
				{
				}
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

