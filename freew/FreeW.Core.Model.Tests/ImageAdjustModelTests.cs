namespace FreeW.Core.Model.Tests;

/// <summary>
/// Verifies the default values and HasAdjustments sentinel for the four new Picture Format &gt; Adjust
/// fields on <see cref="InlineImage"/>: BrightnessPct, ContrastPct, SaturationPct, TransparencyPct.
/// </summary>
public class ImageAdjustModelTests
{
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [Fact]
    public void NewInlineImage_HasNeutralAdjustDefaults()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);

        image.BrightnessPct.Should().Be(0);
        image.ContrastPct.Should().Be(0);
        image.SaturationPct.Should().Be(100);
        image.TransparencyPct.Should().Be(0);
        image.HasAdjustments.Should().BeFalse();
    }

    [Fact]
    public void HasAdjustments_True_WhenBrightnessNonZero()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BrightnessPct = 20 };
        image.HasAdjustments.Should().BeTrue();
    }

    [Fact]
    public void HasAdjustments_True_WhenContrastNonZero()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ContrastPct = -30 };
        image.HasAdjustments.Should().BeTrue();
    }

    [Fact]
    public void HasAdjustments_True_WhenSaturationNotOneHundred()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = 0 };
        image.HasAdjustments.Should().BeTrue();
    }

    [Fact]
    public void HasAdjustments_True_WhenTransparencyNonZero()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { TransparencyPct = 50 };
        image.HasAdjustments.Should().BeTrue();
    }

    [Fact]
    public void HasAdjustments_False_WhenAllNeutral()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            BrightnessPct   = 0,
            ContrastPct     = 0,
            SaturationPct   = 100,
            TransparencyPct = 0
        };
        image.HasAdjustments.Should().BeFalse();
    }
}
