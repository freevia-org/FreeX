using FluentAssertions;
using Free.Shared.Drawing;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingMlColorSubstrateTests
{
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
}
