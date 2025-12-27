using System;
using System.Collections.Generic;

namespace PictureDay.Models
{
    public enum ScheduleMode
    {
        Random,
        FixedTime,
        TimeRange
    }

    public class AppConfig
    {
        public List<string> BlockedApplications { get; set; } = new List<string>();
        public string ScreenshotDirectory { get; set; } = string.Empty;
        public DateTime? LastScreenshotDate { get; set; }
        public int Quality { get; set; } = 90;
        public string ImageFormat { get; set; } = "JPEG";
        public bool StartWithWindows { get; set; } = true;
        public TimeSpan? TodayScheduledTime { get; set; }
        public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Random;
        public TimeSpan? FixedScheduledTime { get; set; }
        public TimeSpan? ScheduleRangeStart { get; set; }
        public TimeSpan? ScheduleRangeEnd { get; set; }
        public bool CaptureAllMonitors { get; set; } = false;
        public int SelectedMonitorIndex { get; set; } = 0;
    }
}

