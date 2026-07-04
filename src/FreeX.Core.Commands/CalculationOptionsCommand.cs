using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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

/// <summary>Sets workbook iterative-calculation settings (enable, max iterations, max change) with undo support.</summary>
public sealed class SetIterativeCalculationOptionsCommand : IWorkbookCommand
{
    private readonly bool _enabled;
    private readonly int? _maxIterations;
    private readonly double? _maxChange;
    private bool _previousEnabled;
    private int? _previousMaxIterations;
    private double? _previousMaxChange;

    public string Label => "Calculation Options";

    public SetIterativeCalculationOptionsCommand(bool enabled, int? maxIterations, double? maxChange)
    {
        _enabled = enabled;
        _maxIterations = maxIterations;
        _maxChange = maxChange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_maxIterations is { } iterations && iterations <= 0)
            return new CommandOutcome(false, "Maximum iterations must be greater than zero.");

        if (_maxChange is { } change && change < 0)
            return new CommandOutcome(false, "Maximum change must not be negative.");

        _previousEnabled = ctx.Workbook.IterativeCalculation;
        _previousMaxIterations = ctx.Workbook.MaxCalculationIterations;
        _previousMaxChange = ctx.Workbook.MaxCalculationChange;

        ctx.Workbook.IterativeCalculation = _enabled;
        ctx.Workbook.MaxCalculationIterations = _maxIterations;
        ctx.Workbook.MaxCalculationChange = _maxChange;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.IterativeCalculation = _previousEnabled;
        ctx.Workbook.MaxCalculationIterations = _previousMaxIterations;
        ctx.Workbook.MaxCalculationChange = _previousMaxChange;
    }
}
