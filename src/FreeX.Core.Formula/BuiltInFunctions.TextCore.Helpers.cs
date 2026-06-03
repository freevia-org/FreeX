using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Shared mapping, length, and DBCS helpers for text functions.

    private static ScalarValue TextResult(string text) =>
        ExceedsExcelTextLimit(text) ? ErrorValue.Value : new TextValue(text);

    private static bool ExceedsExcelTextLimit(string text) =>
        (ContainsSurrogatePair(text) ? CountTextElements(text) : text.Length) > 32767;

    private static RangeValue MapUnaryTextRange(RangeValue range, Func<ScalarValue, ScalarValue> map)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : map(value);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue MapTernaryTextArgs(
        ScalarValue first,
        ScalarValue second,
        ScalarValue third,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue> map)
    {
        var firstRange = first as RangeValue;
        var secondRange = second as RangeValue;
        var thirdRange = third as RangeValue;
        var shape = ChooseBroadcastShape(firstRange, secondRange, thirdRange);
        if (shape is null) return map(first, second, third);
        if ((firstRange is not null && !CanBroadcastToShape(firstRange, shape.RowCount, shape.ColCount)) ||
            (secondRange is not null && !CanBroadcastToShape(secondRange, shape.RowCount, shape.ColCount)) ||
            (thirdRange is not null && !CanBroadcastToShape(thirdRange, shape.RowCount, shape.ColCount)))
            return ErrorValue.Value;

        var cells = new ScalarValue[shape.RowCount, shape.ColCount];
        for (int r = 0; r < shape.RowCount; r++)
            for (int c = 0; c < shape.ColCount; c++)
            {
                var firstValue = firstRange is null ? first : ValueAtBroadcastCell(firstRange, r, c);
                var secondValue = secondRange is null ? second : ValueAtBroadcastCell(secondRange, r, c);
                var thirdValue = thirdRange is null ? third : ValueAtBroadcastCell(thirdRange, r, c);
                cells[r, c] = map(firstValue, secondValue, thirdValue);
            }

        return new RangeValue(cells);
    }

    private static bool CanBroadcastToShape(RangeValue range, int rows, int cols) =>
        (range.RowCount == rows && range.ColCount == cols) || (range.RowCount == 1 && range.ColCount == 1);

    private static ScalarValue ValueAtBroadcastCell(RangeValue range, int row, int col) =>
        range.RowCount == 1 && range.ColCount == 1 ? range.Cells[0, 0] : range.Cells[row, col];

    private static RangeValue? ChooseBroadcastShape(params RangeValue?[] ranges)
    {
        RangeValue? fallback = null;
        foreach (var range in ranges)
        {
            if (range is null) continue;
            fallback ??= range;
            if (range.RowCount != 1 || range.ColCount != 1) return range;
        }

        return fallback;
    }

    private static ScalarValue MapQuaternaryTextArgs(
        ScalarValue first,
        ScalarValue second,
        ScalarValue third,
        ScalarValue fourth,
        Func<ScalarValue, ScalarValue, ScalarValue, ScalarValue, ScalarValue> map)
    {
        var firstRange = first as RangeValue;
        var secondRange = second as RangeValue;
        var thirdRange = third as RangeValue;
        var fourthRange = fourth as RangeValue;
        var shape = ChooseBroadcastShape(firstRange, secondRange, thirdRange, fourthRange);
        if (shape is null) return map(first, second, third, fourth);
        if ((firstRange is not null && !CanBroadcastToShape(firstRange, shape.RowCount, shape.ColCount)) ||
            (secondRange is not null && !CanBroadcastToShape(secondRange, shape.RowCount, shape.ColCount)) ||
            (thirdRange is not null && !CanBroadcastToShape(thirdRange, shape.RowCount, shape.ColCount)) ||
            (fourthRange is not null && !CanBroadcastToShape(fourthRange, shape.RowCount, shape.ColCount)))
            return ErrorValue.Value;

        var cells = new ScalarValue[shape.RowCount, shape.ColCount];
        for (int r = 0; r < shape.RowCount; r++)
            for (int c = 0; c < shape.ColCount; c++)
            {
                var firstValue = firstRange is null ? first : ValueAtBroadcastCell(firstRange, r, c);
                var secondValue = secondRange is null ? second : ValueAtBroadcastCell(secondRange, r, c);
                var thirdValue = thirdRange is null ? third : ValueAtBroadcastCell(thirdRange, r, c);
                var fourthValue = fourthRange is null ? fourth : ValueAtBroadcastCell(fourthRange, r, c);
                cells[r, c] = map(firstValue, secondValue, thirdValue, fourthValue);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue MapScalarArgs(
        IReadOnlyList<ScalarValue> args,
        Func<IReadOnlyList<ScalarValue>, ScalarValue> map)
    {
        var ranges = new RangeValue?[args.Count];
        for (int i = 0; i < args.Count; i++)
            ranges[i] = args[i] as RangeValue;

        var shape = ChooseBroadcastShape(ranges);
        if (shape is null) return map(args);

        foreach (var range in ranges)
        {
            if (range is null) continue;
            if (!CanBroadcastToShape(range, shape.RowCount, shape.ColCount))
                return ErrorValue.Value;
        }

        var cells = new ScalarValue[shape.RowCount, shape.ColCount];
        var scalarArgs = new ScalarValue[args.Count];
        for (int r = 0; r < shape.RowCount; r++)
            for (int c = 0; c < shape.ColCount; c++)
            {
                for (int i = 0; i < args.Count; i++)
                    scalarArgs[i] = args[i] is RangeValue range ? ValueAtBroadcastCell(range, r, c) : args[i];
                cells[r, c] = map(scalarArgs);
            }

        return new RangeValue(cells);
    }

    private static bool ContainsSurrogatePair(string text)
    {
        for (int i = 0; i + 1 < text.Length; i++)
            if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                return true;
        return false;
    }

    private static int TextElementIndexFromOneBasedPosition(string text, int position)
    {
        int index = 0;
        for (int current = 1; current < position && index < text.Length; current++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return index;
    }

    private static int AdvanceTextElements(string text, int index, int count)
    {
        for (int taken = 0; taken < count && index < text.Length; taken++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return index;
    }

    private static int CountTextElements(string text)
    {
        int count = 0;
        for (int index = 0; index < text.Length; count++)
            index += IsSurrogatePairAt(text, index) ? 2 : 1;

        return count;
    }

    private static int OneBasedTextPositionFromUtf16Index(string text, int index)
    {
        int position = 1;
        for (int i = 0; i < index && i < text.Length; position++)
            i += IsSurrogatePairAt(text, i) ? 2 : 1;

        return position;
    }

    private static bool IsSurrogatePairAt(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]);

    private static int CountDbcsBytes(string text)
    {
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            bytes += DbcsByteWidthAt(text, index);
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return bytes;
    }

    private static int DbcsByteWidthAt(string text, int index)
    {
        if (IsSurrogatePairAt(text, index)) return 2;
        var ch = text[index];
        return ch <= '\u00ff' || (ch >= '\uff61' && ch <= '\uff9f') ? 1 : 2;
    }

    private static int DbcsByteOffsetToUtf16Index(string text, int byteOffset)
    {
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            int width = DbcsByteWidthAt(text, index);
            if (bytes + width > byteOffset)
                return bytes == byteOffset ? index : index + (IsSurrogatePairAt(text, index) ? 2 : 1);

            bytes += width;
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return text.Length;
    }

    private static int DbcsBytePositionFromUtf16Index(string text, int utf16Index)
    {
        int bytes = 0;
        for (int index = 0; index < utf16Index && index < text.Length;)
        {
            bytes += DbcsByteWidthAt(text, index);
            index += IsSurrogatePairAt(text, index) ? 2 : 1;
        }

        return bytes + 1;
    }

    private static string SliceDbcsBytes(string text, int startByteOffset, int byteCount)
    {
        int endByteOffset = startByteOffset + byteCount;
        int start = text.Length;
        int end = text.Length;
        int bytes = 0;
        for (int index = 0; index < text.Length;)
        {
            int width = DbcsByteWidthAt(text, index);
            int nextBytes = bytes + width;
            int nextIndex = index + (IsSurrogatePairAt(text, index) ? 2 : 1);
            if (start == text.Length && bytes >= startByteOffset)
                start = index;
            if (nextBytes > endByteOffset)
            {
                end = index;
                break;
            }

            if (nextBytes <= endByteOffset)
                end = nextIndex;

            bytes = nextBytes;
            index = nextIndex;
        }

        if (startByteOffset >= bytes && start == text.Length)
            start = end = text.Length;
        if (end < start) end = start;
        return text[start..end];
    }

}
