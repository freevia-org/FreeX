namespace FreeW.App.Presentation.Dialogs;

public sealed record HyperlinkDialogText(
    string Title,
    string DisplayPlaceholder,
    string AddressPlaceholder,
    string DisplayLabel,
    string AddressLabel);

public sealed record BookmarkDialogText(
    string Title,
    string NamePlaceholder,
    string NameLabel,
    string GoToLabel,
    string AddButton,
    string GoToButton,
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
        DisplayPlaceholder: "Text to display",
        AddressPlaceholder: "https://\u2026  or  #BookmarkName for an internal link",
        DisplayLabel: "Display:",
        AddressLabel: "Address:");

    public static BookmarkDialogText Bookmark { get; } = new(
        Title: "Bookmark",
        NamePlaceholder: "Bookmark name",
        NameLabel: "Name:",
        GoToLabel: "Go to:",
        AddButton: "Add",
        GoToButton: "Go To",
        CloseButton: "Close");

    public static QuickPartDialogText QuickPart { get; } = new(
        Title: "Insert Quick Part",
        SnippetPlaceholder: "Snippet text (one paragraph per line)",
        TextLabel: "Text:");
}
