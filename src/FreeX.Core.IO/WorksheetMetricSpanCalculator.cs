using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class WorksheetMetricSpanCalculator
{
    public static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var column = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(column))
                width += sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

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
