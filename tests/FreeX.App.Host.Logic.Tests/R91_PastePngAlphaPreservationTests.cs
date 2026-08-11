using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-io-clipboard-image-formats-5-4 (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs,
/// TryPasteClipboardImage/TryGetClipboardPngFormatBytes).
///
/// Before the fix: pasting an image copied from a source that places BOTH a flattened
/// CF_DIB/CF_BITMAP entry AND a separate alpha-preserving "PNG" entry (Chrome/Edge and many image
/// editors do this for a transparent-background image) always went through
/// <c>System.Windows.Clipboard.GetImage()</c>, which resolves exclusively to the flattened
/// DIB/Bitmap entry -- silently baking a solid matte over what should have stayed transparent.
///
/// After the fix, TryPasteClipboardImage prefers the raw "PNG" format's bytes (real alpha intact)
/// when present, only falling back to GetImage() when no such format exists.
/// </summary>
public sealed class R91_PastePngAlphaPreservationTests
{
    [Fact]
    public void TryPasteClipboardImage_WhenRichPngFormatPresent_PreservesAlphaChannel()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var anchor = new CellAddress(sheet.Id, 1, 1);
                window.SheetGrid.SelectedRange = new GridRange(anchor, anchor);

                var dataObject = new System.Windows.DataObject();
                dataObject.SetData(System.Windows.DataFormats.Bitmap, CreateOpaqueBitmapSource());
                dataObject.SetData("PNG", CreateTransparentPngBytes());
                System.Windows.Clipboard.SetDataObject(dataObject, copy: true);

                var result = (bool)R49MainWindowTestHarness.Invoke(window, "TryPasteClipboardImage", anchor)!;

                result.Should().BeTrue();
                sheet.Pictures.Should().ContainSingle();
                ReadAlphaOfFirstPixel(sheet.Pictures[0].ImageBytes!).Should().Be(
                    0, "the richer PNG clipboard format's real alpha channel must survive the paste, " +
                       "not get flattened to opaque by GetImage()'s CF_DIB fallback");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: when the source app placed ONLY the flattened Bitmap/DIB format (the
    // overwhelmingly common case -- most copy sources never provide a richer "PNG" entry at all),
    // the pre-existing GetImage() fallback path must still work exactly as before.
    [Fact]
    public void TryPasteClipboardImage_WhenNoRichPngFormatPresent_StillPastesViaFlattenedBitmap()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var anchor = new CellAddress(sheet.Id, 1, 1);
                window.SheetGrid.SelectedRange = new GridRange(anchor, anchor);

                System.Windows.Clipboard.SetImage(CreateOpaqueBitmapSource());

                var result = (bool)R49MainWindowTestHarness.Invoke(window, "TryPasteClipboardImage", anchor)!;

                result.Should().BeTrue();
                sheet.Pictures.Should().ContainSingle();
                sheet.Pictures[0].ContentType.Should().Be("image/png");
                ReadAlphaOfFirstPixel(sheet.Pictures[0].ImageBytes!).Should().Be(255);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static byte ReadAlphaOfFirstPixel(byte[] pngBytes)
    {
        using var stream = new System.IO.MemoryStream(pngBytes);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixels, 4, 0);
        return pixels[3];
    }

    private static byte[] CreateTransparentPngBytes()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);
        // B, G, R, A -- fully transparent (alpha == 0).
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 1, 1), new byte[] { 0, 0, 0, 0 }, 4, 0);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static System.Windows.Media.Imaging.BitmapSource CreateOpaqueBitmapSource()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);
        // Opaque white (alpha == 255) -- what Clipboard.GetImage() would hand back for a flattened
        // CF_DIB/CF_BITMAP entry, matching the "no alpha" real-world case.
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 1, 1), new byte[] { 255, 255, 255, 255 }, 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
