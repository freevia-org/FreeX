using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxChartRangeDataLabelsTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Chart2012Ns = "http://schemas.microsoft.com/office/drawing/2012/chart";
    private const string DataLabelsRangeExtUri = "{02D57815-91ED-43cb-92C2-25804820EDAC}";

    // The chart XML a real "Value From Cells" column chart carries: a c15:datalabelsRange under the
    // series extLst, with a c15:f source formula and a cached point list.
    private const string BarChartWithRangeDataLabelsXml = """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:c15="http://schemas.microsoft.com/office/drawing/2012/chart">
          <c:chart>
            <c:plotArea>
              <c:barChart>
                <c:barDir val="col"/>
                <c:ser>
                  <c:idx val="0"/>
                  <c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                  <c:extLst>
                    <c:ext uri="{02D57815-91ED-43cb-92C2-25804820EDAC}"
                           xmlns:c15="http://schemas.microsoft.com/office/drawing/2012/chart">
                      <c15:datalabelsRange>
                        <c15:f>Sheet1!$C$2:$C$4</c15:f>
                        <c15:dlblRangeCache>
                          <c15:ptCount val="3"/>
                          <c15:pt idx="0"><c15:v>Alpha</c15:v></c15:pt>
                          <c15:pt idx="1"><c15:v>Bravo</c15:v></c15:pt>
                          <c15:pt idx="2"><c15:v>Charlie</c15:v></c15:pt>
                        </c15:dlblRangeCache>
                      </c15:datalabelsRange>
                    </c:ext>
                  </c:extLst>
                </c:ser>
              </c:barChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;

    [Fact]
    public void TryReadSupportedChart_BarSeriesWithRangeDataLabels_CapturesFormulaPointCountAndPoints()
    {
        var sheetId = new SheetId(System.Guid.NewGuid());
        var chartXml = XDocument.Parse(BarChartWithRangeDataLabelsXml);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();

        var seriesLabels = chart.SeriesRangeDataLabels.Should().ContainSingle().Subject;
        seriesLabels.SeriesIndex.Should().Be(0);
        seriesLabels.Formula.Should().Be("Sheet1!$C$2:$C$4");
        seriesLabels.PointCount.Should().Be(3);
        seriesLabels.Points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        seriesLabels.Points.Select(p => p.Text).Should().Equal("Alpha", "Bravo", "Charlie");

        // The flat list the renderer consumes is still populated.
        chart.RangeDataLabels.Should().HaveCount(3);
        chart.RangeDataLabels.Should().Contain(new ChartRangeDataLabel(0, 1, "Bravo"));
    }

    [Fact]
    public void XlsxAdapter_WriteBack_PreservesRangeDataLabelsExt()
    {
        // Read the model from XML carrying the c15:datalabelsRange, write a fresh workbook, then
        // assert the c15 ext (with the uri GUID, c15:f, and 3 c15:pt) survives the write-back.
        var sheetId = new SheetId(System.Guid.NewGuid());
        var chartXml = XDocument.Parse(BarChartWithRangeDataLabelsXml);
        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var readChart)
            .Should().BeTrue();

        var workbook = new Workbook("RangeDataLabelsWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Label"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue($"L{row}"));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesRangeDataLabels = readChart.SeriesRangeDataLabels.ToList(),
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var ext = chartDoc.Descendants(ChartNs + "ext")
            .Single(e => string.Equals(e.Attribute("uri")?.Value, DataLabelsRangeExtUri, System.StringComparison.Ordinal));
        var dataLabelsRange = ext.Element(Chart2012Ns + "datalabelsRange");
        dataLabelsRange.Should().NotBeNull();
        dataLabelsRange!.Element(Chart2012Ns + "f")!.Value.Should().Be("Sheet1!$C$2:$C$4");

        var cache = dataLabelsRange.Element(Chart2012Ns + "dlblRangeCache")!;
        cache.Element(Chart2012Ns + "ptCount")!.Attribute("val")!.Value.Should().Be("3");
        var points = cache.Elements(Chart2012Ns + "pt").ToList();
        points.Should().HaveCount(3);
        points.Select(p => p.Element(Chart2012Ns + "v")!.Value).Should().Equal("Alpha", "Bravo", "Charlie");

        // extLst must be the last child of c:ser.
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        series.Elements().Last().Name.Should().Be(ChartNs + "extLst");

        // Reloading the written package re-captures the per-series definition.
        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var reloadedLabels = reloaded.SeriesRangeDataLabels.Should().ContainSingle().Subject;
        reloadedLabels.Formula.Should().Be("Sheet1!$C$2:$C$4");
        reloadedLabels.PointCount.Should().Be(3);
        reloadedLabels.Points.Select(p => p.Text).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PreservesSeriesRangeDataLabelsAndRebuildsFlatList()
    {
        var workbook = new Workbook("RangeDataLabelsFxl");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(
                    0,
                    "Sheet1!$C$2:$C$4",
                    3,
                    [
                        new ChartRangeDataLabelPoint(0, "Alpha"),
                        new ChartRangeDataLabelPoint(1, "Bravo"),
                        new ChartRangeDataLabelPoint(2, "Charlie"),
                    ]),
            ],
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedChart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        var labels = loadedChart.SeriesRangeDataLabels.Should().ContainSingle().Subject;
        labels.SeriesIndex.Should().Be(0);
        labels.Formula.Should().Be("Sheet1!$C$2:$C$4");
        labels.PointCount.Should().Be(3);
        labels.Points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        labels.Points.Select(p => p.Text).Should().Equal("Alpha", "Bravo", "Charlie");

        // The flat RangeDataLabels list is not persisted in the DTO; it is rebuilt on load.
        loadedChart.RangeDataLabels.Should().HaveCount(3);
        loadedChart.RangeDataLabels.Should().Contain(new ChartRangeDataLabel(0, 2, "Charlie"));
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
