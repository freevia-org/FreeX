using System.Globalization;
using Free.Shared.AppServices;
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

public sealed record BackstageInfoSafetyText(
    string ProtectDocumentHeading,
    string EditAnywayLabel,
    string MarkAsFinalLabel,
    string ClearFinalDescription,
    string MarkAsFinalDescription,
    string RestrictEditingLabel,
    string NoProtectionDescription,
    string ProtectionInactiveDescription,
    string PasswordConfiguredText,
    string ProtectionActiveFormat,
    string InspectDocumentHeading,
    string InspectDocumentLabel,
    string InspectionDefaultDescription,
    string InspectionEmptyDescription,
    string InspectionItemsFormat,
    string CheckAccessibilityLabel,
    string AccessibilityDefaultDescription,
    string AccessibilityEmptyDescription,
    string AccessibilityIssuesFormat,
    string ReadOnlyMode,
    string CommentsOnlyMode,
    string TrackChangesOnlyMode,
    string FillingFormsMode,
    string SingularCountFormat,
    string PluralCountFormat,
    string MetadataItemNoun,
    string IssueNoun,
    string MarkedAsFinalStatus,
    string NotMarkedAsFinalStatus,
    string RestrictionsRemovedStatus,
    string RestrictionsAppliedFormat,
    string SelectedDataRemovedStatus,
    string InspectorCompletedStatus,
    string NoAccessibilityIssuesStatus,
    string AccessibilityIssueCountStatusFormat,
    string MarkedAsFinalBanner);

