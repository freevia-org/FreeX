using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets workbook calculation mode with undo support.</summary>
public sealed class SetCalculationModeCommand : IWorkbookCommand
{
    private readonly WorkbookCalculationMode _mode;
    private WorkbookCalculationMode _previousMode;

    public string Label => "Calculation Options";

    public SetCalculationModeCommand(WorkbookCalculationMode mode)
    {
        _mode = mode;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_mode))
            return new CommandOutcome(false, "Calculation mode is not supported.");

        _previousMode = ctx.Workbook.CalculationMode;
        ctx.Workbook.CalculationMode = _mode;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.CalculationMode = _previousMode;
    }
}
