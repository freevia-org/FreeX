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

/// <summary>
/// Portable (no UI) backing logic for the Allow Users to Edit Ranges dialog (Review ▸ Protect). It parses a
/// typed range against the active sheet, projects the sheet's stored allowed-edit ranges into display rows,
/// derives the list-button enablement, and builds the dialog result records the shell maps onto the Core
/// allow-edit-range commands. Kept UI-free so any shell (WPF, Avalonia) can reuse it and so it is
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
            range = GridRange.Parse((text ?? string.Empty).Trim(), sheetId);
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
}
