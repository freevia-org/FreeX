using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip regression tests for the paper-size coercion bug (M1/RT1).
///
/// Before the fix, any OOXML <c>pageSetup/@paperSize</c> code that was not Letter/A4/Legal was
/// silently coerced to A4 on save. After the fix, the raw integer code is preserved on
/// <see cref="Sheet.PaperSizeCode"/> and round-trips verbatim through XLSX save + reload.
///
/// Test cases:
///   A3=8, Tabloid=3, A5=11, Executive=7  — must survive unchanged.
///   Letter=1, A4=9, Legal=5             — regression guard.
/// </summary>
public sealed class XlsxPaperSizeRoundTripTests
{
    private static readonly XNamespace WorksheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---------------------------------------------------------------------------
    // Build a minimal XLSX package with a given paperSize code
    // ---------------------------------------------------------------------------

    private static MemoryStream BuildMinimalXlsx(int paperSizeCode)
    {
        // Minimal valid workbook with one worksheet
        const string workbook =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""";

        const string workbookRels =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""";

        const string contentTypes =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>""";

        const string rootRels =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

        const string styles =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>""";

        const string sharedStrings =
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"></sst>""";

        var worksheetXml =
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{WorksheetNs}"><sheetData/><pageSetup paperSize="{paperSizeCode}" orientation="portrait"/></worksheet>""";

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", contentTypes);
            WriteEntry(archive, "_rels/.rels", rootRels);
            WriteEntry(archive, "xl/workbook.xml", workbook);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheetXml);
            WriteEntry(archive, "xl/styles.xml", styles);
            WriteEntry(archive, "xl/sharedStrings.xml", sharedStrings);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    // ---------------------------------------------------------------------------
    // Read the paperSize attribute from the saved XLSX worksheet XML
    // ---------------------------------------------------------------------------

    private static int? ReadSavedPaperSizeCode(MemoryStream savedXlsx)
    {
        savedXlsx.Position = 0;
        using var archive = new ZipArchive(savedXlsx, ZipArchiveMode.Read, leaveOpen: true);

        // Find the first worksheet entry
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (entry is null)
        {
            // ClosedXML may renumber sheets; try sheet by name search
            foreach (var e in archive.Entries)
            {
                if (e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }
        }

        if (entry is null)
            return null;

        using var reader = new StreamReader(entry.Open());
        var doc = XDocument.Parse(reader.ReadToEnd());
        var pageSetup = doc.Root!.Element(WorksheetNs + "pageSetup");
        var attr = pageSetup?.Attribute("paperSize");
        if (attr is null || !int.TryParse(attr.Value, out var code))
            return null;

        return code;
    }

    // ---------------------------------------------------------------------------
    // Core helper: load → model check → save → XML check → reload → model check
    // ---------------------------------------------------------------------------

    private static void AssertRoundTrip(
        int paperSizeCode,
        WorksheetPaperSize expectedEnum)
    {
        var adapter = new XlsxFileAdapter();

        // 1. Load the minimal XLSX
        using var source = BuildMinimalXlsx(paperSizeCode);
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // 2. Model assertions after load
        sheet.PaperSizeCode.Should().Be(paperSizeCode,
            $"PaperSizeCode should be {paperSizeCode} after load");
        sheet.PaperSize.Should().Be(expectedEnum,
            $"PaperSize enum should be {expectedEnum} for code {paperSizeCode}");

        // 3. Save
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // 4. Assert raw XML attribute in saved package
        var savedCode = ReadSavedPaperSizeCode(saved);
        savedCode.Should().Be(paperSizeCode,
            $"saved XML <pageSetup paperSize=\"...\"> should be {paperSizeCode}");

        // 5. Reload and assert model is preserved
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.PaperSizeCode.Should().Be(paperSizeCode,
            $"PaperSizeCode should still be {paperSizeCode} after reload");
        reloadedSheet.PaperSize.Should().Be(expectedEnum,
            $"PaperSize enum should still be {expectedEnum} after reload");
    }

    // ---------------------------------------------------------------------------
    // Non-Letter/A4/Legal tests — these were LOST before the fix
    // ---------------------------------------------------------------------------

    [Fact]
    public void A3_PaperSizeCode8_RoundTrips()
        => AssertRoundTrip(8, WorksheetPaperSize.A3);

    [Fact]
    public void Tabloid_PaperSizeCode3_RoundTrips()
        => AssertRoundTrip(3, WorksheetPaperSize.Tabloid);

    [Fact]
    public void A5_PaperSizeCode11_RoundTrips()
        => AssertRoundTrip(11, WorksheetPaperSize.A5);

    [Fact]
    public void Executive_PaperSizeCode7_RoundTrips()
        => AssertRoundTrip(7, WorksheetPaperSize.Executive);

    // ---------------------------------------------------------------------------
    // Regression guards — common sizes must still work
    // ---------------------------------------------------------------------------

    [Fact]
    public void Letter_PaperSizeCode1_RoundTrips()
        => AssertRoundTrip(1, WorksheetPaperSize.Letter);

    [Fact]
    public void A4_PaperSizeCode9_RoundTrips()
        => AssertRoundTrip(9, WorksheetPaperSize.A4);

    [Fact]
    public void Legal_PaperSizeCode5_RoundTrips()
        => AssertRoundTrip(5, WorksheetPaperSize.Legal);

    // ---------------------------------------------------------------------------
    // Unknown code preservation — an obscure code not in the enum map must not
    // be coerced to A4; the raw code must survive and PaperSize stays at A4 fallback.
    // ---------------------------------------------------------------------------

    [Fact]
    public void UnknownCode_RoundTrips_WithA4FallbackEnum()
    {
        const int unknownCode = 50; // not in PaperSizeCodes
        var adapter = new XlsxFileAdapter();

        using var source = BuildMinimalXlsx(unknownCode);
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        sheet.PaperSizeCode.Should().Be(unknownCode);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.A4, "unknown codes fall back to A4 enum");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var savedCode = ReadSavedPaperSizeCode(saved);
        savedCode.Should().Be(unknownCode, "unknown codes must be preserved verbatim in XML");
    }

