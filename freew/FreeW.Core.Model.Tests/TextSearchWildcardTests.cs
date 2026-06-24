using System.Linq;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// Pure unit tests for <see cref="TextSearch"/> wildcard support: the Word-style wildcard→regex
/// translation (<see cref="TextSearch.WildcardToRegex"/>) and the resulting find behaviour
/// (<see cref="TextSearch.FindAll"/> with <c>useWildcards: true</c>). All tests are synchronous
/// and need no WPF surface.
/// </summary>
public sealed class TextSearchWildcardTests
{
    // ── WildcardToRegex translation table ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello",        @"hello")]           // plain literal — no metachar → escaped verbatim
    [InlineData("*",            @".*?")]             // * → zero-or-more (non-greedy, mirrors Word)
    [InlineData("?",            @".")]               // ? → any single char
    [InlineData("h*",           @"h.*?")]            // leading literal + *
    [InlineData("h?llo",        @"h.llo")]           // ? in middle
    [InlineData("[abc]",        @"[abc]")]           // simple char class
    [InlineData("[a-z]",        @"[a-z]")]           // range class
    [InlineData("[!abc]",       @"[^abc]")]          // negated class [!…] → [^…]
    [InlineData("<word",        @"\b(?=\w)word")]    // < = word-start boundary
    [InlineData("word>",        @"word\b(?<=\w)")]   // > = word-end boundary
    [InlineData("<cat>",        @"\b(?=\w)cat\b(?<=\w)")] // < … > = whole word via wildcards
    [InlineData("h[ae]llo",    @"h[ae]llo")]         // class embedded in word
    [InlineData("t*[aeiou]t",  @"t.*?[aeiou]t")]    // * plus class
    [InlineData("a.b",          @"a\.b")]            // . is not a wildcard — must be escaped
    [InlineData("(test)",       @"\(test\)")]        // parens are literals
    [InlineData("a+b",          @"a\+b")]            // + is literal
    public void WildcardToRegex_ProducesExpectedPattern(string wildcard, string expectedRegex)
    {
        var actual = TextSearch.WildcardToRegex(wildcard);
        Assert.Equal(expectedRegex, actual);
    }

    // ── FindAll with useWildcards: basic matching ──────────────────────────────────────────────────

    [Fact]
    public void FindAll_Wildcard_Star_MatchesZeroOrMoreChars()
    {
        // hel*o → hel.*?o: matches "helo" (zero middle chars), "hello" (one), "helllo" (three).
        var matches = TextSearch.FindAll("helo hello helllo", "hel*o", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Equal(3, matches.Count);
        Assert.Equal((0, 4), matches[0]);   // "helo" — * matches ""
        Assert.Equal((5, 5), matches[1]);   // "hello"
        Assert.Equal((11, 6), matches[2]);  // "helllo"
    }

    [Fact]
    public void FindAll_Wildcard_QuestionMark_MatchesAnySingleChar()
    {
        var matches = TextSearch.FindAll("cat cot cut", "c?t", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void FindAll_Wildcard_CharClass_MatchesSetMembers()
    {
        var matches = TextSearch.FindAll("bat cat rat mat", "[bcr]at", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        // bat, cat, rat — but NOT mat
        Assert.Equal(3, matches.Count);
        Assert.Equal((0, 3), matches[0]);
        Assert.Equal((4, 3), matches[1]);
        Assert.Equal((8, 3), matches[2]);
    }

    [Fact]
    public void FindAll_Wildcard_NegatedClass_MatchesNonMembers()
    {
        // [!aeiou]at → a consonant followed by "at"
        var matches = TextSearch.FindAll("bat eat rat oat", "[!aeiou]at", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Equal(2, matches.Count); // bat, rat
    }

    [Fact]
    public void FindAll_Wildcard_WordBoundaryAnchors_MatchWholeWord()
    {
        // "<cat>" should match "cat" but not "concatenate"
        var matches = TextSearch.FindAll("The cat in concatenate", "<cat>", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Single(matches);
        Assert.Equal((4, 3), matches[0]);
    }

    [Fact]
    public void FindAll_Wildcard_CaseInsensitive_HonoursMatchCaseFalse()
    {
        var matches = TextSearch.FindAll("Hello HELLO hello", "hel*o", matchCase: false, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void FindAll_Wildcard_CaseSensitive_HonoursMatchCaseTrue()
    {
        var matches = TextSearch.FindAll("Hello HELLO hello", "hel*o", matchCase: true, wholeWord: false, useWildcards: true)
                                .ToList();
        Assert.Single(matches); // only lowercase "hello"
    }

    [Fact]
    public void FindAll_Wildcard_EmptyPattern_YieldsNoMatches()
    {
        var matches = TextSearch.FindAll("anything", "", matchCase: true, wholeWord: false, useWildcards: true).ToList();
        Assert.Empty(matches);
    }

    [Fact]
    public void FindAll_Wildcard_NoMatch_YieldsEmpty()
    {
        var matches = TextSearch.FindAll("hello world", "xyz*", matchCase: true, wholeWord: false, useWildcards: true).ToList();
        Assert.Empty(matches);
    }

    [Fact]
    public void FindAll_Wildcard_DotLiteral_DoesNotMatchAnyChar()
    {
        // Without wildcards, '.' matches only literal '.'; with wildcards it should still be a literal
        // because only *, ?, [, <, > are special — '.' is not a wildcard metachar.
        var matches = TextSearch.FindAll("a.b axb", "a.b", matchCase: true, wholeWord: false, useWildcards: true).ToList();
        Assert.Single(matches);
        Assert.Equal((0, 3), matches[0]);
    }

    // ── Non-wildcard path unaffected ────────────────────────────────────────────────────────────

    [Fact]
    public void FindAll_NoWildcard_StarTreatedAsLiteral()
    {
        // When useWildcards is false, '*' in the pattern is a literal and must not match "hello".
        var matches = TextSearch.FindAll("hel*lo hello", "hel*lo", matchCase: true, wholeWord: false, useWildcards: false).ToList();
        Assert.Single(matches);
        Assert.Equal((0, 6), matches[0]);
    }

    [Fact]
    public void FindAll_NoWildcard_PlainSearchStillWorks()
    {
        var matches = TextSearch.FindAll("the quick brown fox", "quick", matchCase: true, wholeWord: false, useWildcards: false).ToList();
        Assert.Single(matches);
        Assert.Equal((4, 5), matches[0]);
    }
}
