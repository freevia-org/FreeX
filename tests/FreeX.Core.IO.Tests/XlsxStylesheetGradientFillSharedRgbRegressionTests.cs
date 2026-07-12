using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

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
/// </summary>
public sealed class XlsxStylesheetGradientFillSharedRgbRegressionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_DoesNotOverwriteSharedFill_WhenGenuineSolidCellSharesGradientFirstStopColour()
    {
        // Source: A1 (xf1) is a genuine solid FF0000 cell (fillId 1). B1 (xf2) has a linear gradient
        // whose first stop is also FF0000 (fillId 2).
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """
            <fills count="3">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FFFF0000"/></patternFill></fill>
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
            """
            <fills count="2">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FFFF0000"/></patternFill></fill>
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
        fgColor!.Attribute("rgb")!.Value.Should().Be("FFFF0000", "A1's genuine solid colour must survive untouched");
    }

    [Fact]
    public void Preserve_StillMergesGradient_WhenNoGenuineSolidCellSharesItsColour()
    {
        // Sibling/opposite case: A1 is a genuine solid GREEN cell (a distinct colour from the
        // gradient's first stop), so there is no ambiguity — the existing colour-correlated restore
        // must still apply normally.
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
            """
            <fills count="3">
              <fill><patternFill patternType="none"/></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FF00FF00"/></patternFill></fill>
              <fill><patternFill patternType="solid"><fgColor rgb="FFFF0000"/></patternFill></fill>
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
