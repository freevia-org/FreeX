using FreeW.App.Localization;

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
    string CloseButton,
    string EmptyMessage,
    string EmptyTitle);

public sealed record QuickPartDialogText(
    string Title,
    string SnippetPlaceholder,
    string TextLabel);

public static class InsertDialogTextResources
{
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        "Common_OkText", "Common_CancelText", "InsertTextFromFile_PickerTitle",
        "Hyperlink_Insert_Title", "Hyperlink_Edit_Title", "Hyperlink_Display_Placeholder",
        "Hyperlink_Address_Placeholder", "Hyperlink_Display_Label", "Hyperlink_Address_Label",
        "Hyperlink_ScreenTip_Title", "Hyperlink_ScreenTip_Placeholder", "Hyperlink_ScreenTip_Label",
        "Bookmark_Title", "Bookmark_Name_Placeholder", "Bookmark_Name_Label", "Bookmark_GoTo_Label",
        "Bookmark_Add_Button", "Bookmark_GoTo_Button", "Bookmark_Close_Button",
        "LinkBookmark_Title", "LinkBookmark_Bookmark_Label", "LinkBookmark_Link_Button",
        "Bookmark_NoneForLink_Message", "FreeW_ProductName",
        "QuickParts_Insert_Title", "QuickParts_Snippet_Placeholder", "QuickParts_Text_Label",
    ];

    public static string OkButton => Text("Common_OkText");
    public static string CancelButton => Text("Common_CancelText");
    public static string TextFromFilePickerTitle => Text("InsertTextFromFile_PickerTitle");

    public static HyperlinkDialogText Hyperlink => new(
        Title: Text("Hyperlink_Insert_Title"),
        EditTitle: Text("Hyperlink_Edit_Title"),
        DisplayPlaceholder: Text("Hyperlink_Display_Placeholder"),
        AddressPlaceholder: Text("Hyperlink_Address_Placeholder"),
        DisplayLabel: Text("Hyperlink_Display_Label"),
        AddressLabel: Text("Hyperlink_Address_Label"));

    public static ScreenTipDialogText ScreenTip => new(
        Title: Text("Hyperlink_ScreenTip_Title"),
        Placeholder: Text("Hyperlink_ScreenTip_Placeholder"),
        Label: Text("Hyperlink_ScreenTip_Label"));

    public static BookmarkDialogText Bookmark => new(
        Title: Text("Bookmark_Title"),
        NamePlaceholder: Text("Bookmark_Name_Placeholder"),
        NameLabel: Text("Bookmark_Name_Label"),
        GoToLabel: Text("Bookmark_GoTo_Label"),
        AddButton: Text("Bookmark_Add_Button"),
        GoToButton: Text("Bookmark_GoTo_Button"),
        CloseButton: Text("Bookmark_Close_Button"));

    public static LinkBookmarkDialogText LinkBookmark => new(
        Title: Text("LinkBookmark_Title"),
        BookmarkLabel: Text("LinkBookmark_Bookmark_Label"),
        LinkButton: Text("LinkBookmark_Link_Button"),
        CloseButton: Text("Bookmark_Close_Button"),
        EmptyMessage: Text("Bookmark_NoneForLink_Message"),
        EmptyTitle: Text("FreeW_ProductName"));

    public static QuickPartDialogText QuickPart => new(
        Title: Text("QuickParts_Insert_Title"),
        SnippetPlaceholder: Text("QuickParts_Snippet_Placeholder"),
        TextLabel: Text("QuickParts_Text_Label"));

    private static string Text(string resourceKey) => Loc.Get(resourceKey);
}
