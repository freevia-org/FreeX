namespace Free.Shared.AppServices;

/// <summary>
/// The validated, 1-based inclusive page window a print job actually sends, plus a flag for whether
/// the request resolved to a usable window at all.
/// </summary>
public readonly record struct PrintPageWindow(bool IsValid, int FirstPage, int LastPage)
{
    /// <summary>Pages in the window (inclusive); zero when the window is not valid.</summary>
    public int PageCount => IsValid ? Math.Max(0, LastPage - FirstPage + 1) : 0;

    /// <summary>A window that failed validation: not valid, both endpoints zero.</summary>
    public static PrintPageWindow Invalid { get; } = new(false, 0, 0);
}

/// <summary>
/// Resolves the 1-based inclusive page window a print job should send, given the total number of pages
/// the chosen scope produced. This is the framework-neutral core of print page-range handling: "all
/// pages" maps to the whole document, an explicit range clamps to <c>[FromPage, ToPage]</c> with a
/// missing endpoint extending to the matching document edge, and out-of-bounds or inverted ranges are
/// rejected. Pure integer logic with no document or platform coupling, so every app (FreeX, FreeP,
/// FreeW) inherits identical print page-range behaviour.
/// </summary>
public static class PrintPageWindowResolver
{
    /// <summary>
    /// Resolves the whole-document window: pages <c>1..totalPages</c>, valid only when at least one
    /// page exists.
    /// </summary>
    public static PrintPageWindow ResolveAllPages(int totalPages) =>
        totalPages >= 1
            ? new PrintPageWindow(true, 1, totalPages)
            : PrintPageWindow.Invalid;

    /// <summary>
    /// Resolves an explicit, 1-based inclusive range. A missing <paramref name="fromPage"/> extends to
    /// page 1 and a missing <paramref name="toPage"/> extends to <paramref name="totalPages"/>, so
    /// "from 3" or "to 2" work. The window is valid only when the document has at least one page and
    /// <c>1 &lt;= firstPage &lt;= lastPage &lt;= totalPages</c>.
    /// </summary>
    public static PrintPageWindow ResolveRange(int? fromPage, int? toPage, int totalPages)
    {
        var firstPage = fromPage ?? 1;
        var lastPage = toPage ?? totalPages;

        var valid =
            totalPages >= 1 &&
            firstPage >= 1 &&
            lastPage <= totalPages &&
            firstPage <= lastPage;

        return valid
            ? new PrintPageWindow(true, firstPage, lastPage)
            : PrintPageWindow.Invalid;
    }
}
