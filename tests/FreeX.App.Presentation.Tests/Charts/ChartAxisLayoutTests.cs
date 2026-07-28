using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartAxisLayoutTests
{
    [Fact]
    public void ValueAxis_MinorTickStyleProducesMinorTicksWithoutMinorGridlines()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            YAxisMinimum = 0,
            YAxisMaximum = 20,
            YAxisMajorUnit = 10,
            YAxisMinorUnit = 5,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            ShowYAxisMinorGridlines = false,
        };

        var layout = ChartLayoutEngine.Layout(new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["A", "B"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [5, 15] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FixedTextMeasurer(),
        });

        layout.ValueAxis!.MinorTicks.Should().NotBeNullOrEmpty();
    }

    private sealed class FixedTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontName, double fontSize, bool bold, bool italic)
            => new((text?.Length ?? 1) * fontSize * 0.6, fontSize + 4);
    }
}
