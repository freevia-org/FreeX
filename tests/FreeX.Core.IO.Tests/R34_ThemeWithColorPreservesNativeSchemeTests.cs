using FluentAssertions;
using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R34-io-theme-colors-deep-2: WorkbookTheme.WithColor used to null out the entire
/// NativeColorSchemeXml whenever a single slot changed, forcing the writer to regenerate all
/// 12 clrScheme slots from scratch on save. That converted untouched sysClr entries (dk1/lt1
/// bound to windowText/window) into baked srgbClr values and dropped the clrScheme "name"
/// attribute. WithColor must instead patch only the changed slot in place.
/// </summary>
public sealed class R34_ThemeWithColorPreservesNativeSchemeTests
{
    private const string NativeThemeWithSysClrXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Custom Theme">
          <a:themeElements>
            <a:clrScheme name="Custom Colors">
              <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
              <a:dk2><a:srgbClr val="44546A"/></a:dk2>
              <a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
              <a:accent1><a:srgbClr val="4472C4"/></a:accent1>
              <a:accent2><a:srgbClr val="ED7D31"/></a:accent2>
              <a:accent3><a:srgbClr val="A5A5A5"/></a:accent3>
              <a:accent4><a:srgbClr val="FFC000"/></a:accent4>
              <a:accent5><a:srgbClr val="5B9BD5"/></a:accent5>
              <a:accent6><a:srgbClr val="70AD47"/></a:accent6>
              <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
              <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Custom Fonts">
              <a:majorFont><a:latin typeface="Calibri Light"/></a:majorFont>
              <a:minorFont><a:latin typeface="Calibri"/></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Custom Effects"/>
          </a:themeElements>
        </a:theme>
        """;

    [Fact]
    public void WithColor_ChangingOneAccentSlot_PreservesSysClrSlotsAndSchemeName()
    {
        using var source = XlsxPackageTestFixtures.CreatePackage(("xl/theme/theme1.xml", NativeThemeWithSysClrXml));
        var theme = XlsxWorkbookThemeReader.Load(source);
        theme.NativeColorSchemeXml.Should().Contain("sysClr");

        var updated = theme.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30));

        updated.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(10, 20, 30));
        // Untouched slots must keep their original values.
        updated.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(0, 0, 0));
        updated.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(255, 255, 255));

        using var target = XlsxPackageTestFixtures.CreatePackage();
        XlsxWorkbookThemeWriter.Save(target, updated);
        target.Position = 0;

        using var archive = new ZipArchive(target, ZipArchiveMode.Read, leaveOpen: false);
        var savedTheme = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/theme/theme1.xml");
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var colorScheme = savedTheme.Root!
            .Element(drawingNs + "themeElements")!
            .Element(drawingNs + "clrScheme")!;

        // The clrScheme name and the untouched sysClr slots must survive the single-slot edit.
        colorScheme.Attribute("name")!.Value.Should().Be("Custom Colors");
        colorScheme.Element(drawingNs + "dk1")!.Element(drawingNs + "sysClr")!.Attribute("val")!.Value
            .Should().Be("windowText");
        colorScheme.Element(drawingNs + "lt1")!.Element(drawingNs + "sysClr")!.Attribute("val")!.Value
            .Should().Be("window");

        // The edited slot must reflect the new color as srgbClr.
        colorScheme.Element(drawingNs + "accent1")!.Element(drawingNs + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("0A141E");

        // A sibling untouched srgbClr slot must be preserved unchanged too.
        colorScheme.Element(drawingNs + "accent2")!.Element(drawingNs + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("ED7D31");
    }

    [Fact]
    public void WithColor_WithNoNativeSchemeXml_StillRegeneratesFullSchemeFromModel()
    {
        // Sibling already-working case: when there is no native color-scheme XML to preserve
        // (e.g. a fresh in-memory theme), WithColor must keep working as before and the writer
        // must still emit a complete, schema-valid 12-slot clrScheme.
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(1, 2, 3));

        theme.NativeColorSchemeXml.Should().BeNull();
        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(1, 2, 3));

        using var target = XlsxPackageTestFixtures.CreatePackage();
        XlsxWorkbookThemeWriter.Save(target, theme);
        target.Position = 0;

        using var archive = new ZipArchive(target, ZipArchiveMode.Read, leaveOpen: false);
        var savedTheme = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/theme/theme1.xml");
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var colorScheme = savedTheme.Root!
            .Element(drawingNs + "themeElements")!
            .Element(drawingNs + "clrScheme")!;

        colorScheme.Element(drawingNs + "accent2")!.Element(drawingNs + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("010203");
        colorScheme.Elements().Should().HaveCount(12);
    }
}
