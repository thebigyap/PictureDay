using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Newtonsoft.Json;
using PictureDay.Models;

namespace PictureDay.Services
{
    public class ConfigManager
    {
        private readonly string _configPath;
        private AppConfig _config = null!;
        private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "PictureDay";

        public AppConfig Config => _config;

        public ConfigManager()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appDataPath, "PictureDay");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "config.json");
            LoadConfig();
            UpdateStartupRegistry();
        }

        public void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                catch
                {
                    _config = new AppConfig();
                }
            }
            else
            {
                _config = new AppConfig();
            }

            if (string.IsNullOrEmpty(_config.ScreenshotDirectory))
            {
                _config.ScreenshotDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "PictureDay");
            }
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                UpdateStartupRegistry();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        public void AddBlockedApplication(string processName)
        {
            if (!string.IsNullOrWhiteSpace(processName) &&
                !_config.BlockedApplications.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                _config.BlockedApplications.Add(processName);
            }
        }

        public void RemoveBlockedApplication(string processName)
        {
            _config.BlockedApplications.RemoveAll(x =>
                x.Equals(processName, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateStartupRegistry()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not open registry key for startup");
                    return;
                }

                string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(appPath))
                {
                    System.Diagnostics.Debug.WriteLine("Could not get assembly location");
                    return;
                }

                if (_config.StartWithWindows)
                {
                    key.SetValue(AppName, $"\"{appPath}\"");
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating registry: {ex.Message}");
                Console.WriteLine($"Warning: Could not update startup registry: {ex.Message}");
            }
        }
    }
}