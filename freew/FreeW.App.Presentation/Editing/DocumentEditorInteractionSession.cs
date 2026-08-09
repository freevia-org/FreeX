using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

[Flags]
public enum DocumentEditorInputModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

public enum DocumentEditorInputKey
{
    None,
    Backspace,
    Delete,
    Enter,
    Tab,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    B,
    I,
    U,
    Y,
    Z,
}

public enum DocumentEditorInputIntent
{
    None,
    Undo,
    Redo,
    ToggleBold,
    ToggleItalic,
    ToggleUnderline,
    DeleteBackward,
    DeleteForward,
    InsertParagraphBreak,
    MovePrevious,
    MoveNext,
    MoveLineStart,
    MoveLineEnd,
    MoveLineUp,
    MoveLineDown,
    NavigateTab,
}

public readonly record struct DocumentEditorInputPlan(
    DocumentEditorInputIntent Intent,
    bool ExtendSelection = false,
    bool IsEditingMutation = false);

public enum DocumentPasteTextKind
{
    TextOnly,
    MergeFormatting,
}

public sealed record DocumentPasteTextPlan(
    string Text,
    IReadOnlyList<string> Lines,
    string UndoLabel)
{
    public bool HasText => Text.Length > 0;
}

public readonly record struct DocumentSectionPosition(int Current, int Total);

public readonly record struct DocumentTableCaretPosition(
    int TableBlockIndex,
    int RowIndex,
    int GridColumnIndex,
    int ParagraphIndex,
    int Offset);

public readonly record struct DocumentCaretNavigationResult(
    DocumentTextPosition BodyCaret,
    DocumentTableCaretPosition? TableCaret = null,
    bool AppendTableRow = false,
    bool Handled = true);

/// <summary>
/// Owns layout-independent editor projections and input decisions. Native controls translate keys,
/// carets, selections, clipboard data, and coordinates at the renderer boundary.
/// </summary>
public sealed class DocumentEditorInteractionSession
{
    private readonly DocumentEditingSession _editing;
    private FormatPainterClipboard? _formatPainter;
    private bool _formatPainterLocked;

    internal DocumentEditorInteractionSession(DocumentEditingSession editing)
    {
        _editing = editing;
    }

    public DocumentSectionPosition SectionPosition(int caretBlockIndex)
    {
        var document = _editing.Document;
        var total = Math.Max(1, document.Sections.Count);
        if (total == 1)
            return new DocumentSectionPosition(1, 1);
        if (caretBlockIndex < 0)
            return new DocumentSectionPosition(total, total);

        var current = 1;
        var limit = Math.Min(caretBlockIndex, document.Blocks.Count);
        for (var index = 0; index < limit; index++)
        {
            if (document.Blocks[index] is Paragraph { SectionBreak: not null })
                current++;
        }

        return new DocumentSectionPosition(Math.Clamp(current, 1, total), total);
    }

    public bool IsFormatPainterArmed => _formatPainter is not null;

    public bool ToggleFormatPainter(
        RunFormatting? run,
        ParagraphFormatting? paragraph,
        bool locked = false)
    {
        if (_formatPainter is not null)
        {
            if (locked)
            {
                _formatPainterLocked = true;
                return true;
            }

            CancelFormatPainter();
            return false;
        }

        _formatPainter = FormatPainterClipboard.Capture(run, paragraph);
        _formatPainterLocked = locked;
        return true;
    }

    public void CancelFormatPainter()
    {
        _formatPainter = null;
        _formatPainterLocked = false;
    }

    public bool TryApplyFormatPainter(DocumentTextRange selection)
    {
        if (_formatPainter is not { } painter)
            return false;

        var ranges = ParagraphRanges(selection);
        if (ranges.Count == 0)
            return false;

        var commands = new List<IDocumentCommand>();
        foreach (var range in ranges)
        {
            commands.Add(new FormatPainterRangeCommand(
                range.Start.BlockIndex,
                range.Start.Offset,
                range.End.Offset,
                painter.ApplyTo,
                _editing.RevisionDateXmlForEdit));
        }

        foreach (var blockIndex in ranges
                     .Select(range => range.Start.BlockIndex)
                     .Distinct())
        {
            var paragraph = (Paragraph)_editing.Document.Blocks[blockIndex];
            commands.Add(new SetParagraphFormattingCommand(
                blockIndex,
                painter.ApplyTo(paragraph.Formatting)));
        }

        _editing.ExecuteCommands(commands, "Format Painter");
        if (!_formatPainterLocked)
            _formatPainter = null;
        return true;
    }

