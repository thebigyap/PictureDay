using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using PictureDay.Services;

namespace PictureDay.Tests
{
	public static class StorageChecks
	{
		public static void Run(string root)
		{
			VerifyMetadata(Path.Combine(root, "metadata"));
			VerifyStreaks(Path.Combine(root, "streaks"));
			VerifyRecovery(Path.Combine(root, "recovery"));
			VerifyCapturesAndSettings(Path.Combine(root, "captures"));
			VerifyShrinking(Path.Combine(root, "shrink"));
			Console.WriteLine("Storage checks passed: dates, ranges, ordering, streaks, recovery, collisions, image encoding, destination changes, and old-photo shrinking.");
		}

		private static void VerifyShrinking(string root)
		{
			var storage = new StorageManager(root, 100, "PNG");
			DateTime old = DateTime.Today.AddMonths(-4).AddHours(12);
			DateTime recent = DateTime.Today.AddMonths(-1).AddHours(12);
			string oldPng = SaveNoisyPhoto(storage, root, old, ".png");
			string recentPng = SaveNoisyPhoto(storage, root, recent, ".png");
			storage.UpdateSettings(100, "JPEG");
			string oldJpeg = SaveNoisyPhoto(storage, root, old.AddMinutes(1), ".jpg");
			long oldJpegLength = new FileInfo(oldJpeg).Length;
			DateTime oldPngCreated = File.GetCreationTime(oldPng);
			byte[] recentBytes = File.ReadAllBytes(recentPng);

			storage.UpdateSettings(80, "JPEG");
			int shrunk = storage.ShrinkPhotosOlderThan(DateTime.Today.AddMonths(-3));
			string convertedPng = Path.ChangeExtension(oldPng, ".jpg");
			Assert(shrunk == 2, $"Both old photos should shrink, but {shrunk} did.");
			Assert(!File.Exists(oldPng) && File.Exists(convertedPng), "An old PNG must be replaced by a JPEG with the same capture name.");
			Assert(File.GetCreationTime(convertedPng) == oldPngCreated, "Converting a PNG must keep its creation time.");
			Assert(new FileInfo(oldJpeg).Length < oldJpegLength * 9 / 10, "An old quality-100 JPEG must be re-saved smaller.");
			Assert(File.GetLastWriteTime(oldJpeg) > old.AddDays(1), "A re-saved JPEG must be stamped so later passes skip it.");
			Assert(File.ReadAllBytes(recentPng).SequenceEqual(recentBytes), "Photos newer than the cutoff must not change.");
			Assert(storage.GetSnapshot().Screenshots.Select(photo => photo.DateTaken).OrderBy(date => date)
				.SequenceEqual(new[] { old, old.AddMinutes(1), recent }), "Shrinking must keep every photo and its capture time.");
			using (var image = Image.FromFile(convertedPng))
				Assert(image.Width == 320 && image.Height == 200, "A shrunk photo must keep its dimensions.");
			Assert(storage.ShrinkPhotosOlderThan(DateTime.Today.AddMonths(-3)) == 0, "Already shrunk photos must not be processed again.");
			Assert(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Shrinking must leave no temporary files.");
		}

		/// <summary>Saves a capture-named photo with real image detail, dated like an original capture.</summary>
		private static string SaveNoisyPhoto(StorageManager storage, string root, DateTime capturedAt, string extension)
		{
			var random = new Random(capturedAt.GetHashCode());
			using var bitmap = new Bitmap(320, 200);
			for (int y = 0; y < bitmap.Height; y++)
				for (int x = 0; x < bitmap.Width; x++)
					bitmap.SetPixel(x, y, Color.FromArgb((x * 3 + random.Next(40)) % 256, (y * 5 + random.Next(40)) % 256, random.Next(256)));
			string directory = Path.Combine(root, capturedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, capturedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + extension);
			File.Move(storage.SaveScreenshot(bitmap), path);
			File.SetLastWriteTime(path, capturedAt);
			return path;
		}

		private static void VerifyMetadata(string root)
		{
			var storage = new StorageManager(root);
			DateTime missing = new DateTime(1999, 1, 1);
			Assert(storage.GetPhotosForDate(missing).Count == 0, "Missing day should be empty.");
			Assert(!Directory.Exists(Path.Combine(root, "1999-01")), "Reading a missing day must not create a month folder.");
			Assert(storage.GetSnapshot().Screenshots.Count == 0, "New library should be empty.");

			string first = WritePhoto(root, new DateTime(2024, 5, 9, 23, 59, 59));
			string next = WritePhoto(root, new DateTime(2024, 5, 10));
			string newest = WritePhoto(root, new DateTime(2024, 6, 1), "u_", ".PNG");
			WritePhoto(root, new DateTime(2024, 5, 9, 18, 0, 0), "backup_");
			File.SetCreationTime(first, new DateTime(2025, 1, 1));
			File.SetCreationTime(next, new DateTime(2023, 1, 1));
			var snapshot = storage.GetSnapshot();
			Assert(snapshot.Screenshots.Count == 3, "Gallery must include main/user images and exclude backups.");
			Assert(snapshot.Screenshots[0].FilePath == newest, "Library must sort newest first across months.");
			Assert(snapshot.Screenshots.Single(photo => photo.FilePath == first).DateTaken == new DateTime(2024, 5, 9, 23, 59, 59),
				"Capture dates must survive changed filesystem creation dates.");
			Assert(snapshot.TotalStorageBytes == 9, "Storage totals must use the same visible files as the snapshot.");
			var singleDay = storage.GetScreenshotsByDateRange(new DateTime(2024, 5, 9), new DateTime(2024, 5, 9));
			Assert(singleDay.Count == 1 && singleDay[0].FilePath == first, "Date range must include the full final day and exclude next midnight.");
			Assert(storage.GetScreenshotsByDateRange(new DateTime(2024, 6, 1), new DateTime(2024, 5, 1)).Count == 0,
				"Reversed date range must be empty.");
			Assert(storage.GetScreenshotsByMonth(2024, 5).Count == 2, "Monthly lookup must use capture dates.");

			CultureInfo previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
				Assert(storage.GenerateFileName(new DateTime(2024, 5, 9, 12, 34, 56)) == "2024-05-09_12-34-56.jpg",
					"Capture filenames must use the Gregorian invariant calendar.");
				Assert(storage.GetScreenshotsByMonth(2024, 5).Count == 2, "Date parsing must be independent of current culture.");
			}
			finally { CultureInfo.CurrentCulture = previous; }
		}

