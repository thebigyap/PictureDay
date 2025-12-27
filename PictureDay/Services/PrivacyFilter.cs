using System;
using System.Collections.Generic;
using System.Linq;
using PictureDay.Utils;

namespace PictureDay.Services
{
    public class PrivacyFilter
    {
        private readonly ConfigManager _configManager;
        private readonly List<string> _privacyPatterns = new List<string>
        {
            "Incognito",
            "Private",
            "InPrivate"
        };

        public PrivacyFilter(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public bool ShouldBlockScreenshot()
        {
            List<string> blockedApps = new List<string>(_configManager.Config.BlockedApplications);
            List<string> foundBlockedApps = new List<string>();

            bool blockScreenshot = false;

            WindowHelper.EnumWindows((hWnd, lParam) =>
            {
                if (!WindowHelper.IsWindowVisible(hWnd))
                {
                    return true;
                }

                string? windowTitle = WindowHelper.GetWindowTitle(hWnd);
                if (!string.IsNullOrEmpty(windowTitle))
                {
                    foreach (string pattern in _privacyPatterns)
                    {
                        if (windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            blockScreenshot = true;
                            return false;
                        }
                    }
                }

                string? processName = WindowHelper.GetProcessNameFromWindow(hWnd);
                if (!string.IsNullOrEmpty(processName))
                {
                    foreach (string blockedApp in blockedApps)
                    {
                        string blockedAppName = blockedApp.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                        if (processName.Equals(blockedAppName, StringComparison.OrdinalIgnoreCase) ||
                            processName.Equals(blockedApp, StringComparison.OrdinalIgnoreCase))
                        {
                            foundBlockedApps.Add(blockedApp);
                            blockScreenshot = true;
                            return false;
                        }
                    }
                }

                return true;
            }, IntPtr.Zero);

            return blockScreenshot;
        }

        public void RefreshBlockedApplications()
        {
        }
    }
}

