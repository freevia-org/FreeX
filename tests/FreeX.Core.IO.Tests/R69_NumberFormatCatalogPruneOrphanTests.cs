using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R69-io-numfmt-styles-6-1: XlsxNumberFormatCatalogWriter.BuildNumberFormatCatalog copied every
/// entry in <see cref="Workbook.NumberFormatCatalog"/> verbatim into styles.xml on every save,
/// including custom numFmts (id&gt;=164) loaded from the ORIGINAL file that no live cell / style-only
/// run / conditional-format dxf references any more (e.g. because an in-app edit cleared the only
/// cell that used it) -- resurrecting them as orphaned &lt;numFmt&gt; entries on every subsequent
/// save. The fix prunes the catalog to only formats whose format CODE is still referenced by a live
/// style before (re-)writing it.
/// </summary>
public sealed class R69_NumberFormatCatalogPruneOrphanTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_OrphanedCatalogFormatWithNoLiveReference_IsNotReEmitted()
    {
        // Simulates loading a file whose original styles.xml carried custom numFmt 164 = '0.0"kg"',
        // then clearing the only cell that used it and adding a different custom format to another
        // cell -- the orphaned catalog entry must not be resurrected in the saved package.
        const string orphanFormat = "0.0\"kg\"";
        const string liveFormat = "0.0\"lb\"";

        var workbook = new Workbook("NumFmt");
        var sheet = workbook.AddSheet("Sheet1");

        // workbook.NumberFormatCatalog is populated wholesale from the original file's numFmts on
        // load (XlsxWorkbookMetadataReader.LoadNumberFormatCatalog), independent of what's actually
        // referenced by a live cell -- set it directly here to simulate that load-time snapshot.
        workbook.NumberFormatCatalog[164] = orphanFormat;

        // A different custom format IS live -- applied to an actual cell.
        var liveStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = liveFormat });
        var cell = Cell.FromValue(new NumberValue(2.5));
        cell.StyleId = liveStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var numFmts = SaveAndReadNumFmts(workbook);

        numFmts.Should().NotContain(
            f => HasFormatCode(f, orphanFormat),
            "an orphaned catalog entry with no live cell/style-only/dxf reference must not be re-emitted");
        numFmts.Should().Contain(
            f => HasFormatCode(f, liveFormat),
            "a format actually applied to a live cell must still be written");
    }

    /// <summary>
    /// No-regression sibling: a catalog entry whose format code is STILL applied to a live cell must
    /// survive the prune, exactly as it did before this fix.
    /// </summary>
    [Fact]
    public void Save_CatalogFormatStillReferencedByLiveCell_IsPreserved()
    {
        const string stillLiveFormat = "0.0\"kg\"";

        var workbook = new Workbook("NumFmt");
        var sheet = workbook.AddSheet("Sheet1");

        workbook.NumberFormatCatalog[164] = stillLiveFormat;

        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = stillLiveFormat });
        var cell = Cell.FromValue(new NumberValue(1.5));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var numFmts = SaveAndReadNumFmts(workbook);

        numFmts.Should().Contain(
            f => HasFormatCode(f, stillLiveFormat),
            "a catalog entry still referenced by a live cell must be preserved");
    }

    private static bool HasFormatCode(XElement numFmt, string formatCode) =>
        numFmt.Attribute("formatCode")?.Value == formatCode;

    private static List<XElement> SaveAndReadNumFmts(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesEntry = archive.GetEntry("xl/styles.xml")!;
        using var read = stylesEntry.Open();
        var stylesXml = XDocument.Load(read);
        return stylesXml.Root!.Element(WorkbookNs + "numFmts")?.Elements(WorkbookNs + "numFmt").ToList()
            ?? [];
    }
}
