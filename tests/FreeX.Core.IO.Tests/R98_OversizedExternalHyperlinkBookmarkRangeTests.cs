using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 98 regression test for src/FreeX.Core.IO/XlsxFileAdapter.cs:
///  - ReadWorksheetExternalHyperlinkLocations (invoked directly against the raw, un-sanitized
///    packageArchive before the ClosedXML-input copy is normalized) calls
///    ExpandHyperlinkReferenceToCellKeys for every &lt;hyperlink&gt; element that carries both an
///    r:id and a "location" attribute. That expansion used to be a plain nested for-loop over
///    minRow..maxRow / minCol..maxCol with NO cap. A bounded (non whole-column/row) ref such as
///    "A1:XFD1048576" parses successfully via CellAddress.TryParse (MaxRow=1,048,576,
///    MaxCol=16,384), so the loop would attempt ~17.2 billion iterations -- an OOM/hang triggered
///    by a crafted worksheet XML of only a few hundred bytes. The existing zip-bomb guard
///    (WorkbookOpenSizeGuard) only checks declared zip entry sizes/compression ratio, not
///    worksheet XML semantic content, so it does not catch this.
///  - The fix caps expansion at the same 100,000-cell bound already used by
///    XlsxWorksheetHyperlinkNormalizer.MaxBoundedHyperlinkRangeCellCount for the sibling
///    ClosedXML-input sanitization path: ranges at or under the cap still expand normally: ranges
///    over the cap are skipped (no cells recovered) instead of materialized.
/// </summary>
public sealed class R98_OversizedExternalHyperlinkBookmarkRangeTests
{
    [Fact]
    public async Task Load_ExternalHyperlinkWithWholeSheetBoundedRangeRef_DoesNotHangOrThrow()
    {
        // A1:XFD1048576 is the entire worksheet extent (16,384 cols x 1,048,576 rows), a fully
        // bounded, legally-parseable range. Before the fix this drove ~17.2 billion loop
        // iterations in ExpandHyperlinkReferenceToCellKeys. Bound the test itself with a
        // generous-but-finite timeout so a regression fails fast instead of hanging the run.
        var sourceBytes = CreateExternalHyperlinkSourcePackage(reference: "A1:XFD1048576", location: "Sheet2!C1");
        var adapter = new XlsxFileAdapter();

        var task = Task.Run(() =>
        {
            using var source = new MemoryStream(sourceBytes, writable: false);
            return adapter.Load(source);
        });

        // Generous on purpose. The regression this guards against is effectively unbounded (~17.2
        // billion iterations), so it never completes and any finite bound catches it. Twenty seconds
        // was enough for this file alone but not in a loaded full-suite run, where a healthy load lost
        // the race and the guard reported a hang that was not one.
        var completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMinutes(3)));

        completedTask.Should().BeSameAs(
            task,
            "loading a file with a whole-sheet-extent bounded hyperlink ref must not hang " +
            "(unbounded cell-by-cell expansion previously drove ~17.2 billion loop iterations)");
        var workbook = await task;
        workbook.Should().NotBeNull();
    }

    [Fact]
    public void Load_ExternalHyperlinkWithModeratelyLargeInCapRange_StillRecoversBookmarkForBoundaryCell()
    {
        // Sibling no-regression case: a bounded range comfortably larger than R64's 2-cell case,
        // but still well under the 100,000-cell cap, must keep expanding and recovering the
        // shared bookmark for every cell it covers, including the last one -- the fix's cap check
        // must not affect ranges that were never at risk.
        var sourceBytes = CreateExternalHyperlinkSourcePackage(reference: "A1:A1000", location: "Sheet2!C1");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var firstCell = new CellAddress(sheet.Id, 1, 1);
        var lastCell = new CellAddress(sheet.Id, 1000, 1);

        sheet.HyperlinkMetadata[firstCell].Bookmark.Should().Be("Sheet2!C1");
        sheet.HyperlinkMetadata[lastCell].Bookmark.Should().Be(
            "Sheet2!C1",
            "a bounded range comfortably under the 100,000-cell cap must still fully expand");
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
