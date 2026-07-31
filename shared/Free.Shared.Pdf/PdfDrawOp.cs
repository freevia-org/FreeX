namespace Free.Shared.Pdf;

/// <summary>
/// Which built-in WinAnsi Helvetica face a text op draws with. The portable WinAnsi writer maps
/// these to the standard <c>/Helvetica</c>, <c>/Helvetica-Bold</c>,
/// <c>/Helvetica-Oblique</c>, and <c>/Helvetica-BoldOblique</c> Type1 fonts.
/// </summary>
public enum PdfFontFace
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}

/// <summary>
/// One drawing primitive on a content page, expressed in PDF user space (points, origin at the
/// bottom-left, y increasing upward). The set is deliberately small — filled rectangle, stroked
/// rectangle, and a single line of text — because that is the full vocabulary the spreadsheet/
/// document grid exporters need, and it serializes losslessly to both the WinAnsi and Skia backends.
/// </summary>
public abstract record PdfDrawOp;

/// <summary>Fills an axis-aligned rectangle with a solid color.</summary>
public sealed record PdfFillRect(double X, double Y, double Width, double Height, PdfColor Color) : PdfDrawOp;

/// <summary>
/// One color stop in a PDF linear gradient. Position is normalized to the [0, 1] gradient axis.
/// </summary>
public readonly record struct PdfGradientStop(double Position, PdfColor Color);

/// <summary>
/// Shared linear gradient model for fills and strokes. Coordinates are expressed in PDF user
/// space and colors are interpolated along the line from start to end.
/// </summary>
public sealed record PdfLinearGradient(
    double StartX,
    double StartY,
    double EndX,
    double EndY,
    IReadOnlyList<PdfGradientStop> Stops);

/// <summary>
/// Fills an axis-aligned rectangle with a linear gradient. <paramref name="FallbackColor"/> is
/// used by callers/backends that cannot render the gradient.
/// </summary>
public sealed record PdfFillRectLinearGradient(
    double X,
    double Y,
    double Width,
    double Height,
    PdfLinearGradient Gradient,
    PdfColor FallbackColor) : PdfDrawOp;

/// <summary>Strokes the outline of an axis-aligned rectangle with a solid color and line width.</summary>
public sealed record PdfStrokeRect(
    double X,
    double Y,
    double Width,
    double Height,
    PdfColor Color,
    double LineWidth,
    PdfDashPattern? Dash = null) : PdfDrawOp;

/// <summary>
/// Strokes the outline of an axis-aligned rectangle with a linear gradient.
/// </summary>
public sealed record PdfStrokeRectLinearGradient(
    double X,
    double Y,
    double Width,
    double Height,
    PdfLinearGradient Gradient,
    PdfColor FallbackColor,
    double LineWidth,
    PdfDashPattern? Dash = null) : PdfDrawOp;

/// <summary>Fills an axis-aligned ellipse inside the supplied rectangular bounds.</summary>
public sealed record PdfFillEllipse(double X, double Y, double Width, double Height, PdfColor Color) : PdfDrawOp;

/// <summary>Fills an axis-aligned ellipse inside the supplied rectangular bounds with a linear gradient.</summary>
public sealed record PdfFillEllipseLinearGradient(
    double X,
    double Y,
    double Width,
    double Height,
    PdfLinearGradient Gradient,
    PdfColor FallbackColor) : PdfDrawOp;

/// <summary>Strokes an axis-aligned ellipse inside the supplied rectangular bounds.</summary>
public sealed record PdfStrokeEllipse(
    double X,
    double Y,
    double Width,
    double Height,
    PdfColor Color,
    double LineWidth,
    PdfDashPattern? Dash = null) : PdfDrawOp;

/// <summary>Strokes an axis-aligned ellipse inside the supplied rectangular bounds with a linear gradient.</summary>
public sealed record PdfStrokeEllipseLinearGradient(
    double X,
    double Y,
    double Width,
    double Height,
    PdfLinearGradient Gradient,
    PdfColor FallbackColor,
    double LineWidth,
    PdfDashPattern? Dash = null) : PdfDrawOp;

/// <summary>
/// Draws a single run of text. <paramref name="X"/>/<paramref name="Y"/> is the text origin
/// (baseline left) in PDF user space.
/// </summary>
public sealed record PdfText(
    double X,
    double Y,
    double FontSize,
    PdfFontFace Face,
    PdfColor Color,
    string Text) : PdfDrawOp;

/// <summary>
/// Strokes a straight line between two points in PDF user space (origin bottom-left, y-up).
/// </summary>
public sealed record PdfLine(
    double X1,
    double Y1,
    double X2,
    double Y2,
    PdfColor Color,
    double LineWidth) : PdfDrawOp;

/// <summary>
/// Strokes a straight line with a linear gradient.
/// </summary>
public sealed record PdfLineLinearGradient(
    double X1,
    double Y1,
    double X2,
    double Y2,
    PdfLinearGradient Gradient,
    PdfColor FallbackColor,
    double LineWidth) : PdfDrawOp;

