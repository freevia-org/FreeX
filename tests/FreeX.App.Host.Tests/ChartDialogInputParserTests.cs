using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class ChartDialogInputParserTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("auto", null)]
    [InlineData(" Auto ", null)]
    [InlineData("12.5", 12.5)]
    public void TryReadNullableDouble_AcceptsBlankAutoAndFiniteNumbers(string text, double? expected) =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadNullableDouble(Box(text), out var value).Should().BeTrue();

            value.Should().Be(expected);
        });

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("abc")]
    public void TryReadNullableDouble_RejectsNonFiniteOrInvalidNumbers(string text) =>
        StaTestRunner.Run(() =>
            ChartDialogInputParser.TryReadNullableDouble(Box(text), out _).Should().BeFalse());

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("1", true, 1.0)]
    [InlineData("0", false, null)]
    [InlineData("-1", false, null)]
    public void TryReadNullablePositiveDouble_RequiresPositiveValuesWhenPresent(string text, bool expectedResult, double? expectedValue) =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadNullablePositiveDouble(Box(text), out var value).Should().Be(expectedResult);

            if (expectedResult)
                value.Should().Be(expectedValue);
        });

    [Theory]
    [InlineData("0.5", 0.5, 10, true)]
    [InlineData("10", 0.5, 10, true)]
    [InlineData("0.49", 0.5, 10, false)]
    [InlineData("10.01", 0.5, 10, false)]
    public void TryReadClampedDouble_RequiresFiniteValueInsideRange(string text, double min, double max, bool expected) =>
        StaTestRunner.Run(() =>
            ChartDialogInputParser.TryReadClampedDouble(Box(text), min, max, out _).Should().Be(expected));

    [Fact]
    public void TryReadOptionalColor_UsesSharedHexColorRules() =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadOptionalColor(Box("#102030"), out var color).Should().BeTrue();

            color.Should().Be(new CellColor(0x10, 0x20, 0x30));
        });

    private static TextBox Box(string text) => new() { Text = text };
}
