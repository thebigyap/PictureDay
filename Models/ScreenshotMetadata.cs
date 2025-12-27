using System;

namespace PictureDay.Models
{
    public class ScreenshotMetadata
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime DateTaken { get; set; }
        public string FileName { get; set; } = string.Empty;
    }
}

