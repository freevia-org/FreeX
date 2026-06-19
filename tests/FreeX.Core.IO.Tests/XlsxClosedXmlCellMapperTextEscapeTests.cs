using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxClosedXmlCellMapperTextEscapeTests
{
    // Excel and ClosedXML write characters they cannot emit literally into a string-valued formula's
    // cached <v> using the OOXML _xHHHH_ escape (one entry per UTF-16 code unit). Unlike shared strings,
    // ClosedXML does NOT decode that cached <v> on read, so MapFormulaValue must reverse it. These cases
    // are fed exactly as ClosedXML returns them after a full xlsx rebuild.
    [Theory]
    [InlineData("_xD83C__xDF89_Another Thing", "🎉Another Thing")]        // leading astral emoji (surrogate pair)
    [InlineData("_x005F_x0041_", "_x0041_")]                              // genuine literal "_x0041_" must survive, not become "A"
    [InlineData("a_x005F_x0041_b_xD83C__xDF89_c", "a_x0041_b🎉c")]        // mixed: literal escape run + emoji + ASCII
    [InlineData("_xD83C__xDF89__xD83D__xDE80_", "🎉🚀")]                   // two adjacent astral characters
    [InlineData("Plain Text", "Plain Text")]                              // nothing to decode
    public void MapFormulaValue_DecodesEscapedCodeUnitsInCachedFormulaValue(string cachedV, string expected)
    {
        using var workbook = LoadWorkbookWithStringFormulaCachedValue(cachedV);
        var cell = workbook.Worksheet("S").Cell("A1");

        // Sanity: ClosedXML really does surface the cached value escaped (this is the defect we fix).
        cell.HasFormula.Should().BeTrue();

        var mapped = XlsxClosedXmlCellMapper.MapFormulaValue(cell);

        mapped.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("🎉Another Thing")]   // emoji in a plain (shared-string) cell
    [InlineData("_x0041_")]           // literal "_x0041_" — ClosedXML's shared-string read already decodes it
    [InlineData("a_x0041_b🎉c")]      // mixed
    public void MapValue_LeavesAlreadyDecodedSharedStringTextUntouched(string original)
    {
        // A plain text cell round-trips through ClosedXML's shared-string table, which ClosedXML itself
        // escapes on save and decodes on read. MapValue must NOT decode a second time, or genuine
        // "_xHHHH_" text would be corrupted (e.g. "_x0041_" -> "A").
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Sheet1").Cell("A1").SetValue(original);
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var mapped = XlsxClosedXmlCellMapper.MapValue(reloaded.Worksheet("Sheet1").Cell("A1"));

        mapped.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be(original);
    }

    // Builds a minimal package with a single t="str" formula cell whose cached <v> holds the raw escaped
    // text, mirroring how ClosedXML serialises an astral-plane string result during a full rebuild.
    private static XLWorkbook LoadWorkbookWithStringFormulaCachedValue(string cachedValueXmlText)
    {
        var bytes = BuildPackage(cachedValueXmlText);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    private static byte[] BuildPackage(string cachedValueXmlText)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
            }

            Add("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
            Add("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Add("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");
            Add("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">" +
                $"<c r=\"A1\" t=\"str\"><f>\"x\"</f><v>{cachedValueXmlText}</v></c>" +
                "</row></sheetData></worksheet>");
        }

        return stream.ToArray();
    }
}
