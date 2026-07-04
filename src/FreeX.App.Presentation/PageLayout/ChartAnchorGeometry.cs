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
}
