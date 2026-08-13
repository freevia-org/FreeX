using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaReferenceContainmentTests
{
    [Theory]
    [InlineData("SUM(A1:C3)", 2, 2, true)]
    [InlineData("A:A", 999, 1, true)]
    [InlineData("1:3", 3, 99, true)]
    [InlineData("Sheet2!A1", 1, 1, false)]
    [InlineData("A1+B2", 3, 3, false)]
    public void ContainsUnqualifiedCell_HandlesSupportedReferenceShapes(
        string formula,
        uint row,
        uint column,
        bool expected)
    {
        var node = new Parser(new Lexer(formula).Tokenize()).Parse();
        var cell = new CellAddress(SheetId.New(), row, column);

        FormulaReferenceContainment.ContainsUnqualifiedCell(node, cell).Should().Be(expected);
    }
}
