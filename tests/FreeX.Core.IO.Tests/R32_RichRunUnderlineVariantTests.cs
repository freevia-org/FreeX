using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R32-io-sharedstrings-richtext-deep-1/2: a rich-text run's double/accounting underline must not
/// be silently downgraded to a plain single underline the first time the cell is edited (previously
/// <see cref="CellTextRun.Underline"/> was a bare bool with no double/accounting discriminator, so
/// both the patch-save writer and the ClosedXML full-save writer always re-emitted single).
/// Also covers the smaller richtext-2 follow-on (rPr charset/family/scheme survive an edit).
/// </summary>
public sealed class R32_RichRunUnderlineVariantTests
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

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        return ms;
    }

    // ── Reader: double/accounting variants are distinguished from plain single ──

    [Fact]
    public void Reader_DoubleUnderline_IsFlaggedDistinctFromSingle()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><u val="double"/></rPr><t>Total</t></r>
                  <r><rPr><u/></rPr><t> 500</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var runs     = sheet.RichTextRuns[A(sheet, 1, 1)];

        runs[0].Underline.Should().BeTrue();
        runs[0].DoubleUnderline.Should().BeTrue("val=\"double\" must be distinguished from single");

        // Sibling: bare <u/> (single) must NOT be flagged as double.
        runs[1].Underline.Should().BeTrue();
        runs[1].DoubleUnderline.Should().NotBe(true, "a plain single underline must stay single");
    }

    [Fact]
    public void Reader_DoubleAccountingUnderline_IsAlsoFlaggedAsDouble()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><u val="doubleAccounting"/></rPr><t>Subtotal</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var run      = sheet.RichTextRuns[A(sheet, 1, 1)][0];

        run.Underline.Should().BeTrue();
        run.DoubleUnderline.Should().BeTrue("doubleAccounting is a double-underline variant");
    }

    // ── Patch-save: editing the SAME cell must not downgrade double to single ──

    [Fact]
    public void PatchSave_EditingCellWithDoubleUnderlineRun_PreservesDoubleUnderline()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><u val="double"/></rPr><t>Total: 500</t></r>
                </is>
              </c>
            </row>
            <row r="2">
              <c r="A2" t="inlineStr">
                <is>
                  <r><rPr><u/></rPr><t>Plain single</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr1    = A(sheet, 1, 1);
        var addr2    = A(sheet, 2, 1);

        var loadedRuns1 = sheet.RichTextRuns[addr1];
        loadedRuns1[0].DoubleUnderline.Should().BeTrue();

        // Ordinary user edit: re-type the SAME cell's text, keeping the (already-loaded) run
        // formatting — this is exactly the path that re-emits the run via the writer.
        sheet.SetCell(addr1, new TextValue("Total: 500"));
        sheet.RichTextRuns[addr1] = loadedRuns1;

        // Sibling: also edit the plain single-underline cell so both writer paths are exercised.
        var loadedRuns2 = sheet.RichTextRuns[addr2];
        sheet.SetCell(addr2, new TextValue("Plain single"));
        sheet.RichTextRuns[addr2] = loadedRuns2;

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        var reloadedRun1 = rs.RichTextRuns[A(rs, 1, 1)][0];
        reloadedRun1.Underline.Should().BeTrue();
        reloadedRun1.DoubleUnderline.Should().BeTrue(
            "double underline must survive an edit of the very cell it lives in, not collapse to single");

        var reloadedRun2 = rs.RichTextRuns[A(rs, 2, 1)][0];
        reloadedRun2.Underline.Should().BeTrue();
        reloadedRun2.DoubleUnderline.Should().NotBe(true,
            "a plain single-underline run must remain single after the same edit path");
    }

    // ── Full-save (ClosedXML) path: brand-new Workbook, no source package ──

    [Fact]
    public void FullSave_DoubleUnderlineRun_SurvivesRoundTrip()
    {
        var workbook = new Workbook("FullSaveDoubleUnderline");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("Total: 500"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Total: 500",
                Bold: null, Italic: null, Underline: true, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null,
                DoubleUnderline: true),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        var run = rs.RichTextRuns[A(rs, 1, 1)][0];
        run.Underline.Should().BeTrue();
        run.DoubleUnderline.Should().BeTrue(
            "full-save (ClosedXML) path must also preserve double underline via XLFontUnderlineValues.Double");
    }

    [Fact]
    public void FullSave_SingleUnderlineRun_StaysSingle()
    {
        // Sibling case: an ordinary single-underline run must not regress to double.
        var workbook = new Workbook("FullSaveSingleUnderline");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("Plain"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Plain",
                Bold: null, Italic: null, Underline: true, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        var run = rs.RichTextRuns[A(rs, 1, 1)][0];
        run.Underline.Should().BeTrue();
        run.DoubleUnderline.Should().NotBe(true, "an ordinary single underline must not become double");
    }

    // ── richtext-2 (partial): charset/family/scheme survive an edit ──

    [Fact]
    public void PatchSave_EditingCellWithCharsetFamilyScheme_PreservesThem()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><rFont val="Arial"/><charset val="128"/><family val="2"/><scheme val="minor"/></rPr><t>CJK</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);

        var loadedRuns = sheet.RichTextRuns[addr];
        loadedRuns[0].Charset.Should().Be(128);
        loadedRuns[0].Family.Should().Be(2);
        loadedRuns[0].Scheme.Should().Be("minor");

        // Ordinary edit of the same cell — this is the path that previously stripped
        // charset/family/scheme once the plain-text preserver's key no longer matched.
        sheet.SetCell(addr, new TextValue("CJK"));
        sheet.RichTextRuns[addr] = loadedRuns;

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        var run = rs.RichTextRuns[A(rs, 1, 1)][0];
        run.Charset.Should().Be(128);
        run.Family.Should().Be(2);
        run.Scheme.Should().Be("minor");
    }
}
