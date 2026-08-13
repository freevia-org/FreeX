using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class NumberFormatSectionTokenizerTests
{
    [Theory]
    [InlineData("0;[Red]-0;0;@", "0", "[Red]-0", "0", "@")]
    [InlineData("0\";units\";[Red]-0", "0\";units\"", "[Red]-0")]
    [InlineData("[Color;42]0;0", "[Color;42]0", "0")]
    [InlineData("0\\;kg;0", "0\\;kg", "0")]
    [InlineData("0;;@", "0", "", "@")]
    public void Split_HonorsQuotesBracketsEscapesAndEmptySections(string format, params string[] expected) =>
        NumberFormatSectionTokenizer.Split(format).Should().Equal(expected);

    [Theory]
    [InlineData("0", 1)]
    [InlineData("0;[Red]-0;0;@", 4)]
    [InlineData("0\";units\";0\\;kg", 2)]
    public void Count_UsesTheSameActiveSeparatorGrammarWithoutMaterializingSections(string format, int expected) =>
        NumberFormatSectionTokenizer.Count(format).Should().Be(expected);
}
