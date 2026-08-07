using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Take(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        int rowStart = 0;
        int rowCount = arr.RowCount;
        if (args[1] is not BlankValue &&
            !TryGetArraySliceCount(args[1], arr.RowCount, isTake: true, out rowStart, out rowCount, out var rowSliceError))
            return rowSliceError;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: true, out colStart, out colCount, out var colSliceError))
                return colSliceError;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static ScalarValue Drop(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        int rowStart = 0;
        int rowCount = arr.RowCount;
        if (args[1] is not BlankValue &&
            !TryGetArraySliceCount(args[1], arr.RowCount, isTake: false, out rowStart, out rowCount, out var rowSliceError))
            return rowSliceError;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: false, out colStart, out colCount, out var colSliceError))
                return colSliceError;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static bool TryGetArraySliceCount(
        ScalarValue countValue,
        int dimensionLength,
        bool isTake,
        out int start,
        out int count,
        out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (!TryGetScalarControlArgument(countValue, out var scalarCountValue, out error))
        {
            start = 0;
            count = 0;
            return false;
        }

        countValue = scalarCountValue;
        double raw = ToNumber(countValue);
        if (!double.IsFinite(raw))
        {
            start = 0;
            count = 0;
            return false;
        }

        // Compare/clamp using the double magnitude rather than truncating to Int32 first. Excel treats a
        // rows/cols count whose magnitude reaches or exceeds the array's dimension length - including
        // magnitudes far outside Int32 range, e.g. =TAKE(A1:C3,1E10) - as "the whole dimension or more":
        // TAKE clamps to the full dimension and DROP reports #CALC! (dropping more than the whole array),
        // neither of which is the #VALUE! error a naive Int32 range check produces. Truncating a huge or
        // boundary double to Int32 first would also risk overflow (e.g. Math.Abs(int.MinValue)).
        double magnitude = Math.Abs(raw);
        if (magnitude < 1)
        {
            start = 0;
            count = 0;
            error = ErrorValue.Calc;
            return false;
        }

        bool isNegative = raw < 0;

        if (isTake)
        {
            count = magnitude >= dimensionLength ? dimensionLength : (int)magnitude;
            start = isNegative ? dimensionLength - count : 0;
            return count > 0;
        }

        if (magnitude >= dimensionLength)
        {
            start = 0;
            count = 0;
            error = ErrorValue.Calc;
            return false;
        }

        int requested = (int)magnitude;
        if (isNegative)
        {
            start = 0;
            count = dimensionLength - requested;
        }
        else
        {
            start = requested;
            count = dimensionLength - requested;
        }

        return count > 0;
    }


    private static RangeValue SliceRange(RangeValue arr, int rowStart, int colStart, int rowCount, int colCount)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = arr.Cells[rowStart + r, colStart + c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryResolveChoiceIndexes(args, arr.RowCount, out var rowIndexes, out var error)) return error;
        if ((long)rowIndexes.Count * arr.ColCount > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var result = new ScalarValue[rowIndexes.Count, arr.ColCount];
        for (int r = 0; r < rowIndexes.Count; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndexes[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryResolveChoiceIndexes(args, arr.ColCount, out var colIndexes, out var error)) return error;
        if ((long)arr.RowCount * colIndexes.Count > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var result = new ScalarValue[arr.RowCount, colIndexes.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < colIndexes.Count; c++)
                result[r, c] = arr.Cells[r, colIndexes[c]];
        return new RangeValue(result);
    }

    private static bool TryResolveChoiceIndexes(
        IReadOnlyList<ScalarValue> args,
        int dimensionLength,
        out List<int> indexes,
        out ScalarValue error)
    {
        indexes = new List<int>();
        error = ErrorValue.Value;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e)
            {
                error = e;
                return false;
            }

            if (args[i] is RangeValue range)
            {
                for (int r = 0; r < range.RowCount; r++)
                    for (int c = 0; c < range.ColCount; c++)
                        if (!TryAddChoiceIndex(range.Cells[r, c], dimensionLength, indexes, out error))
                            return false;

                continue;
            }

            if (!TryAddChoiceIndex(args[i], dimensionLength, indexes, out error))
                return false;
        }

        return indexes.Count > 0;
    }

    private static bool TryAddChoiceIndex(
        ScalarValue value,
        int dimensionLength,
        List<int> indexes,
        out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is ErrorValue e)
        {
            error = e;
            return false;
        }

        double raw = ToNumber(value);
        if (!double.IsFinite(raw)) return false;

        int requested = (int)raw;
        if (requested == 0) return false;

        int zeroBased = requested > 0
            ? requested - 1
            : dimensionLength + requested;
        if (zeroBased < 0 || zeroBased >= dimensionLength) return false;

        indexes.Add(zeroBased);
        return true;
    }
}

