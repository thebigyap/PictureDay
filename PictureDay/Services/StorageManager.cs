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
        private readonly int _quality;

        public StorageManager(string baseDirectory, int quality = 90)
        {
            _baseDirectory = baseDirectory;
            _quality = quality;
            EnsureDirectoryExists(_baseDirectory);
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

        public string GenerateFileName(DateTime dateTime, bool isBackup = false)
        {
            string prefix = isBackup ? "backup_" : "";
            return $"{prefix}{dateTime:yyyy-MM-dd_HH-mm-ss}.jpg";
        }

        public string SaveScreenshot(Bitmap bitmap, bool isBackup = false)
        {
            DateTime now = DateTime.Now;
            string monthlyDir = GetMonthlyDirectory(now);
            string fileName = GenerateFileName(now, isBackup);
            string fullPath = Path.Combine(monthlyDir, fileName);

            try
            {
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
                    var files = Directory.GetFiles(monthDir, "*.jpg")
                        .Where(f => !Path.GetFileName(f).StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
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
    }
}

