using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>Where a chart should land when moved: as a floating object in an existing sheet, or onto a brand-new sheet.</summary>
public enum ChartMoveTargetKind
{
    /// <summary>Keep the chart as a floating drawing object, moved into the named existing sheet.</summary>
    ObjectInSheet,

    /// <summary>Move the chart onto a brand-new sheet created with the given name.</summary>
    NewSheet,
}

/// <summary>The move choice collected from the dialog: the target kind plus the (trimmed) sheet name to move into / create.</summary>
public readonly record struct ChartMoveInput(ChartMoveTargetKind TargetKind, string TargetName);

/// <summary>
/// The validated outcome of a "Move Chart" request: either a resolved move (kind + cleaned name) or an
/// English reason it was rejected. The shell dispatches <see cref="MoveChartToNewSheetCommand"/> (new sheet)
/// or <see cref="MoveChartCommand"/> (existing sheet, resolved by name) when <see cref="IsValid"/> is true.
/// </summary>
public readonly record struct ChartMovePlan(ChartMoveTargetKind TargetKind, string TargetName, string? Error)
{
    /// <summary>True when the move should be dispatched (no validation error).</summary>
    public bool IsValid => Error is null;
}

/// <summary>
/// Portable (no UI) planner for the "Move Chart" dialog. Single-sources the target-name validation the
/// classic Excel move dialog enforces (non-empty; for an existing-sheet target the name must resolve to a
/// real sheet) before the shell dispatches the Core move commands. Core re-validates inside the commands
/// (sheet-name rules, pivot-chart guard, protection); this planner keeps the dialog honest and produces a
/// clean trimmed name. Reused across every shell.
/// </summary>
public static class ChartMovePlanner
{
    /// <summary>The dialog's default: keep the chart as an object in the sheet it currently lives on.</summary>
    public static ChartMoveInput DefaultFor(string currentSheetName) =>
        new(ChartMoveTargetKind.ObjectInSheet, (currentSheetName ?? string.Empty).Trim());

    /// <summary>
    /// Validates a move request. <paramref name="sheetNameExists"/> reports whether a candidate name
    /// resolves to a real sheet (only consulted for an existing-sheet target). Returns a plan with the
    /// trimmed name, or an English error when the name is blank / does not resolve.
    /// </summary>
    public static ChartMovePlan Plan(ChartMoveInput input, Func<string, bool> sheetNameExists)
    {
        ArgumentNullException.ThrowIfNull(sheetNameExists);

        var name = (input.TargetName ?? string.Empty).Trim();
        if (name.Length == 0)
            return new ChartMovePlan(input.TargetKind, name, "Enter a name for the destination sheet.");

        if (input.TargetKind == ChartMoveTargetKind.ObjectInSheet && !sheetNameExists(name))
            return new ChartMovePlan(input.TargetKind, name, $"There is no sheet named '{name}'.");

        return new ChartMovePlan(input.TargetKind, name, null);
    }
}
