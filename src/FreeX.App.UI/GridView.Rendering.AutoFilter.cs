using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const double AutoFilterButtonSize = 15;
    private const double AutoFilterButtonMargin = 2;

    private static readonly Brush AutoFilterButtonBrush = CreateAutoFilterButtonBrush();
    private static readonly Pen AutoFilterButtonBorderPen = MakePen(MakeBrush(142, 153, 166), 1);
    private static readonly Brush AutoFilterGlyphBrush = MakeBrush(45, 55, 65);
    private static readonly Brush ActiveAutoFilterGlyphBrush = MakeBrush(15, 109, 140);

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
}
