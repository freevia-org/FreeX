using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed class DelimitedTextFileAdapter(string extension, string formatName, char delimiter) : IFileAdapter
{
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
}
