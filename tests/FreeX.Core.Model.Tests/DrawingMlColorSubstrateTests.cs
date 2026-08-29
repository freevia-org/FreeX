using FluentAssertions;
using Free.Shared.Drawing;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingMlColorSubstrateTests
{
    [Theory]
    [InlineData("0a141e", 0x0A, 0x14, 0x1E)]
    [InlineData("#0A141E", 0x0A, 0x14, 0x1E)]
    [InlineData(" FFFFFF ", 0xFF, 0xFF, 0xFF)]
    public void RgbColor_TryParseHexRgb_ReadsSixDigitDrawingMlValues(
        string text,
        int expectedR,
        int expectedG,
        int expectedB)
    {
        DrawingMlRgbColor.TryParseHexRgb(text, out var color).Should().BeTrue();
        color.Should().Be(new DrawingMlRgbColor((byte)expectedR, (byte)expectedG, (byte)expectedB));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("FF001122")]
    [InlineData("GG0011")]
    public void RgbColor_TryParseHexRgb_RejectsUnsupportedDrawingMlValues(string? text)
    {
        DrawingMlRgbColor.TryParseHexRgb(text, out var color).Should().BeFalse();
        color.Should().Be(new DrawingMlRgbColor(0, 0, 0));
    }

    [Fact]
    public void RgbColor_ToHexRgb_WritesUppercaseBareDrawingMlValue()
    {
        var color = new DrawingMlRgbColor(0x0A, 0x14, 0x1E);

        color.ToHexRgb().Should().Be("0A141E");
        color.ToString().Should().Be("#0A141E");
    }

    [Theory]
    [InlineData("dk1", DrawingMlThemeColorSlot.Dark1)]
    [InlineData("tx1", DrawingMlThemeColorSlot.Dark1)]
    [InlineData("lt1", DrawingMlThemeColorSlot.Light1)]
    [InlineData("bg1", DrawingMlThemeColorSlot.Light1)]
    [InlineData("dk2", DrawingMlThemeColorSlot.Dark2)]
    [InlineData("tx2", DrawingMlThemeColorSlot.Dark2)]
    [InlineData("lt2", DrawingMlThemeColorSlot.Light2)]
    [InlineData("bg2", DrawingMlThemeColorSlot.Light2)]
    [InlineData("accent6", DrawingMlThemeColorSlot.Accent6)]
    [InlineData("hlink", DrawingMlThemeColorSlot.Hyperlink)]
    [InlineData("folHlink", DrawingMlThemeColorSlot.FollowedHyperlink)]
    public void ThemeColorSlotMapper_MapsDrawingMlRoles(string roleName, DrawingMlThemeColorSlot expectedSlot)
    {
        DrawingMlThemeColorSlotMapper.TryMapRole(roleName, out var slot).Should().BeTrue();
        slot.Should().Be(expectedSlot);
    }

    [Fact]
    public void ThemeColorSlotMapper_AppliesEffectiveColorMapBeforeDefaultRoles()
    {
        var effectiveColorMap = new Dictionary<string, string>
        {
            ["tx1"] = "lt1",
            ["accent1"] = "accent2"
        };

        DrawingMlThemeColorSlotMapper.MapRoleToSlot("tx1", effectiveColorMap)
            .Should().Be(DrawingMlThemeColorSlot.Light1);
        DrawingMlThemeColorSlotMapper.MapRoleToSlot("accent1", effectiveColorMap)
            .Should().Be(DrawingMlThemeColorSlot.Accent2);
        DrawingMlThemeColorSlotMapper.MapRoleToSlot("bg1", effectiveColorMap)
            .Should().Be(DrawingMlThemeColorSlot.Light1);
    }

    [Fact]
    public void ThemeColorSlotMapper_ExposesCanonicalColorSchemeElements()
    {
        DrawingMlThemeColorSlotMapper.ColorSchemeElements
            .Should()
            .Equal(
                (DrawingMlThemeColorSlot.Dark1, "dk1"),
                (DrawingMlThemeColorSlot.Light1, "lt1"),
                (DrawingMlThemeColorSlot.Dark2, "dk2"),
                (DrawingMlThemeColorSlot.Light2, "lt2"),
                (DrawingMlThemeColorSlot.Accent1, "accent1"),
                (DrawingMlThemeColorSlot.Accent2, "accent2"),
                (DrawingMlThemeColorSlot.Accent3, "accent3"),
                (DrawingMlThemeColorSlot.Accent4, "accent4"),
                (DrawingMlThemeColorSlot.Accent5, "accent5"),
                (DrawingMlThemeColorSlot.Accent6, "accent6"),
                (DrawingMlThemeColorSlot.Hyperlink, "hlink"),
                (DrawingMlThemeColorSlot.FollowedHyperlink, "folHlink"));
    }

    [Fact]
    public void ColorTransform_AppliesDrawingMlRgbAndLuminanceMath()
    {
        DrawingMlColorTransform.ApplyTint(new DrawingMlRgbColor(101, 151, 201), 0.5)
            .Should().Be(new DrawingMlRgbColor(178, 203, 228));
        DrawingMlColorTransform.ApplyShade(new DrawingMlRgbColor(100, 150, 200), 0.5)
            .Should().Be(new DrawingMlRgbColor(50, 75, 100));
        DrawingMlColorTransform.ApplyLuminance(new DrawingMlRgbColor(64, 64, 64), 0.5, 0.25)
            .Should().Be(new DrawingMlRgbColor(96, 96, 96));
    }

    // Expected values below are derived independently from the DrawingML/ECMA-376 §20.1.2.3.25
    // (hueMod) / §20.1.2.3.32 (satMod) transform formulas -- h' = clamp(h * hueMod, 0, 1),
    // s' = clamp(s * satMod, 0, 1) applied in HSL space -- computed with a standalone reference
    // RGB<->HLS implementation (the same textbook algorithm as Python's colorsys module), NOT by
    // reading back whatever FreeX's own writer currently emits. Round 170 finding shared-theme-colors F1.
    [Fact]
    public void ColorTransform_AppliesSaturationModifier()
    {
        // (200,50,50) has HLS ~ (h=0, l=0.4902, s=0.6). satMod=0.5 halves the saturation.
        DrawingMlColorTransform.ApplySaturation(new DrawingMlRgbColor(200, 50, 50), 0.5)
            .Should().Be(new DrawingMlRgbColor(162, 88, 88));

        // satMod=0.0 desaturates fully to a mid-gray at the same luminance.
        DrawingMlColorTransform.ApplySaturation(new DrawingMlRgbColor(200, 50, 50), 0.0)
            .Should().Be(new DrawingMlRgbColor(125, 125, 125));

        // satMod=1.0 (100%) is documented as a no-op and must return the identical color.
        DrawingMlColorTransform.ApplySaturation(new DrawingMlRgbColor(120, 80, 200), 1.0)
            .Should().Be(new DrawingMlRgbColor(120, 80, 200));
    }

    [Fact]
    public void ColorTransform_AppliesHueModifier()
    {
        // (50,50,200) is blue (h=2/3). hueMod=0.5 halves the hue angle, rotating it to green (h=1/3).
        DrawingMlColorTransform.ApplyHue(new DrawingMlRgbColor(50, 50, 200), 0.5)
            .Should().Be(new DrawingMlRgbColor(50, 200, 50));

        // hueMod=0.0 collapses the hue angle to 0 (red), independent of the original hue.
        DrawingMlColorTransform.ApplyHue(new DrawingMlRgbColor(50, 50, 200), 0.0)
            .Should().Be(new DrawingMlRgbColor(200, 50, 50));

        // hueMod=1.0 (100%) is documented as a no-op and must return the identical color.
        DrawingMlColorTransform.ApplyHue(new DrawingMlRgbColor(120, 80, 200), 1.0)
            .Should().Be(new DrawingMlRgbColor(120, 80, 200));
    }

    [Fact]
    public void ColorTransform_Apply_CombinesSatModAndHueModWithLumTintShade()
    {
        // (30,120,200) with satMod=0.6 and hueMod=0.8 (lumMod/lumOff/tint/shade left neutral).
        DrawingMlColorTransform.Apply(
                new DrawingMlRgbColor(30, 120, 200),
                satMod: 0.6,
                hueMod: 0.8)
            .Should().Be(new DrawingMlRgbColor(64, 166, 143));
    }

    [Fact]
    public void ColorTransform_Apply_WithNeutralSatModAndHueMod_MatchesPreExistingLumTintShadeBehavior()
    {
        // Sibling no-regression case: callers that only pass lumMod/lumOff/tint/shade (the pre-fix
        // parameter set) must see byte-identical output now that satMod/hueMod default to 1.0 (no-op).
        DrawingMlColorTransform.Apply(
                new DrawingMlRgbColor(101, 151, 201),
                lumMod: 1.0,
                lumOff: 0.0,
                tint: 0.5,
                shade: 1.0)
            .Should().Be(DrawingMlColorTransform.ApplyTint(new DrawingMlRgbColor(101, 151, 201), 0.5));

        DrawingMlColorTransform.Apply(
                new DrawingMlRgbColor(64, 64, 64),
                lumMod: 0.5,
                lumOff: 0.25,
                tint: 1.0,
                shade: 1.0)
            .Should().Be(new DrawingMlRgbColor(96, 96, 96));
    }
}
