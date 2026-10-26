namespace FreeW.Core.Model;

/// <summary>Word-style drop-cap placement choices retained in the shared document model.</summary>
public enum DropCapPosition
{
    Dropped,
    InMargin
}

/// <summary>
/// Renderer-neutral drop-cap layout intent. Renderers consume this alongside the split leading run
/// instead of rediscovering drop caps from a large font size.
/// </summary>
public sealed record DropCapLayoutIntent(
    DropCapPosition Position,
    int LineSpan,
    double SizePt,
    double DistanceFromTextPt);

/// <summary>
/// Pure, view-independent paragraph run operations for the Home/Insert ribbon: applying a drop cap
/// (an enlarged, bold leading letter), retaining shared layout intent, and clearing all run formatting
/// back to the document default. Both operate on a model <see cref="Paragraph"/> in place so they are
/// testable without WPF; the editor wires them through its undo/redo bus and re-renders.
/// </summary>
public static class DropCap
{
    /// <summary>The default drop-cap glyph size in points (a Word-like enlarged leading capital).</summary>
    public const double DefaultSizePt = 42;

    /// <summary>The default number of text lines the cap should span.</summary>
    public const int DefaultLineSpan = 3;

    /// <summary>The default distance between the cap box and adjacent body text, in points.</summary>
    public const double DefaultDistanceFromTextPt = 6;

    /// <summary>
    /// Applies a drop cap to <paramref name="paragraph"/>: splits its first run so the leading character
    /// becomes its own run rendered at <paramref name="sizePt"/> points and bold, while the remainder of
    /// that run keeps its original formatting. The rest of the paragraph's runs are untouched. The
    /// paragraph also receives a <see cref="DropCapLayoutIntent"/> so renderers can plan wrapping and
    /// placement without local font-size heuristics. A no-op when the paragraph has no leading text run
    /// (e.g. it is empty or starts with an image/marker run). Mutates the paragraph in place.
    /// </summary>
    /// <param name="paragraph">The paragraph to enlarge the first letter of.</param>
    /// <param name="position">The placement mode to retain for layout/rendering.</param>
    /// <param name="sizePt">The drop-cap glyph size in points (defaults to <see cref="DefaultSizePt"/>).</param>
    /// <param name="lineSpan">The number of text lines the cap should span.</param>
    /// <param name="distanceFromTextPt">The distance between the cap box and adjacent body text.</param>
    public static void ApplyDropCap(
        Paragraph paragraph,
        DropCapPosition position = DropCapPosition.Dropped,
        double sizePt = DefaultSizePt,
        int lineSpan = DefaultLineSpan,
        double distanceFromTextPt = DefaultDistanceFromTextPt)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        // Find the first run that carries literal text; the leading character of that run becomes the cap.
        var firstTextIndex = paragraph.Runs.FindIndex(r => r.Text.Length > 0);
        if (firstTextIndex < 0)
            return; // nothing to enlarge (empty paragraph, or only image/marker runs)

        var first = paragraph.Runs[firstTextIndex];
        var capFormatting = first.Formatting with { Bold = true, FontSizePt = sizePt };

        // r193: the cap is one TEXT ELEMENT, not one UTF-16 char. `first.Text[..1]` split anything
        // outside the BMP down the middle of its surrogate pair -- applying Drop Cap to a paragraph
        // starting with an emoji left a lone high surrogate in the cap run and a lone low surrogate
        // at the head of the remainder. That is not merely a rendering glitch here: this codebase
        // treats a lone surrogate in model text as XML-illegal, and the sanitizer chokepoints abort
        // the WHOLE save when one reaches a writer. Taking a grapheme cluster also keeps a base
        // letter with its combining marks together, which is what a drop cap should show anyway.
        var capLength = System.Globalization.StringInfo.GetNextTextElementLength(first.Text);

        // Carry the leading character into its own enlarged, bold run; the remainder keeps the original
        // formatting on the existing run (preserving any run-level marks such as a hyperlink).
        if (first.Text.Length == capLength)
        {
            first.Formatting = capFormatting;
            paragraph.DropCap = BuildIntent(position, sizePt, lineSpan, distanceFromTextPt);
            return;
        }

        var capRun = new Run(first.Text[..capLength], capFormatting)
        {
            HyperlinkUrl = first.HyperlinkUrl,
            HyperlinkAnchor = first.HyperlinkAnchor,
            // r163 wave B taught the canonical run copiers (DocumentModelCloner.CloneRunCore,
            // RevisionEditPlanner.CloneRunWithText, CommentCommands.CloneRun) to carry a run's
            // character-style link across a split; this hand-written composer needs the same fix
            // or the drop-cap letter silently unlinks from its style.
            StyleId = first.StyleId
        };
        first.Text = first.Text[capLength..];
        paragraph.Runs.Insert(firstTextIndex, capRun);
        paragraph.DropCap = BuildIntent(position, sizePt, lineSpan, distanceFromTextPt);
    }

    /// <summary>
    /// Clears all character formatting in <paramref name="paragraph"/> and removes any retained
    /// drop-cap layout intent: every run's <see cref="Run.Formatting"/> is reset to
    /// <see cref="RunFormatting.Default"/> while its text (and any run-level marks such as hyperlinks)
    /// is preserved. Mutates the paragraph in place.
    /// </summary>
    public static void ClearFormatting(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        foreach (var run in paragraph.Runs)
            run.Formatting = RunFormatting.Default;
        paragraph.DropCap = null;
    }

    private static DropCapLayoutIntent BuildIntent(
        DropCapPosition position,
        double sizePt,
        int lineSpan,
        double distanceFromTextPt) =>
        new(
            position,
            Math.Max(1, lineSpan),
            Math.Max(1, sizePt),
            Math.Max(0, distanceFromTextPt));
}
