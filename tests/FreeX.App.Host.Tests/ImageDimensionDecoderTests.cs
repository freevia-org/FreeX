using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ImageDimensionDecoderTests
{
    [Fact]
    public void TryDecode_ReturnsNaturalPngDimensions()
    {
        StaTestRunner.Run(() =>
        {
            var bytes = ImageTestData.CreatePngBytes(pixelWidth: 37, pixelHeight: 23);

            ImageDimensionDecoder.TryDecode(bytes, out var dimensions).Should().BeTrue();

            dimensions.Width.Should().BeApproximately(37, 0.01);
            dimensions.Height.Should().BeApproximately(23, 0.01);
        });
    }

    [Fact]
    public void TryDecode_ConvertsImageDpiToDeviceIndependentUnits()
    {
        StaTestRunner.Run(() =>
        {
            var bytes = ImageTestData.CreatePngBytes(pixelWidth: 192, pixelHeight: 96, dpiX: 192, dpiY: 192);

            ImageDimensionDecoder.TryDecode(bytes, out var dimensions).Should().BeTrue();

            dimensions.Width.Should().BeApproximately(96, 0.01);
            dimensions.Height.Should().BeApproximately(48, 0.01);
        });
    }

    [Fact]
    public void TryDecode_ReturnsFalseForInvalidImageBytes()
    {
        var result = ImageDimensionDecoder.TryDecode([1, 2, 3, 4], out var dimensions);

        result.Should().BeFalse();
        dimensions.Should().Be(default(DecodedImageDimensions));
    }
}
