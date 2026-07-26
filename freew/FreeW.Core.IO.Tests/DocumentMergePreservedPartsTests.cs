using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;
using FreeW.Core.IO;
using FluentAssertions;

namespace FreeW.Core.IO.Tests;

public class DocumentMergePreservedPartsTests
{
    private static readonly XNamespace Relationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Merge_PreservesRenamedDrawingPackageGraph_WhenWritten()
    {
        const string chartRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        const string chartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
        var source = new TextDocument();
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/chart1.xml",
            Encoding.UTF8.GetBytes("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"),
            chartContentType,
            chartRelationship));
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/_rels/chart1.xml.rels",
            Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"../media/image1.png\" /></Relationships>")));
        source.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [2]));
        source.Preserved.ContentTypeDefaults["png"] = "image/png";
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.FromPreservedDrawing(new PreservedDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId7\" /></w:drawing>",
            [new PreservedDrawingReference("rId7", "/word/charts/chart1.xml", chartRelationship)])));
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/chart1.xml", [9], chartContentType, chartRelationship));
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/_rels/chart1.xml.rels", [8]));
        target.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [7]));

        DocumentMerge.Merge(target, 0, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("word/charts/chart1-freew-import1.xml").Should().NotBeNull();
        zip.GetEntry("word/charts/_rels/chart1-freew-import1.xml.rels").Should().NotBeNull();
        zip.GetEntry("word/media/image1-freew-import1.png").Should().NotBeNull();

        var documentRelationships = ReadXml(zip, "word/_rels/document.xml.rels");
        documentRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "charts/chart1-freew-import1.xml");
        var chartRelationships = ReadXml(zip, "word/charts/_rels/chart1-freew-import1.xml.rels");
        chartRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "../media/image1-freew-import1.png");
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(stream);
    }
}
