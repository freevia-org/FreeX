using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class SortValueComparer
{
    public static int CompareKey(Workbook workbook, Cell? a, Cell? b, SortOn sortOn, CellColor? targetColor, bool caseSensitive)
    {
        if (targetColor is not null && sortOn is SortOn.CellColor or SortOn.FontColor)
        {
            var aColor = sortOn == SortOn.CellColor ? GetStyle(workbook, a).FillColor : GetStyle(workbook, a).FontColor;
            var bColor = sortOn == SortOn.CellColor ? GetStyle(workbook, b).FillColor : GetStyle(workbook, b).FontColor;
            return CompareTargetColor(aColor, bColor, targetColor.Value);
        }

        return sortOn switch
        {
            SortOn.CellColor => CompareNullableColor(GetStyle(workbook, a).FillColor, GetStyle(workbook, b).FillColor),
            SortOn.FontColor => CompareNullableColor(GetStyle(workbook, a).FontColor, GetStyle(workbook, b).FontColor),
            _ => CompareScalar(a?.Value ?? BlankValue.Instance, b?.Value ?? BlankValue.Instance, caseSensitive)
        };
    }

    private static CellStyle GetStyle(Workbook workbook, Cell? cell) =>
        workbook.GetStyle(cell?.StyleId ?? StyleId.Default);

    private static int CompareNullableColor(CellColor? a, CellColor? b)
    {
        if (a is null && b is null)
            return 0;
        if (a is null)
            return 1;
        if (b is null)
            return -1;

        var red = a.Value.R.CompareTo(b.Value.R);
        if (red != 0)
            return red;
        var green = a.Value.G.CompareTo(b.Value.G);
        return green != 0 ? green : a.Value.B.CompareTo(b.Value.B);
    }

    private static int CompareTargetColor(CellColor? a, CellColor? b, CellColor targetColor)
    {
        var aMatches = a == targetColor;
        var bMatches = b == targetColor;
        if (aMatches == bMatches)
            return 0;

        return aMatches ? -1 : 1;
    }

    /// <summary>
    /// Sort comparison mirroring Excel's order: numbers/dates, text, booleans, blanks/errors last.
    /// </summary>
    private static int CompareScalar(ScalarValue a, ScalarValue b, bool caseSensitive)
    {
        bool aNum = a is NumberValue or DateTimeValue;
        bool bNum = b is NumberValue or DateTimeValue;
        if (aNum && bNum)
        {
            double av = a is DateTimeValue da ? da.Value : ((NumberValue)a).Value;
            double bv = b is DateTimeValue db ? db.Value : ((NumberValue)b).Value;
            return av.CompareTo(bv);
        }
        if (aNum) return -1;  // numbers/dates before text/bool/blank
        if (bNum) return  1;
        return (a, b) switch
        {
            (TextValue ta,   TextValue tb  ) => string.Compare(ta.Value, tb.Value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            (TextValue,      _             ) => -1,  // text before bool/blank
            (_,              TextValue     ) =>  1,
            (BoolValue ba,   BoolValue bb  ) => ba.Value.CompareTo(bb.Value),
            (BoolValue,      _             ) => -1,  // bools before blank/error
            (_,              BoolValue     ) =>  1,
            (BlankValue,     BlankValue    ) =>  0,
            (BlankValue,     _             ) =>  1,  // blanks last
            (_,              BlankValue    ) => -1,
            _                               =>  0,
        };
    }
}
