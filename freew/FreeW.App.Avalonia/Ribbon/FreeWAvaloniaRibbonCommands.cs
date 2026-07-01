using System.Globalization;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using FreeW.Ribbon.Definitions;
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
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonHostCallbacks callbacks) =>
        Build(editor, callbacks, out _);

    /// <summary>
    /// Build the registry and also surface the <see cref="MailMergeEngine"/> that backs the Mailings tab
    /// (AV-MAIL), so the shell can drive its dialog-bound commands (Select Recipients / Insert Merge Field)
    /// with the async file-picker / prompt and keep the same session the ribbon commands use.
    /// </summary>
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonHostCallbacks callbacks, out MailMergeEngine mailMerge)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(callbacks);

        var r = new RibbonCommandRegistry();
        mailMerge = new MailMergeEngine(editor, callbacks);

        // ── File ─────────────────────────────────────────────────────────────
        r.Register("freew.backstage", new ActionRibbonCommand(callbacks.Backstage));
        r.Register("freew.new",       new ActionRibbonCommand(callbacks.NewDocument));
        r.Register("freew.open",      new ActionRibbonCommand(callbacks.Open));
        r.Register("freew.save",      new ActionRibbonCommand(callbacks.Save));

        // ── Clipboard ────────────────────────────────────────────────────────
        r.Register("freew.cut",   new ActionRibbonCommand(callbacks.Cut));
        r.Register("freew.copy",  new ActionRibbonCommand(callbacks.Copy));
        r.Register("freew.paste", new ActionRibbonCommand(callbacks.Paste));

        // ── Font ─────────────────────────────────────────────────────────────
        r.Register("freew.font-family", new ValueRibbonCommand(value =>
        {
            if (!string.IsNullOrWhiteSpace(value))
                editor.SetSelectionFontFamily(value);
        }));
        r.Register("freew.font-size", new ValueRibbonCommand(value =>
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var pts) && pts > 0)
                editor.SetSelectionFontSize(pts);
        }));
        r.Register("freew.bold",            new ActionRibbonCommand(editor.ToggleBold));
        r.Register("freew.italic",           new ActionRibbonCommand(editor.ToggleItalic));
        r.Register("freew.underline",        new ActionRibbonCommand(editor.ToggleUnderline));
        r.Register("freew.strikethrough",    new ActionRibbonCommand(editor.ToggleStrikethrough));
        r.Register("freew.superscript",      new ActionRibbonCommand(editor.ToggleSuperscript));
        r.Register("freew.subscript",        new ActionRibbonCommand(editor.ToggleSubscript));
        r.Register("freew.highlight",        new ValueRibbonCommand(value => editor.SetHighlightColor(value)));
        r.Register("freew.grow-font",        new ActionRibbonCommand(editor.GrowFont));
        r.Register("freew.shrink-font",      new ActionRibbonCommand(editor.ShrinkFont));
        r.Register("freew.clear-formatting", new ActionRibbonCommand(editor.ClearFormatting));
        // Font Color — the ribbon control is a Dropdown whose button click opens the colour flyout.
        // Each palette entry is its own command so the button never executes with a null value.
        // "freew.font-color" itself is registered as a no-op so the registry completeness check
        // (which checks every ribbon control's CommandId) continues to pass.
        r.Register("freew.font-color", new ActionRibbonCommand(() => { /* flyout opener — no direct action */ }));
        RegisterFontColorPalette(r, editor);

        r.Register("freew.change-case",   new ActionRibbonCommand(editor.ChangeCase));
        // Dialog launchers — open modal dialogs via shell callbacks (no direct editor method).
        r.Register("freew.font-dialog",      new ActionRibbonCommand(callbacks.OpenFontDialog));

        // ── Paragraph ────────────────────────────────────────────────────────
        r.Register("freew.bullets",          new ActionRibbonCommand(() => editor.ToggleList(ListKind.Bullet)));
        r.Register("freew.numbering",        new ActionRibbonCommand(() => editor.ToggleList(ListKind.Number)));
        r.Register("freew.align-left",       new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Left)));
        r.Register("freew.align-center",     new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Center)));
        r.Register("freew.align-right",      new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Right)));
        r.Register("freew.align-justify",    new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Justify)));
        r.Register("freew.multilevel-list", new ActionRibbonCommand(() => ApplyMultiLevelList(editor)));
        r.Register("freew.multilevel-demote", new ActionRibbonCommand(() => ChangeListLevel(editor, demote: true)));
        r.Register("freew.multilevel-promote", new ActionRibbonCommand(() => ChangeListLevel(editor, demote: false)));
        r.Register("freew.indent-increase",  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Register("freew.indent-decrease",  new ActionRibbonCommand(editor.DecreaseIndent));
        r.Register("freew.increase-indent",  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Register("freew.decrease-indent",  new ActionRibbonCommand(editor.DecreaseIndent));
        var formattingMarks = new FormattingMarksCommand(editor);
        r.Register("freew.formatting-marks", formattingMarks);
        r.Register("freew.show-hide-para", formattingMarks);
        // Paragraph spacing commands (value = points as an invariant-culture decimal string).
        r.Register("freew.space-before",     new ValueRibbonCommand(value =>
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
                editor.SetSpaceBefore(pt);
        }));
        r.Register("freew.space-after",      new ValueRibbonCommand(value =>
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
                editor.SetSpaceAfter(pt);
        }));
        r.Register("freew.space-before-toggle", new ActionRibbonCommand(() => ToggleSpaceBefore(editor)));
        r.Register("freew.space-after-toggle", new ActionRibbonCommand(() => ToggleSpaceAfter(editor)));
        // Line-spacing commands — value = multiplier for Multiple. The fixed ids are compatibility
        // aliases for older Avalonia controls and are no longer used by the Home ribbon profile.
        r.Register("freew.line-spacing", new ValueRibbonCommand(value => SetLineSpacing(editor, value)));
        r.Register("freew.line-spacing-1",    new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.0)));
        r.Register("freew.line-spacing-115",  new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.15)));
        r.Register("freew.line-spacing-15",   new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.5)));
        r.Register("freew.line-spacing-2",    new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 2.0)));
        // Paragraph dialog launcher.
        r.Register("freew.paragraph-dialog",  new ActionRibbonCommand(callbacks.OpenParagraphDialog));

        // ── Styles (AV-STYLES) ────────────────────────────────────────────────
        // Existing quick-style buttons — now routed through the model-backed, undoable ApplyNamedStyle
        // so the paragraph picks up the real built-in style (seeded if absent) instead of just a font tweak.
        r.Register("freew.style-normal",   new ActionRibbonCommand(() => editor.ApplyNamedStyle("Normal")));
        r.Register("freew.style-heading1", new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading1")));
        r.Register("freew.style-heading2", new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading2")));
        r.Register("freew.style-heading3", new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading3")));
        r.Register("freew.style-title",    new ActionRibbonCommand(() => editor.ApplyNamedStyle("Title")));

        // Styles gallery dropdown — opener no-op; one freew.style.<id> command per built-in style applies
        // that named style (paragraph styles set StyleId; character styles overlay run formatting).
        r.Register("freew.styles-gallery", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterStyleGalleryCommands(r, editor);

        // Clear style — revert the paragraph to the document default (Word's paragraph-level reset).
        r.Register("freew.style-clear", new ActionRibbonCommand(editor.ClearParagraphStyle));

        // ── Editing ──────────────────────────────────────────────────────────
        r.Register("freew.undo",              new ActionRibbonCommand(editor.Undo));
        r.Register("freew.redo",              new ActionRibbonCommand(editor.Redo));
        r.Register("freew.select",            new ActionRibbonCommand(editor.SelectAll));
        r.Register("freew.select-all",        new ActionRibbonCommand(editor.SelectAll));
        r.Register("freew.find",              new ActionRibbonCommand(callbacks.OpenFindReplaceDialog));
        r.Register("freew.replace",           new ActionRibbonCommand(callbacks.OpenFindReplaceDialog));
        r.Register("freew.find-replace-dialog", new ActionRibbonCommand(callbacks.OpenFindReplaceDialog));

        // ── Insert ───────────────────────────────────────────────────────────
        // AV-INSERT: Insert-tab depth. Table dropdown (default + sized presets), page break, picture
        // (file-picker via host callback), shape, text box, and a symbol palette.
        r.Register("freew.insert-table", new ActionRibbonCommand(() => editor.InsertTable(3, 3)));
        // Table size presets (dropdown items). The top-level "freew.table" id opens the menu (no-op).
        r.Register("freew.table", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.table-2x2", new ActionRibbonCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-3x3", new ActionRibbonCommand(() => editor.InsertTable(3, 3)));
        r.Register("freew.table-4x4", new ActionRibbonCommand(() => editor.InsertTable(4, 4)));
        r.Register("freew.table-5x2", new ActionRibbonCommand(() => editor.InsertTable(2, 5)));

        // Page break — empty paragraph forcing a page break before it, after the caret block.
        r.Register("freew.page-break", new ActionRibbonCommand(editor.InsertPageBreak));

        // Picture — open a file picker, load the bytes, insert as an inline image (host callback).
        r.Register("freew.picture", new ActionRibbonCommand(callbacks.InsertPicture));

        // Shape / Text Box — floating drawing objects at the caret.
        r.Register("freew.shape",    new ActionRibbonCommand(editor.InsertShape));
        r.Register("freew.text-box", new ActionRibbonCommand(editor.InsertTextBox));

        // Symbol — palette dropdown; the opener is a no-op and each glyph is its own sub-command.
        r.Register("freew.symbol", new ActionRibbonCommand(() => { /* flyout opener */ }));
        RegisterSymbolPalette(r, editor);

        // Header / Footer — enable the page-margin region (render-ready). Region caret editing deferred.
        r.Register("freew.header", new ActionRibbonCommand(editor.EnsureHeader));
        r.Register("freew.footer", new ActionRibbonCommand(editor.EnsureFooter));

        // ── Insert depth 2 (AV-INSERT2) ──────────────────────────────────────
        RegisterInsertDepth2Commands(r, editor, callbacks);

        // ── Table Design contextual tab ───────────────────────────────────────
        // Table Style Options toggles — DocumentView guards no-op when outside a table.
        r.Register("freew.table-header-row",  new ActionRibbonCommand(editor.ToggleTableHeaderRow));
        r.Register("freew.table-banded-rows", new ActionRibbonCommand(editor.ToggleBandedRows));

        // Table shading: apply a quick neutral fill. Full color picker is deferred.
        r.Register("freew.table-shading", new ActionRibbonCommand(() => editor.SetCellShading("#D9D9D9")));

        // Borders dropdown — opener no-op; sub-commands apply specific edges.
        r.Register("freew.table-borders", new ActionRibbonCommand(() => { /* flyout opener */ }));
        RegisterTableBorderCommands(r, editor);

        // ── Table Layout contextual tab ───────────────────────────────────────
        // Selection helpers.
        r.Register("freew.table-select-table", new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                // BY1: clamp to actual table bounds — passing int.MaxValue triggers an overflow
                // loop in ExpandForMergedCells (r++ overflows int.MaxValue → infinite loop).
                var (lastRow, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, 0, lastRow, lastGridCol);
            }
        }));
        r.Register("freew.table-select-row", new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (_, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, 0, cc.Row, lastGridCol);
            }
        }));
        r.Register("freew.table-select-col", new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (lastRow, _) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, cc.Col, lastRow, cc.Col);
            }
        }));
        r.Register("freew.table-select-cell", new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, cc.Col, cc.Row, cc.Col);
        }));

        // Row / column mutations.
        r.Register("freew.table-insert-above",     new ActionRibbonCommand(editor.InsertTableRowAbove));
        r.Register("freew.table-insert-below",     new ActionRibbonCommand(editor.InsertTableRowBelow));
        r.Register("freew.table-insert-col-left",  new ActionRibbonCommand(editor.InsertTableColumnLeft));
        r.Register("freew.table-insert-col-right", new ActionRibbonCommand(editor.InsertTableColumnRight));
        r.Register("freew.table-delete-row",       new ActionRibbonCommand(editor.DeleteTableRow));
        r.Register("freew.table-delete-col",       new ActionRibbonCommand(editor.DeleteTableColumn));
        r.Register("freew.table-delete",           new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.DeleteTableBlock(cc.TableBlock);
        }));

        // Merge / split.
        r.Register("freew.table-merge-cells", new ActionRibbonCommand(editor.MergeSelectedCells));
        r.Register("freew.table-split-cell",  new ActionRibbonCommand(() => editor.SplitCurrentCell()));

        // Cell alignment — 9 = 3 vertical (Top/Center/Bottom) × 3 horizontal (Left/Center/Right).
        // BY2: parity with WPF's table-layout Alignment group (FreeWRibbon.cs ~1201-1219).
        RegisterCellAlignmentCommands(r, editor);

        // ── Layout / Page Setup (AV-PAGE) ────────────────────────────────────
        // Dialog launcher: opens the Page Setup modal (margins + paper + orientation).
        r.Register("freew.page-setup-dialog",   new ActionRibbonCommand(callbacks.OpenPageSetupDialog));
        // Toggle orientation (portrait ↔ landscape).
        r.Register("freew.page-orientation",    new ActionRibbonCommand(callbacks.ToggleOrientation));
        // Margin presets.
        r.Register("freew.page-margins-normal", new ActionRibbonCommand(() => callbacks.ApplyMarginPreset("normal")));
        r.Register("freew.page-margins-narrow", new ActionRibbonCommand(() => callbacks.ApplyMarginPreset("narrow")));
        r.Register("freew.page-margins-wide",   new ActionRibbonCommand(() => callbacks.ApplyMarginPreset("wide")));
        // Quick paper-size selectors.
        r.Register("freew.page-size-letter",    new ActionRibbonCommand(() => callbacks.ApplyPaperSize("letter")));
        r.Register("freew.page-size-a4",        new ActionRibbonCommand(() => callbacks.ApplyPaperSize("a4")));

        // ── View ─────────────────────────────────────────────────────────────
        r.Register("freew.printlayout",       new ActionRibbonCommand(callbacks.SetPrintLayout));
        r.Register("freew.weblayout",         new ActionRibbonCommand(callbacks.SetWebLayout));
        r.Register("freew.draftview",         new ActionRibbonCommand(callbacks.SetDraftView));
        r.Register("freew.navigationpane",    new ActionRibbonCommand(callbacks.ToggleNavigationPane));
        r.Register("freew.reveal-formatting", new ActionRibbonCommand(callbacks.ToggleRevealFormatting));
        r.Register("freew.zoom-in",           new ActionRibbonCommand(() => callbacks.ApplyZoom(null, +0.1)));
        r.Register("freew.zoom-out",          new ActionRibbonCommand(() => callbacks.ApplyZoom(null, -0.1)));
        r.Register("freew.zoom-100",          new ActionRibbonCommand(() => callbacks.ApplyZoom(1.0, 0)));
        // AV-VIEW: Zoom dialog (presets + custom %) and layout gridlines / ruler toggles.
        // The three Window/Zoom-dialog callbacks are optional on RibbonHostCallbacks (default null so
        // test call sites stay terse); fall back to a safe no-op when the shell didn't supply one.
        r.Register("freew.zoom-dialog",       new ActionRibbonCommand(callbacks.OpenZoomDialog ?? (() => { })));
        r.Register("freew.view-gridlines",    new ActionRibbonCommand(() => editor.ShowGridlines = !editor.ShowGridlines));
        r.Register("freew.view-ruler",        new ActionRibbonCommand(() => editor.ShowRuler = !editor.ShowRuler));
        // AV-VIEW: Window group — new window + split (shell callbacks; may note "deferred" in the status bar).
        r.Register("freew.new-window",        new ActionRibbonCommand(callbacks.NewWindow ?? (() => { })));
        r.Register("freew.split",             new ActionRibbonCommand(callbacks.ToggleSplit ?? (() => { })));

        // ── Review ───────────────────────────────────────────────────────────
        r.Register("freew.reviewingpane", new ActionRibbonCommand(callbacks.ToggleReviewingPane));
        // AV-REVIEW: Track Changes toggle (flag only — keystroke-level recording is deferred; turning the
        // current selection into a tracked change is available via DocumentView.MarkSelectionAsRevision).
        r.Register("freew.track-changes", new ActionRibbonCommand(() => editor.ToggleTrackChanges()));
        // Accept / reject — current revision (at/after caret) and all, undoable + re-render.
        r.Register("freew.accept-change", new ActionRibbonCommand(() => editor.AcceptCurrentRevision()));
        r.Register("freew.reject-change", new ActionRibbonCommand(() => editor.RejectCurrentRevision()));
        r.Register("freew.accept-all",    new ActionRibbonCommand(() => editor.AcceptAllRevisions()));
        r.Register("freew.reject-all",    new ActionRibbonCommand(() => editor.RejectAllRevisions()));
        // Comments — new comment over the selection / delete the comment at the caret.
        r.Register("freew.new-comment",    new ActionRibbonCommand(() => editor.NewComment()));
        r.Register("freew.delete-comment", new ActionRibbonCommand(() => editor.DeleteCommentAtCaret()));
        // Word Count — opens the modal stats dialog (shell callback; reads DocumentStatistics).
        r.Register("freew.word-count", new ActionRibbonCommand(callbacks.OpenWordCountDialog));

        // ── References (AV-REF) ──────────────────────────────────────────────
        RegisterReferencesCommands(r, editor);

        // ── Mailings (AV-MAIL) ───────────────────────────────────────────────
        RegisterMailingsCommands(r, mailMerge);

        // ── Design (AV-DESIGN) ───────────────────────────────────────────────
        RegisterDesignCommands(r, editor, callbacks);

        // ── AV-PICTAB: Picture Format + Drawing Format contextual tabs ────────
        RegisterFloatingFormatCommands(r, editor);

        // ── AV-CHARTTAB: Chart Design/Format + SmartArt Design contextual tabs ─
        RegisterChartSmartArtFormatCommands(r, editor);

        return r;
    }

    private const double ParagraphSpacingTogglePoints = 12.0;

    private static void ApplyMultiLevelList(DocumentView editor)
    {
        if (editor.GetCaretFormatting().Paragraph.ListKind != ListKind.MultiLevel)
            editor.ToggleList(ListKind.MultiLevel);
    }

    private static void ChangeListLevel(DocumentView editor, bool demote)
    {
        if (editor.GetCaretFormatting().Paragraph.ListKind == ListKind.None)
            return;

        if (demote)
            editor.IncreaseIndent();
        else
            editor.DecreaseIndent();
    }

    private static void SetLineSpacing(DocumentView editor, string? value)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var spacing))
            editor.SetLineSpacing(LineSpacingRule.Multiple, spacing);
    }

    private static void ToggleSpaceBefore(DocumentView editor)
    {
        var paragraph = editor.GetCaretFormatting().Paragraph;
        editor.SetSpaceBefore(paragraph.SpaceBeforePt > 0 ? 0 : ParagraphSpacingTogglePoints);
    }

    private static void ToggleSpaceAfter(DocumentView editor)
    {
        var paragraph = editor.GetCaretFormatting().Paragraph;
        editor.SetSpaceAfter(paragraph.SpaceAfterPt > 0 ? 0 : ParagraphSpacingTogglePoints);
    }

    private sealed class FormattingMarksCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ShowParagraphMarks = !editor.ShowParagraphMarks;

        public RibbonCommandState GetState() => new(IsChecked: editor.ShowParagraphMarks);
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
            reg.Register(id, new ActionRibbonCommand(() => ed.SetFontColor(hex)));

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
    internal static string StyleCommandId(string styleId) => FreeWRibbonDefinitionData.StyleCommandId(styleId);

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
            r.Register(StyleCommandId(id), new ActionRibbonCommand(() => editor.ApplyNamedStyle(id)));
        }
    }

    /// <summary>
    /// AV-INSERT: common symbols / special characters for the Insert &gt; Symbol palette. Each entry maps a
    /// stable command-id suffix to the literal character it inserts (via <see cref="DocumentView.InsertSymbol"/>).
    /// The set mirrors Word's default "recently used symbols" grid (currency, typography, math, arrows).
    /// </summary>
    internal static readonly IReadOnlyList<(string Id, string Glyph, string Label)> Symbols =
        FreeWRibbonDefinitionData.Symbols;

    /// <summary>
    /// Registers the per-glyph sub-commands for the Insert &gt; Symbol palette dropdown. Each command id
    /// matches an entry in <see cref="Symbols"/> and inserts that character at the caret as ordinary text.
    /// </summary>
    private static void RegisterSymbolPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        foreach (var (id, glyph, _) in Symbols)
            r.Register(id, new ActionRibbonCommand(() => editor.InsertSymbol(glyph)));
    }

    /// <summary>
    /// AV-INSERT2: Registers the second tier of Insert-tab commands — Hyperlink, Bookmark, Cover Page,
    /// Drop Cap, Quick Parts (document-property fields + snippet), Equation, and Text from File. Each
    /// resolves to a model-backed, undoable <see cref="DocumentView"/> insert method; the dialog-driven
    /// commands (Hyperlink / Bookmark / Quick-Part snippet / Text-from-File) route through the optional
    /// <see cref="RibbonHostCallbacks"/> launchers and safely no-op when the shell did not supply one (so
    /// the registry stays complete and existing test call sites keep compiling).
    /// </summary>
    private static void RegisterInsertDepth2Commands(
        RibbonCommandRegistry r, DocumentView editor, RibbonHostCallbacks callbacks)
    {
        // ── Links ────────────────────────────────────────────────────────────
        // Hyperlink / Bookmark open small dialogs (shell callbacks) that call InsertHyperlink / InsertBookmark.
        r.Register("freew.insert-hyperlink", new ActionRibbonCommand(callbacks.OpenHyperlinkDialog ?? (() => { })));
        r.Register("freew.insert-bookmark",  new ActionRibbonCommand(callbacks.OpenBookmarkDialog ?? (() => { })));

        // ── Cover Page ───────────────────────────────────────────────────────
        // The top-level dropdown opener is a no-op; each preset prepends a cover-page block layout.
        r.Register("freew.cover-page",         new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.cover-page.default", new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Default)));
        r.Register("freew.cover-page.banded",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Banded)));
        r.Register("freew.cover-page.motion",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Motion)));

        // ── Drop Cap ─────────────────────────────────────────────────────────
        // Dropped / In Margin both enlarge the leading letter (the in-margin float geometry is an
        // approximation — render-deferred); None clears the paragraph's run formatting.
        r.Register("freew.drop-cap",           new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.drop-cap.dropped",   new ActionRibbonCommand(() => editor.ApplyDropCap()));
        r.Register("freew.drop-cap.in-margin", new ActionRibbonCommand(() => editor.ApplyDropCap()));
        r.Register("freew.drop-cap.none",      new ActionRibbonCommand(editor.ClearDropCap));

        // ── Quick Parts ──────────────────────────────────────────────────────
        // Document-property / date fields insert directly; the snippet entry opens a dialog (shell callback).
        r.Register("freew.quick-parts",         new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.quick-parts.title",   new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Title)));
        r.Register("freew.quick-parts.author",  new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Author)));
        r.Register("freew.quick-parts.subject", new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Subject)));
        r.Register("freew.quick-parts.date",    new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Date)));
        r.Register("freew.quick-parts.snippet", new ActionRibbonCommand(callbacks.OpenQuickPartDialog ?? (() => { })));

        // ── Equation ─────────────────────────────────────────────────────────
        // The opener no-op; each preset inserts an inline OMML equation (default = E=mc²).
        r.Register("freew.equation",           new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.equation.default",   new ActionRibbonCommand(() => editor.InsertEquation()));
        r.Register("freew.equation.fraction",  new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Fraction("a", "b")]))));
        r.Register("freew.equation.script",    new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.SubSuperscript("x", "n", "2")]))));
        r.Register("freew.equation.radical",   new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Radical("x")]))));
        r.Register("freew.equation.integral",  new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.NAry("∫", "a", "b", "f(x) dx")]))));
        r.Register("freew.equation.summation", new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.NAry("∑", "i=1", "n", "i")]))));

        // ── Text from File ───────────────────────────────────────────────────
        // Opens a file picker (shell callback) and inserts the loaded document's text at the caret.
        r.Register("freew.text-from-file", new ActionRibbonCommand(callbacks.InsertTextFromFile ?? (() => { })));
    }

    /// <summary>
    /// Registers the per-edge sub-commands for the Table Borders dropdown.
    /// Each command calls <see cref="DocumentView.SetCellBorders"/> with the appropriate
    /// <see cref="CellBorderEdges"/> flag. The "No Border" entry clears all edges.
    /// </summary>
    private static void RegisterTableBorderCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, CellBorderEdges edges, bool clear = false) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetCellBorders(edges, clearEdges: clear)));

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
            reg.Register(id, new ActionRibbonCommand(() => ed.SetCaretCellAlignment(vAlign, hAlign)));

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
        r.Register("freew.insert-footnote", new ActionRibbonCommand(() => editor.InsertFootnote()));
        r.Register("freew.insert-endnote",  new ActionRibbonCommand(() => editor.InsertEndnote()));

        // Table of Contents — generate from the heading outline / regenerate in place.
        r.Register("freew.insert-toc", new ActionRibbonCommand(editor.InsertTableOfContents));
        r.Register("freew.update-toc", new ActionRibbonCommand(editor.UpdateTableOfContents));

        // Captions — auto-numbered Figure / Table caption paragraph after the caret block.
        // The top-level opener is a no-op; each label is its own command.
        r.Register("freew.insert-caption",        new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.insert-caption.figure", new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Figure)));
        r.Register("freew.insert-caption.table",  new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Table)));

        // Cross-reference — default to the first heading target (text reference, hyperlinked). A full
        // target-picker dialog is deferred; safely no-ops when the document has no headings.
        r.Register("freew.cross-reference", new ActionRibbonCommand(() => InsertDefaultCrossReference(editor)));

        // Citations & Bibliography — in-text citation for the first source; back-matter bibliography block.
        r.Register("freew.insert-citation", new ActionRibbonCommand(() => InsertDefaultCitation(editor)));
        r.Register("freew.bibliography",    new ActionRibbonCommand(editor.InsertBibliography));
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
    /// <c>SelectedFloatingInfo</c>). Wrap, rotate/flip, z-order, size, and shape/text-box fill/outline
    /// commands are all generated from the shared object-format planner.
    /// </para>
    /// </summary>
    private static void RegisterFloatingFormatCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        foreach (var target in ObjectFormatCommandPlanner.Targets)
        {
            r.Register(
                ObjectFormatCommandPlanner.WrapDropdownCommandId(target),
                new ActionRibbonCommand(() => { /* dropdown opener */ }));
            foreach (var command in ObjectFormatCommandPlanner.WrapCommands(target))
            {
                var wrapping = command.Wrapping;
                r.Register(command.CommandId, new ActionRibbonCommand(() => editor.SetFloatingWrap(wrapping)));
            }

            r.Register(
                ObjectFormatCommandPlanner.TransformDropdownCommandId(target),
                new ActionRibbonCommand(() => { /* dropdown opener */ }));
            foreach (var command in ObjectFormatCommandPlanner.TransformCommands(target))
                r.Register(command.CommandId, new ActionRibbonCommand(() => ExecuteFloatingTransform(editor, command)));

            foreach (var command in ObjectFormatCommandPlanner.ZOrderCommands(target))
            {
                var operation = command.Operation;
                r.Register(command.CommandId, new ActionRibbonCommand(() => editor.ChangeFloatingZOrder(operation)));
            }

            foreach (var command in ObjectFormatCommandPlanner.SizeCommands(target))
            {
                var dimension = command.Dimension;
                r.Register(command.CommandId, new ValueRibbonCommand(value =>
                {
                    if (ObjectFormatCommandPlanner.TryParseSizePoints(value, out var pt))
                        SetFloatingSize(editor, dimension, pt);
                }));
            }
        }

        // Shape Styles fill/outline: top-level opener ids plus menu item commands.
        RegisterShapeFillOutlineCommands(r, editor);
    }

    private static void RegisterShapeFillOutlineCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        r.Register(
            ObjectFormatCommandPlanner.ShapeFillCommandId,
            new ShapeStyleCommand(editor, () => { /* opener command */ }));
        foreach (var command in ObjectFormatCommandPlanner.ShapeFillCommands())
            r.Register(command.CommandId, new ShapeFillCommand(editor, command));

        r.Register(
            ObjectFormatCommandPlanner.ShapeOutlineCommandId,
            new ShapeStyleCommand(editor, () => { /* opener command */ }));
        foreach (var command in ObjectFormatCommandPlanner.ShapeOutlineCommands())
            r.Register(command.CommandId, new ShapeOutlineCommand(editor, command));
    }

    private sealed class ShapeStyleCommand(DocumentView editor, Action execute) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (IsEnabled())
                execute();
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() =>
            ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind);
    }

    private sealed class ShapeFillCommand(DocumentView editor, ObjectFormatShapeFillCommand command) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind))
                return;

            if (command.Kind == ObjectFormatShapeFillKind.NoFill)
            {
                editor.SetSelectedShapeExtendedFill(null);
                editor.SetSelectedShapeFill(null);
            }
            else if (ObjectFormatCommandPlanner.UsesExtendedShapeFill(command.Kind))
            {
                editor.SetSelectedShapeExtendedFill(ObjectFormatCommandPlanner.BuildShapeExtendedFill(command.Kind));
            }
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind));
    }

    private sealed class ShapeOutlineCommand(DocumentView editor, ObjectFormatShapeOutlineCommand command) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var shape = editor.SelectedFloatingShape();
            if (!ObjectFormatCommandPlanner.CanFormatShapeFillOutline(shape?.Kind) || shape is null)
                return;

            var plan = ObjectFormatCommandPlanner.PlanShapeOutline(
                command.Kind,
                shape.OutlineColorHex,
                shape.OutlineWidthPt);
            editor.SetSelectedShapeOutline(plan.ColorHex, plan.WidthPt, plan.Dash);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind));
    }

    private static void ExecuteFloatingTransform(DocumentView editor, ObjectFormatTransformCommand command)
    {
        switch (command.Kind)
        {
            case ObjectFormatTransformKind.Rotate:
                editor.RotateSelectedFloating(command.RotationDeltaDegrees);
                break;
            case ObjectFormatTransformKind.FlipHorizontal:
                editor.FlipSelectedFloating(horizontal: true);
                break;
            case ObjectFormatTransformKind.FlipVertical:
                editor.FlipSelectedFloating(horizontal: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private static void SetFloatingSize(
        DocumentView editor,
        ObjectFormatSizeDimension dimension,
        double points)
    {
        switch (dimension)
        {
            case ObjectFormatSizeDimension.Width:
                editor.SetFloatingWidth(points);
                break;
            case ObjectFormatSizeDimension.Height:
                editor.SetFloatingHeight(points);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null);
        }
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
        r.Register("freew.chart-type", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
        {
            var k = kind; // capture
            r.Register($"freew.chart-type-{k.ToString().ToLowerInvariant()}",
                new ActionRibbonCommand(() => editor.SetChartType(k)));
        }

        // Chart Styles — dropdown opener + one command per catalog style.
        r.Register("freew.chart-style", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var style in ChartStyle.Catalog)
        {
            var s = style;
            r.Register($"freew.chart-style-{s.Id}", new ActionRibbonCommand(() => editor.SetChartStyle(s.Id)));
        }

        // Change Colors — dropdown opener + one command per catalog colour scheme.
        r.Register("freew.chart-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.chart-colors-{sc.Id}", new ActionRibbonCommand(() => editor.SetChartColorScheme(sc.Id)));
        }

        // ── SmartArt Design ───────────────────────────────────────────────────
        // Layouts — the four Word families. Cycle maps to the model's Process kind (closest flat sequence).
        r.Register("freew.smartart-layout", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.smartart-layout-list",      new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.List)));
        r.Register("freew.smartart-layout-process",   new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-cycle",     new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-hierarchy", new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Hierarchy)));

        // Change Colors — reuse the chart colour-scheme catalog ids.
        r.Register("freew.smartart-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.smartart-colors-{sc.Id}", new ActionRibbonCommand(() => editor.SetSmartArtColor(sc.Id)));
        }
    }

    /// <summary>
    /// AV-MAIL: Registers the Mailings-tab commands over the portable <see cref="MailMerge"/> engine. The
    /// in-scope subset is: Select Recipients (load a CSV recipient list), Insert Merge Field (insert a
    /// «Field» placeholder at the caret), Address Block / Greeting Line (insert the composite placeholders),
    /// Preview Results (toggle a live preview of record 1) with Next / Previous record stepping, and
    /// Finish &amp; Merge (merge to a new in-memory document). Mail-SEND (e-mail merge) is OUT OF SCOPE and
    /// intentionally not wired.
    ///
    /// <para>
    /// A single <see cref="MailMergeSession"/> is captured by every command (so they share the loaded data,
    /// mapping and preview cursor). Commands that mutate the document (merge-field / address-block /
    /// greeting-line insertion) go through the editor's undoable <see cref="DocumentView.InsertText"/>; the
    /// preview / finish commands swap the whole document via <see cref="DocumentView.LoadDocument"/>.
    /// </para>
    ///
    /// <para>
    /// The two dialog-driven entry points (recipient CSV + field-name picker) are supplied as <b>optional</b>
    /// host callbacks (<see cref="RibbonHostCallbacks.AskRecipientCsv"/> /
    /// <see cref="RibbonHostCallbacks.AskMergeFieldName"/>); when the shell didn't supply them (tests,
    /// parallel waves) those two commands degrade to safe no-ops while the rest of the tab stays usable
    /// (a recipient list can also be loaded directly via <see cref="MailMergeEngine.LoadRecipientsCsv"/>).
    /// </para>
    /// </summary>
    private static void RegisterMailingsCommands(RibbonCommandRegistry r, MailMergeEngine engine)
    {
        r.Register("freew.select-recipients", new ActionRibbonCommand(engine.SelectRecipients));
        r.Register("freew.merge-field",       new ActionRibbonCommand(engine.InsertMergeField));
        r.Register("freew.address-block",     new ActionRibbonCommand(engine.InsertAddressBlock));
        r.Register("freew.greeting-line",     new ActionRibbonCommand(engine.InsertGreetingLine));
        r.Register("freew.preview-results",   new ActionRibbonCommand(engine.TogglePreview));
        r.Register("freew.next-record",       new ActionRibbonCommand(engine.NextRecord));
        r.Register("freew.prev-record",       new ActionRibbonCommand(engine.PreviousRecord));
        r.Register("freew.finish-merge",      new ActionRibbonCommand(() => engine.FinishMerge()));
    }

    /// <summary>
    /// AV-DESIGN: Registers the Design-tab commands — Themes / Colors / Fonts / Paragraph-Spacing galleries
    /// (document-wide style mutations), Page Color, Page Borders, and Watermark. Each gallery dropdown's
    /// top-level id is a no-op opener; the per-item ids resolve to a model-backed, undoable
    /// <see cref="DocumentView"/> Design method. Page Borders + Custom Watermark route through the optional
    /// <see cref="RibbonHostCallbacks"/> dialog launchers and safely no-op when the shell did not supply one
    /// (so the registry-completeness guard passes and parallel waves / tests keep compiling).
    /// </summary>
    private static void RegisterDesignCommands(
        RibbonCommandRegistry r, DocumentView editor, RibbonHostCallbacks callbacks)
    {
        // ── Themes ───────────────────────────────────────────────────────────
        r.Register("freew.theme", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyTheme(t)));
        }

        // ── Colors (palette only — preserves fonts) ──────────────────────────
        r.Register("freew.theme-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme-colors.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyThemeColors(t)));
        }

        // ── Fonts (heading/body pairing — preserves colours) ─────────────────
        r.Register("freew.theme-fonts", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var fontSet in DocumentFontSet.Catalog)
        {
            var f = fontSet;
            r.Register($"freew.theme-fonts.{f.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyDocumentFontSet(f)));
        }

        // ── Paragraph Spacing presets ────────────────────────────────────────
        r.Register("freew.para-spacing", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var spacingSet in DocumentParagraphSpacingSet.Catalog)
        {
            var s = spacingSet;
            r.Register($"freew.para-spacing.{FreeWRibbon.ParaSpacingId(s.Name)}",
                new ActionRibbonCommand(() => editor.ApplyParagraphSpacingSet(s)));
        }

        // ── Page Color swatches (+ No Color) ─────────────────────────────────
        r.Register("freew.page-color", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterPageColorPalette(r, editor);

        // ── Page Borders — dialog launcher (optional callback) ───────────────
        r.Register("freew.page-borders", new ActionRibbonCommand(callbacks.OpenPageBordersDialog ?? (() => { })));

        // ── Watermark — built-in presets + Custom (dialog) + Remove ──────────
        r.Register("freew.watermark", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.watermark.confidential", new ActionRibbonCommand(() => editor.SetWatermarkText("CONFIDENTIAL")));
        r.Register("freew.watermark.do-not-copy",  new ActionRibbonCommand(() => editor.SetWatermarkText("DO NOT COPY")));
        r.Register("freew.watermark.draft",        new ActionRibbonCommand(() => editor.SetWatermarkText("DRAFT")));
        r.Register("freew.watermark.urgent",       new ActionRibbonCommand(() => editor.SetWatermarkText("URGENT")));
        r.Register("freew.watermark.custom",       new ActionRibbonCommand(callbacks.OpenWatermarkDialog ?? (() => { })));
        r.Register("freew.watermark.none",         new ActionRibbonCommand(() => editor.SetWatermark(null)));
    }

    /// <summary>
    /// AV-DESIGN: Registers the per-swatch sub-commands for the Page Color palette. Each id matches an entry
    /// in <see cref="FreeWRibbon.PageColors"/> and calls <see cref="DocumentView.SetPageColor"/> with the
    /// swatch hex (or null for "No Color", which clears the background back to white).
    /// </summary>
    private static void RegisterPageColorPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetPageColor(hex)));

        Add(r, editor, "freew.page-color.none",         null);
        Add(r, editor, "freew.page-color.white",        "#FFFFFF");
        Add(r, editor, "freew.page-color.light-gray",   "#D9D9D9");
        Add(r, editor, "freew.page-color.tan",          "#EAD9C0");
        Add(r, editor, "freew.page-color.light-blue",   "#DDEBF7");
        Add(r, editor, "freew.page-color.light-green",  "#E2EFDA");
        Add(r, editor, "freew.page-color.light-yellow", "#FFF2CC");
        Add(r, editor, "freew.page-color.rose",         "#FCE4EC");
    }
}
