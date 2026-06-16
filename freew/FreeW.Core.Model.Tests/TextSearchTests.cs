namespace FreeW.Core.Model.Tests;

public class TextSearchTests
{
    [Fact]
    public void FindAll_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var hits = TextSearch.FindAll("Cat cat CAT", "cat", matchCase: false, wholeWord: false);

        hits.Should().Equal((0, 3), (4, 3), (8, 3));
    }

    [Fact]
    public void FindAll_CaseSensitive_MatchesOnlyExactCase()
    {
        var hits = TextSearch.FindAll("Cat cat CAT", "cat", matchCase: true, wholeWord: false);

        hits.Should().Equal((4, 3));
    }

    [Fact]
    public void FindAll_WholeWord_ExcludesSubstrings()
    {
        var hits = TextSearch.FindAll("cat category scatter cat", "cat", matchCase: false, wholeWord: true);

        // Only the two standalone "cat" words match; "category" and "scatter" are excluded.
        hits.Should().Equal((0, 3), (21, 3));
    }

    [Fact]
    public void FindAll_SubstringMode_IncludesEmbeddedMatches()
    {
        var hits = TextSearch.FindAll("cat category scatter", "cat", matchCase: false, wholeWord: false);

        hits.Should().Equal((0, 3), (4, 3), (14, 3));
    }

    [Fact]
    public void FindAll_WholeWord_HonoursUnderscoreAndDigitBoundaries()
    {
        // Underscore and digits are word characters, so neither of these is a whole-word "cat".
        TextSearch.FindAll("cat_dog", "cat", matchCase: false, wholeWord: true).Should().BeEmpty();
        TextSearch.FindAll("cat9", "cat", matchCase: false, wholeWord: true).Should().BeEmpty();

        // Punctuation is a boundary, so this one matches.
        TextSearch.FindAll("(cat)", "cat", matchCase: false, wholeWord: true).Should().Equal((1, 3));
    }

    [Fact]
    public void FindAll_OverlappingCandidates_AreReportedNonOverlapping()
    {
        var hits = TextSearch.FindAll("aaaa", "aa", matchCase: false, wholeWord: false);

        hits.Should().Equal((0, 2), (2, 2));
    }

    [Fact]
    public void FindAll_NoMatch_YieldsNothing()
    {
        TextSearch.FindAll("hello world", "xyz", matchCase: false, wholeWord: false).Should().BeEmpty();
    }

    [Fact]
    public void FindAll_EmptyNeedle_YieldsNothing()
    {
        TextSearch.FindAll("anything", string.Empty, matchCase: false, wholeWord: false).Should().BeEmpty();
    }

    [Fact]
    public void FindAll_EmptyHaystack_YieldsNothing()
    {
        TextSearch.FindAll(string.Empty, "cat", matchCase: false, wholeWord: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData("cat", 0, 3, true)]      // whole string
    [InlineData(" cat ", 1, 3, true)]    // spaces both sides
    [InlineData("cat.", 0, 3, true)]     // punctuation after, edge before
    [InlineData("xcat", 1, 3, false)]    // word char before
    [InlineData("catx", 0, 3, false)]    // word char after
    [InlineData("_cat", 1, 3, false)]    // underscore before
    public void IsWholeWordMatch_RespectsBoundaries(string text, int start, int length, bool expected)
    {
        TextSearch.IsWholeWordMatch(text, start, length).Should().Be(expected);
    }
}
