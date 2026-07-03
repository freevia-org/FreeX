using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal sealed record XlsxChartPackagePart(XDocument Xml, XDocument? Relationships, string? Name, XlsxDrawingAnchor? Anchor);

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
    int DrawingOrderIndex);

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
    int DrawingOrderIndex);

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
    double ShapeTextOutlineWidthPoints);

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
        var pictures = ReadPictureParts(archive, drawingPath, drawingXml, drawingRelsXml);
        var (textBoxes, shapes) = ReadShapeParts(drawingXml);
        return new XlsxWorksheetDrawingPackageParts(charts, pictures, textBoxes, shapes);
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
        var relationshipTargets = ReadRelationshipTargetsById(drawingRelsXml.Root, packageRelNs);

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

            charts.Add(new XlsxChartPackagePart(
                XlsxPackageXmlEditor.LoadXml(chartEntry),
                chartRelationships,
                ReadNonVisualName(chartElement),
                ReadNearestAnchor(chartElement)));
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
        var pictures = new List<XlsxPicturePackagePart>(relationshipTargets.Count);

        foreach (var pictureElement in drawingXml.Descendants(spreadsheetDrawingNs + "pic"))
        {
            var imageRelId = ReadPictureEmbedRelationshipId(pictureElement, drawingNs, relNs);
            if (string.IsNullOrWhiteSpace(imageRelId))
                continue;

            if (!relationshipTargets.TryGetValue(imageRelId, out var imageTarget))
                continue;

            var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, imageTarget);
            var imageEntry = archive.GetEntry(imagePath);
            if (imageEntry is null)
                continue;

            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");
            var (name, title, altText) = ReadNonVisualProperties(pictureElement);
            var anchorElement = FindNearestAnchorElement(pictureElement, spreadsheetDrawingNs);

            pictures.Add(new XlsxPicturePackagePart(
                ReadEntryBytes(imageEntry),
                XlsxPackagePath.GetImageContentType(imagePath),
                name,
                title,
                altText,
                anchorElement is null ? null : TryReadAnchor(anchorElement, spreadsheetDrawingNs),
                ReadDrawingRotation(pictureElement.Element(spreadsheetDrawingNs + "spPr")?.Element(drawingNs + "xfrm")),
                ReadDrawingFlipHorizontal(pictureElement.Element(spreadsheetDrawingNs + "spPr")?.Element(drawingNs + "xfrm")),
                ReadDrawingFlipVertical(pictureElement.Element(spreadsheetDrawingNs + "spPr")?.Element(drawingNs + "xfrm")),
                ReadSourceRectangleRatio(sourceRectangle, "l"),
                ReadSourceRectangleRatio(sourceRectangle, "t"),
                ReadSourceRectangleRatio(sourceRectangle, "r"),
                ReadSourceRectangleRatio(sourceRectangle, "b"),
                anchorElement is null ? -1 : ReadAnchorOrderIndex(anchorElement, spreadsheetDrawingNs)));
        }

        return pictures;
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

    private static string? ReadPictureEmbedRelationshipId(XElement pictureElement, XNamespace drawingNs, XNamespace relNs)
    {
        foreach (var blip in pictureElement.Descendants(drawingNs + "blip"))
        {
            var relationshipId = blip.Attribute(relNs + "embed")?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipId))
                return relationshipId;
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
        XDocument drawingXml)
    {
        var textBoxes = new List<XlsxTextBoxPackagePart>();
        var shapes = new List<XlsxShapePackagePart>();
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        foreach (var shapeElement in drawingXml.Descendants(spreadsheetDrawingNs + "sp"))
        {
            if (shapeElement.Ancestors(markupCompatNs + "Fallback").Any())
                continue;

            ReadSpElement(shapeElement, spreadsheetDrawingNs, drawingNs, textBoxes, shapes);
        }

        // Also read connectors (<xdr:cxnSp>) — they use the same spPr/prstGeom structure as sp.
        foreach (var cxnSpElement in drawingXml.Descendants(spreadsheetDrawingNs + "cxnSp"))
        {
            if (cxnSpElement.Ancestors(markupCompatNs + "Fallback").Any())
                continue;

            ReadCxnSpElement(cxnSpElement, spreadsheetDrawingNs, drawingNs, shapes);
        }

        return (textBoxes, shapes);
    }

    private static void ReadSpElement(
        XElement shapeElement,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        List<XlsxTextBoxPackagePart> textBoxes,
        List<XlsxShapePackagePart> shapes)
    {
        var name = ReadNonVisualName(shapeElement);
        var title = ReadNonVisualTitle(shapeElement);
        var altText = ReadNonVisualDescription(shapeElement);
        var spPr = shapeElement.Element(spreadsheetDrawingNs + "spPr");
        var transform = spPr?.Element(drawingNs + "xfrm");
        var rotation = ReadDrawingRotation(transform);
        var flipHorizontal = ReadDrawingFlipHorizontal(transform);
        var flipVertical = ReadDrawingFlipVertical(transform);
        var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(transform, drawingNs);
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
        var text = string.Concat(txBodyElement?.Descendants(drawingNs + "t").Select(t => t.Value) ?? []);

        if (isTxBox && !string.IsNullOrEmpty(text))
        {
            // True text-box: forward to textBoxes list (original behaviour).
            textBoxes.Add(new XlsxTextBoxPackagePart(
                text,
                name,
                title,
                altText,
                ReadNearestAnchor(shapeElement),
                rotation,
                flipHorizontal,
                flipVertical,
                hasFill,
                fillThemeColor is null ? fillColor : null,
                outlineThemeColor is null ? outlineColor : null,
                fillThemeColor,
                outlineThemeColor,
                ReadNearestAnchorOrderIndex(shapeElement)));
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
            ReadNearestAnchor(shapeElement),
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
            textOutlineWidthPt));
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
    /// Reads a connector element (<c>&lt;xdr:cxnSp&gt;</c>) and adds the resulting shape to
    /// <paramref name="shapes"/>.  Connectors use the same <c>spPr/prstGeom</c> structure as
    /// regular shapes (<c>xdr:sp</c>) but have no fill and no txBody; the line element carries
    /// the stroke properties.
    /// </summary>
    private static void ReadCxnSpElement(
        XElement cxnSpElement,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        List<XlsxShapePackagePart> shapes)
    {
        var name = ReadNonVisualName(cxnSpElement);
        var title = ReadNonVisualTitle(cxnSpElement);
        var altText = ReadNonVisualDescription(cxnSpElement);
        var spPr = cxnSpElement.Element(spreadsheetDrawingNs + "spPr");
        var transform = spPr?.Element(drawingNs + "xfrm");
        var rotation = ReadDrawingRotation(transform);
        var flipHorizontal = ReadDrawingFlipHorizontal(transform);
        var flipVertical = ReadDrawingFlipVertical(transform);
        var (xfrmWidthPixels, xfrmHeightPixels) = ReadDrawingXfrmExtent(transform, drawingNs);
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
        shapes.Add(new XlsxShapePackagePart(
            kind,
            name,
            title,
            altText,
            ReadNearestAnchor(cxnSpElement),
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
            ShapeTextOutlineWidthPoints: 0));
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

        return Math.Clamp(value / 100000d, 0, 1);
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
    /// Reads the pre-rotation shape size from <c>&lt;a:xfrm&gt;&lt;a:ext cx cy/&gt;</c>.
    /// Returns (null, null) when the element is absent.
    /// </summary>
    private static (double? WidthPixels, double? HeightPixels) ReadDrawingXfrmExtent(XElement? transform, XNamespace drawingNs)
    {
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

        return (DrawingMlUnits.EmuToPixels(cxEmu), DrawingMlUnits.EmuToPixels(cyEmu));
    }

    /// <summary>
    /// Reads the outline width in points from <c>&lt;a:ln w="..."/&gt;</c>.
    /// The <c>w</c> attribute is in EMU. Returns 0 when absent.
    /// </summary>
    private static double ReadDrawingOutlineWidthPoints(XElement? lnElement)
    {
        var wValue = lnElement?.Attribute("w")?.Value;
        if (!double.TryParse(wValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var emu) || emu <= 0)
            return 0;
        return emu / DrawingMlUnits.EmuPerPoint;
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

        return NormalizeDrawingAngle(angle) switch
        {
            0 => DrawingShapeGradientDirection.Horizontal,
            5400000 => DrawingShapeGradientDirection.DiagonalDown,
            10800000 => DrawingShapeGradientDirection.DiagonalUp,
            16200000 => DrawingShapeGradientDirection.Vertical,
            _ => DrawingShapeGradientDirection.DiagonalDown
        };
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

}
