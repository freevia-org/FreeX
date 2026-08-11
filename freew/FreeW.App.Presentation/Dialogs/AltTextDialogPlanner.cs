using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Dialogs;

public sealed record AltTextDialogText(
    string Title,
    string DescriptionLabel,
    string ImageSelectionRequiredMessage,
    string ShapeSelectionRequiredMessage,
    string ImageSelectionRequiredTitle,
    string OkLabel,
    string CancelLabel);

public static class AltTextDialogPlanner
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("AltText_Dialog_Title", "Alt Text"),
        new("AltText_Description_Label", "Description:"),
        new("AltText_ImageSelectionRequired_Message", "Select an image first, then choose Alt Text."),
        new("AltText_ShapeSelectionRequired_Message", "Select a shape or WordArt first, then choose Alt Text."),
        new("FreeW_ProductName", "FreeW"),
        new("Common_Ok", "OK"),
        new("Common_Cancel", "Cancel"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static AltTextDialogText ResolveText(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText),
            Texts[6].Resolve(getText));
}
