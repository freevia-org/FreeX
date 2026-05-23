using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonIconFactoryTests
{
    [Theory]
    [InlineData(24, 1.00, 24)]
    [InlineData(24, 1.25, 30)]
    [InlineData(24, 1.50, 36)]
    [InlineData(24, 1.75, 42)]
    [InlineData(24, 2.00, 48)]
    [InlineData(24, 2.25, 54)]
    [InlineData(24, 2.50, 60)]
    [InlineData(32, 1.00, 32)]
    [InlineData(32, 1.25, 40)]
    [InlineData(32, 1.50, 48)]
    [InlineData(32, 1.75, 56)]
    [InlineData(32, 2.00, 64)]
    [InlineData(32, 2.25, 72)]
    [InlineData(32, 2.50, 80)]
    public void ResolveCommandIconPixelSizeForDpi_UsesExactCommonWindowsScaleAssets(
        double logicalSize,
        double dpiScale,
        int expectedPixels)
    {
        RibbonIconFactory.ResolveCommandIconPixelSizeForDpi(logicalSize, dpiScale)
            .Should().Be(expectedPixels);
    }
}
