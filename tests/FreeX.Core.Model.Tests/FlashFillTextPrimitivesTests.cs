using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Unit tests for the pure string primitives extracted from the Flash Fill
/// single-column pattern detectors. These pin the exact behavior of each helper
/// so the maintainability extraction stays behavior-preserving.
/// </summary>
public sealed class FlashFillTextPrimitivesTests
{
    // ── ExtractDigits ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "")]
    [InlineData("12345", "12345")]
    [InlineData("(555) 867-5309", "5558675309")]
    [InlineData("a1b2c3", "123")]
    public void ExtractDigits_ReturnsDigitsInOrder(string input, string expected)
    {
        FlashFillTextPrimitives.ExtractDigits(input).Should().Be(expected);
    }

    // ── CreateDigitMask ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("12345", "#####")]
    [InlineData("(555) 867-5309", "(###) ###-####")]
    [InlineData("no digits", "no digits")]
    public void CreateDigitMask_ReplacesDigitsWithHash(string input, string expected)
    {
        FlashFillTextPrimitives.CreateDigitMask(input).Should().Be(expected);
    }

    // ── ApplyDigitMask ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("5558675309", "(###) ###-####", "(555) 867-5309")]
    [InlineData("123456", "##-##-##", "12-34-56")]
    [InlineData("42", "##", "42")]
    public void ApplyDigitMask_FillsPlaceholdersInOrder(string digits, string mask, string expected)
    {
        FlashFillTextPrimitives.ApplyDigitMask(digits, mask).Should().Be(expected);
    }

    [Fact]
    public void ApplyDigitMask_RoundTripsWithCreateDigitMaskAndExtractDigits()
    {
        const string formatted = "(202) 555-0147";
        var digits = FlashFillTextPrimitives.ExtractDigits(formatted);
        var mask = FlashFillTextPrimitives.CreateDigitMask(formatted);

        FlashFillTextPrimitives.ApplyDigitMask(digits, mask).Should().Be(formatted);
    }

    // ── ToProperCase ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("alice", "Alice")]
    [InlineData("ALICE SMITH", "Alice Smith")]
    [InlineData("mARY jane", "Mary Jane")]
    public void ToProperCase_TitleCasesInvariantly(string input, string expected)
    {
        FlashFillTextPrimitives.ToProperCase(input).Should().Be(expected);
    }

    // ── GetUpperInitial ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("alice", "A")]
    [InlineData("Bob", "B")]
    [InlineData("123", "1")]
    public void GetUpperInitial_ReturnsUpperCasedFirstChar(string input, string expected)
    {
        FlashFillTextPrimitives.GetUpperInitial(input).Should().Be(expected);
    }

    // ── TrimSegment ────────────────────────────────────────────────────────────

    [Fact]
    public void TrimSegment_AdvancesPastSurroundingWhitespace()
    {
        const string source = "  ab  ";
        var start = 0;
        var end = source.Length - 1;

        FlashFillTextPrimitives.TrimSegment(source, ref start, ref end);

        start.Should().Be(2);
        end.Should().Be(3);
    }

    [Fact]
    public void TrimSegment_AllWhitespace_CollapsesRange()
    {
        const string source = "    ";
        var start = 0;
        var end = source.Length - 1;

        FlashFillTextPrimitives.TrimSegment(source, ref start, ref end);

        (start > end).Should().BeTrue();
    }

    // ── TrimTrailingWhitespace ─────────────────────────────────────────────────

    [Fact]
    public void TrimTrailingWhitespace_RetreatsPastTrailingSpaces()
    {
        const string source = "abc   ";
        var end = source.Length - 1;

        FlashFillTextPrimitives.TrimTrailingWhitespace(source, ref end);

        end.Should().Be(2);
    }

    // ── SliceSegment ───────────────────────────────────────────────────────────

    [Fact]
    public void SliceSegment_ReturnsRequestedRange()
    {
        FlashFillTextPrimitives.SliceSegment("hello world", 6, 11).Should().Be("world");
    }

    [Fact]
    public void SliceSegment_FullRange_ReturnsSameInstance()
    {
        const string source = "hello";
        FlashFillTextPrimitives.SliceSegment(source, 0, source.Length)
            .Should().BeSameAs(source);
    }

    // ── SliceTrimmedSegment ────────────────────────────────────────────────────

    [Theory]
    [InlineData("a,  b  , c", 2, 7, "b")]
    [InlineData("x   ", 0, 4, "x")]
    [InlineData("    ", 0, 4, "")]
    public void SliceTrimmedSegment_TrimsWhitespaceWithinRange(
        string source, int start, int endExclusive, string expected)
    {
        FlashFillTextPrimitives.SliceTrimmedSegment(source, start, endExclusive)
            .Should().Be(expected);
    }

    // ── TrimmedSegmentEquals ───────────────────────────────────────────────────

    [Fact]
    public void TrimmedSegmentEquals_MatchesAfterTrimming()
    {
        const string source = "first ,  middle , last";
        // The "  middle " segment between the two commas trims to "middle".
        var commaIndex = source.IndexOf(',');
        var secondComma = source.IndexOf(',', commaIndex + 1);

        FlashFillTextPrimitives.TrimmedSegmentEquals(source, commaIndex + 1, secondComma, "middle")
            .Should().BeTrue();
    }

    [Fact]
    public void TrimmedSegmentEquals_DifferentText_ReturnsFalse()
    {
        const string source = "  abc  ";
        FlashFillTextPrimitives.TrimmedSegmentEquals(source, 0, source.Length, "xyz")
            .Should().BeFalse();
    }

    // ── HasNonEmptyPartBeforeDelimiter ─────────────────────────────────────────

    [Fact]
    public void HasNonEmptyPartBeforeDelimiter_DetectsContentBeforeDelimiter()
    {
        const string source = "file.txt";
        var dotIndex = source.IndexOf('.');

        FlashFillTextPrimitives.HasNonEmptyPartBeforeDelimiter(source, dotIndex, '.')
            .Should().BeTrue();
    }

    [Fact]
    public void HasNonEmptyPartBeforeDelimiter_OnlyDelimitersAndWhitespace_ReturnsFalse()
    {
        const string source = " . rest";
        var dotIndex = source.IndexOf('.');

        FlashFillTextPrimitives.HasNonEmptyPartBeforeDelimiter(source, dotIndex, '.')
            .Should().BeFalse();
    }
}
