using System.Text;
using Free.Shared.Pdf.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// End-to-end cover for File &gt; Export to PDF on Windows (the exact renderer/backend pair
/// <see cref="PresentationFileCommandSession.ExportPdfAsync"/> uses). The route rasterizes each slide, so without a text
/// layer the exported PDF holds no text whatsoever: nothing selectable, searchable, or visible to a
/// screen reader. PDFsharp leaves overlay content streams uncompressed, so a literal search of the
/// raw PDF bytes is enough to prove the text really shipped.
/// </summary>
public class PresentationRasterPdfExportTests
{
    private const string TitleText = "Selectable Slide Title PDF";
    private const string BodyText = "Selectable Slide Body PDF";

    private static Presentation DeckWithDistinctiveText()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var slide = new Slide { Title = TitleText };
        slide.Shapes.Add(new SlideShape { Kind = SlideShapeKind.AutoShape, Text = BodyText });
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static byte[] ExportPdfBytes(Presentation presentation) =>
        PresentationRasterPdfExporter.ExportToBytes(
            presentation,
            request: null,
            WpfPresentationSlideImageRenderer.RenderSlideToPng,
            WpfRasterPdfWriter.WriteToBytes);

    [StaFact]
    public void ExportToBytes_WritesSelectableSlideTextIntoThePdf()
    {
        var pdf = Encoding.ASCII.GetString(ExportPdfBytes(DeckWithDistinctiveText()));

        pdf.Should().Contain(TitleText);
        pdf.Should().Contain(BodyText);
    }

    [StaFact]
    public void ExportToBytes_DrawsTheSelectableTextInvisiblyOverTheRaster()
    {
        // The bitmap already paints these glyphs, so the overlay runs in PDF text render mode 3
        // ("3 Tr" — invisible) and never double-prints the slide text on the rendered page.
        var pdf = Encoding.ASCII.GetString(ExportPdfBytes(DeckWithDistinctiveText()));

        pdf.Should().Contain("3 Tr");
    }

    [StaFact]
    public void ExportToBytes_StillEmbedsTheRenderedSlideRaster()
    {
        // No-regression guard: the visual output is still the rasterized slide.
        var bytes = ExportPdfBytes(DeckWithDistinctiveText());
        var pdf = Encoding.ASCII.GetString(bytes);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        pdf.Should().Contain("/Image");
        pdf.Should().Contain("%%EOF");
        bytes.Length.Should().BeGreaterThan(5000);
    }

    // ── R137: File > Export as PDF must exclude hidden slides (matches Print/Notes/Handout) ──────

    private const string VisibleSlideTitle = "Visible Slide For Export";
    private const string HiddenSlideTitle = "Hidden Slide Never Exported";

    private static Presentation DeckWithOneHiddenSlide()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = VisibleSlideTitle });
        presentation.Slides.Add(new Slide { Title = HiddenSlideTitle, IsHidden = true });
        return presentation;
    }

    [StaFact]
    public void ExportToBytes_ExcludesHiddenSlidesFromFileExportAsPdf()
    {
        // File > Export as PDF has no "print hidden slides" option, so it must default to the same
        // PowerPoint behavior as Print/Notes/Handout: hidden slides never ship in the PDF.
        var pdf = Encoding.ASCII.GetString(ExportPdfBytes(DeckWithOneHiddenSlide()));

        pdf.Should().Contain(VisibleSlideTitle);
        pdf.Should().NotContain(HiddenSlideTitle);
    }

    private static byte[] BuildFullPageSlidesPrintPackage(Presentation presentation, bool printHiddenSlides) =>
        PresentationPrintOutputPackageExecutor.BuildPackage(
            presentation,
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides, PrintHiddenSlides: printHiddenSlides),
            WpfPresentationSlideImageRenderer.RenderSlideToPng,
            WpfRasterPdfWriter.WriteToBytes).Bytes;

    [StaFact]
    public void BuildPackage_FullPageSlidesExcludesHiddenSlidesByDefault()
    {
        // Exercises the REAL print path (PresentationPrintOutputPackageExecutor.BuildPackage), not
        // PresentationRasterPdfExporter.BuildRenderPlan directly: an earlier attempt at this fix made
        // BuildRenderPlan drop hidden slides unconditionally, which would also pass a
        // BuildRenderPlan-only test while silently breaking the "Print hidden slides" toggle exercised
        // below.
        var pdf = Encoding.ASCII.GetString(BuildFullPageSlidesPrintPackage(DeckWithOneHiddenSlide(), printHiddenSlides: false));

        pdf.Should().Contain(VisibleSlideTitle);
        pdf.Should().NotContain(HiddenSlideTitle);
    }

    [StaFact]
    public void BuildPackage_FullPageSlidesIncludesHiddenSlidesWhenPrintHiddenSlidesIsChosen()
    {
        // The live backstage "Print hidden slides" toggle must still work for the default
        // FullPageSlides layout: it must not have been silently defeated by excluding hidden slides
        // unconditionally.
        var pdf = Encoding.ASCII.GetString(BuildFullPageSlidesPrintPackage(DeckWithOneHiddenSlide(), printHiddenSlides: true));

        pdf.Should().Contain(VisibleSlideTitle);
        pdf.Should().Contain(HiddenSlideTitle);
    }

    // ── R137: exported PDF hyperlinks must survive as clickable link annotations ───────────────────

    private const string LinkedShapeText = "Visit our site";
    private const string ExternalLinkUrl = "https://example.com/freep-hyperlink-test";

    private static Presentation DeckWithExternalShapeHyperlink()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var slide = new Slide { Title = "Slide With A Hyperlink" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            Text = LinkedShapeText,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 457200,
            Hyperlink = new Hyperlink { Url = ExternalLinkUrl },
        });
        presentation.Slides.Add(slide);
        return presentation;
    }

    [StaFact]
    public void ExportToBytes_EmitsClickableLinkAnnotationForShapeHyperlink()
    {
        // Verified with a strict reader, not by eye: PDFsharp leaves overlay content streams
        // uncompressed (see the class doc comment), so the low-level /Annots/Subtype/Link/A
        // structure this asserts on is exactly what a PDF viewer resolves to make the region
        // clickable -- not merely visible text that happens to look like a link.
        var pdf = Encoding.ASCII.GetString(ExportPdfBytes(DeckWithExternalShapeHyperlink()));

        // PDFsharp's own dictionary serializer (used here, unlike the hand-rolled portable writer)
        // omits the space between adjacent /Key/Value tokens for a directly-nested (non-indirect)
        // annotation dictionary -- matching the existing "/Subtype/Image" precedent in
        // WpfRasterPdfWriterTextOverlayTests.cs for this same writer's image XObject.
        pdf.Should().Contain("/Subtype/Link");
        pdf.Should().Contain("/Annots");
        pdf.Should().Contain(ExternalLinkUrl);
    }

    [StaFact]
    public void ExportToBytes_StillRendersTheLinkedShapeRasterWhenAHyperlinkIsPresent()
    {
        // No-regression guard: adding the link annotation must not disturb the rendered page.
        var bytes = ExportPdfBytes(DeckWithExternalShapeHyperlink());
        var pdf = Encoding.ASCII.GetString(bytes);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        pdf.Should().Contain("/Image");
        pdf.Should().Contain("%%EOF");
    }
}
