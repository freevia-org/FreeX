using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Stacked / 100% stacked grouping and the radar, bubble, stock and column + line combination kinds:
/// each must be written as schema-valid chart XML, read back as the same editable chart, load from
/// foreign (Word/Excel-authored) markup, and anything still unmodelled must stay preserved verbatim.
/// </summary>
public class ChartFamilyRoundTripTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static Chart ThreeSeries(ChartKind kind)
    {
        var chart = new Chart { Kind = kind, Title = "Family" };
        chart.Categories.AddRange(["1", "2", "3"]);
        chart.Series.Add(new ChartSeries("High", [12, 14, 13]));
        chart.Series.Add(new ChartSeries("Low", [8, 9, 7]));
        chart.Series.Add(new ChartSeries("Close", [10, 13, 9]));
        return chart;
    }

    private static MemoryStream Write(Chart chart)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);
        return Write(document);
    }

    private static MemoryStream Write(TextDocument document)
    {
        var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return stream;
    }

    private static XElement PlotArea(Stream docx)
    {
        docx.Position = 0;
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.Single(e => e.FullName.StartsWith("word/charts/chart", StringComparison.Ordinal)
            && e.FullName.EndsWith(".xml", StringComparison.Ordinal) && !e.FullName.Contains("_rels", StringComparison.Ordinal));
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!.Element(C + "chart")!.Element(C + "plotArea")!;
    }

    private static Chart? ReadChart(Stream docx)
    {
        docx.Position = 0;
        return DocxReader.Read(docx).Paragraphs.SelectMany(p => p.Runs).Select(run => run.Chart).FirstOrDefault(c => c is not null);
    }

    private static Chart RoundTrip(Chart chart) => ReadChart(Write(chart))!;

    private static void AssertChartPartIsSchemaValid(Stream docx)
    {
        docx.Position = 0;
        using var copy = new MemoryStream();
        docx.CopyTo(copy);
        copy.Position = 0;
        using var package = WordprocessingDocument.Open(copy, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(package)
            .Where(error => error.Part?.Uri.OriginalString.Contains("/charts/", StringComparison.Ordinal) == true)
            .Select(error => $"{error.Id}: {error.Description}; node={error.Node?.LocalName}")
            .ToList();
        errors.Should().BeEmpty();
    }

    /// <summary>Rewrites the chart part of a FreeW-written package, simulating a Word/Excel-authored chart.</summary>
    private static MemoryStream WithChartPart(Stream docx, Action<XElement> editPlotArea)
    {
        docx.Position = 0;
        var output = new MemoryStream();
        using (var source = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = target.CreateEntry(entry.FullName).Open();
                var isChart = entry.FullName.StartsWith("word/charts/chart", StringComparison.Ordinal)
                    && entry.FullName.EndsWith(".xml", StringComparison.Ordinal);
                if (!isChart)
                {
                    from.CopyTo(to);
                    continue;
                }

                var xml = XDocument.Load(from);
                editPlotArea(xml.Root!.Element(C + "chart")!.Element(C + "plotArea")!);
                xml.Save(to);
            }
        }

        output.Position = 0;
        return output;
    }

    private static List<string> ChartPartSchemaErrors(Stream docx)
    {
        docx.Position = 0;
        using var copy = new MemoryStream();
        docx.CopyTo(copy);
        copy.Position = 0;
        using var package = WordprocessingDocument.Open(copy, false);
        return new OpenXmlValidator(FileFormatVersions.Office2013).Validate(package)
            .Where(error => error.Part?.Uri.OriginalString.Contains("/charts/", StringComparison.Ordinal) == true)
            .Select(error => error.Id)
            .ToList();
    }

    /// <summary>
    /// Sibling control: the schema check above must actually reach the chart part, otherwise every
    /// "is schema valid" assertion in this class would be vacuously green.
    /// </summary>
    [Fact]
    public void SchemaValidation_ReachesTheChartPart_AndRejectsBrokenChartXml()
    {
        using var written = Write(ThreeSeries(ChartKind.Column));
        ChartPartSchemaErrors(written).Should().BeEmpty();

        using var broken = WithChartPart(written, plotArea =>
            plotArea.Element(C + "barChart")!.AddFirst(new XElement(C + "notAChartElement")));

        ChartPartSchemaErrors(broken).Should().NotBeEmpty();
    }

    // ── Grouping ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChartKind.Column, ChartGrouping.Stacked, "barChart", "stacked")]
    [InlineData(ChartKind.Column, ChartGrouping.PercentStacked, "barChart", "percentStacked")]
    [InlineData(ChartKind.Bar, ChartGrouping.Stacked, "barChart", "stacked")]
    [InlineData(ChartKind.Bar, ChartGrouping.PercentStacked, "barChart", "percentStacked")]
    [InlineData(ChartKind.Line, ChartGrouping.Stacked, "lineChart", "stacked")]
    [InlineData(ChartKind.Line, ChartGrouping.PercentStacked, "lineChart", "percentStacked")]
    [InlineData(ChartKind.Area, ChartGrouping.Stacked, "areaChart", "stacked")]
    [InlineData(ChartKind.Area, ChartGrouping.PercentStacked, "areaChart", "percentStacked")]
    public void StackedGrouping_IsWrittenValidated_AndReadBack(ChartKind kind, ChartGrouping grouping, string element, string token)
    {
        var chart = ThreeSeries(kind);
        chart.Grouping = grouping;
        using var docx = Write(chart);

        var type = PlotArea(docx).Element(C + element)!;
        type.Element(C + "grouping")!.Attribute("val")!.Value.Should().Be(token);
        // Stacked bars need c:overlap 100 or Word draws them side by side.
        (type.Element(C + "overlap")?.Attribute("val")?.Value).Should().Be(element == "barChart" ? "100" : null);
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Kind.Should().Be(kind);
        read.Grouping.Should().Be(grouping);
        read.Series.Select(series => series.Values.ToArray()).Should().BeEquivalentTo(
            chart.Series.Select(series => series.Values.ToArray()), options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(ChartKind.Column, "clustered")]
    [InlineData(ChartKind.Bar, "clustered")]
    [InlineData(ChartKind.Line, "standard")]
    [InlineData(ChartKind.Area, "standard")]
    public void StandardGrouping_KeepsTheOriginalMarkup_WithNoOverlap(ChartKind kind, string token)
    {
        using var docx = Write(ThreeSeries(kind));

        var type = PlotArea(docx).Elements().First(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));
        type.Element(C + "grouping")!.Attribute("val")!.Value.Should().Be(token);
        type.Element(C + "overlap").Should().BeNull();
        ReadChart(docx)!.Grouping.Should().Be(ChartGrouping.Standard);
    }

    [Fact]
    public void WordAuthoredStackedColumn_NoLongerLoadsAsClustered()
    {
        using var written = Write(ThreeSeries(ChartKind.Column));
        using var foreign = WithChartPart(written, plotArea =>
            plotArea.Element(C + "barChart")!.Element(C + "grouping")!.Attribute("val")!.Value = "stacked");

        ReadChart(foreign)!.Grouping.Should().Be(ChartGrouping.Stacked);
    }

    [Fact]
    public void GroupingOnAKindThatCannotStack_IsNotWritten()
    {
        var chart = ThreeSeries(ChartKind.Scatter);
        chart.Grouping = ChartGrouping.Stacked;
        using var docx = Write(chart);

        PlotArea(docx).Descendants(C + "grouping").Should().BeEmpty();
        ReadChart(docx)!.Grouping.Should().Be(ChartGrouping.Standard);
    }

    // ── Radar ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, "marker")]
    [InlineData(true, "filled")]
    public void Radar_RoundTrips_WithItsStyle(bool filled, string style)
    {
        var chart = ThreeSeries(ChartKind.Radar);
        chart.RadarFilled = filled;
        using var docx = Write(chart);

        var radar = PlotArea(docx).Element(C + "radarChart")!;
        radar.Element(C + "radarStyle")!.Attribute("val")!.Value.Should().Be(style);
        radar.Elements(C + "ser").Should().HaveCount(3);
        PlotArea(docx).Element(C + "catAx").Should().NotBeNull();
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Kind.Should().Be(ChartKind.Radar);
        read.RadarFilled.Should().Be(filled);
        read.Categories.Should().Equal("1", "2", "3");
        read.Series.Select(series => series.Name).Should().Equal("High", "Low", "Close");
    }

    // ── Bubble ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bubble_RoundTripsXValuesYValuesAndSizes()
    {
        var chart = new Chart { Kind = ChartKind.Bubble };
        chart.Categories.AddRange(["1", "2.5", "4"]);
        var series = new ChartSeries("Markets", [10, 20, 15]);
        series.BubbleSizes.AddRange([3, 9, 1.5]);
        chart.Series.Add(series);
        using var docx = Write(chart);

        var bubble = PlotArea(docx).Element(C + "bubbleChart")!;
        var ser = bubble.Element(C + "ser")!;
        ser.Elements().Select(e => e.Name.LocalName).Should().ContainInOrder("xVal", "yVal", "bubbleSize", "bubble3D");
        PlotArea(docx).Elements(C + "valAx").Should().HaveCount(2, "a bubble chart has two value axes");
        PlotArea(docx).Element(C + "catAx").Should().BeNull();
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Kind.Should().Be(ChartKind.Bubble);
        read.Categories.Should().Equal("1", "2.5", "4");
        read.Series.Single().Values.Should().Equal(10, 20, 15);
        read.Series.Single().BubbleSizes.Should().Equal(3, 9, 1.5);
    }

    [Fact]
    public void Bubble_WithoutStoredSizes_WritesTheValueMagnitudes()
    {
        var chart = Chart.Create(ChartKind.Bubble, ["1", "2"], [4d, -6d]);

        RoundTrip(chart).Series.Single().BubbleSizes.Should().Equal(4, 6);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("abc")]
    public void ForeignBubbleSize_OutsideTheAcceptRange_ReadsAsZero(string hostile)
    {
        var chart = Chart.Create(ChartKind.Bubble, ["1", "2"], [4d, 6d]);
        using var written = Write(chart);
        using var foreign = WithChartPart(written, plotArea =>
            plotArea.Descendants(C + "bubbleSize").Single().Descendants(C + "v").First().Value = hostile);

        ReadChart(foreign)!.Series.Single().BubbleSizes.Should().Equal(0, 6);
    }

    // ── Stock ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stock_HighLowClose_RoundTrips()
    {
        using var docx = Write(ThreeSeries(ChartKind.Stock));

        var stock = PlotArea(docx).Element(C + "stockChart")!;
        stock.Elements(C + "ser").Should().HaveCount(3);
        stock.Element(C + "hiLowLines").Should().NotBeNull();
        stock.Elements().Select(e => e.Name.LocalName).Should().ContainInOrder("ser", "hiLowLines", "axId");
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Kind.Should().Be(ChartKind.Stock);
        read.Series.Select(series => series.Name).Should().Equal("High", "Low", "Close");
        read.Series[2].Values.Should().Equal(10, 13, 9);
    }

    [Fact]
    public void StockKind_WithoutThreeSeries_IsWrittenAsAValidLineChart()
    {
        var chart = Chart.Create(ChartKind.Stock, ["A", "B"], [1d, 2d]);
        using var docx = Write(chart);

        PlotArea(docx).Element(C + "stockChart").Should().BeNull();
        PlotArea(docx).Element(C + "lineChart").Should().NotBeNull();
        AssertChartPartIsSchemaValid(docx);
        ReadChart(docx)!.Series.Single().Values.Should().Equal(1, 2);
    }

    [Fact]
    public void StockCategoriesHeldInANumberCache_AreRead()
    {
        using var written = Write(ThreeSeries(ChartKind.Stock));
        using var foreign = WithChartPart(written, plotArea =>
        {
            // Excel writes date categories as c:numRef / c:numCache.
            foreach (var cat in plotArea.Descendants(C + "cat").ToList())
            {
                cat.ReplaceNodes(new XElement(C + "numRef",
                    new XElement(C + "f", "Sheet1!$A$2:$A$4"),
                    new XElement(C + "numCache",
                        new XElement(C + "formatCode", "General"),
                        new XElement(C + "ptCount", new XAttribute("val", 3)),
                        new XElement(C + "pt", new XAttribute("idx", 0), new XElement(C + "v", "45000")),
                        new XElement(C + "pt", new XAttribute("idx", 1), new XElement(C + "v", "45001")),
                        new XElement(C + "pt", new XAttribute("idx", 2), new XElement(C + "v", "45002")))));
            }
        });

        ReadChart(foreign)!.Categories.Should().Equal("45000", "45001", "45002");
    }

    // ── Combination ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Combo_ColumnPlusLine_RoundTripsOnThePrimaryAxes()
    {
        var chart = ThreeSeries(ChartKind.Combo);
        chart.Series[2].PlotAsLine = true;
        using var docx = Write(chart);

        var plotArea = PlotArea(docx);
        plotArea.Element(C + "barChart")!.Elements(C + "ser").Should().HaveCount(2);
        plotArea.Elements(C + "lineChart").Single().Elements(C + "ser").Should().ContainSingle();
        plotArea.Elements(C + "valAx").Should().ContainSingle();
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Kind.Should().Be(ChartKind.Combo);
        read.Series.Select(series => (series.Name, series.PlotAsLine, series.UseSecondaryAxis)).Should().Equal(
            ("High", false, false), ("Low", false, false), ("Close", true, false));
    }

    [Fact]
    public void Combo_WithASecondaryAxis_WritesBothAxisPairs_AndKeepsTheModelSeriesOrder()
    {
        var chart = ThreeSeries(ChartKind.Combo);
        // A line series BEFORE the columns: the file groups series by chart type, the model must not.
        chart.Series[0].PlotAsLine = true;
        chart.Series[0].UseSecondaryAxis = true;
        using var docx = Write(chart);

        var plotArea = PlotArea(docx);
        plotArea.Elements(C + "valAx").Should().HaveCount(2);
        plotArea.Elements(C + "catAx").Should().HaveCount(2);
        var secondary = plotArea.Elements(C + "valAx").Last();
        secondary.Element(C + "axPos")!.Attribute("val")!.Value.Should().Be("r");
        secondary.Element(C + "crosses")!.Attribute("val")!.Value.Should().Be("max");
        var lineAxes = plotArea.Element(C + "lineChart")!.Elements(C + "axId").Select(a => a.Attribute("val")!.Value);
        var barAxes = plotArea.Element(C + "barChart")!.Elements(C + "axId").Select(a => a.Attribute("val")!.Value);
        lineAxes.Should().NotIntersectWith(barAxes);
        plotArea.Descendants(C + "ser").Select(s => s.Element(C + "idx")!.Attribute("val")!.Value)
            .Should().OnlyHaveUniqueItems();
        AssertChartPartIsSchemaValid(docx);

        var read = ReadChart(docx)!;
        read.Series.Select(series => (series.Name, series.PlotAsLine, series.UseSecondaryAxis)).Should().Equal(
            ("High", true, true), ("Low", false, false), ("Close", false, false));
    }

    [Fact]
    public void ComboKind_WithOnlyColumns_IsWrittenAsAPlainColumnChart()
    {
        using var docx = Write(ThreeSeries(ChartKind.Combo));

        PlotArea(docx).Element(C + "lineChart").Should().BeNull();
        AssertChartPartIsSchemaValid(docx);
        ReadChart(docx)!.Kind.Should().Be(ChartKind.Column);
    }

    // ── Stability and the verbatim fallback ───────────────────────────────────────────────────────

    public static IEnumerable<object[]> FamilyCharts()
    {
        Chart Make(ChartKind kind, Action<Chart>? configure = null)
        {
            var chart = ThreeSeries(kind);
            configure?.Invoke(chart);
            return chart;
        }

        yield return [Make(ChartKind.Column, c => c.Grouping = ChartGrouping.Stacked)];
        yield return [Make(ChartKind.Area, c => c.Grouping = ChartGrouping.PercentStacked)];
        yield return [Make(ChartKind.Radar, c => c.RadarFilled = true)];
        yield return [Make(ChartKind.Bubble, c => c.Series[0].BubbleSizes.AddRange([1, 2, 3]))];
        yield return [Make(ChartKind.Stock)];
        yield return [Make(ChartKind.Combo, c => { c.Series[1].PlotAsLine = true; c.Series[1].UseSecondaryAxis = true; })];
    }

    [Theory]
    [MemberData(nameof(FamilyCharts))]
    public void FamilyChart_IsStableAcrossRepeatedSaves(Chart chart)
    {
        using var first = Write(chart);
        var firstXml = PlotArea(first).ToString(SaveOptions.DisableFormatting);
        first.Position = 0;
        using var second = Write(DocxReader.Read(first));

        PlotArea(second).ToString(SaveOptions.DisableFormatting).Should().Be(firstXml);
    }

    [Theory]
    [InlineData("bar3DChart")]
    [InlineData("surfaceChart")]
    [InlineData("ofPieChart")]
    public void UnmodelledChartType_IsNotLoadedLossily_AndSurvivesTheSaveVerbatim(string unmodelled)
    {
        using var written = Write(ThreeSeries(ChartKind.Column));
        using var foreign = WithChartPart(written, plotArea => plotArea.Element(C + "barChart")!.Name = C + unmodelled);

        var document = DocxReader.Read(foreign);

        document.Paragraphs.SelectMany(p => p.Runs).Should().NotContain(run => run.Chart != null);
        using var saved = Write(document);
        PlotArea(saved).Element(C + unmodelled).Should().NotBeNull("the chart must be kept, not dropped or downgraded");
        PlotArea(saved).Descendants(C + "ser").Should().HaveCount(3);
    }

    [Fact]
    public void OpenHighLowCloseStock_StaysPreservedVerbatim()
    {
        using var written = Write(ThreeSeries(ChartKind.Stock));
        using var foreign = WithChartPart(written, plotArea =>
        {
            var stock = plotArea.Element(C + "stockChart")!;
            var fourth = new XElement(stock.Elements(C + "ser").Last());
            fourth.Element(C + "idx")!.Attribute("val")!.Value = "3";
            fourth.Element(C + "order")!.Attribute("val")!.Value = "3";
            stock.Elements(C + "ser").Last().AddAfterSelf(fourth);
        });

        var document = DocxReader.Read(foreign);

        document.Paragraphs.SelectMany(p => p.Runs).Should().NotContain(run => run.Chart != null);
        using var saved = Write(document);
        PlotArea(saved).Element(C + "stockChart")!.Elements(C + "ser").Should().HaveCount(4);
    }

    [Fact]
    public void CombinationOtherThanColumnPlusLine_StaysPreservedVerbatim()
    {
        var chart = ThreeSeries(ChartKind.Combo);
        chart.Series[2].PlotAsLine = true;
        using var written = Write(chart);
        // Column + AREA: before this change the area series was silently dropped on load.
        using var foreign = WithChartPart(written, plotArea => plotArea.Element(C + "lineChart")!.Name = C + "areaChart");

        var document = DocxReader.Read(foreign);

        document.Paragraphs.SelectMany(p => p.Runs).Should().NotContain(run => run.Chart != null);
        using var saved = Write(document);
        PlotArea(saved).Element(C + "areaChart").Should().NotBeNull();
        PlotArea(saved).Descendants(C + "ser").Should().HaveCount(3);
    }
}
