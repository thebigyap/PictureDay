using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PictureDay.Models;
using PictureDay.Services;
using UserControl = System.Windows.Controls.UserControl;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace PictureDay.Views
{
    public partial class PhotoGalleryView : UserControl
    {
        private const int PageSize = 24;
        private readonly GalleryThumbnailCache _thumbnailCache = new();
        private StorageManager? _storageManager;
        private StorageSnapshot? _snapshot;
        private CancellationTokenSource? _refreshCancellation;
        private CancellationTokenSource? _thumbnailCancellation;
        private List<ScreenshotMetadata> _visiblePhotos = new();
        private int _currentYear = DateTime.Today.Year;
        private int _currentMonth = DateTime.Today.Month;
        private int _pageIndex;
        private bool _updatingControls;
        private bool _refreshing;
        private bool _needsResume;
        private DateTime? _selectedDay;

        public PhotoGalleryView()
        {
            InitializeComponent();
            PreviewKeyDown += async (_, e) =>
            {
                if (e.Key != Key.F5) return;
                e.Handled = true;
                await RefreshAsync();
            };
            Unloaded += (_, _) =>
            {
                _needsResume = true;
                _refreshCancellation?.Cancel();
                _thumbnailCancellation?.Cancel();
            };
            Loaded += async (_, _) =>
            {
                if (_needsResume && _storageManager != null)
                {
                    _needsResume = false;
                    await RefreshAsync();
                }
            };
        }

        public void Initialize(StorageManager storageManager)
        {
            if (!ReferenceEquals(_storageManager, storageManager)) _snapshot = null;
            _storageManager = storageManager;
            _ = RefreshAsync();
        }

        /// <summary>Refresh once after library changes; month, sort and page navigation use this snapshot.</summary>
        public async Task RefreshAsync()
        {
            if (_storageManager == null) return;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            var cancellation = _refreshCancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var storage = _storageManager;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _thumbnailCancellation = null;
            _refreshing = true;
            RefreshButton.IsEnabled = false;
            LoadingProgress.Visibility = Visibility.Visible;
            GalleryStatusText.Text = "Reading your photo library…";
            SetState("Loading your library", "Your photos and activity will appear here in a moment.", _snapshot == null);
            try
            {
                var snapshot = await Task.Run(storage.GetSnapshot, token);
                token.ThrowIfCancellationRequested();
                var firstLoad = _snapshot == null;
                _snapshot = snapshot;
                if (firstLoad && snapshot.Screenshots.Count > 0)
                {
                    var latest = snapshot.Screenshots.Max(photo => photo.DateTaken);
                    _currentYear = latest.Year;
                    _currentMonth = latest.Month;
                }
                UpdateDateControls();
                UpdateStats(snapshot);
                _refreshing = false;
                await LoadPhotosAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading photo library: {ex}");
                GalleryStatusText.Text = "The photo library could not be refreshed.";
                SetState("We couldn't read this folder", "Check that your photo folder is available, then choose Refresh.", true);
                StateActionButton.Visibility = Visibility.Collapsed;
            }
            finally
            {
                if (ReferenceEquals(_refreshCancellation, cancellation))
                {
                    _refreshing = false;
                    RefreshButton.IsEnabled = true;
                    LoadingProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private DateTime CurrentMonth => new(_currentYear, _currentMonth, 1);
        private DateTime EarliestMonth
        {
            get
            {
                var earliest = _snapshot?.Screenshots.MinBy(photo => photo.DateTaken)?.DateTaken ?? DateTime.Today;
                if (earliest > DateTime.Today) earliest = DateTime.Today;
                return new DateTime(earliest.Year, earliest.Month, 1);
            }
        }
        private DateTime LatestMonth
        {
            get
            {
                var latest = _snapshot?.Screenshots.MaxBy(photo => photo.DateTaken)?.DateTaken ?? DateTime.Today;
                if (latest < DateTime.Today) latest = DateTime.Today;
                return new DateTime(latest.Year, latest.Month, 1);
            }
        }

        private void UpdateDateControls()
        {
            _updatingControls = true;
            try
            {
                var current = CurrentMonth;
                if (current < EarliestMonth) current = EarliestMonth;
                if (current > LatestMonth) current = LatestMonth;
                _currentYear = current.Year;
                _currentMonth = current.Month;
                YearComboBox.ItemsSource = Enumerable.Range(EarliestMonth.Year, LatestMonth.Year - EarliestMonth.Year + 1).Reverse().ToList();
                YearComboBox.SelectedItem = _currentYear;
                var minMonth = _currentYear == EarliestMonth.Year ? EarliestMonth.Month : 1;
                var maxMonth = _currentYear == LatestMonth.Year ? LatestMonth.Month : 12;
                MonthComboBox.ItemsSource = Enumerable.Range(minMonth, maxMonth - minMonth + 1)
                    .Select(month => new MonthOption(month, CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month))).ToList();
                MonthComboBox.SelectedValue = _currentMonth;
                PrevMonthButton.IsEnabled = CurrentMonth > EarliestMonth;
                NextMonthButton.IsEnabled = CurrentMonth < LatestMonth;
                LatestButton.IsEnabled = _snapshot?.Screenshots.Count > 0;
            }
            finally { _updatingControls = false; }
        }

        private async Task LoadPhotosAsync()
        {
            if (_snapshot == null || _refreshing) return;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            var cancellation = _thumbnailCancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var photos = _snapshot.Screenshots.Where(photo => photo.DateTaken.Year == _currentYear && photo.DateTaken.Month == _currentMonth);
            if (_selectedDay.HasValue) photos = photos.Where(photo => photo.DateTaken.Date == _selectedDay.Value);
            if (CaptureFilterComboBox.SelectedIndex == 1) photos = photos.Where(IsManual);
            if (CaptureFilterComboBox.SelectedIndex == 2) photos = photos.Where(photo => !IsManual(photo));
            _visiblePhotos = (SortComboBox.SelectedIndex == 1 ? photos.OrderBy(photo => photo.DateTaken) : photos.OrderByDescending(photo => photo.DateTaken)).ToList();
            int pages = Math.Max(1, (_visiblePhotos.Count + PageSize - 1) / PageSize);
            _pageIndex = Math.Clamp(_pageIndex, 0, pages - 1);
            var items = _visiblePhotos.Skip(_pageIndex * PageSize).Take(PageSize).Select(photo => new GalleryPhotoItem(photo)).ToList();
            PhotosItemsControl.ItemsSource = items;
            PhotosScrollViewer.ScrollToTop();
            PageLabel.Text = $"Page {_pageIndex + 1} of {pages}";
            PreviousPageButton.IsEnabled = _pageIndex > 0;
            NextPageButton.IsEnabled = _pageIndex + 1 < pages;
            PageControls.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            var period = _selectedDay?.ToString("D", CultureInfo.CurrentCulture) ?? CurrentMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            MonthSummaryText.Text = $"{_visiblePhotos.Count:N0} {(_visiblePhotos.Count == 1 ? "photo" : "photos")} · {period}";
            ClearDayButton.Visibility = _selectedDay.HasValue ? Visibility.Visible : Visibility.Collapsed;
            var emptyLibrary = _snapshot.Screenshots.Count == 0;
            SetState(emptyLibrary ? "Your story starts with one photo" : "No photos in this view",
                emptyLibrary ? "Use Take Screenshot to save your first moment."
                : "Try another month or capture type, or jump to your latest photo.", items.Count == 0);
            StateActionButton.Visibility = !emptyLibrary && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GalleryStatusText.Text = items.Count > 0 ? "Loading previews…" : "Photos stay in the folder you choose in Settings.";
            LoadingProgress.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            int failed = 0;
            try
            {
                await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, async (item, itemToken) =>
                {
                    System.Windows.Media.Imaging.BitmapImage? thumbnail = null;
                    try { thumbnail = _thumbnailCache.Get(item.FilePath); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref failed);
                        Debug.WriteLine($"Error loading preview {item.FilePath}: {ex.Message}");
                    }
                    itemToken.ThrowIfCancellationRequested();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested) item.SetPreview(thumbnail);
                    });
                });
                token.ThrowIfCancellationRequested();
                GalleryStatusText.Text = failed > 0 ? $"{failed} preview{(failed == 1 ? " is" : "s are")} unavailable. Other photos are ready to open."
                    : items.Count > 0 ? "Open a photo to explore · Arrow keys browse · Esc closes the viewer" : "Photos stay in the folder you choose in Settings.";
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_thumbnailCancellation, cancellation)) LoadingProgress.Visibility = Visibility.Collapsed;
            }
        }

        private static bool IsManual(ScreenshotMetadata photo) => Path.GetFileName(photo.FilePath).StartsWith("u_", StringComparison.OrdinalIgnoreCase);

        private void SetState(string title, string description, bool visible)
        {
            StateTitle.Text = title;
            StateDescription.Text = description;
            GalleryState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PhotosScrollViewer.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateStats(StorageSnapshot snapshot)
        {
            TotalPhotosValue.Text = snapshot.Screenshots.Count.ToString("N0", CultureInfo.CurrentCulture);
            var (value, unit) = FormatBytes(snapshot.TotalStorageBytes);
            StorageUsedValue.Text = value;
            StorageUsedUnit.Text = " " + unit;
            LongestStreakValue.Text = snapshot.LongestStreak.ToString(CultureInfo.CurrentCulture);
            LongestStreakUnit.Text = snapshot.LongestStreak == 1 ? " day" : " days";
            CurrentStreakValue.Text = snapshot.CurrentStreak.ToString(CultureInfo.CurrentCulture);
            CurrentStreakUnit.Text = snapshot.CurrentStreak == 1 ? " day" : " days";
            BuildHeatmap(snapshot.Screenshots);
        }

        private static (string Value, string Unit) FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int order = 0;
            while (value >= 1024 && order < units.Length - 1) { value /= 1024; order++; }
            return (value.ToString(order == 0 || value >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture), units[order]);
        }
		#region Photo activity heatmap

		private const int HeatmapWeeks = 53;
		private const double HeatmapCellSize = 10;
		private const double HeatmapCellGap = 3;
		private const double HeatmapColumnPitch = HeatmapCellSize + HeatmapCellGap;
		private const double HeatmapDayLabelWidth = 26;
		private const double HeatmapMonthLabelHeight = 14;

		/// <summary>
		/// Draws a GitHub-style contribution grid over a fixed, rolling 53-week
		/// window ending with the current week. Not configurable by design.
		/// </summary>
		private void BuildHeatmap(IReadOnlyList<ScreenshotMetadata> allScreenshots)
		{
			HeatmapCanvas.Children.Clear();

			DateTime today = DateTime.Now.Date;
			// Back up to the Sunday of this week, then 52 more weeks, so every
			// column is a full Sunday-to-Saturday week.
			DateTime firstDay = today.AddDays(-(int)today.DayOfWeek).AddDays(-7 * (HeatmapWeeks - 1));

			var countsByDay = new Dictionary<DateTime, int>();
			foreach (var screenshot in allScreenshots)
			{
				DateTime day = screenshot.DateTaken.Date;
				if (day < firstDay || day > today) continue;
				countsByDay[day] = countsByDay.TryGetValue(day, out int seen) ? seen + 1 : 1;
			}

			HeatmapCanvas.Width = HeatmapDayLabelWidth + HeatmapWeeks * HeatmapColumnPitch - HeatmapCellGap;
			HeatmapCanvas.Height = HeatmapMonthLabelHeight + 7 * HeatmapColumnPitch - HeatmapCellGap;

			AddHeatmapWeekdayLabels();
			AddHeatmapMonthLabels(firstDay);

			for (int week = 0; week < HeatmapWeeks; week++)
			{
				for (int dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++)
				{
					DateTime date = firstDay.AddDays(week * 7 + dayOfWeek);
					if (date > today) continue;

					countsByDay.TryGetValue(date, out int count);

					var cell = new Border
					{
						Width = HeatmapCellSize,
						Height = HeatmapCellSize,
						CornerRadius = new CornerRadius(2),
						ToolTip = DescribeHeatmapDay(date, count)
					};
					// Resource reference (not a resolved brush) so the cells
					// follow a light/dark theme switch at runtime.
					cell.SetResourceReference(Border.BackgroundProperty,
						$"HeatmapLevel{Math.Min(count, 4)}Brush");

					Canvas.SetLeft(cell, HeatmapDayLabelWidth + week * HeatmapColumnPitch);
					Canvas.SetTop(cell, HeatmapMonthLabelHeight + dayOfWeek * HeatmapColumnPitch);
					HeatmapCanvas.Children.Add(cell);
				}
			}

			int photosInWindow = countsByDay.Values.Sum();
			HeatmapSummaryText.Text = $"{photosInWindow} photo{(photosInWindow != 1 ? "s" : "")} in the last year";
		}

		private void AddHeatmapWeekdayLabels()
		{
			string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;

			foreach (int dayOfWeek in new[] { 1, 3, 5 })
			{
				var label = CreateHeatmapLabel(dayNames[dayOfWeek]);
				Canvas.SetLeft(label, 0);
				Canvas.SetTop(label, HeatmapMonthLabelHeight + dayOfWeek * HeatmapColumnPitch - 1);
				HeatmapCanvas.Children.Add(label);
			}
		}

		private void AddHeatmapMonthLabels(DateTime firstDay)
		{
			int lastLabeledMonth = -1;
			int lastLabeledWeek = -3;

			for (int week = 0; week < HeatmapWeeks; week++)
			{
				DateTime columnStart = firstDay.AddDays(week * 7);
				if (columnStart.Month == lastLabeledMonth) continue;
				// Leave room between abbreviations so short months don't collide.
				if (week - lastLabeledWeek < 3) continue;

				var label = CreateHeatmapLabel(columnStart.ToString("MMM", CultureInfo.CurrentCulture));
				Canvas.SetLeft(label, HeatmapDayLabelWidth + week * HeatmapColumnPitch);
				Canvas.SetTop(label, 0);
				HeatmapCanvas.Children.Add(label);

				lastLabeledMonth = columnStart.Month;
				lastLabeledWeek = week;
			}
		}

		private TextBlock CreateHeatmapLabel(string text)
		{
			var label = new TextBlock { Text = text, FontSize = 9 };
			label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
			return label;
		}

		private static string DescribeHeatmapDay(DateTime date, int count)
		{
			string dateLabel = date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
			return count == 0
				? $"No photo on {dateLabel}"
				: $"{count} photo{(count != 1 ? "s" : "")} on {dateLabel}";
		}

		#endregion
        private async Task NavigateToAsync(DateTime date, DateTime? day = null)
        {
            _currentYear = date.Year;
            _currentMonth = date.Month;
            _selectedDay = day;
            _pageIndex = 0;
            UpdateDateControls();
            await LoadPhotosAsync();
        }

        private async void PrevMonthButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentMonth > EarliestMonth) await NavigateToAsync(CurrentMonth.AddMonths(-1));
        }
        private async void NextMonthButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentMonth < LatestMonth) await NavigateToAsync(CurrentMonth.AddMonths(1));
        }
        private async void TodayButton_Click(object sender, RoutedEventArgs e) => await NavigateToAsync(DateTime.Today, DateTime.Today);
        private async void LatestButton_Click(object sender, RoutedEventArgs e)
        {
            if (_snapshot?.Screenshots.MaxBy(photo => photo.DateTaken) is { } latest)
            {
                _updatingControls = true;
                CaptureFilterComboBox.SelectedIndex = 0;
                SortComboBox.SelectedIndex = 0;
                _updatingControls = false;
                await NavigateToAsync(latest.DateTaken);
            }
        }
        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
        private async void ClearDayButton_Click(object sender, RoutedEventArgs e) => await NavigateToAsync(CurrentMonth);
        private async void MonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingControls && MonthComboBox.SelectedValue is int month)
                await NavigateToAsync(new DateTime(_currentYear, month, 1));
        }
        private async void YearComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingControls && YearComboBox.SelectedItem is int year)
                await NavigateToAsync(new DateTime(year, _currentMonth, 1));
        }
        private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingControls || _snapshot == null) return;
            _pageIndex = 0;
            await LoadPhotosAsync();
        }
        private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pageIndex > 0) { _pageIndex--; await LoadPhotosAsync(); }
        }
        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if ((_pageIndex + 1) * PageSize < _visiblePhotos.Count) { _pageIndex++; await LoadPhotosAsync(); }
        }
        private void PhotoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: GalleryPhotoItem photo }) OpenImageInViewer(photo.FilePath);
        }
        private static string? GetFilePathFromContext(object sender) => sender is MenuItem { Parent: ContextMenu menu }
            && menu.PlacementTarget is FrameworkElement { DataContext: GalleryPhotoItem photo } ? photo.FilePath : null;

        private void ContextMenu_Open_Click(object sender, RoutedEventArgs e)
        {
            if (GetFilePathFromContext(sender) is { } path) OpenImageInViewer(path);
        }
        private void ContextMenu_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (GetFilePathFromContext(sender) is not { } path) return;
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { ShowActionError("open this photo in File Explorer", ex); }
        }
        private async void ContextMenu_CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (GetFilePathFromContext(sender) is not { } path) return;
            try
            {
                var image = await Task.Run(() => GalleryImageLoader.Load(path));
                Clipboard.SetImage(image);
                GalleryStatusText.Text = "Photo copied to the clipboard.";
            }
            catch (Exception ex) { ShowActionError("copy this photo", ex); }
        }
        private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (GetFilePathFromContext(sender) is not { } path) return;
            try { Clipboard.SetText(path); GalleryStatusText.Text = "File location copied to the clipboard."; }
            catch (Exception ex) { ShowActionError("copy this file location", ex); }
        }
        private async void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (GetFilePathFromContext(sender) is not { } path) return;
            if (MessageBox.Show($"Permanently delete this screenshot?\n\n{Path.GetFileName(path)}\n\nThis cannot be undone.", "Delete screenshot",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { File.Delete(path); await RefreshAsync(); }
            catch (Exception ex) { ShowActionError("delete this photo", ex); }
        }
        private static void ShowActionError(string action, Exception ex) => MessageBox.Show($"PictureDay couldn't {action}.\n\n{ex.Message}", "Photo unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        private void OpenImageInViewer(string filePath)
        {
            var index = _visiblePhotos.FindIndex(photo => string.Equals(photo.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var viewer = new PhotoViewerWindow(_visiblePhotos.ToList(), index) { Owner = Window.GetWindow(this) };
            viewer.Show();
        }
        private sealed record MonthOption(int MonthNumber, string MonthName);
    }
}
