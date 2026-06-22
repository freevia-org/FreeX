namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Shared print-preview navigation state for renderer toolbars: 1-based page numbers, normalized total pages,
/// button enablement, and the "Page X of N" status text.
/// </summary>
public sealed record PrintPreviewNavigationState(
    int CurrentPage,
    int TotalPages,
    bool CanGoFirst,
    bool CanGoPrevious,
    bool CanGoNext,
    bool CanGoLast,
    string StatusText)
{
    public static PrintPreviewNavigationState Create(int currentPage, int totalPages)
    {
        var navigator = PrintPreviewPageNavigator
            .Create(totalPages)
            .JumpTo(currentPage <= 1 ? 0 : currentPage - 1);
        var normalizedTotalPages = Math.Max(1, navigator.PageCount);

        return new PrintPreviewNavigationState(
            navigator.CurrentPageNumber,
            normalizedTotalPages,
            CanGoFirst: navigator.CanGoPrevious,
            CanGoPrevious: navigator.CanGoPrevious,
            CanGoNext: navigator.CanGoNext,
            CanGoLast: navigator.CanGoNext,
            StatusText: navigator.Caption);
    }
}
