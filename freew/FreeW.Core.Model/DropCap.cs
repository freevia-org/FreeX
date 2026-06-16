namespace FreeW.Core.Model;

/// <summary>
/// Pure, view-independent paragraph run operations for the Home/Insert ribbon: applying a drop cap
/// (an enlarged, bold leading letter) and clearing all run formatting back to the document default.
/// Both operate on a model <see cref="Paragraph"/> in place so they are testable without WPF; the
/// editor wires them through its undo/redo bus and re-renders.
/// </summary>
public static class DropCap
{
    /// <summary>The default drop-cap glyph size in points (a Word-like enlarged leading capital).</summary>
    public const double DefaultSizePt = 42;

    /// <summary>
    /// Applies a drop cap to <paramref name="paragraph"/>: splits its first run so the leading character
    /// becomes its own run rendered at <paramref name="sizePt"/> points and bold, while the remainder of
    /// that run keeps its original formatting. The rest of the paragraph's runs are untouched. A no-op
    /// when the paragraph has no leading text run (e.g. it is empty or starts with an image/marker run),
    /// or when the first character is already its own run at the requested size. Mutates the paragraph
    /// in place.
    /// </summary>
    /// <param name="paragraph">The paragraph to enlarge the first letter of.</param>
    /// <param name="sizePt">The drop-cap glyph size in points (defaults to <see cref="DefaultSizePt"/>).</param>
    public static void ApplyDropCap(Paragraph paragraph, double sizePt = DefaultSizePt)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        // Find the first run that carries literal text; the leading character of that run becomes the cap.
        var firstTextIndex = paragraph.Runs.FindIndex(r => r.Text.Length > 0);
        if (firstTextIndex < 0)
            return; // nothing to enlarge (empty paragraph, or only image/marker runs)

        var first = paragraph.Runs[firstTextIndex];
        var capFormatting = first.Formatting with { Bold = true, FontSizePt = sizePt };

        // Carry the leading character into its own enlarged, bold run; the remainder keeps the original
        // formatting on the existing run (preserving any run-level marks such as a hyperlink).
        if (first.Text.Length == 1)
        {
            first.Formatting = capFormatting;
            return;
        }

        var capRun = new Run(first.Text[..1], capFormatting)
        {
            HyperlinkUrl = first.HyperlinkUrl,
            HyperlinkAnchor = first.HyperlinkAnchor
        };
        first.Text = first.Text[1..];
        paragraph.Runs.Insert(firstTextIndex, capRun);
    }

    /// <summary>
    /// Clears all character formatting in <paramref name="paragraph"/>: every run's
    /// <see cref="Run.Formatting"/> is reset to <see cref="RunFormatting.Default"/> while its text (and
    /// any run-level marks such as hyperlinks) is preserved. Mutates the paragraph in place.
    /// </summary>
    public static void ClearFormatting(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        foreach (var run in paragraph.Runs)
            run.Formatting = RunFormatting.Default;
    }
}
