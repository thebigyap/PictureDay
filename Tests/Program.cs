using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PictureDay.Models;
using PictureDay.Services;
using PictureDay.Utils;
using PictureDay.Views;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace PictureDay.Tests;

internal static class Program
{
    // Pump WPF for offscreen checks without starting capture, registry, tray, or updater services.
    private sealed class TestApp : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }

    [STAThread]
    private static int Main(string[] args)
    {

        var app = new TestApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.ApplyTheme("Light");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int exitCode = 0;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                string root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "checks", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                Directory.CreateDirectory(root);
                StorageChecks.Run(Path.Combine(root, "storage"));
                CheckConfig(Path.Combine(root, "config"));
                ConfigChecks.Run(Path.Combine(root, "config-additional"));
                SchedulerChecks.Run(Path.Combine(root, "scheduler"));
                CheckTimeInputs();
                await CheckGalleryAsync(app, root);
                await GalleryChecks.RunAsync(root);
                Console.WriteLine($"All regression checks passed. UI renders: {root}");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return exitCode;
    }

    private static void CheckConfig(string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "config.json");
        var manager = new ConfigManager(path, updateStartupRegistry: false);
        var first = new AppConfig { ScreenshotDirectory = root, Theme = "Dark", BlockedApplications = new() { "private.exe" } };
        manager.SaveConfig(first);
        var next = new AppConfig { ScreenshotDirectory = root, Theme = "Light", BlockedApplications = new() { "banking.exe" } };
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try { manager.SaveConfig(next); } catch (IOException) { failed = true; }
            Assert(failed, "A locked configuration must report save failure.");
            Assert(manager.Config.Theme == "Dark", "Failed save must not commit draft settings.");
        }
        manager.SaveConfig(next);
        Assert(new ConfigManager(path, false).Config.BlockedApplications.Contains("banking.exe"), "Saved privacy configuration must reload.");
        File.WriteAllText(path, "{ invalid json");
        var recovered = new ConfigManager(path, false);
        Assert(recovered.Config.BlockedApplications.Contains("private.exe"), "Corrupt config must recover previous privacy settings from backup.");
        Console.WriteLine("Config checks passed: atomic save, failure rollback, reload, and backup recovery.");
    }

    private static void CheckTimeInputs()
    {
        foreach (string text in new[] { "00:00", "23:59", "9:05", " 14:00 " })
            Assert(ScheduleTimeParser.TryParse(text, out _), $"Valid time rejected: {text}");
        foreach (string text in new[] { "24:00", "-01:30", "1.12:00", "10:99", "14:00:01", "", "noon" })
            Assert(!ScheduleTimeParser.TryParse(text, out _), $"Invalid time accepted: {text}");
        Console.WriteLine("Schedule input checks passed: time-of-day boundaries and invalid durations.");
    }

    private static async Task CheckGalleryAsync(App app, string root)
    {
        string library = Path.Combine(root, "gallery");
        var storage = new StorageManager(library);
        using (var bitmap = new System.Drawing.Bitmap(720, 420))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.FromArgb(24, 32, 51));
            using var font = new System.Drawing.Font("Segoe UI", 23);
            graphics.FillRectangle(System.Drawing.Brushes.SteelBlue, 24, 24, 672, 180);
            graphics.DrawString("A little moment from today", font, System.Drawing.Brushes.White, 50, 70);
            graphics.FillRectangle(System.Drawing.Brushes.DarkSlateGray, 24, 228, 315, 164);
            graphics.FillRectangle(System.Drawing.Brushes.SlateGray, 363, 228, 333, 164);
            for (int i = 0; i < 65; i++)
            {
                DateTime date = DateTime.Today.AddDays(-(i % 12));
                string directory = storage.GetMonthlyDirectory(date);
                bitmap.Save(Path.Combine(directory, $"{(i % 3 == 0 ? "u_" : "")}{date:yyyy-MM-dd}_{10 + i / 60:00}-{i % 60:00}-00.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            bitmap.Save(Path.Combine(storage.GetMonthlyDirectory(DateTime.Today.AddYears(-1)), $"{DateTime.Today.AddYears(-1):yyyy-MM-dd}_12-00-00.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        string brokenPath = Path.Combine(storage.GetMonthlyDirectory(DateTime.Today), $"u_{DateTime.Today:yyyy-MM-dd}_23-59-59.png");
        File.WriteAllText(brokenPath, "This deliberately corrupt fixture should not break the gallery.");
        var config = new ConfigManager(Path.Combine(root, "ui-config.json"), false);
        config.SaveConfig(new AppConfig { ScreenshotDirectory = library, TodayScheduledTime = TimeSpan.FromHours(15), ScheduledTimeDate = DateTime.Today, StartWithWindows = false });
        app.Resources["ConfigManager"] = config;
        app.Resources["StorageManager"] = storage;
        var main = new MainWindow();
        main.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var gallery = (PhotoGalleryView)main.FindName("PhotoGalleryViewControl");
        await gallery.RefreshAsync();
        var content = (FrameworkElement)main.Content;

        foreach (string theme in new[] { "Light", "Dark" })
        {
            app.ApplyTheme(theme);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Render(content, 1160, 780, Path.Combine(root, $"gallery-{theme.ToLowerInvariant()}.png"));
        }
        var photoItems = (ItemsControl)gallery.FindName("PhotosItemsControl");
        Assert(photoItems.Items.Cast<GalleryPhotoItem>().Any(photo => photo.ImageSource != null), "Valid previews must render alongside a corrupt image.");
        Assert(photoItems.Items.Count is > 0 and <= 24, "Gallery should cap instantiated cards per page.");
        var status = ((TextBlock)gallery.FindName("GalleryStatusText")).Text;
        Assert(status.Contains("unavailable"), "Corrupt preview must be reported without aborting other previews.");
        ((System.Windows.Controls.ComboBox)gallery.FindName("CaptureFilterComboBox")).SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await gallery.RefreshAsync();
        Assert(((TextBlock)gallery.FindName("MonthSummaryText")).Text.Contains("photo"), "Filter should retain a readable summary.");
        var settings = (SettingsView)main.FindName("SettingsViewControl");
        ((System.Windows.Controls.RadioButton)main.FindName("SettingsNav")).IsChecked = true;
        Render(content, 980, 660, Path.Combine(root, "settings-compact.png"));
        Assert(((System.Windows.Controls.CheckBox)settings.FindName("ShrinkOldPhotosCheckBox")).IsChecked == true, "Shrinking old photos should load as on by default.");
        ((FrameworkElement)settings.FindName("ShrinkOldPhotosCheckBox")).BringIntoView();
        Render(content, 980, 660, Path.Combine(root, "settings-quality.png"));
        var empty = new StorageManager(Path.Combine(root, "empty"));
        gallery.Initialize(empty);
        await gallery.RefreshAsync();
        ((System.Windows.Controls.RadioButton)main.FindName("GalleryNav")).IsChecked = true;
        Render(content, 980, 660, Path.Combine(root, "gallery-empty.png"));
        Assert(((ItemsControl)gallery.FindName("PhotosItemsControl")).Items.Count == 0, "Empty library must not show stale photos.");
		Directory.Delete(empty.GetBaseDirectory()); // The fixture is empty; never remove user photos.
		await gallery.RefreshAsync();
		Assert(((TextBlock)gallery.FindName("GalleryStatusText")).Text.Contains("could not"), "An unavailable folder must report an error instead of appearing empty.");
        Console.WriteLine("WPF checks passed: populated/empty gallery, corrupt previews, pagination, filters, compact settings, and both themes.");
    }

    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var target = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
