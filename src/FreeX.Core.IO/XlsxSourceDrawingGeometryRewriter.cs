using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// F15 fix: source-loaded drawing objects (pictures/shapes/text boxes originally loaded from the .xlsx)
/// are never emitted by <see cref="XlsxWorksheetDrawingObjectWriter"/> — it gates every object behind
/// <c>!IsSourceLoaded</c> — because their drawing part is instead PRESERVED verbatim by copying the
/// original drawing XML from the source package (see <see cref="XlsxFileAdapter"/>'s
/// <c>PreserveSourcePackageParts</c>/<c>XlsxWorksheetDrawingPartMerger</c>). That verbatim copy replays the
/// ORIGINAL anchor geometry, so a resize/move applied to the in-memory model (<see cref="PictureModel.Width"/>/
/// <see cref="PictureModel.Height"/>/<c>AnchorOffsetX</c>/<c>AnchorOffsetY</c>, and the equivalents on
/// <see cref="TextBoxModel"/>/<see cref="DrawingShapeModel"/>) was silently discarded even on a full save.
/// <para>
/// This rewriter runs AFTER the source drawing parts have been copied/merged into the generated package
/// (so it edits the part at its final path) and rewrites each anchor's sub-cell offset and size/`to`-marker
/// to match the current in-memory model, using the same EMU/pixel math and marker math as the reader
/// (<see cref="XlsxWorksheetDrawingPartReader"/>) and the model writer (<see cref="XlsxWorksheetChartWriter"/>),
/// so a save-then-reload round-trips the new geometry within the ±1px tolerance the reader itself uses.
/// </para>
/// </summary>
internal static class XlsxSourceDrawingGeometryRewriter
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>True when any sheet has at least one source-loaded picture/text box/shape, i.e. this
    /// rewriter has anything to do. Cheap gate for <see cref="XlsxFileAdapter"/>'s feature plan.</summary>
    public static bool HasSourceLoadedDrawingObjects(Sheet sheet) =>
        sheet.Pictures.Any(picture => picture.IsSourceLoaded) ||
        sheet.TextBoxes.Any(textBox => textBox.IsSourceLoaded) ||
        sheet.DrawingShapes.Any(shape => shape.IsSourceLoaded);

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var sheet in workbook.Sheets)
        {
            if (!HasSourceLoadedDrawingObjects(sheet))
                continue;

            var worksheetPath = worksheetPathMap?.SheetPathsByName.GetValueOrDefault(sheet.Name);
            if (string.IsNullOrWhiteSpace(worksheetPath))
                continue;

            var drawingPath = ResolveWorksheetDrawingPath(archive, worksheetPath);
            if (string.IsNullOrWhiteSpace(drawingPath))
                continue;

            var drawingEntry = archive.GetEntry(drawingPath);
            if (drawingEntry is null)
                continue;

            var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            if (drawingXml.Root is null)
                continue;

            if (RewriteDrawingGeometry(drawingXml.Root, sheet))
                XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
        }
    }

    private static string? ResolveWorksheetDrawingPath(ZipArchive archive, string worksheetPath)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return null;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var drawingRelId = worksheetXml.Root?
            .Element(WorkbookNs + "drawing")?
            .Attribute(RelNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            archive,
            XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
            worksheetPath,
            PackageRelNs);
        return worksheetRels.TryGetValue(drawingRelId, out var drawingPath)
            ? XlsxPackagePath.NormalizePackagePath(drawingPath)
            : null;
    }

    /// <summary>
    /// Walks the drawing's anchors in the same document order the reader uses
    /// (<see cref="XlsxWorksheetDrawingPartReader"/>: all &lt;xdr:pic&gt; in order, then all &lt;xdr:sp&gt;
    /// in order classified into text boxes vs shapes exactly like the reader, then all &lt;xdr:cxnSp&gt;
    /// appended to the shapes sequence).
    /// <para>
    /// <see cref="XlsxWorksheetDrawingObjectWriter"/> only ever emits NEW (non-source-loaded) objects, and
    /// always writes them BEFORE <see cref="XlsxWorksheetDrawingPartMerger"/> appends the untouched
    /// source-loaded anchors after them (it only ever appends, never reorders/interleaves relative to the
    /// writer's anchors). So within each element-kind stream (pic / classified-sp / cxnSp), the source-loaded
    /// anchors are always the trailing block, in their original source document order — the same order
    /// <see cref="Sheet.Pictures"/>/<see cref="Sheet.TextBoxes"/>/<see cref="Sheet.DrawingShapes"/> were
    /// populated in for source-loaded objects. A NEW object appended to a model list after load has no
    /// anchor of its own in that trailing block, so it must never be matched against one: doing so (matching
    /// every model in list order, source-loaded or not, against every anchor in document order) is exactly
    /// how geometry silently swapped between a new and a source-loaded object before this fix. Instead, only
    /// source-loaded models are matched, only against that trailing block, in order.
    /// </para>
    /// Returns true when at least one anchor was rewritten.
    /// </summary>
    private static bool RewriteDrawingGeometry(XElement drawingRoot, Sheet sheet)
    {
        var changed = false;

        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var sourcePictures = sheet.Pictures.Where(picture => picture.IsSourceLoaded).ToList();
        var pictureElements = drawingRoot.Descendants(SpreadsheetDrawingNs + "pic").ToList();
        var pictureAnchors = pictureElements.Skip(Math.Max(0, pictureElements.Count - sourcePictures.Count));
        foreach (var (pictureElement, picture) in pictureAnchors.Zip(sourcePictures))
        {
            var anchor = FindNearestAnchorElement(pictureElement);
            if (anchor is not null &&
                RewriteAnchorGeometry(anchor, sheet, picture.Width, picture.Height, picture.AnchorOffsetX, picture.AnchorOffsetY))
            {
                changed = true;
            }

            if (RewritePictureVisualProperties(pictureElement, picture, drawingNs))
                changed = true;
        }

        var sourceTextBoxes = sheet.TextBoxes.Where(textBox => textBox.IsSourceLoaded).ToList();
        var sourceShapes = sheet.DrawingShapes.Where(shape => shape.IsSourceLoaded).ToList();

        var textBoxElements = new List<XElement>();
        var shapeElements = new List<XElement>();
        foreach (var shapeElement in drawingRoot.Descendants(SpreadsheetDrawingNs + "sp"))
        {
            if (shapeElement.Ancestors(MarkupCompatNs + "Fallback").Any())
                continue;

            var isTextBox = shapeElement
                .Element(SpreadsheetDrawingNs + "nvSpPr")?
                .Element(SpreadsheetDrawingNs + "cNvSpPr")?
                .Attribute("txBox")?.Value == "1";
            var txBodyElement = shapeElement.Element(SpreadsheetDrawingNs + "txBody");
            var text = string.Concat(txBodyElement?.Descendants(drawingNs + "t").Select(t => t.Value) ?? []);

            if (isTextBox && !string.IsNullOrEmpty(text))
            {
                textBoxElements.Add(shapeElement);
                continue;
            }

            var preset = shapeElement
                .Element(SpreadsheetDrawingNs + "spPr")?
                .Element(drawingNs + "prstGeom")?
                .Attribute("prst")?
                .Value;
            if (DrawingMlPresetGeometryMap.TryGetShapeKind(preset, out _))
                shapeElements.Add(shapeElement);
        }

        foreach (var connectorElement in drawingRoot.Descendants(SpreadsheetDrawingNs + "cxnSp"))
        {
            if (connectorElement.Ancestors(MarkupCompatNs + "Fallback").Any())
                continue;

            shapeElements.Add(connectorElement);
        }

        var textBoxAnchors = textBoxElements.Skip(Math.Max(0, textBoxElements.Count - sourceTextBoxes.Count));
        foreach (var (textBoxElement, textBox) in textBoxAnchors.Zip(sourceTextBoxes))
        {
            var anchor = FindNearestAnchorElement(textBoxElement);
            if (anchor is not null &&
                RewriteAnchorGeometry(anchor, sheet, textBox.Width, textBox.Height, textBox.AnchorOffsetX, textBox.AnchorOffsetY))
            {
                changed = true;
            }

            if (RewriteTextBoxVisualProperties(textBoxElement, textBox, drawingNs))
                changed = true;
        }

        var shapeAnchors = shapeElements.Skip(Math.Max(0, shapeElements.Count - sourceShapes.Count));
        foreach (var (shapeElement, shape) in shapeAnchors.Zip(sourceShapes))
        {
            var anchor = FindNearestAnchorElement(shapeElement);
            if (anchor is not null &&
                RewriteAnchorGeometry(anchor, sheet, shape.Width, shape.Height, shape.AnchorOffsetX, shape.AnchorOffsetY))
            {
                changed = true;
            }

            if (RewriteShapeAltTextAndTitle(shapeElement, shape.AltText, shape.Title))
                changed = true;
        }

        return changed;
    }

    /// <summary>
    /// R17 fix: beyond anchor geometry, an edited source-loaded text box's body text
    /// (<c>SetTextBoxTextCommand</c> mutates <see cref="TextBoxModel.Text"/> without clearing
    /// <see cref="TextBoxModel.IsSourceLoaded"/>) and its alt text/title (<c>cNvPr@descr</c>/
    /// <c>@title</c>) must be patched into the preserved drawing XML the same way
    /// <see cref="RewritePictureVisualProperties"/> already does for pictures, so a save-then-reload
    /// keeps the edit instead of silently replaying the original source text. Returns true when the
    /// XML was modified.
    /// </summary>
    private static bool RewriteTextBoxVisualProperties(XElement textBoxElement, TextBoxModel textBox, XNamespace drawingNs)
    {
        var changed = RewriteShapeAltTextAndTitle(textBoxElement, textBox.AltText, textBox.Title);

        var txBody = textBoxElement.Element(SpreadsheetDrawingNs + "txBody");
        if (txBody is not null && RewriteTextBodyPlainText(txBody, textBox.Text ?? "", drawingNs))
            changed = true;

        return changed;
    }

    /// <summary>
    /// R17-drawing-hyperlink-name-3 fix: patches <c>cNvPr@descr</c>/<c>@title</c> for a shape,
    /// connector (<c>xdr:cxnSp</c>), or text box element — <see cref="RewritePictureVisualProperties"/>
    /// already did this for pictures, but the shape/text-box loops never patched it, silently
    /// dropping an alt-text/title edit on a source-loaded shape or text box. Uses
    /// <c>Descendants</c> (not a fixed <c>nvSpPr</c>/<c>nvCxnSpPr</c> element chain) so it finds the
    /// <c>cNvPr</c> regardless of which non-visual-properties wrapper the element uses, mirroring
    /// <c>XlsxWorksheetDrawingParts.ReadFirstNonVisualAttribute</c>. Returns true when the XML was
    /// modified.
    /// </summary>
    private static bool RewriteShapeAltTextAndTitle(XElement element, string? altText, string? title)
    {
        var cNvPr = element.Descendants(SpreadsheetDrawingNs + "cNvPr").FirstOrDefault();
        if (cNvPr is null)
            return false;

        var changed = false;
        changed |= SetOrRemoveAttribute(cNvPr, "descr", string.IsNullOrWhiteSpace(altText) ? null : altText);
        changed |= SetOrRemoveAttribute(cNvPr, "title", string.IsNullOrWhiteSpace(title) ? null : title);
        return changed;
    }

    /// <summary>
    /// Patches a preserved <c>&lt;xdr:txBody&gt;</c>'s <c>&lt;a:t&gt;</c> run text so it matches
    /// <paramref name="newText"/> (the in-memory <see cref="TextBoxModel.Text"/>, which uses
    /// <c>\n</c> as its paragraph separator — see
    /// <c>XlsxWorksheetDrawingParts.ReadShapeTextBodyPlainText</c>), while leaving each paragraph's
    /// run/formatting elements (<c>rPr</c> etc.) untouched. Only the FIRST run (or field) in each
    /// paragraph receives the new text; any additional runs/fields/line-breaks in that paragraph are
    /// removed, mirroring the "one run per line" simplification the reader/writer already use for
    /// shape/text-box text (<c>ReadShapeTextBodyPlainText</c> / <c>ToShapeTxBody</c>). When the new
    /// text has more or fewer lines than the preserved body has paragraphs, trailing paragraphs are
    /// cloned from (or trimmed down from) the last existing paragraph so formatting still carries
    /// over onto newly-added lines. Returns true when the XML was modified.
    /// </summary>
    private static bool RewriteTextBodyPlainText(XElement txBody, string newText, XNamespace drawingNs)
    {
        var paragraphs = txBody.Elements(drawingNs + "p").ToList();
        if (paragraphs.Count == 0)
            return false;

        var lines = newText.Split('\n');
        var changed = false;

        // Grow: clone the last paragraph as a formatting template for any extra new lines.
        var template = paragraphs[^1];
        while (paragraphs.Count < lines.Length)
        {
            var clone = new XElement(template);
            template.AddAfterSelf(clone);
            paragraphs.Add(clone);
            template = clone;
            changed = true;
        }

        // Shrink: drop trailing paragraphs beyond what the new text needs.
        while (paragraphs.Count > lines.Length)
        {
            paragraphs[^1].Remove();
            paragraphs.RemoveAt(paragraphs.Count - 1);
            changed = true;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (SetParagraphPlainText(paragraphs[i], lines[i], drawingNs))
                changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Sets a single paragraph's text to <paramref name="text"/>: the first <c>&lt;a:r&gt;</c> (or
    /// <c>&lt;a:fld&gt;</c>) run's <c>&lt;a:t&gt;</c> receives the text (a bare run is created if the
    /// paragraph had none), and any additional runs/fields/<c>&lt;a:br/&gt;</c> breaks are removed so
    /// the paragraph doesn't end up with stale leftover text appended after the new content.
    /// </summary>
    private static bool SetParagraphPlainText(XElement paragraph, string text, XNamespace drawingNs)
    {
        var changed = false;
        var contentNodes = paragraph.Elements()
            .Where(e => e.Name == drawingNs + "r" || e.Name == drawingNs + "fld")
            .ToList();

        XElement firstRun;
        if (contentNodes.Count > 0)
        {
            firstRun = contentNodes[0];
            for (var i = 1; i < contentNodes.Count; i++)
            {
                contentNodes[i].Remove();
                changed = true;
            }
        }
        else
        {
            firstRun = new XElement(drawingNs + "r", new XElement(drawingNs + "t", text));
            paragraph.Add(firstRun);
            return true;
        }

        foreach (var lineBreak in paragraph.Elements(drawingNs + "br").ToList())
        {
            lineBreak.Remove();
            changed = true;
        }

        var t = firstRun.Element(drawingNs + "t");
        if (t is null)
        {
            t = new XElement(drawingNs + "t");
            firstRun.Add(t);
            changed = true;
        }

        if (!string.Equals(t.Value, text, StringComparison.Ordinal))
        {
            t.Value = text;
            changed = true;
        }

        return changed;
    }

    private static XElement? FindNearestAnchorElement(XElement element)
    {
        foreach (var candidate in element.Ancestors())
        {
            if (IsSpreadsheetDrawingAnchor(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsSpreadsheetDrawingAnchor(XElement element) =>
        element.Name == SpreadsheetDrawingNs + "twoCellAnchor" ||
        element.Name == SpreadsheetDrawingNs + "oneCellAnchor" ||
        element.Name == SpreadsheetDrawingNs + "absoluteAnchor";

    /// <summary>
    /// Rewrites one anchor's <c>from</c> sub-cell offset and size (<c>ext</c> for oneCell/absolute anchors,
    /// or the <c>to</c> marker for twoCell anchors) to match the current model geometry. The <c>from</c>
    /// cell itself is left untouched — a change of anchor CELL already fails the patch-safe geometry check
    /// and is out of scope here; only the sub-cell offset and size are rewritten. Returns true when the XML
    /// was modified.
    /// </summary>
    private static bool RewriteAnchorGeometry(
        XElement anchor,
        Sheet sheet,
        double widthPixels,
        double heightPixels,
        double offsetXPixels,
        double offsetYPixels)
    {
        var from = anchor.Element(SpreadsheetDrawingNs + "from");
        if (from is null)
            return false;

        var changed = false;
        changed |= SetOffsetElement(from, "colOff", offsetXPixels);
        changed |= SetOffsetElement(from, "rowOff", offsetYPixels);

        if (anchor.Name == SpreadsheetDrawingNs + "oneCellAnchor" ||
            anchor.Name == SpreadsheetDrawingNs + "absoluteAnchor")
        {
            var ext = anchor.Element(SpreadsheetDrawingNs + "ext");
            if (ext is not null)
            {
                changed |= SetExtentAttribute(ext, "cx", widthPixels);
                changed |= SetExtentAttribute(ext, "cy", heightPixels);
            }

            return changed;
        }

        if (anchor.Name == SpreadsheetDrawingNs + "twoCellAnchor")
        {
            var to = anchor.Element(SpreadsheetDrawingNs + "to");
            if (to is null)
                return changed;

            if (!uint.TryParse(from.Element(SpreadsheetDrawingNs + "col")?.Value, out var fromCol) ||
                !uint.TryParse(from.Element(SpreadsheetDrawingNs + "row")?.Value, out var fromRow))
            {
                return changed;
            }

            // Recompute the to-marker from the from-cell's absolute pixel position plus the new width/
            // height, using the same column-width/row-height walk the model writer uses for charts
            // (XlsxWorksheetChartWriter.ToAnchorMarker) so a save-then-reload measures the resize
            // identically to how XlsxDrawingAnchorApplier/GetAnchorSize measured it on load.
            var fromLeft = SumColumnPixels(sheet, 1, fromCol) + offsetXPixels;
            var fromTop = SumRowPixels(sheet, 1, fromRow) + offsetYPixels;
            var (toCol, toColOffset) = ToMarkerIndex(
                fromLeft + widthPixels,
                sheet.DefaultColumnWidth * 8,
                MaxColumnIndex,
                column => sheet.IsColEffectivelyHidden(column),
                column => sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8);
            var (toRow, toRowOffset) = ToMarkerIndex(
                fromTop + heightPixels,
                sheet.DefaultRowHeight,
                MaxRowIndex,
                row => sheet.IsRowEffectivelyHidden(row),
                row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight));

            changed |= SetIndexElement(to, "col", toCol);
            changed |= SetOffsetElement(to, "colOff", toColOffset);
            changed |= SetIndexElement(to, "row", toRow);
            changed |= SetOffsetElement(to, "rowOff", toRowOffset);
        }

        return changed;
    }

    /// <summary>
    /// R14-image-media-1 fix: beyond anchor geometry, a source-loaded picture's crop (<c>a:srcRect</c>),
    /// rotation/flip (<c>a:xfrm</c> <c>rot</c>/<c>flipH</c>/<c>flipV</c>), and alt text
    /// (<c>xdr:cNvPr</c> <c>descr</c>) must also be patched into the preserved drawing XML, using the same
    /// EMU/percent math as the writer (<see cref="XlsxWorksheetDrawingObjectWriter"/>) so a save-then-reload
    /// round-trips the edit exactly like a freshly-written picture. Returns true when the XML was modified.
    /// </summary>
    private static bool RewritePictureVisualProperties(XElement pictureElement, PictureModel picture, XNamespace drawingNs)
    {
        var changed = false;

        var spPr = pictureElement.Element(SpreadsheetDrawingNs + "spPr");
        var xfrm = spPr?.Element(drawingNs + "xfrm");
        if (xfrm is null && spPr is not null &&
            (NormalizeRotation(picture.RotationDegrees) != 0 || picture.FlipHorizontal || picture.FlipVertical))
        {
            // CT_ShapeProperties requires xfrm (when present) to be the first child of spPr.
            xfrm = new XElement(drawingNs + "xfrm");
            spPr.AddFirst(xfrm);
            changed = true;
        }

        if (xfrm is not null)
            changed |= SetPictureTransform(xfrm, picture);

        var blipFill = pictureElement.Element(SpreadsheetDrawingNs + "blipFill");
        if (blipFill is not null)
            changed |= SetSourceRectangle(blipFill, drawingNs, picture);

        var cNvPr = pictureElement
            .Element(SpreadsheetDrawingNs + "nvPicPr")?
            .Element(SpreadsheetDrawingNs + "cNvPr");
        if (cNvPr is not null)
            changed |= SetOrRemoveAttribute(cNvPr, "descr", string.IsNullOrWhiteSpace(picture.AltText) ? null : picture.AltText);

        return changed;
    }

    private static bool SetPictureTransform(XElement xfrm, PictureModel picture)
    {
        var rotation = NormalizeRotation(picture.RotationDegrees);
        var rotEmu = rotation == 0 ? null : ((long)Math.Round(rotation * 60000)).ToString(CultureInfo.InvariantCulture);

        var changed = false;
        changed |= SetOrRemoveAttribute(xfrm, "rot", rotEmu);
        changed |= SetOrRemoveAttribute(xfrm, "flipH", picture.FlipHorizontal ? "1" : null);
        changed |= SetOrRemoveAttribute(xfrm, "flipV", picture.FlipVertical ? "1" : null);
        return changed;
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0;
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool HasPictureCrop(PictureModel picture) =>
        picture.CropLeft > 0 ||
        picture.CropTop > 0 ||
        picture.CropRight > 0 ||
        picture.CropBottom > 0;

    private static bool SetSourceRectangle(XElement blipFill, XNamespace drawingNs, PictureModel picture)
    {
        var srcRect = blipFill.Element(drawingNs + "srcRect");
        if (!HasPictureCrop(picture))
        {
            if (srcRect is null)
                return false;

            srcRect.Remove();
            return true;
        }

        var left = ToSourceRectanglePercent(picture.CropLeft);
        var top = ToSourceRectanglePercent(picture.CropTop);
        var right = ToSourceRectanglePercent(picture.CropRight);
        var bottom = ToSourceRectanglePercent(picture.CropBottom);

        if (srcRect is not null)
        {
            var changed = false;
            changed |= SetOrRemoveAttribute(srcRect, "l", left);
            changed |= SetOrRemoveAttribute(srcRect, "t", top);
            changed |= SetOrRemoveAttribute(srcRect, "r", right);
            changed |= SetOrRemoveAttribute(srcRect, "b", bottom);
            return changed;
        }

        // CT_BlipFillProperties requires srcRect (when present) immediately after blip and before the
        // fill-mode element (stretch/tile); insert right after blip rather than appending at the end.
        var newSrcRect = new XElement(drawingNs + "srcRect",
            new XAttribute("l", left),
            new XAttribute("t", top),
            new XAttribute("r", right),
            new XAttribute("b", bottom));
        var blip = blipFill.Element(drawingNs + "blip");
        if (blip is not null)
            blip.AddAfterSelf(newSrcRect);
        else
            blipFill.AddFirst(newSrcRect);

        return true;
    }

    private static string ToSourceRectanglePercent(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

    private static bool SetOrRemoveAttribute(XElement element, string attributeName, string? value)
    {
        var existing = element.Attribute(attributeName);
        if (value is null)
        {
            if (existing is null)
                return false;

            existing.Remove();
            return true;
        }

        if (existing is not null && string.Equals(existing.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static bool SetOffsetElement(XElement marker, string elementName, double pixels)
    {
        var element = marker.Element(SpreadsheetDrawingNs + elementName);
        if (element is null)
            return false;

        var emu = DrawingMlUnits.PixelsToEmu(Math.Max(0, pixels)).ToString(CultureInfo.InvariantCulture);
        if (string.Equals(element.Value, emu, StringComparison.Ordinal))
            return false;

        element.Value = emu;
        return true;
    }

    private static bool SetIndexElement(XElement marker, string elementName, uint index)
    {
        var element = marker.Element(SpreadsheetDrawingNs + elementName);
        if (element is null)
            return false;

        var value = index.ToString(CultureInfo.InvariantCulture);
        if (string.Equals(element.Value, value, StringComparison.Ordinal))
            return false;

        element.Value = value;
        return true;
    }

    private static bool SetExtentAttribute(XElement ext, string attributeName, double pixels)
    {
        var attribute = ext.Attribute(attributeName);
        if (attribute is null)
            return false;

        var emu = DrawingMlUnits.PixelsToEmu(Math.Max(0, pixels)).ToString(CultureInfo.InvariantCulture);
        if (string.Equals(attribute.Value, emu, StringComparison.Ordinal))
            return false;

        attribute.Value = emu;
        return true;
    }

    // Excel's real ceilings: 16,384 columns (XFD) vs. 1,048,576 rows.
    private const uint MaxColumnIndex = 16384;
    private const uint MaxRowIndex = 1048576;

    // Mirrors XlsxWorksheetChartWriter.ToMarkerIndex: walks columns/rows from index 0 accumulating pixel
    // sizes (skipping hidden/zero-size ones) until the remaining distance fits within the next column/row,
    // returning its zero-based index and the leftover sub-cell offset in pixels.
    private static (uint Index, double Offset) ToMarkerIndex(
        double pixels,
        double defaultSize,
        uint maxIndex,
        Func<uint, bool> isHidden,
        Func<uint, double> getSize)
    {
        var remaining = Math.Max(0, pixels);
        var index = 0u;
        while (index < maxIndex)
        {
            var oneBasedIndex = index + 1;
            var size = isHidden(oneBasedIndex) ? 0 : Math.Max(0, getSize(oneBasedIndex));
            if (size <= 0)
            {
                index++;
                continue;
            }

            if (remaining < size)
                return (index, remaining);

            remaining -= size;
            index++;
        }

        return (index, Math.Min(remaining, Math.Max(0, defaultSize)));
    }

    private static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var col = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(col))
                width += sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
        }

        return width;
    }

    private static double SumRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
        }

        return height;
    }
}
