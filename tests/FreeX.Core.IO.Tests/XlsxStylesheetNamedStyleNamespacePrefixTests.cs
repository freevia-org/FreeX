using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R62-io-cell-style-named-6-1 and R62-io-cell-style-named-6-2: both
/// <see cref="XlsxStylesheetMetadataPreserver"/>'s named-style cell reconnect (BuildXfStyleSignature,
/// used by ReconnectCellXfNamedStyleLinks) and its cellStyleXfs font/fill/border child-index remap
/// (RemapIndexedRecordReference) correlated source vs. rebuilt-target XML by comparing raw
/// <c>XElement.ToString(SaveOptions.DisableFormatting)</c> output. That string reproduces whatever
/// namespace prefix was in scope when the element was parsed: a genuine Excel-authored styles.xml uses
/// the default (unprefixed) spreadsheetml namespace, while ClosedXML's own SaveAs() output always uses
/// an explicit "x:" prefix on every element (e.g. &lt;x:styleSheet xmlns:x="..."&gt;&lt;x:fonts&gt;
/// &lt;x:font&gt;...). The existing pinned tests for this mechanism only ever exercised a target fixture
/// using the SAME (unprefixed) namespace form as the source, so they never caught that a realistic
/// x:-prefixed ClosedXML target can never match any source signature -- the reconnect/dedup silently
/// never fires against a real save. The fix normalizes both sides down to local-name-only XML (no
/// namespace URIs/prefixes) before comparing, so the signatures match regardless of prefix.
/// </summary>
public sealed class XlsxStylesheetNamedStyleNamespacePrefixTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Preserve_ReconnectsCellXfToRecoveredNamedStyle_WhenTargetUsesClosedXmlNamespacePrefix()
    {
        // Source: genuine Excel-authored styles.xml, default (unprefixed) namespace. cellXfs[1] (bound
        // to a real cell) carries xfId="1" -- Excel's "Good" named cell style (green bold font + light
        // green fill), recorded at cellStyleXfs[1]/cellStyles "Good".
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetUnprefixed()));

        // Target: a REALISTIC ClosedXML 0.105.0 full rebuild, which always emits every element under an
        // explicit "x:" prefix. It baked the SAME resolved font/fill directly onto its own cellXfs[1]
        // (ApplyStyle's direct-formatting bake), but stamped xfId="0" and dropped every custom
        // cellStyleXfs/cellStyles entry down to just the default "Normal".
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetXPrefixed()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;

        var targetCellStyles = targetRoot.Element(WorkbookNs + "cellStyles")!.Elements(WorkbookNs + "cellStyle").ToList();
        var goodStyle = targetCellStyles.Single(style => style.Attribute("name")!.Value == "Good");
        var recoveredXfId = goodStyle.Attribute("xfId")!.Value;

        var targetCellXfs = targetRoot.Element(WorkbookNs + "cellXfs")!.Elements(WorkbookNs + "xf").ToList();
        targetCellXfs.Should().HaveCount(2);
        targetCellXfs[1].Attribute("xfId")!.Value.Should().Be(recoveredXfId,
            "the cell's rebuilt xf must be reconnected to the recovered 'Good' named style even though " +
            "the realistic ClosedXML target uses an 'x:' namespace prefix the source doesn't");
    }

    [Fact]
    public void Preserve_SiblingRegression_DefaultCellXfIsNotSpuriouslyBoundToNamedStyle_WhenTargetIsPrefixed()
    {
        // Sibling/opposite case: cellXfs[0] (the plain default cell, unrelated to "Good") must remain
        // at xfId="0" even once the reconnect can actually match across the namespace-prefix boundary.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetUnprefixed()));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetXPrefixed()));

        Preserve(sourcePackage, targetPackage);

        var targetCellXfs = LoadStylesheet(targetPackage).Root!
            .Element(WorkbookNs + "cellXfs")!
            .Elements(WorkbookNs + "xf")
            .ToList();

        targetCellXfs[0].Attribute("xfId")!.Value.Should().Match(value => value == null || value == "0",
            "the unrelated default cell must not be spuriously bound to the recovered named style");
    }

    [Fact]
    public void Preserve_RemapsNamedStyleXfChildIndices_ToExistingEquivalentTargetRecord_WhenTargetIsPrefixed()
    {
        // Source: genuine Excel-authored styles.xml (unprefixed). The recovered "Good" cellStyleXfs
        // entry (index 1) references fontId=1/fillId=1 -- the bold-green font and light-green fill.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", SourceStyleSheetUnprefixed()));

        // Target: a realistic ClosedXML rebuild ("x:"-prefixed) whose font/fill lists ALREADY contain an
        // equivalent record for that same bold-green font / light-green fill at index 1 (ClosedXML baked
        // it directly onto cellXfs[1] via ApplyStyle's direct-formatting bake), in addition to the plain
        // default font/fill at index 0.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/styles.xml", TargetStyleSheetXPrefixed()));

        Preserve(sourcePackage, targetPackage);

        var targetRoot = LoadStylesheet(targetPackage).Root!;
        var targetFonts = targetRoot.Element(WorkbookNs + "fonts")!.Elements(WorkbookNs + "font").ToList();
        var targetFills = targetRoot.Element(WorkbookNs + "fills")!.Elements(WorkbookNs + "fill").ToList();

        // No duplicate font/fill record should have been appended: the equivalent target record already
        // existed (just under a namespace-prefixed serialization), so the remap must reuse index 1
        // rather than append a redundant new record at index 2.
        targetFonts.Should().HaveCount(2,
            "the remap must reuse the existing equivalent target font instead of appending a duplicate " +
            "just because the target's XML happens to be namespace-prefixed");
        targetFills.Should().HaveCount(2,
            "the remap must reuse the existing equivalent target fill instead of appending a duplicate " +
            "just because the target's XML happens to be namespace-prefixed");

        var recoveredCellStyleXf = targetRoot.Element(WorkbookNs + "cellStyleXfs")!
            .Elements(WorkbookNs + "xf")
            .Last();
        recoveredCellStyleXf.Attribute("fontId")!.Value.Should().Be("1",
            "the recovered cellStyleXfs entry's fontId must be remapped to the existing equivalent target font");
        recoveredCellStyleXf.Attribute("fillId")!.Value.Should().Be("1",
            "the recovered cellStyleXfs entry's fillId must be remapped to the existing equivalent target fill");
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

    private static string SourceStyleSheetUnprefixed() =>
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

    // Mirrors the real ClosedXML 0.105.0 SaveAs() output shape: every element under an explicit "x:"
    // prefix bound to the same spreadsheetml namespace URI as the source's default (unprefixed) namespace.
    private static string TargetStyleSheetXPrefixed() =>
        """
        <x:styleSheet xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <x:numFmts count="0"/>
          <x:fonts count="2">
            <x:font><x:sz val="11"/><x:name val="Calibri"/></x:font>
            <x:font><x:b/><x:color rgb="FF006100"/><x:name val="Calibri"/></x:font>
          </x:fonts>
          <x:fills count="2">
            <x:fill><x:patternFill patternType="none"/></x:fill>
            <x:fill><x:patternFill patternType="solid"><x:fgColor rgb="FFC6EFCE"/></x:patternFill></x:fill>
          </x:fills>
          <x:borders count="1"><x:border/></x:borders>
          <x:cellStyleXfs count="1">
            <x:xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </x:cellStyleXfs>
          <x:cellXfs count="2">
            <x:xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <x:xf numFmtId="0" fontId="1" fillId="1" borderId="0" xfId="0"/>
          </x:cellXfs>
          <x:cellStyles count="1">
            <x:cellStyle name="Normal" xfId="0" builtinId="0"/>
          </x:cellStyles>
        </x:styleSheet>
        """;
}
