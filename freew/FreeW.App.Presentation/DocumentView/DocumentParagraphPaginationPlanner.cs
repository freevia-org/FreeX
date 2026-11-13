using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns the renderer-neutral paragraph pagination policy for Word's widow/orphan defaults.
/// Platform renderers may still choose their native keep-together primitive, but the decision must
/// come from this planner so WPF and Avalonia compose the same ordinary body paragraphs.
/// </summary>
public static class DocumentParagraphPaginationPlanner
{
    /// <summary>Word's widow/orphan rule: a paragraph never leaves fewer than two lines on either page.</summary>
    public const int WordWidowOrphanMinimumLines = 2;

    /// <summary>
    /// Returns whether an ordinary body paragraph should enter the renderer's keep-together path.
    /// Word's widow control only guards a single stranded first/last LINE; it never holds a whole
    /// paragraph together as one page-break-proof unit, so widow control (omitted/default-on or
    /// explicit) is resolved separately by <see cref="ResolveMinimumWidowOrphanLines"/> and
    /// <see cref="PlanLinesOnCurrentPage"/>. Only an explicit keep-lines-together reaches keep-together.
    /// Table cells and paragraphs containing non-text layout objects remain caller-owned because
    /// their pagination has separate table/drawing contracts.
    /// </summary>
    public static bool ShouldKeepParagraphTogether(
        ParagraphFormatting formatting,
        bool isTableCell = false,
        bool hasNonTextLayoutObject = false)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        if (hasNonTextLayoutObject || isTableCell)
            return formatting.KeepLinesTogether || formatting.WidowControl;

        return formatting.KeepLinesTogether;
    }

    /// <summary>
    /// Word treats an omitted <c>w:widowControl</c> as ON (its application default) and only an explicit
    /// <c>w:val="0"</c> as off. Returns the minimum number of lines a page break may leave at the bottom
    /// (orphan) and top (widow) of a page: 2 with widow control, 0 (unconstrained) without.
    /// </summary>
    public static int ResolveMinimumWidowOrphanLines(ParagraphFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        return IsWidowControlEffective(formatting) ? WordWidowOrphanMinimumLines : 0;
    }

    /// <summary>
    /// True when Word lays the paragraph out with no height at all: its paragraph mark is hidden text
    /// (<c>w:pPr/w:rPr/w:vanish</c>, directly or through its style) and it shows nothing else. Word merges a
    /// hidden mark into the following paragraph, so an otherwise empty paragraph -- the classic spacer
    /// between two tables in HTML-sourced documents -- takes no line and none of its spacing. A paragraph
    /// with any visible run, object, note reference, break or section break keeps its normal layout.
    /// </summary>
    public static bool IsCollapsedHiddenMarkParagraph(TextDocument document, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);

        if (paragraph.SectionBreak is not null || paragraph.DropCap is not null)
            return false;

        var mark = DocumentRunFormattingResolver.Resolve(
            document, paragraph, paragraph.MarkFormatting ?? RunFormatting.Default);
        if (!mark.Hidden)
            return false;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsPageBreak || run.IsColumnBreak)
                return false;
            if (DocumentRunFormattingResolver.Resolve(document, paragraph, run).Hidden)
                continue;
            var bare = string.IsNullOrEmpty(run.Text)
                && run.Image is null && run.Equation is null && run.Shape is null && run.WordArt is null
                && run.Chart is null && run.EmbeddedObject is null && run.SmartArt is null
                && run.PreservedDrawing is null && run.DrawingGroup is null
                && run.FootnoteId is null && run.EndnoteId is null
                && run.FieldKind == RunFieldKind.None && run.ComplexField is null && run.Control is null;
            if (!bare)
                return false;
        }

        return true;
    }

    /// <summary>True when Word applies widow/orphan control to the paragraph (omitted = default on).</summary>
    public static bool IsWidowControlEffective(ParagraphFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        return !formatting.WidowControlIsSet || formatting.WidowControl;
    }

    /// <summary>
    /// Word's page-break decision for one paragraph: given its total line count and how many of those
    /// lines fit in the space left on the current page, returns how many lines stay on the current page
    /// (0 = the whole paragraph moves to the next page). With widow control Word never leaves a single
    /// first line at the page bottom (orphan) nor a single last line at the next page top (widow): it pulls
    /// one more line to the next page, and moves the paragraph whole when that would orphan a line.
    /// Keep-lines-together moves the paragraph whole whenever it does not fit. A caller that is already at
    /// the top of a page must still place what fits (<paramref name="atPageTop"/>), as Word does for a
    /// paragraph taller than a page.
    /// </summary>
    public static int PlanLinesOnCurrentPage(
        int totalLines,
        int linesThatFit,
        bool widowControl,
        bool keepLinesTogether = false,
        bool atPageTop = false)
    {
        if (totalLines <= 0)
            return 0;
        linesThatFit = Math.Clamp(linesThatFit, 0, totalLines);
        if (linesThatFit >= totalLines)
            return totalLines;
        if (atPageTop)
            return Math.Max(1, linesThatFit);
        if (keepLinesTogether)
            return 0;
        if (!widowControl)
            return linesThatFit;

        var onPage = linesThatFit;
        if (totalLines - onPage < WordWidowOrphanMinimumLines)
            onPage = totalLines - WordWidowOrphanMinimumLines;
        return onPage < WordWidowOrphanMinimumLines ? 0 : onPage;
    }
}
