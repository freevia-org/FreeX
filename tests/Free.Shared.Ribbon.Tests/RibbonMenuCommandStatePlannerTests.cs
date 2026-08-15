using FluentAssertions;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonMenuCommandStatePlannerTests
{
    [Fact]
    public void Plan_DisablesUnavailableCommandsWithoutInventingCheckability()
    {
        var definition = new RibbonMenuItem("Run", "run");

        var plan = RibbonMenuCommandStatePlanner.Plan(
            definition,
            commandAvailable: false,
            commandState: null);

        plan.Should().Be(new RibbonMenuCommandState(IsEnabled: false, IsChecked: null));
    }

    [Fact]
    public void Plan_PreservesDefinitionDisablementOverLiveCommandState()
    {
        var definition = new RibbonMenuItem("Unavailable", "unavailable")
        {
            IsEnabled = false,
            IsChecked = false,
        };

        var plan = RibbonMenuCommandStatePlanner.Plan(
            definition,
            commandAvailable: true,
            new RibbonCommandState(IsEnabled: true, IsChecked: true));

        plan.Should().Be(new RibbonMenuCommandState(IsEnabled: false, IsChecked: true));
    }

    [Fact]
    public void Plan_ProjectsLiveCheckedStateOnlyForCheckableItems()
    {
        var liveState = new RibbonCommandState(IsEnabled: true, IsChecked: true);

        RibbonMenuCommandStatePlanner.Plan(
                new RibbonMenuItem("Checkable", "checkable") { IsChecked = false },
                commandAvailable: true,
                liveState)
            .Should().Be(new RibbonMenuCommandState(IsEnabled: true, IsChecked: true));

        RibbonMenuCommandStatePlanner.Plan(
                new RibbonMenuItem("Plain", "plain"),
                commandAvailable: true,
                liveState)
            .Should().Be(new RibbonMenuCommandState(IsEnabled: true, IsChecked: null));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PlanCollapsedControl_PreservesToggleState(bool enabled, bool isChecked)
    {
        var plan = RibbonMenuCommandStatePlanner.PlanCollapsedControl(
            new RibbonToggleButton("toggle", "Toggle"),
            commandAvailable: true,
            new RibbonCommandState(IsEnabled: enabled, IsChecked: isChecked));

        plan.Should().Be(new RibbonMenuCommandState(enabled, isChecked));
    }

    [Fact]
    public void PlanCollapsedControl_MakesCheckBoxesCheckableAndDefaultsUnchecked()
    {
        RibbonMenuCommandStatePlanner.PlanCollapsedControl(
                new RibbonCheckBox("check", "Check"),
                commandAvailable: true,
                commandState: null)
            .Should().Be(new RibbonMenuCommandState(IsEnabled: true, IsChecked: false));
    }

    [Fact]
    public void PlanCollapsedControl_DoesNotInventCheckabilityForOrdinaryCommands()
    {
        RibbonMenuCommandStatePlanner.PlanCollapsedControl(
                new RibbonButton("run", "Run"),
                commandAvailable: true,
                new RibbonCommandState(IsEnabled: true, IsChecked: true))
            .Should().Be(new RibbonMenuCommandState(IsEnabled: true, IsChecked: null));
    }

    [Fact]
    public void PlanCollapsedControl_DisablesUnavailableCommands()
    {
        RibbonMenuCommandStatePlanner.PlanCollapsedControl(
                new RibbonToggleButton("missing", "Missing"),
                commandAvailable: false,
                new RibbonCommandState(IsEnabled: true, IsChecked: true))
            .Should().Be(new RibbonMenuCommandState(IsEnabled: false, IsChecked: true));
    }
}
