using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDrawingObjectWriter
{
    // R95-io-drawing-hyperlink-2-2: mirrors XlsxWorksheetChartWriter's HyperlinkRelationshipType --
    // the OOXML package relationship type for an a:hlinkClick's r:id target.
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

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

    /// <summary>
    /// The cNvPr names of the drawing objects on <paramref name="sheet"/> that this writer re-emits a
    /// FRESH anchor for even though they were originally loaded from the source .xlsx — i.e. a
    /// picture/text box/shape whose <c>IsSourceLoaded</c> flag has been CLEARED by an edit (a colour,
    /// rotation, gradient or effect change — see the R51/R62 drawing-format commands) so
    /// <see cref="IsSupportedPicture"/>/<see cref="IsSupportedTextBox"/>/<see cref="IsSupportedShape"/>
    /// now accept it. When such an object is saved on the source-package path, the writer emits its new
    /// anchor into the drawing part BEFORE <see cref="XlsxWorksheetDrawingPartMerger"/> copies the
    /// original source anchors back in; unless the merger is told to drop the object's ORIGINAL anchor,
    /// the saved drawing part carries BOTH anchors and the object is duplicated on reload.
    /// <para>
    /// The reader stamps each source-loaded object's <see cref="TextBoxModel.Name"/> (etc.) from the
    /// source anchor's <c>cNvPr@name</c>, and no edit that clears <c>IsSourceLoaded</c> also renames the
    /// object, so the freshly written anchor and the original source anchor share that name — it is the
    /// stable key that lets the merger recognise the two as the same logical object across the anchor
    /// type / geometry differences the edit introduces.
    /// </para>
    /// <para>
    /// A name is only reported when NO object on the sheet still holds it as a source-loaded object: Excel
    /// reuses default names ("TextBox 1", "Picture 1", …) independently per sheet, so a genuinely distinct
    /// source-loaded object and a newly authored object can share one name, and the still-source-loaded
    /// one's original anchor must keep being preserved. Names are compared ordinally, matching the
    /// verbatim round-trip through the reader and writer.
    /// </para>
    /// <para>
    /// R121-model-drawing-delete-1: also unions in <see cref="Sheet.DeletedSourceDrawingObjectNames"/> --
    /// a drawing object (picture, text box, shape, OR CHART) that DeleteDrawingObjectCommand removed
    /// outright this session is, from the merger's point of view, exactly the same problem as an edited
    /// one: it has no live model entry the writer can re-emit a fresh anchor for (there IS no fresh
    /// anchor -- the object is gone), yet its name still matches an ORIGINAL anchor sitting untouched in
    /// the true source package. Without this union that anchor -- and, for a picture, its image
    /// relationship -- gets copied straight back into the saved drawing part by
    /// <see cref="XlsxWorksheetDrawingPartMerger"/>, resurrecting a deleted object on the very next
    /// reload. A chart has no <c>IsSourceLoaded</c> flag of its own (charts are always fully rewritten by
    /// <c>XlsxWorksheetChartWriter</c> for supported chart types), but the merger's supersede check reads
    /// a source anchor's <c>cNvPr@name</c> generically regardless of anchor kind, so listing a deleted
    /// chart's name here is enough to keep its original graphicFrame out of the merge too.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> GetRewrittenSourceObjectNames(Sheet sheet)
    {
        var rewrittenNames = new HashSet<string>(StringComparer.Ordinal);
        var sourceLoadedNames = new HashSet<string>(StringComparer.Ordinal);
        CollectDrawingObjectNames(sheet.Pictures, IsSupportedPicture, picture => picture.Name, picture => picture.IsSourceLoaded, rewrittenNames, sourceLoadedNames);
        CollectDrawingObjectNames(sheet.TextBoxes, IsSupportedTextBox, textBox => textBox.Name, textBox => textBox.IsSourceLoaded, rewrittenNames, sourceLoadedNames);
        CollectDrawingObjectNames(sheet.DrawingShapes, IsSupportedShape, shape => shape.Name, shape => shape.IsSourceLoaded, rewrittenNames, sourceLoadedNames);
        rewrittenNames.ExceptWith(sourceLoadedNames);

        foreach (var deletedName in sheet.DeletedSourceDrawingObjectNames)
        {
            if (!string.IsNullOrWhiteSpace(deletedName) && !sourceLoadedNames.Contains(deletedName))
                rewrittenNames.Add(deletedName);
        }

        return rewrittenNames;
    }

    private static void CollectDrawingObjectNames<T>(
        IEnumerable<T> objects,
        Func<T, bool> isSupported,
        Func<T, string?> getName,
        Func<T, bool> isSourceLoaded,
        HashSet<string> rewrittenNames,
        HashSet<string> sourceLoadedNames)
    {
        foreach (var drawingObject in objects)
        {
            var name = getName(drawingObject);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (isSourceLoaded(drawingObject))
                sourceLoadedNames.Add(name);
            else if (isSupported(drawingObject))
                rewrittenNames.Add(name);
        }
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        IReadOnlyDictionary<string, string>? sourceDrawingPathsBySheet = null,
        HashSet<string>? usedDrawingPaths = null,
        int startPictureIndex = 1,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Target, string? TargetMode)>>? sourceObjectHyperlinksBySheet = null,
        // drawing-zorder-share-part (residual-gap closure): sheet name -> the drawing part
        // XlsxWorksheetChartWriter FRESHLY allocated for that sheet's charts earlier in this same save
        // (populated only for sheets with no source drawing part of their own -- the case the
        // chart-shadow/XlsxWorksheetDrawingPartMerger route cannot cover, because the merger only runs
        // when the workbook has a source package with drawings). A worksheet can reference exactly ONE
        // drawing part, so allocating a second one here and repointing the worksheet at it silently
        // orphaned the chart writer's part: every chart on a sheet that also had a picture/shape/text
        // box was lost on save. For those sheets we instead write INTO the chart writer's part and
        // carry its chart anchors and relationships forward -- see WriteWorksheetDrawingObjects's
        // preserveExistingContent handling.
        IReadOnlyDictionary<string, string>? chartDrawingPathsBySheet = null)
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
            // R121-io-drawing-delete-1: normally a sheet with nothing "supported" left to emit (every
            // remaining object still IsSourceLoaded) is skipped entirely -- the merger repopulates its
            // drawing part later from the untouched source. But when this sheet ALSO has a tombstoned
            // deletion (DeleteDrawingObjectCommand removed a picture/text box/shape/chart this session),
            // skipping here means the writer never touches this sheet's drawing part at all, so
            // XlsxPackageMetadataMerger.CopyUnknownPackageParts (which only copies a source part when the
            // TARGET has no entry at that path yet) blindly copies the stale, UNFILTERED source drawing
            // part back in wholesale before the merger's own supersededSourceNames-aware add-back logic
            // ever runs -- resurrecting the deleted object regardless of that check. Falling through
            // (even with all three lists empty) makes the writer emit this sheet's own drawing part --
            // empty of anchors, but present in the archive -- so CopyUnknownPackageParts skips its raw
            // copy and the merger's filtered add-back is what actually repopulates it.
            if (pictures.Count == 0 && textBoxes.Count == 0 && shapes.Count == 0 &&
                sheet.DeletedSourceDrawingObjectNames.Count == 0)
            {
                continue;
            }

            // Reuse the sheet's own source drawing part when it has one (so authored objects land on
            // the same drawing as any source-preserved content for that sheet); otherwise allocate the
            // next drawing{N}.xml that is not reserved by another sheet's source drawing, not already
            // present in the archive (catches parts written by the chart writer in this same save), and
            // not already claimed by a previous sheet in this loop.
            string drawingPath;
            var preserveExistingContent = false;
            if (sourceDrawingPaths.TryGetValue(name, out var ownDrawingPath) && localUsedPaths.Add(ownDrawingPath))
            {
                drawingPath = ownDrawingPath;
            }
            else if (chartDrawingPathsBySheet?.TryGetValue(name, out var chartDrawingPath) == true &&
                     localUsedPaths.Add(chartDrawingPath))
            {
                // This sheet's charts were just written into a freshly allocated drawing part. Share it
                // (a worksheet can only reference one) and keep what is already in it.
                drawingPath = chartDrawingPath;
                preserveExistingContent = true;
            }
            else
            {
                drawingPath = AllocateFreshDrawingPath(archive, reservedDrawingPaths, localUsedPaths);
            }

            var objectHyperlinksByName = sourceObjectHyperlinksBySheet?.TryGetValue(name, out var sheetHyperlinks) == true
                ? sheetHyperlinks
                : EmptyObjectHyperlinksByName;
            WriteWorksheetDrawingObjects(archive, worksheetPath, sheet, pictures, textBoxes, shapes, drawingPath, ref pictureIndex, objectHyperlinksByName, preserveExistingContent);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDrawingPathsBySheet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, (string Target, string? TargetMode)> EmptyObjectHyperlinksByName =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal);

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
        ref int pictureIndex,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)> oldObjectHyperlinksByName,
        bool preserveExistingContent = false)
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

        // R95-io-drawing-hyperlink-2-2: each existing picture/text-box/shape's object-level hyperlink
        // (an a:hlinkClick on its xdr:cNvPr) is captured by the CALLER (XlsxFileAdapter, via
        // GetSourceDrawingObjectHyperlinksBySheet) from the TRUE source .xlsx package and passed in as
        // oldObjectHyperlinksByName -- NOT read from `archive` here. At this point in the save
        // pipeline `archive` is the in-progress GENERATED package (freshly built by ClosedXML from the
        // FreeX model, with no drawing parts of its own yet), so it never carries the original
        // hyperlink bytes; only the true source package does. Matched by the object's stable
        // cNvPr@name (the same key GetRewrittenSourceObjectNames/the drawing-part merger already rely
        // on to recognise a rewritten object as the same logical object across the reader/writer
        // round-trip) rather than position, since pictures/text boxes/shapes -- unlike charts, which
        // XlsxWorksheetChartWriter matches positionally -- can be freely reordered, added, or removed
        // independently of one another.
        // drawing-zorder-share-part (residual-gap closure): when this sheet's charts were written into
        // this very part by XlsxWorksheetChartWriter moments ago (preserveExistingContent), carry its
        // anchors and relationships forward instead of dropping them -- the delete-and-rebuild below
        // would otherwise discard every chart on a sheet that also has a picture/shape/text box. The
        // chart writer's relationship ids ("rIdFreeXChart..." / "rIdFreeXChartHyperlink...") can never
        // collide with the ids allocated below ("rIdFreeXPicture..."/"rIdFreeXLinkedPicture..."/
        // "rIdFreeXPictureSvg..."/"rIdFreeXObjectHyperlink..."), so they are carried verbatim.
        var preservedAnchors = new List<XElement>();
        var preservedRelationships = new List<XElement>();
        if (preserveExistingContent)
        {
            if (archive.GetEntry(drawingPath) is { } existingDrawingEntry &&
                XlsxPackageXmlEditor.LoadXml(existingDrawingEntry).Root is { } existingRoot)
            {
                preservedAnchors.AddRange(existingRoot.Elements().Select(anchor => new XElement(anchor)));
            }

            if (archive.GetEntry(drawingRelsPath) is { } existingRelsEntry &&
                XlsxPackageXmlEditor.LoadXml(existingRelsEntry).Root is { } existingRelsRoot)
            {
                preservedRelationships.AddRange(existingRelsRoot
                    .Elements(packageRelNs + "Relationship")
                    .Select(relationship => new XElement(relationship)));
            }
        }

        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships", preservedRelationships));
        var anchors = new List<XElement>(preservedAnchors);
        var nextPictureIndex = pictureIndex;
        var shapeIndex = 1;
        var hyperlinkRelIndex = 1;

        // R97-model-drawing-hyperlink-2-2: builds the rebuilt anchor's <a:hlinkClick> element for an
        // object, PREFERRING the object's own DrawingShapeModel/TextBoxModel/PictureModel.Hyperlink
        // field (populated for every loaded object, and carried through clone/paste --
        // DuplicateSheetDrawingCloner, PasteShapesCommand/PasteTextBoxesCommand/PastePicturesCommand)
        // and falling back to the R95 mechanism -- re-reading the pre-rebuild hyperlink from the TRUE
        // source .xlsx package by the object's stable cNvPr@name (oldObjectHyperlinksByName) -- only
        // when the model itself carries none. The fallback keeps a plain source-loaded object (whose
        // model was never populated by an older snapshot / a caller that skips the model field)
        // round-tripping exactly as it did before the model field existed; the model is preferred so a
        // CLONE (which has no source-package entry under its own sheet's name at all) still gets its
        // hyperlink. Returns null when the object has no hyperlink from either source.
        XElement? BuildObjectHyperlinkElement(DrawingObjectHyperlink? modelHyperlink, string? name)
        {
            string target;
            string? targetMode;
            string? tooltip = null;
            if (modelHyperlink is not null)
            {
                target = modelHyperlink.Target;
                targetMode = modelHyperlink.TargetMode;
                tooltip = modelHyperlink.Tooltip;
            }
            else if (!string.IsNullOrEmpty(name) && oldObjectHyperlinksByName.TryGetValue(name, out var oldHyperlink))
            {
                target = oldHyperlink.Target;
                targetMode = oldHyperlink.TargetMode;
            }
            else
            {
                return null;
            }

            var relId = "rIdFreeXObjectHyperlink" + hyperlinkRelIndex++;
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", relId),
                new XAttribute("Type", HyperlinkRelationshipType),
                new XAttribute("Target", target),
                string.IsNullOrWhiteSpace(targetMode) ? null : new XAttribute("TargetMode", targetMode)));
            return new XElement(
                drawingNs + "hlinkClick",
                new XAttribute(relNs + "id", relId),
                string.IsNullOrWhiteSpace(tooltip) ? null : new XAttribute("tooltip", tooltip));
        }
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

            if (!string.IsNullOrWhiteSpace(picture.LinkedImageTarget))
            {
                // R65-io-image-drawing-6-1: a linked ("Link to File") picture has no raster to embed at
                // all -- emit the same r:link + External relationship the source package used, instead
                // of an r:embed + embedded media file. Checked before the "no raster" CellRangeSnapshot
                // branch below: both have empty ImageBytes, but this one must never be reconstructed as
                // a vector grpSp -- it has real (external) picture content, just not embedded here.
                var linkRelId = $"rIdFreeXLinkedPicture{currentPictureIndex}";
                drawingRelsXml.Root!.Add(new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", linkRelId),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", picture.LinkedImageTarget),
                    new XAttribute("TargetMode", "External")));
                anchors.Add(ToOneCellLinkedPictureAnchor(
                    picture,
                    currentPictureIndex,
                    linkRelId,
                    spreadsheetDrawingNs,
                    drawingNs,
                    relNs,
                    BuildObjectHyperlinkElement(picture.Hyperlink, picture.Name)));
                return;
            }

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

            // R80-io-drawing-image-5-3: re-emit the vector .svg part alongside the PNG fallback (with
            // the asvg:svgBlip extension pointing at it) so a picture inserted via Excel's Insert >
            // Icons/SVG keeps its vector editability instead of permanently downgrading to a flat
            // raster the moment it is edited (crop/rotate/recolor/resize all clear IsSourceLoaded and
            // route the picture through this fresh-emission path).
            string? svgRelId = null;
            if (picture.SvgImageBytes is { Length: > 0 })
            {
                var svgMediaPath = $"xl/media/freexPictureSvg{currentPictureIndex}.svg";
                archive.GetEntry(svgMediaPath)?.Delete();
                var svgMediaEntry = archive.CreateEntry(svgMediaPath);
                using (var svgMediaStream = svgMediaEntry.Open())
                    svgMediaStream.Write(picture.SvgImageBytes);
                XlsxPackageXmlEditor.EnsureDefaultContentType(archive, "svg", "image/svg+xml");

                svgRelId = $"rIdFreeXPictureSvg{currentPictureIndex}";
                drawingRelsXml.Root!.Add(new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", svgRelId),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(drawingPath, svgMediaPath))));
            }

            anchors.Add(ToOneCellPictureAnchor(
                picture,
                currentPictureIndex,
                imageRelId,
                spreadsheetDrawingNs,
                drawingNs,
                relNs,
                svgRelId,
                BuildObjectHyperlinkElement(picture.Hyperlink, picture.Name)));
        }

        void AddTextBoxAnchor(TextBoxModel textBox)
        {
            anchors.Add(ToOneCellTextBoxAnchor(
                textBox,
                shapeIndex++,
                spreadsheetDrawingNs,
                drawingNs,
                relNs,
                BuildObjectHyperlinkElement(textBox.Hyperlink, textBox.Name)));
        }

        void AddShapeAnchor(DrawingShapeModel shape)
        {
            anchors.Add(ToOneCellDrawingShapeAnchor(
                shape,
                shapeIndex++,
                BuildObjectHyperlinkElement(shape.Hyperlink, shape.Name),
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

    /// <summary>
    /// R95-io-drawing-hyperlink-2-2: reads a drawing part's picture/text-box/shape/connector
    /// <c>xdr:cNvPr</c> elements (i.e. everything under <c>xdr:nvPicPr</c>, <c>xdr:nvSpPr</c>, or
    /// <c>xdr:nvCxnSpPr</c> -- deliberately excluding <c>xdr:nvGraphicFramePr</c>, which is chart
    /// territory handled separately by <c>XlsxWorksheetChartWriter.ReadOldChartGraphicFrameHyperlinks</c>)
    /// and resolves each one's object-level hyperlink (an <c>a:hlinkClick</c> on its <c>cNvPr</c>) via
    /// the drawing's OWN relationships part. Returns a name-keyed map (ordinal, matching
    /// <see cref="GetRewrittenSourceObjectNames"/>'s comparer) so the caller can re-attach a hyperlink
    /// to the SAME logical object once its anchor is rebuilt from the (now-edited) model -- which,
    /// unlike the positionally-matched chart case, may reorder/add/remove pictures, text boxes and
    /// shapes independently of one another, so position cannot be used as the key. Returns an empty map
    /// if the drawing part doesn't exist or can't be parsed (nothing to preserve).
    /// <para>
    /// Called by <c>XlsxFileAdapter.GetSourceDrawingObjectHyperlinksBySheet</c> against the TRUE
    /// source .xlsx package (not the in-progress generated package -- see the caller-side comment on
    /// <see cref="WriteWorksheetDrawingObjects"/> for why the generated package can't be used here).
    /// </para>
    /// </summary>
    internal static Dictionary<string, (string Target, string? TargetMode)> ReadOldDrawingObjectHyperlinksByName(
        ZipArchive archive,
        string drawingPath,
        string drawingRelsPath,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
        if (archive.GetEntry(drawingPath) is not { } oldDrawingEntry)
            return result;

        XDocument oldDrawingXml;
        try
        {
            oldDrawingXml = XlsxPackageXmlEditor.LoadXml(oldDrawingEntry);
        }
        catch
        {
            return result;
        }

        var oldRelTargets = new Dictionary<string, (string Target, string? TargetMode)>(StringComparer.OrdinalIgnoreCase);
        if (archive.GetEntry(drawingRelsPath) is { } oldRelsEntry)
        {
            try
            {
                var oldRelsXml = XlsxPackageXmlEditor.LoadXml(oldRelsEntry);
                foreach (var rel in oldRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
                {
                    var id = rel.Attribute("Id")?.Value;
                    var target = rel.Attribute("Target")?.Value;
                    if (string.IsNullOrEmpty(id) || target is null)
                        continue;
                    oldRelTargets[id] = (target, rel.Attribute("TargetMode")?.Value);
                }
            }
            catch
            {
                // Malformed rels part: fall through with no resolvable relationships, so every
                // hyperlink below resolves to nothing (nothing preserved for this drawing part).
            }
        }

        foreach (var cNvPr in oldDrawingXml.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            var parentLocalName = cNvPr.Parent?.Name.LocalName;
            if (parentLocalName is not ("nvPicPr" or "nvSpPr" or "nvCxnSpPr"))
                continue;

            var name = cNvPr.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var relId = cNvPr.Element(drawingNs + "hlinkClick")?.Attribute(relNs + "id")?.Value;
            if (relId is not null && oldRelTargets.TryGetValue(relId, out var resolved))
                result[name] = resolved;
        }

        return result;
    }

    private static XElement ToOneCellPictureAnchor(
        PictureModel picture,
        int pictureIndex,
        string imageRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        string? svgRelId = null,
        XElement? hlinkClick = null) =>
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
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText),
                        // R95/R97-io-drawing-hyperlink: re-attach the object-level hyperlink (from the
                        // model's own Hyperlink field, or -- for a plain source-loaded object -- the
                        // pre-rebuild drawing part; see BuildObjectHyperlinkElement). CT_NonVisualDrawingProps
                        // element order is hlinkClick?, hlinkHover?, extLst? -- must precede the decorative
                        // extLst below.
                        hlinkClick,
                        ToDecorativeExtLst(drawingNs, picture.IsDecorative)),
                    new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                new XElement(spreadsheetDrawingNs + "blipFill",
                    new XElement(drawingNs + "blip",
                        new XAttribute(relNs + "embed", imageRelId),
                        // R80-io-drawing-image-5-3: the Microsoft SVG extension -- keeps the picture
                        // editable as a vector (recolor, "Convert to Shape") in Excel instead of only
                        // ever carrying the PNG fallback embedded above.
                        svgRelId is null
                            ? null
                            : new XElement(drawingNs + "extLst",
                                new XElement(drawingNs + "ext",
                                    new XAttribute("uri", "{96DAC541-7B7A-43D3-8B79-37D633B846F1}"),
                                    new XElement(XNamespace.Get("http://schemas.microsoft.com/office/drawing/2016/SVG/main") + "svgBlip",
                                        new XAttribute(XNamespace.Xmlns + "asvg", "http://schemas.microsoft.com/office/drawing/2016/SVG/main"),
                                        new XAttribute(relNs + "embed", svgRelId))))),
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
    /// R65-io-image-drawing-6-1: emits a picture inserted via Excel's "Link to File" -- identical to
    /// <see cref="ToOneCellPictureAnchor"/> except the <c>&lt;a:blip&gt;</c> carries <c>r:link</c>
    /// (not <c>r:embed</c>), pointing at the External relationship <paramref name="linkRelId"/> added
    /// alongside it (see <c>AddPictureAnchor</c>). No embedded raster exists to write, so there is no
    /// media file and no default-content-type registration for this picture.
    /// </summary>
    private static XElement ToOneCellLinkedPictureAnchor(
        PictureModel picture,
        int pictureIndex,
        string linkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        XElement? hlinkClick = null) =>
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
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText),
                        // R95/R97-io-drawing-hyperlink: see ToOneCellPictureAnchor's matching comment.
                        hlinkClick,
                        ToDecorativeExtLst(drawingNs, picture.IsDecorative)),
                    new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                new XElement(spreadsheetDrawingNs + "blipFill",
                    new XElement(drawingNs + "blip", new XAttribute(relNs + "link", linkRelId)),
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
                        string.IsNullOrWhiteSpace(picture.AltText) ? null : new XAttribute("descr", picture.AltText),
                        // R119-io-camera-linked-picture-identity: replaces the plain
                        // ToDecorativeExtLst call with one that ALSO records
                        // IsLinkedToSourceRange/LinkedSourceRange/LinkedSourceSheetName (plus the
                        // source row/column count needed to remap each per-cell shape's cached
                        // RowOffset/ColumnOffset back onto a PictureModel.Cells entry) in a
                        // FreeX-specific extLst extension. Without this, a "camera" / Paste
                        // Special > Linked Picture object permanently lost its picture identity
                        // and live link on every save+reload -- XlsxWorksheetDrawingParts'
                        // ReadShapeParts had nothing telling it this group of rectangles was ever
                        // one linked picture, so it flattened the group into independent,
                        // ungrouped DrawingShapeModel/TextBoxModel objects with no way back.
                        ToPictureSnapshotGroupExtLst(drawingNs, picture)),
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

    /// <summary>
    /// R90-app-accessibility-checker-5-2: emits the <c>&lt;a:extLst&gt;&lt;a:ext
    /// uri="{C183D7F6-B498-43B3-948B-1728B52AA6E4}"&gt;&lt;adec:decorative val="1"/&gt;</c>
    /// extension (the same one Word/PowerPoint/Excel 2019+ use for "Mark as decorative") as the
    /// last child of a <c>&lt;xdr:cNvPr&gt;</c>, or <see langword="null"/> when
    /// <paramref name="isDecorative"/> is false — matching <see cref="XlsxWorksheetDrawingPartReader.DrawingMlDecorativeExtensionUri"/>
    /// so a decorative picture stays exempt from the Accessibility Checker's Missing Alt Text rule
    /// after opening and resaving.
    /// </summary>
    private static XElement? ToDecorativeExtLst(XNamespace drawingNs, bool isDecorative)
    {
        if (!isDecorative)
            return null;

        XNamespace decorativeNs = "http://schemas.microsoft.com/office/drawing/2017/decorative";
        return new XElement(drawingNs + "extLst",
            new XElement(drawingNs + "ext",
                new XAttribute("uri", XlsxWorksheetDrawingPartReader.DrawingMlDecorativeExtensionUri),
                new XElement(decorativeNs + "decorative",
                    new XAttribute(XNamespace.Xmlns + "adec", decorativeNs.NamespaceName),
                    new XAttribute("val", "1"))));
    }

    /// <summary>
    /// R119-io-camera-linked-picture-identity: builds the <c>&lt;xdr:cNvPr&gt;&lt;a:extLst&gt;</c>
    /// for a reconstructed CellRangeSnapshot picture's <c>&lt;xdr:grpSp&gt;</c>
    /// (<see cref="ToOneCellPictureSnapshotAnchor"/>), combining the existing "Mark as decorative"
    /// extension (when applicable) with a FreeX-specific <c>fx:linkedPictureSnapshot</c> extension
    /// that records everything <see cref="XlsxWorksheetDrawingParts.ReadPictureSnapshotGroupParts"/>
    /// needs to rebuild a single linked/unlinked <see cref="PictureModel"/> from the group instead of
    /// flattening its per-cell rectangles into independent shapes on load. <c>a:extLst</c> permits at
    /// most one child per CT_NonVisualDrawingProps (ECMA-376), so both extensions must share the same
    /// extLst rather than each contributing their own.
    /// </summary>
    private static XElement ToPictureSnapshotGroupExtLst(XNamespace drawingNs, PictureModel picture)
    {
        XNamespace freexNs = "http://schemas.freexapp.com/drawing/2026/camera";
        var marker = new XElement(freexNs + "linkedPictureSnapshot",
            new XAttribute(XNamespace.Xmlns + "fx", freexNs.NamespaceName),
            new XAttribute("isLinked", picture.IsLinkedToSourceRange ? "1" : "0"),
            new XAttribute("sourceRowCount", picture.SourceRowCount),
            new XAttribute("sourceColCount", picture.SourceColumnCount));
        if (picture.IsLinkedToSourceRange && picture.LinkedSourceRange is { } sourceRange)
        {
            marker.Add(
                new XAttribute("sourceStartRow", sourceRange.Start.Row),
                new XAttribute("sourceStartCol", sourceRange.Start.Col),
                new XAttribute("sourceEndRow", sourceRange.End.Row),
                new XAttribute("sourceEndCol", sourceRange.End.Col));
            if (!string.IsNullOrWhiteSpace(picture.LinkedSourceSheetName))
                marker.Add(new XAttribute("sourceSheet", picture.LinkedSourceSheetName));
        }

        var markerExt = new XElement(drawingNs + "ext",
            new XAttribute("uri", XlsxWorksheetDrawingPartReader.CellRangeSnapshotGroupExtensionUri),
            marker);

        XElement? decorativeExt = null;
        if (picture.IsDecorative)
        {
            XNamespace decorativeNs = "http://schemas.microsoft.com/office/drawing/2017/decorative";
            decorativeExt = new XElement(drawingNs + "ext",
                new XAttribute("uri", XlsxWorksheetDrawingPartReader.DrawingMlDecorativeExtensionUri),
                new XElement(decorativeNs + "decorative",
                    new XAttribute(XNamespace.Xmlns + "adec", decorativeNs.NamespaceName),
                    new XAttribute("val", "1")));
        }

        return new XElement(drawingNs + "extLst", decorativeExt, markerExt);
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        // R80-io-drawing-image-5-2: a NEGATIVE crop inset (Excel's "crop past the image edge"
        // outward padding) is also a real crop that must be written -- checking only "> 0" treated a
        // negative-only crop as "no crop" and silently dropped the whole srcRect on save.
        picture.CropLeft != 0 ||
        picture.CropTop != 0 ||
        picture.CropRight != 0 ||
        picture.CropBottom != 0;

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
        // R80-io-drawing-image-5-2: preserve negative (outward-crop/padding) ratios -- only clamp the
        // magnitude to Excel's ±100% bound, matching ReadSourceRectangleRatio's [-1, 1] range.
        ((int)Math.Round(Math.Clamp(ratio, -1, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

    private static XElement ToOneCellTextBoxAnchor(
        TextBoxModel textBox,
        int shapeIndex,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        XElement? hlinkClick = null) =>
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
                        string.IsNullOrWhiteSpace(textBox.AltText) ? null : new XAttribute("descr", textBox.AltText),
                        // R95/R97-io-drawing-hyperlink: re-attach the object-level hyperlink (model
                        // field, or -- for a plain source-loaded object -- the pre-rebuild drawing
                        // part; see BuildObjectHyperlinkElement).
                        hlinkClick),
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
                    drawingNs,
                    // R91-commands-insert-object-5-1: round-trip an explicitly line-suppressed text
                    // box (loaded or freshly inserted) as <a:ln><a:noFill/> instead of silently
                    // baking in a border Excel/the user never authored.
                    outlineHasNoFill: textBox.OutlineHasNoFill),
                ToTextBoxTxBody(textBox, drawingNs, spreadsheetDrawingNs)),
            new XElement(spreadsheetDrawingNs + "clientData"));

    /// <summary>
    /// Builds the <c>&lt;xdr:txBody&gt;</c> element for a text box, round-tripping the font
    /// formatting fields added to <see cref="TextBoxModel"/> for backlog textbox-6-2 (font family,
    /// size, bold, italic, color, horizontal/vertical alignment). Mirrors <see cref="ToShapeTxBody"/>
    /// (which does the same for <see cref="DrawingShapeModel"/>) but stays single-run/single-paragraph
    /// -- unlike shapes, a text box's <see cref="TextBoxModel.Text"/> was never split into one
    /// <c>&lt;a:p&gt;</c> per line here, and adding that behavior is a separate, unrelated concern
    /// from round-tripping formatting.
    /// </summary>
    private static XElement ToTextBoxTxBody(
        TextBoxModel textBox,
        XNamespace drawingNs,
        XNamespace spreadsheetDrawingNs)
    {
        var anchorValue = textBox.TextVAnchor switch
        {
            DrawingShapeTextVAnchor.Bottom => "b",
            DrawingShapeTextVAnchor.Middle => "ctr",
            _ => "t", // DrawingShapeTextVAnchor.Top (also the TextBoxModel default)
        };
        var algnValue = textBox.TextHAlign switch
        {
            DrawingShapeTextHAlign.Center => "ctr",
            DrawingShapeTextHAlign.Right => "r",
            _ => "l", // DrawingShapeTextHAlign.Left (also the TextBoxModel default)
        };

        var rPr = new XElement(drawingNs + "rPr",
            new XAttribute("lang", "en-US"),
            new XAttribute("dirty", "0"));
        if (textBox.TextFontSizePoints > 0)
            rPr.Add(new XAttribute("sz", ((int)Math.Round(textBox.TextFontSizePoints * 100)).ToString(CultureInfo.InvariantCulture)));
        if (textBox.TextBold)
            rPr.Add(new XAttribute("b", "1"));
        if (textBox.TextItalic)
            rPr.Add(new XAttribute("i", "1"));

        // CT_TextCharacterProperties child order (ECMA-376 §21.1.2.3.9): fill group before latin.
        var textFill = ToSolidFill(textBox.TextThemeColor, textBox.TextColor, drawingNs);
        if (textFill is not null)
            rPr.Add(textFill);
        if (!string.IsNullOrWhiteSpace(textBox.TextFontFamily))
            rPr.Add(new XElement(drawingNs + "latin", new XAttribute("typeface", textBox.TextFontFamily)));

        return new XElement(spreadsheetDrawingNs + "txBody",
            new XElement(drawingNs + "bodyPr", new XAttribute("anchor", anchorValue)),
            new XElement(drawingNs + "lstStyle"),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr", new XAttribute("algn", algnValue)),
                new XElement(drawingNs + "r",
                    rPr,
                    new XElement(drawingNs + "t", textBox.Text))));
    }

    private static XElement ToOneCellDrawingShapeAnchor(
        DrawingShapeModel shape,
        int shapeIndex,
        XElement? hlinkClick,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs)
    {
        // R95/R97-io-drawing-hyperlink: re-attach the object-level hyperlink (model field, or -- for
        // a plain source-loaded object -- the pre-rebuild drawing part; see
        // BuildObjectHyperlinkElement). Shared between the cxnSp and sp branches below since both
        // cNvPr elements carry it identically.
        var shapeProperties = ToShapePropertiesForDrawingObject(
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
            shape.TailArrowhead,
            shape.AdjustValues);

        // R78-io-shape-geometry-5-2: connector kinds (Line/ElbowConnector/CurvedConnector) must be
        // packaged as <xdr:cxnSp> (with <xdr:nvCxnSpPr>/<xdr:cNvCxnSpPr>), not the generic <xdr:sp> --
        // otherwise Excel treats the object as a plain autoshape: no connection-site glue, and it is
        // listed as a generic shape rather than "Connector" in the Selection Pane. Connectors carry no
        // txBody per the OOXML CT_Connector schema, so text (which they never have anyway) is omitted.
        XElement shapeOrConnectorElement = DrawingShapeKindSupport.IsLineLike(shape.Kind)
            ? new XElement(spreadsheetDrawingNs + "cxnSp",
                new XElement(spreadsheetDrawingNs + "nvCxnSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 200),
                        new XAttribute("name", DrawingName(shape.Name, $"Shape {shapeIndex}")),
                        string.IsNullOrWhiteSpace(shape.Title) ? null : new XAttribute("title", shape.Title),
                        string.IsNullOrWhiteSpace(shape.AltText) ? null : new XAttribute("descr", shape.AltText),
                        hlinkClick),
                    new XElement(spreadsheetDrawingNs + "cNvCxnSpPr",
                        // R90-shape-5-3: preserve which shapes this connector's endpoints were glued
                        // to (stCxn/endCxn) so a connector loaded from a source file that goes through
                        // this regenerated-element path (e.g. after any other property edit) doesn't
                        // silently lose its shape attachment on save.
                        ToConnectionSiteElement(drawingNs, "stCxn", shape.StartConnectedShapeId, shape.StartConnectedShapeConnectionIndex),
                        ToConnectionSiteElement(drawingNs, "endCxn", shape.EndConnectedShapeId, shape.EndConnectedShapeConnectionIndex))),
                shapeProperties)
            : new XElement(spreadsheetDrawingNs + "sp",
                new XElement(spreadsheetDrawingNs + "nvSpPr",
                    new XElement(spreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", shapeIndex + 200),
                        new XAttribute("name", DrawingName(shape.Name, $"Shape {shapeIndex}")),
                        string.IsNullOrWhiteSpace(shape.Title) ? null : new XAttribute("title", shape.Title),
                        string.IsNullOrWhiteSpace(shape.AltText) ? null : new XAttribute("descr", shape.AltText),
                        hlinkClick),
                    new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                shapeProperties,
                shape.HasShapeText ? ToShapeTxBody(shape, drawingNs, spreadsheetDrawingNs) : null);

        return new(spreadsheetDrawingNs + "oneCellAnchor",
            ToDrawingAnchorFrom(shape.Anchor, spreadsheetDrawingNs, shape.AnchorOffsetX, shape.AnchorOffsetY),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlUnits.PixelsToEmu(shape.Width)),
                new XAttribute("cy", DrawingMlUnits.PixelsToEmu(shape.Height))),
            shapeOrConnectorElement,
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    /// <summary>
    /// R90-shape-5-3: builds a <c>&lt;a:stCxn id="..." idx="..."/&gt;</c>/<c>&lt;a:endCxn .../&gt;</c>
    /// connection-site element, or <see langword="null"/> when <paramref name="shapeId"/> is null
    /// (the connector endpoint is not glued to any shape).
    /// </summary>
    private static XElement? ToConnectionSiteElement(XNamespace drawingNs, string elementName, int? shapeId, int? connectionIndex) =>
        shapeId is null
            ? null
            : new XElement(drawingNs + elementName,
                new XAttribute("id", shapeId.Value),
                new XAttribute("idx", connectionIndex ?? 0));

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
        DrawingArrowhead? tailArrowhead = null,
        IReadOnlyList<DrawingShapeAdjustValue>? adjustValues = null)
    {
        return new XElement(spreadsheetDrawingNs + "spPr",
            ToDrawingTransform(rotationDegrees, flipHorizontal, flipVertical, drawingNs,
                shapeWidthPixels, shapeHeightPixels),
            new XElement(drawingNs + "prstGeom",
                new XAttribute("prst", preset),
                ToAdjustValueList(adjustValues, drawingNs)),
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

    /// <summary>
    /// Builds the <c>&lt;a:avLst&gt;</c> child of <c>&lt;a:prstGeom&gt;</c>, emitting one <c>&lt;a:gd&gt;</c>
    /// per preserved adjust-handle value (R78-io-shape-geometry-5-3). An empty <c>&lt;a:avLst&gt;</c> --
    /// which is what OOXML uses to mean "use this preset's built-in default handle positions" -- is
    /// emitted when no adjust values were preserved, matching prior behavior for shapes that never had
    /// a customized handle.
    /// </summary>
    private static XElement ToAdjustValueList(IReadOnlyList<DrawingShapeAdjustValue>? adjustValues, XNamespace drawingNs)
    {
        var avLst = new XElement(drawingNs + "avLst");
        if (adjustValues is null)
            return avLst;

        foreach (var adjustValue in adjustValues)
        {
            if (string.IsNullOrEmpty(adjustValue.Name) || string.IsNullOrEmpty(adjustValue.Formula))
                continue;

            avLst.Add(new XElement(drawingNs + "gd",
                new XAttribute("name", adjustValue.Name),
                new XAttribute("fmla", adjustValue.Formula)));
        }

        return avLst;
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
        // R65-io-image-drawing-6-1: a linked ("Link to File") picture has no embedded raster --
        // ImageBytes is always empty for it -- but it still carries a non-null LinkedImageTarget, so it
        // must be accepted here too; otherwise it would be silently dropped the same way an edited
        // linked picture used to vanish (the picture is "supported" via its r:link + external
        // relationship instead of an embedded image part -- see AddPictureAnchor).
        (picture.ImageBytes is { Length: > 0 } ||
         picture.Kind == PictureKind.CellRangeSnapshot ||
         !string.IsNullOrWhiteSpace(picture.LinkedImageTarget));

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
