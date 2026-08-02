using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class DoubleStrikethroughRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void DoubleStrikethroughRun_WritesCanonicalPropertyInSchemaOrder_AndSurvivesReopenAndSecondSave()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("double", new RunFormatting
        {
            Strikethrough = true,
            DoubleStrikethrough = true,
            NoProof = true,
            ColorHex = "#123456"
        }));
        paragraph.Runs.Add(new Run("single", new RunFormatting
        {
            Strikethrough = true,
            ColorHex = "#654321"
        }));
        document.Blocks.Add(paragraph);

        var firstBytes = Write(document);
        var firstRuns = Part(firstBytes, "word/document.xml").Descendants(W + "r").ToList();
        var properties = firstRuns[0].Element(W + "rPr")!;

        properties.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("strike", "dstrike", "noProof", "color");
        properties.Element(W + "dstrike")!
            .Should().BeEquivalentTo(new XElement(W + "dstrike"));
        firstRuns[1].Element(W + "rPr")!.Element(W + "strike").Should().NotBeNull();
        firstRuns[1].Element(W + "rPr")!.Element(W + "dstrike").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.Paragraphs.Single().Runs.Select(run => run.Formatting.DoubleStrikethrough)
            .Should().Equal(true, false);
        reopened.Paragraphs.Single().Runs.Select(run => run.Formatting.Strikethrough)
            .Should().Equal(true, true);

        var secondBytes = Write(reopened);
        var secondRuns = Part(secondBytes, "word/document.xml").Descendants(W + "r").ToList();
        secondRuns[0].Element(W + "rPr")!.Element(W + "dstrike")!
            .Should().BeEquivalentTo(new XElement(W + "dstrike"));
        secondRuns[1].Element(W + "rPr")!.Element(W + "dstrike").Should().BeNull();
        Read(secondBytes).Paragraphs.Single().Runs.Select(run => run.Formatting.DoubleStrikethrough)
            .Should().Equal(true, false);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void Reader_AcceptsAllWordOnOffForms(string? value, bool expected)
    {
        var attribute = value is null ? string.Empty : $" w:val=\"{value}\"";
        var document = Read(AuthorDocument(
            $"<w:r><w:rPr><w:dstrike{attribute}/></w:rPr><w:t>text</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.DoubleStrikethrough.Should().Be(expected);
    }

    [Fact]
    public void ExplicitFalse_ReadsFalse_AndCanonicalSaveOmitsProperty()
    {
        var document = Read(AuthorDocument(
            "<w:r><w:rPr><w:dstrike w:val=\"0\"/></w:rPr><w:t>single</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.DoubleStrikethrough.Should().BeFalse();

        var savedRun = Part(Write(document), "word/document.xml").Descendants(W + "r").Single();
        savedRun.Element(W + "rPr")?.Element(W + "dstrike").Should().BeNull();
    }

    [Fact]
    public void DocDefaultsAndStyles_WriteAndRoundTripDoubleStrikethrough_WhileOrdinaryStrikeStaysDistinct()
    {
        var document = new TextDocument
        {
            DefaultRun = new RunFormatting
            {
                FontFamily = "Aptos",
                FontSizePt = 11,
                DoubleStrikethrough = true
            }
        };
        document.Styles["DoubleStrikeStyle"] = new DocumentStyle
        {
            Id = "DoubleStrikeStyle",
            Name = "Double Strike Style",
            Run = new RunFormatting
            {
                Strikethrough = true,
                DoubleStrikethrough = true,
                NoProof = true,
                ColorHex = "#112233"
            }
        };
        document.Styles["SingleStrikeStyle"] = new DocumentStyle
        {
            Id = "SingleStrikeStyle",
            Name = "Single Strike Style",
            Run = new RunFormatting { Strikethrough = true }
        };
        document.Blocks.Add(new Paragraph("double") { StyleId = "DoubleStrikeStyle" });

        var firstBytes = Write(document);
        var styles = Part(firstBytes, "word/styles.xml");
        var defaults = styles.Root!.Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!.Element(W + "rPr")!;
        var doubleStyle = Style(styles, "DoubleStrikeStyle").Element(W + "rPr")!;
        var singleStyle = Style(styles, "SingleStrikeStyle").Element(W + "rPr")!;

        defaults.Element(W + "dstrike")!
            .Should().BeEquivalentTo(new XElement(W + "dstrike"));
        doubleStyle.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("strike", "dstrike", "noProof", "color");
        singleStyle.Element(W + "strike").Should().NotBeNull();
        singleStyle.Element(W + "dstrike").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.DefaultRun.DoubleStrikethrough.Should().BeTrue();
        reopened.Styles["DoubleStrikeStyle"].Run.DoubleStrikethrough.Should().BeTrue();
        reopened.Styles["SingleStrikeStyle"].Run.Strikethrough.Should().BeTrue();
        reopened.Styles["SingleStrikeStyle"].Run.DoubleStrikethrough.Should().BeFalse();

        var secondStyles = Part(Write(reopened), "word/styles.xml");
        secondStyles.Root!.Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!.Element(W + "rPr")!
            .Element(W + "dstrike").Should().NotBeNull();
        Style(secondStyles, "DoubleStrikeStyle").Element(W + "rPr")!
            .Element(W + "dstrike").Should().NotBeNull();
        Style(secondStyles, "SingleStrikeStyle").Element(W + "rPr")!
            .Element(W + "dstrike").Should().BeNull();
    }

    private static XElement Style(XDocument styles, string id) => styles.Root!.Elements(W + "style")
        .Single(style => (string?)style.Attribute(W + "styleId") == id);

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static XDocument Part(byte[] bytes, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] AuthorDocument(string runXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"<w:document xmlns:w=\"{W.NamespaceName}\"><w:body><w:p>{runXml}</w:p></w:body></w:document>");
        }

        return stream.ToArray();
    }
}
