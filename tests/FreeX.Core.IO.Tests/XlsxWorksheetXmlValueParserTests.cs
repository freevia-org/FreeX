using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetXmlValueParserTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTruthy_UsesOpenXmlBooleanSemantics(string? value, bool expected)
    {
        XlsxWorksheetXmlValueParser.IsTruthy(value).Should().Be(expected);
    }
}
