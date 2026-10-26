namespace FreeW.Core.Model;

/// <summary>
/// Shared target and formatting policy for paragraph-level operations inside a shape text body.
/// The child path is relative to the owning drawing-group run, so nested hosts do not need to
/// duplicate group traversal or accidentally format the containing document paragraph.
/// </summary>
public static class ShapeTextFormattingPlanner
{
    public static bool CanApplyParagraphAlignment(Shape? shape) => shape is { HasText: true };

    public static bool TryGetShape(
        TextDocument document,
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int>? childPath,
        out Shape shape)
    {
        ArgumentNullException.ThrowIfNull(document);

        shape = null!;
        if (paragraphIndex < 0 || paragraphIndex >= document.Blocks.Count
            || document.Blocks[paragraphIndex] is not Paragraph paragraph
            || runIndex < 0 || runIndex >= paragraph.Runs.Count)
            return false;

        var run = paragraph.Runs[runIndex];
        if (childPath is { Count: > 0 })
        {
            if (run.DrawingGroup is not { } group
                || !DrawingGroupChildPathResolver.TryGetChild(
                    group, childPath, out _, out var child)
                || child is not Shape nestedShape)
                return false;

            shape = nestedShape;
            return true;
        }

        if (run.Shape is not { } directShape)
            return false;

        shape = directShape;
        return true;
    }

    public static ParagraphFormatting WithAlignment(
        ParagraphFormatting formatting,
        TextAlignment alignment) =>
        formatting with { Alignment = alignment };
}

/// <summary>
/// Applies paragraph alignment to the text paragraphs owned by one direct or nested shape leaf.
/// Group transforms, child offsets, and sibling objects are intentionally outside this command's
/// mutation surface and therefore remain unchanged across apply, undo, and redo.
/// </summary>
public sealed class SetShapeTextParagraphAlignmentCommand(
    int paragraphIndex,
    int runIndex,
    TextAlignment alignment,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private ParagraphFormatting[]? _previous;
    private bool _applied;

    public string Label => "Shape Paragraph Alignment";

    // r203 census, fixed r204: an equal-value setter -- re-confirming what the ribbon already shows
    // pushed an undo entry that changed nothing, and that push clears redo.
    public bool HasEffect(IDocumentCommandContext context) =>
        ShapeTextFormattingPlanner.TryGetShape(
            context.Document, paragraphIndex, runIndex, childPath, out var shape)
        && ShapeTextFormattingPlanner.CanApplyParagraphAlignment(shape)
        && shape.TextParagraphs.Any(paragraph =>
            !Equals(
                paragraph.Formatting,
                ShapeTextFormattingPlanner.WithAlignment(paragraph.Formatting, alignment)));

    public void Apply(IDocumentCommandContext context)
    {
        if (!ShapeTextFormattingPlanner.TryGetShape(
                context.Document, paragraphIndex, runIndex, childPath, out var shape)
            || !ShapeTextFormattingPlanner.CanApplyParagraphAlignment(shape))
            return;

        _previous ??= shape.TextParagraphs.Select(paragraph => paragraph.Formatting).ToArray();
        foreach (var paragraph in shape.TextParagraphs)
            paragraph.Formatting = ShapeTextFormattingPlanner.WithAlignment(paragraph.Formatting, alignment);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied
            || _previous is null
            || !ShapeTextFormattingPlanner.TryGetShape(
                context.Document, paragraphIndex, runIndex, childPath, out var shape))
            return;

        for (var index = 0; index < shape.TextParagraphs.Count && index < _previous.Length; index++)
            shape.TextParagraphs[index].Formatting = _previous[index];
        _applied = false;
    }
}
