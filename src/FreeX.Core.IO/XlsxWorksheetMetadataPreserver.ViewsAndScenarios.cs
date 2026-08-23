using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet custom sheet view and scenario native metadata preservation.
    private static bool MergeWorksheetCustomSheetViews(
        XElement sourceCustomSheetViews,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledCustomViewIds)
    {
        var targetCustomSheetViews = targetRoot.Element(workbookNs + "customSheetViews");
        if (targetCustomSheetViews is null)
        {
            var retainedViews = sourceCustomSheetViews
                .Elements(workbookNs + "customSheetView")
                .Where(view => !modeledCustomViewIds.Contains(XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value) ?? string.Empty))
                .Select(CloneCustomSheetViewForPreservation)
                .ToList();
            if (retainedViews.Count == 0)
                return false;

            XlsxWorksheetElementOrder.Insert(
                targetRoot,
                new XElement(
                    sourceCustomSheetViews.Name,
                    sourceCustomSheetViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration),
                    retainedViews));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomSheetViews, targetCustomSheetViews, []);
        var targetViewsById = targetCustomSheetViews
            .Elements(workbookNs + "customSheetView")
            .Select(view => new
            {
                Id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value),
                View = view
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().View, StringComparer.OrdinalIgnoreCase);

        foreach (var originalSourceView in sourceCustomSheetViews.Elements(workbookNs + "customSheetView"))
        {
            var sourceView = CloneCustomSheetViewForPreservation(originalSourceView);
            var id = XlsxCustomViewMapper.NormalizeId(sourceView.Attribute("guid")?.Value);
            if (!string.IsNullOrWhiteSpace(id) && targetViewsById.TryGetValue(id, out var targetView))
            {
                changed |= MergeModeledCustomSheetViewMetadata(sourceView, targetView, workbookNs);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && modeledCustomViewIds.Contains(id))
                continue;

            targetCustomSheetViews.Add(sourceView);
            if (!string.IsNullOrWhiteSpace(id))
                targetViewsById[id] = targetCustomSheetViews.Elements(workbookNs + "customSheetView").Last();
            changed = true;
        }

        return changed;
    }

    private static XElement CloneCustomSheetViewForPreservation(XElement sourceView)
    {
        var clone = new XElement(sourceView);
        if (XlsxCustomViewMapper.NormalizeId(clone.Attribute("guid")?.Value) is { } id)
            clone.SetAttributeValue("guid", id);
        return clone;
    }

    private static bool MergeModeledCustomSheetViewMetadata(
        XElement sourceView,
        XElement targetView,
        XNamespace workbookNs)
    {
        var changed = MergeMissingAttributes(
            sourceView,
            targetView,
            ["guid", "view", "showGridLines", "showRowCol", "showRuler", "scale", "showFormulas", "state"]);

        var sourcePane = sourceView.Element(workbookNs + "pane");
        var targetPane = targetView.Element(workbookNs + "pane");
        if (sourcePane is not null && targetPane is not null)
        {
            changed |= MergeMissingAttributes(sourcePane, targetPane, ["xSplit", "ySplit", "state"]);
            foreach (var sourceChild in sourcePane.Elements())
            {
                var targetChild = FindChildByIdentityKey(targetPane, sourceChild);
                if (targetChild is not null)
                {
                    if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                        changed = true;
                    continue;
                }

                targetPane.Add(new XElement(sourceChild));
                changed = true;
            }
        }

        foreach (var sourceChild in sourceView.Elements().Where(child => child.Name != workbookNs + "pane"))
        {
            var targetChild = FindChildByIdentityKey(targetView, sourceChild);
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetView.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetScenarios(
        XElement sourceScenarios,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<string> modeledScenarioNames)
    {
        var targetScenarios = targetRoot.Element(workbookNs + "scenarios");
        var changed = false;
        if (targetScenarios is not null)
        {
            changed |= MergeMissingAttributes(sourceScenarios, targetScenarios, ["current", "show"]);
        }

        foreach (var sourceScenario in sourceScenarios.Elements(workbookNs + "scenario"))
        {
            var name = sourceScenario.Attribute("name")?.Value;
            var supported = IsSupportedWorksheetScenario(sourceScenario, workbookNs);
            if (supported)
            {
                if (string.IsNullOrWhiteSpace(name) || !modeledScenarioNames.Contains(name))
                    continue;

                if (targetScenarios is null)
                {
                    targetScenarios = new XElement(
                        workbookNs + "scenarios",
                        sourceScenarios.Attributes()
                            .Where(attribute => !IsScenarioListIndexAttribute(attribute))
                            .Select(attribute => new XAttribute(attribute)));
                    XlsxWorksheetElementOrder.Insert(targetRoot, targetScenarios);
                    changed = true;
                }

                var targetScenario = FindScenarioByName(targetScenarios, workbookNs, name);
                if (targetScenario is not null)
                {
                    changed |= MergeScenarioMetadata(sourceScenario, targetScenario, workbookNs);
                }

                continue;
            }

            if (targetScenarios is null)
            {
                targetScenarios = new XElement(
                    workbookNs + "scenarios",
                    sourceScenarios.Attributes()
                        .Where(attribute => !IsScenarioListIndexAttribute(attribute))
                        .Select(attribute => new XAttribute(attribute)));
                XlsxWorksheetElementOrder.Insert(targetRoot, targetScenarios);
                changed = true;
            }

            if (HasEquivalentScenario(targetScenarios, sourceScenario))
                continue;

            targetScenarios.Add(new XElement(sourceScenario));
            changed = true;
        }

        return changed;
    }

    private static bool IsSupportedWorksheetScenario(XElement scenario, XNamespace workbookNs)
    {
        if (string.IsNullOrWhiteSpace(scenario.Attribute("name")?.Value))
            return false;

        var inputCells = scenario.Elements(workbookNs + "inputCells").ToList();
        if (inputCells.Count == 0)
            return false;

        return inputCells.All(inputCell =>
            !string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value) &&
            inputCell.Attribute("val") is not null &&
            CellAddress.TryParse(inputCell.Attribute("r")!.Value, SheetId.New(), out _));
    }

    private static bool MergeScenarioMetadata(XElement sourceScenario, XElement targetScenario, XNamespace workbookNs)
    {
        var changed = MergeMissingAttributes(sourceScenario, targetScenario, ["name", "count", "comment"]);

        foreach (var sourceChild in sourceScenario.Elements().Where(child => child.Name != workbookNs + "inputCells"))
        {
            var targetChild = FindChildByIdentityKey(targetScenario, sourceChild);
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetScenario.Add(new XElement(sourceChild));
            changed = true;
        }

        var targetInputCellsByReference = targetScenario
            .Elements(workbookNs + "inputCells")
            .Where(inputCell => !string.IsNullOrWhiteSpace(inputCell.Attribute("r")?.Value))
            .GroupBy(inputCell => inputCell.Attribute("r")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var sourceInputCell in sourceScenario.Elements(workbookNs + "inputCells"))
        {
            var reference = sourceInputCell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !targetInputCellsByReference.TryGetValue(reference, out var targetInputCell))
            {
                continue;
            }

            if (MergeMissingAttributes(sourceInputCell, targetInputCell, ["r", "val"]))
                changed = true;
            foreach (var sourceChild in sourceInputCell.Elements())
            {
                var targetChild = FindChildByIdentityKey(targetInputCell, sourceChild);
                if (targetChild is not null)
                {
                    if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                        changed = true;
                    continue;
                }

                targetInputCell.Add(new XElement(sourceChild));
                changed = true;
            }
        }

        return changed;
    }

    private static XElement? FindChildByIdentityKey(XElement target, XElement sourceChild)
    {
        var sourceKey = ElementIdentityKey(sourceChild);
        foreach (var child in target.Elements(sourceChild.Name))
        {
            if (ElementIdentityKey(child) == sourceKey)
                return child;
        }

        return null;
    }

    private static XElement? FindScenarioByName(XElement targetScenarios, XNamespace workbookNs, string? name)
    {
        foreach (var element in targetScenarios.Elements(workbookNs + "scenario"))
        {
            if (string.Equals(element.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
                return element;
        }

        return null;
    }

    private static bool IsScenarioListIndexAttribute(XAttribute attribute)
    {
        return !attribute.IsNamespaceDeclaration &&
               string.IsNullOrEmpty(attribute.Name.NamespaceName) &&
               (string.Equals(attribute.Name.LocalName, "current", StringComparison.Ordinal) ||
                string.Equals(attribute.Name.LocalName, "show", StringComparison.Ordinal));
    }

    private static bool HasEquivalentScenario(XElement targetScenarios, XElement sourceScenario)
    {
        var sourceRaw = sourceScenario.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        return targetScenarios
            .Elements(sourceScenario.Name)
            .Any(targetScenario => string.Equals(
                targetScenario.ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
                sourceRaw,
                StringComparison.Ordinal));
    }

}
