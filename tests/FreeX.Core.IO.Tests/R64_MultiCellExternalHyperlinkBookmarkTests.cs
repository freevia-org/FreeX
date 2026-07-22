using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 64 regression test for src/FreeX.Core.IO/XlsxFileAdapter.cs:
///  - R64-io-hyperlink-6-2: an external hyperlink whose "ref" spans MULTIPLE cells (e.g.
///    ref="A1:B1") must recover its "location" sub-address (bookmark) for EVERY cell the ref
///    covers, not just a single-cell ref. ReadWorksheetExternalHyperlinkLocations previously keyed
///    its lookup dictionary by the raw, verbatim "ref" attribute ("A1:B1"), but the per-cell
///    recovery site looks up each individual cell's A1 address ("A1", "B1"), so the range key
///    never matched and the bookmark was silently dropped for multi-cell refs.
/// </summary>
public sealed class R64_MultiCellExternalHyperlinkBookmarkTests
{
    [Fact]
    public void Load_ExternalHyperlinkWithMultiCellRefAndLocation_RecoversBookmarkForEveryCellInRange()
    {
        var sourceBytes = CreateExternalHyperlinkSourcePackage(reference: "A1:B1", location: "Sheet2!C1");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var addressA1 = new CellAddress(sheet.Id, 1, 1);
        var addressB1 = new CellAddress(sheet.Id, 1, 2);

        sheet.Hyperlinks[addressA1].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[addressA1].Bookmark.Should().Be(
            "Sheet2!C1",
            "the first cell of a multi-cell external hyperlink ref must recover the shared bookmark");

        sheet.Hyperlinks[addressB1].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[addressB1].Bookmark.Should().Be(
            "Sheet2!C1",
            "every cell covered by a multi-cell external hyperlink ref must recover the same shared bookmark, not just the anchor cell");
    }

    [Fact]
    public void Load_ExternalHyperlinkWithSingleCellRefAndLocation_StillRecoversBookmark()
    {
        // Sibling no-regression case: the pre-existing single-cell ref path must still work after
        // the range-expansion fix.
        var sourceBytes = CreateExternalHyperlinkSourcePackage(reference: "A1", location: "Sheet2!C1");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var addressA1 = new CellAddress(sheet.Id, 1, 1);

        sheet.Hyperlinks[addressA1].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[addressA1].Bookmark.Should().Be("Sheet2!C1");
    }

    private static byte[] CreateExternalHyperlinkSourcePackage(string reference, string location)
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
                </Relationships>
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
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:B1"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c><c r="B1" t="inlineStr"><is><t>Jump</t></is></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="{reference}" r:id="rIdExt" location="{location}"/>
                  </hyperlinks>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExt" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/data.xlsx" TargetMode="External"/>
                </Relationships>
                """));

        return package.ToArray();
    }
}
