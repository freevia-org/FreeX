using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet sheet format and sheet view native metadata preservation.
    private static bool MergeWorksheetSheetFormatProperties(XElement? sourceSheetFormatProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetFormatProperties is null)
            return false;

        var targetSheetFormatProperties = targetRoot.Element(workbookNs + "sheetFormatPr");
        if (targetSheetFormatProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetFormatProperties));
            return true;
        }

        string[] nativeOnlyAttributes =
        [
            "baseColWidth",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        ];
        var nativeOnlyAttributeNames = nativeOnlyAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceSheetFormatProperties.Attributes())
        {
            if (targetSheetFormatProperties.Attribute(attribute.Name) is not null &&
                !nativeOnlyAttributeNames.Contains(attribute.Name))
            {
                continue;
            }

            if (targetSheetFormatProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetSheetFormatProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetSheetFormatProperties
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sourceChild in sourceSheetFormatProperties.Elements())
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.ContainsKey(key))
                continue;

            targetSheetFormatProperties.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetSheetFormatProperties.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetViews(XElement? sourceSheetViews, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetViews is null)
            return false;

        var sourceViews = sourceSheetViews.Elements(workbookNs + "sheetView").ToList();
        if (sourceViews.Count == 0)
            return false;

        var targetSheetViews = targetRoot.Element(workbookNs + "sheetViews");
        if (targetSheetViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetViews));
            return true;
        }

        var existingViewIds = targetSheetViews
            .Elements(workbookNs + "sheetView")
            .Select(element => element.Attribute("workbookViewId")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        if (MergeMissingAttributes(sourceSheetViews, targetSheetViews, []))
            changed = true;

        foreach (var sourceView in sourceViews)
        {
            var viewId = sourceView.Attribute("workbookViewId")?.Value;
            var targetView = !string.IsNullOrWhiteSpace(viewId)
                ? targetSheetViews
                    .Elements(workbookNs + "sheetView")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("workbookViewId")?.Value,
                        viewId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                continue;
            }

            targetSheetViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(viewId))
                existingViewIds.Add(viewId);
            changed = true;
        }

        return changed;
    }

}
