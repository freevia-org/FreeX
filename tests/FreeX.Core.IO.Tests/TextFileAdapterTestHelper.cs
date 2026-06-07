using System.Text;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

internal static class TextFileAdapterTestHelper
{
    internal static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithSheet()
    {
        var workbook = new Workbook("Book1");
        return (workbook, workbook.AddSheet("Sheet1"));
    }

    internal static string SaveToUtf8Text(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static Workbook SaveAndLoad(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    internal static (string SavedText, Workbook RoundTripped) SaveTextAndLoad(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        var savedText = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;
        return (savedText, adapter.Load(stream));
    }
}
