using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
            return new CommandOutcome(false, "Selection pane object was not found.");

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
        return _kind switch
        {
            SelectionPaneObjectKind.Chart => Move(sheet.Charts, chart => chart.Id, chart => chart.DataRange.Start),
            SelectionPaneObjectKind.Picture => Move(sheet.Pictures, picture => picture.Id, picture => picture.Anchor),
            SelectionPaneObjectKind.TextBox => Move(sheet.TextBoxes, textBox => textBox.Id, textBox => textBox.Anchor),
            SelectionPaneObjectKind.Shape => Move(sheet.DrawingShapes, shape => shape.Id, shape => shape.Anchor),
            _ => new CommandOutcome(false, "Selection pane object kind is not supported.")
        };
    }

    public void Revert(ICommandContext ctx)
    {
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
        var index = list.FindIndex(item => getId(item) == _objectId);
        if (index < 0)
            return new CommandOutcome(false, "Selection pane object was not found.");

        var toIndex = _forward ? index + 1 : index - 1;
        if (toIndex < 0 || toIndex >= list.Count)
            return new CommandOutcome(true);

        _fromIndex = index;
        _toIndex = toIndex;
        (list[_fromIndex], list[_toIndex]) = (list[_toIndex], list[_fromIndex]);
        return new CommandOutcome(true, AffectedCells: [getAnchor(list[_toIndex])]);
    }

    private void Swap<T>(List<T> list) =>
        (list[_fromIndex], list[_toIndex]) = (list[_toIndex], list[_fromIndex]);
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

        var target = SelectionPaneObjectAccess.Find(ctx.GetSheet(_sheetId), _kind, _objectId);
        if (target is null)
            return new CommandOutcome(false, "Selection pane object was not found.");

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
                    value => chart.Name = value))
                .ToList(),
            SelectionPaneObjectKind.Picture => sheet.Pictures
                .Select(picture => new SelectionPaneObjectRef(
                    picture.Id,
                    picture.Anchor,
                    () => picture.IsVisible,
                    value => picture.IsVisible = value,
                    () => picture.Name,
                    value => picture.Name = value))
                .ToList(),
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes
                .Select(textBox => new SelectionPaneObjectRef(
                    textBox.Id,
                    textBox.Anchor,
                    () => textBox.IsVisible,
                    value => textBox.IsVisible = value,
                    () => textBox.Name,
                    value => textBox.Name = value))
                .ToList(),
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes
                .Select(shape => new SelectionPaneObjectRef(
                    shape.Id,
                    shape.Anchor,
                    () => shape.IsVisible,
                    value => shape.IsVisible = value,
                    () => shape.Name,
                    value => shape.Name = value))
                .ToList(),
            _ => []
        };

    public static SelectionPaneObjectRef? Find(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId) =>
        GetList(sheet, kind).FirstOrDefault(item => item.Id == objectId);
}

internal sealed record SelectionPaneObjectRef(
    Guid Id,
    CellAddress Anchor,
    Func<bool> GetVisibility,
    Action<bool> SetVisibility,
    Func<string?> GetName,
    Action<string?> SetName)
{
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
