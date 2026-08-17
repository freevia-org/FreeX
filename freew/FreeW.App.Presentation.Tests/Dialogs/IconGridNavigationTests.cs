using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Pure index-math coverage for <see cref="IconGridNavigation"/>, the shell-neutral model both the
/// WPF and Avalonia Insert Icon dialogs wire their real KeyDown handlers to (covered end-to-end,
/// through the actual dialog, by IconPickerGridKeyboardNavigationTests in each shell's test
/// project). This file only proves the index arithmetic; it does not on its own prove either shell
/// actually calls it from a key press.
/// </summary>
public sealed class IconGridNavigationTests
{
    [Theory]
    [InlineData(0, IconGridNavigationKey.Right, 1)]
    [InlineData(3, IconGridNavigationKey.Left, 2)]
    [InlineData(2, IconGridNavigationKey.Down, 10)] // columns = 8: row-1, col-2 -> row-2, col-2
    [InlineData(10, IconGridNavigationKey.Up, 2)]
    [InlineData(5, IconGridNavigationKey.Home, 0)]
    [InlineData(5, IconGridNavigationKey.End, 19)] // itemCount = 20 in this test
    public void MovesToTheExpectedIndexForATwentyItemEightColumnGrid(
        int currentIndex, IconGridNavigationKey key, int expected)
    {
        IconGridNavigation.Move(currentIndex, key, itemCount: 20, columns: 8).Should().Be(expected);
    }

    [Fact]
    public void LeftAtTheFirstTileDoesNotWrapOrMove()
    {
        IconGridNavigation.Move(0, IconGridNavigationKey.Left, itemCount: 20, columns: 8).Should().Be(0);
    }

    [Fact]
    public void RightAtTheLastTileDoesNotWrapOrMove()
    {
        IconGridNavigation.Move(19, IconGridNavigationKey.Right, itemCount: 20, columns: 8).Should().Be(19);
    }

    [Fact]
    public void UpFromTheFirstRowDoesNotJumpToAnUnrelatedTile()
    {
        // Index 3 is in the first row (columns = 8); Up must stay put, not clamp to 0 and land on a
        // different tile than the one the user was on.
        IconGridNavigation.Move(3, IconGridNavigationKey.Up, itemCount: 20, columns: 8).Should().Be(3);
    }

    [Fact]
    public void DownPastTheLastRowDoesNotOverflowIntoAnUnrelatedTile()
    {
        // Index 18 (last row) + 8 columns = 26, past the 20-item grid; Down must stay at 18.
        IconGridNavigation.Move(18, IconGridNavigationKey.Down, itemCount: 20, columns: 8).Should().Be(18);
    }

    [Fact]
    public void ZeroOrFewerColumnsIsTreatedAsASingleColumn()
    {
        IconGridNavigation.Move(1, IconGridNavigationKey.Down, itemCount: 5, columns: 0).Should().Be(2);
    }

    [Fact]
    public void EmptyGridAlwaysReportsIndexZero()
    {
        IconGridNavigation.Move(currentIndex: 5, IconGridNavigationKey.Right, itemCount: 0, columns: 8).Should().Be(0);
    }
}
