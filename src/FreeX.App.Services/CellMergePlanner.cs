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
        MergeCellContentResolution contentResolution)
    {
        if (sheet is not null && HasLiveSpillTarget(sheet, range))
            return [RejectSpillOverlapCommand.Instance];

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
        bool mergeCells)
    {
        if (mergeCells)
        {
            if (HasLiveSpillTarget(sheet, range))
                return [RejectSpillOverlapCommand.Instance];

            return range.CellCount <= 1 ? [] : [new MergeCellsCommand(sheetId, range)];
        }

        return CreateUnmergeCommands(sheet, sheetId, range);
    }

    public static IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        if (mergeCells)
        {
            if (HasLiveSpillTarget(sheet, range))
                return [RejectSpillOverlapCommand.Instance];

            return contentResolution == MergeCellContentResolution.ConcatenateAllCells
                ? CreateMergeAndCenterCommands(sheet, sheetId, range, contentResolution)
                    .Where(command => command is not ApplyStyleCommand)
                    .ToList()
                : CreateMergeCommands(sheet, sheetId, range, mergeCells: true);
        }

        return CreateMergeCommands(sheet, sheetId, range, mergeCells: false);
    }

    public static IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(Sheet sheet, SheetId sheetId, GridRange range) =>
        sheet.MergedRegions
            .Where(region => region.Overlaps(range))
            .Select(region => (IWorkbookCommand)new UnmergeCellsCommand(sheetId, region))
            .ToList();

    public static MergeCellContentPlan AnalyzeContent(Sheet sheet, GridRange range)
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
            if (sheet.GetCell(address) is { } cell && HasContent(cell))
            {
                entries.Add(new MergeCellContentEntry(
                    address,
                    FormatDisplayText(cell),
                    address == range.Start));
            }
            else if (spillTargetsInRange.Contains(address))
            {
                entries.Add(new MergeCellContentEntry(
                    address,
                    FormatScalarValue(sheet.GetValue(address)),
                    address == range.Start));
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
