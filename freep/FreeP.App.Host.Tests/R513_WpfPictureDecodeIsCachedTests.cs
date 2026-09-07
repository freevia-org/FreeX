using System.IO;
using System.Windows.Media.Imaging;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r513: the WPF sibling of r512. FreeP's WPF slide renderer decoded a picture's bytes on every
/// render pass, exactly as the Avalonia one did. The consequence differs -- WPF's BitmapImage is
/// GC-managed and the renderer already froze it, so nothing leaked and nothing was thread-bound;
/// what repeated was the decode work itself. These tests pin the seam that removes it, and pin the
/// frozen-ness that makes sharing one instance across callers legal in the first place.
/// </summary>
public sealed class R513_WpfPictureDecodeIsCachedTests
{
    private static byte[] EncodePng()
    {
        var source = BitmapSource.Create(
            2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255 },
            8);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }

    [StaFact]
    public void DecodingTheSameImageTwiceReturnsOneInstance()
    {
        var bytes = EncodePng();

        var first = FreeP.App.Rendering.Wpf.SlideCanvas.DecodePicture(bytes);
        var second = FreeP.App.Rendering.Wpf.SlideCanvas.DecodePicture(bytes);

        // Reference equality is the whole point: a second render pass must not redo the decode.
        Assert.Same(first, second);
        Assert.Equal(2, first.PixelWidth);
    }

    [StaFact]
    public void DecodedImagesAreFrozenSoSharingThemIsLegal()
    {
        // r495's rule: a shared, cached graphics object has to be thread-agnostic. Freeze() is what
        // makes that true in WPF, so caching would be unsound without it.
        Assert.True(FreeP.App.Rendering.Wpf.SlideCanvas.DecodePicture(EncodePng()).IsFrozen);
    }

    [StaFact]
    public void DistinctBuffersDoNotCollide()
    {
        // The cache is keyed on array IDENTITY, not content, so equal-content buffers stay separate
        // rather than one picture silently rendering as another.
        var a = EncodePng();
        var b = EncodePng();
        Assert.Equal(a, b);

        Assert.NotSame(
            FreeP.App.Rendering.Wpf.SlideCanvas.DecodePicture(a),
            FreeP.App.Rendering.Wpf.SlideCanvas.DecodePicture(b));
    }
}
