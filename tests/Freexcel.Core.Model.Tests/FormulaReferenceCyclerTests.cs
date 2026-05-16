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
    [InlineData("='Bob''s Sheet'!A1", 17, "='Bob''s Sheet'!$A$1", 1, 19)]
    [InlineData("=[Budget.xlsx]Sheet2!A1", 22, "=[Budget.xlsx]Sheet2!$A$1", 1, 24)]
    [InlineData("=Sheet1:Sheet3!A1", 16, "=Sheet1:Sheet3!$A$1", 1, 18)]
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

    [Fact]
    public void TryCycleReferenceAtCaret_DoesNotCycleStructuredReferenceColumnNames()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=SUM(Table1[A1],B2)",
            caretIndex: 12,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeFalse();
        result.Should().Be("=SUM(Table1[A1],B2)");
        selectionStart.Should().Be(12);
        selectionLength.Should().Be(0);
    }

    [Fact]
    public void TryCycleReferenceAtCaret_CyclesReferenceAfterStructuredReference()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=SUM(Table1[A1],B2)",
            caretIndex: 17,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeTrue();
        result.Should().Be("=SUM(Table1[A1],$B$2)");
        selectionStart.Should().Be(16);
        selectionLength.Should().Be(4);
    }

    [Fact]
    public void TryCycleReferenceAtCaret_DoesNotCycleA1TextInsideStringLiterals()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=A1&\"B2\"",
            caretIndex: 6,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeFalse();
        result.Should().Be("=A1&\"B2\"");
        selectionStart.Should().Be(6);
        selectionLength.Should().Be(0);
    }

    [Fact]
    public void TryCycleReferenceAtCaret_CyclesReferenceAfterStringLiteral()
    {
        var changed = FormulaReferenceCycler.TryCycleReferenceAtCaret(
            "=\"B2\"&C3",
            caretIndex: 7,
            out var result,
            out var selectionStart,
            out var selectionLength);

        changed.Should().BeTrue();
        result.Should().Be("=\"B2\"&$C$3");
        selectionStart.Should().Be(6);
        selectionLength.Should().Be(4);
    }
}
