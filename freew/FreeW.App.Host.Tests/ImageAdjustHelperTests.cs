using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Unit tests for <see cref="ImageAdjustHelper"/>: verifies that brightness, contrast, saturation, and
/// transparency adjustments produce measurably different bitmaps from the source, without altering the
/// original bytes on the <see cref="InlineImage"/> model.  All tests run on the STA thread because
/// <see cref="WriteableBitmap"/> requires it.
/// </summary>
public sealed class ImageAdjustHelperTests
{
    // A 2×2 grey pixel RGBA source built on the STA thread each time.
    private static BitmapSource MakeSource(byte r, byte g, byte b, byte a = 255)
    {
        // Build a 2×2 Pbgra32 bitmap with every pixel set to (r,g,b,a).
        int width = 2, height = 2, stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // Pbgra32: B, G, R, A (all premultiplied; for opaque pixels no scaling needed).
            pixels[i    ] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
        bmp.Freeze();
        return bmp;
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(BitmapSource src, int x = 0, int y = 0)
    {
        var conv = src.Format == PixelFormats.Pbgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        var buf = new byte[4];
        conv.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buf, 4, 0);
        return (buf[2], buf[1], buf[0], buf[3]); // R,G,B,A from Pbgra32 B,G,R,A
    }

    // ── Neutral (no-op) ───────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void NeutralAdjustments_ReturnsSameSource()
    {
        var src = MakeSource(128, 64, 200);
        var image = new InlineImage([0x89, 0x50], 100, 80); // bytes irrelevant — HasAdjustments is false
        var result = ImageAdjustHelper.Apply(src, image);
        result.Should().BeSameAs(src, "neutral adjustments should return the input reference unchanged");
    }

    [StaFact]
    public void BakedArtisticPreview_IsNotFilteredAgain()
    {
        var src = MakeSource(40, 120, 200);
        var image = new InlineImage([0x89, 0x50], 100, 80)
        {
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
            HasBakedArtisticEffectPreview = true,
        };

        var result = ImageAdjustHelper.Apply(src, image);

        result.Should().BeSameAs(src, "Word's a:blip already contains the rendered artistic preview");
    }

    // ── Transparency ──────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Transparency50_ReducesAlphaByHalf()
    {
        // Start with a fully opaque grey pixel (alpha=255).
        var src = MakeSource(128, 128, 128, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 0, 100, 50);
        var (_, _, _, a) = ReadPixel(result);

        // 50% transparency → opacity=50% → alpha should be ~127 (255 × 0.5).
        a.Should().BeInRange(120, 135, "50% transparency should halve the alpha channel");
    }

    [StaFact]
    public void Transparency100_FullyTransparent()
    {
        var src = MakeSource(100, 100, 100, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 0, 100, 100);
        var (_, _, _, a) = ReadPixel(result);
        a.Should().Be(0, "100% transparency should result in fully transparent pixels");
    }

    [StaFact]
    public void Transparency0_KeepsFullAlpha()
    {
        var src = MakeSource(100, 100, 100, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 0, 100, 0);
        var (_, _, _, a) = ReadPixel(result);
        a.Should().Be(255, "0% transparency should keep full opacity");
    }

    // ── Brightness ────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void BrightnessPlus40_IncreasesLuminance()
    {
        var src = MakeSource(100, 100, 100, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 40, 0, 100, 0);
        var (r, g, b, _) = ReadPixel(result);

        // After +40% brightness shift (in 0-1 space, shift = 0.4), pixel should be brighter than 100.
        r.Should().BeGreaterThan(100, "brightness +40 should raise red channel");
        g.Should().BeGreaterThan(100, "brightness +40 should raise green channel");
        b.Should().BeGreaterThan(100, "brightness +40 should raise blue channel");
    }

