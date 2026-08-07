using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.Services;

public enum MergeCellContentResolution
{
    KeepFirstCell,
    ConcatenateAllCells
}

public sealed record MergeCellContentEntry(CellAddress Address, string DisplayText, bool IsTopLeft);

public sealed record MergeCellContentPlan(
    bool WouldLoseContent,
    IReadOnlyList<MergeCellContentEntry> Entries,
    string ConcatenatedText);

public static class CellMergePlanner
{
    public static bool IsSelectionMerged(Sheet sheet, GridRange range) =>
        sheet.MergedRegions.Any(region => region.Overlaps(range));

    /// <summary>
    /// True if any cell in <paramref name="range"/> currently holds a live dynamic-array spill value
    /// (i.e. a non-anchor cell written by another formula's <c>SetSpillRange</c>). Excel blocks merging
    /// over a spilled array outright rather than silently absorbing the spilled values, so callers use
    /// this to reject the merge instead of treating it as ordinary "would lose content".
    /// </summary>
    public static bool HasLiveSpillTarget(Sheet sheet, GridRange range) =>
        sheet.EnumerateSpillTargetCells().Any(range.Contains);

    public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(SheetId sheetId, GridRange range)
        => CreateMergeAndCenterCommands(null, sheetId, range, MergeCellContentResolution.KeepFirstCell);

    public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(
        Sheet? sheet,
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution,
        bool allowUnmergeToggle = true)
    {
        if (sheet is not null && HasLiveSpillTarget(sheet, range))
            return [RejectSpillOverlapCommand.Instance];

        // Excel's documented "Merge & Center" toggle gesture: selecting an already-merged cell (or any
        // selection that is entirely covered by one existing merged region — e.g. a single-cell selection
        // sitting inside a bigger merge) and clicking Merge & Center again unmerges the whole covering
        // region, rather than failing with the "Range overlaps an existing merged region" conflict error
        // that MergeCellsCommand raises for a genuine overlapping-merge request. A selection that only
        // partially overlaps an existing region (straddles its boundary without being fully covered by
        // it) is still a real conflict and falls through unchanged to the normal merge path below.
        //
        // "Merge Across" is different (see CreateMergeCommands' allowUnmergeToggle remarks): its per-row
        // batch passes allowUnmergeToggle: false through CreateFormatCellsMergeCommands' ConcatenateAllCells
        // branch so an already-merged row of the exact target shape is left merged (falls through to the
        // MergeCellsCommand re-merge below) instead of being toggled back to unmerged (R87-commands-merge-
        // cells-5-1: this branch used to ignore the flag entirely and always applied the toggle).
        if (allowUnmergeToggle && sheet is not null && FindCoveringRegion(sheet, range) is { } toggleRegion)
            return [new UnmergeCellsCommand(sheetId, toggleRegion)];

        var commands = new List<IWorkbookCommand>();
        if (contentResolution == MergeCellContentResolution.ConcatenateAllCells && sheet is not null)
        {
            var contentPlan = AnalyzeContent(sheet, range);
            if (!string.IsNullOrEmpty(contentPlan.ConcatenatedText))
                commands.Add(EditCellsCommand.ForValue(sheetId, range.Start, new TextValue(contentPlan.ConcatenatedText)));
        }

        if (range.CellCount > 1)
            commands.Add(new MergeCellsCommand(sheetId, range));

        commands.Add(new ApplyStyleCommand(sheetId, range, new StyleDiff(HAlign: HorizontalAlignment.Center)));
        return commands;
    }

