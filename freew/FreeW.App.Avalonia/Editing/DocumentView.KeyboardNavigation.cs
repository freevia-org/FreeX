using Avalonia.Input;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private bool TryHandleTextNavigation(KeyEventArgs args)
    {
        if (_shapeCaret is null && (_selectedFloating is not null || _selectedInlineObject is not null))
            return false;

        var modifiers = args.KeyModifiers;
        var plan = DocumentTextNavigationPlanner.Plan(
            ToEditorInputKey(args.Key),
            (modifiers & KeyModifiers.Control) != 0,
            (modifiers & KeyModifiers.Meta) != 0,
            (modifiers & KeyModifiers.Alt) != 0,
            (modifiers & KeyModifiers.Shift) != 0);

        if (_shapeCaret is not null)
        {
            if (plan.Intent != DocumentTextNavigationIntent.None)
                return HandleShapeTextNavigation(plan);
            // Shape text has its own story. A plain vertical arrow must not fall through
            // and move the invisible body caret (or nudge the selected text box).
            if (args.Key is Key.Up or Key.Down)
                return MoveShapeTextCaretVertical(args.Key == Key.Up ? -1 : 1, plan.ExtendSelection);
            return false;
        }

        if (plan.Intent == DocumentTextNavigationIntent.None)
            return false;
        if (_hfCaret is not null)
            return TryHandleHeaderFooterNavigation(plan.Intent, plan.ExtendSelection);

        if (plan.Intent is DocumentTextNavigationIntent.PreviousCharacter or DocumentTextNavigationIntent.NextCharacter)
        {
            // A plain arrow collapses a selection to its directional edge without taking
            // an extra step. All other motion uses text elements, preserving emoji and
            // combining sequences; Shift keeps the original selection anchor.
            if (!plan.ExtendSelection && _cellCaret is not null && _cellAnchor is not null && HasCellTextSelection())
                return CollapseCellKeyboardSelection(plan.Intent == DocumentTextNavigationIntent.PreviousCharacter);
            if (!plan.ExtendSelection && _cellCaret is null && NormalizedSelection() is { } selection)
            {
                _caret = plan.Intent == DocumentTextNavigationIntent.PreviousCharacter ? selection.Start : selection.End;
                _selectionAnchor = _caret;
                InvalidateVisual();
                CaretMoved?.Invoke();
                return true;
            }
            return HandleBodyTextNavigation(plan);
        }

        return HandleBodyTextNavigation(plan);
    }

    private sealed record KeyboardParagraph(
        Paragraph Model,
        int BlockIndex,
        int RowIndex = -1,
        int GridColumnIndex = -1,
        int ParagraphIndex = -1);

    private sealed class KeyboardParagraphTexts(DocumentView owner, IReadOnlyList<KeyboardParagraph> paragraphs) : IReadOnlyList<string>
    {
        public int Count => paragraphs.Count;
        public string this[int index] => owner.KeyboardParagraphText(paragraphs[index]);
        public IEnumerator<string> GetEnumerator() => paragraphs.Select(owner.KeyboardParagraphText).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private List<KeyboardParagraph> BuildKeyboardParagraphs()
    {
        var paragraphs = new List<KeyboardParagraph>();
        for (var blockIndex = 0; blockIndex < _doc.Blocks.Count; blockIndex++)
        {
            if (_doc.Blocks[blockIndex] is Paragraph paragraph)
            {
                paragraphs.Add(new(paragraph, blockIndex));
            }
            else if (_doc.Blocks[blockIndex] is Table table)
            {
                for (var row = 0; row < table.Rows.Count; row++)
                {
                    foreach (var cell in TableGridProjection.ProjectRow(table.Rows[row]))
                    {
                        if (cell.Cell.VerticalMerge == VerticalMergeState.Continue)
                            continue;
                        for (var index = 0; index < cell.Cell.Paragraphs.Count; index++)
                            paragraphs.Add(new(cell.Cell.Paragraphs[index],
                                blockIndex, row, cell.StartColumn, index));
                    }
                }
            }
        }
        return paragraphs;
    }

    private string KeyboardParagraphText(KeyboardParagraph paragraph) =>
        string.Concat(paragraph.Model.Runs.Select((run, index) =>
            ResolveRunDisplayProjection(paragraph.Model, run, index, paragraph.BlockIndex,
                isTableCell: paragraph.RowIndex >= 0).DisplayText));

    private KeyboardParagraph? CurrentKeyboardParagraph()
    {
        if (_cellCaret is { } cell)
            return GetCellParagraph(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx) is { } paragraph
                ? new(paragraph, cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx) : null;
        return _caret.Block >= 0 && _caret.Block < _doc.Blocks.Count
            && _doc.Blocks[_caret.Block] is Paragraph body
                ? new(body, _caret.Block) : null;
    }

    private bool TryHandleLocalTextNavigation(DocumentTextNavigationPlan plan)
    {
        if (CurrentKeyboardParagraph() is not { } paragraph)
            return false;
        var text = KeyboardParagraphText(paragraph);
        var offset = _cellCaret?.Offset ?? _caret.Offset;
        var staysInParagraph = plan.Intent switch
        {
            DocumentTextNavigationIntent.PreviousCharacter or DocumentTextNavigationIntent.PreviousWord
                or DocumentTextNavigationIntent.PreviousParagraph => offset > 0,
            DocumentTextNavigationIntent.NextCharacter or DocumentTextNavigationIntent.NextWord
                or DocumentTextNavigationIntent.NextWordStart or DocumentTextNavigationIntent.NextParagraph => offset < text.Length,
            DocumentTextNavigationIntent.LineStart or DocumentTextNavigationIntent.LineEnd => true,
            _ => false,
        };
        if (!staysInParagraph)
            return false;
        var target = DocumentTextNavigationPlanner.Move([text], new(0, offset), plan.Intent);
        BodyCaretAffinity? affinity = null;
        if (plan.Intent is DocumentTextNavigationIntent.LineStart or DocumentTextNavigationIntent.LineEnd
            && TryGetKeyboardLineEdge(paragraph, offset, plan.Intent == DocumentTextNavigationIntent.LineStart, out var edge, out affinity))
            target = target with { Offset = edge };
        ApplyKeyboardParagraphNavigation(paragraph, target.Offset, plan.ExtendSelection, affinity);
        return true;
    }

    private bool HandleBodyTextNavigation(DocumentTextNavigationPlan plan)
    {
        _pendingRunFmt = null;
        // Common character/word/line movement examines only the current paragraph.
        // Build a document-wide address list only for an actual paragraph/table boundary
        // crossing or document-edge gesture.
        if (TryHandleLocalTextNavigation(plan))
            return true;
        var paragraphs = BuildKeyboardParagraphs();
        var currentIndex = paragraphs.FindIndex(paragraph => _cellCaret is { } cell
            ? paragraph.BlockIndex == cell.TableBlock && paragraph.RowIndex == cell.Row
                && paragraph.GridColumnIndex == cell.Col && paragraph.ParagraphIndex == cell.ParaIdx
            : paragraph.BlockIndex == _caret.Block && paragraph.RowIndex < 0);
        if (currentIndex < 0)
            return true;
        var currentOffset = _cellCaret?.Offset ?? _caret.Offset;
        var current = new DocumentTextNavigationPosition(currentIndex, currentOffset);
        var target = DocumentTextNavigationPlanner.Move(new KeyboardParagraphTexts(this, paragraphs), current, plan.Intent);
        BodyCaretAffinity? affinity = null;
        if (plan.Intent is DocumentTextNavigationIntent.LineStart or DocumentTextNavigationIntent.LineEnd
            && TryGetKeyboardLineEdge(paragraphs[currentIndex], currentOffset,
                plan.Intent == DocumentTextNavigationIntent.LineStart, out var lineOffset, out affinity))
            target = current with { Offset = lineOffset };

        ApplyKeyboardParagraphNavigation(paragraphs[target.ParagraphIndex], target.Offset, plan.ExtendSelection,
            target.ParagraphIndex == currentIndex ? affinity : null);
        return true;
    }

    private void ApplyKeyboardParagraphNavigation(KeyboardParagraph paragraph, int offset, bool extend, BodyCaretAffinity? affinity = null)
    {
        ApplyCaretNavigation(paragraph.RowIndex < 0
            ? new(new DocumentTextPosition(paragraph.BlockIndex, offset))
            : new(new DocumentTextPosition(paragraph.BlockIndex, 0),
                new DocumentTableCaretPosition(paragraph.BlockIndex, paragraph.RowIndex,
                    paragraph.GridColumnIndex, paragraph.ParagraphIndex, offset)), extend, affinity);
    }

    private bool TryGetKeyboardLineEdge(KeyboardParagraph paragraph, int offset, bool toStart, out int edge, out BodyCaretAffinity? affinity)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        affinity = null;
        if (paragraph.RowIndex < 0 && TryGetShapedBodyLineEdge(paragraph.BlockIndex, offset, toStart, out edge, out affinity))
            return true;
        var glyphs = _placed.Where(glyph => glyph.Block == paragraph.BlockIndex
            && (paragraph.RowIndex < 0 ? !glyph.IsCell
                : glyph.CellRow == paragraph.RowIndex && glyph.CellCol == paragraph.GridColumnIndex
                    && glyph.CellParaIdx == paragraph.ParagraphIndex)).ToArray();
        var index = Array.FindLastIndex(glyphs,
            glyph => (paragraph.RowIndex < 0 ? glyph.Offset : glyph.CellParaOffset) == offset);
        edge = 0;
        if (index < 0)
            return false;
        var start = index;
        var end = index;
        while (start > 0 && Math.Abs(glyphs[start - 1].Y - glyphs[index].Y) < 0.5)
            start--;
        while (end + 1 < glyphs.Length && Math.Abs(glyphs[end + 1].Y - glyphs[index].Y) < 0.5)
            end++;
        var chosen = glyphs[toStart ? start : end];
        edge = paragraph.RowIndex < 0 ? chosen.Offset : chosen.CellParaOffset;
        if (!toStart && !chosen.Sentinel && chosen.Ch is not ('\r' or '\n'))
            edge += chosen.ShapedText?.Length ?? 1;
        edge = Math.Clamp(edge, 0, KeyboardParagraphText(paragraph).Length);
        return true;
    }

    private bool CollapseCellKeyboardSelection(bool toStart)
    {
        if (_cellCaret is not { } caret || _cellAnchor is not { } anchor)
            return false;
        var ordered = CompareCellEndpoints(anchor, caret) <= 0 ? (anchor, caret) : (caret, anchor);
        var target = toStart ? ordered.Item1 : ordered.Item2;
        ApplyCaretNavigation(new(new DocumentTextPosition(target.TableBlock, 0), ToPortableCaret(target)), extend: false);
        return true;
    }

    private bool HandleShapeTextNavigation(DocumentTextNavigationPlan plan)
    {
        if (_shapeCaret is not { } caret
            || !TryGetShapeTextTarget(caret.BlockIndex, caret.RunIndex, _activeShapeTextChildPath, out _, out var shape))
            return true;
        var paragraphs = shape.TextParagraphs.Select(paragraph => paragraph.PlainText).ToArray();
        var current = new DocumentTextNavigationPosition(caret.TextParagraphIndex, ShapeTextOffset(shape, caret));
        DocumentTextNavigationPosition target;
        if (!plan.ExtendSelection && CurrentShapeTextSelection is { } selection
            && plan.Intent is DocumentTextNavigationIntent.PreviousCharacter or DocumentTextNavigationIntent.NextCharacter)
        {
            var endpoint = plan.Intent == DocumentTextNavigationIntent.PreviousCharacter ? selection.Start : selection.End;
            target = new(endpoint.TextParagraphIndex, ShapeTextOffset(shape, endpoint));
        }
        else
        {
            target = DocumentTextNavigationPlanner.Move(paragraphs, current, plan.Intent);
            if (plan.Intent is DocumentTextNavigationIntent.LineStart or DocumentTextNavigationIntent.LineEnd
                && TryGetShapeKeyboardLineEdge(shape, plan.Intent == DocumentTextNavigationIntent.LineStart, out var lineTarget))
                target = lineTarget;
        }
        ApplyShapeKeyboardCaret(shape, target, plan.ExtendSelection);
        return true;
    }

    private void ApplyShapeKeyboardCaret(Shape shape, DocumentTextNavigationPosition target, bool extend)
    {
        if (_shapeCaret is not { } caret || shape.TextParagraphs.Count == 0)
            return;
        _pendingRunFmt = null;
        if (extend)
            _shapeSelectionAnchor ??= caret;
        _shapeCaret = ShapeTextPositionAtOffset(shape, caret.BlockIndex, caret.RunIndex, target.ParagraphIndex, target.Offset);
        if (!extend)
            _shapeSelectionAnchor = _shapeCaret;
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    private ShapeTextCaretStop[] ActiveShapeKeyboardStops()
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        return _shapeCaret is { } caret
            ? _shapeTextCaretStops.Where(stop => stop.BlockIndex == caret.BlockIndex && stop.RunIndex == caret.RunIndex
                && ShapeChildPathsEqual(stop.ChildPath, _activeShapeTextChildPath)).ToArray()
            : [];
    }

    private bool TryGetShapeKeyboardLineEdge(Shape shape, bool toStart, out DocumentTextNavigationPosition position)
    {
        var stops = ActiveShapeKeyboardStops();
        position = default;
        if (!TryGetActiveShapeCaretStop(out var current))
            return false;
        var row = stops.Where(stop => stop.TextParagraphIndex == current.TextParagraphIndex
            && Math.Abs(stop.Y - current.Y) < 0.5).ToArray();
        var target = toStart ? row.MinBy(stop => stop.X) : row.MaxBy(stop => stop.X);
        if (target is null)
            return false;
        position = ShapeKeyboardPosition(shape, target);
        return true;
    }

    private static DocumentTextNavigationPosition ShapeKeyboardPosition(Shape shape, ShapeTextCaretStop stop) =>
        new(stop.TextParagraphIndex,
            shape.TextParagraphs[stop.TextParagraphIndex].Runs.Take(stop.TextRunIndex).Sum(run => run.Text.Length) + stop.Offset);

    private bool MoveShapeTextCaretVertical(int direction, bool extend)
    {
        var stops = ActiveShapeKeyboardStops();
        if (_shapeCaret is not { } caret
            || !TryGetShapeTextTarget(caret.BlockIndex, caret.RunIndex, _activeShapeTextChildPath, out _, out var shape)
            || !TryGetActiveShapeCaretStop(out var current))
            return true;
        var next = stops.Where(stop => direction < 0 ? stop.Y < current.Y - 0.5 : stop.Y > current.Y + 0.5)
            .OrderBy(stop => Math.Abs(stop.Y - current.Y))
            .ThenBy(stop => Math.Abs(stop.X - current.X)).FirstOrDefault();
        if (next is not null)
            ApplyShapeKeyboardCaret(shape, ShapeKeyboardPosition(shape, next), extend);
        return true;
    }

    private bool SelectAllActiveShapeText()
    {
        if (_shapeCaret is not { } caret)
            return false;
        if (TryGetShapeTextTarget(caret.BlockIndex, caret.RunIndex, _activeShapeTextChildPath, out _, out var shape)
            && shape.TextParagraphs.Count > 0)
        {
            _shapeSelectionAnchor = ShapeTextPositionAtOffset(shape, caret.BlockIndex, caret.RunIndex, 0, 0);
            var last = shape.TextParagraphs.Count - 1;
            _shapeCaret = ShapeTextPositionAtOffset(shape, caret.BlockIndex, caret.RunIndex,
                last, shape.TextParagraphs[last].PlainText.Length);
            InvalidateVisual();
            CaretMoved?.Invoke();
        }
        return true;
    }

    private string ShapeKeyboardSelectedText()
    {
        if (_shapeCaret is not { } caret || CurrentShapeTextSelection is not { } selection
            || !TryGetShapeTextTarget(caret.BlockIndex, caret.RunIndex, _activeShapeTextChildPath, out _, out var shape))
            return string.Empty;
        var text = new List<string>();
        for (var paragraph = selection.Start.TextParagraphIndex; paragraph <= selection.End.TextParagraphIndex; paragraph++)
        {
            var content = shape.TextParagraphs[paragraph].PlainText;
            var start = paragraph == selection.Start.TextParagraphIndex ? ShapeTextOffset(shape, selection.Start) : 0;
            var end = paragraph == selection.End.TextParagraphIndex ? ShapeTextOffset(shape, selection.End) : content.Length;
            text.Add(content[Math.Clamp(start, 0, content.Length)..Math.Clamp(end, 0, content.Length)]);
        }
        return string.Join("\n", text);
    }
}
