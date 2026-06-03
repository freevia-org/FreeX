using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WorkbookThemeTests
{
    [Fact]
    public void Workbook_UsesOfficeThemeByDefault()
    {
        var workbook = new Workbook();

        workbook.Theme.Name.Should().Be("Office");
        workbook.Theme.MajorFontName.Should().Be("Aptos Display");
        workbook.Theme.MinorFontName.Should().Be("Aptos");
        workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(21, 96, 130));
        workbook.Theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));
    }

    [Fact]
    public void WorkbookTheme_WithColor_ReplacesOnlyRequestedSlot()
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent2,
            new CellColor(1, 2, 3));

        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(1, 2, 3));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1));
    }

    [Fact]
    public void WorkbookTheme_WithNativeThemeSupplementXml_TrimsAndClearsBlankXml()
    {
        WorkbookTheme.Office
            .WithNativeThemeSupplementXml("  <a:objectDefaults/>  ")
            .NativeThemeSupplementXml
            .Should()
            .Be("<a:objectDefaults/>");

        WorkbookTheme.Office
            .WithNativeThemeSupplementXml("  ")
            .NativeThemeSupplementXml
            .Should()
            .BeNull();
    }

    [Fact]
    public void WorkbookTheme_WithNativeFormatSchemeXml_InterpretsOuterShadowEffectDefaults()
    {
        var theme = WorkbookTheme.Office.WithNativeFormatSchemeXml(NativeFormatSchemeWithOuterShadow);

        theme.NativeFormatSchemeXml.Should().Contain("outerShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasShadow.Should().BeTrue();
        theme.EffectDefaults.ShadowOpacity.Should().BeApproximately(0.38, 0.0001);
        theme.EffectDefaults.ShadowOffsetX.Should().Be(0);
        theme.EffectDefaults.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void WorkbookTheme_WithNativeFormatSchemeXml_InterpretsPresetShadowEffectDefaults()
    {
        var theme = WorkbookTheme.Office.WithNativeFormatSchemeXml(NativeFormatSchemeWithPresetShadow);

        theme.NativeFormatSchemeXml.Should().Contain("prstShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasShadow.Should().BeTrue();
        theme.EffectDefaults.ShadowOpacity.Should().BeApproximately(0.5, 0.0001);
        theme.EffectDefaults.ShadowOffsetX.Should().Be(4);
        theme.EffectDefaults.ShadowOffsetY.Should().Be(0);
    }

    [Fact]
    public void WorkbookTheme_WithNativeFormatSchemeXml_InterpretsGlowEffectDefaults()
    {
        var theme = WorkbookTheme.Office.WithNativeFormatSchemeXml(NativeFormatSchemeWithGlow);

        theme.NativeFormatSchemeXml.Should().Contain("glow");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasShadow.Should().BeFalse();
        theme.EffectDefaults.HasGlow.Should().BeTrue();
        theme.EffectDefaults.GlowOpacity.Should().BeApproximately(0.42, 0.0001);
        theme.EffectDefaults.GlowRadius.Should().BeApproximately(4, 0.0001);
        theme.EffectDefaults.GlowColor.Should().Be(new CellColor(91, 155, 213));
    }

    [Fact]
    public void WorkbookTheme_WithEffects_RenamesNativeFormatSchemeAndKeepsEffectDefaults()
    {
        var theme = WorkbookTheme.Office
            .WithNativeFormatSchemeXml(NativeFormatSchemeWithOuterShadow)
            .WithEffects("Renamed Effects");

        theme.NativeFormatSchemeXml.Should().Contain("name=\"Renamed Effects\"");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.ShadowOpacity.Should().BeApproximately(0.38, 0.0001);
        theme.EffectDefaults.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void WorkbookTheme_WithNativeFormatSchemeXml_IgnoresWrongNamespaceEffectDefaults()
    {
        var theme = WorkbookTheme.Office.WithNativeFormatSchemeXml("""
            <fmtScheme xmlns="urn:wrong-theme-namespace" name="Wrong Effects">
              <effectStyleLst>
                <effectStyle><effectLst><outerShdw dist="19050"/></effectLst></effectStyle>
              </effectStyleLst>
            </fmtScheme>
            """);

        theme.NativeFormatSchemeXml.Should().Contain("Wrong Effects");
        theme.EffectDefaults.Should().BeNull();
    }

    [Fact]
    public void WorkbookTheme_WithSupplementalMetadata_CapturesAlternateSchemesAndObjectDefaults()
    {
        var alternate = new WorkbookThemeAlternateColorScheme(
            "Alternate",
            new Dictionary<WorkbookThemeColorSlot, CellColor>
            {
                [WorkbookThemeColorSlot.Accent1] = new(1, 2, 3)
            });

        var objectDefaults = new WorkbookThemeObjectDefaults(
            Shape: new WorkbookThemeShapeObjectDefault(
                FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                OutlineWidthPoints: 1.5));

        var theme = WorkbookTheme.Office.WithSupplementalMetadata(
            [alternate],
            hasObjectDefaults: true,
            objectDefaults);

        theme.HasObjectDefaults.Should().BeTrue();
        theme.ObjectDefaults.Should().Be(objectDefaults);
        theme.ObjectDefaults!.HasModeledDefaults.Should().BeTrue();
        theme.AlternateColorSchemes.Should().ContainSingle()
            .Which.GetColor(WorkbookThemeColorSlot.Accent1)
            .Should().Be(new CellColor(1, 2, 3));
    }

    [Theory]
    [InlineData(100, 150, 200, 0.0, 100, 150, 200)]
    [InlineData(100, 150, 200, 0.5, 178, 202, 228)]
    [InlineData(100, 150, 200, -0.25, 75, 112, 150)]
    [InlineData(100, 150, 200, 2.0, 255, 255, 255)]
    [InlineData(100, 150, 200, -2.0, 0, 0, 0)]
    public void WorkbookTheme_ResolveColor_AppliesExcelTint(
        byte r,
        byte g,
        byte b,
        double tint,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent1,
            new CellColor(r, g, b));

        theme.ResolveColor(WorkbookThemeColorSlot.Accent1, tint)
            .Should().Be(new CellColor(expectedR, expectedG, expectedB));
    }

    [Fact]
    public void CellStyle_ResolvesThemeColorReferencesWithTint()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(80, 120, 160))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(40, 80, 120));
        var style = new CellStyle
        {
            FontColor = CellColor.Black,
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.5),
            FillColor = CellColor.White,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            FillPatternColor = CellColor.Black,
            FillPatternThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.25)
        };

        style.ResolveFontColor(theme).Should().Be(new CellColor(178, 202, 228));
        style.ResolveFillColor(theme).Should().Be(new CellColor(60, 90, 120));
        style.ResolveFillPatternColor(theme).Should().Be(new CellColor(94, 124, 154));
    }

    private const string NativeFormatSchemeWithOuterShadow = """
        <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Imported Effects">
          <a:fillStyleLst/>
          <a:lnStyleLst/>
          <a:effectStyleLst>
            <a:effectStyle>
              <a:effectLst>
                <a:outerShdw blurRad="40000" dist="19050" dir="5400000" rotWithShape="0">
                  <a:srgbClr val="000000"><a:alpha val="38000"/></a:srgbClr>
                </a:outerShdw>
              </a:effectLst>
            </a:effectStyle>
          </a:effectStyleLst>
          <a:bgFillStyleLst/>
        </a:fmtScheme>
        """;

    private const string NativeFormatSchemeWithPresetShadow = """
        <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Imported Effects">
          <a:fillStyleLst/>
          <a:lnStyleLst/>
          <a:effectStyleLst>
            <a:effectStyle>
              <a:effectLst>
                <a:prstShdw prst="shdw1" dist="38100" dir="0">
                  <a:srgbClr val="000000"><a:alpha val="50000"/></a:srgbClr>
                </a:prstShdw>
              </a:effectLst>
            </a:effectStyle>
          </a:effectStyleLst>
          <a:bgFillStyleLst/>
        </a:fmtScheme>
        """;

    private const string NativeFormatSchemeWithGlow = """
        <a:fmtScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Imported Effects">
          <a:fillStyleLst/>
          <a:lnStyleLst/>
          <a:effectStyleLst>
            <a:effectStyle>
              <a:effectLst>
                <a:glow rad="38100">
                  <a:srgbClr val="5B9BD5"><a:alpha val="42000"/></a:srgbClr>
                </a:glow>
              </a:effectLst>
            </a:effectStyle>
          </a:effectStyleLst>
          <a:bgFillStyleLst/>
        </a:fmtScheme>
        """;
}
