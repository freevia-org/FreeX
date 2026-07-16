using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R43-io-richvalue-linkeddata-3-1: the fast byte-patch save path leaves a
/// stale vm/cm rich-value metadata pointer attached to a formula cell whose cached value is being
/// rewritten. A rich value (linked data type such as Stocks/Geography, or an IMAGE()-produced value)
/// propagated onto a formula cell is represented by a vm/cm attribute indexing into
/// xl/metadata.xml's valueMetadata/cellMetadata, describing exactly the cell's <c>/<v> content the
/// metadata was captured against. When the formula's cached value (or the formula text itself)
/// subsequently changes and gets byte-patched in place via
/// RewriteFormulaCachedValue/RewriteFormulaCachedCellValue/RewriteFormulaTextAndCachedCellValue, the
/// old vm/cm must be dropped -- otherwise the saved cell keeps pointing real Excel at rich-value
/// metadata describing its old, now-unrelated value (silent metadata-to-value corruption; the cell
/// keeps rendering as a linked-data-type entity card bound to stale data instead of its new plain
/// value). This mirrors the existing guard in RewriteLiteralCellValue (drops vm/cm when a rich-value
/// placeholder cell is overwritten with a literal) and the full-rewrite guard in
/// XlsxWorksheetMetadataPreserver.CellMetadata.cs (CellValueMatchesCapturedNativeMetadata).
/// </summary>
public sealed class XlsxFormulaCachedValueRichValueMetadataPatchSaveTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook) =>
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

    // A1 is a formula cell carrying vm="7" (a rich value propagated onto the formula's cached
    // #VALUE! placeholder, mirroring Excel's own representation for a linked-data-type formula
    // result) alongside a plain literal sibling cell B1 that carries no rich-value metadata at all.
    private static byte[] CreateSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/metadata.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata" Target="metadata.xml"/>
                </Relationships>
                """),
            (
                // A minimal but structurally valid rich-value metadata part: <valueMetadata count="7">
                // backs vm="7" on A1 below (vm indexes are 1-based into this list). Without this part
                // (or with too few <bk> entries), XlsxWorksheetGridXmlNormalizer's grid-canonicalization
                // pass correctly treats vm="7" as an orphaned/out-of-range index and strips it on ANY
                // patch-save regardless of which cell changed -- a genuinely separate, already-correct
                // safety net for malformed source files. Real Excel never emits vm/cm without a matching
                // xl/metadata.xml, so this part must be present for these tests to exercise the actual
                // RewriteFormulaCachedValue vm/cm staleness bug in isolation.
                "xl/metadata.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <metadata xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <metadataTypes count="1">
                    <metadataType name="XLRICHVALUE" minSupportedVersion="120000" copy="1" pasteAll="1" pasteValues="1" merge="1" splitFirst="1" rowColShift="1" clearFormats="1" clearComments="1" assign="1" coerce="1" cellMeta="1"/>
                  </metadataTypes>
                  <futureMetadata name="XLRICHVALUE" count="1">
                    <bk/>
                  </futureMetadata>
                  <valueMetadata count="7">
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                  </valueMetadata>
                </metadata>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:B1"/>
                  <sheetData>
                    <row r="1">
                      <c r="A1" vm="7"><f>1+1</f><v>2</v></c>
                      <c r="B1"><v>5</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    [Fact]
    public void Save_PatchesFormulaCachedValueOnRichValueCell_DropsStaleVmAttribute()
    {
        var sourceBytes = CreateSourcePackage();
        ReadCellAttribute(sourceBytes, "A1", "vm").Should().Be("7");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        // The formula's cached value changes (2 -> 99.5, e.g. from an edit elsewhere that forces a
        // recalculation), the exact scenario RewriteFormulaCachedValue byte-patches in place.
        cell.Value = new NumberValue(99.5);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Must still be the cheap fast patch-save path -- the bug only reproduces there, not on a
        // full rewrite (which already goes through XlsxWorksheetMetadataPreserver's value-gated
        // native-metadata merge).
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        ReadCellText(savedBytes, "A1").Should().Be("99.5");
        ReadCellAttribute(savedBytes, "A1", "vm").Should().BeNull(
            "the formula cell's cached value changed, so the vm rich-value metadata pointer captured " +
            "against the OLD value is now stale and must be dropped rather than left pointing real Excel " +
            "at mismatched rich-value data");
    }

    [Fact]
    public void Save_PatchesUnrelatedSiblingCell_LeavesUntouchedRichValueFormulaCellVmIntact()
    {
        // Sibling/no-regression case: patching a completely different cell must not disturb a
        // rich-value formula cell that was never touched -- the fix must be scoped to cells whose
        // cached value is actually being rewritten, not a blanket strip of every vm attribute in the
        // worksheet.
        var sourceBytes = CreateSourcePackage();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42)); // B1, unrelated literal cell

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        ReadCellText(savedBytes, "B1").Should().Be("42");
        ReadCellAttribute(savedBytes, "A1", "vm").Should().Be(
            "7",
            "A1's cached value never changed, so its vm rich-value metadata pointer is still valid and " +
            "must be preserved");
        ReadCellText(savedBytes, "A1").Should().Be("2");
    }

    private static string? ReadCellText(byte[] packageBytes, string reference)
    {
        var cell = ReadCellElement(packageBytes, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellAttribute(byte[] packageBytes, string reference, string attributeName) =>
        ReadCellElement(packageBytes, reference).Attribute(attributeName)?.Value;

    private static XElement ReadCellElement(byte[] packageBytes, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));
        cell.Should().NotBeNull();
        return cell!;
    }
}
