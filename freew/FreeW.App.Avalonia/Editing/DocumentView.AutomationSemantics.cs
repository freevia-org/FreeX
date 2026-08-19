using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    internal AccessibleDocumentSnapshot AutomationSnapshot()
    {
        if (_shapeCaret is { } shapeCaret
            && TryGetShapeTextTarget(
                shapeCaret.BlockIndex,
                shapeCaret.RunIndex,
                _activeShapeTextChildPath,
                out _,
                out var shape))
        {
            AccessibleShapeTextPosition? shapeSelectionAnchor = _shapeSelectionAnchor is { } shapeAnchor
                ? new AccessibleShapeTextPosition(
                    shapeAnchor.TextParagraphIndex,
                    shapeAnchor.TextRunIndex,
                    shapeAnchor.Offset)
                : null;
            var label = string.IsNullOrWhiteSpace(shape.AltText)
                ? "Shape text"
                : $"Shape text: {shape.AltText.Trim()}";
            return AccessibleDocumentSnapshotPlanner.BuildShapeText(
                shape.TextParagraphs,
                new AccessibleShapeTextPosition(
                    shapeCaret.TextParagraphIndex,
                    shapeCaret.TextRunIndex,
                    shapeCaret.Offset),
                shapeSelectionAnchor,
                label);
        }

        if (_hfCaret is { } headerFooterCaret
            && ResolveHfStore(headerFooterCaret.Target) is { } store
            && HeaderFooterDialogPlanner.GetSlot(store, headerFooterCaret.Target.Slot) is { } story)
        {
            var sectionNumber = Math.Max(0, headerFooterCaret.Target.SectionIndex) + 1;
            return AccessibleDocumentSnapshotPlanner.BuildHeaderFooter(
                story,
                new HeaderFooterTextPosition(
                    headerFooterCaret.Target.ParaIdx,
                    headerFooterCaret.Offset),
                _hfSelectionAnchor is { } headerFooterAnchor
                    && SameHfStory(headerFooterCaret.Target, headerFooterAnchor.Target)
                        ? new HeaderFooterTextPosition(
                            headerFooterAnchor.Target.ParaIdx,
                            headerFooterAnchor.Offset)
                        : null,
                $"Section {sectionNumber} {StoryName(headerFooterCaret.Target.Slot)}");
        }

        var caret = _cellCaret is { } cell
            ? AccessibleDocumentLocation.TableCell(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx, cell.Offset)
            : AccessibleDocumentLocation.Body(_caret.Block, _caret.Offset);
        AccessibleDocumentLocation? anchor = null;
        if (_cellCaret is not null && _cellAnchor is { } cellAnchor && !cellAnchor.Equals(_cellCaret.Value))
        {
            anchor = AccessibleDocumentLocation.TableCell(
                cellAnchor.TableBlock,
                cellAnchor.Row,
                cellAnchor.Col,
                cellAnchor.ParaIdx,
                cellAnchor.Offset);
        }
        else if (_cellCaret is null && _selectionAnchor is { } bodyAnchor && !bodyAnchor.Equals(_caret))
        {
            anchor = AccessibleDocumentLocation.Body(bodyAnchor.Block, bodyAnchor.Offset);
        }
        return AccessibleDocumentSnapshotPlanner.Build(_doc, caret, anchor);
    }

    internal DocumentAccessibilityTree AutomationSemanticTree() =>
        DocumentAccessibilityNodePlanner.Build(_doc);

    internal Rect AutomationNodeBounds(DocumentAccessibilityNode node)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        var local = AutomationNodeBoundsLocal(node);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || local.Width <= 0 || local.Height <= 0)
            return local;
        var topLeft = this.TranslatePoint(local.TopLeft, topLevel);
        var bottomRight = this.TranslatePoint(local.BottomRight, topLevel);
        return topLeft is not null && bottomRight is not null
            ? new Rect(topLeft.Value, bottomRight.Value)
            : local;
    }

    internal bool AutomationInvokeNode(DocumentAccessibilityNode node)
    {
        if (node.Kind != DocumentAccessibilityNodeKind.Hyperlink
            || string.IsNullOrWhiteSpace(node.HyperlinkTarget))
            return false;
        if (node.IsInternalHyperlink)
            return GoToBookmark(node.HyperlinkTarget);
        HyperlinkActivated?.Invoke(node.HyperlinkTarget);
        return true;
    }

    internal void AutomationFocusNode(DocumentAccessibilityNode node)
    {
        if (node.StoryKind != DocumentAccessibilityStoryKind.Body
            && AutomationHeaderFooterContext(node) is { } storyContext)
        {
            var paragraph = storyContext.Paragraph ?? storyContext.Story.Paragraphs.FirstOrDefault();
            var paragraphIndex = paragraph is null ? 0 : storyContext.Story.Paragraphs.IndexOf(paragraph);
            if (paragraphIndex < 0)
                paragraphIndex = 0;
            Focus();
            PlaceCaretInHeaderFooter(
                MakeHfTarget(storyContext.Store, storyContext.Slot, paragraphIndex),
                Math.Max(0, node.TextStart));
            ScrollToCaretRequested?.Invoke();
            return;
        }

        if (IsAutomationDrawingNode(node.Kind))
        {
            Focus();
            if (node.IsFloatingObject && node.ObjectPath is { Count: > 0 } objectPath
                && TryGetFloatingGroupChildGeometry(node.BlockIndex, node.RunIndex, objectPath, out var child))
            {
                SelectFloatingGroupChildCore(new FloatingGroupChildSelection(
                    node.BlockIndex,
                    node.RunIndex,
                    objectPath,
                    child.Child.Kind.ToString(),
                    child.Child.Rect));
            }
            else if (node.IsFloatingObject)
            {
                SelectFloating(node.BlockIndex, node.RunIndex);
            }
            else
            {
                var offset = AutomationRunStartOffset(node);
                if (node.RowIndex >= 0 && node.ColumnIndex >= 0)
                    PlaceCaretInCell(node.BlockIndex, node.RowIndex, node.ColumnIndex, Math.Max(0, node.ParagraphIndex), offset);
                else
                {
                    _cellCaret = null;
                    _cellAnchor = null;
                    _caret = new DocPosition(node.BlockIndex, offset);
                    _selectionAnchor = _caret;
                    InvalidateVisual();
                    CaretMoved?.Invoke();
                }
            }
            ScrollToCaretRequested?.Invoke();
            return;
        }

        if (node.RowIndex >= 0 && node.ColumnIndex >= 0)
        {
            Focus();
            PlaceCaretInCell(
                node.BlockIndex,
                node.RowIndex,
                node.ColumnIndex,
                Math.Max(0, node.ParagraphIndex),
                Math.Max(0, node.TextStart));
        }
        else if (node.BlockIndex >= 0 && node.BlockIndex < _doc.Blocks.Count)
        {
            _cellCaret = null;
            _cellAnchor = null;
            _caret = new DocPosition(node.BlockIndex, Math.Max(0, node.TextStart));
            _selectionAnchor = _caret;
            Focus();
            InvalidateVisual();
            CaretMoved?.Invoke();
        }
        ScrollToCaretRequested?.Invoke();
    }

    internal bool AutomationNodeHasKeyboardFocus(DocumentAccessibilityNode node)
    {
        if (!IsKeyboardFocusWithin)
            return false;
        if (node.StoryKind != DocumentAccessibilityStoryKind.Body)
        {
            if (_hfCaret is not { } headerFooterCaret
                || AutomationHeaderFooterContext(node) is not { } storyContext
                || headerFooterCaret.Target.Slot != storyContext.Slot
                || !ReferenceEquals(ResolveHfStore(headerFooterCaret.Target), storyContext.Store))
                return false;
            if (storyContext.Paragraph is null)
                return true;
            if (storyContext.Story.Paragraphs.IndexOf(storyContext.Paragraph) != headerFooterCaret.Target.ParaIdx)
                return false;
            return !IsAutomationTextRangeNode(node.Kind)
                || AutomationCaretWithinTextRange(node, headerFooterCaret.Offset);
        }
        if (IsAutomationDrawingNode(node.Kind) && node.IsFloatingObject)
        {
            if (_selectedFloating is not { } selected
                || selected.BlockIndex != node.BlockIndex
                || selected.RunIndex != node.RunIndex)
                return false;
            return node.ObjectPath is not { Count: > 0 } objectPath
                ? _selectedFloatingGroupChild is null
                : _selectedFloatingGroupChild is { } child && child.ChildPath.SequenceEqual(objectPath);
        }
        if (IsAutomationDrawingNode(node.Kind))
        {
            var objectOffset = AutomationRunStartOffset(node);
            if (node.RowIndex >= 0 && node.ColumnIndex >= 0)
            {
                return _cellCaret is { } objectCaret
                    && objectCaret.TableBlock == node.BlockIndex
                    && objectCaret.Row == node.RowIndex
                    && objectCaret.Col == node.ColumnIndex
                    && objectCaret.ParaIdx == Math.Max(0, node.ParagraphIndex)
                    && objectCaret.Offset == objectOffset;
            }
            return _cellCaret is null && _caret.Block == node.BlockIndex && _caret.Offset == objectOffset;
        }
        if (node.RowIndex >= 0 && node.ColumnIndex >= 0)
        {
            if (_cellCaret is not { } caret
                || caret.TableBlock != node.BlockIndex
                || caret.Row != node.RowIndex
                || caret.Col != node.ColumnIndex
                || (node.ParagraphIndex >= 0 && caret.ParaIdx != node.ParagraphIndex))
                return false;
            return !IsAutomationTextRangeNode(node.Kind)
                || AutomationCaretWithinTextRange(node, caret.Offset);
        }
        if (_cellCaret is not null || _caret.Block != node.BlockIndex)
            return false;
        return !IsAutomationTextRangeNode(node.Kind)
            || AutomationCaretWithinTextRange(node, _caret.Offset);
    }

    /// <summary>
    /// AV-A11Y: ticks/unticks the check-box field a semantic node describes, through the same public
    /// interaction the mouse uses — so the control's lock and the document's protection still decide,
    /// and the change is undoable. Table cells address their own paragraph; a header/footer field is not
    /// reachable from the body semantic tree and is not offered.
    /// </summary>
    internal bool AutomationToggleContentControl(DocumentAccessibilityNode node)
    {
        if (node.Kind != DocumentAccessibilityNodeKind.ContentControl
            || node.ContentControlKind != FreeW.Core.Model.ContentControlKind.CheckBox
            || node.BlockIndex < 0
            || node.RunIndex < 0)
        {
            return false;
        }

        return node.RowIndex >= 0 && node.ColumnIndex >= 0 && node.ParagraphIndex >= 0
            ? ApplyContentControlInteraction(
                new ContentControlTarget(
                    node.BlockIndex, node.RunIndex, node.RowIndex, node.ColumnIndex, node.ParagraphIndex),
                ContentControlInteractionPlanner.ToggleCheckBox)
            : ToggleContentControl(node.BlockIndex, node.RunIndex);
    }

    internal bool AutomationNodeIsSelected(DocumentAccessibilityNode node)
    {
        if (!node.IsFloatingObject)
            return false;
        if (node.ObjectPath is { Count: > 0 } objectPath)
        {
            return _selectedFloatingGroupChild is { } child
                && child.BlockIndex == node.BlockIndex
                && child.RunIndex == node.RunIndex
                && child.ChildPath.SequenceEqual(objectPath);
        }
        if (_selectedFloatingGroupChild is { } selectedChild
            && selectedChild.BlockIndex == node.BlockIndex
            && selectedChild.RunIndex == node.RunIndex)
            return false;
        return _selectedFloatingObjects.Any(selected =>
            selected.BlockIndex == node.BlockIndex && selected.RunIndex == node.RunIndex);
    }

    internal void AutomationSelectNode(DocumentAccessibilityNode node, bool addToSelection)
    {
        if (!node.IsFloatingObject)
            return;
        Focus();
        if (node.ObjectPath is { Count: > 0 } objectPath
            && TryGetFloatingGroupChildGeometry(node.BlockIndex, node.RunIndex, objectPath, out var child))
        {
            SelectFloatingGroupChildCore(new FloatingGroupChildSelection(
                node.BlockIndex,
                node.RunIndex,
                objectPath,
                child.Child.Kind.ToString(),
                child.Child.Rect));
            return;
        }
        SelectFloating(node.BlockIndex, node.RunIndex, addToSelection);
    }

    internal void AutomationRemoveNodeFromSelection(DocumentAccessibilityNode node)
    {
        if (AutomationNodeIsSelected(node))
            SelectFloating(node.BlockIndex, node.RunIndex, addToMultiSelect: true);
    }

    private int AutomationRunStartOffset(DocumentAccessibilityNode node)
    {
        if (AutomationNodeParagraph(node) is not { } paragraph)
            return 0;
        var offset = 0;
        for (var index = 0; index < Math.Min(node.RunIndex, paragraph.Runs.Count); index++)
            offset += AutomationRunLayoutLength(paragraph.Runs[index]);
        return offset;
    }

    private static bool IsAutomationDrawingNode(DocumentAccessibilityNodeKind kind) =>
        kind is DocumentAccessibilityNodeKind.Image
            or DocumentAccessibilityNodeKind.Shape
            or DocumentAccessibilityNodeKind.Chart
            or DocumentAccessibilityNodeKind.WordArt
            or DocumentAccessibilityNodeKind.SmartArt
            or DocumentAccessibilityNodeKind.DrawingGroup
            or DocumentAccessibilityNodeKind.EmbeddedObject;

    private Rect AutomationNodeBoundsLocal(DocumentAccessibilityNode node)
    {
        var rectangles = new List<Rect>();
        if (node.StoryKind != DocumentAccessibilityStoryKind.Body
            && node.Kind is DocumentAccessibilityNodeKind.Paragraph
                or DocumentAccessibilityNodeKind.Heading
                or DocumentAccessibilityNodeKind.TextRun
                or DocumentAccessibilityNodeKind.Hyperlink
            && AutomationHeaderFooterContext(node) is { Paragraph: not null } storyContext)
        {
            var paragraphIndex = storyContext.Story.Paragraphs.IndexOf(storyContext.Paragraph);
            rectangles.AddRange(_headerFooterItems
                .Where(item => item.Target is { } target
                    && target.Slot == storyContext.Slot
                    && target.ParaIdx == paragraphIndex
                    && ReferenceEquals(ResolveHfStore(target), storyContext.Store))
                .Select(item => new Rect(
                    item.X,
                    item.Y,
                    Math.Max(1, item.Width > 0 ? item.Width : item.AvailableWidth),
                    Math.Max(1, item.Height > 0 ? item.Height : item.LineHeight))));
        }
        if (node.StoryKind != DocumentAccessibilityStoryKind.Body
            && node.Kind == DocumentAccessibilityNodeKind.EmbeddedObject
            && AutomationHeaderFooterContext(node) is { Paragraph: not null } embeddedContext)
        {
            var paragraphIndex = embeddedContext.Story.Paragraphs.IndexOf(embeddedContext.Paragraph);
            rectangles.AddRange(_headerFooterItems
                .Where(item => item.OwnerTarget is { } target
                    && target.Slot == embeddedContext.Slot
                    && target.ParaIdx == paragraphIndex
                    && ReferenceEquals(ResolveHfStore(target), embeddedContext.Store)
                    && item.EmbeddedObject is not null
                    && item.RunIndex == node.RunIndex)
                .Select(item => new Rect(item.X, item.Y, Math.Max(1, item.Width), Math.Max(1, item.Height))));
        }
        if (node.ObjectPath is { Count: > 0 } objectPath
            && TryGetFloatingGroupChildGeometry(node.BlockIndex, node.RunIndex, objectPath, out var groupChild))
            rectangles.Add(groupChild.Child.Rect);

        switch (node.Kind)
        {
            case DocumentAccessibilityNodeKind.Table:
                rectangles.AddRange(_cellHits.Where(hit => hit.Block == node.BlockIndex).Select(hit => hit.Rect));
                break;
            case DocumentAccessibilityNodeKind.TableRow:
                rectangles.AddRange(_cellHits.Where(hit => hit.Block == node.BlockIndex && hit.Row == node.RowIndex).Select(hit => hit.Rect));
                break;
            case DocumentAccessibilityNodeKind.TableCell:
                rectangles.AddRange(_cellHits.Where(hit => hit.Block == node.BlockIndex && hit.Row == node.RowIndex && hit.Col == node.ColumnIndex).Select(hit => hit.Rect));
                break;
            case DocumentAccessibilityNodeKind.Image:
                if (node.ObjectPath is not { Count: > 0 })
                {
                    var imageModel = AutomationNodeImage(node);
                    rectangles.AddRange(_images
                        .Where(image => image.BlockIndex == node.BlockIndex
                            && (imageModel is null || ReferenceEquals(image.Model, imageModel)))
                        .Select(image => image.Rect));
                    rectangles.AddRange(_floatingImages
                        .Where(image => image.BlockIndex == node.BlockIndex
                            && image.RunIndex == node.RunIndex
                            && (imageModel is null || ReferenceEquals(image.Model, imageModel)))
                        .Select(image => image.Rect));
                }
                break;
            case DocumentAccessibilityNodeKind.Shape:
                if (node.ObjectPath is not { Count: > 0 })
                {
                    rectangles.AddRange(_inlineShapes.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                    rectangles.AddRange(_floatingShapes.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                }
                break;
            case DocumentAccessibilityNodeKind.Chart:
                if (node.ObjectPath is not { Count: > 0 })
                {
                    rectangles.AddRange(_inlineCharts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                    rectangles.AddRange(_floatingCharts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                }
                break;
            case DocumentAccessibilityNodeKind.WordArt:
                if (node.ObjectPath is not { Count: > 0 })
                {
                    rectangles.AddRange(_inlineWordArts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                    rectangles.AddRange(_floatingWordArts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                }
                break;
            case DocumentAccessibilityNodeKind.SmartArt:
                if (node.ObjectPath is not { Count: > 0 })
                {
                    rectangles.AddRange(_inlineSmartArts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                    rectangles.AddRange(_floatingSmartArts.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                }
                break;
            case DocumentAccessibilityNodeKind.DrawingGroup:
                if (node.ObjectPath is not { Count: > 0 })
                    rectangles.AddRange(_floatingGroups.Where(item => item.BlockIndex == node.BlockIndex && item.RunIndex == node.RunIndex).Select(item => item.Rect));
                break;
            case DocumentAccessibilityNodeKind.EmbeddedObject:
                rectangles.AddRange(_inlineEmbeddedObjects
                    .Where(item => item.BlockIndex == node.BlockIndex
                        && item.RunIndex == node.RunIndex
                        && item.CellRow == node.RowIndex
                        && item.CellColumn == node.ColumnIndex
                        && item.CellParagraphIndex == node.ParagraphIndex)
                    .Select(item => item.Rect));
                break;
            case DocumentAccessibilityNodeKind.Paragraph:
            case DocumentAccessibilityNodeKind.Heading:
            case DocumentAccessibilityNodeKind.TextRun:
            case DocumentAccessibilityNodeKind.Hyperlink:
                if (node.StoryKind == DocumentAccessibilityStoryKind.Body)
                {
                    rectangles.AddRange(_placed
                        .Where(placed => AutomationPlacedCharBelongsToNode(placed, node))
                        .Select(placed => new Rect(placed.X, placed.Y, Math.Max(1, placed.W), Math.Max(1, placed.LineHeight))));
                }
                break;
        }

        foreach (var child in node.SemanticChildren)
        {
            var childBounds = AutomationNodeBoundsLocal(child);
            if (childBounds.Width > 0 && childBounds.Height > 0)
                rectangles.Add(childBounds);
        }
        return UnionRectangles(rectangles);
    }

    private bool AutomationPlacedCharBelongsToNode(PlacedChar placed, DocumentAccessibilityNode node)
    {
        if (placed.Block != node.BlockIndex || placed.Sentinel)
            return false;
        if (node.RowIndex >= 0)
        {
            if (!placed.IsCell
                || placed.CellRow != node.RowIndex
                || placed.CellCol != node.ColumnIndex
                || (node.ParagraphIndex >= 0 && placed.CellParaIdx != node.ParagraphIndex))
                return false;
            if (!IsAutomationTextRangeNode(node.Kind))
                return true;
            var range = AutomationTextLayoutRange(node);
            return placed.CellParaOffset >= range.Start && placed.CellParaOffset < range.Start + range.Length;
        }
        if (placed.IsCell)
            return false;
        if (!IsAutomationTextRangeNode(node.Kind))
            return true;
        var bodyRange = AutomationTextLayoutRange(node);
        return placed.Offset >= bodyRange.Start && placed.Offset < bodyRange.Start + bodyRange.Length;
    }

    private InlineImage? AutomationNodeImage(DocumentAccessibilityNode node) =>
        AutomationNodeParagraph(node) is { } paragraph
        && node.RunIndex >= 0
        && node.RunIndex < paragraph.Runs.Count
            ? paragraph.Runs[node.RunIndex].Image
            : null;

    private static bool IsAutomationTextRangeNode(DocumentAccessibilityNodeKind kind) =>
        kind is DocumentAccessibilityNodeKind.TextRun or DocumentAccessibilityNodeKind.Hyperlink;

    private bool AutomationCaretWithinTextRange(DocumentAccessibilityNode node, int offset)
    {
        var (start, length) = AutomationTextLayoutRange(node);
        return length == 0
            ? offset == start
            : offset >= start && offset < start + length;
    }

    private (int Start, int Length) AutomationTextLayoutRange(DocumentAccessibilityNode node)
    {
        if (node.Kind == DocumentAccessibilityNodeKind.TextRun)
            return (Math.Max(0, node.TextStart), Math.Max(0, node.TextLength));
        if (AutomationNodeParagraph(node) is not { } paragraph)
            return (Math.Max(0, node.TextStart), Math.Max(0, node.TextLength));
        var start = 0;
        for (var index = 0; index < Math.Min(node.RunIndex, paragraph.Runs.Count); index++)
            start += AutomationRunLayoutLength(paragraph.Runs[index]);
        var length = 0;
        for (var index = Math.Max(0, node.RunIndex); index < paragraph.Runs.Count; index++)
        {
            var run = paragraph.Runs[index];
            var hasTarget = node.IsInternalHyperlink
                ? run.HyperlinkUrl is null && string.Equals(run.HyperlinkAnchor, node.HyperlinkTarget, StringComparison.Ordinal)
                : run.HyperlinkAnchor is null && string.Equals(run.HyperlinkUrl, node.HyperlinkTarget, StringComparison.Ordinal);
            var expectedTooltip = string.Equals(node.HelpText, node.HyperlinkTarget, StringComparison.Ordinal)
                ? null
                : node.HelpText;
            if (!hasTarget || !string.Equals(run.HyperlinkTooltip, expectedTooltip, StringComparison.Ordinal))
                break;
            length += AutomationRunLayoutLength(run);
        }
        return (start, length);
    }

    private Paragraph? AutomationNodeParagraph(DocumentAccessibilityNode node)
    {
        if (node.StoryKind != DocumentAccessibilityStoryKind.Body)
            return AutomationHeaderFooterContext(node)?.Paragraph;
        if (node.BlockIndex < 0 || node.BlockIndex >= _doc.Blocks.Count)
            return null;
        if (node.RowIndex < 0)
            return _doc.Blocks[node.BlockIndex] as Paragraph;
        var cell = GetCellModel(node.BlockIndex, node.RowIndex, node.ColumnIndex);
        return cell is not null && node.ParagraphIndex >= 0 && node.ParagraphIndex < cell.Paragraphs.Count
            ? cell.Paragraphs[node.ParagraphIndex]
            : null;
    }

    private (SectionHeadersFooters Store, HeaderFooterSlotKind Slot, HeaderFooter Story, Paragraph? Paragraph)?
        AutomationHeaderFooterContext(DocumentAccessibilityNode node)
    {
        if (node.StoryKind == DocumentAccessibilityStoryKind.Body)
            return null;
        var sections = _doc.Sections;
        if (node.SectionIndex < 0 || node.SectionIndex >= sections.Count)
            return null;
        var store = sections[node.SectionIndex].HeadersFooters;
        var slot = node.StoryKind switch
        {
            DocumentAccessibilityStoryKind.Header => HeaderFooterSlotKind.Header,
            DocumentAccessibilityStoryKind.Footer => HeaderFooterSlotKind.Footer,
            DocumentAccessibilityStoryKind.EvenHeader => HeaderFooterSlotKind.EvenHeader,
            DocumentAccessibilityStoryKind.EvenFooter => HeaderFooterSlotKind.EvenFooter,
            DocumentAccessibilityStoryKind.FirstHeader => HeaderFooterSlotKind.FirstHeader,
            DocumentAccessibilityStoryKind.FirstFooter => HeaderFooterSlotKind.FirstFooter,
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.StoryKind, null)
        };
        var story = HeaderFooterDialogPlanner.GetSlot(store, slot);
        if (story is null)
            return null;

        Paragraph? paragraph = null;
        if (node.RowIndex >= 0 && story.Table is { } table)
        {
            var cell = GetCellModelGridCol(table, node.RowIndex, node.ColumnIndex);
            if (cell is not null && node.ParagraphIndex >= 0 && node.ParagraphIndex < cell.Paragraphs.Count)
                paragraph = cell.Paragraphs[node.ParagraphIndex];
        }
        else if (node.ParagraphIndex >= 0 && node.ParagraphIndex < story.Paragraphs.Count)
        {
            paragraph = story.Paragraphs[node.ParagraphIndex];
        }
        return (store, slot, story, paragraph);
    }

    private static int AutomationRunLayoutLength(Run run)
    {
        if (IsFloatingDrawingRun(run))
            return 0;
        if (run.Image is not null
            || run.Shape is not null
            || run.Chart is not null
            || run.WordArt is not null
            || run.SmartArt is not null
            || run.EmbeddedObject is not null)
            return 1;
        return run.Text.Length;
    }

    private static string StoryName(HeaderFooterSlotKind slot) => slot switch
    {
        HeaderFooterSlotKind.Header => "default header",
        HeaderFooterSlotKind.Footer => "default footer",
        HeaderFooterSlotKind.EvenHeader => "even-page header",
        HeaderFooterSlotKind.EvenFooter => "even-page footer",
        HeaderFooterSlotKind.FirstHeader => "first-page header",
        HeaderFooterSlotKind.FirstFooter => "first-page footer",
        _ => "header or footer"
    };

    private static Rect UnionRectangles(IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count == 0)
            return default;
        var left = rectangles.Min(rect => rect.Left);
        var top = rectangles.Min(rect => rect.Top);
        var right = rectangles.Max(rect => rect.Right);
        var bottom = rectangles.Max(rect => rect.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
