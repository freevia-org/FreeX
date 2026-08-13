using Free.Shared.AppServices.Printing;
using Free.Shared.Pdf;

namespace FreeW.App.Presentation.Shell;

/// <summary>Applies renderer-neutral print range and orientation policy to fixed-layout PDF content.</summary>
public static class FreeWFixedLayoutPdfPlanner
{
    public static PdfContentDocument Apply(PdfContentDocument document, PrintSelection selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Print export requires at least one rendered page.");

        var pages = SelectPages(document.Pages, selection.EffectivePageRange)
            .Select(page => ApplyOrientation(page, selection.Orientation))
            .ToArray();
        return new PdfContentDocument(pages, document.Properties);
    }

    private static IEnumerable<PdfContentPage> SelectPages(
        IReadOnlyList<PdfContentPage> pages,
        PrintPageRange range)
    {
        var (first, last) = FreeWPrintRequestPlanner.ResolvePageRange(range, pages.Count);
        return pages.Skip(first - 1).Take(last - first + 1);
    }

    private static PdfContentPage ApplyOrientation(PdfContentPage page, PrintOrientation orientation)
    {
        var shouldRotate = orientation switch
        {
            PrintOrientation.Portrait => page.WidthPoints > page.HeightPoints,
            PrintOrientation.Landscape => page.HeightPoints > page.WidthPoints,
            _ => false,
        };
        if (!shouldRotate)
            return page;

        return new PdfContentPage(
            page.HeightPoints,
            page.WidthPoints,
            [new PdfRotationGroup(
                page.WidthPoints / 2d,
                page.WidthPoints / 2d,
                90,
                page.Ops)],
            LinkOverlays: null,
            NamedDestinations: null);
    }
}
