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

    public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(SheetId sheetId, GridRange range)
        => CreateMergeAndCenterCommands(null, sheetId, range, MergeCellContentResolution.KeepFirstCell);

    public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(
        Sheet? sheet,
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution)
    {
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
            return range.CellCount <= 1 ? [] : [new MergeCellsCommand(sheetId, range)];

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

        var entries = new List<MergeCellContentEntry>();
        foreach (var address in range.AllCells())
        {
            if (sheet.GetCell(address) is not { } cell || !HasContent(cell))
                continue;

            entries.Add(new MergeCellContentEntry(
                address,
                FormatDisplayText(cell),
                address == range.Start));
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
