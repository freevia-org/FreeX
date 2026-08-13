using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace FreeW.App.Host.Editing;

/// <summary>
/// A full-screen, semi-transparent overlay that lets the user drag-select a rectangular screen region
/// for Insert &gt; Illustrations &gt; Screenshot &gt; Screen Clipping (mirroring Word). On mouse-up the
/// selected rectangle is returned in <em>physical screen pixels</em> (ready for
/// <see cref="ScreenshotCapture.CaptureRegionPng"/>); pressing Escape, right-clicking, or a zero-size
/// drag cancels and returns <see langword="null"/>.
/// </summary>
internal sealed class ScreenClipOverlay : Window
{
    private readonly Canvas _canvas;
    private readonly WpfRectangle _selection;
    private readonly ScreenClipSelectionSession _selectionSession = new();
    private System.Drawing.Rectangle? _resultPhysical;

    private ScreenClipOverlay()
    {
        // Span the entire virtual screen (all monitors). Bounds are in WinForms physical pixels; we map
        // them to WPF device-independent units below so the overlay lines up under any DPI scaling.
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;

        _selection = new WpfRectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
            StrokeThickness = 1.5,
            // A clear fill inside the selection so the user sees what they will capture.
            Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed
        };
        _canvas = new Canvas { Background = Brushes.Transparent };
        _canvas.Children.Add(_selection);
        Content = _canvas;

        // The DPI transform only exists after the HWND is created, so finalise placement on SourceInitialized.
        SourceInitialized += (_, _) => PlaceOverVirtualScreen(virtualScreen);

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonDown += (_, _) => Cancel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
    }

    /// <summary>
    /// Shows the overlay modally and returns the user-selected region in physical screen pixels, or
    /// <see langword="null"/> if cancelled / empty.
    /// </summary>
    public static System.Drawing.Rectangle? PromptForRegion()
    {
        var overlay = new ScreenClipOverlay();
        overlay.ShowDialog();
        return overlay._resultPhysical;
    }

    private void PlaceOverVirtualScreen(System.Drawing.Rectangle virtualScreenPx)
    {
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        // Pixels-per-DIP on each axis (1.0 at 96 DPI, 1.5 at 144 DPI, …).
        var scaleX = toDevice.M11 == 0 ? 1.0 : toDevice.M11;
        var scaleY = toDevice.M22 == 0 ? 1.0 : toDevice.M22;

        Left = virtualScreenPx.Left / scaleX;
        Top = virtualScreenPx.Top / scaleY;
        Width = virtualScreenPx.Width / scaleX;
        Height = virtualScreenPx.Height / scaleY;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        ApplySelectionVisual(_selectionSession.Begin(point.X, point.Y));
        _canvas.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(_canvas);
        if (_selectionSession.Update(current.X, current.Y) is { } update)
            ApplySelectionVisual(update);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var current = e.GetPosition(_canvas);
        if (_selectionSession.Complete(current.X, current.Y) is not { } completion)
            return;

        _canvas.ReleaseMouseCapture();
        _resultPhysical = ToPhysicalScreenRect(completion.Origin, completion.Current);
        Close();
    }

    private void Cancel()
    {
        _selectionSession.Cancel();
        _resultPhysical = null;
        Close();
    }

    private void ApplySelectionVisual(ScreenClipSelectionUpdate update)
    {
        Canvas.SetLeft(_selection, update.Bounds.Left);
        Canvas.SetTop(_selection, update.Bounds.Top);
        _selection.Width = update.Bounds.Width;
        _selection.Height = update.Bounds.Height;
        _selection.Visibility = Visibility.Visible;
    }

    // Map the DIP selection (relative to the overlay) back to absolute physical screen pixels.
    private System.Drawing.Rectangle? ToPhysicalScreenRect(ScreenClipPoint a, ScreenClipPoint b)
    {
        var source = PresentationSource.FromVisual(this);
        if (source is null)
            return null;

        // PointToScreen yields absolute physical device pixels (accounts for window position + DPI).
        var startPx = PointToScreen(new System.Windows.Point(a.X, a.Y));
        var endPx = PointToScreen(new System.Windows.Point(b.X, b.Y));
        return ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
            startPx.X,
            startPx.Y,
            endPx.X,
            endPx.Y) is { } region
                ? new System.Drawing.Rectangle(region.X, region.Y, region.Width, region.Height)
                : null;
    }
}