    public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells,
        bool allowUnmergeToggle = true)
    {
        if (mergeCells)
        {
            if (HasLiveSpillTarget(sheet, range))
                return [RejectSpillOverlapCommand.Instance];

            // Same "Merge & Center" toggle gesture (see CreateMergeAndCenterCommands above): re-invoking
            // "Merge Cells" on a selection that is already fully covered by an existing merged region
            // unmerges it, instead of falling through to MergeCellsCommand and failing with "Range
            // overlaps an existing merged region.", matching real Excel where the direct Merge Cells /
            // Merge & Center gesture toggles the merged/unmerged state.
            //
            // "Merge Across" is different: Excel always leaves the selection UNIFORMLY merged per row,
            // even when a mixed-state selection already has some rows correctly merged -- it never
            // toggles an already-merged row back off just because the per-row loop happens to re-invoke
            // this method for that row too. Callers driving that per-row batch pass
            // allowUnmergeToggle: false so an already-merged row of the exact target shape falls through
            // to MergeCellsCommand instead, which absorbs the identical-shape existing region and
            // re-adds it -- i.e. the row is left merged (a no-op re-merge), never split back apart.
            if (allowUnmergeToggle && FindCoveringRegion(sheet, range) is { } toggleRegion)
                return [new UnmergeCellsCommand(sheetId, toggleRegion)];

            return range.CellCount <= 1 ? [] : [new MergeCellsCommand(sheetId, range)];
        }

        return CreateUnmergeCommands(sheet, sheetId, range);
    }

    /// <summary>
    /// Finds the existing merged region (if any) that fully covers <paramref name="range"/> -- i.e. the
    /// selection is entirely inside one existing merge (including the degenerate case where the
    /// selection IS that merge). Used by the merge commands' Excel-parity toggle-to-unmerge gesture.
    /// Public so callers that need to decide UI messaging (e.g. whether a Merge &amp; Center click is
    /// about to toggle to an unmerge) ahead of dispatching the actual command can ask the same question
    /// this class already answers internally, instead of re-deriving it (or approximating it with the
    /// looser "any overlap" <see cref="IsSelectionMerged"/> check, which wrongly matches a selection that
    /// only partially straddles a merge).
    /// </summary>
    public static GridRange? FindCoveringRegion(Sheet sheet, GridRange range)
    {
        foreach (var region in sheet.MergedRegions)
        {
            if (region.Contains(range))
                return region;
        }

        return null;
    }

    public static IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell,
        bool allowUnmergeToggle = true)
    {
        if (mergeCells)
        {
            if (HasLiveSpillTarget(sheet, range))
                return [RejectSpillOverlapCommand.Instance];

            return contentResolution == MergeCellContentResolution.ConcatenateAllCells
                ? CreateMergeAndCenterCommands(sheet, sheetId, range, contentResolution, allowUnmergeToggle)
                    .Where(command => command is not ApplyStyleCommand)
                    .ToList()
                : CreateMergeCommands(sheet, sheetId, range, mergeCells: true, allowUnmergeToggle);
        }

        return CreateMergeCommands(sheet, sheetId, range, mergeCells: false, allowUnmergeToggle);
    }

    public static IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(Sheet sheet, SheetId sheetId, GridRange range) =>
        sheet.MergedRegions
            .Where(region => region.Overlaps(range))
            .Select(region => (IWorkbookCommand)new UnmergeCellsCommand(sheetId, region))
            .ToList();

    public static MergeCellContentPlan AnalyzeContent(Sheet sheet, GridRange range) =>
        AnalyzeContent(sheet, range, perRow: false);

    /// <summary>
    /// Multi-area overload: analyzes EVERY disjoint area a Ctrl+click multi-area selection's merge will
    /// actually touch, in one pass, so the "merging cells can discard cell contents" warning fires
    /// whenever ANY area -- not just the active one -- would lose content. Both shells (Avalonia's
    /// MainWindow.MergePaste.cs/MainWindow.cs and the WPF host's MainWindow.HomeFormatting.cs) execute
    /// their merges per-area via <see cref="SelectionStyleCommandPlanner.ResolveRanges"/>/
    /// GetCurrentSelectionRanges; this overload is the matching multi-area choke point for the
    /// pre-execution content analysis, so the two can never drift out of sync the way the single-range
    /// <see cref="AnalyzeContent(Sheet, GridRange, bool)"/> overload alone did (R127 fixed the multi-area
    /// EXECUTION but left this analysis on the active range only -- a silent data-loss regression for any
    /// non-active area).
    /// </summary>
    public static MergeCellContentPlan AnalyzeContent(Sheet sheet, IReadOnlyList<GridRange> ranges, bool perRow = false)
    {
        if (ranges.Count == 0)
            return new MergeCellContentPlan(false, [], "");

        if (ranges.Count == 1)
            return AnalyzeContent(sheet, ranges[0], perRow);

        var entries = new List<MergeCellContentEntry>();
        foreach (var range in ranges)
            entries.AddRange(AnalyzeContent(sheet, range, perRow).Entries);

        return new MergeCellContentPlan(
            entries.Any(entry => !entry.IsTopLeft),
            entries,
            string.Join(" ", entries.Select(entry => entry.DisplayText).Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    /// <summary>
    /// Analyzes the given range for the "merging cells can discard cell contents" warning.
    /// </summary>
    /// <param name="perRow">
    /// When <c>false</c> (the direct Merge Cells / Merge &amp; Center gesture, which folds the whole
    /// <paramref name="range"/> into ONE merged cell), only <c>range.Start</c> itself -- the single
    /// surviving top-left cell -- is exempt from the content-loss check. When <c>true</c> (a Merge
    /// Across batch, which merges each row of <paramref name="range"/> independently), every row's own
    /// leftmost cell -- i.e. each address sharing <c>range.Start.Col</c> -- is exempt, since that
    /// column is the top-left of ITS row's eventual per-row merge and loses nothing.
    /// </param>
    public static MergeCellContentPlan AnalyzeContent(Sheet sheet, GridRange range, bool perRow)
    {
        if (range.CellCount <= 1)
            return new MergeCellContentPlan(false, [], "");

        // GetCell only looks at authored cells (_cells) — a cell that is merely the target of a
        // neighboring formula's dynamic-array spill (Sheet._spillValues) has no entry there, so it
        // would otherwise be invisible to the loss-of-content check below even though it holds a live
        // value. Collect the spill-target addresses inside the range up front so they're surfaced too
        // (display their current spilled value), same as any other cell with content.
        var spillTargetsInRange = new HashSet<CellAddress>(
            sheet.EnumerateSpillTargetCells().Where(range.Contains));

        var entries = new List<MergeCellContentEntry>();
        foreach (var address in range.AllCells())
        {
            var isTopLeft = perRow ? address.Col == range.Start.Col : address == range.Start;
            if (sheet.GetCell(address) is { } cell && HasContent(cell))
            {
                entries.Add(new MergeCellContentEntry(
                    address,
                    FormatDisplayText(cell),
                    isTopLeft));
            }
            else if (spillTargetsInRange.Contains(address))
            {
                entries.Add(new MergeCellContentEntry(
                    address,
                    FormatScalarValue(sheet.GetValue(address)),
                    isTopLeft));
            }
        }

        var wouldLoseContent = entries.Any(entry => !entry.IsTopLeft);
        return new MergeCellContentPlan(
            wouldLoseContent,
            entries,
            string.Join(" ", entries.Select(entry => entry.DisplayText).Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private static bool HasContent(Cell cell) =>
        cell.HasFormula || cell.Value is not BlankValue;

    private static string FormatDisplayText(Cell cell)
    {
        if (cell.Value is not BlankValue)
            return FormatScalarValue(cell.Value);

        return cell.FormulaText is { Length: > 0 } formula
            ? "=" + formula
            : "";
    }

    private static string FormatScalarValue(ScalarValue value) => value switch
    {
        BlankValue => "",
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => value.ToString() ?? ""
    };
}

/// <summary>
/// A no-op command that always fails with a clear error message. Returned in place of the real merge
/// command(s) when the requested merge range overlaps a live dynamic-array spill range — Excel rejects
/// merging over a spilled array outright ("You can't merge cells that contain part of another merged
/// cell" / spill equivalent) rather than silently absorbing the spilled values. Implementing this as its
/// own command (instead of throwing from a factory method) lets it flow through the normal
/// CommandBus/CompositeWorkbookCommand.Apply failure path, so the caller gets the same
/// CommandOutcome(false, ErrorMessage) shape as any other rejected command and nothing is left applied.
/// </summary>
public sealed class RejectSpillOverlapCommand : IWorkbookCommand
{
    public static readonly RejectSpillOverlapCommand Instance = new();

    public string Label => "Merge Cells";

    public CommandOutcome Apply(ICommandContext ctx) =>
        new(false, "Can't merge cells that overlap a dynamic array's spill range.");

    public void Revert(ICommandContext ctx) { }
}

/// <summary>
/// A command that genuinely does nothing and reports itself as a no-op. Returned in place of the real
/// unmerge command(s) when the requested range overlaps no merged region at all (e.g. "Unmerge Cells" run
/// over a plain, never-merged selection) — matching Excel, which leaves the workbook and undo history
/// untouched rather than recording a phantom edit. Callers that must hand a concrete
/// <see cref="IWorkbookCommand"/> to a factory-shaped API (one command per sheet) use this instead of
/// silently building a command whose Apply would otherwise report success without changing anything;
/// CommandBus skips the undo stack for Success+IsNoOp outcomes, so nothing is pushed.
/// </summary>
public sealed class NoOpWorkbookCommand : IWorkbookCommand
{
    public static readonly NoOpWorkbookCommand Instance = new();

    public string Label => "";

    public CommandOutcome Apply(ICommandContext ctx) => new(true, IsNoOp: true);

    public void Revert(ICommandContext ctx) { }
}
