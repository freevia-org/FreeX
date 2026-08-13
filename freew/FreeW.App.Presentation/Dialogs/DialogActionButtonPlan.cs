using Free.Shared.Shell;
using FreeW.App.Localization;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Shared semantic contract for a dialog action row. Hosts create their native buttons, while this
/// plan keeps user-facing labels, ordering, and Enter/Escape roles aligned.
/// </summary>
public sealed record DialogActionButtonPlan : DialogActionPlan
{
    public DialogActionButtonPlan(
        string Label,
        bool IsDefault = false,
        bool IsCancel = false)
        : base(Label, IsDefault, IsCancel)
    {
    }
}

public static class DocumentInspectorDialogPlanner
{
    public static DocumentInspectorDialogText Text => new(
        Loc.Get("DocumentInspector_Title"),
        Loc.Get("DocumentInspector_Clean_Message"),
        Loc.Get("DocumentInspector_Review_Message"),
        new DocumentInspectorRowText(
            Loc.Get("DocumentInspector_Comments_Label"),
            Loc.Get("DocumentInspector_Comments_HelpText")),
        new DocumentInspectorRowText(
            Loc.Get("DocumentInspector_Revisions_Label"),
            Loc.Get("DocumentInspector_Revisions_HelpText")),
        new DocumentInspectorRowText(
            Loc.Get("DocumentInspector_Properties_Label"),
            Loc.Get("DocumentInspector_Properties_HelpText")),
        new DocumentInspectorRowText(
            Loc.Get("DocumentInspector_Bookmarks_Label"),
            Loc.Get("DocumentInspector_Bookmarks_HelpText")));

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new("Remove Selected", IsDefault: true),
        new("Close", IsCancel: true),
    ];
}

public sealed record DocumentInspectorRowText(string Label, string HelpText);

public sealed record DocumentInspectorDialogText(
    string Title,
    string CleanMessage,
    string ReviewMessage,
    DocumentInspectorRowText Comments,
    DocumentInspectorRowText Revisions,
    DocumentInspectorRowText Properties,
    DocumentInspectorRowText Bookmarks);
