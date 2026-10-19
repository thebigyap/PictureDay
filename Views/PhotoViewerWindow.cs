using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PictureDay.Models;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Controls;

namespace PictureDay.Views;

/// <summary>A separate, cancellable viewer that follows the gallery's current photo order.</summary>
internal sealed class PhotoViewerWindow : Window
{
    private readonly IReadOnlyList<ScreenshotMetadata> _photos;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(16, 10, 16, 12) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(48), MaxWidth = 460 };
    private readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly Button _previous = CreateButton("←", "Previous photo (Left arrow)");
    private readonly Button _next = CreateButton("→", "Next photo (Right arrow)");
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private CancellationTokenSource? _loadCancellation;
    private int _index;
    private bool _closed;
    private bool _fitToWindow = true;
    private double _scale = 1;
    private Point? _dragPoint;

    public PhotoViewerWindow(IReadOnlyList<ScreenshotMetadata> photos, int index)
    {
        _photos = photos;
        _index = index;
        Title = "PictureDay · Photo viewer";
        Width = Math.Min(1200, SystemParameters.WorkArea.Width * 0.9);
        Height = Math.Min(820, SystemParameters.WorkArea.Height * 0.9);
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");

        var close = CreateButton("Close", "Close viewer (Esc)");
        var smaller = CreateButton("−", "Zoom out (−)");
        var larger = CreateButton("+", "Zoom in (+)");
        var fit = CreateButton("Fit", "Fit photo to window (0)");
        var actualSize = CreateButton("100%", "View at original size (1)");
        var controls = new DockPanel { LastChildFill = false, Margin = new Thickness(10) };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        navigation.Children.Add(close);
        navigation.Children.Add(_previous);
        navigation.Children.Add(_next);
        DockPanel.SetDock(navigation, Dock.Left);
        controls.Children.Add(navigation);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        zoom.Children.Add(smaller);
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(larger);
        zoom.Children.Add(fit);
        zoom.Children.Add(actualSize);
        DockPanel.SetDock(zoom, Dock.Right);
        controls.Children.Add(zoom);
        _zoomText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        var imageHost = new Grid { Background = Brushes.Transparent };
        imageHost.Children.Add(_image);
        _scroll.Content = imageHost;
        var canvas = new Grid();
        canvas.Children.Add(_scroll);
        canvas.Children.Add(_message);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(canvas, 1);
        Grid.SetRow(_status, 2);
        layout.Children.Add(controls);
        layout.Children.Add(canvas);
        layout.Children.Add(_status);
        Content = layout;

        close.Click += (_, _) => Close();
        _previous.Click += async (_, _) => await NavigateAsync(-1);
        _next.Click += async (_, _) => await NavigateAsync(1);
        smaller.Click += (_, _) => Zoom(_scale / 1.25);
        larger.Click += (_, _) => Zoom(_scale * 1.25);
        fit.Click += (_, _) => Fit();
        actualSize.Click += (_, _) => Zoom(1);
        _scroll.SizeChanged += (_, _) => { if (_fitToWindow) Fit(); };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            Zoom(e.Delta > 0 ? _scale * 1.12 : _scale / 1.12, e.GetPosition(_scroll));
            e.Handled = true;
        };
        imageHost.MouseLeftButtonDown += (_, e) =>
        {
            if (_scroll.ScrollableWidth <= 0 && _scroll.ScrollableHeight <= 0) return;
            _dragPoint = e.GetPosition(_scroll);
            imageHost.CaptureMouse();
            imageHost.Cursor = Cursors.SizeAll;
            e.Handled = true;
        };
        imageHost.MouseMove += (_, e) =>
        {
            if (_dragPoint is not { } previous || e.LeftButton != MouseButtonState.Pressed) return;
            var current = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset + previous.X - current.X);
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + previous.Y - current.Y);
            _dragPoint = current;
        };
        imageHost.MouseLeftButtonUp += (_, _) =>
        {
            _dragPoint = null;
            imageHost.ReleaseMouseCapture();
            imageHost.Cursor = Cursors.Arrow;
        };
        imageHost.LostMouseCapture += (_, _) => { _dragPoint = null; imageHost.Cursor = Cursors.Arrow; };
        PreviewKeyDown += Viewer_KeyDown;
        Loaded += async (_, _) => { Activate(); Focus(); await LoadPhotoAsync(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _image.Source = null;
        };
    }

    private static Button CreateButton(string text, string description)
    {
        var button = new Button { Content = text, ToolTip = description, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(3, 0, 3, 0), MinWidth = 38 };
        AutomationProperties.SetName(button, description);
        return button;
    }

    private async Task NavigateAsync(int offset)
    {
        int index = _index + offset;
        if (index < 0 || index >= _photos.Count) return;
        _index = index;
        await LoadPhotoAsync();
    }

    private async Task LoadPhotoAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var photo = _photos[_index];
        _previous.IsEnabled = _index > 0;
        _next.IsEnabled = _index + 1 < _photos.Count;
        _message.Text = "Opening photo…";
        _message.Visibility = Visibility.Visible;
        _image.Source = null;
        _status.Text = $"{_index + 1} of {_photos.Count} · {photo.DateTaken:f} · {Path.GetFileName(photo.FilePath)}";
        Title = $"{photo.DateTaken:MMM d, yyyy} · PictureDay";
        try
        {
            // A rapid series of arrow presses queues at most one active full-resolution decode.
            await _decodeGate.WaitAsync(token);
            BitmapImage image;
            try { image = await Task.Run(() => GalleryImageLoader.Load(photo.FilePath), token); }
            finally { _decodeGate.Release(); }
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            _image.Source = image;
            AutomationProperties.SetName(_image, $"Screenshot from {photo.DateTaken:f}");
            _message.Visibility = Visibility.Collapsed;
            _status.Text += $" · {image.PixelWidth} × {image.PixelHeight}";
            Fit();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _closed) return;
            _message.Text = "This photo couldn't be opened.\n\nIt may have moved or be unreadable. Use the arrows to keep browsing, or find its file from the gallery.";
            System.Diagnostics.Debug.WriteLine($"Error opening photo {photo.FilePath}: {ex}");
        }
    }

    private void Fit()
    {
        _fitToWindow = true;
        if (_image.Source is not BitmapImage image) return;
        var width = Math.Max(1, _scroll.ActualWidth - 18);
        var height = Math.Max(1, _scroll.ActualHeight - 18);
        _scale = Math.Min(1, Math.Min(width / image.PixelWidth, height / image.PixelHeight));
        ApplyScale();
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    private void Zoom(double scale, Point? anchor = null)
    {
        if (_image.Source is not BitmapImage image) return;
        var point = anchor ?? new Point(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2);
        // The image is centered while smaller than the viewport; subtract that margin before mapping a point.
        var oldMarginX = Math.Max(0, (_scroll.ViewportWidth - _image.Width) / 2);
        var oldMarginY = Math.Max(0, (_scroll.ViewportHeight - _image.Height) / 2);
        var imageX = (_scroll.HorizontalOffset + point.X - oldMarginX) / _scale;
        var imageY = (_scroll.VerticalOffset + point.Y - oldMarginY) / _scale;
        _fitToWindow = false;
        _scale = Math.Clamp(scale, 0.05, 10);
        ApplyScale();
        _scroll.UpdateLayout();
        var newMarginX = Math.Max(0, (_scroll.ViewportWidth - image.PixelWidth * _scale) / 2);
        var newMarginY = Math.Max(0, (_scroll.ViewportHeight - image.PixelHeight * _scale) / 2);
        _scroll.ScrollToHorizontalOffset(Math.Max(0, imageX * _scale + newMarginX - point.X));
        _scroll.ScrollToVerticalOffset(Math.Max(0, imageY * _scale + newMarginY - point.Y));
    }

    private void ApplyScale()
    {
        if (_image.Source is not BitmapImage image) return;
        _image.Width = image.PixelWidth * _scale;
        _image.Height = image.PixelHeight * _scale;
        _zoomText.Text = $"{_scale:P0}";
    }

    private async void Viewer_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: await NavigateAsync(-1); break;
            case Key.Right: await NavigateAsync(1); break;
            case Key.Home: _index = 0; await LoadPhotoAsync(); break;
            case Key.End: _index = _photos.Count - 1; await LoadPhotoAsync(); break;
            case Key.Add:
            case Key.OemPlus: Zoom(_scale * 1.25); break;
            case Key.Subtract:
            case Key.OemMinus: Zoom(_scale / 1.25); break;
            case Key.D0:
            case Key.NumPad0: Fit(); break;
            case Key.D1:
            case Key.NumPad1: Zoom(1); break;
            default: e.Handled = false; return;
        }
    }
}
