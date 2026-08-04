using System.Windows;
using System.Windows.Documents;
using System.Printing;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class WpfPresentationPrintServiceTests
{
    [StaFact]
    public void BuildPageSource_FullPageSlides_ProducesRasterPages()
    {
        var source = WpfPresentationPrintService.BuildPageSource(
            Presentation.CreateEmpty(),
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides));

        AssertRasterPageSource(source);
    }

    [StaFact]
    public void BuildPageSource_NotesPages_ProducesRasterPages()
    {
        var source = WpfPresentationPrintService.BuildPageSource(
            Presentation.CreateEmpty(),
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages));

        AssertRasterPageSource(source);
    }

    [StaFact]
    public void BuildPageSource_Handouts_ProducesRasterPages()
    {
        var source = WpfPresentationPrintService.BuildPageSource(
            Presentation.CreateEmpty(),
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 2));

        AssertRasterPageSource(source);
    }

    [StaFact]
    public void RasterPagePaginator_ExposesEveryRasterPageAsPrintableDocumentPage()
    {
        var source = WpfPresentationPrintService.BuildPageSource(
            Presentation.CreateEmpty(),
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides));
        var paginator = new WpfRasterPagePaginator(source.Pages, new Size(600, 400));

        paginator.IsPageCountValid.Should().BeTrue();
        paginator.PageCount.Should().Be(source.Pages.Count);
        paginator.GetPage(0).Should().NotBe(DocumentPage.Missing);
    }

    [StaFact]
    public void ApplyPrintTicketOptions_PropagatesSharedCopiesCollationAndColor()
    {
        var ticket = WpfPresentationPrintService.ApplyPrintTicketOptions(
            new PrintTicket(),
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                Copies: 3,
                Collate: false,
                ColorMode: PresentationPrintColorMode.PureBlackAndWhite));

        ticket.CopyCount.Should().Be(3);
        ticket.Collation.Should().Be(Collation.Uncollated);
        ticket.OutputColor.Should().Be(OutputColor.Monochrome);
    }

    private static void AssertRasterPageSource(WpfPrintPageSource source)
    {
        source.Pages.Should().NotBeEmpty();
        source.Pages.Should().OnlyContain(page => page.Length > 0);
        source.PageWidthPoints.Should().BeGreaterThan(0);
        source.PageHeightPoints.Should().BeGreaterThan(0);
    }
}
