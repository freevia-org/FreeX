using FreeW.App.Presentation.Proofing;

namespace FreeW.App.Presentation.Tests;

public sealed class ProofingReviewSessionTests
{
    [Fact]
    public void Start_visits_issues_from_the_caret_and_wraps_around_to_the_document_start()
    {
        var host = new FakeHost("teh first", "the the second", "wierd third") { Caret = new(1, 0) };
        var session = new ProofingReviewSession(host);

        session.Start();
        session.Status.Should().Be(ProofingReviewStatus.Reviewing);
        session.Current!.Kind.Should().Be(ProofingReviewIssueKind.Grammar);
        session.Current.BlockIndex.Should().Be(1);
        host.Selected.Should().BeSameAs(session.Current);

        session.IgnoreOnce().Should().BeTrue();
        session.Current!.Text.Should().Be("wierd");
        host.IgnoredOnce.Should().ContainSingle().Which.RuleId.Should().Be(GrammarRuleIds.RepeatedWord);

        session.IgnoreOnce().Should().BeTrue();
        session.Current!.Text.Should().Be("teh", "the walk wraps to the top after the last issue");

        session.IgnoreOnce().Should().BeTrue();
        session.Status.Should().Be(ProofingReviewStatus.Completed);
        session.Current.Should().BeNull();
        host.Paragraphs.Should().Equal("teh first", "the the second", "wierd third");
    }

    [Fact]
    public void Start_with_no_issues_completes_immediately()
    {
        var session = new ProofingReviewSession(new FakeHost("Nothing to fix here"));

        session.Start();

        session.Status.Should().Be(ProofingReviewStatus.Completed);
        session.CanChange.Should().BeFalse();
        session.IgnoreOnce().Should().BeFalse();
    }

    [Fact]
    public void Start_begins_with_the_issue_the_caret_sits_in()
    {
        var host = new FakeHost("teh and wierd") { Caret = new(0, 10) };
        var session = new ProofingReviewSession(host);

        session.Start();

        session.Current!.Text.Should().Be("wierd");
        session.IgnoreOnce();
        session.Current!.Text.Should().Be("teh");
        session.IgnoreOnce();
        session.Status.Should().Be(ProofingReviewStatus.Completed);
    }

    [Fact]
    public void Change_applies_the_suggestion_as_one_edit_and_moves_on_with_shifted_offsets()
    {
        var host = new FakeHost("teh wierd teh");
        var session = new ProofingReviewSession(host);
        session.Start();

        session.Change(0).Should().BeTrue();

        host.Paragraphs.Should().Equal("the wierd teh");
        host.AppliedBatches.Should().ContainSingle().Which.Should().ContainSingle();
        session.Current!.Text.Should().Be("wierd");
        session.Current.Offset.Should().Be(4);

        session.Change(0).Should().BeTrue();
        host.Paragraphs.Should().Equal("the weird teh");
        session.Current!.Offset.Should().Be(10);
        session.ChangeCount.Should().Be(2);
    }

    [Fact]
    public void ChangeAll_replaces_every_occurrence_in_one_batch_ordered_from_the_end()
    {
        var host = new FakeHost("teh one teh", "wierd", "and teh end");
        var session = new ProofingReviewSession(host);
        session.Start();
        session.CanChangeAll.Should().BeTrue();

        session.ChangeAll(0).Should().BeTrue();

        host.Paragraphs.Should().Equal("the one the", "wierd", "and the end");
        var batch = host.AppliedBatches.Should().ContainSingle().Which;
        batch.Select(replacement => (replacement.BlockIndex, replacement.Offset))
            .Should().Equal((2, 4), (0, 8), (0, 0));
        session.Current!.Text.Should().Be("wierd");

        session.IgnoreOnce();
        session.Status.Should().Be(ProofingReviewStatus.Completed);
    }

    [Fact]
    public void Grammar_change_deletes_the_repeated_word_with_its_leading_space()
    {
        var host = new FakeHost("This is the the issue");
        var session = new ProofingReviewSession(host);
        session.Start();

        session.Current!.Kind.Should().Be(ProofingReviewIssueKind.Grammar);
        session.Current.RuleId.Should().Be(GrammarRuleIds.RepeatedWord);
        session.CanChangeAll.Should().BeFalse("Change All is a spelling action");
        session.CanAddToDictionary.Should().BeFalse();
        session.ChangeAll(0).Should().BeFalse();

        session.Change(0).Should().BeTrue();

        host.Paragraphs.Should().Equal("This is the issue");
        session.Status.Should().Be(ProofingReviewStatus.Completed);
    }

