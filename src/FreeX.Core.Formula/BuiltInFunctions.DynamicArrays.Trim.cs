using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue TrimRange(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var range = args[0] is RangeValue rv
            ? rv
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });

        if (!TryGetTrimRangeMode(args, 1, out int trimRows, out var rowsError)) return rowsError;
        if (!TryGetTrimRangeMode(args, 2, out int trimCols, out var colsError)) return colsError;

        int rowStart = 0;
        int rowEnd = range.RowCount - 1;
        if ((trimRows & 1) != 0)
            while (rowStart <= rowEnd && IsTrimRangeBlankRow(range, rowStart)) rowStart++;
        if ((trimRows & 2) != 0)
            while (rowEnd >= rowStart && IsTrimRangeBlankRow(range, rowEnd)) rowEnd--;

        int colStart = 0;
        int colEnd = range.ColCount - 1;
        if ((trimCols & 1) != 0)
            while (colStart <= colEnd && IsTrimRangeBlankColumn(range, colStart, rowStart, rowEnd)) colStart++;
        if ((trimCols & 2) != 0)
            while (colEnd >= colStart && IsTrimRangeBlankColumn(range, colEnd, rowStart, rowEnd)) colEnd--;

        if (rowStart > rowEnd || colStart > colEnd)
            return ErrorValue.Calc;

        return SliceRange(range, rowStart, colStart, rowEnd - rowStart + 1, colEnd - colStart + 1);
    }

    private static bool TryGetTrimRangeMode(
        IReadOnlyList<ScalarValue> args,
        int index,
        out int mode,
        out ScalarValue error)
    {
        mode = 3;
        error = ErrorValue.Value;
        if (args.Count <= index || args[index] is BlankValue) return true;
        if (!TryGetScalarControlArgument(args[index], out var scalar, out error)) return false;

        var raw = ToNumber(scalar);
        if (!double.IsFinite(raw)) return false;

        mode = (int)raw;
        return mode is >= 0 and <= 3;
    }

    private static bool IsTrimRangeBlankRow(RangeValue range, int row)
    {
        for (int col = 0; col < range.ColCount; col++)
            if (range.Cells[row, col] is not BlankValue)
                return false;

        return true;
    }

    private static bool IsTrimRangeBlankColumn(RangeValue range, int col, int rowStart, int rowEnd)
    {
        for (int row = rowStart; row <= rowEnd; row++)
            if (range.Cells[row, col] is not BlankValue)
                return false;

        return true;
    }
}

