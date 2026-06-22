namespace FreeW.App.Host.Backstage;

internal sealed record BackstageInfoSafetyGroup(
    string Heading,
    IReadOnlyList<BackstageInfoSafetyAction> Actions);

internal sealed record BackstageInfoSafetyAction(
    BackstageInfoSafetyActionKind Kind,
    string Label,
    string Description);

internal enum BackstageInfoSafetyActionKind
{
    MarkAsFinal,
    RestrictEditing,
    InspectDocument,
    CheckAccessibility
}

internal static class BackstageInfoSafetyPanePlanner
{
    public static IReadOnlyList<BackstageInfoSafetyGroup> Build() =>
    [
        new("Protect Document",
        [
            new(
                BackstageInfoSafetyActionKind.MarkAsFinal,
                "Mark as Final",
                "Make the document read-only and tell readers this version is final."),
            new(
                BackstageInfoSafetyActionKind.RestrictEditing,
                "Restrict Editing",
                "Limit editing to comments, tracked changes, forms, or read-only mode."),
        ]),
        new("Inspect Document",
        [
            new(
                BackstageInfoSafetyActionKind.InspectDocument,
                "Inspect Document",
                "Find and remove comments, revisions, document properties, and bookmarks."),
            new(
                BackstageInfoSafetyActionKind.CheckAccessibility,
                "Check Accessibility",
                "Find accessibility issues before sharing the document."),
        ]),
    ];
}
