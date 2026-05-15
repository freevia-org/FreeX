using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Stores the workbook-window arrangement requested from the View tab.</summary>
public sealed class SetWorkbookWindowArrangementCommand : IWorkbookCommand
{
    private readonly WorkbookWindowArrangement _arrangement;
    private WorkbookWindowArrangement _previousArrangement;

    public string Label => "Arrange Windows";

    public SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement arrangement)
    {
        _arrangement = arrangement;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_arrangement))
            return new CommandOutcome(false, "Window arrangement is not supported.");

        _previousArrangement = ctx.Workbook.WindowArrangement;
        ctx.Workbook.WindowArrangement = _arrangement;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.WindowArrangement = _previousArrangement;
    }
}
