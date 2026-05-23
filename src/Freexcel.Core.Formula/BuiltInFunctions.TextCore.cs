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
        var fmt = ToText(args[1]);
        // Simple inline formatter (avoids depending on Freexcel.Core.Calc)
        var val = args[0];
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
        var text = MultiSpaceRegex.Replace(ToText(args[0]).Trim(), " ");
        return TextResult(text);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToUpperInvariant());
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToLowerInvariant());
    }

    private static ScalarValue Proper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
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
        var text    = ToText(args[0]);
        var oldText = ToText(args[1]);
        var newText = ToText(args[2]);

        if (oldText.Length == 0) return TextResult(text);

        if (args.Count > 3 && args[3] is not BlankValue)
        {
            // Replace the Nth occurrence only
            if (args[3] is ErrorValue e3) return e3;
            double rawInstanceNum = ToNumber(args[3]);
            if (!double.IsFinite(rawInstanceNum) || rawInstanceNum > int.MaxValue) return ErrorValue.Value;
            int instanceNum = (int)rawInstanceNum;
            if (instanceNum < 1) return ErrorValue.Value;
            int count = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                int idx = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                if (count == instanceNum)
                    return TextResult(text[..idx] + newText + text[(idx + oldText.Length)..]);
                pos = idx + oldText.Length;
            }
            return TextResult(text); // instance not found
        }
        else
        {
            return TextResult(text.Replace(oldText, newText, StringComparison.Ordinal));
        }
    }

    private static ScalarValue TextResult(string text) =>
        text.Length > 32767 ? ErrorValue.Value : new TextValue(text);

    private static ScalarValue Find(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum = 1;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            double rawStart = ToNumber(args[2]);
            if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
            startNum = (int)rawStart;
        }
        if (startNum < 1) return ErrorValue.Value;
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
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum = 1;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            double rawStart = ToNumber(args[2]);
            if (!double.IsFinite(rawStart) || rawStart > int.MaxValue) return ErrorValue.Value;
            startNum = (int)rawStart;
        }
        if (startNum < 1) return ErrorValue.Value;
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
        var text    = ToText(args[0]);
        double rawStart = ToNumber(args[1]);
        double rawLen   = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        if (rawStart < 1 || rawLen < 0 || rawStart > int.MaxValue || rawLen > int.MaxValue) return ErrorValue.Value;
        if (ContainsSurrogatePair(text))
            return MidTextWithSurrogatePairs(text, (int)rawStart, (int)rawLen);
        int start   = (int)rawStart - 1; // 1-based → 0-based
        int numChars = (int)rawLen;
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
        var text  = ToText(args[0]);
        var timesD = ToNumber(args[1]);
        if (!double.IsFinite(timesD) || timesD > int.MaxValue) return ErrorValue.Value;
        int times = (int)timesD;
        if (times < 0) return ErrorValue.Value;
        if ((long)text.Length * times > 32767) return ErrorValue.Value;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < times; i++) sb.Append(text);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is NumberValue nv) return nv;
        var text = ToText(args[0]).Trim();
        var usCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        if (text.EndsWith('%') &&
            double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Any,
                usCulture, out var pct))
            return new NumberValue(pct / 100.0);
        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                usCulture, out var d))
            return new NumberValue(d);
        if (TryParseExcelFakeLeapDayValueText(text, usCulture, out var fakeLeapSerial))
            return new NumberValue(fakeLeapSerial);
        if (DateTime.TryParse(text, usCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(IsTimeOnlyText(text) ? dt.TimeOfDay.TotalDays : DateToSerial(dt));
        return ErrorValue.Value;
    }

    private static bool IsTimeOnlyText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('-')) return false;
        if (Regex.IsMatch(trimmed, @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)", RegexOptions.IgnoreCase))
            return false;

        return trimmed.Contains(':')
            || Regex.IsMatch(trimmed, @"\b(?:am|pm)\b", RegexOptions.IgnoreCase);
    }
}
