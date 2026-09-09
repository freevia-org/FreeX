using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 581: <c>NormalizeNumericCfvoValueForSave</c> is the single write choke point for a
/// conditional-format threshold — its own doc comment says so, and forbids formatting a threshold
/// inline anywhere else. It parses the stored value and re-emits it with
/// <c>ToString(CultureInfo.InvariantCulture)</c> to fix a comma-decimal defect (r145).
/// <para>
/// That round trip through a <c>double</c> is the same shape r577 found in
/// <c>DataValidationNumericBoundText</c>: a threshold of "1E+400" parses to Infinity and is written
/// back as the literal text <c>Infinity</c> — not a number the cfvo schema can express, in a file
/// that was readable before FreeX saved it. Two features, one shape, and nothing in either file
/// points at the other.
/// </para>
/// <para>
/// The method already has the right answer for this case in its own words: "not a plain number we
/// recognize (unexpected/malformed input) -- write through untouched rather than risk corrupting a
/// value we don't understand". A non-finite value is exactly that.
/// </para>
/// </summary>
public sealed class R581_CfvoNonFiniteThresholdTests
{
    [Theory]
    [InlineData("1E+400")]
    [InlineData("-1E+400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void NonFiniteThreshold_IsWrittenThroughUntouched(string value)
    {
        var normalized = XlsxAdvancedConditionalFormatWriter.NormalizeNumericCfvoValueForSave(
            CfThresholdType.Number, value);

        normalized.Should().Be(value, "an unrecognized threshold is written through, not rewritten");
    }

    [Fact]
    public void OverflowingThreshold_NeverBecomesTheTextInfinity()
    {
        // The sharp form of the same assertion: before the fix this returned "Infinity".
        XlsxAdvancedConditionalFormatWriter.NormalizeNumericCfvoValueForSave(
            CfThresholdType.Number, "1E+400").Should().NotContain("Infinity");
    }

    [Theory]
    [InlineData(CfThresholdType.Number, "12.5", "12.5")]
    [InlineData(CfThresholdType.Percent, "50", "50")]
    [InlineData(CfThresholdType.Percentile, "1E+30", "1E+30")]
    public void FiniteThresholds_StillNormalize(CfThresholdType type, string value, string expected)
    {
        // Non-vacuity: the guard must not have turned the normalizer into a pass-through for the
        // ordinary thresholds it exists to canonicalize.
        XlsxAdvancedConditionalFormatWriter.NormalizeNumericCfvoValueForSave(type, value)
            .Should().Be(expected);
    }
}
