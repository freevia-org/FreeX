using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 124 regression tests for the HasRangeHyperlinkRefs zip-bomb DoS defect
/// (src/FreeX.Core.IO/XlsxClosedXmlLoadPackageSanitizer.cs:529, HIGH severity).
///
/// HasRangeHyperlinkRefs runs unconditionally as the very first step of
/// XlsxClosedXmlLoadPackageSanitizer.Create() -- i.e. on every single .xlsx load's main path,
/// before any ClosedXML parsing. It used to read every worksheet-xml zip entry via a raw
/// `StreamReader.ReadToEnd()` with no character/byte cap, unlike every other XML part in this
/// codebase (which routes through XlsxPackageXmlEditor.LoadXml / OpcXml.LoadXml, applying
/// SecureXmlReaderSettings with a MaxCharactersInDocument ceiling). WorkbookOpenSizeGuard only
/// validates the zip central directory's *declared* entry Length/CompressedLength -- fields an
/// attacker fully controls -- and never verifies what the DeflateStream actually yields when
/// read, so a worksheet part whose declared header size passes the guard but whose real
/// decompressed content is huge would previously be slurped entirely into a single in-memory
/// string, unbounded.
///
/// These tests invoke the private HasRangeHyperlinkRefs method directly via reflection. This is
/// the exact seam named in the defect (the method whose unbounded read is the bug), and testing
/// through the full public Create() entry point would conflate the measurement with several
/// unrelated, already-capped-or-uncapped worksheet scans that GetSanitizationRequirements also
/// runs on the same oversized fixture (see R124_CreateEndToEnd... below for an entry-point-level
/// companion test that isn't a memory-bound assertion for that reason).
/// </summary>
public sealed class R124_HasRangeHyperlinkRefsMemoryBoundTests
{
    private static readonly MethodInfo HasRangeHyperlinkRefsMethod =
        typeof(XlsxClosedXmlLoadPackageSanitizer).GetMethod(
            "HasRangeHyperlinkRefs",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "HasRangeHyperlinkRefs seam not found -- update this test if the method was renamed.");

    private const long AllocationCeiling = 250_000_000; // 250 MB

    // A highly-compressible worksheet body: valid, well-formed XML overall (opens with a real
    // <worksheet> root, closes it properly) but with ~300M characters of filler inside a
    // comment, forcing any bounded reader/parser to give up well before reaching the end. This
    // isn't an adversarial hand-crafted DEFLATE bitstream (that trick targets the *declared vs
    // real size* mismatch in WorkbookOpenSizeGuard, a different file/finding) -- it's simply
    // realistic repetitive content that DEFLATE compresses to a few KB almost instantly, which
    // is sufficient to prove that HasRangeHyperlinkRefs itself no longer materializes the whole
    // thing in memory once it's actually read back.
    private const int FillerCharacterCount = 300_000_000;

    [Fact]
    public void HasRangeHyperlinkRefs_DoesNotUnboundedlyMaterializeAnOversizedWorksheetEntry()
    {
        using var package = CreateOversizedWorksheetPackage(FillerCharacterCount);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = InvokeHasRangeHyperlinkRefs(package);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The correctness of the boolean result isn't the point here (this filler worksheet has
        // no hyperlinks either way) -- the point is that reaching that answer must not require
        // holding the entire ~300M-character decompressed part in memory as one string, the way
        // StreamReader.ReadToEnd() did. A capped reader gives up (throws, caught, "false") having
        // consumed only up to its character cap, not the full manufactured payload.
        result.Should().BeFalse();
        allocated.Should().BeLessThan(
            AllocationCeiling,
            "HasRangeHyperlinkRefs must route worksheet reads through the same char-capped " +
            "reader every other XML part in this codebase uses, instead of an unbounded " +
            "StreamReader.ReadToEnd() that would allocate close to the full " +
            $"{FillerCharacterCount:N0}-character decompressed payload (~{FillerCharacterCount * 2L:N0} bytes as a UTF-16 string)");
    }

    // --- no-regression sibling: the same seam must still correctly detect a genuine, normally-
    // sized oversized-range hyperlink ref (the exact scenario R18_hyperlink_Tests already covers
    // for ContainsRangeHyperlinkRef/StripRangeHyperlinkRefs in isolation) once routed through the
    // new capped reader. ---

