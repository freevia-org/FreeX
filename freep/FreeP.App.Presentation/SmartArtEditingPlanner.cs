using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SmartArtNodeEditKind
{
    ChangeText,
    AddSiblingAfter,
    AddChild,
    Remove,
    MoveUp,
    MoveDown,
    Promote,
    Demote,
    ToggleAssistant
}

[Flags]
public enum SmartArtTextPaneShortcutModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4
}

public enum SmartArtTextPaneShortcutKey
{
    Enter,
    Tab,
    Up,
    Down
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

    public static SmartArtNodeEditIntent MoveUp(string targetModelId) =>
        new(SmartArtNodeEditKind.MoveUp, targetModelId);

    public static SmartArtNodeEditIntent MoveDown(string targetModelId) =>
        new(SmartArtNodeEditKind.MoveDown, targetModelId);

    public static SmartArtNodeEditIntent Promote(string targetModelId) =>
        new(SmartArtNodeEditKind.Promote, targetModelId);

    public static SmartArtNodeEditIntent Demote(string targetModelId) =>
        new(SmartArtNodeEditKind.Demote, targetModelId);

    public static SmartArtNodeEditIntent ToggleAssistant(string targetModelId) =>
        new(SmartArtNodeEditKind.ToggleAssistant, targetModelId);
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

public sealed record SmartArtDataPartRewriteResult(
    bool Applied,
    string Message,
    string? DataPartPath,
    int NodeCount,
    int ConnectionCount);

public sealed record SmartArtDrawingCacheRegenerationResult(
    bool Applied,
    string Message,
    string? DrawingPartPath,
    int NodeCount,
    int ShapeCount);

public sealed record SmartArtTextPaneOutlineRow(
    string Text,
    int Level,
    bool IsAssistant = false,
    string? ModelId = null);

public sealed record SmartArtTextPaneApplyResult(
    bool Applied,
    string Message,
    int RowCount,
    IReadOnlyList<SmartArtNodeOutlineItem> Outline);

public sealed record SmartArtTextPaneKeyboardRoute(
    string RouteId,
    SmartArtTextPaneShortcutKey Key,
    SmartArtTextPaneShortcutModifiers Modifiers,
    SmartArtNodeEditIntent Intent,
    string Description);

public static class SmartArtEditingPlanner
{
    public const string DefaultNewNodeText = "New node";

    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";

    public static SmartArtTextPaneKeyboardRoute? PlanTextPaneKeyboardRoute(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        string? targetModelId)
    {
        var targetId = NormalizeModelId(targetModelId);
        if (targetId is null)
            return null;

        return (key, modifiers) switch
        {
            (SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.None) =>
                Route(
                    "smartart.text-pane.enter.add-sibling-after",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.AddSiblingAfter(targetId, DefaultNewNodeText),
                    "Enter adds a sibling row after the selected SmartArt text-pane row."),
            (SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.Control) =>
                Route(
                    "smartart.text-pane.ctrl-enter.add-child",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.AddChild(targetId, DefaultNewNodeText),
                    "Ctrl+Enter adds a child row below the selected SmartArt text-pane row."),
            (SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.None) =>
                Route(
                    "smartart.text-pane.tab.demote",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.Demote(targetId),
                    "Tab demotes the selected SmartArt text-pane row."),
            (SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.Shift) =>
                Route(
                    "smartart.text-pane.shift-tab.promote",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.Promote(targetId),
                    "Shift+Tab promotes the selected SmartArt text-pane row."),
            (SmartArtTextPaneShortcutKey.Up, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift) =>
                Route(
                    "smartart.text-pane.alt-shift-up.move-up",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.MoveUp(targetId),
                    "Alt+Shift+Up moves the selected SmartArt text-pane row earlier."),
            (SmartArtTextPaneShortcutKey.Down, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift) =>
                Route(
                    "smartart.text-pane.alt-shift-down.move-down",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.MoveDown(targetId),
                    "Alt+Shift+Down moves the selected SmartArt text-pane row later."),
            _ => null
        };
    }

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
            SmartArtNodeEditKind.MoveUp => Move(data, location, targetId, offset: -1),
            SmartArtNodeEditKind.MoveDown => Move(data, location, targetId, offset: 1),
            SmartArtNodeEditKind.Promote => Promote(data, location, targetId),
            SmartArtNodeEditKind.Demote => Demote(data, location, targetId),
            SmartArtNodeEditKind.ToggleAssistant => ToggleAssistant(data, location.Node!, targetId),
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

