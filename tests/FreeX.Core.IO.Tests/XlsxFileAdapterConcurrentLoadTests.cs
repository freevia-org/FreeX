using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFileAdapterConcurrentLoadTests
{
    // Regression guard for the intermittent open crash: ClosedXML's XLWorkbook construction shares
    // process-global static state, so running loads/saves on multiple threads at once (e.g. the
    // startup prewarm racing a user open) could corrupt it and crash.  XlsxFileAdapter now serializes
    // all ClosedXML-backed loads and full-saves through a single gate; this test drives many
    // concurrent save+load cycles and asserts they all complete without throwing.
    [Fact]
    public void ConcurrentSaveAndLoad_DoNotRaceOrThrow()
    {
        var template = BuildSmallWorkbookBytes();

        const int threads = 8;
        const int iterationsPerThread = 6;
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(0, threads, _ =>
        {
            try
            {
                for (var i = 0; i < iterationsPerThread; i++)
                {
                    var adapter = new XlsxFileAdapter();

                    using var loadStream = new MemoryStream(template, writable: false);
                    var workbook = adapter.LoadWithWarnings(loadStream, inspectFeatures: true).Workbook;
                    Assert.True(workbook.Sheets.Count >= 1);

                    using var saveStream = new MemoryStream();
                    adapter.Save(workbook, saveStream); // full save -> exercises the ClosedXML write path
                    Assert.True(saveStream.Length > 0);
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        });

        Assert.True(errors.IsEmpty, errors.IsEmpty ? "" : $"Concurrent load/save threw: {errors.First()}");
    }

    private static byte[] BuildSmallWorkbookBytes()
    {
        var workbook = new Workbook("Concurrent");
        for (var s = 1; s <= 2; s++)
        {
            var sheet = workbook.AddSheet($"Sheet{s}");
            for (var row = 1u; row <= 8u; row++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"S{s}-R{row}"));
                sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(row * 10 + s)));
            }
        }

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }
}
