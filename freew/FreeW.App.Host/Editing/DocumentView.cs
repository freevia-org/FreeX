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
using ModelContentControl = FreeW.Core.Model.ContentControl;
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

    /// <summary>
    /// The file name a FILENAME field resolves to during the current <see cref="Render"/> pass. Set from
    /// <see cref="CurrentFileName"/> at the top of Render so the otherwise-static run builders can resolve
    /// it without threading it through every signature; thread-static to keep it isolated per render call.
    /// </summary>
    [ThreadStatic]
    private static string? _renderFileName;

    private readonly DocumentCommandBus _commands;
    private readonly ScaleTransform _zoomTransform = new(ZoomLevels.Default, ZoomLevels.Default);
    private double _zoomLevel = ZoomLevels.Default;

    /// <summary>
    /// Holds the run + paragraph formatting captured when Format Painter is armed (null when the
    /// painter is idle). On the next selection the user makes, this is stamped onto that selection
    /// and the painter disarms. See <see cref="ArmFormatPainter"/>.
    /// </summary>
    private FormatPainterClipboard? _formatPainter;

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
    /// The current document's file name (without path), used to resolve FILENAME field runs at render.
    /// Null/empty when the document is unsaved, in which case a FILENAME field falls back to its cached
    /// text. The host sets this when a document is opened or saved; the model/IO never see it.
    /// </summary>
    public string? CurrentFileName { get; set; }

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
    /// Mutate the page settings and re-render so layout-affecting changes (notably the column count)
    /// show immediately. Pending in-progress edits are committed first so the re-render does not drop
    /// them. Used by the Layout ribbon's page-setup commands.
    /// </summary>
    public void ApplyPageSettings(Action<PageSettings> apply)
    {
        CommitToModel();
        apply(_model.Page);
        Render();
    }

    /// <summary>
    /// Toggle the whole-page border (w:sectPr/w:pgBorders). When the page has no border one is added
    /// (<paramref name="colorHex"/>/<paramref name="widthPt"/>); otherwise it is cleared. Re-renders so
    /// the change shows immediately and round-trips through the model on save. Layout-ribbon command.
    /// </summary>
    public void TogglePageBorder(string colorHex = "#000000", double widthPt = 1.0) =>
        ApplyPageSettings(page => page.PageBorder =
            page.PageBorder is null ? new PageBorder(colorHex, widthPt) : null);

    /// <summary>
    /// Set (or clear) the page watermark text. A null/empty value removes the watermark. Re-renders so
    /// the faint diagonal text shows immediately and round-trips on save. Layout-ribbon command.
    /// </summary>
    public void SetWatermark(string? text) =>
        ApplyPageSettings(page => page.Watermark = string.IsNullOrWhiteSpace(text) ? null : text.Trim());

    /// <summary>
    /// Apply a document theme (colour/font scheme) to the model's style catalog and re-render so the
    /// new heading colours/fonts and body face show immediately. This is a document-wide style change
    /// to the catalog (not the per-paragraph runs), so it is applied directly rather than through the
    /// undo/redo bus: pending in-progress edits are committed first so the re-render does not drop them,
    /// then <see cref="DocumentTheme.Apply"/> rewrites the relevant styles and the surface re-renders.
    /// Used by the Design ribbon's theme dropdown.
    /// </summary>
    public void ApplyTheme(DocumentTheme theme)
    {
        CommitToModel();
        DocumentTheme.Apply(_model, theme);
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

    /// <summary>
    /// Insert a Table of Contents generated from the document's heading outline. The TOC paragraphs
    /// (built by <see cref="TableOfContents.Build"/>) are inserted at the caret's block (else at the
    /// document start), routed one-by-one through the undo/redo bus so the insert is reversible. The
    /// paragraphs carry dedicated TOC styles (registered via <see cref="TableOfContents.EnsureStyles"/>)
    /// which both give them distinct formatting and mark the region for <see cref="RefreshTableOfContents"/>.
    /// </summary>
    public void InsertTableOfContents()
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        TableOfContents.EnsureStyles(_model);

        // Insert before the caret's block so the TOC reads as a front-matter region; fall back to the
        // document start when the caret can't be mapped.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = 0;

        InsertTocAt(index);
    }

    /// <summary>
    /// Rebuild the Table of Contents: remove the previously inserted TOC region (paragraphs carrying a
    /// TOC style, see <see cref="TableOfContents.IsTocParagraph"/>) and re-insert a freshly generated
    /// TOC at the same position. With no existing TOC this behaves like <see cref="InsertTableOfContents"/>,
    /// inserting at the document start. Every removal/insert is reversible through the undo/redo bus.
    /// </summary>
    public void RefreshTableOfContents()
    {
        CommitToModel();
        TableOfContents.EnsureStyles(_model);

        // Find the contiguous run of existing TOC paragraphs (the marker region). They are inserted as
        // a block, so the first TOC paragraph anchors the region and the rest follow consecutively.
        var firstToc = -1;
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (TableOfContents.IsTocParagraph(_model.Blocks[i]))
            {
                firstToc = i;
                break;
            }
        }

        var insertAt = firstToc >= 0 ? firstToc : 0;

        // Remove every existing TOC paragraph (reversible). Delete from the end so earlier indices stay
        // valid; collect first to avoid mutating while scanning.
        var tocIndices = new List<int>();
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (TableOfContents.IsTocParagraph(_model.Blocks[i]))
                tocIndices.Add(i);
        }
        for (var i = tocIndices.Count - 1; i >= 0; i--)
            _commands.Execute(new DeleteParagraphCommand(tocIndices[i]));

        InsertTocAt(insertAt);
    }

    // Insert the freshly built TOC paragraphs starting at block index `at`, one reversible
    // InsertParagraphCommand each (kept in order), then re-render. The bus's Changed event redraws.
    private void InsertTocAt(int at)
    {
        var toc = TableOfContents.Build(_model);
        var index = Math.Clamp(at, 0, _model.Blocks.Count);
        foreach (var paragraph in toc)
            _commands.Execute(new InsertParagraphCommand(index++, paragraph));
    }

    /// <summary>
    /// Prepend a simple cover page (a Title paragraph, an optional author Subtitle, and a spacer) at the
    /// start of the document, routing each block insert through the undo/redo bus so it is reversible.
    /// The title/author come from <see cref="TextDocument.Properties"/> (see
    /// <see cref="DocumentOps.BuildCoverPage"/>). Re-renders the surface.
    /// </summary>
    public void InsertCoverPage()
    {
        CommitToModel();
        var blocks = DocumentOps.BuildCoverPage(_model);
        for (var i = 0; i < blocks.Count; i++)
            _commands.Execute(new InsertBlockCommand(i, blocks[i]));
    }

    /// <summary>
    /// Insert a horizontal rule (an empty paragraph with a bottom-only border) after the block the caret
    /// sits in, routing through the undo/redo bus. Re-renders the surface.
    /// </summary>
    public void InsertHorizontalRule()
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertBlockCommand(index, DocumentOps.CreateHorizontalRule()));
    }

    /// <summary>
    /// Insert a page break (an empty paragraph that forces a page break before it) after the block the
    /// caret sits in, routing through the undo/redo bus. Re-renders the surface.
    /// </summary>
    public void InsertPageBreak()
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertBlockCommand(index, DocumentOps.CreatePageBreak()));
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
    /// Increase the left indent of every paragraph spanned by the selection by one step
    /// (<paramref name="stepPt"/> points, default 36pt = 0.5in), via the pure
    /// <see cref="Indentation.IncreaseIndent"/> helper. Reversible through the undo/redo bus, then re-rendered.
    /// </summary>
    public void IncreaseIndent(double stepPt = Indentation.DefaultStepPt) =>
        FormatSelectedModelParagraphs(f => Indentation.IncreaseIndent(f, stepPt));

    /// <summary>
    /// Decrease the left indent of every paragraph spanned by the selection by one step
    /// (<paramref name="stepPt"/> points, default 36pt = 0.5in), clamped at zero, via the pure
    /// <see cref="Indentation.DecreaseIndent"/> helper. Reversible through the undo/redo bus, then re-rendered.
    /// </summary>
    public void DecreaseIndent(double stepPt = Indentation.DefaultStepPt) =>
        FormatSelectedModelParagraphs(f => Indentation.DecreaseIndent(f, stepPt));

    /// <summary>
    /// Set the left, right, and first-line indents (points) on every paragraph spanned by the selection,
    /// via the pure <see cref="Indentation.SetIndents"/> helper. A negative <paramref name="firstLine"/>
    /// is a hanging indent (see the convention on <see cref="Indentation"/>); it maps straight to the
    /// rendered paragraph's <see cref="System.Windows.Documents.Paragraph.TextIndent"/>. Reversible via
    /// the bus, then re-rendered.
    /// </summary>
    public void SetParagraphIndents(double left, double right, double firstLine) =>
        FormatSelectedModelParagraphs(f => Indentation.SetIndents(f, left, right, firstLine));

    /// <summary>
    /// The left/right/first-line indents (points) of the first paragraph spanned by the current
    /// selection, or <see cref="ParagraphFormatting.Default"/>'s indents if there is none. Used to seed
    /// the Paragraph dialog with the current values.
    /// </summary>
    public (double Left, double Right, double FirstLine) CurrentParagraphIndents()
    {
        var first = SelectedModelParagraphs().FirstOrDefault();
        var f = first?.Formatting ?? ParagraphFormatting.Default;
        return (f.IndentLeftPt, f.IndentRightPt, f.FirstLineIndentPt);
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

    /// <summary>
    /// Apply a drop cap to the caret's paragraph: the leading letter is split into its own enlarged,
    /// bold run (see <see cref="DropCap.ApplyDropCap"/>), the remainder keeping its formatting. Routes
    /// through the undo/redo bus (reversible) and re-renders so the enlarged letter shows immediately.
    /// No-op outside a paragraph or on a paragraph with no leading text run.
    /// </summary>
    public void ApplyDropCap(double sizePt = DropCap.DefaultSizePt)
    {
        Focus();
        CommitToModel();
        var index = SelectedModelParagraphIndices().FirstOrDefault(-1);
        if (index < 0 || index >= _model.Blocks.Count || _model.Blocks[index] is not ModelParagraph)
            return;
        _commands.Execute(new ReplaceParagraphRunsCommand(index, p => DropCap.ApplyDropCap(p, sizePt)));
    }

    /// <summary>
    /// Clear all character formatting in every model paragraph spanned by the selection (or the caret's
    /// paragraph): each run's formatting is reset to <see cref="RunFormatting.Default"/> while its text is
    /// kept (see <see cref="DropCap.ClearFormatting"/>). One reversible <see cref="FormatParagraphRunsCommand"/>
    /// per paragraph on the undo/redo bus; the view re-renders so the reset shows immediately.
    /// </summary>
    public void ClearFormatting()
    {
        Focus();
        CommitToModel();
        foreach (var index in SelectedModelParagraphIndices())
        {
            if (_model.Blocks[index] is ModelParagraph)
                _commands.Execute(new FormatParagraphRunsCommand(index, _ => RunFormatting.Default));
        }
    }

    /// <summary>
    /// Sort the paragraphs spanned by the current selection (or, with a bare caret, the paragraph the
    /// caret sits in) by their text, in place. Tables interleaved in the selected span are left fixed at
    /// their own positions — only the paragraph blocks are reordered among their own slots — so the
    /// operation stays well-defined over a mixed body. Routes through the undo/redo bus (one reversible
    /// <see cref="ReplaceBlocksCommand"/>) and re-renders. No-op without at least two sortable paragraphs.
    /// </summary>
    public void SortSelectedParagraphs(bool ascending, bool caseSensitive)
    {
        Focus();
        CommitToModel();

        var indices = SelectedModelParagraphIndices();
        if (indices.Count == 0)
            return;

        // The contiguous block span the selection covers (first..last selected index, inclusive).
        var first = indices[0];
        var last = indices[indices.Count - 1];
        if (first < 0 || last >= _model.Blocks.Count)
            return;

        // The paragraph blocks within that span, in document order — only these get reordered.
        var paragraphs = new List<ModelParagraph>();
        for (var i = first; i <= last; i++)
        {
            if (_model.Blocks[i] is ModelParagraph paragraph)
                paragraphs.Add(paragraph);
        }
        if (paragraphs.Count < 2)
            return; // nothing to reorder

        var sorted = ParagraphSort.Sort(paragraphs, ascending, caseSensitive);

        // Rebuild the span: drop sorted paragraphs back into the paragraph slots, keeping any
        // interleaved tables fixed at their own positions.
        var replacement = new List<ModelBlock>(last - first + 1);
        var nextSorted = 0;
        for (var i = first; i <= last; i++)
            replacement.Add(_model.Blocks[i] is ModelParagraph ? sorted[nextSorted++] : _model.Blocks[i]);

        _commands.Execute(new ReplaceBlocksCommand(first, replacement.Count, replacement));
    }

    /// <summary>
    /// Replace the paragraphs spanned by the current selection with a single table built from their
    /// text (split on <paramref name="delimiter"/>, ragged rows padded — see
    /// <see cref="TextTableConvert.TextToTable"/>). Routes through the undo/redo bus (one reversible
    /// <see cref="ReplaceBlocksCommand"/>) and re-renders. No-op when the selection spans no paragraphs.
    /// </summary>
    public void ConvertSelectionToTable(char delimiter)
    {
        Focus();
        CommitToModel();

        var indices = SelectedModelParagraphIndices();
        if (indices.Count == 0)
            return;

        var first = indices[0];
        var last = indices[indices.Count - 1];
        if (first < 0 || last >= _model.Blocks.Count)
            return;

        // Only paragraphs convert; if the span contains no paragraph there is nothing to turn into a table.
        var paragraphs = new List<ModelParagraph>();
        for (var i = first; i <= last; i++)
        {
            if (_model.Blocks[i] is ModelParagraph paragraph)
                paragraphs.Add(paragraph);
        }
        if (paragraphs.Count == 0)
            return;

        var table = TextTableConvert.TextToTable(paragraphs, delimiter);
        _commands.Execute(new ReplaceBlocksCommand(first, last - first + 1, new ModelBlock[] { table }));
    }

    /// <summary>
    /// Replace the table containing the caret with paragraphs built from its rows (cells joined by
    /// <paramref name="delimiter"/>, one paragraph per row — see
    /// <see cref="TextTableConvert.TableToText"/>). Routes through the undo/redo bus (one reversible
    /// <see cref="ReplaceBlocksCommand"/>) and re-renders. No-op when the caret is not inside a table.
    /// </summary>
    public void ConvertTableToText(char delimiter)
    {
        Focus();
        CommitToModel();

        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count || _model.Blocks[blockIndex] is not ModelTable table)
            return;

        var paragraphs = TextTableConvert.TableToText(table, delimiter);
        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, [.. paragraphs]));
    }

    /// <summary>True while Format Painter is armed (captured formatting waiting to be stamped).</summary>
    public bool FormatPainterActive => _formatPainter is not null;

    /// <summary>
    /// Arm the Format Painter: capture the run formatting under the caret/selection and the caret
    /// paragraph's formatting, then wait for the user's next selection to stamp it (the classic
    /// capture-then-apply-to-next gesture). Calling this while already armed disarms it (a toggle).
    /// Returns true if the painter is now armed, false if it was disarmed.
    /// </summary>
    public bool ArmFormatPainter()
    {
        Focus();
        if (_formatPainter is not null)
        {
            _formatPainter = null;
            return false;
        }

        _formatPainter = FormatPainterClipboard.Capture(CaptureSelectionRunFormatting(), CaptureCaretParagraphFormatting());
        return true;
    }

    /// <summary>
    /// If the Format Painter is armed and the current selection is non-empty, stamp the captured run
    /// and paragraph formatting onto it, then disarm. Called on mouse-up after the user drags out the
    /// "next selection". A no-op (leaving the painter armed) when the selection is still empty, so a
    /// stray click that places only a caret does not consume the gesture. Returns true if applied.
    /// </summary>
    private bool TryApplyFormatPainter()
    {
        if (_formatPainter is not { } clipboard || Selection.IsEmpty)
            return false;

        _formatPainter = null; // disarm first so a re-render mid-apply cannot re-trigger

        // Run formatting: stamp the captured character formatting onto the selected text via WPF
        // selection property values (covers partial-run selections), mirroring the inverse of
        // ReadRunFormatting / BuildRun. Paragraph formatting then routes through the model bus.
        ApplyRunFormattingToSelection(clipboard.ApplyTo(RunFormatting.Default));
        var captured = clipboard.Paragraph;
        FormatSelectedModelParagraphs(_ => captured);
        return true;
    }

    // Read the run formatting of the current selection/caret straight from WPF selection property
    // values (so a partial selection or a bare caret both yield a sensible capture), decoding the same
    // way ReadRunFormatting does for a single run.
    private RunFormatting CaptureSelectionRunFormatting()
    {
        var selection = Selection;
        var fontSizePx = selection.GetPropertyValue(TextElement.FontSizeProperty) is double px && px > 0
            ? px
            : DefaultFontSizePt * PxPerPoint;

        var baseline = selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
        var verticalAlign = baseline switch
        {
            BaselineAlignment.Superscript => VerticalAlign.Superscript,
            BaselineAlignment.Subscript => VerticalAlign.Subscript,
            _ => VerticalAlign.Baseline
        };
        var fontSizePt = fontSizePx / PxPerPoint;
        if (verticalAlign != VerticalAlign.Baseline)
            fontSizePt /= SuperSubScale;

        var decorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var capitals = selection.GetPropertyValue(Typography.CapitalsProperty);

        return new RunFormatting
        {
            Bold = selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w >= FontWeights.Bold,
            Italic = selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic,
            Underline = decorations?.Contains(TextDecorations.Underline[0]) == true,
            Strikethrough = decorations?.Contains(TextDecorations.Strikethrough[0]) == true,
            SmallCaps = capitals is FontCapitals.SmallCaps,
            AllCaps = capitals is FontCapitals.AllSmallCaps,
            VerticalAlign = verticalAlign,
            FontFamily = selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family ? family.Source : null,
            FontSizePt = fontSizePt,
            ColorHex = selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush fg ? ToHex(fg.Color) : null,
            HighlightColorHex = selection.GetPropertyValue(TextElement.BackgroundProperty) is SolidColorBrush bg ? ToHex(bg.Color) : null
        };
    }

    // Read the paragraph formatting of the caret's paragraph (or the selection start) from the model,
    // so spacing/alignment/indents/border/shading are captured exactly as they round-trip.
    private ParagraphFormatting CaptureCaretParagraphFormatting()
    {
        var paragraphs = SelectedModelParagraphs();
        return paragraphs.Count > 0 ? paragraphs[0].Formatting : ParagraphFormatting.Default;
    }

    // Apply a fully-resolved RunFormatting to the current selection via WPF selection property values.
    // This is the inverse of CaptureSelectionRunFormatting and reuses the same encodings as BuildRun
    // (super/subscript shrink, small/all caps -> FontCapitals, underline/strikethrough decorations) so
    // the change round-trips through CommitToModel unchanged.
    private void ApplyRunFormattingToSelection(RunFormatting fmt)
    {
        var selection = Selection;
        selection.ApplyPropertyValue(TextElement.FontWeightProperty, fmt.Bold ? FontWeights.Bold : FontWeights.Normal);
        selection.ApplyPropertyValue(TextElement.FontStyleProperty, fmt.Italic ? FontStyles.Italic : FontStyles.Normal);

        if (fmt.FontFamily is { Length: > 0 } family)
            selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(family));

        var fontSizePx = (fmt.FontSizePt ?? DefaultFontSizePt) * PxPerPoint;
        if (fmt.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
        {
            selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty,
                fmt.VerticalAlign == VerticalAlign.Superscript ? BaselineAlignment.Superscript : BaselineAlignment.Subscript);
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSizePx * SuperSubScale);
        }
        else
        {
            selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSizePx);
        }

        selection.ApplyPropertyValue(TextElement.ForegroundProperty,
            TryParseColor(fmt.ColorHex, out var color) ? new SolidColorBrush(color) : Brushes.Black);
        // Highlight: a captured highlight is applied; no highlight clears the background back to none.
        selection.ApplyPropertyValue(TextElement.BackgroundProperty,
            TryParseColor(fmt.HighlightColorHex, out var highlight) ? new SolidColorBrush(highlight) : null!);

        var capitals = fmt.AllCaps ? FontCapitals.AllSmallCaps : fmt.SmallCaps ? FontCapitals.SmallCaps : FontCapitals.Normal;
        selection.ApplyPropertyValue(Typography.CapitalsProperty, capitals);

        var decorations = new TextDecorationCollection();
        if (fmt.Underline)
            decorations.Add(TextDecorations.Underline);
        if (fmt.Strikethrough)
            decorations.Add(TextDecorations.Strikethrough);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
    }

    // After a mouse-driven selection completes, stamp any armed Format Painter onto it. Runs after the
    // base handler so Selection reflects the just-finished drag.
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_formatPainter is not null)
            TryApplyFormatPainter();
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
        // Expose the current file name to the static run builders for this render pass (FILENAME fields).
        _renderFileName = CurrentFileName;
        var flow = new FlowDocument { PagePadding = new Thickness(0) };
        flow.FontFamily = new FontFamily(_model.DefaultRun.FontFamily ?? "Calibri");
        flow.FontSize = (_model.DefaultRun.FontSizePt ?? 11) * PxPerPoint;
        ApplyColumnLayout(flow, _model.Page);

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
        ApplyPageChrome();
        ApplyProtection();
    }

    /// <summary>
    /// Honour the model's document-protection (restrict-editing) state on the editing surface. In
    /// <see cref="ProtectionMode.ReadOnly"/> the RichTextBox is made read-only and given a faint amber
    /// frame so the lock is visible. <see cref="ProtectionMode.CommentsOnly"/> and
    /// <see cref="ProtectionMode.TrackChangesOnly"/> are approximated as read-only too (live
    /// comments-only / forced-tracking editing is out of scope for the RichTextBox), so any protected
    /// mode locks typing; <see cref="ProtectionMode.None"/> restores normal editing. Called from
    /// <see cref="Render"/> (and so from <see cref="LoadModel"/>) and after protection changes.
    /// </summary>
    public void ApplyProtection()
    {
        var protectedDoc = _model.Protection.IsProtected;
        IsReadOnly = protectedDoc;

        // A protected document gets a distinct amber frame so the read-only state is visible. An
        // unprotected document keeps whatever frame ApplyPageChrome set (page border or default grey).
        if (protectedDoc)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x00));
            BorderThickness = new Thickness(Math.Max(2, BorderThickness.Top));
        }
    }

    /// <summary>
    /// Set the document's protection (restrict-editing) mode, committing pending edits first (only while
    /// still editable) so they are not lost, then re-rendering so the read-only state and frame update
    /// immediately. The change round-trips through docx save (word/settings.xml). Used by the Review
    /// ribbon's Restrict Editing command.
    /// </summary>
    public void SetProtection(ProtectionMode mode)
    {
        if (!IsReadOnly)
            CommitToModel();
        _model.Protection = new ProtectionSettings(mode);
        Render();
    }

    /// <summary>
    /// Cycle the document protection: None → ReadOnly → None. Returns the mode now in effect so the
    /// caller (the Review ribbon's Restrict Editing button) can report it. A simple on/off toggle of
    /// read-only protection, the common restrict-editing gesture.
    /// </summary>
    public ProtectionMode ToggleReadOnlyProtection()
    {
        var next = _model.Protection.Mode == ProtectionMode.None ? ProtectionMode.ReadOnly : ProtectionMode.None;
        SetProtection(next);
        return next;
    }

    /// <summary>
    /// Reflects the model's page border and watermark as editor chrome. The page border drives the
    /// control's own <see cref="Control.BorderBrush"/>/<see cref="Control.BorderThickness"/> (drawn
    /// around the editing surface), and the watermark is painted as faint, rotated tiled text behind
    /// the content via the control <see cref="Control.Background"/>. Both are purely visual: the model
    /// and saved document are untouched. Falls back to the default thin grey frame / white background
    /// when neither is set, so existing documents look exactly as before.
    /// </summary>
    private void ApplyPageChrome()
    {
        if (_model.Page.PageBorder is { } pb)
        {
            BorderBrush = new SolidColorBrush(ParseColor(pb.ColorHex, Colors.Black));
            BorderThickness = new Thickness(Math.Max(1, pb.WidthPt * PxPerPoint));
        }
        else
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            BorderThickness = new Thickness(1);
        }

        Background = string.IsNullOrEmpty(_model.Page.Watermark)
            ? Brushes.White
            : BuildWatermarkBrush(_model.Page.Watermark!);
    }

    /// <summary>
    /// Builds a tiling brush that paints faint, 45-degree watermark text on a white page so it sits
    /// behind the document content. Used by the editor background; the print/preview path draws the
    /// same text per page so on-screen and printed output match.
    /// </summary>
    internal static Brush BuildWatermarkBrush(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 48,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80)),
            LayoutTransform = new RotateTransform(-45)
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        label.Arrange(new Rect(label.DesiredSize));

        var visual = new VisualBrush(label)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, Math.Max(240, label.DesiredSize.Width + 80),
                                       Math.Max(240, label.DesiredSize.Height + 80)),
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };

        // Compose the faint tiled watermark over an opaque white page so the editing surface stays
        // white behind the text.
        var canvas = new Grid { Background = Brushes.White };
        canvas.Children.Add(new System.Windows.Shapes.Rectangle { Fill = visual });
        return new VisualBrush(canvas) { Stretch = Stretch.Fill };
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static TextMarkerStyle ToMarkerStyle(ListKind kind) =>
        kind == ListKind.Number ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;

    /// <summary>
    /// Applies the page's multi-column layout to a <see cref="FlowDocument"/>. A FlowDocument derives
    /// its column count from <see cref="FlowDocument.ColumnWidth"/> relative to its content area, so to
    /// render exactly <see cref="PageSettings.ColumnCount"/> equal columns we set the column width to
    /// (contentWidth - (N-1)*gap) / N and the gap to the model's column spacing. Single-column pages
    /// (the default) keep an infinite column width so the text spans the full content area, exactly as
    /// before. Shared by the editor and the print/preview path so on-screen and printed layouts match.
    /// </summary>
    internal static void ApplyColumnLayout(FlowDocument flow, PageSettings page)
    {
        var columns = Math.Max(1, page.ColumnCount);
        if (columns <= 1)
        {
            flow.ColumnWidth = double.PositiveInfinity; // single column spans the whole content area
            flow.ColumnGap = 0;
            return;
        }

        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);
        var gapDip = PageLayout.PointsToDip(page.ColumnSpacingPt);
        var columnWidthDip = (contentWidthDip - (columns - 1) * gapDip) / columns;
        // Guard degenerate geometry (narrow page / wide gaps) so the width stays usable and positive.
        flow.ColumnWidth = Math.Max(1, columnWidthDip);
        flow.ColumnGap = Math.Max(0, gapDip);
    }

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._model;
    }

    /// <summary>
    /// Side-band paragraph data carried on a WPF <see cref="WpfParagraph.Tag"/> so it survives an
    /// edit/commit cycle even though the FlowDocument paragraph has no native slot for it. Holds the
    /// model's tab stops (not representable in WPF), the paragraph's bookmark name (an invisible
    /// marker), and whether a page break is forced before the paragraph (rendered as a separator, but
    /// otherwise invisible in the FlowDocument). Any field may be empty/null/false; the Tag is only
    /// stamped when at least one is set.
    /// </summary>
    private sealed record ParagraphTag(IReadOnlyList<TabStop> TabStops, string? BookmarkName, bool PageBreakBefore = false);

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
            case WpfRun { Tag: EndnoteMarker endnoteMarker }:
                modelParagraph.Runs.Add(ModelRun.EndnoteReference(endnoteMarker.EndnoteId));
                break;
            case WpfRun { Tag: FieldMarker fieldMarker } fieldRun:
                // A document field round-trips its kind; the run's visible text is the last-resolved
                // value, which we keep as the cached fallback (matching Word's cached-field behaviour).
                // If the run somehow lost its text, fall back to the marker's stored cached value.
                var cachedText = fieldRun.Text.Length > 0 ? fieldRun.Text : fieldMarker.Cached;
                modelParagraph.Runs.Add(new ModelRun(cachedText, ReadRunFormatting(fieldRun))
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    FieldKind = fieldMarker.Kind
                });
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
            case WpfRun { Tag: ContentControlMarker ccMarker } controlRun when controlRun.Text.Length > 0:
                // A content-control run: recover its formatting but drop the injected control shade
                // (view-only chrome) and carry the control back onto the model run. For a checkbox the
                // run text already holds the (possibly toggled) ☒/☐ glyph; keep the marker's control in
                // sync with that glyph so a click that toggled the glyph round-trips its checked state.
                var control = ccMarker.Control;
                if (control.Kind == ContentControlKind.CheckBox)
                    control = control with { Checked = controlRun.Text == ModelContentControl.CheckedGlyph };
                modelParagraph.Runs.Add(new ModelRun(controlRun.Text, ReadRunFormatting(controlRun) with { HighlightColorHex = null })
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    Control = control
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
            // A bottom-only border (horizontal rule) draws just the bottom edge; a box draws all four.
            // ReadParagraphFormatting recovers BottomOnly from the same asymmetric thickness.
            var w = border.WidthPt * PxPerPoint;
            wpf.BorderThickness = border.BottomOnly ? new Thickness(0, 0, 0, w) : new Thickness(w);
            wpf.Padding = new Thickness(2);
        }
        if (TryParseColor(paraFmt.ShadingColorHex, out var shading))
            wpf.Background = new SolidColorBrush(shading);

        // A forced page break before the paragraph (w:pageBreakBefore) has no FlowDocument equivalent,
        // so render it as a dashed separator along the paragraph's top edge — a visible "page break"
        // marker — and carry the flag on the Tag so it survives commit and round-trips to docx.
        if (paraFmt.PageBreakBefore)
        {
            wpf.BorderBrush ??= new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
            wpf.BorderThickness = new Thickness(
                wpf.BorderThickness.Left,
                Math.Max(wpf.BorderThickness.Top, 1),
                wpf.BorderThickness.Right,
                wpf.BorderThickness.Bottom);
            if (wpf.Margin.Top < 6 * PxPerPoint)
                wpf.Margin = new Thickness(wpf.Margin.Left, 6 * PxPerPoint, wpf.Margin.Right, wpf.Margin.Bottom);
        }

        // WPF's FlowDocument Paragraph has no tab-stop API, so tab stops cannot be rendered with
        // custom positions/alignments (default tab rendering applies visually). A bookmark name is an
        // invisible marker with no FlowDocument representation either, and page-break-before has no
        // native slot. To avoid losing any of them on an edit/commit cycle, we carry them on the
        // paragraph's Tag (a ParagraphTag) and read them back verbatim on commit; the round-trip is exact.
        if (paraFmt.TabStops.Count > 0 || paragraph.BookmarkName is { Length: > 0 } || paraFmt.PageBreakBefore)
            wpf.Tag = new ParagraphTag(paraFmt.TabStops, paragraph.BookmarkName, paraFmt.PageBreakBefore);

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

        if (run.EndnoteId is { } endnoteId)
            return BuildEndnoteReference(endnoteId, document);

        if (run.FieldKind != RunFieldKind.None)
            return BuildFieldRun(run, document);

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

        // A content control (w:sdt) run is given a subtle shaded background and bracket-style tooltip so
        // it reads as a control, plus a ContentControlMarker tag so the control round-trips on commit
        // (see ReadInline). A checkbox control toggles its glyph when clicked.
        if (run.Control is { } control)
            ApplyContentControlMarker(wpf, control);

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

    /// <summary>Subtle shaded background used to mark a content-control (w:sdt) region (a pale grey).</summary>
    private static readonly Color ContentControlShade = Color.FromRgb(0xEC, 0xEC, 0xF4);

    /// <summary>
    /// Carried on a content-control WPF run's Tag so CommitToModel can round-trip the control (kind,
    /// tag, alias, checked state). Mirrors how CommentMarker/RevisionMarker preserve their marks across
    /// an edit/commit cycle.
    /// </summary>
    private sealed record ContentControlMarker(ModelContentControl Control);

    /// <summary>
    /// Marks a WPF run as the content of a content control (w:sdt): a subtle shaded background so the
    /// control region is visible, a bracket-style tooltip, and a <see cref="ContentControlMarker"/> tag
    /// so the control survives a commit/round-trip. A checkbox control toggles its glyph on click.
    /// </summary>
    private static void ApplyContentControlMarker(WpfRun wpf, ModelContentControl control)
    {
        wpf.Tag = new ContentControlMarker(control);
        wpf.Background = new SolidColorBrush(ContentControlShade);
        wpf.ToolTip = control.Kind == ContentControlKind.CheckBox
            ? (control.Alias is { Length: > 0 } a ? $"Checkbox: {a}" : "Checkbox content control (click to toggle)")
            : (control.Alias is { Length: > 0 } a2 ? $"Content control: {a2}" : "Plain-text content control");

        if (control.Kind == ContentControlKind.CheckBox)
        {
            wpf.Cursor = System.Windows.Input.Cursors.Hand;
            wpf.MouseLeftButtonUp += OnCheckBoxControlClicked;
        }
    }

    /// <summary>
    /// Toggles a checkbox content control when its glyph run is clicked: flips the checked state on the
    /// run's <see cref="ContentControlMarker"/> and swaps the displayed ☒/☐ glyph in place. The owning
    /// view re-commits so the new state round-trips on save.
    /// </summary>
    private static void OnCheckBoxControlClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: ContentControlMarker marker } wpf
            || marker.Control.Kind != ContentControlKind.CheckBox)
            return;

        var toggled = marker.Control with { Checked = !marker.Control.Checked };
        wpf.Tag = new ContentControlMarker(toggled);
        wpf.Text = toggled.Checked ? ModelContentControl.CheckedGlyph : ModelContentControl.UncheckedGlyph;
        e.Handled = true;

        // Persist the new state into the model so a subsequent save reflects the toggle.
        FindOwnerView(wpf)?.CommitToModel();
    }

    /// <summary>Walks up from an inline to the hosting <see cref="DocumentView"/>, if any.</summary>
    private static DocumentView? FindOwnerView(Inline inline)
    {
        var flow = FindFlowDocument(inline);
        return flow?.Parent as DocumentView;
    }

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

    /// <summary>
    /// Renders an endnote reference as a small superscript marker showing the endnote number, tagged
    /// with an <see cref="EndnoteMarker"/> so <see cref="ReadInline"/> can recover the id on commit.
    /// A tooltip surfaces the endnote text when the document carries it. Mirrors
    /// <see cref="BuildFootnoteReference"/>.
    /// </summary>
    private static WpfRun BuildEndnoteReference(int endnoteId, TextDocument document)
    {
        var marker = new WpfRun(endnoteId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            BaselineAlignment = BaselineAlignment.Superscript,
            FontSize = (document.DefaultRun.FontSizePt ?? DefaultFontSizePt) * PxPerPoint * SuperSubScale,
            Tag = new EndnoteMarker(endnoteId)
        };
        if (document.Endnotes.TryGetValue(endnoteId, out var endnote) && endnote.PlainText is { Length: > 0 } text)
            marker.ToolTip = text;
        return marker;
    }

    /// <summary>Carried on an endnote-marker WPF run's Tag so CommitToModel can round-trip its id.</summary>
    private sealed record EndnoteMarker(int EndnoteId);

    /// <summary>
    /// Renders a document field run (DATE/TIME/FILENAME/AUTHOR/NUMPAGES/PAGE) as a WPF run showing the
    /// resolved value, tagged with a <see cref="FieldMarker"/> so <see cref="ReadInline"/> can recover
    /// the kind on commit. DATE/TIME resolve to the current date/time in this app layer (never in the
    /// model/IO); AUTHOR comes from the document properties; FILENAME from the current file name; the
    /// rest fall back to the run's cached text. The marker keeps the original cached text so an unsaved
    /// FILENAME (or an unresolved field) round-trips its last-known value rather than going blank.
    /// </summary>
    private static WpfRun BuildFieldRun(ModelRun run, TextDocument document)
    {
        var display = ResolveFieldText(run.FieldKind, run.Text, document, _renderFileName);
        var fmt = run.Formatting ?? document.DefaultRun;
        var wpf = new WpfRun(display)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal,
            Tag = new FieldMarker(run.FieldKind, run.Text)
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        if (fmt.FontSizePt is { } size)
            wpf.FontSize = size * PxPerPoint;
        if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);
        wpf.ToolTip = run.FieldKind + " field";
        return wpf;
    }

    /// <summary>
    /// Resolves a field's display text in the app layer. DATE/TIME use the current date/time; AUTHOR uses
    /// <see cref="DocumentProperties.Author"/>; FILENAME uses <paramref name="fileName"/>; PAGE/NUMPAGES
    /// and any unresolved value fall back to <paramref name="cached"/> (the last-computed text). This is
    /// the only place date/time is read — the model and docx IO stay deterministic.
    /// </summary>
    private static string ResolveFieldText(RunFieldKind kind, string cached, TextDocument document, string? fileName)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return kind switch
        {
            RunFieldKind.Date => DateTime.Now.ToString("d", culture),
            RunFieldKind.Time => DateTime.Now.ToString("t", culture),
            RunFieldKind.Author => document.Properties.Author is { Length: > 0 } author ? author : cached,
            RunFieldKind.FileName => fileName is { Length: > 0 } name ? name : cached,
            _ => cached
        };
    }

    /// <summary>
    /// Carried on a field WPF run's Tag so CommitToModel can round-trip the field kind and its cached
    /// (last-computed) text. The WPF run's visible text is the resolved value; the cached text is what
    /// the model keeps so a re-resolve next render is possible and field-unaware consumers still render.
    /// </summary>
    private sealed record FieldMarker(RunFieldKind Kind, string Cached);

    /// <summary>
    /// Inserts a document field run of the given <paramref name="kind"/> at the caret. The field is built
    /// with an initially-resolved cached value (DATE/TIME/AUTHOR/FILENAME) so it carries a sensible
    /// fallback even before the next render; it then round-trips through the model and docx as a field.
    /// </summary>
    public void InsertField(RunFieldKind kind)
    {
        Focus();
        if (kind == RunFieldKind.None)
            return;
        var cached = ResolveFieldText(kind, string.Empty, _model, CurrentFileName);
        var run = new ModelRun(cached) { FieldKind = kind };
        var inline = BuildFieldRun(run, _model);
        InsertInlineAtCaret(inline);
    }

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
    /// Inserts an endnote at the caret: allocates the next endnote id, stores <paramref name="text"/>
    /// as the endnote's content in the model, and drops a superscript reference marker at the caret.
    /// Re-renders so the marker round-trips through the model on the next commit. Mirrors
    /// <see cref="InsertFootnote"/> but collected at the document end (word/endnotes.xml).
    /// </summary>
    public void InsertEndnote(string text)
    {
        CommitToModel();

        var id = _model.NextEndnoteId();
        var endnote = new Endnote(id);
        endnote.Content.Add(new ModelParagraph(text));
        _model.Endnotes[id] = endnote;

        var marker = BuildEndnoteReference(id, _model);
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
    /// Inserts a plain-text content control (w:sdt) at the caret. When the selection is non-empty its
    /// text becomes the control's content; otherwise a placeholder ("Click to enter text") is used. The
    /// control carries the optional <paramref name="tag"/> / <paramref name="alias"/> and renders as a
    /// shaded region. Re-renders so the control round-trips on the next commit/save.
    /// </summary>
    public void InsertPlainTextControl(string? tag = null, string? alias = null)
    {
        Focus();

        var selected = Selection?.Text;
        var text = string.IsNullOrEmpty(selected) ? "Click to enter text" : selected;
        if (Selection is { IsEmpty: false })
            Selection.Text = string.Empty;

        var run = BuildControlRun(ModelRun.PlainTextControl(text, tag, alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>
    /// Inserts a checkbox content control (w:sdt) at the caret, initially unchecked. The control carries
    /// the optional <paramref name="tag"/> / <paramref name="alias"/>; its run shows the ☐ glyph and
    /// toggles to ☒ on click. Re-renders so the control round-trips on the next commit/save.
    /// </summary>
    public void InsertCheckBoxControl(string? tag = null, string? alias = null)
    {
        Focus();
        var run = BuildControlRun(ModelRun.CheckBoxControl(@checked: false, tag, alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>Builds the WPF inline for a content-control model run (shaded region + marker tag).</summary>
    private Inline BuildControlRun(ModelRun run) => BuildRun(run, new ModelParagraph(), _model);

    /// <summary>
    /// Inserts a freshly built inline at the caret (or appends to the last paragraph), then commits and
    /// re-renders so the new run round-trips through the model. Shared by the content-control inserts.
    /// </summary>
    private void InsertInlineAtCaret(Inline inline)
    {
        CommitToModel();

        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
        if (paragraph is null)
        {
            paragraph = new WpfParagraph();
            Document.Blocks.Add(paragraph);
        }
        paragraph.Inlines.Add(inline);

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

    /// <summary>The document's bibliographic sources (Insert &gt; Citation reads/writes this list).</summary>
    public IReadOnlyList<Source> Sources
    {
        get
        {
            CommitToModel();
            return _model.Sources;
        }
    }

    /// <summary>
    /// Appends a new bibliographic source to the model and returns it, so a caller can immediately insert
    /// its in-text citation (see <see cref="InsertCitation(Source)"/>). Does not touch the visible flow.
    /// </summary>
    public Source AddSource(string tag, string author, string title, string year, string? publisher)
    {
        CommitToModel();
        var source = new Source
        {
            Tag = tag?.Trim() ?? string.Empty,
            Author = author?.Trim() ?? string.Empty,
            Title = title?.Trim() ?? string.Empty,
            Year = year?.Trim() ?? string.Empty,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim()
        };
        _model.Sources.Add(source);
        return source;
    }

    /// <summary>
    /// Inserts the in-text citation for <paramref name="source"/> (e.g. <c>(Author, Year)</c>, formatted
    /// by <see cref="Citations.FormatInText(Source)"/>) as ordinary text at the caret, flowing through the
    /// RichTextBox's own edit path so it joins the surrounding run and is captured by the undo stack.
    /// </summary>
    public void InsertCitation(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        InsertText(Citations.FormatInText(source));
    }

    /// <summary>
    /// Insert a bibliography generated from the document's <see cref="TextDocument.Sources"/> at the
    /// caret's block (else at the document end), routed one-by-one through the undo/redo bus so the insert
    /// is reversible — mirroring <see cref="InsertTableOfContents"/>. The paragraphs carry dedicated
    /// bibliography styles (registered via <see cref="Citations.EnsureStyles"/>) which both give them
    /// distinct formatting and mark the region.
    /// </summary>
    public void InsertBibliography()
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        Citations.EnsureStyles(_model);

        // Insert at the caret's block (a bibliography reads as back-matter); fall back to the document end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        var bibliography = Citations.BuildBibliography(_model);
        foreach (var paragraph in bibliography)
            _commands.Execute(new InsertParagraphCommand(index++, paragraph));
    }

    /// <summary>
    /// Marks <paramref name="term"/> for the document index (appends it to
    /// <see cref="TextDocument.IndexEntries"/>). Blank terms and exact case-insensitive duplicates are
    /// ignored so the side-store stays clean; the generated index also de-duplicates. Does not touch the
    /// visible flow.
    /// </summary>
    public void MarkIndexEntry(string term)
    {
        CommitToModel();
        var trimmed = term?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return;
        if (_model.IndexEntries.Any(e => string.Equals(e.Term, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        _model.IndexEntries.Add(new IndexEntry(trimmed));
    }

    /// <summary>
    /// Insert an index generated from the document's marked <see cref="TextDocument.IndexEntries"/> at the
    /// caret's block (else at the document end), routed one-by-one through the undo/redo bus so the insert
    /// is reversible — mirroring <see cref="InsertBibliography"/>. The paragraphs carry dedicated index
    /// styles (registered via <see cref="DocumentIndex.EnsureStyles"/>) which both give them distinct
    /// formatting and mark the region.
    /// </summary>
    public void InsertIndex()
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        DocumentIndex.EnsureStyles(_model);

        // Insert at the caret's block (an index reads as back-matter); fall back to the document end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        var entries = DocumentIndex.Build(_model);
        foreach (var paragraph in entries)
            _commands.Execute(new InsertParagraphCommand(index++, paragraph));
    }

    /// <summary>
    /// True when the caret currently sits inside a table block. Used by the Insert Caption command to
    /// default the caption label to <see cref="CaptionLabel.Table"/> for tables (else Figure).
    /// </summary>
    public bool IsCaretInTable() => CaretTableLocation().BlockIndex >= 0;

    /// <summary>
    /// Insert a numbered caption (e.g. "Figure 1: My diagram") of <paramref name="label"/> with the
    /// given <paramref name="text"/> after the block the caret sits in (so it reads under the selected
    /// image/table), else at the document end. The next ordinal is computed by counting the document's
    /// existing captions of that label (see <see cref="Captions.NextCaptionNumber"/>), and the caption
    /// is a single <c>Caption</c>-styled paragraph routed through the undo/redo bus so it is reversible.
    /// </summary>
    public void InsertCaption(CaptionLabel label, string text)
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        Captions.EnsureStyles(_model);

        var number = Captions.NextCaptionNumber(_model, label);
        var caption = Captions.BuildCaption(label, number, text);

        // Insert after the caret's block so the caption sits under the selected image/table.
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertParagraphCommand(index, caption));
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

    /// <summary>
    /// Inserts <paramref name="display"/> at the caret as a clickable internal link to the bookmark
    /// <paramref name="anchor"/> (used by Insert &gt; Cross-reference for anchored targets). Mirrors the
    /// empty-selection branch of <see cref="ApplyInternalLink"/> but lets the visible text differ from
    /// the anchor name. Re-renders so the link is styled and round-trips on the next commit.
    /// </summary>
    public void InsertInternalLink(string display, string anchor)
    {
        if (string.IsNullOrEmpty(display) || string.IsNullOrWhiteSpace(anchor))
            return;
        anchor = anchor.Trim();

        Focus();
        var selection = Selection;
        if (!selection.IsEmpty)
            selection.Text = string.Empty;

        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
        if (paragraph is null)
        {
            paragraph = new WpfParagraph();
            Document.Blocks.Add(paragraph);
        }
        var link = new WpfHyperlink(new WpfRun(display));
        StyleInternalLink(link, anchor);
        paragraph.Inlines.Add(link);

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

    private static ParagraphFormatting ReadParagraphFormatting(WpfParagraph paragraph, TextDocument document)
    {
        var pageBreakBefore = paragraph.Tag is ParagraphTag { PageBreakBefore: true };
        return ParagraphFormatting.Default with
        {
            Alignment = FromWpfAlignment(paragraph.TextAlignment),
            SpaceBeforePt = paragraph.Margin.Top / PxPerPoint,
            SpaceAfterPt = paragraph.Margin.Bottom / PxPerPoint,
            LineSpacing = ReadLineSpacing(paragraph.LineHeight, document),
            IndentLeftPt = paragraph.Margin.Left / PxPerPoint,
            IndentRightPt = paragraph.Margin.Right / PxPerPoint,
            FirstLineIndentPt = paragraph.TextIndent / PxPerPoint,
            Border = ReadParagraphBorder(paragraph, pageBreakBefore),
            PageBreakBefore = pageBreakBefore,
            ShadingColorHex = paragraph.Background is SolidColorBrush shading ? ToHex(shading.Color) : null,
            // Tab stops are not representable in the WPF FlowDocument Paragraph, so they are preserved
            // verbatim from the Tag stamped by BuildParagraph (see comment there); empty if none.
            TabStops = paragraph.Tag is ParagraphTag { TabStops: var tabStops } ? tabStops : []
        };
    }

    // Recover the model paragraph border from a WPF paragraph's BorderBrush/BorderThickness. A bottom-only
    // thickness (top edge off, bottom edge on) is a horizontal rule; an all-edges thickness is a box.
    // When the paragraph carries a forced page break we render a synthetic top edge for it (see
    // BuildParagraph), so a top-only border is ignored here — it is page-break chrome, not a real border.
    private static ParagraphBorder? ReadParagraphBorder(WpfParagraph paragraph, bool pageBreakBefore)
    {
        if (paragraph.BorderBrush is not SolidColorBrush bb)
            return null;
        var t = paragraph.BorderThickness;
        var bottomOnly = t.Bottom > 0 && t.Top <= 0 && t.Left <= 0 && t.Right <= 0;
        if (bottomOnly)
            return new ParagraphBorder(ToHex(bb.Color), t.Bottom / PxPerPoint, BottomOnly: true);
        // A page break renders as a top-only edge; that is not a user border, so drop it.
        if (pageBreakBefore && t.Top > 0 && t.Bottom <= 0 && t.Left <= 0 && t.Right <= 0)
            return null;
        return t.Top > 0 ? new ParagraphBorder(ToHex(bb.Color), t.Top / PxPerPoint) : null;
    }

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
