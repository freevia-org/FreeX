using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public sealed partial class ViewportService
{
    public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return null;
        if (zoom <= 0) return null;

        double targetX = x / zoom;
        double targetY = y / zoom;
        if (targetX < 0 || targetY < 0) return null;

        var row = HitTestRow(sheet, targetY);
        var col = HitTestColumn(sheet, targetX);
        if (row is null || col is null) return null;

        return new CellAddress(sheetId, row.Value, col.Value);
    }

    private static uint? HitTestRow(Sheet sheet, double y)
    {
        if (sheet.RowHeights.Count == 0 &&
            sheet.HiddenRows.Count == 0 &&
            sheet.FilterHiddenRows.Count == 0 &&
            sheet.GroupHiddenRows.Count == 0)
        {
            if (sheet.DefaultRowHeight <= 0) return null;
            var row = (uint)(y / sheet.DefaultRowHeight) + 1;
            return row <= CellAddress.MaxRow ? row : null;
        }

        double top = 0;
        for (uint row = 1; row <= CellAddress.MaxRow; row++)
        {
            if (IsRowHidden(sheet, row)) continue;

            var height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
            if (y < top + height)
                return row;

            top += height;
        }

        return null;
    }

    private static uint? HitTestColumn(Sheet sheet, double x)
    {
        if (sheet.ColumnWidths.Count == 0 &&
            sheet.HiddenCols.Count == 0 &&
            sheet.GroupHiddenCols.Count == 0)
        {
            var pixelWidth = sheet.DefaultColumnWidth * 8;
            if (pixelWidth <= 0) return null;
            var col = (uint)(x / pixelWidth) + 1;
            return col <= CellAddress.MaxCol ? col : null;
        }

        double left = 0;
        for (uint col = 1; col <= CellAddress.MaxCol; col++)
        {
            if (sheet.IsColEffectivelyHidden(col)) continue;

            var width = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
            if (x < left + width)
                return col;

            left += width;
        }

        return null;
    }
}
