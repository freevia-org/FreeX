using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Renderer-facing Quick Analysis item: display text, preview metadata, and the neutral execution route.
/// Each renderer keeps its own controls; this is the shared model those controls render.
/// </summary>
public sealed record QuickAnalysisDisplayItem(
    string Id,
    QuickAnalysisGroup Group,
    string Label,
    QuickAnalysisCommandRoute Route,
    QuickAnalysisPreviewKind PreviewKind,
    string PreviewText,
    QuickAnalysisPreviewVisual PreviewVisual,
    QuickAnalysisCommand? Command = null);

/// <summary>A named group of renderer-facing Quick Analysis items, in display order.</summary>
public sealed record QuickAnalysisDisplayGroup(
    QuickAnalysisGroup Group,
    IReadOnlyList<QuickAnalysisDisplayItem> Items);

/// <summary>Renderer-facing Quick Analysis display model, grouped in Office tab order.</summary>
public sealed record QuickAnalysisDisplayModel(IReadOnlyList<QuickAnalysisDisplayGroup> Groups)
{
    public static QuickAnalysisDisplayModel Empty { get; } = new([]);

    public bool IsEmpty => Groups.Count == 0;

    public IEnumerable<QuickAnalysisDisplayItem> AllItems()
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
                yield return item;
        }
    }

    public IReadOnlyList<QuickAnalysisDisplayItem> ItemsFor(QuickAnalysisGroup group)
    {
        foreach (var entry in Groups)
        {
            if (entry.Group == group)
                return entry.Items;
        }

        return [];
    }

    internal static QuickAnalysisDisplayModel FromItems(IReadOnlyList<QuickAnalysisDisplayItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var groups = new List<QuickAnalysisDisplayGroup>();
        List<QuickAnalysisDisplayItem>? currentItems = null;
        foreach (var item in items)
        {
            if (groups.Count == 0 || groups[^1].Group != item.Group)
            {
                currentItems = [];
                groups.Add(new QuickAnalysisDisplayGroup(item.Group, currentItems));
            }

            currentItems!.Add(item);
        }

        return groups.Count == 0 ? Empty : new QuickAnalysisDisplayModel(groups);
    }
}

/// <summary>Hover/keyboard-focus preview metadata for a renderer-facing Quick Analysis item.</summary>
public sealed record QuickAnalysisDisplayHoverPreview(
    GridRange Range,
    QuickAnalysisPreviewKind PreviewKind,
    string Label,
    string StatusText,
    QuickAnalysisCommandRoute Route,
    QuickAnalysisPreviewVisual PreviewVisual);
