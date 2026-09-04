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
    private static IFormatProvider ResolveNumberProvider()
    {
        var culture = CultureInfo.CurrentCulture;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;

        return decimalSeparator.Length == 1 && decimalSeparator[0] == ResolveLocaleDelimiter()
            ? CultureInfo.InvariantCulture
            : culture;
    }

    private static char ResolveLocaleDelimiter()
    {
        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        return !string.IsNullOrEmpty(separator) && separator[0] is not ('\r' or '\n' or '"')
            ? separator[0]
            : ',';
    }
}