    [Fact]
    public void HasRangeHyperlinkRefs_StillDetectsGenuineOversizedBoundedRangeHyperlink()
    {
        using var package = CreateNormalSizedWorksheetPackageWithHyperlinkRef("A1:Z100000");

        InvokeHasRangeHyperlinkRefs(package).Should().BeTrue(
            "a normal-sized worksheet with a real oversized bounded-range hyperlink ref must " +
            "still be detected after routing through the capped reader -- only the unbounded " +
            "memory allocation was the defect, not the detection logic");
    }

    [Fact]
    public void HasRangeHyperlinkRefs_ReturnsFalseForOrdinaryWorksheetWithNoHyperlinks()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            MinimalContentTypesEntry(),
            MinimalRootRelsEntry(),
            MinimalWorkbookEntry(),
            MinimalWorkbookRelsEntry(),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        InvokeHasRangeHyperlinkRefs(package).Should().BeFalse();
    }

    // --- entry-point-level companion: proves the whole public Create() pipeline still strips a
    // genuine oversized-range hyperlink after this fix, not just the private detection seam. ---

    [Fact]
    public void Create_StillStripsGenuineOversizedBoundedRangeHyperlinkThroughThePublicEntryPoint()
    {
        using var package = CreateNormalSizedWorksheetPackageWithHyperlinkRef("A1:Z100000");

        using var sanitized = XlsxClosedXmlLoadPackageSanitizer.Create(package);

        sanitized.Position = 0;
        using var archive = new ZipArchive(sanitized, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        worksheetXml.Root!.Element(ns + "hyperlinks").Should().BeNull(
            "the real Create() entry point must still strip an oversized bounded-range " +
            "hyperlink ref end to end after routing the pre-check gate through a capped reader");
    }

    private static bool InvokeHasRangeHyperlinkRefs(MemoryStream package) =>
        (bool)HasRangeHyperlinkRefsMethod.Invoke(null, [package])!;

    private static MemoryStream CreateOversizedWorksheetPackage(int fillerCharacterCount)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", MinimalContentTypesXml);
            WriteEntry(archive, "_rels/.rels", MinimalRootRelsXml);
            WriteEntry(archive, "xl/workbook.xml", MinimalWorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", MinimalWorkbookRelsXml);

            var worksheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using var entryStream = worksheetEntry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><!--");

            // Write the filler in reused chunks rather than materializing one giant string, so
            // the *test fixture construction itself* isn't what allocates hundreds of MB (that
            // would muddy the GC.GetAllocatedBytesForCurrentThread measurement taken only around
            // the HasRangeHyperlinkRefs call below).
            const int chunkSize = 1_000_000;
            var chunk = new string('x', chunkSize);
            var remaining = fillerCharacterCount;
            while (remaining > 0)
            {
                var toWrite = Math.Min(chunkSize, remaining);
                writer.Write(toWrite == chunkSize ? chunk : chunk[..toWrite]);
                remaining -= toWrite;
            }

            writer.Write("--></worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateNormalSizedWorksheetPackageWithHyperlinkRef(string rangeRef) =>
        XlsxPackageTestFixtures.CreatePackage(
            MinimalContentTypesEntry(),
            MinimalRootRelsEntry(),
            MinimalWorkbookEntry(),
            MinimalWorkbookRelsEntry(),
            (
                "xl/worksheets/sheet1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="{rangeRef}" location="Sheet1!A1" display="Jump"/>
                  </hyperlinks>
                </worksheet>
                """));

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private const string MinimalContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private const string MinimalRootRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string MinimalWorkbookXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private const string MinimalWorkbookRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static (string, string) MinimalContentTypesEntry() => ("[Content_Types].xml", MinimalContentTypesXml);
    private static (string, string) MinimalRootRelsEntry() => ("_rels/.rels", MinimalRootRelsXml);
    private static (string, string) MinimalWorkbookEntry() => ("xl/workbook.xml", MinimalWorkbookXml);
    private static (string, string) MinimalWorkbookRelsEntry() => ("xl/_rels/workbook.xml.rels", MinimalWorkbookRelsXml);
}
