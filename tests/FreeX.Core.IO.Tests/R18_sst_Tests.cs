using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 18 findings:
///   R18-shared-string-richtext-io-1 — a rich run's raw &lt;t&gt; text was read without decoding
///     OOXML _xHHHH_ escapes (e.g. _x000D_ -> CR), so the literal escape text was stored and
///     re-escaped (compounding) on every subsequent save.
///   R18-shared-string-richtext-io-3 — same-plain-text rich shared-string duplicates were paired
///     source[i] -&gt; target[i] by raw sharedStrings.xml document order, which silently swaps
///     formatting between two cells sharing the same text when the source SST was not written in
///     first-use order (a non-Excel generator).
/// </summary>
public sealed class R18_sst_Tests
{
    private const string WorkbookNsUri = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookNs = WorkbookNsUri;

    // ── R18-shared-string-richtext-io-1 ──────────────────────────────────────

    /// <summary>
    /// Unit-level: a rich run whose raw &lt;t&gt; is "Line1_x000D_Line2" must decode to a real
    /// carriage return, not the literal 7-character escape sequence. Uses two runs so
    /// <see cref="XlsxRichRunReader.ReadRuns"/> does not take its "single unstyled run" shortcut
    /// (which would return null and hide the bug).
    /// </summary>
    [Fact]
    public void ReadRuns_RunWithCarriageReturnEscape_DecodesToRealCr()
    {
        XNamespace ns = WorkbookNsUri;
        var si = new XElement(ns + "si",
            new XElement(ns + "r",
                new XElement(ns + "rPr", new XElement(ns + "b")),
                new XElement(ns + "t", "Bold")),
            new XElement(ns + "r",
                new XElement(ns + "t", "Line1_x000D_Line2")));

        var readRuns = XlsxRichRunReader.ReadRuns(si, ns, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        readRuns.Should().NotBeNull();
        var runs = readRuns!;
        runs.Should().HaveCount(2);
        runs[1].Text.Should().Be("Line1\rLine2", "the _x000D_ escape must decode to a real CR");
        runs[1].Text.Should().NotContain("_x000D_", "the literal escape text must not survive the read");
    }

    // ── R18-shared-string-richtext-io-3 ──────────────────────────────────────

    /// <summary>
    /// Two same-text rich shared strings whose source sharedStrings.xml document order is the
    /// REVERSE of the cells' actual first-use order (a non-Excel generator) must still be paired
    /// to the correct target occurrence — by first-use cell order, not raw source document order —
    /// so each cell keeps its own formatting instead of swapping with the other cell's.
    /// </summary>
    [Fact]
    public void PreserveRichTextAndPhonetics_DuplicateText_NonFirstUseSourceOrder_DoesNotSwapFormatting()
    {
        // Source SST document order: index 0 = italic "Dup", index 1 = bold "Dup".
        // But the source WORKSHEET references index 1 (bold) at A1 (first) and index 0 (italic)
        // at A2 (second) -- i.e. the source SST was NOT written in first-use order.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <si><r><rPr><i/></rPr><t>Dup</t></r></si>
                  <si><r><rPr><b/></rPr><t>Dup</t></r></si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>1</v></c></row>
                    <row r="2"><c r="A2" t="s"><v>0</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        // Target mirrors ClosedXML's regenerated (first-use-ordered) SST: index 0 is the slot for
        // A1 (which held the BOLD source string), index 1 is the slot for A2 (the ITALIC one).
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><r><t>Dup</t></r></si>
              <si><r><t>Dup</t></r></si>
            </sst>
            """));

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetPackage.Position = 0;
        using var verifyArchive = new ZipArchive(targetPackage, ZipArchiveMode.Read, leaveOpen: true);
        var xml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/sharedStrings.xml", "xl/sharedStrings.xml");
        var strings = xml.Root!.Elements(WorkbookNs + "si").ToList();

        strings.Should().HaveCount(2);

        // Target index 0 is A1's slot and must get the BOLD source formatting (not italic).
        strings[0].Elements(WorkbookNs + "r").Single().Element(WorkbookNs + "rPr")!
            .Element(WorkbookNs + "b").Should().NotBeNull(
                "target index 0 backs cell A1, which used the BOLD source string first");
        strings[0].Elements(WorkbookNs + "r").Single().Element(WorkbookNs + "rPr")!
            .Element(WorkbookNs + "i").Should().BeNull(
                "target index 0 must not receive the italic formatting meant for A2");

        // Target index 1 is A2's slot and must get the ITALIC source formatting (not bold).
        strings[1].Elements(WorkbookNs + "r").Single().Element(WorkbookNs + "rPr")!
            .Element(WorkbookNs + "i").Should().NotBeNull(
                "target index 1 backs cell A2, which used the ITALIC source string second");
        strings[1].Elements(WorkbookNs + "r").Single().Element(WorkbookNs + "rPr")!
            .Element(WorkbookNs + "b").Should().BeNull(
                "target index 1 must not receive the bold formatting meant for A1");
    }

    // ── Package helpers (mirrors XlsxRichRunSchemaOrderTests / XlsxRichTextRunRoundTripTests) ──

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        return ms;
    }

    private static Workbook LoadXlsx(Stream stream)
    {
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    private static MemoryStream BuildMinimalXlsx(string sheetDataInnerXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>{sheetDataInnerXml}</sheetData>
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
              <Default Extension="xml"  ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml",        contentTypes);
            WriteEntry(archive, "_rels/.rels",                packageRels);
            WriteEntry(archive, "xl/workbook.xml",            workbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            WriteEntry(archive, "xl/worksheets/sheet1.xml",   worksheetXml);
        }

        ms.Position = 0;
        return ms;

        static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
