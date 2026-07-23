using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R76-io-richtext-runs-4-1: editing a rich-text cell's own run formatting (e.g. bolding the
/// whole cell) must not delete the cell's phonetic guide (furigana) -- the &lt;rPh&gt; run(s)
/// and &lt;phoneticPr&gt; element inside its &lt;is&gt;/&lt;si&gt;. Before the fix,
/// <see cref="XlsxRichRunReader"/> never captured rPh/phoneticPr, <see cref="CellTextRun"/> had
/// no field for them, and <see cref="XlsxRichRunWriter"/> only ever emitted &lt;r&gt; children,
/// so a patch-save that rewrote the cell's &lt;is&gt; (because its run formatting changed)
/// silently dropped the phonetic guide even though the underlying text never changed.
/// </summary>
public sealed class R76_PhoneticGuidePreservationTests
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

    private static Workbook LoadXlsx(Stream stream)
    {
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    private static XElement ReadWorksheetCell(Stream stream, string reference)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var xmlStream = entry.Open();
        var doc = XDocument.Load(xmlStream);
        XNamespace ns = WorkbookNs;
        return doc.Root!
            .Element(ns + "sheetData")!
            .Descendants(ns + "c")
            .Single(element => element.Attribute("r")?.Value == reference);
    }

    // ── Primary: bolding a phonetic-guide cell's own runs must not drop rPh/phoneticPr ──

    [Fact]
    public void PatchSave_BoldingPhoneticCellsOwnRuns_PreservesRPhAndPhoneticPr()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>Rich </t></r>
                  <r><t>phonetic</t></r>
                  <rPh sb="0" eb="4"><t>ri-chi</t></rPh>
                  <phoneticPr fontId="1" type="noConversion"/>
                </is>
              </c>
            </row>
            """);

        var adapter  = new XlsxFileAdapter();
        var workbook = adapter.Load(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);

        // Sanity: the phonetic guide loaded.
        sheet.CellPhoneticGuides.Should().ContainKey(addr);
        var loadedRuns = sheet.RichTextRuns.GetValueOrDefault(addr) ?? new List<CellTextRun>
        {
            new("Rich ", null, null, null, null, null, null, null),
            new("phonetic", null, null, null, null, null, null, null),
        };

        // Materialize the baseline against the freshly-loaded state (mirrors what the real app
        // does right after opening a file), so the upcoming run-formatting edit is detected as
        // a genuine diff rather than lazily baselined from the already-edited state.
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason)
            .Should().BeTrue(prepareBlockReason);

        // "Bold the whole cell": rewrite every run's formatting, leaving Value/StyleId untouched.
        sheet.RichTextRuns[addr] = loadedRuns
            .Select(r => r with { Bold = true })
            .ToList();

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Confirm this actually exercised the incremental patch-save path (not a full-save
        // fallback), i.e. the CellStyle/RichRunsChanged branch under test really ran.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        var savedCell = ReadWorksheetCell(saved, "A1");
        XNamespace ns = WorkbookNs;
        var savedIs = savedCell.Element(ns + "is");
        savedIs.Should().NotBeNull("the cell must still be an inline rich string after the edit");

        var savedRPh = savedIs!.Elements(ns + "rPh").ToList();
        savedRPh.Should().ContainSingle(
            "the phonetic guide's <rPh> run must survive an edit to the cell's own run formatting");
        savedRPh[0].Attribute("sb")?.Value.Should().Be("0");
        savedRPh[0].Attribute("eb")?.Value.Should().Be("4");
        savedRPh[0].Element(ns + "t")?.Value.Should().Be("ri-chi");

        var savedPhoneticPr = savedIs.Element(ns + "phoneticPr");
        savedPhoneticPr.Should().NotBeNull(
            "the <phoneticPr> element must survive an edit to the cell's own run formatting");
        savedPhoneticPr!.Attribute("fontId")?.Value.Should().Be("1");
        savedPhoneticPr.Attribute("type")?.Value.Should().Be("noConversion");

        // The runs themselves must also carry the edit (Bold=true) and their text.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var rs       = reloaded.GetSheetAt(0);
        var rAddr    = A(rs, 1, 1);
        rs.RichTextRuns.Should().ContainKey(rAddr);
        var reloadedRuns = rs.RichTextRuns[rAddr];
        reloadedRuns.Should().HaveCount(2);
        reloadedRuns[0].Bold.Should().BeTrue();
        reloadedRuns[0].Text.Should().Be("Rich ");
        reloadedRuns[1].Bold.Should().BeTrue();
        reloadedRuns[1].Text.Should().Be("phonetic");

        // The reloaded model must also carry the phonetic guide forward.
        rs.CellPhoneticGuides.Should().ContainKey(rAddr);
    }

    // ── Sibling: a non-phonetic rich cell round-trips its runs unchanged through the same edit ──

    [Fact]
    public void PatchSave_BoldingNonPhoneticCellsOwnRuns_RoundTripsWithoutAnyPhoneticGuide()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>Hello </t></r>
                  <r><t>World</t></r>
                </is>
              </c>
            </row>
            """);

        var adapter  = new XlsxFileAdapter();
        var workbook = adapter.Load(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);

        sheet.CellPhoneticGuides.Should().NotContainKey(addr);
        var loadedRuns = sheet.RichTextRuns[addr];

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason)
            .Should().BeTrue(prepareBlockReason);

        sheet.RichTextRuns[addr] = loadedRuns.Select(r => r with { Bold = true }).ToList();

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        var savedCell = ReadWorksheetCell(saved, "A1");
        XNamespace ns = WorkbookNs;
        var savedIs = savedCell.Element(ns + "is");
        savedIs.Should().NotBeNull();
        savedIs!.Elements(ns + "rPh").Should().BeEmpty(
            "a cell with no phonetic guide must not gain a spurious <rPh> from this edit path");
        savedIs.Element(ns + "phoneticPr").Should().BeNull(
            "a cell with no phonetic guide must not gain a spurious <phoneticPr> from this edit path");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var rs       = reloaded.GetSheetAt(0);
        var rAddr    = A(rs, 1, 1);
        rs.CellPhoneticGuides.Should().NotContainKey(rAddr);
        rs.RichTextRuns[rAddr].Should().OnlyContain(r => r.Bold == true);
    }

    // ── Sibling: a plain (non-rich) cell edit is unaffected by the phonetic-guide plumbing ──

    [Fact]
    public void PatchSave_PlainCellEdit_UnaffectedByPhoneticGuidePlumbing()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr"><is><t>plain</t></is></c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);

        sheet.CellPhoneticGuides.Should().BeEmpty();
        sheet.RichTextRuns.Should().BeEmpty();

        sheet.SetCell(addr, new TextValue("changed"));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var rs       = reloaded.GetSheetAt(0);
        rs.GetValue(A(rs, 1, 1)).Should().Be(new TextValue("changed"));
        rs.CellPhoneticGuides.Should().BeEmpty();
    }
}
