using System;
using System.Windows;
using PictureDay.Services;
using PictureDay.Views;
using Application = System.Windows.Application;

namespace PictureDay
{
	public partial class MainWindow : Window
	{
	private ConfigManager? _configManager;
	private StorageManager? _storageManager;
	private PrivacyFilter? _privacyFilter;

	public MainWindow()
	{
	InitializeComponent();
	Title = $"PictureDay - Version: {App.Version}";
	Loaded += MainWindow_Loaded;
	SettingsViewControl.SettingsSaved += SettingsViewControl_SettingsSaved;
	}

	private void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
	_configManager = (ConfigManager?)Application.Current.Resources["ConfigManager"];
	_storageManager = (StorageManager?)Application.Current.Resources["StorageManager"];

	if (_configManager != null)
	{
	_privacyFilter = new PrivacyFilter(_configManager);
	}

	if (_storageManager != null)
	{
	PhotoGalleryViewControl.Initialize(_storageManager);
	}

	if (_configManager != null)
	{
	SettingsViewControl.Initialize(_configManager);
	}
	}

	private void SettingsViewControl_SettingsSaved(object? sender, EventArgs e)
	{
	if (_configManager != null)
	{
	_privacyFilter?.RefreshBlockedApplications();
	}

	if (_storageManager != null)
	{
	PhotoGalleryViewControl.Initialize(_storageManager);
	}
	}

	public void ShowSettingsTab()
	{
	MainTabControl.SelectedIndex = 1;
	}

	private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
	{
	e.Cancel = true;
	WindowState = WindowState.Minimized;
	Hide();
	}
	}
}
