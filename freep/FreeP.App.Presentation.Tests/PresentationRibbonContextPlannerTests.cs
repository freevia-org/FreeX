using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationRibbonContextPlannerTests
{
    [Fact]
    public void Build_activates_every_matching_context_in_a_mixed_selection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape { Id = 1, Kind = SlideShapeKind.AutoShape, TextBody = new TextBody() });
        slide.Shapes.Add(new SlideShape { Id = 2, Kind = SlideShapeKind.Table, Table = new TableShape() });
        slide.Shapes.Add(new SlideShape { Id = 3, Kind = SlideShapeKind.SmartArt, SmartArt = new SmartArtShape() });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        editor.Select(1);
        editor.Select(2, addToSelection: true);
        editor.Select(3, addToSelection: true);

        var state = PresentationRibbonContextPlanner.Build(editor);

        state.IsActive(PresentationRibbonContextPlanner.TextContextKey).Should().BeTrue();
        state.IsActive(PresentationRibbonContextPlanner.TableContextKey).Should().BeTrue();
        state.IsActive(PresentationRibbonContextPlanner.SmartArtContextKey).Should().BeTrue();
    }

    [Fact]
    public void Build_returns_none_for_empty_or_invalid_selection()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        PresentationRibbonContextPlanner.Build(editor).Should().Be(RibbonContextState.None);

        editor.Select(999);
        PresentationRibbonContextPlanner.Build(editor).Should().Be(RibbonContextState.None);
    }

    [Fact]
    public void AreEquivalent_ignores_unrelated_context_keys()
    {
        var left = RibbonContextState.None.With(PresentationRibbonContextPlanner.TextContextKey);
        var right = left.With("future-context");

        PresentationRibbonContextPlanner.AreEquivalent(left, right).Should().BeTrue();
    }
}
