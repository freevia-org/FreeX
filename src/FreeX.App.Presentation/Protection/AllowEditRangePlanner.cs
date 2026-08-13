using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>The action the Allow-Edit-Range dialog resolved to when it closed.</summary>
public enum AllowEditRangeAction
{
    Add,
    Modify,
    Remove,
    Clear,
}

/// <summary>
/// The outcome of an Allow-Edit-Range dialog: the action plus the affected range(s). For
/// <see cref="AllowEditRangeAction.Modify"/> the <see cref="PreviousRange"/> is the range that was replaced.
/// </summary>
public sealed record AllowEditRangeResult(
    AllowEditRangeAction Action,
    GridRange? Range,
    GridRange? PreviousRange = null);

/// <summary>Which list buttons are enabled given the current selection in the Allow-Edit-Range dialog.</summary>
public sealed record AllowEditRangeButtonState(
    bool CanModifySelectedRange,
    bool CanDeleteSelectedRange,
    bool CanUsePermissions);

public sealed record AllowEditRangeCommandPlan(
    AllowEditRangeAction Action,
    GridRange? Range,
    IWorkbookCommand Command);

/// <summary>
/// Portable (no UI) backing logic for the Allow Users to Edit Ranges dialog (Review ▸ Protect). It parses a
/// typed range against the active sheet, projects the sheet's stored allowed-edit ranges into display rows,
/// derives the list-button enablement, and builds the dialog result records the shell maps onto the Core
/// allow-edit-range commands. Kept UI-free so any desktop or cross-platform shell can reuse it and so it is
/// unit-testable without a window.
/// </summary>
public static class AllowEditRangePlanner
{
    /// <summary>
    /// Parses a typed cell/range reference (e.g. <c>A1:B5</c>) against <paramref name="sheetId"/>. Returns
    /// false when the text is not a valid range.
    /// </summary>
    public static bool TryParseRange(string? text, SheetId sheetId, out GridRange range)
    {
        try
        {
            range = GridRange.ParseCellOrRange((text ?? string.Empty).Trim(), sheetId);
            return true;
        }
        catch
        {
            range = default;
            return false;
        }
    }

    /// <summary>Projects the sheet's stored allowed-edit ranges into display strings (A1 form, in stored order).</summary>
    public static IReadOnlyList<string> BuildExistingRangeItems(IReadOnlyList<GridRange>? existingRanges) =>
        existingRanges?.Select(range => range.ToString()).ToList() ?? [];

    /// <summary>Derives the list-button enablement from the current range count and selection.</summary>
    public static AllowEditRangeButtonState BuildButtonState(int rangeCount, bool hasSelectedRange)
    {
        var hasRanges = rangeCount > 0;
        return new AllowEditRangeButtonState(
            CanModifySelectedRange: hasRanges && hasSelectedRange,
            CanDeleteSelectedRange: hasRanges && hasSelectedRange,
            CanUsePermissions: false);
    }

    public static AllowEditRangeResult CreateAddResult(GridRange range) =>
        new(AllowEditRangeAction.Add, range);

    public static AllowEditRangeResult CreateModifyResult(GridRange originalRange, GridRange updatedRange) =>
        new(AllowEditRangeAction.Modify, updatedRange, originalRange);

    public static AllowEditRangeResult CreateRemoveResult(GridRange range) =>
        new(AllowEditRangeAction.Remove, range);

    public static AllowEditRangeResult CreateClearResult() =>
        new(AllowEditRangeAction.Clear, null);

    public static AllowEditRangeCommandPlan? CreateCommandPlan(
        SheetId sheetId,
        AllowEditRangeResult result,
        string? password,
        bool passwordChanged,
        IReadOnlyDictionary<GridRange, string?>? existingPasswords = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        existingPasswords ??= new Dictionary<GridRange, string?>();

        switch (result)
        {
            case { Action: AllowEditRangeAction.Add, Range: { } range }:
                return new AllowEditRangeCommandPlan(
                    result.Action,
                    range,
                    new CompositeWorkbookCommand(
                        "Allow Edit Range",
                        [
                            new AllowEditRangeCommand(sheetId, range),
                            new SetAllowEditRangePasswordCommand(sheetId, range, password)
                        ]));

            case { Action: AllowEditRangeAction.Modify, PreviousRange: { } previousRange, Range: { } range }:
                var modifyCommands = new List<IWorkbookCommand>
                {
                    new RemoveAllowEditRangeCommand(sheetId, previousRange)
                };
                if (range != previousRange)
                    modifyCommands.Add(new SetAllowEditRangePasswordCommand(sheetId, previousRange, null));
                modifyCommands.Add(new AllowEditRangeCommand(sheetId, range));
                if (passwordChanged)
                {
                    modifyCommands.Add(new SetAllowEditRangePasswordCommand(sheetId, range, password));
                }
                else if (range != previousRange && existingPasswords.TryGetValue(previousRange, out var carriedPassword))
                {
                    modifyCommands.Add(new SetAllowEditRangePasswordCommand(sheetId, range, carriedPassword));
                }

                return new AllowEditRangeCommandPlan(
                    result.Action,
                    range,
                    new CompositeWorkbookCommand("Modify Allow Edit Range", modifyCommands));

            case { Action: AllowEditRangeAction.Remove, Range: { } range }:
                return new AllowEditRangeCommandPlan(
                    result.Action,
                    range,
                    new CompositeWorkbookCommand(
                        "Remove Allow Edit Range",
                        [
                            new RemoveAllowEditRangeCommand(sheetId, range),
                            new SetAllowEditRangePasswordCommand(sheetId, range, null)
                        ]));

            case { Action: AllowEditRangeAction.Clear }:
                return new AllowEditRangeCommandPlan(
                    result.Action,
                    null,
                    new ClearAllowEditRangesCommand(sheetId));

            default:
                return null;
        }
    }
}
