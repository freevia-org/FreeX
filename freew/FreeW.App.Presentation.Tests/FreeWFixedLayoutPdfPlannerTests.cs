using Free.Shared.AppServices.Printing;
using Free.Shared.Pdf;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWFixedLayoutPdfPlannerTests
{
    [Fact]
    public void Apply_SelectsRequestedPages_AndRotatesOnlyOrientationMismatches()
    {
        var firstOp = new PdfLine(1, 2, 3, 4, PdfColor.Black, 1);
        var secondOp = new PdfLine(5, 6, 7, 8, PdfColor.Black, 1);
        var thirdOp = new PdfLine(9, 10, 11, 12, PdfColor.Black, 1);
        var properties = new PdfDocumentProperties(Title: "Print job");
        var document = new PdfContentDocument(
            [
                new PdfContentPage(600, 800, [firstOp]),
                new PdfContentPage(500, 700, [secondOp], [new PdfLinkOverlay(1, 2, 3, 4, "https://example.com")]),
                new PdfContentPage(900, 600, [thirdOp]),
            ],
            properties);

        var result = FreeWFixedLayoutPdfPlanner.Apply(
            document,
            new PrintSelection(
                PageRange: PrintPageRange.Between(2, 3),
                Orientation: PrintOrientation.Landscape));

        result.Properties.Should().BeSameAs(properties);
        result.Pages.Should().HaveCount(2);
        result.Pages[0].WidthPoints.Should().Be(700);
        result.Pages[0].HeightPoints.Should().Be(500);
        result.Pages[0].LinkOverlays.Should().BeNull("temporary print rotation cannot retain untransformed links");
        var rotation = result.Pages[0].Ops.Should().ContainSingle().Which.Should().BeOfType<PdfRotationGroup>().Subject;
        rotation.CenterX.Should().Be(250);
        rotation.CenterY.Should().Be(250);
        rotation.RotationDegrees.Should().Be(90);
        rotation.Ops.Should().ContainSingle().Which.Should().BeSameAs(secondOp);

        result.Pages[1].Should().BeSameAs(document.Pages[2], "an already-landscape page needs no transform");
    }

    [Fact]
    public void Apply_UsesSharedRangePolicyToClampBothBounds()
    {
        var document = new PdfContentDocument(
            [new PdfContentPage(600, 800, []), new PdfContentPage(600, 800, [])]);

        FreeWFixedLayoutPdfPlanner.Apply(
                document,
                new PrintSelection(PageRange: PrintPageRange.Between(2, 9)))
            .Pages.Should().ContainSingle().Which.Should().BeSameAs(document.Pages[1]);

        FreeWFixedLayoutPdfPlanner.Apply(
                document,
                new PrintSelection(PageRange: PrintPageRange.Single(3)))
            .Pages.Should().ContainSingle().Which.Should().BeSameAs(document.Pages[1]);
    }
}
