namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Mutable-by-transition review display state shared by the WPF and Avalonia editors.
/// </summary>
public readonly record struct ReviewDisplayState(
    ReviewDisplayMode DisplayMode,
    bool ShowInsertionsAndDeletions,
    bool ShowComments,
    bool ShowFormatting)
{
    public static ReviewDisplayState Default { get; } = new(
        ReviewDisplayMode.AllMarkup,
        ShowInsertionsAndDeletions: true,
        ShowComments: true,
        ShowFormatting: true);

    public ReviewDisplayPolicy ToPolicy() => new(
        DisplayMode,
        ShowInsertionsAndDeletions,
        ShowComments,
        ShowFormatting);

    public ReviewDisplayState WithDisplayMode(ReviewDisplayMode mode) => this with { DisplayMode = mode };

    public ReviewDisplayState WithShowInsertionsAndDeletions(bool show) =>
        this with { ShowInsertionsAndDeletions = show };

    public ReviewDisplayState WithShowComments(bool show) => this with { ShowComments = show };

    public ReviewDisplayState WithShowFormatting(bool show) => this with { ShowFormatting = show };
}
