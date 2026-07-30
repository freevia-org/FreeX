using Free.Shared.AppServices;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free helper that maps a point in slide DIP coordinates to a table cell (row, col).
/// The FreeP adapter supplies table model geometry; shared app services own the neutral hit policy.
/// </summary>
public static class TableCellHitTester
{
    // DrawingML EMU per 96-DPI DIP.
    private const double EmuPerDip = DrawingMlCoordinateUnits.EmuPerPixel;

    /// <summary>
    /// Returns the (row, col) of the cell that contains (<paramref name="slidePtX"/>, <paramref name="slidePtY"/>),
    /// or null if the point is outside the table frame. HMerge / VMerge continuations resolve to their anchor.
    /// </summary>
    public static (int Row, int Col)? HitTest(
        SlideShape tableShape,
        double slidePtX,
        double slidePtY)
    {
        if (tableShape.Table is null)
            return null;

        var table = tableShape.Table;
        var frameX = tableShape.OffsetXEmu / EmuPerDip;
        var frameY = tableShape.OffsetYEmu / EmuPerDip;
        var frameWidth = tableShape.ExtentCxEmu / EmuPerDip;
        var frameHeight = tableShape.ExtentCyEmu / EmuPerDip;
        var frameCenterX = frameX + frameWidth / 2.0;
        var frameCenterY = frameY + frameHeight / 2.0;
        var localPoint = ShapeTransformPlanner.InverseTransformPoint(
            frameCenterX,
            frameCenterY,
            slidePtX,
            slidePtY,
            tableShape.RotationDeg,
            tableShape.FlipH,
            tableShape.FlipV);
        var hit = TableGridGeometryPlanner.HitTest(
            BuildGeometry(table),
            frameX,
            frameY,
            localPoint.X,
            localPoint.Y);

        return hit is { } cell
            ? (cell.Row, cell.Col)
            : null;
    }

    /// <summary>
    /// Returns the DIP-coordinate bounding rect of the cell at (<paramref name="row"/>, <paramref name="col"/>)
    /// within <paramref name="tableShape"/>, accounting for GridSpan / RowSpan.
    /// </summary>
    public static CellRectDip? GetCellRect(SlideShape tableShape, int row, int col)
    {
        if (tableShape.Table is null)
            return null;

        var table = tableShape.Table;
        var frameX = tableShape.OffsetXEmu / EmuPerDip;
        var frameY = tableShape.OffsetYEmu / EmuPerDip;
        var rect = TableGridGeometryPlanner.GetCellRect(
            BuildGeometry(table),
            frameX,
            frameY,
            row,
            col);

        return rect is { } cellRect
            ? new CellRectDip(cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height)
            : null;
    }

    private static TableGridGeometry BuildGeometry(TableShape table) =>
        new(
            table.ColumnWidthsEmu.Select(width => width / EmuPerDip).ToList(),
            table.Rows.Select(row => row.HeightEmu / EmuPerDip).ToList(),
            table.Rows
                .Select(row => (IReadOnlyList<TableGridCell>)row.Cells
                    .Select(cell => new TableGridCell(
                        cell.GridSpan,
                        cell.RowSpan,
                        cell.HMerge,
                        cell.VMerge))
                    .ToList())
                .ToList());
}

/// <summary>DIP-coordinate bounding rectangle for a table cell.</summary>
public readonly struct CellRectDip
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public CellRectDip(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
