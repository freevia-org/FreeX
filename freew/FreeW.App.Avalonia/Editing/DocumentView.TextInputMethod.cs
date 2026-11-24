using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private DocumentTextInputMethodClient? _textInputMethodClient;

    // Avalonia 12.0.4's TextInputMethodClient and AvaloniaNativeTextInputMethod are the contract here.
    // Pinned source: AvaloniaUI/Avalonia a8dd6417fd8918570edefdbecd92d16ac7620069,
    // src/Avalonia.Base/Input/TextInput/TextInputMethodClient.cs and
    // src/Avalonia.Native/AvaloniaNativeTextInputMethod.cs.
    // Composition is deliberately not advertised: the renderer needs a transient inline projection
    // before marked text can reflow without changing model offsets, selections or undo history.
    private void InitializeTextInputMethod()
    {
        if (_textInputMethodClient is not null)
            return;
        _textInputMethodClient = new(this);
        TextInputMethodClientRequested += OnTextInputMethodClientRequested;
        CaretMoved += RefreshTextInputMethod;
        DocumentChanged += RefreshTextInputMethod;
        DocumentLoaded += RefreshTextInputMethod;
        LayoutUpdated += (_, _) => RefreshTextInputMethod();
        GotFocus += (_, e) => { if (ReferenceEquals(e.Source, this)) RefreshTextInputMethod(); };
        LostFocus += (_, e) => { if (ReferenceEquals(e.Source, this)) RefreshTextInputMethod(); };
    }

    private void OnTextInputMethodClientRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, this))
            return;
        RefreshTextInputMethod();
        if (_textInputMethodClient is { HasContext: true } client)
        {
            e.Client = client;
            e.Handled = true;
        }
    }

    private void RefreshTextInputMethod()
    {
        // Legacy placements may raise CaretMoved inside a compound gesture. Its final event, after
        // the command scope closes, publishes the coherent text and selection together.
        if (_textGestureActive || _textInputMethodClient is not { } client)
            return;
        var previouslyAvailable = client.HasContext;
        var context = TryGetTextInputMethodContext(out var rect);
        client.Update(context, rect);
        if (previouslyAvailable != client.HasContext && IsFocused)
            RaiseEvent(new TextInputMethodClientRequeryRequestedEventArgs
            {
                RoutedEvent = InputMethod.TextInputMethodClientRequeryRequestedEvent,
            });
    }

    private enum TextInputStory { Body, Cell, HeaderFooter, Shape }

    private sealed record TextInputMethodContext(
        TextDocument Document,
        Paragraph? Paragraph,
        string Text,
        TextSelection Selection,
        TextInputStory Story,
        int BlockIndex = -1,
        int RunIndex = -1,
        int Row = -1,
        int Column = -1,
        int ParagraphIndex = 0,
        HfTarget? HeaderTarget = null);

    private TextInputMethodContext? TryGetTextInputMethodContext(out Rect rect)
    {
        rect = default;
        if (!IsFocused || !FreeWNativeTextContextCapability.AllowsClient(TopLevel.GetTopLevel(this))
            || !IsEffectivelyEnabled || IsEditingLocked || _textGestureActive
            || SelectedCellRange is not null || _bodyDragPending || _bodyDragActive
            || _hfSelectionDragActive || _shapeTextSelectionDragState is not null)
            return null;
        TextInputMethodContext context;
        if (_shapeCaret is { } shapeCaret)
        {
            if (_activeShapeTextChildPath is { Count: > 0 }
                || !TryGetShapeTextTarget(shapeCaret.BlockIndex, shapeCaret.RunIndex, null, out _, out var shape)
                || shape.TextDirection != ShapeTextDirection.Horizontal || shape.RotationAngle != 0 || shape.FlipH || shape.FlipV
                || shapeCaret.TextParagraphIndex < 0 || shapeCaret.TextParagraphIndex >= shape.TextParagraphs.Count)
                return null;
            var paragraph = shape.TextParagraphs[shapeCaret.TextParagraphIndex];
            // Range replacement rebuilds this paragraph's runs without remapping its bookmark
            // boundaries, and clones the other shape paragraphs through the shared run cloner.
            if (paragraph.BookmarkBoundaries.Count > 0
                || shape.TextParagraphs.Any(item => item.Runs.Any(run => run.NestedRevision is not null)))
                return null;
            var caretOffset = ShapeTextOffset(shape, shapeCaret);
            var anchorOffset = caretOffset;
            if (_shapeSelectionAnchor is { } anchor)
            {
                if (anchor.BlockIndex != shapeCaret.BlockIndex || anchor.RunIndex != shapeCaret.RunIndex
                    || anchor.TextParagraphIndex != shapeCaret.TextParagraphIndex)
                    return null;
                anchorOffset = ShapeTextOffset(shape, anchor);
            }
            context = new(_doc, paragraph, paragraph.PlainText, new(anchorOffset, caretOffset), TextInputStory.Shape,
                shapeCaret.BlockIndex, shapeCaret.RunIndex, ParagraphIndex: shapeCaret.TextParagraphIndex);
        }
        else if (_hfCaret is { } headerCaret)
        {
            var paragraph = GetHfParagraph(headerCaret.Target);
            if (!IsTextInputMethodHeaderFooterParagraph(paragraph))
                return null;
            // Only the shared literal layout supplies actual subsequent-line caret geometry.
            // The styled/tab legacy path still measures prefix width at the item's original Y.
            if (paragraph?.Runs.Any(run => run.Text.Any(IsBodySoftLineBreak)) == true
                && !HeaderFooterUsesSharedLiteralLayout(paragraph))
                return null;
            if (paragraph is null && headerCaret.Target.ParaIdx != 0)
                return null;
            var anchorOffset = headerCaret.Offset;
            if (_hfSelectionAnchor is { } anchor)
            {
                if (!SameHfStory(anchor.Target, headerCaret.Target) || anchor.Target.ParaIdx != headerCaret.Target.ParaIdx)
                    return null;
                anchorOffset = anchor.Offset;
            }
            context = new(_doc, paragraph, paragraph?.PlainText ?? string.Empty,
                new(anchorOffset, headerCaret.Offset), TextInputStory.HeaderFooter,
                ParagraphIndex: headerCaret.Target.ParaIdx, HeaderTarget: headerCaret.Target);
        }
        else if (_cellCaret is { } cellCaret)
        {
            if (GetCellParagraph(cellCaret.TableBlock, cellCaret.Row, cellCaret.Col, cellCaret.ParaIdx) is not { } paragraph
                || GetCellModel(cellCaret.TableBlock, cellCaret.Row, cellCaret.Col)?.TextDirection != CellTextDirection.Horizontal)
                return null;
            var anchorOffset = cellCaret.Offset;
            if (_cellAnchor is { } anchor)
            {
                if (anchor.TableBlock != cellCaret.TableBlock || anchor.Row != cellCaret.Row || anchor.Col != cellCaret.Col
                    || anchor.ParaIdx != cellCaret.ParaIdx)
                    return null;
                anchorOffset = anchor.Offset;
            }
            context = new(_doc, paragraph, paragraph.PlainText, new(anchorOffset, cellCaret.Offset), TextInputStory.Cell,
                cellCaret.TableBlock, Row: cellCaret.Row, Column: cellCaret.Col, ParagraphIndex: cellCaret.ParaIdx);
        }
        else
        {
            if (_selectedFloating is not null || _selectedInlineObject is not null || _selectedFloatingObjects.Count > 0
                || _caret.Block < 0 || _caret.Block >= _doc.Blocks.Count || _doc.Blocks[_caret.Block] is not Paragraph paragraph
                || _selectionAnchor is { } anchor && anchor.Block != _caret.Block)
                return null;
            context = new(_doc, paragraph, paragraph.PlainText, new(_selectionAnchor?.Offset ?? _caret.Offset, _caret.Offset),
                TextInputStory.Body, _caret.Block);
        }

        if (!IsTextInputMethodLiteralParagraph(context.Paragraph)
            || !IsTextInputMethodBoundary(context.Text, context.Selection.Start)
            || !IsTextInputMethodBoundary(context.Text, context.Selection.End))
            return null;
        // The portable ordinary-body coalescer does not compare character-style identities.
        // Replacing a prefix can otherwise merge its new plain run with an untouched styled suffix.
        if (context.Story == TextInputStory.Body && context.Paragraph!.Runs.Any(run => run.StyleId is not null))
            return null;
        if (context.Paragraph is { } source && context.Story is TextInputStory.Body or TextInputStory.Cell
            && KeyboardParagraphText(new(source, context.BlockIndex, context.Row, context.Column, context.ParagraphIndex)) != context.Text)
            return null;
        // Validate source eligibility before asking the legacy renderer to measure it. In particular,
        // rejected controls or multiline HF fallback must not reach a standalone no-glyph formatter.
        if (_laidOutWidth < 0)
            Relayout(Bounds.Width > 0 && double.IsFinite(Bounds.Width) ? Bounds.Width : FallbackWidth);
        switch (context.Story)
        {
            case TextInputStory.Shape:
                if (!TryGetActiveShapeCaretStop(out var stop)) return null;
                rect = new(stop.X, stop.Y, 1.5, stop.Height);
                break;
            case TextInputStory.HeaderFooter:
                if (!TryGetHfCaretRect(out rect)) return null;
                break;
            default:
                if (!TryGetCaretRect(out rect)) return null;
                break;
        }
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y)
            || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || rect.Width <= 0 || rect.Height <= 0)
            return null;
        return context;
    }

    private bool IsTextInputMethodLiteralParagraph(Paragraph? paragraph) => paragraph is null
        || paragraph.BlockContentControl is null && paragraph.BlockCustomXml is null
        && paragraph.Runs.All(run => IsUnicodeLiteralRun(run)
            && run.TableFormula is null && run.CrossReference is null && run.Citation is null
            && run.PreservedDrawing is null && run.SubDocument is null && run.Ruby is null
            && run.Control is null && !run.IsCommentReference && !run.IsPageBreak && !run.IsColumnBreak
            && run.MoveRevisionId is null && run.Revision == RevisionKind.None && run.FormatRevision is null
            && run.NestedRevision is null && run.RevisionAuthor is null && run.RevisionDateXml is null
            && !(run.Text.Length == 0 && run.CommentId is not null)
            && !IsTextHiddenInCurrentView(ResolveRunFmt(run, paragraph))
            && IsTextInputMethodLiteralText(run.Text));

    // HfAtoms/HfSetAtoms preserve literal text, direct formatting and controls only. The
    // normal keyboard path still needs a metadata-preserving rebuild; do not expose new native
    // reconversion in stories whose authored metadata that existing edit path would discard.
    private static bool IsTextInputMethodHeaderFooterParagraph(Paragraph? paragraph) => paragraph is null
        || paragraph.BookmarkBoundaries.Count == 0
        && paragraph.Runs.All(run => run.CommentId is null
            && run.HyperlinkUrl is null && run.HyperlinkAnchor is null && run.HyperlinkTooltip is null
            && run.StyleId is null && run.RevisionAuthor is null && run.RevisionDateXml is null
            && run.PreservedDrawingPreview is null && !run.FieldLocked && !run.FieldCodeVisible
            && (run.Text.Length > 0 || run.Formatting == RunFormatting.Default));

    private static bool IsTextInputMethodLiteralText(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            // These Word text controls retain source offsets. The body/cell projection equality
            // check handles actual mismatches, such as a cell's collapsed CRLF pair.
            // NUL and other unsupported C0/C1 controls are not native surrounding text.
            if (char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\v'))
                return false;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[++index])) return false;
            }
            else if (char.IsLowSurrogate(character)) return false;
        }
        return true;
    }

    private static bool IsTextInputMethodBoundary(string text, int offset) =>
        offset >= 0 && offset <= text.Length
        && (offset == 0 || offset == text.Length || Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), offset) >= 0);

    private void ApplyTextInputMethodSelection(TextSelection selection)
    {
        // Native ranges are UTF-16 offsets in the supplied paragraph. Reject unsafe requests rather
        // than clamping them to a different character or mapping through a field/object projection.
        var context = TryGetTextInputMethodContext(out _);
        if (context is null || !IsTextInputMethodBoundary(context.Text, selection.Start)
            || !IsTextInputMethodBoundary(context.Text, selection.End) || context.Selection == selection)
            return;
        switch (context.Story)
        {
            case TextInputStory.Body:
                _selectionAnchor = new DocPosition(context.BlockIndex, selection.Start);
                _caret = new DocPosition(context.BlockIndex, selection.End);
                break;
            case TextInputStory.Cell:
                var cell = _cellCaret!.Value;
                _cellAnchor = cell with { Offset = selection.Start };
                _cellCaret = cell with { Offset = selection.End };
                _caret = new DocPosition(cell.TableBlock,
                    FindCellGlyphOffset(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx, selection.End));
                _selectionAnchor = _caret;
                break;
            case TextInputStory.HeaderFooter:
                _hfSelectionAnchor = (context.HeaderTarget!.Value, selection.Start);
                _hfCaret = (context.HeaderTarget.Value, selection.End);
                break;
            case TextInputStory.Shape:
                var shapeCaret = _shapeCaret!.Value;
                if (!TryGetShapeTextTarget(shapeCaret.BlockIndex, shapeCaret.RunIndex, null, out _, out var shape))
                    return;
                _shapeSelectionAnchor = ShapeTextPositionAtOffset(shape, context.BlockIndex, context.RunIndex,
                    context.ParagraphIndex, selection.Start);
                _shapeCaret = ShapeTextPositionAtOffset(shape, context.BlockIndex, context.RunIndex,
                    context.ParagraphIndex, selection.End);
                break;
        }
        InvalidateVisual();
        CaretMoved?.Invoke();
        ScrollToCaretRequested?.Invoke();
    }

    private sealed class DocumentTextInputMethodClient(DocumentView owner) : TextInputMethodClient
    {
        private TextInputMethodContext? _context;
        private Rect _cursorRectangle;
        public bool HasContext => _context is not null;
        public override Visual TextViewVisual => owner;
        public override bool SupportsPreedit => false;
        // This is a fixed capability. An unsafe active context detaches the client instead; changing
        // this property dynamically also prevents Avalonia.Native from unsubscribing its old client.
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => _context?.Text ?? string.Empty;
        // View-local DIP: the native bridge transforms this visual to the root. The parent
        // LayoutTransformControl/ScrollViewer apply zoom and scrolling exactly once.
        public override Rect CursorRectangle => _cursorRectangle;
        public override TextSelection Selection
        {
            get
            {
                var selection = _context?.Selection ?? default;
                // Pinned AvnView.mm constructs NSMakeRange(start, end - start) without sorting.
                // Cocoa ranges are unordered; a reversed pair underflows its unsigned length.
                // Normalize only this native boundary, retaining the host anchor, active caret,
                // and cursor rectangle. Other Avalonia backends retain directional endpoints.
                return OperatingSystem.IsMacOS()
                    ? new(Math.Min(selection.Start, selection.End), Math.Max(selection.Start, selection.End))
                    : selection;
            }
            set => owner.ApplyTextInputMethodSelection(value);
        }

        public void Update(TextInputMethodContext? context, Rect cursorRectangle)
        {
            var previous = _context;
            var previousRectangle = _cursorRectangle;
            _context = context;
            _cursorRectangle = context is null ? default : cursorRectangle;
            if (previous?.Selection != context?.Selection)
                RaiseSelectionChanged();
            // Avalonia.Native 12.0.4 reads BOTH text and selection from this event. It does not
            // subscribe to SelectionChanged, so selection-only moves must notify this event too.
            if (previous != context)
                RaiseSurroundingTextChanged();
            if (previousRectangle != _cursorRectangle)
                RaiseCursorRectangleChanged();
        }
    }
}
