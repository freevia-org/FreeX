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
    private readonly ScreenClipSelectionSession _selectionSession = new();
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

        ApplySelectionVisual(_selectionSession.Begin(point.Position.X, point.Position.Y));
        args.Pointer.Capture(_canvas);
        args.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        var current = args.GetPosition(_canvas);
        if (_selectionSession.Update(current.X, current.Y) is { } update)
        {
            ApplySelectionVisual(update);
            args.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton != MouseButton.Left)
            return;

        var current = args.GetPosition(_canvas);
        if (_selectionSession.Complete(current.X, current.Y) is not { } completion)
            return;

        args.Pointer.Capture(null);
        _result = ScreenClipPlanner.BuildPhysicalSelection(
            completion.Origin.X,
            completion.Origin.Y,
            completion.Current.X,
            completion.Current.Y,
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

    private void ApplySelectionVisual(ScreenClipSelectionUpdate update)
    {
        Canvas.SetLeft(_selection, update.Bounds.Left);
        Canvas.SetTop(_selection, update.Bounds.Top);
        _selection.Width = update.Bounds.Width;
        _selection.Height = update.Bounds.Height;
        _selection.IsVisible = true;
    }

    private void Cancel()
    {
        _selectionSession.Cancel();
        _result = null;
        Close();
    }

    private void Complete() => _completion?.TrySetResult(_result);
}
