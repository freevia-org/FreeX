using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetSelectionPaneObjectVisibilityCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _objectId;
    private readonly bool _isVisible;
    private bool _previous;
    private bool _applied;

    public string Label => "Object Visibility";

    public SetSelectionPaneObjectVisibilityCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        bool isVisible)
    {
        _sheetId = sheetId;
        _kind = kind;
        _objectId = objectId;
        _isVisible = isVisible;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var target = SelectionPaneObjectAccess.Find(sheet, _kind, _objectId);
        if (target is null)
            return SelectionPaneObjectAccess.ObjectNotFound();

        if (SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(sheet, target) is { } protectedOutcome)
            return protectedOutcome;

        _previous = target.IsVisible;
        target.IsVisible = _isVisible;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [target.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var target = SelectionPaneObjectAccess.Find(ctx.GetSheet(_sheetId), _kind, _objectId);
        if (target is null)
            return;

        target.IsVisible = _previous;
        _applied = false;
    }
}

public sealed class MoveSelectionPaneObjectCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _objectId;
    private readonly bool _forward;
    private int _fromIndex = -1;
    private int _toIndex = -1;
    private List<DrawingObjectZOrderEntry>? _previousDrawingOrder;
    private bool _hadExplicitDrawingOrder;

    public string Label => _forward ? "Bring Forward" : "Send Backward";

    public MoveSelectionPaneObjectCommand(SheetId sheetId, SelectionPaneObjectKind kind, Guid objectId, bool forward)
    {
        _sheetId = sheetId;
        _kind = kind;
        _objectId = objectId;
        _forward = forward;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        // R62-meta-1: a Chart now moves through the same DrawingObjectZOrder-backed path as every
        // other supported kind. Routing it through Move(sheet.Charts, ...) instead (the old
        // behaviour) only ever reordered the Charts list -- it never touched DrawingObjectZOrder,
        // so a chart's Bring Forward/Send Backward had zero effect on its position relative to
        // shapes/pictures/text boxes in the same drawing stack.
        return _kind switch
        {
            SelectionPaneObjectKind.Chart or
                SelectionPaneObjectKind.Picture or
                SelectionPaneObjectKind.TextBox or
                SelectionPaneObjectKind.Shape => MoveDrawingObject(sheet),
            // R113-model-drawing-object-lock-1-1: the per-object Locked check lives inside
            // MoveDrawingObject (it needs the resolved object); an unsupported kind has no object to
            // consult, so it keeps the original sheet-only protection check ahead of its error.
            _ => SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(sheet)
                ?? new CommandOutcome(false, "Selection pane object kind is not supported.")
        };
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousDrawingOrder is not null)
        {
            var drawingSheet = ctx.GetSheet(_sheetId);
            drawingSheet.DrawingObjectZOrder.Clear();
            if (_hadExplicitDrawingOrder)
                drawingSheet.DrawingObjectZOrder.AddRange(_previousDrawingOrder);
            _previousDrawingOrder = null;
            _hadExplicitDrawingOrder = false;
            return;
        }

        if (_fromIndex < 0 || _toIndex < 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        switch (_kind)
        {
            case SelectionPaneObjectKind.Chart:
                Swap(sheet.Charts);
                break;
            case SelectionPaneObjectKind.Picture:
                Swap(sheet.Pictures);
                break;
            case SelectionPaneObjectKind.TextBox:
                Swap(sheet.TextBoxes);
                break;
            case SelectionPaneObjectKind.Shape:
                Swap(sheet.DrawingShapes);
                break;
        }
        _fromIndex = -1;
        _toIndex = -1;
    }

    private CommandOutcome Move<T>(List<T> list, Func<T, Guid> getId, Func<T, CellAddress> getAnchor)
    {
        var index = FindObjectIndex(list, getId, _objectId);
        if (index < 0)
            return SelectionPaneObjectAccess.ObjectNotFound();

        var toIndex = _forward ? index + 1 : index - 1;
        if (toIndex < 0 || toIndex >= list.Count)
            return new CommandOutcome(true);

        _fromIndex = index;
        _toIndex = toIndex;
        (list[_fromIndex], list[_toIndex]) = (list[_toIndex], list[_fromIndex]);
        return new CommandOutcome(true, AffectedCells: [getAnchor(list[_toIndex])]);
    }

    private CommandOutcome MoveDrawingObject(Sheet sheet)
    {
        var target = SelectionPaneObjectAccess.Find(sheet, _kind, _objectId);
        if (target is null)
            return SelectionPaneObjectAccess.ObjectNotFound();

        if (SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(sheet, target) is { } protectedOutcome)
            return protectedOutcome;

        var entry = new DrawingObjectZOrderEntry(_kind, _objectId);
        if (!DrawingObjectZOrder.ContainsObject(sheet, entry))
            return SelectionPaneObjectAccess.ObjectNotFound();

        var normalizedOrder = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        var index = FindDrawingOrderIndex(normalizedOrder, entry);
        if (index < 0)
            return SelectionPaneObjectAccess.ObjectNotFound();

        var toIndex = _forward ? index + 1 : index - 1;
        if (toIndex < 0 || toIndex >= normalizedOrder.Count)
            return new CommandOutcome(true);

        _hadExplicitDrawingOrder = sheet.DrawingObjectZOrder.Count > 0;
        _previousDrawingOrder = sheet.DrawingObjectZOrder.ToList();
        sheet.DrawingObjectZOrder.Clear();
        sheet.DrawingObjectZOrder.AddRange(normalizedOrder);
        (sheet.DrawingObjectZOrder[index], sheet.DrawingObjectZOrder[toIndex]) =
            (sheet.DrawingObjectZOrder[toIndex], sheet.DrawingObjectZOrder[index]);
        return new CommandOutcome(true, AffectedCells: GetAffectedCells(sheet));
    }

    private static int FindDrawingOrderIndex(
        IReadOnlyList<DrawingObjectZOrderEntry> order,
        DrawingObjectZOrderEntry entry)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == entry)
                return index;
        }

        return -1;
    }

    private IReadOnlyList<CellAddress> GetAffectedCells(Sheet sheet) =>
        SelectionPaneObjectAccess.Find(sheet, _kind, _objectId) is { } target
            ? [target.Anchor]
            : [];

    private void Swap<T>(List<T> list) =>
        (list[_fromIndex], list[_toIndex]) = (list[_toIndex], list[_fromIndex]);

    private static int FindObjectIndex<T>(IReadOnlyList<T> list, Func<T, Guid> getId, Guid objectId)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (getId(list[index]) == objectId)
                return index;
        }

        return -1;
    }
}

public sealed class RenameSelectionPaneObjectCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _objectId;
    private readonly string _newName;
    private string? _previousName;
    private bool _applied;

    public string Label => "Rename Object";

    public RenameSelectionPaneObjectCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        string newName)
    {
        _sheetId = sheetId;
        _kind = kind;
        _objectId = objectId;
        _newName = newName.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_newName))
            return new CommandOutcome(false, "Object name cannot be blank.");

        var sheet = ctx.GetSheet(_sheetId);
        var target = SelectionPaneObjectAccess.Find(sheet, _kind, _objectId);
        if (target is null)
            return SelectionPaneObjectAccess.ObjectNotFound();

        if (SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(sheet, target) is { } protectedOutcome)
            return protectedOutcome;

        _previousName = target.Name;
        target.Name = _newName;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [target.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var target = SelectionPaneObjectAccess.Find(ctx.GetSheet(_sheetId), _kind, _objectId);
        if (target is null)
            return;

        target.Name = _previousName;
        _previousName = null;
        _applied = false;
    }
}

internal static class SelectionPaneObjectAccess
{
    private const string ObjectNotFoundMessage = "Selection pane object was not found.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    /// <summary>
    /// R113-model-drawing-object-lock-1-1: object-aware companion to
    /// <see cref="RejectIfEditObjectsBlocked(Sheet)"/>, mirroring the per-object overloads R111/R112
    /// added to <see cref="PictureCommandGuards"/>, <see cref="ChartCommandGuards"/>,
    /// <see cref="DrawingShapeCommandGuards"/> and <see cref="TextBoxCommandGuards"/>. The selection
    /// pane commands operate on one already-resolved object, so they must honour that object's
    /// author-set Locked flag: an unlocked object (<c>Locked == false</c>) stays showable/hideable,
    /// re-orderable and renameable even while the sheet blocks "Edit objects", matching Excel's
    /// Format Object &gt; Properties &gt; Locked checkbox. A locked object (the default) is rejected
    /// exactly like the sheet-only overload.
    /// </summary>
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet, SelectionPaneObjectRef target) =>
        target.Locked ? RejectIfEditObjectsBlocked(sheet) : null;

    public static CommandOutcome ObjectNotFound() =>
        new(false, ObjectNotFoundMessage);

    public static List<SelectionPaneObjectRef> GetList(Sheet sheet, SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart => sheet.Charts
                .Select(chart => new SelectionPaneObjectRef(
                    chart.Id,
                    chart.DataRange.Start,
                    () => chart.IsVisible,
                    value => chart.IsVisible = value,
                    () => chart.Name,
                    value => chart.Name = value,
                    () => chart.Locked))
                .ToList(),
            SelectionPaneObjectKind.Picture => sheet.Pictures
                .Select(picture => new SelectionPaneObjectRef(
                    picture.Id,
                    picture.Anchor,
                    () => picture.IsVisible,
                    value => picture.IsVisible = value,
                    () => picture.Name,
                    value => picture.Name = value,
                    () => picture.Locked))
                .ToList(),
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes
                .Select(textBox => new SelectionPaneObjectRef(
                    textBox.Id,
                    textBox.Anchor,
                    () => textBox.IsVisible,
                    value => textBox.IsVisible = value,
                    () => textBox.Name,
                    value => textBox.Name = value,
                    () => textBox.Locked))
                .ToList(),
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes
                .Select(shape => new SelectionPaneObjectRef(
                    shape.Id,
                    shape.Anchor,
                    () => shape.IsVisible,
                    value => shape.IsVisible = value,
                    () => shape.Name,
                    value => shape.Name = value,
                    () => shape.Locked))
                .ToList(),
            _ => []
        };

    public static SelectionPaneObjectRef? Find(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId)
    {
        foreach (var item in GetList(sheet, kind))
        {
            if (item.Id == objectId)
                return item;
        }

        return null;
    }
}

internal sealed record SelectionPaneObjectRef(
    Guid Id,
    CellAddress Anchor,
    Func<bool> GetVisibility,
    Action<bool> SetVisibility,
    Func<string?> GetName,
    Action<string?> SetName,
    Func<bool> GetLocked)
{
    /// <summary>
    /// R113-model-drawing-object-lock-1-1: the underlying model's per-object Locked flag, so the
    /// selection pane commands can use
    /// <see cref="SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(Sheet, SelectionPaneObjectRef)"/>.
    /// </summary>
    public bool Locked => GetLocked();

    public bool IsVisible
    {
        get => GetVisibility();
        set => SetVisibility(value);
    }

    public string? Name
    {
        get => GetName();
        set => SetName(value);
    }
}
