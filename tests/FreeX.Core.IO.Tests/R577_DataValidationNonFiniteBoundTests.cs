using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 577's finding: <see cref="DataValidationNumericBoundText.TryParse"/>
/// -- the ONE shared data-validation bound parser used by the dialog-entry gate, by live
/// enforcement (<c>FreeX.Core.Commands.DataValidationBoundsParser</c>) and by the save-side
/// normalizer <see cref="XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave"/> --
/// accepted non-finite bounds.
/// <para>
/// Two independent ways in: <see cref="double.TryParse(string?, System.Globalization.NumberStyles,
/// System.IFormatProvider?, out double)"/> returns <c>true</c> with ±Infinity on magnitude overflow
/// (it has not thrown since .NET Core), and it also accepts the culture's literal NaN/Infinity
/// symbols. The parser's own <c>Styles</c> constant constrains a bound's SPELLING (no parentheses,
/// no currency symbol) but never its SIZE, and its companion shape check
/// <c>NumericTextGroupingValidator.HasValidGroupingShape</c> says so in its own summary: "finite-value
/// policy remain the caller's responsibility" -- and it returns <c>true</c> immediately for any text
/// with no grouping separator in it at all, which "1E+400" has not.
/// </para>
/// <para>
/// The sharpest consequence is on the save path. A bound of "1E+400" parsed to +Infinity and was
/// then re-emitted through <c>ToInvariantString</c>, so FreeX wrote the literal text
/// <c>Infinity</c> into the rule's formula1/formula2 -- text Excel cannot parse back as a bound at
/// all, from a file that had been perfectly readable before the round trip. Excel itself has no
/// non-finite number: 1E+400 typed into a cell or a DV bound is rejected outright, so rejecting the
/// bound (leaving the original text untouched, which is what NormalizeNumericFormulaForSave already
/// does for anything it cannot parse) is the aligned behaviour.
/// </para>
/// </summary>
public sealed class R577_DataValidationNonFiniteBoundTests
{
    [Theory]
    [InlineData("1E+400")]
    [InlineData("-1E+400")]
    [InlineData("1E+999")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void TryParse_RejectsNonFiniteBound(string text)
    {
        DataValidationNumericBoundText.TryParse(text, out var value).Should().BeFalse(
            $"\"{text}\" is not a finite number and Excel has no such data-validation bound; " +
            $"instead it parsed to {value}");
    }

    [Theory]
    [InlineData("1E+400")]
    [InlineData("-1E+400")]
    // NOTE: "NaN" itself is deliberately NOT a case here -- its expected output IS the string
    // "NaN", so the assertion below would hold vacuously whether or not the guard exists. The
    // non-finite REJECTION for NaN is proved by TryParse_RejectsNonFiniteBound above, which
    // failed for "NaN" before the fix.
    public void NormalizeNumericFormulaForSave_NeverWritesANonFiniteLiteralToTheFile(string formula)
    {
        // Before the fix this returned "Infinity" / "NaN" -- the invariant round trip of the parsed
        // double -- which is what would have been persisted into formula1 on disk.
        var normalized = XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave(
            DvType.Decimal, formula);

        normalized.Should().Be(formula,
            "a bound the parser cannot accept must be left exactly as authored, not rewritten");
        normalized.Should().NotContain("Infinity");
    }

    [Theory]
    [InlineData("1E+30", "1E+30")]
    [InlineData("1,234", "1234")]
    [InlineData(" 42 ", "42")]
    public void NormalizeNumericFormulaForSave_StillCanonicalizesFiniteBounds(
        string formula, string expected)
    {
        // Non-vacuity: the guard must not have turned the normalizer into a no-op for the ordinary
        // finite bounds it exists to canonicalize.
        XlsxDataValidationClosedXmlMapper.NormalizeNumericFormulaForSave(DvType.Decimal, formula)
            .Should().Be(expected);
    }
}
