using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

public sealed record BackstageInfoSafetyGroup(
    string Heading,
    IReadOnlyList<BackstageInfoSafetyAction> Actions);

public sealed record BackstageInfoSafetyAction(
    BackstageInfoSafetyActionKind Kind,
    string Label,
    string Description);

public sealed record BackstageInfoSafetyDocumentState(
    bool IsMarkedAsFinal,
    ProtectionMode ProtectionMode,
    bool ProtectionHasPassword,
    int InspectionItemCount,
    int AccessibilityIssueCount,
    int AccessibilityErrorCount,
    int AccessibilityWarningCount,
    int AccessibilityTipCount)
{
    public static BackstageInfoSafetyDocumentState FromDocument(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var inspection = DocumentInspector.Inspect(document);
        var accessibility = AccessibilityChecker.Check(document);

        return new BackstageInfoSafetyDocumentState(
            document.MarkedAsFinal,
            document.Protection.Mode,
            document.Protection.HasPassword,
            inspection.Comments + inspection.Revisions + inspection.NonEmptyProperties + inspection.Bookmarks,
            accessibility.Issues.Count,
            accessibility.ErrorCount,
            accessibility.WarningCount,
            accessibility.TipCount);
    }
}

public enum BackstageInfoSafetyActionKind
{
    MarkAsFinal,
    RestrictEditing,
    InspectDocument,
    CheckAccessibility
}

public static class BackstageInfoSafetyPanePlanner
{
    public static IReadOnlyList<BackstageInfoSafetyGroup> Build(TextDocument? document = null) =>
        Build(document is null ? null : BackstageInfoSafetyDocumentState.FromDocument(document));

    public static IReadOnlyList<BackstageInfoSafetyGroup> Build(BackstageInfoSafetyDocumentState? state) =>
    [
        new("Protect Document",
        [
            new(
                BackstageInfoSafetyActionKind.MarkAsFinal,
                state?.IsMarkedAsFinal == true ? "Edit Anyway" : "Mark as Final",
                state?.IsMarkedAsFinal == true
                    ? "Clear the final marker so the document can be edited again."
                    : "Make the document read-only and tell readers this version is final."),
            new(
                BackstageInfoSafetyActionKind.RestrictEditing,
                "Restrict Editing",
                FormatProtectionDescription(state)),
        ]),
        new("Inspect Document",
        [
            new(
                BackstageInfoSafetyActionKind.InspectDocument,
                "Inspect Document",
                FormatInspectionDescription(state)),
            new(
                BackstageInfoSafetyActionKind.CheckAccessibility,
                "Check Accessibility",
                FormatAccessibilityDescription(state)),
        ]),
    ];

    private static string FormatProtectionDescription(BackstageInfoSafetyDocumentState? state)
    {
        if (state is null)
            return "Limit editing to comments, tracked changes, forms, or read-only mode.";

        if (state.ProtectionMode == ProtectionMode.None)
            return "No editing restrictions are active. Limit editing to comments, tracked changes, forms, or read-only mode.";

        var password = state.ProtectionHasPassword ? " Password protection is configured." : string.Empty;
        return $"Current restriction: {FormatProtectionMode(state.ProtectionMode)}.{password} Change or stop editing restrictions.";
    }

    private static string FormatInspectionDescription(BackstageInfoSafetyDocumentState? state)
    {
        if (state is null)
            return "Find and remove comments, revisions, document properties, and bookmarks.";

        if (state.InspectionItemCount == 0)
            return "No comments, revisions, document properties, or bookmarks are currently detected.";

        return $"Find and remove {FormatCount(state.InspectionItemCount, "metadata item")} across comments, revisions, document properties, and bookmarks.";
    }

    private static string FormatAccessibilityDescription(BackstageInfoSafetyDocumentState? state)
    {
        if (state is null)
            return "Find accessibility issues before sharing the document.";

        if (state.AccessibilityIssueCount == 0)
            return "No accessibility issues are currently detected.";

        return "Find accessibility issues before sharing the document. " +
            $"Current scan: {FormatCount(state.AccessibilityIssueCount, "issue")} " +
            $"({state.AccessibilityErrorCount} errors, {state.AccessibilityWarningCount} warnings, {state.AccessibilityTipCount} tips).";
    }

    private static string FormatProtectionMode(ProtectionMode mode) => mode switch
    {
        ProtectionMode.ReadOnly => "Read only",
        ProtectionMode.CommentsOnly => "Comments only",
        ProtectionMode.TrackChangesOnly => "Tracked changes only",
        ProtectionMode.FillingForms => "Filling in forms",
        _ => mode.ToString()
    };

    private static string FormatCount(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count} {singular}s";
}
