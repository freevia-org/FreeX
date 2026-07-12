using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDrawingObjectWriter
{
    public static bool HasSupportedObjects(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasSupportedObjects(sheet))
                return true;
        }

        return false;
    }

    public static bool HasSupportedObjects(Sheet sheet)
    {
        foreach (var picture in sheet.Pictures)
        {
            if (IsSupportedPicture(picture))
                return true;
        }

        foreach (var textBox in sheet.TextBoxes)
        {
            if (IsSupportedTextBox(textBox))
                return true;
        }

        foreach (var shape in sheet.DrawingShapes)
        {
            if (IsSupportedShape(shape))
                return true;
        }

        return false;
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        IReadOnlyDictionary<string, string>? sourceDrawingPathsBySheet = null,
        HashSet<string>? usedDrawingPaths = null,
        int startPictureIndex = 1)
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

        // Every drawing part that any source sheet already owns is off-limits for fresh allocation; a sheet
        // may only reuse its own. Drawing parts already claimed by the chart writer (which runs before us and
        // has written them into the archive) are excluded by the archive.GetEntry check in AllocateFreshDrawingPath.
        var sourceDrawingPaths = sourceDrawingPathsBySheet ?? EmptyDrawingPathsBySheet;
        var reservedDrawingPaths = sourceDrawingPaths.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localUsedPaths = usedDrawingPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Start picture numbering from startPictureIndex (default 1) to avoid claiming a media
        // file name that the source package already uses for a different picture.  SavePostProcessing
        // passes max(source freexPictureN indices) + 1 so authored pictures land beyond the
        // source-preserved range.  Additionally, AllocateFreshPictureIndex bumps past any
        // freexPictureN files already present in the generated archive.
        var pictureIndex = AllocateFreshPictureIndex(archive, startPictureIndex);
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

            // Reuse the sheet's own source drawing part when it has one (so authored objects land on
            // the same drawing as any source-preserved content for that sheet); otherwise allocate the
            // next drawing{N}.xml that is not reserved by another sheet's source drawing, not already
            // present in the archive (catches parts written by the chart writer in this same save), and
            // not already claimed by a previous sheet in this loop.
            var drawingPath = sourceDrawingPaths.TryGetValue(name, out var ownDrawingPath) &&
                              localUsedPaths.Add(ownDrawingPath)
                ? ownDrawingPath
                : AllocateFreshDrawingPath(archive, reservedDrawingPaths, localUsedPaths);
            WriteWorksheetDrawingObjects(archive, worksheetPath, sheet, pictures, textBoxes, shapes, drawingPath, ref pictureIndex);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDrawingPathsBySheet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Picks the next xl/drawings/drawingN.xml part name that is free: not reserved by a source-package
    // drawing (those get restored at their original paths), not already present in the archive (covers
    // parts written by the chart writer earlier in the same save pass), and not already claimed by
    // another sheet's drawing object set in this loop.
    private static string AllocateFreshDrawingPath(ZipArchive archive, IReadOnlySet<string> reserved, HashSet<string> used)
    {
        var index = 1;
        while (true)
        {
            var path = $"xl/drawings/drawing{index}.xml";
            if (!reserved.Contains(path) && !used.Contains(path) && archive.GetEntry(path) is null)
            {
                used.Add(path);
                return path;
            }

            index++;
        }
    }

    // Returns the first picture index >= startIndex such that xl/media/freexPictureN.* does not
    // already exist in the archive.  startIndex is set by the caller (via SavePostProcessing) to
    // max(source freexPictureN index) + 1, so authored pictures land in a range that the source
    // package's preservation copy cannot collide with.
    private static int AllocateFreshPictureIndex(ZipArchive archive, int startIndex = 1)
    {
        var index = Math.Max(1, startIndex);
        while (archive.Entries.Any(e =>
                   e.FullName.StartsWith($"xl/media/freexPicture{index}.", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return index;
    }

    private static void WriteWorksheetDrawingObjects(
        ZipArchive archive,
        string worksheetPath,
        Sheet sheet,
        IReadOnlyList<PictureModel> pictures,
        IReadOnlyList<TextBoxModel> textBoxes,
        IReadOnlyList<DrawingShapeModel> shapes,
        string drawingPath,
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

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        var nextPictureIndex = pictureIndex;
        var shapeIndex = 1;
        if (sheet.DrawingObjectZOrder.Count > 0)
        {
            var picturesById = CreateObjectMap(pictures, picture => picture.Id);
            var textBoxesById = CreateObjectMap(textBoxes, textBox => textBox.Id);
            var shapesById = CreateObjectMap(shapes, shape => shape.Id);
            foreach (var entry in DrawingObjectZOrder.GetNormalizedOrder(sheet))
            {
                switch (entry.Kind)
                {
                    case SelectionPaneObjectKind.Picture when picturesById.TryGetValue(entry.Id, out var picture):
                        AddPictureAnchor(picture);
                        break;
                    case SelectionPaneObjectKind.TextBox when textBoxesById.TryGetValue(entry.Id, out var textBox):
                        AddTextBoxAnchor(textBox);
                        break;
                    case SelectionPaneObjectKind.Shape when shapesById.TryGetValue(entry.Id, out var shape):
                        AddShapeAnchor(shape);
                        break;
                }
            }
        }
        else
        {
            foreach (var picture in pictures)
                AddPictureAnchor(picture);
            foreach (var textBox in textBoxes)
                AddTextBoxAnchor(textBox);
            foreach (var shape in shapes)
                AddShapeAnchor(shape);
        }

        void AddPictureAnchor(PictureModel picture)
        {
            var currentPictureIndex = nextPictureIndex++;
            if (picture.ImageBytes is not { Length: > 0 })
            {
                // No raster to embed — an authored CellRangeSnapshot ("camera" / Paste Special >
                // Linked Picture) object that was never rasterized. Rather than silently dropping
                // the object (data loss — see IsSupportedPicture), reconstruct it as a vector
                // <xdr:grpSp> of per-cell rectangle+text shapes from the cached Cells snapshot, so
                // the range's content still round-trips through .xlsx as a real drawing object
                // instead of vanishing on save.
                anchors.Add(ToOneCellPictureSnapshotAnchor(
                    picture,
                    currentPictureIndex,
                    spreadsheetDrawingNs,
                    drawingNs));
                return;
            }

            var contentType = string.IsNullOrWhiteSpace(picture.ContentType) ? "image/png" : picture.ContentType;
            var extension = XlsxPackagePath.GetImageExtension(contentType).TrimStart('.');
            var mediaPath = $"xl/media/freexPicture{currentPictureIndex}.{extension}";
            archive.GetEntry(mediaPath)?.Delete();
            var mediaEntry = archive.CreateEntry(mediaPath);
            using (var mediaStream = mediaEntry.Open())
                mediaStream.Write(picture.ImageBytes!);
            XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension, contentType);

            var imageRelId = $"rIdFreeXPicture{currentPictureIndex}";
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

        void AddTextBoxAnchor(TextBoxModel textBox)
        {
            anchors.Add(ToOneCellTextBoxAnchor(
                textBox,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }

        void AddShapeAnchor(DrawingShapeModel shape)
        {
            anchors.Add(ToOneCellDrawingShapeAnchor(
                shape,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs));
        }

        pictureIndex = nextPictureIndex;

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        if (drawingRelsXml.Root?.Elements(packageRelNs + "Relationship").Any() == true)
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
        XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(root, worksheetNs, relNs, drawingRelId);
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
                new XElement(spreadsheetDrawingNs + "colOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, picture.AnchorOffsetX)).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)picture.Anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "rowOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, picture.AnchorOffsetY)).ToString(CultureInfo.InvariantCulture))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlUnits.PixelsToEmu(picture.Width)),
                new XAttribute("cy", DrawingMlUnits.PixelsToEmu(picture.Height))),
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
                    ToDrawingTransform(picture.RotationDegrees, picture.FlipHorizontal, picture.FlipVertical, drawingNs),
                    new XElement(drawingNs + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(drawingNs + "avLst")))),
            new XElement(spreadsheetDrawingNs + "clientData"));

    /// <summary>
    /// Reconstructs a CellRangeSnapshot picture (a "camera" / Paste Special &gt; Linked Picture /
    /// Paste Picture object with no rasterized <see cref="PictureModel.ImageBytes"/>) as a vector
    /// <c>&lt;xdr:grpSp&gt;</c> — one background rectangle plus one rectangle+text shape per cached
    /// <see cref="PictureModel.Cells"/> entry — instead of dropping the object. This mirrors the
    /// on-screen "camera" renderer (<c>GridView.RenderPicture</c>/the Avalonia equivalent), which
    /// also draws this picture kind from the same Cells snapshot rather than from a bitmap.
    /// </summary>
    private static XElement ToOneCellPictureSnapshotAnchor(
        PictureModel picture,
        int pictureIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs)
    {
        var widthEmu = Math.Max(1, DrawingMlUnits.PixelsToEmu(picture.Width));
        var heightEmu = Math.Max(1, DrawingMlUnits.PixelsToEmu(picture.Height));
        var rows = Math.Max(1u, picture.SourceRowCount);
        var cols = Math.Max(1u, picture.SourceColumnCount);
        var cellWidthEmu = Math.Max(1, widthEmu / cols);
        var cellHeightEmu = Math.Max(1, heightEmu / rows);

        // Manual last-wins loop rather than .ToDictionary(...): PictureModel.Cells has no
        // uniqueness constraint on (RowOffset, ColumnOffset) — see the matching comment on the
        // on-screen renderer (GridView.RenderPicture) — so a straight ToDictionary could throw on
        // a hand-edited/adversarial .fxl file. Last-wins keeps saving resilient.
        var cellLookup = new Dictionary<(uint Row, uint Col), PictureCellSnapshot>();
        foreach (var cell in picture.Cells)
        {
            if (cell.RowOffset < rows && cell.ColumnOffset < cols)
                cellLookup[(cell.RowOffset, cell.ColumnOffset)] = cell;
        }

        var groupId = 10000L + pictureIndex;
        var children = new List<XElement>
        {
            ToPictureSnapshotBackgroundShape(groupId + 1, widthEmu, heightEmu, spreadsheetDrawingNs, drawingNs)
        };

        var cellSerial = 1;
        foreach (var cell in cellLookup.Values.OrderBy(c => c.RowOffset).ThenBy(c => c.ColumnOffset))
        {
            children.Add(ToPictureSnapshotCellShape(
                cell,
                groupId * 1000 + cellSerial++,
                cell.ColumnOffset * cellWidthEmu,
                cell.RowOffset * cellHeightEmu,
                cellWidthEmu,
                cellHeightEmu,
                spreadsheetDrawingNs,
                drawingNs));
        }

        var rotation = NormalizeRotation(picture.RotationDegrees);
        return new(spreadsheetDrawingNs + "oneCellAnchor",
            new XElement(spreadsheetDrawingNs + "from",
                new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)picture.Anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "colOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, picture.AnchorOffsetX)).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)picture.Anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
                new XElement(spreadsheetDrawingNs + "rowOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, picture.AnchorOffsetY)).ToString(CultureInfo.InvariantCulture))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", widthEmu),
                new XAttribute("cy", heightEmu)),
            new XElement(spreadsheetDrawingNs + "grpSp",
                new XElement(spreadsheetDrawingNs + "nvGrpSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", groupId),
                        new XAttribute("name", DrawingName(picture.Name, $"Picture {pictureIndex}")),
                        string.IsNullOrWhiteSpace(picture.Title) ? null : new XAttribute("title", picture.Title),
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvGrpSpPr")),
                new XElement(spreadsheetDrawingNs + "grpSpPr",
                    new XElement(drawingNs + "xfrm",
                        rotation == 0 ? null : new XAttribute("rot", (long)Math.Round(rotation * 60000)),
                        picture.FlipHorizontal ? new XAttribute("flipH", "1") : null,
                        picture.FlipVertical ? new XAttribute("flipV", "1") : null,
                        new XElement(drawingNs + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(drawingNs + "ext", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu)),
                        new XElement(drawingNs + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(drawingNs + "chExt", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu)))),
                children),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToPictureSnapshotBackgroundShape(
        long shapeId,
        long widthEmu,
        long heightEmu,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "sp",
            new XElement(spreadsheetDrawingNs + "nvSpPr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", shapeId),
                    new XAttribute("name", "Background")),
                new XElement(spreadsheetDrawingNs + "cNvSpPr")),
            new XElement(spreadsheetDrawingNs + "spPr",
                new XElement(drawingNs + "xfrm",
                    new XElement(drawingNs + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(drawingNs + "ext", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu))),
                new XElement(drawingNs + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(drawingNs + "avLst")),
                new XElement(drawingNs + "solidFill", XlsxDrawingColorWriter.ToRgbColorElement(new CellColor(255, 255, 255), drawingNs)),
                new XElement(drawingNs + "ln",
                    new XAttribute("w", DrawingMlUnits.PointsToEmu(0.75)),
                    new XElement(drawingNs + "solidFill", XlsxDrawingColorWriter.ToRgbColorElement(new CellColor(120, 120, 120), drawingNs)))));

    private static XElement ToPictureSnapshotCellShape(
        PictureCellSnapshot cell,
        long shapeId,
        long offsetXEmu,
        long offsetYEmu,
        long widthEmu,
        long heightEmu,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs)
    {
        var style = cell.Style;
        var fill = style is not null ? ToSolidFill(style.FillThemeColor, style.FillColor, drawingNs) : null;

        var rPr = new XElement(drawingNs + "rPr", new XAttribute("lang", "en-US"));
        if (style is { FontSize: > 0 })
            rPr.Add(new XAttribute("sz", ((int)Math.Round(style.FontSize * 100)).ToString(CultureInfo.InvariantCulture)));
        if (style?.Bold == true)
            rPr.Add(new XAttribute("b", "1"));
        if (style?.Italic == true)
            rPr.Add(new XAttribute("i", "1"));
        if (style?.Underline == true)
            rPr.Add(new XAttribute("u", "sng"));
        var textFill = style is not null ? ToSolidFill(style.FontThemeColor, style.FontColor, drawingNs) : null;
        if (textFill is not null)
            rPr.Add(textFill);

        return new(spreadsheetDrawingNs + "sp",
            new XElement(spreadsheetDrawingNs + "nvSpPr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", shapeId),
                    new XAttribute("name", $"Cell {cell.RowOffset}_{cell.ColumnOffset}")),
                new XElement(spreadsheetDrawingNs + "cNvSpPr")),
            new XElement(spreadsheetDrawingNs + "spPr",
                new XElement(drawingNs + "xfrm",
                    new XElement(drawingNs + "off", new XAttribute("x", offsetXEmu), new XAttribute("y", offsetYEmu)),
                    new XElement(drawingNs + "ext", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu))),
                new XElement(drawingNs + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(drawingNs + "avLst")),
                fill ?? new XElement(drawingNs + "noFill")),
            string.IsNullOrEmpty(cell.Text)
                ? null
                : new XElement(spreadsheetDrawingNs + "txBody",
                    new XElement(drawingNs + "bodyPr"),
                    new XElement(drawingNs + "lstStyle"),
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            rPr,
                            new XElement(drawingNs + "t", cell.Text)))));
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private static Dictionary<Guid, T> CreateObjectMap<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> getId)
    {
        var result = new Dictionary<Guid, T>(items.Count);
        foreach (var item in items)
            result.TryAdd(getId(item), item);

        return result;
    }

    private static string ToSourceRectanglePercent(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

    private static XElement ToOneCellTextBoxAnchor(
        TextBoxModel textBox,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(textBox.Anchor, spreadsheetDrawingNs, textBox.AnchorOffsetX, textBox.AnchorOffsetY),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlUnits.PixelsToEmu(textBox.Width)),
                new XAttribute("cy", DrawingMlUnits.PixelsToEmu(textBox.Height))),
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
                    textBox.FlipHorizontal,
                    textBox.FlipVertical,
                    textBox.HasFill,
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
            ToDrawingAnchorFrom(shape.Anchor, spreadsheetDrawingNs, shape.AnchorOffsetX, shape.AnchorOffsetY),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlUnits.PixelsToEmu(shape.Width)),
                new XAttribute("cy", DrawingMlUnits.PixelsToEmu(shape.Height))),
            new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 200),
                        new XAttribute("name", DrawingName(shape.Name, $"Shape {shapeIndex}")),
                        string.IsNullOrWhiteSpace(shape.Title) ? null : new XAttribute("title", shape.Title),
                        string.IsNullOrWhiteSpace(shape.AltText) ? null : new XAttribute("descr", shape.AltText)),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                ToShapePropertiesForDrawingObject(
                    DrawingMlPresetGeometryMap.GetPreset(shape.Kind),
                    shape.RotationDegrees,
                    shape.FlipHorizontal,
                    shape.FlipVertical,
                    shape.HasFill,
                    shape.FillThemeColor,
                    shape.FillColor,
                    shape.OutlineThemeColor,
                    shape.OutlineColor,
                    spreadsheetDrawingNs,
                    drawingNs,
                    shape.GradientFillEndColor,
                    shape.GetEffectiveGradientFillDirection(),
                    shape.GetEffectiveEffectPreset(),
                    shape.Width,
                    shape.Height,
                    shape.OutlineWidthPoints,
                    shape.OutlineHasNoFill,
                    shape.OutlineDash,
                    shape.HeadArrowhead,
                    shape.TailArrowhead),
                shape.HasShapeText ? ToShapeTxBody(shape, drawingNs, spreadsheetDrawingNs) : null),
            new XElement(spreadsheetDrawingNs + "clientData"));

    /// <summary>
    /// Builds a minimal <c>&lt;xdr:txBody&gt;</c> element that round-trips shape text with
    /// font properties.  <see cref="DrawingShapeModel.ShapeText"/> stores multi-line text as a
    /// single string with <c>\n</c> paragraph separators (see
    /// <c>XlsxWorksheetDrawingParts.ReadShapeTextBodyPlainText</c>); each line is emitted as its
    /// own <c>&lt;a:p&gt;</c> so multi-line shape/text-box text round-trips as distinct lines
    /// instead of collapsing into one paragraph.  Multi-run rich text within a single line is
    /// still not supported — every paragraph carries the shape's one formatting set as a single run.
    /// </summary>
    private static XElement ToShapeTxBody(
        DrawingShapeModel shape,
        XNamespace drawingNs,
        XNamespace spreadsheetDrawingNs)
    {
        var anchorValue = shape.ShapeTextVAnchor switch
        {
            DrawingShapeTextVAnchor.Top => "t",
            DrawingShapeTextVAnchor.Bottom => "b",
            _ => "ctr",
        };
        var wrapValue = shape.ShapeTextWrap ? "square" : "none";

        // Run properties
        var rPr = new XElement(drawingNs + "rPr",
            new XAttribute("lang", "en-US"),
            new XAttribute("dirty", "0"));
        if (shape.ShapeTextFontSizePoints > 0)
            rPr.Add(new XAttribute("sz", ((int)Math.Round(shape.ShapeTextFontSizePoints * 100)).ToString(CultureInfo.InvariantCulture)));
        if (shape.ShapeTextBold)
            rPr.Add(new XAttribute("b", "1"));
        if (shape.ShapeTextItalic)
            rPr.Add(new XAttribute("i", "1"));
        if (shape.ShapeTextUnderline)
            rPr.Add(new XAttribute("u", "sng"));

        // CT_TextCharacterProperties child order (ECMA-376 §21.1.2.3.9):
        //   <a:ln>  (outline)  MUST come BEFORE the fill group (noFill/solidFill/gradFill/...).
        // NOTE: CT_ShapeProperties is fill-then-ln; rPr is the inverse — ln-then-fill.

        // WordArt text outline (<a:rPr><a:ln>) — emitted FIRST per CT_TextCharacterProperties.
        if (shape.IsWordArt && (shape.ShapeTextOutlineColor is not null || shape.ShapeTextOutlineThemeColor is not null))
        {
            var textLn = new XElement(drawingNs + "ln");
            if (shape.ShapeTextOutlineWidthPoints > 0)
                textLn.Add(new XAttribute("w", DrawingMlUnits.PointsToEmu(shape.ShapeTextOutlineWidthPoints).ToString(CultureInfo.InvariantCulture)));
            var outlineFill = ToSolidFill(shape.ShapeTextOutlineThemeColor, shape.ShapeTextOutlineColor, drawingNs);
            if (outlineFill is not null)
                textLn.Add(outlineFill);
            rPr.Add(textLn);
        }

        // Text fill group — emitted AFTER <a:ln> per CT_TextCharacterProperties.
        // Gradient (WordArt) takes priority over solid fill.
        var hasGradEnd = shape.ShapeTextGradientEndColor is not null ||
                         shape.ShapeTextGradientEndThemeColor is not null;
        if (shape.IsWordArt && hasGradEnd)
        {
            // Emit <a:gradFill> with two stops; use the authored angle (default 5400000 = 90° top-to-bottom).
            var gradFill = new XElement(drawingNs + "gradFill",
                new XElement(drawingNs + "gsLst",
                    BuildGradStop(drawingNs, "0",      shape.ShapeTextThemeColor,               shape.ShapeTextColor),
                    BuildGradStop(drawingNs, "100000", shape.ShapeTextGradientEndThemeColor, shape.ShapeTextGradientEndColor)),
                new XElement(drawingNs + "lin",
                    new XAttribute("ang", shape.ShapeTextGradientAngle.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("scaled", "0")));
            rPr.Add(gradFill);
        }
        else
        {
            // Normal solid text fill.
            var textFill = ToSolidFill(shape.ShapeTextThemeColor, shape.ShapeTextColor, drawingNs);
            if (textFill is not null)
                rPr.Add(textFill);
        }

        // Paragraph alignment
        var algnValue = shape.ShapeTextHAlign switch
        {
            DrawingShapeTextHAlign.Center => "ctr",
            DrawingShapeTextHAlign.Right => "r",
            _ => "l",
        };

        // bodyPr: include prstTxWarp when a warp preset is preserved (warp rendering deferred).
        var bodyPrElement = new XElement(drawingNs + "bodyPr",
            new XAttribute("anchor", anchorValue),
            new XAttribute("wrap", wrapValue));
        if (!string.IsNullOrEmpty(shape.WarpPreset))
            bodyPrElement.Add(new XElement(drawingNs + "prstTxWarp",
                new XAttribute("prst", shape.WarpPreset)));

        // Split on the \n paragraph separators the reader joins lines with (see
        // ReadShapeTextBodyPlainText) so each line becomes its own <a:p>, preserving multi-line
        // shape text across a save/reload round-trip instead of collapsing it into one paragraph.
        var lines = (shape.ShapeText ?? "").Split('\n');
        var paragraphElements = new XElement[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            paragraphElements[i] = new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XAttribute("algn", algnValue)),
                new XElement(drawingNs + "r",
                    new XElement(rPr),
                    new XElement(drawingNs + "t", lines[i])));
        }

        return new XElement(spreadsheetDrawingNs + "txBody",
            bodyPrElement,
            new XElement(drawingNs + "lstStyle"),
            paragraphElements);
    }

    private static XElement ToDrawingAnchorFrom(
        CellAddress anchor,
        XNamespace spreadsheetDrawingNs,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0) =>
        new(spreadsheetDrawingNs + "from",
            new XElement(spreadsheetDrawingNs + "col", Math.Max(0, (long)anchor.Col - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "colOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, anchorOffsetX)).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "row", Math.Max(0, (long)anchor.Row - 1).ToString(CultureInfo.InvariantCulture)),
            new XElement(spreadsheetDrawingNs + "rowOff", DrawingMlUnits.PixelsToEmu(Math.Max(0, anchorOffsetY)).ToString(CultureInfo.InvariantCulture)));

    private static XElement ToShapePropertiesForDrawingObject(
        string preset,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical,
        bool hasFill,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        CellColor? gradientFillEndColor = null,
        DrawingShapeGradientDirection gradientFillDirection = DrawingShapeGradientDirection.DiagonalDown,
        DrawingShapeEffectPreset effectPreset = DrawingShapeEffectPreset.None,
        double shapeWidthPixels = 0,
        double shapeHeightPixels = 0,
        double outlineWidthPoints = 0,
        bool outlineHasNoFill = false,
        DrawingShapeOutlineDash outlineDash = DrawingShapeOutlineDash.Solid,
        DrawingArrowhead? headArrowhead = null,
        DrawingArrowhead? tailArrowhead = null)
    {
        return new XElement(spreadsheetDrawingNs + "spPr",
            ToDrawingTransform(rotationDegrees, flipHorizontal, flipVertical, drawingNs,
                shapeWidthPixels, shapeHeightPixels),
            new XElement(drawingNs + "prstGeom",
                new XAttribute("prst", preset),
                new XElement(drawingNs + "avLst")),
            !hasFill
                ? new XElement(drawingNs + "noFill")
                : gradientFillEndColor is { } gradientEndColor && fillColor is { } gradientStartColor
                ? ToGradientFill(gradientStartColor, gradientEndColor, gradientFillDirection, drawingNs)
                : ToSolidFill(fillThemeColor, fillColor, drawingNs),
            ToLineProperties(outlineThemeColor, outlineColor, drawingNs,
                outlineWidthPoints, outlineHasNoFill, outlineDash, headArrowhead, tailArrowhead),
            ToEffectList(effectPreset, drawingNs),
            ToScene3dProperties(effectPreset, drawingNs),
            ToShape3dProperties(effectPreset, drawingNs));
    }

    private static XElement ToDrawingTransform(
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical,
        XNamespace drawingNs,
        double shapeWidthPixels = 0,
        double shapeHeightPixels = 0)
    {
        var rotation = NormalizeRotation(rotationDegrees);
        // Include <a:ext cx cy> when pre-rotation dimensions are known; readers use these to
        // recover the unrotated size rather than the bounding-box span from the outer anchor.
        XElement? extElement = null;
        if (shapeWidthPixels > 0 && shapeHeightPixels > 0)
        {
            extElement = new XElement(drawingNs + "ext",
                new XAttribute("cx", DrawingMlUnits.PixelsToEmu(shapeWidthPixels)),
                new XAttribute("cy", DrawingMlUnits.PixelsToEmu(shapeHeightPixels)));
        }

        return new XElement(drawingNs + "xfrm",
            rotation == 0 ? null : new XAttribute("rot", (long)Math.Round(rotation * 60000)),
            flipHorizontal ? new XAttribute("flipH", "1") : null,
            flipVertical ? new XAttribute("flipV", "1") : null,
            extElement);
    }

    private static XElement ToGradientFill(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction,
        XNamespace drawingNs) =>
        new(drawingNs + "gradFill",
            new XElement(drawingNs + "gsLst",
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "0"),
                    XlsxDrawingColorWriter.ToRgbColorElement(startColor, drawingNs)),
                new XElement(drawingNs + "gs",
                    new XAttribute("pos", "100000"),
                    XlsxDrawingColorWriter.ToRgbColorElement(endColor, drawingNs))),
            new XElement(drawingNs + "lin",
                new XAttribute("ang", ToGradientFillAngle(direction)),
                new XAttribute("scaled", "1")));

    private static string ToGradientFillAngle(DrawingShapeGradientDirection direction) =>
        (Enum.IsDefined(direction) ? direction : DrawingShapeGradientDirection.DiagonalDown) switch
        {
            DrawingShapeGradientDirection.Horizontal => "0",
            DrawingShapeGradientDirection.DiagonalUp => "10800000",
            DrawingShapeGradientDirection.Vertical => "16200000",
            _ => "5400000"
        };

    private static XElement ToOuterShadowEffect(XNamespace drawingNs) =>
        new(drawingNs + "effectLst",
            new XElement(drawingNs + "outerShdw",
                new XAttribute("blurRad", "40000"),
                new XAttribute("dist", "20000"),
                new XAttribute("dir", "5400000"),
                XlsxDrawingColorWriter.ToRgbColorElement(new CellColor(128, 128, 128), drawingNs)));

    private static XElement ToInnerShadowEffect(XNamespace drawingNs) =>
        new(drawingNs + "effectLst",
            new XElement(drawingNs + "innerShdw",
                new XAttribute("blurRad", "38100"),
                new XAttribute("dist", "19050"),
                new XAttribute("dir", "5400000"),
                new XElement(drawingNs + "srgbClr",
                    new XAttribute("val", "000000"),
                    new XElement(drawingNs + "alpha", new XAttribute("val", "50000")))));

    private static XElement ToReflectionEffect(XNamespace drawingNs) =>
        new(drawingNs + "effectLst",
            new XElement(drawingNs + "reflection",
                new XAttribute("blurRad", "20000"),
                new XAttribute("stA", "45000"),
                new XAttribute("endA", "0"),
                new XAttribute("stPos", "0"),
                new XAttribute("endPos", "65000"),
                new XAttribute("dist", "12000"),
                new XAttribute("dir", "5400000")));

    private static XElement? ToEffectList(DrawingShapeEffectPreset effectPreset, XNamespace drawingNs) =>
        effectPreset switch
        {
            DrawingShapeEffectPreset.Shadow => ToOuterShadowEffect(drawingNs),
            DrawingShapeEffectPreset.InnerShadow => ToInnerShadowEffect(drawingNs),
            DrawingShapeEffectPreset.Reflection => ToReflectionEffect(drawingNs),
            DrawingShapeEffectPreset.Glow => new XElement(drawingNs + "effectLst",
                new XElement(drawingNs + "glow",
                    new XAttribute("rad", "50000"),
                    XlsxDrawingColorWriter.ToRgbColorElement(new CellColor(91, 155, 213), drawingNs))),
            DrawingShapeEffectPreset.SoftEdges => new XElement(drawingNs + "effectLst",
                new XElement(drawingNs + "softEdge", new XAttribute("rad", "30000"))),
            _ => null
        };

    private static XElement? ToShape3dProperties(DrawingShapeEffectPreset effectPreset, XNamespace drawingNs) =>
        effectPreset == DrawingShapeEffectPreset.Bevel
            ? new XElement(drawingNs + "sp3d",
                new XElement(drawingNs + "bevelT",
                    new XAttribute("w", "76200"),
                    new XAttribute("h", "25400")))
            : null;

    private static XElement? ToScene3dProperties(DrawingShapeEffectPreset effectPreset, XNamespace drawingNs) =>
        effectPreset == DrawingShapeEffectPreset.ThreeDRotation
            ? new XElement(drawingNs + "scene3d",
                new XElement(drawingNs + "camera", new XAttribute("prst", "isometricOffAxis1Left")),
                new XElement(drawingNs + "lightRig",
                    new XAttribute("rig", "threePt"),
                    new XAttribute("dir", "t")))
            : null;

    private static XElement? ToLineProperties(
        WorkbookThemeColorReference? outlineThemeColor,
        CellColor? outlineColor,
        XNamespace drawingNs,
        double outlineWidthPoints = 0,
        bool outlineHasNoFill = false,
        DrawingShapeOutlineDash outlineDash = DrawingShapeOutlineDash.Solid,
        DrawingArrowhead? headArrowhead = null,
        DrawingArrowhead? tailArrowhead = null)
    {
        // Explicitly no border: write <a:ln><a:noFill/></a:ln>
        if (outlineHasNoFill)
            return new XElement(drawingNs + "ln", new XElement(drawingNs + "noFill"));

        var fill = ToSolidFill(outlineThemeColor, outlineColor, drawingNs);
        if (fill is null)
            return null;

        // Omit zero/default outline widths to keep output compact.
        var wEmu = outlineWidthPoints > 0 ? DrawingMlUnits.PointsToEmu(outlineWidthPoints) : 0;
        var prstDashVal = outlineDash switch
        {
            DrawingShapeOutlineDash.Dash => "dash",
            DrawingShapeOutlineDash.Dot => "dot",
            DrawingShapeOutlineDash.DashDot => "dashDot",
            DrawingShapeOutlineDash.LongDash => "lgDash",
            DrawingShapeOutlineDash.LongDashDot => "lgDashDot",
            DrawingShapeOutlineDash.LongDashDotDot => "lgDashDotDot",
            DrawingShapeOutlineDash.SystemDash => "sysDash",
            DrawingShapeOutlineDash.SystemDot => "sysDot",
            DrawingShapeOutlineDash.SystemDashDot => "sysDashDot",
            _ => null // Solid: omit prstDash element (solid is default)
        };
        return new XElement(drawingNs + "ln",
            wEmu > 0 ? new XAttribute("w", wEmu) : null,
            fill,
            prstDashVal is not null
                ? new XElement(drawingNs + "prstDash", new XAttribute("val", prstDashVal))
                : null,
            ToArrowheadElement(drawingNs, "headEnd", headArrowhead),
            ToArrowheadElement(drawingNs, "tailEnd", tailArrowhead));
    }

    private static XElement? ToArrowheadElement(XNamespace drawingNs, string elementName, DrawingArrowhead? arrowhead)
    {
        if (arrowhead is null || !arrowhead.IsPresent)
            return null;

        var typeVal = arrowhead.Type switch
        {
            DrawingArrowheadType.Triangle => "triangle",
            DrawingArrowheadType.Arrow => "arrow",
            DrawingArrowheadType.Stealth => "stealth",
            DrawingArrowheadType.Diamond => "diamond",
            DrawingArrowheadType.Oval => "oval",
            _ => "none"
        };
        var wVal = arrowhead.Width switch
        {
            DrawingArrowheadSize.Small => "sm",
            DrawingArrowheadSize.Large => "lg",
            _ => "med"
        };
        var lenVal = arrowhead.Length switch
        {
            DrawingArrowheadSize.Small => "sm",
            DrawingArrowheadSize.Large => "lg",
            _ => "med"
        };
        return new XElement(drawingNs + elementName,
            new XAttribute("type", typeVal),
            new XAttribute("w", wVal),
            new XAttribute("len", lenVal));
    }

    /// <summary>
    /// Builds a gradient stop element <c>&lt;a:gs pos="..."&gt;</c> for a two-stop WordArt gradient.
    /// </summary>
    private static XElement BuildGradStop(
        XNamespace drawingNs,
        string position,
        WorkbookThemeColorReference? themeColor,
        CellColor? color)
    {
        var colorElement = XlsxDrawingColorWriter.ToColorElement(themeColor, color, drawingNs)
            // Fallback: transparent white.
            ?? XlsxDrawingColorWriter.ToRgbColorElement(new CellColor(255, 255, 255), drawingNs);

        return new XElement(drawingNs + "gs",
            new XAttribute("pos", position),
            colorElement);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs) =>
        XlsxDrawingColorWriter.ToSolidFill(themeColor, color, drawingNs);

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0;
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    // A camera / "Paste Link Picture" / "Paste Picture" object (Kind == CellRangeSnapshot) is, on
    // the wire, still just a picture anchored on the sheet — Excel itself stores it as a normal
    // <xdr:pic> backed by a rendered bitmap of the source range, plus the linked-range metadata
    // FreeX tracks separately via IsLinkedToSourceRange/LinkedSourceRange. FreeX never rasterizes
    // these at paste time (PasteRangeAsPictureCommand only records the cell-content/style snapshot
    // in Cells), so ImageBytes is always null for them. Requiring ImageBytes here would silently
    // drop the object (and its content) on every .xlsx save; instead a CellRangeSnapshot picture
    // with no raster is still "supported" — AddPictureAnchor reconstructs it as a vector <xdr:grpSp>
    // of per-cell shapes from Cells (see ToOneCellPictureSnapshotAnchor) rather than an <xdr:pic>.
    private static bool IsSupportedPicture(PictureModel picture) =>
        !picture.IsSourceLoaded &&
        double.IsFinite(picture.Width) &&
        double.IsFinite(picture.Height) &&
        picture.Width > 0 &&
        picture.Height > 0 &&
        (picture.ImageBytes is { Length: > 0 } || picture.Kind == PictureKind.CellRangeSnapshot);

    private static bool IsSupportedTextBox(TextBoxModel textBox) =>
        !textBox.IsSourceLoaded &&
        double.IsFinite(textBox.Width) &&
        double.IsFinite(textBox.Height) &&
        textBox.Width > 0 &&
        textBox.Height > 0;

    private static bool IsSupportedShape(DrawingShapeModel shape) =>
        !shape.IsSourceLoaded &&
        DrawingShapeKindSupport.IsRenderable(shape.Kind) &&
        double.IsFinite(shape.Width) &&
        double.IsFinite(shape.Height) &&
        shape.Width > 0 &&
        shape.Height > 0;

    private static string DrawingName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

}
