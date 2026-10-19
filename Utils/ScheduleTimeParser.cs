using System;
using System.Globalization;

namespace PictureDay.Utils;

public static class ScheduleTimeParser
{
    public static bool TryParse(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text?.Trim(), new[] { @"h\:mm", @"hh\:mm" },
            CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
}
