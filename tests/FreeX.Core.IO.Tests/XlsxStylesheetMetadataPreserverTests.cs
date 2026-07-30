using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxStylesheetMetadataPreserverTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // A rebuilt package reorders the dxf list relative to the source (ClosedXML's dxfs plus FreeX's
    // appended advanced-conditional-format dxfs land in a different order than the original). Merging the
    // source dxfs into the rebuilt dxfs by raw index therefore lands native font/fill content on an
    // unrelated dxf. The preserver must only merge when the two dxfs render the same visible style.
    [Fact]
    public void Preserve_DoesNotInjectFontColorIntoPositionallyMismatchedDifferentialStyle()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Source dxf[0]: red font on a pink fill, carrying a native attribute so the source counts as
            // preservable stylesheet metadata.
            """
            <dxf nativeDxfAttr="kept">
              <font><color rgb="FFCC0000"/></font>
              <fill><patternFill patternType="solid"><fgColor rgb="FFF4CCCC"/></patternFill></fill>
            </dxf>
            """)));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Rebuilt dxf[0] at the same index is an unrelated rule: green fill, no font.
            """
            <dxf>
              <fill><patternFill patternType="solid"><fgColor rgb="FFD9EAD3"/></patternFill></fill>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.ToString(SaveOptions.DisableFormatting).Should().NotContain("FFCC0000",
            "a positionally mismatched source dxf must not inject its red font into an unrelated rebuilt dxf");
        targetDxf.Descendants(WorkbookNs + "fgColor").Single().Attribute("rgb")!.Value.Should().Be("FFD9EAD3",
            "the rebuilt dxf's own green fill must be preserved unchanged");
    }

    [Fact]
    public void Preserve_MergesNativeMetadataIntoPositionallyMatchedDifferentialStyle()
    {
        XNamespace fx = "urn:freex-test";
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            $"""
            <dxf nativeDxfAttr="kept">
              <font><color rgb="FF112233"/></font>
              <dxfNativeChild xmlns="{fx}" id="dxf-child"/>
            </dxf>
            """)));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Same visible style (font color FF112233) but missing the native attribute and child.
            """
            <dxf>
              <font><color rgb="FF112233"/></font>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.Attribute("nativeDxfAttr")!.Value.Should().Be("kept",
            "native metadata must still be recovered when the source and rebuilt dxf render the same style");
        targetDxf.Descendants(fx + "dxfNativeChild").Should().ContainSingle();
    }

    // R98: a source dxf with an explicit off-toggle (<b val="0"/>, common when a real Excel-authored CF
    // rule explicitly un-bolds matching cells) must still be recognized as rendering the same style as a
    // rebuilt dxf that omits <b> entirely, because none of FreeX's dxf writers ever re-emit an explicit
    // off-toggle (they only emit <b> when Bold==true). Both dxfs render Bold=false identically; only the
    // tri-state "was this explicitly authored" metadata differs (DxfBold=false vs DxfBold=null), and that
    // metadata must not leak into the equality check the merge gates on.
    [Fact]
    public void R98_Preserve_MergesNativeMetadataWhenSourceHasExplicitOffToggleAndRebuiltOmitsIt()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Source dxf[0]: explicit "turn bold off" plus a native attribute FreeX doesn't model.
            """
            <dxf nativeDxfAttr="kept">
              <font><b val="0"/></font>
            </dxf>
            """)));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Rebuilt dxf[0]: same render (not bold), but the off-toggle was never re-emitted, so <b> is
            // simply absent, and the native attribute is missing (FreeX doesn't model it).
            """
            <dxf>
              <font/>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.Attribute("nativeDxfAttr")!.Value.Should().Be("kept",
            "an explicit off-toggle and a never-mentioned toggle render identically, so native metadata " +
            "must still be recovered rather than silently dropped");
    }

    // Sibling/no-regression: a source dxf with an explicit off-toggle must NOT be merged into a rebuilt
    // dxf that renders a genuinely different visible style (here, still bold). The tri-state fix must not
    // widen the equality check beyond "render the same style" -- it should only stop tri-state metadata
    // from causing false negatives, not cause false positives.
    [Fact]
    public void R98_Preserve_DoesNotMergeWhenSourceExplicitOffToggleDiffersFromRebuiltRenderedStyle()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Source dxf[0]: explicit "turn bold off", carrying a native attribute.
            """
            <dxf nativeDxfAttr="kept">
              <font><b val="0"/></font>
            </dxf>
            """)));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", StyleSheet(
            // Rebuilt dxf[0] at the same index renders a genuinely different style: bold ON.
            """
            <dxf>
              <font><b/></font>
            </dxf>
            """)));

        Preserve(sourcePackage, targetPackage);

        var targetDxf = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "dxfs")!
            .Elements(WorkbookNs + "dxf")
            .Single();
        targetDxf.Attribute("nativeDxfAttr").Should().BeNull(
            "a source dxf that explicitly un-bolds must not merge its native metadata into a rebuilt dxf " +
            "that renders bold ON -- the two visibly differ");
        targetDxf.Element(WorkbookNs + "font")!.Element(WorkbookNs + "b").Should().NotBeNull(
            "the rebuilt dxf's own bold-on style must be preserved unchanged");
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

    private static string StyleSheet(string dxfXml) =>
        $"""
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dxfs count="1">{dxfXml}</dxfs>
        </styleSheet>
        """;

}
