using FluentAssertions;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r456: the scoped-name sheet lookup must stay an O(1) dictionary lookup — asserted deterministically.
///
/// <para>R126 already guards this with a timing test: it builds 100 sheets and 3,000 sheets, holds the
/// scoped-name count fixed, and requires the cost ratio to stay under 4 where the old
/// <c>Sheets.FirstOrDefault</c> scan made it grow ~30x. That test is carefully built -- best-of-N on
/// both sides, a floored denominator -- and its own comments record a previous round of flakiness
/// being tuned out.</para>
///
/// <para>It still measures WALL CLOCK, so it can fail under load through no fault of the code: it
/// failed once in a full-suite run during r455's verification and passed three times out of three in
/// isolation immediately afterwards. A test that fails for reasons unrelated to the defect it guards
/// erodes the property that makes this suite worth running -- that a red is a real signal.</para>
///
/// <para>This does not replace the timing test, which can still catch an O(N) scan reached through a
/// helper this cannot see. It adds a check that cannot be affected by machine load at all, so the
/// specific regression -- reintroducing a linear scan over <c>Workbook.Sheets</c> to find the owning
/// sheet -- is pinned deterministically as well.</para>
/// </summary>
public sealed class R456_ScopedNameSheetLookupStaysConstantTimeTests
{
    // The shared locator, not a private directory walk: TestWorkspaceFileLocatorSourceGuardTests
    // forbids test sources from re-implementing the workspace walk, and caught this file's first
    // version doing exactly that. Its FindWithFailureMessage also replaces the hand-written
    // "did we find the root" assertion below with a better failure message than mine.
    private static string ReadSource() =>
        File.ReadAllText(TestWorkspaceFileLocator.FindWithFailureMessage(
            "the scoped-name rewrite source must be locatable, or this guard silently checks nothing",
            "src", "FreeX.Core.Commands", "RowColumnShiftHelpers.NamedRanges.cs"));

    [Fact]
    public void TheOwningSheetIsFoundByDictionaryLookupNotByScanning()
    {
        var source = ReadSource();

        source.Should().Contain(
            "workbook.GetSheet(sheetId)",
            "GetSheet is the O(1) dictionary lookup this path was changed to use; if it is gone, the " +
            "cost of rewriting scoped names grows with the sheet count again");
    }

    [Fact]
    public void NoLinearScanOverSheetsIsReintroduced()
    {
        var source = ReadSource();

        // The exact shapes the timing test exists to catch. Scanning Sheets to find one by id or name
        // is what made the cost grow ~30x as sheets went 100 -> 3,000.
        source.Should().NotContain("Sheets.FirstOrDefault", "that is the linear scan R126 removed");
        source.Should().NotContain("Sheets.First(", "same scan, different spelling");
        source.Should().NotContain("Sheets.Single(", "and again");
        source.Should().NotContain("Sheets.Where(", "and a filtered scan is no better");
    }

    [Fact]
    public void TheGuardIsReadingTheFileItClaimsTo()
    {
        // Without this, a moved or renamed file would make every assertion above vacuous rather than
        // failing -- the exact instrument failure this review programme keeps finding in other tests.
        var source = ReadSource();

        source.Should().Contain(
            "RewriteNamedFormulas",
            "this guard is worthless if it is reading a file that no longer holds the rewrite path");
    }
}
