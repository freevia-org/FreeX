using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class ChartDataLabelFormatterTests
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

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Sales; Q1; $1,234.50");
    }

    [Fact]
    public void GetPieLabelFormat_EscapesBracesInSeriesName()
    {
        // The series name is user-controlled and the result is an OxyPlot format string; literal braces
        // must be doubled so OxyPlot renders them verbatim instead of parsing them as placeholders.
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = false,
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = false
        };

        var format = ChartDataLabelFormatter.GetPieLabelFormat(chart, "Region {A}");

        format.Should().Be("Region {{A}}");
        // Formatting it through string.Format (as OxyPlot does) must not throw and must render the braces.
        string.Format(format, 0.0, "label", 0.5).Should().Be("Region {A}");
    }

    [Fact]
    public void FormatDataLabel_UsesInvariantNumberCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");
        var chart = new ChartModel { DataLabelNumberFormat = ChartDataLabelNumberFormat.Number };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
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

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
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

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Q1");
    }

    [Fact]
    public void FormatDataLabel_FallsBackToValueWhenNoContentEnabled()
    {
        var chart = new ChartModel { ShowDataLabelValue = false };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("1234.5");
    }

    [Fact]
    public void GetPieLabelFormat_OmitsValuePlaceholderWhenShowValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be("{1}");
    }

    [Fact]
    public void GetPieLabelFormat_KeepsPercentageWhenValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be($"{{1}}{Environment.NewLine}{{2:0%}}");
    }

    [Fact]
    public void ShouldUseNativeValueLabels_FalseWhenValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true
        };

        ChartDataLabelFormatter.ShouldUseNativeValueLabels(chart).Should().BeFalse();
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart).Should().BeTrue();
    }

    [Fact]
    public void NeutralDataLabelFormatting_DelegatesToPresentationPlanner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartDataLabelFormatter.cs");

        source.Should().Contain("ChartDataLabelTextPlanner.FormatDataLabel");
        source.Should().Contain("ChartDataLabelTextPlanner.FormatLabelValue");
        source.Should().Contain("ChartDataLabelTextPlanner.GetDataLabelSeparatorText");
        source.Should().NotContain("return (hasSeriesName, hasCategoryName, hasValue) switch");
        source.Should().NotContain("value.ToString(\"0.00\"");
    }

    [Fact]
    public void GetNativeValueLabelFormat_ReturnsNullWhenAnnotationLabelsAreRequired()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number
        };

        ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1).Should().BeNull();
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart).Should().BeTrue();
    }

    [Fact]
    public void GetPieLabelFormat_ComposesValueAndPercentageWhenBothEnabled()
    {
        // ShowDataLabelValue defaults to true, and Excel independently toggles ShowDataLabelPercentage
        // (both showVal and showPercent can be set at once, e.g. the "Value, Percentage" preset), so
        // both placeholders must appear - value before percentage, per Excel's fixed data-label order.
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be($"Share{Environment.NewLine}{{1}}{Environment.NewLine}{{0}}{Environment.NewLine}{{2:0%}}");
    }
}
