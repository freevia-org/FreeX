using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO.Compression;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorkbookThemeReaderTests
{
    [Fact]
    public void Load_ReturnsOfficeThemeWhenThemePartIsMissing()
    {
        using var package = CreatePackage();

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Should().Be(WorkbookTheme.Office);
    }

    [Fact]
    public void Load_ReadsThemeNameFontsEffectsAndColorScheme()
    {
        using var package = CreatePackage(("xl/theme/theme1.xml", """
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
    public void LoadSave_PreservesThemeSupplementElementsBesideThemeElements()
    {
        using var package = CreatePackage(("xl/theme/theme1.xml", """
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
                  <a:spPr><a:solidFill><a:schemeClr val="accent1"/></a:solidFill></a:spPr>
                </a:spDef>
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
        using var reader = new StreamReader(archive.GetEntry("xl/theme/theme1.xml")!.Open());
        var savedXml = reader.ReadToEnd();

        savedXml.Should().Contain("objectDefaults");
        savedXml.Should().Contain("schemeClr val=\"accent1\"");
        savedXml.Should().Contain("extraClrSchemeLst");
        savedXml.Should().Contain("Alternate Colors");
        savedXml.Should().Contain("compatExt spid=\"1\"");
    }

    [Fact]
    public void Save_WritesModeledAlternateColorSchemesWhenSupplementXmlIsMissing()
    {
        using var package = CreatePackage();
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
        using var reader = new StreamReader(archive.GetEntry("xl/theme/theme1.xml")!.Open());
        var savedXml = reader.ReadToEnd();

        savedXml.Should().Contain("extraClrSchemeLst");
        savedXml.Should().Contain("Modeled Alternate");
        savedXml.Should().Contain("accent1");
        savedXml.Should().Contain("112233");
        savedXml.Should().Contain("hlink");
        savedXml.Should().Contain("445566");
    }

    [Fact]
    public void Save_IgnoresMalformedOrWrongNamespaceThemeSupplementXml()
    {
        using var package = CreatePackage();
        var theme = WorkbookTheme.Office.WithNativeThemeSupplementXml("""
            <wrong:objectDefaults xmlns:wrong="urn:not-drawingml"/>
            <a:objectDefaults xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:spDef/>
            </a:objectDefaults>
            """);

        XlsxWorkbookThemeWriter.Save(package, theme);
        package.Position = 0;

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        using var reader = new StreamReader(archive.GetEntry("xl/theme/theme1.xml")!.Open());
        var savedXml = reader.ReadToEnd();

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

    private static MemoryStream CreatePackage(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
