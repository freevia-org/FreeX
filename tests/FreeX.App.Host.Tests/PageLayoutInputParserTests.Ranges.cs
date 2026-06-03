using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PageLayoutInputParserTests
{
    [Theory]
    [InlineData("1:2", true, 1, 2)]
    [InlineData("$1:$2", true, 1, 2)]
    [InlineData("R1:R2", true, 1, 2)]
    [InlineData("4", true, 4, 4)]
    [InlineData("$4", true, 4, 4)]
    [InlineData("R4", true, 4, 4)]
    [InlineData("5:3", true, 3, 5)]
    [InlineData("R5:R3", true, 3, 5)]
    [InlineData("1048576", true, 1048576, 1048576)]
    [InlineData("none", true, null, null)]
    [InlineData("0:2", false, null, null)]
    [InlineData("R0:R2", false, null, null)]
    [InlineData("1048577", false, null, null)]
    [InlineData("R1048577", false, null, null)]
    [InlineData("1:1048577", false, null, null)]
    [InlineData("A:C", false, null, null)]
    public void TryParseRepeatRows_ParsesExcelStyleRowRanges(string input, bool expected, int? expectedStart, int? expectedEnd)
    {
        var result = PageLayoutInputParser.TryParseRepeatRows(input, out var range);

        result.Should().Be(expected);
        AssertRange(range, expectedStart, expectedEnd);
    }

    [Theory]
    [InlineData("A:C", true, 1, 3)]
    [InlineData("$A:$C", true, 1, 3)]
    [InlineData("C1:C3", true, 1, 3)]
    [InlineData("D", true, 4, 4)]
    [InlineData("$D", true, 4, 4)]
    [InlineData("C4", true, 4, 4)]
    [InlineData("C:A", true, 1, 3)]
    [InlineData("C3:C1", true, 1, 3)]
    [InlineData("XFD", true, 16384, 16384)]
    [InlineData("clear", true, null, null)]
    [InlineData("1:2", false, null, null)]
    [InlineData("C0:C2", false, null, null)]
    [InlineData("XFE", false, null, null)]
    [InlineData("C16385", false, null, null)]
    [InlineData("A:XFE", false, null, null)]
    [InlineData("A:B:C", false, null, null)]
    public void TryParseRepeatColumns_ParsesExcelStyleColumnRanges(string input, bool expected, int? expectedStart, int? expectedEnd)
    {
        var result = PageLayoutInputParser.TryParseRepeatColumns(input, out var range);

        result.Should().Be(expected);
        AssertRange(range, expectedStart, expectedEnd);
    }

    [Theory]
    [InlineData("$A$1:$C$10", true, 1, 1, 10, 3)]
    [InlineData("R1C1:R10C3", true, 1, 1, 10, 3)]
    [InlineData("A$1:$C10", true, 1, 1, 10, 3)]
    [InlineData("$B$2", true, 2, 2, 2, 2)]
    [InlineData("R2C2", true, 2, 2, 2, 2)]
    [InlineData("", true, null, null, null, null)]
    [InlineData("$XFE$1:$XFE$2", false, null, null, null, null)]
    [InlineData("R1C16385:R2C16385", false, null, null, null, null)]
    [InlineData("$A$0:$B$2", false, null, null, null, null)]
    [InlineData("R0C1:R2C2", false, null, null, null, null)]
    [InlineData("$A:$B", false, null, null, null, null)]
    [InlineData("A$1$:B$2", false, null, null, null, null)]
    [InlineData("R[1]C1:R2C2", false, null, null, null, null)]
    public void TryParseOptionalPrintArea_ParsesExcelAbsoluteCellRanges(
        string input,
        bool expected,
        int? expectedStartRow,
        int? expectedStartColumn,
        int? expectedEndRow,
        int? expectedEndColumn)
    {
        var sheetId = SheetId.New();

        var result = PageLayoutInputParser.TryParseOptionalPrintArea(input, sheetId, out var range);

        result.Should().Be(expected);
        if (expectedStartRow is null)
        {
            range.Should().BeNull();
            return;
        }

        range.Should().NotBeNull();
        range!.Value.Start.Should().Be(new CellAddress(sheetId, (uint)expectedStartRow.Value, (uint)expectedStartColumn!.Value));
        range.Value.End.Should().Be(new CellAddress(sheetId, (uint)expectedEndRow!.Value, (uint)expectedEndColumn!.Value));
    }
}
