using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r368: the orphaned-shared-formula recovery path must not throw on the stream it was handed.
///
/// <para>A <c>&lt;f t="shared" si="N"/&gt;</c> with no master on its sheet is a known corruption
/// shape, and FreeX has a last-chance path that strips those slaves and retries the open. That path
/// opened its package in <c>ZipArchiveMode.Update</c> -- but the sanitizer returns the CALLER'S
/// stream unchanged when nothing needed sanitizing, and that stream can be read-only or already
/// closed by an earlier open attempt (a closed <c>MemoryStream</c> reports <c>CanWrite</c> false).
/// The result was <c>ArgumentException("Update mode requires a stream with read, write, and seek
/// capabilities")</c> thrown out of the load: the recovery for an openable-with-repair workbook
/// turned it into an unopenable one.</para>
///
/// <para>The copy has to be an EXPANDABLE MemoryStream. The first fix used
/// <c>new MemoryStream(bytes)</c>, which wraps the array at fixed capacity, and the strip's rewrite
/// then threw <c>NotSupportedException</c> -- one unopenable file traded for another. That is what
/// the second test guards.</para>
/// </summary>
public sealed class R368_OrphanedSharedFormulaFallbackOpensTests
{
    private static MemoryStream PackageWith(string sheetDataXml)
    {
        var workbook = new Workbook("Orphan");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("xl/worksheets/sheet1.xml")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                sheetDataXml + "</worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void AnOrphanedSharedFormulaSlaveStillOpens()
    {
        using var source = PackageWith(
            "<sheetData><row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"7\"/><v>42</v></c></row></sheetData>");

        var open = () => new XlsxFileAdapter().Load(source);

        open.Should().NotThrow(
            "an orphaned shared-formula slave is a corruption Excel degrades on open, and FreeX has a " +
            "path for it -- that path must not fail on the stream it was given");
    }

    [Fact]
    public void TheRecoveredCellKeepsItsCachedValue()
    {
        // Excel's own behaviour for this shape: the formula goes, the cached value stays. Asserting
        // it also stops the fix being "swallow the sheet", which would satisfy a does-not-throw test.
        using var source = PackageWith(
            "<sheetData><row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"7\"/><v>42</v></c></row></sheetData>");

        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value
            .Should().Be(new NumberValue(42), "the cached value survives the strip");
    }
}
