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

    /// <summary>Save <paramref name="wb"/> to HTML and re-load it, returning the reloaded workbook.</summary>
    private static Workbook RoundTrip(Workbook wb)
    {
        var adapter = new HtmlFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    /// <summary>The effective style of (row,col), consulting both value cells and style-only entries.</summary>
    private static CellStyle StyleAt(Workbook wb, uint row, uint col)
    {
        var sheet = wb.Sheets.Single();
        var cell = sheet.GetCell(row, col);
        var id = cell?.StyleId ?? sheet.GetStyleOnly(row, col);
        return id is { } sid ? wb.GetStyle(sid) : CellStyle.Default;
    }

    /// <summary>Build a single-sheet workbook with one styled, value-bearing cell at A1.</summary>
    private static Workbook StyledCell(CellStyle style, ScalarValue value)
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var cell = Cell.FromValue(value);
        cell.StyleId = wb.RegisterStyle(style);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        return wb;
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
    public void Load_CoercesDateAndTimeLiteralsWrittenByTheSaver()
    {
        // These are exactly the shapes HtmlTableWriter.FormatDate emits (date-only, date+time, and a
        // time-only value anchored to the OADate epoch day 1899-12-30). A round-tripped date/time cell
        // must keep its numeric type instead of reloading as text (R29-non-xlsx-format-roundtrip-1).
        var wb = Load("<table><tr><td>2024-01-31</td><td>2024-01-31 13:45:30</td><td>13:45:30</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(DateTimeValue.FromDateTime(new DateTime(2024, 1, 31)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(DateTimeValue.FromDateTime(new DateTime(2024, 1, 31, 13, 45, 30)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new DateTimeValue(new TimeSpan(13, 45, 30).TotalDays));
    }

    [Fact]
    public void Load_TextThatIsNotAnExactDateLiteralStaysText()
    {
        // Sibling case: don't over-match. Plain text and date-ish text in a format the writer never
        // produces (US-style month/day/year) must stay a string, not get coerced into a date.
        var wb = Load("<table><tr><td>hello</td><td>1/31/2024</td></tr></table>");
        var sheet = wb.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("hello"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("1/31/2024"));
    }

    [Fact]
    public void RoundTrip_PreservesDateAndDateTimeValues()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var dateOnly = DateTimeValue.FromDateTime(new DateTime(2024, 1, 31));
        var dateTime = DateTimeValue.FromDateTime(new DateTime(2024, 1, 31, 13, 45, 30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), dateOnly);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), dateTime);
        // Sibling case already covered elsewhere (Load_CoercesNumbersBooleansAndErrors /
        // RoundTrip_PreservesValuesAndMergeGeometry): plain numbers/booleans keep working alongside dates.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(42));

        var got = RoundTrip(wb).Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(dateOnly);
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(dateTime);
        got.GetValue(new CellAddress(got.Id, 1, 3)).Should().Be(new NumberValue(42));
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
    public void Save_LargeMergeDoesNotMaterializeEveryCoveredCell()
    {
        const uint mergedRows = 100_000;
        const uint mergedColumns = 20;
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, new TextValue("Merged"));
        sheet.AddMergedRegion(new GridRange(
            anchor,
            new CellAddress(sheet.Id, mergedRows, mergedColumns)));

        new HtmlFileAdapter().Save(wb, Stream.Null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        new HtmlFileAdapter().Save(wb, Stream.Null);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        allocatedBytes.Should().BeLessThan(
            500_000,
            "HTML export should query the merge index for emitted cells instead of allocating " +
            "one HashSet entry for each of the 2,000,000 covered cells");
    }

    [Fact]
    public void Save_MergeCoverageSourceGuardUsesSheetIndexInsteadOfExpandingAllCells()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("HtmlTableWriter.cs");

        source.Should().Contain("var mergeRegion = hasMergedRegions ? sheet.GetMergeRegion(address) : null;");
        source.Should().NotContain("foreach (var addr in region.AllCells())");
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

    // ---- inline-CSS style round-trip (xlsx->html->xlsx parity, asserted Lossy by the harness) ----------

    [Fact]
    public void RoundTrip_PreservesFontWeightStyleUnderlineSizeAndColor()
    {
        var style = new CellStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            FontName = "Arial",
            FontSize = 14,
            FontColor = new CellColor(0x12, 0x34, 0x56),
        };
        var got = RoundTrip(StyledCell(style, new TextValue("Hi")));
        var s = StyleAt(got, 1, 1);

        s.Bold.Should().BeTrue();
        s.Italic.Should().BeTrue();
        s.Underline.Should().BeTrue();
        s.FontName.Should().Be("Arial");
        s.FontSize.Should().Be(14);
        s.FontColor.Should().Be(new CellColor(0x12, 0x34, 0x56));
    }

    [Fact]
    public void RoundTrip_PreservesSolidFillColor()
    {
        var style = new CellStyle
        {
            FillColor = new CellColor(0xFF, 0xC0, 0x00),
            FillPatternStyle = CellFillPatternStyle.Solid,
        };
        var got = RoundTrip(StyledCell(style, new TextValue("Filled")));
        var s = StyleAt(got, 1, 1);

        s.FillColor.Should().Be(new CellColor(0xFF, 0xC0, 0x00));
        s.FillPatternStyle.Should().Be(CellFillPatternStyle.Solid);
    }

    [Fact]
    public void RoundTrip_PreservesHorizontalAlignment()
    {
        foreach (var align in new[]
        {
            HorizontalAlignment.Left, HorizontalAlignment.Center,
            HorizontalAlignment.Right, HorizontalAlignment.Justify,
        })
        {
            var got = RoundTrip(StyledCell(new CellStyle { HorizontalAlignment = align }, new TextValue("x")));
            StyleAt(got, 1, 1).HorizontalAlignment.Should().Be(align, "alignment {0} should round-trip", align);
        }
    }

    [Fact]
    public void RoundTrip_PreservesPerEdgeBorderStyleAndColor()
    {
        var red = new CellColor(0xCC, 0x00, 0x00);
        var blue = new CellColor(0x00, 0x00, 0xCC);
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, red),
            BorderRight = new CellBorder(BorderStyle.Medium, blue),
            BorderBottom = new CellBorder(BorderStyle.Dashed, red),
            BorderLeft = new CellBorder(BorderStyle.Double, blue),
        };
        var got = RoundTrip(StyledCell(style, new TextValue("Bordered")));
        var s = StyleAt(got, 1, 1);

        s.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, red));
        s.BorderRight.Should().Be(new CellBorder(BorderStyle.Medium, blue));
        s.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dashed, red));
        s.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, blue));
    }

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.Medium)]
    [InlineData(BorderStyle.Thick)]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.Dotted)]
    [InlineData(BorderStyle.Double)]
    public void RoundTrip_EveryModeledBorderStyleSurvives(BorderStyle bstyle)
    {
        var color = new CellColor(0x33, 0x66, 0x99);
        var style = new CellStyle { BorderBottom = new CellBorder(bstyle, color) };
        var got = RoundTrip(StyledCell(style, new TextValue("b")));

        StyleAt(got, 1, 1).BorderBottom.Should().Be(new CellBorder(bstyle, color));
    }

    [Fact]
    public void RoundTrip_PreservesStylingOnFormattedButEmptyCell()
    {
        // A formatted-but-empty (style-only) cell must still carry its CSS through the round-trip, so a
        // styled-but-valueless cell does not silently lose its fill/border.
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));
        var styleId = wb.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(0x00, 0x80, 0x00),
            FillPatternStyle = CellFillPatternStyle.Solid,
            Bold = true,
        });
        sheet.SetStyleOnly(2, 1, styleId); // empty, styled cell directly below the anchor

        var got = RoundTrip(wb);
        var s = StyleAt(got, 2, 1);

        s.Bold.Should().BeTrue();
        s.FillColor.Should().Be(new CellColor(0x00, 0x80, 0x00));
    }

    [Fact]
    public void RoundTrip_DefaultStyledCellEmitsNoCssAndStaysDefault()
    {
        // A cell with the default style emits no inline CSS, so it must reload with the default style
        // (no spurious style registration from an empty style attribute).
        var got = RoundTrip(StyledCell(new CellStyle(), new TextValue("plain")));
        StyleAt(got, 1, 1).Should().Be(CellStyle.Default);
    }
}
