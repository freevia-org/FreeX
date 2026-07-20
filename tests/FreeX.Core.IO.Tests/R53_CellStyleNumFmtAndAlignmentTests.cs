using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R53-io-cellstyle-named-3-1/3-2: <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// ReconnectCellXfNamedStyleLinks correlates a rebuilt cellXfs &lt;xf&gt; back to a recovered named
/// cell style by a "renders the same style" signature (font/fill/border/numFmt). Two gaps in that
/// signature caused named-style bindings to be silently dropped on save even though the rebuild
/// legitimately reproduced the correct visual style:
/// <list type="bullet">
/// <item>3-1: builtin numFmtIds (&lt;164) were signed by their raw id, while custom numFmtIds
/// (&gt;=164) were signed by their resolved format-code text, so a source custom numFmtId and
/// FreeX's own re-canonicalized builtin numFmtId for the IDENTICAL code (e.g. "0%") never compared
/// equal.</item>
/// <item>3-2: alignment/protection were omitted from the signature entirely, so two named styles
/// differing only by those collapsed onto one signature and were flagged as an unresolvable
/// cross-style collision, even though the rebuild produced two distinguishable target xfs.</item>
/// </list>
/// </summary>
public sealed class R53_CellStyleNumFmtAndAlignmentTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_ReconnectsNamedStyle_WhenSourceCustomNumFmtMatchesTargetCanonicalizedBuiltin()
    {
        // Source: a custom numFmtId=164 ("0%") backs both the "PercentReport" named style
        // (cellStyleXfs[1]) and the cell bound to it (cellXfs[1], xfId="1").
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithCustomNumFmt()));

        // Target: a full rebuild that re-canonicalized "0%" to Excel's BUILTIN numFmtId=9 (FreeX's
        // own save path does this via BuiltInNumberFormatCatalog whenever a format string matches a
        // builtin catalog entry) -- same font/fill/border, but numFmtId=9 instead of 164.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithBuiltinNumFmt()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var percentStyle = targetRoot.Element(WorkbookNs + "cellStyles")!
            .Elements(WorkbookNs + "cellStyle")
            .Single(style => style.Attribute("name")!.Value == "PercentReport");
        var recoveredXfId = percentStyle.Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Be(recoveredXfId,
            "the source's custom numFmtId=164 (\"0%\") and the rebuilt target's canonicalized builtin " +
            "numFmtId=9 render the IDENTICAL format, so the cell's rebuilt xf must still be reconnected " +
            "to the recovered 'PercentReport' named style instead of being left at xfId=\"0\"");
    }

    [Fact]
    public void Preserve_SiblingRegression_PlainBuiltinNumFmtStillReconnects()
    {
        // Sibling/no-regression: the pre-existing common case (both source and target use the SAME
        // builtin numFmtId, e.g. General) must keep reconnecting exactly as before this fix.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithBuiltinGeneral()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithBuiltinGeneral()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var goodStyle = targetRoot.Element(WorkbookNs + "cellStyles")!
            .Elements(WorkbookNs + "cellStyle")
            .Single(style => style.Attribute("name")!.Value == "Good");
        var recoveredXfId = goodStyle.Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Be(recoveredXfId,
            "two builtin-numFmtId xfs that render identically must still reconnect as before this fix");
    }

    [Fact]
    public void Preserve_ReconnectsBothNamedStyles_WhenTheyDifferOnlyByAlignment()
    {
        // Source: "ReportHeaderLeft" (left-aligned) and "ReportHeaderCenter" (centered) share the
        // same font/fill/border/numFmt -- they differ ONLY in <alignment horizontal="...">.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithAlignmentOnlyDifference()));

        // Target: a full rebuild that legitimately produced two DISTINCT rebuilt cellXfs (their
        // <alignment> children differ), one per cell/style.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithDistinctAlignmentXfs()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var leftStyleXfId = targetRoot.Element(WorkbookNs + "cellStyles")!.Elements(WorkbookNs + "cellStyle")
            .Single(style => style.Attribute("name")!.Value == "ReportHeaderLeft").Attribute("xfId")!.Value;
        var centerStyleXfId = targetRoot.Element(WorkbookNs + "cellStyles")!.Elements(WorkbookNs + "cellStyle")
            .Single(style => style.Attribute("name")!.Value == "ReportHeaderCenter").Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(3);
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Be(leftStyleXfId,
            "the two named styles differ only by alignment, which the signature must now include, so " +
            "each cell's rebuilt xf must reconnect to its own recovered named style");
        targetCellXfs[2].Attribute("xfId")?.Value.Should().Be(centerStyleXfId,
            "the two named styles differ only by alignment, which the signature must now include, so " +
            "each cell's rebuilt xf must reconnect to its own recovered named style");
    }

    [Fact]
    public void Preserve_SiblingRegression_IdenticalStylesWithoutAlignmentStillDetectedAsCollision()
    {
        // Sibling/no-regression: two named styles that render TRULY identically (no alignment
        // difference either) must still be treated as an unresolvable cross-style collision, exactly
        // as the pre-existing ambiguity guard requires -- adding alignment/protection to the
        // signature must not defeat that guard when there genuinely is no distinguishing attribute.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetWithTrueCollision()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetWithSharedXfNoAlignment()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")?.Value.Should().Match(value => value == null || value == "0",
            "two named styles that render byte-identically (including alignment) must remain an " +
            "unresolvable collision, not be arbitrarily reconnected to one of them");
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

    private static string SourceStyleSheetWithCustomNumFmt() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="1"><numFmt numFmtId="164" formatCode="0%"/></numFmts>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF000000"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFE0B2"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="164" fontId="1" fillId="1" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="164" fontId="1" fillId="1" borderId="0" xfId="1"/>
          </cellXfs>
          <cellStyles count="2">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="PercentReport" xfId="1"/>
          </cellStyles>
        </styleSheet>
        """;

    private static string TargetStyleSheetWithBuiltinNumFmt() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><color rgb="FF000000"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFE0B2"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="9" fontId="1" fillId="1" borderId="0" xfId="0"/>
          </cellXfs>
          <cellStyles count="1">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
          </cellStyles>
        </styleSheet>
        """;

    private static string SourceStyleSheetWithBuiltinGeneral() =>
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

    private static string TargetStyleSheetWithBuiltinGeneral() =>
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

    // cellStyleXfs[1]="ReportHeaderLeft" and cellStyleXfs[2]="ReportHeaderCenter" share the same
    // fontId=1/fillId=1/borderId=0/numFmtId=0 but differ in <alignment horizontal="...">.
    private static string SourceStyleSheetWithAlignmentOnlyDifference() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFDDDDDD"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" applyAlignment="1"><alignment horizontal="left"/></xf>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" applyAlignment="1"><alignment horizontal="center"/></xf>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="1" applyAlignment="1"><alignment horizontal="left"/></xf>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="2" applyAlignment="1"><alignment horizontal="center"/></xf>
          </cellXfs>
          <cellStyles count="3">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
            <cellStyle name="ReportHeaderLeft" xfId="1"/>
            <cellStyle name="ReportHeaderCenter" xfId="2"/>
          </cellStyles>
        </styleSheet>
        """;

    // Target: a legitimate full rebuild that kept the two rebuilt cellXfs DISTINCT (their <alignment>
    // children differ), one per cell/style -- exactly what FreeX's own alignment read/write produces.
    private static string TargetStyleSheetWithDistinctAlignmentXfs() =>
        """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="0"/>
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFDDDDDD"/></patternFill></fill>
          </fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="left"/></xf>
            <xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center"/></xf>
          </cellXfs>
          <cellStyles count="1">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
          </cellStyles>
        </styleSheet>
        """;

    // Two named styles ("CollisionA"/"CollisionB") that render TRULY identically -- same
    // font/fill/border/numFmt AND no alignment/protection difference either.
    private static string SourceStyleSheetWithTrueCollision() =>
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
            <cellStyle name="CollisionA" xfId="1"/>
            <cellStyle name="CollisionB" xfId="2"/>
          </cellStyles>
        </styleSheet>
        """;

    private static string TargetStyleSheetWithSharedXfNoAlignment() =>
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
}
