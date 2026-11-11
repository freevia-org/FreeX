namespace FreeW.Core.Model.Tests;

public class FindSpecialCodesTests
{
    private static List<(int Start, int Length)> Find(string haystack, string needle, TextSearchOptions options = default) =>
        TextSearch.FindAll(haystack, needle, options).ToList();

    [Theory]
    [InlineData("one\rtwo", "^p", 3, 1)]
    [InlineData("a\tb", "^t", 1, 1)]
    [InlineData("a\nb", "^l", 1, 1)]
    [InlineData("a\fb", "^m", 1, 1)]
    [InlineData("a b", "^s", 1, 1)]
    [InlineData("co­op", "^-", 2, 1)]
    [InlineData("co‑op", "^~", 2, 1)]
    [InlineData("2^3", "^^", 1, 1)]
    [InlineData("a—b", "^+", 1, 1)]
    [InlineData("a–b", "^=", 1, 1)]
    public void SingleCode_MatchesItsCharacter(string haystack, string needle, int start, int length)
    {
        Find(haystack, needle).Should().Equal((start, length));
    }

    [Fact]
    public void AnyCharacter_MatchesOneCharacterButNeverAParagraphOrCellMarkOrObject()
    {
        Find("cat cot c\rt c\u0007t c￼t", "c^?t").Should().Equal((0, 3), (4, 3));
    }

    [Fact]
    public void AnyDigit_AndAnyLetter_MatchTheirClasses()
    {
        Find("a1 b22 c", "^#").Should().Equal((1, 1), (4, 1), (5, 1));
        Find("1a2é", "^$").Should().Equal((1, 1), (3, 1));
    }

    [Fact]
    public void WhiteSpace_MatchesAWholeRunOfSpacesTabsAndNonBreakingSpaces()
    {
        Find("a \t b c", "^w").Should().Equal((1, 3), (5, 1));
    }

    [Fact]
    public void Codes_CombineWithLiteralText_AndLiteralRegexCharactersStayLiteral()
    {
        Find("end.\rNext (x)\rend.\r", ".^pNext (x)").Should().Equal((3, 10));
        Find("a.b axb", "a.b").Should().Equal((0, 3));
    }

    [Fact]
    public void DoubleParagraphMark_FindsEmptyParagraphs()
    {
        Find("one\r\rtwo\rthree", "^p^p").Should().Equal((3, 2));
    }

    [Fact]
    public void UnknownCode_IsLiteral()
    {
        Find("x^zy", "^z").Should().Equal((1, 2));
        FindSpecialCodes.ContainsFindCode("^z").Should().BeFalse();
        FindSpecialCodes.ContainsFindCode("a^pb").Should().BeTrue();
    }

    [Fact]
    public void Codes_HonourMatchCaseAndWholeWord()
    {
        Find("Tab\tx tab\tx", "tab^t", new TextSearchOptions(MatchCase: true)).Should().Equal((6, 4));
        Find("cat\t scat\t", "cat^t", new TextSearchOptions(WholeWord: true)).Should().Equal((0, 4));
    }

    [Fact]
    public void WildcardMode_DoesNotInterpretCaretCodes()
    {
        Find("a^pb a\rb", "^p", new TextSearchOptions(UseWildcards: true)).Should().Equal((1, 2));
    }

    [Fact]
    public void PlainNeedle_StillUsesTheLiteralPath()
    {
        Find("Cat cat", "cat").Should().Equal((0, 3), (4, 3));
        Find("cat category", "cat", new TextSearchOptions(WholeWord: true)).Should().Equal((0, 3));
    }

    [Fact]
    public void MatchPrefix_OnlyMatchesAtTheStartOfAWord()
    {
        Find("interact winter inter", "inter", new TextSearchOptions(MatchPrefix: true))
            .Should().Equal((0, 5), (16, 5));
    }

    [Fact]
    public void MatchSuffix_OnlyMatchesAtTheEndOfAWord()
    {
        Find("walking king kingdom", "king", new TextSearchOptions(MatchSuffix: true))
            .Should().Equal((3, 4), (8, 4));
    }

    [Fact]
    public void IgnorePunctuation_SkipsPunctuationInTheTextAndTheNeedle()
    {
        var options = new TextSearchOptions(IgnorePunctuation: true);
        Find("it's its", "its", options).Should().Equal((0, 4), (5, 3));
        Find("its", "it's", options).Should().Equal((0, 3));
        Find("its", "'", options).Should().BeEmpty();
    }

    [Fact]
    public void IgnoreWhiteSpace_SkipsSpacesTabsAndNonBreakingSpaces()
    {
        var options = new TextSearchOptions(IgnoreWhiteSpace: true);
        Find("data base database data \tbase", "database", options)
            .Should().Equal((0, 9), (10, 8), (19, 10));
        Find("database", "data base", options).Should().Equal((0, 8));
    }

    [Theory]
    [InlineData("^p", "x", "\r")]
    [InlineData("a^tb", "x", "a\tb")]
    [InlineData("^l", "x", "\n")]
    [InlineData("[^&]", "found", "[found]")]
    [InlineData("^&^&", "ab", "abab")]
    [InlineData("^^", "x", "^")]
    [InlineData("^s^-^~", "x", " ­‑")]
    [InlineData("^c", "x", "^c")]
    [InlineData("tail^", "x", "tail^")]
    [InlineData("plain", "x", "plain")]
    public void ExpandReplacement_ExpandsWordReplaceCodes(string replacement, string found, string expected)
    {
        FindSpecialCodes.ExpandReplacement(replacement, found).Should().Be(expected);
    }

    [Fact]
    public void ExpandReplacement_OfNothing_IsEmpty()
    {
        FindSpecialCodes.ExpandReplacement(null, "x").Should().BeEmpty();
    }
}
