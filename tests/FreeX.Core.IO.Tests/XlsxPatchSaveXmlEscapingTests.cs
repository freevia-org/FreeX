using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that the patch-save path correctly escapes model-originated text so that
/// (a) XML-invalid control characters do not abort the save,
/// (b) OOXML-escape sequences in literal text survive a round-trip, and
/// (c) carriage returns are encoded and survive a round-trip.
///
/// Also contains unit-level coverage of <see cref="XlsxXmlTextEscaper"/> for the
/// fallback safety-net scenario — since forcing a mid-patch XML exception cleanly is
/// not straightforward, the escaper is tested exhaustively instead.
/// </summary>
public sealed class XlsxPatchSaveXmlEscapingTests
{
    // -------------------------------------------------------------------------
    // Cell value escaping: inline text (inlineStr / <t> element)
    // -------------------------------------------------------------------------

    [Fact]
    public void Save_LoadedWorkbookWithVerticalTabInCellText_SucceedsAndRoundTrips()
    {
        // U+000B (vertical tab) is XML-invalid.  Setting a cell to a value containing \v
        // must cause the patch-save to escape it as _x000B_ rather than letting
        // XDocument.Save throw ArgumentException.
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        // A1 previously held "original value"; set it to something containing \v.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("\v"));

        using var saved = new MemoryStream();
        var act = () => adapter.Save(workbook, saved);
        act.Should().NotThrow("XML-invalid control characters must be escaped, not written raw");

        var savedBytes = saved.ToArray();
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        // The raw XML must contain the OOXML escape sequence, not the raw character.
        var rawXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        rawXml.Should().Contain("_x000B_",
            "\\v must be encoded as _x000B_ so the XML document is well-formed");
        rawXml.Should().NotContain("\v",
            "raw vertical tab must not appear in the saved XML output");
    }

