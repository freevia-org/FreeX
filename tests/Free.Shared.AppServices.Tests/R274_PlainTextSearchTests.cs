using FluentAssertions;
using Free.Shared.TextSearch;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r274: <c>Free.Shared.TextSearch</c> was the only production project in the repository with no test
/// referencing it at all -- found by auditing what the 114 source-scanning tests actually walk, the
/// same method that found r272's and r273's perimeter gaps.
///
/// <para>It is 62 lines and it is not incidental: <c>FreeW.App.Presentation</c>'s Find/Replace dialog
/// and its navigation pane both search through it. Every behaviour below is one its callers already
/// depend on; none of them was pinned.</para>
///
/// <para>The last two tests document policy at the edges rather than asserting a preference. Word
/// characters are decided per <c>char</c>, so a surrogate half and a combining mark both read as
/// non-word -- meaning a match beside an astral letter or an accent counts as whole-word. That may or
/// may not be what a user wants; what matters is that it is now written down and cannot change
/// silently.</para>
/// </summary>
public sealed class R274_PlainTextSearchTests
{
    [Fact]
    public void FindAll_ReturnsEveryNonOverlappingOccurrence()
    {
        PlainTextSearch.FindAll("abcabcabc", "abc", matchCase: true, wholeWord: false)
            .Should().Equal((0, 3), (3, 3), (6, 3));
    }

    /// <summary>
    /// Non-overlapping is a real choice, not an accident: "aaa" contains two overlapping "aa" but the
    /// second starts inside the first, and Find/Replace must not report it or a replace-all would
    /// corrupt the text it just wrote.
    /// </summary>
    [Fact]
    public void FindAll_DoesNotReportOverlappingMatches()
    {
        PlainTextSearch.FindAll("aaa", "aa", matchCase: true, wholeWord: false)
            .Should().Equal((0, 2));
    }

    [Fact]
    public void FindAll_EmptyNeedleYieldsNothing()
    {
        PlainTextSearch.FindAll("abc", string.Empty, matchCase: true, wholeWord: false)
            .Should().BeEmpty("an empty needle otherwise matches at every position forever");
    }

    [Fact]
    public void FindAll_NeedleLongerThanHaystackYieldsNothing() =>
        PlainTextSearch.FindAll("ab", "abc", matchCase: true, wholeWord: false).Should().BeEmpty();

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void FindAll_HonoursCaseSensitivity(bool matchCase, int expected) =>
        PlainTextSearch.FindAll("ABC", "abc", matchCase, wholeWord: false).Should().HaveCount(expected);

    [Fact]
    public void FindAll_WholeWordSkipsMatchesInsideLongerWords()
    {
        PlainTextSearch.FindAll("cat concatenate cat", "cat", matchCase: true, wholeWord: true)
            .Should().Equal((0, 3), (16, 3));
    }

    /// <summary>
    /// The skip must advance by ONE, not by the needle length: "aa" inside "aaa b aa" would otherwise
    /// step past a later valid match while rejecting an earlier invalid one.
    /// </summary>
    [Fact]
    public void FindAll_WholeWordRejectionStillFindsALaterMatch()
    {
        PlainTextSearch.FindAll("xcat cat", "cat", matchCase: true, wholeWord: true)
            .Should().Equal((5, 3));
    }

    [Theory]
    [InlineData("cat", 0, 3, true)]          // whole string
    [InlineData("cat dog", 0, 3, true)]      // followed by a space
    [InlineData("cat_dog", 0, 3, false)]     // underscore is a word character
    [InlineData("cat9", 0, 3, false)]        // digit is a word character
    [InlineData("-cat-", 1, 3, true)]        // punctuation both sides
    public void IsWholeWordMatch_TreatsLettersDigitsAndUnderscoreAsWordCharacters(
        string text, int start, int length, bool expected) =>
        PlainTextSearch.IsWholeWordMatch(text, start, length).Should().Be(expected);

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(2, 2)]
    public void IsWholeWordMatch_RejectsSpansOutsideTheText(int start, int length)
    {
        var act = () => PlainTextSearch.IsWholeWordMatch("abc", start, length);

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a span outside the text would otherwise index past the end while probing its borders");
    }

    /// <summary>
    /// Documents, rather than endorses, the astral-plane edge. A surrogate half is not a letter to
    /// <c>char.IsLetterOrDigit</c>, so a match beside a non-BMP letter reads as whole-word.
    /// </summary>
    [Fact]
    public void IsWholeWordMatch_TreatsASurrogateHalfAsANonWordCharacter()
    {
        var text = "\U0001D400cat";  // MATHEMATICAL BOLD CAPITAL A, then "cat"

        PlainTextSearch.IsWholeWordMatch(text, 2, 3).Should().BeTrue(
            "the policy is per-char, so the low surrogate before the match counts as a boundary");
    }

    /// <summary>
    /// Same for a combining mark: "caf" inside "café" written as e + U+0301 reads as a whole word,
    /// because the mark that follows is not a letter or digit.
    /// </summary>
    [Fact]
    public void IsWholeWordMatch_TreatsACombiningMarkAsANonWordCharacter()
    {
        var text = "café";

        PlainTextSearch.IsWholeWordMatch(text, 0, 4).Should().BeTrue(
            "the combining acute is a non-spacing mark, which the per-char policy treats as a boundary");
    }
}
