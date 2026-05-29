using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

public sealed class WpfBitmapImageLoaderTests
{
    private static readonly byte[] OnePixelPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    [Fact]
    public void TryLoad_ReturnsFalseForNullOrEmptyBytes()
    {
        WpfBitmapImageLoader.TryLoad(null, out var nullImage).Should().BeFalse();
        nullImage.Should().BeNull();

        WpfBitmapImageLoader.TryLoad([], out var emptyImage).Should().BeFalse();
        emptyImage.Should().BeNull();
    }

    [Fact]
    public void TryLoad_ReturnsFalseForInvalidImageBytes()
    {
        WpfBitmapImageLoader.TryLoad([1, 2, 3, 4], out var image).Should().BeFalse();
        image.Should().BeNull();
    }

    [Fact]
    public void TryLoad_ReusesDecodedImageForSameByteArray()
    {
        WpfBitmapImageLoader.TryLoad(OnePixelPng, out var first).Should().BeTrue();
        WpfBitmapImageLoader.TryLoad(OnePixelPng, out var second).Should().BeTrue();

        first.Should().BeSameAs(second);
        first!.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void TryLoad_DecodesDifferentByteArrayInstancesIndependently()
    {
        var imageBytes = OnePixelPng.ToArray();

        WpfBitmapImageLoader.TryLoad(imageBytes, out var first).Should().BeTrue();
        WpfBitmapImageLoader.TryLoad(imageBytes, out var second).Should().BeTrue();
        WpfBitmapImageLoader.TryLoad(OnePixelPng, out var originalBytesImage).Should().BeTrue();

        first.Should().BeSameAs(second);
        first.Should().NotBeSameAs(originalBytesImage);
    }
}
