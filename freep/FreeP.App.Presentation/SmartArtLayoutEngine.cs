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
///
/// Returns null for <see cref="SmartArtFamily.Unknown"/> → compositor falls back to cached drawing.
///
/// Colors: node fills cycle through theme accent1–6.  Text is white on dark fills, black on light.
/// Connectors: simple line/straight-arrow shapes using <see cref="DrawingShapeKind.Line"/>.
/// </summary>
public static class SmartArtLayoutEngine
{
    // 1 inch = 914400 EMU
    private const long EmuPerDip = 9525L;

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
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(theme);

        if (data.Family == SmartArtFamily.Unknown) return null;

        // Flatten all visible nodes in display order
        var nodes = FlattenNodes(data);
        if (nodes.Count == 0)
        {
            // No nodes — return an empty list (compositor will emit nothing from live path)
            return Array.Empty<SlideShape>();
        }

        // Build accent color palette from the theme
        var palette = BuildPalette(theme, effectiveClrMap);

        return data.Family switch
        {
            SmartArtFamily.Process   => LayoutProcess  (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, palette),
            SmartArtFamily.List      => LayoutList      (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, palette),
            SmartArtFamily.Cycle     => LayoutCycle     (nodes, frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, palette),
            SmartArtFamily.Hierarchy => LayoutHierarchy (data,  frameXEmu, frameYEmu, frameCxEmu, frameCyEmu, palette),
            _                        => null
        };
    }

    // ── Node flattening ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens the tree to a display-order list.
    /// For Process/List/Cycle: returns top-level nodes only (level 0).
    /// For Hierarchy: returns all nodes recursively (the layout engine handles tree structure).
    /// </summary>
    private static List<SmartArtNode> FlattenNodes(SmartArtData data)
    {
        if (data.Family == SmartArtFamily.Hierarchy)
        {
            // Hierarchy needs the full tree — return nodes as-is (engine recurses)
            return data.Nodes.ToList();
        }

        // For other families: prefer top-level nodes; if empty fallback to all
        var topLevel = data.Nodes.ToList();
        if (topLevel.Count == 0)
        {
            // Flatten all nodes breadth-first
            var all = new List<SmartArtNode>();
            void Collect(SmartArtNode n) { all.Add(n); foreach (var c in n.Children) Collect(c); }
            foreach (var r in data.Nodes) Collect(r);
            return all;
        }
        return topLevel;
    }

    // ── Color palette ──────────────────────────────────────────────────────────────────────────

    private static SrgbColor[] BuildPalette(PresentationTheme theme, IReadOnlyDictionary<string, string>? clrMap)
    {
        var slots = new[]
        {
            ThemeColorSlot.Accent1, ThemeColorSlot.Accent2, ThemeColorSlot.Accent3,
            ThemeColorSlot.Accent4, ThemeColorSlot.Accent5, ThemeColorSlot.Accent6
        };
        return slots.Select(s =>
        {
            var raw = theme.ColorScheme[s];
            // Apply clrMap remapping if present (for dk/lt remapping)
            return raw;
        }).ToArray();
    }

    private static SrgbColor NodeFill(int index, SrgbColor[] palette) =>
        palette[index % palette.Length];

    /// <summary>Picks white text for dark fills, black for light.</summary>
    private static SrgbColor NodeTextColor(SrgbColor fill)
    {
        // Relative luminance (sRGB approximate)
        double lum = 0.2126 * fill.R / 255.0 + 0.7152 * fill.G / 255.0 + 0.0722 * fill.B / 255.0;
        return lum < 0.5 ? SrgbColor.White : SrgbColor.Black;
    }

    // ── Shape builder helpers ──────────────────────────────────────────────────────────────────

    private static SlideShape MakeBox(
        uint id, string text, SrgbColor fill, SrgbColor textColor,
        long x, long y, long cx, long cy,
        double fontSizePt = NodeFontSizePt)
    {
        var run = new Run { Text = text, Color = new ThemeAwareColor(textColor), Bold = true, FontSizePt = fontSizePt };
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
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
            Fill          = new ShapeFill.Solid(new ThemeAwareColor(fill)),
            Outline       = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.White), 1.0),
            TextBody      = body
        };
    }

    private static SlideShape MakeConnector(uint id, long x1, long y1, long x2, long y2)
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
            Outline       = new ShapeOutline.Visible(new ThemeAwareColor(new SrgbColor(0x70, 0x70, 0x70)), 1.5)
        };
    }

    // ── Process layout ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Horizontal row of boxes with arrow connectors between adjacent pairs.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutProcess(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SrgbColor[] palette)
    {
        int n = nodes.Count;
        var shapes = new List<SlideShape>();

        long outerPad = (long)(fcx * OuterPaddingFrac);
        long connectorW = (long)(fcx * 0.03);   // arrow connector width
        long gap = (long)(fcx * GapFrac);

        long availW = fcx - 2 * outerPad - (n - 1) * (gap + connectorW);
        long boxW   = n > 0 ? Math.Max(availW / n, 1L) : 1L;

        long outerPadY = (long)(fcy * 0.12);
        long boxH      = fcy - 2 * outerPadY;
        long topY      = fy + outerPadY;

        uint idCounter = 100;
        long curX = fx + outerPad;

        for (int i = 0; i < n; i++)
        {
            var fill = NodeFill(i, palette);
            var textClr = NodeTextColor(fill);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, fill, textClr, curX, topY, boxW, boxH));

            if (i < n - 1)
            {
                // Arrow connector from right edge of box to left edge of next box
                long connX = curX + boxW + gap / 2;
                long connY = topY + boxH / 2;
                shapes.Add(MakeConnector(idCounter++, connX, connY, connX + connectorW, connY));
            }

            curX += boxW + gap + connectorW;
        }

        return shapes;
    }

    // ── List layout ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vertical stack of boxes (no connectors — standard list layout).
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutList(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SrgbColor[] palette)
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
            var fill    = NodeFill(i, palette);
            var textClr = NodeTextColor(fill);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, fill, textClr, leftX, curY, boxW, boxH));
            curY += boxH + gapY;
        }

        return shapes;
    }

    // ── Cycle layout ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Boxes arranged on a circle with arrow connectors between adjacent boxes.
    /// </summary>
    private static IReadOnlyList<SlideShape> LayoutCycle(
        List<SmartArtNode> nodes,
        long fx, long fy, long fcx, long fcy,
        SrgbColor[] palette)
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

            var fill    = NodeFill(i, palette);
            var textClr = NodeTextColor(fill);
            shapes.Add(MakeBox(idCounter++, nodes[i].Text, fill, textClr, left, top, boxW, boxH));
        }

        // Arrow connectors: from edge of each box to edge of next box (clockwise)
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var (ax, ay) = centers[i];
            var (bx, by) = centers[j];

            // Midpoint offset toward center
            shapes.Add(MakeConnector(idCounter++, (long)ax, (long)ay, (long)bx, (long)by));
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
        SrgbColor[] palette)
    {
        var shapes = new List<SlideShape>();
        if (data.Nodes.Count == 0) return shapes;

        // Use the first root (most org-charts have a single root)
        var root = data.Nodes[0];

        long padX = (long)(fcx * OuterPaddingFrac);
        long padY = (long)(fcy * OuterPaddingFrac);

        long availW = fcx - 2 * padX;
        long availH = fcy - 2 * padY;

        // Measure the tree depth and max-width to determine box sizes
        int treeDepth    = GetTreeDepth(root);
        int treeMaxWidth = GetTreeWidth(root);

        treeDepth    = Math.Max(treeDepth, 1);
        treeMaxWidth = Math.Max(treeMaxWidth, 1);

        long gapY = (long)(fcy * GapFrac);
        long gapX = (long)(fcx * GapFrac);

        // Compute box height from depth
        long boxH = (availH - (treeDepth - 1) * gapY) / treeDepth;
        boxH = Math.Max(boxH, (long)(fcy * 0.10));

        // Box width is determined per-level from available width
        long boxW = (long)(availW / Math.Max(treeMaxWidth, 1) - gapX);
        boxW = Math.Max(boxW, (long)(fcx * 0.08));

        uint idCounter = 400;
        long startX    = fx + padX;
        long startY    = fy + padY;

        RenderNode(root, 0, 0, treeMaxWidth, startX, startY, availW, boxW, boxH, gapX, gapY,
            shapes, palette, ref idCounter, parentCenterX: -1, parentBottomY: -1);

        return shapes;
    }

    /// <summary>Recursively renders a hierarchy node and its children.</summary>
    private static void RenderNode(
        SmartArtNode node,
        int levelIndex, int siblingIndex, int levelWidth,
        long startX, long levelY, long availW,
        long boxW, long boxH, long gapX, long gapY,
        List<SlideShape> shapes,
        SrgbColor[] palette,
        ref uint idCounter,
        long parentCenterX, long parentBottomY)
    {
        // Compute subtree width so we can center this node within its column slot
        int subWidth = GetTreeWidth(node);
        subWidth = Math.Max(subWidth, 1);

        // Column slot assigned to this subtree
        long slotW    = availW / Math.Max(levelWidth, 1);
        long slotX    = startX + siblingIndex * slotW;

        long boxX = slotX + (slotW - boxW) / 2;
        long boxY = levelY;

        var fill    = NodeFill(node.Level, palette);
        var textClr = NodeTextColor(fill);
        shapes.Add(MakeBox(idCounter++, node.Text, fill, textClr, boxX, boxY, boxW, boxH,
            node.Level == 0 ? NodeFontSizeLargePt : NodeFontSizePt));

        long boxCenterX = boxX + boxW / 2;
        long boxTopY    = boxY;
        long boxBottomY = boxY + boxH;

        // Connector from parent bottom-center to this box top-center
        if (parentCenterX >= 0 && parentBottomY >= 0)
        {
            shapes.Add(MakeConnector(idCounter++, parentCenterX, parentBottomY, boxCenterX, boxTopY));
        }

        // Lay out children
        if (node.Children.Count > 0)
        {
            long childLevelY = boxBottomY + gapY;
            int nChildren    = node.Children.Count;

            // Distribute children evenly in the subtree's column slot
            for (int ci = 0; ci < nChildren; ci++)
            {
                var child = node.Children[ci];
                RenderNode(child,
                    node.Level + 1,
                    ci, nChildren,
                    slotX, childLevelY, slotW,
                    boxW, boxH, gapX, gapY,
                    shapes, palette, ref idCounter,
                    parentCenterX: boxCenterX,
                    parentBottomY: boxBottomY);
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
}
