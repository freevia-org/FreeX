using System.Collections.Generic;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R133 remediation: File &gt; Export to PDF (<see cref="MainWindow"/>'s <c>ExportToPdf</c>) reuses the
/// print pipeline (<see cref="PrintLayout.BuildPaginator"/>), which clones the *already-rendered* editor
/// FlowDocument rather than re-decoding images from the model. A document image the editor could not
/// decode (see <see cref="DocumentView.BuildImageRun"/>/<c>DecodeImage</c>) was therefore invisible to
/// the shared writer's <c>imageDiagnostics</c> sink -- that sink only reports a page's already-composited
/// raster bytes failing to decode, which never happens (the host itself just encoded that PNG). This is
/// the same structural gap as FreeP's raster slide-export path, just one layer further upstream: the loss
/// happens once, at <see cref="DocumentView.LoadModel"/>/<c>Render</c> time, long before export runs.
/// <see cref="DocumentView.ImageDecodeDiagnostics"/> now records it there, and <c>MainWindow.ExportToPdf</c>
/// (see its <c>imageDiagnostics = new List&lt;string&gt;(_editor.ImageDecodeDiagnostics)</c> line) merges it
/// with the writer-level diagnostics.
///
/// <para>
/// These tests exercise <see cref="DocumentView.LoadModel"/> -&gt; <c>Render</c> -&gt; <c>DecodeImage</c>
/// -&gt; <see cref="DocumentView.ImageDecodeDiagnostics"/> directly -- the exact call chain
/// <c>MainWindow.ExportToPdf</c> reads from -- rather than driving the full
/// <see cref="PrintLayout.BuildPaginator"/> -&gt; <see cref="PdfExport.RenderToBytes"/> byte-producing
/// pipeline with an image present: that pipeline's <c>PrintLayout.CloneElement</c> deep-clones the
/// FlowDocument via <c>XamlWriter.Save</c>/<c>XamlReader.Load</c>, and WPF's <c>XamlWriter</c> cannot
/// serialize an <see cref="System.Windows.Controls.Image"/>'s decoded <c>Source</c> (a non-public
/// <c>BitmapFrameDecode</c> for a real picture, or a <c>RenderTargetBitmap</c> -- no parameterless
/// constructor -- for the undecodable-image placeholder) at all, image content aside. That is a separate,
/// pre-existing defect in the clone step itself (confirmed here with both a valid and an invalid image;
/// both fail the same way, before reaching PDF bytes) and is out of scope for this fix; flagged separately.
/// </para>
/// </summary>
public sealed class DocumentViewPdfImageDiagnosticsTests
{
    [StaFact]
    public void LoadModel_RecordsImageDecodeDiagnostics_WhenDocumentImageIsUndecodable()
    {
        var view = BuildViewWithImage(new InlineImage(new byte[] { 1, 2, 3, 4 }, 50, 30, ImageFormat.Wmf));

        // This is exactly what MainWindow.ExportToPdf reads via _editor.ImageDecodeDiagnostics before
        // merging it with the shared writer's own diagnostics.
        view.ImageDecodeDiagnostics.Should().NotBeEmpty(
            "an undecodable document image must be recorded by the editor's own render pass so " +
            "File > Export to PDF can surface it, instead of the loss being visible only as a " +
            "placeholder box in the editor");
    }

    [StaFact]
    public void LoadModel_NoImageDecodeDiagnostics_WhenDocumentImageIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var view = BuildViewWithImage(new InlineImage(MinimalPngBytes(), 50, 30));

        view.ImageDecodeDiagnostics.Should().BeEmpty();
    }

    [StaFact]
    public void ExportToPdf_MergeComposition_ProducesNoWarningsForACleanTextOnlyDocument()
    {
        // No-regression check for the exact merge MainWindow.ExportToPdf performs
        // (imageDiagnostics = editor diagnostics ++ writer diagnostics), run through the real
        // PrintLayout.BuildPaginator -> PdfExport.RenderToBytes pipeline (mirrors PdfExportTests'
        // existing text-only coverage) so an ordinary document does not spuriously warn.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Exported Heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body paragraph with some text to render onto the page."));

        var view = new DocumentView();
        view.LoadModel(doc);

        var paginator = PrintLayout.BuildPaginator(view);
        var writerDiagnostics = new List<string>();
        var bytes = PdfExport.RenderToBytes(paginator, "Sample", writerDiagnostics);

        var imageDiagnostics = new List<string>(view.ImageDecodeDiagnostics);
        imageDiagnostics.AddRange(writerDiagnostics);

        bytes.Should().NotBeEmpty();
        imageDiagnostics.Should().BeEmpty();
    }

    private static DocumentView BuildViewWithImage(InlineImage image)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