    // ---------------------------------------------------------------------------
    // Model-only path: setting PaperSize enum sets PaperSizeCode consistently
    // ---------------------------------------------------------------------------

    [Fact]
    public void PaperSizeCodes_GetCode_MatchesExpectedOoxmlCodes()
    {
        PaperSizeCodes.GetCode(WorksheetPaperSize.Letter).Should().Be(1);
        PaperSizeCodes.GetCode(WorksheetPaperSize.Tabloid).Should().Be(3);
        PaperSizeCodes.GetCode(WorksheetPaperSize.Legal).Should().Be(5);
        PaperSizeCodes.GetCode(WorksheetPaperSize.Executive).Should().Be(7);
        PaperSizeCodes.GetCode(WorksheetPaperSize.A3).Should().Be(8);
        PaperSizeCodes.GetCode(WorksheetPaperSize.A4).Should().Be(9);
        PaperSizeCodes.GetCode(WorksheetPaperSize.A5).Should().Be(11);
        PaperSizeCodes.GetCode(WorksheetPaperSize.B4).Should().Be(12);
        PaperSizeCodes.GetCode(WorksheetPaperSize.B5).Should().Be(13);
        PaperSizeCodes.GetCode(WorksheetPaperSize.Folio).Should().Be(14);
    }

    [Fact]
    public void PaperSizeCodes_TryGetEnum_MatchesExpectedEnumValues()
    {
        PaperSizeCodes.TryGetEnum(1, out var s1).Should().BeTrue(); s1.Should().Be(WorksheetPaperSize.Letter);
        PaperSizeCodes.TryGetEnum(3, out var s3).Should().BeTrue(); s3.Should().Be(WorksheetPaperSize.Tabloid);
        PaperSizeCodes.TryGetEnum(5, out var s5).Should().BeTrue(); s5.Should().Be(WorksheetPaperSize.Legal);
        PaperSizeCodes.TryGetEnum(7, out var s7).Should().BeTrue(); s7.Should().Be(WorksheetPaperSize.Executive);
        PaperSizeCodes.TryGetEnum(8, out var s8).Should().BeTrue(); s8.Should().Be(WorksheetPaperSize.A3);
        PaperSizeCodes.TryGetEnum(9, out var s9).Should().BeTrue(); s9.Should().Be(WorksheetPaperSize.A4);
        PaperSizeCodes.TryGetEnum(11, out var s11).Should().BeTrue(); s11.Should().Be(WorksheetPaperSize.A5);
        PaperSizeCodes.TryGetEnum(50, out _).Should().BeFalse("code 50 is not in the map");
    }

    // ---------------------------------------------------------------------------
    // Clone preserves PaperSizeCode
    // ---------------------------------------------------------------------------

    [Fact]
    public void Sheet_Clone_PreservesPaperSizeCode()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.PaperSizeCode = 8; // A3
        sheet.PaperSize = WorksheetPaperSize.A3;

        var clone = sheet.Clone(new SheetId(Guid.NewGuid()), "Clone");

        clone.PaperSizeCode.Should().Be(8);
        clone.PaperSize.Should().Be(WorksheetPaperSize.A3);
    }
}
