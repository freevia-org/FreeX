using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ThesaurusPaneSessionTests
{
    [Fact]
    public void Visibility_transitions_lookup_only_while_visible()
    {
        var session = new ThesaurusPaneSession();

        var hiddenRefresh = session.Refresh("happy");
        hiddenRefresh.IsVisible.Should().BeFalse();
        hiddenRefresh.ShouldRender.Should().BeFalse();
        session.CurrentWord.Should().BeEmpty();

        var shown = session.Toggle(" happy ");
        shown.IsVisible.Should().BeTrue();
        shown.VisibilityChanged.Should().BeTrue();
        shown.ShouldRender.Should().BeTrue();
        shown.DisplayPlan.HeadingText.Should().Be("happy");
        session.CurrentWord.Should().Be("happy");

        var hidden = session.Toggle("ability");
        hidden.IsVisible.Should().BeFalse();
        hidden.VisibilityChanged.Should().BeTrue();
        hidden.ShouldRender.Should().BeFalse();
        session.CurrentWord.Should().Be("happy");

        session.Show("ability").DisplayPlan.HeadingText.Should().Be("ability");
        session.Hide().IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Successful_replacement_refreshes_lookup_state()
    {
        var session = new ThesaurusPaneSession();
        session.Show("happy");

        var failed = session.CompleteReplacement(replaced: false, currentWord: "pleased");
        failed.ShouldRender.Should().BeFalse();
        session.CurrentWord.Should().Be("happy");

        var replaced = session.CompleteReplacement(replaced: true, currentWord: "pleased");
        replaced.ShouldRender.Should().BeTrue();
        replaced.DisplayPlan.HeadingText.Should().Be("pleased");
        session.CurrentWord.Should().Be("pleased");
    }

    [Fact]
    public void Action_plan_exposes_only_available_native_intents()
    {
        var session = new ThesaurusPaneSession();
        var action = ThesaurusPresentationPlanner.Lookup("happy")
            .Senses.SelectMany(sense => sense.Actions)
            .First();

        var copyOnly = session.PlanAction(action, canReplace: false, canCopy: true);
        copyOnly.CanReplace.Should().BeFalse();
        copyOnly.CopyIntent.Should().Be(new ThesaurusPaneActionIntent(
            ThesaurusPaneActionKind.Copy,
            action.DisplayText));

        var replaceOnly = session.PlanAction(action, canReplace: true, canCopy: false);
        replaceOnly.ReplaceIntent.Should().Be(new ThesaurusPaneActionIntent(
            ThesaurusPaneActionKind.Replace,
            action.DisplayText));
        replaceOnly.CanCopy.Should().BeFalse();

        var invalid = new ThesaurusActionRow("", "", "", "", "");
        session.PlanAction(invalid, canReplace: true, canCopy: true)
            .Should().Be(new ThesaurusPaneActionAvailability(null, null));
    }
}
