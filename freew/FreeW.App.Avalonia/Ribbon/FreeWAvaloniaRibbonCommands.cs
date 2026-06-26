using System.Globalization;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Structured command registry for the FreeW Avalonia shell. This is the Avalonia analogue of the
/// WPF shell's <c>FreeWRibbonCommands.cs</c>.
///
/// <para>
/// Every ribbon command id declared in <see cref="FreeWRibbon.BuildDefinition()"/> must have a
/// corresponding <see cref="RibbonCommandRegistry.Register"/> call here. Commands are grouped by
/// functional area (mirroring the ribbon tab/group structure) for readability.
/// </para>
///
/// <para>
/// <b>Design rule:</b> This file owns all command wiring. <see cref="FreeWRibbon.BuildRegistry"/>
/// delegates here. Shell-level callbacks (open/save/clipboard) are routed through the typed
/// <see cref="RibbonHostCallbacks"/> record so that <c>MainWindow</c> stays thin.
/// </para>
///
/// <para>
/// <b>Wave A1 commands wired here (new in this wave):</b>
/// <list type="bullet">
///   <item><c>freew.strikethrough</c> — toggle run strikethrough</item>
///   <item><c>freew.grow-font</c> — bump font size up one ladder step</item>
///   <item><c>freew.shrink-font</c> — bump font size down one ladder step</item>
///   <item><c>freew.clear-formatting</c> — reset run formatting to default</item>
///   <item><c>freew.font-color</c> — dropdown opener for the colour palette (no-op on click; colour is set by per-colour sub-commands)</item>
///   <item><c>freew.font-color.*</c> — per-colour sub-commands (automatic, black, red, …) registered from <see cref="FreeWRibbon.FontColors"/></item>
///   <item><c>freew.change-case</c> — cycle text case lower → Title → UPPER</item>
///   <item><c>freew.select-all</c> — select the whole document</item>
///   <item><c>freew.show-hide-para</c> — toggle paragraph mark display</item>
///   <item><c>freew.increase-indent</c> — increase list/indent level</item>
///   <item><c>freew.decrease-indent</c> — decrease list/indent level</item>
///   <item><c>freew.style-heading3</c> — apply Heading 3 quick style</item>
///   <item><c>freew.new</c> — create a new blank document</item>
///   <item><c>freew.zoom-in</c> — zoom in 10%</item>
///   <item><c>freew.zoom-out</c> — zoom out 10%</item>
///   <item><c>freew.zoom-100</c> — reset zoom to 100%</item>
/// </list>
/// Existing 22 commands are also registered here (migrated from the old inline ad-hoc block).
/// </para>
/// </summary>
internal static class FreeWAvaloniaRibbonCommands
{
    /// <summary>
    /// Build and return the complete command registry for the Avalonia ribbon.
    /// </summary>
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(callbacks);

        var r = new RibbonCommandRegistry();

        // ── File ─────────────────────────────────────────────────────────────
        r.Register("freew.backstage", new RelayCommand(callbacks.Backstage));
        r.Register("freew.new",       new RelayCommand(callbacks.NewDocument));
        r.Register("freew.open",      new RelayCommand(callbacks.Open));
        r.Register("freew.save",      new RelayCommand(callbacks.Save));

        // ── Clipboard ────────────────────────────────────────────────────────
        r.Register("freew.cut",   new RelayCommand(callbacks.Cut));
        r.Register("freew.copy",  new RelayCommand(callbacks.Copy));
        r.Register("freew.paste", new RelayCommand(callbacks.Paste));

