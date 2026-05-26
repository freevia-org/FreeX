using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core text functions and inline text formatting helpers.

    private static ScalarValue TextFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue formatError) return formatError;
        return MapBinaryMathArgs(args[0], args[1], TextScalarWithFormat);
    }

    private static ScalarValue TextScalarWithFormat(ScalarValue value, ScalarValue formatValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (formatValue is ErrorValue formatError) return formatError;
        return TextFormatValue(value, ToText(formatValue));
    }

    private static RangeValue MapTextFuncRange(RangeValue range, string fmt)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : TextFormatValue(value, fmt);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue TextFormatValue(ScalarValue val, string fmt)
    {
        // Simple inline formatter (avoids depending on Freexcel.Core.Calc)
        if (TryCellNumber(val, out double value))
            return TextResult(FormatNumberInline(value, fmt));
        return TextResult(ToText(val));
    }

    private static string FormatNumberInline(double value, string fmt)
    {
        if (string.IsNullOrEmpty(fmt)) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (TryFormatDateTimeInline(value, fmt, out var dateText)) return dateText;
        try { return value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static bool TryFormatDateTimeInline(double value, string fmt, out string text)
    {
        text = string.Empty;
        if (!LooksLikeDateTimeFormat(fmt)) return false;

        try
        {
            var dt = SerialToDate(value);
            text = dt.ToString(ToDotNetDateTimeFormat(fmt), CultureInfo.GetCultureInfo("en-US"));
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeDateTimeFormat(string fmt) =>
        fmt.Contains("AM/PM", StringComparison.OrdinalIgnoreCase)
        || fmt.Any(c => c is 'y' or 'Y' or 'h' or 'H')
        || LooksLikeMonthFormat(fmt)
        || LooksLikeDayFormat(fmt);

    private static bool LooksLikeMonthFormat(string fmt)
    {
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] is not ('m' or 'M')) continue;
            var prev = PreviousNonSpace(fmt, i);
            var next = NextNonSpace(fmt, i + CountSame(fmt, i));
            if (prev is '/' or '-' or '\0' || next is '/' or '-' or '\0') return true;
        }

        return false;
    }

    private static bool LooksLikeDayFormat(string fmt)
    {
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] is not ('d' or 'D')) continue;
            var prev = PreviousNonSpace(fmt, i);
            var next = NextNonSpace(fmt, i + CountSame(fmt, i));
            if (prev is '/' or '-' or ',' || next is '/' or '-' or ',') return true;
        }

        return false;
    }

    private static string ToDotNetDateTimeFormat(string fmt)
    {
        var sb = new System.Text.StringBuilder(fmt.Length);
        bool lastWasHour = false;
        bool lastWasMinute = false;

        for (int i = 0; i < fmt.Length;)
        {
            if (MatchesAt(fmt, i, "AM/PM"))
            {
                sb.Append("tt");
                i += 5;
                lastWasHour = lastWasMinute = false;
                continue;
            }

            char ch = fmt[i];
            int count = CountSame(fmt, i);
            switch (char.ToLowerInvariant(ch))
            {
                case 'y':
                    sb.Append(count <= 2 ? "yy" : "yyyy");
                    lastWasHour = lastWasMinute = false;
                    break;
                case 'd':
                    sb.Append(new string('d', Math.Min(count, 4)));
                    lastWasHour = lastWasMinute = false;
                    break;
                case 'h':
                    sb.Append(count <= 1 ? "h" : "hh");
                    lastWasHour = true;
                    lastWasMinute = false;
                    break;
                case 's':
                    sb.Append(count <= 1 ? "s" : "ss");
                    lastWasHour = false;
                    lastWasMinute = false;
                    break;
                case 'm':
                    bool minute = lastWasHour || lastWasMinute || PreviousNonSpace(fmt, i) == ':' || NextNonSpace(fmt, i + count) == ':';
                    sb.Append(minute
                        ? count <= 1 ? "m" : "mm"
                        : count switch { 1 => "M", 2 => "MM", 3 => "MMM", _ => "MMMM" });
                    lastWasHour = false;
                    lastWasMinute = minute;
                    break;
                default:
                    sb.Append(ch);
                    lastWasHour = ch == ':' && lastWasHour;
                    lastWasMinute = ch == ':' && lastWasMinute;
                    break;
            }

            i += count;
        }

        return sb.ToString();
    }

    private static bool MatchesAt(string text, int index, string value) =>
        index + value.Length <= text.Length
        && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static int CountSame(string text, int index)
    {
        char ch = char.ToLowerInvariant(text[index]);
        int end = index + 1;
        while (end < text.Length && char.ToLowerInvariant(text[end]) == ch) end++;
        return end - index;
    }

    private static char PreviousNonSpace(string text, int index)
    {
        for (int i = index - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    private static char NextNonSpace(string text, int index)
    {
        for (int i = index; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    private static readonly Regex MultiSpaceRegex = new(@" {2,}", RegexOptions.Compiled);

    private static ScalarValue Trim(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, TrimText);
        return TrimText(args[0]);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UpperText);
        return UpperText(args[0]);
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, LowerText);
        return LowerText(args[0]);
    }

    private static ScalarValue Proper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ProperText);
        return ProperText(args[0]);
    }

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

    private static ScalarValue TrimText(ScalarValue value)
    {
        var text = MultiSpaceRegex.Replace(ToText(value).Trim(), " ");
        return TextResult(text);
    }

    private static ScalarValue UpperText(ScalarValue value) =>
        TextResult(ToText(value).ToUpperInvariant());

    private static ScalarValue LowerText(ScalarValue value) =>
        TextResult(ToText(value).ToLowerInvariant());

    private static ScalarValue ProperText(ScalarValue value)
    {
        var text = ToText(value);
        if (text.Length == 0) return new TextValue("");
        var sb = new System.Text.StringBuilder(text.Length);
        bool capitaliseNext = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || !char.IsLetter(ch)) { capitaliseNext = true; sb.Append(ch); }
            else if (capitaliseNext) { sb.Append(char.ToUpperInvariant(ch)); capitaliseNext = false; }
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return TextResult(sb.ToString());
    }

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

    private static ScalarValue TextResult(string text) =>
        text.Length > 32767 ? ErrorValue.Value : new TextValue(text);

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

    private static readonly ConcurrentDictionary<string, Regex> SearchCache = new();

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

        var regex = SearchCache.GetOrAdd(findText, pattern =>
        {
            return new Regex(WildcardToRegexPattern(pattern, anchored: false), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        var match = regex.Match(withinText, startIdx);
        if (!match.Success) return ErrorValue.Value;
        return new NumberValue(hasSurrogatePair ? OneBasedTextPositionFromUtf16Index(withinText, match.Index) : match.Index + 1);
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
        if (ContainsSurrogatePair(text))
            return MidTextWithSurrogatePairs(text, (int)rawStart, (int)rawLen);
        int start   = (int)rawStart - 1; // 1-based → 0-based
        int numChars = (int)rawLen;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
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

    private static ScalarValue MidText(string text, int startNum, int numChars)
    {
        if (ContainsSurrogatePair(text))
            return MidTextWithSurrogatePairs(text, startNum, numChars);
        int start = startNum - 1;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
    }

    private static bool ContainsSurrogatePair(string text)
    {
        for (int i = 0; i + 1 < text.Length; i++)
            if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                return true;
        return false;
    }

    private static ScalarValue MidTextWithSurrogatePairs(string text, int startNum, int numChars)
    {
        int start = TextElementIndexFromOneBasedPosition(text, startNum);
        if (start >= text.Length) return new TextValue("");

        int end = AdvanceTextElements(text, start, numChars);
        return TextResult(text[start..end]);
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

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue repeatError) return repeatError;
        return MapBinaryMathArgs(args[0], args[1], ReptScalarWithTimes);
    }

    private static ScalarValue ReptScalarWithTimes(ScalarValue value, ScalarValue timesValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (timesValue is ErrorValue timesError) return timesError;
        var timesD = ToNumber(timesValue);
        if (!double.IsFinite(timesD) || timesD > int.MaxValue) return ErrorValue.Value;
        int times = (int)timesD;
        if (times < 0) return ErrorValue.Value;
        return ReptText(ToText(value), times);
    }

    private static ScalarValue ReptText(string text, int times)
    {
        if ((long)text.Length * times > 32767) return ErrorValue.Value;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < times; i++) sb.Append(text);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ValueScalar);
        return ValueScalar(args[0]);
    }

    private static ScalarValue ValueScalar(ScalarValue value)
    {
        if (value is NumberValue nv) return nv;
        var text = ToText(value).Trim();
        if (ExcelTextNumberParser.TryParse(text, out var d))
            return new NumberValue(d);
        return ErrorValue.Value;
    }
}
