using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed class DelimitedTextFileAdapter(
    string extension,
    string formatName,
    char formatDelimiter,
    bool allowSeparatorDirective = true,
    bool collapseConsecutiveDelimiters = false) : IFileAdapter
{
    private readonly char delimiter = ValidateDelimiter(formatDelimiter);

    public string Extension { get; } = extension;
    public string FormatName { get; } = formatName;

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: true)
    ];

    // R88-io-text-import-wizard-5-3: a plain File-Open/double-click load (the WorkbookFileAdapterCatalog
    // .txt/.tsv/.tab registrations) has no explicit user delimiter choice to protect, so it keeps honoring
    // an embedded "sep=X" directive by default (matching Excel's fast-open heuristic). The Get Data wizard
    // constructs its own adapter instance per import and must pass allowSeparatorDirective: false whenever
    // the user picked a delimiter explicitly (i.e. did not leave it on Detect), so that choice isn't
    // silently overridden by a "sep=" line that happens to be present in the file.
    public Workbook Load(Stream stream) =>
        DelimitedTextWorkbookReader.Load(stream, delimiter, allowSeparatorDirective, collapseConsecutiveDelimiters);

    public void Save(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.Save(workbook, stream, delimiter);

    private static char ValidateDelimiter(char delimiter)
    {
        if (delimiter is '\r' or '\n')
            throw new ArgumentException("Delimited text field delimiter cannot be a line break.", nameof(delimiter));
        if (delimiter is '"')
            throw new ArgumentException("Delimited text field delimiter cannot be the quote character.", nameof(delimiter));

        return delimiter;
    }
}
