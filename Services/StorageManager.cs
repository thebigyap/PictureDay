using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using PictureDay.Models;

namespace PictureDay.Services
{
	public class StorageManager
	{
		private readonly object _sync = new object();
		private static readonly Lazy<ImageCodecInfo?> JpegCodec = new Lazy<ImageCodecInfo?>(() =>
			ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid));
		private string _baseDirectory;
		private int _quality;
		private string _imageFormat;

		public StorageManager(string baseDirectory, int quality = 90, string imageFormat = "JPEG")
		{
			_baseDirectory = PrepareDirectory(baseDirectory);
			_quality = Math.Clamp(quality, 1, 100);
			_imageFormat = NormalizeFormat(imageFormat);
		}

		public void UpdateSettings(int quality, string imageFormat, string? baseDirectory = null)
		{
			// Validate the destination before changing any active settings.
			string? directory = baseDirectory == null ? null : PrepareDirectory(baseDirectory);
			lock (_sync)
			{
				_quality = Math.Clamp(quality, 1, 100);
				_imageFormat = NormalizeFormat(imageFormat);
				if (directory != null)
					_baseDirectory = directory;
			}
		}

		private static string PrepareDirectory(string path)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(path);
			string fullPath = Path.GetFullPath(path);
			Directory.CreateDirectory(fullPath);
			return fullPath;
		}

		private static string NormalizeFormat(string format) =>
			string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) ? "PNG" : "JPEG";

		private static string MonthlyPath(string baseDirectory, DateTime date) =>
			Path.Combine(baseDirectory, date.ToString("yyyy-MM", CultureInfo.InvariantCulture));

		public string GetMonthlyDirectory(DateTime date)
		{
			string fullPath = MonthlyPath(GetBaseDirectory(), date);
			Directory.CreateDirectory(fullPath);
			return fullPath;
		}

		public string GenerateFileName(DateTime dateTime, bool isBackup = false, bool isQuarter = false,
			bool isUser = false, string format = "JPEG")
		{
			string prefix = isUser ? "u_" : isQuarter ? "quarter_" : isBackup ? "backup_" : "";
			string extension = NormalizeFormat(format) == "PNG" ? "png" : "jpg";
			return $"{prefix}{dateTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.{extension}";
		}

		public string SaveScreenshot(Bitmap bitmap, bool isBackup = false, bool isQuarter = false, bool isUser = false)
		{
			ArgumentNullException.ThrowIfNull(bitmap);
			lock (_sync)
			{
				DateTime now = DateTime.Now;
				string monthlyDir = GetMonthlyDirectory(now);
				string requestedPath = Path.Combine(monthlyDir, GenerateFileName(now, isBackup, isQuarter, isUser, _imageFormat));
				string temporaryPath = Path.Combine(monthlyDir, $".{Guid.NewGuid():N}.tmp");
				try
				{
					// Publish only complete images, and never overwrite a capture made in the same second.
					using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
						if (_imageFormat == "PNG")
							bitmap.Save(stream, ImageFormat.Png);
						else if (JpegCodec.Value is ImageCodecInfo jpegCodec)
						{
							using var encoderParams = new EncoderParameters(1);
							encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
							bitmap.Save(stream, jpegCodec, encoderParams);
						}
						else
							bitmap.Save(stream, ImageFormat.Jpeg);
						stream.Flush(true);
					}
					return MoveToAvailablePath(temporaryPath, requestedPath);
				}
				finally
				{
					TryDelete(temporaryPath);
				}
			}
		}

		private static string MoveToAvailablePath(string sourcePath, string requestedPath)
		{
			string directory = Path.GetDirectoryName(requestedPath)!;
			string stem = Path.GetFileNameWithoutExtension(requestedPath);
			string extension = Path.GetExtension(requestedPath);
			string destination = requestedPath;
			for (int suffix = 1; ; suffix++)
			{
				try
				{
					File.Move(sourcePath, destination);
					return destination;
				}
				catch (IOException) when (File.Exists(destination))
				{
					destination = Path.Combine(directory, $"{stem}_{suffix:D3}{extension}");
				}
			}
		}

		public void DeleteBackupScreenshot(string backupPath)
		{
			lock (_sync)
				TryDelete(backupPath);
		}

		public string? PromoteBackupToMain(string backupPath)
		{
			return Path.GetFileName(backupPath).StartsWith("backup_", StringComparison.OrdinalIgnoreCase)
				? PromotePhotoToMain(backupPath) : null;
		}

		public string? PromotePhotoToMain(string photoPath)
		{
			lock (_sync)
			{
				try
				{
					if (!File.Exists(photoPath))
						return null;
					string fileName = Path.GetFileName(photoPath);
					int prefixLength = fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase) ? 8
						: fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) ? 7 : 0;
					if (prefixLength == 0)
						return photoPath;
					string requestedPath = Path.Combine(Path.GetDirectoryName(photoPath)!, fileName.Substring(prefixLength));
					return MoveToAvailablePath(photoPath, requestedPath);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Debug.WriteLine($"Error promoting photo to main: {ex.Message}");
					return null;
				}
			}
		}

		public class PhotoInfo
		{
			public string FilePath { get; set; } = string.Empty;
			public string FileName { get; set; } = string.Empty;
			public bool IsMain { get; set; }
			public bool IsQuarter { get; set; }
			public bool IsBackup { get; set; }
		}

		private static PhotoInfo ToPhotoInfo(FileInfo file) => new PhotoInfo
		{
			FilePath = file.FullName,
			FileName = file.Name,
			IsMain = !file.Name.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase) &&
				!file.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase),
			IsQuarter = file.Name.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase),
			IsBackup = file.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)
		};

		public List<PhotoInfo> GetPhotosForDate(DateTime date)
		{
			// Reading a date must not create empty month directories.
			return ReadImageFiles(MonthlyPath(GetBaseDirectory(), date))
				.Where(file => !file.Name.StartsWith("u_", StringComparison.OrdinalIgnoreCase) &&
					TryGetCaptureTime(file.Name, out DateTime capturedAt) && capturedAt.Date == date.Date)
				.Select(ToPhotoInfo).ToList();
		}

		public void ProcessDailyPhotoSelection(DateTime date)
		{
			lock (_sync)
				ProcessDailyPhotoSelection(GetPhotosForDate(date));
		}

		private void ProcessDailyPhotoSelection(List<PhotoInfo> photos)
		{
			var available = photos.Where(photo => File.Exists(photo.FilePath)).ToList();
			if (available.Count == 0)
				return;

			var mainPhotos = available.Where(photo => photo.IsMain).OrderBy(photo => photo.FileName, StringComparer.Ordinal).ToList();
			PhotoInfo selected;
			string? keptPath;
			if (mainPhotos.Count > 0)
			{
				selected = mainPhotos[0];
				keptPath = selected.FilePath;
			}
			else
			{
				var candidates = available.Where(photo => photo.IsQuarter).ToList();
				if (candidates.Count == 0)
					candidates = available.Where(photo => photo.IsBackup).ToList();
				if (candidates.Count == 0)
					return;
				selected = candidates[Random.Shared.Next(candidates.Count)];
				keptPath = PromotePhotoToMain(selected.FilePath);
			}

			// A failed promotion must leave every fallback intact for a later attempt.
			if (keptPath == null || !File.Exists(keptPath))
				return;
			foreach (var photo in available.Where(photo => !string.Equals(photo.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase)))
				TryDelete(photo.FilePath);
		}

		public void CleanupOrphanedPhotos()
		{
			lock (_sync)
			{
				DateTime today = DateTime.Today;
				foreach (string directory in ReadMonthDirectories(_baseDirectory))
				{
					// Preserve today's pending candidates, and recover days missed while the app was closed.
					var pastPhotos = ReadImageFiles(directory)
						.Where(file => !file.Name.StartsWith("u_", StringComparison.OrdinalIgnoreCase))
						.Select(file => new { File = file, Date = TryGetCaptureTime(file.Name, out DateTime date) ? date.Date : (DateTime?)null })
						.Where(photo => photo.Date.HasValue && photo.Date.Value < today)
						.GroupBy(photo => photo.Date!.Value);
					foreach (var day in pastPhotos)
						ProcessDailyPhotoSelection(day.Select(photo => ToPhotoInfo(photo.File)).ToList());
				}
			}
		}

		private static void TryDelete(string path)
		{
			try { File.Delete(path); }
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Debug.WriteLine($"Error deleting photo {path}: {ex.Message}");
			}
		}

		private static bool IsImageFile(string path)
		{
			string extension = Path.GetExtension(path);
			return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
		}

		private static FileInfo[] ReadImageFiles(string directory, bool throwOnError = false)
		{
			try
			{
				return new DirectoryInfo(directory).EnumerateFiles().Where(file => IsImageFile(file.Name)).ToArray();
			}
			catch (Exception ex) when (!throwOnError && (ex is IOException || ex is UnauthorizedAccessException))
			{
				Debug.WriteLine($"Could not read screenshot directory {directory}: {ex.Message}");
				return Array.Empty<FileInfo>();
			}
		}

		private static string[] ReadMonthDirectories(string baseDirectory, bool throwOnError = false)
		{
			try
			{
				return Directory.EnumerateDirectories(baseDirectory)
					.Where(path => DateTime.TryParseExact(Path.GetFileName(path), "yyyy-MM", CultureInfo.InvariantCulture,
						DateTimeStyles.None, out _)).ToArray();
			}
			catch (Exception ex) when (!throwOnError && (ex is IOException || ex is UnauthorizedAccessException))
			{
				Debug.WriteLine($"Could not read screenshot library {baseDirectory}: {ex.Message}");
				return Array.Empty<string>();
			}
		}

		private static bool TryGetCaptureTime(string fileName, out DateTime capturedAt)
		{
			string stem = Path.GetFileNameWithoutExtension(fileName);
			int prefixLength = stem.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase) ? 8
				: stem.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) ? 7
				: stem.StartsWith("u_", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
			string timestamp = stem.Substring(prefixLength);
			capturedAt = default;
			if (timestamp.Length < 19 || (timestamp.Length > 19 &&
				(timestamp[19] != '_' || timestamp.Length == 20 || !timestamp.AsSpan(20).ToString().All(char.IsDigit))))
				return false;
			return DateTime.TryParseExact(timestamp.AsSpan(0, 19), "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out capturedAt);
		}

		private static (List<ScreenshotMetadata> screenshots, long totalBytes) ReadScreenshots(IEnumerable<string> directories, bool throwOnError = false)
		{
			var screenshots = new List<ScreenshotMetadata>();
			long totalBytes = 0;
			foreach (string directory in directories)
			{
				foreach (FileInfo file in ReadImageFiles(directory, throwOnError))
				{
					if (file.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
						continue;
					try
					{
						if (!file.Exists)
							continue;
						long length = file.Length;
						DateTime capturedAt = TryGetCaptureTime(file.Name, out DateTime date) ? date : file.CreationTime;
						screenshots.Add(new ScreenshotMetadata { FilePath = file.FullName, FileName = file.Name, DateTaken = capturedAt });
						totalBytes += length;
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
						Debug.WriteLine($"Could not read screenshot {file.FullName}: {ex.Message}");
					}
				}
			}
			screenshots.Sort((left, right) =>
			{
				int byDate = right.DateTaken.CompareTo(left.DateTaken);
				return byDate != 0 ? byDate : StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath);
			});
			return (screenshots, totalBytes);
		}

		/// <summary>Reads the library once, sharing one consistent set of photos and statistics with the gallery.</summary>
		public StorageSnapshot GetSnapshot()
		{
			var (screenshots, totalBytes) = ReadScreenshots(ReadMonthDirectories(GetBaseDirectory(), throwOnError: true), throwOnError: true);
			return new StorageSnapshot(screenshots.AsReadOnly(), totalBytes, DateTime.Today);
		}

		public List<ScreenshotMetadata> GetScreenshotsByDateRange(DateTime startDate, DateTime endDate)
		{
			if (endDate.Date < startDate.Date)
				return new List<ScreenshotMetadata>();
			string baseDirectory = GetBaseDirectory();
			DateTime current = new DateTime(startDate.Year, startDate.Month, 1);
			DateTime end = new DateTime(endDate.Year, endDate.Month, 1);
			var directories = new List<string>();
			while (true)
			{
				directories.Add(MonthlyPath(baseDirectory, current));
				if (current == end)
					break;
				current = current.AddMonths(1);
			}
			return ReadScreenshots(directories).screenshots
				.Where(photo => photo.DateTaken.Date >= startDate.Date && photo.DateTaken.Date <= endDate.Date).ToList();
		}

		public List<ScreenshotMetadata> GetScreenshotsByMonth(int year, int month)
		{
			DateTime start = new DateTime(year, month, 1);
			return GetScreenshotsByDateRange(start, new DateTime(year, month, DateTime.DaysInMonth(year, month)));
		}

		public List<ScreenshotMetadata> GetAllScreenshots() => GetSnapshot().Screenshots.ToList();
		public long GetTotalStorageUsed() => GetSnapshot().TotalStorageBytes;
		public int GetLongestStreak() => GetSnapshot().LongestStreak;
		public int GetCurrentStreak() => GetSnapshot().CurrentStreak;

		public string GetBaseDirectory()
		{
			lock (_sync)
				return _baseDirectory;
		}

		public (int minYear, int minMonth, int maxYear, int maxMonth) GetDateRange()
		{
			var screenshots = GetSnapshot().Screenshots;
			DateTime min = screenshots.Count == 0 ? DateTime.Today : screenshots[screenshots.Count - 1].DateTaken;
			DateTime max = screenshots.Count == 0 ? DateTime.Today : screenshots[0].DateTaken;
			return (min.Year, min.Month, max.Year, max.Month);
		}

		public (int minMonth, int maxMonth) GetMonthRangeForYear(int year)
		{
			var months = GetSnapshot().Screenshots.Where(photo => photo.DateTaken.Year == year).Select(photo => photo.DateTaken.Month).ToArray();
			return months.Length == 0 ? (1, year == DateTime.Today.Year ? DateTime.Today.Month : 12) : (months.Min(), months.Max());
		}
	}
}
