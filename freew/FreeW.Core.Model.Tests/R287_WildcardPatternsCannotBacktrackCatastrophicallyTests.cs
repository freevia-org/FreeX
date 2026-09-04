using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r287: FreeW compiles the user's Find pattern into a <see cref="Regex"/> with no match timeout, so
/// the only thing standing between a typed pattern and a frozen window is that wildcard syntax cannot
/// express a catastrophically backtracking expression.
///
/// <para>It cannot, and that is a structural property rather than luck: every character outside a
/// bracket class goes through <c>Regex.Escape</c>, and the syntax has no grouping construct, so a
/// nested quantifier -- the <c>(a+)+</c> shape that makes backtracking explode -- has no way to be
/// written. Measured as well as read: ten consecutive <c>*</c> against a 400-character
/// non-matching string completes in single-digit milliseconds.</para>
///
/// <para>These tests pin the property, not the implementation. The risk they guard is a future
/// "richer wildcards" change that passes more syntax through and quietly removes the reason the
/// missing timeout is survivable.</para>
/// </summary>
public sealed class R287_WildcardPatternsCannotBacktrackCatastrophicallyTests
{
    /// <summary>
    /// Grouping is the ingredient a nested quantifier needs. If a needle can ever produce an
    /// unescaped '(' outside a bracket class, this property is gone.
    [Theory]
    [InlineData("(a+)+b")]
    [InlineData("(x|x)*y")]
    [InlineData("a{1,9999}")]
    [InlineData("^(a|b)+$")]
    public void RegexSyntaxInANeedleIsTreatedAsLiteralText(string needle)
    {
        var translated = TextSearch.WildcardToRegex(needle);

        // Checked without a regex on purpose: expressing "an unescaped bracket" as a pattern needs
        // its own escaping, and getting THAT wrong is how the first draft of this test failed.
        UnescapedOccurrences(translated, '(').Should().Be(0,
            $"an unescaped group in '{translated}' would let a typed pattern build a nested "
            + "quantifier, which is the shape that makes backtracking explode");
        UnescapedOccurrences(translated, '{').Should().Be(0,
            "an unescaped counted quantifier is the other way to make the engine explore");
    }

    /// <summary>
    /// Counts occurrences of <paramref name="character"/> that are not preceded by an odd number of
    /// backslashes -- i.e. the ones the regex engine would read as syntax rather than as text.
    /// </summary>
    private static int UnescapedOccurrences(string pattern, char character)
    {
        var count = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != character)
                continue;

            var backslashes = 0;
            for (var j = i - 1; j >= 0 && pattern[j] == '\\'; j--)
                backslashes++;

            if (backslashes % 2 == 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// The translated pattern must still be a pattern the engine accepts -- a guard that escapes
    /// everything into nonsense would pass the test above while breaking search.
    /// </summary>
    [Theory]
    [InlineData("(a+)+b")]
    [InlineData("plain")]
    [InlineData("a*b?c")]
    [InlineData("[abc]d")]
    [InlineData("[!abc]d")]
    public void TheTranslatedPatternIsAValidRegex(string needle)
    {
        var act = () => Regex.Match("sample text", TextSearch.WildcardToRegex(needle));

        act.Should().NotThrow();
    }

    /// <summary>
    /// Wildcards still have to work. Escaping is only safe if it escapes the right things.
    /// </summary>
    [Theory]
    [InlineData("h*o", "hello", true)]
    [InlineData("h?llo", "hello", true)]
    [InlineData("h?o", "hello", false)]
    [InlineData("(a+)+b", "hello", false)]
    [InlineData("(a+)+b", "(a+)+b", true)]
    public void WildcardSearchStillMatchesWhatItShould(string needle, string haystack, bool expected) =>
        TextSearch.FindAll(haystack, needle, matchCase: true, wholeWord: false, useWildcards: true)
            .Any().Should().Be(expected);

    /// <summary>
    /// The measurement, kept as a test. Generous bound: the point is to catch a change that turns
    /// milliseconds into minutes, not to police normal variation on a busy machine.
    /// </summary>
    [Fact]
    public void ManyConsecutiveWildcardsDoNotBlowUp()
    {
        var haystack = new string('a', 400);
        var needle = new string('*', 10) + "z";

        var stopwatch = Stopwatch.StartNew();
        TextSearch.FindAll(haystack, needle, matchCase: true, wholeWord: false, useWildcards: true)
            .Any().Should().BeFalse();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the Find regex carries no match timeout, so a pattern that backtracks freezes the "
            + "window with no way for the user to cancel it");
    }
}