		private static void VerifyStreaks(string root)
		{
			DateTime today = DateTime.Today;
			var storage = new StorageManager(root);
			Assert(storage.GetCurrentStreak() == 0 && storage.GetLongestStreak() == 0, "Empty library has no streak.");
			for (int i = 0; i < 3; i++)
				WritePhoto(root, today.AddDays(-i));
			WritePhoto(root, today.AddHours(5), "u_");
			WritePhoto(root, today.AddDays(1));
			WritePhoto(root, today.AddDays(2));
			for (int i = 10; i < 16; i++)
				WritePhoto(root, today.AddDays(-i));
			var snapshot = storage.GetSnapshot();
			Assert(snapshot.CurrentStreak == 3, "Future photos and same-day duplicates must not change current streak.");
			Assert(snapshot.LongestStreak == 6, "Longest streak must find a historical run despite gaps.");

			var yesterdayStorage = new StorageManager(Path.Combine(root, "yesterday"));
			WritePhoto(yesterdayStorage.GetBaseDirectory(), today.AddDays(-1));
			WritePhoto(yesterdayStorage.GetBaseDirectory(), today.AddDays(-2));
			Assert(yesterdayStorage.GetCurrentStreak() == 2, "Yesterday's run remains active before today's capture.");
			var staleStorage = new StorageManager(Path.Combine(root, "stale"));
			WritePhoto(staleStorage.GetBaseDirectory(), today.AddDays(-2));
			Assert(staleStorage.GetCurrentStreak() == 0 && staleStorage.GetLongestStreak() == 1, "Missing yesterday breaks the current streak.");
		}

