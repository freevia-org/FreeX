using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed class DelimitedTextFileAdapter(string extension, string formatName, char delimiter) : IFileAdapter
{
    public string Extension { get; } = extension;
    public string FormatName { get; } = formatName;

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: false)
    ];

    public Workbook Load(Stream stream) => DelimitedTextWorkbookReader.Load(stream, delimiter);

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException($"{FormatName} is currently open-only. Use Save As Excel Workbook instead.");

}
