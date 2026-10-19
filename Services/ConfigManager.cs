using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;
using PictureDay.Models;

namespace PictureDay.Services
{
	public class ConfigManager
	{
		private readonly string _configPath;
		private readonly bool _updateStartupRegistry;
		private readonly object _configLock = new object();
		private AppConfig _config = null!;
		private bool _loadedFromBackup;
		private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
		private const string AppName = "PictureDay";

		public AppConfig Config => _config;

		public ConfigManager(string? configPath = null, bool updateStartupRegistry = true)
		{
			_configPath = Path.GetFullPath(configPath ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PictureDay", "config.json"));
			_updateStartupRegistry = updateStartupRegistry;
			LoadConfig();
			UpdateStartupRegistry();
		}

		public void LoadConfig()
		{
			lock (_configLock)
			{
				_loadedFromBackup = false;
				AppConfig? loaded = ReadConfig(_configPath);
				if (loaded == null)
				{
					loaded = ReadConfig(_configPath + ".bak");
					_loadedFromBackup = loaded != null;
					if (_loadedFromBackup)
					{
						Console.WriteLine("Recovered configuration from the last saved backup.");
					}
				}
				_config = loaded ?? new AppConfig();
				NormalizeConfig(_config);
			}
		}

		private static AppConfig? ReadConfig(string path)
		{
			if (!File.Exists(path)) return null;
			try
			{
				return JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(path),
					new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Error loading configuration {path}: {ex.Message}");
				Console.WriteLine($"Error loading configuration: {ex.Message}");
				return null;
			}
		}

		public void SaveConfig()
		{
			lock (_configLock)
			{
				SaveConfig(_config);
			}
		}

		public void SaveConfig(AppConfig config, bool preserveSchedule = false)
		{
			ArgumentNullException.ThrowIfNull(config);
			lock (_configLock)
			{
				// Stage a copy: failed validation or disk writes must not replace active settings.
				var candidate = JsonConvert.DeserializeObject<AppConfig>(JsonConvert.SerializeObject(config))
					?? throw new JsonSerializationException("Configuration cannot be empty.");
				if (preserveSchedule)
				{
					candidate.TodayScheduledTime = _config.TodayScheduledTime;
					candidate.ScheduledTimeDate = _config.ScheduledTimeDate;
				}
				NormalizeConfig(candidate);
				string configDirectory = Path.GetDirectoryName(_configPath)!;
				Directory.CreateDirectory(configDirectory);
				string temporaryPath = Path.Combine(configDirectory, $".config-{Guid.NewGuid():N}.tmp");
				try
				{
					using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
						FileShare.None, 4096, FileOptions.WriteThrough))
					{
						using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
						{
							writer.Write(JsonConvert.SerializeObject(candidate, Formatting.Indented));
						}
						stream.Flush(flushToDisk: true);
					}

					if (File.Exists(_configPath))
					{
						// Keep a known-good backup when recovering from a damaged primary file.
						File.Replace(temporaryPath, _configPath, _loadedFromBackup ? null : _configPath + ".bak");
					}
					else
					{
						File.Move(temporaryPath, _configPath);
					}

					_config = candidate;
					_loadedFromBackup = false;
					UpdateStartupRegistry();
				}
				finally
				{
					try
					{
						if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Could not remove temporary configuration: {ex.Message}");
					}
				}
			}
		}

		public void SetScheduledTime(TimeSpan time, DateTime date)
		{
			if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
			{
				throw new ArgumentOutOfRangeException(nameof(time), "Scheduled time must fall within one day.");
			}
			lock (_configLock)
			{
				var candidate = JsonConvert.DeserializeObject<AppConfig>(JsonConvert.SerializeObject(_config))!;
				candidate.TodayScheduledTime = time;
				candidate.ScheduledTimeDate = date.Date;
				SaveConfig(candidate);
			}
		}

		private static void NormalizeConfig(AppConfig config)
		{
			config.BlockedApplications = (config.BlockedApplications ?? new List<string>())
				.Where(app => !string.IsNullOrWhiteSpace(app))
				.Select(app => app.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (string.IsNullOrWhiteSpace(config.ScreenshotDirectory))
			{
				config.ScreenshotDirectory = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PictureDay");
			}
			config.Quality = Math.Clamp(config.Quality, 1, 100);
			config.ImageFormat = string.Equals(config.ImageFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? "PNG" : "JPEG";
			config.Theme = string.Equals(config.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
			config.SelectedMonitorIndex = Math.Max(-1, config.SelectedMonitorIndex);
			if (!Enum.IsDefined(config.ScheduleMode)) config.ScheduleMode = ScheduleMode.Random;
			config.TodayScheduledTime = NormalizeTime(config.TodayScheduledTime);
			config.FixedScheduledTime = NormalizeTime(config.FixedScheduledTime);
			config.ScheduleRangeStart = NormalizeTime(config.ScheduleRangeStart);
			config.ScheduleRangeEnd = NormalizeTime(config.ScheduleRangeEnd);
			if (!config.TodayScheduledTime.HasValue) config.ScheduledTimeDate = null;
		}

		private static TimeSpan? NormalizeTime(TimeSpan? time) => time.HasValue
			&& time.Value >= TimeSpan.Zero && time.Value < TimeSpan.FromDays(1) ? time : null;

		public void AddBlockedApplication(string processName)
		{
			if (string.IsNullOrWhiteSpace(processName)) return;
			lock (_configLock)
			{
				string normalized = processName.Trim();
				if (!_config.BlockedApplications.Contains(normalized, StringComparer.OrdinalIgnoreCase))
				{
					_config.BlockedApplications.Add(normalized);
				}
			}
		}

		public void RemoveBlockedApplication(string processName)
		{
			lock (_configLock)
			{
				_config.BlockedApplications.RemoveAll(x =>
					x.Equals(processName?.Trim(), StringComparison.OrdinalIgnoreCase));
			}
		}

		private void UpdateStartupRegistry()
		{
			if (!_updateStartupRegistry) return;
			try
			{
				using RegistryKey? key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, true);
				if (key == null) return;
				if (!_config.StartWithWindows)
				{
					key.DeleteValue(AppName, false);
					return;
				}

				string? appPath = Environment.ProcessPath;
				if (string.IsNullOrEmpty(appPath))
				{
					using Process process = Process.GetCurrentProcess();
					appPath = process.MainModule?.FileName;
				}
				if (string.IsNullOrEmpty(appPath)) return;
				if (!appPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
				{
					appPath = Path.ChangeExtension(appPath, ".exe");
				}
				key.SetValue(AppName, $"\"{appPath}\"");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Error updating startup registry: {ex.Message}");
				Console.WriteLine($"Warning: Could not update startup registry: {ex.Message}");
			}
		}
	}
}
