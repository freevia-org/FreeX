using System.Globalization;
using System.Text;

namespace FreeW.Core.Model;

/// <summary>Formats canonical Word footnote and endnote reference marks.</summary>
public static class NoteNumberFormatter
{
    private static readonly (int Value, string Symbol)[] RomanNumerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    private static readonly string[] ChicagoSymbols = ["*", "\u2020", "\u2021", "\u00A7"];

    public static string Format(int value, NoteNumberingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Format(value, options.NumberFormat);
    }

    public static string Format(int value, NoteNumberFormat format)
    {
        var normalized = Math.Max(1, value);
        return format switch
        {
            NoteNumberFormat.LowerRoman => ToRoman(normalized).ToLowerInvariant(),
            NoteNumberFormat.UpperRoman => ToRoman(normalized),
            NoteNumberFormat.LowerLetter => ToLetter(normalized, lower: true),
            NoteNumberFormat.UpperLetter => ToLetter(normalized, lower: false),
            NoteNumberFormat.Chicago => ToChicago(normalized),
            _ => normalized.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Above this the numeral degrades to the plain decimal number. Both numeral schemes below emit
    /// a symbol PER UNIT once they run out of larger symbols, so their output grows linearly with the
    /// value: a footnote numbering start of 2,000,000,000 -- which a .docx may declare through
    /// <c>w:numStart</c>, and the reader accepts as long as it is >= 1 -- produced a 2,000,000-char
    /// roman numeral and a 500,000,000-char (1 GB) Chicago numeral (both measured). 3999 is the
    /// classic roman limit and the threshold the other three copies of this helper in the suite
    /// already use (ComplexFieldEngine, HtmlFileAdapter, FreeP's PresentationListMarkerPlanner).
    /// </summary>
    private const int MaximumSymbolicNumeral = 3999;

    private static string ToRoman(int value)
    {
        if (value > MaximumSymbolicNumeral)
            return value.ToString(CultureInfo.InvariantCulture);

        var remaining = value;
        var result = new StringBuilder();
        foreach (var (number, symbol) in RomanNumerals)
        {
            while (remaining >= number)
            {
                result.Append(symbol);
                remaining -= number;
            }
        }

        return result.ToString();
    }

    private static string ToLetter(int value, bool lower)
    {
        var result = new StringBuilder();
        while (value > 0)
        {
            value--;
            result.Insert(0, (char)((lower ? 'a' : 'A') + value % 26));
            value /= 26;
        }

        return result.ToString();
    }

    private static string ToChicago(int value)
    {
        if (value > MaximumSymbolicNumeral)
            return value.ToString(CultureInfo.InvariantCulture);

        var symbol = ChicagoSymbols[(value - 1) % ChicagoSymbols.Length];
        var repeat = (value - 1) / ChicagoSymbols.Length + 1;
        return string.Concat(Enumerable.Repeat(symbol, repeat));
    }
}
