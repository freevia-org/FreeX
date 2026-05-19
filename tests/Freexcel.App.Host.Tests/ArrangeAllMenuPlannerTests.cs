using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ArrangeAllMenuPlannerTests
{
    [Fact]
    public void IsChecked_MatchesCurrentArrangementTagExactly()
    {
        ArrangeAllMenuPlanner.IsChecked("Vertical", WorkbookWindowArrangement.Vertical).Should().BeTrue();
        ArrangeAllMenuPlanner.IsChecked("vertical", WorkbookWindowArrangement.Vertical).Should().BeFalse();
        ArrangeAllMenuPlanner.IsChecked(null, WorkbookWindowArrangement.Vertical).Should().BeFalse();
    }

    [Fact]
    public void TryParseArrangement_AcceptsDefinedArrangementTags()
    {
        var parsed = ArrangeAllMenuPlanner.TryParseArrangement("Cascade", out var arrangement);

        parsed.Should().BeTrue();
        arrangement.Should().Be(WorkbookWindowArrangement.Cascade);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Floating")]
    [InlineData("vertical")]
    public void TryParseArrangement_RejectsInvalidOrNonExactTags(string? tag)
    {
        ArrangeAllMenuPlanner.TryParseArrangement(tag, out _).Should().BeFalse();
    }
}
