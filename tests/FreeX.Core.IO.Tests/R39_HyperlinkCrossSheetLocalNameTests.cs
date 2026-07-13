using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 39 regression tests for src/FreeX.Core.IO/XlsxFileAdapter.Hyperlinks.cs:
///  - R39-meta-1: R38's defined-name-fabrication fix over-corrected. ClosedXML's
///    XLHyperlink.InternalAddress *getter* fabricates a sheet-qualified prefix by prepending the
///    hyperlink's OWN containing sheet name to a bang-less raw address, so a fabricated address
///    always reads "OwnSheet!Name". A genuinely sheet-qualified reference to a sheet-scoped LOCAL
///    defined name on a DIFFERENT sheet (e.g. a hyperlink on Sheet1 pointing at
///    "Sheet2!LocalRegion") is legitimate and must keep its sheet qualifier -- only strip the
///    qualifier when the sheet part equals the hyperlink's own sheet (the fabrication case).
/// </summary>
public sealed class R39_HyperlinkCrossSheetLocalNameTests
{
    [Fact]
    public void Load_InternalHyperlinkToOtherSheetLocalDefinedName_PreservesSheetQualifier()
    {
        // Genuine cross-sheet reference: a hyperlink on Sheet1 targets "Sheet2!LocalRegion", a
        // defined name scoped locally to Sheet2. This is not a ClosedXML fabrication (the
        // hyperlink's own sheet is Sheet1, not Sheet2) so the qualifier must survive intact --
        // stripping it would discard which sheet's local name the hyperlink actually targets.
        var sourceBytes = CreateTwoSheetPackageWithHyperlink(
            hyperlinkSheetCell: "A1",
            locationAttr: "Sheet2!LocalRegion",
            tooltip: "Jump to other sheet's local name");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.Hyperlinks[address].Should().Be("Sheet2!LocalRegion");
        sheet.HyperlinkMetadata[address].Bookmark.Should().Be("Sheet2!LocalRegion");
    }

    [Fact]
    public void Load_InternalHyperlinkFabricatedWithOwnSheetPrefix_StripsToBareWorkbookName()
    {
        // Sibling/no-regression case: R38's target scenario. ClosedXML fabricates
        // "Sheet1!MyWorkbookName" for a bang-less workbook-scoped defined name read from a
        // hyperlink whose own containing sheet is Sheet1 -- the sheet part equals the hyperlink's
        // own sheet, so this must still be detected and stripped down to the bare name.
        var sourceBytes = CreateTwoSheetPackageWithHyperlink(
            hyperlinkSheetCell: "A1",
            locationAttr: "MyWorkbookName",
            tooltip: "Jump to workbook name");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.Hyperlinks[address].Should().Be("MyWorkbookName");
        sheet.HyperlinkMetadata[address].Bookmark.Should().Be("MyWorkbookName");
    }

    [Fact]
    public void Load_PlainCellReferenceInternalHyperlink_IsUnchanged()
    {
        // Sibling/no-regression case: an ordinary same-sheet cell-reference internal hyperlink
        // must never be affected by the own-sheet-vs-other-sheet comparison -- it is recognized
        // as a cell/range reference first and returned as-is regardless of sheet part.
        var sourceBytes = CreateTwoSheetPackageWithHyperlink(
            hyperlinkSheetCell: "A1",
            locationAttr: "Sheet1!B5",
            tooltip: "Jump to cell");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.Hyperlinks[address].Should().Be("Sheet1!B5");
    }

    private static byte[] CreateTwoSheetPackageWithHyperlink(string hyperlinkSheetCell, string locationAttr, string tooltip)
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
                  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
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
                    <sheet name="Sheet2" sheetId="2" r:id="rId2"/>
                  </sheets>
                  <definedNames>
                    <definedName name="MyWorkbookName">Sheet1!$C$3</definedName>
                    <definedName name="LocalRegion" localSheetId="1">Sheet2!$B$2</definedName>
                  </definedNames>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
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
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                    <row r="2"><c r="B2"><v>1</v></c></row>
                    <row r="3"><c r="C3"><v>2</v></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="{hyperlinkSheetCell}" location="{locationAttr}" tooltip="{tooltip}" display="Jump display"/>
                  </hyperlinks>
                </worksheet>
                """),
            (
                "xl/worksheets/sheet2.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:B2"/>
                  <sheetData>
                    <row r="2"><c r="B2"><v>3</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }
}
