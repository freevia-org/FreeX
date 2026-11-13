using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>Formats canonical Word footnote and endnote reference marks.</summary>
public static class NoteNumberFormatter
{
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

    private static string ToRoman(int value) =>
        value <= MaximumSymbolicNumeral
        && Free.Shared.IO.NumeralFormatter.TryToRoman(value, upper: true, out var roman)
            ? roman
            : value.ToString(CultureInfo.InvariantCulture);

    // The caller has already normalised value to >= 1, so the conversion always succeeds.
    private static string ToLetter(int value, bool lower) =>
        Free.Shared.IO.NumeralFormatter.TryToBijectiveLetters(value, upper: !lower, out var letters)
            ? letters
            : value.ToString(CultureInfo.InvariantCulture);

    private static string ToChicago(int value)
    {
        if (value > MaximumSymbolicNumeral)
            return value.ToString(CultureInfo.InvariantCulture);

        var symbol = ChicagoSymbols[(value - 1) % ChicagoSymbols.Length];
        var repeat = (value - 1) / ChicagoSymbols.Length + 1;
        return string.Concat(Enumerable.Repeat(symbol, repeat));
    }
}
