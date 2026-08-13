namespace FreeX.App.Services;

public sealed record ExportPublishOptionEvidencePlan(
    bool RejectsEmptyRenderedPageRange,
    bool RejectsPageRangeStartingAfterLastPage,
    bool RejectsPageRangeEndingAfterLastPage,
    bool RejectsUnsupportedPdfA,
    bool RejectsUnsupportedTaggedPdf,
    bool ClearsPdfOnlyChoicesForXps)
{
    public bool HasCompleteRejectionEvidence =>
        RejectsEmptyRenderedPageRange &&
        RejectsPageRangeStartingAfterLastPage &&
        RejectsPageRangeEndingAfterLastPage &&
        RejectsUnsupportedPdfA &&
        RejectsUnsupportedTaggedPdf &&
        ClearsPdfOnlyChoicesForXps;

    public string StatusText =>
        HasCompleteRejectionEvidence
            ? "Export publish option evidence: rendered page ranges reject empty/start-after/end-after output; PDF/A and tagged PDF are rejected for PDF; XPS clears PDF-only choices before export."
            : "Export publish option evidence is incomplete.";
}

/// <summary>
/// Builds a host-neutral evidence summary for export option rejection paths that WPF and
/// Avalonia both reach before native PDF/XPS painting starts.
/// </summary>
public static class ExportPublishOptionEvidencePlanner
{
    public static ExportPublishOptionEvidencePlan Build(
        int renderedPageCount,
        ExportPlannerTextResolver? textResolver = null)
    {
        var pageCount = Math.Max(1, renderedPageCount);
        var rejectsEmpty = !ExportPlanner.TryValidatePageRange(
            range: null,
            pageCount: 0,
            out _,
            textResolver);
        var rejectsStartAfterLast = !ExportPlanner.TryValidatePageRange(
            new ExportPageRange(pageCount + 1, pageCount + 1),
            pageCount,
            out _,
            textResolver);
        var rejectsEndAfterLast = !ExportPlanner.TryValidatePageRange(
            new ExportPageRange(1, pageCount + 1),
            pageCount,
            out _,
            textResolver);

        var pdfAOptions = ExportOptions.ExcelLikeDefault with
        {
            PdfConformance = PdfConformance.PdfA1b
        };
        var rejectsPdfA = !ExportPlanner.TryValidatePublishOptions(
            pdfAOptions,
            ExportFormat.Pdf,
            out _,
            textResolver);

        var taggedPdfOptions = ExportOptions.ExcelLikeDefault with
        {
            IncludeDocumentStructureTags = true
        };
        var rejectsTaggedPdf = !ExportPlanner.TryValidatePublishOptions(
            taggedPdfOptions,
            ExportFormat.Pdf,
            out _,
            textResolver);

        var xpsOptions = ExportOptions.ExcelLikeDefault with
        {
            Quality = ExportQuality.MinimumSize,
            CreateBookmarks = true,
            BookmarkMode = PdfBookmarkMode.PageNumbers,
            InitialView = PdfInitialView.TwoColumnRight,
            OpenMode = PdfOpenMode.Outlines,
            BitmapTextWhenFontsMayNotBeEmbedded = true,
            PdfLanguage = "uk-UA",
            PdfConformance = PdfConformance.PdfA1b,
            IncludeDocumentStructureTags = true
        };
        var normalizedXpsOptions = ExportPlanner.CreateEffectiveOptionsForFormat(xpsOptions, ExportFormat.Xps);
        var clearsPdfOnlyChoices =
            normalizedXpsOptions.Quality == ExportQuality.Standard &&
            !normalizedXpsOptions.CreateBookmarks &&
            normalizedXpsOptions.BookmarkMode == PdfBookmarkMode.None &&
            normalizedXpsOptions.InitialView == PdfInitialView.SinglePage &&
            normalizedXpsOptions.OpenMode == PdfOpenMode.Normal &&
            !normalizedXpsOptions.BitmapTextWhenFontsMayNotBeEmbedded &&
            normalizedXpsOptions.PdfLanguage == ExportPlanner.DefaultPdfLanguage &&
            normalizedXpsOptions.PdfConformance == PdfConformance.Standard &&
            !normalizedXpsOptions.IncludeDocumentStructureTags;

        return new ExportPublishOptionEvidencePlan(
            rejectsEmpty,
            rejectsStartAfterLast,
            rejectsEndAfterLast,
            rejectsPdfA,
            rejectsTaggedPdf,
            clearsPdfOnlyChoices);
    }
}
