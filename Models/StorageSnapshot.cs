using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureDay.Models
{
	/// <summary>Library metadata and statistics calculated from a single filesystem scan.</summary>
	public sealed class StorageSnapshot
	{
		public IReadOnlyList<ScreenshotMetadata> Screenshots { get; }
		public long TotalStorageBytes { get; }
		public int CurrentStreak { get; }
		public int LongestStreak { get; }

		internal StorageSnapshot(IReadOnlyList<ScreenshotMetadata> screenshots, long totalStorageBytes, DateTime today)
		{
			Screenshots = screenshots;
			TotalStorageBytes = totalStorageBytes;
			// Future-dated imports must not inflate either streak or hide a current run.
			var days = screenshots.Select(photo => photo.DateTaken.Date).Where(date => date <= today.Date)
				.Distinct().OrderByDescending(date => date).ToArray();
			int run = 0;
			int longest = 0;
			for (int i = 0; i < days.Length; i++)
			{
				run = i > 0 && (days[i - 1] - days[i]).Days == 1 ? run + 1 : 1;
				longest = Math.Max(longest, run);
			}
			LongestStreak = longest;

			if (days.Length == 0 || (today.Date - days[0]).Days > 1)
				return;
			int current = 1;
			while (current < days.Length && (days[current - 1] - days[current]).Days == 1)
				current++;
			CurrentStreak = current;
		}
	}
}
