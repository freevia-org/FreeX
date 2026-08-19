using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorkbookThemeReaderTests
{
    [Fact]
    public void Load_ReturnsOfficeThemeWhenThemePartIsMissing()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage();

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Should().Be(WorkbookTheme.Office);
    }

    // default-masks-missing F1: a theme part that IS present but fails to parse (truncated,
    // corrupted, malformed XML) must never be silently collapsed onto the same
    // WorkbookTheme.Office fallback as "workbook legitimately has no custom theme" -- doing so let
    // XlsxWorkbookThemeWriter.Save permanently overwrite the original, still-present, still-broken
    // xl/theme/theme1.xml with a synthesized default on the very next save with no warning. The
    // reader must surface this as a distinct, typed failure instead of swallowing it.
    [Fact]
    public void Load_ThrowsThemePartCorruptExceptionWhenThemePartIsMalformedXml()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/theme/theme1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Broken Theme">
                  <a:themeElements>
                    <a:clrScheme name="Broken Colors">
                      <a:dk1><a:srgbClr val="010203"/></a:dk1>
                """));

        var act = () => XlsxWorkbookThemeReader.Load(package);

        act.Should().Throw<XlsxThemePartCorruptException>();
    }

    // Sibling regression check for the same call site via the internal ZipArchive overload, which
    // is what the production XlsxFileAdapter load path actually calls
    // (XlsxFileAdapter.cs, "workbookTheme = packageParts.HasTheme ? XlsxWorkbookThemeReader.Load(packageArchive) : ...").
    [Fact]
    public void Load_Archive_ThrowsThemePartCorruptExceptionWhenThemePartIsMalformedXml()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/theme/theme1.xml", "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">"));
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var act = () => XlsxWorkbookThemeReader.Load(archive);

        act.Should().Throw<XlsxThemePartCorruptException>()
            .WithInnerException<System.Xml.XmlException>();
    }

    // No-regression sibling: a theme part that resolves and parses cleanly must still round-trip
    // to a WorkbookTheme with no exception, exactly as before this fix -- only the corrupt-part
    // case above changed.
    [Fact]
    public void Load_StillReturnsParsedThemeWhenThemePartIsWellFormed()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Healthy Theme">
              <a:themeElements>
                <a:clrScheme name="Healthy Colors">
                  <a:dk1><a:srgbClr val="010203"/></a:dk1>
                  <a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
                  <a:dk2><a:srgbClr val="111213"/></a:dk2>
                  <a:lt2><a:srgbClr val="E0E1E2"/></a:lt2>
                  <a:accent1><a:srgbClr val="0C2238"/></a:accent1>
                  <a:accent2><a:srgbClr val="456789"/></a:accent2>
                  <a:accent3><a:srgbClr val="ABCDEF"/></a:accent3>
                  <a:accent4><a:srgbClr val="102030"/></a:accent4>
                  <a:accent5><a:srgbClr val="405060"/></a:accent5>
                  <a:accent6><a:srgbClr val="708090"/></a:accent6>
                  <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
                  <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="Healthy Fonts">
                  <a:majorFont><a:latin typeface="Major Test"/></a:majorFont>
                  <a:minorFont><a:latin typeface="Minor Test"/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Effects Test"/>
              </a:themeElements>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Name.Should().Be("Healthy Theme");
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
    }

    [Fact]
    public void XlsxFileAdapter_SaveDefaultTheme_RoundTripsOfficeTheme()
    {
        var workbook = new Workbook("DefaultTheme");
        workbook.AddSheet("Sheet1");
        var adapter = new XlsxFileAdapter();

        using var package = new MemoryStream();
        adapter.Save(workbook, package);
        package.Position = 0;

        var theme = adapter.Load(package).Theme;
        theme.Name.Should().Be(WorkbookTheme.Office.Name);
        theme.MajorFontName.Should().Be(WorkbookTheme.Office.MajorFontName);
        theme.MinorFontName.Should().Be(WorkbookTheme.Office.MinorFontName);
        theme.EffectsName.Should().Be(WorkbookTheme.Office.EffectsName);
        theme.HasObjectDefaults.Should().BeFalse();
        foreach (var slot in Enum.GetValues<WorkbookThemeColorSlot>())
            theme.GetColor(slot).Should().Be(WorkbookTheme.Office.GetColor(slot));
    }

    [Fact]
    public void Load_ReadsThemeNameFontsEffectsAndColorScheme()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="FreeX Test Theme">
              <a:themeElements>
                <a:clrScheme name="FreeX Colors">
                  <a:dk1><a:srgbClr val="010203"/></a:dk1>
                  <a:lt1><a:sysClr val="window" lastClr="FAFBFC"/></a:lt1>
                  <a:dk2><a:srgbClr val="111213"/></a:dk2>
                  <a:lt2><a:srgbClr val="E0E1E2"/></a:lt2>
                  <a:accent1><a:srgbClr val="0C2238"/></a:accent1>
                  <a:accent2><a:srgbClr val="456789"/></a:accent2>
                  <a:accent3><a:srgbClr val="ABCDEF"/></a:accent3>
                  <a:accent4><a:srgbClr val="102030"/></a:accent4>
                  <a:accent5><a:srgbClr val="405060"/></a:accent5>
                  <a:accent6><a:srgbClr val="708090"/></a:accent6>
                  <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
                  <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="FreeX Fonts">
                  <a:majorFont><a:latin typeface="Major Test"/></a:majorFont>
                  <a:minorFont><a:latin typeface="Minor Test"/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Effects Test"/>
              </a:themeElements>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Name.Should().Be("FreeX Test Theme");
        theme.MajorFontName.Should().Be("Major Test");
        theme.MinorFontName.Should().Be("Minor Test");
        theme.EffectsName.Should().Be("Effects Test");
        theme.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(1, 2, 3));
        theme.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(250, 251, 252));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));
    }

    [Fact]
    public void Load_ReadsFormatSchemeOuterShadowEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithDeepSchemesXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("outerShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasShadow.Should().BeTrue();
        theme.EffectDefaults.ShadowOpacity.Should().BeApproximately(0.38, 0.0001);
        theme.EffectDefaults.ShadowOffsetX.Should().Be(0);
        theme.EffectDefaults.ShadowOffsetY.Should().Be(2);
    }

    [Fact]
    public void Load_ReadsFormatSchemePresetShadowEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithPresetShadowXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("prstShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasShadow.Should().BeTrue();
        theme.EffectDefaults.ShadowOpacity.Should().BeApproximately(0.5, 0.0001);
        theme.EffectDefaults.ShadowOffsetX.Should().Be(4);
        theme.EffectDefaults.ShadowOffsetY.Should().Be(0);
    }

    [Fact]
    public void Load_ReadsFormatSchemeGlowEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithGlowXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("glow");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasGlow.Should().BeTrue();
        theme.EffectDefaults.GlowOpacity.Should().BeApproximately(0.42, 0.0001);
        theme.EffectDefaults.GlowRadius.Should().BeApproximately(4, 0.0001);
        theme.EffectDefaults.GlowColor.Should().Be(new CellColor(91, 155, 213));
    }

    [Fact]
    public void Load_ReadsFormatSchemeSoftEdgeEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithSoftEdgeXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("softEdge");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasSoftEdge.Should().BeTrue();
        theme.EffectDefaults.SoftEdgeRadius.Should().BeApproximately(4, 0.0001);
    }

    [Fact]
    public void Load_ReadsFormatSchemeInnerShadowEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithInnerShadowXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("innerShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasInnerShadow.Should().BeTrue();
        theme.EffectDefaults.InnerShadowOpacity.Should().BeApproximately(0.35, 0.0001);
        theme.EffectDefaults.InnerShadowOffsetX.Should().Be(0);
        theme.EffectDefaults.InnerShadowOffsetY.Should().Be(2);
        theme.EffectDefaults.InnerShadowBlurRadius.Should().BeApproximately(4, 0.0001);
    }

    [Fact]
    public void Load_ReadsFormatSchemeBevelAndThreeDRotationEffectDefaults()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithBevelAndThreeDRotationXml));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeFormatSchemeXml.Should().Contain("bevelT");
        theme.NativeFormatSchemeXml.Should().Contain("camera");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.HasBevel.Should().BeTrue();
        theme.EffectDefaults.HasThreeDRotation.Should().BeTrue();
        theme.EffectDefaults.HasAnyEffect.Should().BeTrue();
    }

    [Fact]
    public void LoadSave_PreservesThemeSupplementElementsBesideThemeElements()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Supplement Theme">
              <a:themeElements>
                <a:clrScheme name="Supplement Colors">
                  <a:dk1><a:srgbClr val="000000"/></a:dk1>
                  <a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
                  <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
                  <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
                  <a:accent1><a:srgbClr val="4F81BD"/></a:accent1>
                  <a:accent2><a:srgbClr val="C0504D"/></a:accent2>
                  <a:accent3><a:srgbClr val="9BBB59"/></a:accent3>
                  <a:accent4><a:srgbClr val="8064A2"/></a:accent4>
                  <a:accent5><a:srgbClr val="4BACC6"/></a:accent5>
                  <a:accent6><a:srgbClr val="F79646"/></a:accent6>
                  <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
                  <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="Supplement Fonts">
                  <a:majorFont><a:latin typeface="Cambria"/></a:majorFont>
                  <a:minorFont><a:latin typeface="Calibri"/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Supplement Effects"/>
              </a:themeElements>
              <a:objectDefaults>
                <a:spDef>
                  <a:spPr>
                    <a:solidFill><a:schemeClr val="accent1"><a:lumMod val="80000"/></a:schemeClr></a:solidFill>
                    <a:ln w="19050"><a:solidFill><a:srgbClr val="445566"/></a:solidFill></a:ln>
                  </a:spPr>
                </a:spDef>
                <a:lnDef>
                  <a:spPr><a:ln w="25400"><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></a:ln></a:spPr>
                </a:lnDef>
                <a:txDef>
                  <a:lstStyle>
                    <a:defRPr><a:solidFill><a:schemeClr val="tx1"/></a:solidFill><a:latin typeface="Aptos"/></a:defRPr>
                  </a:lstStyle>
                </a:txDef>
              </a:objectDefaults>
              <a:extraClrSchemeLst>
                <a:extraClrScheme>
                  <a:clrScheme name="Alternate Colors">
                    <a:dk1><a:srgbClr val="010101"/></a:dk1>
                    <a:lt1><a:srgbClr val="FEFEFE"/></a:lt1>
                  </a:clrScheme>
                </a:extraClrScheme>
              </a:extraClrSchemeLst>
              <a:extLst>
                <a:ext uri="{12345678-1234-1234-1234-123456789ABC}">
                  <a:compatExt spid="1"/>
                </a:ext>
              </a:extLst>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.NativeThemeSupplementXml.Should().Contain("objectDefaults");
        theme.NativeThemeSupplementXml.Should().Contain("extraClrSchemeLst");
        theme.NativeThemeSupplementXml.Should().Contain("extLst");
        theme.HasObjectDefaults.Should().BeTrue();
        theme.ObjectDefaults.Should().NotBeNull();
        theme.ObjectDefaults!.Shape.Should().Be(new WorkbookThemeShapeObjectDefault(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, -0.2),
            null,
            null,
            new CellColor(68, 85, 102),
            1.5));
        theme.ObjectDefaults.Line.Should().Be(new WorkbookThemeLineObjectDefault(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            null,
            2));
        theme.ObjectDefaults.Text.Should().Be(new WorkbookThemeTextObjectDefault(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            null,
            "Aptos"));
        theme.AlternateColorSchemes.Should().ContainSingle()
            .Which.Should().Match<WorkbookThemeAlternateColorScheme>(scheme =>
                scheme.Name == "Alternate Colors" &&
                scheme.GetColor(WorkbookThemeColorSlot.Dark1) == new CellColor(1, 1, 1) &&
                scheme.GetColor(WorkbookThemeColorSlot.Light1) == new CellColor(254, 254, 254) &&
                scheme.NativeColorSchemeXml != null &&
                scheme.NativeColorSchemeXml.Contains("Alternate Colors"));

        package.Position = 0;
        XlsxWorkbookThemeWriter.Save(package, theme);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        var savedXml = LoadThemeXml(archive);

        savedXml.Should().Contain("objectDefaults");
        savedXml.Should().Contain("schemeClr val=\"accent1\"");
        savedXml.Should().Contain("extraClrSchemeLst");
        savedXml.Should().Contain("Alternate Colors");
        savedXml.Should().Contain("compatExt spid=\"1\"");
    }

    [Fact]
    public void Save_WritesModeledObjectDefaultsWhenSupplementXmlIsMissing()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage();
        var theme = WorkbookTheme.Office.WithSupplementalMetadata(
            alternateColorSchemes: [],
            hasObjectDefaults: true,
            objectDefaults: new WorkbookThemeObjectDefaults(
                new WorkbookThemeShapeObjectDefault(
                    FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.25),
                    OutlineThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
                    OutlineWidthPoints: 1.25),
                new WorkbookThemeLineObjectDefault(
                    StrokeColor: new CellColor(10, 20, 30),
                    StrokeWidthPoints: 2.5),
                new WorkbookThemeTextObjectDefault(
                    TextThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
                    Typeface: "Aptos")));

        XlsxWorkbookThemeWriter.Save(package, theme);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        var savedXml = LoadThemeXml(archive);

        savedXml.Should().Contain("objectDefaults");
        savedXml.Should().Contain("spDef");
        savedXml.Should().Contain("schemeClr val=\"accent3\"");
        savedXml.Should().Contain("lumOff val=\"25000\"");
        savedXml.Should().Contain("ln w=\"15875\"");
        savedXml.Should().Contain("srgbClr val=\"0A141E\"");
        savedXml.Should().Contain("txDef");
        savedXml.Should().Contain("typeface=\"Aptos\"");
    }

    [Fact]
    public void Save_WritesModeledAlternateColorSchemesWhenSupplementXmlIsMissing()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage();
        var theme = WorkbookTheme.Office.WithSupplementalMetadata(
            [
                new WorkbookThemeAlternateColorScheme(
                    "Modeled Alternate",
                    new Dictionary<WorkbookThemeColorSlot, CellColor>
                    {
                        [WorkbookThemeColorSlot.Accent1] = new(17, 34, 51),
                        [WorkbookThemeColorSlot.Hyperlink] = new(68, 85, 102)
                    })
            ],
            hasObjectDefaults: false);

        XlsxWorkbookThemeWriter.Save(package, theme);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        var savedXml = LoadThemeXml(archive);

        savedXml.Should().Contain("extraClrSchemeLst");
        savedXml.Should().Contain("Modeled Alternate");
        savedXml.Should().Contain("accent1");
        savedXml.Should().Contain("112233");
        savedXml.Should().Contain("hlink");
        savedXml.Should().Contain("445566");
    }

    [Fact]
    public void XlsxNativeJsonBridge_PreservesNativeThemeSchemeDetails()
    {
        using var source = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithDeepSchemesXml));
        var workbook = new Workbook("ThemeBridge")
        {
            Theme = XlsxWorkbookThemeReader.Load(source)
        };
        workbook.AddSheet("S1");

        var nativeAdapter = new NativeJsonAdapter();
        using var nativeJson = new MemoryStream();
        nativeAdapter.Save(workbook, nativeJson);

        nativeJson.Position = 0;
        var loaded = nativeAdapter.Load(nativeJson);

        loaded.Theme.Name.Should().Be("Native JSON Bridge Theme");
        loaded.Theme.NativeColorSchemeXml.Should().Contain("lumMod");
        loaded.Theme.NativeFontSchemeXml.Should().Contain("typeface=\"Major East Asia\"");
        loaded.Theme.NativeFormatSchemeXml.Should().Contain("outerShdw");
        loaded.Theme.EffectDefaults.Should().NotBeNull();
        loaded.Theme.EffectDefaults!.ShadowOpacity.Should().BeApproximately(0.38, 0.0001);
        loaded.Theme.EffectDefaults.ShadowOffsetY.Should().Be(2);
        loaded.Theme.NativeThemeSupplementXml.Should().Contain("extraClrSchemeLst");
        loaded.Theme.NativeThemeSupplementXml.Should().Contain("compatExt");
        loaded.Theme.AlternateColorSchemes.Should().ContainSingle()
            .Which.NativeColorSchemeXml.Should().Contain("Bridge Alternate");
        loaded.Theme.ObjectDefaults!.Shape.Should().Be(new WorkbookThemeShapeObjectDefault(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, -0.2),
            null,
            null,
            new CellColor(68, 85, 102),
            1.5));

        using var target = XlsxPackageTestFixtures.CreatePackage();
        XlsxWorkbookThemeWriter.Save(target, loaded.Theme);
        target.Position = 0;

        using var archive = new ZipArchive(target, ZipArchiveMode.Read, leaveOpen: false);
        var savedTheme = LoadThemeDocument(archive);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var themeElements = savedTheme.Root!.Element(drawingNs + "themeElements")!;

        themeElements
            .Element(drawingNs + "clrScheme")!
            .Element(drawingNs + "accent1")!
            .Element(drawingNs + "srgbClr")!
            .Element(drawingNs + "lumMod")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("75000");
        themeElements
            .Element(drawingNs + "fontScheme")!
            .Element(drawingNs + "majorFont")!
            .Element(drawingNs + "ea")!
            .Attribute("typeface")!
            .Value
            .Should()
            .Be("Major East Asia");
        themeElements
            .Element(drawingNs + "fmtScheme")!
            .Element(drawingNs + "effectStyleLst")!
            .Element(drawingNs + "effectStyle")!
            .Element(drawingNs + "effectLst")!
            .Element(drawingNs + "outerShdw")!
            .Attribute("dist")!
            .Value
            .Should()
            .Be("19050");
        savedTheme.Root!.Element(drawingNs + "extraClrSchemeLst")!
            .Descendants(drawingNs + "clrScheme")
            .Should()
            .ContainSingle(scheme => scheme.Attribute("name") != null &&
                                     scheme.Attribute("name")!.Value == "Bridge Alternate");
        savedTheme.Root!.Element(drawingNs + "extLst")!
            .Descendants(drawingNs + "compatExt")
            .Should()
            .ContainSingle(element => element.Attribute("spid") != null &&
                                      element.Attribute("spid")!.Value == "1");
    }

    [Fact]
    public void Save_PreservesNativeFormatSchemeDetailsWhenEffectNameChanges()
    {
        using var source = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithDeepSchemesXml));
        var theme = XlsxWorkbookThemeReader.Load(source)
            .WithEffects("Renamed Effects");

        theme.NativeFormatSchemeXml.Should().Contain("outerShdw");
        theme.EffectDefaults.Should().NotBeNull();
        theme.EffectDefaults!.ShadowOffsetY.Should().Be(2);

        using var target = XlsxPackageTestFixtures.CreatePackage();
        XlsxWorkbookThemeWriter.Save(target, theme);
        target.Position = 0;

        using var archive = new ZipArchive(target, ZipArchiveMode.Read, leaveOpen: false);
        var savedTheme = LoadThemeDocument(archive);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var formatScheme = savedTheme.Root!
            .Element(drawingNs + "themeElements")!
            .Element(drawingNs + "fmtScheme")!;

        formatScheme.Attribute("name")!.Value.Should().Be("Renamed Effects");
        formatScheme
            .Element(drawingNs + "effectStyleLst")!
            .Element(drawingNs + "effectStyle")!
            .Element(drawingNs + "effectLst")!
            .Element(drawingNs + "outerShdw")!
            .Attribute("dist")!
            .Value
            .Should()
            .Be("19050");
    }

    [Fact]
    public void Save_IgnoresMalformedOrWrongNamespaceThemeSupplementXml()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage();
        var theme = WorkbookTheme.Office.WithNativeThemeSupplementXml("""
            <wrong:objectDefaults xmlns:wrong="urn:not-drawingml"/>
            <a:objectDefaults xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:spDef/>
            </a:objectDefaults>
            """);

        XlsxWorkbookThemeWriter.Save(package, theme);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        var savedXml = LoadThemeXml(archive);

        savedXml.Should().Contain("objectDefaults");
        savedXml.Should().NotContain("urn:not-drawingml");
    }

    [Theory]
    [InlineData("FF0C2238", 12, 34, 56)]
    [InlineData("#0C2238", 12, 34, 56)]
    public void TryReadCellColor_ReadsXlsxRgbAttributes(string rgb, byte r, byte g, byte b)
    {
        var element = System.Xml.Linq.XElement.Parse($"""<color rgb="{rgb}"/>""");

        XlsxColorReader.TryReadCellColor(element, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    private static XDocument LoadThemeDocument(ZipArchive archive)
    {
        return XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/theme/theme1.xml", "xl/theme/theme1.xml");
    }

    private static string LoadThemeXml(ZipArchive archive)
    {
        return LoadThemeDocument(archive).ToString(SaveOptions.DisableFormatting);
    }

    private const string NativeThemeWithDeepSchemesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Native JSON Bridge Theme">
          <a:themeElements>
            <a:clrScheme name="Bridge Colors">
              <a:dk1><a:srgbClr val="010203"/></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FAFBFC"/></a:lt1>
              <a:dk2><a:srgbClr val="44546A"/></a:dk2>
              <a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
              <a:accent1><a:srgbClr val="0C2238"><a:lumMod val="75000"/></a:srgbClr></a:accent1>
              <a:accent2><a:srgbClr val="E97132"/></a:accent2>
              <a:accent3><a:srgbClr val="196B24"/></a:accent3>
              <a:accent4><a:srgbClr val="0F9ED5"/></a:accent4>
              <a:accent5><a:srgbClr val="A02B93"/></a:accent5>
              <a:accent6><a:srgbClr val="4EA72E"/></a:accent6>
              <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
              <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Bridge Fonts">
              <a:majorFont>
                <a:latin typeface="Major Native"/>
                <a:ea typeface="Major East Asia"/>
                <a:cs typeface="Major Complex"/>
                <a:font script="Jpan" typeface="Yu Gothic"/>
              </a:majorFont>
              <a:minorFont>
                <a:latin typeface="Minor Native"/>
                <a:ea typeface="Minor East Asia"/>
                <a:cs typeface="Minor Complex"/>
              </a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Bridge Effects">
              <a:fillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:fillStyleLst>
              <a:lnStyleLst>
                <a:ln w="9525" cap="flat" cmpd="sng" algn="ctr">
                  <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                  <a:prstDash val="solid"/>
                </a:ln>
              </a:lnStyleLst>
              <a:effectStyleLst>
                <a:effectStyle>
                  <a:effectLst>
                    <a:outerShdw blurRad="40000" dist="19050" dir="5400000" rotWithShape="0">
                      <a:srgbClr val="000000"><a:alpha val="38000"/></a:srgbClr>
                    </a:outerShdw>
                  </a:effectLst>
                </a:effectStyle>
              </a:effectStyleLst>
              <a:bgFillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:bgFillStyleLst>
            </a:fmtScheme>
          </a:themeElements>
          <a:objectDefaults>
            <a:spDef>
              <a:spPr>
                <a:solidFill><a:schemeClr val="accent1"><a:lumMod val="80000"/></a:schemeClr></a:solidFill>
                <a:ln w="19050"><a:solidFill><a:srgbClr val="445566"/></a:solidFill></a:ln>
              </a:spPr>
            </a:spDef>
          </a:objectDefaults>
          <a:extraClrSchemeLst>
            <a:extraClrScheme>
              <a:clrScheme name="Bridge Alternate">
                <a:accent1><a:srgbClr val="112233"/></a:accent1>
                <a:hlink><a:srgbClr val="445566"/></a:hlink>
              </a:clrScheme>
            </a:extraClrScheme>
          </a:extraClrSchemeLst>
          <a:extLst>
            <a:ext uri="{12345678-1234-1234-1234-123456789ABC}">
              <a:compatExt spid="1"/>
            </a:ext>
          </a:extLst>
        </a:theme>
        """;

    private const string NativeThemeWithPresetShadowXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Preset Shadow Theme">
          <a:themeElements>
            <a:fmtScheme name="Preset Effects">
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
          </a:themeElements>
        </a:theme>
        """;

    private const string NativeThemeWithGlowXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Glow Theme">
          <a:themeElements>
            <a:fmtScheme name="Glow Effects">
              <a:effectStyleLst>
                <a:effectStyle>
                  <a:effectLst>
                    <a:glow rad="38100">
                      <a:srgbClr val="5B9BD5"><a:alpha val="42000"/></a:srgbClr>
                    </a:glow>
                  </a:effectLst>
                </a:effectStyle>
              </a:effectStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    private const string NativeThemeWithSoftEdgeXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Soft Edge Theme">
          <a:themeElements>
            <a:fmtScheme name="Soft Edge Effects">
              <a:effectStyleLst>
                <a:effectStyle>
                  <a:effectLst>
                    <a:softEdge rad="38100"/>
                  </a:effectLst>
                </a:effectStyle>
              </a:effectStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    private const string NativeThemeWithInnerShadowXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Inner Shadow Theme">
          <a:themeElements>
            <a:fmtScheme name="Inner Shadow Effects">
              <a:effectStyleLst>
                <a:effectStyle>
                  <a:effectLst>
                    <a:innerShdw blurRad="38100" dist="19050" dir="5400000">
                      <a:srgbClr val="000000"><a:alpha val="35000"/></a:srgbClr>
                    </a:innerShdw>
                  </a:effectLst>
                </a:effectStyle>
              </a:effectStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    private const string NativeThemeWithBevelAndThreeDRotationXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Bevel Rotation Theme">
          <a:themeElements>
            <a:fmtScheme name="Bevel Rotation Effects">
              <a:effectStyleLst>
                <a:effectStyle>
                  <a:effectLst/>
                  <a:scene3d>
                    <a:camera prst="orthographicFront"/>
                  </a:scene3d>
                  <a:sp3d>
                    <a:bevelT w="76200" h="38100"/>
                  </a:sp3d>
                </a:effectStyle>
              </a:effectStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;
}
