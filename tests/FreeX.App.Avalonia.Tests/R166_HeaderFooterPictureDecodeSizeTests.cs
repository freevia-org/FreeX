using System.Reflection;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-166 finding shared-image-handling F2: the Avalonia header/footer "Insert
/// Picture" handler (<see cref="MainWindow.ShowHeaderFooterEditorDialogAsync"/> in MainWindow.PageLayout.cs)
/// used to build the inserted <c>WorksheetHeaderFooterPicture</c> with the literal constants 160 and 80 for
/// every picture, regardless of the image's actual pixel dimensions -- unlike the sibling ordinary
/// Insert &gt; Picture path (<c>MainWindow.InsertObjects.cs</c>'s <c>DecodePictureSize</c>), which decodes the
/// image's native size. Because <c>WorksheetPrintPageContentPlanner.ResolvePictureBounds</c> uses
/// <c>picture.Width</c>/<c>Height</c> directly as the render box with no letterboxing, every inserted image
/// whose aspect ratio wasn't exactly 2:1 was visibly stretched/squashed in Print Preview and PDF export.
///
/// Fixed by adding <c>MainWindow.DecodeHeaderFooterPictureSize</c>, which decodes the image's native pixel
/// size via Avalonia's <see cref="global::Avalonia.Media.Imaging.Bitmap"/> and converts it to this app's
/// 96-DPI device-independent unit convention through the same <c>PictureDecodePixelsToDip</c> helper
/// (MainWindow.InsertObjects.cs) that the ordinary Insert &gt; Picture path's <c>DecodePictureSize</c> already
/// uses, and falls back to the historical 160x80 default only when the bytes cannot be decoded as an image
/// (or decode to a non-positive size) at all.
/// </summary>
/// <remarks>
/// <see cref="DecodeHeaderFooterPictureSizeFromValidPng_UsesBitmapDecodeNotHardcoded160x80"/> constructs a
/// real Avalonia <c>Bitmap</c>, which needs a running headless application/render context (see
/// <see cref="AvaloniaHeadlessIsolationTests"/>), so it dispatches through the shared headless session like
/// every other Bitmap-touching test in this project (e.g. <c>R60_AvaloniaWorksheetBackgroundBitmapDisposalTests</c>).
/// In that headless configuration <c>new Bitmap(stream)</c> decodes ANY byte content -- including a genuine,
/// disk-verified non-square PNG -- to a fixed 1x1 stub at 96 DPI rather than actually rasterizing it, and
/// never throws even for garbage bytes (see <c>R150_InsertObjectsDecodePictureSizeDpiTests</c> for the same
/// documented quirk on the sibling ordinary-picture path). That means the outer method can only be proven to
/// call the decoder at all (1x1, not the old hardcoded 160x80) -- it cannot observe real dimensions or the
/// undecodable-bytes fallback in this harness. So the fallback threshold itself is covered separately by
/// <see cref="ResolveHeaderFooterPictureSize_NonPositiveDecodedPixels_FallsBackTo160x80"/>, which calls the
/// extracted pure <c>ResolveHeaderFooterPictureSize</c> helper directly with synthetic pixel/DPI values --
/// no Bitmap, no dispatch needed -- mirroring how <c>R150_InsertObjectsDecodePictureSizeDpiTests</c> tests
/// the sibling path's pure conversion arithmetic directly rather than through a real image decode.
/// </remarks>
[Collection("AvaloniaHeadless")]
public sealed class R166_HeaderFooterPictureDecodeSizeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // A genuine 4x2 PNG (IHDR width=4, height=2, 96 DPI pHYs chunk), generated via System.Drawing and
    // verified on disk -- not a synthetic/truncated fixture. The headless Bitmap decoder stubs its actual
    // pixel content to 1x1 (see remarks above), but decoding it at all -- rather than skipping straight to
    // the old hardcoded 160x80 -- is exactly what this test is proving.
    private static byte[] FourByTwoPngBytes { get; } =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x02, 0x08, 0x06, 0x00, 0x00, 0x00, 0x7F, 0xA8, 0x7D,
        0x63, 0x00, 0x00, 0x00, 0x01, 0x73, 0x52, 0x47, 0x42, 0x00, 0xAE, 0xCE, 0x1C, 0xE9, 0x00, 0x00,
        0x00, 0x04, 0x67, 0x41, 0x4D, 0x41, 0x00, 0x00, 0xB1, 0x8F, 0x0B, 0xFC, 0x61, 0x05, 0x00, 0x00,
        0x00, 0x09, 0x70, 0x48, 0x59, 0x73, 0x00, 0x00, 0x0E, 0xC3, 0x00, 0x00, 0x0E, 0xC3, 0x01, 0xC7,
        0x6F, 0xA8, 0x64, 0x00, 0x00, 0x00, 0x11, 0x49, 0x44, 0x41, 0x54, 0x18, 0x57, 0x63, 0xE0, 0x12,
        0x91, 0xFB, 0x8F, 0x8C, 0x19, 0xD0, 0x05, 0x00, 0x9B, 0xD6, 0x09, 0xD9, 0xA2, 0x66, 0xAD, 0x85,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public async Task DecodeHeaderFooterPictureSizeFromValidPng_UsesBitmapDecodeNotHardcoded160x80()
    {
        // Pre-fix, MainWindow.DecodeHeaderFooterPictureSize did not exist at all (the call site literally
        // hardcoded 160,80 with no decode step), so this reflection lookup throws before the fix. Post-fix,
        // decoding a real PNG through this headless harness's Bitmap stub yields (1,1) DIP -- a value that
        // is only reachable by actually calling the decoder, and is NOT the old hardcoded (160,80).
        await Session.Dispatch(() =>
        {
            var (width, height) = InvokeDecode(FourByTwoPngBytes);

            width.Should().Be(1);
            height.Should().Be(1);
        }, CancellationToken.None);
    }

    [Fact]
    public void ResolveHeaderFooterPictureSize_NonPositiveDecodedPixels_FallsBackTo160x80()
    {
        // Sibling/no-regression case: a decode that yields a non-positive pixel dimension (as a corrupt,
        // truncated, or degenerate image would) must not propagate a zero/negative box -- it falls back to
        // the historical 160x80 default, exactly what every insert unconditionally used before this fix.
        // Exercises the pure conversion+fallback helper directly, without touching Bitmap or the headless
        // dispatcher, matching how R150_InsertObjectsDecodePictureSizeDpiTests tests the sibling formula.
        var (zeroWidth, zeroHeight) = InvokeResolve(0, 10, 96, 96);
        zeroWidth.Should().Be(160);
        zeroHeight.Should().Be(80);

        var (negativeWidth, negativeHeight) = InvokeResolve(10, -1, 96, 96);
        negativeWidth.Should().Be(160);
        negativeHeight.Should().Be(80);
    }

    [Fact]
    public void ResolveHeaderFooterPictureSize_PositiveDecodedPixels_ConvertsThroughDpiNotHardcoded()
    {
        // No-regression sibling: a normal successful decode (positive pixel size, standard 96 DPI) must
        // pass the real decoded size through -- pixels * 96 / 96 == pixels -- not the hardcoded default.
        var (width, height) = InvokeResolve(192, 96, 96, 96);
        width.Should().Be(192);
        height.Should().Be(96);
    }

    private static (double Width, double Height) InvokeDecode(byte[] imageBytes)
    {
        var method = typeof(MainWindow).GetMethod(
            "DecodeHeaderFooterPictureSize",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "MainWindow.DecodeHeaderFooterPictureSize not found via reflection " +
                "(pre-fix: the header/footer Insert Picture handler had no size-decode helper at all, " +
                "just literal 160,80 constants at the WorksheetHeaderFooterPicture call site).");
        var result = method.Invoke(null, [imageBytes]);
        result.Should().NotBeNull();
        // The method returns a value tuple (double Width, double Height); reflection sees it as
        // ValueTuple<double,double> with Item1/Item2 fields (the element names are compiler sugar), so
        // unbox it directly rather than probing for non-existent "Width"/"Height" reflection members.
        return ((double, double))result!;
    }

    private static (double Width, double Height) InvokeResolve(int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        var method = typeof(MainWindow).GetMethod(
            "ResolveHeaderFooterPictureSize",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MainWindow.ResolveHeaderFooterPictureSize not found via reflection.");
        var result = method.Invoke(null, [pixelWidth, pixelHeight, dpiX, dpiY]);
        result.Should().NotBeNull();
        return ((double, double))result!;
    }
}
