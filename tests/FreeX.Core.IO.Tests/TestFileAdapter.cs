using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

internal sealed class TestFileAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IFileAdapter
{
    public string Extension => formats[0].Extension;
    public string FormatName => formats[0].FormatName;
    public IReadOnlyList<FileFormatDescriptor> Formats => formats;
    public Workbook Load(Stream stream) => throw new NotSupportedException();
    public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
}
