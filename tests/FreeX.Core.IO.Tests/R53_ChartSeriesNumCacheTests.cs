using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R53-io-chart-series-order-3-2: every series' c:val/c:cat/c:xVal/c:yVal/c:bubbleSize numRef/strRef
/// was written with only a &lt;c:f&gt; formula, never a paired &lt;c:numCache&gt;/&lt;c:strCache&gt;.
/// Real Excel always emits the cache so the chart still shows last-known data when the referenced
/// range/sheet is unavailable (external link broken, manual calc not yet run, or a non-recalculating
/// OOXML consumer). The fix has the writer read the sheet's current values for a positionally-known
/// strip/category and emit them as a cache alongside the formula.
/// </summary>
public sealed class R53_ChartSeriesNumCacheTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void ColumnChart_ValueSeries_EmitsNumCacheWithActualValues()
    {
        var workbook = new Workbook("ChartNumCache");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Row1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Row2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var series = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").Single();
        var numCache = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull("real Excel always pairs a series value formula with its cached values");
        numCache!.Element(ChartNs + "ptCount")!.Attribute("val")!.Value.Should().Be("2");
        var points = numCache.Elements(ChartNs + "pt").ToList();
        points.Should().HaveCount(2);
        points[0].Element(ChartNs + "v")!.Value.Should().Be("10");
        points[1].Element(ChartNs + "v")!.Value.Should().Be("20");

        var catCache = series.Element(ChartNs + "cat")!.Element(ChartNs + "strRef")!.Element(ChartNs + "strCache");
        catCache.Should().NotBeNull("the category range's cached text should round-trip too");
        catCache!.Elements(ChartNs + "pt").Select(pt => pt.Element(ChartNs + "v")!.Value)
            .Should().BeEquivalentTo(["Row1", "Row2"]);
    }

    [Fact]
    public void ColumnChart_VerbatimMultiAreaFormula_StillOmitsCache_NoRegression()
    {
        // Sibling/no-regression: a series whose value formula is a verbatim multi-area union (no
        // single positional strip is known) must keep working exactly as before this fix -- no cache
        // is fabricated for it, and the verbatim formula itself must still round-trip untouched.
        var workbook = new Workbook("ChartNumCacheVerbatim");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(15));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(0, "Data!$A$1,Data!$A$3", null, null)
            ]
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var series = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").Single();
        var valueRef = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        valueRef.Element(ChartNs + "f")!.Value.Should().Be("Data!$A$1,Data!$A$3");
        valueRef.Element(ChartNs + "numCache").Should().BeNull(
            "a verbatim multi-area formula has no single strip to source cache values from");
    }

    [Fact]
    public void LineChart_DenseVerbatimFormulas_KeepFirstDuplicateSeriesFormula()
    {
        var workbook = new Workbook("DenseClassicVerbatimSeries");
        var sheet = workbook.AddSheet("Data");
        var verbatimFormulas = new List<ChartSeriesVerbatimFormulas>();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 48)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            VerbatimSeriesFormulas = verbatimFormulas
        };

        for (var seriesIndex = 0; seriesIndex < 48; seriesIndex++)
        {
            verbatimFormulas.Add(new ChartSeriesVerbatimFormulas(
                seriesIndex, $"NamedValue{seriesIndex}", null, null));
        }

        // FirstOrDefault previously kept this earlier record. The indexed writer must never let a
        // later duplicate silently replace its verbatim formula.
        verbatimFormulas.Add(new ChartSeriesVerbatimFormulas(0, "NamedDuplicate", null, null));

        var chartXml = FreeX.Core.IO.XlsxChartXmlWriter.ToChartXml(chart, workbook, sheet);
        var series = chartXml.Descendants(ChartNs + "ser").ToList();

        series.Should().HaveCount(48);
        series[0].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("NamedValue0");
        series[47].Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("NamedValue47");
    }

    [Fact]
    public void ColumnChart_DenseSeriesFormats_KeepLastDuplicateFormat()
    {
        var workbook = new Workbook("DenseClassicSeriesFormats");
        var sheet = workbook.AddSheet("Data");
        var formats = new List<ChartSeriesFormat>();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 48)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            SeriesFormats = formats
        };

        for (var seriesIndex = 0; seriesIndex < 48; seriesIndex++)
            formats.Add(new ChartSeriesFormat(seriesIndex, FillColor: new CellColor(1, 2, (byte)seriesIndex)));

        // LastOrDefault previously chose this later entry. The format lookup must retain that
        // override semantics rather than preserving the first color seen for a series.
        formats.Add(new ChartSeriesFormat(0, FillColor: new CellColor(0x11, 0x22, 0x33)));

        var chartXml = FreeX.Core.IO.XlsxChartXmlWriter.ToChartXml(chart, workbook, sheet);
        var series = chartXml.Descendants(ChartNs + "ser").ToList();

        series.Should().HaveCount(48);
        SeriesFillColor(series[0]).Should().Be("112233");
        SeriesFillColor(series[47]).Should().Be("01022F");
    }

    private static string SeriesFillColor(XElement series) =>
        series.Element(ChartNs + "spPr")!
            .Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "srgbClr")!
            .Attribute("val")!.Value;

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
