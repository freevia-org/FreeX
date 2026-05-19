using System.Globalization;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityRoundTripTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.025)]
    [InlineData(0.5)]
    [InlineData(0.975)]
    [InlineData(0.999)]
    public void NormInv_RoundTripsThroughNormDist(double probability)
    {
        double x = Number($"=NORM.INV({D(probability)},0,1)");

        Number($"=NORM.DIST({D(x)},0,1,TRUE)").Should().BeApproximately(probability, 1e-6);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void LognormInv_RoundTripsThroughLognormDist(double probability)
    {
        double x = Number($"=LOGNORM.INV({D(probability)},3.5,1.2)");

        Number($"=LOGNORM.DIST({D(x)},3.5,1.2,TRUE)").Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void GammaInv_RoundTripsThroughGammaDist(double probability)
    {
        double x = Number($"=GAMMA.INV({D(probability)},9,1)");

        Number($"=GAMMA.DIST({D(x)},9,1,TRUE)").Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void BetaInv_RoundTripsThroughBetaDist(double probability)
    {
        double x = Number($"=BETA.INV({D(probability)},2,5)");

        Number($"=BETA.DIST({D(x)},2,5,TRUE)").Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(-512)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(511)]
    public void BinaryEngineeringConversion_RoundTripsThroughDecimal(double value)
    {
        Number($"=BIN2DEC(DEC2BIN({D(value)}))").Should().Be(value);
    }

    [Theory]
    [InlineData(-549755813888)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(549755813887)]
    public void HexEngineeringConversion_RoundTripsThroughDecimal(double value)
    {
        Number($"=HEX2DEC(DEC2HEX({D(value)}))").Should().Be(value);
    }

    [Theory]
    [InlineData(-536870912)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(536870911)]
    public void OctalEngineeringConversion_RoundTripsThroughDecimal(double value)
    {
        Number($"=OCT2DEC(DEC2OCT({D(value)}))").Should().Be(value);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 25)]
    [InlineData(255, 4096)]
    [InlineData(1048575, 65535)]
    [InlineData(281474976710655, 1)]
    public void BitwiseIdentities_RoundTripThroughXorAndAbsorption(double left, double right)
    {
        Number($"=BITXOR(BITXOR({D(left)},{D(right)}),{D(right)})").Should().Be(left);
        Number($"=BITAND({D(left)},BITOR({D(left)},{D(right)}))").Should().Be(left);
        Number($"=BITOR({D(left)},BITAND({D(left)},{D(right)}))").Should().Be(left);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(16, 3)]
    [InlineData(1024, 5)]
    [InlineData(1048576, 10)]
    public void BitShift_RoundTripsWhenShiftedBitsAreZero(double value, double shift)
    {
        Number($"=BITLSHIFT(BITRSHIFT({D(value)},{D(shift)}),{D(shift)})").Should().Be(value);
        Number($"=BITRSHIFT(BITLSHIFT({D(value)},{D(shift)}),{D(shift)})").Should().Be(value);
    }

    private double Number(string formula)
    {
        var value = _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));
        value.Should().BeOfType<NumberValue>(formula);
        return ((NumberValue)value).Value;
    }

    private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
