using System;
using System.Collections.Generic;
using System.Diagnostics;
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
					System.Diagnostics.Debug.WriteLine($"Config loaded - Theme: {_config.Theme}, BlockedApps: {_config.BlockedApplications.Count}");
					Console.WriteLine($"Config loaded - Theme: {_config.Theme}, BlockedApps: {_config.BlockedApplications.Count}");
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}\nStack trace: {ex.StackTrace}");
					Console.WriteLine($"Error loading config: {ex.Message}");
					_config = new AppConfig();
				}
			}
			else
			{
				System.Diagnostics.Debug.WriteLine($"Config file not found at: {_configPath}, using defaults");
				Console.WriteLine($"Config file not found at: {_configPath}, using defaults");
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
				string? configDir = Path.GetDirectoryName(_configPath);
				if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
				{
					Directory.CreateDirectory(configDir);
				}

				string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
				File.WriteAllText(_configPath, json);
				UpdateStartupRegistry();
				System.Diagnostics.Debug.WriteLine($"Config saved successfully to: {_configPath}");
				Console.WriteLine($"Config saved successfully to: {_configPath}");

				if (File.Exists(_configPath))
				{
					string savedJson = File.ReadAllText(_configPath);
					var verifyConfig = JsonConvert.DeserializeObject<AppConfig>(savedJson);
					if (verifyConfig != null)
					{
						System.Diagnostics.Debug.WriteLine($"Config verified - Theme: {verifyConfig.Theme}, BlockedApps: {verifyConfig.BlockedApplications.Count}");
						Console.WriteLine($"Config verified - Theme: {verifyConfig.Theme}, BlockedApps: {verifyConfig.BlockedApplications.Count}");
					}
				}
			}
			catch (Exception ex)
			{
				string errorMsg = $"Error saving config: {ex.Message}\nStack trace: {ex.StackTrace}";
				System.Diagnostics.Debug.WriteLine(errorMsg);
				Console.WriteLine(errorMsg);
				System.Windows.MessageBox.Show($"Failed to save configuration:\n{ex.Message}\n\nPath: {_configPath}",
					"Configuration Error",
					System.Windows.MessageBoxButton.OK,
					System.Windows.MessageBoxImage.Warning);
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

				string? appPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
				if (string.IsNullOrEmpty(appPath))
				{
					System.Diagnostics.Debug.WriteLine("Could not get executable path");
					return;
				}

				if (!appPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
				{
					string directory = Path.GetDirectoryName(appPath) ?? "";
					string exeName = Path.GetFileNameWithoutExtension(appPath) + ".exe";
					appPath = Path.Combine(directory, exeName);
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