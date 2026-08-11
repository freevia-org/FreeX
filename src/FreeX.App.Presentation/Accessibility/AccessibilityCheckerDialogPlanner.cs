using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Accessibility;

public enum AccessibilityCheckerDialogState
{
    Clean,
    Issues,
}

public enum AccessibilityCheckerActionId
{
    GoTo,
    Close,
}

public sealed record AccessibilityCheckerAutomationSpec(
    string Name,
    string AutomationId,
    string HelpText);

public sealed record AccessibilityCheckerActionSpec(
    AccessibilityCheckerActionId Id,
    string Text,
    AccessibilityCheckerAutomationSpec Automation,
    bool IsDefault,
    bool IsCancel);

public sealed record AccessibilityCheckerItemPlan(
    AccessibilityIssue Issue,
    AccessibilityIssueDescriptor Descriptor,
    string ObjectLabel,
    string Description,
    string WhyFix,
    string HowToFix);

public sealed record AccessibilityCheckerGroupPlan(
    AccessibilityIssueDescriptor Descriptor,
    string Label,
    string WhyFix,
    string HowToFix,
    IReadOnlyList<AccessibilityCheckerItemPlan> Items);

public sealed record AccessibilityCheckerSectionPlan(
    AccessibilitySeverity Severity,
    string Header,
    IReadOnlyList<AccessibilityCheckerGroupPlan> Groups)
{
    public int IssueCount => Groups.Sum(group => group.Items.Count);
}

public sealed record AccessibilityCheckerDialogPlan(
    AccessibilityCheckerDialogState State,
    string Title,
    string CleanMessage,
    string IssueSummaryMessage,
    string StatusText,
    string StatusReadyText,
    string InspectionResultsHeader,
    string AdditionalInformationHeader,
    string WhyFixHeader,
    string HowToFixHeader,
    AccessibilityCheckerAutomationSpec ResultAutomation,
    AccessibilityCheckerAutomationSpec IssueListAutomation,
    AccessibilityCheckerActionSpec GoToAction,
    AccessibilityCheckerActionSpec CloseAction,
    IReadOnlyList<AccessibilityCheckerSectionPlan> Sections);

public sealed record AccessibilityCheckerSelectionPlan(
    bool HasAdditionalInformation,
    bool CanNavigate,
    string WhyFix,
    string HowToFix,
    string StatusText);

public static class AccessibilityCheckerDialogPlanner
{
    private const int MaxShownIssues = 20;

    public static AccessibilityCheckerDialogPlan Create(
        IReadOnlyList<AccessibilityIssue> issues,
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(getText);

        var sections = CreateSections(issues, getText);
        var state = sections.Count == 0
            ? AccessibilityCheckerDialogState.Clean
            : AccessibilityCheckerDialogState.Issues;

        return new AccessibilityCheckerDialogPlan(
            state,
            Text("AccessibilityChecker_Title", "Accessibility Checker", getText),
            CreateMessage(issues, getText),
            CreateIssueSummaryMessage(issues),
            state == AccessibilityCheckerDialogState.Clean
                ? Text(
                    "AccessibilityChecker_StatusClean",
                    "No accessibility issues found. People with disabilities should not have difficulty reading this workbook.",
                    getText)
                : Text("AccessibilityChecker_StatusReady", "Ready", getText),
            Text("AccessibilityChecker_StatusReady", "Ready", getText),
            Text("AccessibilityChecker_InspectionResults", "Inspection Results", getText),
            Text("AccessibilityChecker_AdditionalInformation", "Additional Information", getText),
            Text("AccessibilityChecker_WhyFixHeader", "Why Fix:", getText),
            Text("AccessibilityChecker_HowToFixHeader", "How To Fix:", getText),
            new AccessibilityCheckerAutomationSpec(
                Text("AccessibilityChecker_ResultAutomationName", "Accessibility checker result", getText),
                "AccessibilityCheckerResultText",
                Text(
                    "AccessibilityChecker_ResultHelpText",
                    "Summarizes the workbook accessibility check when no issues are found.",
                    getText)),
            new AccessibilityCheckerAutomationSpec(
                Text("AccessibilityChecker_IssueListAutomationName", "Accessibility issues", getText),
                "AccessibilityCheckerIssueList",
                Text(
                    "AccessibilityChecker_IssueListHelpText",
                    "Select an accessibility issue and choose Go To to navigate to its workbook location.",
                    getText)),
            new AccessibilityCheckerActionSpec(
                AccessibilityCheckerActionId.GoTo,
                Text("AccessibilityChecker_GoToButton", "Go To", getText),
                new AccessibilityCheckerAutomationSpec(
                    Text("AccessibilityChecker_GoToAutomationName", "Go to selected accessibility issue", getText),
                    "AccessibilityCheckerGoToButton",
                    Text(
                        "AccessibilityChecker_GoToHelpText",
                        "Navigate to the selected accessibility issue.",
                        getText)),
                IsDefault: true,
                IsCancel: false),
            new AccessibilityCheckerActionSpec(
                AccessibilityCheckerActionId.Close,
                Text("AccessibilityChecker_CloseButton", "_Close", getText),
                new AccessibilityCheckerAutomationSpec(
                    Text("AccessibilityChecker_CloseAutomationName", "Close Accessibility Checker", getText),
                    "AccessibilityCheckerCloseButton",
                    Text(
                        "AccessibilityChecker_CloseHelpText",
                        "Close the Accessibility Checker without navigating to an issue.",
                        getText)),
                IsDefault: false,
                IsCancel: true),
            sections);
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues, Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(getText);

        return issues.Count == 0
            ? Text("AccessibilityChecker_NoIssuesMessage", "No accessibility issues found.", getText)
            : CreateIssueSummaryMessage(issues);
    }

