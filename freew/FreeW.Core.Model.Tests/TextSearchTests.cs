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

    [Theory]
    [InlineData("xcat")]
    [InlineData("catx")]
    [InlineData("_cat")]
    [InlineData("cat_")]
    [InlineData("9cat")]
    [InlineData("cat9")]
    [InlineData("\u03B2cat")]
    [InlineData("cat\u03B2")]
    [InlineData("\u0661cat")]
    [InlineData("cat\u0661")]
    public void FindAll_WholeWord_RejectsAdjacentWordCharacters(string haystack)
    {
        TextSearch.FindAll(haystack, "cat", matchCase: false, wholeWord: true).Should().BeEmpty();
    }

    [Fact]
    public void FindAll_WholeWord_AcceptsPunctuationBoundaries()
    {
        TextSearch.FindAll("cat,cat.cat/cat", "cat", matchCase: false, wholeWord: true)
            .Should().Equal((0, 3), (4, 3), (8, 3), (12, 3));
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
    [InlineData("\u03B2cat", 1, 3, false)] // Unicode letter before
    [InlineData("cat\u0661", 0, 3, false)] // Unicode digit after
    [InlineData("\u2014cat\u2014", 1, 3, true)] // Unicode punctuation boundaries
    public void IsWholeWordMatch_RespectsBoundaries(string text, int start, int length, bool expected)
    {
        TextSearch.IsWholeWordMatch(text, start, length).Should().Be(expected);
    }
}
