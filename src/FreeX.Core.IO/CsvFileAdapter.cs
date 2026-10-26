using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// CSV file adapter with RFC 4180 quoting support.
/// </summary>
public sealed class CsvFileAdapter : IFileAdapter, IWarningCollectingFileAdapter, ISingleSheetFileAdapter
{
    public string Extension => ".csv";
    public string FormatName => "CSV (Comma-separated values)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) =>
        DelimitedTextWorkbookReader.Load(stream, ResolveLocaleDelimiter(), allowSeparatorDirective: true);

    public void Save(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.Save(workbook, stream, ResolveLocaleDelimiter(), ResolveNumberProvider());

    // csv-edge-cases-F1: plain CSV writes the OS ANSI code page (see
    // DelimitedTextWorkbookWriter.ResolveAnsiEncoding), which cannot represent every character —
    // WorkbookSaveService checks for IWarningCollectingFileAdapter (not any concrete adapter type)
    // so this reuses the same "file saved with warnings" pipeline XlsxFileAdapter already surfaces
    // to the user for its own non-fatal, partial-data-loss save outcomes.
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.SaveWithWarnings(
            workbook, stream, ResolveLocaleDelimiter(), ResolveNumberProvider());

    // Real Excel's plain File>Open/Save-As ".csv" (no "sep=" directive present) does not always use a
    // comma: it uses the OS Regional Settings "List separator", which is ';' on de-DE/fr-FR/es-ES/etc.
    // machines precisely because ',' is their decimal mark. Hardcoding ',' here tore a genuine
    // semicolon-delimited European export apart (decimal-comma numbers like "1,50" contain a stray
    // comma that used to be misread as a field break). "sep=" still overrides this via
    // allowSeparatorDirective, so this only governs the no-directive default. Falls back to ',' if the
    // current culture's separator is empty or collides with a character the format already reserves.
    // The other half of the rule above. Excel writes ';' on de-DE BECAUSE ',' is the decimal mark
    // there -- which means it also writes "1,50", not "1.50". Following the locale for the delimiter
    // but not for the decimal point produced "3.14;", a combination no locale writes: real Excel on
    // such a machine imports that as TEXT, not a number, so every number FreeX exported was unusable
    // for the user it was localised for. The read path was already bicultural
    // (DelimitedTextWorkbookReader.TryParseFiniteNumber tries the current culture then the invariant
    // one), so this only ever affected files leaving FreeX.
    //
    // Guarded: if a culture's list separator IS its decimal separator, locale numbers would produce
    // ambiguous fields, so that culture keeps invariant numbers. Cells carrying an explicit number
    // format are unaffected -- those already render through NumberFormatter.
    // r603: a second collision of the same shape, and a far more damaging one. Plain CSV is written
    // in the culture's ANSI code page (DelimitedTextWorkbookWriter.ResolveAnsiEncoding), and on
    // Arabic and Persian machines the characters that culture's own number formatter produces are
    // not IN that code page: fa-IR renders -3.14 as U+200E U+2212 3 U+066B 14, and CP1256 holds
    // none of U+200E, U+2212 or U+066B. Every one became a literal '?', so the file read back as
    // "?3?14" -- text, not a number. Measured, not inferred: R392 fails for fa-IR and ar-SA on
    // CsvFileAdapter alone, with "no numeric cell".
    //
    // Excel does not hit this because Windows NLS gives those locales an ASCII '-' and '.', while
    // .NET on ICU gives the typographic forms; aligning with Excel therefore means writing numbers
    // the target encoding can actually carry, not reproducing ICU's typography. Same remedy as the
    // delimiter collision above -- fall back to invariant numbers for that culture only.
    private static IFormatProvider ResolveNumberProvider()
    {
        var culture = CultureInfo.CurrentCulture;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;

        if (decimalSeparator.Length == 1 && decimalSeparator[0] == ResolveLocaleDelimiter())
            return CultureInfo.InvariantCulture;

        return AnsiEncodingCanCarryNumbersOf(culture) ? culture : CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// True when every character this culture's number formatter can emit survives a round trip
    /// through the ANSI code page the plain-CSV writer uses. Probes an actual formatted value rather
    /// than enumerating NumberFormatInfo fields, so a sign, separator or native digit added by a
    /// future ICU version is covered without this list being updated.
    /// </summary>
    private static bool AnsiEncodingCanCarryNumbersOf(CultureInfo culture)
    {
        var encoding = DelimitedTextWorkbookWriter.ResolveAnsiEncoding();

        // Negative and positive, fractional and grouped: between them these reach the negative sign,
        // the positive sign, the decimal separator, the group separator and the digits themselves.
        foreach (var probe in new[] { (-1234567.89).ToString(culture), 1234567.89.ToString(culture) })
        {
            if (!string.Equals(encoding.GetString(encoding.GetBytes(probe)), probe, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static char ResolveLocaleDelimiter()
    {
        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        return !string.IsNullOrEmpty(separator) && separator[0] is not ('\r' or '\n' or '"')
            ? separator[0]
            : ',';
    }
}
