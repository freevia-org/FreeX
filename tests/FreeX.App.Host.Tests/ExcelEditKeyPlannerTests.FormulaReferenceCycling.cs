using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(Key.F4, ModifierKeys.None, Key.None, true)]
    [InlineData(Key.System, ModifierKeys.None, Key.F4, true)]
    [InlineData(Key.F4, ModifierKeys.Control, Key.None, false)]
    [InlineData(Key.F4, ModifierKeys.Shift, Key.None, false)]
    [InlineData(Key.F4, ModifierKeys.Alt, Key.None, false)]
    public void ShouldCycleFormulaReference_RequiresPlainF4(
        Key key,
        ModifierKeys modifiers,
        Key systemKey,
        bool expected)
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(key, modifiers, systemKey)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ShouldCycleFormulaReference_DoesNotTreatSystemAltF4AsFormulaReferenceCycle()
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(Key.System, ModifierKeys.Alt, Key.F4)
            .Should()
            .BeFalse();
    }
}
