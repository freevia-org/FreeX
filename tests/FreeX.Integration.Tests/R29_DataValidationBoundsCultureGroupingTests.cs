using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R29-localization-resx-culture-1: DataValidationBoundsParser tried the
/// current UI culture (NumberStyles.Any) before invariant, with no grouping-shape validation.
/// .NET's NumberStyles.AllowThousands parsing doesn't verify that group separators actually fall
/// on 3-digit boundaries, so under a '.'-grouping culture (e.g. de-DE) an invariant dot-decimal DV
/// bound literal like "1.5" (the OOXML/model Formula1/Formula2 text is always dot-decimal
/// regardless of UI locale — see XlsxDataValidationClosedXmlMapper, which copies the raw file text
/// verbatim) was silently misread as the grouped integer 15 by the CurrentCulture attempt, instead
/// of falling through to the InvariantCulture attempt. A rule authored as "between 1.5 and 100"
/// was silently enforced as "between 15 and 100", wrongly rejecting valid entries.
///
/// DataValidationBoundsParser is internal, so this drives the bug/fix through the public
/// DataValidationService.Validate entry point instead (the same one MainWindow.Editing.cs calls
/// on every cell edit). The fix ports the HasValidGroupingShape guard already used by
/// src/FreeX.Core.IO/DelimitedTextWorkbookReader.cs for the identical bug class.
/// </summary>
public sealed class R29_DataValidationBoundsCultureGroupingTests
{
    private static DataValidation MakeDecimalBetweenRule(string formula1, string formula2) => new()
    {
        Type = DvType.Decimal,
        Operator = DvOperator.Between,
        Formula1 = formula1,
        Formula2 = formula2,
    };

    [Fact]
    public void Validate_DeDE_InvariantDotDecimalLowerBound_AcceptsValueBetweenOneAndAHalfAndHundred()
    {
        using var _ = new CultureScope("de-DE");

        // Rule authored (or loaded from file) as "between 1.5 and 100". Before the fix, the lower
        // bound was misread as 15 under de-DE, so 5 (which is between 1.5 and 100) was wrongly
        // rejected as "must be >= 15".
        var dv = MakeDecimalBetweenRule("1.5", "100");

        DataValidationService.Validate(dv, new NumberValue(5))
            .Should().BeNull("5 is between the intended bound 1.5 and 100");
    }

    [Fact]
    public void Validate_DeDE_InvariantDotDecimalTwoDigitLowerBound_RejectsValueBelowIntendedBound()
    {
        using var _ = new CultureScope("de-DE");

        // Same bug shape as the finding's second example ("12.34" must not become 1234). Here the
        // bound is used as an upper bound, so the pre-fix mis-parse (1234 instead of 12.34) would
        // wrongly ACCEPT a value that should be rejected.
        var dv = MakeDecimalBetweenRule("1", "12.34");

        DataValidationService.Validate(dv, new NumberValue(50))
            .Should().NotBeNull("50 exceeds the intended upper bound 12.34");

        DataValidationService.Validate(dv, new NumberValue(12.34))
            .Should().BeNull("12.34 itself is within the intended inclusive upper bound");
    }

    [Fact]
    public void Validate_DeDE_GenuineGroupedIntegerBound_StillEnforcedAsGroupedInteger()
    {
        using var _ = new CultureScope("de-DE");

        // Sibling already-working case: a de-DE user directly typing a 3-digit-grouped integer
        // bound into the DV dialog ("1.234" meaning one thousand two hundred thirty-four) must
        // still be honored as 1234, not rejected as a false match for the fractional-literal guard
        // exercised above.
        var dv = MakeDecimalBetweenRule("1", "1.234");

        DataValidationService.Validate(dv, new NumberValue(1000))
            .Should().BeNull("1000 is within the intended upper bound 1234");

        DataValidationService.Validate(dv, new NumberValue(1300))
            .Should().NotBeNull("1300 exceeds the intended upper bound 1234");
    }

    [Fact]
    public void Validate_DeDE_CommaDecimalBound_StillParsesAsUserIntended()
    {
        using var _ = new CultureScope("de-DE");

        // Sibling already-working case: a de-DE user typing their own comma-decimal literal must
        // still be read correctly on the CurrentCulture attempt.
        var dv = MakeDecimalBetweenRule("1,5", "100");

        DataValidationService.Validate(dv, new NumberValue(5))
            .Should().BeNull("5 is between the intended bound 1.5 and 100");

        DataValidationService.Validate(dv, new NumberValue(1))
            .Should().NotBeNull("1 is below the intended lower bound 1.5");
    }

    [Fact]
    public void Validate_EnUS_DotDecimalAndGroupedIntegerBounds_StillEnforcedCorrectly()
    {
        using var _ = new CultureScope("en-US");

        // Sibling already-working case: en-US bounds (dot-decimal, comma-grouped) are unaffected.
        var dv = MakeDecimalBetweenRule("1.5", "1,234");

        DataValidationService.Validate(dv, new NumberValue(5))
            .Should().BeNull("5 is between the intended bound 1.5 and 1234");
        DataValidationService.Validate(dv, new NumberValue(1300))
            .Should().NotBeNull("1300 exceeds the intended upper bound 1234");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string cultureName) =>
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
