using Free.Shared.Shell;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.F4, FreeKeyModifiers.None, FormulaEditorKey.None, true)]
    [InlineData(FormulaEditorKey.System, FreeKeyModifiers.None, FormulaEditorKey.F4, true)]
    [InlineData(FormulaEditorKey.F4, FreeKeyModifiers.Control, FormulaEditorKey.None, false)]
    [InlineData(FormulaEditorKey.F4, FreeKeyModifiers.Shift, FormulaEditorKey.None, false)]
    [InlineData(FormulaEditorKey.F4, FreeKeyModifiers.Alt, FormulaEditorKey.None, false)]
    public void ShouldCycleFormulaReference_RequiresPlainF4(
        FormulaEditorKey key,
        FreeKeyModifiers modifiers,
        FormulaEditorKey systemKey,
        bool expected)
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(key, modifiers, systemKey)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ShouldCycleFormulaReference_DoesNotTreatSystemAltF4AsFormulaReferenceCycle()
    {
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(FormulaEditorKey.System, FreeKeyModifiers.Alt, FormulaEditorKey.F4)
            .Should()
            .BeFalse();
    }
}
