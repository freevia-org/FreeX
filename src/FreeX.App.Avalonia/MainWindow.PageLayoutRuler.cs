using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double PageLayoutMarginHitZone = 5;
    private static readonly IBrush PageLayoutMarginGuideBrush = Brush(80, 150, 220);
    private static readonly IBrush PageLayoutMarginHandleFill = Brush(238, 238, 238);
    private static readonly IBrush PageLayoutMarginHandleBorder = Brush(75, 75, 75);
    private PageLayoutMarginRuler? _pageLayoutRuler;
    private PageLayoutMarginDrag? _pageLayoutMarginDrag;

    private sealed record PageLayoutMarginDrag(
        PageLayoutMarginRuler Ruler,
        IPointer Pointer,
        WorksheetPageMarginEdge Edge,
        double StartCoordinate,
        double StartGuideCoordinate);

    private void AttachPageLayoutRulerPointerHandlers()
    {
        _sheetGridHost.AddHandler(InputElement.PointerPressedEvent, PageLayoutMarginPointerPressed, RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(InputElement.PointerMovedEvent, PageLayoutMarginPointerMoved, RoutingStrategies.Tunnel);
        _sheetGridHost.AddHandler(InputElement.PointerReleasedEvent, PageLayoutMarginPointerReleased, RoutingStrategies.Tunnel);
        _sheetGridHost.PointerCaptureLost += (_, _) => RestorePageLayoutMarginDrag();
    }

    private void PageLayoutMarginPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_pageLayoutRuler is not { Guide: { } guide } ruler ||
            _isOpening || _isSaving || IsBorderDrawModeActive ||
            _session.FormulaEditAddress is not null || _dialogRangeSelectionController.IsActive)
        {
            return;
        }

        var pointer = args.GetCurrentPoint(ruler);
        if (!pointer.Properties.IsLeftButtonPressed || IsContextClick(pointer, args) ||
            ruler.HitTestMargin(pointer.Position) is not { } edge)
        {
            return;
        }

        var guideCoordinate = edge switch
        {
            WorksheetPageMarginEdge.Left => guide.MarginLeft,
            WorksheetPageMarginEdge.Right => guide.MarginRight,
            WorksheetPageMarginEdge.Top => guide.MarginTop,
            _ => guide.MarginBottom,
        };
        _pageLayoutMarginDrag = new(
            ruler, args.Pointer, edge, PageLayoutMarginCoordinate(pointer.Position, edge), guideCoordinate);
        args.Pointer.Capture(_sheetGridHost);
        _sheetGridHost.Cursor = PageLayoutMarginCursor(edge);
        args.Handled = true;
    }

    private void PageLayoutMarginPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_pageLayoutMarginDrag is { } drag && ReferenceEquals(drag.Pointer, args.Pointer))
        {
            if (ReferenceEquals(drag.Ruler, _pageLayoutRuler))
                drag.Ruler.DrawMargins(CalculateDraggedPageMargins(drag, args.GetPosition(drag.Ruler)));
            args.Handled = true;
        }
        else if (args.Pointer.Captured is null && !IsBorderDrawModeActive &&
            _pageLayoutRuler is { } ruler && ruler.HitTestMargin(args.GetPosition(ruler)) is { } edge)
        {
            _sheetGridHost.Cursor = PageLayoutMarginCursor(edge);
            args.Handled = true;
        }
    }

    private void PageLayoutMarginPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_pageLayoutMarginDrag is not { } drag || !ReferenceEquals(drag.Pointer, args.Pointer) ||
            args.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var margins = ReferenceEquals(drag.Ruler, _pageLayoutRuler)
            ? CalculateDraggedPageMargins(drag, args.GetPosition(drag.Ruler))
            : (WorksheetPageMargins?)null;
        RestorePageLayoutMarginDrag();
        args.Pointer.Capture(null);
        args.Handled = true;
        if (margins is { } edited && !_isOpening && !_isSaving && edited != _session.ActiveSheet.PageMargins)
        {
            ExecutePageLayoutCommandWithShellRefresh(CreatePageLayoutCommandSession().PlanMargins(edited));
        }
    }

    private void RestorePageLayoutMarginDrag()
    {
        if (_pageLayoutMarginDrag is not { } drag)
            return;

        _pageLayoutMarginDrag = null;
        _sheetGridHost.Cursor = Cursor.Default;
        if (ReferenceEquals(drag.Ruler, _pageLayoutRuler))
            drag.Ruler.DrawMargins(drag.Ruler.Sheet.PageMargins);
    }

    private static WorksheetPageMargins CalculateDraggedPageMargins(PageLayoutMarginDrag drag, Point position)
    {
        var ruler = drag.Ruler;
        var delta = PageLayoutMarginCoordinate(position, drag.Edge) - drag.StartCoordinate;
        if (Math.Abs(delta) < 0.001 || ruler.Guide is not { } guide)
            return ruler.Sheet.PageMargins;

        var coordinate = drag.StartGuideCoordinate + delta;
        return PageMarginGuideLayoutPlanner.CalculateDraggedMargins(
            ruler.Sheet.PaperSize,
            ruler.Sheet.PageOrientation,
            ruler.Sheet.PageMargins,
            drag.Edge,
            guide,
            IsHorizontalPageLayoutMargin(drag.Edge) ? new LayoutPoint(coordinate, 0) : new LayoutPoint(0, coordinate));
    }

    private static double PageLayoutMarginCoordinate(Point point, WorksheetPageMarginEdge edge) =>
        IsHorizontalPageLayoutMargin(edge) ? point.X : point.Y;

    private static Cursor PageLayoutMarginCursor(WorksheetPageMarginEdge edge) =>
        new(IsHorizontalPageLayoutMargin(edge) ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);

    private static bool IsHorizontalPageLayoutMargin(WorksheetPageMarginEdge edge) =>
        edge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right;

    private sealed class PageLayoutMarginRuler : Canvas
    {
        private readonly ViewportModel _viewport;
        private readonly GridRange _printRange;
        private readonly double _rowHeaderWidth;
        private readonly double _columnHeaderHeight;
        private PageMarginRulerHandles _handles;

        public PageLayoutMarginRuler(
            Sheet sheet, ViewportModel viewport, GridRange printRange, double rowHeaderWidth, double columnHeaderHeight)
        {
            Sheet = sheet;
            _viewport = viewport;
            _printRange = printRange;
            _rowHeaderWidth = rowHeaderWidth;
            _columnHeaderHeight = columnHeaderHeight;
            AutomationProperties.SetAutomationId(this, "PageLayoutMarginRuler");
            DrawMargins(sheet.PageMargins);
        }

        public Sheet Sheet { get; }
        public PageMarginGuideLayout? Guide { get; private set; }

        public WorksheetPageMarginEdge? HitTestMargin(Point position) =>
            Guide is { } guide
                ? PageMarginGuideLayoutPlanner.HitTestGuide(
                    guide, new LayoutPoint(position.X, position.Y), _handles, showRulers: true, PageLayoutMarginHitZone)
                : null;

        public void DrawMargins(WorksheetPageMargins margins)
        {
            Guide = PageMarginGuideLayoutPlanner.CalculateGuide(
                _viewport, _printRange, _rowHeaderWidth, _columnHeaderHeight, Sheet.PaperSize, Sheet.PageOrientation, margins);
            Children.Clear();
            if (Guide is not { } guide)
                return;

            AddGuide(new Point(guide.MarginLeft, guide.Top), new Point(guide.MarginLeft, guide.Bottom));
            AddGuide(new Point(guide.MarginRight, guide.Top), new Point(guide.MarginRight, guide.Bottom));
            AddGuide(new Point(guide.Left, guide.MarginTop), new Point(guide.Right, guide.MarginTop));
            AddGuide(new Point(guide.Left, guide.MarginBottom), new Point(guide.Right, guide.MarginBottom));

            var bounds = new LayoutRect(guide.Left, guide.Top, guide.Right - guide.Left, guide.Bottom - guide.Top);
            _handles = PageMarginRulerLayoutPlanner.CalculateHandles(bounds, Sheet.PaperSize, Sheet.PageOrientation, margins);
            AddHandle(_handles.Left, "PageLayoutMarginLeft");
            AddHandle(_handles.Right, "PageLayoutMarginRight");
            AddHandle(_handles.Top, "PageLayoutMarginTop");
            AddHandle(_handles.Bottom, "PageLayoutMarginBottom");
        }

        private void AddGuide(Point start, Point end) =>
            Children.Add(new Line
            {
                StartPoint = start,
                EndPoint = end,
                Stroke = PageLayoutMarginGuideBrush,
                StrokeThickness = 1,
                StrokeDashArray = [3, 3],
            });

        private void AddHandle(LayoutRect rect, string automationId)
        {
            var handle = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Fill = PageLayoutMarginHandleFill,
                Stroke = PageLayoutMarginHandleBorder,
                StrokeThickness = 1,
            };
            AutomationProperties.SetAutomationId(handle, automationId);
            SetLeft(handle, rect.Left);
            SetTop(handle, rect.Top);
            Children.Add(handle);
        }
    }
}
