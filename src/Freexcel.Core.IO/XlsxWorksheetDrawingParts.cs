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
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom);

internal sealed record XlsxTextBoxPackagePart(
    string Text,
    string? Name,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    CellColor? FillColor,
    CellColor? OutlineColor);

internal sealed record XlsxShapePackagePart(
    DrawingShapeKind Kind,
    string? Name,
    string? AltText,
    XlsxDrawingAnchor? Anchor,
    double RotationDegrees,
    CellColor? FillColor,
    CellColor? OutlineColor,
    CellColor? GradientFillEndColor,
    bool HasShadowEffect);

internal sealed record XlsxDrawingAnchor(
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

internal static class XlsxWorksheetDrawingPartReader
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
            var altText = ReadNonVisualDescription(pictureElement);
            var sourceRectangle = pictureElement
                .Element(spreadsheetDrawingNs + "blipFill")?
                .Element(drawingNs + "srcRect");

            pictures.Add(new XlsxPicturePackagePart(
                ms.ToArray(),
                XlsxPackagePath.GetImageContentType(imagePath),
                name,
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
            var altText = ReadNonVisualDescription(shapeElement);
            var spPr = shapeElement.Element(spreadsheetDrawingNs + "spPr");
            var rotation = ReadDrawingRotation(spPr?.Element(drawingNs + "xfrm"));
            var gradientFill = ReadDrawingGradientFillColors(spPr?.Element(drawingNs + "gradFill"), drawingNs);
            var fillColor = gradientFill.StartColor ?? ReadDrawingSolidFillColor(spPr?.Element(drawingNs + "solidFill"), drawingNs);
            var outlineColor = ReadDrawingSolidFillColor(spPr?.Element(drawingNs + "ln")?.Element(drawingNs + "solidFill"), drawingNs);
            var hasShadowEffect = spPr?
                .Element(drawingNs + "effectLst")?
                .Element(drawingNs + "outerShdw") is not null;
            var text = string.Concat(shapeElement
                .Element(spreadsheetDrawingNs + "txBody")?
                .Descendants(drawingNs + "t")
                .Select(t => t.Value) ?? []);

            if (!string.IsNullOrEmpty(text))
            {
                textBoxes.Add(new XlsxTextBoxPackagePart(text, name, altText, ReadNearestAnchor(shapeElement), rotation, fillColor, outlineColor));
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
                    altText,
                    ReadNearestAnchor(shapeElement),
                    rotation,
                    fillColor,
                    outlineColor,
                    gradientFill.EndColor,
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

    private static XlsxDrawingAnchor? ReadNearestAnchor(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        return element
            .Ancestors(spreadsheetDrawingNs + "twoCellAnchor")
            .Select(TryReadTwoCellAnchor)
            .FirstOrDefault(candidate => candidate is not null)
            ?? element
                .Ancestors(spreadsheetDrawingNs + "oneCellAnchor")
                .Select(TryReadOneCellAnchor)
                .FirstOrDefault(candidate => candidate is not null)
            ?? element
                .Ancestors(spreadsheetDrawingNs + "absoluteAnchor")
                .Select(TryReadAbsoluteAnchor)
                .FirstOrDefault(candidate => candidate is not null);
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

    private static XlsxDrawingAnchor? TryReadTwoCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var to = anchor.Element(spreadsheetDrawingNs + "to");
        if (from is null || to is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset) ||
            !TryReadAnchorCoordinate(to, spreadsheetDrawingNs, out var toRow, out var toCol, out var toRowOffset, out var toColOffset))
        {
            return null;
        }

        if (toRow <= fromRow || toCol <= fromCol)
            return null;

        return new XlsxDrawingAnchor(
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            toRow,
            toCol,
            toRowOffset,
            toColOffset,
            Width: null,
            Height: null);
    }

    private static XlsxDrawingAnchor? TryReadOneCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (from is null || ext is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset))
            return null;

        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

        return new XlsxDrawingAnchor(
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static XlsxDrawingAnchor? TryReadAbsoluteAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var pos = anchor.Element(spreadsheetDrawingNs + "pos");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (pos is null || ext is null)
            return null;

        var left = EmusToPixels(pos.Attribute("x")?.Value);
        var top = EmusToPixels(pos.Attribute("y")?.Value);
        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

        return new XlsxDrawingAnchor(
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            left,
            top,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static bool TryReadAnchorCoordinate(
        XElement marker,
        XNamespace spreadsheetDrawingNs,
        out uint rowZeroBased,
        out uint columnZeroBased,
        out double rowOffset,
        out double columnOffset)
    {
        rowZeroBased = 0;
        columnZeroBased = 0;
        rowOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "rowOff")?.Value);
        columnOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "colOff")?.Value);
        return uint.TryParse(marker.Element(spreadsheetDrawingNs + "row")?.Value, out rowZeroBased) &&
               uint.TryParse(marker.Element(spreadsheetDrawingNs + "col")?.Value, out columnZeroBased);
    }

    private static double EmusToPixels(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var emus)
            ? emus / 9525.0
            : 0;
}
