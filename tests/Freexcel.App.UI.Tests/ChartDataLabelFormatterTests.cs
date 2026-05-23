using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

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
    public void GetPieLabelFormat_UsesSeriesCategoryAndPercentagePlaceholder()
    {
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be($"Share{Environment.NewLine}{{1}}{Environment.NewLine}{{2:0%}}");
    }
}
