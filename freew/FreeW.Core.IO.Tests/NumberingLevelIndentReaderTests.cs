using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A list item takes the indent of its numbering level (<c>w:lvl/w:pPr/w:ind</c>) when it has no direct indent,
/// and Word's automatic (HTML) paragraph spacing collapses between two items of one list but not where a list
/// meets other text.
/// </summary>
public class NumberingLevelIndentReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Numbering =
        $"<w:numbering xmlns:w=\"{Wns}\">" +
        "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/>" +
        "<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/>" +
        "<w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/>" +
        "<w:pPr><w:ind w:left=\"1080\" w:hanging=\"540\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>" +
        "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>" +
        "</w:numbering>";

    private static TextDocument Read(string bodyXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", $"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
            Add(zip, "word/styles.xml", $"<w:styles xmlns:w=\"{Wns}\"/>");
            Add(zip, "word/numbering.xml", Numbering);
        }

        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    private static string Item(int numId, int level, string text, string extra = "") =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr>{extra}</w:pPr>" +
        $"<w:r><w:t>{text}</w:t></w:r></w:p>";

    [Fact]
    public void MappedListItem_CapturesItsLevelIndent()
    {
        var doc = Read(Item(1, 0, "a") + Item(1, 1, "b"));
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        Assert.Equal(36, paragraphs[0].Formatting.ListLevelIndentLeftPt);
        Assert.Equal(-18, paragraphs[0].Formatting.ListLevelFirstLineIndentPt);
        Assert.Equal(72, paragraphs[1].Formatting.ListLevelIndentLeftPt);
    }

    [Fact]
    public void PreservedListItem_CapturesItsLevelIndentToo()
    {
        // numId 2 is a second independent multilevel definition, so FreeW preserves rather than maps it.
        var doc = Read(Item(1, 0, "a") + Item(2, 0, "b"));
        var preserved = doc.Blocks.OfType<Paragraph>().Last();

        Assert.Equal(ListKind.None, preserved.Formatting.ListKind);
        Assert.Equal(54, preserved.Formatting.ListLevelIndentLeftPt);
        Assert.Equal(-27, preserved.Formatting.ListLevelFirstLineIndentPt);
    }

    [Fact]
    public void NonListParagraph_HasNoLevelIndent()
    {
        var doc = Read("<w:p><w:r><w:t>plain</w:t></w:r></w:p>");
        var formatting = doc.Blocks.OfType<Paragraph>().Single().Formatting;

        Assert.Null(formatting.ListLevelIndentLeftPt);
        Assert.Null(formatting.ListLevelFirstLineIndentPt);
    }

    private const string Auto = "<w:spacing w:before=\"100\" w:beforeAutospacing=\"1\" w:after=\"100\" w:afterAutospacing=\"1\"/>";

    [Fact]
    public void AutoSpacing_IsDroppedBetweenItemsOfOneList()
    {
        var doc = Read(Item(1, 0, "a", Auto) + Item(1, 0, "b", Auto));
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        Assert.Equal(14, paragraphs[0].Formatting.SpaceBeforePt);
        Assert.Equal(0, paragraphs[0].Formatting.SpaceAfterPt);
        Assert.Equal(0, paragraphs[1].Formatting.SpaceBeforePt);
        Assert.Equal(14, paragraphs[1].Formatting.SpaceAfterPt);
    }

    [Fact]
    public void AutoSpacing_CollapsesToOneSpaceBetweenAListAndOtherText()
    {
        var plain = $"<w:p><w:pPr>{Auto}</w:pPr><w:r><w:t>intro</w:t></w:r></w:p>";
        var doc = Read(plain + Item(1, 0, "a", Auto));
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        // The space survives once (after the intro) instead of vanishing on both sides.
        Assert.Equal(14, paragraphs[0].Formatting.SpaceAfterPt);
        Assert.Equal(0, paragraphs[1].Formatting.SpaceBeforePt);
    }
}
