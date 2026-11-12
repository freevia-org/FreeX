using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class SlashLineBreakDisplayPlannerTests
{
    private const char Joiner = SlashLineBreakDisplayPlanner.WordJoiner;

    [Theory]
    [InlineData("and header/footer composition", "and header/⁠footer composition")]
    [InlineData("print/export evidence", "print/⁠export evidence")]
    [InlineData("a/b/c", "a/⁠b/⁠c")]
    public void Join_SuppressesTheBreakAfterAnInWordSolidus(string text, string expected)
    {
        SlashLineBreakDisplayPlanner.Join(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no solidus here")]
    [InlineData("1/2")]
    [InlineData("and / or")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("path/ next")]
    public void Join_LeavesOtherSolidiAlone(string text)
    {
        SlashLineBreakDisplayPlanner.Join(text).Should().Be(text);
    }

    [Theory]
    [InlineData("and header/footer composition")]
    [InlineData("a/b/c and 1/2 / x")]
    [InlineData("")]
    public void Strip_RestoresExactlyTheModelText(string text)
    {
        SlashLineBreakDisplayPlanner.Strip(SlashLineBreakDisplayPlanner.Join(text)).Should().Be(text);
    }

    [Fact]
    public void Strip_KeepsAJoinerTheAuthorTypedElsewhere()
    {
        var authored = "keep" + Joiner + "this";
        SlashLineBreakDisplayPlanner.Strip(authored).Should().Be(authored);
    }
}
