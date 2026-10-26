using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R38-io-cellstyle-xf-dedup-1: a named cell style (e.g. the built-in "Good"
/// cell style) survived a full-rebuild save via <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// MergeStylesheetNamedCellStyles (the cellStyleXfs/cellStyles definitions were re-appended), but every
/// cell that referenced it lost its binding: ClosedXML always emits xfId="0" for every rebuilt cellXfs
/// &lt;xf&gt;, so the recovered style ended up referenced by no cell at all. The fix reconnects each
/// rebuilt cellXfs &lt;xf&gt; that renders identically to a source xf bound to a recovered named style,
/// by correlating on the dereferenced font/fill/border/numFmt content (the same "renders the same
/// style" notion the dxf merge already uses).
/// </summary>
public sealed class XlsxStylesheetCellXfNamedStyleLinkTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_ReconnectsCellXfToRecoveredNamedStyle_AfterFullRebuild()
    {
        // Source: cellXfs[1] (bound to a real cell) carries xfId="1" — Excel's "Good" named cell
        // style (green bold font + light-green fill), recorded at cellStyleXfs[1]/cellStyles "Good".
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheet()));

        // Target: a ClosedXML full rebuild. It baked the SAME resolved font/fill directly onto its own
        // cellXfs[1] (ApplyStyle's direct-formatting bake of the resolved named style), but — since
        // ClosedXML has no per-cell named-style concept — stamped xfId="0" and dropped every custom
        // cellStyleXfs/cellStyles entry down to just the default "Normal".
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheet()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;

        var targetCellStyles = targetRoot.Element(WorkbookNs + "cellStyles")!.Elements(WorkbookNs + "cellStyle").ToList();
        var goodStyle = targetCellStyles.Single(style => style.Attribute("name")!.Value == "Good");
        var recoveredXfId = goodStyle.Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")!.Value.Should().Be(recoveredXfId,
            "the cell's rebuilt xf must be reconnected to the recovered 'Good' named style, not left at ClosedXML's default xfId=\"0\"");
    }

    [Fact]
    public void Preserve_SiblingRegression_DefaultCellXfIsNotSpuriouslyBoundToNamedStyle()
    {
        // Sibling/opposite case: cellXfs[0] (the plain default cell, unrelated to "Good") must remain
        // at xfId="0" — reconnection must only touch xfs that actually correlate to a recovered style.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheet()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheet()));

        Preserve(sourcePackage, targetPackage);

        var targetCellXfs = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "cellXfs")!
            .Elements(WorkbookNs + "xf")
            .ToList();

        targetCellXfs[0].Attribute("xfId")!.Value.Should().Match(value => value == null || value == "0",
            "the unrelated default cell must not be spuriously bound to the recovered named style");
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

    private static string SourceStyleSheet() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF006100"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFC6EFCE"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="1"/>
          </cellXfs>
          <cellStyles count="2">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="Good" xfId="1" builtinId="26"/>
          </cellStyles>
        </styleSheet>
        """;

    private static string TargetStyleSheet() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF006100"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFC6EFCE"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0"/>
          </cellXfs>
          <cellStyles count="1">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
          </cellStyles>
        </styleSheet>
        """;
}
