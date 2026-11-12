using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The WPF editor as the renderer port of the shared Spelling &amp; Grammar walk-through (F7). Native
/// WPF spell checking is suppressed in this test host, so spelling comes from the portable correction
/// catalog; the selection, undo and dictionary paths exercised here are the production ones.
/// </summary>
public sealed class SpellingGrammarWalkthroughTests
{
    [StaFact]
    public void Walkthrough_selects_each_issue_and_changes_go_through_the_undo_stack()
    {
        var view = LoadedView("I saw teh cat", "It was the the dog");
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());

        session.Start();
        session.Current!.Kind.Should().Be(ProofingReviewIssueKind.Spelling);
        view.Selection.Text.Should().Be("teh");

        session.Change(0).Should().BeTrue();
        ParagraphTexts(view).Should().Equal("I saw the cat", "It was the the dog");
        session.Current!.Kind.Should().Be(ProofingReviewIssueKind.Grammar);
        view.Selection.Text.Should().Be("the");

        session.Change(0).Should().BeTrue();
        ParagraphTexts(view).Should().Equal("I saw the cat", "It was the dog");
        session.Status.Should().Be(ProofingReviewStatus.Completed);

        view.Commands.Undo().Should().BeTrue();
        ParagraphTexts(view).Should().Equal("I saw the cat", "It was the the dog");
        view.Commands.Undo().Should().BeTrue();
        ParagraphTexts(view).Should().Equal("I saw teh cat", "It was the the dog");
        view.Commands.CanUndo.Should().BeFalse("each proofing change is exactly one undo unit");
    }

    [StaFact]
    public void Walkthrough_starts_at_the_caret_and_wraps_around()
    {
        var view = LoadedView("teh first", "wierd second");
        view.MoveCaretToBlockForTest(1, 0);
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());

        session.Start();
        session.Current!.Text.Should().Be("wierd");
        session.IgnoreOnce();
        session.Current!.Text.Should().Be("teh");
        session.IgnoreOnce();

        session.Status.Should().Be(ProofingReviewStatus.Completed);
        ParagraphTexts(view).Should().Equal("teh first", "wierd second");
    }

    [StaFact]
    public void ChangeAll_is_a_single_undo_unit()
    {
        var view = LoadedView("teh one teh", "and teh end");
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());
        session.Start();

        session.ChangeAll(0).Should().BeTrue();

        ParagraphTexts(view).Should().Equal("the one the", "and the end");
        session.Status.Should().Be(ProofingReviewStatus.Completed);
        view.Commands.Undo().Should().BeTrue();
        ParagraphTexts(view).Should().Equal("teh one teh", "and teh end");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void Change_keeps_the_formatting_of_the_replaced_word()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain "));
        paragraph.Runs.Add(new Run("teh", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(new Run(" tail"));
        doc.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(doc);
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());
        session.Start();

        session.Change(0).Should().BeTrue();

        view.CommitToModel();
        var runs = ((Paragraph)view.Model.Blocks[0]).Runs;
        runs.Single(run => run.Text.Contains("the", StringComparison.Ordinal)).Formatting.Bold.Should().BeTrue();
        ParagraphTexts(view).Should().Equal("plain the tail");
    }

    [StaFact]
    public void AddToDictionary_uses_the_shared_custom_dictionary_store()
    {
        var view = LoadedView("teh one", "teh two");
        var dictionary = new CustomDictionaryStore(null);
        view.ProofingDictionary = dictionary;
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());
        session.Start();

        session.AddToDictionary().Should().BeTrue();

        dictionary.Contains("teh").Should().BeTrue();
        session.Status.Should().Be(ProofingReviewStatus.Completed);
        view.CollectProofingReviewIssues().Should().BeEmpty();
    }

    [StaFact]
    public void IgnoreAll_and_ignored_rules_last_until_another_document_is_loaded()
    {
        var view = LoadedView("teh one", "the the two");
        var session = new ProofingReviewSession(view.CreateProofingReviewHost());
        session.Start();
        session.IgnoreAll().Should().BeTrue();
        session.IgnoreAll().Should().BeTrue();
        session.Status.Should().Be(ProofingReviewStatus.Completed);

        view.CollectProofingReviewIssues().Should().BeEmpty();
        view.IgnoredGrammarRules.Should().Equal(GrammarRuleIds.RepeatedWord);

        view.LoadModel(Document("teh one", "the the two"));
        view.CollectProofingReviewIssues().Should().HaveCount(2);
    }

    [StaFact]
    public void Walkthrough_checks_even_when_as_you_type_checking_is_off_or_hidden()
    {
        var doc = Document("teh the the");
        doc.HideSpellingErrors = true;
        doc.HideGrammaticalErrors = true;
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SpellCheckEnabled = false;

        view.CollectProofingReviewIssues().Select(issue => issue.Kind)
            .Should().BeEquivalentTo([ProofingReviewIssueKind.Spelling, ProofingReviewIssueKind.Grammar]);
    }

    [StaFact]
    public void ApplyProofingReplacements_rejects_ranges_that_no_longer_fit_the_text()
    {
        var view = LoadedView("short");

        view.ApplyProofingReplacements([new ProofingReviewReplacement(0, 3, 10, "x")]).Should().BeFalse();
        view.ApplyProofingReplacements([new ProofingReviewReplacement(4, 0, 1, "x")]).Should().BeFalse();
        view.ApplyProofingReplacements([]).Should().BeFalse();

        ParagraphTexts(view).Should().Equal("short");
        view.Commands.CanUndo.Should().BeFalse();
        view.Commands.IsUndoGroupOpen.Should().BeFalse();
    }

    [StaFact]
    public void Review_proofing_ribbon_binds_spelling_and_grammar_and_shares_the_dictionary()
    {
        var editor = new DocumentView();
        var opened = 0;
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with { OpenSpellingAndGrammar = () => opened++ },
            FreeWWpfRibbonNativeExecutionPorts.Empty);

        registry.TryGet("freew.spelling-grammar", out var command).Should().BeTrue();
        command!.Execute(new RibbonCommandContext(new Dictionary<string, object?>()));

        opened.Should().Be(1);
        editor.ProofingDictionary.Should().NotBeNull("the walk-through must add to the same store as Add to Dictionary");
    }

    private static DocumentView LoadedView(params string[] paragraphs)
    {
        var view = new DocumentView();
        view.LoadModel(Document(paragraphs));
        return view;
    }

    private static TextDocument Document(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var paragraph in paragraphs)
            doc.Blocks.Add(new Paragraph(paragraph));
        return doc;
    }

    private static string[] ParagraphTexts(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToArray();
    }
}
