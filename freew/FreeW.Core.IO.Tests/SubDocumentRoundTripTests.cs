using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class SubDocumentRoundTripTests
{
    private const string SubDocumentRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/subDocument";
    private const string StrictSubDocumentRelationshipType =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/subDocument";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pr = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void ExternalSubDocuments_PreserveTargetsOrderAndNestedContentControlAcrossEdit()
    {
        using var source = BuildPackage();
        var document = DocxReader.Read(source);
        var paragraph = document.Paragraphs.Should().ContainSingle().Which;

        paragraph.Runs.Should().HaveCount(5);
        paragraph.Runs.Select(run => run.Text).Should().Equal("Before ", string.Empty, " between ", string.Empty, " after");
        paragraph.Runs[1].SubDocument.Should().Be(new SubDocumentReference("Chapter 1.docx"));
        paragraph.Runs[3].SubDocument.Should().Be(new SubDocumentReference("file:///C:/Books/Chapter2.docx"));
        paragraph.Runs[3].Control.Should().NotBeNull();
        paragraph.Runs[3].Control!.Tag.Should().Be("chapter-anchor");

        paragraph.Runs[0].Text = "Edited before ";
        var first = Write(document);
        AssertPackage(first);

        var reopened = DocxReader.Read(new MemoryStream(first));
        var reopenedParagraph = reopened.Paragraphs.Single();
        reopenedParagraph.Runs.Select(run => run.SubDocument?.Target)
            .Where(target => target is not null)
            .Should().Equal("Chapter 1.docx", "file:///C:/Books/Chapter2.docx");
        reopenedParagraph.Runs.Single(run => run.SubDocument?.Target.EndsWith("Chapter2.docx", StringComparison.Ordinal) == true)
            .Control.Should().NotBeNull();

        var second = Write(reopened);
        AssertPackage(second);
    }

    [Fact]
    public void StrictOpenXml_PreservesExternalSubDocumentRelationship()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph("Master");
        paragraph.Runs.Add(Run.FromSubDocument("Chapter1.docx"));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        var adapter = DocxFileAdapter.Strict();
        adapter.Save(document, stream);
        var package = stream.ToArray();

        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            var relationships = ReadXml(zip, "word/_rels/document.xml.rels");
            var subDocument = relationships.Root!.Elements()
                .Single(element => element.Attribute("Type")?.Value == StrictSubDocumentRelationshipType);
            subDocument.Attribute("Target")!.Value.Should().Be("Chapter1.docx");
            subDocument.Attribute("TargetMode")!.Value.Should().Be("External");
        }

        using var input = new MemoryStream(package);
        var reopened = adapter.Load(input);
        reopened.Paragraphs.Single().Runs.Single(run => run.SubDocument is not null)
            .SubDocument.Should().Be(new SubDocumentReference("Chapter1.docx"));
    }

    private static void AssertPackage(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var document = ReadXml(zip, "word/document.xml");
        var paragraph = document.Descendants(W + "p").Single();
        paragraph.Value.Should().Be("Edited before  between  after");

        var orderedContent = paragraph.Elements()
            .Where(element => element.Name != W + "pPr")
            .Select(element => element.Name == W + "r"
                ? "text:" + element.Value
                : element.Name == W + "subDoc"
                    ? "subDoc"
                    : element.Name == W + "sdt"
                        ? "sdt"
                        : element.Name.LocalName)
            .ToList();
        orderedContent.Should().Equal("text:Edited before ", "subDoc", "text: between ", "sdt", "text: after");

        var directAnchor = paragraph.Elements(W + "subDoc").Single();
        var controlledAnchor = paragraph.Element(W + "sdt")!
            .Element(W + "sdtContent")!
            .Element(W + "subDoc")!;
        var relationshipIds = new[]
        {
            directAnchor.Attribute(R + "id")!.Value,
            controlledAnchor.Attribute(R + "id")!.Value
        };

        var relationships = ReadXml(zip, "word/_rels/document.xml.rels");
        var subDocuments = relationships.Root!.Elements(Pr + "Relationship")
            .Where(element => element.Attribute("Type")?.Value == SubDocumentRelationshipType)
            .ToDictionary(element => element.Attribute("Id")!.Value);
        subDocuments.Keys.Should().BeEquivalentTo(relationshipIds);
        subDocuments[relationshipIds[0]].Attribute("Target")!.Value.Should().Be("Chapter 1.docx");
        subDocuments[relationshipIds[1]].Attribute("Target")!.Value.Should().Be("file:///C:/Books/Chapter2.docx");
        foreach (var relationship in subDocuments.Values)
            relationship.Attribute("TargetMode")!.Value.Should().Be("External");
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p>
                      <w:r><w:t xml:space="preserve">Before </w:t></w:r>
                      <w:customXml w:element="chapter"><w:subDoc r:id="rIdChapter1"/></w:customXml>
                      <w:r><w:t xml:space="preserve"> between </w:t></w:r>
                      <w:sdt>
                        <w:sdtPr><w:tag w:val="chapter-anchor"/><w:text/></w:sdtPr>
                        <w:sdtContent><w:subDoc r:id="rIdChapter2"/></w:sdtContent>
                      </w:sdt>
                      <w:r><w:t xml:space="preserve"> after</w:t></w:r>
                    </w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdChapter1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/subDocument" Target="Chapter 1.docx" TargetMode="External"/>
                  <Relationship Id="rIdChapter2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/subDocument" Target="file:///C:/Books/Chapter2.docx" TargetMode="External"/>
                </Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
