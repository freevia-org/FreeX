using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class SoftHyphenRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void ManualSoftHyphen_SurvivesUnrelatedEditAndTwoWrites()
    {
        var document = new TextDocument();
        var hyphenatedParagraph = new Paragraph();
        hyphenatedParagraph.Runs.Add(new Run("hy" + Hyphenator.SoftHyphen + "phenation"));
        document.Blocks.Add(hyphenatedParagraph);
        document.Blocks.Add(new Paragraph("Original body text"));

        var source = Write(document);
        AssertSoftHyphenPackage(source);

        var imported = DocxReader.Read(new MemoryStream(source));
        var paragraphs = imported.Paragraphs.ToList();
        paragraphs[0].Runs.Single().Text.Should().Be("hy" + Hyphenator.SoftHyphen + "phenation");
        paragraphs[1].Runs[0].Text = "Edited body text";

        var firstWrite = Write(imported);
        AssertSoftHyphenPackage(firstWrite);
        var reopened = DocxReader.Read(new MemoryStream(firstWrite));
        reopened.Paragraphs.First().Runs.Single().Text
            .Should().Be("hy" + Hyphenator.SoftHyphen + "phenation");

        var secondWrite = Write(reopened);
        AssertSoftHyphenPackage(secondWrite);
        DocxReader.Read(new MemoryStream(secondWrite)).Paragraphs.First().Runs.Single().Text
            .Should().Be("hy" + Hyphenator.SoftHyphen + "phenation");
    }

    [Fact]
    public void PlainTextRun_DoesNotGainSoftHyphenElement()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("hyphenation"));

        var xml = EntryXml(Write(document), "word/document.xml");

        xml.Descendants(W + "softHyphen").Should().BeEmpty();
        xml.Descendants(W + "t").Should().ContainSingle().Which.Value.Should().Be("hyphenation");
    }

    private static void AssertSoftHyphenPackage(byte[] docx)
    {
        var run = EntryXml(docx, "word/document.xml").Descendants(W + "r").First();
        run.Elements().Where(element => element.Name != W + "rPr")
            .Select(element => element.Name.LocalName)
            .Should().Equal("t", "softHyphen", "t");
        run.Elements(W + "t").Select(element => element.Value)
            .Should().Equal("hy", "phenation");
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = archive.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }
}
