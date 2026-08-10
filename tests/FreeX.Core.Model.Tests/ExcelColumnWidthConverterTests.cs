using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ExcelColumnWidthConverterTests
{
    [Theory]
    [InlineData(0.5, 6)]
    [InlineData(1, 12)]
    [InlineData(8.428571428571429, 64)]
    [InlineData(20, 145)]
    public void ColumnWidthToPixels_UsesExcelPiecewiseFormula(double width, double expectedPixels)
    {
        ExcelColumnWidthConverter.ColumnWidthToPixels(width).Should().Be(expectedPixels);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Conversions_RejectInvalidAndNonPositiveInputs(double value)
    {
        ExcelColumnWidthConverter.ColumnWidthToPixels(value).Should().Be(0);
        ExcelColumnWidthConverter.PixelsToColumnWidth(value).Should().Be(0);
    }

    [Fact]
    public void PixelWidths_RoundTripAcrossTheSupportedExcelRange()
    {
        for (var pixels = 1; pixels <= ExcelColumnWidthConverter.MaximumColumnWidthPixels; pixels++)
        {
            var width = ExcelColumnWidthConverter.PixelsToColumnWidth(pixels);
            ExcelColumnWidthConverter.ColumnWidthToPixels(width).Should().Be(pixels);
        }
    }

    [Fact]
    public void PixelsToColumnWidth_ClampsAtExcelMaximum()
    {
        ExcelColumnWidthConverter.PixelsToColumnWidth(
                ExcelColumnWidthConverter.MaximumColumnWidthPixels + 100)
            .Should().Be(ExcelColumnWidthConverter.MaximumColumnWidth);
    }
}
