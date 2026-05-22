using System.Globalization;
using System.Xml.Linq;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet page break, calculation, phonetic, and custom-property native metadata preservation.
    private static HashSet<uint> GetModeledWorksheetBreakIds(Workbook workbook, string sheetName, bool rowBreaks)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return [];

        var maxBreakId = rowBreaks ? CellAddress.MaxRow : CellAddress.MaxCol;
        return (rowBreaks ? sheet.RowPageBreaks : sheet.ColumnPageBreaks)
            .Where(id => IsSupportedWorksheetBreakId(id, maxBreakId))
            .ToHashSet();
    }

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
                    !TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId) ||
                    modeledBreakIds.Contains(sourceId))
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
                Parsed = TryGetSupportedWorksheetBreakId(element, maxBreakId, out var id),
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
            if (TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId))
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

    private static bool TryGetSupportedWorksheetBreakId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        var rawId = breakElement.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(rawId) ||
            !uint.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        return IsSupportedWorksheetBreakId(id, maxBreakId);
    }

    private static bool IsSupportedWorksheetBreakId(uint id, uint maxBreakId)
    {
        return id >= 2 && id <= maxBreakId;
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

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourceSheetCalcPr, targetSheetCalcPr, ["fullCalcOnLoad"]);
        foreach (var sourceChild in sourceSheetCalcPr.Elements())
        {
            var targetChild = targetSheetCalcPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
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

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourcePhoneticPr, targetPhoneticPr, modeledAttributes);
        foreach (var sourceChild in sourcePhoneticPr.Elements())
        {
            var targetChild = targetPhoneticPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
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

            InsertWorksheetMetadataElementInOrder(
                targetRoot,
                workbookNs,
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
                    var targetChild = targetProperty.Elements(sourceChild.Name)
                        .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
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
