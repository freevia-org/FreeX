using System.IO.Compression;
using System.Text;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Compositor.Tests;

public sealed class R367_MalformedPresentationDoesNotThrowTests
{
    private static byte[] Package(string spTreeExtraXml)
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        using var seed = new MemoryStream();
        PptxPackageWriter.Write(presentation, seed);

        var bytes = seed.ToArray();
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("ppt/slides/slide1.xml")?.Delete();
            var entry = archive.CreateEntry("ppt/slides/slide1.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(
                "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<p:cSld><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                "<p:grpSpPr/>" +
                spTreeExtraXml +
                "</p:spTree></p:cSld></p:sld>");
        }

        return stream.ToArray();
    }

    private const string Body =
        "<p:txBody><a:bodyPr/><a:p><a:r><a:t>x</a:t></a:r></a:p></p:txBody>";

    private static string Shape(string xfrm, string extra = "") =>
        "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>" +
        "<p:spPr>" + xfrm + extra + "</p:spPr>" + Body + "</p:sp>";

    public static TheoryData<string, string> Cases() => new()
    {
        { "negative extent", Shape("<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"-9144000\" cy=\"-9144000\"/></a:xfrm>") },
        { "extent beyond long range", Shape("<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"99999999999999999999\" cy=\"1\"/></a:xfrm>") },
        { "offset not a number", Shape("<a:xfrm><a:off x=\"left\" y=\"top\"/><a:ext cx=\"100\" cy=\"100\"/></a:xfrm>") },
        { "rotation enormous", Shape("<a:xfrm rot=\"2147483647\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"100\" cy=\"100\"/></a:xfrm>") },
        { "duplicate shape ids", Shape("") + Shape("") },
        { "shape id zero", "<p:sp><p:nvSpPr><p:cNvPr id=\"0\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/>" + Body + "</p:sp>" },
        { "preset geometry that does not exist", Shape("", "<a:prstGeom prst=\"notAShape\"><a:avLst/></a:prstGeom>") },
        { "colour that is not hex", Shape("", "<a:solidFill><a:srgbClr val=\"zzzzzz\"/></a:solidFill>") },
        { "theme colour that does not exist", Shape("", "<a:solidFill><a:schemeClr val=\"notASlot\"/></a:solidFill>") },
        { "alpha beyond 100000", Shape("", "<a:solidFill><a:srgbClr val=\"FF0000\"><a:alpha val=\"999999999\"/></a:srgbClr></a:solidFill>") },
        { "table with no grid", "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"3\" name=\"t\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"100\" cy=\"100\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tr h=\"100\"><a:tc><a:txBody><a:bodyPr/><a:p/></a:txBody></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame>" },
        { "picture with no relationship", "<p:pic><p:nvPicPr><p:cNvPr id=\"4\" name=\"p\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:embed=\"rIdNope\"/><a:stretch/></p:blipFill><p:spPr/></p:pic>" },
        { "empty spTree", "" },
        { "font size negative", "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"s\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:rPr sz=\"-1200\"/><a:t>x</a:t></a:r></a:p></p:txBody></p:sp>" },
    };

    private static void Open(string body)
    {
        using var stream = new MemoryStream(Package(body));
        _ = PptxPackageReader.Read(stream).Slides.Count;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMalformedFileOpensWithoutThrowing(string label, string body)
    {
        var act = () => Open(body);

        act.Should().NotThrow(
            "a reader must ignore what it cannot use rather than refuse the file; throwing here costs " +
            "the user the whole document over one bad attribute ({0})", label);
    }
}