    public static AccessibilityCheckerSelectionPlan CreateSelection(
        AccessibilityCheckerItemPlan? item,
        AccessibilityCheckerGroupPlan? group,
        AccessibilityCheckerDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (item is not null)
        {
            return new AccessibilityCheckerSelectionPlan(
                HasAdditionalInformation: true,
                CanNavigate: true,
                item.WhyFix,
                item.HowToFix,
                item.Description);
        }

        if (group is not null)
        {
            return new AccessibilityCheckerSelectionPlan(
                HasAdditionalInformation: true,
                CanNavigate: group.Items.Count > 0,
                group.WhyFix,
                group.HowToFix,
                group.Label);
        }

        return new AccessibilityCheckerSelectionPlan(
            HasAdditionalInformation: false,
            CanNavigate: false,
            string.Empty,
            string.Empty,
            plan.StatusReadyText);
    }

    public static CellAddress GetNavigationTarget(AccessibilityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var location = issue.Location.Trim();
        var firstLocation = location.Split(':', 2)[0];
        return CellAddress.TryParse(firstLocation, issue.SheetId, out var address)
            ? address
            : new CellAddress(issue.SheetId, 1, 1);
    }

    private static List<AccessibilityCheckerSectionPlan> CreateSections(
        IReadOnlyList<AccessibilityIssue> issues,
        Func<string, string> getText)
    {
        var sections = new List<AccessibilityCheckerSectionPlan>();

        foreach (var section in AccessibilityInspectionResult.Build(issues))
        {
            var groups = section.Groups
                .Select(group => CreateGroup(group, getText))
                .OrderBy(group => group.Label, StringComparer.CurrentCulture)
                .ToList();

            if (groups.Count > 0)
                sections.Add(new AccessibilityCheckerSectionPlan(section.Severity, SeverityHeader(section.Severity, getText), groups));
        }

        return sections;
    }

    private static AccessibilityCheckerGroupPlan CreateGroup(
        AccessibilityInspectionGroup group,
        Func<string, string> getText)
    {
        var descriptor = group.Descriptor;
        var whyFix = Text(descriptor.WhyFixKey, descriptor.WhyFix, getText);
        var howToFix = Text(descriptor.HowToFixKey, descriptor.HowToFix, getText);
        var items = group.Items
            .Select(item => CreateItem(item.Issue, getText))
            .ToList();

        return new AccessibilityCheckerGroupPlan(
            descriptor,
            Text(descriptor.LabelKey, descriptor.Label, getText),
            whyFix,
            howToFix,
            items);
    }

    private static AccessibilityCheckerItemPlan CreateItem(
        AccessibilityIssue issue,
        Func<string, string> getText)
    {
        var descriptor = AccessibilityIssueClassification.Describe(issue.Kind);
        return new AccessibilityCheckerItemPlan(
            issue,
            descriptor,
            $"{issue.SheetName}!{issue.Location}",
            FormatIssue(issue),
            Text(descriptor.WhyFixKey, descriptor.WhyFix, getText),
            Text(descriptor.HowToFixKey, descriptor.HowToFix, getText));
    }

    private static string SeverityHeader(AccessibilitySeverity severity, Func<string, string> getText) => severity switch
    {
        AccessibilitySeverity.Error => Text("AccessibilityChecker_SectionErrors", "Errors", getText),
        AccessibilitySeverity.Warning => Text("AccessibilityChecker_SectionWarnings", "Warnings", getText),
        _ => Text("AccessibilityChecker_SectionTips", "Tips", getText),
    };

    private static string CreateIssueSummaryMessage(IReadOnlyList<AccessibilityIssue> issues)
    {
        var message = string.Join(Environment.NewLine, issues.Take(MaxShownIssues).Select(FormatIssue));
        if (issues.Count > MaxShownIssues)
            message += $"{Environment.NewLine}...and {issues.Count - MaxShownIssues} more.";
        return message;
    }

    private static string FormatIssue(AccessibilityIssue issue) =>
        $"{issue.SheetName}!{issue.Location}: {issue.Message}";

    private static string Text(string key, string fallback, Func<string, string> getText) =>
        new ResourceTextDescriptor(key, fallback).Resolve(getText);
}