/// <summary>
/// Fills a triangle path. Used for simple vector markers such as connector arrowheads.
/// </summary>
public sealed record PdfFilledTriangle(
    double X1,
    double Y1,
    double X2,
    double Y2,
    double X3,
    double Y3,
    PdfColor Color) : PdfDrawOp;

/// <summary>
/// The kind of a single path segment. Coordinates are expressed in PDF user space.
/// </summary>
public enum PdfPathSegmentKind
{
    Line,
    CubicBezier,
}

public readonly record struct PdfPathPoint(double X, double Y);

/// <summary>
/// PDF stroke dash array and phase. Segments are measured in user-space points and alternate
/// painted and skipped portions, matching DrawingML preset dash semantics after host mapping.
/// </summary>
public sealed record PdfDashPattern(IReadOnlyList<double> Segments, double Phase = 0);

public sealed record PdfPathSegment(
    PdfPathSegmentKind Kind,
    PdfPathPoint End,
    PdfPathPoint Control1 = default,
    PdfPathPoint Control2 = default)
{
    public static PdfPathSegment LineTo(PdfPathPoint end) => new(PdfPathSegmentKind.Line, end);

    public static PdfPathSegment BezierTo(PdfPathPoint control1, PdfPathPoint control2, PdfPathPoint end) =>
        new(PdfPathSegmentKind.CubicBezier, end, control1, control2);
}

public sealed record PdfPathContour(
    PdfPathPoint Start,
    IReadOnlyList<PdfPathSegment> Segments,
    bool Closed);

/// <summary>
/// Draws one or more arbitrary path contours with optional fill and stroke. This is used for
/// PowerPoint custom/freeform geometry that cannot be represented by the simpler rectangle,
/// ellipse, line, or triangle primitives.
/// </summary>
public sealed record PdfPath(
    IReadOnlyList<PdfPathContour> Contours,
    PdfColor? FillColor,
    PdfColor? StrokeColor,
    double StrokeWidth,
    PdfDashPattern? StrokeDash = null) : PdfDrawOp;

/// <summary>
/// Draws arbitrary path contours with optional linear-gradient fill and/or stroke. Solid fallback
/// colors are kept alongside each gradient so unsupported consumers can preserve prior behavior.
/// </summary>
public sealed record PdfPathLinearGradient(
    IReadOnlyList<PdfPathContour> Contours,
    PdfLinearGradient? FillGradient,
    PdfColor? FillFallbackColor,
    PdfLinearGradient? StrokeGradient,
    PdfColor? StrokeFallbackColor,
    double StrokeWidth,
    PdfDashPattern? StrokeDash = null) : PdfDrawOp;

/// <summary>
/// Applies a rotation transform around a fixed PDF user-space center to a child draw-op list.
/// Positive degrees follow Office's visual coordinate convention; writers map that to their
/// backend coordinate system.
/// </summary>
public sealed record PdfRotationGroup(
    double CenterX,
    double CenterY,
    double RotationDegrees,
    IReadOnlyList<PdfDrawOp> Ops,
    bool FlipH = false,
    bool FlipV = false) : PdfDrawOp;

/// <summary>
/// Applies a uniform opacity to a child draw-op list. Writers render the children as a graphics
/// state/layer so callers can reuse the same vector geometry for approximate effects.
/// </summary>
public sealed record PdfOpacityGroup(double Opacity, IReadOnlyList<PdfDrawOp> Ops) : PdfDrawOp;

/// <summary>
/// Optional clipping geometry applied to an image before it is painted.
/// </summary>
public enum PdfImageClipKind
{
    None,
    Ellipse,
    RoundedRectangle,
    Triangle,
    Diamond,
    Parallelogram,
    Hexagon,
    Chevron,
}

/// <summary>
/// Fractional source-image crop margins, matching PresentationML <c>a:srcRect</c> semantics.
/// Values are normalized by writers against the decoded image dimensions before painting.
/// </summary>
public readonly record struct PdfImageSourceCrop(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public bool HasCrop => Left != 0 || Top != 0 || Right != 0 || Bottom != 0;
}

/// <summary>
/// Office-style per-pixel color effects to apply to a picture image before it is drawn or embedded.
/// </summary>
public readonly record struct PdfImageColorEffects(
    bool Grayscale,
    double? BiLevelThreshold,
    double? Brightness,
    double? Contrast)
{
    public bool HasPixelEffects =>
        Grayscale ||
        BiLevelThreshold.HasValue ||
        Brightness.HasValue ||
        Contrast.HasValue;
}

/// <summary>
/// Draws an encoded bitmap image into a rectangular PDF user-space bounds. Supported portable
/// content types are PNG and JPEG; unsupported content types are skipped by the dependency-free
/// writer instead of emitting a corrupt image stream.
/// </summary>
public sealed record PdfImage(
    double X,
    double Y,
    double Width,
    double Height,
    byte[] ImageBytes,
    string ContentType,
    double RotationDegrees = 0,
    PdfImageClipKind ClipKind = PdfImageClipKind.None,
    double Opacity = 1,
    PdfImageSourceCrop SourceCrop = default,
    PdfImageColorEffects ColorEffects = default) : PdfDrawOp;
