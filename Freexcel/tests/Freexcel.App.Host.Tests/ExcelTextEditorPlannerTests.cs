using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ExcelTextEditorPlannerTests
{
    [Fact]
    public void InsertLineBreak_ReplacesSelectionAndMovesCaretAfterNewLine()
    {
        var edit = ExcelTextEditorPlanner.InsertLineBreak("abXYcd", selectionStart: 2, selectionLength: 2, "\r\n");

        edit.Text.Should().Be("ab\r\ncd");
        edit.SelectionStart.Should().Be(4);
        edit.SelectionLength.Should().Be(0);
    }

    [Fact]
    public void TryCycleFormulaReference_CyclesReferenceTouchedByCaret()
    {
        ExcelTextEditorPlanner.TryCycleFormulaReference("=A1+B1", caretIndex: 2, out var edit)
            .Should()
            .BeTrue();

        edit.Text.Should().Be("=$A$1+B1");
        edit.SelectionStart.Should().Be(1);
        edit.SelectionLength.Should().Be(4);
    }

    [Fact]
    public void TryCycleFormulaReference_IgnoresNonFormulaText()
    {
        ExcelTextEditorPlanner.TryCycleFormulaReference("A1", caretIndex: 1, out _)
            .Should()
            .BeFalse();
    }
}
