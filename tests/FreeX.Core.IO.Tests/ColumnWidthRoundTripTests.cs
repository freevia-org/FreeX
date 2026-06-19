using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class ColumnWidthRoundTripTests
{
    // ClosedXML's Column.Width setter inflates the stored width (2.0 -> 2.71) and stamps style="0" on
    // every <col>; the loader then dropped any styled width <= 9.2 and floored the rest. Together that
    // silently lost narrow columns and rounded wide ones on round-trip. Widths must now survive exactly.
    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(8.0)]
    [InlineData(8.43)]
    [InlineData(9.0)]
    [InlineData(12.63)]
    [InlineData(20.0)]
    public void ColumnWidth_RoundTripsExactly(double width)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.ColumnWidths[3] = width;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);

        reloaded.Sheets[0].ColumnWidths.TryGetValue(3, out var got).Should().BeTrue($"width {width} must survive round-trip");
        got.Should().BeApproximately(width, 1e-6);
    }

    // A narrow column whose width sits in the near-default "carrier" band (<= 9.2) and that ALSO carries
    // real cell formatting (so ClosedXML stamps a non-default style index on its <col>) must still
    // round-trip its width. Previously the loader discarded any styled column at <= 9.2 as a styling-only
    // carrier, silently dropping these widths on a full rebuild (e.g. ExcelExamples1 Calendar gutters at
    // 1.71 / 5.71 and Inputs columns at 8.14 / 8.71). The save path now strips the ClosedXML-stamped
    // style from a genuinely-modelled width in that band so the loader keeps it.
    [Theory]
    [InlineData(1.71)]
    [InlineData(5.71)]
    [InlineData(8.14)]
    [InlineData(8.43)]
    [InlineData(8.71)]
    public void StyledColumnWithNarrowOrNearDefaultWidth_RoundTripsExactly(double width)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.ColumnWidths[3] = width;
        // Give the column real cell formatting so ClosedXML stamps a non-default style index on its <col>.
        var styled = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(200, 220, 240) });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new Cell { Value = new NumberValue(1), StyleId = styled });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new Cell { Value = new BlankValue(), StyleId = styled });

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);

        reloaded.Sheets[0].ColumnWidths.TryGetValue(3, out var got)
            .Should().BeTrue($"styled width {width} in the carrier band must survive round-trip");
        got.Should().BeApproximately(width, 1e-6);
    }

    [Fact]
    public void NarrowAndWideColumnWidths_AllRoundTripWithoutLossOrExtras()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.ColumnWidths[1] = 2.0;
        sheet.ColumnWidths[2] = 7.5;
        sheet.ColumnWidths[5] = 18.25;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(wb, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);

        var widths = reloaded.Sheets[0].ColumnWidths;
        widths.Keys.Should().BeEquivalentTo(new uint[] { 1, 2, 5 }, "no widths are lost and none are spuriously added");
        widths[1].Should().BeApproximately(2.0, 1e-6);
        widths[2].Should().BeApproximately(7.5, 1e-6);
        widths[5].Should().BeApproximately(18.25, 1e-6);
    }
}
