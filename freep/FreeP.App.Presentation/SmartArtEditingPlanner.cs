using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SmartArtNodeEditKind
{
    ChangeText,
    AddSiblingAfter,
    AddChild,
    Remove
}

public sealed record SmartArtNodeEditIntent(
    SmartArtNodeEditKind Kind,
    string TargetModelId,
    string? Text = null)
{
    public static SmartArtNodeEditIntent ChangeText(string targetModelId, string text) =>
        new(SmartArtNodeEditKind.ChangeText, targetModelId, text);

    public static SmartArtNodeEditIntent AddSiblingAfter(string targetModelId, string? text = null) =>
        new(SmartArtNodeEditKind.AddSiblingAfter, targetModelId, text);

    public static SmartArtNodeEditIntent AddChild(string targetModelId, string? text = null) =>
        new(SmartArtNodeEditKind.AddChild, targetModelId, text);

    public static SmartArtNodeEditIntent Remove(string targetModelId) =>
        new(SmartArtNodeEditKind.Remove, targetModelId);
}

public sealed record SmartArtNodeOutlineItem(
    string ModelId,
    string Text,
    int Level,
    int SiblingIndex,
    bool IsAssistant);

public sealed record SmartArtNodeEditResult(
    bool Applied,
    SmartArtNodeEditKind Kind,
    string? TargetModelId,
    string? SelectedModelId,
    string Message,
    IReadOnlyList<SmartArtNodeOutlineItem> Outline)
{
    public static SmartArtNodeEditResult NotApplied(
        SmartArtNodeEditKind kind,
        string? targetModelId,
        string message,
        IReadOnlyList<SmartArtNodeOutlineItem>? outline = null) =>
        new(false, kind, targetModelId, null, message, outline ?? Array.Empty<SmartArtNodeOutlineItem>());
}

public static class SmartArtEditingPlanner
{
    public const string DefaultNewNodeText = "New node";

    public static SmartArtNodeEditResult Apply(SmartArtData? data, SmartArtNodeEditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (data is null)
        {
            return SmartArtNodeEditResult.NotApplied(
                intent.Kind,
                intent.TargetModelId,
                "No SmartArt data model is available.");
        }

        var targetId = intent.TargetModelId?.Trim() ?? string.Empty;
        if (targetId.Length == 0)
        {
            return SmartArtNodeEditResult.NotApplied(
                intent.Kind,
                intent.TargetModelId,
                "A SmartArt node id is required.",
                BuildOutline(data));
        }

        var location = FindLocation(data, targetId);
        if (location.Node is null)
        {
            return SmartArtNodeEditResult.NotApplied(
                intent.Kind,
                targetId,
                "The SmartArt node was not found.",
                BuildOutline(data));
        }

        return intent.Kind switch
        {
            SmartArtNodeEditKind.ChangeText => ChangeText(data, location.Node, targetId, intent.Text),
            SmartArtNodeEditKind.AddSiblingAfter => AddSiblingAfter(data, location, targetId, intent.Text),
            SmartArtNodeEditKind.AddChild => AddChild(data, location.Node, targetId, intent.Text),
            SmartArtNodeEditKind.Remove => Remove(data, location, targetId),
            _ => SmartArtNodeEditResult.NotApplied(intent.Kind, targetId, "Unsupported SmartArt edit.", BuildOutline(data))
        };
    }

    public static IReadOnlyList<SmartArtNodeOutlineItem> BuildOutline(SmartArtData? data)
    {
        if (data is null)
            return Array.Empty<SmartArtNodeOutlineItem>();

        var items = new List<SmartArtNodeOutlineItem>();
        for (var i = 0; i < data.Nodes.Count; i++)
            CollectOutline(data.Nodes[i], i, items);
        return items;
    }

    private static SmartArtNodeEditResult ChangeText(
        SmartArtData data,
        SmartArtNode target,
        string targetId,
        string? text)
    {
        target.Text = NormalizeText(text);
        return Applied(data, SmartArtNodeEditKind.ChangeText, targetId, target.ModelId, "SmartArt node text updated.");
    }

    private static SmartArtNodeEditResult AddSiblingAfter(
        SmartArtData data,
        SmartArtNodeLocation location,
        string targetId,
        string? text)
    {
        var siblings = location.Parent is null ? data.Nodes : location.Parent.Children;
        var insertAt = Math.Clamp(location.Index + 1, 0, siblings.Count);
        var node = CreateNode(data, text, location.Node!.Level, isAssistant: location.Node.IsAssistant);
        siblings.Insert(insertAt, node);
        NormalizeLevels(data);

        return Applied(data, SmartArtNodeEditKind.AddSiblingAfter, targetId, node.ModelId, "SmartArt sibling node added.");
    }

