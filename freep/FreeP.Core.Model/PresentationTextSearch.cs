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

    /// <summary>Zero-based run index within the paragraph.</summary>
    public int RunIndex { get; init; }

    /// <summary>Zero-based start character offset within the run's text.</summary>
    public int CharStart { get; init; }

    /// <summary>Exclusive end character offset within the run's text.</summary>
    public int CharEnd { get; init; }

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
            for (int ri = 0; ri < para.Runs.Count; ri++)
            {
                var run = para.Runs[ri];
                var text = run.Text;
                if (string.IsNullOrEmpty(text)) continue;

                foreach (var (start, length) in PlainTextSearch.FindAll(
                    text,
                    query,
                    opts.MatchCase,
                    opts.WholeWord))
                {
                    results.Add(new TextSearchMatch
                    {
                        SlideIndex     = slideIndex,
                        ShapeId        = shapeId,
                        Location       = location,
                        TableRow       = tableRow,
                        TableCol       = tableCol,
                        ParagraphIndex = pi,
                        RunIndex       = ri,
                        CharStart      = start,
                        CharEnd        = start + length,
                        MatchedText    = text.Substring(start, length),
                    });
                }
            }
        }
    }
}
