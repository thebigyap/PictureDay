using System;
using System.Windows;
using PictureDay.Services;

namespace PictureDay.Views
{
	public partial class UpdateWindow : Window
	{
		private UpdateService? _updateService;
		private string? _downloadUrl;

		public UpdateWindow()
		{
			InitializeComponent();
		}

		public void Initialize(UpdateService updateService)
		{
			_updateService = updateService;
			_updateService.UpdateCheckCompleted += UpdateService_UpdateCheckCompleted;
			_updateService.DownloadProgress += UpdateService_DownloadProgress;
			_updateService.UpdateError += UpdateService_UpdateError;
		}

		private void UpdateService_UpdateCheckCompleted(object? sender, UpdateCheckResult e)
		{
			Dispatcher.Invoke(() =>
			{
				if (e.HasUpdate)
				{
					TitleTextBlock.Text = "Update Available";
					MessageTextBlock.Text = $"A new version of PictureDay is available!\n\n" +
										  $"Current version: {App.Version}\n" +
										  $"Latest version: {e.LatestVersion}\n\n" +
										  $"Would you like to download and install it now?";
					_downloadUrl = e.DownloadUrl;
					YesButton.Visibility = Visibility.Visible;
					NoButton.Visibility = Visibility.Visible;
					CloseButton.Visibility = Visibility.Collapsed;
				}
				else
				{
					TitleTextBlock.Text = "No Updates Available";
					MessageTextBlock.Text = $"You are running the latest version of PictureDay (v{App.Version}).";
					CloseButton.Visibility = Visibility.Visible;
				}
			});
		}

		private void UpdateService_DownloadProgress(object? sender, DownloadProgressEventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				if (e.Completed)
				{
					TitleTextBlock.Text = "Download Complete";
					MessageTextBlock.Text = "Update downloaded successfully. The update will be applied when you click OK.";
					DownloadProgressBar.Visibility = Visibility.Collapsed;
					ProgressTextBlock.Visibility = Visibility.Collapsed;
					DownloadButton.Visibility = Visibility.Collapsed;
					OkButton.Visibility = Visibility.Visible;
					CloseButton.Visibility = Visibility.Collapsed;
				}
				else
				{
					DownloadProgressBar.Value = e.Progress;
					ProgressTextBlock.Text = $"Downloading: {e.Progress}% ({FormatBytes(e.Downloaded)} / {FormatBytes(e.Total)})";
					DownloadProgressBar.Visibility = Visibility.Visible;
					ProgressTextBlock.Visibility = Visibility.Visible;
					DownloadButton.Visibility = Visibility.Collapsed;
				}
			});
		}

		private void UpdateService_UpdateError(object? sender, string e)
		{
			Dispatcher.Invoke(() =>
			{
				TitleTextBlock.Text = "Update Error";
				MessageTextBlock.Text = $"An error occurred: {e}";
				CloseButton.Visibility = Visibility.Visible;
				DownloadButton.Visibility = Visibility.Collapsed;
			});
		}

		private void YesButton_Click(object sender, RoutedEventArgs e)
		{
			if (_updateService != null)
			{
				TitleTextBlock.Text = "Downloading Update";
				MessageTextBlock.Text = "Please wait while the update is downloaded...";
				YesButton.Visibility = Visibility.Collapsed;
				NoButton.Visibility = Visibility.Collapsed;
				DownloadButton.Visibility = Visibility.Collapsed;
				DownloadProgressBar.Visibility = Visibility.Visible;
				ProgressTextBlock.Visibility = Visibility.Visible;
				CloseButton.Visibility = Visibility.Collapsed;

				_ = _updateService.StartDownloadAsync();
			}
		}

		private void NoButton_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void CloseButton_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void DownloadButton_Click(object sender, RoutedEventArgs e)
		{
			if (_updateService != null)
			{
				TitleTextBlock.Text = "Downloading Update";
				MessageTextBlock.Text = "Please wait while the update is downloaded...";
				DownloadButton.Visibility = Visibility.Collapsed;
				DownloadProgressBar.Visibility = Visibility.Visible;
				ProgressTextBlock.Visibility = Visibility.Visible;
				CloseButton.Visibility = Visibility.Collapsed;

				_ = _updateService.StartDownloadAsync();
			}
		}

		private void OkButton_Click(object sender, RoutedEventArgs e)
		{
			if (_updateService != null)
			{
				_updateService.ApplyUpdate();
			}
		}

		private string FormatBytes(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len = len / 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		protected override void OnClosed(EventArgs e)
		{
			if (_updateService != null)
			{
				_updateService.UpdateCheckCompleted -= UpdateService_UpdateCheckCompleted;
				_updateService.DownloadProgress -= UpdateService_DownloadProgress;
				_updateService.UpdateError -= UpdateService_UpdateError;
			}
			base.OnClosed(e);
		}
	}
}
