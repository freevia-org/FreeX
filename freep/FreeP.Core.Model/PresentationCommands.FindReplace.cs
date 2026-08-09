using Free.Shared.Drawing;

namespace FreeP.Core.Model;

// ══════════════════════════════════════════════════════════════════════════════
//  FIND & REPLACE COMMANDS  (Wave 12B)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the text in a single <see cref="TextSearchMatch"/> with
/// <paramref name="replacement"/> text.  The matched run is split around the
/// match so only the matched characters are swapped; surrounding text in the
/// same run is preserved.
/// </summary>
public sealed class ReplaceOneCommand : IPresentationCommand
{
    private readonly TextSearchMatch _match;
    private readonly string _replacement;

    // Captured on Apply for Revert
    private string? _capturedOriginalRunText;
    private int     _capturedRunIndex;

    public ReplaceOneCommand(TextSearchMatch match, string replacement)
    {
        _match       = match;
        _replacement = replacement;
    }

    public string Label => "Replace";

    public void Apply(Presentation p)
    {
        var body = ResolveTextBody(p, _match);
        if (body is null) return;

        // The match was captured when Find Next ran, but the dialog is modeless: the user can edit
        // the slide canvas in between and only retyping the search text re-runs the search. The
        // shape still resolves (it is found by its stable id) while the paragraph/run structure
        // underneath it may have shifted, so every offset here has to be re-checked. A stale match
        // is a no-op, not a crash.
        if (!FindReplaceMatchResolver.TryResolveRun(body, _match, out var run))
            return;

        _capturedOriginalRunText = run.Text;
        _capturedRunIndex        = _match.RunIndex;

        // Replace the matched substring in the run.
        run.Text = run.Text.Remove(_match.CharStart, _match.CharEnd - _match.CharStart)
                           .Insert(_match.CharStart, _replacement);
    }

    public void Revert(Presentation p)
    {
        if (_capturedOriginalRunText is null) return;

        var body = ResolveTextBody(p, _match);
        if (body is null) return;

        // Undo can also run after the canvas changed underneath the captured match.
        if (_match.ParagraphIndex < 0 || _match.ParagraphIndex >= body.Paragraphs.Count)
            return;

        var para = body.Paragraphs[_match.ParagraphIndex];
        if (_capturedRunIndex >= 0 && _capturedRunIndex < para.Runs.Count)
            para.Runs[_capturedRunIndex].Text = _capturedOriginalRunText;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TextBody? ResolveTextBody(Presentation p, TextSearchMatch m)
    {
        if (m.SlideIndex < 0 || m.SlideIndex >= p.Slides.Count) return null;
        var slide = p.Slides[m.SlideIndex];

        return m.Location switch
        {
            TextMatchLocation.Notes     => ResolveNotes(slide),
            TextMatchLocation.TableCell => ResolveTableCell(slide, m),
            _                           => ResolveShapeBody(slide, m),
        };
    }

    private static TextBody? ResolveNotes(Slide slide) => slide.Notes;

    private static TextBody? ResolveShapeBody(Slide slide, TextSearchMatch m)
    {
        var shape = FindShape(slide, m.ShapeId);
        return shape?.TextBody;
    }

    private static TextBody? ResolveTableCell(Slide slide, TextSearchMatch m)
    {
        var shape = FindShape(slide, m.ShapeId);
        if (shape?.Table is null) return null;
        if (m.TableRow < 0 || m.TableRow >= shape.Table.Rows.Count) return null;
        var row = shape.Table.Rows[m.TableRow];
        if (m.TableCol < 0 || m.TableCol >= row.Cells.Count) return null;
        return row.Cells[m.TableCol].TextBody;
    }

    private static SlideShape? FindShape(Slide slide, uint shapeId) =>
        ShapeHelper.Find(slide, shapeId);
}

/// <summary>
/// Replaces ALL occurrences of <paramref name="query"/> in the presentation in a
/// single undoable step.  The entire change is captured as a list of (body, run-index,
/// originalText, newText) tuples so Revert can restore every run atomically.
/// </summary>
public sealed class ReplaceAllCommand : IPresentationCommand
{
    private readonly string _query;
    private readonly string _replacement;
    private readonly TextSearchOptions _opts;

    // Captured on Apply
    private readonly List<(TextSearchMatch match, string originalText)> _applied = new();

    public ReplaceAllCommand(string query, string replacement, TextSearchOptions opts)
    {
        _query       = query;
        _replacement = replacement;
        _opts        = opts;
    }

    public string Label => "Replace All";

