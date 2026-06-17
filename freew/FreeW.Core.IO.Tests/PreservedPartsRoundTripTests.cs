using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the preserve-and-re-emit (pass-through) strategy for package parts FreeW does not
/// model: word/settings.xml (preserved + overlaid with FreeW's modelled toggles) and the verbatim pass-through
/// of customXml/* and word/webSettings.xml. An authored-from-scratch document (no preserved parts) must emit
/// none of these and round-trip unchanged.
/// </summary>
public class PreservedPartsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string CustomXmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string CustomXmlPropsContentType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string WebSettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] EntryBytes(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath) =>
        XDocument.Load(new MemoryStream(EntryBytes(docx, entryPath)));

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    /// <summary>
    /// Hand-authors a minimal-but-valid docx package carrying: a body paragraph; a settings.xml with an
    /// unmodelled element (w:defaultTabStop) AND a FreeW-modelled toggle (w:autoHyphenation); a customXml item
    /// (item1.xml + itemProps1.xml + customXml/_rels/item1.xml.rels); and a word/webSettings.xml — all wired up
    /// through [Content_Types].xml and word/_rels/document.xml.rels exactly as Word emits them.
    /// </summary>
    private static byte[] AuthorPackage()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
                  <Override PartName="/word/webSettings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml"/>
                  <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings" Target="webSettings.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Hello</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // settings.xml: an unmodelled element (defaultTabStop) interleaved with a FreeW-modelled toggle
            // (autoHyphenation) plus another unmodelled element (w:compat) — the kind of thing FreeW drops today.
            Add("word/settings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:defaultTabStop w:val="708"/>
                  <w:autoHyphenation/>
                  <w:compat><w:doNotExpandShiftReturn/></w:compat>
                </w:settings>
                """);

            Add("word/webSettings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:webSettings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:optimizeForBrowser/>
                </w:webSettings>
                """);

            Add("customXml/item1.xml",
                """<root xmlns="urn:freew:test"><value>preserved</value></root>""");

            Add("customXml/itemProps1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <ds:datastoreItem ds:itemID="{12345678-1234-1234-1234-1234567890AB}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"><ds:schemaRefs/></ds:datastoreItem>
                """);

            Add("customXml/_rels/item1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
                </Relationships>
                """);
        }
        return stream.ToArray();
    }

    // --- settings.xml: preserve + overlay -----------------------------------------------------------

    [Fact]
    public void Settings_UnmodelledElementAndModelledToggle_BothSurviveAndOrderedCorrectly()
    {
        var read = ReadDoc(AuthorPackage());

        // The modelled toggle was recovered into the model.
        read.Page.AutoHyphenation.Should().BeTrue();
        // The unmodelled settings element was preserved.
        read.Preserved.OriginalSettings.Should().NotBeNull();

        var rewritten = WriteBytes(read);
        var settings = EntryXml(rewritten, "word/settings.xml").Root!;

        // The unmodelled element survives verbatim with its value.
        var defaultTabStop = settings.Element(W + "defaultTabStop");
        defaultTabStop.Should().NotBeNull();
        defaultTabStop!.Attribute(W + "val")!.Value.Should().Be("708");

        // The unmodelled w:compat (and its child) survives too.
        settings.Element(W + "compat")!.Element(W + "doNotExpandShiftReturn").Should().NotBeNull();

        // FreeW's modelled toggle is present exactly once (no duplication from the overlay).
        settings.Elements(W + "autoHyphenation").Should().HaveCount(1);

        // CT_Settings schema order: defaultTabStop (38 in schema) precedes autoHyphenation (39).
        var names = settings.Elements().Select(e => e.Name.LocalName).ToList();
        names.IndexOf("defaultTabStop").Should().BeLessThan(names.IndexOf("autoHyphenation"));
    }

    [Fact]
    public void Settings_TogglingAModelledFeatureOn_InsertsItInSchemaOrderWithoutLosingUnmodelled()
    {
        var read = ReadDoc(AuthorPackage());
        // Turn ON a modelled feature the source did NOT have (documentProtection precedes defaultTabStop).
        read.Protection = new ProtectionSettings(ProtectionMode.ReadOnly);

        var settings = EntryXml(WriteBytes(read), "word/settings.xml").Root!;

        // documentProtection was added with enforcement, and ordered before defaultTabStop (33 < 38).
        var protection = settings.Element(W + "documentProtection");
        protection.Should().NotBeNull();
        protection!.Attribute(W + "edit")!.Value.Should().Be("readOnly");
        protection.Attribute(W + "enforcement")!.Value.Should().Be("1");

        var names = settings.Elements().Select(e => e.Name.LocalName).ToList();
        names.IndexOf("documentProtection").Should().BeLessThan(names.IndexOf("defaultTabStop"));
        // Unmodelled settings are still all present.
        settings.Element(W + "defaultTabStop").Should().NotBeNull();
        settings.Element(W + "compat").Should().NotBeNull();
    }

    // --- customXml + webSettings: verbatim pass-through ---------------------------------------------

    [Fact]
    public void CustomXmlAndWebSettings_SurviveVerbatimWithRelationshipsAndContentTypes()
    {
        var source = AuthorPackage();
        var read = ReadDoc(source);

        // All four satellite parts were captured.
        read.Preserved.Parts.Select(p => p.PartName).Should().Contain(new[]
        {
            "/word/webSettings.xml",
            "/customXml/item1.xml",
            "/customXml/itemProps1.xml",
            "/customXml/_rels/item1.xml.rels"
        });

        var rewritten = WriteBytes(read);

        // The parts survive byte-for-byte.
        EntryBytes(rewritten, "word/webSettings.xml").Should().Equal(EntryBytes(source, "word/webSettings.xml"));
        EntryBytes(rewritten, "customXml/item1.xml").Should().Equal(EntryBytes(source, "customXml/item1.xml"));
        EntryBytes(rewritten, "customXml/itemProps1.xml").Should().Equal(EntryBytes(source, "customXml/itemProps1.xml"));
        EntryBytes(rewritten, "customXml/_rels/item1.xml.rels").Should().Equal(EntryBytes(source, "customXml/_rels/item1.xml.rels"));

        // Content-type Overrides re-emitted for the parts that need them (itemProps + webSettings).
        var overrides = EntryXml(rewritten, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .ToDictionary(o => o.Attribute("PartName")!.Value, o => o.Attribute("ContentType")!.Value);
        overrides["/customXml/itemProps1.xml"].Should().Be(CustomXmlPropsContentType);
        overrides["/word/webSettings.xml"].Should().Be(WebSettingsContentType);

        // Document relationships re-emitted for the directly referenced parts (item + webSettings), with the
        // correct types and reconstructed targets.
        var rels = EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        rels.Should().Contain(r =>
            r.Attribute("Type")!.Value == CustomXmlRelType
            && r.Attribute("Target")!.Value == "../customXml/item1.xml");
        rels.Should().Contain(r =>
            r.Attribute("Type")!.Value.EndsWith("/webSettings")
            && r.Attribute("Target")!.Value == "webSettings.xml");
    }

    [Fact]
    public void CustomXmlAndWebSettings_SurviveASecondRoundTrip()
    {
        // Read → write → read → write: the preserved parts must still be present and identical, proving the
        // capture is itself idempotent (a re-read of our own output re-captures them).
        var once = WriteBytes(ReadDoc(AuthorPackage()));
        var twice = WriteBytes(ReadDoc(once));

        EntryBytes(twice, "customXml/item1.xml").Should().Equal(EntryBytes(once, "customXml/item1.xml"));
        EntryBytes(twice, "word/webSettings.xml").Should().Equal(EntryBytes(once, "word/webSettings.xml"));
        HasEntry(twice, "customXml/_rels/item1.xml.rels").Should().BeTrue();
    }

    // --- Regression: authored-from-scratch emits none of these --------------------------------------

    [Fact]
    public void AuthoredFromScratch_EmitsNoSettingsCustomXmlOrWebSettings()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain body"));

        var bytes = WriteBytes(doc);

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        HasEntry(bytes, "word/webSettings.xml").Should().BeFalse();
        HasEntry(bytes, "customXml/item1.xml").Should().BeFalse();

        // Round-trips unchanged.
        var read = ReadDoc(bytes);
        read.PlainText.Should().Be("Plain body");
        read.Preserved.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AuthoredFromScratch_WithModelledFeature_EmitsFreshMinimalSettingsOnly()
    {
        // A FreeW feature (auto-hyphenation) forces a settings part, but with NO preserved parts it must be the
        // fresh minimal part — no customXml/webSettings, and only FreeW's modelled child.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.AutoHyphenation = true;

        var bytes = WriteBytes(doc);
        var settings = EntryXml(bytes, "word/settings.xml").Root!;

        settings.Elements().Select(e => e.Name.LocalName).Should().Equal("autoHyphenation");
        HasEntry(bytes, "word/webSettings.xml").Should().BeFalse();
        HasEntry(bytes, "customXml/item1.xml").Should().BeFalse();

        ReadDoc(bytes).Page.AutoHyphenation.Should().BeTrue();
    }
}
