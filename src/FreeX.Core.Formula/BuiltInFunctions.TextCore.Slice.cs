using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // LEN/LEFT/RIGHT/MID text slicing functions, including byte-count variants.

    private static ScalarValue Len(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args[0] is RangeValue range)
        {
            var cells = new ScalarValue[range.RowCount, range.ColCount];
            for (int r = 0; r < range.RowCount; r++)
                for (int c = 0; c < range.ColCount; c++)
                {
                    var value = range.Cells[r, c];
                    cells[r, c] = value is ErrorValue e ? e : LenScalar(value);
                }

            return new RangeValue(cells);
        }

        return LenScalar(args[0]);
    }

    private static ScalarValue LenScalar(ScalarValue value)
    {
        var text = ToText(value);
        return new NumberValue(text.Length);
    }

    private static ScalarValue LenB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args[0] is RangeValue range)
            return MapUnaryTextRange(range, LenBScalar);

        return LenBScalar(args[0]);
    }

    private static ScalarValue LenBScalar(ScalarValue value) =>
        new NumberValue(CountDbcsBytes(ToText(value)));

    private static ScalarValue Left(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        // R102: only a genuinely-omitted num_chars (args.Count <= 1 -- no 2nd argument node at all)
        // defaults to 1. A PRESENT-but-empty argument (a trailing comma, e.g. LEFT("abc",)) is no
        // longer specially intercepted upstream -- it now evaluates like any other blank-cell
        // reference (BlankValue.Instance), so it falls straight through to args[1] here and coerces
        // to numeric 0 in LeftScalarWithCount below, correctly yielding "" (not the 1-char default).
        var countArg = args.Count > 1 ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, LeftScalarWithCount);
    }

    private static ScalarValue LeftB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        // See the identical comment on Left above -- only a genuinely-omitted num_bytes (args.Count
        // <= 1) defaults to 1; a present-but-blank argument coerces to 0, yielding "".
        var countArg = args.Count > 1 ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, LeftBScalarWithCount);
    }

    private static ScalarValue LeftScalarWithCount(ScalarValue value, ScalarValue countValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (countValue is ErrorValue countError) return countError;
        var rawCount = ToNumber(countValue);
        if (!double.IsFinite(rawCount) || rawCount < 0 || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        return LeftScalar(value, count);
    }

    private static ScalarValue LeftScalar(ScalarValue value, int count)
    {
        var text = ToText(value);
        count = Math.Min(count, text.Length);
        return TextResult(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        // See the identical comment in Left above -- only a genuinely-omitted num_chars (args.Count
        // <= 1) defaults to 1; a present-but-blank argument coerces to 0, yielding "".
        var countArg = args.Count > 1 ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, RightScalarWithCount);
    }

    private static ScalarValue RightB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        // See the identical comment on Left above -- only a genuinely-omitted num_bytes (args.Count
        // <= 1) defaults to 1; a present-but-blank argument coerces to 0, yielding "".
        var countArg = args.Count > 1 ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, RightBScalarWithCount);
    }

    private static ScalarValue RightScalarWithCount(ScalarValue value, ScalarValue countValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (countValue is ErrorValue countError) return countError;
        var rawCount = ToNumber(countValue);
        if (!double.IsFinite(rawCount) || rawCount < 0 || rawCount > int.MaxValue) return ErrorValue.Value;
        var count = (int)rawCount;
        return RightScalar(value, count);
    }

    private static ScalarValue RightScalar(ScalarValue value, int count)
    {
        var text = ToText(value);
        count = Math.Min(count, text.Length);
        return TextResult(text[(text.Length - count)..]);
    }

    private static ScalarValue LeftBScalarWithCount(ScalarValue value, ScalarValue countValue) =>
        ByteSliceScalarWithCount(value, countValue, fromRight: false);

    private static ScalarValue RightBScalarWithCount(ScalarValue value, ScalarValue countValue) =>
        ByteSliceScalarWithCount(value, countValue, fromRight: true);

    private static ScalarValue ByteSliceScalarWithCount(ScalarValue value, ScalarValue countValue, bool fromRight)
    {
        if (value is ErrorValue valueError) return valueError;
        if (countValue is ErrorValue countError) return countError;
        var rawCount = ToNumber(countValue);
        if (!double.IsFinite(rawCount) || rawCount < 0 || rawCount > int.MaxValue) return ErrorValue.Value;
        var byteCount = (int)rawCount;

        var text = ToText(value);
        return fromRight
            ? TextResult(SliceDbcsBytes(text, Math.Max(0, CountDbcsBytes(text) - byteCount), byteCount))
            : TextResult(SliceDbcsBytes(text, 0, byteCount));
    }

    private static RangeValue MapTextSliceRange(RangeValue range, int count, bool fromRight)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e
                    ? e
                    : fromRight ? RightScalar(value, count) : LeftScalar(value, count);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue Mid(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue startError) return startError;
        if (args[2] is ErrorValue lengthError) return lengthError;
        if (args[0] is RangeValue || args[1] is RangeValue || args[2] is RangeValue)
            return MapTernaryTextArgs(args[0], args[1], args[2], MidScalarWithArgs);
        double rawStart = ToNumber(args[1]);
        double rawLen   = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        if (rawStart < 1 || rawLen < 0 || rawStart > int.MaxValue || rawLen > int.MaxValue) return ErrorValue.Value;
        if (args[0] is RangeValue range) return MapMidRange(range, (int)rawStart, (int)rawLen);
        var text    = ToText(args[0]);
        int start   = (int)rawStart - 1; // 1-based → 0-based
        int numChars = (int)rawLen;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
    }

    private static ScalarValue MidB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue startError) return startError;
        if (args[2] is ErrorValue lengthError) return lengthError;
        return MapTernaryTextArgs(args[0], args[1], args[2], MidBScalarWithArgs);
    }

    private static ScalarValue MidBScalarWithArgs(ScalarValue value, ScalarValue startValue, ScalarValue lengthValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (startValue is ErrorValue startError) return startError;
        if (lengthValue is ErrorValue lengthError) return lengthError;
        double rawStart = ToNumber(startValue);
        double rawLen = ToNumber(lengthValue);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        if (rawStart < 1 || rawLen < 0 || rawStart > int.MaxValue || rawLen > int.MaxValue) return ErrorValue.Value;
        return TextResult(SliceDbcsBytes(ToText(value), (int)rawStart - 1, (int)rawLen));
    }

    private static RangeValue MapMidRange(RangeValue range, int startNum, int numChars)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : MidText(ToText(value), startNum, numChars);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue MidScalarWithArgs(ScalarValue value, ScalarValue startValue, ScalarValue lengthValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (startValue is ErrorValue startError) return startError;
        if (lengthValue is ErrorValue lengthError) return lengthError;
        double rawStart = ToNumber(startValue);
        double rawLen = ToNumber(lengthValue);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        if (rawStart < 1 || rawLen < 0 || rawStart > int.MaxValue || rawLen > int.MaxValue) return ErrorValue.Value;
        return MidText(ToText(value), (int)rawStart, (int)rawLen);
    }

    private static ScalarValue MidText(string text, int startNum, int numChars)
    {
        int start = startNum - 1;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
    }

}
