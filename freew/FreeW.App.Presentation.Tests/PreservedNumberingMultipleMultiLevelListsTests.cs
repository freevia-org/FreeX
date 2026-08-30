using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// freew-styles-numbering F1: FreeW's native model has exactly ONE global
/// <see cref="MultiLevelListFormat"/> (see its doc comment), so a document that defines more than one
/// INDEPENDENT multilevel/outline numbering (two or more distinct <c>w:abstractNum</c> definitions, each
/// shaped as multilevel) used to have every list after the first silently collapse onto the first list's
/// number format, with sub-levels that never got an explicit restart anchor folding into the first list's
/// running count. <c>DocxReader.ReadNumbering</c> now maps only the FIRST distinct multilevel abstractNumId
/// onto the native model; every other, distinct multilevel abstractNumId is routed to the
/// <see cref="Paragraph.PreservedNumbering"/> / <see cref="PreservedParts.OriginalNumbering"/> path instead,
/// so it keeps its own format and its own counter — rendered by the real production consumer of that path,
/// <see cref="PreservedNumberingMarkerPlanner"/> (see also <c>DocumentPersistenceWorkflow.cs</c>'s
/// <c>DocxReader.Read</c> call on File&gt;Open and <c>FreeWOutputWorkflow.cs</c>'s <c>DocxWriter.Write</c> on
/// File&gt;Save).
/// <para>
/// Three independently-formatted outline lists are used throughout (not two) specifically so a fix that
/// only special-cases "the second list" (position) rather than routing by abstractNumId (identity) would
/// still fail: list C must keep ITS OWN format/counter too.
/// </para>
/// </summary>
public sealed class PreservedNumberingMultipleMultiLevelListsTests
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

    // Three independent multilevel/outline abstractNum definitions:
    //   abstractNumId 10 (numId 60) — List A: decimal outline "%1." / "%1.%2."
    //   abstractNumId 11 (numId 61) — List B: lettered outline "%1)" / "%1.%2)" (lowerLetter/lowerRoman)
    //   abstractNumId 12 (numId 62) — List C: roman outline "%1." / "%1.%2)" (upperRoman/upperLetter)
    // List A is the FIRST encountered and is the one FreeW's native single-slot model can represent; B and
    // C must each keep their own format/counter through the preservation path.
    private const string ThreeListNumberingXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:abstractNum w:abstractNumId="10">
            <w:multiLevelType w:val="multilevel"/>
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1.%2."/><w:lvlJc w:val="left"/></w:lvl>
          </w:abstractNum>
          <w:abstractNum w:abstractNumId="11">
            <w:multiLevelType w:val="multilevel"/>
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1)"/><w:lvlJc w:val="left"/></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerRoman"/><w:lvlText w:val="%1.%2)"/><w:lvlJc w:val="left"/></w:lvl>
          </w:abstractNum>
          <w:abstractNum w:abstractNumId="12">
            <w:multiLevelType w:val="multilevel"/>
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="upperLetter"/><w:lvlText w:val="%1.%2)"/><w:lvlJc w:val="left"/></w:lvl>
          </w:abstractNum>
          <w:num w:numId="60"><w:abstractNumId w:val="10"/></w:num>
          <w:num w:numId="61"><w:abstractNumId w:val="11"/></w:num>
          <w:num w:numId="62"><w:abstractNumId w:val="12"/></w:num>
        </w:numbering>
        """;

    private const string ThreeListDocumentBodyXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="60"/></w:numPr></w:pPr><w:r><w:t>A top 1</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="60"/></w:numPr></w:pPr><w:r><w:t>A sub 1</w:t></w:r></w:p>
            <w:p><w:r><w:t>separator 1</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="61"/></w:numPr></w:pPr><w:r><w:t>B top 1</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="61"/></w:numPr></w:pPr><w:r><w:t>B top 2</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="61"/></w:numPr></w:pPr><w:r><w:t>B sub 1</w:t></w:r></w:p>
            <w:p><w:r><w:t>separator 2</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="62"/></w:numPr></w:pPr><w:r><w:t>C top 1</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="62"/></w:numPr></w:pPr><w:r><w:t>C sub 1</w:t></w:r></w:p>
            <w:sectPr/>
          </w:body>
        </w:document>
        """;

    private static byte[] AuthorThreeListPackage()
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
            Add("word/document.xml", ThreeListDocumentBodyXml);
            Add("word/numbering.xml", ThreeListNumberingXml);
        }
        return stream.ToArray();
    }

    [Fact]
    public void ThreeIndependentMultiLevelLists_EachKeepsOwnFormatAndOwnRestartedCounter()
    {
        var docx = AuthorThreeListPackage();
        var doc = ReadDoc(docx);
        var paras = doc.Blocks.OfType<Paragraph>().ToList();
        // Index into paras by their authored text, skipping the two plain separator paragraphs.
        var aTop = paras[0];
        var aSub = paras[1];
        var bTop1 = paras[3];
        var bTop2 = paras[4];
        var bSub1 = paras[5];
        var cTop1 = paras[7];
        var cSub1 = paras[8];

        // --- List A (first encountered) maps onto FreeW's single native multilevel model, as before. ---
        aTop.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        aSub.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        aTop.PreservedNumbering.Should().BeNull();
        aSub.PreservedNumbering.Should().BeNull();
        doc.MultiLevelList.NumberFormats[0].Should().Be(ListNumberFormat.Decimal);
        doc.MultiLevelList.NumberFormats[1].Should().Be(ListNumberFormat.Decimal);
        doc.MultiLevelList.LevelTexts[0].Should().Be("%1.");
        doc.MultiLevelList.LevelTexts[1].Should().Be("%1.%2.");

        // --- Lists B and C are each a SECOND/THIRD, distinct multilevel abstractNum: routed to the
        // preservation path instead of being folded into list A's single global format/counter. ---
        foreach (var p in new[] { bTop1, bTop2, bSub1, cTop1, cSub1 })
            p.Formatting.ListKind.Should().Be(ListKind.None);

        bTop1.PreservedNumbering.Should().Be(new PreservedNumbering(61, 0));
        bTop2.PreservedNumbering.Should().Be(new PreservedNumbering(61, 0));
        bSub1.PreservedNumbering.Should().Be(new PreservedNumbering(61, 1));
        cTop1.PreservedNumbering.Should().Be(new PreservedNumbering(62, 0));
        cSub1.PreservedNumbering.Should().Be(new PreservedNumbering(62, 1));

        // --- The real production renderer for the preservation path must show each list in ITS OWN
        // format, with each list's own counter starting fresh (not continuing list A's, nor list B's for
        // list C) — this is what "restarts its own counter" means in practice. ---
        var plan = PreservedNumberingMarkerPlanner.BuildByParagraph(doc);
        plan.Should().NotContainKey(aTop, "a native MultiLevel paragraph is not part of the preserved-numbering path");
        plan.Should().NotContainKey(aSub);

        plan[bTop1].Text.Should().Be("a)");
        plan[bTop2].Text.Should().Be("b)");
        plan[bSub1].Text.Should().Be("b.i)");
        plan[cTop1].Text.Should().Be("I.");
        plan[cSub1].Text.Should().Be("I.A)");

        // --- Round trip: write, then re-read. Both lists must still exist as SEPARATE preserved
        // abstractNum/num definitions (not merged with each other or with list A), and the renderer must
        // reproduce the exact same markers from the re-read document. ---
        var rewritten = WriteBytes(doc);
        var numbering = EntryXml(rewritten, "word/numbering.xml").Root!;
        var abstracts = numbering.Elements(W + "abstractNum").ToList();

        string? NumFmtAt(XElement abstractNum, int ilvl) => abstractNum.Elements(W + "lvl")
            .First(l => l.Attribute(W + "ilvl")!.Value == ilvl.ToString())
            .Element(W + "numFmt")!.Attribute(W + "val")!.Value;

        var preservedAbstracts = abstracts
            .Where(a => int.Parse(a.Attribute(W + "abstractNumId")!.Value) >= 3)
            .ToList();
        // Both list B's and list C's own definitions survived the round trip, each with its own format —
        // neither collapsed into the other nor into list A's decimal outline.
        var listBAbstract = preservedAbstracts.FirstOrDefault(a => NumFmtAt(a, 0) == "lowerLetter" && NumFmtAt(a, 1) == "lowerRoman");
        var listCAbstract = preservedAbstracts.FirstOrDefault(a => NumFmtAt(a, 0) == "upperRoman" && NumFmtAt(a, 1) == "upperLetter");
        listBAbstract.Should().NotBeNull("list B's lowerLetter/lowerRoman definition must survive the round trip");
        listCAbstract.Should().NotBeNull("list C's upperRoman/upperLetter definition must survive the round trip");
        // List B and list C are DISTINCT preserved definitions, not merged with each other.
        listBAbstract!.Attribute(W + "abstractNumId")!.Value.Should().NotBe(listCAbstract!.Attribute(W + "abstractNumId")!.Value);

        var reread = ReadDoc(rewritten);
        var rereadParas = reread.Blocks.OfType<Paragraph>().ToList();
        var reBTop1 = rereadParas[3];
        var reBTop2 = rereadParas[4];
        var reBSub1 = rereadParas[5];
        var reCTop1 = rereadParas[7];
        var reCSub1 = rereadParas[8];

        // List B's and list C's remapped preserved numIds must still be DISTINCT from each other (identity
        // preserved across the round trip, not just position).
        reBTop1.PreservedNumbering.Should().NotBeNull();
        reCTop1.PreservedNumbering.Should().NotBeNull();
        reBTop1.PreservedNumbering!.Value.NumId.Should().Be(reBTop2.PreservedNumbering!.Value.NumId);
        reBTop1.PreservedNumbering!.Value.NumId.Should().NotBe(reCTop1.PreservedNumbering!.Value.NumId);

        var replan = PreservedNumberingMarkerPlanner.BuildByParagraph(reread);
        replan[reBTop1].Text.Should().Be("a)");
        replan[reBTop2].Text.Should().Be("b)");
        replan[reBSub1].Text.Should().Be("b.i)");
        replan[reCTop1].Text.Should().Be("I.");
        replan[reCSub1].Text.Should().Be("I.A)");
    }
}
