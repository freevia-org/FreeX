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

            var drawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
                archive,
                worksheetPath,
                XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
                XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
                OpcRelationships.Namespace);
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

    /// <summary>
    /// Walks the drawing's anchors in the same document order the reader uses
    /// (<see cref="XlsxWorksheetDrawingPartReader"/>: all &lt;xdr:pic&gt; in order; &lt;xdr:sp&gt; and
    /// &lt;xdr:cxnSp&gt; classified into text boxes vs. shapes/connectors via a SINGLE combined
    /// document-order pass, exactly mirroring <c>XlsxWorksheetDrawingParts.ReadShapeAndTextBoxParts</c>'s
    /// own R78-io-shape-geometry-5-2 fix -- see the R81-io-drawing-shape-cxnsp-order comment below for why
    /// this must NOT be two separate per-element-name passes).
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
        // R78-io-drawing-grpsp-move: group children stay in the candidate pool now (matched by
        // identity in MatchPictureElementsToModels, same as any other picture) -- they are routed to
        // RewriteGroupChildGeometry below instead of being excluded outright, so a moved/resized
        // grouped picture's edit is no longer silently dropped on save.
        var pictureElements = drawingRoot.Descendants(SpreadsheetDrawingNs + "pic").ToList();
        foreach (var (pictureElement, picture) in MatchPictureElementsToModels(pictureElements, sourcePictures))
        {
            if (IsWithinGroupShape(pictureElement))
            {
                if (RewriteGroupChildGeometry(pictureElement, picture.Width, picture.Height, picture.AnchorOffsetX, picture.AnchorOffsetY, drawingNs))
                    changed = true;
            }
            else
            {
                var anchor = FindNearestAnchorElement(pictureElement);
                if (anchor is not null &&
                    RewriteAnchorGeometry(
                        anchor, sheet, picture.Width, picture.Height, picture.AnchorOffsetX, picture.AnchorOffsetY,
                        picture.SourceLoadedWidthPixels, picture.SourceLoadedHeightPixels))
                {
                    changed = true;
                }
            }

            if (RewritePictureVisualProperties(pictureElement, picture, drawingNs))
                changed = true;
        }

        var sourceTextBoxes = sheet.TextBoxes.Where(textBox => textBox.IsSourceLoaded).ToList();
        var sourceShapes = sheet.DrawingShapes.Where(shape => shape.IsSourceLoaded).ToList();

        var textBoxElements = new List<XElement>();
        var shapeElements = new List<XElement>();
        // R81-io-drawing-shape-cxnsp-order fix: walk <xdr:sp> and <xdr:cxnSp> together in a SINGLE
        // document-order pass, exactly mirroring XlsxWorksheetDrawingParts.ReadShapeAndTextBoxParts's own
        // R78-io-shape-geometry-5-2 fix. Two SEPARATE Descendants() passes (every <xdr:sp> first, THEN
        // every <xdr:cxnSp>) silently reordered shapeElements relative to sourceShapes -- which the reader
        // builds via that single combined pass, preserving the drawing's true authored order -- whenever a
        // drawing part mixed shapes and connectors in any order other than "every sp before every cxnSp"
        // (e.g. a connector authored before a later shape). That desynchronized the positional Zip
        // alignment below: a resize applied to one shape's model got silently written onto a completely
        // different shape/connector's XML element (and vice versa) on save.
        foreach (var shapeElement in drawingRoot.Descendants())
        {
            if (shapeElement.Name == SpreadsheetDrawingNs + "sp")
            {
                if (shapeElement.Ancestors(MarkupCompatNs + "Fallback").Any())
                    continue;

                // R72-io-drawing-anchors-4-1/R78-io-drawing-grpsp-move: a shape nested inside an xdr:grpSp
                // is positioned relative to the group's own xdr:xfrm, not by its own top-level anchor --
                // it stays in this candidate list (for positional Zip alignment against sourceShapes
                // below, which includes group children too) but is routed to RewriteGroupChildGeometry
                // rather than ever being matched against the group's shared anchor.
                var isTextBox = shapeElement
                    .Element(SpreadsheetDrawingNs + "nvSpPr")?
                    .Element(SpreadsheetDrawingNs + "cNvSpPr")?
                    .Attribute("txBox")?.Value == "1";

                // R62-io-drawing-textbox-6-3: mirror XlsxWorksheetDrawingParts.ReadSpElement's identical
                // gate -- route purely on the txBox="1" marker, not on non-empty text, so an emptied
                // (text-deleted) text box still zips up against sheet.TextBoxes here instead of shifting
                // into shapeElements and desynchronizing the index-based Zip alignment below.
                if (isTextBox)
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
            else if (shapeElement.Name == SpreadsheetDrawingNs + "cxnSp")
            {
                if (shapeElement.Ancestors(MarkupCompatNs + "Fallback").Any())
                    continue;

                shapeElements.Add(shapeElement);
            }
        }

        var textBoxAnchors = textBoxElements.Skip(Math.Max(0, textBoxElements.Count - sourceTextBoxes.Count));
        foreach (var (textBoxElement, textBox) in textBoxAnchors.Zip(sourceTextBoxes))
        {
            if (IsWithinGroupShape(textBoxElement))
            {
                if (RewriteGroupChildGeometry(textBoxElement, textBox.Width, textBox.Height, textBox.AnchorOffsetX, textBox.AnchorOffsetY, drawingNs))
                    changed = true;
            }
            else
            {
                var anchor = FindNearestAnchorElement(textBoxElement);
                if (anchor is not null &&
                    RewriteAnchorGeometry(
                        anchor, sheet, textBox.Width, textBox.Height, textBox.AnchorOffsetX, textBox.AnchorOffsetY,
                        textBox.SourceLoadedWidthPixels, textBox.SourceLoadedHeightPixels))
                {
                    changed = true;
                }
            }

            if (RewriteTextBoxVisualProperties(textBoxElement, textBox, drawingNs))
                changed = true;
        }

        var shapeAnchors = shapeElements.Skip(Math.Max(0, shapeElements.Count - sourceShapes.Count));
        foreach (var (shapeElement, shape) in shapeAnchors.Zip(sourceShapes))
        {
            if (IsWithinGroupShape(shapeElement))
            {
                if (RewriteGroupChildGeometry(shapeElement, shape.Width, shape.Height, shape.AnchorOffsetX, shape.AnchorOffsetY, drawingNs))
                    changed = true;
            }
            else
            {
                var anchor = FindNearestAnchorElement(shapeElement);
                if (anchor is not null &&
                    RewriteAnchorGeometry(
                        anchor, sheet, shape.Width, shape.Height, shape.AnchorOffsetX, shape.AnchorOffsetY,
                        shape.SourceLoadedWidthPixels, shape.SourceLoadedHeightPixels))
                {
                    changed = true;
                }

                if (RewriteShapeXfrmExtent(shapeElement, shape, drawingNs))
                    changed = true;
            }

            if (RewriteShapeAltTextAndTitle(shapeElement, shape.AltText, shape.Title))
                changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Backlog "shape-xfrm-ext-stale" fix: a source-loaded shape's own
    /// <c>&lt;xdr:spPr&gt;&lt;a:xfrm&gt;&lt;a:ext cx cy/&gt;</c> holds its (pre-rotation) size, and the loader
    /// (<see cref="XlsxDrawingAnchorApplier.ApplyToShape"/>) PREFERS that xfrm extent over the anchor's
    /// cell-span-derived size when both are present (the anchor is the ROTATED bounding box, not the shape's
    /// own unrotated dimensions). Until this fix, <see cref="RewriteDrawingGeometry"/>'s shape loop only
    /// rewrote the anchor's <c>to</c> marker (via <see cref="RewriteAnchorGeometry"/>) and left this internal
    /// <c>ext</c> stale, so a resized shape reverted to its ORIGINAL size after a single save+reload: the
    /// next load read the stale, still-positive <c>ext</c> back and it took priority over the correctly
    /// rewritten anchor. This patches the <c>ext</c> cx/cy to the model's current Width/Height so the anchor
    /// bounding box and the internal xfrm stay consistent, mirroring how
    /// <see cref="RewritePictureVisualProperties"/>/<see cref="SetPictureTransform"/> patch a picture's xfrm.
    /// <para>
    /// Only an <c>ext</c> that is already present is touched: an absent xfrm means the loader used the
    /// anchor-derived size, which <see cref="RewriteAnchorGeometry"/> already handles. And an individual axis
    /// is rewritten ONLY when its SOURCE value is already positive -- this both mirrors <c>ApplyToShape</c>'s
    /// trust condition (it copies the xfrm size into the model only for a positive axis) and protects the
    /// intentional zero axis of a line-like shape (a horizontal Line/ElbowConnector/CurvedConnector has
    /// cy=0, a vertical one cx=0): the zero axis is never faithfully captured in the model (the shape keeps
    /// its default Width/Height there -- see <see cref="DrawingShapeModel"/>), so rewriting it from the model
    /// would clobber that intentional zero with a bogus default-derived size. Returns true when modified.
    /// </para>
    /// </summary>
    private static bool RewriteShapeXfrmExtent(XElement shapeElement, DrawingShapeModel shape, XNamespace drawingNs)
    {
        var ext = shapeElement
            .Element(SpreadsheetDrawingNs + "spPr")?
            .Element(drawingNs + "xfrm")?
            .Element(drawingNs + "ext");
        if (ext is null)
            return false;

        var changed = false;
        if (ParsesPositive(ext.Attribute("cx")))
            changed |= SetExtentAttribute(ext, "cx", shape.Width);
        if (ParsesPositive(ext.Attribute("cy")))
            changed |= SetExtentAttribute(ext, "cy", shape.Height);
        return changed;
    }

    private static bool ParsesPositive(XAttribute? attribute) =>
        attribute is not null &&
        double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        value > 0;

    /// <summary>
    /// R65-io-image-drawing-6-2 fix: pairs each SOURCE-LOADED <see cref="PictureModel"/> with its
    /// correct physical <c>&lt;xdr:pic&gt;</c> element by IDENTITY (matching the <c>cNvPr@name</c> the
    /// reader stamped onto <see cref="PictureModel.Name"/> when it was loaded — see
    /// <c>XlsxWorksheetDrawingParts.ReadNonVisualProperties</c>/<c>ReadPictureParts</c>), rather than a
    /// positional <c>Skip(pictureElements.Count - sourcePictures.Count).Zip(...)</c> that silently
    /// assumed every UNMODELED <c>&lt;xdr:pic&gt;</c> element (e.g. a "Link to File" picture the reader
    /// could not — before R65-io-image-drawing-6-1 — materialize, or any other element skipped for a
    /// different reason, such as a picture whose relationship or image part is missing) sorts BEFORE
    /// every modeled one in document order. When an unmodeled element instead sits in the middle (or
    /// after) the modeled ones, that positional assumption pairs the wrong physical element with a
    /// model, and an edit to one picture gets written onto a completely different picture's XML.
    /// <para>
    /// Falls back to positional pairing, in document order, ONLY among the elements/models left over
    /// after every name match is resolved (covers a picture with no name, or more than one picture
    /// sharing the same name — Excel does not guarantee uniqueness) so every source-loaded model still
    /// gets a best-effort match instead of being silently skipped.
    /// </para>
    /// </summary>
    private static IEnumerable<(XElement Element, PictureModel Picture)> MatchPictureElementsToModels(
        List<XElement> pictureElements,
        IReadOnlyList<PictureModel> sourcePictures)
    {
        var elementsByName = new Dictionary<string, Queue<XElement>>(StringComparer.Ordinal);
        var leftoverElements = new List<XElement>();
        foreach (var element in pictureElements)
        {
            var name = ReadPictureCNvPrName(element);
            if (string.IsNullOrEmpty(name))
            {
                leftoverElements.Add(element);
                continue;
            }

            if (!elementsByName.TryGetValue(name, out var queue))
            {
                queue = new Queue<XElement>();
                elementsByName[name] = queue;
            }

            queue.Enqueue(element);
        }

        var matches = new List<(XElement, PictureModel)>();
        var leftoverPictures = new List<PictureModel>();
        foreach (var picture in sourcePictures)
        {
            if (!string.IsNullOrEmpty(picture.Name) &&
                elementsByName.TryGetValue(picture.Name, out var queue) &&
                queue.Count > 0)
            {
                matches.Add((queue.Dequeue(), picture));
            }
            else
            {
                leftoverPictures.Add(picture);
            }
        }

        // Any elements whose name queue still has entries (more physical elements sharing a name than
        // models claimed it, or a name no source-loaded model carries at all — e.g. the unmodeled
        // linked-picture element) are also leftovers.
        foreach (var queue in elementsByName.Values)
        {
            while (queue.Count > 0)
                leftoverElements.Add(queue.Dequeue());
        }

        // Restore document order among the leftovers: draining per-name queues and the initial
        // unnamed-element pass does not preserve their original interleaving.
        leftoverElements = leftoverElements.OrderBy(pictureElements.IndexOf).ToList();

        foreach (var pair in leftoverElements.Zip(leftoverPictures))
            matches.Add(pair);

        return matches;
    }

    private static string? ReadPictureCNvPrName(XElement pictureElement) =>
        pictureElement
            .Element(SpreadsheetDrawingNs + "nvPicPr")?
            .Element(SpreadsheetDrawingNs + "cNvPr")?
            .Attribute("name")?.Value;

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
    /// R72-io-drawing-anchors-4-1 fix: true when <paramref name="element"/> sits inside an
    /// <c>xdr:grpSp</c> group. A grouped pic/sp/cxnSp's geometry is expressed relative to the group's
    /// own <c>xdr:xfrm</c> (chOff/chExt vs. off/ext), not by an anchor of its own -- ascending past the
    /// group to the shared anchor (as <see cref="FindNearestAnchorElement"/> does with no boundary
    /// check) and rewriting that anchor from a single child's model geometry would silently move/resize
    /// the WHOLE group from one child's edit, and the next matched child would then overwrite it again.
    /// Elements nested in a group must never be matched against the group's shared anchor -- see
    /// <see cref="RewriteGroupChildGeometry"/> (R78-io-drawing-grpsp-move) for the path that instead
    /// rewrites the element's OWN local off/ext.
    /// </summary>
    private static bool IsWithinGroupShape(XElement element) =>
        element.Ancestors(SpreadsheetDrawingNs + "grpSp").Any();

    /// <summary>
    /// R78-io-drawing-grpsp-move fix: a pic/sp/cxnSp nested inside one or more <c>xdr:grpSp</c> groups
    /// has no anchor of its own -- its position/size are its own local <c>&lt;a:xfrm&gt;&lt;a:off&gt;</c>/
    /// <c>&lt;a:ext&gt;</c>, expressed in the immediately enclosing group's <c>chOff</c>/<c>chExt</c>
    /// child coordinate space. The reader (<see cref="XlsxWorksheetDrawingPartReader.ComputeGroupTransform"/>)
    /// composes that space, across every nesting level, into the absolute worksheet position/size this
    /// method is handed as <paramref name="widthPixels"/>/<paramref name="heightPixels"/>/
    /// <paramref name="offsetXPixels"/>/<paramref name="offsetYPixels"/> (the model's current, possibly
    /// user-edited, geometry). R72-io-drawing-anchors-4-1 made the rewriter skip such elements entirely
    /// to avoid corrupting the group's SHARED anchor -- correct, but it traded "group corruption" for
    /// "silently discarding the edit on every save" (the bug this fixes). This inverts the exact same
    /// composed transform to translate the edited absolute position/size back into the element's own
    /// local off/ext, and rewrites ONLY that local element -- the group's own shared anchor/xfrm is
    /// never touched.
    /// <para>
    /// Returns false (no rewrite -- the previous silent no-op) when the group's transform can't be
    /// inverted (a degenerate/malformed group xfrm, e.g. a zero chExt) or the element carries no local
    /// <c>&lt;a:xfrm&gt;&lt;a:off&gt;</c>/<c>&lt;a:ext&gt;</c> to rewrite, rather than guessing.
    /// </para>
    /// </summary>
    private static bool RewriteGroupChildGeometry(
        XElement element,
        double widthPixels,
        double heightPixels,
        double offsetXPixels,
        double offsetYPixels,
        XNamespace drawingNs)
    {
        var groupTransform = XlsxWorksheetDrawingPartReader.ComputeGroupTransform(element, SpreadsheetDrawingNs, drawingNs);
        var determinant = groupTransform.MatrixA * groupTransform.MatrixD - groupTransform.MatrixB * groupTransform.MatrixC;
        if (determinant == 0 || groupTransform.ScaleX == 0 || groupTransform.ScaleY == 0)
            return false;

        if (!TryReadAnchorBaseOffset(FindNearestAnchorElement(element), out var baseXEmu, out var baseYEmu))
            return false;

        var worldDeltaXEmu = DrawingMlCoordinateUnits.PixelsToEmuSigned(offsetXPixels) - baseXEmu;
        var worldDeltaYEmu = DrawingMlCoordinateUnits.PixelsToEmuSigned(offsetYPixels) - baseYEmu;

        // Invert the composed 2x2 affine (matrixA..D) the reader used to map this element's own local
        // <a:off> (in its innermost group's child space) all the way out to the worksheet anchor space
        // -- this recovers exactly the local (x, y) that maps forward to the edited absolute position.
        var localOffXEmu = (groupTransform.MatrixD * worldDeltaXEmu - groupTransform.MatrixB * worldDeltaYEmu) / determinant;
        var localOffYEmu = (-groupTransform.MatrixC * worldDeltaXEmu + groupTransform.MatrixA * worldDeltaYEmu) / determinant;

        var xfrm = element.Element(SpreadsheetDrawingNs + "spPr")?.Element(drawingNs + "xfrm");
        var off = xfrm?.Element(drawingNs + "off");
        var ext = xfrm?.Element(drawingNs + "ext");

        var changed = false;
        if (off is not null)
        {
            changed |= SetSignedEmuAttribute(off, "x", localOffXEmu);
            changed |= SetSignedEmuAttribute(off, "y", localOffYEmu);
        }

        if (ext is not null)
        {
            // ScaleX/ScaleY are the plain magnitude-only chOff/chExt-to-off/ext scale product (see
            // DrawingGroupTransform) -- exactly what ReadDrawingXfrmExtent multiplied by to produce the
            // model's Width/Height, so dividing by it here recovers the element's own pre-scale local size.
            //
            // R81-io-drawing-grpsp-lineflat fix: mirror RewriteShapeXfrmExtent's ParsesPositive guard --
            // only rewrite an axis whose SOURCE ext value is already positive. A grouped line-like
            // connector (a horizontal/vertical straight connector) can legitimately carry an intentional
            // zero on one axis (see DrawingShapeKindSupport.IsLineLike); ComputeGroupTransform's caller
            // (ReadDrawingXfrmExtent) reports that axis to ApplyToShape, which -- because the axis is
            // exactly zero -- never overwrites the model's default (non-zero) Width/Height there (it can't
            // faithfully round-trip an intentional zero through the model). Without this guard, rewriting
            // both axes unconditionally from the model clobbered that intentional zero with the model's
            // bogus default-derived size on every save, turning a flat line diagonal.
            if (ParsesPositive(ext.Attribute("cx")))
            {
                var localCxEmu = DrawingMlCoordinateUnits.PixelsToEmu(widthPixels) / groupTransform.ScaleX;
                changed |= SetExtentAttribute(ext, "cx", DrawingMlCoordinateUnits.EmuToPixels(localCxEmu));
            }

            if (ParsesPositive(ext.Attribute("cy")))
            {
                var localCyEmu = DrawingMlCoordinateUnits.PixelsToEmu(heightPixels) / groupTransform.ScaleY;
                changed |= SetExtentAttribute(ext, "cy", DrawingMlCoordinateUnits.EmuToPixels(localCyEmu));
            }
        }

        return changed;
    }

    /// <summary>
    /// Reads the group's SHARED worksheet anchor's base position in EMU -- the <c>xdr:from</c> cell's
    /// <c>colOff</c>/<c>rowOff</c> for a one/twoCellAnchor, or <c>xdr:pos</c> x/y for an absoluteAnchor
    /// -- which <see cref="RewriteGroupChildGeometry"/> subtracts from the model's edited absolute
    /// offset to recover the world-space DELTA that the composed group transform maps to/from. This
    /// anchor is read-only here: it is never rewritten for a grouped element (see
    /// <see cref="IsWithinGroupShape"/>).
    /// </summary>
    private static bool TryReadAnchorBaseOffset(XElement? anchor, out double baseXEmu, out double baseYEmu)
    {
        baseXEmu = 0;
        baseYEmu = 0;
        if (anchor is null)
            return false;

        if (anchor.Name == SpreadsheetDrawingNs + "absoluteAnchor")
        {
            var pos = anchor.Element(SpreadsheetDrawingNs + "pos");
            return pos is not null &&
                   TryParseEmuAttribute(pos, "x", out baseXEmu) &&
                   TryParseEmuAttribute(pos, "y", out baseYEmu);
        }

        var from = anchor.Element(SpreadsheetDrawingNs + "from");
        if (from is null)
            return false;

        var colOffOk = TryParseEmuElementValue(from.Element(SpreadsheetDrawingNs + "colOff"), out baseXEmu);
        var rowOffOk = TryParseEmuElementValue(from.Element(SpreadsheetDrawingNs + "rowOff"), out baseYEmu);
        return colOffOk && rowOffOk;
    }

    private static bool TryParseEmuAttribute(XElement element, string attributeName, out double value) =>
        double.TryParse(element.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseEmuElementValue(XElement? element, out double value)
    {
        value = 0;
        return element is not null &&
               double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Like <see cref="SetOffsetElement"/> but writes a raw (already-computed) EMU value directly to an
    /// ATTRIBUTE rather than converting pixels to EMU -- used for a group child's own local
    /// <c>&lt;a:off&gt;</c> x/y, which are genuinely signed (a child can legitimately sit left of/above
    /// its group's <c>chOff</c> origin) and are computed once via matrix inversion rather than per-axis
    /// clamping. Returns true when the XML was modified.
    /// </summary>
    private static bool SetSignedEmuAttribute(XElement element, string attributeName, double emuValue)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null)
            return false;

        var rounded = ((long)Math.Round(emuValue)).ToString(CultureInfo.InvariantCulture);
        if (string.Equals(attribute.Value, rounded, StringComparison.Ordinal))
            return false;

        attribute.Value = rounded;
        return true;
    }

    /// <summary>
    /// Rewrites one anchor's <c>from</c> sub-cell offset and size (<c>ext</c> for oneCell/absolute anchors,
    /// or the <c>to</c> marker for twoCell anchors) to match the current model geometry. The <c>from</c>
    /// cell itself is left untouched — a change of anchor CELL already fails the patch-safe geometry check
    /// and is out of scope here; only the sub-cell offset and size are rewritten. Returns true when the XML
    /// was modified.
    /// </summary>
    /// <param name="sourceLoadedWidthPixels">
    /// R94 fix: the object's <c>Width</c> as it was at LOAD time (<c>PictureModel.SourceLoadedWidthPixels</c>
    /// and its TextBoxModel/DrawingShapeModel equivalents) -- <see langword="null"/> when unknown/degenerate.
    /// The twoCellAnchor <c>to</c>-marker walk below is evaluated against the CURRENT sheet's column pixel
    /// sizes (it must be, to place the marker correctly under whatever the sheet looks like now), but that
    /// walk only needs to run at all when the object's OWN geometry genuinely changed since load. Without
    /// this baseline, hiding/resizing some unrelated row/column between load and save -- with this object
    /// never touched -- silently shifts its `to` marker to a different cell than the source file had,
    /// because the walk would skip that now-hidden/resized cell's pixel contribution that the ORIGINAL
    /// marker (implicitly) accounted for. See XlsxSourceDrawingGeometryRewriterTests' R94_* tests.
    /// </param>
    /// <param name="sourceLoadedHeightPixels">The same baseline for <paramref name="heightPixels"/>.</param>
    private static bool RewriteAnchorGeometry(
        XElement anchor,
        Sheet sheet,
        double widthPixels,
        double heightPixels,
        double offsetXPixels,
        double offsetYPixels,
        double? sourceLoadedWidthPixels = null,
        double? sourceLoadedHeightPixels = null)
    {
        // R72-io-drawing-anchors-4-2: an absoluteAnchor has xdr:pos/xdr:ext, never xdr:from/xdr:to, so it
        // must be handled independently of (and before) the from-null guard below -- that guard is only
        // meaningful for the twoCellAnchor path, which genuinely needs from/to markers.
        if (anchor.Name == SpreadsheetDrawingNs + "absoluteAnchor")
            return RewriteAbsoluteAnchorGeometry(anchor, widthPixels, heightPixels, offsetXPixels, offsetYPixels);

        var from = anchor.Element(SpreadsheetDrawingNs + "from");
        if (from is null)
            return false;

        var changed = false;
        changed |= SetOffsetElement(from, "colOff", offsetXPixels);
        changed |= SetOffsetElement(from, "rowOff", offsetYPixels);

        if (anchor.Name == SpreadsheetDrawingNs + "oneCellAnchor")
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

            // R94 fix: only recompute the to-marker when the object's OWN Width/Height genuinely diverges
            // from what was true at load time. The walk below is intentionally evaluated against the
            // CURRENT sheet layout (a real resize must land on the right cell under today's column/row
            // sizes), but running it unconditionally -- as before this fix -- means an untouched object's
            // marker gets silently rewritten to a DIFFERENT cell whenever some unrelated row/column was
            // hidden or resized between load and save, because the walk then skips that cell's pixel
            // contribution while the original file's marker (authored under the old layout) did not.
            // Real Excel never rewrites `to` for a twoCellAnchor absent an explicit user move/resize.
            if (sourceLoadedWidthPixels is { } baselineWidth && ApproximatelyEqualsPixels(baselineWidth, widthPixels) &&
                sourceLoadedHeightPixels is { } baselineHeight && ApproximatelyEqualsPixels(baselineHeight, heightPixels))
            {
                return changed;
            }

            // Recompute the to-marker from the from-cell's absolute pixel position plus the new width/
            // height, using the same column-width/row-height walk the model writer uses for charts
            // (XlsxWorksheetChartWriter.ToAnchorMarker) so a save-then-reload measures the resize
            // identically to how XlsxDrawingAnchorApplier/GetAnchorSize measured it on load.
            var fromLeft = WorksheetMetricSpanCalculator.SumColumnPixels(sheet, 1, fromCol) + offsetXPixels;
            var fromTop = WorksheetMetricSpanCalculator.SumRowPixels(sheet, 1, fromRow) + offsetYPixels;
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
    /// R72-io-drawing-anchors-4-2 fix: an <c>xdr:absoluteAnchor</c> positions and sizes its child purely
    /// via <c>xdr:pos</c> (absolute x/y in EMU) and <c>xdr:ext</c> (cx/cy in EMU) -- it has no
    /// <c>xdr:from</c>/<c>xdr:to</c> markers at all, so it must be rewritten independently of the
    /// from/to-marker math <see cref="RewriteAnchorGeometry"/> uses for the other anchor kinds. Returns
    /// true when the XML was modified.
    /// </summary>
    private static bool RewriteAbsoluteAnchorGeometry(
        XElement anchor,
        double widthPixels,
        double heightPixels,
        double offsetXPixels,
        double offsetYPixels)
    {
        var changed = false;

        // R74-units-mismatch-sweep-1 fix: absoluteAnchor's xdr:pos x/y is CT_Point2D
        // (ST_AdjCoordinate/ST_Coordinate), which is genuinely signed -- a picture positioned to the
        // left of/above the sheet origin has a negative EMU pos. SetExtentAttribute clamps to
        // Math.Max(0, pixels), which would silently snap such a picture back to the origin on every
        // save. Use the signed pixel->EMU conversion here (matching XlsxWorksheetChartWriter's
        // absoluteAnchor pos handling), while ext (a size) still legitimately clamps to non-negative.
        var pos = anchor.Element(SpreadsheetDrawingNs + "pos");
        if (pos is not null)
        {
            changed |= SetSignedExtentAttribute(pos, "x", offsetXPixels);
            changed |= SetSignedExtentAttribute(pos, "y", offsetYPixels);
        }

        var ext = anchor.Element(SpreadsheetDrawingNs + "ext");
        if (ext is not null)
        {
            changed |= SetExtentAttribute(ext, "cx", widthPixels);
            changed |= SetExtentAttribute(ext, "cy", heightPixels);
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
        // R80-io-drawing-image-5-2: a NEGATIVE crop inset (Excel's "crop past the image edge"
        // outward padding) is also a real crop that must be patched in -- checking only "> 0" treated a
        // negative-only crop as "no crop" and silently dropped the whole srcRect on save.
        picture.CropLeft != 0 ||
        picture.CropTop != 0 ||
        picture.CropRight != 0 ||
        picture.CropBottom != 0;

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
        // R80-io-drawing-image-5-2: preserve negative (outward-crop/padding) ratios -- only clamp the
        // magnitude to Excel's ±100% bound, matching ReadSourceRectangleRatio's [-1, 1] range.
        ((int)Math.Round(Math.Clamp(ratio, -1, 1) * 100000d)).ToString(CultureInfo.InvariantCulture);

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

    // R94 fix: tolerance for comparing a live Width/Height pixel value against the load-time baseline
    // captured in *.SourceLoadedWidthPixels/HeightPixels -- generous enough to absorb harmless
    // floating-point drift from a save/reload round trip, but far tighter than a single pixel so any
    // genuine (even sub-pixel-visible) user resize is still detected as a change.
    private static bool ApproximatelyEqualsPixels(double a, double b) => Math.Abs(a - b) < 0.01;

    private static bool SetOffsetElement(XElement marker, string elementName, double pixels)
    {
        var element = marker.Element(SpreadsheetDrawingNs + elementName);
        if (element is null)
            return false;

        var emu = DrawingMlCoordinateUnits.PixelsToEmu(Math.Max(0, pixels)).ToString(CultureInfo.InvariantCulture);
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

        var emu = DrawingMlCoordinateUnits.PixelsToEmu(Math.Max(0, pixels)).ToString(CultureInfo.InvariantCulture);
        if (string.Equals(attribute.Value, emu, StringComparison.Ordinal))
            return false;

        attribute.Value = emu;
        return true;
    }

    /// <summary>
    /// R74-units-mismatch-sweep-1 fix: like <see cref="SetExtentAttribute"/> but preserves negative
    /// values, for the absoluteAnchor xdr:pos x/y attributes -- the only coordinates this rewriter
    /// touches that are genuinely signed in the OOXML schema (see
    /// <see cref="DrawingMlCoordinateUnits.PixelsToEmuSigned"/>).
    /// </summary>
    private static bool SetSignedExtentAttribute(XElement pos, string attributeName, double pixels)
    {
        var attribute = pos.Attribute(attributeName);
        if (attribute is null)
            return false;

        var emu = DrawingMlCoordinateUnits.PixelsToEmuSigned(pixels).ToString(CultureInfo.InvariantCulture);
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

        // R110: the walk exhausted every column/row (e.g. because most of the sheet is hidden,
        // so remaining pixel distance never fit within a visible column/row). `index` is now
        // `maxIndex`, one past Excel's real zero-based ceiling (16383 columns / 1048575 rows) --
        // matching XlsxDrawingAnchorApplier's read-side MaxColumnIndexZeroBased/MaxRowIndexZeroBased.
        // Clamp so we never write an out-of-range <xdr:col>/<xdr:row> that Excel would reject/repair.
        return (maxIndex - 1, Math.Min(remaining, Math.Max(0, defaultSize)));
    }

}
