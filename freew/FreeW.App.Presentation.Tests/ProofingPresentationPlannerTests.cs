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
    public void Proofing_language_dialog_planner_builds_shared_choices_and_selection()
    {
        var plan = ProofingLanguageDialogPlanner.Build(" fr-fr ");

        plan.Choices[0].Should().Be(new ProofingLanguageDialogChoice(
            string.Empty,
            ProofingLanguageDialogPlanner.ClearLanguageLabel));
        plan.SelectedChoice.Tag.Should().Be("fr-FR");
        plan.SelectedChoice.DisplayText.Should().Be("French (France) (fr-FR)");
    }

    [Fact]
    public void Proofing_language_dialog_planner_falls_back_to_clear_for_unknown_or_blank_tag()
    {
        ProofingLanguageDialogPlanner.Build("zz-ZZ").SelectedIndex.Should().Be(0);
        ProofingLanguageDialogPlanner.Build("").SelectedChoice.Tag.Should().BeEmpty();
    }

    [Fact]
    public void Proofing_language_dialog_planner_resolves_localized_surface()
    {
        var plan = ProofingLanguageDialogPlanner.Build("", key => $"localized:{key}");

        plan.Text.Title.Should().Be("localized:ProofingLanguage_Dialog_Title");
        plan.Text.LanguageLabel.Should().Be("localized:ProofingLanguage_Language_Label");
        plan.Text.Instruction.Should().Be("localized:ProofingLanguage_Instruction");
        plan.Choices[0].DisplayText.Should().Be("localized:ProofingLanguage_Clear_Label");
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
    public void Proofing_language_apply_planner_collapsed_caret_targets_current_proofing_word()
    {
        var plan = ProofingLanguageApplyPlanner.BuildForSelectionOrCaretWord(
            "de-DE",
            [0],
            8,
            8,
            new ProofingLanguageCaretContext(0, 8, "alpha bravo"));

        plan.LanguageTag.Should().Be("de-DE");
        plan.Ranges.Should().Equal(new ProofingLanguageTextRange(0, 6, 11));
    }

    [Fact]
    public void Proofing_language_apply_planner_collapsed_caret_without_current_word_has_no_range()
    {
        var plan = ProofingLanguageApplyPlanner.BuildForSelectionOrCaretWord(
            "fr-FR",
            [0],
            6,
            6,
            new ProofingLanguageCaretContext(0, 6, "alpha  bravo"));

        plan.LanguageTag.Should().Be("fr-FR");
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

    [Fact]
    public void Thesaurus_presentation_planner_builds_display_and_action_rows()
    {
        var entry = new ThesaurusEntry(
            "happy",
            [new ThesaurusSense("adj", ["glad_of", "cheerful"])]);

        var plan = ThesaurusPresentationPlanner.Build("Happy", entry);

        plan.SourceWord.Should().Be("Happy");
        plan.HeadingText.Should().Be("Happy");
        plan.StatusText.Should().BeEmpty();
        plan.Senses.Should().ContainSingle();
        plan.Senses[0].DisplayLabel.Should().Be("adjective");
        plan.Senses[0].Actions.Select(action => action.DisplayText)
            .Should().Equal("glad of", "cheerful");
        plan.Senses[0].Actions[0].InsertToolTip.Should().Be("Insert \"glad of\" in place of \"Happy\"");
        plan.Senses[0].Actions[0].CopyToolTip.Should().Be("Copy \"glad of\" to clipboard");
    }

    [Fact]
    public void Thesaurus_presentation_planner_reports_empty_and_missing_results()
    {
        ThesaurusPresentationPlanner.Build("", null).StatusText
            .Should().Be(ThesaurusPresentationPlanner.EmptyWordStatus);

        var plan = ThesaurusPresentationPlanner.Build("unknown", null);

        plan.HeadingText.Should().Be("unknown");
        plan.StatusText.Should().Be(ThesaurusPresentationPlanner.NoSynonymsStatus);
        plan.Senses.Should().BeEmpty();
    }
}
