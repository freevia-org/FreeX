using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartDataLabelTextPlannerTests
{
    [Fact]
    public void FormatDataLabel_CombinesSeriesCategoryAndFormattedValue()
    {
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        ChartDataLabelTextPlanner.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Sales; Q1; $1,234.50");
    }

    [Fact]
    public void FormatDataLabel_UsesInvariantNumberCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        var chart = new ChartModel { DataLabelNumberFormat = ChartDataLabelNumberFormat.Number };

        ChartDataLabelTextPlanner.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("1234.50");
    }

    [Fact]
    public void FormatDataLabel_OmitsValueWhenShowValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon
        };

        ChartDataLabelTextPlanner.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Sales; Q1");
    }

    [Fact]
    public void FormatDataLabel_OmitsValueLeavingSingleCategoryName()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true
        };

        ChartDataLabelTextPlanner.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Q1");
    }

    [Fact]
    public void FormatDataLabel_FallsBackToValueWhenNoContentEnabled()
    {
        var chart = new ChartModel { ShowDataLabelValue = false };

        ChartDataLabelTextPlanner.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("1234.5");
    }

    [Fact]
    public void FormatPieDataLabel_CombinesSeriesCategoryValueAndPercentage()
    {
        // ShowDataLabelValue defaults to true, and Excel independently toggles ShowDataLabelPercentage
        // (both showVal and showPercent can be set at once, e.g. the "Value, Percentage" preset), so
        // both figures must appear - value before percentage, per Excel's fixed data-label order.
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "Share", "Q1", 1234.5, 0.42)
            .Should().Be($"Share{Environment.NewLine}Q1{Environment.NewLine}1234.5{Environment.NewLine}42%");
    }

    [Fact]
    public void FormatPieDataLabel_ShowsValueThenPercentageWhenBothEnabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.Space,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.General
        };

        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "Share", "Q1", 42, 0.35)
            .Should().Be("42 35%");
    }

    [Fact]
    public void FormatPieDataLabel_ShowsOnlyPercentageWhenValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = true
        };

        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "Share", "Q1", 42, 0.35)
            .Should().Be("35%");
    }

    [Fact]
    public void FormatAxisValue_UsesInvariantNumberCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        ChartDataLabelTextPlanner.FormatAxisValue(ChartDataLabelNumberFormat.Number, 1234.5)
            .Should().Be("1234.50");
        ChartDataLabelTextPlanner.FormatAxisValue(ChartDataLabelNumberFormat.Currency, 1234.5)
            .Should().Be("$1,234.50");
        ChartDataLabelTextPlanner.FormatAxisValue(ChartDataLabelNumberFormat.Percent, 0.375)
            .Should().Be("38%");
    }

    [Fact]
    public void RendererNeutralDataLabelFormatting_LivesInPresentationPlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var layoutEngine = File.ReadAllText(Path.Combine(presentationRoot, "Charts", "ChartLayoutEngine.cs"));
        var wpfAxes = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.UI", "ChartRenderer.Axes.cs"));
        var wpfFormatter = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.UI", "ChartDataLabelFormatter.cs"));
        var printOverlays = File.ReadAllText(Path.Combine(presentationRoot, "PageLayout", "PrintChartTextOverlayPlanner.cs"));

        layoutEngine.Should().Contain("ChartDataLabelTextPlanner.FormatPieDataLabel");
        layoutEngine.Should().Contain("ChartDataLabelTextPlanner.FormatDataLabel");
        layoutEngine.Should().Contain("ChartDataLabelTextPlanner.FormatAxisValue");
        layoutEngine.Should().NotContain("private static string BuildCartesianLabel");
        layoutEngine.Should().NotContain("private static string BuildPieLabel");
        layoutEngine.Should().NotContain("private static string FormatLabelValue");
        layoutEngine.Should().NotContain("private static string SeparatorText");
        layoutEngine.Should().NotContain("internal static string FormatAxisValue");

        wpfAxes.Should().Contain("ChartDataLabelTextPlanner.FormatAxisValue");
        wpfAxes.Should().NotContain("private static string FormatAxisValue");

        wpfFormatter.Should().Contain("ChartDataLabelTextPlanner.FormatDataLabel");
        wpfFormatter.Should().Contain("ChartDataLabelTextPlanner.FormatLabelValue");
        wpfFormatter.Should().NotContain("return (hasSeriesName, hasCategoryName, hasValue) switch");

        printOverlays.Should().Contain("ChartDataLabelTextPlanner.FormatDataLabel");
        printOverlays.Should().Contain("ChartDataLabelTextPlanner.FormatAxisValue");
        printOverlays.Should().NotContain("FormatPrintedChartAxisValue");
    }
}
