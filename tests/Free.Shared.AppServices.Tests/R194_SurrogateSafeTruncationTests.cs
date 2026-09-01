using FluentAssertions;
using Free.Shared.IO;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r194: four sheet-name sanitizers wrote <c>name[..31]</c> to enforce Excel's 31-CHARACTER limit --
/// but the slice counts UTF-16 code units. A name whose 31st code unit fell inside a surrogate pair
/// was truncated to a trailing lone high surrogate, nothing validated it, and every subsequent save
/// to .xlsx then threw from ClosedXML's Worksheets.Add ("The surrogate pair is invalid") before
/// writing a byte. The name never changed in memory, so the document was permanently unsaveable in
/// that format.
///
/// Same class as the r193 Drop Cap fix; the sweep that generalised that finding is what surfaced it.
/// </summary>
public sealed class R194_SurrogateSafeTruncationTests
{
    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }

    [Fact]
    public void LimitToTextElements_WhenAPairStraddlesTheCap_CutsBeforeItRatherThanThroughIt()
    {
        // 30 ASCII characters then an emoji: a raw [..31] takes the high surrogate and drops the low.
        var name = new string('a', 30) + "\U0001F600";

        var limited = SurrogateSafeTruncation.LimitToTextElements(name, 31);

        HasLoneSurrogate(limited).Should().BeFalse();
        limited.Should().Be(new string('a', 30), "the emoji does not fit whole, so it is dropped whole");
        limited.Length.Should().BeLessThanOrEqualTo(31);
    }

    [Fact]
    public void LimitToTextElements_WhenAPairFitsExactly_KeepsIt()
    {
        // 29 ASCII + emoji = 31 code units exactly: it fits, so it must survive.
        var name = new string('a', 29) + "\U0001F600";
        name.Length.Should().Be(31);

        SurrogateSafeTruncation.LimitToTextElements(name, 31).Should().Be(name);
    }

    [Fact]
    public void LimitToTextElements_DoesNotSplitACombiningSequence()
    {
        var name = new string('a', 30) + "é";

        var limited = SurrogateSafeTruncation.LimitToTextElements(name, 31);

        limited.Should().Be(new string('a', 30), "the accented letter does not fit whole");
    }

    [Theory]
    [InlineData("", 31)]
    [InlineData("short", 31)]
    [InlineData("exactly-31-characters-long-abcd", 31)]
    public void LimitToTextElements_LeavesAnythingWithinTheCapAlone(string name, int cap)
    {
        SurrogateSafeTruncation.LimitToTextElements(name, cap).Should().Be(name);
    }

    [Fact]
    public void LimitToTextElements_WithNull_ReturnsEmpty()
    {
        SurrogateSafeTruncation.LimitToTextElements(null, 31).Should().BeEmpty();
    }

    [Fact]
    public void LimitToTextElements_WithACapOfZero_ReturnsEmptyRatherThanThrowing()
    {
        SurrogateSafeTruncation.LimitToTextElements("anything", 0).Should().BeEmpty();
    }

    [Fact]
    public void LimitToTextElements_NeverProducesALoneSurrogate_AtAnyCapAcrossAnAstralString()
    {
        // The general property, checked at every cut point rather than at one chosen boundary.
        var name = string.Concat(Enumerable.Repeat("a\U0001F600", 20));

        for (var cap = 0; cap <= name.Length; cap++)
        {
            var limited = SurrogateSafeTruncation.LimitToTextElements(name, cap);
            HasLoneSurrogate(limited).Should().BeFalse("cap {0} must not split a pair", cap);
            limited.Length.Should().BeLessThanOrEqualTo(cap);
        }
    }
}
