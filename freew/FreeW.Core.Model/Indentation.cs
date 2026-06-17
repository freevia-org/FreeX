namespace FreeW.Core.Model;

/// <summary>
/// Pure, deterministic paragraph-indentation step helpers. Each method returns a new
/// <see cref="ParagraphFormatting"/> with adjusted indents, leaving the input untouched (records are
/// immutable). Indents are in points throughout, matching <see cref="ParagraphFormatting.IndentLeftPt"/>,
/// <see cref="ParagraphFormatting.IndentRightPt"/>, and <see cref="ParagraphFormatting.FirstLineIndentPt"/>.
///
/// <para>
/// First-line convention: <see cref="ParagraphFormatting.FirstLineIndentPt"/> is positive for a
/// first-line indent (the first line starts further right than the rest) and negative for a hanging
/// indent (the first line starts further left than the wrapped lines). This matches the editor, where
/// the value maps straight to WPF's <c>Paragraph.TextIndent</c> (positive pushes the first line right,
/// negative pulls it left), and the docx round-trip (positive writes <c>w:ind/@w:firstLine</c>).
/// </para>
/// </summary>
public static class Indentation
{
    /// <summary>The default indent step: 0.5 inch = 36 points, matching Word's Increase/Decrease Indent.</summary>
    public const double DefaultStepPt = 36;

    /// <summary>
    /// Increase the left indent by <paramref name="stepPt"/> points (default 36pt = 0.5in). Other
    /// indents are unchanged. The input record is not mutated.
    /// </summary>
    public static ParagraphFormatting IncreaseIndent(ParagraphFormatting f, double stepPt = DefaultStepPt)
    {
        ArgumentNullException.ThrowIfNull(f);
        return f with { IndentLeftPt = f.IndentLeftPt + stepPt };
    }

    /// <summary>
    /// Decrease the left indent by <paramref name="stepPt"/> points (default 36pt = 0.5in), clamped at
    /// zero so the indent never goes negative. Other indents are unchanged. The input is not mutated.
    /// </summary>
    public static ParagraphFormatting DecreaseIndent(ParagraphFormatting f, double stepPt = DefaultStepPt)
    {
        ArgumentNullException.ThrowIfNull(f);
        return f with { IndentLeftPt = Math.Max(0, f.IndentLeftPt - stepPt) };
    }

    /// <summary>
    /// Set the left, right, and first-line indents explicitly (points). Left and right are clamped at
    /// zero; <paramref name="firstLinePt"/> is taken as-is so a negative value models a hanging indent
    /// (see the first-line convention on <see cref="Indentation"/>). The input is not mutated.
    /// </summary>
    public static ParagraphFormatting SetIndents(
        ParagraphFormatting f, double leftPt, double rightPt, double firstLinePt)
    {
        ArgumentNullException.ThrowIfNull(f);
        return f with
        {
            IndentLeftPt = Math.Max(0, leftPt),
            IndentRightPt = Math.Max(0, rightPt),
            FirstLineIndentPt = firstLinePt
        };
    }
}
