using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using PictureDay.Models;

namespace PictureDay.Services
{
	public class StorageManager
	{
		private readonly string _baseDirectory;
		private int _quality;
		private string _imageFormat;

		public StorageManager(string baseDirectory, int quality = 90, string imageFormat = "JPEG")
		{
			_baseDirectory = baseDirectory;
			_quality = quality;
			_imageFormat = imageFormat;
			EnsureDirectoryExists(_baseDirectory);
		}

		public void UpdateSettings(int quality, string imageFormat)
		{
			_quality = quality;
			_imageFormat = imageFormat;
		}

		private void EnsureDirectoryExists(string path)
		{
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}
		}

		public string GetMonthlyDirectory(DateTime date)
		{
			string monthFolder = date.ToString("yyyy-MM");
			string fullPath = Path.Combine(_baseDirectory, monthFolder);
			EnsureDirectoryExists(fullPath);
			return fullPath;
		}

		public string GenerateFileName(DateTime dateTime, bool isBackup = false, string format = "JPEG")
		{
			string prefix = isBackup ? "backup_" : "";
			string extension = format.ToUpper() == "PNG" ? "png" : "jpg";
			return $"{prefix}{dateTime:yyyy-MM-dd_HH-mm-ss}.{extension}";
		}

		public string SaveScreenshot(Bitmap bitmap, bool isBackup = false)
		{
			DateTime now = DateTime.Now;
			string monthlyDir = GetMonthlyDirectory(now);
			string fileName = GenerateFileName(now, isBackup, _imageFormat);
			string fullPath = Path.Combine(monthlyDir, fileName);

			try
			{
				if (_imageFormat.ToUpper() == "PNG")
				{
					bitmap.Save(fullPath, ImageFormat.Png);
					return fullPath;
				}

				ImageCodecInfo? jpegCodec = ImageCodecInfo.GetImageEncoders()
					.FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

				if (jpegCodec == null)
				{
					bitmap.Save(fullPath, ImageFormat.Jpeg);
					return fullPath;
				}

				EncoderParameters encoderParams = new EncoderParameters(1);
				encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
				bitmap.Save(fullPath, jpegCodec, encoderParams);
				encoderParams.Dispose();

				return fullPath;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error saving screenshot: {ex.Message}");
				throw;
			}
		}

