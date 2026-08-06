using System;
using System.IO;
using System.Text;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for real PDF export (<see cref="PdfExport"/>). The exporter renders the print paginator's
/// pages to a PDF via PDFsharp; these tests confirm it produces non-empty, well-formed PDF bytes from a
/// sample document. Runs on STA because it builds the real WPF editing surface and rasterises pages.
/// </summary>
public sealed class PdfExportTests
{
    [StaFact]
    public void RenderToBytes_SampleDocument_ProducesNonEmptyPdf()
    {
        var view = BuildSampleView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = PdfExport.RenderToBytes(paginator, "Sample");

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Exported PDF should not be empty.");
        // Every PDF begins with the "%PDF-" magic header and ends with the "%%EOF" trailer.
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 32), Math.Min(32, bytes.Length));
        Assert.Contains("%%EOF", tail);
    }

    [StaFact]
    public void SharedWorkflow_SampleDocument_WritesNonEmptyFile()
    {
        var view = BuildSampleView();
        var paginator = PrintLayout.BuildPaginator(view);
        var path = Path.Combine(Path.GetTempPath(), $"freew-pdf-{Guid.NewGuid():N}.pdf");

        try
        {
            var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Sample");
            var execution = FreeWExportWorkflow.ExecuteAsync(
                plan,
                path,
                (stream, _) =>
                {
                    var bytes = PdfExport.RenderToBytes(paginator, "Sample");
                    stream.Write(bytes);
                    return ValueTask.FromResult(new FreeWExportArtifact(paginator.PageCount, "WPF"));
                }).GetAwaiter().GetResult();

            Assert.True(execution.Succeeded, execution.Message);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0, "Exported PDF file should not be empty.");
            var header = new byte[5];
            using (var fs = File.OpenRead(path))
                _ = fs.Read(header, 0, header.Length);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(header));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [StaFact]
    public void ExportFromRealDocx_ProducesValidPdf()
    {
        // End-to-end .docx -> PDF through the shared tier: write a sample document to a real .docx,
        // read it back via FreeW's DocxReader, paginate via the print pipeline, and export to PDF via
        // PdfExport (which now routes through Free.Shared.Pdf.Wpf.WpfRasterPdfWriter).
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Quarterly Report") { StyleId = "Heading1" });
        source.Blocks.Add(new Paragraph("This document was produced from a real .docx file and exported to PDF through the shared PDF tier."));

        var docxPath = Path.Combine(Path.GetTempPath(), $"freew-sample-{Guid.NewGuid():N}.docx");
        var pdfPath = Path.Combine(Path.GetTempPath(), $"freew-sample-{Guid.NewGuid():N}.pdf");
        try
        {
            FreeW.Core.IO.DocxWriter.Write(source, docxPath);

            var loaded = FreeW.Core.IO.DocxReader.Read(docxPath);
            var view = new DocumentView();
            view.LoadModel(loaded);
            var paginator = PrintLayout.BuildPaginator(view);

            var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Quarterly Report");
            var execution = FreeWExportWorkflow.ExecuteAsync(
                plan,
                pdfPath,
                (stream, _) =>
                {
                    var bytes = PdfExport.RenderToBytes(paginator, "Quarterly Report");
                    stream.Write(bytes);
                    return ValueTask.FromResult(new FreeWExportArtifact(paginator.PageCount, "WPF"));
                }).GetAwaiter().GetResult();

            Assert.True(execution.Succeeded, execution.Message);
            Assert.True(File.Exists(pdfPath));
            var bytes = File.ReadAllBytes(pdfPath);
            Assert.True(bytes.Length > 0);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 32), Math.Min(32, bytes.Length)));
        }
        finally
        {
            if (File.Exists(docxPath)) File.Delete(docxPath);
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    [StaFact]
    public void RenderToBytes_MultiPageDocument_ProducesMultiplePdfPages()
    {
        var view = BuildMultiPageView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = PdfExport.RenderToBytes(paginator, "Long");

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        // A long document paginates across several pages; the PDF must carry one /Type /Page object each.
        var content = Encoding.Latin1.GetString(bytes);
        var pageCount = CountOccurrences(content, "/Type /Page\n") + CountOccurrences(content, "/Type/Page");
        Assert.True(paginator.PageCount > 1, "Sample should span more than one page.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

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
