using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class WorkbookThemeEffectStyleTests
{
    [Fact]
    public void FromTheme_ReturnsNoShadowForOffice()
    {
        WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office).HasShadow.Should().BeFalse();
    }

    [Fact]
    public void FromTheme_ReturnsSubtleShadowForSubtleEffects()
    {
        var style = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Subtle"));

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().Be(0.18);
        style.ShadowOffsetX.Should().Be(2);
        style.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void FromTheme_ReturnsStrongerShadowForRefinedEffects()
    {
        var style = WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Refined"));

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().Be(0.28);
        style.ShadowOffsetX.Should().Be(3);
        style.ShadowOffsetY.Should().Be(3);
    }

    [Fact]
    public void FromTheme_UsesImportedFormatSchemeOuterShadow()
    {
        var theme = WorkbookTheme.Office
            .WithEffects("Office")
            .WithNativeFormatSchemeXml("""
                <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Imported Effects">
                  <a:effectStyleLst>
                    <a:effectStyle>
                      <a:effectLst>
                        <a:outerShdw blurRad="40000" dist="19050" dir="5400000" rotWithShape="0">
                          <a:srgbClr val="000000"><a:alpha val="38000"/></a:srgbClr>
                        </a:outerShdw>
                      </a:effectLst>
                    </a:effectStyle>
                  </a:effectStyleLst>
                </a:fmtScheme>
                """);

        var style = WorkbookThemeEffectStyle.FromTheme(theme);

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().BeApproximately(0.38, 0.0001);
        style.ShadowOffsetX.Should().Be(0);
        style.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void FromTheme_UsesImportedFormatSchemePresetShadow()
    {
        var theme = WorkbookTheme.Office
            .WithEffects("Office")
            .WithNativeFormatSchemeXml("""
                <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Imported Effects">
                  <a:effectStyleLst>
                    <a:effectStyle>
                      <a:effectLst>
                        <a:prstShdw prst="shdw1" dist="38100" dir="0">
                          <a:srgbClr val="000000"><a:alpha val="50000"/></a:srgbClr>
                        </a:prstShdw>
                      </a:effectLst>
                    </a:effectStyle>
                  </a:effectStyleLst>
                </a:fmtScheme>
                """);

        var style = WorkbookThemeEffectStyle.FromTheme(theme);

        style.HasShadow.Should().BeTrue();
        style.ShadowOpacity.Should().BeApproximately(0.5, 0.0001);
        style.ShadowOffsetX.Should().Be(4);
        style.ShadowOffsetY.Should().Be(0);
    }

    [Fact]
    public void FromTheme_TreatsUnknownEffectsAsOffice()
    {
        WorkbookThemeEffectStyle.FromTheme(WorkbookTheme.Office.WithEffects("Custom")).HasShadow.Should().BeFalse();
    }
}