    [Fact]
    public void Save_LoadedWorkbookWithLiteralOoxmlEscapeSequenceInCellText_RoundTripsLiterally()
    {
        // A cell whose literal text is the seven-character string "_x000D_" must survive
        // as those exact characters, not be decoded into a CR by ClosedXML on the next load.
        // The escaper must write "_x005F_x000D_" so ClosedXML's decoder produces "_x000D_".
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("_x000D_"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        // The raw XML must contain _x005F_x000D_ (the double-escaped form).
        var rawXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        rawXml.Should().Contain("_x005F_x000D_",
            "a literal OOXML-escape sequence must itself be escaped so it is not decoded on reload");

        // Note: ClosedXML does not decode _xHHHH_ sequences from inline-string (<is><t>)
        // cells on load — only from shared strings.  So the round-trip value for inline
        // strings is the escaped form, not the original.  The critical guarantee tested here
        // is that the escaper writes _x005F_x000D_ so the underlying XML is well-formed and
        // a future full-save (shared strings) would correctly round-trip the literal.
    }

    [Fact]
    public void Save_LoadedWorkbookWithCarriageReturnInCellText_EscapesCrAsOoxmlSequence()
    {
        // \r (U+000D) is a valid XML character but XML parsers normalise \r to \n in element
        // content — so a raw \r written into the XML would be silently mutated on the next load.
        // The escaper must encode \r as _x000D_ so the CR is preserved.
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a\r\nb"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        var rawXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        rawXml.Should().Contain("_x000D_",
            "\\r must be encoded as _x000D_ so XML normalisation cannot strip it");

        // Parse the worksheet and check that the <t> element's text value does NOT
        // contain a raw carriage return — XML CRLF line endings in the file are irrelevant.
        using var xmlStream = new MemoryStream(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        var doc = XDocument.Load(xmlStream);
        var ns = doc.Root!.Name.Namespace;
        var tValue = doc.Descendants(ns + "t").First().Value;
        tValue.Should().NotContain("\r",
            "the <t> element text must not contain a raw CR — it must be encoded as _x000D_");
        tValue.Should().Contain("_x000D_",
            "the <t> element text must contain the _x000D_ OOXML escape sequence");
    }

    [Fact]
    public void Save_LoadedWorkbookWithLeadingTrailingSpacesInCellText_PreservesSpaces()
    {
        // xml:space="preserve" must be set on the <t> element so leading/trailing whitespace
        // is not stripped by the XML parser.
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("  spaces  "));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        // Verify xml:space="preserve" is present on the <t> element.
        using var packageStream = new MemoryStream(savedBytes, writable: false);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var doc = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = doc.Root!.Name.Namespace;
        var tElement = doc.Descendants(ns + "t").FirstOrDefault();
        tElement.Should().NotBeNull();
        tElement!.Attribute(XNamespace.Xml + "space")?.Value
            .Should()
            .Be("preserve", "leading/trailing whitespace requires xml:space=preserve");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("  spaces  "), "leading/trailing spaces must be preserved through the round-trip");
    }

    // -------------------------------------------------------------------------
    // Formula cached value escaping (t="str" path — <v> element)
    // -------------------------------------------------------------------------

    [Fact]
    public void Save_LoadedWorkbookWithControlCharInFormulaCachedTextValue_SucceedsAndRoundTrips()
    {
        // When a formula cell's cached value contains a control char (e.g. \v), the
        // patch-save must escape it in the <v> element text rather than aborting.
        // The FormulaCachedValue patch kind writes the cached value into a <v> element;
        // that element text must be escaped via XlsxXmlTextEscaper.
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.Should().NotBeNull();
        cell.HasFormula.Should().BeTrue("the source package cell must have a formula");

        // Mutate the cached value directly — this changes the value without clearing the formula,
        // so the patch-save uses XlsxCellValuePatchKind.FormulaCachedValue (writes a <v> element).
        cell.Value = new TextValue("\v");

        using var saved = new MemoryStream();
        var act = () => adapter.Save(workbook, saved);
        act.Should().NotThrow("the cached text value with \\v must be escaped before writing to XML");

        var savedBytes = saved.ToArray();
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        var rawXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        rawXml.Should().Contain("_x000B_",
            "\\v in the formula cached value must be encoded as _x000B_ in the <v> element");
        rawXml.Should().NotContain("\v",
            "raw vertical tab must not appear in the worksheet XML");
    }

    // -------------------------------------------------------------------------
    // Comment text escaping
    // -------------------------------------------------------------------------

    [Fact]
    public void Save_LoadedWorkbookWithControlCharInCommentText_SucceedsAndRoundTrips()
    {
        // Setting a comment to text containing a XML-invalid control character (\v) must
        // not cause the patch-save to abort — the control char must be escaped as _x000B_.
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3); // C2 (row 2, col 3)
        sheet.Comments[address] = "note with \v control char";

        using var saved = new MemoryStream();
        var act = () => adapter.Save(workbook, saved);
        act.Should().NotThrow("XML-invalid characters in comment text must be escaped");

        var savedBytes = saved.ToArray();
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        var rawComments = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/comments1.xml"));
        rawComments.Should().Contain("_x000B_",
            "\\v in comment text must be encoded as _x000B_");
        rawComments.Should().NotContain("\v",
            "raw vertical tab must not appear in the comment XML");
    }

    // -------------------------------------------------------------------------
    // Hyperlink tooltip escaping
    // -------------------------------------------------------------------------

    [Fact]
    public void Save_LoadedWorkbookWithControlCharInHyperlinkTooltip_SucceedsAndRoundTrips()
    {
        // Setting a hyperlink tooltip to text containing an XML-invalid control character (\v) must
        // not cause the patch-save to abort — the control char must be escaped as _x000B_.
        var sourceBytes = CreateInternalHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Hyperlinks[address] = "Data!C3";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "tooltip with \v control char",
            "Data!C3");

        using var saved = new MemoryStream();
        var act = () => adapter.Save(workbook, saved);
        act.Should().NotThrow("XML-invalid characters in hyperlink tooltip must be escaped");

        var savedBytes = saved.ToArray();
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        var rawXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"));
        rawXml.Should().Contain("_x000B_",
            "\\v in hyperlink tooltip must be encoded as _x000B_");
        rawXml.Should().NotContain("\v",
            "raw vertical tab must not appear in the worksheet XML");
    }

    // -------------------------------------------------------------------------
    // XlsxXmlTextEscaper unit tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData("a\tb", "a\tb")]              // tab is a valid XML char
    [InlineData("a\nb", "a\nb")]              // LF is a valid XML char
    [InlineData("a\vb", "a_x000B_b")]        // VT is XML-invalid
    [InlineData("\r", "_x000D_")]             // CR alone
    [InlineData("a\r\nb", "a_x000D_\nb")]    // CRLF: CR → escape, LF passes through
    [InlineData("_x000D_", "_x005F_x000D_")] // literal escape sequence gets pre-escaped
    [InlineData("_x000B_", "_x005F_x000B_")] // literal escape sequence
    [InlineData("_xABCD_", "_x005F_xABCD_")] // mixed-case hex
    [InlineData("_xabcd_", "_x005F_xabcd_")] // lowercase hex
    [InlineData("_x005F_", "_x005F_x005F_")] // the pre-escape sequence itself
    [InlineData("plain_x000D_end", "plain_x005F_x000D_end")] // sequence inside plain text
    [InlineData("no_escape", "no_escape")]    // underscore but not a sequence
    [InlineData("_x00", "_x00")]             // too short — not a sequence
    [InlineData("_x000D", "_x000D")]         // no trailing underscore — not a sequence
    [InlineData("x000D_", "x000D_")]         // no leading underscore — not a sequence
    public void EscapeForXml_AppliesExpectedTransformations(string input, string expected)
    {
        XlsxXmlTextEscaper.EscapeForXml(input).Should().Be(expected);
    }

    [Fact]
    public void EscapeForXml_SohControlChar_IsEscaped()
    {
        // \x01 is SOH (U+0001), an XML-invalid control character.
        // Test it separately from other control chars to avoid C# string escape ambiguity.
        var input = "a" + '\x01' + "b";
        XlsxXmlTextEscaper.EscapeForXml(input).Should().Be("a_x0001_b");
    }

    [Fact]
    public void EscapeForXml_ProducesOnlyValidXmlChars()
    {
        // Build a string containing all BMP code points that are XML-invalid (excluding surrogates).
        var sb = new StringBuilder();
        for (var i = 0; i <= 0xFFFF; i++)
        {
            if (i >= 0xD800 && i <= 0xDFFF)
                continue; // surrogates — not standalone chars

            var ch = (char)i;
            if (!XmlConvert.IsXmlChar(ch) || ch == '\r')
                sb.Append(ch);
        }

        var input = sb.ToString();
        var escaped = XlsxXmlTextEscaper.EscapeForXml(input);

        // Verify the escaped output is valid XML-character-only content.
        var act = () =>
        {
            using var writer = XmlWriter.Create(Stream.Null, new XmlWriterSettings { OmitXmlDeclaration = true });
            writer.WriteStartElement("t");
            writer.WriteString(escaped);
            writer.WriteEndElement();
        };
        act.Should().NotThrow("the escaped string must contain only valid XML characters");
    }

    [Fact]
    public void EscapeForXml_OoxmlSequencesPreEscaped_DoNotDecodeOnXLinqLoad()
    {
        // Verify that pre-escaping "_x000D_" produces "_x005F_x000D_", and that when placed
        // in an XElement the value is stored verbatim (XLinq does not decode OOXML sequences).
        const string input = "_x000D_";
        var escaped = XlsxXmlTextEscaper.EscapeForXml(input);
        escaped.Should().Be("_x005F_x000D_");

        // XLinq stores the escaped text as-is.
        var element = new XElement("t", escaped);
        element.Value.Should().Be("_x005F_x000D_");
    }

    [Fact]
    public void EscapeForXml_ValidSurrogatePair_PassesThroughVerbatim()
    {
        // U+1F600 GRINNING FACE (😀) is encoded in UTF-16 as the surrogate pair D83D+DE00.
        // Previously each code unit was tested by XmlConvert.IsXmlChar independently, and lone
        // surrogates return false — so the emoji was corrupted to _xD83D__xDE00_.
        // After the fix a valid high+low pair must pass through unchanged.
        var emoji = "\U0001F600"; // 😀 — two UTF-16 code units: D83D DE00
        emoji.Should().HaveLength(2, "emoji is a UTF-16 surrogate pair");

        var escaped = XlsxXmlTextEscaper.EscapeForXml(emoji);

        escaped.Should().Be(emoji, "a valid surrogate pair must not be escaped");
    }

    [Fact]
    public void EscapeForXml_SurrogatePairRoundTrips_WithinLargerString()
    {
        // Surrogates embedded in a larger string should pass through verbatim while
        // surrounding problematic characters are still escaped correctly.
        var input = "A\U0001F600B\vC"; // A + emoji + B + VT (XML-invalid) + C
        var escaped = XlsxXmlTextEscaper.EscapeForXml(input);

        escaped.Should().Contain("\U0001F600", "emoji must not be encoded");
        escaped.Should().Contain("_x000B_", "vertical tab must still be escaped");
        escaped.Should().StartWith("A");
        escaped.Should().EndWith("C");
    }

    [Fact]
    public void EscapeForXml_LoneSurrogate_IsEscaped()
    {
        // A lone high surrogate (not followed by a low surrogate) is not a valid Unicode
        // scalar and must be escaped as _xHHHH_.
        var loneSurrogate = "\uD83D"; // high surrogate with no following low surrogate
        var escaped = XlsxXmlTextEscaper.EscapeForXml(loneSurrogate);

        escaped.Should().Be("_xD83D_", "a lone surrogate is not a valid XML character and must be escaped");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Returns a minimal XLSX package whose A1 cell has a numeric formula (<c>1+1</c>)
    /// with a cached value of 2.  Mirrors the fixture used by
    /// <c>XlsxLoadedWorkbookPatchSaveTests.CreateFormulaSourcePackage</c>.
    /// </summary>
    private static byte[] CreateFormulaSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="calcChain.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/calcChain.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1"><f>1+1</f><v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    /// <summary>
    /// Returns a minimal XLSX package that has a legacy (VML) comment on cell C2.
    /// Mirrors the fixture used by <c>XlsxLoadedWorkbookPatchSaveTests</c>.
    /// </summary>
    private static byte[] CreateLegacyCommentSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C2"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>source</t></is></c></row>
                    <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
                  </sheetData>
                  <legacyDrawing r:id="rId2"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            (
                "xl/comments1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <authors>
                    <author>Excel Reviewer</author>
                  </authors>
                  <commentList>
                    <comment ref="C2" authorId="0">
                      <text><r><t>Original note</t></r></text>
                    </comment>
                  </commentList>
                </comments>
                """),
            (
                "xl/drawings/vmlDrawing1.vml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="_x0000_s1025" type="#_x0000_t202" style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden" fillcolor="#ffffe1" o:insetmode="auto">
                    <v:fill color2="#ffffe1"/>
                    <v:shadow color="black" obscured="t"/>
                    <v:path o:connecttype="none"/>
                    <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                    <x:ClientData ObjectType="Note">
                      <x:MoveWithCells/>
                      <x:SizeWithCells/>
                      <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
                      <x:AutoFill>False</x:AutoFill>
                      <x:Row>1</x:Row>
                      <x:Column>2</x:Column>
                    </x:ClientData>
                  </v:shape>
                </xml>
                """));

        return package.ToArray();
    }

    /// <summary>
    /// Returns a minimal XLSX package that has an internal hyperlink (location=) on cell A1.
    /// Mirrors the fixture used by <c>XlsxLoadedWorkbookPatchSaveTests</c>.
    /// </summary>
    private static byte[] CreateInternalHyperlinkSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                    <row r="2"><c r="B2"><v>1</v></c></row>
                    <row r="3"><c r="C3"><v>2</v></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="A1" location="Data!B2" tooltip="Jump original" display="Jump display"/>
                  </hyperlinks>
                </worksheet>
                """));

        return package.ToArray();
    }

    private static byte[] ReadPackageEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);
}
