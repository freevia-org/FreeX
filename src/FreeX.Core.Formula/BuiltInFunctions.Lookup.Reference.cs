using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Row(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ctx.CurrentCellAddress is { } cell
            ? new NumberValue(cell.Row)
            : ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return RowNumbers(rv);
        return ErrorValue.Value;
    }

    private static ScalarValue Column(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ctx.CurrentCellAddress is { } cell
            ? new NumberValue(cell.Col)
            : ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return ColumnNumbers(rv);
        return ErrorValue.Value;
    }

    private static ScalarValue RowNumbers(RangeValue range)
    {
        if (range.RowCount == 1) return new NumberValue(range.StartRow);

        var cells = new ScalarValue[range.RowCount, 1];
        for (int r = 0; r < range.RowCount; r++)
            cells[r, 0] = new NumberValue(range.StartRow + r);
        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static ScalarValue ColumnNumbers(RangeValue range)
    {
        if (range.ColCount == 1) return new NumberValue(range.StartCol);

        var cells = new ScalarValue[1, range.ColCount];
        for (int c = 0; c < range.ColCount; c++)
            cells[0, c] = new NumberValue(range.StartCol + c);
        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static ScalarValue Rows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.RowCount);
        return new NumberValue(1);
    }

    private static ScalarValue Columns(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue rv) return new NumberValue(rv.ColCount);
        return new NumberValue(1);
    }

    private static ScalarValue Areas(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue) return new NumberValue(1);
        return ErrorValue.Value;
    }

    private static ScalarValue SheetFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0)
            return TryGetCurrentSheetIndex(ctx, out var currentIndex)
                ? new NumberValue(currentIndex)
                : ErrorValue.NA;

        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range)
        {
            var sheetName = range.SheetName ?? ctx.CurrentSheet?.Name;
            return sheetName is not null && TryGetSheetIndex(ctx, sheetName, out var rangeIndex)
                ? new NumberValue(rangeIndex)
                : ErrorValue.NA;
        }

        var requestedSheetName = ToText(args[0]);
        return TryGetSheetIndex(ctx, requestedSheetName, out var sheetIndex)
            ? new NumberValue(sheetIndex)
            : ErrorValue.NA;
    }

    private static ScalarValue SheetsFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0)
        {
            if (ctx.CurrentWorkbook is not null) return new NumberValue(ctx.CurrentWorkbook.SheetCount);
            return ctx.CurrentSheet is not null ? new NumberValue(1) : ErrorValue.NA;
        }

        if (args[0] is ErrorValue e) return e;
        return args[0] is RangeValue range ? SheetSpanCount(range, ctx) : ErrorValue.Value;
    }

    /// <summary>
    /// Counts the sheets a reference spans. Per Excel's documented SHEETS behavior ("If reference
    /// refers to multiple sheets, SHEETS returns the number of sheets referred to"), a 3-D sheet-span
    /// reference (e.g. Sheet1:Sheet3!A1) must count every sheet from its start to its end sheet
    /// inclusive, not just report 1. RangeValue only carries a single SheetName, so a span is encoded
    /// as "Start:End" in that field; Excel forbids ':' in an actual sheet name, so seeing one there
    /// unambiguously means a span rather than a plain single-sheet reference.
    /// </summary>
    private static ScalarValue SheetSpanCount(RangeValue range, IEvalContext ctx)
    {
        var sheetName = range.SheetName;
        if (sheetName is null) return new NumberValue(1);

        var colonIndex = sheetName.IndexOf(':');
        if (colonIndex < 0) return new NumberValue(1);

        var startName = sheetName[..colonIndex];
        var endName = sheetName[(colonIndex + 1)..];
        if (!TryGetSheetIndex(ctx, startName, out var startIndex) ||
            !TryGetSheetIndex(ctx, endName, out var endIndex))
            return ErrorValue.Ref;

        return new NumberValue(Math.Abs(endIndex - startIndex) + 1);
    }

    private static bool TryGetCurrentSheetIndex(IEvalContext ctx, out int index)
    {
        index = 0;
        var currentSheet = ctx.CurrentSheet;
        if (currentSheet is null) return false;

        if (ctx.CurrentWorkbook is null)
        {
            index = 1;
            return true;
        }

        for (var i = 0; i < ctx.CurrentWorkbook.Sheets.Count; i++)
        {
            if (ctx.CurrentWorkbook.Sheets[i].Id != currentSheet.Id) continue;
            index = i + 1;
            return true;
        }

        return false;
    }

    private static bool TryGetSheetIndex(IEvalContext ctx, string sheetName, out int index)
    {
        index = 0;
        if (ctx.CurrentWorkbook is null)
        {
            if (ctx.CurrentSheet is not null &&
                string.Equals(ctx.CurrentSheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                index = 1;
                return true;
            }

            return false;
        }

        for (var i = 0; i < ctx.CurrentWorkbook.Sheets.Count; i++)
        {
            if (!string.Equals(ctx.CurrentWorkbook.Sheets[i].Name, sheetName, StringComparison.OrdinalIgnoreCase))
                continue;
            index = i + 1;
            return true;
        }

        return false;
    }

    private static ScalarValue Address(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        double dRow = ToNumber(args[0]); double dCol = ToNumber(args[1]);
        if (!double.IsFinite(dRow) || !double.IsFinite(dCol)) return ErrorValue.Num;
        int rowNum = (int)dRow; int colNum = (int)dCol;
        if (rowNum < 1 || rowNum > (int)CellAddress.MaxRow ||
            colNum < 1 || colNum > (int)CellAddress.MaxCol) return ErrorValue.Value;
        double rawAbsNum = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawAbsNum)) return ErrorValue.Value;
        int absNum = (int)rawAbsNum;
        if (absNum is not (1 or 2 or 3 or 4)) return ErrorValue.Value;
        bool useA1 = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);
        string? sheetText = args.Count > 4 && args[4] is not BlankValue ? ToText(args[4]) : null;
        string colLetter = CellAddress.NumberToColumnName((uint)colNum);
        bool colAbs = absNum is 1 or 3;
        bool rowAbs = absNum is 1 or 2;
        string addr = useA1
            ? $"{(colAbs ? "$" : "")}{colLetter}{(rowAbs ? "$" : "")}{rowNum}"
            : $"{(rowAbs ? $"R{rowNum}" : $"R[{rowNum}]")}{(colAbs ? $"C{colNum}" : $"C[{colNum}]")}";
        if (!string.IsNullOrEmpty(sheetText))
            addr = $"{FormatAddressSheetText(sheetText)}!{addr}";
        return new TextValue(addr);
    }

    private static string FormatAddressSheetText(string sheetText) =>
        SheetNameFormatter.QuoteIfNeeded(sheetText);
}

