using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // FIND/SEARCH text lookup functions, including byte-count variants.

    private static ScalarValue Find(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var startArg = args.Count > 2 && args[2] is not BlankValue ? args[2] : new NumberValue(1);
        if (args[0] is RangeValue || args[1] is RangeValue || startArg is RangeValue)
            return MapTernaryTextArgs(args[0], args[1], startArg, FindScalarWithArgs);
        return FindScalarWithArgs(args[0], args[1], startArg);
    }

    private static ScalarValue FindB(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        FindSearchB(args, useWildcards: false);

    private static ScalarValue FindScalarWithArgs(ScalarValue findValue, ScalarValue withinValue, ScalarValue startValue)
    {
        if (findValue is ErrorValue findError) return findError;
        if (withinValue is ErrorValue withinError) return withinError;
        if (startValue is ErrorValue startError) return startError;
        double rawStart = ToNumber(startValue);
        if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
        int startNum = (int)rawStart;
        if (startNum < 1) return ErrorValue.Value;
        return FindText(ToText(findValue), ToText(withinValue), startNum);
    }

    private static RangeValue MapFindRange(string findText, RangeValue range, int startNum)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : FindText(findText, ToText(value), startNum);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue FindText(string findText, string withinText, int startNum)
    {
        bool hasSurrogatePair = ContainsSurrogatePair(withinText);
        int startIdx = hasSurrogatePair
            ? TextElementIndexFromOneBasedPosition(withinText, startNum)
            : startNum - 1;
        if (findText.Length == 0)
            return startNum <= (hasSurrogatePair ? CountTextElements(withinText) : withinText.Length) + 1
                ? new NumberValue(startNum)
                : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        int pos = withinText.IndexOf(findText, startIdx, StringComparison.Ordinal);
        if (pos < 0) return ErrorValue.Value;
        return new NumberValue(hasSurrogatePair ? OneBasedTextPositionFromUtf16Index(withinText, pos) : pos + 1);
    }

    private static ScalarValue Search(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var startArg = args.Count > 2 && args[2] is not BlankValue ? args[2] : new NumberValue(1);
        if (args[0] is RangeValue || args[1] is RangeValue || startArg is RangeValue)
            return MapTernaryTextArgs(args[0], args[1], startArg, SearchScalarWithArgs);
        return SearchScalarWithArgs(args[0], args[1], startArg);
    }

    private static ScalarValue SearchB(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        FindSearchB(args, useWildcards: true);

    private static ScalarValue FindSearchB(IReadOnlyList<ScalarValue> args, bool useWildcards)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var startArg = args.Count > 2 && args[2] is not BlankValue ? args[2] : new NumberValue(1);
        return MapTernaryTextArgs(args[0], args[1], startArg, (findValue, withinValue, startValue) =>
            FindSearchBScalarWithArgs(findValue, withinValue, startValue, useWildcards));
    }

    private static ScalarValue FindSearchBScalarWithArgs(
        ScalarValue findValue,
        ScalarValue withinValue,
        ScalarValue startValue,
        bool useWildcards)
    {
        if (findValue is ErrorValue findError) return findError;
        if (withinValue is ErrorValue withinError) return withinError;
        if (startValue is ErrorValue startError) return startError;
        double rawStart = ToNumber(startValue);
        if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
        int startByte = (int)rawStart;
        if (startByte < 1) return ErrorValue.Value;

        return useWildcards
            ? SearchBText(ToText(findValue), ToText(withinValue), startByte)
            : FindBText(ToText(findValue), ToText(withinValue), startByte);
    }

    private static ScalarValue SearchScalarWithArgs(ScalarValue findValue, ScalarValue withinValue, ScalarValue startValue)
    {
        if (findValue is ErrorValue findError) return findError;
        if (withinValue is ErrorValue withinError) return withinError;
        if (startValue is ErrorValue startError) return startError;
        double rawStart = ToNumber(startValue);
        if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
        int startNum = (int)rawStart;
        if (startNum < 1) return ErrorValue.Value;
        return SearchText(ToText(findValue), ToText(withinValue), startNum);
    }

    private static RangeValue MapSearchRange(string findText, RangeValue range, int startNum)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : SearchText(findText, ToText(value), startNum);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue SearchText(string findText, string withinText, int startNum)
    {
        bool hasSurrogatePair = ContainsSurrogatePair(withinText);
        int startIdx = hasSurrogatePair
            ? TextElementIndexFromOneBasedPosition(withinText, startNum)
            : startNum - 1;
        if (findText.Length == 0)
            return startNum <= (hasSurrogatePair ? CountTextElements(withinText) : withinText.Length) + 1
                ? new NumberValue(startNum)
                : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;

        var regex = GetSearchRegex(findText);
        Match match;
        try
        {
            match = regex.Match(withinText, startIdx);
        }
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }

        if (!match.Success) return ErrorValue.Value;
        return new NumberValue(hasSurrogatePair ? OneBasedTextPositionFromUtf16Index(withinText, match.Index) : match.Index + 1);
    }

    private static ScalarValue FindBText(string findText, string withinText, int startByte)
    {
        if (findText.Length == 0)
            return startByte <= CountDbcsBytes(withinText) + 1 ? new NumberValue(startByte) : ErrorValue.Value;

        int startIdx = DbcsByteOffsetToUtf16Index(withinText, startByte - 1);
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        int pos = withinText.IndexOf(findText, startIdx, StringComparison.Ordinal);
        return pos < 0 ? ErrorValue.Value : new NumberValue(DbcsBytePositionFromUtf16Index(withinText, pos));
    }

    private static ScalarValue SearchBText(string findText, string withinText, int startByte)
    {
        if (findText.Length == 0)
            return startByte <= CountDbcsBytes(withinText) + 1 ? new NumberValue(startByte) : ErrorValue.Value;

        int startIdx = DbcsByteOffsetToUtf16Index(withinText, startByte - 1);
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        var regex = GetSearchRegex(findText);
        Match match;
        try
        {
            match = regex.Match(withinText, startIdx);
        }
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }

        return match.Success ? new NumberValue(DbcsBytePositionFromUtf16Index(withinText, match.Index)) : ErrorValue.Value;
    }

}
