namespace FreeW.Core.Model.Tests;

public sealed class ProofingCorrectionCatalogTests
{
    [Fact]
    public void Catalog_CoversEveryPortableSpellingDiagnostic()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(string.Join(
            " ",
            ProofingCorrectionCatalog.Entries.Select(entry => entry.Misspelling))));

        var diagnostics = ProofingDiagnosticPlanner.Build(document, spellCheckEnabled: true);

        diagnostics
            .Where(diagnostic => diagnostic.Kind == ProofingDiagnosticKind.Spelling)
            .Select(diagnostic => diagnostic.NormalizedWord)
            .Should()
            .Equal(ProofingCorrectionCatalog.Entries.Select(entry => entry.Misspelling));
    }

    [Fact]
    public void Suggestions_AreDeterministicAndPreserveSimpleWordCasing()
    {
        ProofingCorrectionCatalog.SuggestionsFor("teh").Should().Equal("the");
        ProofingCorrectionCatalog.SuggestionsFor("Teh").Should().Equal("The");
        ProofingCorrectionCatalog.SuggestionsFor("TEH").Should().Equal("THE");
        ProofingCorrectionCatalog.SuggestionsFor("unknown").Should().BeEmpty();
    }
}
