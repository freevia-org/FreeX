using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Each c:*Chart plot-type element accepts a different ECMA-376 CT_*Ser content model, and the
/// optional series children (c:smooth, c:marker, c:invertIfNegative) are declared on only some of
/// them. A <see cref="ChartSeries"/> can carry a value its current chart type cannot express — a
/// chart-type change from Line to Radar leaves SmoothLine set, and foreign decks round-trip
/// arbitrary combinations — so the writer must gate on the series schema, not just on whether the
/// model field is populated. Emitting c:smooth in a CT_RadarSer makes PowerPoint repair the deck.
/// </summary>
public sealed class ChartSeriesSchemaGatingTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void RadarSeries_WithSmoothLineSet_DoesNotEmitSmoothAndStaysSchemaValid()
    {
        var series = new ChartSeries { Name = "Coverage", SmoothLine = true };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(ChartType.Radar, series);

        var radarSeries = SingleSeries(bytes, "radarChart");
        radarSeries.Element(C + "smooth").Should()
            .BeNull("CT_RadarSer does not declare c:smooth — PowerPoint repairs a deck that has one");

        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.ColumnClustered, "barChart")]
    [InlineData(ChartType.Pie,             "pieChart")]
    [InlineData(ChartType.Area,            "areaChart")]
    [InlineData(ChartType.Doughnut,        "doughnutChart")]
    public void NonLineSeries_WithSmoothLineSet_DoesNotEmitSmooth(ChartType chartType, string plotElement)
    {
        var series = new ChartSeries { Name = "Coverage", SmoothLine = true };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(chartType, series);

        SingleSeries(bytes, plotElement).Element(C + "smooth").Should().BeNull();
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Line,        "lineChart")]
    [InlineData(ChartType.LineMarkers, "lineChart")]
    [InlineData(ChartType.Stock,       "stockChart")]
    public void LineFamilySeries_StillEmitsSmooth(ChartType chartType, string plotElement)
    {
        var series = new ChartSeries { Name = "Coverage", SmoothLine = true };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(chartType, series);

        var smooth = SingleSeries(bytes, plotElement).Element(C + "smooth");
        smooth.Should().NotBeNull("CT_LineSer declares c:smooth and the authored decision must survive");
        smooth!.Attribute("val")!.Value.Should().Be("1");
    }

    [Fact]
    public void ScatterSeries_StillEmitsSmooth()
    {
        var series = new ChartSeries { Name = "Coverage", SmoothLine = false };
        series.XValues.AddRange(new double?[] { 1, 2, 3, 4 });
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(ChartType.Scatter, series);

        var smooth = SingleSeries(bytes, "scatterChart").Element(C + "smooth");
        smooth.Should().NotBeNull("CT_ScatterSer declares c:smooth");
        smooth!.Attribute("val")!.Value.Should().Be("0");
    }

    [Fact]
    public void BubbleSeries_WithSmoothLineSet_DoesNotEmitSmooth()
    {
        // Bubble shares the scatter series builder but is CT_BubbleSer, which carries
        // bubbleSize/bubble3D where scatter has c:smooth.
        var series = new ChartSeries { Name = "Coverage", SmoothLine = true };
        series.XValues.AddRange(new double?[] { 1, 2, 3 });
        series.Values.AddRange(new double?[] { 10, 12, 14 });
        series.BubbleSizes.AddRange(new double?[] { 4, 5, 6 });
        var bytes = WriteSingleChartDeck(ChartType.Bubble, series);

        SingleSeries(bytes, "bubbleChart").Element(C + "smooth").Should().BeNull();
        ValidateSchema(bytes).Should().BeEmpty();
    }

    // ── c:marker: CT_LineSer / CT_ScatterSer / CT_RadarSer only ──────────────

    [Theory]
    [InlineData(ChartType.ColumnClustered, "barChart")]
    [InlineData(ChartType.Pie,             "pieChart")]
    [InlineData(ChartType.Area,            "areaChart")]
    public void NonMarkerSeries_WithMarkerStyleSet_DoesNotEmitMarker(ChartType chartType, string plotElement)
    {
        var series = new ChartSeries
        {
            Name = "Coverage",
            MarkerStyle = new ChartMarkerStyle { Symbol = ChartMarkerSymbol.Diamond, SizePt = 8 },
        };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(chartType, series);

        SingleSeries(bytes, plotElement).Element(C + "marker").Should().BeNull();
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.LineMarkers, "lineChart")]
    [InlineData(ChartType.Radar,       "radarChart")]
    public void MarkerCapableSeries_StillEmitsMarker(ChartType chartType, string plotElement)
    {
        var series = new ChartSeries
        {
            Name = "Coverage",
            MarkerStyle = new ChartMarkerStyle { Symbol = ChartMarkerSymbol.Diamond, SizePt = 8 },
        };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        var bytes = WriteSingleChartDeck(chartType, series);

        var marker = SingleSeries(bytes, plotElement).Element(C + "marker");
        marker.Should().NotBeNull();
        marker!.Element(C + "symbol")!.Attribute("val")!.Value.Should().Be("diamond");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    // ── c:invertIfNegative: CT_BarSer / CT_BubbleSer only ────────────────────

    [Theory]
    [InlineData(ChartType.Line,  "lineChart")]
    [InlineData(ChartType.Radar, "radarChart")]
    [InlineData(ChartType.Pie,   "pieChart")]
    [InlineData(ChartType.Area,  "areaChart")]
    public void NonBarSeries_WithInvertIfNegativeSet_DoesNotEmitIt(ChartType chartType, string plotElement)
    {
        var series = new ChartSeries { Name = "Coverage", InvertIfNegative = true };
        series.Values.AddRange(new double?[] { 10, -12, 14, 15 });
        var bytes = WriteSingleChartDeck(chartType, series);

        SingleSeries(bytes, plotElement).Element(C + "invertIfNegative").Should().BeNull();
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void BarSeries_StillEmitsInvertIfNegative()
    {
        var series = new ChartSeries { Name = "Coverage", InvertIfNegative = true };
        series.Values.AddRange(new double?[] { 10, -12, 14, 15 });
        var bytes = WriteSingleChartDeck(ChartType.ColumnClustered, series);

        var invert = SingleSeries(bytes, "barChart").Element(C + "invertIfNegative");
        invert.Should().NotBeNull();
        invert!.Attribute("val")!.Value.Should().Be("1");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void BubbleSeries_StillEmitsInvertIfNegative()
    {
        var series = new ChartSeries { Name = "Coverage", InvertIfNegative = true };
        series.XValues.AddRange(new double?[] { 1, 2, 3 });
        series.Values.AddRange(new double?[] { 10, -12, 14 });
        series.BubbleSizes.AddRange(new double?[] { 4, 5, 6 });
        var bytes = WriteSingleChartDeck(ChartType.Bubble, series);

        SingleSeries(bytes, "bubbleChart").Element(C + "invertIfNegative")
            .Should().NotBeNull("CT_BubbleSer declares c:invertIfNegative");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    // ── Secondary plot group is always a lineChart ───────────────────────────

    [Fact]
    public void SecondaryAxisSeries_KeepsSmoothAndDropsInvertIfNegative()
    {
        // The secondary group is emitted as a c:lineChart no matter what the primary type is,
        // so its series are CT_LineSer even when the chart is a column chart.
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            SecondaryValueAxis = new ChartAxis(),
            RegenerateWorkbookOnSave = true,
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        var primary = new ChartSeries { Name = "Revenue", InvertIfNegative = true, SmoothLine = true };
        primary.Values.AddRange(new double?[] { 10, -12, 14, 15 });
        var secondary = new ChartSeries
        {
            Name = "Margin",
            OnSecondaryAxis = true,
            InvertIfNegative = true,
            SmoothLine = true,
        };
        secondary.Values.AddRange(new double?[] { 1, 2, 3, 4 });
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var bytes = WriteDeck(chart);
        var document = LoadChartXml(bytes);
        var plotArea = document.Root!.Element(C + "chart")!.Element(C + "plotArea")!;

        var primarySer = plotArea.Element(C + "barChart")!.Elements(C + "ser").Single();
        primarySer.Element(C + "smooth").Should().BeNull();
        primarySer.Element(C + "invertIfNegative").Should().NotBeNull();

        var secondarySer = plotArea.Element(C + "lineChart")!.Elements(C + "ser").Single();
        secondarySer.Element(C + "smooth").Should().NotBeNull();
        secondarySer.Element(C + "invertIfNegative").Should()
            .BeNull("the secondary group is a c:lineChart, so its series are CT_LineSer");

        // Filtered to c:ser: the secondary c:valAx independently emits c:crosses before
        // c:crossAx, which is a separate (pre-existing) CT_ValAx ordering defect.
        ValidateSchema(bytes).Where(error => error.Contains("c:ser[", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    // ── Data points keep markers on every chart type, in schema order ────────

    [Fact]
    public void PieDataPoint_KeepsMarkerButOrdersItBeforeExplosionAndShapeProperties()
    {
        // Unlike c:ser, CT_DPt declares c:marker for every chart type — but its sequence is
        // idx, invertIfNegative, marker, bubble3D, explosion, spPr.
        var chart = new ChartShape { ChartType = ChartType.Pie, RegenerateWorkbookOnSave = true };
        chart.Categories.AddRange(new[] { "A", "B" });
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 60 });
        series.PointStyles[0] = new ChartPointStyle
        {
            ExplosionPercent = 15,
            Marker = new ChartMarkerStyle { Symbol = ChartMarkerSymbol.Circle, SizePt = 5 },
            FillColor = new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
        };
        chart.Series.Add(series);

        var bytes = WriteDeck(chart);
        var dataPoint = SingleSeries(bytes, "pieChart").Elements(C + "dPt").Single();

        dataPoint.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("idx", "marker", "explosion", "spPr");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] WriteSingleChartDeck(ChartType chartType, ChartSeries series)
    {
        // RegenerateWorkbookOnSave makes the writer address the regenerated workbook, so every
        // c:numRef/c:strRef gets its required c:f child and the schema check exercises the series
        // content model rather than tripping on a missing formula reference.
        var chart = new ChartShape { ChartType = chartType, RegenerateWorkbookOnSave = true };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" }.Take(series.Values.Count));
        chart.Series.Add(series);
        return WriteDeck(chart);
    }

    private static byte[] WriteDeck(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static XElement SingleSeries(byte[] bytes, string plotElementName) =>
        LoadChartXml(bytes).Root!
            .Element(C + "chart")!
            .Element(C + "plotArea")!
            .Element(C + plotElementName)!
            .Elements(C + "ser")
            .Single();

    private static XDocument LoadChartXml(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.Entries.Single(item =>
            item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static string[] ValidateSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = PresentationDocument.Open(stream, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }
}
