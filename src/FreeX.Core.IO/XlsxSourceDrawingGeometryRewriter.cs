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
        }

        var sourceTextBoxes = sheet.TextBoxes.Where(textBox => textBox.IsSourceLoaded).ToList();
        var sourceShapes = sheet.DrawingShapes.Where(shape => shape.IsSourceLoaded).ToList();

        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
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
                column => sheet.IsColEffectivelyHidden(column),
                column => sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8);
            var (toRow, toRowOffset) = ToMarkerIndex(
                fromTop + heightPixels,
                sheet.DefaultRowHeight,
                row => sheet.IsRowEffectivelyHidden(row),
                row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight));

            changed |= SetIndexElement(to, "col", toCol);
            changed |= SetOffsetElement(to, "colOff", toColOffset);
            changed |= SetIndexElement(to, "row", toRow);
            changed |= SetOffsetElement(to, "rowOff", toRowOffset);
        }

        return changed;
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

    // Mirrors XlsxWorksheetChartWriter.ToMarkerIndex: walks columns/rows from index 0 accumulating pixel
    // sizes (skipping hidden/zero-size ones) until the remaining distance fits within the next column/row,
    // returning its zero-based index and the leftover sub-cell offset in pixels.
    private static (uint Index, double Offset) ToMarkerIndex(
        double pixels,
        double defaultSize,
        Func<uint, bool> isHidden,
        Func<uint, double> getSize)
    {
        var remaining = Math.Max(0, pixels);
        var index = 0u;
        while (index < 16384)
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
