using Free.Shared.Drawing;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free compositor that converts a <see cref="Slide"/> (plus its <see cref="Presentation"/>
/// context) into an ordered list of <see cref="DrawOp"/> objects in DIP coordinates.
///
/// The compositor performs:
/// 1. Placeholder inheritance — shapes with a <see cref="Placeholder"/> tag inherit position/size from
///    the matching layout placeholder, then the master placeholder.
/// 2. Theme color resolution — <see cref="ThemeAwareColor"/> values with a <see cref="SchemeColorRef"/>
///    are resolved against the live theme color scheme + lumMod/lumOff.
/// 3. Geometry building — each autoshape kind is turned into a <see cref="ShapeGeometry"/> via
///    <see cref="ShapeGeometryBuilder"/>.
/// 4. EMU to DIP conversion — all coordinates are converted to 96-DPI device-independent pixels.
///
/// The list is in painter's order (back-to-front = z-order of shapes on the slide), with an optional
/// leading <see cref="DrawOp.Background"/> entry when a background fill is present.
/// </summary>
public static class SlideCompositor
{
    // DrawingML EMU per 96-DPI DIP.
    private const double EmuPerDip = DrawingMlCoordinateUnits.EmuPerPixel;

    // Default text insets matching PowerPoint defaults (in DIP)
    private const double DefaultInsetHorzDip = 9.14;  // ~7pt
    private const double DefaultInsetVertDip = 4.57;  // ~3.5pt

    // Default cell insets for tables matching PowerPoint defaults (in points)
    private const double DefaultCellInsetHorzPt = 7.0;
    private const double DefaultCellInsetVertPt = 3.6;

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
    public static IReadOnlyList<DrawOp> Compose(PresentationModel presentation, Slide slide, int slideIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);

        var ops = new List<DrawOp>();

        // Compute the effective color map for this slide (ECMA-376 §19.3.1.20 / §14.2.9):
        //   slide.ColorMapOverride (from p:clrMapOvr/a:overrideClrMapping)
        //     ?? master's ColorMap (from p:clrMap)
        //     ?? null (ThemeColorResolver falls back to the default Office mapping).
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        var master = presentation.Masters.Find(m => m.Id == layout?.MasterId)
                  ?? presentation.Masters.FirstOrDefault();
        IReadOnlyDictionary<string, string>? effectiveClrMap =
            slide.ColorMapOverride as IReadOnlyDictionary<string, string>
            ?? master?.ColorMap as IReadOnlyDictionary<string, string>;

        // MM4: use the OWNING master's theme for color/font resolution.
        // Each master in a multi-master deck has its own theme (SlideMaster.Theme).
        // Fall back to presentation.Theme for single-master decks or when master.Theme is null
        // (degenerate packages that have no per-master theme part).
        var theme = master?.Theme ?? presentation.Theme;

        // Slide bounds in DIP (origin at 0,0)
        double slideWidthDip = presentation.SlideSizeCxEmu / EmuPerDip;
        double slideHeightDip = presentation.SlideSizeCyEmu / EmuPerDip;
        var slideBounds = new LayoutRect(0, 0, slideWidthDip, slideHeightDip);

        // 1. Background
        var bgFill = ResolveBackground(slide, presentation, theme, effectiveClrMap);
        ops.Add(new DrawOp.Background { Fill = bgFill, BoundsDip = slideBounds });

        // 2. Shapes in z-order (back to front)
        foreach (var shape in slide.Shapes)
            ComposeShape(shape, slide, presentation, theme, ops, slideIndex, effectiveClrMap);

        return ops;
    }

    // ─── Background ───────────────────────────────────────────────────────────────────────────

    private static ResolvedFill ResolveBackground(Slide slide, PresentationModel presentation, PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        // Slide-level background overrides layout/master.
        if (slide.Background is not null)
            return ResolveFill(slide.Background, theme, effectiveClrMap);

        // Try layout background.
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        if (layout?.Background is not null)
            return ResolveFill(layout.Background, theme, effectiveClrMap);

        // Try master background.
        var master = presentation.Masters.Find(m => m.Id == layout?.MasterId)
                  ?? presentation.Masters.FirstOrDefault();
        if (master?.Background is not null)
            return ResolveFill(master.Background, theme, effectiveClrMap);

        // Default: white.
        return new ResolvedFill.Solid(SrgbColor.White);
    }

    // ─── Shape dispatch ───────────────────────────────────────────────────────────────────────

    private static void ComposeShape(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        int slideIndex = 0,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        if (!HeaderFooterCommandPlanner.IsVisibleByHeaderFooterFlags(shape, slide))
        {
            return;
        }

        switch (shape.Kind)
        {
            case SlideShapeKind.Picture:
                ComposePicture(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            case SlideShapeKind.Media:
                ComposeMedia(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            case SlideShapeKind.Group:
                // Flatten group children (simplified — no group-level transform for now).
                foreach (var child in shape.Children)
                    ComposeShape(child, slide, presentation, theme, ops, slideIndex, effectiveClrMap);
                break;

            case SlideShapeKind.Table:
                if (shape.Table is not null)
                    ComposeTable(shape, theme, ops, effectiveClrMap);
                break;

            case SlideShapeKind.Chart:
                if (shape.Chart is not null)
                    ComposeChart(shape, theme, ops, effectiveClrMap);
                break;

            case SlideShapeKind.SmartArt:
                if (shape.SmartArt is not null)
                    ComposeSmartArt(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            // Theme 21: OLE — render the fallback preview image (same as Picture path).
            // Live OLE activation is deferred; fallback image is the visual floor.
            case SlideShapeKind.Ole:
                ComposeOle(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            // Wave 25A: preserved modern objects — fallback preview image or grey placeholder.
            case SlideShapeKind.Zoom:
            case SlideShapeKind.Ink:
            case SlideShapeKind.Model3d:
            case SlideShapeKind.PreservedObject:
                if (shape.PreservedObject is not null)
                    ComposePreservedObject(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            default:
                ComposeAutoShape(shape, slide, presentation, theme, ops, slideIndex, effectiveClrMap);
                break;
        }
    }

    // ─── AutoShape / textbox / connector ────────────────────────────────────────────────────

    private static void ComposeAutoShape(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        int slideIndex = 0,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        // Resolve anchor (placeholder inheritance).
        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        // Build geometry from the resolved bounds.
        var geometry = shape.CustomGeometry.Count > 0
            ? CustomGeometryBuilder.BuildCustom(shape.CustomGeometry, boundsDip)
            : ShapeGeometryBuilder.Build(shape.AutoShapeKind, boundsDip);

        // Resolve fill.
        var fill = shape.Fill is not null
            ? ResolveFill(shape.Fill, theme, effectiveClrMap)
            : InferDefaultFill(shape, theme);

        // Resolve outline.
        var outline = shape.Outline is not null
            ? ResolveOutline(shape.Outline, theme, effectiveClrMap)
            : ResolvedOutline.None.Instance;

        // Resolve text.
        ResolvedTextLayout? text = null;
        if (shape.TextBody is not null && shape.TextBody.Paragraphs.Count > 0)
        {
            // P0: Walk shape -> layout placeholder -> master placeholder to resolve
            // the effective vertical anchor and default paragraph alignment.
            var layoutPh = shape.Placeholder is not null
                ? PlaceholderResolver.FindLayoutPlaceholder(shape.Placeholder, slide, presentation)
                : null;
            var masterPh = shape.Placeholder is not null
                ? PlaceholderResolver.FindMasterPlaceholder(shape.Placeholder, slide, presentation)
                : null;

            var effectiveAnchor = ResolveVerticalAnchor(shape.TextBody, layoutPh?.TextBody, masterPh?.TextBody, shape.Placeholder);
            var effectiveDefaultAlign = ResolveDefaultParaAlign(shape.TextBody, layoutPh?.TextBody, masterPh?.TextBody);

            // MM3: resolve the master TextStyles for this slide's master (for text-style inheritance).
            var resolvedLayout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
            var resolvedMaster = resolvedLayout is not null
                ? presentation.Masters.Find(m => m.Id == resolvedLayout.MasterId)
                : presentation.Masters.FirstOrDefault();

            text = ResolveTextLayout(shape.TextBody, effectiveAnchor, effectiveDefaultAlign, shape.Placeholder,
                theme, slideIndex, effectiveClrMap, layoutPh?.TextBody, masterPh?.TextBody, resolvedMaster?.TextStyles);
        }

        // Wave 26: convert the elbow route from EMU to DIP for connector shapes
        IReadOnlyList<LayoutPoint>? elbowRouteDip = null;
        if (shape.Kind == SlideShapeKind.Connector
            && shape.AutoShapeKind == DrawingShapeKind.ElbowConnector
            && shape.ElbowRoute is { Count: >= 2 })
        {
            var pts = new LayoutPoint[shape.ElbowRoute.Count];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new LayoutPoint(shape.ElbowRoute[i].X / EmuPerDip,
                                         shape.ElbowRoute[i].Y / EmuPerDip);
            elbowRouteDip = pts;
        }

        ops.Add(new DrawOp.Shape
        {
            ShapeId = shape.Id,
            Geometry = geometry,
            Fill = fill,
            Outline = outline,
            RotationDeg = anchor.RotationDeg,
            FlipH = anchor.FlipH,
            FlipV = anchor.FlipV,
            BoundsDip = boundsDip,
            Text = text,
            Effects = ResolveEffects(shape.Effects),
            ElbowRouteDip = elbowRouteDip,
        });
    }

    private static ResolvedShapeEffects? ResolveEffects(ShapeEffects? fx)
    {
        if (fx is null) return null;

        bool hasBevel = fx.BevelTop is not null || fx.BevelBottom is not null;
        if (!fx.HasOuterShadow && !fx.HasGlow && !fx.HasSoftEdge
            && !hasBevel && fx.ContourWidthEmu == 0 && fx.Scene3d is null)
            return null;

        return new ResolvedShapeEffects
        {
            HasOuterShadow     = fx.HasOuterShadow,
            OuterShadowColor   = fx.OuterShadowColor,
            OuterShadowAlpha   = fx.OuterShadowAlpha,
            OuterShadowBlurDip = fx.OuterShadowBlurRadEmu / EmuPerDip,
            OuterShadowDistDip = fx.OuterShadowDistEmu    / EmuPerDip,
            OuterShadowDirDeg  = fx.OuterShadowDirDeg,

            HasGlow       = fx.HasGlow,
            GlowColor     = fx.GlowColor,
            GlowAlpha     = fx.GlowAlpha,
            GlowRadiusDip = fx.GlowRadiusEmu / EmuPerDip,

            HasSoftEdge       = fx.HasSoftEdge,
            SoftEdgeRadiusDip = fx.SoftEdgeRadEmu / EmuPerDip,

            // Bevel / 3-D
            BevelTop = fx.BevelTop is not null ? new ResolvedBevel
            {
                WidthDip   = Math.Max(1.0, fx.BevelTop.WidthEmu  / EmuPerDip),
                HeightDip  = Math.Max(1.0, fx.BevelTop.HeightEmu / EmuPerDip),
                PresetName = fx.BevelTop.PresetName
            } : null,
            BevelBottom = fx.BevelBottom is not null ? new ResolvedBevel
            {
                WidthDip   = Math.Max(1.0, fx.BevelBottom.WidthEmu  / EmuPerDip),
                HeightDip  = Math.Max(1.0, fx.BevelBottom.HeightEmu / EmuPerDip),
                PresetName = fx.BevelBottom.PresetName
            } : null,
            ExtrusionDepthDip = fx.ExtrusionHeightEmu / EmuPerDip,
            ContourWidthDip   = fx.ContourWidthEmu    / EmuPerDip,
            ContourColor      = fx.ContourColor,
            LightDirDeg       = ResolveLightDir(fx.Scene3d)
        };
    }

    /// <summary>
    /// Converts the OOXML lightRig dir= string to degrees clockwise from the top.
    /// Returns -1 (→ default top-left = 315°) if no scene3d is present.
    /// </summary>
    private static double ResolveLightDir(Scene3dInfo? scene3d)
    {
        if (scene3d is null) return -1;
        return scene3d.LightRigDir switch
        {
            "t"  => 270,   // top → light comes from above → highlight on top edge
            "tl" => 315,
            "l"  => 0,
            "bl" => 45,
            "b"  => 90,
            "br" => 135,
            "r"  => 180,
            "tr" => 225,
            _    => 315    // default: top-left
        };
    }

    /// <summary>
    /// Resolves the effective vertical anchor for a text body by walking the inheritance chain:
    /// shape -> layout placeholder -> master placeholder -> placeholder-type default.
    /// </summary>
    private static VerticalAnchor ResolveVerticalAnchor(
        TextBody body,
        TextBody? layoutBody,
        TextBody? masterBody,
        Placeholder? ph)
    {
        // Shape's own txBody anchor wins if explicitly set.
        if (body.Anchor.HasValue) return body.Anchor.Value;

        // Layout placeholder's txBody anchor.
        if (layoutBody?.Anchor.HasValue == true) return layoutBody.Anchor!.Value;

        // Master placeholder's txBody anchor.
        if (masterBody?.Anchor.HasValue == true) return masterBody.Anchor!.Value;

        // OOXML default: centered-title -> middle, body -> top, others -> top.
        return ph?.Type switch
        {
            PlaceholderType.CenteredTitle => VerticalAnchor.Middle,
            _ => VerticalAnchor.Top
        };
    }

    /// <summary>
    /// Resolves the effective default paragraph alignment from the lstStyle inheritance chain:
    /// shape -> layout placeholder -> master placeholder.
    /// </summary>
    private static TextAlign? ResolveDefaultParaAlign(
        TextBody body,
        TextBody? layoutBody,
        TextBody? masterBody)
    {
        if (body.DefaultParaAlign.HasValue) return body.DefaultParaAlign.Value;
        if (layoutBody?.DefaultParaAlign.HasValue == true) return layoutBody.DefaultParaAlign!.Value;
        if (masterBody?.DefaultParaAlign.HasValue == true) return masterBody.DefaultParaAlign!.Value;
        return null;
    }

    // ─── Picture ─────────────────────────────────────────────────────────────────────────────

    private static void ComposePicture(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        if (shape.Picture is null) return;

        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        // P3: resolve picture outline from shape's spPr a:ln.
        var outline = shape.Outline is not null
            ? ResolveOutline(shape.Outline, theme, effectiveClrMap)
            : ResolvedOutline.None.Instance;

        // 18A: carry crop + colour-effect fields from PictureFormat onto the draw op
        var pf = shape.PictureFormat;
        ops.Add(new DrawOp.Picture
        {
            ShapeId      = shape.Id,
            Bytes        = shape.Picture.Bytes,
            ContentType  = shape.Picture.ContentType,
            DestDip      = boundsDip,
            RotationDeg  = anchor.RotationDeg,
            Outline      = outline,
            CropLeft     = pf?.CropLeft   ?? 0,
            CropTop      = pf?.CropTop    ?? 0,
            CropRight    = pf?.CropRight  ?? 0,
            CropBottom   = pf?.CropBottom ?? 0,
            Grayscale    = pf?.Grayscale  ?? false,
            BiLevelThreshold = pf?.BiLevelThreshold,
            Brightness   = pf?.Brightness,
            Contrast     = pf?.Contrast,
            AlphaModPct  = pf?.AlphaModPct,
            // Wave 26: picture frame clip geometry
            PictureFrameGeometry = shape.PictureFrameGeometry,
            // Wave 26: also carry shape effects (shadow/soft-edge from effectLst)
            Effects      = ResolveEffects(shape.Effects),
        });
    }

    // ─── OLE embedded object (Theme 21) ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders an OLE embedded object by drawing its fallback preview image (same as ComposePicture).
    /// Live OLE activation is deferred; the fallback image is the visual floor.
    /// When no fallback image is present a grey placeholder rectangle is rendered instead.
    /// </summary>
    private static void ComposeOle(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var anchor    = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        if (shape.Picture is { Bytes.Length: > 0 } pic)
        {
            // Use the same DrawOp.Picture path as regular pictures.
            ops.Add(new DrawOp.Picture
            {
                ShapeId     = shape.Id,
                Bytes       = pic.Bytes,
                ContentType = pic.ContentType,
                DestDip     = boundsDip,
                RotationDeg = anchor.RotationDeg,
                Outline     = ResolvedOutline.None.Instance,
            });
        }
        else
        {
            // No fallback image — emit a grey rectangle placeholder.
            ops.Add(new DrawOp.Shape
            {
                ShapeId     = shape.Id,
                Geometry    = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, boundsDip),
                Fill        = new ResolvedFill.Solid(new SrgbColor(0xC0, 0xC0, 0xC0)),
                Outline     = ResolvedOutline.None.Instance,
                BoundsDip   = boundsDip,
                RotationDeg = anchor.RotationDeg,
            });
        }
    }

    // ─── Preserved modern objects (Wave 25A: zoom / ink / 3D / unknown) ─────────────────────

    /// <summary>
    /// Renders a preserved modern object (slide zoom, ink annotation, 3D model, or unknown
    /// graphicFrame) by drawing its fallback preview image if present, or a grey rectangle
    /// placeholder when no preview is available.
    /// </summary>
    private static void ComposePreservedObject(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var anchor    = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        if (shape.Picture is { Bytes.Length: > 0 } pic)
        {
            ops.Add(new DrawOp.Picture
            {
                Bytes       = pic.Bytes,
                ContentType = pic.ContentType,
                DestDip     = boundsDip,
                RotationDeg = anchor.RotationDeg,
                Outline     = ResolvedOutline.None.Instance,
            });
        }
        else
        {
            // No preview — grey rectangle placeholder (slightly lighter than OLE's 0xC0).
            ops.Add(new DrawOp.Shape
            {
                Geometry    = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, boundsDip),
                Fill        = new ResolvedFill.Solid(new SrgbColor(0xCC, 0xCC, 0xCC)),
                Outline     = ResolvedOutline.None.Instance,
                BoundsDip   = boundsDip,
                RotationDeg = anchor.RotationDeg,
            });
        }
    }

    // ─── Media (audio/video) ────────────────────────────────────────────────────────────────

    private static void ComposeMedia(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var anchor    = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        var outline = shape.Outline is not null
            ? ResolveOutline(shape.Outline, theme, effectiveClrMap)
            : ResolvedOutline.None.Instance;

        if (shape.Picture is { Bytes.Length: > 0 })
        {
            var mpf = shape.PictureFormat;
            ops.Add(new DrawOp.Picture
            {
                ShapeId      = shape.Id,
                Bytes        = shape.Picture.Bytes,
                ContentType  = shape.Picture.ContentType,
                DestDip      = boundsDip,
                RotationDeg  = anchor.RotationDeg,
                Outline      = outline,
                IsMedia      = true,
                CropLeft     = mpf?.CropLeft   ?? 0,
                CropTop      = mpf?.CropTop    ?? 0,
                CropRight    = mpf?.CropRight  ?? 0,
                CropBottom   = mpf?.CropBottom ?? 0,
                Grayscale    = mpf?.Grayscale  ?? false,
                BiLevelThreshold = mpf?.BiLevelThreshold,
                Brightness   = mpf?.Brightness,
                Contrast     = mpf?.Contrast,
                AlphaModPct  = mpf?.AlphaModPct,
            });
        }
        else
        {
            // No poster — draw a dark rectangle placeholder
            ops.Add(new DrawOp.Shape
            {
                ShapeId     = shape.Id,
                Geometry    = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, boundsDip),
                Fill        = new ResolvedFill.Solid(new SrgbColor(0x22, 0x22, 0x22)),
                Outline     = outline,
                BoundsDip   = boundsDip,
                RotationDeg = anchor.RotationDeg,
            });
        }
    }

    // ─── Table ───────────────────────────────────────────────────────────────────────────────

    private static void ComposeTable(SlideShape shape, PresentationTheme theme, List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var table = shape.Table!;
        var frameBounds = new LayoutRect(
            shape.OffsetXEmu / EmuPerDip,
            shape.OffsetYEmu / EmuPerDip,
            shape.ExtentCxEmu / EmuPerDip,
            shape.ExtentCyEmu / EmuPerDip);

        var cellOps = new List<TableCellOp>();

        // Build cumulative row Y offsets in DIP.
        var rowYsDip = new double[table.Rows.Count];
        double runningY = frameBounds.Y;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            rowYsDip[r] = runningY;
            runningY += table.Rows[r].HeightEmu / EmuPerDip;
        }

        // Build cumulative column X offsets in DIP.
        var colXsDip = new double[table.ColumnWidthsEmu.Count];
        double runningX = frameBounds.X;
        for (int c = 0; c < table.ColumnWidthsEmu.Count; c++)
        {
            colXsDip[c] = runningX;
            runningX += table.ColumnWidthsEmu[c] / EmuPerDip;
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < row.Cells.Count && c < table.ColumnWidthsEmu.Count; c++)
            {
                var cell = row.Cells[c];

                // Skip cells that are continuations of a merge (they're rendered by their origin cell).
                if (cell.HMerge || cell.VMerge)
                    continue;

                // Compute cell bounds accounting for GridSpan / RowSpan.
                double cellX = colXsDip[c];
                double cellY = rowYsDip[r];
                double cellW = 0;
                double cellH = 0;

                int gridSpan = Math.Max(1, cell.GridSpan);
                int rowSpan  = Math.Max(1, cell.RowSpan);

                for (int sc = c; sc < c + gridSpan && sc < table.ColumnWidthsEmu.Count; sc++)
                    cellW += table.ColumnWidthsEmu[sc] / EmuPerDip;
                for (int sr = r; sr < r + rowSpan && sr < table.Rows.Count; sr++)
                    cellH += table.Rows[sr].HeightEmu / EmuPerDip;

                var cellRect = new LayoutRect(cellX, cellY, cellW, cellH);

                // Effective fill.
                var effectiveFill = table.ComputeEffectiveFill(r, c, cell);
                var resolvedFill = effectiveFill is not null
                    ? ResolveFill(effectiveFill, theme, effectiveClrMap)
                    : ResolvedFill.None.Instance;

                // Effective border.
                var effectiveBorder = table.ComputeEffectiveBorderOutline(r, c, cell);

                ResolvedOutline ResolveOneBorder(ShapeOutline? explicit_border) =>
                    explicit_border is not null
                        ? ResolveOutline(explicit_border, theme, effectiveClrMap)
                        : (effectiveBorder is not null
                            ? ResolveOutline(effectiveBorder, theme, effectiveClrMap)
                            : ResolvedOutline.None.Instance);

                var borderLeft   = ResolveOneBorder(cell.Borders?.Left);
                var borderRight  = ResolveOneBorder(cell.Borders?.Right);
                var borderTop    = ResolveOneBorder(cell.Borders?.Top);
                var borderBottom = ResolveOneBorder(cell.Borders?.Bottom);

                // Effective text color (for cells that have no explicit run color).
                var effectiveTextColor = table.ComputeEffectiveTextColor(r, c);
                var resolvedTextColor = effectiveTextColor is not null
                    ? ThemeColorResolver.Resolve(effectiveTextColor, theme, effectiveClrMap)
                    : (SrgbColor?)null;

                // Text layout.
                ResolvedTextLayout? textLayout = null;
                if (cell.TextBody is not null && cell.TextBody.Paragraphs.Count > 0)
                {
                    var insets = TextFrameLayoutPlanner.FromOptionalInsets(
                        PointsToDip(cell.InsetLeftPt),
                        PointsToDip(cell.InsetTopPt),
                        PointsToDip(cell.InsetRightPt),
                        PointsToDip(cell.InsetBottomPt),
                        PointsToDip(DefaultCellInsetHorzPt),
                        PointsToDip(DefaultCellInsetVertPt));

                    textLayout = ResolveTableCellTextLayout(
                        cell.TextBody, insets,
                        resolvedTextColor, theme, effectiveClrMap);
                }

                cellOps.Add(new TableCellOp
                {
                    BoundsDip    = cellRect,
                    Fill         = resolvedFill,
                    BorderLeft   = borderLeft,
                    BorderRight  = borderRight,
                    BorderTop    = borderTop,
                    BorderBottom = borderBottom,
                    Text         = textLayout,
                    Anchor       = cell.Anchor ?? TableCellAnchor.Top
                });
            }
        }

        ops.Add(new DrawOp.Table
        {
            ShapeId   = shape.Id,
            BoundsDip = frameBounds,
            Cells     = cellOps
        });
    }

    // ─── Chart ──────────────────────────────────────────────────────────────────────────────────

    private static void ComposeChart(SlideShape shape, PresentationTheme theme, List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var chart = shape.Chart!;

        var frameBounds = new LayoutRect(
            shape.OffsetXEmu / EmuPerDip,
            shape.OffsetYEmu / EmuPerDip,
            shape.ExtentCxEmu / EmuPerDip,
            shape.ExtentCyEmu / EmuPerDip);

        SrgbColor[] seriesColors;

        if (chart.ChartType is ChartType.Pie or ChartType.Doughnut && chart.Series.Count > 0)
        {
            // BV1: For pie AND doughnut charts emit one color per data POINT (cycling accent1-6)
            // so the renderer can pick the right slice fill without re-resolving the theme.
            // Doughnut charts (like pie) color each slice by point, not per-series.
            var firstSeries = chart.Series[0];
            int ptCount = firstSeries.Values.Count;
            seriesColors = new SrgbColor[ptCount];
            for (int pi = 0; pi < ptCount; pi++)
            {
                if (firstSeries.PointColors.TryGetValue(pi, out var ptColor))
                    seriesColors[pi] = ThemeColorResolver.Resolve(ptColor, theme, effectiveClrMap);
                else
                    seriesColors[pi] = DefaultAccentColor(pi, theme);
            }
        }
        else
        {
            // Resolve one concrete sRGB color per series (using theme color resolution)
            seriesColors = new SrgbColor[chart.Series.Count];
            for (int i = 0; i < chart.Series.Count; i++)
            {
                var fillColor = chart.Series[i].FillColor;
                seriesColors[i] = fillColor is not null
                    ? ThemeColorResolver.Resolve(fillColor, theme, effectiveClrMap)
                    : DefaultAccentColor(i, theme);
            }
        }

        var fillPlans = BuildChartFillPlans(chart, theme, effectiveClrMap, seriesColors);

        ops.Add(new DrawOp.Chart
        {
            ShapeId      = shape.Id,
            BoundsDip    = frameBounds,
            ChartShape   = chart,
            SeriesColors = seriesColors,
            FillPlans    = fillPlans
        });
    }

    // ─── SmartArt ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Composes a SmartArt shape using the live layout engine (Theme 17) when the family is
    /// supported, otherwise falls back to the cached dsp:drawing shapes.
    ///
    /// Priority:
    ///   1. If <see cref="SmartArtData"/> is present and family is supported → live layout.
    ///   2. If <see cref="SmartArtShape.FallbackShapes"/> is non-empty → cached drawing path.
    ///   3. Fallback: grey placeholder rectangle.
    /// </summary>
    private static ChartFillPlanSet BuildChartFillPlans(
        ChartShape chart,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        IReadOnlyList<SrgbColor> seriesColors)
    {
        var seriesFills = new List<ChartFillPlan>();
        var pointFills = new Dictionary<ChartFillKey, ChartFillPlan>();
        var markerFills = new Dictionary<ChartFillKey, ChartFillPlan>();
        bool pieLike = chart.ChartType is ChartType.Pie or ChartType.Doughnut;

        if (pieLike && chart.Series.Count > 0)
        {
            var firstSeries = chart.Series[0];
            for (int pointIndex = 0; pointIndex < seriesColors.Count; pointIndex++)
            {
                var pointFill = GetPointFill(firstSeries, pointIndex);
                var pointColor = GetPointFillColor(firstSeries, pointIndex);
                var fill = ResolveChartFillPlan(
                    pointFill ?? firstSeries.Fill,
                    pointColor ?? firstSeries.FillColor,
                    seriesColors[pointIndex],
                    theme,
                    effectiveClrMap);

                seriesFills.Add(fill);
                pointFills[new ChartFillKey(0, pointIndex)] = fill;
            }
        }
        else
        {
            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var fallback = seriesIndex < seriesColors.Count
                    ? seriesColors[seriesIndex]
                    : DefaultAccentColor(seriesIndex, theme);
                seriesFills.Add(ResolveChartFillPlan(
                    chart.Series[seriesIndex].Fill,
                    chart.Series[seriesIndex].FillColor,
                    fallback,
                    theme,
                    effectiveClrMap));
            }
        }

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            int pointCount = Math.Max(series.Values.Count, Math.Max(series.XValues.Count, series.BubbleSizes.Count));
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var pointFill = GetPointFill(series, pointIndex);
                var pointColor = GetPointFillColor(series, pointIndex);
                if (!pieLike && (pointFill is not null || pointColor is not null))
                {
                    var fallback = seriesIndex < seriesColors.Count
                        ? seriesColors[seriesIndex]
                        : DefaultAccentColor(seriesIndex, theme);
                    pointFills[new ChartFillKey(seriesIndex, pointIndex)] = ResolveChartFillPlan(
                        pointFill,
                        pointColor,
                        fallback,
                        theme,
                        effectiveClrMap);
                }

                var marker = series.PointStyles.TryGetValue(pointIndex, out var pointStyle) && pointStyle.Marker is not null
                    ? pointStyle.Marker
                    : series.MarkerStyle;
                if (marker?.Fill is not null || marker?.FillColor is not null)
                {
                    var fallback = pieLike && seriesIndex == 0 && pointIndex < seriesColors.Count
                        ? seriesColors[pointIndex]
                        : seriesIndex < seriesColors.Count
                            ? seriesColors[seriesIndex]
                            : DefaultAccentColor(seriesIndex, theme);
                    markerFills[new ChartFillKey(seriesIndex, pointIndex)] = ResolveChartFillPlan(
                        marker.Fill,
                        marker.FillColor,
                        fallback,
                        theme,
                        effectiveClrMap);
                }
            }
        }

        return new ChartFillPlanSet
        {
            SeriesFills = seriesFills,
            PointFills = pointFills,
            MarkerFills = markerFills
        };
    }

    private static ShapeFill? GetPointFill(ChartSeries series, int pointIndex) =>
        series.PointStyles.TryGetValue(pointIndex, out var pointStyle) ? pointStyle.Fill : null;

    private static ThemeAwareColor? GetPointFillColor(ChartSeries series, int pointIndex)
    {
        if (series.PointStyles.TryGetValue(pointIndex, out var pointStyle) && pointStyle.FillColor is not null)
            return pointStyle.FillColor;

        return series.PointColors.TryGetValue(pointIndex, out var pointColor) ? pointColor : null;
    }

    private static ChartFillPlan ResolveChartFillPlan(
        ShapeFill? fill,
        ThemeAwareColor? color,
        SrgbColor fallback,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        return fill switch
        {
            ShapeFill.Gradient gradient => new ChartFillPlan(fallback, ChartRenderPlanner.RectSeriesFillAlpha)
            {
                Fill = ResolveFill(gradient, theme, effectiveClrMap)
            },
            ShapeFill.Pattern pattern => new ChartFillPlan(fallback, ChartRenderPlanner.RectSeriesFillAlpha)
            {
                Fill = ResolveFill(pattern, theme, effectiveClrMap)
            },
            ShapeFill.Solid solid => new ChartFillPlan(
                ThemeColorResolver.Resolve(solid.Color, theme, effectiveClrMap),
                ChartRenderPlanner.RectSeriesFillAlpha),
            _ when color is not null => new ChartFillPlan(
                ThemeColorResolver.Resolve(color, theme, effectiveClrMap),
                ChartRenderPlanner.RectSeriesFillAlpha),
            _ => new ChartFillPlan(fallback, ChartRenderPlanner.RectSeriesFillAlpha)
        };
    }

    private static void ComposeSmartArt(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var smart = shape.SmartArt!;

        // ── Try live layout first ──────────────────────────────────────────────────
        if (smart.Data is not null && smart.Data.Family != SmartArtFamily.Unknown)
        {
            var liveShapes = SmartArtLayoutEngine.Layout(
                smart.Data,
                shape.OffsetXEmu, shape.OffsetYEmu,
                shape.ExtentCxEmu, shape.ExtentCyEmu,
                theme, effectiveClrMap,
                smart.QuickStyle,
                smart.Colors);

            if (liveShapes is not null)
            {
                foreach (var liveShape in liveShapes)
                    ComposeShape(liveShape, slide, presentation, theme, ops, effectiveClrMap: effectiveClrMap);
                return;
            }
        }

        // ── Cached drawing fallback ────────────────────────────────────────────────
        if (smart.FallbackShapes.Count > 0)
        {
            // Render each fallback shape as an ordinary AutoShape.
            foreach (var fallback in smart.FallbackShapes)
                ComposeShape(fallback, slide, presentation, theme, ops, effectiveClrMap: effectiveClrMap);
        }
        else
        {
            // No cached drawing — emit a grey placeholder rectangle at the frame bounds.
            var placeholder = new SlideShape
            {
                Id            = shape.Id,
                Name          = shape.Name,
                Kind          = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu    = shape.OffsetXEmu,
                OffsetYEmu    = shape.OffsetYEmu,
                ExtentCxEmu   = shape.ExtentCxEmu,
                ExtentCyEmu   = shape.ExtentCyEmu,
                Fill          = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xCC, 0xCC, 0xCC)))
            };
            ComposeAutoShape(placeholder, slide, presentation, theme, ops);
        }
    }

    /// <summary>Returns a default accent color for the given zero-based series index using the theme.</summary>
    private static SrgbColor DefaultAccentColor(int index, PresentationTheme theme)
    {
        var slot = (index % 6) switch
        {
            0 => ThemeColorSlot.Accent1,
            1 => ThemeColorSlot.Accent2,
            2 => ThemeColorSlot.Accent3,
            3 => ThemeColorSlot.Accent4,
            4 => ThemeColorSlot.Accent5,
            _ => ThemeColorSlot.Accent6
        };
        return theme.ColorScheme[slot];
    }

    private static ResolvedTextLayout ResolveTableCellTextLayout(
        TextBody body,
        TextFrameInsets insets,
        SrgbColor? styleTextColor,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        string defaultFont   = theme.FontScheme.MinorLatinFont;
        double defaultSizePt = 14.0; // typical table text default

        var resolvedParas = new List<ResolvedParagraph>(body.Paragraphs.Count);

        foreach (var para in body.Paragraphs)
        {
            var resolvedRuns = new List<ResolvedRun>(para.Runs.Count);
            foreach (var run in para.Runs)
            {
                SrgbColor color = run.Color is not null
                    ? ThemeColorResolver.Resolve(run.Color, theme, effectiveClrMap)
                    : (styleTextColor ?? SrgbColor.Black);

                resolvedRuns.Add(new ResolvedRun
                {
                    Text         = run.Text,
                    FontFamily   = run.FontFamily ?? defaultFont,
                    FontSizePt   = run.FontSizePt ?? defaultSizePt,
                    Bold         = run.Bold,
                    Italic       = run.Italic,
                    Underline    = run.Underline,
                    Strikethrough = run.Strikethrough,
                    Color        = color
                });
            }

            resolvedParas.Add(new ResolvedParagraph
            {
                Runs         = resolvedRuns,
                Align        = para.Align ?? TextAlign.Left,
                Level        = para.Level,
                BulletKind   = para.BulletKind,
                BulletChar   = para.BulletChar,
                SpaceBeforePt = para.SpaceBeforePt ?? 0,
                SpaceAfterPt  = para.SpaceAfterPt ?? 0
            });
        }

        return new ResolvedTextLayout
        {
            Paragraphs    = resolvedParas,
            Anchor        = VerticalAnchor.Top, // cell anchor handled by TableCellOp.Anchor
            InsetLeftDip  = insets.Left,
            InsetRightDip = insets.Right,
            InsetTopDip   = insets.Top,
            InsetBottomDip = insets.Bottom,
            Wrap          = body.Wrap,
            WarpPreset    = body.WarpPreset,
            WarpAdjusts   = body.WarpAdjusts.ToArray()
        };
    }

    // ─── Fill resolution ─────────────────────────────────────────────────────────────────────

    private static ResolvedFill ResolveFill(ShapeFill fill, PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null) => fill switch
    {
        ShapeFill.None => ResolvedFill.None.Instance,
        ShapeFill.Solid s => new ResolvedFill.Solid(ThemeColorResolver.Resolve(s.Color, theme, effectiveClrMap)),
        ShapeFill.Gradient g => new ResolvedFill.Gradient(
            g.Stops.Select(stop => new ResolvedFill.ResolvedGradientStop(
                stop.Position,
                ThemeColorResolver.Resolve(stop.Color, theme, effectiveClrMap))).ToArray(),
            g.Kind,
            g.AngleDegrees),
        ShapeFill.Picture p => new ResolvedFill.Picture(p.ImageBytes, p.ContentType, p.Tile),
        ShapeFill.Pattern pat => new ResolvedFill.PatternFill(
            pat.Preset,
            ThemeColorResolver.Resolve(pat.ForegroundColor, theme, effectiveClrMap),
            ThemeColorResolver.Resolve(pat.BackgroundColor, theme, effectiveClrMap)),
        _ => ResolvedFill.None.Instance
    };

    /// <summary>
    /// Default fill when a shape has no explicit fill: transparent for lines/connectors,
    /// else transparent.
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

    // ─── Outline resolution ──────────────────────────────────────────────────────────────────

    private static ResolvedOutline ResolveOutline(ShapeOutline outline, PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null) => outline switch
    {
        ShapeOutline.None => ResolvedOutline.None.Instance,
        ShapeOutline.Visible v => new ResolvedOutline.Visible(
            ThemeColorResolver.Resolve(v.Color, theme, effectiveClrMap),
            PointsToDip(v.WidthPt),
            v.Dash),
        // Wave 22B: gradient outline — resolve each gradient stop color
        ShapeOutline.GradientVisible gv => new ResolvedOutline.Gradient(
            ResolveGradientFill(gv.Gradient, theme, effectiveClrMap),
            PointsToDip(gv.WidthPt),
            gv.Dash),
        _ => ResolvedOutline.None.Instance
    };

    private static ResolvedFill.Gradient ResolveGradientFill(ShapeFill.Gradient g, PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var resolvedStops = g.Stops.Select(s =>
            new ResolvedFill.ResolvedGradientStop(
                s.Position,
                ThemeColorResolver.Resolve(s.Color, theme, effectiveClrMap))).ToArray();
        return new ResolvedFill.Gradient(resolvedStops, g.Kind, g.AngleDegrees);
    }

    // ─── Text layout resolution ──────────────────────────────────────────────────────────────

    private static string ResolveFieldText(FieldRun field, int slideIndex)
    {
        var t = field.FieldType.ToLowerInvariant();

        if (t.Contains("slidenum") || t == "\\slidenum" || t == "ppslidenum")
            return (slideIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // For cached text always use it (cached text is PowerPoint's baked-in value).
        if (!string.IsNullOrEmpty(field.CachedText))
            return field.CachedText;

        // No cached text — render a sensible fallback instead of the raw type token.
        // datetime / datetime1‥datetime13 → format current date in a readable form.
        if (t.StartsWith("datetime", StringComparison.Ordinal) || t == "date" || t == "time")
            return DateTime.Now.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        // footer / header / slidename with no cache → render empty (not the type token).
        return string.Empty;
    }

    // ─── MM3: Master/layout text-style inheritance ────────────────────────────────────────────

    /// <summary>
    /// Returns the placeholder category ("title", "body", or "other") for a given placeholder type.
    /// Used to look up the correct p:txStyles sub-element on the master.
    /// </summary>
    private static TextStyleCategory GetTextStyleCategory(PlaceholderType? type) => type switch
    {
        PlaceholderType.Title or PlaceholderType.CenteredTitle => TextStyleCategory.Title,
        PlaceholderType.Body or PlaceholderType.Object or PlaceholderType.SubTitle
            or PlaceholderType.Chart or PlaceholderType.Table or PlaceholderType.ClipArt
            or PlaceholderType.Diagram or PlaceholderType.Media or PlaceholderType.Picture
            => TextStyleCategory.Body,
        _ => TextStyleCategory.Other
    };

    private enum TextStyleCategory { Title, Body, Other }

    /// <summary>
    /// Resolves the effective <see cref="TextStyleLevel"/> for a paragraph at a given indent
    /// level by walking: layout placeholder's lstStyle → master txStyles (by category) → null.
    /// The caller applies the hard-coded fallback when this returns null.
    /// </summary>
    private static TextStyleLevel? ResolveTextStyleInheritance(
        int paraLevel,
        TextStyleCategory category,
        TextBody? layoutBody,
        MasterTextStyles? masterTextStyles)
    {
        // 1. Layout placeholder's a:lstStyle for this paragraph level.
        if (layoutBody?.LstStyle is { } layoutLst)
        {
            var lvl = layoutLst[paraLevel];
            if (lvl is not null) return lvl;
            // Walk upward toward level 0 only if the layout has any entry defined.
            for (int l = paraLevel - 1; l >= 0; l--)
            {
                lvl = layoutLst[l];
                if (lvl is not null) return lvl;
            }
        }

        // 2. Master p:txStyles category at this paragraph level.
        if (masterTextStyles is not null)
        {
            var masterStyle = category switch
            {
                TextStyleCategory.Title => masterTextStyles.TitleStyle,
                TextStyleCategory.Body  => masterTextStyles.BodyStyle,
                _                       => masterTextStyles.OtherStyle
            };
            return masterStyle.Resolve(paraLevel);
        }

        return null;
    }

    /// <summary>
    /// Resolves a latin font token (e.g. "+mj-lt" or "+mn-lt") to the actual theme font name.
    /// Other values are returned as-is.
    /// </summary>
    private static string ResolveLatinFont(string? font, PresentationTheme theme)
    {
        if (font is null) return string.Empty;
        return font switch
        {
            "+mj-lt" => theme.FontScheme.MajorLatinFont,
            "+mn-lt" => theme.FontScheme.MinorLatinFont,
            _        => font
        };
    }

    private static ResolvedTextLayout ResolveTextLayout(
        TextBody body,
        VerticalAnchor effectiveAnchor,
        TextAlign? effectiveDefaultAlign,
        Placeholder? placeholder,
        PresentationTheme theme,
        int slideIndex = 0,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null,
        TextBody? layoutBody = null,
        TextBody? masterBody = null,
        MasterTextStyles? masterTextStyles = null)
    {
        // Determine hard-coded fallback font size and font (last resort only).
        double fallbackFontSizePt = placeholder?.Type switch
        {
            PlaceholderType.Title or PlaceholderType.CenteredTitle => DefaultTitleFontSizePt,
            _ => DefaultBodyFontSizePt
        };

        // Determine fallback font from theme (last resort).
        string fallbackFont = placeholder?.Type switch
        {
            PlaceholderType.Title or PlaceholderType.CenteredTitle => theme.FontScheme.MajorLatinFont,
            _ => theme.FontScheme.MinorLatinFont
        };

        // Determine placeholder category for master txStyles lookup (MM3).
        var category = GetTextStyleCategory(placeholder?.Type);

        // The inherited default paragraph alignment (from lstStyle chain or placeholder type).
        // When not set anywhere, centered-title defaults to center, others to left.
        TextAlign fallbackAlign = effectiveDefaultAlign ?? placeholder?.Type switch
        {
            PlaceholderType.CenteredTitle => TextAlign.Center,
            PlaceholderType.SubTitle => TextAlign.Center,
            _ => TextAlign.Left
        };

        // Wave 19A: compute normAutofit scale — divide by 100000 (OOXML unit).
        bool hasStoredFontScale = body.FontScalePPT.HasValue && body.FontScalePPT.Value > 0;
        double fontScale    = hasStoredFontScale
            ? body.FontScalePPT!.Value / 100000.0 : 1.0;
        double lnSpcReduc   = body.LnSpcReductionPPT.HasValue && body.LnSpcReductionPPT.Value > 0
            ? body.LnSpcReductionPPT.Value / 100000.0 : 0.0;

        // Wave 19A: auto-number counters per level (0-based).
        // Counters are incremented when we encounter an Auto bullet paragraph at that level,
        // and reset when the level changes to a lower number (outer level).
        var autoNumCounters = new int[9]; // 1-based, 0 = not yet started
        int lastAutoNumLevel = -1;

        var resolvedParas = new List<ResolvedParagraph>(body.Paragraphs.Count);

        foreach (var para in body.Paragraphs)
        {
            var resolvedRuns = new List<ResolvedRun>(para.Runs.Count);

            // MM3: Resolve the inherited text-style level for this paragraph's indent level.
            // This is done once per paragraph since all runs in a paragraph share the same level.
            var inheritedStyle = ResolveTextStyleInheritance(
                para.Level, category, layoutBody, masterTextStyles);

            // Resolve inherited color from the style chain (if any).
            SrgbColor? inheritedColor = null;
            if (inheritedStyle?.Color is { } styleColor)
                inheritedColor = ThemeColorResolver.Resolve(styleColor, theme, effectiveClrMap);

            // Resolve inherited font from the style chain, expanding +mj-lt / +mn-lt tokens.
            string? inheritedFont = inheritedStyle?.LatinFont is { Length: > 0 } lf
                ? ResolveLatinFont(lf, theme)
                : null;

            foreach (var run in para.Runs)
            {
                // Resolve field text for a:fld runs (slide number, date, etc.)
                string resolvedText = run.Field is not null
                    ? ResolveFieldText(run.Field, slideIndex)
                    : run.Text;

                // Color: explicit run > field color > inherited style > Black.
                SrgbColor color;
                if (run.Field?.Color is SrgbColor fieldColor)
                    color = fieldColor;
                else if (run.Color is not null)
                    color = ThemeColorResolver.Resolve(run.Color, theme, effectiveClrMap);
                else if (inheritedColor.HasValue)
                    color = inheritedColor.Value;
                else
                    color = SrgbColor.Black;

                // Font family: explicit run > field font > inherited style > hard-coded fallback.
                // Expand +mj-lt / +mn-lt theme font tokens at each layer.
                string? fieldFont = run.Field?.FontFamily is { Length: > 0 } ff
                    ? ResolveLatinFont(ff, theme)
                    : null;
                string fontFamily = (run.FontFamily is { Length: > 0 } rf ? ResolveLatinFont(rf, theme) : null)
                    ?? fieldFont
                    ?? inheritedFont
                    ?? fallbackFont;

                // Font size: explicit run > field > inherited style > hard-coded fallback.
                // Wave 19A: apply normAutofit fontScale if set.
                double fontSizePt = (run.FontSizePt
                    ?? run.Field?.FontSizePt
                    ?? inheritedStyle?.FontSizePt
                    ?? fallbackFontSizePt) * fontScale;

                // Bold: PP1 fix — explicit b="0" (BoldSet=true, Bold=false) must beat inherited bold.
                // When BoldSet=true the run has a real a:rPr @b attribute (or was set by an editing
                // command) and its value wins unconditionally.
                // When BoldSet=false fall back to the original OR: run.Bold (programmatic/default)
                // or field or inherited style — preserving existing behaviour for programmatic Runs.
                bool bold = run.BoldSet
                    ? run.Bold
                    : (run.Bold || (run.Field?.Bold ?? false) || (inheritedStyle?.Bold ?? false));

                // Italic: same explicit-wins pattern.  PP1 fix.
                bool italic = run.ItalicSet
                    ? run.Italic
                    : (run.Italic || (run.Field?.Italic ?? false) || (inheritedStyle?.Italic ?? false));

                // Wave 16A: resolve text fill, outline, shadow
                ResolvedFill?   resolvedTextFill    = null;
                ResolvedOutline? resolvedTextOutline = null;
                ResolvedRunShadow? resolvedTextShadow = null;
                ResolvedRunReflection? resolvedTextReflection = null;
                ResolvedRunGlow? resolvedTextGlow = null;
                ResolvedRunSoftEdge? resolvedTextSoftEdge = null;

                if (run.TextFill is not null)
                    resolvedTextFill = ResolveFill(run.TextFill, theme, effectiveClrMap);

                if (run.TextOutline is not null)
                    resolvedTextOutline = ResolveOutline(run.TextOutline, theme, effectiveClrMap);

                if (run.TextShadow is not null)
                {
                    var ts = run.TextShadow;
                    var shadowColor = ThemeColorResolver.Resolve(ts.Color, theme, effectiveClrMap);
                    resolvedTextShadow = new ResolvedRunShadow
                    {
                        Color   = shadowColor,
                        Alpha   = ts.Alpha,
                        BlurDip = PointsToDip(ts.BlurPt),
                        DistDip = PointsToDip(ts.DistPt),
                        DirDeg  = ts.DirDeg,
                    };
                }

                if (run.TextReflection is not null)
                {
                    var reflection = run.TextReflection;
                    resolvedTextReflection = new ResolvedRunReflection
                    {
                        Alpha = reflection.Alpha,
                        BlurDip = PointsToDip(reflection.BlurPt),
                        DistDip = PointsToDip(reflection.DistPt),
                        DirDeg = reflection.DirDeg,
                        ScaleY = reflection.ScaleY,
                    };
                }

                if (run.TextGlow is not null)
                {
                    var glow = run.TextGlow;
                    resolvedTextGlow = new ResolvedRunGlow
                    {
                        Color = ThemeColorResolver.Resolve(glow.Color, theme, effectiveClrMap),
                        Alpha = glow.Alpha,
                        RadiusDip = PointsToDip(glow.RadiusPt),
                    };
                }

                if (run.TextSoftEdge is not null)
                {
                    resolvedTextSoftEdge = new ResolvedRunSoftEdge
                    {
                        RadiusDip = PointsToDip(run.TextSoftEdge.RadiusPt),
                    };
                }

                // Theme 27: OMML math — call the shared MathLayoutEngine to produce the box tree.
                // The engine is framework-free; WPF + Avalonia renderers walk the resulting box tree.
                FreeP.App.Compositor.MathLayout.MathBox.Container? mathLayout = null;
                if (run.Math is not null)
                {
                    var mathNode = FreeP.App.Compositor.MathLayout.OmmlParser.Parse(run.Math.RawXml, resolvedText);
                    mathLayout = FreeP.App.Compositor.MathLayout.MathLayoutEngine.Layout(mathNode, fontFamily, fontSizePt);
                }

                resolvedRuns.Add(new ResolvedRun
                {
                    Text          = resolvedText,
                    FontFamily    = fontFamily,
                    FontSizePt    = fontSizePt,
                    Bold          = bold,
                    Italic        = italic,
                    Underline     = run.Underline,
                    Strikethrough = run.Strikethrough,
                    Color         = color,
                    TextFill      = resolvedTextFill,
                    TextOutline   = resolvedTextOutline,
                    TextShadow    = resolvedTextShadow,
                    TextReflection = resolvedTextReflection,
                    TextGlow      = resolvedTextGlow,
                    TextSoftEdge  = resolvedTextSoftEdge,
                    MathLayout    = mathLayout,
                });
            }

            // Wave 18B: resolve tab stops (EMU → DIP)
            IReadOnlyList<ResolvedTabStop> resolvedTabStops = para.TabStops.Count > 0
                ? para.TabStops.Select(t => new ResolvedTabStop
                    {
                        PositionDip = t.PositionEmu / EmuPerDip,
                        Alignment   = t.Alignment
                    }).ToList()
                : Array.Empty<ResolvedTabStop>();

            // Wave 19A: bullet computation ─────────────────────────────────────────
            // Determine effective bullet kind (paragraph > inherited style).
            var effectiveBulletKind = para.BulletKind;
            string? effectiveBulletChar = para.BulletChar;
            AutoNumType effectiveAutoNumType = para.AutoNumType;
            ThemeAwareColor? effectiveBulletColor = null;
            if (para.BulletColorFollowsText)
                effectiveBulletColor = null;
            else if (para.BulletColor is not null)
                effectiveBulletColor = para.BulletColor;
            else if (inheritedStyle?.BulletColorFollowsText == true)
                effectiveBulletColor = null;
            else
                effectiveBulletColor = inheritedStyle?.BulletColor;

            double? effectiveBulletSizePt = null;
            int? effectiveBulletSizePct = null;
            if (para.BulletSizeFollowsText)
            {
                effectiveBulletSizePt = null;
                effectiveBulletSizePct = null;
            }
            else if (para.BulletSizePt.HasValue)
            {
                effectiveBulletSizePt = para.BulletSizePt;
            }
            else if (para.BulletSizePct.HasValue)
            {
                effectiveBulletSizePct = para.BulletSizePct;
            }
            else if (inheritedStyle?.BulletSizeFollowsText == true)
            {
                effectiveBulletSizePt = null;
                effectiveBulletSizePct = null;
            }
            else if (inheritedStyle?.BulletSizePt.HasValue == true)
            {
                effectiveBulletSizePt = inheritedStyle.BulletSizePt;
            }
            else
            {
                effectiveBulletSizePct = inheritedStyle?.BulletSizePct;
            }

            string? effectiveBulletFont = null;
            if (para.BulletFontFollowsText)
                effectiveBulletFont = null;
            else if (!string.IsNullOrEmpty(para.BulletFontFamily))
                effectiveBulletFont = para.BulletFontFamily;
            else if (inheritedStyle?.BulletFontFollowsText == true)
                effectiveBulletFont = null;
            else
                effectiveBulletFont = inheritedStyle?.BulletFontFamily;

            // BU1: When paragraph has an explicit <a:buNone/>, BulletSuppressed is true — do NOT
            // re-inherit the style bullet; the paragraph actively opts out of any inherited bullet.
            // Only inherit when the bullet element is simply absent (BulletSuppressed == false).
            if (!para.BulletSuppressed && effectiveBulletKind == BulletKind.None && inheritedStyle?.BulletKind.HasValue == true)
            {
                effectiveBulletKind = inheritedStyle.BulletKind!.Value;
                if (effectiveBulletKind == BulletKind.Char && effectiveBulletChar is null)
                    effectiveBulletChar = inheritedStyle.BulletChar;
                if (effectiveBulletKind == BulletKind.Auto)
                    effectiveAutoNumType = inheritedStyle.AutoNumType;
            }

            // Build bullet text and per-paragraph indent info.
            string bulletText = string.Empty;
            var bulletSeedRun = SelectBulletSeedRun(resolvedRuns);
            SrgbColor bulletColor = bulletSeedRun?.Color ?? SrgbColor.Black;
            string bulletFontFamily = bulletSeedRun?.FontFamily ?? fallbackFont;
            double bulletFontSizePt = bulletSeedRun?.FontSizePt ?? (fallbackFontSizePt * fontScale);
            double indentDip = 0.0;
            double hangingDip = 0.0;

            // Resolve marL/indent for indentation.
            long marLEmu = para.MarginLeftEmu ?? inheritedStyle?.MarginLeftEmu ?? 0;
            long indentEmu = para.IndentEmu ?? inheritedStyle?.IndentEmu ?? 0;
            if (marLEmu > 0)
                indentDip = marLEmu / EmuPerDip;
            if (indentEmu < 0)
                hangingDip = -indentEmu / EmuPerDip; // hanging: bullet at indentDip-hangingDip

            // Override bullet color when explicitly set.
            if (effectiveBulletColor is not null)
                bulletColor = ThemeColorResolver.Resolve(effectiveBulletColor, theme, effectiveClrMap);

            // Override bullet font when set.
            if (!string.IsNullOrEmpty(effectiveBulletFont))
                bulletFontFamily = ResolveLatinFont(effectiveBulletFont, theme);

            // Resolve bullet size from buSzPts or buSzPct after run/theme fallback.
            if (effectiveBulletSizePt.HasValue && effectiveBulletSizePt.Value > 0)
                bulletFontSizePt = effectiveBulletSizePt.Value * fontScale;
            else if (effectiveBulletSizePct.HasValue && effectiveBulletSizePct.Value > 0)
                bulletFontSizePt = bulletFontSizePt * effectiveBulletSizePct.Value / 100000.0;

            switch (effectiveBulletKind)
            {
                case BulletKind.Char:
                    bulletText = effectiveBulletChar ?? "•";
                    break;

                case BulletKind.Auto:
                {
                    // BU3: clamp level to valid array range [0,8] as defense-in-depth.
                    // The reader also clamps, but this guard catches programmatically-set
                    // out-of-range values (e.g. Level=9 set directly on the model).
                    int level = Math.Clamp(para.Level, 0, 8);
                    // Reset inner levels when we move to an outer level.
                    if (level < lastAutoNumLevel)
                        for (int li = level + 1; li < 9; li++) autoNumCounters[li] = 0;
                    // Also reset this level when we haven't seen it yet (counter == 0).
                    // Use para.AutoNumStartAt if it's the first paragraph at this level.
                    if (autoNumCounters[level] == 0)
                        autoNumCounters[level] = para.AutoNumStartAt;
                    else
                        autoNumCounters[level]++;
                    lastAutoNumLevel = level;
                    bulletText = FormatAutoNum(effectiveAutoNumType, autoNumCounters[level]);
                    break;
                }
            }

            resolvedParas.Add(new ResolvedParagraph
            {
                Runs = resolvedRuns,
                // P0: use the inherited default alignment when the paragraph has no explicit align.
                Align = para.Align ?? fallbackAlign,
                Level = para.Level,
                BulletKind = effectiveBulletKind,
                BulletChar = effectiveBulletChar,
                SpaceBeforePt = para.SpaceBeforePt ?? 0,
                SpaceAfterPt = para.SpaceAfterPt ?? 0,
                TabStops = resolvedTabStops,  // Wave 18B
                // Wave 19A:
                BulletText       = bulletText,
                BulletColor      = bulletColor,
                BulletFontFamily = bulletFontFamily,
                BulletFontSizePt = bulletFontSizePt,
                IndentDip        = indentDip,
                HangingDip       = hangingDip,
            });
        }

        var insets = TextFrameLayoutPlanner.FromOptionalInsets(
            PointsToDip(body.InsetLeftPt ?? layoutBody?.InsetLeftPt ?? masterBody?.InsetLeftPt),
            PointsToDip(body.InsetTopPt ?? layoutBody?.InsetTopPt ?? masterBody?.InsetTopPt),
            PointsToDip(body.InsetRightPt ?? layoutBody?.InsetRightPt ?? masterBody?.InsetRightPt),
            PointsToDip(body.InsetBottomPt ?? layoutBody?.InsetBottomPt ?? masterBody?.InsetBottomPt),
            DefaultInsetHorzDip,
            DefaultInsetVertDip);

        return new ResolvedTextLayout
        {
            Paragraphs = resolvedParas,
            // P0: use the resolved effective anchor (from shape -> layout -> master chain).
            Anchor = effectiveAnchor,
            InsetLeftDip = insets.Left,
            InsetRightDip = insets.Right,
            InsetTopDip = insets.Top,
            InsetBottomDip = insets.Bottom,
            Wrap = body.Wrap,
            WarpPreset = body.WarpPreset,   // Wave 16A
            WarpAdjusts = body.WarpAdjusts.ToArray(),
            VerticalType = body.VerticalType,  // Wave 18B
            AutoFitKind = body.AutoFitKind,
            HasStoredFontScale = hasStoredFontScale,
            FontScale = fontScale,            // Wave 19A
            LnSpcReduction = lnSpcReduc,      // Wave 19A
            // Wave 22B: text columns
            ColumnCount = Math.Max(1, body.ColumnCount),
            ColumnSpacingDip = body.ColumnSpacingEmu > 0 ? body.ColumnSpacingEmu / EmuPerDip : 0.0,
        };
    }

    // ─── Wave 19A: auto-number formatter ────────────────────────────────────────────────────

    /// <summary>
    /// Formats an auto-numbered bullet counter value according to the given type.
    /// <paramref name="n"/> is 1-based.
    /// </summary>
    public static string FormatAutoNum(AutoNumType type, int n)
    {
        if (n < 1) n = 1;
        return type switch
        {
            AutoNumType.ArabicPeriod     => $"{n}.",
            AutoNumType.ArabicParenR     => $"{n})",
            AutoNumType.ArabicParenBoth  => $"({n})",
            AutoNumType.RomanUcPeriod    => $"{ToRoman(n, upper: true)}.",
            AutoNumType.RomanLcPeriod    => $"{ToRoman(n, upper: false)}.",
            AutoNumType.RomanUcParenR    => $"{ToRoman(n, upper: true)})",
            AutoNumType.RomanLcParenR    => $"{ToRoman(n, upper: false)})",
            AutoNumType.AlphaUcPeriod    => $"{ToAlpha(n, upper: true)}.",
            AutoNumType.AlphaLcPeriod    => $"{ToAlpha(n, upper: false)}.",
            AutoNumType.AlphaUcParenR    => $"{ToAlpha(n, upper: true)})",
            AutoNumType.AlphaLcParenR    => $"{ToAlpha(n, upper: false)})",
            AutoNumType.AlphaUcParenBoth => $"({ToAlpha(n, upper: true)})",
            AutoNumType.AlphaLcParenBoth => $"({ToAlpha(n, upper: false)})",
            _                            => $"{n}."
        };
    }

    private static ResolvedRun? SelectBulletSeedRun(IReadOnlyList<ResolvedRun> runs) =>
        runs.FirstOrDefault(run => run.Text.Length > 0)
        ?? runs.FirstOrDefault();

    private static string ToRoman(int n, bool upper)
    {
        if (n < 1 || n > 3999) return n.ToString();
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var syms   = upper
            ? new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" }
            : new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
            while (n >= values[i]) { sb.Append(syms[i]); n -= values[i]; }
        return sb.ToString();
    }

    private static string ToAlpha(int n, bool upper)
    {
        // 1→A, 26→Z, 27→AA, etc.
        var sb = new System.Text.StringBuilder();
        while (n > 0)
        {
            n--; // convert to 0-based
            sb.Insert(0, (char)((upper ? 'A' : 'a') + (n % 26)));
            n /= 26;
        }
        return sb.ToString();
    }

    // ─── Unit helpers ────────────────────────────────────────────────────────────────────────

    private static LayoutRect AnchorToBounds(ResolvedAnchor anchor) =>
        new(anchor.OffsetXEmu / EmuPerDip,
            anchor.OffsetYEmu / EmuPerDip,
            anchor.ExtentCxEmu / EmuPerDip,
            anchor.ExtentCyEmu / EmuPerDip);

    /// <summary>Converts typographic points to DIP (96/72 = 4/3 scaling).</summary>
    private static double PointsToDip(double pt) => pt * (96.0 / 72.0);

    private static double? PointsToDip(double? pt) => pt.HasValue ? PointsToDip(pt.Value) : null;
}
