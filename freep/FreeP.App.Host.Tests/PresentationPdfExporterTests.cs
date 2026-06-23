using System.Linq;
using System.Text;
using Free.Shared.Pdf;
using FreeP.Core.IO;

namespace FreeP.App.Host.Tests;

public class PresentationPdfExporterTests
{
    private static Presentation SampleDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var s1 = new Slide { Title = "Welcome" };
        s1.Shapes.Add(new SlideShape { Kind = "text", Text = "First bullet" });
        s1.Shapes.Add(new SlideShape { Kind = "text", Text = "Second bullet" });

        var s2 = new Slide { Title = "Agenda" };
        s2.Shapes.Add(new SlideShape { Kind = "text", Text = "Line A\nLine B" });

        presentation.Slides.Add(s1);
        presentation.Slides.Add(s2);
        presentation.Properties.Title = "My Deck";
        presentation.Properties.Author = "Tester";
        return presentation;
    }

    [Fact]
    public void ExportToBytes_ProducesValidPdf()
    {
        var bytes = PresentationPdfExporter.ExportToBytes(SampleDeck());

        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(bytes).Should().Contain("%%EOF");
    }

    [Fact]
    public void BuildDocument_OnePagePerSlide()
    {
        PresentationPdfExporter.BuildDocument(SampleDeck()).Pages.Should().HaveCount(2);
    }

    [Fact]
    public void BuildDocument_EmptyPresentation_StillHasOnePage()
    {
        var empty = Presentation.CreateEmpty();
        empty.Slides.Clear();

        PresentationPdfExporter.BuildDocument(empty).Pages.Should().ContainSingle();
    }

    [Fact]
    public void BuildDocument_DrawsTitleAndShapeText()
    {
        var doc = PresentationPdfExporter.BuildDocument(SampleDeck());

        var page1 = doc.Pages[0].Ops.OfType<PdfText>().Select(t => t.Text).ToList();
        page1.Should().Contain("Welcome");
        page1.Should().Contain("First bullet");
        page1.Should().Contain("Second bullet");

        // A multi-line shape's text splits into one text op per line.
        var page2 = doc.Pages[1].Ops.OfType<PdfText>().Select(t => t.Text).ToList();
        page2.Should().Contain("Line A");
        page2.Should().Contain("Line B");
    }

    [Fact]
    public void TitleOp_IsBold()
    {
        var doc = PresentationPdfExporter.BuildDocument(SampleDeck());

        doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text == "Welcome")
            .Face.Should().Be(PdfFontFace.Bold);
    }

    [Fact]
    public void BuildDocument_SetsCreatorAndDocumentMetadata()
    {
        var props = PresentationPdfExporter.BuildDocument(SampleDeck()).Properties;

        props.Should().NotBeNull();
        props!.Creator.Should().Be("FreeP");
        props.Title.Should().Be("My Deck");
        props.Author.Should().Be("Tester");
    }
}
