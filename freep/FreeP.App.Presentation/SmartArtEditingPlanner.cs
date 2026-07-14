using System.Text;
using System.Xml.Linq;
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
    Demote
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

public static class SmartArtEditingPlanner
{
    public const string DefaultNewNodeText = "New node";

    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

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

    private static DiagramPart? FindDataPart(SmartArtShape smartArt) =>
        smartArt.Parts.Values.FirstOrDefault(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase))
        ?? smartArt.Parts.Values.FirstOrDefault(part =>
            part.PartPath.Contains("/data", StringComparison.OrdinalIgnoreCase) &&
            part.PartPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

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
