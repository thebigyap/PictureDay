using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using PictureDay.Models;
using PictureDay.Services;

namespace PictureDay.Tests
{
	public static class SchedulerChecks
	{
		public static void Run(string root)
		{
			VerifyMidnightRefresh(Path.Combine(root, "midnight"));
			VerifyResume(Path.Combine(root, "resume"));
			Console.WriteLine("Scheduler checks passed: midnight refresh ordering, pending-photo preservation, short resume, long gaps, and completed-day protection.");
		}

		private static void VerifyMidnightRefresh(string root)
		{
			var (scheduler, storage, config) = CreateScheduler(root);
			DateTime today = DateTime.Today;
			string yesterday = WritePhoto(storage, today.AddDays(-1).AddHours(8), "quarter_");
			string todayQuarter = WritePhoto(storage, today.AddHours(8), "quarter_");
			string todayBackup = WritePhoto(storage, today.AddHours(9), "backup_");
			string todayUser = WritePhoto(storage, today.AddHours(10), "u_");
			SetField(scheduler, "_lastMidnightCheck", today.AddDays(-1));
			SetField(scheduler, "_currentDayDate", today.AddDays(-1));
			SetField(scheduler, "_lastTimerTick", today.AddDays(-1).AddHours(23));
			int processed = 0;
			scheduler.PhotosProcessed += (_, _) => processed++;

			// Only exercise transition logic; never start timers or enter screenshot capture paths.
			Invoke(scheduler, "HandleDayTransitions", today.AddHours(12), true, true);
			Assert(!File.Exists(yesterday) && storage.GetPhotosForDate(today.AddDays(-1)).Single().IsMain,
				"Midnight must finalize yesterday before applying a simultaneous settings refresh.");
			Assert(File.Exists(todayQuarter) && File.Exists(todayBackup) && File.Exists(todayUser),
				"A refresh at midnight must not prematurely finalize today's pending captures.");
			Assert(!scheduler.IsDayCompleted, "Yesterday's recovered photo must not mark today complete.");
			Assert((DateTime)GetField(scheduler, "_lastMidnightCheck") == today &&
				(DateTime)GetField(scheduler, "_currentDayDate") == today, "Rollover date markers must advance together.");
			Assert(processed == 1, "Rollover should issue one gallery refresh after finalization.");
			Assert(config.Config.ScheduledTimeDate?.Date == today, "Refreshed schedule must be persisted for the new day.");
			scheduler.Stop();
		}

		private static void VerifyResume(string root)
		{
			var (scheduler, _, config) = CreateScheduler(root);
			DateTime noon = DateTime.Today.AddHours(12);
			TimeSpan missedTime = new TimeSpan(11, 56, 0);
			SetField(scheduler, "_scheduledTime", missedTime);
			SetField(scheduler, "_lastTimerTick", noon.AddSeconds(-30));
			int changed = 0;
			scheduler.ScheduledTimeChanged += (_, _) => changed++;

			Invoke(scheduler, "HandleDayTransitions", noon, false, false);
			Assert(changed == 0 && scheduler.ScheduledTime == missedTime, "An ordinary short tick must preserve its current schedule.");
			SetField(scheduler, "_lastTimerTick", noon.AddSeconds(-30));
			Invoke(scheduler, "HandleDayTransitions", noon, false, true);
			Assert(changed == 1 && scheduler.ScheduledTime == TimeSpan.FromHours(18),
				"A resume shorter than two minutes must still reschedule a missed window.");
			Assert(config.Config.TodayScheduledTime == TimeSpan.FromHours(18) && config.Config.ScheduledTimeDate == noon.Date,
				"Rescheduling must persist time and date together.");

			SetField(scheduler, "_scheduledTime", missedTime);
			SetField(scheduler, "_lastTimerTick", noon.AddMinutes(-3));
			Invoke(scheduler, "HandleDayTransitions", noon, false, false);
			Assert(changed == 2 && scheduler.ScheduledTime == TimeSpan.FromHours(18), "A long timer gap must still recover a missed window.");

			SetField(scheduler, "_dayCompleted", true);
			SetField(scheduler, "_scheduledTime", missedTime);
			SetField(scheduler, "_lastTimerTick", noon.AddSeconds(-30));
			Invoke(scheduler, "HandleDayTransitions", noon, false, true);
			Assert(changed == 2 && scheduler.ScheduledTime == missedTime, "Resume must not reschedule a completed day.");

			// A stopped scheduler rejects the queued callback, retaining the explicit resume signal.
			Invoke(scheduler, "OnPowerModeChanged", scheduler, new PowerModeChangedEventArgs(PowerModes.Resume));
			Assert((int)GetField(scheduler, "_resumeRequested") == 1, "Power resume must retain a signal independent of elapsed tick time.");
			scheduler.Stop();
		}

		private static (DailyScheduler scheduler, StorageManager storage, ConfigManager config) CreateScheduler(string root)
		{
			string library = Path.Combine(root, "photos");
			var storage = new StorageManager(library);
			var config = new ConfigManager(Path.Combine(root, "config.json"), updateStartupRegistry: false);
			config.SaveConfig(new AppConfig
			{
				ScreenshotDirectory = library,
				StartWithWindows = false,
				ScheduleMode = ScheduleMode.FixedTime,
				FixedScheduledTime = TimeSpan.FromHours(18),
				TodayScheduledTime = TimeSpan.FromHours(18),
				ScheduledTimeDate = DateTime.Today
			});
			var privacy = new PrivacyFilter(config);
			var scheduler = new DailyScheduler(config, new ActivityMonitor(), privacy, new ScreenshotService(storage, config), storage);
			return (scheduler, storage, config);
		}

		private static string WritePhoto(StorageManager storage, DateTime date, string prefix)
		{
			string path = Path.Combine(storage.GetMonthlyDirectory(date), prefix + date.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".jpg");
			File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
			return path;
		}

		private static void SetField(DailyScheduler scheduler, string name, object value) =>
			typeof(DailyScheduler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scheduler, value);

		private static object GetField(DailyScheduler scheduler, string name) =>
			typeof(DailyScheduler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler)!;

		private static void Invoke(DailyScheduler scheduler, string name, params object[] arguments) =>
			typeof(DailyScheduler).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scheduler, arguments);

		private static void Assert(bool condition, string message)
		{
			if (!condition) throw new InvalidOperationException(message);
		}
	}
}
