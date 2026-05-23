using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class BuiltInNumberFormatCatalogTests
{
    [Theory]
    [InlineData(null, "General")]
    [InlineData(0, "General")]
    [InlineData(7, "$#,##0.00")]
    [InlineData(14, "m/d/yy")]
    [InlineData(44, "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)")]
    public void TryResolveFormatCode_MapsBuiltInIds(int? numberFormatId, string expected)
    {
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            .Should().BeTrue();

        formatCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("General", null)]
    [InlineData("$#,##0.00", 7)]
    [InlineData("m/d/yy", 14)]
    [InlineData("#,##0.0 \"kg\"", null)]
    public void ResolveNumberFormatIdForCode_MapsKnownBuiltInCodes(string formatCode, int? expected)
    {
        BuiltInNumberFormatCatalog.ResolveNumberFormatIdForCode(formatCode).Should().Be(expected);
    }
}
