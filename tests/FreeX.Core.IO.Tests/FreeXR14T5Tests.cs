using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-14 tightened-loop fixes, bucket T5:
///  - R14-cell-styles-themes-3: a recovered named cell style's cellStyleXfs entry must remap its
///    fontId/fillId/numFmtId child references into the ClosedXML-rebuilt target's own (differently
///    ordered/sized) fonts/fills/numFmts lists, instead of copying the source's stale indices verbatim.
///  - R14-sparklines-3: a sparkline group's theme/indexed color must resolve to the workbook's actual
///    theme color, instead of silently becoming null (which drops the color on save and renders with
///    FreeX's hardcoded default instead of the file's real accent color).
/// </summary>
public sealed class FreeXR14T5Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_RecoveredNamedCellStyle_RemapsFontFillAndNumFmtIndicesIntoRebuiltTarget()
    {
        // Source stylesheet: a custom named style "MyStyle" whose cellStyleXfs entry references
        // fontId=1 (bold red), fillId=2 (solid yellow), and a custom numFmtId=180 ("0.0000").
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml",
            $"""
            <styleSheet xmlns="{WorkbookNs}">
              <numFmts count="1"><numFmt numFmtId="180" formatCode="0.0000"/></numFmts>
              <fonts count="2">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><color rgb="FFFF0000"/><sz val="11"/><name val="Calibri"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/><bgColor indexed="64"/></patternFill></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="2">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
                <xf numFmtId="180" fontId="1" fillId="2" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
              <cellStyles count="2">
                <cellStyle name="Normal" xfId="0" builtinId="0"/>
                <cellStyle name="MyStyle" xfId="1"/>
              </cellStyles>
            </styleSheet>
            """));

        // Target (ClosedXML-rebuilt) stylesheet: only knows the default "Normal" style, with its own
        // differently-ordered/smaller fonts and fills lists -- fontId=1 here is an unrelated plain font,
        // and fillId=2 is entirely out of range (only 2 fills exist), matching what the finding describes.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml",
            $"""
            <styleSheet xmlns="{WorkbookNs}">
              <fonts count="2">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><sz val="11"/><name val="Arial"/></font>
              </fonts>
              <fills count="2">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var myStyle = targetRoot.Element(WorkbookNs + "cellStyles")!
            .Elements(WorkbookNs + "cellStyle")
            .Single(e => e.Attribute("name")!.Value == "MyStyle");
        var newXfId = int.Parse(myStyle.Attribute("xfId")!.Value);

        var targetStyleXfs = targetRoot.Element(WorkbookNs + "cellStyleXfs")!.Elements(WorkbookNs + "xf").ToList();
        var recoveredXf = targetStyleXfs[newXfId];

        var targetFonts = targetRoot.Element(WorkbookNs + "fonts")!.Elements(WorkbookNs + "font").ToList();
        var recoveredFontId = int.Parse(recoveredXf.Attribute("fontId")!.Value);
        recoveredFontId.Should().BeLessThan(targetFonts.Count,
            "the recovered style's fontId must be remapped into the rebuilt target's own font list, not left dangling");
        var recoveredFont = targetFonts[recoveredFontId];
        recoveredFont.Element(WorkbookNs + "b").Should().NotBeNull("the source font was bold");
        recoveredFont.Element(WorkbookNs + "color")!.Attribute("rgb")!.Value.Should().Be("FFFF0000",
            "the recovered style must reference the bold red font, not the target's unrelated font that happens to share the source's raw index");

        var targetFills = targetRoot.Element(WorkbookNs + "fills")!.Elements(WorkbookNs + "fill").ToList();
        var recoveredFillId = int.Parse(recoveredXf.Attribute("fillId")!.Value);
        recoveredFillId.Should().BeLessThan(targetFills.Count,
            "the recovered style's fillId must be remapped into the rebuilt target's own (smaller) fill list, not reference an out-of-range slot that would corrupt the file");
        targetFills[recoveredFillId].Descendants(WorkbookNs + "fgColor").Single().Attribute("rgb")!.Value
            .Should().Be("FFFFFF00", "the recovered style must reference the solid yellow fill");

        var recoveredNumFmtId = int.Parse(recoveredXf.Attribute("numFmtId")!.Value);
        var targetNumFmts = targetRoot.Element(WorkbookNs + "numFmts")?.Elements(WorkbookNs + "numFmt").ToList()
            ?? [];
        var numFmt = targetNumFmts.Should()
            .ContainSingle(e => int.Parse(e.Attribute("numFmtId")!.Value) == recoveredNumFmtId)
            .Subject;
        numFmt.Attribute("formatCode")!.Value.Should().Be("0.0000",
            "the custom number format must be carried into the target's own numFmts list under a valid, non-colliding id");
    }

    private static void Preserve(MemoryStream sourcePackage, MemoryStream targetPackage)
    {
        sourcePackage.Position = 0;
        targetPackage.Position = 0;
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, targetArchive);
        }
    }

    private static XDocument LoadStylesheet(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        return XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
    }

    [Fact]
    public void XlsxAdapter_Load_ResolvesSparklineThemeColorToWorkbookAccentColor()
    {
        // Excel's Insert Sparklines dialog writes theme-based colors by default, e.g.
        // <x14:colorSeries theme="4" tint="-0.4999"/> (theme index 4 = Accent1).
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, worksheetXml =>
        {
            worksheetXml.Root!.Elements(WorkbookNs + "extLst").Remove();
            XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";
            var sparkline = new XElement(
                x14Ns + "sparkline",
                new XElement(xmNs + "f", "Sheet1!A1:A1"),
                new XElement(xmNs + "sqref", "B1"));
            var sparklineGroup = new XElement(
                x14Ns + "sparklineGroup",
                new XAttribute("type", "line"),
                new XElement(x14Ns + "colorSeries", new XAttribute("theme", "4"), new XAttribute("tint", "-0.4999")),
                new XElement(x14Ns + "sparklines", sparkline));
            var extLst = new XElement(
                WorkbookNs + "extLst",
                new XElement(
                    WorkbookNs + "ext",
                    new XAttribute("uri", "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}"),
                    new XElement(
                        x14Ns + "sparklineGroups",
                        new XAttribute(XNamespace.Xmlns + "x14", x14Ns),
                        new XAttribute(XNamespace.Xmlns + "xm", xmNs),
                        sparklineGroup)));
            worksheetXml.Root!.Add(extLst);
        });

        var loaded = new XlsxFileAdapter().Load(package);

        var sparkline = loaded.GetSheetAt(0).Sparklines.Should().ContainSingle().Subject;
        var expected = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, -0.4999);
        sparkline.SeriesColor.Should().Be(expected,
            "a theme+tint sparkline color must resolve to the workbook's actual accent color instead of being silently dropped to null");
    }
}
