using System.Diagnostics;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r397: a Find-with-wildcards pattern must never run unbounded.
///
/// <para>r287 examined this and concluded no timeout was needed, reasoning that wildcard syntax
/// cannot express a catastrophically backtracking expression: there is no grouping and no counted
/// quantifier, so the <c>(a+)+</c> shape cannot be written. That reasoning is sound but incomplete --
/// the classic exponential case needs no group at all. <c>*a*a*a*a*a*a*a*a*b</c> becomes
/// <c>.*?a.*?a...b</c>, where each wildcard can split the text many ways and the failures multiply.
/// r287's measurement used ten CONSECUTIVE stars, which collapses immediately; stars separated by
/// literals are the shape that explodes.</para>
///
/// <para>Measured on the unfixed build: six wildcards took ~370ms and eight did not finish in five
/// seconds against a 40-character string. Find runs on the UI thread, so that is a frozen window with
/// no error and nothing to cancel -- the user kills the process and loses unsaved work. The needle is
/// something they typed themselves, not a malicious file.</para>
/// </summary>
public sealed class R397_WildcardSearchCannotFreezeTheWindowTests
{
    private static TimeSpan TimeToEnumerate(string haystack, string needle)
    {
        var stopwatch = Stopwatch.StartNew();

        // On its own thread so a regression is a failed assertion rather than a hung test run.
        var worker = new Thread(() =>
        {
            foreach (var _ in TextSearch.FindAll(haystack, needle, matchCase: false, wholeWord: false, useWildcards: true))
            {
            }
        })
        { IsBackground = true };

        worker.Start();
        worker.Join(TimeSpan.FromSeconds(30));
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void APathologicalWildcardPatternIsBoundedByTheMatchTimeout(int wildcards)
    {
        var needle = string.Concat(Enumerable.Repeat("*a", wildcards)) + "*b";
        var haystack = new string('a', 40);

        // Generous against the 1s ceiling: the point is bounded-vs-unbounded, and a loaded machine
        // must not turn this into a flaky failure. Unfixed, 8 wildcards alone exceeded 5s.
        TimeToEnumerate(haystack, needle).Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "a typed wildcard pattern must not be able to freeze the window; the match timeout turns " +
            "the pathological case into 'no more matches'");
    }

    [Fact]
    public void OrdinaryWildcardSearchStillFindsItsMatches()
    {
        // The positive control: a timeout that swallowed every result would satisfy the bound above.
        var matches = TextSearch.FindAll(
            "alpha beta gamma", "b*a", matchCase: false, wholeWord: false, useWildcards: true).ToList();

        matches.Should().NotBeEmpty("wildcard search must still work; bounding it must not disable it");
        matches[0].Start.Should().Be(6, "the first match starts at 'beta'");
    }

    [Fact]
    public void TheTimeoutIsDeclaredRatherThanLeftToTheEngineDefault()
    {
        // Regex's static default is InfiniteMatchTimeout, so an explicit value is the whole guard.
        TextSearch.WildcardMatchTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        TextSearch.WildcardMatchTimeout.Should().BeLessThan(
            TimeSpan.FromSeconds(5), "the ceiling has to be short enough that a user does not call it a hang");
    }
}
