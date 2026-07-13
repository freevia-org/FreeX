using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

        // defaultColWidth/defaultRowHeight are modeled directly on Sheet.DefaultColumnWidth/
        // Sheet.DefaultRowHeight (see XlsxWorksheetDimensionDefaultsWriter.ModeledSheetFormatAttributes)
        // and are intentionally omitted by the modeled writer whenever the value is at Excel's default
        // (XlsxWorksheetDimensionDefaultsWriter.IsNonDefaultColumnWidth/IsNonDefaultRowHeight). They
        // must never be copied back from the stale pre-edit source sheetFormatPr, otherwise resetting
        // either value to the Excel default is silently reverted on the next full-rebuild save.
        string[] modeledAttributes = ["defaultColWidth", "defaultRowHeight"];
        var modeledAttributeNames = modeledAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceSheetFormatProperties.Attributes())
        {
            if (modeledAttributeNames.Contains(attribute.Name))
                continue;

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

    private static bool MergeWorksheetSheetViews(
        XElement? sourceSheetViews,
        XElement targetRoot,
        XNamespace workbookNs,
        Sheet? sheet)
    {
        if (sourceSheetViews is null)
            return false;

        var modeledAdditionalViewIds = sheet?.AdditionalViews?.Views
            .Select(view => view.WorkbookViewId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceViews = sourceSheetViews
            .Elements(workbookNs + "sheetView")
            .Where(view => ShouldPreserveWorksheetSheetView(view, modeledAdditionalViewIds))
            .Select(CloneSheetViewForPreservation)
            .ToList();
        if (sourceViews.Count == 0)
            return false;

        var targetSheetViews = targetRoot.Element(workbookNs + "sheetViews");
        if (targetSheetViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetViews.Name, sourceSheetViews.Attributes(), sourceViews));
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
            XElement? targetView = null;
            if (!string.IsNullOrWhiteSpace(viewId))
            {
                foreach (var element in targetSheetViews.Elements(workbookNs + "sheetView"))
                {
                    if (!string.Equals(
                            element.Attribute("workbookViewId")?.Value,
                            viewId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    targetView = element;
                    break;
                }
            }

            if (targetView is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView, ModeledSheetViewMergeAttributes))
                    changed = true;
                if (XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(targetView))
                    changed = true;
                continue;
            }

            targetSheetViews.Add(sourceView);
            if (!string.IsNullOrWhiteSpace(viewId))
                existingViewIds.Add(viewId);
            changed = true;
        }

        return changed;
    }

    private static bool ShouldPreserveWorksheetSheetView(XElement sourceView, HashSet<string>? modeledAdditionalViewIds)
    {
        var workbookViewId = sourceView.Attribute("workbookViewId")?.Value;
        if (string.IsNullOrWhiteSpace(workbookViewId) ||
            string.Equals(workbookViewId, "0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return modeledAdditionalViewIds?.Contains(workbookViewId) == true;
    }

    private static XElement CloneSheetViewForPreservation(XElement sourceView)
    {
        var clone = new XElement(sourceView);
        XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(clone);
        return clone;
    }

}
