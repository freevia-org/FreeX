using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R49-io-cell-metadata-richdata-3-2: a row/column insert or delete performed since the source
/// snapshot was captured shifts an affected rich-value cell to a new address in the freshly
/// regenerated (full-rewrite) target sheet. Before the fix,
/// <c>XlsxWorksheetMetadataPreserver.MergeWorksheetCellNativeMetadata</c> matched source-to-target
/// cells purely by their unchanged source address (<c>targetCellsByAddress.TryGetValue(address,
/// ...)</c>), so a shifted cell's vm/cm rich-value metadata binding (and other native-only
/// metadata) was silently dropped instead of following the cell to its new address.
///
/// This test simulates the shift directly at the model level (remove the cell from its old
/// address, re-add the identical value at the new address) -- the same technique already used by
/// <c>XlsxLegacyCommentVmlBugsTests</c> for the analogous comment-shift finding -- rather than
/// invoking a real Core.Commands row-insert command, keeping this an IO-layer-only test.
///
/// The source package includes a real xl/metadata.xml part with a &lt;valueMetadata count="4"&gt;
/// backing vm="3" -- without it, XlsxWorksheetGridXmlNormalizer's grid-canonicalization pass
/// correctly treats vm="3" as an orphaned/out-of-range index and strips it on ANY save regardless
/// of any shift (a genuinely separate, already-correct safety net for malformed source files; see
/// XlsxFormulaCachedValueRichValueMetadataPatchSaveTests for the same documented requirement).
/// </summary>
public sealed class R49_CellMetadataAddressShiftTests
{
    private static byte[] CreateSourcePackage(string worksheetXml)
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
                    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                  </sheets>
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
                // A minimal but structurally valid rich-value metadata part: <valueMetadata count="4">
                // backs vm="3" below (vm indexes are 1-based into this list).
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
                  <valueMetadata count="4">
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
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            ("xl/worksheets/sheet1.xml", worksheetXml));

        return package.ToArray();
    }

    private static Dictionary<string, XElement> LoadSavedCellsByAddress(byte[] savedBytes)
    {
        using var stream = new MemoryStream(savedBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;

        return document.Root!
            .Descendants(ns + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToDictionary(cell => cell.Attribute("r")!.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullSave_RowInsertShiftsRichValueCell_VmMetadataFollowsToNewAddress()
    {
        // Source: a single rich-value cell at B5 (vm="3", v=42, no other cell at any other
        // address, so the target's B6 -- the shift recipient -- is a genuinely "new" address that
        // never existed in the source snapshot).
        var sourceBytes = CreateSourcePackage("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="B5"/>
              <sheetData>
                <row r="5"><c r="B5" vm="3"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // Act: simulate "insert one row above row 5" -- the rich-value cell moves from B5 to B6
        // with its value unchanged.
        var oldAddress = new CellAddress(sheet.Id, 5, 2); // B5
        var newAddress = new CellAddress(sheet.Id, 6, 2); // B6
        sheet.ClearCell(oldAddress);
        sheet.SetCell(newAddress, new NumberValue(42));

        // Force a full (non-patch) rewrite, exercising XlsxWorksheetMetadataPreserver's
        // native-metadata merge rather than the byte-patch path.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-richvalue-shift-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        var savedCells = LoadSavedCellsByAddress(savedBytes);

        savedCells.Should().ContainKey("B6", "the rich-value cell must be saved at its new, shifted address");
        var b6VmValue = savedCells["B6"].Attribute("vm")?.Value;
        b6VmValue.Should().Be("3",
            "the vm rich-value metadata binding must follow the cell through the row-insert " +
            "address shift instead of being silently dropped (R49-io-cell-metadata-richdata-3-2)");

        savedCells.Should().NotContainKey("B5", "the old address must no longer have any cell");
    }

    [Fact]
    public void FullSave_NoAddressShift_VmMetadataStillReattachesAtSameAddress_NoRegression()
    {
        // Sibling no-regression case: when the cell's address does NOT shift (the ordinary,
        // already-covered case), the direct-address match path must keep working exactly as
        // before the shift-aware fallback was added.
        var sourceBytes = CreateSourcePackage("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="B5"/>
              <sheetData>
                <row r="5"><c r="B5" vm="3"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // No shift this time -- just force a full rewrite via an unrelated new cell.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-richvalue-noshift-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        var savedCells = LoadSavedCellsByAddress(savedBytes);

        savedCells.Should().ContainKey("B5");
        var b5VmValue = savedCells["B5"].Attribute("vm")?.Value;
        b5VmValue.Should().Be("3",
            "an unshifted rich-value cell must still reattach its vm metadata via the direct " +
            "address match (no regression)");
    }
}
