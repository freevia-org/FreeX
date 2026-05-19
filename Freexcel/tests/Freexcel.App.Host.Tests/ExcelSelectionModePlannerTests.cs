using System.Windows.Input;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class ExcelSelectionModePlannerTests
{
    [Theory]
    [InlineData(Key.F8, ModifierKeys.None, ExcelSelectionMode.Normal, ExcelSelectionMode.Extend)]
    [InlineData(Key.F8, ModifierKeys.None, ExcelSelectionMode.Extend, ExcelSelectionMode.Normal)]
    [InlineData(Key.F8, ModifierKeys.Shift, ExcelSelectionMode.Normal, ExcelSelectionMode.Add)]
    [InlineData(Key.F8, ModifierKeys.Shift, ExcelSelectionMode.Add, ExcelSelectionMode.Normal)]
    public void TryToggle_MapsExcelF8SelectionModes(
        Key key,
        ModifierKeys modifiers,
        ExcelSelectionMode current,
        ExcelSelectionMode expected)
    {
        var handled = ExcelSelectionModePlanner.TryToggle(key, modifiers, current, out var next);

        handled.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void TryToggle_IgnoresOtherKeys()
    {
        var handled = ExcelSelectionModePlanner.TryToggle(
            Key.F7,
            ModifierKeys.None,
            ExcelSelectionMode.Normal,
            out var next);

        handled.Should().BeFalse();
        next.Should().Be(ExcelSelectionMode.Normal);
    }

    [Theory]
    [InlineData(ExcelSelectionMode.Normal, ModifierKeys.None, false)]
    [InlineData(ExcelSelectionMode.Normal, ModifierKeys.Shift, true)]
    [InlineData(ExcelSelectionMode.Extend, ModifierKeys.None, true)]
    [InlineData(ExcelSelectionMode.Add, ModifierKeys.None, false)]
    public void ShouldExtendSelection_TreatsF8ExtendModeLikeShift(
        ExcelSelectionMode mode,
        ModifierKeys modifiers,
        bool expected)
    {
        ExcelSelectionModePlanner.ShouldExtendSelection(mode, modifiers).Should().Be(expected);
    }
}
