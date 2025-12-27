using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PictureDay.Models;
using PictureDay.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace PictureDay.Views
{
    public partial class PhotoGalleryView : UserControl
    {
        private StorageManager? _storageManager;
        private int _currentYear;
        private int _currentMonth;

        public PhotoGalleryView()
        {
            InitializeComponent();
            InitializeDateControls();
        }

        public void Initialize(StorageManager storageManager)
        {
            _storageManager = storageManager;
            _currentYear = DateTime.Now.Year;
            _currentMonth = DateTime.Now.Month;
            LoadPhotos();
        }

        private void InitializeDateControls()
        {
            for (int i = 1; i <= 12; i++)
            {
                MonthComboBox.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i));
            }

            int currentYear = DateTime.Now.Year;
            for (int i = currentYear - 5; i <= currentYear + 1; i++)
            {
                YearComboBox.Items.Add(i);
            }

            MonthComboBox.SelectedIndex = DateTime.Now.Month - 1;
            YearComboBox.SelectedItem = DateTime.Now.Year;
        }

        private void LoadPhotos()
        {
            if (_storageManager == null) return;

            PhotosItemsControl.Items.Clear();

            try
            {
                List<ScreenshotMetadata> screenshots = _storageManager.GetScreenshotsByMonth(_currentYear, _currentMonth);

                foreach (var screenshot in screenshots)
                {
                    if (File.Exists(screenshot.FilePath))
                    {
                        var item = new
                        {
                            ImageSource = LoadThumbnail(screenshot.FilePath),
                            DateLabel = screenshot.DateTaken.ToString("MM/dd/yyyy HH:mm")
                        };
                        PhotosItemsControl.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading photos: {ex.Message}");
            }
        }

        private BitmapImage LoadThumbnail(string filePath)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.DecodePixelWidth = 150;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image image && image.Source is BitmapImage bitmapImage)
            {
                Window imageWindow = new Window
                {
                    Title = "Screenshot",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                System.Windows.Controls.Image fullImage = new System.Windows.Controls.Image
                {
                    Source = bitmapImage,
                    Stretch = System.Windows.Media.Stretch.Uniform
                };

                imageWindow.Content = fullImage;
                imageWindow.Show();
            }
        }

        private void PrevMonthButton_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth--;
            if (_currentMonth < 1)
            {
                _currentMonth = 12;
                _currentYear--;
            }
            UpdateDateControls();
            LoadPhotos();
        }

        private void NextMonthButton_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth++;
            if (_currentMonth > 12)
            {
                _currentMonth = 1;
                _currentYear++;
            }
            UpdateDateControls();
            LoadPhotos();
        }

        private void MonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MonthComboBox.SelectedIndex >= 0)
            {
                _currentMonth = MonthComboBox.SelectedIndex + 1;
                LoadPhotos();
            }
        }

        private void YearComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (YearComboBox.SelectedItem is int year)
            {
                _currentYear = year;
                LoadPhotos();
            }
        }

        private void UpdateDateControls()
        {
            MonthComboBox.SelectedIndex = _currentMonth - 1;
            YearComboBox.SelectedItem = _currentYear;
        }
    }
}

