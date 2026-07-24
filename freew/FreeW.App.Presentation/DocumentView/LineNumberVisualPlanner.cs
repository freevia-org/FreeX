using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Assigns Word-style page-margin line numbers to already laid-out body lines. Renderers provide
/// physical line order and page ownership; this planner owns the sequence, restart and suppression rules.
/// </summary>
public static class LineNumberVisualPlanner
{
    public static IReadOnlyList<LineNumberVisualPlanItem> Build(
        LineNumberMode mode,
        int startAt,
        int countBy,
        IReadOnlyList<LineNumberVisualSourceLine> lines)
    {
        if (mode == LineNumberMode.None || lines.Count == 0)
            return [];

        var firstNumber = Math.Max(1, startAt);
        var interval = Math.Max(1, countBy);
        var currentPage = -1;
        var nextNumber = firstNumber;
        var result = new List<LineNumberVisualPlanItem>(lines.Count);

        foreach (var line in lines)
        {
            if (line.PageIndex != currentPage)
            {
                currentPage = line.PageIndex;
                if (mode == LineNumberMode.RestartEachPage)
                    nextNumber = firstNumber;
            }

            var number = nextNumber++;
            var isIntervalLine = (number - firstNumber) % interval == 0;
            result.Add(new LineNumberVisualPlanItem(
                line.PageIndex,
                number,
                IsVisible: isIntervalLine && !line.SuppressNumber));
        }

        return result;
    }
}

public readonly record struct LineNumberVisualSourceLine(int PageIndex, bool SuppressNumber);

public readonly record struct LineNumberVisualPlanItem(int PageIndex, int Number, bool IsVisible);
