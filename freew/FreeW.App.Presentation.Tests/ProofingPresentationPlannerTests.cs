using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ProofingPresentationPlannerTests
{
    [Fact]
    public void Proofing_language_catalog_exposes_common_word_languages()
    {
        ProofingLanguageCatalog.CommonLanguages.Select(choice => choice.Tag)
            .Should().Contain(["en-US", "fr-FR", "de-DE", "es-ES"]);
    }

    [Fact]
    public void Proofing_language_catalog_normalizes_blank_to_clear()
    {
        ProofingLanguageCatalog.NormalizeTag(" fr-FR ").Should().Be("fr-FR");
        ProofingLanguageCatalog.NormalizeTag("").Should().BeNull();
    }

    [Fact]
    public void Shared_thesaurus_lookup_returns_known_synonyms()
    {
        var entry = ThesaurusLookup.Instance.Lookup("happy");

        entry.Should().NotBeNull();
        entry!.Senses.SelectMany(sense => sense.Synonyms).Should().NotBeEmpty();
    }
}
