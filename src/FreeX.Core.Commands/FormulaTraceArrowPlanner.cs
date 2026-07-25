using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class FormulaTraceArrowPlanner
{
    public static IReadOnlyList<FormulaTraceArrow> GetNextPrecedentTraceArrows(
        Workbook workbook,
        CellAddress activeCell,
        IReadOnlyCollection<FormulaTraceArrow> existingArrows)
    {
        var relevantArrows = existingArrows
            .Where(arrow => arrow.Kind == FormulaTraceArrowKind.Precedent)
            .ToArray();
        var knownArrows = new HashSet<FormulaTraceArrow>(relevantArrows);
        var result = new List<FormulaTraceArrow>();

        foreach (var target in GetTraceFrontier(activeCell, relevantArrows, expandPrecedents: true))
        {
            // Use the region form so a multi-cell range precedent (e.g. =SUM(A1:A20)) collapses
            // into a single arrow anchored at the range's top-left cell instead of one arrow per
            // cell in the range, matching Excel's single arrow-to-a-box display
            // (R88-app-formula-auditing-5-3).
            foreach (var region in FormulaAuditingService.GetDirectPrecedentRegions(workbook, target))
            {
                var arrow = new FormulaTraceArrow(region.Start, target, FormulaTraceArrowKind.Precedent);
                if (knownArrows.Add(arrow))
                    result.Add(arrow);
            }
        }

        return result;
    }

    public static IReadOnlyList<FormulaTraceArrow> GetNextDependentTraceArrows(
        Workbook workbook,
        CellAddress activeCell,
        IReadOnlyCollection<FormulaTraceArrow> existingArrows)
    {
        var relevantArrows = existingArrows
            .Where(arrow => arrow.Kind == FormulaTraceArrowKind.Dependent)
            .ToArray();
        var knownArrows = new HashSet<FormulaTraceArrow>(relevantArrows);
        var result = new List<FormulaTraceArrow>();

        foreach (var source in GetTraceFrontier(activeCell, relevantArrows, expandPrecedents: false))
        {
            foreach (var dependent in FormulaAuditingService.GetDirectDependents(workbook, source))
            {
                var arrow = new FormulaTraceArrow(source, dependent, FormulaTraceArrowKind.Dependent);
                if (knownArrows.Add(arrow))
                    result.Add(arrow);
            }
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> GetTraceFrontier(
        CellAddress root,
        IReadOnlyCollection<FormulaTraceArrow> existingArrows,
        bool expandPrecedents)
    {
        if (existingArrows.Count == 0)
            return [root];

        var links = BuildTraceLinks(existingArrows, expandPrecedents);
        var visited = new HashSet<CellAddress> { root };
        var stack = new Stack<CellAddress>();
        var frontier = new List<CellAddress>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!links.TryGetValue(current, out var nextCells) || nextCells.Count == 0)
            {
                frontier.Add(current);
                continue;
            }

            foreach (var next in nextCells)
            {
                if (visited.Add(next))
                    stack.Push(next);
            }
        }

        return frontier;
    }

    private static Dictionary<CellAddress, List<CellAddress>> BuildTraceLinks(
        IReadOnlyCollection<FormulaTraceArrow> existingArrows,
        bool expandPrecedents)
    {
        var links = new Dictionary<CellAddress, List<CellAddress>>();
        foreach (var arrow in existingArrows)
        {
            var from = expandPrecedents ? arrow.To : arrow.From;
            var to = expandPrecedents ? arrow.From : arrow.To;

            if (!links.TryGetValue(from, out var targets))
            {
                targets = [];
                links.Add(from, targets);
            }

            targets.Add(to);
        }

        return links;
    }
}
