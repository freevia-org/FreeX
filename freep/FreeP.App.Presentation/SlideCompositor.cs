using Free.Shared.Drawing;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;
using System.Globalization;
using System.Xml.Linq;

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
    /// <param name="includeBackground">When false, omit the destination slide background for a Zoom transition.</param>
    public static IReadOnlyList<DrawOp> Compose(
        PresentationModel presentation,
        Slide slide,
        int slideIndex = 0,
        bool includeBackground = true)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);

        var ops = new List<DrawOp>();

        // Compute the effective color map for this slide (ECMA-376 §19.3.1.20 / §14.2.9):
        //   slide.ColorMapOverride (from p:clrMapOvr/a:overrideClrMapping on the slide)
        //     ?? layout.ColorMapOverride (from p:clrMapOvr/a:overrideClrMapping on the layout)
        //     ?? master's ColorMap (from p:clrMap)
        //     ?? null (ThemeColorResolver falls back to the default Office mapping).
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        var master = presentation.Masters.Find(m => m.Id == layout?.MasterId)
                  ?? presentation.Masters.FirstOrDefault();
        IReadOnlyDictionary<string, string>? effectiveClrMap =
            slide.ColorMapOverride as IReadOnlyDictionary<string, string>
            ?? layout?.ColorMapOverride as IReadOnlyDictionary<string, string>
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
        if (includeBackground)
        {
            var bgFill = ResolveBackground(slide, presentation, theme, effectiveClrMap);
            ops.Add(new DrawOp.Background { Fill = bgFill, BoundsDip = slideBounds });
        }

        // 2. Master/layout decoration in z-order. Placeholder roots remain inheritance-only;
        // showMasterSp controls authored master decoration without hiding the background.
        // Three independent gates: presentation.ShowMasterShapes is FreeP's Slide Show Settings
        // toggle (applies to every slide in the deck for the duration of a slideshow session);
        // slide.ShowMasterShapes is the authored per-slide p:sld/@showMasterSp ("Hide Background
        // Graphics" in PowerPoint's Design tab — some slides in a deck can hide it, others not);
        // layout.ShowMasterShapes is the authored per-layout p:sldLayout/@showMasterSp ("Hide
        // Background Graphics" set against a layout in Slide Master view — every slide using that
        // layout inherits the hidden master decoration unless it overrides showMasterSp itself).
        // All three must be true for a given slide to show its master's decoration shapes.
        if (presentation.ShowMasterShapes && slide.ShowMasterShapes && (layout?.ShowMasterShapes ?? true) && master is not null)
        {
            foreach (var shape in master.Placeholders.Where(shape => shape.Placeholder is null))
                ComposeShape(shape, slide, presentation, theme, ops, slideIndex, effectiveClrMap);
        }

        if (layout is not null)
        {
            foreach (var shape in layout.Placeholders.Where(shape => shape.Placeholder is null))
                ComposeShape(shape, slide, presentation, theme, ops, slideIndex, effectiveClrMap);
        }

        // 3. Shapes in z-order (back to front)
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

    /// <summary>
    /// Maximum group nesting composed for a slide. Mirrors the reader's own limit: composition
    /// descends one call frame per nested group, and StackOverflowException is uncatchable, so it
    /// would kill the process outright rather than being contained by the render-pass guard.
    /// </summary>
    private const int MaxComposeGroupNestingDepth = 64;

    private static void ComposeShape(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        int slideIndex = 0,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null,
        int groupDepth = 0)
    {
        if (shape.IsHidden)
            return;
        if (groupDepth > MaxComposeGroupNestingDepth)
            return;

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
                // Flatten group children (still no group-level ROTATION transform — pre-existing
                // gap, out of scope here). Children are stored with absolute slide-space EMU
                // coordinates authored relative to the group's a:chOff/a:chExt child space; for a
                // freshly-authored (never-resized) group that space is numerically identical to
                // the group's own off/ext, so most files need no correction. When PowerPoint
                // resizes a group after its children were authored, chOff/chExt stay at their
                // original values while off/ext move to the new box, so the two spaces diverge —
                // apply that mapping here so descendants land where PowerPoint actually renders
                // them: absolute = groupOff + (childRaw - chOff) * (groupExt / chExt).
                foreach (var child in shape.Children)
                    ComposeShape(TransformGroupChild(shape, child), slide, presentation, theme, ops, slideIndex, effectiveClrMap, groupDepth + 1);
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
            // Host interaction can externally activate the preserved payload.
            case SlideShapeKind.Ole:
                ComposeOle(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            // Wave 25A: preserved modern objects — fallback preview image or grey placeholder.
            case SlideShapeKind.Zoom:
            case SlideShapeKind.Model3d:
            case SlideShapeKind.PreservedObject:
                if (shape.PreservedObject is not null)
                    ComposePreservedObject(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            case SlideShapeKind.Ink:
                ComposeInk(shape, slide, presentation, theme, ops, effectiveClrMap);
                break;

            default:
                ComposeAutoShape(shape, slide, presentation, theme, ops, slideIndex, effectiveClrMap);
                break;
        }
    }

    /// <summary>
    /// Maps a group child's authored (child-space) offset/extent into the group's own absolute
    /// space using the standard ECMA-376 group transform: absolute = groupOff + (raw - chOff) *
    /// (groupExt / chExt). Returns the original child unchanged (no allocation) when the group's
    /// child space is absent or numerically identical to its own off/ext — the overwhelmingly
    /// common case for groups that were never resized after authoring, including every group this
    /// app itself writes (PptxPackageWriter always emits chOff==off, chExt==ext).
    /// </summary>
    private static SlideShape TransformGroupChild(SlideShape group, SlideShape child)
    {
        long chOffX = group.ChildOffsetXEmu ?? group.OffsetXEmu;
        long chOffY = group.ChildOffsetYEmu ?? group.OffsetYEmu;
        long chExtCx = group.ChildExtentCxEmu ?? group.ExtentCxEmu;
        long chExtCy = group.ChildExtentCyEmu ?? group.ExtentCyEmu;

        if (chOffX == group.OffsetXEmu && chOffY == group.OffsetYEmu &&
            chExtCx == group.ExtentCxEmu && chExtCy == group.ExtentCyEmu)
        {
            return child; // identity transform — nothing to correct
        }

        double scaleX = chExtCx != 0 ? (double)group.ExtentCxEmu / chExtCx : 1.0;
        double scaleY = chExtCy != 0 ? (double)group.ExtentCyEmu / chExtCy : 1.0;

        long absX  = group.OffsetXEmu + (long)Math.Round((child.OffsetXEmu - chOffX) * scaleX);
        long absY  = group.OffsetYEmu + (long)Math.Round((child.OffsetYEmu - chOffY) * scaleY);
        long absCx = (long)Math.Round(child.ExtentCxEmu * scaleX);
        long absCy = (long)Math.Round(child.ExtentCyEmu * scaleY);

        return child.WithTransformedBounds(absX, absY, absCx, absCy);
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
        // An explicit zero-sized slide placeholder is hidden in PowerPoint. Do not let normal
        // placeholder inheritance turn its authored zero transform into visible layout geometry.
        if (shape.HasExplicitZeroExtentTransform && shape.Placeholder is not null)
            return;

        // Resolve anchor (placeholder inheritance).
        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        // Build geometry from the resolved bounds.
        var geometry = shape.CustomGeometry.Count > 0
            ? CustomGeometryBuilder.BuildCustom(shape.CustomGeometry, boundsDip)
            : ShapeGeometryBuilder.Build(shape.AutoShapeKind, boundsDip, shape.PresetGeometryAdjustments);

        // Placeholder inheritance source (layout, then master) -- shared by fill/outline/text
        // resolution below. A shape that is itself a placeholder and omits Fill/Outline/TextBody
        // is the normal "inherit from layout/master" authoring pattern (PowerPoint itself omits
        // spPr fill/line on slide placeholders that inherit), so those lookups must happen
        // regardless of which of the three properties are missing.
        var placeholderLayoutPh = shape.Placeholder is not null
            ? PlaceholderResolver.FindLayoutPlaceholder(shape.Placeholder, slide, presentation)
            : null;
        var placeholderMasterPh = shape.Placeholder is not null
            ? PlaceholderResolver.FindMasterPlaceholder(shape.Placeholder, slide, presentation)
            : null;

        // Resolve fill: shape's own fill, else layout placeholder's, else master placeholder's,
        // else the transparent default.
        var effectiveFillSource = shape.Fill ?? placeholderLayoutPh?.Fill ?? placeholderMasterPh?.Fill;
        var fill = effectiveFillSource is not null
            ? ResolveFill(effectiveFillSource, theme, effectiveClrMap)
            : InferDefaultFill(shape, theme);
        if (shape.Effects?.Scene3d is not null)
            fill = ResolveScene3dMaterialFill(fill);

        // Resolve outline: shape's own outline, else layout placeholder's, else master
        // placeholder's, else no outline.
        var effectiveOutlineSource = shape.Outline ?? placeholderLayoutPh?.Outline ?? placeholderMasterPh?.Outline;
        var outline = effectiveOutlineSource is not null
            ? ResolveOutline(effectiveOutlineSource, theme, effectiveClrMap)
            : ResolvedOutline.None.Instance;

        // Resolve text.
        ResolvedTextLayout? text = null;
        if (shape.TextBody is not null && shape.TextBody.Paragraphs.Count > 0)
        {
            // P0: Walk shape -> layout placeholder -> master placeholder to resolve
            // the effective vertical anchor and default paragraph alignment.
            var layoutPh = placeholderLayoutPh;
            var masterPh = placeholderMasterPh;

            var effectiveAnchor = ResolveVerticalAnchor(shape.TextBody, layoutPh?.TextBody, masterPh?.TextBody, shape.Placeholder);
            var effectiveDefaultAlign = ResolveDefaultParaAlign(shape.TextBody, layoutPh?.TextBody, masterPh?.TextBody);
            var effectiveDefaultRightToLeft = ResolveDefaultParaRightToLeft(
                shape.TextBody, layoutPh?.TextBody, masterPh?.TextBody);

            // MM3: resolve the master TextStyles for this slide's master (for text-style inheritance).
            var resolvedLayout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
            var resolvedMaster = resolvedLayout is not null
                ? presentation.Masters.Find(m => m.Id == resolvedLayout.MasterId)
                : presentation.Masters.FirstOrDefault();

            // PowerPoint applies a shape effect to the glyphs of a text-only,
            // no-fill text box. Carry that shadow onto runs so renderers do not
            // shadow the empty rectangular frame instead.
            var inheritedTextShadow = shape.Fill is ShapeFill.None && shape.Effects?.HasOuterShadow == true
                ? ResolveShapeShadowAsTextShadow(shape.Effects)
                : null;

            var textInsets = TextFrameLayoutPlanner.FromOptionalInsets(
                PointsToDip(shape.TextBody.InsetLeftPt ?? layoutPh?.TextBody?.InsetLeftPt ?? masterPh?.TextBody?.InsetLeftPt),
                PointsToDip(shape.TextBody.InsetTopPt ?? layoutPh?.TextBody?.InsetTopPt ?? masterPh?.TextBody?.InsetTopPt),
                PointsToDip(shape.TextBody.InsetRightPt ?? layoutPh?.TextBody?.InsetRightPt ?? masterPh?.TextBody?.InsetRightPt),
                PointsToDip(shape.TextBody.InsetBottomPt ?? layoutPh?.TextBody?.InsetBottomPt ?? masterPh?.TextBody?.InsetBottomPt),
                DefaultInsetHorzDip,
                DefaultInsetVertDip);

            text = ResolveTextLayout(shape.TextBody, presentation, effectiveAnchor, effectiveDefaultAlign,
                effectiveDefaultRightToLeft, shape.Placeholder,
                theme, slideIndex, effectiveClrMap, layoutPh?.TextBody, masterPh?.TextBody, resolvedMaster?.TextStyles,
                inheritedTextShadow,
                Math.Max(1, boundsDip.Width - textInsets.Left - textInsets.Right));
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

    private static ResolvedFill ResolveScene3dMaterialFill(ResolvedFill fill)
    {
        if (fill is not ResolvedFill.Solid solid)
            return fill;

        // PowerPoint's default 3-D material/light pass lifts the authored face
        // color slightly; raw theme colors make imported 3-D shapes visibly flat.
        static byte Lift(byte channel) =>
            (byte)Math.Clamp((int)Math.Round(channel * 1.05 + 3), 0, 255);

        return new ResolvedFill.Solid(
            new SrgbColor(Lift(solid.Color.R), Lift(solid.Color.G), Lift(solid.Color.B)),
            solid.Alpha);
    }

    private static ResolvedShapeEffects? ResolveEffects(ShapeEffects? fx)
    {
        if (fx is null) return null;

        bool hasBevel = fx.BevelTop is not null || fx.BevelBottom is not null;
        if (!fx.HasOuterShadow && !fx.HasGlow && !fx.HasSoftEdge && fx.Reflection is null
            && !hasBevel && fx.ExtrusionHeightEmu == 0 && fx.ContourWidthEmu == 0
            && string.IsNullOrEmpty(fx.PrstMaterial) && fx.Scene3d is null)
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

            HasReflection     = fx.Reflection is not null,
            ReflectionAlpha   = (byte)Math.Clamp(
                (int)Math.Round((fx.Reflection?.StartAlpha ?? 0) * 255d / 100000d), 0, 255),
            ReflectionBlurDip = (fx.Reflection?.BlurRadEmu ?? 0) / EmuPerDip,
            ReflectionDistDip = (fx.Reflection?.DistEmu ?? 0) / EmuPerDip,
            ReflectionDirDeg  = fx.Reflection?.DirDeg ?? 90,
            ReflectionScaleY  = (fx.Reflection?.ScaleYPercent ?? -100) / 100.0,
            ReflectionEndPos  = Math.Clamp((fx.Reflection?.EndPos ?? 100000) / 100000.0, 0, 1),

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
            ExtrusionColor    = fx.ExtrusionColor,
            PrstMaterial      = fx.PrstMaterial,
            ContourWidthDip   = fx.ContourWidthEmu    / EmuPerDip,
            ContourColor      = fx.ContourColor,
            LightDirDeg       = ResolveLightDir(fx.Scene3d),
            Scene3dCameraPreset = fx.Scene3d?.CameraPreset ?? string.Empty,
        };
    }

    private static ResolvedRunShadow ResolveShapeShadowAsTextShadow(ShapeEffects effects) => new()
    {
        Color = effects.OuterShadowColor,
        Alpha = effects.OuterShadowAlpha,
        BlurDip = effects.OuterShadowBlurRadEmu / EmuPerDip,
        DistDip = effects.OuterShadowDistEmu / EmuPerDip,
        DirDeg = effects.OuterShadowDirDeg,
    };

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

    private static bool? ResolveDefaultParaRightToLeft(
        TextBody body,
        TextBody? layoutBody,
        TextBody? masterBody)
    {
        if (body.DefaultParaRightToLeft.HasValue) return body.DefaultParaRightToLeft.Value;
        if (layoutBody?.DefaultParaRightToLeft.HasValue == true) return layoutBody.DefaultParaRightToLeft!.Value;
        if (masterBody?.DefaultParaRightToLeft.HasValue == true) return masterBody.DefaultParaRightToLeft!.Value;
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
            FlipH        = anchor.FlipH,
            FlipV        = anchor.FlipV,
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
    /// In-place OLE hosting is deferred; the host can externally activate the payload.
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
                FlipH       = anchor.FlipH,
                FlipV       = anchor.FlipV,
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

    private static bool TryComposeSummaryZoomPreviews(
        SlideShape shape,
        LayoutRect boundsDip,
        double rotationDeg,
        bool flipH,
        bool flipV,
        List<DrawOp> ops,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var info = shape.PreservedObject;
        if (info is null || string.IsNullOrWhiteSpace(info.RawXml))
            return false;

        XElement raw;
        try
        {
            raw = XElement.Parse(info.RawXml);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        var objects = raw.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "summaryZmObj",
                StringComparison.OrdinalIgnoreCase))
            .Take(info.SummaryZoomTargets.Count)
            .ToArray();
        if (objects.Length == 0)
            return false;

        var tileOps = new List<DrawOp>();
        var composed = false;
        for (var index = 0; index < info.SummaryZoomTargets.Count && index < objects.Length; index++)
        {
            var target = info.SummaryZoomTargets[index];
            var tileBounds = new LayoutRect(
                boundsDip.X + boundsDip.Width * target.OffsetFactorX / 100000d,
                boundsDip.Y + boundsDip.Height * target.OffsetFactorY / 100000d,
                boundsDip.Width * target.ScaleFactorX / 100000d,
                boundsDip.Height * target.ScaleFactorY / 100000d);

            var properties = objects[index].Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "zmPr",
                    StringComparison.OrdinalIgnoreCase));
            var crop = ResolveZoomCrop(properties, info.ZoomProperties);
            var outline = ResolveZoomFrameOutline(properties, info.ZoomProperties, theme, effectiveClrMap);
            var geometry = ResolveZoomFrameGeometry(properties, info.ZoomProperties);
            var relId = properties?.Descendants()
                .SelectMany(element => element.Attributes())
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "embed",
                    StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(relId)
                || !info.SlideRels.TryGetValue(relId, out var relation)
                || !info.Parts.TryGetValue(relation.TargetPath, out var bytes)
                || bytes.Length == 0)
            {
                AddSummaryZoomPlaceholder(shape.Id, tileBounds, rotationDeg, flipH, flipV, outline, tileOps);
                continue;
            }

            tileOps.Add(new DrawOp.Picture
            {
                ShapeId = shape.Id,
                Bytes = bytes,
                ContentType = info.PartContentTypes.TryGetValue(relation.TargetPath, out var contentType)
                    ? contentType
                    : "image/png",
                DestDip = tileBounds,
                RotationDeg = rotationDeg,
                FlipH = flipH,
                FlipV = flipV,
                IsCover = IsZoomCover(properties, info.ZoomProperties),
                CropLeft = crop.Left,
                CropTop = crop.Top,
                CropRight = crop.Right,
                CropBottom = crop.Bottom,
                Outline = outline,
                Effects = ResolveZoomFrameEffects(properties, info.ZoomProperties),
                PictureFrameGeometry = geometry,
            });
            composed = true;
        }

        if (composed)
            ops.AddRange(tileOps);

        return composed;
    }

    private static bool TryComposeSingleZoomPreview(
        SlideShape shape,
        LayoutRect boundsDip,
        double rotationDeg,
        bool flipH,
        bool flipV,
        List<DrawOp> ops,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var info = shape.PreservedObject;
        if (info is null || string.IsNullOrWhiteSpace(info.RawXml))
            return false;

        XElement raw;
        try
        {
            raw = XElement.Parse(info.RawXml);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        var properties = raw.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "zmPr",
                StringComparison.OrdinalIgnoreCase));
        var crop = ResolveZoomCrop(properties, info.ZoomProperties);
        var outline = ResolveZoomFrameOutline(properties, info.ZoomProperties, theme, effectiveClrMap);
        var geometry = ResolveZoomFrameGeometry(properties, info.ZoomProperties);
        var relId = properties?.Descendants()
            .SelectMany(element => element.Attributes())
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "embed",
                StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(relId)
            || !info.SlideRels.TryGetValue(relId, out var relation)
            || !info.Parts.TryGetValue(relation.TargetPath, out var bytes)
            || bytes.Length == 0)
        {
            return false;
        }

        ops.Add(new DrawOp.Picture
        {
            ShapeId = shape.Id,
            Bytes = bytes,
            ContentType = info.PartContentTypes.TryGetValue(relation.TargetPath, out var contentType)
                ? contentType
                : "image/png",
            DestDip = boundsDip,
            RotationDeg = rotationDeg,
            FlipH = flipH,
            FlipV = flipV,
            IsCover = IsZoomCover(properties, info.ZoomProperties),
            CropLeft = crop.Left,
            CropTop = crop.Top,
            CropRight = crop.Right,
            CropBottom = crop.Bottom,
            Outline = outline,
            Effects = ResolveZoomFrameEffects(properties, info.ZoomProperties),
            PictureFrameGeometry = geometry,
        });
        return true;
    }

    private static (double Left, double Top, double Right, double Bottom) ResolveZoomCrop(
        XElement? properties,
        ZoomObjectProperties? fallback)
    {
        var sourceRect = properties?.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "srcRect",
                StringComparison.OrdinalIgnoreCase));

        return (
            NormalizeZoomCrop(ReadCropValue(sourceRect, "l", fallback?.CropLeft)),
            NormalizeZoomCrop(ReadCropValue(sourceRect, "t", fallback?.CropTop)),
            NormalizeZoomCrop(ReadCropValue(sourceRect, "r", fallback?.CropRight)),
            NormalizeZoomCrop(ReadCropValue(sourceRect, "b", fallback?.CropBottom)));
    }

    private static bool IsZoomCover(XElement? properties, ZoomObjectProperties? fallback) =>
        string.Equals(
            properties?.Attribute("imageType")?.Value ?? fallback?.ImageType,
            "cover",
            StringComparison.OrdinalIgnoreCase);

    private static int? ReadCropValue(XElement? sourceRect, string attributeName, int? fallback)
    {
        var text = sourceRect?.Attribute(attributeName)?.Value;
        return int.TryParse(text, out var value) ? value : fallback;
    }

    private static double NormalizeZoomCrop(int? value) =>
        value.HasValue
            ? Math.Clamp(value.Value / 100000d, 0, 1)
            : 0;

    private static ResolvedShapeEffects? ResolveZoomFrameEffects(
        XElement? properties,
        ZoomObjectProperties? fallback)
    {
        var shapeProperties = properties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        var effectList = shapeProperties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase));
        var shadow = effectList?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "outerShdw", StringComparison.OrdinalIgnoreCase));
        var glow = effectList?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "glow", StringComparison.OrdinalIgnoreCase));
        var fallbackShadow = fallback?.FrameBorderShadowEnabled == false
            ? null
            : fallback?.FrameBorderShadow;
        var fallbackGlow = fallback?.FrameBorderGlowEnabled == false
            ? null
            : fallback?.FrameBorderGlow;
        var softEdge = effectList?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "softEdge", StringComparison.OrdinalIgnoreCase));
        var fallbackSoftEdge = fallback?.FrameBorderSoftEdgeEnabled == false
            ? null
            : fallback?.FrameBorderSoftEdge;
        var reflection = effectList?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "reflection", StringComparison.OrdinalIgnoreCase));
        var fallbackReflection = fallback?.FrameBorderReflectionEnabled == false
            ? null
            : fallback?.FrameBorderReflection;
        if (shadow is null && fallbackShadow is null && glow is null && fallbackGlow is null
            && softEdge is null && fallbackSoftEdge is null
            && reflection is null && fallbackReflection is null)
            return null;

        var colorText = shadow?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        var color = TryParseZoomRgb(colorText, out var nativeColor)
            ? nativeColor
            : TryParseZoomRgb(fallbackShadow?.Color, out var fallbackColor)
                ? fallbackColor
                : new SrgbColor(0x40, 0x40, 0x40);
        var alpha100k = shadow?.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "alpha", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        var alpha = int.TryParse(alpha100k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawAlpha)
            ? rawAlpha
            : fallbackShadow?.Alpha ?? 50000;
        var blur = long.TryParse(shadow?.Attribute("blurRad")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawBlur)
            ? rawBlur / EmuPerDip
            : (fallbackShadow?.BlurRadiusEmu ?? 0) / EmuPerDip;
        var distance = long.TryParse(shadow?.Attribute("dist")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawDistance)
            ? rawDistance / EmuPerDip
            : (fallbackShadow?.DistanceEmu ?? 0) / EmuPerDip;
        var direction = int.TryParse(shadow?.Attribute("dir")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawDirection)
            ? rawDirection / 60000d
            : (fallbackShadow?.Direction ?? 0) / 60000d;
        var glowColorText = glow?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        var glowColor = TryParseZoomRgb(glowColorText, out var nativeGlowColor)
            ? nativeGlowColor
            : TryParseZoomRgb(fallbackGlow?.Color, out var fallbackGlowColor)
                ? fallbackGlowColor
                : new SrgbColor(0x40, 0x40, 0x40);
        var glowAlpha100k = glow?.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "alpha", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        var glowAlpha = int.TryParse(glowAlpha100k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawGlowAlpha)
            ? rawGlowAlpha
            : fallbackGlow?.Alpha ?? 50000;
        var glowRadius = long.TryParse(glow?.Attribute("rad")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawGlowRadius)
            ? rawGlowRadius / EmuPerDip
            : (fallbackGlow?.RadiusEmu ?? 0) / EmuPerDip;
        var softEdgeRadius = long.TryParse(softEdge?.Attribute("rad")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawSoftEdgeRadius)
            ? rawSoftEdgeRadius / EmuPerDip
            : (fallbackSoftEdge?.RadiusEmu ?? 0) / EmuPerDip;
        var reflectionAlpha = int.TryParse(reflection?.Attribute("stA")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionAlpha)
            ? rawReflectionAlpha
            : fallbackReflection?.Alpha ?? 50000;
        var reflectionBlur = long.TryParse(reflection?.Attribute("blurRad")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionBlur)
            ? rawReflectionBlur / EmuPerDip
            : (fallbackReflection?.BlurRadiusEmu ?? 0) / EmuPerDip;
        var reflectionDistance = long.TryParse(reflection?.Attribute("dist")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionDistance)
            ? rawReflectionDistance / EmuPerDip
            : (fallbackReflection?.DistanceEmu ?? 0) / EmuPerDip;
        var reflectionDirection = int.TryParse(reflection?.Attribute("dir")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionDirection)
            ? rawReflectionDirection / 60000d
            : (fallbackReflection?.Direction ?? 5400000) / 60000d;
        var reflectionScale = int.TryParse(reflection?.Attribute("sy")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionScale)
            ? rawReflectionScale / 100000d
            : (fallbackReflection?.ScaleY ?? -100000) / 100000d;
        var reflectionEnd = int.TryParse(reflection?.Attribute("endPos")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawReflectionEnd)
            ? rawReflectionEnd / 100000d
            : (fallbackReflection?.EndPosition ?? 100000) / 100000d;
        return new ResolvedShapeEffects
        {
            HasOuterShadow = shadow is not null || fallbackShadow is not null,
            OuterShadowColor = color,
            OuterShadowAlpha = (byte)Math.Clamp((int)Math.Round(alpha * 255d / 100000d), 0, 255),
            OuterShadowBlurDip = Math.Max(0, blur),
            OuterShadowDistDip = Math.Max(0, distance),
            OuterShadowDirDeg = direction,
            HasGlow = glow is not null || fallbackGlow is not null,
            GlowColor = glowColor,
            GlowAlpha = (byte)Math.Clamp((int)Math.Round(glowAlpha * 255d / 100000d), 0, 255),
            GlowRadiusDip = Math.Max(0, glowRadius),
            HasSoftEdge = softEdge is not null || fallbackSoftEdge is not null,
            SoftEdgeRadiusDip = Math.Max(0, softEdgeRadius),
            HasReflection = reflection is not null || fallbackReflection is not null,
            ReflectionAlpha = (byte)Math.Clamp((int)Math.Round(reflectionAlpha * 255d / 100000d), 0, 255),
            ReflectionBlurDip = Math.Max(0, reflectionBlur),
            ReflectionDistDip = Math.Max(0, reflectionDistance),
            ReflectionDirDeg = reflectionDirection,
            ReflectionScaleY = Math.Abs(reflectionScale) < 0.001 ? -1 : reflectionScale,
            ReflectionEndPos = Math.Clamp(reflectionEnd, 0, 1),
        };
    }

    private static ResolvedOutline ResolveZoomFrameOutline(
        XElement? properties,
        ZoomObjectProperties? fallback,
        PresentationTheme? theme = null,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var shapeProperties = properties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        var line = shapeProperties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var nativeNoFill = line?.Elements().Any(element =>
            string.Equals(element.Name.LocalName, "noFill", StringComparison.OrdinalIgnoreCase)) == true;
        if (nativeNoFill || (line is null && fallback?.FrameBorderNoFill == true))
            return ResolvedOutline.None.Instance;
        var solidFill = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "solidFill", StringComparison.OrdinalIgnoreCase));
        var widthEmu = line?.Attribute("w") is { Value: var widthText }
            && int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWidth)
            && parsedWidth > 0
                ? parsedWidth
                : fallback?.FrameBorderWidthEmu;
        var widthPoints = widthEmu is int width
            ? Math.Clamp(width / 12700d, 0.01, 1584)
            : 0.75;
        var dash = line?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "prstDash", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        var resolvedDash = TryParseZoomDash(dash) ?? fallback?.FrameBorderDash ?? OutlineDash.Solid;

        var gradientFill = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "gradFill", StringComparison.OrdinalIgnoreCase));
        if (TryResolveZoomGradient(gradientFill, out var resolvedGradient))
            return new ResolvedOutline.Gradient(
                resolvedGradient!, PointsToDip(widthPoints), resolvedDash);
        if (gradientFill is null
            && TryResolveZoomGradient(fallback?.FrameBorderGradient, out resolvedGradient))
            return new ResolvedOutline.Gradient(
                resolvedGradient!, PointsToDip(widthPoints), resolvedDash);

        var patternFill = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "pattFill", StringComparison.OrdinalIgnoreCase));
        if (TryResolveZoomPattern(patternFill, theme, effectiveClrMap, out var resolvedPattern))
            return new ResolvedOutline.Pattern(
                resolvedPattern!, PointsToDip(widthPoints), resolvedDash);
        if (patternFill is null
            && TryResolveZoomPattern(fallback?.FrameBorderPattern, out resolvedPattern))
            return new ResolvedOutline.Pattern(
                resolvedPattern!, PointsToDip(widthPoints), resolvedDash);

        var resolvedColor = ResolveZoomFrameColor(solidFill, theme, effectiveClrMap);
        if (resolvedColor is null
            && fallback?.FrameBorderThemeColor is { } themeSlot
            && theme is not null)
        {
            resolvedColor = ThemeColorResolver.Resolve(
                new ThemeAwareColor(
                    theme.ColorScheme[themeSlot],
                    new SchemeColorRef
                    {
                        Slot = themeSlot,
                        RoleName = ThemeColorSlotMapper.ToSchemeColorString(themeSlot),
                    }),
                theme,
                effectiveClrMap);
        }
        if (resolvedColor is null
            && TryParseZoomRgb(fallback?.FrameBorderColor, out var fallbackColor))
            resolvedColor = fallbackColor;
        if (resolvedColor is null)
            return ResolvedOutline.None.Instance;

        return new ResolvedOutline.Visible(
            resolvedColor.Value,
            PointsToDip(widthPoints),
            resolvedDash,
            255);
    }

    private static bool TryResolveZoomGradient(
        XElement? gradient,
        out ResolvedFill.Gradient? resolved)
    {
        resolved = null;
        var stops = gradient?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "gsLst", StringComparison.OrdinalIgnoreCase))
            ?.Elements().Where(element =>
                string.Equals(element.Name.LocalName, "gs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (stops is not { Length: >= 2 })
            return false;

        var colors = stops.Select(stop => stop.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value).ToArray();
        if (colors.Any(color => !TryParseZoomRgb(color, out _)))
            return false;

        var angleText = gradient?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "lin", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ang")?.Value;
        var angle = int.TryParse(angleText, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var angleValue)
            ? angleValue / 60000d
            : 0d;
        var resolvedStops = colors.Select((color, index) =>
        {
            TryParseZoomRgb(color!, out var resolvedColor);
            return new ResolvedFill.ResolvedGradientStop(
                index / (double)(colors.Length - 1), resolvedColor);
        }).ToArray();
        resolved = new ResolvedFill.Gradient(resolvedStops, GradientKind.Linear, angle);
        return true;
    }

    private static bool TryResolveZoomGradient(
        ZoomFrameBorderGradient? gradient,
        out ResolvedFill.Gradient? resolved)
    {
        resolved = null;
        if (gradient is null
            || !TryParseZoomRgb(gradient.StartColor, out var start)
            || !TryParseZoomRgb(gradient.EndColor, out var end))
            return false;

        resolved = new ResolvedFill.Gradient(start, end, gradient.Angle / 60000d);
        return true;
    }

    private static bool TryResolveZoomPattern(
        XElement? pattern,
        PresentationTheme? theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        out ResolvedFill.PatternFill? resolved)
    {
        resolved = null;
        var preset = ZoomFrameBorderPatternCatalog.Normalize(pattern?.Attribute("prst")?.Value);
        var foreground = ResolveZoomFrameColor(
            pattern?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "fgClr", StringComparison.OrdinalIgnoreCase)),
            theme,
            effectiveClrMap);
        var background = ResolveZoomFrameColor(
            pattern?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "bgClr", StringComparison.OrdinalIgnoreCase)),
            theme,
            effectiveClrMap);
        if (preset is null || foreground is null || background is null)
            return false;

        resolved = new ResolvedFill.PatternFill(preset, foreground.Value, background.Value);
        return true;
    }

    private static bool TryResolveZoomPattern(
        ZoomFrameBorderPattern? pattern,
        out ResolvedFill.PatternFill? resolved)
    {
        resolved = null;
        if (pattern is null
            || ZoomFrameBorderPatternCatalog.Normalize(pattern.Preset) is not { } preset
            || !TryParseZoomRgb(pattern.ForegroundColor, out var foreground)
            || !TryParseZoomRgb(pattern.BackgroundColor, out var background))
            return false;

        resolved = new ResolvedFill.PatternFill(preset, foreground, background);
        return true;
    }

    private static SrgbColor? ResolveZoomFrameColor(
        XElement? solidFill,
        PresentationTheme? theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (solidFill is null)
            return null;

        var rgb = solidFill.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        if (TryParseZoomRgb(rgb, out var resolvedRgb))
            return resolvedRgb;

        var scheme = solidFill.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "schemeClr", StringComparison.OrdinalIgnoreCase));
        var role = scheme?.Attribute("val")?.Value?.Trim();
        if (theme is null || string.IsNullOrWhiteSpace(role))
            return null;

        var slot = ThemeColorResolver.MapRoleToSlot(role, effectiveClrMap);
        var schemeRef = new SchemeColorRef
        {
            RoleName = role,
            Slot = slot,
            LumMod = ReadZoomColorTransform(scheme, "lumMod", 1.0),
            LumOff = ReadZoomColorTransform(scheme, "lumOff", 0.0),
            Tint = ReadZoomColorTransform(scheme, "tint", 1.0),
            Shade = ReadZoomColorTransform(scheme, "shade", 1.0),
        };
        var color = new ThemeAwareColor(theme.ColorScheme[slot], schemeRef);
        return ThemeColorResolver.Resolve(color, theme, effectiveClrMap);
    }

    private static bool TryParseZoomRgb(string? value, out SrgbColor color)
    {
        color = default;
        if (!RgbColorTextCodec.TryParse(
                value,
                RgbColorTextProfile.DrawingMl,
                out var rgb))
            return false;

        color = new SrgbColor(rgb.R, rgb.G, rgb.B);
        return true;
    }

    private static double ReadZoomColorTransform(
        XElement? scheme,
        string name,
        double fallback)
    {
        var value = scheme?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
            ? Math.Clamp(raw / 100000d, 0, 1)
            : fallback;
    }

    private static string? ResolveZoomFrameGeometry(
        XElement? properties,
        ZoomObjectProperties? fallback)
    {
        var geometry = properties?.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "prstGeom",
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("prst")?.Value;
        return string.IsNullOrWhiteSpace(geometry)
            ? fallback?.FrameGeometry
            : geometry.Trim();
    }

    private static OutlineDash? TryParseZoomDash(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                "solid" => OutlineDash.Solid,
                "dash" => OutlineDash.Dash,
                "dot" => OutlineDash.Dot,
                "dashdot" => OutlineDash.DashDot,
                "lgdash" => OutlineDash.LongDash,
                "lgdashdot" => OutlineDash.LongDashDot,
                "lgdashdotdot" => OutlineDash.LongDashDotDot,
                "sysdash" => OutlineDash.SystemDash,
                "sysdot" => OutlineDash.SystemDot,
                "sysdashdot" => OutlineDash.SystemDashDot,
                _ => null,
            };

    private static void AddSummaryZoomPlaceholder(
        uint shapeId,
        LayoutRect boundsDip,
        double rotationDeg,
        bool flipH,
        bool flipV,
        ResolvedOutline outline,
        List<DrawOp> ops)
    {
        ops.Add(new DrawOp.Shape
        {
            ShapeId = shapeId,
            Geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, boundsDip),
            Fill = new ResolvedFill.Solid(new SrgbColor(0xCC, 0xCC, 0xCC)),
            Outline = outline,
            BoundsDip = boundsDip,
            RotationDeg = rotationDeg,
            FlipH = flipH,
            FlipV = flipV,
        });
    }

    // ─── Preserved modern objects (Wave 25A: zoom / ink / 3D / unknown) ─────────────────────

    /// <summary>
    /// Renders a preserved modern object (slide zoom, 3D model, or unknown
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

        if (shape.Kind == SlideShapeKind.Zoom
            && shape.PreservedObject?.SummaryZoomTargets.Count > 0
            && TryComposeSummaryZoomPreviews(
                shape, boundsDip, anchor.RotationDeg, anchor.FlipH, anchor.FlipV,
                ops, theme, effectiveClrMap))
        {
            return;
        }

        if (shape.Kind == SlideShapeKind.Zoom
            && shape.PreservedObject?.SummaryZoomTargets.Count == 0
            && TryComposeSingleZoomPreview(
                shape, boundsDip, anchor.RotationDeg, anchor.FlipH, anchor.FlipV,
                ops, theme, effectiveClrMap))
        {
            return;
        }

        if (shape.Picture is { Bytes.Length: > 0 } pic)
        {
            ops.Add(new DrawOp.Picture
            {
                Bytes       = pic.Bytes,
                ContentType = pic.ContentType,
                DestDip     = boundsDip,
                RotationDeg = anchor.RotationDeg,
                FlipH       = anchor.FlipH,
                FlipV       = anchor.FlipV,
                Outline     = shape.Kind == SlideShapeKind.Zoom
                    ? ResolveZoomFrameOutline(
                        null, shape.PreservedObject?.ZoomProperties, theme, effectiveClrMap)
                    : ResolvedOutline.None.Instance,
                PictureFrameGeometry = shape.Kind == SlideShapeKind.Zoom
                    ? shape.PreservedObject?.ZoomProperties?.FrameGeometry
                    : null,
                IsCover = shape.Kind == SlideShapeKind.Zoom
                    && string.Equals(
                        shape.PreservedObject?.ZoomProperties?.ImageType,
                        "cover",
                        StringComparison.OrdinalIgnoreCase),
            });
        }
        else
        {
            // No preview — grey rectangle placeholder (slightly lighter than OLE's 0xC0).
            ops.Add(new DrawOp.Shape
            {
                Geometry    = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, boundsDip),
                Fill        = new ResolvedFill.Solid(new SrgbColor(0xCC, 0xCC, 0xCC)),
                Outline     = shape.Kind == SlideShapeKind.Zoom
                    ? ResolveZoomFrameOutline(
                        null, shape.PreservedObject?.ZoomProperties, theme, effectiveClrMap)
                    : ResolvedOutline.None.Instance,
                BoundsDip   = boundsDip,
                RotationDeg = anchor.RotationDeg,
            });
        }
    }

    private static void ComposeInk(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        var inkStrokes = SlideShowInkRenderPlanner.Build(shape, presentation);
        var slideBounds = new LayoutRect(
            0,
            0,
            presentation.SlideSizeCxEmu / EmuPerDip,
            presentation.SlideSizeCyEmu / EmuPerDip);

        if (inkStrokes.Count > 0)
        {
            foreach (var stroke in inkStrokes)
            {
                if (stroke.Points.Count == 0)
                    continue;

                var path = new CustomGeometryPath
                {
                    PathW = Math.Max(1, (long)Math.Round(slideBounds.Width)),
                    PathH = Math.Max(1, (long)Math.Round(slideBounds.Height)),
                    Fill = false,
                    Stroke = true,
                };
                path.Segments.Add(new CustomSegment(
                    CustomSegmentKind.MoveTo,
                    stroke.Points[0].X,
                    stroke.Points[0].Y));
                for (var pointIndex = 1; pointIndex < stroke.Points.Count; pointIndex++)
                {
                    var point = stroke.Points[pointIndex];
                    path.Segments.Add(new CustomSegment(
                        CustomSegmentKind.LineTo,
                        point.X,
                        point.Y));
                }

                ops.Add(new DrawOp.Shape
                {
                    ShapeId = shape.Id,
                    Geometry = CustomGeometryBuilder.BuildCustom([path], slideBounds),
                    Fill = ResolvedFill.None.Instance,
                    Outline = new ResolvedOutline.Visible(
                        stroke.Color,
                        stroke.ThicknessDip,
                        OutlineDash.Solid,
                        stroke.Alpha),
                    BoundsDip = slideBounds,
                });
            }

            return;
        }

        // Preserve the existing fallback behavior for malformed or unsupported InkML.
        ComposePreservedObject(shape, slide, presentation, theme, ops, effectiveClrMap);
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
                var borderDiagonalDown = ResolveOneBorder(cell.Borders?.DiagonalDown);
                var borderDiagonalUp = ResolveOneBorder(cell.Borders?.DiagonalUp);

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
                        resolvedTextColor, theme, effectiveClrMap,
                        cell.Anchor ?? TableCellAnchor.Top);
                }

                cellOps.Add(new TableCellOp
                {
                    BoundsDip    = cellRect,
                    Fill         = resolvedFill,
                    BorderLeft   = borderLeft,
                    BorderRight  = borderRight,
                    BorderTop    = borderTop,
                    BorderBottom = borderBottom,
                    BorderDiagonalDown = borderDiagonalDown,
                    BorderDiagonalUp = borderDiagonalUp,
                    Text         = textLayout,
                    Anchor       = cell.Anchor ?? TableCellAnchor.Top
                });
            }
        }

        ops.Add(new DrawOp.Table
        {
            ShapeId   = shape.Id,
            BoundsDip = frameBounds,
            RotationDeg = shape.RotationDeg,
            FlipH     = shape.FlipH,
            FlipV     = shape.FlipV,
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

        if (chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie && chart.Series.Count > 0)
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
            RotationDeg  = shape.RotationDeg,
            ChartShape   = chart,
            SeriesColors = seriesColors,
            FillPlans    = fillPlans,
            ChartAreaFill = ResolveChartSurfaceFill(chart.ChartAreaFill, theme, effectiveClrMap),
            ChartAreaOutline = ResolveChartSurfaceOutline(chart.ChartAreaOutline, theme, effectiveClrMap),
            PlotAreaFill = ResolveChartSurfaceFill(chart.PlotAreaFill, theme, effectiveClrMap),
            PlotAreaOutline = ResolveChartSurfaceOutline(chart.PlotAreaOutline, theme, effectiveClrMap)
        });
    }

    private static ChartFillPlan? ResolveChartSurfaceFill(
        ShapeFill? fill,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (fill is null) return null;
        var resolved = ResolveFill(fill, theme, effectiveClrMap);
        return resolved switch
        {
            ResolvedFill.Solid solid => new ChartFillPlan(solid.Color, solid.Alpha) { Fill = solid },
            ResolvedFill.Gradient gradient => new ChartFillPlan(gradient.StartColor, 255) { Fill = gradient },
            ResolvedFill.PatternFill pattern => new ChartFillPlan(pattern.ForegroundColor, 255) { Fill = pattern },
            _ => null,
        };
    }

    private static ChartStrokePlan? ResolveChartSurfaceOutline(
        ShapeOutline? outline,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (outline is null) return null;
        var resolved = ResolveOutline(outline, theme, effectiveClrMap);
        return resolved switch
        {
            ResolvedOutline.Visible visible => new ChartStrokePlan(visible.Color, visible.Alpha, visible.WidthDip, visible.Dash),
            ResolvedOutline.Gradient gradient => new ChartStrokePlan(gradient.Fill.StartColor, 255, gradient.WidthDip, gradient.Dash) { Fill = gradient.Fill },
            _ => null,
        };
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
        bool pieLike = chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie;

        if (pieLike && chart.Series.Count > 0)
        {
            var firstSeries = chart.Series[0];
            bool varyPointColors = chart.VaryColors;
            for (int pointIndex = 0; pointIndex < seriesColors.Count; pointIndex++)
            {
                var pointFill = GetPointFill(firstSeries, pointIndex);
                var pointColor = GetPointFillColor(firstSeries, pointIndex);
                var fill = ResolveChartFillPlan(
                    pointFill ?? (varyPointColors ? null : firstSeries.Fill),
                    pointColor ?? (varyPointColors ? null : firstSeries.FillColor),
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
            // Cached dsp:drawing coordinates are local to the SmartArt graphic frame.
            foreach (var fallback in smart.FallbackShapes)
            {
                var translated = SlideCloner.CloneShape(fallback);
                TranslateCachedSmartArtShape(translated, shape.OffsetXEmu, shape.OffsetYEmu);
                ApplyCachedSmartArtStyle(translated, smart, theme);
                ComposeShape(translated, slide, presentation, theme, ops, effectiveClrMap: effectiveClrMap);
            }
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

    private static void TranslateCachedSmartArtShape(SlideShape shape, long offsetXEmu, long offsetYEmu)
    {
        shape.OffsetXEmu += offsetXEmu;
        shape.OffsetYEmu += offsetYEmu;

        foreach (var child in shape.Children)
            TranslateCachedSmartArtShape(child, offsetXEmu, offsetYEmu);
    }

    private static void ApplyCachedSmartArtStyle(
        SlideShape shape,
        SmartArtShape smart,
        PresentationTheme theme)
    {
        var data = smart.Data;

        // PowerPoint's simple1/accent1_2 hierarchy cache uses a distinct
        // connector color from the generic accent1 shade. Keep this correction
        // on the cached-drawing path; the bounded hierarchy3 and grouped-list
        // imports normally take the shared live plan before reaching here.
        if (IsSimpleAccentHierarchy(smart)
            && shape.AutoShapeKind is DrawingShapeKind.Line or DrawingShapeKind.Rectangle
            && shape.Outline is ShapeOutline.Visible line)
        {
            var connectorColor = new ThemeAwareColor(SrgbColor.FromRgb(0x0E4B66));
            shape.Outline = new ShapeOutline.Visible(
                connectorColor,
                line.WidthPt,
                line.Dash,
                line.BeginLineEnd,
                line.EndLineEnd);
        }

        // IncreasingCircleProcess cached background ellipses use the accent1 tint from the XML cache,
        // while PowerPoint renders the bgShp role as the neutral Office gray.
        // Keep this correction at the SmartArt cache boundary so normal DrawingML
        // tint resolution remains unchanged for ordinary shapes.
        if (data?.LayoutUniqueId.EndsWith("IncreasingCircleProcess", StringComparison.OrdinalIgnoreCase) == true
            && shape.AutoShapeKind == DrawingShapeKind.Ellipse
            && shape.TextBody is null
            && shape.Fill is ShapeFill.Solid solid
            && solid.Color.SchemeColor?.Slot == ThemeColorSlot.Accent1
            && solid.Color.SchemeColor.Tint < 0.8)
        {
            shape.Fill = new ShapeFill.Solid(new ThemeAwareColor(ResolveSmartArtNeutralBackground(theme)));
        }
        else if (data?.LayoutUniqueId.EndsWith("cycle2", StringComparison.OrdinalIgnoreCase) == true
            && shape.AutoShapeKind == DrawingShapeKind.RightArrow
            && shape.TextBody is not null
            && shape.TextBody.Paragraphs.All(paragraph => paragraph.Runs.All(run => string.IsNullOrEmpty(run.Text)))
            && shape.Fill is ShapeFill.Solid cycleArrowFill
            && cycleArrowFill.Color.SchemeColor?.Slot == ThemeColorSlot.Accent1)
        {
            shape.Fill = new ShapeFill.Solid(new ThemeAwareColor(SmartArtStylePlanner.ResolveNeutralConnector(theme)));
        }

        foreach (var child in shape.Children)
            ApplyCachedSmartArtStyle(child, smart, theme);
    }

    private static bool IsSimpleAccentHierarchy(SmartArtShape smart) =>
        smart.Data?.LayoutUniqueId.EndsWith("/hierarchy3", StringComparison.OrdinalIgnoreCase) == true
        && smart.QuickStyle?.UniqueId.EndsWith("/quickstyle/simple1", StringComparison.OrdinalIgnoreCase) == true
        && smart.Colors?.UniqueId.EndsWith("/colors/accent1_2", StringComparison.OrdinalIgnoreCase) == true;

    private static SrgbColor ResolveSmartArtNeutralBackground(PresentationTheme theme)
    {
        // Office's default accent1_2 SmartArt background role rasterizes to this
        // cool neutral. Preserve its role-independent appearance while allowing
        // the non-default theme to keep a neutral light background.
        var lt2 = theme.ColorScheme[ThemeColorSlot.Lt2];
        if (lt2 == SrgbColor.FromRgb(0xE8E8E8))
            return SrgbColor.FromRgb(0xCCD2D8);

        return ThemeColorTransform.ApplyShade(lt2, 0.88);
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
        IReadOnlyDictionary<string, string>? effectiveClrMap = null,
        TableCellAnchor anchor = TableCellAnchor.Top)
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
                    BaselineOffset = run.BaselineOffset,
                    Bold         = run.Bold,
                    Italic       = run.Italic,
                    Underline    = run.Underline,
                    Strikethrough = run.Strikethrough,
                    RightToLeft  = run.RightToLeft,
                    Color        = color
                });
            }

            resolvedParas.Add(new ResolvedParagraph
            {
                Runs         = resolvedRuns,
                Align        = para.Align ?? TextAlign.Left,
                RightToLeft  = para.RightToLeft
                    ?? body.LstStyle?.Resolve(para.Level)?.RightToLeft
                    ?? body.DefaultParaRightToLeft
                    ?? false,
                Level        = para.Level,
                BulletKind   = para.BulletKind,
                BulletChar   = para.BulletChar,
                BulletImage  = para.BulletImage,
                SpaceBeforePt = para.SpaceBeforePt ?? 0,
                SpaceAfterPt  = para.SpaceAfterPt ?? 0,
                LineSpacingPercent = para.LineSpacingPercent,
                LineSpacingPointsExact = para.LineSpacingPointsExact
            });
        }

        return new ResolvedTextLayout
        {
            Paragraphs    = resolvedParas,
            Anchor        = anchor switch
            {
                TableCellAnchor.Middle => VerticalAnchor.Middle,
                TableCellAnchor.Bottom => VerticalAnchor.Bottom,
                _ => VerticalAnchor.Top,
            },
            VerticalType  = body.VerticalType,
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
        ShapeFill.Solid s => new ResolvedFill.Solid(
            ThemeColorResolver.Resolve(s.Color, theme, effectiveClrMap),
            s.Color.Alpha),
        ShapeFill.Gradient g => new ResolvedFill.Gradient(
            g.Stops.Select(stop => new ResolvedFill.ResolvedGradientStop(
                stop.Position,
                ThemeColorResolver.Resolve(stop.Color, theme, effectiveClrMap),
                stop.Color.Alpha)).ToArray(),
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
            v.Dash,
            v.Color.Alpha),
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
                ThemeColorResolver.Resolve(s.Color, theme, effectiveClrMap),
                s.Color.Alpha)).ToArray();
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
        if (HeaderFooterDateTimeFormatter.IsDateTimeField(t))
            return HeaderFooterDateTimeFormatter.Format(t, DateTime.Now);

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
    /// level by walking PowerPoint's inheritance chain: the shape's OWN txBody-level
    /// a:lstStyle → layout placeholder's lstStyle → master txStyles (by category) → null.
    /// The caller applies the hard-coded fallback when this returns null.
    /// </summary>
    private static TextStyleLevel? ResolveTextStyleInheritance(
        int paraLevel,
        TextStyleCategory category,
        TextStyleLevels? shapeLstStyle,
        TextBody? layoutBody,
        MasterTextStyles? masterTextStyles)
    {
        // 1. The shape's own a:lstStyle (sits between direct paragraph properties and the
        //    layout in PowerPoint's inheritance chain — must win over the layout/master).
        if (shapeLstStyle is not null)
        {
            var lvl = shapeLstStyle[paraLevel];
            if (lvl is not null) return lvl;
            // Walk upward toward level 0 only if the shape's lstStyle has any entry defined.
            for (int l = paraLevel - 1; l >= 0; l--)
            {
                lvl = shapeLstStyle[l];
                if (lvl is not null) return lvl;
            }
        }

        // 2. Layout placeholder's a:lstStyle for this paragraph level.
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

        // 3. Master p:txStyles category at this paragraph level.
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
        PresentationModel presentation,
        VerticalAnchor effectiveAnchor,
        TextAlign? effectiveDefaultAlign,
        bool? effectiveDefaultRightToLeft,
        Placeholder? placeholder,
        PresentationTheme theme,
        int slideIndex = 0,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null,
        TextBody? layoutBody = null,
        TextBody? masterBody = null,
        MasterTextStyles? masterTextStyles = null,
        ResolvedRunShadow? inheritedTextShadow = null,
        double textAreaWidthDip = 1)
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

        var insets = TextFrameLayoutPlanner.FromOptionalInsets(
            PointsToDip(body.InsetLeftPt ?? layoutBody?.InsetLeftPt ?? masterBody?.InsetLeftPt),
            PointsToDip(body.InsetTopPt ?? layoutBody?.InsetTopPt ?? masterBody?.InsetTopPt),
            PointsToDip(body.InsetRightPt ?? layoutBody?.InsetRightPt ?? masterBody?.InsetRightPt),
            PointsToDip(body.InsetBottomPt ?? layoutBody?.InsetBottomPt ?? masterBody?.InsetBottomPt),
            DefaultInsetHorzDip,
            DefaultInsetVertDip);
        double resolvedTextAreaWidthDip = Math.Max(1, textAreaWidthDip);

        var autoNumState = new PresentationListMarkerContinuationState();

        var resolvedParas = new List<ResolvedParagraph>(body.Paragraphs.Count);

        foreach (var para in body.Paragraphs)
        {
            var resolvedRuns = new List<ResolvedRun>(para.Runs.Count);

            // MM3: Resolve the inherited text-style level for this paragraph's indent level.
            // This is done once per paragraph since all runs in a paragraph share the same level.
            var inheritedStyle = ResolveTextStyleInheritance(
                para.Level, category, body.LstStyle, layoutBody, masterTextStyles);

            // Resolve inherited color from the style chain (if any).
            SrgbColor? inheritedColor = null;
            if (inheritedStyle?.Color is { } styleColor)
                inheritedColor = ThemeColorResolver.Resolve(styleColor, theme, effectiveClrMap);

            // Resolve inherited font from the style chain, expanding +mj-lt / +mn-lt tokens.
            string? inheritedFont = inheritedStyle?.LatinFont is { Length: > 0 } lf
                ? ResolveLatinFont(lf, theme)
                : null;
            long marLEmu = para.MarginLeftEmu ?? inheritedStyle?.MarginLeftEmu ?? 0;
            double mathParagraphWidthDip = Math.Max(
                1,
                resolvedTextAreaWidthDip - Math.Max(0, marLEmu) / EmuPerDip);

            foreach (var run in para.Runs)
            {
                // Resolve field text for a:fld runs (slide number, date, etc.)
                string resolvedText = run.Field is not null
                    ? ResolveFieldText(run.Field, slideIndex)
                    : run.Text;
                if (run.Caps == RunTextCaps.All)
                    resolvedText = resolvedText.ToUpperInvariant();

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
                else if (inheritedTextShadow is not null)
                {
                    resolvedTextShadow = inheritedTextShadow;
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
                        EndPos = reflection.EndPos,
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
                    var containingProperties = presentation.DocumentMathProperties?.Overlay(run.Math.ContainingProperties)
                        ?? run.Math.ContainingProperties;
                    var mathNode = FreeP.App.Compositor.MathLayout.OmmlParser.ParsePowerPoint(
                        run.Math.RawXml,
                        resolvedText,
                        ToParserMathProperties(containingProperties));
                    mathLayout = FreeP.App.Compositor.MathLayout.MathLayoutEngine.Layout(
                        mathNode,
                        fontFamily,
                        fontSizePt,
                        paragraphWidthDip: mathParagraphWidthDip);
                }

                resolvedRuns.Add(new ResolvedRun
                {
                    Text          = resolvedText,
                    FontFamily    = fontFamily,
                    FontSizePt    = fontSizePt,
                    BaselineOffset = run.BaselineOffset,
                    Bold          = bold,
                    Italic        = italic,
                    Underline     = run.Underline || run.Field?.Underline == true,
                    Strikethrough = run.Strikethrough || run.Field?.Strikethrough == true,
                    RightToLeft   = run.RightToLeft,
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
                        Alignment   = t.Alignment,
                        Leader      = t.Leader
                    }).ToList()
                : Array.Empty<ResolvedTabStop>();

            var marker = PresentationListMarkerPlanner.Resolve(
                para,
                inheritedStyle,
                autoNumState);

            // Build bullet text and per-paragraph indent info.
            var bulletSeedRun = SelectBulletSeedRun(resolvedRuns);
            SrgbColor bulletColor = bulletSeedRun?.Color ?? SrgbColor.Black;
            string bulletFontFamily = bulletSeedRun?.FontFamily ?? fallbackFont;
            double bulletFontSizePt = bulletSeedRun?.FontSizePt ?? (fallbackFontSizePt * fontScale);
            double indentDip = 0.0;
            double hangingDip = 0.0;

            // Resolve marL/indent for indentation.
            long indentEmu = para.IndentEmu ?? inheritedStyle?.IndentEmu ?? 0;
            if (marLEmu > 0)
                indentDip = marLEmu / EmuPerDip;
            if (indentEmu < 0)
                hangingDip = -indentEmu / EmuPerDip; // hanging: bullet at indentDip-hangingDip

            // Override bullet color when explicitly set.
            if (marker.Color is not null)
                bulletColor = ThemeColorResolver.Resolve(marker.Color, theme, effectiveClrMap);

            // Override bullet font when set.
            if (!string.IsNullOrEmpty(marker.FontFamily))
                bulletFontFamily = ResolveLatinFont(marker.FontFamily, theme);

            // Resolve bullet size from buSzPts or buSzPct after run/theme fallback.
            bulletFontSizePt = marker.ResolveFontSizePt(bulletFontSizePt, fontScale)
                ?? bulletFontSizePt;

            resolvedParas.Add(new ResolvedParagraph
            {
                Runs = resolvedRuns,
                // P0: use the inherited default alignment when the paragraph has no explicit align.
                Align = para.Align ?? fallbackAlign,
                RightToLeft = para.RightToLeft
                    ?? body.LstStyle?.Resolve(para.Level)?.RightToLeft
                    ?? effectiveDefaultRightToLeft
                    ?? inheritedStyle?.RightToLeft
                    ?? false,
                Level = para.Level,
                BulletKind = marker.Kind,
                BulletChar = marker.Character,
                BulletImage = marker.Image,
                SpaceBeforePt = para.SpaceBeforePt ?? 0,
                SpaceAfterPt = para.SpaceAfterPt ?? 0,
                LineSpacingPercent = para.LineSpacingPercent,
                LineSpacingPointsExact = para.LineSpacingPointsExact,
                TabStops = resolvedTabStops,  // Wave 18B
                // Wave 19A:
                BulletText       = marker.Text,
                BulletColor      = bulletColor,
                BulletFontFamily = bulletFontFamily,
                BulletFontSizePt = bulletFontSizePt,
                IndentDip        = indentDip,
                HangingDip       = hangingDip,
            });
        }

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
            Text3dEffects = ResolveEffects(body.Text3dEffects),
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

    private static FreeP.App.Compositor.MathLayout.MathNode.MathProperties? ToParserMathProperties(
        OmmlMathProperties? properties)
    {
        if (properties is null || !properties.HasValues)
            return null;

        var binaryBreak = properties.BinaryBreak switch
        {
            "after" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinaryBreak.After,
            "repeat" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinaryBreak.Repeat,
            "before" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinaryBreak.Before,
            _ when !string.IsNullOrWhiteSpace(properties.BinaryBreak)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinaryBreak.Before,
            _ => (FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinaryBreak?)null,
        };
        var binarySubtraction = properties.BinarySubtraction switch
        {
            "+-" or "plusMinus" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinarySubtraction.PlusMinus,
            "-+" or "minusPlus" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinarySubtraction.MinusPlus,
            "--" or "minusMinus" => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinarySubtraction.MinusMinus,
            _ when !string.IsNullOrWhiteSpace(properties.BinarySubtraction)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinarySubtraction.MinusMinus,
            _ => (FreeP.App.Compositor.MathLayout.MathNode.MathParagraphBinarySubtraction?)null,
        };
        var defaultJustification = properties.DefaultJustification switch
        {
            var value when string.Equals(value, "left", StringComparison.OrdinalIgnoreCase)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification.Left,
            var value when string.Equals(value, "right", StringComparison.OrdinalIgnoreCase)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification.Right,
            var value when string.Equals(value, "center", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "centre", StringComparison.OrdinalIgnoreCase)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification.Center,
            var value when string.Equals(value, "centerGroup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "center-group", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "centreGroup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "centre-group", StringComparison.OrdinalIgnoreCase)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification.CenterGroup,
            var value when !string.IsNullOrWhiteSpace(value)
                => FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification.CenterGroup,
            _ => (FreeP.App.Compositor.MathLayout.MathNode.MathParagraphJustification?)null,
        };

        static FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation ParseLimitLocation(
            string? value,
            FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation fallback) =>
            value?.Trim() switch
            {
                var location when string.Equals(location, "undOvr", StringComparison.OrdinalIgnoreCase)
                    => FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation.UndOvr,
                var location when string.Equals(location, "subSup", StringComparison.OrdinalIgnoreCase)
                    => FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation.SubSup,
                _ => fallback,
            };

        var integralLimitLocation = string.IsNullOrWhiteSpace(properties.IntegralLimitLocation)
            ? (FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation?)null
            : ParseLimitLocation(
                properties.IntegralLimitLocation,
                FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation.SubSup);
        var naryLimitLocation = string.IsNullOrWhiteSpace(properties.NaryLimitLocation)
            ? (FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation?)null
            : ParseLimitLocation(
                properties.NaryLimitLocation,
                FreeP.App.Compositor.MathLayout.MathNode.MathLimitLocation.UndOvr);

        static int? ParseMargin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips)
                && twips >= 0
                ? twips
                : 0;
        }

        return new FreeP.App.Compositor.MathLayout.MathNode.MathProperties(
            binaryBreak,
            binarySubtraction,
            string.IsNullOrWhiteSpace(properties.MathFontFamily) ? null : properties.MathFontFamily,
            properties.SmallFraction,
            defaultJustification,
            integralLimitLocation,
            naryLimitLocation,
            properties.DisplayDefaults,
            ParseMargin(properties.LeftMargin),
            ParseMargin(properties.RightMargin),
            ParseMargin(properties.WrapIndent),
            properties.WrapRight);
    }

    // ─── Wave 19A: auto-number formatter ────────────────────────────────────────────────────

    /// <summary>
    /// Formats an auto-numbered bullet counter value according to the given type.
    /// <paramref name="n"/> is 1-based.
    /// </summary>
    public static string FormatAutoNum(AutoNumType type, int n) =>
        PresentationListMarkerPlanner.FormatAutoNumber(type, n);

    private static ResolvedRun? SelectBulletSeedRun(IReadOnlyList<ResolvedRun> runs) =>
        runs.FirstOrDefault(run => run.Text.Length > 0)
        ?? runs.FirstOrDefault();

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
