using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// "Unicode Text (*.txt)" — Excel's tab-delimited Save-As type written as UTF-16 little-endian
/// <em>with</em> a BOM (the encoding Excel uses for this format). Same tab-delimited engine as the
/// plain <see cref="DelimitedTextFileAdapter"/> for <c>.txt</c>; only the on-disk encoding differs.
/// Reading reuses the shared delimited reader, whose BOM detection already handles UTF-16LE.
/// </summary>
public sealed class UnicodeTextFileAdapter : IFileAdapter
{
    public string Extension => ".txt";
    public string FormatName => "Unicode Text";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".txt", "Unicode Text", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) => DelimitedTextWorkbookReader.Load(stream, '\t', allowSeparatorDirective: true);

    public void Save(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.Save(workbook, stream, '\t', DelimitedTextWorkbookWriter.Utf16LeBom);
}
