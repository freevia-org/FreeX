using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarDisplayModelBuilderTests
{
    private sealed class TestTextProvider : IStatusBarTextProvider
    {
        public string GetReadyText() => "Ready";

        public string GetReadoutFormat(StatusBarReadoutKind kind) => GetReadoutLabel(kind) + ": {0}";

        public string GetReadoutLabel(StatusBarReadoutKind kind) => kind switch
        {
            StatusBarReadoutKind.Average => "Average",
            StatusBarReadoutKind.Count => "Count",
            StatusBarReadoutKind.NumericalCount => "Numerical Count",
            StatusBarReadoutKind.Sum => "Sum",
            StatusBarReadoutKind.Minimum => "Min",
            StatusBarReadoutKind.Maximum => "Max",
            _ => kind.ToString()
        };
    }

    private static readonly TestTextProvider Text = new();

    [Fact]
    public void Ready_HidesStatsAndShowsReadyText()
    {
        var model = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.PageLayout, zoomPercent: 120, "Ready");

        model.IsReadyVisible.Should().BeTrue();
        model.AreStatsVisible.Should().BeFalse();
        model.ReadyText.Should().Be("Ready");
        model.Readouts.Should().BeEmpty();
        model.ViewMode.Should().Be(StatusBarViewMode.PageLayout);
        model.ZoomPercent.Should().Be(120);
    }

    [Fact]
    public void Stats_FormatsVisibleAggregateReadoutItems()
    {
        var stats = new WorkbookSelectionStats(
            Sum: 12,
            Count: 4,
            NumericalCount: 3,
            Average: 4,
            Min: 2,
            Max: 6);

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.Normal, zoomPercent: 100, stats, Text);

        model.IsReadyVisible.Should().BeFalse();
        model.AreStatsVisible.Should().BeTrue();
        model.ReadyText.Should().BeEmpty();
        model.FindReadout(StatusBarReadoutKind.Average)!.Value.Value.Should().Be("Average: 4");
        model.FindReadout(StatusBarReadoutKind.Count)!.Value.Value.Should().Be("Count: 4");
        model.FindReadout(StatusBarReadoutKind.NumericalCount)!.Value.Value.Should().Be("Numerical Count: 3");
        model.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: 12");
        model.FindReadout(StatusBarReadoutKind.Minimum)!.Value.Value.Should().Be("Min: 2");
        model.FindReadout(StatusBarReadoutKind.Maximum)!.Value.Value.Should().Be("Max: 6");

        foreach (var item in model.Readouts)
            item.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Stats_TextOnlySelectionShowsCountsButHidesNumericReadouts()
    {
        var stats = new WorkbookSelectionStats(
            Sum: 0,
            Count: 3,
            NumericalCount: 0,
            Average: null,
            Min: null,
            Max: null);

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.Normal, zoomPercent: 100, stats, Text);

        model.FindReadout(StatusBarReadoutKind.Count)!.Value.IsVisible.Should().BeTrue();
        model.FindReadout(StatusBarReadoutKind.NumericalCount)!.Value.IsVisible.Should().BeTrue();
        model.FindReadout(StatusBarReadoutKind.NumericalCount)!.Value.Value.Should().Be("Numerical Count: 0");

        model.FindReadout(StatusBarReadoutKind.Average)!.Value.IsVisible.Should().BeFalse();
        model.FindReadout(StatusBarReadoutKind.Average)!.Value.Value.Should().BeEmpty();
        model.FindReadout(StatusBarReadoutKind.Sum)!.Value.IsVisible.Should().BeFalse();
        model.FindReadout(StatusBarReadoutKind.Minimum)!.Value.IsVisible.Should().BeFalse();
        model.FindReadout(StatusBarReadoutKind.Maximum)!.Value.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Stats_ReusesFirstFormattedNumberWhenAggregatesAreEqual()
    {
        // Single numeric cell: Sum == Average == Min == Max == 42; the builder reuses
        // the first formatted number rather than reformatting each aggregate.
        var stats = new WorkbookSelectionStats(42, 1, 1, 42, 42, 42);

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.Normal, zoomPercent: 100, stats, Text);

        model.FindReadout(StatusBarReadoutKind.Average)!.Value.Value.Should().Be("Average: 42");
        model.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: 42");
        model.FindReadout(StatusBarReadoutKind.Minimum)!.Value.Value.Should().Be("Min: 42");
        model.FindReadout(StatusBarReadoutKind.Maximum)!.Value.Value.Should().Be("Max: 42");
    }

    [Fact]
    public void Stats_AggregateErrorPropagatesToAverageSumMinMaxButNotCounts()
    {
        // R67 backlog (status-bar-6-2): an error cell in the selection must show up in the
        // Average/Sum/Min/Max readouts instead of being silently excluded from the numbers,
        // matching Excel's own SUM/AVERAGE/MIN/MAX error propagation. Count/Numerical Count are
        // unaffected -- Excel keeps counting normally.
        var stats = new WorkbookSelectionStats(
            Sum: 30,
            Count: 3,
            NumericalCount: 2,
            Average: 15,
            Min: 10,
            Max: 20,
            AggregateErrorCode: "#DIV/0!");

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.Normal, zoomPercent: 100, stats, Text);

        model.FindReadout(StatusBarReadoutKind.Average)!.Value.Value.Should().Be("Average: #DIV/0!");
        model.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: #DIV/0!");
        model.FindReadout(StatusBarReadoutKind.Minimum)!.Value.Value.Should().Be("Min: #DIV/0!");
        model.FindReadout(StatusBarReadoutKind.Maximum)!.Value.Value.Should().Be("Max: #DIV/0!");
        model.FindReadout(StatusBarReadoutKind.Count)!.Value.Value.Should().Be("Count: 3");
        model.FindReadout(StatusBarReadoutKind.NumericalCount)!.Value.Value.Should().Be("Numerical Count: 2");

        foreach (var item in model.Readouts)
            item.IsVisible.Should().BeTrue();
    }

    [Theory]
    [InlineData(12.5, "12.5")]
    [InlineData(12.0000000001, "12")]
    [InlineData(123456789.1234, "123456789.1")]
    public void FormatNumber_UsesCompactExcelLikeStatusText(double value, string expected)
    {
        StatusBarDisplayModelBuilder.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void FormatNumber_DoesNotCorruptLargeNonIntegerTotalsRoundedByFlatG10()
    {
        // freex-status-aggregates F1: a hardcoded "G10" doesn't just trim decimal precision --
        // once the integer part itself needs 10+ significant digits, G10 rounds the whole
        // number, so 1200000000.6 (true Sum of 400000000.1 + 400000000.2 + 400000000.3) used to
        // come out as the plain integer "1200000001": the .6 vanished AND the integer part
        // itself changed (1200000000 -> 1200000001), a 0.4 discrepancy presented as exact.
        var sum = 400000000.1 + 400000000.2 + 400000000.3;
        sum.Should().Be(1200000000.6);

        StatusBarDisplayModelBuilder.FormatNumber(sum).Should().Be("1200000000.6");
    }

    [Fact]
    public void Stats_SumReadoutShowsTrueLargeNonIntegerTotal()
    {
        // Same defect, exercised through the actual Stats() aggregate path (Sum/Average/Min/Max)
        // that the status bar renders, not just the FormatNumber helper directly.
        var stats = new WorkbookSelectionStats(
            Sum: 1200000000.6,
            Count: 3,
            NumericalCount: 3,
            Average: 400000000.2,
            Min: 400000000.1,
            Max: 400000000.3);

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.Normal, zoomPercent: 100, stats, Text);

        model.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: 1200000000.6");
    }

    [Fact]
    public void Stats_CarriesViewModeAndZoomThrough()
    {
        var stats = new WorkbookSelectionStats(12, 4, 3, 4, 2, 6);

        var model = StatusBarDisplayModelBuilder.Stats(StatusBarViewMode.PageBreak, zoomPercent: 75, stats, Text);

        model.ViewMode.Should().Be(StatusBarViewMode.PageBreak);
        model.ZoomPercent.Should().Be(75);
    }
}
