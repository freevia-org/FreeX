using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record SelectionPaneItem(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible,
    bool CanMoveUp,
    bool CanMoveDown);

public sealed record SelectionPaneItemState(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible);

public sealed record SelectionPaneVisibilityChange(SelectionPaneObjectKind Kind, Guid Id, bool IsVisible);

public sealed record SelectionPaneRenameChange(SelectionPaneObjectKind Kind, Guid Id, string Name);

public sealed record SelectionPaneMoveChange(SelectionPaneObjectKind Kind, Guid Id, bool Forward);

/// <summary>
/// R125-selection-pane-delete-wiring: an object the user removed via the Selection Pane's own
/// Delete affordance (button or Delete key), pending until the dialog's OK is accepted. Carries
/// exactly what <see cref="FreeX.Core.Commands.DeleteDrawingObjectCommand"/> needs -- the SAME
/// command the Delete key / context menu on the sheet grid itself already uses (see
/// DrawingObjectCommandPlanner.BuildDeleteCommand) -- so the Selection Pane never grows a second,
/// divergent deletion path.
/// </summary>
public sealed record SelectionPaneDeleteChange(SelectionPaneObjectKind Kind, Guid Id);

public sealed record SelectionPaneReorderPlan(
    IReadOnlyList<Guid> OrderedIds,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

public sealed record SelectionPaneDropVisualPlan(
    Guid TargetId,
    SelectionPaneDropPlacement Placement,
    bool IsAllowed);

public enum SelectionPaneDialogAction
{
    ApplyVisibility,
    MoveUp,
    MoveDown
}

public enum SelectionPaneDropPlacement
{
    Before,
    After
}

public enum SelectionPaneKeyboardKey
{
    Other,
    F2,
    Space,
    Up,
    Down,
    Delete
}

public enum SelectionPaneKeyboardAction
{
    None,
    MoveUp,
    MoveDown,
    FocusRename,
    ToggleVisibility,
    Delete
}

public sealed record SelectionPaneDialogResult(
    SelectionPaneDialogAction Action,
    SelectionPaneItem? Target,
    IReadOnlyList<SelectionPaneVisibilityChange> VisibilityChanges,
    IReadOnlyList<SelectionPaneRenameChange> RenameChanges,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges,
    IReadOnlyList<SelectionPaneDeleteChange> DeleteChanges);

public sealed record SelectionPanePlannerText(
    string DefaultChartNameFormat,
    string DefaultPictureNameFormat,
    string DefaultTextBoxNameFormat,
    string DefaultShapeNameFormat,
    string DefaultEllipseName,
    string DefaultLineName,
    string DefaultRectangleName)
{
    public static SelectionPanePlannerText Default { get; } = new(
        "Chart {0}",
        "Picture {0}",
        "Text Box {0}",
        "{0} {1}",
        "Ellipse",
        "Line",
        "Rectangle");
}

public static class SelectionPaneFilterValues
{
    public const string All = "All";
    public const string Visible = "Visible";
    public const string Hidden = "Hidden";
    public const string Charts = "Charts";
    public const string Pictures = "Pictures";
    public const string Shapes = "Shapes";
    public const string TextBoxes = "Text Boxes";
}

public static class SelectionPanePlanner
{
    public static IReadOnlyList<SelectionPaneItem> BuildItems(Sheet sheet) =>
        BuildItems(sheet, SelectionPanePlannerText.Default);

    public static IReadOnlyList<SelectionPaneItem> BuildItems(
        Sheet sheet,
        SelectionPanePlannerText text)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(text);

        var items = new List<SelectionPaneItem>();
        AddChartItems(sheet, text, items);
        AddDrawingObjectItems(sheet, text, items);
        items.Reverse();
        return items;
    }

    public static IReadOnlyList<SelectionPaneItemState> FilterItems(
        IReadOnlyList<SelectionPaneItemState> items,
        string search,
        string filter)
    {
        var normalizedSearch = search.Trim();
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? SelectionPaneFilterValues.All : filter;
        if (normalizedSearch.Length == 0 &&
            string.Equals(normalizedFilter, SelectionPaneFilterValues.All, StringComparison.Ordinal))
        {
            return items;
        }

        var filtered = new List<SelectionPaneItemState>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (MatchesSearch(item, normalizedSearch) && MatchesFilter(item, normalizedFilter))
                filtered.Add(item);
        }

        return filtered;
    }

    public static SelectionPaneReorderPlan? PlanMove(
        IReadOnlyList<SelectionPaneItemState> items,
        Guid selectedId,
        bool forward)
    {
        var currentIndex = IndexOfItem(items, selectedId);
        var targetIndex = FindMoveTargetIndex(items, currentIndex, forward);
        if (targetIndex < 0)
            return null;

        var orderedIds = CreateOrderedIds(items);
        (orderedIds[currentIndex], orderedIds[targetIndex]) = (orderedIds[targetIndex], orderedIds[currentIndex]);
        var selected = items[currentIndex];
        return new SelectionPaneReorderPlan(
            orderedIds,
            [new SelectionPaneMoveChange(selected.Kind, selected.Id, forward)]);
    }

    public static SelectionPaneReorderPlan? PlanDragReorder(
        IReadOnlyList<SelectionPaneItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before)
    {
        var dragPlan = CreateDragMovePlan(items, draggedId, targetId, placement);
        if (dragPlan is null)
            return null;

        var orderedIds = CreateOrderedIds(items);
        var dragged = orderedIds[dragPlan.DraggedIndex];
        orderedIds.RemoveAt(dragPlan.DraggedIndex);
        orderedIds.Insert(dragPlan.InsertIndex, dragged);
        return new SelectionPaneReorderPlan(orderedIds, dragPlan.MoveChanges);
    }

    public static SelectionPaneDropVisualPlan PlanDropVisual(
        IReadOnlyList<SelectionPaneItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before)
    {
        var plan = PlanDragReorder(items, draggedId, targetId, placement);
        return new SelectionPaneDropVisualPlan(targetId, placement, plan is not null);
    }

    public static SelectionPaneKeyboardAction PlanKeyboardAction(
        SelectionPaneKeyboardKey key,
        bool hasControlModifier) =>
        key switch
        {
            SelectionPaneKeyboardKey.Up when hasControlModifier => SelectionPaneKeyboardAction.MoveUp,
            SelectionPaneKeyboardKey.Down when hasControlModifier => SelectionPaneKeyboardAction.MoveDown,
            SelectionPaneKeyboardKey.F2 => SelectionPaneKeyboardAction.FocusRename,
            SelectionPaneKeyboardKey.Space => SelectionPaneKeyboardAction.ToggleVisibility,
            SelectionPaneKeyboardKey.Delete => SelectionPaneKeyboardAction.Delete,
            _ => SelectionPaneKeyboardAction.None
        };

    public static int FindMoveTargetIndex(
        IReadOnlyList<SelectionPaneItemState> items,
        int currentIndex,
        bool forward)
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

    public static bool CanReorderKinds(SelectionPaneObjectKind draggedKind, SelectionPaneObjectKind targetKind) =>
        draggedKind == targetKind ||
        DrawingObjectZOrder.IsSupportedKind(draggedKind) && DrawingObjectZOrder.IsSupportedKind(targetKind);

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneItemState> currentStates)
    {
        var states = currentStates.ToDictionary(state => state.Id, state => state.IsVisible);
        var changes = new List<SelectionPaneVisibilityChange>();
        for (var index = 0; index < originalItems.Count; index++)
        {
            var item = originalItems[index];
            if (states.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
                changes.Add(new SelectionPaneVisibilityChange(item.Kind, item.Id, isVisible));
        }

        return changes;
    }

    public static IReadOnlyList<SelectionPaneRenameChange> CreateRenameChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneItemState> currentStates)
    {
        var names = currentStates.ToDictionary(state => state.Id, state => NormalizeName(state.Name));
        var changes = new List<SelectionPaneRenameChange>();
        for (var index = 0; index < originalItems.Count; index++)
        {
            var item = originalItems[index];
            if (names.TryGetValue(item.Id, out var name) && !string.Equals(name, item.Name, StringComparison.Ordinal))
                changes.Add(new SelectionPaneRenameChange(item.Kind, item.Id, name));
        }

        return changes;
    }

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<SelectionPaneItemState> currentStates,
        IReadOnlyList<SelectionPaneMoveChange> moveChanges,
        IReadOnlyList<SelectionPaneDeleteChange>? deleteChanges = null) =>
        new(
            action,
            target,
            CreateVisibilityChanges(originalItems, currentStates),
            CreateRenameChanges(originalItems, currentStates),
            moveChanges,
            deleteChanges ?? []);

    public static bool HasChanges(
        IReadOnlyList<SelectionPaneVisibilityChange> visibility,
        IReadOnlyList<SelectionPaneRenameChange> rename,
        IReadOnlyList<SelectionPaneMoveChange> moves,
        IReadOnlyList<SelectionPaneDeleteChange>? deletes = null) =>
        visibility.Count > 0 || rename.Count > 0 || moves.Count > 0 || (deletes?.Count ?? 0) > 0;

    public static IWorkbookCommand? CreateCommand(
        SheetId sheetId,
        IReadOnlyList<SelectionPaneVisibilityChange> visibility,
        IReadOnlyList<SelectionPaneRenameChange> rename,
        IReadOnlyList<SelectionPaneMoveChange> moves,
        IReadOnlyList<SelectionPaneDeleteChange>? deletes = null)
    {
        deletes ??= [];
        if (!HasChanges(visibility, rename, moves, deletes))
            return null;

        var commands = new List<IWorkbookCommand>(rename.Count + visibility.Count + moves.Count + deletes.Count);
        foreach (var change in rename)
            commands.Add(new RenameSelectionPaneObjectCommand(sheetId, change.Kind, change.Id, change.Name));
        foreach (var change in visibility)
            commands.Add(new SetSelectionPaneObjectVisibilityCommand(sheetId, change.Kind, change.Id, change.IsVisible));
        foreach (var change in moves)
            commands.Add(new MoveSelectionPaneObjectCommand(sheetId, change.Kind, change.Id, change.Forward));
        // R125-selection-pane-delete-wiring: same DeleteDrawingObjectCommand the sheet grid's
        // Delete key / context menu use (DrawingObjectCommandPlanner.BuildDeleteCommand) --
        // applied last so a rename/visibility/move on an object also being deleted in the same
        // OK still lands on the object before it's removed (matches Excel's Selection Pane,
        // which never lets you delete AND rename the same object in one apply anyway, since
        // deleting immediately removes it from the editable list).
        foreach (var change in deletes)
            commands.Add(new DeleteDrawingObjectCommand(sheetId, change.Kind, change.Id));

        return new CompositeWorkbookCommand("Selection Pane", commands);
    }

    public static IReadOnlyList<SelectionPaneMoveChange> CreateDragMoveChanges(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement = SelectionPaneDropPlacement.Before) =>
        CreateDragMovePlan(currentOrder, draggedId, targetId, placement)?.MoveChanges ?? [];

    private static void AddChartItems(
        Sheet sheet,
        SelectionPanePlannerText text,
        List<SelectionPaneItem> items)
    {
        for (var index = 0; index < sheet.Charts.Count; index++)
        {
            var chart = sheet.Charts[index];
            items.Add(new SelectionPaneItem(
                SelectionPaneObjectKind.Chart,
                chart.Id,
                DisplayName(chart.Name, Format(text.DefaultChartNameFormat, index + 1)),
                chart.IsVisible,
                index < sheet.Charts.Count - 1,
                index > 0));
        }
    }

    private static void AddDrawingObjectItems(
        Sheet sheet,
        SelectionPanePlannerText text,
        List<SelectionPaneItem> items)
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
                case SelectionPaneObjectKind.Shape:
                    AddShapeItem(sheet, text, items, entry.Id, shapeIndexes, canMoveUp, canMoveDown);
                    break;
                case SelectionPaneObjectKind.Picture:
                    AddPictureItem(sheet, text, items, entry.Id, pictureIndexes, canMoveUp, canMoveDown);
                    break;
                case SelectionPaneObjectKind.TextBox:
                    AddTextBoxItem(sheet, text, items, entry.Id, textBoxIndexes, canMoveUp, canMoveDown);
                    break;
            }
        }
    }

    private static void AddShapeItem(
        Sheet sheet,
        SelectionPanePlannerText text,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> shapeIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!shapeIndexes.TryGetValue(id, out var index))
            return;

        var shape = sheet.DrawingShapes[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.Shape,
            shape.Id,
            DisplayName(shape.Name, Format(text.DefaultShapeNameFormat, ShapeName(text, shape.Kind), index + 1)),
            shape.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static void AddPictureItem(
        Sheet sheet,
        SelectionPanePlannerText text,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> pictureIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!pictureIndexes.TryGetValue(id, out var index))
            return;

        var picture = sheet.Pictures[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.Picture,
            picture.Id,
            DisplayName(picture.Name, Format(text.DefaultPictureNameFormat, index + 1)),
            picture.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static void AddTextBoxItem(
        Sheet sheet,
        SelectionPanePlannerText text,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> textBoxIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!textBoxIndexes.TryGetValue(id, out var index))
            return;

        var textBox = sheet.TextBoxes[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            DisplayName(textBox.Name, Format(text.DefaultTextBoxNameFormat, index + 1)),
            textBox.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        IReadOnlyList<SelectionPaneItemState> items,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        var (draggedIndex, targetIndex) = FindDragIndexes(items, draggedId, targetId);
        if (draggedIndex < 0 || targetIndex < 0 || draggedIndex == targetIndex)
            return null;

        var dragged = items[draggedIndex];
        var target = items[targetIndex];
        if (!CanReorderKinds(dragged.Kind, target.Kind))
            return null;

        return CreateDragMovePlan(dragged.Kind, dragged.Id, draggedIndex, targetIndex, placement);
    }

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> currentOrder,
        Guid draggedId,
        Guid targetId,
        SelectionPaneDropPlacement placement)
    {
        var (draggedIndex, targetIndex) = FindDragIndexes(currentOrder, draggedId, targetId);
        if (draggedIndex < 0 || targetIndex < 0 || draggedIndex == targetIndex)
            return null;

        var dragged = currentOrder[draggedIndex];
        var target = currentOrder[targetIndex];
        if (!CanReorderKinds(dragged.Kind, target.Kind))
            return null;

        return CreateDragMovePlan(dragged.Kind, dragged.Id, draggedIndex, targetIndex, placement);
    }

    private static SelectionPaneDragMovePlan? CreateDragMovePlan(
        SelectionPaneObjectKind kind,
        Guid draggedId,
        int draggedIndex,
        int targetIndex,
        SelectionPaneDropPlacement placement)
    {
        var insertIndex = placement == SelectionPaneDropPlacement.After ? targetIndex + 1 : targetIndex;
        if (draggedIndex < insertIndex)
            insertIndex--;

        if (insertIndex == draggedIndex)
            return null;

        var moves = new List<SelectionPaneMoveChange>(Math.Abs(draggedIndex - insertIndex));
        var forward = draggedIndex > insertIndex;
        var step = forward ? -1 : 1;
        for (var index = draggedIndex; index != insertIndex; index += step)
            moves.Add(new SelectionPaneMoveChange(kind, draggedId, forward));

        return new SelectionPaneDragMovePlan(draggedIndex, insertIndex, moves);
    }

    private static int IndexOfItem(IReadOnlyList<SelectionPaneItemState> items, Guid id)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Id == id)
                return index;
        }

        return -1;
    }

    private static List<Guid> CreateOrderedIds(IReadOnlyList<SelectionPaneItemState> items)
    {
        var orderedIds = new List<Guid>(items.Count);
        for (var index = 0; index < items.Count; index++)
            orderedIds.Add(items[index].Id);

        return orderedIds;
    }

    private static (int DraggedIndex, int TargetIndex) FindDragIndexes(
        IReadOnlyList<SelectionPaneItemState> items,
        Guid draggedId,
        Guid targetId)
    {
        var draggedIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var id = items[index].Id;
            if (id == draggedId)
                draggedIndex = index;
            else if (id == targetId)
                targetIndex = index;

            if (draggedIndex >= 0 && targetIndex >= 0)
                break;
        }

        return (draggedIndex, targetIndex);
    }

    private static (int DraggedIndex, int TargetIndex) FindDragIndexes(
        IReadOnlyList<(SelectionPaneObjectKind Kind, Guid Id)> items,
        Guid draggedId,
        Guid targetId)
    {
        var draggedIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var id = items[index].Id;
            if (id == draggedId)
                draggedIndex = index;
            else if (id == targetId)
                targetIndex = index;

            if (draggedIndex >= 0 && targetIndex >= 0)
                break;
        }

        return (draggedIndex, targetIndex);
    }

    private static string ShapeName(SelectionPanePlannerText text, DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => text.DefaultEllipseName,
            DrawingShapeKind.Line => text.DefaultLineName,
            _ => text.DefaultRectangleName
        };

    private static string DisplayName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    private static string NormalizeName(string name) => name.Trim();

    private static bool MatchesSearch(SelectionPaneItemState item, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(SelectionPaneItemState item, string filter) =>
        filter switch
        {
            SelectionPaneFilterValues.Visible => item.IsVisible,
            SelectionPaneFilterValues.Hidden => !item.IsVisible,
            SelectionPaneFilterValues.Charts => item.Kind == SelectionPaneObjectKind.Chart,
            SelectionPaneFilterValues.Pictures => item.Kind == SelectionPaneObjectKind.Picture,
            SelectionPaneFilterValues.Shapes => item.Kind == SelectionPaneObjectKind.Shape,
            SelectionPaneFilterValues.TextBoxes => item.Kind == SelectionPaneObjectKind.TextBox,
            _ => true
        };

    private static IReadOnlyDictionary<Guid, int> CreateIndexMap<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> getId)
    {
        var indexes = new Dictionary<Guid, int>(items.Count);
        for (var index = 0; index < items.Count; index++)
            indexes[getId(items[index])] = index;

        return indexes;
    }

    private sealed record SelectionPaneDragMovePlan(
        int DraggedIndex,
        int InsertIndex,
        IReadOnlyList<SelectionPaneMoveChange> MoveChanges);
}
