using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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

    // Called from BuildDrawingObjectOverlay so the arrows are painted onto the same overlay Canvas
    // that hosts charts / drawing objects, using the same coordinate mapping (TryGetDisplayedCellBounds).
    private void AddFormulaTraceArrowOverlay(Canvas overlay, ViewportModel viewport)
    {
        if (_formulaTraceArrows.Count == 0)
            return;

        var showHeadings = _session.IsShowingHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var activeSheetId = _session.ActiveSheet.Id;

        var segments = new List<FormulaTraceArrowSegment>();
        foreach (var arrow in _formulaTraceArrows)
        {
            // The viewport renders a single sheet; only draw arrows whose endpoints are both on it.
            if (arrow.From.Sheet != activeSheetId || arrow.To.Sheet != activeSheetId)
                continue;

            if (!TryGetDisplayedCellBounds(viewport, arrow.From, showHeadings, zoomFactor,
                    out var fromLeft, out var fromTop, out var fromWidth, out var fromHeight))
                continue;

            if (!TryGetDisplayedCellBounds(viewport, arrow.To, showHeadings, zoomFactor,
                    out var toLeft, out var toTop, out var toWidth, out var toHeight))
                continue;

            var start = new Point(fromLeft + fromWidth / 2, fromTop + fromHeight / 2);
            var end = new Point(toLeft + toWidth / 2, toTop + toHeight / 2);
            if (start == end)
                continue;

            segments.Add(new FormulaTraceArrowSegment(start, end, arrow.Kind));
        }

        if (segments.Count == 0)
            return;

        var arrowVisual = new FormulaTraceArrowControl(segments)
        {
            Width = overlay.Width,
            Height = overlay.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(arrowVisual, 0);
        Canvas.SetTop(arrowVisual, 0);
        overlay.Children.Add(arrowVisual);
    }

    private readonly record struct FormulaTraceArrowSegment(Point Start, Point End, FormulaTraceArrowKind Kind);

    private sealed class FormulaTraceArrowControl : Control
    {
        private static readonly IBrush PrecedentBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 102, 51));
        private static readonly IBrush DependentBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 86, 179));
        private const double ArrowHeadLength = 10;
        private const double ArrowHeadHalfWidth = 5;
        private const double DotRadius = 3;

        private readonly IReadOnlyList<FormulaTraceArrowSegment> _segments;

        public FormulaTraceArrowControl(IReadOnlyList<FormulaTraceArrowSegment> segments)
        {
            _segments = segments;
        }

        public override void Render(DrawingContext context)
        {
            foreach (var segment in _segments)
            {
                var brush = segment.Kind == FormulaTraceArrowKind.Dependent ? DependentBrush : PrecedentBrush;
                var pen = new Pen(brush, 1.5);

                context.DrawLine(pen, segment.Start, segment.End);

                // Filled dot at the source (Excel renders a bullet at the precedent/source end).
                context.DrawEllipse(brush, null, segment.Start, DotRadius, DotRadius);

                // Filled triangular arrowhead pointing at the target cell.
                DrawArrowHead(context, brush, segment.Start, segment.End);
            }
        }

        private static void DrawArrowHead(DrawingContext context, IBrush brush, Point start, Point end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001)
                return;

            var ux = dx / length;
            var uy = dy / length;

            // Base of the arrowhead, ArrowHeadLength back from the tip along the line.
            var baseX = end.X - ux * ArrowHeadLength;
            var baseY = end.Y - uy * ArrowHeadLength;

            // Perpendicular unit vector.
            var px = -uy;
            var py = ux;

            var left = new Point(baseX + px * ArrowHeadHalfWidth, baseY + py * ArrowHeadHalfWidth);
            var right = new Point(baseX - px * ArrowHeadHalfWidth, baseY - py * ArrowHeadHalfWidth);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(end, isFilled: true);
                ctx.LineTo(left);
                ctx.LineTo(right);
                ctx.EndFigure(isClosed: true);
            }

            context.DrawGeometry(brush, null, geometry);
        }
    }
}
