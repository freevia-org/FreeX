using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    // ── Prefix / suffix trim ──────────────────────────────────────────────────

    [Fact]
    public void Fill_RemovePrefix_TrimsFixedPrefixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Mr. Smith", "Smith"), ("Mr. Jones", "Jones")],
            ["Mr. Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddPrefix_PrependsPrefixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Mr. Smith"), ("Jones", "Mr. Jones")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Mr. Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddSuffix_AppendsSuffixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Smith Ltd"), ("Jones", "Jones Ltd")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Brown Ltd"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_RemoveFinalDottedToken_DropsVariableFileExtensions()
    {
        var result = FlashFillService.Fill(
            [("north.xlsx", "north"), ("sales.summary.csv", "sales.summary")],
            ["ops.backup.tsv", "budget.final.v2.txt"]);

        result.Should().BeEquivalentTo(["ops.backup", "budget.final.v2"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_RemoveFinalDottedToken_ReturnsNullWhenRemainingHasNoExtension()
    {
        var result = FlashFillService.Fill(
            [("north.xlsx", "north"), ("sales.summary.csv", "sales.summary")],
            ["README"]);

        result.Should().BeNull();
    }

    // ── Substring extraction ──────────────────────────────────────────────────

    [Fact]
    public void Fill_SubstringExtraction_AppliesConsistentStartAndLength()
    {
        // "ABCDE" → "BCD" means substring(1, 3)
        // "FGHIJ" → "GHI" means substring(1, 3) — same pattern
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["KLMNO"]);

        result.Should().BeEquivalentTo(["LMN"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_NoExamples_ReturnsNull()
    {
        var result = FlashFillService.Fill([], ["Bob"]);
        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SubstringPatternSourceTooShort_ReturnsNull()
    {
        // Pattern: startIndex=1, length=3 — but "AB" is only 2 chars
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["AB"]);
        result.Should().BeNull();
    }

    // ── No pattern ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_NoPattern_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Alice", "hello"), ("Bob", "world")],
            ["Carol"]);

        result.Should().BeNull();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_SingleExample_StillDetectsPattern()
    {
        // With one example we should still detect UPPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            ["bob", "carol"]);

        result.Should().BeEquivalentTo(["BOB", "CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmptyRemaining_ReturnsEmptyList()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            []);

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public void Fill_SuffixTrimPattern_TrimsFixedSuffixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Smith Ltd", "Smith"), ("Jones Ltd", "Jones")],
            ["Brown Ltd"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

}
