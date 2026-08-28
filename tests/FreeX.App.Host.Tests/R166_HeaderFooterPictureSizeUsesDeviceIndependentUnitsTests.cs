using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round 166: HeaderFooterDialog.Pictures.cs's GetImageSize() (feeding PictureButton_Click, which
/// stores the result verbatim as WorksheetHeaderFooterPicture.Width/Height) used to return the
/// decoded frame's raw PixelWidth/PixelHeight with no DPI correction -- unlike every other picture
/// insertion path in the app (see ImageDimensionDecoder, used by the ordinary Insert>Picture
/// command), which converts native pixels to the app's device-independent 1/96-inch unit
/// convention via pixels * 96 / dpi. Storing raw pixel counts as if they were already DIP units
/// meant a picture with non-96 embedded DPI metadata was stored at the wrong physical size (a
/// 192x96px image at 192 DPI -- physically 1in x 0.5in -- was stored as 192x96 "inches"), which
/// WorksheetPrintPageContentPlanner.ResolveLineHeight then used verbatim to size the entire
/// header/footer band, ballooning it far beyond the printable page.
/// </summary>
public sealed class R166_HeaderFooterPictureSizeUsesDeviceIndependentUnitsTests
{
    private static (double Width, double Height) InvokeGetImageSize(byte[] bytes)
    {
        var method = typeof(HeaderFooterDialog).GetMethod(
            "GetImageSize",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(HeaderFooterDialog), "GetImageSize");
        var result = method.Invoke(null, [bytes]);
        return ((double, double))result!;
    }

    [Fact]
    public void R166_HighDpiImage_SizeIsConvertedToDeviceIndependentUnitsNotRawPixels()
    {
        StaTestRunner.Run(() =>
        {
            // 192x96 pixels at 192 DPI is physically a 1in x 0.5in image: 96x48 DIP units.
            // The pre-fix code returned the raw pixel counts (192, 96) instead -- storing the
            // picture at 4x its actual physical size and, via ResolveLineHeight, forcing the
            // header/footer band to that same inflated height.
            var bytes = ImageTestData.CreatePngBytes(pixelWidth: 192, pixelHeight: 96, dpiX: 192, dpiY: 192);

            var (width, height) = InvokeGetImageSize(bytes);

            width.Should().BeApproximately(96, 0.5);
            height.Should().BeApproximately(48, 0.5);
        });
    }

    [Fact]
    public void R166_StandardDpiImage_SizeIsUnchangedSiblingCase()
    {
        // No-regression sibling: at the app's default 96 DPI convention, pixels * 96 / 96 == pixels,
        // so an ordinary 96 DPI image (the common case for screenshots and most exported photos)
        // must still resolve to its exact pixel dimensions in DIP units, unaffected by the fix.
        StaTestRunner.Run(() =>
        {
            var bytes = ImageTestData.CreatePngBytes(pixelWidth: 64, pixelHeight: 32, dpiX: 96, dpiY: 96);

            var (width, height) = InvokeGetImageSize(bytes);

            width.Should().BeApproximately(64, 0.01);
            height.Should().BeApproximately(32, 0.01);
        });
    }
}
