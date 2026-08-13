using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.Pdf.Skia;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal static class WpfPresentationPrintService
{
    public static PresentationNativePrintHandoffHostCapabilities DetectCapabilities()
    {
        var hostName = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.WpfPrintHostName);
        if (!OperatingSystem.IsWindows())
        {
            return PresentationNativePrintHandoffHostCapabilities.Deferred(
                hostName,
                PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.WpfPrintWindowsOnlyStatus));
        }

        var discovery = WpfPrintQueueCatalog.Discover();
        return discovery.HasQueues
            ? PresentationNativePrintHandoffHostCapabilities.Available(hostName)
            : PresentationNativePrintHandoffHostCapabilities.Deferred(
                hostName,
                discovery.FailureReason ?? PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.NoWindowsPrinterQueuesStatus));
    }

    public static bool ShowPrintDialogAndPrint(
        Presentation presentation,
        PresentationPrintRequest request,
        string suggestedPrintJobName,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        var source = BuildPageSource(presentation, request);
        if (source.Pages.Count == 0)
            return false;

        var dialog = new PrintDialog();
        dialog.PrintTicket = ApplyPrintTicketOptions(new PrintTicket(), request);
        if (dialog.ShowDialog() != true)
            return false;

        var pageWidth = Math.Max(1, dialog.PrintableAreaWidth);
        var pageHeight = Math.Max(1, dialog.PrintableAreaHeight);
        var paginator = new WpfRasterPagePaginator(source.Pages, new Size(pageWidth, pageHeight));
        dialog.PrintDocument(
            paginator,
            PresentationFileTextResources.NormalizePrintJobName(suggestedPrintJobName));
        return true;
    }

    internal static PrintTicket ApplyPrintTicketOptions(
        PrintTicket ticket,
        PresentationPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(request);

        ticket.CopyCount = Math.Clamp(request.Copies, 1, 999);
        ticket.Collation = request.Collate
            ? Collation.Collated
            : Collation.Uncollated;
        ticket.OutputColor = request.ColorMode switch
        {
            PresentationPrintColorMode.Color => OutputColor.Color,
            PresentationPrintColorMode.Grayscale => OutputColor.Grayscale,
            PresentationPrintColorMode.PureBlackAndWhite => OutputColor.Monochrome,
            _ => OutputColor.Color,
        };
        return ticket;
    }

    internal static WpfPrintPageSource BuildPageSource(
        Presentation presentation,
        PresentationPrintRequest request)
    {
        return request.Layout switch
        {
            PresentationPrintLayoutKind.FullPageSlides => BuildFullPageSource(presentation, request),
            PresentationPrintLayoutKind.NotesPages => BuildNotesPageSource(presentation, request),
            PresentationPrintLayoutKind.Handouts => BuildHandoutSource(presentation, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Layout, "Unsupported print layout."),
        };
    }

    private static WpfPrintPageSource BuildFullPageSource(
        Presentation presentation,
        PresentationPrintRequest request)
    {
        var renderPlan = PresentationRasterPdfExporter.BuildRenderPlan(
            presentation,
            new PresentationRasterPdfExportRequest(request.SlideRange),
            WpfPresentationSlideImageRenderer.RenderSlideToPng,
            request.IncludeCommentsAndInkMarkup
                ? WpfPresentationSlideImageRenderer.RenderSlideToPngWithPrintMarkup
                : null);
        return new WpfPrintPageSource(
            renderPlan.Pages.Select(page => page.ImageBytes).ToArray(),
            renderPlan.PageWidthPoints,
            renderPlan.PageHeightPoints);
    }

    private static WpfPrintPageSource BuildNotesPageSource(
        Presentation presentation,
        PresentationPrintRequest request)
    {
        var document = PresentationNotesPagePdfExporter.BuildDocument(
            presentation,
            new PresentationNotesPagePdfExportRequest(request));
        return RenderDrawOpDocument(document);
    }

    private static WpfPrintPageSource BuildHandoutSource(
        Presentation presentation,
        PresentationPrintRequest request)
    {
        var document = PresentationHandoutPdfExporter.BuildDocument(
            presentation,
            new PresentationHandoutPdfExportRequest(request));
        return RenderDrawOpDocument(document);
    }

    private static WpfPrintPageSource RenderDrawOpDocument(Free.Shared.Pdf.PdfContentDocument document)
    {
        if (document.Pages.Count == 0)
            return new WpfPrintPageSource([], PresentationExportPlanner.DefaultPrintPageWidth, PresentationExportPlanner.DefaultPrintPageHeight);

        var firstPage = document.Pages[0];
        return new WpfPrintPageSource(
            SkiaPdfWriter.RenderPagesToPng(document, dpi: 96),
            firstPage.WidthPoints,
            firstPage.HeightPoints);
    }

}

internal sealed record WpfPrintPageSource(
    IReadOnlyList<byte[]> Pages,
    double PageWidthPoints,
    double PageHeightPoints);

internal sealed class WpfRasterPagePaginator : DocumentPaginator
{
    private readonly IReadOnlyList<byte[]> _pages;
    private Size _pageSize;

    public WpfRasterPagePaginator(IReadOnlyList<byte[]> pages, Size pageSize)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        if (pages.Count == 0)
            throw new ArgumentException("At least one print page is required.", nameof(pages));
        _pageSize = pageSize;
    }

    public override IDocumentPaginatorSource Source => null!;

    public override bool IsPageCountValid => true;

    public override int PageCount => _pages.Count;

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = new Size(Math.Max(1, value.Width), Math.Max(1, value.Height));
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pages.Count)
            return DocumentPage.Missing;

        var image = new Image
        {
            Source = Decode(_pages[pageNumber]),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = _pageSize.Width,
            Height = _pageSize.Height,
        };
        var root = new Border
        {
            Background = Brushes.White,
            Width = _pageSize.Width,
            Height = _pageSize.Height,
            Child = image,
        };
        root.Measure(_pageSize);
        root.Arrange(new Rect(new Point(0, 0), _pageSize));
        root.UpdateLayout();
        return new DocumentPage(root, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
