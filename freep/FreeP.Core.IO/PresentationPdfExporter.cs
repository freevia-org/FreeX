using System.IO;
using Free.Shared.Pdf;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Exports a <see cref="Presentation"/> to a real PDF — one page per slide — through the shared, portable
/// (no-WPF) <see cref="PortablePdfWriter"/> tier that FreeX and FreeW also use. Because FreeP's model is
/// text-only today (a slide is a title plus text shapes; geometry/styling are deferred), this emits
/// <em>selectable vector text</em> rather than a rasterized canvas: the slide title in bold near the top and
/// each shape's text below it. As the slide model gains real geometry/visuals, this builder can grow richer
/// draw ops without changing the emitter or the calling code.
/// </summary>
public static class PresentationPdfExporter
{
    // Widescreen 16:9 slide (PowerPoint default 13.333in x 7.5in) in PDF points (1/72 inch).
    private const double SlideWidthPt = 960.0;
    private const double SlideHeightPt = 540.0;
    private const double MarginPt = 54.0;
    private const double TitleSize = 32.0;
    private const double BodySize = 18.0;
    private const double BodyLeadingPt = 26.0;

    /// <summary>Renders the presentation to PDF bytes in memory.</summary>
    public static byte[] ExportToBytes(Presentation presentation) =>
        PortablePdfWriter.WriteToBytes(BuildDocument(presentation), "FreeP portable PDF");

    /// <summary>Renders the presentation and writes the PDF to <paramref name="stream"/> (not disposed).</summary>
    public static void Export(Presentation presentation, Stream stream) =>
        PortablePdfWriter.Write(BuildDocument(presentation), stream, "FreeP portable PDF");

    /// <summary>Builds the app-agnostic content document (one page per slide) handed to the PDF emitter.</summary>
    public static PdfContentDocument BuildDocument(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var pages = new List<PdfContentPage>(Math.Max(presentation.Slides.Count, 1));
        if (presentation.Slides.Count == 0)
            pages.Add(BuildSlidePage(new Slide())); // a valid PDF always has at least one page
        else
            foreach (var slide in presentation.Slides)
                pages.Add(BuildSlidePage(slide));

        var p = presentation.Properties;
        var properties = new PdfDocumentProperties(
            Title: NullIfBlank(p.Title),
            Author: NullIfBlank(p.Author),
            Subject: NullIfBlank(p.Subject),
            Keywords: NullIfBlank(p.Keywords),
            Creator: "FreeP");

        return new PdfContentDocument(pages, properties);
    }

    private static PdfContentPage BuildSlidePage(Slide slide)
    {
        var ops = new List<PdfDrawOp>();

        // PDF user space has its origin at the bottom-left with y increasing upward, so we lay out from the
        // top down by starting at (height - margin) and decreasing y for each line.
        var y = SlideHeightPt - MarginPt - TitleSize;
        if (!string.IsNullOrEmpty(slide.Title))
            ops.Add(new PdfText(MarginPt, y, TitleSize, PdfFontFace.Bold, PdfColor.Black, OneLine(slide.Title)));
        y -= TitleSize * 1.4;

        foreach (var shape in slide.Shapes)
        {
            var content = !string.IsNullOrEmpty(shape.Text) ? shape.Text : $"[{shape.Kind}]";
            foreach (var line in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (y < MarginPt)
                    return new PdfContentPage(SlideWidthPt, SlideHeightPt, ops); // ran out of room on this slide
                ops.Add(new PdfText(MarginPt, y, BodySize, PdfFontFace.Regular, PdfColor.Black, OneLine(line)));
                y -= BodyLeadingPt;
            }
        }

        return new PdfContentPage(SlideWidthPt, SlideHeightPt, ops);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // The portable text op draws a single line; flatten tabs so spacing is at least visible.
    private static string OneLine(string text) => text.Replace("\t", "    ");
}
