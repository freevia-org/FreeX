using System.Reflection;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Proves that <see cref="MainWindow"/>'s picture-insertion pixel-to-DIP conversion (used by
/// <c>DecodePictureSize</c>, which backs both Insert &gt; Picture and Insert &gt; Object with "Embed image as
/// picture") converts pixels to 96-DPI device-independent units using the image's own embedded DPI, the same
/// formula as the WPF host's <c>ImageDimensionDecoder.PixelsToDeviceIndependentUnits</c>
/// (<c>shared/Free.Shared.Shell.Wpf/ImageDimensionDecoder.cs</c>, consumed at
/// <c>src/FreeX.App.Host/MainWindow.Drawing.cs:75</c>): <c>pixels * 96 / dpi</c>.
/// </summary>
/// <remarks>
/// This exercises the extracted conversion helper directly via reflection rather than round-tripping a real
/// image through Avalonia's <c>Bitmap</c> decoder: in this project's headless Avalonia test configuration
/// (<see cref="RibbonHeadlessApp"/>, <c>UseHeadlessDrawing = true</c>), <c>new Bitmap(...)</c> decodes ANY
/// input — including a real, disk-verified PNG asset from this repo — to a stub 1x1 bitmap at a fixed 96 DPI
/// rather than actually rasterizing it (independently confirmed valid via GDI+
/// <c>Image.HorizontalResolution</c>/<c>VerticalResolution</c>). That is a known, separately-quarantined
/// headless-environment limitation (see <c>ConsolidateDialogLifecycleRegressionTests.ConsolidateCapture_...</c>,
/// excluded from the gate via this project's <c>VSTestTestCaseFilter</c>), not a defect in the fix under test,
/// and it makes the full <c>DecodePictureSize</c> pipeline unable to observe a non-96 embedded DPI in this
/// harness. The pixel-to-DIP arithmetic — the actual bug and fix — is pure and independent of bitmap
/// rasterization, so testing it directly is both possible and precise.
/// </remarks>
public sealed class R150_InsertObjectsDecodePictureSizeDpiTests
{
    [Theory]
    [InlineData(1200, 300d, 384d)]   // 300 DPI photo: 1200px -> 4.00in -> 384 DIP (was 1200 DIP pre-fix)
    [InlineData(900, 300d, 288d)]    // matching height: 900px -> 3.00in -> 288 DIP (was 900 DIP pre-fix)
    [InlineData(4800, 96d, 4800d)]   // 96 DPI (the DIP reference) is a no-op conversion either way
    public void PixelsToDipConversion_UsesEmbeddedDpi_NotRawPixelCount(int pixels, double dpi, double expectedDip)
    {
        var actual = InvokePixelsToDip(pixels, dpi);

        actual.Should().BeApproximately(expectedDip, 0.01);
    }

    [Fact]
    public void PixelsToDipConversion_NonPositiveOrNonFiniteDpi_FallsBackTo96()
    {
        // Sibling/no-regression case: a missing or garbage DPI reading (0, negative, NaN, Infinity) must not
        // corrupt the size or throw — it falls back to the 96 DPI reference, so pixels pass through unscaled,
        // exactly like the pre-fix code path did unconditionally.
        InvokePixelsToDip(640, 0d).Should().BeApproximately(640d, 0.01);
        InvokePixelsToDip(640, -1d).Should().BeApproximately(640d, 0.01);
        InvokePixelsToDip(640, double.NaN).Should().BeApproximately(640d, 0.01);
        InvokePixelsToDip(640, double.PositiveInfinity).Should().BeApproximately(640d, 0.01);
    }

    private static double InvokePixelsToDip(int pixels, double dpi)
    {
        var method = typeof(MainWindow).GetMethod(
            "PictureDecodePixelsToDip",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "MainWindow.PictureDecodePixelsToDip not found via reflection " +
                "(pre-fix: DecodePictureSize had no pixel->DIP conversion helper at all).");
        return (double)method.Invoke(null, [pixels, dpi])!;
    }
}
