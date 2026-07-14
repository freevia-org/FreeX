using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Theme 17 — SmartArt live layout engine.
///
/// Given a <see cref="SmartArtData"/> (node tree + family) and the graphicFrame bounds (in EMU),
/// produces an ordered list of <see cref="SlideShape"/> objects — rounded-rect boxes with
/// node text plus connector lines/arrows — that can be composed by the existing
/// <see cref="SlideCompositor"/> shape pipeline without any renderer changes.
///
/// Supported families:
///   <see cref="SmartArtFamily.Process"/>   — horizontal row of boxes + arrow connectors
///   <see cref="SmartArtFamily.List"/>      — vertical stack of boxes
///   <see cref="SmartArtFamily.Cycle"/>     — N boxes on a circle with arrow connectors
///   <see cref="SmartArtFamily.Hierarchy"/> — tree (root top, children below, connector lines)
///   <see cref="SmartArtFamily.Matrix"/>    — up to four boxes in a quadrant grid
///
/// Returns null for <see cref="SmartArtFamily.Unknown"/> → compositor falls back to cached drawing.
///
/// Colors: node fills/outlines/text are assigned by <see cref="SmartArtStylePlanner"/>.
/// Connectors: simple line/straight-arrow shapes using <see cref="DrawingShapeKind.Line"/>.
/// </summary>
public static class SmartArtLayoutEngine
{
    // DrawingML EMU per 96-DPI DIP.
    private const long EmuPerDip = DrawingMlCoordinateUnits.EmuPerPixel;

    // Padding as a fraction of the available dimension
    private const double OuterPaddingFrac = 0.04;  // 4% outer margin
    private const double GapFrac          = 0.025; // 2.5% gap between boxes

    // Box corner radius in EMU (used with roundRect preset)
    private const double CornerRadiusEmu = 100000.0;

    // Default font sizes for node text
    private const double NodeFontSizePt     = 11.0;
    private const double NodeFontSizeLargePt = 12.0;