    [StaFact]
    public void BrightnessMinus40_DecreasesLuminance()
    {
        var src = MakeSource(200, 200, 200, 255);
        var result = ImageAdjustHelper.ApplyCore(src, -40, 0, 100, 0);
        var (r, g, b, _) = ReadPixel(result);

        r.Should().BeLessThan(200, "brightness -40 should lower red channel");
        g.Should().BeLessThan(200);
        b.Should().BeLessThan(200);
    }

    // ── Saturation ────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Saturation0_ProducesGreyscale()
    {
        // A coloured pixel: R=200, G=50, B=50 (strong red). At 0% saturation it becomes grey.
        var src = MakeSource(200, 50, 50, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 0, 0, 0);
        var (r, g, b, _) = ReadPixel(result);

        // All channels should be equal (grey) — R/G/B should be close to each other.
        Math.Abs(r - g).Should().BeLessThan(5, "zero saturation should produce near-equal R and G");
        Math.Abs(r - b).Should().BeLessThan(5, "zero saturation should produce near-equal R and B");
        // The resulting grey must differ from any single original channel.
        r.Should().NotBe(200, "fully desaturated pixel should not retain original R value");
    }

    [StaFact]
    public void Saturation200_AmplifiesColorDifference()
    {
        // Slightly colorful pixel: R=140, G=120, B=120 (near-grey with a tiny red tint).
        var src = MakeSource(140, 120, 120, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 0, 200, 0);
        var (r, g, b, _) = ReadPixel(result);

        // At 200% saturation the red bias should be amplified (R higher, G+B lower than original).
        r.Should().BeGreaterThan(140, "200% saturation should push R further from grey");
        g.Should().BeLessThan(120, "200% saturation should push G further from grey");
        b.Should().BeLessThan(120, "200% saturation should push B further from grey");
    }

    // ── Contrast ─────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ContrastPlus100_PushesChannelsTowardsExtremes()
    {
        // A mid-range pixel at R=160, contrast +100% doubles the distance from mid (0.5).
        var src = MakeSource(160, 160, 160, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, 100, 100, 0);
        var (r, _, _, _) = ReadPixel(result);
        // 160/255 ≈ 0.627; distance from 0.5 = 0.127; × 2 = 0.255; final ≈ 0.755 → ~192.
        r.Should().BeGreaterThan(160, "contrast +100 on a value above mid-grey should push higher");
    }

    [StaFact]
    public void ContrastMinus50_CompressesChannelsTowardsMidGrey()
    {
        // A bright pixel: R=220. Contrast -50% → scale 0.5 → moves toward mid-grey.
        var src = MakeSource(220, 220, 220, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 0, -50, 100, 0);
        var (r, _, _, _) = ReadPixel(result);
        r.Should().BeLessThan(220, "contrast -50 on a bright pixel should compress toward mid-grey");
        r.Should().BeGreaterThan(128, "result should still be above mid-grey for a bright source pixel");
    }

    // ── Output is always a BitmapSource, never null, and original bytes are unaffected ───────────────

    [StaFact]
    public void ApplyCore_ReturnsNonNull_FrozenBitmapSource()
    {
        var src = MakeSource(128, 128, 128, 255);
        var result = ImageAdjustHelper.ApplyCore(src, 20, 0, 80, 10);
        result.Should().NotBeNull();
        result.IsFrozen.Should().BeTrue("the returned BitmapSource must be frozen for thread safety");
    }

    [StaFact]
    public void Apply_DoesNotMutateOriginalBytes()
    {
        var originalPng = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        };
        var snapshot = (byte[])originalPng.Clone();

        var image = new InlineImage(originalPng, 100, 80)
        {
            BrightnessPct   = 30,
            ContrastPct     = 20,
            SaturationPct   = 50,
            TransparencyPct = 25
        };

        var src = MakeSource(128, 128, 128, 255);
        ImageAdjustHelper.Apply(src, image);

        // The original bytes array on the model must be untouched.
        image.Bytes.Should().Equal(snapshot, "adjustment must be non-destructive — Bytes must be unchanged");
    }
}
