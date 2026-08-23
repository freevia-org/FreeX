using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet page break, calculation, phonetic, and custom-property native metadata preservation.
    //
    // NOTE: this returns the FULL (unfiltered) set of live break ids from the model, not just the
    // subset that XlsxWorksheetPageBreakIdReader.IsSupported/the modeled writer currently round-trip
    // (id >= 2). A manual break placed immediately after row 1 / column A is a completely normal,
    // common Excel value (id == 1), and the live model can and does represent it (see
    // XlsxFileAdapter's `if (rowBreak > 0) sheet.RowPageBreaks.Add(...)` load path). Filtering it out
    // here would make MergeWorksheetBreaks unable to tell "the user removed break id=1" apart from
    // "break id=1 can never be modeled", permanently resurrecting it from the source package. Ids that
    // truly can never be modeled (e.g. id == 0, which has no corresponding row/column) are handled by
    // ShouldRetainUnsupportedBreak below, independent of what this set contains.
    private static HashSet<uint> GetModeledWorksheetBreakIds(Workbook workbook, string sheetName, bool rowBreaks)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return [];

        return (rowBreaks ? sheet.RowPageBreaks : sheet.ColumnPageBreaks).ToHashSet();
    }

    // Decides whether to retain (from the pristine source package) a break whose id fails the shared
    // reader/writer's IsSupported(id >= 2) check. Ids that are structurally unaddressable (0, or beyond
    // the sheet's row/column limit) can never appear in the live model at all, so there is no way to
    // detect "the user removed it" for them -- they must always be preserved verbatim. An addressable-
    // but-"unsupported" id (in practice just id == 1: the break falls right after row 1 / column A) CAN
    // appear in the live model (GetModeledWorksheetBreakIds above returns it unfiltered), so for that
    // case we honor the live model instead of unconditionally resurrecting the source break.
    private static bool ShouldRetainUnsupportedBreak(uint maxBreakId, uint rawId, HashSet<uint> modeledBreakIds) =>
        rawId < 1 || rawId > maxBreakId || modeledBreakIds.Contains(rawId);

    private static bool MergeWorksheetBreaks(
        XElement sourceBreaks,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<uint> modeledBreakIds,
        uint maxBreakId)
    {
        var targetBreaks = targetRoot.Element(sourceBreaks.Name);
        if (targetBreaks is null)
        {
            var retainedBreaks = sourceBreaks
                .Elements(workbookNs + "brk")
                .Where(sourceBreak =>
                    XlsxWorksheetPageBreakIdReader.TryReadSupportedId(
                        sourceBreak,
                        maxBreakId,
                        out var sourceId)
                        ? modeledBreakIds.Contains(sourceId)
                        : ShouldRetainUnsupportedBreak(maxBreakId, sourceId, modeledBreakIds))
                .Select(sourceBreak => new XElement(sourceBreak))
                .ToList();
            if (retainedBreaks.Count == 0)
                return false;

            targetRoot.Add(new XElement(sourceBreaks.Name, sourceBreaks.Attributes(), retainedBreaks));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceBreaks.Attributes())
        {
            if (targetBreaks.Attribute(attribute.Name) is not null)
                continue;

            targetBreaks.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetBreaksBySupportedId = targetBreaks
            .Elements(workbookNs + "brk")
            .Select(element => new
            {
                Element = element,
                Parsed = XlsxWorksheetPageBreakIdReader.TryReadSupportedId(
                    element,
                    maxBreakId,
                    out var id),
                Id = id
            })
            .Where(entry => entry.Parsed)
            .GroupBy(entry => entry.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First().Element);
        var targetBreaksByRawId = targetBreaks
            .Elements(workbookNs + "brk")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
            .GroupBy(element => element.Attribute("id")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceBreak in sourceBreaks.Elements(workbookNs + "brk"))
        {
            var id = sourceBreak.Attribute("id")?.Value;
            if (XlsxWorksheetPageBreakIdReader.TryReadSupportedId(
                sourceBreak,
                maxBreakId,
                out var sourceId))
            {
                if (!modeledBreakIds.Contains(sourceId))
                    continue;

                if (targetBreaksBySupportedId.TryGetValue(sourceId, out var targetBreak))
                {
                    changed |= MergeMissingAttributes(sourceBreak, targetBreak);
                    continue;
                }

                targetBreaks.Add(new XElement(sourceBreak));
                var addedBreak = targetBreaks.Elements(workbookNs + "brk").Last();
                targetBreaksBySupportedId[sourceId] = addedBreak;
                if (!string.IsNullOrWhiteSpace(id))
                    targetBreaksByRawId[id] = addedBreak;
                changed = true;
                continue;
            }

            if (!ShouldRetainUnsupportedBreak(maxBreakId, sourceId, modeledBreakIds))
                continue;

            if (!string.IsNullOrWhiteSpace(id) &&
                targetBreaksByRawId.ContainsKey(id))
            {
                continue;
            }

            targetBreaks.Add(new XElement(sourceBreak));
            if (!string.IsNullOrWhiteSpace(id))
                targetBreaksByRawId[id] = targetBreaks.Elements(workbookNs + "brk").Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetCalculationProperties(
        XElement sourceSheetCalcPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var targetSheetCalcPr = targetRoot.Element(workbookNs + "sheetCalcPr");
        if (targetSheetCalcPr is null)
        {
            var retained = new XElement(sourceSheetCalcPr);
            retained.Attribute("fullCalcOnLoad")?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            XlsxWorksheetElementOrder.Insert(targetRoot, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourceSheetCalcPr, targetSheetCalcPr, ["fullCalcOnLoad"]);
        foreach (var sourceChild in sourceSheetCalcPr.Elements())
        {
            var targetChild = FindChildByIdentity(targetSheetCalcPr, sourceChild);
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetSheetCalcPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetPhoneticProperties(
        XElement sourcePhoneticPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var modeledAttributes = new[] { "fontId", "type", "alignment" };
        var targetPhoneticPr = targetRoot.Element(workbookNs + "phoneticPr");
        if (targetPhoneticPr is null)
        {
            var retained = new XElement(sourcePhoneticPr);
            foreach (var attributeName in modeledAttributes)
                retained.Attribute(attributeName)?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            XlsxWorksheetElementOrder.Insert(targetRoot, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourcePhoneticPr, targetPhoneticPr, modeledAttributes);
        foreach (var sourceChild in sourcePhoneticPr.Elements())
        {
            var targetChild = FindChildByIdentity(targetPhoneticPr, sourceChild);
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetPhoneticPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetCustomProperties(
        XElement sourceCustomProperties,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledPropertyNames)
    {
        var targetCustomProperties = targetRoot.Element(workbookNs + "customProperties");
        if (targetCustomProperties is null)
        {
            var retainedProperties = sourceCustomProperties
                .Elements(workbookNs + "customPr")
                .Where(property => !IsSupportedWorksheetCustomProperty(property))
                .Select(property => new XElement(property))
                .ToList();
            if (retainedProperties.Count == 0)
                return false;

            XlsxWorksheetElementOrder.Insert(
                targetRoot,
                new XElement(sourceCustomProperties.Name, sourceCustomProperties.Attributes(), retainedProperties));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomProperties, targetCustomProperties, []);
        var targetPropertiesByName = targetCustomProperties
            .Elements(workbookNs + "customPr")
            .Select(property => new
            {
                Name = property.Attribute("name")?.Value,
                Element = property
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Element, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceProperty in sourceCustomProperties.Elements(workbookNs + "customPr"))
        {
            var name = sourceProperty.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && targetPropertiesByName.TryGetValue(name, out var targetProperty))
            {
                changed |= MergeMissingAttributes(sourceProperty, targetProperty, ["name", "id"]);
                foreach (var sourceChild in sourceProperty.Elements())
                {
                    var targetChild = FindChildByIdentity(targetProperty, sourceChild);
                    if (targetChild is not null)
                    {
                        if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                            changed = true;
                        continue;
                    }

                    targetProperty.Add(new XElement(sourceChild));
                    changed = true;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(name) && modeledPropertyNames.Contains(name))
                continue;

            if (IsSupportedWorksheetCustomProperty(sourceProperty))
                continue;

            targetCustomProperties.Add(new XElement(sourceProperty));
            if (!string.IsNullOrWhiteSpace(name))
                targetPropertiesByName[name] = targetCustomProperties.Elements(workbookNs + "customPr").Last();
            changed = true;
        }

        return changed;
    }

    private static XElement? FindChildByIdentity(XElement parent, XElement sourceChild)
    {
        var sourceIdentityKey = ElementIdentityKey(sourceChild);
        foreach (var child in parent.Elements(sourceChild.Name))
        {
            if (ElementIdentityKey(child) == sourceIdentityKey)
                return child;
        }

        return null;
    }

    private static bool RebindWorksheetCustomPropertyRelationships(
        XlsxSourcePackagePreservationContext context,
        XElement sourceCustomProperties,
        XElement targetRoot,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        const string customPropertyRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty";

        var targetCustomProperties = targetRoot.Element(workbookNs + "customProperties");
        if (targetCustomProperties is null)
            return false;

        var sourcePropertiesByName = sourceCustomProperties
            .Elements(workbookNs + "customPr")
            .Select(property => new
            {
                Name = property.Attribute("name")?.Value,
                RelationshipId = property.Attribute(relNs + "id")?.Value
            })
            .Where(property =>
                !string.IsNullOrWhiteSpace(property.Name) &&
                !string.IsNullOrWhiteSpace(property.RelationshipId))
            .GroupBy(property => property.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().RelationshipId!,
                StringComparer.OrdinalIgnoreCase);
        if (sourcePropertiesByName.Count == 0)
            return false;

        var sourceRelationshipTargets = context.GetSourceRelationshipTargets(sourceWorksheetPath);
        if (sourceRelationshipTargets.Count == 0)
            return false;

        var targetArchive = context.TargetArchive;
        var (targetRelsPath, targetRelsXml) = context.LoadOrCreateTargetRelationships(targetWorksheetPath);

        var changed = false;
        var relsChanged = targetArchive.GetEntry(targetRelsPath) is null;
        foreach (var targetProperty in targetCustomProperties.Elements(workbookNs + "customPr"))
        {
            var name = targetProperty.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                !sourcePropertiesByName.TryGetValue(name, out var sourceRelId) ||
                !sourceRelationshipTargets.TryGetValue(sourceRelId, out var sourceTargetPath) ||
                !sourceTargetPath.StartsWith("xl/customProperty/", StringComparison.OrdinalIgnoreCase) ||
                targetArchive.GetEntry(sourceTargetPath) is null)
            {
                continue;
            }

            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                sourceTargetPath,
                customPropertyRelationshipType);
            if (string.IsNullOrWhiteSpace(targetRelId))
                continue;

            relsChanged = true;
            if (!string.Equals(targetProperty.Attribute(relNs + "id")?.Value, targetRelId, StringComparison.Ordinal))
            {
                targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
                targetProperty.SetAttributeValue(relNs + "id", targetRelId);
                changed = true;
            }
        }

        if (relsChanged)
            context.ReplaceTargetPartXml(targetRelsPath, targetRelsXml);

        return changed;
    }

    private static void RebindWorksheetCustomPropertyRelationships(
        XlsxSourcePackagePreservationContext context,
        IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata)
    {
        var targetArchive = context.TargetArchive;
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the LOAD-TIME name -- resolve
            // via the shared rename-tolerant fallback so a renamed sheet's custom-property
            // relationships still get rebound instead of being dropped like a deleted sheet's.
            if (!XlsxRenamedSourceSheetResolver.TryResolveTargetWorksheetPath(
                    context, sheetName, sourceWorksheetPath, out var targetWorksheetPath))
            {
                continue;
            }
            if (worksheetsWithPreservableSourceMetadata is not null &&
                !worksheetsWithPreservableSourceMetadata.Contains(sheetName))
            {
                continue;
            }

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
            var sourceCustomProperties = sourceWorksheetXml?.Root?.Element(context.WorkbookNs + "customProperties");
            if (sourceCustomProperties is null)
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            if (RebindWorksheetCustomPropertyRelationships(
                context,
                sourceCustomProperties,
                targetRoot,
                sourceWorksheetPath,
                targetWorksheetPath,
                context.WorkbookNs,
                context.RelNs))
            {
                XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
            }
        }
    }

    private static bool IsSupportedWorksheetCustomProperty(XElement customProperty)
    {
        return !string.IsNullOrWhiteSpace(customProperty.Attribute("name")?.Value) &&
               int.TryParse(
                   customProperty.Attribute("id")?.Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var id) &&
               id > 0;
    }

}
