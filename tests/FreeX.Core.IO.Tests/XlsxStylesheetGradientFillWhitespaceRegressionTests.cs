using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R25-io-styles-deep-1: <see cref="XlsxStylesheetMetadataPreserver"/>
/// crashed with an InvalidCastException while restoring a gradient fill whenever the source
/// styles.xml's &lt;fill&gt;&lt;gradientFill&gt; entry carried insignificant whitespace between
/// the tags (e.g. from any pretty-printing tool/editor, or a plain XDocument.Save with default
/// formatting). MergeStylesheetGradientFills replaced the target &lt;fill&gt;'s children via
/// `sourceFill.Nodes().Select(n => (XElement)n)`, and `.Nodes()` includes the whitespace XText
/// node sitting between the tags, which cannot be cast to XElement.
/// </summary>
public sealed class XlsxStylesheetGradientFillWhitespaceRegressionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_DoesNotThrow_WhenSourceGradientFillHasInsignificantWhitespace()
    {
        // Source fill[1] wraps <gradientFill> with indentation/newlines, exactly as a
        // pretty-printing tool (or a bare XDocument.Save with default formatting) would emit.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """
            <fills count="2">
              <fill><patternFill patternType="none"/></fill>
              <fill>
                <gradientFill degree="90">
                  <stop position="0">
                    <color rgb="FFFF0000"/>
                  </stop>
                  <stop position="1">
                    <color rgb="FF0000FF"/>
                  </stop>
                </gradientFill>
              </fill>
            </fills>
            """,
            """
            <cellXfs count="2">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/>
            </cellXfs>
            """)));

        // Rebuilt target: same signature xf but still pointing at the plain (no-fill) fill —
        // simulates ClosedXML's full-rebuild path, which knows nothing about gradients.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """
            <fills count="1">
              <fill><patternFill patternType="none"/></fill>
            </fills>
            """,
            """
            <cellXfs count="2">
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
              <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            </cellXfs>
            """)));

        var act = () => Preserve(sourcePackage, targetPackage);

        act.Should().NotThrow("insignificant whitespace between <fill> and <gradientFill> must not crash the merge");

        var targetFills = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "fills")!
            .Elements(WorkbookNs + "fill")
            .ToList();
        var restoredFill = targetFills[targetFills.Count - 1];
        var gradient = restoredFill.Element(WorkbookNs + "gradientFill");
        gradient.Should().NotBeNull("the gradient must actually be restored onto the matched target fill");
        gradient!.Attribute("degree")!.Value.Should().Be("90");
        gradient.Elements(WorkbookNs + "stop").Should().HaveCount(2,
            "both gradient stops must survive the merge, not just the element shell");
    }

    [Fact]
    public void Preserve_StillMergesGradient_WhenSourceHasNoInsignificantWhitespace()
    {
        // Sibling/opposite case: a compact (non-indented) source, i.e. the shape that already
        // worked before the fix. The fix must not regress this already-working case.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """<fills count="2"><fill><patternFill patternType="none"/></fill><fill><gradientFill degree="45"><stop position="0"><color rgb="FF00FF00"/></stop><stop position="1"><color rgb="FFFFFFFF"/></stop></gradientFill></fill></fills>""",
            """<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="0" fillId="1" borderId="0" xfId="0" applyFill="1"/></cellXfs>""")));

        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            """<fills count="1"><fill><patternFill patternType="none"/></fill></fills>""",
            """<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>""")));

        Preserve(sourcePackage, targetPackage);

        var targetFills = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "fills")!
            .Elements(WorkbookNs + "fill")
            .ToList();
        var restoredFill = targetFills[targetFills.Count - 1];
        var gradient = restoredFill.Element(WorkbookNs + "gradientFill");
        gradient.Should().NotBeNull("a compact source gradient must still merge correctly after the fix");
        gradient!.Attribute("degree")!.Value.Should().Be("45");
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
