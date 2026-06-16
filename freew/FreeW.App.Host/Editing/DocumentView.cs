using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.Model;
using System.Diagnostics;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;
using WpfHyperlink = System.Windows.Documents.Hyperlink;
using WpfList = System.Windows.Documents.List;
using WpfListItem = System.Windows.Documents.ListItem;
using WpfTable = System.Windows.Documents.Table;
using WpfTableRow = System.Windows.Documents.TableRow;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTextAlignment = System.Windows.TextAlignment;
using ModelBlock = FreeW.Core.Model.Block;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;
using ModelTable = FreeW.Core.Model.Table;
using ModelTableRow = FreeW.Core.Model.TableRow;
using ModelTableCell = FreeW.Core.Model.TableCell;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The FreeW editing surface: a RichTextBox that renders a <see cref="TextDocument"/> into a
/// WPF FlowDocument (resolving run/paragraph formatting through styles + document defaults) and
/// commits edits back into the model. Caret, selection, typing, delete and Enter come from the
/// RichTextBox; <see cref="CommitToModel"/> maps the edited view back to the model.
/// </summary>
public sealed class DocumentView : RichTextBox
{
    private const double PxPerPoint = 96.0 / 72.0;

    /// <summary>Document default run size in points, used when a run inherits its size.</summary>
    private const double DefaultFontSizePt = 11;

    /// <summary>Glyph-shrink factor applied to superscript/subscript runs (and undone on commit).</summary>
    private const double SuperSubScale = 0.65;

    private TextDocument _model = TextDocument.CreateEmpty();
    private readonly DocumentCommandBus _commands;
    private readonly ScaleTransform _zoomTransform = new(ZoomLevels.Default, ZoomLevels.Default);
    private double _zoomLevel = ZoomLevels.Default;

    public DocumentView()
    {
        AcceptsTab = true;
        IsDocumentEnabled = true;
        SpellCheck.IsEnabled = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        Background = Brushes.White;
        Padding = new Thickness(48);

        // Scale the editing surface via a LayoutTransform so text, images, and tables all zoom together
        // while the model and on-disk document are untouched (this is pure view chrome).
        LayoutTransform = _zoomTransform;

        _commands = new DocumentCommandBus(new ViewContext(this));
        _commands.Changed += Render;
    }

    public TextDocument Model => _model;

    /// <summary>
    /// When true (the default), as-you-type smart typing corrections (smart quotes, dashes, symbols,
    /// ellipsis, sentence capitalization) are applied via <see cref="AutoCorrect"/> on each keystroke.
    /// </summary>
    public bool AutoCorrectEnabled { get; set; } = true;

