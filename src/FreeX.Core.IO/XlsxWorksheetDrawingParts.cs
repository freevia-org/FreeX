using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal sealed record XlsxChartPackagePart(
    XDocument Xml,
    XDocument? Relationships,
    string? Name,
    XlsxDrawingAnchor? Anchor,
    // R63-io-drawing-chart-zorder: the chart's position among its sibling anchors in the drawing
    // part's document order (same mechanism as XlsxPicturePackagePart/XlsxTextBoxPackagePart/
    // XlsxShapePackagePart's DrawingOrderIndex -- see ReadNearestAnchorOrderIndex). Defaults to -1
    // (unknown/no anchor) so the one out-of-scope positional caller that never supplies it
    // (XlsxFileAdapter.SourcePackageSnapshot.cs's TryReadWorksheetChartParts fallback, which also
    // passes Anchor: null) keeps compiling unchanged.
    int DrawingOrderIndex = -1,
    // R80-app-accessibility-a11y-5-1: the chart's Alt Text title/description, read from the chart's
    // own <xdr:graphicFrame><xdr:nvGraphicFramePr><xdr:cNvPr title="..." descr="..."/> (NOT from
    // inside the <c:chart>/<cx:chart> element itself, which never carries a cNvPr) -- mirrors
    // XlsxPicturePackagePart/XlsxTextBoxPackagePart/XlsxShapePackagePart's Title/AltText fields.
    // Both default to null so the same out-of-scope positional caller referenced above (which never
    // supplies them either) keeps compiling unchanged.
    string? Title = null,
    string? AltText = null,
    // R98-io-chart-hyperlink-model-field: the chart graphicFrame's OWN object-level hyperlink (an
    // <a:hlinkClick> on its <xdr:nvGraphicFramePr><xdr:cNvPr>), resolved via the drawing part's own
    // relationships at load time -- see ReadObjectHyperlink/ReadRelationshipsWithTargetModeById, the
    // same mechanism XlsxPicturePackagePart/XlsxTextBoxPackagePart/XlsxShapePackagePart already use
    // (R97-model-drawing-hyperlink-2-2). Defaults to null so the out-of-scope positional caller
    // referenced above keeps compiling unchanged.
    DrawingObjectHyperlink? Hyperlink = null);

internal sealed record XlsxPicturePackagePart(
    byte[] ImageBytes,
    string ContentType,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom,
    int DrawingOrderIndex,
    // R65-io-image-drawing-6-1: non-null only for a "Link to File" picture -- its <a:blip> carries
    // r:link instead of r:embed, and there is no embedded image part in the package for it at all
    // (ImageBytes is empty and ContentType is ""). Carries the external relationship Target verbatim
    // so the writer can re-emit the same r:link + External relationship on save instead of the
    // picture silently vanishing (see XlsxWorksheetDrawingObjectWriter.AddPictureAnchor).
    string? LinkTarget = null,
    // R80-io-drawing-image-5-3: the raw bytes of the vector .svg media part referenced by this
    // picture's <a:blip><a:extLst><a:ext><asvg:svgBlip r:embed=".."/> extension (Excel's "Insert
    // Icons/SVG" pictures keep a PNG raster in ImageBytes/ContentType as the universal fallback AND
    // this vector original so the picture stays editable as a shape/recolorable in Excel's "Graphics
    // Format" tab). Null for every ordinary raster picture. See ReadPictureSvgBlipRelationshipId.
    byte[]? SvgImageBytes = null,
    // R90-app-accessibility-checker-5-2: true when this picture's <xdr:cNvPr><a:extLst> carries the
    // "Mark as decorative" extension (see XlsxWorksheetDrawingParts.ReadNonVisualDecorative). Must
    // round-trip on save (XlsxWorksheetDrawingObjectWriter) or a decorative picture would incorrectly
    // become a real Missing-Alt-Text finding (in FreeX and in real Excel) after opening and resaving.
    bool IsDecorative = false,
    // R97-model-drawing-hyperlink-2-2: the picture's object-level hyperlink (<a:hlinkClick> on its
    // cNvPr), resolved via the drawing part's own relationships. Populated for EVERY loaded picture
    // (not just ones that stay source-loaded) so DuplicateSheetDrawingCloner/PastePicturesCommand
    // have something to carry forward once IsSourceLoaded is cleared. Null = no hyperlink.
    DrawingObjectHyperlink? Hyperlink = null,
    // ── R119-io-camera-linked-picture-identity fields ──────────────────────────────────────────
    // Populated ONLY for a part built by ReadPictureSnapshotGroupParts (from an <xdr:grpSp> the
    // writer's fx:linkedPictureSnapshot extension marks as a reconstructed CellRangeSnapshot
    // picture -- see ToOneCellPictureSnapshotAnchor/ToPictureSnapshotGroupExtLst). Every ordinary
    // <xdr:pic>-backed part from ReadPictureParts keeps the default PictureKind.Image/null/false/0
    // values below, so those existing callers/constructions are unaffected.
    /// <summary>PictureKind.CellRangeSnapshot for a reconstructed camera/linked-picture group; PictureKind.Image (default) for every ordinary embedded picture.</summary>
    PictureKind Kind = PictureKind.Image,
    /// <summary>The group's per-cell text/style snapshot, parsed back from its child rectangle shapes. Null for an ordinary picture.</summary>
    IReadOnlyList<PictureCellSnapshot>? SnapshotCells = null,
    bool IsLinkedToSourceRange = false,
    string? LinkedSourceSheetName = null,
    int? LinkedSourceStartRow = null,
    int? LinkedSourceStartCol = null,
    int? LinkedSourceEndRow = null,
    int? LinkedSourceEndCol = null,
    uint SnapshotSourceRowCount = 0,
    uint SnapshotSourceColumnCount = 0);

internal sealed record XlsxTextBoxPackagePart(
    string Text,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical,
    bool HasFill,
    CellColor? FillColor,
    CellColor? OutlineColor,
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor,
    int DrawingOrderIndex,
    // ── txBody text-formatting fields (backlog textbox-6-2) ──────────────
    // Mirrors XlsxShapePackagePart's ShapeText* fields -- see ReadShapeTextFormatting.
    /// <summary>Font family from the first run's &lt;a:latin typeface="..."/&gt;; null = not authored.</summary>
    string? TextFontFamily = null,
    /// <summary>Font size in points from &lt;a:rPr sz&gt;; 0 = default.</summary>
    double TextFontSizePoints = 0,
    bool TextBold = false,
    bool TextItalic = false,
    CellColor? TextColor = null,
    WorkbookThemeColorReference? TextThemeColor = null,
    DrawingShapeTextHAlign TextHAlign = DrawingShapeTextHAlign.Left,
    DrawingShapeTextVAnchor TextVAnchor = DrawingShapeTextVAnchor.Top,
    // R91-commands-insert-object-5-1: true when &lt;a:ln&gt;&lt;a:noFill/&gt; is present -- explicitly
    // no border. Mirrors XlsxShapePackagePart.OutlineHasNoFill; without this an authored borderless
    // text box always regained a gray border on load (and permanently baked it in on re-save).
    bool OutlineHasNoFill = false,
    // R97-model-drawing-hyperlink-2-2: see the matching field on XlsxPicturePackagePart.
    DrawingObjectHyperlink? Hyperlink = null,
    // R149-app-accessibility-checker-decorative-shapes: true when this text box's <xdr:cNvPr>
    // <a:extLst> carries the "Mark as decorative" extension (see
    // XlsxWorksheetDrawingParts.ReadNonVisualDecorative) -- mirrors XlsxPicturePackagePart's
    // IsDecorative field.
    bool IsDecorative = false);

internal sealed record XlsxShapePackagePart(
    DrawingShapeKind Kind,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical,
    bool HasFill,
    CellColor? FillColor,
    CellColor? OutlineColor,
    CellColor? GradientFillEndColor,
    DrawingShapeGradientDirection GradientFillDirection,
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor,
    bool HasShadowEffect,
    DrawingShapeEffectPreset EffectPreset,
    bool UsesThemeEffects,
    int DrawingOrderIndex,
    /// <summary>Pre-rotation width in DIP pixels from &lt;a:xfrm&gt;&lt;a:ext cx&gt;, or null if absent.</summary>
    double? XfrmWidthPixels,
    /// <summary>Pre-rotation height in DIP pixels from &lt;a:xfrm&gt;&lt;a:ext cy&gt;, or null if absent.</summary>
    double? XfrmHeightPixels,
    /// <summary>Outline width in points; 0 = use default.</summary>
    double OutlineWidthPoints,
    /// <summary>True when &lt;a:ln&gt;&lt;a:noFill/&gt; is present — explicitly no border.</summary>
    bool OutlineHasNoFill,
    /// <summary>Outline dash style from &lt;a:prstDash val="..."/&gt;.</summary>
    DrawingShapeOutlineDash OutlineDash,
    // ── Arrowheads for line-like shapes (Line, ElbowConnector, CurvedConnector) ─
    /// <summary>Arrowhead at the start of a line/connector, from &lt;a:headEnd&gt;; null = none.</summary>
    DrawingArrowhead? HeadArrowhead,
    /// <summary>Arrowhead at the end of a line/connector, from &lt;a:tailEnd&gt;; null = none.</summary>
    DrawingArrowhead? TailArrowhead,
    /// <summary>
    /// R90-shape-5-3: id of the shape a connector's start point is glued to, from
    /// &lt;a:stCxn id="..." idx="..."/&gt; under &lt;xdr:cNvCxnSpPr&gt;; null = unattached.
    /// </summary>
    int? StartConnectedShapeId,
    /// <summary>Connection-site index on <see cref="StartConnectedShapeId"/>; null when unattached.</summary>
    int? StartConnectedShapeConnectionIndex,
    /// <summary>
    /// R90-shape-5-3: id of the shape a connector's end point is glued to, from
    /// &lt;a:endCxn id="..." idx="..."/&gt; under &lt;xdr:cNvCxnSpPr&gt;; null = unattached.
    /// </summary>
    int? EndConnectedShapeId,
    /// <summary>Connection-site index on <see cref="EndConnectedShapeId"/>; null when unattached.</summary>
    int? EndConnectedShapeConnectionIndex,
    // ── txBody text fields (null/empty = no text) ─────────────────────────
    /// <summary>Concatenated plain text from all &lt;a:t&gt; runs; null when there is no txBody.</summary>
    string? ShapeText,
    /// <summary>Font size in points from &lt;a:rPr sz&gt; (OOXML hundredths-of-a-point / 100); 0 = default.</summary>
    double ShapeTextFontSizePoints,
    bool ShapeTextBold,
    bool ShapeTextItalic,
    bool ShapeTextUnderline,
    CellColor? ShapeTextColor,
    WorkbookThemeColorReference? ShapeTextThemeColor,
    DrawingShapeTextHAlign ShapeTextHAlign,
    DrawingShapeTextVAnchor ShapeTextVAnchor,
    bool ShapeTextWrap,
    // ── WordArt fields (null/false = not WordArt) ─────────────────────────
    /// <summary>True when the run carries a gradient text fill, text outline, or prstTxWarp.</summary>
    bool IsWordArt,
    /// <summary>Warp preset string from &lt;a:prstTxWarp prst="..."&gt;; null = no warp.</summary>
    string? WarpPreset,
    /// <summary>Gradient end color for a WordArt text gradient fill; null = solid fill.</summary>
    CellColor? ShapeTextGradientEndColor,
    WorkbookThemeColorReference? ShapeTextGradientEndThemeColor,
    /// <summary>
    /// Linear gradient angle in OOXML 60,000ths-of-a-degree from &lt;a:lin ang="..."&gt;.
    /// 5400000 = 90° = top-to-bottom (default).
    /// </summary>
    long ShapeTextGradientAngle,
    /// <summary>Text outline color from &lt;a:rPr&gt;&lt;a:ln&gt;; null = no text outline.</summary>
    CellColor? ShapeTextOutlineColor,
    WorkbookThemeColorReference? ShapeTextOutlineThemeColor,
    double ShapeTextOutlineWidthPoints,
    /// <summary>Adjust-handle values from &lt;a:avLst&gt;&lt;a:gd .../&gt;; null/empty = geometry defaults.</summary>
    IReadOnlyList<DrawingShapeAdjustValue>? AdjustValues = null,
    // R97-model-drawing-hyperlink-2-2: see the matching field on XlsxPicturePackagePart.
    DrawingObjectHyperlink? Hyperlink = null,
    // R149-app-accessibility-checker-decorative-shapes: true when this shape/connector's
    // <xdr:cNvPr><a:extLst> carries the "Mark as decorative" extension (see
    // XlsxWorksheetDrawingParts.ReadNonVisualDecorative) -- mirrors XlsxPicturePackagePart's
    // IsDecorative field.
    bool IsDecorative = false);

internal sealed record XlsxWorksheetDrawingPackageParts(
    IReadOnlyList<XlsxChartPackagePart> ChartParts,
    IReadOnlyList<XlsxPicturePackagePart> PictureParts,
    IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
    IReadOnlyList<XlsxShapePackagePart> ShapeParts)
{
    public static XlsxWorksheetDrawingPackageParts Empty { get; } = new([], [], [], []);
}

internal sealed record XlsxDrawingAnchor(
    ChartDrawingAnchorKind Kind,
    uint FromRowZeroBased,
    uint FromColumnZeroBased,
    double FromRowOffset,
    double FromColumnOffset,
    double? AbsoluteLeft,
    double? AbsoluteTop,
    uint? ToRowZeroBased,
    uint? ToColumnZeroBased,
    double? ToRowOffset,
    double? ToColumnOffset,
    double? Width,
    double? Height);

