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
    public void Proofing_language_apply_planner_normalizes_tag_and_single_range()
    {
        var plan = ProofingLanguageApplyPlanner.Build(" fr-FR ", [2], 3, 8);

        plan.LanguageTag.Should().Be("fr-FR");
        plan.Ranges.Should().Equal(new ProofingLanguageTextRange(2, 3, 8));
    }

    [Fact]
    public void Proofing_language_apply_planner_spans_selected_blocks_only()
    {
        var plan = ProofingLanguageApplyPlanner.Build("de-DE", [4, 7, 9], 5, 2);

        plan.Ranges.Should().Equal(
            new ProofingLanguageTextRange(4, 5, int.MaxValue),
            new ProofingLanguageTextRange(7, 0, int.MaxValue),
            new ProofingLanguageTextRange(9, 0, 2));
    }

    [Fact]
    public void Proofing_language_apply_planner_collapsed_range_has_no_selected_text()
    {
        var plan = ProofingLanguageApplyPlanner.Build("", [1], 4, 4);

        plan.LanguageTag.Should().BeNull();
        plan.HasSelectedText.Should().BeFalse();
        plan.Ranges.Should().BeEmpty();
    }

    [Fact]
    public void Shared_thesaurus_lookup_returns_known_synonyms()
    {
        var entry = ThesaurusLookup.Instance.Lookup("happy");

        entry.Should().NotBeNull();
        entry!.Senses.SelectMany(sense => sense.Synonyms).Should().NotBeEmpty();
    }
}
