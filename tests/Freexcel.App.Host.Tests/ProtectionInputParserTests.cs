using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ProtectionInputParserTests
{
    [Theory]
    [InlineData("A1:B2", true, "A1", "B2")]
    [InlineData(" B2:A1 ", true, "A1", "B2")]
    [InlineData("A1", false, "", "")]
    [InlineData("A1:B2:C3", false, "", "")]
    [InlineData("bad", false, "", "")]
    public void TryParseAllowEditRange_ParsesRangeTextWithoutThrowing(
        string input,
        bool expected,
        string expectedStart,
        string expectedEnd)
    {
        var sheetId = SheetId.New();

        var result = ProtectionInputParser.TryParseAllowEditRange(input, sheetId, out var range);

        result.Should().Be(expected);
        if (expected)
        {
            range.Start.ToA1().Should().Be(expectedStart);
            range.End.ToA1().Should().Be(expectedEnd);
            range.Start.Sheet.Should().Be(sheetId);
        }
    }
}
