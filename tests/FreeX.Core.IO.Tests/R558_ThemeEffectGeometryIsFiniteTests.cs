using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r558: a workbook's theme carries its own format-scheme XML, and
/// <see cref="WorkbookTheme.WithNativeFormatSchemeXml"/> reads effect geometry straight out of it
/// -- shadow distance and direction, glow radius, soft-edge radius, inner-shadow blur. Every one
/// of those is file-controlled.
///
/// <para>Two readers let a non-finite value through. ReadPositiveCoordinatePixels guards with
/// <c>coordinate &lt;= 0</c>, the REJECT form r551 showed cannot stop Infinity (which fails the
/// comparison) or NaN (which fails every comparison); ReadAngleRadians had no range check at all.
/// The value then reaches <c>DrawingMlCoordinateUnits.EmuToPixels(double)</c>, which is a bare
/// division -- r550 guarded only the string overload -- so an infinite EMU becomes infinite
/// pixels of shadow offset or glow radius.</para>
///
/// <para>Both readers already return 0 for an absent or unparseable attribute, so a value that
/// cannot be used takes the path they already had.</para>
/// </summary>
public sealed class R558_ThemeEffectGeometryIsFiniteTests
{
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static string FormatScheme(string shadowDistance, string shadowDirection, string glowRadius) =>
        $"""
        <fmtScheme xmlns="{DrawingNs}" name="Probe">
          <effectStyleLst>
            <effectStyle>
              <effectLst>
                <outerShdw blurRad="40000" dist="{shadowDistance}" dir="{shadowDirection}">
                  <srgbClr val="000000"><alpha val="35000"/></srgbClr>
                </outerShdw>
                <glow rad="{glowRadius}"><srgbClr val="FF0000"/></glow>
              </effectLst>
            </effectStyle>
          </effectStyleLst>
        </fmtScheme>
        """;

    private static WorkbookThemeEffectDefaults? EffectsFor(
        string shadowDistance = "38100",
        string shadowDirection = "5400000",
        string glowRadius = "63500") =>
        WorkbookTheme.Office
            .WithNativeFormatSchemeXml(FormatScheme(shadowDistance, shadowDirection, glowRadius))
            .EffectDefaults;

    [Theory]
    [InlineData("1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_shadow_distance_that_is_not_finite_is_dropped(string hostile)
    {
        var effects = EffectsFor(shadowDistance: hostile);

        effects.Should().NotBeNull();
        double.IsFinite(effects!.ShadowOffsetX).Should().BeTrue("ShadowOffsetX was " + effects.ShadowOffsetX);
        double.IsFinite(effects.ShadowOffsetY).Should().BeTrue("ShadowOffsetY was " + effects.ShadowOffsetY);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void A_shadow_direction_that_is_not_finite_is_dropped(string hostile)
    {
        // Direction feeds sin/cos, so a non-finite angle poisons BOTH offsets even when the
        // distance itself is perfectly ordinary.
        var effects = EffectsFor(shadowDirection: hostile);

        effects.Should().NotBeNull();
        double.IsFinite(effects!.ShadowOffsetX).Should().BeTrue("ShadowOffsetX was " + effects.ShadowOffsetX);
        double.IsFinite(effects.ShadowOffsetY).Should().BeTrue("ShadowOffsetY was " + effects.ShadowOffsetY);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void A_glow_radius_that_is_not_finite_is_dropped(string hostile)
    {
        var effects = EffectsFor(glowRadius: hostile);

        effects.Should().NotBeNull();
        double.IsFinite(effects!.GlowRadius).Should().BeTrue("GlowRadius was " + effects.GlowRadius);
    }

    [Fact]
    public void Ordinary_theme_effects_are_still_read()
    {
        // Non-vacuity: a guard that zeroed every value would satisfy the assertions above while
        // silently discarding every real theme effect. 38100 EMU is 4 pixels at 9525 EMU/px, and
        // a 5400000 (90 degree) direction puts the whole offset on Y.
        var effects = EffectsFor();

        effects.Should().NotBeNull();
        effects!.GlowRadius.Should().BeApproximately(63500 / 9525d, 0.001);
        (Math.Abs(effects.ShadowOffsetX) + Math.Abs(effects.ShadowOffsetY))
            .Should().BeApproximately(38100 / 9525d, 0.001);
    }
}
