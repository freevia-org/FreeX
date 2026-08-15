using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class FindReplaceDialogPlannerTests
{
    [Fact]
    public void Surface_ProvidesSharedFieldsActionsMetricsAndAutomationNames()
    {
        var surface = FindReplaceDialogPlanner.Surface;

        surface.Title.Should().Be("Find & Replace");
        surface.Fields.Select(field => field.Kind).Should().Equal(
            FindReplaceDialogFieldKind.Find,
            FindReplaceDialogFieldKind.Replace);
        surface.Actions.Select(action => action.Kind).Should().Equal(
            FindReplaceDialogActionKind.FindNext,
            FindReplaceDialogActionKind.Replace,
            FindReplaceDialogActionKind.ReplaceAll,
            FindReplaceDialogActionKind.Close);
        surface.Options.Should().BeSameAs(FindReplaceDialogPlanner.OptionChoices);
        surface.Metrics.WindowWidth.Should().Be(420);
        surface.Fields.Should().OnlyContain(field => !string.IsNullOrWhiteSpace(field.AutomationId));
        surface.Options.Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.AutomationId));
        surface.Actions.Should().OnlyContain(action => !string.IsNullOrWhiteSpace(action.AutomationId));
        surface.GoToButtonAutomationId.Should().Be("FindReplaceGoToButton");
    }

    [Fact]
    public void OptionChoices_ExposeWordFindReplaceOptionsInDisplayOrder()
    {
        FindReplaceDialogPlanner.OptionChoices.Select(choice => choice.Kind)
            .Should().Equal(
                FindReplaceOptionKind.MatchCase,
                FindReplaceOptionKind.WholeWord,
                FindReplaceOptionKind.UseWildcards);

        FindReplaceDialogPlanner.OptionChoices.Select(choice => choice.Label)
            .Should().Equal("Match case", "Whole word", "Use wildcards  (* ? [ ] < >)");
    }

    [Fact]
    public void BuildOptionPlans_DisablesWholeWordWhenWildcardsAreEnabled()
    {
        var plans = FindReplaceDialogPlanner.BuildOptionPlans(new FindReplaceSearchOptions(
            MatchCase: false,
            WholeWord: true,
            UseWildcards: true));

        plans.Should().Contain(plan =>
            plan.Kind == FindReplaceOptionKind.WholeWord &&
            plan.Label == "Whole word" &&
            !plan.IsEnabled);
        plans.Where(plan => plan.Kind != FindReplaceOptionKind.WholeWord)
            .Should().OnlyContain(plan => plan.IsEnabled);
    }

    [Fact]
    public void NormalizeOptions_ClearsWholeWordWhenWildcardsAreEnabled()
    {
        var options = FindReplaceDialogPlanner.NormalizeOptions(new FindReplaceSearchOptions(
            MatchCase: true,
            WholeWord: true,
            UseWildcards: true));

        options.MatchCase.Should().BeTrue();
        options.WholeWord.Should().BeFalse();
        options.UseWildcards.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryCreateSearchRequest_RejectsMissingSearchTerm(string? term)
    {
        FindReplaceDialogPlanner.TryCreateSearchRequest(
                term,
                new FindReplaceSearchOptions(),
                out var request,
                out var error)
            .Should().BeFalse();

        request.Should().BeNull();
        error.Should().Be(FindReplaceValidationError.SearchTermRequired);
        FindReplaceDialogPlanner.ValidationMessageFor(error)
            .Should().Be(FindReplaceDialogPlanner.SearchTermRequiredMessage);
    }

    [Fact]
    public void TryCreateReplaceRequest_NormalizesReplacementAndOptions()
    {
        FindReplaceDialogPlanner.TryCreateReplaceRequest(
                "fox",
                replacement: null,
                new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: true),
                out var request,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        request.Should().NotBeNull();
        request!.Term.Should().Be("fox");
        request.Replacement.Should().BeEmpty();
        request.Options.Should().Be(new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: true));
    }

    [Fact]
    public void StatusBuilders_ComposeFindAndReplaceResults()
    {
        var search = new FindReplaceSearchRequest("fox", new FindReplaceSearchOptions());
        var replace = new FindReplaceReplaceRequest("fox", "wolf", new FindReplaceSearchOptions());

        FindReplaceDialogPlanner.BuildFindStatus(search, found: true).Should().BeEmpty();
        FindReplaceDialogPlanner.BuildFindStatus(search, found: false).Should().Be("\"fox\" not found.");
        FindReplaceDialogPlanner.BuildReplaceStatus(replace, replaced: true).Should().BeEmpty();
        FindReplaceDialogPlanner.BuildReplaceStatus(replace, replaced: false).Should().Be("\"fox\" not found.");
        FindReplaceDialogPlanner.BuildReplaceAllStatus(replace, replacementCount: 0).Should().Be("\"fox\" not found.");
        FindReplaceDialogPlanner.BuildReplaceAllStatus(replace, replacementCount: 1).Should().Be("Replaced 1 occurrence.");
        FindReplaceDialogPlanner.BuildReplaceAllStatus(replace, replacementCount: 2).Should().Be("Replaced 2 occurrences.");
        FindReplaceDialogPlanner.BuildReplaceAllStatus(replace, replacementCount: 1, inSelection: true)
            .Should().Be("Replaced 1 occurrence in selection.");
    }

    [Fact]
    public void ResolvePolicyText_UsesFreeWResourceDescriptors()
    {
        var text = FindReplaceDialogPlanner.ResolvePolicyText(key => $"resolved:{key}");

        text.SearchTermRequired.Should().Be("resolved:FreeW_FindReplace_SearchTermRequired");
        text.NoMatches.Should().Be("resolved:FreeW_FindReplace_NoMatches");
        text.NoReplacements.Should().Be("resolved:FreeW_FindReplace_NoReplacements");
        text.NotFoundFormat.Should().Be("resolved:FreeW_FindReplace_NotFound_Format");
        text.MatchFormat.Should().Be("resolved:FreeW_FindReplace_Match_Format");
        text.ReplacedOccurrencesFormat.Should().Be("resolved:FreeW_FindReplace_ReplacedOccurrences_Format");
        text.ReplacementsMadeFormat.Should().Be("resolved:FreeW_FindReplace_ReplacementsMade_Format");
        FindReplaceDialogPlanner.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void StatusBuilders_ForwardResolvedPolicyText()
    {
        var search = new FindReplaceSearchRequest("fox", new FindReplaceSearchOptions());
        var replace = new FindReplaceReplaceRequest("fox", "wolf", new FindReplaceSearchOptions());
        var text = LocalizedPolicyText();

        FindReplaceDialogPlanner.ValidationMessageFor(FindReplaceValidationError.SearchTermRequired, text)
            .Should().Be("search required");
        FindReplaceDialogPlanner.BuildFindStatus(search, found: false, text)
            .Should().Be("missing fox");
        FindReplaceDialogPlanner.BuildReplaceStatus(replace, replaced: false, text)
            .Should().Be("missing fox");
        FindReplaceDialogPlanner.BuildReplaceAllStatus(replace, replacementCount: 2, text: text)
            .Should().Be("changed 2 items");
    }

    [Fact]
    public void ShouldUsePlainEditorSearch_OnlyForDefaultOptions()
    {
        FindReplaceDialogPlanner.ShouldUsePlainEditorSearch(new FindReplaceSearchOptions()).Should().BeTrue();
        FindReplaceDialogPlanner
            .ShouldUsePlainEditorSearch(new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: false))
            .Should()
            .BeFalse();
        FindReplaceDialogPlanner
            .ShouldUsePlainEditorSearch(new FindReplaceSearchOptions(MatchCase: false, WholeWord: true, UseWildcards: false))
            .Should()
            .BeFalse();
        FindReplaceDialogPlanner
            .ShouldUsePlainEditorSearch(new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: true))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void CountMatches_FindsAllCaseInsensitiveOccurrences()
    {
        var doc = BuildSampleDoc("The quick brown fox. FOX jumped over the fox.");

        var count = FindReplaceDialogPlanner.CountMatches(doc, "fox", new FindReplaceSearchOptions());

        count.Should().Be(3);
    }

    [Fact]
    public void CountMatches_RespectsMatchCase()
    {
        var doc = BuildSampleDoc("The quick brown fox. FOX jumped over the fox.");

        var count = FindReplaceDialogPlanner.CountMatches(
            doc,
            "fox",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: false));

        count.Should().Be(2);
    }

    [Fact]
    public void CountMatches_RespectsWholeWord()
    {
        var doc = BuildSampleDoc("foxglove fox foxes");

        var count = FindReplaceDialogPlanner.CountMatches(
            doc,
            "fox",
            new FindReplaceSearchOptions(MatchCase: false, WholeWord: true, UseWildcards: false));

        count.Should().Be(1);
    }

    [Fact]
    public void CountMatches_RespectsWildcardsAndClearsWholeWord()
    {
        var doc = BuildSampleDoc("cat bat hat sat rat");

        var count = FindReplaceDialogPlanner.CountMatches(
            doc,
            "[cbh]at",
            new FindReplaceSearchOptions(MatchCase: false, WholeWord: true, UseWildcards: true));

        count.Should().Be(3);
    }

    [Fact]
    public void CountMatches_ReturnsZeroForEmptyNeedle()
    {
        var doc = BuildSampleDoc("some text");

        FindReplaceDialogPlanner.CountMatches(doc, string.Empty, new FindReplaceSearchOptions())
            .Should()
            .Be(0);
    }

    [Fact]
    public void CountMatches_SpansMultipleParagraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Hello world") } });
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Say hello again") } });

        var count = FindReplaceDialogPlanner.CountMatches(doc, "hello", new FindReplaceSearchOptions());

        count.Should().Be(2);
    }

    [Fact]
    public void DocumentContains_UsesPlannedSearchRequest()
    {
        var doc = BuildSampleDoc("fox");
        var request = new FindReplaceSearchRequest(
            "fox",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: false));

        FindReplaceDialogPlanner.DocumentContains(doc, request).Should().BeTrue();
    }

    [Fact]
    public void FindAllAndMatchesExactly_UseTheNormalizedOptionPolicy()
    {
        var options = new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: true);

        FindReplaceDialogPlanner.FindAll("cat bat hat", "[cbh]at", options)
            .Should().HaveCount(3);
        FindReplaceDialogPlanner.MatchesExactly("BAT", "[cbh]at", options)
            .Should().BeFalse();
        FindReplaceDialogPlanner.MatchesExactly("bat", "[cbh]at", options)
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesExactly_UsesAnyFullSpanForWildcardSelection()
    {
        var options = new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: true);

        FindReplaceDialogPlanner.MatchesExactly("xbar", "*bar", options)
            .Should().BeTrue();
    }

    [Fact]
    public void FindNextMatch_UsesOptionsAndWrapsAcrossParagraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("first fox"));
        doc.Blocks.Add(new Paragraph("second FOX"));

        var match = FindReplaceDialogPlanner.FindNextMatch(
            doc,
            "fox",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: false),
            fromBlock: 1,
            fromOffset: 10);

        match.Should().Be(new FindReplaceMatch(0, 6, 3));
    }

    [Theory]
    [InlineData("the", 0, 0, 0, 0, 3)]
    [InlineData("the", 0, 3, 1, 11, 3)]
    [InlineData("quick", 1, 0, 0, 4, 5)]
    [InlineData("QUICK", 0, 0, 0, 4, 5)]
    public void FindNextMatch_DefaultOptionsFindAndWrapCaseInsensitively(
        string query,
        int fromBlock,
        int fromOffset,
        int expectedBlock,
        int expectedStart,
        int expectedLength)
    {
        var match = FindReplaceDialogPlanner.FindNextMatch(
            BuildTwoParagraphDocument(),
            query,
            new FindReplaceSearchOptions(),
            fromBlock,
            fromOffset);

        match.Should().Be(new FindReplaceMatch(expectedBlock, expectedStart, expectedLength));
    }

    [Fact]
    public void FindNextMatch_ReturnsNullWhenTextIsAbsent()
    {
        FindReplaceDialogPlanner.FindNextMatch(
                BuildTwoParagraphDocument(),
                "zebra",
                new FindReplaceSearchOptions(),
                fromBlock: 0,
                fromOffset: 0)
            .Should().BeNull();
    }

    [Fact]
    public void BuildGoToTargets_ProjectsStartEndHeadingsAndBookmarksInParityOrder()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Title") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body") { BookmarkName = "BodyTarget" });

        var targets = FindReplaceDialogPlanner.BuildGoToTargets(doc);

        targets.Select(target => target.Kind).Should().Equal(
            FindReplaceGoToTargetKind.DocumentStart,
            FindReplaceGoToTargetKind.DocumentEnd,
            FindReplaceGoToTargetKind.Heading,
            FindReplaceGoToTargetKind.Bookmark);
        targets.Select(target => target.Label).Should().Equal(
            "Document start",
            "Document end",
            "  Title",
            "Bookmark: BodyTarget");
        targets.Select(target => target.BlockIndex).Should().Equal(0, 1, 0, 1);
    }

    [Theory]
    [InlineData(FindReplaceGoToTargetKind.DocumentStart, 99, 0, "Document start")]
    [InlineData(FindReplaceGoToTargetKind.DocumentEnd, 0, 3, "Document end")]
    [InlineData(FindReplaceGoToTargetKind.Heading, 2, 2, "Heading")]
    [InlineData(FindReplaceGoToTargetKind.Bookmark, 99, 3, "Bookmark: Last")]
    public void PlanGoTo_ResolvesPortableBlockAndStatus(
        FindReplaceGoToTargetKind kind,
        int requestedBlock,
        int expectedBlock,
        string label)
    {
        var plan = FindReplaceDialogPlanner.PlanGoTo(
            new FindReplaceGoToTarget(kind, requestedBlock, $"  {label}  "),
            blockCount: 4);

        plan.Should().NotBeNull();
        plan!.BlockIndex.Should().Be(expectedBlock);
        plan.Label.Should().Be(label);
        plan.StatusText.Should().Be($"Jumped to {label}.");
    }

    private static TextDocument BuildSampleDoc(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { new Run(text) } });
        return doc;
    }

    private static TextDocument BuildTwoParagraphDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("the quick brown fox"));
        doc.Blocks.Add(new Paragraph("jumps over the lazy dog"));
        return doc;
    }

    /// <summary>
    /// Every combination of FreeW's three option flags, asserting which of them affect
    /// option enablement. Only wildcards do -- and only for whole-word. This rule stays
    /// FreeW-local (FreeP has no wildcards, FreeX has no whole-WORD option at all).
    /// </summary>
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    public void IsOptionEnabled_DisablesWholeWordExactlyWhenWildcardsAreOn(
        bool matchCase,
        bool wholeWord,
        bool useWildcards,
        bool expectedWholeWordEnabled)
    {
        var options = new FindReplaceSearchOptions(matchCase, wholeWord, useWildcards);

        FindReplaceDialogPlanner
            .IsOptionEnabled(FindReplaceOptionKind.WholeWord, options)
            .Should()
            .Be(expectedWholeWordEnabled);
        FindReplaceDialogPlanner
            .IsOptionEnabled(FindReplaceOptionKind.MatchCase, options)
            .Should()
            .BeTrue();
        FindReplaceDialogPlanner
            .IsOptionEnabled(FindReplaceOptionKind.UseWildcards, options)
            .Should()
            .BeTrue();
        FindReplaceDialogPlanner.NormalizeOptions(options).WholeWord
            .Should()
            .Be(wholeWord && expectedWholeWordEnabled);
    }

    [Theory]
    [InlineData(false, FindReplaceOpenMode.Find)]
    [InlineData(true, FindReplaceOpenMode.Replace)]
    public void OpenMode_IsTheSharedCrossAppFindReplaceMode(
        bool showReplace,
        FindReplaceOpenMode expected)
    {
        // FreeW consumes the same FindReplaceOpenMode as FreeX and FreeP; it renders the
        // mode as an initial-focus target rather than by hiding the replacement field.
        FindReplaceDialogPolicy.OpenModeFor(showReplace).Should().Be(expected);
        typeof(FindReplaceOpenMode).Assembly.GetName().Name
            .Should()
            .Be("Free.Shared.AppServices");
    }

    private static FindReplacePolicyTextSpec LocalizedPolicyText() => new(
        "search required",
        "no matches",
        "no replacements",
        "missing {0}",
        "match {0}/{1}",
        "changed {0} item{1}",
        "made {0} replacements");
}
