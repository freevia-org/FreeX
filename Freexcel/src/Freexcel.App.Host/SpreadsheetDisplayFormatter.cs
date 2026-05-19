using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SpreadsheetDisplayFormatter
{
    public static string FormatCellReference(CellAddress address, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? $"R{address.Row}C{address.Col}"
            : address.ToA1();

    public static string FormatColumnReference(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? $"C{column}"
            : CellAddress.NumberToColumnName(column);

    public static string FormatRangeReference(CellAddress start, CellAddress end, bool useR1C1ReferenceStyle) =>
        start == end
            ? FormatCellReference(start, useR1C1ReferenceStyle)
            : $"{FormatCellReference(start, useR1C1ReferenceStyle)}:{FormatCellReference(end, useR1C1ReferenceStyle)}";

    public static string FormatFormulaBarText(Cell? cell, CellAddress address, bool useR1C1ReferenceStyle)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
        {
            var formula = useR1C1ReferenceStyle
                ? FormulaReferenceStyleService.ToR1C1(cell.FormulaText, address)
                : cell.FormulaText;
            return "=" + formula;
        }

        return FormatCellValue(cell?.Value);
    }

    public static string FormatCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => FormatDateTimeCellValue(dt),
        ErrorValue err => err.Code,
        _ => ""
    };

    private static string FormatDateTimeCellValue(DateTimeValue value)
    {
        try { return value.ToDateTime().ToString("yyyy-MM-dd"); }
        catch { return value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }
}
