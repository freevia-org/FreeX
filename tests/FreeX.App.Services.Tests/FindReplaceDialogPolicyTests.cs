using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class FindReplaceDialogPolicyTests
{
    [Theory]
    [InlineData(false, FindReplaceOpenMode.Find)]
    [InlineData(true, FindReplaceOpenMode.Replace)]
    public void OpenModeFor_ProjectsHostReplaceFlagOntoSharedMode(
        bool showReplace,
        FindReplaceOpenMode expected)
    {
        FindReplaceDialogPolicy.OpenModeFor(showReplace).Should().Be(expected);
    }

    [Theory]
    [InlineData(FindReplaceOpenMode.Find, false)]
    [InlineData(FindReplaceOpenMode.Replace, true)]
    public void ShowsReplaceSurface_OffersReplaceCommandsOnlyInReplaceMode(
        FindReplaceOpenMode mode,
        bool expected)
    {
        FindReplaceDialogPolicy.ShowsReplaceSurface(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, "", false)]
    [InlineData(false, "needle", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "needle", true)]
    public void ReplaceAllEnablement_RequiresBothReplaceModeAndSearchTerm(
        bool showReplace,
        string query,
        bool expected)
    {
        var mode = FindReplaceDialogPolicy.OpenModeFor(showReplace);
        var canReplaceAll =
            FindReplaceDialogPolicy.ShowsReplaceSurface(mode) &&
            FindReplaceDialogPolicy.CanRunWithQuery(query);

        canReplaceAll.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, 3, 1, false)]
    [InlineData(true, 0, -1, false)]
    [InlineData(true, 3, -1, true)]
    [InlineData(true, 3, 1, true)]
    public void ReplaceEnablement_RequiresReplaceModeAndAResolvableTargetMatch(
        bool showReplace,
        int matchCount,
        int currentMatchIndex,
        bool expected)
    {
        var mode = FindReplaceDialogPolicy.OpenModeFor(showReplace);
        var canReplace =
            FindReplaceDialogPolicy.ShowsReplaceSurface(mode) &&
            FindReplaceDialogPolicy.ReplacementTargetIndex(currentMatchIndex, matchCount) >= 0;

        canReplace.Should().Be(expected);
    }

    [Fact]
    public void Navigate_AdvancesWrapsAndExhaustsTheMatchCursor()
    {
        var index = -1;
        foreach (var expected in new[] { 0, 1, 2, 0 })
        {
            var step = FindReplaceDialogPolicy.Navigate(index, matchCount: 3, direction: 1);
            step.HasMatch.Should().BeTrue();
            step.MatchIndex.Should().Be(expected);
            index = step.MatchIndex;
        }

        // Backwards from the first match wraps onto the last one.
        FindReplaceDialogPolicy.Navigate(0, matchCount: 3, direction: -1).MatchIndex.Should().Be(2);

        // An exhausted (emptied) match set reports no cursor at all, in either direction.
        foreach (var direction in new[] { 1, -1 })
        {
            var exhausted = FindReplaceDialogPolicy.Navigate(index, matchCount: 0, direction);
            exhausted.HasMatch.Should().BeFalse();
            exhausted.MatchIndex.Should().Be(-1);
            exhausted.StatusKind.Should().Be(FindReplacePolicyStatusKind.NoMatches);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("needle", true)]
    public void CanRunWithQuery_UsesEmptyQueryPolicy(string? query, bool expected)
    {
        FindReplaceDialogPolicy.CanRunWithQuery(query).Should().Be(expected);
    }

    [Fact]
    public void TryValidateSearchTerm_ReturnsSearchTermRequiredForEmptyQuery()
    {
        FindReplaceDialogPolicy.TryValidateSearchTerm(string.Empty, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(FindReplaceValidationErrorKind.SearchTermRequired);
        FindReplaceDialogPolicy.ValidationMessageFor(error)
            .Should()
            .Be(FindReplaceDialogPolicy.SearchTermRequiredMessage);
    }

    [Fact]
    public void StatusBuilders_ComposeFindReplaceAndReplaceAllMessages()
    {
        FindReplaceDialogPolicy.BuildFindStatus("fox", found: true).Should().BeEmpty();
        FindReplaceDialogPolicy.BuildFindStatus("fox", found: false).Should().Be("\"fox\" not found.");
        FindReplaceDialogPolicy.BuildReplaceStatus("fox", replaced: true).Should().BeEmpty();
        FindReplaceDialogPolicy.BuildReplaceStatus("fox", replaced: false).Should().Be("\"fox\" not found.");
        FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus("fox", replacementCount: 0)
            .Should()
            .Be("\"fox\" not found.");
        FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus("fox", replacementCount: 1)
            .Should()
            .Be("Replaced 1 occurrence.");
        FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus("fox", replacementCount: 2)
            .Should()
            .Be("Replaced 2 occurrences.");
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
        FindReplaceDialogPolicy.ReplacementTargetIndex(currentIndex, matchCount)
            .Should()
            .Be(expectedIndex);
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
        var plan = FindReplaceDialogPolicy.Navigate(currentIndex, matchCount, direction);

        plan.HasMatch.Should().BeTrue();
        plan.MatchIndex.Should().Be(expectedIndex);
        plan.StatusText.Should().Be(expectedStatus);
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.Match);
    }

    [Fact]
    public void Navigate_WithNoMatches_ReturnsNoMatchesStatus()
    {
        var plan = FindReplaceDialogPolicy.Navigate(currentMatchIndex: -1, matchCount: 0, direction: 1);

        plan.HasMatch.Should().BeFalse();
        plan.MatchIndex.Should().Be(-1);
        plan.StatusText.Should().Be(FindReplaceDialogPolicy.NoMatchesStatus);
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.NoMatches);
    }

    [Theory]
    [InlineData(0, FindReplaceDialogPolicy.NoReplacementsStatus, FindReplacePolicyStatusKind.NoReplacements)]
    [InlineData(2, "2 replacement(s) made.", FindReplacePolicyStatusKind.Replacements)]
    public void BuildReplacementStatus_FormatsDialogStatus(
        int count,
        string expectedStatus,
        FindReplacePolicyStatusKind expectedKind)
    {
        var status = FindReplaceDialogPolicy.BuildReplacementStatus(count);

        status.StatusText.Should().Be(expectedStatus);
        status.StatusKind.Should().Be(expectedKind);
    }
}
