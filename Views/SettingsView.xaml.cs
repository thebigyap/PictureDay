using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PictureDay.Models;
using PictureDay.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using ListBox = System.Windows.Controls.ListBox;
using Button = System.Windows.Controls.Button;

namespace PictureDay.Views
{
	public partial class SettingsView : UserControl
	{
		private ConfigManager? _configManager;
		private StorageManager? _storageManager;
		private List<string> _tempBlockedApps = new List<string>();
		private DispatcherTimer? _dayCheckTimer;
		private DateTime? _lastCheckedDate;
		private TimeSpan? _lastCheckedScheduledTime;

		public event EventHandler? SettingsSaved;
		public event EventHandler? RequestNavigateHome;

		private bool _isInitialized = false;
		private bool _loading = false;
		private bool _dirty = false;

		/// <summary>True when the form has edits that have not been saved.</summary>
		public bool HasUnsavedChanges => _dirty;

		private void MarkDirty()
		{
			if (_isInitialized && !_loading)
			{
				_dirty = true;
			}
		}

		public SettingsView()
		{
			InitializeComponent();
			_isInitialized = true;
			SetupDayCheckTimer();
			Unloaded += SettingsView_Unloaded;
			Loaded += (s, e) => _dayCheckTimer?.Start();

			// Track edits on controls that have no dedicated handler
			StartWithWindowsCheckBox.Click += (s, e) => MarkDirty();
			DirectoryTextBox.TextChanged += (s, e) => MarkDirty();
			FixedTimeTextBox.TextChanged += (s, e) => MarkDirty();
			RangeStartTimeTextBox.TextChanged += (s, e) => MarkDirty();
			RangeEndTimeTextBox.TextChanged += (s, e) => MarkDirty();
		}

		private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
		{
			_dayCheckTimer?.Stop();
		}

		public void Initialize(ConfigManager configManager)
		{
			_configManager = configManager;
			_storageManager = (StorageManager?)System.Windows.Application.Current.Resources["StorageManager"];
			LoadSettings();
			_lastCheckedDate = _configManager?.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
			_lastCheckedScheduledTime = _configManager?.Config.TodayScheduledTime;

			var dailyScheduler = (DailyScheduler?)System.Windows.Application.Current.Resources["DailyScheduler"];
			if (dailyScheduler != null)
			{
				dailyScheduler.PhotosProcessed += (s, e) =>
				{
					System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
					{
						UpdateScheduledTimeDisplay();
						_lastCheckedDate = _configManager?.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
						_lastCheckedScheduledTime = _configManager?.Config.TodayScheduledTime;
					}));
				};

