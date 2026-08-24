using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for native XPS export (<see cref="XpsExport"/>). The exporter serialises the same print
/// paginator the PDF/Print paths use into an in-box WPF XPS package; these tests confirm it produces a
/// well-formed OPC (zip) package containing the FixedDocumentSequence part. Runs on STA because XPS
/// serialisation walks the live WPF visual tree.
/// </summary>
public sealed class XpsExportTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.XpsExportTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    // Local OPC magic so the test does not depend on any other helper: every .xps / OOXML package is a
    // zip, which begins with the "PK\x03\x04" local-file-header signature.
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    [StaFact]
    public void RenderToBytes_SampleDocument_ProducesValidXpsPackage()
    {
        var view = BuildSampleView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = XpsExport.RenderToBytes(paginator);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Exported XPS should not be empty.");
        // OPC packages are zip archives -> they start with the PK\x03\x04 local-file-header signature.
        Assert.True(bytes.Length >= ZipMagic.Length);
        Assert.Equal(ZipMagic, bytes.Take(ZipMagic.Length).ToArray());

        // The package must contain the FixedDocumentSequence part (the XPS document root).
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipMode());
        Assert.Contains(zip.Entries, e =>
            e.FullName.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.Contains("FixedDocumentSequence", StringComparison.OrdinalIgnoreCase));
    }

    [StaFact]
    public void SharedWorkflow_SampleDocument_WritesValidXpsFile()
    {
        var view = BuildSampleView();
        var paginator = PrintLayout.BuildPaginator(view);
        var path = Path.Combine(_temporaryDirectory.Path, "sample.xps");

        try
        {
            var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Xps, "Sample");
            var execution = FreeWExportWorkflow.ExecuteAsync(
                plan,
                path,
                (stream, _) =>
                {
                    stream.Write(XpsExport.RenderToBytes(paginator));
                    return ValueTask.FromResult(new FreeWExportArtifact());
                }).GetAwaiter().GetResult();

            Assert.True(execution.Succeeded, execution.Message);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0, "Exported XPS file should not be empty.");

            var header = new byte[ZipMagic.Length];
            using (var fs = File.OpenRead(path))
                _ = fs.Read(header, 0, header.Length);
            Assert.Equal(ZipMagic, header);

            using var zip = ZipFile.OpenRead(path);
            Assert.Contains(zip.Entries, e =>
                e.FullName.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Contains("FixedDocumentSequence", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [StaFact]
    public void RenderToBytes_MultiPageDocument_SerialisesEveryPage()
    {
        var view = BuildMultiPageView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = XpsExport.RenderToBytes(paginator);

        Assert.True(paginator.PageCount > 1, "Sample should span more than one page.");

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipMode());
        // XPS emits one FixedPage part (.fpage) per page; the count must match the paginator.
        var fixedPages = zip.Entries.Count(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(paginator.PageCount, fixedPages);
    }

    [StaFact]
    public void RenderToBytes_FontSubsetterFallback_PreservesPagesWithoutMutatingDocument()
    {
        var view = BuildSampleView();
        var originalFontFamily = view.Document.FontFamily.Source;
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = XpsExport.RenderToBytesWithSimulatedFontSubsetterFailureForTests(paginator);

        Assert.NotEmpty(bytes);
        Assert.Equal(originalFontFamily, view.Document.FontFamily.Source);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipMode());
        Assert.Equal(
            paginator.PageCount,
            zip.Entries.Count(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".odttf", StringComparison.OrdinalIgnoreCase));
    }

    private static ZipArchiveMode ZipMode() => ZipArchiveMode.Read;

    private static DocumentView BuildSampleView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Exported Heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body paragraph with some text to render onto the page."));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView BuildMultiPageView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Exported Heading") { StyleId = "Heading1" });
        for (var i = 0; i < 400; i++)
            doc.Blocks.Add(new Paragraph($"Body paragraph number {i} with enough text to fill the page and force pagination across several pages."));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }
}
