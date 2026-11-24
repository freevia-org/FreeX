using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class NumberingStartingValueMarkerTests
{
    [Theory]
    [InlineData(ListKind.Number)]
    [InlineData(ListKind.MultiLevel)]
    public void SharedLiveRendererKeepsAnExplicitZeroStartAndThenIncrements(ListKind kind)
    {
        var planner = new DocumentListMarkerSequencePlanner();
        var formatting = ParagraphFormatting.Default with { ListKind = kind, ListStartOverride = 0 };

        planner.Advance(formatting).MarkerText.Should().Be("0.");
        planner.Advance(formatting with { ListStartOverride = null }).MarkerText.Should().Be("1.");

        planner.Reset();
        planner.Advance(formatting with { ListStartOverride = null }).MarkerText.Should().Be("1.");
    }

    [Fact]
    public void MultilevelZeroIsDistinctFromAnUnusedAncestorAndDeeperLevelsReset()
    {
        var planner = new DocumentListMarkerSequencePlanner(levelTexts: ["%1)", "%1.%2)"]);
        var parent = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListStartOverride = 0 };
        var child = parent with { ListLevel = 1 };

        planner.Advance(parent).MarkerText.Should().Be("0)");
        planner.Advance(child).MarkerText.Should().Be("0.0)");
        planner.Advance(parent with { ListStartOverride = null }).MarkerText.Should().Be("1)");
        planner.Advance(child with { ListStartOverride = null }).MarkerText.Should().Be("1.1)");

        planner.Reset();
        planner.Advance(child with { ListStartOverride = null }).MarkerText.Should().Be("1.1)");
    }
}
