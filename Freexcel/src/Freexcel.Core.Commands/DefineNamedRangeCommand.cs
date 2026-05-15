using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Command to define (or replace) a named range in the workbook.
/// Supports undo: if the name previously existed, its old range is restored on Revert;
/// if it was newly created, it is removed on Revert.
/// </summary>
public sealed class DefineNamedRangeCommand : IWorkbookCommand
{
    private readonly string _name;
    private readonly GridRange _range;

    // Snapshot captured during Apply for undo
    private bool _existed;
    private GridRange _previousRange;

    public string Label => $"Define Named Range '{_name}'";

    public DefineNamedRangeCommand(string name, GridRange range)
    {
        _name = name;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validationError = ctx.Workbook.ValidateNamedRangeName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        ctx.Workbook.DefineNamedRange(_name, _range);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_existed)
            ctx.Workbook.DefineNamedRange(_name, _previousRange);
        else
            ctx.Workbook.RemoveNamedRange(_name);
    }
}

/// <summary>
/// Command to remove a named range from the workbook.
/// Supports undo: restores the range on Revert.
/// </summary>
public sealed class RemoveNamedRangeCommand : IWorkbookCommand
{
    private readonly string _name;
    private GridRange _previousRange;
    private bool _existed;

    public string Label => $"Remove Named Range '{_name}'";

    public RemoveNamedRangeCommand(string name)
    {
        _name = name;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _existed = ctx.Workbook.TryGetNamedRange(_name, out _previousRange);
        if (!_existed)
            return new CommandOutcome(false, $"Named range '{_name}' does not exist.");

        ctx.Workbook.RemoveNamedRange(_name);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_existed)
            ctx.Workbook.DefineNamedRange(_name, _previousRange);
    }
}
