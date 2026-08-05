using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionDialogSessionTests
{
    [Fact]
    public void AreaSession_ProjectsTargetsAndCommitsCultureAwareFormatting()
    {
        var chart = new ChartShape
        {
            ChartAreaFill = SolidFill(0xFFFFFF),
            PlotAreaFill = SolidFill(0xEEEEEE),
        };
        var session = new ChartAreaOptionsDialogSession(CreateEditor(chart));

        session.State.TargetIndex.Should().Be(0);
        session.SelectTarget(1).FillColor.Should().Be("#EEEEEE");

        var result = session.TryCommit(
            new ChartAreaOptionsDialogInput(
                1,
                "#112233",
                "25,5",
                false,
                "#445566",
                false,
                "1,5"),
            CultureInfo.GetCultureInfo("fr-FR"));

        result.Succeeded.Should().BeTrue();
        result.CommitPlan!.Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
        var fill = chart.PlotAreaFill.Should().BeOfType<ShapeFill.Solid>().Subject;
        fill.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x112233));
        fill.Color.Alpha.Should().BeLessThan((byte)255);
        var outline = chart.PlotAreaOutline.Should().BeOfType<ShapeOutline.Visible>().Subject;
        outline.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x445566));
        outline.WidthPt.Should().Be(1.5);
    }

    [Fact]
    public void AreaSession_InvalidNumberReturnsFailureWithoutDispatch()
    {
        var original = SolidFill(0xEEEEEE);
        var chart = new ChartShape { PlotAreaFill = original };
        var session = new ChartAreaOptionsDialogSession(CreateEditor(chart));

        var result = session.TryCommit(
            new ChartAreaOptionsDialogInput(1, "#112233", "invalid", false, "", false, ""),
            CultureInfo.InvariantCulture);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeEmpty();
        chart.PlotAreaFill.Should().BeSameAs(original);
    }

    [Fact]
    public void DataTableSession_ParsesAndDispatchesAllPortableOptions()
    {
        var chart = new ChartShape();
        var session = new ChartDataTableOptionsDialogSession(CreateEditor(chart));

        session.State.ShowDataTable.Should().BeFalse();
        var result = session.TryCommit(
            new ChartDataTableOptionsDialogInput(
                true,
                true,
                false,
                true,
                true,
                "#F2F2F2",
                "#1F4E79",
                "1,5",
                "#112233",
                "9,5",
                "Aptos",
                true,
                false),
            CultureInfo.GetCultureInfo("fr-FR"));

        result.Succeeded.Should().BeTrue();
        chart.DataTable.Should().NotBeNull();
        var dataTable = chart.DataTable!;
        dataTable.ShowHorizontalBorder.Should().BeTrue();
        dataTable.ShowVerticalBorder.Should().BeFalse();
        dataTable.ShowOutlineBorder.Should().BeTrue();
        dataTable.ShowLegendKeys.Should().BeTrue();
        dataTable.BackgroundFill.Should().BeOfType<ShapeFill.Solid>();
        dataTable.BorderOutline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.WidthPt.Should().Be(1.5);
        dataTable.TextStyle!.FontSizePt.Should().Be(9.5);
        dataTable.TextStyle.FontFamily.Should().Be("Aptos");
        dataTable.TextStyle.Bold.Should().BeTrue();
        dataTable.TextStyle.Italic.Should().BeFalse();
    }

    [Fact]
    public void DataTableSession_InvalidColorReturnsFailureWithoutDispatch()
    {
        var chart = new ChartShape();
        var session = new ChartDataTableOptionsDialogSession(CreateEditor(chart));

        var result = session.TryCommit(
            new ChartDataTableOptionsDialogInput(
                true, true, true, true, false,
                "not-a-color", "", "", "", "", "", null, null),
            CultureInfo.InvariantCulture);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeEmpty();
        chart.DataTable.Should().BeNull();
    }

    [Fact]
    public void LayoutSession_PreservesImportedModeAndCommitsSelectedTarget()
    {
        var chart = new ChartShape
        {
            PlotAreaManualLayout = new ChartManualLayout
            {
                LayoutTarget = "vendor-layout",
                XMode = ChartManualLayoutMode.Unsupported,
                RawXModeToken = "vendor-mode",
                X = 0.1,
            },
        };
        var session = new ChartLayoutOptionsDialogSession(CreateEditor(chart));
        var state = session.State;

        state.LayoutTargetOptions[state.LayoutTargetIndex].Value.Should().Be("vendor-layout");
        var preserved = session.BuildCommitPlan(
            new ChartLayoutOptionsDialogInput(
                state.TargetIndex,
                state.LayoutTargetIndex,
                state.XModeIndex,
                state.YModeIndex,
                state.WidthModeIndex,
                state.HeightModeIndex,
                "0.1", "", "", ""),
            CultureInfo.InvariantCulture);
        preserved.XMode.Should().Be(ChartManualLayoutMode.Unsupported);
        preserved.RawXModeToken.Should().Be("vendor-mode");

        var legendState = session.SelectTarget(1);
        var result = session.TryCommit(
            new ChartLayoutOptionsDialogInput(
                1,
                1,
                1,
                legendState.YModeIndex,
                legendState.WidthModeIndex,
                legendState.HeightModeIndex,
                "0,2", "0,3", "0,4", "0,5"),
            CultureInfo.GetCultureInfo("fr-FR"));

        result.Succeeded.Should().BeTrue();
        chart.LegendManualLayout.Should().NotBeNull();
        chart.LegendManualLayout!.LayoutTarget.Should().Be("inner");
        chart.LegendManualLayout.XMode.Should().Be(ChartManualLayoutMode.Edge);
        chart.LegendManualLayout.X.Should().Be(0.2);
        chart.LegendManualLayout.Height.Should().Be(0.5);
    }

    [Fact]
    public void LayoutSession_InvalidNumberReturnsFailureWithoutDispatch()
    {
        var chart = new ChartShape();
        var session = new ChartLayoutOptionsDialogSession(CreateEditor(chart));
        var state = session.State;

        var result = session.TryCommit(
            new ChartLayoutOptionsDialogInput(
                0, 0,
                state.XModeIndex, state.YModeIndex, state.WidthModeIndex, state.HeightModeIndex,
                "NaN", "", "", ""),
            CultureInfo.InvariantCulture);

        result.Succeeded.Should().BeFalse();
        chart.PlotAreaManualLayout.Should().BeNull();
    }

    [Fact]
    public void PieSession_ParsesFiltersAndDispatchesOfPieOptions()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            FirstSliceAngleDegrees = 10,
            DoughnutHolePercent = 50,
        };
        var series = new ChartSeries { Name = "Series" };
        series.Values.AddRange([1.0, 2.0, 3.0, 4.0]);
        chart.Series.Add(series);
        var session = new ChartPieOptionsDialogSession(CreateEditor(chart));

        session.State.IsOfPie.Should().BeTrue();
        var result = session.TryCommit(
            new ChartPieOptionsDialogInput(
                "120",
                "60",
                1,
                1,
                "2,5",
                "125",
                "3, 1, 3, 8",
                "50",
                true),
            CultureInfo.GetCultureInfo("fr-FR"));

        result.Succeeded.Should().BeTrue();
        chart.FirstSliceAngleDegrees.Should().Be(120);
        chart.DoughnutHolePercent.Should().Be(60);
        chart.OfPieType.Should().Be(OfPieType.Bar);
        chart.OfPieSplitType.Should().Be(OfPieSplitType.Custom);
        chart.OfPieSplitPosition.Should().Be(2.5);
        chart.OfPieSecondPieSizePercent.Should().Be(125);
        chart.OfPieCustomPointIndices.Should().Equal(1, 3);
        chart.BarGapWidthPercent.Should().Be(50);
        chart.OfPieSeriesLinesSpecified.Should().BeTrue();
    }

    [Fact]
    public void PieSession_InvalidAngleReturnsFailureWithoutDispatch()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            FirstSliceAngleDegrees = 10,
        };
        var session = new ChartPieOptionsDialogSession(CreateEditor(chart));

        var result = session.TryCommit(
            new ChartPieOptionsDialogInput("400", "50", 0, 0, null, null, null, null, false),
            CultureInfo.InvariantCulture);

        result.Succeeded.Should().BeFalse();
        chart.FirstSliceAngleDegrees.Should().Be(10);
    }

    private static EditingSession CreateEditor(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }

    private static ShapeFill.Solid SolidFill(int rgb) =>
        new(new ThemeAwareColor(SrgbColor.FromRgb(rgb)));
}
