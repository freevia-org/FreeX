using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R88 regression coverage for the pie "Value, Percentage" data-label preset: Excel's showVal and
/// showPercent flags are independent, so a pie slice with both enabled must render both figures
/// (e.g. "50, 20%") instead of the percentage silently displacing the value.
/// </summary>
public sealed class R88_ChartDataLabelFormatterPieValuePercentageTests
{
    [Fact]
    public void GetPieLabelFormat_ValueAndPercentageBothEnabled_ComposesBothWithValuesOwnNumberFormat()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number
        };

        var format = ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share");

        // Applied the way OxyPlot applies it: string.Format(format, value, label, fraction).
        string.Format(format, 50.0, "label", 0.2).Should().Be("50.00, 20%");
    }

    [Fact]
    public void GetPieLabelFormat_ValueOnly_UnaffectedByChange()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = false,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number
        };

        var format = ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share");

        string.Format(format, 50.0, "label", 0.2).Should().Be("50.00");
    }

    [Fact]
    public void GetPieLabelFormat_PercentageOnly_UnaffectedByChange()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number
        };

        var format = ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share");

        string.Format(format, 50.0, "label", 0.2).Should().Be("20%");
    }
}
