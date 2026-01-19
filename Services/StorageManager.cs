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
		private Random _random = new Random();

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

	public string GenerateFileName(DateTime dateTime, bool isBackup = false, bool isQuarter = false, bool isUser = false, string format = "JPEG")
	{
		string prefix = "";
		if (isUser)
		{
			prefix = "u_";
		}
		else if (isQuarter)
		{
			prefix = "quarter_";
		}
		else if (isBackup)
		{
			prefix = "backup_";
		}
		string extension = format.ToUpper() == "PNG" ? "png" : "jpg";
		return $"{prefix}{dateTime:yyyy-MM-dd_HH-mm-ss}.{extension}";
	}

	public string SaveScreenshot(Bitmap bitmap, bool isBackup = false, bool isQuarter = false, bool isUser = false)
	{
		DateTime now = DateTime.Now;
		string photoType = isUser ? "USER" : (isBackup ? "BACKUP" : (isQuarter ? "QUARTER" : "MAIN"));
		string monthlyDir = GetMonthlyDirectory(now);
		string fileName = GenerateFileName(now, isBackup, isQuarter, isUser, _imageFormat);
		string fullPath = Path.Combine(monthlyDir, fileName);

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] StorageManager.SaveScreenshot - Type: {photoType}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Date: {now.Date:yyyy-MM-dd}, Time: {now:HH:mm:ss}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Monthly directory: {monthlyDir}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   File name: {fileName}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Full path: {fullPath}");

			try
			{
				if (_imageFormat.ToUpper() == "PNG")
				{
					bitmap.Save(fullPath, ImageFormat.Png);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Screenshot saved as PNG");
					return fullPath;
				}

				ImageCodecInfo? jpegCodec = ImageCodecInfo.GetImageEncoders()
					.FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

				if (jpegCodec == null)
				{
					bitmap.Save(fullPath, ImageFormat.Jpeg);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Screenshot saved as JPEG (no codec)");
					return fullPath;
				}

				EncoderParameters encoderParams = new EncoderParameters(1);
				encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
				bitmap.Save(fullPath, jpegCodec, encoderParams);
				encoderParams.Dispose();

				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Screenshot saved as JPEG with quality {_quality}");
				return fullPath;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR saving screenshot: {ex.Message}");
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Stack trace: {ex.StackTrace}");
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

		public string? PromotePhotoToMain(string photoPath)
		{
			DateTime now = DateTime.Now;
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] PromotePhotoToMain called for: {photoPath}");

			try
			{
				if (!File.Exists(photoPath))
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: Photo file does not exist: {photoPath}");
					return null;
				}

				string fileName = Path.GetFileName(photoPath);
				string newFileName = fileName;
				string prefix = "";

				if (fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase))
				{
					prefix = "quarter_";
					newFileName = fileName.Substring(8);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Detected quarter photo, removing prefix");
				}
				else if (fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
				{
					prefix = "backup_";
					newFileName = fileName.Substring(7);
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Detected backup photo, removing prefix");
				}
				else
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Photo is already main (no prefix), returning as-is");
					return photoPath;
				}

				string directory = Path.GetDirectoryName(photoPath) ?? _baseDirectory;
				string newPath = Path.Combine(directory, newFileName);
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] New path will be: {newPath}");

				if (File.Exists(newPath))
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] WARNING: Target file already exists. Comparing file sizes to determine which to keep...");

					try
					{
						var sourceInfo = new FileInfo(photoPath);
						var targetInfo = new FileInfo(newPath);

						if (sourceInfo.Length > targetInfo.Length)
						{
							Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Source file is larger, replacing target with source");
							File.Delete(newPath);
							File.Move(photoPath, newPath);
							return newPath;
						}
						else
						{
							Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Target file is larger or equal, keeping target and deleting source");
							File.Delete(photoPath);
							return newPath;
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR comparing files: {ex.Message}. Keeping source file.");
						return photoPath;
					}
				}

				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Moving file from {photoPath} to {newPath}");
				File.Move(photoPath, newPath);
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Successfully promoted {prefix} screenshot to main: {newFileName}");
				System.Diagnostics.Debug.WriteLine($"Promoted {prefix} screenshot to main: {newFileName}");
				return newPath;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR promoting photo to main: {ex.Message}");
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Stack trace: {ex.StackTrace}");
				System.Diagnostics.Debug.WriteLine($"Error promoting photo to main: {ex.Message}");
				return null;
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

		public List<PhotoInfo> GetPhotosForDate(DateTime date)
		{
			DateTime now = DateTime.Now;
			List<PhotoInfo> photos = new List<PhotoInfo>();
			string monthDir = GetMonthlyDirectory(date);
			string dateStr = date.ToString("yyyy-MM-dd");

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] GetPhotosForDate called for: {date:yyyy-MM-dd}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Month directory: {monthDir}");
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Searching for files with exact date: {dateStr}");

			if (!Directory.Exists(monthDir))
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Month directory does not exist, returning empty list");
				return photos;
			}

			var allFiles = Directory.EnumerateFiles(monthDir, "*.jpg")
				.Concat(Directory.EnumerateFiles(monthDir, "*.png"));

			int totalFiles = 0;
			foreach (string filePath in allFiles)
			{
				totalFiles++;
				string fileName = Path.GetFileName(filePath);

				bool matches = false;
				string datePattern = $"{dateStr}_";

			if (fileName.StartsWith("u_", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			else if (fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase))
			{
				if (fileName.Length >= 8 + datePattern.Length &&
					fileName.Substring(8, datePattern.Length).Equals(datePattern, StringComparison.OrdinalIgnoreCase))
				{
					matches = true;
				}
			}
			else if (fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
			{
				if (fileName.Length >= 7 + datePattern.Length &&
					fileName.Substring(7, datePattern.Length).Equals(datePattern, StringComparison.OrdinalIgnoreCase))
				{
					matches = true;
				}
			}
			else
			{
				if (fileName.StartsWith(datePattern, StringComparison.OrdinalIgnoreCase))
				{
					matches = true;
				}
			}

				if (matches)
				{
					bool isMain = !fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase) &&
								  !fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
					bool isQuarter = fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase);
					bool isBackup = fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);

					photos.Add(new PhotoInfo
					{
						FilePath = filePath,
						FileName = fileName,
						IsMain = isMain,
						IsQuarter = isQuarter,
						IsBackup = isBackup
					});
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   Found matching file: {fileName} (Main: {isMain}, Quarter: {isQuarter}, Backup: {isBackup})");
				}
			}

			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] GetPhotosForDate complete: {photos.Count} matching photos found out of {totalFiles} total files");
			return photos;
		}

		public void ProcessDailyPhotoSelection(DateTime date)
		{
			DateTime now = DateTime.Now;
			Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] *** ProcessDailyPhotoSelection called for date: {date:yyyy-MM-dd} ***");

			try
			{
				var photos = GetPhotosForDate(date);
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Found {photos.Count} total photos for {date:yyyy-MM-dd}");

				if (photos.Count == 0)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] No photos found for {date:yyyy-MM-dd}, returning");
					return;
				}

				var mainPhotos = photos.Where(p => p.IsMain).ToList();
				var quarterPhotos = photos.Where(p => p.IsQuarter).ToList();
				var backupPhotos = photos.Where(p => p.IsBackup).ToList();

				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Photo breakdown: {mainPhotos.Count} main, {quarterPhotos.Count} quarter, {backupPhotos.Count} backup");

				foreach (var photo in photos)
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}]   - {photo.FileName} (Main: {photo.IsMain}, Quarter: {photo.IsQuarter}, Backup: {photo.IsBackup})");
				}

				if (mainPhotos.Any())
				{
					if (mainPhotos.Count > 1)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Multiple main photos found ({mainPhotos.Count}), keeping only the first one");
						var photoToKeep = mainPhotos.OrderBy(p => p.FileName).First();
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Keeping: {photoToKeep.FileName}");

						foreach (var photo in mainPhotos.Where(p => p.FilePath != photoToKeep.FilePath))
						{
							try
							{
								if (File.Exists(photo.FilePath))
								{
									Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting extra main photo: {photo.FileName}");
									File.Delete(photo.FilePath);
								}
							}
							catch (Exception ex)
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR deleting extra main photo {photo.FilePath}: {ex.Message}");
								System.Diagnostics.Debug.WriteLine($"Error deleting extra main photo {photo.FilePath}: {ex.Message}");
							}
						}
					}

					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Main photos exist, deleting {quarterPhotos.Count + backupPhotos.Count} quarter/backup photos");
					foreach (var photo in quarterPhotos.Concat(backupPhotos))
					{
						try
						{
							if (File.Exists(photo.FilePath))
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting: {photo.FileName}");
								File.Delete(photo.FilePath);
							}
							else
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] File not found (already deleted?): {photo.FilePath}");
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR deleting photo {photo.FilePath}: {ex.Message}");
							System.Diagnostics.Debug.WriteLine($"Error deleting photo {photo.FilePath}: {ex.Message}");
						}
					}
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ProcessDailyPhotoSelection complete - main photos kept");
					return;
				}

				if (quarterPhotos.Any())
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] No main photos, promoting one of {quarterPhotos.Count} quarter photos");
					var selectedPhoto = quarterPhotos[_random.Next(quarterPhotos.Count)];
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Selected quarter photo to promote: {selectedPhoto.FileName}");
					string? promotedPath = PromotePhotoToMain(selectedPhoto.FilePath);
					if (promotedPath != null)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Successfully promoted to: {Path.GetFileName(promotedPath)}");

						int deleteCount = quarterPhotos.Count(p => p.FilePath != selectedPhoto.FilePath) + backupPhotos.Count;
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting {deleteCount} remaining quarter/backup photos");
						foreach (var photo in quarterPhotos.Where(p => p.FilePath != selectedPhoto.FilePath).Concat(backupPhotos))
						{
							try
							{
								if (File.Exists(photo.FilePath))
								{
									Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting: {photo.FileName}");
									File.Delete(photo.FilePath);
								}
								else
								{
									Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] File not found (already deleted?): {photo.FilePath}");
								}
							}
							catch (Exception ex)
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR deleting photo {photo.FilePath}: {ex.Message}");
								System.Diagnostics.Debug.WriteLine($"Error deleting photo {photo.FilePath}: {ex.Message}");
							}
						}
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ProcessDailyPhotoSelection complete - quarter photo promoted");
					}
					else
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: Promotion returned null! Aborting deletion to prevent data loss.");
					}
					return;
				}

				if (backupPhotos.Any())
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] No main or quarter photos, promoting one of {backupPhotos.Count} backup photos");
					var selectedPhoto = backupPhotos[_random.Next(backupPhotos.Count)];
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Selected backup photo to promote: {selectedPhoto.FileName}");
					string? promotedPath = PromotePhotoToMain(selectedPhoto.FilePath);
					if (promotedPath != null)
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Successfully promoted to: {Path.GetFileName(promotedPath)}");

						int deleteCount = backupPhotos.Count(p => p.FilePath != selectedPhoto.FilePath);
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting {deleteCount} remaining backup photos");
						foreach (var photo in backupPhotos.Where(p => p.FilePath != selectedPhoto.FilePath))
						{
							try
							{
								if (File.Exists(photo.FilePath))
								{
									Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Deleting: {photo.FileName}");
									File.Delete(photo.FilePath);
								}
								else
								{
									Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] File not found (already deleted?): {photo.FilePath}");
								}
							}
							catch (Exception ex)
							{
								Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR deleting photo {photo.FilePath}: {ex.Message}");
								System.Diagnostics.Debug.WriteLine($"Error deleting photo {photo.FilePath}: {ex.Message}");
							}
						}
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ProcessDailyPhotoSelection complete - backup photo promoted");
					}
					else
					{
						Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR: Promotion returned null! Aborting deletion to prevent data loss.");
					}
				}
				else
				{
					Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] WARNING: No main, quarter, or backup photos found (but photos.Count > 0)");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] ERROR processing daily photo selection: {ex.Message}");
				Console.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss}] Stack trace: {ex.StackTrace}");
				System.Diagnostics.Debug.WriteLine($"Error processing daily photo selection: {ex.Message}");
			}
		}

		public void CleanupOrphanedPhotos()
		{
			try
			{
				if (!Directory.Exists(_baseDirectory))
				{
					return;
				}

				var monthDirs = Directory.GetDirectories(_baseDirectory)
					.Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d{4}-\d{2}$"));

				HashSet<DateTime> processedDates = new HashSet<DateTime>();

				foreach (string monthDir in monthDirs)
				{
					var allFiles = Directory.EnumerateFiles(monthDir, "*.jpg")
						.Concat(Directory.EnumerateFiles(monthDir, "*.png"));

				foreach (string filePath in allFiles)
				{
					string fileName = Path.GetFileName(filePath);
					if (!fileName.Contains("_"))
					{
						continue;
					}

					if (fileName.StartsWith("u_", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					try
					{
						int dateStartIndex = -1;

						if (fileName.StartsWith("quarter_", StringComparison.OrdinalIgnoreCase))
							{
								int firstUnderscore = fileName.IndexOf('_');
								int secondUnderscore = fileName.IndexOf('_', firstUnderscore + 1);
								if (secondUnderscore >= 0)
								{
									dateStartIndex = secondUnderscore - 10;
								}
							}
							else if (fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
							{
								int firstUnderscore = fileName.IndexOf('_');
								int secondUnderscore = fileName.IndexOf('_', firstUnderscore + 1);
								if (secondUnderscore >= 0)
								{
									dateStartIndex = secondUnderscore - 10;
								}
							}
							else
							{
								int firstUnderscore = fileName.IndexOf('_');
								if (firstUnderscore >= 0)
								{
									dateStartIndex = firstUnderscore - 10;
								}
							}

							if (dateStartIndex < 0 || dateStartIndex + 10 > fileName.Length)
							{
								continue;
							}

							string datePart = fileName.Substring(dateStartIndex, 10);
							if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime fileDate))
							{
								DateTime date = fileDate.Date;
								if (processedDates.Contains(date))
								{
									continue;
								}
								processedDates.Add(date);

								var photos = GetPhotosForDate(date);
								var mainPhotos = photos.Where(p => p.IsMain).ToList();

								if (!mainPhotos.Any())
								{
									foreach (var photo in photos.Where(p => p.IsQuarter || p.IsBackup))
									{
										try
										{
											if (File.Exists(photo.FilePath))
											{
												File.Delete(photo.FilePath);
											}
										}
										catch (Exception ex)
										{
											System.Diagnostics.Debug.WriteLine($"Error deleting orphaned photo {photo.FilePath}: {ex.Message}");
										}
									}
								}
							}
						}
						catch { }
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error cleaning up orphaned photos: {ex.Message}");
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

		public int GetCurrentStreak()
		{
			var allScreenshots = GetAllScreenshots();
			if (allScreenshots.Count == 0)
			{
				return 0;
			}

			var dates = allScreenshots
				.Select(s => s.DateTaken.Date)
				.Distinct()
				.OrderByDescending(d => d)
				.ToList();

			if (dates.Count == 0)
			{
				return 0;
			}

			DateTime today = DateTime.Now.Date;
			DateTime mostRecentDate = dates[0];

			if (mostRecentDate < today.AddDays(-1))
			{
				return 0;
			}

			int currentStreak = 1;
			DateTime currentDate = mostRecentDate;

			for (int i = 1; i < dates.Count; i++)
			{
				TimeSpan diff = currentDate - dates[i];
				if (diff.Days == 1)
				{
					currentStreak++;
					currentDate = dates[i];
				}
				else
				{
					break;
				}
			}

			return currentStreak;
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

