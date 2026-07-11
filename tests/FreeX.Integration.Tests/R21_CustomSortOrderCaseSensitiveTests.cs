using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-autofilter-sort-state-3: CustomSortOrder.Compare hardcoded StringComparison.OrdinalIgnoreCase
/// for its non-list-member tie-break, with no way for a caller to honor SortOptions.CaseSensitive
/// (Excel Sort dialog: Options &gt; Case sensitive) when a custom list ("First key sort order") is
/// also set on the key. Text values that are not members of the custom list (e.g. "Apple", "APPLE",
/// "apple") must fall back to a case-sensitive ordinal comparison when the caller asks for one —
/// exactly like SortCommand's own non-custom-list text tie-break at SortCommand.cs:751 — instead of
/// silently treating all case variants as equal and leaving them in stable/original-index order.
/// </summary>
public sealed class R21_CustomSortOrderCaseSensitiveTests
{
    private static CustomSortOrder ParseOrThrow(string order)
    {
        CustomSortOrder.TryParse(order, out var customOrder).Should().BeTrue();
        return customOrder!;
    }

    [Fact]
    public void Compare_NonListMembers_CaseSensitiveTrue_DistinguishesCaseVariants()
    {
        // Custom list that does not contain any of "Apple"/"APPLE"/"apple"/"Zeta" — mirrors the
        // finding's scenario (a "Monday,Tuesday" custom list applied to unrelated text data).
        var customOrder = ParseOrThrow("Monday,Tuesday");

        // With case sensitivity on, "Apple" and "apple" must NOT compare equal.
        customOrder.Compare("Apple", "apple", caseSensitive: true).Should().NotBe(0);
        customOrder.Compare("Apple", "APPLE", caseSensitive: true).Should().NotBe(0);

        // And the ordering must match Excel's case-sensitive ordinal order: uppercase letters sort
        // before their lowercase counterparts (StringComparison.Ordinal), so "APPLE" < "Apple" < "apple".
        customOrder.Compare("APPLE", "Apple", caseSensitive: true).Should().BeLessThan(0);
        customOrder.Compare("Apple", "apple", caseSensitive: true).Should().BeLessThan(0);
        customOrder.Compare("apple", "APPLE", caseSensitive: true).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_NonListMembers_CaseSensitiveFalse_TreatsCaseVariantsAsEqual()
    {
        var customOrder = ParseOrThrow("Monday,Tuesday");

        // Explicit case-insensitive request (Sort Options > Case sensitive left OFF) must still
        // treat "Apple"/"APPLE"/"apple" as equal, same as before this fix.
        customOrder.Compare("Apple", "apple", caseSensitive: false).Should().Be(0);
        customOrder.Compare("Apple", "APPLE", caseSensitive: false).Should().Be(0);
    }

    [Fact]
    public void Compare_DefaultOverload_StaysCaseInsensitive_ForBackwardCompatibility()
    {
        // Existing 2-argument callers (that predate the caseSensitive parameter) must keep their
        // original case-insensitive tie-break behavior unchanged.
        var customOrder = ParseOrThrow("Monday,Tuesday");

        customOrder.Compare("Apple", "apple").Should().Be(0);
        customOrder.Compare("Apple", "APPLE").Should().Be(0);
    }

    [Fact]
    public void Compare_ListMembership_IsAlwaysCaseInsensitive_RegardlessOfCaseSensitiveFlag()
    {
        // Excel's custom list membership matching is itself always case-insensitive; only the
        // *non-member tie-break* should honor caseSensitive. "monday" (list member, different case)
        // must still rank before a non-member even when caseSensitive is true.
        var customOrder = ParseOrThrow("Monday,Tuesday");

        customOrder.Compare("monday", "Zeta", caseSensitive: true).Should().BeLessThan(0);
        customOrder.Compare("MONDAY", "TUESDAY", caseSensitive: true).Should().BeLessThan(0);
    }
}