    [Fact]
    public void IgnoreAll_skips_every_occurrence_of_the_word_and_tells_the_host()
    {
        var host = new FakeHost("teh one", "wierd", "teh two");
        var session = new ProofingReviewSession(host);
        session.Start();

        session.IgnoreAll().Should().BeTrue();

        host.IgnoredSpellings.Should().Equal("teh");
        session.Current!.Text.Should().Be("wierd");
        session.IgnoreOnce();
        session.Status.Should().Be(ProofingReviewStatus.Completed);
        host.Paragraphs.Should().Equal("teh one", "wierd", "teh two");
    }

    [Fact]
    public void IgnoreAll_on_a_grammar_issue_disables_its_rule_for_the_session()
    {
        var host = new FakeHost("the the one", "and and two");
        var session = new ProofingReviewSession(host);
        session.Start();

        session.IgnoreAll().Should().BeTrue();

        host.IgnoredRules.Should().Equal(GrammarRuleIds.RepeatedWord);
        session.Status.Should().Be(ProofingReviewStatus.Completed);
    }

    [Fact]
    public void AddToDictionary_goes_through_the_host_dictionary_and_stops_flagging_the_word()
    {
        var host = new FakeHost("teh one teh", "wierd");
        var session = new ProofingReviewSession(host);
        session.Start();
        session.CanAddToDictionary.Should().BeTrue();

        session.AddToDictionary().Should().BeTrue();

        host.Dictionary.Should().Equal("teh");
        session.Current!.Text.Should().Be("wierd");
    }

    [Fact]
    public void Rejected_change_keeps_the_current_issue_and_the_document()
    {
        var host = new FakeHost("teh wierd") { RejectEdits = true };
        var session = new ProofingReviewSession(host);
        session.Start();
        var current = session.Current;

        session.Change(0).Should().BeFalse();

        session.Current.Should().BeSameAs(current);
        session.ChangeCount.Should().Be(0);
        host.Paragraphs.Should().Equal("teh wierd");
    }

    [Fact]
    public void Change_never_applies_offsets_of_an_issue_that_was_edited_away_behind_the_pane()
    {
        var host = new FakeHost("teh wierd");
        var session = new ProofingReviewSession(host);
        session.Start();

        host.Paragraphs[0] = "xx teh wierd";
        session.Change(0).Should().BeFalse();

        host.AppliedBatches.Should().BeEmpty();
        session.Current!.Text.Should().Be("teh");
        session.Current.Offset.Should().Be(3);
    }

    [Fact]
    public void Change_rejects_an_out_of_range_suggestion_index()
    {
        var session = new ProofingReviewSession(new FakeHost("teh"));
        session.Start();

        session.Change(-1).Should().BeFalse();
        session.Change(5).Should().BeFalse();
        session.Status.Should().Be(ProofingReviewStatus.Reviewing);
    }

    [Fact]
    public void Ignored_occurrence_stays_ignored_after_an_earlier_change_shifts_it()
    {
        var host = new FakeHost("the the teh x") { Caret = new(0, 9) };
        var session = new ProofingReviewSession(host);
        session.Start();
        session.Current!.Text.Should().Be("teh");
        session.IgnoreOnce();
        session.Current!.Kind.Should().Be(ProofingReviewIssueKind.Grammar);

        session.Change(0).Should().BeTrue();

        host.Paragraphs.Should().Equal("the teh x");
        session.Status.Should().Be(ProofingReviewStatus.Completed, "the ignored 'teh' moved left but stays ignored");
    }

    [Fact]
    public void Close_returns_to_idle()
    {
        var session = new ProofingReviewSession(new FakeHost("teh"));
        session.Start();

        session.Close();

        session.Status.Should().Be(ProofingReviewStatus.Idle);
        session.Current.Should().BeNull();
        session.Change(0).Should().BeFalse();
    }

    [Fact]
    public void Pane_plan_describes_a_spelling_issue_with_word_style_actions()
    {
        var session = new ProofingReviewSession(new FakeHost("teh cat"));
        session.Start();

        var plan = ProofingReviewPanePlanner.Build(session);

        plan.Title.Should().Be("Spelling & Grammar");
        plan.ShowIssue.Should().BeTrue();
        plan.Heading.Should().Be("Not in Dictionary");
        plan.IssueText.Should().Be("teh");
        plan.Suggestions.Should().Equal("the");
        plan.Change.Should().Be(new ProofingReviewPaneAction("Change", true, true));
        plan.ChangeAll.Should().Be(new ProofingReviewPaneAction("Change All", true, true));
        plan.IgnoreOnce.Should().Be(new ProofingReviewPaneAction("Ignore Once", true, true));
        plan.IgnoreAll.Should().Be(new ProofingReviewPaneAction("Ignore All", true, true));
        plan.AddToDictionary.Should().Be(new ProofingReviewPaneAction("Add to Dictionary", true, true));
        plan.StatusText.Should().BeEmpty();
    }

