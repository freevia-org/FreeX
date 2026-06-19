using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

/// <summary>
/// Portable planner for the Selection Pane drawing-object manager. Single-sources the parts that must behave
/// identically across the WPF host and the cross-platform shell: enumerating the active sheet's drawing objects
/// (charts, pictures, shapes, text boxes) with display names / visibility / z-order, computing reorder results
/// (bring forward / send backward, show-all / hide-all), and translating the pending visibility / rename / move
/// edits into Core drawing commands. The shell layer only owns the dialog chrome; all object-list building, the
/// can-move-up/down reasoning, and the command list come from here so the two shells never drift.
/// </summary>
public static class SelectionPaneViewPlanner
{
    /// <summary>Localized display text the planner needs when building default object names + kind labels.</summary>
    public sealed record Text(
        string DefaultChartNameFormat,
        string DefaultPictureNameFormat,
        string DefaultTextBoxNameFormat,
        string DefaultShapeNameFormat,
        string DefaultEllipseName,
        string DefaultLineName,
        string DefaultRectangleName,
        string KindChart,
        string KindPicture,
        string KindShape,
        string KindTextBox)
    {
        public static Text Default { get; } = new(
            "Chart {0}",
            "Picture {0}",
            "Text Box {0}",
            "{0} {1}",
            "Ellipse",
            "Line",
            "Rectangle",
            "Chart",
            "Picture",
            "Shape",
            "Text Box");
    }

    /// <summary>One row in the Selection Pane list: a drawing object with display name, kind, visibility and z-order.</summary>
    public sealed record Item(
        SelectionPaneObjectKind Kind,
        Guid Id,
        string Name,
        string KindLabel,
        bool IsVisible,
        bool CanMoveUp,
        bool CanMoveDown);

    /// <summary>A pending visibility toggle for one object.</summary>
    public sealed record VisibilityChange(SelectionPaneObjectKind Kind, Guid Id, bool IsVisible);

    /// <summary>A pending rename for one object.</summary>
    public sealed record RenameChange(SelectionPaneObjectKind Kind, Guid Id, string Name);

    /// <summary>A pending one-step z-order move for one object.</summary>
    public sealed record MoveChange(SelectionPaneObjectKind Kind, Guid Id, bool Forward);

    /// <summary>Builds the ordered object list (front-most first) for the active sheet.</summary>
    public static IReadOnlyList<Item> BuildItems(Sheet sheet, Text text)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(text);

