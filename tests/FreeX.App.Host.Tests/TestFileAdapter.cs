using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

internal sealed class TestFileAdapter(
    Func<Stream, Workbook>? load = null,
    Action<Workbook, Stream>? save = null,
    string extension = ".fxjson",
    string formatName = "Fake") : IFileAdapter
{
    public string Extension => extension;
    public string FormatName => formatName;

    public Workbook Load(Stream stream)
    {
        if (load is null)
        {
            throw new NotSupportedException();
        }

        return load(stream);
    }

    public void Save(Workbook workbook, Stream stream)
    {
        if (save is null)
        {
            throw new NotSupportedException();
        }

        save(workbook, stream);
    }
}
