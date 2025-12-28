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
        private DateTime? _lastActivityTime;
        private bool _waitingForBackupDelay;

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

                bool isActive = _activityMonitor.IsUserActive();

                if (isActive)
                {
                    _lastActivityTime = now;

                    bool inScheduledWindow = IsInScheduledWindow(now);

                    if (inScheduledWindow)
                    {
                        TryTakeMainScreenshot();
                    }
                    else if (!_hasBackupScreenshot && !_waitingForBackupDelay)
                    {
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
                _configManager.Config.LastScreenshotDate?.Date != now.Date)
            {
                _scheduledTime = DetermineScheduledTime();
                _configManager.Config.TodayScheduledTime = _scheduledTime;
                _configManager.SaveConfig();
            }
            else
            {
                _scheduledTime = _configManager.Config.TodayScheduledTime.Value;
            }

            _dayCompleted = false;
            _hasBackupScreenshot = false;
            _backupScreenshotPath = null;
            _waitingForBackupDelay = false;
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
            int startHour = 9;
            int endHour = 21;

            // Default 9am-9pm range
            TimeSpan defaultStart = new TimeSpan(startHour, 0, 0);
            TimeSpan defaultEnd = new TimeSpan(endHour, 0, 0);

            // If current time is after 9am, use current time + 1 minute as minimum
            TimeSpan effectiveStart = defaultStart;
            if (currentTime > defaultStart)
            {
                effectiveStart = currentTime.Add(TimeSpan.FromMinutes(1));
            }

            // If we're past 9pm, we'll schedule for tomorrow (return default start)
            if (effectiveStart >= defaultEnd)
            {
                return defaultStart;
            }

            // Calculate random time between effective start and end
            int startMinutes = (int)effectiveStart.TotalMinutes;
            int endMinutes = (int)defaultEnd.TotalMinutes;
            int randomMinutes = random.Next(startMinutes, endMinutes);

            return TimeSpan.FromMinutes(randomMinutes);
        }

        private bool IsInScheduledWindow(DateTime now)
        {
            TimeSpan currentTime = now.TimeOfDay;
            TimeSpan windowStart = _scheduledTime.Subtract(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));
            TimeSpan windowEnd = _scheduledTime.Add(TimeSpan.FromMinutes(ScheduledWindowMinutes / 2.0));

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
                if (_hasBackupScreenshot && !string.IsNullOrEmpty(_backupScreenshotPath))
                {
                    _storageManager.DeleteBackupScreenshot(_backupScreenshotPath);
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
    }
}

