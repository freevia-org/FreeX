using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R82-formula-text-transform-5-1: FIXED() displayed a leading minus sign for a negative value
// that rounds to zero at the requested precision (e.g. FIXED(-0.001,2) -> "-0.00" instead of
// Excel's "0.00"), because FixedScalar passes the raw signed value into FormatRoundedNumber,
// whose "F"/"N"-formatted rounded double can be IEEE negative zero (re-acquired when
// RoundWithExcelDigits' decimal round-trips through a double cast). TEXT() and NumberFormatter
// already guard this case (IsNegativeZeroRepresentation/IsAllZeroText, NumberFormatter.cs:807/826)
// -- FormatRoundedNumber now applies the same class of guard.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Fixed_TinyNegativeRoundsToZero_SuppressesLeadingMinusSign()
    {
        _eval.Evaluate("=FIXED(-0.001,2)", MakeSheet())
            .Should().Be(new TextValue("0.00"));
    }

    [Fact]
    public void Fixed_TinyNegativeRoundsToZeroAtZeroDecimals_SuppressesLeadingMinusSign()
    {
        _eval.Evaluate("=FIXED(-0.001,0)", MakeSheet())
            .Should().Be(new TextValue("0"));
    }

    [Fact]
    public void Fixed_GenuineNegativeThatRoundsAwayFromZero_StillShowsMinusSign()
    {
        // Sibling/no-regression case: a negative value whose rounded magnitude is still
        // non-zero must keep its sign (here rounding -1.5 away from zero to -2).
        _eval.Evaluate("=FIXED(-1.5,0)", MakeSheet())
            .Should().Be(new TextValue("-2"));
    }
}
