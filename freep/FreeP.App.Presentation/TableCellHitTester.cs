using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free helper that maps a point in slide DIP coordinates to a table cell (row, col).
///
/// The geometry is computed the same way as <see cref="FreeP.App.Compositor.SlideCompositor"/>:
/// cumulative column X offsets from the left edge of the table frame, cumulative row Y offsets
/// from the top edge.  HMerge / VMerge cells are skipped — the click registers on the anchor.
///
/// Unit-testable without STA / WPF.
/// </summary>
public static class TableCellHitTester
{
    // 1 EMU = 1/9525 DIP
    private const double EmuPerDip = 9525.0;

    /// <summary>
    /// Returns the (row, col) of the cell that contains (<paramref name="slidePtX"/>, <paramref name="slidePtY"/>),
    /// or null if the point is outside the table frame.
    ///
    /// HMerge / VMerge cells return the index of the anchor cell that covers them (top-left of
    /// the merged region).  GridSpan / RowSpan geometry is NOT traced here; the caller just gets
    /// the logical (row, col) of the physical slot that the point falls in.
    /// </summary>
    public static (int Row, int Col)? HitTest(
        SlideShape tableShape,
        double slidePtX,
        double slidePtY)
    {
        if (tableShape.Table is null) return null;

        var table   = tableShape.Table;
        double frameX = tableShape.OffsetXEmu / EmuPerDip;
        double frameY = tableShape.OffsetYEmu / EmuPerDip;

        // Quick frame reject.
        double totalW = table.ColumnWidthsEmu.Sum() / EmuPerDip;
        double totalH = table.Rows.Sum(r => r.HeightEmu) / EmuPerDip;

        if (slidePtX < frameX || slidePtX > frameX + totalW) return null;
        if (slidePtY < frameY || slidePtY > frameY + totalH) return null;

        // Find column.
        double runX = frameX;
        int col = -1;
        for (int c = 0; c < table.ColumnWidthsEmu.Count; c++)
        {
            double colW = table.ColumnWidthsEmu[c] / EmuPerDip;
            if (slidePtX <= runX + colW)
            {
                col = c;
                break;
            }
            runX += colW;
        }
        if (col < 0) col = table.ColumnWidthsEmu.Count - 1;

        // Find row.
        double runY = frameY;
        int row = -1;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            double rowH = table.Rows[r].HeightEmu / EmuPerDip;
            if (slidePtY <= runY + rowH)
            {
                row = r;
                break;
            }
            runY += rowH;
        }
        if (row < 0) row = table.Rows.Count - 1;

        if (row < 0 || col < 0) return null;

        // If the cell at (row, col) is an HMerge / VMerge continuation, find its anchor.
        (row, col) = FindAnchor(table, row, col);

        return (row, col);
    }

    /// <summary>
    /// Returns the DIP-coordinate bounding rect of the cell at (<paramref name="row"/>, <paramref name="col"/>)
    /// within <paramref name="tableShape"/>, accounting for GridSpan / RowSpan.
    /// Returns null when indices are out of bounds.
    /// </summary>
    public static CellRectDip? GetCellRect(SlideShape tableShape, int row, int col)
    {
        if (tableShape.Table is null) return null;
        var table = tableShape.Table;
        if (row < 0 || row >= table.Rows.Count) return null;
        if (col < 0 || col >= table.ColumnWidthsEmu.Count) return null;

        double frameX = tableShape.OffsetXEmu / EmuPerDip;
        double frameY = tableShape.OffsetYEmu / EmuPerDip;

        // Column X.
        double x = frameX;
        for (int c = 0; c < col; c++)
            x += table.ColumnWidthsEmu[c] / EmuPerDip;

        // Row Y.
        double y = frameY;
        for (int r = 0; r < row; r++)
            y += table.Rows[r].HeightEmu / EmuPerDip;

        var cell = table.Rows[row].Cells[col];
        int gridSpan = Math.Max(1, cell.GridSpan);
        int rowSpan  = Math.Max(1, cell.RowSpan);

        double w = 0;
        for (int sc = col; sc < col + gridSpan && sc < table.ColumnWidthsEmu.Count; sc++)
            w += table.ColumnWidthsEmu[sc] / EmuPerDip;

        double h = 0;
        for (int sr = row; sr < row + rowSpan && sr < table.Rows.Count; sr++)
            h += table.Rows[sr].HeightEmu / EmuPerDip;

        return new CellRectDip(x, y, w, h);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks backward from (row, col) to find the anchor cell that covers it.
    /// For HMerge: scan left in the same row. For VMerge: scan up in the same column.
    /// </summary>
    private static (int row, int col) FindAnchor(TableShape table, int row, int col)
    {
        // Scan up first (VMerge means we're in a row below the anchor row).
        int r = row;
        while (r >= 0 && r < table.Rows.Count)
        {
            var cell = table.Rows[r].Cells.ElementAtOrDefault(col);
            if (cell is null) break;
            if (!cell.VMerge) break;
            r--;
        }
        row = Math.Max(0, r);

        // Now scan left for HMerge within the resolved row.
        int c = col;
        while (c >= 0)
        {
            var cell = table.Rows[row].Cells.ElementAtOrDefault(c);
            if (cell is null) break;
            if (!cell.HMerge) break;
            c--;
        }
        col = Math.Max(0, c);

        return (row, col);
    }
}

/// <summary>DIP-coordinate bounding rectangle for a table cell.</summary>
public readonly struct CellRectDip
{
    public double X      { get; }
    public double Y      { get; }
    public double Width  { get; }
    public double Height { get; }

    public CellRectDip(double x, double y, double w, double h)
    {
        X = x; Y = y; Width = w; Height = h;
    }
}
