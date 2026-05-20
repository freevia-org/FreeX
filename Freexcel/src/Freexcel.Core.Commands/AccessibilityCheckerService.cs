using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum AccessibilityIssueKind
{
    MergedCells,
    MissingAltText,
    GenericAltText,
    ChartMissingTitle,
    HyperlinkDisplayTextIsUrl,
    DefaultWorksheetName,
    HiddenSheetWithContent,
    HiddenRowWithContent,
    HiddenColumnWithContent
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static class AccessibilityCheckerService
{
    private static readonly HashSet<string> GenericHyperlinkDisplayTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "click here",
        "here",
        "link",
        "more",
        "read more",
        "learn more"
    };

    private static readonly HashSet<string> GenericAltTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "picture",
        "photo",
        "shape",
        "text box",
        "object",
        "graphic"
    };

    public static IReadOnlyList<AccessibilityIssue> FindIssues(Workbook workbook)
    {
        var issues = new List<AccessibilityIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            if (IsDefaultWorksheetName(sheet.Name))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.DefaultWorksheetName,
                    sheet.Id,
                    sheet.Name,
                    sheet.Name,
                    "Worksheet tab names should describe their contents."));
            }

            AddHiddenContentIssues(issues, sheet);

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
                AddAltTextIssue(issues, sheet, picture.Anchor, "Picture", picture.AltText);

            foreach (var shape in sheet.DrawingShapes)
                AddAltTextIssue(issues, sheet, shape.Anchor, "Shape", shape.AltText);

            foreach (var textBox in sheet.TextBoxes)
                AddAltTextIssue(issues, sheet, textBox.Anchor, "Text box", textBox.AltText);

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                if (sheet.GetCell(address)?.Value is TextValue displayText &&
                    IsDescriptiveHyperlinkText(displayText.Value, target))
                    continue;

                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HyperlinkDisplayTextIsUrl,
                    sheet.Id,
                    sheet.Name,
                    address.ToA1(),
                    "Hyperlink display text should describe the destination."));
            }

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

    private static void AddHiddenContentIssues(List<AccessibilityIssue> issues, Sheet sheet)
    {
        var usedCells = sheet.GetUsedCells().Keys.ToList();
        if (usedCells.Count == 0)
            return;

        if (sheet.IsHidden || sheet.IsVeryHidden)
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenSheetWithContent,
                sheet.Id,
                sheet.Name,
                sheet.Name,
                "Hidden sheets with content may not be available to assistive technologies."));
        }

        foreach (var row in usedCells
                     .Select(address => address.Row)
                     .Where(sheet.IsRowEffectivelyHidden)
                     .Distinct()
                     .Order())
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenRowWithContent,
                sheet.Id,
                sheet.Name,
                $"{row}:{row}",
                "Hidden rows with content may not be available to assistive technologies."));
        }

        foreach (var col in usedCells
                     .Select(address => address.Col)
                     .Where(sheet.IsColEffectivelyHidden)
                     .Distinct()
                     .Order())
        {
            var name = CellAddress.NumberToColumnName(col);
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenColumnWithContent,
                sheet.Id,
                sheet.Name,
                $"{name}:{name}",
                "Hidden columns with content may not be available to assistive technologies."));
        }
    }

    private static AccessibilityIssue MissingAltText(Sheet sheet, CellAddress anchor, string objectType) => new(
        AccessibilityIssueKind.MissingAltText,
        sheet.Id,
        sheet.Name,
        anchor.ToA1(),
        $"{objectType} is missing alternate text.");

    private static void AddAltTextIssue(List<AccessibilityIssue> issues, Sheet sheet, CellAddress anchor, string objectType, string? altText)
    {
        if (string.IsNullOrWhiteSpace(altText))
        {
            issues.Add(MissingAltText(sheet, anchor, objectType));
            return;
        }

        if (IsGenericAltText(altText))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.GenericAltText,
                sheet.Id,
                sheet.Name,
                anchor.ToA1(),
                $"{objectType} alternate text should describe the object."));
        }
    }

    private static bool IsDescriptiveHyperlinkText(string displayText, string target)
    {
        var text = displayText.Trim();
        return text.Length > 0 &&
            !GenericHyperlinkDisplayTexts.Contains(text) &&
            !string.Equals(text, target.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !LooksLikeUrl(text);
    }

    private static bool LooksLikeUrl(string text) =>
        (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto ||
             uri.Scheme == Uri.UriSchemeFtp)) ||
        text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericAltText(string altText)
    {
        var text = altText.Trim();
        return GenericAltTexts.Contains(text) ||
            text.StartsWith("picture ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "picture ") ||
            text.StartsWith("image ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "image ") ||
            text.StartsWith("shape ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "shape ") ||
            text.StartsWith("text box ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "text box ");
    }

    private static bool IsNumberSuffix(string text, string prefix) =>
        int.TryParse(text[prefix.Length..], out _);

    private static bool IsDefaultWorksheetName(string name) =>
        name.StartsWith("Sheet", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(name["Sheet".Length..], out _);

    private static string FormatRange(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
}
