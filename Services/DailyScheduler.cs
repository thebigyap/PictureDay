using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
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
		private TimeSpan _scheduledTime;
		private DateTime? _lastActivityTime;
		private List<TimeSpan> _quarterCheckpoints = new List<TimeSpan>();
		private DateTime _currentDayDate;
		private Random _random = new Random();
		private DateTime _lastTimerTick = DateTime.Now;

		public event EventHandler? PhotosProcessed;
		public event EventHandler? ScheduledTimeChanged;

		private const int MonitoringIntervalSeconds = 60;
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
			_currentDayDate = DateTime.Now.Date;
			InitializeDay();
		}

		public void Start()
		{
			_lastTimerTick = DateTime.Now;
			_monitoringTimer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(MonitoringIntervalSeconds));
			SystemEvents.PowerModeChanged += OnPowerModeChanged;
		}

		public void Stop()
		{
			SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			_monitoringTimer?.Dispose();
			_monitoringTimer = null;
		}

		private void OnTimerTick(object? state)
		{
			try
			{
				DateTime now = DateTime.Now;
				TimeSpan currentTime = now.TimeOfDay;

				TimeSpan timeSinceLastTick = now - _lastTimerTick;
				if (timeSinceLastTick.TotalMinutes > MonitoringIntervalSeconds * 2 / 60.0)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Large time gap detected ({timeSinceLastTick.TotalMinutes:F1} minutes) - possible sleep/resume. Checking if scheduled time was missed...");
					CheckAndRescheduleIfMissed(now);
				}
				_lastTimerTick = now;

				if (now.Date > _lastMidnightCheck)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] *** MIDNIGHT TRANSITION DETECTED ***");
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Processing photos for previous day: {_currentDayDate:yyyy-MM-dd}");
					_storageManager.ProcessDailyPhotoSelection(_currentDayDate);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Resetting day for new date: {now.Date:yyyy-MM-dd}");
					ResetDay();
					_lastMidnightCheck = now.Date;
					_currentDayDate = now.Date;
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Day reset complete. New scheduled time: {_scheduledTime:hh\\:mm\\:ss}");

					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Notifying UI to refresh gallery...");
					PhotosProcessed?.Invoke(this, EventArgs.Empty);
				}

				if (_dayCompleted)
				{
					return;
				}

				bool isActive = _activityMonitor.IsUserActive();

				if (isActive)
				{
					_lastActivityTime = now;

					bool inScheduledWindow = IsInScheduledWindow(now);
					if (inScheduledWindow)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] In scheduled window ({_scheduledTime:hh\\:mm\\:ss}), attempting main screenshot...");
						TryTakeMainScreenshot();
					}

					if (!_dayCompleted && _quarterCheckpoints.Any())
					{
						foreach (var checkpoint in _quarterCheckpoints)
						{
							if (IsInCheckpointWindow(now, checkpoint))
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] In checkpoint window ({checkpoint:hh\\:mm\\:ss}), attempting quarter screenshot...");
								TryTakeQuarterScreenshot();
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ERROR] Error in scheduler tick: {ex.Message}");
				Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
				System.Diagnostics.Debug.WriteLine($"Error in scheduler tick: {ex.Message}");
			}
		}

		private void InitializeDay()
		{
			DateTime now = DateTime.Now;
			_currentDayDate = now.Date;
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Initializing day for {now.Date:yyyy-MM-dd}");

			DateTime yesterday = now.Date.AddDays(-1);
			var yesterdayPhotos = _storageManager.GetPhotosForDate(yesterday);
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Checking yesterday ({yesterday:yyyy-MM-dd}): Found {yesterdayPhotos.Count} photos, {yesterdayPhotos.Count(p => p.IsMain)} main");
			if (yesterdayPhotos.Any() && !yesterdayPhotos.Any(p => p.IsMain))
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Processing yesterday's photos (no main photo found)");
				_storageManager.ProcessDailyPhotoSelection(yesterday);
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Notifying UI to refresh gallery after processing yesterday...");
				PhotosProcessed?.Invoke(this, EventArgs.Empty);
			}

			var photos = _storageManager.GetPhotosForDate(now.Date);
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Checking today ({now.Date:yyyy-MM-dd}): Found {photos.Count} photos, {photos.Count(p => p.IsMain)} main");
			if (photos.Any(p => p.IsMain))
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Day already completed (main photo exists)");
				_dayCompleted = true;
				return;
			}

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Cleaning up orphaned photos...");
			_storageManager.CleanupOrphanedPhotos();

		if (_configManager.Config.TodayScheduledTime == null ||
			_configManager.Config.ScheduledTimeDate?.Date != now.Date)
		{
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Determining new scheduled time (mode: {_configManager.Config.ScheduleMode})");
			_scheduledTime = DetermineScheduledTime();
			_configManager.Config.TodayScheduledTime = _scheduledTime;
			_configManager.Config.ScheduledTimeDate = now.Date;
			_configManager.SaveConfig();
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Scheduled time set to: {_scheduledTime:hh\\:mm\\:ss}");

			if (_configManager.Config.ScheduleMode == ScheduleMode.TimeRange &&
				_configManager.Config.ScheduleRangeStart.HasValue &&
				_configManager.Config.ScheduleRangeEnd.HasValue)
			{
				_quarterCheckpoints = CalculateQuarterCheckpoints(
					_configManager.Config.ScheduleRangeStart.Value,
					_configManager.Config.ScheduleRangeEnd.Value);
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Calculated {_quarterCheckpoints.Count} quarter checkpoints");
				foreach (var cp in _quarterCheckpoints)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   - Checkpoint: {cp:hh\\:mm\\:ss}");
				}
			}

			ScheduledTimeChanged?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			_scheduledTime = _configManager.Config.TodayScheduledTime.Value;
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Using existing scheduled time: {_scheduledTime:hh\\:mm\\:ss}");

			bool scheduledTimePassed = HasScheduledTimePassed(now);
			bool noMainPhoto = !photos.Any(p => p.IsMain);

			if (scheduledTimePassed && noMainPhoto)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Scheduled time ({_scheduledTime:hh\\:mm\\:ss}) has passed and no photo taken. Recalculating for rest of day...");
				_scheduledTime = DetermineScheduledTime();
				_configManager.Config.TodayScheduledTime = _scheduledTime;
				_configManager.Config.ScheduledTimeDate = now.Date;
				_configManager.SaveConfig();
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] New scheduled time set to: {_scheduledTime:hh\\:mm\\:ss}");

				if (_configManager.Config.ScheduleMode == ScheduleMode.TimeRange &&
					_configManager.Config.ScheduleRangeStart.HasValue &&
					_configManager.Config.ScheduleRangeEnd.HasValue)
				{
					_quarterCheckpoints = CalculateQuarterCheckpoints(
						_configManager.Config.ScheduleRangeStart.Value,
						_configManager.Config.ScheduleRangeEnd.Value);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Calculated {_quarterCheckpoints.Count} quarter checkpoints");
					foreach (var cp in _quarterCheckpoints)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   - Checkpoint: {cp:hh\\:mm\\:ss}");
					}
				}

				ScheduledTimeChanged?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				if (_configManager.Config.ScheduleMode == ScheduleMode.TimeRange &&
					_configManager.Config.ScheduleRangeStart.HasValue &&
					_configManager.Config.ScheduleRangeEnd.HasValue)
				{
					_quarterCheckpoints = CalculateQuarterCheckpoints(
						_configManager.Config.ScheduleRangeStart.Value,
						_configManager.Config.ScheduleRangeEnd.Value);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Calculated {_quarterCheckpoints.Count} quarter checkpoints");
					foreach (var cp in _quarterCheckpoints)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   - Checkpoint: {cp:hh\\:mm\\:ss}");
					}
				}
			}
		}

		_dayCompleted = false;
		}

		private TimeSpan DetermineScheduledTime()
		{
			DateTime now = DateTime.Now;
			TimeSpan currentTime = now.TimeOfDay;

			_quarterCheckpoints.Clear();

			switch (_configManager.Config.ScheduleMode)
			{
				case ScheduleMode.FixedTime:
					if (_configManager.Config.FixedScheduledTime.HasValue)
					{
						TimeSpan fixedTime = _configManager.Config.FixedScheduledTime.Value;
						if (fixedTime.TotalHours >= 24)
						{
							fixedTime = fixedTime.Subtract(TimeSpan.FromDays(1));
						}
						if (fixedTime < TimeSpan.Zero)
						{
							fixedTime = fixedTime.Add(TimeSpan.FromDays(1));
						}
						if (fixedTime >= currentTime && fixedTime < TimeSpan.FromDays(1))
						{
							return fixedTime;
						}
						return fixedTime;
					}
					break;

				case ScheduleMode.TimeRange:
					if (_configManager.Config.ScheduleRangeStart.HasValue &&
						_configManager.Config.ScheduleRangeEnd.HasValue)
					{
						TimeSpan start = _configManager.Config.ScheduleRangeStart.Value;
						TimeSpan end = _configManager.Config.ScheduleRangeEnd.Value;
						TimeSpan effectiveEnd = end >= start ? end : new TimeSpan(23, 59, 59);

						if (effectiveEnd < currentTime)
						{
							return start;
						}

						_quarterCheckpoints = CalculateQuarterCheckpoints(start, end);
						return PickRandomTimeInRange(start, end, currentTime);
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
			TimeSpan effectiveEnd = end >= start ? end : new TimeSpan(23, 59, 59);

			TimeSpan effectiveStart = start;
			if (currentTime > start)
			{
				effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
			}

			if (effectiveStart >= effectiveEnd)
			{
				return start;
			}

			int startMinutes = (int)effectiveStart.TotalMinutes;
			int endMinutes = (int)effectiveEnd.TotalMinutes;

			if (startMinutes >= endMinutes)
			{
				return start;
			}

			int randomMinutes = _random.Next(startMinutes, endMinutes);
			return TimeSpan.FromMinutes(randomMinutes);
		}

		private List<TimeSpan> CalculateQuarterCheckpoints(TimeSpan start, TimeSpan end)
		{
			List<TimeSpan> checkpoints = new List<TimeSpan>();

			TimeSpan effectiveEnd = end >= start ? end : new TimeSpan(23, 59, 59);
			TimeSpan range = effectiveEnd - start;
			TimeSpan quarter = TimeSpan.FromMinutes(range.TotalMinutes / 4.0);

			checkpoints.Add(start);
			checkpoints.Add(start.Add(quarter));
			checkpoints.Add(start.Add(TimeSpan.FromMinutes(quarter.TotalMinutes * 2)));
			checkpoints.Add(start.Add(TimeSpan.FromMinutes(quarter.TotalMinutes * 3)));

			return checkpoints;
		}


		private void ResetDay()
		{
			_dayCompleted = false;
			_lastActivityTime = null;
			_quarterCheckpoints.Clear();
			InitializeDay();
		}

		private TimeSpan PickRandomTime(TimeSpan currentTime)
		{
			TimeSpan dayStart = TimeSpan.Zero; // 0:00
			TimeSpan dayEnd = new TimeSpan(23, 59, 59); // 23:59:59

			TimeSpan effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
			if (effectiveStart > dayEnd)
			{
				return dayStart;
			}

			int startMinutes = (int)effectiveStart.TotalMinutes;
			int endMinutes = (int)dayEnd.TotalMinutes;
			int randomMinutes = _random.Next(startMinutes, endMinutes + 1);
			return TimeSpan.FromMinutes(randomMinutes);
		}

		private bool IsInScheduledWindow(DateTime now)
		{
			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan normalizedScheduled = _scheduledTime;

			if (normalizedScheduled.TotalHours >= 24)
			{
				normalizedScheduled = normalizedScheduled.Subtract(TimeSpan.FromDays(1));
			}
			if (normalizedScheduled.TotalHours < 0)
			{
				normalizedScheduled = normalizedScheduled.Add(TimeSpan.FromDays(1));
			}

			TimeSpan windowStart = normalizedScheduled.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
			TimeSpan windowEnd = normalizedScheduled.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

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

			return currentTime >= windowStart && currentTime <= windowEnd;
		}

		private bool HasScheduledTimePassed(DateTime now)
		{
			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan normalizedScheduled = _scheduledTime;

			if (normalizedScheduled.TotalHours >= 24)
			{
				normalizedScheduled = normalizedScheduled.Subtract(TimeSpan.FromDays(1));
			}
			if (normalizedScheduled.TotalHours < 0)
			{
				normalizedScheduled = normalizedScheduled.Add(TimeSpan.FromDays(1));
			}

			TimeSpan windowEnd = normalizedScheduled.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

			if (windowEnd.TotalHours >= 24)
			{
				windowEnd = windowEnd.Subtract(TimeSpan.FromDays(1));
				return currentTime > windowEnd;
			}

			return currentTime > windowEnd;
		}


		private void TryTakeMainScreenshot()
		{
			DateTime now = DateTime.Now;
			if (_dayCompleted)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping main screenshot - day already completed");
				return;
			}

			if (_privacyFilter.ShouldBlockScreenshot())
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping main screenshot - privacy filter blocking");
				return;
			}

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Attempting to capture MAIN screenshot...");
			string? screenshotPath = _screenshotService.CaptureScreen(isBackup: false, isQuarter: false);
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] MAIN screenshot saved successfully: {screenshotPath}");

				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Processing photos immediately to set MAIN as official photo...");
				_storageManager.ProcessDailyPhotoSelection(_currentDayDate);

				_dayCompleted = true;
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Day marked as completed after successful processing");

				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Notifying UI to refresh gallery after MAIN photo processing...");
				PhotosProcessed?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] MAIN screenshot capture failed or returned null");
			}
		}

		private void TryTakeQuarterScreenshot()
		{
			DateTime now = DateTime.Now;
			if (_dayCompleted)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping quarter screenshot - day already completed");
				return;
			}

			if (!_activityMonitor.IsUserActive())
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping quarter screenshot - user not active");
				return;
			}

			if (_privacyFilter.ShouldBlockScreenshot())
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping quarter screenshot - privacy filter blocking");
				return;
			}

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Attempting to capture QUARTER screenshot...");
			string? screenshotPath = _screenshotService.CaptureScreen(isBackup: false, isQuarter: true);
			if (!string.IsNullOrEmpty(screenshotPath))
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] QUARTER screenshot saved successfully: {screenshotPath}");
			}
			else
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] QUARTER screenshot capture failed or returned null");
			}
		}

		private bool IsInCheckpointWindow(DateTime now, TimeSpan checkpointTime)
		{
			TimeSpan currentTime = now.TimeOfDay;
			TimeSpan windowStart = checkpointTime.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
			TimeSpan windowEnd = checkpointTime.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

			if (windowEnd > TimeSpan.FromDays(1))
			{
				windowEnd = windowEnd.Subtract(TimeSpan.FromDays(1));
				return currentTime >= windowStart || currentTime <= windowEnd;
			}

			if (windowStart < TimeSpan.Zero)
			{
				windowStart = windowStart.Add(TimeSpan.FromDays(1));
				windowEnd = windowEnd.Add(TimeSpan.FromDays(1));
				return currentTime >= windowStart || currentTime <= windowEnd.Subtract(TimeSpan.FromDays(1));
			}

			return currentTime >= windowStart && currentTime <= windowEnd;
		}

		private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
		{
			DateTime now = DateTime.Now;
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Power mode changed: {e.Mode}");

			if (e.Mode == PowerModes.Resume)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] System resumed from sleep/hibernate. Checking if scheduled time was missed...");
				_lastTimerTick = now;
				CheckAndRescheduleIfMissed(now);
			}
		}

		private void CheckAndRescheduleIfMissed(DateTime now)
		{
			if (_dayCompleted)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping reschedule check - day already completed");
				return;
			}

			var photos = _storageManager.GetPhotosForDate(now.Date);
			bool hasMainPhoto = photos.Any(p => p.IsMain);

			if (hasMainPhoto)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Skipping reschedule check - main photo already exists");
				return;
			}

			bool scheduledTimePassed = HasScheduledTimePassed(now);

			if (scheduledTimePassed)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Scheduled time ({_scheduledTime:hh\\:mm\\:ss}) has passed and no photo taken. Recalculating for rest of day...");
				_scheduledTime = DetermineScheduledTime();
				_configManager.Config.TodayScheduledTime = _scheduledTime;
				_configManager.Config.ScheduledTimeDate = now.Date;
				_configManager.SaveConfig();
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] New scheduled time set to: {_scheduledTime:hh\\:mm\\:ss}");

				if (_configManager.Config.ScheduleMode == ScheduleMode.TimeRange &&
					_configManager.Config.ScheduleRangeStart.HasValue &&
					_configManager.Config.ScheduleRangeEnd.HasValue)
				{
					_quarterCheckpoints = CalculateQuarterCheckpoints(
						_configManager.Config.ScheduleRangeStart.Value,
						_configManager.Config.ScheduleRangeEnd.Value);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Calculated {_quarterCheckpoints.Count} quarter checkpoints");
					foreach (var cp in _quarterCheckpoints)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   - Checkpoint: {cp:hh\\:mm\\:ss}");
					}
				}

				ScheduledTimeChanged?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Scheduled time ({_scheduledTime:hh\\:mm\\:ss}) has not yet passed. No reschedule needed.");
			}
		}

	}
}