    /// <summary>Raised whenever <see cref="ZoomLevel"/> changes; carries the new factor (1.0 == 100%).</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// The editor zoom factor where 1.0 == 100%. Assignments are clamped to the supported range
    /// (<see cref="ZoomLevels.Min"/>..<see cref="ZoomLevels.Max"/>) and applied as a <see cref="ScaleTransform"/>
    /// on the editing surface. Purely visual: the model and saved document are unaffected.
    /// </summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            var clamped = ZoomLevels.Clamp(value);
            if (clamped == _zoomLevel)
                return;
            _zoomLevel = clamped;
            _zoomTransform.ScaleX = clamped;
            _zoomTransform.ScaleY = clamped;
            ZoomChanged?.Invoke(this, clamped);
        }
    }

    // Ctrl+MouseWheel zooms the surface in/out one step per notch (optional convenience). The event is
    // marked handled so the editor does not also scroll while the user is zooming.
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ZoomLevel = e.Delta > 0 ? ZoomLevels.StepUp(_zoomLevel) : ZoomLevels.StepDown(_zoomLevel);
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    /// <summary>
    /// As-you-type smart typing. Before the RichTextBox inserts the typed character, ask
    /// <see cref="AutoCorrect"/> (using the text immediately before the caret in the current paragraph)
    /// whether this keystroke triggers a correction. If so, apply the replacement through the normal
    /// edit path (so it is captured by the editor's own undo stack) and mark the event handled so the
    /// raw character is not also inserted. Otherwise let the keystroke proceed unchanged.
    /// </summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (AutoCorrectEnabled
            && !string.IsNullOrEmpty(e.Text)
            && e.Text.Length == 1
            && Selection.IsEmpty
            && TryAutoCorrect(e.Text[0]))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewTextInput(e);
    }

    // Read the text before the caret (within the current paragraph), evaluate the AutoCorrect rules for
    // the just-typed char, and if one fires, delete back N chars and insert the replacement at the caret.
    // Returns true when a correction was applied (the raw keystroke should be suppressed).
    private bool TryAutoCorrect(char justTyped)
    {
        var caret = CaretPosition?.GetInsertionPosition(LogicalDirection.Backward);
        if (caret?.Paragraph is null)
            return false;

        // Text from the start of the current paragraph up to the caret. AutoCorrect only inspects a few
        // trailing characters, but the paragraph-relative text is enough to detect a paragraph start.
        var start = caret.Paragraph.ContentStart;
        var textBefore = new TextRange(start, caret).Text;

        var result = AutoCorrect.Evaluate(textBefore, justTyped);
        if (!result.Applies)
            return false;

        // Walk back DeleteBefore characters (caret-relative) to find the start of the range to replace.
        var deleteStart = caret;
        for (var i = 0; i < result.DeleteBefore; i++)
        {
            var prev = deleteStart?.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev is null)
                return false; // not enough room (e.g. crossed a run/paragraph boundary) — bail safely
            deleteStart = prev;
        }
        if (deleteStart is null)
            return false;

        // Replace [deleteStart, caret) with the insertion text in one edit so it is a single undo unit.
        var range = new TextRange(deleteStart, caret) { Text = result.Insert };
        CaretPosition = range.End;
        return true;
    }

    /// <summary>Undo/redo command bus over this view's model (backed by the shared UndoRedoStack).</summary>
    public DocumentCommandBus Commands => _commands;

    /// <summary>Render a model document into the editable surface.</summary>
    public void LoadModel(TextDocument document)
    {
        _model = document;
        Render();
    }

    /// <summary>
    /// Insert a table at the caret (after the block the caret sits in, else at the end), routing
    /// through the undo/redo command bus so the insert is reversible. Re-renders the surface.
    /// </summary>
    public void InsertTable(int rows, int columns)
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertBlockCommand(index, ModelTable.Create(rows, columns)));
    }

    /// <summary>Insert a blank row below the caret's row in the table containing the caret.</summary>
    public void InsertTableRow() => MutateCaretTable((index, rowIndex, _) =>
        new InsertTableRowCommand(index, rowIndex + 1));

    /// <summary>Delete the caret's row from the table containing the caret (no-op on the last row).</summary>
    public void DeleteTableRow() => MutateCaretTable((index, rowIndex, _) =>
        new DeleteTableRowCommand(index, rowIndex));

    /// <summary>Insert a blank column to the right of the caret's column in the table containing the caret.</summary>
    public void InsertTableColumn() => MutateCaretTable((index, _, columnIndex) =>
        new InsertTableColumnCommand(index, columnIndex + 1));

    /// <summary>Delete the caret's column from the table containing the caret (no-op on the last column).</summary>
    public void DeleteTableColumn() => MutateCaretTable((index, _, columnIndex) =>
        new DeleteTableColumnCommand(index, columnIndex));

    /// <summary>
    /// Set (or clear, when <paramref name="colorHex"/> is null/empty) the background shading of the
    /// table cell containing the caret. Commits pending edits, mutates the model cell directly, and
    /// re-renders so the fill shows immediately and round-trips through save. No-op outside a table.
    /// </summary>
    public void SetCaretCellShading(string? colorHex)
    {
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        if (columnIndex < 0 || columnIndex >= cells.Count)
            return;
        cells[columnIndex].ShadingColorHex = string.IsNullOrEmpty(colorHex) ? null : colorHex;
        Render();
    }

    /// <summary>
    /// Resize the currently selected inline image to <paramref name="widthPt"/> points wide, scaling
    /// the height to preserve aspect ratio. Routes through the bus (undoable). No-op without a selection.
    /// </summary>
    public void SetSelectedImageSize(double widthPt)
    {
        if (widthPt <= 0)
            return;
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null)
            return;
        var aspect = image.WidthPt > 0 ? image.HeightPt / image.WidthPt : 1;
        _commands.Execute(new SetImageSizeCommand(blockIndex, runIndex, widthPt, widthPt * aspect));
    }

    /// <summary>The inline image targeted by the current selection/caret, or null if none is selected.</summary>
    public InlineImage? SelectedImage() => SelectedImageLocation().Image;

    /// <summary>
    /// Toggle a box border on every paragraph touched by the current selection/caret. If any selected
    /// paragraph lacks a border, all get one (<paramref name="colorHex"/>/<paramref name="widthPt"/>);
    /// otherwise the border is cleared. Re-renders so it round-trips through the model on the next commit.
    /// </summary>
    public void ToggleParagraphBorder(string colorHex = "#000000", double widthPt = 0.5) =>
        MutateSelectedParagraphs(paragraphs =>
        {
            var enable = paragraphs.Any(p => p.BorderThickness.Top <= 0);
            foreach (var p in paragraphs)
            {
                if (enable)
                {
                    p.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                    p.BorderThickness = new Thickness(widthPt * PxPerPoint);
                    p.Padding = new Thickness(2);
                }
                else
                {
                    p.BorderBrush = null;
                    p.BorderThickness = new Thickness(0);
                    p.Padding = new Thickness(0);
                }
            }
        });

    /// <summary>
    /// Toggle paragraph shading over the selection. A null/empty <paramref name="colorHex"/> clears
    /// shading; otherwise each touched paragraph is filled with that colour. Re-renders the surface.
    /// </summary>
    public void ToggleParagraphShading(string? colorHex) =>
        MutateSelectedParagraphs(paragraphs =>
        {
            var clear = string.IsNullOrEmpty(colorHex)
                || paragraphs.All(p => p.Background is SolidColorBrush b && ToHex(b.Color) == colorHex);
            foreach (var p in paragraphs)
                p.Background = clear
                    ? null
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex!));
        });

    /// <summary>
    /// Set the line spacing (a multiplier on the default font size, e.g. 1.0 single / 1.5 / 2.0 double)
    /// on every paragraph spanned by the selection. Routes through the undo/redo bus so it is reversible.
    /// </summary>
    public void SetLineSpacing(double multiplier) =>
        FormatSelectedModelParagraphs(f => f with { LineSpacing = multiplier });

    /// <summary>
    /// Toggle "Add/Remove Space Before Paragraph" over the selection: if any spanned paragraph has no
    /// space before, all get <paramref name="spacePt"/> points; otherwise space-before is cleared.
    /// Reversible via the bus.
    /// </summary>
    public void ToggleSpaceBefore(double spacePt = 12)
    {
        var enable = SelectedModelParagraphs().Any(p => p.Formatting.SpaceBeforePt <= 0);
        FormatSelectedModelParagraphs(f => f with { SpaceBeforePt = enable ? spacePt : 0 });
    }

    /// <summary>
    /// Toggle "Add/Remove Space After Paragraph" over the selection: if any spanned paragraph has no
    /// space after, all get <paramref name="spacePt"/> points; otherwise space-after is cleared.
    /// Reversible via the bus.
    /// </summary>
    public void ToggleSpaceAfter(double spacePt = 12)
    {
        var enable = SelectedModelParagraphs().Any(p => p.Formatting.SpaceAfterPt <= 0);
        FormatSelectedModelParagraphs(f => f with { SpaceAfterPt = enable ? spacePt : 0 });
    }

    /// <summary>
    /// Apply a named paragraph style (its <paramref name="styleId"/>) to every model paragraph spanned
    /// by the selection, routing one reversible <see cref="SetParagraphStyleCommand"/> per paragraph
    /// through the undo/redo bus. The view re-renders so the style's run/paragraph formatting resolves.
    /// A null <paramref name="styleId"/> (or one not in the catalog) clears the style. No-op if unknown.
    /// </summary>
    public void SetParagraphStyle(string? styleId)
    {
        if (styleId is { Length: > 0 } && !_model.Styles.ContainsKey(styleId))
            return;
        Focus();
        CommitToModel();
        foreach (var index in SelectedModelParagraphIndices())
        {
            if (_model.Blocks[index] is ModelParagraph)
                _commands.Execute(new SetParagraphStyleCommand(index, styleId));
        }
    }

    // Commit pending edits, then apply a paragraph-formatting transform to every model paragraph spanned
    // by the selection, one reversible SetParagraphFormattingCommand per paragraph on the undo/redo bus.
    private void FormatSelectedModelParagraphs(Func<ParagraphFormatting, ParagraphFormatting> transform)
    {
        Focus();
        CommitToModel();
        var indices = SelectedModelParagraphIndices();
        foreach (var index in indices)
        {
            if (_model.Blocks[index] is ModelParagraph paragraph)
                _commands.Execute(new SetParagraphFormattingCommand(index, transform(paragraph.Formatting)));
        }
    }

    // The model paragraphs spanned by the current selection/caret (post-commit snapshot, for state checks).
    private IReadOnlyList<ModelParagraph> SelectedModelParagraphs()
    {
        CommitToModel();
        return SelectedModelParagraphIndices()
            .Select(i => _model.Blocks[i])
            .OfType<ModelParagraph>()
            .ToList();
    }

    // Map the WPF paragraphs spanned by the selection to their model block indices. The model is built
    // by flattening lists into their item paragraphs in document order (see CommitToModel), so a WPF
    // paragraph's model index equals the count of "leaf" blocks (paragraphs/tables) preceding it.
    private IReadOnlyList<int> SelectedModelParagraphIndices()
    {
        var start = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        var end = Selection.End.Paragraph ?? start;
        if (start is null)
            return [];

        // Number every leaf block in document order, recording the model index of each WPF paragraph.
        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);

        if (!indexOf.TryGetValue(start, out var startIndex))
            return [];
        if (end is null || !indexOf.TryGetValue(end, out var endIndex))
            endIndex = startIndex;

        var result = new List<int>();
        for (var i = Math.Min(startIndex, endIndex); i <= Math.Max(startIndex, endIndex); i++)
            result.Add(i);
        return result;
    }

    // Walk a FlowDocument block in the same order CommitToModel reads it, assigning each top-level
    // paragraph/table a model index and recording paragraph identities so the selection can be mapped.
    private static void NumberLeafBlocks(System.Windows.Documents.Block block, IDictionary<WpfParagraph, int> indexOf, ref int modelIndex)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
                indexOf[paragraph] = modelIndex++;
                break;
            case WpfList list:
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks)
                        NumberLeafBlocks(itemBlock, indexOf, ref modelIndex);
                break;
            case WpfTable:
                modelIndex++;
                break;
        }
    }

    // Apply a mutation to the WPF paragraphs spanned by the selection (or the caret's paragraph),
    // then commit + re-render so the change lands in the model and round-trips on save.
    private void MutateSelectedParagraphs(Action<IReadOnlyList<WpfParagraph>> mutate)
    {
        Focus();
        var start = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        var end = Selection.End.Paragraph ?? start;
        if (start is null)
            return;

        var paragraphs = new List<WpfParagraph>();
        for (WpfParagraph? p = start; p is not null; p = p.NextBlock as WpfParagraph)
        {
            paragraphs.Add(p);
            if (ReferenceEquals(p, end))
                break;
        }
        if (paragraphs.Count == 0)
            return;

        mutate(paragraphs);
        CommitToModel();
        Render();
    }

    // Commit pending edits, locate the caret's table + cell, build a command for it, run it through the bus.
    private void MutateCaretTable(Func<int, int, int, IDocumentCommand> build)
    {
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0)
            return;
        _commands.Execute(build(blockIndex, rowIndex, columnIndex));
    }

    // Locate the model block/row/column of the table containing the caret; blockIndex is -1 if not in a table.
    private (int BlockIndex, int RowIndex, int ColumnIndex) CaretTableLocation()
    {
        // Walk up from the caret to the hosting WPF cell/row/table.
        TextElement? element = CaretPosition?.Parent as TextElement;
        WpfTableCell? cell = null;
        while (element is not null)
        {
            if (element is WpfTableCell c)
            {
                cell = c;
                break;
            }
            element = element.Parent as TextElement;
        }
        if (cell?.Parent is not WpfTableRow wpfRow || wpfRow.Parent is not TableRowGroup group
            || group.Parent is not WpfTable wpfTable)
            return (-1, -1, -1);

        var blockIndex = new List<System.Windows.Documents.Block>(Document.Blocks).IndexOf(wpfTable);
        var rowIndex = new List<WpfTableRow>(group.Rows).IndexOf(wpfRow);
        var columnIndex = new List<WpfTableCell>(wpfRow.Cells).IndexOf(cell);
        return (blockIndex, rowIndex, columnIndex);
    }

    // Locate the model paragraph/run index of the inline image under the selection, plus the image itself.
    private (int BlockIndex, int RunIndex, InlineImage? Image) SelectedImageLocation()
    {
        // An InlineUIContainer hosting our tagged Image is the selected picture; find it around the caret.
        var image = ImageInElement(CaretPosition?.Parent as TextElement)
            ?? ImageInElement(Selection.Start.Parent as TextElement)
            ?? ImageInElement(Selection.End.Parent as TextElement);
        if (image is null)
            return (-1, -1, null);

        // Match it back to a top-level model paragraph + run by identity (images embedded in tables are skipped).
        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (ReferenceEquals(paragraph.Runs[r].Image, image))
                    return (b, r, image);
            }
        }
        return (-1, -1, null);
    }

    private static InlineImage? ImageInElement(TextElement? element)
    {
        while (element is not null)
        {
            if (element is InlineUIContainer { Child: Image { Tag: InlineImage modelImage } })
                return modelImage;
            element = element.Parent as TextElement;
        }
        return null;
    }

    // The index of the model block containing the caret, or the last block (-1 when the body is empty).
    private int CaretBlockIndex()
    {
        TextElement? caretBlock = CaretPosition?.Paragraph
            ?? CaretPosition?.Parent as TextElement;
        // Walk up to the block hosted directly by the FlowDocument (its parent is not a TextElement).
        while (caretBlock?.Parent is TextElement parent)
            caretBlock = parent;

        var viewIndex = caretBlock is System.Windows.Documents.Block b
            ? new List<System.Windows.Documents.Block>(Document.Blocks).IndexOf(b)
            : -1;
        return viewIndex >= 0 ? viewIndex : _model.Blocks.Count - 1;
    }

    private void Render()
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0) };
        flow.FontFamily = new FontFamily(_model.DefaultRun.FontFamily ?? "Calibri");
        flow.FontSize = (_model.DefaultRun.FontSizePt ?? 11) * PxPerPoint;

        // Coalesce consecutive list paragraphs of the same kind into one WPF List so they render with
        // shared bullet/number decoration; everything else maps one-to-one via BuildBlock.
        var blocks = _model.Blocks;
        var i = 0;
        while (i < blocks.Count)
        {
            if (blocks[i] is ModelParagraph { Formatting.ListKind: not ListKind.None } first)
            {
                var kind = first.Formatting.ListKind;
                var list = new WpfList { MarkerStyle = ToMarkerStyle(kind) };
                while (i < blocks.Count
                    && blocks[i] is ModelParagraph { Formatting.ListKind: var k } listParagraph
                    && k == kind)
                {
                    list.ListItems.Add(new WpfListItem(BuildParagraph(listParagraph, _model)));
                    i++;
                }
                flow.Blocks.Add(list);
            }
            else
            {
                flow.Blocks.Add(BuildBlock(blocks[i], _model));
                i++;
            }
        }

        Document = flow;
    }

    private static TextMarkerStyle ToMarkerStyle(ListKind kind) =>
        kind == ListKind.Number ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._model;
    }

    /// <summary>
    /// Side-band paragraph data carried on a WPF <see cref="WpfParagraph.Tag"/> so it survives an
    /// edit/commit cycle even though the FlowDocument paragraph has no native slot for it. Holds the
    /// model's tab stops (not representable in WPF) and the paragraph's bookmark name (an invisible
    /// marker). Either field may be empty/null; the Tag is only stamped when at least one is set.
    /// </summary>
    private sealed record ParagraphTag(IReadOnlyList<TabStop> TabStops, string? BookmarkName);

    /// <summary>Read the edited FlowDocument back into the model (paragraphs + tables).</summary>
    public void CommitToModel()
    {
        _model.Blocks.Clear();
        foreach (var block in Document.Blocks)
        {
            switch (block)
            {
                case WpfList wpfList:
                    ReadList(_model.Blocks, wpfList, _model);
                    break;
                case WpfParagraph wpfParagraph:
                    _model.Blocks.Add(ReadParagraph(wpfParagraph, _model));
                    break;
                case WpfTable wpfTable:
                    _model.Blocks.Add(ReadTable(wpfTable, _model));
                    break;
            }
        }

        if (_model.Blocks.Count == 0)
            _model.Blocks.Add(new ModelParagraph());
    }

    private static ModelParagraph ReadParagraph(WpfParagraph wpfParagraph, TextDocument document)
    {
        var modelParagraph = new ModelParagraph
        {
            Formatting = ReadParagraphFormatting(wpfParagraph, document),
            // The bookmark name (an invisible marker) is preserved across edits via the paragraph Tag.
            BookmarkName = wpfParagraph.Tag is ParagraphTag { BookmarkName: { Length: > 0 } name } ? name : null
        };
        foreach (var inline in wpfParagraph.Inlines)
            ReadInline(modelParagraph, inline, hyperlinkUrl: null, hyperlinkAnchor: null);
        return modelParagraph;
    }

    // Flatten a WPF List into model paragraphs, stamping each with the list's kind and the nesting
    // depth as ListLevel. ListItems may hold nested Lists (deeper levels) alongside paragraphs.
    private static void ReadList(IList<ModelBlock> target, WpfList wpfList, TextDocument document, int level = 0)
    {
        var kind = FromMarkerStyle(wpfList.MarkerStyle);
        foreach (var item in wpfList.ListItems)
        {
            foreach (var itemBlock in item.Blocks)
            {
                switch (itemBlock)
                {
                    case WpfList nested:
                        ReadList(target, nested, document, level + 1);
                        break;
                    case WpfParagraph paragraph:
                        var model = ReadParagraph(paragraph, document);
                        model.Formatting = model.Formatting with { ListKind = kind, ListLevel = level };
                        target.Add(model);
                        break;
                    case WpfTable table:
                        target.Add(ReadTable(table, document));
                        break;
                }
            }
        }
    }

    private static ListKind FromMarkerStyle(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman => ListKind.Number,
        TextMarkerStyle.None => ListKind.Bullet,
        _ => ListKind.Bullet
    };

    // Maps one FlowDocument inline to model run(s). A Hyperlink is a Span of inlines, so we recurse
    // into it carrying its target. An external link carries a NavigateUri (-> HyperlinkUrl); an
    // internal link carries its bookmark name on the Hyperlink's Tag (-> HyperlinkAnchor).
    private static void ReadInline(ModelParagraph modelParagraph, Inline inline, string? hyperlinkUrl, string? hyperlinkAnchor)
    {
        switch (inline)
        {
            case WpfHyperlink link:
                var anchor = link.Tag as string ?? hyperlinkAnchor;
                // An internal link has no NavigateUri; only treat NavigateUri as an external URL.
                var url = anchor is { Length: > 0 } ? hyperlinkUrl : link.NavigateUri?.ToString() ?? hyperlinkUrl;
                foreach (var child in link.Inlines)
                    ReadInline(modelParagraph, child, url, anchor);
                break;
            case InlineUIContainer { Child: Image { Tag: InlineImage modelImage } }:
                modelParagraph.Runs.Add(new ModelRun(string.Empty) { Image = modelImage, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor });
                break;
            case WpfRun { Tag: FootnoteMarker marker }:
                modelParagraph.Runs.Add(ModelRun.FootnoteReference(marker.FootnoteId));
                break;
            case WpfRun { Tag: CommentMarker { IsReference: true } reference }:
                // The textless comment anchor: round-trips as a comment-reference run.
                modelParagraph.Runs.Add(ModelRun.CommentReference(reference.CommentId));
                break;
            case WpfRun { Tag: CommentMarker { IsReference: false } covered } commentedRun when commentedRun.Text.Length > 0:
                // A commented text run: recover its formatting but drop the injected review highlight
                // (it is view-only chrome) and carry the comment id on the model run.
                modelParagraph.Runs.Add(new ModelRun(commentedRun.Text, ReadRunFormatting(commentedRun) with { HighlightColorHex = null })
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    CommentId = covered.CommentId
                });
                break;
            case WpfRun { Tag: RevisionMarker marker } revisedRun when revisedRun.Text.Length > 0:
                // A tracked-change run: recover its formatting but strip the injected revision colour and
                // the kind's decoration (view-only chrome), carrying the revision mark back onto the model.
                modelParagraph.Runs.Add(new ModelRun(revisedRun.Text, StripRevisionChrome(ReadRunFormatting(revisedRun), marker.Kind))
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    Revision = marker.Kind,
                    RevisionAuthor = marker.Author,
                    RevisionDateXml = marker.DateXml
                });
                break;
            case WpfRun run when run.Text.Length > 0:
                modelParagraph.Runs.Add(new ModelRun(run.Text, ReadRunFormatting(run)) { HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor });
                break;
        }
    }

    private static ModelTable ReadTable(WpfTable wpfTable, TextDocument document)
    {
        var table = new ModelTable();

        // Preserve column widths (column-level in WPF) so the docx tblGrid round-trips through edit.
        foreach (var column in wpfTable.Columns)
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0)
                table.ColumnWidthsPt.Add(column.Width.Value / PxPerPoint);
            else
                table.ColumnWidthsPt.Add(0);
        }
        // Drop the grid entirely if no column carried an explicit width (keeps plain tables unchanged).
        if (table.ColumnWidthsPt.All(w => w <= 0))
            table.ColumnWidthsPt.Clear();

        foreach (var rowGroup in wpfTable.RowGroups)
        {
            foreach (var wpfRow in rowGroup.Rows)
            {
                var row = new ModelTableRow();
                foreach (var wpfCell in wpfRow.Cells)
                {
                    var cell = new ModelTableCell
                    {
                        ShadingColorHex = wpfCell.Background is SolidColorBrush shading ? ToHex(shading.Color) : null
                    };
                    foreach (var cellBlock in wpfCell.Blocks)
                    {
                        if (cellBlock is WpfParagraph cellParagraph)
                            cell.Paragraphs.Add(ReadParagraph(cellParagraph, document));
                    }
                    if (cell.Paragraphs.Count == 0)
                        cell.Paragraphs.Add(new ModelParagraph());
                    row.Cells.Add(cell);
                }
                table.Rows.Add(row);
            }
        }
        return table;
    }

    // --- model -> view ---

    private static System.Windows.Documents.Block BuildBlock(ModelBlock block, TextDocument document) => block switch
    {
        ModelTable table => BuildTable(table, document),
        ModelParagraph paragraph => BuildParagraph(paragraph, document),
        _ => BuildParagraph(new ModelParagraph(), document)
    };

    private static WpfTable BuildTable(ModelTable table, TextDocument document)
    {
        var wpf = new WpfTable();
        var columns = table.ColumnCount;
        for (var c = 0; c < columns; c++)
        {
            var column = new TableColumn();
            // WPF FlowDocument tables only support column-level (not per-cell) widths, so the model's
            // column widths drive TableColumn.Width here; per-cell widths are preserved in the model
            // for docx round-trip but not individually rendered.
            if (c < table.ColumnWidthsPt.Count && table.ColumnWidthsPt[c] > 0)
                column.Width = new GridLength(table.ColumnWidthsPt[c] * PxPerPoint);
            wpf.Columns.Add(column);
        }

        var borderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        if (table.Formatting.Borders)
        {
            wpf.BorderBrush = borderBrush;
            wpf.BorderThickness = new Thickness(0.5);
        }

        var group = new TableRowGroup();
        foreach (var modelRow in table.Rows)
        {
            var wpfRow = new WpfTableRow();
            foreach (var modelCell in modelRow.Cells)
            {
                var wpfCell = new WpfTableCell
                {
                    Padding = new Thickness(4, 2, 4, 2)
                };
                if (table.Formatting.Borders)
                {
                    wpfCell.BorderBrush = borderBrush;
                    wpfCell.BorderThickness = new Thickness(0.5);
                }
                if (modelCell.ShadingColorHex is { Length: > 0 } cellShading)
                    wpfCell.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cellShading));
                if (modelCell.Paragraphs.Count == 0)
                {
                    wpfCell.Blocks.Add(BuildParagraph(new ModelParagraph(), document));
                }
                else
                {
                    foreach (var cellParagraph in modelCell.Paragraphs)
                        wpfCell.Blocks.Add(BuildParagraph(cellParagraph, document));
                }
                wpfRow.Cells.Add(wpfCell);
            }
            group.Rows.Add(wpfRow);
        }
        wpf.RowGroups.Add(group);
        return wpf;
    }

    private static WpfParagraph BuildParagraph(ModelParagraph paragraph, TextDocument document)
    {
        var paraFmt = Resolve(paragraph, document);
        var wpf = new WpfParagraph
        {
            TextAlignment = ToWpfAlignment(paraFmt.Alignment),
            Margin = new Thickness(
                paraFmt.IndentLeftPt * PxPerPoint,
                paraFmt.SpaceBeforePt * PxPerPoint,
                paraFmt.IndentRightPt * PxPerPoint,
                paraFmt.SpaceAfterPt * PxPerPoint),
            TextIndent = paraFmt.FirstLineIndentPt * PxPerPoint,
            LineHeight = paraFmt.LineSpacing > 0
                ? paraFmt.LineSpacing * (document.DefaultRun.FontSizePt ?? 11) * PxPerPoint
                : double.NaN
        };

        if (paraFmt.Border is { } border && TryParseColor(border.ColorHex, out var borderColor))
        {
            wpf.BorderBrush = new SolidColorBrush(borderColor);
            wpf.BorderThickness = new Thickness(border.WidthPt * PxPerPoint);
            wpf.Padding = new Thickness(2);
        }
        if (TryParseColor(paraFmt.ShadingColorHex, out var shading))
            wpf.Background = new SolidColorBrush(shading);

        // WPF's FlowDocument Paragraph has no tab-stop API, so tab stops cannot be rendered with
        // custom positions/alignments (default tab rendering applies visually). A bookmark name is an
        // invisible marker with no FlowDocument representation either. To avoid losing either on an
        // edit/commit cycle, we carry both on the paragraph's Tag (a ParagraphTag) and read them back
        // verbatim on commit; the docx round-trip remains exact.
        if (paraFmt.TabStops.Count > 0 || paragraph.BookmarkName is { Length: > 0 })
            wpf.Tag = new ParagraphTag(paraFmt.TabStops, paragraph.BookmarkName);

        foreach (var run in paragraph.Runs)
            wpf.Inlines.Add(BuildRun(run, paragraph, document));

        return wpf;
    }

    private static Inline BuildRun(ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        if (run.Image is { } image)
            return BuildImageRun(image);

        if (run.FootnoteId is { } footnoteId)
            return BuildFootnoteReference(footnoteId, document);

        // The textless comment anchor round-trips as an empty, tagged run carrying its reference flag.
        if (run is { IsCommentReference: true, CommentId: { } refId })
            return new WpfRun(string.Empty) { Tag = new CommentMarker(refId, IsReference: true) };

        var fmt = Resolve(run, paragraph, document);
        var wpf = new WpfRun(run.Text)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        var fontSizePx = (fmt.FontSizePt ?? DefaultFontSizePt) * PxPerPoint;
        // Superscript/subscript: nudge the baseline and shrink the glyphs. Set FontSize explicitly
        // (even at the default) so ReadRunFormatting can recover the original point size by undoing
        // the SuperSubScale factor; plain runs leave FontSize at its inherited default.
        if (fmt.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
        {
            wpf.BaselineAlignment = fmt.VerticalAlign == VerticalAlign.Superscript
                ? BaselineAlignment.Superscript
                : BaselineAlignment.Subscript;
            wpf.FontSize = fontSizePx * SuperSubScale;
        }
        else if (fmt.FontSizePt is { } size)
        {
            wpf.FontSize = size * PxPerPoint;
        }
        if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);
        if (TryParseColor(fmt.HighlightColorHex, out var highlight))
            wpf.Background = new SolidColorBrush(highlight);

        // Small caps / all caps. AllCaps wins visually but both flags are preserved on commit by
        // mapping each to a distinct FontCapitals value that ReadRunFormatting decodes back.
        if (fmt.AllCaps)
            Typography.SetCapitals(wpf, FontCapitals.AllSmallCaps);
        else if (fmt.SmallCaps)
            Typography.SetCapitals(wpf, FontCapitals.SmallCaps);

        var decorations = new TextDecorationCollection();
        if (fmt.Underline)
            decorations.Add(TextDecorations.Underline);
        if (fmt.Strikethrough)
            decorations.Add(TextDecorations.Strikethrough);

        // A tracked-change run is coloured in the revision colour and decorated: insertions get an
        // underline, deletions get a strikethrough. A RevisionMarker tag carries the kind/author/date
        // so the mark round-trips on commit (see ReadInline). The mark wins over the run's own colour.
        if (run.Revision != RevisionKind.None)
        {
            wpf.Foreground = new SolidColorBrush(RevisionColor);
            decorations.Add(run.Revision == RevisionKind.Deleted
                ? TextDecorations.Strikethrough[0]
                : TextDecorations.Underline[0]);
            wpf.Tag = new RevisionMarker(run.Revision, run.RevisionAuthor, run.RevisionDateXml);
        }

        if (decorations.Count > 0)
            wpf.TextDecorations = decorations;

        // A commented run gets a subtle highlight + a tooltip surfacing the comment author and text,
        // and a CommentMarker tag so the id round-trips on commit (see ReadInline).
        if (run.CommentId is { } commentId)
            ApplyCommentMarker(wpf, commentId, document);

        if (run.HyperlinkUrl is { Length: > 0 } url)
            return BuildHyperlink(wpf, url);
        if (run.HyperlinkAnchor is { Length: > 0 } anchor)
            return BuildInternalHyperlink(wpf, anchor);

        return wpf;
    }

    /// <summary>Subtle highlight used to mark a commented text range (a pale review yellow).</summary>
    private static readonly Color CommentHighlight = Color.FromRgb(0xFF, 0xF4, 0xCE);

    /// <summary>The fixed colour tracked changes are rendered in (a Word-like revision maroon/red).</summary>
    private static readonly Color RevisionColor = Color.FromRgb(0xC0, 0x00, 0x40);

    /// <summary>
    /// Carried on a tracked-change WPF run's Tag so CommitToModel can round-trip its revision kind,
    /// author and date. Mirrors how CommentMarker/FootnoteMarker preserve their marks across an edit.
    /// </summary>
    private sealed record RevisionMarker(RevisionKind Kind, string? Author, string? DateXml);

    /// <summary>
    /// Marks a WPF run as covered by the comment with id <paramref name="commentId"/>: a subtle
    /// background highlight (only when the run has no explicit highlight of its own) plus a tooltip
    /// showing the comment author and text, and a <see cref="CommentMarker"/> tag so the id survives a
    /// commit/round-trip.
    /// </summary>
    private static void ApplyCommentMarker(WpfRun wpf, int commentId, TextDocument document)
    {
        wpf.Tag = new CommentMarker(commentId, IsReference: false);
        if (wpf.Background is null)
            wpf.Background = new SolidColorBrush(CommentHighlight);
        if (document.Comments.TryGetValue(commentId, out var comment))
        {
            var author = comment.Author.Length > 0 ? comment.Author : "Comment";
            var body = comment.PlainText;
            wpf.ToolTip = body.Length > 0 ? $"{author}: {body}" : author;
        }
    }

    /// <summary>
    /// Carried on a commented WPF run's Tag so CommitToModel can round-trip its comment id. When
    /// <see cref="IsReference"/> is true the run is the textless anchor (the w:commentReference);
    /// otherwise it is a covered text run within the comment range.
    /// </summary>
    private sealed record CommentMarker(int CommentId, bool IsReference);

    // Wraps a styled run in a WPF Hyperlink that targets an internal bookmark. The bookmark name is
    // stored on the link's Tag (not NavigateUri, which is reserved for external URLs) so it reads back
    // on commit; navigating scrolls the bookmarked paragraph into view (best-effort).
    private static Inline BuildInternalHyperlink(WpfRun content, string anchor)
    {
        var link = new WpfHyperlink(content);
        StyleInternalLink(link, anchor);
        return link;
    }

    private static void StyleInternalLink(WpfHyperlink link, string anchor)
    {
        link.Tag = anchor;
        link.ToolTip = "Go to bookmark: " + anchor;
        link.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x63, 0xC1));
        link.Click += OnInternalLinkClick;
    }

    // Scroll the paragraph carrying the linked bookmark into view (best-effort). Matches on the
    // model BookmarkName preserved via each WPF paragraph's ParagraphTag, searching the FlowDocument
    // that hosts the clicked link.
    private static void OnInternalLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfHyperlink { Tag: string anchor } link || anchor.Length == 0)
            return;
        var flow = FindFlowDocument(link);
        var target = flow?.Blocks.OfType<WpfParagraph>()
            .FirstOrDefault(p => p.Tag is ParagraphTag { BookmarkName: { } name } && name == anchor);
        target?.BringIntoView();
    }

    // Walk a TextElement's logical parent chain up to the hosting FlowDocument, if any.
    private static FlowDocument? FindFlowDocument(TextElement element)
    {
        DependencyObject? node = element;
        while (node is not null)
        {
            if (node is FlowDocument flow)
                return flow;
            node = node is TextElement te ? te.Parent : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    // Wraps a styled run in a WPF Hyperlink (blue + underlined, with NavigateUri) so the link reads
    // back on commit and can be opened. Falls back to a plain run if the URL is not a valid Uri.
    private static Inline BuildHyperlink(WpfRun content, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return content;

        var link = new WpfHyperlink(content) { NavigateUri = uri };
        StyleLink(link, url);
        return link;
    }

    // Opens the link target in the default handler. Only http/https are launched (safe + simple).
    private static void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        var uri = e.Uri;
        if (uri is null || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Ignore launch failures (no handler, blocked, etc.) — opening a link must never crash the editor.
        }
    }

    /// <summary>
    /// Renders a footnote reference as a small superscript marker showing the footnote number, tagged
    /// with a <see cref="FootnoteMarker"/> so <see cref="ReadInline"/> can recover the id on commit.
    /// A tooltip surfaces the footnote text when the document carries it.
    /// </summary>
    private static WpfRun BuildFootnoteReference(int footnoteId, TextDocument document)
    {
        var marker = new WpfRun(footnoteId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            BaselineAlignment = BaselineAlignment.Superscript,
            FontSize = (document.DefaultRun.FontSizePt ?? DefaultFontSizePt) * PxPerPoint * SuperSubScale,
            Tag = new FootnoteMarker(footnoteId)
        };
        if (document.Footnotes.TryGetValue(footnoteId, out var footnote) && footnote.PlainText is { Length: > 0 } text)
            marker.ToolTip = text;
        return marker;
    }

    /// <summary>Carried on a footnote-marker WPF run's Tag so CommitToModel can round-trip its id.</summary>
    private sealed record FootnoteMarker(int FootnoteId);

    /// <summary>Renders an inline image as an InlineUIContainer hosting a WPF Image (PNG-decoded).</summary>
    private static InlineUIContainer BuildImageRun(InlineImage image)
    {
        var element = new Image
        {
            Source = DecodePng(image.PngBytes),
            Width = image.WidthPt * PxPerPoint,
            Height = image.HeightPt * PxPerPoint,
            Stretch = Stretch.Fill,
            Tag = image // carries the model image so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    private static BitmapImage DecodePng(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Inserts an inline image at the caret. Width/height in points; preserved on save.</summary>
    public void InsertImage(InlineImage image)
    {
        CommitToModel();
        var container = BuildImageRun(image);
        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        if (caret.Paragraph is { } paragraph)
            paragraph.Inlines.Add(container);
        else if (Document.Blocks.LastOrDefault() is WpfParagraph last)
            last.Inlines.Add(container);
        else
        {
            var p = new WpfParagraph(container);
            Document.Blocks.Add(p);
        }
        CommitToModel();
        Render();
    }

    /// <summary>
    /// Insert plain text at the caret through the RichTextBox's own edit path, so it joins the run the
    /// caret sits in (inheriting its formatting), replaces any active selection, and is captured by the
    /// existing undo stack. A no-op for null/empty text. Used by Insert &gt; Symbol and Date &amp; Time,
    /// which just drop ordinary text runs at the caret — no model or docx changes.
    /// </summary>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Focus();
        var selection = Selection;
        if (!selection.IsEmpty)
        {
            // Typing over a selection replaces it: clear it first, then insert at the resulting caret.
            selection.Text = string.Empty;
        }

        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        caret.InsertTextInRun(text);
        // Advance the caret past the inserted text so subsequent typing continues from there.
        CaretPosition = caret.GetPositionAtOffset(text.Length) ?? caret;
        CommitToModel();
        Render();
    }

    /// <summary>
    /// Inserts a footnote at the caret: allocates the next footnote id, stores <paramref name="text"/>
    /// as the footnote's content in the model, and drops a superscript reference marker at the caret.
    /// Re-renders so the marker round-trips through the model on the next commit.
    /// </summary>
    public void InsertFootnote(string text)
    {
        CommitToModel();

        var id = _model.NextFootnoteId();
        var footnote = new Footnote(id);
        footnote.Content.Add(new ModelParagraph(text));
        _model.Footnotes[id] = footnote;

        var marker = BuildFootnoteReference(id, _model);
        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
        if (paragraph is null)
        {
            paragraph = new WpfParagraph();
            Document.Blocks.Add(paragraph);
        }
        paragraph.Inlines.Add(marker);

        CommitToModel();
        Render();
    }

    /// <summary>
    /// Adds a review comment over the current selection: allocates the next comment id, marks the
    /// selected run span with it (a w:commentRangeStart/End pair on save), appends a reference anchor,
    /// and stores the comment (author/initials/text) in the model. With an empty selection the comment
    /// covers the caret's whole paragraph. Re-renders so the highlight + tooltip appear and the markers
    /// round-trip on the next commit/save.
    /// </summary>
    public void InsertComment(string text, string author, string initials)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        Focus();

        // Capture the selection geometry (start paragraph + char offsets within it) before committing,
        // since committing rebuilds the model. We support a selection inside one paragraph (the common
        // case); a wider or empty selection falls back to covering the start paragraph in full.
        var startParagraph = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        if (startParagraph is null)
            return;
        var sameParagraph = ReferenceEquals(Selection.Start.Paragraph, Selection.End.Paragraph);
        var startOffset = OffsetInParagraph(startParagraph, Selection.Start);
        var endOffset = sameParagraph ? OffsetInParagraph(startParagraph, Selection.End) : int.MaxValue;
        if (Selection.IsEmpty || !sameParagraph)
        {
            startOffset = 0;
            endOffset = int.MaxValue;
        }

        // Resolve the start paragraph to its model block index, then commit so the model matches the view.
        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);
        if (!indexOf.TryGetValue(startParagraph, out var paragraphIndex))
            return;

        CommitToModel();
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph modelParagraph)
            return;

        var id = _model.NextCommentId();
        if (!MarkCommentRange(modelParagraph, startOffset, endOffset, id))
            return; // nothing textual to anchor the comment to

        _model.Comments[id] = new Comment(id)
        {
            Author = author,
            Initials = initials,
            // W3CDTF (UTC, second precision) — matches what the docx writer expects for w:date.
            DateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
        };
        _model.Comments[id].Content.Add(new ModelParagraph(text));

        Render();
    }

    /// <summary>
    /// When true, the editor is in Track Changes mode. Live keystroke-level tracking is not attempted
    /// (it is brittle in a RichTextBox); the flag is a model/UI state that the ribbon toggle reflects and
    /// that gates <see cref="MarkSelectionAsRevision"/> (used to mark the selection as an insertion or
    /// deletion). Accept-All / Reject-All operate regardless of this flag.
    /// </summary>
    public bool TrackChangesEnabled { get; set; }

    /// <summary>True when the committed model carries any tracked change.</summary>
    public bool HasRevisions()
    {
        CommitToModel();
        return TrackChanges.HasRevisions(_model);
    }

    /// <summary>
    /// Marks the current selection as a tracked change of <paramref name="kind"/> (insertion or
    /// deletion) by the given author/date, splitting runs at the selection boundaries. With an empty
    /// selection the caret's whole paragraph is marked. Re-renders so the revision colour/decoration
    /// appears and the marks round-trip on the next commit/save. A no-op for <see cref="RevisionKind.None"/>.
    /// </summary>
    public void MarkSelectionAsRevision(RevisionKind kind, string author, string? dateXml)
    {
        if (kind == RevisionKind.None)
            return;

        Focus();

        var startParagraph = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        if (startParagraph is null)
            return;
        var sameParagraph = ReferenceEquals(Selection.Start.Paragraph, Selection.End.Paragraph);
        var startOffset = OffsetInParagraph(startParagraph, Selection.Start);
        var endOffset = sameParagraph ? OffsetInParagraph(startParagraph, Selection.End) : int.MaxValue;
        if (Selection.IsEmpty || !sameParagraph)
        {
            startOffset = 0;
            endOffset = int.MaxValue;
        }

        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);
        if (!indexOf.TryGetValue(startParagraph, out var paragraphIndex))
            return;

        CommitToModel();
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph modelParagraph)
            return;

        MarkRevisionRange(modelParagraph, startOffset, endOffset, kind, author, dateXml);
        Render();
    }

    /// <summary>
    /// Accept every tracked change in the document: insertions become ordinary text, deletions are
    /// removed. Commits pending edits first, then re-renders so the resolved text shows immediately.
    /// </summary>
    public void AcceptAllRevisions()
    {
        CommitToModel();
        TrackChanges.AcceptAll(_model);
        Render();
    }

    /// <summary>
    /// Reject every tracked change in the document: insertions are removed, deletions become ordinary
    /// text. Commits pending edits first, then re-renders so the resolved text shows immediately.
    /// </summary>
    public void RejectAllRevisions()
    {
        CommitToModel();
        TrackChanges.RejectAll(_model);
        Render();
    }

    /// <summary>
    /// Marks the model runs of <paramref name="paragraph"/> covering the character range
    /// [<paramref name="startOffset"/>, <paramref name="endOffset"/>) as a tracked change of
    /// <paramref name="kind"/>, splitting runs at the boundaries. Offsets are measured over the
    /// paragraph's plain text. Mirrors <see cref="MarkCommentRange"/>.
    /// </summary>
    private static void MarkRevisionRange(ModelParagraph paragraph, int startOffset, int endOffset, RevisionKind kind, string author, string? dateXml)
    {
        var pos = 0;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var len = run.Text.Length;
            var runStart = pos;
            var runEnd = pos + len;
            pos = runEnd;
            if (len == 0)
                continue;

            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
                continue;

            if (coverStart > runStart)
            {
                var head = new ModelRun(run.Text[..(coverStart - runStart)], run.Formatting)
                {
                    HyperlinkUrl = run.HyperlinkUrl,
                    HyperlinkAnchor = run.HyperlinkAnchor,
                    CommentId = run.CommentId,
                    Revision = run.Revision,
                    RevisionAuthor = run.RevisionAuthor,
                    RevisionDateXml = run.RevisionDateXml
                };
                run.Text = run.Text[(coverStart - runStart)..];
                paragraph.Runs.Insert(i, head);
                i++;
            }
            if (coverEnd < runEnd)
            {
                var tail = new ModelRun(run.Text[(coverEnd - coverStart)..], run.Formatting)
                {
                    HyperlinkUrl = run.HyperlinkUrl,
                    HyperlinkAnchor = run.HyperlinkAnchor,
                    CommentId = run.CommentId,
                    Revision = run.Revision,
                    RevisionAuthor = run.RevisionAuthor,
                    RevisionDateXml = run.RevisionDateXml
                };
                run.Text = run.Text[..(coverEnd - coverStart)];
                paragraph.Runs.Insert(i + 1, tail);
            }

            run.Revision = kind;
            run.RevisionAuthor = author;
            run.RevisionDateXml = dateXml;
        }
    }

    /// <summary>The plain-text character offset of <paramref name="position"/> from the paragraph's start.</summary>
    private static int OffsetInParagraph(WpfParagraph paragraph, TextPointer position)
    {
        var range = new TextRange(paragraph.ContentStart, position);
        return range.Text.Length;
    }

    /// <summary>
    /// Marks the model runs of <paramref name="paragraph"/> covering the character range
    /// [<paramref name="startOffset"/>, <paramref name="endOffset"/>) with comment id
    /// <paramref name="commentId"/>, splitting runs at the boundaries, and inserts a textless reference
    /// run just after the covered span. Offsets are measured over the paragraph's plain text. Returns
    /// false when no textual run is covered (nothing to comment on).
    /// </summary>
    private static bool MarkCommentRange(ModelParagraph paragraph, int startOffset, int endOffset, int commentId)
    {
        var pos = 0;
        var lastCoveredIndex = -1;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            // Non-text runs (images, markers) have no width in this offset model; skip but advance past
            // any literal text they carry.
            var len = run.Text.Length;
            var runStart = pos;
            var runEnd = pos + len;
            pos = runEnd;
            if (len == 0)
                continue;

            // Clip the run to the selected range; skip runs entirely outside it.
            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
                continue;

            // Split off the leading uncovered part, if any.
            if (coverStart > runStart)
            {
                var head = new ModelRun(run.Text[..(coverStart - runStart)], run.Formatting)
                {
                    HyperlinkUrl = run.HyperlinkUrl,
                    HyperlinkAnchor = run.HyperlinkAnchor
                };
                run.Text = run.Text[(coverStart - runStart)..];
                paragraph.Runs.Insert(i, head);
                i++;
            }
            // Split off the trailing uncovered part, if any.
            if (coverEnd < runEnd)
            {
                var tail = new ModelRun(run.Text[(coverEnd - coverStart)..], run.Formatting)
                {
                    HyperlinkUrl = run.HyperlinkUrl,
                    HyperlinkAnchor = run.HyperlinkAnchor
                };
                run.Text = run.Text[..(coverEnd - coverStart)];
                paragraph.Runs.Insert(i + 1, tail);
            }

            run.CommentId = commentId;
            lastCoveredIndex = i;
        }

        if (lastCoveredIndex < 0)
            return false;

        paragraph.Runs.Insert(lastCoveredIndex + 1, ModelRun.CommentReference(commentId));
        return true;
    }

    /// <summary>
    /// Applies an external hyperlink to the current selection. If the selection is non-empty its text
    /// becomes the link; if it is empty the URL itself is inserted as a linked run. Re-renders so the
    /// link is styled and round-trips through the model on the next commit.
    /// </summary>
    public void ApplyHyperlink(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        Focus();
        var selection = Selection;
        if (selection.IsEmpty)
        {
            // No selection: drop the URL as its own linked run at the caret.
            var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
            var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
            if (paragraph is null)
            {
                paragraph = new WpfParagraph();
                Document.Blocks.Add(paragraph);
            }
            paragraph.Inlines.Add(NewLink(new WpfRun(url), uri, url));
        }
        else
        {
            // Wrap the selected text range in a hyperlink (WPF splits runs at the range boundaries).
            try
            {
                var link = new WpfHyperlink(selection.Start, selection.End)
                {
                    NavigateUri = uri,
                    ToolTip = url
                };
                StyleLink(link, url);
            }
            catch (ArgumentException)
            {
                // Selection spanned a non-text boundary (e.g. a table); ignore rather than crash.
                return;
            }
        }

        CommitToModel();
        Render();
    }

    private static WpfHyperlink NewLink(WpfRun content, Uri uri, string url)
    {
        var link = new WpfHyperlink(content) { NavigateUri = uri, ToolTip = url };
        StyleLink(link, url);
        return link;
    }

    /// <summary>
    /// Scrolls the body block at <paramref name="modelBlockIndex"/> (an index into
    /// <see cref="TextDocument.Blocks"/>, e.g. an <see cref="OutlineEntry.BlockIndex"/>) into view and
    /// moves the caret to its start, giving the editor focus. The model block order maps to the
    /// FlowDocument by numbering "leaf" blocks (paragraphs, table-cell-flattened list items, and
    /// tables) in document order — the same scheme <see cref="CommitToModel"/> reads back — so the
    /// mapping stays correct across lists and tables. A no-op for an out-of-range or unmappable index.
    /// </summary>
    public void BringBlockIntoView(int modelBlockIndex)
    {
        if (modelBlockIndex < 0)
            return;

        var target = LeafBlockAtModelIndex(modelBlockIndex);
        if (target is null)
            return;

        target.BringIntoView();
        // Place the caret at the block's content start and focus so the user lands on the heading.
        if (target.ContentStart is { } start)
            CaretPosition = start.GetInsertionPosition(LogicalDirection.Forward) ?? start;
        Focus();
    }

    // Find the FlowDocument leaf block whose model index equals modelBlockIndex, numbering leaf blocks
    // in document order exactly as NumberLeafBlocks/CommitToModel do (lists flatten into their item
    // paragraphs; a table counts as one leaf). Returns null if the index is past the last leaf block.
    private System.Windows.Documents.Block? LeafBlockAtModelIndex(int modelBlockIndex)
    {
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
        {
            if (FindLeafBlock(block, modelBlockIndex, ref modelIndex) is { } found)
                return found;
        }
        return null;
    }

    private static System.Windows.Documents.Block? FindLeafBlock(
        System.Windows.Documents.Block block, int targetIndex, ref int modelIndex)
    {
        switch (block)
        {
            case WpfParagraph:
                if (modelIndex == targetIndex)
                    return block;
                modelIndex++;
                break;
            case WpfList list:
                foreach (var item in list.ListItems)
                {
                    foreach (var itemBlock in item.Blocks)
                    {
                        if (FindLeafBlock(itemBlock, targetIndex, ref modelIndex) is { } found)
                            return found;
                    }
                }
                break;
            case WpfTable:
                if (modelIndex == targetIndex)
                    return block;
                modelIndex++;
                break;
        }
        return null;
    }

    /// <summary>The names of every bookmark defined in the document (committed state), in document order.</summary>
    public IReadOnlyList<string> BookmarkNames()
    {
        CommitToModel();
        return _model.Blocks.OfType<ModelParagraph>()
            .Where(p => p.BookmarkName is { Length: > 0 })
            .Select(p => p.BookmarkName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Names the paragraph containing the caret as a bookmark target (an invisible marker). An empty
    /// or whitespace name clears any existing bookmark on that paragraph. Re-renders so the name
    /// round-trips through the model on the next commit.
    /// </summary>
    public void SetBookmarkAtCaret(string? name)
    {
        Focus();
        CommitToModel();
        var index = CaretBlockIndex();
        if (index < 0 || index >= _model.Blocks.Count || _model.Blocks[index] is not ModelParagraph paragraph)
            return;
        paragraph.BookmarkName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Render();
    }

    /// <summary>
    /// Applies an internal hyperlink (to an existing bookmark) over the current selection. If the
    /// selection is empty the bookmark name itself is inserted as a linked run at the caret. Re-renders
    /// so the link is styled and round-trips (as w:hyperlink w:anchor) on the next commit.
    /// </summary>
    public void ApplyInternalLink(string anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
            return;
        anchor = anchor.Trim();

        Focus();
        var selection = Selection;
        if (selection.IsEmpty)
        {
            var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
            var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
            if (paragraph is null)
            {
                paragraph = new WpfParagraph();
                Document.Blocks.Add(paragraph);
            }
            var link = new WpfHyperlink(new WpfRun(anchor));
            StyleInternalLink(link, anchor);
            paragraph.Inlines.Add(link);
        }
        else
        {
            try
            {
                var link = new WpfHyperlink(selection.Start, selection.End);
                StyleInternalLink(link, anchor);
            }
            catch (ArgumentException)
            {
                // Selection spanned a non-text boundary (e.g. a table); ignore rather than crash.
                return;
            }
        }

        CommitToModel();
        Render();
    }

    private static void StyleLink(WpfHyperlink link, string url)
    {
        link.ToolTip = url;
        link.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x63, 0xC1));
        link.RequestNavigate += OnHyperlinkRequestNavigate;
    }

    // --- view -> model ---

    private static RunFormatting ReadRunFormatting(WpfRun run)
    {
        var verticalAlign = run.BaselineAlignment switch
        {
            BaselineAlignment.Superscript => VerticalAlign.Superscript,
            BaselineAlignment.Subscript => VerticalAlign.Subscript,
            _ => VerticalAlign.Baseline
        };
        // Super/subscript glyphs are rendered shrunk by SuperSubScale; undo that so the committed
        // point size matches what the user actually chose.
        var fontSizePt = run.FontSize / PxPerPoint;
        if (verticalAlign != VerticalAlign.Baseline)
            fontSizePt /= SuperSubScale;

        var capitals = Typography.GetCapitals(run);
        return new RunFormatting
        {
            Bold = run.FontWeight >= FontWeights.Bold,
            Italic = run.FontStyle == FontStyles.Italic,
            Underline = run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true,
            Strikethrough = run.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true,
            SmallCaps = capitals == FontCapitals.SmallCaps,
            AllCaps = capitals == FontCapitals.AllSmallCaps,
            VerticalAlign = verticalAlign,
            FontFamily = run.FontFamily.Source,
            FontSizePt = fontSizePt,
            ColorHex = run.Foreground is SolidColorBrush brush ? ToHex(brush.Color) : null,
            HighlightColorHex = run.Background is SolidColorBrush highlight ? ToHex(highlight.Color) : null
        };
    }

    // Undo the view-only chrome BuildRun injects for a tracked-change run: clear the revision colour
    // (so it doesn't leak into the model as an explicit colour) and remove the decoration the kind added
    // (underline for an insertion, strikethrough for a deletion). The run's own real formatting is kept.
    private static RunFormatting StripRevisionChrome(RunFormatting formatting, RevisionKind kind)
    {
        var revisionHex = ToHex(RevisionColor);
        return formatting with
        {
            ColorHex = string.Equals(formatting.ColorHex, revisionHex, StringComparison.OrdinalIgnoreCase) ? null : formatting.ColorHex,
            Underline = kind == RevisionKind.Inserted ? false : formatting.Underline,
            Strikethrough = kind == RevisionKind.Deleted ? false : formatting.Strikethrough
        };
    }

    private static ParagraphFormatting ReadParagraphFormatting(WpfParagraph paragraph, TextDocument document) =>
        ParagraphFormatting.Default with
        {
            Alignment = FromWpfAlignment(paragraph.TextAlignment),
            SpaceBeforePt = paragraph.Margin.Top / PxPerPoint,
            SpaceAfterPt = paragraph.Margin.Bottom / PxPerPoint,
            LineSpacing = ReadLineSpacing(paragraph.LineHeight, document),
            IndentLeftPt = paragraph.Margin.Left / PxPerPoint,
            IndentRightPt = paragraph.Margin.Right / PxPerPoint,
            FirstLineIndentPt = paragraph.TextIndent / PxPerPoint,
            Border = paragraph.BorderBrush is SolidColorBrush bb && paragraph.BorderThickness.Top > 0
                ? new ParagraphBorder(ToHex(bb.Color), paragraph.BorderThickness.Top / PxPerPoint)
                : null,
            ShadingColorHex = paragraph.Background is SolidColorBrush shading ? ToHex(shading.Color) : null,
            // Tab stops are not representable in the WPF FlowDocument Paragraph, so they are preserved
            // verbatim from the Tag stamped by BuildParagraph (see comment there); empty if none.
            TabStops = paragraph.Tag is ParagraphTag { TabStops: var tabStops } ? tabStops : []
        };

    // Recover the line-spacing multiplier from a WPF paragraph's LineHeight, inverting the formula used
    // in BuildParagraph (LineHeight = LineSpacing * defaultFontSize * PxPerPoint). An unset LineHeight is
    // NaN; fall back to the model default so editing text never silently flattens a paragraph's spacing.
    private static double ReadLineSpacing(double lineHeight, TextDocument document)
    {
        var fontPt = document.DefaultRun.FontSizePt ?? 11;
        if (double.IsNaN(lineHeight) || lineHeight <= 0 || fontPt <= 0)
            return ParagraphFormatting.Default.LineSpacing;
        return lineHeight / (fontPt * PxPerPoint);
    }

    // --- formatting resolution (run/paragraph -> style -> document default) ---

    private static RunFormatting Resolve(ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        var style = StyleRun(paragraph, document);
        var d = document.DefaultRun;
        var r = run.Formatting;
        return new RunFormatting
        {
            Bold = r.Bold || style.Bold || d.Bold,
            Italic = r.Italic || style.Italic || d.Italic,
            Underline = r.Underline || style.Underline || d.Underline,
            Strikethrough = r.Strikethrough || style.Strikethrough || d.Strikethrough,
            SmallCaps = r.SmallCaps || style.SmallCaps || d.SmallCaps,
            AllCaps = r.AllCaps || style.AllCaps || d.AllCaps,
            VerticalAlign = r.VerticalAlign != VerticalAlign.Baseline ? r.VerticalAlign
                : style.VerticalAlign != VerticalAlign.Baseline ? style.VerticalAlign
                : d.VerticalAlign,
            FontFamily = r.FontFamily ?? style.FontFamily ?? d.FontFamily,
            FontSizePt = r.FontSizePt ?? style.FontSizePt ?? d.FontSizePt,
            ColorHex = r.ColorHex ?? style.ColorHex ?? d.ColorHex,
            HighlightColorHex = r.HighlightColorHex ?? style.HighlightColorHex ?? d.HighlightColorHex
        };
    }

    private static ParagraphFormatting Resolve(ModelParagraph paragraph, TextDocument document)
    {
        // Explicit paragraph formatting wins; otherwise fall back to the style's paragraph props.
        if (paragraph.StyleId is { } id && document.Styles.TryGetValue(id, out var style))
        {
            var sp = style.Paragraph;
            var p = paragraph.Formatting;
            return p == ParagraphFormatting.Default ? sp : p;
        }
        return paragraph.Formatting;
    }

    private static RunFormatting StyleRun(ModelParagraph paragraph, TextDocument document) =>
        paragraph.StyleId is { } id && document.Styles.TryGetValue(id, out var style)
            ? style.Run
            : RunFormatting.Default;

    private static WpfTextAlignment ToWpfAlignment(ModelTextAlignment alignment) => alignment switch
    {
        ModelTextAlignment.Center => WpfTextAlignment.Center,
        ModelTextAlignment.Right => WpfTextAlignment.Right,
        ModelTextAlignment.Justify => WpfTextAlignment.Justify,
        _ => WpfTextAlignment.Left
    };

    private static ModelTextAlignment FromWpfAlignment(WpfTextAlignment alignment) => alignment switch
    {
        WpfTextAlignment.Center => ModelTextAlignment.Center,
        WpfTextAlignment.Right => ModelTextAlignment.Right,
        WpfTextAlignment.Justify => ModelTextAlignment.Justify,
        _ => ModelTextAlignment.Left
    };

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
