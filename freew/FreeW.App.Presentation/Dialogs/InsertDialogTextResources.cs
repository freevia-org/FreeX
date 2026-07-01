namespace FreeW.App.Presentation.Dialogs;

public sealed record HyperlinkDialogText(
    string Title,
    string EditTitle,
    string DisplayPlaceholder,
    string AddressPlaceholder,
    string DisplayLabel,
    string AddressLabel);

public sealed record ScreenTipDialogText(
    string Title,
    string Placeholder,
    string Label);

public sealed record BookmarkDialogText(
    string Title,
    string NamePlaceholder,
    string NameLabel,
    string GoToLabel,
    string AddButton,
    string GoToButton,
    string CloseButton);

public sealed record LinkBookmarkDialogText(
    string Title,
    string BookmarkLabel,
    string LinkButton,
    string CloseButton);

public sealed record QuickPartDialogText(
    string Title,
    string SnippetPlaceholder,
    string TextLabel);

public static class InsertDialogTextResources
{
    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";
    public const string TextFromFilePickerTitle = "Insert Text from File";

    public static HyperlinkDialogText Hyperlink { get; } = new(
        Title: "Insert Hyperlink",
        EditTitle: "Edit Hyperlink",
        DisplayPlaceholder: "Text to display",
        AddressPlaceholder: "https://\u2026  or  #BookmarkName for an internal link",
        DisplayLabel: "Display:",
        AddressLabel: "Address:");

    public static ScreenTipDialogText ScreenTip { get; } = new(
        Title: "Set ScreenTip",
        Placeholder: "ScreenTip",
        Label: "ScreenTip:");

    public static BookmarkDialogText Bookmark { get; } = new(
        Title: "Bookmark",
        NamePlaceholder: "Bookmark name",
        NameLabel: "Name:",
        GoToLabel: "Go to:",
        AddButton: "Add",
        GoToButton: "Go To",
        CloseButton: "Close");

    public static LinkBookmarkDialogText LinkBookmark { get; } = new(
        Title: "Link to Bookmark",
        BookmarkLabel: "Bookmark:",
        LinkButton: "Link",
        CloseButton: "Close");

    public static QuickPartDialogText QuickPart { get; } = new(
        Title: "Insert Quick Part",
        SnippetPlaceholder: "Snippet text (one paragraph per line)",
        TextLabel: "Text:");
}
