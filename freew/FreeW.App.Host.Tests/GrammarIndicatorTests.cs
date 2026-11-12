using System.Windows.Controls;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Proofing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Grammar issues on the WPF editing surface: the blue double-underline ranges and the right-click
/// grammar menu, both fed by the shared grammar diagnostics and the document's hide-errors setting.
/// </summary>
public sealed class GrammarIndicatorTests
{
    [StaFact]
    public void Squiggle_ranges_cover_exactly_the_flagged_text_of_each_grammar_issue()
    {
        var view = LoadedView("She ate a apple ,then left", "It was the the dog");

        view.RefreshGrammarSquiggles();

        view.GrammarSquiggleTexts.Should().Equal("a", "apple ,", "the");
        view.SharedGrammarDiagnostics.Select(diagnostic => diagnostic.RuleId).Should().Equal(
            GrammarRuleIds.IndefiniteArticle,
            GrammarRuleIds.SpaceBeforePunctuation,
            GrammarRuleIds.RepeatedWord);
    }

    [StaFact]
    public void Squiggles_respect_the_documents_hide_grammar_setting_and_the_as_you_type_toggle()
    {
        var doc = Document("She ate a apple");
        doc.HideGrammaticalErrors = true;
        var view = new DocumentView();
        view.LoadModel(doc);

        view.RefreshGrammarSquiggles();
        view.GrammarSquiggleTexts.Should().BeEmpty("the document hides grammatical errors");
        view.CollectProofingReviewIssues().Should().ContainSingle("F7 still checks hidden errors");

        view.LoadModel(Document("She ate a apple"));
        view.RefreshGrammarSquiggles();
        view.GrammarSquiggleTexts.Should().Equal("a");

        view.SpellCheckEnabled = false;
        view.RefreshGrammarSquiggles();
        view.GrammarSquiggleTexts.Should().BeEmpty();
    }

    [StaFact]
    public void Grammar_options_switch_individual_rules_off_on_the_surface()
    {
        var view = LoadedView("She ate a apple and the the pear");

        view.GrammarOptions = new GrammarCheckOptions([GrammarRuleIds.IndefiniteArticle]);

        view.GrammarSquiggleTexts.Should().Equal("the");
        view.CollectProofingReviewIssues().Select(issue => issue.RuleId)
            .Should().Equal(GrammarRuleIds.RepeatedWord);
    }

    [StaFact]
    public void Non_english_and_no_proof_text_gets_no_grammar_underline()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Voici a apple. ", RunFormatting.Default with { LanguageTag = "fr-FR" }));
        paragraph.Runs.Add(new Run("Code a apple. ", RunFormatting.Default with { NoProof = true }));
        paragraph.Runs.Add(new Run("She ate a egg"));
        doc.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.RefreshGrammarSquiggles();

