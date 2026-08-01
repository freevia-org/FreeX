using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class RepeatingSectionContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    [Fact]
    public void Word2013RepeatingSection_PreservesNestedItemsPackageXmlAndReopenedModel()
    {
        using var source = BuildPackage(
            """
            <w:sdt>
              <w:sdtPr>
                <w:alias w:val="Order lines"/>
                <w:id w:val="100"/>
                <w:tag w:val="Orders"/>
                <w15:repeatingSection>
                  <w15:sectionTitle w:val="Line items"/>
                  <w15:doNotAllowInsertDeleteSection w:val="1"/>
                </w15:repeatingSection>
              </w:sdtPr>
              <w:sdtContent>
                <w:sdt>
                  <w:sdtPr>
                    <w:alias w:val="Order line 1"/>
                    <w:id w:val="101"/>
                    <w:tag w:val="Order1"/>
                    <w15:repeatingSectionItem/>
                  </w:sdtPr>
                  <w:sdtContent>
                    <w:p><w:r><w:t>First item</w:t></w:r></w:p>
                    <w:p><w:r><w:t>First item detail</w:t></w:r></w:p>
                  </w:sdtContent>
                </w:sdt>
                <w:sdt>
                  <w:sdtPr>
                    <w:alias w:val="Order line 2"/>
                    <w:id w:val="102"/>
                    <w:tag w:val="Order2"/>
                    <w15:repeatingSectionItem/>
                  </w:sdtPr>
                  <w:sdtContent>
                    <w:p><w:r><w:t>Second item</w:t></w:r></w:p>
                  </w:sdtContent>
                </w:sdt>
              </w:sdtContent>
            </w:sdt>
            """);

        var imported = DocxReader.Read(source);
        AssertModel(imported);

        var saved = Write(imported);
        var xml = ReadDocumentXml(saved);
        AssertCanonicalPackageXml(xml);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        AssertModel(reopened);
        AssertCanonicalPackageXml(ReadDocumentXml(Write(reopened)));
    }

    [Fact]
    public void OrdinaryContentControls_RemainFlatAndDoNotRequireWord2013Declarations()
    {
        var document = new TextDocument();
        var blockControl = new BlockContentControl(
            BlockContentControlKind.RichText,
            Tag: "OrdinaryBlock",
            Alias: "Ordinary block");
        document.Blocks.Add(new Paragraph("Block text") { BlockContentControl = blockControl });
        var inlineParagraph = new Paragraph();
        inlineParagraph.Runs.Add(Run.PlainTextControl("Inline text", tag: "OrdinaryInline"));
        document.Blocks.Add(inlineParagraph);

        var package = Write(document);
        var xml = ReadDocumentXml(package);

        xml.Root!.GetNamespaceOfPrefix("w15").Should().BeNull();
        xml.Root.GetNamespaceOfPrefix("mc").Should().BeNull();
        xml.Descendants(W15 + "repeatingSection").Should().BeEmpty();
        xml.Descendants(W15 + "repeatingSectionItem").Should().BeEmpty();
        xml.Root.Element(W + "body")!.Elements(W + "sdt").Should().ContainSingle();
        xml.Descendants(W + "sdt").Should().HaveCount(2);

        var reopened = DocxReader.Read(new MemoryStream(package));
        reopened.Blocks[0].BlockContentControl!.Kind.Should().Be(BlockContentControlKind.RichText);
        reopened.Blocks[0].BlockContentControl!.Parent.Should().BeNull();
        reopened.Paragraphs.ElementAt(1).Runs.Single().Control!.Kind.Should().Be(ContentControlKind.PlainText);
    }

    private static void AssertModel(TextDocument document)
    {
        document.Blocks.Select(block => ((Paragraph)block).PlainText).Should().Equal(
            "First item",
            "First item detail",
            "Second item");

        var firstItem = document.Blocks[0].BlockContentControl!;
        document.Blocks[1].BlockContentControl.Should().BeSameAs(firstItem);
        var secondItem = document.Blocks[2].BlockContentControl!;
        secondItem.Should().NotBeSameAs(firstItem);

        firstItem.Kind.Should().Be(BlockContentControlKind.RepeatingSectionItem);
        firstItem.Tag.Should().Be("Order1");
        firstItem.WordMetadata!.Id.Should().Be("101");
        secondItem.Kind.Should().Be(BlockContentControlKind.RepeatingSectionItem);
        secondItem.Tag.Should().Be("Order2");
        secondItem.WordMetadata!.Id.Should().Be("102");

        var section = firstItem.Parent!;
        secondItem.Parent.Should().BeSameAs(section);
        section.Kind.Should().Be(BlockContentControlKind.RepeatingSection);
        section.Tag.Should().Be("Orders");
        section.Alias.Should().Be("Order lines");
        section.WordMetadata!.Id.Should().Be("100");
        section.RepeatingSectionTitle.Should().Be("Line items");
        section.DoNotAllowInsertDeleteSection.Should().BeTrue();
        section.Parent.Should().BeNull();
    }

    private static void AssertCanonicalPackageXml(XDocument xml)
    {
        xml.Root!.GetNamespaceOfPrefix("w15").Should().Be(W15);
        xml.Root.GetNamespaceOfPrefix("mc").Should().Be(Mc);
        xml.Root.Attribute(Mc + "Ignorable")!.Value.Split(' ').Should().Contain("w15");

        var body = xml.Root.Element(W + "body")!;
        var outer = body.Elements(W + "sdt").Should().ContainSingle().Subject;
        var repeatingSection = outer.Element(W + "sdtPr")!.Element(W15 + "repeatingSection");
        repeatingSection.Should().NotBeNull();
        var repeatingSectionElement = repeatingSection!;
        repeatingSectionElement.Elements().Select(element => element.Name).Should().Equal(
            W15 + "sectionTitle",
            W15 + "doNotAllowInsertDeleteSection");
        repeatingSectionElement.Element(W15 + "sectionTitle")!
            .Attribute(W + "val")!.Value.Should().Be("Line items");
        repeatingSectionElement.Element(W15 + "doNotAllowInsertDeleteSection")!
            .Attributes().Should().BeEmpty("the canonical true form is an empty on/off element");

        var items = outer.Element(W + "sdtContent")!.Elements(W + "sdt").ToList();
        items.Should().HaveCount(2);
        items.Should().OnlyContain(item =>
            item.Element(W + "sdtPr")!.Elements(W15 + "repeatingSectionItem").Count() == 1);
        items.Select(item => item.Element(W + "sdtPr")!
                .Element(W15 + "repeatingSectionItem")!.HasElements)
            .Should().OnlyContain(hasElements => !hasElements);
        items[0].Element(W + "sdtContent")!.Elements(W + "p").Should().HaveCount(2);
        items[1].Element(W + "sdtContent")!.Elements(W + "p").Should().ContainSingle();
    }

    private static MemoryStream BuildPackage(string bodyXml)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml",
                $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml"
                            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                            mc:Ignorable="w15">
                  <w:body>{{bodyXml}}<w:sectPr/></w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument ReadDocumentXml(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
