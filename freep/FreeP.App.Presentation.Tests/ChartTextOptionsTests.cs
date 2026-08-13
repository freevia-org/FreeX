using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartTextOptionsTests
{
    [Fact]
    public void Planner_UsesWorkingCopyAndBuildsAutomaticOrExplicitValues()
    {
        var chart = new ChartShape
        {
            TextStyle = new ChartTextStyle
            {
                FontFamily = "Aptos",
                FontSizePt = 11,
                Bold = true,
                Italic = false,
                Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
            },
        };

        var planner = ChartTextOptionsPlanner.FromChart(chart);
        planner.SetFontFamily("Calibri");
        planner.SetFontSizePt(14);
        planner.SetBold(false);
        planner.SetItalic(true);
        planner.SetColor("#C00000");

        var options = planner.BuildCommitPlan();
        options.FontFamily.Should().Be("Calibri");
        options.FontSizePt.Should().Be(14);
        options.Bold.Should().BeFalse();
        options.Italic.Should().BeTrue();
        options.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        chart.TextStyle!.FontFamily.Should().Be("Aptos");
        chart.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("14.5", 14.5)]
    public void ParseOptionalFontSize_MapsBlankAndParsesCurrentCulture(string text, double? expected)
    {
        ChartTextOptionsDialogSession.ParseOptionalFontSize(text, CultureInfo.CurrentCulture)
            .Should().Be(expected);
    }

    [Fact]
    public void SetChartTextOptions_RoundTripsAndUndoRestoresInheritedText()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape();
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        var options = new ChartTextOptions(
            "Calibri", 14, false, true,
            new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)));
        bus.Execute(new SetChartTextOptionsCommand(0, shape.Id, options));

        chart.TextStyle.Should().NotBeNull();
        chart.TextStyle!.IsImplicitDefault.Should().BeFalse();
        chart.TextStyle.FontFamily.Should().Be("Calibri");
        chart.TextStyle.FontSizePt.Should().Be(14);
        chart.TextStyle.Bold.Should().BeFalse();
        chart.TextStyle.Italic.Should().BeTrue();
        chart.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.TextStyle.Should().NotBeNull();
        roundTripped.TextStyle!.FontFamily.Should().Be("Calibri");
        roundTripped.TextStyle.FontSizePt.Should().Be(14);
        roundTripped.TextStyle.Bold.Should().BeFalse();
        roundTripped.TextStyle.Italic.Should().BeTrue();
        roundTripped.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));

        bus.Undo();
        chart.TextStyle.Should().BeNull();
    }

    [Fact]
    public void SetChartTextOptions_GroupedChart_UpdatesAndUndoRestores()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape();
        var chartShape = new SlideShape { Id = 11, Kind = SlideShapeKind.Chart, Chart = chart };
        var group = new SlideShape { Id = 12, Kind = SlideShapeKind.Group };
        group.Children.Add(chartShape);
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetChartTextOptionsCommand(
            0,
            chartShape.Id,
            new ChartTextOptions("Calibri", 14, true, false, null)));

        chart.TextStyle.Should().NotBeNull();
        chart.TextStyle!.FontFamily.Should().Be("Calibri");
        bus.Undo();
        chart.TextStyle.Should().BeNull();
    }

    [Fact]
    public void SetChartTextOptions_BlankValuesClearAuthoredStyle()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape
        {
            TextStyle = new ChartTextStyle { FontFamily = "Aptos", FontSizePt = 12 },
        };
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetChartTextOptionsCommand(0, shape.Id,
            new ChartTextOptions(null, null, null, null, null)));

        chart.TextStyle.Should().BeNull("blank values restore inherited chart defaults");
    }

    [Fact]
    public void SetChartTextOptions_TitleTarget_RoundTripsAndUndoRestores()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { Title = "Revenue" };
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        var options = new ChartTextOptions(
            "Calibri", 16, true, true,
            new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
            ChartTextTarget.Title);
        bus.Execute(new SetChartTextOptionsCommand(0, shape.Id, options));

        chart.TitleStyle.Should().NotBeNull();
        chart.TitleStyle!.FontFamily.Should().Be("Calibri");
        chart.TextStyle.Should().BeNull();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.TitleStyle.Should().NotBeNull();
        roundTripped.TitleStyle!.FontSizePt.Should().Be(16);
        roundTripped.TitleStyle.Bold.Should().BeTrue();
        roundTripped.TitleStyle.Italic.Should().BeTrue();
        roundTripped.TitleStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));

        var titlePlan = ChartRenderPlanner.BuildScenePlan(
            roundTripped,
            new ChartPlanRect(0, 0, 320, 220)).Title!.Value;
        titlePlan.FontFamily.Should().Be("Calibri");
        titlePlan.FontSize.Should().Be(16);
        titlePlan.IsBold.Should().BeTrue();
        titlePlan.IsItalic.Should().BeTrue();
        titlePlan.TextColor.Should().Be(SrgbColor.FromRgb(0xC00000));

        bus.Undo();
        chart.TitleStyle.Should().BeNull();
    }

    [Fact]
    public void SetChartTextOptions_LegendTarget_RoundTripsAndPlansLegendText()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { Legend = LegendPosition.Right };
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Categories.AddRange(["Q1", "Q2"]);
        chart.Series.Add(series);
        var shape = new SlideShape { Id = 1, Kind = SlideShapeKind.Chart, Chart = chart };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetChartTextOptionsCommand(
            0,
            shape.Id,
            new ChartTextOptions(
                "Calibri", 13, true, true,
                new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
                ChartTextTarget.Legend)));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Chart!;
        roundTripped.LegendTextStyle.Should().NotBeNull();
        roundTripped.LegendTextStyle!.FontSizePt.Should().Be(13);
        roundTripped.LegendTextStyle.Bold.Should().BeTrue();

        var legendText = ChartRenderPlanner.BuildScenePlan(
            roundTripped,
            new ChartPlanRect(0, 0, 320, 220)).LegendItems.Single().Label;
        legendText.FontFamily.Should().Be("Calibri");
        legendText.FontSize.Should().Be(13);
        legendText.IsBold.Should().BeTrue();
        legendText.IsItalic.Should().BeTrue();
        legendText.TextColor.Should().Be(SrgbColor.FromRgb(0x1F4E79));

        bus.Undo();
        chart.LegendTextStyle.Should().BeNull();
    }
}
