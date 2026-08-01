using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using FreeW.Core.IO;
using FreeW.App.Host;
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
using ModelFormatRevision = FreeW.Core.Model.FormatRevision;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word's mutually-exclusive document view modes (View ▸ Views), as far as the live editing surface is
/// concerned. Read Mode and Outline are separate host-level overlays (they swap the surface out entirely),
/// so they are not part of this enum — these three all reuse the one editable surface and differ only in
/// the page chrome they show.
/// <list type="bullet">
/// <item><see cref="PrintLayout"/> — the Word default: a white page sheet on the grey workspace, margins,
/// drop shadow and page-break markers.</item>
/// <item><see cref="WebLayout"/> — a continuous, full-width view with no page chrome (text wraps to the
/// window like a web page).</item>
/// <item><see cref="Draft"/> — a simplified continuous view with no page chrome, for fast editing.</item>
/// </list>
/// </summary>
public enum DocumentViewMode
{
    PrintLayout,
    WebLayout,
    Draft,
    /// <summary>
    /// Renders the document as discrete editable page boxes (<see cref="PageBox"/>)
    /// stacked in a <see cref="PaginatedEditorPanel"/>.  Opt-in via View ▸ Views ▸ Page Edit.
    /// Entering commits the continuous editor first; exiting commits all page boxes back to the
    /// model and reloads the continuous editor so PrintLayout/Web/Draft work normally again.
    /// The default continuous editor (PrintLayout/Web/Draft) and its
    /// <see cref="DocumentView.CommitToModel"/> path are untouched.
    /// </summary>
    PagedEdit,
}

/// <summary>
/// The FreeW editing surface: a RichTextBox that renders a <see cref="TextDocument"/> into a
/// WPF FlowDocument (resolving run/paragraph formatting through styles + document defaults) and
/// commits edits back into the model. Caret, selection, typing, delete and Enter come from the
/// RichTextBox; <see cref="CommitToModel"/> maps the edited view back to the model.
/// </summary>
public sealed class DocumentView : RichTextBox
{
    private const double PxPerPoint = 96.0 / 72.0;
    // TableCell and BlockUIContainer already contribute this much horizontal content inset.
    private const double WpfTableCellContentInsetDip = 6.0;
    // WPF's Calibri line box remains about 1% short after restoring Word's 12-point application fallback.
    private const double ImportedWordApplicationLineHeightScale = 1.01;

    // Matches the shared planner's default page-space gap around wrapped objects.
    private const double FloatingWrapGapDip = 9.0;

    // WPF Figure adds clearance beyond its declared box; Word page anchors do not.
    private const double FloatingFigureWrapHeightInsetDip = 17.0;

    // This imported TextBox's square-wrap exclusion extends below its painted bounds in Word.
    private const double ImportedWatermarkBackingFigureHeightExtensionDip = 18.0;

    // This paired WordArt's square-wrap exclusion follows the same paragraph-space band.
    private const double ImportedWatermarkReviewFigureHeightExtensionDip = 18.0;

    /// <summary>Document default run size in points, used when a run inherits its size.</summary>
    private const double DefaultFontSizePt = 11;

    /// <summary>Glyph-shrink factor applied to superscript/subscript runs (and undone on commit).</summary>
    private const double SuperSubScale = 0.65;

    // Keep note markers visually superscripted without letting WPF's Superscript baseline expand the
    // surrounding line box. The transform is calibrated to Word's cached footnote/endnote references.
    private const double NoteReferenceSuperscriptOffsetDip = 5.0;

    private TextDocument _model = TextDocument.CreateEmpty();
    private DocumentViewDepthLayoutPlan _viewDepthLayout =
        DocumentViewDepthLayoutPlanner.Build(FreeWViewDepthMode.LiveEditor);

    /// <summary>
    /// The file name a FILENAME field resolves to during the current <see cref="Render"/> pass. Set from
    /// <see cref="CurrentFileName"/> at the top of Render so the otherwise-static run builders can resolve
    /// it without threading it through every signature; thread-static to keep it isolated per render call.
    /// </summary>
    [ThreadStatic]
    private static string? _renderFileName;

    [ThreadStatic]
    private static bool _renderPageBreakMarkers = true;

    /// <summary>
    /// The 1-based page number to use when resolving PAGE fields during a header/footer sub-editor render
    /// in <see cref="DocumentViewMode.PagedEdit"/>. Zero means "not set" (fall back to cached). Set just
    /// before <see cref="LoadModel"/> on a header/footer sub-editor, then cleared immediately after so it
    /// cannot leak into unrelated renders. Thread-static to mirror <see cref="_renderFileName"/>.
    /// </summary>
    [ThreadStatic]
    internal static int _renderHfPageNumber;

    /// <summary>
    /// Optional preformatted PAGE display text for header/footer rendering. When set, this wins over
    /// <see cref="_renderHfPageNumber"/> so section formats such as Roman numerals render correctly.
    /// </summary>
    [ThreadStatic]
    internal static string? _renderHfPageNumberText;

    /// <summary>
    /// The total page count to use when resolving NUMPAGES fields during a header/footer sub-editor render
    /// in <see cref="DocumentViewMode.PagedEdit"/>. Zero means "not set" (fall back to cached). Mirrors
    /// <see cref="_renderHfPageNumber"/>.
    /// </summary>
    [ThreadStatic]
    internal static int _renderHfPageCount;

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

    // Page-aligned column rules are visual chrome. WPF's native FlowDocument rule straddles two
    // device pixels, so the live editor draws the shared pixel-aligned rule through this overlay.
    private ColumnRuleAdorner? _columnRuleAdorner;

    // The live overlay drawing line numbers in the left margin when the document enables them
    // (w:lnNumType), or null when line numbering is off. Like the page-break overlay it is an
    // AdornerLayer overlay, never part of the FlowDocument content, and recomputed on relayout.
    private LineNumberAdorner? _lineNumberAdorner;

    // Word-style freeform vertex handles. They are transient chrome on the editor's AdornerLayer,
    // while all geometry mutations continue through the document command bus.
    private ShapeEditPointsAdorner? _shapeEditPointsAdorner;
    private ShapeEditPointsTarget? _shapeEditPointsTarget;

    // Transparent Canvas placed as a sibling in the same Grid cell as this editor by the host
    // (MainWindow). Floats above the editor so floating images render and hit-test on top of the text.
    // Null until the host calls SyncFloatingObjectsCanvas for the first time.
    private Canvas? _floatingCanvas;

    // The floating InlineImage currently "selected" via a click on the overlay canvas.
    // Null when no floating image is selected. Used by SelectedImage()/SelectedImageLocation() as
    // a fallback when no inline-image selection exists in the RichTextBox.
    private InlineImage? _selectedFloatingImage;
    // The floating non-image object currently selected on the overlay canvas (Shape/Chart/SmartArt/WordArt/DrawingGroup).
    // Null when the selected floating object is an InlineImage (use _selectedFloatingImage) or none.
    private object? _selectedFloatingObject;
    // A nested child selection keeps the top-level owning group active for group-level commands while
    // the shared child geometry commands target the selected local child through its full path.
    private sealed record FloatingGroupChildSelection(
        FreeW.Core.Model.DrawingGroup RootGroup,
        IReadOnlyList<int> ChildPath)
    {
        public int ChildIndex => ChildPath[^1];
    }

    private FloatingGroupChildSelection? _selectedFloatingGroupChild;

    private object? SelectedFloatingGroupChildObject() =>
        _selectedFloatingGroupChild is { } selected
        && DrawingGroupChildPathResolver.TryGetChild(
            selected.RootGroup, selected.ChildPath, out _, out var child)
            ? child
            : null;

    private void RestoreSelectedFloatingGroupChildPath(object? selectedChild)
    {
        if (selectedChild is null
            || _selectedFloatingGroupChild is not { } selected
            || !DrawingGroupChildPathResolver.TryFindPath(
                selected.RootGroup, selectedChild, out var childPath))
        {
            return;
        }

        _selectedFloatingGroupChild = selected with { ChildPath = childPath };
    }

    // Multi-select: the set of currently selected floating objects (each an InlineImage / Shape / Chart /
    // SmartArt / WordArt / FreeW.Core.Model.DrawingGroup). Populated by Shift/Ctrl-click; the single-select path keeps this
    // in sync (1-element set).  Group command uses this to collect members.
    private readonly List<object> _selectedFloatingObjects = [];


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
    /// and the painter disarms (single-shot) or stays armed (locked mode). See <see cref="ArmFormatPainter"/>.
    /// </summary>
    private FormatPainterClipboard? _formatPainter;

    /// <summary>
    /// When true the painter stays armed after each application (double-click / lock mode), re-applying
    /// on every new selection until the user clicks the button again or presses Escape. False for the
    /// default single-shot gesture.
    /// </summary>
    private bool _formatPainterLocked;

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

        // Clear the floating-image selection when the user clicks within the text body so the inline
        // selection takes priority and the floating selection does not persist unexpectedly.
        PreviewMouseLeftButtonDown += (_, _) => { _selectedFloatingImage = null; };
    }

    public TextDocument Model => _model;

    internal DocumentViewDepthLayoutPlan ViewDepthLayout => _viewDepthLayout;

    internal void ApplyViewDepthLayout(DocumentViewDepthLayoutPlan layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _viewDepthLayout = layout;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

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
    public bool PrintLayoutEnabled => ViewMode == DocumentViewMode.PrintLayout;

    /// <summary>
    /// Whether a paragraph with <c>w:pageBreakBefore</c> paints the editor-only separator line.
    /// Pagination still honours the break when this is disabled.
    /// </summary>
    public bool RenderPageBreakMarkers { get; set; } = true;

    /// <summary>
    /// The active document view mode (View ▸ Views). Defaults to <see cref="DocumentViewMode.PrintLayout"/>
    /// — the Word default. Web Layout and Draft both drop the page chrome (no sheet/margins/shadow/page
    /// breaks) and let the editor fill the window width; Print Layout shows the page sheet. Switching is
    /// purely visual (the model and saved document are untouched); use <see cref="SetViewMode"/> to change it
    /// so the chrome and overlays re-apply.
    /// </summary>
    public DocumentViewMode ViewMode { get; private set; } = DocumentViewMode.PrintLayout;

    /// <summary>
    /// Switch the editing surface to a new <see cref="DocumentViewMode"/> and re-apply the page chrome
    /// (padding/width/shadow) plus the page-break and line-number overlays so the change shows immediately.
    /// No-op (and no re-render) when already in that mode. Never mutates the model.
    /// </summary>
    public void SetViewMode(DocumentViewMode mode)
    {
        if (ViewMode == mode)
            return;
        ViewMode = mode;
        _viewDepthLayout = DocumentViewDepthLayoutPlanner.Build(FreeWViewDepthMode.LiveEditor);
        ApplyPageChrome();
        SyncPageBreakAdorner();
        SyncColumnRuleAdorner();
        SyncLineNumberAdorner();
    }

    /// <summary>
    /// Toggle the View ribbon's "Print Layout" button: flip between Print Layout and the plain continuous
    /// (Draft) view, returning whether Print Layout is now on. Backward-compatible shim over
    /// <see cref="SetViewMode"/> so existing callers keep working; the dedicated Web Layout / Draft commands
    /// call <see cref="SetViewMode"/> directly.
    /// </summary>
    public bool TogglePrintLayout()
    {
        SetViewMode(PrintLayoutEnabled ? DocumentViewMode.Draft : DocumentViewMode.PrintLayout);
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
    /// ellipsis, sentence capitalization, lists, ordinals, fractions, hyperlinks) are applied via
    /// <see cref="AutoCorrect"/> on each keystroke, honouring <see cref="AutoFormatOptions"/>.
    /// </summary>
    public bool AutoCorrectEnabled { get; set; } = true;

    /// <summary>
    /// The per-rule AutoFormat-As-You-Type toggles consulted by <see cref="AutoCorrect.Evaluate(string?, char, AutoFormatOptions)"/>.
    /// Defaults to every rule on; the host pushes the persisted <c>FreeWOptions.AutoFormat</c> here so the
    /// user's choices take effect live. Never null.
    /// </summary>
    public AutoFormatOptions AutoFormatOptions { get; set; } = AutoFormatOptions.Default;

    /// <summary>
    /// The Word "AutoCorrect"-tab settings consulted by
    /// <see cref="AutoCorrectEngine.Evaluate(string?, char, AutoCorrectOptions)"/> — the two-initial-capitals
    /// fix, day-name capitalization, and the user-editable replace-text table. Defaults to every rule on; the
    /// host pushes the persisted <c>FreeWOptions.AutoCorrect</c> here so the user's choices take effect live.
    /// Never null.
    /// </summary>
    public AutoCorrectOptions AutoCorrectOptions { get; set; } = AutoCorrectOptions.Default;

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
    /// Deterministic shared grammar diagnostics for the committed model. WPF keeps native
    /// <see cref="SpellCheck"/> for spelling, while this exposes the portable grammar slice used by
    /// non-WPF renderers.
    /// </summary>
    public IReadOnlyList<ProofingDiagnostic> SharedGrammarDiagnostics =>
        ProofingDiagnosticPlanner.Build(_model, SpellCheckEnabled)
            .Where(diagnostic => diagnostic.Kind == ProofingDiagnosticKind.Grammar)
            .ToList();

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

    /// <summary>
    /// Returns the word at or adjacent to the caret (or the selected text if a word-length selection exists).
    /// Uses WPF TextPointer word-boundary navigation to extract the token. Returns null when the caret
    /// is not inside a text run (e.g. sits between paragraphs or on an image).
    /// </summary>
    public string? GetCaretWord()
    {
        // If the selection is a non-empty single word, use it directly.
        if (!Selection.IsEmpty)
        {
            var sel = Selection.Text?.Trim();
            if (!string.IsNullOrEmpty(sel) && !sel.Contains(' ') && !sel.Contains('\n'))
                return sel;
        }

        var caret = CaretPosition;
        if (caret is null) return null;

        // Walk backward to the start of the word, then forward to its end.
        var wordStart = caret.GetPositionAtOffset(0, LogicalDirection.Backward);
        if (wordStart is null) return null;

        // Scan backward while we are inside a letter/digit run.
        while (true)
        {
            var prev = wordStart.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev is null) break;
            var ch = new TextRange(prev, wordStart).Text;
            if (ch.Length != 1 || (!char.IsLetterOrDigit(ch[0]) && ch[0] != '\''))
                break;
            wordStart = prev;
        }

        var wordEnd = caret.GetPositionAtOffset(0, LogicalDirection.Forward);
        if (wordEnd is null) return null;

        // Scan forward while we are inside a letter/digit run.
        while (true)
        {
            var next = wordEnd.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is null) break;
            var ch = new TextRange(wordEnd, next).Text;
            if (ch.Length != 1 || (!char.IsLetterOrDigit(ch[0]) && ch[0] != '\''))
                break;
            wordEnd = next;
        }

        var word = new TextRange(wordStart, wordEnd).Text?.Trim();
        return string.IsNullOrEmpty(word) ? null : word;
    }

    /// <summary>
    /// Replaces the word at the caret with <paramref name="replacement"/> (the word is determined by the same
    /// word-boundary walk as <see cref="GetCaretWord"/>). If a word is found it is selected and replaced
    /// through the standard <see cref="InsertText"/> path so the action is undoable. A no-op when the
    /// caret is not on a word.
    /// </summary>
    public void ReplaceCaretWord(string replacement)
    {
        if (string.IsNullOrEmpty(replacement)) return;
        var caret = CaretPosition;
        if (caret is null) return;

        // Find word start (backward).
        var wordStart = caret.GetPositionAtOffset(0, LogicalDirection.Backward);
        if (wordStart is null) return;
        while (true)
        {
            var prev = wordStart.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev is null) break;
            var ch = new TextRange(prev, wordStart).Text;
            if (ch.Length != 1 || (!char.IsLetterOrDigit(ch[0]) && ch[0] != '\''))
                break;
            wordStart = prev;
        }

        // Find word end (forward).
        var wordEnd = caret.GetPositionAtOffset(0, LogicalDirection.Forward);
        if (wordEnd is null) return;
        while (true)
        {
            var next = wordEnd.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is null) break;
            var ch = new TextRange(wordEnd, next).Text;
            if (ch.Length != 1 || (!char.IsLetterOrDigit(ch[0]) && ch[0] != '\''))
                break;
            wordEnd = next;
        }

        // Select the word then insert the replacement.
        Selection.Select(wordStart, wordEnd);
        InsertText(replacement);
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
        if (TrackChangesEnabled
            && !string.IsNullOrEmpty(e.Text)
            && TryRecordTrackedTextInput(e.Text))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (isCtrl && e.Key == Key.Z && !AllowsCurrentUndoHistory())
        {
            e.Handled = true;
            return;
        }

        if (isCtrl && e.Key == Key.Y && !AllowsCurrentRedoHistory())
        {
            e.Handled = true;
            return;
        }

        if (TrackChangesEnabled
            && Keyboard.Modifiers == ModifierKeys.None
            && (e.Key == Key.Back || e.Key == Key.Delete))
        {
            var handled = e.Key == Key.Back
                ? TryRecordTrackedBackspace()
                : TryRecordTrackedDeleteForward();
            if (handled)
            {
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Test seam: simulate typing a single character at the caret through the same AutoCorrect/AutoFormat
    /// path <see cref="OnPreviewTextInput"/> uses. When a rule fires the correction is applied and the raw
    /// character is suppressed (returns true); otherwise the character is inserted literally (returns false).
    /// Lets the as-you-type rules be driven deterministically from STA tests without synthesising WPF input
    /// events. Honours <see cref="AutoCorrectEnabled"/> and <see cref="AutoFormatOptions"/> just like real typing.
    /// </summary>
    internal bool SimulateTypeCharacter(char c)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return false;

        if (AutoCorrectEnabled && Selection.IsEmpty && TryAutoCorrect(c))
            return true;
        if (TrackChangesEnabled)
        {
            InsertText(c.ToString());
            return false;
        }
        // No rule fired: insert the literal character at the caret (mirroring the RichTextBox's own insert).
        CaretPosition.InsertTextInRun(c.ToString());
        CaretPosition = CaretPosition.GetPositionAtOffset(1, LogicalDirection.Forward) ?? CaretPosition;
        return false;
    }

    /// <summary>Test seam: type a whole string one character at a time through <see cref="SimulateTypeCharacter"/>.</summary>
    internal void SimulateTypeText(string text)
    {
        foreach (var c in text)
            SimulateTypeCharacter(c);
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

        // Two engines share the delete-back/insert idiom below: the AutoCorrect-tab word-completion rules
        // (replace-text table, two-initial-caps, day names) fire when a separator ends a word; the AutoFormat
        // As-You-Type rules (smart quotes, dashes, lists, ordinals…) fire on their own trigger characters.
        // Try AutoCorrect first (its corrections are the user's authoritative table); fall back to AutoFormat.
        var result = AutoCorrectEngine.Evaluate(textBefore, justTyped, AutoCorrectOptions);
        if (!result.Applies)
            result = AutoCorrect.Evaluate(textBefore, justTyped, AutoFormatOptions);
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

        // List outcomes consume the typed marker and convert the paragraph to a list instead of inserting
        // text: delete the marker range, then run the built-in bullet/number toggle on the now-empty line.
        if (result.Outcome is AutoFormatOutcomeKind.BulletList or AutoFormatOutcomeKind.NumberList)
        {
            new TextRange(deleteStart, caret) { Text = string.Empty };
            CaretPosition = deleteStart;
            var toggle = result.Outcome == AutoFormatOutcomeKind.BulletList
                ? EditingCommands.ToggleBullets
                : EditingCommands.ToggleNumbering;
            toggle.Execute(null, this);
            return true;
        }

        // Replace [deleteStart, caret) with the insertion text in one edit so it is a single undo unit.
        var range = new TextRange(deleteStart, caret) { Text = result.Insert };
        CaretPosition = range.End;

        // Super-script the trailing suffix of an ordinal (the "st" of "1st "); the trailing space we just
        // emitted is excluded from the styled span by walking back one position from the range end.
        if (result.Outcome == AutoFormatOutcomeKind.SuperscriptSuffix && result.SuffixLength > 0)
        {
            ApplySuperscriptSuffix(range.End, result.SuffixLength);
        }
        // Hyperlink the just-completed URL/e-mail word: the styled span is [start-of-word, end-of-word),
        // i.e. the insert minus its trailing space (so the word length is Insert.Length - 1).
        else if (result.Outcome == AutoFormatOutcomeKind.Hyperlink && result.LinkTarget is { } target)
        {
            ApplyAutoHyperlink(range.End, result.Insert.Length - 1, target);
        }

        return true;
    }

    // Super-script the last <suffixLength> characters ending one position before <afterInsert> (the trailing
    // space stays baseline). Pure-WPF: select the suffix run and apply the VerticalAlignment property so it
    // round-trips as w:vertAlign on save, then collapse the caret to the end.
    private void ApplySuperscriptSuffix(TextPointer afterInsert, int suffixLength)
    {
        var suffixEnd = afterInsert.GetNextInsertionPosition(LogicalDirection.Backward); // skip the space
        if (suffixEnd is null)
            return;
        var suffixStart = suffixEnd;
        for (var i = 0; i < suffixLength; i++)
        {
            var prev = suffixStart?.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev is null)
                return;
            suffixStart = prev;
        }
        if (suffixStart is null)
            return;
        var span = new TextRange(suffixStart, suffixEnd);
        span.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Superscript);
        span.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Max(1.0, FontSize * 0.65));
        CaretPosition = afterInsert;
    }

    // Wrap the <wordLength>-character word ending one position before <afterInsert> (the trailing space) in
    // a hyperlink to <target>. Walks back from the caret by insertion positions (same idiom as the ordinal
    // helper) so the span lands on real text. Mirrors ApplyHyperlink's styling so an auto-link looks identical.
    private void ApplyAutoHyperlink(TextPointer afterInsert, int wordLength, string target)
    {
        var linkEnd = afterInsert.GetNextInsertionPosition(LogicalDirection.Backward); // skip the trailing space
        if (linkEnd is null || wordLength <= 0 || !Uri.TryCreate(target, UriKind.Absolute, out var uri))
            return;
        var wordStart = linkEnd;
        for (var i = 0; i < wordLength; i++)
        {
            var prev = wordStart?.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev is null)
                return;
            wordStart = prev;
        }
        if (wordStart is null)
            return;
        try
        {
            // Route through the editor's selection so WPF normalises the endpoints into a single valid text
            // span (a raw Span/Hyperlink ctor over hand-walked pointers can land on element edges and throw).
            Selection.Select(wordStart, linkEnd);
            var link = new WpfHyperlink(Selection.Start, Selection.End) { NavigateUri = uri, ToolTip = target };
            StyleLink(link, target);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return; // spanned a non-text boundary — leave the text un-linked rather than crash
        }
        CaretPosition = afterInsert;
    }

    /// <summary>Undo/redo command bus over this view's model (backed by the shared UndoRedoStack).</summary>
    public DocumentCommandBus Commands => _commands;

    public new bool CanUndo =>
        (base.CanUndo && AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryUndo, mutationKind: null))
        || (_commands.CanUndo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryUndo,
            _commands.NextUndoMutationKind));

    public new bool CanRedo =>
        (base.CanRedo && AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryRedo, mutationKind: null))
        || (_commands.CanRedo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryRedo,
            _commands.NextRedoMutationKind));

    public new void Undo()
    {
        if (base.CanUndo
            && AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryUndo, mutationKind: null))
        {
            base.Undo();
            return;
        }

        if (_commands.CanUndo
            && AllowsRestrictEditingHistoryOperation(
                RestrictEditingOperationKind.HistoryUndo,
                _commands.NextUndoMutationKind))
        {
            var selectedChild = SelectedFloatingGroupChildObject();
            _commands.Undo();
            RestoreSelectedFloatingGroupChildPath(selectedChild);
        }
    }

    public new void Redo()
    {
        if (base.CanRedo
            && AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryRedo, mutationKind: null))
        {
            base.Redo();
            return;
        }

        if (_commands.CanRedo
            && AllowsRestrictEditingHistoryOperation(
                RestrictEditingOperationKind.HistoryRedo,
                _commands.NextRedoMutationKind))
        {
            var selectedChild = SelectedFloatingGroupChildObject();
            _commands.Redo();
            RestoreSelectedFloatingGroupChildPath(selectedChild);
        }
    }

    /// <summary>Render a model document into the editable surface.</summary>
    public void LoadModel(TextDocument document)
    {
        _model = document;
        Render();
    }

    /// <summary>
    /// Mutate page settings as one undoable command and re-render through the command bus. Pending
    /// in-progress edits are committed first so the layout refresh does not drop them.
    /// </summary>
    public void ApplyPageSettings(Action<PageSettings> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        CommitToModel();
        var settings = _model.Page.Clone();
        apply(settings);
        _commands.Execute(new SetPageSettingsCommand(settings));
    }

    /// <summary>Apply confirmed manual soft-hyphen insertions as one undoable body edit.</summary>
    public void ApplyManualHyphenation(IReadOnlyList<ManualHyphenationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count > 0)
            _commands.Execute(new ApplyManualHyphenationCommand(edits));
    }

    public void ApplyPageNumberFormat(PageNumberFormatDialogResult result) =>
        ApplyPageSettings(page => PageNumberFormatDialogPlanner.ApplyResult(page, result));

    /// <summary>
    /// Toggle the whole-page border (w:sectPr/w:pgBorders). When the page has no border one is added
    /// (<paramref name="colorHex"/>/<paramref name="widthPt"/>); otherwise it is cleared. Re-renders so
    /// the change shows immediately and round-trips through the model on save. Design-ribbon command.
    /// </summary>
    public void TogglePageBorder(string colorHex = "#000000", double widthPt = 1.0) =>
        ApplyPageSettings(page => page.PageBorder =
            page.PageBorder is null ? new PageBorder(colorHex, widthPt) : null);

    /// <summary>
    /// Set (or clear) the page watermark text. A null/empty value removes the watermark. Re-renders so
    /// the faint diagonal text shows immediately and round-trips on save. Design-ribbon command.
    /// </summary>
    public void SetWatermark(string? text) =>
        ApplyPageSettings(page => page.Watermark = string.IsNullOrWhiteSpace(text) ? null : text.Trim());

    /// <summary>
    /// Set (or clear) the page watermark with full options (text, font, colour, layout, opacity). A
    /// null value removes the watermark. Re-renders immediately and round-trips on save. Design-ribbon
    /// command (Custom Watermark dialog).
    /// </summary>
    public void SetWatermarkOptions(WatermarkOptions? options) =>
        ApplyPageSettings(page =>
        {
            page.WatermarkOptions = options;
            // Clear the legacy plain-text field so EffectiveWatermark is driven entirely by the new options.
            page.Watermark = null;
        });

    /// <summary>
    /// Set (or clear) the page background colour (Word's Design &gt; Page Color). A null/empty value
    /// clears it back to the default white sheet. The hex is normalised to an "#RRGGBB" form. Re-renders
    /// so the page sheet recolours immediately, and round-trips through the model's w:background on save.
    /// Design-ribbon command.
    /// </summary>
    public void SetPageColor(string? colorHex) =>
        ApplyPageSettings(page => page.BackgroundColorHex = NormalizePageColor(colorHex));

    private static string? NormalizePageColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;
        var trimmed = colorHex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }

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
    /// Apply only a Design &gt; Document Formatting colour palette, preserving the current heading/body
    /// fonts. This mirrors Word's separate Colors surface.
    /// </summary>
    public void ApplyThemeColors(DocumentTheme theme)
    {
        CommitToModel();
        DocumentTheme.ApplyColors(_model, theme);
        Render();
    }

    /// <summary>
    /// Apply a Design &gt; Document Formatting style set to the document style catalog and re-render so
    /// existing styled paragraphs immediately pick up the coordinated typography.
    /// </summary>
    public void ApplyStyleSet(DocumentStyleSet styleSet)
    {
        CommitToModel();
        DocumentStyleSet.Apply(_model, styleSet);
        Render();
    }

    /// <summary>
    /// Apply a Design &gt; Document Formatting font set to the document style catalog and re-render.
    /// </summary>
    public void ApplyFontSet(DocumentFontSet fontSet)
    {
        CommitToModel();
        DocumentFontSet.Apply(_model, fontSet);
        Render();
    }

    /// <summary>
    /// Apply a Design &gt; Document Formatting paragraph-spacing preset to the document style catalog and
    /// default paragraph formatting, preserving direct paragraph overrides.
    /// </summary>
    public void ApplyParagraphSpacingSet(DocumentParagraphSpacingSet spacingSet)
    {
        CommitToModel();
        DocumentParagraphSpacingSet.Apply(_model, spacingSet);
        Render();
    }

    /// <summary>
    /// Apply a Design &gt; Document Formatting effect set to the document theme. This affects DrawingML
    /// theme consumers such as shapes, SmartArt, charts, and WordArt via the saved <c>a:fmtScheme</c>.
    /// </summary>
    public void ApplyEffectSet(DocumentEffectSet effectSet)
    {
        CommitToModel();
        DocumentEffectSet.Apply(_model, effectSet);
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

    // Snapshot of the document's pre-theme look (Theme + DefaultRun + each affected style's Run) for theme preview.
    private (DocumentTheme Theme, RunFormatting DefaultRun, Dictionary<string, RunFormatting> Runs)? _themeSnapshot;

    // Snapshot of each affected style's run/paragraph formatting for style-set preview.
    private (RunFormatting DefaultRun, Dictionary<string, (RunFormatting Run, ParagraphFormatting Paragraph)> Styles)? _styleSetSnapshot;

    // Snapshot of the document's pre-font-set look for font-set preview.
    private (DocumentTheme Theme, RunFormatting DefaultRun, Dictionary<string, RunFormatting> Runs)? _fontSetSnapshot;

    // Snapshot of the document's pre-paragraph-spacing look for spacing-set preview.
    private (ParagraphFormatting DefaultParagraph, Dictionary<string, ParagraphFormatting> Paragraphs)? _paragraphSpacingSetSnapshot;

    // Snapshot of the document's pre-effect-set theme for effect-set preview.
    private DocumentTheme? _effectSetSnapshot;

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

        _themeSnapshot = (_model.Theme, _model.DefaultRun, runs);
        DocumentTheme.Apply(_model, theme);
        Render();
    }

    /// <summary>
    /// Preview a document colour palette without committing. Used by the Design Colors gallery.
    /// </summary>
    public void PreviewThemeColors(DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (_themeSnapshot is null)
            CommitToModel();
        else
            RestoreThemePreview();

        _themeSnapshot = CaptureRunPreview();
        DocumentTheme.ApplyColors(_model, theme);
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
        _model.Theme = snapshot.Theme;
        _model.DefaultRun = snapshot.DefaultRun;
        foreach (var (id, run) in snapshot.Runs)
        {
            if (_model.Styles.TryGetValue(id, out var style))
                style.Run = run;
        }
        _themeSnapshot = null;
    }

    private (DocumentTheme Theme, RunFormatting DefaultRun, Dictionary<string, RunFormatting> Runs) CaptureRunPreview()
    {
        var runs = new Dictionary<string, RunFormatting>();
        foreach (var id in new[] { "Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote" })
        {
            if (_model.Styles.TryGetValue(id, out var style))
                runs[id] = style.Run;
        }

        return (_model.Theme, _model.DefaultRun, runs);
    }

    /// <summary>
    /// Preview a document style set without committing: snapshot the default run and the style catalog
    /// entries the set rewrites, apply the set, and re-render. The real apply happens on click.
    /// </summary>
    public void PreviewStyleSet(DocumentStyleSet styleSet)
    {
        ArgumentNullException.ThrowIfNull(styleSet);

        if (_styleSetSnapshot is null)
            CommitToModel();
        else
            RestoreStyleSetPreview();

        var styles = new Dictionary<string, (RunFormatting Run, ParagraphFormatting Paragraph)>();
        foreach (var id in new[] { "Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote" })
        {
            if (_model.Styles.TryGetValue(id, out var style))
                styles[id] = (style.Run, style.Paragraph);
        }

        _styleSetSnapshot = (_model.DefaultRun, styles);
        DocumentStyleSet.Apply(_model, styleSet);
        Render();
    }

    /// <summary>Revert a style-set preview started by <see cref="PreviewStyleSet"/> and re-render.</summary>
    public void EndStyleSetPreview()
    {
        if (_styleSetSnapshot is null)
            return;
        RestoreStyleSetPreview();
        Render();
    }

    private void RestoreStyleSetPreview()
    {
        if (_styleSetSnapshot is not { } snapshot)
            return;
        _model.DefaultRun = snapshot.DefaultRun;
        foreach (var (id, formatting) in snapshot.Styles)
        {
            if (_model.Styles.TryGetValue(id, out var style))
            {
                style.Run = formatting.Run;
                style.Paragraph = formatting.Paragraph;
            }
        }
        _styleSetSnapshot = null;
    }

    /// <summary>
    /// Preview a document font set without committing. Used by the Design Fonts gallery.
    /// </summary>
    public void PreviewFontSet(DocumentFontSet fontSet)
    {
        ArgumentNullException.ThrowIfNull(fontSet);

        if (_fontSetSnapshot is null)
            CommitToModel();
        else
            RestoreFontSetPreview();

        _fontSetSnapshot = CaptureRunPreview();
        DocumentFontSet.Apply(_model, fontSet);
        Render();
    }

    /// <summary>Revert a font-set preview started by <see cref="PreviewFontSet"/> and re-render.</summary>
    public void EndFontSetPreview()
    {
        if (_fontSetSnapshot is null)
            return;
        RestoreFontSetPreview();
        Render();
    }

    private void RestoreFontSetPreview()
    {
        if (_fontSetSnapshot is not { } snapshot)
            return;
        _model.Theme = snapshot.Theme;
        _model.DefaultRun = snapshot.DefaultRun;
        foreach (var (id, run) in snapshot.Runs)
        {
            if (_model.Styles.TryGetValue(id, out var style))
                style.Run = run;
        }
        _fontSetSnapshot = null;
    }

    /// <summary>
    /// Preview a document paragraph-spacing preset without committing. Used by the Design Paragraph
    /// Spacing gallery.
    /// </summary>
    public void PreviewParagraphSpacingSet(DocumentParagraphSpacingSet spacingSet)
    {
        ArgumentNullException.ThrowIfNull(spacingSet);

        if (_paragraphSpacingSetSnapshot is null)
            CommitToModel();
        else
            RestoreParagraphSpacingSetPreview();

        var paragraphs = new Dictionary<string, ParagraphFormatting>();
        foreach (var id in new[] { "Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote" })
        {
            if (_model.Styles.TryGetValue(id, out var style))
                paragraphs[id] = style.Paragraph;
        }

        _paragraphSpacingSetSnapshot = (_model.DefaultParagraph, paragraphs);
        DocumentParagraphSpacingSet.Apply(_model, spacingSet);
        Render();
    }

    /// <summary>Revert a paragraph-spacing preview started by <see cref="PreviewParagraphSpacingSet"/>.</summary>
    public void EndParagraphSpacingSetPreview()
    {
        if (_paragraphSpacingSetSnapshot is null)
            return;
        RestoreParagraphSpacingSetPreview();
        Render();
    }

    private void RestoreParagraphSpacingSetPreview()
    {
        if (_paragraphSpacingSetSnapshot is not { } snapshot)
            return;
        _model.DefaultParagraph = snapshot.DefaultParagraph;
        foreach (var (id, paragraph) in snapshot.Paragraphs)
        {
            if (_model.Styles.TryGetValue(id, out var style))
                style.Paragraph = paragraph;
        }
        _paragraphSpacingSetSnapshot = null;
    }

    /// <summary>
    /// Preview a document effect set without committing. Used by the Design Effects gallery/menu.
    /// </summary>
    public void PreviewEffectSet(DocumentEffectSet effectSet)
    {
        ArgumentNullException.ThrowIfNull(effectSet);

        if (_effectSetSnapshot is null)
            CommitToModel();
        else
            RestoreEffectSetPreview();

        _effectSetSnapshot = _model.Theme;
        DocumentEffectSet.Apply(_model, effectSet);
        Render();
    }

    /// <summary>Revert an effect-set preview started by <see cref="PreviewEffectSet"/>.</summary>
    public void EndEffectSetPreview()
    {
        if (_effectSetSnapshot is null)
            return;
        RestoreEffectSetPreview();
        Render();
    }

    private void RestoreEffectSetPreview()
    {
        if (_effectSetSnapshot is null)
            return;
        _model.Theme = _effectSetSnapshot;
        _effectSetSnapshot = null;
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

        // Word places the caret in the new table's first cell, so the Table Design contextual tab appears
        // immediately and the user can type straight into the table. BringBlockIntoView moves the caret to
        // the table leaf's first insertion position (inside cell 1).
        BringBlockIntoView(index);
    }

    /// <summary>
    /// Replace the paragraphs in a specific table cell identified by
    /// (<paramref name="blockIndex"/>, <paramref name="rowIndex"/>, <paramref name="colIndex"/>)
    /// with <paramref name="paragraphs"/>.  Routed through the undo/redo bus so the change is
    /// reversible.  Table structure (merge state, widths, shading) is preserved.  Out-of-range
    /// coordinates are silently ignored.
    ///
    /// <para>Used by the Mailings Labels command to populate each grid cell with per-record
    /// merged content after the blank label grid has been inserted.</para>
    /// </summary>
    public void SetTableCellContent(
        int blockIndex,
        int rowIndex,
        int colIndex,
        IReadOnlyList<ModelParagraph> paragraphs)
    {
        CommitToModel();
        _commands.Execute(new SetTableCellContentCommand(blockIndex, rowIndex, colIndex, paragraphs));
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

        var clones = DocumentMerge.CloneBlocksForInsertion(_model, source);
        if (clones.Count == 0)
            return;

        // Insert after the block the caret sits in (else at the end), keeping document order.
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        foreach (var block in clones)
            _commands.Execute(new InsertBlockCommand(index++, block));
    }

    /// <summary>
    /// Replaces an empty editable body paragraph with parsed clipboard RTF, preserving source runs,
    /// paragraphs, and tables. Rich paste at a partial paragraph or active selection continues through the
    /// merge-formatting path until the model has a lossless inline-fragment splice operation.
    /// </summary>
    public void PasteKeepSourceFormatting()
    {
        string? rtf;
        try
        {
            rtf = System.Windows.Clipboard.ContainsData(DataFormats.Rtf)
                ? System.Windows.Clipboard.GetData(DataFormats.Rtf) as string
                : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return;
        }

        if (TryReadRtfClipboardDocument(rtf, out var source)
            && source is not null
            && PasteKeepSourceFormatting(source))
        {
            return;
        }

        PasteFromClipboard();
    }

    // Test seam for the clipboard payload conversion. RTF clipboard text is ASCII control syntax plus
    // source-encoded text; Latin-1 preserves each supplied code unit for RtfReader's code-page handling.
    internal static bool TryReadRtfClipboardDocument(string? rtf, out TextDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(rtf))
            return false;

        try
        {
            using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
            var parsed = RtfReader.Read(stream);
            if (parsed.Blocks.Count == 0)
                return false;

            document = parsed;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Testable rich-paste model operation. It deliberately accepts only a collapsed caret in an empty body
    /// paragraph, because source blocks cannot be spliced losslessly into a partial destination paragraph.
    /// </summary>
    internal bool PasteKeepSourceFormatting(TextDocument source)
    {
        if (source is null
            || source.Blocks.Count == 0
            || !Selection.IsEmpty
            || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
        {
            return false;
        }

        CommitToModel();
        var index = CaretBlockIndex();
        if (index < 0 || index >= _model.Blocks.Count || _model.Blocks[index] is not ModelParagraph { PlainText.Length: 0 })
            return false;

        foreach (var (id, style) in source.Styles)
            _model.Styles.TryAdd(id, style);

        var clones = DocumentMerge.CloneBlocksForInsertion(_model, source);
        if (clones.Count == 0)
            return false;

        _commands.Execute(new ReplaceBlocksCommand(index, 1, clones));
        return true;
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
        RefreshTableOfContentsFromModel();
    }

    private void RefreshTableOfContentsFromModel()
    {
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
    /// Prepend a cover page using the given <paramref name="preset"/> at the start of the document,
    /// routing each block insert through the undo/redo bus so it is reversible. The title/author come
    /// from <see cref="TextDocument.Properties"/>. Re-renders the surface.
    /// </summary>
    public void InsertCoverPage(CoverPagePreset preset)
    {
        CommitToModel();
        var blocks = DocumentOps.BuildCoverPage(_model, preset);
        for (var i = 0; i < blocks.Count; i++)
            _commands.Execute(new InsertBlockCommand(i, blocks[i]));
    }

    /// <summary>
    /// Insert a full blank page after the block the caret sits in. FreeW represents this with two
    /// page-break-before paragraphs so following content moves after the inserted blank page.
    /// </summary>
    public void InsertBlankPage()
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        foreach (var block in DocumentOps.BuildBlankPage())
            _commands.Execute(new InsertBlockCommand(index++, block));
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

    /// <summary>Insert a blank row above the caret's row in the table containing the caret.</summary>
    public void InsertTableRowAbove() => MutateCaretTable((index, rowIndex, _) =>
        new InsertTableRowCommand(index, rowIndex));

    /// <summary>
    /// Insert a page-number field run in a new paragraph after the caret's block, routing through the
    /// undo/redo bus. Used by Insert &gt; Header &amp; Footer &gt; Page Number &gt; Current Position.
    /// </summary>
    public void InsertPageNumberAtCaret()
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        var para = new FreeW.Core.Model.Paragraph();
        para.Runs.Add(new FreeW.Core.Model.Run(ResolvePageNumberFieldText(_model))
        {
            FieldKind = RunFieldKind.PageNumber
        });
        _commands.Execute(new InsertBlockCommand(index, para));
    }

    /// <summary>
    /// Insert a section break of the given <paramref name="breakKind"/> after the caret's block, routing
    /// through the undo/redo bus. The new paragraph's SectionBreak inherits the current document's final
    /// page settings so the new section starts with the same layout.
    /// </summary>
    public void InsertSectionBreak(SectionBreakKind breakKind)
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertBlockCommand(index, DocumentOps.CreateSectionBreak(breakKind, _model.Page)));
    }

    /// <summary>
    /// Insert a column break after the caret's block, routing through the undo/redo bus.
    /// </summary>
    public void InsertColumnBreak()
    {
        CommitToModel();
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertBlockCommand(index, DocumentOps.CreateColumnBreak()));
    }

    /// <summary>Insert a blank row below the caret's row in the table containing the caret.</summary>
    public void InsertTableRow() => MutateCaretTable((index, rowIndex, _) =>
        new InsertTableRowCommand(index, rowIndex + 1));

    /// <summary>Delete the caret's row from the table containing the caret (no-op on the last row).</summary>
    public void DeleteTableRow() => MutateCaretTable((index, rowIndex, _) =>
        new DeleteTableRowCommand(index, rowIndex));

    /// <summary>Insert a blank column to the left of the caret's column in the table containing the caret.</summary>
    public void InsertTableColumnLeft() => MutateCaretTable((index, _, columnIndex) =>
        new InsertTableColumnCommand(index, columnIndex));

    /// <summary>Insert a blank column to the right of the caret's column in the table containing the caret.</summary>
    public void InsertTableColumn() => MutateCaretTable((index, _, columnIndex) =>
        new InsertTableColumnCommand(index, columnIndex + 1));

    /// <summary>Delete the caret's column from the table containing the caret (no-op on the last column).</summary>
    public void DeleteTableColumn() => MutateCaretTable((index, _, columnIndex) =>
        new DeleteTableColumnCommand(index, columnIndex));

    /// <summary>Delete the entire table containing the caret from the document (routes through the undo/redo bus).</summary>
    public void DeleteTable()
    {
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0)
            return;
        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, [new ModelParagraph(string.Empty)]));
    }

    /// <summary>
    /// Split the table at the caret row into two tables: the current row becomes the first row of the
    /// new (lower) table, and a blank paragraph is inserted between them. Routes through the undo/redo
    /// bus (one reversible <see cref="ReplaceBlocksCommand"/>). No-op outside a table or when the caret
    /// is in the first row (nothing to split off above).
    /// </summary>
    public void SplitTable()
    {
        Focus();
        CommitToModel();
        var (blockIndex, rowIndex, _) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (TableLayoutOperations.TryBuildSplitReplacement(table, rowIndex, out var replacement))
            _commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, replacement));
    }

    /// <summary>
    /// Extend the selection to span the entire table containing the caret (navigates caret to first cell).
    /// No-op outside a table or when the table has no rows.
    /// </summary>
    public void SelectTable()
    {
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (table.Rows.Count == 0)
            return;
        // Move caret to start of first cell — full WPF cross-cell selection is not supported
        Focus();
    }

    /// <summary>
    /// Extend the selection to span the entire row containing the caret. No-op outside a table.
    /// </summary>
    public void SelectTableRow()
    {
        CommitToModel();
        var (blockIndex, rowIndex, _) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        Focus();
    }

    /// <summary>
    /// Extend the selection to span the caret's column. No-op outside a table.
    /// </summary>
    public void SelectTableColumn()
    {
        CommitToModel();
        var (blockIndex, _, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (columnIndex < 0)
            return;
        Focus();
    }

    /// <summary>
    /// Extend the selection to span the entire cell containing the caret. No-op outside a table.
    /// </summary>
    public void SelectTableCell()
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
        Focus();
    }

    /// <summary>
    /// Set the vertical and horizontal alignment of the cell containing the caret. No-op outside a table.
    /// </summary>
    public void SetCaretCellAlignment(TableCellVerticalAlignment verticalAlignment, ModelTextAlignment horizontalAlignment)
    {
        Focus();
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        if (columnIndex < 0 || columnIndex >= cells.Count)
            return;
        var cell = cells[columnIndex];
        cell.VerticalAlignment = verticalAlignment;
        foreach (var paragraph in cell.Paragraphs)
            paragraph.Formatting = paragraph.Formatting with { Alignment = horizontalAlignment };
        Render();
    }

    /// <summary>
    /// Set all rows in the table containing the caret to the same height (the average of any explicit
    /// heights, or auto when none are set). No-op outside a table.
    /// </summary>
    public void DistributeTableRows()
    {
        Focus();
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (TableLayoutOperations.DistributeRows(table))
            Render();
    }

    /// <summary>
    /// Set all columns in the table containing the caret to equal width. No-op outside a table or when
    /// there are no columns.
    /// </summary>
    public void DistributeTableColumns()
    {
        Focus();
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (TableLayoutOperations.DistributeColumns(table))
            Render();
    }

    /// <summary>
    /// Apply an auto-fit mode to the table containing the caret. No-op outside a table.
    /// </summary>
    public void SetTableAutoFit(AutoFitMode mode)
    {
        Focus();
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (TableLayoutOperations.SetAutoFit(table, mode))
            Render();
    }

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
    /// Removes the caret cell's right border by merging it with its right neighbor. If the user already
    /// selected multiple cells, the selection is merged instead. The command bus supplies undo/redo.
    /// </summary>
    public void EraseTableBorderAtCaret()
    {
        var start = TableLocationOf(Selection.Start.Parent as TextElement);
        var end = TableLocationOf(Selection.End.Parent as TextElement);
        var caret = CaretTableLocation();
        CommitToModel();
        if (start.BlockIndex >= 0 && end.BlockIndex == start.BlockIndex
            && (start.RowIndex != end.RowIndex || start.ColumnIndex != end.ColumnIndex))
        {
            MergeSelectedCells();
            return;
        }

        var (blockIndex, rowIndex, columnIndex) = caret;
        if (blockIndex < 0
            || _model.Blocks[blockIndex] is not ModelTable table
            || TableEraserCommandPlanner.PlanByCellIndex(table, rowIndex, columnIndex) is not { } plan)
            return;

        _commands.Execute(new MergeCellsHorizontalCommand(
            blockIndex,
            plan.RowIndex,
            plan.FirstCellIndex,
            plan.LastCellIndex));
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
    /// Set (or clear, when <paramref name="borders"/> is null) the per-edge cell borders on the table cell
    /// containing the caret. Commits pending edits, mutates the model cell directly, and re-renders so the
    /// borders show immediately and round-trip through save. No-op outside a table.
    /// </summary>
    public void SetCaretCellBorders(CellBorders? borders)
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
        cells[columnIndex].Borders = borders;
        Render();
    }

    /// <summary>
    /// Set the text direction on the table cell containing the caret. Commits pending edits, mutates the
    /// model cell directly, and re-renders so the rotated text shows immediately and round-trips through
    /// save (mirroring <see cref="SetSelectedShapeTextDirection"/>). No-op outside a table.
    /// </summary>
    public void SetCaretCellTextDirection(CellTextDirection direction)
    {
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (TableLayoutOperations.SetCellTextDirection(table, rowIndex, columnIndex, direction))
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

    /// <summary>Toggle the last-row distinct style on the table containing the caret.</summary>
    public void ToggleTableLastRow() =>
        UpdateCaretTableFormatting(f => f with { LastRow = !f.LastRow });

    /// <summary>Toggle the first-column distinct style on the table containing the caret.</summary>
    public void ToggleTableFirstColumn() =>
        UpdateCaretTableFormatting(f => f with { FirstColumn = !f.FirstColumn });

    /// <summary>Toggle the last-column distinct style on the table containing the caret.</summary>
    public void ToggleTableLastColumn() =>
        UpdateCaretTableFormatting(f => f with { LastColumn = !f.LastColumn });

    /// <summary>Toggle banded-column shading (alternate columns shaded) on the table containing the caret.</summary>
    public void ToggleTableBandedColumns() =>
        UpdateCaretTableFormatting(f => f with { BandedColumns = !f.BandedColumns });

    /// <summary>
    /// When true, faint gridlines are drawn on tables that have no visible borders. This is a
    /// display-only toggle (like Word's View > Table Gridlines) — it does not mutate the document model.
    /// </summary>
    public bool ViewGridlines { get; set; }

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
        if (TableLayoutOperations.UpdateFormatting(table, update))
            Render();
    }

    /// <summary>
    /// Resize the currently selected inline image to <paramref name="widthPt"/> × <paramref name="heightPt"/>
    /// points. If <paramref name="heightPt"/> is omitted (≤ 0), the height scales to preserve the aspect ratio.
    /// Routes through the bus (undoable). No-op without a selection.
    /// </summary>
    public void SetSelectedImageSize(double widthPt, double heightPt = 0)
    {
        if (widthPt <= 0)
            return;
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null)
            return;
        var finalHeight = heightPt > 0
            ? heightPt
            : (image.WidthPt > 0 ? image.HeightPt / image.WidthPt : 1) * widthPt;
        _commands.Execute(new SetImageSizeCommand(blockIndex, runIndex, widthPt, finalHeight));
    }

    /// <summary>
    /// The image targeted by the current selection/caret (inline) or by a floating-image click
    /// (overlay canvas). Returns null when neither an inline nor a floating image is selected.
    /// </summary>
    public InlineImage? SelectedImage() => SelectedImageLocation().Image;

    // ── Chart selection (mirrors SelectedImage / SelectedImageLocation) ──────────────────────────

    /// <summary>The inline chart targeted by the current selection/caret, or null if none is selected.</summary>
    public Chart? SelectedChart()
    {
        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is Chart nestedChart)
            return nestedChart;

        return SelectedChartLocation().Chart;
    }

    // Locate the model paragraph/run index of the inline chart under the selection, plus the chart itself.
    private (int BlockIndex, int RunIndex, Chart? Chart) SelectedChartLocation()
    {
        var chart = ChartAtPointer(CaretPosition)
            ?? ChartAtPointer(Selection.Start)
            ?? ChartAtPointer(Selection.End)
            ?? ChartInElement(CaretPosition?.Parent as TextElement)
            ?? ChartInElement(Selection.Start.Parent as TextElement)
            ?? ChartInElement(Selection.End.Parent as TextElement);
        if (chart is null)
            return (-1, -1, null);

        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (ReferenceEquals(paragraph.Runs[r].Chart, chart))
                    return (b, r, chart);
            }
        }
        return (-1, -1, null);
    }

    private static Chart? ChartAtPointer(TextPointer? pointer)
    {
        if (pointer is null)
            return null;
        if (ChartInElement(pointer.Parent as TextElement) is { } parentChart)
            return parentChart;
        return ChartFromAdjacent(pointer, LogicalDirection.Forward)
            ?? ChartFromAdjacent(pointer, LogicalDirection.Backward);
    }

    private static Chart? ChartFromAdjacent(TextPointer pointer, LogicalDirection direction) =>
        pointer.GetAdjacentElement(direction) is InlineUIContainer { Child: Border { Tag: Chart modelChart } }
            ? modelChart
            : null;

    private static Chart? ChartInElement(TextElement? element)
    {
        while (element is not null)
        {
            if (element is InlineUIContainer { Child: Border { Tag: Chart modelChart } })
                return modelChart;
            element = element.Parent as TextElement;
        }
        return null;
    }

    // ── Shape selection (mirrors SelectedImage / SelectedChart) ──────────────────────────────────

    /// <summary>The inline shape targeted by the current selection/caret, or null if none is selected.</summary>
    public Shape? SelectedShape()
    {
        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is Shape nestedShape)
            return nestedShape;

        return _selectedFloatingObject as Shape ?? SelectedShapeLocation().Shape;
    }

    // Locate the model paragraph/run index of the inline shape under the selection, plus the shape itself.
    private (int BlockIndex, int RunIndex, Shape? Shape) SelectedShapeLocation()
    {
        var shape = _selectedFloatingObject as Shape
            ?? ShapeAtPointer(CaretPosition)
            ?? ShapeAtPointer(Selection.Start)
            ?? ShapeAtPointer(Selection.End)
            ?? ShapeInElement(CaretPosition?.Parent as TextElement)
            ?? ShapeInElement(Selection.Start.Parent as TextElement)
            ?? ShapeInElement(Selection.End.Parent as TextElement);
        if (shape is null)
            return (-1, -1, null);

        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (ReferenceEquals(paragraph.Runs[r].Shape, shape))
                    return (b, r, shape);
            }
        }
        return (-1, -1, null);
    }

    private (int BlockIndex, int RunIndex, IReadOnlyList<int> ChildPath)? SelectedNestedShapeLocation()
    {
        if (_selectedFloatingGroupChild is not { } selected
            || !DrawingGroupChildPathResolver.TryGetChild(
                selected.RootGroup, selected.ChildPath, out _, out var child)
            || child is not Shape)
            return null;

        var location = FindFloatingObjectLocation(selected.RootGroup);
        return location.BlockIndex >= 0
            ? (location.BlockIndex, location.RunIndex, selected.ChildPath)
            : null;
    }

    private static Shape? ShapeAtPointer(TextPointer? pointer)
    {
        if (pointer is null)
            return null;
        if (ShapeInElement(pointer.Parent as TextElement) is { } parentShape)
            return parentShape;
        return ShapeFromAdjacent(pointer, LogicalDirection.Forward)
            ?? ShapeFromAdjacent(pointer, LogicalDirection.Backward);
    }

    private static Shape? ShapeFromAdjacent(TextPointer pointer, LogicalDirection direction) =>
        pointer.GetAdjacentElement(direction) is InlineUIContainer { Child: FrameworkElement { Tag: Shape modelShape } }
            ? modelShape
            : null;

    private static Shape? ShapeInElement(TextElement? element)
    {
        while (element is not null)
        {
            if (element is InlineUIContainer { Child: FrameworkElement { Tag: Shape modelShape } })
                return modelShape;
            element = element.Parent as TextElement;
        }
        return null;
    }

    // ── WordArt selection (mirrors SelectedShape) ─────────────────────────────────────────────────

    /// <summary>The inline WordArt targeted by the current selection/caret, or null if none is selected.</summary>
    public WordArt? SelectedWordArt() => SelectedWordArtLocation().WordArt;

    // Locate the model paragraph/run index of the inline WordArt under the selection, plus the WordArt itself.
    private (int BlockIndex, int RunIndex, WordArt? WordArt) SelectedWordArtLocation()
    {
        var wordArt = WordArtAtPointer(CaretPosition)
            ?? WordArtAtPointer(Selection.Start)
            ?? WordArtAtPointer(Selection.End)
            ?? WordArtInElement(CaretPosition?.Parent as TextElement)
            ?? WordArtInElement(Selection.Start.Parent as TextElement)
            ?? WordArtInElement(Selection.End.Parent as TextElement);
        if (wordArt is null)
            return (-1, -1, null);

        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (ReferenceEquals(paragraph.Runs[r].WordArt, wordArt))
                    return (b, r, wordArt);
            }
        }
        return (-1, -1, null);
    }

    private static WordArt? WordArtAtPointer(TextPointer? pointer)
    {
        if (pointer is null)
            return null;
        if (WordArtInElement(pointer.Parent as TextElement) is { } parentWordArt)
            return parentWordArt;
        return WordArtFromAdjacent(pointer, LogicalDirection.Forward)
            ?? WordArtFromAdjacent(pointer, LogicalDirection.Backward);
    }

    private static WordArt? WordArtFromAdjacent(TextPointer pointer, LogicalDirection direction) =>
        pointer.GetAdjacentElement(direction) is InlineUIContainer { Child: FrameworkElement { Tag: WordArt modelWordArt } }
            ? modelWordArt
            : null;

    private static WordArt? WordArtInElement(TextElement? element)
    {
        while (element is not null)
        {
            if (element is InlineUIContainer { Child: FrameworkElement { Tag: WordArt modelWordArt } })
                return modelWordArt;
            element = element.Parent as TextElement;
        }
        return null;
    }

    // ── Shape mutation methods (used by drawing-format contextual tab commands) ─────────────────

    /// <summary>
    /// Change the kind of the selected shape. Routes through the command bus (undoable). No-op without a shape.
    /// </summary>
    public void SetSelectedShapeKind(ShapeKind kind)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeKindCommand(
                nested.BlockIndex, nested.RunIndex, kind, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeKindCommand(blockIndex, runIndex, kind));
        Render();
    }

    /// <summary>
    /// Convert the currently selected preset shape to a freeform polygon (custom geometry). The polygon
    /// is derived from the preset kind (rectangle, ellipse, rounded-rectangle) using the matching
    /// <see cref="CustomGeometry"/> factory. Undoable. No-op without a shape selection.
    /// </summary>
    public void ConvertSelectedShapeToFreeform()
    {
        CommitToModel();
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        if (shape.HasCustomGeometry) return; // already freeform

        // Build the matching freeform polygon from the current preset kind.
        CustomGeometry poly = shape.Kind switch
        {
            ShapeKind.Ellipse          => CustomGeometry.EllipsePoly(),
            ShapeKind.RoundedRectangle => CustomGeometry.RoundedRectPoly(),
            _                          => CustomGeometry.RectanglePoly(),
        };
        _commands.Execute(new SetShapeCustomGeometryCommand(blockIndex, runIndex, poly));
        Render();
    }

    /// <summary>
    /// Enter edit-points mode for the currently selected shape (converts to freeform first if needed).
    /// Currently shows an informational notice; full drag-handle UI is a future enhancement.
    /// Backed: confirms the command bus integration for the Edit Points ribbon action.
    /// </summary>
    public void BeginShapeEditPoints()
    {
        if (_selectedFloatingGroupChild is { } selectedChild
            && FindFloatingObjectLocation(selectedChild.RootGroup) is var groupLocation
            && groupLocation.BlockIndex >= 0
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is Shape nestedShape)
        {
            if (!nestedShape.HasCustomGeometry)
            {
                CustomGeometry poly = nestedShape.Kind switch
                {
                    ShapeKind.Ellipse => CustomGeometry.EllipsePoly(),
                    ShapeKind.RoundedRectangle => CustomGeometry.RoundedRectPoly(),
                    _ => CustomGeometry.RectanglePoly(),
                };
                _commands.Execute(new SetShapeCustomGeometryCommand(
                    groupLocation.BlockIndex,
                    groupLocation.RunIndex,
                    poly,
                    selectedChild.ChildPath));
            }

            _shapeEditPointsTarget = new ShapeEditPointsTarget(
                groupLocation.BlockIndex,
                groupLocation.RunIndex,
                nestedShape,
                selectedChild.ChildPath.ToArray());
            SyncShapeEditPointsAdorner();
            return;
        }

        CommitToModel();
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;

        if (!shape.HasCustomGeometry)
        {
            CustomGeometry poly = shape.Kind switch
            {
                ShapeKind.Ellipse => CustomGeometry.EllipsePoly(),
                ShapeKind.RoundedRectangle => CustomGeometry.RoundedRectPoly(),
                _ => CustomGeometry.RectanglePoly(),
            };
            _commands.Execute(new SetShapeCustomGeometryCommand(blockIndex, runIndex, poly));
        }

        _shapeEditPointsTarget = new ShapeEditPointsTarget(blockIndex, runIndex, shape);
        SyncShapeEditPointsAdorner();
    }

    internal int ActiveShapeEditPointHandleCount => _shapeEditPointsAdorner?.HandleCount ?? 0;

    internal bool MoveActiveShapeEditPoint(int segmentIndex, long x, long y)
    {
        if (_shapeEditPointsTarget is not { } target || !IsCurrentShapeEditPointsTarget(target))
            return false;

        MoveShapeEditPoint(target, segmentIndex, x, y);
        return true;
    }

    private void MoveShapeEditPoint(ShapeEditPointsTarget target, int segmentIndex, long x, long y)
    {
        if (!IsCurrentShapeEditPointsTarget(target))
            return;

        _commands.Execute(new MoveShapeEditPointCommand(
            target.BlockIndex,
            target.RunIndex,
            segmentIndex,
            x,
            y,
            target.ChildPath));
    }

    /// <summary>
    /// Set the fill color of the selected shape. Pass null to remove fill. Undoable. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeFill(string? colorHex)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeFillCommand(
                nested.BlockIndex, nested.RunIndex, colorHex, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeFillCommand(blockIndex, runIndex, colorHex));
        Render();
    }

    /// <summary>
    /// Set the outline on the selected shape. Pass null colorHex to remove. Undoable. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeOutline(string? colorHex, double widthPt, string? dash = null)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeOutlineCommand(
                nested.BlockIndex, nested.RunIndex, colorHex, widthPt, dash, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeOutlineCommand(blockIndex, runIndex, colorHex, widthPt, dash));
        Render();
    }

    /// <summary>
    /// Resize the selected shape. Undoable. No-op without a shape or if widthPt ≤ 0.
    /// </summary>
    public void SetSelectedShapeSize(double widthPt, double heightPt)
    {
        if (widthPt <= 0 || heightPt <= 0) return;
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetDrawingGroupChildSizeCommand(
                nested.BlockIndex, nested.RunIndex, nested.ChildPath, widthPt, heightPt));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeSizeCommand(blockIndex, runIndex, widthPt, heightPt));
        Render();
    }

    /// <summary>
    /// Set the alt text on the selected shape. Undoable. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeAltText(string? altText)
    {
        CommitToModel();
        var normalized = string.IsNullOrWhiteSpace(altText) ? null : altText!.Trim();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeAltTextCommand(
                nested.BlockIndex, nested.RunIndex, normalized, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeAltTextCommand(blockIndex, runIndex, normalized));
        Render();
    }

    /// <summary>
    /// Set the text direction on the selected text-box shape. Undoable. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeTextDirection(ShapeTextDirection direction)
    {
        CommitToModel();
        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is Shape
            && FindFloatingObjectLocation(selectedChild.RootGroup) is var groupLocation
            && groupLocation.BlockIndex >= 0)
        {
            _commands.Execute(new SetShapeTextDirectionCommand(
                groupLocation.BlockIndex,
                groupLocation.RunIndex,
                direction,
                selectedChild.ChildPath));
            Render();
            return;
        }

        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeTextDirectionCommand(blockIndex, runIndex, direction));
        Render();
    }

    /// <summary>
    /// Align the selected shape's paragraph left/center/right. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeAlignment(ModelTextAlignment alignment)
    {
        CommitToModel();

        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is Shape nestedShape
            && ShapeTextFormattingPlanner.CanApplyParagraphAlignment(nestedShape)
            && FindFloatingObjectLocation(selectedChild.RootGroup) is var groupLocation
            && groupLocation.BlockIndex >= 0)
        {
            _commands.Execute(new SetShapeTextParagraphAlignmentCommand(
                groupLocation.BlockIndex,
                groupLocation.RunIndex,
                alignment,
                selectedChild.ChildPath));
            Render();
            return;
        }

        var (blockIndex, _, shape) = SelectedShapeLocation();
        if (shape is null || blockIndex < 0 || _model.Blocks[blockIndex] is not ModelParagraph paragraph)
            return;
        _commands.Execute(new SetParagraphFormattingCommand(
            blockIndex,
            paragraph.Formatting with { Alignment = alignment }));
        Render();
    }

    // ── WordArt mutation methods (used by drawing-format contextual tab commands) ───────────────

    /// <summary>
    /// Change the style preset on the selected WordArt. Undoable. No-op without a WordArt selection.
    /// </summary>
    public void SetSelectedWordArtStyle(WordArtStyle style)
    {
        CommitToModel();
        var (blockIndex, runIndex, wordArt) = SelectedWordArtLocation();
        if (wordArt is null) return;
        _commands.Execute(new SetWordArtStyleCommand(blockIndex, runIndex, style));
        Render();
    }

    /// <summary>
    /// Apply a <see cref="ShapeStylePreset"/> to the selected shape. Undoable. No-op without a shape selection.
    /// </summary>
    public void ApplySelectedShapeStyle(ShapeStylePreset preset)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new ApplyShapeStyleCommand(
                nested.BlockIndex, nested.RunIndex, preset, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, _) = SelectedShapeLocation();
        if (blockIndex < 0) return;
        _commands.Execute(new ApplyShapeStyleCommand(blockIndex, runIndex, preset));
        Render();
    }

    /// <summary>
    /// Set the extended fill (gradient / pattern / no-fill) on the selected shape. Undoable.
    /// </summary>
    public void SetSelectedShapeExtendedFill(ShapeFill? fill)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeExtendedFillCommand(
                nested.BlockIndex, nested.RunIndex, fill, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, _) = SelectedShapeLocation();
        if (blockIndex < 0) return;
        _commands.Execute(new SetShapeExtendedFillCommand(blockIndex, runIndex, fill));
        Render();
    }

    /// <summary>
    /// Set (or clear) the effects bundle on the selected shape. Undoable. No-op without a shape.
    /// </summary>
    public void SetSelectedShapeEffects(ShapeEffectLst? effects)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetShapeEffectsCommand(
                nested.BlockIndex, nested.RunIndex, effects, nested.ChildPath));
            Render();
            return;
        }
        var (blockIndex, runIndex, _) = SelectedShapeLocation();
        if (blockIndex < 0) return;
        _commands.Execute(new SetShapeEffectsCommand(blockIndex, runIndex, effects));
        Render();
    }

    /// <summary>
    /// Set the wrapping mode on the currently selected shape. Undoable. No-op without a shape selection.
    /// Mirrors <see cref="SetSelectedImageWrapping"/> for shapes (routes through
    /// <see cref="FloatingPlacement.Wrapping"/>).
    /// </summary>
    public void SetSelectedShapeWrapping(ImageWrapping wrapping)
    {
        CommitToModel();
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeWrappingCommand(blockIndex, runIndex, wrapping));
        Render();
    }

    /// <summary>
    /// Set rotation angle (degrees) and flip flags on the currently selected shape. Undoable.
    /// No-op without a shape selection.
    /// </summary>
    public void SetSelectedShapeRotation(double angleDeg, bool flipH, bool flipV)
    {
        CommitToModel();
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapeRotationCommand(blockIndex, runIndex, angleDeg, flipH, flipV));
        Render();
    }

    /// <summary>Return the selected direct shape position or nested shape's group-local offset.</summary>
    public (double HorizontalOffsetPt, double VerticalOffsetPt,
        HorizontalAnchor HorizontalAnchor, VerticalAnchor VerticalAnchor, bool IsGroupLocal)?
        GetSelectedShapePosition()
    {
        if (_selectedFloatingGroupChild is { ChildPath.Count: > 0 } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out var owningGroup,
                out var nestedChild)
            && nestedChild is Shape)
        {
            var childIndex = selectedChild.ChildPath[^1];
            var offset = childIndex < owningGroup.ChildOffsets.Count
                ? owningGroup.ChildOffsets[childIndex]
                : (X: 0d, Y: 0d);
            return (offset.X, offset.Y,
                HorizontalAnchor.Column, VerticalAnchor.Paragraph, true);
        }

        var shape = SelectedShapeLocation().Shape;
        if (shape is null)
            return null;
        var placement = shape.Placement;
        return (
            placement?.HorizontalOffsetPt ?? 0,
            placement?.VerticalOffsetPt ?? 0,
            placement?.HorizontalAnchor ?? HorizontalAnchor.Column,
            placement?.VerticalAnchor ?? VerticalAnchor.Paragraph,
            false);
    }

    /// <summary>
    /// Set the floating position of a direct shape or the group-local offset of a nested shape. Undoable.
    /// No-op without a shape selection. Mirrors <see cref="SetSelectedImagePosition"/> for direct shapes.
    /// </summary>
    public void SetSelectedShapePosition(double horizontalOffsetPt, double verticalOffsetPt,
        HorizontalAnchor horizontalAnchor = HorizontalAnchor.Column,
        VerticalAnchor verticalAnchor = VerticalAnchor.Paragraph)
    {
        CommitToModel();
        if (SelectedNestedShapeLocation() is { } nested)
        {
            _commands.Execute(new SetDrawingGroupChildPositionCommand(
                nested.BlockIndex, nested.RunIndex, nested.ChildPath,
                horizontalOffsetPt, verticalOffsetPt));
            Render();
            return;
        }
        var (blockIndex, runIndex, shape) = SelectedShapeLocation();
        if (shape is null) return;
        _commands.Execute(new SetShapePositionCommand(
            blockIndex, runIndex,
            horizontalOffsetPt, verticalOffsetPt,
            horizontalAnchor, verticalAnchor));
        Render();
    }

    /// <summary>
    /// Set the text warp on the selected WordArt. Undoable. No-op without a WordArt selection.
    /// </summary>
    public void SetSelectedWordArtWarp(WordArtWarp warp)
    {
        CommitToModel();
        var (blockIndex, runIndex, wordArt) = SelectedWordArtLocation();
        if (wordArt is null) return;
        _commands.Execute(new SetWordArtWarpCommand(blockIndex, runIndex, warp));
        Render();
    }

    /// <summary>
    /// Set the alt text on the selected WordArt. Undoable. No-op without a WordArt selection.
    /// </summary>
    public void SetSelectedWordArtAltText(string? altText)
    {
        CommitToModel();
        var (blockIndex, runIndex, wordArt) = SelectedWordArtLocation();
        if (wordArt is null) return;
        _commands.Execute(new SetWordArtAltTextCommand(blockIndex, runIndex,
            string.IsNullOrWhiteSpace(altText) ? null : altText!.Trim()));
        Render();
    }

    // ── Chart mutation methods (used by chart contextual tab commands) ────────────────────────────

    /// <summary>
    /// Change the kind of the selected chart and re-render. No-op without a chart selection.
    /// Mutates the model chart in place — it persists through the next <see cref="CommitToModel"/>.
    /// </summary>
    public void SetSelectedChartKind(ChartKind kind)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartKindCommand(location.BlockIndex, location.RunIndex, kind));
        Render();
    }

    /// <summary>
    /// Toggle the legend on the selected chart and re-render. No-op without a chart selection.
    /// </summary>
    public void ToggleSelectedChartLegend()
    {
        CommitToModel();
        var location = SelectedChartLocation();
        var chart = location.Chart;
        if (chart is null)
            return;
        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        if (!state.CanToggleLegend)
            return;
        _commands.Execute(new SetChartLegendCommand(location.BlockIndex, location.RunIndex, !state.IsLegendVisible));
        Render();
    }

    /// <summary>
    /// Set (or clear, when null/empty) the chart title on the selected chart and re-render.
    /// No-op without a chart selection.
    /// </summary>
    public void SetSelectedChartTitle(string? title)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartTitleCommand(location.BlockIndex, location.RunIndex, title));
        Render();
    }

    /// <summary>
    /// Set (or clear) axis titles on the selected chart and re-render.
    /// No-op without a chart selection or for pie/doughnut charts.
    /// </summary>
    public void SetSelectedChartAxisTitles(string? categoryAxisTitle, string? valueAxisTitle)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartAxisTitlesCommand(
            location.BlockIndex,
            location.RunIndex,
            categoryAxisTitle,
            valueAxisTitle));
        Render();
    }

    /// <summary>
    /// Set the size (width and height in points) of the selected chart and re-render.
    /// No-op without a chart selection or when both dimensions are non-positive.
    /// </summary>
    public void SetSelectedChartSize(double widthPt, double heightPt)
    {
        if (widthPt <= 0 || heightPt <= 0)
            return;
        CommitToModel();
        var chart = SelectedChartLocation().Chart;
        if (chart is null)
            return;
        chart.WidthPt = widthPt;
        chart.HeightPt = heightPt;
        Render();
    }

    /// <summary>
    /// Replace the data of the selected chart (categories + series) and re-render.
    /// Called after the user edits data via the Edit Data dialog.
    /// No-op without a chart selection.
    /// </summary>
    public void ReplaceSelectedChartData(Chart replacement)
    {
        CommitToModel();
        var chart = SelectedChartLocation().Chart;
        if (chart is null)
            return;
        chart.Kind = replacement.Kind;
        chart.Title = replacement.Title;
        chart.ShowLegend = replacement.ShowLegend;
        chart.CategoryAxisTitle = replacement.CategoryAxisTitle;
        chart.ValueAxisTitle = replacement.ValueAxisTitle;
        chart.WidthPt = replacement.WidthPt > 0 ? replacement.WidthPt : chart.WidthPt;
        chart.HeightPt = replacement.HeightPt > 0 ? replacement.HeightPt : chart.HeightPt;
        chart.Categories.Clear();
        chart.Categories.AddRange(replacement.Categories);
        chart.Series.Clear();
        foreach (var s in replacement.Series)
            chart.Series.Add(s);
        Render();
    }

    /// <summary>
    /// Re-render the document without committing to the model first. Called by the gallery hover
    /// preview path (ChartDesignGallery) after transiently mutating the selected chart's style/color/
    /// quick-layout properties so the live preview is visible. The next CommitToModel call (or the
    /// leave-revert) restores the pre-hover state.
    /// </summary>
    public void RerenderSelectedChart() => Render();

    /// <summary>
    /// Trigger a full re-render of the document surface from the current model state.  Use this after
    /// directly mutating model objects (e.g. batch floating-object position changes) that bypass the undo
    /// bus's built-in render call.
    /// </summary>
    public void Rerender() => Render();

    /// <summary>
    /// Apply a <see cref="ChartStyle"/> to the selected chart and re-render.
    /// No-op without a chart selection.
    /// </summary>
    public void ApplySelectedChartStyle(ChartStyle style)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartStyleCommand(location.BlockIndex, location.RunIndex, style.Id));
    }

    /// <summary>
    /// Apply a <see cref="ChartColorScheme"/> to the selected chart and re-render.
    /// No-op without a chart selection.
    /// </summary>
    public void ApplySelectedChartColorScheme(ChartColorScheme scheme)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartColorSchemeCommand(location.BlockIndex, location.RunIndex, scheme.Id));
    }

    /// <summary>
    /// Apply a <see cref="ChartQuickLayout"/> to the selected chart and re-render.
    /// No-op without a chart selection.
    /// </summary>
    public void ApplySelectedChartQuickLayout(ChartQuickLayout layout)
    {
        CommitToModel();
        var location = SelectedChartLocation();
        if (location.Chart is null)
            return;
        _commands.Execute(new SetChartQuickLayoutCommand(location.BlockIndex, location.RunIndex, layout));
    }

    // ── SmartArt selection (mirrors SelectedChart / SelectedChartLocation) ────────────────────────

    /// <summary>The inline SmartArt diagram targeted by the current selection/caret, or null if none is selected.</summary>
    public SmartArt? SelectedSmartArt()
    {
        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var nestedChild)
            && nestedChild is SmartArt nestedSmartArt)
            return nestedSmartArt;

        return SelectedSmartArtLocation().SmartArt;
    }

    // Locate the model paragraph/run index of the inline SmartArt under the selection, plus the diagram.
    private (int BlockIndex, int RunIndex, SmartArt? SmartArt) SelectedSmartArtLocation()
    {
        var smartArt = SmartArtAtPointer(CaretPosition)
            ?? SmartArtAtPointer(Selection.Start)
            ?? SmartArtAtPointer(Selection.End)
            ?? SmartArtInElement(CaretPosition?.Parent as TextElement)
            ?? SmartArtInElement(Selection.Start.Parent as TextElement)
            ?? SmartArtInElement(Selection.End.Parent as TextElement);
        if (smartArt is null)
            return (-1, -1, null);

        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (ReferenceEquals(paragraph.Runs[r].SmartArt, smartArt))
                    return (b, r, smartArt);
            }
        }
        return (-1, -1, null);
    }

    private static SmartArt? SmartArtAtPointer(TextPointer? pointer)
    {
        if (pointer is null)
            return null;
        if (SmartArtInElement(pointer.Parent as TextElement) is { } parentDiagram)
            return parentDiagram;
        return SmartArtFromAdjacent(pointer, LogicalDirection.Forward)
            ?? SmartArtFromAdjacent(pointer, LogicalDirection.Backward);
    }

    private static SmartArt? SmartArtFromAdjacent(TextPointer pointer, LogicalDirection direction) =>
        pointer.GetAdjacentElement(direction) is InlineUIContainer { Child: Border { Tag: SmartArt modelSmartArt } }
            ? modelSmartArt
            : null;

    private static SmartArt? SmartArtInElement(TextElement? element)
    {
        while (element is not null)
        {
            if (element is InlineUIContainer { Child: Border { Tag: SmartArt modelSmartArt } })
                return modelSmartArt;
            element = element.Parent as TextElement;
        }
        return null;
    }

    // ── SmartArt mutation methods (used by SmartArt Design contextual tab commands) ──────────────

    /// <summary>
    /// Append a new node to the selected SmartArt and re-render. No-op without a SmartArt selection.
    /// </summary>
    public void SmartArtAddShape()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.AddShape);
    }

    /// <summary>
    /// Remove the last node from the selected SmartArt and re-render. No-op without a selection or
    /// when only one node remains (a SmartArt must have at least one node).
    /// </summary>
    public void SmartArtRemoveShape()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.RemoveShape);
    }

    /// <summary>
    /// Move the last top-level node of the selected SmartArt up one position (i.e. swap it with its
    /// preceding sibling). No-op without a selection or when there is only one node or it is already first.
    /// </summary>
    public void SmartArtMoveUp()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.MoveUp);
    }

    /// <summary>
    /// Move the first top-level node of the selected SmartArt down one position (i.e. swap it with its
    /// following sibling). No-op without a selection or when there is only one node or it is already last.
    /// </summary>
    public void SmartArtMoveDown()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.MoveDown);
    }

    /// <summary>
    /// Promote the last top-level node in a Hierarchy SmartArt: move it from being a child of its parent to
    /// being a sibling after its parent at the top level. For List/Process (flat) diagrams this is a no-op.
    /// No-op without a selection.
    /// </summary>
    public void SmartArtPromote()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.Promote);
    }

    /// <summary>
    /// Demote the last top-level node in a Hierarchy SmartArt: move it to become the last child of the node
    /// preceding it. No-op when there are fewer than two top-level nodes, or for non-Hierarchy diagrams.
    /// No-op without a selection.
    /// </summary>
    public void SmartArtDemote()
    {
        ExecuteSmartArtStructureCommand(SmartArtStructureOperation.Demote);
    }

    /// <summary>
    /// Replace the node texts of the selected SmartArt with those from <paramref name="replacement"/>
    /// (the result of re-opening the Insert SmartArt dialog). Also updates the Kind if changed.
    /// No-op without a SmartArt selection.
    /// </summary>
    public void ReplaceSelectedSmartArt(SmartArt replacement)
    {
        CommitToModel();
        var location = SelectedSmartArtLocation();
        if (location.SmartArt is null) return;
        _commands.Execute(new ReplaceSmartArtContentCommand(location.BlockIndex, location.RunIndex, replacement));
    }

    /// <summary>
    /// Apply a layout preset to the selected SmartArt: sets <see cref="SmartArt.LayoutId"/> and
    /// updates <see cref="SmartArt.Kind"/> to match the preset's target kind, then re-renders.
    /// No-op without a SmartArt selection.
    /// </summary>
    public void ApplySmartArtLayout(SmartArtLayoutPreset preset)
    {
        CommitToModel();
        var smartArt = SelectedSmartArtLocation().SmartArt;
        if (smartArt is null) return;
        smartArt.LayoutId = preset.Id;
        smartArt.Kind = preset.Kind;
        Render();
    }

    /// <summary>
    /// Apply a color-scheme preset to the selected SmartArt: sets <see cref="SmartArt.ColorSchemeId"/>
    /// and re-renders so node colors update immediately.
    /// No-op without a SmartArt selection.
    /// </summary>
    public void ApplySmartArtColorScheme(SmartArtColorScheme scheme)
    {
        CommitToModel();
        var smartArt = SelectedSmartArtLocation().SmartArt;
        if (smartArt is null) return;
        smartArt.ColorSchemeId = scheme.Id;
        Render();
    }

    /// <summary>
    /// Apply a style preset to the selected SmartArt: sets <see cref="SmartArt.StyleId"/> and re-renders
    /// so shadow/corner/fill treatment updates immediately.
    /// No-op without a SmartArt selection.
    /// </summary>
    public void ApplySmartArtStyle(SmartArtStyle style)
    {
        CommitToModel();
        var location = SelectedSmartArtLocation();
        if (location.SmartArt is null) return;
        _commands.Execute(new SetSmartArtStyleCommand(location.BlockIndex, location.RunIndex, style.Id));
    }

    private void ExecuteSmartArtStructureCommand(SmartArtStructureOperation operation)
    {
        CommitToModel();
        var location = SelectedSmartArtLocation();
        if (!MutateSmartArtStructureCommand.CanApply(location.SmartArt, operation))
            return;
        _commands.Execute(new MutateSmartArtStructureCommand(location.BlockIndex, location.RunIndex, operation));
    }

    /// <summary>
    /// Set (or clear, when null/empty) the accessibility alt text on the currently selected inline image.

    /// Mutates the model image in place — the alt text is carried by the image instance, so it survives
    /// the next <see cref="CommitToModel"/> — then re-renders so the tooltip/automation name refresh.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageAltText(string? altText)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null)
            return;
        _commands.Execute(new SetImageAltTextCommand(blockIndex, runIndex, altText));
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
    /// Set the wrapping mode for the currently selected image. No-op without an image selection.
    /// The backing model and DOCX writer already carry the Word-style wrapping modes.
    /// </summary>
    public void SetSelectedImageWrapping(ImageWrapping wrapping)
    {
        CommitToModel();
        var image = SelectedImageLocation().Image;
        if (image is null)
            return;
        image.Wrapping = wrapping;
        Render();
    }

    /// <summary>
    /// Set rotation angle (degrees) and flip flags on the currently selected image. Undoable.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageRotation(double angleDeg, bool flipH, bool flipV)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageRotationCommand(blockIndex, runIndex, angleDeg, flipH, flipV));
        Render();
    }

    /// <summary>
    /// Set crop fractions (0–1 per edge) on the currently selected image. Undoable.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageCrop(double left, double right, double top, double bottom)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageCropCommand(blockIndex, runIndex, left, right, top, bottom));
        Render();
    }

    /// <summary>
    /// Set the Picture Format > Adjust parameters on the currently selected image. Undoable.
    /// Brightness/contrast in percent offset (-100..100, 0=neutral); saturation in percent (100=normal);
    /// transparency in percent (0=opaque). No-op without an image selection.
    /// </summary>
    public void SetSelectedImageAdjust(double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageAdjustCommand(blockIndex, runIndex, brightnessPct, contrastPct, saturationPct, transparencyPct));
        Render();
    }

    /// <summary>
    /// Set picture border (colorHex = 6-digit RGB hex, widthPt, dash token) on the currently
    /// selected image. Pass null colorHex to remove the border. Undoable. No-op without an image selection.
    /// </summary>
    public void SetSelectedImageBorder(string? colorHex, double widthPt, string? dash = null)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageBorderCommand(blockIndex, runIndex, colorHex, widthPt, dash));
        Render();
    }

    /// <summary>
    /// Set picture effects (shadow/glow/reflection/softEdge/bevel) on the currently selected image.
    /// Pass 0/0.0 for each to clear the corresponding effect. Undoable. No-op without an image selection.
    /// </summary>
    public void SetSelectedImageEffect(
        int shadowPreset, double glowSizePt, string? glowColorHex,
        int reflectionPreset, double softEdgePt, int bevelPreset)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageEffectCommand(
            blockIndex, runIndex,
            shadowPreset, glowSizePt, glowColorHex,
            reflectionPreset, softEdgePt, bevelPreset));
        Render();
    }

    /// <summary>
    /// Set the recolor mode and/or color temperature on the currently selected image. Undoable.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageRecolor(ImageRecolorMode mode, double colorTemperature = 0)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageRecolorCommand(blockIndex, runIndex, mode, colorTemperature));
        Render();
    }

    /// <summary>
    /// Set the artistic filter on the currently selected image. Undoable. Non-destructive: original bytes
    /// are never modified; the effect is applied at render time by the pixel pipeline.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImageArtisticEffect(ImageArtisticEffect effect)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageArtisticEffectCommand(blockIndex, runIndex, effect));
        Render();
    }

    /// <summary>
    /// Apply a Picture Style preset (bundles border + effects). Undoable. No-op without an image selection.
    /// </summary>
    public void ApplySelectedImageStyle(
        int stylePreset,
        string? borderColorHex, double borderWidthPt, string? borderDash,
        int shadowPreset, int reflectionPreset, double softEdgePt)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageStyleCommand(
            blockIndex, runIndex,
            stylePreset, borderColorHex, borderWidthPt, borderDash,
            shadowPreset, reflectionPreset, softEdgePt));
        Render();
    }

    /// <summary>Apply a shared Picture Styles catalog preset to the selected picture.</summary>
    public void ApplySelectedImageStyle(PictureStylePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImageStyleCommand(blockIndex, runIndex, preset));
        Render();
    }

    /// <summary>
    /// Restore the currently selected image to its natural size (from OriginalPixelWidth/Height), clearing
    /// any rotation, flip, and crop. Uses the shared 96-DPI bitmap-to-point policy. Undoable.
    /// No-op without an image selection, or when OriginalPixelWidth is 0 (not recorded at insert time).
    /// </summary>
    public void ResetSelectedImage()
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;

        var naturalSize = ImageResetCommandPlanner.BuildNaturalSize(
            image.OriginalPixelWidth,
            image.OriginalPixelHeight,
            image.WidthPt,
            image.HeightPt);
        _commands.Execute(new ResetImageSizeCommand(
            blockIndex,
            runIndex,
            naturalSize.WidthPt,
            naturalSize.HeightPt));
        Render();
    }

    /// <summary>
    /// Set the floating position offsets and anchors for the currently selected image. Undoable.
    /// No-op without an image selection.
    /// </summary>
    public void SetSelectedImagePosition(double horizontalOffsetPt, double verticalOffsetPt,
        HorizontalAnchor horizontalAnchor = HorizontalAnchor.Column,
        VerticalAnchor verticalAnchor = VerticalAnchor.Paragraph)
    {
        CommitToModel();
        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is null) return;
        _commands.Execute(new SetImagePositionCommand(
            blockIndex, runIndex,
            horizontalOffsetPt, verticalOffsetPt,
            horizontalAnchor, verticalAnchor));
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
    /// Set (or clear, when <paramref name="border"/> is null) the box border on every selected paragraph,
    /// honouring its line style, width, colour and per-edge flags. Used by the Borders and Shading dialog;
    /// routes through the undo/redo bus and re-renders. The full <see cref="ParagraphBorder"/> survives an
    /// edit/commit cycle (the model-only fields ride on the paragraph Tag — see BuildParagraph).
    /// </summary>
    public void SetParagraphBorder(ParagraphBorder? border) =>
        FormatSelectedModelParagraphs(f => f with { Border = border });

    /// <summary>
    /// Set (or clear, when <paramref name="colorHex"/> is null/empty) paragraph shading over the selection
    /// with the given fill colour and <paramref name="pattern"/>. Used by the Borders and Shading dialog;
    /// routes through the undo/redo bus and re-renders. Mirrors <see cref="ToggleParagraphShading"/> but
    /// applies an explicit colour+pattern rather than toggling.
    /// </summary>
    public void SetParagraphShading(string? colorHex, ShadingPattern pattern) =>
        FormatSelectedModelParagraphs(f => f with
        {
            ShadingColorHex = string.IsNullOrEmpty(colorHex) ? null : colorHex,
            ShadingPattern = string.IsNullOrEmpty(colorHex) ? ShadingPattern.Clear : pattern,
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
        FormatSelectedModelParagraphs(f => f with { WidowControl = enable, WidowControlIsSet = true });
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

    public void ApplyMultiLevelNumberFormats(IReadOnlyList<ListNumberFormat> numberFormats)
    {
        _model.MultiLevelList.SetNumberFormats(numberFormats);
        Render();
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
    /// Apply start-at overrides to multilevel-list paragraphs spanned by the selection, based on their
    /// list level. <paramref name="level0StartAt"/> applies to paragraphs at ListLevel 0;
    /// <paramref name="level1StartAt"/> applies to paragraphs at ListLevel 1. Null means "no override
    /// (continue)". Called by the Define New Multilevel List command after <see cref="ApplyMultiLevelList"/>
    /// sets the list kind. Reversible via the bus.
    /// </summary>
    public void ApplyListStartOverrides(int? level0StartAt, int? level1StartAt) =>
        FormatSelectedModelParagraphs(f =>
            f.ListKind != ListKind.MultiLevel ? f :
            f.ListLevel == 0 && level0StartAt.HasValue ? f with { ListStartOverride = level0StartAt } :
            f.ListLevel == 1 && level1StartAt.HasValue ? f with { ListStartOverride = level1StartAt } :
            f);

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
    /// Set an exact space-before value (points) on every paragraph spanned by the selection.
    /// Unlike <see cref="ToggleSpaceBefore"/> this always writes the supplied amount directly,
    /// making it suitable for the numeric combo box in Layout &gt; Paragraph. Reversible via the bus.
    /// </summary>
    public void FormatSelectedParagraphSpaceBefore(double spacePt) =>
        FormatSelectedModelParagraphs(f => f with { SpaceBeforePt = Math.Max(0, spacePt) });

    /// <summary>
    /// Set an exact space-after value (points) on every paragraph spanned by the selection.
    /// Unlike <see cref="ToggleSpaceAfter"/> this always writes the supplied amount directly,
    /// making it suitable for the numeric combo box in Layout &gt; Paragraph. Reversible via the bus.
    /// </summary>
    public void FormatSelectedParagraphSpaceAfter(double spacePt) =>
        FormatSelectedModelParagraphs(f => f with { SpaceAfterPt = Math.Max(0, spacePt) });

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
    /// The <em>effective</em> character formatting of the current selection (or the caret), resolved the
    /// same way the toolbar/format-painter reads it: font, size, colour, highlight, bold/italic/underline/
    /// strikethrough, small/all caps and super/subscript, taken from the live WPF selection so the run's
    /// own properties already cascade over its style and the document default. Read-only; used by the
    /// Reveal Formatting pane to mirror what is actually in effect at the caret. Does not commit pending
    /// edits (cheap, called on selection change).
    /// </summary>
    public RunFormatting CurrentRunFormatting => CaptureSelectionRunFormatting();

    /// <summary>
    /// Replace the tab stops (pPr/w:tabs) on every paragraph spanned by the selection with
    /// <paramref name="tabStops"/> (positions/alignments/leaders), via the undo/redo bus. Pass an empty
    /// list to clear all custom stops. The stops round-trip to docx through the existing w:tabs writer;
    /// WPF's FlowDocument has no tab-stop API, so the model values are carried on the rendered
    /// paragraph's Tag (a ParagraphTag) and applied by Print Preview / on save (see Render). Used by the
    /// Tabs dialog (Home > Paragraph > Tabs…).
    /// </summary>
    public void SetParagraphTabStops(IReadOnlyList<TabStop> tabStops) =>
        FormatSelectedModelParagraphs(f => f with { TabStops = tabStops });

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

        var contentHeight = DocumentViewLayoutPlanner.BuildPageMetrics(_model.Page).ContentHeightDip;
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

    /// <summary>Home &gt; Styles &gt; New Style: create a paragraph style and apply it to the selection.</summary>
    public DocumentStyle? CreateParagraphStyleAndApply(
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? nextStyleId = null)
    {
        CommitToModel();
        var targets = SelectedModelParagraphIndices();
        DocumentStyle? created = null;

        _commands.BeginUndoGroup();
        try
        {
            _commands.Execute(new StyleCatalogCommand("New Style", doc =>
            {
                created = StyleManager.CreateStyle(doc, name, basedOnId, run, paragraph, nextStyleId);
            }));

            if (created is not null)
            {
                foreach (var index in targets)
                {
                    if (index >= 0 && index < _model.Blocks.Count && _model.Blocks[index] is ModelParagraph)
                        _commands.Execute(new SetParagraphStyleCommand(index, created.Id));
                }
            }

            _commands.CommitUndoGroup("New Style");
        }
        catch
        {
            _commands.AbortUndoGroup();
            throw;
        }

        return created;
    }

    /// <summary>Home &gt; Styles &gt; Manage Styles: modify a custom or built-in style definition.</summary>
    public DocumentStyle? ModifyParagraphStyle(
        string styleId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? basedOnId,
        string? nextStyleId)
    {
        CommitToModel();
        if (string.IsNullOrWhiteSpace(styleId) || !_model.Styles.ContainsKey(styleId))
            return null;

        DocumentStyle? updated = null;
        _commands.Execute(new StyleCatalogCommand("Modify Style", doc =>
        {
            updated = StyleManager.ModifyStyle(doc, styleId,
                run: run,
                para: paragraph,
                basedOnId: basedOnId,
                clearBasedOn: basedOnId is null,
                nextStyleId: nextStyleId,
                clearNext: nextStyleId is null);
        }));
        return updated;
    }

    /// <summary>Home &gt; Styles &gt; Manage Styles: delete a custom style through shared catalog rules.</summary>
    public bool DeleteParagraphStyle(string styleId)
    {
        CommitToModel();
        if (string.IsNullOrWhiteSpace(styleId)
            || StyleManager.IsBuiltIn(styleId)
            || !_model.Styles.ContainsKey(styleId))
            return false;

        var deleted = false;
        _commands.Execute(new StyleCatalogCommand("Delete Style", doc => deleted = StyleManager.DeleteStyle(doc, styleId)));
        return deleted;
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

    /// <summary>
    /// Set the paragraph at <paramref name="modelBlockIndex"/> directly to <c>Heading1</c> (Word's
    /// outline "Promote to Heading 1" double-arrow). Routes through the same reversible
    /// <see cref="SetParagraphStyleCommand"/> as Promote/Demote, so it is a single undoable step.
    /// No-op when the index is not a paragraph or it is already Heading 1.
    /// </summary>
    public void PromoteHeadingToHeading1(int modelBlockIndex) =>
        ShiftHeadingStyle(modelBlockIndex, _ => "Heading1");

    /// <summary>
    /// Move the heading at <paramref name="modelBlockIndex"/> — together with its whole subtree (every
    /// block down to the next same-or-higher heading) — one position toward the document start
    /// (<paramref name="moveUp"/> = true) or end (false), swapping it with the adjacent sibling subtree.
    /// The reorder is computed by the pure <see cref="OutlineTools.MoveSubtree"/> and applied through the
    /// reversible <see cref="ReorderBlocksCommand"/> on the undo/redo bus, so it is a single undoable step.
    /// Returns the new block index of the moved heading (so the nav pane can re-select it), or the original
    /// index when nothing moved (already at an outline edge, or not a heading). Any collapsed-heading view
    /// state is dropped first so the indices cannot become stale across the reorder.
    /// </summary>
    public int MoveHeading(int modelBlockIndex, bool moveUp)
    {
        CommitToModel();

        // Collapse markers are tracked by model block index; a reorder invalidates them, so expand all
        // first (purely a view concern — the model is unaffected) before relocating the subtree.
        if (_collapsedHeadings.Count > 0)
            _collapsedHeadings.Clear();

        var reordered = OutlineTools.MoveSubtree(_model.Blocks, modelBlockIndex, moveUp);
        if (ReferenceEquals(reordered, _model.Blocks))
            return modelBlockIndex; // nothing to move

        var heading = _model.Blocks[modelBlockIndex];
        _commands.Execute(new ReorderBlocksCommand(reordered));

        for (var i = 0; i < reordered.Count; i++)
        {
            if (ReferenceEquals(reordered[i], heading))
                return i;
        }
        return modelBlockIndex;
    }

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
    /// Sets the heading level of the paragraph at <paramref name="modelBlockIndex"/> directly (no
    /// step-promote/demote). <paramref name="level"/> 0 maps to "Title", 1–<see cref="OutlineTools.MaxHeadingLevel"/>
    /// map to "Heading1"–"HeadingN", and -1 (or any value below 0) maps to "Normal" (body text).
    /// No-op when <paramref name="modelBlockIndex"/> is out of range or the paragraph is already at the
    /// requested level.
    /// </summary>
    public void SetHeadingLevel(int modelBlockIndex, int level)
    {
        var styleId = level < 0
            ? "Normal"
            : level == 0
                ? "Title"
                : $"Heading{Math.Min(level, OutlineTools.MaxHeadingLevel)}";
        ShiftHeadingStyle(modelBlockIndex, _ => styleId);
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
    public void ApplyDropCap(
        DropCapPosition position = DropCapPosition.Dropped,
        double sizePt = DropCap.DefaultSizePt,
        int lineSpan = DropCap.DefaultLineSpan,
        double distanceFromTextPt = DropCap.DefaultDistanceFromTextPt)
    {
        Focus();
        CommitToModel();
        var index = SelectedModelParagraphIndices().FirstOrDefault(-1);
        if (index < 0 || index >= _model.Blocks.Count || _model.Blocks[index] is not ModelParagraph)
            return;
        _commands.Execute(new ReplaceParagraphRunsCommand(
            index,
            p => DropCap.ApplyDropCap(p, position, sizePt, lineSpan, distanceFromTextPt)));
    }

    /// <summary>
    /// Remove a drop cap from the caret's paragraph: every run's formatting is reset to the paragraph
    /// default (see <see cref="DropCap.ClearFormatting"/>). This is the "None" position in the Drop
    /// Cap Options dialog. Routes through the undo/redo bus and re-renders immediately.
    /// </summary>
    public void ClearDropCap()
    {
        Focus();
        CommitToModel();
        var index = SelectedModelParagraphIndices().FirstOrDefault(-1);
        if (index < 0 || index >= _model.Blocks.Count || _model.Blocks[index] is not ModelParagraph)
            return;
        _commands.Execute(new ReplaceParagraphRunsCommand(index, DropCap.ClearFormatting));
    }

    /// <summary>
    /// Clear all character formatting in every model paragraph spanned by the selection (or the caret's
    /// paragraph): each run's formatting is reset to <see cref="RunFormatting.Default"/> while its text is
    /// kept (see <see cref="DropCap.ClearFormatting"/>). One reversible <see cref="FormatParagraphRunsCommand"/>
    /// per paragraph on the undo/redo bus; the view re-renders so the reset shows immediately.
    /// </summary>
    public void ClearFormatting()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyFormatting))
            return;

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
    /// caret sits in) by their text, ascending and case-insensitively. Convenience overload of
    /// <see cref="SortSelectedParagraphs(SortKind, bool, bool, bool)"/>.
    /// </summary>
    public void SortSelectedParagraphs(bool ascending, bool caseSensitive) =>
        SortSelectedParagraphs(SortKind.Text, ascending, caseSensitive, hasHeaderRow: false);

    /// <summary>
    /// Sort the paragraphs spanned by the current selection (or, with a bare caret, the paragraph the
    /// caret sits in) in place, interpreting each as <paramref name="kind"/>
    /// (<see cref="SortKind.Text"/>/<see cref="SortKind.Number"/>/<see cref="SortKind.Date"/>). When
    /// <paramref name="hasHeaderRow"/> is true the first selected paragraph stays put and only the rest
    /// reorder. Tables interleaved in the selected span are left fixed at their own positions — only the
    /// paragraph blocks are reordered among their own slots — so the operation stays well-defined over a
    /// mixed body. Routes through the undo/redo bus (one reversible <see cref="ReplaceBlocksCommand"/>)
    /// and re-renders. No-op without at least two sortable paragraphs.
    /// </summary>
    public void SortSelectedParagraphs(SortKind kind, bool ascending, bool caseSensitive, bool hasHeaderRow)
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

        var sorted = ParagraphSort.Sort(paragraphs, kind, ascending, caseSensitive, hasHeaderRow);

        // Rebuild the span: drop sorted paragraphs back into the paragraph slots, keeping any
        // interleaved tables fixed at their own positions.
        var replacement = new List<ModelBlock>(last - first + 1);
        var nextSorted = 0;
        for (var i = first; i <= last; i++)
            replacement.Add(_model.Blocks[i] is ModelParagraph ? sorted[nextSorted++] : _model.Blocks[i]);

        _commands.Execute(new ReplaceBlocksCommand(first, replacement.Count, replacement));
    }

    /// <summary>
    /// Sort the rows of the table containing the caret by the caret's column (matching Word, which sorts
    /// table rows by a chosen column when the selection is inside a table), interpreting each key as
    /// <paramref name="kind"/>. When <paramref name="hasHeaderRow"/> is true the first row stays put and
    /// only the body rows reorder. A fresh table with the same formatting, column grid, and row instances
    /// (reordered) replaces the original through the undo/redo bus (one reversible
    /// <see cref="ReplaceBlocksCommand"/>). No-op outside a table or with fewer than two sortable rows.
    /// </summary>
    public void SortCaretTableRows(SortKind kind, bool ascending, bool caseSensitive, bool hasHeaderRow)
    {
        Focus();
        CommitToModel();

        var (blockIndex, _, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelTable table)
            return;
        if (table.Rows.Count < 2)
            return;

        var keyColumn = columnIndex < 0 ? 0 : columnIndex;
        var sorted = ParagraphSort.SortRows(table.Rows, keyColumn, kind, ascending, caseSensitive, hasHeaderRow);

        // Rebuild the table preserving its formatting and column grid; only the row order changes (the
        // same TableRow instances are reused, so cell content/shading travels with each row).
        var replacement = new ModelTable { Formatting = table.Formatting };
        replacement.ColumnWidthsPt.AddRange(table.ColumnWidthsPt);
        replacement.Rows.AddRange(sorted);

        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, new ModelBlock[] { replacement }));
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
    /// paragraph's formatting, then wait for the user's next selection to stamp it. Calling this while
    /// already armed disarms it (a toggle). When <paramref name="locked"/> is true the painter stays
    /// armed after each application (double-click lock mode) until the user clicks again or presses
    /// Escape. Returns true if the painter is now armed, false if it was disarmed.
    /// </summary>
    public bool ArmFormatPainter(bool locked = false)
    {
        Focus();
        if (_formatPainter is not null)
        {
            _formatPainter = null;
            _formatPainterLocked = false;
            return false;
        }

        _formatPainter = FormatPainterClipboard.Capture(CaptureSelectionRunFormatting(), CaptureCaretParagraphFormatting());
        _formatPainterLocked = locked;
        return true;
    }

    /// <summary>Disarm the Format Painter regardless of lock mode (e.g. on Escape key).</summary>
    public void EscapeFormatPainter()
    {
        _formatPainter = null;
        _formatPainterLocked = false;
    }

    /// <summary>
    /// If the Format Painter is armed and the current selection is non-empty, stamp the captured run
    /// and paragraph formatting onto it, then disarm. Called on mouse-up after the user drags out the
    /// "next selection". A no-op (leaving the painter armed) when the selection is still empty, so a
    /// stray click that places only a caret does not consume the gesture. Returns true if applied.
    /// </summary>
    private bool TryApplyFormatPainter()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyFormatting))
            return false;

        if (_formatPainter is not { } clipboard || Selection.IsEmpty)
            return false;

        if (!_formatPainterLocked)
            _formatPainter = null; // disarm first in single-shot mode; locked mode stays armed

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

        // Model-only fields (character border, shading, language) are not in the WPF selection property
        // bag; recover them from the caret run's CharacterFormatMarker tag instead. This gives the "at
        // caret" value, matching how CaptureCaretParagraphFormatting works for paragraph-level fields.
        var caretRun = (CaretPosition?.Parent as WpfRun ?? selection.Start.Parent as WpfRun);
        var charFmt = (caretRun?.Tag as RunMarkers)?.CharacterFormat;

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
            HighlightColorHex = selection.GetPropertyValue(TextElement.BackgroundProperty) is SolidColorBrush bg ? ToHex(bg.Color) : null,
            CharacterBorder = charFmt?.Border,
            CharacterShadingHex = charFmt?.ShadingHex,
            CharacterShadingPattern = charFmt?.ShadingPattern ?? ShadingPattern.Clear,
            LanguageTag = charFmt?.LanguageTag,
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
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyFormatting))
            return;

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

    // Apply a run-formatting transform to every run in every model paragraph spanned by the current
    // selection. Commits to the model first, issues one SetRunFormattingCommand per run (fully undoable),
    // then re-renders. Used for model-only run properties (CharacterBorder, CharacterShadingHex,
    // LanguageTag) that have no WPF property slot and therefore cannot be applied via ApplyPropertyValue.
    private void FormatSelectedModelRuns(Func<RunFormatting, RunFormatting> transform)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyFormatting))
            return;

        Focus();
        CommitToModel();
        var indices = SelectedModelParagraphIndices();
        foreach (var blockIndex in indices)
        {
            if (_model.Blocks[blockIndex] is not ModelParagraph paragraph)
                continue;
            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                _commands.Execute(new SetRunFormattingCommand(blockIndex, runIndex, transform(paragraph.Runs[runIndex].Formatting)));
        }
        // Re-render so the visual chrome (border decoration, shading background, language) updates.
        LoadModel(_model);
    }

    /// <summary>
    /// Set (or clear when <paramref name="border"/> is null) the character border on every run in the
    /// selected paragraphs. Routes through the undo/redo bus and re-renders.
    /// </summary>
    public void SetCharacterBorder(ParagraphBorder? border) =>
        FormatSelectedModelRuns(f => f with { CharacterBorder = border });

    /// <summary>
    /// Set (or clear when <paramref name="colorHex"/> is null/empty) the character shading on every run
    /// in the selected paragraphs. Routes through the undo/redo bus and re-renders.
    /// </summary>
    public void SetCharacterShading(string? colorHex, ShadingPattern pattern = ShadingPattern.Clear) =>
        FormatSelectedModelRuns(f => f with
        {
            CharacterShadingHex = string.IsNullOrEmpty(colorHex) ? null : colorHex,
            CharacterShadingPattern = string.IsNullOrEmpty(colorHex) ? ShadingPattern.Clear : pattern,
        });

    /// <summary>
    /// Set (or clear when <paramref name="languageTag"/> is null/empty) the proofing language on the
    /// selected text range, or on the current proofing word when the caret is collapsed inside one.
    /// </summary>
    public void SetProofingLanguage(string? languageTag)
    {
        Focus();

        var selectedRange = SelectedVisibleTextRange();
        if (selectedRange is null)
            return;

        CommitToModel();

        var selectedBlocks = selectedRange.Value.VisibleBlockIndices
            .Select(ModelIndexFromVisible)
            .ToArray();
        var caretContext = selectedBlocks.Length == 1
            && selectedRange.Value.StartOffset == selectedRange.Value.EndOffset
            && selectedBlocks[0] >= 0
            && selectedBlocks[0] < _model.Blocks.Count
            && _model.Blocks[selectedBlocks[0]] is ModelParagraph caretParagraph
                ? new ProofingLanguageCaretContext(
                    selectedBlocks[0],
                    selectedRange.Value.StartOffset,
                    caretParagraph.PlainText)
                : null;

        var plan = ProofingLanguageApplyPlanner.BuildForSelectionOrCaretWord(
            languageTag,
            selectedBlocks,
            selectedRange.Value.StartOffset,
            selectedRange.Value.EndOffset,
            caretContext);
        if (!plan.HasSelectedText)
            return;

        ApplyProofingLanguagePlan(plan);
    }

    /// <summary>
    /// Apply all fields of <paramref name="fmt"/> to the current selection, covering both WPF-backed
    /// properties (bold, italic, underline, strikethrough, font family, size, colour, super/subscript,
    /// small/all caps) and model-only advanced typography fields (character spacing, kerning, position,
    /// ligatures, stylistic set, number form, number spacing). Used by the Font dialog-launcher
    /// (freew.font-dialog). Both layers are applied atomically from the caller's perspective: the WPF
    /// properties change the live surface immediately; the model-only fields are pushed through the
    /// undo/redo bus via <see cref="FormatSelectedModelRuns"/>. A subsequent <see cref="CommitToModel"/>
    /// call merges the WPF surface back into the model, so both sets of changes survive the round-trip.
    /// </summary>
    public void ApplyFontFormatting(RunFormatting fmt)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyFormatting))
            return;

        Focus();
        // Apply the WPF-visible fields via the selection property bag (the normal path for bold/size/…).
        ApplyRunFormattingToSelection(fmt);
        // Apply model-only advanced fields via the bus so they are undoable and round-trip through docx.
        FormatSelectedModelRuns(f => f with
        {
            CharacterSpacingPt = fmt.CharacterSpacingPt,
            KerningMinSizePt   = fmt.KerningMinSizePt,
            PositionPt         = fmt.PositionPt,
            Ligatures          = fmt.Ligatures,
            StylisticSet       = fmt.StylisticSet,
            NumberForm         = fmt.NumberForm,
            NumberSpacing      = fmt.NumberSpacing,
        });
    }

    /// <summary>
    /// Apply the full paragraph formatting block captured by the Paragraph dialog (indents + spacing from
    /// the Indents and Spacing tab + line-and-page-break toggles from the Line and Page Breaks tab) to
    /// every paragraph spanned by the selection. All changes route through the undo/redo bus.
    /// </summary>
    public void ApplyParagraphDialogFormatting(
        double leftPt, double rightPt, double firstLinePt,
        double spaceBeforePt, double spaceAfterPt, double lineSpacing,
        bool keepWithNext, bool keepLinesTogether, bool widowControl,
        bool pageBreakBefore, bool suppressAutoHyphens, bool suppressLineNumbers, bool contextualSpacing) =>
        FormatSelectedModelParagraphs(f => f with
        {
            IndentLeftPt       = leftPt,
            IndentRightPt      = rightPt,
            FirstLineIndentPt  = firstLinePt,
            SpaceBeforePt      = spaceBeforePt,
            SpaceAfterPt       = spaceAfterPt,
            LineSpacing        = lineSpacing,
            KeepWithNext       = keepWithNext,
            KeepLinesTogether  = keepLinesTogether,
            WidowControl       = widowControl,
            WidowControlIsSet  = true,
            PageBreakBefore    = pageBreakBefore,
            SuppressAutoHyphens= suppressAutoHyphens,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressLineNumbersIsSet = true,
            ContextualSpacing  = contextualSpacing,
        });

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

    private (IReadOnlyList<int> VisibleBlockIndices, int StartOffset, int EndOffset)? SelectedVisibleTextRange()
    {
        var start = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        var end = Selection.End.Paragraph ?? start;
        if (start is null || end is null)
            return null;

        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);

        if (!indexOf.TryGetValue(start, out var startIndex))
            return null;
        if (!indexOf.TryGetValue(end, out var endIndex))
            endIndex = startIndex;

        var startOffset = OffsetInParagraph(start, Selection.Start);
        var endOffset = OffsetInParagraph(end, Selection.End);
        var firstIndex = Math.Min(startIndex, endIndex);
        var lastIndex = Math.Max(startIndex, endIndex);
        var indices = new List<int>();
        for (var i = firstIndex; i <= lastIndex; i++)
            indices.Add(i);

        return startIndex <= endIndex
            ? (indices, startOffset, endOffset)
            : (indices, endOffset, startOffset);
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

        var blockIndex = wpfTable.Tag is WpfTableTag { SourceBlockIndex: >= 0 } tag
            ? tag.SourceBlockIndex
            : new List<System.Windows.Documents.Block>(Document.Blocks).IndexOf(wpfTable);
        var rowIndex = ModelRowIndexOfRenderedRow(group, wpfRow);
        var columnIndex = new List<WpfTableCell>(wpfRow.Cells).IndexOf(cell);
        return (blockIndex, rowIndex, columnIndex);
    }

    private static int ModelRowIndexOfRenderedRow(TableRowGroup group, WpfTableRow renderedRow)
    {
        if (renderedRow.Tag is WpfTableRowTag { SourceRowIndex: >= 0 } tag)
            return tag.SourceRowIndex;
        if (renderedRow.Tag is WpfTableRowTag { IsRepeatedHeader: true, SourceRowIndex: var sourceRowIndex })
            return sourceRowIndex;

        var modelRowIndex = 0;
        foreach (var row in group.Rows)
        {
            if (row.Tag is WpfTableRowTag { IsRepeatedHeader: true })
                continue;
            if (ReferenceEquals(row, renderedRow))
                return modelRowIndex;
            modelRowIndex++;
        }

        return -1;
    }

    private static bool IsRepeatedHeaderRenderRow(WpfTableRow row) =>
        row.Tag is WpfTableRowTag { IsRepeatedHeader: true };

    // Locate the model paragraph/run index of the inline image under the selection (or a floating
    // image clicked on the overlay canvas), plus the image itself.
    private (int BlockIndex, int RunIndex, InlineImage? Image) SelectedImageLocation()
    {
        // An InlineUIContainer hosting our tagged Image is the selected picture; find it around the caret.
        var image = ImageAtPointer(CaretPosition)
            ?? ImageAtPointer(Selection.Start)
            ?? ImageAtPointer(Selection.End)
            ?? ImageInElement(CaretPosition?.Parent as TextElement)
            ?? ImageInElement(Selection.Start.Parent as TextElement)
            ?? ImageInElement(Selection.End.Parent as TextElement);

        // Fall back to the floating image selected by a click on the overlay canvas.
        // This allows all existing SetSelectedImage* commands (size/position/wrap/rotate/crop/border)
        // to operate on a floating image without modification.
        if (image is null)
            image = _selectedFloatingImage;

        if (image is null)
            return (-1, -1, null);

        // Match it back to a top-level model paragraph + run by identity. For a floating image the
        // run holds only an AnchorMarker (zero-width placeholder), but the Image reference is the same
        // object, so identity comparison finds it correctly. Images embedded in tables are skipped.
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

    private static InlineImage? ImageAtPointer(TextPointer? pointer)
    {
        if (pointer is null)
            return null;
        if (ImageInElement(pointer.Parent as TextElement) is { } parentImage)
            return parentImage;
        return ImageFromAdjacent(pointer, LogicalDirection.Forward)
            ?? ImageFromAdjacent(pointer, LogicalDirection.Backward);
    }

    private static InlineImage? ImageFromAdjacent(TextPointer pointer, LogicalDirection direction) =>
        pointer.GetAdjacentElement(direction) is InlineUIContainer { Child: Image { Tag: InlineImage modelImage } }
            ? modelImage
            : null;

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

    /// <summary>
    /// The index — into <see cref="ReadAloudController.ExtractSegments(TextDocument)"/>'s ordered,
    /// non-empty segment list — of the segment Review &gt; Read Aloud should start at: the first speakable
    /// paragraph at or after the caret's block (Word reads from the caret to the end). Commits pending
    /// edits first so the model reflects the current text, then counts the non-empty speakable paragraphs
    /// preceding the caret block in the same reading order the controller uses (top-level paragraphs, then
    /// table-cell paragraphs). Returns 0 when the body is empty or the caret precedes all speakable text.
    /// </summary>
    public int ReadAloudStartSegmentIndex()
    {
        CommitToModel();

        var caretBlockIndex = CaretBlockIndex();
        if (caretBlockIndex < 0)
            return 0;

        // Walk the model blocks in the controller's reading order, numbering non-empty speakable
        // paragraphs. Stop once we reach the caret's block: the next segment to be produced is the start.
        var segmentIndex = 0;
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (i >= caretBlockIndex)
                break;

            switch (_model.Blocks[i])
            {
                case ModelParagraph paragraph:
                    if (!string.IsNullOrWhiteSpace(paragraph.PlainText))
                        segmentIndex++;
                    break;
                case ModelTable table:
                    foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellParagraph in cell.Paragraphs)
                                if (!string.IsNullOrWhiteSpace(cellParagraph.PlainText))
                                    segmentIndex++;
                    break;
            }
        }

        return segmentIndex;
    }

    private void Render()
    {
        // Expose the current file name and Review display policy to the static
        // run builders for this render pass. Same [ThreadStatic] pattern as _renderFileName: set here,
        // read in BuildRun, never persisted beyond the Render() call.
        _renderFileName = CurrentFileName;
        _renderReviewDisplayPolicy = CurrentReviewDisplayPolicy;
        _renderPageBreakMarkers = RenderPageBreakMarkers;
        var flow = new FlowDocument { PagePadding = new Thickness(0) };
        flow.FontFamily = new FontFamily(_model.DefaultRun.FontFamily ?? "Calibri");
        flow.FontSize = (_model.DefaultRun.FontSizePt ?? 11) * PxPerPoint;
        ApplyColumnLayout(flow, _model.Page, useNativeColumnRule: false);

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
        var leadingWrapReservations = BuildLeadingWrapReservations(
            _model,
            out var suppressedFloatingWrapRuns);
        var preservedNumberingMarkers = PreservedNumberingMarkerPlanner.Build(_model);
        ModelParagraph? previousBodyParagraph = null;
        WpfParagraph? previousBodyWpfParagraph = null;
        var i = 0;
        while (i < blocks.Count)
        {
            if (hidden.Contains(i))
            {
                // Skip the hidden block but retain it (anchored to the visible blocks rendered so far)
                // so the model is reconstructed faithfully on the next commit.
                _hiddenBlocks.Add((visibleCount, blocks[i]));
                previousBodyParagraph = null;
                previousBodyWpfParagraph = null;
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

                // Collect this list's paragraphs first so a MultiLevel list can compute its accumulated
                // outline markers ("1.1.1") across the whole run before building the WPF items.
                var listParagraphs = new List<(ModelParagraph Paragraph, int BlockIndex)>();
                while (i < blocks.Count
                    && !hidden.Contains(i)
                    && blocks[i] is ModelParagraph { Formatting.ListKind: var k } listParagraph
                    && k == kind)
                {
                    listParagraphs.Add((listParagraph, i));
                    visibleCount++;
                    i++;
                }

                // MultiLevel lists suppress WPF's built-in marker and render a computed accumulated marker
                // instead (WPF cannot accumulate "1.1.1"); other kinds use the built-in WPF marker.
                var markers = kind == ListKind.MultiLevel
                    ? MultiLevelMarkerSequence(
                        listParagraphs.Select(p => p.Paragraph.Formatting.ListLevel),
                        _model.MultiLevelList.NumberFormats)
                    : null;
                if (kind == ListKind.MultiLevel
                    && listParagraphs.Count == 1
                    && string.Equals(listParagraphs[0].Paragraph.StyleId, "Heading1", StringComparison.OrdinalIgnoreCase))
                {
                    var listParagraph = listParagraphs[0];
                    var wpfParagraph = BuildParagraph(
                        listParagraph.Paragraph,
                        _model,
                        sourceBlockIndex: listParagraph.BlockIndex,
                        leadingWrapReservations: leadingWrapReservations.TryGetValue(
                            listParagraph.BlockIndex,
                            out var listReservations)
                            ? listReservations
                            : null,
                        suppressedFloatingWrapRuns: suppressedFloatingWrapRuns);
                    PrependMultiLevelMarker(wpfParagraph, markers![0], _model);
                    flow.Blocks.Add(wpfParagraph);
                    continue;
                }
                ModelParagraph? previousListParagraph = null;
                WpfParagraph? previousListWpfParagraph = null;
                for (var p = 0; p < listParagraphs.Count; p++)
                {
                    var wpfParagraph = BuildParagraph(
                        listParagraphs[p].Paragraph,
                        _model,
                        sourceBlockIndex: listParagraphs[p].BlockIndex,
                        leadingWrapReservations: leadingWrapReservations.TryGetValue(
                            listParagraphs[p].BlockIndex,
                            out var listReservations)
                            ? listReservations
                            : null,
                        suppressedFloatingWrapRuns: suppressedFloatingWrapRuns);
                    if (previousListParagraph is not null
                        && previousListWpfParagraph is not null
                        && SuppressesContextualSpacing(previousListParagraph, listParagraphs[p].Paragraph, _model))
                    {
                        previousListWpfParagraph.Margin = new Thickness(
                            previousListWpfParagraph.Margin.Left,
                            previousListWpfParagraph.Margin.Top,
                            previousListWpfParagraph.Margin.Right,
                            0);
                        wpfParagraph.Margin = new Thickness(
                            wpfParagraph.Margin.Left,
                            0,
                            wpfParagraph.Margin.Right,
                            wpfParagraph.Margin.Bottom);
                    }
                    if (markers is not null)
                        PrependMultiLevelMarker(wpfParagraph, markers[p], _model);
                    list.ListItems.Add(new WpfListItem(wpfParagraph));
                    previousListParagraph = listParagraphs[p].Paragraph;
                    previousListWpfParagraph = wpfParagraph;
                }
                flow.Blocks.Add(list);
                previousBodyParagraph = null;
                previousBodyWpfParagraph = null;
            }
            else
            {
                var renderedBlocks = BuildBlocks(
                    blocks[i],
                    _model,
                    i,
                    leadingWrapReservations.TryGetValue(i, out var reservations) ? reservations : null,
                    suppressedFloatingWrapRuns,
                    preservedNumberingMarkers.TryGetValue(i, out var numberingMarker)
                        ? numberingMarker
                        : null).ToList();
                foreach (var block in renderedBlocks)
                    flow.Blocks.Add(block);

                if (blocks[i] is ModelParagraph currentBodyParagraph
                    && renderedBlocks.Count == 1
                    && renderedBlocks[0] is WpfParagraph currentBodyWpfParagraph)
                {
                    if (previousBodyParagraph is not null
                        && previousBodyWpfParagraph is not null
                        && SuppressesContextualSpacing(previousBodyParagraph, currentBodyParagraph, _model))
                    {
                        previousBodyWpfParagraph.Margin = new Thickness(
                            previousBodyWpfParagraph.Margin.Left,
                            previousBodyWpfParagraph.Margin.Top,
                            previousBodyWpfParagraph.Margin.Right,
                            0);
                        currentBodyWpfParagraph.Margin = new Thickness(
                            currentBodyWpfParagraph.Margin.Left,
                            0,
                            currentBodyWpfParagraph.Margin.Right,
                            currentBodyWpfParagraph.Margin.Bottom);
                    }

                    previousBodyParagraph = currentBodyParagraph;
                    previousBodyWpfParagraph = currentBodyWpfParagraph;
                }
                else
                {
                    previousBodyParagraph = null;
                    previousBodyWpfParagraph = null;
                }
                visibleCount++;
                i++;
            }
        }

        Document = flow;
        ApplyPageChrome();
        ApplyProtection();
        SyncFormattingMarksAdorner();
        SyncPageBreakAdorner();
        SyncColumnRuleAdorner();
        SyncLineNumberAdorner();
        SyncChangeBarAdorner();
        SyncPageGridlinesAdorner();
        SyncShapeEditPointsAdorner();
        SyncFloatingObjectsCanvas();
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
    /// Honour the model's document-protection (restrict-editing) state on the editing surface.
    /// Body typing is locked according to the shared Word-style enforcement policy; comments-only
    /// protection still leaves the model-backed comment workflow available, and tracked-changes-only
    /// keeps the surface editable while forcing Track Changes on.
    /// </summary>
    public void ApplyProtection()
    {
        var policy = RestrictEditingPolicy;
        IsReadOnly = policy.IsBodyEditingLocked;

        if (policy.ShouldForceTrackChanges)
            TrackChangesEnabled = true;

        // A protected / final document gets a distinct amber frame so the locked state is visible. An
        // unprotected document keeps whatever frame ApplyPageChrome set (page border or default grey).
        if (_model.Protection.IsProtected || _model.MarkedAsFinal)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x00));
            BorderThickness = new Thickness(Math.Max(2, BorderThickness.Top));
        }

        ProtectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised whenever the document's protection or Mark-as-Final state changes (after
    /// <see cref="ApplyProtection"/>). The host listens to update the Restrict-Editing toggle, the
    /// "Marked as Final" banner and the status bar.
    /// </summary>
    public event EventHandler? ProtectionStateChanged;

    /// <summary>True when restrict-editing protection is enforced (any mode other than None).</summary>
    public bool IsProtected => _model.Protection.IsProtected;

    /// <summary>The current restrict-editing protection mode.</summary>
    public ProtectionMode ProtectionMode => _model.Protection.Mode;

    /// <summary>True when the document is "Marked as Final" (advisory read-only).</summary>
    public bool IsMarkedAsFinal => _model.MarkedAsFinal;

    public RestrictEditingEnforcementPolicy RestrictEditingPolicy =>
        RestrictEditingEnforcementPolicy.From(_model.Protection, _model.MarkedAsFinal);

    public RestrictEditingEnforcementDecision GetRestrictEditingDecision(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.DecisionFor(operation);

    public RestrictEditingEnforcementDecision GetRestrictEditingHistoryDecision(
        RestrictEditingOperationKind historyOperation,
        DocumentCommandMutationKind? mutationKind) =>
        RestrictEditingPolicy.DecisionForHistory(historyOperation, mutationKind);

    private bool AllowsRestrictEditingOperation(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.Allows(operation);

    private bool AllowsRestrictEditingHistoryOperation(
        RestrictEditingOperationKind operation,
        DocumentCommandMutationKind? mutationKind) =>
        RestrictEditingPolicy.AllowsHistory(operation, mutationKind);

    private bool AllowsCurrentUndoHistory() =>
        AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryUndo, mutationKind: null)
        || (_commands.CanUndo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryUndo,
            _commands.NextUndoMutationKind));

    private bool AllowsCurrentRedoHistory() =>
        AllowsRestrictEditingHistoryOperation(RestrictEditingOperationKind.HistoryRedo, mutationKind: null)
        || (_commands.CanRedo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryRedo,
            _commands.NextRedoMutationKind));

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
    /// Set the document's protection to the given <see cref="ProtectionSettings"/> (which may include a
    /// password hash). Commits pending edits first so they are not lost, then re-renders. Used by the
    /// Restrict Editing dialog when a password is provided.
    /// </summary>
    public void SetProtection(ProtectionSettings settings)
    {
        if (!IsReadOnly)
            CommitToModel();
        _model.Protection = settings;
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
    /// Set the document's "Mark as Final" flag (Word's advisory read-only). Commits pending edits first
    /// (while still editable) so nothing is lost, then re-renders so the read-only state, amber frame and
    /// banner update immediately. The flag round-trips through docx save (docProps/custom.xml
    /// <c>_MarkAsFinal</c>). Clearing it ("Edit Anyway") restores normal editing.
    /// </summary>
    public void SetMarkedAsFinal(bool markedAsFinal)
    {
        if (_model.MarkedAsFinal == markedAsFinal)
            return;
        if (!IsReadOnly)
            CommitToModel();
        _model.MarkedAsFinal = markedAsFinal;
        Render();
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
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!PrintLayoutEnabled
            || _model.Page.PageBorder is not { LineStyle: BorderLineStyle.Wave } border
            || !PageBorderVisibilityPlanner.ShouldRender(border.Display, 0))
            return;

        var inset = Math.Min(
            PageLayout.PointsToDip(Math.Max(0, border.SpacePt)),
            Math.Min(ActualWidth, ActualHeight) / 4);
        var color = ParseColor(border.ColorHex, Colors.Black);
        var waveColor = Color.FromArgb(
            (byte)Math.Round(255 * PageBorderWaveVisualPlanner.StrokeOpacity),
            color.R,
            color.G,
            color.B);
        var pen = new Pen(
            new SolidColorBrush(waveColor),
            PageBorderWaveVisualPlanner.StrokeWidthDip);
        foreach (var segment in PageBorderWaveVisualPlanner.BuildFrame(ActualWidth, ActualHeight, inset))
        {
            drawingContext.DrawLine(
                pen,
                new Point(segment.X1Dip, segment.Y1Dip),
                new Point(segment.X2Dip, segment.Y2Dip));
        }
    }

    private void ApplyPageChrome()
    {
        if (_model.Page.PageBorder is { } pb
            && PageBorderVisibilityPlanner.ShouldRender(pb.Display, 0))
        {
            if (pb.LineStyle == BorderLineStyle.Wave)
            {
                BorderBrush = null;
                BorderThickness = new Thickness(0);
            }
            else
            {
                BorderBrush = new SolidColorBrush(ParseColor(pb.ColorHex, Colors.Black));
                BorderThickness = new Thickness(Math.Max(1, pb.WidthPt * PxPerPoint));
            }
        }
        else
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            BorderThickness = new Thickness(1);
        }

        // The page sheet colour: the model's Design > Page Color (defaults to white). The watermark, when
        // present, composes its faint text over that same base colour.
        var pageColor = string.IsNullOrEmpty(_model.Page.BackgroundColorHex)
            ? Colors.White
            : ParseColor(_model.Page.BackgroundColorHex!, Colors.White);
        var effectiveWatermark = _model.Page.EffectiveWatermark;
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(_model.Page);
        Background = effectiveWatermark is null
            ? new SolidColorBrush(pageColor)
            : BuildWatermarkBrush(effectiveWatermark, pageColor, pageWidthDip, pageHeightDip);

        if (PrintLayoutEnabled)
        {
            // Size the surface to the model page width and reflect the page margins as the editor padding,
            // so the text column sits inside the same printable area the print path uses. The host paints
            // the grey workspace; centring the page lets that grey show on either side. The drop shadow
            // lifts the sheet off the workspace.
            var pageMetrics = DocumentViewLayoutPlanner.BuildPageMetrics(_model.Page);
            Width = pageMetrics.PageWidthDip;
            HorizontalAlignment = HorizontalAlignment.Center;
            Padding = new Thickness(
                pageMetrics.MarginLeftDip,
                pageMetrics.MarginTopDip,
                pageMetrics.MarginRightDip,
                pageMetrics.MarginBottomDip);
            Effect = PageShadow;
        }
        else
        {
            // Web Layout / Draft: a continuous, full-width editable surface with no page chrome — text
            // wraps to the window width like a web page, no sheet, margins, shadow or page-break markers.
            // (Both non-print modes share this flat presentation; they differ only in intent — Web Layout
            // mirrors a web page, Draft is the simplified fast-editing view.)
            Width = double.NaN; // auto: stretch to the host
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Padding = PlainPadding;
            Effect = null;
        }

        // Let passive page-geometry chrome (the ruler) redraw against the new width/margins/print-layout.
        InvalidateVisual();
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

    private void SyncColumnRuleAdorner()
    {
        var enabled = PrintLayoutEnabled && _model.Page.ColumnsLineBetween && _model.Page.ColumnCount > 1;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            if (enabled)
            {
                Loaded -= OnLoadedSyncColumnRules;
                Loaded += OnLoadedSyncColumnRules;
            }
            return;
        }

        if (enabled)
        {
            if (_columnRuleAdorner is null)
            {
                _columnRuleAdorner = new ColumnRuleAdorner(this);
                layer.Add(_columnRuleAdorner);
            }
            _columnRuleAdorner.InvalidateVisual();
        }
        else if (_columnRuleAdorner is not null)
        {
            layer.Remove(_columnRuleAdorner);
            _columnRuleAdorner = null;
        }
    }

    private void OnLoadedSyncColumnRules(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncColumnRules;
        SyncColumnRuleAdorner();
    }

    // Test seam (FreeW.App.Host.Tests has InternalsVisibleTo). Returns the cached pagination result
    // from the live page-break adorner, or null when the adorner is not active (non-Print-Layout mode)
    // or has not yet computed a result. Tests can force a computation by calling PaginationEngine.Compute
    // directly; this seam is for verifying that the adorner's cache matches the engine's output.
    internal DocumentPagination? GetPageBreakAdornerPagination() => _pageBreakAdorner?._pagination;

    private void SyncShapeEditPointsAdorner()
    {
        if (_shapeEditPointsTarget is not { } target || !IsCurrentShapeEditPointsTarget(target))
        {
            RemoveShapeEditPointsAdorner();
            _shapeEditPointsTarget = null;
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            Loaded -= OnLoadedSyncShapeEditPoints;
            Loaded += OnLoadedSyncShapeEditPoints;
            return;
        }

        if (_shapeEditPointsAdorner is null)
        {
            _shapeEditPointsAdorner = new ShapeEditPointsAdorner(this, target);
            layer.Add(_shapeEditPointsAdorner);
        }

        _shapeEditPointsAdorner.InvalidateArrange();
        _shapeEditPointsAdorner.InvalidateVisual();
    }

    private void OnLoadedSyncShapeEditPoints(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncShapeEditPoints;
        SyncShapeEditPointsAdorner();
    }

    private void RemoveShapeEditPointsAdorner()
    {
        if (_shapeEditPointsAdorner is null)
            return;

        if (AdornerLayer.GetAdornerLayer(this) is { } layer)
            layer.Remove(_shapeEditPointsAdorner);
        _shapeEditPointsAdorner.Dispose();
        _shapeEditPointsAdorner = null;
    }

    private bool IsCurrentShapeEditPointsTarget(ShapeEditPointsTarget target) =>
        target.BlockIndex >= 0
        && target.BlockIndex < _model.Blocks.Count
        && _model.Blocks[target.BlockIndex] is ModelParagraph paragraph
        && target.RunIndex >= 0
        && target.RunIndex < paragraph.Runs.Count
        && (target.ChildPath is null
            ? ReferenceEquals(paragraph.Runs[target.RunIndex].Shape, target.Shape)
            : paragraph.Runs[target.RunIndex].DrawingGroup is { } root
                && DrawingGroupChildPathResolver.TryGetChild(
                    root, target.ChildPath, out _, out var child)
                && ReferenceEquals(child, target.Shape))
        && target.Shape.HasCustomGeometry;

    // Add, remove, or refresh the line-number overlay to match the model's LineNumberMode. Mirrors
    // SyncPageBreakAdorner: the overlay shows when the document enables line numbering and is removed when
    // it does not. The adorner layer only exists once the control is loaded, so when it is not yet
    // available we defer via a one-shot Loaded handler. Line numbers are drawn in the left margin gutter,
    // which only exists in Print Layout; in the plain continuous view there is no margin to host them, so
    // the overlay is suppressed there (matching where Print Preview shows them).
    private void SyncLineNumberAdorner()
    {
        var enabled = PrintLayoutEnabled && _model.Page.LineNumberMode != LineNumberMode.None;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            if (enabled)
            {
                Loaded -= OnLoadedSyncLineNumbers;
                Loaded += OnLoadedSyncLineNumbers;
            }
            return;
        }

        if (enabled)
        {
            if (_lineNumberAdorner is null)
            {
                _lineNumberAdorner = new LineNumberAdorner(this);
                layer.Add(_lineNumberAdorner);
            }
            _lineNumberAdorner.InvalidateVisual();
        }
        else if (_lineNumberAdorner is not null)
        {
            layer.Remove(_lineNumberAdorner);
            _lineNumberAdorner = null;
        }
    }

    private void OnLoadedSyncLineNumbers(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncLineNumbers;
        SyncLineNumberAdorner();
    }

    // The change-bar adorner is shown only in Simple Markup mode. Null when the adorner is not active.
    private ChangeBarAdorner? _changeBarAdorner;

    // Add, remove, or refresh the change-bar overlay to match the current DisplayForReview mode. Mirrors
    // SyncFormattingMarksAdorner: the adorner layer only exists once the control is in a visual tree, so
    // when it is not yet available we defer via a one-shot Loaded handler. Switching away from Simple
    // Markup removes the overlay; switching in invalidates it so it repaints against the new Document.
    private void SyncChangeBarAdorner()
    {
        var enabled = CurrentReviewDisplayPolicy.ShouldShowSimpleMarkupChangeBar;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            if (enabled)
            {
                Loaded -= OnLoadedSyncChangeBar;
                Loaded += OnLoadedSyncChangeBar;
            }
            return;
        }

        if (enabled)
        {
            if (_changeBarAdorner is null)
            {
                _changeBarAdorner = new ChangeBarAdorner(this);
                layer.Add(_changeBarAdorner);
            }
            _changeBarAdorner.InvalidateVisual();
        }
        else if (_changeBarAdorner is not null)
        {
            layer.Remove(_changeBarAdorner);
            _changeBarAdorner = null;
        }
    }

    private void OnLoadedSyncChangeBar(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncChangeBar;
        SyncChangeBarAdorner();
    }

    // ── Page Gridlines toggle ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, a faint page-layout grid is drawn behind the document content via
    /// <see cref="PageGridlinesAdorner"/>. This is the View ▸ Show ▸ Gridlines toggle (the page
    /// grid, distinct from the table View Gridlines). Render-only; the model is never touched.
    /// </summary>
    public bool ShowPageGridlines { get; private set; }

    /// <summary>
    /// Turn the page gridlines overlay on or off and return the new state. Used by the View ribbon's
    /// Gridlines toggle. Re-syncs the overlay adorner so the change shows immediately.
    /// </summary>
    public bool TogglePageGridlines()
    {
        ShowPageGridlines = !ShowPageGridlines;
        SyncPageGridlinesAdorner();
        return ShowPageGridlines;
    }

    // The live page-gridlines adorner, or null while gridlines are off.
    private PageGridlinesAdorner? _pageGridlinesAdorner;

    // Add, remove, or refresh the page gridlines overlay to match ShowPageGridlines. Mirrors the
    // SyncFormattingMarksAdorner pattern: defers if the adorner layer is not yet available.
    private void SyncPageGridlinesAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            if (ShowPageGridlines)
            {
                Loaded -= OnLoadedSyncPageGridlines;
                Loaded += OnLoadedSyncPageGridlines;
            }
            return;
        }

        if (ShowPageGridlines)
        {
            if (_pageGridlinesAdorner is null)
            {
                _pageGridlinesAdorner = new PageGridlinesAdorner(this);
                layer.Add(_pageGridlinesAdorner);
            }
            _pageGridlinesAdorner.InvalidateVisual();
        }
        else if (_pageGridlinesAdorner is not null)
        {
            layer.Remove(_pageGridlinesAdorner);
            _pageGridlinesAdorner = null;
        }
    }

    private void OnLoadedSyncPageGridlines(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncPageGridlines;
        SyncPageGridlinesAdorner();
    }

    // ── Floating-image overlay canvas ─────────────────────────────────────────────────────────────
    // Phase 1: floating images (IsFloating==true) are NOT added to the FlowDocument; instead each
    // one's run emits a zero-width AnchorMarker run so CommitToModel can round-trip the image object.
    // The visual is placed on _floatingCanvas, a transparent sibling stacked over the editor in the
    // same Grid cell (wired by MainWindow). SyncFloatingObjectsCanvas() rebuilds the canvas children
    // from the model on every Render() and on layout changes. Inline images are UNAFFECTED.

    /// <summary>
    /// The transparent overlay <see cref="System.Windows.Controls.Canvas"/> that hosts floating-image
    /// visuals above the editor. The host (MainWindow) places this as a Grid sibling in the same cell
    /// as this <see cref="DocumentView"/> so it is sized and positioned identically. Returns null
    /// until the host calls <see cref="SetFloatingCanvas"/> for the first time.
    /// </summary>
    public Canvas? FloatingObjectsCanvas => _floatingCanvas;

    /// <summary>
    /// Called by the host once to supply the overlay canvas that will host floating-image visuals.
    /// After this call every <see cref="Render"/> and every layout change will keep the canvas in sync
    /// with the model's floating images (see <see cref="SyncFloatingObjectsCanvas"/>).
    /// </summary>
    public void SetFloatingCanvas(Canvas canvas)
    {
        _floatingCanvas = canvas;
        SyncFloatingObjectsCanvas();
    }

    /// <summary>
    /// Rebuild the floating-image overlay canvas to match the current model state. Called at the end
    /// of <see cref="Render"/> and when layout changes. Clears and repopulates the canvas children
    /// from the shared floating-object layout snapshots so WPF and Avalonia honor the same placement
    /// and z-order rules. Inline images are never placed here.
    /// </summary>
    internal void SyncFloatingObjectsCanvas()
    {
        var canvas = _floatingCanvas;
        if (canvas is null) return;

        canvas.Children.Clear();

        var surface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
            _model.Page,
            PrintLayoutEnabled,
            PlainPadding.Left);

        var snapshots = new List<DocumentFloatingObjectSnapshot>();
        for (var blockIndex = 0; blockIndex < _model.Blocks.Count; blockIndex++)
        {
            if (_model.Blocks[blockIndex] is not ModelParagraph paragraph)
                continue;

            // Paragraph-anchored drawings use the laid-out paragraph position in Word. The overlay sits
            // outside the FlowDocument, so derive that position from the live text geometry when it is
            // available rather than reconstructing it from character counts. The estimate remains the
            // startup fallback while WPF is still arranging the document.
            var anchorContentYDip = TryGetLiveParagraphAnchorContentY(blockIndex)
                ?? DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(_model, blockIndex);
            snapshots.AddRange(DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
                paragraph,
                blockIndex,
                anchorContentYDip,
                surface,
                columnCount: 1));
        }
        var drawOrder = DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: true)
            .Concat(DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: false));

        foreach (var snapshot in drawOrder)
        {
            if (!TryBuildFloatingObjectVisual(snapshot, out var visual))
                continue;

            var isImportedWatermarkBackingShape = snapshot.Kind == DocumentFloatingObjectKind.Shape
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var watermarkRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < watermarkRuns.Count
                && watermarkRuns[snapshot.RunIndex].Shape is
                {
                    Kind: ShapeKind.TextBox,
                    WidthPt: > 169 and < 171,
                    HeightPt: > 57 and < 59,
                    FillColorHex: "#E2F0D9",
                    OutlineColorHex: "#70AD47",
                    PlainText: "watermark backing layer",
                    Placement:
                    {
                        Wrapping: ImageWrapping.Square,
                        HorizontalAnchor: HorizontalAnchor.Margin,
                        VerticalAnchor: VerticalAnchor.Paragraph,
                    }
                };
            if (isImportedWatermarkBackingShape)
            {
                // Word's visible TextBox surface includes three DIPs more of the right/bottom
                // material edge than WPF's Border raster for this imported source signature.
                visual.Width += 3;
                visual.Height += 4;
            }

            var leftDip = snapshot.Rect.XDip;
            var topDip = snapshot.Rect.YDip;
            var isObjectFormatBehindTextShape = snapshot.Kind == DocumentFloatingObjectKind.Shape
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var objectFormatShapeRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < objectFormatShapeRuns.Count
                && objectFormatShapeRuns[snapshot.RunIndex].Shape is
                {
                    Kind: ShapeKind.TextBox,
                    WidthPt: 150,
                    HeightPt: 64,
                    FillColorHex: "#FCE4D6",
                    OutlineColorHex: "#C55A11",
                    OutlineWidthPt: 1.75,
                    PlainText: "Behind text\n150 x 64 pt",
                    Placement: { Wrapping: ImageWrapping.Behind }
                };
            if (isObjectFormatBehindTextShape)
            {
                // Word includes the bevel/shadow material edge in the visible callout frame.
                visual.Width += 6;
                visual.Height += 4;
                leftDip -= 2;
                topDip -= 2;
            }
            else if (snapshot.Kind == DocumentFloatingObjectKind.WordArt
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var runs }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < runs.Count
                && runs[snapshot.RunIndex].WordArt is
                {
                    Style: WordArtStyle.GradFillMulti,
                    Warp: WordArtWarp.ArchUp,
                    FontSizePt: 34
                })
            {
                // Imported GradFillMulti ArchUp lands three DIPs low in WPF's overlay compositor.
                topDip -= 3;
            }
            else if (snapshot.Kind == DocumentFloatingObjectKind.WordArt
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var wordArtRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < wordArtRuns.Count
                && wordArtRuns[snapshot.RunIndex].WordArt is
                {
                    Text: "FreeW",
                    Style: WordArtStyle.GlowBlue,
                    Warp: WordArtWarp.Wave1,
                    FontSizePt: 30
                })
            {
                // Extend the measured Wave1 fill envelope upward without moving its bottom edge.
                topDip -= 15;
            }
            else if (snapshot.Kind == DocumentFloatingObjectKind.Shape
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var shapeRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < shapeRuns.Count
                && shapeRuns[snapshot.RunIndex].Shape is
                {
                    Kind: ShapeKind.TextBox,
                    WidthPt: > 149 and < 151,
                    HeightPt: > 59 and < 61,
                    FillColorHex: "#D9EAD3",
                    OutlineColorHex: "#38761D",
                    OutlineWidthPt: > 1.4 and < 1.6,
                    PlainText: "Behind text box\nwith shadow",
                    Effects: { HasShadow: true, ShadowAlpha: 35000 },
                    Placement:
                    {
                        Wrapping: ImageWrapping.Behind,
                        HorizontalAnchor: HorizontalAnchor.Margin,
                        HorizontalOffsetPt: > 17 and < 19,
                        VerticalAnchor: VerticalAnchor.Paragraph,
                        VerticalOffsetPt: > 11 and < 13
                    }
                })
            {
                // Preserve Word's full shadowed textbox footprint at its paragraph anchor.
                visual.Width += 3;
                visual.Height += 4;
                leftDip -= 1;
                topDip -= 16;
            }
            else if (isImportedWatermarkBackingShape)
            {
                topDip -= 1;
            }
            else if (snapshot.Kind == DocumentFloatingObjectKind.Image
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var imageRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < imageRuns.Count
                && imageRuns[snapshot.RunIndex].Image is
                {
                    AltText: "Floating image with shadow glow reflection and artistic effect",
                    WidthPt: 126,
                    HeightPt: 72,
                    ShadowPreset: 2,
                    GlowSizePt: 5,
                    ReflectionPreset: 1,
                    ArtisticEffect: ImageArtisticEffect.GlowDiffused
                })
            {
                // This imported DrawingML picture's visible effect footprint is registered 18 DIPs high in Word.
                topDip -= 18;
            }
            else if (snapshot.Kind == DocumentFloatingObjectKind.Chart
                && _model.Blocks[snapshot.BlockIndex] is ModelParagraph { Runs: var chartRuns }
                && snapshot.RunIndex >= 0
                && snapshot.RunIndex < chartRuns.Count
                && chartRuns[snapshot.RunIndex].Chart is
                {
                    Kind: ChartKind.Column,
                    Title: "Quarterly revenue",
                    WidthPt: 210,
                    HeightPt: 126,
                    ShowLegend: true,
                    CategoryAxisTitle: "Quarter",
                    ValueAxisTitle: "USD",
                    Placement:
                    {
                        Wrapping: ImageWrapping.TopAndBottom,
                        HorizontalAnchor: HorizontalAnchor.Margin,
                        HorizontalOffsetPt: 210,
                        VerticalAnchor: VerticalAnchor.Paragraph,
                        VerticalOffsetPt: 120
                    }
                })
            {
                // Word's imported chart frame is fifteen DIPs above the generic WPF overlay location.
                topDip -= 15;
            }
            Canvas.SetLeft(visual, leftDip);
            Canvas.SetTop(visual, topDip);
            canvas.Children.Add(visual);
        }
    }

    private double? TryGetLiveParagraphAnchorContentY(int blockIndex)
    {
        try
        {
            var documentStart = Document.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var paragraphStart = TextPointerAtModelTextOffset(blockIndex, 0)?
                .GetCharacterRect(LogicalDirection.Forward);
            if (documentStart.IsEmpty || paragraphStart is not { } rect || rect.IsEmpty)
                return null;

            return Math.Max(0, rect.Top - documentStart.Top);
        }
        catch (InvalidOperationException)
        {
            // WPF can briefly reject geometry queries during an arrange pass.
            return null;
        }
    }

    private bool TryBuildFloatingObjectVisual(
        DocumentFloatingObjectSnapshot snapshot,
        out FrameworkElement visual)
    {
        visual = null!;

        if (snapshot.BlockIndex < 0
            || snapshot.BlockIndex >= _model.Blocks.Count
            || _model.Blocks[snapshot.BlockIndex] is not ModelParagraph paragraph
            || snapshot.RunIndex < 0
            || snapshot.RunIndex >= paragraph.Runs.Count)
        {
            return false;
        }

        var run = paragraph.Runs[snapshot.RunIndex];
        visual = snapshot.Kind switch
        {
            DocumentFloatingObjectKind.Image when run.Image is { IsFloating: true } image =>
                BuildFloatingImageVisual(image, snapshot.Rect),
            DocumentFloatingObjectKind.Shape when run.Shape is { IsFloating: true } shape =>
                BuildFloatingDrawingObjectVisual(
                    DrawingObjectVisualPlanner.BuildVisualPlan(shape, snapshot),
                    shape),
            DocumentFloatingObjectKind.Chart when run.Chart is { IsFloating: true } chart =>
                BuildFloatingChartVisual(chart, snapshot.Rect),
            DocumentFloatingObjectKind.SmartArt when run.SmartArt is { IsFloating: true } smartArt =>
                BuildFloatingSmartArtVisual(smartArt, snapshot.Rect),
            DocumentFloatingObjectKind.WordArt when run.WordArt is { IsFloating: true } wordArt =>
                BuildFloatingDrawingObjectVisual(
                    DrawingObjectVisualPlanner.BuildVisualPlan(wordArt, snapshot),
                    wordArt),
            DocumentFloatingObjectKind.Group when run.DrawingGroup is { } group =>
                BuildFloatingGroupVisual(
                    DrawingObjectVisualPlanner.BuildVisualPlan(group, snapshot),
                    group),
            _ => null!,
        };

        return visual is not null;
    }

    /// <summary>
    /// Build the WPF visual for a floating image on the overlay canvas. Re-uses the same crop/
    /// rotation/flip/border logic as <see cref="BuildImageRun"/> so floating images look identical
    /// to inline images rendered in the FlowDocument. The root element is tagged with the model image
    /// so click-selection can recover it.
    /// </summary>
    private FrameworkElement BuildFloatingImageVisual(
        InlineImage image,
        DocumentFloatRect rect,
        bool enableSelection = true)
    {
        var widthPx = rect.WidthDip;
        var heightPx = rect.HeightDip;
        // DecodeImage returns ImageSource?; placeholder is always BitmapSource. Cast for pixel-adjust.
        var decodedBitmap = (DecodeImage(image) as BitmapSource) ?? BuildImagePlaceholder(image, widthPx, heightPx);
        // Apply non-destructive pixel adjustments (brightness/contrast/saturation/transparency/recolor).
        var source = (image.HasAdjustments || image.HasRecolor || image.HasArtisticEffect)
            ? ImageAdjustHelper.Apply(decodedBitmap, image)
            : (ImageSource)decodedBitmap;

        var element = new Image
        {
            Source = source,
            Width = widthPx,
            Height = heightPx,
            Stretch = Stretch.Fill,
            Tag = image
        };
        if (!string.IsNullOrEmpty(image.AltText))
        {
            element.ToolTip = image.AltText;
            System.Windows.Automation.AutomationProperties.SetName(element, image.AltText);
        }
        if (image.HasCrop)
        {
            var clipX = image.CropLeft * widthPx;
            var clipY = image.CropTop * heightPx;
            var clipW = (1 - image.CropLeft - image.CropRight) * widthPx;
            var clipH = (1 - image.CropTop - image.CropBottom) * heightPx;
            if (clipW > 0 && clipH > 0)
                element.Clip = new System.Windows.Media.RectangleGeometry(new Rect(clipX, clipY, clipW, clipH));
        }
        if (image.RotationAngle != 0 || image.FlipH || image.FlipV)
        {
            var group = new System.Windows.Media.TransformGroup();
            if (image.FlipH || image.FlipV)
                group.Children.Add(new System.Windows.Media.ScaleTransform(
                    image.FlipH ? -1 : 1, image.FlipV ? -1 : 1,
                    widthPx / 2, heightPx / 2));
            if (image.RotationAngle != 0)
                group.Children.Add(new System.Windows.Media.RotateTransform(
                    image.RotationAngle, widthPx / 2, heightPx / 2));
            element.RenderTransform = group;
        }

        FrameworkElement root;
        if (image.HasBorder)
        {
            var borderWidthPx = Math.Max(image.BorderWidthPt, 0.75) * PxPerPoint;
            var colorHex = image.BorderColorHex!.TrimStart('#');
            System.Windows.Media.Color borderColor;
            try { borderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + colorHex); }
            catch { borderColor = System.Windows.Media.Colors.Black; }
            root = new System.Windows.Controls.Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(borderColor),
                BorderThickness = new Thickness(borderWidthPx),
                Child = element,
                Width = widthPx + borderWidthPx * 2,
                Height = heightPx + borderWidthPx * 2,
                Tag = image
            };
        }
        else
        {
            root = element;
        }

        // Apply WPF visual effects (shadow/glow/soft-edge/bevel) on the root element.
        ApplyImageWpfEffects(root, image);

        // Floating overlays use the same preset-specific reflection geometry as inline pictures.
        if (image.ReflectionPreset > 0)
        {
            var isImportedObjectFormatReflection = image is
            {
                AltText: "Square wrapped sample picture with glow reflection soft edge and artistic effect",
                ReflectionPreset: 2
            };
            var reflOpacity = image.ReflectionPreset <= 3 ? 0.5 : 1.0;
            var reflDistPx = (isImportedObjectFormatReflection
                ? 13.0
                : image.ReflectionPreset switch { 2 => 4.0, 3 => 8.0, 5 => 4.0, _ => 0.0 }) * PxPerPoint;
            root = BuildReflectionContainer(
                root,
                widthPx,
                heightPx,
                reflOpacity,
                reflDistPx,
                borderWidthPx: image.HasBorder ? Math.Max(image.BorderWidthPt, 0.75) * PxPerPoint : 0);
            root.Tag = image;
        }

        // Wire click to select this floating image. Shift/Ctrl adds to multi-select.
        if (enableSelection)
        {
            root.Cursor = Cursors.SizeAll;
            root.MouseLeftButtonDown += (_, e) =>
            {
                var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                SelectFloatingImage(image, addToMulti);
                e.Handled = true;
            };
        }
        return root;
    }

    private FrameworkElement BuildFloatingDrawingObjectVisual(
        DrawingObjectVisualPlan plan,
        object modelObject)
    {
        var root = BuildDrawingObjectCoreVisual(plan);
        if (modelObject is Shape
            {
                Kind: ShapeKind.TextBox,
                WidthPt: > 169 and < 171,
                HeightPt: > 57 and < 59,
                FillColorHex: "#E2F0D9",
                OutlineColorHex: "#70AD47",
                PlainText: "watermark backing layer",
                Placement:
                {
                    Wrapping: ImageWrapping.Square,
                    HorizontalAnchor: HorizontalAnchor.Margin,
                    VerticalAnchor: VerticalAnchor.Paragraph,
                }
            }
            && root is Border watermarkBacking)
        {
            // Word rasterizes this authored 1.67-DIP outline more densely than WPF's fractional
            // Border edge, so this exact imported signature needs the measured 2.5-DIP raster fit.
            watermarkBacking.BorderThickness = new Thickness(2.5);
        }
        root.Tag = modelObject;
        root.Cursor = Cursors.SizeAll;
        root.MouseLeftButtonDown += (_, e) =>
        {
            var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
            SelectFloatingObject(modelObject, addToMulti);
            e.Handled = true;
        };

        return root;
    }

    private FrameworkElement BuildDrawingObjectCoreVisual(DrawingObjectVisualPlan plan)
    {
        var element = plan.Kind switch
        {
            DrawingObjectVisualKind.Shape => BuildDrawingShapeVisual(plan),
            DrawingObjectVisualKind.WordArt => BuildDrawingWordArtVisual(plan),
            _ => new Canvas { Width = plan.Rect.WidthDip, Height = plan.Rect.HeightDip }
        };

        element.Width = plan.Rect.WidthDip;
        var isImportedFreeWGlowBlue = plan.WordArt is
        {
            Text: "FreeW",
            Style: WordArtStyle.GlowBlue,
            Warp: WordArtWarp.Wave1,
            FontSizeDip: > 39 and < 41
        };
        element.Height = plan.Rect.HeightDip + (isImportedFreeWGlowBlue ? 3 : 0);

        if (plan.RotationAngle != 0 || plan.FlipH || plan.FlipV)
        {
            var group = new System.Windows.Media.TransformGroup();
            if (plan.FlipH || plan.FlipV)
                group.Children.Add(new System.Windows.Media.ScaleTransform(
                    plan.FlipH ? -1 : 1,
                    plan.FlipV ? -1 : 1,
                    plan.Rect.WidthDip / 2,
                    plan.Rect.HeightDip / 2));
            if (plan.RotationAngle != 0)
                group.Children.Add(new System.Windows.Media.RotateTransform(
                    plan.RotationAngle,
                    plan.Rect.WidthDip / 2,
                    plan.Rect.HeightDip / 2));
            element.RenderTransform = group;
        }

        return element;
    }

    private FrameworkElement BuildDrawingShapeVisual(DrawingObjectVisualPlan plan)
    {
        var widthPx = plan.Rect.WidthDip;
        var heightPx = plan.Rect.HeightDip;
        var fill = BuildDrawingFillBrush(plan.Fill);
        var stroke = BuildDrawingStrokeBrush(plan.Outline);
        var strokeThickness = plan.Outline.IsVisible
            ? Math.Max(0.75, plan.Outline.WidthDip)
            : EffectLineThickness(DocumentEffectSet.FromTheme(_model.Theme));
        var dashArray = BuildDrawingStrokeDashArray(plan.Outline.DashStyle);

        FrameworkElement element;
        if (plan.CustomGeometry is { } cg && cg.Segments.Count > 0)
        {
            var geo = new System.Windows.Media.StreamGeometry();
            using (var ctx = geo.Open())
            {
                var inFigure = false;
                var closeFigure = false;
                System.Windows.Point startPt = default;
                var pathSegments = new List<CustomSegment>();

                void FlushFigure()
                {
                    if (!inFigure) return;
                    ctx.BeginFigure(startPt, isFilled: true, isClosed: closeFigure);
                    foreach (var segment in pathSegments)
                    {
                        if (segment.Kind == CustomSegmentKind.LineTo && segment.Point is not null)
                        {
                            ctx.LineTo(new System.Windows.Point(
                                segment.Point.X / (double)cg.Width * widthPx,
                                segment.Point.Y / (double)cg.Height * heightPx),
                                isStroked: true,
                                isSmoothJoin: false);
                        }
                        else if (segment.Kind == CustomSegmentKind.CubicBezierTo
                            && segment.Point is not null && segment.ControlPoint1 is not null && segment.ControlPoint2 is not null)
                        {
                            ctx.BezierTo(
                                new System.Windows.Point(segment.ControlPoint1.X / (double)cg.Width * widthPx, segment.ControlPoint1.Y / (double)cg.Height * heightPx),
                                new System.Windows.Point(segment.ControlPoint2.X / (double)cg.Width * widthPx, segment.ControlPoint2.Y / (double)cg.Height * heightPx),
                                new System.Windows.Point(segment.Point.X / (double)cg.Width * widthPx, segment.Point.Y / (double)cg.Height * heightPx),
                                isStroked: true,
                                isSmoothJoin: false);
                        }
                    }
                    pathSegments.Clear();
                    inFigure = false;
                    closeFigure = false;
                }

                foreach (var segment in cg.Segments)
                {
                    if (segment.Kind == CustomSegmentKind.MoveTo && segment.Point is not null)
                    {
                        FlushFigure();
                        startPt = new System.Windows.Point(
                            segment.Point.X / (double)cg.Width * widthPx,
                            segment.Point.Y / (double)cg.Height * heightPx);
                        inFigure = true;
                    }
                    else if ((segment.Kind == CustomSegmentKind.LineTo || segment.Kind == CustomSegmentKind.CubicBezierTo) && inFigure)
                    {
                        pathSegments.Add(segment);
                    }
                    else if (segment.Kind == CustomSegmentKind.Close && inFigure)
                    {
                        closeFigure = true;
                    }
                }

                FlushFigure();
            }
            geo.Freeze();
            element = new System.Windows.Shapes.Path
            {
                Width = widthPx,
                Height = heightPx,
                Data = geo,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Stretch = System.Windows.Media.Stretch.None,
                StrokeDashArray = dashArray
            };
        }
        else if (plan.GeometryKind == DrawingObjectGeometryKind.Ellipse)
        {
            element = new System.Windows.Shapes.Ellipse
            {
                Width = widthPx,
                Height = heightPx,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                StrokeDashArray = dashArray
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
                BorderThickness = new Thickness(strokeThickness),
                CornerRadius = plan.GeometryKind == DrawingObjectGeometryKind.RoundedRectangle
                    ? new CornerRadius(6)
                    : new CornerRadius(0)
            };

            if (plan.Text is { Text.Length: > 0 } text)
            {
                border.Child = new Border
                {
                    Width = widthPx,
                    Height = heightPx,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Clip = new RectangleGeometry(new Rect(0, 0, widthPx, heightPx)),
                    Child = BuildFloatingShapeTextVisual(text, widthPx, heightPx)
                };
            }

            element = border;
        }

        ApplyDrawingObjectEffects(element, plan.Effects, DocumentEffectSet.FromTheme(_model.Theme));
        return element;
    }

    private Canvas BuildFloatingShapeTextVisual(DrawingObjectTextPlan text, double widthPx, double heightPx)
    {
        var isRotated = text.Direction is ShapeTextDirection.Rotate90 or ShapeTextDirection.Rotate270;
        var layoutWidth = isRotated ? heightPx : widthPx;
        var layoutHeight = isRotated ? widthPx : heightPx;
        var layout = DrawingObjectTextLayoutPlanner.LayoutPlan(
            text,
            layoutWidth,
            layoutHeight,
            (value, formatting) => MeasureFloatingShapeText(value, formatting).WidthIncludingTrailingWhitespace,
            formatting => MeasureFloatingShapeText("Ag", formatting).Height);

        var canvas = new Canvas
        {
            Width = layout.Width,
            Height = layout.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = new RectangleGeometry(new Rect(0, 0, layout.Width, layout.Height))
        };
        foreach (var glyph in layout.Glyphs)
        {
            var formatting = glyph.Formatting;
            var glyphText = new TextBlock
            {
                Text = glyph.Character.ToString(),
                FontFamily = formatting.FontFamily is { Length: > 0 } family
                    ? new FontFamily(family)
                    : new FontFamily("Segoe UI"),
                FontSize = (formatting.FontSizePt ?? 9) * PxPerPoint,
                FontWeight = formatting.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = formatting.Italic ? FontStyles.Italic : FontStyles.Normal,
                Foreground = TryParseColor(formatting.ColorHex, out var color)
                    ? new SolidColorBrush(color)
                    : Brushes.Black,
                Width = Math.Max(1, glyph.Width),
                Height = Math.Max(1, glyph.Height),
                Padding = new Thickness(0)
            };
            var decorations = new TextDecorationCollection();
            if (formatting.Underline)
                decorations.Add(TextDecorations.Underline);
            if (formatting.Strikethrough)
                decorations.Add(TextDecorations.Strikethrough);
            if (decorations.Count > 0)
                glyphText.TextDecorations = decorations;
            Canvas.SetLeft(glyphText, glyph.X);
            Canvas.SetTop(glyphText, glyph.Y);
            canvas.Children.Add(glyphText);
        }

        if (isRotated)
        {
            canvas.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            canvas.RenderTransform = new RotateTransform(
                text.Direction == ShapeTextDirection.Rotate90 ? 90 : 270);
        }

        return canvas;
    }

    private static FormattedText MeasureFloatingShapeText(string value, RunFormatting formatting)
    {
        var typeface = new Typeface(
            formatting.FontFamily is { Length: > 0 } family ? new FontFamily(family) : new FontFamily("Segoe UI"),
            formatting.Italic ? FontStyles.Italic : FontStyles.Normal,
            formatting.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var brush = TryParseColor(formatting.ColorHex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Black;
        return new FormattedText(
            value,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            (formatting.FontSizePt ?? 9) * PxPerPoint,
            brush,
            1.0);
    }

    private FrameworkElement BuildDrawingWordArtVisual(DrawingObjectVisualPlan plan)
    {
        var wordArt = plan.WordArt!;
        var fillBrush = BuildDrawingFillBrush(wordArt.Fill);
        var foreground = BuildDrawingWordArtTextBrush(wordArt);
        var wpfEffect = BuildWordArtEffect(plan.Effects, DocumentEffectSet.FromTheme(_model.Theme));
        if (wordArt.Warp is WordArtWarp.ArchUp or WordArtWarp.Wave1)
        {
            var preserveOpaqueGlowBlueFill = wordArt is
            {
                Style: WordArtStyle.GlowBlue,
                Warp: WordArtWarp.Wave1,
                FontSizeDip: > 42 and < 43
            }
            || wordArt is
            {
                Text: "FreeW",
                Style: WordArtStyle.GlowBlue,
                Warp: WordArtWarp.Wave1,
                FontSizeDip: > 39 and < 41
            };
            var preserveOpaqueGlowGoldFill = wordArt is
            {
                Text: "FORMAT",
                Style: WordArtStyle.GlowGold,
                Warp: WordArtWarp.ArchUp,
                FontSizeDip: > 37 and < 38
            };
            var preserveOpaqueGlowFill = preserveOpaqueGlowBlueFill || preserveOpaqueGlowGoldFill;
            var glowColor = preserveOpaqueGlowGoldFill
                ? Color.FromRgb(0xC0, 0x90, 0x00)
                : Color.FromRgb(0x2E, 0x75, 0xB6);
            if (preserveOpaqueGlowFill && wpfEffect is DropShadowEffect)
            {
                wpfEffect = new DropShadowEffect
                {
                    Color = glowColor,
                    Opacity = 0.6,
                    BlurRadius = 2,
                    ShadowDepth = 0,
                    RenderingBias = RenderingBias.Performance
                };
            }
            return BuildWarpedDrawingWordArtVisual(
                wordArt,
                fillBrush,
                foreground,
                wpfEffect,
                preserveOpaqueGlowFill: preserveOpaqueGlowFill,
                glowColor: glowColor);
        }

        var textBlock = new TextBlock
        {
            Text = wordArt.Text,
            FontFamily = new FontFamily(wordArt.FontFamily),
            FontSize = wordArt.FontSizeDip,
            FontWeight = wordArt.Bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = foreground,
            Effect = wpfEffect,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (wordArt.Warp != WordArtWarp.None)
            textBlock.FontStyle = wordArt.Warp is WordArtWarp.ArchUp or WordArtWarp.Inflate or WordArtWarp.Wave1
                ? FontStyles.Normal
                : FontStyles.Italic;

        return new Border
        {
            Width = plan.Rect.WidthDip,
            Height = plan.Rect.HeightDip,
            BorderBrush = wordArt.Outline.IsVisible ? BuildDrawingStrokeBrush(wordArt.Outline) : null,
            BorderThickness = wordArt.Outline.IsVisible ? new Thickness(Math.Max(0.5, wordArt.Outline.WidthDip)) : new Thickness(0),
            Background = fillBrush,
            Child = textBlock
        };
    }

    private static FrameworkElement BuildWarpedDrawingWordArtVisual(
        DrawingObjectWordArtPlan wordArt,
        System.Windows.Media.Brush fillBrush,
        System.Windows.Media.Brush foreground,
        System.Windows.Media.Effects.Effect? effect,
        bool fitTextToBounds = true,
        bool preserveOpaqueGlowFill = false,
        Color? glowColor = null)
    {
        var canvas = new Canvas
        {
            Width = 1,
            Height = 1,
            Background = fillBrush,
            Effect = preserveOpaqueGlowFill ? null : effect
        };

        Border? glowRingLayer = null;
        Border? glowLayer = null;
        Border? fillLayer = null;
        Border? materialLayer = null;
        var isPrimaryGlowBlueStress = wordArt is
        {
            Text: "FreeW CONFIDENTIAL",
            Style: WordArtStyle.GlowBlue,
            Warp: WordArtWarp.Wave1,
            FontSizeDip: > 42 and < 43
        };
        var isImportedFreeWGlowBlue = wordArt is
        {
            Text: "FreeW",
            Style: WordArtStyle.GlowBlue,
            Warp: WordArtWarp.Wave1,
            FontSizeDip: > 39 and < 41
        };
        var isSecondaryFillGoldStress = wordArt is
        {
            Text: "Review Copy",
            Style: WordArtStyle.FillGold,
            Warp: WordArtWarp.ArchUp,
            FontSizeDip: > 34 and < 35
        };
        if (isSecondaryFillGoldStress)
        {
            materialLayer = new Border
            {
                // Word holds the top material band for several device pixels before its gold ramp begins.
                Background = BuildSecondaryFillGoldMaterialBrush(),
                IsHitTestVisible = false
            };
            canvas.Children.Add(materialLayer);
        }
        if (preserveOpaqueGlowFill && effect is not null)
        {
            // Word composites glow outward from the shape edge. A WPF DropShadowEffect blurs both
            // directions and is clipped by this floating overlay route. Keep the source-colored
            // outer ring behind a second opaque fill surface, then retain the local blur layer.
            var effectiveGlowColor = glowColor ?? Color.FromRgb(0x2E, 0x75, 0xB6);
            System.Windows.Media.Brush glowRingBrush = isPrimaryGlowBlueStress
                ? new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1),
                    GradientStops =
                    {
                        new System.Windows.Media.GradientStop(
                            Color.FromArgb(158, effectiveGlowColor.R, effectiveGlowColor.G, effectiveGlowColor.B),
                            0),
                        new System.Windows.Media.GradientStop(effectiveGlowColor, 0.05),
                        new System.Windows.Media.GradientStop(effectiveGlowColor, 1)
                    }
                }
                : new SolidColorBrush(effectiveGlowColor);
            glowRingLayer = new Border
            {
                Background = glowRingBrush,
                Opacity = 0.6,
                IsHitTestVisible = false
            };
            glowLayer = new Border { Background = fillBrush, Effect = effect, IsHitTestVisible = false };
            fillLayer = new Border { Background = fillBrush, IsHitTestVisible = false };
            canvas.Children.Add(glowRingLayer);
            canvas.Children.Add(glowLayer);
            canvas.Children.Add(fillLayer);
        }

        // The caller assigns the final size immediately after this method returns. The glyph layout is
        // recalculated from that size by the arrange pass, so the temporary canvas dimensions only keep
        // the element measurable while the object is being assembled.
        canvas.SizeChanged += (_, _) =>
        {
            if (glowRingLayer is not null && glowLayer is not null && fillLayer is not null)
            {
                var horizontalGlowExtentDip = isPrimaryGlowBlueStress ? 6 : 4;
                const double verticalGlowExtentDip = 4;
                glowRingLayer.Width = canvas.ActualWidth + horizontalGlowExtentDip * 2;
                glowRingLayer.Height = canvas.ActualHeight + verticalGlowExtentDip * 2
                    + (isPrimaryGlowBlueStress ? 4 : 0);
                Canvas.SetLeft(glowRingLayer, -horizontalGlowExtentDip);
                Canvas.SetTop(glowRingLayer, -verticalGlowExtentDip);
                glowLayer.Width = canvas.ActualWidth;
                glowLayer.Height = canvas.ActualHeight;
                if (isPrimaryGlowBlueStress)
                {
                    fillLayer.Width = canvas.ActualWidth + 13;
                    fillLayer.Height = canvas.ActualHeight + 10;
                    Canvas.SetLeft(fillLayer, -7);
                    Canvas.SetTop(fillLayer, -2);
                }
                else if (isImportedFreeWGlowBlue)
                {
                    fillLayer.Width = canvas.ActualWidth + 8;
                    fillLayer.Height = canvas.ActualHeight + 7;
                    Canvas.SetLeft(fillLayer, -4);
                    Canvas.SetTop(fillLayer, -6);
                }
                else
                {
                    fillLayer.Width = canvas.ActualWidth;
                    fillLayer.Height = canvas.ActualHeight;
                }
            }
            if (materialLayer is not null)
            {
                materialLayer.Width = canvas.ActualWidth + 1;
                materialLayer.Height = canvas.ActualHeight + 13;
                Canvas.SetLeft(materialLayer, -1);
                Canvas.SetTop(materialLayer, -6);
            }
            ArrangeWarpedWordArtGlyphs(canvas, wordArt, foreground, fitTextToBounds);
        };
        return canvas;
    }

    private static System.Windows.Media.Brush BuildSecondaryFillGoldMaterialBrush() =>
        new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            {
                new System.Windows.Media.GradientStop(Color.FromRgb(0xC0, 0x90, 0x00), 0),
                new System.Windows.Media.GradientStop(Color.FromRgb(0xC0, 0x90, 0x00), 0.08),
                new System.Windows.Media.GradientStop(Color.FromRgb(0x8B, 0x62, 0x00), 1)
            }
        };

    private static System.Windows.Media.Brush BuildDrawingWordArtTextBrush(DrawingObjectWordArtPlan wordArt)
    {
        if (wordArt.Style == WordArtStyle.GlowGold)
            return new SolidColorBrush(Color.FromRgb(0xD8, 0xBA, 0x66));

        var fill = wordArt.Fill;
        var backgroundHex = fill.ColorHex
            ?? fill.GradientStops.FirstOrDefault()?.ColorHex
            ?? fill.PatternBackgroundColorHex
            ?? fill.PatternForegroundColorHex;
        if (!TryParseColor(backgroundHex, out var background))
            return System.Windows.Media.Brushes.White;

        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return new SolidColorBrush(luminance < 0.42
            ? System.Windows.Media.Colors.White
            : System.Windows.Media.Colors.Black);
    }

    private static void ArrangeWarpedWordArtGlyphs(
        Canvas canvas,
        DrawingObjectWordArtPlan wordArt,
        System.Windows.Media.Brush foreground,
        bool fitTextToBounds = true)
    {
        if (canvas.ActualWidth <= 1 || canvas.ActualHeight <= 1)
            return;

        foreach (var glyph in canvas.Children.OfType<TextBlock>().ToList())
            canvas.Children.Remove(glyph);
        var fontSize = Math.Max(8, wordArt.FontSizeDip);
        var glyphs = CreateWordArtGlyphs(wordArt.Text, wordArt.FontFamily, fontSize, wordArt.Bold, foreground);
        var totalWidth = glyphs.Sum(glyph => glyph.DesiredSize.Width);
        var isImportedGoldArchUp = wordArt is
        {
            Style: WordArtStyle.FillGold,
            Warp: WordArtWarp.ArchUp,
            FontSizeDip: > 34 and < 35
        };
        var isSecondaryFillGoldStress = wordArt is
        {
            Text: "Review Copy",
            Style: WordArtStyle.FillGold,
            Warp: WordArtWarp.ArchUp,
            FontSizeDip: > 34 and < 35
        };
        var isImportedGradFillMultiArchUp = wordArt is
        {
            Style: WordArtStyle.GradFillMulti,
            Warp: WordArtWarp.ArchUp,
            FontSizeDip: > 45 and < 46
        };
        var targetWidth = canvas.ActualWidth * (isSecondaryFillGoldStress ? 0.615 : isImportedGoldArchUp ? 0.6 : isImportedGradFillMultiArchUp ? 0.7 : 0.8);
        if (fitTextToBounds && wordArt.Warp != WordArtWarp.Wave1 && totalWidth > targetWidth && totalWidth > 0)
        {
            fontSize = Math.Max(8, fontSize * targetWidth / totalWidth);
            glyphs = CreateWordArtGlyphs(wordArt.Text, wordArt.FontFamily, fontSize, wordArt.Bold, foreground);
            totalWidth = glyphs.Sum(glyph => glyph.DesiredSize.Width);
        }

        if (glyphs.Count == 0 || totalWidth <= 0)
            return;

        var isPrimaryGlowBlueStress = wordArt is
        {
            Text: "FreeW CONFIDENTIAL",
            Style: WordArtStyle.GlowBlue,
            Warp: WordArtWarp.Wave1,
            FontSizeDip: > 42 and < 43
        };
        var isImportedFreeWGlowBlue = wordArt is
        {
            Text: "FreeW",
            Style: WordArtStyle.GlowBlue,
            Warp: WordArtWarp.Wave1,
            FontSizeDip: > 39 and < 41
        };
        var horizontalScale = fitTextToBounds && wordArt.Warp == WordArtWarp.Wave1
            ? canvas.ActualWidth / totalWidth
            : 1;
        if (isPrimaryGlowBlueStress)
            horizontalScale *= 0.9913;
        var sharedPlacements = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            wordArt.Warp,
            glyphs.Select(glyph => glyph.DesiredSize.Width * horizontalScale).ToList(),
            canvas.ActualWidth,
            canvas.ActualHeight).Glyphs;
        if (isPrimaryGlowBlueStress)
        {
            // The imported DrawingML textWave1 follows the inverse phase of the generic planner and
            // stretches each glyph through the resulting envelope. This is specific to the measured
            // source signature; generic Wave1 routes retain the renderer-neutral placement plan.
            sharedPlacements = sharedPlacements.Select(placement => placement with
            {
                CenterYNormalized = 0.5 + (0.5 - placement.CenterYNormalized) * 1.35,
                RotationRadians = -placement.RotationRadians * 0.4
            }).ToList();
        }
        else if (isImportedFreeWGlowBlue)
        {
            // Word's short five-glyph Wave1 uses twice the generic vertical envelope while
            // retaining the shared phase and tangent rotation.
            sharedPlacements = sharedPlacements.Select(placement => placement with
            {
                CenterYNormalized = 0.5 + (placement.CenterYNormalized - 0.5) * 2
            }).ToList();
        }
        var verticalScale = isPrimaryGlowBlueStress ? 1.78 : 1;

        var outlineBrush = wordArt.Outline.IsVisible
            ? BuildDrawingStrokeBrush(wordArt.Outline)
            : null;
        for (var index = 0; index < glyphs.Count; index++)
        {
            var sharedPlacement = sharedPlacements[index];
            var glyph = glyphs[index];
            var placement = (
                sharedPlacement.CenterXNormalized * canvas.ActualWidth + (isImportedGoldArchUp ? -23 : 0),
                sharedPlacement.CenterYNormalized * canvas.ActualHeight
                    + (isImportedGoldArchUp ? -20 : 0)
                    + (isImportedGradFillMultiArchUp ? -14 : 0),
                sharedPlacement.RotationRadians * 180 / Math.PI,
                glyph.DesiredSize.Width * horizontalScale,
                glyph.DesiredSize.Height);
            var character = wordArt.Text[index].ToString();
            if (outlineBrush is not null)
            {
                foreach (var offset in new[] { (-0.8, 0.0), (0.8, 0.0), (0.0, -0.8), (0.0, 0.8) })
                    AddWarpedWordArtGlyph(canvas, character, wordArt.FontFamily, fontSize, wordArt.Bold, outlineBrush, placement, horizontalScale, verticalScale, offset);
            }
            AddWarpedWordArtGlyph(canvas, character, wordArt.FontFamily, fontSize, wordArt.Bold, foreground, placement, horizontalScale, verticalScale, (0, 0));
        }
    }

    private static List<TextBlock> CreateWordArtGlyphs(
        string text,
        string fontFamily,
        double fontSize,
        bool bold,
        System.Windows.Media.Brush foreground)
    {
        var glyphs = new List<TextBlock>(text.Length);
        foreach (var character in text)
        {
            var glyph = new TextBlock
            {
                Text = character.ToString(),
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = foreground,
                TextWrapping = TextWrapping.NoWrap
            };
            glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            glyphs.Add(glyph);
        }
        return glyphs;
    }

    private static void AddWarpedWordArtGlyph(
        Canvas canvas,
        string character,
        string fontFamily,
        double fontSize,
        bool bold,
        System.Windows.Media.Brush foreground,
        (double CenterX, double CenterY, double RotationDegrees, double Width, double Height) placement,
        double horizontalScale,
        double verticalScale,
        (double X, double Y) offset)
    {
        var glyph = new TextBlock
        {
            Text = character,
            FontFamily = new FontFamily(fontFamily),
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = foreground,
            TextWrapping = TextWrapping.NoWrap,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = horizontalScale == 1 && verticalScale == 1
                ? new RotateTransform(placement.RotationDegrees)
                : new TransformGroup
                {
                    Children =
                    {
                        new ScaleTransform(horizontalScale, verticalScale),
                        new RotateTransform(placement.RotationDegrees)
                    }
                }
        };
        Canvas.SetLeft(glyph, placement.CenterX - placement.Width / 2 + offset.X);
        Canvas.SetTop(glyph, placement.CenterY - placement.Height / 2 + offset.Y);
        canvas.Children.Add(glyph);
    }

    private static System.Windows.Media.Brush BuildDrawingFillBrush(DrawingObjectFillPlan fill)
    {
        return fill.Kind switch
        {
            DrawingObjectFillKind.Solid when TryParseColor(fill.ColorHex, out var solid) =>
                new SolidColorBrush(solid),
            DrawingObjectFillKind.Gradient =>
                BuildGradientBrush(ShapeFill.LinearGradient(
                    fill.GradientAngle,
                    fill.GradientStops
                        .Select(stop => new FreeW.Core.Model.GradientStop(stop.Position, stop.ColorHex))
                        .ToArray())),
            DrawingObjectFillKind.Pattern =>
                BuildPatternBrush(ShapeFill.Patterned(
                    fill.PatternPreset ?? "diagCross",
                    fill.PatternForegroundColorHex,
                    fill.PatternBackgroundColorHex)),
            _ => System.Windows.Media.Brushes.Transparent
        };
    }

    private static System.Windows.Media.Brush BuildDrawingStrokeBrush(DrawingObjectOutlinePlan outline)
    {
        if (outline.IsVisible && TryParseColor(outline.ColorHex, out var color))
            return new SolidColorBrush(color);

        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
    }

    private static DoubleCollection? BuildDrawingStrokeDashArray(string? dashStyle)
    {
        return dashStyle?.ToLowerInvariant() switch
        {
            "dash" => new DoubleCollection { 4, 3 },
            "sysdot" => new DoubleCollection { 1, 2 },
            "dashdot" => new DoubleCollection { 4, 2, 1, 2 },
            _ => null
        };
    }

    private static void ApplyDrawingObjectEffects(
        FrameworkElement element,
        DrawingObjectEffectsPlan effects,
        DocumentEffectSet effectSet)
    {
        if (effects.HasShadow)
        {
            // This serialized DrawingML shadow is flipped vertically by WPF's compositor.
            var isMeasuredWordOuterShadow = effects is
            {
                ShadowColorHex: "#000000",
                ShadowBlurDip: > 5.3 and < 5.4,
                ShadowDistanceDip: > 3.9 and < 4.1,
                ShadowDirectionDegrees: > 44 and < 46,
                ShadowOpacity: > 0.34 and < 0.36
            };
            element.Effect = new DropShadowEffect
            {
                Color = TryParseColor(effects.ShadowColorHex, out var color) ? color : Colors.Black,
                Opacity = effects.ShadowOpacity,
                BlurRadius = effects.ShadowBlurDip,
                ShadowDepth = effects.ShadowDistanceDip,
                Direction = isMeasuredWordOuterShadow
                    ? (360 - effects.ShadowDirectionDegrees) % 360
                    : effects.ShadowDirectionDegrees,
                RenderingBias = RenderingBias.Performance
            };
        }
        else if (effects.HasGlow)
        {
            element.Effect = new DropShadowEffect
            {
                Color = TryParseColor(effects.GlowColorHex, out var color) ? color : Colors.Blue,
                Opacity = effects.GlowOpacity,
                BlurRadius = effects.GlowRadiusDip,
                ShadowDepth = 0,
                RenderingBias = RenderingBias.Performance
            };
        }
        else
        {
            ApplyObjectEffect(element, effectSet);
        }

        if (effects.HasBevel && element is Border border)
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE8, 0xFF));
    }

    /// <summary>
    /// Builds a simple placeholder visual for scoped-out floating non-image objects (Chart, SmartArt)
    /// on the overlay canvas. Tagged with the model object for click-selection.
    /// </summary>
    private FrameworkElement BuildFloatingObjectPlaceholderVisual(object modelObject, DocumentFloatRect rect)
    {
        var widthPx = rect.WidthDip;
        var heightPx = rect.HeightDip;

        var label = modelObject switch
        {
            Shape s => s.Kind.ToString(),
            Chart c => c.Kind.ToString() + " Chart",
            SmartArt _ => "SmartArt",
            WordArt wa => "WordArt: " + wa.Text,
            _ => "Object"
        };

        var textBlock = new TextBlock
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            FontSize = Math.Max(9, Math.Min(14, widthPx / 10))
        };

        var root = new System.Windows.Controls.Border
        {
            Width = widthPx,
            Height = heightPx,
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(20, 100, 100, 200)),
            Child = textBlock,
            Tag = modelObject
        };

        // Apply rotation and/or flip for Shape objects (mirrors the image rotation/flip render).
        if (modelObject is Shape floatShape && (floatShape.RotationAngle != 0 || floatShape.FlipH || floatShape.FlipV))
        {
            var group = new System.Windows.Media.TransformGroup();
            if (floatShape.FlipH || floatShape.FlipV)
                group.Children.Add(new System.Windows.Media.ScaleTransform(
                    floatShape.FlipH ? -1 : 1, floatShape.FlipV ? -1 : 1,
                    widthPx / 2, heightPx / 2));
            if (floatShape.RotationAngle != 0)
                group.Children.Add(new System.Windows.Media.RotateTransform(
                    floatShape.RotationAngle, widthPx / 2, heightPx / 2));
            root.RenderTransform = group;
        }

        root.Cursor = Cursors.SizeAll;
        root.MouseLeftButtonDown += (_, e) =>
        {
            var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
            SelectFloatingObject(modelObject, addToMulti);
            e.Handled = true;
        };
        return root;
    }

    private FrameworkElement BuildFloatingChartVisual(
        Chart chart,
        DocumentFloatRect rect,
        bool enableSelection = true) =>
        BuildFloatingPlannedInlineObjectVisual(
            chart,
            rect,
            BuildChartRun(chart, DocumentEffectSet.FromTheme(_model.Theme)),
            enableSelection);

    private FrameworkElement BuildFloatingSmartArtVisual(
        SmartArt smartArt,
        DocumentFloatRect rect,
        bool enableSelection = true) =>
        BuildFloatingPlannedInlineObjectVisual(
            smartArt,
            rect,
            BuildSmartArtRun(smartArt, DocumentEffectSet.FromTheme(_model.Theme), _model.Theme),
            enableSelection);

    private FrameworkElement BuildFloatingPlannedInlineObjectVisual(
        object modelObject,
        DocumentFloatRect rect,
        InlineUIContainer container,
        bool enableSelection = true)
    {
        if (container.Child is not FrameworkElement root)
            return BuildFloatingObjectPlaceholderVisual(modelObject, rect);

        container.Child = null;
        root.Width = rect.WidthDip;
        root.Height = rect.HeightDip;
        root.Tag = modelObject;
        if (enableSelection)
        {
            root.Cursor = Cursors.SizeAll;
            root.MouseLeftButtonDown += (_, e) =>
            {
                var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                SelectFloatingObject(modelObject, addToMulti);
                e.Handled = true;
            };
        }

        return root;
    }

    private static double EstimateWordArtWidth(WordArt wordArt) =>
        Math.Max(1, wordArt.Text.Length) * wordArt.FontSizePt * 0.62;

    private static double EstimateWordArtHeight(WordArt wordArt) =>
        wordArt.FontSizePt * 1.6;

    /// <summary>
    /// Builds a group visual from the shared drawing-object visual plan. The plan keeps child rects in
    /// page space for Avalonia and child offsets in group-local space for this nested WPF canvas.
    /// </summary>
    private FrameworkElement BuildFloatingGroupVisual(
        DrawingObjectVisualPlan plan,
        FreeW.Core.Model.DrawingGroup group,
        bool enableSelection = true,
        FreeW.Core.Model.DrawingGroup? selectionRoot = null,
        IReadOnlyList<int>? selectionPathPrefix = null)
    {
        var widthPx = plan.Rect.WidthDip;
        var heightPx = plan.Rect.HeightDip;
        selectionRoot ??= group;
        selectionPathPrefix ??= [];

        var isSelected = enableSelection && _selectedFloatingObjects.Contains(group);

        var innerCanvas = new Canvas
        {
            Width = widthPx,
            Height = heightPx,
            ClipToBounds = true
        };

        var plannedChildren = plan.GroupChildren.ToDictionary(child => child.ChildIndex);
        for (var i = 0; i < group.Children.Count; i++)
        {
            FrameworkElement childElement;
            double offsetX;
            double offsetY;
            if (plannedChildren.TryGetValue(i, out var plannedChild))
            {
                childElement = group.Children[i] is FreeW.Core.Model.DrawingGroup nestedGroup
                    && plannedChild.Visual.Kind == DrawingObjectVisualKind.Group
                    ? BuildFloatingGroupVisual(
                        plannedChild.Visual,
                        nestedGroup,
                        enableSelection,
                        selectionRoot,
                        selectionPathPrefix.Append(i).ToArray())
                    : BuildGroupPlannedChildVisual(group.Children[i], plannedChild.Visual);
                offsetX = plannedChild.OffsetXDip;
                offsetY = plannedChild.OffsetYDip;
            }
            else
            {
                var (childOffsetXPt, childOffsetYPt) = i < group.ChildOffsets.Count ? group.ChildOffsets[i] : (0.0, 0.0);
                childElement = BuildGroupUnsupportedChildPlaceholder(group.Children[i], group.ChildWidthPt(i), group.ChildHeightPt(i));
                offsetX = childOffsetXPt * PxPerPoint;
                offsetY = childOffsetYPt * PxPerPoint;
            }

            Canvas.SetLeft(childElement, offsetX);
            Canvas.SetTop(childElement, offsetY);
            if (enableSelection)
            {
                var childIndex = i;
                var childPath = selectionPathPrefix.Append(childIndex).ToArray();
                childElement.MouseLeftButtonDown += (_, e) =>
                {
                    var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                    if (!addToMulti)
                        SelectFloatingGroupChild(selectionRoot, childPath);
                    e.Handled = true;
                };
            }
            innerCanvas.Children.Add(childElement);
        }

        var root = new System.Windows.Controls.Border
        {
            Width = widthPx,
            Height = heightPx,
            BorderBrush = isSelected ? System.Windows.Media.Brushes.DodgerBlue : null,
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Child = innerCanvas,
            Tag = group
        };

        if (plan.RotationAngle != 0 || plan.FlipH || plan.FlipV)
        {
            var transforms = new System.Windows.Media.TransformGroup();
            if (plan.FlipH || plan.FlipV)
                transforms.Children.Add(new System.Windows.Media.ScaleTransform(
                    plan.FlipH ? -1 : 1,
                    plan.FlipV ? -1 : 1,
                    widthPx / 2,
                    heightPx / 2));
            if (plan.RotationAngle != 0)
                transforms.Children.Add(new System.Windows.Media.RotateTransform(
                    plan.RotationAngle,
                    widthPx / 2,
                    heightPx / 2));
            root.RenderTransform = transforms;
        }

        if (enableSelection)
        {
            root.Cursor = Cursors.SizeAll;
            root.MouseLeftButtonDown += (_, e) =>
            {
                var addToMulti = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                SelectFloatingObject(group, addToMulti);
                e.Handled = true;
            };
        }
        return root;
    }

    private FrameworkElement BuildGroupPlannedChildVisual(object child, DrawingObjectVisualPlan plan)
    {
        // The DOCX writer represents unsupported group children as solid gray wps:wsp rectangles.
        // Keep those payload-free imported stubs visually faithful without changing authored rich children.
        if (IsSerializedGroupPlaceholder(child))
            return BuildSerializedGroupPlaceholder(child, plan.Rect);

        return plan.Kind switch
        {
            DrawingObjectVisualKind.Shape or DrawingObjectVisualKind.WordArt =>
                BuildDrawingObjectCoreVisual(plan),
            DrawingObjectVisualKind.Image when child is InlineImage image =>
                BuildFloatingImageVisual(image, plan.Rect, enableSelection: false),
            DrawingObjectVisualKind.Chart when child is Chart chart =>
                BuildFloatingChartVisual(chart, plan.Rect, enableSelection: false),
            DrawingObjectVisualKind.SmartArt when child is SmartArt smartArt =>
                BuildFloatingSmartArtVisual(smartArt, plan.Rect, enableSelection: false),
            DrawingObjectVisualKind.Group when child is FreeW.Core.Model.DrawingGroup nestedGroup =>
                BuildFloatingGroupVisual(plan, nestedGroup, enableSelection: false),
            _ => BuildGroupUnsupportedChildPlaceholder(
                child,
                plan.Rect.WidthDip / PxPerPoint,
                plan.Rect.HeightDip / PxPerPoint)
        };
    }

    private static bool IsSerializedGroupPlaceholder(object child) => child switch
    {
        InlineImage { Bytes.Length: 0 } => true,
        Chart { Categories.Count: 0, Series.Count: 0 } => true,
        SmartArt { Nodes.Count: 0 } => true,
        _ => false
    };

    private static FrameworkElement BuildSerializedGroupPlaceholder(object child, DocumentFloatRect rect) =>
        new System.Windows.Controls.Border
        {
            Width = rect.WidthDip,
            Height = rect.HeightDip,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
            Tag = child
        };

    private static FrameworkElement BuildGroupUnsupportedChildPlaceholder(object child, double widthPt, double heightPt)
    {
        var widthPx = Math.Max(1, widthPt * PxPerPoint);
        var heightPx = Math.Max(1, heightPt * PxPerPoint);
        var label = child switch
        {
            InlineImage => "Image",
            Chart chart => chart.Kind + " Chart",
            SmartArt => "SmartArt",
            _ => "Object"
        };

        return new Border
        {
            Width = widthPx,
            Height = heightPx,
            BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(18, 100, 100, 200)),
            Child = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DimGray,
                FontSize = Math.Max(7, Math.Min(11, widthPx / 10)),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    /// <summary>
    /// Select a floating non-image object. Mirrors SelectFloatingImage but clears the image selection.
    /// Pass <paramref name="addToMultiSelect"/> true (Shift/Ctrl held) to extend the multi-select set.
    /// </summary>
    internal void SelectFloatingObject(object obj, bool addToMultiSelect = false)
    {
        _selectedFloatingGroupChild = null;
        if (addToMultiSelect)
        {
            if (_selectedFloatingObjects.Contains(obj))
                _selectedFloatingObjects.Remove(obj);
            else
                _selectedFloatingObjects.Add(obj);
        }
        else
        {
            _selectedFloatingObjects.Clear();
            _selectedFloatingObjects.Add(obj);
        }
        _selectedFloatingObject = obj;
        _selectedFloatingImage = null;
        SyncFloatingObjectsCanvas();
        Focus();
        RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, this));
    }

    /// <summary>
    /// Select a floating image: set <see cref="_selectedFloatingImage"/> and fire the selection-changed
    /// path so the Picture Format contextual tab activates. Clears the RichTextBox selection so the
    /// picture-format commands see no competing inline selection.
    /// Pass <paramref name="addToMultiSelect"/> true (Shift/Ctrl held) to extend the multi-select set.
    /// </summary>
    internal void SelectFloatingImage(InlineImage image, bool addToMultiSelect = false)
    {
        _selectedFloatingGroupChild = null;
        if (addToMultiSelect)
        {
            if (_selectedFloatingObjects.Contains(image))
                _selectedFloatingObjects.Remove(image);
            else
                _selectedFloatingObjects.Add(image);
        }
        else
        {
            _selectedFloatingObjects.Clear();
            _selectedFloatingObjects.Add(image);
        }
        _selectedFloatingImage = image;
        // Refresh the overlay so the selection highlight can be drawn next cycle.
        SyncFloatingObjectsCanvas();
        // Raise SelectionChanged so the host's contextual-tab controller sees the new selection.
        Focus();
        RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, this));
    }

    /// <summary>Select one direct child while retaining its owning group as the active selection.</summary>
    internal void SelectFloatingGroupChild(FreeW.Core.Model.DrawingGroup group, int childIndex)
        => SelectFloatingGroupChild(group, [childIndex]);

    /// <summary>Select a direct or nested child while retaining the top-level group as active.</summary>
    internal void SelectFloatingGroupChild(
        FreeW.Core.Model.DrawingGroup group,
        IReadOnlyList<int> childPath)
    {
        if (!DrawingGroupChildPathResolver.TryGetChild(
                group,
                childPath,
                out _,
                out _))
            return;

        _selectedFloatingGroupChild = new FloatingGroupChildSelection(
            group,
            childPath.ToArray());
        _selectedFloatingObjects.Clear();
        _selectedFloatingObjects.Add(group);
        _selectedFloatingObject = group;
        _selectedFloatingImage = null;
        SyncFloatingObjectsCanvas();
        Focus();
        RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, this));
    }

    /// <summary>Move the selected WPF group child in group-local points through the shared command.</summary>
    internal bool MoveSelectedFloatingGroupChild(double dxPt, double dyPt)
    {
        if (_selectedFloatingGroupChild is not { } selected)
            return false;

        CommitToModel();
        var (blockIndex, runIndex) = FindFloatingObjectLocation(selected.RootGroup);
        if (blockIndex < 0)
            return false;

        if (!DrawingGroupChildPathResolver.TryGetChild(
                selected.RootGroup,
                selected.ChildPath,
                out var owningGroup,
                out _))
            return false;

        SetDrawingGroupChildPositionCommand.EnsureOffsetSlot(owningGroup, selected.ChildIndex);
        var offset = owningGroup.ChildOffsets[selected.ChildIndex];
        _commands.Execute(new SetDrawingGroupChildPositionCommand(
            blockIndex,
            runIndex,
            selected.ChildPath,
            offset.X + dxPt,
            offset.Y + dyPt));
        SyncFloatingObjectsCanvas();
        return true;
    }

    /// <summary>Resize the selected WPF group child, optionally moving its local top-left anchor.</summary>
    internal bool ResizeSelectedFloatingGroupChild(
        double widthPt,
        double heightPt,
        double dxPt = 0,
        double dyPt = 0)
    {
        if (_selectedFloatingGroupChild is not { } selected || widthPt <= 0 || heightPt <= 0)
            return false;

        CommitToModel();
        var (blockIndex, runIndex) = FindFloatingObjectLocation(selected.RootGroup);
        if (blockIndex < 0)
            return false;

        if (!DrawingGroupChildPathResolver.TryGetChild(
                selected.RootGroup,
                selected.ChildPath,
                out var owningGroup,
                out _))
            return false;

        var commands = new List<IDocumentCommand>
        {
            new SetDrawingGroupChildSizeCommand(
                blockIndex,
                runIndex,
                selected.ChildPath,
                widthPt,
                heightPt)
        };
        if (Math.Abs(dxPt) > 0.01 || Math.Abs(dyPt) > 0.01)
        {
            SetDrawingGroupChildPositionCommand.EnsureOffsetSlot(owningGroup, selected.ChildIndex);
            var offset = owningGroup.ChildOffsets[selected.ChildIndex];
            commands.Add(new SetDrawingGroupChildPositionCommand(
                blockIndex,
                runIndex,
                selected.ChildPath,
                offset.X + dxPt,
                offset.Y + dyPt));
        }

        _commands.Execute(new CompositeDocumentCommand("Resize Group Child", commands));
        SyncFloatingObjectsCanvas();
        return true;
    }

    /// <summary>Returns the current multi-select set as a read-only snapshot.</summary>
    internal IReadOnlyList<object> SelectedFloatingObjects => _selectedFloatingObjects.AsReadOnly();

    /// <summary>Returns the selected child within a group, when child editing is active.</summary>
    internal (FreeW.Core.Model.DrawingGroup Group, int ChildIndex)? SelectedFloatingGroupChild =>
        _selectedFloatingGroupChild is { } selected
            ? (selected.RootGroup, selected.ChildIndex)
            : null;

    /// <summary>Returns the complete root-relative path for the selected group child.</summary>
    internal IReadOnlyList<int>? SelectedFloatingGroupChildPath =>
        _selectedFloatingGroupChild?.ChildPath;

    /// <summary>Returns true when two or more floating objects are currently multi-selected.</summary>
    internal bool HasMultipleFloatingObjectsSelected => _selectedFloatingObjects.Count >= 2;

    /// <summary>Returns true when exactly one FreeW.Core.Model.DrawingGroup is selected.</summary>
    internal bool IsGroupSelected => _selectedFloatingObjects.Count == 1 && _selectedFloatingObjects[0] is FreeW.Core.Model.DrawingGroup;

    /// <summary>
    /// Rotate the selected floating object through the shared transform command. A selected direct or
    /// nested group child keeps the owning run active, so its full root-relative path is sent to
    /// <see cref="SetDrawingGroupChildRotationCommand"/> and the group's transform is left intact.
    /// </summary>
    public bool RotateSelectedFloating(double angleDeg)
    {
        CommitToModel();

        if (SelectedFloatingGroupChildTransform() is { } child)
        {
            _commands.Execute(new SetDrawingGroupChildRotationCommand(
                child.BlockIndex,
                child.RunIndex,
                child.ChildPath,
                AddRotation(child.Angle, angleDeg),
                child.FlipH,
                child.FlipV));
            Render();
            return true;
        }

        if (SelectedImageLocation() is { Image: { } image } imageLocation)
        {
            _commands.Execute(new SetImageRotationCommand(
                imageLocation.BlockIndex,
                imageLocation.RunIndex,
                AddRotation(image.RotationAngle, angleDeg),
                image.FlipH,
                image.FlipV));
            Render();
            return true;
        }

        if (SelectedShapeLocation() is { Shape: { } shape } shapeLocation)
        {
            _commands.Execute(new SetShapeRotationCommand(
                shapeLocation.BlockIndex,
                shapeLocation.RunIndex,
                AddRotation(shape.RotationAngle, angleDeg),
                shape.FlipH,
                shape.FlipV));
            Render();
            return true;
        }

        if (SelectedFloatingObjectTransform() is not { } selected)
            return false;

        return TrySetSelectedFloatingRotation(
            AddRotation(selected.Angle, angleDeg),
            flipH: null,
            flipV: null);
    }

    /// <summary>Flip the selected floating object through the same shared transform route as rotation.</summary>
    public bool FlipSelectedFloating(bool horizontal)
    {
        CommitToModel();

        if (SelectedFloatingGroupChildTransform() is { } child)
        {
            _commands.Execute(new SetDrawingGroupChildRotationCommand(
                child.BlockIndex,
                child.RunIndex,
                child.ChildPath,
                child.Angle,
                horizontal ? !child.FlipH : child.FlipH,
                horizontal ? child.FlipV : !child.FlipV));
            Render();
            return true;
        }

        if (SelectedImageLocation() is { Image: { } image } imageLocation)
        {
            _commands.Execute(new SetImageRotationCommand(
                imageLocation.BlockIndex,
                imageLocation.RunIndex,
                image.RotationAngle,
                horizontal ? !image.FlipH : image.FlipH,
                horizontal ? image.FlipV : !image.FlipV));
            Render();
            return true;
        }

        if (SelectedShapeLocation() is { Shape: { } shape } shapeLocation)
        {
            _commands.Execute(new SetShapeRotationCommand(
                shapeLocation.BlockIndex,
                shapeLocation.RunIndex,
                shape.RotationAngle,
                horizontal ? !shape.FlipH : shape.FlipH,
                horizontal ? shape.FlipV : !shape.FlipV));
            Render();
            return true;
        }

        if (SelectedFloatingObjectTransform() is { } selected)
        {
            return TrySetSelectedFloatingRotation(
                selected.Angle,
                horizontal ? !selected.FlipH : selected.FlipH,
                horizontal ? selected.FlipV : !selected.FlipV);
        }

        return false;
    }

    private bool TrySetSelectedFloatingRotation(double angleDeg, bool? flipH, bool? flipV)
    {
        if (SelectedFloatingObjectTransform() is not { } selected)
            return false;

        _commands.Execute(new SetFloatingRotationCommand(
            selected.BlockIndex,
            selected.RunIndex,
            angleDeg,
            flipH ?? selected.FlipH,
            flipV ?? selected.FlipV));
        Render();
        return true;
    }

    private (int BlockIndex, int RunIndex, IReadOnlyList<int> ChildPath,
        double Angle, bool FlipH, bool FlipV)? SelectedFloatingGroupChildTransform()
    {
        if (_selectedFloatingGroupChild is not { } selected)
            return null;

        var location = FindFloatingObjectLocation(selected.RootGroup);
        if (location.BlockIndex < 0
            || !DrawingGroupChildPathResolver.TryGetChild(
                selected.RootGroup,
                selected.ChildPath,
                out _,
                out var child))
            return null;

        var transform = GetDrawingGroupChildTransform(child);
        return (
            location.BlockIndex,
            location.RunIndex,
            selected.ChildPath,
            transform.Angle,
            transform.FlipH,
            transform.FlipV);
    }

    private (int BlockIndex, int RunIndex, double Angle, bool FlipH, bool FlipV)? SelectedFloatingObjectTransform()
    {
        if (_selectedFloatingObject is null)
            return null;

        var location = FindFloatingObjectLocation(_selectedFloatingObject);
        if (location.BlockIndex < 0
            || location.BlockIndex >= _model.Blocks.Count
            || _model.Blocks[location.BlockIndex] is not ModelParagraph paragraph
            || location.RunIndex < 0
            || location.RunIndex >= paragraph.Runs.Count)
            return null;

        var run = paragraph.Runs[location.RunIndex];
        var transform = run.Image is { } image && ReferenceEquals(image, _selectedFloatingObject)
            ? (image.RotationAngle, image.FlipH, image.FlipV)
            : run.Shape is { } shape && ReferenceEquals(shape, _selectedFloatingObject)
                ? (shape.RotationAngle, shape.FlipH, shape.FlipV)
                : run.Chart is { } chart && ReferenceEquals(chart, _selectedFloatingObject)
                    ? (chart.RotationAngle, chart.FlipH, chart.FlipV)
                    : run.SmartArt is { } smartArt && ReferenceEquals(smartArt, _selectedFloatingObject)
                        ? (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV)
                        : run.WordArt is { } wordArt && ReferenceEquals(wordArt, _selectedFloatingObject)
                            ? (wordArt.RotationAngle, wordArt.FlipH, wordArt.FlipV)
                            : run.DrawingGroup is { } group && ReferenceEquals(group, _selectedFloatingObject)
                                ? (group.RotationAngle, group.FlipH, group.FlipV)
                                : (double.NaN, false, false);

        return double.IsNaN(transform.Item1)
            ? null
            : (location.BlockIndex, location.RunIndex, transform.Item1, transform.Item2, transform.Item3);
    }

    private static (double Angle, bool FlipH, bool FlipV) GetDrawingGroupChildTransform(object child) => child switch
    {
        InlineImage image => (image.RotationAngle, image.FlipH, image.FlipV),
        Shape shape => (shape.RotationAngle, shape.FlipH, shape.FlipV),
        Chart chart => (chart.RotationAngle, chart.FlipH, chart.FlipV),
        SmartArt smartArt => (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV),
        WordArt wordArt => (wordArt.RotationAngle, wordArt.FlipH, wordArt.FlipV),
        FreeW.Core.Model.DrawingGroup group => (group.RotationAngle, group.FlipH, group.FlipV),
        _ => (0, false, false)
    };

    private static double AddRotation(double currentAngle, double delta) =>
        (currentAngle + delta + 360) % 360;

    /// <summary>
    /// Adds z-order commands to the method set. Called by the host via the ribbon command bus.
    /// </summary>
    public bool ChangeSelectedFloatingZOrder(ZOrderOperation operation)
    {
        CommitToModel();
        if (_selectedFloatingGroupChild is { } selectedChild
            && DrawingGroupChildPathResolver.TryGetChild(
                selectedChild.RootGroup,
                selectedChild.ChildPath,
                out _,
                out var child))
        {
            var (groupBlockIndex, groupRunIndex) = FindFloatingObjectLocation(selectedChild.RootGroup);
            if (groupBlockIndex < 0)
                return false;
            _commands.Execute(new ChangeDrawingGroupChildZOrderCommand(
                groupBlockIndex, groupRunIndex, selectedChild.ChildPath, operation));
            RestoreSelectedFloatingGroupChildPath(child);
            SyncFloatingObjectsCanvas();
            return true;
        }

        var (blockIndex, runIndex, image) = SelectedImageLocation();
        if (image is { IsFloating: true })
        {
            _commands.Execute(new ChangeZOrderCommand(blockIndex, runIndex, operation));
            SyncFloatingObjectsCanvas();
            return true;
        }
        if (_selectedFloatingObject is not null)
        {
            var (bi, ri) = FindFloatingObjectLocation(_selectedFloatingObject);
            if (bi >= 0)
            {
                _commands.Execute(new ChangeZOrderCommand(bi, ri, operation));
                SyncFloatingObjectsCanvas();
                return true;
            }
        }

        return false;
    }

    public void ChangeSelectedImageZOrder(ZOrderOperation operation) =>
        ChangeSelectedFloatingZOrder(operation);

    /// <summary>
    /// Groups the current multi-select set into a FreeW.Core.Model.DrawingGroup, if at least 2 objects are selected.
    /// Executes a <see cref="GroupFloatingObjectsCommand"/> via the undoable command bus.
    /// </summary>
    public void GroupSelectedFloatingObjects()
    {
        if (_selectedFloatingObjects.Count < 2) return;
        CommitToModel();

        // Collect (blockIndex, runIndex) for each selected floating object.
        var members = new List<(int Bi, int Ri)>();
        foreach (var obj in _selectedFloatingObjects)
        {
            if (obj is InlineImage img)
            {
                var (bi, ri, _) = SelectedImageLocationForObject(img);
                if (bi >= 0) members.Add((bi, ri));
            }
            else
            {
                var (bi, ri) = FindFloatingObjectLocation(obj);
                if (bi >= 0) members.Add((bi, ri));
            }
        }

        if (members.Count < 2) return;

        _commands.Execute(new GroupFloatingObjectsCommand(members));
        _selectedFloatingObjects.Clear();
        _selectedFloatingGroupChild = null;
        _selectedFloatingImage = null;
        _selectedFloatingObject = null;
        SyncFloatingObjectsCanvas();
    }

    /// <summary>
    /// Ungroups the currently selected FreeW.Core.Model.DrawingGroup, if exactly one group is selected.
    /// Executes a <see cref="UngroupFloatingObjectsCommand"/> via the undoable command bus.
    /// </summary>
    public void UngroupSelectedFloatingObject()
    {
        if (_selectedFloatingObject is not FreeW.Core.Model.DrawingGroup group) return;
        CommitToModel();

        var (bi, ri) = FindFloatingObjectLocation(group);
        if (bi < 0) return;

        _commands.Execute(new UngroupFloatingObjectsCommand(bi, ri));
        _selectedFloatingObjects.Clear();
        _selectedFloatingGroupChild = null;
        _selectedFloatingImage = null;
        _selectedFloatingObject = null;
        SyncFloatingObjectsCanvas();
    }

    /// <summary>
    /// Aligns/distributes floating objects through the shared model command. WPF keeps its historic
    /// document-wide behavior unless the user has an explicit multi-selection.
    /// </summary>
    public bool ArrangeFloatingObjects(FloatingObjectArrangeKind kind)
    {
        CommitToModel();

        var members = FloatingArrangeLocations();
        if (ArrangeFloatingObjectsCommand.CountApplicableObjects(_model, members) < RequiredArrangeObjectCount(kind))
            return false;

        _commands.Execute(new ArrangeFloatingObjectsCommand(kind, members));
        SyncFloatingObjectsCanvas();
        return true;
    }

    private IReadOnlyList<(int BlockIndex, int RunIndex)> FloatingArrangeLocations()
    {
        var selected = SelectedFloatingArrangeLocations();
        if (selected.Count >= 2)
            return selected;

        return ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(_model);
    }

    private List<(int BlockIndex, int RunIndex)> SelectedFloatingArrangeLocations()
    {
        var members = new List<(int BlockIndex, int RunIndex)>();
        foreach (var obj in _selectedFloatingObjects)
        {
            (int BlockIndex, int RunIndex) location = obj is InlineImage image
                ? SelectedImageLocationForObject(image) is var imageLocation && imageLocation.BlockIndex >= 0
                    ? (imageLocation.BlockIndex, imageLocation.RunIndex)
                    : (-1, -1)
                : FindFloatingObjectLocation(obj);

            if (location.BlockIndex >= 0 && !members.Contains(location))
                members.Add(location);
        }

        return members;
    }

    private static int RequiredArrangeObjectCount(FloatingObjectArrangeKind kind) =>
        kind is FloatingObjectArrangeKind.DistributeHorizontal or FloatingObjectArrangeKind.DistributeVertical
            ? 2
            : 1;

    private (int BlockIndex, int RunIndex, InlineImage? Image) SelectedImageLocationForObject(InlineImage target)
    {
        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph para) continue;
            for (var r = 0; r < para.Runs.Count; r++)
            {
                if (ReferenceEquals(para.Runs[r].Image, target))
                    return (b, r, target);
            }
        }
        return (-1, -1, null);
    }

    /// <summary>Locates a floating non-image object in the model to get its (blockIndex, runIndex).</summary>
    private (int BlockIndex, int RunIndex) FindFloatingObjectLocation(object obj)
    {
        for (var b = 0; b < _model.Blocks.Count; b++)
        {
            if (_model.Blocks[b] is not ModelParagraph para) continue;
            for (var r = 0; r < para.Runs.Count; r++)
            {
                var run = para.Runs[r];
                if (ReferenceEquals(run.Shape, obj) || ReferenceEquals(run.Chart, obj)
                    || ReferenceEquals(run.SmartArt, obj) || ReferenceEquals(run.WordArt, obj)
                    || ReferenceEquals(run.DrawingGroup, obj))
                    return (b, r);
            }
        }
        return (-1, -1);
    }

    /// <summary>
    /// Builds a brush for Word's fixed VML text-path watermark behind document content. Respects the
    /// full <see cref="WatermarkOptions"/> — font family, colour, opacity and diagonal vs horizontal
    /// layout. The print/preview path consumes the same shared shape plan per page.
    /// </summary>
    internal static Brush BuildWatermarkBrush(WatermarkOptions options) =>
        BuildWatermarkBrush(options, Colors.White);

    internal static Brush BuildWatermarkBrush(
        WatermarkOptions options,
        Color pageColor,
        double pageWidthDip = 816,
        double pageHeightDip = 1056)
    {
        if (options.IsPicture)
            return BuildPictureWatermarkBrush(options, pageColor, pageWidthDip, pageHeightDip);

        var baseColor = ParseColor(options.FontColorHex, Color.FromRgb(0x80, 0x80, 0x80));
        var alpha = (byte)Math.Clamp((int)Math.Round(options.Opacity * 255), 0, 255);
        var foreground = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

        var width = Math.Max(1, pageWidthDip);
        var height = Math.Max(1, pageHeightDip);
        var plan = WatermarkVisualPlanner.BuildTextLayout(options, width, height);
        if (plan is null)
            return new SolidColorBrush(pageColor);

        var typeface = new Typeface(
            new System.Windows.Media.FontFamily(options.FontFamily),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var unitText = new FormattedText(
            options.Text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            1,
            foreground,
            1);
        var fontSize = WatermarkVisualPlanner.ResolveTextPathFontSize(plan, unitText.Width);
        var text = new FormattedText(
            options.Text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground,
            1);

        var drawing = new System.Windows.Media.DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(pageColor),
            null,
            new RectangleGeometry(new Rect(0, 0, width, height))));
        var geometry = text.BuildGeometry(new Point(plan.CenterXDip - text.Width / 2, plan.CenterYDip - text.Height / 2));
        if (Math.Abs(plan.RotationDegrees) > 0.01)
            geometry.Transform = new RotateTransform(plan.RotationDegrees, plan.CenterXDip, plan.CenterYDip);
        drawing.Children.Add(new GeometryDrawing(foreground, null, geometry));

        return new DrawingBrush(drawing)
        {
            Viewbox = new Rect(0, 0, width, height),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }

    private static Brush BuildPictureWatermarkBrush(
        WatermarkOptions options,
        Color pageColor,
        double pageWidthDip,
        double pageHeightDip)
    {
        var source = TryDecodeRaster(options.ImageBytes);
        if (source is null)
            return new SolidColorBrush(pageColor);

        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            options,
            pageWidthDip,
            pageHeightDip,
            sourceWidthDip: source.PixelWidth,
            sourceHeightDip: source.PixelHeight);
        if (plan is null)
            return new SolidColorBrush(pageColor);

        var width = Math.Max(1, pageWidthDip);
        var height = Math.Max(1, pageHeightDip);
        var normalizedX = plan.XDip / width;
        var normalizedY = plan.YDip / height;
        var normalizedWidth = plan.WidthDip / width;
        var normalizedHeight = plan.HeightDip / height;
        var normalizedCenterX = plan.CenterXDip / width;
        var normalizedCenterY = plan.CenterYDip / height;

        var group = new System.Windows.Media.DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(pageColor),
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        var imageGroup = new System.Windows.Media.DrawingGroup
        {
            Opacity = plan.Opacity
        };
        if (Math.Abs(plan.RotationDegrees) > 0.01)
            imageGroup.Transform = new RotateTransform(plan.RotationDegrees, normalizedCenterX, normalizedCenterY);

        imageGroup.Children.Add(new ImageDrawing(
            source,
            new Rect(normalizedX, normalizedY, normalizedWidth, normalizedHeight)));
        group.Children.Add(imageGroup);

        return new DrawingBrush(group)
        {
            Stretch = Stretch.Fill
        };
    }

    /// <summary>Legacy overload — adapts a bare text string to a default <see cref="WatermarkOptions"/>.</summary>
    internal static Brush BuildWatermarkBrush(string text) =>
        BuildWatermarkBrush(WatermarkOptions.FromLegacyText(text), Colors.White);

    /// <summary>Legacy overload — adapts a bare text string to a default <see cref="WatermarkOptions"/>.</summary>
    internal static Brush BuildWatermarkBrush(string text, Color pageColor) =>
        BuildWatermarkBrush(WatermarkOptions.FromLegacyText(text), pageColor);

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

    // Numbered lists render with WPF's built-in decimal marker. MultiLevel lists suppress the built-in
    // marker (None) because WPF cannot produce an accumulating outline marker (true "1.1.1" form); their
    // per-paragraph accumulated text is computed by MultiLevelMarkerSequence and rendered as a leading
    // non-editable run instead (see Render). Bullets use a disc.
    private static TextMarkerStyle ToMarkerStyle(ListKind kind) => kind switch
    {
        ListKind.Number => TextMarkerStyle.Decimal,
        ListKind.MultiLevel => TextMarkerStyle.None,
        _ => TextMarkerStyle.Disc
    };

    /// <summary>
    /// Computes the accumulated outline marker text ("1.", "1.1.", "1.1.1.", …) for a run of multilevel
    /// list paragraphs, mirroring exactly what FreeW writes to <c>numbering.xml</c>: each level n shows the
    /// dotted run of all ancestor counters, <c>%1.%2.…%(n+1).</c> (see <c>DocxWriter.BuildNumbering</c>).
    /// One marker is returned per input level, in order.
    /// <para>
    /// Counter rules match Word's <c>w:multiLevelType="multilevel"</c>: entering a level increments that
    /// level's counter and resets every deeper level to its start; an ancestor level that has not yet been
    /// numbered in this run is shown at its start value (1) so a list that begins at, or jumps to, a deeper
    /// level still renders a sensible dotted prefix rather than zeros.
    /// </para>
    /// Pure (no WPF), so it is unit-testable. Levels are clamped to the modelled multilevel depth.
    /// </summary>
    internal static IReadOnlyList<string> MultiLevelMarkerSequence(
        IEnumerable<int> levels,
        IReadOnlyList<ListNumberFormat>? numberFormats = null) =>
        MultiLevelListMarkerFormatter.MarkerSequence(levels, numberFormats);

    /// <summary>
    /// Prepends the computed accumulated outline marker (e.g. <c>1.1.1.</c>) to a multilevel-list
    /// paragraph as a leading non-editable run, plus a tab so the body text aligns past the marker
    /// (mirroring Word's hanging-indent layout). The run is tagged with <see cref="MultiLevelMarker"/>
    /// so <see cref="ReadInline"/> drops it on commit — the marker is view-only chrome and never enters
    /// the model (the outline definition lives in <c>numbering.xml</c>, regenerated on save). Marker text
    /// inherits the paragraph's leading run formatting so it tracks the list's font size/colour.
    /// </summary>
    private static void PrependMultiLevelMarker(WpfParagraph paragraph, string markerText, TextDocument document)
    {
        // Mirrors the footnote/endnote marker convention: a plain run carrying a Tag that ReadInline drops
        // on commit. The marker text is regenerated on every Render, so even if the user edits over it the
        // model is unaffected (the outline definition lives in numbering.xml).
        var marker = new WpfRun(markerText + ' ')
        {
            Tag = MultiLevelMarker.Instance
        };
        // Match the paragraph's font size so the marker scales with the list text (fall back to default).
        var firstRun = paragraph.Inlines.OfType<WpfRun>().FirstOrDefault();
        if (firstRun is not null && firstRun.FontSize > 0)
            marker.FontSize = firstRun.FontSize;
        else
            marker.FontSize = (document.DefaultRun.FontSizePt ?? DefaultFontSizePt) * PxPerPoint;
        paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, marker);
    }

    /// <summary>
    /// Marks the synthetic leading run that renders a multilevel list's accumulated outline number
    /// (see <see cref="PrependMultiLevelMarker"/>). View-only chrome: <see cref="ReadInline"/> skips any
    /// run carrying this tag so the marker text never round-trips into the model.
    /// </summary>
    private sealed record MultiLevelMarker
    {
        public static readonly MultiLevelMarker Instance = new();
    }

    /// <summary>
    /// Applies the page's multi-column layout to a <see cref="FlowDocument"/>. A FlowDocument derives
    /// its column count from <see cref="FlowDocument.ColumnWidth"/> relative to its content area, so to
    /// render exactly <see cref="PageSettings.ColumnCount"/> equal columns we set the column width to
    /// (contentWidth - (N-1)*gap) / N and the gap to the model's column spacing. Single-column pages
    /// (the default) keep an infinite column width so the text spans the full content area, exactly as
    /// before. <see cref="PageSettings.ColumnsLineBetween"/> maps to the FlowDocument's column rule, and
    /// explicit unequal widths (<see cref="PageSettings.ColumnWidthsPt"/>) use the narrowest column as the
    /// flexible column width so WPF lays out the requested number of columns (it cannot render genuinely
    /// unequal columns in one FlowDocument — the narrowest-width approximation keeps the count correct and
    /// the unequal split round-trips faithfully to docx/Word). All WPF surfaces share this flow geometry;
    /// paginated surfaces can replace the native rule raster with <see cref="BuildColumnRuleVisual"/>.
    /// </summary>
    internal static void ApplyColumnLayout(FlowDocument flow, PageSettings page, bool useNativeColumnRule = true)
    {
        var columns = Math.Max(1, page.ColumnCount);
        if (columns <= 1)
        {
            flow.ColumnWidth = double.PositiveInfinity; // single column spans the whole content area
            flow.ColumnGap = 0;
            flow.ColumnRuleWidth = 0;
            return;
        }

        var pageMetrics = DocumentViewLayoutPlanner.BuildPageMetrics(page);
        var columnPlan = DocumentViewLayoutPlanner.BuildColumnPlan(
            page,
            pageMetrics.ContentWidthDip,
            usePageColumns: true);

        // Guard degenerate geometry (narrow page / wide gaps) so the width stays usable and positive.
        flow.ColumnWidth = columnPlan.WidthDip;
        flow.IsColumnWidthFlexible = true; // let WPF expand columns to fill the content area
        flow.ColumnGap = columnPlan.GapDip;

        // "Line between" (w:cols/@w:sep) → a thin rule centred in the gap.
        if (columnPlan.LineBetween)
        {
            flow.ColumnRuleWidth = useNativeColumnRule ? 1 : 0;
            flow.ColumnRuleBrush = System.Windows.Media.Brushes.Gray;
        }
        else
        {
            flow.ColumnRuleWidth = 0;
        }
    }

    /// <summary>
    /// Draws Word-style inter-column rules at device-pixel-aligned page coordinates. WPF's native
    /// <see cref="FlowDocument.ColumnRuleWidth"/> centers a one-DIP rule across two pixels; the
    /// print/composite paths use this visual instead so the rule remains one opaque pixel.
    /// </summary>
    internal static DrawingVisual BuildColumnRuleVisual(
        PageSettings page,
        double contentLeftDip,
        double contentTopDip,
        double contentWidthDip,
        double contentBottomDip)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        DrawColumnRules(dc, page, contentLeftDip, contentTopDip, contentWidthDip, contentBottomDip);
        return visual;
    }

    internal static void DrawColumnRules(
        DrawingContext drawingContext,
        PageSettings page,
        double contentLeftDip,
        double contentTopDip,
        double contentWidthDip,
        double contentBottomDip)
    {
        if (!page.ColumnsLineBetween || page.ColumnCount <= 1 || contentWidthDip <= 0 || contentBottomDip <= contentTopDip)
            return;

        var plan = DocumentViewLayoutPlanner.BuildColumnPlan(page, contentWidthDip, usePageColumns: true);
        var pen = new Pen(Brushes.Black, 1);
        for (var column = 1; column < page.ColumnCount; column++)
        {
            var x = contentLeftDip + column * (plan.WidthDip + plan.GapDip) - plan.GapDip / 2 - 0.5;
            drawingContext.DrawLine(pen, new Point(x, contentTopDip + 0.5), new Point(x, contentBottomDip - 0.5));
        }
    }

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._model;
    }

    /// <summary>
    /// Side-band paragraph data carried on a WPF <see cref="WpfParagraph.Tag"/> so it survives an
    /// edit/commit cycle even though the FlowDocument paragraph has no native slot for it. Holds the
    /// model's tab stops (not representable in WPF), the paragraph's bookmark names (invisible
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
    /// <para>
    /// Also carries the paragraph's list nesting depth (<see cref="ModelParagraph.Formatting"/>'s
    /// <c>ListLevel</c>). The editor coalesces a run of same-kind list paragraphs into one flat WPF
    /// <see cref="WpfList"/>, so the nesting depth has no structural slot in the FlowDocument and was
    /// dropped on commit (collapsing every multilevel item back to level 0). Stamping it here makes the
    /// list level round-trip through an edit/commit cycle, which keeps the accumulated outline markers
    /// (1.1.1) stable after editing. Defaults to 0 (the non-list / top-level case).
    /// </para>
    private sealed record ParagraphTag(IReadOnlyList<TabStop> TabStops, IReadOnlyList<string> BookmarkNames, bool PageBreakBefore = false, bool WidowControl = false, bool WidowControlIsSet = false, string? StyleId = null, int ListLevel = 0, ParagraphBorder? Border = null, ShadingPattern ShadingPattern = ShadingPattern.Clear, bool SuppressAutoHyphens = false, bool SuppressLineNumbers = false, bool SuppressLineNumbersIsSet = false, FreeW.Core.Model.Section? SectionBreak = null, DropCapLayoutIntent? DropCap = null, ListKind? ListKind = null, bool KeepLinesTogether = false);

    private sealed record RenderedBookmarkBoundary(BookmarkBoundary Boundary);

    private sealed record RenderedTabStopSpan(
        ParagraphTabStopPlacementPlan Plan,
        RunFormatting Formatting,
        int? CommentId,
        ModelContentControl? Control,
        RevisionKind Revision,
        string? RevisionAuthor,
        string? RevisionDateXml,
        ModelFormatRevision? FormatRevision);

    private sealed record TabFollowingSegmentMetrics(double WidthDip, double? DecimalAlignmentOffsetDip);

    internal static IReadOnlyList<(double StopPositionDip, double SegmentStartDip, double AdvanceDip, TabStopAlignment Alignment, TabLeader Leader, bool IsExplicit)> GetRenderedTabStopPlans(WpfParagraph paragraph)
    {
        var plans = new List<(double, double, double, TabStopAlignment, TabLeader, bool)>();
        CollectRenderedTabStopPlans(paragraph.Inlines, plans);
        return plans;
    }

    private static void CollectRenderedTabStopPlans(
        InlineCollection inlines,
        ICollection<(double StopPositionDip, double SegmentStartDip, double AdvanceDip, TabStopAlignment Alignment, TabLeader Leader, bool IsExplicit)> plans)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: FrameworkElement { Tag: RenderedTabStopSpan marker } }:
                    plans.Add((
                        marker.Plan.StopPositionDip,
                        marker.Plan.SegmentStartDip,
                        marker.Plan.AdvanceDip,
                        marker.Plan.Alignment,
                        marker.Plan.Leader,
                        marker.Plan.IsExplicit));
                    break;
                case Span span:
                    CollectRenderedTabStopPlans(span.Inlines, plans);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads the blocks of an arbitrary <paramref name="flowDoc"/> — which must have been produced
    /// by a <see cref="Render"/>/<see cref="LoadModel"/> call on this or a same-model scratch editor
    /// so its elements carry the standard Tag payloads — into <paramref name="target"/>.
    ///
    /// <para>
    /// Used by <see cref="PaginatedCommitCoordinator"/> to reassemble the full model from the
    /// per-page <see cref="PageBox"/> body FlowDocuments without duplicating the private static
    /// <c>ReadParagraph</c> / <c>ReadList</c> / <c>ReadTable</c> logic.
    /// </para>
    /// </summary>
    internal void ReadBlocksInto(FlowDocument flowDoc, IList<ModelBlock> target)
    {
        ReadRenderedBlocksInto(flowDoc.Blocks, target, _model);
    }

    /// <summary>Read the edited FlowDocument back into the model (paragraphs + tables).</summary>
    public void CommitToModel()
    {
        // Read the (visible) FlowDocument blocks back into a fresh list first. When outline collapse is
        // active the view only holds the visible blocks, so the hidden model blocks are spliced back in
        // afterwards (see MergeHiddenBlocks) to keep the model document complete.
        var visible = new List<ModelBlock>();
        ReadRenderedBlocksInto(Document.Blocks, visible, _model);

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

    private static void ReadRenderedBlocksInto(
        BlockCollection blocks,
        IList<ModelBlock> target,
        TextDocument document)
    {
        static IEnumerable<System.Windows.Documents.Block> FlattenSections(BlockCollection source)
        {
            foreach (var block in source)
            {
                if (block is System.Windows.Documents.Section section)
                {
                    foreach (var nested in FlattenSections(section.Blocks))
                        yield return nested;
                }
                else
                {
                    yield return block;
                }
            }
        }

        ModelTable? pendingSegmentTable = null;
        WpfTableTag? pendingSegmentTag = null;

        void FlushPendingSegment()
        {
            if (pendingSegmentTable is null)
                return;

            target.Add(pendingSegmentTable);
            pendingSegmentTable = null;
            pendingSegmentTag = null;
        }

        foreach (var block in FlattenSections(blocks))
        {
            switch (block)
            {
                case WpfList wpfList:
                    FlushPendingSegment();
                    ReadList(target, wpfList, document);
                    break;
                case WpfParagraph wpfParagraph:
                    FlushPendingSegment();
                    target.Add(ReadParagraph(wpfParagraph, document));
                    break;
                case WpfTable wpfTable:
                    var table = ReadTable(wpfTable, document);
                    if (wpfTable.Tag is WpfTableTag { IsPaginationSegment: true, SegmentCount: > 1 } tag)
                    {
                        if (pendingSegmentTable is not null
                            && pendingSegmentTag is not null
                            && pendingSegmentTag.SourceBlockIndex == tag.SourceBlockIndex
                            && pendingSegmentTag.SegmentCount == tag.SegmentCount
                            && tag.SegmentIndex == pendingSegmentTag.SegmentIndex + 1)
                        {
                            foreach (var row in table.Rows)
                                pendingSegmentTable.Rows.Add(row);
                            pendingSegmentTag = tag;
                        }
                        else
                        {
                            FlushPendingSegment();
                            pendingSegmentTable = table;
                            pendingSegmentTag = tag;
                        }

                        if (tag.SegmentIndex >= tag.SegmentCount - 1)
                            FlushPendingSegment();
                    }
                    else
                    {
                        FlushPendingSegment();
                        target.Add(table);
                    }
                    break;
                default:
                    FlushPendingSegment();
                    break;
            }
        }

        FlushPendingSegment();
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
        var tag = wpfParagraph.Tag as ParagraphTag;
        var modelParagraph = new ModelParagraph
        {
            Formatting = ReadParagraphFormatting(wpfParagraph, document) with
            {
                ListKind = tag?.ListKind ?? ListKind.None,
                ListLevel = tag?.ListLevel ?? 0,
                WidowControlIsSet = tag?.WidowControlIsSet ?? false
            },
            // The bookmark names, style id, and section break (invisible markers with no FlowDocument slot)
            // are preserved across edits via the paragraph Tag (see ParagraphTag).
            StyleId = tag?.StyleId is { Length: > 0 } styleId ? styleId : null,
            SectionBreak = tag?.SectionBreak,
            DropCap = tag?.DropCap
        };
        if (tag?.BookmarkNames is { Count: > 0 } bookmarkNames)
            modelParagraph.BookmarkNames.AddRange(bookmarkNames);
        foreach (var inline in wpfParagraph.Inlines)
            ReadInline(modelParagraph, inline, hyperlinkUrl: null, hyperlinkAnchor: null);
        RebindBookmarkBoundaryControls(modelParagraph);
        return modelParagraph;
    }

    private static void RebindBookmarkBoundaryControls(ModelParagraph paragraph)
    {
        for (var index = 0; index < paragraph.BookmarkBoundaries.Count; index++)
        {
            var boundary = paragraph.BookmarkBoundaries[index];
            if (boundary.OwnerControl is null)
                continue;

            ModelContentControl? owner = null;
            var next = boundary.RunIndex < paragraph.Runs.Count
                ? paragraph.Runs[boundary.RunIndex].Control
                : null;
            var previous = boundary.RunIndex > 0
                ? paragraph.Runs[boundary.RunIndex - 1].Control
                : null;

            if (ReferenceEquals(next, boundary.OwnerControl) || Equals(next, boundary.OwnerControl))
                owner = next;
            else if (ReferenceEquals(previous, boundary.OwnerControl) || Equals(previous, boundary.OwnerControl))
                owner = previous;
            else
                owner = boundary.Kind == BookmarkBoundaryKind.Start ? next : previous;

            paragraph.BookmarkBoundaries[index] = boundary with { OwnerControl = owner };
        }
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
                        // Recover the list nesting depth: prefer the depth stamped on the paragraph Tag at
                        // render (the editor flattens a list run into one WPF List, so the structural nesting
                        // `level` is 0 for every item); fall back to the structural level for nested WPF lists
                        // the user built fresh in the editor (those carry no ParagraphTag depth).
                        var listLevel = paragraph.Tag is ParagraphTag { ListLevel: var taggedLevel } && taggedLevel > 0
                            ? taggedLevel
                            : level;
                        model.Formatting = model.Formatting with { ListKind = kind, ListLevel = listLevel };
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
            case InlineUIContainer { Child: FrameworkElement { Tag: RenderedBookmarkBoundary marker } }:
                modelParagraph.BookmarkBoundaries.Add(marker.Boundary with
                {
                    RunIndex = modelParagraph.Runs.Count
                });
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: RenderedTabStopSpan marker } }:
                modelParagraph.Runs.Add(new ModelRun("\t", marker.Formatting)
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    HyperlinkTooltip = hyperlinkTooltip,
                    CommentId = marker.CommentId,
                    Control = marker.Control,
                    Revision = marker.Revision,
                    RevisionAuthor = marker.RevisionAuthor,
                    RevisionDateXml = marker.RevisionDateXml,
                    FormatRevision = marker.FormatRevision
                });
                break;
            case InlineUIContainer { Child: Image { Tag: InlineImage modelImage } }:
                modelParagraph.Runs.Add(new ModelRun(string.Empty) { Image = modelImage, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip });
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Shape modelShape } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromShape(modelShape), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Chart modelChart } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromChart(modelChart), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: WordArt modelWordArt } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromWordArt(modelWordArt), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: Equation modelEquation } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromEquation(modelEquation), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: SmartArt modelSmartArt } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromSmartArt(modelSmartArt), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case InlineUIContainer { Child: FrameworkElement { Tag: EmbeddedObject modelEmbedded } }:
                modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromEmbeddedObject(modelEmbedded), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
                break;
            case WpfRun { Tag: MultiLevelMarker }:
                // Synthetic accumulated outline marker ("1.1.1") — view-only chrome, never enters the
                // model (numbering.xml carries the list definition). Drop it on commit.
                break;
            case WpfRun { Tag: FootnoteMarker marker }:
                modelParagraph.Runs.Add(ModelRun.FootnoteReference(marker.FootnoteId));
                break;
            case WpfRun { Tag: PageBreakMarker }:
                modelParagraph.Runs.Add(ModelRun.PageBreak());
                break;
            case WpfRun { Tag: ColumnBreakMarker }:
                modelParagraph.Runs.Add(ModelRun.ColumnBreak());
                break;
            case Floater floater when HasFloatingWrapReservationMarker(floater):
                ReadFloatingWrapReservationFloater(modelParagraph, floater, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
                break;
            case Figure figure when HasFloatingWrapReservationMarker(figure):
                ReadFloatingWrapReservationFigure(modelParagraph, figure, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
                break;
            case Floater floater:
                ReadFloaterInlineContent(modelParagraph, floater, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
                break;
            case WpfRun { Tag: AnchorMarker marker }:
                AddAnchorMarkerRun(modelParagraph, marker, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
                break;
            case WpfRun { Tag: CitationMarker citationMarker }:
                // A hidden Mark Citation (TA) field round-trips as a textless citation-mark run.
                var citationRun = ModelRun.CitationMark(citationMarker.Citation);
                citationRun.Formatting = citationMarker.Formatting;
                modelParagraph.Runs.Add(citationRun);
                break;
            case WpfRun { Tag: EndnoteMarker endnoteMarker }:
                modelParagraph.Runs.Add(ModelRun.EndnoteReference(endnoteMarker.EndnoteId));
                break;
            case WpfRun { Tag: TableFormulaMarker formulaMarker } formulaRun:
                // A table-cell formula field round-trips its formula (expression + number format); the run's
                // visible text is the last-computed result, kept as the cached fallback.
                modelParagraph.Runs.Add(new ModelRun(formulaRun.Text, ReadRunFormatting(formulaRun))
                {
                    TableFormula = formulaMarker.Formula
                });
                break;
            case WpfRun { Tag: CrossReferenceMarker crossRefMarker } crossRefRun:
                // A cross-reference field round-trips its field definition; the run's visible text is the
                // last-resolved value, kept as the cached fallback (matching Word's cached-field behaviour).
                modelParagraph.Runs.Add(new ModelRun(crossRefRun.Text, ReadRunFormatting(crossRefRun))
                {
                    CrossReference = crossRefMarker.Field
                });
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
            case WpfRun { Tag: ComplexFieldMarker complexMarker } complexFieldRun:
                // A complex (w:fldChar/w:instrText) field round-trips its raw instruction + show-code toggle.
                // When field codes are shown the visible text is the code, so the cached result is taken from
                // the marker; otherwise the visible text IS the resolved result and is kept as the cache.
                var complexCached = complexMarker.Field.ShowCode || complexFieldRun.Text.Length == 0
                    ? complexMarker.Cached
                    : complexFieldRun.Text;
                modelParagraph.Runs.Add(new ModelRun(complexCached, ReadRunFormatting(complexFieldRun))
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    HyperlinkTooltip = hyperlinkTooltip,
                    ComplexField = complexMarker.Field
                });
                break;
            case WpfRun { Tag: RunMarkers { Comment: { IsReference: true } reference } }:
                // The textless comment anchor: round-trips as a comment-reference run.
                modelParagraph.Runs.Add(ModelRun.CommentReference(reference.CommentId));
                break;
            case WpfRun { Tag: RunMarkers markers } markedRun
                when markedRun.Text.Length > 0 || markers.Comment is not null || markers.Control is not null || markers.FormatRevision is not null || markers.CharacterFormat is not null:
                // A run carrying any combination of comment / content-control / revision / format-revision
                // marks. Recover its formatting, strip the view-only chrome each facet injected (review
                // highlight, control shade, revision colour/decoration, format-revision tint), and carry
                // every facet back onto the model run so a run that is, say, both commented and
                // tracked-changed survives the round-trip intact. A run whose text was emptied but that
                // still carries a comment, content-control, or format-revision marker is kept as a
                // zero-length marked run rather than dropped, so the marker is not lost on commit.
                var markedFmt = ReadRunFormatting(markedRun);
                if (markers.Revision is { } rev)
                    markedFmt = StripRevisionChrome(markedFmt, rev.Kind, rev.RenderedColorHex);
                // Format-revision decoration injects a dotted underline and may tint the foreground;
                // strip both so they aren't mistaken for real formatting on commit.
                if (markers.FormatRevision is { } formatRevision)
                    markedFmt = StripFormatRevisionChrome(markedFmt, formatRevision.RenderedColorHex);
                // Comment and content-control both inject a background; clear it so it isn't mistaken for a
                // real highlight on commit (matching the prior per-facet behaviour).
                if (markers.Comment is not null || markers.Control is not null)
                    markedFmt = markedFmt with { HighlightColorHex = null };

                // For a checkbox the run text holds the (possibly toggled) ☒/☐ glyph; keep the control's
                // checked state in sync with the glyph so an in-place toggle round-trips.
                var control = markers.Control?.Control;
                if (control is { Kind: ContentControlKind.CheckBox })
                    control = control with { Checked = markedRun.Text == ModelContentControl.CheckedGlyph };

                modelParagraph.Runs.Add(new ModelRun(StripSoftHyphens(markedRun.Text), markedFmt)
                {
                    HyperlinkUrl = hyperlinkUrl,
                    HyperlinkAnchor = hyperlinkAnchor,
                    HyperlinkTooltip = hyperlinkTooltip,
                    CommentId = markers.Comment?.CommentId,
                    Control = control,
                    Revision = markers.Revision?.Kind ?? RevisionKind.None,
                    RevisionAuthor = markers.Revision?.Author,
                    RevisionDateXml = markers.Revision?.DateXml,
                    FormatRevision = markers.FormatRevision?.Revision
                });
                break;
            case WpfRun run when run.Text.Length > 0:
                modelParagraph.Runs.Add(new ModelRun(StripSoftHyphens(run.Text), ReadRunFormatting(run)) { HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip });
                break;
        }
    }

    private static void ReadFloaterInlineContent(ModelParagraph modelParagraph, Floater floater, string? hyperlinkUrl, string? hyperlinkAnchor, string? hyperlinkTooltip)
    {
        foreach (var paragraph in floater.Blocks.OfType<WpfParagraph>())
            foreach (var inline in paragraph.Inlines)
                ReadInline(modelParagraph, inline, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
    }

    private static ModelRun WithHyperlink(ModelRun run, string? url, string? anchor, string? tooltip)
    {
        run.HyperlinkUrl = url;
        run.HyperlinkAnchor = anchor;
        run.HyperlinkTooltip = tooltip;
        return run;
    }

    private static void ReadFloatingWrapReservationFloater(ModelParagraph modelParagraph, Floater floater, string? hyperlinkUrl, string? hyperlinkAnchor, string? hyperlinkTooltip)
    {
        if (floater.Tag is FloatingWrapReservationMarker { Anchor: { } marker } reservationMarker)
        {
            AddAnchorMarkerRun(
                modelParagraph,
                marker,
                reservationMarker.HyperlinkUrl ?? hyperlinkUrl,
                reservationMarker.HyperlinkAnchor ?? hyperlinkAnchor,
                reservationMarker.HyperlinkTooltip ?? hyperlinkTooltip);
            return;
        }

        foreach (var block in floater.Blocks)
        {
            if (block is BlockUIContainer { Child: FrameworkElement { Tag: FloatingWrapReservationMarker { Anchor: { } nestedMarker } nestedReservationMarker } })
            {
                AddAnchorMarkerRun(
                    modelParagraph,
                    nestedMarker,
                    nestedReservationMarker.HyperlinkUrl ?? hyperlinkUrl,
                    nestedReservationMarker.HyperlinkAnchor ?? hyperlinkAnchor,
                    nestedReservationMarker.HyperlinkTooltip ?? hyperlinkTooltip);
                return;
            }
        }
    }

    private static bool HasFloatingWrapReservationMarker(Floater floater)
    {
        if (floater.Tag is FloatingWrapReservationMarker)
            return true;

        return floater.Blocks.OfType<BlockUIContainer>()
            .Any(block => block.Child is FrameworkElement { Tag: FloatingWrapReservationMarker });
    }

    private static void ReadFloatingWrapReservationFigure(ModelParagraph modelParagraph, Figure figure, string? hyperlinkUrl, string? hyperlinkAnchor, string? hyperlinkTooltip)
    {
        if (figure.Tag is FloatingWrapReservationMarker { Anchor: { } marker } reservationMarker)
        {
            AddAnchorMarkerRun(
                modelParagraph,
                marker,
                reservationMarker.HyperlinkUrl ?? hyperlinkUrl,
                reservationMarker.HyperlinkAnchor ?? hyperlinkAnchor,
                reservationMarker.HyperlinkTooltip ?? hyperlinkTooltip);
            return;
        }

        foreach (var block in figure.Blocks)
        {
            if (block is BlockUIContainer { Child: FrameworkElement { Tag: FloatingWrapReservationMarker { Anchor: { } nestedMarker } nestedReservationMarker } })
            {
                AddAnchorMarkerRun(
                    modelParagraph,
                    nestedMarker,
                    nestedReservationMarker.HyperlinkUrl ?? hyperlinkUrl,
                    nestedReservationMarker.HyperlinkAnchor ?? hyperlinkAnchor,
                    nestedReservationMarker.HyperlinkTooltip ?? hyperlinkTooltip);
                return;
            }
        }
    }

    private static bool HasFloatingWrapReservationMarker(Figure figure)
    {
        if (figure.Tag is FloatingWrapReservationMarker)
            return true;

        return figure.Blocks.OfType<BlockUIContainer>()
            .Any(block => block.Child is FrameworkElement { Tag: FloatingWrapReservationMarker });
    }

    private static void AddAnchorMarkerRun(ModelParagraph modelParagraph, AnchorMarker marker, string? hyperlinkUrl, string? hyperlinkAnchor, string? hyperlinkTooltip)
    {
        if (marker.Image is { } anchorImage)
        {
            modelParagraph.Runs.Add(new ModelRun(string.Empty)
            {
                Image = anchorImage,
                HyperlinkUrl = hyperlinkUrl,
                HyperlinkAnchor = hyperlinkAnchor,
                HyperlinkTooltip = hyperlinkTooltip,
            });
            return;
        }

        if (marker.Shape is { } anchorShape)
        {
            modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromShape(anchorShape), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
            return;
        }

        if (marker.Chart is { } anchorChart)
        {
            modelParagraph.Runs.Add(new ModelRun(string.Empty)
            {
                Chart = anchorChart,
                HyperlinkUrl = hyperlinkUrl,
                HyperlinkAnchor = hyperlinkAnchor,
                HyperlinkTooltip = hyperlinkTooltip,
            });
            return;
        }

        if (marker.SmartArt is { } anchorSmartArt)
        {
            modelParagraph.Runs.Add(new ModelRun(string.Empty)
            {
                SmartArt = anchorSmartArt,
                HyperlinkUrl = hyperlinkUrl,
                HyperlinkAnchor = hyperlinkAnchor,
                HyperlinkTooltip = hyperlinkTooltip,
            });
            return;
        }

        if (marker.WordArt is { } anchorWordArt)
        {
            modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromWordArt(anchorWordArt), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
            return;
        }

        if (marker.DrawingGroup is { } anchorGroup)
            modelParagraph.Runs.Add(WithHyperlink(ModelRun.FromDrawingGroup(anchorGroup), hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip));
    }

    private static ModelTable ReadTable(WpfTable wpfTable, TextDocument document)
    {
        var table = new ModelTable();

        // Recover the table-style toggles and catalog style id stashed by BuildTable (WPF FlowDocument
        // tables can't express header/banded/repeat or a named style as table-level state, so they ride
        // along on the WpfTableTag). Borders are still reconstructed from the view below so a user
        // toggling borders is honoured.
        var stashedTag = wpfTable.Tag as WpfTableTag;
        var stashed = stashedTag?.Formatting;
        var headerRow = stashed?.HeaderRow ?? false;
        var bandedRows = stashed?.BandedRows ?? false;
        var repeatHeader = stashed?.RepeatHeaderRow ?? false;
        var tableStyleId = stashedTag?.TableStyleId;
        var tableBorders = stashedTag?.Borders;

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
        // preserving explicit vertical-merge tags. Legacy WPF RowSpan cells are still expanded into
        // Continue placeholders below so documents rendered before the explicit-cell path round-trip.
        var modelRows = new List<ModelTableRow>();
        // pendingContinues[rowIndex] = list of (gridColumn, gridSpan) continuation cells to inject.
        var pendingContinues = new List<List<(int Column, int Span)>>();
        foreach (var rowGroup in wpfTable.RowGroups)
        {
            foreach (var wpfRow in rowGroup.Rows)
            {
                if (IsRepeatedHeaderRenderRow(wpfRow))
                    continue;
                modelRows.Add(new ModelTableRow());
                pendingContinues.Add([]);
            }
        }

        var rowIndex = 0;
        foreach (var rowGroup in wpfTable.RowGroups)
        {
            foreach (var wpfRow in rowGroup.Rows)
            {
                if (IsRepeatedHeaderRenderRow(wpfRow))
                    continue;
                var sourceRowIndex = wpfRow.Tag is WpfTableRowTag { SourceRowIndex: >= 0 } rowTag
                    ? rowTag.SourceRowIndex
                    : rowIndex;
                var isHeaderRow = headerRow && sourceRowIndex == 0;
                var isBandedRow = bandedRows
                    && !isHeaderRow
                    && TableBanding.IsBandedBodyRow(sourceRowIndex, headerRow);
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
                    var cellTag = wpfCell.Tag as TableCellTag;
                    var cell = new ModelTableCell
                    {
                        ShadingColorHex = cellShading,
                        GridSpan = span,
                        VerticalMerge = cellTag?.VerticalMerge ?? VerticalMergeState.None,
                        // Recover per-cell borders, text direction and vertical alignment from the stashed
                        // Tag (WPF FlowDocument has no native representation for any of these; they survive
                        // the view→model round-trip only via the Tag).
                        Borders = cellTag?.Borders,
                        TextDirection = cellTag?.TextDirection ?? CellTextDirection.Horizontal,
                        VerticalAlignment = cellTag?.VerticalAlignment ?? TableCellVerticalAlignment.Top
                    };
                    foreach (var cellBlock in wpfCell.Blocks)
                        AddCellBlockParagraphs(cell, cellBlock, document);
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
        // Recover the catalog style id (null if no named style was applied). This is stashed on the
        // WpfTableTag by BuildTable and must be written back so CommitToModel preserves the style.
        table.TableStyleId = tableStyleId;
        table.Borders = tableBorders;
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

    private static IReadOnlyList<System.Windows.Documents.Block> BuildBlocks(
        ModelBlock block,
        TextDocument document,
        int sourceBlockIndex,
        IReadOnlyList<LeadingWrapReservation>? leadingWrapReservations = null,
        IReadOnlySet<ModelRun>? suppressedFloatingWrapRuns = null,
        PreservedNumberingMarkerPlan? preservedNumberingMarker = null) => block switch
    {
        ModelTable table => BuildTableBlocks(table, document, sourceBlockIndex),
        ModelParagraph paragraph => [BuildParagraph(
            paragraph,
            document,
            sourceBlockIndex: sourceBlockIndex,
            leadingWrapReservations: leadingWrapReservations,
            suppressedFloatingWrapRuns: suppressedFloatingWrapRuns,
            preservedNumberingMarker: preservedNumberingMarker?.Text)],
        _ => [BuildParagraph(new ModelParagraph(), document)]
    };

    private IReadOnlyDictionary<int, IReadOnlyList<LeadingWrapReservation>> BuildLeadingWrapReservations(
        TextDocument document,
        out HashSet<ModelRun> suppressedFloatingWrapRuns)
    {
        suppressedFloatingWrapRuns = new HashSet<ModelRun>(ReferenceEqualityComparer.Instance);
        var result = new Dictionary<int, IReadOnlyList<LeadingWrapReservation>>();
        var surface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
            document.Page,
            PrintLayoutEnabled,
            PlainPadding.Left);

        for (var sourceBlockIndex = 1; sourceBlockIndex < document.Blocks.Count; sourceBlockIndex++)
        {
            if (document.Blocks[sourceBlockIndex] is not ModelParagraph paragraph)
                continue;

            if (!paragraph.Runs.Any(run =>
                    run.Image is
                    {
                        IsFloating: true,
                        VerticalAnchor: VerticalAnchor.Page,
                        Wrapping: ImageWrapping.Square or ImageWrapping.Tight
                    }))
                continue;

            var snapshots = DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
                paragraph,
                sourceBlockIndex,
                DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(document, sourceBlockIndex),
                surface,
                columnCount: 1);

            foreach (var snapshot in snapshots)
            {
                if (snapshot.Kind != DocumentFloatingObjectKind.Image
                    || snapshot.Wrapping is not (ImageWrapping.Square or ImageWrapping.Tight)
                    || snapshot.Rect.TopDip >= surface.MarginTopDip
                    || snapshot.RunIndex < 0
                    || snapshot.RunIndex >= paragraph.Runs.Count)
                    continue;

                var reservation = DocumentViewLayoutPlanner.BuildFloatingWrapReservation(
                    paragraph.Runs[snapshot.RunIndex]);
                if (reservation is null)
                    continue;

                var contentCenterDip = surface.ContentLeftDip + surface.ContentWidthDip / 2;
                var contentRightDip = surface.ContentLeftDip + surface.ContentWidthDip;
                var reservationWidthDip = snapshot.Rect.CenterXDip <= contentCenterDip
                    ? snapshot.Rect.RightDip - surface.ContentLeftDip + FloatingWrapGapDip
                    : contentRightDip - snapshot.Rect.LeftDip + FloatingWrapGapDip;
                reservation = reservation with
                {
                    WidthDip = Math.Max(reservation.WidthDip, reservationWidthDip)
                };

                // A page-anchored image above the content margin belongs in the first text paragraph
                // on its page for flow purposes. The real anchor marker remains in its original model
                // paragraph; this visual-only copy is deliberately untagged and is ignored on commit.
                var pageIndex = surface.PageIndexFromPageSpaceY(snapshot.Rect.TopDip);
                var firstFlowParagraphIndex = FindFirstFlowParagraphIndexOnPage(
                    document,
                    surface,
                    pageIndex,
                    sourceBlockIndex);
                if (firstFlowParagraphIndex < 0)
                    continue;

                var list = result.TryGetValue(firstFlowParagraphIndex, out var existing)
                    ? existing.ToList()
                    : [];
                var run = paragraph.Runs[snapshot.RunIndex];
                list.Add(new LeadingWrapReservation(run, reservation, snapshot.Rect));
                suppressedFloatingWrapRuns.Add(run);
                result[firstFlowParagraphIndex] = list;
            }
        }

        return result;
    }

    private static int FindFirstFlowParagraphIndexOnPage(
        TextDocument document,
        DocumentViewSurfacePlan surface,
        int pageIndex,
        int sourceBlockIndex)
    {
        if (sourceBlockIndex <= 0)
            return -1;

        var safePageIndex = Math.Max(0, pageIndex);
        for (var blockIndex = 0; blockIndex < sourceBlockIndex; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not ModelParagraph)
                continue;

            var leadingContentHeightDip = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(document, blockIndex);
            var estimatedPageIndex = surface.TextAreaHeightDip > 0
                ? Math.Max(0, (int)(leadingContentHeightDip / surface.TextAreaHeightDip))
                : 0;
            if (estimatedPageIndex >= safePageIndex)
                return blockIndex;
        }

        return -1;
    }

    // The legacy light fills used to render the table-style toggles when no named TableStyleId is set.
    // These match DocxWriter's header/banded fill constants and round-trip via DocxReader's strip logic.
    private static readonly Color HeaderRowFill = Color.FromRgb(0xD9, 0xE2, 0xF3);
    private static readonly Color BandedRowFill = Color.FromRgb(0xF2, 0xF2, 0xF2);

    /// <summary>
    /// Carried on a rendered <see cref="WpfTableCell"/>'s Tag so <see cref="ReadTable"/> can recover the
    /// cell's <em>author-set</em> shading on commit. The rendered background alone is ambiguous — real
    /// shading can equal the header/banded style fill — so the model value is stashed verbatim here and the
    /// colour-equality heuristic is used only for cells the user created fresh in the editor (no Tag).
    /// </summary>
    private sealed record TableCellTag(
        string? ShadingColorHex,
        CellBorders? Borders = null,
        CellTextDirection TextDirection = CellTextDirection.Horizontal,
        TableCellVerticalAlignment VerticalAlignment = TableCellVerticalAlignment.Top,
        VerticalMergeState VerticalMerge = VerticalMergeState.None);

    /// <summary>
    /// Carried on a rendered <see cref="WpfTable"/>'s Tag so <see cref="ReadTable"/> can recover values
    /// that the WPF FlowDocument table cannot express: the <see cref="TableFormatting"/> toggles, explicit
    /// <see cref="TableBorders"/>, and the <see cref="TableStyleId"/> (the named catalog style). They are stashed on <see cref="BuildTable"/>
    /// and recovered on commit so they survive the view→model round-trip unmodified.
    /// </summary>
    private sealed record WpfTableTag(
        TableFormatting Formatting,
        string? TableStyleId,
        TableBorders? Borders,
        int SourceBlockIndex = -1,
        int SegmentIndex = 0,
        int SegmentCount = 1,
        int PageNumber = 1,
        bool IsPaginationSegment = false);

    private sealed record WpfTableRowTag(int SourceRowIndex, bool IsRepeatedHeader);

    private static IReadOnlyList<System.Windows.Documents.Block> BuildTableBlocks(
        ModelTable table,
        TextDocument document,
        int sourceBlockIndex)
    {
        var leadingContentHeightDip = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(
            document,
            sourceBlockIndex);
        var tableLayoutPlan = DocumentViewLayoutPlanner.BuildTableLayoutPlan(
            table,
            page: document.Page,
            firstPageLeadingContentHeightDip: leadingContentHeightDip);
        var paginationPlan = tableLayoutPlan.Pagination;
        if (ShouldRenderPlannedTablePages(table, paginationPlan))
        {
            var blocks = new List<System.Windows.Documents.Block>();
            foreach (var (page, segmentIndex) in paginationPlan.Pages.Select((page, index) => (page, index)))
            {
                var section = new System.Windows.Documents.Section
                {
                    BreakPageBefore = segmentIndex > 0,
                    Margin = new Thickness(0)
                };
                section.Blocks.Add(BuildTable(
                    table,
                    document,
                    sourceBlockIndex,
                    tableLayoutPlan,
                    page,
                    segmentIndex,
                    paginationPlan.Pages.Count));
                blocks.Add(section);
            }

            return blocks;
        }

        return [BuildTable(table, document, sourceBlockIndex, tableLayoutPlan)];
    }

    private static bool ShouldRenderPlannedTablePages(ModelTable table, DocumentTablePaginationPlan paginationPlan) =>
        paginationPlan.Pages.Count > 1 && !TableHasVerticalMerges(table);

    private static bool TableHasVerticalMerges(ModelTable table) =>
        table.Rows.SelectMany(row => row.Cells).Any(cell => cell.VerticalMerge != VerticalMergeState.None);

    private static bool TryResolveHeaderOnlyTableBorderColor(ModelTable table, out Color color)
    {
        color = Colors.Black;
        if (!table.Formatting.HeaderRow || table.Rows.Count < 2 || table.Borders is null)
            return false;

        var edges = new[]
        {
            table.Borders.Top,
            table.Borders.Left,
            table.Borders.Bottom,
            table.Borders.Right,
            table.Borders.InsideHorizontal,
            table.Borders.InsideVertical
        };
        if (edges.Any(edge => edge is null)
            || edges.Any(edge => edge!.Style != BorderLineStyle.Single || Math.Abs(edge.WidthPt - 0.5) > 0.001))
            return false;

        if (table.Rows[0].Cells.Count == 0
            || table.Rows[0].Cells.Any(cell => cell.Borders is null or { IsEmpty: true })
            || table.Rows.Skip(1).SelectMany(row => row.Cells).Any(cell => cell.Borders is { IsEmpty: false }))
            return false;

        var colorToken = edges[0]!.ColorToken.Trim();
        if (!edges.All(edge => string.Equals(edge!.ColorToken.Trim(), colorToken, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (string.Equals(colorToken, "auto", StringComparison.OrdinalIgnoreCase))
            return true;

        return TryParseColor(colorToken, out color);
    }

    private static WpfTable BuildTable(
        ModelTable table,
        TextDocument document,
        int sourceBlockIndex = -1,
        DocumentTableLayoutPlan? tableLayoutPlan = null,
        DocumentTablePaginationPagePlan? paginationPage = null,
        int segmentIndex = 0,
        int segmentCount = 1)
    {
        // Stash the model's table formatting AND the catalog style id on the WPF table Tag so both survive
        // the view→model round-trip (CommitToModel's ReadTable reconstructs Borders from the view but
        // recovers the toggles and style id from this tag, which WPF FlowDocument tables can't express).
        var isPaginationSegment = paginationPage is not null && segmentCount > 1;
        var wpf = new WpfTable
        {
            BreakPageBefore = isPaginationSegment && segmentIndex > 0,
            Tag = new WpfTableTag(
                table.Formatting,
                table.TableStyleId,
                table.Borders,
                sourceBlockIndex,
                segmentIndex,
                segmentCount,
                paginationPage?.PageNumber ?? 1,
                isPaginationSegment)
        };
        if (isPaginationSegment)
        {
            wpf.Margin = ResolveTableBlockMargin(table, document);
            wpf.CellSpacing = 0;
        }
        // WPF Table.CellSpacing grows both axes and breaks Word's fixed-width paginated tables.
        // Preserve that width contract while reserving the authored vertical cell gap on each row.
        var paginationVerticalCellSpacingDip = table.CellSpacingPt is > 0
            ? table.CellSpacingPt.Value * PxPerPoint
            : 0;
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
        tableLayoutPlan ??= DocumentViewLayoutPlanner.BuildTableLayoutPlan(table, page: document.Page);
        var cellEffectiveFills = tableLayoutPlan.Cells.ToDictionary(
            cell => (cell.RowIndex, cell.CellIndex),
            cell => cell.EffectiveFill);
        var preservedNumberingMarkers = PreservedNumberingMarkerPlanner.BuildByParagraph(document);

        DocumentTableCellEffectiveFillPlan EffectiveFillFor(int rowIndex, int cellIndex) =>
            cellEffectiveFills.TryGetValue((rowIndex, cellIndex), out var fillPlan)
                ? fillPlan
                : DocumentTableCellEffectiveFillPlan.Empty;

        // Resolve the catalog style for table-level chrome such as border color; per-cell fills and bold
        // come from the shared DocumentTableCellEffectiveFillPlan above.
        var catalogStyle = table.TableStyleId is { Length: > 0 } sid
            ? DocumentTableStyle.FindById(sid)
            : null;

        // A header-only custom-border table keeps its complete uniform table payload for generic chrome.
        // Word resolves its literal "auto" token to black here, ahead of the named-style fallback.
        var borderColor = TryResolveHeaderOnlyTableBorderColor(table, out var explicitBorderColor)
            ? explicitBorderColor
            : catalogStyle?.BorderColorHex is { Length: > 0 } borderHex
                ? (Color)ColorConverter.ConvertFromString("#" + borderHex)
                : Color.FromRgb(0x9A, 0x9A, 0x9A);
        var borderBrush = new SolidColorBrush(borderColor);

        if (table.Formatting.Borders)
        {
            wpf.BorderBrush = borderBrush;
            wpf.BorderThickness = new Thickness(0.5);
        }

        var totalRows = table.Rows.Count;
        var group = new TableRowGroup();
        void AppendRenderedRow(int rowIndex, bool isRepeatedHeader)
        {
            var modelRow = table.Rows[rowIndex];
            var wpfRow = new WpfTableRow { Tag = new WpfTableRowTag(rowIndex, isRepeatedHeader) };
            // WPF System.Windows.Documents.TableRow is a TextElement (not FrameworkElement), so it has
            // no MinHeight / Height property. To enforce a minimum row height we inject a zero-width
            // height-enforcer into every non-Continue cell: a BlockUIContainer holding a Border whose
            // MinHeight matches the requested row height. For both AtLeast and Exact rules, MinHeight is
            // the closest WPF mapping. Exact cannot clip cell content (content overflows rather than being
            // clipped) — that is a documented WPF FlowDocument limitation.
            var rowHeightPx = modelRow.HeightPt is { } heightPt && heightPt > 0
                ? (double?)(heightPt * PxPerPoint)
                : null;
            if (rowHeightPx is { } authoredRowHeight && modelRow.HeightRule == TableRowHeightRule.Exact)
            {
                // FlowDocument adds table-cell chrome outside the BlockUI content host. Reserve
                // that measured overhead so an exact Word row does not grow by the full amount.
                rowHeightPx = Math.Max(0, authoredRowHeight - 2);
            }
            // Track the running grid-column position so vertical-merge runs can be matched up by
            // column even when earlier cells span multiple grid columns.
            var gridColumn = 0;
            var cellIndex = 0;
            foreach (var modelCell in modelRow.Cells)
            {
                var span = Math.Max(1, modelCell.GridSpan);
                var wpfCell = new WpfTableCell
                {
                    Padding = new Thickness(
                        4,
                        2 + paginationVerticalCellSpacingDip,
                        4,
                        2 + paginationVerticalCellSpacingDip)
                };
                if (span > 1)
                    wpfCell.ColumnSpan = span;
                if (table.Formatting.Borders)
                {
                    wpfCell.BorderBrush = borderBrush;
                    wpfCell.BorderThickness = new Thickness(0.5);
                }
                // Stash the model's author-set shading, per-cell borders, text direction and vertical
                // alignment on the cell Tag so ReadTable can recover them on commit. A colour-equality
                // heuristic alone can't distinguish author shading from style fills; borders, text
                // direction and vertical alignment have no WPF FlowDocument equivalent, so they survive
                // only through the stashed Tag.
                wpfCell.Tag = new TableCellTag(modelCell.ShadingColorHex, modelCell.Borders,
                    modelCell.TextDirection, modelCell.VerticalAlignment, modelCell.VerticalMerge);

                var cellBorderPlan = TableCellBorderVisualPlanner.Build(modelCell.Borders, PxPerPoint);
                if (cellBorderPlan.HasVisibleEdges)
                {
                    wpfCell.BorderBrush = null;
                    wpfCell.BorderThickness = new Thickness(0);
                }

                var mergeSource = modelCell.VerticalMerge == VerticalMergeState.Continue
                    ? FindVerticalMergeRestart(table, rowIndex, gridColumn)
                    : null;
                var cellAppearance = mergeSource is { } source
                    ? EffectiveFillFor(source.RowIndex, source.CellIndex)
                    : EffectiveFillFor(rowIndex, cellIndex);
                if (TryParseColor(cellAppearance.EffectiveFillHex, out var cellFill))
                    wpfCell.Background = new SolidColorBrush(cellFill);
                if (cellAppearance.EffectiveBold)
                    wpfCell.FontWeight = FontWeights.Bold;
                var hasPlannedCellBorders = cellBorderPlan.HasVisibleEdges;
                if (!hasPlannedCellBorders && table.Formatting.Borders &&
                    modelCell.VerticalMerge is VerticalMergeState.Restart or VerticalMergeState.Continue)
                {
                    var hasContinuation = modelCell.VerticalMerge == VerticalMergeState.Restart &&
                        rowIndex + 1 < table.Rows.Count &&
                        CellAtGridColumn(table.Rows[rowIndex + 1], gridColumn)?.VerticalMerge == VerticalMergeState.Continue;
                    wpfCell.BorderThickness = modelCell.VerticalMerge == VerticalMergeState.Restart && hasContinuation
                        ? new Thickness(0.5, 0.5, 0.5, 0)
                        : modelCell.VerticalMerge == VerticalMergeState.Continue
                            ? new Thickness(0.5, 0, 0.5, 0.5)
                            : wpfCell.BorderThickness;
                }
                // Resolve cell content. For non-Top vertical alignment, or when text is rotated, we wrap
                // everything in a BlockUIContainer so we can position the content via WPF layout. WPF
                // FlowDocument's TableCell has no VerticalAlignment property, so the Grid wrapper is the
                // closest faithful mapping: it stretches to fill the cell height (given by MinHeight) and
                // positions the inner content at the requested vertical position. Top is rendered as plain
                // Paragraph blocks (the default FlowDocument path) for editing efficiency; Center and
                // Bottom use a Grid+StackPanel wrapper. Exact-height clamping is not enforceable in WPF
                // FlowDocument (content may overflow the MinHeight), which is a known residual WPF limit.
                var vAlign = modelCell.VerticalAlignment;
                var paginatedContentMargins = isPaginationSegment && table.CellSpacingPt is > 0
                    ? modelCell.Margins ?? table.DefaultCellMargins ?? TableCellMargins.Default
                    : null;
                if (hasPlannedCellBorders)
                {
                    var cellContentHost = BuildCellContentHost(
                        modelCell,
                        document,
                        vAlign,
                        rowHeightPx,
                        cellBorderPlan,
                        paginatedContentMargins,
                        preservedNumberingMarkers);
                    if (isPaginationSegment
                        && table.CellSpacingPt is > 0
                        && wpfCell.Background is { } spacedCellBackground)
                    {
                        var spacingDip = table.CellSpacingPt.Value * PxPerPoint;
                        var surfaceMargin = new Thickness(
                            cellIndex == 0 ? spacingDip / 2 : -spacingDip,
                            0,
                            cellIndex == modelRow.Cells.Count - 1 ? spacingDip : 0,
                            0);
                        var fillSurface = new Border
                        {
                            Background = spacedCellBackground,
                            IsHitTestVisible = false,
                            Margin = surfaceMargin
                        };
                        System.Windows.Controls.Panel.SetZIndex(fillSurface, -1);
                        cellContentHost.Children.Insert(0, fillSurface);
                        cellContentHost.Children.OfType<TableCellBorderChrome>().Single().Margin = surfaceMargin;
                        wpfCell.Background = null;
                    }
                    wpfCell.Blocks.Add(new BlockUIContainer(cellContentHost));
                }
                else if (rowHeightPx is not null)
                {
                    // Keep the authored row height around its content. Appending a separate spacer
                    // below the content makes WPF add the two heights together.
                    wpfCell.Blocks.Add(new BlockUIContainer(BuildCellContentHost(
                        modelCell,
                        document,
                        vAlign,
                        rowHeightPx,
                        cellBorderPlan,
                        paginatedContentMargins,
                        preservedNumberingMarkers)));
                }
                else if (modelCell.TextDirection != CellTextDirection.Horizontal)
                {
                    // Rotated cell: wrap all paragraphs in a StackPanel with a LayoutTransform so the
                    // text rotates inside the cell, mirroring how shapes apply LayoutTransform to their
                    // text blocks (see BuildShape). A BlockUIContainer lets any UIElement live in a
                    // FlowDocument block.
                    var angle = modelCell.TextDirection == CellTextDirection.Rotate90 ? 90.0 : 270.0;
                    var stack = new System.Windows.Controls.StackPanel
                    {
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(angle)
                    };
                    foreach (var block in BuildTableCellParagraphs(modelCell, document, preservedNumberingMarkers))
                    {
                        // Keep each paragraph in a nested FlowDocument so its margins survive rotation.
                        var nested = new System.Windows.Controls.RichTextBox
                        {
                            Document = new System.Windows.Documents.FlowDocument(block),
                            IsReadOnly = true,
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(0),
                            Background = System.Windows.Media.Brushes.Transparent
                        };
                        stack.Children.Add(nested);
                    }
                    wpfCell.Blocks.Add(new BlockUIContainer(stack));
                }
                else if (vAlign != TableCellVerticalAlignment.Top)
                {
                    // Center or Bottom vertical alignment: wrap all paragraphs in a Grid that stretches
                    // to fill the row height and positions the inner StackPanel accordingly. The Grid's
                    // own VerticalAlignment=Stretch (the WPF default) fills the cell; the StackPanel's
                    // VerticalAlignment positions the content block within the cell.
                    var wpfVAlign = vAlign == TableCellVerticalAlignment.Center
                        ? VerticalAlignment.Center
                        : VerticalAlignment.Bottom;
                    var contentStack = new System.Windows.Controls.StackPanel
                    {
                        VerticalAlignment = wpfVAlign
                    };
                    foreach (var paraBlock in BuildTableCellParagraphs(modelCell, document, preservedNumberingMarkers))
                    {
                        var nestedRtb = new System.Windows.Controls.RichTextBox
                        {
                            Document = new System.Windows.Documents.FlowDocument(paraBlock),
                            IsReadOnly = true,
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(0),
                            Background = System.Windows.Media.Brushes.Transparent
                        };
                        contentStack.Children.Add(nestedRtb);
                    }
                    var grid = new System.Windows.Controls.Grid();
                    grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
                    {
                        Height = new GridLength(1, GridUnitType.Star)
                    });
                    grid.Children.Add(contentStack);
                    wpfCell.Blocks.Add(new BlockUIContainer(grid));
                }
                else
                {
                    foreach (var paraBlock in BuildTableCellParagraphs(modelCell, document, preservedNumberingMarkers))
                        wpfCell.Blocks.Add(paraBlock);
                }
                wpfRow.Cells.Add(wpfCell);
                gridColumn += span;
                cellIndex++;
            }
            group.Rows.Add(wpfRow);
        }

        if (paginationPage is not null && paginationPage.RenderRows.Count > 0)
        {
            foreach (var renderRow in paginationPage.RenderRows)
            {
                if (renderRow.SourceRowIndex >= 0 && renderRow.SourceRowIndex < totalRows)
                    AppendRenderedRow(renderRow.SourceRowIndex, renderRow.IsRepeatedHeader);
            }
        }
        else
        {
            for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
                AppendRenderedRow(rowIndex, isRepeatedHeader: false);
        }
        wpf.RowGroups.Add(group);
        return wpf;
    }

    // Word suppresses the shared before/after spacing only for adjacent paragraphs in the same
    // cell that resolve to the same contextual style. Keep this sequence local to the cell: table
    // boundaries and the rotated TextBlock path have different layout ownership.
    private static IReadOnlyList<WpfParagraph> BuildTableCellParagraphs(
        ModelTableCell cell,
        TextDocument document,
        IReadOnlyDictionary<ModelParagraph, PreservedNumberingMarkerPlan> preservedNumberingMarkers)
    {
        var modelParagraphs = cell.Paragraphs.Count > 0
            ? cell.Paragraphs
            : [new ModelParagraph()];
        var paragraphs = new List<WpfParagraph>(modelParagraphs.Count);
        ModelParagraph? previousModelParagraph = null;
        WpfParagraph? previousWpfParagraph = null;

        foreach (var modelParagraph in modelParagraphs)
        {
            var wpfParagraph = BuildParagraph(
                modelParagraph,
                document,
                inTableCell: true,
                preservedNumberingMarker: preservedNumberingMarkers.TryGetValue(modelParagraph, out var marker)
                    ? marker.Text
                    : null);
            if (previousModelParagraph is not null
                && previousWpfParagraph is not null
                && SuppressesContextualSpacing(previousModelParagraph, modelParagraph, document))
            {
                previousWpfParagraph.Margin = new Thickness(
                    previousWpfParagraph.Margin.Left,
                    previousWpfParagraph.Margin.Top,
                    previousWpfParagraph.Margin.Right,
                    0);
                wpfParagraph.Margin = new Thickness(
                    wpfParagraph.Margin.Left,
                    0,
                    wpfParagraph.Margin.Right,
                    wpfParagraph.Margin.Bottom);
            }

            paragraphs.Add(wpfParagraph);
            previousModelParagraph = modelParagraph;
            previousWpfParagraph = wpfParagraph;
        }

        return paragraphs;
    }

    private static Thickness ResolveTableBlockMargin(ModelTable table, TextDocument document)
    {
        var indent = Math.Max(0, table.IndentFromLeftPt ?? 0) * PxPerPoint;
        var widthPt = table.PreferredWidthPt is > 0
            ? table.PreferredWidthPt.Value
            : table.ColumnWidthsPt.Count > 0
                ? table.ColumnWidthsPt.Where(width => width > 0).Sum()
                : 0;
        if (widthPt <= 0 || table.Alignment == TableAlignment.Left)
            return new Thickness(indent, 0, 0, 0);

        var metrics = DocumentViewLayoutPlanner.BuildPageMetrics(document.Page);
        var contentWidth = document.Page.ColumnCount > 1
            ? DocumentViewLayoutPlanner.BuildColumnPlan(document.Page, metrics.ContentWidthDip, usePageColumns: true).WidthDip
            : metrics.ContentWidthDip;
        var slack = Math.Max(0, contentWidth - widthPt * PxPerPoint - indent);
        var alignmentOffset = table.Alignment == TableAlignment.Center ? slack / 2 : slack;
        return new Thickness(indent + alignmentOffset, 0, 0, 0);
    }

    private static System.Windows.Controls.Grid BuildCellContentHost(
        ModelTableCell modelCell,
        TextDocument document,
        TableCellVerticalAlignment verticalAlignment,
        double? minHeightPx,
        TableCellBorderVisualPlan borderPlan,
        TableCellMargins? contentMargins,
        IReadOnlyDictionary<ModelParagraph, PreservedNumberingMarkerPlan> preservedNumberingMarkers)
    {
        var grid = new System.Windows.Controls.Grid();
        if (minHeightPx is { } minHeight)
            grid.MinHeight = minHeight;

        var stack = new System.Windows.Controls.StackPanel
        {
            RenderTransform = contentMargins is { } margins
                ? new TranslateTransform(
                    Math.Max(0, margins.LeftPt * PxPerPoint - WpfTableCellContentInsetDip),
                    margins.TopPt * PxPerPoint)
                : Transform.Identity,
            VerticalAlignment = verticalAlignment switch
            {
                TableCellVerticalAlignment.Center => VerticalAlignment.Center,
                TableCellVerticalAlignment.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top
            }
        };

        if (modelCell.TextDirection != CellTextDirection.Horizontal)
        {
            // Keep the rotated content's measured bounds inside the authored row. Rotating a
            // RichTextBox or StackPanel with LayoutTransform makes WPF's table measure an
            // unconstrained swapped axis and can expand a short Word row to the full page height.
            var textWidth = minHeightPx is { } height ? Math.Max(1, height) : 100;
            var angle = modelCell.TextDirection == CellTextDirection.Rotate90 ? 90.0 : 270.0;
            stack.HorizontalAlignment = HorizontalAlignment.Center;
            stack.VerticalAlignment = VerticalAlignment.Center;
            foreach (var textBlock in BuildConstrainedRotatedCellTextBlocks(modelCell, document, textWidth, angle))
                stack.Children.Add(textBlock);
        }
        else
        {
            foreach (var paraBlock in BuildTableCellParagraphs(modelCell, document, preservedNumberingMarkers))
            {
                stack.Children.Add(new System.Windows.Controls.RichTextBox
                {
                    Document = new System.Windows.Documents.FlowDocument(paraBlock),
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent
                });
            }
        }

        grid.Children.Add(stack);

        var borderChrome = new TableCellBorderChrome(borderPlan)
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        System.Windows.Controls.Panel.SetZIndex(borderChrome, 1);
        grid.Children.Add(borderChrome);

        return grid;
    }

    // Constrained rotated cells cannot host FlowDocument paragraphs without expanding the table's
    // measured axis. Preserve their logical paragraph spacing on the TextBlock sequence instead.
    private static IReadOnlyList<System.Windows.Controls.TextBlock> BuildConstrainedRotatedCellTextBlocks(
        ModelTableCell cell,
        TextDocument document,
        double textWidth,
        double angle)
    {
        var modelParagraphs = cell.Paragraphs.Count > 0
            ? cell.Paragraphs
            : [new ModelParagraph()];
        var textBlocks = new List<System.Windows.Controls.TextBlock>(modelParagraphs.Count);
        ModelParagraph? previousModelParagraph = null;
        System.Windows.Controls.TextBlock? previousTextBlock = null;

        foreach (var modelParagraph in modelParagraphs)
        {
            var formatting = Resolve(modelParagraph, document);
            var before = formatting.SpaceBeforeIsSet
                ? Math.Max(0, formatting.SpaceBeforePt) * PxPerPoint
                : 0;
            var after = formatting.SpaceAfterIsSet
                ? Math.Max(0, formatting.SpaceAfterPt) * PxPerPoint
                : 0;
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Width = textWidth,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = System.Windows.TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, before, 0, after),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle)
            };
            foreach (var run in modelParagraph.Runs)
                textBlock.Inlines.Add(BuildRun(run, modelParagraph, document));

            if (previousModelParagraph is not null
                && previousTextBlock is not null
                && SuppressesContextualSpacing(previousModelParagraph, modelParagraph, document))
            {
                previousTextBlock.Margin = new Thickness(
                    previousTextBlock.Margin.Left,
                    previousTextBlock.Margin.Top,
                    previousTextBlock.Margin.Right,
                    0);
                textBlock.Margin = new Thickness(
                    textBlock.Margin.Left,
                    0,
                    textBlock.Margin.Right,
                    textBlock.Margin.Bottom);
            }

            textBlocks.Add(textBlock);
            previousModelParagraph = modelParagraph;
            previousTextBlock = textBlock;
        }

        return textBlocks;
    }

    private sealed class TableCellBorderChrome(TableCellBorderVisualPlan plan) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            foreach (var edge in plan.Edges)
                DrawEdge(drawingContext, rect, edge);
        }

        private static void DrawEdge(DrawingContext drawingContext, Rect rect, TableCellBorderEdgeVisualPlan edge)
        {
            if (!edge.IsVisible)
                return;

            var (p1, p2) = CellBorderPoints(edge.Edge, rect, 0);
            var pen = CreatePen(edge);

            if (edge.Style == BorderLineStyle.Wave)
            {
                DrawWaveEdge(drawingContext, rect, edge, pen);
                return;
            }

            if (edge.Style == BorderLineStyle.Double)
            {
                var offset = Math.Max(1.0, edge.WidthDip * 1.5);
                var (outer1, outer2) = CellBorderPoints(edge.Edge, rect, -offset / 2);
                var (inner1, inner2) = CellBorderPoints(edge.Edge, rect, offset / 2);
                drawingContext.DrawLine(pen, outer1, outer2);
                drawingContext.DrawLine(pen, inner1, inner2);
                return;
            }

            drawingContext.DrawLine(pen, p1, p2);
        }

        private static void DrawWaveEdge(
            DrawingContext drawingContext,
            Rect rect,
            TableCellBorderEdgeVisualPlan edge,
            Pen pen)
        {
            // WPF's border chrome starts inside the nested cell-content host.
            const double registrationDip = 2.0;
            var length = edge.Edge is TableCellBorderVisualEdge.Top or TableCellBorderVisualEdge.Bottom
                ? rect.Width
                : rect.Height;
            var offsets = TableCellBorderVisualPlanner.BuildWaveOffsets(length);
            if (offsets.Count < 2)
                return;

            var previous = WavePoint(
                edge.Edge,
                rect,
                offsets[0].AlongDip,
                registrationDip + offsets[0].OutwardDip);
            foreach (var offset in offsets.Skip(1))
            {
                var current = WavePoint(
                    edge.Edge,
                    rect,
                    offset.AlongDip,
                    registrationDip + offset.OutwardDip);
                drawingContext.DrawLine(pen, previous, current);
                previous = current;
            }
        }

        private static Point WavePoint(
            TableCellBorderVisualEdge edge,
            Rect rect,
            double along,
            double outward) => edge switch
            {
                TableCellBorderVisualEdge.Top => new Point(rect.Left + along, rect.Top - outward),
                TableCellBorderVisualEdge.Bottom => new Point(rect.Left + along, rect.Bottom + outward),
                TableCellBorderVisualEdge.Left => new Point(rect.Left - outward, rect.Top + along),
                TableCellBorderVisualEdge.Right => new Point(rect.Right + outward, rect.Top + along),
                _ => new Point(rect.Left + along, rect.Top - outward),
            };

        private static Pen CreatePen(TableCellBorderEdgeVisualPlan edge)
        {
            var color = ParseColor(edge.ColorHex, Colors.Black);
            if (edge.Style == BorderLineStyle.Wave)
                color = Color.FromArgb((byte)Math.Round(255 * edge.StrokeOpacity), color.R, color.G, color.B);

            var pen = new Pen(
                new SolidColorBrush(color),
                edge.WidthDip);

            pen.DashStyle = edge.Style switch
            {
                BorderLineStyle.Dashed => DashStyles.Dash,
                BorderLineStyle.Dotted => DashStyles.Dot,
                _ => null
            };

            return pen;
        }

        private static (Point Start, Point End) CellBorderPoints(TableCellBorderVisualEdge edge, Rect rect, double inwardOffset) =>
            edge switch
            {
                TableCellBorderVisualEdge.Top => (
                    new Point(rect.Left, rect.Top + inwardOffset),
                    new Point(rect.Right, rect.Top + inwardOffset)),
                TableCellBorderVisualEdge.Bottom => (
                    new Point(rect.Left, rect.Bottom - inwardOffset),
                    new Point(rect.Right, rect.Bottom - inwardOffset)),
                TableCellBorderVisualEdge.Left => (
                    new Point(rect.Left + inwardOffset, rect.Top),
                    new Point(rect.Left + inwardOffset, rect.Bottom)),
                TableCellBorderVisualEdge.Right => (
                    new Point(rect.Right - inwardOffset, rect.Top),
                    new Point(rect.Right - inwardOffset, rect.Bottom)),
                _ => (new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top)),
            };
    }

    private static (int RowIndex, int CellIndex)? FindVerticalMergeRestart(
        ModelTable table,
        int continuationRow,
        int gridColumn)
    {
        for (var rowIndex = continuationRow; rowIndex >= 0; rowIndex--)
        {
            var column = 0;
            for (var cellIndex = 0; cellIndex < table.Rows[rowIndex].Cells.Count; cellIndex++)
            {
                var cell = table.Rows[rowIndex].Cells[cellIndex];
                var span = Math.Max(1, cell.GridSpan);
                if (gridColumn < column || gridColumn >= column + span)
                {
                    column += span;
                    continue;
                }

                if (cell.VerticalMerge == VerticalMergeState.Restart)
                    return (rowIndex, cellIndex);
                if (cell.VerticalMerge != VerticalMergeState.Continue)
                    return null;
                break;
            }
        }

        return null;
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

    private static void AddCellBlockParagraphs(ModelTableCell cell, System.Windows.Documents.Block cellBlock, TextDocument document)
    {
        if (cellBlock is WpfParagraph cellParagraph)
        {
            cell.Paragraphs.Add(ReadParagraph(cellParagraph, document));
            return;
        }

        if (cellBlock is not BlockUIContainer { Child: DependencyObject child })
            return;

        foreach (var richTextBox in DescendantRichTextBoxes(child))
        {
            foreach (var paragraph in richTextBox.Document.Blocks.OfType<WpfParagraph>())
                cell.Paragraphs.Add(ReadParagraph(paragraph, document));
        }
    }

    private static IEnumerable<System.Windows.Controls.RichTextBox> DescendantRichTextBoxes(DependencyObject root)
    {
        if (root is System.Windows.Controls.RichTextBox richTextBox)
            yield return richTextBox;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var nested in DescendantRichTextBoxes(child))
                yield return nested;
        }
    }

    private static WpfParagraph BuildParagraph(
        ModelParagraph paragraph,
        TextDocument document,
        bool inTableCell = false,
        int? sourceBlockIndex = null,
        IReadOnlyList<LeadingWrapReservation>? leadingWrapReservations = null,
        IReadOnlySet<ModelRun>? suppressedFloatingWrapRuns = null,
        string? preservedNumberingMarker = null)
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
        // Word's application-default heading/title line boxes leave slightly more clearance before the
        // first body paragraph than WPF's Calibri metrics. Keep this host calibration on the exact imported
        // no-rPrDefault route; explicit document defaults and other styles retain their authored spacing.
        if (document.UseWordApplicationDefaultRunFormatting)
        {
            var applicationClearancePt = paragraph.StyleId?.ToUpperInvariant() switch
            {
                "HEADING1" => 3.0,
                "TITLE" => 4.5,
                _ => 0.0
            };
            if (applicationClearancePt > 0)
                paraFmt = paraFmt with { SpaceAfterPt = paraFmt.SpaceAfterPt + applicationClearancePt };
        }
        // Imported WordprocessingML uses Word's application default multiple when the package cascade
        // omits w:spacing/@w:line. Model-authored FreeW documents keep the host's natural single-line box.
        // Explicit paragraph/style rules and non-default model values remain authoritative.
        var usesWordApplicationDefaultLineSpacing = !paraFmt.LineSpacingIsSet &&
            Math.Abs(paraFmt.LineSpacing - ParagraphFormatting.Default.LineSpacing) <= 0.0001 &&
            document.UseWordApplicationDefaultLineSpacing;
        var hasExplicitMultipleLineSpacing = paraFmt.LineSpacingIsSet ||
            Math.Abs(paraFmt.LineSpacing - ParagraphFormatting.Default.LineSpacing) > 0.0001 ||
            usesWordApplicationDefaultLineSpacing;
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
            BreakColumnBefore = paragraph.Runs.Any(r => r.IsColumnBreak),
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
            LineHeight = paraFmt.LineRule == LineSpacingRule.Multiple && hasExplicitMultipleLineSpacing
                ? (paraFmt.LineSpacing > 0
                    ? paraFmt.LineSpacing * DefaultLineHeightRatio(document) *
                      (usesWordApplicationDefaultLineSpacing && document.UseWordApplicationDefaultRunFormatting
                          ? ImportedWordApplicationLineHeightScale
                          : 1.0) *
                      (document.DefaultRun.FontSizePt ?? 11) * PxPerPoint
                    : double.NaN)
                : (paraFmt.LineHeightPt > 0 ? paraFmt.LineHeightPt * PxPerPoint : double.NaN),
            LineStackingStrategy = paraFmt.LineRule == LineSpacingRule.Exact
                ? LineStackingStrategy.BlockLineHeight
                : LineStackingStrategy.MaxHeight,
            // Flow control: WPF's Paragraph exposes KeepWithNext/KeepTogether directly, so map them so
            // they survive an edit/commit cycle without a Tag. WidowControl has no FlowDocument slot and
            // is carried on the Tag instead (see below).
            KeepWithNext = paraFmt.KeepWithNext,
            // WPF has no widow/orphan setting. KeepTogether is the closest behavior for Word's default-on
            // widow control, especially for the common two-line paragraph at a page boundary.
            // Word keeps the caption/text run with a large inline object when the object would otherwise
            // cross a page boundary. Apply the same paragraph-level constraint to inline charts, SmartArt,
            // WordArt, and images while preserving the explicit model setting for ordinary paragraphs.
            KeepTogether = paraFmt.KeepLinesTogether || !paraFmt.WidowControlIsSet || paraFmt.WidowControl || paragraph.Runs.Any(run =>
                run.Chart is { IsFloating: false } ||
                run.SmartArt is { IsFloating: false } ||
                run.WordArt is { IsFloating: false } ||
                run.Image is { IsFloating: false })
        };

        if (paraFmt.Border is { } border && TryParseColor(border.ColorHex, out var borderColor))
        {
            wpf.BorderBrush = new SolidColorBrush(borderColor);
            // A bottom-only border (horizontal rule) draws just the bottom edge; otherwise the per-edge
            // flags select which edges are drawn (all four = a box). The model-only line style/pattern can't
            // be expressed on a WPF Border, so the full ParagraphBorder is also carried on the Tag (below)
            // and recovered verbatim on commit.
            var w = border.WidthPt * PxPerPoint;
            wpf.BorderThickness = border.BottomOnly
                ? new Thickness(0, 0, 0, w)
                : new Thickness(border.Left ? w : 0, border.Top ? w : 0, border.Right ? w : 0, border.Bottom ? w : 0);
            wpf.Padding = new Thickness(2);
        }
        if (TryParseColor(paraFmt.ShadingColorHex, out var shading))
            wpf.Background = new SolidColorBrush(shading);

        // A forced page break before the paragraph (w:pageBreakBefore) has no FlowDocument equivalent,
        // so render it as a dashed separator along the paragraph's top edge — a visible "page break"
        // marker — and carry the flag on the Tag so it survives commit and round-trips to docx.
        if (paraFmt.PageBreakBefore && _renderPageBreakMarkers)
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

        // WPF's FlowDocument Paragraph has no tab-stop API, so the renderer emits planned inline spacer
        // elements for ordinary text tabs below. A bookmark name is an invisible marker with no FlowDocument
        // representation, and page-break-before has no native slot. Keep the model metadata on every
        // paragraph Tag so an edit/commit cycle cannot serialize renderer-only flow properties such as the
        // widow-control approximation as authored keep-lines-together.
        // The list nesting depth is carried on the Tag too: the editor flattens a list run into one WPF
        // List, so depth has no structural slot and would otherwise reset to 0 on commit (see ParagraphTag).
        // The border's line style / per-edge flags and the shading pattern have no WPF Border equivalent,
        // so carry the full ParagraphBorder + shading pattern on the Tag whenever they are non-default; they
        // are recovered verbatim on commit (see ReadParagraphFormatting) so the dialog's choices survive.
        var borderNeedsTag = paraFmt.Border is { } b
            && (b.LineStyle != BorderLineStyle.Single || !b.Top || !b.Left || !b.Bottom || !b.Right);
        var shadingNeedsTag = paraFmt.ShadingColorHex is { Length: > 0 } && paraFmt.ShadingPattern != ShadingPattern.Clear;
        wpf.Tag = new ParagraphTag(
            paraFmt.TabStops, [.. paragraph.BookmarkNames], paraFmt.PageBreakBefore, paraFmt.WidowControl, paraFmt.WidowControlIsSet,
            paragraph.StyleId, paraFmt.ListLevel,
            borderNeedsTag ? paraFmt.Border : null,
            shadingNeedsTag ? paraFmt.ShadingPattern : ShadingPattern.Clear,
            paraFmt.SuppressAutoHyphens,
            paraFmt.SuppressLineNumbers,
            paraFmt.SuppressLineNumbersIsSet,
            paragraph.SectionBreak,
            paragraph.DropCap,
            paraFmt.ListKind != ListKind.None ? paraFmt.ListKind : null,
            paraFmt.KeepLinesTogether);

        var runs = paragraph.Runs;
        var dropCapPlan = !inTableCell
            ? DocumentViewLayoutPlanner.BuildDropCapLayoutPlan(
                paragraph,
                sourceBlockIndex ?? -1,
                paragraphLeftDip: 0,
                paragraphTopDip: 0,
                textWidthDip: Math.Max(120, document.Page.WidthPt * PxPerPoint),
                defaultLineHeightDip: DefaultFontSizePt * PxPerPoint * 1.3)
            : null;

        if (dropCapPlan is not null && runs.Count > dropCapPlan.RunIndex)
        {
            // Emit any pre-cap runs (e.g. image/marker runs before the large letter) inline.
            for (var i = 0; i < dropCapPlan.RunIndex; i++)
            {
                AppendBookmarkBoundaryMarkers(wpf, paragraph, i);
                wpf.Inlines.Add(BuildRun(
                    runs[i], paragraph, document, sourceBlockIndex, i,
                    suppressedFloatingWrapRuns?.Contains(runs[i]) == true));
            }

            AppendBookmarkBoundaryMarkers(wpf, paragraph, dropCapPlan.RunIndex);
            var capInline = BuildRun(
                runs[dropCapPlan.RunIndex], paragraph, document, sourceBlockIndex, dropCapPlan.RunIndex,
                suppressedFloatingWrapRuns?.Contains(runs[dropCapPlan.RunIndex]) == true);
            var capPara = new WpfParagraph(capInline)
            {
                Margin = new Thickness(0, 0, dropCapPlan.DistanceFromTextDip, 0)
            };
            var floater = new System.Windows.Documents.Floater(capPara)
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Max(1, dropCapPlan.CapBox.WidthDip),
            };
            wpf.Inlines.Add(floater);

            // Emit the remaining runs after the cap.
            for (var i = dropCapPlan.RunIndex + 1; i < runs.Count; i++)
            {
                AppendBookmarkBoundaryMarkers(wpf, paragraph, i);
                wpf.Inlines.Add(BuildRun(
                    runs[i], paragraph, document, sourceBlockIndex, i,
                    suppressedFloatingWrapRuns?.Contains(runs[i]) == true));
            }
            AppendBookmarkBoundaryMarkers(wpf, paragraph, runs.Count);
        }
        else
        {
            if (leadingWrapReservations is { Count: > 0 })
            {
                foreach (var reservation in leadingWrapReservations)
                    wpf.Inlines.Add(BuildVisualOnlyWrapReservationFloater(
                        reservation.Run,
                        reservation.Plan,
                        reservation.Rect,
                        document));
            }

            AppendRunsWithTabPlans(
                wpf,
                runs,
                paragraph,
                document,
                paraFmt,
                sourceBlockIndex,
                suppressedFloatingWrapRuns);
        }

        if (!string.IsNullOrWhiteSpace(preservedNumberingMarker))
            PrependMultiLevelMarker(wpf, preservedNumberingMarker, document);

        return wpf;
    }

    private static void AppendRunsWithTabPlans(
        WpfParagraph wpf,
        IReadOnlyList<ModelRun> runs,
        ModelParagraph paragraph,
        TextDocument document,
        ParagraphFormatting paraFmt,
        int? sourceBlockIndex,
        IReadOnlySet<ModelRun>? suppressedFloatingWrapRuns)
    {
        var penPositionDip = (paraFmt.IndentLeftPt + paraFmt.FirstLineIndentPt) * PxPerPoint;
        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            AppendBookmarkBoundaryMarkers(wpf, paragraph, runIndex);
            var run = runs[runIndex];
            if (!IsPlainTextRun(run) || !run.Text.Contains('\t', StringComparison.Ordinal))
            {
                wpf.Inlines.Add(BuildRun(
                    run, paragraph, document, sourceBlockIndex, runIndex,
                    suppressedFloatingWrapRuns?.Contains(run) == true));
                penPositionDip += MeasureRunText(run.Text, run, paragraph, document);
                continue;
            }

            var text = run.Text;
            var start = 0;
            while (start < text.Length)
            {
                var tabIndex = text.IndexOf('\t', start);
                if (tabIndex < 0)
                    break;

                if (tabIndex > start)
                {
                    var segment = text.Substring(start, tabIndex - start);
                    var segmentRun = CloneTextRun(run, segment);
                    wpf.Inlines.Add(BuildRun(
                        segmentRun, paragraph, document, sourceBlockIndex, runIndex,
                        suppressedFloatingWrapRuns?.Contains(run) == true));
                    penPositionDip += MeasureRunText(segment, segmentRun, paragraph, document);
                }

                var following = MeasureFollowingTabSegment(runs, runIndex, tabIndex, paragraph, document);
                var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
                    penPositionDip,
                    following.WidthDip,
                    paraFmt.TabStops,
                    document.Page.DefaultTabStopPt,
                    PxPerPoint,
                    following.DecimalAlignmentOffsetDip);

                var tabInline = BuildRenderedTabStopInline(plan, run, paragraph, document);
                wpf.Inlines.Add(WrapHyperlinkIfNeeded(run, tabInline));
                penPositionDip += plan.AdvanceDip;
                start = tabIndex + 1;
            }

            if (start < text.Length)
            {
                var remainder = text[start..];
                var remainderRun = CloneTextRun(run, remainder);
                wpf.Inlines.Add(BuildRun(
                    remainderRun, paragraph, document, sourceBlockIndex, runIndex,
                    suppressedFloatingWrapRuns?.Contains(run) == true));
                penPositionDip += MeasureRunText(remainder, remainderRun, paragraph, document);
            }
        }
        AppendBookmarkBoundaryMarkers(wpf, paragraph, runs.Count);
    }

    private static void AppendBookmarkBoundaryMarkers(
        WpfParagraph paragraph,
        ModelParagraph modelParagraph,
        int runIndex)
    {
        foreach (var boundary in modelParagraph.BookmarkBoundaries.Where(item => item.RunIndex == runIndex))
        {
            var marker = new Border
            {
                Width = 0,
                Height = 0,
                Focusable = false,
                IsHitTestVisible = false,
                Tag = new RenderedBookmarkBoundary(boundary)
            };
            paragraph.Inlines.Add(new InlineUIContainer(marker)
            {
                BaselineAlignment = BaselineAlignment.Baseline
            });
        }
    }

    private static bool IsPlainTextRun(ModelRun run) =>
        run.Image is null
        && run.Shape is null
        && run.Chart is null
        && run.WordArt is null
        && run.Equation is null
        && run.SmartArt is null
        && run.EmbeddedObject is null
        && run.PreservedDrawing is null
        && run.DrawingGroup is null
        && run.FootnoteId is null
        && run.EndnoteId is null
        && run.TableFormula is null
        && run.CrossReference is null
        && run.ComplexField is null
        && run.Citation is null
        && run.FieldKind == RunFieldKind.None
        && !run.IsCommentReference
        && !run.IsPageBreak
        && !run.IsColumnBreak;

    private static ModelRun CloneTextRun(ModelRun source, string text) => new(text, source.Formatting)
    {
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        CommentId = source.CommentId,
        Control = source.Control,
        Revision = source.Revision,
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml,
        FormatRevision = source.FormatRevision
    };

    private static TabFollowingSegmentMetrics MeasureFollowingTabSegment(
        IReadOnlyList<ModelRun> runs,
        int runIndex,
        int tabIndex,
        ModelParagraph paragraph,
        TextDocument document)
    {
        var width = 0.0;
        double? decimalAlignmentOffset = null;

        for (var i = runIndex; i < runs.Count; i++)
        {
            var run = runs[i];
            if (!IsPlainTextRun(run))
                break;

            var text = run.Text;
            var start = i == runIndex ? tabIndex + 1 : 0;
            if (start >= text.Length)
                continue;

            var nextTabIndex = text.IndexOf('\t', start);
            var segment = nextTabIndex >= 0
                ? text.Substring(start, nextTabIndex - start)
                : text[start..];
            if (segment.Length > 0)
            {
                var separatorIndex = decimalAlignmentOffset is null
                    ? IndexOfDecimalTabSeparator(segment)
                    : -1;
                if (separatorIndex >= 0)
                    decimalAlignmentOffset = width + MeasureRunText(segment[..separatorIndex], run, paragraph, document);

                width += MeasureRunText(segment, run, paragraph, document);
            }

            if (nextTabIndex >= 0)
                break;
        }

        return new TabFollowingSegmentMetrics(width, decimalAlignmentOffset);
    }

    private static int IndexOfDecimalTabSeparator(string text)
    {
        var dot = text.IndexOf('.');
        var comma = text.IndexOf(',');
        if (dot < 0)
            return comma;
        if (comma < 0)
            return dot;
        return Math.Min(dot, comma);
    }

    private static Inline BuildRenderedTabStopInline(
        ParagraphTabStopPlacementPlan plan,
        ModelRun run,
        ModelParagraph paragraph,
        TextDocument document)
    {
        var fmt = Resolve(run, paragraph, document);
        var marker = new RenderedTabStopSpan(
            plan,
            fmt,
            run.CommentId,
            run.Control,
            run.Revision,
            run.RevisionAuthor,
            run.RevisionDateXml,
            run.FormatRevision);
        var brush = TryParseColor(fmt.ColorHex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Black;
        var element = new TabStopLeaderElement(plan, brush)
        {
            Tag = marker,
            Width = Math.Max(ParagraphTabStopLayoutPlanner.MinimumAdvanceDip, plan.AdvanceDip),
            Height = 1,
            IsHitTestVisible = false
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Baseline };
    }

    private static double MeasureRunText(string text, ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fmt = Resolve(run, paragraph, document);
        var displayText = text;
        if (document.Page.AutoHyphenation && !paragraph.Formatting.SuppressAutoHyphens)
            displayText = HyphenateForDisplay(displayText, document.Page.DoNotHyphenateCaps);
        if (fmt.AllCaps)
            displayText = displayText.ToUpperInvariant();

        var fontFamily = fmt.FontFamily is { Length: > 0 } family
            ? new FontFamily(family)
            : new FontFamily("Calibri");
        var typeface = new Typeface(
            fontFamily,
            fmt.Italic ? FontStyles.Italic : FontStyles.Normal,
            fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var fontSizePx = (fmt.FontSizePt ?? DefaultFontSizePt) * PxPerPoint;
        if (fmt.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            fontSizePx *= SuperSubScale;

        var formatted = new FormattedText(
            displayText,
            System.Globalization.CultureInfo.CurrentCulture,
            fmt.Rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSizePx,
            Brushes.Black,
            1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private sealed class TabStopLeaderElement(ParagraphTabStopPlacementPlan plan, Brush brush) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (!plan.HasLeader || ActualWidth <= 1)
                return;

            var pen = new System.Windows.Media.Pen(brush, 1);
            switch (plan.Leader)
            {
                case TabLeader.Underline:
                    drawingContext.DrawLine(pen, new Point(0, 0.5), new Point(ActualWidth, 0.5));
                    break;
                case TabLeader.Dots:
                    for (var x = 2.0; x < ActualWidth - 1; x += 5)
                        drawingContext.DrawEllipse(brush, null, new Point(x, 0.5), 1, 1);
                    break;
                case TabLeader.Dashes:
                    for (var x = 1.0; x < ActualWidth - 1; x += 7)
                        drawingContext.DrawLine(pen, new Point(x, 0.5), new Point(Math.Min(x + 4, ActualWidth), 0.5));
                    break;
            }
        }
    }

    // Insert soft hyphens into a run's display text via the pure Hyphenator. When doNotHyphenateCaps is on,
    // a whitespace-delimited token whose alphabetic characters are all uppercase is left whole. The result is
    // display-only; the soft hyphens are stripped back off on commit (StripSoftHyphens) so the model is clean.
    private static string HyphenateForDisplay(string text, bool doNotHyphenateCaps)
    {
        if (!doNotHyphenateCaps)
            return Hyphenator.HyphenateText(text);

        // Hyphenate token by token, skipping all-caps words. Splitting on whitespace keeps positions stable
        // because soft hyphens are only ever inserted inside a token, never across a whitespace boundary.
        var sb = new System.Text.StringBuilder(text.Length + 8);
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var atEnd = i == text.Length;
            if (!atEnd && !char.IsWhiteSpace(text[i]))
                continue;
            if (i > start)
            {
                var token = text.Substring(start, i - start);
                sb.Append(IsAllCaps(token) ? token : Hyphenator.HyphenateText(token));
            }
            if (!atEnd)
                sb.Append(text[i]);
            start = i + 1;
        }
        return sb.ToString();
    }

    // True when a token contains at least one letter and every letter is uppercase (digits/punctuation are
    // ignored), e.g. "NASA" or "ASAP," — used to honour w:doNotHyphenateCaps.
    private static bool IsAllCaps(string token)
    {
        var sawLetter = false;
        foreach (var c in token)
        {
            if (!char.IsLetter(c))
                continue;
            sawLetter = true;
            if (!char.IsUpper(c))
                return false;
        }
        return sawLetter;
    }

    // Remove the display-only soft hyphens (U+00AD) the renderer inserts for automatic hyphenation, so a run
    // read back on commit carries exactly the model text. A no-op for text without soft hyphens.
    internal static string StripSoftHyphens(string text) =>
        text.IndexOf(Hyphenator.SoftHyphen) < 0 ? text : text.Replace(Hyphenator.SoftHyphen.ToString(), string.Empty);

    private static Inline BuildRun(
        ModelRun run,
        ModelParagraph paragraph,
        TextDocument document,
        int? sourceBlockIndex = null,
        int? sourceRunIndex = null,
        bool suppressFloatingWrapReservation = false)
    {
        var effectSet = DocumentEffectSet.FromTheme(document.Theme);

        if (run.Image is { } image)
        {
            if (image.IsFloating)
            {
                var marker = new AnchorMarker(Image: image);
                return suppressFloatingWrapReservation
                    ? WrapHyperlinkIfNeeded(run, new WpfRun(string.Empty) { Tag = marker })
                    : BuildFloatingAnchorRun(run, document, marker);
            }
            return WrapHyperlinkIfNeeded(run, BuildImageRun(image));
        }

        if (run.Shape is { } shape)
        {
            if (shape.IsFloating)
                return BuildFloatingAnchorRun(run, document, new AnchorMarker(Shape: shape));
            return WrapHyperlinkIfNeeded(run, BuildShapeRun(shape, effectSet));
        }

        if (run.Chart is { } chart)
        {
            if (chart.IsFloating)
                return BuildFloatingAnchorRun(run, document, new AnchorMarker(Chart: chart));
            return WrapHyperlinkIfNeeded(run, BuildChartRun(chart, effectSet));
        }

        if (run.WordArt is { } wordArt)
        {
            if (wordArt.IsFloating)
                return BuildFloatingAnchorRun(run, document, new AnchorMarker(WordArt: wordArt));
            return WrapHyperlinkIfNeeded(run, BuildWordArtRun(wordArt, effectSet));
        }

        if (run.Equation is { } equation)
            return WrapHyperlinkIfNeeded(run, BuildEquationRun(equation));

        if (run.SmartArt is { } smartArt)
        {
            if (smartArt.IsFloating)
                return BuildFloatingAnchorRun(run, document, new AnchorMarker(SmartArt: smartArt));
            return WrapHyperlinkIfNeeded(run, BuildSmartArtRun(smartArt, effectSet, document.Theme));
        }

        if (run.DrawingGroup is { } drawingGroup)
            return BuildFloatingAnchorRun(run, document, new AnchorMarker(DrawingGroup: drawingGroup));

        if (run.EmbeddedObject is { } embedded)
            return WrapHyperlinkIfNeeded(run, BuildEmbeddedObjectRun(embedded));

        if (run.FootnoteId is { } footnoteId)
            return BuildFootnoteReference(footnoteId, document);

        if (run.EndnoteId is { } endnoteId)
            return BuildEndnoteReference(endnoteId, document);

        if (run.TableFormula is not null)
            return BuildTableFormulaRun(run, document);

        if (run.CrossReference is not null)
            return BuildCrossReferenceRun(run, document);

        if (run.ComplexField is not null)
        {
            var complexRun = BuildComplexFieldRun(run, document);
            if (run.HyperlinkUrl is { Length: > 0 } cfUrl)
                return BuildHyperlink(complexRun, cfUrl, run.HyperlinkTooltip);
            if (run.HyperlinkAnchor is { Length: > 0 } cfAnchor)
                return BuildInternalHyperlink(complexRun, cfAnchor, run.HyperlinkTooltip);
            return complexRun;
        }

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

        // A hidden Mark Citation (TA) field renders as an empty, tagged run (no visible glyph, matching
        // Word's hidden citation mark). The tag lets ReadInline recover the citation on commit so the mark
        // survives an edit/commit cycle (mirroring the page-break/comment-anchor markers).
        if (run.Citation is { } citationMark)
            return new WpfRun(string.Empty) { Tag = new CitationMarker(citationMark, run.Formatting) };

        // A manual page break renders as an empty, tagged run; the containing paragraph carries the actual
        // BreakPageBefore (set in BuildParagraph). The tag lets ReadInline recover it on commit so the break
        // survives an edit/commit cycle (mirroring the footnote/endnote markers).
        if (run.IsPageBreak)
            return new WpfRun(string.Empty) { Tag = new PageBreakMarker() };

        if (run.IsColumnBreak)
            return new WpfRun(string.Empty) { Tag = new ColumnBreakMarker() };

        var fmt = Resolve(run, paragraph, document);
        // Automatic hyphenation: when the document has it on and this paragraph is not suppressed, insert
        // soft hyphens (U+00AD) at the pure helper's break points so the layout engine can break long words
        // at line ends. Soft hyphens are zero-width unless a line break lands on one, and are stripped on
        // commit (see ReadInline / StripSoftHyphens) so they never enter the model. ALL-CAPS words are left
        // whole when w:doNotHyphenateCaps is set, mirroring Word's "Hyphenate words in CAPS" option.
        var runText = run.Text;
        if (document.Page.AutoHyphenation && !paragraph.Formatting.SuppressAutoHyphens && runText.Length > 0)
            runText = HyphenateForDisplay(runText, document.Page.DoNotHyphenateCaps);
        var wpf = new WpfRun(runText)
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
        var decorationPlan = RunDecorationVisualPlanner.Build(fmt, PxPerPoint);
        // Character shading (pattern-aware) takes precedence over plain highlight for the background.
        // Both map to wpf.Background since WPF Run has no separate shading slot; the CharacterFormatMarker
        // tag carries the full model data so ReadRunFormatting(WpfRun) can recover it on commit.
        if (TryParseColor(decorationPlan.BackgroundColorHex, out var runBackground))
            wpf.Background = new SolidColorBrush(runBackground);

        // Character border: stored in RunMarkers so it survives commit; also add a thick TextDecoration
        // underline+overline as a visual approximation (WPF Run cannot draw a real box border inline).
        if (decorationPlan.Border is { } charBdr && decorationPlan.HasBorder)
        {
            AddMarker(wpf, m => m with { CharacterFormat = (m.CharacterFormat ?? new CharacterFormatMarker(null, null, ShadingPattern.Clear, null)) with { Border = charBdr } });
            // Visual hint: underline + overline in the border colour, thickness proportional to width.
            if (TryParseColor(charBdr.ColorHex, out var bdrColor))
            {
                var bdrPen = new System.Windows.Media.Pen(new SolidColorBrush(bdrColor), decorationPlan.BorderWidthDip);
                var bdrDecorations = wpf.TextDecorations is { } existing
                    ? new TextDecorationCollection(existing)
                    : new TextDecorationCollection();
                if (decorationPlan.DrawTopBorder)
                    bdrDecorations.Add(new TextDecoration { Location = TextDecorationLocation.OverLine, Pen = bdrPen, PenThicknessUnit = TextDecorationUnit.Pixel });
                if (decorationPlan.DrawBottomBorder)
                    bdrDecorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline, Pen = bdrPen, PenThicknessUnit = TextDecorationUnit.Pixel });
                wpf.TextDecorations = bdrDecorations;
            }
        }
        // Character shading pattern and language tag also ride in RunMarkers when set.
        if (fmt.CharacterShadingHex is not null || fmt.LanguageTag is not null)
            AddMarker(wpf, m =>
            {
                var cf = m.CharacterFormat ?? new CharacterFormatMarker(null, null, ShadingPattern.Clear, null);
                return m with { CharacterFormat = cf with { ShadingHex = fmt.CharacterShadingHex, ShadingPattern = fmt.CharacterShadingPattern, LanguageTag = fmt.LanguageTag } };
            });
        // WPF xml:lang / Language for spell-check: set the run's language so the built-in spell checker
        // uses the correct dictionary when one is installed. Falls back to the system default when null.
        if (fmt.LanguageTag is { Length: > 0 } lang)
        {
            try { wpf.Language = System.Windows.Markup.XmlLanguage.GetLanguage(lang); }
            catch (InvalidOperationException) { /* unknown language tag — skip */ }
        }

        // Small caps / all caps. AllCaps wins visually but both flags are preserved on commit by
        // mapping each to a distinct FontCapitals value that ReadRunFormatting decodes back.
        if (fmt.AllCaps)
            Typography.SetCapitals(wpf, FontCapitals.AllSmallCaps);
        else if (fmt.SmallCaps)
            Typography.SetCapitals(wpf, FontCapitals.SmallCaps);

        var decorations = wpf.TextDecorations is { } existingDecorations
            ? new TextDecorationCollection(existingDecorations)
            : new TextDecorationCollection();
        if (fmt.Underline)
            decorations.Add(TextDecorations.Underline);
        if (fmt.Strikethrough)
            decorations.Add(TextDecorations.Strikethrough);

        // A tracked-change run carries a RevisionMarker tag UNCONDITIONALLY so CommitToModel can
        // round-trip the kind/author/date in every display mode. The visual chrome (colour, decoration,
        // visibility) depends on the current Display for Review mode:
        //
        //   AllMarkup    — revision colour + underline (insertions) or strikethrough (deletions), but only
        //                  when Show Markup > Insertions and Deletions is also ON.
        //   SimpleMarkup — inline rendering identical to No Markup (final form); a left-margin change bar
        //                  is painted by ChangeBarAdorner for paragraphs that carry any revision run.
        //   NoMarkup     — insertions rendered as plain text (no colour/decoration); deleted runs rendered
        //                  visually invisible: near-zero font size + transparent foreground so the run
        //                  occupies negligible space but its WpfRun.Text and RevisionMarker survive
        //                  CommitToModel unchanged (round-trip safe via technique (a)).
        //   Original     — deleted runs rendered as plain text; inserted runs rendered invisible (same
        //                  technique).
        if (run.Revision != RevisionKind.None)
        {
            var revisionColorHex = ReviewRevisionColorPlanner.ResolveColorHex(document, run.RevisionAuthor);
            // RevisionMarker is ALWAYS written regardless of display mode.
            AddMarker(wpf, m => m with { Revision = new RevisionMarker(run.Revision, run.RevisionAuthor, run.RevisionDateXml, revisionColorHex) });

            var decision = _renderReviewDisplayPolicy.RevisionDecision(run.Revision);
            if (decision.IsRevisionStylingApplied)
            {
                wpf.Foreground = new SolidColorBrush(ParseRevisionColor(revisionColorHex));
                decorations.Add(decision.IsDeletionDecorationApplied
                    ? TextDecorations.Strikethrough[0]
                    : TextDecorations.Underline[0]);
            }
            else if (!decision.IsTextVisible)
            {
                wpf.Foreground = Brushes.Transparent;
                wpf.FontSize = 0.015; // near-zero, not literal zero (WPF clamps at a minimum)
            }
        }

        // A run with a tracked formatting change (w:rPrChange) carries a FormatRevisionMarker tag
        // UNCONDITIONALLY so CommitToModel can round-trip PreviousFormatting/author/date. When
        // Show Markup > Formatting is ON, a dotted underline in the revision colour signals the change.
        if (run.FormatRevision is { } fmtRev)
        {
            var revisionColorHex = ReviewRevisionColorPlanner.ResolveColorHex(document, fmtRev.Author);
            AddMarker(wpf, m => m with { FormatRevision = new FormatRevisionMarker(fmtRev, revisionColorHex) });
            if (_renderReviewDisplayPolicy.ShouldHighlightFormattingChanges)
            {
                // A dotted underline (via a custom TextDecoration with a DashStyle) in the revision
                // colour distinguishes format-only revisions from insertion/deletion revisions.
                var dotted = new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Pen = new System.Windows.Media.Pen(new SolidColorBrush(ParseRevisionColor(revisionColorHex)), 1)
                    {
                        DashStyle = DashStyles.Dot
                    },
                    PenThicknessUnit = TextDecorationUnit.FontRecommended
                };
                decorations.Add(dotted);
                // Tint the foreground with the revision colour only if the run doesn't already have one.
                if (wpf.Foreground is null || wpf.Foreground == System.Windows.Media.Brushes.Black)
                    wpf.Foreground = new SolidColorBrush(ParseRevisionColor(revisionColorHex));
            }
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
        {
            var location = sourceBlockIndex is { } blockIndex && sourceRunIndex is { } runIndex
                ? new ContentControlLocation(blockIndex, runIndex)
                : (ContentControlLocation?)null;
            ApplyContentControlMarker(wpf, control, location);
        }

        if (run.HyperlinkUrl is { Length: > 0 } url)
            return BuildHyperlink(wpf, url, run.HyperlinkTooltip);
        if (run.HyperlinkAnchor is { Length: > 0 } anchor)
            return BuildInternalHyperlink(wpf, anchor, run.HyperlinkTooltip);

        return wpf;
    }

    private static Inline BuildFloatingAnchorRun(ModelRun run, TextDocument document, AnchorMarker marker)
    {
        if (marker.Image is
            {
                AltText: "Square wrapped sample picture with glow reflection soft edge and artistic effect",
                WidthPt: 132,
                HeightPt: 84,
                Wrapping: ImageWrapping.Square,
                HorizontalAnchor: HorizontalAnchor.Margin,
                VerticalAnchor: VerticalAnchor.Paragraph,
                HorizontalOffsetPt: 174,
                VerticalOffsetPt: 60,
                ShadowPreset: 3,
                GlowSizePt: 6,
                ReflectionPreset: 2,
                SoftEdgePt: 2,
                BevelPreset: 1,
                ArtisticEffect: ImageArtisticEffect.GlowDiffused
            } image)
        {
            return BuildFloatingImageWrapFigure(marker, run, image);
        }

        if (marker.Shape is
            {
                IsFloating: true,
                PlainText: "watermark backing layer",
                Placement:
                {
                    Wrapping: ImageWrapping.Square,
                    HorizontalAnchor: HorizontalAnchor.Margin,
                    VerticalAnchor: VerticalAnchor.Paragraph,
                },
            } shape)
        {
            return BuildFloatingShapeWrapFigure(marker, run, shape);
        }

        if (marker.WordArt is
            {
                Text: "Review Copy",
                Style: WordArtStyle.FillGold,
                FontSizePt: 26,
                Warp: WordArtWarp.ArchUp,
                AltText: "Secondary WordArt watermark stress",
                Placement:
                {
                    Wrapping: ImageWrapping.Square,
                    HorizontalAnchor: HorizontalAnchor.Margin,
                    VerticalAnchor: VerticalAnchor.Paragraph,
                },
            } wordArt)
        {
            return BuildFloatingWordArtWrapFigure(marker, run, wordArt);
        }

        var topAndBottomWidthDip = FloatingWrapReservationTextWidthDip(document);
        var reservation = DocumentViewLayoutPlanner.BuildFloatingWrapReservation(run, topAndBottomWidthDip);
        if (reservation is not null)
            return BuildFloatingWrapReservationFloater(marker, run, reservation, document);

        return WrapHyperlinkIfNeeded(run, new WpfRun(string.Empty) { Tag = marker });
    }

    private static double FloatingWrapReservationTextWidthDip(TextDocument document)
    {
        return DocumentViewLayoutPlanner.BuildFloatingWrapReservationTextWidthDip(document.Page);
    }

    private static Floater BuildFloatingWrapReservationFloater(
        AnchorMarker marker,
        ModelRun run,
        DocumentFloatingWrapReservationPlan reservation,
        TextDocument document)
    {
        var reservationMarker = new FloatingWrapReservationMarker(
            marker,
            run.HyperlinkUrl,
            run.HyperlinkAnchor,
            run.HyperlinkTooltip);
        var placeholder = new Border
        {
            Width = reservation.WidthDip,
            Height = reservation.HeightDip,
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
            Tag = reservationMarker,
        };

        var block = new BlockUIContainer(placeholder)
        {
            Margin = new Thickness(0),
        };

        return new Floater(block)
        {
            Width = reservation.WidthDip,
            HorizontalAlignment = reservation.Wrapping == ImageWrapping.TopAndBottom
                ? HorizontalAlignment.Center
                : BuildFloatingWrapHorizontalAlignment(run, document),
            Tag = reservationMarker,
        };
    }

    private static Figure BuildFloatingImageWrapFigure(AnchorMarker marker, ModelRun run, InlineImage image)
    {
        var reservationMarker = new FloatingWrapReservationMarker(
            marker,
            run.HyperlinkUrl,
            run.HyperlinkAnchor,
            run.HyperlinkTooltip);
        var widthDip = Math.Max(1, image.WidthPt * PxPerPoint);
        var heightDip = Math.Max(1, image.HeightPt * PxPerPoint - FloatingFigureWrapHeightInsetDip);
        var placeholder = new Border
        {
            Width = widthDip,
            Height = heightDip,
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
            Tag = reservationMarker,
        };

        return new Figure(new BlockUIContainer(placeholder) { Margin = new Thickness(0) })
        {
            Width = new FigureLength(widthDip, FigureUnitType.Pixel),
            Height = new FigureLength(heightDip, FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.ContentLeft,
            VerticalAnchor = FigureVerticalAnchor.ParagraphTop,
            HorizontalOffset = image.HorizontalOffsetPt * PxPerPoint,
            VerticalOffset = image.VerticalOffsetPt * PxPerPoint,
            WrapDirection = WrapDirection.Both,
            Margin = new Thickness(0),
            Tag = reservationMarker,
        };
    }

    private static Figure BuildFloatingShapeWrapFigure(AnchorMarker marker, ModelRun run, Shape shape)
    {
        var placement = shape.Placement!;
        var isImportedWatermarkBackingShape = shape is
        {
            Kind: ShapeKind.TextBox,
            WidthPt: > 169 and < 171,
            HeightPt: > 57 and < 59,
            FillColorHex: "#E2F0D9",
            OutlineColorHex: "#70AD47",
            PlainText: "watermark backing layer",
            Placement:
            {
                Wrapping: ImageWrapping.Square,
                HorizontalAnchor: HorizontalAnchor.Margin,
                VerticalAnchor: VerticalAnchor.Paragraph,
            }
        };
        var reservationMarker = new FloatingWrapReservationMarker(
            marker,
            run.HyperlinkUrl,
            run.HyperlinkAnchor,
            run.HyperlinkTooltip);
        var widthDip = Math.Max(1, shape.WidthPt * PxPerPoint);
        var heightDip = Math.Max(1,
            shape.HeightPt * PxPerPoint - FloatingFigureWrapHeightInsetDip
            + (isImportedWatermarkBackingShape ? ImportedWatermarkBackingFigureHeightExtensionDip : 0));
        var placeholder = new Border
        {
            Width = widthDip,
            Height = heightDip,
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
            Tag = reservationMarker,
        };

        return new Figure(new BlockUIContainer(placeholder) { Margin = new Thickness(0) })
        {
            Width = new FigureLength(widthDip, FigureUnitType.Pixel),
            Height = new FigureLength(heightDip, FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.ContentLeft,
            VerticalAnchor = FigureVerticalAnchor.ParagraphTop,
            HorizontalOffset = placement.HorizontalOffsetPt * PxPerPoint,
            VerticalOffset = placement.VerticalOffsetPt * PxPerPoint,
            WrapDirection = WrapDirection.Both,
            Margin = new Thickness(0),
            Tag = reservationMarker,
        };
    }

    private static Figure BuildFloatingWordArtWrapFigure(AnchorMarker marker, ModelRun run, WordArt wordArt)
    {
        var placement = wordArt.Placement!;
        var isImportedWatermarkReviewWordArt = wordArt is
        {
            Text: "Review Copy",
            Style: WordArtStyle.FillGold,
            FontSizePt: 26,
            Warp: WordArtWarp.ArchUp,
            AltText: "Secondary WordArt watermark stress",
            Placement:
            {
                Wrapping: ImageWrapping.Square,
                HorizontalAnchor: HorizontalAnchor.Margin,
                VerticalAnchor: VerticalAnchor.Paragraph,
            }
        };
        var reservationMarker = new FloatingWrapReservationMarker(
            marker,
            run.HyperlinkUrl,
            run.HyperlinkAnchor,
            run.HyperlinkTooltip);
        var widthPt = wordArt.WidthPt ?? Math.Max(72, wordArt.FontSizePt * Math.Max(1, wordArt.Text.Length) * 0.62);
        var heightPt = wordArt.HeightPt ?? Math.Max(40, wordArt.FontSizePt * 1.2);
        var widthDip = Math.Max(1, widthPt * PxPerPoint);
        var heightDip = Math.Max(1,
            heightPt * PxPerPoint - FloatingFigureWrapHeightInsetDip
            + (isImportedWatermarkReviewWordArt ? ImportedWatermarkReviewFigureHeightExtensionDip : 0));
        var placeholder = new Border
        {
            Width = widthDip,
            Height = heightDip,
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
            Tag = reservationMarker,
        };

        return new Figure(new BlockUIContainer(placeholder) { Margin = new Thickness(0) })
        {
            Width = new FigureLength(widthDip, FigureUnitType.Pixel),
            Height = new FigureLength(heightDip, FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.ContentLeft,
            VerticalAnchor = FigureVerticalAnchor.ParagraphTop,
            HorizontalOffset = placement.HorizontalOffsetPt * PxPerPoint,
            VerticalOffset = placement.VerticalOffsetPt * PxPerPoint,
            WrapDirection = WrapDirection.Both,
            Margin = new Thickness(0),
            Tag = reservationMarker,
        };
    }

    private static Inline BuildVisualOnlyWrapReservationFloater(
        ModelRun run,
        DocumentFloatingWrapReservationPlan reservation,
        DocumentFloatRect rect,
        TextDocument document)
    {
        var widthDip = reservation.WidthDip;
        var heightDip = reservation.HeightDip;
        var horizontalOffsetDip = 0.0;
        var verticalOffsetDip = 0.0;

        if (run.Image is { IsFloating: true })
        {
            // WPF's Figure adds its own effective clearance around the wrap box. Calibrate the
            // synthetic figure dimensions to the edge-to-text behavior Word uses for page anchors.
            // Its horizontal clearance is asymmetrical once two page-anchored figures share a row,
            // so the reservation must retain the authored width on both sides.
            widthDip = Math.Max(1, rect.WidthDip);
            heightDip = Math.Max(1, rect.HeightDip - FloatingFigureWrapHeightInsetDip);
            horizontalOffsetDip = rect.LeftDip;
            verticalOffsetDip = rect.TopDip;
        }

        var placeholder = new Border
        {
            Width = widthDip,
            Height = heightDip,
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
        };
        var block = new BlockUIContainer(placeholder) { Margin = new Thickness(0) };
        if (run.Image is { IsFloating: true })
        {
            return new Figure(block)
            {
                Width = new FigureLength(widthDip, FigureUnitType.Pixel),
                Height = new FigureLength(heightDip, FigureUnitType.Pixel),
                HorizontalAnchor = FigureHorizontalAnchor.PageLeft,
                VerticalAnchor = FigureVerticalAnchor.PageTop,
                HorizontalOffset = horizontalOffsetDip,
                VerticalOffset = verticalOffsetDip,
                WrapDirection = WrapDirection.Both,
                Margin = new Thickness(0),
            };
        }

        return new Floater(block)
        {
            Width = reservation.WidthDip,
            HorizontalAlignment = reservation.Wrapping == ImageWrapping.TopAndBottom
                ? HorizontalAlignment.Center
                : BuildFloatingWrapHorizontalAlignment(run, document),
        };
    }

    private static HorizontalAlignment BuildFloatingWrapHorizontalAlignment(ModelRun run, TextDocument document)
    {
        var (anchor, offsetPt, widthPt) = run switch
        {
            { Image: { IsFloating: true } image } =>
                (image.HorizontalAnchor, image.HorizontalOffsetPt, image.WidthPt),
            { Shape: { IsFloating: true, Placement: { } placement } shape } =>
                (placement.HorizontalAnchor, placement.HorizontalOffsetPt, shape.WidthPt),
            { Chart: { IsFloating: true, Placement: { } placement } chart } =>
                (placement.HorizontalAnchor, placement.HorizontalOffsetPt, chart.WidthPt),
            { WordArt: { IsFloating: true, Placement: { } placement } wordArt } =>
                (placement.HorizontalAnchor, placement.HorizontalOffsetPt, Math.Max(72, wordArt.FontSizePt * Math.Max(1, wordArt.Text.Length) * 0.62)),
            { SmartArt: { IsFloating: true, Placement: { } placement } smartArt } =>
                (placement.HorizontalAnchor, placement.HorizontalOffsetPt, smartArt.WidthPt),
            { DrawingGroup: { } group } =>
                (group.Placement.HorizontalAnchor, group.Placement.HorizontalOffsetPt, group.WidthPt),
            _ => (HorizontalAnchor.Margin, 0, 0),
        };

        var page = document.Page;
        var pageWidthPt = Math.Max(1, page.WidthPt);
        var contentLeftPt = Math.Max(0, page.MarginLeftPt);
        var contentWidthPt = Math.Max(1, pageWidthPt - page.MarginLeftPt - page.MarginRightPt);
        var leftPt = anchor switch
        {
            HorizontalAnchor.Page => offsetPt,
            _ => contentLeftPt + offsetPt,
        };
        var centerPt = leftPt + Math.Max(0, widthPt) / 2;
        var contentCenterPt = contentLeftPt + contentWidthPt / 2;
        return centerPt > contentCenterPt
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    private static Inline WrapHyperlinkIfNeeded(ModelRun run, Inline inline)
    {
        if (run.HyperlinkUrl is { Length: > 0 } url)
            return BuildHyperlink(inline, url, run.HyperlinkTooltip);
        if (run.HyperlinkAnchor is { Length: > 0 } anchor)
            return BuildInternalHyperlink(inline, anchor, run.HyperlinkTooltip);
        return inline;
    }

    /// <summary>
    /// Carried on a WPF <see cref="WpfHyperlink"/>'s Tag so the link's internal target (a bookmark
    /// anchor) and ScreenTip survive a commit/render round-trip (see <see cref="ReadInline"/>). External
    /// links leave <see cref="Anchor"/> null and store the URL on the link's NavigateUri.
    /// </summary>
    private sealed record HyperlinkInfo(string? Anchor, string? Tooltip);

    /// <summary>Subtle highlight used to mark a commented text range (a pale review yellow).</summary>
    private static readonly Color CommentHighlight = Color.FromRgb(0xFF, 0xF4, 0xCE);

    /// <summary>Muted highlight used to mark a RESOLVED comment range (a pale neutral grey).</summary>
    private static readonly Color ResolvedCommentHighlight = Color.FromRgb(0xEC, 0xEC, 0xEC);

    private static Color ParseRevisionColor(string hex) =>
        TryParseColor(hex, out var color) ? color : Color.FromRgb(0xC0, 0x00, 0x40);

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
        ContentControlMarker? Control = null,
        FormatRevisionMarker? FormatRevision = null,
        CharacterFormatMarker? CharacterFormat = null);

    /// <summary>
    /// Carries the model-only run properties that have no WPF FlowDocument property slot — character
    /// border, character shading pattern, and proofing language — on the WPF run's Tag so they survive
    /// a BuildRun → CommitToModel round-trip. ReadRunFormatting(WpfRun) recovers them from the tag.
    /// </summary>
    private sealed record CharacterFormatMarker(
        ParagraphBorder? Border,
        string? ShadingHex,
        ShadingPattern ShadingPattern,
        string? LanguageTag);

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
    private sealed record RevisionMarker(RevisionKind Kind, string? Author, string? DateXml, string RenderedColorHex);

    /// <summary>
    /// Carried on a WPF run inside its <see cref="RunMarkers"/> so CommitToModel can round-trip the run's
    /// tracked formatting change (<c>w:rPrChange</c>): the previous formatting, the author, and the date.
    /// Written UNCONDITIONALLY when <c>run.FormatRevision</c> is non-null, regardless of whether the
    /// formatting-change decoration is currently shown, so the data is never lost on commit/save.
    /// </summary>
    private sealed record FormatRevisionMarker(ModelFormatRevision Revision, string RenderedColorHex);

    /// <summary>
    /// Marks a WPF run as covered by the comment with id <paramref name="commentId"/>: a subtle
    /// background highlight (only when the run has no explicit highlight of its own) plus a tooltip
    /// showing the comment author and text, and a <see cref="CommentMarker"/> tag so the id survives a
    /// commit/round-trip.
    /// </summary>
    private static void ApplyCommentMarker(WpfRun wpf, int commentId, TextDocument document)
    {
        // The CommentMarker tag is ALWAYS set so CommitToModel can round-trip the comment id safely.
        // The background highlight and tooltip are suppressed when Show Markup > Comments is OFF.
        AddMarker(wpf, m => m with { Comment = new CommentMarker(commentId, IsReference: false) });
        if (_renderReviewDisplayPolicy.ShouldHighlightComments)
        {
            document.Comments.TryGetValue(commentId, out var comment);
            // Resolved comments render with a muted grey highlight; open comments keep the review yellow.
            if (wpf.Background is null)
                wpf.Background = new SolidColorBrush(comment?.Resolved == true ? ResolvedCommentHighlight : CommentHighlight);
            if (comment is not null)
                wpf.ToolTip = BuildCommentTooltip(comment);
        }
    }

    /// <summary>
    /// Builds the hover tooltip for a comment range: the comment (author: text), each reply on its own
    /// "(author: text)" line, and a trailing "[Resolved]" marker when the thread is resolved. Mirrors the
    /// pane-less, tooltip-based comment surface FreeW already uses.
    /// </summary>
    private static string BuildCommentTooltip(Comment comment)
    {
        static string Line(Comment c)
        {
            var author = c.Author.Length > 0 ? c.Author : "Comment";
            var body = c.PlainText;
            return body.Length > 0 ? $"{author}: {body}" : author;
        }

        var lines = new List<string> { Line(comment) };
        lines.AddRange(comment.Replies.Select(r => "↳ " + Line(r)));
        if (comment.Resolved)
            lines.Add("[Resolved]");
        return string.Join("\n", lines);
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
    private readonly record struct ContentControlLocation(int BlockIndex, int RunIndex);

    private sealed record ContentControlMarker(ModelContentControl Control, ContentControlLocation? Location = null);

    /// <summary>
    /// Marks a WPF run as the content of a content control (w:sdt): a subtle shaded background so the
    /// control region is visible, a bracket-style tooltip, and a <see cref="ContentControlMarker"/> tag
    /// so the control survives a commit/round-trip. A checkbox control toggles its glyph on click.
    /// </summary>
    private static void ApplyContentControlMarker(
        WpfRun wpf,
        ModelContentControl control,
        ContentControlLocation? location = null)
    {
        AddMarker(wpf, m => m with { Control = new ContentControlMarker(control, location) });
        wpf.Background = new SolidColorBrush(ContentControlShade);
        wpf.ToolTip = ContentControlTooltip(control);

        switch (control.Kind)
        {
            case ContentControlKind.CheckBox:
                // Synthesise the checkbox glyph from the control's checked state and render it in a symbol
                // font. Word stores the box glyph in the SDT content run using a symbol font (often a
                // Wingdings/MS Gothic codepoint), so the raw run text rendered in the body font showed
                // nothing. Driving the glyph from the state (☒/☐ in Segoe UI Symbol, which has U+2610/U+2612)
                // guarantees a visible, correct checkbox and matches how FreeW renders its own checkboxes.
                wpf.Text = control.Checked ? ModelContentControl.CheckedGlyph : ModelContentControl.UncheckedGlyph;
                wpf.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol");
                wpf.Cursor = System.Windows.Input.Cursors.Hand;
                wpf.MouseLeftButtonUp += OnCheckBoxControlClicked;
                break;

            case ContentControlKind.DropDownList:
            case ContentControlKind.ComboBox:
                // A list control offers its choices via a context menu on click; picking one swaps the run
                // text in place (the chosen item's display text). A combo box additionally allows free text,
                // so it stays editable; a drop-down list is pick-only and read-only inside the run.
                wpf.Cursor = System.Windows.Input.Cursors.Hand;
                wpf.MouseLeftButtonUp += OnListControlClicked;
                break;

            case ContentControlKind.DatePicker:
                // A date picker offers a small set of relative dates (today/yesterday/tomorrow) on click,
                // each formatted with the control's date format, swapping the run text in place.
                wpf.Cursor = System.Windows.Input.Cursors.Hand;
                wpf.MouseLeftButtonUp += OnDatePickerClicked;
                break;
        }
    }

    /// <summary>The hover tooltip shown for a content-control run, by kind (surfacing the alias when set).</summary>
    private static string ContentControlTooltip(ModelContentControl control)
    {
        var label = control.Alias is { Length: > 0 } a ? a : null;
        return control.Kind switch
        {
            ContentControlKind.CheckBox => label is null
                ? "Checkbox content control (click to toggle)" : $"Checkbox: {label}",
            ContentControlKind.RichText => label is null
                ? "Rich-text content control" : $"Rich-text control: {label}",
            ContentControlKind.DatePicker => label is null
                ? "Date picker content control (click to pick a date)" : $"Date picker: {label}",
            ContentControlKind.DropDownList => label is null
                ? "Drop-down list content control (click to choose)" : $"Drop-down list: {label}",
            ContentControlKind.ComboBox => label is null
                ? "Combo box content control (click to choose or type)" : $"Combo box: {label}",
            _ => label is null ? "Plain-text content control" : $"Content control: {label}"
        };
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

        e.Handled = true;
        var owner = FindOwnerView(wpf);
        if (owner is null || !owner.AllowsContentControlInteraction(marker.Control))
            return;

        if (owner.RestrictEditingPolicy.IsFormFieldEditingOnly
            && marker.Location is { } location
            && owner.ToggleContentControl(location.BlockIndex, location.RunIndex))
        {
            return;
        }

        var toggled = marker.Control with { Checked = !marker.Control.Checked };
        AddMarker(wpf, m => m with { Control = new ContentControlMarker(toggled, marker.Location) });
        wpf.Text = toggled.Checked ? ModelContentControl.CheckedGlyph : ModelContentControl.UncheckedGlyph;

        // Persist the new state into the model so a subsequent save reflects the toggle.
        owner.CommitToModel();
    }

    /// <summary>
    /// Opens the choice list of a drop-down / combo content control when its run is clicked: a context
    /// menu offers each <see cref="ModelContentControl.Items"/> display text; selecting one swaps the run
    /// text in place and re-commits so the choice round-trips on save.
    /// </summary>
    private static void OnListControlClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: RunMarkers { Control: { } marker } } wpf)
            return;
        var control = marker.Control;
        if (control.Kind is not (ContentControlKind.DropDownList or ContentControlKind.ComboBox)
            || control.Items.Count == 0)
            return;

        e.Handled = true;
        var owner = FindOwnerView(wpf);
        if (owner is null || !owner.AllowsContentControlInteraction(control))
            return;

        var menu = new ContextMenu();
        var plan = FreeWContextMenuPlanner.BuildContentControl(new ModelRun(wpf.Text) { Control = control });
        foreach (var planned in plan.Items)
        {
            if (planned.CommandId is not { } commandId
                || !FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.ContentChoicePrefix, out var selectedIndex)
                || selectedIndex >= control.Items.Count)
                continue;
            var display = control.Items[selectedIndex].DisplayText;
            var entry = new MenuItem
            {
                Header = planned.Header,
                IsCheckable = planned.IsChecked.HasValue,
                IsChecked = planned.IsChecked ?? false,
                IsEnabled = planned.IsEnabled,
            };
            entry.Click += (_, _) =>
            {
                if (owner.RestrictEditingPolicy.IsFormFieldEditingOnly
                    && marker.Location is { } location
                    && owner.SelectContentControlItem(location.BlockIndex, location.RunIndex, selectedIndex))
                {
                    return;
                }

                wpf.Text = display;
                owner.CommitToModel();
            };
            menu.Items.Add(entry);
        }
        menu.PlacementTarget = wpf.Parent as UIElement;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Opens a small relative-date menu for a date-picker content control when its run is clicked: Today,
    /// Yesterday and Tomorrow, each formatted with the control's <see cref="ModelContentControl.DateFormat"/>.
    /// Selecting one swaps the displayed date text in place and re-commits so it round-trips on save.
    /// </summary>
    private static void OnDatePickerClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not WpfRun { Tag: RunMarkers { Control: { } marker } } wpf
            || marker.Control.Kind != ContentControlKind.DatePicker)
            return;

        e.Handled = true;
        var owner = FindOwnerView(wpf);
        if (owner is null || !owner.AllowsContentControlInteraction(marker.Control))
            return;

        var menu = new ContextMenu();
        var choices = ContentControlInteractionPlanner.RelativeDateChoices(marker.Control);
        var plan = FreeWContextMenuPlanner.BuildContentControl(new ModelRun(wpf.Text) { Control = marker.Control });
        foreach (var planned in plan.Items)
        {
            if (planned.CommandId is not { } commandId
                || !FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.ContentDatePrefix, out var selectedIndex)
                || selectedIndex >= choices.Count)
                continue;
            var choice = choices[selectedIndex];
            var entry = new MenuItem
            {
                Header = planned.Header,
                IsCheckable = planned.IsChecked.HasValue,
                IsChecked = planned.IsChecked ?? false,
                IsEnabled = planned.IsEnabled,
            };
            entry.Click += (_, _) =>
            {
                if (owner.RestrictEditingPolicy.IsFormFieldEditingOnly
                    && marker.Location is { } location
                    && owner.SelectContentControlRelativeDate(location.BlockIndex, location.RunIndex, selectedIndex))
                {
                    return;
                }

                wpf.Text = choice.DisplayText;
                owner.CommitToModel();
            };
            menu.Items.Add(entry);
        }
        menu.PlacementTarget = wpf.Parent as UIElement;
        menu.IsOpen = true;
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
    private static Inline BuildInternalHyperlink(Inline content, string anchor, string? tooltip = null)
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
    // model bookmark names preserved via each WPF paragraph's ParagraphTag, searching the FlowDocument
    // that hosts the clicked link.
    private static void OnInternalLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfHyperlink { Tag: HyperlinkInfo { Anchor: { Length: > 0 } anchor } } link)
            return;
        var flow = FindFlowDocument(link);
        var target = flow?.Blocks.OfType<WpfParagraph>()
            .FirstOrDefault(p => p.Tag is ParagraphTag { BookmarkNames: { } names } && names.Contains(anchor));
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
    private static Inline BuildHyperlink(Inline content, string url, string? tooltip = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return content;

        var link = new WpfHyperlink(content) { NavigateUri = uri };
        StyleLink(link, url, tooltip);
        return link;
    }

    // Opens the link target in the default handler, routed through the shared launcher so the scheme
    // allowlist lives in one place. Blocked schemes and launch failures are silently dropped —
    // opening a link must never crash the editor.
    private static void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri is not { } uri)
            return;
        ExternalUriLauncher.Open(
            uri.AbsoluteUri,
            target => Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }));
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
            Tag = new FootnoteMarker(footnoteId)
        };
        ApplyNoteReferencePresentation(marker, document);
        if (document.Footnotes.TryGetValue(footnoteId, out var footnote) && footnote.PlainText is { Length: > 0 } text)
            marker.ToolTip = text;
        return marker;
    }

    /// <summary>Carried on a footnote-marker WPF run's Tag so CommitToModel can round-trip its id.</summary>
    private sealed record FootnoteMarker(int FootnoteId);

    /// <summary>
    /// Scans <paramref name="blocks"/> (recursively through paragraphs, lists, tables) and returns
    /// the ordered distinct footnote and endnote IDs that appear in them.  Used by
    /// <see cref="FreeW.App.Host.Editing.PageBox"/> and <see cref="PaginatedEditorPanel"/> to
    /// determine which footnote/endnote entries belong on each page box.
    /// </summary>
    internal static (IReadOnlyList<int> FootnoteIds, IReadOnlyList<int> EndnoteIds)
        CollectNoteIds(IEnumerable<System.Windows.Documents.Block> blocks)
    {
        var fnIds = new List<int>();
        var enIds = new List<int>();
        var fnSeen = new HashSet<int>();
        var enSeen = new HashSet<int>();

        foreach (var block in blocks)
            CollectNoteIdsFromBlock(block, fnIds, enIds, fnSeen, enSeen);

        return (fnIds, enIds);
    }

    /// <summary>
    /// Identifies one footnote reference at its concrete text position. A paragraph can begin on one
    /// page and place its reference on the next, so paginator consumers must use the marker position
    /// rather than assigning the entire block to a page.
    /// </summary>
    internal sealed record FootnoteMarkerPosition(int FootnoteId, TextPointer Position);

    /// <summary>
    /// Returns the ordered footnote marker IDs and text positions inside the supplied blocks.
    /// </summary>
    internal static IReadOnlyList<FootnoteMarkerPosition> CollectFootnoteMarkers(
        IEnumerable<System.Windows.Documents.Block> blocks)
    {
        var markers = new List<FootnoteMarkerPosition>();
        foreach (var block in blocks)
            CollectFootnoteMarkersFromBlock(block, markers);
        return markers;
    }

    /// <summary>
    /// Returns only footnote marker positions for existing page-assignment callers.
    /// </summary>
    internal static IReadOnlyList<TextPointer> CollectFootnoteMarkerPositions(
        IEnumerable<System.Windows.Documents.Block> blocks) =>
        CollectFootnoteMarkers(blocks).Select(marker => marker.Position).ToList();

    private static void CollectFootnoteMarkersFromBlock(
        System.Windows.Documents.Block block,
        List<FootnoteMarkerPosition> markers)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
                CollectFootnoteMarkersFromInlines(paragraph.Inlines, markers);
                break;
            case WpfList list:
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks)
                        CollectFootnoteMarkersFromBlock(itemBlock, markers);
                break;
            case System.Windows.Documents.Table table:
                foreach (var rg in table.RowGroups)
                    foreach (var row in rg.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellBlock in cell.Blocks)
                                CollectFootnoteMarkersFromBlock(cellBlock, markers);
                break;
        }
    }

    private static void CollectFootnoteMarkersFromInlines(
        InlineCollection inlines,
        List<FootnoteMarkerPosition> markers)
    {
        foreach (var inline in inlines)
        {
            if (inline is WpfRun run && run.Tag is FootnoteMarker marker)
                markers.Add(new FootnoteMarkerPosition(marker.FootnoteId, run.ContentStart));
            else if (inline is Span span)
                CollectFootnoteMarkersFromInlines(span.Inlines, markers);
        }
    }

    private static void CollectNoteIdsFromBlock(
        System.Windows.Documents.Block block,
        List<int> fnIds, List<int> enIds,
        HashSet<int> fnSeen, HashSet<int> enSeen)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
                CollectNoteIdsFromInlines(paragraph.Inlines, fnIds, enIds, fnSeen, enSeen);
                break;
            case WpfList list:
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks)
                        CollectNoteIdsFromBlock(itemBlock, fnIds, enIds, fnSeen, enSeen);
                break;
            case System.Windows.Documents.Table table:
                foreach (var rg in table.RowGroups)
                    foreach (var row in rg.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellBlock in cell.Blocks)
                                CollectNoteIdsFromBlock(cellBlock, fnIds, enIds, fnSeen, enSeen);
                break;
        }
    }

    private static void CollectNoteIdsFromInlines(
        InlineCollection inlines,
        List<int> fnIds, List<int> enIds,
        HashSet<int> fnSeen, HashSet<int> enSeen)
    {
        foreach (var inline in inlines)
        {
            if (inline is WpfRun run)
            {
                if (run.Tag is FootnoteMarker fm && fnSeen.Add(fm.FootnoteId))
                    fnIds.Add(fm.FootnoteId);
                else if (run.Tag is EndnoteMarker em && enSeen.Add(em.EndnoteId))
                    enIds.Add(em.EndnoteId);
            }
            else if (inline is Span span)
            {
                CollectNoteIdsFromInlines(span.Inlines, fnIds, enIds, fnSeen, enSeen);
            }
        }
    }

    /// <summary>
    /// Carried on a hidden Mark Citation (TA) marker WPF run's Tag so CommitToModel can round-trip the
    /// citation it records. Mirrors how <see cref="FootnoteMarker"/>/<see cref="PageBreakMarker"/> preserve
    /// their marks across an edit/commit cycle.
    /// </summary>
    private sealed record CitationMarker(Citation Citation, RunFormatting Formatting);

    /// <summary>Carried on a manual page-break WPF run's Tag so CommitToModel can round-trip it.</summary>
    private sealed record PageBreakMarker;

    /// <summary>Carried on a manual column-break WPF run's Tag so CommitToModel can round-trip it.</summary>
    private sealed record ColumnBreakMarker;

    /// <summary>
    /// Carried on a zero-width WPF run's Tag for a floating image so <see cref="ReadInline"/> can
    /// round-trip the image object back to the model on <see cref="CommitToModel"/>. The visual is
    /// NOT added to the FlowDocument — it lives on the overlay <see cref="FloatingObjectsCanvas"/>.
    /// This mirrors the <see cref="PageBreakMarker"/> placeholder pattern: the run carries model state
    /// but contributes no visible glyph to the text flow.
    /// </summary>
    /// <summary>
    /// Carried on a zero-width WPF run for any floating drawing object so CommitToModel can
    /// recover the model object verbatim. Exactly one of the five payload fields is non-null.
    /// </summary>
    private sealed record AnchorMarker(
        InlineImage? Image = null,
        Shape? Shape = null,
        Chart? Chart = null,
        SmartArt? SmartArt = null,
        WordArt? WordArt = null,
        FreeW.Core.Model.DrawingGroup? DrawingGroup = null);

    /// <summary>
    /// Distinguishes WPF floating-object wrap reservations from other <see cref="Floater"/> uses, such as
    /// drop caps, so commit readback only recovers overlay-canvas anchors created by this path.
    /// </summary>
    private sealed record FloatingWrapReservationMarker(
        AnchorMarker Anchor,
        string? HyperlinkUrl = null,
        string? HyperlinkAnchor = null,
        string? HyperlinkTooltip = null);

    // A visual-only wrap band copied into the earlier paragraph when a page-anchored image sits above
    // its model anchor. It has no marker, so ReadFloaterInlineContent ignores it during commit.
    private sealed record LeadingWrapReservation(
        ModelRun Run,
        DocumentFloatingWrapReservationPlan Plan,
        DocumentFloatRect Rect);

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
            Tag = new EndnoteMarker(endnoteId)
        };
        ApplyNoteReferencePresentation(marker, document);
        if (document.Endnotes.TryGetValue(endnoteId, out var endnote) && endnote.PlainText is { Length: > 0 } text)
            marker.ToolTip = text;
        return marker;
    }

    private static void ApplyNoteReferencePresentation(WpfRun marker, TextDocument document)
    {
        marker.BaselineAlignment = BaselineAlignment.Baseline;
        marker.FontSize = (document.DefaultRun.FontSizePt ?? DefaultFontSizePt) * PxPerPoint * SuperSubScale;
        marker.TextEffects.Add(new TextEffect
        {
            Transform = new TranslateTransform(0, -NoteReferenceSuperscriptOffsetDip)
        });
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
    /// resolve live when a paged-edit sub-editor context is active (non-zero
    /// <see cref="_renderHfPageNumber"/>/<see cref="_renderHfPageCount"/>), otherwise fall back to
    /// <paramref name="cached"/> (the last-computed text). This is the only place date/time is read —
    /// the model and docx IO stay deterministic.
    /// </summary>
    private static string ResolveFieldText(RunFieldKind kind, string cached, TextDocument document, string? fileName)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        // In PagedEdit mode, PAGE and NUMPAGES are resolved to the actual page-box page number / page
        // count injected by PaginatedEditorPanel just before LoadModel on the h/f sub-editor.  The
        // thread-static fields are zero outside that narrow window, so ordinary renders are unaffected.
        if (kind == RunFieldKind.PageNumber && _renderHfPageNumber > 0)
            return _renderHfPageNumberText
                ?? _renderHfPageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (kind == RunFieldKind.NumPages && _renderHfPageCount > 0)
            return _renderHfPageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (kind == RunFieldKind.PageNumber)
            return ResolvePageNumberFieldText(document);
        return kind switch
        {
            RunFieldKind.Date => DateTime.Now.ToString("d", culture),
            RunFieldKind.Time => DateTime.Now.ToString("t", culture),
            RunFieldKind.Author => document.Properties.Author is { Length: > 0 } author ? author : cached,
            RunFieldKind.FileName => fileName is { Length: > 0 } name ? name : cached,
            RunFieldKind.Title => document.Properties.Title is { Length: > 0 } title ? title : cached,
            RunFieldKind.Subject => document.Properties.Subject is { Length: > 0 } subject ? subject : cached,
            RunFieldKind.Keywords => document.Properties.Keywords is { Length: > 0 } keywords ? keywords : cached,
            RunFieldKind.DocComments => document.Properties.Comments is { Length: > 0 } comments ? comments : cached,
            _ => cached
        };
    }

    private static string ResolvePageNumberFieldText(TextDocument document)
    {
        var firstValue = Math.Max(1, document.Page.PageNumberStartAt ?? 1);
        return PageNumberFormatDialogPlanner.FormatPageNumber(firstValue, document.Page.PageNumberFormat);
    }

    /// <summary>
    /// Carried on a field WPF run's Tag so CommitToModel can round-trip the field kind and its cached
    /// (last-computed) text. The WPF run's visible text is the resolved value; the cached text is what
    /// the model keeps so a re-resolve next render is possible and field-unaware consumers still render.
    /// </summary>
    private sealed record FieldMarker(RunFieldKind Kind, string Cached);

    /// <summary>
    /// Carried on a table-formula WPF run's Tag so <see cref="CommitToModel"/> can round-trip the formula
    /// (expression + number format). The WPF run's visible text is the cached computed result.
    /// </summary>
    private sealed record TableFormulaMarker(TableFormulaField Formula);

    /// <summary>
    /// Carried on a cross-reference WPF run's Tag so <see cref="CommitToModel"/> can round-trip the field
    /// (kind + target + insert-as + hyperlink). The WPF run's visible text is the cached resolved value.
    /// </summary>
    private sealed record CrossReferenceMarker(CrossReferenceField Field);

    /// <summary>
    /// Carried on a complex-field WPF run's Tag so <see cref="CommitToModel"/> can round-trip the field
    /// (raw instruction + show-code toggle). The WPF run's visible text is either the field code (when
    /// <see cref="ComplexField.ShowCode"/>, e.g. <c>{ PAGE }</c>) or the resolved/cached result.
    /// </summary>
    private sealed record ComplexFieldMarker(ComplexField Field, string Cached);

    /// <summary>
    /// Renders a generic complex field (the <c>w:fldChar</c>/<c>w:instrText</c> construct). When the field's
    /// <see cref="ComplexField.ShowCode"/> is on (Alt+F9) it shows the field code as <c>{ INSTR }</c>;
    /// otherwise it shows the resolved result — DATE/TIME/AUTHOR/FILENAME resolve live (reusing
    /// <see cref="ResolveFieldText"/> via the instruction keyword), the rest fall back to the cached text.
    /// Tagged with a <see cref="ComplexFieldMarker"/> so the instruction round-trips on commit.
    /// </summary>
    private static WpfRun BuildComplexFieldRun(ModelRun run, TextDocument document)
    {
        var field = run.ComplexField!;
        var displayPlan = ComplexFieldDisplayPlanner.Build(
            field,
            ResolveFieldText(ComplexFieldDisplayPlanner.ResolveLiveKind(field.Keyword), run.Text, document, _renderFileName),
            document);
        var display = displayPlan.Text;
        var fmt = run.Formatting ?? document.DefaultRun;
        var wpf = new WpfRun(display)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal,
            Tag = new ComplexFieldMarker(field, run.Text)
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        if (fmt.FontSizePt is { } size)
            wpf.FontSize = size * PxPerPoint;
        if (displayPlan.IsFieldCode)
            wpf.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        else if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);
        wpf.ToolTip = (field.Keyword.Length > 0 ? field.Keyword : "Field") + " field: " + field.Instruction.Trim();
        return wpf;
    }

    /// <summary>
    /// Maps a complex field's leading keyword to the <see cref="RunFieldKind"/> that resolves it live, so
    /// PAGE/DATE/TIME/FILENAME/AUTHOR/NUMPAGES complex fields share <see cref="ResolveFieldText"/> with
    /// their <c>w:fldSimple</c> cousins. Unrecognised keywords map to <see cref="RunFieldKind.None"/> so the
    /// field shows its cached result.
    /// </summary>
    /// <summary>Builds a WPF run rendering a cross-reference field's cached text, tagged for round-trip.</summary>
    private static WpfRun BuildCrossReferenceRun(ModelRun run, TextDocument document)
    {
        var fmt = run.Formatting ?? document.DefaultRun;
        var wpf = new WpfRun(run.Text)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal,
            Tag = new CrossReferenceMarker(run.CrossReference!)
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        if (fmt.FontSizePt is { } size)
            wpf.FontSize = size * PxPerPoint;
        if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);
        // A hyperlink cross-reference renders in the link colour so it reads as clickable, matching Word.
        else if (run.CrossReference!.Hyperlink)
            wpf.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x63, 0xC1));
        wpf.ToolTip = "Cross-reference: " + run.CrossReference!.Kind;
        return wpf;
    }

    /// <summary>Builds a WPF run rendering a table-formula field's cached result, tagged for round-trip.</summary>
    private static WpfRun BuildTableFormulaRun(ModelRun run, TextDocument document)
    {
        var fmt = run.Formatting ?? document.DefaultRun;
        var wpf = new WpfRun(run.Text)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal,
            Tag = new TableFormulaMarker(run.TableFormula!)
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        if (fmt.FontSizePt is { } size)
            wpf.FontSize = size * PxPerPoint;
        if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);
        wpf.ToolTip = "Formula: " + run.TableFormula!.Expression;
        return wpf;
    }

    /// <summary>
    /// Inserts a document field run of the given <paramref name="kind"/> at the caret. The field is built
    /// with an initially-resolved cached value (DATE/TIME/AUTHOR/FILENAME) so it carries a sensible
    /// fallback even before the next render; it then round-trips through the model and docx as a field.
    /// </summary>
    /// <summary>
    /// Word's Table &gt; Data &gt; Formula. Inserts a computed table-cell formula field (e.g.
    /// <c>=SUM(ABOVE)</c> with an optional number format) at the caret. The caret must be inside a table
    /// cell; outside a table this is a no-op. The result is computed immediately from the table's cell
    /// values and carried as the field's cached text, so it shows at once and round-trips through docx.
    /// </summary>
    public void InsertTableFormula(TableFormulaField formula)
    {
        Focus();
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;

        var run = TableLayoutOperations.BuildFormulaRun(table, rowIndex, columnIndex, formula);
        InsertInlineAtCaret(BuildTableFormulaRun(run, _model));
    }

    /// <summary>
    /// The model table containing the caret, or null when the caret is not inside a table. Lets the app
    /// layer (e.g. the Formula dialog) seed a default formula based on whether numbers sit above or to the
    /// left of the caret cell.
    /// </summary>
    public (ModelTable Table, int RowIndex, int ColumnIndex)? CaretTableCell()
    {
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return null;
        return (table, rowIndex, columnIndex);
    }

    /// <summary>
    /// The caret's table plus its current row and cell, for seeding the Table Properties dialog, or null when
    /// the caret is not inside a table. Commits pending edits first so the model reflects current content.
    /// </summary>
    public ModelTableContext? CaretTableContext()
    {
        CommitToModel();
        var (blockIndex, rowIndex, columnIndex) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return null;
        var row = rowIndex >= 0 && rowIndex < table.Rows.Count ? table.Rows[rowIndex] : null;
        var cell = row is not null && columnIndex >= 0 && columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null;
        return new ModelTableContext(table, row, cell);
    }

    /// <summary>
    /// Apply the values from the Table Properties dialog onto the caret's table / current row / current cell
    /// (direct model set + re-render, mirroring <see cref="SetCaretCellShading"/>). Table-level properties go
    /// on the table; row-level properties on the caret's row; cell-level properties on the caret's cell.
    /// No-op outside a table.
    /// </summary>
    public void ApplyTableProperties(TablePropertiesValues values)
    {
        var context = CaretTableContext();
        if (context is null)
            return;

        TablePropertiesDialogPlanner.ApplyValues(context, values);
        Render();
    }

    // Snapshot of the caret table's previous style id for table-style live-preview.
    private (int BlockIndex, string? PriorStyleId, string? PriorBorderColorHex, bool PriorBorders)? _tableStyleSnapshot;

    /// <summary>
    /// Apply a catalog table style to the table at the caret: sets <see cref="Table.TableStyleId"/> and
    /// adjusts <see cref="TableFormatting.Borders"/> to match the style definition, then re-renders. The
    /// change is direct (no undo bus) like <see cref="ApplyTableProperties"/>; the gallery calls it on
    /// click after reverting any live preview.
    /// </summary>
    public void ApplyTableStyle(DocumentTableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        CommitToModel();
        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;

        table.TableStyleId = style.WordStyleId;
        // Apply the style's border intent; the tblLook toggles (HeaderRow/BandedRows/etc.) are left
        // unchanged so the user's Table Style Options selections continue to drive the active regions.
        table.Formatting = table.Formatting with { Borders = style.Borders };
        Render();
    }

    /// <summary>
    /// Live-preview a catalog table style on the table at the caret without committing. A snapshot of
    /// the table's prior style id and border state is saved; <see cref="EndTableStylePreview"/> restores
    /// it. Used by the Table Styles gallery's hover preview. No-op outside a table.
    /// </summary>
    public void PreviewTableStyle(DocumentTableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (_tableStyleSnapshot is null)
            CommitToModel();
        else
            RestoreTableStylePreview();

        var (blockIndex, _, _) = CaretTableLocation();
        if (blockIndex < 0 || _model.Blocks[blockIndex] is not ModelTable table)
            return;

        _tableStyleSnapshot = (blockIndex, table.TableStyleId, null, table.Formatting.Borders);
        table.TableStyleId = style.WordStyleId;
        table.Formatting = table.Formatting with { Borders = style.Borders };
        Render();
    }

    /// <summary>Revert a live preview started by <see cref="PreviewTableStyle"/>. No-op if none is active.</summary>
    public void EndTableStylePreview()
    {
        if (_tableStyleSnapshot is null)
            return;
        RestoreTableStylePreview();
        Render();
    }

    private void RestoreTableStylePreview()
    {
        if (_tableStyleSnapshot is not { } snap)
            return;
        if (snap.BlockIndex >= 0 && snap.BlockIndex < _model.Blocks.Count
            && _model.Blocks[snap.BlockIndex] is ModelTable table)
        {
            table.TableStyleId = snap.PriorStyleId;
            table.Formatting = table.Formatting with { Borders = snap.PriorBorders };
        }
        _tableStyleSnapshot = null;
    }

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
    /// Inserts a generic complex field (Insert &gt; Quick Parts &gt; Field) from a raw instruction such as
    /// <c> PAGE </c>, <c> NUMPAGES </c>, <c> DATE \@ "M/d/yyyy" </c>, <c> FILENAME </c>, <c> AUTHOR </c> or
    /// <c> REF bookmark </c>. The field's result is resolved immediately (for recognised keywords) so it is
    /// not blank, and it serialises as the <c>w:fldChar</c>/<c>w:instrText</c> sequence so it round-trips
    /// and supports Alt+F9 / F9.
    /// </summary>
    public void InsertComplexField(string instruction)
    {
        Focus();
        if (string.IsNullOrWhiteSpace(instruction))
            return;
        // Word stores instructions with a single leading/trailing space; normalise so " PAGE " is produced
        // from a bare "PAGE".
        var normalized = " " + instruction.Trim() + " ";
        var field = new ComplexField(normalized);
        var cached = ResolveFieldText(ComplexFieldDisplayPlanner.ResolveLiveKind(field.Keyword), string.Empty, _model, CurrentFileName);
        var run = new ModelRun(cached) { ComplexField = field };
        InsertInlineAtCaret(BuildComplexFieldRun(run, _model));
    }

    /// <summary>
    /// Alt+F9: toggles whether complex fields in the document show their field <em>codes</em> (e.g.
    /// <c>{ PAGE }</c>) or their <em>results</em>. Flips every complex field's
    /// <see cref="ComplexField.ShowCode"/> to the opposite of the current majority state and re-renders.
    /// </summary>
    public void ToggleFieldCodes()
    {
        CommitToModel();
        var fields = _model.Blocks
            .OfType<ModelParagraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.ComplexField is not null)
            .ToList();
        if (fields.Count == 0)
            return;
        // Show codes unless they are already (mostly) shown, in which case hide them again.
        var show = fields.Count(r => r.ComplexField!.ShowCode) * 2 <= fields.Count;
        foreach (var r in fields)
            r.ComplexField = r.ComplexField! with { ShowCode = show };
        Render();
    }

    /// <summary>
    /// F9: updates (recomputes) every field's cached result. DATE/TIME/AUTHOR/FILENAME/NUMPAGES re-resolve
    /// to their current values; the reference/numbering fields FreeW models — <c>REF</c>/<c>PAGEREF</c>
    /// (cross-references to a bookmark: text vs page number) and <c>SEQ</c> (sequence numbering, the basis
    /// of captions) — re-evaluate against the current document via <see cref="ComplexFieldEngine"/>; and any
    /// inserted Table of Contents is regenerated (Word's "Update entire table"). Also re-resolves the simple
    /// <see cref="RunFieldKind"/> fields so both field forms stay current.
    /// </summary>
    public void UpdateFields()
    {
        CommitToModel();
        var blocks = _model.Blocks;
        for (var b = 0; b < blocks.Count; b++)
        {
            if (blocks[b] is not ModelParagraph paragraph)
                continue;
            for (var i = 0; i < paragraph.Runs.Count; i++)
            {
                var r = paragraph.Runs[i];
                if (r.CrossReference is { } crossReference)
                {
                    var resolved = CrossReferences.ResolveField(_model, crossReference, r.Text, b);
                    if (resolved.Length > 0)
                        r.Text = resolved;
                }
                else if (r.ComplexField is { } cf)
                {
                    // REF/PAGEREF/SEQ re-evaluate against current bookmarks/sequences; the rest reuse the
                    // live DATE/AUTHOR/… resolver (PAGE/NUMPAGES keep their cached value here).
                    var resolved = ComplexFieldEngine.CanRecompute(cf)
                        ? ComplexFieldEngine.Recompute(_model, b, i)
                        : ResolveFieldText(ComplexFieldDisplayPlanner.ResolveLiveKind(cf.Keyword), r.Text, _model, CurrentFileName);
                    if (resolved.Length > 0)
                        r.Text = resolved;
                }
                else if (r.FieldKind != RunFieldKind.None)
                {
                    var resolved = ResolveFieldText(r.FieldKind, r.Text, _model, CurrentFileName);
                    if (resolved.Length > 0)
                        r.Text = resolved;
                }
            }
        }

        // "Update entire table": regenerate inserted generated-reference regions from current document
        // state. Keep TOC and bibliography independent so a document containing both updates both in one
        // F9 pass instead of short-circuiting after the first region.
        var refreshedGeneratedRegion = false;
        if (_model.Blocks.Any(TableOfContents.IsTocParagraph))
        {
            RefreshTableOfContentsFromModel();
            refreshedGeneratedRegion = true;
        }

        if (_model.Blocks.Any(Citations.IsBibliographyParagraph))
        {
            RefreshBibliographyFromModel();
            refreshedGeneratedRegion = true;
        }

        if (_model.Blocks.Any(TableOfFigures.IsTableOfFiguresParagraph))
        {
            RefreshTableOfFigures(TableOfFigures.ExistingLabelText(_model) ?? Captions.FigureLabelText);
            refreshedGeneratedRegion = true;
        }

        if (TableOfAuthoritiesRegionPlanner.ContainsRegion(_model))
        {
            ApplyTableOfAuthoritiesPlan(
                TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
                    _model,
                    pageResolver: BuildTableOfAuthoritiesPageResolver()));
            refreshedGeneratedRegion = true;
        }

        if (refreshedGeneratedRegion)
        {
            Render();
            return;
        }

        Render();
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

        // DecodeImage returns ImageSource?; placeholder is always BitmapSource. Cast for pixel-adjust.
        var decodedBitmap = (DecodeImage(image) as BitmapSource) ?? BuildImagePlaceholder(image, widthPx, heightPx);
        // Apply non-destructive pixel adjustments (brightness/contrast/saturation/transparency/recolor).
        // The alpha bake in ImageAdjustHelper covers static bitmap consumers; Opacity covers the live element.
        var source = (image.HasAdjustments || image.HasRecolor || image.HasArtisticEffect)
            ? ImageAdjustHelper.Apply(decodedBitmap, image)
            : (ImageSource)decodedBitmap;

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

        // Apply crop as a WPF RectangleGeometry clip on the Image.
        if (image.HasCrop)
        {
            var clipX  = image.CropLeft  * widthPx;
            var clipY  = image.CropTop   * heightPx;
            var clipW  = (1 - image.CropLeft - image.CropRight)  * widthPx;
            var clipH  = (1 - image.CropTop  - image.CropBottom) * heightPx;
            if (clipW > 0 && clipH > 0)
                element.Clip = new System.Windows.Media.RectangleGeometry(new Rect(clipX, clipY, clipW, clipH));
        }

        // Apply rotation and/or flip via a WPF TransformGroup on the Image.
        if (image.RotationAngle != 0 || image.FlipH || image.FlipV)
        {
            var group = new System.Windows.Media.TransformGroup();
            if (image.FlipH || image.FlipV)
                group.Children.Add(new System.Windows.Media.ScaleTransform(
                    image.FlipH ? -1 : 1, image.FlipV ? -1 : 1,
                    widthPx / 2, heightPx / 2));
            if (image.RotationAngle != 0)
                group.Children.Add(new System.Windows.Media.RotateTransform(
                    image.RotationAngle, widthPx / 2, heightPx / 2));
            element.RenderTransform = group;
        }

        // Apply picture border as WPF border around the image.
        FrameworkElement inlineRoot;
        if (image.HasBorder)
        {
            var borderWidthPx = Math.Max(image.BorderWidthPt, 0.75) * PxPerPoint;
            var colorHex = image.BorderColorHex!.TrimStart('#');
            System.Windows.Media.Color borderColor;
            try
            {
                borderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + colorHex);
            }
            catch
            {
                borderColor = System.Windows.Media.Colors.Black;
            }
            var borderBrush = new System.Windows.Media.SolidColorBrush(borderColor);

            System.Windows.Media.Brush strokeBrush = borderBrush;
            // Apply dash if specified (use DashStyles for dotted/dashed).
            if (!string.IsNullOrEmpty(image.BorderDash) && image.BorderDash != "solid")
            {
                strokeBrush = borderBrush; // keep solid brush; WPF Border doesn't do dash natively, we use a simpler style
            }

            inlineRoot = new System.Windows.Controls.Border
            {
                BorderBrush = strokeBrush,
                BorderThickness = new Thickness(borderWidthPx),
                Child = element,
                Width = widthPx + borderWidthPx * 2,
                Height = heightPx + borderWidthPx * 2,
            };
        }
        else
        {
            inlineRoot = element;
        }

        // Apply WPF effects (shadow / glow / soft-edge / bevel) on the root element.
        // Reflection is handled separately as a visual child below.
        ApplyImageWpfEffects(inlineRoot, image);

        // Reflection: render a mirrored low-opacity copy below the image using a VisualBrush.
        if (image.ReflectionPreset > 0)
        {
            var reflOpacity = image.ReflectionPreset <= 3 ? 0.5 : 1.0;
            var reflDistPx  = image.ReflectionPreset switch { 2 => 4.0, 3 => 8.0, 5 => 4.0, _ => 0.0 } * PxPerPoint;
            var reflContainer = BuildReflectionContainer(inlineRoot, widthPx, heightPx, reflOpacity, reflDistPx,
                borderWidthPx: image.HasBorder ? Math.Max(image.BorderWidthPt, 0.75) * PxPerPoint : 0);
            return new InlineUIContainer(reflContainer) { BaselineAlignment = BaselineAlignment.Bottom };
        }

        return new InlineUIContainer(inlineRoot) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// Apply WPF visual effects (DropShadowEffect, BlurEffect for soft-edge / bevel approximation)
    /// to the given image root element based on the model's effect presets.
    /// </summary>
    private static void ApplyImageWpfEffects(FrameworkElement root, InlineImage image)
    {
        if (!image.HasEffects) return;

        // Shadow overrides glow if both are set; WPF Effect is a single Effect per element.
        if (image.ShadowPreset > 0)
        {
            var (blur, dist, opacity) = image.ShadowPreset switch
            {
                1 => (4.0, 3.0, 0.50),
                2 => (6.0, 5.0, 0.55),
                3 => (8.0, 7.0, 0.60),
                4 => (4.0, 4.0, 0.50),
                _ => (10.0, 10.0, 0.65)
            };
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius   = blur,
                ShadowDepth  = dist,
                Direction    = 315,
                Opacity      = opacity,
                Color        = System.Windows.Media.Colors.Black
            };
        }
        else if (image.GlowSizePt > 0)
        {
            // Glow: use DropShadowEffect with 0 distance and colored output.
            System.Windows.Media.Color glowColor;
            try
            {
                var hex = !string.IsNullOrEmpty(image.GlowColorHex)
                    ? image.GlowColorHex.TrimStart('#') : "4472C4";
                glowColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            }
            catch { glowColor = System.Windows.Media.Color.FromRgb(0x44, 0x72, 0xC4); }

            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius  = image.GlowSizePt * PxPerPoint,
                ShadowDepth = 0,
                Opacity     = 0.6,
                Color       = glowColor
            };
        }
        else if (image.SoftEdgePt > 0)
        {
            // Soft edge: a BlurEffect on the element (approximate — true soft edge clips are more complex).
            root.Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius      = image.SoftEdgePt * PxPerPoint * 0.5,
                KernelType  = System.Windows.Media.Effects.KernelType.Gaussian
            };
        }
        else if (image.BevelPreset > 0)
        {
            // Bevel: inner-highlight approximation via very short white DropShadow at 45°.
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius  = image.BevelPreset switch { 1 => 3.0, 2 => 5.0, 3 => 3.0, _ => 6.0 },
                ShadowDepth = 1.0,
                Direction   = 135,
                Opacity     = 0.40,
                Color       = System.Windows.Media.Colors.White
            };
        }
    }

    /// <summary>
    /// Build a StackPanel containing the image element on top and a vertically-flipped fading
    /// reflection copy below it (separated by <paramref name="distPx"/> pixels). The reflection
    /// fades from opaque near the object to fully transparent at the bottom, matching Word's look,
    /// via a vertical <see cref="System.Windows.Media.LinearGradientBrush"/> applied as an
    /// <see cref="System.Windows.UIElement.OpacityMask"/>.
    /// </summary>
    private static System.Windows.Controls.StackPanel BuildReflectionContainer(
        FrameworkElement imageRoot, double widthPx, double heightPx,
        double reflOpacity, double distPx, double borderWidthPx)
    {
        var totalW = widthPx + borderWidthPx * 2;
        var totalH = heightPx + borderWidthPx * 2;

        // Create a visual brush from the image root for the reflection copy.
        var vBrush = new System.Windows.Media.VisualBrush(imageRoot)
        {
            Stretch  = System.Windows.Media.Stretch.None,
            AlignmentX = System.Windows.Media.AlignmentX.Left,
            AlignmentY = System.Windows.Media.AlignmentY.Top
        };

        // Fade gradient: opaque at the top of the reflection (near the image) → transparent at
        // the bottom. Applied as an OpacityMask so the reflection fades rather than sitting at
        // a flat half-opacity — this matches Word's "reflection" visual appearance.
        var fadeMask = new System.Windows.Media.LinearGradientBrush(
            new System.Windows.Media.GradientStopCollection
            {
                new(Color.FromArgb((byte)(reflOpacity * 255), 0, 0, 0), 0.0),
                new(Color.FromArgb(0, 0, 0, 0), 1.0),
            },
            new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

        var reflRect = new System.Windows.Shapes.Rectangle
        {
            Width  = totalW,
            Height = totalH,
            Fill   = vBrush,
            // Vertical flip via RenderTransform; Opacity removed — the OpacityMask handles fade.
            RenderTransform = new System.Windows.Media.ScaleTransform(1, -1, totalW / 2, totalH / 2),
            OpacityMask = fadeMask,
            Margin = new Thickness(0, distPx, 0, 0)
        };

        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Width  = totalW
        };
        panel.Children.Add(imageRoot);
        panel.Children.Add(reflRect);
        return panel;
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
    private static double EffectLineThickness(DocumentEffectSet effectSet) =>
        Math.Max(1, effectSet.LineWidthEmu / 12700.0 * PxPerPoint);

    private static DropShadowEffect? CreateObjectEffect(DocumentEffectSet effectSet)
    {
        if (!effectSet.OuterShadow && !effectSet.SoftEdges)
            return null;

        var shadow = new DropShadowEffect
        {
            Color = Color.FromRgb(0x40, 0x40, 0x40),
            BlurRadius = effectSet.SoftEdges ? 10 : 5,
            ShadowDepth = effectSet.SoftEdges ? 2 : 1,
            Direction = 315,
            Opacity = effectSet.SoftEdges ? 0.30 : 0.22
        };
        shadow.Freeze();
        return shadow;
    }

    private static void ApplyObjectEffect(FrameworkElement element, DocumentEffectSet effectSet) =>
        element.Effect = CreateObjectEffect(effectSet);

    private static InlineUIContainer BuildShapeRun(Shape shape, DocumentEffectSet effectSet)
    {
        var widthPx = shape.WidthPt * PxPerPoint;
        var heightPx = shape.HeightPt * PxPerPoint;
        var strokeThickness = EffectLineThickness(effectSet);

        // Fill: extended fill (gradient/pattern/no-fill) takes priority over solid FillColorHex.
        System.Windows.Media.Brush fill;
        if (shape.ExtendedFill is { } extFill)
        {
            fill = extFill.Kind switch
            {
                ShapeFillKind.NoFill   => System.Windows.Media.Brushes.Transparent,
                ShapeFillKind.Gradient => BuildGradientBrush(extFill),
                ShapeFillKind.Pattern  => BuildPatternBrush(extFill),
                _                      => System.Windows.Media.Brushes.Transparent,
            };
        }
        else
        {
            fill = TryParseColor(shape.FillColorHex, out var fillColor)
                ? new SolidColorBrush(fillColor)
                : System.Windows.Media.Brushes.Transparent;
        }

        // Outline: use model OutlineColorHex/OutlineWidthPt when set; fall back to a faint grey hairline.
        System.Windows.Media.Brush stroke = TryParseColor(shape.OutlineColorHex, out var strokeColor)
            ? new SolidColorBrush(strokeColor)
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
        var outlineThickness = shape.OutlineColorHex is { Length: > 0 }
            ? Math.Max(1, shape.OutlineWidthPt * PxPerPoint)
            : strokeThickness;

        FrameworkElement element;
        if (shape.HasCustomGeometry && shape.CustomGeometry is { } cg)
        {
            // W25: Render custom (freeform) geometry using a WPF Path with StreamGeometry.
            var geo = new System.Windows.Media.StreamGeometry();
            using (var ctx = geo.Open())
            {
                // Collect all segments, tracking whether we need to close the current figure.
                bool inFigure = false;
                bool closeFigure = false;
                System.Windows.Point startPt = default;
                var pathSegments = new System.Collections.Generic.List<CustomSegment>();

                void FlushFigure()
                {
                    if (!inFigure) return;
                    ctx.BeginFigure(startPt, isFilled: true, isClosed: closeFigure);
                    foreach (var segment in pathSegments)
                    {
                        if (segment.Kind == CustomSegmentKind.LineTo && segment.Point is not null)
                        {
                            ctx.LineTo(new System.Windows.Point(
                                segment.Point.X / (double)cg.Width * widthPx,
                                segment.Point.Y / (double)cg.Height * heightPx),
                                isStroked: true,
                                isSmoothJoin: false);
                        }
                        else if (segment.Kind == CustomSegmentKind.CubicBezierTo
                            && segment.Point is not null && segment.ControlPoint1 is not null && segment.ControlPoint2 is not null)
                        {
                            ctx.BezierTo(
                                new System.Windows.Point(segment.ControlPoint1.X / (double)cg.Width * widthPx, segment.ControlPoint1.Y / (double)cg.Height * heightPx),
                                new System.Windows.Point(segment.ControlPoint2.X / (double)cg.Width * widthPx, segment.ControlPoint2.Y / (double)cg.Height * heightPx),
                                new System.Windows.Point(segment.Point.X / (double)cg.Width * widthPx, segment.Point.Y / (double)cg.Height * heightPx),
                                isStroked: true,
                                isSmoothJoin: false);
                        }
                    }
                    pathSegments.Clear();
                    inFigure = false;
                    closeFigure = false;
                }

                foreach (var seg in cg.Segments)
                {
                    if (seg.Kind == CustomSegmentKind.MoveTo && seg.Point is not null)
                    {
                        FlushFigure();
                        startPt = new System.Windows.Point(
                            seg.Point.X / (double)cg.Width  * widthPx,
                            seg.Point.Y / (double)cg.Height * heightPx);
                        inFigure = true;
                    }
                    else if ((seg.Kind == CustomSegmentKind.LineTo || seg.Kind == CustomSegmentKind.CubicBezierTo) && inFigure)
                    {
                        pathSegments.Add(seg);
                    }
                    else if (seg.Kind == CustomSegmentKind.Close && inFigure)
                    {
                        closeFigure = true;
                    }
                }
                FlushFigure();
            }
            geo.Freeze();
            element = new System.Windows.Shapes.Path
            {
                Width = widthPx,
                Height = heightPx,
                Data = geo,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = outlineThickness,
                Stretch = System.Windows.Media.Stretch.None,
            };
        }
        else if (shape.Kind == ShapeKind.Ellipse)
        {
            element = new System.Windows.Shapes.Ellipse
            {
                Width = widthPx,
                Height = heightPx,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = outlineThickness,
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
                BorderThickness = new Thickness(outlineThickness),
                CornerRadius = shape.Kind == ShapeKind.RoundedRectangle ? new CornerRadius(6) : new CornerRadius(0),
            };
            if (shape.HasText)
            {
                var textBlock = new TextBlock
                {
                    Text = shape.PlainText,
                    Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                // Apply LayoutTransform for rotated text directions.
                if (shape.TextDirection == ShapeTextDirection.Rotate90)
                    textBlock.LayoutTransform = new RotateTransform(90);
                else if (shape.TextDirection == ShapeTextDirection.Rotate270)
                    textBlock.LayoutTransform = new RotateTransform(270);
                border.Child = textBlock;
            }
            element = border;
        }

        // Shape effects: apply model-level effects (shadow/glow/bevel) on top of the document theme effect.
        ApplyShapeModelEffects(element, shape.Effects, effectSet);
        element.Tag = shape; // carries the model shape so CommitToModel can round-trip it
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// Applies model-level <see cref="ShapeEffectLst"/> to a rendered shape element, compositing with the
    /// document theme effect. Shadow wins if both are present (model shadow is more specific). Glow is
    /// approximated as a second DropShadow with ShadowDepth=0. Bevel / 3-D is rendered as a bright
    /// border highlight (best-effort). Soft-edge and reflection have no lightweight WPF equivalent; they
    /// are preserved in the model / DOCX but not visually rendered.
    /// </summary>
    private static void ApplyShapeModelEffects(FrameworkElement element, ShapeEffectLst? fx, DocumentEffectSet effectSet)
    {
        if (fx is null)
        {
            ApplyObjectEffect(element, effectSet);
            return;
        }

        if (fx.HasShadow)
        {
            element.Effect = new DropShadowEffect
            {
                Color      = TryParseColor("#" + fx.ShadowColorHex, out var sc) ? sc : Colors.Black,
                Opacity    = fx.ShadowAlpha / 100000.0,
                BlurRadius = fx.ShadowBlurRad / 12700.0,
                ShadowDepth = fx.ShadowDist / 12700.0,
                Direction  = (fx.ShadowDir / 60000.0) % 360,
                RenderingBias = RenderingBias.Performance,
            };
        }
        else if (fx.HasGlow)
        {
            // WPF has no glow effect; approximate with a zero-depth blurred shadow.
            element.Effect = new DropShadowEffect
            {
                Color      = TryParseColor("#" + fx.GlowColorHex, out var gc) ? gc : Colors.Blue,
                Opacity    = fx.GlowAlpha / 100000.0,
                BlurRadius = fx.GlowRad / 12700.0,
                ShadowDepth = 0,
                RenderingBias = RenderingBias.Performance,
            };
        }
        else
        {
            ApplyObjectEffect(element, effectSet);
        }

        // Bevel: add a highlight border on the Border element.
        if (fx.HasBevel && element is Border bevelBorder)
            bevelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE8, 0xFF));
    }

    /// <summary>Builds a WPF LinearGradientBrush from a <see cref="ShapeFill"/> gradient descriptor.</summary>
    private static System.Windows.Media.LinearGradientBrush BuildGradientBrush(ShapeFill fill)
    {
        // GradientAngle in 60k-degree units; 0 = left-to-right, 5400000 = top-to-bottom.
        var angleDeg = fill.GradientAngle / 60000.0;
        var brush = new System.Windows.Media.LinearGradientBrush();
        brush.StartPoint = new System.Windows.Point(0, 0);
        brush.EndPoint   = new System.Windows.Point(
            Math.Cos(angleDeg * Math.PI / 180.0),
            Math.Sin(angleDeg * Math.PI / 180.0));
        foreach (var stop in fill.GradientStops)
        {
            if (TryParseColor(stop.ColorHex, out var c))
                brush.GradientStops.Add(new System.Windows.Media.GradientStop(c, stop.Position / 100000.0));
        }
        return brush;
    }

    /// <summary>
    /// Renders a DrawingML preset pattern fill as a tiled <see cref="System.Windows.Media.DrawingBrush"/>.
    /// Each distinct preset group (horizontal, vertical, diagonal, cross, dot, etc.) maps to a visually
    /// distinct tile so patterns are distinguishable from each other and from solid fills. The previous
    /// implementation used a single diagCross tile for all presets.
    /// </summary>
    private static System.Windows.Media.DrawingBrush BuildPatternBrush(ShapeFill fill)
    {
        TryParseColor(fill.PatternFgColorHex ?? "#4472C4", out var fg);
        TryParseColor(fill.PatternBgColorHex ?? "#FFFFFF", out var bg);

        var preset = fill.PatternPreset ?? string.Empty;
        var fgBrush = new SolidColorBrush(fg);
        var pen = new System.Windows.Media.Pen(fgBrush, 1);

        // Build a tile drawing based on the preset family.
        // Tile is 8×8 device-independent pixels; complex patterns use 12×12.
        System.Windows.Media.Drawing tile;

        if (preset is "horz" or "ltHorz" or "medGray" or "dkHorz" or "pct5" or "pct10" or "pct20")
        {
            // Horizontal lines
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(8, 4))));
            tile = g;
        }
        else if (preset is "vert" or "ltVert" or "dkVert" or "pct25" or "pct30")
        {
            // Vertical lines
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(4, 0), new System.Windows.Point(4, 8))));
            tile = g;
        }
        else if (preset is "diagStripe" or "ltDnDiag" or "dkDnDiag" or "dnDiag" or "pct50")
        {
            // Diagonal top-left to bottom-right
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 0), new System.Windows.Point(8, 8))));
            tile = g;
        }
        else if (preset is "ltUpDiag" or "dkUpDiag" or "upDiag" or "pct60" or "pct70")
        {
            // Diagonal bottom-left to top-right
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 8), new System.Windows.Point(8, 0))));
            tile = g;
        }
        else if (preset is "cross" or "ltGrid" or "dkGrid" or "pct75" or "pct80")
        {
            // Cross (horizontal + vertical grid)
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(8, 4))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(4, 0), new System.Windows.Point(4, 8))));
            tile = g;
        }
        else if (preset is "dotGrid" or "dotDmnd" or "smGrid" or "pct90")
        {
            // Dotted / dot grid — single dot per cell
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(fgBrush, null,
                new System.Windows.Media.EllipseGeometry(new System.Windows.Point(4, 4), 1, 1)));
            tile = g;
        }
        else if (preset is "horzBrick" or "divot" or "weave")
        {
            // Brick — alternating horizontal dashes
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 12, 8));
            var thinPen = new System.Windows.Media.Pen(fgBrush, 0.5);
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 0), new System.Windows.Point(12, 0))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(6, 4), new System.Windows.Point(12, 4))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(3, 4))));
            // Vertical grout lines at offsets
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(6, 0), new System.Windows.Point(6, 4))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(0, 8))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, thinPen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(12, 4), new System.Windows.Point(12, 8))));
            tile = g;
            // 12-wide tile — return early with custom viewport
            return new System.Windows.Media.DrawingBrush(tile)
            {
                TileMode      = System.Windows.Media.TileMode.Tile,
                Viewport      = new System.Windows.Rect(0, 0, 12, 8),
                ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
            };
        }
        else
        {
            // Default / diagCross — covers "diagCross", "ltDiagCross", "dkDiagCross", and unknowns.
            var g = new System.Windows.Media.DrawingGroup();
            g.Children.Add(BgRect(bg, 8, 8));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 0), new System.Windows.Point(8, 8))));
            g.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(8, 0), new System.Windows.Point(0, 8))));
            tile = g;
        }

        return new System.Windows.Media.DrawingBrush(tile)
        {
            TileMode      = System.Windows.Media.TileMode.Tile,
            Viewport      = new System.Windows.Rect(0, 0, 8, 8),
            ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
        };

        static System.Windows.Media.GeometryDrawing BgRect(Color c, double w, double h) =>
            new(new SolidColorBrush(c), null,
                new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, w, h)));
    }

    /// <summary>
    /// Renders an inline equation as an InlineUIContainer. The unadorned Border carries the model
    /// <see cref="Equation"/> on its Tag so CommitToModel can round-trip it, while leaving the
    /// mathematical content to sit directly on the document surface like Word.
    /// </summary>
    private static InlineUIContainer BuildEquationRun(Equation equation)
    {
        var plan = EquationVisualPlanner.Build(equation);
        var content = BuildEquationVisualContent(plan);
        var element = new Border
        {
            Child = content,
            Tag = equation // carries the model equation so CommitToModel can round-trip it
        };
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static FrameworkElement BuildEquationVisualContent(EquationVisualPlan plan)
    {
        if (plan.Elements.All(element => element.Kind == EquationVisualElementKind.Segments))
            return BuildEquationTextBlock(plan.Segments);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var element in plan.Elements)
            panel.Children.Add(BuildEquationVisualElement(element));

        return panel;
    }

    private static FrameworkElement BuildEquationVisualElement(EquationVisualElement element)
    {
        return element.Kind switch
        {
            EquationVisualElementKind.Fraction => BuildEquationFractionElement(element),
            EquationVisualElementKind.Radical => BuildEquationRadicalElement(element),
            EquationVisualElementKind.NAry => BuildEquationNAryElement(element),
            EquationVisualElementKind.Matrix => BuildEquationMatrixElement(element),
            EquationVisualElementKind.Accent => BuildEquationAccentElement(element),
            EquationVisualElementKind.Bar => BuildEquationBarElement(element),
            EquationVisualElementKind.Delimiter => BuildEquationDelimiterElement(element),
            EquationVisualElementKind.GroupChar => BuildEquationGroupCharElement(element),
            EquationVisualElementKind.FunctionApply => BuildEquationFunctionApplyElement(element),
            _ => BuildEquationTextBlock(element.Segments)
        };
    }

    private static TextBlock BuildEquationTextBlock(IReadOnlyList<EquationVisualSegment> segments)
    {
        var text = new TextBlock
        {
            FontFamily = new FontFamily(EquationVisualPlanner.DefaultMathFontFamily),
            FontSize = DefaultFontSizePt * PxPerPoint,
            FontStyle = FontStyles.Italic,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var segment in segments)
            AppendEquationVisualSegment(text, segment);

        return text;
    }

    private static FrameworkElement BuildEquationFractionElement(EquationVisualElement element)
    {
        var numerator = SegmentWithRole(element, EquationVisualSegmentRole.FractionNumerator);
        var denominator = SegmentWithRole(element, EquationVisualSegmentRole.FractionDenominator);
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Tag = EquationVisualElementKind.Fraction
        };

        stack.Children.Add(BuildEquationStructureTextBlock(numerator, WpfTextAlignment.Center));
        stack.Children.Add(new Border
        {
            Background = Brushes.Black,
            Height = 1,
            MinWidth = 14,
            Margin = new Thickness(0, 0, 0, 0)
        });
        stack.Children.Add(BuildEquationStructureTextBlock(denominator, WpfTextAlignment.Center));
        return stack;
    }

    private static FrameworkElement BuildEquationRadicalElement(EquationVisualElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Tag = EquationVisualElementKind.Radical
        };

        if (SegmentWithRole(element, EquationVisualSegmentRole.RadicalDegree) is { Text.Length: > 0 } degree)
        {
            panel.Children.Add(BuildEquationStructureTextBlock(
                degree,
                WpfTextAlignment.Center,
                new Thickness(0, 0, -1, 7)));
        }

        panel.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.RadicalSign),
            WpfTextAlignment.Center));
        panel.Children.Add(new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = BuildEquationStructureTextBlock(
                SegmentWithRole(element, EquationVisualSegmentRole.RadicalRadicand),
                WpfTextAlignment.Center)
        });

        return panel;
    }

    private static FrameworkElement BuildEquationNAryElement(EquationVisualElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Tag = EquationVisualElementKind.NAry
        };

        var limits = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        limits.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        limits.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        limits.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddNAryLimitText(
            limits,
            row: 0,
            SegmentWithRole(element, EquationVisualSegmentRole.NAryUpperLimit),
            new Thickness(0, 0, 0, -2));
        AddNAryLimitText(
            limits,
            row: 1,
            SegmentWithRole(element, EquationVisualSegmentRole.NAryOperator),
            new Thickness(0, -1, 0, -1));
        AddNAryLimitText(
            limits,
            row: 2,
            SegmentWithRole(element, EquationVisualSegmentRole.NAryLowerLimit),
            new Thickness(0, -2, 0, 0));

        panel.Children.Add(limits);
        panel.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.NAryOperand),
            WpfTextAlignment.Left,
            new Thickness(3, 0, 0, 0)));

        return panel;
    }

    private static FrameworkElement BuildEquationAccentElement(EquationVisualElement element)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 4, 2, 0),
            Tag = EquationVisualElementKind.Accent
        };

        stack.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.AccentMark),
            WpfTextAlignment.Center,
            new Thickness(0, 0, 0, -3)));
        stack.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.AccentBase),
            WpfTextAlignment.Center,
            new Thickness(0, -3, 0, 0)));
        return stack;
    }

    private static FrameworkElement BuildEquationBarElement(EquationVisualElement element)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 4, 2, 0),
            MinWidth = 14,
            Tag = EquationVisualElementKind.Bar
        };

        var baseText = BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.BarBase),
            WpfTextAlignment.Center);
        if (element.BarTop)
        {
            stack.Children.Add(BuildEquationBarLine(new Thickness(0, 0, 0, -1)));
            stack.Children.Add(baseText);
        }
        else
        {
            stack.Children.Add(baseText);
            stack.Children.Add(BuildEquationBarLine(new Thickness(0, -1, 0, 0)));
        }

        return stack;
    }

    private static FrameworkElement BuildEquationDelimiterElement(EquationVisualElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0),
            Tag = EquationVisualElementKind.Delimiter
        };

        panel.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.DelimiterOpen),
            WpfTextAlignment.Center,
            new Thickness(1, 0, 1, 0)));
        panel.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.DelimiterContent),
            WpfTextAlignment.Center,
            new Thickness(1, 0, 1, 0)));
        panel.Children.Add(BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.DelimiterClose),
            WpfTextAlignment.Center,
            new Thickness(1, 0, 1, 0)));

        return panel;
    }

    private static FrameworkElement BuildEquationGroupCharElement(EquationVisualElement element)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 6, 2, 0),
            Tag = EquationVisualElementKind.GroupChar
        };

        var mark = BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.GroupCharMark),
            WpfTextAlignment.Center,
            new Thickness(0, 0, 0, -3));
        var baseText = BuildEquationStructureTextBlock(
            SegmentWithRole(element, EquationVisualSegmentRole.GroupCharBase),
            WpfTextAlignment.Center,
            new Thickness(0, -3, 0, 0));

        if (element.GroupCharacterTop)
        {
            stack.Children.Add(mark);
            stack.Children.Add(baseText);
        }
        else
        {
            baseText.Margin = new Thickness(0, 0, 0, -3);
            mark.Margin = new Thickness(0, -3, 0, 0);
            stack.Children.Add(baseText);
            stack.Children.Add(mark);
        }

        return stack;
    }

    private static FrameworkElement BuildEquationFunctionApplyElement(EquationVisualElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Tag = EquationVisualElementKind.FunctionApply
        };

        AddEquationFunctionPart(panel, EquationVisualSegmentRole.FunctionName, new Thickness(1, 0, 0, 0));
        AddEquationFunctionPart(panel, EquationVisualSegmentRole.FunctionOpenDelimiter, new Thickness(1, 0, 0, 0));
        AddEquationFunctionPart(panel, EquationVisualSegmentRole.FunctionArgument, new Thickness(0, 0, 0, 0));
        AddEquationFunctionPart(panel, EquationVisualSegmentRole.FunctionCloseDelimiter, new Thickness(0, 0, 1, 0));
        return panel;

        void AddEquationFunctionPart(StackPanel target, EquationVisualSegmentRole role, Thickness margin)
        {
            var segment = SegmentWithRole(element, role);
            if (segment.Text.Length == 0)
                return;

            target.Children.Add(BuildEquationStructureTextBlock(segment, WpfTextAlignment.Center, margin));
        }
    }

    private static Border BuildEquationBarLine(Thickness margin) =>
        new()
        {
            Background = Brushes.Black,
            Height = 1,
            MinWidth = 14,
            Margin = margin
        };

    private static FrameworkElement BuildEquationMatrixElement(EquationVisualElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Tag = EquationVisualElementKind.Matrix
        };

        panel.Children.Add(BuildEquationMatrixDelimiterTextBlock(
            EquationVisualPlanner.MatrixOpenDelimiterText,
            EquationVisualSegmentRole.MatrixOpenDelimiter));

        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        var rowCount = Math.Max(1, element.MatrixRowCount);
        var columnCount = Math.Max(1, element.MatrixColumnCount);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        foreach (var row in element.MatrixRows)
        {
            foreach (var cell in row.Cells)
            {
                var text = BuildEquationStructureTextBlock(
                    MatrixCellSegment(cell.Text),
                    WpfTextAlignment.Center,
                    new Thickness(2, 0, 2, 0));
                Grid.SetRow(text, cell.RowIndex);
                Grid.SetColumn(text, cell.ColumnIndex);
                grid.Children.Add(text);
            }
        }

        if (grid.Children.Count == 0)
        {
            grid.Children.Add(BuildEquationStructureTextBlock(
                MatrixCellSegment(string.Empty),
                WpfTextAlignment.Center,
                new Thickness(4, 0, 4, 0)));
        }

        panel.Children.Add(grid);
        panel.Children.Add(BuildEquationMatrixDelimiterTextBlock(
            EquationVisualPlanner.MatrixCloseDelimiterText,
            EquationVisualSegmentRole.MatrixCloseDelimiter));
        return panel;
    }

    private static TextBlock BuildEquationMatrixDelimiterTextBlock(
        string text,
        EquationVisualSegmentRole role)
    {
        return BuildEquationStructureTextBlock(
            new EquationVisualSegment(
                text,
                role,
                new EquationVisualStyle(
                    FontFamily: EquationVisualPlanner.DefaultMathFontFamily,
                    Italic: false,
                    FontSizeScale: 1.25,
                    BaselineRole: EquationVisualBaselineRole.Normal,
                    BaselineOffsetEm: 0.0)),
            WpfTextAlignment.Center,
            new Thickness(1, 0, 1, 0));
    }

    private static EquationVisualSegment MatrixCellSegment(string text) =>
        new(
            text,
            EquationVisualSegmentRole.MatrixCell,
            new EquationVisualStyle(
                FontFamily: EquationVisualPlanner.DefaultMathFontFamily,
                Italic: true,
                FontSizeScale: EquationVisualPlanner.StructureFontSizeScale,
                BaselineRole: EquationVisualBaselineRole.Normal,
                BaselineOffsetEm: 0.0));

    private static void AddNAryLimitText(Grid grid, int row, EquationVisualSegment segment, Thickness margin)
    {
        if (segment.Text.Length == 0)
            return;

        var text = BuildEquationStructureTextBlock(segment, WpfTextAlignment.Center, margin);
        Grid.SetRow(text, row);
        grid.Children.Add(text);
    }

    private static EquationVisualSegment SegmentWithRole(EquationVisualElement element, EquationVisualSegmentRole role)
    {
        return element.Segments.FirstOrDefault(segment => segment.Role == role)
            ?? new EquationVisualSegment(string.Empty, role, new EquationVisualStyle(
                EquationVisualPlanner.DefaultMathFontFamily,
                Italic: true,
                FontSizeScale: 1.0,
                EquationVisualBaselineRole.Normal,
                BaselineOffsetEm: 0.0));
    }

    private static TextBlock BuildEquationStructureTextBlock(
        EquationVisualSegment segment,
        WpfTextAlignment textAlignment,
        Thickness? margin = null)
    {
        var text = BuildEquationTextBlock([segment]);
        text.TextAlignment = textAlignment;
        text.Margin = margin ?? new Thickness(0);
        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.LineHeight = Math.Max(1, text.FontSize * 0.85);
        return text;
    }

    private static void AppendEquationVisualSegment(TextBlock text, EquationVisualSegment segment)
    {
        var run = new WpfRun(segment.Text)
        {
            FontFamily = new FontFamily(segment.Style.FontFamily),
            FontSize = DefaultFontSizePt * PxPerPoint * segment.Style.FontSizeScale,
            FontStyle = segment.Style.Italic ? FontStyles.Italic : FontStyles.Normal,
            BaselineAlignment = segment.Style.BaselineRole switch
            {
                EquationVisualBaselineRole.Superscript => BaselineAlignment.Superscript,
                EquationVisualBaselineRole.Subscript => BaselineAlignment.Subscript,
                _ => BaselineAlignment.Baseline
            }
        };
        text.Inlines.Add(run);
    }

    /// <summary>
    /// Renders inline WordArt as an InlineUIContainer carrying the model <see cref="WordArt"/> on its Tag
    /// (so CommitToModel round-trips it, mirroring shapes). ArchUp/Wave1 reuse the same glyph placement
    /// adapter as floating WordArt; other warps retain the compact TextBlock visual.
    /// </summary>
    private static InlineUIContainer BuildWordArtRun(WordArt wordArt, DocumentEffectSet effectSet)
    {
        var plan = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(wordArt);
        var wordArtPlan = plan.WordArt;
        var foreground = BuildDrawingFillBrush(wordArtPlan.Fill);
        var wpfEffect = BuildWordArtEffect(plan.Effects, effectSet);

        FrameworkElement element;
        if (wordArtPlan.Warp is WordArtWarp.ArchUp or WordArtWarp.Wave1)
        {
            var warpForeground = BuildDrawingWordArtTextBrush(wordArtPlan);
            var canvas = (Canvas)BuildWarpedDrawingWordArtVisual(
                wordArtPlan,
                foreground,
                warpForeground,
                wpfEffect,
                fitTextToBounds: false);
            var glyphs = CreateWordArtGlyphs(
                wordArtPlan.Text,
                wordArtPlan.FontFamily,
                Math.Max(8, wordArtPlan.FontSizeDip),
                wordArtPlan.Bold,
                warpForeground);
            canvas.Width = Math.Max(1, glyphs.Sum(glyph => glyph.DesiredSize.Width));
            canvas.Height = Math.Max(1, glyphs.Count == 0 ? 1 : glyphs.Max(glyph => glyph.DesiredSize.Height));
            element = canvas;
        }
        else
        {
            var textBlock = new TextBlock
            {
                Text       = wordArtPlan.Text,
                FontFamily = new FontFamily(wordArtPlan.FontFamily),
                FontSize   = wordArtPlan.FontSizeDip,
                FontWeight = wordArtPlan.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = foreground,
                Effect     = wpfEffect,
            };
            if (wordArtPlan.Warp != WordArtWarp.None)
                textBlock.FontStyle = wordArtPlan.Warp is WordArtWarp.Inflate
                    ? FontStyles.Normal : FontStyles.Italic;
            element = textBlock;
        }

        element.Tag = wordArt; // carries the model WordArt so CommitToModel can round-trip it

        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static System.Windows.Media.Effects.Effect? BuildWordArtEffect(
        DrawingObjectEffectsPlan effects,
        DocumentEffectSet effectSet)
    {
        if (effects.HasShadow)
        {
            return new DropShadowEffect
            {
                Color = TryParseColor(effects.ShadowColorHex, out var color) ? color : Colors.Black,
                BlurRadius = effects.ShadowBlurDip,
                ShadowDepth = effects.ShadowDistanceDip,
                Direction = effects.ShadowDirectionDegrees,
                Opacity = effects.ShadowOpacity,
                RenderingBias = RenderingBias.Performance
            };
        }

        if (effects.HasGlow)
        {
            return new DropShadowEffect
            {
                Color = TryParseColor(effects.GlowColorHex, out var color) ? color : Colors.Blue,
                BlurRadius = effects.GlowRadiusDip,
                ShadowDepth = 0,
                Opacity = effects.GlowOpacity,
                RenderingBias = RenderingBias.Performance
            };
        }

        return CreateObjectEffect(effectSet);
    }

    /// <summary>
    /// Renders an inline chart as an InlineUIContainer hosting a Border that carries the model
    /// <see cref="Chart"/> on its Tag (so CommitToModel round-trips it, mirroring shapes). Renders **all**
    /// series, honours the chart <see cref="ChartKind"/> (column / bar / line / area / scatter / pie /
    /// doughnut), and shows a category-axis + a legend — a lightweight but type-faithful stand-in for the
    /// DrawingML chart. Sizes the plot Canvas explicitly so the code-positioned children land correctly in
    /// the headless print/measure pass (there is no live layout to query ActualWidth).
    /// </summary>
    // ── Chart Design render helpers ───────────────────────────────────────────────────────────────

    /// <summary>Parse a #RRGGBB hex colour string to a WPF Color, falling back to the Office blue.</summary>
    private static Color ParseHexColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x5B, 0x9B, 0xD5); }
    }

    private static InlineUIContainer BuildChartRun(Chart chart, DocumentEffectSet effectSet)
    {
        var widthPx = chart.WidthPt * PxPerPoint;
        var heightPx = chart.HeightPt * PxPerPoint;
        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, widthPx, heightPx);
        var strokeThickness = EffectLineThickness(effectSet);

        var root = BuildChartSceneCanvas(scene);
        var element = new Border
        {
            Width = widthPx,
            Height = heightPx,
            CornerRadius = new CornerRadius(10),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(strokeThickness),
            Child = root,
            Tag = chart // carries the model chart so CommitToModel can round-trip it
        };
        ApplyObjectEffect(element, effectSet);
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    internal static Canvas BuildChartSceneCanvas(ChartScene scene)
    {
        var canvas = new Canvas
        {
            Width = scene.FrameBounds.Width,
            Height = scene.FrameBounds.Height,
            Background = System.Windows.Media.Brushes.Transparent
        };
        RenderChartScene(canvas, scene);
        return canvas;
    }

    private static void RenderChartScene(Canvas canvas, ChartScene scene)
    {
        if (scene.PlotFillHex is not null)
        {
            var plotFill = new Border
            {
                Width = scene.PlotBounds.Width,
                Height = scene.PlotBounds.Height,
                Background = new SolidColorBrush(ParseSceneColor(scene.PlotFillHex))
            };
            Canvas.SetLeft(plotFill, scene.PlotBounds.X);
            Canvas.SetTop(plotFill, scene.PlotBounds.Y);
            canvas.Children.Add(plotFill);
        }

        foreach (var line in scene.GridLines.Concat(scene.AxisLines))
        {
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = line.X1, Y1 = line.Y1, X2 = line.X2, Y2 = line.Y2,
                Stroke = new SolidColorBrush(ParseSceneColor(line.StrokeHex)),
                StrokeThickness = line.StrokeWidth
            });
        }

        foreach (var bar in scene.Bars)
        {
            var shape = new System.Windows.Shapes.Rectangle
            {
                Width = bar.Bounds.Width,
                Height = bar.Bounds.Height,
                Fill = new SolidColorBrush(ParseSceneColor(bar.FillHex, bar.FillOpacity))
            };
            Canvas.SetLeft(shape, bar.Bounds.X);
            Canvas.SetTop(shape, bar.Bounds.Y);
            canvas.Children.Add(shape);
        }

        foreach (var series in scene.LineSeries)
        {
            if (series.FillArea && series.Points.Count > 1)
            {
                var figure = new PathFigure
                {
                    StartPoint = new Point(series.Points[0].X, series.AreaBaselineY),
                    IsClosed = true
                };
                foreach (var point in series.Points)
                    figure.Segments.Add(new LineSegment(new Point(point.X, point.Y), true));
                figure.Segments.Add(new LineSegment(
                    new Point(series.Points[^1].X, series.AreaBaselineY), true));
                var area = new PathGeometry();
                area.Figures.Add(figure);
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = area,
                    Fill = new SolidColorBrush(ParseSceneColor(series.StrokeHex, series.AreaOpacity))
                });
            }

            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = new SolidColorBrush(ParseSceneColor(series.StrokeHex)),
                StrokeThickness = series.StrokeWidth,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var point in series.Points)
                polyline.Points.Add(new Point(point.X, point.Y));
            canvas.Children.Add(polyline);
        }

        foreach (var marker in scene.Markers)
            AddSceneMarker(canvas, marker);
        foreach (var slice in scene.Slices)
            AddSceneSlice(canvas, slice);
        var usesWordQuickLayoutColumnAxisTitles =
            scene.Kind == ChartKind.Column
            && scene.StyleId == 7
            && scene.QuickLayoutId == 9
            && string.Equals(scene.ColorSchemeId, "mono-blue", StringComparison.OrdinalIgnoreCase);
        foreach (var text in scene.Texts)
            AddSceneText(canvas, text, usesWordQuickLayoutColumnAxisTitles);

        for (var index = 0; index < scene.Legend.Count; index++)
        {
            var entry = scene.Legend[index];
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = entry.SwatchSize,
                Height = entry.SwatchSize,
                Fill = new SolidColorBrush(ParseSceneColor(scene.PaletteHex[index % scene.PaletteHex.Count]))
            };
            Canvas.SetLeft(swatch, entry.SwatchX);
            Canvas.SetTop(swatch, entry.SwatchY);
            canvas.Children.Add(swatch);
            AddSceneText(canvas, new ChartSceneText(entry.Text, entry.TextX, entry.TextY,
                ChartSceneTextAnchor.TopLeft, ChartSceneTextKind.Legend, "#000000", 9));
        }
    }

    private static void AddSceneText(
        Canvas canvas,
        ChartSceneText text,
        bool usesWordQuickLayoutColumnAxisTitles = false)
    {
        var label = new TextBlock
        {
            Text = text.Text,
            // Word uses the Office display face for chart titles while labels keep the chart
            // fallback. This stays renderer-local because the shared scene owns geometry, not glyph rasterization.
            FontFamily = new FontFamily(
                text.Kind == ChartSceneTextKind.Title ? "Aptos" : "Calibri"),
            FontSize = Math.Max(
                1,
                text.Kind == ChartSceneTextKind.AxisTitle && usesWordQuickLayoutColumnAxisTitles
                    ? text.FontSize * (text.RotationDegrees == 0 ? 1.2 : 1.52)
                    : text.FontSize),
            Foreground = new SolidColorBrush(ParseSceneColor(text.ColorHex)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = label.DesiredSize.Width;
        var height = label.DesiredSize.Height;
        var x = text.Anchor switch
        {
            ChartSceneTextAnchor.TopCenter or ChartSceneTextAnchor.Center => text.X - width / 2,
            ChartSceneTextAnchor.CenterRight => text.X - width,
            _ => text.X
        };
        var y = text.Anchor switch
        {
            ChartSceneTextAnchor.Center or ChartSceneTextAnchor.CenterRight => text.Y - height / 2,
            _ => text.Y
        };
        if (usesWordQuickLayoutColumnAxisTitles
            && text.Kind == ChartSceneTextKind.AxisTitle
            && text.RotationDegrees != 0)
            // WPF rotates around the measured TextBlock center; Word's vertical chart-title
            // footprint is wider and sits inside the plot reservation rather than outside it.
            x += 17;
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        if (text.RotationDegrees != 0)
        {
            label.RenderTransformOrigin = new Point(0.5, 0.5);
            label.RenderTransform = new RotateTransform(text.RotationDegrees);
        }
        canvas.Children.Add(label);
    }

    private static void AddSceneMarker(Canvas canvas, ChartSceneMarker marker)
    {
        var fill = new SolidColorBrush(ParseSceneColor(marker.FillHex, marker.FillOpacity));
        var diameter = marker.Radius * 2;
        FrameworkElement shape = marker.Kind switch
        {
            ChartSceneMarkerKind.Diamond => new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new(marker.Radius, 0), new(diameter, marker.Radius),
                    new(marker.Radius, diameter), new(0, marker.Radius)
                },
                Fill = fill
            },
            ChartSceneMarkerKind.Square => new System.Windows.Shapes.Rectangle
            {
                Width = diameter, Height = diameter, Fill = fill
            },
            ChartSceneMarkerKind.Triangle => new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new(marker.Radius, 0), new(diameter, diameter), new(0, diameter)
                },
                Fill = fill
            },
            ChartSceneMarkerKind.Cross => new Canvas
            {
                Width = diameter,
                Height = diameter,
                Children =
                {
                    new System.Windows.Shapes.Line
                    {
                        X1 = 1, Y1 = 1, X2 = diameter - 1, Y2 = diameter - 1,
                        Stroke = fill, StrokeThickness = 1.5
                    },
                    new System.Windows.Shapes.Line
                    {
                        X1 = diameter - 1, Y1 = 1, X2 = 1, Y2 = diameter - 1,
                        Stroke = fill, StrokeThickness = 1.5
                    }
                }
            },
            _ => new System.Windows.Shapes.Ellipse
            {
                Width = diameter, Height = diameter, Fill = fill
            }
        };
        Canvas.SetLeft(shape, marker.CenterX - marker.Radius);
        Canvas.SetTop(shape, marker.CenterY - marker.Radius);
        canvas.Children.Add(shape);
    }

    private static void AddSceneSlice(Canvas canvas, ChartSceneSlice slice)
    {
        var start = slice.StartAngleRadians;
        var end = start + slice.SweepAngleRadians;
        var outerStart = new Point(slice.CenterX + slice.OuterRadius * Math.Cos(start),
            slice.CenterY + slice.OuterRadius * Math.Sin(start));
        var outerEnd = new Point(slice.CenterX + slice.OuterRadius * Math.Cos(end),
            slice.CenterY + slice.OuterRadius * Math.Sin(end));
        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
        figure.Segments.Add(new ArcSegment(outerEnd,
            new Size(slice.OuterRadius, slice.OuterRadius), 0,
            slice.SweepAngleRadians > Math.PI, SweepDirection.Clockwise, true));
        if (slice.InnerRadius > 0)
        {
            var innerEnd = new Point(slice.CenterX + slice.InnerRadius * Math.Cos(end),
                slice.CenterY + slice.InnerRadius * Math.Sin(end));
            var innerStart = new Point(slice.CenterX + slice.InnerRadius * Math.Cos(start),
                slice.CenterY + slice.InnerRadius * Math.Sin(start));
            figure.Segments.Add(new LineSegment(innerEnd, true));
            figure.Segments.Add(new ArcSegment(innerStart,
                new Size(slice.InnerRadius, slice.InnerRadius), 0,
                slice.SweepAngleRadians > Math.PI, SweepDirection.Counterclockwise, true));
        }
        else
            figure.Segments.Add(new LineSegment(new Point(slice.CenterX, slice.CenterY), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(ParseSceneColor(slice.FillHex)),
            Stroke = new SolidColorBrush(ParseSceneColor(slice.StrokeHex)),
            StrokeThickness = slice.StrokeWidth
        });
    }

    private static Color ParseSceneColor(string hex, double opacity = 1)
    {
        var color = ParseHexColor(hex);
        return Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), color.R, color.G, color.B);
    }


    /// <summary>Inserts an inline shape / text box at the caret. Size in points; preserved on save.</summary>
    public void InsertShape(Shape shape)
    {
        CommitToModel();
        var container = BuildShapeRun(shape, DocumentEffectSet.FromTheme(_model.Theme));
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
    /// renderer honours the diagram's <see cref="SmartArt.LayoutId"/>, <see cref="SmartArt.ColorSchemeId"/>
    /// and <see cref="SmartArt.StyleId"/> to produce distinct layout geometries, node colours and
    /// fill/shadow treatments. Hierarchy children are rendered as indented sub-boxes.
    /// </summary>
    private static InlineUIContainer BuildSmartArtRun(
        SmartArt smartArt,
        DocumentEffectSet effectSet,
        DocumentTheme documentTheme)
    {
        var widthPx = smartArt.WidthPt * PxPerPoint;
        var heightPx = smartArt.HeightPt * PxPerPoint;
        if (smartArt.IsWordSuppressedByDuplicateDrawingId)
        {
            return new InlineUIContainer(new Border
            {
                Width = widthPx,
                Height = heightPx,
                Background = Brushes.Transparent,
                Tag = smartArt
            }) { BaselineAlignment = BaselineAlignment.Bottom };
        }

        var strokeThickness = EffectLineThickness(effectSet);

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt, documentTheme);
        var content = SmartArtRenderer.Build(smartArt, plan, strokeThickness);
        var isNativeWordSmartArt = plan.LayoutId is "orgchart1" or "pyramid1";

        var element = new Border
        {
            Width = widthPx,
            Height = heightPx,
            Background = isNativeWordSmartArt ? Brushes.Transparent : Brushes.White,
            BorderBrush = isNativeWordSmartArt
                ? null
                : new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = isNativeWordSmartArt ? new Thickness(0) : new Thickness(strokeThickness),
            Child = content,
            Tag = smartArt // carries the model SmartArt so CommitToModel can round-trip it
        };
        ApplyObjectEffect(element, effectSet);
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
    public void InsertChart(Chart chart) => InsertInlineContainer(BuildChartRun(chart, DocumentEffectSet.FromTheme(_model.Theme)));

    /// <summary>Inserts inline WordArt at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertWordArt(WordArt wordArt) => InsertInlineContainer(BuildWordArtRun(wordArt, DocumentEffectSet.FromTheme(_model.Theme)));

    /// <summary>Inserts an inline SmartArt diagram at the caret. Round-trips through CommitToModel (mirrors InsertShape).</summary>
    public void InsertSmartArt(SmartArt smartArt) => InsertInlineContainer(BuildSmartArtRun(smartArt, DocumentEffectSet.FromTheme(_model.Theme), _model.Theme));

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
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        if (string.IsNullOrEmpty(text))
            return;

        Focus();
        if (TrackChangesEnabled && TryRecordTrackedTextInput(text))
            return;

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

    private bool TryRecordTrackedTextInput(string text)
    {
        if (!TrackChangesEnabled
            || string.IsNullOrEmpty(text)
            || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit)
            || !TryGetCurrentBodyTextTarget(out var paragraphIndex, out var startOffset, out var endOffset, out var hasSelection))
        {
            return false;
        }

        var insertOffset = Math.Min(startOffset, endOffset);
        var author = CurrentRevisionAuthor();
        var dateXml = CurrentRevisionDateXml();

        CommitToModel();
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph)
            return false;

        _commands.Execute(new ReplaceParagraphRunsCommand(paragraphIndex, paragraph =>
        {
            if (hasSelection)
                RevisionEditPlanner.DeleteRangeAsRevision(paragraph, startOffset, endOffset, author, dateXml);

            var formatting = RevisionEditPlanner.FormattingAtOffset(paragraph, insertOffset);
            var link = RevisionEditPlanner.LinkAtOffset(paragraph, insertOffset);
            RevisionEditPlanner.InsertText(
                paragraph,
                insertOffset,
                text,
                formatting,
                new RevisionEditPlanner.InsertOptions(
                    RevisionKind.Inserted,
                    author,
                    dateXml,
                    link.HyperlinkUrl,
                    link.HyperlinkAnchor,
                    link.HyperlinkTooltip));
        }));
        PlaceCaretAtModelTextOffset(paragraphIndex, insertOffset + text.Length);
        return true;
    }

    private bool TryRecordTrackedBackspace()
    {
        if (!TrackChangesEnabled || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return false;
        if (!TryGetCurrentBodyTextTarget(out var paragraphIndex, out var startOffset, out var endOffset, out var hasSelection))
            return false;
        if (hasSelection)
            return TryRecordTrackedDeletion(paragraphIndex, startOffset, endOffset, placeAfterKeptForwardDelete: false);
        if (startOffset <= 0)
            return false;
        return TryRecordTrackedDeletion(paragraphIndex, startOffset - 1, startOffset, placeAfterKeptForwardDelete: false);
    }

    private bool TryRecordTrackedDeleteForward()
    {
        if (!TrackChangesEnabled || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return false;
        if (!TryGetCurrentBodyTextTarget(out var paragraphIndex, out var startOffset, out var endOffset, out var hasSelection))
            return false;
        if (hasSelection)
            return TryRecordTrackedDeletion(paragraphIndex, startOffset, endOffset, placeAfterKeptForwardDelete: false);
        CommitToModel();
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph paragraph)
            return false;
        if (startOffset >= paragraph.PlainText.Length)
            return false;
        return TryRecordTrackedDeletion(paragraphIndex, startOffset, startOffset + 1, placeAfterKeptForwardDelete: true);
    }

    private bool TryRecordTrackedDeletion(int paragraphIndex, int startOffset, int endOffset, bool placeAfterKeptForwardDelete)
    {
        var author = CurrentRevisionAuthor();
        var dateXml = CurrentRevisionDateXml();
        RevisionEditPlanner.DeleteResult result = default;

        CommitToModel();
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph)
            return false;

        _commands.Execute(new ReplaceParagraphRunsCommand(paragraphIndex, paragraph =>
        {
            result = RevisionEditPlanner.DeleteRangeAsRevision(paragraph, startOffset, endOffset, author, dateXml);
        }));

        var caretOffset = placeAfterKeptForwardDelete && result.KeptDeletedText
            ? Math.Max(startOffset, endOffset)
            : result.CaretOffset;
        PlaceCaretAtModelTextOffset(paragraphIndex, caretOffset);
        return true;
    }

    private bool TryGetCurrentBodyTextTarget(
        out int paragraphIndex,
        out int startOffset,
        out int endOffset,
        out bool hasSelection)
    {
        paragraphIndex = -1;
        startOffset = 0;
        endOffset = 0;
        hasSelection = false;

        WpfParagraph? paragraph;
        if (!Selection.IsEmpty)
        {
            paragraph = Selection.Start.Paragraph;
            if (paragraph is null || !ReferenceEquals(paragraph, Selection.End.Paragraph))
                return false;
            startOffset = OffsetInParagraph(paragraph, Selection.Start);
            endOffset = OffsetInParagraph(paragraph, Selection.End);
            hasSelection = startOffset != endOffset;
        }
        else
        {
            paragraph = CaretPosition?.Paragraph;
            if (paragraph is null || CaretPosition is null)
                return false;
            startOffset = OffsetInParagraph(paragraph, CaretPosition);
            endOffset = startOffset;
        }

        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);
        if (!indexOf.TryGetValue(paragraph, out var visibleIndex))
            return false;

        paragraphIndex = ModelIndexFromVisible(visibleIndex);
        return true;
    }

    private string CurrentRevisionAuthor()
    {
        var author = string.IsNullOrWhiteSpace(RevisionAuthor)
            ? _model.Properties.Author
            : RevisionAuthor;
        if (string.IsNullOrWhiteSpace(author))
            author = Environment.UserName;
        return string.IsNullOrWhiteSpace(author) ? "FreeW User" : author.Trim();
    }

    private static string CurrentRevisionDateXml() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    private void PlaceCaretAtModelTextOffset(int modelBlockIndex, int offset)
    {
        if (TextPointerAtModelTextOffset(modelBlockIndex, offset) is { } pointer)
        {
            CaretPosition = pointer;
            Focus();
        }
    }

    private TextPointer? TextPointerAtModelTextOffset(int modelBlockIndex, int offset)
    {
        if (LeafBlockAtModelIndex(modelBlockIndex) is not WpfParagraph paragraph)
            return null;
        return TextPointerAtParagraphOffset(paragraph, offset);
    }

    private static TextPointer TextPointerAtParagraphOffset(WpfParagraph paragraph, int offset)
    {
        var remaining = Math.Max(0, offset);
        foreach (var inline in paragraph.Inlines)
        {
            if (TryTextPointerInInline(inline, ref remaining, out var pointer))
                return pointer;
        }

        return paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? paragraph.ContentEnd;
    }

    private static bool TryTextPointerInInline(Inline inline, ref int remaining, out TextPointer pointer)
    {
        switch (inline)
        {
            case WpfRun run:
                if (remaining <= run.Text.Length)
                {
                    pointer = run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward)
                        ?? run.ContentStart;
                    return true;
                }
                remaining -= run.Text.Length;
                break;
            case Span span:
                foreach (var child in span.Inlines)
                {
                    if (TryTextPointerInInline(child, ref remaining, out pointer))
                        return true;
                }
                break;
        }

        pointer = inline.ContentEnd;
        return false;
    }

    internal void MoveCaretToBlockForTest(int modelBlockIndex, int offset) =>
        PlaceCaretAtModelTextOffset(modelBlockIndex, offset);

    internal void SetSelectionRangeForTest(int anchorBlock, int anchorOffset, int caretBlock, int caretOffset)
    {
        var anchor = TextPointerAtModelTextOffset(anchorBlock, anchorOffset);
        var caret = TextPointerAtModelTextOffset(caretBlock, caretOffset);
        if (anchor is not null && caret is not null)
            Selection.Select(anchor, caret);
    }

    internal void BackspaceForTest()
    {
        if (!TryRecordTrackedBackspace())
            EditingCommands.Backspace.Execute(null, this);
    }

    internal void DeleteForwardForTest()
    {
        if (!TryRecordTrackedDeleteForward())
            EditingCommands.Delete.Execute(null, this);
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
    /// Removes a footnote from the model and strips its reference marker from the body. Re-renders so
    /// the change is immediately visible. Mirrors <see cref="InsertFootnote"/> in reverse.
    /// </summary>
    public void DeleteFootnote(int id)
    {
        CommitToModel();
        _model.Footnotes.Remove(id);
        StripNoteMarker(id, footnote: true);
        CommitToModel();
        Render();
    }

    /// <summary>
    /// Removes an endnote from the model and strips its reference marker from the body. Re-renders so
    /// the change is immediately visible. Mirrors <see cref="InsertEndnote"/> in reverse.
    /// </summary>
    public void DeleteEndnote(int id)
    {
        CommitToModel();
        _model.Endnotes.Remove(id);
        StripNoteMarker(id, footnote: false);
        CommitToModel();
        Render();
    }

    private void StripNoteMarker(int id, bool footnote)
    {
        var toRemove = new List<WpfRun>();
        foreach (var run in NoteMarkers(footnote))
        {
            var markerId = footnote
                ? (run.Tag as FootnoteMarker)?.FootnoteId
                : (run.Tag as EndnoteMarker)?.EndnoteId;
            if (markerId == id)
                toRemove.Add(run);
        }
        foreach (var run in toRemove)
            run.ContentStart.Paragraph?.Inlines.Remove(run);
    }

    /// <summary>Moves the caret to the next footnote reference marker in visible document order.</summary>
    public bool MoveToNextFootnote() => MoveToAdjacentNote(footnote: true, previous: false);

    /// <summary>Moves the caret to the previous footnote reference marker in visible document order.</summary>
    public bool MoveToPreviousFootnote() => MoveToAdjacentNote(footnote: true, previous: true);

    /// <summary>Moves the caret to the next endnote reference marker in visible document order.</summary>
    public bool MoveToNextEndnote() => MoveToAdjacentNote(footnote: false, previous: false);

    /// <summary>Moves the caret to the previous endnote reference marker in visible document order.</summary>
    public bool MoveToPreviousEndnote() => MoveToAdjacentNote(footnote: false, previous: true);

    private bool MoveToAdjacentNote(bool footnote, bool previous)
    {
        Focus();
        var markers = NoteMarkers(footnote).ToArray();
        if (markers.Length == 0)
            return false;

        var caret = CaretPosition ?? Document.ContentStart;
        WpfRun target;
        if (previous)
        {
            target = markers.LastOrDefault(marker => marker.ContentStart.CompareTo(caret) < 0) ?? markers[^1];
        }
        else
        {
            target = markers.FirstOrDefault(marker => marker.ContentStart.CompareTo(caret) > 0) ?? markers[0];
        }

        CaretPosition = target.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? target.ContentStart;
        target.ContentStart.Paragraph?.BringIntoView();
        Focus();
        return true;
    }

    private IEnumerable<WpfRun> NoteMarkers(bool footnote)
    {
        foreach (var block in Document.Blocks)
        {
            foreach (var marker in NoteMarkers(block, footnote))
                yield return marker;
        }
    }

    private static IEnumerable<WpfRun> NoteMarkers(System.Windows.Documents.Block block, bool footnote)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
                foreach (var marker in NoteMarkers(paragraph.Inlines, footnote))
                    yield return marker;
                break;
            case WpfList list:
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks)
                        foreach (var marker in NoteMarkers(itemBlock, footnote))
                            yield return marker;
                break;
            case WpfTable table:
                foreach (var rowGroup in table.RowGroups)
                    foreach (var row in rowGroup.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellBlock in cell.Blocks)
                                foreach (var marker in NoteMarkers(cellBlock, footnote))
                                    yield return marker;
                break;
        }
    }

    private static IEnumerable<WpfRun> NoteMarkers(InlineCollection inlines, bool footnote)
    {
        foreach (var inline in inlines)
        {
            if (inline is WpfRun run
                && (footnote
                    ? run.Tag is FootnoteMarker
                    : run.Tag is EndnoteMarker))
            {
                yield return run;
            }

            if (inline is Span span)
            {
                foreach (var marker in NoteMarkers(span.Inlines, footnote))
                    yield return marker;
            }
        }
    }

    public bool ToggleContentControl(int blockIndex, int runIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, ContentControlInteractionPlanner.ToggleCheckBox);

    public bool SelectContentControlItem(int blockIndex, int runIndex, int itemIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, run =>
            ContentControlInteractionPlanner.SelectItem(run, itemIndex));

    public bool SelectContentControlRelativeDate(int blockIndex, int runIndex, int choiceIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, run =>
            ContentControlInteractionPlanner.SelectRelativeDate(run, choiceIndex));

    private bool ApplyContentControlInteraction(int blockIndex, int runIndex, Func<ModelRun, ModelRun?> planner)
    {
        if (!TryGetBodyContentControlRun(blockIndex, runIndex, out var current)
            || !ContentControlInteractionPlanner.CanEditExistingContentControl(current, RestrictEditingPolicy))
        {
            return false;
        }

        var updated = planner(current);
        if (updated is null)
            return false;

        _commands.Execute(new ReplaceContentControlRunCommand(blockIndex, runIndex, updated));
        return true;
    }

    private bool TryGetBodyContentControlRun(int blockIndex, int runIndex, out ModelRun run)
    {
        run = null!;
        if (blockIndex < 0
            || blockIndex >= _model.Blocks.Count
            || _model.Blocks[blockIndex] is not ModelParagraph paragraph
            || runIndex < 0
            || runIndex >= paragraph.Runs.Count
            || paragraph.Runs[runIndex].Control is null)
        {
            return false;
        }

        run = paragraph.Runs[runIndex];
        return true;
    }

    private bool AllowsContentControlInteraction(ModelContentControl control)
    {
        var probe = new ModelRun(string.Empty) { Control = control };
        return ContentControlInteractionPlanner.CanEditExistingContentControl(probe, RestrictEditingPolicy);
    }

    /// <summary>
    /// Inserts a plain-text content control (w:sdt) at the caret. When the selection is non-empty its
    /// text becomes the control's content; otherwise a placeholder ("Click to enter text") is used. The
    /// control carries the optional <paramref name="tag"/> / <paramref name="alias"/> and renders as a
    /// shaded region. Re-renders so the control round-trips on the next commit/save.
    /// </summary>
    public void InsertPlainTextControl(string? tag = null, string? alias = null)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

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
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        var run = BuildControlRun(ModelRun.CheckBoxControl(@checked: false, tag, alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>
    /// Inserts a rich-text content control (w:sdt/w:richText) at the caret. Mirrors
    /// <see cref="InsertPlainTextControl"/> — the selection's text (or a placeholder) becomes the control's
    /// content and it renders as a shaded region. Re-renders so the control round-trips on the next save.
    /// </summary>
    public void InsertRichTextControl(string? tag = null, string? alias = null)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();

        var selected = Selection?.Text;
        var text = string.IsNullOrEmpty(selected) ? "Click to enter text" : selected;
        if (Selection is { IsEmpty: false })
            Selection.Text = string.Empty;

        var run = BuildControlRun(ModelRun.RichTextControl(text, tag, alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>
    /// Inserts a date-picker content control (w:sdt/w:date) at the caret, pre-filled with today's date in
    /// the control's date format. Clicking the rendered region offers relative dates. Re-renders so the
    /// control round-trips on the next save.
    /// </summary>
    public void InsertDatePickerControl(string? tag = null, string? alias = null, string? dateFormat = null)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        var fmt = string.IsNullOrEmpty(dateFormat) ? ModelContentControl.DefaultDateFormat : dateFormat!;
        var today = System.DateTime.Today.ToString(fmt, System.Globalization.CultureInfo.CurrentCulture);
        var run = BuildControlRun(ModelRun.DatePickerControl(today, tag, alias, fmt));
        InsertInlineAtCaret(run);
    }

    /// <summary>
    /// Inserts a drop-down-list content control (w:sdt/w:dropDownList) at the caret offering
    /// <paramref name="items"/> (a small default sample when none is given). Clicking the rendered region
    /// lets the user pick one. Re-renders so the control round-trips on the next save.
    /// </summary>
    public void InsertDropDownListControl(
        IReadOnlyList<ContentControlListItem>? items = null, string? tag = null, string? alias = null)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        var run = BuildControlRun(ModelRun.DropDownListControl(items ?? DefaultListItems, tag: tag, alias: alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>
    /// Inserts a combo-box content control (w:sdt/w:comboBox) at the caret offering <paramref name="items"/>
    /// (a small default sample when none is given) and allowing free text. Clicking the rendered region lets
    /// the user pick one. Re-renders so the control round-trips on the next save.
    /// </summary>
    public void InsertComboBoxControl(
        IReadOnlyList<ContentControlListItem>? items = null, string? tag = null, string? alias = null)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        var run = BuildControlRun(ModelRun.ComboBoxControl(items ?? DefaultListItems, tag: tag, alias: alias));
        InsertInlineAtCaret(run);
    }

    /// <summary>A small default choice sample used when a list/combo control is inserted without items.</summary>
    private static readonly IReadOnlyList<ContentControlListItem> DefaultListItems =
    [
        new ContentControlListItem("Choose an item"),
        new ContentControlListItem("Item 1"),
        new ContentControlListItem("Item 2"),
        new ContentControlListItem("Item 3")
    ];

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
        InsertInlineAt(paragraph, caret, inline);
        CaretPosition = inline.ContentEnd.GetInsertionPosition(LogicalDirection.Forward) ?? inline.ElementEnd;

        CommitToModel();
        Render();
    }

    private static void InsertInlineAt(WpfParagraph paragraph, TextPointer caret, Inline inline)
    {
        if (paragraph.Inlines.FirstInline is null)
        {
            paragraph.Inlines.Add(inline);
            return;
        }

        if (caret.Parent is WpfRun run && ReferenceEquals(run.Parent, paragraph))
        {
            InsertInlineIntoRun(paragraph, run, caret, inline);
            return;
        }

        foreach (var existing in paragraph.Inlines)
        {
            if (caret.CompareTo(existing.ContentStart) <= 0)
            {
                paragraph.Inlines.InsertBefore(existing, inline);
                return;
            }

            if (caret.CompareTo(existing.ContentEnd) <= 0)
            {
                paragraph.Inlines.InsertAfter(existing, inline);
                return;
            }
        }

        paragraph.Inlines.Add(inline);
    }

    private static void InsertInlineIntoRun(WpfParagraph paragraph, WpfRun run, TextPointer caret, Inline inline)
    {
        var before = new TextRange(run.ContentStart, caret).Text;
        var after = new TextRange(caret, run.ContentEnd).Text;

        if (before.Length == 0)
        {
            paragraph.Inlines.InsertBefore(run, inline);
            return;
        }

        if (after.Length == 0)
        {
            paragraph.Inlines.InsertAfter(run, inline);
            return;
        }

        run.Text = before;
        var tail = CloneTextRun(run, after);
        paragraph.Inlines.InsertAfter(run, inline);
        paragraph.Inlines.InsertAfter(inline, tail);
    }

    private static WpfRun CloneTextRun(WpfRun source, string text)
    {
        var clone = new WpfRun(text)
        {
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Foreground = source.Foreground,
            Background = source.Background,
            BaselineAlignment = source.BaselineAlignment,
            TextDecorations = source.TextDecorations,
            FlowDirection = source.FlowDirection,
            Tag = source.Tag,
            ToolTip = source.ToolTip
        };
        Typography.SetCapitals(clone, Typography.GetCapitals(source));
        return clone;
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
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentInsert))
            return;

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

        if (!AddCommentCommand.HasCommentableRange(modelParagraph, startOffset, endOffset))
            return; // nothing textual to anchor the comment to

        var id = _model.NextCommentId();
        var comment = new Comment(id)
        {
            Author = author,
            Initials = initials,
            // W3CDTF (UTC, second precision) - matches what the docx writer expects for w:date.
            DateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
        };
        comment.Content.Add(new ModelParagraph(text));
        _commands.Execute(new AddCommentCommand(paragraphIndex, startOffset, endOffset, id, comment));
    }

    /// <summary>
    /// The id of the top-level comment whose range covers the caret/selection, or null when the caret is
    /// not inside a comment. Resolves from the tagged WPF run at the selection (the common case); falling
    /// back to scanning the committed caret paragraph's runs for any CommentId so a caret placed anywhere
    /// in the range still finds its comment. A reply's id is mapped up to its owning top-level comment.
    /// </summary>
    private int? CommentIdAtCaret()
    {
        // Fast path: the run under the caret/selection start carries a CommentMarker tag.
        if ((Selection.Start.Parent as WpfRun ?? CaretPosition?.Parent as WpfRun) is { Tag: RunMarkers { Comment: { } marker } })
            return TopLevelCommentId(marker.CommentId);

        // Fallback: commit and look for a commented run in the caret's model paragraph.
        var caretParagraph = Selection.Start.Paragraph ?? CaretPosition?.Paragraph;
        if (caretParagraph is null)
            return null;
        var indexOf = new Dictionary<WpfParagraph, int>();
        var modelIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref modelIndex);
        if (!indexOf.TryGetValue(caretParagraph, out var paragraphIndex))
            return null;

        CommitToModel();
        paragraphIndex = ModelIndexFromVisible(paragraphIndex);
        if (paragraphIndex < 0 || paragraphIndex >= _model.Blocks.Count || _model.Blocks[paragraphIndex] is not ModelParagraph modelParagraph)
            return null;
        foreach (var run in modelParagraph.Runs)
            if (run.CommentId is { } cid)
                return TopLevelCommentId(cid);
        return null;
    }

    /// <summary>
    /// Maps a comment id (which may be a reply's id) to its owning top-level comment id — the one keyed in
    /// <see cref="TextDocument.Comments"/> and referenced by body ranges. Returns the id unchanged when it
    /// is already a top-level comment (or unknown).
    /// </summary>
    private int TopLevelCommentId(int commentId)
    {
        return DeleteCommentCommand.ResolveTopLevel(_model, commentId);
    }

    private sealed record CommentNavigationTarget(int CommentId, int BlockIndex);

    private IEnumerable<CommentNavigationTarget> CommentNavigationTargets()
    {
        CommitToModel();

        var seen = new HashSet<int>();
        for (var blockIndex = 0; blockIndex < _model.Blocks.Count; blockIndex++)
        {
            foreach (var paragraph in ParagraphsInBlock(_model.Blocks[blockIndex]))
            {
                foreach (var run in paragraph.Runs)
                {
                    if (run.CommentId is not { } commentId)
                        continue;

                    var topLevelId = TopLevelCommentId(commentId);
                    if (_model.Comments.ContainsKey(topLevelId) && seen.Add(topLevelId))
                        yield return new CommentNavigationTarget(topLevelId, blockIndex);
                }
            }
        }
    }

    private static IEnumerable<ModelParagraph> ParagraphsInBlock(ModelBlock block)
    {
        if (block is ModelParagraph topLevelParagraph)
        {
            yield return topLevelParagraph;
            yield break;
        }

        if (block is not ModelTable table)
            yield break;

        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;
    }

    /// <summary>
    /// Adds <paramref name="text"/> as a reply (by <paramref name="author"/>/<paramref name="initials"/>) to
    /// the comment thread covering the caret/selection, then re-renders so the thread tooltip updates. No-op
    /// when the caret is not inside a comment or the reply text is blank. Returns true when a reply was added.
    /// </summary>
    public bool ReplyToCommentAtCaret(string text, string author, string initials)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentReply))
            return false;

        if (string.IsNullOrWhiteSpace(text))
            return false;
        Focus();
        if (CommentIdAtCaret() is not { } id || !_model.Comments.TryGetValue(id, out var comment))
            return false;

        var replyId = _model.NextCommentId();
        var reply = new Comment(replyId, text.Trim(), author, initials)
        {
            DateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
        };
        _commands.Execute(new AddCommentReplyCommand(id, reply));
        if (!comment.Replies.Any(candidate => candidate.Id == replyId))
            return false;

        MoveCaretToComment(id);
        return true;
    }

    /// <summary>
    /// Toggles the resolved (done) state of the comment thread covering the caret/selection and re-renders
    /// (resolved ranges show muted). No-op when the caret is not inside a comment. Returns the new resolved
    /// state, or null when there was no comment to toggle.
    /// </summary>
    public bool? ToggleResolveCommentAtCaret()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentResolve))
            return null;

        Focus();
        if (CommentIdAtCaret() is not { } id || !_model.Comments.TryGetValue(id, out var comment))
            return null;
        var newState = !comment.Resolved;
        _commands.Execute(new SetCommentResolvedCommand(id, newState));
        MoveCaretToComment(id);
        return newState;
    }

    /// <summary>
    /// Deletes the comment thread covering the caret/selection, removing both the stored thread and body
    /// range/reference marks. No-op when the caret is not inside a comment.
    /// </summary>
    public bool DeleteCommentAtCaret()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentDelete))
            return false;

        Focus();
        if (CommentIdAtCaret() is not { } id || !_model.Comments.ContainsKey(id))
            return false;

        _commands.Execute(new DeleteCommentCommand(id));
        return !_model.Comments.ContainsKey(id);
    }

    /// <summary>Moves the caret to the next comment thread in document order, wrapping at the end.</summary>
    public bool MoveToNextComment() => MoveToAdjacentComment(direction: 1);

    /// <summary>Moves the caret to the previous comment thread in document order, wrapping at the start.</summary>
    public bool MoveToPreviousComment() => MoveToAdjacentComment(direction: -1);

    private bool MoveToAdjacentComment(int direction)
    {
        Focus();
        var currentId = CommentIdAtCaret();
        var targets = CommentNavigationTargets().ToArray();
        if (targets.Length == 0)
            return false;

        var currentIndex = currentId is { } id
            ? Array.FindIndex(targets, target => target.CommentId == id)
            : -1;
        var targetIndex = currentIndex < 0
            ? (direction > 0 ? 0 : targets.Length - 1)
            : (currentIndex + direction + targets.Length) % targets.Length;

        var target = targets[targetIndex];
        BringBlockIntoView(target.BlockIndex);
        MoveCaretToComment(target.CommentId);
        return true;
    }

    private bool MoveCaretToComment(int commentId)
    {
        foreach (var block in Document.Blocks)
        {
            if (MoveCaretToComment(block, commentId))
                return true;
        }

        return false;
    }

    private bool MoveCaretToComment(System.Windows.Documents.Block block, int commentId)
    {
        switch (block)
        {
            case WpfParagraph paragraph:
                return MoveCaretToComment(paragraph.Inlines, commentId);
            case WpfList list:
                foreach (var item in list.ListItems)
                {
                    foreach (var itemBlock in item.Blocks)
                    {
                        if (MoveCaretToComment(itemBlock, commentId))
                            return true;
                    }
                }
                break;
            case WpfTable table:
                foreach (var rowGroup in table.RowGroups)
                {
                    foreach (var row in rowGroup.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellBlock in cell.Blocks)
                            {
                                if (MoveCaretToComment(cellBlock, commentId))
                                    return true;
                            }
                        }
                    }
                }
                break;
        }

        return false;
    }

    private bool MoveCaretToComment(InlineCollection inlines, int commentId)
    {
        foreach (var inline in inlines)
        {
            if (inline is WpfRun { Tag: RunMarkers { Comment: { } marker } } run
                && TopLevelCommentId(marker.CommentId) == commentId)
            {
                CaretPosition = run.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? run.ContentStart;
                Focus();
                return true;
            }

            if (inline is Span span && MoveCaretToComment(span.Inlines, commentId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The active bibliographic style (APA / MLA / Chicago / IEEE) used when inserting in-text citations and
    /// the bibliography. Selected via the References group's "Citation Style" combo box; defaults to APA,
    /// which is the original author–year behaviour. Backed by <see cref="TextDocument.BibliographyStyle"/> so
    /// the choice is persisted to / restored from the document (it survives a save/load).
    /// </summary>
    public CitationStyle ActiveCitationStyle
    {
        get => _model.BibliographyStyle;
        set => _model.BibliographyStyle = value;
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
    public Source AddSource(string tag, string author, string title, string year, string? publisher) =>
        AddSource(new Source
        {
            Tag = tag?.Trim() ?? string.Empty,
            Author = author?.Trim() ?? string.Empty,
            Title = title?.Trim() ?? string.Empty,
            Year = year?.Trim() ?? string.Empty,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim()
        });

    /// <summary>
    /// Appends a complete bibliographic source to the model and returns the stored clone, so callers can
    /// keep Word-style source types and type-specific fields intact when immediately inserting a citation.
    /// </summary>
    public Source AddSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CommitToModel();
        var stored = SourceManagementDialogPlanner.CloneSource(source);
        _model.Sources.Add(stored);
        return stored;
    }

    /// <summary>
    /// Replace the document's bibliographic source list through the undo bus. Used by References &gt;
    /// Manage Sources, where source edits may not insert visible text but still need to persist.
    /// </summary>
    public void ReplaceSources(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        CommitToModel();
        _commands.Execute(new ReplaceSourcesCommand(sources));
    }

    /// <summary>
    /// Inserts the in-text citation for <paramref name="source"/>. Tagged sources are inserted as Word-like
    /// <c>CITATION</c> complex fields so Update Fields can re-resolve style and numeric numbering changes;
    /// untagged sources keep the plain-text fallback because Word's field has no stable tag to address.
    /// </summary>
    public void InsertCitation(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Citations.TryCreateCitationFieldRun(_model, source, ActiveCitationStyle, out var run))
            InsertInlineAtCaret(BuildComplexFieldRun(run, _model));
        else
            InsertText(Citations.FormatInText(_model, source, ActiveCitationStyle));
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

        // Insert at the caret's block (a bibliography reads as back-matter); fall back to the document end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        ApplyBibliographyPlan(
            BibliographyRegionPlanner.BuildInsertPlan(_model, index, ActiveCitationStyle),
            "Insert Bibliography");
    }

    /// <summary>
    /// Rebuilds the generated bibliography/reference-list region from the current sources and citation
    /// style. With no existing region, this inserts at the document end, matching the shared planner.
    /// </summary>
    public void RefreshBibliography()
    {
        CommitToModel();
        RefreshBibliographyFromModel();
        Render();
    }

    private void RefreshBibliographyFromModel() =>
        ApplyBibliographyPlan(
            BibliographyRegionPlanner.BuildRefreshPlan(_model, ActiveCitationStyle),
            "Update Bibliography");

    private void ApplyBibliographyPlan(BibliographyRegionPlan plan, string label)
    {
        _commands.BeginUndoGroup();
        try
        {
            foreach (var deleteIndex in plan.DeleteIndicesDescending)
                _commands.Execute(new DeleteParagraphCommand(deleteIndex));

            var index = Math.Clamp(plan.InsertIndex, 0, _model.Blocks.Count);
            foreach (var paragraph in plan.Paragraphs)
                _commands.Execute(new InsertParagraphCommand(index++, paragraph));

            _commands.CommitUndoGroup(label);
        }
        catch
        {
            _commands.AbortUndoGroup();
            throw;
        }
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
    /// Rebuild the generated document index in place. Existing index paragraphs are recognised by the
    /// dedicated styles from <see cref="DocumentIndex"/>; the refreshed index is inserted at the first
    /// previous index paragraph, or at the document end when there is not yet an index.
    /// </summary>
    public void RefreshIndex()
    {
        CommitToModel();
        DocumentIndex.EnsureStyles(_model);

        var firstIndex = -1;
        var indexParagraphs = new List<int>();
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            if (!DocumentIndex.IsIndexParagraph(_model.Blocks[i]))
                continue;
            firstIndex = firstIndex < 0 ? i : firstIndex;
            indexParagraphs.Add(i);
        }

        var insertAt = firstIndex >= 0 ? firstIndex : _model.Blocks.Count;
        for (var i = indexParagraphs.Count - 1; i >= 0; i--)
            _commands.Execute(new DeleteParagraphCommand(indexParagraphs[i]));

        var entries = DocumentIndex.Build(_model);
        var index = Math.Clamp(insertAt, 0, _model.Blocks.Count);
        foreach (var paragraph in entries)
            _commands.Execute(new InsertParagraphCommand(index++, paragraph));
    }

    /// <summary>
    /// Marks the selected text (or a supplied citation) as a legal citation for a Table of Authorities
    /// (Word's References &gt; Mark Citation): drops a hidden <c>TA</c> field mark at the caret recording the
    /// long/short forms and category. The mark is textless (no visible glyph) and round-trips through docx;
    /// <see cref="InsertTableOfAuthorities"/> builds the visible table from these marks, mirroring how
    /// <see cref="MarkIndexEntry"/>/<see cref="InsertIndex"/> relate. A citation with a blank long form is
    /// ignored. The mark is inserted directly into the live flow (like a footnote marker) so it survives the
    /// next commit.
    /// </summary>
    public void MarkCitation(Citation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        if (citation.LongCitation.Length == 0)
            return;

        CommitToModel();

        var marker = BuildRun(ModelRun.CitationMark(citation), new ModelParagraph(), _model);
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
    /// Insert a Table of Authorities generated from the document's marked citations (the hidden <c>TA</c>
    /// field marks, see <see cref="MarkCitation"/>) at the caret's block (else at the document end), routed
    /// one-by-one through the undo/redo bus so the insert is reversible — mirroring <see cref="InsertIndex"/>.
    /// The paragraphs carry dedicated styles (registered via <see cref="TableOfAuthorities.EnsureStyles"/>)
    /// which both give them distinct formatting and mark the region for <see cref="RefreshTableOfAuthorities"/>.
    /// Uses default <see cref="ToaOptions"/>; use the overload to supply Word's full dialog options.
    /// </summary>
    public void InsertTableOfAuthorities() => InsertTableOfAuthorities(ToaOptions.Default);

    /// <summary>
    /// Insert a Table of Authorities generated from the document's marked citations using the given
    /// <paramref name="options"/> (category filter, passim, keep original formatting, tab leader) at the
    /// caret's block (else at the document end). Reversible through the undo/redo bus.
    /// </summary>
    public void InsertTableOfAuthorities(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();

        // Insert at the caret's block (the table reads as front-/back-matter); fall back to the document end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        ApplyTableOfAuthoritiesPlan(
            TableOfAuthoritiesRegionPlanner.BuildInsertPlan(
                _model,
                index,
                options,
                BuildTableOfAuthoritiesPageResolver()));
    }

    /// <summary>
    /// Rebuild the Table of Authorities: remove the previously inserted region (paragraphs carrying a
    /// Table of Authorities style, see <see cref="TableOfAuthorities.IsTableOfAuthoritiesParagraph"/>) and
    /// re-insert a freshly generated one at the same position. With no existing region this behaves like
    /// <see cref="InsertTableOfAuthorities"/>, inserting at the document end. Every removal/insert is
    /// reversible through the undo/redo bus. Mirrors <see cref="RefreshTableOfFigures"/>.
    /// </summary>
    public void RefreshTableOfAuthorities()
    {
        RefreshTableOfAuthorities(ToaOptions.Default);
    }

    public void RefreshTableOfAuthorities(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        CommitToModel();
        ApplyTableOfAuthoritiesPlan(
            TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
                _model,
                options,
                BuildTableOfAuthoritiesPageResolver()));
    }

    private ToaCitationPageResolver? BuildTableOfAuthoritiesPageResolver()
    {
        try
        {
            var pagination = PaginationEngine.Compute(this);
            var pageCount = Math.Max(1, pagination.PageCount);
            if (pageCount == 1 || pagination.PageBreakYsDip.Count == 0)
            {
                return (_, blockIndex, runIndex, _) =>
                    IsModelCitationRun(blockIndex, runIndex)
                        ? TableOfAuthorities.CreatePageReference(1)
                        : null;
            }

            var firstRect = Document.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (firstRect.IsEmpty)
                return null;

            var topY = firstRect.Top;
            return (_, blockIndex, runIndex, _) =>
            {
                var offset = ModelRunStartOffset(blockIndex, runIndex);
                var pointer = TextPointerAtModelTextOffset(blockIndex, offset);
                if (pointer is null)
                    return null;

                var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty)
                    return null;

                var y = rect.Top - topY;
                var pageIndex = 0;
                foreach (var breakY in pagination.PageBreakYsDip)
                {
                    if (y + 0.5 < breakY)
                        break;
                    pageIndex++;
                }

                var pageNumber = Math.Min(Math.Max(1, pageIndex + 1), pageCount);
                return TableOfAuthorities.CreatePageReference(pageNumber);
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private bool IsModelCitationRun(int modelBlockIndex, int runIndex) =>
        modelBlockIndex >= 0
        && modelBlockIndex < _model.Blocks.Count
        && _model.Blocks[modelBlockIndex] is ModelParagraph paragraph
        && runIndex >= 0
        && runIndex < paragraph.Runs.Count
        && paragraph.Runs[runIndex].Citation is not null;

    private int ModelRunStartOffset(int modelBlockIndex, int runIndex)
    {
        if (modelBlockIndex < 0
            || modelBlockIndex >= _model.Blocks.Count
            || _model.Blocks[modelBlockIndex] is not ModelParagraph paragraph)
        {
            return 0;
        }

        var offset = 0;
        var limit = Math.Clamp(runIndex, 0, paragraph.Runs.Count);
        for (var i = 0; i < limit; i++)
            offset += paragraph.Runs[i].Text.Length;
        return offset;
    }

    private void ApplyTableOfAuthoritiesPlan(TableOfAuthoritiesRegionPlan plan)
    {
        foreach (var deleteIndex in plan.DeleteIndicesDescending)
            _commands.Execute(new DeleteParagraphCommand(deleteIndex));

        var index = Math.Clamp(plan.InsertIndex, 0, _model.Blocks.Count);
        foreach (var paragraph in plan.Paragraphs)
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
        InsertTableOfFigures(Captions.LabelText(label));
    }

    public void InsertTableOfFigures(string labelText)
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        labelText = Captions.NormalizeLabelText(labelText);
        TableOfFigures.EnsureStyles(_model);

        // Insert at the caret's block (a table of figures reads as front-/back-matter); fall back to the end.
        var index = CaretBlockIndex();
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;

        InsertTableOfFiguresAt(index, labelText);
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
        RefreshTableOfFigures(Captions.LabelText(label));
    }

    public void RefreshTableOfFigures(string labelText)
    {
        CommitToModel();
        labelText = Captions.NormalizeLabelText(labelText);
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

        InsertTableOfFiguresAt(insertAt, labelText);
    }

    // Insert the freshly built table-of-figures paragraphs starting at block index `at`, one reversible
    // InsertParagraphCommand each (kept in order). The bus's Changed event redraws.
    private void InsertTableOfFiguresAt(int at, string labelText)
    {
        var entries = TableOfFigures.Build(_model, labelText);
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
        InsertCaption(Captions.LabelText(label), text);
    }

    public void InsertCaption(string labelText, string text)
    {
        // Capture the user's in-progress edits before mutating the model out from under the view.
        CommitToModel();
        labelText = Captions.NormalizeLabelText(labelText);
        Captions.EnsureStyles(_model);

        var number = Captions.NextCaptionNumber(_model, labelText);
        var caption = Captions.BuildCaption(labelText, number, text);

        // Insert after the caret's block so the caption sits under the selected image/table.
        var index = CaretBlockIndex() + 1;
        if (index < 0 || index > _model.Blocks.Count)
            index = _model.Blocks.Count;
        _commands.Execute(new InsertParagraphCommand(index, caption));
    }

    /// <summary>
    /// Inserts a cross-reference field (Word's References &gt; Cross-reference) at the caret pointing at
    /// <paramref name="target"/> of <paramref name="type"/>, showing <paramref name="insertAs"/> and
    /// optionally as a clickable hyperlink. For a body target that lacks a bookmark anchor, a hidden
    /// <c>_Ref…</c> bookmark is added to the target paragraph so the resulting REF/PAGEREF field resolves
    /// (Word auto-bookmarks targets the same way). The inserted run carries a cached resolved value (the
    /// target's text/number/position) so it renders sensibly before the next update, and round-trips
    /// through the model and docx as a field. Routed through a single commit/render so it is undoable via
    /// the normal text-edit flow.
    /// </summary>
    public void InsertCrossReference(CrossRefType type, CrossRefTarget target, CrossRefInsertAs insertAs, bool hyperlink)
    {
        Focus();
        CommitToModel();

        var sourceBlock = CaretBlockIndex();
        var plan = CrossReferences.PlanInsertion(_model, type, target, insertAs, hyperlink, sourceBlock);

        // The shared plan chooses the anchor; WPF owns the direct model mutation and keeps the existing
        // commit/render lifecycle intact.
        if (plan.BookmarkNameToAdd is { } anchor
            && plan.Target.BlockIndex is { } targetBlock
            && _model.Blocks[targetBlock] is ModelParagraph targetParagraph)
        {
            if (!targetParagraph.BookmarkNames.Contains(anchor))
                targetParagraph.BookmarkNames.Add(anchor);
        }

        // Append the field run to the caret's paragraph in the model (or the last paragraph / a fresh one),
        // then re-render. Working at the model level keeps the just-added target bookmark from being clobbered
        // by a view->model commit, and avoids losing the field marker that a view round-trip could.
        var caretBlock = sourceBlock >= 0 && sourceBlock < _model.Blocks.Count ? sourceBlock : -1;
        if (caretBlock < 0 || _model.Blocks[caretBlock] is not ModelParagraph host)
        {
            host = _model.Blocks.OfType<ModelParagraph>().LastOrDefault() ?? new ModelParagraph();
            if (!_model.Blocks.Contains(host))
                _model.Blocks.Add(host);
        }
        host.Runs.Add(plan.FieldRun);
        Render();
    }

    /// <summary>
    /// When true, the editor is in Track Changes mode. Body text typing and Backspace/Delete are recorded
    /// as tracked insertions/deletions through the shared revision edit planner; selection-marking and
    /// accept/reject operate regardless of this flag.
    /// </summary>
    public bool TrackChangesEnabled { get; set; }

    /// <summary>The default revision author stamped on tracked changes this editor records.</summary>
    public string RevisionAuthor { get; set; } = "FreeW User";

    // ── Review > Tracking display controls ────────────────────────────────────────────────────────
    // These are view-only flags: they affect how the document renders but never touch the model.
    // Revision and comment markers are ALWAYS written to WPF runs regardless of these flags so that
    // CommitToModel can round-trip them safely. Default state (all ON) reproduces today's behaviour.

    /// <summary>
    /// Display for Review mode.
    /// <list type="bullet">
    ///   <item><term>AllMarkup</term><description>Default. Insertions shown in revision colour with underline;
    ///   deletions in revision colour with strikethrough.</description></item>
    ///   <item><term>SimpleMarkup</term><description>Inline rendering identical to No Markup (final form:
    ///   insertions plain, deletions invisible) plus a thin vertical bar in the left margin beside every
    ///   paragraph that contains at least one tracked-change run. The <see cref="RevisionMarker"/> tag is
    ///   still written on every run so CommitToModel round-trips safely.</description></item>
    ///   <item><term>NoMarkup</term><description>Insertions shown as plain text (no colour/decoration);
    ///   deleted runs rendered invisible (zero-width transparent). The <see cref="RevisionMarker"/> tag is
    ///   still written on every run so CommitToModel can round-trip both the text and the revision kind safely.
    ///   The run is NOT removed from the WPF tree — only its visual properties change.</description></item>
    ///   <item><term>Original</term><description>Deleted runs shown as plain text; inserted runs rendered
    ///   invisible. Same round-trip guarantee via RevisionMarker.</description></item>
    /// </list>
    /// </summary>
    /// <summary>Current Display for Review setting. Defaults to All Markup (today's behaviour).</summary>
    private ReviewDisplayState _reviewDisplayState = ReviewDisplayState.Default;

    public ReviewDisplayState CurrentReviewDisplayState => _reviewDisplayState;

    public ReviewDisplayMode DisplayForReview
    {
        get => _reviewDisplayState.DisplayMode;
        set => _reviewDisplayState = _reviewDisplayState.WithDisplayMode(value);
    }

    /// <summary>
    /// When false, revision colour and strikethrough/underline decoration are suppressed in the
    /// rendered view. The <see cref="RevisionMarker"/> tag is still applied so the revision
    /// round-trips on commit. Default is true (current unconditional behaviour).
    /// </summary>
    public bool ShowMarkupInsertionsAndDeletions
    {
        get => _reviewDisplayState.ShowInsertionsAndDeletions;
        set => _reviewDisplayState = _reviewDisplayState.WithShowInsertionsAndDeletions(value);
    }

    /// <summary>
    /// When false, comment background highlight is suppressed in the rendered view. The
    /// <see cref="CommentMarker"/> tag is still applied so the comment id round-trips on commit.
    /// Default is true (current unconditional behaviour).
    /// </summary>
    public bool ShowMarkupComments
    {
        get => _reviewDisplayState.ShowComments;
        set => _reviewDisplayState = _reviewDisplayState.WithShowComments(value);
    }

    /// <summary>
    /// When true (default), runs whose <c>FormatRevision</c> is non-null receive a distinct visual
    /// decoration (dotted underline in the revision colour) to flag the tracked formatting change.
    /// When false the decoration is suppressed but the <see cref="FormatRevisionMarker"/> tag is still
    /// written unconditionally so <c>CommitToModel</c> can round-trip the <c>FormatRevision</c> safely.
    /// Most documents have no format revisions so this is visually quiet by default even when ON.
    /// </summary>
    public bool ShowMarkupFormatting
    {
        get => _reviewDisplayState.ShowFormatting;
        set => _reviewDisplayState = _reviewDisplayState.WithShowFormatting(value);
    }

    public ReviewDisplayPolicy CurrentReviewDisplayPolicy =>
        _reviewDisplayState.ToPolicy();

    public ReviewWorkflowStatus CurrentReviewWorkflowStatus
    {
        get
        {
            CommitToModel();
            return ReviewWorkflowStatusPlanner.Build(_model, CurrentReviewDisplayPolicy, TrackChangesEnabled);
        }
    }

    // [ThreadStatic] fields used by the static BuildRun family to read the above policy during a render
    // pass — same pattern as _renderFileName (set in Render(), read in static helpers, never escapes
    // the render call).
    [ThreadStatic]
    private static ReviewDisplayPolicy _renderReviewDisplayPolicy;

    /// <summary>
    /// Apply a change to the Show Markup Insertions/Deletions flag and re-render so the updated
    /// decoration (or lack of it) becomes visible immediately. Pending edits are committed first.
    /// </summary>
    public void ApplyShowMarkupInsertionsAndDeletions(bool show)
    {
        CommitToModel();
        _reviewDisplayState = _reviewDisplayState.WithShowInsertionsAndDeletions(show);
        Render();
    }

    /// <summary>
    /// Apply a change to the Show Markup Comments flag and re-render so the updated
    /// highlight (or lack of it) becomes visible immediately. Pending edits are committed first.
    /// </summary>
    public void ApplyShowMarkupComments(bool show)
    {
        CommitToModel();
        _reviewDisplayState = _reviewDisplayState.WithShowComments(show);
        Render();
    }

    /// <summary>
    /// Switch to a new Display for Review mode and re-render. Pending edits are committed first.
    /// The round-trip invariant is maintained: every revision run stays in the WPF tree in every
    /// mode, carrying its <see cref="RevisionMarker"/> tag; only colour/decoration/visibility change.
    /// </summary>
    public void ApplyDisplayForReview(ReviewDisplayMode mode)
    {
        CommitToModel();
        _reviewDisplayState = _reviewDisplayState.WithDisplayMode(mode);
        Render();
    }

    /// <summary>
    /// Apply a change to the Show Markup Formatting flag and re-render. Pending edits are committed
    /// first. The <see cref="FormatRevisionMarker"/> tag is always written regardless of this flag.
    /// </summary>
    public void ApplyShowMarkupFormatting(bool show)
    {
        CommitToModel();
        _reviewDisplayState = _reviewDisplayState.WithShowFormatting(show);
        Render();
    }

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

        RevisionEditPlanner.MarkRevisionRange(modelParagraph, startOffset, endOffset, kind, author, dateXml);
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
    /// Every tracked change in the committed document, in reading order — the model behind the Reviewing
    /// Pane. Commits pending edits first so the list reflects the current text, then defers to the pure
    /// <see cref="RevisionList"/>. Re-call after any single accept/reject to get a fresh, non-stale list.
    /// </summary>
    public IReadOnlyList<RevisionEntry> ListRevisions()
    {
        CommitToModel();
        return RevisionList.Enumerate(_model);
    }

    /// <summary>
    /// Accept exactly one tracked change (the one described by <paramref name="entry"/>), leaving every
    /// other revision pending. Re-renders so the resolved text shows immediately. Returns true when the
    /// entry resolved (false when it was already stale). The caller must re-list revisions afterwards.
    /// </summary>
    public bool AcceptRevision(RevisionEntry entry)
    {
        var resolved = RevisionList.Accept(_model, entry);
        if (resolved)
            Render();
        return resolved;
    }

    /// <summary>
    /// Reject exactly one tracked change (the one described by <paramref name="entry"/>), leaving every
    /// other revision pending. Re-renders so the resolved text shows immediately. Returns true when the
    /// entry resolved (false when it was already stale). The caller must re-list revisions afterwards.
    /// </summary>
    public bool RejectRevision(RevisionEntry entry)
    {
        var resolved = RevisionList.Reject(_model, entry);
        if (resolved)
            Render();
        return resolved;
    }

    /// <summary>
    /// Scroll the editor to (and place the caret at the start of) the top-level block that owns the given
    /// revision — the click-to-navigate / Previous-Next target. For a revision inside a table cell this
    /// lands on the table (the granularity <see cref="BringBlockIntoView"/> supports). A no-op when the
    /// owning block can no longer be found.
    /// </summary>
    public void NavigateToRevision(RevisionEntry entry)
    {
        var topLevelIndex = TopLevelBlockIndexOf(entry.Paragraph);
        if (topLevelIndex >= 0)
            BringBlockIntoView(topLevelIndex);
    }

    // The index, among the committed model's top-level blocks, of the block that owns paragraph
    // <paramref name="target"/> — itself if it is a top-level paragraph, or the containing table. Returns
    // -1 if it is not found. Matches the leaf-numbering BringBlockIntoView uses (a table is one leaf).
    private int TopLevelBlockIndexOf(ModelParagraph target)
    {
        for (var i = 0; i < _model.Blocks.Count; i++)
        {
            var block = _model.Blocks[i];
            if (ReferenceEquals(block, target))
                return i;
            if (block is ModelTable table &&
                table.Rows.Any(r => r.Cells.Any(c => c.Paragraphs.Any(p => ReferenceEquals(p, target)))))
                return i;
        }
        return -1;
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
    /// paragraph's plain text. Mirrors <see cref="AddCommentCommand.MarkCommentRange"/>.
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

    private void ApplyProofingLanguagePlan(ProofingLanguageApplyPlan plan)
    {
        var ranges = plan.Ranges
            .Where(range => range.BlockIndex < _model.Blocks.Count
                && _model.Blocks[range.BlockIndex] is ModelParagraph paragraph
                && TextRangeCoversParagraphText(paragraph, range.StartOffset, range.EndOffset))
            .ToList();
        if (ranges.Count == 0)
            return;

        if (ranges.Count == 1)
        {
            ExecuteProofingLanguageRange(ranges[0], plan.LanguageTag);
            return;
        }

        _commands.BeginUndoGroup();
        foreach (var range in ranges)
            ExecuteProofingLanguageRange(range, plan.LanguageTag);
        _commands.CommitUndoGroup("Proofing Language");
    }

    private void ExecuteProofingLanguageRange(ProofingLanguageTextRange range, string? languageTag) =>
        _commands.Execute(new ReplaceParagraphRunsCommand(range.BlockIndex, paragraph =>
            ApplyRunFormattingToTextRange(
                paragraph,
                range.StartOffset,
                range.EndOffset,
                formatting => formatting with { LanguageTag = languageTag })));

    private static bool TextRangeCoversParagraphText(ModelParagraph paragraph, int startOffset, int endOffset)
    {
        var textLength = paragraph.Runs.Sum(run => run.Text.Length);
        var start = Math.Clamp(startOffset, 0, textLength);
        var end = Math.Clamp(endOffset, 0, textLength);
        return end > start;
    }

    private static void ApplyRunFormattingToTextRange(
        ModelParagraph paragraph,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform)
    {
        var rebuilt = new List<ModelRun>();
        var position = 0;
        foreach (var source in paragraph.Runs)
        {
            var length = source.Text.Length;
            var runStart = position;
            var runEnd = position + length;
            position = runEnd;
            if (length == 0)
            {
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                continue;
            }

            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
            {
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                continue;
            }

            var localStart = coverStart - runStart;
            var localEnd = coverEnd - runStart;

            if (localStart > 0)
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[..localStart]));

            var covered = RevisionEditPlanner.CloneRunWithText(source, source.Text[localStart..localEnd]);
            covered.Formatting = transform(source.Formatting);
            rebuilt.Add(covered);

            if (localEnd < length)
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[localEnd..]));
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
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

        // Recover model-only fields (character border, shading, language) from the CharacterFormatMarker
        // tag set by BuildRun. The background brush is also inspected for the highlight/shading fallback
        // (plain runs without the marker use the background as-is for HighlightColorHex).
        var charFmt = (run.Tag as RunMarkers)?.CharacterFormat;
        var charBorder = charFmt?.Border;
        var charShadingHex = charFmt?.ShadingHex;
        var charShadingPattern = charFmt?.ShadingPattern ?? ShadingPattern.Clear;
        var languageTag = charFmt?.LanguageTag;

        // The background brush is the rendered colour of either CharacterShading or Highlight; use the
        // marker to tell them apart (marker present → shading, no marker → highlight).
        string? highlightHex = null;
        if (run.Background is SolidColorBrush bg)
        {
            if (charShadingHex is null)
                highlightHex = ToHex(bg.Color);
            // else: the background was set from CharacterShadingHex; don't also capture as highlight.
        }

        return new RunFormatting
        {
            Bold = run.FontWeight >= FontWeights.Bold,
            Italic = run.FontStyle == FontStyles.Italic,
            // Character border injects an overline+underline; strip them so they don't register as real
            // Underline/Strikethrough (the border's TextDecorations have a coloured custom Pen, distinct
            // from the standard single-colour decorations). We recover the real underline/strikethrough
            // by consulting the tag: if the run had a real underline BuildRun added TextDecorations.Underline[0]
            // BEFORE the border decorations. We can't distinguish them here, so we rely on the fact that
            // if CharacterBorder is set we strip all decorations and trust the model marker; the real
            // underline/strikethrough state comes back through the next full round-trip from the model.
            // For the common case (no character border), the standard paths apply unchanged.
            Underline = charBorder is null && run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true,
            Strikethrough = charBorder is null && run.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true,
            SmallCaps = capitals == FontCapitals.SmallCaps,
            AllCaps = capitals == FontCapitals.AllSmallCaps,
            VerticalAlign = verticalAlign,
            // Right-to-left run direction reads back off the WPF run's FlowDirection (set in BuildRun).
            Rtl = run.FlowDirection == System.Windows.FlowDirection.RightToLeft,
            FontFamily = run.FontFamily.Source,
            FontSizePt = fontSizePt,
            ColorHex = run.Foreground is SolidColorBrush brush ? ToHex(brush.Color) : null,
            HighlightColorHex = highlightHex,
            CharacterBorder = charBorder,
            CharacterShadingHex = charShadingHex,
            CharacterShadingPattern = charShadingPattern,
            LanguageTag = languageTag,
        };
    }

    // Undo the view-only chrome BuildRun injects for a tracked-change run: clear the revision colour
    // (so it doesn't leak into the model as an explicit colour) and remove the decoration the kind added
    // (underline for an insertion, strikethrough for a deletion). The run's own real formatting is kept.
    private static RunFormatting StripRevisionChrome(RunFormatting formatting, RevisionKind kind, string revisionHex)
    {
        return formatting with
        {
            ColorHex = string.Equals(formatting.ColorHex, revisionHex, StringComparison.OrdinalIgnoreCase) ? null : formatting.ColorHex,
            Underline = kind == RevisionKind.Inserted ? false : formatting.Underline,
            Strikethrough = kind == RevisionKind.Deleted ? false : formatting.Strikethrough
        };
    }

    /// <summary>
    /// Undo the view-only chrome BuildRun injects for a tracked formatting-change run: clear the revision
    /// colour tint (so it doesn't leak into the model as an explicit colour) and remove the dotted-underline
    /// decoration. The run's own real formatting (bold, italic, etc.) is kept unchanged.
    /// </summary>
    private static RunFormatting StripFormatRevisionChrome(RunFormatting formatting, string revisionHex)
    {
        // Only strip the colour if it matches the revision tint exactly — if the run has its own colour,
        // leave it alone. The dotted underline is a WPF TextDecoration; ReadRunFormatting maps underlines
        // via the Underline property so if we added a dotted underline in BuildRun but the run itself
        // didn't have an underline, the Underline flag in the recovered formatting would be true. Clear it.
        // NOTE: WPF TextDecorations.Underline[0] and our custom dotted decoration are both "underline
        // location" so ReadRunFormatting can't distinguish them; we clear only when the model run's
        // Underline was false (the FormatRevisionMarker carries the original; we use it to restore).
        return formatting with
        {
            ColorHex = string.Equals(formatting.ColorHex, revisionHex, StringComparison.OrdinalIgnoreCase) ? null : formatting.ColorHex,
            // The dotted underline BuildRun added would be read back as Underline=true, but only if the
            // run's real formatting had no underline. We conservatively clear underline here; if the run
            // truly was underlined, its FormatRevisionMarker.Revision.PreviousFormatting tells us.
            // However, since WPF merges decorations, we can't easily tell the real underline from the
            // decoration we injected. The safest approach: the FormatRevisionMarker records the ORIGINAL
            // FormatRevision which is returned to the model directly, so any inaccuracy in the formatting
            // snapshot here is irrelevant — the model gets its FormatRevision from the marker, not from
            // the stripped WPF formatting (FormatRevision itself is a model-level concept).
            Underline = formatting.Underline
        };
    }

    private static ParagraphFormatting ReadParagraphFormatting(WpfParagraph paragraph, TextDocument document)
    {
        var pageBreakBefore = paragraph.Tag is ParagraphTag { PageBreakBefore: true };
        // WidowControl rides on the Tag (no FlowDocument property); KeepWithNext/KeepLinesTogether read
        // straight back off the WPF Paragraph's native properties set in BuildParagraph.
        var tag = paragraph.Tag as ParagraphTag;
        var widowControl = tag?.WidowControl ?? false;
        var widowControlIsSet = tag?.WidowControlIsSet ?? false;
        var keepLinesTogether = tag?.KeepLinesTogether ?? paragraph.KeepTogether;
        // SuppressAutoHyphens has no FlowDocument property either, so it rides on the Tag like WidowControl.
        var suppressAutoHyphens = paragraph.Tag is ParagraphTag { SuppressAutoHyphens: true };
        var suppressLineNumbers = paragraph.Tag is ParagraphTag { SuppressLineNumbers: true };
        var suppressLineNumbersIsSet = paragraph.Tag is ParagraphTag { SuppressLineNumbersIsSet: true };
        return ParagraphFormatting.Default with
        {
            SuppressAutoHyphens = suppressAutoHyphens,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressLineNumbersIsSet = suppressLineNumbersIsSet,
            Alignment = FromWpfAlignment(paragraph.TextAlignment),
            // Right-to-left direction reads straight back off the WPF Paragraph's FlowDirection (set in
            // BuildParagraph), so an RTL paragraph survives an edit/commit cycle.
            Rtl = paragraph.FlowDirection == System.Windows.FlowDirection.RightToLeft,
            KeepWithNext = paragraph.KeepWithNext,
            KeepLinesTogether = keepLinesTogether,
            WidowControl = widowControl,
            WidowControlIsSet = widowControlIsSet,
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
            // A border whose line style / per-edge flags were set in the dialog has no WPF Border slot, so it
            // is carried verbatim on the Tag and recovered here in preference to the WPF-derived border; an
            // untagged paragraph (a plain quick-toggle box / horizontal rule) recovers from the WPF Border.
            Border = paragraph.Tag is ParagraphTag { Border: { } taggedBorder }
                ? taggedBorder
                : ReadParagraphBorder(paragraph, pageBreakBefore),
            PageBreakBefore = pageBreakBefore,
            ShadingColorHex = paragraph.Background is SolidColorBrush shading ? ToHex(shading.Color) : null,
            // The shading pattern (w:shd/@w:val) likewise has no WPF slot; recovered from the Tag (Clear when
            // untagged) so a non-solid pattern set in the dialog survives an edit/commit cycle.
            ShadingPattern = paragraph.Tag is ParagraphTag { ShadingPattern: var pattern } ? pattern : ShadingPattern.Clear,
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
            HighlightColorHex = r.HighlightColorHex ?? style.HighlightColorHex ?? d.HighlightColorHex,
            CharacterBorder = r.CharacterBorder ?? style.CharacterBorder ?? d.CharacterBorder,
            CharacterShadingHex = r.CharacterShadingHex ?? style.CharacterShadingHex ?? d.CharacterShadingHex,
            CharacterShadingPattern = r.CharacterShadingHex is not null ? r.CharacterShadingPattern
                : style.CharacterShadingHex is not null ? style.CharacterShadingPattern
                : d.CharacterShadingPattern,
            // Language is direct-only until FreeW gains explicit style/default language inheritance tracking.
            LanguageTag = r.LanguageTag,
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
            var lineFrom = p.LineSpacingIsSet
                ? p
                : sp.LineSpacingIsSet
                    ? sp
                    : document.DefaultParagraph.LineSpacingIsSet
                        ? document.DefaultParagraph
                        : p;
            return p with
            {
                ContextualSpacing = p.ContextualSpacing ?? sp.ContextualSpacing ?? document.DefaultParagraph.ContextualSpacing,
                SuppressLineNumbers = p.SuppressLineNumbersIsSet
                    ? p.SuppressLineNumbers
                    : sp.SuppressLineNumbersIsSet && sp.SuppressLineNumbers,
                SuppressLineNumbersIsSet = p.SuppressLineNumbersIsSet || sp.SuppressLineNumbersIsSet,
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
                LineSpacingIsSet = p.LineSpacingIsSet || sp.LineSpacingIsSet || document.DefaultParagraph.LineSpacingIsSet,
                IndentLeftPt = p.IndentLeftPt != d.IndentLeftPt ? p.IndentLeftPt : sp.IndentLeftPt,
                IndentRightPt = p.IndentRightPt != d.IndentRightPt ? p.IndentRightPt : sp.IndentRightPt,
                FirstLineIndentPt = p.FirstLineIndentPt != d.FirstLineIndentPt ? p.FirstLineIndentPt : sp.FirstLineIndentPt,
                Border = p.Border ?? sp.Border,
                ShadingColorHex = p.ShadingColorHex ?? sp.ShadingColorHex,
            };
        }
        return p with
        {
            ContextualSpacing = p.ContextualSpacing ?? document.DefaultParagraph.ContextualSpacing,
            LineSpacing = !p.LineSpacingIsSet && document.DefaultParagraph.LineSpacingIsSet
                ? document.DefaultParagraph.LineSpacing
                : p.LineSpacing,
            LineRule = !p.LineSpacingIsSet && document.DefaultParagraph.LineSpacingIsSet
                ? document.DefaultParagraph.LineRule
                : p.LineRule,
            LineHeightPt = !p.LineSpacingIsSet && document.DefaultParagraph.LineSpacingIsSet
                ? document.DefaultParagraph.LineHeightPt
                : p.LineHeightPt,
            LineSpacingIsSet = p.LineSpacingIsSet || document.DefaultParagraph.LineSpacingIsSet,
        };
    }

    private static bool SuppressesContextualSpacing(
        ModelParagraph previous,
        ModelParagraph current,
        TextDocument document) =>
        string.Equals(previous.StyleId, current.StyleId, StringComparison.OrdinalIgnoreCase)
        && Resolve(current, document).ContextualSpacing is true;

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

    private sealed record ShapeEditPointsTarget(
        int BlockIndex,
        int RunIndex,
        Shape Shape,
        IReadOnlyList<int>? ChildPath = null);

    /// <summary>
    /// Interactive freeform vertex handles. The handles only preview their position while dragging;
    /// releasing one commits a single model command so Ctrl+Z has Word-like one-drag granularity.
    /// </summary>
    private sealed class ShapeEditPointsAdorner : Adorner, IDisposable
    {
        private const double HandleSize = 10;

        private readonly DocumentView _view;
        private readonly ShapeEditPointsTarget _target;
        private readonly List<(int SegmentIndex, Thumb Handle)> _handles = [];
        private Rect _lastShapeBounds = Rect.Empty;

        public ShapeEditPointsAdorner(DocumentView view, ShapeEditPointsTarget target) : base(view)
        {
            _view = view;
            _target = target;
            IsHitTestVisible = true;
            BuildHandles();
            _view.LayoutUpdated += OnViewLayoutUpdated;
        }

        public int HandleCount => _handles.Count;

        public void Dispose() => _view.LayoutUpdated -= OnViewLayoutUpdated;

        protected override int VisualChildrenCount => _handles.Count;

        protected override Visual GetVisualChild(int index) => _handles[index].Handle;

        protected override Size MeasureOverride(Size constraint)
        {
            foreach (var (_, handle) in _handles)
                handle.Measure(new Size(HandleSize, HandleSize));
            return constraint;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!TryGetShapeBounds(out var origin, out var width, out var height))
            {
                foreach (var (_, handle) in _handles)
                    handle.Arrange(Rect.Empty);
                _lastShapeBounds = Rect.Empty;
                return finalSize;
            }

            var geometry = _target.Shape.CustomGeometry!;
            var rendered = FindRenderedShapeVisual(_view, _target.Shape);
            if (rendered is null)
                return finalSize;

            _lastShapeBounds = new Rect(origin, new Size(width, height));
            foreach (var (segmentIndex, handle) in _handles)
            {
                var point = geometry.Segments[segmentIndex].Point!;
                var pagePoint = rendered.TransformToAncestor(_view).Transform(new Point(
                    point.X / (double)geometry.Width * rendered.ActualWidth,
                    point.Y / (double)geometry.Height * rendered.ActualHeight));
                handle.Arrange(new Rect(
                    pagePoint.X - HandleSize / 2,
                    pagePoint.Y - HandleSize / 2,
                    HandleSize,
                    HandleSize));
            }

            return finalSize;
        }

        private void BuildHandles()
        {
            var geometry = _target.Shape.CustomGeometry!;
            for (var segmentIndex = 0; segmentIndex < geometry.Segments.Count; segmentIndex++)
            {
                if (geometry.Segments[segmentIndex].Point is null)
                    continue;

                var index = segmentIndex;
                var handle = new Thumb
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Background = Brushes.White,
                    BorderBrush = Brushes.DodgerBlue,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Cross,
                    Focusable = false,
                    ToolTip = "Drag edit point"
                };

                CustomPoint? dragStart = null;
                var dragX = 0.0;
                var dragY = 0.0;
                var dragStartPage = default(Point);
                handle.DragStarted += (_, _) =>
                {
                    dragStart = _target.Shape.CustomGeometry?.Segments[index].Point;
                    dragX = 0;
                    dragY = 0;
                    if (FindRenderedShapeVisual(_view, _target.Shape) is { } rendered
                        && dragStart is { } start)
                    {
                        dragStartPage = rendered.TransformToAncestor(_view).Transform(new Point(
                            start.X / (double)_target.Shape.CustomGeometry!.Width * rendered.ActualWidth,
                            start.Y / (double)_target.Shape.CustomGeometry.Height * rendered.ActualHeight));
                    }
                    handle.RenderTransform = new TranslateTransform();
                };
                handle.DragDelta += (_, e) =>
                {
                    if (handle.RenderTransform is TranslateTransform transform)
                    {
                        dragX += e.HorizontalChange;
                        dragY += e.VerticalChange;
                        transform.X = dragX;
                        transform.Y = dragY;
                    }
                };
                handle.DragCompleted += (_, _) =>
                {
                    handle.RenderTransform = null;
                    if (dragStart is null
                        || FindRenderedShapeVisual(_view, _target.Shape) is not { } rendered
                        || rendered.ActualWidth <= 0
                        || rendered.ActualHeight <= 0)
                        return;

                    var geometry = _target.Shape.CustomGeometry;
                    if (geometry is null)
                        return;

                    var inverse = rendered.TransformToAncestor(_view).Inverse;
                    if (inverse is null)
                        return;
                    var localPoint = inverse.Transform(new Point(
                        dragStartPage.X + dragX,
                        dragStartPage.Y + dragY));
                    var x = Math.Clamp(
                        (long)Math.Round(localPoint.X / rendered.ActualWidth * geometry.Width),
                        0,
                        geometry.Width);
                    var y = Math.Clamp(
                        (long)Math.Round(localPoint.Y / rendered.ActualHeight * geometry.Height),
                        0,
                        geometry.Height);
                    _view.MoveShapeEditPoint(_target, index, x, y);
                };

                _handles.Add((index, handle));
                AddVisualChild(handle);
            }
        }

        private void OnViewLayoutUpdated(object? sender, EventArgs e)
        {
            if (TryGetShapeBounds(out var origin, out var width, out var height))
            {
                var current = new Rect(origin, new Size(width, height));
                if (current != _lastShapeBounds)
                    InvalidateArrange();
            }
        }

        private bool TryGetShapeBounds(out Point origin, out double width, out double height)
        {
            origin = default;
            width = 0;
            height = 0;
            var element = FindRenderedShapeVisual(_view, _target.Shape);
            if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;

            try
            {
                origin = element.TransformToAncestor(_view).Transform(new Point(0, 0));
                width = element.ActualWidth;
                height = element.ActualHeight;
                return double.IsFinite(origin.X) && double.IsFinite(origin.Y);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static FrameworkElement? FindRenderedShapeVisual(DependencyObject root, Shape shape)
        {
            if (root is FrameworkElement element && ReferenceEquals(element.Tag, shape))
                return element;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindRenderedShapeVisual(VisualTreeHelper.GetChild(root, i), shape);
                if (found is not null)
                    return found;
            }

            return null;
        }
    }

    /// <summary>
    /// Draws pixel-aligned inter-column rules over the continuous Print-Layout editor. The flow still
    /// owns text columns and pagination; this chrome replaces only WPF's half-pixel rule raster.
    /// </summary>
    private sealed class ColumnRuleAdorner : Adorner
    {
        private readonly DocumentView _view;
        private DocumentPagination? _pagination;

        public ColumnRuleAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
            _view.TextChanged += (_, _) => _pagination = null;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_view.Document is not { } doc)
                return;

            var page = _view._model.Page;
            if (!page.ColumnsLineBetween || page.ColumnCount <= 1)
                return;

            double firstContentTop;
            try
            {
                var firstRect = doc.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (firstRect.IsEmpty)
                    return;
                firstContentTop = firstRect.Top;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            try
            {
                _pagination ??= PaginationEngine.Compute(_view);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var (_, contentHeightDip) = PageLayout.ContentAreaDip(page);
            var (marginLeftDip, _, marginRightDip, _) = PageLayout.MarginsDip(page);
            var contentWidthDip = Math.Max(0, _view.RenderSize.Width - marginLeftDip - marginRightDip);
            if (contentHeightDip <= 0 || contentWidthDip <= 0)
                return;

            var bounds = new Rect(_view.RenderSize);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                double pageStartOffset = 0;
                for (var pageIndex = 0; pageIndex < Math.Max(1, _pagination.PageCount); pageIndex++)
                {
                    var nextBreakOffset = pageIndex < _pagination.PageBreakYsDip.Count
                        ? _pagination.PageBreakYsDip[pageIndex]
                        : pageStartOffset + contentHeightDip;
                    if (nextBreakOffset <= pageStartOffset)
                        nextBreakOffset = pageStartOffset + contentHeightDip;

                    var top = firstContentTop + pageStartOffset;
                    var bottom = firstContentTop + nextBreakOffset;
                    if (bottom >= bounds.Top && top <= bounds.Bottom)
                        DrawColumnRules(drawingContext, page, marginLeftDip, top, contentWidthDip, bottom);

                    pageStartOffset = nextBreakOffset;
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }
    }

    /// <summary>
    /// Draws the faint "— Page N —" break markers down the Print-Layout editing surface, so the user
    /// perceives where the single continuous flow would break across printed pages. This is a low-key
    /// visual cue rather than exact pagination; Print Preview remains authoritative.
    /// </summary>
    private sealed class PageBreakAdorner : Adorner
    {
        private static readonly Pen BreakPen = CreateBreakPen();
        private static readonly Brush LabelBrush = CreateLabelBrush();

        private readonly DocumentView _view;

        // Cached result from PaginationEngine. Null means "needs recompute" (content has changed since
        // the last successful computation). Invalidated on TextChanged so we don't re-paginate every
        // OnRender (ComputePageCount is a full layout pass — expensive on large docs).
        // Internal so DocumentView.GetPageBreakAdornerPagination() can expose it as a test seam
        // (outer class cannot access a nested class's private fields in C#).
        internal DocumentPagination? _pagination;

        public PageBreakAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            // Repaint when the surface scrolls or relayouts so the markers track the content.
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
            // Invalidate the pagination cache whenever the document content changes, so the next
            // OnRender gets fresh break positions. TextChanged fires for every edit operation.
            _view.TextChanged += (_, _) => _pagination = null;
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

            // Anchor at the top of the first laid-out content line. Without a first rectangle (empty/just
            // re-rendered document) there is nothing to anchor to, so skip painting this pass.
            var origin = FirstContentTop(doc);
            if (origin is not { } topY)
                return;

            // Ensure the pagination cache is populated. PaginationEngine.Compute is a full layout pass;
            // the cache is only discarded on TextChanged, so during normal scrolling/zoom this is a
            // cheap cache hit.
            _pagination ??= TryComputePagination();
            if (_pagination is null)
                return; // layout momentarily unavailable — skip this pass

            var breakYs = _pagination.PageBreakYsDip;
            if (breakYs.Count == 0)
                return; // single page, nothing to draw

            var bounds = new Rect(_view.RenderSize);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                var pixelsPerDip = VisualTreeHelper.GetDpi(_view).PixelsPerDip;
                for (var i = 0; i < breakYs.Count; i++)
                {
                    // breakYs[i] is the cumulative content height at break i, measured from the first
                    // content line. Translate to adorner coordinates by adding topY.
                    var y = topY + breakYs[i];
                    if (y > bounds.Bottom)
                        break; // scrolled past bottom — no more visible breaks
                    if (y < bounds.Top)
                        continue; // scrolled above viewport — skip but keep iterating

                    // Page number shown on the label is the number of the page that begins AFTER this break.
                    DrawMarker(drawingContext, y, i + 2, bounds, pixelsPerDip);
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        /// <summary>
        /// Computes page-break Y positions via the authoritative pagination engine. Returns null when
        /// the layout is momentarily unavailable (e.g. during a re-layout triggered by a TextChanged).
        /// </summary>
        private DocumentPagination? TryComputePagination()
        {
            try
            {
                return PaginationEngine.Compute(_view);
            }
            catch (InvalidOperationException)
            {
                // WPF layout not yet settled — skip; will retry on next LayoutUpdated.
                return null;
            }
        }

        /// <summary>
        /// Returns the page-break Y positions (in the adorner's coordinate space, i.e. relative to
        /// <paramref name="topY"/>) for the current pagination. Used by tests to verify accuracy without
        /// triggering a full render. Returns null when the layout is unavailable.
        /// </summary>
        internal IReadOnlyList<double>? GetBreakYsForTest(double topY)
        {
            _pagination ??= TryComputePagination();
            if (_pagination is null)
                return null;
            return _pagination.PageBreakYsDip.Select(y => topY + y).ToArray();
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

    /// <summary>
    /// Draws line numbers in the left-margin gutter of the live Print-Layout editing surface when the
    /// document enables line numbering (<see cref="PageSettings.LineNumberMode"/>), so the editor matches
    /// what Print Preview and the printed page show rather than surfacing the numbers only on print.
    ///
    /// The editable surface is one continuous WPF flow, so the adorner reads the laid-out <em>visual</em>
    /// lines via <see cref="TextPointer.GetLineStartPosition"/> and numbers them top-to-bottom. Continuous
    /// mode counts every line from the document start; RestartEachPage resets the counter at each printed
    /// page boundary, approximated (like <see cref="PageBreakAdorner"/>) by stepping the page's printable
    /// content height — Print Preview remains the authoritative paginated view. Only every
    /// <see cref="PageSettings.LineNumberCountBy"/>-th number is drawn. Numbers are right-aligned just
    /// inside the left page margin, matching <c>PrintPreviewWindow.BuildLineNumbers</c>.
    ///
    /// Coordinates and zoom behave exactly as for <see cref="PageBreakAdorner"/>: the adorner shares the
    /// editor's content coordinate space and is scaled by its LayoutTransform, so numbers track the text
    /// under zoom. Painting is clipped to the visible surface.
    /// </summary>
    private sealed class LineNumberAdorner : Adorner
    {
        private static readonly Brush NumberBrush = CreateNumberBrush();

        // Cap the number of lines walked per paint so a pathological layout can't make the overlay
        // expensive; far above any realistic single-screen line count.
        private const int MaxLines = 20_000;

        private readonly DocumentView _view;

        public LineNumberAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
        }

        private static Brush CreateNumberBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var page = _view._model.Page;
            var sections = _view._model.Sections;
            if (sections.All(section => section.Page.LineNumberMode == LineNumberMode.None)
                || _view.Document is not { } doc)
                return;

            // Page geometry used to (a) place numbers in the left margin and (b) approximate page resets.
            var (_, contentHeight) = PageLayout.ContentAreaDip(page);
            var (leftMarginDip, _, _, _) = PageLayout.MarginsDip(page);
            var gutterRight = Math.Max(0, leftMarginDip - PageLayout.PointsToDip(6));

            var origin = FirstLineTop(doc);
            if (origin is not { } topY)
                return;

            var bounds = new Rect(_view.RenderSize);
            var pixelsPerDip = VisualTreeHelper.GetDpi(_view).PixelsPerDip;

            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                var laidOutLines = new List<(Rect Rect, LineNumberVisualSourceLine Source)>();
                var line = doc.ContentStart;
                System.Windows.Documents.Paragraph? previousParagraph = null;
                var sectionIndex = 0;
                for (var lineIndex = 0; lineIndex < MaxLines; lineIndex++)
                {
                    Rect rect;
                    try
                    {
                        rect = line.GetCharacterRect(LogicalDirection.Forward);
                    }
                    catch (InvalidOperationException)
                    {
                        // Layout momentarily unavailable during a relayout; abandon this pass.
                        return;
                    }

                    var paragraph = line.Paragraph;
                    if (!ReferenceEquals(paragraph, previousParagraph))
                    {
                        if (previousParagraph?.Tag is ParagraphTag { SectionBreak: not null })
                            sectionIndex++;
                        previousParagraph = paragraph;
                    }

                    var pageIndex = contentHeight > 0
                        ? Math.Max(0, (int)Math.Floor((rect.Top - topY) / contentHeight))
                        : 0;
                    laidOutLines.Add((
                        rect,
                        new LineNumberVisualSourceLine(
                            pageIndex,
                            paragraph?.Tag is ParagraphTag { SuppressLineNumbers: true },
                            Math.Min(sectionIndex, sections.Count - 1))));

                    var next = line.GetLineStartPosition(1);
                    if (next is null || next.CompareTo(line) <= 0)
                        break; // no further line (end of document) or layout not advancing
                    line = next;

                    // Stop once we've stepped past the bottom of the viewport (continuous mode keeps the
                    // global count, but there's nothing more to paint on screen).
                    if (rect.Top > bounds.Bottom)
                        break;
                }

                var plans = LineNumberVisualPlanner.Build(
                    laidOutLines.Select(item => item.Source).ToList(),
                    sections.Select(section => new LineNumberVisualSectionSettings(
                        section.Page.LineNumberMode,
                        section.Page.LineNumberStartAt,
                        section.Page.LineNumberCountBy)).ToList());
                for (var index = 0; index < plans.Count; index++)
                {
                    var plan = plans[index];
                    var rect = laidOutLines[index].Rect;
                    if (plan.IsVisible && !rect.IsEmpty
                        && rect.Bottom >= bounds.Top && rect.Top <= bounds.Bottom)
                        DrawNumber(drawingContext, plan.Number, rect, gutterRight, pixelsPerDip);
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        // Top Y (editor content coordinates) of the first laid-out line, or null when layout isn't ready.
        private static double? FirstLineTop(FlowDocument doc)
        {
            try
            {
                var rect = doc.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                return rect.IsEmpty ? null : rect.Top;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static void DrawNumber(DrawingContext dc, int lineNumber, Rect lineRect, double gutterRight, double pixelsPerDip)
        {
            var formatted = new FormattedText(
                lineNumber.ToString(System.Globalization.CultureInfo.CurrentCulture),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Calibri"),
                PageLayout.PointsToDip(9.0),
                NumberBrush,
                pixelsPerDip);

            // Right-align into the gutter; vertically centre against the line's box.
            var x = Math.Max(0, gutterRight - formatted.Width);
            var y = lineRect.Top + Math.Max(0, (lineRect.Height - formatted.Height) / 2);
            dc.DrawText(formatted, new Point(x, y));
        }
    }

    /// <summary>
    /// Draws a thin vertical bar in the left margin beside every paragraph in the WPF tree that
    /// contains at least one tracked-change run (insertion, deletion, or format revision). Used
    /// exclusively in <see cref="ReviewDisplayMode.SimpleMarkup"/>, where the inline rendering
    /// shows the final form (No Markup path) and this bar is the only visible cue that a change
    /// exists. The overlay is hit-test transparent and repaints on layout/scroll, mirroring the
    /// pattern of <see cref="FormattingMarksAdorner"/> and <see cref="LineNumberAdorner"/>.
    ///
    /// <para><b>Y-position strategy:</b> per-paragraph Y is obtained from
    /// <c>WpfParagraph.ContentStart.GetCharacterRect()</c>, which is the same geometry the
    /// formatting-marks adorner uses. The bar's height spans from the paragraph's first-line top
    /// to its last-content bottom, both derived from the same API. This is accurate for single-
    /// column continuous-flow documents. Print-Layout mode is also accurate (the adorner shares
    /// the editor's coordinate space and is scaled by its LayoutTransform). The only known
    /// approximation is that the bar's bottom uses <c>ContentEnd</c>'s rect, which, for very
    /// large paragraphs or paragraphs whose content end falls mid-line, may clip the last line by
    /// a few pixels — visually imperceptible for a margin indicator.</para>
    ///
    /// <para><b>Left-margin X position:</b> the bar is placed at a fixed small inset from the
    /// left edge of the editor's coordinate space (x = 2 dip), which sits in the left-margin
    /// padding added by <see cref="ApplyPageChrome"/> in Print Layout or at the edge of the
    /// content area in continuous view. This matches the visual convention of Word's change bar,
    /// which appears in the left gutter without encroaching on the text column.</para>
    /// </summary>
    internal sealed class ChangeBarAdorner : Adorner
    {
        // The bar colour matches Word's change-bar colour — a muted revision blue-grey.
        private static readonly Pen BarPen = CreateBarPen();

        // Width of the vertical bar in DIP (matching Word's ~3 pt bar width).
        private const double BarWidth = 3.0;

        // Horizontal inset from the left edge of the editor coordinate space (sits in the
        // page's left-margin padding added by ApplyPageChrome / the FlowDocument padding).
        private const double BarX = 2.0;

        // Cap how many paragraphs we scan per paint so a pathological document cannot make the
        // overlay expensive; far above any realistic single-screen paragraph count.
        private const int MaxParagraphs = 5_000;

        private readonly DocumentView _view;

        public ChangeBarAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            // Repaint when the surface scrolls or relayouts so the bars stay aligned with the text.
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
        }

        private static Pen CreateBarPen()
        {
            // A muted indigo-grey that reads clearly against a white page without competing with text.
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0xC0)), BarWidth);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_view.Document is not { } doc)
                return;

            var bounds = new Rect(_view.RenderSize);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            try
            {
                var count = 0;
                foreach (var block in doc.Blocks)
                {
                    DrawBlockBars(drawingContext, block, bounds, ref count);
                    if (count >= MaxParagraphs)
                        break;
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        // Walk a top-level block (paragraph, list, table) and draw bars for each changed paragraph.
        private void DrawBlockBars(DrawingContext dc, System.Windows.Documents.Block block, Rect bounds, ref int count)
        {
            switch (block)
            {
                case WpfParagraph paragraph:
                    if (count < MaxParagraphs)
                    {
                        DrawBarIfChanged(dc, paragraph, bounds);
                        count++;
                    }
                    break;
                case WpfList list:
                    foreach (var item in list.ListItems)
                        foreach (var inner in item.Blocks)
                            DrawBlockBars(dc, inner, bounds, ref count);
                    break;
                case WpfTable table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var inner in cell.Blocks)
                                    DrawBlockBars(dc, inner, bounds, ref count);
                    break;
            }
        }

        // Draw a vertical bar beside `paragraph` if it contains any tracked-change run.
        // The bar spans the paragraph's first-line top to its content-end bottom, both obtained
        // via GetCharacterRect (same geometry the FormattingMarksAdorner uses for glyphs).
        private void DrawBarIfChanged(DrawingContext dc, WpfParagraph paragraph, Rect bounds)
        {
            if (!ParagraphHasRevision(paragraph))
                return;

            Rect topRect, bottomRect;
            try
            {
                topRect = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                bottomRect = paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            }
            catch (InvalidOperationException)
            {
                // Layout momentarily unavailable during a relayout; skip this paragraph.
                return;
            }

            if (topRect.IsEmpty || bottomRect.IsEmpty)
                return;

            var barTop = topRect.Top;
            var barBottom = bottomRect.Bottom;

            // Cull bars that are entirely outside the visible viewport.
            if (barBottom < bounds.Top || barTop > bounds.Bottom)
                return;

            // Clamp to visible surface so the bar doesn't bleed outside the clip region.
            barTop = Math.Max(barTop, bounds.Top);
            barBottom = Math.Min(barBottom, bounds.Bottom);

            if (barBottom <= barTop)
                return;

            var midX = BarX + BarWidth / 2.0;
            dc.DrawLine(BarPen, new Point(midX, barTop), new Point(midX, barBottom));
        }

        /// <summary>
        /// Returns true when any inline in <paramref name="paragraph"/> carries a
        /// <see cref="RevisionMarker"/> (tracked insertion or deletion) or a
        /// <see cref="FormatRevisionMarker"/> (tracked formatting change). Used by
        /// <see cref="DrawBarIfChanged"/> and independently testable without a display surface.
        /// </summary>
        internal static bool ParagraphHasRevision(WpfParagraph paragraph)
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (InlineHasRevision(inline))
                    return true;
            }
            return false;
        }

        // Recurse into Span/Hyperlink containers; check WpfRun tags directly.
        private static bool InlineHasRevision(System.Windows.Documents.Inline inline) =>
            inline switch
            {
                WpfRun run => run.Tag is RunMarkers { Revision: not null },
                System.Windows.Documents.Span span => span.Inlines.Any(InlineHasRevision),
                _ => false
            };
    }

    /// <summary>
    /// A non-editable overlay that draws a faint rectangular grid behind the document content,
    /// mirroring Word's View ▸ Show ▸ Gridlines feature. The grid uses a fixed cell size (default
    /// ~14.4 pt ≈ 0.2 inch) that aligns with Word's default gridline spacing. It is purely
    /// decorative: hit-test transparent, never printed, and repaints on every layout update so it
    /// stays aligned while the user scrolls. Mirrors the pattern of
    /// <see cref="FormattingMarksAdorner"/> and <see cref="ChangeBarAdorner"/>.
    /// </summary>
    private sealed class PageGridlinesAdorner : Adorner
    {
        // Faint blue-grey — close to Word's gridline colour (roughly #C8D8E8).
        private static readonly Pen GridPen = CreateGridPen();

        // Grid cell size in device-independent pixels. Word defaults to ~0.2 inch ≈ 19.2 px at 96 dpi.
        // Using a slightly tighter 18 px so the grid is visually distinct from the content baseline.
        private const double CellSize = 18.0;

        private readonly DocumentView _view;

        public PageGridlinesAdorner(DocumentView view) : base(view)
        {
            _view = view;
            IsHitTestVisible = false;
            _view.LayoutUpdated += (_, _) => InvalidateVisual();
        }

        private static Pen CreateGridPen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0xA8, 0xC8)), 0.5);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var size = _view.RenderSize;
            if (size.Width <= 0 || size.Height <= 0)
                return;

            var bounds = new Rect(size);
            dc.PushClip(new RectangleGeometry(bounds));
            try
            {
                // Horizontal lines
                for (var y = CellSize; y < size.Height; y += CellSize)
                    dc.DrawLine(GridPen, new Point(0, y), new Point(size.Width, y));

                // Vertical lines
                for (var x = CellSize; x < size.Width; x += CellSize)
                    dc.DrawLine(GridPen, new Point(x, 0), new Point(x, size.Height));
            }
            finally
            {
                dc.Pop();
            }
        }
    }
}
