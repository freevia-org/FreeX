using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 16 chart-XML-writer findings:
///  - R16-meta-1: the round-15 fix routed the READER's bar-family category/value reverse-order
///    flags asymmetrically (category -&gt; YAxisReverseOrder, value -&gt; XAxisReverseOrder for a
///    horizontal Bar chart) but the WRITER kept emitting both axes' &lt;c:orientation&gt; from the
///    non-bar-family fields, so a reversed category axis silently jumped onto the value axis (and
///    vice versa) on every save. Fixed by mirroring the reader's routing in
///    XlsxChartXmlWriter.Axes.cs (ToCategoryAxisXml / the main-path ToValueAxisXml call).
///  - R16-chart-datasource-editing-3: XlsxChartXmlWriter.Series.cs's BuildChartSeries ignored
///    <see cref="ChartModel.SeriesColumnMappings"/> entirely and always emitted one &lt;c:ser&gt;
///    per worksheet column in the value-strip span, re-creating a phantom series for a column the
///    user had deselected from the chart (and whose mapping entry was therefore absent). Fixed by
///    making BuildChartSeries iterate the authoritative mapping (when present) instead of the raw
///    column span.
/// </summary>
public sealed class R16_chart_io_Tests
{
    [Fact]
    public void XlsxAdapter_RoundTrip_HorizontalBarChart_CategoryAxisReversed_IsIdempotent()
    {
        var workbook1 = new Workbook("R16ChartIoBar1");
        var sheet1 = workbook1.AddSheet("Data");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("A"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new NumberValue(10));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("B"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            // For a Bar chart the category axis is physically on the Left (Y) and the value axis is
            // physically on the Bottom (X) — see XlsxChartAxisReader.ApplyAxisMetadata. So "category
            // reversed, value normal" is represented in the model as YAxisReverseOrder=true (category),
            // XAxisReverseOrder=false (value).
            YAxisReverseOrder = true,
            XAxisReverseOrder = false,
            DataRange = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 2, 2)),
        };
        sheet1.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms1 = new MemoryStream();
        adapter.Save(workbook1, ms1);
        ms1.Position = 0;
        var loaded1 = adapter.Load(ms1);
        var afterFirstRoundTrip = loaded1.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        afterFirstRoundTrip.YAxisReverseOrder.Should().BeTrue(
            "the category axis (left, for a Bar chart) was reversed and must stay reversed after one save+load");
        afterFirstRoundTrip.XAxisReverseOrder.Should().BeFalse(
            "the value axis (bottom, for a Bar chart) was NOT reversed and must not pick up the category axis's flag");

        // Re-save/reload what was just loaded — a true idempotent round-trip must not flip the
        // reversal onto the other axis on a SECOND pass either.
        using var ms2 = new MemoryStream();
        adapter.Save(loaded1, ms2);
        ms2.Position = 0;
        var loaded2 = adapter.Load(ms2);
        var afterSecondRoundTrip = loaded2.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        afterSecondRoundTrip.YAxisReverseOrder.Should().BeTrue(
            "the category axis reversal must survive a second save+load cycle unchanged");
        afterSecondRoundTrip.XAxisReverseOrder.Should().BeFalse(
            "the value axis must remain non-reversed after a second save+load cycle");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_SeriesColumnMappingsSkippingAColumn_DoesNotEmitPhantomSeries()
    {
        // Three columns: A = category, B = the one series the user kept selected, C = a helper
        // column that falls inside DataRange but was deselected from the chart (no mapping entry).
        var workbook = new Workbook("R16ChartIoMapping");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Cat2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(200));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(SeriesXmlIndex: 0, ValueColumn: 2)],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 3)),
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loadedWorkbook = adapter.Load(ms);
        var loaded = loadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Pre-fix, the writer ignored SeriesColumnMappings and emitted a <c:ser> for every column in
        // the value-strip span (B AND C), so column C came back as a second phantom series/mapping.
        loaded.SeriesColumnMappings.Should().ContainSingle(
            "column C was deselected from the chart (no mapping entry) and must not be re-emitted as a series")
            .Which.ValueColumn.Should().Be(2u, "column B is the only column the chart actually maps to a series");

        loaded.DataRange.End.Col.Should().Be(2u,
            "with only column B written as a series, nothing in the saved file references column C anymore");
    }
}
