using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-56 targeted regression tests for the "io-a" bucket findings:
/// <list type="bullet">
///   <item>R56-default-arm-masks-a-case-sweep-1: ODS <c>ReadCellValue</c>'s default arm must fall back
///   to the cell's <c>&lt;text:p&gt;</c> content for a missing/unrecognized <c>office:value-type</c>
///   instead of silently discarding visible text.</item>
///   <item>R56-io-styles-xf-indexing-5-1: <see cref="XlsxNumberFormatCatalogWriter"/> must not emit a
///   duplicate &lt;numFmt&gt; for a format code that already exists in the rebuilt styles.xml under a
///   different id when the catalog's own id slot is free.</item>
///   <item>R56-io-shared-strings-richtext-5-1: <see cref="XlsxSharedStringMetadataPreserver"/> must let
///   a rich/phonetic shared-string entry whose concatenated text is empty participate in patch-back
///   matching instead of being silently excluded.</item>
/// </list>
/// </summary>
public sealed class Round56_IoABucketTests
{
    // ---------------------------------------------------------------------------------------
    // R56-default-arm-masks-a-case-sweep-1
    // ---------------------------------------------------------------------------------------

    private static MemoryStream BuildOdsPackage(string contentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(contentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private const string OdsContentXmlHeader =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<office:document-content " +
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "office:version=\"1.2\">" +
        "<office:body><office:spreadsheet>";

    private const string OdsContentXmlFooter =
        "</office:spreadsheet></office:body></office:document-content>";

    [Fact]
    public void Ods_CellWithNoValueTypeButVisibleText_ImportsAsText()
    {
        // A hand-authored (non-FreeX) producer that omits office:value-type on a plain string cell,
        // per the ODF spec's optional-attribute rule, but still carries a visible <text:p> run.
        var contentXml = OdsContentXmlHeader +
            "<table:table table:name=\"Sheet1\">" +
            "<table:table-row>" +
            "<table:table-cell><text:p>Some Label</text:p></table:table-cell>" +
            "</table:table-row>" +
            "</table:table>" +
            OdsContentXmlFooter;

        using var stream = BuildOdsPackage(contentXml);
        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Some Label"));
    }

    [Fact]
    public void Ods_CellWithNoValueTypeAndNoText_ImportsAsBlank()
    {
        // Sibling no-regression case: a genuinely empty cell (no value-type, no text) must still
        // import as blank, not as an empty-string TextValue.
        var contentXml = OdsContentXmlHeader +
            "<table:table table:name=\"Sheet1\">" +
            "<table:table-row>" +
            "<table:table-cell/>" +
            "</table:table-row>" +
            "</table:table>" +
            OdsContentXmlFooter;

        using var stream = BuildOdsPackage(contentXml);
        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.CellCount.Should().Be(0);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(BlankValue.Instance);
    }

    // ---------------------------------------------------------------------------------------
    // R56-io-styles-xf-indexing-5-1
    // ---------------------------------------------------------------------------------------

    private static MemoryStream BuildMinimalXlsxWithStyles(string numFmtsInnerXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void WriteEntry(string name, string xml)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            WriteEntry(
                "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            WriteEntry(
                "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheets/></workbook>");

            WriteEntry(
                "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                numFmtsInnerXml +
                "<fonts count=\"1\"><font/></fonts>" +
                "<fills count=\"1\"><fill/></fills>" +
                "<borders count=\"1\"><border/></borders>" +
                "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf/></cellXfs>" +
                "</styleSheet>");
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void NumberFormatCatalogWriter_FreeIdWithEquivalentFormatCodeElsewhere_RemapsInsteadOfDuplicating()
    {
        // Simulates: the catalog wants id 170/"0.0000%" written back, but the rebuilt styles.xml
        // (as ClosedXML would leave it) already carries the byte-identical formatCode under a
        // different, currently-occupied id (164) -- id 170 itself is free.
        using var stream = BuildMinimalXlsxWithStyles(
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.0000%\"/></numFmts>");

        var workbook = new Workbook("Untitled");
        workbook.NumberFormatCatalog[170] = "0.0000%";
        // R69-io-numfmt-styles-6-1: BuildNumberFormatCatalog now prunes catalog entries that no
        // live cell/style-only/dxf style references, so a live cell using this exact format code is
        // required for the id-170 catalog entry to survive into the remap logic under test here.
        var sheet = workbook.AddSheet("Sheet1");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0000%" });
        var cell = Cell.FromValue(new NumberValue(0.1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var remap = XlsxNumberFormatCatalogWriter.Save(stream, workbook);

        remap.Should().ContainKey(170);
        remap[170].Should().Be(164, "an equivalent formatCode already exists under id 164, so 170 should remap to it instead of duplicating");

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/styles.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var numFmtElements = stylesXml.Root!.Element(ns + "numFmts")!.Elements(ns + "numFmt").ToList();

        numFmtElements.Should().ContainSingle(e => e.Attribute("formatCode")!.Value == "0.0000%",
            "the writer must not add a second <numFmt> with the same formatCode under the free id 170");
    }

    [Fact]
    public void NumberFormatCatalogWriter_FreeIdWithNoEquivalentElsewhere_AddsNewEntry()
    {
        // Sibling no-regression case: when the free catalog id's formatCode has no equivalent
        // already present, the writer must still add it (not silently drop it).
        using var stream = BuildMinimalXlsxWithStyles(
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.00\"/></numFmts>");

        var workbook = new Workbook("Untitled");
        workbook.NumberFormatCatalog[171] = "0.0000%";
        // R69-io-numfmt-styles-6-1: a live cell using this exact format code is required for the
        // id-171 catalog entry to survive the liveness prune (see sibling test above).
        var sheet = workbook.AddSheet("Sheet1");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0000%" });
        var cell = Cell.FromValue(new NumberValue(0.1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var remap = XlsxNumberFormatCatalogWriter.Save(stream, workbook);

        remap.Should().ContainKey(171);
        remap[171].Should().Be(171);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/styles.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var numFmtElements = stylesXml.Root!.Element(ns + "numFmts")!.Elements(ns + "numFmt").ToList();

        numFmtElements.Should().Contain(e =>
            e.Attribute("numFmtId")!.Value == "171" && e.Attribute("formatCode")!.Value == "0.0000%");
    }

    [Fact]
    public void NumberFormatCatalogWriter_DuplicateAndMalformedEntries_PreserveFirstMatchSemantics()
    {
        using var stream = BuildMinimalXlsxWithStyles(
            "<numFmts count=\"5\">" +
            "<numFmt numFmtId=\"not-an-id\" formatCode=\"target\"/>" +
            "<numFmt numFmtId=\"165\" formatCode=\"target\"/>" +
            "<numFmt numFmtId=\"166\" formatCode=\"target\"/>" +
            "<numFmt numFmtId=\"170\" formatCode=\"wrong\"/>" +
            "<numFmt numFmtId=\"170\" formatCode=\"target\"/>" +
            "</numFmts>");

        var workbook = new Workbook("Untitled");
        workbook.NumberFormatCatalog[170] = "target";
        var sheet = workbook.AddSheet("Sheet1");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "target" });
        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var remap = XlsxNumberFormatCatalogWriter.Save(stream, workbook);

        remap[170].Should().Be(165,
            "the first occurrence of an id and the first valid custom-code match must keep winning");

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/styles.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var numFmts = stylesXml.Root!.Element(ns + "numFmts")!;
        numFmts.Attribute("count")!.Value.Should().Be("5");
        numFmts.Elements(ns + "numFmt").Should().HaveCount(5,
            "malformed and duplicate source entries remain part of the preserved XML catalog");
    }

    [Fact]
    public void NumberFormatCatalogWriter_AppendedCode_IsReusedByLaterPivotCollision()
    {
        using var stream = BuildMinimalXlsxWithStyles("<numFmts count=\"0\"/>");
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        var pivot = new PivotTableModel { Name = "PivotTable1" };
        pivot.DataFields.Add(new PivotDataFieldModel(
            0, "Primary", "sum", NumberFormatId: 164, NumberFormatCode: "primary"));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1, "Colliding", "sum", NumberFormatId: 164, NumberFormatCode: "secondary"));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2, "Independent", "sum", NumberFormatId: 170, NumberFormatCode: "secondary"));
        sheet.PivotTables.Add(pivot);

        var remap = XlsxNumberFormatCatalogWriter.Save(stream, workbook);

        remap.ResolveDataFieldNumberFormatId(164, "secondary").Should().Be(170,
            "the main catalog appended code must be visible to the later pivot-collision lookup");

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var stylesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/styles.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        stylesXml.Root!.Element(ns + "numFmts")!.Elements(ns + "numFmt")
            .Should().ContainSingle(element => element.Attribute("formatCode")!.Value == "secondary");
    }

    // ---------------------------------------------------------------------------------------
    // R56-io-shared-strings-richtext-5-1
    // ---------------------------------------------------------------------------------------

    private static MemoryStream BuildXlsxPackageWithSharedStrings(string sharedStringsInnerXml, string sheet1Xml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void WriteEntry(string name, string xml)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            WriteEntry(
                "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "count=\"1\" uniqueCount=\"1\">" +
                sharedStringsInnerXml +
                "</sst>");

            WriteEntry(
                "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                sheet1Xml +
                "</worksheet>");
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void SharedStringMetadataPreserver_EmptyTextRichEntry_IsPatchedBackNotDropped()
    {
        // A rich <si> whose only run has empty text but carries bold+color formatting -- the
        // "concatenated plain text is empty" case that must still participate in the match.
        const string richSi =
            "<si><r><rPr><b/><color rgb=\"FFFF0000\"/></rPr><t></t></r></si>";
        const string sheetXml =
            "<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData>";

        using var sourceArchiveStream = BuildXlsxPackageWithSharedStrings(richSi, sheetXml);
        using var sourceArchive = new ZipArchive(sourceArchiveStream, ZipArchiveMode.Read);

        // The "target" package simulates ClosedXML's full-rebuild: same cell, same empty text,
        // but the rich run metadata has been dropped down to plain text.
        const string plainSi = "<si><t></t></si>";
        using var targetArchiveStream = BuildXlsxPackageWithSharedStrings(plainSi, sheetXml);
        using (var targetArchive = new ZipArchive(targetArchiveStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetArchiveStream.Position = 0;
        using var verifyArchive = new ZipArchive(targetArchiveStream, ZipArchiveMode.Read);
        var targetSharedStringsXml = XlsxPackageXmlEditor.LoadXml(verifyArchive.GetEntry("xl/sharedStrings.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var si = targetSharedStringsXml.Root!.Elements(ns + "si").Single();

        si.Elements(ns + "r").Should().NotBeEmpty("the rich run must be grafted back onto the target entry, not dropped");
        si.Descendants(ns + "color").Should().ContainSingle(e => e.Attribute("rgb")!.Value == "FFFF0000");
    }

    [Fact]
    public void SharedStringMetadataPreserver_NonEmptyRichText_StillPatchedBack()
    {
        // Sibling no-regression case: the existing, already-covered non-empty rich-text path must
        // keep working unaffected by the empty-text change.
        const string richSi =
            "<si><r><rPr><b/></rPr><t>Hello</t></r></si>";
        const string sheetXml =
            "<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData>";

        using var sourceArchiveStream = BuildXlsxPackageWithSharedStrings(richSi, sheetXml);
        using var sourceArchive = new ZipArchive(sourceArchiveStream, ZipArchiveMode.Read);

        const string plainSi = "<si><t>Hello</t></si>";
        using var targetArchiveStream = BuildXlsxPackageWithSharedStrings(plainSi, sheetXml);
        using (var targetArchive = new ZipArchive(targetArchiveStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetArchiveStream.Position = 0;
        using var verifyArchive = new ZipArchive(targetArchiveStream, ZipArchiveMode.Read);
        var targetSharedStringsXml = XlsxPackageXmlEditor.LoadXml(verifyArchive.GetEntry("xl/sharedStrings.xml")!);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var si = targetSharedStringsXml.Root!.Elements(ns + "si").Single();

        si.Elements(ns + "r").Should().NotBeEmpty();
        si.Descendants(ns + "b").Should().NotBeEmpty();
    }
}
