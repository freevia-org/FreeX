using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    [Theory]
    [InlineData(FormulaEditorKey.F4, FormulaEditorModifiers.None, FormulaEditorKey.None, true)]
    [InlineData(FormulaEditorKey.System, FormulaEditorModifiers.None, FormulaEditorKey.F4, true)]
    [InlineData(FormulaEditorKey.F4, FormulaEditorModifiers.Control, FormulaEditorKey.None, false)]
    [InlineData(FormulaEditorKey.F4, FormulaEditorModifiers.Shift, FormulaEditorKey.None, false)]
    [InlineData(FormulaEditorKey.F4, FormulaEditorModifiers.Alt, FormulaEditorKey.None, false)]
    public void ShouldCycleFormulaReference_RequiresPlainF4(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
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
        ExcelEditKeyPlanner.ShouldCycleFormulaReference(FormulaEditorKey.System, FormulaEditorModifiers.Alt, FormulaEditorKey.F4)
            .Should()
            .BeFalse();
    }
}
