using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// CSV file adapter with RFC 4180 quoting support.
/// </summary>
public sealed class CsvFileAdapter : IFileAdapter, IWarningCollectingFileAdapter
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
        DelimitedTextWorkbookWriter.Save(workbook, stream, ResolveLocaleDelimiter());

    // csv-edge-cases-F1: plain CSV writes the OS ANSI code page (see
    // DelimitedTextWorkbookWriter.ResolveAnsiEncoding), which cannot represent every character —
    // WorkbookSaveService checks for IWarningCollectingFileAdapter (not any concrete adapter type)
    // so this reuses the same "file saved with warnings" pipeline XlsxFileAdapter already surfaces
    // to the user for its own non-fatal, partial-data-loss save outcomes.
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.SaveWithWarnings(workbook, stream, ResolveLocaleDelimiter());

    // Real Excel's plain File>Open/Save-As ".csv" (no "sep=" directive present) does not always use a
    // comma: it uses the OS Regional Settings "List separator", which is ';' on de-DE/fr-FR/es-ES/etc.
    // machines precisely because ',' is their decimal mark. Hardcoding ',' here tore a genuine
    // semicolon-delimited European export apart (decimal-comma numbers like "1,50" contain a stray
    // comma that used to be misread as a field break). "sep=" still overrides this via
    // allowSeparatorDirective, so this only governs the no-directive default. Falls back to ',' if the
    // current culture's separator is empty or collides with a character the format already reserves.
    private static char ResolveLocaleDelimiter()
    {
        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        return !string.IsNullOrEmpty(separator) && separator[0] is not ('\r' or '\n' or '"')
            ? separator[0]
            : ',';
    }
}
