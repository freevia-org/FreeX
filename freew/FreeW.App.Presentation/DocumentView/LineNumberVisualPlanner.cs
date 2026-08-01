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
        return Build(lines, [new LineNumberVisualSectionSettings(mode, startAt, countBy)]);
    }

    public static IReadOnlyList<LineNumberVisualPlanItem> Build(
        IReadOnlyList<LineNumberVisualSourceLine> lines,
        IReadOnlyList<LineNumberVisualSectionSettings> sections)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(sections);
        if (lines.Count == 0 || sections.Count == 0 || sections.All(section => section.Mode == LineNumberMode.None))
            return [];

        var currentPage = -1;
        var currentSection = -1;
        var nextNumber = 1;
        var numberingActive = false;
        var result = new List<LineNumberVisualPlanItem>(lines.Count);

        foreach (var line in lines)
        {
            var sectionIndex = Math.Clamp(line.SectionIndex, 0, sections.Count - 1);
            var settings = sections[sectionIndex];
            var sectionChanged = sectionIndex != currentSection;
            var pageChanged = line.PageIndex != currentPage;
            currentSection = sectionIndex;
            currentPage = line.PageIndex;

            if (settings.Mode == LineNumberMode.None)
            {
                numberingActive = false;
                result.Add(new LineNumberVisualPlanItem(line.PageIndex, 0, IsVisible: false));
                continue;
            }

            var firstNumber = Math.Max(1, settings.StartAt);
            var interval = Math.Max(1, settings.CountBy);
            if (!numberingActive
                || (settings.Mode == LineNumberMode.RestartEachSection && sectionChanged)
                || (settings.Mode == LineNumberMode.RestartEachPage && pageChanged))
                nextNumber = firstNumber;

            numberingActive = true;
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

public readonly record struct LineNumberVisualSourceLine(
    int PageIndex,
    bool SuppressNumber,
    int SectionIndex = 0);

public readonly record struct LineNumberVisualSectionSettings(
    LineNumberMode Mode,
    int StartAt,
    int CountBy);

public readonly record struct LineNumberVisualPlanItem(int PageIndex, int Number, bool IsVisible);
