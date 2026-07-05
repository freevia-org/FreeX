using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Sums a sheet's real, non-uniform column/row pixel sizes (skipping hidden rows/columns), matching
/// the same convention <c>XlsxDrawingAnchorApplier</c> uses to derive <see cref="ChartModel.Left"/>/
/// <see cref="ChartModel.Top"/> when loading a drawing anchor. Print/preview layout needs the same
/// real-sheet pixel coordinate space to translate a chart's absolute anchor position into a printed
/// page's grid coordinates, so charts land where the on-screen (non-uniform) grid shows them.
/// </summary>
public static class ChartAnchorGeometry
{
    /// <summary>Sums the pixel width of <paramref name="count"/> columns starting at <paramref name="firstColumn"/>, skipping hidden columns.</summary>
    public static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var col = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(col))
                width += sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

    /// <summary>Sums the pixel height of <paramref name="count"/> rows starting at <paramref name="firstRow"/>, skipping hidden rows.</summary>
    public static double SumRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
        }

        return height;
    }

    /// <summary>
    /// Converts an absolute chart-anchor X offset from column 1 (in <see cref="SumColumnPixels"/>'s
    /// <c>width-in-chars * 8</c> convention) into the equivalent offset in the print grid's real pixel
    /// space (<see cref="ColumnWidthPixelMapper.ColumnWidthToPixels"/>'s <c>width*7+5</c> convention,
    /// which <see cref="Core.Model.PrintGridMeasurement.ColumnOffset"/> is built from). Chart anchors and
    /// the printed grid otherwise use two different, incompatible pixel-per-character conventions, so a
    /// raw offset computed in one space cannot be summed with a position computed in the other (the
    /// error grows with the number/width of columns between the sheet origin and the anchor). This walks
    /// whole columns in the anchor's *8 space to find which column the offset lands in and how far into
    /// it, then re-projects that same (column, fraction-of-column) position using each column's real
    /// grid-space pixel width, so the result lands in the same coordinate space as
    /// <see cref="Core.Model.PrintGridMeasurement.ColumnOffset"/>/<c>bodyGridLeft</c>.
    /// </summary>
    public static double ConvertColumnOffsetToGridSpace(Sheet sheet, double anchorSpaceOffsetX)
    {
        return ConvertOffsetToGridSpace(
            anchorSpaceOffsetX,
            col => sheet.IsColEffectivelyHidden(col),
            col => sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8,
            col => ColumnWidthPixelMapper.ColumnWidthToPixels(sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth)));
    }

    /// <summary>
    /// Converts an absolute chart-anchor Y offset from row 1 (in <see cref="SumRowPixels"/>'s real
    /// per-row-height convention) into the print grid's row pixel space. Rows use the same real pixel
    /// height in both the anchor and grid conventions (only columns have divergent conventions), so this
    /// is provided for symmetry/clarity at call sites and returns the offset unchanged aside from
    /// skipping hidden rows identically to <see cref="SumRowPixels"/>.
    /// </summary>
    public static double ConvertRowOffsetToGridSpace(Sheet sheet, double anchorSpaceOffsetY)
    {
        return ConvertOffsetToGridSpace(
            anchorSpaceOffsetY,
            row => sheet.IsRowEffectivelyHidden(row),
            row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight),
            row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight));
    }

    /// <summary>
    /// Converts a chart-anchor width (in <see cref="SumColumnPixels"/>'s <c>width-in-chars * 8</c>
    /// convention) that starts at anchor-space offset <paramref name="anchorSpaceOffsetX"/> into the
    /// equivalent extent in the print grid's pixel space. A chart's Width/Height (as derived by
    /// <c>XlsxDrawingAnchorApplier</c>) are anchor-space extents, not absolute offsets, so they cannot be
    /// converted with <see cref="ConvertColumnOffsetToGridSpace"/> directly (that would treat the extent
    /// as if it started at the sheet origin). Instead this converts both the anchor's left edge and its
    /// right edge (left + width) into grid space and returns their difference, so origin and extent stay
    /// in one consistent coordinate space end-to-end.
    /// </summary>
    public static double ConvertColumnExtentToGridSpace(Sheet sheet, double anchorSpaceOffsetX, double anchorSpaceWidth)
    {
        var left = ConvertColumnOffsetToGridSpace(sheet, anchorSpaceOffsetX);
        var right = ConvertColumnOffsetToGridSpace(sheet, anchorSpaceOffsetX + anchorSpaceWidth);
        return right - left;
    }

    /// <summary>
    /// Converts a chart-anchor height that starts at anchor-space offset <paramref name="anchorSpaceOffsetY"/>
    /// into the print grid's row pixel space, by converting the top and bottom edges separately and
    /// taking their difference. See <see cref="ConvertColumnExtentToGridSpace"/> for why an extent cannot
    /// be converted the same way as an absolute offset.
    /// </summary>
    public static double ConvertRowExtentToGridSpace(Sheet sheet, double anchorSpaceOffsetY, double anchorSpaceHeight)
    {
        var top = ConvertRowOffsetToGridSpace(sheet, anchorSpaceOffsetY);
        var bottom = ConvertRowOffsetToGridSpace(sheet, anchorSpaceOffsetY + anchorSpaceHeight);
        return bottom - top;
    }

    /// <summary>
    /// Walks whole 1-based indexes (columns or rows) starting at 1, accumulating each index's size in
    /// the "anchor space" convention until <paramref name="anchorSpaceOffset"/> is consumed, then
    /// re-accumulates the same whole indexes plus the leftover fraction using each index's size in the
    /// "grid space" convention. This keeps the (index, fraction-within-index) position identical while
    /// translating the unit system, so an anchor that landed, say, 40% of the way across column 5 still
    /// lands 40% of the way across column 5's real grid-space width.
    /// </summary>
    private static double ConvertOffsetToGridSpace(
        double anchorSpaceOffset,
        Func<uint, bool> isHidden,
        Func<uint, double> anchorSpaceSize,
        Func<uint, double> gridSpaceSize)
    {
        if (!double.IsFinite(anchorSpaceOffset) || anchorSpaceOffset <= 0)
            return 0;

        var remaining = anchorSpaceOffset;
        var gridSpaceOffset = 0.0;
        var index = 1u;

        // Consume whole indexes (skipping hidden ones, which contribute zero in both spaces) until the
        // remaining anchor-space offset is less than the current index's anchor-space size. Bounded by
        // the sheet's max row (the larger of the two max index constants, so it safely covers both the
        // column and row callers) so a degenerate all-hidden/zero-size run can never loop forever.
        while (index <= CellAddress.MaxRow)
        {
            if (isHidden(index))
            {
                index++;
                continue;
            }

            var anchorSize = anchorSpaceSize(index);
            if (anchorSize <= 0 || remaining < anchorSize)
                break;

            gridSpaceOffset += gridSpaceSize(index);
            remaining -= anchorSize;
            index++;
        }

        // The leftover 'remaining' is a fraction of the current (non-hidden) index's anchor-space size;
        // apply that same fraction to the index's grid-space size so the position within the index is
        // preserved across the unit conversion.
        var currentAnchorSize = anchorSpaceSize(index);
        if (currentAnchorSize > 0)
            gridSpaceOffset += remaining / currentAnchorSize * gridSpaceSize(index);

        return gridSpaceOffset;
    }
}
