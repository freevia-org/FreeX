namespace FreeX.App.Presentation.Dialogs;

public sealed record TextEntryDialogResult(string Text);

public static class TextEntryDialogPlanner
{
    public static TextEntryDialogResult CreateResult(string? text) =>
        new((text ?? "").Trim());
}
