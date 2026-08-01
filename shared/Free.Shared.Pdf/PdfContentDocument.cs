namespace Free.Shared.Pdf;

/// <summary>
/// One page of a draw-op PDF: a media box (points) and the ordered drawing primitives painted onto
/// it. Pages may differ in size, so each carries its own width/height.
/// </summary>
public sealed record PdfContentPage(
    double WidthPoints,
    double HeightPoints,
    IReadOnlyList<PdfDrawOp> Ops,
    IReadOnlyList<PdfLinkOverlay>? LinkOverlays = null,
    IReadOnlyList<PdfNamedDestination>? NamedDestinations = null);

/// <summary>
/// App-agnostic, fully laid-out document handed to a draw-op PDF backend (the portable WinAnsi
/// writer or the Skia writer). FreeX builds this from a <c>Workbook</c>; FreeW builds it from its
/// document model — the backends neither know nor care which.
/// </summary>
public sealed record PdfContentDocument(
    IReadOnlyList<PdfContentPage> Pages,
    PdfDocumentProperties? Properties = null);
