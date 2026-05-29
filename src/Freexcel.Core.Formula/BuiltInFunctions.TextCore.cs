using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core text functions and inline text formatting helpers.

    private static ScalarValue Concat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is RangeValue range)
            {
                foreach (var cell in range.Flatten())
                {
                    if (cell is ErrorValue cellError) return cellError;
                    sb.Append(ToText(cell));
                }

                continue;
            }

            sb.Append(ToText(arg));
        }
        return TextResult(sb.ToString());
    }

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
                    if (value is ErrorValue e) return e;
                    cells[r, c] = LenScalar(value);
                }

            return new RangeValue(cells);
        }

        return LenScalar(args[0]);
    }

    private static ScalarValue LenScalar(ScalarValue value)
    {
        var text = ToText(value);
        return new NumberValue(ContainsSurrogatePair(text) ? CountTextElements(text) : text.Length);
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
        var countArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, LeftScalarWithCount);
    }

    private static ScalarValue LeftB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var countArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
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
        if (ContainsSurrogatePair(text))
            return TextResult(text[..AdvanceTextElements(text, 0, count)]);
        return TextResult(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var countArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], countArg, RightScalarWithCount);
    }

    private static ScalarValue RightB(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var countArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
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
        int start = ContainsSurrogatePair(text)
            ? AdvanceTextElements(text, 0, Math.Max(0, CountTextElements(text) - count))
            : text.Length - count;
        return TextResult(text[start..]);
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
        var text = MultiSpaceRegex.Replace(ToText(value).Trim(' '), " ");
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

        var regex = SearchCache.GetOrAdd(findText, pattern =>
        {
            return new Regex(WildcardToRegexPattern(pattern, anchored: false), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        var match = regex.Match(withinText, startIdx);
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
        var regex = SearchCache.GetOrAdd(findText, pattern =>
        {
            return new Regex(WildcardToRegexPattern(pattern, anchored: false), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        var match = regex.Match(withinText, startIdx);
        return match.Success ? new NumberValue(DbcsBytePositionFromUtf16Index(withinText, match.Index)) : ErrorValue.Value;
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
        if (!double.IsFinite(timesD) || timesD < 0 || timesD > int.MaxValue) return ErrorValue.Value;
        int times = (int)timesD;
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

    private static ScalarValue Concatenate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var rangeIndex = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e) return e;
            if (args[i] is RangeValue)
            {
                if (rangeIndex >= 0) return ErrorValue.Value;
                rangeIndex = i;
            }
        }

        if (rangeIndex >= 0)
            return MapConcatenateRange((RangeValue)args[rangeIndex], args, rangeIndex);

        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            sb.Append(ToText(a));
        }
        return TextResult(sb.ToString());
    }

    private static RangeValue MapConcatenateRange(RangeValue range, IReadOnlyList<ScalarValue> args, int rangeIndex)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e)
                {
                    cells[r, c] = e;
                    continue;
                }

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < args.Count; i++)
                    sb.Append(i == rangeIndex ? ToText(value) : ToText(args[i]));
                cells[r, c] = TextResult(sb.ToString());
            }

        return new RangeValue(cells);
    }

    private static ScalarValue TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, TScalar);
        return TScalar(args[0]);
    }

    private static ScalarValue TScalar(ScalarValue value) =>
        value switch
        {
            ErrorValue e => e,
            TextValue t => TextResult(t.Value),
            _ => new TextValue("")
        };

    private static ScalarValue Hyperlink(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 1 && args[0] is RangeValue && args[1] is RangeValue)
            return MapBinaryMathArgs(args[0], args[1], HyperlinkScalar);
        if (args.Count > 1 && args[1] is RangeValue friendlyRange)
            return MapUnaryTextRange(friendlyRange, value => HyperlinkScalar(args[0], value));
        if (args[0] is RangeValue linkRange)
            return MapUnaryTextRange(linkRange, value => HyperlinkScalar(value, args.Count > 1 ? args[1] : null));

        return HyperlinkScalar(args[0], args.Count > 1 ? args[1] : null);
    }

    private static ScalarValue HyperlinkScalar(ScalarValue link, ScalarValue? friendlyName)
    {
        var display = friendlyName is not null && friendlyName is not BlankValue ? ToText(friendlyName) : ToText(link);
        return TextResult(display);
    }

    private static ScalarValue Fixed(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var decimalsArg = args.Count > 1 ? args[1] : new NumberValue(2);
        var noCommasArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapTernaryTextArgs(args[0], decimalsArg, noCommasArg, FixedScalarWithArgs);
    }

    private static ScalarValue FixedScalarWithArgs(ScalarValue value, ScalarValue decimalsValue, ScalarValue noCommasValue)
    {
        if (noCommasValue is ErrorValue noCommasError) return noCommasError;
        bool noCommas = noCommasValue is not BlankValue && ToBool(noCommasValue);
        return FixedScalarWithDecimals(value, decimalsValue, noCommas);
    }

    private static ScalarValue FixedScalarWithDecimals(ScalarValue value, ScalarValue decimalsValue, bool noCommas)
    {
        if (value is ErrorValue valueError) return valueError;
        if (decimalsValue is ErrorValue decimalsError) return decimalsError;
        int dec = 2;
        if (decimalsValue is not BlankValue)
        {
            double rawDec = ToNumber(decimalsValue);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        return FixedScalar(value, dec, noCommas);
    }

    private static ScalarValue FixedScalar(ScalarValue value, int dec, bool noCommas)
    {
        double n = ToNumber(value);
        return TextResult(FormatRoundedNumber(n, dec, useCommas: !noCommas));
    }

    private static ScalarValue Clean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CleanText);
        return CleanText(args[0]);
    }

    private static ScalarValue CleanText(ScalarValue value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in ToText(value))
            if (c >= 32) sb.Append(c);
        return TextResult(sb.ToString());
    }

    private static ScalarValue Dollar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var decimalsArg = args.Count > 1 ? args[1] : new NumberValue(2);
        return MapBinaryMathArgs(args[0], decimalsArg, DollarScalarWithDecimals);
    }

    private static ScalarValue DollarScalarWithDecimals(ScalarValue value, ScalarValue decimalsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (decimalsValue is ErrorValue decimalsError) return decimalsError;
        int dec = 2;
        if (decimalsValue is BlankValue)
        {
            dec = 0;
        }
        else
        {
            double rawDec = ToNumber(decimalsValue);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        return DollarScalar(value, dec);
    }

    private static ScalarValue DollarScalar(ScalarValue value, int dec)
    {
        double n = ToNumber(value);
        var numberText = FormatRoundedNumber(Math.Abs(n), dec, useCommas: true);
        var formatted = "$" + numberText;
        return TextResult(n < 0 && (dec >= 0 || numberText != "0") ? "(" + formatted + ")" : formatted);
    }

    private static string FormatRoundedNumber(double value, int decimals, bool useCommas)
    {
        if (!double.IsFinite(value)) throw new FormulaEvalException("#NUM!", "Invalid number");
        if (decimals > 32767) throw new FormulaEvalException("#VALUE!", "Formatted text exceeds Excel cell text limit");

        double rounded = decimals <= 15 ? RoundWithExcelDigits(value, decimals) : value;
        int displayDecimals = Math.Clamp(decimals, 0, 99); // .NET "N"/"F" format supports 0-99 only
        string format = (useCommas ? "N" : "F") + displayDecimals;
        return rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
