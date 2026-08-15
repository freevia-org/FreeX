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
}
