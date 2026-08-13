using FluentAssertions;
using FreeW.App.Presentation.Editing;
using Xunit;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentNoteNavigationPlannerTests
{
    [Theory]
    [InlineData(false, 15, 20)]
    [InlineData(false, 30, 10)]
    [InlineData(true, 25, 20)]
    [InlineData(true, 10, 30)]
    public void FindAdjacent_SelectsByDirectionAndWraps(
        bool previous,
        int caret,
        int expected)
    {
        DocumentNoteNavigationPlanner.TryFindAdjacent(
                new[] { 10, 20, 30 },
                marker => marker.CompareTo(caret),
                previous,
                out var target)
            .Should().BeTrue();

        target.Should().Be(expected);
    }

    [Fact]
    public void FindAdjacent_RejectsAnEmptyMarkerList()
    {
        DocumentNoteNavigationPlanner.TryFindAdjacent(
                Array.Empty<int>(),
                marker => marker,
                previous: false,
                out _)
            .Should().BeFalse();
    }
}
