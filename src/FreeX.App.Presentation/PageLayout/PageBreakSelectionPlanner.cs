using System;
using System.Collections.Generic;
using System.Linq;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageBreakAxis
{
    Row,
    Column
}

public static class PageBreakSelectionPlanner
{
    public static PageBreakSelectionPlan Insert(
        GridRange selection,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks) =>
        Create(selection, existingRowBreaks, existingColumnBreaks, addBreak: true);

    public static PageBreakSelectionPlan Remove(
        GridRange selection,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks) =>
        Create(selection, existingRowBreaks, existingColumnBreaks, addBreak: false);

    public static PageBreakSelectionPlan Move(
        PageBreakAxis axis,
        uint originalIndex,
        uint? newIndex,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks)
    {
        ArgumentNullException.ThrowIfNull(existingRowBreaks);
        ArgumentNullException.ThrowIfNull(existingColumnBreaks);

        var rowBreaks = new SortedSet<uint>(existingRowBreaks);
        var columnBreaks = new SortedSet<uint>(existingColumnBreaks);
        var targetBreaks = axis == PageBreakAxis.Row ? rowBreaks : columnBreaks;

        targetBreaks.Remove(originalIndex);
        if (newIndex is { } index)
            targetBreaks.Add(index);

        return new PageBreakSelectionPlan(rowBreaks.ToArray(), columnBreaks.ToArray());
    }

    private static PageBreakSelectionPlan Create(
        GridRange selection,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks,
        bool addBreak)
    {
        var rowBreaks = new SortedSet<uint>(existingRowBreaks);
        var columnBreaks = new SortedSet<uint>(existingColumnBreaks);
        var wholeRows = SelectionRangeService.IsWholeRowSelection(selection);
        var wholeColumns = SelectionRangeService.IsWholeColumnSelection(selection);

        var includeRowBreak = wholeRows || !wholeColumns;
        var includeColumnBreak = wholeColumns || !wholeRows;
        var rowBreak = selection.Start.Row;
        var columnBreak = selection.Start.Col;

        if (includeRowBreak && PageLayoutInputParser.IsValidRowBreak(rowBreak))
            Apply(rowBreaks, rowBreak, addBreak);
        if (includeColumnBreak && PageLayoutInputParser.IsValidColumnBreak(columnBreak))
            Apply(columnBreaks, columnBreak, addBreak);

        return new PageBreakSelectionPlan(rowBreaks.ToArray(), columnBreaks.ToArray());
    }

    private static void Apply(SortedSet<uint> breaks, uint value, bool addBreak)
    {
        if (addBreak)
            breaks.Add(value);
        else
            breaks.Remove(value);
    }
}

public sealed record PageBreakSelectionPlan(
    IReadOnlyList<uint> RowBreaks,
    IReadOnlyList<uint> ColumnBreaks);
