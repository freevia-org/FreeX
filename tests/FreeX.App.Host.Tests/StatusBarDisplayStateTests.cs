using System.Windows;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarDisplayStateTests
{
    [Fact]
    public void Ready_HidesStatsAndShowsReadyText()
    {
        var state = StatusBarDisplayState.Ready("Ready");

        state.ReadyVisibility.Should().Be(Visibility.Visible);
        state.StatsVisibility.Should().Be(Visibility.Collapsed);
        state.ReadyText.Should().Be("Ready");
        state.CountText.Should().BeEmpty();
    }

    [Fact]
    public void Stats_FormatsVisibleAggregateText()
    {
        var stats = new StatusBarCalculator.Stats(
            Count: 4,
            NumericalCount: 3,
            Sum: 12,
            Average: 4,
            Min: 2,
            Max: 6);

        var state = StatusBarDisplayState.Stats(stats);

        state.ReadyVisibility.Should().Be(Visibility.Collapsed);
        state.StatsVisibility.Should().Be(Visibility.Visible);
        state.AverageText.Should().Be("Average: 4");
        state.CountText.Should().Be("Count: 4");
        state.NumericalCountText.Should().Be("Numerical Count: 3");
        state.SumText.Should().Be("Sum: 12");
        state.MinText.Should().Be("Min: 2");
        state.MaxText.Should().Be("Max: 6");
    }
}
