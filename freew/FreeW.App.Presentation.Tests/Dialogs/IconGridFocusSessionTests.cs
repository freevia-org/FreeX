using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class IconGridFocusSessionTests
{
    [Fact]
    public void StartsOnTheFirstTileAndResetsThere()
    {
        var session = new IconGridFocusSession();
        session.FocusedIndex.Should().Be(0);

        session.MoveTo(5, tileCount: 20).Should().BeTrue();
        session.FocusedIndex.Should().Be(5);

        session.Reset();
        session.FocusedIndex.Should().Be(0);
    }

    [Fact]
    public void PlanNavigation_ComputesTheTargetWithoutMovingTheTabStop()
    {
        var session = new IconGridFocusSession();
        session.MoveTo(2, tileCount: 20);

        session.PlanNavigation(IconGridNavigationKey.Down, tileCount: 20, columns: 8).Should().Be(10);
        session.FocusedIndex.Should().Be(2, "the caller applies the plan with MoveTo once native focus moved");
        session.PlanNavigation(IconGridNavigationKey.Up, tileCount: 20, columns: 8).Should().Be(2,
            "a key that would leave the grid keeps the focused tile");
    }

    [Fact]
    public void EmptyGrid_HandlesNothing()
    {
        var session = new IconGridFocusSession();

        session.PlanNavigation(IconGridNavigationKey.Right, tileCount: 0, columns: 8).Should().BeNull();
        session.ActivationIndex(tileCount: 0).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    public void MoveTo_RejectsIndexesOutsideTheGrid(int index)
    {
        var session = new IconGridFocusSession();
        session.MoveTo(3, tileCount: 20);

        session.MoveTo(index, tileCount: 20).Should().BeFalse();
        session.FocusedIndex.Should().Be(3);
    }

    [Fact]
    public void ActivationIndex_ClampsAfterTheGridShrinks()
    {
        var session = new IconGridFocusSession();
        session.MoveTo(15, tileCount: 20);

        session.ActivationIndex(tileCount: 4).Should().Be(3);
    }

    [Fact]
    public void BothIconPickersKeepTheirFocusStateInTheSharedSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        foreach (var path in new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Host", "IconPickerDialog.cs"),
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "IconPickerDialog.cs"),
        })
        {
            var source = File.ReadAllText(path);
            source.Should().Contain("IconGridFocusSession _focus = new();");
            source.Should().Contain("_focus.PlanNavigation(");
            source.Should().Contain("_focus.ActivationIndex(");
            source.Should().NotContain("_focusedIndex");
            source.Should().NotContain("IconGridNavigation.Move(");
        }
    }
}
