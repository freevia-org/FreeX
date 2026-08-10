using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R133-io-chart-radar-stale-smooth: <see cref="XlsxChartXmlWriter"/> (via
/// <c>BuildChartSeries</c> in XlsxChartXmlWriter.Series.cs) used to gate the per-series
/// &lt;c:smooth&gt; element on <c>chart.Type is Line or ThreeDLine || forceLineShapeProperties</c>
/// -- the exact same condition used for the series-level marker/spPr line-style branch just above
/// it. <c>forceLineShapeProperties</c> is ALSO true for Radar charts (they share that line-style
/// marker/spPr handling), but CT_RadarSer has no &lt;c:smooth&gt; child at all, so a series that
/// still carried a stale <see cref="ChartSeriesFormat.Smooth"/> flag from a prior Line/ThreeDLine/
/// Scatter chart type produced a schema-invalid &lt;c:radarChart&gt; that Excel has to repair.
/// </summary>
public sealed class R133_ChartSeriesSmoothTypeGateTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void RadarChart_WithStaleSmoothFlagFromPriorLineType_EmitsNoSmoothElement()
    {
        // Simulates a series that still carries a Smooth flag left over from before the chart was
        // switched to Radar -- either because the model was constructed directly (e.g. a load path
        // that doesn't route through SetChartLayoutCommand's model-side ClampSeriesFormat), or a
        // file "authored elsewhere" that pairs a Radar series with a captured smooth value. The
        // writer itself must refuse to emit <c:smooth> here regardless of how the flag got set.
        var workbook = new Workbook("RadarStaleSmooth");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Axis"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series1"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"A{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 3));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, Smooth: true)],
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var radarSeries = chartDoc.Root!.Descendants(ChartNs + "radarChart")
            .Descendants(ChartNs + "ser").Should().ContainSingle().Subject;
        radarSeries.Element(ChartNs + "smooth").Should()
            .BeNull("CT_RadarSer has no <c:smooth> child -- emitting one makes the file schema-invalid");

        SchemaErrors(saved).Should().BeEmpty("a stale Smooth flag must never make the saved package schema-invalid");
    }

    [Fact]
    public void StockVolumeChart_WithStaleSmoothFlagOnVolumeSeries_EmitsNoSmoothInBarSeries()
    {
        // Sibling hazard uncovered while fixing the Radar case: a VolumeHighLowClose/
        // VolumeOpenHighLowClose stock chart pairs a <c:barChart> (the volume series, CT_BarSer --
        // also has no <c:smooth>) with a <c:stockChart> (CT_LineSer, which DOES support smooth).
        // Both are built by BuildChartSeries for the same ChartModel (chart.Type == Stock in both
        // calls), so a bare chart.Type-based gate cannot tell the two <c:ser> emissions apart and
        // would wrongly leak <c:smooth> into the volume bar series too. Only
        // CreateStockVolumeBarChart's caller passes forceLineShapeProperties: false (its default),
        // which is what the fixed gate actually keys off.
        var workbook = new Workbook("StockVolumeStaleSmooth");
        var sheet = workbook.AddSheet("Data");
        string[] headers = ["Date", "Volume", "High", "Low", "Close"];
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)index + 1), new TextValue(headers[index]));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Day {row - 1}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(1000 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(15 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(9 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(13 + row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeHighLowClose,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5)),
            // Series index 0 is the volume series -- CreateStockVolumeBarChart routes it into the
            // <c:barChart>, not the <c:stockChart>.
            SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, Smooth: true)],
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var barSeries = chartDoc.Root!.Descendants(ChartNs + "barChart")
            .Descendants(ChartNs + "ser").Should().ContainSingle().Subject;
        barSeries.Element(ChartNs + "smooth").Should()
            .BeNull("CT_BarSer has no <c:smooth> child -- emitting one makes the file schema-invalid");

        SchemaErrors(saved).Should().BeEmpty("a stale Smooth flag on the volume series must never leak into the barChart");
    }

    [Fact]
    public void LineChart_WithSmoothFlag_StillEmitsSmoothElement()
    {
        // Sibling no-regression: a chart type that genuinely supports <c:smooth> (CT_LineSer) must
        // keep emitting it -- the Radar/Stock-volume exclusion above must not over-correct into
        // dropping smooth for the types that actually use it.
        var workbook = new Workbook("LineSmoothRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, Smooth: true)],
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var lineSeries = chartDoc.Root!.Descendants(ChartNs + "lineChart")
            .Descendants(ChartNs + "ser").Should().ContainSingle().Subject;
        lineSeries.Element(ChartNs + "smooth")!.Attribute("val")!.Value.Should().Be("1");

        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void StockNonVolumeChart_WithSmoothFlag_StillEmitsSmoothInStockSeries()
    {
        // Sibling no-regression for the non-volume Stock path: CreateStockPlotChart always passes
        // forceLineShapeProperties: true, and its <c:ser> elements are CT_LineSer (reused by
        // <c:stockChart>), which DOES support <c:smooth>. This must keep working after the fix.
        var workbook = new Workbook("StockSmoothRegression");
        var sheet = workbook.AddSheet("Data");
        string[] headers = ["Date", "High", "Low", "Close"];
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)index + 1), new TextValue(headers[index]));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Day {row - 1}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(15 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(9 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(13 + row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.HighLowClose,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, Smooth: true)],
        });

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var stockSeries = chartDoc.Root!.Descendants(ChartNs + "stockChart")
            .Descendants(ChartNs + "ser").First(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "0");
        stockSeries.Element(ChartNs + "smooth")!.Attribute("val")!.Value.Should().Be("1");

        SchemaErrors(saved).Should().BeEmpty();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static System.Collections.Generic.List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
