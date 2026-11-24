using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private sealed record UnicodeDeletionSpan(Run Run, int DisplayStart, int DisplayLength, int ModelStart, bool IsLiteral);

    private bool TryDeleteUnicodeTextElement(bool backward)
    {
        if (_shapeCaret is { } shapeCaret)
        {
            if (CurrentShapeTextSelection is not null
                || !TryGetShapeTextTarget(shapeCaret.BlockIndex, shapeCaret.RunIndex, _activeShapeTextChildPath,
                    out _, out var shape))
                return false;
            var paragraph = shape.TextParagraphs[shapeCaret.TextParagraphIndex];
            var offset = ShapeTextOffset(shape, shapeCaret);
            if (!TryPlanUnicodeDeletion(paragraph, offset, backward, displayAddress: null, out var range, out var displayRange))
                return false;
            if (!CanDeleteUnicodeRange(paragraph, range))
                return true;
            var paragraphs = shape.TextParagraphs.Select(item => (Paragraph)DocumentMerge.CloneBlock(item)).ToList();
            var deletion = ApplyUnicodeDeletion(paragraphs[shapeCaret.TextParagraphIndex], range);
            _bus.Execute(new ReplaceShapeTextParagraphsCommand(shapeCaret.BlockIndex, shapeCaret.RunIndex,
                paragraphs, _activeShapeTextChildPath));
            var target = new Shape();
            target.TextParagraphs.AddRange(paragraphs);
            _shapeCaret = ShapeTextPositionAtOffset(target, shapeCaret.BlockIndex, shapeCaret.RunIndex,
                shapeCaret.TextParagraphIndex, UnicodeDeletionCaret(displayRange, deletion, backward));
            _shapeSelectionAnchor = _shapeCaret;
            InvalidateLayoutAndVisual();
            CaretMoved?.Invoke();
            return true;
        }

        if (_hfCaret is { } hfCaret)
        {
            if (NormalizedHfSelection() is not null || GetHfParagraph(hfCaret.Target) is not { } paragraph
                || !TryPlanUnicodeDeletion(paragraph, hfCaret.Offset, backward, displayAddress: null,
                    out var range, out var displayRange))
                return false;
            if (!CanDeleteUnicodeRange(paragraph, range))
                return true;
            var deletion = default(RevisionEditPlanner.DeleteResult);
            _bus.Execute(new FreeW.Core.Model.EditHeaderFooterParagraphCommand(hfCaret.Target.SectionIndex,
                hfCaret.Target.UseFinalSectionStore, HeaderFooterDialogPlanner.CommandSlotIndexFor(hfCaret.Target.Slot),
                hfCaret.Target.ParaIdx, item => deletion = ApplyUnicodeDeletion(item, range)));
            _hfCaret = (hfCaret.Target, UnicodeDeletionCaret(displayRange, deletion, backward));
            _hfSelectionAnchor = null;
            InvalidateLayoutAndVisual();
            CaretMoved?.Invoke();
            return true;
        }

        if (SelectedCellRange is not null)
            return false;
        if (_cellCaret is { } cell)
        {
            if (HasCellTextSelection() || GetCellParagraph(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx) is not { } paragraph)
                return false;
            var address = new KeyboardParagraph(paragraph, cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx);
            if (!TryPlanUnicodeDeletion(paragraph, cell.Offset, backward, address, out var range, out var displayRange))
                return false;
            if (!CanDeleteUnicodeRange(paragraph, range))
                return true;
            var deletion = default(RevisionEditPlanner.DeleteResult);
            _bus.Execute(new ReplaceCellParagraphRunsCommand(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx,
                item => deletion = ApplyUnicodeDeletion(item, range)));
            var offset = UnicodeDeletionCaret(displayRange, deletion, backward);
            _cellCaret = cell with { Offset = offset };
            _cellAnchor = _cellCaret;
            _caret = new DocPosition(cell.TableBlock, FindCellGlyphOffset(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx, offset));
            _selectionAnchor = _caret;
            CaretMoved?.Invoke();
            return true;
        }

        if (NormalizedSelection() is not null || CurrentParagraph() is not { } body)
            return false;
        var bodyAddress = new KeyboardParagraph(body, _caret.Block);
        if (!TryPlanUnicodeDeletion(body, _caret.Offset, backward, bodyAddress, out var bodyRange, out var bodyDisplayRange))
            return false;
        if (!CanDeleteUnicodeRange(body, bodyRange))
            return true;
        var result = default(RevisionEditPlanner.DeleteResult);
        _bus.Execute(new ReplaceParagraphRunsCommand(_caret.Block, item => result = ApplyUnicodeDeletion(item, bodyRange)));
        _caret = new DocPosition(_caret.Block, UnicodeDeletionCaret(bodyDisplayRange, result, backward));
        _selectionAnchor = _caret;
        CaretMoved?.Invoke();
        return true;
    }

    private RevisionEditPlanner.DeleteResult ApplyUnicodeDeletion(Paragraph paragraph, TextElementDeletionRange range) =>
        TextElementDeletionPlanner.Apply(paragraph, range, TrackChangesEnabled,
            RevisionAuthor, TrackChangesEnabled ? _editingSession.RevisionDateXmlForEdit() : null);

    private static int UnicodeDeletionCaret(TextElementDeletionRange range, RevisionEditPlanner.DeleteResult result, bool backward) =>
        !backward && result.KeptDeletedText ? range.End : range.Start;

    private bool TryPlanUnicodeDeletion(
        Paragraph paragraph,
        int caretOffset,
        bool backward,
        KeyboardParagraph? displayAddress,
        out TextElementDeletionRange modelRange,
        out TextElementDeletionRange displayRange)
    {
        var text = new System.Text.StringBuilder();
        var spans = new List<UnicodeDeletionSpan>();
        var modelOffset = 0;
        foreach (var run in paragraph.Runs)
        {
            var literal = IsUnicodeLiteralRun(run);
            string content;
            if (displayAddress is null)
            {
                content = run.Text;
            }
            else
            {
                var singleRun = new Paragraph();
                singleRun.Runs.Add(run);
                content = KeyboardParagraphText(displayAddress with { Model = singleRun });
            }
            spans.Add(new(run, text.Length, content.Length, modelOffset, literal));
            text.Append(content);
            modelOffset += run.Text.Length;
        }
        var planned = TextElementDeletionPlanner.Plan(text.ToString(), caretOffset, backward);
        modelRange = default;
        displayRange = default;
        if (planned is not { Length: > 1 } range)
            return false;
        var touched = spans.Where(span => span.DisplayStart < range.End
            && span.DisplayStart + span.DisplayLength > range.Start).ToArray();
        if (touched.Length == 0 || touched.Any(span => !span.IsLiteral || span.DisplayLength != span.Run.Text.Length))
            return false;
        var first = touched[0];
        var last = touched[^1];
        var start = first.ModelStart + range.Start - first.DisplayStart;
        var end = last.ModelStart + range.End - last.DisplayStart;
        modelRange = new(start, end - start);
        displayRange = range;
        return true;
    }

    private static bool IsUnicodeLiteralRun(Run run) =>
        run.FieldKind == RunFieldKind.None && run.ComplexField is null && run.FootnoteId is null && run.EndnoteId is null
        && run.Equation is null && run.Image is null && run.Shape is null && run.DrawingGroup is null
        && run.Chart is null && run.WordArt is null && run.SmartArt is null && run.EmbeddedObject is null;

    private bool CanDeleteUnicodeRange(Paragraph paragraph, TextElementDeletionRange range)
    {
        if (ContentControlInteractionPlanner.IsBlockContentControlLocked(paragraph.BlockContentControl))
            return false;
        foreach (var span in ContentControlSpans(paragraph))
        {
            var run = paragraph.Runs[span.FirstRunIndex];
            var end = span.Start + span.Length;
            // This is a text edit: Apply preserves an empty control run when all its
            // text is removed. A control-deletion lock must not freeze editable text.
            if (span.Start < range.End && end > range.Start && run.Control is not null
                && !ContentControlInteractionPlanner.CanEditContentControlText(run, RestrictEditingPolicy))
                return false;
        }
        return true;
    }
}
