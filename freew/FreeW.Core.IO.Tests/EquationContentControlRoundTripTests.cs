using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class EquationContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    [Fact]
    public void InlineEquationControl_PreservesKindOmmlAndCanonicalXmlAcrossTwoSaves()
    {
        var source = CreateSource();
        var sourceControls = GetControlsByTag(source);
        AssertSourceEquationXml(sourceControls["EquationControl"]);
        AssertSourceControlKinds(sourceControls);
        SchemaErrors(source).Should().BeEmpty();

        var imported = Read(source);
        AssertModel(imported);

        var firstSave = Write(imported);
        var firstControls = GetControlsByTag(firstSave);
        AssertCanonicalEquationXml(firstControls["EquationControl"]);
        AssertCanonicalControlKinds(firstControls);
        SchemaErrors(firstSave).Should().BeEmpty();

        var reopened = Read(firstSave);
        AssertModel(reopened);

        var secondSave = Write(reopened);
        var secondControls = GetControlsByTag(secondSave);
        secondControls.Keys.Should().BeEquivalentTo(firstControls.Keys);
        foreach (var tag in firstControls.Keys)
        {
            XNode.DeepEquals(firstControls[tag], secondControls[tag]).Should().BeTrue(
                $"canonical {tag} SDT XML must remain stable after reopening and saving again");
        }
        AssertCanonicalEquationXml(secondControls["EquationControl"]);
        AssertCanonicalControlKinds(secondControls);
        SchemaErrors(secondSave).Should().BeEmpty();
    }

    private static void AssertModel(TextDocument document)
    {
        document.Paragraphs.Should().HaveCount(3);

        var equationRun = document.Paragraphs.ElementAt(0).Runs.Should().ContainSingle().Subject;
        equationRun.Text.Should().Be("x+1");
        equationRun.Equation.Should().NotBeNull();
        equationRun.Equation!.LinearText.Should().Be("x+1");
        equationRun.Control.Should().Be(new ContentControl(
            ContentControlKind.Equation,
            Tag: "EquationControl",
            Alias: "Inline equation",
            WordMetadata: new ContentControlWordMetadata(Id: "701")));

        var richTextRuns = document.Paragraphs.ElementAt(1).Runs;
        richTextRuns.Should().HaveCount(2);
        richTextRuns.Select(run => run.Text).Should().Equal("Rich", " text");
        richTextRuns.Should().OnlyContain(
            run => run.Control != null && run.Control.Kind == ContentControlKind.RichText);
        richTextRuns[0].Control.Should().BeSameAs(richTextRuns[1].Control);
        richTextRuns[0].Formatting.Bold.Should().BeTrue();

        var absentRun = document.Paragraphs.ElementAt(2).Runs.Should().ContainSingle().Subject;
        absentRun.Text.Should().Be("No explicit kind");
        absentRun.Control!.Tag.Should().Be("AbsentKind");
        absentRun.Control.Kind.Should().Be(ContentControlKind.PlainText);
    }

    private static void AssertSourceEquationXml(XElement control)
    {
        var expected = new XElement(W + "sdt",
            new XElement(W + "sdtPr",
                new XElement(W + "alias", new XAttribute(W + "val", "Inline equation")),
                new XElement(W + "id", new XAttribute(W + "val", "701")),
                new XElement(W + "tag", new XAttribute(W + "val", "EquationControl")),
                new XElement(W + "equation")),
            new XElement(W + "sdtContent",
                new XElement(M + "oMath",
                    new XElement(M + "r", new XElement(M + "t", "x+1")))));

        XNode.DeepEquals(control, expected).Should().BeTrue("the source fixture must contain exact Word equation SDT XML");
    }

    private static void AssertCanonicalEquationXml(XElement control)
    {
        var expected = new XElement(W + "sdt",
            new XElement(W + "sdtPr",
                new XElement(W + "alias", new XAttribute(W + "val", "Inline equation")),
                new XElement(W + "id", new XAttribute(W + "val", "701")),
                new XElement(W + "tag", new XAttribute(W + "val", "EquationControl")),
                new XElement(W + "equation")),
            new XElement(W + "sdtContent",
                new XElement(M + "oMath",
                    new XElement(M + "r",
                        new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "x+1")))));

        XNode.DeepEquals(control, expected).Should().BeTrue("the writer must emit the canonical equation SDT and OMML payload");
    }

    private static void AssertSourceControlKinds(IReadOnlyDictionary<string, XElement> controls)
    {
        var richTextProperties = Properties(controls["RichText"]);
        richTextProperties.Element(W + "richText").Should().NotBeNull();
        richTextProperties.Element(W + "equation").Should().BeNull();
        richTextProperties.Element(W + "text").Should().BeNull();

        var absentProperties = Properties(controls["AbsentKind"]);
        absentProperties.Elements().Select(element => element.Name)
            .Should().Equal(W + "tag");
    }

    private static void AssertCanonicalControlKinds(IReadOnlyDictionary<string, XElement> controls)
    {
        var richTextProperties = Properties(controls["RichText"]);
        richTextProperties.Element(W + "richText").Should().NotBeNull();
        richTextProperties.Element(W + "equation").Should().BeNull();
        richTextProperties.Element(W + "text").Should().BeNull();

        var absentProperties = Properties(controls["AbsentKind"]);
        absentProperties.Elements().Select(element => element.Name)
            .Should().Equal(W + "tag", W + "text");
        absentProperties.Element(W + "equation").Should().BeNull();
    }

    private static XElement Properties(XElement control) => control.Element(W + "sdtPr")!;

    private static byte[] CreateSource()
    {
        using var stream = new MemoryStream();
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
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="{{W}}" xmlns:m="{{M}}">
                  <w:body>
                    <w:p><w:sdt><w:sdtPr><w:alias w:val="Inline equation"/><w:id w:val="701"/><w:tag w:val="EquationControl"/><w:equation/></w:sdtPr><w:sdtContent><m:oMath><m:r><m:t>x+1</m:t></m:r></m:oMath></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="RichText"/><w:richText/></w:sdtPr><w:sdtContent><w:r><w:rPr><w:b/></w:rPr><w:t>Rich</w:t></w:r><w:r><w:t xml:space="preserve"> text</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="AbsentKind"/></w:sdtPr><w:sdtContent><w:r><w:t>No explicit kind</w:t></w:r></w:sdtContent></w:sdt></w:p>
                  </w:body>
                </w:document>
                """);
        }
        return stream.ToArray();
    }

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

    private static Dictionary<string, XElement> GetControlsByTag(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry)
            .Descendants(W + "sdt")
            .ToDictionary(
                control => Properties(control).Element(W + "tag")!.Attribute(W + "val")!.Value,
                control => control);
    }

    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
