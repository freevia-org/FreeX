using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal sealed record XlsxChartPackagePart(XDocument Xml, XDocument? Relationships, string? Name, XlsxDrawingAnchor? Anchor);

internal sealed record XlsxPicturePackagePart(
    byte[] ImageBytes,
    string ContentType,
    string? Name,
    string? Title,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

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
    WorkbookThemeColorReference? OutlineThemeColor);

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
    WorkbookThemeColorReference? FillThemeColor,
    WorkbookThemeColorReference? OutlineThemeColor,
    bool HasShadowEffect);

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
    public static IReadOnlyList<XlsxChartPackagePart> ReadChartParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var charts = new List<XlsxChartPackagePart>();
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var drawingContext = ReadDrawingContext(archive, worksheetPath, worksheetXml);
        if (drawingContext is null)
            return charts;

        var (drawingPath, drawingXml) = drawingContext.Value;
        var chartElements = drawingXml
            .Descendants(chartNs + "chart")
            .ToList();
        if (chartElements.Count == 0)
            return charts;

        var drawingRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(drawingPath));
        if (drawingRelsEntry is null)
            return charts;

        var drawingRelsXml = XlsxPackageXmlEditor.LoadXml(drawingRelsEntry);
        foreach (var chartElement in chartElements)
        {
            var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId))
                continue;

            var chartTarget = drawingRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, chartRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(chartTarget))
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

    public static IReadOnlyList<XlsxPicturePackagePart> ReadPictureParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var pictures = new List<XlsxPicturePackagePart>();
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingContext = ReadDrawingContext(archive, worksheetPath, worksheetXml);
        if (drawingContext is null)
            return pictures;

        var (drawingPath, drawingXml) = drawingContext.Value;
        var drawingRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(drawingPath));
        if (drawingRelsEntry is null)
            return pictures;

        var drawingRelsXml = XlsxPackageXmlEditor.LoadXml(drawingRelsEntry);
        foreach (var pictureElement in drawingXml.Descendants(spreadsheetDrawingNs + "pic"))
        {
            var imageRelId = pictureElement
                .Descendants(drawingNs + "blip")
                .Select(element => element.Attribute(relNs + "embed")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(imageRelId))
                continue;

            var imageTarget = drawingRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, imageRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(imageTarget))
                continue;

            var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, imageTarget);
            var imageEntry = archive.GetEntry(imagePath);
            if (imageEntry is null)
                continue;

            using var imageStream = imageEntry.Open();
            using var ms = new MemoryStream();
            imageStream.CopyTo(ms);

            var name = ReadNonVisualName(pictureElement);
            var title = ReadNonVisualTitle(pictureElement);
            var altText = ReadNonVisualDescription(pictureElement);
            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");

            pictures.Add(new XlsxPicturePackagePart(
                ms.ToArray(),
                XlsxPackagePath.GetImageContentType(imagePath),
                name,
                title,
                altText,
                ReadNearestAnchor(pictureElement),
                ReadSourceRectangleRatio(sourceRectangle, "l"),
                ReadSourceRectangleRatio(sourceRectangle, "t"),
                ReadSourceRectangleRatio(sourceRectangle, "r"),
                ReadSourceRectangleRatio(sourceRectangle, "b")));
        }

        return pictures;
    }

    public static (IReadOnlyList<XlsxTextBoxPackagePart> TextBoxes, IReadOnlyList<XlsxShapePackagePart> Shapes) ReadShapeParts(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var textBoxes = new List<XlsxTextBoxPackagePart>();
        var shapes = new List<XlsxShapePackagePart>();
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingContext = ReadDrawingContext(archive, worksheetPath, worksheetXml);
        if (drawingContext is null)
            return (textBoxes, shapes);

        foreach (var shapeElement in drawingContext.Value.DrawingXml.Descendants(spreadsheetDrawingNs + "sp"))
        {
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
            var hasShadowEffect = spPr?
                .Element(drawingNs + "effectLst")?
                .Element(drawingNs + "outerShdw") is not null;
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
                    outlineThemeColor));
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
                    fillThemeColor,
                    outlineThemeColor,
                    hasShadowEffect));
        }

        return (textBoxes, shapes);
    }

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
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        foreach (var chartElement in drawingXml.Descendants(chartNs + "chart"))
        {
            var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId))
                continue;

            var chartTarget = drawingRelsXml.Root
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, chartRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(chartTarget))
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
        var pictures = new List<XlsxPicturePackagePart>();
        if (drawingRelsXml?.Root is null)
            return pictures;

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        foreach (var pictureElement in drawingXml.Descendants(spreadsheetDrawingNs + "pic"))
        {
            var imageRelId = pictureElement
                .Descendants(drawingNs + "blip")
                .Select(element => element.Attribute(relNs + "embed")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(imageRelId))
                continue;

            var imageTarget = drawingRelsXml.Root
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, imageRelId, StringComparison.Ordinal))?
                .Attribute("Target")?
                .Value;
            if (string.IsNullOrWhiteSpace(imageTarget))
                continue;

            var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, imageTarget);
            var imageEntry = archive.GetEntry(imagePath);
            if (imageEntry is null)
                continue;

            using var imageStream = imageEntry.Open();
            using var ms = new MemoryStream();
            imageStream.CopyTo(ms);

            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");

            pictures.Add(new XlsxPicturePackagePart(
                ms.ToArray(),
                XlsxPackagePath.GetImageContentType(imagePath),
                ReadNonVisualName(pictureElement),
                ReadNonVisualTitle(pictureElement),
                ReadNonVisualDescription(pictureElement),
                ReadNearestAnchor(pictureElement),
                ReadSourceRectangleRatio(sourceRectangle, "l"),
                ReadSourceRectangleRatio(sourceRectangle, "t"),
                ReadSourceRectangleRatio(sourceRectangle, "r"),
                ReadSourceRectangleRatio(sourceRectangle, "b")));
        }

        return pictures;
    }

    private static (IReadOnlyList<XlsxTextBoxPackagePart> TextBoxes, IReadOnlyList<XlsxShapePackagePart> Shapes) ReadShapeParts(
        XDocument drawingXml)
    {
        var textBoxes = new List<XlsxTextBoxPackagePart>();
        var shapes = new List<XlsxShapePackagePart>();
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        foreach (var shapeElement in drawingXml.Descendants(spreadsheetDrawingNs + "sp"))
        {
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
            var hasShadowEffect = spPr?
                .Element(drawingNs + "effectLst")?
                .Element(drawingNs + "outerShdw") is not null;
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
                    outlineThemeColor));
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
                    fillThemeColor,
                    outlineThemeColor,
                    hasShadowEffect));
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

    private static (CellColor? StartColor, CellColor? EndColor) ReadDrawingGradientFillColors(
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
            ? (colors[0], colors[^1])
            : (colors?.FirstOrDefault(), null);
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
