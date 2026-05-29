using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class DataTableInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly GridRange Range = new(new CellAddress(SheetId, 3, 2), new CellAddress(SheetId, 8, 5));

    [Theory]
    [InlineData("two", true)]
    [InlineData("2", true)]
    [InlineData("one", false)]
    [InlineData("1", false)]
    [InlineData("anything", false)]
    public void IsTwoVariableMode_RecognizesTwoVariableAliases(string input, bool expected)
    {
        DataTableInputParser.IsTwoVariableMode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 3, 2)]
    public void GetDefaultFormulaCell_UsesExcelAdjacentFormulaDefaults(bool twoVariable, uint expectedRow, uint expectedCol)
    {
        DataTableInputParser.GetDefaultFormulaCell(Range, twoVariable)
            .Should().Be(new CellAddress(SheetId, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(" C5 ", true, 5, 3)]
    [InlineData("$C$5", true, 5, 3)]
    [InlineData("C$5", true, 5, 3)]
    [InlineData("$C5", true, 5, 3)]
    [InlineData("R5C3", true, 5, 3)]
    [InlineData("bad", false, 0, 0)]
    [InlineData("$C", false, 0, 0)]
    [InlineData("R0C3", false, 0, 0)]
    public void TryParseCell_ParsesTrimmedCellAddress(string input, bool expected, uint row, uint col)
    {
        var result = DataTableInputParser.TryParseCell(input, SheetId, out var address);

        result.Should().Be(expected);
        if (expected)
            address.Should().Be(new CellAddress(SheetId, row, col));
    }
}
