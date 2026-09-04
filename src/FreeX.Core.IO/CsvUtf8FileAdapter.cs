using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// "CSV UTF-8 (Comma delimited)" — Excel's modern CSV Save-As type. Same comma-delimited engine as
/// <see cref="CsvFileAdapter"/>, but the file is written UTF-8 <em>with</em> a byte-order mark so
/// non-ASCII text survives a round-trip through tools that key off the BOM (notably Excel itself).
/// Reading reuses the shared delimited reader, whose BOM detection already strips the UTF-8 BOM.
/// </summary>
public sealed class CsvUtf8FileAdapter : IFileAdapter, ISingleSheetFileAdapter
{
    public string Extension => ".csv";
    public string FormatName => "CSV UTF-8 (Comma delimited)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".csv", "CSV UTF-8 (Comma delimited)", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) => DelimitedTextWorkbookReader.Load(stream, ',', allowSeparatorDirective: true);

    public void Save(Workbook workbook, Stream stream) =>
        DelimitedTextWorkbookWriter.Save(workbook, stream, ',', DelimitedTextWorkbookWriter.Utf8Bom);
}
