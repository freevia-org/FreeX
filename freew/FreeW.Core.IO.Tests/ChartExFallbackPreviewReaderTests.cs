using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A chartex chart is preserved verbatim, but its <c>mc:Fallback</c> picture is kept beside it as a view-only
/// preview so a renderer can draw something of the right size where the chart sits.
/// </summary>
public class ChartExFallbackPreviewReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // 1x1 transparent PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static TextDocument Read(bool withFallback)
    {
        var fallback = withFallback
            ? "<mc:Fallback><w:drawing><wp:inline><wp:extent cx=\"5486400\" cy=\"3200400\"/>" +
              "<wp:docPr id=\"3\" name=\"Chart 3\"/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
              "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
              "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:blipFill><a:blip r:embed=\"rId9\"/></pic:blipFill></pic:pic>" +
              "</a:graphicData></a:graphic></wp:inline></w:drawing></mc:Fallback>"
            : string.Empty;
        var body =
            "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"cx\"><w:drawing><wp:inline>" +
            "<wp:extent cx=\"5486400\" cy=\"3200400\"/><wp:docPr id=\"3\" name=\"Chart 3\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.microsoft.com/office/drawing/2014/chartex\">" +
            "<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId8\"/>" +
            "</a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>" + fallback +
            "</mc:AlternateContent></w:r></w:p>";

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/charts/chart4.xml\" ContentType=\"application/vnd.ms-office.chartex+xml\"/>" +
                "</Types>");
            Add(zip, "word/document.xml",
                $"<w:document xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\" " +
                "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
                "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
                $"<w:body>{body}</w:body></w:document>");
            Add(zip, "word/_rels/document.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId8\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" Target=\"charts/chart4.xml\"/>" +
                "<Relationship Id=\"rId9\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>" +
                "</Relationships>");
            Add(zip, "word/charts/chart4.xml",
                "<cx:chartSpace xmlns:cx=\"http://schemas.microsoft.com/office/drawing/2014/chartex\"><cx:chart/></cx:chartSpace>");
            var image = zip.CreateEntry("word/media/image1.png");
            using (var imageStream = image.Open())
                imageStream.Write(Png);
        }

        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    [Fact]
    public void ChartExRun_KeepsItsFallbackPictureAsAViewOnlyPreview()
    {
        var run = Read(withFallback: true).Blocks.OfType<Paragraph>().Single().Runs.Single();

        Assert.NotNull(run.PreservedDrawing);
        Assert.NotNull(run.PreservedDrawingPreview);
        Assert.Equal(432, run.PreservedDrawingPreview!.WidthPt, 1);   // 5486400 EMU = 6 in
        Assert.Equal(252, run.PreservedDrawingPreview.HeightPt, 1);   // 3200400 EMU = 3.5 in
    }

    [Fact]
    public void ChartExRunWithoutAFallback_HasNoPreview()
    {
        var run = Read(withFallback: false).Blocks.OfType<Paragraph>().Single().Runs.Single();

        Assert.NotNull(run.PreservedDrawing);
        Assert.Null(run.PreservedDrawingPreview);
    }
}
