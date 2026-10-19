using System.IO;
using Newtonsoft.Json;
using PictureDay.Models;
using PictureDay.Services;

namespace PictureDay.Tests;

internal static class ConfigChecks
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        CheckNormalization(Path.Combine(root, "normalization.json"));
        CheckScheduleTransactions(Path.Combine(root, "schedule.json"));
        Console.WriteLine("Additional config checks passed: normalization, stale draft merge, schedule rollback, and temporary-file cleanup.");
    }

    private static void CheckNormalization(string path)
    {
        File.WriteAllText(path, """
            {
              "BlockedApplications": [null, "", "  Banking.EXE  ", "banking.exe"],
              "ScreenshotDirectory": "  ",
              "Quality": 400,
              "ImageFormat": null,
              "Theme": "unknown",
              "SelectedMonitorIndex": -20,
              "ScheduleMode": 100,
              "TodayScheduledTime": "1.01:00:00",
              "ScheduledTimeDate": "2026-09-05T00:00:00",
              "FixedScheduledTime": "-01:00:00",
              "ScheduleRangeStart": "00:00:00",
              "ScheduleRangeEnd": "23:59:59"
            }
            """);
        var manager = new ConfigManager(path, false);
        AppConfig normalized = manager.Config;
        Assert(normalized.BlockedApplications.SequenceEqual(new[] { "Banking.EXE" }), "Normalization must retain and deduplicate valid blocked applications.");
        Assert(!string.IsNullOrWhiteSpace(normalized.ScreenshotDirectory), "A blank photo directory must get a usable default.");
        Assert(normalized.Quality == 100 && normalized.ImageFormat == "JPEG" && normalized.Theme == "Light", "Invalid quality, format, and theme must normalize.");
        Assert(normalized.SelectedMonitorIndex == -1 && normalized.ScheduleMode == ScheduleMode.Random, "Invalid monitor and schedule values must normalize.");
        Assert(normalized.TodayScheduledTime == null && normalized.ScheduledTimeDate == null && normalized.FixedScheduledTime == null, "Invalid times must not keep stale scheduling dates.");
        Assert(normalized.ScheduleRangeStart == TimeSpan.Zero && normalized.ScheduleRangeEnd == new TimeSpan(23, 59, 59), "Valid day-boundary times must be retained.");

        manager.SaveConfig(new AppConfig { BlockedApplications = null!, ImageFormat = "png", Theme = "dark", Quality = -10 });
        Assert(manager.Config.BlockedApplications.Count == 0 && manager.Config.ImageFormat == "PNG" && manager.Config.Theme == "Dark" && manager.Config.Quality == 1, "Null lists and case-insensitive choices must normalize on save too.");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".config-*.tmp").Any(), "Successful config saves must leave no temporary files.");
    }

    private static void CheckScheduleTransactions(string path)
    {
        var manager = new ConfigManager(path, false);
        DateTime today = new DateTime(2026, 9, 5);
        manager.SaveConfig(new AppConfig { TodayScheduledTime = TimeSpan.FromHours(10), ScheduledTimeDate = today });
        var staleDraft = JsonConvert.DeserializeObject<AppConfig>(JsonConvert.SerializeObject(manager.Config))!;
        manager.SetScheduledTime(TimeSpan.FromHours(14), today.AddDays(1));
        staleDraft.Theme = "Dark";
        manager.SaveConfig(staleDraft, preserveSchedule: true);
        Assert(manager.Config.Theme == "Dark" && manager.Config.TodayScheduledTime == TimeSpan.FromHours(14)
            && manager.Config.ScheduledTimeDate == today.AddDays(1), "An unchanged schedule in a settings draft must preserve newer scheduler state.");

        string savedJson = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try { manager.SetScheduledTime(TimeSpan.FromHours(16), today.AddDays(2)); }
            catch (IOException) { failed = true; }
            Assert(failed, "A locked schedule file must report save failure.");
            Assert(manager.Config.TodayScheduledTime == TimeSpan.FromHours(14) && manager.Config.ScheduledTimeDate == today.AddDays(1), "Failed schedule saves must retain both previous fields.");
        }
        Assert(File.ReadAllText(path) == savedJson, "Failed schedule saves must leave the committed JSON unchanged.");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".config-*.tmp").Any(), "Failed config saves must clean up their temporary files.");

        staleDraft.TodayScheduledTime = null;
        staleDraft.ScheduledTimeDate = null;
        manager.SaveConfig(staleDraft);
        Assert(manager.Config.TodayScheduledTime == null && manager.Config.ScheduledTimeDate == null, "An explicit schedule change must be allowed to reset the generated time.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
