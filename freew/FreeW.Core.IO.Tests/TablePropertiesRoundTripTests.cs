using System.IO;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Write→read round-trip tests for the Table Properties dialog's model surface (w:tblPr / w:trPr / w:tcPr):
/// table preferred width / alignment / indent / cell spacing / text wrapping / default cell margins, row
/// height + rule + cant-split, and per-cell vertical alignment + margins.
/// </summary>
public class TablePropertiesRoundTripTests
{
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
    }
}
