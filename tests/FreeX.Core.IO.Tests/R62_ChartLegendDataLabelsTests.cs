using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-62 "io-chart-b" bucket fixes:
///   - R62-io-chart-legend-datalabels-6-1: a series-level "delete all data labels"
///     (&lt;c:dLbls&gt;&lt;c:delete val="1"/&gt;&lt;/c:dLbls&gt;) is now captured into
///     <see cref="ChartModel.SeriesDataLabelFormats"/> (via the new
///     <see cref="ChartSeriesDataLabelFormat.IsDeleted"/> field) instead of being silently
///     discarded by <c>HasSeriesDataLabelMetadata</c>. NOTE: this fix covers the read/model side
///     only (XlsxChartDataLabelReader.cs + ChartModel.Support.cs, the files owned by this bucket).
///     Persisting the flag back out to XML on save additionally requires a companion change in
///     XlsxChartXmlWriter.SeriesFormatting.cs (HasSeriesDataLabelFormatting doesn't check
///     IsDeleted, and ToSeriesDataLabelDefaultsXml never emits &lt;c:delete&gt;), which is a
///     different file outside this bucket's edit set.
///   - R62-io-chart-legend-datalabels-6-2: legend position "tr" (top-right corner) now round-trips
///     via the new <see cref="ChartLegendPosition.TopRight"/> member instead of collapsing into a
///     full-height "r" (Right) legend.
/// R62-io-chart-legend-datalabels-6-3 (a freshly-authored chart's explicitly-chosen Right legend
/// silently downgraded to Bottom by the classic-stacked-chart heuristic) is deferred: there is no
/// in-scope signal to distinguish "user explicitly picked Right" from "left at ChartModel's C#
/// default of Right" for a chart that never round-tripped through the XLSX reader (both cases have
/// LegendPosition == Right and LegendPositionExplicit == null) — the only place that could supply
/// that provenance signal is SetChartLayoutCommand.ApplyOptions.cs, outside this bucket's owned
/// files, and XlsxClassicChartDefaultTests.XlsxAdapter_Save_WritesExcelNativeStackedBarColumnDefaults
/// already pins the opposite requirement (a freshly-created stacked chart with LegendPosition left
/// at its default must still default to "b"). This mirrors the identical deferral already recorded
/// for R42-io-chart-plotarea-legend-3-3 in R42_ChartPlotAreaFormatLegendTitleTests.cs.
/// </summary>
public sealed class R62_ChartLegendDataLabelsTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ApplyPointDataLabels_SeriesLevelDeleteAll_IsCapturedNotDiscarded()
    {
        // A series <c:dLbls> whose ONLY child is <c:delete val="1"/> -- Excel's "hide all data
        // labels for this one series" override of a chart-wide showVal=1 default.
        var seriesXml = new XElement(ChartNs + "ser",
            new XElement(ChartNs + "idx", new XAttribute("val", 0)),
            new XElement(ChartNs + "dLbls",
                new XElement(ChartNs + "delete", new XAttribute("val", "1"))));

        var chart = new ChartModel { Type = ChartType.Column };

        XlsxChartDataLabelReader.ApplyPointDataLabels(seriesXml, seriesIndex: 0, chart);

        var format = chart.SeriesDataLabelFormats.Should().ContainSingle(
            "a delete-only series <c:dLbls> must not be discarded as \"no metadata\"").Subject;
        format.SeriesIndex.Should().Be(0);
        format.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void ApplyPointDataLabels_SeriesLevelShowValOnly_StillCapturedWithNoDeleteFlag()
    {
        // Sibling no-regression case: a series <c:dLbls> that only overrides showVal (the
        // pre-existing, already-working code path) must keep working unaffected by the new
        // IsDeleted field, and must not spuriously report IsDeleted.
        var seriesXml = new XElement(ChartNs + "ser",
            new XElement(ChartNs + "idx", new XAttribute("val", 2)),
            new XElement(ChartNs + "dLbls",
                new XElement(ChartNs + "showVal", new XAttribute("val", "1"))));

        var chart = new ChartModel { Type = ChartType.Column };

        XlsxChartDataLabelReader.ApplyPointDataLabels(seriesXml, seriesIndex: 2, chart);

        var format = chart.SeriesDataLabelFormats.Should().ContainSingle().Subject;
        format.SeriesIndex.Should().Be(2);
        format.ShowValue.Should().BeTrue();
        format.IsDeleted.Should().BeNull("no <c:delete> element was present, so the flag must stay unset");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_PreservesTopRightLegendPosition()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ShowLegend = true;
            chart.LegendPosition = ChartLegendPosition.TopRight;
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        chartXml.Descendants(ChartNs + "legend").Single()
            .Element(ChartNs + "legendPos")!.Attribute("val")!.Value.Should().Be(
                "tr", "an explicit top-right corner legend must not collapse into a full-height right legend");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.LegendPosition.Should().Be(ChartLegendPosition.TopRight);
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_PreservesPlainRightLegendPosition()
    {
        // Sibling no-regression case: the ordinary full-height Right legend (the pre-existing,
        // already-working position) must still round-trip as "r", unaffected by adding "tr".
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ShowLegend = true;
            chart.LegendPosition = ChartLegendPosition.Right;
            chart.LegendPositionExplicit = true;
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        chartXml.Descendants(ChartNs + "legend").Single()
            .Element(ChartNs + "legendPos")!.Attribute("val")!.Value.Should().Be("r");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.LegendPosition.Should().Be(ChartLegendPosition.Right);
    }

    private static Workbook CreateColumnChartWorkbook(System.Action<ChartModel> configure)
    {
        var workbook = new Workbook("ChartLegendDataLabels");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
        };
        configure(chart);
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static MemoryStream SaveToStream(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument ReadChartXml(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        return XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/charts/chart1.xml",
            "http://schemas.openxmlformats.org/drawingml/2006/chart");
    }
}
