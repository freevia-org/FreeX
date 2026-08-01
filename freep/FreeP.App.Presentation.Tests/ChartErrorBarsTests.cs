using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartErrorBarsTests
{
    [Fact]
    public void SeriesOptionsPlanner_RoundTripsTrendlineOptionsInWorkingCopy()
    {
        var chart = new ChartShape();
        chart.Series.Add(new ChartSeries { Name = "Revenue" });

        var planner = ChartSeriesOptionsPlanner.FromChart(chart);
        planner.SetTrendlineEnabled(true);
        planner.SetTrendlineType(ChartTrendlineType.Polynomial);
        planner.SetTrendlineOrder(3);
        planner.SetTrendlineForward(1.5);
        planner.SetTrendlineBackward(0.5);
        planner.SetTrendlineEquation(true);
        planner.SetTrendlineRSquared(true);

        var options = planner.BuildCommitPlan();
        options.Trendline.Should().NotBeNull();
        options.Trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        options.Trendline.PolynomialOrder.Should().Be(3);
        options.Trendline.Forward.Should().Be(1.5);
        options.Trendline.Backward.Should().Be(0.5);
        options.Trendline.DisplayEquation.Should().BeTrue();
        options.Trendline.DisplayRSquared.Should().BeTrue();
        chart.Series[0].Trendline.Should().BeNull("the planner is a working copy");
    }

    [Fact]
    public void Trendline_PackageRoundTripPreservesAuthoredSeriesSettings()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        var series = new ChartSeries
        {
            Name = "Revenue",
            Trendline = new ChartTrendline
            {
                Type = ChartTrendlineType.MovingAverage,
                MovingAveragePeriod = 3,
                Forward = 1.5,
                Backward = 0.5,
                DisplayEquation = true,
                DisplayRSquared = true,
            },
        };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Trendline",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopenedChart = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        var roundTripped = reopenedChart.Series[0].Trendline;

        roundTripped.Should().NotBeNull();
        roundTripped!.Type.Should().Be(ChartTrendlineType.MovingAverage);
        roundTripped.MovingAveragePeriod.Should().Be(3);
        roundTripped.Forward.Should().Be(1.5);
        roundTripped.Backward.Should().Be(0.5);
        roundTripped.DisplayEquation.Should().BeTrue();
        roundTripped.DisplayRSquared.Should().BeTrue();

        var reopenedPlanner = ChartSeriesOptionsPlanner.FromChart(reopenedChart);
        reopenedPlanner.TrendlineForward.Should().Be(1.5);
        reopenedPlanner.TrendlineBackward.Should().Be(0.5);
        var reopenedOptions = reopenedPlanner.BuildCommitPlan();
        reopenedOptions.Trendline.Should().NotBeNull();
        reopenedOptions.Trendline!.Forward.Should().Be(1.5);
        reopenedOptions.Trendline.Backward.Should().Be(0.5);
    }

    [Fact]
    public void ScatterTrendline_PackageRoundTripPreservesAuthoredSeriesSettings()
    {
        var chart = new ChartShape { ChartType = ChartType.Scatter };
        var series = new ChartSeries
        {
            Name = "Revenue",
            Trendline = new ChartTrendline
            {
                Type = ChartTrendlineType.Polynomial,
                PolynomialOrder = 3,
                DisplayEquation = true,
            },
        };
        series.XValues.AddRange(new double?[] { 1, 2, 3, 4 });
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Scatter trendline",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        var trendline = reopened.Series[0].Trendline;

        trendline.Should().NotBeNull();
        trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        trendline.PolynomialOrder.Should().Be(3);
        trendline.DisplayEquation.Should().BeTrue();
    }

    [Fact]
    public void SeriesOptionsPlanner_RoundTripsErrorBarOptionsInWorkingCopy()
    {
        var chart = new ChartShape();
        chart.Series.Add(new ChartSeries { Name = "Revenue" });

        var planner = ChartSeriesOptionsPlanner.FromChart(chart);
        planner.SetErrorBarsEnabled(true);
        planner.SetErrorDirection(ChartErrorDirection.X);
        planner.SetErrorBarType(ChartErrorBarType.Plus);
        planner.SetErrorValueType(ChartErrorValueType.Percentage);
        planner.SetErrorValue(12.5);
        planner.SetErrorNoEndCap(true);

        var options = planner.BuildCommitPlan();
        options.ErrorBars.Should().NotBeNull();
        options.ErrorBars!.Direction.Should().Be(ChartErrorDirection.X);
        options.ErrorBars.BarType.Should().Be(ChartErrorBarType.Plus);
        options.ErrorBars.ValueType.Should().Be(ChartErrorValueType.Percentage);
        options.ErrorBars.Value.Should().Be(12.5);
        options.ErrorBars.NoEndCap.Should().BeTrue();
        chart.Series[0].ErrorBars.Should().BeNull("the planner is a working copy");
    }

    [Fact]
    public void ErrorBars_PackageRoundTripPreservesAuthoredSeriesSettings()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        var series = new ChartSeries { Name = "Revenue", ErrorBars = new ChartErrorBars
        {
            Direction = ChartErrorDirection.Y,
            BarType = ChartErrorBarType.Both,
            ValueType = ChartErrorValueType.Fixed,
            Value = 1.25,
            NoEndCap = true,
        }};
        series.Values.AddRange(new double?[] { 10, 12, 14 });
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Error bars",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!.Series[0].ErrorBars;

        roundTripped.Should().NotBeNull();
        roundTripped!.Direction.Should().Be(ChartErrorDirection.Y);
        roundTripped.BarType.Should().Be(ChartErrorBarType.Both);
        roundTripped.ValueType.Should().Be(ChartErrorValueType.Fixed);
        roundTripped.Value.Should().Be(1.25);
        roundTripped.NoEndCap.Should().BeTrue();
    }

    [Fact]
    public void ErrorBars_WriterEmitsCanonicalChartElements()
    {
        var chart = new ChartShape();
        chart.Categories.Add("Q1");
        var series = new ChartSeries { Name = "Revenue", ErrorBars = new ChartErrorBars
        {
            Direction = ChartErrorDirection.X,
            BarType = ChartErrorBarType.Minus,
            ValueType = ChartErrorValueType.Percentage,
            Value = 8,
        }};
        series.Values.Add(10);
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 1, Name = "C", Kind = SlideShapeKind.Chart, ExtentCxEmu = 1_000_000, ExtentCyEmu = 1_000_000, Chart = chart });
        presentation.Slides.Add(slide);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.Single(item => item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase));
        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var errorBars = document.Descendants(c + "errBars").Single();
        errorBars.Element(c + "errDir")!.Attribute("val")!.Value.Should().Be("x");
        errorBars.Element(c + "errBarType")!.Attribute("val")!.Value.Should().Be("minus");
        errorBars.Element(c + "errValType")!.Attribute("val")!.Value.Should().Be("percentage");
        errorBars.Element(c + "val")!.Attribute("val")!.Value.Should().Be("8");
    }
}
