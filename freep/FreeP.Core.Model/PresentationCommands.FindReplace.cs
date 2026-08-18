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

    // Captured on Apply for Revert: one entry per run the match touched (more than one when
    // the match spanned a run/formatting boundary), in ascending run-index order.
    private readonly List<(int RunIndex, string OriginalText)> _capturedRuns = new();

    public ReplaceOneCommand(TextSearchMatch match, string replacement)
    {
        _match       = match;
        _replacement = replacement;
    }

    public string Label => "Replace";

    public void Apply(Presentation p)
    {
        _capturedRuns.Clear();

        var body = ResolveTextBody(p, _match);
        if (body is null) return;

        // The match was captured when Find Next ran, but the dialog is modeless: the user can edit
        // the slide canvas in between and only retyping the search text re-runs the search. The
        // shape still resolves (it is found by its stable id) while the paragraph/run structure
        // underneath it may have shifted, so every offset here has to be re-checked. A stale match
        // is a no-op, not a crash.
        if (!FindReplaceMatchResolver.TryResolveSpan(body, _match, out var paragraph, out int endRunIndex))
            return;

        for (int ri = _match.RunIndex; ri <= endRunIndex; ri++)
            _capturedRuns.Add((ri, paragraph.Runs[ri].Text));

        FindReplaceRunSpanWriter.ApplyReplacement(paragraph, _match, endRunIndex, _replacement);
    }

    public void Revert(Presentation p)
    {
        if (_capturedRuns.Count == 0) return;

        var body = ResolveTextBody(p, _match);
        if (body is null) return;

        // Undo can also run after the canvas changed underneath the captured match.
        if (_match.ParagraphIndex < 0 || _match.ParagraphIndex >= body.Paragraphs.Count)
            return;

        var para = body.Paragraphs[_match.ParagraphIndex];
        foreach (var (runIndex, originalText) in _capturedRuns)
        {
            if (runIndex >= 0 && runIndex < para.Runs.Count)
                para.Runs[runIndex].Text = originalText;
        }
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

    // Captured on Apply: one entry per (paragraph, run) touched, holding that run's
    // pristine pre-replacement text so Revert can restore it. A match that spans a
    // run/formatting boundary touches more than one run, so this is keyed per run, not
    // per match.
    private readonly List<(int SlideIndex, uint ShapeId, TextMatchLocation Location,
        int TableRow, int TableCol, int ParagraphIndex, int RunIndex, string OriginalText)> _applied = new();

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

        // Group by the paragraph the matches live in -- not by run, because a match may now
        // span more than one run (a query that straddles a formatting boundary).  Within a
        // paragraph, process matches in REVERSE position order (highest starting run/offset
        // first) so replacing one match cannot shift the char-offsets of matches that precede
        // it in the same paragraph; run index only ever increases left-to-right, so ordering
        // by (RunIndex desc, CharStart desc) reproduces reverse paragraph-position order.
        var grouped = matches
            .GroupBy(m => (m.SlideIndex, m.ShapeId, m.Location, m.TableRow, m.TableCol, m.ParagraphIndex))
            .ToList();

        foreach (var group in grouped)
        {
            var key  = group.Key;
            var body = ResolveTextBody(p, key.SlideIndex, key.ShapeId, key.Location, key.TableRow, key.TableCol);
            if (body is null) continue;
            if (key.ParagraphIndex < 0 || key.ParagraphIndex >= body.Paragraphs.Count) continue;
            var paragraph = body.Paragraphs[key.ParagraphIndex];

            var touchedRuns = new HashSet<int>();

            foreach (var m in group.OrderByDescending(m => m.RunIndex).ThenByDescending(m => m.CharStart))
            {
                int endRunIndex = m.ResolvedEndRunIndex;
                if (m.RunIndex < 0 || endRunIndex < m.RunIndex || endRunIndex >= paragraph.Runs.Count)
                    continue;
                if (m.CharStart < 0 || m.CharStart > paragraph.Runs[m.RunIndex].Text.Length)
                    continue;

                // Capture each touched run's ORIGINAL text exactly once, before the first
                // edit touches it (matches are processed right-to-left, so the first touch
                // is always the pristine text).
                for (int ri = m.RunIndex; ri <= endRunIndex; ri++)
                {
                    if (touchedRuns.Add(ri))
                    {
                        _applied.Add((key.SlideIndex, key.ShapeId, key.Location, key.TableRow, key.TableCol,
                            key.ParagraphIndex, ri, paragraph.Runs[ri].Text));
                    }
                }

                FindReplaceRunSpanWriter.ApplyReplacement(paragraph, m, endRunIndex, _replacement);
            }
        }
    }

    public void Revert(Presentation p)
    {
        foreach (var entry in _applied)
        {
            var body = ResolveTextBody(p, entry.SlideIndex, entry.ShapeId, entry.Location, entry.TableRow, entry.TableCol);
            if (body is null) continue;
            if (entry.ParagraphIndex < 0 || entry.ParagraphIndex >= body.Paragraphs.Count)
                continue;
            var para = body.Paragraphs[entry.ParagraphIndex];
            if (entry.RunIndex >= 0 && entry.RunIndex < para.Runs.Count)
                para.Runs[entry.RunIndex].Text = entry.OriginalText;
        }
        _applied.Clear();
    }

    public bool HasEffect(Presentation p)
        => PresentationTextSearch.FindAll(p, _query, _opts).Count > 0;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TextBody? ResolveTextBody(
        Presentation p, int slideIndex, uint shapeId, TextMatchLocation location, int tableRow, int tableCol)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        var slide = p.Slides[slideIndex];

        return location switch
        {
            TextMatchLocation.Notes     => slide.Notes,
            TextMatchLocation.TableCell => ResolveTableCell(slide, shapeId, tableRow, tableCol),
            _                           => ResolveShapeBody(slide, shapeId),
        };
    }

    private static TextBody? ResolveShapeBody(Slide slide, uint shapeId)
        => FindShape(slide, shapeId)?.TextBody;

    private static TextBody? ResolveTableCell(Slide slide, uint shapeId, int tableRow, int tableCol)
    {
        var shape = FindShape(slide, shapeId);
        if (shape?.Table is null) return null;
        if (tableRow < 0 || tableRow >= shape.Table.Rows.Count) return null;
        var row = shape.Table.Rows[tableRow];
        if (tableCol < 0 || tableCol >= row.Cells.Count) return null;
        return row.Cells[tableCol].TextBody;
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

    /// <summary>
    /// Validates and resolves a (possibly run-spanning) match against the live model,
    /// returning the owning paragraph and the resolved ending run index. Handles both the
    /// single-run case and matches whose <see cref="TextSearchMatch.ResolvedEndRunIndex"/>
    /// differs from <see cref="TextSearchMatch.RunIndex"/>.
    /// </summary>
    public static bool TryResolveSpan(TextBody body, TextSearchMatch match, out Paragraph paragraph, out int endRunIndex)
    {
        paragraph   = null!;
        endRunIndex = -1;

        if (match.ParagraphIndex < 0 || match.ParagraphIndex >= body.Paragraphs.Count)
            return false;

        var candidateParagraph = body.Paragraphs[match.ParagraphIndex];
        int end = match.ResolvedEndRunIndex;
        if (match.RunIndex < 0 || end < match.RunIndex || end >= candidateParagraph.Runs.Count)
            return false;

        var startRun = candidateParagraph.Runs[match.RunIndex];
        if (match.CharStart < 0 || match.CharStart > startRun.Text.Length)
            return false;

        if (end == match.RunIndex)
        {
            if (match.CharEnd < match.CharStart || match.CharEnd > startRun.Text.Length)
                return false;
        }
        else
        {
            var endRun = candidateParagraph.Runs[end];
            int endOffset = match.ResolvedEndCharOffset;
            if (endOffset < 0 || endOffset > endRun.Text.Length)
                return false;
        }

        paragraph   = candidateParagraph;
        endRunIndex = end;
        return true;
    }
}

