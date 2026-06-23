using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const double AutoFilterButtonSize = 15;
    private const double AutoFilterButtonMargin = 2;
    private const double PivotHeaderDropdownButtonSize = 17;
    private const double PivotHeaderDropdownButtonMargin = 1;
    private const double PivotExpandCollapseButtonSize = 10;
    private const double PivotExpandCollapseButtonReserve = 15;

    private static readonly Brush AutoFilterButtonBrush = CreateAutoFilterButtonBrush();
    private static readonly Pen AutoFilterButtonBorderPen = MakePen(MakeBrush(142, 153, 166), 1);
    private static readonly Brush AutoFilterGlyphBrush = MakeBrush(45, 55, 65);
    private static readonly Brush ActiveAutoFilterGlyphBrush = MakeBrush(15, 109, 140);
    private static readonly Brush PivotHeaderDropdownButtonBrush = CreatePivotHeaderDropdownButtonBrush();
    private static readonly Pen PivotHeaderDropdownButtonBorderPen = MakePen(MakeBrush(154, 164, 174), 1);
    private static readonly Brush PivotHeaderDropdownGlyphBrush = MakeBrush(66, 76, 86);
    private static readonly Brush PivotExpandCollapseButtonBrush = MakeBrush(248, 248, 248);
    private static readonly Pen PivotExpandCollapseBorderPen = MakePen(MakeBrush(174, 174, 174), 1);
    private static readonly Pen PivotExpandCollapseGlyphPen = MakePen(MakeBrush(96, 96, 96), 1);

    private void RenderAutoFilterButtons(DrawingContext dc)
    {
        if (Viewport is null || AutoFilterRange is not { } range)
            return;

        var headerRow = FindRowMetric(Viewport.RowMetrics, range.Start.Row);
        if (headerRow is null)
            return;

        foreach (var column in Viewport.ColMetrics)
        {
            if (column.Col < range.Start.Col || column.Col > range.End.Col)
                continue;

            var rect = GetAutoFilterButtonRect(headerRow, column);
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            var isActive = ActiveAutoFilterColumns?.Contains(column.Col - range.Start.Col) == true;
            dc.DrawRectangle(AutoFilterButtonBrush, AutoFilterButtonBorderPen, rect);
            DrawAutoFilterGlyph(dc, rect, isActive);
        }
    }

    private bool TryHitTestAutoFilterButton(Point pos, out CellAddress headerCell)
    {
        headerCell = default;
        if (Viewport is null || AutoFilterRange is not { } range)
            return false;

        var headerRow = FindRowMetric(Viewport.RowMetrics, range.Start.Row);
        if (headerRow is null)
            return false;

        foreach (var column in Viewport.ColMetrics)
        {
            if (column.Col < range.Start.Col || column.Col > range.End.Col)
                continue;

            if (!RectHitTest.ContainsInclusive(GetAutoFilterButtonRect(headerRow, column), pos))
                continue;

            headerCell = new CellAddress(range.Start.Sheet, range.Start.Row, column.Col);
            return true;
        }

        return false;
    }

    private void RenderPivotHeaderDropdownButtons(DrawingContext dc)
    {
        if (Viewport is null || PivotHeaderDropdowns is not { Count: > 0 } buttons)
            return;

        foreach (var button in buttons)
        {
            if (GetDropdownButtonRect(button.HeaderCell) is not { } rect ||
                rect.Width <= 0 ||
                rect.Height <= 0)
            {
                continue;
            }

            dc.DrawRectangle(PivotHeaderDropdownButtonBrush, PivotHeaderDropdownButtonBorderPen, rect);
            DrawPivotHeaderDropdownGlyph(dc, rect, button.IsActive);
        }
    }

    private void RenderPivotRowLabelAdornments(DrawingContext dc)
    {
        if (Viewport is null || PivotRowLabelAdornments is not { Count: > 0 } adornments)
            return;

        foreach (var adornment in adornments)
        {
            if (!adornment.ShowExpandCollapseButton ||
                FindRowMetric(Viewport.RowMetrics, adornment.Cell.Row) is not { } row ||
                FindColMetric(Viewport.ColMetrics, adornment.Cell.Col) is not { } column)
            {
                continue;
            }

            var size = Math.Min(PivotExpandCollapseButtonSize, Math.Max(0, Math.Min(row.Height, column.Width) - 4));
            if (size <= 0)
                continue;

            var indent = Math.Clamp(adornment.IndentLevel, 0, 15) * 8.0;
            var rect = new Rect(
                ActualRowHeaderWidth + column.LeftOffset + 2 + indent,
                EffectiveColHeaderHeight + row.TopOffset + Math.Max(0, (row.Height - size) / 2),
                size,
                size);
            DrawPivotExpandCollapseButton(dc, rect, adornment.IsExpanded);
        }
    }

    private bool TryHitTestPivotHeaderDropdownButton(Point pos, out CellAddress headerCell)
    {
        headerCell = default;
        if (Viewport is null || PivotHeaderDropdowns is not { Count: > 0 } buttons)
            return false;

        foreach (var button in buttons)
        {
            if (GetDropdownButtonRect(button.HeaderCell) is not { } rect)
                continue;

            if (!RectHitTest.ContainsInclusive(rect, pos))
                continue;

            headerCell = button.HeaderCell;
            return true;
        }

        return false;
    }

    private double GetPivotRowLabelAdornmentTextPadding(uint row, uint col)
    {
        if (PivotRowLabelAdornments is not { Count: > 0 } adornments)
            return 0;

        foreach (var adornment in adornments)
        {
            if (adornment.Cell.Row == row &&
                adornment.Cell.Col == col &&
                adornment.ReserveTextPadding)
            {
                return PivotExpandCollapseButtonReserve;
            }
        }

        return 0;
    }

    private Rect? GetDropdownButtonRect(CellAddress cell)
    {
        if (Viewport is null)
            return null;

        var row = FindRowMetric(Viewport.RowMetrics, cell.Row);
        var column = FindColMetric(Viewport.ColMetrics, cell.Col);
        return row is null || column is null
            ? null
            : GetPivotHeaderDropdownButtonRect(row, column, ActualRowHeaderWidth, EffectiveColHeaderHeight);
    }

    internal static Rect GetPivotHeaderDropdownButtonRect(
        RowMetric row,
        ColMetric column,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var size = Math.Min(
            PivotHeaderDropdownButtonSize,
            Math.Max(0, Math.Min(row.Height, column.Width) - PivotHeaderDropdownButtonMargin * 2));
        if (size <= 0)
            return Rect.Empty;

        return new Rect(
            rowHeaderWidth + column.LeftOffset + Math.Max(0, column.Width - size - PivotHeaderDropdownButtonMargin),
            columnHeaderHeight + row.TopOffset + Math.Max(0, (row.Height - size) / 2),
            size,
            size);
    }

    private Rect GetAutoFilterButtonRect(RowMetric row, ColMetric column)
    {
        var size = Math.Min(AutoFilterButtonSize, Math.Max(0, Math.Min(row.Height, column.Width) - AutoFilterButtonMargin * 2));
        if (size <= 0)
            return Rect.Empty;

        return new Rect(
            ActualRowHeaderWidth + column.LeftOffset + Math.Max(0, column.Width - size - AutoFilterButtonMargin),
            EffectiveColHeaderHeight + row.TopOffset + Math.Max(0, (row.Height - size) / 2),
            size,
            size);
    }

    private static void DrawPivotExpandCollapseButton(DrawingContext dc, Rect rect, bool isExpanded)
    {
        dc.DrawRectangle(PivotExpandCollapseButtonBrush, PivotExpandCollapseBorderPen, rect);
        var centerY = Math.Round(rect.Top + rect.Height / 2) + 0.5;
        var left = Math.Round(rect.Left + 2.5) + 0.5;
        var right = Math.Round(rect.Right - 2.5) + 0.5;
        dc.DrawLine(PivotExpandCollapseGlyphPen, new Point(left, centerY), new Point(right, centerY));
        if (!isExpanded)
        {
            var centerX = Math.Round(rect.Left + rect.Width / 2) + 0.5;
            var top = Math.Round(rect.Top + 2.5) + 0.5;
            var bottom = Math.Round(rect.Bottom - 2.5) + 0.5;
            dc.DrawLine(PivotExpandCollapseGlyphPen, new Point(centerX, top), new Point(centerX, bottom));
        }
    }

    private static void DrawPivotHeaderDropdownGlyph(DrawingContext dc, Rect rect, bool isActive)
    {
        if (isActive)
        {
            DrawAutoFilterGlyph(dc, rect, isActive: true);
            return;
        }

        var centerX = rect.Left + rect.Width / 2;
        var centerY = rect.Top + rect.Height / 2 + 1;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(centerX - 4, centerY - 2), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(centerX + 4, centerY - 2), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(centerX, centerY + 3), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(PivotHeaderDropdownGlyphBrush, null, geometry);
    }

    private static void DrawAutoFilterGlyph(DrawingContext dc, Rect rect, bool isActive)
    {
        var centerX = rect.Left + rect.Width / 2;
        if (isActive)
        {
            var top = rect.Top + Math.Max(3, rect.Height * 0.25);
            var bottom = rect.Bottom - Math.Max(3, rect.Height * 0.22);
            var activeGeometry = new StreamGeometry();
            using (var ctx = activeGeometry.Open())
            {
                ctx.BeginFigure(new Point(centerX - 4, top), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(centerX + 4, top), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(centerX + 1.5, top + 4), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(centerX + 1.5, bottom), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(centerX - 1.5, bottom), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(centerX - 1.5, top + 4), isStroked: true, isSmoothJoin: false);
            }

            activeGeometry.Freeze();
            dc.DrawGeometry(ActiveAutoFilterGlyphBrush, null, activeGeometry);
            return;
        }

        var centerY = rect.Top + rect.Height / 2 + 1;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(centerX - 3, centerY - 2), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(centerX + 3, centerY - 2), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(centerX, centerY + 2), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(AutoFilterGlyphBrush, null, geometry);
    }

    private static Brush CreateAutoFilterButtonBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(252, 252, 252),
            Color.FromRgb(225, 232, 238),
            90);
        brush.Freeze();
        return brush;
    }

    private static Brush CreatePivotHeaderDropdownButtonBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(249, 251, 253),
            Color.FromRgb(219, 228, 237),
            90);
        brush.Freeze();
        return brush;
    }
}
