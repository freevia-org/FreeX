using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R143 fix (freew-numbering-lvltext-discarded): <c>DocxReader.ReadMultiLevelNumberFormats</c> only ever
/// captured each level's <c>w:numFmt</c> token, discarding <c>w:lvlText</c> entirely; <c>MultiLevelListMarkerState.Advance</c>
/// then unconditionally built every marker as an accumulated "N.N.N." string with a hardcoded '.' separator.
/// A real-world outline/legal list whose lvlText uses a different separator, prefix, or suffix (e.g. Word's
/// own "1) a) i)" Multilevel List gallery style, captured here as <c>%1)</c> / <c>%1.%2)</c>) therefore
/// rendered with the wrong marker text even though the document's own numbering.xml fully specifies it.
/// </summary>
public sealed class MultiLevelListLvlTextTests
{
    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
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
    /// THE FIX: a genuine Word "Multilevel List" gallery definition whose level 0 lvlText is "%1)" (not
    /// "%1.") and whose level 1 lvlText already accumulates BOTH ancestor placeholders with its own
    /// separator ("%1.%2)", not "%2)" alone -- the real OOXML shape). Reading it must capture these exact
    /// patterns into <see cref="MultiLevelListFormat.LevelTexts"/>, and rendering through the shared
    /// production formatter (<see cref="MultiLevelListMarkerFormatter"/>, the same class every
    /// on-screen/PDF/accessibility marker in both shells goes through via
    /// <c>DocumentListMarkerSequencePlanner</c>) must produce "1)" / "1.1)", not the old hardcoded "1." /
    /// "1.1.".
    /// </summary>
    [Fact]
    public void MultiLevelAbstractNum_CapturesLvlTextPattern_AndRendersItInsteadOfHardcodedDots()
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

        var doc = ReadDoc(docx);

        doc.MultiLevelList.LevelTexts[0].Should().Be("%1)",
            "the document's own level-0 lvlText must be captured, not discarded");
        doc.MultiLevelList.LevelTexts[1].Should().Be("%1.%2)",
            "level 1's lvlText already encodes both ancestor placeholders with its own separator");

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1],
            doc.MultiLevelList.NumberFormats,
            doc.MultiLevelList.LevelTexts);

        markers.Should().Equal(
            new[] { "1)", "1.1)" },
            "the captured lvlText pattern must be rendered verbatim, not replaced with the hardcoded " +
            "accumulated \"N.N.\" dotted pattern");
    }

    /// <summary>
    /// SIBLING / no-regression coverage: an abstractNum whose levels carry no lvlText at all (only
    /// numFmt) -- and FreeW's own "Define new Multilevel list" dialog output, which never populates
    /// LevelTexts -- must keep rendering the classic accumulated dotted outline unchanged.
    /// </summary>
    [Fact]
    public void MultiLevelAbstractNum_WithoutLvlText_StillRendersDefaultDottedPattern()
    {
        var docx = AuthorPackage(
            numberingXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="11">
                    <w:multiLevelType w:val="multilevel"/>
                    <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlJc w:val="left"/></w:lvl>
                    <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlJc w:val="left"/></w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="60"><w:abstractNumId w:val="11"/></w:num>
                </w:numbering>
                """,
            documentBodyXml:
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="60"/></w:numPr></w:pPr><w:r><w:t>Top</w:t></w:r></w:p>
                    <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="60"/></w:numPr></w:pPr><w:r><w:t>Sub</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

        var doc = ReadDoc(docx);

        doc.MultiLevelList.LevelTexts[0].Should().BeNull();
        doc.MultiLevelList.LevelTexts[1].Should().BeNull();

        var markers = MultiLevelListMarkerFormatter.MarkerSequence(
            [0, 1],
            doc.MultiLevelList.NumberFormats,
            doc.MultiLevelList.LevelTexts);

        markers.Should().Equal("1.", "1.1.");
    }
}
