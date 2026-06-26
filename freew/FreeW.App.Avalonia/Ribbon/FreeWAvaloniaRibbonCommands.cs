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

        // ── Styles ───────────────────────────────────────────────────────────
        r.Register("freew.style-normal",   new RelayCommand(() => editor.ApplyQuickStyle(11, bold: false)));
        r.Register("freew.style-heading1", new RelayCommand(() => editor.ApplyQuickStyle(16, bold: true)));
        r.Register("freew.style-heading2", new RelayCommand(() => editor.ApplyQuickStyle(14, bold: true)));
        r.Register("freew.style-heading3", new RelayCommand(() => editor.ApplyQuickStyle(12, bold: true)));
        r.Register("freew.style-title",    new RelayCommand(() => editor.ApplyQuickStyle(24, bold: true)));

        // ── Editing ──────────────────────────────────────────────────────────
        r.Register("freew.undo",              new RelayCommand(editor.Undo));
        r.Register("freew.redo",              new RelayCommand(editor.Redo));
        r.Register("freew.select-all",        new RelayCommand(editor.SelectAll));
        r.Register("freew.find-replace-dialog", new RelayCommand(callbacks.OpenFindReplaceDialog));

        // ── Insert ───────────────────────────────────────────────────────────
        r.Register("freew.insert-table", new RelayCommand(() => editor.InsertTable(3, 3)));

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
                editor.SetCellBlockSelection(cc.TableBlock, 0, 0, int.MaxValue, int.MaxValue);
        }));
        r.Register("freew.table-select-row", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, 0, cc.Row, int.MaxValue);
        }));
        r.Register("freew.table-select-col", new RelayCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.SetCellBlockSelection(cc.TableBlock, 0, cc.Col, int.MaxValue, cc.Col);
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

        // ── View ─────────────────────────────────────────────────────────────
        r.Register("freew.printlayout",       new RelayCommand(callbacks.SetPrintLayout));
        r.Register("freew.weblayout",         new RelayCommand(callbacks.SetWebLayout));
        r.Register("freew.draftview",         new RelayCommand(callbacks.SetDraftView));
        r.Register("freew.navigationpane",    new RelayCommand(callbacks.ToggleNavigationPane));
        r.Register("freew.reveal-formatting", new RelayCommand(callbacks.ToggleRevealFormatting));
        r.Register("freew.zoom-in",           new RelayCommand(() => callbacks.ApplyZoom(null, +0.1)));
        r.Register("freew.zoom-out",          new RelayCommand(() => callbacks.ApplyZoom(null, -0.1)));
        r.Register("freew.zoom-100",          new RelayCommand(() => callbacks.ApplyZoom(1.0, 0)));

        // ── Review ───────────────────────────────────────────────────────────
        r.Register("freew.reviewingpane", new RelayCommand(callbacks.ToggleReviewingPane));

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
}
