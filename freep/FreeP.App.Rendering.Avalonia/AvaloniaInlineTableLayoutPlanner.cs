using Avalonia;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

internal readonly record struct AvaloniaInlineTableCellTextPlan(
    Rect Area,
    Point Origin);

internal readonly record struct AvaloniaInlineTableRotatedTextPlan(
    double LayoutWidthDip,
    Point Origin,
    Matrix Transform);

internal static class AvaloniaInlineTableLayoutPlanner
{
    internal const double PtToDip = 96.0 / 72.0;
    private const double DefaultCellInsetDip = 2;

    internal static AvaloniaInlineTableCellTextPlan PlanCellText(
        TableCell? cell,
        Rect bounds,
        double measuredTextHeight)
    {
        var area = GetTextArea(cell, bounds);
        return new(area, GetTextOrigin(cell, area, measuredTextHeight));
    }

    internal static Rect GetTextArea(TableCell? cell, Rect bounds)
    {
        double left = ResolveInset(cell?.InsetLeftPt);
        double top = ResolveInset(cell?.InsetTopPt);
        double right = ResolveInset(cell?.InsetRightPt);
        double bottom = ResolveInset(cell?.InsetBottomPt);
        var area = new Rect(
            bounds.X + left,
            bounds.Y + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
        return area;
    }

    internal static Point GetTextOrigin(
        TableCell? cell,
        Rect area,
        double measuredTextHeight)
    {
        double extraHeight = Math.Max(0, area.Height - measuredTextHeight);
        double offset = cell?.Anchor switch
        {
            TableCellAnchor.Middle => extraHeight / 2,
            TableCellAnchor.Bottom => extraHeight,
            _ => 0,
        };
        return new(area.X, area.Y + offset);
    }

    internal static bool IsRotatedText(TextVerticalType verticalType) =>
        verticalType is TextVerticalType.Vertical or TextVerticalType.Vertical270;

    internal static AvaloniaInlineTableRotatedTextPlan PlanRotatedText(
        TextVerticalType verticalType,
        Rect area,
        Size layoutSize,
        double flowTop)
    {
        if (!IsRotatedText(verticalType))
            throw new ArgumentException("The text type must use a quarter-turn orientation.", nameof(verticalType));

        double centerX = area.Center.X;
        double centerY = area.Center.Y;
        double angle = verticalType == TextVerticalType.Vertical270
            ? -Math.PI / 2
            : Math.PI / 2;
        var transform = Matrix.CreateTranslation(-centerX, -centerY)
            * Matrix.CreateRotation(angle)
            * Matrix.CreateTranslation(centerX, centerY);

        double originX = verticalType == TextVerticalType.Vertical
            ? centerX + flowTop - centerY
            : centerX + centerY - layoutSize.Width - flowTop;
        double originY = centerY - layoutSize.Height / 2;
        return new(
            Math.Max(1, area.Height),
            new Point(originX, originY),
            transform);
    }

    private static double ResolveInset(double? points) =>
        points is { } value && value >= 0
            ? value * PtToDip
            : DefaultCellInsetDip;
}

internal sealed record AvaloniaInlineTableCellLayout(
    int RowIndex,
    int ColumnIndex,
    TableCell? Cell,
    Rect Bounds,
    int SourceCellIndex);

internal sealed class AvaloniaInlineTableGridLayout
{
    private readonly IReadOnlyList<AvaloniaInlineTableCellLayout> _cells;
    private readonly InlineTableLayoutPlan _layout;
    private readonly Point _origin;

    private AvaloniaInlineTableGridLayout(
        IReadOnlyList<AvaloniaInlineTableCellLayout> cells,
        InlineTableLayoutPlan layout,
        Point origin)
    {
        _cells = cells;
        _layout = layout;
        _origin = origin;
    }

    internal IReadOnlyList<AvaloniaInlineTableCellLayout> Cells => _cells;

    internal static AvaloniaInlineTableGridLayout Create(
        InlineTableLayoutPlan layout,
        Point origin)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var cells = new List<AvaloniaInlineTableCellLayout>(layout.Cells.Count);
        foreach (var placement in layout.Cells)
        {
            var bounds = placement.Bounds;
            cells.Add(new AvaloniaInlineTableCellLayout(
                placement.RowIndex,
                placement.ColumnIndex,
                placement.Cell,
                new Rect(
                    origin.X + bounds.X,
                    origin.Y + bounds.Y,
                    bounds.Width,
                    bounds.Height),
                placement.SourceCellIndex));
        }

        return new AvaloniaInlineTableGridLayout(cells, layout, origin);
    }

    internal AvaloniaInlineTableCellLayout? GetCell(int rowIndex, int columnIndex)
    {
        var placement = _layout.ResolveCell(rowIndex, columnIndex);
        return placement is null
            ? null
            : _cells.FirstOrDefault(cell =>
                cell.RowIndex == placement.RowIndex
                && cell.ColumnIndex == placement.ColumnIndex);
    }

    internal AvaloniaInlineTableCellLayout? HitTest(Point point)
    {
        var placement = _layout.HitTest(
            point.X - _origin.X,
            point.Y - _origin.Y);
        return placement is null
            ? null
            : GetCell(placement.RowIndex, placement.ColumnIndex);
    }
}
