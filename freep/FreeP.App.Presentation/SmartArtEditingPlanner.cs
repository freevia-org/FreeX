using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SmartArtNodeEditKind
{
    ChangeText,
    SetPicture,
    ClearPicture,
    AddSiblingAfter,
    AddChild,
    Remove,
    MoveUp,
    MoveDown,
    Promote,
    Demote,
    AddAssistant,
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
    Down,
    Delete
}

public sealed record SmartArtNodeEditIntent(
    SmartArtNodeEditKind Kind,
    string TargetModelId,
    string? Text = null,
    ImagePart? Picture = null)
{
    public static SmartArtNodeEditIntent ChangeText(string targetModelId, string text) =>
        new(SmartArtNodeEditKind.ChangeText, targetModelId, text);

    public static SmartArtNodeEditIntent SetPicture(string targetModelId, ImagePart picture) =>
        new(SmartArtNodeEditKind.SetPicture, targetModelId, Picture: picture);

    public static SmartArtNodeEditIntent ClearPicture(string targetModelId) =>
        new(SmartArtNodeEditKind.ClearPicture, targetModelId);

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

    public static SmartArtNodeEditIntent AddAssistant(string targetModelId, string? text = null) =>
        new(SmartArtNodeEditKind.AddAssistant, targetModelId, text);

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
    /// <summary>Opens the editable SmartArt outline pane for the selected graphic.</summary>
    public const string OpenTextPaneCommandId = "freep.smartart.text-pane";

    public const string DefaultNewNodeText = "New node";

    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DiagramDrawingRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

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
            (SmartArtTextPaneShortcutKey.Delete, SmartArtTextPaneShortcutModifiers.None) =>
                Route(
                    "smartart.text-pane.delete.remove",
                    key,
                    modifiers,
                    SmartArtNodeEditIntent.Remove(targetId),
                    "Delete removes the selected SmartArt text-pane row."),
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
            SmartArtNodeEditKind.SetPicture => SetPicture(data, location.Node, targetId, intent.Picture),
            SmartArtNodeEditKind.ClearPicture => ClearPicture(data, location.Node, targetId),
            SmartArtNodeEditKind.AddSiblingAfter => AddSiblingAfter(data, location, targetId, intent.Text),
            SmartArtNodeEditKind.AddChild => AddChild(data, location.Node, targetId, intent.Text),
            SmartArtNodeEditKind.Remove => Remove(data, location, targetId),
            SmartArtNodeEditKind.MoveUp => Move(data, location, targetId, offset: -1),
            SmartArtNodeEditKind.MoveDown => Move(data, location, targetId, offset: 1),
            SmartArtNodeEditKind.Promote => Promote(data, location, targetId),
            SmartArtNodeEditKind.Demote => Demote(data, location, targetId),
            SmartArtNodeEditKind.AddAssistant => AddAssistant(data, location.Node!, targetId, intent.Text),
            SmartArtNodeEditKind.ToggleAssistant => ToggleAssistant(data, location, targetId),
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

            if (stack.Count > level)
                stack.RemoveRange(level, stack.Count - level);

            if (row.IsAssistant)
            {
                if (data.Family != SmartArtFamily.Hierarchy)
                {
                    return new SmartArtTextPaneApplyResult(
                        false,
                        "Assistant nodes are supported only in hierarchy SmartArt.",
                        0,
                        BuildOutline(data));
                }

                if (level == 0)
                {
                    return new SmartArtTextPaneApplyResult(
                        false,
                        "A root SmartArt node cannot be an assistant.",
                        0,
                        BuildOutline(data));
                }

                if (stack[level - 1].Children.Any(child => !child.IsAssistant))
                {
                    return new SmartArtTextPaneApplyResult(
                        false,
                        "SmartArt assistants must remain before regular reports.",
                        0,
                        BuildOutline(data));
                }
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
        XDocument? sourceDocument = null;
        try
        {
            if (dataPart.Bytes.Length > 0)
                sourceDocument = ParseXml(dataPart.Bytes);
        }
        catch (Exception ex) when (ex is FormatException or XmlException)
        {
            // A malformed source part is replaced by the canonical generated form below.
        }

        var document = BuildDataPartDocument(
            smartArt.Data,
            nodeIds,
            ref nodeCount,
            ref connectionCount,
            sourceDocument);
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

        var drawingPart = FindDrawingPart(smartArt) ?? CreateDrawingPart(smartArt);
        if (drawingPart is null)
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "No SmartArt data part is available from which to create a drawing cache part.",
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
        if (shapes.Any(shape => shape.Kind is not (SlideShapeKind.AutoShape or SlideShapeKind.Connector or SlideShapeKind.Picture)))
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "SmartArt drawing cache regeneration does not support this shape kind.",
                drawingPart.PartPath,
                CountNodes(smartArt.Data),
                shapes.Count);
        }

        var pictureCount = shapes.Count(shape => shape.Kind == SlideShapeKind.Picture);
        var existingPictureRelationships = GetPictureRelationships(smartArt, drawingPart.PartPath);
        if ((pictureCount > 0 || existingPictureRelationships.Count > 0) &&
            !SyncPictureMediaParts(smartArt, drawingPart.PartPath))
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "SmartArt picture media relationships do not match the node picture payloads.",
                drawingPart.PartPath,
                CountNodes(smartArt.Data),
                shapes.Count);
        }

        var pictureRelIds = GetPictureRelationshipIds(smartArt, drawingPart.PartPath);
        if (pictureCount != pictureRelIds.Count ||
            shapes.Any(shape => shape.Kind == SlideShapeKind.Picture &&
                                shape.Picture?.Bytes is not { Length: > 0 }))
        {
            return new SmartArtDrawingCacheRegenerationResult(
                false,
                "SmartArt picture cache relationships do not match the planned picture nodes.",
                drawingPart.PartPath,
                CountNodes(smartArt.Data),
                shapes.Count);
        }

        // Key regenerated picture cache shapes to diagram data identity instead of
        // document order so a later node reorder cannot silently swap media payloads.
        var pictureModelIds = FlattenNodes(smartArt.Data)
            .Where(node => node.Picture?.Bytes is { Length: > 0 })
            .Select(node => node.ModelId)
            .ToArray();

        XDocument? sourceDocument = null;
        try
        {
            if (drawingPart.Bytes.Length > 0)
                sourceDocument = ParseXml(drawingPart.Bytes);
        }
        catch (Exception ex) when (ex is FormatException or XmlException)
        {
            // A malformed source cache is replaced by the canonical generated form below.
        }

        drawingPart.Bytes = SerializeXml(
            BuildDrawingCacheDocument(shapes, pictureRelIds, pictureModelIds, sourceDocument));
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

    /// <summary>
    /// Updates uniquely matched text-bearing shapes in an imported native drawing cache.
    /// This is intentionally limited to text-only data changes with unchanged node topology.
    /// Unsupported layout mutations must continue to fail rather than replacing an authored
    /// cache with a guessed live layout.
    /// </summary>
    public static SmartArtDrawingCacheRegenerationResult SynchronizePreservedDrawingText(
        SmartArtShape? smartArt,
        SmartArtData? previousData)
    {
        if (smartArt?.Data is not { } currentData || previousData is null)
            return NotAppliedDrawingCacheResult(smartArt, "No before/after SmartArt data is available.");

        var previousNodes = EnumerateNodes(previousData.Nodes).ToArray();
        var currentNodes = EnumerateNodes(currentData.Nodes).ToArray();
        if (previousNodes.Length != currentNodes.Length ||
            previousNodes.Zip(currentNodes).Any(pair =>
                !StringComparer.Ordinal.Equals(pair.First.ModelId, pair.Second.ModelId) ||
                pair.First.Level != pair.Second.Level ||
                pair.First.IsAssistant != pair.Second.IsAssistant))
        {
            return NotAppliedDrawingCacheResult(
                smartArt,
                "Preserved SmartArt cache synchronization requires unchanged node topology.");
        }

        var changedNodes = previousNodes.Zip(currentNodes)
            .Select((pair, index) => (Index: index, Pair: pair))
            .Where(item => !StringComparer.Ordinal.Equals(
                NormalizeText(item.Pair.First.Text),
                NormalizeText(item.Pair.Second.Text)))
            .Select(item => (
                Index: item.Index,
                OldText: NormalizeText(item.Pair.First.Text),
                NewText: NormalizeText(item.Pair.Second.Text)))
            .ToArray();
        if (changedNodes.Length == 0)
        {
            return NotAppliedDrawingCacheResult(
                smartArt,
                "Preserved SmartArt cache synchronization requires at least one text change.");
        }

        var drawingPart = FindDrawingPart(smartArt);
        if (drawingPart is null || drawingPart.Bytes.Length == 0)
            return NotAppliedDrawingCacheResult(smartArt, "No preserved SmartArt drawing cache is available.");

        XDocument document;
        try
        {
            document = ParseXml(drawingPart.Bytes);
        }
        catch (Exception ex) when (ex is FormatException or XmlException)
        {
            return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt drawing cache is malformed.");
        }

        var cachedBodies = document.Descendants(Dsp + "txBody").ToArray();
        var cachedFallbacks = smartArt.FallbackShapes
            .Where(shape => shape.TextBody is not null)
            .ToArray();
        var previousTexts = previousNodes.Select(node => NormalizeText(node.Text)).ToArray();
        var canUseOrdinalMapping =
            cachedBodies.Length == previousTexts.Length &&
            cachedBodies.Select(ReadDrawingText).SequenceEqual(previousTexts, StringComparer.Ordinal) &&
            cachedFallbacks.Length == previousTexts.Length &&
            cachedFallbacks.Select(shape => NormalizeText(shape.PlainText))
                .SequenceEqual(previousTexts, StringComparer.Ordinal);

        if (!canUseOrdinalMapping && changedNodes.GroupBy(change => change.OldText, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            return NotAppliedDrawingCacheResult(
                smartArt,
                "Preserved SmartArt cache synchronization cannot disambiguate duplicate source text.");
        }

        var bodyUpdates = new List<(XElement Body, string Text)>();
        var usedBodies = new HashSet<XElement>();
        foreach (var change in changedNodes)
        {
            var matchingBody = canUseOrdinalMapping
                ? new[] { cachedBodies[change.Index] }
                : cachedBodies
                    .Where(body => !usedBodies.Contains(body) &&
                                   StringComparer.Ordinal.Equals(ReadDrawingText(body), change.OldText))
                    .ToArray();
            if (matchingBody.Length != 1)
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "A changed SmartArt node does not map to one cached text shape.");
            }

            if (!CanReplaceDrawingText(matchingBody[0], change.NewText))
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "A changed SmartArt node has text that does not fit its cached paragraph structure.");
            }

            usedBodies.Add(matchingBody[0]);
            bodyUpdates.Add((matchingBody[0], change.NewText));
        }

        var fallbackUpdates = new List<(SlideShape Shape, string Text)>();
        var usedFallbacks = new HashSet<SlideShape>();
        foreach (var change in changedNodes)
        {
            var matchingFallback = canUseOrdinalMapping
                ? new[] { cachedFallbacks[change.Index] }
                : cachedFallbacks
                    .Where(shape => !usedFallbacks.Contains(shape) &&
                                    StringComparer.Ordinal.Equals(NormalizeText(shape.PlainText), change.OldText))
                    .ToArray();
            if (matchingFallback.Length != 1)
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "A changed SmartArt node does not map to one cached model shape.");
            }

            if (!CanReplaceShapeText(matchingFallback[0], change.NewText))
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "A changed SmartArt node has text that does not fit its cached paragraph structure.");
            }

            usedFallbacks.Add(matchingFallback[0]);
            fallbackUpdates.Add((matchingFallback[0], change.NewText));
        }

        foreach (var update in bodyUpdates)
            ReplaceDrawingText(update.Body, update.Text);
        foreach (var update in fallbackUpdates)
            ReplaceShapeText(update.Shape, update.Text);

        drawingPart.Bytes = SerializeXml(document);
        return new SmartArtDrawingCacheRegenerationResult(
            true,
            changedNodes.Length == 1
                ? "One text edit was applied to the preserved native SmartArt drawing cache."
                : $"{changedNodes.Length} text edits were applied to the preserved native SmartArt drawing cache.",
            drawingPart.PartPath,
            CountNodes(currentData),
            smartArt.FallbackShapes.Count);
    }

    /// <summary>
    /// Updates the media payload for existing picture nodes in an imported cached drawing.
    /// This is the package-only counterpart to live picture-cache regeneration: it never
    /// invents geometry or adds a new picture slot. Every changed node must already have a
    /// cached picture identified by the serialized <c>modelId</c>.
    /// </summary>
    public static SmartArtDrawingCacheRegenerationResult SynchronizePreservedDrawingPictures(
        SmartArtShape? smartArt,
        SmartArtData? previousData)
    {
        if (smartArt?.Data is not { } currentData || previousData is null)
            return NotAppliedDrawingCacheResult(smartArt, "No before/after SmartArt data is available.");

        var previousNodes = EnumerateNodes(previousData)
            .ToDictionary(node => node.ModelId, StringComparer.Ordinal);
        var currentNodes = EnumerateNodes(currentData)
            .ToDictionary(node => node.ModelId, StringComparer.Ordinal);
        var changedPictures = currentNodes.Values
            .Where(node => previousNodes.TryGetValue(node.ModelId, out var previousNode)
                && node.Picture?.Bytes is { Length: > 0 }
                && previousNode.Picture?.Bytes is { Length: > 0 }
                && !ImagesEqual(previousNode.Picture, node.Picture))
            .ToDictionary(node => node.ModelId, StringComparer.Ordinal);
        var removedPictures = previousNodes.Values
            .Where(node => node.Picture?.Bytes is { Length: > 0 }
                && (!currentNodes.TryGetValue(node.ModelId, out var currentNode)
                    || currentNode.Picture?.Bytes is not { Length: > 0 }))
            .ToDictionary(node => node.ModelId, StringComparer.Ordinal);
        if (changedPictures.Count == 0 && removedPictures.Count == 0)
            return NotAppliedDrawingCacheResult(
                smartArt,
                "Preserved SmartArt picture synchronization requires an existing changed or removed picture node.");

        var drawingPart = FindDrawingPart(smartArt);
        if (drawingPart is null || drawingPart.Bytes.Length == 0)
            return NotAppliedDrawingCacheResult(smartArt, "No preserved SmartArt drawing cache is available.");

        XDocument drawing;
        try
        {
            drawing = ParseXml(drawingPart.Bytes);
        }
        catch (Exception ex) when (ex is FormatException or XmlException)
        {
            return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt drawing cache is malformed.");
        }

        if (!smartArt.PartRels.TryGetValue(drawingPart.PartPath, out var relationshipBytes)
            || relationshipBytes.Length == 0)
        {
            return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt drawing has no relationship map.");
        }

        var relationships = OpcXml.TryLoadXml(relationshipBytes);
        if (relationships is null)
            return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt drawing relationships are malformed.");

        var relationshipById = relationships.Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("Id")?.Value))
            .ToDictionary(element => element.Attribute("Id")!.Value, StringComparer.Ordinal);
        // Word's schema-valid SmartArt cache uses dsp:sp with an a:blipFill for
        // picture nodes; a few producers use a dsp:pic-shaped payload instead.
        // Identify the serialized picture owner by its model id and embedded
        // relationship rather than by the local element name.
        var pictureEntries = drawing.Descendants()
            .Where(IsDrawingShapeElement)
            .Select(element =>
            {
                var modelId = (string?)element.Attribute("modelId")
                    ?? element.Descendants()
                        .FirstOrDefault(child => child.Name.LocalName == "cNvPr")
                        ?.Attribute("modelId")?.Value;
                var embed = element.Descendants()
                    .FirstOrDefault(child => child.Name.LocalName == "blip")
                    ?.Attribute(R + "embed")?.Value;
                return (Element: element, ModelId: modelId?.Trim(), Embed: embed?.Trim());
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ModelId)
                && !string.IsNullOrWhiteSpace(entry.Embed))
            .ToDictionary(entry => entry.ModelId!, StringComparer.Ordinal);
        var pictureEntriesInOrder = pictureEntries.Values.ToArray();

        var updates = new List<(SmartArtNode Node, string MediaPath)>();
        foreach (var node in changedPictures.Values)
        {
            if (!pictureEntries.TryGetValue(node.ModelId, out var entry)
                || !relationshipById.TryGetValue(entry.Embed!, out var relationship))
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "Preserved SmartArt picture synchronization requires serialized modelId and relationship identity.");
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt picture relationship has no target.");

            updates.Add((node, ResolveRelativeZipPath(GetDirectoryName(drawingPart.PartPath), target)));
        }

        var removedMediaPaths = new List<string>();
        foreach (var node in removedPictures.Values)
        {
            if (!pictureEntries.TryGetValue(node.ModelId, out var entry)
                || !relationshipById.TryGetValue(entry.Embed!, out var relationship))
            {
                return NotAppliedDrawingCacheResult(
                    smartArt,
                    "Preserved SmartArt picture removal requires serialized modelId and relationship identity.");
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                return NotAppliedDrawingCacheResult(smartArt, "The preserved SmartArt picture relationship has no target.");

            entry.Element.Remove();
            relationship.Remove();
            removedMediaPaths.Add(ResolveRelativeZipPath(GetDirectoryName(drawingPart.PartPath), target));
        }

        foreach (var (node, mediaPath) in updates)
        {
            smartArt.Parts[mediaPath] = new DiagramPart
            {
                PartPath = mediaPath,
                ContentType = node.Picture!.ContentType,
                Bytes = node.Picture.Bytes.ToArray(),
            };
        }

        foreach (var mediaPath in removedMediaPaths)
        {
            if (!relationships.Descendants().Any(element =>
                    element.Name.LocalName == "Relationship"
                    && string.Equals(
                        ResolveRelativeZipPath(GetDirectoryName(drawingPart.PartPath), element.Attribute("Target")?.Value ?? string.Empty),
                        mediaPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                smartArt.Parts.Remove(mediaPath);
            }
        }

        drawingPart.Bytes = SerializeXml(drawing);
        smartArt.PartRels[drawingPart.PartPath] = SerializeXml(relationships);

        var fallbackPictures = EnumerateShapes(smartArt.FallbackShapes)
            .Where(shape => shape.Kind == SlideShapeKind.Picture)
            .ToArray();
        var pictureIndex = 0;
        foreach (var entry in pictureEntriesInOrder)
        {
            if (pictureIndex >= fallbackPictures.Length)
                break;
            if (changedPictures.TryGetValue(entry.ModelId!, out var node))
            {
                fallbackPictures[pictureIndex].Picture = new ImagePart
                {
                    Bytes = node.Picture!.Bytes.ToArray(),
                    ContentType = node.Picture.ContentType,
                };
            }
            pictureIndex++;
        }

        var removedOrdinals = pictureEntriesInOrder
            .Select((entry, index) => (entry, index))
            .Where(item => removedPictures.ContainsKey(item.entry.ModelId!))
            .Select(item => item.index)
            .OrderByDescending(index => index)
            .ToArray();
        foreach (var ordinal in removedOrdinals)
        {
            var currentOrdinal = 0;
            RemovePictureAtOrdinal(smartArt.FallbackShapes, ordinal, ref currentOrdinal);
        }

        return new SmartArtDrawingCacheRegenerationResult(
            true,
            $"{updates.Count} SmartArt cached picture payload(s) synchronized and {removedPictures.Count} removed without rebuilding layout.",
            drawingPart.PartPath,
            currentNodes.Count,
            smartArt.FallbackShapes.Count);
    }

    private static bool ImagesEqual(ImagePart left, ImagePart right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left.ContentType, right.ContentType)
        && left.Bytes.AsSpan().SequenceEqual(right.Bytes);

    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateShapes(shape.Children))
                yield return child;
        }
    }

    private static bool RemovePictureAtOrdinal(
        IList<SlideShape> shapes,
        int targetOrdinal,
        ref int currentOrdinal)
    {
        for (var index = 0; index < shapes.Count; index++)
        {
            var shape = shapes[index];
            if (shape.Kind == SlideShapeKind.Picture)
            {
                if (currentOrdinal == targetOrdinal)
                {
                    shapes.RemoveAt(index);
                    return true;
                }

                currentOrdinal++;
            }

            if (RemovePictureAtOrdinal(shape.Children, targetOrdinal, ref currentOrdinal))
                return true;
        }

        return false;
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

    private static SmartArtNodeEditResult SetPicture(
        SmartArtData data,
        SmartArtNode target,
        string targetId,
        ImagePart? picture)
    {
        if (picture?.Bytes is not { Length: > 0 })
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.SetPicture,
                targetId,
                "A non-empty picture payload is required.",
                BuildOutline(data));
        }

        target.Picture = new ImagePart
        {
            Bytes = picture.Bytes.ToArray(),
            ContentType = picture.ContentType,
        };
        return Applied(
            data,
            SmartArtNodeEditKind.SetPicture,
            targetId,
            target.ModelId,
            "SmartArt node picture updated.");
    }

    private static SmartArtNodeEditResult ClearPicture(
        SmartArtData data,
        SmartArtNode target,
        string targetId)
    {
        if (target.Picture is null)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ClearPicture,
                targetId,
                "The selected SmartArt node has no picture to remove.",
                BuildOutline(data));
        }

        target.Picture = null;
        return Applied(
            data,
            SmartArtNodeEditKind.ClearPicture,
            targetId,
            target.ModelId,
            "SmartArt node picture removed.");
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
        if (data.Family == SmartArtFamily.Hierarchy
            && AssistantReorderingWouldCrossReportBoundary(siblings, location.Index, destination))
        {
            return SmartArtNodeEditResult.NotApplied(
                offset < 0 ? SmartArtNodeEditKind.MoveUp : SmartArtNodeEditKind.MoveDown,
                targetId,
                "SmartArt assistants must remain before regular reports.",
                BuildOutline(data));
        }

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

    private static bool AssistantReorderingWouldCrossReportBoundary(
        IReadOnlyList<SmartArtNode> siblings,
        int sourceIndex,
        int destinationIndex)
    {
        var firstReportIndex = -1;
        for (var index = 0; index < siblings.Count; index++)
        {
            if (!siblings[index].IsAssistant)
            {
                firstReportIndex = index;
                break;
            }
        }

        // Preserve the existing generic reorder behavior for malformed/imported
        // sibling lists. The guard applies to the normal assistant-prefix shape.
        if (firstReportIndex < 0
            || siblings.Skip(firstReportIndex).Any(node => node.IsAssistant))
            return false;

        var movingAssistant = siblings[sourceIndex].IsAssistant;
        return movingAssistant
            ? destinationIndex >= firstReportIndex
            : destinationIndex < firstReportIndex;
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
        if (node.IsAssistant && parentLocation.Parent is null)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.Promote,
                targetId,
                "An assistant node cannot be promoted to the root.",
                BuildOutline(data));
        }

        currentSiblings.RemoveAt(location.Index);

        var promotedSiblings = parentLocation.Parent is null
            ? data.Nodes
            : parentLocation.Parent.Children;
        var insertAt = node.IsAssistant
            ? promotedSiblings.TakeWhile(sibling => sibling.IsAssistant).Count()
            : Math.Clamp(parentLocation.Index + 1, 0, promotedSiblings.Count);
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
        var insertAt = node.IsAssistant
            ? newParent.Children.TakeWhile(child => child.IsAssistant).Count()
            : newParent.Children.Count;
        newParent.Children.Insert(insertAt, node);
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
        SmartArtNodeLocation location,
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

        var target = location.Node!;
        if (target.Level == 0)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ToggleAssistant,
                targetId,
                "A root SmartArt node cannot be an assistant.",
                BuildOutline(data));
        }

        var siblings = location.Parent is null ? data.Nodes : location.Parent.Children;
        var wasAssistant = target.IsAssistant;
        siblings.RemoveAt(location.Index);
        target.IsAssistant = !wasAssistant;

        // PowerPoint keeps assistants in a leading block before ordinary reports.
        // Reordering here keeps the text-pane outline valid after a direct toggle,
        // instead of creating a state that the outline importer would reject.
        var insertAt = siblings.TakeWhile(node => node.IsAssistant).Count();
        siblings.Insert(insertAt, target);
        NormalizeLevels(data);

        return Applied(
            data,
            SmartArtNodeEditKind.ToggleAssistant,
            targetId,
            target.ModelId,
            target.IsAssistant
                ? "SmartArt node marked as an assistant."
                : "SmartArt assistant designation removed and report moved after assistants.");
    }

    private static SmartArtNodeEditResult AddAssistant(
        SmartArtData data,
        SmartArtNode target,
        string targetId,
        string? text)
    {
        if (data.Family != SmartArtFamily.Hierarchy)
        {
            return SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.AddAssistant,
                targetId,
                "Assistant nodes are supported only in hierarchy SmartArt.",
                BuildOutline(data));
        }

        var assistant = CreateNode(data, text ?? "Assistant", target.Level + 1, isAssistant: true);
        var insertAt = target.Children.TakeWhile(child => child.IsAssistant).Count();
        target.Children.Insert(insertAt, assistant);
        NormalizeLevels(data);

        return Applied(
            data,
            SmartArtNodeEditKind.AddAssistant,
            targetId,
            assistant.ModelId,
            "SmartArt assistant node added.");
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

    private static IEnumerable<SmartArtNode> EnumerateNodes(IEnumerable<SmartArtNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Children))
                yield return child;
        }
    }

    private static string ReadDrawingText(XElement body) =>
        string.Join("\n", body.Elements(A + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(A + "t").Select(text => text.Value))));

    private static bool ReplaceDrawingText(XElement body, string text)
    {
        var paragraphs = body.Elements(A + "p").ToArray();
        var lines = text.Split('\n');
        if (paragraphs.Length != lines.Length)
            return false;

        for (var index = 0; index < paragraphs.Length; index++)
        {
            var textNodes = paragraphs[index].Descendants(A + "t").ToArray();
            if (textNodes.Length == 0)
                return false;

            textNodes[0].Value = lines[index];
            foreach (var textNode in textNodes.Skip(1))
                textNode.Value = string.Empty;
        }

        return true;
    }

    private static bool CanReplaceDrawingText(XElement body, string text)
    {
        var paragraphs = body.Elements(A + "p").ToArray();
        var lines = text.Split('\n');
        return paragraphs.Length == lines.Length &&
               paragraphs.All(paragraph => paragraph.Descendants(A + "t").Any());
    }

    private static bool ReplaceShapeText(SlideShape shape, string text)
    {
        if (shape.TextBody is not { } body)
            return false;

        var paragraphs = body.Paragraphs;
        var lines = text.Split('\n');
        if (paragraphs.Count != lines.Length)
            return false;

        for (var index = 0; index < paragraphs.Count; index++)
        {
            var runs = paragraphs[index].Runs;
            if (runs.Count == 0)
                runs.Add(new Run());

            runs[0].Text = lines[index];
            for (var runIndex = 1; runIndex < runs.Count; runIndex++)
                runs[runIndex].Text = string.Empty;
        }

        return true;
    }

    private static bool CanReplaceShapeText(SlideShape shape, string text) =>
        shape.TextBody is { } body && body.Paragraphs.Count == text.Split('\n').Length;

    private static SmartArtDrawingCacheRegenerationResult NotAppliedDrawingCacheResult(
        SmartArtShape? smartArt,
        string message) =>
        new(
            false,
            message,
            smartArt is null ? null : FindDrawingPart(smartArt)?.PartPath,
            smartArt?.Data is null ? 0 : CountNodes(smartArt.Data),
            smartArt?.FallbackShapes.Count ?? 0);

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

    private static DiagramPart? CreateDrawingPart(SmartArtShape smartArt)
    {
        var dataPart = FindDataPart(smartArt);
        if (dataPart is null || string.IsNullOrWhiteSpace(dataPart.PartPath))
            return null;

        var dataRelationships = LoadOrCreateRelationships(smartArt, dataPart.PartPath);
        var drawingRelationship = dataRelationships.Root?
            .Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Relationship", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Type")?.Value, DiagramDrawingRelationshipType, StringComparison.Ordinal));

        var drawingPath = string.Empty;
        if (drawingRelationship?.Attribute("Target")?.Value is { Length: > 0 } target)
            drawingPath = ResolveRelativeZipPath(GetDirectoryName(dataPart.PartPath), target);

        if (string.IsNullOrWhiteSpace(drawingPath))
        {
            var dataFileName = dataPart.PartPath[(dataPart.PartPath.LastIndexOf('/') + 1)..];
            var drawingFileName = dataFileName.StartsWith("data", StringComparison.OrdinalIgnoreCase)
                ? "drawing" + dataFileName[4..]
                : "drawing-freep.xml";
            drawingPath = GetDirectoryName(dataPart.PartPath) + "/" + drawingFileName;
            var suffix = 2;
            while (smartArt.Parts.ContainsKey(drawingPath))
                drawingPath = GetDirectoryName(dataPart.PartPath) + $"/drawing-freep-{suffix++}.xml";

            var usedRelationshipIds = dataRelationships.Root?
                .Elements()
                .Select(element => element.Attribute("Id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var relationshipId = "rIdFreePDrawing";
            var relationshipSuffix = 1;
            while (!usedRelationshipIds.Add(relationshipId))
                relationshipId = $"rIdFreePDrawing{relationshipSuffix++}";

            dataRelationships.Root!.Add(new XElement(
                PackageRelationships + "Relationship",
                new XAttribute("Id", relationshipId),
                new XAttribute("Type", DiagramDrawingRelationshipType),
                new XAttribute("Target", MakeRelativeZipPath(
                    GetDirectoryName(dataPart.PartPath), drawingPath))));
            smartArt.PartRels[dataPart.PartPath] = SerializeXml(dataRelationships);
        }

        if (!smartArt.Parts.TryGetValue(drawingPath, out var drawingPart))
        {
            drawingPart = new DiagramPart
            {
                ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
                PartPath = drawingPath,
                Bytes = Array.Empty<byte>(),
            };
            smartArt.Parts[drawingPath] = drawingPart;
        }

        smartArt.DrawingPartPath = drawingPath;
        if (!smartArt.PartRels.ContainsKey(drawingPath))
            smartArt.PartRels[drawingPath] = SerializeXml(CreateEmptyRelationshipsDocument());

        return drawingPart;
    }

    private static XDocument LoadOrCreateRelationships(SmartArtShape smartArt, string partPath)
    {
        if (smartArt.PartRels.TryGetValue(partPath, out var bytes) && bytes.Length > 0)
        {
            try
            {
                var document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.PreserveWhitespace);
                if (document.Root?.Name.LocalName == "Relationships")
                    return document;
            }
            catch (XmlException)
            {
                // Rebuild malformed relationship metadata from the authoritative part model.
            }
        }

        return CreateEmptyRelationshipsDocument();
    }

    private static XDocument CreateEmptyRelationshipsDocument() =>
        new(new XElement(PackageRelationships + "Relationships"));

    private static XDocument BuildDrawingCacheDocument(
        IReadOnlyList<SlideShape> shapes,
        IReadOnlyList<string> pictureRelIds,
        IReadOnlyList<string> pictureModelIds,
        XDocument? sourceDocument = null)
    {
        var shapeElements = new List<XElement>();
        var pictureIndex = 0;
        foreach (var shape in shapes)
        {
            if (shape.Kind == SlideShapeKind.Picture)
            {
                shapeElements.Add(BuildDrawingCachePicture(
                    shape,
                    pictureRelIds[pictureIndex],
                    pictureIndex < pictureModelIds.Count ? pictureModelIds[pictureIndex] : null));
                pictureIndex++;
            }
            else
                shapeElements.Add(BuildDrawingCacheShape(shape));
        }

        var generated = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(Dsp + "spTree",
                    new XElement(Dsp + "nvGrpSpPr",
                        new XElement(Dsp + "cNvPr",
                            new XAttribute("id", "1"),
                            new XAttribute("name", "SmartArt Cache")),
                        new XElement(Dsp + "cNvGrpSpPr")),
                    new XElement(Dsp + "grpSpPr",
                        new XElement(A + "xfrm",
                            new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(A + "ext", new XAttribute("cx", "1"), new XAttribute("cy", "1")),
                            new XElement(A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(A + "chExt", new XAttribute("cx", "1"), new XAttribute("cy", "1")))),
                    shapeElements)));

        if (sourceDocument?.Root is not { } sourceRoot || sourceRoot.Name != Dsp + "drawing")
            return generated;

        var generatedRoot = generated.Root!;
        var generatedSpTree = generatedRoot.Element(Dsp + "spTree")!;
        var sourceSpTree = sourceRoot.Element(Dsp + "spTree");
        if (sourceSpTree is null)
        {
            sourceRoot.Add(new XElement(generatedSpTree));
            return sourceDocument;
        }

        var generatedEnvelope = generatedSpTree.Elements()
            .Where(element => !IsDrawingShapeElement(element))
            .Select(element => new XElement(element))
            .ToArray();
        if (sourceSpTree.Element(Dsp + "nvGrpSpPr") is null)
            sourceSpTree.AddFirst(new XElement(generatedEnvelope.First(element => element.Name == Dsp + "nvGrpSpPr")));
        if (sourceSpTree.Element(Dsp + "grpSpPr") is null)
        {
            var groupProperties = new XElement(generatedEnvelope.First(element => element.Name == Dsp + "grpSpPr"));
            var firstShape = sourceSpTree.Elements().FirstOrDefault(IsDrawingShapeElement);
            if (firstShape is null)
                sourceSpTree.Add(groupProperties);
            else
                firstShape.AddBeforeSelf(groupProperties);
        }

        var generatedShapes = generatedSpTree.Elements()
            .Where(IsDrawingShapeElement)
            .Select(element => new XElement(element))
            .ToArray();
        PreserveAuthoredDrawingVisuals(generatedShapes, sourceSpTree);

        foreach (var staleShape in sourceSpTree.Elements().Where(IsDrawingShapeElement).ToList())
            staleShape.Remove();

        var extensionList = sourceSpTree.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "extLst");
        if (extensionList is null)
            sourceSpTree.Add(generatedShapes);
        else
            extensionList.AddBeforeSelf(generatedShapes);

        return sourceDocument;
    }

    private static void PreserveAuthoredDrawingVisuals(
        IReadOnlyList<XElement> generatedShapes,
        XElement sourceSpTree)
    {
        var sourceByModelId = sourceSpTree
            .Descendants()
            .Where(IsDrawingShapeElement)
            .Select(element => (ModelId: element.Attribute("modelId")?.Value?.Trim(), Element: element))
            .Where(item => !string.IsNullOrWhiteSpace(item.ModelId))
            .GroupBy(item => item.ModelId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Element, StringComparer.Ordinal);

        foreach (var generatedShape in generatedShapes)
        {
            var modelId = generatedShape.Attribute("modelId")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(modelId) ||
                !sourceByModelId.TryGetValue(modelId, out var sourceShape))
            {
                continue;
            }

            var sourceProperties = sourceShape.Element(Dsp + "spPr");
            var generatedProperties = generatedShape.Element(Dsp + "spPr");
            if (sourceProperties is not null && generatedProperties is not null)
            {
                foreach (var name in new[] { A + "effectLst", A + "scene3d", A + "sp3d", A + "extLst" })
                {
                    var authoredPayload = sourceProperties.Element(name);
                    if (authoredPayload is null)
                        continue;

                    generatedProperties.Element(name)?.Remove();
                    generatedProperties.Add(new XElement(authoredPayload));
                }
            }

            PreserveAuthoredTextFormatting(generatedShape, sourceShape);
        }
    }

    private static void PreserveAuthoredTextFormatting(
        XElement generatedShape,
        XElement sourceShape)
    {
        var sourceBody = sourceShape.Element(Dsp + "txBody");
        var generatedBody = generatedShape.Element(Dsp + "txBody");
        if (sourceBody is null || generatedBody is null)
            return;

        foreach (var name in new[] { A + "bodyPr", A + "lstStyle" })
        {
            var authored = sourceBody.Element(name);
            if (authored is null)
                continue;

            var generated = generatedBody.Element(name);
            if (generated is not null)
                generated.ReplaceWith(new XElement(authored));
            else
                generatedBody.AddFirst(new XElement(authored));
        }

        var sourceParagraphs = sourceBody.Elements(A + "p").ToArray();
        var generatedParagraphs = generatedBody.Elements(A + "p").ToArray();
        for (var paragraphIndex = 0;
             paragraphIndex < Math.Min(sourceParagraphs.Length, generatedParagraphs.Length);
             paragraphIndex++)
        {
            var sourceParagraph = sourceParagraphs[paragraphIndex];
            var generatedParagraph = generatedParagraphs[paragraphIndex];
            CopyTextChild(sourceParagraph, generatedParagraph, A + "pPr", insertAtStart: true);
            CopyTextChild(sourceParagraph, generatedParagraph, A + "endParaRPr", insertAtStart: false);

            var sourceRuns = sourceParagraph.Elements(A + "r").ToArray();
            var generatedRuns = generatedParagraph.Elements(A + "r").ToArray();
            for (var runIndex = 0;
                 runIndex < Math.Min(sourceRuns.Length, generatedRuns.Length);
                 runIndex++)
            {
                CopyTextChild(sourceRuns[runIndex], generatedRuns[runIndex], A + "rPr", insertAtStart: true);
            }
        }
    }

    private static void CopyTextChild(
        XElement source,
        XElement generated,
        XName name,
        bool insertAtStart)
    {
        var authored = source.Element(name);
        if (authored is null)
            return;

        var existing = generated.Element(name);
        if (existing is not null)
        {
            existing.ReplaceWith(new XElement(authored));
            return;
        }

        if (insertAtStart)
            generated.AddFirst(new XElement(authored));
        else
            generated.Add(new XElement(authored));
    }

    private static bool IsDrawingShapeElement(XElement element) =>
        element.Name == Dsp + "sp" ||
        element.Name == Dsp + "pic" ||
        element.Name == Dsp + "cxnSp" ||
        element.Name == Dsp + "grpSp" ||
        element.Name == Dsp + "graphicFrame";

    private static XElement BuildDrawingCacheShape(SlideShape shape)
    {
        var id = shape.Id == 0 ? 1u : shape.Id;
        return new XElement(Dsp + "sp",
            new XAttribute("modelId", id),
            new XElement(Dsp + "nvSpPr",
                new XElement(Dsp + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", string.IsNullOrWhiteSpace(shape.Name) ? $"SmartArt Cache {id}" : shape.Name)),
                new XElement(Dsp + "cNvSpPr")),
            BuildShapeProperties(shape),
            BuildTextBody(shape.TextBody));
    }

    private static XElement BuildDrawingCachePicture(
        SlideShape shape,
        string relationshipId,
        string? modelId)
    {
        var id = shape.Id == 0 ? 1u : shape.Id;
        // The diagram drawing schema does not allow dsp:pic directly under
        // dsp:spTree.  SmartArt picture caches use a regular dsp:sp whose
        // shape properties carry the image fill, while the relationship and
        // geometry remain the same.
        return new XElement(Dsp + "sp",
            new XAttribute("modelId", string.IsNullOrWhiteSpace(modelId) ? id : modelId),
            new XElement(Dsp + "nvSpPr",
                new XElement(Dsp + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", string.IsNullOrWhiteSpace(shape.Name) ? $"SmartArt Picture {id}" : shape.Name)),
                new XElement(Dsp + "cNvSpPr")),
            new XElement(Dsp + "spPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off",
                    new XAttribute("x", shape.OffsetXEmu),
                    new XAttribute("y", shape.OffsetYEmu)),
                    new XElement(A + "ext",
                        new XAttribute("cx", shape.ExtentCxEmu),
                        new XAttribute("cy", shape.ExtentCyEmu))),
                new XElement(A + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(A + "avLst")),
                new XElement(A + "blipFill",
                    new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)),
                    new XElement(A + "stretch", new XElement(A + "fillRect")))));
    }

    private static IReadOnlyList<string> GetPictureRelationshipIds(
        SmartArtShape smartArt,
        string drawingPartPath)
        => GetPictureRelationships(smartArt, drawingPartPath)
            .Select(relationship => relationship.Id)
            .ToArray();

    private static IReadOnlyList<(string Id, string Target)> GetPictureRelationships(
        SmartArtShape smartArt,
        string drawingPartPath)
    {
        if (!smartArt.PartRels.TryGetValue(drawingPartPath, out var relationshipBytes) ||
            relationshipBytes.Length == 0)
        {
            return Array.Empty<(string Id, string Target)>();
        }

        try
        {
            var document = OpcXml.TryLoadXml(relationshipBytes);
            if (document is null)
                return Array.Empty<(string Id, string Target)>();

            return document.Descendants()
                .Where(element =>
                    element.Name.LocalName == "Relationship" &&
                    element.Attribute("Type")?.Value.EndsWith("/image", StringComparison.OrdinalIgnoreCase) == true)
                .Select(element => (
                    Id: element.Attribute("Id")?.Value,
                    Target: element.Attribute("Target")?.Value))
                .Where(relationship =>
                    !string.IsNullOrWhiteSpace(relationship.Id) &&
                    !string.IsNullOrWhiteSpace(relationship.Target))
                .Select(relationship => (relationship.Id!, relationship.Target!))
                .ToArray();
        }
        catch (XmlException)
        {
            return Array.Empty<(string Id, string Target)>();
        }
    }

    private static bool SyncPictureMediaParts(SmartArtShape smartArt, string drawingPartPath)
    {
        if (smartArt.Data is null)
            return false;

        var nodes = FlattenNodes(smartArt.Data);
        var pictureNodes = nodes
            .Where(node => node.Picture?.Bytes is { Length: > 0 })
            .ToList();
        var document = smartArt.PartRels.TryGetValue(drawingPartPath, out var relationshipBytes) &&
                       relationshipBytes.Length > 0
            ? OpcXml.TryLoadXml(relationshipBytes)
            : CreateEmptyRelationshipsDocument();
        if (document is null)
            return false;
        var relationshipElements = document.Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .ToList();
        var imageElements = relationshipElements
            .Where(element => element.Attribute("Type")?.Value.EndsWith("/image", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        var usedIds = relationshipElements
            .Select(element => element.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var oldMediaPaths = imageElements
            .Select(element => element.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => ResolveRelativeZipPath(GetDirectoryName(drawingPartPath), target!))
            .ToArray();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacements = new List<XElement>();

        for (var index = 0; index < pictureNodes.Count; index++)
        {
            var picture = pictureNodes[index].Picture!;
            var relationshipId = index < imageElements.Count
                ? imageElements[index].Attribute("Id")!.Value
                : AllocatePictureRelationshipId(usedIds, index + 1);

            var mediaPath = index < imageElements.Count
                ? ResolveRelativeZipPath(
                    GetDirectoryName(drawingPartPath),
                    imageElements[index].Attribute("Target")?.Value ?? string.Empty)
                : AllocatePictureMediaPath(smartArt, picture.ContentType, index + 1);
            if (!usedTargets.Add(mediaPath))
                mediaPath = AllocatePictureMediaPath(smartArt, picture.ContentType, index + 1);

            var existing = index < imageElements.Count
                ? new XElement(imageElements[index])
                : new XElement(
                    PackageRelationships + "Relationship",
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"));
            existing.SetAttributeValue("Id", relationshipId);
            existing.SetAttributeValue(
                "Target",
                MakeRelativeZipPath(GetDirectoryName(drawingPartPath), mediaPath));
            replacements.Add(existing);

            smartArt.Parts[mediaPath] = new DiagramPart
            {
                PartPath = mediaPath,
                ContentType = picture.ContentType,
                Bytes = picture.Bytes.ToArray(),
            };
        }

        foreach (var imageElement in imageElements)
            imageElement.Remove();
        var lastRelationship = relationshipElements
            .Where(element => !imageElements.Contains(element))
            .LastOrDefault();
        foreach (var replacement in replacements)
        {
            if (lastRelationship is null)
                document.Root?.Add(replacement);
            else
                lastRelationship.AddAfterSelf(replacement);
            lastRelationship = replacement;
        }
        smartArt.PartRels[drawingPartPath] = SerializeXml(document);

        foreach (var oldMediaPath in oldMediaPaths)
        {
            if (!replacements.Any(replacement =>
                    string.Equals(
                        ResolveRelativeZipPath(
                            GetDirectoryName(drawingPartPath),
                            replacement.Attribute("Target")?.Value ?? string.Empty),
                        oldMediaPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                smartArt.Parts.Remove(oldMediaPath);
            }
        }

        return true;
    }

    private static string AllocatePictureRelationshipId(HashSet<string> usedIds, int ordinal)
    {
        var candidate = $"rIdFreePSmartArtPic{ordinal}";
        var suffix = 1;
        while (!usedIds.Add(candidate))
            candidate = $"rIdFreePSmartArtPic{ordinal}_{suffix++}";
        return candidate;
    }

    private static string AllocatePictureMediaPath(
        SmartArtShape smartArt,
        string contentType,
        int ordinal)
    {
        var extension = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/svg+xml" => "svg",
            "image/bmp" => "bmp",
            _ => "png",
        };
        var candidate = $"ppt/media/freep-smartart-picture{ordinal}.{extension}";
        var suffix = 1;
        while (smartArt.Parts.ContainsKey(candidate))
            candidate = $"ppt/media/freep-smartart-picture{ordinal}_{suffix++}.{extension}";
        return candidate;
    }

    private static string MakeRelativeZipPath(string baseDirectory, string absolutePath)
    {
        var baseParts = baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var targetParts = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < baseParts.Length && common < targetParts.Length &&
               string.Equals(baseParts[common], targetParts[common], StringComparison.OrdinalIgnoreCase))
            common++;

        var segments = Enumerable.Repeat("..", baseParts.Length - common)
            .Concat(targetParts.Skip(common));
        return string.Join('/', segments);
    }

    private static List<SmartArtNode> FlattenNodes(SmartArtData data)
    {
        var nodes = new List<SmartArtNode>();
        foreach (var root in data.Nodes)
            Collect(root);
        return nodes;

        void Collect(SmartArtNode node)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
                Collect(child);
        }
    }

    private static string ResolveRelativeZipPath(string baseDirectory, string target)
    {
        var segments = new List<string>();
        foreach (var segment in (baseDirectory + "/" + target).Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string GetDirectoryName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? string.Empty : path[..slash];
    }

    private static XElement BuildShapeProperties(SlideShape shape)
    {
        var transform = new XElement(A + "xfrm",
            new XElement(A + "off",
                new XAttribute("x", shape.OffsetXEmu),
                new XAttribute("y", shape.OffsetYEmu)),
            new XElement(A + "ext",
                new XAttribute("cx", shape.ExtentCxEmu),
                new XAttribute("cy", shape.ExtentCyEmu)));
        if (shape.FlipH)
            transform.SetAttributeValue("flipH", "1");
        if (shape.FlipV)
            transform.SetAttributeValue("flipV", "1");

        var spPr = new XElement(Dsp + "spPr",
            transform,
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
                        new XAttribute("val", ToHex(outline.Color.Resolved)))),
                shape.Kind == SlideShapeKind.Connector || shape.AutoShapeKind == DrawingShapeKind.Line
                    ? BuildLineEndElement(A + "headEnd", outline.EndLineEnd)
                    : null,
                shape.Kind == SlideShapeKind.Connector || shape.AutoShapeKind == DrawingShapeKind.Line
                    ? BuildLineEndElement(A + "tailEnd", outline.BeginLineEnd)
                    : null));
        }
        else if (shape.Outline is ShapeOutline.None)
        {
            spPr.Add(new XElement(A + "ln", new XElement(A + "noFill")));
        }

        return spPr;
    }

    private static XElement? BuildLineEndElement(XName name, ShapeLineEnd? lineEnd) =>
        lineEnd is null
            ? null
            : new XElement(name, new XAttribute("type", lineEnd.Kind == ShapeLineEndKind.Triangle ? "triangle" : "none"));

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
            DrawingShapeKind.Chevron => "chevron",
            DrawingShapeKind.Triangle => "triangle",
            DrawingShapeKind.Diamond => "diamond",
            DrawingShapeKind.Trapezoid => "trapezoid",
            DrawingShapeKind.Chord => "chord",
            DrawingShapeKind.Ellipse => "ellipse",
            _ => "rect"
        };

    private static string ToHex(SrgbColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static XDocument BuildDataPartDocument(
        SmartArtData data,
        Dictionary<SmartArtNode, string> nodeIds,
        ref int nodeCount,
        ref int connectionCount,
        XDocument? sourceDocument = null)
    {
        var points = new List<XElement>();
        var connections = new List<XElement>();
        var generatedIdIndex = 1;

        var authoredPoints = sourceDocument?.Root?.Element(Dgm + "ptLst")?.Elements(Dgm + "pt")
            .Select(point => (Id: point.Attribute("modelId")?.Value?.Trim(), Point: point))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Point, StringComparer.Ordinal);
        var authoredConnections = sourceDocument?.Root?.Element(Dgm + "cxnLst")?.Elements(Dgm + "cxn")
            .Select(connection =>
            {
                var type = connection.Attribute("type")?.Value?.Trim() ?? "parOf";
                var sourceId = connection.Attribute("srcId")?.Value?.Trim();
                var destinationId = connection.Attribute("destId")?.Value?.Trim();
                return (Key: sourceId is null || destinationId is null
                    ? null
                    : BuildConnectionKey(type, sourceId, destinationId), Connection: connection);
            })
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Connection, StringComparer.Ordinal);
        var reservedConnectionIds = authoredConnections?.Values
            .Select(connection => connection.Attribute("modelId")?.Value?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var emittedConnectionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in data.Nodes)
            CollectDataPartElements(root, null, 0, points, connections, nodeIds,
                ref generatedIdIndex, ref nodeCount, ref connectionCount,
                authoredPoints, authoredConnections, reservedConnectionIds, emittedConnectionIds);

        PreserveNonTreeConnections(
            connections,
            authoredConnections,
            points.Select(point => point.Attribute("modelId")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal),
            emittedConnectionIds,
            ref connectionCount);

        if (sourceDocument?.Root is { } sourceRoot && sourceRoot.Name == Dgm + "dataModel")
        {
            var pointList = sourceRoot.Element(Dgm + "ptLst");
            if (pointList is null)
                sourceRoot.Add(new XElement(Dgm + "ptLst", points));
            else
                pointList.ReplaceNodes(points);

            var connectionList = sourceRoot.Element(Dgm + "cxnLst");
            if (connectionList is null)
                sourceRoot.Add(new XElement(Dgm + "cxnLst", connections));
            else
                connectionList.ReplaceNodes(connections);

            return sourceDocument;
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dgm + "ptLst", points),
                new XElement(Dgm + "cxnLst", connections)));
    }

    private static void PreserveNonTreeConnections(
        List<XElement> connections,
        IReadOnlyDictionary<string, XElement>? authoredConnections,
        ISet<string> livePointIds,
        ISet<string> emittedConnectionIds,
        ref int connectionCount)
    {
        if (authoredConnections is null)
            return;

        foreach (var authoredConnection in authoredConnections.Values)
        {
            var type = authoredConnection.Attribute("type")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(type)
                || string.Equals(type, "parOf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceId = authoredConnection.Attribute("srcId")?.Value?.Trim();
            var destinationId = authoredConnection.Attribute("destId")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(sourceId)
                || string.IsNullOrWhiteSpace(destinationId)
                || !livePointIds.Contains(sourceId)
                || !livePointIds.Contains(destinationId))
            {
                continue;
            }

            var connectionId = authoredConnection.Attribute("modelId")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(connectionId)
                || !emittedConnectionIds.Add(connectionId))
            {
                continue;
            }

            connections.Add(new XElement(authoredConnection));
            connectionCount++;
        }
    }

    private static void CollectDataPartElements(
        SmartArtNode node,
        SmartArtNode? parent,
        int sourceOrder,
        List<XElement> points,
        List<XElement> connections,
        Dictionary<SmartArtNode, string> nodeIds,
        ref int generatedIdIndex,
        ref int nodeCount,
        ref int connectionCount,
        IReadOnlyDictionary<string, XElement>? authoredPoints = null,
        IReadOnlyDictionary<string, XElement>? authoredConnections = null,
        ISet<string>? reservedConnectionIds = null,
        ISet<string>? emittedConnectionIds = null)
    {
        var id = GetNodeId(node, nodeIds, ref generatedIdIndex);
        XElement? authoredPoint = null;
        authoredPoints?.TryGetValue(id, out authoredPoint);
        points.Add(BuildPointElement(node, id, authoredPoint));
        nodeCount++;

        if (parent is not null)
        {
            var parentId = GetNodeId(parent, nodeIds, ref generatedIdIndex);
            var connectionKey = BuildConnectionKey("parOf", parentId, id);
            XElement? authoredConnection = null;
            authoredConnections?.TryGetValue(connectionKey, out authoredConnection);
            var connection = authoredConnection is null
                ? new XElement(Dgm + "cxn")
                : new XElement(authoredConnection);
            var connectionId = authoredConnection?.Attribute("modelId")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(connectionId) ||
                emittedConnectionIds is null || !emittedConnectionIds.Add(connectionId))
            {
                var generatedConnectionId = connectionCount + 1;
                do
                {
                    connectionId = generatedConnectionId.ToString(CultureInfo.InvariantCulture);
                    generatedConnectionId++;
                }
                while (reservedConnectionIds?.Contains(connectionId) == true ||
                       emittedConnectionIds?.Contains(connectionId) == true);

                emittedConnectionIds?.Add(connectionId);
            }

            connection.SetAttributeValue("modelId", connectionId);
            connection.SetAttributeValue("type", "parOf");
            connection.SetAttributeValue("srcId", parentId);
            connection.SetAttributeValue("destId", id);
            connection.SetAttributeValue("srcOrd", sourceOrder);
            connection.SetAttributeValue("destOrd", 0);
            connections.Add(connection);
            connectionCount++;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            CollectDataPartElements(node.Children[index], node, index, points, connections, nodeIds,
                ref generatedIdIndex, ref nodeCount, ref connectionCount,
                authoredPoints, authoredConnections, reservedConnectionIds, emittedConnectionIds);
        }
    }

    private static string BuildConnectionKey(string type, string sourceId, string destinationId) =>
        $"{type}\u001F{sourceId}\u001F{destinationId}";

    private static XElement BuildPointElement(SmartArtNode node, string id, XElement? authoredPoint)
    {
        var point = authoredPoint is null
            ? new XElement(Dgm + "pt")
            : new XElement(authoredPoint);

        point.SetAttributeValue("modelId", id);
        point.SetAttributeValue("type", node.IsAssistant ? "asst" : "node");
        var textElement = BuildPointTextElement(node, point.Element(Dgm + "t"));
        if (point.Element(Dgm + "t") is { } existingText)
            existingText.ReplaceWith(textElement);
        else
            point.Add(textElement);
        return point;
    }

    private static XElement BuildPointTextElement(SmartArtNode node, XElement? authoredText)
    {
        // An outline edit rewrites every point, but unchanged nodes still own the
        // application's rich text payload. Keep that payload byte-for-byte at the
        // element level so additional runs, hyperlinks, and run properties survive
        // a sibling edit instead of collapsing to the first run's formatting.
        if (authoredText is not null
            && string.Equals(
                NormalizeText(ReadAuthoredText(authoredText)),
                NormalizeText(node.Text),
                StringComparison.Ordinal))
        {
            return new XElement(authoredText);
        }

        var authoredParagraphs = authoredText?.Elements(A + "p").ToArray() ?? [];
        var fallbackParagraph = authoredParagraphs.FirstOrDefault();
        var result = new XElement(
            Dgm + "t",
            authoredText?.Attributes().Select(attribute => new XAttribute(attribute)) ?? [],
            authoredText?.Element(A + "bodyPr") is { } bodyPr
                ? new XElement(bodyPr)
                : new XElement(A + "bodyPr"),
            authoredText?.Element(A + "lstStyle") is { } listStyle
                ? new XElement(listStyle)
                : new XElement(A + "lstStyle"));

        foreach (var (paragraphText, index) in NormalizeText(node.Text).Split('\n').Select((text, index) => (text, index)))
        {
            var authoredParagraph = index < authoredParagraphs.Length
                ? authoredParagraphs[index]
                : fallbackParagraph;
            var paragraph = new XElement(A + "p");
            if (authoredParagraph?.Element(A + "pPr") is { } paragraphProperties)
                paragraph.Add(new XElement(paragraphProperties));

            var run = new XElement(A + "r");
            if (authoredParagraph?.Element(A + "r")?.Element(A + "rPr") is { } runProperties)
                run.Add(new XElement(runProperties));
            run.Add(new XElement(A + "t", paragraphText));
            paragraph.Add(run);

            if (authoredParagraph?.Element(A + "endParaRPr") is { } endParagraphRunProperties)
                paragraph.Add(new XElement(endParagraphRunProperties));
            result.Add(paragraph);
        }

        return result;
    }

    private static string ReadAuthoredText(XElement textElement)
    {
        var paragraphs = textElement.Elements(A + "p").ToArray();
        if (paragraphs.Length == 0)
            return string.Concat(textElement.Descendants(A + "t").Select(element => element.Value));

        var values = paragraphs.Select(paragraph =>
        {
            var builder = new StringBuilder();
            foreach (var node in paragraph.DescendantNodes())
            {
                if (node is not XElement element)
                    continue;

                if (element.Name == A + "t")
                    builder.Append(element.Value);
                else if (element.Name == A + "br")
                    builder.Append('\n');
            }

            return builder.ToString();
        });

        return string.Join("\n", values);
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

    private static XDocument ParseXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private readonly record struct SmartArtNodeLocation(SmartArtNode? Node, SmartArtNode? Parent, int Index)
    {
        public static SmartArtNodeLocation NotFound => new(null, null, -1);
    }
}
