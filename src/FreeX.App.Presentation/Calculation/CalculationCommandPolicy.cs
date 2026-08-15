using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Ribbon;

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

public sealed record IterativeCalculationChangePlan(
    IWorkbookCommand? Command,
    CalculationRecalculationScope RecalculationScope,
    string FailureResourceKey)
{
    public bool IsNoOp => Command is null;
}

/// <summary>
/// Portable policy for the Formulas/Calculation command surface. Hosts own native menu state,
/// command execution, and repainting; this policy owns mode transitions, recalc scope, feedback
/// resource keys, and which workbook-facing state needs to be refreshed afterward.
/// </summary>
public static class CalculationCommandPolicy
{
    public const string CommandLabel = "Calculation Options";
    public const string FormulaErrorRulesCommandLabel = "Error Checking Options";
    public const string FailureResourceKey = "ShellLoc_CouldNotChangeCalcMode";
    public const int DefaultMaxCalculationIterations = 100;
    public const double DefaultMaxCalculationChange = 0.001;

    public static bool IsManual(WorkbookCalculationMode mode) =>
        mode == WorkbookCalculationMode.Manual;

    public static bool IsSelected(
        WorkbookCalculationMode currentMode,
        WorkbookCalculationMode candidateMode) =>
        currentMode == candidateMode;

    public static RibbonCommandState ModeCommandState(
        WorkbookCalculationMode currentMode,
        WorkbookCalculationMode candidateMode) =>
        new(IsChecked: IsSelected(currentMode, candidateMode));

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

    public static IReadOnlyList<IWorkbookCommand> PlanFormulaErrorRuleChanges(
        IEnumerable<string> currentDisabledErrorCodes,
        IEnumerable<string> requestedDisabledErrorCodes)
    {
        var current = new HashSet<string>(currentDisabledErrorCodes, StringComparer.OrdinalIgnoreCase);
        var requested = new HashSet<string>(requestedDisabledErrorCodes, StringComparer.OrdinalIgnoreCase);

        return FormulaErrorCheckingRuleCatalog.SupportedRules
            .Where(rule => current.Contains(rule.ErrorCode) != requested.Contains(rule.ErrorCode))
            .Select(rule => (IWorkbookCommand)new SetFormulaErrorCheckingRuleCommand(
                rule.ErrorCode,
                enabled: !requested.Contains(rule.ErrorCode)))
            .ToList();
    }

    public static IterativeCalculationChangePlan PlanIterativeCalculationChange(
        bool currentEnabled,
        int? currentMaxIterations,
        double? currentMaxChange,
        bool requestedEnabled,
        int? requestedMaxIterations,
        double? requestedMaxChange)
    {
        var unchanged = currentEnabled == requestedEnabled &&
                        (currentMaxIterations ?? DefaultMaxCalculationIterations) ==
                        (requestedMaxIterations ?? DefaultMaxCalculationIterations) &&
                        (currentMaxChange ?? DefaultMaxCalculationChange) ==
                        (requestedMaxChange ?? DefaultMaxCalculationChange);

        return new IterativeCalculationChangePlan(
            unchanged
                ? null
                : new SetIterativeCalculationOptionsCommand(
                    requestedEnabled,
                    requestedMaxIterations,
                    requestedMaxChange),
            unchanged
                ? CalculationRecalculationScope.None
                : CalculationRecalculationScope.FullWorkbook,
            FailureResourceKey);
    }

    public static string ModeDisplayResourceKey(WorkbookCalculationMode mode) =>
        mode switch
        {
            WorkbookCalculationMode.Manual => "ShellLoc_CalcModeManual",
            WorkbookCalculationMode.AutomaticExceptDataTables => "MainWindow_Header_AutomaticExceptDataTables",
            _ => "ShellLoc_CalcModeAutomatic"
        };
}
