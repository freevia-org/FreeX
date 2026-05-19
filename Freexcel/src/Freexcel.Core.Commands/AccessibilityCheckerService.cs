using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum AccessibilityIssueKind
{
    MergedCells,
    MissingAltText,
    ChartMissingTitle
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static class AccessibilityCheckerService
{
    public static IReadOnlyList<AccessibilityIssue> FindIssues(Workbook workbook)
    {
        var issues = new List<AccessibilityIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var range in sheet.MergedRegions)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.MergedCells,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(range),
                    "Merged cells can make worksheet navigation harder for assistive technologies."));
            }

            foreach (var picture in sheet.Pictures.Where(p => string.IsNullOrWhiteSpace(p.AltText)))
                issues.Add(MissingAltText(sheet, picture.Anchor, "Picture"));

            foreach (var shape in sheet.DrawingShapes.Where(s => string.IsNullOrWhiteSpace(s.AltText)))
                issues.Add(MissingAltText(sheet, shape.Anchor, "Shape"));

            foreach (var textBox in sheet.TextBoxes.Where(t => string.IsNullOrWhiteSpace(t.AltText)))
                issues.Add(MissingAltText(sheet, textBox.Anchor, "Text box"));

            foreach (var chart in sheet.Charts.Where(c => string.IsNullOrWhiteSpace(c.Title)))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.ChartMissingTitle,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(chart.DataRange),
                    "Chart is missing a title."));
            }
        }

        return issues;
    }

    private static AccessibilityIssue MissingAltText(Sheet sheet, CellAddress anchor, string objectType) => new(
        AccessibilityIssueKind.MissingAltText,
        sheet.Id,
        sheet.Name,
        anchor.ToA1(),
        $"{objectType} is missing alternate text.");

    private static string FormatRange(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
}
