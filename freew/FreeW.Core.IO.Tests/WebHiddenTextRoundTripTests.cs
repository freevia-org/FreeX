using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class WebHiddenTextRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void WebHiddenRun_WritesCanonicalProperty_AndSurvivesReopenAndSecondSave()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("web hidden", new RunFormatting
        {
            Strikethrough = true,
            Hidden = true,
            WebHidden = true,
            ColorHex = "#123456"
        }));
        paragraph.Runs.Add(new Run("visible", new RunFormatting { ColorHex = "#654321" }));
        document.Blocks.Add(paragraph);

        var firstBytes = Write(document);
        var firstRuns = Part(firstBytes, "word/document.xml").Descendants(W + "r").ToList();
        var properties = firstRuns[0].Element(W + "rPr")!;

        properties.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("strike", "vanish", "webHidden", "color");
        properties.Element(W + "webHidden")!
            .Should().BeEquivalentTo(new XElement(W + "webHidden"));
        firstRuns[1].Element(W + "rPr")!.Element(W + "webHidden").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.Paragraphs.Single().Runs.Select(run => run.Formatting.WebHidden)
            .Should().Equal(true, false);

        var secondBytes = Write(reopened);
        var secondRuns = Part(secondBytes, "word/document.xml").Descendants(W + "r").ToList();
        secondRuns[0].Element(W + "rPr")!.Element(W + "webHidden")!
            .Should().BeEquivalentTo(new XElement(W + "webHidden"));
        secondRuns[1].Element(W + "rPr")!.Element(W + "webHidden").Should().BeNull();
        Read(secondBytes).Paragraphs.Single().Runs.Select(run => run.Formatting.WebHidden)
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
            $"<w:r><w:rPr><w:webHidden{attribute}/></w:rPr><w:t>text</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.WebHidden.Should().Be(expected);
    }

    [Fact]
    public void ExplicitFalse_ReadsFalse_AndCanonicalSaveOmitsProperty()
    {
        var document = Read(AuthorDocument(
            "<w:r><w:rPr><w:webHidden w:val=\"0\"/></w:rPr><w:t>visible</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.WebHidden.Should().BeFalse();

        var savedRun = Part(Write(document), "word/document.xml").Descendants(W + "r").Single();
        savedRun.Element(W + "rPr")?.Element(W + "webHidden").Should().BeNull();
    }

    [Fact]
    public void DocDefaultsAndStyles_WriteAndRoundTripWebHidden_WhileFalsePathsOmitIt()
    {
        var document = new TextDocument
        {
            DefaultRun = new RunFormatting { FontFamily = "Aptos", FontSizePt = 11, WebHidden = true }
        };
        document.Styles["WebHiddenStyle"] = new DocumentStyle
        {
            Id = "WebHiddenStyle",
            Name = "Web Hidden Style",
            Run = new RunFormatting { Hidden = true, WebHidden = true, ColorHex = "#112233" }
        };
        document.Styles["VisibleStyle"] = new DocumentStyle
        {
            Id = "VisibleStyle",
            Name = "Visible Style",
            Run = RunFormatting.Default
        };
        document.Blocks.Add(new Paragraph("styled") { StyleId = "WebHiddenStyle" });

        var firstBytes = Write(document);
        var styles = Part(firstBytes, "word/styles.xml");
        var defaults = styles.Root!.Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!.Element(W + "rPr")!;
        var hiddenStyle = Style(styles, "WebHiddenStyle").Element(W + "rPr")!;

        defaults.Element(W + "webHidden")!
            .Should().BeEquivalentTo(new XElement(W + "webHidden"));
        hiddenStyle.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("vanish", "webHidden", "color");
        Style(styles, "VisibleStyle").Element(W + "rPr")?.Element(W + "webHidden").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.DefaultRun.WebHidden.Should().BeTrue();
        reopened.Styles["WebHiddenStyle"].Run.WebHidden.Should().BeTrue();
        reopened.Styles["VisibleStyle"].Run.WebHidden.Should().BeFalse();

        var secondStyles = Part(Write(reopened), "word/styles.xml");
        secondStyles.Root!.Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!.Element(W + "rPr")!
            .Element(W + "webHidden").Should().NotBeNull();
        Style(secondStyles, "WebHiddenStyle").Element(W + "rPr")!
            .Element(W + "webHidden").Should().NotBeNull();
        Style(secondStyles, "VisibleStyle").Element(W + "rPr")?
            .Element(W + "webHidden").Should().BeNull();
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
