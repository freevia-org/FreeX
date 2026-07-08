using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// P7 fix: slicer/timeline SELECTION/RANGE/LEVEL is discarded on a full save of an xlsx-loaded workbook.
/// On the source-package (loaded) save path the slicer/timeline/slicerCache/timelineCache parts are
/// PRESERVED verbatim by <c>PreserveSourcePackageParts</c> — so any change the in-memory model made to a
/// slicer's selected items, a timeline's selected date range, or a timeline's level/selectionLevel was
/// silently replayed back to the ORIGINAL values on save.
/// <para>
/// This rewriter runs AFTER the source parts have been preserved (so it edits each part at its final path)
/// and rewrites ONLY the selection/range/level values in place from the current model, mirroring exactly
/// what <see cref="XlsxSlicerTimelineMetadataReader"/> parses on load, and leaving every other byte
/// (graphicFrame, style, caption, columnCount, pivot binding, table binding, package graph) untouched. It
/// is a strict no-op when a control's model selection state is empty/absent and the preserved part already
/// carries no selection — this is what keeps the corpus/schema retention tests (whose fixtures declare no
/// selection) byte-stable, exactly like <see cref="XlsxSourceDrawingGeometryRewriter"/> does for anchors.
/// </para>
/// <para>
/// This mirrors the "re-apply model state onto the preserved part after preservation" shape used by
/// <see cref="XlsxSourceDrawingGeometryRewriter"/> and <see cref="XlsxX14DataValidationWriter"/>. It never
/// calls <see cref="XlsxSlicerTimelineWriter.SaveSlicerTimelines"/> (the fresh-writer emission), so it can
/// never clobber the preserved native XML or the critical package parts.
/// </para>
/// </summary>
internal static class XlsxSlicerTimelineStateRewriter
{
    // FreeX-custom extLst used by the fresh writer to persist a slicer's selected item CAPTIONS
    // (XlsxSlicerTimelineWriter emits <ext uri="{9F2C6F77-...}"><selectedItems><selectedItem value=".."/>).
    // The reader parses SelectedItems from any descendant <selectedItem @value> (namespace-tolerant).
    private static readonly XNamespace FreexSelectionNs = "https://freex.local/xlsx/slicerTimelineState";
    private const string SlicerSelectionExtensionUri = "{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Cheap gate: is there any slicer/timeline whose selection/range/level the model can carry?</summary>
    public static bool HasSlicerTimelineState(Workbook workbook) =>
        workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0;

    public static void Save(Stream packageStream, Workbook workbook)
    {
        if (!HasSlicerTimelineState(workbook))
            return;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        RewriteSlicerSelections(archive, workbook);
        RewriteTimelineState(archive, workbook);
    }

    private static void RewriteSlicerSelections(ZipArchive archive, Workbook workbook)
    {
        if (workbook.Slicers.Count == 0)
            return;

        // Model slicers keyed by their control name (the association the reader uses).
        var slicersByName = new Dictionary<string, SlicerModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var slicer in workbook.Slicers)
            slicersByName.TryAdd(slicer.Name, slicer);

