using System.Globalization;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private readonly record struct LocaleFormatSeparators(
        string DecimalSeparator,
        string GroupSeparator,
        string DateSeparator,
        int[]? NumberGroupSizes = null);

    private static readonly IReadOnlyDictionary<string, LocaleFormatSeparators> LocaleFormatCatalog =
        new Dictionary<string, LocaleFormatSeparators>(StringComparer.OrdinalIgnoreCase)
        {
            ["402"] = new(",", "\u00A0", "."),
            ["401"] = new(".", ",", "/"),
            ["409"] = new(".", ",", "/"),
            ["405"] = new(",", " ", "."),
            ["406"] = new(",", ".", "-"),
            ["407"] = new(",", ".", "."),
            ["408"] = new(",", ".", "/"),
            ["404"] = new(".", ",", "/"),
            ["40D"] = new(".", ",", "/"),
            ["40A"] = new(",", ".", "/"),
            ["40B"] = new(",", " ", "."),
            ["40C"] = new(",", " ", "/"),
            ["40E"] = new(",", " ", "."),
            ["410"] = new(",", ".", "/"),
            ["411"] = new(".", ",", "/"),
            ["412"] = new(".", ",", "-"),
            ["413"] = new(",", ".", "-"),
            ["414"] = new(",", " ", "."),
            ["415"] = new(",", " ", "."),
            ["416"] = new(",", ".", "/"),
            ["418"] = new(",", ".", "."),
            ["419"] = new(",", " ", "."),
            ["41A"] = new(",", ".", "."),
            ["41B"] = new(",", "\u00A0", "."),
            ["41D"] = new(",", " ", "-"),
            ["41E"] = new(".", ",", "/"),
            ["41F"] = new(",", ".", "."),
            ["420"] = new(".", ",", "/"),
            ["421"] = new(",", ".", "/"),
            ["422"] = new(",", " ", "."),
            ["424"] = new(",", ".", "."),
            ["425"] = new(",", "\u00A0", "."),
            ["426"] = new(",", "\u00A0", "."),
            ["427"] = new(",", "\u00A0", "-"),
            ["429"] = new("/", ",", "/"),
            ["42A"] = new(",", ".", "/"),
            ["42B"] = new(".", ",", "."),
            ["42C"] = new(",", ".", "."),
            ["434"] = new(".", "\u00A0", "/"),
            ["435"] = new(".", ",", "/"),
            ["436"] = new(",", "\u00A0", "-"),
            ["437"] = new(",", "\u00A0", "."),
            ["439"] = new(".", ",", "-", [3, 2]),
            ["43F"] = new(",", "\u00A0", "."),
            ["440"] = new(",", "\u00A0", "/"),
            ["441"] = new(".", ",", "/"),
            ["443"] = new(",", "\u00A0", "/"),
            ["43E"] = new(".", ",", "/"),
            ["450"] = new(".", ",", "."),
            ["453"] = new(".", ",", "/"),
            ["454"] = new(",", ".", "/"),
            ["455"] = new(".", ",", "/"),
            ["45B"] = new(".", ",", "-"),
            ["45E"] = new(".", ",", "/"),
            ["461"] = new(".", ",", "/"),
            ["463"] = new(",", ".", "/"),
            ["468"] = new(".", ",", "/"),
            ["46A"] = new(".", ",", "/"),
            ["470"] = new(".", ",", "/"),
            ["492"] = new(".", ",", "/"),
            ["804"] = new(".", ",", "/"),
            ["809"] = new(".", ",", "/"),
            ["80A"] = new(".", ",", "/"),
            ["807"] = new(".", "'", "."),
            ["813"] = new(",", ".", "/"),
            ["816"] = new(",", " ", "/"),
            ["100A"] = new(".", ",", "/"),
            ["C04"] = new(".", ",", "/"),
            ["C01"] = new(".", ",", "/"),
            ["C09"] = new(".", ",", "/"),
            ["C0C"] = new(",", "\u00A0", "-"),
            ["C0A"] = new(",", ".", "/"),
            ["1009"] = new(".", ",", "-"),
            ["100C"] = new(".", "'", "."),
            ["1409"] = new(".", ",", "/"),
            ["140A"] = new(",", "\u00A0", "/"),
            ["1801"] = new(".", ",", "-"),
            ["1809"] = new(".", ",", "/"),
            ["180A"] = new(".", ",", "/"),
            ["1C09"] = new(",", "\u00A0", "/"),
            ["1C0A"] = new(".", ",", "/"),
            ["200A"] = new(",", ".", "/"),
            ["241A"] = new(",", ".", "."),
            ["240A"] = new(",", ".", "/"),
            ["280A"] = new(".", ",", "/"),
            ["280C"] = new(",", "\u202F", "/"),
            ["2C0A"] = new(",", ".", "/"),
            ["300A"] = new(",", ".", "/"),
            ["340A"] = new(",", ".", "-"),
            ["380A"] = new(",", ".", "/"),
            ["3801"] = new(".", ",", "/"),
            ["380C"] = new(",", ".", "/"),
            ["3C0A"] = new(",", ".", "/"),
            ["400A"] = new(",", ".", "/"),
            ["4009"] = new(".", ",", "/", [3, 2]),
            ["445"] = new(".", ",", "-", [3, 2]),
            ["447"] = new(".", ",", "-", [3, 2]),
            ["449"] = new(".", ",", "-", [3, 2]),
            ["44A"] = new(".", ",", "-", [3, 2]),
            ["44E"] = new(".", ",", "-", [3, 2]),
            ["440A"] = new(".", ",", "/"),
            ["500A"] = new(".", ",", "/")
        };

    private static string PreserveLocaleCurrencyTokens(
        string format,
        out NumberFormatInfo numberFormat,
        out DateTimeFormatInfo dateTimeFormat)
    {
        numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        dateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat;
        var sb = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }

            if (!inQuote &&
                c == '[' &&
                i + 2 < format.Length &&
                format[i + 1] == '$')
            {
                int close = format.IndexOf(']', i + 2);
                if (close > i)
                {
                    string token = format[(i + 2)..close];
                    int localeSeparator = token.LastIndexOf('-');
                    var localeToken = localeSeparator >= 0 ? token[(localeSeparator + 1)..] : null;
                    if (localeToken is not null &&
                        TryCreateLocaleFormats(localeToken, out var localeNumberFormat, out var localeDateTimeFormat))
                    {
                        numberFormat = localeNumberFormat;
                        dateTimeFormat = localeDateTimeFormat;
                    }

                    if (localeSeparator == 0)
                    {
                        i = close;
                        continue;
                    }

                    string symbol = localeSeparator > 0 ? token[..localeSeparator] : token;
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        sb.Append('"');
                        sb.Append(symbol.Replace("\"", "\"\"", StringComparison.Ordinal));
                        sb.Append('"');
                        if (close + 2 < format.Length &&
                            format[close + 1] == '*' &&
                            format[close + 2] == ' ')
                        {
                            sb.Append(' ');
                            i = close + 2;
                            continue;
                        }

                        i = close;
                        continue;
                    }
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool TryCreateLocaleFormats(
        string localeToken,
        out NumberFormatInfo numberFormat,
        out DateTimeFormatInfo dateTimeFormat)
    {
        numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        dateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat;
        var normalized = localeToken.Trim().TrimStart('0').ToUpperInvariant();
        if (normalized.Length == 0)
            normalized = "0";

        if (!LocaleFormatCatalog.TryGetValue(normalized, out var separators))
            return TryCreateCultureInfoLocaleFormats(normalized, out numberFormat, out dateTimeFormat);

        var hasCultureFormats = TryCreateCultureInfoLocaleFormats(
            normalized,
            out var cultureNumberFormat,
            out var cultureDateTimeFormat);

        numberFormat = hasCultureFormats
            ? (NumberFormatInfo)cultureNumberFormat.Clone()
            : (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numberFormat.NumberDecimalSeparator = separators.DecimalSeparator;
        numberFormat.NumberGroupSeparator = separators.GroupSeparator;
        numberFormat.PercentDecimalSeparator = separators.DecimalSeparator;
        numberFormat.PercentGroupSeparator = separators.GroupSeparator;
        if (separators.NumberGroupSizes is { Length: > 0 } groupSizes)
        {
            numberFormat.NumberGroupSizes = groupSizes;
            numberFormat.PercentGroupSizes = groupSizes;
        }
        dateTimeFormat = hasCultureFormats
            ? (DateTimeFormatInfo)cultureDateTimeFormat.Clone()
            : (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        UseGregorianCalendarWhenAvailable(dateTimeFormat);
        dateTimeFormat.DateSeparator = separators.DateSeparator;
        return true;
    }

    private static void UseGregorianCalendarWhenAvailable(DateTimeFormatInfo dateTimeFormat)
    {
        if (dateTimeFormat.Calendar is GregorianCalendar)
            return;

        try
        {
            dateTimeFormat.Calendar = new GregorianCalendar();
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static bool TryCreateCultureInfoLocaleFormats(
        string normalizedLocaleToken,
        out NumberFormatInfo numberFormat,
        out DateTimeFormatInfo dateTimeFormat)
    {
        numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        dateTimeFormat = CultureInfo.InvariantCulture.DateTimeFormat;

        if (!int.TryParse(
                normalizedLocaleToken,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var lcid))
        {
            return false;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(lcid);
            numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();
            dateTimeFormat = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
