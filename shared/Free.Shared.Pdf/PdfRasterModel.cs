namespace Free.Shared.Pdf;

/// <summary>
/// A page whose visual content is a pre-rendered raster image plus optional selectable-text and
/// link overlays. This is the model the WPF/PDFsharp backend consumes: the host rasterizes each
/// laid-out page (a WPF visual / FixedPage / DocumentPage) to a bitmap, then the shared backend
/// places the bitmap and overlays into the PDF. Image bytes are an encoded bitmap (PNG/BMP) the
/// platform backend can decode.
/// </summary>
public sealed record PdfRasterPage(
    double WidthPoints,
    double HeightPoints,
    byte[] ImageBytes,
    IReadOnlyList<PdfTextOverlay>? TextOverlays = null,
    IReadOnlyList<PdfLinkOverlay>? LinkOverlays = null);

/// <summary>A document of raster pages plus metadata, handed to the WPF/PDFsharp backend.</summary>
public sealed record PdfRasterDocument(
    IReadOnlyList<PdfRasterPage> Pages,
    PdfDocumentProperties? Properties = null);

/// <summary>
/// A selectable-text overlay positioned in the page's top-left, y-down coordinate space (points).
/// Drawn invisibly over the raster so the exported PDF is searchable/selectable while looking
/// pixel-identical to the rendered page.
/// </summary>
public sealed record PdfTextOverlay(
    double X,
    double Y,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic,
    PdfColor Color,
    double RotationDegrees,
    string Text);

/// <summary>
/// A clickable link region in the page's top-left, y-down coordinate space (points), targeting an
/// external URI or a named destination. Shared raster and draw-op pages both use this geometry
/// contract. Writers that support internal navigation resolve <see cref="DestinationName"/> against
/// the document's named destinations.
/// </summary>
public sealed record PdfLinkOverlay(
    double X,
    double Y,
    double Width,
    double Height,
    string? Uri,
    string? Tooltip = null,
    string? DestinationName = null);

/// <summary>
/// A named PDF navigation target in a page's top-left, y-down coordinate space (points).
/// </summary>
public sealed record PdfNamedDestination(
    string Name,
    double X,
    double Y);