		private static void VerifyRecovery(string root)
		{
			DateTime today = DateTime.Today;
			var storage = new StorageManager(root);
			string todayQuarter = WritePhoto(root, today.AddHours(8), "quarter_");
			string todayBackup = WritePhoto(root, today.AddHours(9), "backup_");
			string todayUser = WritePhoto(root, today.AddHours(10), "u_");
			string future = WritePhoto(root, today.AddDays(1), "quarter_");
			DateTime pendingDay = today.AddDays(-3);
			string pastQuarter = WritePhoto(root, pendingDay.AddHours(8), "quarter_");
			WritePhoto(root, pendingDay.AddHours(9), "backup_");
			string pastUser = WritePhoto(root, pendingDay.AddHours(10), "u_");
			DateTime backupDay = today.AddDays(-5);
			WritePhoto(root, backupDay.AddHours(8), "backup_");
			DateTime completedDay = today.AddDays(-7);
			string earliestMain = WritePhoto(root, completedDay.AddHours(8));
			WritePhoto(root, completedDay.AddHours(9));
			WritePhoto(root, completedDay.AddHours(10), "quarter_");
			storage.CleanupOrphanedPhotos();

			Assert(File.Exists(todayQuarter) && File.Exists(todayBackup) && File.Exists(todayUser) && File.Exists(future),
				"Startup cleanup must retain current/future pending photos and user captures.");
			var recovered = storage.GetPhotosForDate(pendingDay);
			Assert(recovered.Count == 1 && recovered[0].IsMain && recovered[0].FileName.Contains("08-00-00"),
				"Past pending days must promote the quarter candidate before a backup.");
			Assert(!File.Exists(pastQuarter) && File.Exists(pastUser), "Promotion moves the candidate while preserving user photos.");
			Assert(storage.GetPhotosForDate(backupDay).Single().IsMain, "Backup-only days must be recovered.");
			Assert(storage.GetPhotosForDate(completedDay).Single().FilePath == earliestMain, "Completed days must retain their earliest main photo.");

			DateTime collisionDate = today.AddDays(-9).AddHours(12);
			string existingMain = WritePhoto(root, collisionDate);
			string backup = WritePhoto(root, collisionDate, "backup_");
			File.WriteAllBytes(backup, new byte[] { 9, 8, 7, 6 });
			string? promoted = storage.PromoteBackupToMain(backup);
			Assert(promoted != null && promoted != existingMain && File.Exists(promoted), "Promotion collision must choose a new filename.");
			Assert(File.ReadAllBytes(existingMain).SequenceEqual(new byte[] { 1, 2, 3 }) &&
				File.ReadAllBytes(promoted!).SequenceEqual(new byte[] { 9, 8, 7, 6 }), "Promotion collision must preserve both original images.");

			DateTime failedDay = today.AddDays(-11);
			string lockedQuarter = WritePhoto(root, failedDay.AddHours(8), "quarter_");
			string retainedBackup = WritePhoto(root, failedDay.AddHours(9), "backup_");
			using (var locked = new FileStream(lockedQuarter, FileMode.Open, FileAccess.Read, FileShare.None))
				storage.ProcessDailyPhotoSelection(failedDay);
			Assert(File.Exists(lockedQuarter) && File.Exists(retainedBackup), "Failed promotion must preserve all alternatives.");
		}

		private static void VerifyCapturesAndSettings(string root)
		{
			var storage = new StorageManager(root, 90, "PNG");
			DateTime now = DateTime.Now;
			var protectedFiles = new Dictionary<string, byte[]>();
			// Reserve nearby timestamps so the first save deterministically exercises a collision.
			for (int i = -2; i < 60; i++)
			{
				DateTime at = now.AddSeconds(i);
				string path = Path.Combine(storage.GetMonthlyDirectory(at), storage.GenerateFileName(at, isUser: true, format: "PNG"));
				byte[] content = new byte[] { 4, 5, 6 };
				File.WriteAllBytes(path, content);
				protectedFiles[path] = content;
			}
			using var bitmap = new Bitmap(12, 8);
			using (var graphics = Graphics.FromImage(bitmap))
				graphics.Clear(Color.CornflowerBlue);
			string first = storage.SaveScreenshot(bitmap, isUser: true);
			string second = storage.SaveScreenshot(bitmap, isUser: true);
			Assert(first != second && File.Exists(first) && File.Exists(second), "Rapid captures must have unique paths.");
			Assert(Path.GetFileNameWithoutExtension(first).EndsWith("_001", StringComparison.Ordinal), "Capture must safely handle an existing same-second filename.");
			Assert(protectedFiles.All(entry => File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)), "Capture must not overwrite any existing image.");
			using (var image = Image.FromFile(first))
				Assert(image.Width == 12 && image.Height == 8, "Saved PNG must decode at the original dimensions.");
			Assert(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Successful saves must leave no temporary files.");

			string nextRoot = Path.Combine(root, "new-location");
			storage.UpdateSettings(75, "JPEG", nextRoot);
			string jpeg = storage.SaveScreenshot(bitmap, isUser: true);
			Assert(storage.GetBaseDirectory() == Path.GetFullPath(nextRoot) && jpeg.StartsWith(Path.GetFullPath(nextRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
				"Changing the screenshot directory must affect active captures immediately.");
			Assert(Path.GetExtension(jpeg) == ".jpg" && storage.GetSnapshot().Screenshots.Count == 1, "New settings must update encoding and the active gallery library.");
			using (var image = Image.FromFile(jpeg))
				Assert(image.Width == 12 && image.Height == 8, "Saved JPEG must decode correctly.");
			storage.UpdateSettings(80, "PNG");
			Assert(storage.GetBaseDirectory() == Path.GetFullPath(nextRoot), "Two-argument settings update must retain the current directory.");

			string invalidDirectory = Path.Combine(root, "not-a-directory");
			File.WriteAllText(invalidDirectory, "fixture");
			bool failed = false;
			try { storage.UpdateSettings(90, "JPEG", invalidDirectory); }
			catch (IOException) { failed = true; }
			Assert(failed && storage.GetBaseDirectory() == Path.GetFullPath(nextRoot), "Invalid destination must leave active settings intact.");
		}

		private static string WritePhoto(string root, DateTime capturedAt, string prefix = "", string extension = ".jpg")
		{
			string directory = Path.Combine(root, capturedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(directory);
			string fileName = prefix + capturedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + extension;
			string path = Path.Combine(directory, fileName);
			File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
			return path;
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}
	}
}