public static class BackstageInfoSafetyPanePlanner
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        Text("Backstage_Safety_ProtectDocument_Heading", "Protect Document"),
        Text("Backstage_Safety_EditAnyway_Label", "Edit Anyway"),
        Text("Backstage_Safety_MarkAsFinal_Label", "Mark as Final"),
        Text("Backstage_Safety_ClearFinal_Description", "Clear the final marker so the document can be edited again."),
        Text("Backstage_Safety_MarkAsFinal_Description", "Make the document read-only and tell readers this version is final."),
        Text("Backstage_Safety_RestrictEditing_Label", "Restrict Editing"),
        Text("Backstage_Safety_Protection_Default_Description", "Limit editing to comments, tracked changes, forms, or read-only mode."),
        Text("Backstage_Safety_Protection_Inactive_Description", "No editing restrictions are active. Limit editing to comments, tracked changes, forms, or read-only mode."),
        Text("Backstage_Safety_Protection_PasswordConfigured", " Password protection is configured."),
        Text("Backstage_Safety_Protection_Active_Format", "Current restriction: {0}.{1} Change or stop editing restrictions."),
        Text("Backstage_Safety_InspectDocument_Heading", "Inspect Document"),
        Text("Backstage_Safety_InspectDocument_Label", "Inspect Document"),
        Text("Backstage_Safety_Inspection_Default_Description", "Find and remove comments, revisions, document properties, and bookmarks."),
        Text("Backstage_Safety_Inspection_Empty_Description", "No comments, revisions, document properties, or bookmarks are currently detected."),
        Text("Backstage_Safety_Inspection_Items_Format", "Find and remove {0} across comments, revisions, document properties, and bookmarks."),
        Text("Backstage_Safety_CheckAccessibility_Label", "Check Accessibility"),
        Text("Backstage_Safety_Accessibility_Default_Description", "Find accessibility issues before sharing the document."),
        Text("Backstage_Safety_Accessibility_Empty_Description", "No accessibility issues are currently detected."),
        Text("Backstage_Safety_Accessibility_Issues_Format", "Find accessibility issues before sharing the document. Current scan: {0} ({1} errors, {2} warnings, {3} tips)."),
        Text("Backstage_Safety_ProtectionMode_ReadOnly", "Read only"),
        Text("Backstage_Safety_ProtectionMode_CommentsOnly", "Comments only"),
        Text("Backstage_Safety_ProtectionMode_TrackChangesOnly", "Tracked changes only"),
        Text("Backstage_Safety_ProtectionMode_FillingForms", "Filling in forms"),
        Text("Backstage_Safety_Count_Singular_Format", "1 {0}"),
        Text("Backstage_Safety_Count_Plural_Format", "{0} {1}s"),
        Text("Backstage_Safety_MetadataItem_Noun", "metadata item"),
        Text("Backstage_Safety_Issue_Noun", "issue"),
        Text("Backstage_Safety_MarkedAsFinal_Status", "Document marked as final."),
        Text("Backstage_Safety_NotMarkedAsFinal_Status", "Document is no longer marked as final."),
        Text("Backstage_Safety_RestrictionsRemoved_Status", "Editing restrictions removed."),
        Text("Backstage_Safety_RestrictionsApplied_Status_Format", "Editing restricted: {0}."),
        Text("Backstage_Safety_SelectedDataRemoved_Status", "Selected document data removed."),
        Text("Backstage_Safety_InspectorCompleted_Status", "Document Inspector completed."),
        Text("Backstage_Safety_NoAccessibilityIssues_Status", "No accessibility issues found."),
        Text("Backstage_Safety_AccessibilityIssueCount_Status_Format", "{0} accessibility issue(s) found."),
        Text("Backstage_Safety_MarkedAsFinal_Banner", "Marked as Final  An author has marked this document as final to discourage editing."),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static BackstageInfoSafetyText ResolveText(Func<string, string?>? getText = null)
    {
        var values = Texts.Select(text => text.Resolve(getText)).ToArray();
        return new BackstageInfoSafetyText(
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15],
            values[16], values[17], values[18], values[19], values[20], values[21], values[22], values[23],
            values[24], values[25], values[26], values[27], values[28], values[29], values[30], values[31],
            values[32], values[33], values[34], values[35]);
    }

    public static IReadOnlyList<BackstageInfoSafetyGroup> Build(
        TextDocument? document = null,
        Func<string, string?>? getText = null) =>
        Build(document is null ? null : BackstageInfoSafetyDocumentState.FromDocument(document), getText);

    public static IReadOnlyList<BackstageInfoSafetyGroup> Build(
        BackstageInfoSafetyDocumentState? state,
        Func<string, string?>? getText = null)
    {
        var text = ResolveText(getText);
        return
        [
        new(text.ProtectDocumentHeading,
        [
            new(
                BackstageInfoSafetyActionKind.MarkAsFinal,
                state?.IsMarkedAsFinal == true ? text.EditAnywayLabel : text.MarkAsFinalLabel,
                state?.IsMarkedAsFinal == true
                    ? text.ClearFinalDescription
                    : text.MarkAsFinalDescription),
            new(
                BackstageInfoSafetyActionKind.RestrictEditing,
                text.RestrictEditingLabel,
                FormatProtectionDescription(state, text)),
        ]),
        new(text.InspectDocumentHeading,
        [
            new(
                BackstageInfoSafetyActionKind.InspectDocument,
                text.InspectDocumentLabel,
                FormatInspectionDescription(state, text)),
            new(
                BackstageInfoSafetyActionKind.CheckAccessibility,
                text.CheckAccessibilityLabel,
                FormatAccessibilityDescription(state, text)),
        ]),
        ];
    }

    private static string FormatProtectionDescription(BackstageInfoSafetyDocumentState? state, BackstageInfoSafetyText text)
    {
        if (state is null)
            return text.NoProtectionDescription;

        if (state.ProtectionMode == ProtectionMode.None)
            return text.ProtectionInactiveDescription;

        var password = state.ProtectionHasPassword ? text.PasswordConfiguredText : string.Empty;
        return Format(text.ProtectionActiveFormat, FormatProtectionMode(state.ProtectionMode, text), password);
    }

    private static string FormatInspectionDescription(BackstageInfoSafetyDocumentState? state, BackstageInfoSafetyText text)
    {
        if (state is null)
            return text.InspectionDefaultDescription;

        if (state.InspectionItemCount == 0)
            return text.InspectionEmptyDescription;

        return Format(text.InspectionItemsFormat, FormatCount(state.InspectionItemCount, text.MetadataItemNoun, text));
    }

    private static string FormatAccessibilityDescription(BackstageInfoSafetyDocumentState? state, BackstageInfoSafetyText text)
    {
        if (state is null)
            return text.AccessibilityDefaultDescription;

        if (state.AccessibilityIssueCount == 0)
            return text.AccessibilityEmptyDescription;

        return Format(
            text.AccessibilityIssuesFormat,
            FormatCount(state.AccessibilityIssueCount, text.IssueNoun, text),
            state.AccessibilityErrorCount,
            state.AccessibilityWarningCount,
            state.AccessibilityTipCount);
    }

    private static string FormatProtectionMode(ProtectionMode mode, BackstageInfoSafetyText text) => mode switch
    {
        ProtectionMode.ReadOnly => text.ReadOnlyMode,
        ProtectionMode.CommentsOnly => text.CommentsOnlyMode,
        ProtectionMode.TrackChangesOnly => text.TrackChangesOnlyMode,
        ProtectionMode.FillingForms => text.FillingFormsMode,
        _ => mode.ToString()
    };

    private static string FormatCount(int count, string singular, BackstageInfoSafetyText text) =>
        count == 1
            ? Format(text.SingularCountFormat, singular)
            : Format(text.PluralCountFormat, count, singular);

    private static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);

    private static ResourceTextDescriptor Text(string key, string fallbackText) => new(key, fallbackText);
}
