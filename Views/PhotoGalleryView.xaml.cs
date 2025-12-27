using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
                            FilePath = screenshot.FilePath,
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
            if (sender is System.Windows.Controls.Image image)
            {
                var dataContext = image.DataContext;
                if (dataContext == null) return;

                string? filePath = null;
                var filePathProperty = dataContext.GetType().GetProperty("FilePath");
                if (filePathProperty != null)
                {
                    filePath = filePathProperty.GetValue(dataContext) as string;
                }

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return;
                }

                Window imageWindow = new Window
                {
                    Title = $"Screenshot - {Path.GetFileName(filePath)}",
                    Width = 1200,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                BitmapImage fullResolutionImage = new BitmapImage();
                fullResolutionImage.BeginInit();
                fullResolutionImage.UriSource = new Uri(filePath);
                fullResolutionImage.CacheOption = BitmapCacheOption.OnLoad;
                fullResolutionImage.EndInit();
                fullResolutionImage.Freeze();

                System.Windows.Controls.Image fullImage = new System.Windows.Controls.Image
                {
                    Source = fullResolutionImage,
                    Stretch = System.Windows.Media.Stretch.Uniform
                };

                Grid grid = new Grid();
                grid.Children.Add(fullImage);

                ScrollViewer scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = grid
                };

                imageWindow.Content = scrollViewer;

                imageWindow.Loaded += (s, e) =>
                {
                    if (fullResolutionImage.Width > 0 && fullResolutionImage.Height > 0)
                    {
                        double windowWidth = imageWindow.ActualWidth;
                        double windowHeight = imageWindow.ActualHeight;
                        double imageWidth = fullResolutionImage.Width;
                        double imageHeight = fullResolutionImage.Height;

                        double widthRatio = windowWidth / imageWidth;
                        double heightRatio = windowHeight / imageHeight;
                        double scale = Math.Min(widthRatio, heightRatio);

                        if (scale < 1.0)
                        {
                            fullImage.Width = imageWidth * scale;
                            fullImage.Height = imageHeight * scale;
                        }
                        else
                        {
                            fullImage.Width = imageWidth;
                            fullImage.Height = imageHeight;
                        }
                    }
                };

                imageWindow.SizeChanged += (s, e) =>
                {
                    if (fullResolutionImage.Width > 0 && fullResolutionImage.Height > 0)
                    {
                        double windowWidth = imageWindow.ActualWidth;
                        double windowHeight = imageWindow.ActualHeight;
                        double imageWidth = fullResolutionImage.Width;
                        double imageHeight = fullResolutionImage.Height;

                        double widthRatio = windowWidth / imageWidth;
                        double heightRatio = windowHeight / imageHeight;
                        double scale = Math.Min(widthRatio, heightRatio);

                        if (scale < 1.0)
                        {
                            fullImage.Width = imageWidth * scale;
                            fullImage.Height = imageHeight * scale;
                        }
                        else
                        {
                            fullImage.Width = imageWidth;
                            fullImage.Height = imageHeight;
                        }
                    }
                };

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

