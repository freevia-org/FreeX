using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class ProofingDiagnosticsTests
{
    [StaFact]
    public void SharedGrammarDiagnostics_surface_repeated_word_from_committed_model()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("The "));
        paragraph.Runs.Add(new Run("the answer"));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);

        var diagnostic = view.SharedGrammarDiagnostics.Should().ContainSingle().Which;
        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Grammar);
        diagnostic.Word.Should().Be("the");
        diagnostic.RunIndex.Should().Be(1);
        diagnostic.RunOffset.Should().Be(0);
        diagnostic.ParagraphOffset.Should().Be(4);
    }

    [StaFact]
    public void SharedGrammarDiagnostics_follow_spelling_and_grammar_toggle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("the the"));
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SharedGrammarDiagnostics.Should().ContainSingle();
        view.ToggleSpellCheck().Should().BeFalse();

        view.SharedGrammarDiagnostics.Should().BeEmpty();
    }
}
