using FreeW.App.Host.Editing;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

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

    [StaFact]
    public void Document_proofing_visibility_flags_hide_only_their_indicators()
    {
        var hiddenSpelling = TextDocument.CreateEmpty();
        hiddenSpelling.Blocks.Clear();
        hiddenSpelling.Blocks.Add(new Paragraph("teh the the"));
        hiddenSpelling.HideSpellingErrors = true;
        var view = new DocumentView();

        view.LoadModel(hiddenSpelling);

        view.SpellCheckEnabled.Should().BeTrue("the document setting must not change the user toggle");
        view.NativeSpellCheckEnabledForTest.Should().BeFalse();
        view.SharedGrammarDiagnostics.Should().ContainSingle();

        var hiddenGrammar = TextDocument.CreateEmpty();
        hiddenGrammar.Blocks.Clear();
        hiddenGrammar.Blocks.Add(new Paragraph("teh the the"));
        hiddenGrammar.HideGrammaticalErrors = true;

        view.LoadModel(hiddenGrammar);

        view.NativeSpellCheckEnabledForTest.Should().BeTrue();
        view.SharedGrammarDiagnostics.Should().BeEmpty();

        view.SpellCheckEnabled = false;
        view.LoadModel(hiddenSpelling);
        view.LoadModel(TextDocument.CreateEmpty());

        view.SpellCheckEnabled.Should().BeFalse("switching documents must preserve the user preference");
        view.NativeSpellCheckEnabledForTest.Should().BeFalse();
    }

    [StaFact]
    public void NoProof_disables_native_spellcheck_only_on_effective_run_and_survives_commit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("teh", RunFormatting.Default with { NoProof = true }));
        paragraph.Runs.Add(new Run(" visible"));
        doc.Blocks.Add(paragraph);
        var view = new DocumentView();

        view.LoadModel(doc);

        var runs = view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>().ToArray();
        runs[0].Language.IetfLanguageTag.Should().Be("zxx");
        runs[1].Language.IetfLanguageTag.Should().NotBe("zxx");

        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs[0].Formatting.NoProof.Should().BeTrue();
        ((Paragraph)view.Model.Blocks[0]).Runs[1].Formatting.NoProof.Should().BeFalse();
    }

    [StaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoProof_native_spellcheck_suppression_honors_style_chain_and_document_default(bool useDefault)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph("teh");
        if (useDefault)
        {
            doc.DefaultRun = doc.DefaultRun with { NoProof = true };
        }
        else
        {
            doc.Styles["BaseNoProof"] = new DocumentStyle
            {
                Id = "BaseNoProof",
                Name = "Base no proof",
                Run = RunFormatting.Default with { NoProof = true },
            };
            doc.Styles["ChildNoProof"] = new DocumentStyle
            {
                Id = "ChildNoProof",
                Name = "Child no proof",
                BasedOnStyleId = "BaseNoProof",
            };
            paragraph.StyleId = "ChildNoProof";
        }
        doc.Blocks.Add(paragraph);
        var view = new DocumentView();

        view.LoadModel(doc);

        view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>().Single()
            .Language.IetfLanguageTag.Should().Be("zxx");
    }
}
