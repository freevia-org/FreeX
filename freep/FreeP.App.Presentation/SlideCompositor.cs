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
    // 1 inch = 914400 EMU; 1 inch = 96 DIP -> 1 DIP = 9525 EMU
    private const double EmuPerDip = 9525.0;

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

    // ─── Background ───────────────────────────────────────────────────────────────────────────

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

    // ─── Shape dispatch ───────────────────────────────────────────────────────────────────────

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
                ComposePicture(shape, slide, presentation, theme, ops);
                break;

            case SlideShapeKind.Group:
                // Flatten group children (simplified — no group-level transform for now).
                foreach (var child in shape.Children)
                    ComposeShape(child, slide, presentation, theme, ops);
                break;

            case SlideShapeKind.Table:
                if (shape.Table is not null)
                    ComposeTable(shape, theme, ops);
                break;

            case SlideShapeKind.Chart:
                if (shape.Chart is not null)
                    ComposeChart(shape, theme, ops);
                break;

            case SlideShapeKind.SmartArt:
                if (shape.SmartArt is not null)
                    ComposeSmartArt(shape, slide, presentation, theme, ops);
                break;

            default:
                ComposeAutoShape(shape, slide, presentation, theme, ops);
                break;
        }
    }

    // ─── AutoShape / textbox / connector ────────────────────────────────────────────────────

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
        var geometry = shape.CustomGeometry.Count > 0
            ? CustomGeometryBuilder.BuildCustom(shape.CustomGeometry, boundsDip)
            : ShapeGeometryBuilder.Build(shape.AutoShapeKind, boundsDip);

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

            text = ResolveTextLayout(shape.TextBody, effectiveAnchor, effectiveDefaultAlign, shape.Placeholder, theme);
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
            Text = text,
            Effects = ResolveEffects(shape.Effects)
        });
    }

    private static ResolvedShapeEffects? ResolveEffects(ShapeEffects? fx)
    {
        if (fx is null) return null;
        if (!fx.HasOuterShadow && !fx.HasGlow && !fx.HasSoftEdge) return null;

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
            SoftEdgeRadiusDip = fx.SoftEdgeRadEmu / EmuPerDip
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
        List<DrawOp> ops)
    {
        if (shape.Picture is null) return;

        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        var boundsDip = AnchorToBounds(anchor);

        // P3: resolve picture outline from shape's spPr a:ln.
        var outline = shape.Outline is not null
            ? ResolveOutline(shape.Outline, theme)
            : ResolvedOutline.None.Instance;

        ops.Add(new DrawOp.Picture
        {
            Bytes = shape.Picture.Bytes,
            ContentType = shape.Picture.ContentType,
            DestDip = boundsDip,
            RotationDeg = anchor.RotationDeg,
            Outline = outline
        });
    }

    // ─── Table ───────────────────────────────────────────────────────────────────────────────

    private static void ComposeTable(SlideShape shape, PresentationTheme theme, List<DrawOp> ops)
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
                    ? ResolveFill(effectiveFill, theme)
                    : ResolvedFill.None.Instance;

                // Effective border.
                var effectiveBorder = table.ComputeEffectiveBorderOutline(r, c, cell);

                ResolvedOutline ResolveOneBorder(ShapeOutline? explicit_border) =>
                    explicit_border is not null
                        ? ResolveOutline(explicit_border, theme)
                        : (effectiveBorder is not null
                            ? ResolveOutline(effectiveBorder, theme)
                            : ResolvedOutline.None.Instance);

                var borderLeft   = ResolveOneBorder(cell.Borders?.Left);
                var borderRight  = ResolveOneBorder(cell.Borders?.Right);
                var borderTop    = ResolveOneBorder(cell.Borders?.Top);
                var borderBottom = ResolveOneBorder(cell.Borders?.Bottom);

                // Effective text color (for cells that have no explicit run color).
                var effectiveTextColor = table.ComputeEffectiveTextColor(r, c);
                var resolvedTextColor = effectiveTextColor is not null
                    ? ThemeColorResolver.Resolve(effectiveTextColor, theme)
                    : (SrgbColor?)null;

                // Text layout.
                ResolvedTextLayout? textLayout = null;
                if (cell.TextBody is not null && cell.TextBody.Paragraphs.Count > 0)
                {
                    // Cell insets.
                    double insetL = cell.InsetLeftPt.HasValue
                        ? PointsToDip(cell.InsetLeftPt.Value)
                        : PointsToDip(DefaultCellInsetHorzPt);
                    double insetR = cell.InsetRightPt.HasValue
                        ? PointsToDip(cell.InsetRightPt.Value)
                        : PointsToDip(DefaultCellInsetHorzPt);
                    double insetT = cell.InsetTopPt.HasValue
                        ? PointsToDip(cell.InsetTopPt.Value)
                        : PointsToDip(DefaultCellInsetVertPt);
                    double insetB = cell.InsetBottomPt.HasValue
                        ? PointsToDip(cell.InsetBottomPt.Value)
                        : PointsToDip(DefaultCellInsetVertPt);

                    textLayout = ResolveTableCellTextLayout(
                        cell.TextBody, insetL, insetR, insetT, insetB,
                        resolvedTextColor, theme);
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
            BoundsDip = frameBounds,
            Cells     = cellOps
        });
    }

    // ─── Chart ──────────────────────────────────────────────────────────────────────────────────

    private static void ComposeChart(SlideShape shape, PresentationTheme theme, List<DrawOp> ops)
    {
        var chart = shape.Chart!;

        var frameBounds = new LayoutRect(
            shape.OffsetXEmu / EmuPerDip,
            shape.OffsetYEmu / EmuPerDip,
            shape.ExtentCxEmu / EmuPerDip,
            shape.ExtentCyEmu / EmuPerDip);

        SrgbColor[] seriesColors;

        if (chart.ChartType == ChartType.Pie && chart.Series.Count > 0)
        {
            // For pie charts emit one color per data POINT (cycling accent1-6) so the
            // renderer can pick the right slice fill without re-resolving the theme.
            var firstSeries = chart.Series[0];
            int ptCount = firstSeries.Values.Count;
            seriesColors = new SrgbColor[ptCount];
            for (int pi = 0; pi < ptCount; pi++)
            {
                if (firstSeries.PointColors.TryGetValue(pi, out var ptColor))
                    seriesColors[pi] = ThemeColorResolver.Resolve(ptColor, theme);
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
                    ? ThemeColorResolver.Resolve(fillColor, theme)
                    : DefaultAccentColor(i, theme);
            }
        }

        ops.Add(new DrawOp.Chart
        {
            BoundsDip    = frameBounds,
            ChartShape   = chart,
            SeriesColors = seriesColors
        });
    }

    // ─── SmartArt ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Composes a SmartArt shape as a flat group of its fallback (dsp:drawing) shapes.
    /// Each fallback shape is already positioned in slide-coordinate space so they are
    /// composed identically to ordinary AutoShapes — no new rendering primitives needed.
    /// If the dsp:drawing was missing (empty FallbackShapes), emits a placeholder grey rectangle.
    /// </summary>
    private static void ComposeSmartArt(
        SlideShape shape,
        Slide slide,
        PresentationModel presentation,
        PresentationTheme theme,
        List<DrawOp> ops)
    {
        var smart = shape.SmartArt!;

        if (smart.FallbackShapes.Count > 0)
        {
            // Render each fallback shape as an ordinary AutoShape.
            foreach (var fallback in smart.FallbackShapes)
                ComposeShape(fallback, slide, presentation, theme, ops);
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
        double insetL, double insetR, double insetT, double insetB,
        SrgbColor? styleTextColor,
        PresentationTheme theme)
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
                    ? ThemeColorResolver.Resolve(run.Color, theme)
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
            InsetLeftDip  = insetL,
            InsetRightDip = insetR,
            InsetTopDip   = insetT,
            InsetBottomDip = insetB,
            Wrap          = body.Wrap
        };
    }

    // ─── Fill resolution ─────────────────────────────────────────────────────────────────────

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

    private static ResolvedOutline ResolveOutline(ShapeOutline outline, PresentationTheme theme) => outline switch
    {
        ShapeOutline.None => ResolvedOutline.None.Instance,
        ShapeOutline.Visible v => new ResolvedOutline.Visible(
            ThemeColorResolver.Resolve(v.Color, theme),
            PointsToDip(v.WidthPt),
            v.Dash),
        _ => ResolvedOutline.None.Instance
    };

    // ─── Text layout resolution ──────────────────────────────────────────────────────────────

    private static ResolvedTextLayout ResolveTextLayout(
        TextBody body,
        VerticalAnchor effectiveAnchor,
        TextAlign? effectiveDefaultAlign,
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

        // The inherited default paragraph alignment (from lstStyle chain or placeholder type).
        // When not set anywhere, centered-title defaults to center, others to left.
        TextAlign fallbackAlign = effectiveDefaultAlign ?? placeholder?.Type switch
        {
            PlaceholderType.CenteredTitle => TextAlign.Center,
            PlaceholderType.SubTitle => TextAlign.Center,
            _ => TextAlign.Left
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
                // P0: use the inherited default alignment when the paragraph has no explicit align.
                Align = para.Align ?? fallbackAlign,
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
            // P0: use the resolved effective anchor (from shape -> layout -> master chain).
            Anchor = effectiveAnchor,
            InsetLeftDip = body.InsetLeftPt.HasValue ? PointsToDip(body.InsetLeftPt.Value) : DefaultInsetHorzDip,
            InsetRightDip = body.InsetRightPt.HasValue ? PointsToDip(body.InsetRightPt.Value) : DefaultInsetHorzDip,
            InsetTopDip = body.InsetTopPt.HasValue ? PointsToDip(body.InsetTopPt.Value) : DefaultInsetVertDip,
            InsetBottomDip = body.InsetBottomPt.HasValue ? PointsToDip(body.InsetBottomPt.Value) : DefaultInsetVertDip,
            Wrap = body.Wrap
        };
    }

    // ─── Unit helpers ────────────────────────────────────────────────────────────────────────

    private static LayoutRect AnchorToBounds(ResolvedAnchor anchor) =>
        new(anchor.OffsetXEmu / EmuPerDip,
            anchor.OffsetYEmu / EmuPerDip,
            anchor.ExtentCxEmu / EmuPerDip,
            anchor.ExtentCyEmu / EmuPerDip);

    /// <summary>Converts typographic points to DIP (96/72 = 4/3 scaling).</summary>
    private static double PointsToDip(double pt) => pt * (96.0 / 72.0);
}
