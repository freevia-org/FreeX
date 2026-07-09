using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R19-chartex-deep-1 / R19-chartex-deep-2: saving a loaded chartEx chart WITHOUT touching it used
/// to wholesale-replace every &lt;cx:series&gt; (dropping dataPt/dataLabels/spPr/marker/extLst) and
/// the entire &lt;cx:chartData&gt; block (dropping cached &lt;cx:pt&gt; point values and any extra
/// dimension levels) with a bare-bones reconstruction. These tests inject exactly that kind of
/// unmodeled source content into a saved chartEx part, load it, re-save it unchanged, and assert the
/// content survives.
/// </summary>
public sealed class R19_chartex_preserve_Tests
{
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ChartPartPath = "xl/charts/chart1.xml";

    private static MemoryStream SaveTreemapWorkbook()
    {
        var workbook = new Workbook("ChartExPreserveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Treemap,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Treemap"
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static void ReplaceChartXml(ZipArchive archive, XDocument xml)
    {
        archive.GetEntry(ChartPartPath)?.Delete();
        var entry = archive.CreateEntry(ChartPartPath);
        using var stream = entry.Open();
        xml.Save(stream);
    }

    private static XDocument LoadChartXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, ChartPartPath, ChartPartPath);

    [Fact]
    public void SaveLoadSave_UntouchedChartExPreservesSourceSeriesDataPtWithShapeProperties()
    {
        var source = SaveTreemapWorkbook();

        // Inject a source cx:series/cx:dataPt with its own spPr (a manually-recolored point) --
        // content FreeX never models and BuildChartExSeries never emits.
        var dataPt = new XElement(ChartExNs + "dataPt",
            new XAttribute("idx", "1"),
            new XElement(ChartExNs + "spPr",
                new XElement(DrawingNs + "solidFill",
                    new XElement(DrawingNs + "srgbClr", new XAttribute("val", "FF0000")))));

        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadChartXml(archive);
            var series = chartXml.Root!
                .Element(ChartExNs + "chart")!
                .Element(ChartExNs + "plotArea")!
                .Element(ChartExNs + "plotAreaRegion")!
                .Element(ChartExNs + "series")!;
            series.AddFirst(dataPt);
            ReplaceChartXml(archive, chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        workbook.GetSheetAt(0).Charts.Should().ContainSingle();

        // Save the workbook again WITHOUT touching the chart at all -- this is the exact scenario
        // (open, save, never edited) that R19-chartex-deep-1 flagged as silently destructive.
        var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, resaved);
        resaved.Position = 0;

        using var resavedArchive = new ZipArchive(resaved, ZipArchiveMode.Read, leaveOpen: false);
        var resavedChartXml = LoadChartXml(resavedArchive);
        var resavedSeries = resavedChartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Element(ChartExNs + "series")!;

        var resavedDataPt = resavedSeries.Element(ChartExNs + "dataPt");
        resavedDataPt.Should().NotBeNull("the source dataPt must survive an untouched save/reload");
        resavedDataPt!.Attribute("idx")!.Value.Should().Be("1");
        resavedDataPt!.Element(ChartExNs + "spPr")
            .Should().NotBeNull("the per-point spPr override must not be dropped");
        resavedDataPt!.Descendants(DrawingNs + "srgbClr").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("FF0000");

        // dataId (the modeled part) must still be present and correct alongside the preserved dataPt.
        resavedSeries.Element(ChartExNs + "dataId").Should().NotBeNull();
    }

    [Fact]
    public void SaveLoadSave_UntouchedChartExPreservesSourceChartDataCachedPointValue()
    {
        var source = SaveTreemapWorkbook();

        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadChartXml(archive);
            var numDim = chartXml.Root!
                .Element(ChartExNs + "chartData")!
                .Element(ChartExNs + "data")!
                .Element(ChartExNs + "numDim")!;
            // Cached point value Excel writes alongside the formula reference -- content
            // BuildChartExData never emits and R19-chartex-deep-2 flagged as silently destroyed.
            numDim.Add(new XElement(ChartExNs + "pt",
                new XAttribute("idx", "0"),
                new XElement(ChartExNs + "v", "12345")));
            ReplaceChartXml(archive, chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        workbook.GetSheetAt(0).Charts.Should().ContainSingle();

        var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, resaved);
        resaved.Position = 0;

        using var resavedArchive = new ZipArchive(resaved, ZipArchiveMode.Read, leaveOpen: false);
        var resavedChartXml = LoadChartXml(resavedArchive);
        var resavedNumDim = resavedChartXml.Root!
            .Element(ChartExNs + "chartData")!
            .Element(ChartExNs + "data")!
            .Element(ChartExNs + "numDim")!;

        var resavedPt = resavedNumDim.Element(ChartExNs + "pt");
        resavedPt.Should().NotBeNull("the cached cx:pt value must survive an untouched save/reload");
        resavedPt!.Attribute("idx")!.Value.Should().Be("0");
        resavedPt!.Element(ChartExNs + "v")!.Value.Should().Be("12345");

        // The modeled formula reference must still be present alongside the preserved cached value.
        resavedNumDim.Element(ChartExNs + "f").Should().NotBeNull();
    }
}
