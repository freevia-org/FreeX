using Free.Shared.Drawing;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free compositor that converts a <see cref="Slide"/> (plus its <see cref="Presentation"/>
/// context) into an ordered list of <see cref="DrawOp"/> objects in DIP coordinates.
///
/// The compositor performs:
/// 1. Placeholder inheritance â€” shapes with a <see cref="Placeholder"/> tag inherit position/size from
///    the matching layout placeholder, then the master placeholder.
/// 2. Theme color resolution â€” <see cref="ThemeAwareColor"/> values with a <see cref="SchemeColorRef"/>
///    are resolved against the live theme color scheme + lumMod/lumOff.
/// 3. Geometry building â€” each autoshape kind is turned into a <see cref="ShapeGeometry"/> via
///    <see cref="ShapeGeometryBuilder"/>.
/// 4. EMU â†’ DIP conversion â€” all coordinates are converted to 96-DPI device-independent pixels.
///
/// The list is in painter's order (back-to-front = z-order of shapes on the slide), with an optional
/// leading <see cref="DrawOp.Background"/> entry when a background fill is present.
/// </summary>
public static class SlideCompositor
{
    // 1 inch = 914400 EMU; 1 inch = 96 DIP â†’ 1 DIP = 9525 EMU
    private const double EmuPerDip = 9525.0;

    // Default text insets matching PowerPoint defaults (in DIP)
    private const double DefaultInsetHorzDip = 9.14;  // ~7pt
    private const double DefaultInsetVertDip = 4.57;  // ~3.5pt

    // Default text run properties
    private const string DefaultFontFamily = "Calibri";
    private const double DefaultTitleFontSizePt = 40.0;
    private const double DefaultBodyFontSizePt = 18.0;

    /// <summary>
    /// Composes the given <paramref name="slide"/> into an ordered list of draw operations.
    /// </summary>
    /// <param name="presentation">The parent presentation (for theme, layouts, masters).</param>
    /// <param name="slide">The slide to composite.</param>
    /// <returns>
    /// Ordered list of <see cref="DrawOp"/> in painter's order (background first, then shapes back to front).
    /// </returns>
    public static IReadOnlyList<DrawOp> Compose(PresentationModel presentation, Slide slide)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);

        var ops = new List<DrawOp>();
        var theme = presentation.Theme;

        // Slide bounds in DIP (origin at 0,0)
        double slideWidthDip = presentation.SlideSizeCxEmu / EmuPerDip;
        double slideHeightDip = presentation.SlideSizeCyEmu / EmuPerDip;
        var slideBounds = new LayoutRect(0, 0, slideWidthDip, slideHeightDip);

        // 1. Background
        var bgFill = ResolveBackground(slide, presentation, theme);
        ops.Add(new DrawOp.Background { Fill = bgFill, BoundsDip = slideBounds });

        // 2. Shapes in z-order (back to front)
        foreach (var shape in slide.Shapes)
            ComposeShape(shape, slide, presentation, theme, ops);

        return ops;
    }

    // â”€â”€â”€ Background â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ResolvedFill ResolveBackground(Slide slide, PresentationModel presentation, PresentationTheme theme)
    {
        // Slide-level background overrides layout/master.
        if (slide.Background is not null)
            return ResolveFill(slide.Background, theme);

        // Try layout background.
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        if (layout?.Background is not null)
            return ResolveFill(layout.Background, theme);

        // Try master background.
        var master = presentation.Masters.Find(m => m.Id == layout?.MasterId)
                  ?? presentation.Masters.FirstOrDefault();
        if (master?.Background is not null)
            return ResolveFill(master.Background, theme);

        // Default: white.
        return new ResolvedFill.Solid(SrgbColor.White);
    }

    // â”€â”€â”€ Shape dispatch â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void ComposeShape(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops)
    {
        switch (shape.Kind)
        {
            case SlideShapeKind.Picture:
                ComposePicture(shape, slide, presentation, ops);
                break;

            case SlideShapeKind.Group:
                // Flatten group children (simplified â€” no group-level transform for now).
                foreach (var child in shape.Children)
                    ComposeShape(child, slide, presentation, theme, ops);
                break;

            default:
                ComposeAutoShape(shape, slide, presentation, theme, ops);
                break;
        }
    }

    // â”€â”€â”€ AutoShape / textbox / connector â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void ComposeAutoShape(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops)
    {
        // Resolve anchor (placeholder inheritance).
        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        // Build geometry from the resolved bounds.
        var geometry = ShapeGeometryBuilder.Build(shape.AutoShapeKind, boundsDip);

        // Resolve fill.
        var fill = shape.Fill is not null
            ? ResolveFill(shape.Fill, theme)
            : InferDefaultFill(shape, theme);

        // Resolve outline.
        var outline = shape.Outline is not null
            ? ResolveOutline(shape.Outline, theme)
            : ResolvedOutline.None.Instance;

        // Resolve text.
        ResolvedTextLayout? text = null;
        if (shape.TextBody is not null && shape.TextBody.Paragraphs.Count > 0)
        {
            var textSource = PlaceholderResolver.FindInheritedTextSource(shape, presentation);
            text = ResolveTextLayout(shape.TextBody, textSource, shape.Placeholder, theme);
        }

        ops.Add(new DrawOp.Shape
        {
            Geometry = geometry,
            Fill = fill,
            Outline = outline,
            RotationDeg = anchor.RotationDeg,
            FlipH = anchor.FlipH,
            FlipV = anchor.FlipV,
            BoundsDip = boundsDip,
            Text = text
        });
    }

    // â”€â”€â”€ Picture â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void ComposePicture(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        List<DrawOp> ops)
    {
        if (shape.Picture is null) return;

        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        ops.Add(new DrawOp.Picture
        {
            Bytes = shape.Picture.Bytes,
            ContentType = shape.Picture.ContentType,
            DestDip = boundsDip,
            RotationDeg = anchor.RotationDeg
        });
    }

    // â”€â”€â”€ Fill resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ResolvedFill ResolveFill(ShapeFill fill, PresentationTheme theme) => fill switch
    {
        ShapeFill.None => ResolvedFill.None.Instance,
        ShapeFill.Solid s => new ResolvedFill.Solid(ThemeColorResolver.Resolve(s.Color, theme)),
        ShapeFill.Gradient g => new ResolvedFill.Gradient(
            ThemeColorResolver.Resolve(g.StartColor, theme),
            ThemeColorResolver.Resolve(g.EndColor, theme),
            g.AngleDegrees),
        _ => ResolvedFill.None.Instance
    };

    /// <summary>
    /// Default fill when a shape has no explicit fill: transparent for lines/connectors,
    /// white for rectangles/text boxes, else transparent.
    /// </summary>
    private static ResolvedFill InferDefaultFill(SlideShape shape, PresentationTheme theme)
    {
        return shape.AutoShapeKind switch
        {
            DrawingShapeKind.Line or DrawingShapeKind.ElbowConnector or DrawingShapeKind.CurvedConnector
                => ResolvedFill.None.Instance,
            _ => ResolvedFill.None.Instance   // transparent by default (theme/master provides bg)
        };
    }

    // â”€â”€â”€ Outline resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ResolvedOutline ResolveOutline(ShapeOutline outline, PresentationTheme theme) => outline switch
    {
        ShapeOutline.None => ResolvedOutline.None.Instance,
        ShapeOutline.Visible v => new ResolvedOutline.Visible(
            ThemeColorResolver.Resolve(v.Color, theme),
            PointsToDip(v.WidthPt),
            v.Dash),
        _ => ResolvedOutline.None.Instance
    };

    // â”€â”€â”€ Text layout resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ResolvedTextLayout ResolveTextLayout(
        TextBody body,
        SlideShape? inheritedSource,
        Placeholder? placeholder,
        PresentationTheme theme)
    {
        // Determine the default font size based on placeholder type.
        double defaultFontSizePt = placeholder?.Type switch
        {
            PlaceholderType.Title or PlaceholderType.CenteredTitle => DefaultTitleFontSizePt,
            _ => DefaultBodyFontSizePt
        };

        // Determine default font from theme.
        string defaultMajorFont = theme.FontScheme.MajorLatinFont;
        string defaultMinorFont = theme.FontScheme.MinorLatinFont;
        string defaultFont = placeholder?.Type switch
        {
            PlaceholderType.Title or PlaceholderType.CenteredTitle => defaultMajorFont,
            _ => defaultMinorFont
        };

        var resolvedParas = new List<ResolvedParagraph>(body.Paragraphs.Count);

        foreach (var para in body.Paragraphs)
        {
            var resolvedRuns = new List<ResolvedRun>(para.Runs.Count);

            foreach (var run in para.Runs)
            {
                var color = run.Color is not null
                    ? ThemeColorResolver.Resolve(run.Color, theme)
                    : SrgbColor.Black;

                resolvedRuns.Add(new ResolvedRun
                {
                    Text = run.Text,
                    FontFamily = run.FontFamily ?? defaultFont,
                    FontSizePt = run.FontSizePt ?? defaultFontSizePt,
                    Bold = run.Bold,
                    Italic = run.Italic,
                    Underline = run.Underline,
                    Strikethrough = run.Strikethrough,
                    Color = color
                });
            }

            resolvedParas.Add(new ResolvedParagraph
            {
                Runs = resolvedRuns,
                Align = para.Align ?? TextAlign.Left,
                Level = para.Level,
                BulletKind = para.BulletKind,
                BulletChar = para.BulletChar,
                SpaceBeforePt = para.SpaceBeforePt ?? 0,
                SpaceAfterPt = para.SpaceAfterPt ?? 0
            });
        }

        return new ResolvedTextLayout
        {
            Paragraphs = resolvedParas,
            Anchor = body.Anchor,
            InsetLeftDip = body.InsetLeftPt.HasValue ? PointsToDip(body.InsetLeftPt.Value) : DefaultInsetHorzDip,
            InsetRightDip = body.InsetRightPt.HasValue ? PointsToDip(body.InsetRightPt.Value) : DefaultInsetHorzDip,
            InsetTopDip = body.InsetTopPt.HasValue ? PointsToDip(body.InsetTopPt.Value) : DefaultInsetVertDip,
            InsetBottomDip = body.InsetBottomPt.HasValue ? PointsToDip(body.InsetBottomPt.Value) : DefaultInsetVertDip,
            Wrap = body.Wrap
        };
    }

    // â”€â”€â”€ Unit helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static LayoutRect AnchorToBounds(ResolvedAnchor anchor) =>
        new(anchor.OffsetXEmu / EmuPerDip,
            anchor.OffsetYEmu / EmuPerDip,
            anchor.ExtentCxEmu / EmuPerDip,
            anchor.ExtentCyEmu / EmuPerDip);

    /// <summary>Converts typographic points to DIP (96/72 = 4/3 scaling).</summary>
    private static double PointsToDip(double pt) => pt * (96.0 / 72.0);
}

