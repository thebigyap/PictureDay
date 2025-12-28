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
                        .Where(f =>
                        {
                            string ext = Path.GetExtension(f).ToLower();
                            return (ext == ".jpg" || ext == ".png") &&
                                   !Path.GetFileName(f).StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
                        })
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