internal static partial class XlsxWorksheetDrawingPartReader
{
    public static XlsxWorksheetDrawingPackageParts ReadParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var drawingContext = ReadDrawingContext(archive, worksheetPath, worksheetXml);
        if (drawingContext is null)
            return XlsxWorksheetDrawingPackageParts.Empty;

        var (drawingPath, drawingXml) = drawingContext.Value;
        var drawingRelsXml = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(drawingPath)) is { } drawingRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(drawingRelsEntry)
            : null;

        var charts = ReadChartParts(archive, drawingPath, drawingXml, drawingRelsXml);
        // R119-io-camera-linked-picture-identity: a reconstructed camera/linked-picture group (see
        // ReadPictureSnapshotGroupParts) is an <xdr:grpSp>, not an <xdr:pic>, so ReadPictureParts'
        // Descendants(pic) walk never finds it -- it must be read as its own pass and merged in here.
        var pictures = ReadPictureParts(archive, drawingPath, drawingXml, drawingRelsXml)
            .Concat(ReadPictureSnapshotGroupParts(drawingXml))
            .ToList();
        var (textBoxes, shapes) = ReadShapeParts(drawingXml, drawingRelsXml);
        return new XlsxWorksheetDrawingPackageParts(charts, pictures, textBoxes, shapes);
    }

    /// <summary>
    /// R97-model-drawing-hyperlink-2-2: reads every relationship in a drawing part's own <c>.rels</c>,
    /// keeping BOTH the Target and TargetMode (unlike <see cref="ReadRelationshipTargetsById"/>, which
    /// only the blip-embed/blip-link callers need and which drops TargetMode). Used to resolve an
    /// <c>a:hlinkClick@r:id</c> on a picture/shape/text-box's <c>cNvPr</c> into the (Target,
    /// TargetMode) pair <see cref="DrawingObjectHyperlink"/> carries -- mirrors
    /// <c>XlsxWorksheetDrawingObjectWriter.ReadOldDrawingObjectHyperlinksByName</c>'s identical
    /// relationship-resolution shape (that one resolves by object NAME across a save's pre-rebuild
    /// bytes; this one resolves by r:id at LOAD time so the model itself carries the hyperlink).
    /// </summary>
    private static Dictionary<string, (string Target, string? TargetMode)> ReadRelationshipsWithTargetModeById(
        XElement? relationshipRoot, XNamespace packageRelNs)
    {
        var rels = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        if (relationshipRoot is null)
            return rels;

        foreach (var relationship in relationshipRoot.Elements(packageRelNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
                rels.TryAdd(id, (target, relationship.Attribute("TargetMode")?.Value));
        }

        return rels;
    }

    /// <summary>
    /// R97-model-drawing-hyperlink-2-2: reads the object-level hyperlink (<c>&lt;a:hlinkClick&gt;</c>)
    /// off the FIRST <c>cNvPr</c> descendant of <paramref name="element"/> -- the same descendant
    /// <see cref="ReadNonVisualProperties"/> reads name/title/descr from -- and resolves its
    /// <c>r:id</c> via <paramref name="hyperlinkRelsById"/>. Returns null when the element has no
    /// hlinkClick, or its r:id doesn't resolve (a malformed/missing rels part).
    /// </summary>
    private static DrawingObjectHyperlink? ReadObjectHyperlink(
        XElement element,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)> hyperlinkRelsById)
    {
        var cNvPr = element.Descendants(spreadsheetDrawingNs + "cNvPr").FirstOrDefault();
        var hlinkClick = cNvPr?.Element(drawingNs + "hlinkClick");
        var relId = hlinkClick?.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrEmpty(relId) || !hyperlinkRelsById.TryGetValue(relId, out var resolved))
            return null;

        var tooltip = hlinkClick!.Attribute("tooltip")?.Value;
        return new DrawingObjectHyperlink(
            resolved.Target,
            resolved.TargetMode,
            string.IsNullOrWhiteSpace(tooltip) ? null : tooltip);
    }

    private static IReadOnlyList<XlsxChartPackagePart> ReadChartParts(
        ZipArchive archive,
        string drawingPath,
        XDocument drawingXml,
        XDocument? drawingRelsXml)
    {
        var charts = new List<XlsxChartPackagePart>();
        if (drawingRelsXml?.Root is null)
            return charts;

        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var relationshipTargets = ReadRelationshipTargetsById(drawingRelsXml.Root, packageRelNs);
        // R98-io-chart-hyperlink-model-field: TargetMode-carrying sibling of relationshipTargets above,
        // used only to resolve a chart graphicFrame's a:hlinkClick/@r:id -- mirrors ReadPictureParts'/
        // ReadShapeParts' identical hyperlinkRelsById (R97-model-drawing-hyperlink-2-2).
        var hyperlinkRelsById = ReadRelationshipsWithTargetModeById(drawingRelsXml.Root, packageRelNs);

        // A single chart part referenced by more than one anchor in the same worksheet drawing is one
        // chart, not several: count each resolved chart-part path at most once so a drawing that ends up
        // with duplicate graphicFrames pointing at the same chart (e.g. a source-package anchor merged on
        // top of a freshly-written one) does not load — and re-save — the chart twice.
        var seenChartPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chartElement in drawingXml.Descendants().Where(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart"))
        {
            var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId))
                continue;

            if (!relationshipTargets.TryGetValue(chartRelId, out var chartTarget))
                continue;

            var chartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartTarget);
            if (!seenChartPaths.Add(chartPath))
                continue;
            var chartEntry = archive.GetEntry(chartPath);
            if (chartEntry is null)
                continue;
            var chartRelationships = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(chartPath)) is { } chartRelsEntry
                ? XlsxPackageXmlEditor.LoadXml(chartRelsEntry)
                : null;

            // R42-io-drawing-group-transform-3-1: a chart nested inside one or more <xdr:grpSp>
            // groups shares its nearest worksheet anchor with every sibling in the group -- that
            // anchor alone only describes the GROUP's outer bounding box. Compose the ancestor
            // group transform chain (the same mechanism ReadPictureParts/ReadSpElement/
            // ReadCxnSpElement use -- see ComputeGroupTransform) so the chart's own local
            // <xdr:graphicFrame><xdr:xfrm><a:off>/<a:ext> is translated into worksheet coordinates
            // and the chart gets its own sub-position and sub-size within the group instead of
            // inheriting the whole group's anchor as-is.
            var graphicFrameElement = chartElement.Ancestors(spreadsheetDrawingNs + "graphicFrame").FirstOrDefault();
            var chartTransform = graphicFrameElement?.Element(spreadsheetDrawingNs + "xfrm");
            var chartGroupTransform = ComputeGroupTransform(chartElement, spreadsheetDrawingNs, drawingNs);
            var chartAnchor = ReadNearestAnchor(chartElement, chartTransform, chartGroupTransform);
            if (chartAnchor is not null && chartGroupTransform != DrawingGroupTransform.Identity)
            {
                var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(chartTransform, drawingNs, chartGroupTransform);
                if (xfrmWidthPixels is > 0 && xfrmHeightPixels is > 0)
                    chartAnchor = chartAnchor with { Width = xfrmWidthPixels, Height = xfrmHeightPixels };
            }

            // R63-io-drawing-chart-zorder: same nearest-anchor sibling-index lookup ReadPictureParts/
            // ReadSpElement/ReadCxnSpElement already use for their own DrawingOrderIndex (see
            // ReadNearestAnchorOrderIndex in XlsxWorksheetDrawingPartReader.Anchors.cs) -- without this,
            // a chart carried no record of its true position relative to sibling shape/picture/text-box
            // anchors and always normalized to the back of the z-order stack on load.
            var chartDrawingOrderIndex = ReadNearestAnchorOrderIndex(chartElement);

            // R80-app-accessibility-a11y-5-1: the chart's Alt Text title/description live on the
            // <xdr:graphicFrame>'s own <xdr:nvGraphicFramePr><xdr:cNvPr title="..." descr="..."/> --
            // NOT inside the <c:chart>/<cx:chart> element itself, which (per schema) is just a
            // self-closing r:id reference and has no cNvPr descendant of its own.
            var (_, chartAltTextTitle, chartAltTextDescription) = ReadNonVisualProperties(graphicFrameElement ?? chartElement);

            charts.Add(new XlsxChartPackagePart(
                XlsxPackageXmlEditor.LoadXml(chartEntry),
                chartRelationships,
                // R81-io-drawing-chart-name: the chart's name lives on the <xdr:graphicFrame>'s own
                // <xdr:nvGraphicFramePr><xdr:cNvPr name="..."/> -- NOT inside the <c:chart>/<cx:chart>
                // element, which (per schema) is just a self-closing r:id reference with no cNvPr
                // descendant. Read from graphicFrameElement (same source as the Alt Text title/descr
                // above) so the round-tripped ChartModel.Name is preserved instead of always null.
                ReadNonVisualName(graphicFrameElement ?? chartElement),
                chartAnchor,
                chartDrawingOrderIndex,
                chartAltTextTitle,
                chartAltTextDescription,
                // R98-io-chart-hyperlink-model-field: resolve the chart graphicFrame's OWN object-level
                // hyperlink from the drawing part's own relationships -- reads from graphicFrameElement
                // (same source as the name/Alt-Text reads above) since that is where the cNvPr carrying
                // hlinkClick actually lives, not inside <c:chart>/<cx:chart>.
                ReadObjectHyperlink(graphicFrameElement ?? chartElement, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById)));
        }

        return charts;
    }

    internal static IReadOnlyList<XlsxPicturePackagePart> ReadPictureParts(
        ZipArchive archive,
        string drawingPath,
        XDocument drawingXml,
        XDocument? drawingRelsXml)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        if (drawingRelsXml?.Root is null)
            return [];

        var relationshipTargets = ReadRelationshipTargetsById(drawingRelsXml.Root, packageRelNs);
        // R97-model-drawing-hyperlink-2-2: TargetMode-carrying sibling of relationshipTargets above,
        // used only to resolve a:hlinkClick/@r:id (blip embed/link resolution above never needs
        // TargetMode, so it keeps using the lighter relationshipTargets dict).
        var hyperlinkRelsById = ReadRelationshipsWithTargetModeById(drawingRelsXml.Root, packageRelNs);
        var pictures = new List<XlsxPicturePackagePart>(relationshipTargets.Count);

        foreach (var pictureElement in drawingXml.Descendants(spreadsheetDrawingNs + "pic"))
        {
            var (blipRelId, isExternalLink) = ReadPictureBlipRelationshipId(pictureElement, drawingNs, relNs);
            if (string.IsNullOrWhiteSpace(blipRelId))
                continue;

            if (!relationshipTargets.TryGetValue(blipRelId, out var blipTarget))
                continue;

            byte[] imageBytes;
            string contentType;
            string? linkTarget = null;
            if (isExternalLink)
            {
                // R65-io-image-drawing-6-1: a picture inserted via Excel "Link to File" carries r:link
                // (not r:embed) and has NO corresponding image part inside the package at all --
                // blipTarget is an external URI/path (often absolute, e.g.
                // "file:///C:/Images/photo.png"), not a package-relative part path, so it must never be
                // run through ResolveRelationshipTarget/archive.GetEntry (which looks for -- and would
                // fail to find -- a package entry; that failed lookup is exactly how this picture used
                // to be silently dropped). Materialize it with no embedded bytes instead, preserving the
                // external target verbatim so the writer can re-emit the same r:link + External
                // relationship on save.
                imageBytes = [];
                contentType = "";
                linkTarget = blipTarget;
            }
            else
            {
                var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, blipTarget);
                var imageEntry = archive.GetEntry(imagePath);
                if (imageEntry is null)
                    continue;

                imageBytes = ReadEntryBytes(imageEntry);
                contentType = XlsxPackagePath.GetImageContentType(imagePath);
            }

            // R80-io-drawing-image-5-3: a picture inserted via Excel's Insert > Icons/SVG carries a
            // second, vector relationship inside <a:blip><a:extLst> (the asvg:svgBlip extension) that
            // ReadPictureBlipRelationshipId's plain Descendants(a:blip) walk above never surfaces --
            // read it separately so the vector original isn't silently reduced to the PNG fallback.
            byte[]? svgImageBytes = null;
            if (!isExternalLink)
            {
                var svgBlipRelId = ReadPictureSvgBlipRelationshipId(pictureElement, drawingNs, relNs);
                if (!string.IsNullOrWhiteSpace(svgBlipRelId) &&
                    relationshipTargets.TryGetValue(svgBlipRelId, out var svgBlipTarget))
                {
                    var svgImagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, svgBlipTarget);
                    var svgImageEntry = archive.GetEntry(svgImagePath);
                    if (svgImageEntry is not null)
                        svgImageBytes = ReadEntryBytes(svgImageEntry);
                }
            }

            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");
            var (name, title, altText) = ReadNonVisualProperties(pictureElement);
            // R90-app-accessibility-checker-5-2: preserve Excel's "Mark as decorative" flag so a
            // decorative picture stays exempt from the Missing-Alt-Text rule after a round-trip.
            var isDecorative = ReadNonVisualDecorative(pictureElement);
            var anchorElement = FindNearestAnchorElement(pictureElement, spreadsheetDrawingNs);
            var pictureTransform = pictureElement.Element(spreadsheetDrawingNs + "spPr")?.Element(drawingNs + "xfrm");

            // A picture nested inside one or more <xdr:grpSp> groups shares its nearest worksheet
            // anchor with every sibling in the group — that anchor alone only describes the GROUP's
            // outer bounding box. Compose the ancestor group transform chain (the same mechanism
            // ReadSpElement/ReadCxnSpElement use) so the picture's own local <a:xfrm><a:off>/<a:ext>
            // is translated into worksheet coordinates and the picture gets its own sub-position and
            // sub-size within the group instead of inheriting the whole group's anchor as-is.
            var groupTransform = ComputeGroupTransform(pictureElement, spreadsheetDrawingNs, drawingNs);
            var anchor = anchorElement is null
                ? null
                : ReadNearestAnchor(pictureElement, pictureTransform, groupTransform);
            if (anchor is not null && groupTransform != DrawingGroupTransform.Identity)
            {
                var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(pictureTransform, drawingNs, groupTransform);
                if (xfrmWidthPixels is > 0 && xfrmHeightPixels is > 0)
                    anchor = anchor with { Width = xfrmWidthPixels, Height = xfrmHeightPixels };
            }

            // R54-io-drawing-group-transform-4-1: compose the picture's own local rotation/flip with
            // every ancestor group's rotation/flip (see ComposeShapeOrientationWithGroups) so a picture
            // nested in a rotated/flipped group gets its true rendered facing direction, not just its
            // own local (pre-group) one.
            var (pictureRotation, pictureFlipHorizontal, pictureFlipVertical) = ComposeShapeOrientationWithGroups(
                ReadDrawingRotation(pictureTransform), ReadDrawingFlipHorizontal(pictureTransform), ReadDrawingFlipVertical(pictureTransform), groupTransform);

            pictures.Add(new XlsxPicturePackagePart(
                imageBytes,
                contentType,
                name,
                title,
                altText,
                anchor,
                pictureRotation,
                pictureFlipHorizontal,
                pictureFlipVertical,
                ReadSourceRectangleRatio(sourceRectangle, "l"),
                ReadSourceRectangleRatio(sourceRectangle, "t"),
                ReadSourceRectangleRatio(sourceRectangle, "r"),
                ReadSourceRectangleRatio(sourceRectangle, "b"),
                anchorElement is null ? -1 : ReadAnchorOrderIndex(anchorElement, spreadsheetDrawingNs),
                linkTarget,
                svgImageBytes,
                isDecorative,
                ReadObjectHyperlink(pictureElement, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById)));
        }

        return pictures;
    }

    /// <summary>
    /// R119-io-camera-linked-picture-identity: recognizes an <c>&lt;xdr:grpSp&gt;</c> the writer
    /// marked (via <c>ToPictureSnapshotGroupExtLst</c>'s <c>fx:linkedPictureSnapshot</c> extension on
    /// its <c>&lt;xdr:nvGrpSpPr&gt;&lt;xdr:cNvPr&gt;</c>) as a reconstructed CellRangeSnapshot
    /// picture -- a "camera" / Paste Special &gt; Linked Picture / Paste Picture object with no
    /// rasterized bitmap, re-emitted as a background rectangle plus one rectangle+text shape per
    /// cached cell (see <c>ToOneCellPictureSnapshotAnchor</c>) -- and reconstructs it as a SINGLE
    /// <see cref="XlsxPicturePackagePart"/> (<see cref="PictureKind.CellRangeSnapshot"/>) instead of
    /// letting <see cref="ReadShapeParts"/> flatten its children into independent, ungrouped
    /// shapes. Without this, IsLinkedToSourceRange/LinkedSourceRange/LinkedSourceSheetName -- and the
    /// picture's identity as a single object -- were permanently destroyed on every save+reload: the
    /// group carried none of that metadata anywhere in its XML, and nothing rebuilt a PictureModel
    /// from a loaded grpSp at all.
    /// <para>
    /// An ordinary, user-authored group of shapes (Excel's own "Group" command) carries no such
    /// extension and is untouched by this method -- it is skipped and continues to flatten via
    /// <see cref="ReadShapeParts"/> exactly as before.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<XlsxPicturePackagePart> ReadPictureSnapshotGroupParts(XDocument drawingXml)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var result = new List<XlsxPicturePackagePart>();

        foreach (var group in drawingXml.Descendants(spreadsheetDrawingNs + "grpSp"))
        {
            var marker = ReadPictureSnapshotGroupMarker(group, spreadsheetDrawingNs, drawingNs);
            if (marker is null)
                continue;

            var (name, title, altText) = ReadNonVisualProperties(group);
            var isDecorative = ReadNonVisualDecorative(group);
            var groupTransform = group.Element(spreadsheetDrawingNs + "grpSpPr")?.Element(drawingNs + "xfrm");
            var rotation = ReadDrawingRotation(groupTransform);
            var flipHorizontal = ReadDrawingFlipHorizontal(groupTransform);
            var flipVertical = ReadDrawingFlipVertical(groupTransform);
            var anchor = ReadNearestAnchor(group);
            var orderIndex = ReadNearestAnchorOrderIndex(group);

            var cells = new List<PictureCellSnapshot>();
            foreach (var sp in group.Elements(spreadsheetDrawingNs + "sp"))
            {
                var cell = ReadPictureSnapshotCell(sp, spreadsheetDrawingNs, drawingNs);
                if (cell is not null)
                    cells.Add(cell);
            }

            result.Add(new XlsxPicturePackagePart(
                ImageBytes: [],
                ContentType: "",
                Name: name,
                Title: title,
                AltText: altText,
                Anchor: anchor,
                RotationDegrees: rotation,
                FlipHorizontal: flipHorizontal,
                FlipVertical: flipVertical,
                CropLeft: 0,
                CropTop: 0,
                CropRight: 0,
                CropBottom: 0,
                DrawingOrderIndex: orderIndex,
                IsDecorative: isDecorative,
                Kind: PictureKind.CellRangeSnapshot,
                SnapshotCells: cells,
                IsLinkedToSourceRange: marker.Value.IsLinked,
                LinkedSourceSheetName: marker.Value.SourceSheetName,
                LinkedSourceStartRow: marker.Value.StartRow,
                LinkedSourceStartCol: marker.Value.StartCol,
                LinkedSourceEndRow: marker.Value.EndRow,
                LinkedSourceEndCol: marker.Value.EndCol,
                SnapshotSourceRowCount: marker.Value.SourceRowCount,
                SnapshotSourceColumnCount: marker.Value.SourceColumnCount));
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="element"/> (an <c>&lt;xdr:sp&gt;</c>/<c>&lt;xdr:cxnSp&gt;</c>
    /// candidate for <see cref="ReadShapeParts"/>) sits inside a group <see cref="ReadPictureSnapshotGroupParts"/>
    /// already reconstructed whole as a single picture -- see the matching comment at its call sites.
    /// </summary>
    private static bool IsInsideCellRangeSnapshotGroup(XElement element, XNamespace spreadsheetDrawingNs, XNamespace drawingNs)
    {
        foreach (var group in element.Ancestors(spreadsheetDrawingNs + "grpSp"))
        {
            if (ReadPictureSnapshotGroupMarker(group, spreadsheetDrawingNs, drawingNs) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the <c>fx:linkedPictureSnapshot</c> marker (see <see cref="XlsxWorksheetDrawingObjectWriter.ToPictureSnapshotGroupExtLst"/>)
    /// off <paramref name="group"/>'s <c>&lt;xdr:nvGrpSpPr&gt;&lt;xdr:cNvPr&gt;&lt;a:extLst&gt;</c>, or
    /// <see langword="null"/> when <paramref name="group"/> carries no such marker (an ordinary,
    /// user-authored group of shapes).
    /// </summary>
    private static (bool IsLinked, string? SourceSheetName, int? StartRow, int? StartCol, int? EndRow, int? EndCol, uint SourceRowCount, uint SourceColumnCount)?
        ReadPictureSnapshotGroupMarker(XElement group, XNamespace spreadsheetDrawingNs, XNamespace drawingNs)
    {
        var cNvPr = group.Element(spreadsheetDrawingNs + "nvGrpSpPr")?.Element(spreadsheetDrawingNs + "cNvPr");
        var marker = cNvPr?
            .Element(drawingNs + "extLst")?
            .Elements(drawingNs + "ext")
            .FirstOrDefault(ext => (string?)ext.Attribute("uri") == CellRangeSnapshotGroupExtensionUri)?
            .Elements()
            .FirstOrDefault(child => child.Name.LocalName == "linkedPictureSnapshot");
        if (marker is null)
            return null;

        int? ParseInt(string attributeName) =>
            int.TryParse(marker.Attribute(attributeName)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        uint ParseUintOrDefault(string attributeName, uint fallback) =>
            uint.TryParse(marker.Attribute(attributeName)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        var sourceSheet = marker.Attribute("sourceSheet")?.Value;
        return (
            XlsxWorksheetXmlValueParser.IsTruthy(marker.Attribute("isLinked")?.Value),
            string.IsNullOrWhiteSpace(sourceSheet) ? null : sourceSheet,
            ParseInt("sourceStartRow"),
            ParseInt("sourceStartCol"),
            ParseInt("sourceEndRow"),
            ParseInt("sourceEndCol"),
            ParseUintOrDefault("sourceRowCount", 1),
            ParseUintOrDefault("sourceColCount", 1));
    }

    /// <summary>
    /// Reconstructs one <see cref="PictureCellSnapshot"/> from a per-cell rectangle
    /// <c>&lt;xdr:sp&gt;</c> written by <c>ToPictureSnapshotCellShape</c>. The cell's RowOffset/
    /// ColumnOffset is recovered from the shape's own <c>cNvPr@name</c> ("Cell {row}_{col}" --
    /// written by that same method), not recomputed from pixel geometry, so it is exact even when
    /// the group's cellWidthEmu/cellHeightEmu division was lossy. Returns <see langword="null"/> for
    /// the group's "Background" rectangle (drawn by <c>ToPictureSnapshotBackgroundShape</c>, not a
    /// cell) or any shape whose name doesn't match the expected pattern.
    /// </summary>
    private static PictureCellSnapshot? ReadPictureSnapshotCell(XElement sp, XNamespace spreadsheetDrawingNs, XNamespace drawingNs)
    {
        var cellName = sp.Element(spreadsheetDrawingNs + "nvSpPr")?.Element(spreadsheetDrawingNs + "cNvPr")?.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(cellName) || !cellName.StartsWith("Cell ", StringComparison.Ordinal))
            return null;

        var offsetParts = cellName["Cell ".Length..].Split('_');
        if (offsetParts.Length != 2 ||
            !uint.TryParse(offsetParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowOffset) ||
            !uint.TryParse(offsetParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var colOffset))
        {
            return null;
        }

        var spPr = sp.Element(spreadsheetDrawingNs + "spPr");
        var fillColor = ReadDrawingSolidFillColor(spPr?.Element(drawingNs + "solidFill"), drawingNs);
        var run = sp.Element(spreadsheetDrawingNs + "txBody")?.Element(drawingNs + "p")?.Element(drawingNs + "r");
        var text = run?.Element(drawingNs + "t")?.Value ?? string.Empty;

        CellStyle? style = null;
        var rPr = run?.Element(drawingNs + "rPr");
        if (fillColor is not null || rPr is not null)
        {
            style = new CellStyle { FillColor = fillColor };
            if (rPr is not null)
            {
                if (int.TryParse(rPr.Attribute("sz")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var szHundredths) && szHundredths > 0)
                    style.FontSize = szHundredths / 100.0;
                style.Bold = rPr.Attribute("b")?.Value == "1";
                style.Italic = rPr.Attribute("i")?.Value == "1";
                style.Underline = rPr.Attribute("u")?.Value == "sng";
                var fontColor = ReadDrawingSolidFillColor(rPr.Element(drawingNs + "solidFill"), drawingNs);
                if (fontColor is not null)
                    style.FontColor = fontColor.Value;
            }
        }

        return new PictureCellSnapshot(rowOffset, colOffset, text, style);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        if (entry.Length <= int.MaxValue)
        {
            var bytes = GC.AllocateUninitializedArray<byte>((int)entry.Length);
            using var stream = entry.Open();
            stream.ReadExactly(bytes);
            return bytes;
        }

        using var fallbackStream = entry.Open();
        using var ms = new MemoryStream();
        fallbackStream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads the relationship id a picture's <c>&lt;a:blip&gt;</c> points at, preferring
    /// <c>r:embed</c> (a normal embedded image part) and falling back to <c>r:link</c> only when no
    /// embed id is present anywhere on the element (R65-io-image-drawing-6-1: a picture inserted via
    /// Excel's "Link to File" carries only <c>r:link</c>, pointing at an External-mode relationship
    /// whose Target is an external file path/URI, not a package part). <c>IsExternalLink</c> is true
    /// only for that link fallback, telling the caller to treat the resolved relationship target as an
    /// external URI rather than a package-relative part path.
    /// </summary>
    private static (string? RelationshipId, bool IsExternalLink) ReadPictureBlipRelationshipId(
        XElement pictureElement, XNamespace drawingNs, XNamespace relNs)
    {
        foreach (var blip in pictureElement.Descendants(drawingNs + "blip"))
        {
            var embedId = blip.Attribute(relNs + "embed")?.Value;
            if (!string.IsNullOrWhiteSpace(embedId))
                return (embedId, false);
        }

        foreach (var blip in pictureElement.Descendants(drawingNs + "blip"))
        {
            var linkId = blip.Attribute(relNs + "link")?.Value;
            if (!string.IsNullOrWhiteSpace(linkId))
                return (linkId, true);
        }

        return (null, false);
    }

    /// <summary>
    /// R80-io-drawing-image-5-3: reads the relationship id of a picture's vector fallback, carried in
    /// <c>&lt;a:blip&gt;&lt;a:extLst&gt;&lt;a:ext uri="{96DAC541-7B7A-43D3-8B79-37D633B846F1}"&gt;
    /// &lt;asvg:svgBlip r:embed=".."/&gt;&lt;/a:ext&gt;&lt;/a:extLst&gt;</c> -- the Microsoft SVG
    /// extension Excel writes for a picture inserted via Insert &gt; Icons/SVG so the picture stays
    /// editable as a vector (recolor, "Convert to Shape") even though <see cref="ReadPictureBlipRelationshipId"/>
    /// only ever resolves the PNG rasterization used as the universal-compatibility fallback. Returns
    /// null when the picture has no vector fallback (the common case for an ordinary raster picture).
    /// </summary>
    private static string? ReadPictureSvgBlipRelationshipId(
        XElement pictureElement, XNamespace drawingNs, XNamespace relNs)
    {
        XNamespace svgNs = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
        foreach (var blip in pictureElement.Descendants(drawingNs + "blip"))
        {
            var svgBlip = blip
                .Element(drawingNs + "extLst")?
                .Elements(drawingNs + "ext")
                .Select(ext => ext.Element(svgNs + "svgBlip"))
                .FirstOrDefault(element => element is not null);
            var embedId = svgBlip?.Attribute(relNs + "embed")?.Value;
            if (!string.IsNullOrWhiteSpace(embedId))
                return embedId;
        }

        return null;
    }

    private static Dictionary<string, string> ReadRelationshipTargetsById(XElement relationshipRoot, XNamespace packageRelNs)
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in relationshipRoot.Elements(packageRelNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
                targets.TryAdd(id, target);
        }

        return targets;
    }

    internal static (IReadOnlyList<XlsxTextBoxPackagePart> TextBoxes, IReadOnlyList<XlsxShapePackagePart> Shapes) ReadShapeParts(
        XDocument drawingXml, XDocument? drawingRelsXml = null)
    {
        var textBoxes = new List<XlsxTextBoxPackagePart>();
        var shapes = new List<XlsxShapePackagePart>();
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        // R97-model-drawing-hyperlink-2-2: resolves a shape/text-box's a:hlinkClick/@r:id into a
        // DrawingObjectHyperlink -- see ReadObjectHyperlink/ReadRelationshipsWithTargetModeById.
        var hyperlinkRelsById = ReadRelationshipsWithTargetModeById(drawingRelsXml?.Root, packageRelNs);

        // R78-io-shape-geometry-5-2: walk <xdr:sp> and <xdr:cxnSp> together in a single
        // document-order pass (rather than one full pass per element name) so a drawing part that
        // mixes plain shapes and connectors preserves their authored/original relative order in
        // the resulting shapes list -- two separate full passes would group all <xdr:sp> results
        // before all <xdr:cxnSp> results regardless of where each one actually sits in the XML.
        foreach (var element in drawingXml.Descendants())
        {
            if (element.Name == spreadsheetDrawingNs + "sp")
            {
                if (element.Ancestors(markupCompatNs + "Fallback").Any())
                    continue;

                // R119-io-camera-linked-picture-identity: a rectangle nested inside a group the
                // writer marked as a reconstructed CellRangeSnapshot picture (see
                // ToPictureSnapshotGroupExtLst/ReadPictureSnapshotGroupParts) is that picture's
                // per-cell content, already consumed whole by ReadPictureSnapshotGroupParts --
                // reading it AGAIN here would flatten it into a second, independent
                // DrawingShapeModel/TextBoxModel and permanently destroy the picture's identity
                // (and duplicate its content) on every load.
                if (IsInsideCellRangeSnapshotGroup(element, spreadsheetDrawingNs, drawingNs))
                    continue;

                ReadSpElement(element, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById, textBoxes, shapes);
            }
            else if (element.Name == spreadsheetDrawingNs + "cxnSp")
            {
                // Connectors (<xdr:cxnSp>) use the same spPr/prstGeom structure as sp.
                if (element.Ancestors(markupCompatNs + "Fallback").Any())
                    continue;

                if (IsInsideCellRangeSnapshotGroup(element, spreadsheetDrawingNs, drawingNs))
                    continue;

                ReadCxnSpElement(element, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById, shapes);
            }
        }

        return (textBoxes, shapes);
    }

    private static void ReadSpElement(
        XElement shapeElement,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)> hyperlinkRelsById,
        List<XlsxTextBoxPackagePart> textBoxes,
        List<XlsxShapePackagePart> shapes)
    {
        var name = ReadNonVisualName(shapeElement);
        var title = ReadNonVisualTitle(shapeElement);
        var altText = ReadNonVisualDescription(shapeElement);
        // R149-app-accessibility-checker-decorative-shapes: preserve Excel's "Mark as decorative"
        // flag for shapes and text boxes so they stay exempt from the Missing-Alt-Text rule, the
        // same as XlsxPicturePackagePart already does for pictures.
        var isDecorative = ReadNonVisualDecorative(shapeElement);
        var hyperlink = ReadObjectHyperlink(shapeElement, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById);
        var spPr = shapeElement.Element(spreadsheetDrawingNs + "spPr");
        var transform = spPr?.Element(drawingNs + "xfrm");
        var groupTransform = ComputeGroupTransform(shapeElement, spreadsheetDrawingNs, drawingNs);
        // R54-io-drawing-group-transform-4-1: a shape's own <a:xfrm> rot/flipH/flipV describe only its
        // facing direction WITHIN its immediate parent's coordinate space -- when that parent is a
        // rotated and/or flipped <xdr:grpSp>, the shape's true rendered facing direction also includes
        // every ancestor group's own rotation/flip, exactly as ComputeGroupTransform already composes
        // for POSITION.
        var (rotation, flipHorizontal, flipVertical) = ComposeShapeOrientationWithGroups(
            ReadDrawingRotation(transform), ReadDrawingFlipHorizontal(transform), ReadDrawingFlipVertical(transform), groupTransform);
        var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(transform, drawingNs, groupTransform);
        var (gradFillStartColor, _, gradFillEndColor, _, gradFillDirection, _) =
            ReadDrawingGradientFillColors(spPr?.Element(drawingNs + "gradFill"), drawingNs);
        var solidFill = spPr?.Element(drawingNs + "solidFill");
        var hasFill = spPr?.Element(drawingNs + "noFill") is null;
        var lnElement = spPr?.Element(drawingNs + "ln");
        var outlineFill = lnElement?.Element(drawingNs + "solidFill");
        var fillColor = gradFillStartColor ?? ReadDrawingSolidFillColor(solidFill, drawingNs);
        var outlineColor = ReadDrawingSolidFillColor(outlineFill, drawingNs);
        var fillThemeColor = solidFill is not null &&
                             XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, drawingNs, out var readFillThemeColor)
            ? readFillThemeColor
            : (WorkbookThemeColorReference?)null;
        var outlineThemeColor = outlineFill is not null &&
                                XlsxDrawingColorReader.TryReadThemeColorReference(outlineFill, drawingNs, out var readOutlineThemeColor)
            ? readOutlineThemeColor
            : (WorkbookThemeColorReference?)null;
        var outlineWidthPoints = ReadDrawingOutlineWidthPoints(lnElement);
        var outlineHasNoFill = lnElement is not null && lnElement.Element(drawingNs + "noFill") is not null;
        var outlineDash = ReadDrawingOutlineDash(lnElement, drawingNs);
        var effectPreset = ReadDrawingShapeEffectPreset(spPr, drawingNs);
        var hasShadowEffect = effectPreset == DrawingShapeEffectPreset.Shadow;

        // Determine if this is a true text-box (cNvSpPr txBox="1") or a shape that happens to
        // carry text in its txBody.  Text-boxes go into the textBoxes list (no prstGeom); shapes
        // with text stay as shapes so their geometry (ellipse etc.) is preserved.
        var isTxBox = shapeElement
            .Element(spreadsheetDrawingNs + "nvSpPr")?
            .Element(spreadsheetDrawingNs + "cNvSpPr")?
            .Attribute("txBox")?.Value == "1";

        var txBodyElement = shapeElement.Element(spreadsheetDrawingNs + "txBody");
        var text = ReadShapeTextBodyPlainText(txBodyElement, drawingNs);

        if (isTxBox)
        {
            // True text-box: forward to textBoxes list (original behaviour). R62-io-drawing-textbox-6-3:
            // route here purely on the cNvSpPr/@txBox="1" marker -- an emptied (text-deleted) text box
            // still carries that marker and Excel authors it with a <a:prstGeom prst="rect"/> just like
            // a populated one, so gating on non-empty text here would misclassify it as a generic
            // Rectangle shape and permanently lose its TextBox identity.
            //
            // backlog textbox-6-2: read the txBody's rich-text formatting (font size/bold/italic/
            // color/alignment) via the same ReadShapeTextFormatting helper the shape branch below
            // uses -- without this a loaded text box's formatting had nowhere to go (TextBoxModel
            // carried no fields for it) and was silently lost on Duplicate Sheet / re-save. Font
            // family isn't part of that helper's return (shapes don't track it either), so it's
            // read separately from the same first run.
            var (txBoxFontSizePt, txBoxBold, txBoxItalic, _, txBoxColor, txBoxThemeColor, txBoxHAlign, txBoxVAnchor,
                 _, _, _, _, _, _, _, _, _) = ReadShapeTextFormatting(txBodyElement, drawingNs);
            var txBoxFontFamily = ReadShapeTextFontFamily(txBodyElement, drawingNs);

            textBoxes.Add(new XlsxTextBoxPackagePart(
                text,
                name,
                title,
                altText,
                ReadNearestAnchor(shapeElement, transform, groupTransform),
                rotation,
                flipHorizontal,
                flipVertical,
                hasFill,
                fillThemeColor is null ? fillColor : null,
                outlineThemeColor is null ? outlineColor : null,
                fillThemeColor,
                outlineThemeColor,
                ReadNearestAnchorOrderIndex(shapeElement),
                txBoxFontFamily,
                txBoxFontSizePt,
                txBoxBold,
                txBoxItalic,
                txBoxColor,
                txBoxThemeColor,
                txBoxHAlign,
                txBoxVAnchor,
                outlineHasNoFill,
                hyperlink,
                isDecorative));
            return;
        }

        var preset = spPr?
            .Element(drawingNs + "prstGeom")?
            .Attribute("prst")?
            .Value;
        if (!DrawingMlPresetGeometryMap.TryGetShapeKind(preset, out var kind))
            return;

        // Parse txBody text formatting (simplified to first-run properties).
        var shapeText = string.IsNullOrEmpty(text) ? null : text;
        var (textFontSizePt, textBold, textItalic, textUnderline,
             textColor, textThemeColor, textHAlign, textVAnchor, textWrap,
             isWordArt, warpPreset,
             textGradEndColor, textGradEndThemeColor, textGradAngle,
             textOutlineColor, textOutlineThemeColor, textOutlineWidthPt) =
            ReadShapeTextFormatting(txBodyElement, drawingNs);

        shapes.Add(new XlsxShapePackagePart(
            kind,
            name,
            title,
            altText,
            ReadNearestAnchor(shapeElement, transform, groupTransform),
            rotation,
            flipHorizontal,
            flipVertical,
            hasFill,
            fillThemeColor is null ? fillColor : null,
            outlineThemeColor is null ? outlineColor : null,
            gradFillEndColor,
            gradFillDirection,
            fillThemeColor,
            outlineThemeColor,
            hasShadowEffect,
            effectPreset,
            ReadUsesThemeEffectStyle(shapeElement, drawingNs, spreadsheetDrawingNs),
            ReadNearestAnchorOrderIndex(shapeElement),
            xfrmWidthPixels,
            xfrmHeightPixels,
            outlineWidthPoints,
            outlineHasNoFill,
            outlineDash,
            ReadDrawingArrowhead(lnElement, drawingNs, "headEnd"),
            ReadDrawingArrowhead(lnElement, drawingNs, "tailEnd"),
            // R90-shape-5-3: only <xdr:cxnSp> elements carry stCxn/endCxn -- a plain <xdr:sp> never does.
            null, // StartConnectedShapeId
            null, // StartConnectedShapeConnectionIndex
            null, // EndConnectedShapeId
            null, // EndConnectedShapeConnectionIndex
            shapeText,
            textFontSizePt,
            textBold,
            textItalic,
            textUnderline,
            textColor,
            textThemeColor,
            textHAlign,
            textVAnchor,
            textWrap,
            isWordArt,
            warpPreset,
            textGradEndColor,
            textGradEndThemeColor,
            textGradAngle,
            textOutlineColor,
            textOutlineThemeColor,
            textOutlineWidthPt,
            ReadShapeAdjustValues(spPr, drawingNs),
            hyperlink,
            isDecorative));
    }

    /// <summary>
    /// Flattens a shape <c>&lt;txBody&gt;</c> element to a single plain-text string, preserving
    /// paragraph boundaries as <c>\n</c> so multi-paragraph shape/text-box text (e.g. a text box
    /// with several lines) round-trips as distinct lines instead of being run together with no
    /// separator.  Runs within the same paragraph are concatenated directly (a run boundary is a
    /// formatting split, not a line break); an explicit <c>&lt;a:br/&gt;</c> line break within a
    /// paragraph is also mapped to <c>\n</c>.
    /// <para>
    /// Per-run formatting beyond the first run is still simplified away — see
    /// <see cref="ReadShapeTextFormatting"/> — because <c>DrawingShapeModel</c> only carries a
    /// single flat <c>ShapeText</c> string with one set of formatting fields, not a per-run model.
    /// </para>
    /// </summary>
    private static string ReadShapeTextBodyPlainText(XElement? txBody, XNamespace drawingNs)
    {
        if (txBody is null)
            return "";

        var paragraphs = txBody.Elements(drawingNs + "p").ToList();
        if (paragraphs.Count == 0)
            return "";

        var paragraphTexts = new List<string>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var node in paragraph.Elements())
            {
                if (node.Name == drawingNs + "br")
                {
                    builder.Append('\n');
                }
                else if (node.Name == drawingNs + "r" || node.Name == drawingNs + "fld")
                {
                    var t = node.Element(drawingNs + "t");
                    if (t is not null)
                        builder.Append(t.Value);
                }
            }

            paragraphTexts.Add(builder.ToString());
        }

        return string.Join("\n", paragraphTexts);
    }

    /// <summary>
    /// Parses formatting from the first run of a shape <c>&lt;txBody&gt;</c> element.
    /// Also reads WordArt-specific fields: gradient text fill, text outline, and warp preset.
    /// Multi-run rich text is simplified to first-run properties (documented simplification).
    /// </summary>
    private static (double FontSizePt, bool Bold, bool Italic, bool Underline,
        CellColor? Color, WorkbookThemeColorReference? ThemeColor,
        DrawingShapeTextHAlign HAlign, DrawingShapeTextVAnchor VAnchor, bool Wrap,
        bool IsWordArt, string? WarpPreset,
        CellColor? GradEndColor, WorkbookThemeColorReference? GradEndThemeColor, long GradAngle,
        CellColor? OutlineColor, WorkbookThemeColorReference? OutlineThemeColor, double OutlineWidthPt)
        ReadShapeTextFormatting(XElement? txBody, XNamespace drawingNs)
    {
        static (double, bool, bool, bool, CellColor?, WorkbookThemeColorReference?,
            DrawingShapeTextHAlign, DrawingShapeTextVAnchor, bool,
            bool, string?,
            CellColor?, WorkbookThemeColorReference?, long,
            CellColor?, WorkbookThemeColorReference?, double)
            Default(DrawingShapeTextHAlign hAlign = DrawingShapeTextHAlign.Left,
                    DrawingShapeTextVAnchor vAnchor = DrawingShapeTextVAnchor.Middle,
                    bool wrap = true)
            => (0, false, false, false, null, null, hAlign, vAnchor, wrap,
                false, null, null, null, 5400000L, null, null, 0);

        if (txBody is null)
            return Default();

        // bodyPr: anchor attribute, wrap attribute, and prstTxWarp (WordArt warp preset).
        var bodyPr = txBody.Element(drawingNs + "bodyPr");
        var anchorAttr = bodyPr?.Attribute("anchor")?.Value ?? "";
        var vAnchor = anchorAttr switch
        {
            "t" => DrawingShapeTextVAnchor.Top,
            "b" => DrawingShapeTextVAnchor.Bottom,
            _ => DrawingShapeTextVAnchor.Middle, // "ctr" or unspecified
        };
        var wrapAttr = bodyPr?.Attribute("wrap")?.Value ?? "square";
        var wrap = !string.Equals(wrapAttr, "none", StringComparison.OrdinalIgnoreCase);

        // prstTxWarp: WordArt warp preset. Preserved for round-trip; not rendered (deferred).
        var prstTxWarp = bodyPr?.Element(drawingNs + "prstTxWarp");
        var warpPreset = prstTxWarp?.Attribute("prst")?.Value;

        // Paragraph: first <a:p> → <a:pPr algn>
        var firstParagraph = txBody.Element(drawingNs + "p");
        var pPr = firstParagraph?.Element(drawingNs + "pPr");
        var algnAttr = pPr?.Attribute("algn")?.Value ?? "";
        var hAlign = algnAttr switch
        {
            "ctr" => DrawingShapeTextHAlign.Center,
            "r" => DrawingShapeTextHAlign.Right,
            _ => DrawingShapeTextHAlign.Left, // "l" or unspecified
        };

        // First run: <a:r><a:rPr>
        var firstRun = firstParagraph?.Element(drawingNs + "r");
        var rPr = firstRun?.Element(drawingNs + "rPr");
        if (rPr is null)
            return Default(hAlign, vAnchor, wrap);

        // sz is in hundredths of a point.
        var szAttr = rPr.Attribute("sz")?.Value;
        var fontSizePt = szAttr is not null && int.TryParse(szAttr, out var szHundredths)
            ? szHundredths / 100.0
            : 0.0;

        var bold = rPr.Attribute("b")?.Value == "1";
        var italic = rPr.Attribute("i")?.Value == "1";
        var uAttr = rPr.Attribute("u")?.Value ?? "";
        var underline = !string.IsNullOrEmpty(uAttr) &&
                        !string.Equals(uAttr, "none", StringComparison.OrdinalIgnoreCase);

        // ── Text solid fill (normal) ────────────────────────────────────────
        var solidFill = rPr.Element(drawingNs + "solidFill");
        var textColor = ReadDrawingSolidFillColor(solidFill, drawingNs);
        WorkbookThemeColorReference? textThemeColor = solidFill is not null &&
            XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, drawingNs, out var tc)
            ? tc : null;
        if (textThemeColor is not null)
            textColor = null;

        // ── WordArt: gradient text fill ────────────────────────────────────
        // <a:rPr><a:gradFill> carries gradient stops; we read the first and last stop colors,
        // including theme-color (schemeClr) stops.  A missing distinct end stop means solid fill
        // — we do NOT synthesise a dummy end stop equal to the start color.
        CellColor? gradEndColor = null;
        WorkbookThemeColorReference? gradEndThemeColor = null;
        long gradAngle = 5400000; // default: top-to-bottom (90°)
        var gradFillEl = rPr.Element(drawingNs + "gradFill");
        if (gradFillEl is not null)
        {
            var (gradStartColor, gradStartThemeColor, gradStopEndColor, gradStopEndThemeColor, _, gradRawAngle) =
                ReadDrawingGradientFillColors(gradFillEl, drawingNs);
            // Use gradient start color/theme as the main text color (replaces solid fill).
            if (textColor is null && textThemeColor is null)
            {
                textColor = gradStartColor;
                textThemeColor = gradStartThemeColor;
            }
            if (textThemeColor is not null)
                textColor = null;
            // Preserve distinct end stop.  When there is no distinct end stop, leave null
            // so the writer emits a solid fill instead of a degenerate 2-stop gradient (WW5).
            gradEndColor = gradStopEndColor;
            gradEndThemeColor = gradStopEndThemeColor;
            gradAngle = gradRawAngle;
        }

        // ── WordArt: text outline (<a:rPr><a:ln>) ─────────────────────────
        CellColor? textOutlineColor = null;
        WorkbookThemeColorReference? textOutlineThemeColor = null;
        var textOutlineWidthPt = 0.0;
        var textLnEl = rPr.Element(drawingNs + "ln");
        if (textLnEl is not null)
        {
            var lnFill = textLnEl.Element(drawingNs + "solidFill");
            textOutlineColor = ReadDrawingSolidFillColor(lnFill, drawingNs);
            if (lnFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(lnFill, drawingNs, out var otc))
            {
                textOutlineThemeColor = otc;
                textOutlineColor = null;
            }
            textOutlineWidthPt = ReadDrawingOutlineWidthPoints(textLnEl);
        }

        // ── WordArt detection ───────────────────────────────────────────────
        var isWordArt = warpPreset is not null || gradFillEl is not null || textLnEl is not null;

        return (fontSizePt, bold, italic, underline, textColor, textThemeColor, hAlign, vAnchor, wrap,
                isWordArt, warpPreset,
                gradEndColor, gradEndThemeColor, gradAngle,
                textOutlineColor, textOutlineThemeColor, textOutlineWidthPt);
    }

    /// <summary>
    /// Reads the font family/typeface from the first run of a shape/text-box <c>&lt;txBody&gt;</c>
    /// element, i.e. <c>&lt;a:rPr&gt;&lt;a:latin typeface="..."/&gt;</c>. Not part of
    /// <see cref="ReadShapeTextFormatting"/>'s return tuple because neither
    /// <c>DrawingShapeModel</c> nor (previously) <c>TextBoxModel</c> tracked a font family --
    /// text boxes now do (backlog textbox-6-2), so this is read alongside that helper's other
    /// first-run fields rather than folded into its already-large tuple.
    /// </summary>
    private static string? ReadShapeTextFontFamily(XElement? txBody, XNamespace drawingNs)
    {
        var firstRun = txBody?.Element(drawingNs + "p")?.Element(drawingNs + "r");
        var typeface = firstRun?.Element(drawingNs + "rPr")?.Element(drawingNs + "latin")?.Attribute("typeface")?.Value;
        return string.IsNullOrWhiteSpace(typeface) ? null : typeface;
    }

    /// <summary>
    /// Reads a connector element (<c>&lt;xdr:cxnSp&gt;</c>) and adds the resulting shape to
    /// <paramref name="shapes"/>.  Connectors use the same <c>spPr/prstGeom</c> structure as
    /// regular shapes (<c>xdr:sp</c>) but have no fill and no txBody; the line element carries
    /// the stroke properties.
    /// </summary>
    private static void ReadCxnSpElement(
        XElement cxnSpElement,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)> hyperlinkRelsById,
        List<XlsxShapePackagePart> shapes)
    {
        var name = ReadNonVisualName(cxnSpElement);
        var title = ReadNonVisualTitle(cxnSpElement);
        var altText = ReadNonVisualDescription(cxnSpElement);
        // R149-app-accessibility-checker-decorative-shapes: see ReadSpElement's identical read.
        var isDecorative = ReadNonVisualDecorative(cxnSpElement);
        var hyperlink = ReadObjectHyperlink(cxnSpElement, spreadsheetDrawingNs, drawingNs, relNs, hyperlinkRelsById);
        var spPr = cxnSpElement.Element(spreadsheetDrawingNs + "spPr");
        var transform = spPr?.Element(drawingNs + "xfrm");
        var groupTransform = ComputeGroupTransform(cxnSpElement, spreadsheetDrawingNs, drawingNs);
        // R54-io-drawing-group-transform-4-1: see ReadSpElement's identical composition.
        var (rotation, flipHorizontal, flipVertical) = ComposeShapeOrientationWithGroups(
            ReadDrawingRotation(transform), ReadDrawingFlipHorizontal(transform), ReadDrawingFlipVertical(transform), groupTransform);
        var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(transform, drawingNs, groupTransform);
        var lnElement = spPr?.Element(drawingNs + "ln");
        var outlineFill = lnElement?.Element(drawingNs + "solidFill");
        var outlineColor = ReadDrawingSolidFillColor(outlineFill, drawingNs);
        var outlineThemeColor = outlineFill is not null &&
                                XlsxDrawingColorReader.TryReadThemeColorReference(outlineFill, drawingNs, out var readOutlineThemeColor)
            ? readOutlineThemeColor
            : (WorkbookThemeColorReference?)null;
        var outlineWidthPoints = ReadDrawingOutlineWidthPoints(lnElement);
        var outlineHasNoFill = lnElement is not null && lnElement.Element(drawingNs + "noFill") is not null;
        var outlineDash = ReadDrawingOutlineDash(lnElement, drawingNs);

        var preset = spPr?
            .Element(drawingNs + "prstGeom")?
            .Attribute("prst")?
            .Value;

        // Default to Line when no prstGeom is present (bare connector with no geometry override).
        var kind = DrawingMlPresetGeometryMap.GetShapeKindOrDefault(preset, DrawingShapeKind.Line);

        // R90-shape-5-3: <xdr:nvCxnSpPr><xdr:cNvCxnSpPr><a:stCxn id=".." idx=".."/><a:endCxn .../>
        // record which shapes (by their cNvPr id) this connector's endpoints are glued to. Previously
        // never read at all, so an attached connector silently became a bare unattached line/geometry
        // on load, with no way to know it had ever been glued to anything.
        var cNvCxnSpPr = cxnSpElement.Element(spreadsheetDrawingNs + "nvCxnSpPr")?.Element(spreadsheetDrawingNs + "cNvCxnSpPr");
        var (startConnectedShapeId, startConnectedShapeConnectionIndex) = ReadConnectionSite(cNvCxnSpPr?.Element(drawingNs + "stCxn"));
        var (endConnectedShapeId, endConnectedShapeConnectionIndex) = ReadConnectionSite(cNvCxnSpPr?.Element(drawingNs + "endCxn"));

        shapes.Add(new XlsxShapePackagePart(
            kind,
            name,
            title,
            altText,
            ReadNearestAnchor(cxnSpElement, transform, groupTransform),
            rotation,
            flipHorizontal,
            flipVertical,
            HasFill: false,           // connectors never have a body fill
            FillColor: null,
            outlineThemeColor is null ? outlineColor : null,
            GradientFillEndColor: null,
            DrawingShapeGradientDirection.DiagonalDown,
            FillThemeColor: null,
            outlineThemeColor,
            HasShadowEffect: false,
            DrawingShapeEffectPreset.None,
            UsesThemeEffects: false,
            ReadNearestAnchorOrderIndex(cxnSpElement),
            xfrmWidthPixels,
            xfrmHeightPixels,
            outlineWidthPoints,
            outlineHasNoFill,
            outlineDash,
            ReadDrawingArrowhead(lnElement, drawingNs, "headEnd"),
            ReadDrawingArrowhead(lnElement, drawingNs, "tailEnd"),
            StartConnectedShapeId: startConnectedShapeId,
            StartConnectedShapeConnectionIndex: startConnectedShapeConnectionIndex,
            EndConnectedShapeId: endConnectedShapeId,
            EndConnectedShapeConnectionIndex: endConnectedShapeConnectionIndex,
            // connectors carry no text
            ShapeText: null,
            ShapeTextFontSizePoints: 0,
            ShapeTextBold: false,
            ShapeTextItalic: false,
            ShapeTextUnderline: false,
            ShapeTextColor: null,
            ShapeTextThemeColor: null,
            DrawingShapeTextHAlign.Left,
            DrawingShapeTextVAnchor.Middle,
            ShapeTextWrap: true,
            // connectors are never WordArt
            IsWordArt: false,
            WarpPreset: null,
            ShapeTextGradientEndColor: null,
            ShapeTextGradientEndThemeColor: null,
            ShapeTextGradientAngle: 5400000,
            ShapeTextOutlineColor: null,
            ShapeTextOutlineThemeColor: null,
            ShapeTextOutlineWidthPoints: 0,
            AdjustValues: ReadShapeAdjustValues(spPr, drawingNs),
            Hyperlink: hyperlink,
            IsDecorative: isDecorative));
    }

    private static bool ReadUsesThemeEffectStyle(
        XElement shapeElement,
        XNamespace drawingNs,
        XNamespace spreadsheetDrawingNs)
    {
        var effectStyleIndex = shapeElement
            .Element(spreadsheetDrawingNs + "style")?
            .Element(drawingNs + "effectRef")?
            .Attribute("idx")?
            .Value;

        return int.TryParse(effectStyleIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
               index > 0;
    }

    private static (string DrawingPath, XDocument DrawingXml)? ReadDrawingContext(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        var worksheetRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return null;

        var worksheetRelsXml = XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
        var drawingTarget = ReadRelationshipTarget(worksheetRelsXml.Root, packageRelNs, drawingRelId);
        if (string.IsNullOrWhiteSpace(drawingTarget))
            return null;

        var drawingPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
        var drawingEntry = archive.GetEntry(drawingPath);
        return drawingEntry is null
            ? null
            : (drawingPath, XlsxPackageXmlEditor.LoadXml(drawingEntry));
    }

    private static string? ReadNonVisualName(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var name = ReadFirstNonVisualAttribute(element, spreadsheetDrawingNs, "name");
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static (string? Name, string? Title, string? Description) ReadNonVisualProperties(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        foreach (var item in element.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            var name = item.Attribute("name")?.Value;
            var title = item.Attribute("title")?.Value;
            var description = item.Attribute("descr")?.Value;
            return (
                string.IsNullOrWhiteSpace(name) ? null : name,
                string.IsNullOrWhiteSpace(title) ? null : title,
                string.IsNullOrWhiteSpace(description) ? null : description);
        }

        return (null, null, null);
    }

    private static string? ReadNonVisualDescription(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        return ReadFirstNonVisualAttribute(element, spreadsheetDrawingNs, "descr");
    }

    private static string? ReadNonVisualTitle(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        return ReadFirstNonVisualAttribute(element, spreadsheetDrawingNs, "title");
    }

    /// <summary>
    /// R90-app-accessibility-checker-5-2: reads Excel's "Mark as decorative" flag from the first
    /// descendant <c>&lt;xdr:cNvPr&gt;</c>'s <c>&lt;a:extLst&gt;&lt;a:ext
    /// uri="{C183D7F6-B498-43B3-948B-1728B52AA6E4}"&gt;&lt;adec:decorative val="1"/&gt;</c>
    /// extension (the same extension Word/PowerPoint/Excel 2019+ use for their shared "Alt Text ->
    /// Mark as decorative" checkbox). Returns <see langword="false"/> when the element/extension is
    /// absent or <c>val</c> is not a truthy value.
    /// </summary>
    private static bool ReadNonVisualDecorative(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        foreach (var cNvPr in element.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            var decorativeVal = cNvPr
                .Element(drawingNs + "extLst")?
                .Elements(drawingNs + "ext")
                .FirstOrDefault(ext => (string?)ext.Attribute("uri") == DrawingMlDecorativeExtensionUri)?
                .Elements()
                .FirstOrDefault(child => child.Name.LocalName == "decorative")?
                .Attribute("val")?.Value;
            return XlsxWorksheetXmlValueParser.IsTruthy(decorativeVal);
        }

        return false;
    }

    /// <summary>
    /// Extension-list URI for the "Mark as decorative" flag on a <c>&lt;xdr:cNvPr&gt;</c>, shared by
    /// <see cref="ReadNonVisualDecorative"/> and <see cref="XlsxWorksheetDrawingObjectWriter"/>.
    /// </summary>
    internal const string DrawingMlDecorativeExtensionUri = "{C183D7F6-B498-43B3-948B-1728B52AA6E4}";

    /// <summary>
    /// R119-io-camera-linked-picture-identity: extension-list URI for the FreeX-specific
    /// <c>fx:linkedPictureSnapshot</c> marker on a reconstructed CellRangeSnapshot picture's
    /// <c>&lt;xdr:grpSp&gt;&lt;xdr:nvGrpSpPr&gt;&lt;xdr:cNvPr&gt;</c>, shared by
    /// <see cref="ReadPictureSnapshotGroupMarker"/> and
    /// <see cref="XlsxWorksheetDrawingObjectWriter.ToPictureSnapshotGroupExtLst"/>. Not a real
    /// Microsoft/ECMA-376 extension -- just a private, FreeX-authored uri under the standard
    /// <c>a:extLst</c>/<c>a:ext</c> extensibility point every OOXML consumer (including Excel
    /// itself) is required to ignore-and-preserve when it doesn't recognize the uri.
    /// </summary>
    internal const string CellRangeSnapshotGroupExtensionUri = "{6E6ECF3A-6EFD-46C8-9E23-4E1B2E9D6DE0}";

    private static double ReadSourceRectangleRatio(XElement? sourceRectangle, string attributeName)
    {
        if (!double.TryParse(
                sourceRectangle?.Attribute(attributeName)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return 0;
        }

        // R80-io-drawing-image-5-2: a NEGATIVE l/t/r/b is a valid, Excel-authored "crop past the
        // image edge" (dragging a crop handle outward pads/zooms-out the picture within its frame) --
        // clamping the floor to 0 here silently discarded that outward crop and made the picture
        // render as if uncropped. Preserve negative insets (mirrored against the +1/-1 = ±100% bound
        // that already applied on the positive side) instead of flooring them to 0.
        return Math.Clamp(value / 100000d, -1, 1);
    }

    private static double ReadDrawingRotation(XElement? transform)
    {
        if (!double.TryParse(transform?.Attribute("rot")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation))
            return 0;
        var degrees = rotation / 60000d;
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static bool ReadDrawingFlipHorizontal(XElement? transform) =>
        XlsxWorksheetXmlValueParser.IsTruthy(transform?.Attribute("flipH")?.Value);

    private static bool ReadDrawingFlipVertical(XElement? transform) =>
        XlsxWorksheetXmlValueParser.IsTruthy(transform?.Attribute("flipV")?.Value);

    /// <summary>
    /// Reads the pre-rotation shape size from <c>&lt;a:xfrm&gt;&lt;a:ext cx cy/&gt;</c>, scaled by
    /// <paramref name="groupTransform"/> so a shape nested inside one or more groups reports the
    /// size it actually renders at once the group's chOff/chExt child-to-parent scale is applied
    /// (the group may stretch or shrink its children relative to their authored local size).
    /// Returns (null, null) when the element is absent.
    /// </summary>
    private static (double? WidthPixels, double? HeightPixels) ReadDrawingXfrmExtent(
        XElement? transform, XNamespace drawingNs, DrawingGroupTransform? groupTransform = null)
    {
        var effectiveGroupTransform = groupTransform ?? DrawingGroupTransform.Identity;
        var ext = transform?.Element(drawingNs + "ext");
        if (ext is null)
            return (null, null);

        var cxStr = ext.Attribute("cx")?.Value;
        var cyStr = ext.Attribute("cy")?.Value;
        if (!double.TryParse(cxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var cxEmu) ||
            !double.TryParse(cyStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var cyEmu))
            return (null, null);

        if (cxEmu < 0 || cyEmu < 0)
            return (null, null);

        // Return the values even when one axis is zero (e.g. a perfectly horizontal line has cy=0).
        // A value of 0 is meaningful: it tells ApplyToShape that the shape is flat on that axis.
        // Both zero → treat as absent (no usable xfrm extent).
        if (cxEmu <= 0 && cyEmu <= 0)
            return (null, null);

        var scaleX = effectiveGroupTransform.ScaleX;
        var scaleY = effectiveGroupTransform.ScaleY;
        return (DrawingMlCoordinateUnits.EmuToPixels(cxEmu * scaleX), DrawingMlCoordinateUnits.EmuToPixels(cyEmu * scaleY));
    }

    /// <summary>
    /// Composed child-space-to-anchor-space transform accumulated from the chain of ancestor
    /// <c>&lt;xdr:grpSp&gt;</c> group transforms enclosing a shape.  Identity (<c>OffsetXEmu</c>/
    /// <c>OffsetYEmu</c> = 0, <c>ScaleX</c>/<c>ScaleY</c> = 1, <c>Matrix</c> = the 2x2 identity)
    /// when the shape is not inside a group. See <see cref="ComputeGroupTransform"/>.
    /// <para>
    /// <c>ScaleX</c>/<c>ScaleY</c> remain the plain magnitude-only chOff/chExt-to-off/ext scale
    /// product (ignoring any ancestor group rotation/flip) — used only for extent/size scaling
    /// (<see cref="ReadDrawingXfrmExtent"/>), where composing a rotated bounding-box size is a
    /// separate, deferred problem (R42-io-drawing-group-transform-3-2/3-3 only cover position).
    /// </para>
    /// <para>
    /// <c>MatrixA</c>/<c>MatrixB</c>/<c>MatrixC</c>/<c>MatrixD</c> together with <c>OffsetXEmu</c>/
    /// <c>OffsetYEmu</c> form the full 2D affine <c>(x, y) -&gt; (MatrixA*x + MatrixB*y +
    /// OffsetXEmu, MatrixC*x + MatrixD*y + OffsetYEmu)</c> used for POSITION mapping — this one
    /// does include every ancestor group's own rotation and flip about its own bounding-box center
    /// (see <see cref="ComputeGroupLevelAffine"/>). When no ancestor group carries rotation or
    /// flip, this reduces to the same diagonal (no-cross-term) transform as
    /// <c>ScaleX</c>/<c>ScaleY</c>, so existing (non-rotated) callers are unaffected.
    /// </para>
    /// </summary>
    // R78-io-drawing-grpsp-move: internal (not private) so XlsxSourceDrawingGeometryRewriter can
    // reuse the exact same composed transform on the WRITE side -- inverting it to translate an
    // edited grouped shape's absolute anchor-space position back into its own local <a:off> inside
    // the group's chOff/chExt child space, instead of silently dropping the edit.
    internal readonly record struct DrawingGroupTransform(
        double OffsetXEmu, double OffsetYEmu, double ScaleX, double ScaleY,
        double MatrixA, double MatrixB, double MatrixC, double MatrixD,
        double OrientationA = 1, double OrientationB = 0, double OrientationC = 0, double OrientationD = 1)
    {
        public static readonly DrawingGroupTransform Identity = new(0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1);
    }

    /// <summary>
    /// Composes the chain of ancestor <c>&lt;xdr:grpSp&gt;</c> group transforms (innermost first)
    /// enclosing <paramref name="element"/> into a single transform that maps the element's own
    /// local <c>&lt;a:xfrm&gt;&lt;a:off&gt;</c> (expressed in the innermost group's child
    /// coordinate space, <c>chOff</c>/<c>chExt</c>) into the outermost group's child space — which
    /// is exactly the space the worksheet anchor (<c>twoCellAnchor</c>/<c>oneCellAnchor</c>/
    /// <c>absoluteAnchor</c>) positions the whole group tree in.
    /// <para>
    /// Each group's own <c>&lt;a:xfrm&gt;</c> carries both <c>off</c>/<c>ext</c> (its position and
    /// size in its parent's space) and <c>chOff</c>/<c>chExt</c> (the origin and extent of the
    /// coordinate space its direct children are authored in, which can differ in scale from
    /// <c>ext</c> — the group can stretch its contents). A child's local coordinate <c>(x, y)</c>
    /// maps to the parent space as <c>off + (x - chOff) * (ext / chExt)</c> — and, when the group
    /// itself carries a <c>rot</c> and/or <c>flipH</c>/<c>flipV</c>, that whole mapped point (and
    /// everything else in the group) is additionally rotated/mirrored about the group's own
    /// off/ext bounding-box center (R42-io-drawing-group-transform-3-2/3-3).
    /// </para>
    /// Returns <see cref="DrawingGroupTransform.Identity"/> when the element has no enclosing
    /// group or any ancestor group lacks a usable <c>chOff</c>/<c>chExt</c>.
    /// </summary>
    internal static DrawingGroupTransform ComputeGroupTransform(XElement element, XNamespace spreadsheetDrawingNs, XNamespace drawingNs)
    {
        double scaleX = 1, scaleY = 1;
        double matrixA = 1, matrixB = 0, matrixC = 0, matrixD = 1, translateX = 0, translateY = 0;
        // R54-io-drawing-group-transform-4-1: a SEPARATE, scale-free rotation/flip-only 2x2 matrix,
        // composed purely from each ancestor group's own rot/flipH/flipV (never its off/ext/chOff/chExt
        // scale) -- this is the piece the shape's own FACING direction (RotationDegrees/FlipHorizontal/
        // FlipVertical) needs to be composed with at the call sites below; MatrixA-D above already mixes
        // in scale and is used only for POSITION mapping.
        double orientationA = 1, orientationB = 0, orientationC = 0, orientationD = 1;
        foreach (var group in element.Ancestors(spreadsheetDrawingNs + "grpSp"))
        {
            var groupXfrm = group.Element(spreadsheetDrawingNs + "grpSpPr")?.Element(drawingNs + "xfrm");
            if (!TryReadGroupXfrm(groupXfrm, drawingNs, out var groupOffX, out var groupOffY,
                    out var groupExtCx, out var groupExtCy,
                    out var groupChOffX, out var groupChOffY,
                    out var groupChExtCx, out var groupChExtCy,
                    out var groupRotationDegrees, out var groupFlipH, out var groupFlipV))
            {
                continue;
            }

            var groupScaleX = groupChExtCx != 0 ? groupExtCx / groupChExtCx : 1;
            var groupScaleY = groupChExtCy != 0 ? groupExtCy / groupChExtCy : 1;
            scaleX *= groupScaleX;
            scaleY *= groupScaleY;

            var (levelA, levelB, levelC, levelD, levelE, levelF) = ComputeGroupLevelAffine(
                groupOffX, groupOffY, groupExtCx, groupExtCy,
                groupChOffX, groupChOffY, groupChExtCx, groupChExtCy,
                groupRotationDegrees, groupFlipH, groupFlipV);

            // Compose this level's own affine AFTER everything accumulated so far: this group sits
            // one step further OUT than every level already folded in (innermost-first iteration),
            // i.e. composed_new(p) = levelAffine(composed_old(p)).
            var newMatrixA = levelA * matrixA + levelB * matrixC;
            var newMatrixB = levelA * matrixB + levelB * matrixD;
            var newMatrixC = levelC * matrixA + levelD * matrixC;
            var newMatrixD = levelC * matrixB + levelD * matrixD;
            var newTranslateX = levelA * translateX + levelB * translateY + levelE;
            var newTranslateY = levelC * translateX + levelD * translateY + levelF;

            matrixA = newMatrixA;
            matrixB = newMatrixB;
            matrixC = newMatrixC;
            matrixD = newMatrixD;
            translateX = newTranslateX;
            translateY = newTranslateY;

            var (levelOrientationA, levelOrientationB, levelOrientationC, levelOrientationD) =
                ToOrientationMatrix(groupRotationDegrees, groupFlipH, groupFlipV);
            (orientationA, orientationB, orientationC, orientationD) = ComposeOrientationMatrices(
                levelOrientationA, levelOrientationB, levelOrientationC, levelOrientationD,
                orientationA, orientationB, orientationC, orientationD);
        }

        return new DrawingGroupTransform(
            translateX, translateY, scaleX, scaleY, matrixA, matrixB, matrixC, matrixD,
            orientationA, orientationB, orientationC, orientationD);
    }

    /// <summary>
    /// Builds the 2x2 linear map for a single flip-then-rotate orientation step: mirror about the
    /// local axes (per <c>flipH</c>/<c>flipV</c>), then rotate by <paramref name="rotationDegrees"/> --
    /// exactly the order <see cref="ApplyGroupLevelPoint"/> already applies when composing position, so
    /// composing this matrix across the ancestor chain (and then with a shape's own local rotation/flip)
    /// yields the shape's true composed facing direction (R54-io-drawing-group-transform-4-1).
    /// </summary>
    private static (double A, double B, double C, double D) ToOrientationMatrix(double rotationDegrees, bool flipH, bool flipV)
    {
        var fH = flipH ? -1.0 : 1.0;
        var fV = flipV ? -1.0 : 1.0;
        if (rotationDegrees == 0)
            return (fH, 0, 0, fV);

        var radians = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        // R(rot) * diag(fH, fV):
        return (cos * fH, -sin * fV, sin * fH, cos * fV);
    }

    /// <summary>
    /// Composes two orientation steps, <paramref name="outer"/> applied AFTER <paramref name="inner"/>
    /// (standard matrix multiplication <c>outer * inner</c>).
    /// </summary>
    private static (double A, double B, double C, double D) ComposeOrientationMatrices(
        double outerA, double outerB, double outerC, double outerD,
        double innerA, double innerB, double innerC, double innerD) => (
        outerA * innerA + outerB * innerC,
        outerA * innerB + outerB * innerD,
        outerC * innerA + outerD * innerC,
        outerC * innerB + outerD * innerD);

    /// <summary>
    /// Decomposes a composed orientation matrix (a flip-then-rotate 2x2 linear map, determinant
    /// &#177;1) back into a single <c>(rotationDegrees, flipHorizontal, flipVertical)</c> triple in the
    /// same <c>R(rot) * diag(flipH?-1:1, flipV?-1:1)</c> canonical form OOXML's own <c>rot</c>/
    /// <c>flipH</c>/<c>flipV</c> attributes use -- so the composed facing direction can be stored back
    /// onto <c>RotationDegrees</c>/<c>FlipHorizontal</c>/<c>FlipVertical</c> exactly like an ordinary
    /// (non-grouped) shape's own local transform already is. A reflection (determinant -1) is always
    /// normalized to FlipHorizontal-only (never FlipVertical, and never both at once) plus a
    /// compensating rotation, matching how Excel itself always authors a single flip axis.
    /// </summary>
    private static (double RotationDegrees, bool FlipHorizontal, bool FlipVertical) DecomposeOrientationMatrix(
        double a, double b, double c, double d)
    {
        var determinant = a * d - b * c;
        if (determinant < 0)
        {
            // Solve R(rot) * diag(-1, 1) = [[-cos,-sin],[-sin,cos]] = [[a,b],[c,d]] for rot:
            // cos(rot) = -a, sin(rot) = -c.
            var rotationDegreesFlipped = Math.Atan2(-c, -a) * 180.0 / Math.PI;
            return (NormalizeDegrees(rotationDegreesFlipped), true, false);
        }

        var rotationDegrees = Math.Atan2(c, a) * 180.0 / Math.PI;
        return (NormalizeDegrees(rotationDegrees), false, false);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        if (normalized < 0)
            normalized += 360.0;
        return normalized;
    }

    /// <summary>
    /// Composes a shape's own local <c>RotationDegrees</c>/<c>FlipHorizontal</c>/<c>FlipVertical</c>
    /// (read from its own <c>&lt;a:xfrm&gt;</c>) with the ancestor group chain's orientation (from
    /// <see cref="ComputeGroupTransform"/>), so a shape's facing direction reflects every enclosing
    /// group's own rotation/flip -- not just its own -- matching real Excel's rendering
    /// (R54-io-drawing-group-transform-4-1). Returns the local values unchanged when there is no
    /// enclosing group (identity orientation).
    /// </summary>
    private static (double RotationDegrees, bool FlipHorizontal, bool FlipVertical) ComposeShapeOrientationWithGroups(
        double localRotationDegrees, bool localFlipHorizontal, bool localFlipVertical, DrawingGroupTransform groupTransform)
    {
        if (groupTransform.OrientationA == 1 && groupTransform.OrientationB == 0 &&
            groupTransform.OrientationC == 0 && groupTransform.OrientationD == 1)
        {
            return (localRotationDegrees, localFlipHorizontal, localFlipVertical);
        }

        var (localA, localB, localC, localD) = ToOrientationMatrix(localRotationDegrees, localFlipHorizontal, localFlipVertical);
        var (composedA, composedB, composedC, composedD) = ComposeOrientationMatrices(
            groupTransform.OrientationA, groupTransform.OrientationB, groupTransform.OrientationC, groupTransform.OrientationD,
            localA, localB, localC, localD);
        return DecomposeOrientationMatrix(composedA, composedB, composedC, composedD);
    }

    /// <summary>
    /// Computes the single-group-level affine <c>(x, y) -&gt; (A*x + B*y + E, C*x + D*y + F)</c>
    /// mapping a point in this group's child coordinate space (<c>chOff</c>/<c>chExt</c>) into its
    /// parent's coordinate space, including the group's own rotation and flip about its own
    /// off/ext bounding-box center (evaluated numerically at three probe points — (0,0), (1,0),
    /// (0,1) — via <see cref="ApplyGroupLevelPoint"/> rather than hand-derived symbolically, since
    /// any 2D affine map is fully determined by its image of the origin and the two unit vectors).
    /// </summary>
    private static (double A, double B, double C, double D, double E, double F) ComputeGroupLevelAffine(
        double offX, double offY, double extCx, double extCy,
        double chOffX, double chOffY, double chExtCx, double chExtCy,
        double rotationDegrees, bool flipH, bool flipV)
    {
        var origin = ApplyGroupLevelPoint(0, 0, offX, offY, extCx, extCy, chOffX, chOffY, chExtCx, chExtCy, rotationDegrees, flipH, flipV);
        var unitX = ApplyGroupLevelPoint(1, 0, offX, offY, extCx, extCy, chOffX, chOffY, chExtCx, chExtCy, rotationDegrees, flipH, flipV);
        var unitY = ApplyGroupLevelPoint(0, 1, offX, offY, extCx, extCy, chOffX, chOffY, chExtCx, chExtCy, rotationDegrees, flipH, flipV);

        return (unitX.X - origin.X, unitY.X - origin.X, unitX.Y - origin.Y, unitY.Y - origin.Y, origin.X, origin.Y);
    }

    /// <summary>
    /// Maps a single point <c>(x, y)</c> from a group's child coordinate space into its parent's
    /// space for exactly one group level: scale by <c>ext/chExt</c> relative to <c>chOff</c>,
    /// mirror about the group's own off/ext box center when <c>flipH</c>/<c>flipV</c>, rotate
    /// about that same center by the group's own <c>rot</c>, then translate by <c>off</c>.
    /// </summary>
    private static (double X, double Y) ApplyGroupLevelPoint(
        double x, double y,
        double offX, double offY, double extCx, double extCy,
        double chOffX, double chOffY, double chExtCx, double chExtCy,
        double rotationDegrees, bool flipH, bool flipV)
    {
        var scaleX = chExtCx != 0 ? extCx / chExtCx : 1;
        var scaleY = chExtCy != 0 ? extCy / chExtCy : 1;

        var localX = (x - chOffX) * scaleX;
        var localY = (y - chOffY) * scaleY;
        if (flipH)
            localX = extCx - localX;
        if (flipV)
            localY = extCy - localY;

        var centerX = extCx / 2;
        var centerY = extCy / 2;
        var dx = localX - centerX;
        var dy = localY - centerY;

        if (rotationDegrees != 0)
        {
            var radians = rotationDegrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var rotatedX = dx * cos - dy * sin;
            var rotatedY = dx * sin + dy * cos;
            dx = rotatedX;
            dy = rotatedY;
        }

        return (offX + dx + centerX, offY + dy + centerY);
    }

    private static bool TryReadGroupXfrm(
        XElement? groupXfrm, XNamespace drawingNs,
        out double offX, out double offY, out double extCx, out double extCy,
        out double chOffX, out double chOffY, out double chExtCx, out double chExtCy,
        out double rotationDegrees, out bool flipH, out bool flipV)
    {
        offX = offY = extCx = extCy = chOffX = chOffY = chExtCx = chExtCy = 0;
        rotationDegrees = 0;
        flipH = false;
        flipV = false;
        if (groupXfrm is null)
            return false;

        var off = groupXfrm.Element(drawingNs + "off");
        var ext = groupXfrm.Element(drawingNs + "ext");
        var chOff = groupXfrm.Element(drawingNs + "chOff");
        var chExt = groupXfrm.Element(drawingNs + "chExt");
        if (off is null || ext is null || chOff is null || chExt is null)
            return false;

        if (!(TryParseEmuAttribute(off, "x", out offX) &&
              TryParseEmuAttribute(off, "y", out offY) &&
              TryParseEmuAttribute(ext, "cx", out extCx) &&
              TryParseEmuAttribute(ext, "cy", out extCy) &&
              TryParseEmuAttribute(chOff, "x", out chOffX) &&
              TryParseEmuAttribute(chOff, "y", out chOffY) &&
              TryParseEmuAttribute(chExt, "cx", out chExtCx) &&
              TryParseEmuAttribute(chExt, "cy", out chExtCy)))
        {
            return false;
        }

        // R42-io-drawing-group-transform-3-2/3-3: the group's OWN rotation/flip (as opposed to a
        // child shape's local rotation/flip, read elsewhere via these same helpers applied to the
        // shape's own xfrm) rotates/mirrors the group's entire rendered content -- including every
        // descendant's computed position -- about the group's own off/ext bounding-box center.
        rotationDegrees = ReadDrawingRotation(groupXfrm);
        flipH = ReadDrawingFlipHorizontal(groupXfrm);
        flipV = ReadDrawingFlipVertical(groupXfrm);
        return true;
    }

    private static bool TryParseEmuAttribute(XElement element, string attributeName, out double value) =>
        double.TryParse(element.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Reads the nearest enclosing worksheet anchor for <paramref name="element"/> and, when the
    /// element is nested inside one or more groups (<paramref name="groupTransform"/> is not
    /// identity), translates the anchor's from-cell sub-cell offset by the shape's own local
    /// <c>&lt;a:xfrm&gt;&lt;a:off&gt;</c> composed through the group chain — so a grouped shape is
    /// positioned at its true worksheet location instead of collapsing onto the whole group's
    /// outer anchor (see <see cref="ComputeGroupTransform"/>).
    /// </summary>
    private static XlsxDrawingAnchor? ReadNearestAnchor(XElement element, XElement? transform, DrawingGroupTransform groupTransform)
    {
        var anchor = ReadNearestAnchor(element);
        if (anchor is null || groupTransform == DrawingGroupTransform.Identity)
        {
            return anchor;
        }

        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var off = transform?.Element(drawingNs + "off");
        var localOffXEmu = ReadEmuAttributeOrZero(off, "x");
        var localOffYEmu = ReadEmuAttributeOrZero(off, "y");

        // Map the shape's own local off (in the innermost group's child space) through the
        // composed group transform -- the full affine, including every ancestor group's own
        // rotation and flip about its own bounding-box center (R42-io-drawing-group-transform-3-2/
        // 3-3) -- into the outermost group's child space, which is the same space the worksheet
        // anchor positions the group tree in.
        var absoluteOffXEmu = groupTransform.MatrixA * localOffXEmu + groupTransform.MatrixB * localOffYEmu + groupTransform.OffsetXEmu;
        var absoluteOffYEmu = groupTransform.MatrixC * localOffXEmu + groupTransform.MatrixD * localOffYEmu + groupTransform.OffsetYEmu;

        var deltaXPixels = DrawingMlCoordinateUnits.EmuToPixels(absoluteOffXEmu);
        var deltaYPixels = DrawingMlCoordinateUnits.EmuToPixels(absoluteOffYEmu);

        return anchor with
        {
            FromColumnOffset = anchor.FromColumnOffset + deltaXPixels,
            FromRowOffset = anchor.FromRowOffset + deltaYPixels,
        };
    }

    private static double ReadEmuAttributeOrZero(XElement? element, string attributeName) =>
        double.TryParse(element?.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    /// <summary>
    /// Reads the outline width in points from <c>&lt;a:ln w="..."/&gt;</c>.
    /// The <c>w</c> attribute is in EMU. Returns 0 when absent.
    /// </summary>
    private static double ReadDrawingOutlineWidthPoints(XElement? lnElement)
    {
        var wValue = lnElement?.Attribute("w")?.Value;
        if (!double.TryParse(wValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var emu) || emu <= 0)
            return 0;
        return emu / DrawingMlCoordinateUnits.EmuPerPoint;
    }

    /// <summary>
    /// Reads the outline dash style from <c>&lt;a:ln&gt;&lt;a:prstDash val="..."/&gt;</c>.
    /// </summary>
    private static DrawingShapeOutlineDash ReadDrawingOutlineDash(XElement? lnElement, XNamespace drawingNs)
    {
        var val = lnElement?.Element(drawingNs + "prstDash")?.Attribute("val")?.Value;
        return val switch
        {
            "dash" => DrawingShapeOutlineDash.Dash,
            "dot" => DrawingShapeOutlineDash.Dot,
            "dashDot" => DrawingShapeOutlineDash.DashDot,
            "lgDash" => DrawingShapeOutlineDash.LongDash,
            "lgDashDot" => DrawingShapeOutlineDash.LongDashDot,
            "lgDashDotDot" => DrawingShapeOutlineDash.LongDashDotDot,
            "sysDash" => DrawingShapeOutlineDash.SystemDash,
            "sysDot" => DrawingShapeOutlineDash.SystemDot,
            "sysDashDot" => DrawingShapeOutlineDash.SystemDashDot,
            _ => DrawingShapeOutlineDash.Solid
        };
    }

    private static CellColor? ReadDrawingSolidFillColor(XElement? solidFill, XNamespace drawingNs)
    {
        var value = solidFill?
            .Element(drawingNs + "srgbClr")?
            .Attribute("val")?
            .Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length != 6)
            return null;
        return byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
               byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
               byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new CellColor(r, g, b)
            : null;
    }

    private static (
        CellColor? StartColor, WorkbookThemeColorReference? StartThemeColor,
        CellColor? EndColor,   WorkbookThemeColorReference? EndThemeColor,
        DrawingShapeGradientDirection Direction, long RawAngle) ReadDrawingGradientFillColors(
        XElement? gradientFill,
        XNamespace drawingNs)
    {
        if (gradientFill is null)
            return (null, null, null, null, DrawingShapeGradientDirection.DiagonalDown, 5400000);

        // Read each gradient stop, capturing both concrete RGB and theme-color references.
        var stops = gradientFill
            .Element(drawingNs + "gsLst")?
            .Elements(drawingNs + "gs")
            .Select(gs =>
            {
                var pos = int.TryParse(gs.Attribute("pos")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
                WorkbookThemeColorReference? themeColor = null;
                CellColor? color = null;
                if (XlsxDrawingColorReader.TryReadThemeColorReference(gs, drawingNs, out var tc))
                    themeColor = tc;
                else
                    color = ReadDrawingSolidFillColor(gs, drawingNs);
                return new { pos, color, themeColor };
            })
            .Where(s => s.color is not null || s.themeColor is not null)
            .OrderBy(s => s.pos)
            .ToList();

        if (stops is not { Count: >= 2 })
        {
            var first = stops is { Count: > 0 } ? stops[0] : null;
            return (first?.color, first?.themeColor, null, null, DrawingShapeGradientDirection.DiagonalDown, 5400000);
        }

        var rawAngle = long.TryParse(
            gradientFill.Element(drawingNs + "lin")?.Attribute("ang")?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var ang)
            ? ang
            : 5400000L;

        return (stops[0].color, stops[0].themeColor,
                stops[^1].color, stops[^1].themeColor,
                ReadDrawingGradientFillDirection(gradientFill, drawingNs), rawAngle);
    }

    private static string? ReadRelationshipTarget(XElement? relationshipRoot, XNamespace packageRelNs, string relationshipId)
    {
        if (relationshipRoot is null)
            return null;

        foreach (var relationship in relationshipRoot.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
                return relationship.Attribute("Target")?.Value;
        }

        return null;
    }

    private static string? ReadFirstNonVisualAttribute(XElement element, XNamespace spreadsheetDrawingNs, string attributeName)
    {
        foreach (var item in element.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            var value = item.Attribute(attributeName)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static CellColor? FirstColor(IReadOnlyList<CellColor?>? colors) =>
        colors is { Count: > 0 } ? colors[0] : null;

    private static DrawingShapeGradientDirection ReadDrawingGradientFillDirection(
        XElement? gradientFill,
        XNamespace drawingNs)
    {
        if (!long.TryParse(
                gradientFill?.Element(drawingNs + "lin")?.Attribute("ang")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var angle))
        {
            return DrawingShapeGradientDirection.DiagonalDown;
        }

        return NearestCardinalGradientDirection(NormalizeDrawingAngle(angle));
    }

    /// <summary>
    /// <see cref="DrawingShapeGradientDirection"/> can only represent the four cardinal angles
    /// (0/90/180/270 degrees, matching <c>XlsxWorksheetDrawingObjectWriter.ToGradientFillAngle</c>'s
    /// inverse mapping) — any other angle from the source file (e.g. a 33-degree gradient) must be
    /// snapped to whichever of those four buckets is actually CLOSEST, not unconditionally forced to
    /// DiagonalDown (90 degrees) regardless of how far away it really is (R51-io-picture-fill-shape-3-2).
    /// </summary>
    private static DrawingShapeGradientDirection NearestCardinalGradientDirection(long normalizedAngle)
    {
        const long fullTurn = 21600000;
        ReadOnlySpan<(long Angle, DrawingShapeGradientDirection Direction)> cardinals =
        [
            (0L, DrawingShapeGradientDirection.Horizontal),
            (5400000L, DrawingShapeGradientDirection.DiagonalDown),
            (10800000L, DrawingShapeGradientDirection.DiagonalUp),
            (16200000L, DrawingShapeGradientDirection.Vertical),
        ];

        var best = DrawingShapeGradientDirection.DiagonalDown;
        var bestDistance = long.MaxValue;
        foreach (var (candidateAngle, direction) in cardinals)
        {
            var diff = Math.Abs(normalizedAngle - candidateAngle);
            var circularDistance = Math.Min(diff, fullTurn - diff);
            if (circularDistance < bestDistance)
            {
                bestDistance = circularDistance;
                best = direction;
            }
        }

        return best;
    }

    private static long NormalizeDrawingAngle(long angle)
    {
        const long fullTurn = 21600000;
        var normalized = angle % fullTurn;
        return normalized < 0 ? normalized + fullTurn : normalized;
    }

    private static DrawingShapeEffectPreset ReadDrawingShapeEffectPreset(
        XElement? shapeProperties,
        XNamespace drawingNs)
    {
        var effectList = shapeProperties?.Element(drawingNs + "effectLst");
        if (effectList?.Element(drawingNs + "outerShdw") is not null)
            return DrawingShapeEffectPreset.Shadow;
        if (effectList?.Element(drawingNs + "innerShdw") is not null)
            return DrawingShapeEffectPreset.InnerShadow;
        if (effectList?.Element(drawingNs + "reflection") is not null)
            return DrawingShapeEffectPreset.Reflection;
        if (effectList?.Element(drawingNs + "glow") is not null)
            return DrawingShapeEffectPreset.Glow;
        if (effectList?.Element(drawingNs + "softEdge") is not null)
            return DrawingShapeEffectPreset.SoftEdges;
        if (shapeProperties?.Element(drawingNs + "sp3d")?.Element(drawingNs + "bevelT") is not null)
            return DrawingShapeEffectPreset.Bevel;
        if (shapeProperties?.Element(drawingNs + "scene3d")?.Element(drawingNs + "camera") is not null)
            return DrawingShapeEffectPreset.ThreeDRotation;

        return DrawingShapeEffectPreset.None;
    }

    /// <summary>
    /// Reads one arrowhead descriptor from a <c>&lt;a:headEnd&gt;</c> or <c>&lt;a:tailEnd&gt;</c> element.
    /// Returns <see langword="null"/> when the element is absent or has <c>type="none"</c> (or no type attribute).
    /// </summary>
    private static DrawingArrowhead? ReadDrawingArrowhead(XElement? lnElement, XNamespace drawingNs, string elementName)
    {
        var element = lnElement?.Element(drawingNs + elementName);
        if (element is null)
            return null;

        var typeAttr = element.Attribute("type")?.Value;
        var type = typeAttr switch
        {
            "triangle" => DrawingArrowheadType.Triangle,
            "arrow" => DrawingArrowheadType.Arrow,
            "stealth" => DrawingArrowheadType.Stealth,
            "diamond" => DrawingArrowheadType.Diamond,
            "oval" => DrawingArrowheadType.Oval,
            _ => DrawingArrowheadType.None // "none" or absent
        };
        if (type == DrawingArrowheadType.None)
            return null;

        var w = element.Attribute("w")?.Value switch
        {
            "sm" => DrawingArrowheadSize.Small,
            "lg" => DrawingArrowheadSize.Large,
            _ => DrawingArrowheadSize.Medium
        };
        var len = element.Attribute("len")?.Value switch
        {
            "sm" => DrawingArrowheadSize.Small,
            "lg" => DrawingArrowheadSize.Large,
            _ => DrawingArrowheadSize.Medium
        };
        return new DrawingArrowhead(type, w, len);
    }

    /// <summary>
    /// R90-shape-5-3: reads a connector's <c>&lt;a:stCxn id="..." idx="..."/&gt;</c> or
    /// <c>&lt;a:endCxn id="..." idx="..."/&gt;</c> connection-site element (child of
    /// <c>&lt;xdr:cNvCxnSpPr&gt;</c>). Returns (null, null) when <paramref name="connectionElement"/>
    /// is absent or its <c>id</c>/<c>idx</c> attributes are missing/unparsable -- an unattached
    /// connector endpoint.
    /// </summary>
    private static (int? ShapeId, int? ConnectionIndex) ReadConnectionSite(XElement? connectionElement)
    {
        if (connectionElement is null)
            return (null, null);

        var id = int.TryParse(connectionElement.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)
            ? parsedId
            : (int?)null;
        var idx = int.TryParse(connectionElement.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIdx)
            ? parsedIdx
            : (int?)null;
        return id is null ? (null, null) : (id, idx);
    }

    /// <summary>
    /// Reads a preset geometry's adjust-handle values from <c>&lt;a:prstGeom&gt;&lt;a:avLst&gt;&lt;a:gd .../&gt;</c>
    /// (R78-io-shape-geometry-5-3). Returns <see langword="null"/> when there is no <c>prstGeom</c>/<c>avLst</c>
    /// or it carries no <c>gd</c> children (the common case — geometry defaults apply), so the
    /// customized handle only round-trips when Excel actually authored one.
    /// </summary>
    private static IReadOnlyList<DrawingShapeAdjustValue>? ReadShapeAdjustValues(XElement? spPr, XNamespace drawingNs)
    {
        var avLst = spPr?.Element(drawingNs + "prstGeom")?.Element(drawingNs + "avLst");
        if (avLst is null)
            return null;

        var values = new List<DrawingShapeAdjustValue>();
        foreach (var gd in avLst.Elements(drawingNs + "gd"))
        {
            var name = gd.Attribute("name")?.Value;
            var formula = gd.Attribute("fmla")?.Value;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(formula))
                values.Add(new DrawingShapeAdjustValue(name, formula));
        }

        return values.Count > 0 ? values : null;
    }

}
