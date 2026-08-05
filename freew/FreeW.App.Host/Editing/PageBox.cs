using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>Delegate used by <see cref="PageBox"/> to notify the panel that a cross-page
/// Shift+arrow selection boundary was crossed.</summary>
internal delegate void CrossPageShiftArrowHandler(PageBox source, bool movingForward);


/// <summary>
/// One physical page slot in the <see cref="PaginatedEditorPanel"/>.  Hosts an editable header
/// region at the top (Phase 4), a body <see cref="RichTextBox"/> that the user edits, an optional
/// read-only footnote region (separator rule + numbered footnote texts) above the footer, and an
/// editable footer region at the bottom (Phase 4).  The body is fixed to the page content area so
/// each box represents exactly one page.
///
/// <para>
/// <strong>Phase 4 — In-page WYSIWYG header/footer:</strong>  The header and footer strips are
/// replaced by compact <see cref="DocumentView"/> sub-editors loaded via the same wrapper-document
/// pattern used by the Wave 11 docked pane (<see cref="MainWindow.OpenHeaderFooterPane"/>).  Each
/// sub-editor is seeded with the appropriate <see cref="HeaderFooter"/> slot for this page (default,
/// even, or first-page) and dimmed until the user clicks it (Word-style activation).  On
/// <see cref="CommitHfSlots"/> the sub-editors' blocks are read back into the model slots they own.
/// </para>
///
/// <para>
/// The body RichTextBox wraps a freshly created <see cref="FlowDocument"/> whose blocks have been
/// moved directly from the source FlowDocument produced by <see cref="PaginatedEditorPanel.Build"/>.
/// Moving — rather than serialising and re-parsing — is the critical Tag-preservation strategy: every
/// WPF <see cref="System.Windows.Documents.Block"/> element carries its original <c>Tag</c> payload
/// (<c>ParagraphTag</c>, <c>RunMarkers</c>, <c>FootnoteMarker</c>, etc.) intact, so
/// <see cref="PaginatedCommitCoordinator"/> can read them back with the same logic that
/// <see cref="DocumentView.CommitToModel"/> uses for the continuous editor.
/// </para>
///
/// <para>
/// <strong>Cross-page caret routing (Phase 3b-1):</strong> <c>PreviewKeyDown</c> intercepts
/// Down/Right at the last caret position in this box and routes focus to the next box's start;
/// Up/Left at the first position routes to the previous box's end.  Home/End/PageUp/PageDown fall
/// through to the native RichTextBox behaviour.
/// </para>
/// </summary>
internal sealed class PageBox : Border
{
    // ── geometry constants ────────────────────────────────────────────────────────────────────────
    private const double PageGapDip = 20;        // vertical gap rendered above each page box
    private const double HeaderHeightDip = 36;   // in-page header region height (Phase 4; was 24)
    private const double FooterHeightDip = 36;   // in-page footer region height (Phase 4; was 24)
    private const double FootnoteSeparatorHeight = 1.0; // thin horizontal rule
    private const double FootnoteTextSizePt = 9.0;      // slightly smaller than body (Word default)

    // ── public surface ────────────────────────────────────────────────────────────────────────────
    /// <summary>The editable body RichTextBox for this page.</summary>
    internal RichTextBox Body { get; }

    /// <summary>1-based page number (informational; shown in header strip label).</summary>
    internal int PageNumber { get; }

    /// <summary>Formatted PAGE field display text for this page, including section start/style.</summary>
    internal string PageNumberText { get; }

