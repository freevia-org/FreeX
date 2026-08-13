using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ScreenClipImageFactoryTests
{
    [Theory]
    [InlineData(96, 48, 72, 36)]
    [InlineData(1200, 600, 400, 200)]
    [InlineData(1600, 900, 400, 225)]
    public void CreateBuildsPngModelWithDisplaySizeAndCaptureMetadata(
        int pixelWidth,
        int pixelHeight,
        double expectedWidthPt,
        double expectedHeightPt)
    {
        byte[] pngBytes = [137, 80, 78, 71];

        var image = ScreenClipImageFactory.Create(pngBytes, pixelWidth, pixelHeight);

        image.Bytes.Should().BeSameAs(pngBytes);
        image.Format.Should().Be(ImageFormat.Png);
        image.WidthPt.Should().BeApproximately(expectedWidthPt, 0.001);
        image.HeightPt.Should().BeApproximately(expectedHeightPt, 0.001);
        image.OriginalPixelWidth.Should().Be(pixelWidth);
        image.OriginalPixelHeight.Should().Be(pixelHeight);
        image.Wrapping.Should().Be(ImageWrapping.Inline);
    }

    [Fact]
    public void CreateRejectsNullOrEmptyCaptureBytes()
    {
        var nullBytes = () => ScreenClipImageFactory.Create(null!, 10, 10);
        var emptyBytes = () => ScreenClipImageFactory.Create([], 10, 10);

        nullBytes.Should().Throw<ArgumentNullException>();
        emptyBytes.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void CreateRejectsEmptyPixelDimensions(int pixelWidth, int pixelHeight)
    {
        var act = () => ScreenClipImageFactory.Create([1], pixelWidth, pixelHeight);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
