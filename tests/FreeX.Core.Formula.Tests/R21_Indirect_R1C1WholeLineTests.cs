using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-lookup-reference-deep-3: INDIRECT with R1C1-style whole-row/whole-column text
/// ("R5", "C3", "R5:R10", "C3:C7") must resolve to the corresponding row/column range
/// when a1_style=FALSE, matching real Excel, instead of falling through to #REF!.
/// </summary>
public class R21_Indirect_R1C1WholeLineTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Indirect_R1C1WholeRow_ReturnsRowRange()
    {
        var sheet = MakeSheet(
            (5, 1, new NumberValue(10)),
            (5, 2, new NumberValue(20)));

        _eval.Evaluate("=SUM(INDIRECT(\"R5\",FALSE))", sheet)
            .Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Indirect_R1C1WholeColumn_ReturnsColumnRange()
    {
        var sheet = MakeSheet(
            (1, 3, new NumberValue(7)),
            (2, 3, new NumberValue(8)));

        _eval.Evaluate("=SUM(INDIRECT(\"C3\",FALSE))", sheet)
            .Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Indirect_R1C1WholeRowRange_ReturnsAggregateRowRange()
    {
        var sheet = MakeSheet(
            (5, 1, new NumberValue(1)),
            (10, 2, new NumberValue(2)));

        _eval.Evaluate("=SUM(INDIRECT(\"R5:R10\",FALSE))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Indirect_R1C1WholeColumnRange_ReturnsAggregateColumnRange()
    {
        var sheet = MakeSheet(
            (1, 3, new NumberValue(4)),
            (2, 7, new NumberValue(5)));

        _eval.Evaluate("=SUM(INDIRECT(\"C3:C7\",FALSE))", sheet)
            .Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Indirect_R1C1RelativeWholeRow_ResolvesRelativeToCurrentCell()
    {
        var sheet = MakeSheet(
            (5, 1, new NumberValue(100)),
            (5, 2, new NumberValue(200)));

        // R[-2] from current row 7 resolves to row 5.
        _eval.Evaluate("=SUM(INDIRECT(\"R[-2]\",FALSE))", sheet, currentCell: new CellAddress(sheet.Id, 7, 1))
            .Should().Be(new NumberValue(300));
    }
}
