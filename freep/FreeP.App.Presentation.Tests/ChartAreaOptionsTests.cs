using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartAreaOptionsTests
{
    [Fact]
    public void Planner_UsesWorkingCopyAndSwitchesBetweenChartAndPlotArea()
    {
        var chart = new ChartShape
        {
            ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xF2F2F2))),
            ChartAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x7F7F7F)), 1.25),
        };

        var planner = ChartAreaOptionsPlanner.FromChart(chart);
        planner.SetFillColor("#D9EAD3");
        planner.SetOutlineColor("#548235");
        planner.SetOutlineWidth(2.0);
        var chartOptions = planner.BuildCommitPlan();

        chartOptions.Target.Should().Be(ChartAreaFormattingTarget.ChartArea);
        ((ShapeFill.Solid)chartOptions.Fill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xD9EAD3));
        ((ShapeOutline.Visible)chartOptions.Outline!).WidthPt.Should().Be(2.0);
        chart.ChartAreaFill.Should().BeOfType<ShapeFill.Solid>();
        ((ShapeFill.Solid)chart.ChartAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xF2F2F2));

        planner.SetTarget(ChartAreaFormattingTarget.PlotArea);
        planner.SetFillColor("#FFF2CC");
        planner.SetOutlineColor("");
        planner.SetOutlineWidth(null);
        var plotOptions = planner.BuildCommitPlan();

        plotOptions.Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
        ((ShapeFill.Solid)plotOptions.Fill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xFFF2CC));
        plotOptions.Outline.Should().BeNull();
    }

    [Fact]
    public void Planner_PreservesFillTransparencyAsDrawingMlAlpha()
    {
        var chart = new ChartShape
        {
            ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
        };

        var planner = ChartAreaOptionsPlanner.FromChart(chart);
        planner.SetFillTransparency(40);

        var options = planner.BuildCommitPlan();
        var fill = options.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        fill.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        fill.Color.Alpha.Should().Be(153);

        var reloaded = ChartAreaOptionsPlanner.FromChart(new ChartShape
        {
            ChartAreaFill = fill,
        });
        reloaded.FillTransparencyPercent.Should().BeApproximately(40, 0.5);
    }

    [Fact]
    public void Planner_AuthorsExplicitNoFillAndNoOutlineStates()
    {
        var chart = new ChartShape();
        var planner = ChartAreaOptionsPlanner.FromChart(chart);

        planner.SetNoFill(true);
        planner.SetNoOutline(true);

        var options = planner.BuildCommitPlan();
        options.Fill.Should().BeSameAs(ShapeFill.None.Instance);
        options.Outline.Should().BeSameAs(ShapeOutline.None.Instance);
    }

    [Fact]
    public void SetChartAreaOptions_RoundTripsAndUndoRestoresBothTargets()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape
        {
            ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFFFFFF))),
            PlotAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xEEEEEE))),
        };
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        var options = new ChartAreaOptions(
            ChartAreaFormattingTarget.PlotArea,
            new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79), alpha: 153)),
            new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x17365D)), 1.5));
        bus.Execute(new SetChartAreaOptionsCommand(0, shape.Id, options));

        ((ShapeFill.Solid)chart.PlotAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        ((ShapeOutline.Visible)chart.PlotAreaOutline!).WidthPt.Should().Be(1.5);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        var reopenedFill = ((ShapeFill.Solid)reopened.PlotAreaFill!).Color;
        reopenedFill.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        reopenedFill.Alpha.Should().Be(153);
        ((ShapeOutline.Visible)reopened.PlotAreaOutline!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x17365D));

        bus.Undo();
        ((ShapeFill.Solid)chart.PlotAreaFill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xEEEEEE));
        chart.PlotAreaOutline.Should().BeNull();
    }

    [Fact]
    public void SetChartAreaOptions_RoundTripsExplicitNoFillAndNoOutline()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape
        {
            ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFFFFFF))),
            ChartAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x000000)), 1),
        };
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetChartAreaOptionsCommand(
            0,
            shape.Id,
            new ChartAreaOptions(
                ChartAreaFormattingTarget.ChartArea,
                ShapeFill.None.Instance,
                ShapeOutline.None.Instance)));

        chart.ChartAreaFill.Should().BeSameAs(ShapeFill.None.Instance);
        chart.ChartAreaOutline.Should().BeSameAs(ShapeOutline.None.Instance);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        reopened.ChartAreaFill.Should().BeOfType<ShapeFill.None>();
        reopened.ChartAreaOutline.Should().BeOfType<ShapeOutline.None>();

        bus.Undo();
        chart.ChartAreaFill.Should().BeOfType<ShapeFill.Solid>();
        chart.ChartAreaOutline.Should().BeOfType<ShapeOutline.Visible>();
    }

    [Fact]
    public void BuildScenePlan_CarriesChartAndPlotAreaPaintToSharedRendererPlan()
    {
        var chart = new ChartShape
        {
            ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xF2F2F2))),
            ChartAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x7F7F7F)), 1),
            PlotAreaFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xEAF2F8))),
            PlotAreaOutline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)), 0.75),
        };
        var chartFill = new ChartFillPlan(new SrgbColor(0xF2, 0xF2, 0xF2), 255);
        var chartOutline = new ChartStrokePlan(new SrgbColor(0x7F, 0x7F, 0x7F), 255, 1);
        var plotFill = new ChartFillPlan(new SrgbColor(0xEA, 0xF2, 0xF8), 255);
        var plotOutline = new ChartStrokePlan(new SrgbColor(0x1F, 0x4E, 0x79), 255, 0.75);
        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 400, 300),
            null,
            null,
            chartFill,
            chartOutline,
            plotFill,
            plotOutline);

        scene.ChartAreaFill.Should().Be(chartFill);
        scene.ChartAreaOutline.Should().Be(chartOutline);
        scene.PlotAreaFill.Should().Be(plotFill);
        scene.PlotAreaOutline.Should().Be(plotOutline);
        scene.Frame.Plot.HasPositiveArea.Should().BeTrue();
    }
}
