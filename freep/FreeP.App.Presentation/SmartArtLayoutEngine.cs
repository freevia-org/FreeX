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
///   <see cref="SmartArtFamily.Matrix"/>    — two-column grid of boxes with additional rows
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

        if (IsVerticalBulletListLayout(data.LayoutUniqueId))
            return LayoutVerticalBulletList(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsVerticalBlockListLayout(data.LayoutUniqueId))
            return LayoutVerticalBlockList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPictureCaptionListLayout(data.LayoutUniqueId))
            return LayoutPictureCaptionList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPictureAccentListLayout(data.LayoutUniqueId))
            return LayoutPictureAccentList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPictureStackLayout(data.LayoutUniqueId))
            return LayoutPictureStack(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPictureLineupLayout(data.LayoutUniqueId))
            return LayoutPictureLineup(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPictureGridLayout(data.LayoutUniqueId))
            return LayoutPictureGrid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsAlternatingProcessLayout(data.LayoutUniqueId))
            return LayoutAlternatingProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPhasedProcessLayout(data.LayoutUniqueId))
            return LayoutPhasedProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBendingProcessLayout(data.LayoutUniqueId))
            return LayoutBendingProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicTimelineLayout(data.LayoutUniqueId))
            return LayoutBasicTimeline(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicRadialLayout(data.LayoutUniqueId))
            return LayoutBasicRadial(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsRadialClusterLayout(data.LayoutUniqueId))
            return LayoutRadialCluster(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsRadialListLayout(data.LayoutUniqueId))
            return LayoutRadialList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsStepDownProcessLayout(data.LayoutUniqueId))
            return LayoutStepDownProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsArrowRibbonLayout(data.LayoutUniqueId))
            return LayoutArrowRibbon(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsCircleProcessLayout(data.LayoutUniqueId))
            return LayoutCircleProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsCircleArrowProcessLayout(data.LayoutUniqueId))
            return LayoutCircleArrowProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsFunnelProcessLayout(data.LayoutUniqueId))
            return LayoutFunnelProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsVerticalProcessLayout(data.LayoutUniqueId))
            return LayoutVerticalProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsVerticalChevronListLayout(data.LayoutUniqueId))
            return LayoutVerticalChevronList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsVerticalArrowListLayout(data.LayoutUniqueId))
            return LayoutVerticalArrowList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsHorizontalBulletListLayout(data.LayoutUniqueId))
            return LayoutHorizontalBulletList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsHorizontalBlockListLayout(data.LayoutUniqueId))
            return LayoutHorizontalBlockList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsDescendingBlockListLayout(data.LayoutUniqueId))
            return LayoutDescendingBlockList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsChevronProcessLayout(data.LayoutUniqueId))
            return LayoutChevronProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsBasicPyramidLayout(data.LayoutUniqueId))
            return LayoutBasicPyramid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsPyramidListLayout(data.LayoutUniqueId))
            return LayoutPyramidList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsInvertedPyramidLayout(data.LayoutUniqueId))
            return LayoutInvertedPyramid(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsTitledMatrixLayout(data.LayoutUniqueId))
            return LayoutTitledMatrix(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (IsCycle2Layout(data.LayoutUniqueId))
            return LayoutCycle2(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan, theme);

        if (data.Family == SmartArtFamily.Hierarchy && IsHierarchy3Layout(data.LayoutUniqueId))
            return LayoutHierarchy3(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsOrgChartLayout(data.LayoutUniqueId))
            return LayoutOrgChart(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsHorizontalHierarchyLayout(data.LayoutUniqueId))
            return LayoutHorizontalHierarchy(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsTableHierarchyLayout(data.LayoutUniqueId))
            return LayoutTableHierarchy(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

        if (data.Family == SmartArtFamily.Hierarchy && IsLabeledHierarchyLayout(data.LayoutUniqueId))
            return LayoutLabeledHierarchy(data, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

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
                : IsOpposingIdeasLayout(data.LayoutUniqueId)
                    ? LayoutOpposingIdeas(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsConvergingRadialLayout(data.LayoutUniqueId)
                    ? LayoutConvergingRadial(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsDivergingRadialLayout(data.LayoutUniqueId)
                    ? LayoutDivergingRadial(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                : IsRadialVennLayout(data.LayoutUniqueId)
                    ? LayoutRadialVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                    : IsTargetListLayout(data.LayoutUniqueId)
                        ? LayoutTargetList(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                        : IsStackedVennLayout(data.LayoutUniqueId)
                            ? LayoutStackedVenn(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
                            : IsInterlockingRingsLayout(data.LayoutUniqueId)
                                ? LayoutInterlockingRings(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan)
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

    private static List<SmartArtNode> FlattenVisibleHierarchyNodes(SmartArtData data)
    {
        var all = new List<SmartArtNode>();
        foreach (var root in data.Nodes)
            CollectPreOrder(root, all);

        return all
            .Where(node => !string.IsNullOrWhiteSpace(node.Text))
            .ToList();
    }

    // ── Color palette ──────────────────────────────────────────────────────────────────────────

    // ── Shape builder helpers ──────────────────────────────────────────────────────────────────

    private static SlideShape MakeBox(
        uint id, string text, SmartArtNodeStyle style,
        long x, long y, long cx, long cy,
        double fontSizePt = NodeFontSizePt,
        DrawingShapeKind shapeKind = DrawingShapeKind.RoundedRectangle,
        double? geometryAdjustment = null)
    {
        var body = new TextBody();
        foreach (var line in NormalizeSmartArtText(text).Split('\n'))
        {
            var paragraph = new Paragraph { Align = TextAlign.Center };
            paragraph.Runs.Add(new Run
            {
                Text = line,
                Color = style.Text,
                Bold = true,
                FontSizePt = fontSizePt,
            });
            body.Paragraphs.Add(paragraph);
        }
        body.Anchor = VerticalAnchor.Middle;
        body.Wrap   = true;

        var shape = new SlideShape
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

        if (geometryAdjustment is double adjustment)
            shape.PresetGeometryAdjustments["adj"] = adjustment;

        return shape;
    }

    private static string NormalizeSmartArtText(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static SlideShape MakeBulletListBox(
        uint id, SmartArtNode node, SmartArtNodeStyle style,
        long x, long y, long cx, long cy)
    {
        var shape = MakeBox(id, node.Text, style, x, y, cx, cy, NodeFontSizePt, DrawingShapeKind.Rectangle);
        var paragraph = shape.TextBody!.Paragraphs[0];
        paragraph.Align = TextAlign.Left;
        paragraph.BulletKind = BulletKind.Char;
        paragraph.BulletChar = "•";
        paragraph.BulletColor = style.Text;
        paragraph.BulletSizePt = NodeFontSizePt;
        paragraph.MarginLeftEmu = EmuPerDip * (16 + Math.Max(node.Level, 0) * 18);
        paragraph.IndentEmu = -EmuPerDip * 10;
        return shape;
    }

    private static SlideShape MakeOrgChartBox(
        uint id, string text, SmartArtNodeStyle style,
        long x, long y, long cx, long cy,
        bool isAssistant,
        double fontSizePt = NodeFontSizePt)
    {
        var body = new TextBody
        {
            Anchor = VerticalAnchor.Middle,
            Wrap = true,
            InsetTopPt = 3,
            InsetBottomPt = 3,
            InsetLeftPt = 5,
            InsetRightPt = 5,
        };
        foreach (var line in NormalizeSmartArtText(text).Split('\n'))
        {
            var paragraph = new Paragraph { Align = TextAlign.Center };
            paragraph.Runs.Add(new Run
            {
                Text = line,
                Color = style.Text,
                Bold = true,
                FontSizePt = fontSizePt,
            });
            body.Paragraphs.Add(paragraph);
        }

        return new SlideShape
        {
            Id = id,
            Name = $"SmartArt_OrgChartBox_{id}",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = isAssistant ? DrawingShapeKind.Rectangle : DrawingShapeKind.RoundedRectangle,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Fill = new ShapeFill.Solid(style.Fill),
            Outline = new ShapeOutline.Visible(style.Outline, style.OutlineWidthPt),
            TextBody = body,
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

    /// <summary>
    /// Inverted Pyramid geometry keeps the authored layout identity distinct
    /// from Pyramid List while producing editable descending bands.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutInvertedPyramid(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var shapes = new List<SlideShape>();
        var outerPadX = (long)(fcx * OuterPaddingFrac);
        var outerPadY = (long)(fcy * OuterPaddingFrac);
        var gapY = Math.Max((long)(fcy * 0.01), 1L);
        var innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        var availableH = Math.Max(fcy - 2 * outerPadY - Math.Max(nodes.Count - 1, 0) * gapY, 1L);
        var bandH = Math.Max(availableH / Math.Max(nodes.Count, 1), 1L);
        var minimumWidthFraction = nodes.Count == 1 ? 1.0 : 0.30;
        var currentY = fy + outerPadY;

        for (var index = 0; index < nodes.Count; index++)
        {
            var progress = nodes.Count == 1 ? 1.0 : (double)index / (nodes.Count - 1);
            var widthFraction = 1.0 - ((1.0 - minimumWidthFraction) * progress);
            var bandW = Math.Max((long)(innerW * widthFraction), 1L);
            var x = fx + outerPadX + (innerW - bandW) / 2;
            var nodeStyle = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            var kind = index == nodes.Count - 1 ? DrawingShapeKind.Triangle : DrawingShapeKind.Trapezoid;
            shapes.Add(MakeBox(
                (uint)(540 + index), nodes[index].Text, nodeStyle,
                x, currentY, bandW, bandH, NodeFontSizePt, kind));
            currentY += bandH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Bending Process uses a two-track zig-zag: each stage advances horizontally while
    /// alternating between the upper and lower track.  The diagonal connectors preserve
    /// the authored sequence without collapsing the preset into the generic single-row
    /// process layout.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutBendingProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 1 || fcx <= 0 || fcy <= 0)
            return null;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * 0.10), 1L);
        long rawGap = Math.Max((long)(fcx * GapFrac), 1L);
        long rawConnectorW = Math.Max((long)(fcx * 0.03), 1L);
        long availableWidth = fcx - 2 * padX;
        long overhead = (nodes.Count - 1) * (rawGap + rawConnectorW);
        double scale = overhead > 0 && overhead > availableWidth / 2
            ? (double)(availableWidth / 2) / overhead
            : 1.0;
        long gap = Math.Max((long)(rawGap * scale), 1L);
        long connectorW = Math.Max((long)(rawConnectorW * scale), 1L);
        long boxW = Math.Max((availableWidth - (nodes.Count - 1) * (gap + connectorW)) / nodes.Count, 1L);
        long boxH = Math.Max((long)(fcy * 0.28), 1L);
        long upperY = fy + padY;
        long lowerY = fy + fcy - padY - boxH;
        if (boxW <= 0 || boxH <= 0 || lowerY <= upperY)
            return null;

        var shapes = new List<SlideShape>(nodes.Count * 2);
        var centers = new (long x, long y)[nodes.Count];
        uint idCounter = 140;
        long x = fx + padX;

        for (var i = 0; i < nodes.Count; i++)
        {
            long y = i % 2 == 0 ? upperY : lowerY;
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, nodeStyle, x, y, boxW, boxH));
            centers[i] = (x + boxW / 2, y + boxH / 2);
            x += boxW + gap + connectorW;
        }

        for (var i = 0; i < centers.Length - 1; i++)
        {
            var from = centers[i];
            var to = centers[i + 1];
            shapes.Add(MakeConnector(
                idCounter++,
                from.x + boxW / 2,
                from.y,
                to.x - boxW / 2,
                to.y,
                stylePlan.Connector));
        }

        return shapes;
    }

    /// <summary>
    /// Bounded live plan for the three chevron process variants. Each stage is a real
    /// Chevron preset so the shared compositor, WPF host, and Avalonia host all consume
    /// the same polygon geometry. The stage step is intentionally shorter than the stage
    /// width to reproduce the authored overlap between adjacent chevrons.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutChevronProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        // Negative offsets are valid for objects that intentionally extend beyond the slide;
        // only the frame extents and the generated geometry are bounded here.
        if (nodes.Count < 1 || fcx <= 0 || fcy <= 0)
            return null;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * 0.12), 1L);
        long height = fcy - (2 * padY);
        long availableWidth = fcx - (2 * padX);
        if (height <= 0 || availableWidth <= 0)
            return null;

        // ShapeGeometryBuilder's Chevron preset uses a 24% notch. Advancing by the
        // remaining 76% makes the next notch receive the preceding tip, which gives
        // every admitted variant the same evidence-backed interlocking geometry.
        const double ChevronOverlap = 0.24;
        double overlap = ChevronOverlap;
        double denominator = nodes.Count - ((nodes.Count - 1) * overlap);
        long stageWidth = (long)(availableWidth / denominator);
        long step = (long)(stageWidth * (1.0 - overlap));
        if (stageWidth <= 0 || step <= 0 || stageWidth < 6_000L || step < 4_000L)
            return null;

        var shapes = new List<SlideShape>(nodes.Count);
        uint idCounter = 100;
        long x = fx + padX;
        // DrawingML guide values use the shared 0..100000 scale. 24000 matches the
        // normalized 24% notch used by ShapeGeometryBuilder when no adjustment exists.
        const double ChevronDepth = 24000.0;

        for (var i = 0; i < nodes.Count; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Process);
            shapes.Add(MakeBox(
                idCounter++, nodes[i].Text, nodeStyle, x, fy + padY, stageWidth, height,
                NodeFontSizePt, DrawingShapeKind.Chevron, ChevronDepth));
            x += step;
        }

        return shapes;
    }

    /// <summary>
    /// Titled matrix: a full-width title band followed by a two-column body.
    /// The first node is semantic title content rather than a quadrant. Body rows are
    /// derived from the parsed node count so larger authored matrices remain editable.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutTitledMatrix(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count == 0 || string.IsNullOrWhiteSpace(nodes[0].Text))
            return null;

        var bodyNodes = nodes.Skip(1).ToList();
        int columns = bodyNodes.Count <= 1 ? 1 : 2;
        int rows = (bodyNodes.Count + columns - 1) / columns;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapX = columns > 1 ? (long)(fcx * GapFrac) : 0;
        long gapY = (long)(fcy * GapFrac);
        long titleH = Math.Max((long)(fcy * 0.18), 1L);
        long titleGap = Math.Max((long)(fcy * 0.035), 1L);
        long bodyY = fy + outerPadY + titleH + titleGap;
        long bodyH = fcy - 2 * outerPadY - titleH - titleGap;
        if (bodyH <= 0)
            return null;

        long bodyW = fcx - 2 * outerPadX;
        long boxW = Math.Max((bodyW - (columns - 1) * gapX) / columns, 1L);
        long boxH = rows > 0
            ? Math.Max((bodyH - (rows - 1) * gapY) / rows, 1L)
            : 1L;

        var shapes = new List<SlideShape>(bodyNodes.Count + 1)
        {
            MakeBox(
                520,
                nodes[0].Text,
                stylePlan.GetNodeStyle(0, nodes[0].Level, SmartArtFamily.Matrix),
                fx + outerPadX,
                fy + outerPadY,
                bodyW,
                titleH,
                NodeFontSizeLargePt,
                DrawingShapeKind.Rectangle)
        };

        uint idCounter = 521;
        for (int i = 0; i < bodyNodes.Count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            shapes.Add(MakeBox(
                idCounter++,
                bodyNodes[i].Text,
                stylePlan.GetNodeStyle(i + 1, bodyNodes[i].Level, SmartArtFamily.Matrix),
                fx + outerPadX + column * (boxW + gapX),
                bodyY + row * (boxH + gapY),
                boxW,
                boxH,
                NodeFontSizePt,
                DrawingShapeKind.Rectangle));
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

    // Phased Process keeps the shared process-family authoring route live while using
    // the existing two-track geometry until a native PowerPoint layout baseline is added.
    private static IReadOnlyList<SlideShape> LayoutPhasedProcess(
        List<SmartArtNode> nodes,
        long frameXEmu, long frameYEmu, long frameCxEmu, long frameCyEmu,
        SmartArtStylePlan stylePlan) =>
        LayoutAlternatingProcess(nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, stylePlan);

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
    /// Circle Arrow Process keeps its native layout identity for authoring,
    /// save/reopen, and cache regeneration. The current line-shape model cannot
    /// express PowerPoint's curved arrowheads, so it reuses live circular stage
    /// geometry until that connector primitive exists.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutCircleArrowProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan) =>
        LayoutCircleProcess(nodes, fx, fy, fcx, fcy, stylePlan);

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

    /// <summary>
    /// Vertical Block List geometry: a flat ordered stack of independently editable
    /// rectangular blocks. Preserve authored hierarchy levels as bounded left insets;
    /// unlike the generic list path, this layout intentionally uses block rectangles
    /// and does not imply a rounded-card or connector treatment.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutVerticalBlockList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var visibleNodes = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Text))
            .ToList();
        if (visibleNodes.Count == 0)
            return [];

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = Math.Max((long)(fcy * GapFrac * 0.8), 1L);
        long levelStep = Math.Max((long)(fcx * 0.035), 1L);
        long availableH = Math.Max(
            fcy - 2 * outerPadY - (visibleNodes.Count - 1) * gapY,
            1L);
        long boxH = Math.Max(availableH / visibleNodes.Count, 1L);
        var shapes = new List<SlideShape>(visibleNodes.Count);
        long currentY = fy + outerPadY;

        for (int i = 0; i < visibleNodes.Count; i++)
        {
            var node = visibleNodes[i];
            long indent = Math.Min(Math.Max(node.Level, 0), 4) * levelStep;
            long x = fx + outerPadX + indent;
            long width = Math.Max(fcx - 2 * outerPadX - indent, 1L);
            var style = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            shapes.Add(MakeBox(
                (uint)(240 + i), node.Text, style,
                x, currentY, width, boxH,
                NodeFontSizePt, DrawingShapeKind.Rectangle));
            currentY += boxH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Vertical Arrow List geometry: editable down-arrow stages stack from top
    /// to bottom and use the shared list style plan. The arrow bodies carry the
    /// progression cue, so no separate connector shapes are emitted.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutVerticalArrowList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>(n);
        if (n == 0)
            return shapes;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = Math.Max((long)(fcy * GapFrac * 0.65), 1L);
        long boxW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availableH = Math.Max(fcy - 2 * outerPadY - (n - 1) * gapY, 1L);
        long boxH = Math.Max(availableH / n, 1L);
        long curY = fy + outerPadY;

        for (int i = 0; i < n; i++)
        {
            var style = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(
                (uint)(760 + i), nodes[i].Text, style,
                fx + outerPadX, curY, boxW, boxH,
                NodeFontSizePt, DrawingShapeKind.DownArrow));
            curY += boxH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Vertical Bullet List geometry: flatten the authored hierarchy into an ordered
    /// stack of independently editable bullet paragraphs. PowerPoint treats this
    /// layout as a list, not an org-chart tree, so no parent-child connectors are emitted.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutVerticalBulletList(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var nodes = FlattenVisibleHierarchyNodes(data);
        var shapes = new List<SlideShape>(nodes.Count);
        if (nodes.Count == 0)
            return shapes;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = Math.Max((long)(fcy * GapFrac), 1L);
        long boxW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availableH = Math.Max(fcy - 2 * outerPadY - (nodes.Count - 1) * gapY, 1L);
        long boxH = Math.Max(availableH / nodes.Count, 1L);
        long curY = fy + outerPadY;

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var style = stylePlan.GetNodeStyle(index, node.Level, SmartArtFamily.List);
            shapes.Add(MakeBulletListBox(
                (uint)(290 + index), node, style,
                fx + outerPadX, curY, boxW, boxH));
            curY += boxH + gapY;
        }

        return shapes;
    }

    /// <summary>
    /// Horizontal Bullet List geometry: lays the visible bullets into a compact
    /// row-major grid instead of falling back to the cached diagram drawing.
    /// This keeps the layout deterministic for authoring and save/reopen while
    /// retaining the normal list-family node styling.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutHorizontalBulletList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int count = nodes.Count;
        int columns = Math.Min(Math.Max(count, 1), 2);
        int rows = (count + columns - 1) / columns;
        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapX = Math.Max((long)(fcx * GapFrac), 1L);
        long gapY = Math.Max((long)(fcy * GapFrac), 1L);
        long innerWidth = Math.Max(fcx - 2 * outerPadX - (columns - 1) * gapX, 1L);
        long innerHeight = Math.Max(fcy - 2 * outerPadY - (rows - 1) * gapY, 1L);
        long boxWidth = Math.Max(innerWidth / columns, 1L);
        long boxHeight = Math.Max(innerHeight / rows, 1L);
        long startX = fx + outerPadX;
        long startY = fy + outerPadY;
        var shapes = new List<SlideShape>(count);

        for (int index = 0; index < count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            var nodeStyle = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(
                (uint)(260 + index),
                nodes[index].Text,
                nodeStyle,
                startX + column * (boxWidth + gapX),
                startY + row * (boxHeight + gapY),
                boxWidth,
                boxHeight));
        }

        return shapes;
    }

    /// <summary>
    /// Horizontal Block List geometry: one editable block per authored node in a
    /// left-to-right row. Unlike the bullet-list route, each block owns the full
    /// node surface and no synthetic bullet/grid treatment is introduced.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutHorizontalBlockList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var count = nodes.Count;
        var padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        var padY = Math.Max((long)(fcy * OuterPaddingFrac), 1L);
        var gap = Math.Max((long)(fcx * GapFrac), 1L);
        var innerWidth = Math.Max(fcx - (2 * padX) - ((count - 1) * gap), 1L);
        var blockWidth = Math.Max(innerWidth / Math.Max(count, 1), 1L);
        var blockHeight = Math.Max(fcy - (2 * padY), 1L);
        var shapes = new List<SlideShape>(count);

        for (var index = 0; index < count; index++)
        {
            var style = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(
                (uint)(275 + index),
                nodes[index].Text,
                style,
                fx + padX + index * (blockWidth + gap),
                fy + padY,
                blockWidth,
                blockHeight,
                NodeFontSizePt,
                DrawingShapeKind.Rectangle));
        }

        return shapes;
    }

    /// <summary>
    /// Vertical Chevron List geometry. PowerPoint uses this list layout to emphasize
    /// progression while giving each node a distinct chevron body. The shared route
    /// keeps one ordered chevron per node and uses the same bounds/style contract in
    /// WPF and Avalonia; native cached drawing remains available for malformed input.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutVerticalChevronList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 1 || fcx <= 0 || fcy <= 0)
            return null;
        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * 0.08), 1L);
        long gap = Math.Max((long)(fcy * GapFrac), 1L);
        long innerWidth = Math.Max(fcx - 2 * padX, 1L);
        long innerHeight = Math.Max(fcy - 2 * padY - (nodes.Count - 1) * gap, 1L);
        long boxHeight = Math.Max(innerHeight / nodes.Count, 1L);
        if (boxHeight < 6_000L)
            return null;

        var shapes = new List<SlideShape>(nodes.Count);
        long y = fy + padY;
        for (var index = 0; index < nodes.Count; index++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            shapes.Add(MakeBox(
                (uint)(280 + index), nodes[index].Text, nodeStyle,
                fx + padX, y, innerWidth, boxHeight,
                NodeFontSizePt, DrawingShapeKind.Chevron, 24000));
            y += boxHeight + gap;
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
            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            shapes.Add(node.Picture is { Bytes.Length: > 0 }
                ? new SlideShape
                {
                    Id = idCounter++,
                    Name = $"SmartArt_Picture_{idCounter}",
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = leftX,
                    OffsetYEmu = curY,
                    ExtentCxEmu = pictureW,
                    ExtentCyEmu = rowH,
                    Picture = node.Picture
                }
                : MakePicturePlaceholder(
                    idCounter++,
                    leftX,
                    curY,
                    pictureW,
                    rowH,
                    nodeStyle));

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

    private static IReadOnlyList<SlideShape>? LayoutPictureAccentList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>(n * 3);
        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long gapY = Math.Max((long)(fcy * 0.025), 1L);
        long gapX = Math.Max((long)(fcx * 0.018), 1L);
        long rowW = Math.Max(fcx - 2 * outerPadX, 1L);
        long availH = Math.Max(fcy - 2 * outerPadY - Math.Max(n - 1, 0) * gapY, 1L);
        long rowH = n > 0 ? Math.Max(availH / n, 1L) : 1L;
        long pictureW = Math.Max(Math.Min(rowH, (long)(rowW * 0.27)), 1L);
        long accentW = Math.Max((long)(fcx * 0.018), 1L);
        long captionX = fx + outerPadX + pictureW + gapX + accentW + gapX;
        long captionW = Math.Max(fx + fcx - outerPadX - captionX, 1L);
        long curY = fy + outerPadY;
        long leftX = fx + outerPadX;
        uint idCounter = 360;

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            shapes.Add(node.Picture is { Bytes.Length: > 0 }
                ? new SlideShape
                {
                    Id = idCounter++,
                    Name = $"SmartArt_AccentPicture_{i + 1}",
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = leftX,
                    OffsetYEmu = curY,
                    ExtentCxEmu = pictureW,
                    ExtentCyEmu = rowH,
                    Picture = node.Picture,
                }
                : MakePicturePlaceholder(idCounter++, leftX, curY, pictureW, rowH, nodeStyle));

            shapes.Add(new SlideShape
            {
                Id = idCounter++,
                Name = $"SmartArt_AccentBar_{i + 1}",
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu = leftX + pictureW + gapX,
                OffsetYEmu = curY,
                ExtentCxEmu = accentW,
                ExtentCyEmu = rowH,
                Fill = new ShapeFill.Solid(nodeStyle.Fill),
                Outline = ShapeOutline.None.Instance,
            });

            shapes.Add(MakeCaption(idCounter++, node.Text, nodeStyle, captionX, curY, captionW, rowH));
            curY += rowH + gapY;
        }

        return shapes;
    }

    private static IReadOnlyList<SlideShape>? LayoutPictureGrid(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
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
            var nodeStyle = stylePlan.GetNodeStyle(index, nodes[index].Level, SmartArtFamily.List);
            shapes.Add(nodes[index].Picture is { Bytes.Length: > 0 }
                ? new SlideShape
                {
                    Id = idCounter++,
                    Name = $"SmartArt_GridPicture_{index + 1}",
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = x,
                    OffsetYEmu = y,
                    ExtentCxEmu = cellW,
                    ExtentCyEmu = pictureH,
                    Picture = nodes[index].Picture,
                }
                : MakePicturePlaceholder(
                    idCounter++,
                    x,
                    y,
                    cellW,
                    pictureH,
                    nodeStyle));

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

    private static IReadOnlyList<SlideShape>? LayoutPictureLineup(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>(n * 2);
        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long gapX = Math.Max((long)(fcx * 0.025), 1L);
        long cellW = n > 0
            ? Math.Max((fcx - 2 * padX - Math.Max(n - 1, 0) * gapX) / n, 1L)
            : 1L;
        long pictureH = Math.Max((long)(fcy * 0.58), 1L);
        long captionH = Math.Max(fcy - 2 * padY - pictureH, 1L);
        long pictureY = fy + padY;
        long captionY = pictureY + pictureH;
        uint idCounter = 460;

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            long x = fx + padX + i * (cellW + gapX);
            shapes.Add(node.Picture is { Bytes.Length: > 0 }
                ? new SlideShape
                {
                    Id = idCounter++,
                    Name = $"SmartArt_LineupPicture_{i + 1}",
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = x,
                    OffsetYEmu = pictureY,
                    ExtentCxEmu = cellW,
                    ExtentCyEmu = pictureH,
                    Picture = node.Picture,
                }
                : MakePicturePlaceholder(idCounter++, x, pictureY, cellW, pictureH, nodeStyle));

            shapes.Add(MakeCaption(idCounter++, node.Text, nodeStyle, x, captionY, cellW, captionH));
        }

        return shapes;
    }

    private static IReadOnlyList<SlideShape>? LayoutPictureStack(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>(n * 2);
        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long gapX = Math.Max((long)(fcx * 0.035), 1L);
        long pictureW = Math.Max((long)(fcx * 0.34), 1L);
        long pictureH = Math.Max((long)(fcy * 0.26), 1L);
        long stepY = Math.Max((long)(pictureH * 0.32), 1L);
        long stackH = n == 0 ? pictureH : pictureH + Math.Max(n - 1, 0) * stepY;
        long stackY = fy + Math.Max((fcy - stackH) / 2, padY);
        long captionX = fx + padX + pictureW + gapX;
        long captionW = Math.Max(fx + fcx - padX - captionX, 1L);
        uint idCounter = 410;

        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.List);
            long pictureY = stackY + i * stepY;
            shapes.Add(node.Picture is { Bytes.Length: > 0 }
                ? new SlideShape
                {
                    Id = idCounter++,
                    Name = $"SmartArt_StackPicture_{i + 1}",
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = fx + padX,
                    OffsetYEmu = pictureY,
                    ExtentCxEmu = pictureW,
                    ExtentCyEmu = pictureH,
                    Picture = node.Picture,
                }
                : MakePicturePlaceholder(idCounter++, fx + padX, pictureY, pictureW, pictureH, nodeStyle));

            shapes.Add(MakeCaption(
                idCounter++,
                node.Text,
                nodeStyle,
                captionX,
                pictureY,
                captionW,
                pictureH));
        }

        return shapes;
    }

    private static SlideShape MakePicturePlaceholder(
        uint id,
        long x,
        long y,
        long width,
        long height,
        SmartArtNodeStyle style)
    {
        var placeholderStyle = new SmartArtNodeStyle(
            new ThemeAwareColor(SrgbColor.FromRgb(0xE7E6E6)),
            style.Outline,
            new ThemeAwareColor(SrgbColor.FromRgb(0x666666)),
            style.OutlineWidthPt);
        var placeholder = MakeBox(
            id,
            "Add picture",
            placeholderStyle,
            x,
            y,
            width,
            height,
            10,
            DrawingShapeKind.Rectangle);
        placeholder.Name = $"SmartArt_PicturePlaceholder_{id}";
        return placeholder;
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
    private static IReadOnlyList<SlideShape>? LayoutOpposingIdeas(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 2)
            return null;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        long arrowW = Math.Max((long)(innerW * 0.38), 1L);
        long leftX = fx + outerPadX;
        long rightX = fx + fcx - outerPadX - arrowW;

        int leftCount = (nodes.Count + 1) / 2;
        int rightCount = nodes.Count - leftCount;
        int maxRows = Math.Max(leftCount, rightCount);
        long rowGap = Math.Max(
            (long)(innerH * 0.08 * Math.Min(1.0, 2.0 / maxRows)),
            1L);
        long arrowH = Math.Max(
            Math.Min(
                (long)(innerH * 0.28),
                (innerH - rowGap * Math.Max(maxRows - 1, 0)) / maxRows),
            1L);
        long totalH = arrowH * maxRows + rowGap * Math.Max(maxRows - 1, 0);
        long firstY = fy + outerPadY + Math.Max((innerH - totalH) / 2, 0L);
        var shapes = new List<SlideShape>(nodes.Count);
        uint idCounter = 535;
        for (int i = 0; i < nodes.Count; i++)
        {
            bool left = i < leftCount;
            int row = left ? i : i - leftCount;
            int count = left ? leftCount : rightCount;
            long y = firstY + row * (arrowH + rowGap);
            if (count == 1)
                y = fy + outerPadY + Math.Max((innerH - arrowH) / 2, 0L);
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            shapes.Add(MakeBox(
                idCounter++, nodes[i].Text, nodeStyle,
                left ? leftX : rightX, y, arrowW, arrowH,
                NodeFontSizePt,
                left ? DrawingShapeKind.RightArrow : DrawingShapeKind.LeftArrow));
        }

        return shapes;
    }

    /// <summary>
    /// Converging Radial geometry: three or four authored inward-facing arrows
    /// retain their established compass arrangement. Larger authored lists use
    /// a bounded radial ring with cardinal arrow presets so both hosts can keep
    /// the diagram live and regenerate the same editable shapes.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutConvergingRadial(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 3)
            return null;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        long arrowW = Math.Max((long)(innerW * 0.24), 1L);
        long arrowH = Math.Max((long)(innerH * 0.22), 1L);
        long centerX = fx + outerPadX + innerW / 2;
        long centerY = fy + outerPadY + innerH / 2;
        long horizontalOffset = Math.Max((long)(innerW * 0.31), arrowW / 2);
        long verticalOffset = Math.Max((long)(innerH * 0.31), arrowH / 2);

        var positions = new List<(long x, long y, DrawingShapeKind shapeKind)>(nodes.Count);
        if (nodes.Count == 3)
        {
            positions.Add((centerX - arrowW / 2, centerY - verticalOffset - arrowH / 2, DrawingShapeKind.DownArrow));
            positions.Add((centerX - horizontalOffset - arrowW / 2, centerY - arrowH / 2, DrawingShapeKind.RightArrow));
            positions.Add((centerX + horizontalOffset - arrowW / 2, centerY - arrowH / 2, DrawingShapeKind.LeftArrow));
        }
        else if (nodes.Count == 4)
        {
            positions.Add((centerX - arrowW / 2, centerY - verticalOffset - arrowH / 2, DrawingShapeKind.DownArrow));
            positions.Add((centerX + horizontalOffset - arrowW / 2, centerY - arrowH / 2, DrawingShapeKind.LeftArrow));
            positions.Add((centerX - arrowW / 2, centerY + verticalOffset - arrowH / 2, DrawingShapeKind.UpArrow));
            positions.Add((centerX - horizontalOffset - arrowW / 2, centerY - arrowH / 2, DrawingShapeKind.RightArrow));
        }
        else
        {
            // Reduce the arrow footprint as the ring gets denser while keeping
            // a generous inset from the authored frame edges.
            var density = Math.Max((int)Math.Ceiling(Math.Sqrt(nodes.Count)), 1);
            arrowW = Math.Max(Math.Min(arrowW, innerW / (density + 2)), 1L);
            arrowH = Math.Max(Math.Min(arrowH, innerH / (density + 2)), 1L);
            var radiusX = Math.Max((long)(innerW * 0.32), arrowW / 2);
            var radiusY = Math.Max((long)(innerH * 0.32), arrowH / 2);

            for (var i = 0; i < nodes.Count; i++)
            {
                var angle = -Math.PI / 2 + (2 * Math.PI * i / nodes.Count);
                var x = centerX + (long)(Math.Cos(angle) * radiusX) - arrowW / 2;
                var y = centerY + (long)(Math.Sin(angle) * radiusY) - arrowH / 2;
                var shapeKind = Math.Abs(Math.Cos(angle)) > 0.45
                    ? (Math.Cos(angle) > 0 ? DrawingShapeKind.LeftArrow : DrawingShapeKind.RightArrow)
                    : (Math.Sin(angle) > 0 ? DrawingShapeKind.UpArrow : DrawingShapeKind.DownArrow);
                positions.Add((x, y, shapeKind));
            }
        }

        var shapes = new List<SlideShape>(nodes.Count);
        uint idCounter = 536;
        for (int i = 0; i < nodes.Count; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var (x, y, shapeKind) = positions[i];
            shapes.Add(MakeBox(
                idCounter++, nodes[i].Text, nodeStyle,
                x, y, arrowW, arrowH,
                NodeFontSizePt, shapeKind));
        }

        return shapes;
    }

    /// <summary>
    /// Diverging Radial geometry: the first logical node is the central idea and
    /// the remaining nodes radiate outward as equal circles. This keeps the
    /// native relationship layout live and editable in both hosts instead of
    /// falling back to the cached SmartArt drawing.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutDivergingRadial(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 2)
            return null;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * OuterPaddingFrac), 1L);
        long innerCx = Math.Max(fcx - 2 * padX, 1L);
        long innerCy = Math.Max(fcy - 2 * padY, 1L);
        double centerX = fx + padX + innerCx / 2.0;
        double centerY = fy + padY + innerCy / 2.0;

        long centerDiameter = Math.Max((long)(Math.Min(innerCx, innerCy) * 0.27), 1L);
        uint idCounter = 548;
        var shapes = new List<SlideShape>(1 + (nodes.Count - 1) * 2);
        var centerStyle = stylePlan.GetNodeStyle(0, nodes[0].Level, SmartArtFamily.Relationship);
        shapes.Add(MakeBox(
            idCounter++, nodes[0].Text, centerStyle,
            (long)(centerX - centerDiameter / 2.0),
            (long)(centerY - centerDiameter / 2.0),
            centerDiameter,
            centerDiameter,
            NodeFontSizeLargePt,
            DrawingShapeKind.Ellipse));

        int outerCount = nodes.Count - 1;
        double angleStep = 360.0 / outerCount;
        double radiusX = innerCx / 2.0 * 0.70;
        double radiusY = innerCy / 2.0 * 0.70;
        double halfChord = Math.Sin(Math.PI / outerCount);
        double outerDiameterFrac = outerCount == 1
            ? 0.20
            : Math.Min(0.20, halfChord * 0.62);
        long outerDiameter = Math.Max(
            (long)(Math.Min(innerCx, innerCy) * outerDiameterFrac),
            1L);
        var outerCenters = new (double x, double y)[outerCount];

        for (int i = 0; i < outerCount; i++)
        {
            double angle = (-90 + i * angleStep) * Math.PI / 180.0;
            outerCenters[i] = (
                centerX + radiusX * Math.Cos(angle),
                centerY + radiusY * Math.Sin(angle));
            shapes.Add(MakeConnector(
                idCounter++,
                (long)centerX,
                (long)centerY,
                (long)outerCenters[i].x,
                (long)outerCenters[i].y,
                stylePlan.Connector));
        }

        for (int i = 0; i < outerCount; i++)
        {
            var node = nodes[i + 1];
            var nodeStyle = stylePlan.GetNodeStyle(i + 1, node.Level, SmartArtFamily.Relationship);
            shapes.Add(MakeBox(
                idCounter++, node.Text,
                nodeStyle,
                (long)(outerCenters[i].x - outerDiameter / 2.0),
                (long)(outerCenters[i].y - outerDiameter / 2.0),
                outerDiameter,
                outerDiameter,
                NodeFontSizePt,
                DrawingShapeKind.Ellipse));
        }

        return shapes;
    }

    private static IReadOnlyList<SlideShape>? LayoutBasicRelationship(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        if (nodes.Count < 2)
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
        if (n == 0)
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
        if (n < 3)
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
    /// The shared plan keeps one live ellipse per parsed node; exact PowerPoint
    /// ring clipping, label offsets, and effects remain outside this renderer-
    /// neutral geometry contract.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutTargetList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n == 0)
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
        if (n < 2)
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
    /// Interlocking Rings geometry: translucent equal-sized ellipses overlap in a
    /// readable horizontal chain. The native relationship layout ID remains the
    /// source of truth while both hosts consume the same editable shape plan.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutInterlockingRings(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        int n = nodes.Count;
        if (n < 2)
            return null;

        long outerPadX = (long)(fcx * OuterPaddingFrac);
        long outerPadY = (long)(fcy * OuterPaddingFrac);
        long innerW = Math.Max(fcx - 2 * outerPadX, 1L);
        long innerH = Math.Max(fcy - 2 * outerPadY, 1L);
        const double overlapFrac = 0.46;
        long diameter = Math.Max(
            Math.Min((long)(innerW / (1.0 + overlapFrac * (n - 1))), (long)(innerH * 0.84)),
            1L);
        long step = Math.Max((long)(diameter * overlapFrac), 1L);
        long totalW = diameter + (n - 1) * step;
        long leftX = fx + outerPadX + Math.Max((innerW - totalW) / 2, 0L);
        long topY = fy + outerPadY + Math.Max((innerH - diameter) / 2, 0L);

        var shapes = new List<SlideShape>(n);
        uint idCounter = 660;
        for (int i = 0; i < n; i++)
        {
            var baseStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Relationship);
            var ringStyle = baseStyle with
            {
                Fill = new ThemeAwareColor(baseStyle.Fill.Resolved, alpha: 165)
            };
            shapes.Add(MakeBox(
                idCounter++,
                nodes[i].Text,
                ringStyle,
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

    /// <summary>
    /// Radial Cluster shares the authored central-idea/outer-node contract with
    /// Diverging Radial, while keeping its native layout identity distinct for
    /// Change Layout and package round-trip operations.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutRadialCluster(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
        => LayoutDivergingRadial(nodes, fx, fy, fcx, fcy, stylePlan);

    /// <summary>
    /// Radial List geometry: every list item radiates from the shared center while
    /// the center remains an implicit routing point. This keeps the list items equal
    /// and editable, unlike the generic cycle plan which links adjacent items into a
    /// closed loop.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutRadialList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var shapes = new List<SlideShape>();
        if (nodes.Count == 0) return null;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * OuterPaddingFrac), 1L);
        long innerCx = Math.Max(fcx - 2 * padX, 1L);
        long innerCy = Math.Max(fcy - 2 * padY, 1L);
        double centerX = fx + padX + innerCx / 2.0;
        double centerY = fy + padY + innerCy / 2.0;

        int itemCount = nodes.Count;
        double angleStep = 360.0 / itemCount;
        double radiusX = innerCx / 2.0 * 0.62;
        double radiusY = innerCy / 2.0 * 0.62;
        double halfChord = Math.Sin(Math.PI / itemCount);
        long boxW = Math.Max((long)(innerCx * Math.Min(0.28, halfChord * 0.82)), 1L);
        long boxH = Math.Max((long)(innerCy * Math.Min(0.24, halfChord * 0.82)), 1L);
        var centers = new (double x, double y)[itemCount];
        uint idCounter = 820;

        for (int i = 0; i < itemCount; i++)
        {
            double angle = (-90 + i * angleStep) * Math.PI / 180.0;
            centers[i] = (centerX + radiusX * Math.Cos(angle), centerY + radiusY * Math.Sin(angle));
            shapes.Add(MakeConnector(
                idCounter++,
                (long)centerX,
                (long)centerY,
                (long)centers[i].x,
                (long)centers[i].y,
                stylePlan.Connector));
        }

        for (int i = 0; i < itemCount; i++)
        {
            var node = nodes[i];
            var nodeStyle = stylePlan.GetNodeStyle(i, node.Level, SmartArtFamily.Cycle);
            shapes.Add(MakeBox(
                idCounter++,
                node.Text,
                nodeStyle,
                (long)(centers[i].x - boxW / 2.0),
                (long)(centers[i].y - boxH / 2.0),
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

    /// <summary>
    /// PowerPoint's cycle2 layout places two to seven equal ellipse nodes around an
    /// elliptical ring and inserts tangent right-arrow shapes between consecutive
    /// nodes. The native layout definition caps the child count at seven; larger or
    /// malformed diagrams return null and remain on the cached drawing path.
    /// </summary>
    private static IReadOnlyList<SlideShape>? LayoutCycle2(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan,
        PresentationTheme theme)
    {
        if (nodes.Count is < 2 or > 7)
            return null;

        long padX = Math.Max((long)(fcx * OuterPaddingFrac), 1L);
        long padY = Math.Max((long)(fcy * OuterPaddingFrac), 1L);
        long innerW = Math.Max(fcx - 2 * padX, 1L);
        long innerH = Math.Max(fcy - 2 * padY, 1L);
        double centerX = fx + padX + innerW / 2.0;
        double centerY = fy + padY + innerH / 2.0;
        double radiusX = innerW * 0.27;
        double radiusY = innerH * 0.34;
        long diameter = Math.Max(
            Math.Min((long)(innerW * 0.18), (long)(innerH * 0.28)),
            1L);
        double angleStep = 360.0 / nodes.Count;
        var centers = new (double X, double Y)[nodes.Count];
        var shapes = new List<SlideShape>(nodes.Count * 2);
        var arrowStyle = stylePlan.GetNodeStyle(0, nodes[0].Level, SmartArtFamily.Cycle) with
        {
            Fill = new ThemeAwareColor(SmartArtStylePlanner.ResolveNeutralConnector(theme))
        };
        uint id = 860;

        // Arrows are emitted first so their heads remain behind the ellipse nodes.
        for (int i = 0; i < nodes.Count; i++)
        {
            double angle = (-90 + i * angleStep) * Math.PI / 180.0;
            centers[i] = (centerX + radiusX * Math.Cos(angle), centerY + radiusY * Math.Sin(angle));
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var from = centers[i];
            var to = centers[(i + 1) % nodes.Count];
            double midX = (from.X + to.X) / 2.0;
            double midY = (from.Y + to.Y) / 2.0;
            double midAngle = (-90 + (i + 0.5) * angleStep) + 90.0;
            var arrow = MakeBox(
                id++, string.Empty, arrowStyle,
                (long)(midX - diameter * 0.135),
                (long)(midY - diameter * 0.17),
                Math.Max((long)(diameter * 0.27), 1L),
                Math.Max((long)(diameter * 0.34), 1L),
                NodeFontSizePt,
                DrawingShapeKind.RightArrow);
            arrow.Name = $"SmartArt_Cycle2_Arrow_{i}";
            arrow.TextBody = null;
            arrow.Outline = ShapeOutline.None.Instance;
            arrow.RotationDeg = midAngle;
            shapes.Add(arrow);
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var nodeStyle = stylePlan.GetNodeStyle(i, nodes[i].Level, SmartArtFamily.Cycle);
            var center = centers[i];
            shapes.Add(MakeBox(
                id++, nodes[i].Text, nodeStyle,
                (long)(center.X - diameter / 2.0),
                (long)(center.Y - diameter / 2.0),
                diameter,
                diameter,
                NodeFontSizePt,
                DrawingShapeKind.Ellipse));
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

    /// <summary>
    /// Organization Chart layout. It uses a dedicated assistant-aware tree plan and
    /// renderer-neutral ordinary shapes. Assistant nodes use rectangular boxes while
    /// regular organization nodes use rounded boxes.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutOrgChart(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan) =>
        LayoutHierarchy(
            data,
            fx,
            fy,
            fcx,
            fcy,
            stylePlan,
            useOrgChartAssistantLayout: true,
            useOrgChartBoxStyle: true);

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

    /// <summary>
    /// Labeled hierarchy layout: top-level branches become labeled section boxes on the
    /// left, while each branch's children are rendered as a horizontal hierarchy to the
    /// right. The label is a real shape in the live plan, so it remains editable and is
    /// connected to every first-level child rather than being flattened into the cache.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutLabeledHierarchy(
        SmartArtData data,
        long fx, long fy, long fcx, long fcy,
        SmartArtStylePlan stylePlan)
    {
        var roots = data.Nodes
            .Select(CloneVisibleHierarchyNode)
            .Where(node => node is not null)
            .Cast<SmartArtNode>()
            .ToList();
        if (roots.Count == 0)
            return [];

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);
        long availW = Math.Max(fcx - 2 * padX, 1L);
        long availH = Math.Max(fcy - 2 * padY, 1L);
        long gapX = Math.Max((long)(fcx * GapFrac), 1L);
        long gapY = Math.Max((long)(fcy * GapFrac), 1L);
        long labelW = Math.Max((long)(availW * 0.24), 1L);
        long contentW = Math.Max(availW - labelW - gapX, 1L);

        int leafRows = Math.Max(
            roots.Sum(root => root.Children.Count == 0
                ? 1
                : root.Children.Sum(GetTreeWidth)),
            1);
        int contentDepth = Math.Max(
            roots.SelectMany(root => root.Children)
                .Select(GetTreeDepth)
                .DefaultIfEmpty(1)
                .Max(),
            1);
        long boxH = Math.Max(
            (availH - Math.Max(leafRows - 1, 0) * gapY - Math.Max(roots.Count - 1, 0) * gapY)
                / leafRows,
            1L);
        long boxW = Math.Max(
            (contentW - Math.Max(contentDepth - 1, 0) * gapX) / contentDepth,
            1L);

        var shapes = new List<SlideShape>();
        uint idCounter = 560;
        long currentY = fy + padY;
        long contentX = fx + padX + labelW + gapX;

        foreach (var root in roots)
        {
            int rootRows = root.Children.Count == 0
                ? 1
                : Math.Max(root.Children.Sum(GetTreeWidth), 1);
            long sectionH = rootRows * boxH + Math.Max(rootRows - 1, 0) * gapY;
            var labelStyle = stylePlan.GetNodeStyle(0, root.Level, SmartArtFamily.Hierarchy);
            long labelY = currentY;
            long labelCenterY = labelY + sectionH / 2;

            shapes.Add(MakeBox(
                idCounter++, root.Text, labelStyle,
                fx + padX, labelY, labelW, sectionH,
                NodeFontSizeLargePt, DrawingShapeKind.Rectangle));

            if (root.Children.Count > 0)
            {
                long childY = currentY;
                foreach (var child in root.Children)
                {
                    int childRows = Math.Max(GetTreeWidth(child), 1);
                    RenderHorizontalNode(
                        child,
                        levelIndex: 0,
                        slotY: childY,
                        leafRows: childRows,
                        startX: contentX,
                        boxW,
                        boxH,
                        gapX,
                        gapY,
                        shapes,
                        stylePlan,
                        ref idCounter,
                        parentRightX: fx + padX + labelW,
                        parentCenterY: labelCenterY);
                    childY += childRows * boxH + Math.Max(childRows - 1, 0) * gapY + gapY;
                }
            }

            currentY += sectionH + gapY;
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
        bool useOrgChartAssistantLayout,
        bool useOrgChartBoxStyle = false)
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
                shapes, stylePlan, ref idCounter, useOrgChartAssistantLayout,
                useOrgChartBoxStyle, parentCenterX: -1, parentBottomY: -1);

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
        bool useOrgChartBoxStyle,
        long parentCenterX, long parentBottomY)
    {
        // BI1: The slot for this node is exactly availW (already pre-allocated by the caller).
        // Center the box within its slot, clamping boxW so it never exceeds the slot.
        long slotW = availW;
        long nodeBoxW = Math.Min(boxW, Math.Max(slotW - gapX, 1L));

        long boxX = startX + (slotW - nodeBoxW) / 2;
        long boxY = levelY;

        var nodeStyle = stylePlan.GetNodeStyle(0, node.Level, SmartArtFamily.Hierarchy);
        shapes.Add(useOrgChartBoxStyle
            ? MakeOrgChartBox(idCounter++, node.Text, nodeStyle, boxX, boxY, nodeBoxW, boxH,
                node.IsAssistant, node.Level == 0 ? NodeFontSizeLargePt : NodeFontSizePt)
            : MakeBox(idCounter++, node.Text, nodeStyle, boxX, boxY, nodeBoxW, boxH,
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
                        useOrgChartBoxStyle,
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
                        useOrgChartBoxStyle,
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
        return id.Split('/').Last() is "orgchart" or "nameandtitleorgchart";
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

    private static bool IsLabeledHierarchyLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "labeledhierarchy", StringComparison.Ordinal);
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

    private static bool IsPictureAccentListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "pictureaccentlist", StringComparison.Ordinal);
    }

    private static bool IsPictureStackLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "picturestack", StringComparison.Ordinal);
    }

    private static bool IsPictureLineupLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return id.Split('/').Last() is "picturelineup" or "picturestrips" or "continuouspicturelist";
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

    private static bool IsPhasedProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "phasedprocess", StringComparison.Ordinal);
    }

    private static bool IsBendingProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "bendingprocess", StringComparison.Ordinal);
    }

    private static bool IsBasicTimelineLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return id.Split('/').Last() is "basictimeline" or "circleaccenttimeline";
    }

    private static bool IsBasicRadialLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "radial1", StringComparison.Ordinal);
    }

    private static bool IsRadialClusterLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "radialcluster", StringComparison.Ordinal);
    }

    private static bool IsRadialListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "radiallist", StringComparison.Ordinal);
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

    private static bool IsCircleArrowProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "circlearrowprocess", StringComparison.Ordinal);
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

    private static bool IsVerticalChevronListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "verticalchevronlist", StringComparison.Ordinal);
    }

    private static bool IsVerticalArrowListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "verticalarrowlist", StringComparison.Ordinal);
    }

    private static bool IsVerticalBulletListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "verticalbulletlist", StringComparison.Ordinal);
    }

    private static bool IsVerticalBlockListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "verticalblocklist", StringComparison.Ordinal);
    }

    private static bool IsHorizontalBulletListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "horizontalbulletlist", StringComparison.Ordinal);
    }

    private static bool IsHorizontalBlockListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "horizontalblocklist", StringComparison.Ordinal);
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

    private static bool IsInvertedPyramidLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "invertedpyramid", StringComparison.Ordinal);
    }

    private static bool IsTitledMatrixLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "titledmatrix", StringComparison.Ordinal);
    }

    private static bool IsDescendingBlockListLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "descendingblocklist", StringComparison.Ordinal);
    }

    private static bool IsChevronProcessLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return id.Split('/').Last() is "chevronprocess" or "basicchevronprocess" or "closedchevronprocess";
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

    private static bool IsOpposingIdeasLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;
        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "opposingideas", StringComparison.Ordinal);
    }

    private static bool IsConvergingRadialLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;
        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "convergingradial", StringComparison.Ordinal);
    }

    private static bool IsDivergingRadialLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;
        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "divergingradial", StringComparison.Ordinal);
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

    private static bool IsInterlockingRingsLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "interlockingrings", StringComparison.Ordinal);
    }

    private static bool IsCycle2Layout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Equals(id.Split('/').Last(), "cycle2", StringComparison.Ordinal);
    }
}
