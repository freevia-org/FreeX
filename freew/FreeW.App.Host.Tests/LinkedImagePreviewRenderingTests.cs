using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class LinkedImagePreviewRenderingTests
{
    [StaFact]
    public void DecodeImage_UsesResolvedLinkedPreviewWhenEmbeddedBytesAreAbsent()
    {
        var image = new InlineImage([], 24, 24)
        {
            LinkedImageTarget = "linked.png",
            ResolvedLinkedImageBytes = OnePixelPng()
        };

        var decoded = DocumentView.DecodeImage(image).Should().BeAssignableTo<BitmapSource>().Subject;

        decoded.PixelWidth.Should().Be(1);
        decoded.PixelHeight.Should().Be(1);
        image.Bytes.Should().BeEmpty();
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
