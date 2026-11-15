namespace Free.Shared.Pdf;

/// <summary>
/// App-agnostic PDF document metadata. The default language belongs to the document catalog.
/// </summary>
public sealed record PdfDocumentProperties(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string? Keywords = null,
    string? Creator = null)
{
    public string? Language { get; init; }

    public PdfDocumentViewerSettings? ViewerSettings { get; init; }

    public IReadOnlyList<PdfOutlineEntry> Outlines { get; init; } = [];
}

public enum PdfViewerPageLayout
{
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight
}

public enum PdfViewerOpenMode
{
    Normal,
    Outlines,
    FullScreen
}

public sealed record PdfDocumentViewerSettings(
    PdfViewerPageLayout PageLayout,
    PdfViewerOpenMode OpenMode,
    bool DisplayDocumentTitle = false);

/// <summary>A document bookmark targeting a zero-based page index in the exported PDF.</summary>
public sealed record PdfOutlineEntry(string Title, int PageIndex);
