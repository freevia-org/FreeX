using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityTrimRangeTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void TrimRange_DefaultsToTrimmingLeadingAndTrailingBlankRowsAndColumns()
    {
        var result = _eval.Evaluate("=TRIMRANGE(A1:D4)", Sheet(
                (2, 2, new NumberValue(1)),
                (2, 3, new NumberValue(2)),
                (3, 2, new NumberValue(3)),
                (3, 3, new NumberValue(4))))
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new NumberValue(1));
        result.At(1, 2).Should().Be(new NumberValue(2));
        result.At(2, 1).Should().Be(new NumberValue(3));
        result.At(2, 2).Should().Be(new NumberValue(4));
    }

    [Theory]
    [InlineData("=TRIMRANGE(A1:D4,0,3)", 4, 2)]
    [InlineData("=TRIMRANGE(A1:D4,1,3)", 3, 2)]
    [InlineData("=TRIMRANGE(A1:D4,2,3)", 3, 2)]
    [InlineData("=TRIMRANGE(A1:D4,3,0)", 2, 4)]
    [InlineData("=TRIMRANGE(A1:D4,3,1)", 2, 3)]
    [InlineData("=TRIMRANGE(A1:D4,3,2)", 2, 3)]
    public void TrimRange_HonorsRowAndColumnTrimModes(string formula, int rows, int cols)
    {
        var result = _eval.Evaluate(formula, Sheet(
                (2, 2, new TextValue("x")),
                (3, 3, new TextValue("y"))))
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(rows);
        result.ColCount.Should().Be(cols);
    }

    [Fact]
    public void TrimRange_PreservesInteriorBlanksAndErrors()
    {
        var result = _eval.Evaluate("=TRIMRANGE(A1:D4)", Sheet(
                (2, 2, new NumberValue(1)),
                (2, 3, ErrorValue.NA),
                (3, 3, new NumberValue(4))))
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new NumberValue(1));
        result.At(1, 2).Should().Be(ErrorValue.NA);
        result.At(2, 1).Should().Be(BlankValue.Instance);
        result.At(2, 2).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void TrimRange_ReturnsCalcForFullyBlankArray()
    {
        _eval.Evaluate("=TRIMRANGE(A1:B2)", Sheet()).Should().Be(ErrorValue.Calc);
    }

    [Theory]
    [InlineData("=TRIMRANGE(A1:B2,4)")]
    [InlineData("=TRIMRANGE(A1:B2,,4)")]
    public void TrimRange_ReturnsValueForInvalidModes(string formula)
    {
        _eval.Evaluate(formula, Sheet((1, 1, new NumberValue(1)))).Should().Be(ErrorValue.Value);
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);

        return sheet;
    }
}