    // ── Public entry point ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the layout engine for the given data + frame.
    /// Returns null for unsupported families (caller should use cached drawing).
    /// </summary>
    /// <param name="data">Parsed SmartArt data model.</param>
    /// <param name="frameXEmu">Graphic frame X offset in EMU (slide coordinate).</param>
    /// <param name="frameYEmu">Graphic frame Y offset in EMU.</param>
    /// <param name="frameCxEmu">Graphic frame width in EMU.</param>
    /// <param name="frameCyEmu">Graphic frame height in EMU.</param>
    /// <param name="theme">Presentation theme (for accent colors).</param>
    /// <param name="effectiveClrMap">Optional color map override (may be null).</param>
    /// <returns>Ordered list of <see cref="SlideShape"/> objects, or null when unsupported.</returns>
    public static IReadOnlyList<SlideShape>? Layout(
        SmartArtData data,
        long frameXEmu, long frameYEmu, long frameCxEmu, long frameCyEmu,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null,
        SmartArtQuickStyleMetadata? quickStyle = null,
        SmartArtColorMetadata? colors = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(theme);

        if (data.Family == SmartArtFamily.Unknown) return null;
        if (!data.IsLiveLayoutSupported) return null;

        // Flatten all visible nodes in display order
        var nodes = FlattenNodes(data);
        if (nodes.Count == 0)
        {
            // BI2: Return null (not an empty list) so the compositor proceeds to the
            // cached-drawing fallback instead of emitting nothing and rendering blank.
            return null;
        }

        var stylePlan = SmartArtStylePlanner.Build(data.Family, quickStyle, colors, theme, effectiveClrMap);

        if (IsPictureCaptionListLayout(data.LayoutUniqueId))
            return LayoutPictureCaptionList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsAlternatingProcessLayout(data.LayoutUniqueId))
            return LayoutAlternatingProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicPyramidLayout(data.LayoutUniqueId))
            return LayoutBasicPyramid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        return data.Family switch
        {
            SmartArtFamily.Process   => LayoutProcess  (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.List      => LayoutList      (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Cycle     => LayoutCycle     (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Hierarchy => LayoutHierarchy (data,  frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan, IsOrgChartLayout(data.LayoutUniqueId)),
            SmartArtFamily.Matrix    => LayoutMatrix    (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Relationship => IsBasicVennLayout(data.LayoutUniqueId)
                ? LayoutBasicVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsTargetListLayout(data.LayoutUniqueId)
                    ? LayoutTargetList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : null,
            _                        => null
        };
    }

    // ── Node flattening ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens the tree to a display-order list.
    /// For Process/List/Cycle: returns every visible node in connection/tree order.
    /// For Hierarchy: returns all nodes recursively (the layout engine handles tree structure).
    /// </summary>
    private static List<SmartArtNode> FlattenNodes(SmartArtData data)
    {
        if (data.Family == SmartArtFamily.Hierarchy)
        {
            // Hierarchy needs the full tree — return nodes as-is (engine recurses)
            return data.Nodes.ToList();
        }

        // Process SmartArt in PowerPoint-authored/live corpus files can encode sequencing
        // as a parOf chain instead of root-level siblings. Treat that tree as display order
        // for flat families so descendants are not dropped from the live render plan.
        var all = new List<SmartArtNode>();
        foreach (var root in data.Nodes)
            CollectPreOrder(root, all);
        return all;
    }

    private static void CollectPreOrder(SmartArtNode node, List<SmartArtNode> output)
    {
        output.Add(node);
        foreach (var child in node.Children)
            CollectPreOrder(child, output);
    }

    // ── Color palette ──────────────────────────────────────────────────────────────────────────

    // ── Shape builder helpers ──────────────────────────────────────────────────────────────────

    private static SlideShape MakeBox(
        uint id, string text, SmartArtNodeStyle style,
        long x, long y, long cx, long cy,
        double fontSizePt = NodeFontSizePt,
        DrawingShapeKind shapeKind = DrawingShapeKind.RoundedRectangle)
    {
        var run = new Run { Text = text, Color = style.Text, Bold = true, FontSizePt = fontSizePt };
        var para = new Paragraph();
        para.Runs.Add(run);
        para.Align = TextAlign.Center;
        var body = new TextBody();
        body.Paragraphs.Add(para);
        body.Anchor = VerticalAnchor.Middle;
        body.Wrap   = true;

        return new SlideShape
        {
            Id            = id,
            Name          = $"SmartArt_Box_{id}",
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = shapeKind,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
            Fill          = new ShapeFill.Solid(style.Fill),
            Outline       = new ShapeOutline.Visible(style.Outline, style.OutlineWidthPt),
            TextBody      = body
        };
    }

    private static SlideShape MakeConnector(uint id, long x1, long y1, long x2, long y2, SmartArtConnectorStyle style)
    {
        // Represent connector as a straight line from center-right of left box to center-left of right box
        // We use a Line shape; position is bounding box of the line, FlipH/V encode direction
        long left   = Math.Min(x1, x2);
        long top    = Math.Min(y1, y2);
        long right  = Math.Max(x1, x2);
        long bottom = Math.Max(y1, y2);

        long cx = Math.Max(right - left, 914L); // at least 1 pt wide
        long cy = Math.Max(bottom - top, 914L);

        bool flipH = x2 < x1;
        bool flipV = y2 < y1;

        return new SlideShape
        {
            Id            = id,
            Name          = $"SmartArt_Conn_{id}",
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu    = left,
            OffsetYEmu    = top,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
            FlipH         = flipH,
            FlipV         = flipV,
            Outline       = new ShapeOutline.Visible(style.Outline, style.WidthPt)
        };
    }

    private static SlideShape MakeCaption(
        uint id, string text, SmartArtNodeStyle style,
        long x, long y, long cx, long cy)
    {
        var run = new Run { Text = text, Color = style.Text, Bold = true, FontSizePt = NodeFontSizePt };
        var para = new Paragraph();
        para.Runs.Add(run);
        para.Align = TextAlign.Left;

        var body = new TextBody();
        body.Paragraphs.Add(para);
        body.Anchor = VerticalAnchor.Middle;
        body.Wrap = true;

        return new SlideShape
        {
            Id = id,
            Name = $"SmartArt_Caption_{id}",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Fill = ShapeFill.None.Instance,
            Outline = ShapeOutline.None.Instance,
            TextBody = body
        };
    }

    // ── Process layout ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Horizontal row of boxes with arrow connectors between adjacent pairs.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPad = (long)(fcx * OuterPaddingFrac);

        // BI3: Scale gap and connectorW down proportionally when many nodes would overflow.
        // Raw (unscaled) values:
        long rawConnectorW = (long)(fcx * 0.03);
        long rawGap        = (long)(fcx * GapFrac);

        // Total overhead consumed by gaps+connectors between the n boxes:
        long rawOverhead = (n - 1) * (rawGap + rawConnectorW);
        long innerW      = fcx - 2 * outerPad;

        // If the overhead alone would consume more than 50% of innerW, shrink it to 50%
        // (leaves at least half the inner width for the boxes themselves).
        double scale = (rawOverhead > 0 && rawOverhead > innerW / 2)
            ? (double)(innerW / 2) / rawOverhead
            : 1.0;

        long connectorW = (long)(rawConnectorW * scale);
        long gap        = (long)(rawGap        * scale);

        long availW = fcx - 2 * outerPad - (n - 1) * (gap + connectorW);
        long boxW   = n > 0 ? Math.Max(availW / n, 1L) : 1L;

        long outerPadY = (long)(fcy * 0.12);
        long boxH      = fcy - 2 * outerPadY;
        long topY      = fy + outerPadY;

        uint idCounter = 100;
        long curX = fx + outerPad;

        for (int i = 0; i < n; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, curX, topY, boxW, boxH));

            if (i < n - 1)
            {
                // Arrow connector from right edge of box to left edge of next box
                long connX = curX + boxW + gap / 2;
                long connY = topY + boxH / 2;
                shapes.Add(MakeConnector(idCounter++, connX, connY, connX + connectorW, connY, stylePlan.Connector));
            }

            curX += boxW + gap + connectorW;
        }

        return shapes;
    }

    // ── List layout ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vertical stack of boxes (no connectors — standard list layout).
    /// </summary>
    /// <summary>
    /// Alternating process geometry: ordered steps alternate between an upper and lower
    /// track while connectors keep the same shared DrawOp path for WPF and Avalonia.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutAlternatingProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        int columns = Math.Max((n + 1) / 2, 1);
        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * 0.10);
        long gapX = columns > 1 ? (long)(fcx * GapFrac) : 0;
        long gapY = (long)(fcy * 0.08);

        long availW = fcx - 2 * outerPadX - (columns - 1) * gapX;
        long boxW = Math.Max(availW / columns, 1L);
        long boxH = Math.Max((fcy - 2 * outerPadY - gapY) / 2, 1L);

        var centers = new (long x, long y)[n];
        uint idCounter = 180;

        for (int i = 0; i < n; i++)
        {
            int column = i / 2;
            bool lowerTrack = i % 2 == 1;
            long x = fx + outerPadX + column * (boxW + gapX);
            long y = fy + outerPadY + (lowerTrack ? boxH + gapY : 0);

            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, x, y, boxW, boxH));
            centers[i] = (x + boxW / 2, y + boxH / 2);
        }

        for (int i = 0; i < n - 1; i++)
        {
            var from = centers[i];
            var to = centers[i + 1];
            shapes.Add(MakeConnector(idCounter++, from.x, from.y, to.x, to.y, stylePlan.Connector));
        }

        return shapes;
    }

    private static IReadOnlyList<SlideShape> LayoutList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY      = (long)(fcy * GapFrac);

        long boxW = fcx - 2 * outerPadX;
        long availH = fcy - 2 * outerPadY - (n - 1) * gapY;
        long boxH   = n > 0 ? Math.Max(availH / n, 1L) : 1L;

        uint idCounter = 200;
        long curY = fy + outerPadY;
        long leftX = fx + outerPadX;

        for (int i = 0; i < n; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, leftX, curY, boxW, boxH));
            curY += boxH + gapY;
        }

        return shapes;
    }

    // ── Cycle layout ───────────────────────────────────────────────────────────────────────────

    // Matrix layout.

    /// <summary>
    /// Bounded two-by-two quadrant grid for PowerPoint matrix layouts.
    /// More than four parsed nodes keep cached drawing fallback in control.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutMatrix(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n is 0 or > 4)
            return null;

        int columns = n == 1 ? 1 : 2;
        int rows = n <= 2 ? 1 : 2;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapX = columns > 1 ? (long)(fcx * GapFrac) : 0;
        long gapY = rows > 1 ? (long)(fcy * GapFrac) : 0;

        long availW = fcx - 2 * outerPadX - (columns - 1) * gapX;
        long availH = fcy - 2 * outerPadY - (rows - 1) * gapY;
        long boxW = Math.Max(availW / columns, 1L);
        long boxH = Math.Max(availH / rows, 1L);

        var shapes = new List<SlideShape>();
        uint idCounter = 500;

        for (int i = 0; i < n; i++)
        {
            int row = i / columns;
            int column = i % columns;
            long x = fx + outerPadX + column * (boxW + gapX);
            long y = fy + outerPadY + row * (boxH + gapY);
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Matrix);
            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                nodeStyle,
                x,
                y,
                boxW,
                boxH,
                NodeFontSizePt,
                DrawingShapeKind.Rectangle));
        }

        return shapes;
    }

    /// <summary>
    /// Boxes arranged on a circle with arrow connectors between adjacent boxes.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutPictureCaptionList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Any(n => n.Picture is not { Bytes.Length: > 0 }))
            return null;

        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = (long)(fcy * GapFrac);
        long gapX = (long)(fcx * GapFrac);

        long rowW = fcx - 2 * outerPadX;
        long availH = fcy - 2 * outerPadY - (n - 1) * gapY;
        long rowH = n > 0 ? Math.Max(availH / n, 1L) : 1L;
        long pictureW = Math.Min((long)(rowW * 0.34), rowH);
        pictureW = Math.Max(pictureW, 1L);
        long captionW = Math.Max(rowW - pictureW - gapX, 1L);

        uint idCounter = 260;
        long curY = fy + outerPadY;
        long leftX = fx + outerPadX;

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            shapes.Add(new SlideShape
            {
                Id = idCounter++,
                Name = $"SmartArt_Picture_{idCounter}",
                Kind = SlideShapeKind.Picture,
                OffsetXEmu = leftX,
                OffsetYEmu = curY,
                ExtentCxEmu = pictureW,
                ExtentCyEmu = rowH,
                Picture = node.Picture
            });

            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            shapes.Add(MakeCaption(
                idCounter++,
                node.Text,
                nodeStyle,
                leftX + pictureW + gapX,
                curY,
                captionW,
                rowH));

            curY += rowH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Basic pyramid geometry: top-to-bottom centered segments that widen toward
    /// the base. This owns renderer-neutral segment placement, not exact
    /// PowerPoint bevels, effects, or merged segment contours.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutBasicPyramid(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = (long)(fcy * 0.01);

        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availH = Math.Max(fcy - 2 * outerPadY - (n - 1) * gapY, 1L);
        long segmentH = Math.Max(availH / n, 1L);
        double minWidthFrac = n == 1 ? 1.0 : 0.34;

        uint idCounter = 520;
        long curY = fy + outerPadY;

        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 1.0 : (double)i / (n - 1);
            double widthFrac = minWidthFrac + ((1.0 - minWidthFrac) * t);
            long segmentW = Math.Max((long)(innerW * widthFrac), 1L);
            long x = fx + outerPadX + (innerW - segmentW) / 2;
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.List);
            var shapeKind = i == 0 ? DrawingShapeKind.Triangle : DrawingShapeKind.Trapezoid;

            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                nodeStyle,
                x,
                curY,
                segmentW,
                segmentH,
                NodeFontSizePt,
                shapeKind));

            curY += segmentH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Basic Venn geometry: overlapping translucent ellipses centered in the
    /// frame. This models bounded relationship-family placement with shared
    /// shape ops, not exact PowerPoint blend math or text offsets.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutBasicVenn(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n is 0 or > 4)
            return null;

        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);

        const double overlapStepFrac = 0.58;
        long diameter = n == 1
            ? Math.Min((long)(innerW * 0.62), (long)(innerH * 0.82))
            : Math.Min((long)(innerW / (1.0 + overlapStepFrac * (n - 1))), (long)(innerH * 0.82));
        diameter = Math.Max(diameter, 1L);

        long step = n == 1 ? 0 : Math.Max((long)(diameter * overlapStepFrac), 1L);
        long totalW = diameter + (n - 1) * step;
        long leftX = fx + outerPadX + Math.Max((innerW - totalW) / 2, 0L);
        long topY = fy + outerPadY + Math.Max((innerH - diameter) / 2, 0L);

        uint idCounter = 540;
        for (int i = 0; i < n; i++)
        {
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var translucentStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 150)
            };

            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                translucentStyle,
                leftX + i * step,
                topY,
                diameter,
                diameter,
                NodeFontSizePt,
                DrawingShapeKind.Ellipse));
        }

        return shapes;
    }

    /// <summary>
    /// Basic target/list geometry: concentric ellipses centered in the frame.
    /// This is a bounded relationship-family approximation; exact PowerPoint
    /// ring clipping, label offsets, and effects remain on cached fallback.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutTargetList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n is 0 or > 5)
            return null;

        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        long maxDiameter = Math.Max(Math.Min(innerW, innerH), 1L);
        double centerX = fx + outerPadX + innerW / 2.0;
        double centerY = fy + outerPadY + innerH / 2.0;
        double step = n == 1 ? 0.0 : 0.72 / n;

        uint idCounter = 560;
        for (int i = 0; i < n; i++)
        {
            double diameterFrac = Math.Max(1.0 - i * step, 0.28);
            long diameter = Math.Max((long)(maxDiameter * diameterFrac), 1L);
            long x = (long)(centerX - diameter / 2.0);
            long y = (long)(centerY - diameter / 2.0);
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var translucentStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 205)
            };

            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                translucentStyle,
                x,
                y,
                diameter,
                diameter,
                NodeFontSizePt,
                DrawingShapeKind.Ellipse));
        }

        return shapes;
    }

    private static IReadOnlyList<SlideShape> LayoutCycle(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();
        if (n == 0) return shapes;

        // Use an inscribed circle in the frame (accounting for padding)
        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);

        long innerCx = fcx - 2 * padX;
        long innerCy = fcy - 2 * padY;

        // Center of layout in EMU
        double cx = fx + padX + innerCx / 2.0;
        double cy = fy + padY + innerCy / 2.0;

        // Radius to box centers — slightly less than half to leave room for the box itself
        double radiusX = innerCx / 2.0 * 0.62;
        double radiusY = innerCy / 2.0 * 0.62;

        // Box size: sized so they don't overlap (chord fraction)
        double angleDeg  = 360.0 / n;
        double angleRad  = angleDeg * Math.PI / 180.0;
        double boxFrac   = Math.Min(0.45, Math.Sin(angleRad / 2) * 0.9);
        long boxW = (long)(innerCx * boxFrac);
        long boxH = (long)(innerCy * boxFrac);
        // Ensure minimum readable size
        boxW = Math.Max(boxW, (long)(innerCx * 0.15));
        boxH = Math.Max(boxH, (long)(innerCy * 0.15));

        // Store center positions for connectors
        var centers = new (double x, double y)[n];
        uint idCounter = 300;

        for (int i = 0; i < n; i++)
        {
            // Start at top (−90°) and go clockwise
            double angle = (-90 + i * angleDeg) * Math.PI / 180.0;
            double bx = cx + radiusX * Math.Cos(angle);
            double by = cy + radiusY * Math.Sin(angle);

            long left = (long)(bx - boxW / 2.0);
            long top  = (long)(by - boxH / 2.0);

            centers[i] = (bx, by);

            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Cycle);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, left, top, boxW, boxH));
        }

        // Arrow connectors: from edge of each box to edge of next box (clockwise)
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var (ax, ay) = centers[i];
            var (bx, by) = centers[j];

            // Midpoint offset toward center
            shapes.Add(MakeConnector(idCounter++, (long)ax, (long)ay, (long)bx, (long)by, stylePlan.Connector));
        }

        return shapes;
    }

    // ── Hierarchy layout ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tree layout: root at top, children spread below, connector lines joining parent to children.
    /// Handles arbitrary depth; widths computed bottom-up.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutHierarchy(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan,
        bool useOrgChartAssistantLayout)
    {
        var shapes = new List<SlideShape>();
        if (data.Nodes.Count == 0) return shapes;

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);

        long availW = fcx - 2 * padX;
        long availH = fcy - 2 * padY;

        // BI4: Measure across ALL roots so sizing accounts for the whole forest.
        int treeDepth = useOrgChartAssistantLayout
            ? data.Nodes.Max(GetOrgChartTreeDepth)
            : data.Nodes.Max(GetTreeDepth);
        int treeMaxWidth = useOrgChartAssistantLayout
            ? data.Nodes.Sum(GetOrgChartTreeWidth)
            : data.Nodes.Sum(GetTreeWidth);

        treeDepth    = Math.Max(treeDepth, 1);
        treeMaxWidth = Math.Max(treeMaxWidth, 1);

        long gapY = (long)(fcy * GapFrac);
        long gapX = (long)(fcx * GapFrac);

        // Compute box height from depth
        long boxH = (availH - (treeDepth - 1) * gapY) / treeDepth;
        boxH = Math.Max(boxH, (long)(fcy * 0.10));

        // Box width is determined from the total leaf-column count across all roots
        long boxW = (long)(availW / Math.Max(treeMaxWidth, 1) - gapX);
        boxW = Math.Max(boxW, (long)(fcx * 0.08));

        uint idCounter = 400;
        long startX    = fx + padX;
        long startY    = fy + padY;

        // BI4: Lay out each root side-by-side, each allocated horizontal space
        // proportional to its subtree leaf-width.
        long curX = startX;
        foreach (var root in data.Nodes)
        {
            int rootWidth = useOrgChartAssistantLayout ? GetOrgChartTreeWidth(root) : GetTreeWidth(root);
            long rootSlotW = (long)((double)rootWidth / treeMaxWidth * availW);

            RenderNode(root, 0, 0, rootWidth, curX, startY, rootSlotW, boxW, boxH, gapX, gapY,
                shapes, stylePlan, ref idCounter, useOrgChartAssistantLayout, parentCenterX: -1, parentBottomY: -1);

            curX += rootSlotW;
        }

        return shapes;
    }

    /// <summary>Recursively renders a hierarchy node and its children.</summary>
    /// <param name="node">Node to render.</param>
    /// <param name="levelIndex">Depth level (unused, kept for diagnostics).</param>
    /// <param name="siblingIndex">Not used here — slot is passed via startX/availW.</param>
    /// <param name="levelWidth">Total leaf-column width of this node's subtree (for proportional sizing).</param>
    /// <param name="startX">Left edge of the slot allocated to this subtree.</param>
    /// <param name="levelY">Top Y of this level's boxes.</param>
    /// <param name="availW">Horizontal space allocated to this subtree (the slot width).</param>
    private static void RenderNode(
        SmartArtNode node,
        int levelIndex, int siblingIndex, int levelWidth,
        long startX, long levelY, long availW,
        long boxW, long boxH, long gapX, long gapY,
        List<SlideShape> shapes,
        SmartArtStylePlan stylePlan,
        ref uint idCounter,
        bool useOrgChartAssistantLayout,
        long parentCenterX, long parentBottomY)
    {
        // BI1: The slot for this node is exactly availW (already pre-allocated by the caller).
        // Center the box within its slot, clamping boxW so it never exceeds the slot.
        long slotW = availW;
        long nodeBoxW = Math.Min(boxW, Math.Max(slotW - gapX, 1L));

        long boxX = startX + (slotW - nodeBoxW) / 2;
        long boxY = levelY;

        var nodeStyle = stylePlan.GetNodeStyle(0, node.Level, SmartArtFamily.Hierarchy);
        shapes.Add(MakeBox(idCounter++, node.Text, nodeStyle, boxX, boxY, nodeBoxW, boxH,
            node.Level == 0 ? NodeFontSizeLargePt : NodeFontSizePt));

        long boxCenterX = boxX + nodeBoxW / 2;
        long boxTopY    = boxY;
        long boxBottomY = boxY + boxH;

        // Connector from parent bottom-center to this box top-center
        if (parentCenterX >= 0 && parentBottomY >= 0)
        {
            shapes.Add(MakeConnector(idCounter++, parentCenterX, parentBottomY, boxCenterX, boxTopY, stylePlan.Connector));
        }

        // Lay out children
        if (node.Children.Count > 0)
        {
            long childLevelY = boxBottomY + gapY;
            List<SmartArtNode> assistantChildren = useOrgChartAssistantLayout
                ? node.Children.Where(child => child.IsAssistant).ToList()
                : new List<SmartArtNode>();
            List<SmartArtNode> regularChildren = useOrgChartAssistantLayout
                ? node.Children.Where(child => !child.IsAssistant).ToList()
                : node.Children;

            if (assistantChildren.Count > 0)
            {
                long assistantSlotW = Math.Min(availW, Math.Max(availW / 3, boxW + gapX));
                long maxAssistantStartX = Math.Max(startX, startX + availW - assistantSlotW);
                long preferredAssistantStartX = boxCenterX + gapX;
                long assistantStartX = Math.Clamp(preferredAssistantStartX, startX, maxAssistantStartX);
                long assistantBoxW = Math.Max(Math.Min(boxW * 4 / 5, assistantSlotW - gapX), 1L);

                foreach (var assistant in assistantChildren)
                {
                    var assistantDepth = GetOrgChartTreeDepth(assistant);
                    RenderNode(assistant,
                        node.Level + 1,
                        0,
                        GetOrgChartTreeWidth(assistant),
                        assistantStartX,
                        childLevelY,
                        assistantSlotW,
                        assistantBoxW,
                        boxH,
                        gapX,
                        gapY,
                        shapes,
                        stylePlan,
                        ref idCounter,
                        useOrgChartAssistantLayout,
                        parentCenterX: boxCenterX,
                        parentBottomY: boxBottomY);

                    childLevelY += assistantDepth * (boxH + gapY);
                }
            }

            // BI1: Distribute children's horizontal slots PROPORTIONALLY by each child's
            // GetTreeWidth (subtree leaf count), not evenly by sibling count.
            // This prevents unbalanced trees from assigning slots narrower than boxW.
            if (regularChildren.Count > 0)
            {
                int totalChildWidth = useOrgChartAssistantLayout
                    ? regularChildren.Sum(GetOrgChartTreeWidth)
                    : regularChildren.Sum(GetTreeWidth);
                totalChildWidth = Math.Max(totalChildWidth, 1);

                long childCurX = startX;
                foreach (var child in regularChildren)
                {
                    int childWidth = useOrgChartAssistantLayout ? GetOrgChartTreeWidth(child) : GetTreeWidth(child);
                    long childSlotW = (long)((double)childWidth / totalChildWidth * availW);

                    RenderNode(child,
                        node.Level + 1,
                        0, childWidth,
                        childCurX, childLevelY, childSlotW,
                        boxW, boxH, gapX, gapY,
                        shapes, stylePlan, ref idCounter,
                        useOrgChartAssistantLayout,
                        parentCenterX: boxCenterX,
                        parentBottomY: boxBottomY);

                    childCurX += childSlotW;
                }
            }
        }
    }

    // ── Tree metrics helpers ───────────────────────────────────────────────────────────────────

    private static int GetTreeDepth(SmartArtNode node)
    {
        if (node.Children.Count == 0) return 1;
        return 1 + node.Children.Max(GetTreeDepth);
    }

    private static int GetTreeWidth(SmartArtNode node)
    {
        if (node.Children.Count == 0) return 1;
        return node.Children.Sum(GetTreeWidth);
    }

    private static int GetOrgChartTreeDepth(SmartArtNode node)
    {
        if (node.Children.Count == 0) return 1;

        var assistants = node.Children.Where(child => child.IsAssistant).ToList();
        var regular = node.Children.Where(child => !child.IsAssistant).ToList();
        if (assistants.Count == 0)
            return 1 + node.Children.Max(GetOrgChartTreeDepth);

        int assistantRows = assistants.Sum(GetOrgChartTreeDepth);
        int regularRows = regular.Count == 0 ? 0 : regular.Max(GetOrgChartTreeDepth);
        return 1 + assistantRows + regularRows;
    }

    private static int GetOrgChartTreeWidth(SmartArtNode node)
    {
        if (node.Children.Count == 0) return 1;

        var regular = node.Children.Where(child => !child.IsAssistant).ToList();
        if (regular.Count == 0)
            return Math.Max(1, node.Children.Sum(GetOrgChartTreeWidth));

        return regular.Sum(GetOrgChartTreeWidth);
    }

    private static bool IsOrgChartLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "orgchart", StringComparison.Ordinal);
    }

    private static bool IsPictureCaptionListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "picturecaptionlist", StringComparison.Ordinal);
    }

    private static bool IsAlternatingProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "alternatingprocess", StringComparison.Ordinal);
    }

    private static bool IsBasicPyramidLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "basicpyramid", StringComparison.Ordinal);
    }

    private static bool IsBasicVennLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "basicvenn", StringComparison.Ordinal);
    }

    private static bool IsTargetListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "targetlist", StringComparison.Ordinal);
    }
}
