namespace FreeW.Core.Model.Tests;

public sealed class ProofingDiagnosticPlannerTests
{
    [Fact]
    public void Build_detects_known_typo()
    {
        var doc = DocumentWithRun(new Run("This is teh typo."));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true).Should()
            .ContainSingle()
            .Which;

        diagnostic.Word.Should().Be("teh");
        diagnostic.NormalizedWord.Should().Be("teh");
        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Spelling);
        diagnostic.BlockIndex.Should().Be(0);
        diagnostic.RunIndex.Should().Be(0);
        diagnostic.RunOffset.Should().Be(8);
        diagnostic.ParagraphOffset.Should().Be(8);
        diagnostic.Length.Should().Be(3);
    }

    [Fact]
    public void Build_suppresses_diagnostics_when_spellcheck_disabled()
    {
        var doc = DocumentWithRun(new Run("teh teh"));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: false)
            .Should().BeEmpty();
    }

    [Fact]
    public void Build_suppresses_custom_dictionary_words()
    {
        var doc = DocumentWithRun(new Run("teh"));
        var dictionary = new CustomDictionary(["teh"]);

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true, dictionary)
            .Should().BeEmpty();
    }

    [Fact]
    public void Build_carries_run_language_tag()
    {
        var doc = DocumentWithRun(new Run("teh", new RunFormatting { LanguageTag = "fr-FR" }));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle()
            .Which.LanguageTag.Should().Be("fr-FR");
    }

    [Fact]
    public void Build_carries_run_and_paragraph_offsets()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello "));
        paragraph.Runs.Add(new Run("teh world"));
        doc.Blocks.Add(paragraph);

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle()
            .Which;

        diagnostic.RunIndex.Should().Be(1);
        diagnostic.RunOffset.Should().Be(0);
        diagnostic.ParagraphOffset.Should().Be(6);
    }

    [Fact]
    public void Build_detects_word_split_across_runs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("t"));
        paragraph.Runs.Add(new Run("eh"));
        doc.Blocks.Add(paragraph);

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle()
            .Which;

        diagnostic.Word.Should().Be("teh");
        diagnostic.RunIndex.Should().Be(0);
        diagnostic.RunOffset.Should().Be(0);
        diagnostic.ParagraphOffset.Should().Be(0);
        diagnostic.Length.Should().Be(3);
    }

    [Fact]
    public void Build_avoids_false_positive_for_normal_words()
    {
        var doc = DocumentWithRun(new Run("The quick brown fox jumps over the lazy dog."));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Build_skips_url_and_email_like_tokens()
    {
        var doc = DocumentWithRun(new Run("mail teh@example.com and visit https://teh.example"));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Build_detects_adjacent_repeated_word_as_grammar_on_second_word()
    {
        var doc = DocumentWithRun(new Run("This is the the issue."));

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true).Should()
            .ContainSingle()
            .Which;

        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Grammar);
        diagnostic.Word.Should().Be("the");
        diagnostic.NormalizedWord.Should().Be("the");
        diagnostic.BlockIndex.Should().Be(0);
        diagnostic.RunIndex.Should().Be(0);
        diagnostic.RunOffset.Should().Be(12);
        diagnostic.ParagraphOffset.Should().Be(12);
        diagnostic.Length.Should().Be(3);
    }

    [Fact]
    public void Build_detects_repeated_word_case_insensitively_across_runs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("The "));
        paragraph.Runs.Add(new Run("the answer"));
        doc.Blocks.Add(paragraph);

        var diagnostic = ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().ContainSingle()
            .Which;

        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Grammar);
        diagnostic.RunIndex.Should().Be(1);
        diagnostic.RunOffset.Should().Be(0);
        diagnostic.ParagraphOffset.Should().Be(4);
    }

    [Fact]
    public void Build_avoids_repeated_word_false_positive_across_punctuation_url_and_email_boundaries()
    {
        var doc = DocumentWithRun(new Run("the, the. the www.the the mail the@example.com the"));

        ProofingDiagnosticPlanner.Build(doc, spellCheckEnabled: true)
            .Should().BeEmpty();
    }

    private static TextDocument DocumentWithRun(Run run)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        doc.Blocks.Add(paragraph);
        return doc;
    }
}
