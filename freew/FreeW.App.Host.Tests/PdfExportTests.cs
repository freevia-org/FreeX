using System;
using System.IO;
using System.Text;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
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
}
