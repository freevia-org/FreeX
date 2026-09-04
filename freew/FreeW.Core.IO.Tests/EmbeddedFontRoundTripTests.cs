using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for embedded fonts (roadmap item F3): a document that embeds a font materialises a
/// <c>word/fontTable.xml</c> (w:font/w:embed*), the obfuscated <c>word/fonts/fontN.odttf</c> parts, the
/// content-type entries, the document→fontTable relationship and <c>w:embedTrueTypeFonts</c> in
/// word/settings.xml — and the de-obfuscated bytes recovered by the reader equal the originals. Also covers
/// the ODTTF XOR transform directly and the no-embedded-fonts regression (no fontTable, unchanged round-trip).
/// </summary>
public class EmbeddedFontRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string FontTableContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";
    private const string ObfuscatedFontContentType = "application/vnd.openxmlformats-officedocument.obfuscatedFont";
    private const string FontTableRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        var bytes = WriteBytes(document);
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static byte[] EntryBytes(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    /// <summary>A synthetic "font" longer than the 32-byte obfuscation window so we exercise both regions.</summary>
    private static byte[] FakeFont(byte seed, int length = 50) =>
        Enumerable.Range(0, length).Select(i => (byte)((i * 7 + seed) & 0xFF)).ToArray();

    private static TextDocument DocumentWithEmbeddedFont(EmbeddedFont font)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.EmbeddedFonts.Add(font);
        return doc;
    }

    [Fact]
    public void EmbeddedFont_EmitsFontTablePartsContentTypesRelationshipAndSettingsToggle()
    {
        var regular = FakeFont(1);
        var bold = FakeFont(99);
        var doc = DocumentWithEmbeddedFont(new EmbeddedFont("Demo Sans", Regular: regular, Bold: bold));

        var docx = WriteBytes(doc);

        // The fontTable + one .odttf per embedded style + the fontTable's own rels are present.
        HasEntry(docx, "word/fontTable.xml").Should().BeTrue();
        HasEntry(docx, "word/_rels/fontTable.xml.rels").Should().BeTrue();
        HasEntry(docx, "word/fonts/font1.odttf").Should().BeTrue();
        HasEntry(docx, "word/fonts/font2.odttf").Should().BeTrue();

        // Content types: the fontTable Override + the obfuscatedFont Default for the odttf extension.
        var types = EntryXml(docx, "[Content_Types].xml").Root!;
        types.Elements(Ct + "Override").Should().Contain(o =>
            o.Attribute("PartName")!.Value == "/word/fontTable.xml"
            && o.Attribute("ContentType")!.Value == FontTableContentType);
        types.Elements(Ct + "Default").Should().Contain(d =>
            d.Attribute("Extension")!.Value == "odttf"
            && d.Attribute("ContentType")!.Value == ObfuscatedFontContentType);

        // The document→fontTable relationship.
        var docRels = EntryXml(docx, "word/_rels/document.xml.rels").Root!;
        docRels.Elements(Rel + "Relationship").Should().Contain(r =>
            r.Attribute("Type")!.Value == FontTableRelType
            && r.Attribute("Target")!.Value == "fontTable.xml");

        // w:embedTrueTypeFonts is emitted in settings.xml (a settings part is forced by the embed).
        var settings = EntryXml(docx, "word/settings.xml").Root!;
        settings.Element(W + "embedTrueTypeFonts").Should().NotBeNull();

        // The fontTable carries the family name and a w:embedRegular/w:embedBold each with r:id + w:fontKey.
        var font = EntryXml(docx, "word/fontTable.xml").Root!.Element(W + "font")!;
        font.Attribute(W + "name")!.Value.Should().Be("Demo Sans");
        var embedRegular = font.Element(W + "embedRegular")!;
        embedRegular.Attribute(R + "id")?.Value.Should().NotBeNullOrEmpty();
        embedRegular.Attribute(W + "fontKey")?.Value.Should().NotBeNullOrEmpty();
        font.Element(W + "embedBold").Should().NotBeNull();
    }

    [Fact]
    public void EmbeddedFont_OnDiskPartIsObfuscated_AndDeObfuscatesToTheOriginal()
    {
        var regular = FakeFont(1);
        var doc = DocumentWithEmbeddedFont(new EmbeddedFont("Demo Sans", Regular: regular));

        var docx = WriteBytes(doc);

        // The stored part differs from the original within the first 32 bytes (it is obfuscated on disk)...
        var stored = EntryBytes(docx, "word/fonts/font1.odttf");
        stored.Should().NotEqual(regular);

        // ...but the fontKey from the table de-obfuscates it back to the original (XOR is self-inverse).
        var fontKey = EntryXml(docx, "word/fontTable.xml").Root!
            .Element(W + "font")!.Element(W + "embedRegular")!.Attribute(W + "fontKey")!.Value;
        Ooxml.ObfuscateFont(stored, fontKey).Should().Equal(regular);
    }

    [Fact]
    public void EmbeddedFont_RoundTripsAllFourStyles_RecoveringTheOriginalBytes()
    {
        var original = new EmbeddedFont(
            "Demo Sans",
            Regular: FakeFont(1),
            Bold: FakeFont(2),
            Italic: FakeFont(3),
            BoldItalic: FakeFont(4));

        var reloaded = RoundTrip(DocumentWithEmbeddedFont(original));

        reloaded.EmbeddedFonts.Should().ContainSingle();
        var font = reloaded.EmbeddedFonts[0];
        font.Family.Should().Be("Demo Sans");
        font.Regular.Should().Equal(original.Regular);
        font.Bold.Should().Equal(original.Bold);
        font.Italic.Should().Equal(original.Italic);
        font.BoldItalic.Should().Equal(original.BoldItalic);
    }

    [Fact]
    public void EmbeddedFont_PartialStyles_OnlyEmbeddedStylesRoundTrip()
    {
        var original = new EmbeddedFont("Mono", Regular: FakeFont(7), Italic: FakeFont(8));

        var reloaded = RoundTrip(DocumentWithEmbeddedFont(original));

        var font = reloaded.EmbeddedFonts.Single();
        font.Regular.Should().Equal(original.Regular);
        font.Italic.Should().Equal(original.Italic);
        font.Bold.Should().BeNull();
        font.BoldItalic.Should().BeNull();
    }

    [Fact]
    public void NoEmbeddedFonts_EmitsNoFontTable_AndRoundTripsUnchanged()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        var docx = WriteBytes(doc);

        HasEntry(docx, "word/fontTable.xml").Should().BeFalse();
        HasEntry(docx, "word/_rels/fontTable.xml.rels").Should().BeFalse();
        // No settings part is forced for a plain document.
        HasEntry(docx, "word/settings.xml").Should().BeFalse();
        // No odttf Default content type leaks into a font-less document.
        EntryXml(docx, "[Content_Types].xml").Root!.Elements(Ct + "Default")
            .Should().NotContain(d => d.Attribute("Extension")!.Value == "odttf");

        var reloaded = RoundTrip(doc);
        reloaded.EmbeddedFonts.Should().BeEmpty();
        reloaded.PlainText.Should().Be("Body");
    }

    [Fact]
    public void ObfuscateFont_IsItsOwnInverse_AndOnlyTouchesTheFirst32Bytes()
    {
        var fontKey = Ooxml.DeterministicFontKey("Demo Sans/embedRegular");
        var original = FakeFont(5, length: 64);

        var obfuscated = Ooxml.ObfuscateFont(original, fontKey);

        // obfuscate∘obfuscate == identity.
        Ooxml.ObfuscateFont(obfuscated, fontKey).Should().Equal(original);

        // Only the first 32 bytes change; bytes 32+ are copied verbatim.
        obfuscated.Skip(32).Should().Equal(original.Skip(32));
        // The window is genuinely transformed (at least one of the first 32 bytes differs).
        obfuscated.Take(32).Should().NotEqual(original.Take(32));
    }

    [Fact]
    public void ObfuscateFont_ShortFont_ShorterThan32Bytes_RoundTrips()
    {
        var fontKey = Ooxml.DeterministicFontKey("seed");
        var original = FakeFont(9, length: 10);

        var obfuscated = Ooxml.ObfuscateFont(original, fontKey);

        obfuscated.Length.Should().Be(original.Length);
        Ooxml.ObfuscateFont(obfuscated, fontKey).Should().Equal(original);
    }

    [Fact]
    public void DeterministicFontKey_IsStableAcrossWrites()
    {
        var doc = DocumentWithEmbeddedFont(new EmbeddedFont("Demo Sans", Regular: FakeFont(1)));

        string Key(byte[] docx) => EntryXml(docx, "word/fontTable.xml").Root!
            .Element(W + "font")!.Element(W + "embedRegular")!.Attribute(W + "fontKey")!.Value;

        // The same document written twice yields byte-identical font parts and font keys (no randomness).
        var first = WriteBytes(doc);
        var second = WriteBytes(doc);
        Key(first).Should().Be(Key(second));
        EntryBytes(first, "word/fonts/font1.odttf").Should().Equal(EntryBytes(second, "word/fonts/font1.odttf"));
    }
}
