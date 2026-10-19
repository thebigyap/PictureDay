using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using PictureDay.Models;

namespace PictureDay.Views;

public sealed class GalleryPhotoItem : INotifyPropertyChanged
{
    private BitmapImage? _imageSource;
    private string _previewStatus = "Loading preview…";

    public GalleryPhotoItem(ScreenshotMetadata screenshot) => Screenshot = screenshot;

    public ScreenshotMetadata Screenshot { get; }
    public string FilePath => Screenshot.FilePath;
    public string DateLabel => Screenshot.DateTaken.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
    public string DetailLabel => $"{Screenshot.DateTaken.ToString("t", CultureInfo.CurrentCulture)} · {(IsManual ? "Manual capture" : "Daily capture")}";
    public string AccessibleName => $"Open screenshot from {Screenshot.DateTaken.ToString("f", CultureInfo.CurrentCulture)}";
    public bool IsManual => Path.GetFileName(FilePath).StartsWith("u_", StringComparison.OrdinalIgnoreCase);
    public BitmapImage? ImageSource => _imageSource;
    public string PreviewStatus => _previewStatus;

    public void SetPreview(BitmapImage? image)
    {
        _imageSource = image;
        _previewStatus = image == null ? "Preview unavailable\nOpen the photo or reveal its file" : "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewStatus)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Frozen images can cross threads; OnLoad and a short-lived stream release file handles.</summary>
internal static class GalleryImageLoader
{
    public static BitmapImage Load(string path, int decodeWidth = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // StreamSource has no URI cache entry. IgnoreImageCache would ask WPF to
        // remove a null URI from its cache and fail before decoding on .NET 8.
        bitmap.StreamSource = stream;
        if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>A small least-recently-used cache bounds decoded memory across month changes.</summary>
internal sealed class GalleryThumbnailCache
{
    private const int Capacity = 96;
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Modified, long Length, BitmapImage Image, LinkedListNode<string> Node)> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();

    public BitmapImage Get(string path)
    {
        var info = new FileInfo(path);
        var modified = info.LastWriteTimeUtc.Ticks;
        var length = info.Length;
        lock (_gate)
        {
            if (_images.TryGetValue(path, out var entry))
            {
                _recent.Remove(entry.Node);
                if (entry.Modified == modified && entry.Length == length)
                {
                    _recent.AddFirst(entry.Node);
                    return entry.Image;
                }
                _images.Remove(path);
            }
        }

        var image = GalleryImageLoader.Load(path, 360);
        lock (_gate)
        {
            if (_images.Remove(path, out var previous)) _recent.Remove(previous.Node);
            _images[path] = (modified, length, image, _recent.AddFirst(path));
            while (_images.Count > Capacity && _recent.Last is { } oldest)
            {
                _images.Remove(oldest.Value);
                _recent.RemoveLast();
            }
        }
        return image;
    }
}
