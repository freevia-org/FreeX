using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Excel's legacy implicit intersection (the <c>@</c> operator): resolves a multi-cell range used in a
/// scalar context to the single cell that intersects the formula's own row/column. A 1×1 range always
/// resolves to its single cell; otherwise an off-axis formula position yields <c>#VALUE!</c>.
/// </summary>
public static class ImplicitIntersection
{
    public static ScalarValue Resolve(RangeValue range, uint cellRow, uint cellCol)
    {
        int rows = range.RowCount;
        int cols = range.ColCount;

        if (rows == 1 && cols == 1)
            return range.Cells[0, 0];

        if (rows == 1)
        {
            long c = (long)cellCol - range.StartCol;
            return c >= 0 && c < cols ? range.Cells[0, (int)c] : ErrorValue.Value;
        }

        if (cols == 1)
        {
            long r = (long)cellRow - range.StartRow;
            return r >= 0 && r < rows ? range.Cells[(int)r, 0] : ErrorValue.Value;
        }

        long row = (long)cellRow - range.StartRow;
        long col = (long)cellCol - range.StartCol;
        return row >= 0 && row < rows && col >= 0 && col < cols
            ? range.Cells[(int)row, (int)col]
            : ErrorValue.Value;
    }
}
