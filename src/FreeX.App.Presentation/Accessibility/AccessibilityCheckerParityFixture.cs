using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Accessibility;

public static class AccessibilityCheckerParityFixture
{
    public const string SheetName = "Sheet1";
    public const string ChartName = "Revenue Chart";

    public static IReadOnlyList<AccessibilityIssue> CreateDialogIssues(SheetId sheetId) =>
    [
        new AccessibilityIssue(
            AccessibilityIssueKind.DefaultWorksheetName,
            sheetId,
            SheetName,
            SheetName,
            "Worksheet tab names should describe their contents."),
        new AccessibilityIssue(
            AccessibilityIssueKind.MissingAltText,
            sheetId,
            SheetName,
            ChartName,
            "Charts should include descriptive alternative text."),
    ];
}
