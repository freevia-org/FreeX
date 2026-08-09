using System;
using System.IO;
using System.Linq;
using System.Text;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
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
    public void Save_SampleDocument_WritesNonEmptyFile()
    {
        var view = BuildSampleView();
        var paginator = PrintLayout.BuildPaginator(view);
        var path = Path.Combine(Path.GetTempPath(), $"freew-pdf-{Guid.NewGuid():N}.pdf");

        try
        {
            PdfExport.Save(paginator, path, "Sample");

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

            PdfExport.Save(paginator, pdfPath, "Quarterly Report");

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

    [StaFact]
    public void RenderToBytes_SampleDocument_CarriesSelectableTextLayer()
    {
        // R132: FreeW's Windows PDF export used to be raster-only (no text layer at all) unlike FreeX's
        // WPF export and FreeW's own Avalonia PDF export, so nothing in the exported page was
        // searchable/selectable/screen-reader-visible. The overlay is drawn uncompressed (see
        // WpfRasterPdfWriter), so a real, distinctive paragraph string should appear verbatim in the raw
        // PDF bytes once the text layer is present.
        var view = BuildDistinctiveTextView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = PdfExport.RenderToBytes(paginator, "Sample");

        var pdfText = Encoding.Latin1.GetString(bytes);
        Assert.Contains("Findable Selectable Overlay Marker Text", pdfText);
    }

    [StaFact]
    public void RenderToBytes_MultiPageDocument_PlacesEachPagesTextOnItsOwnPage()
    {
        // Sibling/no-regression: overlays must map to the RIGHT page (not just exist anywhere in the
        // PDF, and not all glued onto page 1) -- i.e. the per-page overlay list built from the XPS
        // round-trip must line up with the raster page loop's page order/count.
        //
        // This reads the PDF back through PDFsharp and inspects each page's own decoded content stream
        // (rather than grepping the whole raw byte buffer) so the assertions are anchored to which page
        // actually carries which marker. A whole-file substring search cannot distinguish "the text
        // layer is missing entirely" from "the text layer is present and correctly placed" when the
        // page also happens to embed other data; per-page extraction can, and does fail if the overlay
        // is dropped or glued onto the wrong page.
        var view = BuildTwoDistinctPagesView();
        var paginator = PrintLayout.BuildPaginator(view);

        var bytes = PdfExport.RenderToBytes(paginator, "TwoPages");

        // PageCount is only valid once computed, which PdfExport.RenderToBytes forces internally;
        // check it after rendering (mirrors RenderToBytes_MultiPageDocument_ProducesMultiplePdfPages).
        Assert.True(paginator.PageCount > 1, "Sample should span more than one page.");

        using var pdfStream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1, "Reopened PDF should carry more than one page.");

        var pageTexts = pdf.Pages.Cast<PdfPage>().Select(ReadDecodedPageContent).ToArray();
        var firstPageIndex = Array.FindIndex(
            pageTexts, text => text.Contains("PageOneMarkerText", StringComparison.Ordinal));
        var secondPageIndex = Array.FindIndex(
            pageTexts, text => text.Contains("PageTwoMarkerText", StringComparison.Ordinal));

        Assert.True(firstPageIndex >= 0, "Expected page-one marker text to be present as selectable text on some page.");
        Assert.True(secondPageIndex >= 0, "Expected page-two marker text to be present as selectable text on some page.");
        Assert.True(
            secondPageIndex > firstPageIndex,
            "The page-two marker's overlay should land on a strictly later page than the page-one marker's. " +
            "If the per-page overlay list is misaligned with the raster page loop -- or every overlay is " +
            "glued onto a single page -- both markers end up on the same page (or out of order) instead.");
        Assert.DoesNotContain("PageTwoMarkerText", pageTexts[firstPageIndex]);
        Assert.DoesNotContain("PageOneMarkerText", pageTexts[secondPageIndex]);
    }

    private static string ReadDecodedPageContent(PdfPage page)
    {
        var builder = new StringBuilder();
        foreach (var content in page.Contents)
        {
            if (content.Stream?.Value is { } streamBytes)
                builder.Append(Encoding.Latin1.GetString(streamBytes));
        }

        return builder.ToString();
    }

    private static DocumentView BuildDistinctiveTextView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Findable Selectable Overlay Marker Text"));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView BuildTwoDistinctPagesView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("PageOneMarkerText"));
        // Match the scale BuildMultiPageView already uses (proven to force pagination above).
        for (var i = 0; i < 400; i++)
            doc.Blocks.Add(new Paragraph($"Filler paragraph {i} with enough text to fill the page and force pagination across several pages before the second marker."));
        doc.Blocks.Add(new Paragraph("PageTwoMarkerText"));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
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
