using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R32-io-styles-numfmt-deep-1: <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// gradient restore correlated a gradient placeholder to a rebuilt target &lt;fill&gt; by RGB alone.
/// When a genuinely-solid (non-gradient) source cell happens to share its exact fill colour with a
/// gradient's first stop, <see cref="FreeX.Core.IO.XlsxClosedXmlCellMapper"/>.ApplyStyle stamps the
/// gradient cell's placeholder as an IDENTICAL solid fill, so ClosedXML's style cache legitimately
/// dedups both into ONE rebuilt &lt;fill&gt; shared by both cellXfs. The old merge would unconditionally
/// overwrite that shared &lt;fill&gt; with the gradient content, silently corrupting the unrelated
/// genuine solid cell (it would render the gradient too). The fix refuses to restore a gradient whose
/// placeholder colour is also a genuine solid fill colour used elsewhere in the source, rather than
/// risk that corruption.
///
/// R75-io-styles-fonts-4-1 changed what colour ApplyStyle actually stamps: it now perturbs the
/// placeholder's low-order bits with a hash of the gradient's FULL content (not just its first stop),
/// so two distinct gradients sharing a first stop no longer collide (see
/// <see cref="XlsxGradientFillRoundTripTests.XlsxAdapter_TwoDistinctGradientsSharingFirstStop_SaveAndReload_BothStayDistinct"/>).
/// These fixtures now use <see cref="XlsxClosedXmlCellMapper.ComputeGradientPlaceholderColor"/> to
/// derive the exact colour a real rebuild would stamp, so the "genuine solid cell coincidentally
/// shares the gradient's stamped placeholder colour" scenario this test guards against stays realistic.
/// </summary>
public sealed class XlsxStylesheetGradientFillSharedRgbRegressionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_DoesNotOverwriteSharedFill_WhenGenuineSolidCellSharesGradientFirstStopColour()
    {
        var gradient = new CellGradientFill
        {
            Degree = 90,
            Stops =
            [
                new CellGradientStop(0, new CellColor(0xFF, 0x00, 0x00)),
                new CellGradientStop(1, new CellColor(0x00, 0x00, 0xFF)),
            ],
        };
        var placeholderHex = ToArgbHex(XlsxClosedXmlCellMapper.ComputeGradientPlaceholderColor(gradient));

        // Source: A1 (xf1) is a genuine solid cell whose colour happens to equal the EXACT placeholder
        // ApplyStyle would stamp for B1's gradient (fillId 2), not just its raw first stop.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            $"""
            <fills count="3">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="{placeholderHex}"/></patternFill></fill>
              <fill><gradientFill degree="90"><stop position="0"><color rgb="FFFF0000"/></stop><stop position="1"><color rgb="FF0000FF"/></stop></gradientFill></fill>
            </fills>
            """,
            """
            <cellXfs count="3">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
              <xf numFmtId="0" fontId="0" fillId="2" borderId="0" xfId="0" applyFill="1"/>
            </cellXfs>
            """)));

        // Rebuilt target: ClosedXML legitimately deduplicated A1's genuine solid fill and B1's
        // identical solid placeholder into the SAME target fillId (1) — exactly what a real
        // full-rebuild save produces when the colours coincide.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            $"""
            <fills count="2">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="{placeholderHex}"/></patternFill></fill>
            </fills>
            """,
            """
            <cellXfs count="3">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
            </cellXfs>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetFills = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "fills")!
            .Elements(WorkbookNs + "fill")
            .ToList();

        targetFills.Should().HaveCount(2, "the merge must not append a spurious extra fill either");
        var sharedFill = targetFills[1];
        sharedFill.Element(WorkbookNs + "gradientFill").Should().BeNull(
            "the shared fill must stay a plain solid fill — overwriting it with the gradient would corrupt A1's genuine solid cell");
        var fgColor = sharedFill.Element(WorkbookNs + "patternFill")!.Element(WorkbookNs + "fgColor");
        fgColor!.Attribute("rgb")!.Value.Should().Be(placeholderHex, "A1's genuine solid colour must survive untouched");
    }

    [Fact]
    public void Preserve_StillMergesGradient_WhenNoGenuineSolidCellSharesItsColour()
    {
        var gradientSpec = new CellGradientFill
        {
            Degree = 90,
            Stops =
            [
                new CellGradientStop(0, new CellColor(0xFF, 0x00, 0x00)),
                new CellGradientStop(1, new CellColor(0x00, 0x00, 0xFF)),
            ],
        };
        var placeholderHex = ToArgbHex(XlsxClosedXmlCellMapper.ComputeGradientPlaceholderColor(gradientSpec));

        // Sibling/opposite case: A1 is a genuine solid GREEN cell (a distinct colour from the
        // gradient's stamped placeholder), so there is no ambiguity — the existing colour-correlated
        // restore must still apply normally.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """
            <fills count="3">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FF00FF00"/></patternFill></fill>
              <fill><gradientFill degree="90"><stop position="0"><color rgb="FFFF0000"/></stop><stop position="1"><color rgb="FF0000FF"/></stop></gradientFill></fill>
            </fills>
            """,
            """
            <cellXfs count="3">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
              <xf numFmtId="0" fontId="0" fillId="2" borderId="0" xfId="0" applyFill="1"/>
            </cellXfs>
            """)));

        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            $"""
            <fills count="3">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FF00FF00"/></patternFill></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="{placeholderHex}"/></patternFill></fill>
            </fills>
            """,
            """
            <cellXfs count="3">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
              <xf numFmtId="0" fontId="0" fillId="2" borderId="0" xfId="0" applyFill="1"/>
            </cellXfs>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetFills = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "fills")!
            .Elements(WorkbookNs + "fill")
            .ToList();

        var greenFill = targetFills[1];
        greenFill.Element(WorkbookNs + "gradientFill").Should().BeNull("A1's unrelated green fill must be untouched");

        var restoredFill = targetFills[2];
        var gradient = restoredFill.Element(WorkbookNs + "gradientFill");
        gradient.Should().NotBeNull("B1's gradient must still be restored since no colour ambiguity exists");
        gradient!.Attribute("degree")!.Value.Should().Be("90");
        gradient.Elements(WorkbookNs + "stop").Should().HaveCount(2);
    }

    // ARGB hex string matching the "rgb" attribute format XLSX solid fills use, e.g. "FFFF0000".
    private static string ToArgbHex(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

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

    private static string StyleSheet(string fillsXml, string cellXfsXml) =>
        $"""
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font/></fonts>
          {fillsXml}
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          {cellXfsXml}
        </styleSheet>
        """;
}
