using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R78-selfreg-twin-sweep-1: the full-save (non-patch, ClosedXML-driven) path never read
/// <see cref="Sheet.CellPhoneticGuides"/> anywhere in XlsxFileAdapter.Save.cs -- ApplyRichTextRuns
/// writes per-run formatting via ClosedXML's IXLRichText API, which has no way to express a
/// phonetic guide (furigana). Any save that falls back from the incremental patch-save path to a
/// full ClosedXML rewrite (e.g. because an UNRELATED edit -- like adding a new sheet -- makes the
/// patch-save diff ineligible) silently dropped every cell's &lt;rPh&gt;/&lt;phoneticPr&gt;
/// markup, even for a cell whose own text/formatting never changed.
/// </summary>
public sealed class R78_FullSavePhoneticGuideTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static CellAddress A(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static MemoryStream BuildMinimalXlsx(string sheetDataInnerXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                {sheetDataInnerXml}
              </sheetData>
            </worksheet>
            """;

        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;

        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;

        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",        contentTypes);
            Write(archive, "_rels/.rels",                packageRels);
            Write(archive, "xl/workbook.xml",            workbookXml);
            Write(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            Write(archive, "xl/worksheets/sheet1.xml",   worksheetXml);
        }

        ms.Position = 0;
        return ms;

        static void Write(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    // ── Primary: an unrelated edit (adding a new sheet) forces a full ClosedXML rewrite; the
    //    untouched furigana cell's phonetic guide must survive it ──────────────────────────────

    [Fact]
    public void FullSave_ForcedByUnrelatedSheetAdd_PreservesPhoneticGuideOnUntouchedAndNewCells()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <t>田中</t>
                  <rPh sb="0" eb="2"><t>たなか</t></rPh>
                  <phoneticPr fontId="1" type="fullwidthKatakana"/>
                </is>
              </c>
            </row>
            """);

        var adapter  = new XlsxFileAdapter();
        var workbook = adapter.Load(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addrA1   = A(sheet, 1, 1);
        var addrB1   = A(sheet, 1, 2);

        // Sanity: the phonetic guide loaded, and this cell has no modeled rich runs (a plain
        // furigana cell with no bold/italic on the base text -- the common real-world case, and
        // the one XlsxRichRunLoader.ReadRuns returns null for since there is no <r> child).
        sheet.CellPhoneticGuides.Should().ContainKey(addrA1);
        sheet.RichTextRuns.Should().NotContainKey(addrA1);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason)
            .Should().BeTrue(prepareBlockReason);

        // Mirrors what CopyRangeCommand does on a copy/paste of a phonetic-guide cell: a BRAND-NEW
        // cell (never present in the source package at all, let alone with this guide) acquires
        // the same text and the same guide. Since the source package has no correspondence for
        // B1's shared-string entry, XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics
        // (which restores a SOURCE-verbatim guide by text+cell-address cross-check) cannot recover
        // it -- only reading Sheet.CellPhoneticGuides directly (this fix) can.
        sheet.SetCell(addrB1, new TextValue("田中"));
        sheet.CellPhoneticGuides[addrB1] = sheet.CellPhoneticGuides[addrA1];

        // An additional edit that has nothing to do with either phonetic cell, but makes the
        // patch-save diff ineligible (XlsxFileAdapter.SourcePackageSnapshot.cs's
        // "change_sheet_count" gate) -- forcing the fallback to a full ClosedXML rewrite the
        // finding describes.
        workbook.AddSheet("Sheet2");

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Confirm this actually took the FULL-save path (not patch-save) -- the branch under test.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var rs        = reloaded.GetSheetAt(0);
        var reloadedA1 = A(rs, 1, 1);
        var reloadedB1 = A(rs, 1, 2);

        rs.GetValue(reloadedA1).Should().Be(new TextValue("田中"));
        rs.CellPhoneticGuides.Should().ContainKey(reloadedA1,
            "a full ClosedXML rewrite must not silently drop an untouched cell's phonetic guide");

        rs.GetValue(reloadedB1).Should().Be(new TextValue("田中"));
        rs.CellPhoneticGuides.Should().ContainKey(reloadedB1,
            "a brand-new cell that acquired a phonetic guide in-memory (e.g. via copy/paste) must " +
            "keep it through a full ClosedXML rewrite, even though the source package never had " +
            "any content -- let alone a phonetic guide -- at this address");

        foreach (var rAddr in new[] { reloadedA1, reloadedB1 })
        {
            var guide = rs.CellPhoneticGuides[rAddr];
            guide.RunPhoneticXmls.Should().ContainSingle();
            XElement.Parse(guide.RunPhoneticXmls[0]).Element((XNamespace)WorkbookNs + "t")?.Value
                .Should().Be("たなか");
            guide.PhoneticPropertiesXml.Should().NotBeNull();
            XElement.Parse(guide.PhoneticPropertiesXml!).Attribute("type")?.Value
                .Should().Be("fullwidthKatakana");
        }
    }

    // ── Sibling: the same forced full-save, but the cell has NO phonetic guide -- must round-trip
    //    plainly, with no phantom rPh/phoneticPr ever introduced ────────────────────────────────

    [Fact]
    public void FullSave_ForcedByUnrelatedSheetAdd_PlainCellRoundTripsWithoutPhoneticGuide()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr"><is><t>plain text</t></is></c>
            </row>
            """);

        var adapter  = new XlsxFileAdapter();
        var workbook = adapter.Load(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);

        sheet.CellPhoneticGuides.Should().BeEmpty();

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason)
            .Should().BeTrue(prepareBlockReason);

        workbook.AddSheet("Sheet2");

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var rs       = reloaded.GetSheetAt(0);
        var rAddr    = A(rs, 1, 1);

        rs.GetValue(rAddr).Should().Be(new TextValue("plain text"));
        rs.CellPhoneticGuides.Should().BeEmpty(
            "a cell with no phonetic guide must never gain a spurious one from this writer");
    }
}
