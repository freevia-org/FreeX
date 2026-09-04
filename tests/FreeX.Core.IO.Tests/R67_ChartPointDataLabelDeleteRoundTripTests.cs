using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-67 fix completing the per-point half of R62/R63-io-chart-legend-datalabels-6-1 (the r63
/// fix only handled the SERIES-level &lt;c:dLbls&gt;&lt;c:delete val="1"/&gt;&lt;/c:dLbls&gt; case).
/// <see cref="ChartPointDataLabelFormat.IsDeleted"/> and its reader
/// (<c>XlsxChartDataLabelReader.ReadPointDataLabelFormat</c>) already existed, but the writer
/// (<c>XlsxChartXmlWriter.ToPointDataLabelXml</c> in <c>XlsxChartXmlWriter.SeriesFormatting.cs</c>)
/// unconditionally emitted the show-flags/formatting Group_DLbl content alongside &lt;c:delete&gt;
/// whenever both were present on the same point -- CT_DLbl's trailing content is a choice between
/// delete and Group_DLbl, so this produced schema-invalid XML that fails validation / risks an
/// Excel repair prompt whenever a deleted point also carried any other per-point override.
/// </summary>
public sealed class R67_ChartPointDataLabelDeleteRoundTripTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void XlsxAdapter_SaveLoad_PerPointDelete_SurvivesRoundTripAndLeavesOtherPointsShown()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ShowDataLabels = true;
            chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
                SeriesIndex: 0,
                PointIndex: 1,
                IsDeleted: true));
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var label = chartXml.Descendants(ChartNs + "ser").Single()
            .Element(ChartNs + "dLbls")!
            .Elements(ChartNs + "dLbl").Should().ContainSingle(
                "only the one deleted point should get its own <c:dLbl> override").Subject;
        label.Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("1");
        label.Element(ChartNs + "delete")?.Attribute("val")!.Value.Should().Be(
            "1", "the point's \"hide this label\" flag must round-trip as <c:delete val=\"1\"/>");
        label.Element(ChartNs + "showVal").Should().BeNull(
            "CT_DLbl's trailing content is delete XOR the Group_DLbl defaults -- a delete-only "
            + "<c:dLbl> must not also carry show* children");
        label.Elements().Should().HaveCount(2,
            "a delete-only <c:dLbl> must contain only <c:idx> and <c:delete>, nothing else");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var loadedFormat = loadedChart.PointDataLabelFormats.Should().ContainSingle(
            "the delete-only point format must survive load -> save -> reload, not be dropped on save").Subject;
        loadedFormat.SeriesIndex.Should().Be(0);
        loadedFormat.PointIndex.Should().Be(1);
        loadedFormat.IsDeleted.Should().BeTrue(
            "reloading a saved point delete override must still report IsDeleted == true");

        // The other points on this series were never overridden, so they must keep showing the
        // chart-wide data labels (no dLbl entry, no spurious deletion) -- only point 1 is affected.
        loadedChart.PointDataLabelFormats.Should().NotContain(format => format.PointIndex != 1);
        loadedChart.ShowDataLabels.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_PerPointDeleteAlongsideOtherOverrides_EmitsDeleteOnlyChoice()
    {
        // The bug scenario named in the finding: a point that carries BOTH IsDeleted=true AND other
        // per-point formatting overrides (e.g. a leftover Position/ShowValue from before the point
        // was deleted). The writer must still emit ONLY <c:delete>, never both branches of the
        // CT_DLbl choice at once.
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ShowDataLabels = true;
            chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
                SeriesIndex: 0,
                PointIndex: 0,
                IsDeleted: true,
                ShowValue: true,
                Position: ChartDataLabelPosition.Center));
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var label = chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle().Subject;
        label.Element(ChartNs + "delete")?.Attribute("val")!.Value.Should().Be("1");
        label.Element(ChartNs + "showVal").Should().BeNull();
        label.Element(ChartNs + "dLblPos").Should().BeNull();
        label.Elements().Should().HaveCount(2);
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_PerPointShowValOnly_StillRoundTripsUnaffected()
    {
        // Sibling no-regression case: an ordinary (non-delete) per-point data-label override -- the
        // pre-existing, already-working path -- must keep round-tripping unaffected by the new
        // IsDeleted handling, and must not spuriously pick up a delete flag.
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ShowDataLabels = true;
            chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
                SeriesIndex: 0,
                PointIndex: 0,
                ShowValue: true));
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var label = chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle().Subject;
        label.Element(ChartNs + "delete").Should().BeNull(
            "a non-deleted point format must not emit <c:delete>");
        label.Element(ChartNs + "showVal")?.Attribute("val")!.Value.Should().Be("1");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var loadedFormat = loadedChart.PointDataLabelFormats.Should().ContainSingle().Subject;
        loadedFormat.SeriesIndex.Should().Be(0);
        loadedFormat.PointIndex.Should().Be(0);
        loadedFormat.ShowValue.Should().BeTrue();
        loadedFormat.IsDeleted.Should().BeNull("no <c:delete> element was written, so the flag must stay unset");
    }

    private static Workbook CreateColumnChartWorkbook(System.Action<ChartModel> configure)
    {
        var workbook = new Workbook("ChartPointDataLabelDelete");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
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
