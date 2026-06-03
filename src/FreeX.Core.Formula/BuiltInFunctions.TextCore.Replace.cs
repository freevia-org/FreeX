using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Replacement and substitution text functions.

    private static ScalarValue Substitute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue oldTextError) return oldTextError;
        if (args[2] is ErrorValue newTextError) return newTextError;
        var instanceArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        if (instanceArg is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], instanceArg, SubstituteScalarWithArgs);
    }

    private static ScalarValue SubstituteScalarWithArgs(
        ScalarValue textValue,
        ScalarValue oldTextValue,
        ScalarValue newTextValue,
        ScalarValue instanceValue)
    {
        if (textValue is ErrorValue textError) return textError;
        if (oldTextValue is ErrorValue oldTextError) return oldTextError;
        if (newTextValue is ErrorValue newTextError) return newTextError;
        if (instanceValue is ErrorValue instanceError) return instanceError;
        var oldText = ToText(oldTextValue);
        var newText = ToText(newTextValue);
        int? instanceNum = null;
        if (instanceValue is not BlankValue)
        {
            double rawInstanceNum = ToNumber(instanceValue);
            if (!double.IsFinite(rawInstanceNum) || rawInstanceNum > int.MaxValue) return ErrorValue.Value;
            instanceNum = (int)rawInstanceNum;
            if (instanceNum < 1) return ErrorValue.Value;
        }
        return SubstituteText(ToText(textValue), oldText, newText, instanceNum);
    }

    private static ScalarValue SubstituteText(string text, string oldText, string newText, int? instanceNum)
    {
        if (oldText.Length == 0) return TextResult(text);

        if (instanceNum is int instance)
        {
            int count = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                int idx = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                if (count == instance)
                    return TextResult(text[..idx] + newText + text[(idx + oldText.Length)..]);
                pos = idx + oldText.Length;
            }
            return TextResult(text);
        }

        return TextResult(text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    private static ScalarValue Replace(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], ReplaceScalarWithArgs);
    }

    private static ScalarValue ReplaceScalarWithArgs(
        ScalarValue value,
        ScalarValue startValue,
        ScalarValue numCharsValue,
        ScalarValue newTextValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (startValue is ErrorValue startError) return startError;
        if (numCharsValue is ErrorValue numCharsError) return numCharsError;
        if (newTextValue is ErrorValue newTextError) return newTextError;
        double rawStart = ToNumber(startValue);
        double rawNumChars = ToNumber(numCharsValue);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawNumChars)) return ErrorValue.Value;
        if (rawStart > int.MaxValue || rawNumChars > int.MaxValue) return ErrorValue.Value;

        int startNum = (int)rawStart;
        int numChars = (int)rawNumChars;
        if (startNum < 1 || numChars < 0) return ErrorValue.Value;

        return ReplaceText(ToText(value), startNum, numChars, ToText(newTextValue));
    }

    private static ScalarValue ReplaceB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;
        return MapQuaternaryTextArgs(args[0], args[1], args[2], args[3], ReplaceBScalarWithArgs);
    }

    private static ScalarValue ReplaceBScalarWithArgs(
        ScalarValue value,
        ScalarValue startValue,
        ScalarValue numBytesValue,
        ScalarValue newTextValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (startValue is ErrorValue startError) return startError;
        if (numBytesValue is ErrorValue numBytesError) return numBytesError;
        if (newTextValue is ErrorValue newTextError) return newTextError;
        double rawStart = ToNumber(startValue);
        double rawNumBytes = ToNumber(numBytesValue);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawNumBytes)) return ErrorValue.Value;
        if (rawStart > int.MaxValue || rawNumBytes > int.MaxValue) return ErrorValue.Value;

        int startByte = (int)rawStart;
        int numBytes = (int)rawNumBytes;
        if (startByte < 1 || numBytes < 0) return ErrorValue.Value;

        return ReplaceBText(ToText(value), startByte, numBytes, ToText(newTextValue));
    }

    private static RangeValue MapReplaceRange(RangeValue range, int startNum, int numChars, string newText)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : ReplaceText(ToText(value), startNum, numChars, newText);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue ReplaceText(string text, int startNum, int numChars, string newText)
    {
        bool hasSurrogatePair = ContainsSurrogatePair(text);
        int length = hasSurrogatePair ? CountTextElements(text) : text.Length;
        if (startNum > length + 1) return ErrorValue.Value;

        int start = hasSurrogatePair
            ? TextElementIndexFromOneBasedPosition(text, startNum)
            : Math.Min(startNum - 1, text.Length);
        int end = hasSurrogatePair
            ? AdvanceTextElements(text, start, numChars)
            : start + Math.Min(numChars, text.Length - start);
        return TextResult(text[..start] + newText + text[end..]);
    }

    private static ScalarValue ReplaceBText(string text, int startByte, int numBytes, string newText)
    {
        if (startByte > CountDbcsBytes(text) + 1) return ErrorValue.Value;

        int start = DbcsByteOffsetToUtf16Index(text, startByte - 1);
        int byteCount = CountDbcsBytes(text);
        int endByteOffset = startByte - 1 + Math.Min(numBytes, byteCount - (startByte - 1));
        int end = DbcsByteOffsetToUtf16Index(text, endByteOffset);
        return TextResult(text[..start] + newText + text[end..]);
    }

}
