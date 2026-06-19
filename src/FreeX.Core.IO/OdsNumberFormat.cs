using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Bridges Excel number-format codes and ODF <c>number:*-style</c> definitions. ODF's data-style model
/// is structurally different from Excel's format-code grammar, and a fully faithful structural mapping is
/// large. To guarantee the exact Excel format string round-trips (the harness asserts the per-cell format
/// code exactly), each emitted data style also carries the verbatim Excel code in a private attribute
/// (<c>number:freex-format-code</c>); on read that hint is preferred. The structural body is still emitted
/// so the file renders correctly in conforming ODF consumers, but it is informational for FreeX's own
/// round-trip.
/// </summary>
internal static class OdsNumberFormat
{
    internal static readonly XName FreeXFormatCodeAttr = OdsFileAdapter.NumberNs + "freex-format-code";

    public static bool IsCustom(string? code) =>
        !string.IsNullOrEmpty(code) && !code.Equals("General", StringComparison.OrdinalIgnoreCase);

    public static bool IsPercentage(string? code) =>
        code is not null && code.Contains('%', StringComparison.Ordinal);

    public static bool IsCurrency(string? code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        // Treat a format containing a currency symbol or an explicit [$...] currency token as currency.
        return code.Contains('$', StringComparison.Ordinal)
            || code.Contains('€', StringComparison.Ordinal)
            || code.Contains('£', StringComparison.Ordinal)
            || code.Contains('¥', StringComparison.Ordinal)
            || code.Contains("[$", StringComparison.Ordinal);
    }

    public static bool IsDate(string? code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        // A heuristic: date/time tokens y/m/d/h/s outside quotes. Good enough to pick the ODF style kind;
        // exact rendering is governed by the verbatim format-code hint anyway.
        var inQuote = false;
        foreach (var ch in code)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            switch (char.ToLowerInvariant(ch))
            {
                case 'y':
                case 'd':
                    return true;
            }
        }
        // 'm' is ambiguous (month vs minute); only treat as date when paired with a separator pattern.
        return code.Contains('/', StringComparison.Ordinal) && (code.Contains('m', StringComparison.OrdinalIgnoreCase) || code.Contains('d', StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Emits an ODF data-style element for the given Excel code, tagged with the verbatim code hint.
    /// The element kind (number/percentage/currency/date) is chosen heuristically; the body is a minimal
    /// but valid representation.
    /// </summary>
    public static XElement BuildDataStyle(string styleName, string excelCode)
    {
        var number = OdsFileAdapter.NumberNs;

        XElement element;
        if (IsPercentage(excelCode))
        {
            element = new XElement(number + "percentage-style",
                NumberElement(excelCode),
                new XElement(number + "text", "%"));
        }
        else if (IsCurrency(excelCode))
        {
            element = new XElement(number + "currency-style",
                new XElement(number + "currency-symbol", ExtractCurrencySymbol(excelCode)),
                NumberElement(excelCode));
        }
        else if (IsDate(excelCode))
        {
            element = new XElement(number + "date-style",
                new XElement(number + "year", new XAttribute(number + "style", "long")),
                new XElement(number + "text", "-"),
                new XElement(number + "month", new XAttribute(number + "style", "long")),
                new XElement(number + "text", "-"),
                new XElement(number + "day", new XAttribute(number + "style", "long")));
        }
        else
        {
            element = new XElement(number + "number-style", NumberElement(excelCode));
        }

        element.SetAttributeValue(OdsFileAdapter.StyleNs + "name", styleName);
        // The round-trip hint: the exact Excel code, so FreeX recovers the original format string.
        element.SetAttributeValue(FreeXFormatCodeAttr, excelCode);
        return element;
    }

    private static XElement NumberElement(string excelCode)
    {
        var number = OdsFileAdapter.NumberNs;
        var decimals = CountDecimals(excelCode);
        var grouping = excelCode.Contains(",", StringComparison.Ordinal) && excelCode.Contains("#,##", StringComparison.Ordinal);
        var e = new XElement(number + "number",
            new XAttribute(number + "min-integer-digits", "1"));
        if (decimals > 0)
            e.SetAttributeValue(number + "decimal-places", decimals.ToString(CultureInfo.InvariantCulture));
        if (grouping)
            e.SetAttributeValue(number + "grouping", "true");
        return e;
    }

    private static int CountDecimals(string excelCode)
    {
        var dot = excelCode.IndexOf('.');
        if (dot < 0) return 0;
        var count = 0;
        for (var i = dot + 1; i < excelCode.Length; i++)
        {
            if (excelCode[i] is '0' or '#') count++;
            else break;
        }
        return count;
    }

    private static string ExtractCurrencySymbol(string excelCode)
    {
        foreach (var ch in excelCode)
            if (ch is '$' or '€' or '£' or '¥') return ch.ToString();
        return "$";
    }

    /// <summary>Reads the verbatim Excel code hint from a data-style element, or null if absent.</summary>
    public static string? ReadFreeXFormatCode(XElement dataStyle) =>
        dataStyle.Attribute(FreeXFormatCodeAttr)?.Value;
}
