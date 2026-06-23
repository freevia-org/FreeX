using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageBreakDialogAction
{
    Clear,
    AddRow,
    AddColumn
}

public sealed record PageBreakDialogResult(PageBreakDialogAction Action, uint? RowBreak, uint? ColumnBreak);

/// <summary>
/// Portable planning for the page-break dialog contract. Renderers own the radio buttons,
/// text boxes, focus, and validation messages; this class owns the resulting break model.
/// </summary>
public static class PageBreakDialogPlanner
{
    public static PageBreakDialogResult CreateClearResult() =>
        new(PageBreakDialogAction.Clear, null, null);

    public static PageBreakDialogResult CreateRowResult(uint rowBreak) =>
        new(PageBreakDialogAction.AddRow, rowBreak, null);

    public static PageBreakDialogResult CreateColumnResult(uint columnBreak) =>
        new(PageBreakDialogAction.AddColumn, null, columnBreak);

    public static string BuildDefaultInput(GridRange? selectedRange)
    {
        if (selectedRange is null)
            return "row 2";

        var range = selectedRange.Value;
        if (SelectionRangeService.IsWholeColumnSelection(range))
            return $"column {range.Start.Col.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return $"row {range.Start.Row.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public static bool TryCreateResult(string input, out PageBreakDialogResult result)
    {
        result = CreateClearResult();
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return true;

        if (PageLayoutInputParser.TryParseBreakInput(trimmed, "row", out var rowBreak) &&
            PageLayoutInputParser.IsValidRowBreak(rowBreak))
        {
            result = CreateRowResult(rowBreak);
            return true;
        }

        if ((PageLayoutInputParser.TryParseColumnBreakInput(trimmed, "col", out var columnBreak) ||
             PageLayoutInputParser.TryParseColumnBreakInput(trimmed, "column", out columnBreak)) &&
            PageLayoutInputParser.IsValidColumnBreak(columnBreak))
        {
            result = CreateColumnResult(columnBreak);
            return true;
        }

        return false;
    }

    public static bool TryCreateResult(
        PageBreakDialogAction action,
        string rowInput,
        string columnInput,
        out PageBreakDialogResult result)
    {
        result = CreateClearResult();
        return action switch
        {
            PageBreakDialogAction.Clear => true,
            PageBreakDialogAction.AddColumn => TryCreateResult($"column {columnInput}", out result),
            _ => TryCreateResult($"row {rowInput}", out result),
        };
    }

    public static PageBreakSelectionPlan PlanPageBreaks(
        PageBreakDialogResult result,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks)
    {
        if (result.Action == PageBreakDialogAction.Clear)
            return PageLayoutRibbonCommandPlanner.PlanResetPageBreaks();

        var rowBreaks = new SortedSet<uint>(existingRowBreaks);
        var columnBreaks = new SortedSet<uint>(existingColumnBreaks);

        if (result.RowBreak is { } rowBreak)
            rowBreaks.Add(rowBreak);
        if (result.ColumnBreak is { } columnBreak)
            columnBreaks.Add(columnBreak);

        return new PageBreakSelectionPlan(rowBreaks.ToArray(), columnBreaks.ToArray());
    }
}