/// <summary>
/// Applies a single (possibly run-spanning) match's replacement text directly onto the model,
/// reconstructing the touched runs rather than flattening the paragraph into one run. The
/// replacement text always adopts the formatting of the run the match STARTED in: that run
/// keeps its untouched prefix followed by the replacement text, any runs fully swallowed by
/// the match in between are emptied (their formatting becomes moot -- they contain no text),
/// and the run the match ENDED in keeps only its untouched suffix. Formatting on every run
/// outside the matched span is left completely untouched.
/// </summary>
internal static class FindReplaceRunSpanWriter
{
    public static void ApplyReplacement(Paragraph paragraph, TextSearchMatch match, int endRunIndex, string replacement)
    {
        var startRun = paragraph.Runs[match.RunIndex];

        if (endRunIndex == match.RunIndex)
        {
            startRun.Text = startRun.Text.Remove(match.CharStart, match.CharEnd - match.CharStart)
                                          .Insert(match.CharStart, replacement);
            return;
        }

        var endRun = paragraph.Runs[endRunIndex];
        int endOffset = match.ResolvedEndCharOffset;

        // Runs strictly between the start and end run are entirely consumed by the match.
        for (int ri = match.RunIndex + 1; ri < endRunIndex; ri++)
            paragraph.Runs[ri].Text = string.Empty;

        endRun.Text   = endRun.Text.Substring(endOffset);
        startRun.Text = startRun.Text.Substring(0, match.CharStart) + replacement;
    }
}
