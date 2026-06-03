using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Inline number, date/time, fixed, and currency text formatting.

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
        // Simple inline formatter (avoids depending on FreeX.Core.Calc)
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
        else
        {
            dec = 0;
        }
        return FixedScalar(value, dec, noCommas);
    }

    private static ScalarValue FixedScalar(ScalarValue value, int dec, bool noCommas)
    {
        double n = ToNumber(value);
        return TextResult(FormatRoundedNumber(n, dec, useCommas: !noCommas));
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
