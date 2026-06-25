namespace FreeX.Core.Commands;

/// <summary>One affected object under an issue-type group (e.g. "Sheet1!Revenue Chart").</summary>
public sealed record AccessibilityInspectionItem(AccessibilityIssue Issue)
{
    /// <summary>Object reference shown as the leaf under an issue-type group.</summary>
    public string ObjectLabel => $"{Issue.SheetName}!{Issue.Location}";

    /// <summary>Full one-line description (used by flat/legacy presentations and tooltips).</summary>
    public string Description => $"{Issue.SheetName}!{Issue.Location}: {Issue.Message}";
}

/// <summary>An issue-type heading (e.g. "Missing alternative text") and its affected objects.</summary>
public sealed record AccessibilityInspectionGroup(
    AccessibilityIssueDescriptor Descriptor,
    IReadOnlyList<AccessibilityInspectionItem> Items);

/// <summary>A severity bucket (Errors / Warnings / Tips) and the issue-type groups it contains.</summary>
public sealed record AccessibilityInspectionSection(
    AccessibilitySeverity Severity,
    IReadOnlyList<AccessibilityInspectionGroup> Groups)
{
    public int IssueCount => Groups.Sum(g => g.Items.Count);
}

/// <summary>
/// Excel-style Inspection Results: issues bucketed into Errors / Warnings / Tips sections, and
/// within each section grouped by issue type. Built once from the analysis output and shared by the
/// WPF and Avalonia Accessibility Checker dialogs so both present identical structure.
/// </summary>
public static class AccessibilityInspectionResult
{
    public static IReadOnlyList<AccessibilityInspectionSection> Build(IReadOnlyList<AccessibilityIssue> issues)
    {
        var sections = new List<AccessibilityInspectionSection>();

        foreach (var severity in AccessibilityIssueClassification.SeverityOrder)
        {
            var groups = issues
                .Where(issue => AccessibilityIssueClassification.GetSeverity(issue.Kind) == severity)
                .GroupBy(issue => AccessibilityIssueClassification.Describe(issue.Kind).Label)
                .Select(group => new AccessibilityInspectionGroup(
                    AccessibilityIssueClassification.Describe(group.First().Kind),
                    group.Select(issue => new AccessibilityInspectionItem(issue)).ToList()))
                .OrderBy(group => group.Descriptor.Label, StringComparer.CurrentCulture)
                .ToList();

            if (groups.Count > 0)
                sections.Add(new AccessibilityInspectionSection(severity, groups));
        }

        return sections;
    }
}
