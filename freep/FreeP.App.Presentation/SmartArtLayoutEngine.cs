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

        if (IsPictureGridLayout(data.LayoutUniqueId))
            return LayoutPictureGrid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsAlternatingProcessLayout(data.LayoutUniqueId))
            return LayoutAlternatingProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicTimelineLayout(data.LayoutUniqueId))
            return LayoutBasicTimeline(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicRadialLayout(data.LayoutUniqueId))
            return LayoutBasicRadial(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsStepDownProcessLayout(data.LayoutUniqueId))
            return LayoutStepDownProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsArrowRibbonLayout(data.LayoutUniqueId))
            return LayoutArrowRibbon(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsCircleProcessLayout(data.LayoutUniqueId))
            return LayoutCircleProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsFunnelProcessLayout(data.LayoutUniqueId))
            return LayoutFunnelProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsVerticalProcessLayout(data.LayoutUniqueId))
            return LayoutVerticalProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsDescendingBlockListLayout(data.LayoutUniqueId))
            return LayoutDescendingBlockList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicPyramidLayout(data.LayoutUniqueId))
            return LayoutBasicPyramid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPyramidListLayout(data.LayoutUniqueId))
            return LayoutPyramidList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsHierarchy3Layout(data.LayoutUniqueId))
            return LayoutHierarchy3(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsHorizontalHierarchyLayout(data.LayoutUniqueId))
            return LayoutHorizontalHierarchy(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsTableHierarchyLayout(data.LayoutUniqueId))
            return LayoutTableHierarchy(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        return data.Family switch
        {
            SmartArtFamily.Process   => LayoutProcess  (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.List      => LayoutList      (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Cycle     => LayoutCycle     (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Hierarchy => LayoutHierarchy (data,  frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan, IsOrgChartLayout(data.LayoutUniqueId)),
            SmartArtFamily.Matrix    => LayoutMatrix    (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan),
            SmartArtFamily.Relationship => IsBasicVennLayout(data.LayoutUniqueId)
                ? LayoutBasicVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsBasicRelationshipLayout(data.LayoutUniqueId)
                    ? LayoutBasicRelationship(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsRadialVennLayout(data.LayoutUniqueId)
                    ? LayoutRadialVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                    : IsTargetListLayout(data.LayoutUniqueId)
                        ? LayoutTargetList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                        : IsStackedVennLayout(data.LayoutUniqueId)
                            ? LayoutStackedVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
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

    /// <summary>
    /// Basic Timeline geometry: a shared horizontal time rail, one marker per node,
    /// and alternating text boxes above and below the rail. This keeps the node order
    /// and connector ownership deterministic for both live hosts.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutBasicTimeline(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();
        if (n == 0) return shapes;

        long padX = Math.Max((long)(fcx * 0.06), 1L);
        long railY = fy + fcy / 2;
        long railLeft = fx + padX;
        long railRight = fx + fcx - padX;
        long marker = Math.Max(Math.Min((long)(fcy * 0.075), (long)(fcx * 0.025)), 1L);
        long gap = Math.Max((long)(fcy * 0.035), marker);
        long boxH = Math.Max((long)(fcy * 0.23), 1L);
        long innerW = Math.Max(railRight - railLeft, 1L);
        long step = n > 1 ? innerW / (n - 1) : 0L;
        long boxW = Math.Max(n > 1 ? (long)(step * 0.72) : (long)(innerW * 0.58), 1L);
        uint idCounter = 240;

        shapes.Add(MakeConnector(idCounter++, railLeft, railY, railRight, railY, stylePlan.Connector));

        for (int i = 0; i < n; i++)
        {
            long centerX = n > 1 ? railLeft + i * step : railLeft + innerW / 2;
            long markerX = centerX - marker / 2;
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);

            shapes.Add(MakeBox(
                idCounter++, string.Empty, nodeStyle,
                markerX, railY - marker / 2, marker, marker,
                NodeFontSizePt, DrawingShapeKind.Ellipse));

            bool aboveRail = i % 2 == 0;
            long boxX = centerX - boxW / 2;
            long boxY = aboveRail
                ? railY - marker / 2 - gap - boxH
                : railY + marker / 2 + gap;
            long boxEdgeY = aboveRail ? boxY + boxH : boxY;
            shapes.Add(MakeConnector(idCounter++, centerX, railY, centerX, boxEdgeY, stylePlan.Connector));
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, boxX, boxY, boxW, boxH));
        }

        return shapes;
    }

    /// <summary>
    /// Step Down Process geometry: ordered stages descend diagonally through the frame,
    /// with each connector attached to the preceding stage. This is distinct from the
    /// single-row process layout while keeping the same renderer-neutral shape contract.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutStepDownProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();
        if (n == 0) return shapes;

        long padX = Math.Max((long)(fcx * 0.06), 1L);
        long padY = Math.Max((long)(fcy * 0.08), 1L);
        long gapX = Math.Max((long)(fcx * 0.035), 1L);
        long gapY = Math.Max((long)(fcy * 0.035), 1L);
        long boxW = Math.Max((fcx - 2 * padX - Math.Min(n - 1, 3) * gapX) / Math.Min(n, 4), 1L);
        long boxH = Math.Max((fcy - 2 * padY - Math.Min(n - 1, 3) * gapY) / Math.Min(n, 4), 1L);
        boxH = Math.Min(boxH, (long)(fcy * 0.23));
        long stepX = boxW + gapX;
        long stepY = boxH + gapY;
        uint idCounter = 270;
        var centers = new (long x, long y)[n];

        for (int i = 0; i < n; i++)
        {
            int row = i / 4;
            int column = i % 4;
            long x = fx + padX + column * stepX;
            long y = fy + padY + (row * stepY) + column * Math.Max(gapY / 2, 1L);
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, x, y, boxW, boxH));
            centers[i] = (x + boxW / 2, y + boxH / 2);

            if (i > 0)
            {
                var previous = centers[i - 1];
                shapes.Add(MakeConnector(
                    idCounter++,
                    previous.x + boxW / 2,
                    previous.y,
                    centers[i].x - boxW / 2,
                    centers[i].y,
                    stylePlan.Connector));
            }
        }

        return shapes;
    }

    /// <summary>
    /// Arrow ribbon geometry: ordered process stages represented as shared ribbon
    /// segments with straight connector ops. This is intentionally renderer-neutral
    /// live layout, not exact PowerPoint folded-ribbon artwork.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutArrowRibbon(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * 0.16);
        long connectorW = n > 1 ? (long)(fcx * 0.025) : 0;
        long gap = n > 1 ? (long)(fcx * 0.015) : 0;
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long boxW = Math.Max((innerW - (n - 1) * (connectorW + gap)) / n, 1L);
        long boxH = Math.Max(fcy - 2 * outerPadY, 1L);
        long y = fy + outerPadY;
        long x = fx + outerPadX;

        uint idCounter = 600;
        for (int i = 0; i < n; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                nodeStyle,
                x,
                y,
                boxW,
                boxH,
                NodeFontSizePt,
                DrawingShapeKind.Ribbon));

            if (i < n - 1)
            {
                long connY = y + boxH / 2;
                long connX1 = x + boxW + gap / 2;
                long connX2 = connX1 + connectorW;
                shapes.Add(MakeConnector(idCounter++, connX1, connY, connX2, connY, stylePlan.Connector));
            }

            x += boxW + connectorW + gap;
        }

        return shapes;
    }

    /// <summary>
    /// Circle process geometry: ordered process stages placed around an ellipse
    /// with clockwise connector ops. This is a bounded shared approximation,
    /// not exact PowerPoint circular arrow or segment artwork.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutCircleProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();
        if (n == 0) return shapes;

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long innerCx = Math.Max(fcx - 2 * padX, 1L);
        long innerCy = Math.Max(fcy - 2 * padY, 1L);

        double centerX = fx + padX + innerCx / 2.0;
        double centerY = fy + padY + innerCy / 2.0;
        double radiusX = innerCx / 2.0 * 0.60;
        double radiusY = innerCy / 2.0 * 0.60;

        double angleStep = 360.0 / n;
        double angleRad = angleStep * Math.PI / 180.0;
        double boxFrac = Math.Min(0.42, Math.Sin(angleRad / 2) * 0.86);
        long boxW = Math.Max((long)(innerCx * boxFrac), (long)(innerCx * 0.14));
        long boxH = Math.Max((long)(innerCy * boxFrac), (long)(innerCy * 0.14));

        var centers = new (double x, double y)[n];
        uint idCounter = 320;

        for (int i = 0; i < n; i++)
        {
            double angle = (-90 + i * angleStep) * Math.PI / 180.0;
            double boxCenterX = centerX + radiusX * Math.Cos(angle);
            double boxCenterY = centerY + radiusY * Math.Sin(angle);
            long left = (long)(boxCenterX - boxW / 2.0);
            long top = (long)(boxCenterY - boxH / 2.0);

            centers[i] = (boxCenterX, boxCenterY);
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, left, top, boxW, boxH));
        }

        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            shapes.Add(MakeConnector(
                idCounter++,
                (long)centers[i].x,
                (long)centers[i].y,
                (long)centers[next].x,
                (long)centers[next].y,
                stylePlan.Connector));
        }

        return shapes;
    }

    /// <summary>
    /// Funnel process geometry: ordered stages stack vertically and narrow toward
    /// the bottom while connectors keep WPF/Avalonia on the same shared DrawOp path.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutFunnelProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();
        var centers = new (long x, long topY, long bottomY)[n];

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = (long)(fcy * 0.018);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availH = Math.Max(fcy - 2 * outerPadY - (n - 1) * gapY, 1L);
        long stageH = Math.Max(availH / n, 1L);
        double minWidthFrac = n == 1 ? 0.82 : 0.42;

        uint idCounter = 190;
        long curY = fy + outerPadY;

        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0.0 : (double)i / (n - 1);
            double widthFrac = 1.0 - ((1.0 - minWidthFrac) * t);
            long stageW = Math.Max((long)(innerW * widthFrac), 1L);
            long x = fx + outerPadX + (innerW - stageW) / 2;
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);

            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                nodeStyle,
                x,
                curY,
                stageW,
                stageH,
                NodeFontSizePt,
                DrawingShapeKind.Trapezoid));

            centers[i] = (x + stageW / 2, curY, curY + stageH);
            curY += stageH + gapY;
        }

        for (int i = 0; i < n - 1; i++)
        {
            var from = centers[i];
            var to = centers[i + 1];
            shapes.Add(MakeConnector(idCounter++, from.x, from.bottomY, to.x, to.topY, stylePlan.Connector));
        }

        return shapes;
    }

    /// <summary>
    /// Vertical process geometry: ordered stages stack top-to-bottom while shared
    /// connector ops keep WPF and Avalonia on the same renderer-neutral path.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutVerticalProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * 0.18);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long rawConnectorH = n > 1 ? (long)(fcy * 0.035) : 0;
        long rawGap = n > 1 ? (long)(fcy * GapFrac) : 0;
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        long rawOverhead = (n - 1) * (rawGap + rawConnectorH);

        double scale = (rawOverhead > 0 && rawOverhead > innerH / 2)
            ? (double)(innerH / 2) / rawOverhead
            : 1.0;

        long connectorH = (long)(rawConnectorH * scale);
        long gap = (long)(rawGap * scale);
        long boxW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availH = Math.Max(fcy - 2 * outerPadY - (n - 1) * (gap + connectorH), 1L);
        long boxH = Math.Max(availH / n, 1L);
        long x = fx + outerPadX;
        long curY = fy + outerPadY;

        uint idCounter = 720;
        for (int i = 0; i < n; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, x, curY, boxW, boxH));

            if (i < n - 1)
            {
                long connX = x + boxW / 2;
                long connY1 = curY + boxH + gap / 2;
                long connY2 = connY1 + connectorH;
                shapes.Add(MakeConnector(idCounter++, connX, connY1, connX, connY2, stylePlan.Connector));
            }

            curY += boxH + gap + connectorH;
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
    /// Descending block list geometry: top-to-bottom list blocks narrow toward
    /// the bottom while keeping their right edge aligned.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutDescendingBlockList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = (long)(fcy * GapFrac);

        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availH = Math.Max(fcy - 2 * outerPadY - (n - 1) * gapY, 1L);
        long boxH = n > 0 ? Math.Max(availH / n, 1L) : 1L;
        long rightX = fx + outerPadX + innerW;
        double minWidthFrac = n == 1 ? 1.0 : 0.58;

        uint idCounter = 280;
        long curY = fy + outerPadY;

        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0.0 : (double)i / (n - 1);
            double widthFrac = 1.0 - ((1.0 - minWidthFrac) * t);
            long boxW = Math.Max((long)(innerW * widthFrac), 1L);
            long x = rightX - boxW;

            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, x, curY, boxW, boxH));
            curY += boxH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Two-column matrix grid for PowerPoint matrix layouts.
    /// Four nodes retain the traditional quadrant geometry; larger node sets
    /// continue into additional rows instead of dropping to cached drawing.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutMatrix(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n == 0)
            return null;

        int columns = n == 1 ? 1 : 2;
        int rows = (n + columns - 1) / columns;

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

    private static IReadOnlyList<SlideShape>? LayoutPictureGrid(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Any(n => n.Picture is not { Bytes.Length: > 0 }))
            return null;

        var columns = Math.Min(2, Math.Max(1, nodes.Count));
        var rows = (nodes.Count + columns - 1) / columns;
        var padX = (long)(fcx * OuterPaddingFrac);
        var padY = (long)(fcy * OuterPaddingFrac);
        var gapX = Math.Max((long)(fcx * 0.03), 1L);
        var gapY = Math.Max((long)(fcy * 0.03), 1L);
        var cellW = Math.Max((fcx - 2 * padX - (columns - 1) * gapX) / columns, 1L);
        var cellH = Math.Max((fcy - 2 * padY - (rows - 1) * gapY) / rows, 1L);
        var pictureH = Math.Max((long)(cellH * 0.62), 1L);
        var captionH = Math.Max(cellH - pictureH, 1L);
        var shapes = new List<SlideShape>(nodes.Count * 2);
        uint idCounter = 310;

        for (var index = 0; index < nodes.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = fx + padX + column * (cellW + gapX);
            var y = fy + padY + row * (cellH + gapY);
            shapes.Add(new SlideShape
            {
                Id = idCounter++,
                Name = $"SmartArt_GridPicture_{index + 1}",
                Kind = SlideShapeKind.Picture,
                OffsetXEmu = x,
                OffsetYEmu = y,
                ExtentCxEmu = cellW,
                ExtentCyEmu = pictureH,
                Picture = nodes[index].Picture,
            });

            var nodeStyle = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            shapes.Add(MakeCaption(
                idCounter++,
                nodes[index].Text,
                nodeStyle,
                x,
                y + pictureH,
                cellW,
                captionH));
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
    /// Pyramid List geometry: centered rows that narrow toward the base, with
    /// the widest segment at the top. The native layout ID remains authoritative
    /// for save/reopen; this supplies a stable live edit/render path.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutPyramidList(
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

        uint idCounter = 530;
        long curY = fy + outerPadY;
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 1.0 : (double)i / (n - 1);
            double widthFrac = 1.0 - ((1.0 - minWidthFrac) * t);
            long segmentW = Math.Max((long)(innerW * widthFrac), 1L);
            long x = fx + outerPadX + (innerW - segmentW) / 2;
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.List);
            var shapeKind = i == n - 1 ? DrawingShapeKind.Triangle : DrawingShapeKind.Trapezoid;

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
    private static IReadOnlyList<SlideShape>? LayoutBasicRelationship(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count is < 2 or > 3)
            return null;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        const double overlapStepFrac = 0.58;
        long diameter = Math.Min(
            (long)(innerW / (1.0 + overlapStepFrac * (nodes.Count - 1))),
            (long)(innerH * 0.82));
        diameter = Math.Max(diameter, 1L);
        long step = Math.Max((long)(diameter * overlapStepFrac), 1L);
        long totalW = diameter + (nodes.Count - 1) * step;
        long leftX = fx + outerPadX + Math.Max((innerW - totalW) / 2, 0L);
        long topY = fy + outerPadY + Math.Max((innerH - diameter) / 2, 0L);

        var shapes = new List<SlideShape>(nodes.Count);
        uint idCounter = 530;
        for (int i = 0; i < nodes.Count; i++)
        {
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var translucentStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 150)
            };
            shapes.Add(MakeBox(
                idCounter++, nodes[i].Text, translucentStyle,
                leftX + i * step, topY, diameter, diameter,
                NodeFontSizePt, DrawingShapeKind.Ellipse));
        }

        return shapes;
    }

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
    /// Radial Venn geometry: equally sized translucent ellipses around a shared
    /// center. This keeps the relationship-family shape ops shared while exact
    /// PowerPoint intersection blending/effects remain deferred.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutRadialVenn(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n is < 3 or > 5)
            return null;

        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        long diameter = Math.Max(Math.Min((long)(innerW * 0.52), (long)(innerH * 0.58)), 1L);

        double centerX = fx + outerPadX + innerW / 2.0;
        double centerY = fy + outerPadY + innerH / 2.0;
        double radiusX = Math.Min((innerW - diameter) / 2.0, diameter * 0.44);
        double radiusY = Math.Min((innerH - diameter) / 2.0, diameter * 0.34);
        radiusX = Math.Max(radiusX, 0.0);
        radiusY = Math.Max(radiusY, 0.0);

        uint idCounter = 580;
        for (int i = 0; i < n; i++)
        {
            double angle = -Math.PI / 2.0 + (2.0 * Math.PI * i / n);
            long x = (long)Math.Round(centerX + Math.Cos(angle) * radiusX - diameter / 2.0);
            long y = (long)Math.Round(centerY + Math.Sin(angle) * radiusY - diameter / 2.0);
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var translucentStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 150)
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

    /// <summary>
    /// Stacked Venn geometry: equally sized translucent ellipses offset down and
    /// right in a readable stack. This is shared relationship-family evidence,
    /// not exact PowerPoint stacked-region styling or text offsets.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutStackedVenn(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n is < 2 or > 5)
            return null;

        var shapes = new List<SlideShape>();

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);

        const double stepXFrac = 0.24;
        const double stepYFrac = 0.30;
        long diameter = Math.Min(
            (long)(innerW / (1.0 + stepXFrac * (n - 1))),
            (long)(innerH / (1.0 + stepYFrac * (n - 1))));
        diameter = Math.Max(diameter, 1L);

        long stepX = Math.Max((long)(diameter * stepXFrac), 1L);
        long stepY = Math.Max((long)(diameter * stepYFrac), 1L);
        long totalW = diameter + (n - 1) * stepX;
        long totalH = diameter + (n - 1) * stepY;
        long leftX = fx + outerPadX + Math.Max((innerW - totalW) / 2, 0L);
        long topY = fy + outerPadY + Math.Max((innerH - totalH) / 2, 0L);

        uint idCounter = 640;
        for (int i = 0; i < n; i++)
        {
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var translucentStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 165)
            };

            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                translucentStyle,
                leftX + i * stepX,
                topY + i * stepY,
                diameter,
                diameter,
                NodeFontSizePt,
                DrawingShapeKind.Ellipse));
        }

        return shapes;
    }

    /// <summary>
    /// Basic Radial geometry: the first logical node is the central topic and the
    /// remaining nodes radiate from it with direct spoke connectors. This preserves
    /// the defining hub-and-spoke interaction of PowerPoint's radial1 layout while
    /// keeping the output on the shared shape/connector contract.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutBasicRadial(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var shapes = new List<SlideShape>();
        if (nodes.Count == 0) return shapes;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * OuterPaddingFrac), 1L);
        long innerCx = Math.Max(fcx - 2 * padX, 1L);
        long innerCy = Math.Max(fcy - 2 * padY, 1L);
        double centerX = fx + padX + innerCx / 2.0;
        double centerY = fy + padY + innerCy / 2.0;

        long centerW = Math.Max((long)(innerCx * 0.24), 1L);
        long centerH = Math.Max((long)(innerCy * 0.24), 1L);
        uint idCounter = 780;
        var centerStyle = stylePlan.GetNodeStyle(0, nodes[0].Level, SmartArtFamily.Cycle);
        shapes.Add(MakeBox(
            idCounter++, nodes[0].Text, centerStyle,
            (long)(centerX - centerW / 2.0),
            (long)(centerY - centerH / 2.0),
            centerW,
            centerH,
            NodeFontSizeLargePt,
            DrawingShapeKind.Ellipse));

        int spokeCount = nodes.Count - 1;
        if (spokeCount == 0) return shapes;

        double angleStep = 360.0 / spokeCount;
        double radiusX = innerCx / 2.0 * 0.70;
        double radiusY = innerCy / 2.0 * 0.70;
        double halfChord = Math.Sin(Math.PI / spokeCount);
        long boxW = Math.Max((long)(innerCx * Math.Min(0.28, halfChord * 0.80)), 1L);
        long boxH = Math.Max((long)(innerCy * Math.Min(0.25, halfChord * 0.80)), 1L);
        var outerCenters = new (double x, double y)[spokeCount];

        for (int i = 0; i < spokeCount; i++)
        {
            double angle = (-90 + i * angleStep) * Math.PI / 180.0;
            outerCenters[i] = (centerX + radiusX * Math.Cos(angle), centerY + radiusY * Math.Sin(angle));
            shapes.Add(MakeConnector(
                idCounter++,
                (long)centerX,
                (long)centerY,
                (long)outerCenters[i].x,
                (long)outerCenters[i].y,
                stylePlan.Connector));
        }

        for (int i = 0; i < spokeCount; i++)
        {
            var node = nodes[i + 1];
            var nodeStyle = stylePlan.GetNodeStyle(i + 1, node.Level, SmartArtFamily.Cycle);
            shapes.Add(MakeBox(
                idCounter++, node.Text, nodeStyle,
                (long)(outerCenters[i].x - boxW / 2.0),
                (long)(outerCenters[i].y - boxH / 2.0),
                boxW,
                boxH));
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
    /// Horizontal hierarchy layout: roots/parents on the left, child/report nodes
    /// in depth columns to the right, with shared connector line shapes. Hierarchy3
    /// uses the native left-to-right algorithm; empty authored template leaves remain
    /// in the model for editing but do not become visible live boxes.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutHierarchy3(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var visibleData = new SmartArtData
        {
            Family = data.Family,
            LayoutUniqueId = data.LayoutUniqueId,
            IsLiveLayoutSupported = data.IsLiveLayoutSupported
        };

        foreach (var node in data.Nodes)
        {
            var visibleNode = CloneVisibleHierarchyNode(node);
            if (visibleNode is not null)
                visibleData.Nodes.Add(visibleNode);
        }

        return LayoutHorizontalHierarchy(visibleData, fx, fy, fcx, fcy, stylePlan);
    }

    private static SmartArtNode? CloneVisibleHierarchyNode(SmartArtNode node)
    {
        var visibleChildren = node.Children
            .Select(CloneVisibleHierarchyNode)
            .Where(child => child is not null)
            .Cast<SmartArtNode>()
            .ToList();

        if (string.IsNullOrWhiteSpace(node.Text) && visibleChildren.Count == 0)
            return null;

        var clone = new SmartArtNode
        {
            ModelId = node.ModelId,
            Text = node.Text,
            Level = node.Level,
            IsAssistant = node.IsAssistant,
            Picture = node.Picture
        };
        clone.Children.AddRange(visibleChildren);
        return clone;
    }

    private static IReadOnlyList<SlideShape> LayoutHorizontalHierarchy(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var shapes = new List<SlideShape>();
        if (data.Nodes.Count == 0) return shapes;

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long availW = Math.Max(fcx - 2 * padX, 1L);
        long availH = Math.Max(fcy - 2 * padY, 1L);

        int treeDepth = Math.Max(data.Nodes.Max(GetTreeDepth), 1);
        int leafRows = Math.Max(data.Nodes.Sum(GetTreeWidth), 1);

        long gapX = treeDepth > 1 ? (long)(fcx * GapFrac) : 0;
        long gapY = leafRows > 1 ? (long)(fcy * GapFrac) : 0;

        long boxW = Math.Max((availW - (treeDepth - 1) * gapX) / treeDepth, 1L);
        long boxH = Math.Max((availH - (leafRows - 1) * gapY) / leafRows, 1L);

        long totalH = leafRows * boxH + (leafRows - 1) * gapY;
        long startX = fx + padX;
        long startY = fy + padY + Math.Max((availH - totalH) / 2, 0L);

        uint idCounter = 450;
        long curY = startY;
        foreach (var root in data.Nodes)
        {
            int rootRows = Math.Max(GetTreeWidth(root), 1);
            RenderHorizontalNode(
                root,
                levelIndex: 0,
                slotY: curY,
                leafRows: rootRows,
                startX,
                boxW,
                boxH,
                gapX,
                gapY,
                shapes,
                stylePlan,
                ref idCounter,
                parentRightX: -1,
                parentCenterY: -1);

            curY += rootRows * boxH + (rootRows - 1) * gapY + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Table hierarchy layout: each root is a full-width section heading, followed by
    /// aligned child-group columns. Descendants stay in their group's column and are
    /// stacked top-to-bottom. The native tableHierarchy definition has no connecting
    /// lines, so this plan intentionally emits cells only.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutTableHierarchy(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var visibleRoots = data.Nodes
            .Select(CloneVisibleHierarchyNode)
            .Where(node => node is not null)
            .Cast<SmartArtNode>()
            .ToList();
        if (visibleRoots.Count == 0)
            return [];

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long availW = Math.Max(fcx - 2 * padX, 1L);
        long availH = Math.Max(fcy - 2 * padY, 1L);
        long gapX = (long)(fcx * GapFrac);
        long gapY = (long)(fcy * GapFrac);

        var sections = visibleRoots
            .Select(root => new TableHierarchySection(root, root.Children
                .Select(FlattenGroupNodes)
                .Where(group => group.Count > 0)
                .ToList()))
            .ToList();
        int totalRows = sections.Sum(section =>
            1 + (section.Groups.Count == 0 ? 0 : section.Groups.Max(group => group.Count)));
        totalRows = Math.Max(totalRows, 1);
        long rowH = Math.Max((availH - Math.Max(totalRows - 1, 0) * gapY) / totalRows, 1L);

        var shapes = new List<SlideShape>();
        uint idCounter = 520;
        long currentY = fy + padY;

        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var rootStyle = stylePlan.GetNodeStyle(0, section.Root.Level, SmartArtFamily.Hierarchy);
            shapes.Add(MakeBox(
                idCounter++, section.Root.Text, rootStyle,
                fx + padX, currentY, availW, rowH,
                NodeFontSizeLargePt, DrawingShapeKind.Rectangle));
            currentY += rowH;

            if (section.Groups.Count == 0)
            {
                if (sectionIndex < sections.Count - 1)
                    currentY += gapY;
                continue;
            }

            currentY += gapY;
            long groupW = Math.Max(
                (availW - (section.Groups.Count - 1) * gapX) / section.Groups.Count,
                1L);
            int groupRows = section.Groups.Max(group => group.Count);

            for (int groupIndex = 0; groupIndex < section.Groups.Count; groupIndex++)
            {
                var group = section.Groups[groupIndex];
                long groupX = fx + padX + groupIndex * (groupW + gapX);
                for (int rowIndex = 0; rowIndex < group.Count; rowIndex++)
                {
                    var node = group[rowIndex];
                    var nodeStyle = stylePlan.GetNodeStyle(
                        groupIndex, node.Level, SmartArtFamily.Hierarchy);
                    long cellY = currentY + rowIndex * (rowH + gapY);
                    shapes.Add(MakeBox(
                        idCounter++, node.Text, nodeStyle,
                        groupX, cellY, groupW, rowH,
                        node.Level == 0 ? NodeFontSizeLargePt : NodeFontSizePt,
                        DrawingShapeKind.Rectangle));
                }
            }

            currentY += groupRows * rowH + Math.Max(groupRows - 1, 0) * gapY;
            if (sectionIndex < sections.Count - 1)
                currentY += gapY;
        }

        return shapes;
    }

    private static List<SmartArtNode> FlattenGroupNodes(SmartArtNode root)
    {
        var nodes = new List<SmartArtNode>();
        CollectPreOrder(root, nodes);
        return nodes;
    }

    private sealed record TableHierarchySection(
        SmartArtNode Root,
        List<List<SmartArtNode>> Groups);

    private static void RenderHorizontalNode(
        SmartArtNode node,
        int levelIndex,
        long slotY,
        int leafRows,
        long startX,
        long boxW,
        long boxH,
        long gapX,
        long gapY,
        List<SlideShape> shapes,
        SmartArtStylePlan stylePlan,
        ref uint idCounter,
        long parentRightX,
        long parentCenterY)
    {
        long slotH = leafRows * boxH + (leafRows - 1) * gapY;
        long boxX = startX + levelIndex * (boxW + gapX);
        long boxY = slotY + Math.Max((slotH - boxH) / 2, 0L);

        var nodeStyle = stylePlan.GetNodeStyle(0, node.Level, SmartArtFamily.Hierarchy);
        shapes.Add(MakeBox(idCounter++, node.Text, nodeStyle, boxX, boxY, boxW, boxH,
            node.Level == 0 ? NodeFontSizeLargePt : NodeFontSizePt));

        long boxRightX = boxX + boxW;
        long boxCenterY = boxY + boxH / 2;
        if (parentRightX >= 0 && parentCenterY >= 0)
            shapes.Add(MakeConnector(idCounter++, parentRightX, parentCenterY, boxX, boxCenterY, stylePlan.Connector));

        if (node.Children.Count == 0)
            return;

        long childCurY = slotY;
        foreach (var child in node.Children)
        {
            int childRows = Math.Max(GetTreeWidth(child), 1);
            RenderHorizontalNode(
                child,
                levelIndex + 1,
                childCurY,
                childRows,
                startX,
                boxW,
                boxH,
                gapX,
                gapY,
                shapes,
                stylePlan,
                ref idCounter,
                parentRightX: boxRightX,
                parentCenterY: boxCenterY);

            childCurY += childRows * boxH + (childRows - 1) * gapY + gapY;
        }
    }

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

    private static bool IsHorizontalHierarchyLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "horizontalhierarchy", StringComparison.Ordinal);
    }

    private static bool IsTableHierarchyLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "tablehierarchy", StringComparison.Ordinal);
    }

    private static bool IsHierarchy3Layout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "hierarchy3", StringComparison.Ordinal);
    }

    private static bool IsPictureCaptionListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "picturecaptionlist", StringComparison.Ordinal);
    }

    private static bool IsPictureGridLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "picturegrid", StringComparison.Ordinal);
    }

    private static bool IsAlternatingProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "alternatingprocess", StringComparison.Ordinal);
    }

    private static bool IsBasicTimelineLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "basictimeline", StringComparison.Ordinal);
    }

    private static bool IsBasicRadialLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "radial1", StringComparison.Ordinal);
    }

    private static bool IsStepDownProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "stepdownprocess", StringComparison.Ordinal);
    }

    private static bool IsArrowRibbonLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "arrowribbon", StringComparison.Ordinal);
    }

    private static bool IsCircleProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "circleprocess", StringComparison.Ordinal);
    }

    private static bool IsFunnelProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "funnelprocess", StringComparison.Ordinal);
    }

    private static bool IsVerticalProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "verticalprocess", StringComparison.Ordinal);
    }

    private static bool IsBasicPyramidLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "basicpyramid", StringComparison.Ordinal);
    }

    private static bool IsPyramidListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "pyramidlist", StringComparison.Ordinal);
    }

    private static bool IsDescendingBlockListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "descendingblocklist", StringComparison.Ordinal);
    }

    private static bool IsBasicVennLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "basicvenn", StringComparison.Ordinal);
    }

    private static bool IsBasicRelationshipLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;
        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "relationship1", StringComparison.Ordinal);
    }

    private static bool IsRadialVennLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "radialvenn", StringComparison.Ordinal);
    }

    private static bool IsTargetListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "targetlist", StringComparison.Ordinal);
    }

    private static bool IsStackedVennLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "stackedvenn", StringComparison.Ordinal);
    }
}
