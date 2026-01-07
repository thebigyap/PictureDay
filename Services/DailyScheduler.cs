using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureDay.Models;
using Timer = System.Threading.Timer;

namespace PictureDay.Services
{
	public class DailyScheduler
	{
		private readonly ConfigManager _configManager;
		private readonly ActivityMonitor _activityMonitor;
		private readonly PrivacyFilter _privacyFilter;
		private readonly ScreenshotService _screenshotService;
		private readonly StorageManager _storageManager;

		private Timer? _monitoringTimer;
		private DateTime _lastMidnightCheck;
		private bool _dayCompleted;
		private bool _hasBackupScreenshot;
		private string? _backupScreenshotPath;
		private TimeSpan _scheduledTime;
		private TimeSpan? _backupScheduledTime;
		private DateTime? _lastActivityTime;
		private bool _waitingForBackupDelay;
		private bool _waitingForBackupScheduledTime;

		private const int MonitoringIntervalSeconds = 45;
		private const int BackupDelaySeconds = 60;
		private const int ScheduledWindowMinutes = 5;

		public DailyScheduler(
			ConfigManager configManager,
			ActivityMonitor activityMonitor,
			PrivacyFilter privacyFilter,
			ScreenshotService screenshotService,
			StorageManager storageManager)
		{
			_configManager = configManager;
			_activityMonitor = activityMonitor;
			_privacyFilter = privacyFilter;
			_screenshotService = screenshotService;
			_storageManager = storageManager;

			_lastMidnightCheck = DateTime.Now.Date;
			InitializeDay();
		}

		public void Start()
		{
			_monitoringTimer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(MonitoringIntervalSeconds));
		}

		public void Stop()
		{
			_monitoringTimer?.Dispose();
			_monitoringTimer = null;
		}