    public static SmartArtTextPaneApplyResult ApplyTextPaneOutline(
        SmartArtData? data,
        IReadOnlyList<SmartArtTextPaneOutlineRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (data is null)
        {
            return new SmartArtTextPaneApplyResult(
                false,
                "No SmartArt data model is available.",
                0,
                Array.Empty<SmartArtNodeOutlineItem>());
        }

        if (rows.Count == 0)
        {
            return new SmartArtTextPaneApplyResult(
                false,
                "At least one SmartArt text-pane row is required.",
                0,
                BuildOutline(data));
        }

        var existingNodes = EnumerateNodes(data).ToList();
        var existingById = existingNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ModelId))
            .GroupBy(node => node.ModelId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rebuiltRoots = new List<SmartArtNode>();
        var stack = new List<SmartArtNode>();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var level = Math.Max(0, row.Level);
            if (index == 0 && level != 0)
            {
                return new SmartArtTextPaneApplyResult(
                    false,
                    "The first SmartArt text-pane row must be a root node.",
                    0,
                    BuildOutline(data));
            }

            if (level > stack.Count)
            {
                return new SmartArtTextPaneApplyResult(
                    false,
                    "SmartArt text-pane levels cannot skip a parent level.",
                    0,
                    BuildOutline(data));
            }

            var explicitId = NormalizeModelId(row.ModelId);
            var candidateId = explicitId
                ?? (index < existingNodes.Count ? NormalizeModelId(existingNodes[index].ModelId) : null)
                ?? CreateModelId(usedIds);

            if (usedIds.Contains(candidateId))
            {
                if (explicitId is not null)
                {
                    return new SmartArtTextPaneApplyResult(
                        false,
                        "Duplicate SmartArt text-pane node ids are not allowed.",
                        0,
                        BuildOutline(data));
                }

                candidateId = CreateModelId(usedIds);
            }

            usedIds.Add(candidateId);
            var preservedNode = existingById.TryGetValue(candidateId, out var byId)
                ? byId
                : index < existingNodes.Count
                    ? existingNodes[index]
                    : null;

            var node = new SmartArtNode
            {
                ModelId = candidateId,
                Text = NormalizeText(row.Text),
                Level = level,
                IsAssistant = row.IsAssistant,
                Picture = preservedNode?.Picture
            };

            if (stack.Count > level)
                stack.RemoveRange(level, stack.Count - level);

            if (level == 0)
                rebuiltRoots.Add(node);
            else
                stack[level - 1].Children.Add(node);

            stack.Add(node);
        }

        data.Nodes.Clear();
        data.Nodes.AddRange(rebuiltRoots);
        NormalizeLevels(data);