    [Fact]
    public void Pane_plan_explains_a_grammar_issue_and_hides_spelling_only_actions()
    {
        var session = new ProofingReviewSession(new FakeHost("the the cat"));
        session.Start();

        var plan = ProofingReviewPanePlanner.Build(session);

        plan.Heading.Should().Be("Repeated Word");
        plan.Explanation.Should().Contain("twice in a row");
        plan.Suggestions.Should().Equal("Delete Repeated Word");
        plan.Change.IsEnabled.Should().BeTrue();
        plan.ChangeAll.IsVisible.Should().BeFalse();
        plan.AddToDictionary.IsVisible.Should().BeFalse();
        plan.IgnoreAll.Label.Should().Be("Don't Check for This Issue");
    }

    [Fact]
    public void Pane_plan_reports_words_completion_message_and_failed_changes()
    {
        var host = new FakeHost("teh");
        var session = new ProofingReviewSession(host);
        session.Start();

        ProofingReviewPanePlanner.Build(session, lastChangeFailed: true).StatusText
            .Should().Contain("can't be made");

        session.IgnoreOnce();
        var complete = ProofingReviewPanePlanner.Build(session);
        complete.ShowIssue.Should().BeFalse();
        complete.StatusText.Should().Be("Spelling and grammar check is complete.");
        complete.Change.IsVisible.Should().BeFalse();
        complete.Close.IsEnabled.Should().BeTrue();
        ProofingReviewPanePlanner.CompleteMessage().Should().Be(complete.StatusText);
    }

    [Fact]
    public void Pane_plan_resolves_text_through_the_supplied_localizer()
    {
        var session = new ProofingReviewSession(new FakeHost("the the"));
        session.Start();

        var plan = ProofingReviewPanePlanner.Build(session, key => $"<{key}>");

        plan.Title.Should().Be("<Proofing_Review_Title>");
        plan.Heading.Should().Be("<Grammar_RepeatedWord_Title>");
        plan.Explanation.Should().Be("<Grammar_RepeatedWord_Explanation>");
        plan.Suggestions.Should().Equal("<Grammar_RepeatedWord_Fix>");
        plan.IgnoreAll.Label.Should().Be("<Proofing_Review_IgnoreRule>");
    }

    [Fact]
    public void Pane_and_rule_resource_keys_exist_in_the_neutral_resources()
    {
        var neutral = FreeW.App.Localization.Loc.GetNeutralResourceKeys();

        ProofingReviewPanePlanner.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        ProofingReviewPanePlanner.RequiredResourceKeys.Should().OnlyContain(key => neutral.Contains(key));
    }

    /// <summary>
    /// A host over plain paragraph strings whose issues come from the real shared planner, so the
    /// session is exercised against the diagnostics the renderers actually feed it.
    /// </summary>
    private sealed class FakeHost(params string[] paragraphs) : IProofingReviewHost
    {
        public List<string> Paragraphs { get; } = [.. paragraphs];

        public ProofingReviewPosition Caret { get; set; }

        public bool RejectEdits { get; set; }

        public ProofingReviewIssue? Selected { get; private set; }

        public List<IReadOnlyList<ProofingReviewReplacement>> AppliedBatches { get; } = [];

        public List<string> IgnoredSpellings { get; } = [];

        public List<string> IgnoredRules { get; } = [];

        public List<string> Dictionary { get; } = [];

        public ProofingReviewPosition CaretPosition => Caret;

        public IReadOnlyList<ProofingReviewIssue> CollectIssues()
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            foreach (var paragraph in Paragraphs)
                document.Blocks.Add(new Paragraph(paragraph));

            return ProofingDiagnosticPlanner.Build(document, spellCheckEnabled: true, Dictionary)
                .Select(ProofingReviewIssue.FromDiagnostic)
                .Reverse()
                .ToArray();
        }

        public void SelectIssue(ProofingReviewIssue issue) => Selected = issue;

        public bool ApplyReplacements(IReadOnlyList<ProofingReviewReplacement> replacements)
        {
            if (RejectEdits)
                return false;

            AppliedBatches.Add(replacements);
            foreach (var replacement in replacements)
            {
                var text = Paragraphs[replacement.BlockIndex];
                Paragraphs[replacement.BlockIndex] = text[..replacement.Offset]
                    + replacement.Text
                    + text[(replacement.Offset + replacement.Length)..];
            }

            return true;
        }

        public List<ProofingReviewIssue> IgnoredOnce { get; } = [];

        public void IgnoreOnce(ProofingReviewIssue issue) => IgnoredOnce.Add(issue);

        public void IgnoreSpelling(ProofingReviewIssue issue) => IgnoredSpellings.Add(issue.Text);

        public void AddToDictionary(ProofingReviewIssue issue) => Dictionary.Add(issue.Text);

        public void IgnoreGrammarRule(string ruleId) => IgnoredRules.Add(ruleId);
    }
}
