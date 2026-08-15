using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class FindReplaceDialogPlannerTests
{
    [Fact]
    public void PolicyTextCatalog_OwnsUniqueResourceKeysAndResolvesEveryStatus()
    {
        var localized = FreePFindReplacePolicyTextCatalog.BuildTextSpec(key => $"localized:{key}");

        FreePFindReplacePolicyTextCatalog.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        localized.SearchTermRequired.Should().Be($"localized:{CommonShellResourceKeys.FindReplaceSearchTermRequired}");
        localized.NoMatches.Should().Be($"localized:{CommonShellResourceKeys.FindReplaceNoMatches}");
        localized.NoReplacements.Should().Be($"localized:{FreePFindReplacePolicyResourceKeys.NoReplacements}");
        localized.NotFoundFormat.Should().Be($"localized:{CommonShellResourceKeys.FindReplaceNotFoundFormat}");
        localized.MatchFormat.Should().Be($"localized:{CommonShellResourceKeys.FindReplaceMatchFormat}");
        localized.ReplacedOccurrencesFormat.Should().Be($"localized:{FreePFindReplacePolicyResourceKeys.ReplacedOccurrencesFormat}");
        localized.ReplacementsMadeFormat.Should().Be($"localized:{FreePFindReplacePolicyResourceKeys.ReplacementsMadeFormat}");
    }

    [Fact]
    public void BuildSurfacePlan_OwnsLabelsAndOrderedOptionCatalogs()
    {
        var surface = FindReplaceDialogPlanner.BuildSurfacePlan();

        surface.FindLabel.Should().Be("Find what:");
        surface.ReplaceLabel.Should().Be("Replace with:");
        surface.Options.Should().Equal(
            new FindReplaceDialogOption(FindReplaceDialogOptionKind.MatchCase, "Match case"),
            new FindReplaceDialogOption(FindReplaceDialogOptionKind.WholeWord, "Whole word"));
        surface.Actions.Should().Equal(
            new FindReplaceDialogActionOption(FindReplaceDialogAction.FindNext, "Find Next"),
            new FindReplaceDialogActionOption(FindReplaceDialogAction.FindPrevious, "Find Previous"),
            new FindReplaceDialogActionOption(FindReplaceDialogAction.ReplaceCurrent, "Replace"),
            new FindReplaceDialogActionOption(FindReplaceDialogAction.ReplaceAll, "Replace All"));
        surface.CloseLabel.Should().Be("Close");
        surface.Schema.Fields.Select(field => field.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Schema.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Action(FindReplaceDialogAction.FindNext).IsDefault.Should().BeTrue();
        surface.Action(FindReplaceDialogAction.Close).IsCancel.Should().BeTrue();
        surface.Field(FindReplaceDialogField.Query).HelpText.Should().Be("Enter text to find.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildInitialState_OwnsEmptyInputAndOptionDefaults(bool showReplace)
    {
        FindReplaceDialogPlanner.BuildInitialState(showReplace).Should().Be(
            new FindReplaceDialogInitialState(
                showReplace,
                string.Empty,
                string.Empty,
                MatchCase: false,
                WholeWord: false));
    }

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
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.Match);
    }

    [Fact]
    public void Navigate_WithNoMatches_ReturnsNoMatchesStatus()
    {
        var plan = FindReplaceDialogPlanner.Navigate(currentMatchIndex: -1, matchCount: 0, direction: 1);

        plan.HasMatch.Should().BeFalse();
        plan.MatchIndex.Should().Be(-1);
        plan.StatusText.Should().Be(FindReplaceDialogPolicy.NoMatchesStatus);
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.NoMatches);
    }

    [Fact]
    public void Navigate_AndReplacementStatus_UseInjectedPolicyText()
    {
        var text = new FindReplacePolicyTextSpec(
            "query required",
            "nothing found",
            "nothing replaced",
            "missing {0}",
            "result {0}/{1}",
            "changed {0}{1}",
            "changed total {0}");

        FindReplaceDialogPlanner.Navigate(-1, 2, 1, text).StatusText.Should().Be("result 1/2");
        FindReplaceDialogPlanner.Navigate(-1, 0, 1, text).StatusText.Should().Be("nothing found");
        FindReplaceDialogPlanner.ReplacementStatus(0, text).StatusText.Should().Be("nothing replaced");
        FindReplaceDialogPlanner.ReplacementStatus(3, text).StatusText.Should().Be("changed total 3");
    }

    [Theory]
    [InlineData(0, FindReplaceDialogPolicy.NoReplacementsStatus, FindReplacePolicyStatusKind.NoReplacements)]
    [InlineData(2, "2 replacement(s) made.", FindReplacePolicyStatusKind.Replacements)]
    public void ReplacementStatus_FormatsDialogStatus(int count, string expectedStatus, FindReplacePolicyStatusKind expectedKind)
    {
        var status = FindReplaceDialogPlanner.ReplacementStatus(count);

        status.StatusText.Should().Be(expectedStatus);
        status.StatusKind.Should().Be(expectedKind);
    }

    [Fact]
    public void BuildWorkflowPlan_ProjectsRendererStateWithoutFrameworkDependencies()
    {
        var matches = new[]
        {
            new TextSearchMatch { SlideIndex = 0, ShapeId = 7, MatchedText = "needle" },
            new TextSearchMatch { SlideIndex = 1, ShapeId = 8, MatchedText = "needle" },
        };

        var plan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            showReplace: true,
            query: "needle",
            replacement: "thread",
            matchCase: true,
            wholeWord: false,
            matches,
            currentMatchIndex: 1,
            statusText: "Match 2 of 2",
            statusKind: FindReplacePolicyStatusKind.Match);

        plan.Title.Should().Be(FindReplaceDialogPlanner.FindAndReplaceTitle);
        plan.ShowReplace.Should().BeTrue();
        plan.Query.Should().Be("needle");
        plan.Replacement.Should().Be("thread");
        plan.MatchCase.Should().BeTrue();
        plan.WholeWord.Should().BeFalse();
        plan.MatchCount.Should().Be(2);
        plan.CurrentMatchIndex.Should().Be(1);
        plan.StatusText.Should().Be("Match 2 of 2");
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.Match);
        plan.CanSearch.Should().BeTrue();
        plan.CanNavigate.Should().BeTrue();
        plan.CanReplace.Should().BeTrue();
        plan.CanReplaceAll.Should().BeTrue();
    }

    [Fact]
    public void BuildWorkflowPlan_DisablesReplaceActionsWhenFindModeOrQueryMissing()
    {
        var plan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            showReplace: false,
            query: "",
            replacement: null,
            matchCase: false,
            wholeWord: true,
            matches: Array.Empty<TextSearchMatch>(),
            currentMatchIndex: 4);

        plan.Title.Should().Be(FindReplaceDialogPlanner.FindTitle);
        plan.CurrentMatchIndex.Should().Be(-1);
        plan.MatchCount.Should().Be(0);
        plan.CanSearch.Should().BeFalse();
        plan.CanNavigate.Should().BeFalse();
        plan.CanReplace.Should().BeFalse();
        plan.CanReplaceAll.Should().BeFalse();
        plan.WholeWord.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, FindReplaceOpenMode.Find)]
    [InlineData(true, FindReplaceOpenMode.Replace)]
    public void OpenModeFor_RoutesTheHostShowReplaceFlagThroughTheSharedPolicy(
        bool showReplace,
        FindReplaceOpenMode expected)
    {
        FindReplaceDialogPlanner.OpenModeFor(showReplace).Should().Be(expected);
        FindReplaceDialogPlanner.ShowsReplaceSurface(expected)
            .Should()
            .Be(FindReplaceDialogPolicy.ShowsReplaceSurface(expected));
    }

    [Theory]
    [InlineData(FindReplaceOpenMode.Find, FindReplaceDialogPlanner.FindTitle)]
    [InlineData(FindReplaceOpenMode.Replace, FindReplaceDialogPlanner.FindAndReplaceTitle)]
    public void TitleForMode_IsDrivenByTheSharedOpenMode(
        FindReplaceOpenMode mode,
        string expectedTitle)
    {
        FindReplaceDialogSurfaceCatalog.Surface.TitleForMode(mode).Should().Be(expectedTitle);
    }

    /// <summary>
    /// Every combination of the two inputs that gate Replace All -- the shared open mode and a
    /// non-empty search term -- plus the match-cursor states that gate Replace.
    /// </summary>
    [Theory]
    [InlineData(false, "needle", 2, 1, false, false)]
    [InlineData(false, "", 2, 1, false, false)]
    [InlineData(true, "", 2, 1, true, false)]
    [InlineData(true, "needle", 2, 1, true, true)]
    [InlineData(true, "needle", 0, -1, false, true)]
    [InlineData(true, "needle", 2, -1, true, true)]
    [InlineData(true, "needle", 2, 9, true, true)]
    public void BuildWorkflowPlan_GatesReplaceCommandsOnOpenModeQueryAndCursor(
        bool showReplace,
        string query,
        int matchCount,
        int currentMatchIndex,
        bool expectedCanReplace,
        bool expectedCanReplaceAll)
    {
        var matches = Enumerable.Range(0, matchCount)
            .Select(index => new TextSearchMatch
            {
                SlideIndex = index,
                ShapeId = (uint)index,
                MatchedText = "needle",
            })
            .ToArray();

        var plan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            showReplace,
            query,
            replacement: "thread",
            matchCase: false,
            wholeWord: false,
            matches,
            currentMatchIndex);

        plan.CanReplace.Should().Be(expectedCanReplace);
        plan.CanReplaceAll.Should().Be(expectedCanReplaceAll);
        plan.CanSearch.Should().Be(FindReplaceDialogPolicy.CanRunWithQuery(query));
        plan.CanNavigate.Should().Be(matchCount > 0);
    }
}
