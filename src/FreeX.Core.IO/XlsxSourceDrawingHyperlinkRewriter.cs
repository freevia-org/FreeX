using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// round-176-drawing-hyperlink-persist fix, the object-hyperlink sibling of
/// <see cref="XlsxSourceDrawingGeometryRewriter"/>. Source-loaded pictures/shapes/text boxes are never
/// emitted by <see cref="XlsxWorksheetDrawingObjectWriter"/> -- it gates every object behind
/// <c>!IsSourceLoaded</c> -- so their anchor (including its <c>a:hlinkClick</c> and the drawing-rels
/// entry that <c>hlinkClick</c> resolves through) is instead carried forward VERBATIM from the source
/// package, either by <see cref="XlsxWorksheetDrawingPartMerger"/> or by the generic unknown-part
/// passthrough. That verbatim copy replays the ORIGINAL hyperlink target, so a
/// <see cref="DrawingObjectHyperlink"/> rewritten on the in-memory model by a command that does not
/// clear <c>IsSourceLoaded</c> was silently discarded on save.
/// <para>
/// The command that motivated this is the row/column insert/delete shift (freex-hyperlinks F1,
/// <c>RowColumnShiftHelpers.AddressState.cs</c>): it rewrites a "Place in This Document" target so the
/// hyperlink follows the cells it points at, but -- exactly like the resize/move edits F15 covered for
/// geometry -- it leaves <c>IsSourceLoaded</c> alone, so the fix reached the model and never the file.
/// Sheet Rename / Delete Sheet (<c>DrawingObjectHyperlinkRewriter</c>, R107) rewrite the same field the
/// same way and were losing it the same way. A CHART's hyperlink needs nothing here: charts have no
/// <c>IsSourceLoaded</c> flag and are always fully re-emitted from the model by
/// <see cref="XlsxWorksheetChartWriter"/>.
/// </para>
/// <para>
/// Runs alongside the geometry rewriter (same feature-plan gate, same "after PreserveSourcePackageParts,
/// so the part is already at its final path" placement) and matches each source-loaded model to its
/// anchor by the object's stable <c>cNvPr@name</c> -- the same key
/// <c>XlsxWorksheetDrawingObjectWriter.ReadOldDrawingObjectHyperlinksByName</c> and
/// <c>XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames</c> already use for this exact
/// reader/writer correspondence, and (unlike the geometry rewriter's positional matching) the only key
/// that stays correct when the writer has interleaved freshly-emitted anchors into the same part.
/// A name shared by more than one drawing object on the sheet is ambiguous and is left untouched.
/// </para>
/// </summary>
internal static class XlsxSourceDrawingHyperlinkRewriter
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string HyperlinkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var sheet in workbook.Sheets)
        {
            if (!XlsxSourceDrawingGeometryRewriter.HasSourceLoadedDrawingObjects(sheet))
                continue;

            var hyperlinksByName = GetSourceLoadedHyperlinksByName(sheet);
            if (hyperlinksByName.Count == 0)
                continue;

            var worksheetPath = worksheetPathMap?.SheetPathsByName.GetValueOrDefault(sheet.Name);
            if (string.IsNullOrWhiteSpace(worksheetPath))
                continue;

            var drawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
                archive,
                worksheetPath,
                XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
                RelNs,
                OpcRelationships.Namespace);
            if (string.IsNullOrWhiteSpace(drawingPath))
                continue;

            var drawingEntry = archive.GetEntry(drawingPath);
            if (drawingEntry is null)
                continue;

            XDocument drawingXml;
            try
            {
                drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            }
            catch
            {
                continue; // Malformed drawing part: leave it exactly as it is rather than corrupting it.
            }

            if (drawingXml.Root is null)
                continue;

            var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
            var relsXml = LoadOrCreateRelationships(archive, drawingRelsPath);
            if (relsXml?.Root is null)
                continue;

            if (!RewriteHyperlinks(drawingXml.Root, relsXml.Root, hyperlinksByName))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
            XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, relsXml);
        }
    }

    /// <summary>
    /// The sheet's source-loaded pictures/text boxes/shapes keyed by <c>cNvPr@name</c>, with any name
    /// that is not unique ACROSS ALL of the sheet's drawing objects (source-loaded or not) dropped --
    /// a duplicate name cannot identify one anchor, and silently rewriting the wrong object's hyperlink
    /// would be worse than leaving the stale one in place. The value is the model's CURRENT hyperlink,
    /// which is deliberately allowed to be null: a source-loaded object whose hyperlink was REMOVED in
    /// this session must have the preserved <c>a:hlinkClick</c> taken back out, not left behind.
    /// </summary>
    private static Dictionary<string, DrawingObjectHyperlink?> GetSourceLoadedHyperlinksByName(Sheet sheet)
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in AllDrawingObjectNames(sheet))
            nameCounts[name] = nameCounts.GetValueOrDefault(name) + 1;

        var result = new Dictionary<string, DrawingObjectHyperlink?>(StringComparer.Ordinal);
        foreach (var picture in sheet.Pictures)
        {
            if (picture.IsSourceLoaded && IsUniqueName(picture.Name, nameCounts))
                result[picture.Name!] = picture.Hyperlink;
        }

        foreach (var textBox in sheet.TextBoxes)
        {
            if (textBox.IsSourceLoaded && IsUniqueName(textBox.Name, nameCounts))
                result[textBox.Name!] = textBox.Hyperlink;
        }

        foreach (var shape in sheet.DrawingShapes)
        {
            if (shape.IsSourceLoaded && IsUniqueName(shape.Name, nameCounts))
                result[shape.Name!] = shape.Hyperlink;
        }

        return result;
    }

    private static IEnumerable<string> AllDrawingObjectNames(Sheet sheet)
    {
        foreach (var picture in sheet.Pictures)
        {
            if (!string.IsNullOrEmpty(picture.Name))
                yield return picture.Name;
        }

        foreach (var textBox in sheet.TextBoxes)
        {
            if (!string.IsNullOrEmpty(textBox.Name))
                yield return textBox.Name;
        }

        foreach (var shape in sheet.DrawingShapes)
        {
            if (!string.IsNullOrEmpty(shape.Name))
                yield return shape.Name;
        }
    }

    private static bool IsUniqueName(string? name, Dictionary<string, int> nameCounts) =>
        !string.IsNullOrEmpty(name) && nameCounts.GetValueOrDefault(name) == 1;

    /// <summary>
    /// Rewrites every matched anchor's <c>a:hlinkClick</c> (and the drawing-rels entry it resolves
    /// through) to the model's current value, adding one where the model gained a hyperlink and removing
    /// one where the model lost it. Returns true when anything actually changed -- an unedited hyperlink
    /// rewrites to the identical target, so an ordinary save stays byte-stable.
    /// </summary>
    private static bool RewriteHyperlinks(
        XElement drawingRoot,
        XElement relsRoot,
        Dictionary<string, DrawingObjectHyperlink?> hyperlinksByName)
    {
        var relationshipsById = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relsRoot.Elements(OpcRelationships.Namespace + "Relationship"))
        {
            if (relationship.Attribute("Id")?.Value is { Length: > 0 } id)
                relationshipsById[id] = relationship;
        }

        var changed = false;
        foreach (var cNvPr in drawingRoot.Descendants(SpreadsheetDrawingNs + "cNvPr").ToList())
        {
            // Same element filter as ReadOldDrawingObjectHyperlinksByName: pictures, shapes and
            // connectors, deliberately NOT nvGraphicFramePr (chart territory, already model-driven).
            if (cNvPr.Parent?.Name.LocalName is not ("nvPicPr" or "nvSpPr" or "nvCxnSpPr"))
                continue;

            if (cNvPr.Attribute("name")?.Value is not { Length: > 0 } name ||
                !hyperlinksByName.TryGetValue(name, out var modelHyperlink))
            {
                continue;
            }

            var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
            if (modelHyperlink is null)
            {
                if (hlinkClick is null)
                    continue;

                hlinkClick.Remove();
                changed = true;
                continue;
            }

            var relId = hlinkClick?.Attribute(RelNs + "id")?.Value;
            if (hlinkClick is null || string.IsNullOrEmpty(relId) || !relationshipsById.ContainsKey(relId))
            {
                // The model carries a hyperlink the preserved anchor has no (resolvable) hlinkClick for
                // -- it was added this session, or the source relationship is dangling. Allocate a fresh
                // relationship and attach it. CT_NonVisualDrawingProps element order is
                // hlinkClick?, hlinkHover?, extLst?, so the new element goes FIRST.
                hlinkClick?.Remove();
                relId = XlsxPackageXmlEditor.NextRelationshipId(relsRoot.Document!, OpcRelationships.Namespace);
                var relationship = new XElement(
                    OpcRelationships.Namespace + "Relationship",
                    new XAttribute("Id", relId),
                    new XAttribute("Type", HyperlinkRelationshipType));
                relsRoot.Add(relationship);
                relationshipsById[relId] = relationship;

                hlinkClick = new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", relId));
                cNvPr.AddFirst(hlinkClick);
                changed = true;
            }

            var resolved = relationshipsById[relId!];
            changed |= SetAttribute(resolved, "Target", modelHyperlink.Target);
            changed |= SetAttribute(resolved, "TargetMode", NullIfBlank(modelHyperlink.TargetMode));
            changed |= SetAttribute(hlinkClick, "tooltip", NullIfBlank(modelHyperlink.Tooltip));
        }

        // A removed hyperlink can orphan its relationship. Drop only hyperlink relationships that
        // nothing in the (already rewritten) drawing part references any more -- an orphaned
        // relationship is exactly what XlsxWorksheetHyperlinkRelationshipPruner exists to keep out of
        // the worksheet parts, and Excel rejects a dangling r:id just as readily here.
        if (changed)
            changed |= PruneOrphanedHyperlinkRelationships(drawingRoot, relsRoot);

        return changed;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool SetAttribute(XElement element, XName name, string? value)
    {
        var existing = element.Attribute(name);
        if (value is null)
        {
            if (existing is null)
                return false;
            existing.Remove();
            return true;
        }

        if (existing is not null && existing.Value == value)
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool PruneOrphanedHyperlinkRelationships(XElement drawingRoot, XElement relsRoot)
    {
        // Every relationship id the drawing part still points at, from ANY r:-namespace attribute
        // (r:id on an hlinkClick/hlinkHover -- including the run-level ones inside shape text --
        // r:embed/r:link on a blip, ...), so a hyperlink relationship shared with a text run is kept.
        var referencedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in drawingRoot.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.Namespace == RelNs && attribute.Value is { Length: > 0 } value)
                    referencedIds.Add(value);
            }
        }

        var removed = false;
        foreach (var relationship in relsRoot.Elements(OpcRelationships.Namespace + "Relationship").ToList())
        {
            if (relationship.Attribute("Type")?.Value != HyperlinkRelationshipType)
                continue;
            if (relationship.Attribute("Id")?.Value is not { Length: > 0 } id || referencedIds.Contains(id))
                continue;

            relationship.Remove();
            removed = true;
        }

        return removed;
    }

    private static XDocument? LoadOrCreateRelationships(ZipArchive archive, string drawingRelsPath)
    {
        if (archive.GetEntry(drawingRelsPath) is not { } relsEntry)
            return new XDocument(new XElement(OpcRelationships.Namespace + "Relationships"));

        try
        {
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            return relsXml.Root is null ? null : relsXml;
        }
        catch
        {
            return null;
        }
    }
}
