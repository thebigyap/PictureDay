using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PictureDay.Utils;

namespace PictureDay.Services
{
	public class PrivacyFilter
	{
		private readonly ConfigManager _configManager;
		private static readonly string[] PrivacyPatterns = { "Incognito", "Private", "InPrivate" };

		public PrivacyFilter(ConfigManager configManager)
		{
			_configManager = configManager;
		}

		public bool ShouldBlockScreenshot()
		{
			if (!IsInteractiveDesktopAvailable())
			{
				return true;
			}

			try
			{
				var blockedApps = new HashSet<string>(
					(_configManager.Config.BlockedApplications ?? new List<string>())
						.Where(app => !string.IsNullOrWhiteSpace(app))
						.Select(NormalizeProcessName), StringComparer.OrdinalIgnoreCase);
				if (blockedApps.Count > 0 && IsBlockedApplicationRunning(blockedApps))
				{
					return true;
				}
				bool blocked = false;

				bool enumerated = WindowHelper.EnumWindows((hWnd, lParam) =>
				{
					if (!WindowHelper.IsWindowVisible(hWnd))
					{
						return true;
					}

					string? title = WindowHelper.GetWindowTitle(hWnd);
					if (!string.IsNullOrEmpty(title) && PrivacyPatterns.Any(pattern =>
						title.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
					{
						blocked = true;
						return false;
					}

					return true;
				}, IntPtr.Zero);

				// If Windows cannot enumerate the desktop, do not assume it is safe.
				return blocked || !enumerated;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Privacy check failed: {ex.Message}");
				return true;
			}
		}

		public static bool IsInteractiveDesktopAvailable()
		{
			IntPtr sessionInfo = IntPtr.Zero;
			IntPtr desktop = IntPtr.Zero;
			try
			{
				// OpenInputDesktop can succeed in a disconnected remote session, so check both.
				const int WtsCurrentSession = -1;
				const int WtsConnectState = 8;
				if (!WTSQuerySessionInformation(IntPtr.Zero, WtsCurrentSession, WtsConnectState,
					out sessionInfo, out int bytes) || sessionInfo == IntPtr.Zero || bytes < sizeof(int)
					|| Marshal.ReadInt32(sessionInfo) != 0)
				{
					return false;
				}

				const uint DesktopReadObjects = 0x0001;
				desktop = OpenInputDesktop(0, false, DesktopReadObjects);
				if (desktop == IntPtr.Zero)
				{
					return false;
				}

				const int UoiName = 2;
				var name = new StringBuilder(256);
				return GetUserObjectInformation(desktop, UoiName, name, name.Capacity * sizeof(char), out _)
					&& name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Desktop availability check failed: {ex.Message}");
				return false;
			}
			finally
			{
				if (desktop != IntPtr.Zero) CloseDesktop(desktop);
				if (sessionInfo != IntPtr.Zero) WTSFreeMemory(sessionInfo);
			}
		}

		private static string NormalizeProcessName(string name)
		{
			string normalized = Path.GetFileName(name.Trim().Trim('"'));
			return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? normalized[..^4] : normalized;
		}

		private static bool IsBlockedApplicationRunning(HashSet<string> blockedApps)
		{
			// Include applications minimized to the tray and those with no visible window.
			Process[] processes = Process.GetProcesses();
			try
			{
				foreach (Process process in processes)
				{
					try
					{
						if (blockedApps.Contains(process.ProcessName)) return true;
					}
					catch (InvalidOperationException)
					{
						// Processes may exit between the snapshot and name lookup.
					}
					catch (System.ComponentModel.Win32Exception)
					{
						// Windows can deny access to protected system processes.
					}
				}
				return false;
			}
			finally
			{
				// Dispose the entire snapshot even when a match ends the search early.
				foreach (Process process in processes) process.Dispose();
			}
		}

		public void RefreshBlockedApplications()
		{
			// Each check reads current settings; no persistent cache needs invalidation.
		}

		[DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass,
			out IntPtr buffer, out int bytesReturned);

		[DllImport("wtsapi32.dll")]
		private static extern void WTSFreeMemory(IntPtr memory);

		[DllImport("user32.dll")]
		private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info,
			int length, out int requiredLength);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CloseDesktop(IntPtr desktop);
	}
}