        var items = new List<Item>();
        AddChartItems(sheet, text, items);
        AddDrawingObjectItems(sheet, text, items);
        items.Reverse();
        return items;
    }

    /// <summary>
    /// Returns the index the selected row would move to for a one-step bring-forward (<paramref name="forward"/>
    /// true) or send-backward, skipping rows of an incompatible kind, or -1 when no legal move exists.
    /// </summary>
    public static int FindMoveTargetIndex(IReadOnlyList<Item> items, int currentIndex, bool forward)
    {
        if (currentIndex < 0 || currentIndex >= items.Count)
            return -1;

        var step = forward ? -1 : 1;
        for (var index = currentIndex + step; index >= 0 && index < items.Count; index += step)
        {
            if (CanReorderKinds(items[currentIndex].Kind, items[index].Kind))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Plans a one-step z-order move of the row at <paramref name="currentIndex"/>: the reordered list (for the
    /// dialog) plus the single Core <see cref="MoveChange"/> to record, or null when no legal move exists.
    /// </summary>
    public static (IReadOnlyList<Item> Ordered, MoveChange Change)? PlanMove(
        IReadOnlyList<Item> items,
        int currentIndex,
        bool forward)
    {
        var targetIndex = FindMoveTargetIndex(items, currentIndex, forward);
        if (targetIndex < 0)
            return null;

        var ordered = items.ToList();
        (ordered[currentIndex], ordered[targetIndex]) = (ordered[targetIndex], ordered[currentIndex]);
        var moved = RecomputeMoveFlags(ordered);
        var selected = items[currentIndex];
        return (moved, new MoveChange(selected.Kind, selected.Id, forward));
    }

    /// <summary>True when two object kinds participate in the same z-order stack and can be reordered relative to each other.</summary>
    public static bool CanReorderKinds(SelectionPaneObjectKind draggedKind, SelectionPaneObjectKind targetKind) =>
        draggedKind == targetKind ||
        (DrawingObjectZOrder.IsSupportedKind(draggedKind) && DrawingObjectZOrder.IsSupportedKind(targetKind));

    /// <summary>Recomputes <see cref="Item.CanMoveUp"/>/<see cref="Item.CanMoveDown"/> after a reorder.</summary>
    public static IReadOnlyList<Item> RecomputeMoveFlags(IReadOnlyList<Item> items)
    {
        var result = new List<Item>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var canUp = FindMoveTargetIndex(items, index, forward: true) >= 0;
            var canDown = FindMoveTargetIndex(items, index, forward: false) >= 0;
            result.Add(items[index] with { CanMoveUp = canUp, CanMoveDown = canDown });
        }

        return result;
    }

    /// <summary>Diffs visibility against the originals, returning only the rows that actually changed.</summary>
    public static IReadOnlyList<VisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<Item> originalItems,
        IReadOnlyList<Item> currentItems)
    {
        var current = currentItems.ToDictionary(item => item.Id, item => item.IsVisible);
        var changes = new List<VisibilityChange>();
        foreach (var item in originalItems)
        {
            if (current.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
                changes.Add(new VisibilityChange(item.Kind, item.Id, isVisible));
        }

        return changes;
    }

    /// <summary>Diffs trimmed names against the originals, returning only the rows that actually changed.</summary>
    public static IReadOnlyList<RenameChange> CreateRenameChanges(
        IReadOnlyList<Item> originalItems,
        IReadOnlyList<Item> currentItems)
    {
        var current = currentItems.ToDictionary(item => item.Id, item => item.Name.Trim());
        var changes = new List<RenameChange>();
        foreach (var item in originalItems)
        {
            if (current.TryGetValue(item.Id, out var name) && !string.Equals(name, item.Name, StringComparison.Ordinal))
                changes.Add(new RenameChange(item.Kind, item.Id, name));
        }

        return changes;
    }

    /// <summary>True when the pending edits contain anything worth committing.</summary>
    public static bool HasChanges(
        IReadOnlyList<VisibilityChange> visibility,
        IReadOnlyList<RenameChange> rename,
        IReadOnlyList<MoveChange> moves) =>
        visibility.Count > 0 || rename.Count > 0 || moves.Count > 0;

    /// <summary>
    /// Builds the composite Core command for the active sheet: renames, then visibility toggles, then z-order
    /// moves. Returns null when there is nothing to do. Targets are resolved on the active sheet (the shell only
    /// shows the active sheet's objects), so ids are applied directly.
    /// </summary>
    public static IWorkbookCommand? CreateCommand(
        SheetId sheetId,
        IReadOnlyList<VisibilityChange> visibility,
        IReadOnlyList<RenameChange> rename,
        IReadOnlyList<MoveChange> moves)
    {
        if (!HasChanges(visibility, rename, moves))
            return null;

        var commands = new List<IWorkbookCommand>(rename.Count + visibility.Count + moves.Count);
        foreach (var change in rename)
            commands.Add(new RenameSelectionPaneObjectCommand(sheetId, change.Kind, change.Id, change.Name));
        foreach (var change in visibility)
            commands.Add(new SetSelectionPaneObjectVisibilityCommand(sheetId, change.Kind, change.Id, change.IsVisible));
        foreach (var change in moves)
            commands.Add(new MoveSelectionPaneObjectCommand(sheetId, change.Kind, change.Id, change.Forward));

        return new CompositeWorkbookCommand("Selection Pane", commands);
    }

    private static void AddChartItems(Sheet sheet, Text text, List<Item> items)
    {
        for (var index = 0; index < sheet.Charts.Count; index++)
        {
            var chart = sheet.Charts[index];
            items.Add(new Item(
                SelectionPaneObjectKind.Chart,
                chart.Id,
                DisplayName(chart.Name, Format(text.DefaultChartNameFormat, index + 1)),
                text.KindChart,
                chart.IsVisible,
                index < sheet.Charts.Count - 1,
                index > 0));
        }
    }

    private static void AddDrawingObjectItems(Sheet sheet, Text text, List<Item> items)
    {
        var shapeIndexes = CreateIndexMap(sheet.DrawingShapes, shape => shape.Id);
        var pictureIndexes = CreateIndexMap(sheet.Pictures, picture => picture.Id);
        var textBoxIndexes = CreateIndexMap(sheet.TextBoxes, textBox => textBox.Id);
        var order = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        for (var stackIndex = 0; stackIndex < order.Count; stackIndex++)
        {
            var entry = order[stackIndex];
            var canMoveUp = stackIndex < order.Count - 1;
            var canMoveDown = stackIndex > 0;
            switch (entry.Kind)
            {
                case SelectionPaneObjectKind.Shape when shapeIndexes.TryGetValue(entry.Id, out var index):
                    var shape = sheet.DrawingShapes[index];
                    items.Add(new Item(
                        SelectionPaneObjectKind.Shape,
                        shape.Id,
                        DisplayName(shape.Name, Format(text.DefaultShapeNameFormat, ShapeName(text, shape.Kind), index + 1)),
                        text.KindShape,
                        shape.IsVisible,
                        canMoveUp,
                        canMoveDown));
                    break;
                case SelectionPaneObjectKind.Picture when pictureIndexes.TryGetValue(entry.Id, out var index):
                    var picture = sheet.Pictures[index];
                    items.Add(new Item(
                        SelectionPaneObjectKind.Picture,
                        picture.Id,
                        DisplayName(picture.Name, Format(text.DefaultPictureNameFormat, index + 1)),
                        text.KindPicture,
                        picture.IsVisible,
                        canMoveUp,
                        canMoveDown));
                    break;
                case SelectionPaneObjectKind.TextBox when textBoxIndexes.TryGetValue(entry.Id, out var index):
                    var textBox = sheet.TextBoxes[index];
                    items.Add(new Item(
                        SelectionPaneObjectKind.TextBox,
                        textBox.Id,
                        DisplayName(textBox.Name, Format(text.DefaultTextBoxNameFormat, index + 1)),
                        text.KindTextBox,
                        textBox.IsVisible,
                        canMoveUp,
                        canMoveDown));
                    break;
            }
        }
    }

    private static string ShapeName(Text text, DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => text.DefaultEllipseName,
            DrawingShapeKind.Line => text.DefaultLineName,
            _ => text.DefaultRectangleName
        };

    private static string DisplayName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static string Format(string format, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);

    private static IReadOnlyDictionary<Guid, int> CreateIndexMap<T>(IReadOnlyList<T> items, Func<T, Guid> getId)
    {
        var indexes = new Dictionary<Guid, int>(items.Count);
        for (var index = 0; index < items.Count; index++)
            indexes[getId(items[index])] = index;

        return indexes;
    }
}
