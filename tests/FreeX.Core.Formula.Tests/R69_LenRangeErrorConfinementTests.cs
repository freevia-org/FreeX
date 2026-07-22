using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R69-formula-text-search-6-1: LEN over a range must confine an error to its own cell in the
// spilled array result -- matching Excel and every sibling elementwise text function (TRIM,
// UPPER, ...) -- instead of short-circuiting on the first error element and returning a bare
// scalar error for the whole array.
public sealed class R69_LenRangeErrorConfinementTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Len_RangeWithErrorCell_ConfinesErrorToItsOwnCell()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("ab"));

        var result = _eval.Evaluate("=LEN(A1:A3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(5));
        result.At(2, 1).Should().Be(ErrorValue.DivByZero);
        result.At(3, 1).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Len_RangeWithNoErrors_StillSpillsLengths_NoRegression()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hi"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("abc"));

        var result = _eval.Evaluate("=LEN(A1:A3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(5));
        result.At(2, 1).Should().Be(new NumberValue(2));
        result.At(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Len_ScalarArgument_StillReturnsSingleLength_NoRegression()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        _eval.Evaluate("=LEN(A1)", sheet).Should().Be(new NumberValue(5));
    }
}
