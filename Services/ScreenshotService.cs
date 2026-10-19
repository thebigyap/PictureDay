using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
		private readonly PrivacyFilter? _privacyFilter;
		private int _captureInProgress;

		public ScreenshotService(StorageManager storageManager, ConfigManager? configManager = null)
		{
			_storageManager = storageManager;
			_configManager = configManager;
			_privacyFilter = configManager == null ? null : new PrivacyFilter(configManager);
		}

		public string? CaptureScreen(bool isBackup = false, bool isQuarter = false, bool isUser = false)
		{
			// Timer and manual requests share this service. Never minimize the desktop twice.
			if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
			{
				return null;
			}

			try
			{
				if (ShouldBlockCapture())
				{
					return null;
				}

				using Bitmap? bitmap = CaptureBitmap();
				if (bitmap == null || ShouldBlockCapture())
				{
					return null;
				}

				string filePath = _storageManager.SaveScreenshot(bitmap, isBackup, isQuarter, isUser);
				Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Screenshot saved to: {filePath}");
				return filePath;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error capturing screenshot: {ex.Message}");
				System.Diagnostics.Debug.WriteLine(ex);
				return null;
			}
			finally
			{
				Interlocked.Exchange(ref _captureInProgress, 0);
			}
		}

		private bool ShouldBlockCapture() => _privacyFilter?.ShouldBlockScreenshot()
			?? !PrivacyFilter.IsInteractiveDesktopAvailable();

		private Bitmap? CaptureBitmap()
		{
			var minimizedWindows = new List<(IntPtr Handle, WindowPlacement Placement)>();
			try
			{
				if (_configManager?.Config.DesktopOnly == true)
				{
					// Keep the list in the caller so even a partial enumeration can be restored.
					MinimizeAllWindows(minimizedWindows);
					Thread.Sleep(500);
					if (ShouldBlockCapture())
					{
						return null;
					}
				}

				if (_configManager?.Config.CaptureAllMonitors == true)
				{
					return CaptureAllMonitors();
				}

				Screen? screen = GetTargetScreen();
				return screen == null ? null : CaptureBounds(screen.Bounds);
			}
			finally
			{
				// Restore before encoding or disk I/O, including failed screen copies.
				RestoreWindows(minimizedWindows);
			}
		}

		private static void MinimizeAllWindows(List<(IntPtr Handle, WindowPlacement Placement)> windows)
		{
			WindowHelper.EnumWindows((hWnd, lParam) =>
			{
				if (!WindowHelper.IsWindowVisible(hWnd) || WindowHelper.IsMinimized(hWnd))
				{
					return true;
				}

				string? title = WindowHelper.GetWindowTitle(hWnd);
				if (string.IsNullOrEmpty(title) || title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}

				var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
				if (GetWindowPlacement(hWnd, ref placement))
				{
					windows.Add((hWnd, placement));
					WindowHelper.MinimizeWindow(hWnd);
				}
				return true;
			}, IntPtr.Zero);
		}

		private static void RestoreWindows(List<(IntPtr Handle, WindowPlacement Placement)> windows)
		{
			// EnumWindows returns foreground windows first; restore them last.
			for (int i = windows.Count - 1; i >= 0; i--)
			{
				try
				{
					var (handle, placement) = windows[i];
					if (WindowHelper.IsMinimized(handle))
					{
						SetWindowPlacement(handle, ref placement);
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Could not restore window: {ex.Message}");
				}
			}
		}

		private Screen? GetTargetScreen()
		{
			Screen[] screens = Screen.AllScreens;
			int selectedIndex = _configManager?.Config.SelectedMonitorIndex ?? -1;
			if (selectedIndex >= 0 && selectedIndex < screens.Length)
			{
				return screens[selectedIndex];
			}
			return Screen.PrimaryScreen ?? screens.FirstOrDefault();
		}

		private static Bitmap CaptureBounds(Rectangle bounds)
		{
			var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
			try
			{
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
				return bitmap;
			}
			catch
			{
				bitmap.Dispose();
				throw;
			}
		}

		private static Bitmap? CaptureAllMonitors()
		{
			Screen[] screens = Screen.AllScreens;
			if (screens.Length == 0)
			{
				return null;
			}
			Rectangle bounds = screens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
			return CaptureBounds(bounds);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct WindowPlacement
		{
			public int Length;
			public int Flags;
			public int ShowCommand;
			public Point MinPosition;
			public Point MaxPosition;
			public NativeRectangle NormalPosition;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRectangle
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);
	}
}
