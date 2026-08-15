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

    [Fact]
    public void BuildRendererPlan_MapsPresentationToStableRendererSlots()
    {
        var model = StatsModel();
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            NumericalCount = true,
            Sum = false,
            Maximum = true,
            Zoom = false
        };
        var presentation = StatusBarPresentationPlanner.Build(
            model,
            options,
            fallbackAutomationText: "Customize Status Bar");

        var renderer = StatusBarPresentationPlanner.BuildRendererPlan(presentation);

        renderer.ReadyText.Should().BeEmpty();
        renderer.ReadyTextVisible.Should().BeFalse();
        renderer.VisibleReadoutText.Should().Be("Average: 20   Count: 4   Numerical Count: 3   Max: 30");
        renderer.VisibleReadoutTextVisible.Should().BeTrue();
        renderer.ZoomPercent.Should().Be(100);
        renderer.StatsPanelAutomationText.Should().Be("Average: 20; Count: 4; Numerical Count: 3; Max: 30");
        renderer.VisibilityElements.Should().Contain(new StatusBarElementVisibilityPlan(
            StatusBarPresentationElement.StatsPanel,
            true));
        renderer.IsElementVisible(StatusBarPresentationElement.StatsPanel).Should().BeTrue();
        renderer.VisibilityElements.Should().Contain(new StatusBarElementVisibilityPlan(
            StatusBarPresentationElement.Sum,
            false));
        renderer.VisibilityElements.Should().Contain(new StatusBarElementVisibilityPlan(
            StatusBarPresentationElement.ZoomText,
            false));
        renderer.VisibilityElements.Should().Contain(new StatusBarElementVisibilityPlan(
            StatusBarPresentationElement.ZoomControls,
            true));

        renderer.ReadoutElements.Should().ContainInOrder(
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.Average,
                StatusBarPresentationElement.Average,
                "Average: 20",
                "StatusAvgText",
                StatusBarTextResourceKeys.Average),
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.Count,
                StatusBarPresentationElement.Count,
                "Count: 4",
                "StatusCountText",
                StatusBarTextResourceKeys.Count),
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.NumericalCount,
                StatusBarPresentationElement.NumericalCount,
                "Numerical Count: 3",
                "StatusNumericalCountText",
                StatusBarTextResourceKeys.NumericalCount),
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.Sum,
                StatusBarPresentationElement.Sum,
                "Sum: 60",
                "StatusSumText",
                StatusBarTextResourceKeys.Sum),
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.Minimum,
                StatusBarPresentationElement.Minimum,
                "Min: 10",
                "StatusMinText",
                StatusBarTextResourceKeys.Minimum),
            new StatusBarReadoutPresentationPlan(
                StatusBarReadoutKind.Maximum,
                StatusBarPresentationElement.Maximum,
                "Max: 30",
                "StatusMaxText",
                StatusBarTextResourceKeys.Maximum));
    }

    [Fact]
    public void BuildRendererPlan_ReadyModelPlansSingleLineFooterState()
    {
        var model = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.Normal, zoomPercent: 125, "Ready");
        var presentation = StatusBarPresentationPlanner.Build(
            model,
            StatusBarOptionVisibility.ExcelDefaults,
            fallbackAutomationText: "Customize Status Bar");

        var renderer = StatusBarPresentationPlanner.BuildRendererPlan(presentation);

        renderer.ReadyText.Should().Be("Ready");
        renderer.ReadyTextVisible.Should().BeTrue();
        renderer.VisibleReadoutText.Should().BeEmpty();
        renderer.VisibleReadoutTextVisible.Should().BeFalse();
        renderer.ZoomPercent.Should().Be(125);
        renderer.StatsPanelAutomationText.Should().Be("Customize Status Bar");
        renderer.IsElementVisible(StatusBarPresentationElement.ReadyText).Should().BeTrue();
        renderer.IsElementVisible(StatusBarPresentationElement.StatsPanel).Should().BeFalse();
    }

    [Fact]
    public void AutomationChangePlanner_EmitsDeterministicVisibleFieldAndPanelChanges()
    {
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            Average = true,
            Count = true,
            NumericalCount = false,
            Sum = false,
            Minimum = false,
            Maximum = false,
        };
        var previous = AutomationSnapshot(StatsModel(average: 20), options);
        var current = AutomationSnapshot(StatsModel(average: 25), options);

        var changes = StatusBarAutomationChangePlanner.PlanChanges(previous, current);

        changes.Where(change => change.ShouldNotify)
            .Select(change => change.Current.Element)
            .Should().Equal(
                StatusBarPresentationElement.Average,
                StatusBarPresentationElement.StatsPanel);
        changes.Should().OnlyContain(change => change.Current.IsVisible);
    }

    [Fact]
    public void AutomationChangePlanner_SuppressesInitialUnchangedAndHiddenAnnouncements()
    {
        var hiddenAverageOptions = StatusBarOptionVisibility.ExcelDefaults with
        {
            Average = false,
            Count = true,
            NumericalCount = false,
            Sum = false,
            Minimum = false,
            Maximum = false,
        };
        var previous = AutomationSnapshot(StatsModel(average: 20), hiddenAverageOptions);
        var current = AutomationSnapshot(StatsModel(average: 25), hiddenAverageOptions);

        StatusBarAutomationChangePlanner.PlanChanges(null, previous)
            .Should().OnlyContain(change => !change.ShouldNotify);
        StatusBarAutomationChangePlanner.PlanChanges(previous, previous).Should().BeEmpty();
        StatusBarAutomationChangePlanner.PlanChanges(previous, current).Should().BeEmpty();
    }

    [Fact]
    public void AutomationSnapshot_UsesStableSixFieldOrderThenSinglePanelAggregate()
    {
        var snapshot = AutomationSnapshot(StatsModel(), StatusBarOptionVisibility.ExcelDefaults);

        snapshot.Elements.Select(element => element.Element).Should().Equal(
            StatusBarPresentationElement.Average,
            StatusBarPresentationElement.Count,
            StatusBarPresentationElement.NumericalCount,
            StatusBarPresentationElement.Sum,
            StatusBarPresentationElement.Minimum,
            StatusBarPresentationElement.Maximum,
            StatusBarPresentationElement.StatsPanel);
        snapshot.Elements.Last().AutomationId.Should().Be(
            StatusBarAutomationChangePlanner.StatsPanelAutomationId);
    }

    private static StatusBarAutomationSnapshot AutomationSnapshot(
        StatusBarViewModel model,
        StatusBarOptionVisibility options) =>
        StatusBarAutomationChangePlanner.BuildSnapshot(
            StatusBarPresentationPlanner.BuildRendererPlan(
                StatusBarPresentationPlanner.Build(model, options, fallbackAutomationText: "Statistics")),
            GetText,
            "Statistics");

    private static StatusBarViewModel StatsModel(double average = 20) =>
        StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 100,
            new WorkbookSelectionStats(Sum: 60, Count: 4, NumericalCount: 3, Average: average, Min: 10, Max: 30),
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
