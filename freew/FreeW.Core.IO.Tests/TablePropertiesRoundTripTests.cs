using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Write→read round-trip tests for the Table Properties dialog's model surface (w:tblPr / w:trPr / w:tcPr):
/// table preferred width / alignment / indent / cell spacing / text wrapping / default cell margins, row
/// height + rule + cant-split, and per-cell vertical alignment + margins.
/// </summary>
public class TablePropertiesRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static Table SingleTableAfterRoundTrip(Table table)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(table);
        return RoundTrip(doc).Blocks.OfType<Table>().Single();
    }

    private static byte[] DocumentBytes(Table table)
    {
        var document = new TextDocument();
        document.Blocks.Add(table);
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XElement DocumentXml(Table table)
    {
        using var archive = new ZipArchive(new MemoryStream(DocumentBytes(table)), ZipArchiveMode.Read);
        using var entry = archive.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Root!;
    }

    [Fact]
    public void Table_PreferredWidthAlignmentIndentSpacingWrapping_RoundTrip()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("x");
        table.PreferredWidthPt = 360;
        table.Alignment = TableAlignment.Center;
        table.IndentFromLeftPt = 18;
        table.CellSpacingPt = 3;
        table.TextWrapping = true;

        var read = SingleTableAfterRoundTrip(table);

        read.PreferredWidthPt.Should().BeApproximately(360, 0.05);
        read.Alignment.Should().Be(TableAlignment.Center);
        read.IndentFromLeftPt.Should().BeApproximately(18, 0.05);
        read.CellSpacingPt.Should().BeApproximately(3, 0.05);
        read.TextWrapping.Should().BeTrue();
        read.FloatingPosition.Should().Be(TableFloatingPosition.WordCompatibleDefault);
    }

    [Fact]
    public void Table_FloatingPositionAndOverlap_EmitExactAttributesAndRoundTrip()
    {
        var table = Table.Create(1, 1);
        table.ColumnWidthsPt.Add(100);
        table.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Margin,
            VerticalAnchor: TableVerticalAnchor.Page,
            HorizontalOffsetPt: -12.5,
            VerticalOffsetPt: 15.25,
            HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
            VerticalAlignment: TableVerticalPositionAlignment.Inside,
            LeftFromTextPt: 1.5,
            RightFromTextPt: 2.5,
            TopFromTextPt: 3.5,
            BottomFromTextPt: 4.5);
        table.FloatingTableAllowsOverlap = false;

        var tblPr = DocumentXml(table).Descendants(W + "tblPr").Single();
        tblPr.Elements().Take(3).Select(element => element.Name.LocalName)
            .Should().Equal("tblpPr", "tblOverlap", "tblW");
        var position = tblPr.Element(W + "tblpPr")!;
        position.Attribute(W + "leftFromText")!.Value.Should().Be("30");
        position.Attribute(W + "rightFromText")!.Value.Should().Be("50");
        position.Attribute(W + "topFromText")!.Value.Should().Be("70");
        position.Attribute(W + "bottomFromText")!.Value.Should().Be("90");
        position.Attribute(W + "vertAnchor")!.Value.Should().Be("page");
        position.Attribute(W + "horzAnchor")!.Value.Should().Be("margin");
        position.Attribute(W + "tblpXSpec")!.Value.Should().Be("outside");
        position.Attribute(W + "tblpX")!.Value.Should().Be("-250");
        position.Attribute(W + "tblpYSpec")!.Value.Should().Be("inside");
        position.Attribute(W + "tblpY")!.Value.Should().Be("305");
        tblPr.Element(W + "tblOverlap")!.Attribute(W + "val")!.Value.Should().Be("never");

        using (var package = WordprocessingDocument.Open(new MemoryStream(DocumentBytes(table)), false))
        {
            new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
                .Where(error => error.ErrorType == ValidationErrorType.Schema)
                .Should().BeEmpty();
        }

        var read = SingleTableAfterRoundTrip(table);
        read.FloatingPosition.Should().Be(table.FloatingPosition);
        read.FloatingTableAllowsOverlap.Should().BeFalse();
    }

    [Fact]
    public void Table_DefaultCellMargins_RoundTrip()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("x");
        table.DefaultCellMargins = new TableCellMargins(TopPt: 2, LeftPt: 6, BottomPt: 2, RightPt: 6);

        var read = SingleTableAfterRoundTrip(table);

        read.DefaultCellMargins.Should().NotBeNull();
        read.DefaultCellMargins!.TopPt.Should().BeApproximately(2, 0.05);
        read.DefaultCellMargins.LeftPt.Should().BeApproximately(6, 0.05);
        read.DefaultCellMargins.BottomPt.Should().BeApproximately(2, 0.05);
        read.DefaultCellMargins.RightPt.Should().BeApproximately(6, 0.05);
    }

    [Fact]
    public void Table_RowHeightRuleAndCantSplit_RoundTrip()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("x");
        table.Rows[0].HeightPt = 40;
        table.Rows[0].HeightRule = TableRowHeightRule.Exact;
        table.Rows[0].AllowBreakAcrossPages = false;

        var read = SingleTableAfterRoundTrip(table);

        read.Rows[0].HeightPt.Should().BeApproximately(40, 0.05);
        read.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Exact);
        read.Rows[0].AllowBreakAcrossPages.Should().BeFalse();
    }

    [Fact]
    public void Table_CellVerticalAlignmentAndMargins_RoundTrip()
    {
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("centered")
        {
            VerticalAlignment = TableCellVerticalAlignment.Center,
            Margins = new TableCellMargins(TopPt: 3, LeftPt: 9, BottomPt: 3, RightPt: 9)
        };
        table.Rows[0].Cells[1] = new TableCell("bottom") { VerticalAlignment = TableCellVerticalAlignment.Bottom };

        var read = SingleTableAfterRoundTrip(table);

        var first = read.Rows[0].Cells[0];
        first.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        first.Margins.Should().NotBeNull();
        first.Margins!.LeftPt.Should().BeApproximately(9, 0.05);
        first.Margins.RightPt.Should().BeApproximately(9, 0.05);

        var second = read.Rows[0].Cells[1];
        second.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Bottom);
        second.Margins.Should().BeNull();
    }

    [Fact]
    public void Table_CellWrapAndFitText_EmitOnlyNonDefaultsAndRoundTrip()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell("no wrap") { WrapText = false };
        table.Rows[0].Cells[1] = new TableCell("fit")
        {
            FitText = true,
            VerticalAlignment = TableCellVerticalAlignment.Center,
            TextDirection = CellTextDirection.Rotate90,
            Margins = new TableCellMargins(1, 2, 3, 4)
        };
        table.Rows[0].Cells[2] = new TableCell("defaults");

        var cells = DocumentXml(table).Descendants(W + "tc").ToList();
        cells[0].Element(W + "tcPr")!.Elements(W + "noWrap").Should().ContainSingle();
        cells[0].Element(W + "tcPr")!.Elements(W + "tcFitText").Should().BeEmpty();
        cells[1].Element(W + "tcPr")!.Elements(W + "noWrap").Should().BeEmpty();
        cells[1].Element(W + "tcPr")!.Elements(W + "tcFitText").Should().ContainSingle();
        cells[1].Element(W + "tcPr")!.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("tcMar", "textDirection", "tcFitText", "vAlign");
        cells[2].Element(W + "tcPr").Should().BeNull();

        var read = SingleTableAfterRoundTrip(table);
        read.Rows[0].Cells[0].WrapText.Should().BeFalse();
        read.Rows[0].Cells[0].FitText.Should().BeFalse();
        read.Rows[0].Cells[1].WrapText.Should().BeTrue();
        read.Rows[0].Cells[1].FitText.Should().BeTrue();
        read.Rows[0].Cells[2].WrapText.Should().BeTrue();
        read.Rows[0].Cells[2].FitText.Should().BeFalse();
    }

    [Fact]
    public void Table_PlainTable_HasDefaultPropertiesAfterRoundTrip()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("x");

        var read = SingleTableAfterRoundTrip(table);

        read.PreferredWidthPt.Should().BeNull();
        read.Alignment.Should().Be(TableAlignment.Left);
        read.IndentFromLeftPt.Should().BeNull();
        read.CellSpacingPt.Should().BeNull();
        read.TextWrapping.Should().BeFalse();
        read.DefaultCellMargins.Should().BeNull();
        read.Rows[0].HeightPt.Should().BeNull();
        read.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Auto);
        read.Rows[0].AllowBreakAcrossPages.Should().BeTrue();
        read.Rows[0].Cells[0].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        read.Rows[0].Cells[0].Margins.Should().BeNull();
        read.Rows[0].Cells[0].WrapText.Should().BeTrue();
        read.Rows[0].Cells[0].FitText.Should().BeFalse();
    }
}