    private static SmartArtNodeEditResult AddChild(
        SmartArtData data,
        SmartArtNode target,
        string targetId,
        string? text)
    {
        var node = CreateNode(data, text, target.Level + 1, isAssistant: false);
        target.Children.Add(node);
        NormalizeLevels(data);

        return Applied(data, SmartArtNodeEditKind.AddChild, targetId, node.ModelId, "SmartArt child node added.");
    }

    private static SmartArtNodeEditResult Remove(
        SmartArtData data,
        SmartArtNodeLocation location,
        string targetId)
    {
        if (CountNodes(data) <= 1)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.Remove,
                targetId,
                "At least one SmartArt node must remain.",
                BuildOutline(data));
        }

        var siblings = location.Parent is null ? data.Nodes : location.Parent.Children;
        siblings.RemoveAt(location.Index);
        NormalizeLevels(data);

        var selected = PickSelectionAfterRemove(data, siblings, location.Index, location.Parent);
        return Applied(data, SmartArtNodeEditKind.Remove, targetId, selected?.ModelId, "SmartArt node removed.");
    }

    private static SmartArtNode CreateNode(SmartArtData data, string? text, int level, bool isAssistant)
    {
        return new SmartArtNode
        {
            ModelId = CreateModelId(data),
            Text = string.IsNullOrWhiteSpace(text) ? DefaultNewNodeText : NormalizeText(text),
            Level = Math.Max(0, level),
            IsAssistant = isAssistant
        };
    }

    private static string CreateModelId(SmartArtData data)
    {
        var existing = new HashSet<string>(
            EnumerateNodes(data).Select(n => n.ModelId),
            StringComparer.OrdinalIgnoreCase);

        for (var index = existing.Count + 1; ; index++)
        {
            var candidate = $"freep-smartart-node-{index}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    private static SmartArtNodeEditResult Applied(
        SmartArtData data,
        SmartArtNodeEditKind kind,
        string targetId,
        string? selectedModelId,
        string message) =>
        new(true, kind, targetId, selectedModelId, message, BuildOutline(data));

    private static SmartArtNode? PickSelectionAfterRemove(
        SmartArtData data,
        IReadOnlyList<SmartArtNode> siblings,
        int removedIndex,
        SmartArtNode? parent)
    {
        if (siblings.Count > 0)
            return siblings[Math.Clamp(removedIndex, 0, siblings.Count - 1)];

        return parent ?? data.Nodes.FirstOrDefault();
    }

    private static SmartArtNodeLocation FindLocation(SmartArtData data, string modelId)
    {
        for (var i = 0; i < data.Nodes.Count; i++)
        {
            var match = FindLocation(data.Nodes[i], parent: null, i, modelId);
            if (match.Node is not null)
                return match;
        }

        return SmartArtNodeLocation.NotFound;
    }

    private static SmartArtNodeLocation FindLocation(
        SmartArtNode node,
        SmartArtNode? parent,
        int index,
        string modelId)
    {
        if (StringComparer.Ordinal.Equals(node.ModelId, modelId))
            return new SmartArtNodeLocation(node, parent, index);

        for (var i = 0; i < node.Children.Count; i++)
        {
            var match = FindLocation(node.Children[i], node, i, modelId);
            if (match.Node is not null)
                return match;
        }

        return SmartArtNodeLocation.NotFound;
    }

    private static IEnumerable<SmartArtNode> EnumerateNodes(SmartArtData data)
    {
        foreach (var root in data.Nodes)
        {
            foreach (var node in EnumerateNodes(root))
                yield return node;
        }
    }

    private static IEnumerable<SmartArtNode> EnumerateNodes(SmartArtNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in EnumerateNodes(child))
                yield return descendant;
        }
    }

    private static int CountNodes(SmartArtData data) => EnumerateNodes(data).Count();

    private static void NormalizeLevels(SmartArtData data)
    {
        foreach (var root in data.Nodes)
            NormalizeLevels(root, 0);
    }

    private static void NormalizeLevels(SmartArtNode node, int level)
    {
        node.Level = level;
        foreach (var child in node.Children)
            NormalizeLevels(child, level + 1);
    }

    private static void CollectOutline(SmartArtNode node, int siblingIndex, List<SmartArtNodeOutlineItem> items)
    {
        items.Add(new SmartArtNodeOutlineItem(node.ModelId, node.Text, node.Level, siblingIndex, node.IsAssistant));
        for (var i = 0; i < node.Children.Count; i++)
            CollectOutline(node.Children[i], i, items);
    }

    private static string NormalizeText(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private readonly record struct SmartArtNodeLocation(SmartArtNode? Node, SmartArtNode? Parent, int Index)
    {
        public static SmartArtNodeLocation NotFound => new(null, null, -1);
    }
}