				dailyScheduler.ScheduledTimeChanged += (s, e) =>
				{
					System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
					{
						UpdateScheduledTimeDisplay();
						_lastCheckedDate = _configManager?.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
						_lastCheckedScheduledTime = _configManager?.Config.TodayScheduledTime;
					}));
				};
			}
		}

		private void SetupDayCheckTimer()
		{
			_dayCheckTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMinutes(1)
			};
			_dayCheckTimer.Tick += DayCheckTimer_Tick;
			_dayCheckTimer.Start();
		}

		private void DayCheckTimer_Tick(object? sender, EventArgs e)
		{
			if (_configManager == null)
			{
				return;
			}

			DateTime? scheduledDate = _configManager.Config.ScheduledTimeDate?.Date;
			TimeSpan? scheduledTime = _configManager.Config.TodayScheduledTime;

			if (scheduledDate != _lastCheckedDate || scheduledTime != _lastCheckedScheduledTime)
			{
				_lastCheckedDate = scheduledDate;
				_lastCheckedScheduledTime = scheduledTime;
				UpdateScheduledTimeDisplay();
			}
		}

		private void LoadSettings()
		{
			if (_configManager == null) return;

			_loading = true;
			try
			{
				LoadSettingsCore();
			}
			finally
			{
				_loading = false;
				_dirty = false;
			}
		}

		private void LoadSettingsCore()
		{
			if (_configManager == null) return;

			_tempBlockedApps = new List<string>(_configManager.Config.BlockedApplications);
			RefreshBlockedAppsList();

			StartWithWindowsCheckBox.IsChecked = _configManager.Config.StartWithWindows;
			DirectoryTextBox.Text = _configManager.Config.ScreenshotDirectory;

			QualitySlider.Value = _configManager.Config.Quality;
			QualityValueTextBlock.Text = _configManager.Config.Quality.ToString();

			foreach (ComboBoxItem item in FormatComboBox.Items)
			{
				if (item.Tag?.ToString() == _configManager.Config.ImageFormat)
				{
					FormatComboBox.SelectedItem = item;
					break;
				}
			}

			string theme = _configManager.Config.Theme ?? "Light";
			foreach (ComboBoxItem item in ThemeComboBox.Items)
			{
				if (item.Tag?.ToString() == theme)
				{
					ThemeComboBox.SelectedItem = item;
					break;
				}
			}

			UpdateQualityUI();

			ScheduleModeComboBox.SelectedIndex = (int)_configManager.Config.ScheduleMode;
			UpdateScheduleUI();

			if (_configManager.Config.FixedScheduledTime.HasValue)
			{
				FixedTimeTextBox.Text = _configManager.Config.FixedScheduledTime.Value.ToString(@"hh\:mm");
			}

			if (_configManager.Config.ScheduleRangeStart.HasValue)
			{
				RangeStartTimeTextBox.Text = _configManager.Config.ScheduleRangeStart.Value.ToString(@"hh\:mm");
			}

			if (_configManager.Config.ScheduleRangeEnd.HasValue)
			{
				RangeEndTimeTextBox.Text = _configManager.Config.ScheduleRangeEnd.Value.ToString(@"hh\:mm");
			}

			LoadMonitorSettings();
			DesktopOnlyCheckBox.IsChecked = _configManager.Config.DesktopOnly;
			UpdateScheduledTimeDisplay();
			_lastCheckedDate = _configManager.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
			_lastCheckedScheduledTime = _configManager.Config.TodayScheduledTime;
		}

		private void LoadMonitorSettings()
		{
			Screen[] screens = System.Windows.Forms.Screen.AllScreens;
			MonitorComboBox.Items.Clear();

			foreach (Screen screen in screens)
			{
				string displayName = $"Monitor {MonitorComboBox.Items.Count + 1} ({screen.Bounds.Width}x{screen.Bounds.Height})";
				MonitorComboBox.Items.Add(displayName);
			}

			if (_configManager != null)
			{
				if (_configManager.Config.CaptureAllMonitors)
				{
					AllMonitorsRadio.IsChecked = true;
					MonitorComboBox.IsEnabled = false;
				}
				else if (_configManager.Config.SelectedMonitorIndex >= 0 &&
						 _configManager.Config.SelectedMonitorIndex < MonitorComboBox.Items.Count)
				{
					SpecificMonitorRadio.IsChecked = true;
					MonitorComboBox.SelectedIndex = _configManager.Config.SelectedMonitorIndex;
					MonitorComboBox.IsEnabled = true;
				}
				else
				{
					PrimaryMonitorRadio.IsChecked = true;
					MonitorComboBox.IsEnabled = false;
				}
			}
		}

		private void MonitorSelection_Changed(object sender, RoutedEventArgs e)
		{
			if (SpecificMonitorRadio.IsChecked == true)
			{
				MonitorComboBox.IsEnabled = true;
				if (MonitorComboBox.SelectedIndex < 0 && MonitorComboBox.Items.Count > 0)
				{
					MonitorComboBox.SelectedIndex = 0;
				}
			}
			else
			{
				MonitorComboBox.IsEnabled = false;
			}
			MarkDirty();
		}

		private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			MarkDirty();
		}

		private void DesktopOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
		{
			MarkDirty();
		}

		private void ScheduleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			UpdateScheduleUI();
			MarkDirty();
		}

		private void UpdateScheduleUI()
		{
			if (ScheduleModeComboBox.SelectedItem is ComboBoxItem item)
			{
				string? mode = item.Tag?.ToString();
				FixedTimePanel.Visibility = mode == "FixedTime" ? Visibility.Visible : Visibility.Collapsed;
				TimeRangePanel.Visibility = mode == "TimeRange" ? Visibility.Visible : Visibility.Collapsed;
			}
			UpdateScheduledTimeDisplay();
		}

		private void UpdateScheduledTimeDisplay()
		{
			if (_configManager == null || ScheduledTimeTextBlock == null)
			{
				return;
			}

			if (_configManager.Config.TodayScheduledTime.HasValue)
			{
				TimeSpan scheduledTime = _configManager.Config.TodayScheduledTime.Value;
				string timeString = FormatTimeSpan(scheduledTime);

				DateTime scheduledDate = _configManager.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
				bool hasPassed = HasScheduledTimePassedForDisplay(scheduledTime);

				string prefix = scheduledDate == DateTime.Today
					? "Today's scheduled time"
					: $"Scheduled time for {scheduledDate:yyyy-MM-dd}";

				if (hasPassed)
				{
					ScheduledTimeTextBlock.Text = $"{prefix}: {timeString} (already passed - will schedule for tomorrow)";
				}
				else
				{
					ScheduledTimeTextBlock.Text = $"{prefix}: {timeString}";
				}
			}
			else
			{
				ScheduledTimeTextBlock.Text = "Today's scheduled time: Not set";
			}
		}

		private string FormatTimeSpan(TimeSpan timeSpan)
		{
			int totalHours = (int)timeSpan.TotalHours;
			int hours = totalHours % 24;
			int minutes = timeSpan.Minutes;
			string period = hours >= 12 ? "PM" : "AM";

			if (hours == 0)
			{
				hours = 12;
			}
			else if (hours > 12)
			{
				hours -= 12;
			}

			return $"{hours}:{minutes:D2} {period}";
		}

		private bool HasScheduledTimePassedForDisplay(TimeSpan scheduledTime)
		{
			if (_configManager == null)
			{
				return false;
			}

			DateTime scheduledDate = _configManager.Config.ScheduledTimeDate?.Date ?? DateTime.Today;
			DateTime scheduledDateTime = scheduledDate + scheduledTime;

			// Small grace window after the scheduled time before we consider it "passed"
			DateTime windowEnd = scheduledDateTime.AddMinutes(2.5);

			return DateTime.Now > windowEnd;
		}

		private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (!_isInitialized || QualityValueTextBlock == null)
			{
				return;
			}
			QualityValueTextBlock.Text = ((int)e.NewValue).ToString();
			MarkDirty();
		}

		private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!_isInitialized)
			{
				return;
			}
			UpdateQualityUI();
			MarkDirty();
		}

		private void UpdateQualityUI()
		{
			if (QualitySlider == null || FormatComboBox == null)
			{
				return;
			}

			if (FormatComboBox.SelectedItem is ComboBoxItem item)
			{
				bool isJpeg = item.Tag?.ToString() == "JPEG";
				QualitySlider.IsEnabled = isJpeg;
			}
			else
			{
				QualitySlider.IsEnabled = true;
			}
		}

		private void RefreshBlockedAppsList()
		{
			BlockedAppsListBox.Items.Clear();
			foreach (string app in _tempBlockedApps)
			{
				BlockedAppsListBox.Items.Add(app);
			}
		}

		private void AddAppButton_Click(object sender, RoutedEventArgs e)
		{
			string appName = NewAppTextBox.Text.Trim();
			if (string.IsNullOrEmpty(appName))
			{
				MessageBox.Show("Please enter an application name.", "Invalid Input",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				appName = appName + ".exe";
			}

			if (!_tempBlockedApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
			{
				_tempBlockedApps.Add(appName);
				RefreshBlockedAppsList();
				NewAppTextBox.Clear();
				MarkDirty();
			}
			else
			{
				MessageBox.Show("This application is already in the list.", "Duplicate Entry",
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
		{
			if (BlockedAppsListBox.SelectedItem is string selectedApp)
			{
				_tempBlockedApps.Remove(selectedApp);
				RefreshBlockedAppsList();
				MarkDirty();
			}
		}

		private void BrowseProcessesButton_Click(object sender, RoutedEventArgs e)
		{
			Window processWindow = new Window
			{
				Title = "Select Running Process",
				Width = 400,
				Height = 500,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BackgroundBrush"]
			};

			ListBox processListBox = new ListBox
			{
				Margin = new Thickness(10)
			};

			try
			{
				var processes = Process.GetProcesses()
					.Where(p => !string.IsNullOrEmpty(p.ProcessName))
					.Select(p => p.ProcessName + ".exe")
					.Distinct()
					.OrderBy(p => p)
					.ToList();

				foreach (string process in processes)
				{
					processListBox.Items.Add(process);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error loading processes: {ex.Message}", "Error",
					MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			ScrollViewer scrollViewer = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Content = processListBox,
				Margin = new Thickness(10, 10, 10, 0),
				MaxHeight = 400
			};

			Button selectButton = new Button
			{
				Content = "Select",
				Padding = new Thickness(15, 5, 15, 5),
				Margin = new Thickness(10),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			};

			selectButton.Click += (s, args) =>
			{
				if (processListBox.SelectedItem is string selectedProcess)
				{
					if (!_tempBlockedApps.Contains(selectedProcess, StringComparer.OrdinalIgnoreCase))
					{
						_tempBlockedApps.Add(selectedProcess);
						RefreshBlockedAppsList();
						MarkDirty();
					}
					processWindow.Close();
				}
			};

			StackPanel panel = new StackPanel();
			panel.Children.Add(scrollViewer);
			panel.Children.Add(selectButton);

			processWindow.Content = panel;
			processWindow.ShowDialog();
		}

		private void BrowseDirectoryButton_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new System.Windows.Forms.FolderBrowserDialog
			{
				SelectedPath = DirectoryTextBox.Text
			};

			if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				DirectoryTextBox.Text = dialog.SelectedPath;
			}
		}

		private void SaveButton_Click(object sender, RoutedEventArgs e)
		{
			SaveSettings();
		}

		/// <summary>
		/// Persists the current form. Returns false (and leaves the user on Settings)
		/// if validation fails. On success raises <see cref="SettingsSaved"/>.
		/// </summary>
        public bool SaveSettings()
        {
            if (_configManager == null) return false;
            try
            {
                // Build and validate a separate draft so failed validation cannot change capture behavior.
                var current = _configManager.Config;
                var draft = Newtonsoft.Json.JsonConvert.DeserializeObject<AppConfig>(
                    Newtonsoft.Json.JsonConvert.SerializeObject(current))!;
                draft.BlockedApplications = new List<string>(_tempBlockedApps);
                draft.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
                draft.Quality = (int)QualitySlider.Value;
                draft.ImageFormat = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "JPEG";
                draft.Theme = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Light";
                draft.CaptureAllMonitors = AllMonitorsRadio.IsChecked == true;
                draft.SelectedMonitorIndex = SpecificMonitorRadio.IsChecked == true ? MonitorComboBox.SelectedIndex : -1;
                draft.DesktopOnly = DesktopOnlyCheckBox.IsChecked == true;
                string mode = (ScheduleModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Random";
                draft.ScheduleMode = Enum.Parse<ScheduleMode>(mode);
                if (draft.ScheduleMode == ScheduleMode.FixedTime)
                {
                    if (!PictureDay.Utils.ScheduleTimeParser.TryParse(FixedTimeTextBox.Text, out var time))
                        throw new ArgumentException("Enter a time from 00:00 to 23:59, for example 14:00.");
                    draft.FixedScheduledTime = time;
                }
                else if (draft.ScheduleMode == ScheduleMode.TimeRange)
                {
                    if (!PictureDay.Utils.ScheduleTimeParser.TryParse(RangeStartTimeTextBox.Text, out var start) ||
                        !PictureDay.Utils.ScheduleTimeParser.TryParse(RangeEndTimeTextBox.Text, out var end))
                        throw new ArgumentException("Enter start and end times from 00:00 to 23:59.");
                    if (start >= end) throw new ArgumentException("The start time must be before the end time on the same day.");
                    draft.ScheduleRangeStart = start;
                    draft.ScheduleRangeEnd = end;
                }
                string directory = DirectoryTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
                    throw new ArgumentException("Choose a full path for your photo folder, such as C:\\Pictures\\PictureDay.");
                draft.ScreenshotDirectory = Path.GetFullPath(directory);
                Directory.CreateDirectory(draft.ScreenshotDirectory);
                // Verify permission before persisting a destination the capture service cannot use.
                using (var probe = new FileStream(Path.Combine(draft.ScreenshotDirectory, $".pictureday-{Guid.NewGuid():N}.tmp"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                bool scheduleChanged = current.ScheduleMode != draft.ScheduleMode ||
                    current.FixedScheduledTime != draft.FixedScheduledTime || current.ScheduleRangeStart != draft.ScheduleRangeStart ||
                    current.ScheduleRangeEnd != draft.ScheduleRangeEnd ||
                    !string.Equals(current.ScreenshotDirectory, draft.ScreenshotDirectory, StringComparison.OrdinalIgnoreCase);
                if (scheduleChanged) { draft.TodayScheduledTime = null; draft.ScheduledTimeDate = null; }
                _configManager.SaveConfig(draft, preserveSchedule: !scheduleChanged);
                _storageManager?.UpdateSettings(draft.Quality, draft.ImageFormat, draft.ScreenshotDirectory);
                if (scheduleChanged && System.Windows.Application.Current.Resources["DailyScheduler"] is DailyScheduler scheduler)
                    scheduler.RequestScheduleRefresh();
                ApplyTheme(draft.Theme);
                UpdateScheduledTimeDisplay();
                _dirty = false;
                SettingsSaved?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Settings could not be saved.\n\n{ex.Message}", "Check your settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
		/// <summary>Reverts the form to the last saved configuration (including theme).</summary>
		public void DiscardChanges()
		{
			LoadSettings();
		}

		/// <summary>Asks the user whether to save before leaving Settings.</summary>
		public MessageBoxResult PromptUnsavedChanges()
		{
			return MessageBox.Show(
				"You have unsaved changes. Do you want to save them before leaving?",
				"Unsaved Changes",
				MessageBoxButton.YesNoCancel,
				MessageBoxImage.Warning);
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e)
		{
			DiscardChanges();
			RequestNavigateHome?.Invoke(this, EventArgs.Empty);
		}

		private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!_isInitialized || _configManager == null) return;

			if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem)
			{
				string? theme = themeItem.Tag?.ToString() ?? "Light";
				// Only apply theme visually, don't save yet - wait for Save button
				ApplyTheme(theme);
				MarkDirty();
			}
		}

		private void ApplyTheme(string theme)
		{
			if (System.Windows.Application.Current is App app)
			{
				app.ApplyTheme(theme);
			}
		}
	}
}
