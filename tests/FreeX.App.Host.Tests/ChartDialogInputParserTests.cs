using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class ChartDialogInputParserTests
{
    [Fact]
    public void TryReadNullableDouble_DelegatesTextBoxTextToSharedParser() =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadNullableDouble(Box("12.5"), out var value).Should().BeTrue();

            value.Should().Be(12.5);
        });

    [Fact]
    public void TryReadNullablePositiveDouble_DelegatesTextBoxTextToSharedParser() =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadNullablePositiveDouble(Box("0"), out var value).Should().BeFalse();

            value.Should().Be(0);
        });

    [Fact]
    public void TryReadClampedDouble_DelegatesTextBoxTextToSharedParser() =>
        StaTestRunner.Run(() =>
            ChartDialogInputParser.TryReadClampedDouble(Box("10.01"), min: 0.5, max: 10, out _).Should().BeFalse());

    [Fact]
    public void TryReadOptionalColor_UsesSharedHexColorRules() =>
        StaTestRunner.Run(() =>
        {
            ChartDialogInputParser.TryReadOptionalColor(Box("#102030"), out var color).Should().BeTrue();

            color.Should().Be(new CellColor(0x10, 0x20, 0x30));
        });

    private static TextBox Box(string text) => new() { Text = text };
}