    public void Apply(Presentation p)
    {
        _applied.Clear();

        var matches = PresentationTextSearch.FindAll(p, _query, _opts);

        // Process matches in REVERSE order within each run so that earlier char-offsets
        // within the same run are not shifted by earlier replacements.  Group by
        // (slideIndex, shapeId, location, row, col, paragraphIndex, runIndex) so the
        // multi-match-in-one-run case is handled correctly.
        var grouped = matches
            .GroupBy(m => (m.SlideIndex, m.ShapeId, m.Location, m.TableRow, m.TableCol, m.ParagraphIndex, m.RunIndex))
            .ToList();

        foreach (var group in grouped)
        {
            var first = group.First();
            var body  = ResolveTextBody(p, first);
            if (body is null) continue;

            if (!FindReplaceMatchResolver.TryResolveRun(body, first, out var run))
                continue;

            // Capture original text once per run.
            string originalText = run.Text;

            // Apply all replacements for this run in descending char-start order.
            string current = originalText;
            foreach (var m in group.OrderByDescending(m => m.CharStart))
            {
                if (m.CharStart < 0 || m.CharEnd < m.CharStart || m.CharEnd > current.Length)
                    continue;
                current = current.Remove(m.CharStart, m.CharEnd - m.CharStart)
                                 .Insert(m.CharStart, _replacement);
            }

            run.Text = current;
            _applied.Add((first, originalText));
        }
    }

    public void Revert(Presentation p)
    {
        foreach (var (match, originalText) in _applied)
        {
            var body = ResolveTextBody(p, match);
            if (body is null) continue;
            if (match.ParagraphIndex < 0 || match.ParagraphIndex >= body.Paragraphs.Count)
                continue;
            var para = body.Paragraphs[match.ParagraphIndex];
            if (match.RunIndex >= 0 && match.RunIndex < para.Runs.Count)
                para.Runs[match.RunIndex].Text = originalText;
        }
        _applied.Clear();
    }

    public bool HasEffect(Presentation p)
        => PresentationTextSearch.FindAll(p, _query, _opts).Count > 0;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TextBody? ResolveTextBody(Presentation p, TextSearchMatch m)
    {
        if (m.SlideIndex < 0 || m.SlideIndex >= p.Slides.Count) return null;
        var slide = p.Slides[m.SlideIndex];

        return m.Location switch
        {
            TextMatchLocation.Notes     => slide.Notes,
            TextMatchLocation.TableCell => ResolveTableCell(slide, m),
            _                           => ResolveShapeBody(slide, m),
        };
    }

    private static TextBody? ResolveShapeBody(Slide slide, TextSearchMatch m)
        => FindShape(slide, m.ShapeId)?.TextBody;

    private static TextBody? ResolveTableCell(Slide slide, TextSearchMatch m)
    {
        var shape = FindShape(slide, m.ShapeId);
        if (shape?.Table is null) return null;
        if (m.TableRow < 0 || m.TableRow >= shape.Table.Rows.Count) return null;
        var row = shape.Table.Rows[m.TableRow];
        if (m.TableCol < 0 || m.TableCol >= row.Cells.Count) return null;
        return row.Cells[m.TableCol].TextBody;
    }

    private static SlideShape? FindShape(Slide slide, uint shapeId) =>
        ShapeHelper.Find(slide, shapeId);
}

/// <summary>
/// Re-validates a captured <see cref="TextSearchMatch"/> against the model as it is now.
/// <para>
/// Find &amp; Replace holds matches captured when the search ran, and the dialog is modeless: the user
/// can delete a paragraph or edit the matched text on the canvas in between, and only retyping the
/// search text re-runs the search. The shape still resolves afterwards because it is found by its
/// stable id, so the paragraph index, run index and character offsets are the parts that go stale
/// and they were used as raw indexers. Replacing (or undoing a replace) after such an edit threw
/// ArgumentOutOfRangeException out of the dialog's click handler with nothing to catch it.
/// </para>
/// </summary>
internal static class FindReplaceMatchResolver
{
    public static bool TryResolveRun(TextBody body, TextSearchMatch match, out Run run)
    {
        run = null!;
        if (match.ParagraphIndex < 0 || match.ParagraphIndex >= body.Paragraphs.Count)
            return false;

        var paragraph = body.Paragraphs[match.ParagraphIndex];
        if (match.RunIndex < 0 || match.RunIndex >= paragraph.Runs.Count)
            return false;

        var candidate = paragraph.Runs[match.RunIndex];
        if (match.CharStart < 0 || match.CharEnd < match.CharStart ||
            match.CharEnd > candidate.Text.Length)
            return false;

        run = candidate;
        return true;
    }
}
