using System;
using System.Collections.Generic;

namespace PictureDay.Models
{
    public class AppConfig
    {
        public List<string> BlockedApplications { get; set; } = new List<string>();
        public string ScreenshotDirectory { get; set; } = string.Empty;
        public DateTime? LastScreenshotDate { get; set; }
        public int Quality { get; set; } = 90;
        public bool StartWithWindows { get; set; } = true;
        public TimeSpan? TodayScheduledTime { get; set; }
    }
}

