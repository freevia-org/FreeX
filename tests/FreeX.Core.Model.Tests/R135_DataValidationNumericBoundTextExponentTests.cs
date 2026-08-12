using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression test for round 135's finding: r134's unification of the DV numeric-bound parser
/// dropped <see cref="System.Globalization.NumberStyles.AllowExponent"/>, which the pre-r134
/// dialog-entry gate (<c>NumberStyles.Float</c>) and live-enforcement parser (<c>NumberStyles.Any</c>)
/// both had. That made a legitimate Excel DV bound like "1E+10" unparseable, and -- worse -- made
/// <see cref="DataValidationNumericBoundText.TryParse"/> unable to read back text that its own
/// sibling <see cref="DataValidationNumericBoundText.ToInvariantString"/> emits for sufficiently
/// extreme magnitudes, since <see cref="double.ToString(System.IFormatProvider?)"/> switches to
/// scientific notation once the magnitude is large or small enough.
/// </summary>
public sealed class R135_DataValidationNumericBoundTextExponentTests
{
    [Theory]
    [InlineData("1E+10", 1e10)]
    [InlineData("1e+10", 1e10)]
    [InlineData("1.5E+3", 1500)]
    [InlineData("1E-2", 0.01)]
    public void TryParse_AcceptsExponentNotation_LikeExcelDvBoundSyntax(string text, double expected)
    {
        // This is the exact shape a user can type into the Data Validation dialog's Formula1/
        // Formula2 box, and the exact shape NumberStyles.Float (the pre-r134 dialog gate) and
        // NumberStyles.Any (the pre-r134 live-enforcement parser) both accepted.
        DataValidationNumericBoundText.TryParse(text, out var value).Should().BeTrue(
            $"\"{text}\" is valid Excel DV bound syntax and was accepted before r134's unification");
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData(1e21)]
    [InlineData(1.2345e25)]
    [InlineData(1e-10)]
    public void ToInvariantString_ExtremeMagnitude_RoundTripsThroughTryParse(double original)
    {
        // ToInvariantString delegates to double.ToString(CultureInfo.InvariantCulture), which
        // switches to scientific notation once the magnitude is extreme enough (empirically
        // confirmed here -- e.g. 1e16 still formats as plain digits "10000000000000000" in this
        // runtime, but 1e21 formats as "1E+21"). The parser that reads its own formatter's output
        // back must accept that notation regardless of exactly where the runtime's threshold sits
        // -- this is the self-inconsistency the r135 finding called out: a value round-tripped
        // through save-canonicalization (ToInvariantString) could no longer be read back by the
        // very same TryParse used by live enforcement and the dialog gate.
        var formatted = DataValidationNumericBoundText.ToInvariantString(original);

        formatted.Should().Contain("E", "double's invariant ToString switches to scientific notation for sufficiently extreme magnitudes");

        DataValidationNumericBoundText.TryParse(formatted, out var roundTripped).Should().BeTrue(
            $"TryParse must be able to read back \"{formatted}\", the exact text ToInvariantString produced for it");
        roundTripped.Should().Be(original);
    }

    [Fact]
    public void TryParse_RejectsGarbageThatMerelyContainsAnE()
    {
        // Sibling/no-regression: adding AllowExponent must not turn the parser into an "anything
        // goes" parser -- plain non-numeric text (even text containing the letter E) must still
        // be rejected, exactly as it was before this fix.
        DataValidationNumericBoundText.TryParse("1E", out _).Should().BeFalse("\"1E\" has no exponent digits and is not a valid number");
        DataValidationNumericBoundText.TryParse("banana", out _).Should().BeFalse();
    }
}
