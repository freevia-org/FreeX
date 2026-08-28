using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity. <c>table:number-columns-repeated</c> is a COUNT the
/// file declares, not a measure of anything it contains, so a ~600-byte .odt can ask for two billion
/// table columns. Expanding it verbatim allocated 32.7 GB and froze the open for 13s on the reference
/// machine (measured via GC.GetTotalAllocatedBytes; an OutOfMemoryException on a smaller one).
///
/// <see cref="OdtFileAdapterSizeGuardTests"/>'s zip-bomb guard cannot catch this: the package really
/// is tiny, and the amplification happens in the reader's own loop. Same class as the FreeX half of
/// this round, reached through file input rather than a selection.
/// </summary>
public sealed class R164_OdtColumnRepeatTests
{
    [Fact]
    public void Load_AbsurdColumnRepeat_IsCappedInsteadOfAllocatingBillionsOfWidths()
    {
        using var package = BuildOdtWithTable(
            """<table:table table:name="T"><table:table-column table:style-name="co1" table:number-columns-repeated="2000000000"/><table:table-row><table:table-cell><text:p>x</text:p></table:table-cell></table:table-row></table:table>""");

        var document = OdtFileAdapter.Odt().Load(package);

        var table = document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.ColumnWidthsPt.Count.Should().BeLessThanOrEqualTo(1024);
    }

    [Fact]
    public void Load_OrdinaryColumnRepeat_StillExpandsEveryColumnWidth()
    {
        // Sibling/no-regression: the cap sits far above the 63/64-column ceilings Word and
        // LibreOffice Writer impose, so a real document's widths are expanded exactly as before.
        using var package = BuildOdtWithTable(
            """<table:table table:name="T"><table:table-column table:style-name="co1" table:number-columns-repeated="3"/><table:table-row><table:table-cell><text:p>x</text:p></table:table-cell></table:table-row></table:table>""");

        var document = OdtFileAdapter.Odt().Load(package);

        var table = document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.ColumnWidthsPt.Should().HaveCount(3);
        table.ColumnWidthsPt.Should().AllSatisfy(width => width.Should().BeApproximately(56.7, 0.5));
    }

    private static MemoryStream BuildOdtWithTable(string tableXml)
    {
        var content = $"""
<?xml version="1.0" encoding="UTF-8"?>
<office:document-content
    xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
    xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
    xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
    xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
  <office:automatic-styles>
    <style:style style:name="co1" style:family="table-column">
      <style:table-column-properties style:column-width="2cm"/>
    </style:style>
  </office:automatic-styles>
  <office:body>
    <office:text>
      {tableXml}
    </office:text>
  </office:body>
</office:document-content>
""";

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        stream.Position = 0;
        return stream;
    }
}
