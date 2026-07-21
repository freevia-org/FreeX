using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-63 fix completing the WRITE half of R62-io-chart-legend-datalabels-6-1. The r62 change
/// (<see cref="XlsxChartDataLabelReader.ApplyPointDataLabels"/> / <c>HasSeriesDataLabelMetadata</c>)
/// taught the reader to capture a series-level "delete all data labels"
/// (&lt;c:dLbls&gt;&lt;c:delete val="1"/&gt;&lt;/c:dLbls&gt;) into
/// <see cref="ChartSeriesDataLabelFormat.IsDeleted"/> instead of discarding it. But the writer
/// (<c>XlsxChartXmlWriter.SeriesFormatting.cs</c>) never emitted <c>&lt;c:delete&gt;</c> back out,
/// and its <c>HasSeriesDataLabelFormatting</c> guard didn't even consider <c>IsDeleted</c> --
/// a delete-only format was nulled out before it reached the XML writer, so a full round trip
/// (load -&gt; save -&gt; reload) silently resurrected the deleted labels.
/// </summary>
public sealed class R63_ChartSeriesDataLabelDeleteRoundTripTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void XlsxAdapter_SaveLoad_SeriesLevelDeleteAll_SurvivesRoundTrip()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.SeriesDataLabelFormats.Add(new ChartSeriesDataLabelFormat(
                SeriesIndex: 0,
                IsDeleted: true));
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var seriesDLbls = chartXml.Descendants(ChartNs + "ser").Single().Element(ChartNs + "dLbls");
        seriesDLbls.Should().NotBeNull("the series-level delete-all override must be written back out");
        seriesDLbls!.Element(ChartNs + "delete")?.Attribute("val")?.Value.Should().Be(
            "1", "the series' \"hide all data labels\" flag must round-trip as <c:delete val=\"1\"/>");
        seriesDLbls.Element(ChartNs + "showVal").Should().BeNull(
            "CT_DLbls' trailing content is delete XOR the Group_DLbls defaults -- a delete-only "
            + "series dLbls must not also carry show* children");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var loadedFormat = loadedChart.SeriesDataLabelFormats.Should().ContainSingle(
            "the delete-only series format must survive load -> save -> reload, not be dropped on save").Subject;
        loadedFormat.SeriesIndex.Should().Be(0);
        loadedFormat.IsDeleted.Should().BeTrue(
            "reloading a saved series delete-all override must still report IsDeleted == true");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_SeriesLevelShowValOnly_StillRoundTripsUnaffected()
    {
        // Sibling no-regression case: an ordinary (non-delete) series-level data-label format --
        // the pre-existing, already-working path -- must keep round-tripping unaffected by the
        // new IsDeleted handling, and must not spuriously pick up a delete flag.
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.SeriesDataLabelFormats.Add(new ChartSeriesDataLabelFormat(
                SeriesIndex: 0,
                ShowValue: true));
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var seriesDLbls = chartXml.Descendants(ChartNs + "ser").Single().Element(ChartNs + "dLbls");
        seriesDLbls.Should().NotBeNull();
        seriesDLbls!.Element(ChartNs + "delete").Should().BeNull(
            "a non-deleted series format must not emit <c:delete>");
        seriesDLbls.Element(ChartNs + "showVal")?.Attribute("val")?.Value.Should().Be("1");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var loadedFormat = loadedChart.SeriesDataLabelFormats.Should().ContainSingle().Subject;
        loadedFormat.SeriesIndex.Should().Be(0);
        loadedFormat.ShowValue.Should().BeTrue();
        loadedFormat.IsDeleted.Should().BeNull("no <c:delete> element was written, so the flag must stay unset");
    }

    private static Workbook CreateColumnChartWorkbook(System.Action<ChartModel> configure)
    {
        var workbook = new Workbook("ChartSeriesDataLabelDelete");
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
