using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Sweep103 F1 fix: <c>DocxReader</c> captures each multilevel level's real <c>w:lvlText</c> pattern into
/// <see cref="MultiLevelListFormat.LevelTexts"/> (proven by <see cref="MultiLevelListLvlTextTests"/>), but
/// <c>DocxWriter.BuildNumbering</c>'s <c>MultiLevelAbstractNum</c> local function used to ignore
/// <c>LevelTexts</c> entirely and unconditionally rebuild every level as the hardcoded accumulated dotted
/// pattern ("%1.", "%1.%2.", ...). The read side made a foreign document look right the moment it opened,
/// so the loss was invisible until the file was saved and reopened: a document numbered "1)"/"1.1)" came
/// back "1."/"1.1." on the very next open. These tests start from a FOREIGN pattern (never FreeW's own
/// hardcoded shape), round-trip it through <see cref="DocxWriter.Write(TextDocument, Stream)"/> and back
/// through <see cref="DocxReader.Read"/>, and assert the pattern survived.
/// </summary>
public sealed class MultiLevelListLvlTextRoundTripTests
{
    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] WriteDoc(TextDocument document)
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
    /// THE FIX: a genuine Word "Multilevel List" gallery definition -- level 0 lvlText "%1)", level 1
    /// "%1.%2)" -- never FreeW's own hardcoded "%1."/"%1.%2." shape. Read, then save with NO edits, then
    /// reread: the pattern must survive. Before the fix, DocxWriter rebuilt both levels as the hardcoded
    /// dotted pattern regardless of what was read, so this assertion fails on the pre-fix writer even
    /// though the initial read (and on-screen render) was already correct -- which is exactly why the
    /// existing read-only test could not see this defect.
    /// </summary>
    [Fact]
    public void MultiLevelList_ForeignLvlTextPattern_SurvivesSaveAndReload()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="9">
                    <w:multiLevelType w:val="multilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1)"/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1.%2)"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="50"><w:abstractNumId w:val="9"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="50"/></w:numPr></w:pPr><w:r><w:t>Top</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="50"/></w:numPr></w:pPr><w:r><w:t>Sub</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var initial = ReadDoc(docx);
        initial.MultiLevelList.LevelTexts[0].Should().Be("%1)", "sanity: the read side already captures the real pattern");
        initial.MultiLevelList.LevelTexts[1].Should().Be("%1.%2)");

        var savedBytes = WriteDoc(initial);
        var reloaded = ReadDoc(savedBytes);

        reloaded.MultiLevelList.LevelTexts[0].Should().Be("%1)",
            "the document's own level-0 pattern must survive a save+reload, not collapse to the hardcoded \"%1.\"");
        reloaded.MultiLevelList.LevelTexts[1].Should().Be("%1.%2)",
            "the document's own level-1 pattern must survive a save+reload, not collapse to the hardcoded \"%1.%2.\"");

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1],
            reloaded.MultiLevelList.NumberFormats,
            reloaded.MultiLevelList.LevelTexts);
        markers.Should().Equal(
            new[] { "1)", "1.1)" },
            "the on-screen marker after reopening the saved file must still read the source document's own style");
    }

    /// <summary>
    /// SIBLING / no-regression coverage: a FreeW-authored multilevel list -- built directly via the model,
    /// exactly like "Define new Multilevel list" (which only ever sets a <see cref="ListNumberFormat"/> per
    /// level and never populates <see cref="MultiLevelListFormat.LevelTexts"/>) -- must keep rendering the
    /// classic accumulated dotted outline unchanged by the fix, including after a save+reload. (A round
    /// trip through DOCX always writes SOME literal lvlText, so <c>LevelTexts</c> itself is expected to come
    /// back as the non-null "%1."/"%1.%2." strings on reload both before and after this fix -- that is not
    /// a regression, since those strings render identically to the null/default case; the invariant this
    /// test actually guards is the rendered MARKER TEXT staying "1."/"1.1.".)
    /// </summary>
    [Fact]
    public void MultiLevelList_DefaultDottedPattern_StillRoundTripsAfterFix()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Top")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 }
        });
        doc.Blocks.Add(new Paragraph("Sub")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 1 }
        });

        doc.MultiLevelList.LevelTexts[0].Should().BeNull("a freshly-constructed model never captured any DOCX lvlText");
        doc.MultiLevelList.LevelTexts[1].Should().BeNull();

        var reloaded = ReadDoc(WriteDoc(doc));

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1],
            reloaded.MultiLevelList.NumberFormats,
            reloaded.MultiLevelList.LevelTexts);
        markers.Should().Equal(
            new[] { "1.", "1.1." },
            "FreeW's own authored multilevel lists must keep rendering the classic dotted outline after the fix");
    }
}
