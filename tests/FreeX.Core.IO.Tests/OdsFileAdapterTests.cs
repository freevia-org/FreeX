using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for the OpenDocument Spreadsheet (.ods) adapter. These assert the adapter's own
/// fidelity directly (save -> load, no intermediate rebuild hop), covering every dimension marked Full
/// in the ODS capability profile: values + types, A1<->OpenFormula formulas, number formats, fonts /
/// fills / borders / alignment, merged cells, multiple sheets + names, and column/row sizes.
/// </summary>
public sealed class OdsFileAdapterTests
{
    private static Workbook RoundTrip(Workbook source)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var wb = new Workbook("Untitled");
        sheet = wb.AddSheet("Sheet1");
        return wb;
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value, StyleId? style = null)
    {
        var cell = Cell.FromValue(value);
        if (style is { } s) cell.StyleId = s;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }

    [Fact]
    public void RoundTrips_ScalarValueTypes()
    {
        var wb = NewWorkbook(out var sheet);
        Set(sheet, 1, 1, new TextValue("hello"));
        Set(sheet, 1, 2, new NumberValue(42.5));
        Set(sheet, 1, 3, new BoolValue(true));
        Set(sheet, 1, 4, new BoolValue(false));
        Set(sheet, 1, 5, DateTimeValue.FromDateTime(new DateTime(2024, 1, 31)));
        Set(sheet, 1, 6, ErrorValue.DivByZero);
        Set(sheet, 1, 7, new TextValue("multi\nline"));

        var got = RoundTrip(wb).Sheets.Single();
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("hello"));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new NumberValue(42.5));
        got.GetValue(new CellAddress(got.Id, 1, 3)).Should().Be(new BoolValue(true));
        got.GetValue(new CellAddress(got.Id, 1, 4)).Should().Be(new BoolValue(false));
        got.GetValue(new CellAddress(got.Id, 1, 5)).Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Date.Should().Be(new DateTime(2024, 1, 31));
        got.GetValue(new CellAddress(got.Id, 1, 6)).Should().BeOfType<ErrorValue>()
            .Which.Code.Should().Be("#DIV/0!");
        got.GetValue(new CellAddress(got.Id, 1, 7)).Should().Be(new TextValue("multi\nline"));
    }

    [Fact]
    public void RoundTrips_Formulas_SameSheetAndCrossSheet()
    {
        var wb = new Workbook("Untitled");
        var s1 = wb.AddSheet("First");
        var s2 = wb.AddSheet("Data Sheet");
        Set(s1, 1, 1, new NumberValue(10));
        Set(s1, 1, 2, new NumberValue(20));
        s1.SetCell(new CellAddress(s1.Id, 1, 3), Cell.FromFormula("A1+B1"));
        s1.SetCell(new CellAddress(s1.Id, 2, 1), Cell.FromFormula("SUM(A1:B1)"));
        // A cross-sheet reference to a quoted sheet name.
        s1.SetCell(new CellAddress(s1.Id, 3, 1), Cell.FromFormula("'Data Sheet'!A1*2"));
        Set(s2, 1, 1, new NumberValue(7));

        var got = RoundTrip(wb);
        var g1 = got.GetSheet("First")!;
        g1.GetCell(1, 3)!.FormulaText.Should().Be("A1+B1");
        g1.GetCell(2, 1)!.FormulaText.Should().Be("SUM(A1:B1)");
        g1.GetCell(3, 1)!.FormulaText.Should().Be("'Data Sheet'!A1*2");
    }

    [Fact]
    public void RoundTrips_NumberFormatString_Exactly()
    {
        var wb = NewWorkbook(out var sheet);
        var pct = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        var money = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var date = wb.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" });
        var custom = wb.RegisterStyle(new CellStyle { NumberFormat = "#,##0.000" });
        Set(sheet, 1, 1, new NumberValue(0.25), pct);
        Set(sheet, 1, 2, new NumberValue(1234.5), money);
        Set(sheet, 1, 3, DateTimeValue.FromDateTime(new DateTime(2024, 6, 1)), date);
        Set(sheet, 1, 4, new NumberValue(9876.5), custom);

        var got = RoundTrip(wb);
        var g = got.Sheets.Single();
        got.GetStyle(g.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("0.00%");
        got.GetStyle(g.GetCell(1, 2)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        got.GetStyle(g.GetCell(1, 3)!.StyleId).NumberFormat.Should().Be("yyyy-mm-dd");
        got.GetStyle(g.GetCell(1, 4)!.StyleId).NumberFormat.Should().Be("#,##0.000");
    }

    [Fact]
    public void RoundTrips_FontAttributes()
    {
        var wb = NewWorkbook(out var sheet);
        var style = wb.RegisterStyle(new CellStyle
        {
            FontName = "Times New Roman",
            FontSize = 14,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontColor = new CellColor(200, 30, 60),
        });
        Set(sheet, 1, 1, new TextValue("styled"), style);

        var got = RoundTrip(wb);
        var s = got.GetStyle(got.Sheets.Single().GetCell(1, 1)!.StyleId);
        s.FontName.Should().Be("Times New Roman");
        s.FontSize.Should().Be(14);
        s.Bold.Should().BeTrue();
        s.Italic.Should().BeTrue();
        s.Underline.Should().BeTrue();
        s.Strikethrough.Should().BeTrue();
        s.FontColor.Should().Be(new CellColor(200, 30, 60));
    }

    [Fact]
    public void RoundTrips_FillColor()
    {
        var wb = NewWorkbook(out var sheet);
        var style = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 235, 59) });
        Set(sheet, 1, 1, new TextValue("filled"), style);

        var got = RoundTrip(wb);
        got.GetStyle(got.Sheets.Single().GetCell(1, 1)!.StyleId).FillColor
            .Should().Be(new CellColor(255, 235, 59));
    }

    [Fact]
    public void RoundTrips_Borders_IncludingColoredInvisibleBorders()
    {
        var wb = NewWorkbook(out var sheet);
        var style = wb.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
            BorderRight = new CellBorder(BorderStyle.Medium, new CellColor(15, 158, 213)),
            BorderBottom = new CellBorder(BorderStyle.Double, new CellColor(220, 20, 60)),
            // An Excel quirk: a colored border whose line style is None. CellBorder compares both
            // style AND color, so this must round-trip exactly.
            BorderLeft = new CellBorder(BorderStyle.None, new CellColor(242, 242, 242)),
        });
        Set(sheet, 1, 1, new TextValue("bordered"), style);

        var got = RoundTrip(wb);
        var s = got.GetStyle(got.Sheets.Single().GetCell(1, 1)!.StyleId);
        s.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)));
        s.BorderRight.Should().Be(new CellBorder(BorderStyle.Medium, new CellColor(15, 158, 213)));
        s.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(220, 20, 60)));
        s.BorderLeft.Should().Be(new CellBorder(BorderStyle.None, new CellColor(242, 242, 242)));
    }

    [Fact]
    public void RoundTrips_Alignment()
    {
        var wb = NewWorkbook(out var sheet);
        var style = wb.RegisterStyle(new CellStyle
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            WrapText = true,
            TextRotation = 45,
            IndentLevel = 3,
        });
        Set(sheet, 1, 1, new TextValue("aligned"), style);

        var got = RoundTrip(wb);
        var s = got.GetStyle(got.Sheets.Single().GetCell(1, 1)!.StyleId);
        s.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        s.VerticalAlignment.Should().Be(VerticalAlignment.Top);
        s.WrapText.Should().BeTrue();
        s.TextRotation.Should().Be(45);
        s.IndentLevel.Should().Be(3);
    }

    [Fact]
    public void RoundTrips_MergedRegions()
    {
        var wb = NewWorkbook(out var sheet);
        Set(sheet, 1, 1, new TextValue("anchor"));
        Set(sheet, 4, 4, new TextValue("single"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)));

        var got = RoundTrip(wb).Sheets.Single();
        got.MergedRegions.Should().ContainSingle()
            .Which.Should().Be(new GridRange(
                new CellAddress(got.Id, 1, 1), new CellAddress(got.Id, 2, 3)));
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("anchor"));
        got.GetValue(new CellAddress(got.Id, 4, 4)).Should().Be(new TextValue("single"));
    }

    [Fact]
    public void RoundTrips_MultipleSheetsAndNames()
    {
        var wb = new Workbook("Untitled");
        var a = wb.AddSheet("Summary");
        var b = wb.AddSheet("Q1 Data");
        var c = wb.AddSheet("Notes & More");
        Set(a, 1, 1, new TextValue("a"));
        Set(b, 1, 1, new TextValue("b"));
        Set(c, 1, 1, new TextValue("c"));

        var got = RoundTrip(wb);
        got.Sheets.Select(s => s.Name).Should().ContainInOrder("Summary", "Q1 Data", "Notes & More");
        got.GetSheet("Q1 Data")!.GetValue(new CellAddress(got.GetSheet("Q1 Data")!.Id, 1, 1))
            .Should().Be(new TextValue("b"));
    }

    [Fact]
    public void RoundTrips_ColumnWidthsAndRowHeights()
    {
        var wb = NewWorkbook(out var sheet);
        Set(sheet, 1, 1, new TextValue("x"));
        sheet.ColumnWidths[1] = 12.5;
        sheet.ColumnWidths[3] = 20.0;
        sheet.RowHeights[1] = 30.0;
        sheet.RowHeights[5] = 45.5;

        var got = RoundTrip(wb).Sheets.Single();
        got.ColumnWidths.Should().ContainKey(1).WhoseValue.Should().BeApproximately(12.5, 1e-6);
        got.ColumnWidths.Should().ContainKey(3).WhoseValue.Should().BeApproximately(20.0, 1e-6);
        got.RowHeights.Should().ContainKey(1).WhoseValue.Should().BeApproximately(30.0, 1e-6);
        got.RowHeights.Should().ContainKey(5).WhoseValue.Should().BeApproximately(45.5, 1e-6);
    }

    [Fact]
    public void RoundTrips_NamedRange()
    {
        var wb = NewWorkbook(out var sheet);
        Set(sheet, 1, 1, new NumberValue(1));
        wb.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));

        var got = RoundTrip(wb);
        got.NamedRanges.Should().ContainKey("MyData");
        var range = got.NamedRanges["MyData"];
        (range.Start.Row, range.Start.Col, range.End.Row, range.End.Col).Should().Be((1u, 1u, 3u, 2u));
    }

    [Fact]
    public void RoundTrips_StyleOnlyEmptyCell()
    {
        var wb = NewWorkbook(out var sheet);
        var style = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 176, 80) });
        sheet.SetStyleOnly(2, 2, style);

        var gotWb = RoundTrip(wb);
        var got = gotWb.Sheets.Single();
        var styleId = got.GetStyleOnly(2, 2);
        styleId.Should().NotBeNull();
        gotWb.GetStyle(styleId!.Value).FillColor.Should().Be(new CellColor(0, 176, 80));
    }

    [Fact]
    public void Save_EmitsMimetypeAsFirstStoredEntry()
    {
        var wb = NewWorkbook(out var sheet);
        Set(sheet, 1, 1, new TextValue("x"));

        using var stream = new MemoryStream();
        new OdsFileAdapter().Save(wb, stream);
        stream.Position = 0;

        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        archive.Entries[0].FullName.Should().Be("mimetype");
        using var reader = new StreamReader(archive.Entries[0].Open());
        reader.ReadToEnd().Should().Be(OdsFileAdapter.MimeType);
        archive.GetEntry("content.xml").Should().NotBeNull();
        archive.GetEntry("META-INF/manifest.xml").Should().NotBeNull();
    }

    [Fact]
    public void RoundTrips_PercentageAndCurrencyValueTypes()
    {
        var wb = NewWorkbook(out var sheet);
        var pct = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cur = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0" });
        Set(sheet, 1, 1, new NumberValue(0.5), pct);
        Set(sheet, 1, 2, new NumberValue(1000), cur);

        var got = RoundTrip(wb).Sheets.Single();
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new NumberValue(0.5));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new NumberValue(1000));
    }
}
