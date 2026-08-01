using FluentAssertions;
using FreeX.App.Presentation.FillSeries;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Tests for R112-app-host-fillseries-trend-unreachable (MED): the WPF Fill ▸ Series dialog
/// (<see cref="FillSeriesStepDialog"/>) built rows for SeriesIn/Type/DateUnit/Step/Stop but never
/// offered Excel's Fill ▸ Series "Trend" checkbox at all, and its only paths to a result
/// (<c>TryCreateResult</c> and the <c>Accept()</c> handler) only ever called
/// <c>FillSeriesPlanner.TryCreateOptions</c> overloads that had no way to set
/// <see cref="FillSeriesOptions.Trend"/> -- so even though the planner fully implements Trend mode
/// (see R81_FillSeriesTrendTests in FreeX.App.Presentation.Tests), a user could never reach it from
/// this dialog. These tests exercise the new Trend checkbox and its threading into
/// <c>FillSeriesStepDialog.TryCreateResult</c>.
/// </summary>
public sealed partial class RemainingDialogTests
{
    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_TrendTrue_ThreadsTrendFlagIntoResult()
    {
        // This is the exact reachability gap: before this fix there was no overload of
        // TryCreateResult that could accept a Trend flag from dialog input at all.
        FillSeriesStepDialog.TryCreateResult(
                FillSeriesDirection.Columns,
                FillSeriesType.Linear,
                FillSeriesDateUnit.Day,
                "1",
                null,
                trend: true,
                out var result,
                out _,
                out _)
            .Should().BeTrue();

        result.Trend.Should().BeTrue();
    }

    // No-regression sibling: the pre-existing no-trend overload (still used by the single-value
    // ribbon "Fill Down/Right series" shortcut path) must keep defaulting Trend to false.
    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_WithoutTrendArgument_StillDefaultsTrendFalse()
    {
        FillSeriesStepDialog.TryCreateResult(
                FillSeriesDirection.Columns,
                FillSeriesType.Linear,
                FillSeriesDateUnit.Day,
                "1",
                null,
                out var result,
                out _,
                out _)
            .Should().BeTrue();

        result.Trend.Should().BeFalse();
    }

    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_TrendTrue_StillValidatesStepAndStop()
    {
        FillSeriesStepDialog.TryCreateResult(
                FillSeriesDirection.Columns,
                FillSeriesType.Linear,
                FillSeriesDateUnit.Day,
                "1",
                "not-a-number",
                trend: true,
                out _,
                out var error,
                out _)
            .Should().BeFalse();

        error.Should().Contain("stop");
    }

    [Fact]
    public void FillSeriesStepDialog_HasATrendCheckBoxWiredToSelectedSeriesType()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("private readonly CheckBox _trendBox = new() { Content = UiText.Get(\"FillSeriesStep_Trend\") };");
        source.Should().Contain("private void UpdateTrendAvailability()");
        source.Should().Contain("FillSeriesPlanner.IsTrendEnabled(SelectedSeriesType())");
        source.Should().Contain("_linearButton.Checked += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("_growthButton.Checked += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("_dateButton.Checked += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("_autoFillButton.Checked += (_, _) => UpdateTrendAvailability();");
    }

    [Fact]
    public void FillSeriesStepDialog_TrendCheckedDisablesStepValueBox()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        // Excel disables the Step value box while Trend is checked (Step plays no part in Trend mode).
        source.Should().Contain("_stepBox.IsEnabled = !(isTrendEligible && _trendBox.IsChecked == true);");
    }

    [Fact]
    public void FillSeriesStepDialogAccept_PassesTheTrendCheckBoxStateIntoTryCreateResult()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("_trendBox.IsEnabled && _trendBox.IsChecked == true,");
    }
}
