using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private sealed record ContentControlDisplaySpan(
        int FirstRunIndex, int RunCount, int Start, int Length, int ModelLength,
        IReadOnlyList<int> ModelBoundaries);

    private KeyboardParagraph ContentControlParagraphAddress(Paragraph paragraph) =>
        _cellCaret is { } cell
            ? new(paragraph, cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx)
            : new(paragraph, _caret.Block);

    private IEnumerable<ContentControlDisplaySpan> ContentControlDisplaySpans(KeyboardParagraph address)
    {
        var runs = address.Model.Runs;
        var displayOffset = 0;
        for (var index = 0; index < runs.Count;)
        {
            var first = index;
            var start = displayOffset;
            var control = runs[index].Control;
            var modelLength = 0;
            var boundaries = new List<int> { 0 };
            var editableText = true;
            do
            {
                var run = runs[index];
                var single = new Paragraph();
                single.Runs.Add(run);
                displayOffset += KeyboardParagraphText(address with { Model = single }).Length;
                editableText &= IsUnicodeLiteralRun(run) && run.Ruby is null && run.PreservedDrawing is null
                    && run.SubDocument is null && run.Citation is null && run.TableFormula is null
                    && run.CrossReference is null && run.MoveRevisionId is null
                    && !run.IsPageBreak && !run.IsColumnBreak;
                AppendControlModelBoundaries(boundaries, run.Text, modelLength, address.RowIndex >= 0);
                modelLength += run.Text.Length;
                index++;
            } while (control is not null && index < runs.Count && ReferenceEquals(runs[index].Control, control));

            // Structural children cannot be rebuilt by the literal control text editor.
            // An empty literal control still has one boundary and remains an edit target.
            if (control is not null && editableText && boundaries.Count == displayOffset - start + 1)
                yield return new(first, index - first, start, displayOffset - start, modelLength, boundaries);
        }
    }

    private static void AppendControlModelBoundaries(List<int> boundaries, string text, int modelStart, bool cell)
    {
        for (var offset = 0; offset < text.Length; offset++)
        {
            if (cell && text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n')
                offset++;
            boundaries.Add(modelStart + offset + 1);
        }
    }

    private static int ControlDisplayOffsetForModel(IReadOnlyList<Run> runs, int modelOffset, bool cell)
    {
        var boundaries = new List<int> { 0 };
        var modelStart = 0;
        foreach (var run in runs)
        {
            AppendControlModelBoundaries(boundaries, run.Text, modelStart, cell);
            modelStart += run.Text.Length;
        }
        modelOffset = Math.Clamp(modelOffset, 0, modelStart);
        var index = boundaries.BinarySearch(modelOffset);
        return index >= 0 ? index : Math.Min(~index, boundaries.Count - 1);
    }

}
