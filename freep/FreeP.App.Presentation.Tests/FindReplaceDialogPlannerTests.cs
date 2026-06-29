using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FindReplaceDialogPlannerTests
{
    [Theory]
    [InlineData(false, FindReplaceDialogPlanner.FindTitle)]
    [InlineData(true, FindReplaceDialogPlanner.FindAndReplaceTitle)]
    public void TitleForMode_MatchesDialogMode(bool showReplace, string expectedTitle)
    {
        FindReplaceDialogPlanner.TitleForMode(showReplace).Should().Be(expectedTitle);
    }

    [Fact]
    public void BuildOptions_CopiesCheckboxStateToSearchOptions()
    {
        var options = FindReplaceDialogPlanner.BuildOptions(matchCase: true, wholeWord: false);

        options.MatchCase.Should().BeTrue();
        options.WholeWord.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("needle", true)]
    public void CanReplaceAll_PreservesExistingEmptyOnlyGuard(string? query, bool expected)
    {
        FindReplaceDialogPlanner.CanReplaceAll(query).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, 3, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(0, 0, -1)]
    public void ReplacementTargetIndex_UsesCurrentSelectionOrFirstMatch(
        int currentIndex,
        int matchCount,
        int expectedIndex)
    {
        FindReplaceDialogPlanner.ReplacementTargetIndex(currentIndex, matchCount)
            .Should().Be(expectedIndex);
    }

    [Theory]
    [InlineData(-1, 3, 1, 0, "Match 1 of 3")]
    [InlineData(-1, 3, -1, 1, "Match 2 of 3")]
    [InlineData(2, 3, 1, 0, "Match 1 of 3")]
    [InlineData(0, 3, -1, 2, "Match 3 of 3")]
    public void Navigate_PreservesWraparoundIndexPolicy(
        int currentIndex,
        int matchCount,
        int direction,
        int expectedIndex,
        string expectedStatus)
    {
        var plan = FindReplaceDialogPlanner.Navigate(currentIndex, matchCount, direction);

        plan.HasMatch.Should().BeTrue();
        plan.MatchIndex.Should().Be(expectedIndex);
        plan.StatusText.Should().Be(expectedStatus);
        plan.StatusKind.Should().Be(FindReplaceStatusKind.Match);
    }

    [Fact]
    public void Navigate_WithNoMatches_ReturnsNoMatchesStatus()
    {
        var plan = FindReplaceDialogPlanner.Navigate(currentMatchIndex: -1, matchCount: 0, direction: 1);

        plan.HasMatch.Should().BeFalse();
        plan.MatchIndex.Should().Be(-1);
        plan.StatusText.Should().Be(FindReplaceDialogPlanner.NoMatchesStatus);
        plan.StatusKind.Should().Be(FindReplaceStatusKind.NoMatches);
    }

    [Theory]
    [InlineData(0, FindReplaceDialogPlanner.NoReplacementsStatus, FindReplaceStatusKind.NoReplacements)]
    [InlineData(2, "2 replacement(s) made.", FindReplaceStatusKind.Replacements)]
    public void ReplacementStatus_FormatsDialogStatus(int count, string expectedStatus, FindReplaceStatusKind expectedKind)
    {
        var status = FindReplaceDialogPlanner.ReplacementStatus(count);

        status.StatusText.Should().Be(expectedStatus);
        status.StatusKind.Should().Be(expectedKind);
    }
}
