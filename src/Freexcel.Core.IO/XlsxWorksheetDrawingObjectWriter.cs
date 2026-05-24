using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDrawingObjectWriter
{
    public static bool HasSupportedObjects(Workbook workbook) =>
        workbook.Sheets.Any(sheet =>
            sheet.Pictures.Any(IsSupportedPicture) ||
            sheet.TextBoxes.Any(IsSupportedTextBox) ||
            sheet.DrawingShapes.Any(IsSupportedShape));

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        var drawingIndex = 1;
        var pictureIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var pictures = sheet.Pictures.Where(IsSupportedPicture).ToList();
            var textBoxes = sheet.TextBoxes.Where(IsSupportedTextBox).ToList();
            var shapes = sheet.DrawingShapes.Where(IsSupportedShape).ToList();
            if (pictures.Count == 0 && textBoxes.Count == 0 && shapes.Count == 0)
                continue;

            WriteWorksheetDrawingObjects(archive, worksheetPath, pictures, textBoxes, shapes, drawingIndex++, ref pictureIndex);
        }
    }

    private static void WriteWorksheetDrawingObjects(
        ZipArchive archive,
        string worksheetPath,
        IReadOnlyList<PictureModel> pictures,
        IReadOnlyList<TextBoxModel> textBoxes,
        IReadOnlyList<DrawingShapeModel> shapes,
        int drawingIndex,
        ref int pictureIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        var drawingPath = $"xl/drawings/drawing{drawingIndex}.xml";
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        foreach (var picture in pictures)
        {
            var currentPictureIndex = pictureIndex++;
            var contentType = string.IsNullOrWhiteSpace(picture.ContentType) ? "image/png" : picture.ContentType;
            var extension = XlsxPackagePath.GetImageExtension(contentType).TrimStart('.');
            var mediaPath = $"xl/media/freexcelPicture{currentPictureIndex}.{extension}";
            archive.GetEntry(mediaPath)?.Delete();
            var mediaEntry = archive.CreateEntry(mediaPath);
            using (var mediaStream = mediaEntry.Open())
                mediaStream.Write(picture.ImageBytes!);
            XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension, contentType);

            var imageRelId = $"rIdFreexcelPicture{currentPictureIndex}";
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", imageRelId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(drawingPath, mediaPath))));
            anchors.Add(ToOneCellPictureAnchor(
                picture,
                currentPictureIndex,
                imageRelId,
                spreadsheetDrawingNs,
                drawingNs,
                relNs));
        }

        var shapeIndex = 1;
        foreach (var textBox in textBoxes)
        {
            anchors.Add(ToOneCellTextBoxAnchor(
                textBox,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }

        foreach (var shape in shapes)
        {
            anchors.Add(ToOneCellDrawingShapeAnchor(
                shape,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, drawingRelsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        var drawingRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            worksheetRelsXml,
            packageRelNs,
            worksheetPath,
            drawingPath,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, worksheetRelsXml);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "drawing").Remove();
        root.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", drawingRelId)));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XElement ToOneCellPictureAnchor(
        PictureModel picture,
        int pictureIndex,
        string imageRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            new XElement(spreadsheetDrawingNs + "from",
                new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)picture.Anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "colOff", "0"),
                new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)picture.Anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "rowOff", "0")),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(picture.Width)),
                new XAttribute("cy", PixelsToEmus(picture.Height))),
            new XElement(spreadsheetDrawingNs + "pic",
                new XElement(spreadsheetDrawingNs + "nvPicPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", pictureIndex + 1),
                        new XAttribute("name", DrawingName(picture.Name, $"Picture {pictureIndex}")),
                        string.IsNullOrWhiteSpace(picture.Title) ? null : new XAttribute("title", picture.Title),
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                new XElement(spreadsheetDrawingNs + "blipFill",
                    new XElement(drawingNs + "blip", new XAttribute(relNs + "embed", imageRelId)),
                    HasPictureCrop(picture)
                        ? new XElement(drawingNs + "srcRect",
                            new XAttribute("l", ToSourceRectanglePercent(picture.CropLeft)),
                            new XAttribute("t", ToSourceRectanglePercent(picture.CropTop)),
                            new XAttribute("r", ToSourceRectanglePercent(picture.CropRight)),
                            new XAttribute("b", ToSourceRectanglePercent(picture.CropBottom)))
                        : null,
                    new XElement(drawingNs + "stretch", new XElement(drawingNs + "fillRect"))),
                new XElement(spreadsheetDrawingNs + "spPr",
                    new XElement(drawingNs + "xfrm"),
                    new XElement(drawingNs + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(drawingNs + "avLst")))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private static string ToSourceRectanglePercent(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

    private static XElement ToOneCellTextBoxAnchor(
        TextBoxModel textBox,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(textBox.Anchor, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(textBox.Width)),
                new XAttribute("cy", PixelsToEmus(textBox.Height))),
            new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 100),
                        new XAttribute("name", DrawingName(textBox.Name, $"TextBox {shapeIndex}")),
                        string.IsNullOrWhiteSpace(textBox.Title) ? null : new XAttribute("title", textBox.Title),
                        string.IsNullOrWhiteSpace(textBox.AltText) ? null : new XAttribute("descr", textBox.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr", new XAttribute("txBox", "1"))),
                ToShapePropertiesForDrawingObject(
                    "rect",
                    textBox.RotationDegrees,
                    textBox.FillThemeColor,
                    textBox.FillColor,
                    textBox.OutlineThemeColor,
                    textBox.OutlineColor,
                    spreadsheetDrawingNs,
                    drawingNs),
                new XElement(spreadsheetDrawingNs + "txBody",
                    new XElement(drawingNs + "bodyPr"),
                    new XElement(drawingNs + "lstStyle"),
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            new XElement(drawingNs + "t", textBox.Text))))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToOneCellDrawingShapeAnchor(
        DrawingShapeModel shape,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(shape.Anchor, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", PixelsToEmus(shape.Width)),
                new XAttribute("cy", PixelsToEmus(shape.Height))),
            new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 200),
                        new XAttribute("name", DrawingName(shape.Name, $"Shape {shapeIndex}")),
                        string.IsNullOrWhiteSpace(shape.Title) ? null : new XAttribute("title", shape.Title),
                        string.IsNullOrWhiteSpace(shape.AltText) ? null : new XAttribute("descr", shape.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                ToShapePropertiesForDrawingObject(
                    ToDrawingPreset(shape.Kind),
                    shape.RotationDegrees,
                    shape.FillThemeColor,
                    shape.FillColor,
                    shape.OutlineThemeColor,
                    shape.OutlineColor,
                    spreadsheetDrawingNs,
                    drawingNs,
                    shape.GradientFillEndColor,
                    shape.HasShadowEffect)),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToDrawingAnchorFrom(CellAddress anchor, XNamespace spreadsheetDrawingNs) =>
        new(spreadsheetDrawingNs + "from",
            new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "colOff", "0"),
            new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "rowOff", "0"));

    private static XElement ToShapePropertiesForDrawingObject(
        string preset,
        double rotationDegrees,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        CellColor? gradientFillEndColor = null,
        bool hasShadowEffect = false)
    {
        var rotation = NormalizeRotation(rotationDegrees);
        return new XElement(spreadsheetDrawingNs + "spPr",
            new XElement(drawingNs + "xfrm",
                rotation == 0 ? null : new XAttribute("rot", (long)Math.Round(rotation * 60000))),
            new XElement(drawingNs + "prstGeom",
                new XAttribute("prst", preset),
                new XElement(drawingNs + "avLst")),
            gradientFillEndColor is { } gradientEndColor && fillColor is { } gradientStartColor
                ? ToGradientFill(gradientStartColor, gradientEndColor, drawingNs)
                : ToSolidFill(fillThemeColor, fillColor, drawingNs),
            ToLineProperties(outlineThemeColor, outlineColor, drawingNs),
            hasShadowEffect ? ToOuterShadowEffect(drawingNs) : null);
    }

    private static XElement ToGradientFill(CellColor startColor, CellColor endColor, XNamespace drawingNs) =>
        new(drawingNs + "gradFill",
            new XElement(drawingNs + "gsLst",
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "0"),
                    ToRgbColorElement(startColor, drawingNs)),
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "100000"),
                    ToRgbColorElement(endColor, drawingNs))),
            new XElement(drawingNs + "lin",
                new XAttribute("ang", "5400000"),
                new XAttribute("scaled", "1")));

    private static XElement ToOuterShadowEffect(XNamespace drawingNs) =>
        new(drawingNs + "effectLst",
            new XElement(drawingNs + "outerShdw",
                new XAttribute("blurRad", "40000"),
                new XAttribute("dist", "20000"),
                new XAttribute("dir", "5400000"),
                ToRgbColorElement(new CellColor(128, 128, 128), drawingNs)));

    private static XElement ToRgbColorElement(CellColor color, XNamespace drawingNs) =>
        new(drawingNs + "srgbClr", new XAttribute("val", FormatColor(color)));

    private static XElement? ToLineProperties(
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(outlineThemeColor, outlineColor, drawingNs);
        return fill is null ? null : new XElement(drawingNs + "ln", fill);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        XElement? colorElement = null;
        if (themeColor is { } theme)
        {
            colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToDrawingSchemeColor(theme.Slot)));
            ApplyTint(colorElement, theme.Tint, drawingNs);
        }
        else if (color is { } concrete)
        {
            colorElement = new XElement(drawingNs + "srgbClr",
                new XAttribute("val", FormatColor(concrete)));
        }

        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static string ToDrawingPreset(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => "ellipse",
            DrawingShapeKind.Line => "line",
            _ => "rect"
        };

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0;
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool IsSupportedPicture(PictureModel picture) =>
        !picture.IsSourceLoaded &&
        picture.Kind == PictureKind.Image &&
        picture.ImageBytes is { Length: > 0 } &&
        double.IsFinite(picture.Width) &&
        double.IsFinite(picture.Height) &&
        picture.Width > 0 &&
        picture.Height > 0;

    private static bool IsSupportedTextBox(TextBoxModel textBox) =>
        !textBox.IsSourceLoaded &&
        double.IsFinite(textBox.Width) &&
        double.IsFinite(textBox.Height) &&
        textBox.Width > 0 &&
        textBox.Height > 0;

    private static bool IsSupportedShape(DrawingShapeModel shape) =>
        !shape.IsSourceLoaded &&
        Enum.IsDefined(shape.Kind) &&
        double.IsFinite(shape.Width) &&
        double.IsFinite(shape.Height) &&
        shape.Width > 0 &&
        shape.Height > 0;

    private static string DrawingName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static string FormatColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToDrawingSchemeColor(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };

    private static long PixelsToEmus(double pixels) =>
        (long)Math.Round(Math.Max(0, pixels) * 9525.0);
}
