using Free.Shared.Drawing;
using Free.Shared.TextSearch;

namespace FreeP.Core.Model;

// ══════════════════════════════════════════════════════════════════════════════
//  FIND & REPLACE  — framework-free search types + enumerator  (Wave 12B)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Options that control how <see cref="PresentationTextSearch"/> matches text.
/// </summary>
public sealed class TextSearchOptions
{
    /// <summary>Case-sensitive comparison. Default: false (case-insensitive).</summary>
    public bool MatchCase { get; init; }

    /// <summary>
    /// Whole-word matching: the match must be bordered by non-word characters or string edges.
    /// Default: false.
    /// </summary>
    public bool WholeWord { get; init; }
}

/// <summary>
/// Identifies the type of container that holds a matched text run.
/// </summary>
public enum TextMatchLocation
{
    /// <summary>Run inside a shape's TextBody.</summary>
    ShapeBody,

    /// <summary>Run inside a table cell's TextBody.</summary>
    TableCell,

    /// <summary>Run inside the slide's speaker notes TextBody.</summary>
    Notes,
}

/// <summary>
/// Describes a single text-search hit.
/// </summary>
public sealed class TextSearchMatch
{
    /// <summary>Zero-based slide index within <see cref="Presentation.Slides"/>.</summary>
    public int SlideIndex { get; init; }

    /// <summary>
    /// The shape that contains the match.  Always set (even for table cells and notes,
    /// where it is the table/placeholder shape or a virtual id).
    /// </summary>
    public uint ShapeId { get; init; }

    /// <summary>Where the match lives within the shape.</summary>
    public TextMatchLocation Location { get; init; }

    /// <summary>
    /// For <see cref="TextMatchLocation.TableCell"/>: zero-based row index.
    /// -1 for other locations.
    /// </summary>
    public int TableRow { get; init; } = -1;

    /// <summary>
    /// For <see cref="TextMatchLocation.TableCell"/>: zero-based column index.
    /// -1 for other locations.
    /// </summary>
    public int TableCol { get; init; } = -1;

    /// <summary>Zero-based paragraph index within the TextBody.</summary>
    public int ParagraphIndex { get; init; }

    /// <summary>Zero-based run index the match starts in.</summary>
    public int RunIndex { get; init; }

    /// <summary>Zero-based start character offset within the <see cref="RunIndex"/> run's text.</summary>
    public int CharStart { get; init; }

    /// <summary>
    /// Exclusive end character offset of the match. When the match is contained entirely
    /// within <see cref="RunIndex"/> (the common case), this is the offset within that same
    /// run. When the match spans a run boundary (<see cref="EndRunIndex"/> differs from
    /// <see cref="RunIndex"/>), this instead equals the full length of the <see cref="RunIndex"/>
    /// run's text -- i.e. the match runs to the end of the starting run -- and
    /// <see cref="EndCharOffset"/> carries the offset within the ending run.
    /// </summary>
    public int CharEnd { get; init; }

    /// <summary>
    /// Zero-based run index the match ends in. Equal to <see cref="RunIndex"/> unless the
    /// match spans a formatting/run boundary (e.g. a query that straddles a bold word).
    /// Defaults to -1, meaning "same as <see cref="RunIndex"/>" for matches constructed
    /// without setting it (keeps older single-run call sites source-compatible).
    /// </summary>
    public int EndRunIndex { get; init; } = -1;

    /// <summary>
    /// Exclusive end character offset within the <see cref="EndRunIndex"/> run's text.
    /// Only meaningful when <see cref="EndRunIndex"/> differs from <see cref="RunIndex"/>.
    /// Defaults to -1, meaning "use <see cref="CharEnd"/>" (the single-run case).
    /// </summary>
    public int EndCharOffset { get; init; } = -1;

    /// <summary>Resolved run index the match ends in (<see cref="EndRunIndex"/> if set, else <see cref="RunIndex"/>).</summary>
    public int ResolvedEndRunIndex => EndRunIndex < 0 ? RunIndex : EndRunIndex;

    /// <summary>Resolved exclusive end offset within <see cref="ResolvedEndRunIndex"/> (<see cref="EndCharOffset"/> if set, else <see cref="CharEnd"/>).</summary>
    public int ResolvedEndCharOffset => EndCharOffset < 0 ? CharEnd : EndCharOffset;

    /// <summary>The exact text that was matched.</summary>
    public string MatchedText { get; init; } = string.Empty;
}

