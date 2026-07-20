using System.IO.Compression;
using Free.Shared.Pdf;
using Free.Shared.Xps;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class PortableXpsWriterTests
{
    [Fact]
    public void WriteToBytes_VectorDocumentProducesRealXpsPackage()
    {
        var document = new PdfContentDocument([
            new PdfContentPage(612, 792, [
                new PdfFillRect(36, 36, 120, 48, new PdfColor(20, 80, 140)),
                new PdfLine(36, 36, 156, 84, PdfColor.Black, 1),
            ])]);

        var bytes = PortableXpsWriter.WriteToBytes(document);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        archive.GetEntry("_rels/.rels").Should().NotBeNull();
        archive.GetEntry("FixedDocSeq.fdseq").Should().NotBeNull();
        archive.GetEntry("Documents/1/FixedDocument.fdoc").Should().NotBeNull();
        var page = archive.GetEntry("Documents/1/Pages/1.fpage");
        page.Should().NotBeNull();
        using var reader = new StreamReader(page!.Open());
        var xml = reader.ReadToEnd();
        xml.Should().Contain("FixedPage");
        xml.Should().Contain("Path");
        xml.Should().Contain("#14508C");
    }

    [Fact]
    public void Analyze_TextWithoutFontProvesExactMissingDependency()
    {
        var document = new PdfContentDocument([
            new PdfContentPage(612, 792, [new PdfText(36, 756, 12, PdfFontFace.Regular, PdfColor.Black, "text")])]);

        var report = PortableXpsWriter.Analyze(document);

        report.IsExportable.Should().BeFalse();
        report.TextOperationCount.Should().Be(1);
        report.Requirements.Should().Contain(message =>
            message.Contains("embedded XPS font resource", StringComparison.Ordinal));
        var action = () => PortableXpsWriter.WriteToBytes(document);
        action.Should().Throw<XpsUnsupportedContentException>();
    }
}
