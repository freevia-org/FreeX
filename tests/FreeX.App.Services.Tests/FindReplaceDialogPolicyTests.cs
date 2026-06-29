using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class FindReplaceDialogPolicyTests
{
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
