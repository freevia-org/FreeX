using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the HTML (.html/.htm) adapter: import of &lt;table&gt; rows/cells with numeric coercion and
/// colspan/rowspan→merged regions, export of a styled single &lt;table&gt; with display values + inline
/// CSS, and the value/merge-geometry round-trip.
/// </summary>
public sealed class HtmlFileAdapterTests
{
    private static Workbook Load(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return new HtmlFileAdapter().Load(stream);
    }

    private static string SaveToString(Workbook wb)
    {
        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(wb, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Load_ParsesTableRowsAndCells()
    {
        var wb = Load("<table><tr><td>Name</td><td>Qty</td></tr><tr><td>Apple</td><td>12</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Qty"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Apple"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(12));
    }

    [Fact]
    public void Load_CoercesNumbersBooleansAndErrors()
    {
        var wb = Load("<table><tr><td>3.5</td><td>TRUE</td><td>#DIV/0!</td><td>hello</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(3.5));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().BeOfType<ErrorValue>();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Load_DecodesEntitiesAndStripsInnerTags()
    {
        var wb = Load("<table><tr><td>a &amp; b &lt;c&gt;</td><td><b>bold</b> text</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("a & b <c>"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("bold text"));
    }

    [Fact]
    public void Load_ColspanCreatesMergedRegionAndKeepsColumnAlignment()
    {
        var wb = Load("<table>" +
            "<tr><td colspan=\"2\">Spanned</td><td>After</td></tr>" +
            "<tr><td>A</td><td>B</td><td>C</td></tr>" +
            "</table>");
        var sheet = wb.Sheets.Single();

        // "After" must land at column 3 (the colspan reserves cols 1-2).
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Spanned"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("After"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("C"));

        sheet.MergedRegions.Should().ContainSingle(r =>
            r.Start.Row == 1 && r.Start.Col == 1 && r.End.Row == 1 && r.End.Col == 2);
    }

    [Fact]
    public void Load_RowspanReservesColumnInFollowingRow()
    {
        var wb = Load("<table>" +
            "<tr><td rowspan=\"2\">Tall</td><td>R1C2</td></tr>" +
            "<tr><td>R2C2</td></tr>" +
            "</table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Tall"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("R1C2"));
        // The rowspan keeps column 1 occupied in row 2, so "R2C2" must land at column 2, not column 1.
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("R2C2"));

        sheet.MergedRegions.Should().ContainSingle(r =>
            r.Start.Row == 1 && r.Start.Col == 1 && r.End.Row == 2 && r.End.Col == 1);
    }

    [Fact]
    public void Save_EmitsTableWithDisplayValuesAndStyling()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var bold = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 255, 0) });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.GetCell(1, 1)!.StyleId = bold;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        var html = SaveToString(wb);

        html.Should().Contain("<table");
        html.Should().Contain(">Header<");
        html.Should().Contain("font-weight:bold");
        html.Should().Contain("background-color:#FFFF00");
        html.Should().Contain(">42<");
    }

    [Fact]
    public void Save_EmitsColspanAndRowspanForMergedRegions()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Below"));

        var html = SaveToString(wb);

        html.Should().Contain("colspan=\"3\"");
        html.Should().Contain("rowspan=\"2\"");
    }

    [Fact]
    public void RoundTrip_PreservesValuesAndMergeGeometry()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Title"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Item"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new BoolValue(true));

        var adapter = new HtmlFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var got = adapter.Load(stream).Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("Title"));
        got.GetValue(new CellAddress(got.Id, 2, 1)).Should().Be(new TextValue("Item"));
        got.GetValue(new CellAddress(got.Id, 2, 2)).Should().Be(new NumberValue(100));
        got.GetValue(new CellAddress(got.Id, 2, 3)).Should().Be(new BoolValue(true));
        got.MergedRegions.Should().ContainSingle(r =>
            r.Start.Row == 1 && r.Start.Col == 1 && r.End.Row == 1 && r.End.Col == 3);
    }

    [Fact]
    public void Load_OnlyFirstTableIsImported()
    {
        var wb = Load("<table><tr><td>first</td></tr></table><table><tr><td>second</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("first"));
        sheet.CellCount.Should().Be(1);
    }

    [Fact]
    public void Load_EmptyOrTablelessHtmlYieldsEmptySheet()
    {
        var wb = Load("<html><body><p>no tables here</p></body></html>");
        wb.Sheets.Single().CellCount.Should().Be(0);
    }
}
