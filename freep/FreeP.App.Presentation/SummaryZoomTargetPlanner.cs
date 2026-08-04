using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SummaryZoomTargetEditPlan(
    IReadOnlyList<SummaryZoomTarget> Targets,
    string RawXml);

/// <summary>Plans a Summary Zoom target-list edit while preserving retained tile payloads.</summary>
public static class SummaryZoomTargetPlanner
{
    public const string CommandId = "freep.edit-summary-zoom-targets";
    public const string DialogTitle = "Edit Summary Zoom Targets";

    /// <summary>Returns the selected target ids in the order shown by the editor.</summary>
    public static IReadOnlyList<string> SelectOrderedTargets(
        IEnumerable<string> orderedTargetIds,
        IEnumerable<string> selectedTargetIds)
    {
        ArgumentNullException.ThrowIfNull(orderedTargetIds);
        ArgumentNullException.ThrowIfNull(selectedTargetIds);

        var selected = selectedTargetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return orderedTargetIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && selected.Contains(id))
            .ToArray();
    }

    public static bool TryBuildPlan(
        Presentation presentation,
        PreservedObjectInfo info,
        IEnumerable<string>? targetSectionIds,
        out SummaryZoomTargetEditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(info);
        plan = null!;

        if (info.SummaryZoomTargets.Count < 2
            || !SummaryZoomInsertionPlanner.TryBuildPlan(
                presentation, targetSectionIds, out var insertionPlan))
            return false;

        XElement root;
        try { root = XElement.Parse(info.RawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        var existingTiles = root.Descendants()
            .Where(element => element.Name.LocalName == "summaryZmObj")
            .ToArray();
        if (existingTiles.Length == 0)
            return false;

        var existingBySection = existingTiles
            .Where(tile => !string.IsNullOrWhiteSpace(tile.Attribute("sectionId")?.Value))
            .ToDictionary(tile => tile.Attribute("sectionId")!.Value,
                StringComparer.OrdinalIgnoreCase);
        var existingTargets = info.SummaryZoomTargets.ToDictionary(
            target => target.SectionId, StringComparer.OrdinalIgnoreCase);
        var effectiveTargets = insertionPlan.Targets
            .Select(target => existingTargets.TryGetValue(target.SectionId, out var existing)
                ? target with
                {
                    OffsetFactorX = existing.OffsetFactorX,
                    OffsetFactorY = existing.OffsetFactorY,
                    ScaleFactorX = existing.ScaleFactorX,
                    ScaleFactorY = existing.ScaleFactorY,
                }
                : target)
            .ToArray();

        var container = existingTiles[0].Parent;
        if (container is null)
            return false;

        foreach (var tile in existingTiles)
            tile.Remove();

        foreach (var target in effectiveTargets)
        {
            var tile = existingBySection.TryGetValue(target.SectionId, out var existingTile)
                ? new XElement(existingTile)
                : NewTileFrom(existingTiles[0], target);
            SetTileAttributes(tile, target);

            var fixedLayout = container.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "fixedLayout");
            if (fixedLayout is null)
                container.Add(tile);
            else
                fixedLayout.AddBeforeSelf(tile);
        }

        plan = new SummaryZoomTargetEditPlan(
            effectiveTargets,
            root.ToString(SaveOptions.DisableFormatting));
        return true;
    }

    private static XElement NewTileFrom(XElement template, SummaryZoomTarget target)
    {
        var tile = new XElement(template);
        foreach (var attribute in tile.DescendantsAndSelf().Attributes().Where(attribute =>
                     attribute.Name.LocalName == "embed").ToArray())
            attribute.Remove();
        foreach (var srcRect in tile.Descendants().Where(element => element.Name.LocalName == "srcRect").ToArray())
            srcRect.Remove();

        var properties = tile.Descendants().FirstOrDefault(element => element.Name.LocalName == "zmPr");
        if (properties is not null)
        {
            properties.SetAttributeValue("id", Guid.NewGuid().ToString("B").ToUpperInvariant());
            properties.SetAttributeValue("returnToParent", "1");
            properties.SetAttributeValue("imageType", "preview");
            properties.Attribute("transitionDur")?.Remove();
            properties.SetAttributeValue("showBg", "1");
        }

        return tile;
    }

    private static void SetTileAttributes(XElement tile, SummaryZoomTarget target)
    {
        tile.SetAttributeValue("sectionId", target.SectionId);
        tile.SetAttributeValue("title", target.Title);
        tile.SetAttributeValue("descr", target.Description);
        tile.SetAttributeValue("offsetFactorX", target.OffsetFactorX);
        tile.SetAttributeValue("offsetFactorY", target.OffsetFactorY);
        tile.SetAttributeValue("scaleFactorX", target.ScaleFactorX);
        tile.SetAttributeValue("scaleFactorY", target.ScaleFactorY);
    }
}
