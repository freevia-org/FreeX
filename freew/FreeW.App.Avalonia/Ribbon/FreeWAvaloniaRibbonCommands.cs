using System.Globalization;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
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
    private static IRibbonCommand HostCommand(Action? action) =>
        action is null ? UnavailableRibbonCommand.Instance : new ActionRibbonCommand(action);

    private sealed class UnavailableRibbonCommand : IRibbonStatefulCommand
    {
        public static readonly UnavailableRibbonCommand Instance = new();

        private UnavailableRibbonCommand()
        {
        }

        public RibbonCommandState GetState() => new(IsEnabled: false);

        public void Execute(RibbonCommandContext context) =>
            throw new InvalidOperationException("An unavailable ribbon command cannot be executed.");
    }

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
        r.Register("freew.import-pdf-text", new ActionRibbonCommand(callbacks.ImportPdfText ?? (() => { })));
        r.Register("freew.save",      new ActionRibbonCommand(callbacks.Save));

        r.Register("freew.read-mode", callbacks.ToggleReadMode is { } toggle && callbacks.IsReadModeActive is { } isActive
            ? new ToggleActionCommand(toggle, isActive)
            : HostCommand(null));
        RegisterReadModeChoice(r, "freew.read-mode-column-narrow", FreeWReadModePlanner.NarrowColumn, callbacks.ApplyReadModeColumnWidth);
        RegisterReadModeChoice(r, "freew.read-mode-column-default", FreeWReadModePlanner.DefaultColumn, callbacks.ApplyReadModeColumnWidth);
        RegisterReadModeChoice(r, "freew.read-mode-column-wide", FreeWReadModePlanner.WideColumn, callbacks.ApplyReadModeColumnWidth);
        RegisterReadModeChoice(r, "freew.read-mode-color-none", FreeWReadModePlanner.NoColor, callbacks.ApplyReadModePageColor);
        RegisterReadModeChoice(r, "freew.read-mode-color-sepia", FreeWReadModePlanner.SepiaColor, callbacks.ApplyReadModePageColor);
        RegisterReadModeChoice(r, "freew.read-mode-color-inverse", FreeWReadModePlanner.InverseColor, callbacks.ApplyReadModePageColor);

        // ── Clipboard ────────────────────────────────────────────────────────
        r.Register("freew.cut",   new ActionRibbonCommand(callbacks.Cut));
        r.Register("freew.copy",  new ActionRibbonCommand(callbacks.Copy));
        r.Register("freew.paste", new ActionRibbonCommand(callbacks.Paste));
        r.Register("freew.paste-plain", new ActionRibbonCommand(callbacks.PastePlainText ?? (() => { })));
        r.Register("freew.paste-merge", new ActionRibbonCommand(callbacks.PasteMergeFormatting ?? (() => { })));
        r.Register("freew.paste-special", new ActionRibbonCommand(callbacks.OpenPasteSpecial ?? (() => { })));
        r.Register("freew.format-painter", new FormatPainterCommand(editor));

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
        r.Register("freew.smallcaps",        new ActionRibbonCommand(editor.ToggleSmallCaps));
        r.Register("freew.allcaps",          new ActionRibbonCommand(editor.ToggleAllCaps));
        r.Register("freew.superscript",      new ActionRibbonCommand(editor.ToggleSuperscript));
        r.Register("freew.subscript",        new ActionRibbonCommand(editor.ToggleSubscript));
        r.Register("freew.highlight",        new ValueRibbonCommand(value => editor.SetHighlightColor(value)));
        r.Register("freew.char-border",      new ActionRibbonCommand(callbacks.OpenCharacterBorderDialog ?? (() => { })));
        r.Register("freew.char-shading",     new ActionRibbonCommand(callbacks.OpenCharacterShadingDialog ?? (() => { })));
        RegisterHighlightPalette(r, editor);
        RegisterCharacterBorderPalette(r, editor);
        RegisterCharacterShadingPalette(r, editor);
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
        r.Register("freew.multilevel-preset-0", new ActionRibbonCommand(() =>
            ApplyMultiLevelPreset(editor, MultiLevelListFormat.DecimalNumberFormats)));
        r.Register("freew.multilevel-preset-1", new ActionRibbonCommand(() =>
            ApplyMultiLevelPreset(editor, MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats)));
        r.Register("freew.multilevel-preset-2", new ActionRibbonCommand(() =>
        {
            editor.ApplyMultiLevelHeadingPreset();
            editor.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalNumberFormats);
        }));
        r.Register("freew.multilevel-define", new ActionRibbonCommand(
            callbacks.OpenMultilevelListDialog ?? (() =>
            {
                editor.ApplyMultiLevelListToSelection();
                editor.ApplyMultiLevelListStartOverrides(level0StartAt: 1, level1StartAt: 1);
            })));
        r.Register("freew.indent-increase",  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Register("freew.indent-decrease",  new ActionRibbonCommand(editor.DecreaseIndent));
        r.Register("freew.increase-indent",  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Register("freew.decrease-indent",  new ActionRibbonCommand(editor.DecreaseIndent));
        r.Register("freew.indent-left", new ParagraphValueCommand(
            editor,
            pt => editor.SetIndents(leftPt: pt),
            paragraph => paragraph.IndentLeftPt));
        r.Register("freew.indent-right", new ParagraphValueCommand(
            editor,
            pt => editor.SetIndents(rightPt: pt),
            paragraph => paragraph.IndentRightPt));
        var formattingMarks = new FormattingMarksCommand(editor);
        r.Register("freew.formatting-marks", formattingMarks);
        r.Register("freew.show-hide-para", formattingMarks);
        // Paragraph spacing commands (value = points as an invariant-culture decimal string).
        r.Register("freew.space-before", new ParagraphValueCommand(
            editor,
            editor.SetSpaceBefore,
            paragraph => paragraph.SpaceBeforePt));
        r.Register("freew.space-after", new ParagraphValueCommand(
            editor,
            editor.SetSpaceAfter,
            paragraph => paragraph.SpaceAfterPt));
        r.Register("freew.space-before-toggle", new ActionRibbonCommand(() => ToggleSpaceBefore(editor)));
        r.Register("freew.space-after-toggle", new ActionRibbonCommand(() => ToggleSpaceAfter(editor)));
        r.Register("freew.keep-with-next", new ActionRibbonCommand(editor.ToggleKeepWithNext));
        r.Register("freew.keep-lines", new ActionRibbonCommand(editor.ToggleKeepLinesTogether));
        r.Register("freew.widow-control", new ActionRibbonCommand(editor.ToggleWidowControl));
        r.Register("freew.para-border", new ActionRibbonCommand(() => editor.ToggleParagraphBorder()));
        r.Register("freew.para-shading", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterParagraphShadingPalette(r, editor);
        r.Register("freew.borders-shading", new ActionRibbonCommand(callbacks.OpenBordersAndShadingDialog ?? (() => { })));
        r.Register("freew.tabs-dialog", new ActionRibbonCommand(callbacks.OpenTabsDialog ?? (() => { })));
        r.Register("freew.sort", new ActionRibbonCommand(() => ExecuteSortCommand(editor, callbacks)));
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
        r.Register("freew.new-style", new ActionRibbonCommand(callbacks.OpenNewStyleDialog ?? (() => { })));
        r.Register("freew.manage-styles", new ActionRibbonCommand(callbacks.OpenManageStylesDialog ?? (() => { })));

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
        // Match WPF's primary face: clicking the Table dropdown button inserts a 2x2 table;
        // clicking its arrow still exposes the sized presets below.
        r.Register("freew.table", new ActionRibbonCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-2x2", new ActionRibbonCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-3x3", new ActionRibbonCommand(() => editor.InsertTable(3, 3)));
        r.Register("freew.table-4x4", new ActionRibbonCommand(() => editor.InsertTable(4, 4)));
        r.Register("freew.table-5x2", new ActionRibbonCommand(() => editor.InsertTable(2, 5)));

        // Page break — empty paragraph forcing a page break before it, after the caret block.
        r.Register("freew.page-break", new ActionRibbonCommand(editor.InsertPageBreak));
        r.Register("freew.blank-page", new ActionRibbonCommand(editor.InsertBlankPage));
        r.Register("freew.horizontal-rule", new ActionRibbonCommand(editor.InsertHorizontalRule));

        // Picture — open a file picker, load the bytes, insert as an inline image (host callback).
        r.Register("freew.picture", new ActionRibbonCommand(callbacks.InsertPicture));

        // Shape / Text Box — floating drawing objects at the caret.
        r.Register("freew.shape",    new ActionRibbonCommand(editor.InsertShape));
        r.Register("freew.text-box", new ActionRibbonCommand(editor.InsertTextBox));

        r.Register("freew.symbol", HostCommand(callbacks.OpenSymbolPickerDialog));
        RegisterSymbolPalette(r, editor);
        r.Register("freew.screenshot", HostCommand(callbacks.CaptureScreenClip));
        r.Register("freew.screen-clipping", HostCommand(callbacks.CaptureScreenClip));

        // Header / Footer — match WPF's text prompt when the shell supplies it. The fallback keeps
        // headless registry callers deterministic and retains the old region-creation behavior.
        r.Register("freew.header", HeaderFooterTextCommand(editor, callbacks, footer: false));
        r.Register("freew.footer", HeaderFooterTextCommand(editor, callbacks, footer: true));
        r.Register("freew.page-number", new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Register("freew.page-number-top", new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        r.Register("freew.page-number-bottom", new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Register("freew.page-number-current", new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.PageNumber)));
        r.Register("freew.page-number-format", new ContextRibbonCommand(
            context => ExecutePageNumberFormat(editor, callbacks, context)));
        r.Register("freew.datetime", new ActionRibbonCommand(
            callbacks.OpenDateTimeDialog ?? (() => editor.InsertField(RunFieldKind.Date))));
        r.Register("freew.field", new ActionRibbonCommand(callbacks.OpenFieldDialog ?? (() => { })));
        r.Register("freew.save-quickpart", new ActionRibbonCommand(callbacks.SaveQuickPartSelection ?? (() => { })));
        r.Register("freew.building-blocks-organizer", new ActionRibbonCommand(callbacks.OpenBuildingBlocksOrganizer ?? (() => { })));
        RegisterHeaderFooterCommands(r, editor);

        // ── Insert depth 2 (AV-INSERT2) ──────────────────────────────────────
        RegisterInsertDepth2Commands(r, editor, callbacks);

        // ── Developer ────────────────────────────────────────────────────────
        RegisterDeveloperControls(r, editor);

        // ── Table Design contextual tab ───────────────────────────────────────
        // Table Style Options toggles — DocumentView guards no-op when outside a table.
        r.Register("freew.table-header-row",  new ActionRibbonCommand(editor.ToggleTableHeaderRow));
        r.Register("freew.table-banded-rows", new ActionRibbonCommand(editor.ToggleBandedRows));
        r.Register("freew.table-last-row", new ActionRibbonCommand(editor.ToggleTableLastRow));
        r.Register("freew.table-first-column", new ActionRibbonCommand(editor.ToggleTableFirstColumn));
        r.Register("freew.table-last-column", new ActionRibbonCommand(editor.ToggleTableLastColumn));
        r.Register("freew.table-banded-cols", new ActionRibbonCommand(editor.ToggleTableBandedColumns));

        // Table shading: open the WPF-parity palette; the shell applies the chosen result only after
        // the user accepts a swatch or No Color. Closing the picker is a no-op.
        r.Register("freew.table-shading", new ActionRibbonCommand(callbacks.OpenCellShadingDialog ?? (() => { })));
        r.Register("freew.table-styles", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
        {
            var style = DocumentTableStyle.Catalog[index];
            r.Register(FreeWContextMenuPlanner.TableStylesPrefix + index,
                new ActionRibbonCommand(() => editor.ApplyTableStyle(style)));
        }

        // Borders dropdown — opener no-op; sub-commands apply specific edges.
        r.Register("freew.table-borders", new ActionRibbonCommand(() => { /* flyout opener */ }));
        RegisterTableBorderCommands(r, editor);
        r.Register("freew.draw-table", new ActionRibbonCommand(callbacks.OpenDrawTableDialog ?? (() => { })));
        r.Register("freew.eraser", new ActionRibbonCommand(editor.EraseTableBorderAtCaret));

        // ── Table Layout contextual tab ───────────────────────────────────────
        // Selection helpers.
        r.Register("freew.table-view-gridlines", new ActionRibbonCommand(() =>
        {
            editor.ViewTableGridlines = !editor.ViewTableGridlines;
        }));
        IRibbonCommand tablePropertiesCommand = callbacks.OpenTablePropertiesDialog is { } openTableProperties
            ? new TablePropertiesCommand(editor, openTableProperties)
            : UnavailableRibbonCommand.Instance;
        r.Register("freew.table-properties", tablePropertiesCommand);
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
        r.Register("freew.split-table", new ActionRibbonCommand(editor.SplitTable));

        // Cell size.
        r.Register("freew.table-row-height", tablePropertiesCommand);
        r.Register("freew.table-col-width", tablePropertiesCommand);
        r.Register("freew.table-distribute-rows", new ActionRibbonCommand(editor.DistributeTableRows));
        r.Register("freew.table-distribute-cols", new ActionRibbonCommand(editor.DistributeTableColumns));
        r.Register("freew.table-autofit-contents", new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Contents)));
        r.Register("freew.table-autofit-window", new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Window)));
        r.Register("freew.table-autofit-fixed", new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Fixed)));

        // Cell alignment — 9 = 3 vertical (Top/Center/Bottom) × 3 horizontal (Left/Center/Right).
        // BY2: parity with WPF's table-layout Alignment group (FreeWRibbon.cs ~1201-1219).
        RegisterCellAlignmentCommands(r, editor);
        r.Register("freew.table-cell-margins", tablePropertiesCommand);
        r.Register("freew.cell-text-direction-horizontal", new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Horizontal)));
        r.Register("freew.cell-text-direction-rotate90", new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Rotate90)));
        r.Register("freew.cell-text-direction-rotate270", new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Rotate270)));

        // Data.
        r.Register("freew.table-repeat-header", new ActionRibbonCommand(editor.ToggleTableRepeatHeaderRow));
        r.Register("freew.table-formula", callbacks.OpenTableFormulaDialog is { } openTableFormula
            ? new TableFormulaCommand(editor, openTableFormula)
            : UnavailableRibbonCommand.Instance);
        r.Register("freew.table-to-text", new TableToTextCommand(editor, callbacks));

        // ── Layout / Page Setup (AV-PAGE) ────────────────────────────────────
        // Dialog launcher: opens the Page Setup modal (margins + paper + orientation).
        var pageSetupCommand = new ActionRibbonCommand(callbacks.OpenPageSetupDialog);
        r.Register("freew.page-setup", pageSetupCommand);
        r.Register("freew.custom-margins", new ActionRibbonCommand(callbacks.OpenCustomMarginsDialog ?? callbacks.OpenPageSetupDialog));
        r.Register("freew.more-paper-sizes", new ActionRibbonCommand(callbacks.OpenMorePaperSizesDialog ?? callbacks.OpenPageSetupDialog));
        r.Register("freew.page-setup-dialog", pageSetupCommand);
        // Toggle orientation (portrait ↔ landscape).
        var orientationCommand = new HostPageSettingCommand(editor, callbacks.ToggleOrientation);
        r.Register("freew.orientation", orientationCommand);
        r.Register("freew.page-orientation", orientationCommand);
        // Margin presets.
        r.Register("freew.margins", new HostPageSettingCommand(editor, () => ToggleNormalNarrowMargins(editor, callbacks)));
        r.Register("freew.page-margins-normal", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("normal")));
        r.Register("freew.page-margins-narrow", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("narrow")));
        r.Register("freew.page-margins-wide", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("wide")));
        // Quick paper-size selectors.
        r.Register("freew.size", new HostPageSettingCommand(editor, () => ToggleLetterA4Paper(editor, callbacks)));
        r.Register("freew.page-size-letter", new HostPageSettingCommand(editor, () => callbacks.ApplyPaperSize("letter")));
        r.Register("freew.page-size-a4", new HostPageSettingCommand(editor, () => callbacks.ApplyPaperSize("a4")));

        var columnsDialogCommand = new ActionRibbonCommand(callbacks.OpenColumnsDialog ?? (() => { }));
        r.Register("freew.columns", columnsDialogCommand);
        r.Register("freew.columns-more", columnsDialogCommand);
        r.Register("freew.columns-one", new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.One),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.One)));
        r.Register("freew.columns-two", new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Two),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Two)));
        r.Register("freew.columns-three", new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Three),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Three)));
        r.Register("freew.columns-left", new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Left),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Left)));
        r.Register("freew.columns-right", new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Right),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Right)));

        r.Register("freew.breaks", EmptyRibbonCommand.Instance);
        r.Register("freew.column-break", new ActionRibbonCommand(editor.InsertColumnBreak));
        r.Register("freew.section-break-next-page", new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.NextPage)));
        r.Register("freew.section-break-continuous", new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.Continuous)));
        r.Register("freew.section-break-even-page", new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.EvenPage)));
        r.Register("freew.section-break-odd-page", new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.OddPage)));
        r.Register("freew.line-numbers", new PageSettingCommand(editor, PageLayoutCommandPlanner.CycleLineNumberMode));
        r.Register("freew.line-numbers-none", new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.None, page => page.LineNumberMode == LineNumberMode.None));
        r.Register("freew.line-numbers-continuous", new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.Continuous, page => page.LineNumberMode == LineNumberMode.Continuous));
        r.Register("freew.line-numbers-restart-page", new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.RestartEachPage, page => page.LineNumberMode == LineNumberMode.RestartEachPage));
        r.Register("freew.line-numbers-restart-section", new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.RestartEachSection, page => page.LineNumberMode == LineNumberMode.RestartEachSection));
        r.Register("freew.line-numbers-options", new ActionRibbonCommand(callbacks.OpenLineNumberOptionsDialog ?? (() => { })));
        r.Register("freew.hyphenation", new PageSettingCommand(editor, PageLayoutCommandPlanner.ToggleHyphenation, page => page.AutoHyphenation));
        r.Register("freew.hyphenation-none", new PageSettingCommand(editor, page => page.AutoHyphenation = false, page => !page.AutoHyphenation));
        r.Register("freew.hyphenation-auto", new PageSettingCommand(editor, page => page.AutoHyphenation = true, page => page.AutoHyphenation));
        r.Register("freew.hyphenation-manual", new ActionRibbonCommand(callbacks.OpenManualHyphenationDialog ?? (() => { })));
        r.Register("freew.hyphenation-options", new ActionRibbonCommand(callbacks.OpenHyphenationOptionsDialog ?? (() => { })));
        r.Register("freew.different-first-page", new PageSettingCommand(editor, page => page.DifferentFirstPage = !page.DifferentFirstPage, page => page.DifferentFirstPage));
        r.Register("freew.page-valign", new ActionRibbonCommand(editor.CyclePageVerticalAlignment));
        r.Register("freew.text-to-table", new ActionRibbonCommand(
            callbacks.OpenTextToTableDialog ?? editor.ConvertCurrentParagraphToTable));

        // ── View ─────────────────────────────────────────────────────────────
        var printPreviewCommand = new ActionRibbonCommand(callbacks.OpenPrintPreview ?? (() => { }));
        r.Register("freew.print-preview", printPreviewCommand);

        var printLayoutCommand = new ToggleActionCommand(
            callbacks.SetPrintLayout,
            callbacks.IsPrintLayoutActive ?? (() => editor.ViewMode == DocumentViewMode.PrintLayout));
        var webLayoutCommand = new ToggleActionCommand(
            callbacks.SetWebLayout,
            callbacks.IsWebLayoutActive ?? (() => editor.ViewMode == DocumentViewMode.WebLayout));
        var draftViewCommand = new ToggleActionCommand(
            callbacks.SetDraftView,
            callbacks.IsDraftViewActive ?? (() => editor.ViewMode == DocumentViewMode.Draft));
        // Outline is a distinct host surface. A host that does not provide it must not silently
        // route the command to Draft; production MainWindow supplies the real toggle and state query.
        var outlineViewCommand = new ToggleActionCommand(
            callbacks.SetOutlineView ?? (() => { }),
            callbacks.IsOutlineViewActive ?? (() => false));
        var pagedEditViewCommand = new ToggleActionCommand(
            callbacks.TogglePagedEditView ?? callbacks.SetPrintLayout,
            callbacks.IsPagedEditViewActive ?? (() => false));
        r.Register("freew.print-layout", printLayoutCommand);
        r.Register("freew.web-layout", webLayoutCommand);
        r.Register("freew.draft-view", draftViewCommand);
        r.Register("freew.outline-view", outlineViewCommand);
        r.Register("freew.paged-edit-view", pagedEditViewCommand);
        // Compatibility aliases for older Avalonia definitions/tests that used compact ids.
        r.Register("freew.printlayout", printLayoutCommand);
        r.Register("freew.weblayout", webLayoutCommand);
        r.Register("freew.draftview", draftViewCommand);
        var navigationPaneCommand = new ToggleActionCommand(
            callbacks.ToggleNavigationPane,
            callbacks.IsNavigationPaneVisible ?? (() => false));
        r.Register("freew.nav-pane",          navigationPaneCommand);
        r.Register("freew.navigationpane",    navigationPaneCommand);
        r.Register("freew.reveal-formatting", new ToggleActionCommand(
            callbacks.ToggleRevealFormatting,
            callbacks.IsRevealFormattingVisible ?? (() => false)));
        r.Register("freew.zoom-in",           new ActionRibbonCommand(() => callbacks.ApplyZoom(null, +0.1)));
        r.Register("freew.zoom-out",          new ActionRibbonCommand(() => callbacks.ApplyZoom(null, -0.1)));
        r.Register("freew.zoom-100",          new ActionRibbonCommand(() => callbacks.ApplyZoom(1.0, 0)));
        r.Register("freew.zoom-one-page",     new ActionRibbonCommand(callbacks.ZoomOnePage ?? (() => { })));
        r.Register("freew.zoom-page-width",   new ActionRibbonCommand(callbacks.ZoomPageWidth ?? (() => { })));
        r.Register("freew.zoom-multiple-pages",
            new ToggleActionCommand(callbacks.ToggleMultiplePages ?? (() => { }), callbacks.IsMultiplePagesActive ?? (() => false)));
        r.Register("freew.zoom-side-to-side",
            new ToggleActionCommand(callbacks.ToggleSideToSide ?? (() => { }), callbacks.IsSideToSideActive ?? (() => false)));
        // AV-VIEW: Zoom dialog (presets + custom %) and layout gridlines / ruler toggles.
        // The three Window/Zoom-dialog callbacks are optional on RibbonHostCallbacks (default null so
        // test call sites stay terse); fall back to a safe no-op when the shell didn't supply one.
        r.Register("freew.zoom-dialog",       new ActionRibbonCommand(callbacks.OpenZoomDialog ?? (() => { })));
        var gridlinesCommand = new ToggleActionCommand(
            () => editor.ShowGridlines = !editor.ShowGridlines,
            () => editor.ShowGridlines);
        var rulerCommand = new ToggleActionCommand(
            () => editor.ShowRuler = !editor.ShowRuler,
            () => editor.ShowRuler);
        r.Register("freew.gridlines",         gridlinesCommand);
        r.Register("freew.view-gridlines",    gridlinesCommand);
        r.Register("freew.ruler",             rulerCommand);
        r.Register("freew.view-ruler",        rulerCommand);
        // AV-VIEW: Window group — new window, Arrange All, and split.
        r.Register("freew.new-window",        new ActionRibbonCommand(callbacks.NewWindow ?? (() => { })));
        r.Register("freew.arrange-all",       new ActionRibbonCommand(callbacks.ArrangeAll ?? (() => { })));
        var splitCommand = new ToggleActionCommand(callbacks.ToggleSplit ?? (() => { }), callbacks.IsSplitActive ?? (() => false));
        r.Register("freew.split",             splitCommand);
        r.Register("freew.split-window",      splitCommand);

        // ── Review ───────────────────────────────────────────────────────────
        var reviewingPaneCommand = new ToggleActionCommand(
            callbacks.ToggleReviewingPane,
            callbacks.IsReviewingPaneVisible ?? (() => false));
        r.Register("freew.reviewing-pane", reviewingPaneCommand);
        r.Register("freew.reviewingpane", reviewingPaneCommand);
        // AV-REVIEW: Track Changes uses the same selection transition as the WPF command: enabling it
        // over a non-empty selection immediately records that selection as an insertion.
        r.Register("freew.track-changes", new TrackChangesToggleCommand(editor));
        var displayAllMarkup = new DisplayForReviewCommand(editor, ReviewDisplayMode.AllMarkup);
        r.Register("freew.display-for-review", displayAllMarkup);
        r.Register("freew.display-for-review-all-markup", displayAllMarkup);
        r.Register("freew.display-for-review-simple-markup", new DisplayForReviewCommand(editor, ReviewDisplayMode.SimpleMarkup));
        r.Register("freew.display-for-review-no-markup", new DisplayForReviewCommand(editor, ReviewDisplayMode.NoMarkup));
        r.Register("freew.display-for-review-original", new DisplayForReviewCommand(editor, ReviewDisplayMode.Original));
        r.Register("freew.show-markup", EmptyRibbonCommand.Instance);
        r.Register("freew.show-markup-insertions-deletions", new ShowMarkupInsertionsDeletionsCommand(editor));
        r.Register("freew.show-markup-comments", new ShowMarkupCommentsCommand(editor));
        r.Register("freew.show-markup-formatting", new ShowMarkupFormattingCommand(editor));
        r.Register("freew.show-markup-balloons", new ShowMarkupBalloonsCommand(editor, callbacks));
        // Accept / reject the revision selected in the Reviewing Pane, matching WPF's selected-row
        // authority. Test-only or detached registries retain the caret-relative fallback.
        var acceptCurrentRevisionCommand = new ActionRibbonCommand(
            callbacks.AcceptThisChange ?? (() => editor.AcceptCurrentRevision()));
        var rejectCurrentRevisionCommand = new ActionRibbonCommand(
            callbacks.RejectThisChange ?? (() => editor.RejectCurrentRevision()));
        r.Register("freew.accept-this", acceptCurrentRevisionCommand);
        r.Register("freew.accept-change", acceptCurrentRevisionCommand);
        r.Register("freew.reject-this", rejectCurrentRevisionCommand);
        r.Register("freew.reject-change", rejectCurrentRevisionCommand);
        r.Register("freew.accept-all",    new ActionRibbonCommand(() => editor.AcceptAllRevisions()));
        r.Register("freew.reject-all",    new ActionRibbonCommand(() => editor.RejectAllRevisions()));
        r.Register("freew.previous-change", new ActionRibbonCommand(callbacks.PreviousChange ?? (() => { })));
        r.Register("freew.next-change", new ActionRibbonCommand(callbacks.NextChange ?? (() => { })));
        // Comments — thread navigation/actions over the shared comment model.
        r.Register("freew.new-comment",    new ActionRibbonCommand(() => editor.NewComment()));
        r.Register("freew.delete-comment", new ActionRibbonCommand(() => editor.DeleteCommentAtCaret()));
        r.Register("freew.previous-comment", new ActionRibbonCommand(() => editor.PreviousComment()));
        r.Register("freew.next-comment", new ActionRibbonCommand(() => editor.NextComment()));
        r.Register("freew.reply-comment", new ActionRibbonCommand(
            callbacks.ReplyComment ?? (() => editor.ReplyToCommentAtCaret())));
        r.Register("freew.resolve-comment", new ActionRibbonCommand(() => editor.ToggleResolveCommentAtCaret()));
        r.Register("freew.show-comments", new ActionRibbonCommand(() =>
            callbacks.ShowComments?.Invoke(editor.PlannedCommentList())));
        // Word Count — opens the modal stats dialog (shell callback; reads DocumentStatistics).
        var statisticsCommand = new ActionRibbonCommand(callbacks.OpenWordCountDialog);
        r.Register("freew.statistics", statisticsCommand);
        r.Register("freew.word-count", statisticsCommand);
        r.Register("freew.spellcheck-toggle", new ToggleActionCommand(
            callbacks.ToggleSpellcheck ?? (() => editor.ToggleSpellCheck()),
            callbacks.IsSpellcheckActive ?? (() => editor.SpellCheckEnabled)));
        r.Register("freew.add-to-dictionary", new ActionRibbonCommand(
            callbacks.AddToDictionary ?? (() => editor.AddCurrentWordToDictionary())));
        r.Register("freew.thesaurus", new ActionRibbonCommand(callbacks.OpenThesaurus ?? (() => { })));
        r.Register("freew.set-proofing-language", new ProofingLanguageCommand(editor, callbacks));
        r.Register("freew.read-aloud", new ToggleActionCommand(
            callbacks.ToggleReadAloud ?? (() => { }),
            callbacks.IsReadAloudActive ?? (() => false)));
        r.Register("freew.check-accessibility", new ActionRibbonCommand(callbacks.CheckAccessibility ?? (() => { })));
        r.Register("freew.inspect-document", new ActionRibbonCommand(callbacks.InspectDocument ?? (() => { })));
        r.Register("freew.compare", HostCommand(callbacks.CompareDocuments));
        r.Register("freew.combine", new ActionRibbonCommand(callbacks.CombineDocuments ?? (() => { })));
        r.Register("freew.help-online", HostCommand(callbacks.OpenHelpOnline));
        r.Register("freew.feedback", HostCommand(callbacks.OpenFeedback));
        r.Register("freew.copy-diagnostics", HostCommand(callbacks.CopyDiagnostics));
        r.Register("freew.check-updates", HostCommand(callbacks.CheckForUpdates));
        r.Register("freew.about", HostCommand(callbacks.OpenAbout));
        r.Register("freew.legal-notices", HostCommand(callbacks.OpenLegalNotices));
        r.Register("freew.mark-as-final", new ToggleActionCommand(
            callbacks.MarkAsFinal ?? (() => editor.SetMarkedAsFinal(!editor.IsMarkedAsFinal)),
            () => ReviewProtectionStatePlanner.Build(editor.Document.Protection, editor.IsMarkedAsFinal)
                .MarkAsFinal.IsChecked));
        r.Register("freew.restrict-editing", new ToggleActionCommand(
            callbacks.RestrictEditing ?? (() => { }),
            () => ReviewProtectionStatePlanner.Build(editor.Document.Protection, editor.IsMarkedAsFinal)
                .RestrictEditing.IsChecked));

        // ── References (AV-REF) ──────────────────────────────────────────────
        RegisterReferencesCommands(r, editor, callbacks);

        // ── Mailings (AV-MAIL) ───────────────────────────────────────────────
        RegisterMailingsCommands(r, mailMerge);

        // ── Design (AV-DESIGN) ───────────────────────────────────────────────
        RegisterDesignCommands(r, editor, callbacks);

        // ── AV-PICTAB: Picture Format + Drawing Format contextual tabs ────────
        RegisterFloatingFormatCommands(r, editor, callbacks);

        // ── AV-CHARTTAB: Chart Design/Format + SmartArt Design contextual tabs ─
        RegisterChartSmartArtFormatCommands(r, editor, callbacks);

        return r;
    }

    private const double ParagraphSpacingTogglePoints = 12.0;

    private static void RegisterReadModeChoice(
        RibbonCommandRegistry registry,
        string commandId,
        string token,
        Action<string>? apply)
    {
        registry.Register(commandId, apply is null
            ? HostCommand(null)
            : new ActionRibbonCommand(() => apply(token)));
    }

    private static void ApplyMultiLevelList(DocumentView editor)
    {
        if (editor.GetCaretFormatting().Paragraph.ListKind != ListKind.MultiLevel)
            editor.ApplyMultiLevelListToSelection();
        editor.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalNumberFormats);
    }

    private static void ApplyMultiLevelPreset(DocumentView editor, IReadOnlyList<ListNumberFormat> numberFormats)
    {
        editor.ApplyMultiLevelListToSelection();
        editor.ApplyMultiLevelNumberFormats(numberFormats);
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

    private static void ToggleNormalNarrowMargins(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        var page = editor.Document.Page;
        callbacks.ApplyMarginPreset(PageLayoutCommandPlanner.HasNormalMargins(page) ? "narrow" : "normal");
    }

    private static void ToggleLetterA4Paper(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        var page = editor.Document.Page;
        callbacks.ApplyPaperSize(PageLayoutCommandPlanner.HasLetterPaperSize(page) ? "a4" : "letter");
    }

    private static void RegisterHeaderFooterCommands(RibbonCommandRegistry r, DocumentView editor)
    {
        r.Register("freew.hf-edit-header", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("header")));
        r.Register("freew.hf-edit-footer", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("footer")));
        r.Register("freew.hf-edit-first-header", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("first-header")));
        r.Register("freew.hf-edit-first-footer", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("first-footer")));
        r.Register("freew.hf-edit-even-header", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("even-header")));
        r.Register("freew.hf-edit-even-footer", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("even-footer")));

        r.Register("freew.hf-go-to-header", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("header")));
        r.Register("freew.hf-go-to-footer", new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("footer")));
        r.Register("freew.hf-close", new ActionRibbonCommand(editor.CloseHeaderFooterEditing));

        r.Register("freew.hf-different-first-page", new PageSettingCommand(
            editor,
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage));
        r.Register("freew.hf-different-odd-even", new PageSettingCommand(
            editor,
            page => page.DifferentOddEvenPages = !page.DifferentOddEvenPages,
            page => page.DifferentOddEvenPages));

        r.Register("freew.hf-header-from-top", new HeaderFooterDistanceCommand(editor, footer: false));
        r.Register("freew.hf-footer-from-bottom", new HeaderFooterDistanceCommand(editor, footer: true));

        r.Register("freew.hf-insert-page-number", new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        r.Register("freew.hf-insert-page-number-footer", new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Register("freew.hf-insert-datetime", new ActionRibbonCommand(editor.InsertHeaderFooterDateTime));
        r.Register("freew.hf-insert-field", new ActionRibbonCommand(editor.InsertHeaderFooterDocumentInfo));
    }

    private static IRibbonCommand HeaderFooterTextCommand(
        DocumentView editor,
        RibbonHostCallbacks callbacks,
        bool footer) =>
        callbacks.AskHeaderFooterText is { } ask
            ? new ActionRibbonCommand(() => _ = ApplyHeaderFooterTextAsync(editor, ask, footer))
            : new ActionRibbonCommand(footer ? editor.EnsureFooter : editor.EnsureHeader);

    private static async Task ApplyHeaderFooterTextAsync(
        DocumentView editor,
        Func<bool, string, Task<string?>> ask,
        bool footer)
    {
        var current = footer ? editor.Document.Footer : editor.Document.Header;
        var result = await ask(footer, current?.PlainText ?? string.Empty);
        if (result is null)
            return;

        editor.ApplyHeaderFooterText(footer, result);
    }

    private static void RegisterDeveloperControls(RibbonCommandRegistry r, DocumentView editor)
    {
        r.Register("freew.cc-text", new ActionRibbonCommand(() => editor.InsertPlainTextControl()));
        r.Register("freew.cc-richtext", new ActionRibbonCommand(() => editor.InsertRichTextControl()));
        r.Register("freew.cc-checkbox", new ActionRibbonCommand(() => editor.InsertCheckBoxControl()));
        r.Register("freew.cc-date", new ActionRibbonCommand(() => editor.InsertDatePickerControl()));
        r.Register("freew.cc-dropdown", new ActionRibbonCommand(() => editor.InsertDropDownListControl()));
        r.Register("freew.cc-combo", new ActionRibbonCommand(() => editor.InsertComboBoxControl()));
    }

    private sealed class HeaderFooterDistanceCommand(DocumentView editor, bool footer) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled
                || !HeaderFooterDialogPlanner.TryParseDistance(context.SelectedValue, out var points))
            {
                return;
            }

            if (footer)
                editor.SetFooterDistance(points);
            else
                editor.SetHeaderDistance(points);
        }

        public RibbonCommandState GetState()
        {
            var page = editor.Document.Page;
            var points = footer ? page.FooterDistancePt : page.HeaderDistancePt;
            return new(
                IsEnabled: !editor.IsEditingLocked,
                Value: HeaderFooterDialogPlanner.FormatDistance(points));
        }
    }

    private static void ExecutePageNumberFormat(
        DocumentView editor,
        RibbonHostCallbacks callbacks,
        RibbonCommandContext context)
    {
        if (PageNumberFormatDialogPlanner.TryBuildResultFromCommandValue(context.SelectedValue, out var result))
        {
            editor.ApplyPageNumberFormat(result);
            return;
        }

        callbacks.OpenPageNumberFormatDialog?.Invoke();
    }

    private sealed class PageSettingCommand(
        DocumentView editor,
        Action<PageSettings> apply,
        Func<PageSettings, bool>? isChecked = null) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => editor.ApplyPageSettings(apply);

        public RibbonCommandState GetState() => new(
            IsEnabled: !editor.IsEditingLocked,
            IsChecked: isChecked?.Invoke(editor.Document.Page) == true);
    }

    private sealed class HostPageSettingCommand(DocumentView editor, Action execute) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                execute();
        }

        public RibbonCommandState GetState() => new(IsEnabled: !editor.IsEditingLocked);
    }

    private sealed class ParagraphValueCommand(
        DocumentView editor,
        Action<double> apply,
        Func<ParagraphFormatting, double> current) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (double.TryParse(context.SelectedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var points)
                && points >= 0)
            {
                apply(points);
            }
        }

        public RibbonCommandState GetState()
        {
            var paragraph = editor.GetCaretFormatting().Paragraph;
            return new(Value: current(paragraph).ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    private sealed class ToggleActionCommand(Action toggle, Func<bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => toggle();

        public RibbonCommandState GetState() => new(IsChecked: isChecked());
    }

    private sealed class TrackChangesToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var plan = TrackChangesTogglePlanner.Build(
                editor.TrackChangesEnabled,
                hasSelection: editor.SelectedText.Length > 0);
            editor.ToggleTrackChanges();
            if (plan.MarkSelectionAsInsertion)
                editor.MarkSelectionAsRevision(RevisionKind.Inserted);
        }

        public RibbonCommandState GetState() => new(IsChecked: editor.TrackChangesEnabled);
    }

    private sealed class ProofingLanguageCommand(DocumentView editor, RibbonHostCallbacks callbacks) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (context.SelectedValue is { } selected)
            {
                editor.SetProofingLanguage(selected);
                editor.Focus();
                return;
            }

            callbacks.SetProofingLanguage?.Invoke();
        }
    }

    private sealed class FormatPainterCommand(DocumentView editor) : IRibbonCommand
    {
        private DateTime _lastExecute = DateTime.MinValue;
        private const double DoubleClickMs = 500;

        public void Execute(RibbonCommandContext context)
        {
            var now = DateTime.UtcNow;
            var isDoubleClick = (now - _lastExecute).TotalMilliseconds <= DoubleClickMs;
            _lastExecute = now;
            editor.ArmFormatPainter(locked: isDoubleClick);
            editor.Focus();
        }
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
    /// Registers the WPF-authority paragraph shading palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterParagraphShadingPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetParagraphShading(hex)));

        Add(r, editor, "freew.para-shading.yellow", "#FFFF00");
        Add(r, editor, "freew.para-shading.green", "#92D050");
        Add(r, editor, "freew.para-shading.cyan", "#00B0F0");
        Add(r, editor, "freew.para-shading.gold", "#FFC000");
        Add(r, editor, "freew.para-shading.red", "#FF0000");
        Add(r, editor, "freew.para-shading.gray", "#D9D9D9");
        Add(r, editor, "freew.para-shading.light-gray", "#A6A6A6");
        Add(r, editor, "freew.para-shading.light-yellow", "#FFF2CC");
        Add(r, editor, "freew.para-shading.light-blue", "#DEEBF7");
        Add(r, editor, "freew.para-shading.light-green", "#E2EFDA");
        Add(r, editor, "freew.para-shading.light-peach", "#FCE4D6");
        Add(r, editor, "freew.para-shading.very-light-gray", "#EDEDED");
        Add(r, editor, "freew.para-shading.none", null);
    }

    /// <summary>
    /// Registers the WPF-authority character shading palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterCharacterShadingPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetCharacterShading(hex)));

        Add(r, editor, "freew.char-shading.yellow", "#FFFF00");
        Add(r, editor, "freew.char-shading.green", "#92D050");
        Add(r, editor, "freew.char-shading.cyan", "#00B0F0");
        Add(r, editor, "freew.char-shading.gold", "#FFC000");
        Add(r, editor, "freew.char-shading.red", "#FF0000");
        Add(r, editor, "freew.char-shading.gray", "#D9D9D9");
        Add(r, editor, "freew.char-shading.light-gray", "#A6A6A6");
        Add(r, editor, "freew.char-shading.light-yellow", "#FFF2CC");
        Add(r, editor, "freew.char-shading.light-blue", "#DEEBF7");
        Add(r, editor, "freew.char-shading.light-green", "#E2EFDA");
        Add(r, editor, "freew.char-shading.light-peach", "#FCE4D6");
        Add(r, editor, "freew.char-shading.very-light-gray", "#EDEDED");
        Add(r, editor, "freew.char-shading.none", null);
    }

    /// <summary>
    /// Registers the WPF-authority character border palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit color or No Border choice.
    /// </summary>
    private static void RegisterCharacterBorderPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetCharacterBorder(
                hex is null ? null : new ParagraphBorder(hex, 0.5) { LineStyle = BorderLineStyle.Single })));

        Add(r, editor, "freew.char-border.black", "#000000");
        Add(r, editor, "freew.char-border.red", "#FF0000");
        Add(r, editor, "freew.char-border.blue", "#0070C0");
        Add(r, editor, "freew.char-border.green", "#00B050");
        Add(r, editor, "freew.char-border.gold", "#FFC000");
        Add(r, editor, "freew.char-border.purple", "#7030A0");
        Add(r, editor, "freew.char-border.gray", "#808080");
        Add(r, editor, "freew.char-border.dark-red", "#C00000");
        Add(r, editor, "freew.char-border.dark-blue", "#002060");
        Add(r, editor, "freew.char-border.dark-green", "#375623");
        Add(r, editor, "freew.char-border.brown", "#974706");
        Add(r, editor, "freew.char-border.dark-gray", "#3F3F3F");
        Add(r, editor, "freew.char-border.none", null);
    }

    /// <summary>
    /// Registers the WPF-authority text-highlight palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterHighlightPalette(RibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(RibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
            reg.Register(id, new ActionRibbonCommand(() => ed.SetHighlightColor(hex)));

        Add(r, editor, "freew.highlight.black", "#000000");
        Add(r, editor, "freew.highlight.dark-gray", "#404040");
        Add(r, editor, "freew.highlight.gray", "#7F7F7F");
        Add(r, editor, "freew.highlight.dark-red", "#C00000");
        Add(r, editor, "freew.highlight.red", "#FF0000");
        Add(r, editor, "freew.highlight.gold", "#FFC000");
        Add(r, editor, "freew.highlight.yellow", "#FFFF00");
        Add(r, editor, "freew.highlight.light-green", "#92D050");
        Add(r, editor, "freew.highlight.green", "#00B050");
        Add(r, editor, "freew.highlight.cyan", "#00B0F0");
        Add(r, editor, "freew.highlight.blue", "#0070C0");
        Add(r, editor, "freew.highlight.dark-blue", "#2F5496");
        Add(r, editor, "freew.highlight.purple", "#7030A0");
        Add(r, editor, "freew.highlight.white", "#FFFFFF");
        Add(r, editor, "freew.highlight.none", null);
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
        // Hyperlink / Bookmark open small dialogs (shell callbacks) that call the model-backed editor methods.
        r.Register("freew.hyperlink",        new ActionRibbonCommand(callbacks.OpenHyperlinkDialog ?? (() => { })));
        r.Register("freew.insert-hyperlink", new ActionRibbonCommand(callbacks.OpenHyperlinkDialog ?? (() => { })));
        r.Register("freew.edit-hyperlink",   new ActionRibbonCommand(callbacks.OpenEditHyperlinkDialog ?? (() => { })));
        r.Register("freew.remove-hyperlink", new ActionRibbonCommand(editor.RemoveHyperlink));
        r.Register("freew.hyperlink-tooltip", new ActionRibbonCommand(callbacks.OpenHyperlinkTooltipDialog ?? (() => { })));
        r.Register("freew.bookmark",         new ActionRibbonCommand(callbacks.OpenBookmarkDialog ?? (() => { })));
        r.Register("freew.insert-bookmark",  new ActionRibbonCommand(callbacks.OpenBookmarkDialog ?? (() => { })));
        r.Register("freew.link-bookmark",    new ActionRibbonCommand(callbacks.OpenLinkBookmarkDialog ?? (() => LinkToFirstBookmark(editor))));
        r.Register("freew.bookmark-manager", new ActionRibbonCommand(
            callbacks.OpenBookmarkManagerDialog ?? callbacks.OpenBookmarkDialog ?? (() => { })));

        // ── Cover Page ───────────────────────────────────────────────────────
        // The split-button face inserts the WPF default; each preset prepends its cover-page block layout.
        r.Register("freew.cover-page",         new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Default)));
        r.Register("freew.cover-page.default", new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Default)));
        r.Register("freew.cover-page.banded",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Banded)));
        r.Register("freew.cover-page.motion",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Motion)));

        // ── Drop Cap ─────────────────────────────────────────────────────────
        // Dropped / In Margin both enlarge the leading letter (the in-margin float geometry is an
        // approximation — render-deferred); None clears the paragraph's run formatting.
        r.Register("freew.drop-cap",           new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Register("freew.drop-cap.dropped",   new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Register("freew.drop-cap.in-margin", new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        r.Register("freew.drop-cap.none",      new ActionRibbonCommand(editor.ClearDropCap));
        r.Register("freew.drop-cap-dropped",   new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Register("freew.drop-cap-in-margin", new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        r.Register("freew.drop-cap-none",      new ActionRibbonCommand(editor.ClearDropCap));
        r.Register("freew.drop-cap-options",   new ActionRibbonCommand(callbacks.OpenDropCapOptionsDialog ?? (() => { })));

        // ── Quick Parts ──────────────────────────────────────────────────────
        // Document-property / date fields insert directly; the snippet entry opens a dialog (shell callback).
        r.Register("freew.quick-parts",         new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.quick-parts.title",   new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Title)));
        r.Register("freew.quick-parts.author",  new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Author)));
        r.Register("freew.quick-parts.subject", new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Subject)));
        r.Register("freew.quick-parts.keywords", new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Keywords)));
        r.Register("freew.quick-parts.comments", new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.DocComments)));
        r.Register("freew.quick-parts.date",    new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.Date)));
        r.Register("freew.quick-parts.snippet", new ActionRibbonCommand(callbacks.OpenQuickPartDialog ?? (() => { })));

        // ── Equation ─────────────────────────────────────────────────────────
        // The split-button face inserts the WPF default; each preset inserts an inline OMML equation.
        r.Register("freew.equation",           new ActionRibbonCommand(() => editor.InsertEquation()));
        r.Register("freew.equation.default",   new ActionRibbonCommand(() => editor.InsertEquation()));
        r.Register("freew.equation.fraction",  new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Fraction("a", "b")]))));
        r.Register("freew.equation.script",    new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.SubSuperscript("x", "n", "2")]))));
        r.Register("freew.equation.radical",   new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Radical("x")]))));
        r.Register("freew.equation.nthroot",   new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Radical("x", "n")]))));
        r.Register("freew.equation.integral",  new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.NAry("∫", "a", "b", "f(x) dx")]))));
        r.Register("freew.equation.summation", new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.NAry("∑", "i=1", "n", "i")]))));
        r.Register("freew.equation.product",   new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.NAry("∏", "i=1", "n", "i")]))));
        r.Register("freew.equation.accent",    new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.AccentOf("x")]))));
        r.Register("freew.equation.bar",       new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.BarOf("x")]))));
        r.Register("freew.equation.bracket",   new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.Delimiter("a, b")]))));
        r.Register("freew.equation.matrix",    new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]))));
        r.Register("freew.equation.func",      new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.FunctionApply("sin", "x")]))));
        r.Register("freew.equation.groupchr",  new ActionRibbonCommand(() => editor.InsertEquation(new Equation([MathRun.GroupCharOf("x+y")]))));

        // ── Text from File ───────────────────────────────────────────────────
        // Opens a file picker (shell callback) and inserts the loaded document's text at the caret.
        var textFromFileCommand = new ActionRibbonCommand(callbacks.InsertTextFromFile ?? (() => { }));
        r.Register("freew.insert-file", textFromFileCommand);
        r.Register("freew.text-from-file", textFromFileCommand);
        r.Register("freew.chart", new EditingActionCommand(editor, callbacks.OpenInsertChartDialog, () => editor.InsertChart()));
        r.Register("freew.smartart", new EditingActionCommand(editor, callbacks.OpenInsertSmartArtDialog, () => editor.InsertSmartArt()));
        r.Register("freew.insert-icon", new EditingActionCommand(editor, callbacks.OpenIconPickerDialog, editor.InsertIcon));
        r.Register("freew.wordart", new ActionRibbonCommand(() => editor.InsertWordArt()));
        r.Register("freew.object", new ActionRibbonCommand(() => editor.InsertEmbeddedObject()));
        r.Register("freew.update-fields", new ActionRibbonCommand(editor.UpdateFields));
        r.Register("freew.toggle-field-codes", new ActionRibbonCommand(editor.ToggleFieldCodes));
    }

    private static void LinkToFirstBookmark(DocumentView editor)
    {
        var bookmarks = editor.BookmarkNames();
        if (bookmarks.Count > 0)
            editor.ApplyInternalLink(bookmarks[0]);
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

    private sealed class DisplayForReviewCommand(DocumentView editor, ReviewDisplayMode mode) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => editor.ApplyDisplayForReview(mode);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == mode);
    }

    private sealed class ShowMarkupInsertionsDeletionsCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyShowMarkupInsertionsAndDeletions(!editor.ShowMarkupInsertionsAndDeletions);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupInsertionsAndDeletions);
    }

    private sealed class ShowMarkupCommentsCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyShowMarkupComments(!editor.ShowMarkupComments);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupComments);
    }

    private sealed class ShowMarkupFormattingCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyShowMarkupFormatting(!editor.ShowMarkupFormatting);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupFormatting);
    }

    private sealed class ShowMarkupBalloonsCommand(DocumentView editor, RibbonHostCallbacks callbacks) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (callbacks.ToggleReviewBalloons is { } toggle)
            {
                toggle();
                return;
            }

            editor.ApplyShowMarkupBalloons(!editor.ShowMarkupBalloons);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: callbacks.IsReviewBalloonsActive?.Invoke() ?? editor.ShowMarkupBalloons);
    }

    private sealed class TablePropertiesCommand(
        DocumentView editor,
        Action<ModelTableContext> openDialog) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (editor.CaretTableContext() is not { } tableContext)
                return;

            openDialog(tableContext);
        }
    }

    private sealed class TableFormulaCommand(
        DocumentView editor,
        Action<TableFormulaDialogInitialState> openDialog) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (editor.CaretTableCell() is not { } caret)
                return;

            var initialState = TableFormulaDialogPlanner.BuildInitialState(
                caret.Table,
                caret.RowIndex,
                caret.ColumnIndex);
            openDialog(initialState);
        }
    }

    /// <summary>
    /// AV-REF: Registers the References-tab commands — footnote / endnote, Table of Contents
    /// (insert + update), caption (Figure / Table), cross-reference, and citation / bibliography.
    ///
    /// <para>
    /// Footnote / endnote insert an empty note (the user types its content where the AV-HF note region
    /// renders). The two caption commands auto-number via <see cref="Captions.NextCaptionNumber"/>.
    /// Cross-reference, citation, and source management route through shell dialog callbacks so the shell
    /// realizes the shared planner choices instead of silently choosing a default target/source.
    /// Bibliography builds the back-matter block using the model's Citations engine.
    /// </para>
    /// </summary>
    private static void RegisterReferencesCommands(
        RibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
    {
        // Footnotes & Endnotes — insert an empty note + reference marker at the caret.
        var footnote = new ActionRibbonCommand(
            callbacks.OpenFootnoteDialog ?? (() => editor.InsertFootnote()));
        r.Register("freew.footnote", footnote);
        r.Register("freew.insert-footnote", footnote);
        r.Register("freew.next-footnote", new ActionRibbonCommand(() => editor.MoveToNextFootnote()));
        r.Register("freew.previous-footnote", new ActionRibbonCommand(() => editor.MoveToPreviousFootnote()));
        r.Register("freew.next-endnote", new ActionRibbonCommand(() => editor.MoveToNextEndnote()));
        r.Register("freew.previous-endnote", new ActionRibbonCommand(() => editor.MoveToPreviousEndnote()));
        r.Register("freew.show-notes",
            callbacks.ToggleNotesPane is { } toggle && callbacks.IsNotesPaneVisible is { } isVisible
                ? new FreeWStatefulToggleCommand(toggle, isVisible)
                : new ActionRibbonCommand(callbacks.ToggleNotesPane ?? (() => { })));
        r.Register("freew.footnote-endnote-options", new ActionRibbonCommand(
            callbacks.OpenFootnoteEndnoteOptionsDialog ?? (() => { })));

        var endnote = new ActionRibbonCommand(
            callbacks.OpenEndnoteDialog ?? (() => editor.InsertEndnote()));
        r.Register("freew.endnote", endnote);
        r.Register("freew.insert-endnote", endnote);

        // Table of Contents — generate from the heading outline / regenerate in place.
        var toc = new ActionRibbonCommand(editor.InsertTableOfContents);
        r.Register("freew.toc", toc);
        r.Register("freew.insert-toc", toc);

        var tocRefresh = new ActionRibbonCommand(editor.UpdateTableOfContents);
        r.Register("freew.toc-refresh", tocRefresh);
        r.Register("freew.update-toc", tocRefresh);

        // Captions — the primary action opens the label/text dialog; menu labels remain direct.
        var caption = new ActionRibbonCommand(callbacks.OpenCaptionDialog ?? (() => { }));
        r.Register("freew.caption", caption);
        r.Register("freew.insert-caption", caption);
        r.Register("freew.insert-caption.figure", new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Figure)));
        r.Register("freew.insert-caption.table",  new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Table)));
        r.Register("freew.insert-caption.equation", new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Equation)));

        // Dialog-backed commands no-op without a shell callback instead of silently choosing defaults.
        r.Register("freew.cross-reference", new ActionRibbonCommand(callbacks.OpenCrossReferenceDialog ?? (() => { })));

        var citation = new ActionRibbonCommand(callbacks.OpenCitationDialog ?? (() => { }));
        r.Register("freew.citation", citation);
        r.Register("freew.insert-citation", citation);
        r.Register("freew.manage-sources", new ActionRibbonCommand(callbacks.OpenManageSourcesDialog ?? (() => { })));
        r.Register("freew.citation-style", new CitationStyleCommand(editor));
        r.Register("freew.bibliography", new ActionRibbonCommand(editor.InsertBibliography));

        r.Register("freew.tof", new ActionRibbonCommand(() => editor.InsertTableOfFigures()));
        r.Register("freew.tof.figure", new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Figure)));
        r.Register("freew.tof.table", new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Table)));
        r.Register("freew.tof.equation", new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Equation)));
        r.Register("freew.tof-refresh", new ActionRibbonCommand(() => editor.RefreshTableOfFigures()));
        r.Register("freew.tof-refresh.figure", new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Figure)));
        r.Register("freew.tof-refresh.table", new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Table)));
        r.Register("freew.tof-refresh.equation", new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Equation)));
        r.Register("freew.index-mark", new ActionRibbonCommand(() => editor.MarkIndexEntry()));
        r.Register("freew.index-insert", new ActionRibbonCommand(editor.InsertIndex));
        r.Register("freew.index-refresh", new ActionRibbonCommand(editor.RefreshIndex));
        r.Register("freew.mark-citation", new ActionRibbonCommand(callbacks.OpenMarkCitationDialog ?? (() => { })));
        r.Register("freew.table-of-authorities", new ActionRibbonCommand(
            callbacks.ShowTableOfAuthoritiesDialog ?? (() =>
            {
                var options = callbacks.OpenTableOfAuthoritiesDialog?.Invoke();
                if (options is null && callbacks.OpenTableOfAuthoritiesDialog is not null)
                    return;
                editor.InsertTableOfAuthorities(options ?? ToaOptions.Default);
            })));
        r.Register("freew.table-of-authorities-refresh", new ActionRibbonCommand(editor.RefreshTableOfAuthorities));
    }

    private sealed class CitationStyleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (context.SelectedValue is not { Length: > 0 } value)
                return;

            editor.Document.BibliographyStyle = Citations.ParseStyle(value, editor.Document.BibliographyStyle);
        }

        public RibbonCommandState GetState() =>
            new(Value: Citations.StyleName(editor.Document.BibliographyStyle));
    }

    /// <summary>
    /// AV-PICTAB: Registers the Picture Format + Drawing Format contextual-tab commands, wiring each
    /// to the floating-object edit surface on <see cref="DocumentView"/>. Both tabs share the same
    /// underlying methods (the model dispatches by the selected float's kind), so the only difference
    /// is the command-id prefix (<c>image-</c> vs <c>shape-</c>) used by the respective tab.
    ///
    /// <para>
    /// Commands no-op when no compatible float is selected (the DocumentView methods guard on
    /// <c>SelectedFloatingInfo</c>). Top-level button commands use shared default plans when Avalonia
    /// has no dialog value yet; wrap, rotate/flip, z-order, size, and shape/text-box fill/outline
    /// commands are generated from the shared object-format planner.
    /// </para>
    /// </summary>
    private static void RegisterFloatingFormatCommands(
        RibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
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

        RegisterFloatingPositionCommands(r, editor, "image", "Image", callbacks.OpenImagePositionDialog);
        r.Register("freew.image-adjust-dialog", new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAdjustDialog));
        r.Register("freew.image-crop", new ImageCropCommand(editor, callbacks));
        r.Register("freew.image-size", new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageSizeDialog));
        r.Register("freew.image-alt-text", new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAltTextDialog));
        r.Register("freew.image-border", new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageBorderDialog));
        RegisterImageAdjustmentCommands(r, editor, callbacks);
        r.Register("freew.image-reset", new ImageResetCommand(editor));
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            var captured = preset;
            r.Register(
                $"freew.image-style-{captured.Id}",
                new ImageStylePresetCommand(editor, captured));
        }
        r.Register("freew.image-align-left", new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Left));
        r.Register("freew.image-align-center", new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Center));
        r.Register("freew.image-align-right", new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Right));
        r.Register("freew.image-align-to-page", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        r.Register("freew.image-align-to-margin", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        r.Register("freew.image-distribute-h", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        r.Register("freew.image-distribute-v", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        RegisterFloatingPositionCommands(r, editor, "shape", "Shape", callbacks.OpenShapePositionDialog);
        r.Register("freew.shape-edit-shape", new ActionRibbonCommand(() => editor.Focus()));
        r.Register("freew.shape-convert-freeform", new ActionRibbonCommand(editor.ConvertSelectedShapeToFreeform));
        r.Register("freew.shape-edit-points", new ActionRibbonCommand(editor.BeginShapeEditPoints));
        r.Register("freew.shape-change", new ShapeKindCommand(editor, null));
        r.Register("freew.shape-change-rectangle", new ShapeKindCommand(editor, ShapeKind.Rectangle));
        r.Register("freew.shape-change-rounded", new ShapeKindCommand(editor, ShapeKind.RoundedRectangle));
        r.Register("freew.shape-change-ellipse", new ShapeKindCommand(editor, ShapeKind.Ellipse));
        r.Register("freew.shape-text-direction", new ActionRibbonCommand(() => editor.Focus()));
        r.Register("freew.shape-text-horizontal", new ShapeTextDirectionCommand(editor, ShapeTextDirection.Horizontal));
        r.Register("freew.shape-text-rotate90", new ShapeTextDirectionCommand(editor, ShapeTextDirection.Rotate90));
        r.Register("freew.shape-text-rotate270", new ShapeTextDirectionCommand(editor, ShapeTextDirection.Rotate270));
        r.Register("freew.shape-align-left", new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Left));
        r.Register("freew.shape-align-center", new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Center));
        r.Register("freew.shape-align-right", new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Right));
        r.Register("freew.shape-align-to-page", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        r.Register("freew.shape-align-to-margin", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        r.Register("freew.shape-distribute-h", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        r.Register("freew.shape-distribute-v", new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        r.Register("freew.shape-size", new FloatingObjectSizeCommand(editor, "Shape", callbacks.OpenShapeSizeDialog));
        foreach (var preset in FreeWRibbonDefinitionData.FloatingSizePresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-size-{captured.Suffix}",
                new FloatingObjectSizePresetCommand(editor, "Shape", captured));
        }

        r.Register("freew.shape-alt-text", new FloatingObjectAltTextCommand(editor, callbacks.OpenShapeAltTextDialog));
        foreach (var preset in FreeWRibbonDefinitionData.ShapeAltTextPresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-alt-text-{captured.Suffix}",
                new FloatingObjectAltTextPresetCommand(editor, captured));
        }
        r.Register("freew.object-group", new FloatingObjectGroupCommand(editor));
        r.Register("freew.object-ungroup", new FloatingObjectUngroupCommand(editor));

        // Shape Styles fill/outline: top-level opener ids plus menu item commands.
        RegisterShapeFillOutlineCommands(r, editor);
    }

    private static void RegisterImageAdjustmentCommands(
        RibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
    {
        // These IDs are the WPF authority's Picture Format > Adjust routes. Keep the
        // value-preserving mutations in DocumentView so both hosts use the shared model commands.
        RegisterImageMutation(r, editor, "freew.image-brightness-plus20",
            image => editor.SetSelectedImageAdjust(20, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-brightness-plus40",
            image => editor.SetSelectedImageAdjust(40, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-brightness-minus20",
            image => editor.SetSelectedImageAdjust(-20, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-brightness-minus40",
            image => editor.SetSelectedImageAdjust(-40, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-contrast-plus20",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, 20, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-contrast-minus20",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, -20, image.SaturationPct, image.TransparencyPct));

        RegisterImageMutation(r, editor, "freew.image-saturation-0",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 0, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-saturation-50",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 50, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-saturation-200",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 200, image.TransparencyPct));
        RegisterImageMutation(r, editor, "freew.image-transparency-25",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 25));
        RegisterImageMutation(r, editor, "freew.image-transparency-50",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 50));
        RegisterImageMutation(r, editor, "freew.image-transparency-75",
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 75));

        // Avalonia currently exposes one shared adjustment dialog callback, which is also
        // the WPF route used for Color and Transparency's full-value dialogs.
        r.Register("freew.image-color-dialog", new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));
        r.Register("freew.image-transparency-dialog", new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));

        RegisterImageMutation(r, editor, "freew.image-recolor-grayscale",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Grayscale));
        RegisterImageMutation(r, editor, "freew.image-recolor-sepia",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Sepia));
        RegisterImageMutation(r, editor, "freew.image-recolor-washout",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Washout));
        RegisterImageMutation(r, editor, "freew.image-recolor-blackwhite",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.BlackWhite));
        RegisterImageMutation(r, editor, "freew.image-recolor-none",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None));
        RegisterImageMutation(r, editor, "freew.image-colortemp-warm",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, 60));
        RegisterImageMutation(r, editor, "freew.image-colortemp-cool",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, -60));
        RegisterImageMutation(r, editor, "freew.image-colortemp-neutral",
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, 0));

        RegisterImageMutation(r, editor, "freew.image-shadow-none",
            image => editor.SetSelectedImageEffect(0, image.GlowSizePt, image.GlowColorHex,
                image.ReflectionPreset, image.SoftEdgePt, image.BevelPreset));
        for (var preset = 1; preset <= 5; preset++)
        {
            var captured = preset;
            RegisterImageMutation(r, editor, $"freew.image-shadow-{captured}",
                image => editor.SetSelectedImageEffect(captured, image.GlowSizePt, image.GlowColorHex,
                    image.ReflectionPreset, image.SoftEdgePt, image.BevelPreset));
            RegisterImageMutation(r, editor, $"freew.image-reflection-{captured}",
                image => editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                    captured, image.SoftEdgePt, image.BevelPreset));
        }
        RegisterImageMutation(r, editor, "freew.image-reflection-none",
            image => editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                0, image.SoftEdgePt, image.BevelPreset));

        foreach (var glow in new[] { 0d, 5d, 8d, 11d, 18d })
        {
            var captured = glow;
            var suffix = captured == 0 ? "none" : captured.ToString("0", CultureInfo.InvariantCulture);
            RegisterImageMutation(r, editor, $"freew.image-glow-{suffix}",
                image => editor.SetSelectedImageEffect(image.ShadowPreset, captured, image.GlowColorHex,
                    image.ReflectionPreset, image.SoftEdgePt, image.BevelPreset));
        }
        foreach (var softEdge in new[] { 0d, 1d, 2.5d, 5d, 10d })
        {
            var captured = softEdge;
            var suffix = captured == 0
                ? "none"
                : captured == 2.5
                    ? "2pt5"
                    : captured.ToString("0", CultureInfo.InvariantCulture);
            RegisterImageMutation(r, editor, $"freew.image-softedge-{suffix}",
                image => editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                    image.ReflectionPreset, captured, image.BevelPreset));
        }
        RegisterImageMutation(r, editor, "freew.image-bevel-none",
            image => editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                image.ReflectionPreset, image.SoftEdgePt, 0));
        for (var preset = 1; preset <= 4; preset++)
        {
            var captured = preset;
            RegisterImageMutation(r, editor, $"freew.image-bevel-{captured}",
                image => editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                    image.ReflectionPreset, image.SoftEdgePt, captured));
        }

        foreach (var effect in Enum.GetValues<ImageArtisticEffect>())
        {
            var captured = effect;
            var suffix = captured switch
            {
                ImageArtisticEffect.Blur => "blur",
                ImageArtisticEffect.PencilGrayscale => "pencil-gray",
                ImageArtisticEffect.GlowDiffused => "glow-diffused",
                ImageArtisticEffect.GlowEdges => "glow-edges",
                ImageArtisticEffect.PencilSketch => "pencil-sketch",
                ImageArtisticEffect.LineDrawing => "line-drawing",
                ImageArtisticEffect.Paintbrush => "paintbrush",
                ImageArtisticEffect.PaintStrokes => "paint-strokes",
                ImageArtisticEffect.Photocopy => "photocopy",
                ImageArtisticEffect.Posterize => "posterize",
                ImageArtisticEffect.Pastels => "pastels",
                ImageArtisticEffect.Watercolor => "watercolor",
                ImageArtisticEffect.FilmGrain => "film-grain",
                ImageArtisticEffect.Mosaic => "mosaic",
                _ => "none"
            };
            RegisterImageMutation(r, editor, $"freew.image-artistic-{suffix}",
                _ => editor.SetSelectedImageArtisticEffect(captured));
        }
    }

    private static void RegisterImageMutation(
        RibbonCommandRegistry registry,
        DocumentView editor,
        string commandId,
        Action<InlineImage> mutation) =>
        registry.Register(commandId, new SelectedImageMutationCommand(editor, mutation));

    private static void RegisterFloatingPositionCommands(
        RibbonCommandRegistry r,
        DocumentView editor,
        string prefix,
        string requiredKind,
        Action? openDialog = null)
    {
        r.Register($"freew.{prefix}-position", new FloatingObjectPositionCommand(editor, requiredKind, openDialog));
        foreach (var preset in FreeWRibbonDefinitionData.FloatingPositionPresets)
        {
            var captured = preset;
            r.Register(
                $"freew.{prefix}-position-{captured.Suffix}",
                new FloatingObjectPositionPresetCommand(editor, requiredKind, captured));
        }
    }

    private sealed class FloatingObjectArrangeCommand(
        DocumentView editor,
        FloatingObjectArrangeKind kind) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (editor.CanArrangeSelectedFloatingObjects(kind))
                editor.ArrangeSelectedFloatingObjects(kind);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.CanArrangeSelectedFloatingObjects(kind));
    }

    private sealed class ImageCropCommand(
        DocumentView editor,
        RibbonHostCallbacks callbacks) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                callbacks.OpenImageCropDialog?.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingImage() is not null && callbacks.OpenImageCropDialog is not null);
    }

    private sealed class SelectedImageDialogCommand(
        DocumentView editor,
        Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                openDialog!.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingImage() is not null && openDialog is not null);
    }

    private sealed class SelectedImageMutationCommand(
        DocumentView editor,
        Action<InlineImage> mutation) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled && editor.SelectedFloatingImage() is { } image)
                mutation(image);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingImage() is not null);
    }

    private sealed class SelectedFloatingDialogCommand(
        DocumentView editor,
        string requiredKind,
        Action? openDialog,
        Action? fallbackAction = null) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                (openDialog ?? fallbackAction)!.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingInfo?.Kind == requiredKind
                && (openDialog is not null || fallbackAction is not null));
    }

    private sealed class EditingActionCommand(
        DocumentView editor,
        Action? hostAction,
        Action fallbackAction) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;
            (hostAction ?? fallbackAction)();
        }

        public RibbonCommandState GetState() => new(IsEnabled: !editor.IsEditingLocked);
    }

    private sealed class ImageResetCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                editor.ResetSelectedImage();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingImage() is not null);
    }

    private sealed class ImageStylePresetCommand(
        DocumentView editor,
        PictureStylePreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                editor.ApplySelectedImageStyle(preset);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingImage() is not null);
    }

    private sealed class TableToTextCommand(
        DocumentView editor,
        RibbonHostCallbacks callbacks) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                callbacks.OpenTableToTextDialog?.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.CanConvertTableToText && callbacks.OpenTableToTextDialog is not null);
    }

    private sealed class FloatingObjectParagraphAlignCommand(
        DocumentView editor,
        string requiredKind,
        TextAlignment alignment) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!IsEnabled())
                return;

            if (requiredKind == "Image")
                editor.SetSelectedImageAlignment(alignment);
            else
                editor.SetSelectedShapeAlignment(alignment);
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => requiredKind == "Shape"
            ? editor.SelectedFloatingShape() is not null
            : editor.SelectedFloatingInfo?.Kind == requiredKind;
    }

    private sealed class FloatingObjectPositionCommand(
        DocumentView editor,
        string requiredKind,
        Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!IsEnabled())
                return;

            if (!TryParsePosition(context.SelectedValue, out var hOffset, out var vOffset, out var hAnchor, out var vAnchor))
            {
                openDialog?.Invoke();
                return;
            }

            editor.SetFloatingPosition(hOffset, vOffset, hAnchor, vAnchor);
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => editor.SelectedFloatingInfo?.Kind == requiredKind;

        private static bool TryParsePosition(
            string? value,
            out double hOffset,
            out double vOffset,
            out HorizontalAnchor hAnchor,
            out VerticalAnchor vAnchor)
        {
            hOffset = 0;
            vOffset = 0;
            hAnchor = HorizontalAnchor.Column;
            vAnchor = VerticalAnchor.Paragraph;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out hOffset)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vOffset))
            {
                return false;
            }

            if (parts.Length >= 3)
                Enum.TryParse(parts[2], ignoreCase: true, out hAnchor);
            if (parts.Length >= 4)
                Enum.TryParse(parts[3], ignoreCase: true, out vAnchor);
            return true;
        }
    }

    private sealed class FloatingObjectPositionPresetCommand(
        DocumentView editor,
        string requiredKind,
        FreeWFloatingPositionPreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!IsEnabled())
                return;

            editor.SetFloatingPosition(
                preset.HorizontalOffsetPt,
                preset.VerticalOffsetPt,
                preset.HorizontalAnchor,
                preset.VerticalAnchor);
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => editor.SelectedFloatingInfo?.Kind == requiredKind;
    }

    private sealed class FloatingObjectSizeCommand(
        DocumentView editor,
        string requiredKind,
        Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!IsEnabled())
                return;

            if (TryParseSize(context.SelectedValue, out var widthPt, out var heightPt))
                editor.SetFloatingSize(widthPt, heightPt);
            else if (string.IsNullOrWhiteSpace(context.SelectedValue))
                openDialog?.Invoke();
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => editor.SelectedFloatingInfo?.Kind == requiredKind
            && editor.GetSelectedFloatingSize() is not null;

        private static bool TryParseSize(string? value, out double widthPt, out double heightPt)
        {
            widthPt = 0;
            heightPt = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            return parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out widthPt)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out heightPt)
                && widthPt > 0
                && heightPt > 0;
        }
    }

    private sealed class FloatingObjectSizePresetCommand(
        DocumentView editor,
        string requiredKind,
        FreeWFloatingSizePreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (IsEnabled())
                editor.SetFloatingSize(preset.WidthPt, preset.HeightPt);
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => editor.SelectedFloatingInfo?.Kind == requiredKind
            && editor.GetSelectedFloatingSize() is not null;
    }

    private sealed class FloatingObjectAltTextCommand(
        DocumentView editor,
        Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!CanEditAltText())
                return;

            if (context.SelectedValue is null)
                openDialog?.Invoke();
            else
                editor.SetSelectedFloatingAltText(context.SelectedValue);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: CanEditAltText());

        private bool CanEditAltText() =>
            editor.SelectedFloatingInfo?.Kind is "Shape" or "WordArt";
    }

    private sealed class FloatingObjectAltTextPresetCommand(
        DocumentView editor,
        FreeWAltTextPreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (CanEditAltText())
                editor.SetSelectedFloatingAltText(preset.AltText);
        }

        public RibbonCommandState GetState() => new(IsEnabled: CanEditAltText());

        private bool CanEditAltText() =>
            editor.SelectedFloatingInfo?.Kind is "Shape" or "WordArt";
    }

    private sealed class FloatingObjectGroupCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (editor.HasMultipleFloatingObjectsSelected)
                editor.GroupSelectedFloatingObjects();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.HasMultipleFloatingObjectsSelected);
    }

    private sealed class FloatingObjectUngroupCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (editor.IsGroupSelected)
                editor.UngroupSelectedFloatingObject();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.IsGroupSelected);
    }

    private sealed class ShapeTextDirectionCommand(
        DocumentView editor,
        ShapeTextDirection direction) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                editor.SetSelectedShapeTextDirection(direction);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingShape() is { HasText: true });
    }

    private sealed class ShapeKindCommand(
        DocumentView editor,
        ShapeKind? kind) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (kind is { } target && GetState().IsEnabled)
                editor.SetSelectedShapeKind(target);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingShape() is not null);
    }

    private sealed class ShapeEffectsCommand(
        DocumentView editor,
        ShapeEffectLst? effects) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;

            editor.SetSelectedShapeEffects(effects?.Clone());
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingShape() is not null);
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

        r.Register("freew.shape-effects", new ShapeEffectsCommand(editor, null));
        r.Register("freew.shape-effects-none", new ShapeEffectsCommand(editor, null));
        r.Register("freew.shape-effect-shadow", new ShapeEffectsCommand(editor, new ShapeEffectLst { HasShadow = true }));
        r.Register("freew.shape-effect-glow", new ShapeEffectsCommand(editor, new ShapeEffectLst { HasGlow = true }));
        r.Register("freew.shape-effect-soft-edge", new ShapeEffectsCommand(editor, new ShapeEffectLst { HasSoftEdge = true }));
        r.Register("freew.shape-effect-reflection", new ShapeEffectsCommand(editor, new ShapeEffectLst { HasReflection = true }));
        r.Register("freew.shape-effect-bevel", new ShapeEffectsCommand(editor, new ShapeEffectLst { HasBevel = true }));

        r.Register("freew.shape-styles-gallery", new ShapeStylesGalleryCommand(editor));
        foreach (var preset in ShapeStylePreset.Catalog)
        {
            var captured = preset;
            r.Register($"freew.{captured.Id}", new ShapeStylePresetCommand(editor, captured));
        }
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

    private sealed class ShapeStylesGalleryCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind))
                return;

            if (string.IsNullOrWhiteSpace(context.SelectedValue))
                return;

            var preset = ShapeStylePreset.Catalog
                .FirstOrDefault(item => string.Equals(item.Id, context.SelectedValue, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
                editor.ApplySelectedShapeStyle(preset);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind));
    }

    private sealed class ShapeStylePresetCommand(
        DocumentView editor,
        ShapeStylePreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ObjectFormatCommandPlanner.CanFormatShapeFillOutline(editor.SelectedFloatingShape()?.Kind))
                editor.ApplySelectedShapeStyle(preset);
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
    private static void RegisterChartSmartArtFormatCommands(
        RibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
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

        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var captured = layout;
            r.Register(
                $"freew.chart-quick-layout-{captured.Id}",
                new ChartQuickLayoutRibbonCommand(editor, captured));
        }

        // Change Colors — dropdown opener + one command per catalog colour scheme.
        r.Register("freew.chart-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.chart-colors-{sc.Id}", new ActionRibbonCommand(() => editor.SetChartColorScheme(sc.Id)));
        }

        r.Register("freew.chart-toggle-legend", new ActionRibbonCommand(editor.ToggleChartLegend));
        r.Register("freew.chart-title", new SelectedFloatingDialogCommand(
            editor, "Chart", callbacks.OpenChartTitleDialog, editor.ToggleChartTitle));
        r.Register("freew.chart-axis-titles", new SelectedFloatingDialogCommand(
            editor, "Chart", callbacks.OpenChartAxisTitlesDialog, editor.ToggleChartAxisTitles));
        r.Register("freew.chart-edit-data", new ContextRibbonCommand(context =>
        {
            if (TryBuildChartDataPreset(context.SelectedValue, out var chart))
                editor.ReplaceSelectedChartData(chart);
            else if (string.IsNullOrWhiteSpace(context.SelectedValue)
                     && editor.SelectedFloatingChart() is not null)
                callbacks.OpenChartEditDataDialog?.Invoke();
        }));
        var chartSize = new ChartSizeCommand(editor, callbacks.OpenChartSizeDialog);
        r.Register("freew.chart-size", chartSize);
        r.Register("freew.chart-size-dialog", chartSize);

        // ── SmartArt Design ───────────────────────────────────────────────────
        // Layouts — the four Word families. Cycle maps to the model's Process kind (closest flat sequence).
        r.Register("freew.smartart-layout", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.smartart-layout-list",      new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.List)));
        r.Register("freew.smartart-layout-process",   new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-cycle",     new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Process)));
        r.Register("freew.smartart-layout-hierarchy", new ActionRibbonCommand(() => editor.SetSmartArtLayout(SmartArtKind.Hierarchy)));
        foreach (var preset in SmartArtLayoutPreset.Catalog)
            RegisterSmartArtLayoutPreset(r, editor, $"freew.smartart-layout-{preset.Id}", preset.Id);

        // Change Colors — use the SmartArt catalog. Its native ids differ from chart color-scheme ids.
        r.Register("freew.smartart-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in SmartArtColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.smartart-colors-{sc.Id}", new ActionRibbonCommand(() => editor.SetSmartArtColor(sc.Id)));
        }

        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-add-shape", SmartArtStructureOperation.AddShape);
        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-remove-shape", SmartArtStructureOperation.RemoveShape);
        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-promote", SmartArtStructureOperation.Promote);
        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-demote", SmartArtStructureOperation.Demote);
        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-move-up", SmartArtStructureOperation.MoveUp);
        RegisterSmartArtStructureCommand(r, editor, "freew.smartart-move-down", SmartArtStructureOperation.MoveDown);
        r.Register("freew.smartart-edit-text", new SmartArtEditTextRibbonCommand(editor, callbacks.OpenSmartArtEditDialog));
        r.Register("freew.smartart-change-style", new SmartArtStyleRibbonCommand(editor));
    }

    private static void RegisterSmartArtStructureCommand(
        RibbonCommandRegistry registry,
        DocumentView editor,
        string commandId,
        SmartArtStructureOperation operation) =>
        registry.Register(commandId, new SmartArtStructureRibbonCommand(editor, operation));

    private sealed class SmartArtStructureRibbonCommand(
        DocumentView editor,
        SmartArtStructureOperation operation) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                editor.MutateSelectedSmartArt(operation);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.IsEnabled(editor.SelectedFloatingSmartArt(), operation));
    }

    private sealed class SmartArtEditTextRibbonCommand(
        DocumentView editor,
        Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var selected = editor.SelectedFloatingSmartArt();
            if (!SmartArtCommandPlanner.CanEdit(selected))
                return;

            if (context.SelectedValue is { } nodeText)
            {
                if (SmartArtCommandPlanner.BuildEditedContent(selected!.Kind, nodeText) is { } replacement)
                    editor.ReplaceSelectedSmartArt(replacement);
                return;
            }

            openDialog?.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.CanEdit(editor.SelectedFloatingSmartArt()));
    }

    private sealed class SmartArtStyleRibbonCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled || SmartArtCommandPlanner.ResolveStyle(context.SelectedValue) is not { } style)
                return;
            editor.SetSmartArtStyle(style);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.CanEdit(editor.SelectedFloatingSmartArt()));
    }

    private sealed class ChartQuickLayoutRibbonCommand(
        DocumentView editor,
        ChartQuickLayout layout) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                editor.SetChartQuickLayout(layout);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.GetSelectedChartInfo() is not null);
    }

    private static bool TryBuildChartDataPreset(string? value, out Chart chart)
    {
        chart = new Chart();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        chart = value.Trim() switch
        {
            "Quarterly Sales" => Chart.Create(
                ChartKind.Column,
                ["Q1", "Q2", "Q3", "Q4"],
                [12.0, 18.0, 16.0, 24.0],
                seriesName: "Sales",
                title: "Quarterly Sales"),
            "Monthly Revenue" => Chart.Create(
                ChartKind.Line,
                ["Jan", "Feb", "Mar"],
                [5.0, 6.0, 7.0],
                seriesName: "Revenue",
                title: "Monthly Revenue"),
            _ => null!
        };

        return chart is not null;
    }

    private static void RegisterSmartArtLayoutPreset(
        RibbonCommandRegistry registry,
        DocumentView editor,
        string commandId,
        string layoutId)
    {
        if (SmartArtLayoutPreset.FindById(layoutId) is { } preset)
            registry.Register(commandId, new ActionRibbonCommand(() => editor.SetSmartArtLayout(preset)));
        else
            registry.Register(commandId, EmptyRibbonCommand.Instance);
    }

    private static bool TryParseChartSize(string? value, out double widthPt, out double heightPt)
    {
        widthPt = 0;
        heightPt = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(['x', 'X'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], CultureInfo.InvariantCulture, out widthPt)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out heightPt)
            && widthPt > 0
            && heightPt > 0;
    }

    private sealed class ChartSizeCommand(DocumentView editor, Action? openDialog) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;

            if (TryParseChartSize(context.SelectedValue, out var widthPt, out var heightPt))
                editor.SetSelectedChartSize(widthPt, heightPt);
            else if (string.IsNullOrWhiteSpace(context.SelectedValue))
                openDialog?.Invoke();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedFloatingChart() is not null);
    }

    /// <summary>
    /// AV-MAIL: Registers the Mailings-tab commands over the portable <see cref="MailMerge"/> engine. The
    /// in-scope subset is: Select Recipients (load a CSV recipient list), Insert Merge Field (insert a
    /// «Field» placeholder at the caret), Address Block / Greeting Line (insert the composite placeholders),
    /// Preview Results (toggle a live preview of record 1) with Next / Previous record stepping, and
    /// Finish &amp; Merge (merge to a new in-memory document), and Send E-mail Messages planning (no delivery).
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
        r.Register("freew.merge-envelopes", new ActionRibbonCommand(engine.ApplyDefaultEnvelope));
        r.Register("freew.merge-labels", new ActionRibbonCommand(engine.ApplyDefaultLabels));
        r.Register("freew.start-mail-merge", new ActionRibbonCommand(engine.StartMailMergeLetters));
        r.Register("freew.start-mail-merge-letters", new ActionRibbonCommand(engine.StartMailMergeLetters));
        r.Register("freew.start-mail-merge-directory", new ActionRibbonCommand(engine.StartMailMergeDirectory));
        r.Register("freew.start-mail-merge-normal", new ActionRibbonCommand(engine.ClearMergeSession));
        RegisterMailingsAlias(r, "freew.merge-data", new ActionRibbonCommand(engine.SelectRecipients),
            "freew.select-recipients");
        r.Register("freew.merge-edit-recipients", new ActionRibbonCommand(engine.SelectRecipients));
        r.Register("freew.merge-field", new ActionRibbonCommand(engine.InsertMergeField));
        RegisterMailingsAlias(r, "freew.merge-address-block", new ActionRibbonCommand(engine.InsertAddressBlock),
            "freew.address-block");
        RegisterMailingsAlias(r, "freew.merge-greeting-line", new ActionRibbonCommand(engine.InsertGreetingLine),
            "freew.greeting-line");
        r.Register("freew.merge-match-fields", new ActionRibbonCommand(engine.MatchFields));
        r.Register("freew.merge-filter-sort", new ActionRibbonCommand(engine.FilterSortRecipients));
        r.Register("freew.merge-rules", EmptyRibbonCommand.Instance);
        r.Register("freew.merge-rule-if", new ActionRibbonCommand(engine.InsertIfRule));
        r.Register("freew.merge-rule-skip-record-if", new ActionRibbonCommand(engine.InsertSkipRecordIfRule));
        r.Register("freew.merge-rule-next-record-if", new ActionRibbonCommand(engine.InsertNextRecordIfRule));
        r.Register("freew.merge-next-record", new ActionRibbonCommand(engine.InsertNextRecordField));
        r.Register("freew.merge-record-number", new ActionRibbonCommand(engine.InsertMergeRecordNumberField));
        r.Register("freew.merge-sequence-number", new ActionRibbonCommand(engine.InsertMergeSequenceNumberField));
        r.Register("freew.merge-rule-fill-in", new ActionRibbonCommand(engine.InsertFillInRule));
        r.Register("freew.merge-rule-ask", new ActionRibbonCommand(engine.InsertAskRule));
        r.Register("freew.merge-rule-set", new ActionRibbonCommand(engine.InsertSetRule));
        r.Register("freew.merge-rule-ref", new ActionRibbonCommand(engine.InsertRefRule));
        RegisterMailingsAlias(r, "freew.merge-preview", new ActionRibbonCommand(engine.TogglePreview),
            "freew.preview-results");
        r.Register("freew.merge-preview-first", new ActionRibbonCommand(engine.FirstRecord));
        RegisterMailingsAlias(r, "freew.merge-preview-next", new ActionRibbonCommand(engine.NextRecord),
            "freew.next-record");
        RegisterMailingsAlias(r, "freew.merge-preview-previous", new ActionRibbonCommand(engine.PreviousRecord),
            "freew.prev-record");
        r.Register("freew.merge-preview-last", new ActionRibbonCommand(engine.LastRecord));
        // MainWindow replaces these with owner-modal dialogs; keep definition parity for headless hosts.
        r.Register("freew.merge-find-recipient", new ActionRibbonCommand(() => { }));
        r.Register("freew.merge-check-errors", new ActionRibbonCommand(() => { }));
        RegisterMailingsAlias(r, "freew.merge-finish", new ActionRibbonCommand(() => engine.FinishMerge()),
            "freew.finish-merge");
        r.Register("freew.merge-email", new ActionRibbonCommand(() => engine.PlanEmailMerge()));
    }

    private static void RegisterMailingsAlias(
        RibbonCommandRegistry r,
        string canonicalId,
        IRibbonCommand command,
        params string[] aliases)
    {
        r.Register(canonicalId, command);
        foreach (var alias in aliases)
            r.Register(alias, command);
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
        r.Register("freew.customize-colors", new ActionRibbonCommand(callbacks.OpenCustomizeThemeColorsDialog ?? (() => { })));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme-colors.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyThemeColors(t)));
        }

        // ── Fonts (heading/body pairing — preserves colours) ─────────────────
        r.Register("freew.theme-fonts", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.customize-fonts", new ActionRibbonCommand(callbacks.OpenCustomizeThemeFontsDialog ?? (() => { })));
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
        r.Register("freew.custom-paragraph-spacing",
            new ActionRibbonCommand(callbacks.OpenCustomParagraphSpacingDialog ?? (() => { })));

        r.Register("freew.theme-effects", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        for (var index = 0; index < DocumentEffectSet.Catalog.Count; index++)
        {
            var effectSet = DocumentEffectSet.Catalog[index];
            r.Register(FreeWContextMenuPlanner.EffectsPrefix + index,
                new ActionRibbonCommand(() => editor.ApplyEffectSet(effectSet)));
        }

        // ── Page Color swatches (+ No Color) ─────────────────────────────────
        r.Register("freew.style-set", new ValueRibbonCommand(value =>
        {
            if (!string.IsNullOrWhiteSpace(value) && DocumentStyleSet.FindByName(value) is { } styleSet)
                editor.ApplyStyleSet(styleSet);
        }));
        r.Register("freew.reset-style-set", new ActionRibbonCommand(() => editor.ApplyStyleSet(DocumentStyleSet.Default)));

        r.Register("freew.page-color", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.page-color.more", new ActionRibbonCommand(callbacks.OpenPageColorDialog ?? (() => { })));
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

    private static void ExecuteSortCommand(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        if (callbacks.OpenSortDialog is not null)
        {
            callbacks.OpenSortDialog();
            return;
        }

        if (editor.IsCaretInTable())
            editor.SortCaretTableRows(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);
        else
            editor.SortSelectedParagraphs(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);
    }
}
