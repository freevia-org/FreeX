namespace FreeW.App.Presentation.Dialogs;

public sealed record ScreenTipDialogPresentation(
    string Title,
    string Label,
    string Placeholder,
    string InitialScreenTip);

/// <summary>Renderer-neutral presentation and acceptance policy for hyperlink ScreenTips.</summary>
public static class ScreenTipDialogPlanner
{
    public static ScreenTipDialogPresentation Build(string? initialScreenTip)
    {
        var text = InsertDialogTextResources.ScreenTip;
        return new ScreenTipDialogPresentation(
            text.Title,
            text.Label,
            text.Placeholder,
            initialScreenTip ?? string.Empty);
    }

    /// <summary>A blank accepted value intentionally clears the current ScreenTip.</summary>
    public static string PlanAcceptance(string? screenTip) => screenTip?.Trim() ?? string.Empty;
}
