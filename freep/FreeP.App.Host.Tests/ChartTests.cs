using System.IO;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 2b chart tests: model, I/O round-trip, and compositor.
/// </summary>
public sealed class ChartTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ChartTests", Guid.NewGuid().ToString("N"));

    public ChartTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Model — SlideShapeKind.Chart = 5
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SlideShapeKind_Chart_HasValue5()
    {
        ((int)SlideShapeKind.Chart).Should().Be(5);
    }

    [Fact]
    public void ChartShape_DefaultValues()
    {
        var chart = new ChartShape();
        chart.ChartType.Should().Be(ChartType.ColumnClustered);
        chart.Title.Should().BeNull();
        chart.Categories.Should().BeEmpty();
        chart.Series.Should().BeEmpty();
        chart.Legend.Should().BeNull();
    }

    [Fact]
    public void ChartSeries_DefaultValues()
    {
        var series = new ChartSeries();
        series.Name.Should().BeEmpty();
        series.FillColor.Should().BeNull();
        series.Values.Should().BeEmpty();
        series.PointColors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. I/O round-trip — write a chart shape then read it back
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Chart_ShapeKindPreserved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Chart);
        shape.Should().NotBeNull("chart shape should survive round-trip");
    }

    [Fact]
    public void RoundTrip_Chart_AnchorPreserved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart);
        shape.OffsetXEmu.Should().Be(914400,  "offset X preserved");
        shape.OffsetYEmu.Should().Be(457200,  "offset Y preserved");
        shape.ExtentCxEmu.Should().Be(5486400, "width preserved");
        shape.ExtentCyEmu.Should().Be(3657600, "height preserved");
    }

    [Fact]
    public void RoundTrip_Chart_TypePreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart);
        shape.Chart.Should().NotBeNull();
        shape.Chart!.ChartType.Should().Be(ChartType.ColumnClustered);
    }

    [Fact]
    public void RoundTrip_Chart_SeriesCountPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series.Should().HaveCount(2, "two series survive round-trip");
    }

    [Fact]
    public void RoundTrip_Chart_SeriesNamesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series[0].Name.Should().Be("Sales");
        rt.Series[1].Name.Should().Be("Budget");
    }

    [Fact]
    public void RoundTrip_Chart_ValuesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Series[0].Values.Should().HaveCount(3);
        rt.Series[0].Values[0].Should().BeApproximately(100, 0.01);
        rt.Series[0].Values[1].Should().BeApproximately(200, 0.01);
        rt.Series[0].Values[2].Should().BeApproximately(150, 0.01);
    }

    [Fact]
    public void RoundTrip_Chart_CategoriesPreserved()
    {
        var chart = BuildColumnChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Categories.Should().Equal(new[] { "Q1", "Q2", "Q3" });
    }

    [Fact]
    public void RoundTrip_Chart_TitlePreserved()
    {
        var chart = BuildColumnChart();
        chart.Title = "Quarterly Performance";
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.Title.Should().Be("Quarterly Performance");
    }

    [Fact]
    public void RoundTrip_PieChart_TypePreserved()
    {
        var chart = BuildPieChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().Be(ChartType.Pie);
    }

    [Fact]
    public void RoundTrip_LineChart_TypePreserved()
    {
        var chart = BuildLineChart();
        var pres = BuildPresWithChart(chart);
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.Chart).Chart!;
        rt.ChartType.Should().BeOneOf([ChartType.Line, ChartType.LineMarkers], "line charts round-trip as line variant");
    }

    [Fact]
    public void RoundTrip_TwoCharts_SameSlide()
    {
        var pres = new Presentation();
        var slide = new Slide();

        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Chart1",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 3000000, ExtentCyEmu = 3000000,
            Chart = BuildColumnChart()
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "Chart2",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 3500000, OffsetYEmu = 0,
            ExtentCxEmu = 3000000, ExtentCyEmu = 3000000,
            Chart = BuildPieChart()
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var charts = reloaded.Slides[0].Shapes
            .Where(s => s.Kind == SlideShapeKind.Chart)
            .ToList();
        charts.Should().HaveCount(2, "both charts survive round-trip");
        charts[0].Chart!.ChartType.Should().Be(ChartType.ColumnClustered);
        charts[1].Chart!.ChartType.Should().Be(ChartType.Pie);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Compositor — chart produces DrawOp.Chart
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compositor_Chart_ProducesChartOp()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        ops.OfType<DrawOp.Chart>().Should().HaveCount(1, "compositor produces one DrawOp.Chart");
    }

    [Fact]
    public void Compositor_Chart_BoundsCorrect()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        // 914400 EMU / 9525 = 96 DIP
        op.BoundsDip.X.Should().BeApproximately(96.0, 0.1);
        op.BoundsDip.Y.Should().BeApproximately(48.0, 0.1);
    }

    [Fact]
    public void Compositor_Chart_SeriesColorsResolved()
    {
        var pres = BuildPresWithChart(BuildColumnChart());
        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op   = ops.OfType<DrawOp.Chart>().First();

        op.SeriesColors.Should().HaveCount(2, "one color per series");
    }

    [Fact]
    public void Compositor_Chart_ChartShapeReference()
    {
        var chart = BuildColumnChart();
        var pres  = BuildPresWithChart(chart);
        var ops   = SlideCompositor.Compose(pres, pres.Slides[0]);
        var op    = ops.OfType<DrawOp.Chart>().First();

        op.ChartShape.Should().BeSameAs(chart, "compositor passes through the model reference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Multiple slides with charts
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TwoSlides_EachWithChart()
    {
        var pres = new Presentation();

        for (int si = 0; si < 2; si++)
        {
            var slide = new Slide();
            slide.Shapes.Add(new SlideShape
            {
                Id = 1, Name = $"Chart_Slide{si}",
                Kind = SlideShapeKind.Chart,
                OffsetXEmu = 914400, OffsetYEmu = 457200,
                ExtentCxEmu = 5486400, ExtentCyEmu = 3657600,
                Chart = BuildColumnChart()
            });
            pres.Slides.Add(slide);
        }

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Kind == SlideShapeKind.Chart);
        reloaded.Slides[1].Shapes.Should().Contain(s => s.Kind == SlideShapeKind.Chart);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ChartShape BuildColumnChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var s1 = new ChartSeries { Name = "Sales" };
        s1.Values.AddRange(new double?[] { 100, 200, 150 });
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Budget" };
        s2.Values.AddRange(new double?[] { 120, 180, 160 });
        chart.Series.Add(s2);

        return chart;
    }

    private static ChartShape BuildPieChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.AddRange(new[] { "Alpha", "Beta", "Gamma" });

        var s = new ChartSeries { Name = "Share" };
        s.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(s);

        return chart;
    }

    private static ChartShape BuildLineChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });

        var s = new ChartSeries { Name = "Trend" };
        s.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(s);

        return chart;
    }

    private static Presentation BuildPresWithChart(ChartShape chart)
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "MyChart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart
        });
        pres.Slides.Add(slide);
        return pres;
    }

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }
}
