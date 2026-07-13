using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R39-meta-3: <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// ReconnectCellXfNamedStyleLinks bucketed rebuilt cellXfs &lt;xf&gt; candidates purely by rendered
/// style signature (font/fill/border/numFmt), with no check that the matched target xf is exclusively
/// used by cells that were themselves bound to the named style in the source. If ClosedXML's full
/// rebuild interns a SINGLE shared cellXfs record for both a named-style-bound cell (e.g. the built-in
/// "Good" cell style) and an unrelated plain-formatted cell that happens to render identically, the
/// reconnect would bind that shared record's xfId to the recovered named style — silently pulling the
/// plain cell into the "Good" style gallery membership too. The fix adds a provenance guard: if any
/// source cellXfs record with the same rendered signature is NOT bound to a named style (xfId 0/absent),
/// the shared target xf cannot be safely attributed, so the reconnect is skipped for that signature.
/// </summary>
public sealed class XlsxStylesheetCellXfNamedStyleExclusivityTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_SkipsReconnect_WhenSharedTargetXfAlsoRendersLikeAPlainSourceCell()
    {
        // Source: cellXfs[1] is the real "Good"-bound cell (xfId="1"). cellXfs[2] is an UNRELATED
        // plain cell (xfId="0", i.e. no named-style link) that happens to render identically (same
        // fontId/fillId/borderId/numFmtId) to cellXfs[1].
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithCollidingPlainCell()));

        // Target: a ClosedXML full rebuild that interned ONE shared cellXfs record for both the
        // "Good"-styled cell and the plain cell, since they render identically. There is no way to
        // tell, from styles.xml alone, that this single record is also used by the plain cell.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithSharedXf()));

        Preserve(sourcePackage, targetPackage);

        var targetCellXfs = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "cellXfs")!
            .Elements(WorkbookNs + "xf")
            .ToList();

        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Match(value => value == null || value == "0",
            "the shared rebuilt xf also renders like a plain (non-named-style) source cell, so it must not " +
            "be reconnected to the recovered 'Good' named style — doing so would wrongly enroll the plain " +
            "cell in the style gallery too");
    }

    [Fact]
    public void Preserve_StillReconnects_WhenNoPlainCellRendersLikeTheNamedStyleCell()
    {
        // Sibling/no-regression case: same shape as above, but WITHOUT a colliding plain cell in the
        // source — the reconnect must still fire normally (this is the R38 behavior it must not break).
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithoutCollidingPlainCell()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithSharedXf()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var goodStyle = targetRoot.Element(WorkbookNs + "cellStyles")!
            .Elements(WorkbookNs + "cellStyle")
            .Single(style => style.Attribute("name")!.Value == "Good");
        var recoveredXfId = goodStyle.Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Be(recoveredXfId,
            "with no colliding plain cell in the source, the rebuilt xf must still be reconnected to the " +
            "recovered 'Good' named style as before");
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

    // cellXfs[1] = "Good"-bound cell (xfId="1"); cellXfs[2] = unrelated plain cell (xfId="0") that
    // renders identically to cellXfs[1] (same fontId=1/fillId=1/borderId=0/numFmtId=0).
    private static string SourceStyleSheetWithCollidingPlainCell() =>
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
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="1"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0"/>
          </cellXfs>
          <cellStyles count="2">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="Good" xfId="1" builtinId="26"/>
          </cellStyles>
        </styleSheet>
        """;

    // Same as above, minus the colliding plain cellXfs[2] entry.
    private static string SourceStyleSheetWithoutCollidingPlainCell() =>
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

    // Target: ClosedXML full rebuild with a single shared cellXfs[1] record standing in for BOTH the
    // "Good"-styled cell and the plain cell (since they render identically).
    private static string TargetStyleSheetWithSharedXf() =>
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
