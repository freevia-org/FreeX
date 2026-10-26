using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R44-io-cellstyles-named-builtin-3-3: <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// ReconnectCellXfNamedStyleLinks bucketed rebuilt cellXfs &lt;xf&gt; candidates purely by rendered style
/// signature (font/fill/border/numFmt), with no check for the signature being shared between TWO DIFFERENT
/// named styles in the source (e.g. a custom style duplicated under a new name, so both resolve to
/// byte-identical formatting). When ClosedXML's full rebuild collapses every cell bound to either style onto
/// the SAME shared target &lt;xf&gt; (since they render identically), the old dequeue-based reconnect let the
/// first-processed source xf claim that sole shared candidate, silently mislabeling every cell bound to the
/// other named style with the first style's name. The fix adds a cross-style ambiguity guard: if a rendered
/// signature is shared by source xfs bound to more than one distinct recovered named style, the shared
/// target xf cannot be safely attributed to either one, so reconnect is skipped for that signature (both
/// cells fall back to "Normal"/no link, matching the existing "don't guess" behavior for plain-cell
/// collisions instead of actively mislabeling one of them).
/// </summary>
public sealed class XlsxStylesheetCellXfNamedStyleAmbiguityTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_SkipsReconnect_WhenSharedTargetXfRendersLikeTwoDifferentNamedStyles()
    {
        // Source: cellStyleXfs[1]="Report2024" and cellStyleXfs[2]="Report2025" resolve to byte-identical
        // formatting (same fontId=1/fillId=1/borderId=0). Cell A1 (cellXfs[1]) is bound to Report2024
        // (xfId="1"); cell B1 (cellXfs[2]) is bound to Report2025 (xfId="2").
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithCollidingNamedStyles()));

        // Target: a ClosedXML full rebuild that interned ONE shared cellXfs record for both A1 and B1,
        // since they render identically. There is no way to tell, from styles.xml alone, which of the two
        // named styles that shared record should be reconnected to.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithSharedXf()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")!.Value.Should().Match(value => value == null || value == "0",
            "the shared rebuilt xf renders like BOTH 'Report2024' and 'Report2025', so it must not be " +
            "reconnected to either one -- doing so would silently mislabel whichever cell doesn't actually " +
            "own that name");

        // Both named-style definitions must still survive the merge even though neither cell could be
        // safely reconnected to them.
        var targetStyleNames = targetRoot.Element(WorkbookNs + "cellStyles")!
            .Elements(WorkbookNs + "cellStyle")
            .Select(style => style.Attribute("name")!.Value)
            .ToList();
        targetStyleNames.Should().Contain("Report2024").And.Contain("Report2025");
    }

    [Fact]
    public void Preserve_StillReconnectsBoth_WhenTwoNamedStylesRenderDifferently()
    {
        // Sibling/no-regression case: two DIFFERENT named styles that render DIFFERENTLY (distinct
        // signatures) must both still reconnect correctly -- the new cross-style ambiguity guard must
        // only suppress reconnection when the rendered signatures actually collide.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithDistinctNamedStyles()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithDistinctXfs()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var targetCellStyles = targetRoot.Element(WorkbookNs + "cellStyles")!.Elements(WorkbookNs + "cellStyle").ToList();
        var report2024XfId = targetCellStyles.Single(style => style.Attribute("name")!.Value == "Report2024").Attribute("xfId")!.Value;
        var reportBoldXfId = targetCellStyles.Single(style => style.Attribute("name")!.Value == "ReportBold").Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(3);
        targetCellXfs[1].Attribute("xfId")!.Value.Should().Be(report2024XfId,
            "the two named styles render differently, so each cell's rebuilt xf must still be reconnected " +
            "to its own recovered named style as before");
        targetCellXfs[2].Attribute("xfId")!.Value.Should().Be(reportBoldXfId,
            "the two named styles render differently, so each cell's rebuilt xf must still be reconnected " +
            "to its own recovered named style as before");
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

    // cellStyleXfs[1]="Report2024" and cellStyleXfs[2]="Report2025" both reference fontId=1/fillId=1/
    // borderId=0 -- byte-identical rendering, different names. cellXfs[1] (A1) is bound to xfId=1;
    // cellXfs[2] (B1) is bound to xfId=2.
    private static string SourceStyleSheetWithCollidingNamedStyles() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF0000FF"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="1"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="2"/>
          </cellXfs>
          <cellStyles count="3">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="Report2024" xfId="1"/>
            <cellStyle name="Report2025" xfId="2"/>
          </cellStyles>
        </styleSheet>
        """;

    // Target: ClosedXML full rebuild with a single shared cellXfs[1] record standing in for BOTH A1
    // (Report2024) and B1 (Report2025), since they render identically.
    private static string TargetStyleSheetWithSharedXf() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF0000FF"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/></patternFill></fill>
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

    // cellStyleXfs[1]="Report2024" (fontId=1/fillId=1) and cellStyleXfs[2]="ReportBold" (fontId=2/fillId=2)
    // render DIFFERENTLY. cellXfs[1] (A1) bound to xfId=1; cellXfs[2] (B1) bound to xfId=2.
    private static string SourceStyleSheetWithDistinctNamedStyles() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="3">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF0000FF"/><name val="Calibri"/></font>
            <font><b/><color rgb="FFFF0000"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF00FF00"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0"/>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="1"/>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="0" xfId="2"/>
          </cellXfs>
          <cellStyles count="3">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="Report2024" xfId="1"/>
            <cellStyle name="ReportBold" xfId="2"/>
          </cellStyles>
        </styleSheet>
        """;

    // Target: ClosedXML full rebuild with two DISTINCT rebuilt cellXfs records, one per rendered style.
    private static string TargetStyleSheetWithDistinctXfs() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="3">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF0000FF"/><name val="Calibri"/></font>
            <font><b/><color rgb="FFFF0000"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF00FF00"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="0" xfId="0"/>
          </cellXfs>
          <cellStyles count="1">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
          </cellStyles>
        </styleSheet>
        """;
}
