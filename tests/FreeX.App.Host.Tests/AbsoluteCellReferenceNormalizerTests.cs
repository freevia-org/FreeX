using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host.Tests;

public sealed class AbsoluteCellReferenceNormalizerTests
{
    [Theory]
    [InlineData("A1", "A1")]
    [InlineData(" $A$1 ", "A1")]
    [InlineData("$A1", "A1")]
    [InlineData("A$1", "A1")]
    [InlineData("$XFD$1048576", "XFD1048576")]
    public void Normalize_RemovesOptionalAbsoluteMarkers(string input, string expected)
    {
        AbsoluteCellReferenceNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("$A")]
    [InlineData("1")]
    [InlineData("A$1$")]
    [InlineData("R1C1")]
    [InlineData("Sheet1!$A$1")]
    [InlineData("$A$1:$B$2")]
    [InlineData("'[Book.xlsx]Sheet 1'!$A$1")]
    public void Normalize_RejectsMalformedA1References(string input)
    {
        AbsoluteCellReferenceNormalizer.Normalize(input).Should().BeNull();
    }
}