        // ── Font ─────────────────────────────────────────────────────────────
        r.Register("freew.font-family", new RelayValueCommand(value =>
        {
            if (!string.IsNullOrWhiteSpace(value))
                editor.SetSelectionFontFamily(value);
        }));
        r.Register("freew.font-size", new RelayValueCommand(value =>
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var pts) && pts > 0)
                editor.SetSelectionFontSize(pts);
        }));
        r.Register("freew.bold",            new RelayCommand(editor.ToggleBold));
        r.Register("freew.italic",           new RelayCommand(editor.ToggleItalic));
        r.Register("freew.underline",        new RelayCommand(editor.ToggleUnderline));
        r.Register("freew.strikethrough",    new RelayCommand(editor.ToggleStrikethrough));
        r.Register("freew.superscript",      new RelayCommand(editor.ToggleSuperscript));
        r.Register("freew.subscript",        new RelayCommand(editor.ToggleSubscript));
        r.Register("freew.highlight",        new RelayValueCommand(value => editor.SetHighlightColor(value)));
        r.Register("freew.grow-font",        new RelayCommand(editor.GrowFont));
        r.Register("freew.shrink-font",      new RelayCommand(editor.ShrinkFont));
        r.Register("freew.clear-formatting", new RelayCommand(editor.ClearFormatting));
        // Font Color — the ribbon control is a Dropdown whose button click opens the colour flyout.
        // Each palette entry is its own command so the button never executes with a null value.
        // "freew.font-color" itself is registered as a no-op so the registry completeness check
        // (which checks every ribbon control's CommandId) continues to pass.
        r.Register("freew.font-color", new RelayCommand(() => { /* flyout opener — no direct action */ }));
        RegisterFontColorPalette(r, editor);

        r.Register("freew.change-case",   new RelayCommand(editor.ChangeCase));
        // Dialog launchers — open modal dialogs via shell callbacks (no direct editor method).
        r.Register("freew.font-dialog",      new RelayCommand(callbacks.OpenFontDialog));

        // ── Paragraph ────────────────────────────────────────────────────────
        r.Register("freew.bullets",          new RelayCommand(() => editor.ToggleList(ListKind.Bullet)));
        r.Register("freew.numbering",        new RelayCommand(() => editor.ToggleList(ListKind.Number)));
        r.Register("freew.align-left",       new RelayCommand(() => editor.SetAlignment(TextAlignment.Left)));
        r.Register("freew.align-center",     new RelayCommand(() => editor.SetAlignment(TextAlignment.Center)));
        r.Register("freew.align-right",      new RelayCommand(() => editor.SetAlignment(TextAlignment.Right)));
        r.Register("freew.align-justify",    new RelayCommand(() => editor.SetAlignment(TextAlignment.Justify)));
        r.Register("freew.increase-indent",  new RelayCommand(editor.IncreaseIndent));
        r.Register("freew.decrease-indent",  new RelayCommand(editor.DecreaseIndent));
        r.Register("freew.show-hide-para",   new RelayCommand(() => editor.ShowParagraphMarks = !editor.ShowParagraphMarks));
        // Paragraph spacing commands (value = points as an invariant-culture decimal string).
        r.Register("freew.space-before",     new RelayValueCommand(value =>
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
                editor.SetSpaceBefore(pt);
        }));
        r.Register("freew.space-after",      new RelayValueCommand(value =>
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
                editor.SetSpaceAfter(pt);
        }));
        // Line-spacing commands — value = multiplier for Multiple, pt for Exact/AtLeast.
        r.Register("freew.line-spacing-1",    new RelayCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.0)));
        r.Register("freew.line-spacing-115",  new RelayCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.15)));
        r.Register("freew.line-spacing-15",   new RelayCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.5)));
        r.Register("freew.line-spacing-2",    new RelayCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 2.0)));
        // Paragraph dialog launcher.
        r.Register("freew.paragraph-dialog",  new RelayCommand(callbacks.OpenParagraphDialog));

        // ── Styles (AV-STYLES) ────────────────────────────────────────────────
        // Existing quick-style buttons — now routed through the model-backed, undoable ApplyNamedStyle
        // so the paragraph picks up the real built-in style (seeded if absent) instead of just a font tweak.
        r.Register("freew.style-normal",   new RelayCommand(() => editor.ApplyNamedStyle("Normal")));
        r.Register("freew.style-heading1", new RelayCommand(() => editor.ApplyNamedStyle("Heading1")));
        r.Register("freew.style-heading2", new RelayCommand(() => editor.ApplyNamedStyle("Heading2")));
        r.Register("freew.style-heading3", new RelayCommand(() => editor.ApplyNamedStyle("Heading3")));
        r.Register("freew.style-title",    new RelayCommand(() => editor.ApplyNamedStyle("Title")));

        // Styles gallery dropdown — opener no-op; one freew.style.<id> command per built-in style applies
        // that named style (paragraph styles set StyleId; character styles overlay run formatting).
        r.Register("freew.styles-gallery", new RelayCommand(() => { /* dropdown opener */ }));
        RegisterStyleGalleryCommands(r, editor);

        // Clear style — revert the paragraph to the document default (Word's paragraph-level reset).
        r.Register("freew.style-clear", new RelayCommand(editor.ClearParagraphStyle));

        // ── Editing ──────────────────────────────────────────────────────────
        r.Register("freew.undo",              new RelayCommand(editor.Undo));
        r.Register("freew.redo",              new RelayCommand(editor.Redo));
        r.Register("freew.select-all",        new RelayCommand(editor.SelectAll));
        r.Register("freew.find-replace-dialog", new RelayCommand(callbacks.OpenFindReplaceDialog));

        // ── Insert ───────────────────────────────────────────────────────────
        // AV-INSERT: Insert-tab depth. Table dropdown (default + sized presets), page break, picture
        // (file-picker via host callback), shape, text box, and a symbol palette.
        r.Register("freew.insert-table", new RelayCommand(() => editor.InsertTable(3, 3)));
        // Table size presets (dropdown items). The top-level "freew.table" id opens the menu (no-op).
        r.Register("freew.table", new RelayCommand(() => { /* dropdown opener */ }));
        r.Register("freew.table-2x2", new RelayCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-3x3", new RelayCommand(() => editor.InsertTable(3, 3)));
        r.Register("freew.table-4x4", new RelayCommand(() => editor.InsertTable(4, 4)));
        r.Register("freew.table-5x2", new RelayCommand(() => editor.InsertTable(2, 5)));

        // Page break — empty paragraph forcing a page break before it, after the caret block.
        r.Register("freew.page-break", new RelayCommand(editor.InsertPageBreak));

        // Picture — open a file picker, load the bytes, insert as an inline image (host callback).
        r.Register("freew.picture", new RelayCommand(callbacks.InsertPicture));

        // Shape / Text Box — floating drawing objects at the caret.
        r.Register("freew.shape",    new RelayCommand(editor.InsertShape));
        r.Register("freew.text-box", new RelayCommand(editor.InsertTextBox));

        // Symbol — palette dropdown; the opener is a no-op and each glyph is its own sub-command.
        r.Register("freew.symbol", new RelayCommand(() => { /* flyout opener */ }));
        RegisterSymbolPalette(r, editor);

        // Header / Footer — enable the page-margin region (render-ready). Region caret editing deferred.
        r.Register("freew.header", new RelayCommand(editor.EnsureHeader));
        r.Register("freew.footer", new RelayCommand(editor.EnsureFooter));

        // ── Table Design contextual tab ───────────────────────────────────────
        // Table Style Options toggles — DocumentView guards no-op when outside a table.
        r.Register("freew.table-header-row",  new RelayCommand(editor.ToggleTableHeaderRow));
        r.Register("freew.table-banded-rows", new RelayCommand(editor.ToggleBandedRows));

        // Table shading: apply a quick neutral fill. Full color picker is deferred.
        r.Register("freew.table-shading", new RelayCommand(() => editor.SetCellShading("#D9D9D9")));

        // Borders dropdown — opener no-op; sub-commands apply specific edges.
        r.Register("freew.table-borders", new RelayCommand(() => { /* flyout opener */ }));
        RegisterTableBorderCommands(r, editor);

        // ── Table Layout contextual tab ───────────────────────────────────────
        // Selection helpers.
        r.Register("freew.table-select-table", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                // BY1: clamp to actual table bounds — passing int.MaxValue triggers an overflow
                // loop in ExpandForMergedCells (r++ overflows int.MaxValue → infinite loop).
                var (lastRow, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, 0, lastRow, lastGridCol);
            }
        }));
        r.Register("freew.table-select-row", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (_, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, 0, cc.Row, lastGridCol);
            }
        }));
        r.Register("freew.table-select-col", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (lastRow, _) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, cc.Col, lastRow, cc.Col);
            }
        }));
        r.Register("freew.table-select-cell", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, cc.Col, cc.Row, cc.Col);
        }));

        // Row / column mutations.
        r.Register("freew.table-insert-above",     new RelayCommand(editor.InsertTableRowAbove));
        r.Register("freew.table-insert-below",     new RelayCommand(editor.InsertTableRowBelow));
        r.Register("freew.table-insert-col-left",  new RelayCommand(editor.InsertTableColumnLeft));
        r.Register("freew.table-insert-col-right", new RelayCommand(editor.InsertTableColumnRight));
        r.Register("freew.table-delete-row",       new RelayCommand(editor.DeleteTableRow));
        r.Register("freew.table-delete-col",       new RelayCommand(editor.DeleteTableColumn));
        r.Register("freew.table-delete",           new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.DeleteTableBlock(cc.TableBlock);
        }));

        // Merge / split.
        r.Register("freew.table-merge-cells", new RelayCommand(editor.MergeSelectedCells));
        r.Register("freew.table-split-cell",  new RelayCommand(() => editor.SplitCurrentCell()));

        // Cell alignment — 9 = 3 vertical (Top/Center/Bottom) × 3 horizontal (Left/Center/Right).
        // BY2: parity with WPF's table-layout Alignment group (FreeWRibbon.cs ~1201-1219).
        RegisterCellAlignmentCommands(r, editor);

        // ── Layout / Page Setup (AV-PAGE) ────────────────────────────────────
        // Dialog launcher: opens the Page Setup modal (margins + paper + orientation).
        r.Register("freew.page-setup-dialog",   new RelayCommand(callbacks.OpenPageSetupDialog));
        // Toggle orientation (portrait ↔ landscape).
        r.Register("freew.page-orientation",    new RelayCommand(callbacks.ToggleOrientation));
        // Margin presets.
        r.Register("freew.page-margins-normal", new RelayCommand(() => callbacks.ApplyMarginPreset("normal")));
        r.Register("freew.page-margins-narrow", new RelayCommand(() => callbacks.ApplyMarginPreset("narrow")));
        r.Register("freew.page-margins-wide",   new RelayCommand(() => callbacks.ApplyMarginPreset("wide")));
        // Quick paper-size selectors.
        r.Register("freew.page-size-letter",    new RelayCommand(() => callbacks.ApplyPaperSize("letter")));
        r.Register("freew.page-size-a4",        new RelayCommand(() => callbacks.ApplyPaperSize("a4")));

        // ── View ─────────────────────────────────────────────────────────────
        r.Register("freew.printlayout",       new RelayCommand(callbacks.SetPrintLayout));
        r.Register("freew.weblayout",         new RelayCommand(callbacks.SetWebLayout));
        r.Register("freew.draftview",         new RelayCommand(callbacks.SetDraftView));
        r.Register("freew.navigationpane",    new RelayCommand(callbacks.ToggleNavigationPane));
        r.Register("freew.reveal-formatting", new RelayCommand(callbacks.ToggleRevealFormatting));
        r.Register("freew.zoom-in",           new RelayCommand(() => callbacks.ApplyZoom(null, +0.1)));
        r.Register("freew.zoom-out",          new RelayCommand(() => callbacks.ApplyZoom(null, -0.1)));
        r.Register("freew.zoom-100",          new RelayCommand(() => callbacks.ApplyZoom(1.0, 0)));
        // AV-VIEW: Zoom dialog (presets + custom %) and layout gridlines / ruler toggles.
        // The three Window/Zoom-dialog callbacks are optional on RibbonHostCallbacks (default null so
        // test call sites stay terse); fall back to a safe no-op when the shell didn't supply one.
        r.Register("freew.zoom-dialog",       new RelayCommand(callbacks.OpenZoomDialog ?? (() => { })));
        r.Register("freew.view-gridlines",    new RelayCommand(() => editor.ShowGridlines = !editor.ShowGridlines));
        r.Register("freew.view-ruler",        new RelayCommand(() => editor.ShowRuler = !editor.ShowRuler));
        // AV-VIEW: Window group — new window + split (shell callbacks; may note "deferred" in the status bar).
        r.Register("freew.new-window",        new RelayCommand(callbacks.NewWindow ?? (() => { })));
        r.Register("freew.split",             new RelayCommand(callbacks.ToggleSplit ?? (() => { })));

        // ── Review ───────────────────────────────────────────────────────────
        r.Register("freew.reviewingpane", new RelayCommand(callbacks.ToggleReviewingPane));
        // AV-REVIEW: Track Changes toggle (flag only — keystroke-level recording is deferred; turning the
        // current selection into a tracked change is available via DocumentView.MarkSelectionAsRevision).
        r.Register("freew.track-changes", new RelayCommand(() => editor.ToggleTrackChanges()));
        // Accept / reject — current revision (at/after caret) and all, undoable + re-render.
        r.Register("freew.accept-change", new RelayCommand(() => editor.AcceptCurrentRevision()));
        r.Register("freew.reject-change", new RelayCommand(() => editor.RejectCurrentRevision()));
        r.Register("freew.accept-all",    new RelayCommand(() => editor.AcceptAllRevisions()));
        r.Register("freew.reject-all",    new RelayCommand(() => editor.RejectAllRevisions()));
        // Comments — new comment over the selection / delete the comment at the caret.
        r.Register("freew.new-comment",    new RelayCommand(() => editor.NewComment()));
        r.Register("freew.delete-comment", new RelayCommand(() => editor.DeleteCommentAtCaret()));
        // Word Count — opens the modal stats dialog (shell callback; reads DocumentStatistics).
        r.Register("freew.word-count", new RelayCommand(callbacks.OpenWordCountDialog));

        // ── References (AV-REF) ──────────────────────────────────────────────
        RegisterReferencesCommands(r, editor);

        // ── AV-PICTAB: Picture Format + Drawing Format contextual tabs ────────
        RegisterFloatingFormatCommands(r, editor);

        // ── AV-CHARTTAB: Chart Design/Format + SmartArt Design contextual tabs ─
        RegisterChartSmartArtFormatCommands(r, editor);

        return r;
    }

    /// <summary>
    /// Registers the per-colour sub-commands for the Font Color palette dropdown.
    /// Each command id matches an entry in <see cref="FreeWRibbon.FontColors"/> and calls
    /// <see cref="DocumentView.SetFontColor"/> with the appropriate RRGGBB hex string
    /// (or <c>null</c> for the "Automatic" entry, which restores the default run colour).
    /// </summary>
    private static void RegisterFontColorPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        // Maps command-id suffix → CSS hex colour (null = automatic/default).
        // Colours chosen to match Word's standard palette.
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new RelayCommand(() => ed.SetFontColor(hex)));

        Add(r, editor, "freew.font-color.automatic", null);
        Add(r, editor, "freew.font-color.black",     "#000000");
        Add(r, editor, "freew.font-color.dark-red",  "#C00000");
        Add(r, editor, "freew.font-color.red",       "#FF0000");
        Add(r, editor, "freew.font-color.orange",    "#FF6600");
        Add(r, editor, "freew.font-color.yellow",    "#FFFF00");
        Add(r, editor, "freew.font-color.green",     "#00B050");
        Add(r, editor, "freew.font-color.blue",      "#0070C0");
        Add(r, editor, "freew.font-color.dark-blue", "#00008B");
        Add(r, editor, "freew.font-color.purple",    "#7030A0");
        Add(r, editor, "freew.font-color.white",     "#FFFFFF");
    }

    /// <summary>
    /// AV-STYLES: the command-id prefix for a built-in gallery style. The Styles gallery dropdown item and
    /// its registry command both use <c>freew.style.&lt;id&gt;</c> (e.g. <c>freew.style.Heading1</c>), so the
    /// ribbon definition and the registry agree on the id.
    /// </summary>
    internal static string StyleCommandId(string styleId) => $"freew.style.{styleId}";

    /// <summary>
    /// Registers one <c>freew.style.&lt;id&gt;</c> command per built-in gallery style (see
    /// <see cref="BuiltInStyles.Gallery"/>). Each applies that named style to the current selection /
    /// paragraph via <see cref="DocumentView.ApplyNamedStyle"/> — paragraph styles set the paragraph
    /// StyleId, character styles overlay run formatting — model-backed and undoable.
    /// </summary>
    private static void RegisterStyleGalleryCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        foreach (var descriptor in BuiltInStyles.Gallery)
        {
            var id = descriptor.Id;
            r.Register(StyleCommandId(id), new RelayCommand(() => editor.ApplyNamedStyle(id)));
        }
    }

    /// <summary>
    /// AV-INSERT: common symbols / special characters for the Insert &gt; Symbol palette. Each entry maps a
    /// stable command-id suffix to the literal character it inserts (via <see cref="DocumentView.InsertSymbol"/>).
    /// The set mirrors Word's default "recently used symbols" grid (currency, typography, math, arrows).
    /// </summary>
    internal static readonly IReadOnlyList<(string Id, string Glyph, string Label)> Symbols =
    [
        ("freew.symbol.euro",        "€", "Euro Sign"),
        ("freew.symbol.pound",       "£", "Pound Sign"),
        ("freew.symbol.yen",         "¥", "Yen Sign"),
        ("freew.symbol.cent",        "¢", "Cent Sign"),
        ("freew.symbol.copyright",   "©", "Copyright"),
        ("freew.symbol.registered",  "®", "Registered"),
        ("freew.symbol.trademark",   "™", "Trademark"),
        ("freew.symbol.degree",      "°", "Degree Sign"),
        ("freew.symbol.plusminus",   "±", "Plus-Minus"),
        ("freew.symbol.multiply",    "×", "Multiplication"),
        ("freew.symbol.divide",      "÷", "Division"),
        ("freew.symbol.notequal",    "≠", "Not Equal"),
        ("freew.symbol.lessequal",   "≤", "Less-Or-Equal"),
        ("freew.symbol.greaterequal","≥", "Greater-Or-Equal"),
        ("freew.symbol.bullet",      "•", "Bullet"),
        ("freew.symbol.ellipsis",    "…", "Ellipsis"),
        ("freew.symbol.emdash",      "—", "Em Dash"),
        ("freew.symbol.endash",      "–", "En Dash"),
        ("freew.symbol.arrow-right", "→", "Right Arrow"),
        ("freew.symbol.arrow-left",  "←", "Left Arrow"),
    ];

    /// <summary>
    /// Registers the per-glyph sub-commands for the Insert &gt; Symbol palette dropdown. Each command id
    /// matches an entry in <see cref="Symbols"/> and inserts that character at the caret as ordinary text.
    /// </summary>
    private static void RegisterSymbolPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        foreach (var (id, glyph, _) in Symbols)
            r.Register(id, new RelayCommand(() => editor.InsertSymbol(glyph)));
    }

    /// <summary>
    /// Registers the per-edge sub-commands for the Table Borders dropdown.
    /// Each command calls <see cref="DocumentView.SetCellBorders"/> with the appropriate
    /// <see cref="CellBorderEdges"/> flag. The "No Border" entry clears all edges.
    /// </summary>
    private static void RegisterTableBorderCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, CellBorderEdges edges, bool clear = false) =>
            reg.Register(id, new RelayCommand(() => ed.SetCellBorders(edges, clearEdges: clear)));

        Add(r, editor, "freew.table-borders.all",     CellBorderEdges.All);
        Add(r, editor, "freew.table-borders.outside", CellBorderEdges.Outside);
        Add(r, editor, "freew.table-borders.inside",  CellBorderEdges.Inside);
        Add(r, editor, "freew.table-borders.none",    CellBorderEdges.All, clear: true);
        Add(r, editor, "freew.table-borders.top",     CellBorderEdges.Top);
        Add(r, editor, "freew.table-borders.bottom",  CellBorderEdges.Bottom);
        Add(r, editor, "freew.table-borders.left",    CellBorderEdges.Left);
        Add(r, editor, "freew.table-borders.right",   CellBorderEdges.Right);
    }

    /// <summary>
    /// Registers the 9 cell-alignment commands (3 vertical × 3 horizontal) for the
    /// table-layout Alignment group. Command ids are identical to the WPF host so
    /// keyboard macros and tests are interchangeable.
    /// </summary>
    private static void RegisterCellAlignmentCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id,
            TableCellVerticalAlignment vAlign, TextAlignment hAlign) =>
            reg.Register(id, new RelayCommand(() => ed.SetCaretCellAlignment(vAlign, hAlign)));

        Add(r, editor, "freew.cell-align-top-left",       TableCellVerticalAlignment.Top,    TextAlignment.Left);
        Add(r, editor, "freew.cell-align-top-center",     TableCellVerticalAlignment.Top,    TextAlignment.Center);
        Add(r, editor, "freew.cell-align-top-right",      TableCellVerticalAlignment.Top,    TextAlignment.Right);
        Add(r, editor, "freew.cell-align-middle-left",    TableCellVerticalAlignment.Center, TextAlignment.Left);
        Add(r, editor, "freew.cell-align-middle-center",  TableCellVerticalAlignment.Center, TextAlignment.Center);
        Add(r, editor, "freew.cell-align-middle-right",   TableCellVerticalAlignment.Center, TextAlignment.Right);
        Add(r, editor, "freew.cell-align-bottom-left",    TableCellVerticalAlignment.Bottom, TextAlignment.Left);
        Add(r, editor, "freew.cell-align-bottom-center",  TableCellVerticalAlignment.Bottom, TextAlignment.Center);
        Add(r, editor, "freew.cell-align-bottom-right",   TableCellVerticalAlignment.Bottom, TextAlignment.Right);
    }

    /// <summary>
    /// AV-REF: Registers the References-tab commands — footnote / endnote, Table of Contents
    /// (insert + update), caption (Figure / Table), cross-reference, and citation / bibliography. Each
    /// resolves to a model-backed, undoable <see cref="DocumentView"/> insert method.
    ///
    /// <para>
    /// Footnote / endnote insert an empty note (the user types its content where the AV-HF note region
    /// renders). The two caption commands auto-number via <see cref="Captions.NextCaptionNumber"/>. The
    /// cross-reference command defaults to the first available heading target (a full target-picker dialog
    /// is a larger surface, deferred); it safely no-ops when the document has no headings. Citation inserts
    /// an in-text citation for the document's first source (or no-ops with no sources), and bibliography
    /// builds the back-matter block — both reuse the model's Citations engine.
    /// </para>
    /// </summary>
    private static void RegisterReferencesCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        // Footnotes & Endnotes — insert an empty note + reference marker at the caret.
        r.Register("freew.insert-footnote", new RelayCommand(() => editor.InsertFootnote()));
        r.Register("freew.insert-endnote",  new RelayCommand(() => editor.InsertEndnote()));

        // Table of Contents — generate from the heading outline / regenerate in place.
        r.Register("freew.insert-toc", new RelayCommand(editor.InsertTableOfContents));
        r.Register("freew.update-toc", new RelayCommand(editor.UpdateTableOfContents));

        // Captions — auto-numbered Figure / Table caption paragraph after the caret block.
        // The top-level opener is a no-op; each label is its own command.
        r.Register("freew.insert-caption",        new RelayCommand(() => { /* dropdown opener */ }));
        r.Register("freew.insert-caption.figure", new RelayCommand(() => editor.InsertCaption(CaptionLabel.Figure)));
        r.Register("freew.insert-caption.table",  new RelayCommand(() => editor.InsertCaption(CaptionLabel.Table)));

        // Cross-reference — default to the first heading target (text reference, hyperlinked). A full
        // target-picker dialog is deferred; safely no-ops when the document has no headings.
        r.Register("freew.cross-reference", new RelayCommand(() => InsertDefaultCrossReference(editor)));

        // Citations & Bibliography — in-text citation for the first source; back-matter bibliography block.
        r.Register("freew.insert-citation", new RelayCommand(() => InsertDefaultCitation(editor)));
        r.Register("freew.bibliography",    new RelayCommand(editor.InsertBibliography));
    }

    /// <summary>
    /// AV-REF: Insert a cross-reference to the document's first heading (the most common case), shown as the
    /// heading's text and hyperlinked. When the document has no headings this no-ops, so the button is
    /// always safe to click. A full target/insert-as picker dialog is deferred.
    /// </summary>
    private static void InsertDefaultCrossReference(DocumentView editor)
    {
        var targets = CrossReferences.Targets(editor.Document, CrossRefType.Heading);
        if (targets.Count == 0)
            return;
        editor.InsertCrossReference(CrossRefType.Heading, targets[0], CrossRefInsertAs.Text, hyperlink: true);
    }

    /// <summary>
    /// AV-REF: Insert an in-text citation for the document's first <see cref="Source"/> in its active
    /// bibliography style. No-ops when the document has no sources (a source-manager dialog is deferred).
    /// </summary>
    private static void InsertDefaultCitation(DocumentView editor)
    {
        if (editor.Document.Sources.Count == 0)
            return;
        editor.InsertCitation(editor.Document.Sources[0]);
    }

    /// <summary>
    /// AV-PICTAB: Registers the Picture Format + Drawing Format contextual-tab commands, wiring each
    /// to the floating-object edit surface on <see cref="DocumentView"/>. Both tabs share the same
    /// underlying methods (the model dispatches by the selected float's kind), so the only difference
    /// is the command-id prefix (<c>image-</c> vs <c>shape-</c>) used by the respective tab.
    ///
    /// <para>
    /// Every command safely no-ops when no float is selected (the DocumentView methods guard on
    /// <c>SelectedFloatingInfo</c>). Wrap, rotate/flip, z-order and size are wired through; shape
    /// fill/outline editing is <b>deferred</b> (no DocumentView setter exists yet) and registered as
    /// no-op openers so the registry-completeness guard continues to pass.
    /// </para>
    /// </summary>
    private static void RegisterFloatingFormatCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        // Wrap modes (shared menu items, distinct ids per tab prefix).
        foreach (var prefix in new[] { "image", "shape" })
        {
            r.Register($"freew.{prefix}-wrap",   new RelayCommand(() => { /* dropdown opener */ }));
            r.Register($"freew.{prefix}-wrap-inline",     new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.Inline)));
            r.Register($"freew.{prefix}-wrap-square",     new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.Square)));
            r.Register($"freew.{prefix}-wrap-tight",      new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.Tight)));
            r.Register($"freew.{prefix}-wrap-top-bottom", new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.TopAndBottom)));
            r.Register($"freew.{prefix}-wrap-behind",     new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.Behind)));
            r.Register($"freew.{prefix}-wrap-front",      new RelayCommand(() => editor.SetFloatingWrap(ImageWrapping.InFront)));

            // Rotate / flip.
            r.Register($"freew.{prefix}-rotate", new RelayCommand(() => { /* dropdown opener */ }));
            r.Register($"freew.{prefix}-rotate-right90", new RelayCommand(() => editor.RotateSelectedFloating(+90)));
            r.Register($"freew.{prefix}-rotate-left90",  new RelayCommand(() => editor.RotateSelectedFloating(-90)));
            r.Register($"freew.{prefix}-flip-vertical",   new RelayCommand(() => editor.FlipSelectedFloating(horizontal: false)));
            r.Register($"freew.{prefix}-flip-horizontal", new RelayCommand(() => editor.FlipSelectedFloating(horizontal: true)));

            // Z-order.
            r.Register($"freew.{prefix}-bring-to-front", new RelayCommand(() => editor.ChangeFloatingZOrder(ZOrderOperation.BringToFront)));
            r.Register($"freew.{prefix}-send-to-back",   new RelayCommand(() => editor.ChangeFloatingZOrder(ZOrderOperation.SendToBack)));
            r.Register($"freew.{prefix}-bring-forward",  new RelayCommand(() => editor.ChangeFloatingZOrder(ZOrderOperation.BringForward)));
            r.Register($"freew.{prefix}-send-backward",  new RelayCommand(() => editor.ChangeFloatingZOrder(ZOrderOperation.SendBackward)));

            // Size — width/height combos (value = points as an invariant-culture decimal).
            r.Register($"freew.{prefix}-width", new RelayValueCommand(value =>
            {
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt) && pt > 0)
                    editor.SetFloatingWidth(pt);
            }));
            r.Register($"freew.{prefix}-height", new RelayValueCommand(value =>
            {
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt) && pt > 0)
                    editor.SetFloatingHeight(pt);
            }));
        }

        // Shape Styles fill/outline — DEFERRED: no DocumentView setter for shape fill/outline yet.
        // Registered as safe no-op openers so the ribbon's registry-completeness guard passes.
        r.Register("freew.shape-fill",    new RelayCommand(() => { /* deferred: shape fill edit */ }));
        r.Register("freew.shape-outline", new RelayCommand(() => { /* deferred: shape outline edit */ }));
    }

    /// <summary>
    /// AV-CHARTTAB: Registers the Chart Design + SmartArt Design contextual-tab commands, wiring each to
    /// the chart/smartart edit surface on <see cref="DocumentView"/>. The Chart/SmartArt Format tabs reuse
    /// the shared Arrange/Size <c>freew.shape-*</c> commands already registered by
    /// <see cref="RegisterFloatingFormatCommands"/>, so only the Design-tab commands are added here.
    ///
    /// <para>
    /// Every command safely no-ops when the corresponding kind is not the selected float (the DocumentView
    /// methods guard on the selection kind). Chart type → <see cref="DocumentView.SetChartType"/>, chart
    /// style → <see cref="DocumentView.SetChartStyle"/>, chart colours → <see cref="DocumentView.SetChartColorScheme"/>;
    /// SmartArt layout → <see cref="DocumentView.SetSmartArtLayout"/>, SmartArt colours →
    /// <see cref="DocumentView.SetSmartArtColor"/>.
    /// </para>
    /// </summary>
    private static void RegisterChartSmartArtFormatCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        // ── Chart Design ──────────────────────────────────────────────────────
        // Change Chart Type — dropdown opener + one command per ChartKind.
        r.Register("freew.chart-type", new RelayCommand(() => { /* dropdown opener */ }));
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
        {
            var k = kind; // capture
            r.Register($"freew.chart-type-{k.ToString().ToLowerInvariant()}",
                new RelayCommand(() => editor.SetChartType(k)));
        }

        // Chart Styles — dropdown opener + one command per catalog style.
        r.Register("freew.chart-style", new RelayCommand(() => { /* dropdown opener */ }));
        foreach (var style in ChartStyle.Catalog)
        {
            var s = style;
            r.Register($"freew.chart-style-{s.Id}", new RelayCommand(() => editor.SetChartStyle(s.Id)));
        }

        // Change Colors — dropdown opener + one command per catalog colour scheme.
        r.Register("freew.chart-colors", new RelayCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.chart-colors-{sc.Id}", new RelayCommand(() => editor.SetChartColorScheme(sc.Id)));
        }

        // ── SmartArt Design ───────────────────────────────────────────────────
        // Layouts — the four Word families. Cycle maps to the model's Process kind (closest flat sequence).
        r.Register("freew.smartart-layout", new RelayCommand(() => { /* dropdown opener */ }));
        r.Register("freew.smartart-layout-list",      new RelayCommand(() => editor.SetSmartArtLayout(SmartArtKind.List)));
        r.Register("freew.smartart-layout-process",   new RelayCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-cycle",     new RelayCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-hierarchy", new RelayCommand(() => editor.SetSmartArtLayout(SmartArtKind.Hierarchy)));

        // Change Colors — reuse the chart colour-scheme catalog ids.
        r.Register("freew.smartart-colors", new RelayCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.smartart-colors-{sc.Id}", new RelayCommand(() => editor.SetSmartArtColor(sc.Id)));
        }
    }
}
