using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A conservative, local workbook-performance scan. It reports only formatting-only cells that
/// extend a worksheet's used range; it does not change workbook content or formatting.
/// </summary>
public sealed record WorkbookPerformanceIssue(
    SheetId SheetId,
    string SheetName,
    GridRange FormattingRange,
    GridRange? ContentRange,
    int FormattingOnlyCellCount);

public sealed record WorkbookPerformanceReport(
    IReadOnlyList<WorkbookPerformanceIssue> Issues)
{
    public bool HasIssues => Issues.Count != 0;
}

public static class WorkbookPerformanceService
{
    public static WorkbookPerformanceReport Analyze(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var issues = new List<WorkbookPerformanceIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasStyleOnlyCells)
                continue;

            var contentRange = sheet.GetContentUsedRange();
            var formattingRange = sheet.GetUsedRange();
            if (formattingRange is null || formattingRange == contentRange)
                continue;

            issues.Add(new WorkbookPerformanceIssue(
                sheet.Id,
                sheet.Name,
                formattingRange.Value,
                contentRange,
                sheet.StyleOnlyCellCount));
        }

        return new WorkbookPerformanceReport(issues);
    }
}