        // Resolve, per slicer part, which cache part backs each <slicer cache="..."> so we can patch the
        // matching cache root. Caches are keyed by their root @name (same as the reader).
        var cacheNamesBySlicerName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slicerEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicers/"))
                     .ToList())
        {
            var slicerXml = XlsxPackageXmlEditor.LoadXml(slicerEntry);
            foreach (var slicerElement in EnumerateByLocalName(slicerXml.Root, "slicer"))
            {
                var name = slicerElement.Attribute("name")?.Value;
                var cacheName = slicerElement.Attribute("cache")?.Value;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(cacheName))
                    cacheNamesBySlicerName.TryAdd(name, cacheName);
            }
        }

        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicerCaches/"))
                     .ToList())
        {
            var cachePath = XlsxPackagePath.NormalizeEntryPath(cacheEntry);
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheName = root.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(cacheName))
                continue;

            // Find a model slicer bound to this cache (by <slicer cache="..">). If none of the slicers that
            // reference this cache is present in the model, leave the part alone.
            SlicerModel? model = null;
            foreach (var pair in cacheNamesBySlicerName)
            {
                if (string.Equals(pair.Value, cacheName, StringComparison.OrdinalIgnoreCase) &&
                    slicersByName.TryGetValue(pair.Key, out var candidate))
                {
                    model = candidate;
                    break;
                }
            }

            if (model is null)
                continue;

            var changed = RewriteSlicerCacheSelection(root, model);
            changed |= RewriteNativeCacheItemSelection(root, model, workbook);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, cacheXml);
        }
    }

    /// <summary>
    /// Reconciles the cache part's FreeX selected-item extLst (<c>&lt;selectedItem value=".."/&gt;</c>) with
    /// the model's <see cref="SlicerModel.SelectedItems"/>, the exact list the reader parses into
    /// <c>SelectedItems</c>. Returns true when the part XML changed. No-op (returns false) when the model
    /// has no selection AND the part carries none, so a corpus cache with no selection stays byte-stable.
    /// </summary>
    private static bool RewriteSlicerCacheSelection(XElement cacheRoot, SlicerModel model)
    {
        var existing = cacheRoot
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "selectedItem", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("value")?.Value ?? "")
            .ToList();

        var desired = model.SelectedItems;

        // Nothing to do when both are empty (keeps no-selection corpus caches untouched), or when the
        // preserved list already equals the model list (idempotent re-save of an unchanged workbook).
        if (existing.Count == desired.Count && existing.SequenceEqual(desired, StringComparer.Ordinal))
            return false;

        // Drop any existing FreeX selected-item extLst so we can re-emit the model's list cleanly, leaving
        // every other extLst ext (and every other cache attribute/child) intact.
        cacheRoot
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "selectedItems", StringComparison.OrdinalIgnoreCase))
            .Where(element => element.Ancestors().Any(ancestor =>
                string.Equals(ancestor.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ancestor.Attribute("uri")?.Value, SlicerSelectionExtensionUri, StringComparison.OrdinalIgnoreCase)))
            .Remove();
        RemoveEmptyFreexSelectionExtensions(cacheRoot);

        if (desired.Count == 0)
            return true;

        var slicerNs = cacheRoot.Name.Namespace;
        var extList = cacheRoot.Element(slicerNs + "extLst");
        if (extList is null)
        {
            extList = new XElement(slicerNs + "extLst");
            cacheRoot.Add(extList);
        }

        extList.Add(new XElement(WorkbookNs + "ext",
            new XAttribute("uri", SlicerSelectionExtensionUri),
            new XElement(FreexSelectionNs + "selectedItems",
                desired.Select(item =>
                    new XElement(FreexSelectionNs + "selectedItem", new XAttribute("value", item))))));
        return true;
    }

    private static void RemoveEmptyFreexSelectionExtensions(XElement cacheRoot)
    {
        cacheRoot
            .Descendants()
            .Where(element =>
                string.Equals(element.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("uri")?.Value, SlicerSelectionExtensionUri, StringComparison.OrdinalIgnoreCase) &&
                !element.HasElements)
            .Remove();
    }

    /// <summary>
    /// R11-xlsx-pivot-slicer-1: a pivot slicer cache's NATIVE selection form is
    /// <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt;</c> (see
    /// <see cref="XlsxSlicerTimelineMetadataReader"/>'s <c>ReadSlicerCacheItems</c>) — Excel reads the
    /// selection from THESE flags, never from the FreeX-private extLst that
    /// <see cref="RewriteSlicerCacheSelection"/> reconciles. On a source-preserved workbook these native
    /// <c>&lt;i s="1"&gt;</c> flags are copied verbatim, so a FreeX-side selection change (which only
    /// updates <see cref="SlicerModel.SelectedItems"/>, not <see cref="SlicerModel.CacheItems"/>) never
    /// reached them and Excel kept showing the stale selection. This resolves each cache item's caption
    /// from the pivot cache field's shared items (mirroring FreeX.Core.Commands.SlicerItemResolver's
    /// normalization) and rewrites its <c>s</c>
    /// flag to match whether that caption is in the model's current <see cref="SlicerModel.SelectedItems"/>.
    /// No-op when the part carries no native tabular items, or when every flag already matches the model
    /// (idempotent re-save of an unchanged workbook stays byte-stable). Also a no-op when
    /// <see cref="SlicerModel.SelectedItems"/> is empty AND <see cref="SlicerModel.SelectionCaptured"/> is
    /// false: an empty selection is otherwise ambiguous — it is the model's post-load default (the Core.IO
    /// load path never populates it from these native flags; only the host UI's
    /// <c>SlicerItemResolver.ResolvePivotCacheItems</c> projects a PARTIAL native selection into it, and even
    /// that resolver deliberately skips projecting when every item is selected) AND it is what a user's
    /// explicit Clear-Filter (<c>SetSlicerSelectionCommand</c> with an empty list) produces.
    /// <see cref="SlicerModel.SelectionCaptured"/> disambiguates only this empty case: false means "the
    /// model never captured/changed the selection" (leave the preserved native <c>s</c> flags untouched);
    /// true with an empty <see cref="SlicerModel.SelectedItems"/> means "the user explicitly cleared the
    /// filter to select-all" and every native <c>s</c> flag must be stripped so the clear round-trips instead
    /// of silently reverting to the stale native selection. A non-empty <see cref="SlicerModel.SelectedItems"/>
    /// always rewrites the native flags to match it, regardless of <see cref="SlicerModel.SelectionCaptured"/>.
    /// </summary>
    private static bool RewriteNativeCacheItemSelection(XElement cacheRoot, SlicerModel model, Workbook workbook)
    {
        if (model.SelectedItems.Count == 0 && !model.SelectionCaptured)
            return false;

        var itemsElement = cacheRoot
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "items", StringComparison.OrdinalIgnoreCase));
        if (itemsElement is null)
            return false;

        var field = ResolveSharedItemsField(workbook, model);
        var sharedItems = field?.SharedItems;
        var selected = new HashSet<string>(model.SelectedItems, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var itemElement in itemsElement.Elements())
        {
            if (!string.Equals(itemElement.Name.LocalName, "i", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(itemElement.Attribute("x")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (sharedItems is null || index < 0 || index >= sharedItems.Count)
                continue;

            var caption = NormalizeSharedItemCaption(sharedItems[index], field);
            if (string.IsNullOrEmpty(caption))
                continue;

            var shouldBeSelected = selected.Contains(caption);
            changed |= SetSelectedFlag(itemElement, shouldBeSelected);
        }

        return changed;
    }

    /// <summary>
    /// Sets/clears the <c>s</c> (selected) boolean attribute on a native <c>&lt;i&gt;</c> cache item.
    /// Excel's default for an absent <c>s</c> is unselected, so a false value REMOVES the attribute rather
    /// than writing <c>s="0"</c>, keeping an all-cleared re-save shaped like Excel's own output.
    /// </summary>
    private static bool SetSelectedFlag(XElement itemElement, bool selected)
    {
        var current = string.Equals(itemElement.Attribute("s")?.Value, "1", StringComparison.Ordinal);
        if (current == selected)
            return false;

        if (selected)
            itemElement.SetAttributeValue("s", "1");
        else
            itemElement.SetAttributeValue("s", null);
        return true;
    }

    /// <summary>
    /// Finds the pivot cache field backing this slicer's <see cref="SlicerModel.SourceFieldName"/>, the
    /// same association FreeX.Core.Commands.SlicerItemResolver uses (name match against
    /// every field with non-empty shared items across the workbook's pivot caches).
    /// </summary>
    private static PivotCacheFieldModel? ResolveSharedItemsField(Workbook workbook, SlicerModel slicer)
    {
        var fieldName = slicer.SourceFieldName;
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;

        foreach (var cache in workbook.PivotCaches)
        {
            foreach (var candidateField in cache.Fields)
            {
                if (string.Equals(candidateField.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                    candidateField.SharedItems is { Count: > 0 })
                {
                    return candidateField;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reformats a raw pivot-cache shared-item attribute string into the same caption
    /// FreeX.Core.Commands.SlicerItemResolver would resolve for that value (its
    /// NormalizeSharedItemCaption), so the caption compared here against
    /// <see cref="SlicerModel.SelectedItems"/> matches what the UI/refresh path uses. Text items pass
    /// through unchanged; numbers/dates are reformatted using the field's element kind (or containment
    /// flags when no per-item kind was preserved) and current-culture formatting.
    /// </summary>
    private static string NormalizeSharedItemCaption(string raw, PivotCacheFieldModel? field)
    {
        if (field is null || string.IsNullOrEmpty(raw))
            return raw;

        if (field.ContainsDate && !field.ContainsString && !field.ContainsNumber)
        {
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return raw;

            return field.Grouping switch
            {
                PivotFieldGrouping.Year => date.Year.ToString(CultureInfo.InvariantCulture),
                PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
                PivotFieldGrouping.Month => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => date.ToShortDateString()
            };
        }

        if (field.ContainsNumber && !field.ContainsString && !field.ContainsDate)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.CurrentCulture)
                : raw;
        }

        return raw;
    }

    private static void RewriteTimelineState(ZipArchive archive, Workbook workbook)
    {
        if (workbook.Timelines.Count == 0)
            return;

        var timelinesByName = new Dictionary<string, TimelineModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var timeline in workbook.Timelines)
            timelinesByName.TryAdd(timeline.Name, timeline);

        // The timeline definition part carries level/selectionLevel/scrollPosition on <timeline>; the
        // timeline cache carries selectedStartDate/selectedEndDate. Patch both, matched by control name and
        // cache name respectively (mirroring the reader's associations).
        var cacheNamesByTimelineName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var timelineEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelines/"))
                     .ToList())
        {
            var timelinePath = XlsxPackagePath.NormalizeEntryPath(timelineEntry);
            var timelineXml = XlsxPackageXmlEditor.LoadXml(timelineEntry);
            var changed = false;
            foreach (var timelineElement in EnumerateByLocalName(timelineXml.Root, "timeline"))
            {
                var name = timelineElement.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name) || !timelinesByName.TryGetValue(name, out var model))
                    continue;

                var cacheName = timelineElement.Attribute("cache")?.Value;
                if (!string.IsNullOrEmpty(cacheName))
                    cacheNamesByTimelineName.TryAdd(cacheName, name);

                changed |= RewriteTimelineDefinition(timelineElement, model);
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, timelineXml);
        }

        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelineCaches/"))
                     .ToList())
        {
            var cachePath = XlsxPackagePath.NormalizeEntryPath(cacheEntry);
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheName = root.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(cacheName) ||
                !cacheNamesByTimelineName.TryGetValue(cacheName, out var timelineName) ||
                !timelinesByName.TryGetValue(timelineName, out var model))
            {
                continue;
            }

            if (RewriteTimelineCacheSelection(root, model))
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, cacheXml);
        }
    }

    /// <summary>
    /// Rewrites ONLY the <c>level</c>/<c>selectionLevel</c>/<c>scrollPosition</c> attributes on the
    /// preserved <c>&lt;timeline&gt;</c> element to match the model, mirroring the reader. Every other
    /// attribute (name, cache, caption, style) is untouched. An attribute is only removed when the model
    /// value is null AND the attribute was present, and only added when the model value is set — so a
    /// timeline whose model carries no level/scroll state leaves the preserved part byte-stable.
    /// </summary>
    private static bool RewriteTimelineDefinition(XElement timelineElement, TimelineModel model)
    {
        var changed = false;
        changed |= SetOptionalAttribute(
            timelineElement,
            "level",
            model.Level?.ToString(CultureInfo.InvariantCulture));
        changed |= SetOptionalAttribute(
            timelineElement,
            "selectionLevel",
            (model.SelectionLevel ?? model.Level)?.ToString(CultureInfo.InvariantCulture));
        changed |= SetOptionalAttribute(
            timelineElement,
            "scrollPosition",
            string.IsNullOrEmpty(model.ScrollPosition) ? null : model.ScrollPosition + "T00:00:00");
        return changed;
    }

    /// <summary>
    /// Rewrites ONLY the selected date range on the preserved timeline cache to match the model, mirroring
    /// exactly what the reader parses: the root <c>selectedStartDate</c>/<c>selectedEndDate</c> attributes
    /// (the fresh writer's form) and, when present, the <c>&lt;state&gt;&lt;selection&gt;</c>
    /// <c>startDate</c>/<c>endDate</c> attributes (Excel's native form). The available-range
    /// <c>startDate</c>/<c>endDate</c> and every other attribute/child are left untouched.
    /// </summary>
    private static bool RewriteTimelineCacheSelection(XElement cacheRoot, TimelineModel model)
    {
        var changed = false;

        // Root-attribute form (what XlsxSlicerTimelineWriter emits, a bare yyyy-MM-dd). Only add when the
        // model has a value; only remove when the model cleared a previously-present value. Emitting the
        // bare date keeps an unchanged re-save byte-identical to the fresh writer's output.
        var selectedStart = string.IsNullOrWhiteSpace(model.SelectedStartDate) ? null : model.SelectedStartDate;
        var selectedEnd = string.IsNullOrWhiteSpace(model.SelectedEndDate) ? null : model.SelectedEndDate;
        changed |= SetOptionalAttribute(cacheRoot, "selectedStartDate", selectedStart);
        changed |= SetOptionalAttribute(cacheRoot, "selectedEndDate", selectedEnd);

        // Native <state><selection> form: patch it in place when the preserved part uses it, so a real
        // Excel timeline round-trips too. Excel's selection dates carry a time component; emit the same
        // yyyy-MM-ddT00:00:00 shape here. Never create the element when it is absent (the root form covers
        // that case and matches the fresh writer).
        var selection = cacheRoot
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "selection", StringComparison.OrdinalIgnoreCase));
        if (selection is not null)
        {
            changed |= SetOptionalAttribute(selection, "startDate", NormalizeSelectedDate(model.SelectedStartDate));
            changed |= SetOptionalAttribute(selection, "endDate", NormalizeSelectedDate(model.SelectedEndDate));
        }

        return changed;
    }

    // The model stores selected dates normalized to yyyy-MM-dd; Excel's timeline dates carry a time
    // component (e.g. "2026-03-01T00:00:00"). Emit the same yyyy-MM-ddT00:00:00 shape the fresh writer's
    // available-range attributes use, so the reader's NormalizeTimelineDate parses back the model value.
    private static string? NormalizeSelectedDate(string? date) =>
        string.IsNullOrWhiteSpace(date) ? null : date + "T00:00:00";

    /// <summary>
    /// Sets <paramref name="attributeName"/> to <paramref name="value"/> when non-null, or removes it when
    /// null. Returns true only when the XML actually changed, so an unchanged value is a no-op.
    /// </summary>
    private static bool SetOptionalAttribute(XElement element, string attributeName, string? value)
    {
        var attribute = element.Attribute(attributeName);
        if (value is null)
        {
            if (attribute is null)
                return false;
            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static IEnumerable<XElement> EnumerateByLocalName(XElement? root, string localName)
    {
        if (root is null)
            yield break;

        if (string.Equals(root.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
        {
            yield return root;
            yield break;
        }

        foreach (var element in root.Elements())
        {
            if (string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                yield return element;
        }
    }
}