    public string ProjectSelectionText(DocumentTextRange selection)
    {
        var range = selection.Normalize();
        if (range.IsCollapsed
            || range.Start.BlockIndex < 0
            || range.End.BlockIndex >= _editing.Document.Blocks.Count)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (var blockIndex = range.Start.BlockIndex; blockIndex <= range.End.BlockIndex; blockIndex++)
        {
            if (_editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            if (text.Length > 0)
                text.Append('\n');
            var start = blockIndex == range.Start.BlockIndex ? range.Start.Offset : 0;
            var end = blockIndex == range.End.BlockIndex ? range.End.Offset : paragraph.PlainText.Length;
            start = Math.Clamp(start, 0, paragraph.PlainText.Length);
            end = Math.Clamp(end, start, paragraph.PlainText.Length);
            text.Append(paragraph.PlainText.AsSpan(start, end - start));
        }

        return text.ToString();
    }

    public DocumentTextRange? SelectAllBodyText()
    {
        var first = -1;
        for (var index = 0; index < _editing.Document.Blocks.Count; index++)
        {
            if (_editing.Document.Blocks[index] is Paragraph paragraph
                && IsBodyTextNavigable(paragraph))
            {
                first = index;
                break;
            }
        }
        if (first < 0)
            return null;

        var last = first;
        for (var index = _editing.Document.Blocks.Count - 1; index >= first; index--)
        {
            if (_editing.Document.Blocks[index] is Paragraph paragraph
                && IsBodyTextNavigable(paragraph))
            {
                last = index;
                break;
            }
        }

        return new DocumentTextRange(
            new DocumentTextPosition(first, 0),
            new DocumentTextPosition(last, BodyTextLength(last)));
    }

    public bool HasBodyTextRange(int blockIndex, int startOffset, int endOffset)
    {
        if (blockIndex < 0
            || blockIndex >= _editing.Document.Blocks.Count
            || _editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return false;
        }

        var textLength = paragraph.PlainText.Length;
        var start = Math.Clamp(startOffset, 0, textLength);
        var end = Math.Clamp(endOffset, 0, textLength);
        return end > start;
    }

    public int BodyRunStartOffset(int blockIndex, int runIndex)
    {
        if (blockIndex < 0
            || blockIndex >= _editing.Document.Blocks.Count
            || _editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return 0;
        }

        var limit = Math.Clamp(runIndex, 0, paragraph.Runs.Count);
        var offset = 0;
        for (var index = 0; index < limit; index++)
            offset += paragraph.Runs[index].Text.Length;
        return offset;
    }

    public DocumentCaretNavigationResult NavigateBodyHorizontal(
        DocumentTextPosition caret,
        int delta)
    {
        var length = BodyTextLength(caret.BlockIndex);
        var offset = caret.Offset + delta;
        if (offset < 0)
        {
            var previous = PreviousBodyTextBlock(caret.BlockIndex);
            return new DocumentCaretNavigationResult(previous < 0
                ? new DocumentTextPosition(caret.BlockIndex, 0)
                : new DocumentTextPosition(previous, BodyTextLength(previous)));
        }

        if (offset > length)
        {
            var next = NextBodyTextBlock(caret.BlockIndex);
            return new DocumentCaretNavigationResult(next < 0
                ? new DocumentTextPosition(caret.BlockIndex, length)
                : new DocumentTextPosition(next, 0));
        }

        return new DocumentCaretNavigationResult(
            new DocumentTextPosition(caret.BlockIndex, offset));
    }

    public DocumentCaretNavigationResult NavigateTableHorizontal(
        DocumentTableCaretPosition caret,
        int delta)
    {
        if (!TryGetTable(caret.TableBlockIndex, out var table)
            || CellAtGridColumn(table, caret.RowIndex, caret.GridColumnIndex) is not { } cell)
        {
            return default;
        }

        var paragraph = caret.ParagraphIndex >= 0 && caret.ParagraphIndex < cell.Paragraphs.Count
            ? cell.Paragraphs[caret.ParagraphIndex]
            : null;
        var length = paragraph?.PlainText.Length ?? 0;
        var offset = caret.Offset + delta;
        if (offset >= 0 && offset <= length)
        {
            return TableResult(caret with { Offset = offset });
        }

        if (offset < 0 && caret.ParagraphIndex > 0)
        {
            var previousParagraph = cell.Paragraphs[caret.ParagraphIndex - 1];
            return TableResult(caret with
            {
                ParagraphIndex = caret.ParagraphIndex - 1,
                Offset = previousParagraph.PlainText.Length,
            });
        }

        if (offset > length && caret.ParagraphIndex < cell.Paragraphs.Count - 1)
        {
            return TableResult(caret with
            {
                ParagraphIndex = caret.ParagraphIndex + 1,
                Offset = 0,
            });
        }

        var order = TableCellOrder(table, skipVerticalMergeContinuations: false);
        var currentIndex = order.FindIndex(item =>
            item.RowIndex == caret.RowIndex && item.GridColumnIndex == caret.GridColumnIndex);
        var targetIndex = currentIndex + Math.Sign(delta);
        if (currentIndex < 0)
            return default;

        if (targetIndex < 0 || targetIndex >= order.Count)
        {
            var bodyBlock = delta < 0
                ? PreviousBodyTextBlock(caret.TableBlockIndex)
                : NextBodyTextBlock(caret.TableBlockIndex);
            var bodyCaret = bodyBlock < 0
                ? new DocumentTextPosition(caret.TableBlockIndex, 0)
                : new DocumentTextPosition(
                    bodyBlock,
                    delta < 0 ? BodyTextLength(bodyBlock) : 0);
            return new DocumentCaretNavigationResult(bodyCaret);
        }

        var target = order[targetIndex];
        var targetCell = CellAtGridColumn(table, target.RowIndex, target.GridColumnIndex)!;
        var paragraphIndex = delta > 0 ? 0 : Math.Max(0, targetCell.Paragraphs.Count - 1);
        var targetOffset = delta > 0 || targetCell.Paragraphs.Count == 0
            ? 0
            : targetCell.Paragraphs[paragraphIndex].PlainText.Length;
        return TableResult(new DocumentTableCaretPosition(
            caret.TableBlockIndex,
            target.RowIndex,
            target.GridColumnIndex,
            paragraphIndex,
            targetOffset));
    }

    public DocumentCaretNavigationResult NavigateTableTab(
        DocumentTableCaretPosition caret,
        bool forward)
    {
        if (!TryGetTable(caret.TableBlockIndex, out var table))
            return default;

        var order = TableCellOrder(table, skipVerticalMergeContinuations: true);
        var currentIndex = order.FindIndex(item =>
            item.RowIndex == caret.RowIndex && item.GridColumnIndex == caret.GridColumnIndex);
        if (currentIndex < 0)
            return default;

        var targetIndex = currentIndex + (forward ? 1 : -1);
        if (forward && targetIndex >= order.Count)
        {
            return new DocumentCaretNavigationResult(
                new DocumentTextPosition(caret.TableBlockIndex, 0),
                caret,
                AppendTableRow: true);
        }

        if (targetIndex < 0)
            return TableResult(caret);

        var target = order[targetIndex];
        return TableResult(new DocumentTableCaretPosition(
            caret.TableBlockIndex,
            target.RowIndex,
            target.GridColumnIndex,
            ParagraphIndex: 0,
            Offset: 0));
    }

    public int BodyTextLength(int blockIndex)
    {
        if (blockIndex < 0
            || blockIndex >= _editing.Document.Blocks.Count
            || _editing.Document.Blocks[blockIndex] is not Paragraph paragraph
            || !IsBodyTextNavigable(paragraph))
        {
            return 0;
        }

        return paragraph.Runs
            .Where(run => !IsFloatingDrawingRun(run))
            .Sum(run => run.Text.Length);
    }

    public int FirstBodyTextBlock()
    {
        for (var index = 0; index < _editing.Document.Blocks.Count; index++)
        {
            if (_editing.Document.Blocks[index] is Paragraph paragraph
                && IsBodyTextNavigable(paragraph))
            {
                return index;
            }
        }

        return 0;
    }

    public int NextBodyTextBlock(int from)
    {
        for (var index = from + 1; index < _editing.Document.Blocks.Count; index++)
        {
            if (_editing.Document.Blocks[index] is Paragraph paragraph
                && IsBodyTextNavigable(paragraph))
            {
                return index;
            }
        }

        return -1;
    }

    public int PreviousBodyTextBlock(int from)
    {
        for (var index = from - 1; index >= 0; index--)
        {
            if (_editing.Document.Blocks[index] is Paragraph paragraph
                && IsBodyTextNavigable(paragraph))
            {
                return index;
            }
        }

        return -1;
    }

    public DocumentPasteTextPlan PlanPasteText(string? clipboardText, DocumentPasteTextKind kind)
    {
        var normalized = PasteText.Normalize(clipboardText);
        return new DocumentPasteTextPlan(
            normalized,
            normalized.Length == 0 ? [] : normalized.Split('\n'),
            kind == DocumentPasteTextKind.TextOnly ? "Paste Text Only" : "Merge Formatting");
    }

    public static DocumentEditorInputPlan PlanBodyKey(
        DocumentEditorInputKey key,
        DocumentEditorInputModifiers modifiers)
    {
        var control = (modifiers & DocumentEditorInputModifiers.Control) != 0;
        var shift = (modifiers & DocumentEditorInputModifiers.Shift) != 0;
        return (key, control) switch
        {
            (DocumentEditorInputKey.Z, true) => Mutation(DocumentEditorInputIntent.Undo),
            (DocumentEditorInputKey.Y, true) => Mutation(DocumentEditorInputIntent.Redo),
            (DocumentEditorInputKey.B, true) => Mutation(DocumentEditorInputIntent.ToggleBold),
            (DocumentEditorInputKey.I, true) => Mutation(DocumentEditorInputIntent.ToggleItalic),
            (DocumentEditorInputKey.U, true) => Mutation(DocumentEditorInputIntent.ToggleUnderline),
            (DocumentEditorInputKey.Backspace, _) => Mutation(DocumentEditorInputIntent.DeleteBackward),
            (DocumentEditorInputKey.Delete, _) => Mutation(DocumentEditorInputIntent.DeleteForward),
            (DocumentEditorInputKey.Enter, _) => Mutation(DocumentEditorInputIntent.InsertParagraphBreak),
            (DocumentEditorInputKey.Tab, _) => Mutation(DocumentEditorInputIntent.NavigateTab, shift),
            (DocumentEditorInputKey.Left, _) => Move(DocumentEditorInputIntent.MovePrevious, shift),
            (DocumentEditorInputKey.Right, _) => Move(DocumentEditorInputIntent.MoveNext, shift),
            (DocumentEditorInputKey.Home, _) => Move(DocumentEditorInputIntent.MoveLineStart, shift),
            (DocumentEditorInputKey.End, _) => Move(DocumentEditorInputIntent.MoveLineEnd, shift),
            (DocumentEditorInputKey.Up, _) => Move(DocumentEditorInputIntent.MoveLineUp, shift),
            (DocumentEditorInputKey.Down, _) => Move(DocumentEditorInputIntent.MoveLineDown, shift),
            _ => default,
        };
    }

    private static DocumentEditorInputPlan Mutation(
        DocumentEditorInputIntent intent,
        bool extendSelection = false) =>
        new(intent, extendSelection, IsEditingMutation: true);

    private static DocumentEditorInputPlan Move(
        DocumentEditorInputIntent intent,
        bool extendSelection) =>
        new(intent, extendSelection);

    private IReadOnlyList<DocumentTextRange> ParagraphRanges(DocumentTextRange selection)
    {
        var normalized = selection.Normalize();
        if (normalized.IsCollapsed
            || normalized.Start.BlockIndex < 0
            || normalized.End.BlockIndex >= _editing.Document.Blocks.Count)
        {
            return [];
        }

        var ranges = new List<DocumentTextRange>();
        for (var blockIndex = normalized.Start.BlockIndex;
             blockIndex <= normalized.End.BlockIndex;
             blockIndex++)
        {
            if (_editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            var start = blockIndex == normalized.Start.BlockIndex ? normalized.Start.Offset : 0;
            var end = blockIndex == normalized.End.BlockIndex
                ? normalized.End.Offset
                : paragraph.PlainText.Length;
            start = Math.Clamp(start, 0, paragraph.PlainText.Length);
            end = Math.Clamp(end, start, paragraph.PlainText.Length);
            if (end > start)
            {
                ranges.Add(new DocumentTextRange(
                    new DocumentTextPosition(blockIndex, start),
                    new DocumentTextPosition(blockIndex, end)));
            }
        }

        return ranges;
    }

    private bool TryGetTable(int blockIndex, out Table table)
    {
        table = null!;
        if (blockIndex < 0
            || blockIndex >= _editing.Document.Blocks.Count
            || _editing.Document.Blocks[blockIndex] is not Table target)
        {
            return false;
        }

        table = target;
        return true;
    }

    private static DocumentCaretNavigationResult TableResult(DocumentTableCaretPosition caret) =>
        new(new DocumentTextPosition(caret.TableBlockIndex, 0), caret);

    private static List<(int RowIndex, int GridColumnIndex)> TableCellOrder(
        Table table,
        bool skipVerticalMergeContinuations)
    {
        var order = new List<(int RowIndex, int GridColumnIndex)>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var gridColumnIndex = 0;
            foreach (var cell in table.Rows[rowIndex].Cells)
            {
                if (!skipVerticalMergeContinuations
                    || cell.VerticalMerge != VerticalMergeState.Continue)
                {
                    order.Add((rowIndex, gridColumnIndex));
                }
                gridColumnIndex += Math.Max(1, cell.GridSpan);
            }
        }

        return order;
    }

    private static TableCell? CellAtGridColumn(Table table, int rowIndex, int gridColumnIndex)
    {
        if (rowIndex < 0 || rowIndex >= table.Rows.Count || gridColumnIndex < 0)
            return null;

        var currentGridColumn = 0;
        foreach (var cell in table.Rows[rowIndex].Cells)
        {
            if (gridColumnIndex >= currentGridColumn
                && gridColumnIndex < currentGridColumn + Math.Max(1, cell.GridSpan))
            {
                return cell;
            }
            currentGridColumn += Math.Max(1, cell.GridSpan);
        }

        return null;
    }

    private bool IsBodyTextNavigable(Paragraph paragraph) =>
        !_editing.Review.RestrictEditingPolicy.IsBodyEditingLocked
        && paragraph.Runs.All(run =>
            run.Image is null
            && run.Equation is null
            && run.FieldKind == RunFieldKind.None
            && run.ComplexField is null
            && run.FootnoteId is null
            && run.EndnoteId is null
            && run.Control is null
            && !IsFloatingDrawingRun(run));

    private static bool IsFloatingDrawingRun(Run run) =>
        run.Image is { IsFloating: true }
        || run.Shape is { IsFloating: true }
        || run.Chart is { IsFloating: true }
        || run.WordArt is { IsFloating: true }
        || run.SmartArt is { IsFloating: true }
        || run.DrawingGroup is { IsFloating: true };

    private sealed class FormatPainterRangeCommand(
        int blockIndex,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform,
        Func<string?> revisionDateXml) : IDocumentCommand
    {
        private List<Run>? _previous;
        private List<Run>? _replacement;

        public string Label => "Format Painter";

        public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyFormatting;

        public void Apply(IDocumentCommandContext context)
        {
            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            if (_replacement is not null)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.AddRange(_replacement);
                return;
            }

            _previous = [.. paragraph.Runs];
            var rebuilt = new List<Run>();
            var position = 0;
            foreach (var source in paragraph.Runs)
            {
                var runStart = position;
                var runEnd = runStart + source.Text.Length;
                position = runEnd;
                var coverStart = Math.Max(runStart, startOffset);
                var coverEnd = Math.Min(runEnd, endOffset);
                if (source.Text.Length == 0 || coverStart >= coverEnd)
                {
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                    continue;
                }

                var localStart = coverStart - runStart;
                var localEnd = coverEnd - runStart;
                if (localStart > 0)
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[..localStart]));

                var covered = RevisionEditPlanner.CloneRunWithText(
                    source,
                    source.Text[localStart..localEnd]);
                var formatting = transform(source.Formatting);
                covered.Formatting = formatting;
                if (context.Document is { TrackRevisions: true, DoNotTrackFormatting: false }
                    && formatting != source.Formatting
                    && covered.FormatRevision is null)
                {
                    var author = string.IsNullOrWhiteSpace(context.RevisionAuthor)
                        ? "FreeW User"
                        : context.RevisionAuthor.Trim();
                    covered.FormatRevision = new FormatRevision(
                        source.Formatting,
                        author,
                        revisionDateXml());
                }
                rebuilt.Add(covered);

                if (localEnd < source.Text.Length)
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[localEnd..]));
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(rebuilt);
            _replacement = [.. paragraph.Runs];
        }

        public void Revert(IDocumentCommandContext context)
        {
            if (_previous is null)
                return;
            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(_previous);
        }
    }
}
