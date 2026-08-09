using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum AccessibilityIssueKind
{
    MergedCells,
    MissingAltText,
    GenericAltText,
    ChartMissingTitle,
    GenericChartTitle,
    HyperlinkDisplayTextIsUrl,
    DefaultWorksheetName,
    HiddenSheetWithContent,
    HiddenRowWithContent,
    HiddenColumnWithContent,
    TableMissingHeaderText,
    TableDefaultHeaderText,
    TableDuplicateHeaderText,
    TableMissingHeaderRow,
    BlankRowOrColumnInTable,
    ChartMissingAxisTitle,
    GenericChartAxisTitle,
    LowContrastCellText,
    LowContrastChartText,
    LowContrastObjectText
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static partial class AccessibilityCheckerService
{
    private const double DefaultObjectTextFontSize = 11d;

    public static IReadOnlyList<AccessibilityIssue> FindIssues(Workbook workbook)
    {
        var issues = new List<AccessibilityIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            if (AccessibilityTextRules.IsDefaultWorksheetName(sheet.Name))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.DefaultWorksheetName,
                    sheet.Id,
                    sheet.Name,
                    sheet.Name,
                    "Worksheet tab names should describe their contents."));
            }

            AddHiddenContentIssues(issues, sheet);
            AddStructuredTableIssues(issues, sheet);
            AddLowContrastCellTextIssues(issues, workbook, sheet);

            foreach (var range in sheet.MergedRegions)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.MergedCells,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(range),
                    "Merged cells can make worksheet navigation harder for assistive technologies."));
            }

            foreach (var picture in sheet.Pictures)
            {
                if (!picture.IsVisible)
                    continue;

                AddAltTextIssue(
                    issues, sheet, picture.Anchor, "Picture", picture.AltText, picture.Title, picture.Name,
                    isDecorative: picture.IsDecorative);
            }

            foreach (var shape in sheet.DrawingShapes)
            {
                if (!shape.IsVisible)
                    continue;

                AddAltTextIssue(issues, sheet, shape.Anchor, "Shape", shape.AltText, shape.Title, shape.Name);
                AddLowContrastShapeTextIssue(issues, workbook, sheet, shape);
            }

            foreach (var textBox in sheet.TextBoxes)
            {
                if (!textBox.IsVisible)
                    continue;

                AddAltTextIssue(issues, sheet, textBox.Anchor, "Text box", textBox.AltText, textBox.Title, textBox.Name);
                AddLowContrastTextBoxTextIssue(issues, workbook, sheet, textBox);
            }

            foreach (var pivot in sheet.PivotTables)
            {
                AddAltTextIssue(
                    issues,
                    sheet,
                    pivot.TargetRange.Start,
                    "PivotTable",
                    pivot.AltTextDescription,
                    pivot.AltTextTitle,
                    name: null);
            }

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                if (sheet.GetCell(address)?.Value is TextValue displayText &&
                    AccessibilityTextRules.IsDescriptiveHyperlinkText(displayText.Value, target))
                    continue;

                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HyperlinkDisplayTextIsUrl,
                    sheet.Id,
                    sheet.Name,
                    address.ToA1(),
                    "Hyperlink display text should describe the destination."));
            }

            AddChartIssues(issues, workbook, sheet);
        }

        return issues;
    }

    private static AccessibilityIssue MissingAltText(Sheet sheet, CellAddress anchor, string objectType) => new(
        AccessibilityIssueKind.MissingAltText,
        sheet.Id,
        sheet.Name,
        anchor.ToA1(),
        $"{objectType} is missing alternate text.");

    private static void AddAltTextIssue(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        CellAddress anchor,
        string objectType,
        string? altText,
        string? title,
        string? name,
        bool isDecorative = false)
    {
        // R90-app-accessibility-checker-5-2: a picture the user explicitly marked "decorative" in
        // Excel's Alt Text pane is intentionally content-free and is exempt from the Missing
        // alternative text rule, even when it has no alt text/title/name at all -- matching real
        // Excel's own Accessibility Checker.
        if (isDecorative)
            return;

        var hasAccessibleText = false;
        foreach (var candidate in GetObjectAccessibleTextCandidates(altText, title, name))
        {
            hasAccessibleText = true;
            if (!AccessibilityTextRules.IsGenericAltText(candidate))
                return;
        }

        if (!hasAccessibleText)
        {
            issues.Add(MissingAltText(sheet, anchor, objectType));
        }
        else
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.GenericAltText,
                sheet.Id,
                sheet.Name,
                anchor.ToA1(),
                $"{objectType} alternate text should describe the object."));
        }
    }

    private static IEnumerable<string> GetObjectAccessibleTextCandidates(string? altText, string? title, string? name)
    {
        if (!string.IsNullOrWhiteSpace(altText))
            yield return altText;
        if (!string.IsNullOrWhiteSpace(title))
            yield return title;
        if (!string.IsNullOrWhiteSpace(name))
            yield return name;
    }

    private static string FormatRange(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
}
