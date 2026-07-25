using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R88 regression coverage for the pie "Value, Percentage" data-label preset: Excel's showVal and
/// showPercent flags are independent, so a pie slice with both enabled must render the raw value
/// (formatted with its own number format) followed by the percentage, e.g. "50, 20%" for a slice
/// worth 50 out of a total of 250 - not the value re-formatted as a percentage (e.g. "5000%").
/// </summary>
public sealed class R88_ChartDataLabelTextPlannerPieValuePercentageTests
{
    [Fact]
    public void FormatPieDataLabel_ValueAndPercentageBothEnabled_OnPieChart_RendersValueThenPercentage()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = true
        };

        // Slice value 50 of a 250 total => 20%.
        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "", "", 50, 0.2)
            .Should().Be("50, 20%");
    }

    [Fact]
    public void FormatPieDataLabel_ValueOnly_OnPieChart_UnaffectedByChange()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = false
        };

        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "", "", 50, 0.2)
            .Should().Be("50");
    }

    [Fact]
    public void FormatPieDataLabel_PercentageOnly_OnPieChart_UnaffectedByChange()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = true
        };

        ChartDataLabelTextPlanner.FormatPieDataLabel(chart, "", "", 50, 0.2)
            .Should().Be("20%");
    }
}
