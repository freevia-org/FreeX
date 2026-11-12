namespace FreeW.Core.Model.Tests;

/// <summary>
/// The grammar rule engine as surfaced through <see cref="ProofingDiagnosticPlanner"/>: run mapping,
/// proofing-language and no-proof eligibility, rule options, and the document visibility flag.
/// </summary>
public sealed class ProofingGrammarDiagnosticsTests
{
    [Fact]
    public void Build_surfaces_rule_issues_as_grammar_diagnostics_with_rule_id_and_suggestions()
    {
        var doc = Document(new Run("She ate a apple"));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which;

        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Grammar);
        diagnostic.RuleId.Should().Be(GrammarRuleIds.IndefiniteArticle);
        diagnostic.Word.Should().Be("a");
        diagnostic.ParagraphOffset.Should().Be(8);
        diagnostic.Length.Should().Be(1);
        diagnostic.SuggestionsOrEmpty.Should().Equal(new ProofingSuggestion("an", 8, 1));
    }

    [Fact]
    public void Build_maps_rule_issues_to_the_run_they_start_in()
    {
        var doc = Document(new Run("Intro text. "), new Run("then more."));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which;

        diagnostic.RuleId.Should().Be(GrammarRuleIds.SentenceCapitalization);
        diagnostic.RunIndex.Should().Be(1);
        diagnostic.RunOffset.Should().Be(0);
        diagnostic.ParagraphOffset.Should().Be(12);
    }

    [Fact]
    public void Build_keeps_a_paragraphs_diagnostics_in_reading_order_across_passes()
    {
        var doc = Document(new Run("She ate a apple and teh the the pear ,quickly"));

        var diagnostics = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true);

        diagnostics.Select(diagnostic => diagnostic.ParagraphOffset).Should().BeInAscendingOrder();
        diagnostics.Select(diagnostic => (diagnostic.Kind, diagnostic.RuleId)).Should().Equal(
            (ProofingDiagnosticKind.Grammar, GrammarRuleIds.IndefiniteArticle),
            (ProofingDiagnosticKind.Spelling, null),
            (ProofingDiagnosticKind.Grammar, GrammarRuleIds.RepeatedWord),
            (ProofingDiagnosticKind.Grammar, GrammarRuleIds.SpaceBeforePunctuation));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("bg-BG")]
    public void English_rules_do_not_fire_for_a_non_english_document(string languageTag)
    {
        var doc = Document(new Run("it ended. the a apple ,then could of gone"));
        doc.DefaultRun = doc.DefaultRun with { LanguageTag = languageTag };

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true).Should().BeEmpty();
    }

    [Fact]
    public void English_rules_do_not_fire_for_non_english_runs_but_do_for_english_ones()
    {
        var french = RunFormatting.Default with { LanguageTag = "fr-FR" };
        var doc = Document(
            new Run("Bonjour , a apple. ", french),
            new Run("She ate a apple"));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which;

        diagnostic.RuleId.Should().Be(GrammarRuleIds.IndefiniteArticle);
        diagnostic.RunIndex.Should().Be(1);
    }

    [Fact]
    public void English_run_inside_a_non_english_document_is_checked()
    {
        var doc = Document(
            new Run("Voici un mot , "),
            new Run("a apple", RunFormatting.Default with { LanguageTag = "en-GB" }));
        doc.DefaultRun = doc.DefaultRun with { LanguageTag = "fr-FR" };

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which.RuleId.Should().Be(GrammarRuleIds.IndefiniteArticle);
    }

    [Fact]
    public void Paragraph_style_language_decides_eligibility_through_the_style_chain()
    {
        var doc = Document(new Run("She ate a apple"));
        doc.Styles["BaseFrench"] = new DocumentStyle
        {
            Id = "BaseFrench",
            Name = "Base French",
            Run = RunFormatting.Default with { LanguageTag = "fr-FR" },
        };
        doc.Styles["ChildFrench"] = new DocumentStyle
        {
            Id = "ChildFrench",
            Name = "Child French",
            BasedOnStyleId = "BaseFrench",
        };
        ((Paragraph)doc.Blocks[0]).StyleId = "ChildFrench";

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true).Should().BeEmpty();
    }

    [Fact]
    public void Repeated_word_check_stays_language_independent()
    {
        var doc = Document(new Run("le le mot"));
        doc.DefaultRun = doc.DefaultRun with { LanguageTag = "fr-FR" };

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which.RuleId.Should().Be(GrammarRuleIds.RepeatedWord);
    }

    [Fact]
    public void Rule_issues_touching_no_proof_text_are_dropped()
    {
        var noProof = RunFormatting.Default with { NoProof = true };
        var doc = Document(
            new Run("She ate a apple ", noProof),
            new Run("and a egg"));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle().Which;

        diagnostic.RunIndex.Should().Be(1);
        diagnostic.Word.Should().Be("a");
    }

    [Fact]
    public void No_proof_document_default_silences_every_rule()
    {
        var doc = Document(new Run("it ended. the a apple ,then the the could of gone"));
        doc.DefaultRun = doc.DefaultRun with { NoProof = true };

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true).Should().BeEmpty();
    }

    [Fact]
    public void Grammar_options_switch_individual_rules_off_including_repeated_words()
    {
        var doc = Document(new Run("She ate a apple and the the pear"));
        var options = new GrammarCheckOptions([GrammarRuleIds.RepeatedWord]);

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true, customDictionaryWords: null, options)
            .Should().ContainSingle().Which.RuleId.Should().Be(GrammarRuleIds.IndefiniteArticle);
        ProofingDiagnosticPlanner.Build(
                doc,
                spellCheckEnabled: true,
                customDictionaryWords: null,
                options.WithRuleDisabled(GrammarRuleIds.IndefiniteArticle))
            .Should().BeEmpty();
    }

    [Fact]
    public void Spellcheck_toggle_and_hide_grammar_flag_govern_rule_issues_too()
    {
        var doc = Document(new Run("She ate a apple"));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: false).Should().BeEmpty();
        ProofingDiagnosticPlanner.BuildVisibleIndicators(doc, spellCheckEnabled: true).Should().ContainSingle();

        doc.HideGrammaticalErrors = true;
        ProofingDiagnosticPlanner.BuildVisibleIndicators(doc, spellCheckEnabled: true).Should().BeEmpty();
        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle("hiding indicators must not discard the diagnostics");
    }

    private static TextDocument Document(params Run[] runs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var run in runs)
            paragraph.Runs.Add(run);
        doc.Blocks.Add(paragraph);
        return doc;
    }
}
