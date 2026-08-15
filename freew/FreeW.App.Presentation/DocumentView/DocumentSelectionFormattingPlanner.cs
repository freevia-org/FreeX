using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// A renderer-neutral slice of selected text. Offsets address <see cref="Paragraph.PlainText"/>.
/// <see cref="IncludesParagraphMark"/> lets a renderer preserve the character formatting carried by
/// a selected paragraph break, including an otherwise-empty paragraph.
/// </summary>
public readonly record struct DocumentFormattingTextRange(
    Paragraph Paragraph,
    int StartOffset,
    int EndOffset,
    bool IncludesParagraphMark = false);

/// <summary>
/// Resolves the effective character formatting across arbitrary body, table-cell, header/footer, or
/// drawing-text ranges and projects it into the shared Font-dialog/ribbon state. Renderers identify
/// the selected model ranges; style cascade and mixed-value policy remain shared.
/// </summary>
public static class DocumentSelectionFormattingPlanner
{
    public static FontDialogSelectionState Build(
        TextDocument document,
        RunFormatting current,
        IEnumerable<DocumentFormattingTextRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ranges);

        return FontDialogPlanner.BuildSelectionState(
            current,
            ResolveSelectedFormatting(document, ranges));
    }

    public static IReadOnlyList<RunFormatting> ResolveSelectedFormatting(
        TextDocument document,
        IEnumerable<DocumentFormattingTextRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ranges);

        var selected = new List<RunFormatting>();
        foreach (var range in ranges)
        {
            ArgumentNullException.ThrowIfNull(range.Paragraph);
            AppendRange(document, range, selected);
        }

        return selected;
    }

    private static void AppendRange(
        TextDocument document,
        DocumentFormattingTextRange range,
        ICollection<RunFormatting> selected)
    {
        var paragraph = range.Paragraph;
        var textLength = paragraph.PlainText.Length;
        var start = Math.Clamp(Math.Min(range.StartOffset, range.EndOffset), 0, textLength);
        var end = Math.Clamp(Math.Max(range.StartOffset, range.EndOffset), 0, textLength);
        var position = 0;

        foreach (var run in paragraph.Runs)
        {
            var runStart = position;
            var runEnd = runStart + run.Text.Length;
            position = runEnd;
            if (Math.Min(end, runEnd) <= Math.Max(start, runStart))
                continue;

            selected.Add(DocumentRunFormattingResolver.Resolve(document, paragraph, run.Formatting));
        }

        if (range.IncludesParagraphMark)
        {
            selected.Add(DocumentFormattingProbePlanner.Resolve(
                document,
                paragraph,
                textLength).Run);
        }
    }
}
