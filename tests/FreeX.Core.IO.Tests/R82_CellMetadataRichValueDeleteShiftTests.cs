using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-io-cell-rich-metadata-5-1: a row/column DELETE only shrinks the sheet -- unlike an INSERT, it
/// never frees up a brand-new address -- so deleting a middle row leaves every surviving shifted-up
/// cell's OLD address still valid in the freshly regenerated (full-rewrite) target sheet, just now
/// holding a DIFFERENT cell's content. Before the fix, MergeWorksheetCellNativeMetadata's direct-
/// address match trusted this same-address hit once CellValueMatchesCapturedNativeMetadata's
/// t/formula/&lt;v&gt; equality check passed -- which it always does for a column of rich-value
/// placeholder cells (Stocks/Geography/IMAGE(), all serializing as t="e"/&lt;v&gt;#VALUE!&lt;/v&gt;
/// regardless of which distinct entity their vm points to), silently cross-attaching a deleted
/// sibling's vm onto the wrong shifted-up cell.
///
/// Mirrors R49_CellMetadataAddressShiftTests's model-level shift-simulation technique (rather than
/// invoking a real Core.Commands row-delete command), keeping this an IO-layer-only test.
/// </summary>
public sealed class R82_CellMetadataRichValueDeleteShiftTests
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
                // A minimal but structurally valid rich-value metadata part: <valueMetadata count="14">
                // backs vm="10".."13" below (vm indexes are 1-based into this list).
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
                  <valueMetadata count="14">
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
                    <bk><rc t="1" v="0"/></bk>
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
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            ("xl/worksheets/sheet1.xml", worksheetXml));

        return package.ToArray();
    }

    private static string StocksColumnWorksheetXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="B2:B5"/>
          <sheetData>
            <row r="2"><c r="B2" t="e" vm="10"><v>#VALUE!</v></c></row>
            <row r="3"><c r="B3" t="e" vm="11"><v>#VALUE!</v></c></row>
            <row r="4"><c r="B4" t="e" vm="12"><v>#VALUE!</v></c></row>
            <row r="5"><c r="B5" t="e" vm="13"><v>#VALUE!</v></c></row>
          </sheetData>
        </worksheet>
        """;

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
    public void FullSave_RowDeleteShiftsRichValueSiblings_DoesNotCrossAttachStaleVmToShiftedCells()
    {
        var sourceBytes = CreateSourcePackage(StocksColumnWorksheetXml());

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // Act: simulate "delete row 3" (the MSFT row) directly at the model level -- GOOG (old row 4)
        // and AMZN (old row 5) shift up to rows 3 and 4. At the model level every rich-value
        // placeholder cell is represented uniformly (none of vm/cm is modeled), matching the
        // real-world degenerate-signature scenario this finding describes.
        var b3 = new CellAddress(sheet.Id, 3, 2);
        var b4 = new CellAddress(sheet.Id, 4, 2);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.ClearCell(b3);
        sheet.ClearCell(b4);
        sheet.SetCell(b3, new ErrorValue("#VALUE!")); // GOOG shifts B4 -> B3
        sheet.ClearCell(b5);
        sheet.SetCell(b4, new ErrorValue("#VALUE!")); // AMZN shifts B5 -> B4

        // Force a full (non-patch) rewrite, exercising XlsxWorksheetMetadataPreserver's
        // native-metadata merge rather than the byte-patch path.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-richvalue-delete-shift-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var savedCells = LoadSavedCellsByAddress(savedBytes);

        savedCells.Should().ContainKey("B2");
        savedCells["B2"].Attribute("vm")?.Value.Should().Be(
            "10",
            "AAPL's cell (B2) never shifted, so its own vm binding must survive unchanged");

        // The core regression: B3 now holds GOOG's shifted-up data, not MSFT's, so it must never keep
        // MSFT's stale vm="11" -- that would silently bind the wrong rich-value entity to this cell.
        savedCells.Should().ContainKey("B3");
        savedCells["B3"].Attribute("vm")?.Value.Should().NotBe(
            "11",
            "B3 now holds GOOG's shifted-up data, not MSFT's -- reattaching MSFT's vm='11' here would " +
            "silently cross-bind the wrong rich-value entity (R82-io-cell-rich-metadata-5-1)");

        savedCells.Should().ContainKey("B4");
        savedCells["B4"].Attribute("vm")?.Value.Should().NotBe(
            "12",
            "B4 now holds AMZN's shifted-up data, not GOOG's -- reattaching GOOG's vm='12' here would " +
            "silently cross-bind the wrong rich-value entity (R82-io-cell-rich-metadata-5-1)");
    }

    [Fact]
    public void FullSave_NoDeleteAmbiguousSignatureSiblings_StillReattachAtSameAddress_NoRegression()
    {
        // Sibling no-regression case: several rich-value placeholder cells share the exact same
        // degenerate (t="e"/#VALUE!) signature but NONE of them shifted -- the new ambiguity guard
        // must see source-count == target-count for that signature and let the ordinary
        // direct-address reattachment proceed exactly as it did before this fix.
        var sourceBytes = CreateSourcePackage(StocksColumnWorksheetXml());

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // No shift this time -- just force a full rewrite via an unrelated new cell.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-richvalue-noshift-ambiguous-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var savedCells = LoadSavedCellsByAddress(savedBytes);

        savedCells["B2"].Attribute("vm")?.Value.Should().Be("10");
        savedCells["B3"].Attribute("vm")?.Value.Should().Be("11");
        savedCells["B4"].Attribute("vm")?.Value.Should().Be("12");
        savedCells["B5"].Attribute("vm")?.Value.Should().Be("13");
    }
}
