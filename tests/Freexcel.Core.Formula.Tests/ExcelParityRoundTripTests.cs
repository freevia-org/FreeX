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
    [InlineData(0.1, 3)]
    [InlineData(0.5, 10)]
    [InlineData(0.9, 30)]
    public void TInv_RoundTripsThroughTDist(double probability, double degreesFreedom)
    {
        double x = Number($"=T.INV({D(probability)},{D(degreesFreedom)})");

        Number($"=T.DIST({D(x)},{D(degreesFreedom)},TRUE)").Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(0.1, 5, 10)]
    [InlineData(0.5, 8, 12)]
    [InlineData(0.9, 12, 20)]
    public void FInv_RoundTripsThroughFDist(double probability, double degFreedom1, double degFreedom2)
    {
        double x = Number($"=F.INV({D(probability)},{D(degFreedom1)},{D(degFreedom2)})");

        Number($"=F.DIST({D(x)},{D(degFreedom1)},{D(degFreedom2)},TRUE)")
            .Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(0.1, 2)]
    [InlineData(0.5, 5)]
    [InlineData(0.9, 12)]
    public void ChiSqInv_RoundTripsThroughChiSqDist(double probability, double degreesFreedom)
    {
        double x = Number($"=CHISQ.INV({D(probability)},{D(degreesFreedom)})");

        Number($"=CHISQ.DIST({D(x)},{D(degreesFreedom)},TRUE)").Should().BeApproximately(probability, 1e-5);
    }

    [Theory]
    [InlineData(-512)]
    [InlineData(-255)]
    [InlineData(-128)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(255)]
    [InlineData(511)]
    public void BinaryEngineeringConversion_RoundTripsThroughDecimal(double value)
    {
        Number($"=BIN2DEC(DEC2BIN({D(value)}))").Should().Be(value);
    }

    [Theory]
    [InlineData(-549755813888)]
    [InlineData(-65536)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(255)]
    [InlineData(65535)]
    [InlineData(549755813887)]
    public void HexEngineeringConversion_RoundTripsThroughDecimal(double value)
    {
        Number($"=HEX2DEC(DEC2HEX({D(value)}))").Should().Be(value);
    }

    [Theory]
    [InlineData(-536870912)]
    [InlineData(-4096)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(4095)]
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

    [Fact]
    public void Property_DistributionInversePairs_RoundTripAcrossRepresentativeProbabilities()
    {
        foreach (var probability in new[] { 0.01, 0.05, 0.25, 0.5, 0.75, 0.95, 0.99 })
        {
            var p = D(probability);
            var normX = Number($"=NORM.INV({p},2,3)");
            Number($"=NORM.DIST({D(normX)},2,3,TRUE)").Should().BeApproximately(probability, 1e-6);

            var gammaX = Number($"=GAMMA.INV({p},4,2)");
            Number($"=GAMMA.DIST({D(gammaX)},4,2,TRUE)").Should().BeApproximately(probability, 1e-5);
        }
    }

    [Fact]
    public void Property_EngineeringBaseConversions_RoundTripAcrossBoundaries()
    {
        foreach (var value in new[] { -512d, -129d, -1d, 0d, 1d, 127d, 255d, 511d })
            Number($"=BIN2DEC(DEC2BIN({D(value)}))").Should().Be(value);

        foreach (var value in new[] { -536870912d, -1024d, -1d, 0d, 1d, 1024d, 536870911d })
            Number($"=OCT2DEC(DEC2OCT({D(value)}))").Should().Be(value);

        foreach (var value in new[] { -549755813888d, -65535d, -1d, 0d, 1d, 65535d, 549755813887d })
            Number($"=HEX2DEC(DEC2HEX({D(value)}))").Should().Be(value);
    }

    private double Number(string formula)
    {
        var value = _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));
        value.Should().BeOfType<NumberValue>(formula);
        return ((NumberValue)value).Value;
    }

    private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