    /// <summary>
    /// The ordered footnote IDs whose text is rendered in this page's footnote region.
    /// Empty when the page has no footnotes.  Set by <see cref="PaginatedEditorPanel.Build"/>.
    /// </summary>
    internal IReadOnlyList<int> FootnoteIds { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// The ordered endnote IDs rendered in this page's endnote region (used when this box is the
    /// synthetic endnotes page appended at the end of the document).
    /// </summary>
    internal IReadOnlyList<int> EndnoteIds { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// True when this page box is the synthetic endnotes page appended after all body pages.
    /// It has no body blocks but retains the final section's header/footer regions. Used by the
    /// FidelityRender tool to identify the synthetic page and render it separately from the body
    /// FlowDocument paginator.
    /// </summary>
    internal bool IsEndnoteSyntheticPage { get; private set; }

    /// <summary>
    /// True when this is a print-only blank page inserted to satisfy an EvenPage or OddPage section
    /// start. It owns no model blocks, notes, or editable header/footer slots.
    /// </summary>
    internal bool IsParitySyntheticPage { get; private set; }

    // ── Phase 4: in-page editable header/footer sub-editors ───────────────────────────────────────

    /// <summary>
    /// The in-page header sub-editor (a <see cref="DocumentView"/> loaded with the wrapper document
    /// for this page's header slot).  Null when the slot was null and no content exists.
    /// Caller (<see cref="PaginatedCommitCoordinator"/>) commits this back to the model slot via
    /// <see cref="CommitHfSlots"/>.
    /// </summary>
    internal DocumentView? HeaderSubEditor { get; }

    /// <summary>
    /// The in-page footer sub-editor.  Null when the slot was null and no content exists.
    /// </summary>
    internal DocumentView? FooterSubEditor { get; }

    /// <summary>
    /// The model slot name that <see cref="HeaderSubEditor"/> belongs to (e.g. "header",
    /// "first-header", "even-header"), so the commit coordinator can write back to the right slot.
    /// </summary>
    internal string? HeaderSlotName { get; }

    /// <summary>
    /// The model slot name that <see cref="FooterSubEditor"/> belongs to.
    /// </summary>
    internal string? FooterSlotName { get; }

    /// <summary>
    /// The <see cref="SectionHeadersFooters"/> that this page box's header and footer sub-editors
    /// should commit back to.  Set by <see cref="PaginatedEditorPanel.Build"/> from
    /// <c>ComputePageSectionMap</c> so the commit coordinator writes to the correct section's slot
    /// rather than always writing to the document-level
    /// <see cref="TextDocument.FinalSectionHeadersFooters"/>.
    /// </summary>
    internal SectionHeadersFooters? OwnerSectionHf { get; set; }

    /// <summary>
    /// The <see cref="PageSettings"/> that governs this page's geometry (width, height, orientation,
    /// margins).  Set from the section that owns this page so portrait and landscape sections can
    /// render at different sizes inside the same panel.
    /// </summary>
    internal PageSettings PageGeometry { get; private set; } = null!;

    // ── neighbour references (set by PaginatedEditorPanel after all boxes are created) ────────────
    internal PageBox? PreviousBox { get; set; }
    internal PageBox? NextBox { get; set; }

    // ── cross-page selection (Phase 3b-2) ─────────────────────────────────────────────────────────
    /// <summary>
    /// Fired when a Shift+Down/Right at the document end (or Shift+Up/Left at the document start)
    /// means the selection should extend into an adjacent box.  The panel intercepts this to update
    /// the <see cref="CrossPageSelection"/> model.
    /// </summary>
    internal event CrossPageShiftArrowHandler? ShiftArrowBoundaryReached;

    // ── construction ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a page box for <paramref name="pageNumber"/> using geometry from <paramref name="page"/>.
    /// <paramref name="pageBlocks"/> are the WPF Block elements (already detached from their previous
    /// parent) that belong on this page; they are added directly to the body FlowDocument so Tags are
    /// preserved.
    ///
    /// <para>
    /// <strong>Phase 4:</strong> <paramref name="headerSlot"/>, <paramref name="footerSlot"/>,
    /// <paramref name="headerSlotName"/>, <paramref name="footerSlotName"/>, and
    /// <paramref name="sourceModel"/> drive the in-page editable sub-editors via the wrapper-document
    /// pattern.  Pass null slots to suppress the sub-editor for that region (the old placeholder strip
    /// is shown instead).
    /// </para>
    ///
    /// <para>
    /// <strong>Live page numbers (W18):</strong> <paramref name="pageCount"/> is passed to the sub-editor
    /// so PAGE field runs in the header/footer render as the actual 1-based page number for this box, and
    /// NUMPAGES renders the real total.  The underlying model field run is unchanged (round-trip lossless).
    /// </para>
    ///
    /// <para>
    /// <strong>Footnotes:</strong> <paramref name="footnoteIds"/> lists the IDs of footnotes that
    /// appeared on this page (determined by scanning the body blocks for <c>FootnoteMarker</c> tags).
    /// When non-empty a footnote region is inserted between the body and the footer: a short horizontal
    /// separator followed by the footnote paragraphs in smaller text.
    /// </para>
    /// </summary>
    internal PageBox(
        int pageNumber,
        PageSettings page,
        IReadOnlyList<System.Windows.Documents.Block> pageBlocks,
        TextDocument? sourceModel = null,
        HeaderFooter? headerSlot = null,
        string? headerSlotName = null,
        HeaderFooter? footerSlot = null,
        string? footerSlotName = null,
        int pageCount = 1,
        string? pageNumberText = null,
        IReadOnlyList<int>? footnoteIds = null,
        IReadOnlyList<int>? endnoteIds = null,
        bool isEndnoteSyntheticPage = false,
        bool isParitySyntheticPage = false)
    {
        PageNumber = pageNumber;
        PageNumberText = pageNumberText
            ?? pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PageGeometry = page;
        HeaderSlotName = headerSlotName;
        FooterSlotName = footerSlotName;
        if (footnoteIds is { Count: > 0 }) FootnoteIds = footnoteIds;
        if (endnoteIds is { Count: > 0 })
            EndnoteIds = endnoteIds;
        IsEndnoteSyntheticPage = isEndnoteSyntheticPage;
        IsParitySyntheticPage = isParitySyntheticPage;

        var (pageWidth, _) = PageLayout.PageSizeDip(page);
        var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
        var (contentWidth, contentHeight) = PageLayout.ContentAreaDip(page);

        // ── page-chrome border (the white page "sheet") ───────────────────────────────────────────
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        BorderThickness = new Thickness(1);
        Margin = new Thickness(0, PageGapDip, 0, 0);
        Width = pageWidth;

        // ── outer grid: row 0 = header | row 1 = body | row 2 = footnote region | row 3 = footer ──
        // The footnote row is always present but has zero height when there are no footnotes, so
        // the layout is identical to the pre-footnote behaviour for pages without footnotes.
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeightDip) });    // row 0: header
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // row 1: body
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // row 2: footnotes
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeightDip) });    // row 3: footer

        // ── Phase 4: header region ────────────────────────────────────────────────────────────────
        if (sourceModel is not null && headerSlotName is not null)
        {
            HeaderSubEditor = BuildHfSubEditor(
                sourceModel, headerSlot, marginLeft, marginRight, isActivated: false,
                hfPageNumber: pageNumber, hfPageNumberText: pageNumberText, hfPageCount: pageCount);
            Grid.SetRow(HeaderSubEditor, 0);
            stack.Children.Add(HeaderSubEditor);
        }
        else
        {
            // Fallback: read-only label (no slot or no source model provided).
            var headerStrip = BuildStrip($"— Page {pageNumber} —", marginLeft, marginRight);
            Grid.SetRow(headerStrip, 0);
            stack.Children.Add(headerStrip);
        }

        // ── body RichTextBox ──────────────────────────────────────────────────────────────────────
        var bodyFlow = new FlowDocument { PagePadding = new Thickness(0) };
        if (contentWidth > 0)
            bodyFlow.PageWidth = contentWidth;

        // W18: Apply multi-column layout to the body FlowDocument so that pages using
        // PageSettings.ColumnCount > 1 render columns inside the page box correctly.
        // DocumentView.ApplyColumnLayout computes the per-column width from contentWidth and the
        // column spacing — the same calculation used by the continuous editor.
        // Single-column pages (the default) are a no-op (ColumnWidth stays +Infinity).
        DocumentView.ApplyColumnLayout(bodyFlow, page);

        // Move the pre-rendered blocks into the body FlowDocument.  Moving preserves Tags because
        // the block objects themselves are not recreated — only their parent pointer changes.
        foreach (var block in pageBlocks)
            bodyFlow.Blocks.Add(block);

        Body = new RichTextBox
        {
            Document = bodyFlow,
            IsReadOnly = isParitySyntheticPage,
            IsDocumentEnabled = true,
            AcceptsTab = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(marginLeft, marginTop, marginRight, marginBottom),
            VerticalContentAlignment = page.VerticalAlignment switch
            {
                PageVerticalAlignment.Center => VerticalAlignment.Center,
                PageVerticalAlignment.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top
            },
            // Fix height to the full page content height so the box has a definite page size.
            MinHeight = contentHeight + marginTop + marginBottom,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // ── Cross-page caret routing ──────────────────────────────────────────────────────────────
        Body.PreviewKeyDown += OnBodyPreviewKeyDown;

        Grid.SetRow(Body, 1);
        stack.Children.Add(Body);

        // ── Footnote / endnote region (row 2) ─────────────────────────────────────────────────────
        // Rendered as a read-only StackPanel: separator rule + one TextBlock per note entry.
        // If neither footnotes nor endnotes are present, the row collapses to zero height.
        var noteIds = endnoteIds is { Count: > 0 } ? endnoteIds : footnoteIds;
        bool isEndnoteBox = endnoteIds is { Count: > 0 };
        if (sourceModel is not null && noteIds is { Count: > 0 })
        {
            var noteRegion = BuildNoteRegion(
                sourceModel, footnoteIds ?? Array.Empty<int>(),
                endnoteIds ?? Array.Empty<int>(),
                marginLeft, marginRight, contentWidth, isEndnoteBox);
            Grid.SetRow(noteRegion, 2);
            stack.Children.Add(noteRegion);
        }

        // ── Phase 4: footer region (row 3) ───────────────────────────────────────────────────────
        if (sourceModel is not null && footerSlotName is not null)
        {
            FooterSubEditor = BuildHfSubEditor(
                sourceModel, footerSlot, marginLeft, marginRight, isActivated: false,
                hfPageNumber: pageNumber, hfPageNumberText: pageNumberText, hfPageCount: pageCount);
            Grid.SetRow(FooterSubEditor, 3);
            stack.Children.Add(FooterSubEditor);
        }
        else
        {
            var footerStrip = BuildStrip(string.Empty, marginLeft, marginRight);
            Grid.SetRow(footerStrip, 3);
            stack.Children.Add(footerStrip);
        }

        Child = stack;
    }

    // ── Phase 4: wrapper-document sub-editor builder ──────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="DocumentView"/> sub-editor for a header or footer slot, using the same
    /// wrapper-document pattern as the Wave 11 docked pane.  The wrapper is seeded with the main
    /// document's <c>DefaultRun</c> / <c>DefaultParagraph</c> so fonts match; the slot's
    /// <see cref="HeaderFooter.Paragraphs"/> are transferred directly (preserving run formatting).
    ///
    /// <para>
    /// The sub-editor starts dimmed (Opacity 0.45) to signal it is inactive (Word-style).  A
    /// <c>GotFocus</c> handler removes the dim and a <c>LostFocus</c> handler restores it, so only
    /// the active region is fully opaque.
    /// </para>
    ///
    /// <para>
    /// <strong>Live page numbers (W18):</strong> when <paramref name="hfPageNumber"/> is non-zero,
    /// it is injected into <see cref="DocumentView._renderHfPageNumber"/> / <see cref="DocumentView._renderHfPageCount"/>
    /// immediately before <c>LoadModel</c> and cleared immediately after.  This makes PAGE/NUMPAGES fields
    /// in the slot render as the correct page number without mutating the model's field runs.
    /// </para>
    /// </summary>
    private static DocumentView BuildHfSubEditor(
        TextDocument sourceModel,
        HeaderFooter? slot,
        double marginLeft,
        double marginRight,
        bool isActivated,
        int hfPageNumber = 0,
        string? hfPageNumberText = null,
        int hfPageCount = 0)
    {
        // Build wrapper document (same pattern as MainWindow.OpenHeaderFooterPane).
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun       = sourceModel.DefaultRun;
        wrapper.DefaultParagraph = sourceModel.DefaultParagraph;
        wrapper.Blocks.Clear();

        if (slot is not null)
        {
            foreach (var para in slot.Paragraphs)
                wrapper.Blocks.Add(para);
        }
        if (wrapper.Blocks.Count == 0)
            wrapper.Blocks.Add(new FreeW.Core.Model.Paragraph());

        var sub = new DocumentView
        {
            MinHeight = HeaderHeightDip - 4,
            MaxHeight = HeaderHeightDip - 4,
            Margin    = new Thickness(marginLeft, 2, marginRight, 2),
            // Transparent background so the page-white shows through.
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            // Suppress the built-in scrollbars — the strip has a fixed height.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            // Start dimmed (Word-style: header is inactive until clicked).
            Opacity = isActivated ? 1.0 : 0.45,
        };

        // ── Live page-number injection (W18) ──────────────────────────────────────────────────────
        // Set the thread-static context fields so that PAGE/NUMPAGES field runs in this slot render
        // as the actual page number for this box.  Cleared immediately after LoadModel so the context
        // cannot leak into any subsequent render pass on this thread.
        if (hfPageNumber > 0)
        {
            DocumentView._renderHfPageNumber = hfPageNumber;
            DocumentView._renderHfPageNumberText = hfPageNumberText;
            DocumentView._renderHfPageCount  = hfPageCount > 0 ? hfPageCount : 1;
        }
        try
        {
            sub.LoadModel(wrapper);
        }
        finally
        {
            DocumentView._renderHfPageNumber = 0;
            DocumentView._renderHfPageNumberText = null;
            DocumentView._renderHfPageCount  = 0;
        }

        // Dim/undim on focus changes (Word-style activation).
        sub.GotFocus  += (_, _) => sub.Opacity = 1.0;
        sub.LostFocus += (_, _) => sub.Opacity = 0.45;

        return sub;
    }

    // ── Phase 4: commit header/footer sub-editors back to model slots ─────────────────────────────

    /// <summary>
    /// Commits the header and footer sub-editors back to the appropriate
    /// <see cref="SectionHeadersFooters"/> slots on <paramref name="hf"/>.  Mirrors the
    /// <c>CloseHeaderFooterPane</c> commit pattern.  Called by
    /// <see cref="PaginatedCommitCoordinator"/> during panel exit.
    ///
    /// <para>Only the <em>first</em> page box that owns a given slot should call this; the panel
    /// coordinator ensures that only one page box per slot name triggers the commit.</para>
    /// </summary>
    internal void CommitHfSlots(DocumentView helper, SectionHeadersFooters hf)
    {
        CommitOneSlot(HeaderSubEditor, HeaderSlotName, helper, hf);
        CommitOneSlot(FooterSubEditor, FooterSlotName, helper, hf);
    }

    private static void CommitOneSlot(
        DocumentView? subEditor,
        string? slotName,
        DocumentView helper,
        SectionHeadersFooters hf)
    {
        if (subEditor is null || slotName is null)
            return;

        // Flush sub-editor edits into its wrapper model.
        subEditor.CommitToModel();

        // Build a new HeaderFooter from the wrapper's blocks (same pattern as
        // MainWindow.CloseHeaderFooterPane).
        var hfOut = new HeaderFooter();
        foreach (var block in subEditor.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>())
            hfOut.Paragraphs.Add(block);

        // Write back to the correct slot.
        switch (slotName)
        {
            case "header":       hf.Header      = hfOut; break;
            case "footer":       hf.Footer      = hfOut; break;
            case "even-header":  hf.EvenHeader  = hfOut; break;
            case "even-footer":  hf.EvenFooter  = hfOut; break;
            case "first-header": hf.FirstHeader = hfOut; break;
            case "first-footer": hf.FirstFooter = hfOut; break;
        }
    }

    // ── cross-page caret routing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Intercepts arrow-key presses at the edges of the page box and routes the caret (or
    /// cross-page selection) to the adjacent page box.
    ///
    /// <list type="bullet">
    ///   <item><c>Down</c> or <c>Right</c> at the document end → next box start.</item>
    ///   <item><c>Up</c> or <c>Left</c> at the document start → previous box end.</item>
    ///   <item><c>Shift+Down/Right</c> at the document end → fires
    ///   <see cref="ShiftArrowBoundaryReached"/> so the panel can extend the cross-page
    ///   selection into the next box.</item>
    ///   <item><c>Shift+Up/Left</c> at the document start → same, backwards.</item>
    /// </list>
    ///
    /// All other keys, and arrow keys that are not at an edge, fall through to the native
    /// <see cref="RichTextBox"/> handler.
    /// </summary>
    private void OnBodyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Down:
            case Key.Right:
                if (NextBox is not null && IsCaretAtDocumentEnd())
                {
                    if (shiftHeld)
                    {
                        // Notify the panel: extend selection into next box.
                        ShiftArrowBoundaryReached?.Invoke(this, movingForward: true);
                        e.Handled = true;
                    }
                    else
                    {
                        MoveCaretToBoxStart(NextBox);
                        e.Handled = true;
                    }
                }
                break;

            case Key.Up:
            case Key.Left:
                if (PreviousBox is not null && IsCaretAtDocumentStart())
                {
                    if (shiftHeld)
                    {
                        // Notify the panel: extend selection into previous box.
                        ShiftArrowBoundaryReached?.Invoke(this, movingForward: false);
                        e.Handled = true;
                    }
                    else
                    {
                        MoveCaretToBoxEnd(PreviousBox);
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    /// <summary>Returns whether the caret is positioned at or past the last insertion point of the document.</summary>
    private bool IsCaretAtDocumentEnd()
    {
        try
        {
            var end = Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            if (end is null)
                return false;
            var caret = Body.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            if (caret is null)
                return false;
            return caret.CompareTo(end) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns whether the caret is positioned at or before the first insertion point of the document.</summary>
    private bool IsCaretAtDocumentStart()
    {
        try
        {
            var start = Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            if (start is null)
                return false;
            var caret = Body.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            if (caret is null)
                return false;
            return caret.CompareTo(start) <= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Routes the caret to the first insertion position in <paramref name="target"/> and focuses it.
    /// Called when Down/Right is pressed at the end of this box.
    /// </summary>
    private static void MoveCaretToBoxStart(PageBox target)
    {
        target.Body.Focus();
        try
        {
            var start = target.Body.Document.ContentStart
                .GetInsertionPosition(LogicalDirection.Forward);
            if (start is not null)
                target.Body.CaretPosition = start;
        }
        catch { /* caret at default location */ }
    }

    /// <summary>
    /// Routes the caret to the last insertion position in <paramref name="target"/> and focuses it.
    /// Called when Up/Left is pressed at the start of this box.
    /// </summary>
    private static void MoveCaretToBoxEnd(PageBox target)
    {
        target.Body.Focus();
        try
        {
            var end = target.Body.Document.ContentEnd
                .GetInsertionPosition(LogicalDirection.Backward);
            if (end is not null)
                target.Body.CaretPosition = end;
        }
        catch { /* caret at default location */ }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static Border BuildStrip(string text, double padLeft, double padRight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            Padding = new Thickness(padLeft, 2, padRight, 2),
            Child = label
        };
    }

    // ── Footnote / endnote region builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the read-only note region displayed at the bottom of a page (above the footer).
    /// For footnotes: a short horizontal separator rule (matching Word's footnote separator) followed
    /// by the footnote texts in smaller type, each prefixed with a superscript number matching the
    /// body reference mark.  For the synthetic endnotes page: a heading "Endnotes" + separator +
    /// numbered endnote texts.
    ///
    /// <para>The region is read-only (not a RichTextBox sub-editor) because footnote content round-
    /// trips through the model, not through the page-box commit path.</para>
    /// </summary>
    private static StackPanel BuildNoteRegion(
        TextDocument model,
        IReadOnlyList<int> footnoteIds,
        IReadOnlyList<int> endnoteIds,
        double marginLeft,
        double marginRight,
        double contentWidth,
        bool isEndnotePage)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        double textSizePx = FootnoteTextSizePt * (96.0 / 72.0);
        var plan = footnoteIds.Count > 0
            ? DocumentNoteRegionPlanner.BuildFootnoteRegion(model, footnoteIds, pageNumber: 1, contentWidth)
            : DocumentNoteRegionPlanner.BuildEndnoteRegion(model, endnoteIds, pageNumber: 1, contentWidth, isEndnotePage);

        if (plan.Kind == DocumentNoteRegionKind.Footnotes && plan.Rows.Count > 0)
        {
            // ── Footnote separator rule ────────────────────────────────────────────────────────────
            // Word renders a ~50mm (about 1/3 of the text column) horizontal rule.
            panel.Children.Add(new Border
            {
                Height = FootnoteSeparatorHeight,
                Width = plan.SeparatorWidthDip,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(marginLeft, 4, 0, 2),
                Background = Brushes.Black
            });

            // ── Footnote text entries ──────────────────────────────────────────────────────────────
            foreach (var row in plan.Rows)
            {
                panel.Children.Add(BuildNoteTextBlock(
                    row.Label,
                    row.Text,
                    marginLeft,
                    marginRight,
                    textSizePx));
            }
        }

        if (plan.Kind == DocumentNoteRegionKind.Endnotes && plan.Rows.Count > 0)
        {
            // ── Endnotes page heading + separator ─────────────────────────────────────────────────
            if (isEndnotePage)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = plan.Heading ?? "Endnotes",
                    FontSize = textSizePx + 2,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(marginLeft, 8, marginRight, 2)
                });
            }

            panel.Children.Add(new Border
            {
                Height = FootnoteSeparatorHeight,
                Width = plan.SeparatorWidthDip,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(marginLeft, 2, marginRight, 2),
                Background = Brushes.Black
            });

            // ── Endnote text entries ───────────────────────────────────────────────────────────────
            foreach (var row in plan.Rows)
            {
                panel.Children.Add(BuildNoteTextBlock(
                    row.Label,
                    row.Text,
                    marginLeft,
                    marginRight,
                    textSizePx));
            }
        }

        return panel;
    }

    /// <summary>
    /// Builds one note entry line: a superscript number label followed by the note text.
    /// The number visually matches the in-body reference superscript.
    /// </summary>
    private static TextBlock BuildNoteTextBlock(
        string number,
        string text,
        double marginLeft,
        double marginRight,
        double textSizePx)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(marginLeft, 1, marginRight, 1),
            FontSize = textSizePx
        };

        if (!string.IsNullOrEmpty(number))
        {
            tb.Inlines.Add(new System.Windows.Documents.Run(number)
            {
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = textSizePx * 0.75
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" "));
        }
        else
        {
            tb.Inlines.Add(new System.Windows.Documents.Run("\u200B")
            {
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = textSizePx * 0.75
            });
        }

        tb.Inlines.Add(new System.Windows.Documents.Run(text));

        return tb;
    }
}
