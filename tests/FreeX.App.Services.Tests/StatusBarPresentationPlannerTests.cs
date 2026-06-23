using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarPresentationPlannerTests
{
    [Fact]
    public void Build_ReadyModelCarriesReadyTextZoomAndVisibility()
    {
        var model = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.PageLayout, zoomPercent: 125, "Ready");
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            PageNumber = true,
            Zoom = false,
            ZoomSlider = true
        };

        var plan = StatusBarPresentationPlanner.Build(
            model,
            options,
            hasPageNumberText: true,
            fallbackAutomationText: "Customize Status Bar");

        plan.ReadyText.Should().Be("Ready");
        plan.ZoomPercent.Should().Be(125);
        plan.VisibleReadoutText.Should().BeEmpty();
        plan.AutomationText.Should().Be("Customize Status Bar");
        plan.AverageText.Should().BeEmpty();
        plan.Visibility.ReadyTextVisible.Should().BeTrue();
        plan.Visibility.PageNumberVisible.Should().BeTrue();
        plan.Visibility.StatsPanelVisible.Should().BeFalse();
        plan.Visibility.ZoomVisible.Should().BeFalse();
        plan.Visibility.ZoomSliderVisible.Should().BeTrue();
        plan.Visibility.ZoomControlsVisible.Should().BeTrue();
    }

    [Fact]
    public void Build_StatsModelCarriesIndividualAndFilteredReadouts()
    {
        var model = StatsModel();
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            NumericalCount = true,
            Sum = false,
            Maximum = true,
            ViewShortcuts = false,
            Zoom = false,
            ZoomSlider = false
        };

        var plan = StatusBarPresentationPlanner.Build(
            model,
            options,
            fallbackAutomationText: "Customize Status Bar");

        plan.AverageText.Should().Be("Average: 20");
        plan.CountText.Should().Be("Count: 4");
        plan.NumericalCountText.Should().Be("Numerical Count: 3");
        plan.SumText.Should().Be("Sum: 60");
        plan.MinimumText.Should().Be("Min: 10");
        plan.MaximumText.Should().Be("Max: 30");
        plan.VisibleReadoutText.Should().Be("Average: 20   Count: 4   Numerical Count: 3   Max: 30");
        plan.AutomationText.Should().Be("Average: 20; Count: 4; Numerical Count: 3; Max: 30");
        plan.Visibility.StatsPanelVisible.Should().BeTrue();
        plan.Visibility.SumVisible.Should().BeFalse();
        plan.Visibility.InteractiveControlsVisible.Should().BeFalse();
    }

    [Fact]
    public void ReadoutValue_ReturnsEmptyTextForMissingReadout()
    {
        var model = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.Normal, zoomPercent: 100, "Ready");

        StatusBarPresentationPlanner.ReadoutValue(model, StatusBarReadoutKind.Sum).Should().BeEmpty();
    }

    private static StatusBarViewModel StatsModel() =>
        StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 100,
            new WorkbookSelectionStats(Sum: 60, Count: 4, NumericalCount: 3, Average: 20, Min: 10, Max: 30),
            new ResourceKeyStatusBarTextProvider(GetText));

    private static string GetText(string resourceKey) =>
        resourceKey switch
        {
            StatusBarTextResourceKeys.AverageFormat => "Average: {0}",
            StatusBarTextResourceKeys.CountFormat => "Count: {0}",
            StatusBarTextResourceKeys.NumericalCountFormat => "Numerical Count: {0}",
            StatusBarTextResourceKeys.SumFormat => "Sum: {0}",
            StatusBarTextResourceKeys.MinimumFormat => "Min: {0}",
            StatusBarTextResourceKeys.MaximumFormat => "Max: {0}",
            StatusBarTextResourceKeys.Average => "Average",
            StatusBarTextResourceKeys.Count => "Count",
            StatusBarTextResourceKeys.NumericalCount => "Numerical Count",
            StatusBarTextResourceKeys.Sum => "Sum",
            StatusBarTextResourceKeys.Minimum => "Minimum",
            StatusBarTextResourceKeys.Maximum => "Maximum",
            _ => resourceKey
        };
}
