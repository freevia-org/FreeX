using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 44 finding R44-meta-3 (r43-INCOMPLETE): the r43 per-axis XAxisTitleFontSize/
/// YAxisTitleFontSize/*TextColor override fields were WRITTEN by the writer (with a
/// <c>?? AxisTitleFontSize</c> fallback) but never POPULATED by the reader --
/// XlsxChartAxisReader.ApplyAxisTitleFormatting wrote only the SHARED chart.AxisTitleFontSize/
/// *TextColor fields regardless of the isXAxis param, so reading the Y axis after the X axis
/// clobbered the X axis's formatting into the single shared field, and the new per-axis fields
/// stayed null after any XLSX load. Fixed by routing the read font size/color into the per-axis
/// field (XAxisTitleFontSize/XAxisTitleTextColor when isXAxis, else the Y* fields).
/// </summary>
public sealed class R44_chart_axis_title_read_Tests
{
    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R44ChartAxisTitleRead");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static ChartModel RoundTrip(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    // --- R44-meta-3 ---------------------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_DistinctXAndYAxisTitleFontSizes_AreReadIntoSeparateFields()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            XAxisTitleFontSize = 14,
            YAxisTitleFontSize = 18,
        };

        var loaded = RoundTrip(chart);

        loaded.XAxisTitleFontSize.Should().Be(14,
            "pre-fix, the reader only ever populated the shared AxisTitleFontSize field, leaving " +
            "XAxisTitleFontSize null regardless of what was in the XLSX");
        loaded.YAxisTitleFontSize.Should().Be(18,
            "pre-fix, the Y axis read clobbered the shared field last, but never populated YAxisTitleFontSize itself");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_DistinctXAndYAxisTitleColors_AreReadIntoSeparateFields()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            XAxisTitleTextColor = new CellColor(255, 0, 0),
            YAxisTitleTextColor = new CellColor(0, 0, 255),
        };

        var loaded = RoundTrip(chart);

        loaded.XAxisTitleTextColor.Should().Be(new CellColor(255, 0, 0));
        loaded.YAxisTitleTextColor.Should().Be(new CellColor(0, 0, 255));
    }

    // Sibling/no-regression case: a chart that only sets the shared AxisTitleFontSize (no per-axis
    // overrides) still round-trips both axis titles at that same shared size, and the per-axis
    // fields end up populated with that same value on read (since both axes carry the same size
    // in the XML) -- not left mismatched or null.
    [Fact]
    public void XlsxAdapter_RoundTrip_SharedAxisTitleFontSize_PopulatesBothPerAxisFieldsConsistently()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            AxisTitleFontSize = 20,
        };

        var loaded = RoundTrip(chart);

        loaded.XAxisTitleFontSize.Should().Be(20);
        loaded.YAxisTitleFontSize.Should().Be(20);
        loaded.AxisTitleFontSize.Should().Be(20);
    }
}
