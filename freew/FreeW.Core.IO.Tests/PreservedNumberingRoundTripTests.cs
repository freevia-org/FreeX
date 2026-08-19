using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the preserve-alongside strategy for numbering FreeW does not model: when a
/// document's paragraphs reference a <c>word/numbering.xml</c> with rich (multilevel / custom-format) definitions
/// FreeW's heuristic list model cannot represent, BOTH the original numbering.xml AND the paragraphs' w:numPr
/// must survive a round-trip. FreeW's own authored lists keep using FreeW's fixed numIds (1/2/3); the preserved
/// definitions are merged under a DISJOINT id range (abstractNumId&gt;=3 / numId&gt;=4) so the two never collide.
/// An authored-from-scratch / FreeW-only-lists document carries no preserved numbering and is unaffected.
/// </summary>
public class PreservedNumberingRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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
    /// Hand-authors a minimal-but-valid docx package whose two body paragraphs reference a numbering definition
    /// (numId 12) that FreeW's reader does NOT map to one of its own list kinds — its w:num points at an abstract
    /// (abstractNumId 99) that is not defined in the part, the way numbering carried by a referenced numbering
    /// style is left unresolved. The same numbering.xml also carries a rich legal/multilevel abstract
    /// (abstractNumId 5, upperRoman/decimal/lowerLetter custom level text + a w15 extension attribute) — the kind
    /// of formatting FreeW cannot represent and drops today. Wired up through [Content_Types].xml +
    /// document.xml.rels exactly as Word emits it.
    /// </summary>
    private static byte[] AuthorForeignNumberingPackage()
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

            // Two body paragraphs at two outline levels of the foreign numId 12 (which the reader leaves
            // unmapped, so FreeW does not model these as its own lists).
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="12"/></w:numPr></w:pPr><w:r><w:t>Article one</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="2"/><w:numId w:val="12"/></w:numPr></w:pPr><w:r><w:t>Sub clause</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // A numbering.xml using arbitrary ids: a rich legal/multilevel abstract (abstractNumId 5: upperRoman
            // / decimal / lowerLetter custom level text + a w15 extension attribute) FreeW cannot represent, and
            // the num the paragraphs use (numId 12) points at abstractNumId 99 which is intentionally NOT defined
            // here — so the reader leaves it unmapped (ListKind.None) and the preserve path must keep both the
            // numbering.xml and the paragraphs' numPr. Both abstract + both nums survive verbatim (remapped).
            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml">
                  <w:abstractNum w:abstractNumId="5" w15:restartNumberingAfterBreak="0">
                    <w:multiLevelType w:val="multilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Article %1."/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1.%2"/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1.%2.%3"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="7"><w:abstractNumId w:val="5"/></w:num>
                  <w:num w:numId="12"><w:abstractNumId w:val="99"/></w:num>
                </w:numbering>
                """);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Hand-authors a minimal-but-valid docx package whose paragraph STYLE definition (styleId "ListNum")
    /// carries the numbering via <c>w:pPr/w:numPr</c> (numId 2, referencing a rich multilevel abstract in
    /// numbering.xml FreeW cannot represent) — exactly the FieldCodes.docx / stress023.docx shape where the
    /// document body has no direct numPr. One body paragraph uses that style. The numbering.xml's <c>w:num</c>
    /// (numId 2) resolves to a multilevel/legal abstract (abstractNumId 10), so the reader leaves it unmodelled
    /// (style-level numbering FreeW does not represent) and the preserve path must keep both numbering.xml AND
    /// the style's numPr.
    /// </summary>
    private static byte[] AuthorStyleLevelNumberingPackage()
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
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                </Relationships>
                """);

            // The body paragraph gets its numbering ONLY from its style (w:pStyle val="ListNum"); it carries
            // NO direct w:numPr — the style-level numbering case (numId=0 in the body).
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:pStyle w:val="ListNum"/></w:pPr><w:r><w:t>Numbered via style</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // The paragraph style "ListNum" carries the numbering in its OWN definition (w:pPr/w:numPr → numId 2).
            Add("word/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="ListNum">
                    <w:name w:val="List Number"/>
                    <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr>
                  </w:style>
                </w:styles>
                """);

            // numId 2 → abstractNumId 10, a rich multilevel/legal definition (upperRoman custom level text +
            // a w15 extension attribute) FreeW cannot represent and drops today.
            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml">
                  <w:abstractNum w:abstractNumId="10" w15:restartNumberingAfterBreak="0">
                    <w:multiLevelType w:val="multilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Section %1."/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1.%2"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="2"><w:abstractNumId w:val="10"/></w:num>
                </w:numbering>
                """);
        }
        return stream.ToArray();
    }

    // --- Preserve-alongside: STYLE-level numbering survives -----------------------------------------

    [Fact]
    public void StyleLevelNumbering_PartAndStyleNumPr_SurviveRoundTrip()
    {
        var read = ReadDoc(AuthorStyleLevelNumberingPackage());

        // The numbering.xml was captured, and the style kept its original numPr (FreeW does NOT model
        // style-level numbering, so the style carries a PreservedNumbering pointing at the source numId).
        read.Preserved.OriginalNumbering.Should().NotBeNull();
        read.Styles.Should().ContainKey("ListNum");
        var style = read.Styles["ListNum"];
        style.PreservedNumbering.Should().NotBeNull();
        style.PreservedNumbering!.Value.NumId.Should().Be(2);
        style.PreservedNumbering!.Value.Ilvl.Should().Be(0);
        // The body paragraph has no direct numbering of its own.
        var bodyPara = read.Blocks.OfType<Paragraph>().First();
        bodyPara.Formatting.ListKind.Should().Be(ListKind.None);
        bodyPara.PreservedNumbering.Should().BeNull();

        var rewritten = WriteBytes(read);

        // numbering.xml survives, carrying the rich formatting (upperRoman, custom level text).
        HasEntry(rewritten, "word/numbering.xml").Should().BeTrue();
        var numbering = EntryXml(rewritten, "word/numbering.xml").Root!;
        numbering.Descendants(W + "numFmt").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("upperRoman");
        numbering.Descendants(W + "lvlText").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("Section %1.");

        // The style in the output still carries a w:pPr/w:numPr pointing at a w:num that EXISTS in the
        // re-emitted numbering.xml (the remapped, disjoint id).
        var emittedNumIds = numbering.Elements(W + "num")
            .Select(n => n.Attribute(W + "numId")!.Value).ToHashSet();
        var styleEl = EntryXml(rewritten, "word/styles.xml").Root!.Elements(W + "style")
            .Single(s => s.Attribute(W + "styleId")!.Value == "ListNum");
        var styleNumPr = styleEl.Element(W + "pPr")!.Element(W + "numPr")!;
        var styleNumId = styleNumPr.Element(W + "numId")!.Attribute(W + "val")!.Value;
        emittedNumIds.Should().Contain(styleNumId);
        styleNumPr.Element(W + "ilvl")!.Attribute(W + "val")!.Value.Should().Be("0");

        // The remapped style numId is clear of FreeW's reserved ids (1/2/3) and collides with no other num.
        int.Parse(styleNumId).Should().BeGreaterThanOrEqualTo(4);
        var numIds = numbering.Elements(W + "num").Select(n => n.Attribute(W + "numId")!.Value).ToList();
        numIds.Should().OnlyHaveUniqueItems();
        numbering.Elements(W + "abstractNum").Select(a => a.Attribute(W + "abstractNumId")!.Value)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void StyleLevelNumbering_SurvivesASecondRoundTrip()
    {
        // read → write → read → write: the captured numbering + style numPr must still resolve after a re-read
        // of our own output (idempotent: our remapped style numPr is itself re-captured on re-read).
        var once = WriteBytes(ReadDoc(AuthorStyleLevelNumberingPackage()));
        var reread = ReadDoc(once);
        reread.Preserved.OriginalNumbering.Should().NotBeNull();
        reread.Styles["ListNum"].PreservedNumbering.Should().NotBeNull();

        var twice = WriteBytes(reread);
        HasEntry(twice, "word/numbering.xml").Should().BeTrue();
        var emittedNumIds = EntryXml(twice, "word/numbering.xml").Root!.Elements(W + "num")
            .Select(n => n.Attribute(W + "numId")!.Value).ToHashSet();
        var styleEl = EntryXml(twice, "word/styles.xml").Root!.Elements(W + "style")
            .Single(s => s.Attribute(W + "styleId")!.Value == "ListNum");
        var styleNumId = styleEl.Element(W + "pPr")!.Element(W + "numPr")!.Element(W + "numId")!.Attribute(W + "val")!.Value;
        emittedNumIds.Should().Contain(styleNumId);
    }

    // --- Regression: FreeW-authored styles (no numbering) are unaffected ----------------------------

    [Fact]
    public void FreeWAuthoredStyles_NoNumbering_RoundTripUnchanged_NoPreservedNumbering()
    {
        // A FreeW-authored-styles document with no style-level numbering must be byte-equivalent to before:
        // the styles carry no w:numPr, NO numbering part is emitted, and the plan stays null.
        var doc = new TextDocument();
        doc.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Run = RunFormatting.Default with { Bold = true }
        };
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });

        var first = WriteBytes(doc);
        // No numbering part (no list, no preserved numbering).
        HasEntry(first, "word/numbering.xml").Should().BeFalse();
        // The style carries no w:pPr/w:numPr.
        var styleEl = EntryXml(first, "word/styles.xml").Root!.Elements(W + "style")
            .Single(s => s.Attribute(W + "styleId")!.Value == "Heading1");
        styleEl.Descendants(W + "numPr").Should().BeEmpty();

        // Byte-equivalence: read back and re-write yields the identical package (the plan stays null because
        // no style/paragraph carries preserved numbering).
        var read = ReadDoc(first);
        read.Styles["Heading1"].PreservedNumbering.Should().BeNull();
        var second = WriteBytes(read);
        second.Should().Equal(first);
    }

    // --- Preserve-alongside: foreign numbering survives ---------------------------------------------

    [Fact]
    public void ForeignNumbering_PartAndNumPr_SurviveRoundTrip()
    {
        var read = ReadDoc(AuthorForeignNumberingPackage());

        // The original numbering.xml was captured, and the two body paragraphs kept their original numPr
        // (FreeW did NOT model their numbering as one of its own lists, so ListKind stays None).
        read.Preserved.OriginalNumbering.Should().NotBeNull();
        var paragraphs = read.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.None);
        paragraphs[0].PreservedNumbering.Should().NotBeNull();
        paragraphs[0].PreservedNumbering!.Value.NumId.Should().Be(12);
        paragraphs[0].PreservedNumbering!.Value.Ilvl.Should().Be(0);
        paragraphs[1].PreservedNumbering!.Value.Ilvl.Should().Be(2);

        var rewritten = WriteBytes(read);

        // numbering.xml survives, carrying the foreign definition's rich formatting (upperRoman, custom text).
        HasEntry(rewritten, "word/numbering.xml").Should().BeTrue();
        var numbering = EntryXml(rewritten, "word/numbering.xml").Root!;
        numbering.Descendants(W + "numFmt").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("upperRoman").And.Contain("lowerLetter");
        numbering.Descendants(W + "lvlText").Attributes(W + "val").Select(a => a.Value)
            .Should().Contain("Article %1.");

        // Each body paragraph still carries a w:numPr that points at a w:num that actually EXISTS in the
        // re-emitted numbering.xml (the remapped id), so the list still renders.
        var emittedNumIds = numbering.Elements(W + "num")
            .Select(n => n.Attribute(W + "numId")!.Value).ToHashSet();
        var docParagraphs = EntryXml(rewritten, "word/document.xml").Root!
            .Element(W + "body")!.Elements(W + "p").ToList();
        foreach (var p in docParagraphs.Take(2))
        {
            var numId = p.Element(W + "pPr")!.Element(W + "numPr")!.Element(W + "numId")!.Attribute(W + "val")!.Value;
            emittedNumIds.Should().Contain(numId);
        }

        // The two paragraphs reference the SAME (remapped) num and keep their distinct ilvls.
        var n0 = docParagraphs[0].Element(W + "pPr")!.Element(W + "numPr")!;
        var n1 = docParagraphs[1].Element(W + "pPr")!.Element(W + "numPr")!;
        n0.Element(W + "numId")!.Attribute(W + "val")!.Value
            .Should().Be(n1.Element(W + "numId")!.Attribute(W + "val")!.Value);
        n0.Element(W + "ilvl")!.Attribute(W + "val")!.Value.Should().Be("0");
        n1.Element(W + "ilvl")!.Attribute(W + "val")!.Value.Should().Be("2");
    }

    [Fact]
    public void ForeignNumbering_SurvivesASecondRoundTrip()
    {
        // read → write → read → write: the preserved numbering + numPr must still resolve after a re-read of
        // our own output, proving the capture is idempotent (our remapped output is itself captured on re-read).
        var once = WriteBytes(ReadDoc(AuthorForeignNumberingPackage()));
        var reread = ReadDoc(once);
        reread.Preserved.OriginalNumbering.Should().NotBeNull();
        reread.Blocks.OfType<Paragraph>().First().PreservedNumbering.Should().NotBeNull();

        var twice = WriteBytes(reread);
        HasEntry(twice, "word/numbering.xml").Should().BeTrue();
        var emittedNumIds = EntryXml(twice, "word/numbering.xml").Root!.Elements(W + "num")
            .Select(n => n.Attribute(W + "numId")!.Value).ToHashSet();
        var p0 = EntryXml(twice, "word/document.xml").Root!.Element(W + "body")!.Elements(W + "p").First();
        var numId = p0.Element(W + "pPr")!.Element(W + "numPr")!.Element(W + "numId")!.Attribute(W + "val")!.Value;
        emittedNumIds.Should().Contain(numId);
    }

    [Fact]
    public void ForeignNumbering_InNonBodyStories_SurvivesRoundTrip()
    {
        var source = ReadDoc(AuthorForeignNumberingPackage());
        var document = new TextDocument();
        document.Preserved.OriginalNumbering = new XElement(source.Preserved.OriginalNumbering!);

        Paragraph StoryParagraph(string text, int level) => new(text)
        {
            PreservedNumbering = new PreservedNumbering(12, level)
        };

        var header = new HeaderFooter();
        header.Paragraphs.Add(StoryParagraph("Header item", 0));
        document.Header = header;

        var footnote = new Footnote(1);
        footnote.Content.Add(StoryParagraph("Footnote item", 1));
        document.Footnotes[footnote.Id] = footnote;

        var endnote = new Endnote(1);
        endnote.Content.Add(StoryParagraph("Endnote item", 2));
        document.Endnotes[endnote.Id] = endnote;

        var comment = new Comment(0, string.Empty, author: "A", initials: "A");
        comment.Content[0] = StoryParagraph("Comment item", 0);
        document.Comments[comment.Id] = comment;

        var bytes = WriteBytes(document);
        HasEntry(bytes, "word/numbering.xml").Should().BeTrue();
        var emittedNumIds = EntryXml(bytes, "word/numbering.xml").Root!.Elements(W + "num")
            .Select(element => element.Attribute(W + "numId")!.Value).ToHashSet();

        string AssertStoryNumPr(string path, string text, string expectedLevel)
        {
            var paragraph = EntryXml(bytes, path).Descendants(W + "p")
                .Single(element => string.Concat(element.Descendants(W + "t").Select(t => t.Value)) == text);
            var numPr = paragraph.Element(W + "pPr")!.Element(W + "numPr")!;
            var numId = numPr.Element(W + "numId")!.Attribute(W + "val")!.Value;
            emittedNumIds.Should().Contain(numId);
            numPr.Element(W + "ilvl")!.Attribute(W + "val")!.Value.Should().Be(expectedLevel);
            return numId;
        }

        var remappedNumId = int.Parse(AssertStoryNumPr("word/header1.xml", "Header item", "0"));
        AssertStoryNumPr("word/footnotes.xml", "Footnote item", "1").Should().Be(remappedNumId.ToString());
        AssertStoryNumPr("word/endnotes.xml", "Endnote item", "2").Should().Be(remappedNumId.ToString());
        AssertStoryNumPr("word/comments.xml", "Comment item", "0").Should().Be(remappedNumId.ToString());

        var reread = ReadDoc(bytes);
        reread.Header!.Paragraphs.Single(paragraph => paragraph.PlainText == "Header item")
            .PreservedNumbering.Should().Be(new PreservedNumbering(remappedNumId, 0));
        reread.Footnotes[1].Content.Single().PreservedNumbering.Should().Be(new PreservedNumbering(remappedNumId, 1));
        reread.Endnotes[1].Content.Single().PreservedNumbering.Should().Be(new PreservedNumbering(remappedNumId, 2));
        reread.Comments[0].Content.Single().PreservedNumbering.Should().Be(new PreservedNumbering(remappedNumId, 0));
    }

    // --- Disjoint id space: FreeW-authored list + foreign numbering coexist -------------------------

    [Fact]
    public void FreeWListAndForeignNumbering_Coexist_WithoutNumIdCollision()
    {
        // Start from a doc that ALREADY has foreign numbering, then add a FreeW-authored bullet list paragraph.
        var read = ReadDoc(AuthorForeignNumberingPackage());
        read.Blocks.Add(new Paragraph("FreeW bullet")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });

        var rewritten = WriteBytes(read);
        var numbering = EntryXml(rewritten, "word/numbering.xml").Root!;

        // No two w:num share a numId, and no two w:abstractNum share an abstractNumId (no collision).
        var numIds = numbering.Elements(W + "num").Select(n => n.Attribute(W + "numId")!.Value).ToList();
        numIds.Should().OnlyHaveUniqueItems();
        var abstractIds = numbering.Elements(W + "abstractNum")
            .Select(a => a.Attribute(W + "abstractNumId")!.Value).ToList();
        abstractIds.Should().OnlyHaveUniqueItems();

        // FreeW's own fixed ids (1/2/3) are present (FreeW authored a list); the foreign num was remapped clear
        // of them (numId >= 4) so both render their own numbering.
        numIds.Should().Contain("1");
        numIds.Where(id => int.Parse(id) >= 4).Should().NotBeEmpty();

        // Both lists render: the FreeW bullet paragraph points at numId 1; the foreign paragraphs point at the
        // remapped (>=4) num that exists in numbering.xml.
        var emitted = numIds.ToHashSet();
        var docParas = EntryXml(rewritten, "word/document.xml").Root!.Element(W + "body")!.Elements(W + "p").ToList();
        var foreign0 = docParas[0].Element(W + "pPr")!.Element(W + "numPr")!.Element(W + "numId")!.Attribute(W + "val")!.Value;
        var freeWBullet = docParas[2].Element(W + "pPr")!.Element(W + "numPr")!.Element(W + "numId")!.Attribute(W + "val")!.Value;
        emitted.Should().Contain(foreign0);
        freeWBullet.Should().Be("1");
        int.Parse(foreign0).Should().BeGreaterThanOrEqualTo(4);
    }

    // --- Regression: FreeW-authored lists are unaffected --------------------------------------------

    [Fact]
    public void FreeWAuthoredLists_RoundTripUnchanged_WithNoPreservedNumbering()
    {
        // A FreeW-authored bullet + number + multilevel document (no foreign numbering) must behave exactly as
        // before: FreeW's fixed numIds 1/2/3, three abstractNums 0/1/2, and no preserved numbering captured.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bullet") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });
        doc.Blocks.Add(new Paragraph("number") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 1 } });
        doc.Blocks.Add(new Paragraph("outline") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 1 } });

        var bytes = WriteBytes(doc);
        var numbering = EntryXml(bytes, "word/numbering.xml").Root!;

        // Exactly FreeW's three abstractNums (0/1/2) and three nums (1/2/3), nothing extra.
        numbering.Elements(W + "abstractNum").Select(a => a.Attribute(W + "abstractNumId")!.Value)
            .Should().Equal("0", "1", "2");
        numbering.Elements(W + "num").Select(n => n.Attribute(W + "numId")!.Value)
            .Should().Equal("1", "2", "3");

        // Read back: the kinds/levels survive and NO preserved numbering is captured.
        var read = ReadDoc(bytes);
        read.Preserved.OriginalNumbering.Should().NotBeNull(); // numbering.xml exists, so it is captured...
        var paras = read.Blocks.OfType<Paragraph>().ToList();
        // ...but FreeW maps each paragraph to its own ListKind, so none keeps a PreservedNumbering.
        paras.Should().OnlyContain(p => p.PreservedNumbering == null);
        paras[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paras[1].Formatting.ListKind.Should().Be(ListKind.Number);
        paras[2].Formatting.ListKind.Should().Be(ListKind.MultiLevel);

        // Re-writing the read-back document yields the SAME FreeW-only numbering (no preserved merge kicked in,
        // because no paragraph carries a PreservedNumbering).
        var rewritten = WriteBytes(read);
        var renumbering = EntryXml(rewritten, "word/numbering.xml").Root!;
        renumbering.Elements(W + "num").Select(n => n.Attribute(W + "numId")!.Value)
            .Should().Equal("1", "2", "3");
    }

    [Fact]
    public void AuthoredFromScratch_NoNumbering_EmitsNoNumberingPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain body"));

        var bytes = WriteBytes(doc);

        HasEntry(bytes, "word/numbering.xml").Should().BeFalse();
        var read = ReadDoc(bytes);
        read.Preserved.OriginalNumbering.Should().BeNull();
        read.Preserved.IsEmpty.Should().BeTrue();
        read.Blocks.OfType<Paragraph>().Should().OnlyContain(p => p.PreservedNumbering == null);
    }

    // P11 regression — a "fancy" multilevel abstractNum whose level 0 is a bullet (as in Word's
    // "List Bullet Multilevel" template) must be classified MultiLevel, not Bullet.  Before the fix the
    // IsMultiLevel check only ran in the else branch of (numFmt == "bullet"), so a bullet-level-0
    // multilevel template was collapsed to ListKind.Bullet, dropping the numbered character of sub-levels.
    [Fact]
    public void AbstractNum_BulletLevel0_WithMultiLevelType_IsClassifiedMultiLevel()
    {
        // Build a minimal docx with a numbering.xml that has:
        //   abstractNumId=1, w:multiLevelType="multilevel",
        //   lvl 0 = bullet, lvl 1 = decimal  (Word's "List Bullet Multilevel" template shape).
        // A body paragraph at ilvl 0 uses numId 1 → abstractNumId 1.
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

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Bullet top</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Decimal sub</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // abstractNumId=1 has multiLevelType="multilevel", level 0 = bullet, level 1 = decimal.
            // Without the P11 fix this was classified ListKind.Bullet (level-0 wins); with the fix it
            // is classified ListKind.MultiLevel (IsMultiLevel checked first).
            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="1">
                    <w:multiLevelType w:val="multilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#x2022;"/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%2."/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="1"><w:abstractNumId w:val="1"/></w:num>
                </w:numbering>
                """);
        }
        stream.Position = 0;
        var doc = ReadDoc(stream.ToArray());

        // Both paragraphs reference numId 1 → abstractNumId 1 → classified MultiLevel (P11 fix).
        doc.Blocks.OfType<Paragraph>().Should().OnlyContain(
            p => p.Formatting.ListKind == ListKind.MultiLevel,
            because: "an abstractNum with multiLevelType=multilevel must be MultiLevel even when level 0 is a bullet");
    }

    /// <summary>
    /// An imported document whose second, independently-numbered list has MORE THAN ONE paragraph must
    /// re-export with that whole list on one numbering instance of its own. The reader turns the numId change
    /// into a restart on the second list's FIRST paragraph only (its continuations read as "continue"), and
    /// the writer must keep those continuations on the restart's numId instead of dropping them back onto the
    /// base list — otherwise list 2 reopens in Word numbered 1, 4, 5 instead of 1, 2, 3.
    /// </summary>
    [Fact]
    public void ImportedIndependentSecondList_ReExports_WithItsWholeRunOnOneNumId()
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

            // Two lists, two numIds against the SAME decimal abstract (what Word writes for "new list"),
            // the second one three paragraphs long.
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>list 1 item 1</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>list 1 item 2</w:t></w:r></w:p>
                    <w:p><w:r><w:t>separating body text</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>list 2 item 1</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>list 2 item 2</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>list 2 item 3</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            Add("word/numbering.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="0">
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
                  <w:num w:numId="2"><w:abstractNumId w:val="0"/></w:num>
                </w:numbering>
                """);
        }

        var imported = ReadDoc(stream.ToArray());
        var importedParagraphs = imported.Blocks.OfType<Paragraph>().ToList();

        // The reader marks the start of the second instance and nothing else.
        importedParagraphs.Select(p => p.Formatting.ListStartOverride)
            .Should().Equal(null, null, null, 1, null, null);

        var reExported = EntryXml(WriteBytes(imported), "word/document.xml");
        var numIds = reExported.Descendants(W + "numId")
            .Where(id => id.Parent?.Name == W + "numPr")
            .Select(id => int.Parse(id.Attribute(W + "val")!.Value))
            .ToList();

        numIds.Should().HaveCount(5);
        numIds[1].Should().Be(numIds[0]);
        numIds[2].Should().NotBe(numIds[0], "the second list is an independent numbering instance");
        numIds[3].Should().Be(numIds[2]);
        numIds[4].Should().Be(numIds[2]);
    }

    // --- sweep85 F1: an explicit list toggle must discard stale preserved numbering -----------------

    /// <summary>Minimal <see cref="IDocumentCommandContext"/> so production commands (e.g.
    /// <see cref="SetParagraphFormattingCommand"/>) can be exercised directly against a document that was
    /// never wired to a full <see cref="DocumentCommandBus"/>/editing session.</summary>
    private sealed class TestCommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    /// <summary>
    /// Reproduces the sweep85 F1 finding: a paragraph imported with foreign numbering FreeW cannot model
    /// (ListKind.None + Paragraph.PreservedNumbering set) has FreeW's own list turned ON then OFF again —
    /// exactly the transforms <c>DocumentParagraphFormattingCoordinator.ToggleListKind</c>'s enable/disable
    /// branches apply, via the same <see cref="SetParagraphFormattingCommand"/> the ribbon's Bullets/Numbering
    /// toggle drives. Before the fix, PreservedNumbering survived the round trip untouched (it lives on
    /// <see cref="Paragraph"/>, not <see cref="ParagraphFormatting"/>), so DocxWriter's ListKind==None fallback
    /// re-emitted the paragraph's OLD foreign numPr — the user's explicit "remove this list" action came back
    /// after save/reopen.
    /// </summary>
    [Fact]
    public void ForeignNumbering_ToggleListKindOnThenOff_ClearsPreservedNumbering_DoesNotReturnOnSave()
    {
        var read = ReadDoc(AuthorForeignNumberingPackage());
        var paragraph = read.Blocks.OfType<Paragraph>().First();
        paragraph.Formatting.ListKind.Should().Be(ListKind.None);
        paragraph.PreservedNumbering.Should().NotBeNull("the reader captured the foreign numId=12 it could not model");

        var context = new TestCommandContext(read);

        // Ribbon Bullets toggle, enable branch: ToggleListKind sets ListKind to the requested kind.
        var enable = new SetParagraphFormattingCommand(0, paragraph.Formatting with { ListKind = ListKind.Bullet });
        enable.Apply(context);
        paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);

        // Ribbon Bullets toggle, disable branch: ToggleListKind resets ListKind/ListLevel/ListStartOverride.
        var disable = new SetParagraphFormattingCommand(0, paragraph.Formatting with
        {
            ListKind = ListKind.None,
            ListLevel = 0,
            ListStartOverride = null,
        });
        disable.Apply(context);
        paragraph.Formatting.ListKind.Should().Be(ListKind.None);

        // The fix under test: the explicit ListKind round trip must have discarded the stale foreign
        // numbering, or the writer's ListKind==None fallback will re-emit it below.
        paragraph.PreservedNumbering.Should().BeNull(
            "the user explicitly decided this paragraph's list state; the foreign numbering it replaced must not survive");

        var rewritten = WriteBytes(read);
        var p0 = EntryXml(rewritten, "word/document.xml").Root!.Element(W + "body")!.Elements(W + "p").First();
        p0.Element(W + "pPr")?.Element(W + "numPr").Should().BeNull(
            "the disabled list must not come back as a numPr pointing at the old foreign numbering");

        // Reopening confirms the paragraph reads back as a plain paragraph, not a list item.
        var reread = ReadDoc(rewritten);
        var reParagraph = reread.Blocks.OfType<Paragraph>().First();
        reParagraph.Formatting.ListKind.Should().Be(ListKind.None);
        reParagraph.PreservedNumbering.Should().BeNull();
    }

    /// <summary>
    /// Sibling no-regression case: a paragraph-formatting edit that does NOT touch ListKind (a "Keep With
    /// Next" toggle here, standing in for any of the many other <c>DocumentParagraphFormattingCoordinator</c>
    /// commands that route through the same <see cref="SetParagraphFormattingCommand"/>) must leave an
    /// untouched paragraph's foreign numbering alone — the fix must not overreach into formatting edits that
    /// never decided the paragraph's list state.
    /// </summary>
    [Fact]
    public void ForeignNumbering_UnrelatedFormattingEdit_KeepsPreservedNumbering_StillRoundTrips()
    {
        var read = ReadDoc(AuthorForeignNumberingPackage());
        var paragraph = read.Blocks.OfType<Paragraph>().First();
        paragraph.PreservedNumbering.Should().NotBeNull();

        var context = new TestCommandContext(read);
        var command = new SetParagraphFormattingCommand(0, paragraph.Formatting with { KeepWithNext = true });
        command.Apply(context);

        paragraph.Formatting.ListKind.Should().Be(ListKind.None);
        paragraph.Formatting.KeepWithNext.Should().BeTrue();
        paragraph.PreservedNumbering.Should().NotBeNull("an edit that never changed ListKind is not a list decision");
        paragraph.PreservedNumbering!.Value.NumId.Should().Be(12);

        var rewritten = WriteBytes(read);
        var p0 = EntryXml(rewritten, "word/document.xml").Root!.Element(W + "body")!.Elements(W + "p").First();
        p0.Element(W + "pPr")!.Element(W + "numPr").Should().NotBeNull(
            "the foreign numbering is still untouched by any list decision and must keep round-tripping");
    }

    /// <summary>
    /// Adjacent-case check for the fix: undoing the "turn list on" step must restore the ORIGINAL foreign
    /// numbering along with the formatting, not leave the paragraph permanently stripped of it. A fix that
    /// cleared PreservedNumbering without snapshotting it for undo would make Ctrl+Z on this action strictly
    /// worse than before the fix (silent data loss on undo, instead of only on redundant save).
    /// </summary>
    [Fact]
    public void ForeignNumbering_UndoAfterListToggle_RestoresOriginalPreservedNumbering()
    {
        var read = ReadDoc(AuthorForeignNumberingPackage());
        var paragraph = read.Blocks.OfType<Paragraph>().First();
        var original = paragraph.PreservedNumbering;
        original.Should().NotBeNull();

        var context = new TestCommandContext(read);
        var enable = new SetParagraphFormattingCommand(0, paragraph.Formatting with { ListKind = ListKind.Bullet });
        enable.Apply(context);
        paragraph.PreservedNumbering.Should().BeNull();

        enable.Revert(context);
        paragraph.Formatting.ListKind.Should().Be(ListKind.None);
        paragraph.PreservedNumbering.Should().Be(original, "undo must restore the paragraph exactly, foreign numbering included");
    }
}
