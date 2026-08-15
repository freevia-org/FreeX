namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Resolves the model foreground written when a native renderer commits an edited text run.
/// A renderer must report only a locally-authored foreground; an inherited/effective display
/// color is not an explicit document color. Visually hidden runs retain their model formatting
/// because their native foreground is temporary presentation chrome.
/// </summary>
public static class DocumentRunForegroundCommitPlanner
{
    public static string? ResolveColorHex(
        string? retainedColorHex,
        string? localColorHex,
        bool isVisuallyHidden) =>
        isVisuallyHidden ? retainedColorHex : localColorHex;
}
