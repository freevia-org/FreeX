using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfAnnotationPlannerTests
{
    [Fact]
    public void BuildLinkAnnotations_FiltersInvalidEntriesAndClampsTopDownBounds()
    {
        var page = new PdfContentPage(
            WidthPoints: 100,
            HeightPoints: 80,
            Ops: [],
            LinkOverlays:
            [
                new PdfLinkOverlay(-10, 70, 40, 20, " https://example.test ", "tip"),
                new PdfLinkOverlay(20, 10, 15, 12, null, null, " destination "),
                new PdfLinkOverlay(0, 0, 0, 10, "https://zero.test", null),
                new PdfLinkOverlay(double.NaN, 0, 10, 10, "https://nan.test", null),
                new PdfLinkOverlay(0, 0, 10, 10, "  ", null, "  "),
            ]);

        var links = PdfAnnotationPlanner.BuildLinkAnnotations(page);

        links.Should().Equal(
            new PdfLinkAnnotationPlan(0, 70, 30, 80, "https://example.test", "tip", null),
            new PdfLinkAnnotationPlan(20, 10, 35, 22, null, null, "destination"));
    }

    [Fact]
    public void BuildNamedDestinations_FiltersInvalidEntriesAndClampsCoordinates()
    {
        var page = new PdfContentPage(
            WidthPoints: 100,
            HeightPoints: 80,
            Ops: [],
            NamedDestinations:
            [
                new PdfNamedDestination(" chapter ", -5, 90),
                new PdfNamedDestination(" ", 10, 20),
                new PdfNamedDestination("invalid", double.PositiveInfinity, 20),
            ]);

        PdfAnnotationPlanner.BuildNamedDestinations(page).Should().Equal(
            new PdfNamedDestination("chapter", 0, 80));
    }

    [Fact]
    public void BuildLinkAnnotations_DimensionOverloadSharesRasterBackendPolicy()
    {
        PdfAnnotationPlanner.BuildLinkAnnotations(
                pageWidth: 100,
                pageHeight: 80,
                overlays: [new PdfLinkOverlay(90, -5, 30, 25, " link ", "tip")])
            .Should().Equal(
                new PdfLinkAnnotationPlan(90, 0, 100, 20, "link", "tip", null));
    }
}