        return new SmartArtTextPaneApplyResult(
            true,
            "SmartArt text-pane outline applied to the shared model.",
            rows.Count,
            BuildOutline(data));
    }

    public static SmartArtDataPartRewriteResult RewriteDataPart(SmartArtShape? smartArt)
    {
        if (smartArt?.Data is null)
        {
            return new SmartArtDataPartRewriteResult(
                false,
                "No SmartArt data model is available.",
                null,
                0,
                0);
        }

        var dataPart = FindDataPart(smartArt);
        if (dataPart is null)
        {
            return new SmartArtDataPartRewriteResult(
                false,
                "No SmartArt diagram data part is available.",
                null,
                0,
                0);
        }

        var nodeIds = new Dictionary<SmartArtNode, string>();
        var nodeCount = 0;
        var connectionCount = 0;
        var document = BuildDataPartDocument(smartArt.Data, nodeIds, ref nodeCount, ref connectionCount);
        dataPart.Bytes = SerializeXml(document);

        return new SmartArtDataPartRewriteResult(
            true,
            "SmartArt diagram data part regenerated from the shared model.",
            dataPart.PartPath,
            nodeCount,
            connectionCount);
    }

    public static SmartArtDrawingCacheRegenerationResult RegenerateDrawingCache(
        SmartArtShape? smartArt,
        long frameXEmu,
        long frameYEmu,
        long frameCxEmu,
        long frameCyEmu,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (smartArt?.Data is null)
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "No SmartArt data model is available.",
                null,
                0,
                0);
        }

        var drawingPart = FindDrawingPart(smartArt);
        if (drawingPart is null)
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "No SmartArt drawing cache part is available.",
                null,
                CountNodes(smartArt.Data),
                0);
        }

        var plannedShapes = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            frameXEmu,
            frameYEmu,
            frameCxEmu,
            frameCyEmu,
            theme,
            effectiveClrMap,
            smartArt.QuickStyle,
            smartArt.Colors);

        if (plannedShapes is null)
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "The SmartArt layout is not covered by the shared cache regeneration planner.",
                drawingPart.PartPath,
                CountNodes(smartArt.Data),
                0);
        }

        var shapes = plannedShapes.ToList();
        if (shapes.Any(shape => shape.Kind != SlideShapeKind.AutoShape))
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "SmartArt drawing cache regeneration currently supports auto-shape layouts only.",
                drawingPart.PartPath,
                CountNodes(smartArt.Data),
                shapes.Count);
        }

        drawingPart.Bytes = SerializeXml(BuildDrawingCacheDocument(shapes));
        smartArt.DrawingPartPath = drawingPart.PartPath;
        smartArt.FallbackShapes.Clear();
        foreach (var shape in shapes)
            smartArt.FallbackShapes.Add(SlideCloner.CloneShape(shape));

        return new SmartArtDrawingCacheRegenerationResult(
            true,
            "SmartArt drawing cache regenerated from the shared live-layout plan.",
            drawingPart.PartPath,
            CountNodes(smartArt.Data),
            shapes.Count);
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

    private static SmartArtNodeEditResult Move(
        SmartArtData data,
        SmartArtNodeLocation location,
        string targetId,
        int offset)
    {
        var siblings = location.Parent is null ? data.Nodes : location.Parent.Children;
        var destination = location.Index + offset;
        if (destination < 0 || destination >= siblings.Count)
        {
            return SmartArtNodeEditResult.NotApplied(
                offset < 0 ? SmartArtNodeEditKind.MoveUp : SmartArtNodeEditKind.MoveDown,
                targetId,
                offset < 0 ? "The SmartArt node is already first." : "The SmartArt node is already last.",
                BuildOutline(data));
        }

        var node = siblings[location.Index];
        siblings.RemoveAt(location.Index);
        siblings.Insert(destination, node);
        NormalizeLevels(data);

        return Applied(
            data,
            offset < 0 ? SmartArtNodeEditKind.MoveUp : SmartArtNodeEditKind.MoveDown,
            targetId,
            node.ModelId,
            offset < 0 ? "SmartArt node moved up." : "SmartArt node moved down.");
    }

    private static SmartArtNodeEditResult Promote(
        SmartArtData data,
        SmartArtNodeLocation location,
        string targetId)
    {
        if (location.Parent is null)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.Promote,
                targetId,
                "A root SmartArt node cannot be promoted.",
                BuildOutline(data));
        }

        var parentLocation = FindLocation(data, location.Parent.ModelId);
        var currentSiblings = location.Parent.Children;
        var node = currentSiblings[location.Index];
        currentSiblings.RemoveAt(location.Index);

        var promotedSiblings = parentLocation.Parent is null
            ? data.Nodes
            : parentLocation.Parent.Children;
        var insertAt = Math.Clamp(parentLocation.Index + 1, 0, promotedSiblings.Count);
        promotedSiblings.Insert(insertAt, node);
        NormalizeLevels(data);

        return Applied(
            data,
            SmartArtNodeEditKind.Promote,
            targetId,
            node.ModelId,
            "SmartArt node promoted.");
    }

    private static SmartArtNodeEditResult Demote(
        SmartArtData data,
        SmartArtNodeLocation location,
        string targetId)
    {
        var siblings = location.Parent is null ? data.Nodes : location.Parent.Children;
        if (location.Index == 0)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.Demote,
                targetId,
                "The first SmartArt sibling cannot be demoted.",
                BuildOutline(data));
        }

        var node = siblings[location.Index];
        var newParent = siblings[location.Index - 1];
        siblings.RemoveAt(location.Index);
        newParent.Children.Add(node);
        NormalizeLevels(data);

        return Applied(
            data,
            SmartArtNodeEditKind.Demote,
            targetId,
            node.ModelId,
            "SmartArt node demoted.");
    }

    private static SmartArtNodeEditResult ToggleAssistant(
        SmartArtData data,
        SmartArtNode target,
        string targetId)
    {
        if (data.Family != SmartArtFamily.Hierarchy)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ToggleAssistant,
                targetId,
                "Assistant nodes are supported only in hierarchy SmartArt.",
                BuildOutline(data));
        }

        if (target.Level == 0)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ToggleAssistant,
                targetId,
                "A root SmartArt node cannot be an assistant.",
                BuildOutline(data));
        }

        target.IsAssistant = !target.IsAssistant;
        return Applied(
            data,
            SmartArtNodeEditKind.ToggleAssistant,
            targetId,
            target.ModelId,
            target.IsAssistant
                ? "SmartArt node marked as an assistant."
                : "SmartArt assistant designation removed.");
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

        return CreateModelId(existing);
    }

    private static string CreateModelId(HashSet<string> existing)
    {
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

    private static string? NormalizeModelId(string? modelId)
    {
        var trimmed = modelId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeText(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static SmartArtTextPaneKeyboardRoute Route(
        string routeId,
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        SmartArtNodeEditIntent intent,
        string description) =>
        new(routeId, key, modifiers, intent, description);

    private static DiagramPart? FindDataPart(SmartArtShape smartArt) =>
        smartArt.Parts.Values.FirstOrDefault(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase))
        ?? smartArt.Parts.Values.FirstOrDefault(part =>
            part.PartPath.Contains("/data", StringComparison.OrdinalIgnoreCase) &&
            part.PartPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

    private static DiagramPart? FindDrawingPart(SmartArtShape smartArt)
    {
        if (!string.IsNullOrWhiteSpace(smartArt.DrawingPartPath) &&
            smartArt.Parts.TryGetValue(smartArt.DrawingPartPath, out var drawingPart))
        {
            return drawingPart;
        }

        return smartArt.Parts.Values.FirstOrDefault(part =>
            part.ContentType.Contains("diagramDrawing", StringComparison.OrdinalIgnoreCase))
        ?? smartArt.Parts.Values.FirstOrDefault(part =>
            part.PartPath.Contains("/drawing", StringComparison.OrdinalIgnoreCase) &&
            part.PartPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument BuildDrawingCacheDocument(IReadOnlyList<SlideShape> shapes) =>
        new(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dsp + "spTree",
                    shapes.Select(BuildDrawingCacheShape))));

    private static XElement BuildDrawingCacheShape(SlideShape shape)
    {
        var id = shape.Id == 0 ? 1u : shape.Id;
        return new XElement(Dsp + "sp",
            new XElement(Dsp + "nvSpPr",
                new XElement(Dsp + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", string.IsNullOrWhiteSpace(shape.Name) ? $"SmartArt Cache {id}" : shape.Name)),
                new XElement(Dsp + "cNvSpPr")),
            BuildShapeProperties(shape),
            BuildTextBody(shape.TextBody));
    }

    private static XElement BuildShapeProperties(SlideShape shape)
    {
        var spPr = new XElement(Dsp + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off",
                    new XAttribute("x", shape.OffsetXEmu),
                    new XAttribute("y", shape.OffsetYEmu)),
                new XElement(A + "ext",
                    new XAttribute("cx", shape.ExtentCxEmu),
                    new XAttribute("cy", shape.ExtentCyEmu))),
            new XElement(A + "prstGeom",
                new XAttribute("prst", ToPresetGeometry(shape.AutoShapeKind)),
                new XElement(A + "avLst",
                    shape.PresetGeometryAdjustments.Select(pair =>
                        new XElement(A + "gd",
                            new XAttribute("name", pair.Key),
                            new XAttribute("fmla", $"val {pair.Value.ToString("0.########", CultureInfo.InvariantCulture)}"))))));

        if (shape.Fill is ShapeFill.Solid solid)
        {
            spPr.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr",
                    new XAttribute("val", ToHex(solid.Color.Resolved)))));
        }
        else if (shape.Fill is ShapeFill.None)
        {
            spPr.Add(new XElement(A + "noFill"));
        }

        if (shape.Outline is ShapeOutline.Visible outline)
        {
            spPr.Add(new XElement(A + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(outline.WidthPt * 12700.0))),
                new XElement(A + "solidFill",
                    new XElement(A + "srgbClr",
                        new XAttribute("val", ToHex(outline.Color.Resolved))))));
        }
        else if (shape.Outline is ShapeOutline.None)
        {
            spPr.Add(new XElement(A + "ln", new XElement(A + "noFill")));
        }

        return spPr;
    }

    private static XElement BuildTextBody(TextBody? textBody)
    {
        var txBody = new XElement(Dsp + "txBody",
            new XElement(A + "bodyPr"),
            new XElement(A + "lstStyle"));

        if (textBody is null || textBody.Paragraphs.Count == 0)
        {
            txBody.Add(new XElement(A + "p"));
            return txBody;
        }

        foreach (var paragraph in textBody.Paragraphs)
        {
            var p = new XElement(A + "p");
            foreach (var run in paragraph.Runs)
            {
                p.Add(new XElement(A + "r",
                    new XElement(A + "rPr", new XAttribute("lang", "en-US")),
                    new XElement(A + "t", run.Text ?? string.Empty)));
            }

            if (!paragraph.Runs.Any())
                p.Add(new XElement(A + "r", new XElement(A + "t", string.Empty)));

            txBody.Add(p);
        }

        return txBody;
    }

    private static string ToPresetGeometry(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Line => "line",
            DrawingShapeKind.Rectangle => "rect",
            DrawingShapeKind.RoundedRectangle => "roundRect",
            DrawingShapeKind.Triangle => "triangle",
            DrawingShapeKind.Trapezoid => "trapezoid",
            DrawingShapeKind.Chord => "chord",
            _ => "rect"
        };

    private static string ToHex(SrgbColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static XDocument BuildDataPartDocument(
        SmartArtData data,
        Dictionary<SmartArtNode, string> nodeIds,
        ref int nodeCount,
        ref int connectionCount)
    {
        var points = new List<XElement>();
        var connections = new List<XElement>();
        var generatedIdIndex = 1;

        foreach (var root in data.Nodes)
            CollectDataPartElements(root, null, points, connections, nodeIds, ref generatedIdIndex, ref nodeCount, ref connectionCount);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dgm + "ptLst", points),
                new XElement(Dgm + "cxnLst", connections)));
    }

    private static void CollectDataPartElements(
        SmartArtNode node,
        SmartArtNode? parent,
        List<XElement> points,
        List<XElement> connections,
        Dictionary<SmartArtNode, string> nodeIds,
        ref int generatedIdIndex,
        ref int nodeCount,
        ref int connectionCount)
    {
        var id = GetNodeId(node, nodeIds, ref generatedIdIndex);
        points.Add(BuildPointElement(node, id));
        nodeCount++;

        if (parent is not null)
        {
            var parentId = GetNodeId(parent, nodeIds, ref generatedIdIndex);
            connections.Add(new XElement(Dgm + "cxn",
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", parentId),
                new XAttribute("destId", id)));
            connectionCount++;
        }

        foreach (var child in node.Children)
            CollectDataPartElements(child, node, points, connections, nodeIds, ref generatedIdIndex, ref nodeCount, ref connectionCount);
    }

    private static XElement BuildPointElement(SmartArtNode node, string id)
    {
        return new XElement(Dgm + "pt",
            new XAttribute("modelId", id),
            new XAttribute("type", node.IsAssistant ? "asst" : "node"),
            new XElement(Dgm + "t",
                NormalizeText(node.Text)
                    .Split('\n')
                    .Select(paragraph => new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", paragraph))))));
    }

    private static string GetNodeId(
        SmartArtNode node,
        Dictionary<SmartArtNode, string> nodeIds,
        ref int generatedIdIndex)
    {
        if (nodeIds.TryGetValue(node, out var existing))
            return existing;

        var id = string.IsNullOrWhiteSpace(node.ModelId)
            ? $"freep-smartart-node-{generatedIdIndex++}"
            : node.ModelId.Trim();
        nodeIds[node] = id;
        return id;
    }

    private static byte[] SerializeXml(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
            document.Save(writer);
        return stream.ToArray();
    }

    private readonly record struct SmartArtNodeLocation(SmartArtNode? Node, SmartArtNode? Parent, int Index)
    {
        public static SmartArtNodeLocation NotFound => new(null, null, -1);
    }
}