		private void OnTimerTick(object? state)
		{
			try
			{
				DateTime now = DateTime.Now;
				if (now.Date > _lastMidnightCheck)
				{
					ResetDay();
					_lastMidnightCheck = now.Date;
				}

				if (_dayCompleted)
				{
					return;
				}

				// Check if scheduled time has passed and we have a backup - promote it to main
				if (_hasBackupScreenshot && !string.IsNullOrEmpty(_backupScreenshotPath) && HasScheduledTimePassed(now))
				{
					PromoteBackupToMain();
				}

				bool isActive = _activityMonitor.IsUserActive();

				if (isActive)
				{
					_lastActivityTime = now;

					bool inScheduledWindow = IsInScheduledWindow(now);
					bool inBackupScheduledWindow = _backupScheduledTime.HasValue && IsInBackupScheduledWindow(now);

					if (inScheduledWindow)
					{
						TryTakeMainScreenshot();
					}
					else if (inBackupScheduledWindow && !_hasBackupScreenshot && !_waitingForBackupDelay)
					{
						// Take backup screenshot during scheduled backup time
						TryTakeBackupScreenshot();
					}
					else if (!_hasBackupScreenshot && !_waitingForBackupDelay && !_waitingForBackupScheduledTime)
					{
						// Fallback: take backup if missed scheduled time (original behavior)
						_waitingForBackupDelay = true;
						Task.Delay(TimeSpan.FromSeconds(BackupDelaySeconds))
							.ContinueWith(_ => TryTakeBackupScreenshot());
					}
				}
				else
				{
					if (_waitingForBackupDelay)
					{
						_waitingForBackupDelay = false;
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in scheduler tick: {ex.Message}");
			}
		}

		private void InitializeDay()
		{
			DateTime now = DateTime.Now;
			DateTime lastScreenshotDate = _configManager.Config.LastScreenshotDate ?? DateTime.MinValue;

			if (lastScreenshotDate.Date == now.Date)
			{
				_dayCompleted = true;
				return;
			}

		CheckForOrphanedBackups(now);

		if (_configManager.Config.TodayScheduledTime == null ||
			_configManager.Config.ScheduledTimeDate?.Date != now.Date)
		{
			_scheduledTime = DetermineScheduledTime();
			_configManager.Config.TodayScheduledTime = _scheduledTime;
			_configManager.Config.ScheduledTimeDate = now.Date;

				// If scheduled time is after 9 PM (21:00), schedule a backup between 9 AM - 9 PM
				TimeSpan ninePM = new TimeSpan(21, 0, 0);
				if (_scheduledTime > ninePM || _scheduledTime < new TimeSpan(9, 0, 0))
				{
					// Schedule backup between 9 AM - 9 PM
					Random random = new Random();
					int backupStartMinutes = 9 * 60; // 9 AM
					int backupEndMinutes = 21 * 60; // 9 PM
					int randomBackupMinutes = random.Next(backupStartMinutes, backupEndMinutes);
					_backupScheduledTime = TimeSpan.FromMinutes(randomBackupMinutes);
				}
				else
				{
					_backupScheduledTime = null;
				}

				_configManager.SaveConfig();
			}
			else
			{
				_scheduledTime = _configManager.Config.TodayScheduledTime.Value;
				// Recalculate backup time if needed
				TimeSpan ninePM = new TimeSpan(21, 0, 0);
				if (_scheduledTime > ninePM || _scheduledTime < new TimeSpan(9, 0, 0))
				{
					if (!_hasBackupScreenshot)
					{
						Random random = new Random();
						int backupStartMinutes = 9 * 60;
						int backupEndMinutes = 21 * 60;
						int randomBackupMinutes = random.Next(backupStartMinutes, backupEndMinutes);
						_backupScheduledTime = TimeSpan.FromMinutes(randomBackupMinutes);
					}
				}
				else
				{
					_backupScheduledTime = null;
				}
			}

			_dayCompleted = false;
			_hasBackupScreenshot = false;
			_backupScreenshotPath = null;
			_waitingForBackupDelay = false;
			_waitingForBackupScheduledTime = false;
		}

		private TimeSpan DetermineScheduledTime()
		{
			DateTime now = DateTime.Now;
			TimeSpan currentTime = now.TimeOfDay;

			switch (_configManager.Config.ScheduleMode)
			{
				case ScheduleMode.FixedTime:
					if (_configManager.Config.FixedScheduledTime.HasValue)
					{
						TimeSpan fixedTime = _configManager.Config.FixedScheduledTime.Value;
						// If fixed time is in the past, it will be scheduled for tomorrow
						return fixedTime;
					}
					break;

				case ScheduleMode.TimeRange:
					if (_configManager.Config.ScheduleRangeStart.HasValue &&
						_configManager.Config.ScheduleRangeEnd.HasValue)
					{
						return PickRandomTimeInRange(
							_configManager.Config.ScheduleRangeStart.Value,
							_configManager.Config.ScheduleRangeEnd.Value,
							currentTime);
					}
					break;

				case ScheduleMode.Random:
				default:
					return PickRandomTime(currentTime);
			}

			return PickRandomTime(currentTime);
		}

		private TimeSpan PickRandomTimeInRange(TimeSpan start, TimeSpan end, TimeSpan currentTime)
		{
			Random random = new Random();

			// Ensure we don't schedule in the past
			TimeSpan effectiveStart = start;
			if (currentTime > start)
			{
				// Current time is after the start of the range, use current time + 1 minute as minimum
				effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
			}

			// If effective start is after end, we're past the window for today
			// In this case, we'll still return a time, but it won't trigger until the next day
			if (effectiveStart >= end)
			{
				// Return the start time (will be scheduled for tomorrow)
				return start;
			}

			int startMinutes = (int)effectiveStart.TotalMinutes;
			int endMinutes = (int)end.TotalMinutes;
			int randomMinutes = random.Next(startMinutes, endMinutes);
			return TimeSpan.FromMinutes(randomMinutes);
		}

		private void CheckForOrphanedBackups(DateTime currentDate)
		{
			try
			{
				DateTime yesterday = currentDate.AddDays(-1);
				string monthDir = Path.Combine(
					_configManager.Config.ScreenshotDirectory,
					yesterday.ToString("yyyy-MM"));

				if (Directory.Exists(monthDir))
				{
					var backupFiles = Directory.GetFiles(monthDir, "backup_*.jpg")
						.Where(f =>
						{
							string fileName = Path.GetFileName(f);
							if (fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
							{
								try
								{
									string datePart = fileName.Substring(7, 10);
									if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime fileDate))
									{
										return fileDate.Date == yesterday.Date;
									}
								}
								catch
								{
								}
							}
							return false;
						})
						.ToList();

					if (backupFiles.Any())
					{
						DateTime yesterdayScreenshotDate = _configManager.Config.LastScreenshotDate ?? DateTime.MinValue;
						if (yesterdayScreenshotDate.Date != yesterday.Date)
						{
							string mostRecentBackup = backupFiles
								.OrderByDescending(f => new FileInfo(f).CreationTime)
								.First();

							string newName = Path.GetFileName(mostRecentBackup).Substring(7);
							string newPath = Path.Combine(monthDir, newName);

							if (!File.Exists(newPath))
							{
								File.Move(mostRecentBackup, newPath);
								Console.WriteLine($"Promoted backup screenshot to main: {newName}");

								_configManager.Config.LastScreenshotDate = yesterday;
								_configManager.SaveConfig();
							}

							foreach (string backupFile in backupFiles)
							{
								if (File.Exists(backupFile))
								{
									File.Delete(backupFile);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error checking for orphaned backups: {ex.Message}");
			}
		}

		private void ResetDay()
		{
			_dayCompleted = false;
			_hasBackupScreenshot = false;
			_backupScreenshotPath = null;
			_waitingForBackupDelay = false;
			_lastActivityTime = null;
			InitializeDay();
		}

		private TimeSpan PickRandomTime(TimeSpan currentTime)
		{
			Random random = new Random();
			int startHour = 9; // 9 AM
			int endHour = 3; // 3 AM next day (represented as 3 hours, which is < 9 hours)

			// Default 9am-3am (next day) range
			// Times after midnight but before 9 AM are represented as hours 0-9
			// Times from 9 AM to midnight are represented as hours 9-24
			TimeSpan defaultStart = new TimeSpan(startHour, 0, 0); // 9 AM
			TimeSpan defaultEnd = new TimeSpan(endHour, 0, 0); // 3 AM (next day, but represented as 3 hours)

			// Determine effective start time
			TimeSpan effectiveStart = defaultStart;

			// If current time is between 9 AM and midnight, use current time + 1 minute
			if (currentTime >= defaultStart && currentTime < TimeSpan.FromHours(24))
			{
				effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
				if (effectiveStart >= TimeSpan.FromDays(1))
				{
					effectiveStart = effectiveStart.Subtract(TimeSpan.FromDays(1));
				}
			}
			// If current time is between midnight and 3 AM, we can still schedule for today (3 AM)
			else if (currentTime < defaultEnd)
			{
				effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
			}
			// If current time is between 3 AM and 9 AM, schedule for 9 AM today
			else if (currentTime >= defaultEnd && currentTime < defaultStart)
			{
				return defaultStart;
			}

			// Calculate random time
			// We have two ranges: 9 AM - midnight (9-24 hours) and midnight - 3 AM (0-3 hours)
			int totalRangeMinutes = (24 - startHour) * 60 + endHour * 60; // 15 hours * 60 = 900 minutes

			int currentMinutes = (int)effectiveStart.TotalMinutes;
			int effectiveStartMinutes;

			if (currentMinutes >= startHour * 60) // After 9 AM
			{
				effectiveStartMinutes = currentMinutes - (startHour * 60);
			}
			else // Before 9 AM (midnight to 9 AM)
			{
				effectiveStartMinutes = (24 - startHour) * 60 + currentMinutes; // Hours from 9 AM to current time
			}

			int randomOffset = random.Next(0, totalRangeMinutes - effectiveStartMinutes);
			int randomMinutes = effectiveStartMinutes + randomOffset;

			// Convert back to TimeSpan
			if (randomMinutes < (24 - startHour) * 60)
			{
				// Time is between 9 AM and midnight
				return TimeSpan.FromMinutes(startHour * 60 + randomMinutes);
			}
			else
			{
				// Time is between midnight and 3 AM
				int minutesAfterMidnight = randomMinutes - (24 - startHour) * 60;
				return TimeSpan.FromMinutes(minutesAfterMidnight);
			}
		}

		private bool IsInScheduledWindow(DateTime now)
		{
			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan windowStart = _scheduledTime.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
			TimeSpan windowEnd = _scheduledTime.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

			// Handle wrap-around for times after midnight (e.g., 2:30 AM)
			if (_scheduledTime.TotalHours >= 24 || _scheduledTime.TotalHours < 9)
			{
				// Scheduled time is after midnight, check if we're in the window
				TimeSpan normalizedScheduled = _scheduledTime;
				if (normalizedScheduled.TotalHours >= 24)
				{
					normalizedScheduled = normalizedScheduled.Subtract(TimeSpan.FromDays(1));
				}

				windowStart = normalizedScheduled.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
				windowEnd = normalizedScheduled.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

				// If window crosses midnight
				if (windowStart.TotalHours < 0)
				{
					windowStart = windowStart.Add(TimeSpan.FromDays(1));
					return currentTime >= windowStart || currentTime <= windowEnd;
				}
				if (windowEnd.TotalHours >= 24)
				{
					windowEnd = windowEnd.Subtract(TimeSpan.FromDays(1));
					return currentTime >= windowStart || currentTime <= windowEnd;
				}
			}

			if (windowEnd > TimeSpan.FromDays(1))
			{
				return currentTime >= windowStart || currentTime <= windowEnd.Subtract(TimeSpan.FromDays(1));
			}

			return currentTime >= windowStart && currentTime <= windowEnd;
		}

		private bool IsInBackupScheduledWindow(DateTime now)
		{
			if (!_backupScheduledTime.HasValue)
			{
				return false;
			}

			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan windowStart = _backupScheduledTime.Value.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
			TimeSpan windowEnd = _backupScheduledTime.Value.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

			if (windowEnd > TimeSpan.FromDays(1))
			{
				return currentTime >= windowStart || currentTime <= windowEnd.Subtract(TimeSpan.FromDays(1));
			}

			return currentTime >= windowStart && currentTime <= windowEnd;
		}

		private void TryTakeMainScreenshot()
		{
			if (_dayCompleted)
			{
				return;
			}

			// Check privacy filter
			if (_privacyFilter.ShouldBlockScreenshot())
			{
				return;
			}

			string? screenshotPath = _screenshotService.CaptureScreen(isBackup: false);
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				// If we took the main screenshot and have a backup, delete the backup
				if (_hasBackupScreenshot && !string.IsNullOrEmpty(_backupScreenshotPath))
				{
					_storageManager.DeleteBackupScreenshot(_backupScreenshotPath);
					_hasBackupScreenshot = false;
					_backupScreenshotPath = null;
				}

				_dayCompleted = true;
				_configManager.Config.LastScreenshotDate = DateTime.Now;
				_configManager.SaveConfig();
			}
		}

		private void TryTakeBackupScreenshot()
		{
			if (_dayCompleted || _hasBackupScreenshot)
			{
				_waitingForBackupDelay = false;
				return;
			}

			if (!_activityMonitor.IsUserActive())
			{
				_waitingForBackupDelay = false;
				return;
			}

			// Check privacy filter
			if (_privacyFilter.ShouldBlockScreenshot())
			{
				_waitingForBackupDelay = false;
				return;
			}

			string? screenshotPath = _screenshotService.CaptureScreen(isBackup: true);
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				_hasBackupScreenshot = true;
				_backupScreenshotPath = screenshotPath;
			}

			_waitingForBackupDelay = false;
		}

		private bool HasScheduledTimePassed(DateTime now)
		{
			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan windowEnd = _scheduledTime.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

			// Handle wrap-around for times after midnight
			if (_scheduledTime.TotalHours >= 24 || _scheduledTime.TotalHours < 9)
			{
				TimeSpan normalizedScheduled = _scheduledTime;
				if (normalizedScheduled.TotalHours >= 24)
				{
					normalizedScheduled = normalizedScheduled.Subtract(TimeSpan.FromDays(1));
				}
				windowEnd = normalizedScheduled.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

				// If scheduled time is after midnight (e.g., 2:30 AM), check if we're past 3 AM
				if (normalizedScheduled.TotalHours < 9)
				{
					// Scheduled time is between midnight and 9 AM
					// Check if current time is past the window and past 3 AM
					if (currentTime.TotalHours >= 3 && currentTime.TotalHours < 9)
					{
						return currentTime > windowEnd;
					}
					// If we're past 9 AM, the scheduled time has definitely passed
					if (currentTime.TotalHours >= 9)
					{
						return true;
					}
				}
			}
			else
			{
				// Normal case: scheduled time is between 9 AM and 9 PM
				if (windowEnd > TimeSpan.FromDays(1))
				{
					windowEnd = windowEnd.Subtract(TimeSpan.FromDays(1));
					return currentTime > windowEnd && currentTime < _scheduledTime;
				}
				return currentTime > windowEnd;
			}

			return false;
		}

		private void PromoteBackupToMain()
		{
			if (!_hasBackupScreenshot || string.IsNullOrEmpty(_backupScreenshotPath))
			{
				return;
			}

			try
			{
				// Move backup screenshot to main screenshot location
				string? newPath = _storageManager.PromoteBackupToMain(_backupScreenshotPath);
				if (!string.IsNullOrEmpty(newPath))
				{
					_dayCompleted = true;
					_hasBackupScreenshot = false;
					_backupScreenshotPath = null;
					_configManager.Config.LastScreenshotDate = DateTime.Now;
					_configManager.SaveConfig();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error promoting backup to main: {ex.Message}");
			}
		}
	}
}

