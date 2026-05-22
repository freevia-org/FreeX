using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void SetPivotValueCell(
        Workbook workbook,
        Sheet sheet,
        CellAddress address,
        double value,
        PivotDataFieldModel dataField,
        PivotTableModel? pivotTable = null,
        bool isEmptyIntersection = false)
    {
        if (isEmptyIntersection && !string.IsNullOrWhiteSpace(pivotTable?.EmptyValueText))
        {
            sheet.SetCell(address, new TextValue(pivotTable.EmptyValueText));
            return;
        }

        var cell = Cell.FromValue(new NumberValue(value));
        if (TryResolveNumberFormat(workbook, dataField, out var formatCode) &&
            formatCode != CellStyle.Default.NumberFormat)
        {
            var style = CellStyle.Default.Clone();
            style.NumberFormat = formatCode;
            cell.StyleId = workbook.RegisterStyle(style);
        }

        sheet.SetCell(address, cell);
    }

    private static bool TryResolveNumberFormat(Workbook workbook, PivotDataFieldModel dataField, out string formatCode)
    {
        if (!string.IsNullOrWhiteSpace(dataField.NumberFormatCode))
        {
            formatCode = dataField.NumberFormatCode;
            return true;
        }

        if (dataField.NumberFormatId is >= 164 and var numberFormatId &&
            workbook.NumberFormatCatalog.TryGetValue(numberFormatId, out var catalogFormatCode) &&
            !string.IsNullOrWhiteSpace(catalogFormatCode))
        {
            formatCode = catalogFormatCode;
            return true;
        }

        return TryResolveBuiltInNumberFormat(dataField.NumberFormatId, out formatCode);
    }

    private static bool TryResolveBuiltInNumberFormat(int? numberFormatId, out string formatCode)
    {
        return BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out formatCode);
    }
}
