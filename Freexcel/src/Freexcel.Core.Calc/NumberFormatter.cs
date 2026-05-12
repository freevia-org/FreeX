using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public static class NumberFormatter
{
    public static string Format(ScalarValue value, string formatString)
    {
        if (string.IsNullOrEmpty(formatString) || formatString == "General")
            return FormatGeneral(value);

        return value switch
        {
            NumberValue n   => FormatNumber(n.Value, formatString),
            DateTimeValue d => FormatNumber(d.Value, formatString),
            TextValue t     => t.Value,
            BoolValue b     => b.Value ? "TRUE" : "FALSE",
            ErrorValue e    => e.Code,
            BlankValue      => "",
            _               => ""
        };
    }

    // ── General format ────────────────────────────────────────────────────────

    private static string FormatGeneral(ScalarValue value) => value switch
    {
        NumberValue n   => FormatNumberGeneral(n.Value),
        DateTimeValue d => DateTime.FromOADate(d.Value).ToShortDateString(),
        TextValue t     => t.Value,
        BoolValue b     => b.Value ? "TRUE" : "FALSE",
        ErrorValue e    => e.Code,
        BlankValue      => "",
        _               => ""
    };

    private static string FormatNumberGeneral(double value)
    {
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.CurrentCulture);
        return value.ToString("G10", CultureInfo.CurrentCulture);
    }

    // ── Specific format strings ───────────────────────────────────────────────

    private static string FormatNumber(double value, string format)
    {
        // Percentage: multiply by 100 before formatting
        if (format.Contains('%'))
        {
            var pctFmt = format.Replace("%", "").Trim();
            try
            {
                return (value * 100).ToString(pctFmt, CultureInfo.CurrentCulture) + "%";
            }
            catch
            {
                return (value * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
            }
        }

        // Date / time format
        if (IsDateTimeFormat(format))
        {
            try
            {
                var dt = DateTime.FromOADate(value);
                return dt.ToString(ToNetDateFormat(format), CultureInfo.CurrentCulture);
            }
            catch
            {
                return value.ToString(CultureInfo.CurrentCulture);
            }
        }

        // Plain number format — .NET handles most Excel number patterns natively
        try
        {
            return value.ToString(format, CultureInfo.CurrentCulture);
        }
        catch
        {
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }

    // ── Date format detection ─────────────────────────────────────────────────

    // A format is a date/time format when it contains date/time tokens (y, d, h)
    // and does NOT contain number tokens (0, #) which would indicate a number format.
    private static bool IsDateTimeFormat(string format)
    {
        bool hasDateToken = format.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S']) >= 0;
        bool hasNumberToken = format.IndexOfAny(['0', '#']) >= 0;
        return hasDateToken && !hasNumberToken;
    }

    // Map common Excel date format tokens to .NET equivalents.
    // Order matters: replace longer tokens before shorter ones.
    private static string ToNetDateFormat(string excelFmt) =>
        excelFmt
            .Replace("AM/PM", "tt")
            .Replace("am/pm", "tt")
            .Replace("yyyy", "yyyy")
            .Replace("yy",   "yy")
            .Replace("mmmm", "MMMM")
            .Replace("mmm",  "MMM")
            .Replace("mm",   "MM")
            .Replace("m",    "M")
            .Replace("dddd", "dddd")
            .Replace("ddd",  "ddd")
            .Replace("dd",   "dd")
            .Replace("d",    "d")
            .Replace("hh",   "HH")
            .Replace("h",    "H")
            .Replace("ss",   "ss")
            .Replace("s",    "s");
}
