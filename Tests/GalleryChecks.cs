using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PictureDay.Models;
using PictureDay.Services;
using PictureDay.Views;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;

namespace PictureDay.Tests;

public static class GalleryChecks
{
    public static async Task RunAsync(string root)
    {
        var storage = new StorageManager(Path.Combine(root, "gallery-regression-cases"));
        int firstYear = DateTime.Today.Year - 2;
        int secondYear = firstYear + 1;
        string firstPath = WritePhoto(storage, new DateTime(firstYear, 11, 7, 10, 0, 0));
        for (int i = 0; i < 31; i++)
            WritePhoto(storage, new DateTime(secondYear, 2, 1 + i % 28, 10, i, 0), manual: i % 2 == 0);
        string corruptPath = Path.Combine(storage.GetMonthlyDirectory(new DateTime(secondYear, 2, 28)), $"{secondYear}-02-28_23-59-59.png");
        File.WriteAllText(corruptPath, "An invalid PNG must fail only this preview.");

        var gallery = new PhotoGalleryView();
        gallery.Initialize(storage);
        await gallery.RefreshAsync();
        var year = Find<ComboBox>(gallery, "YearComboBox");
        var month = Find<ComboBox>(gallery, "MonthComboBox");
        var filter = Find<ComboBox>(gallery, "CaptureFilterComboBox");
        var sort = Find<ComboBox>(gallery, "SortComboBox");
        var items = Find<ItemsControl>(gallery, "PhotosItemsControl");

        year.SelectedItem = firstYear;
        await InvokeTask(gallery, "LoadPhotosAsync");
        Assert(month.SelectedValue is 11, "Selecting an earliest year must select November by value, not the nonexistent eleventh dropdown index.");
        Assert(items.Items.Cast<GalleryPhotoItem>().Single().FilePath == firstPath, "Year navigation must display its selected month.");

        year.SelectedItem = secondYear;
        month.SelectedValue = 2;
        await InvokeTask(gallery, "LoadPhotosAsync");
        Assert(items.Items.Count == 24, "A dense month must instantiate only 24 gallery cards.");
        Assert(Find<TextBlock>(gallery, "GalleryStatusText").Text.Contains("unavailable"), "One corrupt preview must be reported while the rest finish.");
        Assert(items.Items.Cast<GalleryPhotoItem>().Any(photo => photo.ImageSource != null), "Readable previews must survive a corrupt neighbor.");

        filter.SelectedIndex = 1;
        await InvokeTask(gallery, "LoadPhotosAsync");
        Assert(items.Items.Cast<GalleryPhotoItem>().All(photo => photo.IsManual), "Manual filter must exclude scheduled captures.");
        filter.SelectedIndex = 2;
        sort.SelectedIndex = 1;
        await InvokeTask(gallery, "LoadPhotosAsync");
        var scheduled = items.Items.Cast<GalleryPhotoItem>().ToArray();
        Assert(scheduled.All(photo => !photo.IsManual), "Daily filter must exclude manual captures.");
        Assert(scheduled.Select(photo => photo.Screenshot.DateTaken).SequenceEqual(scheduled.Select(photo => photo.Screenshot.DateTaken).Order()), "Oldest-first order must be chronological.");

        // Several navigation/refresh requests can overlap; only the newest request may populate the view.
        Task firstRefresh = gallery.RefreshAsync();
        Task secondRefresh = gallery.RefreshAsync();
        year.SelectedItem = firstYear;
        year.SelectedItem = secondYear;
        month.SelectedValue = 2;
        filter.SelectedIndex = 0;
        await Task.WhenAll(firstRefresh, secondRefresh);
        await InvokeTask(gallery, "LoadPhotosAsync");
        Assert(items.Items.Cast<GalleryPhotoItem>().All(photo => photo.Screenshot.DateTaken.Year == secondYear && photo.Screenshot.DateTaken.Month == 2), "Canceled navigation must not add stale photos.");

        gallery.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        gallery.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        await gallery.RefreshAsync();
        Assert(items.Items.Count == 24, "A gallery must resume loading after it is unloaded and shown again.");
        var currentItems = items.Items.Cast<GalleryPhotoItem>().ToArray();
        var cachedImage = currentItems.First(photo => photo.ImageSource != null);
        await InvokeTask(gallery, "LoadPhotosAsync");
        Assert(ReferenceEquals(cachedImage.ImageSource, items.Items.Cast<GalleryPhotoItem>().Single(photo => photo.FilePath == cachedImage.FilePath).ImageSource), "Revisiting a page should reuse unchanged frozen previews.");

        var viewerType = typeof(PhotoGalleryView).Assembly.GetType("PictureDay.Views.PhotoViewerWindow", throwOnError: true)!;
        var viewerPhotos = new List<ScreenshotMetadata>
        {
            new() { FilePath = corruptPath, DateTaken = new DateTime(secondYear, 2, 28) },
            new() { FilePath = firstPath, DateTaken = new DateTime(firstYear, 11, 7) },
            new() { FilePath = Path.Combine(root, "does-not-exist.png"), DateTaken = DateTime.Today }
        };
        var viewer = (Window)Activator.CreateInstance(viewerType, viewerPhotos, 0)!;
        try
        {
            await InvokeTask(viewer, "LoadPhotoAsync");
            Assert(GetField<TextBlock>(viewer, "_message").Text.Contains("couldn't be opened"), "A corrupt photo must show an inline viewer error.");
            await InvokeTask(viewer, "NavigateAsync", 1);
            Assert(GetField<Image>(viewer, "_image").Source is BitmapImage { IsFrozen: true }, "Viewer navigation must recover from a corrupt photo.");
            await InvokeTask(viewer, "NavigateAsync", 1);
            Assert(GetField<Image>(viewer, "_image").Source == null, "A missing photo must clear the previous image.");
            Task previous = InvokeTask(viewer, "NavigateAsync", -1);
            Task next = InvokeTask(viewer, "NavigateAsync", 1);
            await Task.WhenAll(previous, next);
            Assert(GetField<Image>(viewer, "_image").Source == null, "A canceled decode must not replace the latest viewer state.");
        }
        finally { viewer.Close(); }

        Console.WriteLine("Gallery checks passed: partial-year month selection, pagination, capture filters, ordering, cancellation, resume, thumbnail reuse, and corrupt/missing viewer recovery.");
    }

    private static string WritePhoto(StorageManager storage, DateTime date, bool manual = false)
    {
        var pixels = new byte[120 * 80 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 130; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 120 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(storage.GetMonthlyDirectory(date), $"{(manual ? "u_" : "")}{date:yyyy-MM-dd_HH-mm-ss}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static T Find<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
    private static T GetField<T>(object instance, string field) => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static Task InvokeTask(object instance, string method, params object[] arguments) => (Task)instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments)!;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