/// <summary>
/// Framework-free enumerator that walks all text bodies in a <see cref="Presentation"/>
/// and reports every occurrence of a search query as <see cref="TextSearchMatch"/> objects.
///
/// Coverage: shape TextBody (including groups), table cell TextBody, slide Notes.
/// Comments are intentionally excluded (read-only annotation data, not editable content).
/// </summary>
public static class PresentationTextSearch
{
    /// <summary>
    /// Returns all matches for <paramref name="query"/> across all slides in the presentation.
    /// Returns an empty list if query is null or empty.
    /// </summary>
    public static List<TextSearchMatch> FindAll(
        Presentation presentation,
        string? query,
        TextSearchOptions? opts = null)
    {
        var results = new List<TextSearchMatch>();
        if (string.IsNullOrEmpty(query)) return results;
        opts ??= new TextSearchOptions();

        for (int si = 0; si < presentation.Slides.Count; si++)
            SearchSlide(presentation.Slides[si], si, query, opts, results);

        return results;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void SearchSlide(
        Slide slide, int slideIndex,
        string query, TextSearchOptions opts,
        List<TextSearchMatch> results)
    {
        foreach (var shape in slide.Shapes)
            SearchShape(shape, slideIndex, query, opts, results);

        if (slide.Notes is not null)
        {
            uint notesVirtualId = (uint)(0xFFFF0000 + slideIndex);
            SearchTextBody(slide.Notes, slideIndex, notesVirtualId,
                TextMatchLocation.Notes, -1, -1, query, opts, results);
        }
    }

    private static void SearchShape(
        SlideShape shape, int slideIndex,
        string query, TextSearchOptions opts,
        List<TextSearchMatch> results)
    {
        if (shape.Kind == SlideShapeKind.Group)
        {
            foreach (var child in shape.Children)
                SearchShape(child, slideIndex, query, opts, results);
            return;
        }

        if (shape.TextBody is not null)
        {
            SearchTextBody(shape.TextBody, slideIndex, shape.Id,
                TextMatchLocation.ShapeBody, -1, -1, query, opts, results);
        }

        if (shape.Kind == SlideShapeKind.Table && shape.Table is not null)
        {
            for (int r = 0; r < shape.Table.Rows.Count; r++)
            {
                var row = shape.Table.Rows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.TextBody is not null)
                    {
                        SearchTextBody(cell.TextBody, slideIndex, shape.Id,
                            TextMatchLocation.TableCell, r, c, query, opts, results);
                    }
                }
            }
        }
    }

    private static void SearchTextBody(
        TextBody body,
        int slideIndex, uint shapeId,
        TextMatchLocation location,
        int tableRow, int tableCol,
        string query, TextSearchOptions opts,
        List<TextSearchMatch> results)
    {
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            var para = body.Paragraphs[pi];
            if (para.Runs.Count == 0) continue;

            // Search the paragraph's runs as one concatenated string rather than run-by-run:
            // a run boundary is a formatting seam (e.g. one bold word mid-sentence) invisible
            // to the user on the slide, so a query whose characters straddle that seam must
            // still be found. Each run's start offset within the concatenated text is recorded
            // so a match can be mapped back to the run(s) that actually contain it.
            var runStarts = new int[para.Runs.Count];
            var sb = new System.Text.StringBuilder();
            for (int ri = 0; ri < para.Runs.Count; ri++)
            {
                runStarts[ri] = sb.Length;
                var runText = para.Runs[ri].Text;
                if (!string.IsNullOrEmpty(runText)) sb.Append(runText);
            }

            if (sb.Length == 0) continue;
            var paragraphText = sb.ToString();

            foreach (var (start, length) in PlainTextSearch.FindAll(
                paragraphText,
                query,
                opts.MatchCase,
                opts.WholeWord))
            {
                int end = start + length;
                int startRunIndex = FindRunIndexForOffset(runStarts, start);
                int endRunIndex   = FindRunIndexForOffset(runStarts, end - 1);

                int charStart = start - runStarts[startRunIndex];
                int charEnd, endCharOffset;
                if (endRunIndex == startRunIndex)
                {
                    charEnd       = end - runStarts[startRunIndex];
                    endCharOffset = charEnd;
                }
                else
                {
                    // Match spans a run boundary: CharEnd marks "runs to the end of the
                    // starting run" and EndCharOffset carries the offset within the run
                    // the match actually ends in.
                    charEnd       = para.Runs[startRunIndex].Text.Length;
                    endCharOffset = end - runStarts[endRunIndex];
                }

                results.Add(new TextSearchMatch
                {
                    SlideIndex     = slideIndex,
                    ShapeId        = shapeId,
                    Location       = location,
                    TableRow       = tableRow,
                    TableCol       = tableCol,
                    ParagraphIndex = pi,
                    RunIndex       = startRunIndex,
                    CharStart      = charStart,
                    CharEnd        = charEnd,
                    EndRunIndex    = endRunIndex,
                    EndCharOffset  = endCharOffset,
                    MatchedText    = paragraphText.Substring(start, length),
                });
            }
        }
    }

    /// <summary>
    /// Returns the index of the run whose span (in the paragraph's concatenated text)
    /// contains character offset <paramref name="offset"/>. Runs are laid out contiguously
    /// with no gaps, so the last run whose start is at or before the offset is the answer.
    /// </summary>
    private static int FindRunIndexForOffset(int[] runStarts, int offset)
    {
        for (int ri = runStarts.Length - 1; ri >= 0; ri--)
        {
            if (offset >= runStarts[ri]) return ri;
        }
        return 0;
    }
}
