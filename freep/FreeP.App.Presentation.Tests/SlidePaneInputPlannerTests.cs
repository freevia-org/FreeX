using Free.Shared.Shell;

namespace FreeP.App.Compositor.Tests;

public sealed class SlidePaneInputPlannerTests
{
    [Theory]
    [InlineData("Insert", FreeKeyModifiers.None, SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide)]
    [InlineData("Delete", FreeKeyModifiers.None, SlidePaneKeyboardIntentKind.DeleteCurrentSlide)]
    [InlineData("D", FreeKeyModifiers.Control, SlidePaneKeyboardIntentKind.DuplicateCurrentSlide)]
    [InlineData("Up", FreeKeyModifiers.Alt, SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier)]
    [InlineData("Down", FreeKeyModifiers.Alt, SlidePaneKeyboardIntentKind.MoveCurrentSlideLater)]
    [InlineData("Delete", FreeKeyModifiers.Shift, SlidePaneKeyboardIntentKind.None)]
    [InlineData("D", FreeKeyModifiers.Control | FreeKeyModifiers.Shift, SlidePaneKeyboardIntentKind.None)]
    [InlineData("Up", FreeKeyModifiers.None, SlidePaneKeyboardIntentKind.None)]
    [InlineData("Down", FreeKeyModifiers.Alt | FreeKeyModifiers.Meta, SlidePaneKeyboardIntentKind.None)]
    [InlineData("A", FreeKeyModifiers.None, SlidePaneKeyboardIntentKind.None)]
    public void ResolveKeyboardIntent_RequiresTheExactChord(
        string keyName,
        FreeKeyModifiers modifiers,
        SlidePaneKeyboardIntentKind expected) =>
        SlidePaneInputPlanner.ResolveKeyboardIntent(keyName, modifiers).Should().Be(expected);

    // Two rows of two 100x50 tiles: 0,1 on the top row, 2,3 on the second row.
    private static readonly SlideSorterTileBounds[] Grid =
    [
        new(0, 0, 0, 100, 50),
        new(1, 100, 0, 100, 50),
        new(2, 0, 50, 100, 50),
        new(3, 100, 50, 100, 50),
    ];

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(60, 40, 1)]
    [InlineData(110, 10, 0)]
    [InlineData(110, 40, 1)]
    [InlineData(190, 40, 2)]
    [InlineData(60, 90, 3)]
    [InlineData(190, 90, 4)]
    [InlineData(500, 500, 4)]
    public void ResolveSlideSorterInsertionIndex_InsertsBeforeTheFirstTileTheCursorPrecedes(
        double x,
        double y,
        int expected) =>
        SlidePaneInputPlanner.ResolveSlideSorterInsertionIndex(Grid, x, y, slideCount: 4)
            .Should().Be(expected);

    [Fact]
    public void ResolveSlideSorterInsertionIndex_ScansInReadingOrderRegardlessOfInputOrder()
    {
        var shuffled = Grid.Reverse().ToArray();

        SlidePaneInputPlanner.ResolveSlideSorterInsertionIndex(shuffled, 110, 40, slideCount: 4)
            .Should().Be(1);
    }

    [Fact]
    public void BothHosts_DelegateSlidePaneInputDecisionsToThePlanner()
    {
        var wpf = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "SlidePane.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SlidePaneInputPlanner.ResolveKeyboardIntent(")
                .And.Contain("SlidePaneInputPlanner.ResolveSlideSorterInsertionIndex(")
                .And.Contain("SlideShowCustomShowDragTracker")
                .And.NotContain("SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide,")
                .And.NotContain("_slideSorterDragSourceIndex");
        }
    }

    [Fact]
    public void ResolveSlideSorterInsertionIndex_WithNoTilesAppendsAtTheSlideCount() =>
        SlidePaneInputPlanner.ResolveSlideSorterInsertionIndex([], 5, 5, slideCount: 7)
            .Should().Be(7);
}
