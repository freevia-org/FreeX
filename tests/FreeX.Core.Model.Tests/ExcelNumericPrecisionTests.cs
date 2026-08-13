using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ExcelNumericPrecisionTests
{
    [Theory]
    [InlineData(1.2345678901234567, 1.23456789012346)]
    [InlineData(-1.2345678901234567, -1.23456789012346)]
    [InlineData(123456789012345678d, 123456789012345000d)]
    [InlineData(-123456789012345678d, -123456789012345000d)]
    [InlineData(123456789012345d, 123456789012345d)]
    public void CapSignificantDigits_AppliesExcelStoragePrecision(double value, double expected)
    {
        ExcelNumericPrecision.CapSignificantDigits(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(5e-200)]
    [InlineData(-5e-200)]
    [InlineData(1e-16)]
    [InlineData(double.Epsilon)]
    public void CapSignificantDigits_PreservesTinyFiniteValues(double value)
    {
        ExcelNumericPrecision.CapSignificantDigits(value).Should().Be(value);
    }

    [Fact]
    public void CapSignificantDigits_PreservesZeroSignAndNonFiniteValues()
    {
        BitConverter.DoubleToInt64Bits(ExcelNumericPrecision.CapSignificantDigits(-0d))
            .Should().Be(BitConverter.DoubleToInt64Bits(-0d));
        ExcelNumericPrecision.CapSignificantDigits(double.NaN).Should().BeNaN();
        ExcelNumericPrecision.CapSignificantDigits(double.PositiveInfinity).Should().Be(double.PositiveInfinity);
        ExcelNumericPrecision.CapSignificantDigits(double.NegativeInfinity).Should().Be(double.NegativeInfinity);
    }
}
