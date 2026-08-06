using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Calculation;

public enum CalculationCommandAction
{
    CalculateNow,
    CalculateFull,
    CalculateActiveSheet
}

public enum CalculationRecalculationScope
{
    None,
    DirtyWorkbook,
    FullWorkbook,
    ActiveSheet
}

[Flags]
public enum CalculationStateRefreshPolicy
{
    None = 0,
    CommandSurface = 1,
    FormulaResults = 2
}

public sealed record CalculationStatusPlan(
    string ResourceKey,
    string? ArgumentResourceKey = null);

public sealed record CalculationModeChangePlan(
    WorkbookCalculationMode RequestedMode,
    IWorkbookCommand? Command,
    CalculationRecalculationScope RecalculationScope,
    CalculationStateRefreshPolicy RefreshPolicy,
    CalculationStatusPlan Status,
    string FailureResourceKey)
{
    public bool IsNoOp => Command is null;
}

public sealed record CalculationCommandActionPlan(
    CalculationCommandAction Action,
    CalculationRecalculationScope RecalculationScope,
    CalculationStateRefreshPolicy RefreshPolicy,
    CalculationStatusPlan Status);

/// <summary>
/// Portable policy for the Formulas/Calculation command surface. Hosts own native menu state,
/// command execution, and repainting; this policy owns mode transitions, recalc scope, feedback
/// resource keys, and which workbook-facing state needs to be refreshed afterward.
/// </summary>
public static class CalculationCommandPolicy
{
    public const string CommandLabel = "Calculation Options";
    public const string FailureResourceKey = "ShellLoc_CouldNotChangeCalcMode";

    public static bool IsManual(WorkbookCalculationMode mode) =>
        mode == WorkbookCalculationMode.Manual;

    public static bool IsSelected(
        WorkbookCalculationMode currentMode,
        WorkbookCalculationMode candidateMode) =>
        currentMode == candidateMode;

    public static WorkbookCalculationMode ToggleTarget(WorkbookCalculationMode currentMode) =>
        IsManual(currentMode)
            ? WorkbookCalculationMode.Automatic
            : WorkbookCalculationMode.Manual;

    public static CalculationModeChangePlan PlanModeChange(
        WorkbookCalculationMode currentMode,
        WorkbookCalculationMode requestedMode)
    {
        var modeResourceKey = ModeDisplayResourceKey(requestedMode);
        if (currentMode == requestedMode)
        {
            return new CalculationModeChangePlan(
                requestedMode,
                Command: null,
                CalculationRecalculationScope.None,
                CalculationStateRefreshPolicy.CommandSurface,
                new CalculationStatusPlan("ShellLoc_CalculationAlreadySet", modeResourceKey),
                FailureResourceKey);
        }

        var recalc = requestedMode is WorkbookCalculationMode.Automatic
            or WorkbookCalculationMode.AutomaticExceptDataTables
                ? CalculationRecalculationScope.FullWorkbook
                : CalculationRecalculationScope.None;
        var refresh = CalculationStateRefreshPolicy.CommandSurface;
        if (recalc != CalculationRecalculationScope.None)
            refresh |= CalculationStateRefreshPolicy.FormulaResults;

        return new CalculationModeChangePlan(
            requestedMode,
            new SetCalculationModeCommand(requestedMode),
            recalc,
            refresh,
            new CalculationStatusPlan("ShellLoc_CalculationSet", modeResourceKey),
            FailureResourceKey);
    }

    public static CalculationCommandActionPlan PlanAction(CalculationCommandAction action) =>
        new(
            action,
            action switch
            {
                CalculationCommandAction.CalculateNow => CalculationRecalculationScope.DirtyWorkbook,
                CalculationCommandAction.CalculateFull => CalculationRecalculationScope.FullWorkbook,
                CalculationCommandAction.CalculateActiveSheet => CalculationRecalculationScope.ActiveSheet,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            },
            CalculationStateRefreshPolicy.CommandSurface | CalculationStateRefreshPolicy.FormulaResults,
            new CalculationStatusPlan("ShellLoc_RecalculatedAllFormulas"));

    public static string ModeDisplayResourceKey(WorkbookCalculationMode mode) =>
        mode switch
        {
            WorkbookCalculationMode.Manual => "ShellLoc_CalcModeManual",
            WorkbookCalculationMode.AutomaticExceptDataTables => "MainWindow_Header_AutomaticExceptDataTables",
            _ => "ShellLoc_CalcModeAutomatic"
        };
}
