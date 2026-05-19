using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum AccessibilityIssueKind
{
    MergedCells,
    MissingAltText,
    HiddenSheetContent,
    HiddenRowContent,
    HiddenColumnContent,
    UnclearHyperlinkText,
    MissingChartTitle
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static class AccessibilityCheckerService
{
    private static readonly HashSet<string> GenericHyperlinkText = new(StringComparer.OrdinalIgnoreCase)
    {
        "click here",
        "here",
        "link",
        "more",
        "read more"
    };

    public static IReadOnlyList<AccessibilityIssue> FindIssues(Workbook workbook)
    {
        var issues = new List<AccessibilityIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var range in sheet.MergedRegions.OrderBy(r => r.Start.Row).ThenBy(r => r.Start.Col))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.MergedCells,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(range),
                    "Merged cells can make worksheet navigation harder for assistive technologies."));
            }

            foreach (var issue in GetMissingAltTextIssues(sheet))
                issues.Add(issue);

            if ((sheet.IsHidden || sheet.IsVeryHidden) && SheetContainsContentOrObjects(sheet))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenSheetContent,
                    sheet.Id,
                    sheet.Name,
                    "Sheet",
                    "Hidden sheet contains content or objects that may be missed by assistive technologies."));
            }

            foreach (var row in GetEffectivelyHiddenRows(sheet).Where(row => RowContainsContentOrObjects(sheet, row)))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenRowContent,
                    sheet.Id,
                    sheet.Name,
                    $"{row}:{row}",
                    "Hidden row contains cells, comments, hyperlinks, charts, or anchored objects."));
            }

            foreach (var col in GetEffectivelyHiddenColumns(sheet).Where(col => ColumnContainsContentOrObjects(sheet, col)))
            {
                var columnName = CellAddress.NumberToColumnName(col);
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenColumnContent,
                    sheet.Id,
                    sheet.Name,
                    $"{columnName}:{columnName}",
                    "Hidden column contains cells, comments, hyperlinks, charts, or anchored objects."));
            }

            foreach (var (address, target) in sheet.Hyperlinks.OrderBy(link => link.Key.Row).ThenBy(link => link.Key.Col))
            {
                if (!HasUnclearHyperlinkDisplayText(sheet, address, target))
                    continue;

                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.UnclearHyperlinkText,
                    sheet.Id,
                    sheet.Name,
                    address.ToA1(),
                    "Hyperlink display text is blank, generic, or just a URL."));
            }

            foreach (var chart in sheet.Charts
                .Where(chart => string.IsNullOrWhiteSpace(chart.Title))
                .OrderBy(chart => chart.DataRange.Start.Row)
                .ThenBy(chart => chart.DataRange.Start.Col))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.MissingChartTitle,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(chart.DataRange),
                    "Chart is missing a title to use as its accessible label."));
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

    private static IEnumerable<AccessibilityIssue> GetMissingAltTextIssues(Sheet sheet) =>
        sheet.Pictures
            .Where(picture => string.IsNullOrWhiteSpace(picture.AltText))
            .Select(picture => (picture.Anchor, ObjectType: "Picture"))
            .Concat(sheet.DrawingShapes
                .Where(shape => string.IsNullOrWhiteSpace(shape.AltText))
                .Select(shape => (shape.Anchor, ObjectType: "Shape")))
            .Concat(sheet.TextBoxes
                .Where(textBox => string.IsNullOrWhiteSpace(textBox.AltText))
                .Select(textBox => (textBox.Anchor, ObjectType: "Text box")))
            .OrderBy(item => item.Anchor.Row)
            .ThenBy(item => item.Anchor.Col)
            .ThenBy(item => item.ObjectType, StringComparer.Ordinal)
            .Select(item => MissingAltText(sheet, item.Anchor, item.ObjectType));

    private static string FormatRange(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";

    private static IEnumerable<uint> GetEffectivelyHiddenRows(Sheet sheet) =>
        sheet.HiddenRows.Concat(sheet.FilterHiddenRows).Concat(sheet.GroupHiddenRows).Distinct().Order();

    private static IEnumerable<uint> GetEffectivelyHiddenColumns(Sheet sheet) =>
        sheet.HiddenCols.Concat(sheet.GroupHiddenCols).Distinct().Order();

    private static bool SheetContainsContentOrObjects(Sheet sheet) =>
        sheet.EnumerateCells().Any(entry => IsNonBlank(entry.Cell)) ||
        sheet.Comments.Count > 0 ||
        sheet.Hyperlinks.Count > 0 ||
        sheet.Pictures.Count > 0 ||
        sheet.DrawingShapes.Count > 0 ||
        sheet.TextBoxes.Count > 0 ||
        sheet.Charts.Count > 0 ||
        sheet.Sparklines.Count > 0;

    private static bool RowContainsContentOrObjects(Sheet sheet, uint row) =>
        sheet.EnumerateCells().Any(entry => entry.Address.Row == row && IsNonBlank(entry.Cell)) ||
        sheet.Comments.Keys.Any(address => address.Row == row) ||
        sheet.Hyperlinks.Keys.Any(address => address.Row == row) ||
        sheet.Pictures.Any(picture => picture.Anchor.Row == row) ||
        sheet.DrawingShapes.Any(shape => shape.Anchor.Row == row) ||
        sheet.TextBoxes.Any(textBox => textBox.Anchor.Row == row) ||
        sheet.Charts.Any(chart => RangeSpansRow(chart.DataRange, row)) ||
        sheet.Sparklines.Any(sparkline => sparkline.Location.Row == row);

    private static bool ColumnContainsContentOrObjects(Sheet sheet, uint col) =>
        sheet.EnumerateCells().Any(entry => entry.Address.Col == col && IsNonBlank(entry.Cell)) ||
        sheet.Comments.Keys.Any(address => address.Col == col) ||
        sheet.Hyperlinks.Keys.Any(address => address.Col == col) ||
        sheet.Pictures.Any(picture => picture.Anchor.Col == col) ||
        sheet.DrawingShapes.Any(shape => shape.Anchor.Col == col) ||
        sheet.TextBoxes.Any(textBox => textBox.Anchor.Col == col) ||
        sheet.Charts.Any(chart => RangeSpansColumn(chart.DataRange, col)) ||
        sheet.Sparklines.Any(sparkline => sparkline.Location.Col == col);

    private static bool RangeSpansRow(GridRange range, uint row) =>
        range.Start.Row <= row && row <= range.End.Row;

    private static bool RangeSpansColumn(GridRange range, uint col) =>
        range.Start.Col <= col && col <= range.End.Col;

    private static bool IsNonBlank(Cell cell) =>
        cell.HasFormula ||
        cell.Value switch
        {
            BlankValue => false,
            TextValue text => !string.IsNullOrWhiteSpace(text.Value),
            _ => true
        };

    private static bool HasUnclearHyperlinkDisplayText(Sheet sheet, CellAddress address, string target)
    {
        var displayText = GetCellDisplayText(sheet.GetCell(address));
        if (string.IsNullOrWhiteSpace(displayText))
            return true;

        var normalizedDisplay = displayText.Trim();
        if (GenericHyperlinkText.Contains(normalizedDisplay))
            return true;

        return IsRawUrlDisplayText(normalizedDisplay, target);
    }

    private static string? GetCellDisplayText(Cell? cell) =>
        cell?.Value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => null
        };

    private static bool IsRawUrlDisplayText(string displayText, string target)
    {
        if (displayText.Equals(target.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (displayText.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return true;

        return Uri.TryCreate(displayText, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto ||
             uri.Scheme == Uri.UriSchemeFtp);
    }
}
