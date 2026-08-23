using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public interface ITableCellBorderRenderSink
{
    void Render(ResolvedOutline outline, LayoutPoint start, LayoutPoint end);
}

public static class TableCellBorderRenderSequence
{
    public static void Dispatch<TSink>(TableCellOp cell, ref TSink sink)
        where TSink : struct, ITableCellBorderRenderSink
    {
        ArgumentNullException.ThrowIfNull(cell);

        var bounds = cell.BoundsDip;
        sink.Render(
            cell.BorderTop,
            new LayoutPoint(bounds.Left, bounds.Top),
            new LayoutPoint(bounds.Right, bounds.Top));
        sink.Render(
            cell.BorderBottom,
            new LayoutPoint(bounds.Left, bounds.Bottom),
            new LayoutPoint(bounds.Right, bounds.Bottom));
        sink.Render(
            cell.BorderLeft,
            new LayoutPoint(bounds.Left, bounds.Top),
            new LayoutPoint(bounds.Left, bounds.Bottom));
        sink.Render(
            cell.BorderRight,
            new LayoutPoint(bounds.Right, bounds.Top),
            new LayoutPoint(bounds.Right, bounds.Bottom));
        sink.Render(
            cell.BorderDiagonalDown,
            new LayoutPoint(bounds.Left, bounds.Top),
            new LayoutPoint(bounds.Right, bounds.Bottom));
        sink.Render(
            cell.BorderDiagonalUp,
            new LayoutPoint(bounds.Left, bounds.Bottom),
            new LayoutPoint(bounds.Right, bounds.Top));
    }
}