        view.GrammarSquiggleTexts.Should().Equal("a");
        view.SharedGrammarDiagnostics.Should().ContainSingle().Which.RunIndex.Should().Be(2);
    }

    [StaFact]
    public void Context_menu_offers_the_rule_suggestions_and_ignore_actions_then_clipboard_commands()
    {
        var view = LoadedView("She ate a apple");
        var issue = GrammarIssueAtOffset(view, 0, 8);

        var menu = view.BuildGrammarContextMenu(issue);

        var entries = menu.Items.Cast<object>()
            .Select(item => item switch
            {
                // Clipboard captions come from the framework in the OS language; assert them by command.
                MenuItem { Command: not null } => "<command>",
                MenuItem menuItem => (string)menuItem.Header,
                _ => "<separator>",
            })
            .ToArray();
        entries.Should().Equal(
            "A or An",
            "an",
            "<separator>",
            "Ignore Once",
            "Don't Check for This Issue",
            "<separator>",
            "<command>",
            "<command>",
            "<command>");
        menu.Items.OfType<MenuItem>().First().IsEnabled.Should().BeFalse("the rule name is a caption");
        menu.Items.OfType<MenuItem>().Where(item => item.Command is not null).Select(item => item.Command)
            .Should().Equal(
                System.Windows.Input.ApplicationCommands.Cut,
                System.Windows.Input.ApplicationCommands.Copy,
                System.Windows.Input.ApplicationCommands.Paste);
    }

    [StaFact]
    public void Context_menu_suggestion_applies_the_fix_as_one_undoable_edit()
    {
        var view = LoadedView("She ate a apple");
        var issue = GrammarIssueAtOffset(view, 0, 8);

        view.ExecuteGrammarContextMenuCommand(
            issue,
            new RibbonCommandId(ProofingContextMenuPlanner.GrammarReplacementPrefix + "0"));

        ParagraphTexts(view).Should().Equal("She ate an apple");
        view.RefreshGrammarSquiggles();
        view.GrammarSquiggleTexts.Should().BeEmpty();
        view.Commands.Undo().Should().BeTrue();
        ParagraphTexts(view).Should().Equal("She ate a apple");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void Context_menu_ignore_once_hides_only_that_occurrence_until_its_paragraph_changes()
    {
        var view = LoadedView("She ate a apple", "He ate a egg");
        var issue = GrammarIssueAtOffset(view, 0, 8);

        view.ExecuteGrammarContextMenuCommand(issue, new RibbonCommandId(ProofingContextMenuPlanner.GrammarIgnoreOnce));

        view.GrammarSquiggleTexts.Should().ContainSingle();
        view.CollectProofingReviewIssues().Should().ContainSingle().Which.BlockIndex.Should().Be(1);

        view.ApplyProofingReplacements([new ProofingReviewReplacement(0, 0, 3, "They")]).Should().BeTrue();
        view.CollectProofingReviewIssues().Should().HaveCount(2, "an edited paragraph is checked afresh");
    }

    [StaFact]
    public void Context_menu_ignore_rule_stops_checking_that_rule()
    {
        var view = LoadedView("She ate a apple", "He ate a egg and the the pear");
        var issue = GrammarIssueAtOffset(view, 0, 8);

        view.ExecuteGrammarContextMenuCommand(issue, new RibbonCommandId(ProofingContextMenuPlanner.GrammarIgnoreRule));

        view.GrammarSquiggleTexts.Should().Equal("the");
        view.IgnoredGrammarRules.Should().Equal(GrammarRuleIds.IndefiniteArticle);
    }

    [StaFact]
    public void Grammar_issue_lookup_only_hits_positions_inside_a_visible_issue()
    {
        var view = LoadedView("She ate a apple");

        view.MoveCaretToBlockForTest(0, 8);
        view.GrammarIssueAt(view.CaretPosition)!.RuleId.Should().Be(GrammarRuleIds.IndefiniteArticle);

        view.MoveCaretToBlockForTest(0, 1);
        view.GrammarIssueAt(view.CaretPosition).Should().BeNull();

        view.Model.HideGrammaticalErrors = true;
        view.MoveCaretToBlockForTest(0, 8);
        view.GrammarIssueAt(view.CaretPosition).Should().BeNull();
    }

    [Fact]
    public void Shared_grammar_menu_plan_localizes_and_disables_edits_when_editing_is_restricted()
    {
        var issue = new ProofingReviewIssue(
            ProofingReviewIssueKind.Grammar,
            0,
            4,
            9,
            "Hello,you",
            [new ProofingReviewSuggestion(" ", 10, 0)],
            GrammarRuleIds.MissingSpaceAfterPunctuation);

        var plan = ProofingContextMenuPlanner.BuildGrammar(issue, canEdit: false, key => $"<{key}>");

        plan.Items.Select(item => item.Kind == RibbonMenuItemKind.Separator ? "|" : item.Header).Should().Equal(
            "<Grammar_MissingSpaceAfterPunctuation_Title>",
            "Hello, you",
            "|",
            "<Proofing_Review_IgnoreOnce>",
            "<Proofing_Review_IgnoreRule>",
            "|");
        plan.Items[1].IsEnabled.Should().BeFalse();
        plan.Items[3].IsEnabled.Should().BeTrue();
        ProofingContextMenuPlanner.TryParseReplacement(plan.Items[1].CommandId!.Value, out var index).Should().BeTrue();
        index.Should().Be(0);
    }

    private static ProofingReviewIssue GrammarIssueAtOffset(DocumentView view, int block, int offset)
    {
        view.MoveCaretToBlockForTest(block, offset);
        return view.GrammarIssueAt(view.CaretPosition)!;
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
