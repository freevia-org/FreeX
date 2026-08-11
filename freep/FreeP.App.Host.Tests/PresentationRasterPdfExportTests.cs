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
}
