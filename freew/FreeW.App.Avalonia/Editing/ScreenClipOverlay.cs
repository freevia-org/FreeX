using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

/// <summary>Fullscreen cross-platform drag selector for Insert &gt; Screen Clipping.</summary>
internal sealed partial class ScreenClipOverlay : Window
{
    private readonly Canvas _canvas;
    private readonly Rectangle _selection;
    private readonly PixelRect _virtualBounds;
    private Point _origin;
    private bool _dragging;
    private ScreenPixelRect? _result;
    private TaskCompletionSource<ScreenPixelRect?>? _completion;

    public ScreenClipOverlay(PixelRect virtualBounds, double renderScale)
    {
        if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(virtualBounds));
        if (!double.IsFinite(renderScale) || renderScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderScale));

        _virtualBounds = virtualBounds;
        Position = new PixelPoint(virtualBounds.X, virtualBounds.Y);
        Width = virtualBounds.Width / renderScale;
        Height = virtualBounds.Height / renderScale;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Cursor = new Cursor(StandardCursorType.Cross);

        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            IsVisible = false,
        };
        _canvas = new Canvas { Background = Brushes.Transparent };
        _canvas.Children.Add(_selection);
        Content = _canvas;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
        Closed += (_, _) => Complete();
        Opened += (_, _) => Focus();
    }

    public Task<ScreenPixelRect?> ShowSelectionAsync()
    {
        if (_completion is not null)
            throw new InvalidOperationException("The screen clipping overlay is already active.");

        _completion = new TaskCompletionSource<ScreenPixelRect?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Show();
        Activate();
        return _completion.Task;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint(_canvas);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            Cancel();
            args.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        _origin = point.Position;
        _dragging = true;
        UpdateSelectionVisual(_origin);
        args.Pointer.Capture(_canvas);
        args.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (!_dragging)
            return;

        UpdateSelectionVisual(args.GetPosition(_canvas));
        args.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!_dragging || args.InitialPressMouseButton != MouseButton.Left)
            return;

        _dragging = false;
        args.Pointer.Capture(null);
        var current = args.GetPosition(_canvas);
        _result = ScreenClipPlanner.BuildPhysicalSelection(
            _origin.X,
            _origin.Y,
            current.X,
            current.Y,
            _virtualBounds.X,
            _virtualBounds.Y,
            RenderScaling);
        args.Handled = true;
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape)
            return;

        args.Handled = true;
        Cancel();
    }

    private void UpdateSelectionVisual(Point current)
    {
        var left = Math.Min(_origin.X, current.X);
        var top = Math.Min(_origin.Y, current.Y);
        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = Math.Abs(current.X - _origin.X);
        _selection.Height = Math.Abs(current.Y - _origin.Y);
        _selection.IsVisible = true;
    }

    private void Cancel()
    {
        _result = null;
        Close();
    }

    private void Complete() => _completion?.TrySetResult(_result);
}
