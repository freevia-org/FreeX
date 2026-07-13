using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-40 finding R40-commands-autofill-flashfill-3-1: Flash Fill with
/// exactly one training example used to fall back to a fixed positional split (locking in a
/// column index from the single example) instead of a token-relative "last token" extraction,
/// producing wrong values on rows with a different token count. Excel generalizes a single
/// example as "take the last whitespace-delimited token".
/// </summary>
public sealed class R40_FlashFillSingleExampleLastTokenTests
{
    [Fact]
    public void Fill_SingleExample_LastNameExtraction_UsesLastTokenNotFixedPosition()
    {
        // One example: "John Smith" -> "Smith" (2 tokens, last token happens to be index 1).
        // The remaining rows have a DIFFERENT token count, so a fixed positional split (index 1)
        // would wrongly pick the middle name instead of the last name.
        var result = FlashFillService.Fill(
            [("John Smith", "Smith")],
            ["Mary Jane Watson", "Robert Brown"]);

        result.Should().BeEquivalentTo(["Watson", "Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SingleExample_LastNameExtraction_ThreeTokenExample_StillGeneralizesToLastToken()
    {
        // Sibling case: the one example itself has 3 tokens, confirming the fix isn't special-
        // cased to 2-token examples -- it must generalize "last token" from any single example.
        var result = FlashFillService.Fill(
            [("Mary Jane Watson", "Watson")],
            ["John Smith", "Robert Lee Brown"]);

        result.Should().BeEquivalentTo(["Smith", "Brown"], o => o.WithStrictOrdering());
    }

    // ── No-regression sibling: multi-example ambiguity guard is preserved. ─────────────────

    [Fact]
    public void Fill_TwoExamples_WithIdenticalFirstToken_StillDefersWhenAmbiguous()
    {
        // With 2+ examples sharing the same first token, "last token" is ambiguous with a
        // fixed-prefix-removal pattern; the delimiter/prefix based patterns should still be free
        // to take over exactly as before this fix (last-token extraction must not fire here in a
        // way that breaks pre-existing multi-example disambiguation).
        var result = FlashFillService.Fill(
            [("Mr Smith", "Smith"), ("Mr Jones", "Jones")],
            ["Mr Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }
}
