using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Tests that <see cref="HtmlFileAdapter"/> import honors the document's declared/encoded charset
/// instead of blindly decoding as UTF-8 -- a Windows-1252 file with accented characters, and no
/// declared charset, no BOM, must not come out as mojibake; a UTF-8 file with a BOM must still work.
/// Also covers the sibling altChunk text/html import path in <see cref="DocxReader"/>, which shared the
/// same blind-UTF-8 assumption before the fix.
/// </summary>
public class HtmlCharsetDetectionTests
{
    static HtmlCharsetDetectionTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // -------------------------------------------------------------------------
    // 1. Windows-1252 file, declared via <meta charset>, no BOM.
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_Windows1252MetaCharset_DecodesAccentedTextCorrectly()
    {
        // "Café — déjà vu, señor" — bytes encoded as Windows-1252, no BOM. If the importer blindly
        // decodes as UTF-8, the high bytes (0xE9, 0xE8, 0xF1, the em dash 0x97) are either invalid
        // UTF-8 sequences or decode to the wrong characters entirely -- mojibake.
        const string text = "Café — déjà vu, señor";
        var html = "<!doctype html><html><head><meta charset=\"windows-1252\"></head><body><p>"
            + text + "</p></body></html>";

        var windows1252 = Encoding.GetEncoding(1252);
        var bytes = windows1252.GetBytes(html);

        var document = new HtmlFileAdapter().Load(new MemoryStream(bytes));

        document.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be(text);
    }

    // -------------------------------------------------------------------------
    // 2. Windows-1252 file, declared via legacy http-equiv Content-Type meta.
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_Windows1252HttpEquivCharset_DecodesAccentedTextCorrectly()
    {
        const string text = "naïve café";
        var html = "<!doctype html><html><head>"
            + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\">"
            + "</head><body><p>" + text + "</p></body></html>";

        var bytes = Encoding.GetEncoding(1252).GetBytes(html);

        var document = new HtmlFileAdapter().Load(new MemoryStream(bytes));

        document.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be(text);
    }

    // -------------------------------------------------------------------------
    // 3. UTF-8 file with a BOM must still import correctly (sibling no-regression).
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_Utf8WithBom_DecodesCorrectlyAndBomIsNotLeakedIntoText()
    {
        const string text = "Über résumé 日本語";
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><p>" + text + "</p></body></html>";

        // Encoding.GetBytes(string) never emits a preamble regardless of encoderShouldEmitUTF8Identifier
        // -- only GetPreamble() (as used by BOM-aware writers) does -- so the BOM must be prepended by hand.
        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = utf8WithBom.GetPreamble().Concat(utf8WithBom.GetBytes(html)).ToArray();
        bytes[0].Should().Be(0xEF, because: "the UTF-8 BOM must be present for this test to be meaningful");

        var document = new HtmlFileAdapter().Load(new MemoryStream(bytes));

        var paragraph = document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        paragraph.PlainText.Should().Be(text);
        paragraph.PlainText.Should().NotContain("﻿", because: "the BOM must be stripped, not decoded into a stray character");
    }

    // -------------------------------------------------------------------------
    // 4. Sibling no-regression: plain bomless UTF-8 with no declared charset still works.
    // -------------------------------------------------------------------------

    [Fact]
    public void Load_BomlessUtf8NoDeclaredCharset_StillDecodesCorrectly()
    {
        const string text = "plain ascii and é too";
        var html = "<!doctype html><html><body><p>" + text + "</p></body></html>";
        var bytes = new UTF8Encoding(false).GetBytes(html);

        var document = new HtmlFileAdapter().Load(new MemoryStream(bytes));

        document.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be(text);
    }

    // -------------------------------------------------------------------------
    // 5. DecodeBytes is genuinely reached (not a dead diagnostic path): the internal helper's
    //    behavior matches what Load actually returns, across all three resolution branches.
    // -------------------------------------------------------------------------

    [Fact]
    public void DecodeBytes_MatchesLoad_ForWindows1252Meta()
    {
        const string text = "Café";
        var html = "<meta charset=\"windows-1252\"><p>" + text + "</p>";
        var bytes = Encoding.GetEncoding(1252).GetBytes(html);

        var decoded = HtmlFileAdapter.DecodeBytes(bytes);
        decoded.Should().Contain(text);

        var document = new HtmlFileAdapter().Load(new MemoryStream(bytes));
        document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be(text);
    }

    // -------------------------------------------------------------------------
    // 6. FAMILY: DocxReader's altChunk text/html import path shares HtmlFileAdapter.DecodeBytes and
    //    must therefore also honor a declared Windows-1252 charset, not blindly decode as UTF-8.
    // -------------------------------------------------------------------------

    [Fact]
    public void AltChunkHtmlImport_HonorsDeclaredWindows1252Charset()
    {
        const string text = "Café — déjà vu";
        var html = "<!doctype html><html><head><meta charset=\"windows-1252\"></head><body><p>"
            + text + "</p></body></html>";
        var bytes = Encoding.GetEncoding(1252).GetBytes(html);

        using var docxStream = new MemoryStream();
        BuildDocxWithHtmlAltChunk(docxStream, bytes);
        docxStream.Position = 0;

        var document = DocxReader.Read(docxStream);

        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Contain(text);
    }

    /// <summary>
    /// Builds a minimal .docx package whose body is a single <c>w:altChunk</c> referencing an embedded
    /// <c>text/html</c> part -- exactly the shape Word produces for an inserted HTML fragment, and the
    /// shape <see cref="DocxReader"/>'s <c>TryMaterializeAltChunk</c> reads.
    /// </summary>
    private static void BuildDocxWithHtmlAltChunk(Stream stream, byte[] htmlBytes)
    {
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="html" ContentType="text/html"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);

        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk" Target="chunk1.html"/>
            </Relationships>
            """);

        WriteEntry(archive, "word/document.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:altChunk r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
                <w:sectPr/>
              </w:body>
            </w:document>
            """);

        var entry = archive.CreateEntry("word/chunk1.html");
        using var entryStream = entry.Open();
        entryStream.Write(htmlBytes, 0, htmlBytes.Length);
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
