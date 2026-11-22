using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Model;
using Free.Shared.Drawing;
using Free.Shared.Localization;

using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace FreeX.App.Avalonia;

/// <summary>
/// Dragging a selected chart element (title, legend, plot area, axis title, data label) to a new
/// position, and resizing the plot area by its grips, like Excel. The geometry and the resulting
/// manual layout come from the shared <see cref="ChartElementDragPlanner"/> (the same planner the WPF
/// host uses). The press that selects an element also starts the drag; because the chart overlay is
/// rebuilt on selection, the move/release are tracked at window level while the pointer is captured.
/// The drag is committed as one undoable layout command on release.
/// </summary>
public sealed partial class MainWindow
{
    private ChartElementDragSession? _elementDrag;
    private Guid _elementDragChartId;
    private Point _elementDragWindowStart;
    private LayoutPoint _elementDragPress;
    private AvaloniaRectangle? _elementDragGhost;
    private bool _elementDragHandlersWired;

    // The highlight layer of the selected element, so the drag ghost can be added without a rebuild.
    private AvaloniaCanvas? _chartElementHighlightLayer;

    private bool TryBeginChartElementDrag(
        ChartModel chart,
        ChartElementMap map,
        ChartElementRef element,
        LayoutPoint local,
        PointerPressedEventArgs args,
        ChartResizeHandle handle)
    {
        var session = ChartElementDragPlanner.Begin(chart, map, element, local, handle);
        if (session is null)
            return false;

        if (!_elementDragHandlersWired)
        {
            AddHandler(PointerMovedEvent, OnElementDragPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, OnElementDragPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(KeyDownEvent, OnElementDragKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            _elementDragHandlersWired = true;
        }

        _elementDrag = session;
        _elementDragChartId = chart.Id;
        _elementDragPress = local;
        _elementDragWindowStart = args.GetPosition(this);
        _elementDragGhost = null;
        args.Pointer.Capture(this);
        return true;
    }

    private void OnElementDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_elementDrag is not { } session)
            return;

        var p = e.GetPosition(this);
        var bounds = session.Update(new LayoutPoint(
            _elementDragPress.X + (p.X - _elementDragWindowStart.X),
            _elementDragPress.Y + (p.Y - _elementDragWindowStart.Y)));
        if (!session.HasMoved || _chartElementHighlightLayer is null)
            return;

        if (_elementDragGhost is null)
        {
            _elementDragGhost = new AvaloniaRectangle
            {
                Stroke = SelectionBorder,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 2],
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            _chartElementHighlightLayer.Children.Add(_elementDragGhost);
        }

        _elementDragGhost.Width = Math.Max(1, bounds.Width);
        _elementDragGhost.Height = Math.Max(1, bounds.Height);
        AvaloniaCanvas.SetLeft(_elementDragGhost, bounds.Left);
        AvaloniaCanvas.SetTop(_elementDragGhost, bounds.Top);
    }

    private void OnElementDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_elementDrag is not { } session)
            return;

        var chartId = _elementDragChartId;
        ClearElementDrag(e.Pointer);
        if (session.Complete() is not { } options ||
            _session.ActiveSheet.Charts.FirstOrDefault(c => c.Id == chartId) is not { } chart)
        {
            return;
        }

        var name = ChartElementCommandPlanner
            .Describe(session.Element, _selectedChartElementSeriesName)
            .Resolve(UiText.Get, UiText.Format);
        ApplyChartLayout(name, chart, options);
    }

    private void OnElementDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _elementDrag is null)
            return;

        ClearElementDrag(null);
        if (_elementDragGhost?.Parent is AvaloniaCanvas layer)
            layer.Children.Remove(_elementDragGhost);
        _elementDragGhost = null;
        e.Handled = true;
    }

    private void ClearElementDrag(IPointer? pointer)
    {
        _elementDrag = null;
        _elementDragChartId = Guid.Empty;
        pointer?.Capture(null);
    }
}
