using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Plans block-level Insert-tab mutations from model coordinates. Native renderers resolve their caret
/// to a body block, while placement, generated model content, and atomic replacement semantics live here.
/// </summary>
public static class DocumentBlockInsertionMutationPlanner
{
    public static DocumentBlockReplacementPlan PlanCoverPage(
        TextDocument document,
        CoverPagePreset preset = CoverPagePreset.Default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentBlockReplacementPlan(0, 0, DocumentOps.BuildCoverPage(document, preset));
    }

    public static DocumentBlockReplacementPlan PlanPageBreak(TextDocument document, int caretBlockIndex) =>
        PlanAfterCaret(document, caretBlockIndex, [DocumentOps.CreatePageBreak()]);

    public static DocumentBlockReplacementPlan PlanBlankPage(TextDocument document, int caretBlockIndex) =>
        PlanAfterCaret(document, caretBlockIndex, DocumentOps.BuildBlankPage());

    public static DocumentBlockReplacementPlan PlanHorizontalRule(TextDocument document, int caretBlockIndex) =>
        PlanAfterCaret(document, caretBlockIndex, [DocumentOps.CreateHorizontalRule()]);

    public static DocumentBlockReplacementPlan PlanColumnBreak(TextDocument document, int caretBlockIndex) =>
        PlanAfterCaret(document, caretBlockIndex, [DocumentOps.CreateColumnBreak()]);

    public static DocumentBlockReplacementPlan PlanSectionBreak(
        TextDocument document,
        int caretBlockIndex,
        SectionBreakKind breakKind)
    {
        ArgumentNullException.ThrowIfNull(document);
        return PlanAfterCaret(
            document,
            caretBlockIndex,
            [DocumentOps.CreateSectionBreak(breakKind, document.Page)]);
    }

    private static DocumentBlockReplacementPlan PlanAfterCaret(
        TextDocument document,
        int caretBlockIndex,
        IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(blocks);
        var insertIndex = (int)Math.Clamp((long)caretBlockIndex + 1, 0L, document.Blocks.Count);
        return new DocumentBlockReplacementPlan(insertIndex, 0, blocks);
    }
}
