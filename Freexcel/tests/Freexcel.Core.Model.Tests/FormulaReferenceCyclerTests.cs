using Freexcel.Core.Commands;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class FormulaReferenceCyclerTests
{
    [Theory]
    [InlineData("=A1", 2, "=$A$1")]
    [InlineData("=$A$1", 3, "=A$1")]
    [InlineData("=A$1", 3, "=$A1")]
    [InlineData("=$A1", 3, "=A1")]
    public void TryCycleReferenceAtCaret_CyclesExcelAbsoluteReferenceModes(
        string input,
        int caretIndex,
        string expected)
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            input,
            caretIndex,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeTrue();
        result.Should().Be(expected);
        selectionStart.Should().Be(1);
        selectionLength.Should().Be(expected.Length - 1);
    }

    [Fact]
    public void TryCycleReferenceAtCaret_CyclesReferenceInsideFormula()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=SUM(A1:B2)",
            caretIndex: 6,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeTrue();
        result.Should().Be("=SUM($A$1:B2)");
        selectionStart.Should().Be(5);
        selectionLength.Should().Be(4);
    }

    [Theory]
    [InlineData("=Sheet2!A1", 9, "=Sheet2!$A$1", 1, 11)]
    [InlineData("='Sales FY26'!A1", 14, "='Sales FY26'!$A$1", 1, 17)]
    [InlineData("=[Budget.xlsx]Sheet2!A1", 22, "=[Budget.xlsx]Sheet2!$A$1", 1, 24)]
    public void TryCycleReferenceAtCaret_CyclesSheetQualifiedReferenceAsOneToken(
        string input,
        int caretIndex,
        string expected,
        int expectedSelectionStart,
        int expectedSelectionLength)
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            input,
            caretIndex,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeTrue();
        result.Should().Be(expected);
        selectionStart.Should().Be(expectedSelectionStart);
        selectionLength.Should().Be(expectedSelectionLength);
    }

    [Fact]
    public void TryCycleReferenceAtCaret_ReturnsFalseWhenCaretIsNotOnReference()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=SUM(A1)",
            caretIndex: 2,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeFalse();
        result.Should().Be("=SUM(A1)");
        selectionStart.Should().Be(2);
        selectionLength.Should().Be(0);
    }
}
