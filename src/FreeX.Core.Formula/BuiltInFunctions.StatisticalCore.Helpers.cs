using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static (IReadOnlyList<double> Values, ErrorValue? Error) CollectAValues(ScalarValue value, bool arrayTextAsZero = false)
    {
        var values = new List<double>();
        var error = AddAValues(value, values, directText: value is DirectTextLiteralValue, arrayTextAsZero);
        return (values, error);
    }

    // Excel (verified via COM) feeds the "A" statistical functions three kinds of input:
    //  - cell references (A1:A4, and INDEX/OFFSET/IF/LET results that stay references): text counts as 0 --
    //    even numeric-looking text such as "1" is NOT parsed -- and TRUE/FALSE count as 1/0;
    //  - literal or computed arrays ({1,"a",TRUE}, TRANSPOSE(range), range&""): booleans are ignored and text
    //    is ignored too, except that AVERAGEA counts array text as 0;
    //  - direct scalar arguments: numeric text is parsed, other text is #VALUE!, booleans are 1/0.
    private static ErrorValue? AddAValues(ScalarValue value, List<double> values, bool directText, bool arrayTextAsZero)
    {
        switch (value)
        {
            case ErrorValue e:
                return e;
            case ReferencedScalarValue referenced:
                return AddAReferenceCell(referenced.Value, values);
            case ArrayElementScalarValue arrayElement:
                return AddAArrayElement(arrayElement.Value, values, arrayTextAsZero);
            case RangeValue range:
                foreach (var cell in range.Flatten())
                {
                    var error = range.IsSheetReference
                        ? AddAReferenceCell(cell is ReferencedScalarValue r ? r.Value : cell, values)
                        : AddAArrayElement(cell, values, arrayTextAsZero);
                    if (error is not null) return error;
                }
                return null;
            case BlankValue:
                return null;
            case NumberValue n:
                values.Add(n.Value);
                return null;
            case DateTimeValue d:
                values.Add(d.Value);
                return null;
            case BoolValue b:
                values.Add(b.Value ? 1.0 : 0.0);
                return null;
            case DirectTextLiteralValue t:
                if (ExcelTextNumberParser.TryParse(t.Value, out var directParsed))
                    values.Add(directParsed);
                else if (t.Value.Length == 0 || !directText)
                    values.Add(0.0);
                else
                    return ErrorValue.Value;
                return null;
            case TextValue t:
                if (ExcelTextNumberParser.TryParse(t.Value, out var textParsed))
                    values.Add(textParsed);
                else if (t.Value.Length == 0 || !directText)
                    values.Add(0.0);
                else
                    return ErrorValue.Value;
                return null;
            default:
                return ErrorValue.Value;
        }
    }

    private static ErrorValue? AddAReferenceCell(ScalarValue cell, List<double> values)
    {
        switch (cell)
        {
            case ErrorValue e:
                return e;
            case BlankValue:
                return null;
            case NumberValue n:
                values.Add(n.Value);
                return null;
            case DateTimeValue d:
                values.Add(d.Value);
                return null;
            case BoolValue b:
                values.Add(b.Value ? 1.0 : 0.0);
                return null;
            case TextValue:
                values.Add(0.0);
                return null;
            case ReferencedScalarValue inner:
                return AddAReferenceCell(inner.Value, values);
            default:
                return ErrorValue.Value;
        }
    }

    private static ErrorValue? AddAArrayElement(ScalarValue element, List<double> values, bool arrayTextAsZero)
    {
        switch (element)
        {
            case ErrorValue e:
                return e;
            case ReferencedScalarValue referenced:
                return AddAReferenceCell(referenced.Value, values);
            case NumberValue n:
                values.Add(n.Value);
                return null;
            case DateTimeValue d:
                values.Add(d.Value);
                return null;
            case TextValue or DirectTextLiteralValue:
                if (arrayTextAsZero) values.Add(0.0);
                return null;
            default:
                // Blank and boolean array elements are skipped.
                return null;
        }
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectAValues(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (var i = start; i < args.Count; i++)
        {
            var (values, error) = CollectAValues(args[i], arrayTextAsZero: false);
            if (error is not null) return (null, error);
            list.AddRange(values);
        }

        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectNumbers(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (int i = start; i < args.Count; i++)
        {
            var a = args[i];
            if (a is ErrorValue e) return (null, e);
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) list.Add(value);
                else if (refError is not null) return (null, refError);
            }
            else if (a is NumberValue nv) list.Add(nv.Value);
            else if (a is BoolValue bv) list.Add(bv.Value ? 1.0 : 0.0);
            else if (a is DateTimeValue dt) list.Add(dt.Value);
            else if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return (null, ErrorValue.Value);
                list.Add(value);
            }
            else if (a is RangeValue inlineArray)
            {
                // An inline array constant (SKEW({1,2,10})) or other computed array reaches the function whole,
                // unlike worksheet ranges, which are expanded cell by cell before the call.
                var (arrayNums, arrayErr) = CollectRangeNumbers(inlineArray);
                if (arrayErr is not null) return (null, arrayErr);
                list.AddRange(arrayNums!);
            }
            else if (a is UnionValue union)
            {
                // R93-AREAS-union-value-model: a union reference argument (e.g.
                // AVERAGE((A1:A2,B1:B2))) evaluates to a UnionValue rather than a RangeValue --
                // fold every numeric cell across every area, ignoring text/blanks like a plain
                // range, matching CollectRangeNumbers' rules below.
                foreach (var area in union.Areas)
                {
                    var (areaNums, areaErr) = CollectRangeNumbers(area);
                    if (areaErr is not null) return (null, areaErr);
                    list.AddRange(areaNums!);
                }
            }
        }
        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbers(RangeValue range)
    {
        var (count, err) = CountRangeNumbers(range);
        if (err is not null) return (null, err);

        var list = new List<double>(count);
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is NumberValue n) list.Add(n.Value);
                else if (value is DateTimeValue d) list.Add(d.Value);
            }
        }

        return (list, null);
    }

    private static (int Count, ErrorValue? Error) CountRangeNumbers(RangeValue range)
    {
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e) return (0, e);
                if (value is NumberValue or DateTimeValue) count++;
            }
        }

        return (count, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbersForSelection(RangeValue range)
    {
        var list = new List<double>(range.RowCount * range.ColCount);
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e) return (null, e);
                if (value is NumberValue n) list.Add(n.Value);
                else if (value is DateTimeValue d) list.Add(d.Value);
            }
        }

        return (list, null);
    }

    private static double SelectKthSmallest(List<double> values, int k)
    {
        int left = 0;
        int right = values.Count - 1;
        var comparer = Comparer<double>.Default;

        while (left < right)
        {
            int pivotIndex = left + ((right - left) / 2);
            var (equalStart, equalEnd) = Partition(values, left, right, pivotIndex, comparer);

            if (k < equalStart)
                right = equalStart - 1;
            else if (k > equalEnd)
                left = equalEnd + 1;
            else
                break;
        }

        return values[k];
    }

    private static (int EqualStart, int EqualEnd) Partition(List<double> values, int left, int right, int pivotIndex, Comparer<double> comparer)
    {
        double pivotValue = values[pivotIndex];
        int less = left;
        int current = left;
        int greater = right;

        while (current <= greater)
        {
            int comparison = comparer.Compare(values[current], pivotValue);
            if (comparison < 0)
            {
                Swap(values, less, current);
                less++;
                current++;
            }
            else if (comparison > 0)
            {
                Swap(values, current, greater);
                greater--;
            }
            else
            {
                current++;
            }
        }

        return (less, greater);
    }

    private static void Swap(List<double> values, int i, int j)
    {
        if (i == j) return;
        (values[i], values[j]) = (values[j], values[i]);
    }

}
