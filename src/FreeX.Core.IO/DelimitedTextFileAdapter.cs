using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed class DelimitedTextFileAdapter(string extension, string formatName, char formatDelimiter) : IFileAdapter
{
    private readonly char delimiter = ValidateDelimiter(formatDelimiter);

    public string Extension { get; } = extension;
    public string FormatName { get; } = formatName;

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) =>
        DelimitedTextWorkbookReader.Load(stream, delimiter, allowSeparatorDirective: true);

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
