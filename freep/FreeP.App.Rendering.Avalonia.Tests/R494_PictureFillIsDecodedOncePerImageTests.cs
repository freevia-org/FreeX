using SkiaSharp;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Headless;
using FluentAssertions;
using FreeP.App.Compositor;
using Xunit;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// r494: a picture fill must be decoded once per image, not once per paint.
///
/// <para><c>MakePictureBrush</c> is reached only from <c>MakeBrush</c>, and every caller of that is a
/// Render method taking a DrawingContext - RenderBackground, RenderShape, RenderShapeEffects,
/// RenderTableCellGeometry. So it ran on every PAINT, decoding a fresh <see cref="Bitmap"/> and
/// handing it to an ImageBrush, which does not own or dispose its source. A slide holding one
/// picture-filled shape therefore grew native memory on every scroll, resize, animation tick and
/// slide-show frame; there was no cache anywhere in the file.</para>
///
/// <para>The fix keys decoded bitmaps on the image array's IDENTITY, which is what makes it safe to
/// cache at all: no hashing cost, no staleness question because a different image is a different
/// array, and the entry dies with the image data rather than outliving the deck.</para>
/// </summary>
public sealed class R494_PictureFillIsDecodedOncePerImageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    // A real 2x2 PNG encoded by Skia: a hand-written byte array is easy to get subtly wrong, and a
    // rejected image silently becomes Brushes.Transparent rather than an ImageBrush -- which is
    // exactly how the first version of this test failed.
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(new SKColor(0x40, 0x80, 0xC0));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IImage? SourceOf(IBrush brush) => (brush as ImageBrush)?.Source as IImage;

    [Fact]
    public async Task RepaintingTheSameFillReusesOneDecodedBitmap()
    {
        IImage? first = null;
        IImage? second = null;

        await Session.Dispatch(() =>
        {
            // The SAME ResolvedFill instance, as a repaint of an unchanged slide supplies.
            var fill = new ResolvedFill.Picture(Png(), "image/png");

            first = SourceOf(SlideCanvas.MakePictureBrush(fill));
            second = SourceOf(SlideCanvas.MakePictureBrush(fill));
        }, default);

        first.Should().NotBeNull("the fill must still produce a usable brush");
        second.Should().BeSameAs(first,
            "a second paint must reuse the decoded bitmap; decoding again leaks it, because " +
            "ImageBrush neither owns nor disposes its source");
    }

    [Fact]
    public async Task TheTilingOptionSurvivesTheCache()
    {
        // The cache holds the BITMAP, not the brush, so per-fill options must still be applied.
        // Every property is read INSIDE the dispatch: Avalonia objects are thread-owned, and
        // touching one from the test thread throws rather than returning a wrong answer.
        var tiledMode = TileMode.None;
        var stretchedMode = TileMode.Tile;
        var sharesSource = false;

        await Session.Dispatch(() =>
        {
            var bytes = Png();
            var tiled = (ImageBrush)SlideCanvas.MakePictureBrush(new ResolvedFill.Picture(bytes, "image/png", tile: true));
            var stretched = (ImageBrush)SlideCanvas.MakePictureBrush(new ResolvedFill.Picture(bytes, "image/png"));

            tiledMode = tiled.TileMode;
            stretchedMode = stretched.TileMode;
            sharesSource = ReferenceEquals(tiled.Source, stretched.Source);
        }, default);

        tiledMode.Should().Be(TileMode.Tile);
        stretchedMode.Should().Be(TileMode.None);
        sharesSource.Should().BeTrue("both brushes share the one decode of that image");
    }

    [Fact]
    public async Task ADifferentImageStillDecodesSeparately()
    {
        // Narrowness: caching must not collapse two different pictures onto one bitmap.
        var shareSource = true;

        await Session.Dispatch(() =>
        {
            var left = SlideCanvas.MakePictureBrush(new ResolvedFill.Picture(Png(), "image/png"));
            var right = SlideCanvas.MakePictureBrush(new ResolvedFill.Picture(Png(), "image/png"));
            shareSource = ReferenceEquals(((ImageBrush)left).Source, ((ImageBrush)right).Source);
        }, default);

        shareSource.Should().BeFalse("distinct image arrays are distinct pictures");
    }
}
