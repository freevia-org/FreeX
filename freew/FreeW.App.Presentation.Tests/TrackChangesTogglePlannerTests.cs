using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TrackChangesTogglePlannerTests
{
    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    public void Build_matches_wpf_toggle_transition(
        bool currentlyEnabled,
        bool hasSelection,
        bool expectedEnabled,
        bool expectedMarkSelection)
    {
        var plan = TrackChangesTogglePlanner.Build(currentlyEnabled, hasSelection);

        plan.Enabled.Should().Be(expectedEnabled);
        plan.MarkSelectionAsInsertion.Should().Be(expectedMarkSelection);
    }
}
