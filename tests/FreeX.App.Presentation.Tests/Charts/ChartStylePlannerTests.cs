using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartStylePlannerTests
{
    [Fact]
    public void StyleOptions_ExposeAutomaticAndNumberedResourceDescriptors()
    {
        var options = ChartStylePlanner.GetStyleOptions();

        options.Should().HaveCount(49);
        options[0].Should().Be(new ChartStyleGalleryOptionDescriptor(
            null,
            "ChartStyle_AutomaticOption",
            "ChartStyle_AutomaticPreview"));
        options.Skip(1).Select(option => option.StyleId).Should().Equal(Enumerable.Range(1, 48).Cast<int?>());
        options.Skip(1).Should().OnlyContain(option =>
            option.DisplayNameResourceKey == "ChartStyle_NumberedOption" &&
            option.PreviewLabelResourceKey == "ChartStyle_NumberedPreview" &&
            option.ResourceValue == option.StyleId);
    }

    [Fact]
    public void StyleResultParsingAndLookup_AreSharedWithRenderers()
    {
        ChartStylePlanner.CreateResult(0).Should().Be(new ChartStyleInput(1));
        ChartStylePlanner.CreateResult(99).Should().Be(new ChartStyleInput(48));
        ChartStylePlanner.CreateResult(null).Should().Be(new ChartStyleInput(null));
        ChartStylePlanner.ParseStyleId(" 14 ").Should().Be(14);
        ChartStylePlanner.ParseStyleId("not a style").Should().BeNull();
        ChartStylePlanner.FindStyleOptionIndex(null).Should().Be(0);
        ChartStylePlanner.FindStyleOptionIndex(48).Should().Be(48);
        ChartStylePlanner.GetStyleOption(999).StyleId.Should().Be(48);
        ChartStylePlanner.NextStyleId(null).Should().Be(4);
        ChartStylePlanner.NextStyleId(44).Should().Be(48);
        ChartStylePlanner.NextStyleId(45).Should().Be(1);
    }

    [Fact]
    public void BuildExcelSeriesPalette_UsesOfficeAccentTintRounds()
    {
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(WorkbookTheme.Office);

        palette.Should().HaveCount(30);
        palette[0].Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0));
        palette[5].Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent6, 0));
        palette[6].Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4));
        palette[12].Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, -0.25));
    }

    [Fact]
    public void FindSeriesFormat_UsesLastMatchingFormat()
    {
        var first = new ChartSeriesFormat(2, FillColor: new CellColor(1, 2, 3));
        var latest = new ChartSeriesFormat(2, FillColor: new CellColor(9, 8, 7));
        var chart = new ChartModel
        {
            SeriesFormats = [first, new ChartSeriesFormat(1), latest]
        };

        ChartStylePlanner.FindSeriesFormat(chart, 2).Should().BeSameAs(latest);
    }

    [Fact]
    public void ResolvePointFillColor_UsesLastMatchingFormat()
    {
        var chart = new ChartModel
        {
            PointFillColors =
            [
                new ChartPointFillFormat(0, 1, FillColor: new CellColor(1, 2, 3)),
                new ChartPointFillFormat(0, 1, FillColor: new CellColor(4, 5, 6)),
            ]
        };

        ChartStylePlanner.ResolvePointFillColor(chart, 0, 1, WorkbookTheme.Office)
            .Should().Be(new CellColor(4, 5, 6));
    }

    [Fact]
    public void ResolveSeriesPaint_UsesFillStrokeThenPaletteFallbacks()
    {
        var chart = new ChartModel
        {
            SeriesFormats =
            [
                new ChartSeriesFormat(1, FillColor: new CellColor(10, 20, 30)),
                new ChartSeriesFormat(2, StrokeColor: new CellColor(40, 50, 60)),
            ]
        };
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(WorkbookTheme.Office);

        ChartStylePlanner.ResolveSeriesPaint(chart, 0, WorkbookTheme.Office, palette)
            .Should().Be(new ChartSeriesPaint(palette[0], palette[0]));
        ChartStylePlanner.ResolveSeriesPaint(chart, 1, WorkbookTheme.Office, palette)
            .Should().Be(new ChartSeriesPaint(new CellColor(10, 20, 30), new CellColor(10, 20, 30)));
        ChartStylePlanner.ResolveSeriesPaint(chart, 2, WorkbookTheme.Office, palette)
            .Should().Be(new ChartSeriesPaint(new CellColor(40, 50, 60), new CellColor(40, 50, 60)));
    }

    [Fact]
    public void ResolveBarPaint_CentralizesNoFillNoLineAndOutlineRules()
    {
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(WorkbookTheme.Office);
        var chart = new ChartModel
        {
            SeriesFormats =
            [
                new ChartSeriesFormat(1, NoFill: true),
                new ChartSeriesFormat(2, NoLine: true, FillColor: new CellColor(20, 30, 40)),
                new ChartSeriesFormat(3, FillColor: new CellColor(50, 60, 70)),
                new ChartSeriesFormat(4, StrokeThickness: 2.5),
            ]
        };

        ChartStylePlanner.ResolveBarPaint(chart, 0, WorkbookTheme.Office, palette)
            .Should().Be(new ChartBarPaint(palette[0], palette[0], 0.75));
        ChartStylePlanner.ResolveBarPaint(chart, 1, WorkbookTheme.Office, palette).HasFill
            .Should().BeFalse("NoFill makes the bar body transparent");
        ChartStylePlanner.ResolveBarPaint(chart, 2, WorkbookTheme.Office, palette)
            .Should().Be(new ChartBarPaint(new CellColor(20, 30, 40), null, 0));
        ChartStylePlanner.ResolveBarPaint(chart, 3, WorkbookTheme.Office, palette)
            .Should().Be(new ChartBarPaint(new CellColor(50, 60, 70), null, 0));
        ChartStylePlanner.ResolveBarPaint(chart, 4, WorkbookTheme.Office, palette)
            .Should().Be(new ChartBarPaint(palette[4], palette[4], 2.5));
    }

    [Fact]
    public void DesktopChartRenderers_DelegateStyleDecisionsToSharedPlanner()
    {
        var root = FindRepositoryRoot();

        var wpfRenderer = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.UI", "ChartRenderer.cs"));
        wpfRenderer.Should().Contain("ChartStylePlanner.BuildExcelSeriesPalette");

        var wpfSeriesFormatting = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.UI", "ChartRenderer.SeriesFormatting.cs"));
        wpfSeriesFormatting.Should().Contain("ChartStylePlanner.FindSeriesFormat");
        wpfSeriesFormatting.Should().Contain("ChartStylePlanner.ResolvePointFillColor");

        var avaloniaRenderer = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "Charts", "AvaloniaChartRenderer.cs"));
        avaloniaRenderer.Should().Contain("ChartStylePlanner.BuildExcelSeriesPalette");
        avaloniaRenderer.Should().Contain("ChartStylePlanner.ResolveSeriesPaint");
        avaloniaRenderer.Should().Contain("ChartStylePlanner.ResolveBarPaint");
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
