using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreeX.App.Presentation.FormulaAuditing;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Trace arrows are an in-memory overlay only: we keep the active arrow set in this field and
    // redraw it on every overlay rebuild (RefreshShell -> BuildSheetGrid -> BuildDrawingObjectOverlay).
    // Persisting across recalcs is intentionally out of scope for v1.
    private readonly List<FormulaTraceArrow> _formulaTraceArrows = new();

    private void TraceFormulaPrecedents()
    {
        var workbook = _session.Workbook;
        var activeCell = _session.ActiveCell;

        var added = FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(
            workbook,
            activeCell,
            _formulaTraceArrows);

        if (added.Count == 0)
        {
            RefreshShell(UiText.Get("TableLoc_NoPrecedentsToTrace"));
            return;
        }

        _formulaTraceArrows.AddRange(added);
        RefreshShell(UiText.Format(
            added.Count == 1 ? "TableLoc_TracedPrecedentArrowsOne" : "TableLoc_TracedPrecedentArrowsMany",
            added.Count));
    }

    private void TraceFormulaDependents()
    {
        var workbook = _session.Workbook;
        var activeCell = _session.ActiveCell;

        var added = FormulaTraceArrowPlanner.GetNextDependentTraceArrows(
            workbook,
            activeCell,
            _formulaTraceArrows);

        if (added.Count == 0)
        {
            RefreshShell(UiText.Get("TableLoc_NoDependentsReference"));
            return;
        }

        _formulaTraceArrows.AddRange(added);
        RefreshShell(UiText.Format(
            added.Count == 1 ? "TableLoc_TracedDependentArrowsOne" : "TableLoc_TracedDependentArrowsMany",
            added.Count));
    }

    private void RemoveFormulaTraceArrows()
    {
        if (_formulaTraceArrows.Count == 0)
        {
            RefreshShell(UiText.Get("TableLoc_NoTraceArrowsToRemove"));
            return;
        }

        _formulaTraceArrows.Clear();
        RefreshShell(UiText.Get("TableLoc_RemovedAllTraceArrows"));
    }

    // Called from BuildDrawingObjectOverlay so the portable trace plan is painted onto the same
    // overlay Canvas that hosts charts and drawing objects.
    private void AddFormulaTraceArrowOverlay(Canvas overlay, ViewportModel viewport)
    {
        if (_formulaTraceArrows.Count == 0)
            return;

        var showHeadings = _session.IsShowingHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var activeSheetId = _session.ActiveSheet.Id;
        var projection = FormulaTraceViewportProjection.FromSequentialVisibleMetrics(
            showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0,
            showHeadings ? GetColumnHeaderHeight(viewport, zoomFactor) : 0,
            zoomFactor,
            MinimumDisplayedColumnWidth,
            MinimumDisplayedRowHeight);
        var layouts = FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            _formulaTraceArrows,
            activeSheetId,
            projection,
            FormulaTraceOverlayProfiles.Avalonia);

        if (layouts.Count == 0)
            return;

        var arrowVisual = new FormulaTraceArrowControl(layouts)
        {
            Width = overlay.Width,
            Height = overlay.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(arrowVisual, 0);
        Canvas.SetTop(arrowVisual, 0);
        overlay.Children.Add(arrowVisual);
    }

    private sealed class FormulaTraceArrowControl : Control
    {
        private static readonly FormulaTraceOverlayStyle Style = FormulaTraceOverlayProfiles.Avalonia.Style;
        private static readonly IBrush PrecedentBrush = CreateBrush(Style.PrecedentColor);
        private static readonly IBrush DependentBrush = CreateBrush(Style.DependentColor);

        private readonly IReadOnlyList<FormulaTraceArrowLayout> _layouts;

        public FormulaTraceArrowControl(IReadOnlyList<FormulaTraceArrowLayout> layouts)
        {
            _layouts = layouts;
        }

        public override void Render(DrawingContext context)
        {
            foreach (var layout in _layouts)
            {
                var brush = ResolveBrush(Style.ResolveColor(layout.ArrowKind));
                var pen = new Pen(brush, Style.StrokeWidth);
                var start = ToAvaloniaPoint(layout.Start);
                var end = ToAvaloniaPoint(layout.End);

                context.DrawLine(pen, start, end);

                // Filled dot at the source (Excel renders a bullet at the precedent/source end).
                context.DrawEllipse(brush, null, start, Style.SourceMarkerRadius, Style.SourceMarkerRadius);

                // Filled triangular arrowhead pointing at the target cell.
                DrawArrowHead(context, brush, layout.Start, layout.End);
            }
        }

        private static void DrawArrowHead(
            DrawingContext context,
            IBrush brush,
            LayoutPoint start,
            LayoutPoint end)
        {
            var arrowHead = FormulaTraceOverlayGeometryPlanner.CalculateArrowHead(start, end, Style);
            if (!arrowHead.IsVisible)
                return;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToAvaloniaPoint(arrowHead.Tip), isFilled: true);
                ctx.LineTo(ToAvaloniaPoint(arrowHead.Left));
                ctx.LineTo(ToAvaloniaPoint(arrowHead.Right));
                ctx.EndFigure(isClosed: true);
            }

            context.DrawGeometry(brush, null, geometry);
        }

        private static IBrush CreateBrush(FormulaTraceColor color) =>
            new ImmutableSolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

        private static IBrush ResolveBrush(FormulaTraceColor color) =>
            color == Style.DependentColor ? DependentBrush : PrecedentBrush;

        private static Point ToAvaloniaPoint(LayoutPoint point) => new(point.X, point.Y);
    }
}
