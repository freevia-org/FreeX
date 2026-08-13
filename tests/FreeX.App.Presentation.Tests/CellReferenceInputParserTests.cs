using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class CellReferenceInputParserTests
{
    [Theory]
    [InlineData("$B$5", 5, 2)]
    [InlineData("R5C2", 5, 2)]
    [InlineData("r5c2", 5, 2)]
    public void TryParseCell_AcceptsAbsoluteA1AndR1C1(string input, uint row, uint column)
    {
        var sheetId = SheetId.New();
        CellReferenceInputParser.TryParseCell(input, sheetId, out var address).Should().BeTrue();
        address.Should().Be(new CellAddress(sheetId, row, column));
    }

    [Theory]
    [InlineData("R0C1")]
    [InlineData("R1C0")]
    [InlineData("R1048577C1")]
    [InlineData("R1C16385")]
    [InlineData("R[1]C1")]
    public void TryParseAbsoluteR1C1Cell_RejectsRelativeAndOutOfRangeInputs(string input) =>
        CellReferenceInputParser.TryParseAbsoluteR1C1Cell(input, SheetId.New(), out _).Should().BeFalse();

    [Fact]
    public void R1C1RowAndColumnParsersShareTheSameBounds()
    {
        CellReferenceInputParser.TryParseAbsoluteR1C1Row("R1048576", out var row).Should().BeTrue();
        row.Should().Be(CellAddress.MaxRow);
        CellReferenceInputParser.TryParseAbsoluteR1C1Column("C16384", out var column).Should().BeTrue();
        column.Should().Be(CellAddress.MaxCol);
        CellReferenceInputParser.TryParseAbsoluteR1C1Row("R1048577", out _).Should().BeFalse();
        CellReferenceInputParser.TryParseAbsoluteR1C1Column("C16385", out _).Should().BeFalse();
    }
}
