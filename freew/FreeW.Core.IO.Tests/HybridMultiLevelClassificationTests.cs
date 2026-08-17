using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R140 fix (freew-numbering-1): <c>DocxReader.IsMultiLevel</c> only recognized the literal
/// <c>w:multiLevelType="multilevel"</c> value, so an abstract numbering definition using Word's actual
/// "Multilevel List" gallery value — <c>hybridMultilevel</c> — with each level independently (not
/// accumulating-ly) formatted was misclassified as a flat <see cref="ListKind.Number"/>/<see
/// cref="ListKind.Bullet"/> list. On screen every sub-level rendered with FreeW's flat decimal counter
/// instead of the letter/roman format Word declared per level, and on save <c>DocxWriter</c> re-emitted the
/// paragraph against FreeW's own fixed flat-decimal abstractNum (<c>NumberNumId</c>), permanently discarding
/// the original multi-level definition.
///
/// Word also stamps <c>hybridMultilevel</c> on perfectly ordinary FLAT bullet/number lists (see
/// <see cref="NumberingInstanceRestartTests"/>'s fixtures, which use exactly that value for a single-level
/// list), so the fix must not treat every hybridMultilevel abstract as multi-level — only ones that actually
/// define more than one level.
/// </summary>
public sealed class HybridMultiLevelClassificationTests
{
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
    /// THE FIX: a real Word "Multilevel List" gallery definition ("1. / a) / i)" shape) declares
    /// w:multiLevelType="hybridMultilevel" (not "multilevel") and gives each level its OWN, independent
    /// (non-accumulating) lvlText — level 0 is "%1.", level 1 is "%2)", not "%1.%2)". Before the fix neither
    /// branch of IsMultiLevel recognized this: the literal-value check only matched "multilevel", and the
    /// %1+%2 fallback requires an ACCUMULATING level-1 lvlText, which this independently-formatted style
    /// never has. Both paragraphs must be classified MultiLevel, and the per-level number formats (decimal /
    /// lowerLetter / lowerRoman) must be captured into the document's multilevel format table rather than
    /// being discarded.
    /// </summary>
    [Fact]
    public void AbstractNum_HybridMultilevel_IndependentLvlText_IsClassifiedMultiLevel()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="7">
                    <w:multiLevelType w:val="hybridMultilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%2)"/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="lowerRoman"/><w:lvlText w:val="%3)"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="30"><w:abstractNumId w:val="7"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="30"/></w:numPr></w:pPr><w:r><w:t>Top level</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="30"/></w:numPr></w:pPr><w:r><w:t>Sub level</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        var top = paragraphs.Single(p => p.Runs.Any(r => r.Text == "Top level"));
        var sub = paragraphs.Single(p => p.Runs.Any(r => r.Text == "Sub level"));

        top.Formatting.ListKind.Should().Be(ListKind.MultiLevel,
            "a hybridMultilevel abstract that defines more than one level is a genuine multi-level list, " +
            "matching how Word's own Multilevel List gallery templates are declared");
        sub.Formatting.ListKind.Should().Be(ListKind.MultiLevel);

        doc.MultiLevelList.GetNumberFormat(0).Should().Be(ListNumberFormat.Decimal);
        doc.MultiLevelList.GetNumberFormat(1).Should().Be(ListNumberFormat.LowerLetter,
            "the declared per-level numFmt must be captured, not discarded, once the list is correctly " +
            "recognized as multi-level");
        doc.MultiLevelList.GetNumberFormat(2).Should().Be(ListNumberFormat.LowerRoman);

        // Writer side: the list must round-trip as MultiLevel, not flatten back to a Number-kind list
        // against FreeW's own fixed flat-decimal abstractNum (which would permanently lose the letter/roman
        // sub-level formats).
        var rewritten = ReadDoc(WriteBytes(doc));
        var rewrittenParagraphs = rewritten.Blocks.OfType<Paragraph>().ToList();
        rewrittenParagraphs.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.MultiLevel,
            "the multi-level classification, once correctly detected on read, must survive a save+reload " +
            "round trip instead of reverting to a flat numbered list");
    }

    /// <summary>
    /// SIBLING / no-regression coverage: Word also stamps w:multiLevelType="hybridMultilevel" on perfectly
    /// ordinary FLAT numbered lists that only ever define level 0 (this is the shape
    /// <see cref="NumberingInstanceRestartTests"/>'s fixtures already rely on as "the representative
    /// real-world value"). A single-level hybridMultilevel abstract must NOT be swept into MultiLevel by
    /// this fix — it has nothing to be multi-level about, and misclassifying it would corrupt the
    /// (unrelated, document-global) MultiLevelListFormat table and re-author the list against FreeW's
    /// multi-level numbering scheme instead of its flat one.
    /// </summary>
    [Fact]
    public void AbstractNum_HybridMultilevel_SingleLevel_StaysClassifiedNumber()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="7">
                    <w:multiLevelType w:val="hybridMultilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="30"><w:abstractNumId w:val="7"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="30"/></w:numPr></w:pPr><w:r><w:t>Flat one</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="30"/></w:numPr></w:pPr><w:r><w:t>Flat two</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);
        doc.Blocks.OfType<Paragraph>().Should().OnlyContain(
            p => p.Formatting.ListKind == ListKind.Number,
            because: "a hybridMultilevel abstract that only ever defines level 0 is an ordinary flat " +
                     "numbered list, not a multi-level one");
    }
}