		public void DeleteBackupScreenshot(string backupPath)
		{
			try
			{
				if (File.Exists(backupPath))
				{
					File.Delete(backupPath);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error deleting backup screenshot: {ex.Message}");
			}
		}

		public string? PromoteBackupToMain(string backupPath)
		{
			try
			{
				if (!File.Exists(backupPath))
				{
					return null;
				}

				string fileName = Path.GetFileName(backupPath);
				if (!fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}

				string newFileName = fileName.Substring(7); // Remove "backup_" prefix
				string directory = Path.GetDirectoryName(backupPath) ?? _baseDirectory;
				string newPath = Path.Combine(directory, newFileName);

				if (File.Exists(newPath))
				{
					// If main file already exists, just delete the backup
					File.Delete(backupPath);
					return newPath;
				}

				File.Move(backupPath, newPath);
				System.Diagnostics.Debug.WriteLine($"Promoted backup screenshot to main: {newFileName}");
				return newPath;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error promoting backup to main: {ex.Message}");
				return null;
			}
		}

		private bool IsValidScreenshot(string filePath)
		{
			string ext = Path.GetExtension(filePath).ToLower();
			string fileName = Path.GetFileName(filePath);
			return (ext == ".jpg" || ext == ".png") &&
				   !fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
		}

		public List<ScreenshotMetadata> GetScreenshotsByDateRange(DateTime startDate, DateTime endDate)
		{
			List<ScreenshotMetadata> screenshots = new List<ScreenshotMetadata>();

			DateTime current = new DateTime(startDate.Year, startDate.Month, 1);
			DateTime end = new DateTime(endDate.Year, endDate.Month, 1);

			while (current <= end)
			{
				string monthDir = Path.Combine(_baseDirectory, current.ToString("yyyy-MM"));
				if (Directory.Exists(monthDir))
				{
					var files = Directory.GetFiles(monthDir, "*.*")
						.Where(IsValidScreenshot)
						.Select(f => new FileInfo(f))
						.Where(fi => fi.CreationTime >= startDate && fi.CreationTime <= endDate.AddDays(1))
						.OrderByDescending(fi => fi.CreationTime);

					foreach (var file in files)
					{
						screenshots.Add(new ScreenshotMetadata
						{
							FilePath = file.FullName,
							DateTaken = file.CreationTime,
							FileName = file.Name
						});
					}
				}
				current = current.AddMonths(1);
			}

			return screenshots;
		}

		public List<ScreenshotMetadata> GetScreenshotsByMonth(int year, int month)
		{
			DateTime startDate = new DateTime(year, month, 1);
			DateTime endDate = startDate.AddMonths(1).AddDays(-1);
			return GetScreenshotsByDateRange(startDate, endDate);
		}

		public List<ScreenshotMetadata> GetAllScreenshots()
		{
			List<ScreenshotMetadata> screenshots = new List<ScreenshotMetadata>();

			if (!Directory.Exists(_baseDirectory))
			{
				return screenshots;
			}

			var monthDirs = Directory.GetDirectories(_baseDirectory)
				.Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d{4}-\d{2}$"))
				.OrderBy(d => d);

			foreach (string monthDir in monthDirs)
			{
				var files = Directory.GetFiles(monthDir, "*.*")
					.Where(IsValidScreenshot)
					.Select(f => new FileInfo(f))
					.OrderByDescending(fi => fi.CreationTime);

				foreach (var file in files)
				{
					screenshots.Add(new ScreenshotMetadata
					{
						FilePath = file.FullName,
						DateTaken = file.CreationTime,
						FileName = file.Name
					});
				}
			}

			return screenshots;
		}

		public long GetTotalStorageUsed()
		{
			var allScreenshots = GetAllScreenshots();
			long totalBytes = 0;

			foreach (var screenshot in allScreenshots)
			{
				try
				{
					if (File.Exists(screenshot.FilePath))
					{
						var fileInfo = new FileInfo(screenshot.FilePath);
						totalBytes += fileInfo.Length;
					}
				}
				catch
				{
				}
			}

			return totalBytes;
		}

		public int GetLongestStreak()
		{
			var allScreenshots = GetAllScreenshots();
			if (allScreenshots.Count == 0)
			{
				return 0;
			}

			var dates = allScreenshots
				.Select(s => s.DateTaken.Date)
				.Distinct()
				.OrderBy(d => d)
				.ToList();

			if (dates.Count == 0)
			{
				return 0;
			}

			int longestStreak = 1;
			int currentStreak = 1;

			for (int i = 1; i < dates.Count; i++)
			{
				TimeSpan diff = dates[i] - dates[i - 1];
				if (diff.Days == 1)
				{
					currentStreak++;
					longestStreak = Math.Max(longestStreak, currentStreak);
				}
				else
				{
					currentStreak = 1;
				}
			}

			return longestStreak;
		}

		public string GetBaseDirectory()
		{
			return _baseDirectory;
		}

		public (int minYear, int minMonth, int maxYear, int maxMonth) GetDateRange()
		{
			var allScreenshots = GetAllScreenshots();

			if (allScreenshots.Count == 0)
			{
				DateTime now = DateTime.Now;
				return (now.Year, now.Month, now.Year, now.Month);
			}

			var dates = allScreenshots
				.Select(s => s.DateTaken)
				.OrderBy(d => d)
				.ToList();

			DateTime minDate = dates.First();
			DateTime maxDate = dates.Last();

			return (minDate.Year, minDate.Month, maxDate.Year, maxDate.Month);
		}

		public (int minMonth, int maxMonth) GetMonthRangeForYear(int year)
		{
			var allScreenshots = GetAllScreenshots();
			var yearScreenshots = allScreenshots
				.Where(s => s.DateTaken.Year == year)
				.ToList();

			if (yearScreenshots.Count == 0)
			{
				DateTime now = DateTime.Now;
				if (year == now.Year)
				{
					return (1, now.Month);
				}
				return (1, 12);
			}

			var months = yearScreenshots
				.Select(s => s.DateTaken.Month)
				.OrderBy(m => m)
				.ToList();

			int minMonth = months.First();
			int maxMonth = months.Last();

			DateTime now2 = DateTime.Now;
			if (year == now2.Year)
			{
				maxMonth = Math.Min(maxMonth, now2.Month);
			}

			return (minMonth, maxMonth);
		}
	}
}

