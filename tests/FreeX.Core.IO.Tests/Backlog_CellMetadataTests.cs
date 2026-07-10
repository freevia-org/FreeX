using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog regression coverage for two long-deferred XlsxWorksheetMetadataPreserver.CellMetadata.cs gaps:
/// (a) an oversized bounded-range hyperlink that gets stripped from the ClosedXML-input copy at load
///     time (so ClosedXML never materializes it) must still be re-emitted verbatim on a full (non-patch)
///     save, and
/// (b) a rich-value cell's vm/cm attributes must only be reattached during a full-rewrite native-metadata
///     merge when the cell's current t/formula/&lt;v&gt; still match what the metadata was captured
///     against -- an edited cell must drop stale vm/cm rather than resurrect it.
/// </summary>
public sealed class Backlog_cellmetadata_Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Builds a fully valid single-sheet .xlsx package (via a real adapter save, so every required
    // package part -- content types, workbook.xml, styles, etc. -- is already correct) and then swaps in
    // hand-authored worksheet XML for xl/worksheets/sheet1.xml. This mirrors the technique
    // XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage uses, without touching that file.
    private static MemoryStream CreateSourcePackage(string worksheetXml)
    {
        var workbook = new Workbook("Backlog-CellMetadata");
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
            using var writer = new StreamWriter(replacementEntry.Open());
            writer.Write(worksheetXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static Dictionary<string, XElement> LoadSavedCellsByAddress(MemoryStream saved)
    {
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull();
        using var entryStream = worksheetEntry!.Open();
        var worksheetXml = XDocument.Load(entryStream);

        return worksheetXml.Root!
            .Descendants(WorkbookNs + "c")
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute("r")?.Value))
            .ToDictionary(cell => cell.Attribute("r")!.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullSave_ReemitsOversizedBoundedRangeHyperlinkStrippedAtLoad()
    {
        // A1:A200000 is 200,000 cells -- above the 100,000-cell cap XlsxWorksheetHyperlinkNormalizer
        // uses to strip a bounded-range hyperlink from the ClosedXML-input copy before load (so ClosedXML
        // never materializes ~200k per-cell hyperlink entries). The original ref is only ever visible via
        // the untouched source package bytes, not via anything ClosedXML's model knows about.
        using var source = CreateSourcePackage("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
              <hyperlinks>
                <hyperlink ref="A1:A200000" location="Sheet1!A1" display="Sheet1!A1"/>
              </hyperlinks>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // Editing a brand-new cell (outside the loaded sheetData) forces a full (non-patch) rewrite --
        // the same technique XlsxCorpusRunnerTests.Retention.cs uses to exercise
        // XlsxWorksheetMetadataPreserver on an ordinary model edit.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-hyperlink-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull();
        using var entryStream = worksheetEntry!.Open();
        var savedWorksheetXml = XDocument.Load(entryStream);

        var hyperlink = savedWorksheetXml.Root!
            .Element(WorkbookNs + "hyperlinks")?
            .Elements(WorkbookNs + "hyperlink")
            .FirstOrDefault(element => string.Equals(element.Attribute("ref")?.Value, "A1:A200000", StringComparison.Ordinal));

        hyperlink.Should().NotBeNull(
            "a bounded-range hyperlink stripped at load time because it exceeds the per-cell materialization " +
            "cap must still be re-emitted verbatim on a full (non-patch) save");
        hyperlink!.Attribute("location")?.Value.Should().Be("Sheet1!A1");
    }

    [Fact]
    public void FullSave_KeepsWholeColumnHyperlinkAcrossModelEdit()
    {
        // Whole-column refs (e.g. "C:C") hit the same load-time strip path via
        // IsWholeColumnOrRowRef/IsWholeColumnOrRowHyperlinkRef rather than the cell-count cap.
        using var source = CreateSourcePackage("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="C1"><v>1</v></c></row>
              </sheetData>
              <hyperlinks>
                <hyperlink ref="C:C" location="Sheet1!C1" display="Sheet1!C1"/>
              </hyperlinks>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-whole-column-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull();
        using var entryStream = worksheetEntry!.Open();
        var savedWorksheetXml = XDocument.Load(entryStream);

        var hyperlink = savedWorksheetXml.Root!
            .Element(WorkbookNs + "hyperlinks")?
            .Elements(WorkbookNs + "hyperlink")
            .FirstOrDefault(element => string.Equals(element.Attribute("ref")?.Value, "C:C", StringComparison.Ordinal));

        hyperlink.Should().NotBeNull(
            "a whole-column hyperlink stripped at load time must still be re-emitted verbatim on a full save");
    }

    [Fact]
    public void FullSave_DropsStaleRichValueMetadataOnEditedCell_ButKeepsItOnUneditedCell()
    {
        // A2 (vm="1", v=42) is left untouched; A3 (vm="2", v=100) has its value edited to 999. Neither
        // cell carries an explicit t attribute -- both are plain numbers, the OOXML default type, so the
        // full-rewrite output's own type representation for a plain number is identical for both cells
        // regardless of whichever convention the writer uses.
        using var source = CreateSourcePackage("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="2"><c r="A2" vm="1"><v>42</v></c></row>
                <row r="3"><c r="A3" vm="2"><v>100</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(999));
        // Editing a brand-new cell forces a full (non-patch) rewrite, exercising
        // XlsxWorksheetMetadataPreserver's native-metadata merge rather than the byte-patch path.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-richvalue-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var savedCells = LoadSavedCellsByAddress(saved);

        savedCells.Should().ContainKey("A2");
        savedCells["A2"].Attribute("vm")?.Value.Should().Be(
            "1",
            "an unedited rich-value cell's t/formula/<v> still match what the vm metadata was captured " +
            "against, so vm must be reattached on a full save");

        savedCells.Should().ContainKey("A3");
        savedCells["A3"].Attribute("vm").Should().BeNull(
            "an edited rich-value cell's <v> no longer matches what the vm metadata was captured against, " +
            "so the stale vm must be dropped rather than reattached to the new value");
    }

    [Fact]
    public void FullSave_KeepsCellMetadataAttribute_WhenNotARichValueAttribute()
    {
        // Guards against an over-broad fix: an ordinary preserved non-modeled cell attribute (unrelated to
        // vm/cm) on an edited cell must still be retained -- only vm/cm are value-gated.
        using var source = CreateSourcePackage("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="2"><c r="A2" ph="1"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(999));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("freex-ph-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var savedCells = LoadSavedCellsByAddress(saved);

        savedCells.Should().ContainKey("A2");
        savedCells["A2"].Attribute("ph")?.Value.Should().Be(
            "1",
            "non-rich-value native cell attributes (e.g. ph) are unaffected by the vm/cm value-equality guard");
    }
}
