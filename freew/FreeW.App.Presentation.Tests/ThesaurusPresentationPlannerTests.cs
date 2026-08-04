using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ThesaurusPresentationPlannerTests
{
    [Fact]
    public void Build_exposes_shared_insert_and_copy_action_contract()
    {
        var plan = ThesaurusPresentationPlanner.Lookup("happy");

        var action = plan.Senses.SelectMany(sense => sense.Actions)
            .First(action => action.DisplayText == "pleased");

        action.SourceWord.Should().Be("happy");
        action.RawSynonym.Should().Be("pleased");
        action.CanInsert().Should().BeTrue();
        action.InsertToolTip.Should().Be("Insert \"pleased\" in place of \"happy\"");
        action.CopyToolTip.Should().Be("Copy \"pleased\" to clipboard");
    }

    [Fact]
    public void Build_keeps_empty_word_actions_empty_and_disabled()
    {
        var action = new ThesaurusActionRow("", "", "", "", "");

        action.CanInsert().Should().BeFalse();
        ThesaurusPresentationPlanner.Lookup(null).HasSynonyms.Should().BeFalse();
    }
}
