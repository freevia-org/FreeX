using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ExcelWorksheetNavigationPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Alt, 10u)]
    [InlineData(Key.None, Key.PageDown, ModifierKeys.Alt, 10u)]
    [InlineData(Key.PageUp, Key.None, ModifierKeys.Alt, 1u)]
    [InlineData(Key.None, Key.PageUp, ModifierKeys.Alt, 1u)]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Alt | ModifierKeys.Shift, 10u)]
    public void GetHorizontalPageTarget_MapsExcelAltPageNavigation(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        uint expectedCol)
    {
        var current = new CellAddress(SheetId, 5, 4);

        var target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            key,
            systemKey,
            modifiers,
            current,
            pageSize: 6);

        target.Should().Be(new CellAddress(SheetId, 5, expectedCol));
    }

    [Fact]
    public void GetHorizontalPageTarget_IgnoresPlainPageKeys()
    {
        var current = new CellAddress(SheetId, 5, 4);

        var target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            Key.PageDown,
            Key.None,
            ModifierKeys.None,
            current,
            pageSize: 6);

        target.Should().BeNull();
    }

    [Theory]
    [InlineData(Key.End, ModifierKeys.None, false, true)]
    [InlineData(Key.End, ModifierKeys.None, true, false)]
    public void TryToggleEndMode_MapsPlainEndKey(
        Key key,
        ModifierKeys modifiers,
        bool current,
        bool expected)
    {
        var handled = ExcelWorksheetNavigationPlanner.TryToggleEndMode(key, modifiers, current, out var next);

        handled.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Right, true)]
    [InlineData(Key.Left, true)]
    [InlineData(Key.Up, true)]
    [InlineData(Key.Down, true)]
    [InlineData(Key.PageDown, false)]
    public void ShouldUseDataBoundary_TreatsEndModeArrowsLikeCtrlArrow(Key key, bool expected)
    {
        ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(
                key,
                ModifierKeys.None,
                endMode: true)
            .Should()
            .Be(expected);
    }
}
