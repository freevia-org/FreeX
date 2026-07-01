using FreeX.Core.Commands;
using FreeX.App.Presentation.Accessibility;

namespace FreeX.App.Services;

public static class AccessibilityIssueFormatter
{
    public static string Format(IReadOnlyList<AccessibilityIssue> issues) =>
        AccessibilityCheckerDialogPlanner.CreateMessage(issues, key => key);
}
