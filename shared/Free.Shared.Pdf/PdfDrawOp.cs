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

/// <summary>Host-neutral visual families used by DrawingML preset pattern fills.</summary>
public enum PdfPatternKind
{
    Horizontal,
    Vertical,
    DownDiagonal,
    UpDiagonal,
    Cross,
    Dot,
    Brick,
    DiagonalCross,
}

/// <summary>
/// A tiled, two-colour pattern fill. Preset family bucketing intentionally follows the WPF live
/// renderer; <paramref name="UnitScale"/> converts its 8x8 (or 12x8 brick) DIP tile into the
/// coordinate space used by the caller, normally PDF points.
/// </summary>
public sealed record PdfPatternFill(
    PdfPatternKind Kind,
    PdfColor Foreground,
    PdfColor Background,
    double UnitScale = 1)
{
    public double TileWidth => (Kind == PdfPatternKind.Brick ? 12 : 8) * UnitScale;
    public double TileHeight => 8 * UnitScale;
    public double StrokeWidth => (Kind == PdfPatternKind.Brick ? 0.5 : 1) * UnitScale;

    public static PdfPatternFill FromPreset(
        string? preset,
        PdfColor foreground,
        PdfColor background,
        double unitScale = 1)
    {
        if (!double.IsFinite(unitScale) || unitScale <= 0)
            unitScale = 1;

        var kind = preset switch
        {
            "horz" or "ltHorz" or "medGray" or "dkHorz" or "pct5" or "pct10" or "pct20"
                => PdfPatternKind.Horizontal,
            "vert" or "ltVert" or "dkVert" or "pct25" or "pct30"
                => PdfPatternKind.Vertical,
            "diagStripe" or "ltDnDiag" or "dkDnDiag" or "dnDiag" or "pct50"
                => PdfPatternKind.DownDiagonal,
            "ltUpDiag" or "dkUpDiag" or "upDiag" or "pct60" or "pct70"
                => PdfPatternKind.UpDiagonal,
            "cross" or "ltGrid" or "dkGrid" or "pct75" or "pct80"
                => PdfPatternKind.Cross,
            "dotGrid" or "dotDmnd" or "smGrid" or "pct90"
                => PdfPatternKind.Dot,
            "horzBrick" or "divot" or "weave"
                => PdfPatternKind.Brick,
            _ => PdfPatternKind.DiagonalCross,
        };

        return new PdfPatternFill(kind, foreground, background, unitScale);
    }
}

/// <summary>Fills an axis-aligned rectangle with a shared tiled pattern.</summary>
public sealed record PdfFillRectPattern(
    double X,
    double Y,
    double Width,
    double Height,
    PdfPatternFill Pattern) : PdfDrawOp;

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

/// <summary>Fills an axis-aligned ellipse with a shared tiled pattern.</summary>
public sealed record PdfFillEllipsePattern(
    double X,
    double Y,
    double Width,
    double Height,
    PdfPatternFill Pattern) : PdfDrawOp;

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

/// <summary>Draws arbitrary contours with a shared tiled pattern and optional solid outline.</summary>
public sealed record PdfPathPattern(
    IReadOnlyList<PdfPathContour> Contours,
    PdfPatternFill Pattern,
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
/// Clips a child draw-op list to an axis-aligned rectangular bounds in PDF user space.
/// This is the shared equivalent of a drawing group's local <c>ClipToBounds</c> surface and is
/// intentionally composable with <see cref="PdfRotationGroup"/> for nested group transforms.
/// </summary>
public sealed record PdfClipGroup(
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<PdfDrawOp> Ops) : PdfDrawOp;

/// <summary>
/// Applies a uniform opacity to a child draw-op list. Writers render the children as a graphics
/// state/layer so callers can reuse the same vector geometry for approximate effects.
/// </summary>
public sealed record PdfOpacityGroup(double Opacity, IReadOnlyList<PdfDrawOp> Ops) : PdfDrawOp;

/// <summary>Visual effect families shared by the vector PDF backends.</summary>
public enum PdfEffectKind
{
    Shadow,
    Glow,
    SoftEdge,
    Reflection,
    Bevel,
}

/// <summary>
/// Parameters for a rendered object effect. Bounds are in PDF user space and are used by
/// reflection to mirror the child operations around the object's lower edge. A null color means
/// that the child operation colors are retained; this is used for reflections and soft edges.
/// </summary>
public sealed record PdfEffectParameters(
    PdfColor? Color,
    double Opacity,
    double Radius,
    double OffsetX = 0,
    double OffsetY = 0,
    double ReflectionGap = 0,
    double ReflectionDirectionDegrees = 90,
    PdfColor? SecondaryColor = null,
    double ReflectionEndOpacity = 0,
    double ReflectionStartPosition = 0,
    double ReflectionEndPosition = 1,
    double ReflectionFadeDirectionDegrees = 90,
    double ReflectionScaleX = 1,
    double ReflectionScaleY = -1,
    double ReflectionSkewXDegrees = 0,
    double ReflectionSkewYDegrees = 0,
    double BevelWidth = 0,
    double BevelHeight = 0,
    double BevelLightDirectionDegrees = 135);

/// <summary>
/// Renders a composable vector layer from the child operations. This is intentionally an
/// operation, rather than an export marker: portable PDF emits translated/recolored passes and
/// Skia renders the same passes with its raster compositor where available.
/// </summary>
public sealed record PdfEffectGroup(
    PdfEffectKind Kind,
    double BoundsX,
    double BoundsY,
    double BoundsWidth,
    double BoundsHeight,
    PdfEffectParameters Parameters,
    IReadOnlyList<PdfDrawOp> Ops) : PdfDrawOp;

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
