using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    // FIX 2: Overload that accepts double? — null means write a blank cell (Excel parity
    // for min/max/product/stddev/var over a group with no numeric values).
    private static void SetPivotValueCell(
        Workbook workbook,
        Sheet sheet,
        CellAddress address,
        double? value,
        PivotDataFieldModel dataField,
        PivotTableModel? pivotTable = null,
        bool isEmptyIntersection = false)
    {
        if (value is null)
        {
            SetPivotCell(sheet, address, BlankValue.Instance);
            return;
        }

        SetPivotValueCell(workbook, sheet, address, value.Value, dataField, pivotTable, isEmptyIntersection);
    }

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
            SetPivotCell(sheet, address, new TextValue(pivotTable.EmptyValueText));
            return;
        }

        var cell = Cell.FromValue(new NumberValue(value));
        if (pivotTable?.ApplyNumberFormats != false &&
            TryResolveNumberFormat(workbook, dataField, out var formatCode) &&
            formatCode != CellStyle.Default.NumberFormat)
        {
            var style = CellStyle.Default.Clone();
            style.NumberFormat = formatCode;
            cell.StyleId = workbook.RegisterStyle(style);
        }

        SetPivotCell(sheet, address, cell);
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
