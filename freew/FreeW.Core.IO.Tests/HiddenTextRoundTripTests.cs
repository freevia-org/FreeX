using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class HiddenTextRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void HiddenRun_WritesCanonicalVanish_AndSurvivesReopenAndSecondSave()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("hidden", new RunFormatting
        {
            Strikethrough = true,
            Hidden = true,
            ColorHex = "#123456"
        }));
        paragraph.Runs.Add(new Run("visible", new RunFormatting { ColorHex = "#654321" }));
        document.Blocks.Add(paragraph);

        var firstBytes = Write(document);
        var firstXml = Part(firstBytes, "word/document.xml");
        var firstRuns = firstXml.Descendants(W + "r").ToList();
        var hiddenProperties = firstRuns[0].Element(W + "rPr")!;
        var names = hiddenProperties.Elements().Select(element => element.Name.LocalName).ToList();

        names.Should().ContainInOrder("strike", "vanish", "color");
        hiddenProperties.Element(W + "vanish")!.Should().BeEquivalentTo(new XElement(W + "vanish"));
        firstRuns[1].Element(W + "rPr")!.Element(W + "vanish").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.Paragraphs.Single().Runs.Select(run => run.Formatting.Hidden)
            .Should().Equal(true, false);

        var secondBytes = Write(reopened);
        var secondRuns = Part(secondBytes, "word/document.xml").Descendants(W + "r").ToList();
        secondRuns[0].Element(W + "rPr")!.Element(W + "vanish")!
            .Should().BeEquivalentTo(new XElement(W + "vanish"));
        secondRuns[1].Element(W + "rPr")!.Element(W + "vanish").Should().BeNull();
        Read(secondBytes).Paragraphs.Single().Runs.Select(run => run.Formatting.Hidden)
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
            $"<w:r><w:rPr><w:vanish{attribute}/></w:rPr><w:t>text</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.Hidden.Should().Be(expected);
    }

    [Fact]
    public void ExplicitFalse_ReadsFalse_AndCanonicalSaveOmitsVanish()
    {
        var document = Read(AuthorDocument(
            "<w:r><w:rPr><w:vanish w:val=\"0\"/></w:rPr><w:t>visible</w:t></w:r>"));

        document.Paragraphs.Single().Runs.Single().Formatting.Hidden.Should().BeFalse();

        var savedRun = Part(Write(document), "word/document.xml").Descendants(W + "r").Single();
        savedRun.Element(W + "rPr")?.Element(W + "vanish").Should().BeNull();
    }

    [Fact]
    public void DocDefaultsAndStyles_WriteAndRoundTripHidden_WhileFalsePathsOmitIt()
    {
        var document = new TextDocument
        {
            DefaultRun = new RunFormatting { FontFamily = "Aptos", FontSizePt = 11, Hidden = true }
        };
        document.Styles["HiddenStyle"] = new DocumentStyle
        {
            Id = "HiddenStyle",
            Name = "Hidden Style",
            Run = new RunFormatting { Strikethrough = true, Hidden = true, ColorHex = "#112233" }
        };
        document.Styles["VisibleStyle"] = new DocumentStyle
        {
            Id = "VisibleStyle",
            Name = "Visible Style",
            Run = RunFormatting.Default
        };
        document.Blocks.Add(new Paragraph("styled") { StyleId = "HiddenStyle" });

        var firstBytes = Write(document);
        var styles = Part(firstBytes, "word/styles.xml");
        var defaultProperties = styles.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;
        var hiddenStyleProperties = Style(styles, "HiddenStyle").Element(W + "rPr")!;

        defaultProperties.Element(W + "vanish")!.Should().BeEquivalentTo(new XElement(W + "vanish"));
        hiddenStyleProperties.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("strike", "vanish", "color");
        Style(styles, "VisibleStyle").Element(W + "rPr")?.Element(W + "vanish").Should().BeNull();

        var reopened = Read(firstBytes);
        reopened.DefaultRun.Hidden.Should().BeTrue();
        reopened.Styles["HiddenStyle"].Run.Hidden.Should().BeTrue();
        reopened.Styles["VisibleStyle"].Run.Hidden.Should().BeFalse();

        var secondStyles = Part(Write(reopened), "word/styles.xml");
        secondStyles.Root!.Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!.Element(W + "rPr")!
            .Element(W + "vanish").Should().NotBeNull();
        Style(secondStyles, "HiddenStyle").Element(W + "rPr")!
            .Element(W + "vanish").Should().NotBeNull();
        Style(secondStyles, "VisibleStyle").Element(W + "rPr")?.Element(W + "vanish").Should().BeNull();
    }

    [Fact]
    public void DirectFalse_DoesNotEraseInheritedStyleOrDocumentDefaultState()
    {
        var bytes = AuthorDocument(
            "<w:r><w:rPr><w:vanish w:val=\"false\"/></w:rPr><w:t>styled</w:t></w:r>",
            paragraphProperties: "<w:pPr><w:pStyle w:val=\"Child\"/></w:pPr>",
            stylesBody:
                """
                <w:docDefaults><w:rPrDefault><w:rPr><w:vanish/></w:rPr></w:rPrDefault></w:docDefaults>
                <w:style w:type="paragraph" w:styleId="Base"><w:name w:val="Base"/><w:rPr><w:vanish/></w:rPr></w:style>
                <w:style w:type="paragraph" w:styleId="Child"><w:name w:val="Child"/><w:basedOn w:val="Base"/><w:rPr><w:vanish w:val="0"/></w:rPr></w:style>
                """);

        var document = Read(bytes);

        document.DefaultRun.Hidden.Should().BeTrue();
        document.Styles["Base"].Run.Hidden.Should().BeTrue();
        document.Styles["Child"].Run.Hidden.Should().BeFalse();
        document.Paragraphs.Single().Runs.Single().Formatting.Hidden.Should().BeFalse(
            "false is the absence of a direct toggle in FreeW's existing non-nullable bool model");

        var saved = Write(document);
        Part(saved, "word/document.xml").Descendants(W + "rPr").SingleOrDefault()?
            .Element(W + "vanish").Should().BeNull();
        Style(Part(saved, "word/styles.xml"), "Base").Element(W + "rPr")!
            .Element(W + "vanish").Should().NotBeNull();
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

    private static byte[] AuthorDocument(
        string runXml,
        string paragraphProperties = "",
        string? stylesBody = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            static void Add(ZipArchive archive, string path, string xml)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(xml);
            }

            Add(archive, "word/document.xml",
                $"""
                <w:document xmlns:w="{W.NamespaceName}">
                  <w:body><w:p>{paragraphProperties}{runXml}</w:p></w:body>
                </w:document>
                """);
            if (stylesBody is not null)
            {
                Add(archive, "word/styles.xml",
                    $"<w:styles xmlns:w=\"{W.NamespaceName}\">{stylesBody}</w:styles>");
            }
        }

        return stream.ToArray();
    }
}
