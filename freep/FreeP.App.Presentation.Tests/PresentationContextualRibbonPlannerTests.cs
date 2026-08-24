using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationContextualRibbonPlannerTests
{
    [Fact]
    public void ContextKeysAreNeutralAndStable()
    {
        PresentationContextualRibbonPlanner.TextContextKey.Should().Be("text");
        PresentationContextualRibbonPlanner.TableContextKey.Should().Be("table");
        PresentationContextualRibbonPlanner.SmartArtContextKey.Should().Be("smartart");
    }

    [Fact]
    public void ContextSourcePublishesOnlyChangedStates()
    {
        var source = new PresentationRibbonContextSource();
        var changes = 0;
        source.ContextChanged += (_, _) => changes++;

        source.Apply(RibbonContextState.None);
        source.Apply(RibbonContextState.None.With(PresentationContextualRibbonPlanner.TextContextKey));

        changes.Should().Be(1);
        source.Current.IsActive(PresentationContextualRibbonPlanner.TextContextKey).Should().BeTrue();
    }
}
