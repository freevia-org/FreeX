using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.App.Presentation.Editing;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// sweep99 F1/F2: <c>DocxReader.ReadNumbering</c> used to collapse EVERY non-multilevel abstractNum to a
/// bare <see cref="ListKind.Bullet"/>/<see cref="ListKind.Number"/> enum, discarding the source document's
/// actual <c>w:lvlText</c> glyph/pattern and <c>w:numFmt</c> — the marker planner then hardcoded a round
/// bullet and a decimal-with-period for every such list, and <c>DocxWriter.BuildNumbering</c> only ever
/// emitted its own two fixed definitions, so the original glyph/format was permanently lost on save. These
/// tests hand-author a FOREIGN (non-FreeW-shaped) docx — FreeW's own writer never produces the non-default
/// shapes under test, so a save-and-reload of FreeW's own output could never exercise this bug — and assert
/// BOTH what <c>DocumentListMarkerSequencePlanner</c> (the shared WPF/Avalonia on-screen marker planner)
/// produces AND what a subsequent save preserves in word/numbering.xml.
/// </summary>
public sealed class FlatListMarkerFidelityTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static byte[] AuthorPackage(string numberingXml, string documentBodyXml)
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
                  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
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
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                </Relationships>
                """);
            Add("word/document.xml", documentBodyXml);
            Add("word/numbering.xml", numberingXml);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// THE FIX (F1, bullet glyph): a foreign dash-bulleted list (abstractNum numFmt="bullet",
    /// lvlText="-", NOT FreeW's own round "•") must read back with the real glyph captured, the shared
    /// on-screen marker planner must render it as "-" instead of the hardcoded "•", and saving must re-emit
    /// "-" in word/numbering.xml rather than silently substituting FreeW's fixed bullet definition.
    /// </summary>
    [Fact]
    public void DashBulletList_CapturesActualGlyph_RendersAndSurvivesSave()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="7">
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="-"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="40"><w:abstractNumId w:val="7"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="40"/></w:numPr></w:pPr><w:r><w:t>Dashed</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);
        var paragraph = doc.Blocks.OfType<Paragraph>().Single();

        paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraph.Formatting.ListMarkerText.Should().Be("-",
            "the document's own dash glyph must be captured, not discarded in favor of a hardcoded round bullet");

        var plan = new DocumentListMarkerSequencePlanner().Advance(paragraph);
        plan.MarkerText.Should().Be("-",
            "the shared WPF/Avalonia marker planner must show the real glyph the user sees on open");

        var saved = WriteBytes(doc);
        var numbering = EntryXml(saved, "word/numbering.xml");
        var savedLvlTexts = numbering.Descendants(W + "lvlText").Attributes(W + "val").Select(a => a.Value).ToList();
        savedLvlTexts.Should().Contain("-",
            "saving must re-emit the actual glyph in word/numbering.xml, not just FreeW's two fixed definitions");

        // The glyph must survive a full write-then-reread round trip, not just appear once in the saved XML.
        var reread = ReadDoc(saved);
        var rereadParagraph = reread.Blocks.OfType<Paragraph>().Single();
        rereadParagraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
        rereadParagraph.Formatting.ListMarkerText.Should().Be("-");
    }

    /// <summary>
    /// THE FIX (F1, number format): a foreign flat (single-level, non-multilevel) "a) b) c)" list
    /// (abstractNum numFmt="lowerLetter", lvlText="%1)") must read back with the real format captured, the
    /// planner must render successive markers as "a)", "b)", "c)" instead of "1.", "2.", "3.", and saving
    /// must preserve the lowerLetter format and "%1)" pattern.
    /// </summary>
    [Fact]
    public void LowerLetterFlatList_CapturesActualFormat_RendersAndSurvivesSave()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="8">
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1)"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="41"><w:abstractNumId w:val="8"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="41"/></w:numPr></w:pPr><w:r><w:t>First</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="41"/></w:numPr></w:pPr><w:r><w:t>Second</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Number);
        paragraphs[0].Formatting.ListNumberFormat.Should().Be(ListNumberFormat.LowerLetter);
        paragraphs[0].Formatting.ListMarkerText.Should().Be("%1)");

        var planner = new DocumentListMarkerSequencePlanner();
        planner.Advance(paragraphs[0]).MarkerText.Should().Be("a)",
            "lowerLetter format + the real \"%1)\" pattern must render as \"a)\", not the hardcoded \"1.\"");
        planner.Advance(paragraphs[1]).MarkerText.Should().Be("b)");

        var saved = WriteBytes(doc);
        var numbering = EntryXml(saved, "word/numbering.xml");
        numbering.Descendants(W + "numFmt").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("lowerLetter",
                "saving must preserve the actual number format, not silently downgrade every list to decimal");
        numbering.Descendants(W + "lvlText").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("%1)");
    }

    /// <summary>
    /// THE FIX (F2): a per-instance <c>w:lvlOverride/w:lvl</c> (Word's "customize just this list" feature,
    /// distinct from a plain <c>w:lvlOverride/w:startOverride</c>) must win over the shared abstractNum's own
    /// default for that one numId, instead of being silently skipped because the reader only ever looked for
    /// startOverride.
    /// </summary>
    [Fact]
    public void PerInstanceLvlOverride_WinsOverAbstractNumDefault()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="9">
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#x2022;"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="50"><w:abstractNumId w:val="9"/></w:num>
                  <w:num w:numId="51">
                    <w:abstractNumId w:val="9"/>
                    <w:lvlOverride w:ilvl="0">
                      <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#x25AA;"/><w:lvlJc w:val="left"/></w:lvl>
                    </w:lvlOverride>
                  </w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="50"/></w:numPr></w:pPr><w:r><w:t>Plain</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="51"/></w:numPr></w:pPr><w:r><w:t>Customized</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[0].Formatting.ListMarkerText.Should().BeNull(
            "the plain instance uses the abstractNum's own default round bullet, so it normalizes to null");

        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[1].Formatting.ListMarkerText.Should().Be("▪",
            "the per-instance w:lvlOverride/w:lvl customization must win over the shared abstractNum default");

        var saved = WriteBytes(doc);
        var numbering = EntryXml(saved, "word/numbering.xml");
        numbering.Descendants(W + "lvlText").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("▪",
                "the per-instance override must survive a save, not just the plain instance's default bullet");
    }

    /// <summary>
    /// SIBLING / no-regression coverage: FreeW's own authored bullet and number lists (no captured marker
    /// data — ListMarkerText null, ListNumberFormat Decimal, exactly as before this fix) must still round-trip
    /// through word/numbering.xml using ONLY the historical fixed bullet('•')/decimal('%1.') abstractNum
    /// definitions — no extra marker-override abstractNum is allocated for the common, unmodified case.
    /// </summary>
    [Fact]
    public void FreeWAuthoredLists_WithDefaultMarkers_EmitOnlyTheFixedDefinitions()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bullet item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });
        doc.Blocks.Add(new Paragraph("number item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number }
        });

        var saved = WriteBytes(doc);
        var numbering = EntryXml(saved, "word/numbering.xml");

        var abstractNumIds = numbering.Descendants(W + "abstractNum")
            .Attributes(W + "abstractNumId").Select(a => a.Value).ToList();
        abstractNumIds.Should().BeEquivalentTo(new[] { "0", "1", "2" },
            "unmodified FreeW-authored lists must keep using exactly the three fixed definitions, with no " +
            "extra marker-override abstractNum allocated just because the paragraphs carry (default) marker fields");

        var reread = ReadDoc(saved);
        var paragraphs = reread.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[0].Formatting.ListMarkerText.Should().BeNull();
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Number);
        paragraphs[1].Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);

        var planner = new DocumentListMarkerSequencePlanner();
        planner.Advance(paragraphs[0]).MarkerText.Should().Be("•");
        planner.Advance(paragraphs[1]).MarkerText.Should().Be("1.");
    }
}
