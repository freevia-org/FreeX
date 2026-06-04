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
    CellColor? FillColor,
    CellColor? OutlineColor,
    CellColor? GradientFillEndColor,
    DrawingShapeGradientDirection GradientFillDirection,
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor,
    bool HasShadowEffect,
    DrawingShapeEffectPreset EffectPreset,
    int DrawingOrderIndex);

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

        foreach (var chartElement in drawingXml.Descendants().Where(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart"))
        {
            var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId))
                continue;

            if (!relationshipTargets.TryGetValue(chartRelId, out var chartTarget))
                continue;

            var chartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartTarget);
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

    private static IReadOnlyList<XlsxPicturePackagePart> ReadPictureParts(
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

    private static (IReadOnlyList<XlsxTextBoxPackagePart> TextBoxes, IReadOnlyList<XlsxShapePackagePart> Shapes) ReadShapeParts(
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

            var name = ReadNonVisualName(shapeElement);
            var title = ReadNonVisualTitle(shapeElement);
            var altText = ReadNonVisualDescription(shapeElement);
            var spPr = shapeElement.Element(spreadsheetDrawingNs + "spPr");
            var rotation = ReadDrawingRotation(spPr?.Element(drawingNs + "xfrm"));
            var gradientFill = ReadDrawingGradientFillColors(spPr?.Element(drawingNs + "gradFill"), drawingNs);
            var solidFill = spPr?.Element(drawingNs + "solidFill");
            var outlineFill = spPr?.Element(drawingNs + "ln")?.Element(drawingNs + "solidFill");
            var fillColor = gradientFill.StartColor ?? ReadDrawingSolidFillColor(solidFill, drawingNs);
            var outlineColor = ReadDrawingSolidFillColor(outlineFill, drawingNs);
            var fillThemeColor = solidFill is not null &&
                                 XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, drawingNs, out var readFillThemeColor)
                ? readFillThemeColor
                : (WorkbookThemeColorReference?)null;
            var outlineThemeColor = outlineFill is not null &&
                                    XlsxDrawingColorReader.TryReadThemeColorReference(outlineFill, drawingNs, out var readOutlineThemeColor)
                ? readOutlineThemeColor
                : (WorkbookThemeColorReference?)null;
            var effectPreset = ReadDrawingShapeEffectPreset(spPr?.Element(drawingNs + "effectLst"), drawingNs);
            var hasShadowEffect = effectPreset == DrawingShapeEffectPreset.Shadow;
            var text = string.Concat(shapeElement
                .Element(spreadsheetDrawingNs + "txBody")?
                .Descendants(drawingNs + "t")
                .Select(t => t.Value) ?? []);

            if (!string.IsNullOrEmpty(text))
            {
                textBoxes.Add(new XlsxTextBoxPackagePart(
                    text,
                    name,
                    title,
                    altText,
                    ReadNearestAnchor(shapeElement),
                    rotation,
                    fillThemeColor is null ? fillColor : null,
                    outlineThemeColor is null ? outlineColor : null,
                    fillThemeColor,
                    outlineThemeColor,
                    ReadNearestAnchorOrderIndex(shapeElement)));
                continue;
            }

            var preset = spPr?
                .Element(drawingNs + "prstGeom")?
                .Attribute("prst")?
                .Value;
            if (ToDrawingShapeKind(preset) is { } kind)
                shapes.Add(new XlsxShapePackagePart(
                    kind,
                    name,
                    title,
                    altText,
                    ReadNearestAnchor(shapeElement),
                    rotation,
                    fillThemeColor is null ? fillColor : null,
                    outlineThemeColor is null ? outlineColor : null,
                    gradientFill.EndColor,
                    gradientFill.Direction,
                    fillThemeColor,
                    outlineThemeColor,
                    hasShadowEffect,
                    effectPreset,
                    ReadNearestAnchorOrderIndex(shapeElement)));
        }

        return (textBoxes, shapes);
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
        var drawingTarget = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, drawingRelId, StringComparison.Ordinal))?
            .Attribute("Target")?
            .Value;
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
        var name = element
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .Select(item => item.Attribute("name")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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
        return element
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .Select(item => item.Attribute("descr")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ReadNonVisualTitle(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        return element
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .Select(item => item.Attribute("title")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private static (CellColor? StartColor, CellColor? EndColor, DrawingShapeGradientDirection Direction) ReadDrawingGradientFillColors(
        XElement? gradientFill,
        XNamespace drawingNs)
    {
        var colors = gradientFill?
            .Element(drawingNs + "gsLst")?
            .Elements(drawingNs + "gs")
            .Select(gs => new
            {
                Position = int.TryParse(gs.Attribute("pos")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)
                    ? pos
                    : 0,
                Color = ReadDrawingSolidFillColor(gs, drawingNs)
            })
            .Where(item => item.Color is not null)
            .OrderBy(item => item.Position)
            .Select(item => item.Color)
            .ToList();

        return colors is { Count: >= 2 }
            ? (colors[0], colors[^1], ReadDrawingGradientFillDirection(gradientFill, drawingNs))
            : (colors?.FirstOrDefault(), null, DrawingShapeGradientDirection.DiagonalDown);
    }

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
        XElement? effectList,
        XNamespace drawingNs)
    {
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

        return DrawingShapeEffectPreset.None;
    }

    private static DrawingShapeKind? ToDrawingShapeKind(string? preset) =>
        preset switch
        {
            "rect" or "roundRect" => DrawingShapeKind.Rectangle,
            "ellipse" => DrawingShapeKind.Ellipse,
            "line" => DrawingShapeKind.Line,
            _ => null
        };

}
