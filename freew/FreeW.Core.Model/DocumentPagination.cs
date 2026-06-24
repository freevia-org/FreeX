namespace FreeW.Core.Model;

/// <summary>
/// Immutable result of a pagination computation over a <c>DocumentView</c>'s content. Produced by
/// <c>PaginationEngine.Compute</c> in <c>FreeW.App.Host</c>; stored here in <c>FreeW.Core.Model</c> so
/// the record has no WPF dependency and can be used freely from model/test code.
/// </summary>
/// <param name="PageCount">Total number of pages (always &gt;= 1).</param>
/// <param name="PageBreakYsDip">
/// Cumulative Y offsets (in the editor's DIP coordinate space) of each inter-page boundary, ordered
/// from first to last. A document with <paramref name="PageCount"/> pages has exactly
/// <c>PageCount − 1</c> entries. Entry <c>i</c> (zero-based) is the Y coordinate of the bottom of
/// page <c>i+1</c> / the top of page <c>i+2</c> within the editor's content area, measured from the
/// top of the first content line and accounting for the editor's <c>Padding.Top</c>.
/// </param>
public sealed record DocumentPagination(int PageCount, IReadOnlyList<double> PageBreakYsDip)
{
    /// <summary>A sentinel representing a document with no pagination result yet (single page, no breaks).</summary>
    public static readonly DocumentPagination Empty = new(1, Array.Empty<double>());
}
