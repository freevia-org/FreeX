using FluentAssertions;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonDisplayStateTests
{
    [Fact]
    public void StartsWithTheRibbonAlwaysShown()
    {
        var state = new RibbonDisplayState();

        state.IsCollapsed.Should().BeFalse();
        state.ShowsCommands.Should().BeTrue();
    }

    [Fact]
    public void ToggleCollapsed_HidesAndRestoresTheCommands()
    {
        var state = new RibbonDisplayState();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        state.ToggleCollapsed();
        state.IsCollapsed.Should().BeTrue();
        state.ShowsCommands.Should().BeFalse();

        state.ToggleCollapsed();
        state.IsCollapsed.Should().BeFalse();
        state.ShowsCommands.Should().BeTrue();
        changes.Should().Be(2);
    }

    [Fact]
    public void TabClick_ShowsTheCollapsedRibbonUntilACommandRuns()
    {
        var state = new RibbonDisplayState();
        state.SetCollapsed(true);

        state.OnTabClicked();
        state.ShowsCommands.Should().BeTrue();
        state.IsCollapsed.Should().BeTrue("a temporary show does not pin the ribbon");

        state.OnTabClicked();
        state.ShowsCommands.Should().BeTrue("switching tabs keeps the temporary ribbon open");

        state.OnCommandExecuted();
        state.ShowsCommands.Should().BeFalse();
    }

    [Fact]
    public void TabClick_ShowsTheCollapsedRibbonUntilDismissed()
    {
        var state = new RibbonDisplayState();
        state.SetCollapsed(true);
        state.OnTabClicked();

        state.OnDismissed();

        state.ShowsCommands.Should().BeFalse();
    }

    [Fact]
    public void TabClickAndCommands_DoNothingWhileTheRibbonIsPinned()
    {
        var state = new RibbonDisplayState();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        state.OnTabClicked();
        state.OnCommandExecuted();
        state.OnDismissed();

        changes.Should().Be(0);
        state.ShowsCommands.Should().BeTrue();
    }

    [Fact]
    public void PinningATemporarilyShownRibbon_KeepsItShown()
    {
        var state = new RibbonDisplayState();
        state.SetCollapsed(true);
        state.OnTabClicked();

        state.ToggleCollapsed();

        state.IsCollapsed.Should().BeFalse();
        state.IsTemporarilyShown.Should().BeFalse();
        state.ShowsCommands.Should().BeTrue();
    }
}
