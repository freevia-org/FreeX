using System.IO.Compression;
using System.Threading;
using Avalonia.Headless;
using Free.Shared.Pdf;
using Free.Shared.Xps;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class PortableXpsWriterTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

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

    [Fact]
    public async Task AvaloniaExport_TextDocumentUsesRasterPageFallbackInsideXpsPackage()
    {
        await Session.Dispatch(() =>
        {
            var view = new DocumentView();
            view.InsertText("Cyrillic XPS fallback");

            using var stream = new MemoryStream();
            FreeWAvaloniaXpsExport.Save(view, stream);

            using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
            archive.GetEntry("FixedDocSeq.fdseq").Should().NotBeNull();
            archive.Entries.Should().Contain(entry => entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }, CancellationToken.None);
    }
}
