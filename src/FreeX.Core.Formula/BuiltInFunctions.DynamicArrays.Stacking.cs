using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue VStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        long rowCountL = 0;
        foreach (var a in arrays) rowCountL += a.RowCount;
        int colCount = arrays.Max(a => a.ColCount);
        if (rowCountL * colCount > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
        int rowCount = (int)rowCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int rowOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[rowOffset + r, c] = arr.Cells[r, c];
            rowOffset += arr.RowCount;
        }

        return new RangeValue(result);
    }

    private static ScalarValue HStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        int rowCount = arrays.Max(a => a.RowCount);
        long colCountL = 0;
        foreach (var a in arrays) colCountL += a.ColCount;
        if ((long)rowCount * colCountL > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
        int colCount = (int)colCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int colOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, colOffset + c] = arr.Cells[r, c];
            colOffset += arr.ColCount;
        }

        return new RangeValue(result);
    }

    private static bool TryCollectStackArrays(
        IReadOnlyList<ScalarValue> args,
        out List<RangeValue> arrays,
        out ScalarValue error)
    {
        arrays = new List<RangeValue>();
        error = ErrorValue.Value;

        foreach (var arg in args)
        {
            arrays.Add(arg is RangeValue arr
                ? arr
                : new RangeValue(new[,] { { arg } }));
        }

        return arrays.Count > 0;
    }

    private static ScalarValue[,] CreateFilledRange(int rowCount, int colCount, ScalarValue value)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = value;
        return result;
    }

    private static ScalarValue ToRow(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Calc;
        if (values.Count > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var result = new ScalarValue[1, values.Count];
        for (int c = 0; c < values.Count; c++)
            result[0, c] = values[c];
        return new RangeValue(result);
    }

    private static ScalarValue ToCol(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Calc;
        if (values.Count > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var result = new ScalarValue[values.Count, 1];
        for (int r = 0; r < values.Count; r++)
            result[r, 0] = values[r];
        return new RangeValue(result);
    }

    private static bool TryFlattenArray(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        error = ErrorValue.Value;

        int ignore = 0;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[1], out var ignoreArg, out error)) return false;
            double rawIgnore = ToNumber(ignoreArg);
            if (!double.IsFinite(rawIgnore)) return false;
            ignore = (int)rawIgnore;
            if (ignore is < 0 or > 3) return false;
        }

        bool scanByColumn = false;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[2], out var scanArg, out error)) return false;
            scanByColumn = ToBool(scanArg);
        }

        bool ignoreBlanks = (ignore & 1) != 0;
        bool ignoreErrors = (ignore & 2) != 0;

        if (args[0] is ErrorValue arrayError)
        {
            if (ignoreErrors) return true;
            error = arrayError;
            return false;
        }

        if (args[0] is not RangeValue arr)
        {
            AddFlattenedValue(args[0], ignoreBlanks, ignoreErrors, values);
            return true;
        }

        if (scanByColumn)
        {
            for (int c = 0; c < arr.ColCount; c++)
                for (int r = 0; r < arr.RowCount; r++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }
        else
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }

        return true;
    }

    private static void AddFlattenedValue(
        ScalarValue value,
        bool ignoreBlanks,
        bool ignoreErrors,
        List<ScalarValue> values)
    {
        if (ignoreBlanks && value is BlankValue) return;
        if (ignoreErrors && value is ErrorValue) return;
        values.Add(value);
    }

    private static ScalarValue WrapRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int rowCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)rowCount * wrapCount > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
        var result = CreateFilledRange(rowCount, wrapCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i / wrapCount, i % wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static ScalarValue WrapCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int colCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)wrapCount * colCount > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
        var result = CreateFilledRange(wrapCount, colCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i % wrapCount, i / wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static bool TryGetWrapArgs(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out int wrapCount,
        out ScalarValue padWith,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        wrapCount = 0;
        padWith = ErrorValue.NA;
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (!TryGetScalarControlArgument(args[1], out var wrapCountArg, out error)) return false;
        double rawWrapCount = ToNumber(wrapCountArg);
        if (!double.IsFinite(rawWrapCount)) return false;
        if (rawWrapCount > int.MaxValue || rawWrapCount <= int.MinValue)
        {
            error = ErrorValue.Num;
            return false;
        }
        wrapCount = (int)rawWrapCount;
        if (wrapCount < 1)
        {
            error = ErrorValue.Num;
            return false;
        }

        if (args[0] is RangeValue arr)
        {
            if (!TryReadVector(arr, values)) return false;
        }
        else
        {
            values.Add(args[0]);
        }

        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetScalarFillArgument(args[2], out padWith, out error)) return false;
        }

        return values.Count > 0;
    }

    private static bool TryReadVector(RangeValue arr, List<ScalarValue> values)
    {
        if (arr.RowCount == 1)
        {
            for (int c = 0; c < arr.ColCount; c++)
                values.Add(arr.Cells[0, c]);
            return true;
        }

        if (arr.ColCount == 1)
        {
            for (int r = 0; r < arr.RowCount; r++)
                values.Add(arr.Cells[r, 0]);
            return true;
        }

        return false;
    }

    private static ScalarValue Expand(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryGetExpandDimension(args[1], arr.RowCount, out int rowCount, out var rowError)) return rowError;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetExpandDimension(args[2], arr.ColCount, out colCount, out var colError)) return colError;
        }

        if (rowCount < arr.RowCount || colCount < arr.ColCount) return ErrorValue.Value;
        if ((long)rowCount * colCount > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var padWith = (ScalarValue)ErrorValue.NA;
        if (args.Count > 3 && args[3] is not BlankValue)
        {
            if (!TryGetScalarFillArgument(args[3], out padWith, out var padError)) return padError;
        }

        var result = CreateFilledRange(rowCount, colCount, padWith);
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, c];
        return new RangeValue(result);
    }

    private static bool TryGetExpandDimension(ScalarValue value, int originalLength, out int dimension, out ScalarValue error)
    {
        dimension = originalLength;
        error = ErrorValue.Value;
        if (value is BlankValue) return true;

        if (!TryGetScalarControlArgument(value, out var scalarValue, out error)) return false;
        double raw = ToNumber(scalarValue);
        if (!double.IsFinite(raw) || raw > int.MaxValue) return false;
        dimension = (int)raw;
        return dimension >= 1;
    }
}

