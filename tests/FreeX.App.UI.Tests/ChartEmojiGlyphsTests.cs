using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class ChartEmojiGlyphsTests
{
    [Theory]
    [InlineData("\U0001F44D 30%", "\U0001F44D", "30%")]   // 👍 30%
    [InlineData("\U0001F44E 5%", "\U0001F44E", "5%")]     // 👎 5%
    [InlineData("\U0001F44C 0%", "\U0001F44C", "0%")]     // 👌 0%
    [InlineData("\U0001F44D\U0001F44C 42%", "\U0001F44D\U0001F44C", "42%")] // two emoji then text
    public void SplitLeadingEmoji_PeelsLeadingEmojiRunFromText(string label, string expectedEmoji, string expectedText)
    {
        var (emoji, text) = ChartEmojiGlyphs.SplitLeadingEmoji(label);

        emoji.Should().Be(expectedEmoji);
        text.Should().Be(expectedText);
    }

    [Fact]
    public void SplitLeadingEmoji_AbsorbsVariationSelectorIntoEmojiRun()
    {
        // 👍 followed by VARIATION SELECTOR-16 (emoji presentation) then a space + text.
        var (emoji, text) = ChartEmojiGlyphs.SplitLeadingEmoji("\U0001F44D️ 12%");

        emoji.Should().Be("\U0001F44D️");
        text.Should().Be("12%");
    }

    [Theory]
    [InlineData("30%")]
    [InlineData("")]
    [InlineData("Actual")]
    public void SplitLeadingEmoji_NoLeadingEmoji_ReturnsEmptyEmojiAndOriginalText(string label)
    {
        var (emoji, text) = ChartEmojiGlyphs.SplitLeadingEmoji(label);

        emoji.Should().BeEmpty();
        text.Should().Be(label);
    }

    [Theory]
    [InlineData("\U0001F44D 30%", true)]
    [InlineData("30%", false)]
    [InlineData("", false)]
    public void HasLeadingEmoji_ReportsWhetherLabelStartsWithEmoji(string label, bool expected) =>
        ChartEmojiGlyphs.HasLeadingEmoji(label).Should().Be(expected);

    [Theory]
    [InlineData("\U0001F44D 30%", "\U0001F44D", "30%")]   // 👍 drawable
    [InlineData("\U0001F44E 5%", "\U0001F44E", "5%")]     // 👎 drawable
    [InlineData("\U0001F44C 0%", "\U0001F44C", "0%")]     // 👌 drawable
    public void SplitLeadingDrawableEmoji_PeelsKnownDrawableEmoji(string label, string expectedEmoji, string expectedText)
    {
        var (emoji, text) = ChartEmojiGlyphs.SplitLeadingDrawableEmoji(label);

        emoji.Should().Be(expectedEmoji);
        text.Should().Be(expectedText);
    }

    [Fact]
    public void SplitLeadingDrawableEmoji_NonDrawableEmoji_LeavesLabelIntact()
    {
        // 🚀 (rocket) is a valid emoji but we have no colored drawing for it → keep on text path.
        var (emoji, text) = ChartEmojiGlyphs.SplitLeadingDrawableEmoji("\U0001F680 99%");

        emoji.Should().BeEmpty();
        text.Should().Be("\U0001F680 99%");
    }

    [Fact]
    public void RenderEmojiPng_ProducesNonEmptyColorBitmap()
    {
        EmojiBitmap? result = null;
        WpfTestThread.Run(() =>
        {
            result = ChartEmojiGlyphs.RenderEmojiPng("\U0001F44D", fontSize: 14, renderScale: 2.0);
        });

        result.Should().NotBeNull();
        result!.Value.Png.Should().NotBeEmpty();
        result.Value.PixelWidth.Should().BeGreaterThan(0);
        result.Value.PixelHeight.Should().BeGreaterThan(0);

        // The whole point of this path: the thumbs-up must render in COLOR, not as a flat black/gray
        // monochrome glyph. Decode the PNG and assert it contains a meaningfully saturated/colored pixel.
        IsColorful(result.Value.Png).Should().BeTrue("Segoe UI Emoji color-glyph layers must survive rasterization");
    }

    [Fact]
    public void RenderEmojiPng_EmptyInput_ReturnsNull() =>
        ChartEmojiGlyphs.RenderEmojiPng("", fontSize: 14, renderScale: 2.0).Should().BeNull();

    private static bool IsColorful(byte[] png)
    {
        using var ms = new System.IO.MemoryStream(png);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var o = 0; o < pixels.Length; o += 4)
        {
            byte b = pixels[o], g = pixels[o + 1], r = pixels[o + 2], a = pixels[o + 3];
            if (a < 32) continue; // ignore transparent background
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            // A colored (non-gray) pixel has a clear channel spread; grayscale glyphs have max≈min.
            if (max - min > 40)
                return true;
        }

        return false;
    }
}
