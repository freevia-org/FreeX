using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;        // StreamWriter / Encoding.UTF8 in WriteEntry helper
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Guards the two rich-text-run write bugs fixed in the EE1/EE6 review pass:
/// <list type="bullet">
///   <item>
///     EE1 — <c>rPr</c> children must appear in CT_RPrElt order
///     (<c>rFont, b, i, strike, color, sz, u, vertAlign</c>);
///     wrong order caused Excel "we found a problem … repair" and dropped formatting.
///   </item>
///   <item>
///     EE6 — theme/indexed run colors must round-trip as the original OOXML reference kind
///     (<c>&lt;color theme="N" tint="T"/&gt;</c> / <c>&lt;color indexed="N"/&gt;</c>)
///     rather than being flattened to an RGB hex value.
///   </item>
/// </list>
/// </summary>
public sealed class XlsxRichRunSchemaOrderTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Ns = WorkbookNs;

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

    /// <summary>
    /// Wraps an already-built <c>&lt;is&gt;</c> element into a minimal XLSX stream with
    /// A1 = <c>t="inlineStr"</c>, suitable for OpenXmlValidator and FreeX load round-trips.
    /// The <see cref="XNamespace"/> of the <paramref name="isElement"/> must match the
    /// spreadsheetml namespace; all descendant elements inherit it automatically.
    /// </summary>
    private static MemoryStream WrapIsElementInXlsx(XElement isElement, XNamespace ns)
    {
        // Rebuild a deep copy of isElement with the default namespace stripped off attributes
        // (it's already on the element name) then serialise only the row content.
        // BuildMinimalXlsx wraps the row in <sheetData> for us.
        var cell = new XElement(ns + "c",
            new XAttribute("r", "A1"),
            new XAttribute("t", "inlineStr"),
            new XElement(isElement)); // deep copy
        var row = new XElement(ns + "row", new XAttribute("r", "1"), cell);
        // Serialise without the xml-declaration; BuildMinimalXlsx provides its own wrapper.
        var rowXml = row.ToString(SaveOptions.DisableFormatting);
        return BuildMinimalXlsx(rowXml);
    }

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet
    /// with the given sheetData inner XML.  Mirrors the helper in XlsxRichTextRunRoundTripTests.
    /// </summary>
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

    /// <summary>
    /// Validates the XLSX stream with the OpenXml SDK schema validator and returns
    /// only schema-error messages (description @ XPath).
    /// </summary>
    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        using var document = SpreadsheetDocument.Open(copy, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(e => e.ErrorType == ValidationErrorType.Schema)
            .Select(e => $"{e.Description} @ {e.Path?.XPath}")
            .ToList();
    }

    // ── EE1: rPr child ORDER ─────────────────────────────────────────────────

    /// <summary>
    /// EE1: A run with FontName, Bold, Underline, VertAlign=Superscript, FontSize, and FontColor
    /// must emit rPr children in CT_RPrElt order:
    ///   rFont → b → color → sz → u → vertAlign
    /// Previously the order was b → i → strike → u → vertAlign → sz → color → rFont (wrong).
    ///
    /// Tests <see cref="XlsxRichRunWriter.CreateRichInlineStringElement"/> directly so that
    /// the assertion does not depend on patch-save pipeline eligibility.
    /// </summary>
    [Fact]
    public void EE1_RichRun_WithFontNameUnderlineSuperscript_EmitsRprChildrenInCtRprEltOrder()
    {
        XNamespace ns = WorkbookNs;
        var runs = new List<CellTextRun>
        {
            new CellTextRun(
                "Hello",
                Bold:          true,
                Italic:        null,
                Underline:     true,
                Strikethrough: null,
                FontName:      "Arial",
                FontSize:      8.0,
                FontColor:     CellRunColor.FromRgb(new CellColor(0xFF, 0x00, 0x00)),
                VertAlign:     CellTextRunVertAlign.Superscript),
        };

        // Call the writer directly — no need to go through patch-save.
        var isElement = XlsxRichRunWriter.CreateRichInlineStringElement(ns, runs);

        var rPrElements = isElement.Descendants(ns + "rPr").ToList();
        rPrElements.Should().HaveCount(1, "one run with formatting");

        var rPr = rPrElements[0];
        var childNames = rPr.Elements().Select(e => e.Name.LocalName).ToList();

        // CT_RPrElt order subset: rFont → b → color → sz → u → vertAlign.
        childNames.Should().Contain("rFont");
        childNames.Should().Contain("b");
        childNames.Should().Contain("color");
        childNames.Should().Contain("sz");
        childNames.Should().Contain("u");
        childNames.Should().Contain("vertAlign");

        var idxRFont    = childNames.IndexOf("rFont");
        var idxB        = childNames.IndexOf("b");
        var idxColor    = childNames.IndexOf("color");
        var idxSz       = childNames.IndexOf("sz");
        var idxU        = childNames.IndexOf("u");
        var idxVertAlign = childNames.IndexOf("vertAlign");

        idxRFont.Should().BeLessThan(idxB,        "rFont must precede b in CT_RPrElt");
        idxB.Should().BeLessThan(idxColor,        "b must precede color in CT_RPrElt");
        idxColor.Should().BeLessThan(idxSz,       "color must precede sz in CT_RPrElt");
        idxSz.Should().BeLessThan(idxU,           "sz must precede u in CT_RPrElt");
        idxU.Should().BeLessThan(idxVertAlign,    "u must precede vertAlign in CT_RPrElt");

        // Also validate a synthetic XLSX containing this <is> so the OpenXmlValidator
        // confirms the schema is satisfied.
        using var xlsx = WrapIsElementInXlsx(isElement, ns);
        SchemaErrors(xlsx).Should().BeEmpty("rPr children in CT_RPrElt order must not produce schema violations");
    }

    /// <summary>
    /// EE1 round-trip: a run with FontName, Bold, Underline, FontSize, FontColor, VertAlign
    /// must survive write (via <see cref="XlsxRichRunWriter.CreateRichInlineStringElement"/>)
    /// → read (via <see cref="XlsxRichRunReader"/>) with all properties preserved.
    /// </summary>
    [Fact]
    public void EE1_RichRun_WithFontNameUnderlineSuperscript_RoundTripsAllFormatting()
    {
        XNamespace ns = WorkbookNs;
        var original = new List<CellTextRun>
        {
            new CellTextRun(
                "Hi",
                Bold:          true,
                Italic:        null,
                Underline:     true,
                Strikethrough: null,
                FontName:      "Arial",
                FontSize:      8.0,
                FontColor:     CellRunColor.FromRgb(new CellColor(0x12, 0x34, 0x56)),
                VertAlign:     CellTextRunVertAlign.Superscript),
        };

        // Write the <is> element using the fixed writer.
        var isElement = XlsxRichRunWriter.CreateRichInlineStringElement(ns, original);

        // Wrap it in a minimal XLSX so the reader can round-trip it.
        using var xlsx = WrapIsElementInXlsx(isElement, ns);

        // Load and extract.
        var workbook = LoadXlsx(xlsx);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = new CellAddress(sheet.Id, 1, 1);

        sheet.RichTextRuns.Should().ContainKey(addr);
        var runs = sheet.RichTextRuns[addr];
        runs.Should().HaveCount(1);

        var r = runs[0];
        r.FontName.Should().Be("Arial");
        r.Bold.Should().BeTrue();
        r.Underline.Should().BeTrue();
        r.FontSize.Should().BeApproximately(8.0, 0.001);
        r.FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(0x12, 0x34, 0x56)));
        r.VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
    }

    // ── EE6: theme/indexed color ROUND-TRIP ──────────────────────────────────

    /// <summary>
    /// EE6: A run color stored as a theme reference (<c>&lt;color theme="4" tint="0.4"/&gt;</c>)
    /// must survive a load→save→reload cycle as a theme reference — NOT as a flattened RGB hex.
    /// </summary>
    [Fact]
    public void EE6_ThemeColorRun_RoundTripsAsThemeReference_NotRgb()
    {
        // Build XLSX with a hand-crafted <color theme="4" tint="0.4"/> in rPr.
        var sheetXml = """
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><color theme="4" tint="0.4"/></rPr><t>Themed</t></r>
                  <r><t> plain</t></r>
                </is>
              </c>
            </row>
            """;

        using var inputPkg = BuildMinimalXlsx(sheetXml);

        // Load the workbook.
        var workbook = LoadXlsx(inputPkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = new CellAddress(sheet.Id, 1, 1);

        sheet.RichTextRuns.Should().ContainKey(addr, "the themed run should be loaded");
        var loaded = sheet.RichTextRuns[addr];
        loaded[0].FontColor.Should().NotBeNull();
        var loadedColor = loaded[0].FontColor!.Value;
        loadedColor.Kind.Should().Be(CellRunColorKind.Theme,
            "theme color must be stored as Theme kind, not flattened to Rgb");
        loadedColor.ThemeIndex.Should().Be(4);
        loadedColor.Tint.Should().BeApproximately(0.4, 0.0001);

        // Modify an unrelated cell so patch-save writes the worksheet.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        // Save and inspect the XML — the color element must be <color theme="4" tint="0.4"/>.
        using var saved = SaveXlsx(workbook);

        saved.Position = 0;
        using var archive   = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        using var xmlStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var savedDoc = XDocument.Load(xmlStream);

        var colorElements = savedDoc.Root!
            .Descendants(Ns + "color")
            .Where(e => e.Parent?.Name.LocalName == "rPr")
            .ToList();

        colorElements.Should().HaveCount(1, "one run with a color");
        var colorEl = colorElements[0];
        colorEl.Attribute("theme")!.Value.Should().Be("4",
            "theme index must be preserved, not replaced by rgb");
        colorEl.Attribute("rgb").Should().BeNull(
            "a theme color must NOT be written as rgb");
        colorEl.Attribute("tint")?.Value.Should().NotBeNullOrWhiteSpace(
            "tint must be preserved");

        // OpenXmlValidator must be happy with the saved file.
        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty("theme color round-trip must produce valid OOXML");
    }

    /// <summary>
    /// EE6: An indexed-color run (<c>&lt;color indexed="N"/&gt;</c>) must survive as indexed.
    /// </summary>
    [Fact]
    public void EE6_IndexedColorRun_RoundTripsAsIndexedReference()
    {
        var sheetXml = """
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><color indexed="3"/></rPr><t>Indexed</t></r>
                  <r><t> plain</t></r>
                </is>
              </c>
            </row>
            """;

        using var inputPkg = BuildMinimalXlsx(sheetXml);
        var workbook = LoadXlsx(inputPkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = new CellAddress(sheet.Id, 1, 1);

        var loaded = sheet.RichTextRuns[addr];
        var indexedColor = loaded[0].FontColor!.Value;
        indexedColor.Kind.Should().Be(CellRunColorKind.Indexed,
            "indexed color must be stored as Indexed kind");
        indexedColor.IndexedIndex.Should().Be(3);

        // Touch another cell so the sheet gets re-written.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(0));

        using var saved = SaveXlsx(workbook);

        saved.Position = 0;
        using var archive   = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        using var xmlStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var savedDoc = XDocument.Load(xmlStream);

        var colorEl = savedDoc.Root!
            .Descendants(Ns + "color")
            .First(e => e.Parent?.Name.LocalName == "rPr");

        colorEl.Attribute("indexed")!.Value.Should().Be("3",
            "indexed color reference must be preserved");
        colorEl.Attribute("rgb").Should().BeNull("indexed color must NOT be flattened to rgb");
    }

    /// <summary>
    /// EE6: An explicit RGB run color must still round-trip as RGB (regression guard).
    /// </summary>
    [Fact]
    public void EE6_RgbColorRun_StaysRgbOnRoundTrip()
    {
        var sheetXml = """
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><color rgb="FFFF0000"/></rPr><t>Red</t></r>
                  <r><t> plain</t></r>
                </is>
              </c>
            </row>
            """;

        using var inputPkg = BuildMinimalXlsx(sheetXml);
        var workbook = LoadXlsx(inputPkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = new CellAddress(sheet.Id, 1, 1);

        var loaded = sheet.RichTextRuns[addr];
        var rgbColor = loaded[0].FontColor!.Value;
        rgbColor.Kind.Should().Be(CellRunColorKind.Rgb);
        rgbColor.Rgb.Should().Be(new CellColor(255, 0, 0));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(0));

        using var saved = SaveXlsx(workbook);

        saved.Position = 0;
        using var archive   = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        using var xmlStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var savedDoc = XDocument.Load(xmlStream);

        var colorEl = savedDoc.Root!
            .Descendants(Ns + "color")
            .First(e => e.Parent?.Name.LocalName == "rPr");

        colorEl.Attribute("rgb")!.Value.Should().StartWith("FF", "RGB color must be preserved with FF alpha prefix");
        colorEl.Attribute("theme").Should().BeNull("rgb color must not gain a theme attribute");
    }

}
