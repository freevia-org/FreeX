namespace FreeP.App.Compositor.Tests;

public sealed class PresentationHandoutPdfExporterAspectRatioTests
{
    private static Presentation BuildDeck(int slideCount, long slideSizeCxEmu, long slideSizeCyEmu)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.SlideSizeCxEmu = slideSizeCxEmu;
        presentation.SlideSizeCyEmu = slideSizeCyEmu;
        for (var i = 1; i <= slideCount; i++)
        {
            var slide = new Slide { Title = $"Slide {i}" };
            slide.Shapes.Add(new SlideShape
            {
                Kind = SlideShapeKind.AutoShape,
                Text = $"Body {i}",
            });
            presentation.Slides.Add(slide);
        }

        return presentation;
    }

    [Fact]
    public void HandoutSlotBounds_MatchPresentationSlideAspectRatio_ForA4x3Deck()
    {
        // 4:3 deck (9144000 x 6858000 EMU), not the shared 16:9 default.
        var deck = BuildDeck(1, slideSizeCxEmu: 9144000, slideSizeCyEmu: 6858000);
        var expectedAspect = 9144000.0 / 6858000.0; // 4:3 == 1.3333...

        var plan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        var slot = plan.LayoutPlan.Pages.Single().Slots.Single();
        var actualAspect = slot.SlideBounds.Width / slot.SlideBounds.Height;

        actualAspect.Should().BeApproximately(expectedAspect, 0.01,
            "the handout slot must be shaped for the deck's real 4:3 slide size, not the hardcoded 16:9 default");
        actualAspect.Should().NotBeApproximately(16d / 9d, 0.01,
            "a 4:3 deck must not be shaped as if it were 16:9");
    }

    [Fact]
    public void HandoutSlotBounds_StayAt16By9_ForTheDefault16x9Deck()
    {
        // Sibling case: the ordinary 16:9 deck (Presentation.CreateEmpty()'s own default slide
        // size) must keep producing 16:9-shaped handout slots.
        var deck = BuildDeck(1, slideSizeCxEmu: 12192000, slideSizeCyEmu: 6858000);

        var plan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        var slot = plan.LayoutPlan.Pages.Single().Slots.Single();
        var actualAspect = slot.SlideBounds.Width / slot.SlideBounds.Height;

        actualAspect.Should().BeApproximately(16d / 9d, 0.01);
    }
}
