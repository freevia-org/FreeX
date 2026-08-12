using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    private static readonly IBrush SplitPaneScrollbarTrackBrush = Brush(224, 224, 224);
    private static readonly IBrush SplitPaneScrollbarThumbBrush = Brush(170, 170, 170);
    private static readonly IBrush SplitPaneScrollbarBorderBrush = Brush(150, 150, 150);
    private bool _splitPanePointerHandlersAttached;
    private SplitPanePointerHandle _splitPaneDividerDragHandle;
    private IPointer? _splitPaneDragPointer;
    private SplitPanePointerScrollbar? _splitPaneScrollbarDragSource;
    private double _splitPaneScrollbarDragPointerOffset;

    private void AttachSplitPanePointerHandlers()
    {
        if (_splitPanePointerHandlersAttached)
            return;

        _splitPanePointerHandlersAttached = true;
        _sheetGridHost.AddHandler(
            InputElement.PointerPressedEvent,
            SplitPanePointerPressed,
            RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(
            InputElement.PointerMovedEvent,
            SplitPanePointerMoved,
            RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(
            InputElement.PointerReleasedEvent,
            SplitPanePointerReleased,
            RoutingStrategies.Tunnel);
        _sheetGridHost.PointerCaptureLost += SplitPanePointerCaptureLost;
    }

    private void SplitPanePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(_sheetGridHost).Properties.IsLeftButtonPressed ||
            !TryGetSplitPanePointerLayout(out var layout))
        {
            return;
        }

        var position = args.GetPosition(_sheetGridHost);
        var pointer = new GridPoint(position.X, position.Y);
        if (SplitPanePointerPlanner.HitTestScrollbar(layout.Chrome, pointer) is { } scrollbarHit)
        {
            _splitPaneScrollbarDragSource = scrollbarHit.Region == SplitPanePointerRegion.TopRight
                ? layout.Chrome.HorizontalTopRight
                : layout.Chrome.VerticalBottomLeft;
            if (_splitPaneScrollbarDragSource is not { } scrollbar)
                return;

            _splitPaneScrollbarDragPointerOffset = scrollbarHit.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
                ? position.X - scrollbar.Thumb.Left
                : position.Y - scrollbar.Thumb.Top;
            if (scrollbarHit.Part == SplitPanePointerScrollbarPart.Thumb)
            {
                _splitPaneDragPointer = args.Pointer;
                args.Pointer.Capture(_sheetGridHost);
            }
            else
            {
                _splitPaneScrollbarDragSource = null;
                _splitPaneScrollbarDragPointerOffset = 0;
                ApplySplitPaneScrollbarTrackTarget(layout.Viewport, pointer, scrollbar);
            }

            args.Handled = true;
            return;
        }

        var divider = SplitPanePointerPlanner.HitTestDivider(
            layout.Viewport,
            pointer,
            layout.Width,
            layout.Height,
            layout.RowHeaderWidth,
            layout.ColumnHeaderHeight,
            layout.MetricScale);
        if (divider == SplitPanePointerHandle.None)
            return;

        _splitPaneDividerDragHandle = divider;
        _splitPaneDragPointer = args.Pointer;
        args.Pointer.Capture(_sheetGridHost);
        args.Handled = true;
    }

    private void SplitPanePointerMoved(object? sender, PointerEventArgs args)
    {
        if (_splitPaneDividerDragHandle == SplitPanePointerHandle.None &&
            _splitPaneScrollbarDragSource is not { } source)
        {
            return;
        }

        if (!TryGetSplitPanePointerLayout(out var layout))
            return;

        var position = args.GetPosition(_sheetGridHost);
        var pointer = new GridPoint(position.X, position.Y);
        if (_splitPaneDividerDragHandle != SplitPanePointerHandle.None)
        {
            _sheetGridHost.Cursor = _splitPaneDividerDragHandle switch
            {
                SplitPanePointerHandle.Intersection => new Cursor(StandardCursorType.SizeAll),
                SplitPanePointerHandle.Vertical => new Cursor(StandardCursorType.SizeWestEast),
                _ => new Cursor(StandardCursorType.SizeNorthSouth),
            };
            args.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragSource is { } scrollbar && _splitPaneDragPointer is not null)
        {
            var target = SplitPanePointerPlanner.CalculateThumbDragTarget(
                scrollbar,
                pointer,
                _splitPaneScrollbarDragPointerOffset);
            ApplySplitPaneScrollbarTarget(target);
            _sheetGridHost.Cursor = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
                ? new Cursor(StandardCursorType.SizeWestEast)
                : new Cursor(StandardCursorType.SizeNorthSouth);
            args.Handled = true;
        }
    }

    private void SplitPanePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_splitPaneDividerDragHandle == SplitPanePointerHandle.None &&
            _splitPaneScrollbarDragSource is null)
        {
            return;
        }

        if (TryGetSplitPanePointerLayout(out var layout))
        {
            var position = args.GetPosition(_sheetGridHost);
            var pointer = new GridPoint(position.X, position.Y);
            if (_splitPaneDividerDragHandle != SplitPanePointerHandle.None)
            {
                var handle = _splitPaneDividerDragHandle;
                if (SplitPanePointerPlanner.CalculateDividerDragTarget(
                        layout.Viewport,
                        handle,
                        pointer,
                        layout.RowHeaderWidth,
                        layout.ColumnHeaderHeight,
                        layout.MetricScale) is { } target)
                {
                    ApplySplitPaneDividerTarget(target);
                }
            }
            else if (_splitPaneScrollbarDragSource is { } scrollbar)
            {
                ApplySplitPaneScrollbarTarget(SplitPanePointerPlanner.CalculateThumbDragTarget(
                    scrollbar,
                    pointer,
                    _splitPaneScrollbarDragPointerOffset));
            }
        }

        ClearSplitPanePointerCapture();
        args.Handled = true;
    }

    private void SplitPanePointerCaptureLost(object? sender, PointerCaptureLostEventArgs args) =>
        ClearSplitPanePointerCapture();

    private void ClearSplitPanePointerCapture()
    {
        _splitPaneDividerDragHandle = SplitPanePointerHandle.None;
        _splitPaneScrollbarDragSource = null;
        _splitPaneScrollbarDragPointerOffset = 0;
        _splitPaneDragPointer = null;
        _sheetGridHost.Cursor = Cursor.Default;
    }

    private bool TryGetSplitPanePointerLayout(out SplitPanePointerLayout layout)
    {
        var viewport = _session.Viewport;
        if (viewport.SplitPanes is null)
        {
            layout = default;
            return false;
        }

        var zoomFactor = GetActiveZoomFactor();
        var rowHeaderWidth = _session.IsShowingHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var columnHeaderHeight = _session.IsShowingHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;
        var width = _sheetGridHost.Bounds.Width;
        var height = _sheetGridHost.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            layout = default;
            return false;
        }

        var chrome = SplitPanePointerPlanner.CalculateScrollbarChrome(
            viewport,
            width,
            height,
            rowHeaderWidth,
            columnHeaderHeight,
            zoomFactor);
        layout = new SplitPanePointerLayout(
            viewport,
            chrome,
            width,
            height,
            rowHeaderWidth,
            columnHeaderHeight,
            zoomFactor);
        return true;
    }

    private void ApplySplitPaneDividerTarget(SplitPanePointerDividerDragTarget target)
    {
        var currentRow = _session.GetEffectiveSplitRow();
        var currentColumn = _session.GetEffectiveSplitCol();
        var nextRow = target.Row ?? currentRow;
        var nextColumn = target.Column ?? currentColumn;
        if (nextRow == currentRow && nextColumn == currentColumn)
            return;

        var result = _session.SetSplitPanes(nextRow, nextColumn);
        if (result.Success)
        {
            RefreshShell(UiText.Get("MainLoc_Ready"));
        }
        else
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("SplitPane_MoveFailed"));
    }

    private void ApplySplitPaneScrollbarTrackTarget(
        ViewportModel viewport,
        GridPoint position,
        SplitPanePointerScrollbar scrollbar)
    {
        var currentIndex = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? viewport.ColMetrics.FirstOrDefault()?.Col ?? 1
            : viewport.RowMetrics.FirstOrDefault()?.Row ?? 1;
        ApplySplitPaneScrollbarTarget(SplitPanePointerPlanner.CalculatePageTarget(
            scrollbar,
            currentIndex,
            position));
    }

    private void ApplySplitPaneScrollbarTarget(SplitPanePointerScrollTarget target)
    {
        var currentIndex = target.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? _session.ViewportOrigin.LeftCol
            : _session.ViewportOrigin.TopRow;
        var delta = target.Index > currentIndex
            ? (int)Math.Min(target.Index - currentIndex, int.MaxValue)
            : -(int)Math.Min(currentIndex - target.Index, int.MaxValue);
        var changed = target.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? _session.PanViewport(0, delta)
            : _session.PanViewport(delta, 0);
        if (changed)
        {
            RefreshShellForViewportPan(UiText.Get("MainLoc_Ready"));
            BroadcastScrollOffsetToSideBySidePartner();
        }
    }

    private static bool CanScrollSplitPane(SplitPanePointerRegion region, bool horizontal) =>
        SplitPanePointerPlanner.CanScroll(region, horizontal);

    private void AddSplitPanePointerChromeToGrid(
        AvaloniaGrid grid,
        ViewportModel viewport,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        bool showHeadings,
        double zoomFactor)
    {
        var rowHeaderWidth = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;
        var columnHeaderHeight = showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0;
        var actualWidth = rowHeaderWidth + colMetrics.Sum(metric => GetDisplayedColumnWidth(metric, zoomFactor));
        var actualHeight = columnHeaderHeight + rowMetrics.Sum(metric => GetDisplayedRowHeight(metric, zoomFactor));
        var chrome = SplitPanePointerPlanner.CalculateScrollbarChrome(
            viewport,
            actualWidth,
            actualHeight,
            rowHeaderWidth,
            columnHeaderHeight,
            zoomFactor);
        if (chrome.HorizontalTopRight is null && chrome.VerticalBottomLeft is null)
            return;

        var layer = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            VerticalAlignment = AvaloniaVerticalAlignment.Stretch,
        };
        AvaloniaGrid.SetRow(layer, 0);
        AvaloniaGrid.SetColumn(layer, 0);
        AvaloniaGrid.SetRowSpan(layer, grid.RowDefinitions.Count);
        AvaloniaGrid.SetColumnSpan(layer, grid.ColumnDefinitions.Count);
        AddSplitPaneScrollbarVisual(layer, chrome.HorizontalTopRight);
        AddSplitPaneScrollbarVisual(layer, chrome.VerticalBottomLeft);
        grid.Children.Add(layer);
    }

    private static void AddSplitPaneScrollbarVisual(Canvas layer, SplitPanePointerScrollbar? scrollbar)
    {
        if (scrollbar is not { } value)
            return;

        var track = new Border
        {
            Background = SplitPaneScrollbarTrackBrush,
            BorderBrush = SplitPaneScrollbarBorderBrush,
            BorderThickness = new Thickness(1),
            Width = value.Track.Width,
            Height = value.Track.Height,
        };
        Canvas.SetLeft(track, value.Track.Left);
        Canvas.SetTop(track, value.Track.Top);
        layer.Children.Add(track);

        var thumb = new Border
        {
            Background = SplitPaneScrollbarThumbBrush,
            BorderBrush = SplitPaneScrollbarBorderBrush,
            BorderThickness = new Thickness(1),
            Width = value.Thumb.Width,
            Height = value.Thumb.Height,
        };
        Canvas.SetLeft(thumb, value.Thumb.Left);
        Canvas.SetTop(thumb, value.Thumb.Top);
        layer.Children.Add(thumb);
    }

    private readonly record struct SplitPanePointerLayout(
        ViewportModel Viewport,
        SplitPanePointerScrollbarChrome Chrome,
        double Width,
        double Height,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        double MetricScale);
}
