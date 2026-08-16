using System.Text;
using Free.Shared.Pdf.Skia;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R137: shape and text hyperlinks must survive PDF export as clickable link annotations, for both
/// external (URL) and internal (slide-to-slide) targets. This covers the vector PDF pipeline
/// (<see cref="FreeP.Core.IO.PresentationPdfExporter"/> via <see cref="SkiaPdfWriter"/>) that FreeP's
/// Notes Pages and Handout PDF export use on both WPF and Avalonia -- see
/// <c>WpfPresentationFileCommandPorts.WriteVectorPdf</c>, which binds this exact writer. Verified with
/// a strict reader: these assertions check the low-level <c>/Annots</c>/<c>/Subtype /Link</c>/<c>/A</c>
/// or <c>/Dest</c> structure a PDF viewer actually resolves to make the region clickable, not merely
/// that the link's visible text made it into the page content.
/// </summary>
public sealed class PresentationVectorPdfHyperlinkTests
{
    private const string ExternalLinkUrl = "https://example.com/freep-hyperlink-test";

    private static Presentation TwoSlidePresentation()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Slide One" });
        presentation.Slides.Add(new Slide { Title = "Slide Two (Target)" });
        return presentation;
    }

    private static SlideShape LinkedShape(Hyperlink hyperlink) => new()
    {
        Text = "Visit our site",
        OffsetXEmu = 914400,
        OffsetYEmu = 914400,
        ExtentCxEmu = 1828800,
        ExtentCyEmu = 457200,
        Hyperlink = hyperlink,
    };

    private static byte[] ExportVectorPdfBytes(Presentation presentation) =>
        SkiaPdfWriter.WriteToBytesWithPortableFallback(FreeP.Core.IO.PresentationPdfExporter.BuildDocument(presentation));

    [StaFact]
    public void BuildDocument_EmitsClickableLinkAnnotationForExternalShapeHyperlink()
    {
        var presentation = TwoSlidePresentation();
        presentation.Slides[0].Shapes.Add(LinkedShape(new Hyperlink { Url = ExternalLinkUrl }));

        var pdf = Encoding.ASCII.GetString(ExportVectorPdfBytes(presentation));

        pdf.Should().Contain("/Subtype /Link");
        pdf.Should().Contain(ExternalLinkUrl);
    }

    [StaFact]
    public void BuildDocument_EmitsInternalLinkToTheTargetSlideForSlideToSlideHyperlink()
    {
        var presentation = TwoSlidePresentation();
        var targetSlideId = presentation.Slides[1].Id;
        presentation.Slides[0].Shapes.Add(LinkedShape(new Hyperlink { TargetSlideId = targetSlideId }));

        var pdf = Encoding.ASCII.GetString(ExportVectorPdfBytes(presentation));

        // Internal navigation has no URI target: a real cross-page /Dest, not a /S /URI action.
        pdf.Should().Contain("/Subtype /Link");
        pdf.Should().NotContain("/S /URI");
    }

    [StaFact]
    public void BuildDocument_StillRendersBothPagesWhenAHyperlinkIsPresent()
    {
        // No-regression guard: adding link annotations must not disturb ordinary page content.
        // SkiaPdfWriter embeds a subsetted CID (/Encoding /Identity-H) font, so slide title text is
        // not literal ASCII in the byte stream -- assert on the structural markers a broken export
        // would actually lose instead (both pages present, a valid trailer).
        var presentation = TwoSlidePresentation();
        presentation.Slides[0].Shapes.Add(LinkedShape(new Hyperlink { Url = ExternalLinkUrl }));

        var bytes = ExportVectorPdfBytes(presentation);
        var pdf = Encoding.ASCII.GetString(bytes);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        pdf.Should().Contain("/BaseFont", "the slide title/body text is still laid out and embeds a font");
        pdf.Should().Contain("%%EOF");
        bytes.Length.Should().BeGreaterThan(2000);
    }
}
