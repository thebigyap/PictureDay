using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PictureDay.Models;
using PictureDay.Services;
using UserControl = System.Windows.Controls.UserControl;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

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
		}

		public void Initialize(StorageManager storageManager)
		{
			_storageManager = storageManager;
			InitializeDateControls();
			LoadPhotos();
		}

		private void InitializeDateControls()
		{
			if (_storageManager == null)
			{
				MonthComboBox.SelectedIndex = DateTime.Now.Month - 1;
				YearComboBox.SelectedItem = DateTime.Now.Year;
				return;
			}

			var (minYear, minMonth, maxYear, maxMonth) = _storageManager.GetDateRange();

			YearComboBox.Items.Clear();
			for (int year = minYear; year <= maxYear; year++)
			{
				YearComboBox.Items.Add(year);
			}

			_currentYear = DateTime.Now.Year;
			_currentMonth = DateTime.Now.Month;

			if (_currentYear < minYear || _currentYear > maxYear)
			{
				_currentYear = maxYear;
				_currentMonth = maxMonth;
			}
			else if (_currentYear == minYear && _currentMonth < minMonth)
			{
				_currentMonth = minMonth;
			}
			else if (_currentYear == maxYear && _currentMonth > maxMonth)
			{
				_currentMonth = maxMonth;
			}

			UpdateMonthComboBox();
			YearComboBox.SelectedItem = _currentYear;
		}

		private void UpdateMonthComboBox()
		{
			if (_storageManager == null) return;

			var (minMonth, maxMonth) = _storageManager.GetMonthRangeForYear(_currentYear);

			MonthComboBox.Items.Clear();

			for (int i = minMonth; i <= maxMonth; i++)
			{
				MonthComboBox.Items.Add(new { MonthNumber = i, MonthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i) });
			}

			if (_currentMonth < minMonth || _currentMonth > maxMonth)
			{
				_currentMonth = minMonth;
			}

			int selectedIndex = -1;
			for (int i = 0; i < MonthComboBox.Items.Count; i++)
			{
				var item = MonthComboBox.Items[i];
				var monthNumberProperty = item.GetType().GetProperty("MonthNumber");
				if (monthNumberProperty != null && (int)monthNumberProperty.GetValue(item)! == _currentMonth)
				{
					selectedIndex = i;
					break;
				}
			}

			if (selectedIndex >= 0)
			{
				MonthComboBox.SelectedIndex = selectedIndex;
			}
			else if (MonthComboBox.Items.Count > 0)
			{
				MonthComboBox.SelectedIndex = 0;
			}
		}

		private void LoadPhotos()
		{
			if (_storageManager == null) return;

			PhotosItemsControl.Items.Clear();

			try
			{
				List<ScreenshotMetadata> screenshots = _storageManager.GetScreenshotsByMonth(_currentYear, _currentMonth);

				if (screenshots.Count == 0)
				{
					var noPhotosItem = new
					{
						IsNoPhotos = true,
						Message = "No Photos"
					};
					PhotosItemsControl.Items.Add(noPhotosItem);
				}
				else
				{
					foreach (var screenshot in screenshots)
					{
						if (File.Exists(screenshot.FilePath))
						{
							var item = new
							{
								ImageSource = LoadThumbnail(screenshot.FilePath),
								FilePath = screenshot.FilePath,
								DateLabel = screenshot.DateTaken.ToString("MM/dd/yyyy HH:mm"),
								IsNoPhotos = false
							};
							PhotosItemsControl.Items.Add(item);
						}
					}
				}

				UpdateStats();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error loading photos: {ex.Message}");
			}
		}

		private void UpdateStats()
		{
			if (_storageManager == null) return;

			try
			{
				var allScreenshots = _storageManager.GetAllScreenshots();
				int totalPhotos = allScreenshots.Count;
				TotalPhotosValue.Text = totalPhotos.ToString();

				long totalBytes = _storageManager.GetTotalStorageUsed();
				StorageUsedValue.Text = FormatBytes(totalBytes);

				int longestStreak = _storageManager.GetLongestStreak();
				LongestStreakValue.Text = $"{longestStreak} day{(longestStreak != 1 ? "s" : "")}";
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error updating stats: {ex.Message}");
			}
		}

		private string FormatBytes(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len = len / 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		private BitmapImage LoadThumbnail(string filePath)
		{
			BitmapImage bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.UriSource = new Uri(filePath);
			bitmap.DecodePixelWidth = 250;
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

				OpenImageInViewer(filePath);
			}
		}

		private void PrevMonthButton_Click(object sender, RoutedEventArgs e)
		{
			if (_storageManager == null) return;

			var (minYear, minMonth, maxYear, maxMonth) = _storageManager.GetDateRange();

			_currentMonth--;
			if (_currentMonth < 1)
			{
				_currentMonth = 12;
				_currentYear--;
			}

			DateTime currentDate = new DateTime(_currentYear, _currentMonth, 1);
			DateTime minDate = new DateTime(minYear, minMonth, 1);

			if (currentDate < minDate)
			{
				_currentYear = minYear;
				_currentMonth = minMonth;
			}

			UpdateMonthComboBox();
			UpdateDateControls();
			LoadPhotos();
		}

		private void NextMonthButton_Click(object sender, RoutedEventArgs e)
		{
			if (_storageManager == null) return;

			var (minYear, minMonth, maxYear, maxMonth) = _storageManager.GetDateRange();

			_currentMonth++;
			if (_currentMonth > 12)
			{
				_currentMonth = 1;
				_currentYear++;
			}

			DateTime currentDate = new DateTime(_currentYear, _currentMonth, 1);
			DateTime maxDate = new DateTime(maxYear, maxMonth, 1);

			if (currentDate > maxDate)
			{
				_currentYear = maxYear;
				_currentMonth = maxMonth;
			}

			UpdateMonthComboBox();
			UpdateDateControls();
			LoadPhotos();
		}

		private void MonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (MonthComboBox.SelectedItem != null)
			{
				var monthNumberProperty = MonthComboBox.SelectedItem.GetType().GetProperty("MonthNumber");
				if (monthNumberProperty != null)
				{
					_currentMonth = (int)monthNumberProperty.GetValue(MonthComboBox.SelectedItem)!;
					LoadPhotos();
				}
			}
		}

		private void YearComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (YearComboBox.SelectedItem is int year)
			{
				_currentYear = year;
				UpdateMonthComboBox();
				MonthComboBox.SelectedIndex = _currentMonth - 1;
				LoadPhotos();
			}
		}

		private void UpdateDateControls()
		{
			MonthComboBox.SelectedIndex = _currentMonth - 1;
			YearComboBox.SelectedItem = _currentYear;
		}

		private string? GetFilePathFromContext(object sender)
		{
			if (sender is MenuItem menuItem)
			{
				var contextMenu = menuItem.Parent as ContextMenu;
				if (contextMenu?.PlacementTarget is System.Windows.Controls.Image image)
				{
					var dataContext = image.DataContext;
					if (dataContext != null)
					{
						var filePathProperty = dataContext.GetType().GetProperty("FilePath");
						if (filePathProperty != null)
						{
							return filePathProperty.GetValue(dataContext) as string;
						}
					}
				}
			}
			return null;
		}

		private void ContextMenu_Open_Click(object sender, RoutedEventArgs e)
		{
			string? filePath = GetFilePathFromContext(sender);
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return;
			}

			// Reuse the existing Image_MouseLeftButtonDown logic
			OpenImageInViewer(filePath);
		}

		private void Image_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			// This ensures the context menu gets the correct data context
			if (sender is System.Windows.Controls.Image image)
			{
				e.Handled = false; // Allow context menu to show
			}
		}

		private void ContextMenu_OpenInExplorer_Click(object sender, RoutedEventArgs e)
		{
			string? filePath = GetFilePathFromContext(sender);
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return;
			}

			try
			{
				// Open the folder and select the file
				Process.Start("explorer.exe", $"/select,\"{filePath}\"");
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error opening file in Explorer: {ex.Message}", "Error",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ContextMenu_CopyImage_Click(object sender, RoutedEventArgs e)
		{
			string? filePath = GetFilePathFromContext(sender);
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return;
			}

			try
			{
				BitmapImage bitmap = new BitmapImage();
				bitmap.BeginInit();
				bitmap.UriSource = new Uri(filePath);
				bitmap.CacheOption = BitmapCacheOption.OnLoad;
				bitmap.EndInit();
				bitmap.Freeze();

				Clipboard.SetImage(bitmap);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error copying image: {ex.Message}", "Error",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
		{
			string? filePath = GetFilePathFromContext(sender);
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}

			try
			{
				Clipboard.SetText(filePath);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error copying file path: {ex.Message}", "Error",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
		{
			string? filePath = GetFilePathFromContext(sender);
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return;
			}

			var result = MessageBox.Show(
				$"Are you sure you want to delete this screenshot?\n\n{Path.GetFileName(filePath)}",
				"Delete Screenshot",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);

			if (result == MessageBoxResult.Yes)
			{
				try
				{
					File.Delete(filePath);
					LoadPhotos(); // Refresh the gallery
				}
				catch (Exception ex)
				{
					MessageBox.Show($"Error deleting file: {ex.Message}", "Error",
						MessageBoxButton.OK, MessageBoxImage.Error);
				}
			}
		}

		private void OpenImageInViewer(string filePath)
		{
			// Get all screenshots for the current month, sorted by date
			List<ScreenshotMetadata> allScreenshots = new List<ScreenshotMetadata>();
			if (_storageManager != null)
			{
				allScreenshots = _storageManager.GetScreenshotsByMonth(_currentYear, _currentMonth)
					.Where(s => File.Exists(s.FilePath))
					.OrderBy(s => s.DateTaken)
					.ToList();
			}

			// Find current image index
			int currentIndex = allScreenshots.FindIndex(s => s.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
			if (currentIndex < 0) currentIndex = 0;

			Window imageWindow = new Window
			{
				Title = $"Screenshot - {Path.GetFileName(filePath)}",
				Width = 1200,
				Height = 800,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("BackgroundBrush") ?? System.Windows.Media.Brushes.White
			};

			double zoomLevel = 1.0;
			double baseImageWidth = 0;
			double baseImageHeight = 0;
			double baseFitScale = 1.0;

			System.Windows.Controls.Image fullImage = new System.Windows.Controls.Image
			{
				Stretch = System.Windows.Media.Stretch.Uniform
			};

			System.Windows.Controls.Button exitButton = new System.Windows.Controls.Button
			{
				Content = "Exit",
				Padding = new Thickness(10, 5, 10, 5),
				Margin = new Thickness(10, 5, 0, 5),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				VerticalAlignment = System.Windows.VerticalAlignment.Top
			};

			System.Windows.Controls.TextBlock zoomLevelText = new System.Windows.Controls.TextBlock
			{
				Text = "100%",
				FontSize = 14,
				VerticalAlignment = System.Windows.VerticalAlignment.Center,
				Margin = new Thickness(5, 0, 5, 0),
				Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("TextBrush") ?? System.Windows.Media.Brushes.Black
			};

			System.Windows.Controls.Button zoomInButton = new System.Windows.Controls.Button
			{
				Content = "+",
				FontSize = 16,
				Width = 30,
				Height = 30,
				Padding = new Thickness(0),
				Margin = new Thickness(5, 0, 0, 0)
			};

			System.Windows.Controls.Button zoomOutButton = new System.Windows.Controls.Button
			{
				Content = "-",
				FontSize = 16,
				Width = 30,
				Height = 30,
				Padding = new Thickness(0),
				Margin = new Thickness(5, 0, 0, 0)
			};

			System.Windows.Controls.Button zoomResetButton = new System.Windows.Controls.Button
			{
				Content = "Reset",
				FontSize = 12,
				Padding = new Thickness(8, 4, 8, 4),
				Margin = new Thickness(5, 0, 0, 0)
			};

			System.Windows.Controls.StackPanel zoomPanel = new System.Windows.Controls.StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				VerticalAlignment = System.Windows.VerticalAlignment.Top,
				Margin = new Thickness(0, 5, 10, 5)
			};
			zoomPanel.Children.Add(zoomOutButton);
			zoomPanel.Children.Add(zoomLevelText);
			zoomPanel.Children.Add(zoomInButton);
			zoomPanel.Children.Add(zoomResetButton);

		System.Windows.Controls.Button leftArrow = new System.Windows.Controls.Button
		{
			Content = "←",
			FontSize = 24,
			Width = 50,
			Height = 50,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			VerticalAlignment = System.Windows.VerticalAlignment.Center,
			Margin = new Thickness(10, 0, 0, 0),
			Padding = new Thickness(0),
			Visibility = currentIndex < allScreenshots.Count - 1 ? Visibility.Visible : Visibility.Hidden
		};

		System.Windows.Controls.Button rightArrow = new System.Windows.Controls.Button
		{
			Content = "→",
			FontSize = 24,
			Width = 50,
			Height = 50,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			VerticalAlignment = System.Windows.VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 10, 0),
			Padding = new Thickness(0),
			Visibility = currentIndex > 0 ? Visibility.Visible : Visibility.Hidden
		};

			Action updateZoom = () =>
			{
				if (baseImageWidth > 0 && baseImageHeight > 0)
				{
					double scaledWidth = baseImageWidth * baseFitScale * zoomLevel;
					double scaledHeight = baseImageHeight * baseFitScale * zoomLevel;
					fullImage.Width = scaledWidth;
					fullImage.Height = scaledHeight;
					zoomLevelText.Text = $"{(int)(zoomLevel * 100)}%";
				}
			};

			Action? updateZoomWithCursor = null;
			Action<double, System.Windows.Point?>? zoomToPoint = null;

			Action<int> loadImage = (index) =>
			{
				if (index < 0 || index >= allScreenshots.Count) return;

				var screenshot = allScreenshots[index];
				if (!File.Exists(screenshot.FilePath)) return;

				BitmapImage fullResolutionImage = new BitmapImage();
				fullResolutionImage.BeginInit();
				fullResolutionImage.UriSource = new Uri(screenshot.FilePath);
				fullResolutionImage.CacheOption = BitmapCacheOption.OnLoad;
				fullResolutionImage.EndInit();
				fullResolutionImage.Freeze();

				fullImage.Source = fullResolutionImage;
				imageWindow.Title = $"Screenshot - {Path.GetFileName(screenshot.FilePath)}";

				leftArrow.Visibility = index < allScreenshots.Count - 1 ? Visibility.Visible : Visibility.Hidden;
				rightArrow.Visibility = index > 0 ? Visibility.Visible : Visibility.Hidden;

				zoomLevel = 1.0;

				// Calc image size and fit scale
				if (fullResolutionImage.Width > 0 && fullResolutionImage.Height > 0)
				{
					baseImageWidth = fullResolutionImage.Width;
					baseImageHeight = fullResolutionImage.Height;

					double windowWidth = imageWindow.ActualWidth;
					double windowHeight = imageWindow.ActualHeight;

					double widthRatio = windowWidth / baseImageWidth;
					double heightRatio = windowHeight / baseImageHeight;
					baseFitScale = Math.Min(widthRatio, heightRatio);

					if (baseFitScale > 1.0)
					{
						baseFitScale = 1.0;
					}

					updateZoomWithCursor?.Invoke();
				}
			};

			exitButton.Click += (s, args) =>
			{
				imageWindow.Close();
			};

			int currentImageIndex = currentIndex;
			leftArrow.Click += (s, args) =>
			{
				if (currentImageIndex < allScreenshots.Count - 1)
				{
					currentImageIndex++;
					loadImage(currentImageIndex);
				}
			};

			rightArrow.Click += (s, args) =>
			{
				if (currentImageIndex > 0)
				{
					currentImageIndex--;
					loadImage(currentImageIndex);
				}
			};

			System.Windows.Controls.Grid topPanel = new System.Windows.Controls.Grid
			{
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("BackgroundBrush") ?? System.Windows.Media.Brushes.White
			};
			topPanel.Children.Add(exitButton);
			topPanel.Children.Add(zoomPanel);

			System.Windows.Controls.Grid imageGrid = new System.Windows.Controls.Grid();
			imageGrid.Children.Add(fullImage);
			imageGrid.Children.Add(leftArrow);
			imageGrid.Children.Add(rightArrow);

			ScrollViewer scrollViewer = new ScrollViewer
			{
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = imageGrid,
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("BackgroundBrush") ?? System.Windows.Media.Brushes.White
			};

			Action updateCursor = () =>
			{
				if (baseImageWidth > 0 && baseImageHeight > 0 && scrollViewer != null)
				{
					double scaledWidth = baseImageWidth * baseFitScale * zoomLevel;
					double scaledHeight = baseImageHeight * baseFitScale * zoomLevel;
					bool canScroll = scaledWidth > scrollViewer.ViewportWidth || scaledHeight > scrollViewer.ViewportHeight;
					imageGrid.Cursor = canScroll ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
				}
			};

			updateZoomWithCursor = () =>
			{
				updateZoom();
				updateCursor();
			};

			zoomToPoint = (newZoomLevel, zoomPoint) =>
			{
				if (zoomPoint.HasValue && baseImageWidth > 0 && baseImageHeight > 0 && scrollViewer != null)
				{
					double oldZoom = zoomLevel;
					double oldScaledWidth = baseImageWidth * baseFitScale * oldZoom;
					double oldScaledHeight = baseImageHeight * baseFitScale * oldZoom;

					double relativeX = (scrollViewer.HorizontalOffset + zoomPoint.Value.X) / oldScaledWidth;
					double relativeY = (scrollViewer.VerticalOffset + zoomPoint.Value.Y) / oldScaledHeight;

					zoomLevel = newZoomLevel;
					updateZoom();
					updateCursor();

					double newScaledWidth = baseImageWidth * baseFitScale * zoomLevel;
					double newScaledHeight = baseImageHeight * baseFitScale * zoomLevel;

					double newX = (relativeX * newScaledWidth) - zoomPoint.Value.X;
					double newY = (relativeY * newScaledHeight) - zoomPoint.Value.Y;

					scrollViewer.ScrollToHorizontalOffset(Math.Max(0, newX));
					scrollViewer.ScrollToVerticalOffset(Math.Max(0, newY));
				}
				else
				{
					zoomLevel = newZoomLevel;
					updateZoomWithCursor();
				}
			};

			zoomInButton.Click += (s, args) =>
			{
				if (scrollViewer != null && zoomToPoint != null)
				{
					System.Windows.Point centerPoint = new System.Windows.Point(
						scrollViewer.ViewportWidth / 2,
						scrollViewer.ViewportHeight / 2);
					zoomToPoint(Math.Min(zoomLevel * 1.2, 10.0), centerPoint);
				}
				else
				{
					zoomLevel = Math.Min(zoomLevel * 1.2, 10.0);
					updateZoomWithCursor?.Invoke();
				}
			};

			zoomOutButton.Click += (s, args) =>
			{
				if (scrollViewer != null && zoomToPoint != null)
				{
					System.Windows.Point centerPoint = new System.Windows.Point(
						scrollViewer.ViewportWidth / 2,
						scrollViewer.ViewportHeight / 2);
					zoomToPoint(Math.Max(zoomLevel / 1.2, 0.1), centerPoint);
				}
				else
				{
					zoomLevel = Math.Max(zoomLevel / 1.2, 0.1);
					updateZoomWithCursor?.Invoke();
				}
			};

			zoomResetButton.Click += (s, args) =>
			{
				if (scrollViewer != null && zoomToPoint != null)
				{
					System.Windows.Point centerPoint = new System.Windows.Point(
						scrollViewer.ViewportWidth / 2,
						scrollViewer.ViewportHeight / 2);
					zoomToPoint(1.0, centerPoint);
				}
				else
				{
					zoomLevel = 1.0;
					updateZoomWithCursor?.Invoke();
				}
			};

			imageGrid.MouseWheel += (s, args) =>
			{
				if (zoomToPoint != null && scrollViewer != null)
				{
					System.Windows.Point mousePos = args.GetPosition(scrollViewer);
					double newZoom;
					if (args.Delta > 0)
					{
						newZoom = Math.Min(zoomLevel * 1.1, 10.0);
					}
					else
					{
						newZoom = Math.Max(zoomLevel / 1.1, 0.1);
					}
					zoomToPoint(newZoom, mousePos);
				}
				else
				{
					if (args.Delta > 0)
					{
						zoomLevel = Math.Min(zoomLevel * 1.1, 10.0);
					}
					else
					{
						zoomLevel = Math.Max(zoomLevel / 1.1, 0.1);
					}
					updateZoomWithCursor?.Invoke();
				}
				args.Handled = true;
			};

			bool isDragging = false;
			System.Windows.Point lastMousePosition = new System.Windows.Point();

			Func<bool> isImageScrollable = () =>
			{
				if (baseImageWidth > 0 && baseImageHeight > 0)
				{
					double scaledWidth = baseImageWidth * baseFitScale * zoomLevel;
					double scaledHeight = baseImageHeight * baseFitScale * zoomLevel;
					return scaledWidth > scrollViewer.ViewportWidth || scaledHeight > scrollViewer.ViewportHeight;
				}
				return false;
			};

			imageGrid.MouseDown += (s, args) =>
			{
				if (args.LeftButton == System.Windows.Input.MouseButtonState.Pressed && isImageScrollable())
				{
					isDragging = true;
					lastMousePosition = args.GetPosition(scrollViewer);
					imageGrid.CaptureMouse();
					args.Handled = true;
				}
			};

			imageGrid.MouseMove += (s, args) =>
			{
				updateCursor();

				if (isDragging && args.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
				{
					System.Windows.Point currentPosition = args.GetPosition(scrollViewer);
					double deltaX = lastMousePosition.X - currentPosition.X;
					double deltaY = lastMousePosition.Y - currentPosition.Y;

					scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + deltaX);
					scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);

					lastMousePosition = currentPosition;
					args.Handled = true;
				}
			};

			imageGrid.MouseUp += (s, args) =>
			{
				if (isDragging)
				{
					isDragging = false;
					imageGrid.ReleaseMouseCapture();
					args.Handled = true;
				}
			};

			imageGrid.MouseLeave += (s, args) =>
			{
				if (isDragging)
				{
					isDragging = false;
					imageGrid.ReleaseMouseCapture();
				}
			};

			System.Windows.Controls.Grid mainGrid = new System.Windows.Controls.Grid
			{
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("BackgroundBrush") ?? System.Windows.Media.Brushes.White
			};
			mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
			mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

			System.Windows.Controls.Grid.SetRow(topPanel, 0);
			System.Windows.Controls.Grid.SetRow(scrollViewer, 1);

			mainGrid.Children.Add(topPanel);
			mainGrid.Children.Add(scrollViewer);

			imageWindow.Content = mainGrid;
			imageWindow.Owner = System.Windows.Application.Current.MainWindow;
			imageWindow.ShowActivated = true;

			loadImage(currentIndex);

			imageWindow.Loaded += (s, e) =>
			{
				imageWindow.Activate();
				imageWindow.Focus();
			};

			imageWindow.SizeChanged += (s, e) =>
			{
				if (fullImage.Source is BitmapImage currentImage && currentImage.Width > 0 && currentImage.Height > 0)
				{
					double windowWidth = imageWindow.ActualWidth;
					double windowHeight = imageWindow.ActualHeight - topPanel.ActualHeight;
					double imageWidth = currentImage.Width;
					double imageHeight = currentImage.Height;

					double widthRatio = windowWidth / imageWidth;
					double heightRatio = windowHeight / imageHeight;
					baseFitScale = Math.Min(widthRatio, heightRatio);

					if (baseFitScale > 1.0)
					{
						baseFitScale = 1.0;
					}

					baseImageWidth = imageWidth;
					baseImageHeight = imageHeight;
					updateZoomWithCursor();
				}
			};

			imageWindow.Show();
		}
	}

	public class InverseBooleanToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool boolValue)
			{
				return !boolValue ? Visibility.Visible : Visibility.Collapsed;
			}
			return Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is Visibility visibility)
			{
				return visibility != Visibility.Visible;
			}
			return false;
		}
	}
}

