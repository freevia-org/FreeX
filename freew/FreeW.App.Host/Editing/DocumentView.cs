using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

    /// <summary>The "plain"/continuous view padding (the original flat-text-box look) restored when Print Layout is off.</summary>
    private static readonly Thickness PlainPadding = new(48);

    /// <summary>The page drop shadow applied to the editing surface in Print-Layout mode (a soft, Word-like page lift).</summary>
    private static readonly DropShadowEffect PageShadow = CreatePageShadow();

    // The live overlay drawing faint "— Page N —" break markers down the surface in Print-Layout mode,
    // or null while Print Layout is off. Added to / removed from this control's AdornerLayer so it never
    // participates in the FlowDocument content (and so never round-trips through CommitToModel). It reads
    // geometry from the current Document + model PageSettings and is recomputed cheaply on relayout.
    private PageBreakAdorner? _pageBreakAdorner;

    private static DropShadowEffect CreatePageShadow()
    {
        var shadow = new DropShadowEffect
        {
            Color = Color.FromRgb(0x80, 0x80, 0x80),
            BlurRadius = 14,
            ShadowDepth = 3,
            Direction = 270,
            Opacity = 0.55
        };
        shadow.Freeze();
        return shadow;
    }

    /// <summary>
    /// Holds the run + paragraph formatting captured when Format Painter is armed (null when the
    /// painter is idle). On the next selection the user makes, this is stamped onto that selection
    /// and the painter disarms. See <see cref="ArmFormatPainter"/>.
    /// </summary>
    private FormatPainterClipboard? _formatPainter;

    /// <summary>
    /// Model block indices of headings the user has collapsed in the outline. Collapse is purely a
    /// view concern: while a heading is collapsed, <see cref="Render"/> skips building the body blocks
    /// beneath it (down to the next same-or-higher heading), and <see cref="CommitToModel"/> re-inserts
    /// those hidden model blocks so the model document stays complete. Toggling re-renders.
    /// </summary>
    private readonly HashSet<int> _collapsedHeadings = new();

    /// <summary>
    /// Model blocks the most recent <see cref="Render"/> hid because of <see cref="_collapsedHeadings"/>,
    /// each tagged with the number of <em>visible</em> blocks that preceded it at render time. On the
    /// next <see cref="CommitToModel"/> these are spliced back into the rebuilt model at the matching
    /// visible offset, so a collapsed region survives an edit/commit cycle intact. Empty when nothing
    /// is collapsed (the common case), so normal commit is completely unaffected.
    /// </summary>
    private readonly List<(int VisibleOffset, ModelBlock Block)> _hiddenBlocks = new();

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
    /// Whether the editor presents a Word-style "Print Layout" page view: the editable surface is sized to
    /// the model page width, the page margins (<see cref="PageSettings"/>) become the editor padding, the
    /// page gets a soft drop shadow, and faint "— Page N —" break markers are drawn down the flow. When
    /// off, the surface reverts to the original flat/continuous look (a comfortable fixed padding, no width
    /// cap, no shadow, no markers). Default ON — the Word default and the showcase view. Purely visual:
    /// the model, saved document, zoom, read mode and all editing commands are unaffected; the surface
    /// stays a single live, fully editable <see cref="RichTextBox"/> either way (see the limitation note in
    /// <see cref="ApplyPageChrome"/>). Re-applied on every <see cref="Render"/> and when page settings change.
    /// </summary>
    public bool PrintLayoutEnabled { get; private set; } = true;

    /// <summary>
    /// Turn Print-Layout page view on/off and return the new state. Used by the View ribbon's "Print
    /// Layout" toggle. Re-applies the page chrome (padding/width/shadow) and the page-break overlay so the
    /// change shows immediately; never mutates the model.
    /// </summary>
    public bool TogglePrintLayout()
    {
        PrintLayoutEnabled = !PrintLayoutEnabled;
        ApplyPageChrome();
        SyncPageBreakAdorner();
        return PrintLayoutEnabled;
    }

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

    /// <summary>
    /// Whether the editor's built-in spell checking (red squiggles) is on. Mirrors
    /// <see cref="System.Windows.Controls.SpellCheck.IsEnabled"/> on this control so the Review ribbon's
    /// Spell Check toggle can flip it and read it back.
    /// </summary>
    public bool SpellCheckEnabled
    {
        get => SpellCheck.IsEnabled;
        set => SpellCheck.IsEnabled = value;
    }

    /// <summary>Turn the editor's spell checking on/off and return the new state. Used by the Review ribbon.</summary>
    public bool ToggleSpellCheck()
    {
        SpellCheck.IsEnabled = !SpellCheck.IsEnabled;
        return SpellCheck.IsEnabled;
    }

    /// <summary>
    /// Register a custom dictionary (<c>.lex</c>) file with this control's spell checker so the words it
    /// contains stop being flagged as misspellings. WPF reads the file at registration time, so callers
    /// add the file path once after the on-disk dictionary exists; re-registering after the file changes
    /// (see <see cref="RefreshCustomDictionary"/>) picks up newly added words. A null/blank/non-existent
    /// path is ignored, and a duplicate registration is skipped. Best-effort: a failure to register never
    /// disrupts editing.
    /// </summary>
    public void RegisterCustomDictionary(string? lexFilePath)
    {
        if (string.IsNullOrWhiteSpace(lexFilePath) || !File.Exists(lexFilePath))
            return;
        try
        {
            var uri = new Uri(lexFilePath, UriKind.Absolute);
            var dictionaries = SpellCheck.CustomDictionaries;
            if (!dictionaries.Contains(uri))
                dictionaries.Add(uri);
            _customDictionaryUri = uri;
        }
        catch
        {
            // Registering a custom dictionary is best-effort; never block editing on it.
        }
    }

    // The currently-registered custom dictionary Uri (null until RegisterCustomDictionary succeeds),
    // remembered so RefreshCustomDictionary can drop and re-add it to reload the file's contents.
    private Uri? _customDictionaryUri;

    /// <summary>
    /// Re-read the registered custom dictionary file so words just added to it stop being flagged. WPF
    /// snapshots the <c>.lex</c> file when it is added to <c>CustomDictionaries</c>, so to pick up new
    /// words we remove and re-add the same Uri. No-op when no dictionary has been registered yet.
    /// </summary>
    public void RefreshCustomDictionary()
    {
        if (_customDictionaryUri is not { } uri)
            return;
        try
        {
            var dictionaries = SpellCheck.CustomDictionaries;
            if (dictionaries.Contains(uri))
                dictionaries.Remove(uri);
            if (File.Exists(uri.LocalPath))
                dictionaries.Add(uri);
        }
        catch
        {
            // Best-effort refresh; leave the existing registration in place on failure.
        }
    }

    /// <summary>
    /// The misspelled word the caret currently sits in/next to, or null when the caret is not on a
    /// spelling error (or spell checking is off). Used by the Review ribbon's "Add to Dictionary"
    /// command to learn which word to add. Reads the WPF <see cref="SpellingError"/> at the caret and
    /// returns the underlying text via the surrounding <see cref="TextRange"/>.
    /// </summary>
    public string? MisspelledWordAtCaret()
    {
        if (!SpellCheck.IsEnabled || CaretPosition is not { } caret)
            return null;

        // GetSpellingErrorRange returns the TextRange covering the flagged word, or null when the
        // position is not on a spelling error. Probe the caret, then a position just before it, so a
        // caret resting at the end of a misspelling still finds the word.
        var range = GetSpellingErrorRange(caret)
            ?? (caret.GetNextInsertionPosition(LogicalDirection.Backward) is { } prev
                ? GetSpellingErrorRange(prev)
                : null);
        if (range is null)
            return null;

        var word = range.Text?.Trim();
        return string.IsNullOrEmpty(word) ? null : word;
    }

    /// <summary>Raised whenever <see cref="ZoomLevel"/> changes; carries the new factor (1.0 == 100%).</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// Raised whenever the page chrome is (re)applied — i.e. when the page size/margins change, Print
    /// Layout is toggled, or the document re-renders. Lets passive view chrome that mirrors the page
    /// geometry (e.g. the <see cref="Ruler"/>) redraw without polling. Purely a view-layout signal; it
    /// never implies a model change.
    /// </summary>
    public event EventHandler? LayoutChanged;

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
    /// Re-render the surface after the document's <see cref="TextDocument.Styles"/> catalog has been
    /// mutated out-of-band (e.g. a style created/modified/deleted via <see cref="StyleManager"/>), so the
    /// new run/paragraph formatting resolves for any paragraph referencing the affected style. Commits the
    /// in-progress edits first, mirroring <see cref="ApplyTheme"/>.
    /// </summary>
    public void RefreshStyles()
    {
        CommitToModel();
        Render();
    }

    // --- Live preview (galleries) ---------------------------------------------------------------
    //
    // The Styles / Themes galleries preview a choice while the pointer hovers a swatch and revert it
    // when the pointer leaves (unless the user clicks, which commits through the normal reversible
    // path). Preview deliberately bypasses the undo/redo bus: it mutates the model in place and
    // re-renders, snapshotting exactly what it changed so EndPreview can restore the document to its
    // pre-hover state without touching undo history. Commit (the real apply) is a separate, reversible
    // operation the gallery triggers on click — preview only ever shows, never persists.

    // Snapshot of the paragraph StyleIds a style preview overwrote (model index -> prior style id).
    private Dictionary<int, string?>? _styleStyleIdSnapshot;

    // The model paragraph indices a style-preview session targets. Captured from the selection when the
    // session starts (first hover) and reused for every subsequent hover, so re-rendering between hovers
    // (which clears the editor selection) doesn't make later previews target nothing.
    private IReadOnlyList<int>? _stylePreviewTargets;

    // Snapshot of the document's pre-theme look (DefaultRun + each affected style's Run) for theme preview.
    private (RunFormatting DefaultRun, Dictionary<string, RunFormatting> Runs)? _themeSnapshot;

    /// <summary>
    /// Preview a paragraph style on the current selection without committing: snapshot the selected
    /// paragraphs' current <see cref="ModelParagraph.StyleId"/>, set the previewed id on each, and
    /// re-render so the style's formatting shows. <see cref="EndStylePreview"/> restores them. A no-op
    /// for an unknown style id. Used by the Styles gallery's hover live-preview.
    /// </summary>
    public void PreviewParagraphStyle(string? styleId)
    {
        if (styleId is { Length: > 0 } && !_model.Styles.ContainsKey(styleId))
            return;

        // Re-baseline against the committed model: on the first hover of a session commit pending edits
        // and capture the target paragraphs from the selection; on a subsequent hover revert the prior
        // preview and reuse the captured targets (the re-render between hovers clears the selection).
        if (_styleStyleIdSnapshot is null)
        {
            CommitToModel();
            _stylePreviewTargets = SelectedModelParagraphIndices();
        }
        else
        {
            RestoreStylePreview();
        }

        var snapshot = new Dictionary<int, string?>();
        foreach (var index in _stylePreviewTargets ?? [])
        {
            if (index >= 0 && index < _model.Blocks.Count && _model.Blocks[index] is ModelParagraph paragraph)
            {
                snapshot[index] = paragraph.StyleId;
                paragraph.StyleId = styleId;
            }
        }

        _styleStyleIdSnapshot = snapshot;
        Render();
    }

    /// <summary>Revert a style preview started by <see cref="PreviewParagraphStyle"/> and re-render. No-op if none is active.</summary>
    public void EndStylePreview()
    {
        if (_styleStyleIdSnapshot is null)
            return;
        RestoreStylePreview();
        _stylePreviewTargets = null;
        Render();
    }

    // Restore previewed paragraph style ids from the snapshot (without re-rendering).
    private void RestoreStylePreview()
    {
        if (_styleStyleIdSnapshot is null)
            return;
        foreach (var (index, styleId) in _styleStyleIdSnapshot)
        {
            if (index >= 0 && index < _model.Blocks.Count && _model.Blocks[index] is ModelParagraph paragraph)
                paragraph.StyleId = styleId;
        }
        _styleStyleIdSnapshot = null;
    }

    /// <summary>
    /// Preview a document <paramref name="theme"/> without committing: snapshot the document default run
    /// and the run formatting of every style the theme rewrites, apply the theme to the catalog, and
    /// re-render. <see cref="EndThemePreview"/> restores the snapshot. Used by the Themes gallery's hover
    /// live-preview; the real apply goes through <see cref="ApplyTheme"/> on click.
    /// </summary>
    public void PreviewTheme(DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (_themeSnapshot is null)
            CommitToModel();
        else
            RestoreThemePreview();

        var runs = new Dictionary<string, RunFormatting>();
        foreach (var id in new[] { "Normal", "Title", "Heading1", "Heading2", "Heading3" })
        {
            if (_model.Styles.TryGetValue(id, out var style))
                runs[id] = style.Run;
        }

        _themeSnapshot = (_model.DefaultRun, runs);
        DocumentTheme.Apply(_model, theme);
        Render();
    }

    /// <summary>Revert a theme preview started by <see cref="PreviewTheme"/> and re-render. No-op if none is active.</summary>
    public void EndThemePreview()
    {
        if (_themeSnapshot is null)
            return;
        RestoreThemePreview();
        Render();
    }

    // Restore the pre-preview document default + style runs from the theme snapshot (without re-rendering).
    private void RestoreThemePreview()
    {
        if (_themeSnapshot is not { } snapshot)
            return;
        _model.DefaultRun = snapshot.DefaultRun;
        foreach (var (id, run) in snapshot.Runs)
        {
            if (_model.Styles.TryGetValue(id, out var style))
                style.Run = run;
        }
        _themeSnapshot = null;
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
    /// Insert the body content of another document (<paramref name="source"/>, typically a just-opened
    /// .docx) at the caret. The source's blocks are deep-cloned via <see cref="DocumentMerge.CloneBlocks"/>
    /// (so the source is never aliased), then each clone is inserted after the caret's block — one
    /// reversible <see cref="InsertBlockCommand"/> per block, in order — and the surface re-renders.
    /// Any named styles the source defines that the target lacks are also brought over so the inserted
    /// paragraphs resolve their styling (existing target styles are never overwritten).
    /// </summary>
    public void InsertDocument(TextDocument source)
    {
        if (source is null)
            return;

        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();

        // Bring over any styles the source has that the target is missing, so style-referencing
        // paragraphs (e.g. Heading1) render correctly. Never clobber a style the target already defines.
        foreach (var (id, style) in source.Styles)
            _model.Styles.TryAdd(id, style);

        var clones = DocumentMerge.CloneBlocks(source);

        // Insert after the block the caret sits in (else at the end), keeping document order.
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        foreach (var block in clones)
            _commands.Execute(new InsertBlockCommand(index++, block));
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
    /// Merge the table cells spanned by the current selection. When the selection covers several cells
    /// in one row, they merge horizontally (the left cell's <c>GridSpan</c> grows, the rest are dropped).
    /// When it covers several rows in one column, they merge vertically (top cell becomes the merge head,
    /// the cells below become continuations). Routes through the undo/redo bus. No-op outside a table or
    /// when the selection touches a single cell. Mixed row+column selections fall back to merging
    /// horizontally within the start row.
    /// </summary>
    public void MergeSelectedCells()
    {
        CommitToModel();
        var start = TableLocationOf(Selection.Start.Parent as TextElement);
        var end = TableLocationOf(Selection.End.Parent as TextElement);

        // Fall back to the caret cell when an endpoint is outside any table (e.g. collapsed selection).
        if (start.BlockIndex < 0)
            start = CaretTableLocation();
        if (end.BlockIndex < 0)
            end = start;
        if (start.BlockIndex < 0 || start.BlockIndex != end.BlockIndex)
            return;

        var blockIndex = start.BlockIndex;
        if (start.RowIndex == end.RowIndex && start.ColumnIndex != end.ColumnIndex)
        {
            _commands.Execute(new MergeCellsHorizontalCommand(blockIndex, start.RowIndex, start.ColumnIndex, end.ColumnIndex));
        }
        else if (start.ColumnIndex == end.ColumnIndex && start.RowIndex != end.RowIndex)
        {
            _commands.Execute(new MergeCellsVerticalCommand(blockIndex, start.ColumnIndex, start.RowIndex, end.RowIndex));
        }
        else if (start.RowIndex != end.RowIndex && start.ColumnIndex != end.ColumnIndex)
        {
            // Mixed rectangular selection: merge horizontally across the start row as a best-effort.
            _commands.Execute(new MergeCellsHorizontalCommand(blockIndex, start.RowIndex, start.ColumnIndex, end.ColumnIndex));
        }
    }

    /// <summary>
    /// Split the merged cell at the caret back into single cells: a horizontal merge resets the cell's
    /// <c>GridSpan</c> to 1 (re-adding empty cells), and a vertical merge clears the head and its
    /// continuations. Routes through the undo/redo bus. No-op outside a table or on an unmerged cell.
    /// </summary>
    public void SplitCell() => MutateCaretTable((index, rowIndex, columnIndex) =>
        new SplitCellCommand(index, rowIndex, columnIndex));

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
    /// Toggle the header-row style (bold + shaded first row) on the table containing the caret. Commits
    /// pending edits, flips <see cref="TableFormatting.HeaderRow"/> on the model table, and re-renders so
    /// the styling shows immediately and round-trips through save. No-op outside a table.
    /// </summary>
    public void ToggleTableHeaderRow() =>
        UpdateCaretTableFormatting(f => f with { HeaderRow = !f.HeaderRow });

    /// <summary>
    /// Toggle banded-row shading (alternate body rows shaded) on the table containing the caret. Commits
    /// pending edits, flips <see cref="TableFormatting.BandedRows"/>, and re-renders. No-op outside a table.
    /// </summary>
    public void ToggleTableBandedRows() =>
        UpdateCaretTableFormatting(f => f with { BandedRows = !f.BandedRows });

    /// <summary>
    /// Toggle whether the header (first) row repeats across page breaks on the table containing the caret.
    /// Commits pending edits, flips <see cref="TableFormatting.RepeatHeaderRow"/>, and re-renders. No-op
    /// outside a table.
    /// </summary>
    public void ToggleTableRepeatHeaderRow() =>
        UpdateCaretTableFormatting(f => f with { RepeatHeaderRow = !f.RepeatHeaderRow });

    /// <summary>
    /// Apply <paramref name="update"/> to the formatting of the table containing the caret (direct model
    /// set + re-render), mirroring <see cref="SetCaretCellShading"/>. No-op outside a table.
    /// </summary>
    private void UpdateCaretTableFormatting(Func<TableFormatting, TableFormatting> update)
    {
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        table.Formatting = update(table.Formatting);
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
    /// Set (or clear, when null/empty) the accessibility alt text on the currently selected inline image.
    /// Mutates the model image in place — the alt text is carried by the image instance, so it survives
    /// the next <see cref="CommitToModel"/> — then re-renders so the tooltip/automation name refresh.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageAltText(string? altText)
    {
        CommitToModel();
        var image = SelectedImageLocation().Image;
        if (image is null)
            return;
        image.AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        Render();
    }

    /// <summary>
    /// Align the paragraph that contains the currently selected inline image. "Image alignment" is the
    /// alignment of its (image-only) paragraph, which round-trips through the existing
    /// <see cref="ParagraphFormatting.Alignment"/> infrastructure. No-op without an image selection.
    /// </summary>
    public void SetSelectedImageAlignment(ModelTextAlignment alignment)
    {
        CommitToModel();
        var (blockIndex, _, image) = SelectedImageLocation();
        if (image is null || blockIndex < 0 || _model.Blocks[blockIndex] is not ModelParagraph paragraph)
            return;
        paragraph.Formatting = paragraph.Formatting with { Alignment = alignment };
        Render();
    }

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
    /// Toggle "keep with next" (pPr/w:keepNext) over the selected paragraphs. If any spanned paragraph
    /// lacks the flag, all get it; otherwise it is cleared. Reversible via the undo/redo bus.
    /// </summary>
    public void ToggleKeepWithNext()
    {
        var enable = SelectedModelParagraphs().Any(p => !p.Formatting.KeepWithNext);
        FormatSelectedModelParagraphs(f => f with { KeepWithNext = enable });
    }

    /// <summary>
    /// Toggle "keep lines together" (pPr/w:keepLines) over the selected paragraphs. If any spanned
    /// paragraph lacks the flag, all get it; otherwise it is cleared. Reversible via the undo/redo bus.
    /// </summary>
    public void ToggleKeepLinesTogether()
    {
        var enable = SelectedModelParagraphs().Any(p => !p.Formatting.KeepLinesTogether);
        FormatSelectedModelParagraphs(f => f with { KeepLinesTogether = enable });
    }

    /// <summary>
    /// Toggle widow/orphan control (pPr/w:widowControl) over the selected paragraphs. If any spanned
    /// paragraph lacks the flag, all get it; otherwise it is cleared. Reversible via the undo/redo bus.
    /// </summary>
    public void ToggleWidowControl()
    {
        var enable = SelectedModelParagraphs().Any(p => !p.Formatting.WidowControl);
        FormatSelectedModelParagraphs(f => f with { WidowControl = enable });
    }

    /// <summary>
    /// Apply (or toggle off) multilevel/legal outline numbering over the selection. If any spanned
    /// paragraph is not already a <see cref="ListKind.MultiLevel"/> list, all become multilevel lists
    /// (preserving their <see cref="ParagraphFormatting.ListLevel"/>); otherwise the list decoration is
    /// cleared back to <see cref="ListKind.None"/>. Reversible via the undo/redo bus, then re-rendered.
    /// The numbering definition persists to word/numbering.xml as an outline abstract num.
    /// </summary>
    public void ApplyMultiLevelList()
    {
        var enable = SelectedModelParagraphs().Any(p => p.Formatting.ListKind != ListKind.MultiLevel);
        FormatSelectedModelParagraphs(f => f with
        {
            ListKind = enable ? ListKind.MultiLevel : ListKind.None,
            ListLevel = enable ? f.ListLevel : 0
        });
    }

    /// <summary>
    /// Change the outline depth (<see cref="ParagraphFormatting.ListLevel"/>) of every list paragraph
    /// spanned by the selection by <paramref name="delta"/> (e.g. +1 to demote on Tab, -1 to promote on
    /// Shift+Tab), clamped to 0..8. Non-list paragraphs are unaffected. Reversible via the bus.
    /// </summary>
    public void ChangeListLevel(int delta) =>
        FormatSelectedModelParagraphs(f => f.ListKind == ListKind.None
            ? f
            : f with { ListLevel = Math.Clamp(f.ListLevel + delta, 0, 8) });

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
    /// The <see cref="ParagraphFormatting"/> of the first paragraph spanned by the current selection (the
    /// caret's paragraph), or <see cref="ParagraphFormatting.Default"/> when none is selected. Read-only;
    /// used by the <see cref="Ruler"/> to reflect the current paragraph's indents and tab stops. Does not
    /// commit pending edits (cheap, called on selection change) — the indent/tab markers are advisory.
    /// </summary>
    public ParagraphFormatting CurrentParagraphFormatting =>
        SelectedModelParagraphs().FirstOrDefault()?.Formatting ?? ParagraphFormatting.Default;

    /// <summary>
    /// An approximate "Page X of Y" for the status bar: the page the caret currently sits on, and the
    /// total page count. Both are computed from the editable surface's single continuous flow against the
    /// page's printable content height (<see cref="PageLayout.ContentAreaDip"/>) — the same approximation
    /// the <see cref="PageBreakAdorner"/> uses (see its remarks), so the numbers track the on-screen
    /// page-break markers. It does not model keep-together rules, explicit breaks, or straddling tables, so
    /// it can differ by a page from the fully paginated Print Preview. Returns (1, 1) when geometry is not
    /// yet available (e.g. before first layout) so callers always have a sane value.
    /// </summary>
    public (int Current, int Total) PageInfo()
    {
        if (Document is not { } doc)
            return (1, 1);

        var (_, contentHeight) = PageLayout.ContentAreaDip(_model.Page);
        if (contentHeight <= 0)
            return (1, 1);

        try
        {
            var top = doc.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;

            // Total: span from the first to the last content line, divided into page-height bands.
            var bottom = doc.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom;
            var total = Math.Max(1, (int)Math.Ceiling(Math.Max(0, bottom - top) / contentHeight));

            // Current: which band the caret's line falls into (1-based), clamped into range.
            var caret = (CaretPosition ?? doc.ContentStart).GetCharacterRect(LogicalDirection.Forward).Top;
            var current = Math.Clamp((int)Math.Floor(Math.Max(0, caret - top) / contentHeight) + 1, 1, total);
            return (current, total);
        }
        catch (InvalidOperationException)
        {
            // Layout momentarily unavailable during a relayout; report a safe default.
            return (1, 1);
        }
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
        ApplyParagraphStyleToIndices(styleId, SelectedModelParagraphIndices());
    }

    /// <summary>
    /// Commit a style chosen from the Styles gallery: if a live-preview session is active (the user
    /// hovered swatches), revert the preview and apply <paramref name="styleId"/> reversibly to the
    /// paragraphs that session targeted — even though the intervening re-renders cleared the editor
    /// selection. With no active session this is equivalent to <see cref="SetParagraphStyle"/>.
    /// </summary>
    public void CommitStylePreview(string? styleId)
    {
        if (styleId is { Length: > 0 } && !_model.Styles.ContainsKey(styleId))
            return;

        var targets = _stylePreviewTargets;
        if (_styleStyleIdSnapshot is not null)
        {
            RestoreStylePreview();
            Render();
        }
        _stylePreviewTargets = null;

        if (targets is null || targets.Count == 0)
        {
            SetParagraphStyle(styleId);
            return;
        }

        Focus();
        CommitToModel();
        ApplyParagraphStyleToIndices(styleId, targets);
    }

    // Apply a paragraph style id to the given model paragraph indices, one reversible command each.
    private void ApplyParagraphStyleToIndices(string? styleId, IReadOnlyList<int> indices)
    {
        foreach (var index in indices)
        {
            if (index >= 0 && index < _model.Blocks.Count && _model.Blocks[index] is ModelParagraph)
                _commands.Execute(new SetParagraphStyleCommand(index, styleId));
        }
    }

    /// <summary>
    /// The <see cref="ModelParagraph.StyleId"/> of the paragraph at the caret (the first paragraph in the
    /// current selection), or null when that paragraph has no explicit style or no paragraph is selected.
    /// Used by the New Style / Modify Style commands to seed the dialog (based-on / which style to edit).
    /// </summary>
    public string? CurrentParagraphStyleId
    {
        get
        {
            CommitToModel();
            var index = SelectedModelParagraphIndices().FirstOrDefault(-1);
            return index >= 0 && _model.Blocks[index] is ModelParagraph paragraph ? paragraph.StyleId : null;
        }
    }

    /// <summary>
    /// Promote the heading at <paramref name="modelBlockIndex"/> one rank toward the top of the outline
    /// (Heading3 → Heading2 → Heading1 → Title; Title stays). The paragraph's <see cref="ModelParagraph.StyleId"/>
    /// is changed through the reversible <see cref="SetParagraphStyleCommand"/> (the same path the styles
    /// dropdown uses) so it is undoable, then the view re-renders. No-op when the index is not a paragraph
    /// or the style does not change (e.g. a non-heading paragraph, which has nothing to promote).
    /// </summary>
    public void PromoteHeading(int modelBlockIndex) =>
        ShiftHeadingStyle(modelBlockIndex, OutlineTools.Promote);

    /// <summary>
    /// Demote the heading at <paramref name="modelBlockIndex"/> one rank toward the bottom of the outline
    /// (Title → Heading1 → Heading2 → … → Heading6; a non-heading paragraph becomes Heading1). Routed
    /// through the reversible <see cref="SetParagraphStyleCommand"/> and re-rendered. No-op when the index
    /// is not a paragraph or the style does not change (already at the deepest level).
    /// </summary>
    public void DemoteHeading(int modelBlockIndex) =>
        ShiftHeadingStyle(modelBlockIndex, OutlineTools.Demote);

    // Apply a pure style-id shift (promote/demote) to a single model paragraph via the undo/redo bus.
    private void ShiftHeadingStyle(int modelBlockIndex, Func<string?, string?> shift)
    {
        CommitToModel();
        if (modelBlockIndex < 0 || modelBlockIndex >= _model.Blocks.Count
            || _model.Blocks[modelBlockIndex] is not ModelParagraph paragraph)
            return;

        var next = shift(paragraph.StyleId);
        if (string.Equals(next, paragraph.StyleId, StringComparison.Ordinal))
            return; // no change (e.g. promoting Title, or demoting past the cap)

        _commands.Execute(new SetParagraphStyleCommand(modelBlockIndex, next));
    }

    /// <summary>
    /// Collapse the heading at <paramref name="modelBlockIndex"/>: its body blocks (everything down to
    /// the next same-or-higher-level heading) are hidden in the rendered view only. The model document is
    /// untouched — the hidden blocks are restored on the next commit (see <see cref="CommitToModel"/>).
    /// Re-renders. No-op when the index is not a heading paragraph.
    /// </summary>
    public void CollapseHeading(int modelBlockIndex)
    {
        if (!IsHeadingBlock(modelBlockIndex) || !_collapsedHeadings.Add(modelBlockIndex))
            return;
        CommitToModel();
        Render();
    }

    /// <summary>
    /// Expand a previously collapsed heading at <paramref name="modelBlockIndex"/>, showing its hidden
    /// body blocks again. Re-renders. No-op when the heading was not collapsed.
    /// </summary>
    public void ExpandHeading(int modelBlockIndex)
    {
        if (!_collapsedHeadings.Remove(modelBlockIndex))
            return;
        CommitToModel();
        Render();
    }

    /// <summary>True when the heading at <paramref name="modelBlockIndex"/> is currently collapsed.</summary>
    public bool IsHeadingCollapsed(int modelBlockIndex) => _collapsedHeadings.Contains(modelBlockIndex);

    // Whether the model block at the given index is a heading/title paragraph (an outline entry).
    private bool IsHeadingBlock(int modelBlockIndex) =>
        modelBlockIndex >= 0 && modelBlockIndex < _model.Blocks.Count
        && _model.Blocks[modelBlockIndex] is ModelParagraph paragraph
        && DocumentOutline.TryGetLevel(paragraph.StyleId, out _);

    // Compute the set of model block indices hidden by the currently collapsed headings. For each
    // collapsed heading, every following block is hidden until (but not including) the next heading whose
    // level is the same or higher (a smaller-or-equal level number), matching how an outline nests.
    // Collapsed headings nested inside another collapsed region stay tracked but contribute no extra
    // hidden blocks (their descendants are already hidden). A heading index that no longer points at a
    // heading (the document changed underneath us) is ignored.
    private HashSet<int> HiddenBlockIndices()
    {
        var hidden = new HashSet<int>();
        if (_collapsedHeadings.Count == 0)
            return hidden;

        var blocks = _model.Blocks;
        // Snapshot the indices so stale ones (no longer pointing at a heading) can be pruned in place.
        foreach (var headingIndex in _collapsedHeadings.ToArray())
        {
            if (headingIndex < 0 || headingIndex >= blocks.Count
                || blocks[headingIndex] is not ModelParagraph heading
                || !DocumentOutline.TryGetLevel(heading.StyleId, out var headingLevel))
            {
                _collapsedHeadings.Remove(headingIndex); // heading moved or is no longer a heading
                continue;
            }

            for (var j = headingIndex + 1; j < blocks.Count; j++)
            {
                if (blocks[j] is ModelParagraph p
                    && DocumentOutline.TryGetLevel(p.StyleId, out var level)
                    && level <= headingLevel)
                    break; // reached the next same-or-higher heading: the collapsed region ends here
                hidden.Add(j);
            }
        }
        return hidden;
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
    /// Change the case of the current selection's text per <paramref name="kind"/> (UPPERCASE, lowercase,
    /// Sentence case, Capitalize Each Word, or tOGGLE cASE), via the pure <see cref="ChangeCase.Apply"/>
    /// helper. The transformed text is written straight back over the selection (<c>Selection.Text</c>),
    /// so it flows through the RichTextBox's own edit/undo path and keeps the run formatting of the
    /// selection start (WPF behaviour when replacing selection text). No-op when the selection is empty or
    /// contains no letters to recase. The selection is re-established over the replacement text so the user
    /// can immediately apply another case.
    /// </summary>
    public void ChangeSelectionCase(CaseKind kind)
    {
        Focus();
        var selection = Selection;
        if (selection.IsEmpty)
            return;

        var original = selection.Text;
        if (string.IsNullOrEmpty(original))
            return;

        var transformed = ChangeCase.Apply(original, kind);
        if (string.Equals(original, transformed, StringComparison.Ordinal))
            return; // nothing changed (e.g. already in the target case) — leave the edit/undo stack alone

        // Remember the endpoints so the recased text stays selected after the replacement.
        var start = selection.Start;
        selection.Text = transformed;
        selection.Select(start, selection.End);
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

        // NumberLeafBlocks numbers the *visible* blocks; map each to its real _model.Blocks index so the
        // returned indices stay correct when a heading is collapsed before the selection (hidden blocks
        // are re-spliced into the model on commit — see ModelIndexFromVisible). Identity when nothing is
        // collapsed.
        var result = new List<int>();
        for (var i = Math.Min(startIndex, endIndex); i <= Math.Max(startIndex, endIndex); i++)
            result.Add(ModelIndexFromVisible(i));
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
    private (int BlockIndex, int RowIndex, int ColumnIndex) CaretTableLocation() =>
        TableLocationOf(CaretPosition?.Parent as TextElement);

    // Resolve a text element (typically a selection endpoint or the caret) to the model block/row/column
    // of its hosting table cell. blockIndex is -1 if the element is not inside a table.
    private (int BlockIndex, int RowIndex, int ColumnIndex) TableLocationOf(TextElement? element)
    {
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

        // Outline collapse is view-only: compute the model blocks hidden beneath collapsed headings,
        // skip building them, and remember them (with their preceding-visible-block count) so
        // CommitToModel can restore them. With nothing collapsed both collections are empty and this is
        // a no-op, leaving the original rendering path unchanged.
        var blocks = _model.Blocks;
        var hidden = HiddenBlockIndices();
        _hiddenBlocks.Clear();
        var visibleCount = 0;

        // Coalesce consecutive list paragraphs of the same kind into one WPF List so they render with
        // shared bullet/number decoration; everything else maps one-to-one via BuildBlock.
        var i = 0;
        while (i < blocks.Count)
        {
            if (hidden.Contains(i))
            {
                // Skip the hidden block but retain it (anchored to the visible blocks rendered so far)
                // so the model is reconstructed faithfully on the next commit.
                _hiddenBlocks.Add((visibleCount, blocks[i]));
                i++;
                continue;
            }

            if (blocks[i] is ModelParagraph { Formatting.ListKind: not ListKind.None } first)
            {
                var kind = first.Formatting.ListKind;
                // Stash the exact model ListKind on the list's Tag: WPF renders Number and MultiLevel with
                // the same Decimal marker, so ReadList recovers the kind from this Tag rather than inferring
                // it from the marker (which can't tell MultiLevel from Number) — see ReadList/FromMarkerStyle.
                var list = new WpfList { MarkerStyle = ToMarkerStyle(kind), Tag = kind };
                while (i < blocks.Count
                    && !hidden.Contains(i)
                    && blocks[i] is ModelParagraph { Formatting.ListKind: var k } listParagraph
                    && k == kind)
                {
                    list.ListItems.Add(new WpfListItem(BuildParagraph(listParagraph, _model)));
                    visibleCount++;
                    i++;
                }
                flow.Blocks.Add(list);
            }
            else
            {
                flow.Blocks.Add(BuildBlock(blocks[i], _model));
                visibleCount++;
                i++;
            }
        }

        Document = flow;
        ApplyPageChrome();
        ApplyProtection();
        SyncFormattingMarksAdorner();
        SyncPageBreakAdorner();
    }

    /// <summary>
    /// Whether the editor is showing formatting marks (pilcrow <c>¶</c> at paragraph ends, middle dots
    /// <c>·</c> for spaces, right arrows <c>→</c> for tabs). The marks are drawn as a non-editable
    /// <see cref="FormattingMarksAdorner"/> overlay computed from the live document's text geometry, so
    /// they are purely visual and never enter the document model/text (a <see cref="CommitToModel"/>
    /// after toggling them on adds no glyphs to any run). Purely view chrome; the model is untouched.
    /// </summary>
    public bool ShowFormattingMarks { get; private set; }

    /// <summary>
    /// Turn the formatting-marks overlay on or off and return the new state. Used by the View ribbon's
    /// "Show ¶" toggle. Re-syncs the overlay adorner so the change shows immediately; never mutates the
    /// model (the marks are display-only decorations).
    /// </summary>
    public bool ToggleFormattingMarks()
    {
        ShowFormattingMarks = !ShowFormattingMarks;
        SyncFormattingMarksAdorner();
        return ShowFormattingMarks;
    }

    // The live overlay drawing the ¶/·/→ glyphs, or null while marks are off. Added to / removed from
    // this control's AdornerLayer so it never participates in the FlowDocument content (and so never
    // round-trips through CommitToModel). Recreated cheaply; it reads geometry from the current Document.
    private FormattingMarksAdorner? _formattingMarksAdorner;

    // Add, remove, or refresh the formatting-marks overlay to match ShowFormattingMarks. The adorner
    // layer only exists once the control is in a visual tree (loaded), so when it is not yet available
    // we defer: a one-shot Loaded handler re-runs this. Toggling off removes the adorner; an already
    // present adorner is just invalidated so it repaints against the freshly rendered Document.
    private void SyncFormattingMarksAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            // Not in a visual tree yet: retry once we are loaded (covers a toggle before first show).
            if (ShowFormattingMarks)
            {
                Loaded -= OnLoadedSyncFormattingMarks;
                Loaded += OnLoadedSyncFormattingMarks;
            }
            return;
        }

        if (ShowFormattingMarks)
        {
            if (_formattingMarksAdorner is null)
            {
                _formattingMarksAdorner = new FormattingMarksAdorner(this);
                layer.Add(_formattingMarksAdorner);
            }
            _formattingMarksAdorner.InvalidateVisual();
        }
        else if (_formattingMarksAdorner is not null)
        {
            layer.Remove(_formattingMarksAdorner);
            _formattingMarksAdorner = null;
        }
    }

    private void OnLoadedSyncFormattingMarks(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncFormattingMarks;
        SyncFormattingMarksAdorner();
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
    /// Reflects the model's page border and watermark as editor chrome, and — in Print-Layout mode — gives
    /// the editing surface a Word-style page presentation. The page border drives the control's own
    /// <see cref="Control.BorderBrush"/>/<see cref="Control.BorderThickness"/> (drawn around the editing
    /// surface), and the watermark is painted as faint, rotated tiled text behind the content via the
    /// control <see cref="Control.Background"/>.
    ///
    /// When <see cref="PrintLayoutEnabled"/> is on (the default), the surface is sized to the model page
    /// width (<see cref="PageLayout.PageSizeDip"/>), centred so the grey workspace its host paints shows on
    /// either side, given the page's margins as <see cref="Control.Padding"/> (so the text column matches
    /// the printed content area), and lifted with a soft <see cref="DropShadowEffect"/> so it reads as a
    /// physical sheet. When off, the surface reverts to the original flat/continuous presentation (a fixed
    /// comfortable padding, full width, no shadow), so existing behaviour is preserved exactly.
    ///
    /// LIMITATION: WPF's editable <see cref="RichTextBox"/>/<see cref="FlowDocument"/> is a single
    /// continuous flow, so this is a Print-Layout *visual treatment* of one editable surface — not true
    /// multi-page editable pagination (content flowing across discrete page objects). Page boundaries are
    /// shown as the <see cref="PageBreakAdorner"/> markers (see its remarks for the approximation); the
    /// fully paginated rendering remains the read-only Print Preview path.
    ///
    /// All of the above is purely visual: the model and saved document are untouched, and it cooperates
    /// with zoom (the <see cref="LayoutTransform"/> scales the sized page too) and read mode.
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

        if (PrintLayoutEnabled)
        {
            // Size the surface to the model page width and reflect the page margins as the editor padding,
            // so the text column sits inside the same printable area the print path uses. The host paints
            // the grey workspace; centring the page lets that grey show on either side. The drop shadow
            // lifts the sheet off the workspace.
            var (pageWidthDip, _) = PageLayout.PageSizeDip(_model.Page);
            var (left, top, right, bottom) = PageLayout.MarginsDip(_model.Page);
            Width = pageWidthDip;
            HorizontalAlignment = HorizontalAlignment.Center;
            Padding = new Thickness(left, top, right, bottom);
            Effect = PageShadow;
        }
        else
        {
            // Plain / continuous view: the original flat editable text box — full width, fixed padding,
            // no page shadow.
            Width = double.NaN; // auto: stretch to the host
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Padding = PlainPadding;
            Effect = null;
        }

        // Let passive page-geometry chrome (the ruler) redraw against the new width/margins/print-layout.
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // Add, remove, or refresh the page-break overlay to match PrintLayoutEnabled. Mirrors
    // SyncFormattingMarksAdorner: the adorner layer only exists once the control is in a visual tree, so
    // when it is not yet available we defer via a one-shot Loaded handler. Turning Print Layout off removes
    // the overlay; an already-present overlay is just invalidated so it repaints against the current page.
    private void SyncPageBreakAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            if (PrintLayoutEnabled)
            {
                Loaded -= OnLoadedSyncPageBreaks;
                Loaded += OnLoadedSyncPageBreaks;
            }
            return;
        }

        if (PrintLayoutEnabled)
        {
            if (_pageBreakAdorner is null)
            {
                _pageBreakAdorner = new PageBreakAdorner(this);
                layer.Add(_pageBreakAdorner);
            }
            _pageBreakAdorner.InvalidateVisual();
        }
        else if (_pageBreakAdorner is not null)
        {
            layer.Remove(_pageBreakAdorner);
            _pageBreakAdorner = null;
        }
    }

    private void OnLoadedSyncPageBreaks(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncPageBreaks;
        SyncPageBreakAdorner();
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

    // Both numbered and multilevel lists render with a decimal marker. WPF's FlowDocument List has no
    // built-in accumulating outline marker (true "1.1.1" form), so MultiLevel is rendered best-effort
    // as a plain decimal-per-level marker; the outline definition still round-trips through the docx.
    private static TextMarkerStyle ToMarkerStyle(ListKind kind) =>
        kind is ListKind.Number or ListKind.MultiLevel ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;

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
    /// <para>
    /// Also carries the paragraph's <see cref="ModelParagraph.StyleId"/>: the FlowDocument resolves a
    /// style's run/paragraph formatting at render but has no slot for the style <em>name</em>, so without
    /// this the style id was dropped on every <see cref="CommitToModel"/> — which in turn broke outline
    /// collapse (heading detection is by style id) after a commit. Stamping it here makes the style id
    /// round-trip through an edit/commit cycle.
    /// </para>
    /// </summary>
    private sealed record ParagraphTag(IReadOnlyList<TabStop> TabStops, string? BookmarkName, bool PageBreakBefore = false, bool WidowControl = false, string? StyleId = null);

    /// <summary>Read the edited FlowDocument back into the model (paragraphs + tables).</summary>
    public void CommitToModel()
    {
        // Read the (visible) FlowDocument blocks back into a fresh list first. When outline collapse is
        // active the view only holds the visible blocks, so the hidden model blocks are spliced back in
        // afterwards (see MergeHiddenBlocks) to keep the model document complete.
        var visible = new List<ModelBlock>();
        foreach (var block in Document.Blocks)
        {
            switch (block)
            {
                case WpfList wpfList:
                    ReadList(visible, wpfList, _model);
                    break;
                case WpfParagraph wpfParagraph:
                    visible.Add(ReadParagraph(wpfParagraph, _model));
                    break;
                case WpfTable wpfTable:
                    visible.Add(ReadTable(wpfTable, _model));
                    break;
            }
        }

        _model.Blocks.Clear();
        if (_hiddenBlocks.Count == 0)
        {
            foreach (var block in visible)
                _model.Blocks.Add(block);
        }
        else
        {
            MergeHiddenBlocks(visible);
        }

        if (_model.Blocks.Count == 0)
            _model.Blocks.Add(new ModelParagraph());
    }

    // Reconstruct the full model from the committed visible blocks plus the blocks that Render hid for
    // collapsed headings. Each hidden block was recorded with the count of visible blocks that preceded
    // it; we re-insert it once that many visible blocks have been emitted, so a collapsed region lands
    // back in document order even if the user split/merged visible paragraphs while it was collapsed.
    private void MergeHiddenBlocks(IReadOnlyList<ModelBlock> visible)
    {
        var hiddenIndex = 0;
        for (var emitted = 0; emitted <= visible.Count; emitted++)
        {
            // Drop in every hidden block anchored at this visible offset before the next visible block.
            while (hiddenIndex < _hiddenBlocks.Count && _hiddenBlocks[hiddenIndex].VisibleOffset == emitted)
                _model.Blocks.Add(_hiddenBlocks[hiddenIndex++].Block);

            if (emitted < visible.Count)
                _model.Blocks.Add(visible[emitted]);
        }

        // Any hidden blocks anchored past the last visible block (e.g. the document ended inside a
        // collapsed region) are appended so nothing is lost.
        while (hiddenIndex < _hiddenBlocks.Count)
            _model.Blocks.Add(_hiddenBlocks[hiddenIndex++].Block);
    }

    // Map a *visible* block ordinal (as produced by NumberLeafBlocks / SelectedModelParagraphIndices,
    // which only number the rendered FlowDocument blocks) to the matching index in _model.Blocks after a
    // commit. With outline collapse active, MergeHiddenBlocks re-splices each hidden block back in front
    // of the visible block at its VisibleOffset, so the i-th visible block sits at (i + the number of
    // hidden blocks anchored at or before it). With nothing collapsed _hiddenBlocks is empty and this is
    // the identity, leaving the non-collapsed path unchanged. Fixes the index drift where a paragraph
    // command (style/format/comment/revision) mis-targeted when a heading was collapsed before the
    // selection.
    private int ModelIndexFromVisible(int visibleIndex)
    {
        if (_hiddenBlocks.Count == 0 || visibleIndex < 0)
            return visibleIndex;
        var shift = 0;
        foreach (var (visibleOffset, _) in _hiddenBlocks)
        {
            if (visibleOffset <= visibleIndex)
                shift++;
        }
        return visibleIndex + shift;
    }

    private static ModelParagraph ReadParagraph(WpfParagraph wpfParagraph, TextDocument document)
    {
        var modelParagraph = new ModelParagraph
        {
            Formatting = ReadParagraphFormatting(wpfParagraph, document),
            // The bookmark name and style id (invisible markers with no FlowDocument slot) are preserved
            // across edits via the paragraph Tag (see ParagraphTag).
            BookmarkName = wpfParagraph.Tag is ParagraphTag { BookmarkName: { Length: > 0 } name } ? name : null,
            StyleId = wpfParagraph.Tag is ParagraphTag { StyleId: { Length: > 0 } styleId } ? styleId : null
        };
        foreach (var inline in wpfParagraph.Inlines)
            ReadInline(modelParagraph, inline, hyperlinkUrl: null, hyperlinkAnchor: null);
        return modelParagraph;
    }

    // Flatten a WPF List into model paragraphs, stamping each with the list's kind and the nesting
    // depth as ListLevel. ListItems may hold nested Lists (deeper levels) alongside paragraphs.
    private static void ReadList(IList<ModelBlock> target, WpfList wpfList, TextDocument document, int level = 0)
    {
        // Prefer the exact ListKind stashed on the Tag at render (Render stamps it because WPF renders
        // Number and MultiLevel with the same Decimal marker); fall back to inferring it from the marker
        // for lists the user created fresh in the editor (which carry no Tag).
        var kind = wpfList.Tag is ListKind tagged ? tagged : FromMarkerStyle(wpfList.MarkerStyle);
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

    // Recover a ListKind from a WPF List's marker, used only as a fallback for lists with no stashed
    // kind on their Tag (i.e. lists the user created fresh in the editor). A rendered model list carries
    // its exact ListKind on the list Tag (see Render/ReadList), so a MultiLevel list survives an in-editor
    // edit cycle; this marker-based inference can't tell MultiLevel from Number (both use Decimal).
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
    private static void ReadInline(ModelParagraph modelParagraph, Inline inline, string? hyperlinkUrl, string? hyperlinkAnchor, string? hyperlinkTooltip = null)
    {
        switch (inline)
        {
            case WpfHyperlink link:
                var info = link.Tag as HyperlinkInfo;
                var anchor = info?.Anchor ?? hyperlinkAnchor;
                // An internal link has no NavigateUri; only treat NavigateUri as an external URL.
                var url = anchor is { Length: > 0 } ? hyperlinkUrl : link.NavigateUri?.ToString() ?? hyperlinkUrl;
                var tooltip = info?.Tooltip ?? hyperlinkTooltip;
                foreach (var child in link.Inlines)
                    ReadInline(modelParagraph, child, url, anchor, tooltip);
                break;
            case InlineUIContainer { Child: Image { Tag: InlineImage modelImage } }:
                modelParagraph.Runs.Add(new ModelRun(string.Empty) { Image = modelImage, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip });
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Shape modelShape } }:
                modelParagraph.Runs.Add(ModelRun.FromShape(modelShape));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Chart modelChart } }:
                modelParagraph.Runs.Add(ModelRun.FromChart(modelChart));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: WordArt modelWordArt } }:
                modelParagraph.Runs.Add(ModelRun.FromWordArt(modelWordArt));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Equation modelEquation } }:
                modelParagraph.Runs.Add(ModelRun.FromEquation(modelEquation));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: SmartArt modelSmartArt } }:
                modelParagraph.Runs.Add(ModelRun.FromSmartArt(modelSmartArt));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: EmbeddedObject modelEmbedded } }:
                modelParagraph.Runs.Add(ModelRun.FromEmbeddedObject(modelEmbedded));
                break;
            case WpfRun { Tag: FootnoteMarker marker }:
                modelParagraph.Runs.Add(ModelRun.FootnoteReference(marker.FootnoteId));
                break;
            case WpfRun { Tag: PageBreakMarker }:
                modelParagraph.Runs.Add(ModelRun.PageBreak());
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
                    HyperlinkTooltip = hyperlinkTooltip,
                    FieldKind = fieldMarker.Kind
                });
                break;
            case WpfRun { Tag: RunMarkers { Comment: { IsReference: true } reference } }:
                // The textless comment anchor: round-trips as a comment-reference run.
                modelParagraph.Runs.Add(ModelRun.CommentReference(reference.CommentId));
                break;
            case WpfRun { Tag: RunMarkers markers } markedRun
                when markedRun.Text.Length > 0 || markers.Comment is not null || markers.Control is not null:
                // A run carrying any combination of comment / content-control / revision marks. Recover its
                // formatting, strip the view-only chrome each facet injected (review highlight, control
                // shade, revision colour/decoration), and carry every facet back onto the model run so a
                // run that is, say, both commented and tracked-changed survives the round-trip intact. A run
                // whose text was emptied but that still carries a comment or content-control marker is kept
                // as a zero-length marked run rather than dropped, so the marker is not lost on commit.
                var markedFmt = ReadRunFormatting(markedRun);
                if (markers.Revision is { } rev)
                    markedFmt = StripRevisionChrome(markedFmt, rev.Kind);
                // Comment and content-control both inject a background; clear it so it isn't mistaken for a
                // real highlight on commit (matching the prior per-facet behaviour).
                if (markers.Comment is not null || markers.Control is not null)
                    markedFmt = markedFmt with { HighlightColorHex = null };

                // For a checkbox the run text holds the (possibly toggled) ☒/☐ glyph; keep the control's
                // checked state in sync with the glyph so an in-place toggle round-trips.
                var control = markers.Control?.Control;
                if (control is { Kind: ContentControlKind.CheckBox })
                    control = control with { Checked = markedRun.Text == ModelContentControl.CheckedGlyph };

                modelParagraph.Runs.Add(new ModelRun(markedRun.Text, markedFmt)
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    HyperlinkTooltip = hyperlinkTooltip,
                    CommentId = markers.Comment?.CommentId,
                    Control = control,
                    Revision = markers.Revision?.Kind ?? RevisionKind.None,
                    RevisionAuthor = markers.Revision?.Author,
                    RevisionDateXml = markers.Revision?.DateXml
                });
                break;
            case WpfRun run when run.Text.Length > 0:
                modelParagraph.Runs.Add(new ModelRun(run.Text, ReadRunFormatting(run)) { HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip });
                break;
        }
    }

    private static ModelTable ReadTable(WpfTable wpfTable, TextDocument document)
    {
        var table = new ModelTable();

        // Recover the table-style toggles stashed by BuildTable (WPF FlowDocument tables can't express
        // header/banded/repeat as table-level state, so they ride along on the Tag). Borders are still
        // reconstructed from the view below so a user toggling borders is honoured.
        var stashed = wpfTable.Tag as TableFormatting;
        var headerRow = stashed?.HeaderRow ?? false;
        var bandedRows = stashed?.BandedRows ?? false;
        var repeatHeader = stashed?.RepeatHeaderRow ?? false;

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

        // First pass: read each WPF cell into a model cell, carrying ColumnSpan -> GridSpan and
        // RowSpan -> VerticalMerge.Restart. Continue cells are not rendered (they are absorbed by the
        // restart's RowSpan), so we record where they need to be re-synthesised in the rows below.
        var modelRows = new List<ModelTableRow>();
        // pendingContinues[rowIndex] = list of (gridColumn, gridSpan) continuation cells to inject.
        var pendingContinues = new List<List<(int Column, int Span)>>();
        foreach (var rowGroup in wpfTable.RowGroups)
        {
            foreach (var _ in rowGroup.Rows)
            {
                modelRows.Add(new ModelTableRow());
                pendingContinues.Add([]);
            }
        }

        var rowIndex = 0;
        foreach (var rowGroup in wpfTable.RowGroups)
        {
            foreach (var wpfRow in rowGroup.Rows)
            {
                var isHeaderRow = headerRow && rowIndex == 0;
                var isBandedRow = bandedRows && !isHeaderRow && IsBandedBodyRow(rowIndex, headerRow);
                var row = modelRows[rowIndex];
                foreach (var wpfCell in wpfRow.Cells)
                {
                    var span = Math.Max(1, wpfCell.ColumnSpan);
                    string? cellShading;
                    if (wpfCell.Tag is TableCellTag tag)
                    {
                        // A rendered cell carries its author-set shading verbatim on the Tag, so honour it
                        // directly: this distinguishes real shading that happens to equal the header/banded
                        // style fill (which the colour heuristic below would wrongly strip) from a style fill.
                        cellShading = tag.ShadingColorHex is { Length: > 0 } stashedShading ? stashedShading : null;
                    }
                    else if (wpfCell.Background is SolidColorBrush shading)
                    {
                        // A cell the user created fresh in the editor has no Tag: fall back to inferring
                        // shading from the background, excluding the header/banded style fills (rendered
                        // chrome, not user shading — they re-derive from the toggles on render/save).
                        var isStyleFill = (isHeaderRow && ColorsEqual(shading.Color, HeaderRowFill))
                            || (isBandedRow && ColorsEqual(shading.Color, BandedRowFill));
                        cellShading = isStyleFill ? null : ToHex(shading.Color);
                    }
                    else
                    {
                        cellShading = null;
                    }
                    var cell = new ModelTableCell
                    {
                        ShadingColorHex = cellShading,
                        GridSpan = span
                    };
                    foreach (var cellBlock in wpfCell.Blocks)
                    {
                        if (cellBlock is WpfParagraph cellParagraph)
                            cell.Paragraphs.Add(ReadParagraph(cellParagraph, document));
                    }
                    if (cell.Paragraphs.Count == 0)
                        cell.Paragraphs.Add(new ModelParagraph());

                    if (wpfCell.RowSpan > 1)
                    {
                        cell.VerticalMerge = VerticalMergeState.Restart;
                        // Compute this cell's grid column from cells already placed in the row, then queue
                        // a Continue placeholder in each covered row below so the model keeps its shape.
                        var gridColumn = row.Cells.Sum(c => Math.Max(1, c.GridSpan));
                        for (var r = rowIndex + 1; r < rowIndex + wpfCell.RowSpan && r < pendingContinues.Count; r++)
                            pendingContinues[r].Add((gridColumn, span));
                    }
                    row.Cells.Add(cell);
                }
                rowIndex++;
            }
        }

        // Second pass: inject the queued Continue cells at their grid columns (insertion order by column
        // keeps the row laid out left-to-right).
        for (var r = 0; r < modelRows.Count; r++)
        {
            foreach (var (column, span) in pendingContinues[r].OrderBy(c => c.Column))
            {
                var insertAt = InsertIndexForGridColumn(modelRows[r], column);
                modelRows[r].Cells.Insert(insertAt, new ModelTableCell
                {
                    GridSpan = span,
                    VerticalMerge = VerticalMergeState.Continue
                });
            }
        }

        foreach (var row in modelRows)
            table.Rows.Add(row);

        table.Formatting = table.Formatting with
        {
            HeaderRow = headerRow,
            BandedRows = bandedRows,
            RepeatHeaderRow = repeatHeader
        };
        return table;
    }

    // Returns the cell index at which a Continue placeholder for the given grid column should be
    // inserted, walking the already-placed cells and summing their grid spans.
    private static int InsertIndexForGridColumn(ModelTableRow row, int gridColumn)
    {
        var column = 0;
        for (var i = 0; i < row.Cells.Count; i++)
        {
            if (column >= gridColumn)
                return i;
            column += Math.Max(1, row.Cells[i].GridSpan);
        }
        return row.Cells.Count;
    }

    private static bool ColorsEqual(Color a, Color b) =>
        a.R == b.R && a.G == b.G && a.B == b.B;

    // --- model -> view ---

    private static System.Windows.Documents.Block BuildBlock(ModelBlock block, TextDocument document) => block switch
    {
        ModelTable table => BuildTable(table, document),
        ModelParagraph paragraph => BuildParagraph(paragraph, document),
        _ => BuildParagraph(new ModelParagraph(), document)
    };

    // The light fills used to render the table-style toggles (mirroring DocxWriter's header/banded fills).
    private static readonly Color HeaderRowFill = Color.FromRgb(0xD9, 0xE2, 0xF3);
    private static readonly Color BandedRowFill = Color.FromRgb(0xF2, 0xF2, 0xF2);

    /// <summary>
    /// Carried on a rendered <see cref="WpfTableCell"/>'s Tag so <see cref="ReadTable"/> can recover the
    /// cell's <em>author-set</em> shading on commit. The rendered background alone is ambiguous — real
    /// shading can equal the header/banded style fill — so the model value is stashed verbatim here and the
    /// colour-equality heuristic is used only for cells the user created fresh in the editor (no Tag).
    /// </summary>
    private sealed record TableCellTag(string? ShadingColorHex);

    private static WpfTable BuildTable(ModelTable table, TextDocument document)
    {
        // Stash the model's table formatting on the WPF table so the flags survive the view->model
        // round-trip (CommitToModel's ReadTable reconstructs Borders from the view but recovers the
        // header/banded/repeat toggles from this Tag, which WPF FlowDocument tables can't express).
        var wpf = new WpfTable { Tag = table.Formatting };
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

        var fmt = table.Formatting;
        var group = new TableRowGroup();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var modelRow = table.Rows[rowIndex];
            var isHeaderRow = fmt.HeaderRow && rowIndex == 0;
            var isBandedRow = fmt.BandedRows && !isHeaderRow && IsBandedBodyRow(rowIndex, fmt.HeaderRow);
            var wpfRow = new WpfTableRow();
            // Track the running grid-column position so vertical-merge runs can be matched up by
            // column even when earlier cells span multiple grid columns.
            var gridColumn = 0;
            foreach (var modelCell in modelRow.Cells)
            {
                var span = Math.Max(1, modelCell.GridSpan);
                // A vertical-merge "continue" cell is absorbed by the restart cell above (which carries
                // a RowSpan covering it), so it is not rendered as its own WPF cell.
                if (modelCell.VerticalMerge == VerticalMergeState.Continue)
                {
                    gridColumn += span;
                    continue;
                }

                var wpfCell = new WpfTableCell
                {
                    Padding = new Thickness(4, 2, 4, 2)
                };
                if (span > 1)
                    wpfCell.ColumnSpan = span;
                if (modelCell.VerticalMerge == VerticalMergeState.Restart)
                {
                    var rowSpan = CountVerticalMergeSpan(table, rowIndex, gridColumn);
                    if (rowSpan > 1)
                        wpfCell.RowSpan = rowSpan;
                }
                if (table.Formatting.Borders)
                {
                    wpfCell.BorderBrush = borderBrush;
                    wpfCell.BorderThickness = new Thickness(0.5);
                }
                // Stash the model's author-set shading on the cell Tag so ReadTable can tell it apart from
                // a style-derived header/banded fill on commit — a colour-equality heuristic alone can't,
                // and would strip real shading that happens to match the style fill (see ReadTable).
                wpfCell.Tag = new TableCellTag(modelCell.ShadingColorHex);

                // The cell's explicit shading wins; otherwise apply the header/banded style fill.
                if (modelCell.ShadingColorHex is { Length: > 0 } cellShading)
                    wpfCell.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cellShading));
                else if (isHeaderRow)
                    wpfCell.Background = new SolidColorBrush(HeaderRowFill);
                else if (isBandedRow)
                    wpfCell.Background = new SolidColorBrush(BandedRowFill);
                if (isHeaderRow)
                    wpfCell.FontWeight = FontWeights.Bold;
                if (modelCell.Paragraphs.Count == 0)
                {
                    wpfCell.Blocks.Add(BuildParagraph(new ModelParagraph(), document, inTableCell: true));
                }
                else
                {
                    foreach (var cellParagraph in modelCell.Paragraphs)
                        wpfCell.Blocks.Add(BuildParagraph(cellParagraph, document, inTableCell: true));
                }
                wpfRow.Cells.Add(wpfCell);
                gridColumn += span;
            }
            group.Rows.Add(wpfRow);
        }
        wpf.RowGroups.Add(group);
        return wpf;
    }

    // Counts the height (in rows) of a vertical-merge run that starts at (restartRow, gridColumn):
    // the restart row plus every immediately following row whose cell at the same grid column carries
    // VerticalMerge.Continue. Returns at least 1 (the restart cell itself).
    private static int CountVerticalMergeSpan(ModelTable table, int restartRow, int gridColumn)
    {
        var span = 1;
        for (var r = restartRow + 1; r < table.Rows.Count; r++)
        {
            var continuation = CellAtGridColumn(table.Rows[r], gridColumn);
            if (continuation?.VerticalMerge == VerticalMergeState.Continue)
                span++;
            else
                break;
        }
        return span;
    }

    // Resolves the model cell occupying a given grid-column position in a row, honouring GridSpan so
    // a wide cell is matched for any column it covers. Returns null if the position is past the row.
    private static ModelTableCell? CellAtGridColumn(ModelTableRow row, int gridColumn)
    {
        var column = 0;
        foreach (var cell in row.Cells)
        {
            var span = Math.Max(1, cell.GridSpan);
            if (gridColumn >= column && gridColumn < column + span)
                return cell;
            column += span;
        }
        return null;
    }

    /// <summary>Mirror of DocxWriter's banding rule: which body row (2nd, 4th, ...) is shaded.</summary>
    private static bool IsBandedBodyRow(int rowIndex, bool hasHeader)
    {
        var bodyIndex = hasHeader ? rowIndex - 1 : rowIndex;
        return bodyIndex >= 0 && bodyIndex % 2 == 1;
    }

    private static WpfParagraph BuildParagraph(ModelParagraph paragraph, TextDocument document, bool inTableCell = false)
    {
        var paraFmt = Resolve(paragraph, document);
        // Inside a table cell, paragraphs that don't set their own spacing follow the table style rather than
        // the document default. Word's built-in TableNormal style (the base of every table style) uses 0pt
        // before/after and single line spacing, so its cells render compact — FreeW otherwise applied the
        // body docDefault (e.g. 10pt-after, 1.15-line), making rows visibly taller than Word's. Compact only
        // the fields the paragraph/its style didn't explicitly set (the IsSet flags), so an explicitly-spaced
        // cell paragraph is untouched.
        if (inTableCell)
        {
            if (!paraFmt.SpaceBeforeIsSet)
                paraFmt = paraFmt with { SpaceBeforePt = 0 };
            if (!paraFmt.SpaceAfterIsSet)
                paraFmt = paraFmt with { SpaceAfterPt = 0 };
            if (!paraFmt.LineSpacingIsSet)
                paraFmt = paraFmt with { LineSpacing = 1.0, LineRule = LineSpacingRule.Multiple, LineHeightPt = 0 };
        }
        var wpf = new WpfParagraph
        {
            // Right-to-left paragraph direction (w:bidi). WPF lays the inline content out RTL and, because
            // TextAlignment is interpreted relative to FlowDirection, the model's default (Left = leading)
            // alignment lands at the right edge — matching Word's default for a bidi paragraph.
            FlowDirection = paraFmt.Rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
            // Start this paragraph on a new page in the paginator (print/preview) when it forces a break
            // before it (w:pageBreakBefore) or carries a manual page-break run (w:br type=page). The
            // editor's continuous RichTextBox ignores BreakPageBefore, so this only affects the paginated
            // output — which previously honoured neither, leaving FreeW badly under-paginated vs Word.
            BreakPageBefore = paraFmt.PageBreakBefore || paragraph.Runs.Any(r => r.IsPageBreak),
            TextAlignment = ToWpfAlignment(paraFmt.Alignment),
            // WPF's Block.Margin (unlike FrameworkElement.Margin) rejects negative components with an
            // ArgumentException, so clamp at >= 0. Real docs do carry negative indents/spacing (e.g. a
            // negative right indent pulling into the margin); WPF cannot represent that as a block margin,
            // so we render it as 0 rather than crash. The model keeps the original value, so docx
            // round-trip is unaffected; only the live render clamps. (TextIndent below may stay negative —
            // a hanging first-line indent is valid there.)
            Margin = new Thickness(
                Math.Max(0, paraFmt.IndentLeftPt * PxPerPoint),
                Math.Max(0, paraFmt.SpaceBeforePt * PxPerPoint),
                Math.Max(0, paraFmt.IndentRightPt * PxPerPoint),
                Math.Max(0, paraFmt.SpaceAfterPt * PxPerPoint)),
            TextIndent = paraFmt.FirstLineIndentPt * PxPerPoint,
            // Line spacing. For the Multiple rule, Word multiplies the font's *natural* line height (one
            // "line" = ascent+descent+gap), NOT the raw em — so a 1.08/1.15-line paragraph is ~8–15% taller
            // than the font size. WPF exposes that natural height as FontFamily.LineSpacing, so the multiple
            // is applied to (em x ratio). Previously we multiplied the bare em, and MaxHeight then clamped the
            // result back to a single natural line, rendering every multiple-spaced paragraph too short and
            // letting FreeW pack more lines per page than Word. For Exact/AtLeast the model carries an absolute
            // height in points; WPF only does absolute LineHeight, so both map to that height — and Exact
            // additionally forces BlockLineHeight so the height is honoured even for taller content (AtLeast is
            // approximated as exact, the closest FlowDocument behaviour).
            LineHeight = paraFmt.LineRule == LineSpacingRule.Multiple
                ? (paraFmt.LineSpacing > 0
                    ? paraFmt.LineSpacing * DefaultLineHeightRatio(document) * (document.DefaultRun.FontSizePt ?? 11) * PxPerPoint
                    : double.NaN)
                : (paraFmt.LineHeightPt > 0 ? paraFmt.LineHeightPt * PxPerPoint : double.NaN),
            LineStackingStrategy = paraFmt.LineRule == LineSpacingRule.Exact
                ? LineStackingStrategy.BlockLineHeight
                : LineStackingStrategy.MaxHeight,
            // Flow control: WPF's Paragraph exposes KeepWithNext/KeepTogether directly, so map them so
            // they survive an edit/commit cycle without a Tag. WidowControl has no FlowDocument slot and
            // is carried on the Tag instead (see below).
            KeepWithNext = paraFmt.KeepWithNext,
            KeepTogether = paraFmt.KeepLinesTogether
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
        // WidowControl has no FlowDocument property either, so it joins the Tag alongside tab stops,
        // bookmark name and page-break-before; carried verbatim and recovered on commit.
        if (paraFmt.TabStops.Count > 0 || paragraph.BookmarkName is { Length: > 0 } || paraFmt.PageBreakBefore || paraFmt.WidowControl || paragraph.StyleId is { Length: > 0 })
            wpf.Tag = new ParagraphTag(paraFmt.TabStops, paragraph.BookmarkName, paraFmt.PageBreakBefore, paraFmt.WidowControl, paragraph.StyleId);

        foreach (var run in paragraph.Runs)
            wpf.Inlines.Add(BuildRun(run, paragraph, document));

        return wpf;
    }

    private static Inline BuildRun(ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        if (run.Image is { } image)
            return BuildImageRun(image);

        if (run.Shape is { } shape)
            return BuildShapeRun(shape);

        if (run.Chart is { } chart)
            return BuildChartRun(chart);

        if (run.WordArt is { } wordArt)
            return BuildWordArtRun(wordArt);

        if (run.Equation is { } equation)
            return BuildEquationRun(equation);

        if (run.SmartArt is { } smartArt)
            return BuildSmartArtRun(smartArt);

        if (run.EmbeddedObject is { } embedded)
            return BuildEmbeddedObjectRun(embedded);

        if (run.FootnoteId is { } footnoteId)
            return BuildFootnoteReference(footnoteId, document);

        if (run.EndnoteId is { } endnoteId)
            return BuildEndnoteReference(endnoteId, document);

        if (run.FieldKind != RunFieldKind.None)
        {
            // A field run can also carry a hyperlink (e.g. a PAGE/DATE field placed inside a link). Wrap
            // the resolved field run in the same Hyperlink chrome ordinary runs get so its link survives
            // the next CommitToModel (ReadInline's FieldMarker case carries the url/anchor back).
            var fieldRun = BuildFieldRun(run, document);
            if (run.HyperlinkUrl is { Length: > 0 } fieldUrl)
                return BuildHyperlink(fieldRun, fieldUrl, run.HyperlinkTooltip);
            if (run.HyperlinkAnchor is { Length: > 0 } fieldAnchor)
                return BuildInternalHyperlink(fieldRun, fieldAnchor, run.HyperlinkTooltip);
            return fieldRun;
        }

        // The textless comment anchor round-trips as an empty, tagged run carrying its reference flag.
        if (run is { IsCommentReference: true, CommentId: { } refId })
            return new WpfRun(string.Empty) { Tag = new RunMarkers(Comment: new CommentMarker(refId, IsReference: true)) };

        // A manual page break renders as an empty, tagged run; the containing paragraph carries the actual
        // BreakPageBefore (set in BuildParagraph). The tag lets ReadInline recover it on commit so the break
        // survives an edit/commit cycle (mirroring the footnote/endnote markers).
        if (run.IsPageBreak)
            return new WpfRun(string.Empty) { Tag = new PageBreakMarker() };

        var fmt = Resolve(run, paragraph, document);
        var wpf = new WpfRun(run.Text)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal
        };
        // Right-to-left run direction (w:rtl): force this run RTL even inside an LTR paragraph.
        if (fmt.Rtl)
            wpf.FlowDirection = System.Windows.FlowDirection.RightToLeft;
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
            AddMarker(wpf, m => m with { Revision = new RevisionMarker(run.Revision, run.RevisionAuthor, run.RevisionDateXml) });
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
            return BuildHyperlink(wpf, url, run.HyperlinkTooltip);
        if (run.HyperlinkAnchor is { Length: > 0 } anchor)
            return BuildInternalHyperlink(wpf, anchor, run.HyperlinkTooltip);

        return wpf;
    }

    /// <summary>
    /// Carried on a WPF <see cref="WpfHyperlink"/>'s Tag so the link's internal target (a bookmark
    /// anchor) and ScreenTip survive a commit/render round-trip (see <see cref="ReadInline"/>). External
    /// links leave <see cref="Anchor"/> null and store the URL on the link's NavigateUri.
    /// </summary>
    private sealed record HyperlinkInfo(string? Anchor, string? Tooltip);

    /// <summary>Subtle highlight used to mark a commented text range (a pale review yellow).</summary>
    private static readonly Color CommentHighlight = Color.FromRgb(0xFF, 0xF4, 0xCE);

    /// <summary>The fixed colour tracked changes are rendered in (a Word-like revision maroon/red).</summary>
    private static readonly Color RevisionColor = Color.FromRgb(0xC0, 0x00, 0x40);

    /// <summary>
    /// Composite review/control marker carried on a WPF run's <see cref="WpfRun.Tag"/>. A single run can
    /// simultaneously be a tracked change, sit inside a comment range, and live inside a content control,
    /// so each facet is held independently here rather than overwriting a single-purpose Tag (the prior
    /// bug, where the last writer won and the other marks were lost on the next <see cref="CommitToModel"/>).
    /// Every non-null facet is recovered in <see cref="ReadInline"/>. Facets that are mutually exclusive
    /// with these marks (image/shape/field/footnote/endnote) keep using their own dedicated Tag types and
    /// never share a run with these.
    /// </summary>
    private sealed record RunMarkers(
        RevisionMarker? Revision = null,
        CommentMarker? Comment = null,
        ContentControlMarker? Control = null);

    /// <summary>
    /// Merge a marker facet into the run's composite <see cref="RunMarkers"/> Tag (creating it on first
    /// use), so revision/comment/content-control marks accumulate on the same run instead of clobbering
    /// each other. Any non-marker Tag is replaced (those run kinds never carry these marks).
    /// </summary>
    private static void AddMarker(WpfRun wpf, Func<RunMarkers, RunMarkers> merge) =>
        wpf.Tag = merge(wpf.Tag as RunMarkers ?? new RunMarkers());

    /// <summary>
    /// Carried on a tracked-change run inside its <see cref="RunMarkers"/> so CommitToModel can round-trip
    /// its revision kind, author and date. Mirrors how CommentMarker/FootnoteMarker preserve their marks.
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
        AddMarker(wpf, m => m with { Comment = new CommentMarker(commentId, IsReference: false) });
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
        AddMarker(wpf, m => m with { Control = new ContentControlMarker(control) });
        wpf.Background = new SolidColorBrush(ContentControlShade);
        wpf.ToolTip = control.Kind == ContentControlKind.CheckBox
            ? (control.Alias is { Length: > 0 } a ? $"Checkbox: {a}" : "Checkbox content control (click to toggle)")
            : (control.Alias is { Length: > 0 } a2 ? $"Content control: {a2}" : "Plain-text content control");

        if (control.Kind == ContentControlKind.CheckBox)
        {
            // Synthesise the checkbox glyph from the control's checked state and render it in a symbol font.
            // Word stores the box glyph in the SDT content run using a symbol font (often a Wingdings/MS
            // Gothic codepoint), so the raw run text rendered in the body font showed nothing. Driving the
            // glyph from the state (☒/☐ in Segoe UI Symbol, which has U+2610/U+2612) guarantees a visible,
            // correct checkbox and matches how FreeW renders its own inserted checkboxes.
            wpf.Text = control.Checked ? ModelContentControl.CheckedGlyph : ModelContentControl.UncheckedGlyph;
            wpf.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol");
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
        if (sender is not WpfRun { Tag: RunMarkers { Control: { } marker } } wpf
            || marker.Control.Kind != ContentControlKind.CheckBox)
            return;

        var toggled = marker.Control with { Checked = !marker.Control.Checked };
        AddMarker(wpf, m => m with { Control = new ContentControlMarker(toggled) });
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
    private static Inline BuildInternalHyperlink(WpfRun content, string anchor, string? tooltip = null)
    {
        var link = new WpfHyperlink(content);
        StyleInternalLink(link, anchor, tooltip);
        return link;
    }

    private static void StyleInternalLink(WpfHyperlink link, string anchor, string? tooltip = null)
    {
        link.Tag = new HyperlinkInfo(anchor, tooltip);
        // A ScreenTip, when set, wins over the default "Go to bookmark" chrome tooltip.
        link.ToolTip = tooltip is { Length: > 0 } ? tooltip : "Go to bookmark: " + anchor;
        link.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x63, 0xC1));
        link.Click += OnInternalLinkClick;
    }

    // Scroll the paragraph carrying the linked bookmark into view (best-effort). Matches on the
    // model BookmarkName preserved via each WPF paragraph's ParagraphTag, searching the FlowDocument
    // that hosts the clicked link.
    private static void OnInternalLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfHyperlink { Tag: HyperlinkInfo { Anchor: { Length: > 0 } anchor } } link)
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
    private static Inline BuildHyperlink(WpfRun content, string url, string? tooltip = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return content;

        var link = new WpfHyperlink(content) { NavigateUri = uri };
        StyleLink(link, url, tooltip);
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

    /// <summary>Carried on a manual page-break WPF run's Tag so CommitToModel can round-trip it.</summary>
    private sealed record PageBreakMarker;

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

    /// <summary>
    /// Renders an inline image as an InlineUIContainer hosting a WPF Image. The image bytes are decoded
    /// crash-proof: a raster format goes through WPF's WIC pipeline; WMF/EMF metafiles are rendered
    /// best-effort via GDI+ (see <see cref="TryDecodeMetafile"/>). Any decode failure (e.g. a format WIC
    /// cannot handle, or corrupt bytes) is caught and a sized placeholder box is shown in the image's
    /// place instead of throwing, so one un-decodable image never fails the whole document render. The
    /// element keeps the model <see cref="InlineImage"/> on its <c>Tag</c> either way, so the run still
    /// round-trips through <see cref="CommitToModel"/> unchanged.
    /// </summary>
    private static InlineUIContainer BuildImageRun(InlineImage image)
    {
        var widthPx = image.WidthPt * PxPerPoint;
        var heightPx = image.HeightPt * PxPerPoint;

        var source = DecodeImage(image) ?? BuildImagePlaceholder(image, widthPx, heightPx);

        var element = new Image
        {
            Source = source,
            Width = widthPx,
            Height = heightPx,
            Stretch = Stretch.Fill,
            Tag = image // carries the model image so CommitToModel can round-trip it
        };
        // Surface alt text as the hover tooltip and the accessibility (automation) name when present.
        if (!string.IsNullOrEmpty(image.AltText))
        {
            element.ToolTip = image.AltText;
            System.Windows.Automation.AutomationProperties.SetName(element, image.AltText);
        }
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// Decode an inline image's bytes into a WPF <see cref="ImageSource"/>, returning null (never throwing)
    /// when the bytes cannot be decoded. WMF/EMF metafiles are routed through GDI+
    /// (<see cref="TryDecodeMetafile"/>); everything else goes through WPF's WIC decoder. A null result
    /// signals <see cref="BuildImageRun"/> to render a placeholder in the image's place.
    /// </summary>
    private static ImageSource? DecodeImage(InlineImage image)
    {
        var bytes = image.Bytes;
        if (bytes is null || bytes.Length == 0)
            return null;

        try
        {
            if (image.Format is ImageFormat.Wmf or ImageFormat.Emf)
                return TryDecodeMetafile(bytes);

            return DecodeRaster(bytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException
            or System.Runtime.InteropServices.ExternalException or ArgumentException
            or InvalidOperationException or IOException or OutOfMemoryException)
        {
            // WIC and GDI+ throw a small family of exceptions for an undecodable/corrupt image: WIC raises
            // NotSupportedException ("No imaging component suitable...") for WMF/EMF, and GDI+ raises
            // ExternalException ("A generic error occurred in GDI+") / ArgumentException / OutOfMemoryException
            // for malformed metafile bytes. (ExternalException covers its COMException subclass too.) Swallow
            // them so the rest of the document still renders; the caller draws a placeholder instead.
            return null;
        }
    }

    // Decode raster image bytes (PNG/JPEG/GIF/BMP/TIFF/…) via WPF's WIC pipeline. OnLoad caching reads the
    // whole stream up front so the MemoryStream can be discarded immediately and the result frozen.
    private static BitmapSource DecodeRaster(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// <summary>Backwards-compatible alias kept for callers that decode a known-PNG (e.g. embedded-object icons).</summary>
    private static BitmapSource DecodePng(byte[] bytes) => DecodeRaster(bytes);

    /// <summary>
    /// Guarded raster decode for byte payloads that are nominally images but may be undecodable (e.g.
    /// embedded-object icons): returns null instead of throwing, mirroring <see cref="DecodeImage"/>, so a
    /// bad payload falls back to a placeholder rather than taking down the whole render.
    /// </summary>
    private static BitmapSource? TryDecodeRaster(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        try
        {
            return DecodeRaster(bytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException
            or System.Runtime.InteropServices.ExternalException or ArgumentException
            or InvalidOperationException or IOException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort render of a WMF/EMF metafile to a WPF <see cref="BitmapSource"/> via GDI+: load the
    /// bytes as a <see cref="System.Drawing.Imaging.Metafile"/>, draw it onto a
    /// <see cref="System.Drawing.Bitmap"/> at the metafile's natural pixel size, then convert that bitmap
    /// to a frozen <see cref="BitmapSource"/>. Returns null when the metafile reports no usable size.
    /// GDI+ is Windows-only, but this is a net10.0-windows WPF app so it is always available here.
    /// Limitation: rasterises the vector metafile at a fixed resolution (it is then stretched to the
    /// image's point size), and exotic metafile records that GDI+ cannot play back are dropped; any GDI+
    /// failure throws and is caught by <see cref="DecodeImage"/>, which falls back to the placeholder.
    /// </summary>
    private static BitmapSource? TryDecodeMetafile(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var metafile = new System.Drawing.Imaging.Metafile(stream);

        var width = metafile.Width;
        var height = metafile.Height;
        if (width <= 0 || height <= 0)
            return null;

        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawImage(metafile, 0, 0, width, height);
        }

        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// Build a bordered placeholder <see cref="BitmapSource"/> shown in place of an image whose bytes
    /// could not be decoded. Sized to the image's pixel box, it draws a light-grey filled rectangle with a
    /// dashed border and a small centred caption naming the format (e.g. "WMF image"), so the rest of the
    /// document renders normally and the user sees where the un-decodable picture sits.
    /// </summary>
    private static BitmapSource BuildImagePlaceholder(InlineImage image, double widthPx, double heightPx)
    {
        // Guard against zero/negative sizes so RenderTargetBitmap always gets a valid (>=1px) surface.
        var w = Math.Max(1.0, widthPx);
        var h = Math.Max(1.0, heightPx);

        var caption = $"{image.Format.ToString().ToUpperInvariant()} image";

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            fill.Freeze();
            var stroke = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
            stroke.Freeze();
            var pen = new Pen(stroke, 1) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
            pen.Freeze();

            var rect = new Rect(0.5, 0.5, Math.Max(1.0, w - 1), Math.Max(1.0, h - 1));
            dc.DrawRectangle(fill, pen, rect);

            // Draw the caption centred, but only when there is room for it (tiny placeholders stay blank).
            var text = new FormattedText(
                caption,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                stroke,
                1.0);
            if (text.Width + 4 <= w && text.Height + 2 <= h)
                dc.DrawText(text, new System.Windows.Point((w - text.Width) / 2, (h - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // P/Invoke for releasing the GDI HBITMAP produced by Bitmap.GetHbitmap in TryDecodeMetafile (the
    // managed Bitmap does not own it, so it must be freed explicitly to avoid a GDI handle leak).
    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }

    /// <summary>
    /// Renders an inline shape / text box as an InlineUIContainer hosting a WPF element that carries the
    /// model <see cref="Shape"/> on its Tag, so CommitToModel round-trips it (mirroring images). Ellipses
    /// render as a System.Windows.Shapes.Ellipse-backed border; rectangles / rounded rectangles / text
    /// boxes render as a Border (with a corner radius for rounded). A text box shows its plain text.
    /// </summary>
    private static InlineUIContainer BuildShapeRun(Shape shape)
    {
        var widthPx = shape.WidthPt * PxPerPoint;
        var heightPx = shape.HeightPt * PxPerPoint;

        System.Windows.Media.Brush fill = TryParseColor(shape.FillColorHex, out var fillColor)
            ? new SolidColorBrush(fillColor)
            : System.Windows.Media.Brushes.Transparent;
        var stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

        FrameworkElement element;
        if (shape.Kind == ShapeKind.Ellipse)
        {
            element = new System.Windows.Shapes.Ellipse
            {
                Width = widthPx,
                Height = heightPx,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
            };
        }
        else
        {
            var border = new Border
            {
                Width = widthPx,
                Height = heightPx,
                Background = fill,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = shape.Kind == ShapeKind.RoundedRectangle ? new CornerRadius(6) : new CornerRadius(0),
            };
            if (shape.HasText)
                border.Child = new TextBlock
                {
                    Text = shape.PlainText,
                    Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                };
            element = border;
        }

        element.Tag = shape; // carries the model shape so CommitToModel can round-trip it
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// Renders an inline equation as an InlineUIContainer hosting a Border that carries the model
    /// <see cref="Equation"/> on its Tag (so CommitToModel round-trips it, mirroring shapes). The border
    /// shows the equation's linear form in a serif/italic face as a lightweight visual stand-in.
    /// </summary>
    private static InlineUIContainer BuildEquationRun(Equation equation)
    {
        var element = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC8, 0xD8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = equation.LinearText,
                FontFamily = new FontFamily("Cambria, Times New Roman, serif"),
                FontStyle = FontStyles.Italic
            },
            Tag = equation // carries the model equation so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>
    /// Renders inline WordArt as an InlineUIContainer hosting a TextBlock that carries the model
    /// <see cref="WordArt"/> on its Tag (so CommitToModel round-trips it, mirroring shapes). The text is
    /// drawn at the WordArt's font size with a style-derived fill/outline as a lightweight visual stand-in.
    /// </summary>
    private static InlineUIContainer BuildWordArtRun(WordArt wordArt)
    {
        var fill = wordArt.Style switch
        {
            WordArtStyle.Outline => System.Windows.Media.Brushes.Transparent,
            WordArtStyle.GradientFill => new SolidColorBrush(Color.FromRgb(0x2E, 0x74, 0xB5)),
            WordArtStyle.Shadow => new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
            _ => new SolidColorBrush(Color.FromRgb(0x1F, 0x49, 0x7D)),
        };
        var element = new TextBlock
        {
            Text = wordArt.Text,
            FontSize = wordArt.FontSizePt * PxPerPoint,
            FontWeight = FontWeights.Bold,
            Foreground = fill,
            Tag = wordArt // carries the model WordArt so CommitToModel can round-trip it
        };
        if (wordArt.Style == WordArtStyle.Outline)
        {
            element.Foreground = System.Windows.Media.Brushes.White;
            element.Effect = null;
        }
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>Office-style series/slice colour palette (blue, orange, grey, gold, indigo, green).</summary>
    private static readonly Color[] ChartPalette =
    {
        Color.FromRgb(0x5B, 0x9B, 0xD5), Color.FromRgb(0xED, 0x7D, 0x31),
        Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xFF, 0xC0, 0x00),
        Color.FromRgb(0x44, 0x72, 0xC4), Color.FromRgb(0x70, 0xAD, 0x47)
    };

    /// <summary>
    /// Renders an inline chart as an InlineUIContainer hosting a Border that carries the model
    /// <see cref="Chart"/> on its Tag (so CommitToModel round-trips it, mirroring shapes). Renders **all**
    /// series, honours the chart <see cref="ChartKind"/> (column / bar / line / area / scatter / pie /
    /// doughnut), and shows a category-axis + a legend — a lightweight but type-faithful stand-in for the
    /// DrawingML chart. Sizes the plot Canvas explicitly so the code-positioned children land correctly in
    /// the headless print/measure pass (there is no live layout to query ActualWidth).
    /// </summary>
    private static InlineUIContainer BuildChartRun(Chart chart)
    {
        var widthPx = chart.WidthPt * PxPerPoint;
        var heightPx = chart.HeightPt * PxPerPoint;

        var root = new DockPanel { Margin = new Thickness(6), LastChildFill = true };

        if (!string.IsNullOrEmpty(chart.Title))
        {
            var title = new TextBlock
            {
                Text = chart.Title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
        }

        var showLegend = (chart.ShowLegend || chart.Series.Count > 1) && chart.Series.Count > 0;
        if (showLegend)
        {
            var legend = BuildChartLegend(chart);
            DockPanel.SetDock(legend, Dock.Bottom);
            root.Children.Add(legend);
        }

        var titleH = string.IsNullOrEmpty(chart.Title) ? 0 : 22;
        var legendH = showLegend ? 22 : 0;
        var plotW = Math.Max(24, widthPx - 12);
        var plotH = Math.Max(24, heightPx - 12 - titleH - legendH);
        var plot = new Canvas { Width = plotW, Height = plotH };
        switch (chart.Kind)
        {
            case ChartKind.Pie:
            case ChartKind.Doughnut:
                DrawPieChart(plot, chart, plotW, plotH, doughnut: chart.Kind == ChartKind.Doughnut);
                break;
            case ChartKind.Line:
            case ChartKind.Area:
            case ChartKind.Scatter:
                DrawLineChart(plot, chart, plotW, plotH);
                break;
            case ChartKind.Bar:
                DrawBarChart(plot, chart, plotW, plotH, horizontal: true);
                break;
            default: // Column
                DrawBarChart(plot, chart, plotW, plotH, horizontal: false);
                break;
        }
        root.Children.Add(plot);

        var element = new Border
        {
            Width = widthPx,
            Height = heightPx,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Child = root,
            Tag = chart // carries the model chart so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    private static int ChartCategoryCount(Chart chart)
    {
        var n = chart.Categories.Count;
        foreach (var s in chart.Series)
            n = Math.Max(n, s.Values.Count);
        return n;
    }

    private static double ChartMax(Chart chart)
    {
        var max = 0.0;
        foreach (var s in chart.Series)
            foreach (var v in s.Values)
                max = Math.Max(max, v);
        return Math.Max(1.0, max);
    }

    /// <summary>A centred horizontal legend: a colour swatch + label per series (or per slice for pie).</summary>
    private static FrameworkElement BuildChartLegend(Chart chart)
    {
        var pie = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;
        var labels = pie
            ? Enumerable.Range(0, ChartCategoryCount(chart))
                .Select(i => i < chart.Categories.Count && !string.IsNullOrEmpty(chart.Categories[i]) ? chart.Categories[i] : $"Item {i + 1}")
                .ToList()
            : chart.Series.Select((s, i) => string.IsNullOrEmpty(s.Name) ? $"Series {i + 1}" : s.Name!).ToList();

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < labels.Count; i++)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0) };
            item.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(ChartPalette[i % ChartPalette.Length])
            });
            item.Children.Add(new TextBlock { Text = labels[i], FontSize = 10, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(item);
        }
        return panel;
    }

    private static System.Windows.Shapes.Line ChartAxisLine(double x1, double y1, double x2, double y2) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF)),
        StrokeThickness = 1
    };

    /// <summary>Faint horizontal value gridlines across the plot (matching Word), drawn behind the data.</summary>
    private static void DrawChartGridlines(Canvas plot, double plotH, double w)
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        for (var i = 1; i <= 4; i++)
        {
            var y = plotH - plotH * i / 4.0;
            plot.Children.Add(new System.Windows.Shapes.Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }
    }

    /// <summary>Grouped column (vertical) or bar (horizontal) chart over all series, with category labels.</summary>
    private static void DrawBarChart(Canvas plot, Chart chart, double w, double h, bool horizontal)
    {
        var cats = ChartCategoryCount(chart);
        if (cats == 0 || chart.Series.Count == 0)
            return;
        var seriesCount = chart.Series.Count;
        var max = ChartMax(chart);

        if (!horizontal)
        {
            const double labelStrip = 14;
            var plotH = Math.Max(8, h - labelStrip);
            var groupW = w / cats;
            var gap = groupW * 0.15;
            var barW = Math.Max(1, (groupW - 2 * gap) / seriesCount);
            DrawChartGridlines(plot, plotH, w);
            plot.Children.Add(ChartAxisLine(0, plotH, w, plotH));
            for (var c = 0; c < cats; c++)
            {
                for (var s = 0; s < seriesCount; s++)
                {
                    var vals = chart.Series[s].Values;
                    if (c >= vals.Count)
                        continue;
                    var barH = plotH * (Math.Max(0, vals[c]) / max);
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = barW * 0.92,
                        Height = Math.Max(1, barH),
                        Fill = new SolidColorBrush(ChartPalette[s % ChartPalette.Length])
                    };
                    Canvas.SetLeft(rect, c * groupW + gap + s * barW);
                    Canvas.SetTop(rect, plotH - barH);
                    plot.Children.Add(rect);
                }
                AddCategoryLabel(plot, chart, c, c * groupW, plotH + 1, groupW, System.Windows.TextAlignment.Center);
            }
        }
        else
        {
            const double gutter = 42;
            var plotW = Math.Max(8, w - gutter);
            var groupH = h / cats;
            var gap = groupH * 0.15;
            var barH = Math.Max(1, (groupH - 2 * gap) / seriesCount);
            plot.Children.Add(ChartAxisLine(gutter, 0, gutter, h));
            for (var c = 0; c < cats; c++)
            {
                for (var s = 0; s < seriesCount; s++)
                {
                    var vals = chart.Series[s].Values;
                    if (c >= vals.Count)
                        continue;
                    var barW = plotW * (Math.Max(0, vals[c]) / max);
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = Math.Max(1, barW),
                        Height = barH * 0.92,
                        Fill = new SolidColorBrush(ChartPalette[s % ChartPalette.Length])
                    };
                    Canvas.SetLeft(rect, gutter);
                    Canvas.SetTop(rect, c * groupH + gap + s * barH);
                    plot.Children.Add(rect);
                }
                AddCategoryLabel(plot, chart, c, 0, c * groupH + groupH / 2 - 7, gutter - 3, System.Windows.TextAlignment.Right);
            }
        }
    }

    /// <summary>Multi-series line chart (also used for area/scatter): one polyline per series.</summary>
    private static void DrawLineChart(Canvas plot, Chart chart, double w, double h)
    {
        var cats = ChartCategoryCount(chart);
        if (cats == 0 || chart.Series.Count == 0)
            return;
        var max = ChartMax(chart);
        const double labelStrip = 14;
        var plotH = Math.Max(8, h - labelStrip);
        DrawChartGridlines(plot, plotH, w);
        plot.Children.Add(ChartAxisLine(0, plotH, w, plotH));

        double X(int c) => cats == 1 ? w / 2 : (c + 0.5) * (w / cats);

        for (var s = 0; s < chart.Series.Count; s++)
        {
            var vals = chart.Series[s].Values;
            var color = new SolidColorBrush(ChartPalette[s % ChartPalette.Length]);
            var poly = new System.Windows.Shapes.Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            for (var c = 0; c < vals.Count; c++)
                poly.Points.Add(new System.Windows.Point(X(c), plotH - plotH * (Math.Max(0, vals[c]) / max)));
            plot.Children.Add(poly);
        }

        for (var c = 0; c < cats; c++)
            AddCategoryLabel(plot, chart, c, X(c) - (w / cats) / 2, plotH + 1, w / cats, System.Windows.TextAlignment.Center);
    }

    /// <summary>Pie (or doughnut) chart over the first series' values, one slice per category.</summary>
    private static void DrawPieChart(Canvas plot, Chart chart, double w, double h, bool doughnut)
    {
        if (chart.Series.Count == 0)
            return;
        var vals = chart.Series[0].Values;
        var total = vals.Where(v => v > 0).Sum();
        if (total <= 0)
            return;

        var cx = w / 2;
        var cy = h / 2;
        var r = Math.Max(4, Math.Min(w, h) / 2 - 4);
        var start = -Math.PI / 2; // 12 o'clock
        for (var i = 0; i < vals.Count; i++)
        {
            if (vals[i] <= 0)
                continue;
            var sweep = (vals[i] / total) * 2 * Math.PI;
            var end = start + sweep;
            var fig = new PathFigure { StartPoint = new System.Windows.Point(cx, cy), IsClosed = true };
            fig.Segments.Add(new LineSegment(new System.Windows.Point(cx + r * Math.Cos(start), cy + r * Math.Sin(start)), true));
            fig.Segments.Add(new ArcSegment(
                new System.Windows.Point(cx + r * Math.Cos(end), cy + r * Math.Sin(end)),
                new System.Windows.Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            plot.Children.Add(new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(ChartPalette[i % ChartPalette.Length]),
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1,
                Data = geo
            });
            start = end;
        }

        if (doughnut)
        {
            var hole = new System.Windows.Shapes.Ellipse { Width = r, Height = r, Fill = System.Windows.Media.Brushes.White };
            Canvas.SetLeft(hole, cx - r / 2);
            Canvas.SetTop(hole, cy - r / 2);
            plot.Children.Add(hole);
        }
    }

    private static void AddCategoryLabel(Canvas plot, Chart chart, int index, double left, double top, double width, System.Windows.TextAlignment align)
    {
        if (index >= chart.Categories.Count || string.IsNullOrEmpty(chart.Categories[index]))
            return;
        var label = new TextBlock
        {
            Text = chart.Categories[index],
            FontSize = 9,
            Width = Math.Max(1, width),
            TextAlignment = align,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        plot.Children.Add(label);
    }

    /// <summary>Inserts an inline shape / text box at the caret. Size in points; preserved on save.</summary>
    public void InsertShape(Shape shape)
    {
        CommitToModel();
        var container = BuildShapeRun(shape);
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
    /// Renders an inline SmartArt diagram as an InlineUIContainer hosting a Border that carries the model
    /// <see cref="SmartArt"/> on its Tag (so CommitToModel round-trips it, mirroring shapes/charts). The
    /// border sketches the diagram's top-level node texts as a simple labelled stack (a lightweight visual
    /// stand-in — the diagram's real layout is recomputed by Word on open).
    /// </summary>
    private static InlineUIContainer BuildSmartArtRun(SmartArt smartArt)
    {
        var widthPx = smartArt.WidthPt * PxPerPoint;
        var heightPx = smartArt.HeightPt * PxPerPoint;

        // Lay top-level nodes out left-to-right for Process, top-to-bottom otherwise, as labelled boxes.
        var horizontal = smartArt.Kind == SmartArtKind.Process;
        var nodes = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var node in smartArt.Nodes)
            nodes.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x4E, 0x81, 0xBD)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = node.Text,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });

        var element = new Border
        {
            Width = widthPx,
            Height = heightPx,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            Child = nodes,
            Tag = smartArt // carries the model SmartArt so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// Renders an inline embedded OLE object as an InlineUIContainer hosting a Border that carries the model
    /// <see cref="EmbeddedObject"/> on its Tag (so CommitToModel round-trips it, mirroring shapes). Shows the
    /// object's icon image when present, otherwise a labelled package placeholder with its ProgID.
    /// </summary>
    private static InlineUIContainer BuildEmbeddedObjectRun(EmbeddedObject embedded)
    {
        var widthPx = embedded.WidthPt * PxPerPoint;
        var heightPx = embedded.HeightPt * PxPerPoint;

        FrameworkElement content;
        // The icon bytes are nominally PNG but real OLE objects carry icons in formats WIC cannot decode
        // (WMF/EMF/uncommon codecs); decode defensively so a bad icon falls back to the ProgID placeholder
        // instead of throwing NotSupportedException and blanking the whole document.
        var iconSource = embedded.Icon is { } icon ? TryDecodeRaster(icon.PngBytes) : null;
        if (iconSource is not null)
        {
            content = new Image
            {
                Source = iconSource,
                Stretch = Stretch.Uniform
            };
        }
        else
        {
            content = new TextBlock
            {
                Text = embedded.ProgId,
                Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4)
            };
        }

        var element = new Border
        {
            Width = widthPx,
            Height = heightPx,
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC8, 0xD8)),
            BorderThickness = new Thickness(1),
            Child = content,
            Tag = embedded // carries the model object so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>Inserts an inline equation at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertEquation(Equation equation) => InsertInlineContainer(BuildEquationRun(equation));

    /// <summary>Inserts an inline chart at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertChart(Chart chart) => InsertInlineContainer(BuildChartRun(chart));

    /// <summary>Inserts inline WordArt at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertWordArt(WordArt wordArt) => InsertInlineContainer(BuildWordArtRun(wordArt));

    /// <summary>Inserts an inline SmartArt diagram at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertSmartArt(SmartArt smartArt) => InsertInlineContainer(BuildSmartArtRun(smartArt));

    /// <summary>Inserts an inline embedded OLE object at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertEmbeddedObject(EmbeddedObject embedded) => InsertInlineContainer(BuildEmbeddedObjectRun(embedded));

    // Shared caret-insertion path for the tagged InlineUIContainers (shape/image/chart/wordart/equation):
    // commit pending edits, drop the container at the caret's paragraph (or the last block), commit + render.
    private void InsertInlineContainer(InlineUIContainer container)
    {
        CommitToModel();
        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        if (caret.Paragraph is { } paragraph)
            paragraph.Inlines.Add(container);
        else if (Document.Blocks.LastOrDefault() is WpfParagraph last)
            last.Inlines.Add(container);
        else
            Document.Blocks.Add(new WpfParagraph(container));
        CommitToModel();
        Render();
    }

    /// <summary>
    /// Best-effort "Section X of N" for the status bar: the total comes from <see cref="TextDocument.Sections"/>
    /// (reconstructed from <see cref="Paragraph.SectionBreak"/> markers plus the final body section); the current
    /// index is which section the caret's block falls in, counting section-break markers at or before the caret's
    /// top-level paragraph. A document with no section breaks reports "1 of 1". The mapping is approximate: in-editor
    /// edits that have not been saved may not preserve every section marker, so the count can simplify to 1.
    /// </summary>
    public (int Current, int Total) SectionInfo()
    {
        CommitToModel();
        var total = Math.Max(1, _model.Sections.Count);
        if (total == 1)
            return (1, 1);

        // Find the caret's containing top-level WPF paragraph ordinal, then count model section breaks
        // at or before the model block at that ordinal (model + WPF top-level blocks stay aligned for the
        // simple paragraph/table flow these documents use).
        var caretParagraph = CaretPosition?.Paragraph;
        var caretOrdinal = -1;
        if (caretParagraph is not null)
        {
            var ordinal = 0;
            foreach (var block in Document.Blocks)
            {
                if (ReferenceEquals(block, caretParagraph))
                {
                    caretOrdinal = ordinal;
                    break;
                }
                ordinal++;
            }
        }
        if (caretOrdinal < 0)
            return (total, total); // caret position unknown: report the last (body) section

        var current = 1;
        for (var i = 0; i < _model.Blocks.Count && i <= caretOrdinal; i++)
            if (_model.Blocks[i] is FreeW.Core.Model.Paragraph { SectionBreak: not null } && i < caretOrdinal)
                current++;
        return (Math.Clamp(current, 1, total), total);
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
    /// Paste the clipboard's text as unformatted text at the caret ("Paste Text Only"). The clipboard
    /// text is normalized (line endings canonicalized, control chars stripped — see
    /// <see cref="PasteText.Normalize"/>) and inserted through <see cref="InsertText"/>, which joins the
    /// run the caret sits in (so the pasted text inherits the destination formatting), replaces any active
    /// selection, and is captured by the undo stack. All source/rich formatting is discarded. A no-op when
    /// the clipboard holds no usable text. Reads <see cref="System.Windows.Clipboard"/> directly — no model
    /// or docx changes.
    /// </summary>
    public void PastePlainText() => PasteFromClipboard();

    /// <summary>
    /// Paste the clipboard's text and merge it into the destination's formatting ("Merge Formatting"). In
    /// FreeW, merging formatting means matching the destination: the pasted text takes the formatting of
    /// the run the caret sits in (the same path <see cref="PastePlainText"/> uses), rather than carrying
    /// the source's character formatting. The text is normalized (see <see cref="PasteText.Normalize"/>)
    /// and inserted via <see cref="InsertText"/> so it is undoable. A no-op when the clipboard holds no
    /// usable text.
    /// </summary>
    public void PasteMergeFormatting() => PasteFromClipboard();

    // Shared body for the paste-special commands: read the clipboard's text (guarding the absent/empty
    // case and the rare clipboard-access failure), normalize it, and insert it at the caret. Both
    // "Paste Text Only" and "Merge Formatting" resolve to match-destination insertion in FreeW, so they
    // share one implementation.
    private void PasteFromClipboard()
    {
        string raw;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;
            raw = System.Windows.Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard can be transiently locked by another process; treat that as nothing to paste.
            return;
        }

        var text = PasteText.Normalize(raw);
        if (text.Length == 0)
            return;

        InsertText(text);
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
        // Map the visible paragraph ordinal to its real model index (collapsed-heading drift): commit
        // re-splices hidden blocks back in, so a raw visible index would mis-target with a heading
        // collapsed before the selection. Identity when nothing is collapsed.
        paragraphIndex = ModelIndexFromVisible(paragraphIndex);
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
    /// The active bibliographic style (APA / MLA / Chicago) used when inserting in-text citations and the
    /// bibliography. Selected via the References group's "Citation Style" combo box; defaults to APA, which
    /// is the original author–year behaviour.
    /// </summary>
    public CitationStyle ActiveCitationStyle { get; set; } = CitationStyle.Apa;

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
        InsertText(Citations.FormatInText(source, ActiveCitationStyle));
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

        var bibliography = Citations.BuildBibliography(_model, ActiveCitationStyle);
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
    /// Insert a Table of Figures (or Table of Tables) generated from the document's <see cref="CaptionLabel"/>
    /// captions at the caret's block (else at the document end), routed one-by-one through the undo/redo bus
    /// so the insert is reversible — mirroring <see cref="InsertTableOfContents"/>. The paragraphs carry
    /// dedicated styles (registered via <see cref="TableOfFigures.EnsureStyles"/>) which both give them
    /// distinct formatting and mark the region for <see cref="RefreshTableOfFigures"/>.
    /// </summary>
    public void InsertTableOfFigures(CaptionLabel label = CaptionLabel.Figure)
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        TableOfFigures.EnsureStyles(_model);

        // Insert at the caret's block (a table of figures reads as front-/back-matter); fall back to the end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        InsertTableOfFiguresAt(index, label);
    }

    /// <summary>
    /// Rebuild the Table of Figures: remove the previously inserted region (paragraphs carrying a
    /// table-of-figures style, see <see cref="TableOfFigures.IsTableOfFiguresParagraph"/>) and re-insert a
    /// freshly generated one at the same position. With no existing region this behaves like
    /// <see cref="InsertTableOfFigures"/>, inserting at the document end. Every removal/insert is reversible
    /// through the undo/redo bus.
    /// </summary>
    public void RefreshTableOfFigures(CaptionLabel label = CaptionLabel.Figure)
    {
        CommitToModel();
        TableOfFigures.EnsureStyles(_model);

        // Find the first existing table-of-figures paragraph (the marker region anchor).
        var first = -1;
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (TableOfFigures.IsTableOfFiguresParagraph(_model.Blocks[i]))
            {
                first = i;
                break;
            }
        }

        var insertAt = first >= 0 ? first : _model.Blocks.Count;

        // Remove every existing table-of-figures paragraph (reversible). Collect first to avoid mutating
        // while scanning, then delete from the end so earlier indices stay valid.
        var indices = new List<int>();
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (TableOfFigures.IsTableOfFiguresParagraph(_model.Blocks[i]))
                indices.Add(i);
        }
        for (var i = indices.Count - 1; i >= 0; i--)
            _commands.Execute(new DeleteParagraphCommand(indices[i]));

        InsertTableOfFiguresAt(insertAt, label);
    }

    // Insert the freshly built table-of-figures paragraphs starting at block index `at`, one reversible
    // InsertParagraphCommand each (kept in order). The bus's Changed event redraws.
    private void InsertTableOfFiguresAt(int at, CaptionLabel label)
    {
        var entries = TableOfFigures.Build(_model, label);
        var index = Math.Clamp(at, 0, _model.Blocks.Count);
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
        // Map the visible paragraph ordinal to its real model index (collapsed-heading drift), as in
        // InsertComment. Identity when nothing is collapsed.
        paragraphIndex = ModelIndexFromVisible(paragraphIndex);
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
    /// Apply the Document Inspector's selected removal operations to the model and re-render. Pending
    /// edits are committed first so the removals run over the current text, then each selected category
    /// is stripped via the pure <see cref="DocumentInspector"/> ops (which mutate the model in place),
    /// and the view re-renders so the cleaned document shows immediately.
    /// </summary>
    public void ApplyInspectorRemovals(bool comments, bool revisions, bool properties, bool bookmarks)
    {
        CommitToModel();
        if (comments)
            DocumentInspector.RemoveComments(_model);
        if (revisions)
            DocumentInspector.RemoveRevisions(_model);
        if (properties)
            DocumentInspector.RemoveProperties(_model);
        if (bookmarks)
            DocumentInspector.RemoveBookmarks(_model);
        Render();
    }

    /// <summary>
    /// Removes the bookmark named <paramref name="name"/> from the document (clears the matching
    /// paragraph's <see cref="ModelParagraph.BookmarkName"/> via the pure <see cref="Bookmarks"/>
    /// helper), then re-renders so the cleared marker round-trips on the next commit. Used by the
    /// Bookmark Manager's Delete action. No-op for a null/empty name or an unknown bookmark.
    /// </summary>
    public void RemoveBookmark(string name)
    {
        CommitToModel();
        Bookmarks.RemoveBookmark(_model, name);
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
                    HyperlinkTooltip = run.HyperlinkTooltip,
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
                    HyperlinkTooltip = run.HyperlinkTooltip,
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
                    HyperlinkAnchor = run.HyperlinkAnchor,
                    HyperlinkTooltip = run.HyperlinkTooltip
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
                    HyperlinkAnchor = run.HyperlinkAnchor,
                    HyperlinkTooltip = run.HyperlinkTooltip
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

    // --- hyperlink management (edit / remove / screentip) ---

    /// <summary>
    /// The WPF <see cref="WpfHyperlink"/> wrapping the caret (the link the caret sits inside/next to), or
    /// null when the caret is not on a hyperlink. Walks the caret position's parent chain up to a link.
    /// </summary>
    private WpfHyperlink? HyperlinkAtCaret()
    {
        var pointer = CaretPosition;
        if (pointer is null)
            return null;
        // Prefer the element to the caret's left so a caret resting at a link's trailing edge still
        // resolves to that link; fall back to the right-hand element otherwise.
        DependencyObject? node = pointer.GetAdjacentElement(LogicalDirection.Backward)
            ?? pointer.GetAdjacentElement(LogicalDirection.Forward)
            ?? pointer.Parent;
        while (node is not null)
        {
            if (node is WpfHyperlink link)
                return link;
            node = node is TextElement te ? te.Parent : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    /// <summary>
    /// True when the caret sits on a hyperlink (external URL or internal bookmark). Lets the ribbon
    /// enable/disable the manage-link commands.
    /// </summary>
    public bool IsCaretOnHyperlink()
    {
        Focus();
        return HyperlinkAtCaret() is not null;
    }

    /// <summary>
    /// The current external URL of the hyperlink at the caret (its NavigateUri), or null when the caret
    /// is not on an external link. Used to seed the Edit Hyperlink prompt.
    /// </summary>
    public string? HyperlinkUrlAtCaret()
    {
        Focus();
        return HyperlinkAtCaret()?.NavigateUri?.ToString();
    }

    /// <summary>The current ScreenTip of the hyperlink at the caret, or null when none/not on a link.</summary>
    public string? HyperlinkTooltipAtCaret()
    {
        Focus();
        return (HyperlinkAtCaret()?.Tag as HyperlinkInfo)?.Tooltip;
    }

    /// <summary>
    /// Changes the external URL of the hyperlink at the caret to <paramref name="newUrl"/> (preserving its
    /// ScreenTip and visible text), re-styling it. A no-op when the caret is not on a link or the URL is
    /// not a valid absolute Uri. Commits + re-renders so the change round-trips.
    /// </summary>
    public void EditHyperlink(string newUrl)
    {
        Focus();
        if (string.IsNullOrWhiteSpace(newUrl) || !Uri.TryCreate(newUrl, UriKind.Absolute, out var uri))
            return;
        if (HyperlinkAtCaret() is not { } link)
            return;

        var tooltip = (link.Tag as HyperlinkInfo)?.Tooltip;
        // Re-target as an external link: drop any internal-anchor wiring and restyle for the new URL.
        link.Click -= OnInternalLinkClick;
        link.RequestNavigate -= OnHyperlinkRequestNavigate;
        link.NavigateUri = uri;
        StyleLink(link, newUrl, tooltip);

        CommitToModel();
        Render();
    }

    /// <summary>
    /// Removes the hyperlink at the caret, leaving its visible text in place (clears the URL/anchor and
    /// ScreenTip). A no-op when the caret is not on a link. Commits + re-renders.
    /// </summary>
    public void RemoveHyperlink()
    {
        Focus();
        if (HyperlinkAtCaret() is not { } link || link.Parent is not WpfParagraph paragraph)
            return;

        // Unwrap: replace the Hyperlink span with its child inlines, in order, then re-render so the
        // freed runs commit as plain (un-linked) text.
        var children = link.Inlines.ToList();
        var anchorPos = paragraph.Inlines.FirstOrDefault(inline => ReferenceEquals(inline, link));
        foreach (var child in children)
        {
            link.Inlines.Remove(child);
            if (anchorPos is not null)
                paragraph.Inlines.InsertBefore(anchorPos, child);
            else
                paragraph.Inlines.Add(child);
        }
        paragraph.Inlines.Remove(link);

        CommitToModel();
        Render();
    }

    /// <summary>
    /// Sets (or clears, when null/blank) the ScreenTip on the hyperlink at the caret. A no-op when the
    /// caret is not on a link. Commits + re-renders so the tip round-trips as w:hyperlink w:tooltip.
    /// </summary>
    public void SetHyperlinkTooltip(string? tip)
    {
        Focus();
        if (HyperlinkAtCaret() is not { } link)
            return;

        var tooltip = string.IsNullOrWhiteSpace(tip) ? null : tip.Trim();
        if ((link.Tag as HyperlinkInfo)?.Anchor is { Length: > 0 } anchor)
        {
            // Internal link: re-apply its bookmark styling carrying the new tip.
            link.Click -= OnInternalLinkClick;
            StyleInternalLink(link, anchor, tooltip);
        }
        else if (link.NavigateUri?.ToString() is { Length: > 0 } url)
        {
            // External link: re-apply its URL styling carrying the new tip.
            link.RequestNavigate -= OnHyperlinkRequestNavigate;
            StyleLink(link, url, tooltip);
        }
        else
        {
            return;
        }

        CommitToModel();
        Render();
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

    private static void StyleLink(WpfHyperlink link, string url, string? tooltip = null)
    {
        // External links carry no Anchor; the ScreenTip (when set) round-trips on the Tag and wins over
        // the default URL chrome tooltip.
        link.Tag = new HyperlinkInfo(null, tooltip);
        link.ToolTip = tooltip is { Length: > 0 } ? tooltip : url;
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
            // Right-to-left run direction reads back off the WPF run's FlowDirection (set in BuildRun).
            Rtl = run.FlowDirection == System.Windows.FlowDirection.RightToLeft,
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
        // WidowControl rides on the Tag (no FlowDocument property); KeepWithNext/KeepLinesTogether read
        // straight back off the WPF Paragraph's native properties set in BuildParagraph.
        var widowControl = paragraph.Tag is ParagraphTag { WidowControl: true };
        return ParagraphFormatting.Default with
        {
            Alignment = FromWpfAlignment(paragraph.TextAlignment),
            // Right-to-left direction reads straight back off the WPF Paragraph's FlowDirection (set in
            // BuildParagraph), so an RTL paragraph survives an edit/commit cycle.
            Rtl = paragraph.FlowDirection == System.Windows.FlowDirection.RightToLeft,
            KeepWithNext = paragraph.KeepWithNext,
            KeepLinesTogether = paragraph.KeepTogether,
            WidowControl = widowControl,
            SpaceBeforePt = paragraph.Margin.Top / PxPerPoint,
            SpaceAfterPt = paragraph.Margin.Bottom / PxPerPoint,
            // An exact line height (BlockLineHeight, set for the Exact rule) reads back as an absolute
            // height in points; otherwise the LineHeight is a multiple of the font size.
            LineRule = paragraph.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
                ? LineSpacingRule.Exact
                : LineSpacingRule.Multiple,
            LineHeightPt = paragraph.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
                && !double.IsNaN(paragraph.LineHeight)
                ? paragraph.LineHeight / PxPerPoint
                : 0,
            LineSpacing = paragraph.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
                ? ParagraphFormatting.Default.LineSpacing
                : ReadLineSpacing(paragraph.LineHeight, document),
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
    // in BuildParagraph (LineHeight = LineSpacing * ratio * defaultFontSize * PxPerPoint, where ratio is the
    // default font's natural line height). Must use the SAME ratio as the forward path or an edit/commit
    // cycle would shift every paragraph's spacing. An unset LineHeight is NaN; fall back to the model default
    // so editing text never silently flattens a paragraph's spacing.
    private static double ReadLineSpacing(double lineHeight, TextDocument document)
    {
        var fontPt = document.DefaultRun.FontSizePt ?? 11;
        var ratio = DefaultLineHeightRatio(document);
        if (double.IsNaN(lineHeight) || lineHeight <= 0 || fontPt <= 0 || ratio <= 0)
            return ParagraphFormatting.Default.LineSpacing;
        return lineHeight / (fontPt * PxPerPoint * ratio);
    }

    // One "line" in Word's Multiple line rule is the font's natural line height (ascent+descent+line gap),
    // which WPF surfaces as FontFamily.LineSpacing (Times ~1.15, Calibri ~1.22). Keyed on the document's
    // default font and cached, since BuildParagraph runs this for every paragraph on load. Forward and
    // inverse line-spacing math both call this so they stay exactly invertible.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> LineHeightRatioCache = new();
    private static double DefaultLineHeightRatio(TextDocument document)
    {
        var name = document.DefaultRun.FontFamily;
        if (string.IsNullOrEmpty(name))
            name = "Calibri";
        return LineHeightRatioCache.GetOrAdd(name, static n =>
        {
            try
            {
                var ratio = new System.Windows.Media.FontFamily(n).LineSpacing;
                return ratio > 0 ? ratio : 1.0;
            }
            catch
            {
                return 1.0;
            }
        });
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
        var p = paragraph.Formatting;
        // Per-property cascade: direct paragraph formatting wins; for any presentation property the
        // paragraph leaves at the model default, inherit the paragraph style's value (Word's cascade).
        // The previous all-or-nothing rule fell back to FreeW's hardcoded defaults for a paragraph that set
        // ANY property (e.g. a list kind), ignoring the style's spacing/indents. List membership, breaks
        // and toggles stay paragraph-intrinsic. (Most value-typed formatting can't distinguish "explicitly the
        // default" from "unset", so a property explicitly set to the default value inherits the style; the
        // fully-correct fix is nullable formatting recording only explicit props — a larger refactor. Line
        // spacing is the exception: it carries an explicit LineSpacingIsSet flag, so it cascades precisely.)
        if (paragraph.StyleId is { } id && document.Styles.TryGetValue(id, out var style))
        {
            var sp = style.Paragraph;
            if (p == ParagraphFormatting.Default)
                return sp;
            var d = ParagraphFormatting.Default;
            // Line spacing resolves as one unit (direct w:line ?? style w:line ?? the paragraph's own
            // inherited docDefault/built-in value, which the reader already baked into p). The IsSet flag
            // distinguishes an explicit setting from an inherited one, so a paragraph with no direct line
            // spacing correctly takes its style's — not the docDefault that masked it before.
            var lineFrom = p.LineSpacingIsSet ? p : sp.LineSpacingIsSet ? sp : p;
            return p with
            {
                Alignment = p.Alignment != d.Alignment ? p.Alignment : sp.Alignment,
                // Space before/after cascade on the explicit flag, not value-vs-default: a read paragraph
                // carries 0pt-after when it sets none, and 0 != the model's 8pt default would otherwise keep
                // the 0 and never inherit the style's spacing (packing styled list items tighter than Word).
                SpaceBeforePt = p.SpaceBeforeIsSet ? p.SpaceBeforePt : sp.SpaceBeforeIsSet ? sp.SpaceBeforePt : p.SpaceBeforePt,
                SpaceAfterPt = p.SpaceAfterIsSet ? p.SpaceAfterPt : sp.SpaceAfterIsSet ? sp.SpaceAfterPt : p.SpaceAfterPt,
                SpaceBeforeIsSet = p.SpaceBeforeIsSet || sp.SpaceBeforeIsSet,
                SpaceAfterIsSet = p.SpaceAfterIsSet || sp.SpaceAfterIsSet,
                LineSpacing = lineFrom.LineSpacing,
                LineRule = lineFrom.LineRule,
                LineHeightPt = lineFrom.LineHeightPt,
                LineSpacingIsSet = p.LineSpacingIsSet || sp.LineSpacingIsSet,
                IndentLeftPt = p.IndentLeftPt != d.IndentLeftPt ? p.IndentLeftPt : sp.IndentLeftPt,
                IndentRightPt = p.IndentRightPt != d.IndentRightPt ? p.IndentRightPt : sp.IndentRightPt,
                FirstLineIndentPt = p.FirstLineIndentPt != d.FirstLineIndentPt ? p.FirstLineIndentPt : sp.FirstLineIndentPt,
                Border = p.Border ?? sp.Border,
                ShadingColorHex = p.ShadingColorHex ?? sp.ShadingColorHex,
            };
        }
        return p;
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

    /// <summary>
    /// A non-editable overlay that draws formatting marks — a pilcrow (<see cref="FormattingMarks.Pilcrow"/>)
    /// at each paragraph end, a middle dot (<see cref="FormattingMarks.SpaceDot"/>) over every space and a
    /// right arrow (<see cref="FormattingMarks.TabArrow"/>) over every tab — on top of the adorned
    /// <see cref="DocumentView"/>. The glyphs are painted from the live FlowDocument's text geometry
    /// (<see cref="TextPointer.GetCharacterRect"/>) and are never part of the document content, so they
    /// cannot round-trip into the model through <see cref="CommitToModel"/>. The overlay is hit-test
    /// transparent so it never intercepts clicks/selection, and it repaints as the surface scrolls or
    /// relayouts (see the LayoutUpdated subscription) so the marks stay aligned with the text.
    /// </summary>
    private sealed class FormattingMarksAdorner : Adorner
    {
        // Faint grey so the marks read as light decorations rather than real text.
        private static readonly Brush MarkBrush = CreateMarkBrush();

        // Cap how many characters we scan per paragraph so a pathologically long line can never make the
        // overlay expensive to paint; the pilcrow at the paragraph end is always still drawn.
        private const int MaxCharsPerParagraph = 20_000;

        private readonly DocumentView _view;

        public FormattingMarksAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            // Repaint when the surface scrolls or relayouts so the glyphs track the text. LayoutUpdated
            // fires after scrolling/resize; invalidating here keeps the overlay aligned.
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
        }

        private static Brush CreateMarkBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_view.Document is not { } doc)
                return;

            // Clip drawing to the visible surface so glyphs for scrolled-off text are not painted onto
            // the chrome/margins around the editor.
            var bounds = new Rect(_view.RenderSize);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                var emPx = Math.Max(1.0, doc.FontSize);
                foreach (var block in doc.Blocks)
                    DrawBlockMarks(drawingContext, block, emPx, bounds);
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        // Walk a top-level block (paragraph, list, table, …) and draw the marks for every paragraph it
        // contains. Only paragraphs carry text positions/ends to decorate; container blocks recurse.
        private void DrawBlockMarks(DrawingContext dc, System.Windows.Documents.Block block, double emPx, Rect bounds)
        {
            switch (block)
            {
                case WpfParagraph paragraph:
                    DrawParagraphMarks(dc, paragraph, emPx, bounds);
                    break;
                case WpfList list:
                    foreach (var item in list.ListItems)
                        foreach (var inner in item.Blocks)
                            DrawBlockMarks(dc, inner, emPx, bounds);
                    break;
                case WpfTable table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var inner in cell.Blocks)
                                    DrawBlockMarks(dc, inner, emPx, bounds);
                    break;
            }
        }

        // Draw the space/tab glyphs along a paragraph and a pilcrow at its end. Each glyph is placed at
        // the character's on-screen rectangle (translated from the editor's content coordinates into the
        // adorner's). Positions are obtained per character from the FlowDocument, so nothing is written
        // back into the document — the overlay is purely additive paint.
        private void DrawParagraphMarks(DrawingContext dc, WpfParagraph paragraph, double emPx, Rect bounds)
        {
            var pointer = paragraph.ContentStart;
            var end = paragraph.ContentEnd;
            var scanned = 0;

            while (pointer is not null
                && pointer.CompareTo(end) < 0
                && scanned < MaxCharsPerParagraph)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var text = pointer.GetTextInRun(LogicalDirection.Forward);
                    for (var i = 0; i < text.Length && scanned < MaxCharsPerParagraph; i++)
                    {
                        var c = text[i];
                        if (c is ' ' or '\t')
                        {
                            var glyphPos = pointer.GetPositionAtOffset(i, LogicalDirection.Forward);
                            if (glyphPos is not null)
                                DrawGlyphAt(dc, glyphPos, c == ' ' ? FormattingMarks.SpaceDot : FormattingMarks.TabArrow, emPx, bounds);
                        }
                        scanned++;
                    }
                }

                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }

            // The pilcrow sits just after the last content position of the paragraph.
            DrawGlyphAt(dc, end, FormattingMarks.Pilcrow, emPx, bounds, atEnd: true);
        }

        // Draw a single glyph anchored at the character rectangle of `position`. `atEnd` requests the
        // rectangle on the backward side (the paragraph's trailing edge) so the pilcrow lands after the
        // last glyph rather than before the following block.
        private void DrawGlyphAt(DrawingContext dc, TextPointer position, char glyph, double emPx, Rect bounds, bool atEnd = false)
        {
            Rect rect;
            try
            {
                rect = position.GetCharacterRect(atEnd ? LogicalDirection.Backward : LogicalDirection.Forward);
            }
            catch (InvalidOperationException)
            {
                // The document layout can be momentarily unavailable during a relayout; skip this glyph.
                return;
            }

            if (rect.IsEmpty)
                return;

            // GetCharacterRect is relative to the editor's content; the adorner shares the editor's
            // coordinate space (it adorns the same element), so the rect maps directly. Cull glyphs that
            // fall outside the visible surface.
            if (rect.Bottom < bounds.Top || rect.Top > bounds.Bottom || rect.Right < bounds.Left || rect.Left > bounds.Right)
                return;

            var fontSize = Math.Max(6.0, rect.Height > 0 ? rect.Height * 0.72 : emPx * 0.72);
            var formatted = new FormattedText(
                glyph.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                MarkBrush,
                VisualTreeHelper.GetDpi(_view).PixelsPerDip);

            // Centre the glyph on the character cell: horizontally at the cell's left edge (where the
            // space/tab/end sits) and vertically within the line.
            var x = atEnd ? rect.Left : rect.Left + Math.Max(0, (rect.Width - formatted.Width) / 2);
            var y = rect.Top + Math.Max(0, (rect.Height - formatted.Height) / 2);
            dc.DrawText(formatted, new Point(x, y));
        }
    }

    /// <summary>
    /// Draws the faint "— Page N —" break markers down the Print-Layout editing surface, so the user
    /// perceives where the single continuous flow would break across printed pages.
    ///
    /// APPROXIMATION: the editable surface is one continuous WPF flow (see the limitation note on
    /// <see cref="ApplyPageChrome"/>), so there are no real per-page boxes to read. Instead we anchor at
    /// the top of the first laid-out content line (its character rectangle) and step downward by the page's
    /// printable content height (<see cref="PageLayout.ContentAreaDip"/>, the same page geometry the print
    /// path uses), drawing a marker at each multiple. This assumes uniform content flow: it does not model
    /// per-block keep-together rules, explicit page breaks, tables that straddle a boundary, or differing
    /// first-page geometry, so a marker can land a line or two away from where the printed page would
    /// actually break. It is a low-key visual cue, not an exact pagination — Print Preview remains the
    /// authoritative paginated view. Markers past the end of the content are not drawn.
    ///
    /// Coordinates: the adorner shares the editor's content coordinate space (it adorns the same element),
    /// and the editor's <see cref="DocumentView.LayoutTransform"/> zoom scales the adorner with it, so the
    /// markers track the text under zoom without extra math. Painting is clipped to the visible surface.
    /// </summary>
    private sealed class PageBreakAdorner : Adorner
    {
        private static readonly Pen BreakPen = CreateBreakPen();
        private static readonly Brush LabelBrush = CreateLabelBrush();

        // Never draw more than this many markers, so a tiny page height (degenerate geometry) or an
        // enormous document can't make the overlay expensive to paint.
        private const int MaxMarkers = 2_000;

        private readonly DocumentView _view;

        public PageBreakAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            // Repaint when the surface scrolls or relayouts so the markers track the content.
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
        }

        private static Pen CreateBreakPen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), 1.0)
            {
                DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
            };
            pen.Freeze();
            return pen;
        }

        private static Brush CreateLabelBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_view.Document is not { } doc)
                return;

            // The page's printable content height in DIP — the same geometry the print path paginates by.
            var (_, contentHeight) = PageLayout.ContentAreaDip(_view._model.Page);
            if (contentHeight <= 0)
                return;

            // Anchor at the top of the first laid-out content line. Without a first rectangle (empty/just
            // re-rendered document) there is nothing to anchor to, so skip painting this pass.
            var origin = FirstContentTop(doc);
            if (origin is not { } topY)
                return;

            var bounds = new Rect(_view.RenderSize);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                var pixelsPerDip = VisualTreeHelper.GetDpi(_view).PixelsPerDip;
                for (var pageIndex = 1; pageIndex <= MaxMarkers; pageIndex++)
                {
                    var y = topY + pageIndex * contentHeight; // bottom of page `pageIndex`
                    if (y > bounds.Bottom)
                        break; // first boundary past the bottom of the viewport: nothing more is visible
                    if (y < bounds.Top)
                        continue; // boundary scrolled above the viewport — skip but keep counting pages

                    // The rule sits at the foot of page `pageIndex`; the page beginning below it is the next.
                    DrawMarker(drawingContext, y, pageIndex + 1, bounds, pixelsPerDip);
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        // The top Y (in the editor's content coordinates) of the first laid-out content line, or null when
        // no rectangle is available yet. Used as the origin the page-height stepping counts from.
        private static double? FirstContentTop(FlowDocument doc)
        {
            var start = doc.ContentStart;
            try
            {
                var rect = start.GetCharacterRect(LogicalDirection.Forward);
                return rect.IsEmpty ? null : rect.Top;
            }
            catch (InvalidOperationException)
            {
                // Layout momentarily unavailable during a relayout; skip this pass.
                return null;
            }
        }

        // Draw one page-break marker: a dashed rule spanning the page content width with a small centred
        // "— Page N —" label, so it reads as a low-key boundary cue rather than real content.
        private void DrawMarker(DrawingContext dc, double y, int pageNumber, Rect bounds, double pixelsPerDip)
        {
            dc.DrawLine(BreakPen, new Point(bounds.Left, y), new Point(bounds.Right, y));

            var label = new FormattedText(
                $"Page {pageNumber}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9.0,
                LabelBrush,
                pixelsPerDip);

            // Sit the label just below the rule, centred across the visible width.
            var x = bounds.Left + Math.Max(0, (bounds.Width - label.Width) / 2);
            dc.DrawText(label, new Point(x, y + 1));
        }
    }
}
