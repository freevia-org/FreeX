using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewingPaneStatePlannerTests
{
    [Theory]
    [InlineData(0, -1, -1, "No tracked changes")]
    [InlineData(1, -1, 0, "1 change")]
    [InlineData(3, -1, 0, "3 changes")]
    [InlineData(3, 1, 1, "3 changes")]
    [InlineData(3, 8, 2, "3 changes")]
    public void BuildRefreshState_preserves_or_clamps_selection_and_formats_WPF_status(
        int revisionCount,
        int previousIndex,
        int expectedIndex,
        string expectedStatus)
    {
        var state = ReviewingPaneStatePlanner.BuildRefreshState(revisionCount, previousIndex);

        state.SelectedIndex.Should().Be(expectedIndex);
        state.StatusText.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(0, -1, 1, -1)]
    [InlineData(3, -1, 1, 0)]
    [InlineData(3, -1, -1, 2)]
    [InlineData(3, 0, -1, 2)]
    [InlineData(3, 2, 1, 0)]
    [InlineData(3, 1, 99, 2)]
    [InlineData(3, 1, -99, 0)]
    public void ResolveStep_wraps_and_treats_direction_as_previous_or_next(
        int revisionCount,
        int currentIndex,
        int direction,
        int expectedIndex)
    {
        ReviewingPaneStatePlanner.ResolveStep(revisionCount, currentIndex, direction)
            .Should().Be(expectedIndex);
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        FluentActions.Invoking(() => ReviewingPaneStatePlanner.BuildRefreshState(-1, -1))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReviewingPaneStatePlanner.ResolveStep(1, 0, 0))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReviewingPaneStatePlanner.ResolveStep(1, 1, 1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
