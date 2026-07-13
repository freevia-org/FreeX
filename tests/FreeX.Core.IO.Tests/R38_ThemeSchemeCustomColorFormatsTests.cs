using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R38-io-theme-scheme-2-1: clrScheme slots using hslClr/scrgbClr/prstClr (instead of
/// srgbClr/sysClr) must load their actual custom color, not silently fall back to the
/// hardcoded Office default.
/// </summary>
public sealed class R38_ThemeSchemeCustomColorFormatsTests
{
    [Fact]
    public void Load_ReadsHslScRgbAndPresetClrThemeColors_InsteadOfOfficeDefault()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Custom Color Formats Theme">
              <a:themeElements>
                <a:clrScheme name="Custom Color Formats">
                  <a:dk1><a:srgbClr val="000000"/></a:dk1>
                  <a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
                  <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
                  <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
                  <a:accent1><a:hslClr hue="12600000" sat="50000" lum="40000"/></a:accent1>
                  <a:accent2><a:scrgbClr r="100000" g="0" b="0"/></a:accent2>
                  <a:accent3><a:scrgbClr r="50000" g="0" b="0"/></a:accent3>
                  <a:accent4><a:prstClr val="green"/></a:accent4>
                  <a:accent5><a:srgbClr val="405060"/></a:accent5>
                  <a:accent6><a:srgbClr val="708090"/></a:accent6>
                  <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
                  <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="Custom Fonts">
                  <a:majorFont><a:latin typeface="Cambria"/></a:majorFont>
                  <a:minorFont><a:latin typeface="Calibri"/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Custom Effects"/>
              </a:themeElements>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        // hslClr: hue=210deg, sat=50%, lum=40% -> RGB(51,102,153).
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(51, 102, 153));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().NotBe(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1));

        // scrgbClr: full-intensity red component (r=100%) -> pure red regardless of gamma curve.
        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(255, 0, 0));

        // scrgbClr: half-intensity (linear) red component must be gamma-encoded to sRGB,
        // i.e. brighter than a naive linear pass-through (which would land near 128) and
        // still pure in the red channel only.
        var accent3 = theme.GetColor(WorkbookThemeColorSlot.Accent3);
        accent3.R.Should().BeInRange((byte)175, (byte)200);
        accent3.G.Should().Be(0);
        accent3.B.Should().Be(0);
        accent3.Should().NotBe(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent3));

        // prstClr "green" is the DrawingML/CSS preset 008000 (not the Office default accent color).
        theme.GetColor(WorkbookThemeColorSlot.Accent4).Should().Be(new CellColor(0, 128, 0));
        theme.GetColor(WorkbookThemeColorSlot.Accent4).Should().NotBe(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent4));
    }

    [Fact]
    public void Load_StillReadsSrgbAndSysClrThemeColors_AlongsideNewColorForms()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Mixed Color Formats Theme">
              <a:themeElements>
                <a:clrScheme name="Mixed Color Formats">
                  <a:dk1><a:srgbClr val="010203"/></a:dk1>
                  <a:lt1><a:sysClr val="window" lastClr="FAFBFC"/></a:lt1>
                  <a:dk2><a:srgbClr val="111213"/></a:dk2>
                  <a:lt2><a:srgbClr val="E0E1E2"/></a:lt2>
                  <a:accent1><a:srgbClr val="0C2238"/></a:accent1>
                  <a:accent2><a:hslClr hue="0" sat="100000" lum="50000"/></a:accent2>
                  <a:accent3><a:srgbClr val="ABCDEF"/></a:accent3>
                  <a:accent4><a:prstClr val="white"/></a:accent4>
                  <a:accent5><a:srgbClr val="405060"/></a:accent5>
                  <a:accent6><a:srgbClr val="708090"/></a:accent6>
                  <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
                  <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
                </a:clrScheme>
              </a:themeElements>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        // srgbClr / sysClr paths are unaffected by the new hslClr/scrgbClr/prstClr handling.
        theme.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(1, 2, 3));
        theme.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(250, 251, 252));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));

        // hslClr hue=0,sat=100%,lum=50% is pure red.
        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(255, 0, 0));

        // prstClr "white" is 255,255,255.
        theme.GetColor(WorkbookThemeColorSlot.Accent4).Should().Be(new CellColor(255, 255, 255));
    }
}
