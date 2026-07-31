using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the whole-column/whole-row (and oversized bounded-range) hyperlink
/// ref-shift gap: such a hyperlink is stripped from the ClosedXML-input copy at load time
/// (XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs) because Sheet.Hyperlinks/
/// HyperlinkMetadata are single-CellAddress-keyed and cannot represent it. Before this fix, the
/// ORIGINAL "ref" string was simply re-emitted verbatim from the pristine pre-edit source-package
/// snapshot on every full save (XlsxWorksheetMetadataPreserver.CellMetadata.cs), so inserting or
/// deleting a real row/column via the actual InsertColumnsCommand/InsertRowsCommand/
/// DeleteColumnsCommand/DeleteRowsCommand -- the ONLY code paths that ever move a hyperlink's
/// address when sheet structure changes -- silently left the ref pointing at the WRONG column/row
/// after a save. Sheet.RangeHyperlinks now tracks a live, shift-adjusted GridRange for each such ref,
/// updated by those very commands, and the save-time merge re-emits the CURRENT ref computed from it.
/// </summary>
public sealed class R106_RangeHyperlinkRowColumnShiftTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string StrippedTargetUrl = "https://example.com/range-hyperlink-target";

    // Mirrors R99_HyperlinkRelationshipRebindTests.CreateSourcePackage: builds a fully valid
    // single-sheet .xlsx via a real adapter save, then swaps in hand-authored worksheet XML (+ its
    // .rels) so the whole-column/row hyperlink can be authored directly rather than materialized
    // cell-by-cell by ClosedXML (which cannot represent it at all).
    private static MemoryStream CreateSourcePackage(string worksheetXml, string worksheetRelsXml)
    {
        var workbook = new Workbook("R106-RangeHyperlinkShift");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var existingEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            existingEntry.Should().NotBeNull("a freshly saved single-sheet workbook must contain xl/worksheets/sheet1.xml");
            existingEntry!.Delete();

            var replacementEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(replacementEntry.Open()))
                writer.Write(worksheetXml);

            var relsEntry = archive.CreateEntry("xl/worksheets/_rels/sheet1.xml.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(worksheetRelsXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static XElement GetSavedHyperlinksElement(MemoryStream saved)
    {
        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull();
        using var worksheetStream = worksheetEntry!.Open();
        var savedWorksheetXml = XDocument.Load(worksheetStream);
        var hyperlinksElement = savedWorksheetXml.Root!.Element(WorkbookNs + "hyperlinks");
        hyperlinksElement.Should().NotBeNull("the reemitted range hyperlink must survive the full save");
        return hyperlinksElement!;
    }

    private const string WholeColumnSourceWorksheetXml =
        """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData>
            <row r="1"><c r="A1"><v>1</v></c></row>
          </sheetData>
          <hyperlinks>
            <hyperlink ref="C:C" r:id="rId1"/>
          </hyperlinks>
        </worksheet>
        """;

    private static readonly string WholeColumnSourceWorksheetRelsXml =
        $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{StrippedTargetUrl}" TargetMode="External"/>
        </Relationships>
        """;

    [Fact]
    public void InsertColumnBeforeIt_ShiftsWholeColumnHyperlinkRefRight()
    {
        using var source = CreateSourcePackage(WholeColumnSourceWorksheetXml, WholeColumnSourceWorksheetRelsXml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // Real product entry point: the only code path that ever moves a hyperlink's address when
        // sheet structure changes. Inserting a column before column A shifts every column right by
        // one -- the whole-column hyperlink on "C:C" must become "D:D".
        var ctx = new TestCommandContext(workbook);
        new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var hyperlinksElement = GetSavedHyperlinksElement(saved);
        var refs = hyperlinksElement.Elements(WorkbookNs + "hyperlink")
            .Select(e => e.Attribute("ref")?.Value)
            .ToList();

        refs.Should().Contain("D:D",
            "the whole-column hyperlink must shift from C:C to D:D after a real column insert before it");
        refs.Should().NotContain("C:C",
            "the stale pre-insert ref must not survive the save once the column it anchored to has moved");
    }

    [Fact]
    public void DeleteColumnBeforeIt_ShiftsWholeColumnHyperlinkRefLeft()
    {
        using var source = CreateSourcePackage(WholeColumnSourceWorksheetXml, WholeColumnSourceWorksheetRelsXml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        var ctx = new TestCommandContext(workbook);
        new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 1).Apply(ctx);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var hyperlinksElement = GetSavedHyperlinksElement(saved);
        var refs = hyperlinksElement.Elements(WorkbookNs + "hyperlink")
            .Select(e => e.Attribute("ref")?.Value)
            .ToList();

        refs.Should().Contain("B:B",
            "the whole-column hyperlink must shift from C:C to B:B after a real column delete before it");
        refs.Should().NotContain("C:C");
    }

    [Fact]
    public void InsertRowBeforeIt_ShiftsWholeRowHyperlinkRefDown()
    {
        using var source = CreateSourcePackage(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
              <hyperlinks>
                <hyperlink ref="3:3" r:id="rId1"/>
              </hyperlinks>
            </worksheet>
            """,
            WholeColumnSourceWorksheetRelsXml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        var ctx = new TestCommandContext(workbook);
        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var hyperlinksElement = GetSavedHyperlinksElement(saved);
        var refs = hyperlinksElement.Elements(WorkbookNs + "hyperlink")
            .Select(e => e.Attribute("ref")?.Value)
            .ToList();

        refs.Should().Contain("4:4",
            "the whole-row hyperlink must shift from 3:3 to 4:4 after a real row insert before it");
        refs.Should().NotContain("3:3");
    }

    // No-regression sibling: a structural edit that does NOT touch anything at or before the
    // range-hyperlink's own column/row must leave its ref completely unchanged -- proving the fix
    // does not overshift (or spuriously touch) a range hyperlink the edit never reached, mirroring
    // the pre-existing pass-through behavior R99_HyperlinkRelationshipRebindTests already covers for
    // the "no structural edit at all" case.
    [Fact]
    public void InsertColumnAfterIt_LeavesWholeColumnHyperlinkRefUnchanged()
    {
        using var source = CreateSourcePackage(WholeColumnSourceWorksheetXml, WholeColumnSourceWorksheetRelsXml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        var ctx = new TestCommandContext(workbook);
        // Insert a column well after column C (the hyperlink's own column) -- it must not move.
        new InsertColumnsCommand(sheet.Id, beforeCol: 10, count: 1).Apply(ctx);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var hyperlinksElement = GetSavedHyperlinksElement(saved);
        var refs = hyperlinksElement.Elements(WorkbookNs + "hyperlink")
            .Select(e => e.Attribute("ref")?.Value)
            .ToList();

        refs.Should().Contain("C:C",
            "a structural edit strictly after the hyperlink's own column must leave its ref unchanged");
    }
}
