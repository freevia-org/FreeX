using System.Globalization;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
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
    private static IRibbonCommand HostCommand(Action? action) =>
        action is null ? FreeWRibbonExecutionProfile.UnavailableCommand : new ActionRibbonCommand(action);

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

        var r = new FreeWRibbonCommandBindingPorts();
        mailMerge = new MailMergeEngine(editor, callbacks);
        FreeWRibbonHostExecutionProfile.Register(r, callbacks, registerFileAdapterCommands: true);

        // ── File ─────────────────────────────────────────────────────────────

        // ── Clipboard ────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.FormatPainter, new FreeWRibbonFormatPainterCommand(locked =>
        {
            editor.ArmFormatPainter(locked);
            editor.Focus();
        }));

        // ── Font ─────────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.FontFamily, new FontFamilyCommand(editor));
        r.Bind(FreeWRibbonCommandAction.FontSize, new FontSizeCommand(editor));
        r.Bind(FreeWRibbonCommandAction.Bold,            new ActionRibbonCommand(editor.ToggleBold));
        r.Bind(FreeWRibbonCommandAction.Italic,           new ActionRibbonCommand(editor.ToggleItalic));
        r.Bind(FreeWRibbonCommandAction.Underline,        new ActionRibbonCommand(editor.ToggleUnderline));
        r.Bind(FreeWRibbonCommandAction.Strikethrough,    new ActionRibbonCommand(editor.ToggleStrikethrough));
        r.Bind(FreeWRibbonCommandAction.Smallcaps,        new ActionRibbonCommand(editor.ToggleSmallCaps));
        r.Bind(FreeWRibbonCommandAction.Allcaps,          new ActionRibbonCommand(editor.ToggleAllCaps));
        r.Bind(FreeWRibbonCommandAction.Superscript,      new ActionRibbonCommand(editor.ToggleSuperscript));
        r.Bind(FreeWRibbonCommandAction.Subscript,        new ActionRibbonCommand(editor.ToggleSubscript));
        r.Bind(FreeWRibbonCommandAction.Highlight,        new ValueRibbonCommand(value => editor.SetHighlightColor(value)));
        RegisterHighlightPalette(r, editor);
        RegisterCharacterBorderPalette(r, editor);
        RegisterCharacterShadingPalette(r, editor);
        r.Bind(FreeWRibbonCommandAction.GrowFont,        new ActionRibbonCommand(editor.GrowFont));
        r.Bind(FreeWRibbonCommandAction.ShrinkFont,      new ActionRibbonCommand(editor.ShrinkFont));
        r.Bind(FreeWRibbonCommandAction.ClearFormatting, new ActionRibbonCommand(editor.ClearFormatting));
        // Font Color — the ribbon control is a Dropdown whose button click opens the colour flyout.
        // Each palette entry is its own command so the button never executes with a null value.
        // "freew.font-color" itself is registered as a no-op so the registry completeness check
        // (which checks every ribbon control's CommandId) continues to pass.
        r.Bind(FreeWRibbonCommandAction.FontColor, new ActionRibbonCommand(() => { /* flyout opener — no direct action */ }));
        RegisterFontColorPalette(r, editor);

        r.Bind(FreeWRibbonCommandAction.ChangeCase,   new ActionRibbonCommand(editor.ChangeCase));
        // Dialog launchers — open modal dialogs via shell callbacks (no direct editor method).

        // ── Paragraph ────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.Bullets,          new ActionRibbonCommand(() => editor.ToggleList(ListKind.Bullet)));
        r.Bind(FreeWRibbonCommandAction.Numbering,        new ActionRibbonCommand(() => editor.ToggleList(ListKind.Number)));
        r.Bind(FreeWRibbonCommandAction.AlignLeft,       new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Left)));
        r.Bind(FreeWRibbonCommandAction.AlignCenter,     new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Center)));
        r.Bind(FreeWRibbonCommandAction.AlignRight,      new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Right)));
        r.Bind(FreeWRibbonCommandAction.AlignJustify,    new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Justify)));
        r.Bind(FreeWRibbonCommandAction.MultilevelList, new ActionRibbonCommand(() =>
            editor.ApplyMultiLevelListDefinition(MultilevelListDialogPlanner.DefaultDefinition)));
        r.Bind(FreeWRibbonCommandAction.MultilevelDemote, new ActionRibbonCommand(() => ChangeListLevel(editor, demote: true)));
        r.Bind(FreeWRibbonCommandAction.MultilevelPromote, new ActionRibbonCommand(() => ChangeListLevel(editor, demote: false)));
        foreach (var preset in MultilevelListDialogPlanner.Presets)
        {
            var capturedPreset = preset;
            r.Register(capturedPreset.CommandId, new ActionRibbonCommand(() =>
                editor.ApplyMultiLevelListDefinition(capturedPreset.Definition)));
        }
        r.Bind(FreeWRibbonCommandAction.MultilevelDefine, new ActionRibbonCommand(
            callbacks.OpenMultilevelListDialog ?? (() =>
            {
                editor.ApplyMultiLevelListToSelection();
                editor.ApplyMultiLevelListStartOverrides(level0StartAt: 1, level1StartAt: 1);
            })));
        r.Bind(FreeWRibbonCommandAction.IndentIncrease,  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Bind(FreeWRibbonCommandAction.IndentDecrease,  new ActionRibbonCommand(editor.DecreaseIndent));
        r.Register("freew.increase-indent",  new ActionRibbonCommand(editor.IncreaseIndent));
        r.Register("freew.decrease-indent",  new ActionRibbonCommand(editor.DecreaseIndent));
        r.Bind(FreeWRibbonCommandAction.IndentLeft, new ParagraphValueCommand(
            editor,
            pt => editor.SetIndents(leftPt: pt),
            paragraph => paragraph.IndentLeftPt));
        r.Bind(FreeWRibbonCommandAction.IndentRight, new ParagraphValueCommand(
            editor,
            pt => editor.SetIndents(rightPt: pt),
            paragraph => paragraph.IndentRightPt));
        var formattingMarks = r.BindToggle(FreeWRibbonCommandAction.FormattingMarks,
            () => editor.ShowParagraphMarks = !editor.ShowParagraphMarks,
            () => editor.ShowParagraphMarks);
        r.Register("freew.show-hide-para", formattingMarks);
        // Paragraph spacing commands (value = points as an invariant-culture decimal string).
        r.Bind(FreeWRibbonCommandAction.SpaceBefore, new ParagraphValueCommand(
            editor,
            editor.SetSpaceBefore,
            paragraph => paragraph.SpaceBeforePt));
        r.Bind(FreeWRibbonCommandAction.SpaceAfter, new ParagraphValueCommand(
            editor,
            editor.SetSpaceAfter,
            paragraph => paragraph.SpaceAfterPt));
        r.Bind(FreeWRibbonCommandAction.SpaceBeforeToggle, new ActionRibbonCommand(() => ToggleSpaceBefore(editor)));
        r.Bind(FreeWRibbonCommandAction.SpaceAfterToggle, new ActionRibbonCommand(() => ToggleSpaceAfter(editor)));
        r.Bind(FreeWRibbonCommandAction.KeepWithNext, new ActionRibbonCommand(editor.ToggleKeepWithNext));
        r.Bind(FreeWRibbonCommandAction.KeepLines, new ActionRibbonCommand(editor.ToggleKeepLinesTogether));
        r.Bind(FreeWRibbonCommandAction.WidowControl, new ActionRibbonCommand(editor.ToggleWidowControl));
        r.Bind(FreeWRibbonCommandAction.ParaBorder, new ActionRibbonCommand(() => editor.ToggleParagraphBorder()));
        r.Bind(FreeWRibbonCommandAction.ParaShading, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterParagraphShadingPalette(r, editor);
        r.Bind(FreeWRibbonCommandAction.Sort, new ActionRibbonCommand(() => ExecuteSortCommand(editor, callbacks)));
        // Line-spacing commands — value = multiplier for Multiple. The fixed ids are compatibility
        // aliases for older Avalonia controls and are no longer used by the Home ribbon profile.
        r.Bind(FreeWRibbonCommandAction.LineSpacing, new FreeWRibbonNumericValueCommand(
            spacing => editor.SetLineSpacing(LineSpacingRule.Multiple, spacing),
            () => editor.GetCaretFormatting().Paragraph.LineSpacing,
            minimumExclusive: 0));
        r.Register("freew.line-spacing-1",    new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.0)));
        r.Register("freew.line-spacing-115",  new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.15)));
        r.Register("freew.line-spacing-15",   new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 1.5)));
        r.Register("freew.line-spacing-2",    new ActionRibbonCommand(() => editor.SetLineSpacing(LineSpacingRule.Multiple, 2.0)));
        // Paragraph dialog launcher.

        // ── Styles (AV-STYLES) ────────────────────────────────────────────────
        // Existing quick-style buttons — now routed through the model-backed, undoable ApplyNamedStyle
        // so the paragraph picks up the real built-in style (seeded if absent) instead of just a font tweak.
        r.Bind(FreeWRibbonCommandAction.Style, new ParagraphStyleCommand(editor));
        r.Bind(FreeWRibbonCommandAction.StyleNormal,   new ActionRibbonCommand(() => editor.ApplyNamedStyle("Normal")));
        r.Bind(FreeWRibbonCommandAction.StyleHeading1, new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading1")));
        r.Bind(FreeWRibbonCommandAction.StyleHeading2, new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading2")));
        r.Bind(FreeWRibbonCommandAction.StyleHeading3, new ActionRibbonCommand(() => editor.ApplyNamedStyle("Heading3")));
        r.Bind(FreeWRibbonCommandAction.StyleTitle,    new ActionRibbonCommand(() => editor.ApplyNamedStyle("Title")));

        // Styles gallery dropdown — opener no-op; one freew.style.<id> command per built-in style applies
        // that named style (paragraph styles set StyleId; character styles overlay run formatting).
        r.Register("freew.styles-gallery", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterStyleGalleryCommands(r, editor);

        // Clear style — revert the paragraph to the document default (Word's paragraph-level reset).
        r.Bind(FreeWRibbonCommandAction.StyleClear, new ActionRibbonCommand(editor.ClearParagraphStyle));

        // ── Editing ──────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.Undo,              new ActionRibbonCommand(editor.Undo));
        r.Bind(FreeWRibbonCommandAction.Redo,              new ActionRibbonCommand(editor.Redo));
        r.Bind(FreeWRibbonCommandAction.Select,            new ActionRibbonCommand(editor.SelectAll));
        r.Register("freew.select-all",        new ActionRibbonCommand(editor.SelectAll));

        // ── Insert ───────────────────────────────────────────────────────────
        // AV-INSERT: Insert-tab depth. Table dropdown (default + sized presets), page break, picture
        // (file-picker via host callback), shape, text box, and a symbol palette.
        r.Register("freew.insert-table", new ActionRibbonCommand(() => editor.InsertTable(3, 3)));
        // Match WPF's primary face: clicking the Table dropdown button inserts a 2x2 table;
        // clicking its arrow still exposes the sized presets below.
        r.Bind(FreeWRibbonCommandAction.Table, new ActionRibbonCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-2x2", new ActionRibbonCommand(() => editor.InsertTable(2, 2)));
        r.Register("freew.table-3x3", new ActionRibbonCommand(() => editor.InsertTable(3, 3)));
        r.Register("freew.table-4x4", new ActionRibbonCommand(() => editor.InsertTable(4, 4)));
        r.Register("freew.table-5x2", new ActionRibbonCommand(() => editor.InsertTable(2, 5)));

        // Page break — empty paragraph forcing a page break before it, after the caret block.
        r.Bind(FreeWRibbonCommandAction.PageBreak, new ActionRibbonCommand(editor.InsertPageBreak));
        r.Bind(FreeWRibbonCommandAction.BlankPage, new ActionRibbonCommand(editor.InsertBlankPage));
        r.Bind(FreeWRibbonCommandAction.HorizontalRule, new ActionRibbonCommand(editor.InsertHorizontalRule));

        // Picture — open a file picker, load the bytes, insert as an inline image (host callback).

        // Shape / Text Box — floating drawing objects at the caret.
        r.Register("freew.shape",    new ActionRibbonCommand(editor.InsertShape));
        r.Register("freew.text-box", new ActionRibbonCommand(editor.InsertTextBox));

        RegisterSymbolPalette(r, editor);

        // Header / Footer — match WPF's text prompt when the shell supplies it. The fallback keeps
        // headless registry callers deterministic and retains the old region-creation behavior.
        r.Bind(FreeWRibbonCommandAction.Header, HeaderFooterTextCommand(editor, callbacks, footer: false));
        r.Bind(FreeWRibbonCommandAction.Footer, HeaderFooterTextCommand(editor, callbacks, footer: true));
        r.Bind(FreeWRibbonCommandAction.PageNumber, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Bind(FreeWRibbonCommandAction.PageNumberTop, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        r.Bind(FreeWRibbonCommandAction.PageNumberBottom, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Bind(FreeWRibbonCommandAction.PageNumberCurrent, new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.PageNumber)));
        r.Bind(FreeWRibbonCommandAction.PageNumberFormat, new ContextRibbonCommand(
            context => ExecutePageNumberFormat(editor, callbacks, context)));
        r.Bind(FreeWRibbonCommandAction.Datetime, new ActionRibbonCommand(
            callbacks.OpenDateTimeDialog ?? (() => editor.InsertField(RunFieldKind.Date))));
        RegisterHeaderFooterCommands(r, editor);

        // ── Insert depth 2 (AV-INSERT2) ──────────────────────────────────────
        RegisterInsertDepth2Commands(r, editor, callbacks);

        // ── Developer ────────────────────────────────────────────────────────
        RegisterDeveloperControls(r, editor);

        // ── Table Design contextual tab ───────────────────────────────────────
        // Table Style Options toggles — DocumentView guards no-op when outside a table.
        r.Bind(FreeWRibbonCommandAction.TableHeaderRow,  new ActionRibbonCommand(editor.ToggleTableHeaderRow));
        r.Bind(FreeWRibbonCommandAction.TableBandedRows, new ActionRibbonCommand(editor.ToggleBandedRows));
        r.Bind(FreeWRibbonCommandAction.TableLastRow, new ActionRibbonCommand(editor.ToggleTableLastRow));
        r.Bind(FreeWRibbonCommandAction.TableFirstColumn, new ActionRibbonCommand(editor.ToggleTableFirstColumn));
        r.Bind(FreeWRibbonCommandAction.TableLastColumn, new ActionRibbonCommand(editor.ToggleTableLastColumn));
        r.Bind(FreeWRibbonCommandAction.TableBandedCols, new ActionRibbonCommand(editor.ToggleTableBandedColumns));

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
        r.Bind(FreeWRibbonCommandAction.Eraser, new ActionRibbonCommand(editor.EraseTableBorderAtCaret));

        // ── Table Layout contextual tab ───────────────────────────────────────
        // Selection helpers.
        r.Bind(FreeWRibbonCommandAction.TableViewGridlines, new ActionRibbonCommand(() =>
        {
            editor.ViewTableGridlines = !editor.ViewTableGridlines;
        }));
        IRibbonCommand tablePropertiesCommand = callbacks.OpenTablePropertiesDialog is { } openTableProperties
            ? new TablePropertiesCommand(editor, openTableProperties)
            : FreeWRibbonExecutionProfile.UnavailableCommand;
        r.Bind(FreeWRibbonCommandAction.TableProperties, tablePropertiesCommand);
        r.Bind(FreeWRibbonCommandAction.TableSelectTable, new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                // BY1: clamp to actual table bounds — passing int.MaxValue triggers an overflow
                // loop in ExpandForMergedCells (r++ overflows int.MaxValue → infinite loop).
                var (lastRow, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, 0, lastRow, lastGridCol);
            }
        }));
        r.Bind(FreeWRibbonCommandAction.TableSelectRow, new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (_, lastGridCol) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, 0, cc.Row, lastGridCol);
            }
        }));
        r.Bind(FreeWRibbonCommandAction.TableSelectCol, new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
            {
                var (lastRow, _) = editor.GetTableBounds(cc.TableBlock);
                editor.SetCellBlockSelection(cc.TableBlock, 0, cc.Col, lastRow, cc.Col);
            }
        }));
        r.Bind(FreeWRibbonCommandAction.TableSelectCell, new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.SetCellBlockSelection(cc.TableBlock, cc.Row, cc.Col, cc.Row, cc.Col);
        }));

        // Row / column mutations.
        r.Bind(FreeWRibbonCommandAction.TableInsertAbove,     new ActionRibbonCommand(editor.InsertTableRowAbove));
        r.Register("freew.table-insert-below",     new ActionRibbonCommand(editor.InsertTableRowBelow));
        r.Bind(FreeWRibbonCommandAction.TableInsertColLeft,  new ActionRibbonCommand(editor.InsertTableColumnLeft));
        r.Register("freew.table-insert-col-right", new ActionRibbonCommand(editor.InsertTableColumnRight));
        r.Bind(FreeWRibbonCommandAction.TableDeleteRow,       new ActionRibbonCommand(editor.DeleteTableRow));
        r.Bind(FreeWRibbonCommandAction.TableDeleteCol,       new ActionRibbonCommand(editor.DeleteTableColumn));
        r.Bind(FreeWRibbonCommandAction.TableDelete,           new ActionRibbonCommand(() =>
        {
            if (editor.CellCaretInfo is { } cc)
                editor.DeleteTableBlock(cc.TableBlock);
        }));

        // Merge / split.
        r.Register("freew.table-merge-cells", new ActionRibbonCommand(editor.MergeSelectedCells));
        r.Register("freew.table-split-cell",  new ActionRibbonCommand(() => editor.SplitCurrentCell()));
        r.Bind(FreeWRibbonCommandAction.SplitTable, new ActionRibbonCommand(editor.SplitTable));

        // Cell size.
        r.Bind(FreeWRibbonCommandAction.TableRowHeight, tablePropertiesCommand);
        r.Bind(FreeWRibbonCommandAction.TableColWidth, tablePropertiesCommand);
        r.Bind(FreeWRibbonCommandAction.TableDistributeRows, new ActionRibbonCommand(editor.DistributeTableRows));
        r.Bind(FreeWRibbonCommandAction.TableDistributeCols, new ActionRibbonCommand(editor.DistributeTableColumns));
        r.Bind(FreeWRibbonCommandAction.TableAutofitContents, new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Contents)));
        r.Bind(FreeWRibbonCommandAction.TableAutofitWindow, new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Window)));
        r.Bind(FreeWRibbonCommandAction.TableAutofitFixed, new ActionRibbonCommand(() => editor.SetTableAutoFit(AutoFitMode.Fixed)));

        // Cell alignment — 9 = 3 vertical (Top/Center/Bottom) × 3 horizontal (Left/Center/Right).
        // BY2: parity with WPF's table-layout Alignment group (FreeWRibbon.cs ~1201-1219).
        RegisterCellAlignmentCommands(r, editor);
        r.Bind(FreeWRibbonCommandAction.TableCellMargins, tablePropertiesCommand);
        r.Bind(FreeWRibbonCommandAction.CellTextDirectionHorizontal, new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Horizontal)));
        r.Bind(FreeWRibbonCommandAction.CellTextDirectionRotate90, new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Rotate90)));
        r.Bind(FreeWRibbonCommandAction.CellTextDirectionRotate270, new ActionRibbonCommand(() => editor.SetCaretCellTextDirection(CellTextDirection.Rotate270)));

        // Data.
        r.Bind(FreeWRibbonCommandAction.TableRepeatHeader, new ActionRibbonCommand(editor.ToggleTableRepeatHeaderRow));
        r.Bind(FreeWRibbonCommandAction.TableFormula, callbacks.OpenTableFormulaDialog is { } openTableFormula
            ? new TableFormulaCommand(editor, openTableFormula)
            : FreeWRibbonExecutionProfile.UnavailableCommand);
        r.Bind(FreeWRibbonCommandAction.TableToText, new TableToTextCommand(editor, callbacks));

        // ── Layout / Page Setup (AV-PAGE) ────────────────────────────────────
        // Dialog launcher: opens the Page Setup modal (margins + paper + orientation).
        // Toggle orientation (portrait ↔ landscape).
        var orientationCommand = new HostPageSettingCommand(editor, callbacks.ToggleOrientation);
        r.Bind(FreeWRibbonCommandAction.Orientation, orientationCommand);
        r.Register("freew.page-orientation", orientationCommand);
        // Margin presets.
        r.Bind(FreeWRibbonCommandAction.Margins, new HostPageSettingCommand(editor, () => ToggleNormalNarrowMargins(editor, callbacks)));
        r.Register("freew.page-margins-normal", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("normal")));
        r.Register("freew.page-margins-narrow", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("narrow")));
        r.Register("freew.page-margins-wide", new HostPageSettingCommand(editor, () => callbacks.ApplyMarginPreset("wide")));
        // Quick paper-size selectors.
        r.Bind(FreeWRibbonCommandAction.Size, new HostPageSettingCommand(editor, () => ToggleLetterA4Paper(editor, callbacks)));
        r.Register("freew.page-size-letter", new HostPageSettingCommand(editor, () => callbacks.ApplyPaperSize("letter")));
        r.Register("freew.page-size-a4", new HostPageSettingCommand(editor, () => callbacks.ApplyPaperSize("a4")));

        var columnsDialogCommand = new ActionRibbonCommand(callbacks.OpenColumnsDialog ?? (() => { }));
        r.Bind(FreeWRibbonCommandAction.Columns, columnsDialogCommand);
        r.Bind(FreeWRibbonCommandAction.ColumnsMore, columnsDialogCommand);
        r.Bind(FreeWRibbonCommandAction.ColumnsOne, new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.One),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.One)));
        r.Bind(FreeWRibbonCommandAction.ColumnsTwo, new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Two),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Two)));
        r.Bind(FreeWRibbonCommandAction.ColumnsThree, new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Three),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Three)));
        r.Bind(FreeWRibbonCommandAction.ColumnsLeft, new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Left),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Left)));
        r.Bind(FreeWRibbonCommandAction.ColumnsRight, new PageSettingCommand(editor,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Right),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Right)));

        r.Register("freew.breaks", EmptyRibbonCommand.Instance);
        r.Bind(FreeWRibbonCommandAction.ColumnBreak, new ActionRibbonCommand(editor.InsertColumnBreak));
        r.Bind(FreeWRibbonCommandAction.SectionBreakNextPage, new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.NextPage)));
        r.Bind(FreeWRibbonCommandAction.SectionBreakContinuous, new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.Continuous)));
        r.Bind(FreeWRibbonCommandAction.SectionBreakEvenPage, new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.EvenPage)));
        r.Bind(FreeWRibbonCommandAction.SectionBreakOddPage, new ActionRibbonCommand(() => editor.InsertSectionBreak(SectionBreakKind.OddPage)));
        r.Bind(FreeWRibbonCommandAction.LineNumbers, new PageSettingCommand(editor, PageLayoutCommandPlanner.CycleLineNumberMode));
        r.Bind(FreeWRibbonCommandAction.LineNumbersNone, new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.None, page => page.LineNumberMode == LineNumberMode.None));
        r.Bind(FreeWRibbonCommandAction.LineNumbersContinuous, new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.Continuous, page => page.LineNumberMode == LineNumberMode.Continuous));
        r.Bind(FreeWRibbonCommandAction.LineNumbersRestartPage, new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.RestartEachPage, page => page.LineNumberMode == LineNumberMode.RestartEachPage));
        r.Bind(FreeWRibbonCommandAction.LineNumbersRestartSection, new PageSettingCommand(editor, page => page.LineNumberMode = LineNumberMode.RestartEachSection, page => page.LineNumberMode == LineNumberMode.RestartEachSection));
        r.Bind(FreeWRibbonCommandAction.Hyphenation, new PageSettingCommand(editor, PageLayoutCommandPlanner.ToggleHyphenation, page => page.AutoHyphenation));
        r.Bind(FreeWRibbonCommandAction.HyphenationNone, new PageSettingCommand(editor, page => page.AutoHyphenation = false, page => !page.AutoHyphenation));
        r.Bind(FreeWRibbonCommandAction.HyphenationAuto, new PageSettingCommand(editor, page => page.AutoHyphenation = true, page => page.AutoHyphenation));
        r.Bind(FreeWRibbonCommandAction.DifferentFirstPage, new PageSettingCommand(editor, page => page.DifferentFirstPage = !page.DifferentFirstPage, page => page.DifferentFirstPage));
        r.Bind(FreeWRibbonCommandAction.PageValign, new ActionRibbonCommand(editor.CyclePageVerticalAlignment));
        r.Bind(FreeWRibbonCommandAction.TextToTable, new ActionRibbonCommand(
            callbacks.OpenTextToTableDialog ?? editor.ConvertCurrentParagraphToTable));

        ViewRibbonWorkflow.Register(
            r,
            new ViewRibbonCommandBindings(
                PrintPreview: new ViewRibbonActionBinding(callbacks.OpenPrintPreview ?? (static () => { })),
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: callbacks.ToggleReadMode is { } toggle && callbacks.IsReadModeActive is { } isActive
                        ? new ViewRibbonToggleBinding(toggle, isActive)
                        : new ViewRibbonToggleBinding(FallbackCommand: FreeWRibbonExecutionProfile.UnavailableCommand),
                    ColumnWidth: new ViewRibbonChoiceBinding(
                        callbacks.ApplyReadModeColumnWidth,
                        FreeWRibbonExecutionProfile.UnavailableCommand),
                    PageColor: new ViewRibbonChoiceBinding(
                        callbacks.ApplyReadModePageColor,
                        FreeWRibbonExecutionProfile.UnavailableCommand)),
                Modes: new ViewRibbonModeBindings(
                    PrintLayout: new ViewRibbonToggleBinding(
                        callbacks.SetPrintLayout,
                        callbacks.IsPrintLayoutActive ??
                            (() => editor.ViewMode == DocumentViewMode.PrintLayout)),
                    WebLayout: new ViewRibbonToggleBinding(
                        callbacks.SetWebLayout,
                        callbacks.IsWebLayoutActive ??
                            (() => editor.ViewMode == DocumentViewMode.WebLayout)),
                    Draft: new ViewRibbonToggleBinding(
                        callbacks.SetDraftView,
                        callbacks.IsDraftViewActive ??
                            (() => editor.ViewMode == DocumentViewMode.Draft)),
                    Outline: new ViewRibbonToggleBinding(
                        callbacks.SetOutlineView ?? (static () => { }),
                        callbacks.IsOutlineViewActive ?? (static () => false)),
                    PagedEdit: new ViewRibbonToggleBinding(
                        callbacks.TogglePagedEditView ?? callbacks.SetPrintLayout,
                        callbacks.IsPagedEditViewActive ?? (static () => false))),
                Show: new ViewRibbonShowBindings(
                    NavigationPane: new ViewRibbonToggleBinding(
                        callbacks.ToggleNavigationPane,
                        callbacks.IsNavigationPaneVisible ?? (static () => false)),
                    RevealFormatting: new ViewRibbonToggleBinding(
                        callbacks.ToggleRevealFormatting,
                        callbacks.IsRevealFormattingVisible ?? (static () => false)),
                    Gridlines: new ViewRibbonToggleBinding(
                        () => editor.ShowGridlines = !editor.ShowGridlines,
                        () => editor.ShowGridlines),
                    Ruler: new ViewRibbonToggleBinding(
                        () => editor.ShowRuler = !editor.ShowRuler,
                        () => editor.ShowRuler)),
                Zoom: new ViewRibbonZoomBindings(
                    Dialog: new ViewRibbonActionBinding(callbacks.OpenZoomDialog ?? (static () => { })),
                    ZoomIn: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(null, +0.1)),
                    ZoomOut: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(null, -0.1)),
                    Reset100: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(1.0, 0)),
                    OnePage: new ViewRibbonActionBinding(callbacks.ZoomOnePage ?? (static () => { })),
                    PageWidth: new ViewRibbonActionBinding(callbacks.ZoomPageWidth ?? (static () => { })),
                    MultiplePages: new ViewRibbonToggleBinding(
                        callbacks.ToggleMultiplePages ?? (static () => { }),
                        callbacks.IsMultiplePagesActive ?? (static () => false)),
                    SideToSide: new ViewRibbonToggleBinding(
                        callbacks.ToggleSideToSide ?? (static () => { }),
                        callbacks.IsSideToSideActive ?? (static () => false))),
                Window: new ViewRibbonWindowBindings(
                    NewWindow: new ViewRibbonActionBinding(callbacks.NewWindow ?? (static () => { })),
                    ArrangeAll: new ViewRibbonActionBinding(callbacks.ArrangeAll ?? (static () => { })),
                    Split: new ViewRibbonToggleBinding(
                        callbacks.ToggleSplit ?? (static () => { }),
                        callbacks.IsSplitActive ?? (static () => false))),
                RegisterCompatibilityAliases: true));

        // ── Review ───────────────────────────────────────────────────────────
        var reviewingPaneCommand = r.BindToggle(FreeWRibbonCommandAction.ReviewingPane,
            callbacks.ToggleReviewingPane,
            callbacks.IsReviewingPaneVisible ?? (() => false));
        r.Register("freew.reviewingpane", reviewingPaneCommand);
        ReviewTrackingRibbonWorkflow.Register(
            r,
            new ReviewTrackingCommandBindings(
                PrepareExecution: static () => { },
                IsTrackChangesEnabled: () => editor.TrackChangesEnabled,
                HasSelection: () => editor.SelectedText.Length > 0,
                ToggleTrackChanges: () => editor.ToggleTrackChanges(),
                MarkSelectionAsInsertion: () => editor.MarkSelectionAsRevision(RevisionKind.Inserted),
                IsTrackFormattingEnabled: () => editor.TrackFormattingEnabled,
                ToggleTrackFormatting: () => editor.ToggleTrackFormatting(),
                GetDisplayForReview: () => editor.DisplayForReview,
                ApplyDisplayForReview: editor.ApplyDisplayForReview,
                ShowMarkupInsertionsAndDeletions: () => editor.ShowMarkupInsertionsAndDeletions,
                ApplyShowMarkupInsertionsAndDeletions: editor.ApplyShowMarkupInsertionsAndDeletions,
                ShowMarkupComments: () => editor.ShowMarkupComments,
                ApplyShowMarkupComments: editor.ApplyShowMarkupComments,
                ShowMarkupFormatting: () => editor.ShowMarkupFormatting,
                ApplyShowMarkupFormatting: editor.ApplyShowMarkupFormatting,
                AcceptAllRevisions: () => editor.AcceptAllRevisions(),
                RejectAllRevisions: () => editor.RejectAllRevisions()));
        r.Bind(FreeWRibbonCommandAction.ShowMarkupBalloons, new ShowMarkupBalloonsCommand(editor, callbacks));
        // Accept / reject the revision selected in the Reviewing Pane, matching WPF's selected-row
        // authority. Test-only or detached registries retain the caret-relative fallback.
        var acceptCurrentRevisionCommand = new ActionRibbonCommand(
            callbacks.AcceptThisChange ?? (() => editor.AcceptCurrentRevision()));
        var rejectCurrentRevisionCommand = new ActionRibbonCommand(
            callbacks.RejectThisChange ?? (() => editor.RejectCurrentRevision()));
        r.Bind(FreeWRibbonCommandAction.AcceptThis, acceptCurrentRevisionCommand);
        r.Register("freew.accept-change", acceptCurrentRevisionCommand);
        r.Bind(FreeWRibbonCommandAction.RejectThis, rejectCurrentRevisionCommand);
        r.Register("freew.reject-change", rejectCurrentRevisionCommand);
        // Comments — thread navigation/actions over the shared comment model.
        r.Bind(FreeWRibbonCommandAction.NewComment,    new ActionRibbonCommand(() => editor.NewComment()));
        r.Bind(FreeWRibbonCommandAction.DeleteComment, new ActionRibbonCommand(() => editor.DeleteCommentAtCaret()));
        r.Bind(FreeWRibbonCommandAction.PreviousComment, new ActionRibbonCommand(() => editor.PreviousComment()));
        r.Bind(FreeWRibbonCommandAction.NextComment, new ActionRibbonCommand(() => editor.NextComment()));
        r.Bind(FreeWRibbonCommandAction.ReplyComment, new ActionRibbonCommand(
            callbacks.ReplyComment ?? (() => editor.ReplyToCommentAtCaret())));
        r.Bind(FreeWRibbonCommandAction.ResolveComment, new ActionRibbonCommand(() => editor.ToggleResolveCommentAtCaret()));
        r.Bind(FreeWRibbonCommandAction.ShowComments, new ActionRibbonCommand(() =>
            callbacks.ShowComments?.Invoke(editor.PlannedCommentList())));
        // Word Count — opens the modal stats dialog (shell callback; reads DocumentStatistics).
        r.Bind(FreeWRibbonCommandAction.SpellcheckToggle, new ToggleActionCommand(
            callbacks.ToggleSpellcheck ?? (() => editor.ToggleSpellCheck()),
            callbacks.IsSpellcheckActive ?? (() => editor.SpellCheckEnabled)));
        r.Bind(FreeWRibbonCommandAction.AddToDictionary, new ActionRibbonCommand(
            callbacks.AddToDictionary ?? (() => editor.AddCurrentWordToDictionary())));
        r.Bind(FreeWRibbonCommandAction.SetProofingLanguage, new ProofingLanguageCommand(editor, callbacks));
        r.Bind(FreeWRibbonCommandAction.ReadAloud, new ToggleActionCommand(
            callbacks.ToggleReadAloud ?? (() => { }),
            callbacks.IsReadAloudActive ?? (() => false)));
        r.Bind(FreeWRibbonCommandAction.MarkAsFinal, new ToggleActionCommand(
            callbacks.MarkAsFinal ?? (() => editor.SetMarkedAsFinal(!editor.IsMarkedAsFinal)),
            () => ReviewProtectionStatePlanner.Build(editor.Document.Protection, editor.IsMarkedAsFinal)
                .MarkAsFinal.IsChecked));
        r.Bind(FreeWRibbonCommandAction.RestrictEditing, new ToggleActionCommand(
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

        return FreeWRibbonExecutionProfile.Build(r).Registry;
    }

    private const double ParagraphSpacingTogglePoints = 12.0;

    private static void ChangeListLevel(DocumentView editor, bool demote)
    {
        if (editor.GetCaretFormatting().Paragraph.ListKind == ListKind.None)
            return;

        if (demote)
            editor.IncreaseIndent();
        else
            editor.DecreaseIndent();
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

    private static void RegisterHeaderFooterCommands(IRibbonCommandRegistry r, DocumentView editor)
    {
        r.Bind(FreeWRibbonCommandAction.HfEditHeader, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("header")));
        r.Bind(FreeWRibbonCommandAction.HfEditFooter, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("footer")));
        r.Bind(FreeWRibbonCommandAction.HfEditFirstHeader, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("first-header")));
        r.Bind(FreeWRibbonCommandAction.HfEditFirstFooter, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("first-footer")));
        r.Bind(FreeWRibbonCommandAction.HfEditEvenHeader, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("even-header")));
        r.Bind(FreeWRibbonCommandAction.HfEditEvenFooter, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("even-footer")));

        r.Bind(FreeWRibbonCommandAction.HfGoToHeader, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("header")));
        r.Bind(FreeWRibbonCommandAction.HfGoToFooter, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot("footer")));
        r.Bind(FreeWRibbonCommandAction.HfClose, new ActionRibbonCommand(editor.CloseHeaderFooterEditing));

        r.Bind(FreeWRibbonCommandAction.HfDifferentFirstPage, new PageSettingCommand(
            editor,
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage));
        r.Bind(FreeWRibbonCommandAction.HfDifferentOddEven, new PageSettingCommand(
            editor,
            page => page.DifferentOddEvenPages = !page.DifferentOddEvenPages,
            page => page.DifferentOddEvenPages));

        r.Bind(FreeWRibbonCommandAction.HfHeaderFromTop, new HeaderFooterDistanceCommand(editor, footer: false));
        r.Bind(FreeWRibbonCommandAction.HfFooterFromBottom, new HeaderFooterDistanceCommand(editor, footer: true));

        r.Bind(FreeWRibbonCommandAction.HfInsertPageNumber, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        r.Bind(FreeWRibbonCommandAction.HfInsertPageNumberFooter, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        r.Bind(FreeWRibbonCommandAction.HfInsertDatetime, new ActionRibbonCommand(editor.InsertHeaderFooterDateTime));
        r.Bind(FreeWRibbonCommandAction.HfInsertField, new ActionRibbonCommand(editor.InsertHeaderFooterDocumentInfo));
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

    private static void RegisterDeveloperControls(IRibbonCommandRegistry r, DocumentView editor)
    {
        r.Bind(FreeWRibbonCommandAction.CcText, new ActionRibbonCommand(() => editor.InsertPlainTextControl()));
        r.Bind(FreeWRibbonCommandAction.CcRichtext, new ActionRibbonCommand(() => editor.InsertRichTextControl()));
        r.Bind(FreeWRibbonCommandAction.CcCheckbox, new ActionRibbonCommand(() => editor.InsertCheckBoxControl()));
        r.Bind(FreeWRibbonCommandAction.CcDate, new ActionRibbonCommand(() => editor.InsertDatePickerControl()));
        r.Bind(FreeWRibbonCommandAction.CcDropdown, new ActionRibbonCommand(() => editor.InsertDropDownListControl()));
        r.Bind(FreeWRibbonCommandAction.CcCombo, new ActionRibbonCommand(() => editor.InsertComboBoxControl()));
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

    private sealed class FontFamilyCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.SelectedValue))
                editor.SetSelectionFontFamily(context.SelectedValue);
        }

        public RibbonCommandState GetState() =>
            new(Value: editor.GetCaretFormatting().Run.FontFamily ?? "Calibri");
    }

    private sealed class FontSizeCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (double.TryParse(context.SelectedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var points)
                && points > 0)
            {
                editor.SetSelectionFontSize(points);
            }
        }

        public RibbonCommandState GetState() =>
            new(Value: (editor.GetCaretFormatting().Run.FontSizePt ?? 11)
                .ToString("0.##", CultureInfo.InvariantCulture));
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

    private sealed class ParagraphStyleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (string.IsNullOrWhiteSpace(context.SelectedValue))
                return;

            var descriptor = BuiltInStyles.Gallery.FirstOrDefault(style =>
                style.Type == StyleType.Paragraph
                && string.Equals(style.Name, context.SelectedValue, StringComparison.OrdinalIgnoreCase));
            if (descriptor is not null)
                editor.ApplyNamedStyle(descriptor.Id);
        }

        public RibbonCommandState GetState()
        {
            var styleId = editor.CurrentParagraphStyleId;
            if (string.IsNullOrWhiteSpace(styleId))
                return new(Value: "Normal");

            if (BuiltInStyles.Find(styleId) is { } builtIn)
                return new(Value: builtIn.Name);

            return editor.Document.Styles.TryGetValue(styleId, out var style)
                ? new RibbonCommandState(Value: style.Name)
                : new RibbonCommandState(Value: styleId);
        }
    }

    private sealed class ToggleActionCommand(Action toggle, Func<bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => toggle();

        public RibbonCommandState GetState() => new(IsChecked: isChecked());
    }

    private sealed class ThemeCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.SelectedValue)
                && DocumentTheme.FindByName(context.SelectedValue) is { } theme)
            {
                editor.ApplyTheme(theme);
            }
        }

        public RibbonCommandState GetState() => new(Value: editor.Document.Theme.Name);
    }

    private sealed class StyleSetCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.SelectedValue)
                && DocumentStyleSet.FindByName(context.SelectedValue) is { } styleSet)
            {
                editor.ApplyStyleSet(styleSet);
            }
        }

        public RibbonCommandState GetState() =>
            new(Value: DocumentStyleSet.FindMatching(editor.Document)?.Name);
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

    /// <summary>
    /// Registers the per-colour sub-commands for the Font Color palette dropdown.
    /// Each command id matches an entry in <see cref="FreeWRibbon.FontColors"/> and calls
    /// <see cref="DocumentView.SetFontColor"/> with the appropriate RRGGBB hex string
    /// (or <c>null</c> for the "Automatic" entry, which restores the default run colour).
    /// </summary>
    private static void RegisterFontColorPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        // Maps command-id suffix → CSS hex colour (null = automatic/default).
        // Colours chosen to match Word's standard palette.
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
    private static void RegisterParagraphShadingPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
    private static void RegisterCharacterShadingPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
    private static void RegisterCharacterBorderPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
    private static void RegisterHighlightPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
    private static void RegisterStyleGalleryCommands(IRibbonCommandRegistry r, DocumentView editor)
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
    private static void RegisterSymbolPalette(IRibbonCommandRegistry r, DocumentView editor)
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
        IRibbonCommandRegistry r, DocumentView editor, RibbonHostCallbacks callbacks)
    {
        // ── Links ────────────────────────────────────────────────────────────
        // Hyperlink / Bookmark open small dialogs (shell callbacks) that call the model-backed editor methods.
        r.Bind(FreeWRibbonCommandAction.Hyperlink,        new ActionRibbonCommand(callbacks.OpenHyperlinkDialog ?? (() => { })));
        r.Register("freew.insert-hyperlink", new ActionRibbonCommand(callbacks.OpenHyperlinkDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.EditHyperlink,   new ActionRibbonCommand(callbacks.OpenEditHyperlinkDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.RemoveHyperlink, new ActionRibbonCommand(editor.RemoveHyperlink));
        r.Bind(FreeWRibbonCommandAction.HyperlinkTooltip, new ActionRibbonCommand(callbacks.OpenHyperlinkTooltipDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.Bookmark,         new ActionRibbonCommand(callbacks.OpenBookmarkDialog ?? (() => { })));
        r.Register("freew.insert-bookmark",  new ActionRibbonCommand(callbacks.OpenBookmarkDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.LinkBookmark,    new ActionRibbonCommand(callbacks.OpenLinkBookmarkDialog ?? (() => LinkToFirstBookmark(editor))));
        r.Bind(FreeWRibbonCommandAction.BookmarkManager, new ActionRibbonCommand(
            callbacks.OpenBookmarkManagerDialog ?? callbacks.OpenBookmarkDialog ?? (() => { })));

        // ── Cover Page ───────────────────────────────────────────────────────
        // The split-button face inserts the WPF default; each preset prepends its cover-page block layout.
        r.Bind(FreeWRibbonCommandAction.CoverPage,         new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Default)));
        r.Register("freew.cover-page.default", new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Default)));
        r.Register("freew.cover-page.banded",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Banded)));
        r.Register("freew.cover-page.motion",  new ActionRibbonCommand(() => editor.InsertCoverPage(CoverPagePreset.Motion)));

        // ── Drop Cap ─────────────────────────────────────────────────────────
        // Dropped / In Margin both enlarge the leading letter (the in-margin float geometry is an
        // approximation — render-deferred); None clears the paragraph's run formatting.
        r.Bind(FreeWRibbonCommandAction.DropCap,           new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Bind(FreeWRibbonCommandAction.DropCap_Dropped,   new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Bind(FreeWRibbonCommandAction.DropCap_InMargin, new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        r.Bind(FreeWRibbonCommandAction.DropCap_None,      new ActionRibbonCommand(editor.ClearDropCap));
        r.Bind(FreeWRibbonCommandAction.DropCapDropped,   new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        r.Bind(FreeWRibbonCommandAction.DropCapInMargin, new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        r.Bind(FreeWRibbonCommandAction.DropCapNone,      new ActionRibbonCommand(editor.ClearDropCap));
        r.Bind(FreeWRibbonCommandAction.DropCapOptions,   new ActionRibbonCommand(callbacks.OpenDropCapOptionsDialog ?? (() => { })));

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
        r.Bind(FreeWRibbonCommandAction.Equation,           new ActionRibbonCommand(() => editor.InsertEquation()));
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
        // Opens a file picker (shell callback); DOCX content is inserted as model blocks and TXT as plain text.
        var textFromFileCommand = new ActionRibbonCommand(callbacks.InsertTextFromFile ?? (() => { }));
        r.Bind(FreeWRibbonCommandAction.InsertFile, textFromFileCommand);
        r.Register("freew.text-from-file", textFromFileCommand);
        r.Bind(FreeWRibbonCommandAction.Chart, new EditingActionCommand(editor, callbacks.OpenInsertChartDialog, () => editor.InsertChart()));
        r.Bind(FreeWRibbonCommandAction.Smartart, new EditingActionCommand(editor, callbacks.OpenInsertSmartArtDialog, () => editor.InsertSmartArt()));
        r.Bind(FreeWRibbonCommandAction.InsertIcon, new EditingActionCommand(editor, callbacks.OpenIconPickerDialog, editor.InsertIcon));
        r.Bind(FreeWRibbonCommandAction.Wordart, new ActionRibbonCommand(() => editor.InsertWordArt()));
        r.Bind(FreeWRibbonCommandAction.Object, new ActionRibbonCommand(() => editor.InsertEmbeddedObject()));
        r.Bind(FreeWRibbonCommandAction.UpdateFields, new ActionRibbonCommand(editor.UpdateFields));
        r.Bind(FreeWRibbonCommandAction.ToggleFieldCodes, new ActionRibbonCommand(editor.ToggleFieldCodes));
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
    private static void RegisterTableBorderCommands(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, CellBorderEdges edges, bool clear = false) =>
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
    private static void RegisterCellAlignmentCommands(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(
            IRibbonCommandRegistry reg,
            DocumentView ed,
            FreeWRibbonCommandAction action,
            TableCellVerticalAlignment vAlign,
            TextAlignment hAlign) =>
            reg.Bind(action,
                new ActionRibbonCommand(() => ed.SetCaretCellAlignment(vAlign, hAlign)));

        Add(r, editor, FreeWRibbonCommandAction.CellAlignTopLeft,       TableCellVerticalAlignment.Top,    TextAlignment.Left);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignTopCenter,     TableCellVerticalAlignment.Top,    TextAlignment.Center);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignTopRight,      TableCellVerticalAlignment.Top,    TextAlignment.Right);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignMiddleLeft,    TableCellVerticalAlignment.Center, TextAlignment.Left);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignMiddleCenter,  TableCellVerticalAlignment.Center, TextAlignment.Center);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignMiddleRight,   TableCellVerticalAlignment.Center, TextAlignment.Right);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignBottomLeft,    TableCellVerticalAlignment.Bottom, TextAlignment.Left);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignBottomCenter,  TableCellVerticalAlignment.Bottom, TextAlignment.Center);
        Add(r, editor, FreeWRibbonCommandAction.CellAlignBottomRight,   TableCellVerticalAlignment.Bottom, TextAlignment.Right);
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
        IRibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
    {
        // Footnotes & Endnotes — insert an empty note + reference marker at the caret.
        var footnote = new ActionRibbonCommand(
            callbacks.OpenFootnoteDialog ?? (() => editor.InsertFootnote()));
        r.Bind(FreeWRibbonCommandAction.Footnote, footnote);
        r.Register("freew.insert-footnote", footnote);
        r.Bind(FreeWRibbonCommandAction.NextFootnote, new ActionRibbonCommand(() => editor.MoveToNextFootnote()));
        r.Bind(FreeWRibbonCommandAction.PreviousFootnote, new ActionRibbonCommand(() => editor.MoveToPreviousFootnote()));
        r.Bind(FreeWRibbonCommandAction.NextEndnote, new ActionRibbonCommand(() => editor.MoveToNextEndnote()));
        r.Bind(FreeWRibbonCommandAction.PreviousEndnote, new ActionRibbonCommand(() => editor.MoveToPreviousEndnote()));
        if (callbacks.ToggleNotesPane is { } toggle && callbacks.IsNotesPaneVisible is { } isVisible)
        {
            r.BindToggle(FreeWRibbonCommandAction.ShowNotes,
                toggle,
                isVisible);
        }
        else
        {
            r.BindAction(FreeWRibbonCommandAction.ShowNotes,
                callbacks.ToggleNotesPane ?? (() => { }));
        }
        r.Bind(FreeWRibbonCommandAction.FootnoteEndnoteOptions, new ActionRibbonCommand(
            callbacks.OpenFootnoteEndnoteOptionsDialog ?? (() => { })));

        var endnote = new ActionRibbonCommand(
            callbacks.OpenEndnoteDialog ?? (() => editor.InsertEndnote()));
        r.Bind(FreeWRibbonCommandAction.Endnote, endnote);
        r.Register("freew.insert-endnote", endnote);

        // Table of Contents — generate from the heading outline / regenerate in place.
        var toc = new ActionRibbonCommand(editor.InsertTableOfContents);
        r.Bind(FreeWRibbonCommandAction.Toc, toc);
        r.Register("freew.insert-toc", toc);

        var tocRefresh = new ActionRibbonCommand(editor.UpdateTableOfContents);
        r.Bind(FreeWRibbonCommandAction.TocRefresh, tocRefresh);
        r.Register("freew.update-toc", tocRefresh);

        // Captions — the primary action opens the label/text dialog; menu labels remain direct.
        var caption = new ActionRibbonCommand(callbacks.OpenCaptionDialog ?? (() => { }));
        r.Bind(FreeWRibbonCommandAction.Caption, caption);
        r.Register("freew.insert-caption", caption);
        r.Bind(FreeWRibbonCommandAction.InsertCaption_Figure, new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Figure)));
        r.Bind(FreeWRibbonCommandAction.InsertCaption_Table,  new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Table)));
        r.Bind(FreeWRibbonCommandAction.InsertCaption_Equation, new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Equation)));

        // Dialog-backed commands no-op without a shell callback instead of silently choosing defaults.
        r.Bind(FreeWRibbonCommandAction.CrossReference, new ActionRibbonCommand(callbacks.OpenCrossReferenceDialog ?? (() => { })));

        var citation = new ActionRibbonCommand(callbacks.OpenCitationDialog ?? (() => { }));
        r.Bind(FreeWRibbonCommandAction.Citation, citation);
        r.Register("freew.insert-citation", citation);
        r.Bind(FreeWRibbonCommandAction.ManageSources, new ActionRibbonCommand(callbacks.OpenManageSourcesDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.CitationStyle, new FreeWRibbonChoiceCommand(
            value => editor.ApplyCitationStyle(Citations.ParseStyle(value, editor.Document.BibliographyStyle)),
            () => Citations.StyleName(editor.Document.BibliographyStyle)));
        r.Bind(FreeWRibbonCommandAction.Bibliography, new ActionRibbonCommand(editor.InsertBibliography));

        r.Bind(FreeWRibbonCommandAction.Tof, new ActionRibbonCommand(() => editor.InsertTableOfFigures()));
        r.Bind(FreeWRibbonCommandAction.Tof_Figure, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Figure)));
        r.Bind(FreeWRibbonCommandAction.Tof_Table, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Table)));
        r.Bind(FreeWRibbonCommandAction.Tof_Equation, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Equation)));
        r.Bind(FreeWRibbonCommandAction.TofRefresh, new ActionRibbonCommand(() => editor.RefreshTableOfFigures()));
        r.Bind(FreeWRibbonCommandAction.TofRefresh_Figure, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Figure)));
        r.Bind(FreeWRibbonCommandAction.TofRefresh_Table, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Table)));
        r.Bind(FreeWRibbonCommandAction.TofRefresh_Equation, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Equation)));
        r.Bind(FreeWRibbonCommandAction.IndexMark, new ActionRibbonCommand(
            callbacks.OpenMarkIndexEntryDialog ?? (() => editor.MarkIndexEntry())));
        r.Bind(FreeWRibbonCommandAction.IndexInsert, new ActionRibbonCommand(
            callbacks.OpenInsertIndexDialog ?? (() => editor.InsertIndex())));
        r.Bind(FreeWRibbonCommandAction.IndexRefresh, new ActionRibbonCommand(
            callbacks.OpenUpdateIndexDialog ?? (() => editor.RefreshIndex())));
        r.Bind(FreeWRibbonCommandAction.MarkCitation, new ActionRibbonCommand(callbacks.OpenMarkCitationDialog ?? (() => { })));
        r.Bind(FreeWRibbonCommandAction.TableOfAuthorities, new ActionRibbonCommand(
            callbacks.ShowTableOfAuthoritiesDialog ?? (() =>
            {
                var commit = TableOfAuthoritiesDialogPlanner.PlanCommit(
                    callbacks.OpenTableOfAuthoritiesDialog?.Invoke(),
                    useDefaultsWhenUnavailable: callbacks.OpenTableOfAuthoritiesDialog is null);
                if (commit.ShouldInsert)
                    editor.InsertTableOfAuthorities(commit.Options!);
            })));
        r.Bind(FreeWRibbonCommandAction.TableOfAuthoritiesRefresh, new ActionRibbonCommand(editor.RefreshTableOfAuthorities));
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
        IRibbonCommandRegistry r,
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
                var requiredKind = target == ObjectFormatTarget.Picture ? "Image" : "Shape";
                r.Register(command.CommandId, new ActionRibbonCommand(() =>
                    editor.ChangeSelectedFloatingZOrder(operation, requiredKind)));
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
        r.Bind(FreeWRibbonCommandAction.ImageAdjustDialog, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAdjustDialog));
        r.Bind(FreeWRibbonCommandAction.ImageCrop, new ImageCropCommand(editor, callbacks));
        r.Bind(FreeWRibbonCommandAction.ImageSize, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageSizeDialog));
        r.Bind(FreeWRibbonCommandAction.ImageAltText, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAltTextDialog));
        r.Bind(FreeWRibbonCommandAction.ImageBorder, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageBorderDialog));
        RegisterImageAdjustmentCommands(r, editor, callbacks);
        r.Bind(FreeWRibbonCommandAction.ImageReset, new ImageResetCommand(editor));
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            var captured = preset;
            r.Register(
                $"freew.image-style-{captured.Id}",
                new FreeWRibbonStatefulPortCommand(
                    _ => editor.ApplySelectedImageStyle(captured),
                    () => new RibbonCommandState(
                        IsEnabled: editor.SelectedFloatingImage() is not null)));
        }
        r.Bind(FreeWRibbonCommandAction.ImageAlignLeft, new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Left));
        r.Bind(FreeWRibbonCommandAction.ImageAlignCenter, new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Center));
        r.Bind(FreeWRibbonCommandAction.ImageAlignRight, new FloatingObjectParagraphAlignCommand(editor, "Image", TextAlignment.Right));
        r.Bind(FreeWRibbonCommandAction.ImageAlignToPage, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        r.Bind(FreeWRibbonCommandAction.ImageAlignToMargin, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        r.Bind(FreeWRibbonCommandAction.ImageDistributeH, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        r.Bind(FreeWRibbonCommandAction.ImageDistributeV, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        RegisterFloatingPositionCommands(r, editor, "shape", "Shape", callbacks.OpenShapePositionDialog);
        r.Bind(FreeWRibbonCommandAction.ShapeEditShape, new ActionRibbonCommand(() => editor.Focus()));
        r.Bind(FreeWRibbonCommandAction.ShapeConvertFreeform, new ActionRibbonCommand(editor.ConvertSelectedShapeToFreeform));
        r.Bind(FreeWRibbonCommandAction.ShapeEditPoints, new ActionRibbonCommand(editor.BeginShapeEditPoints));
        r.Register("freew.shape-change", new ShapeKindCommand(editor, null));
        r.Bind(FreeWRibbonCommandAction.ShapeChangeRectangle, new ShapeKindCommand(editor, ShapeKind.Rectangle));
        r.Bind(FreeWRibbonCommandAction.ShapeChangeRounded, new ShapeKindCommand(editor, ShapeKind.RoundedRectangle));
        r.Bind(FreeWRibbonCommandAction.ShapeChangeEllipse, new ShapeKindCommand(editor, ShapeKind.Ellipse));
        r.Bind(FreeWRibbonCommandAction.ShapeTextDirection, new ActionRibbonCommand(() => editor.Focus()));
        r.Bind(FreeWRibbonCommandAction.ShapeTextHorizontal, new ShapeTextDirectionCommand(editor, ShapeTextDirection.Horizontal));
        r.Bind(FreeWRibbonCommandAction.ShapeTextRotate90, new ShapeTextDirectionCommand(editor, ShapeTextDirection.Rotate90));
        r.Bind(FreeWRibbonCommandAction.ShapeTextRotate270, new ShapeTextDirectionCommand(editor, ShapeTextDirection.Rotate270));
        r.Bind(FreeWRibbonCommandAction.ShapeAlignLeft, new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Left));
        r.Bind(FreeWRibbonCommandAction.ShapeAlignCenter, new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Center));
        r.Bind(FreeWRibbonCommandAction.ShapeAlignRight, new FloatingObjectParagraphAlignCommand(editor, "Shape", TextAlignment.Right));
        r.Bind(FreeWRibbonCommandAction.ShapeAlignToPage, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        r.Bind(FreeWRibbonCommandAction.ShapeAlignToMargin, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        r.Bind(FreeWRibbonCommandAction.ShapeDistributeH, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        r.Bind(FreeWRibbonCommandAction.ShapeDistributeV, new FloatingObjectArrangeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        r.Bind(FreeWRibbonCommandAction.ShapeSize, new FloatingObjectSizeCommand(editor, "Shape", callbacks.OpenShapeSizeDialog));
        foreach (var preset in FreeWRibbonDefinitionData.FloatingSizePresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-size-{captured.Suffix}",
                new FloatingObjectSizePresetCommand(editor, "Shape", captured));
        }

        r.Bind(FreeWRibbonCommandAction.ShapeAltText, new FloatingObjectAltTextCommand(editor, callbacks.OpenShapeAltTextDialog));
        foreach (var preset in FreeWRibbonDefinitionData.ShapeAltTextPresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-alt-text-{captured.Suffix}",
                new FloatingObjectAltTextPresetCommand(editor, captured));
        }
        r.Bind(FreeWRibbonCommandAction.ObjectGroup, new FloatingObjectGroupCommand(editor));
        r.Bind(FreeWRibbonCommandAction.ObjectUngroup, new FloatingObjectUngroupCommand(editor));

        // Shape Styles fill/outline: top-level opener ids plus menu item commands.
        RegisterShapeFillOutlineCommands(r, editor);
    }

    private static void RegisterImageAdjustmentCommands(
        IRibbonCommandRegistry r,
        DocumentView editor,
        RibbonHostCallbacks callbacks)
    {
        // These IDs are the WPF authority's Picture Format > Adjust routes. Keep the
        // value-preserving mutations in DocumentView so both hosts use the shared model commands.
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageBrightnessPlus20,
            image => editor.SetSelectedImageAdjust(20, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageBrightnessPlus40,
            image => editor.SetSelectedImageAdjust(40, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageBrightnessMinus20,
            image => editor.SetSelectedImageAdjust(-20, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageBrightnessMinus40,
            image => editor.SetSelectedImageAdjust(-40, image.ContrastPct, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageContrastPlus20,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, 20, image.SaturationPct, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageContrastMinus20,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, -20, image.SaturationPct, image.TransparencyPct));

        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageSaturation0,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 0, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageSaturation50,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 50, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageSaturation200,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, 200, image.TransparencyPct));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageTransparency25,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 25));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageTransparency50,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 50));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageTransparency75,
            image => editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, 75));

        // Avalonia currently exposes one shared adjustment dialog callback, which is also
        // the WPF route used for Color and Transparency's full-value dialogs.
        r.Bind(FreeWRibbonCommandAction.ImageColorDialog, new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));
        r.Bind(FreeWRibbonCommandAction.ImageTransparencyDialog, new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));

        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageRecolorGrayscale,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Grayscale));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageRecolorSepia,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Sepia));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageRecolorWashout,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.Washout));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageRecolorBlackwhite,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.BlackWhite));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageRecolorNone,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageColortempWarm,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, 60));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageColortempCool,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, -60));
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageColortempNeutral,
            _ => editor.SetSelectedImageRecolor(ImageRecolorMode.None, 0));

        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageShadowNone,
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
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageReflectionNone,
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
        RegisterImageMutation(r, editor, FreeWRibbonCommandAction.ImageBevelNone,
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
        IRibbonCommandRegistry registry,
        DocumentView editor,
        FreeWRibbonCommandAction action,
        Action<InlineImage> mutation) =>
        registry.Bind(action,
            new SelectedImageMutationCommand(editor, mutation));

    private static void RegisterImageMutation(
        IRibbonCommandRegistry registry,
        DocumentView editor,
        string commandId,
        Action<InlineImage> mutation) =>
        registry.Register(commandId, new SelectedImageMutationCommand(editor, mutation));

    private static void RegisterFloatingPositionCommands(
        IRibbonCommandRegistry r,
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

            if (requiredKind == "Shape")
                editor.SetSelectedShapePosition(hOffset, vOffset, hAnchor, vAnchor);
            else
                editor.SetFloatingPosition(hOffset, vOffset, hAnchor, vAnchor);
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => requiredKind == "Shape"
            ? editor.SelectedFloatingShape() is not null
            : editor.SelectedFloatingInfo?.Kind == requiredKind;

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

            if (requiredKind == "Shape")
            {
                editor.SetSelectedShapePosition(
                    preset.HorizontalOffsetPt,
                    preset.VerticalOffsetPt,
                    preset.HorizontalAnchor,
                    preset.VerticalAnchor);
            }
            else
            {
                editor.SetFloatingPosition(
                    preset.HorizontalOffsetPt,
                    preset.VerticalOffsetPt,
                    preset.HorizontalAnchor,
                    preset.VerticalAnchor);
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => requiredKind == "Shape"
            ? editor.SelectedFloatingShape() is not null
            : editor.SelectedFloatingInfo?.Kind == requiredKind;
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
                ApplySize(widthPt, heightPt);
            else if (string.IsNullOrWhiteSpace(context.SelectedValue))
                openDialog?.Invoke();
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private void ApplySize(double widthPt, double heightPt)
        {
            if (requiredKind == "Shape")
                editor.SetSelectedShapeSize(widthPt, heightPt);
            else
                editor.SetFloatingSize(widthPt, heightPt);
        }

        private bool IsEnabled() => (requiredKind == "Shape"
                ? editor.SelectedFloatingShape() is not null
                : editor.SelectedFloatingInfo?.Kind == requiredKind)
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
            {
                if (requiredKind == "Shape")
                    editor.SetSelectedShapeSize(preset.WidthPt, preset.HeightPt);
                else
                    editor.SetFloatingSize(preset.WidthPt, preset.HeightPt);
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: IsEnabled());

        private bool IsEnabled() => (requiredKind == "Shape"
                ? editor.SelectedFloatingShape() is not null
                : editor.SelectedFloatingInfo?.Kind == requiredKind)
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

    private static void RegisterShapeFillOutlineCommands(IRibbonCommandRegistry r, DocumentView editor)
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

        r.Bind(FreeWRibbonCommandAction.ShapeEffects, new ShapeEffectsCommand(editor, null));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectsNone, new ShapeEffectsCommand(editor, null));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectShadow, new ShapeEffectsCommand(editor, new ShapeEffectLst { HasShadow = true }));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectGlow, new ShapeEffectsCommand(editor, new ShapeEffectLst { HasGlow = true }));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectSoftEdge, new ShapeEffectsCommand(editor, new ShapeEffectLst { HasSoftEdge = true }));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectReflection, new ShapeEffectsCommand(editor, new ShapeEffectLst { HasReflection = true }));
        r.Bind(FreeWRibbonCommandAction.ShapeEffectBevel, new ShapeEffectsCommand(editor, new ShapeEffectLst { HasBevel = true }));

        r.Bind(FreeWRibbonCommandAction.ShapeStylesGallery, new ShapeStylesGalleryCommand(editor));
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
        IRibbonCommandRegistry r,
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
                new FreeWRibbonStatefulPortCommand(
                    _ => editor.SetChartQuickLayout(captured),
                    () => new RibbonCommandState(
                        IsEnabled: editor.GetSelectedChartInfo() is not null)));
        }

        // Change Colors — dropdown opener + one command per catalog colour scheme.
        r.Register("freew.chart-colors", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            r.Register($"freew.chart-colors-{sc.Id}", new ActionRibbonCommand(() => editor.SetChartColorScheme(sc.Id)));
        }

        r.Bind(FreeWRibbonCommandAction.ChartToggleLegend, new ActionRibbonCommand(editor.ToggleChartLegend));
        r.Bind(FreeWRibbonCommandAction.ChartTitle, new SelectedFloatingDialogCommand(
            editor, "Chart", callbacks.OpenChartTitleDialog, editor.ToggleChartTitle));
        r.Bind(FreeWRibbonCommandAction.ChartAxisTitles, new SelectedFloatingDialogCommand(
            editor, "Chart", callbacks.OpenChartAxisTitlesDialog, editor.ToggleChartAxisTitles));
        r.Bind(FreeWRibbonCommandAction.ChartEditData, new ContextRibbonCommand(context =>
        {
            if (TryBuildChartDataPreset(context.SelectedValue, out var chart))
                editor.ReplaceSelectedChartData(chart);
            else if (string.IsNullOrWhiteSpace(context.SelectedValue)
                     && editor.SelectedFloatingChart() is not null)
                callbacks.OpenChartEditDataDialog?.Invoke();
        }));
        var chartSize = new ChartSizeCommand(editor, callbacks.OpenChartSizeDialog);
        r.Bind(FreeWRibbonCommandAction.ChartSize, chartSize);
        r.Bind(FreeWRibbonCommandAction.ChartSizeDialog, chartSize);

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

        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartAddShape, SmartArtStructureOperation.AddShape);
        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartRemoveShape, SmartArtStructureOperation.RemoveShape);
        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartPromote, SmartArtStructureOperation.Promote);
        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartDemote, SmartArtStructureOperation.Demote);
        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartMoveUp, SmartArtStructureOperation.MoveUp);
        RegisterSmartArtStructureCommand(r, editor, FreeWRibbonCommandAction.SmartartMoveDown, SmartArtStructureOperation.MoveDown);
        r.Bind(FreeWRibbonCommandAction.SmartartEditText, new SmartArtEditTextRibbonCommand(editor, callbacks.OpenSmartArtEditDialog));
        r.Bind(FreeWRibbonCommandAction.SmartartChangeStyle, new FreeWRibbonStatefulPortCommand(
            context =>
            {
                if (SmartArtCommandPlanner.ResolveStyle(context.SelectedValue) is { } style)
                    editor.SetSmartArtStyle(style);
            },
            () => new RibbonCommandState(
                IsEnabled: SmartArtCommandPlanner.CanEdit(editor.SelectedFloatingSmartArt()))));
    }

    private static void RegisterSmartArtStructureCommand(
        IRibbonCommandRegistry registry,
        DocumentView editor,
        FreeWRibbonCommandAction action,
        SmartArtStructureOperation operation) =>
        registry.Bind(action, new FreeWRibbonStatefulPortCommand(
            _ => editor.MutateSelectedSmartArt(operation),
            () => new RibbonCommandState(
                IsEnabled: SmartArtCommandPlanner.IsEnabled(
                    editor.SelectedFloatingSmartArt(),
                    operation))));

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
        IRibbonCommandRegistry registry,
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
    private static void RegisterMailingsCommands(IRibbonCommandRegistry r, MailMergeEngine engine)
    {
        r.Bind(FreeWRibbonCommandAction.MergeEnvelopes, new ActionRibbonCommand(engine.ApplyDefaultEnvelope));
        r.Bind(FreeWRibbonCommandAction.MergeLabels, new ActionRibbonCommand(engine.ApplyDefaultLabels));
        r.Bind(FreeWRibbonCommandAction.StartMailMerge, new ActionRibbonCommand(engine.StartMailMergeLetters));
        r.Bind(FreeWRibbonCommandAction.StartMailMergeLetters, new ActionRibbonCommand(engine.StartMailMergeLetters));
        r.Bind(FreeWRibbonCommandAction.StartMailMergeDirectory, new ActionRibbonCommand(engine.StartMailMergeDirectory));
        r.Bind(FreeWRibbonCommandAction.StartMailMergeNormal, new ActionRibbonCommand(engine.ClearMergeSession));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergeData, new ActionRibbonCommand(engine.SelectRecipients),
            "freew.select-recipients");
        r.Bind(FreeWRibbonCommandAction.MergeEditRecipients, new ActionRibbonCommand(engine.SelectRecipients));
        r.Bind(FreeWRibbonCommandAction.MergeField, new ActionRibbonCommand(engine.InsertMergeField));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergeAddressBlock, new ActionRibbonCommand(engine.InsertAddressBlock),
            "freew.address-block");
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergeGreetingLine, new ActionRibbonCommand(engine.InsertGreetingLine),
            "freew.greeting-line");
        r.Bind(FreeWRibbonCommandAction.MergeMatchFields, new ActionRibbonCommand(engine.MatchFields));
        r.Bind(FreeWRibbonCommandAction.MergeFilterSort, new ActionRibbonCommand(engine.FilterSortRecipients));
        r.Bind(FreeWRibbonCommandAction.MergeRules, EmptyRibbonCommand.Instance);
        r.Bind(FreeWRibbonCommandAction.MergeRuleIf, new ActionRibbonCommand(engine.InsertIfRule));
        r.Bind(FreeWRibbonCommandAction.MergeRuleSkipRecordIf, new ActionRibbonCommand(engine.InsertSkipRecordIfRule));
        r.Bind(FreeWRibbonCommandAction.MergeRuleNextRecordIf, new ActionRibbonCommand(engine.InsertNextRecordIfRule));
        r.Bind(FreeWRibbonCommandAction.MergeNextRecord, new ActionRibbonCommand(engine.InsertNextRecordField));
        r.Bind(FreeWRibbonCommandAction.MergeRecordNumber, new ActionRibbonCommand(engine.InsertMergeRecordNumberField));
        r.Bind(FreeWRibbonCommandAction.MergeSequenceNumber, new ActionRibbonCommand(engine.InsertMergeSequenceNumberField));
        r.Bind(FreeWRibbonCommandAction.MergeRuleFillIn, new ActionRibbonCommand(engine.InsertFillInRule));
        r.Bind(FreeWRibbonCommandAction.MergeRuleAsk, new ActionRibbonCommand(engine.InsertAskRule));
        r.Bind(FreeWRibbonCommandAction.MergeRuleSet, new ActionRibbonCommand(engine.InsertSetRule));
        r.Bind(FreeWRibbonCommandAction.MergeRuleRef, new ActionRibbonCommand(engine.InsertRefRule));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreview, new ActionRibbonCommand(engine.TogglePreview),
            "freew.preview-results");
        r.Bind(FreeWRibbonCommandAction.MergePreviewFirst, new ActionRibbonCommand(engine.FirstRecord));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreviewNext, new ActionRibbonCommand(engine.NextRecord),
            "freew.next-record");
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreviewPrevious, new ActionRibbonCommand(engine.PreviousRecord),
            "freew.prev-record");
        r.Bind(FreeWRibbonCommandAction.MergePreviewLast, new ActionRibbonCommand(engine.LastRecord));
        // MainWindow replaces these with owner-modal dialogs; keep definition parity for headless hosts.
        r.Bind(FreeWRibbonCommandAction.MergeFindRecipient, new ActionRibbonCommand(() => { }));
        r.Bind(FreeWRibbonCommandAction.MergeCheckErrors, new ActionRibbonCommand(() => { }));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergeFinish, new ActionRibbonCommand(() => engine.FinishMerge()),
            "freew.finish-merge");
        r.Bind(FreeWRibbonCommandAction.MergeEmail, new ActionRibbonCommand(() => engine.PlanEmailMerge()));
    }

    private static void RegisterMailingsAlias(
        IRibbonCommandRegistry r,
        FreeWRibbonCommandAction canonicalAction,
        IRibbonCommand command,
        params string[] aliases)
    {
        r.Bind(canonicalAction, command);
        foreach (var alias in aliases)
            r.Register(alias, command);
    }

    /// <summary>
    /// AV-DESIGN: Registers the Design-tab commands — Themes / Colors / Fonts / Paragraph-Spacing galleries
    /// (document-wide style mutations), Page Color, Page Borders, and Watermark. Each gallery dropdown's
    /// top-level dropdown ids either consume the selected combo value or act as menu openers; the
    /// per-item ids resolve to a model-backed, undoable
    /// <see cref="DocumentView"/> Design method. Page Borders + Custom Watermark route through the optional
    /// <see cref="RibbonHostCallbacks"/> dialog launchers and safely no-op when the shell did not supply one
    /// (so the registry-completeness guard passes and parallel waves / tests keep compiling).
    /// </summary>
    private static void RegisterDesignCommands(
        IRibbonCommandRegistry r, DocumentView editor, RibbonHostCallbacks callbacks)
    {
        // ── Themes ───────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.Theme, new ThemeCommand(editor));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyTheme(t)));
        }

        // ── Colors (palette only — preserves fonts) ──────────────────────────
        r.Bind(FreeWRibbonCommandAction.ThemeColors, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Bind(FreeWRibbonCommandAction.CustomizeColors, new ActionRibbonCommand(callbacks.OpenCustomizeThemeColorsDialog ?? (() => { })));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme-colors.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyThemeColors(t)));
        }

        // ── Fonts (heading/body pairing — preserves colours) ─────────────────
        r.Bind(FreeWRibbonCommandAction.ThemeFonts, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Bind(FreeWRibbonCommandAction.CustomizeFonts, new ActionRibbonCommand(callbacks.OpenCustomizeThemeFontsDialog ?? (() => { })));
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
        r.Bind(FreeWRibbonCommandAction.CustomParagraphSpacing,
            new ActionRibbonCommand(callbacks.OpenCustomParagraphSpacingDialog ?? (() => { })));

        r.Bind(FreeWRibbonCommandAction.ThemeEffects, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        for (var index = 0; index < DocumentEffectSet.Catalog.Count; index++)
        {
            var effectSet = DocumentEffectSet.Catalog[index];
            r.Register(FreeWContextMenuPlanner.EffectsPrefix + index,
                new ActionRibbonCommand(() => editor.ApplyEffectSet(effectSet)));
        }

        // ── Page Color swatches (+ No Color) ─────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.StyleSet, new StyleSetCommand(editor));
        r.Bind(FreeWRibbonCommandAction.ResetStyleSet, new ActionRibbonCommand(() => editor.ApplyStyleSet(DocumentStyleSet.Default)));

        r.Bind(FreeWRibbonCommandAction.PageColor, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.page-color.more", new ActionRibbonCommand(callbacks.OpenPageColorDialog ?? (() => { })));
        RegisterPageColorPalette(r, editor);

        // ── Page Borders — dialog launcher (optional callback) ───────────────
        r.Register("freew.page-borders", new ActionRibbonCommand(callbacks.OpenPageBordersDialog ?? (() => { })));

        // ── Watermark — built-in presets + Custom (dialog) + Remove ──────────
        r.Bind(FreeWRibbonCommandAction.Watermark, new ActionRibbonCommand(() => { /* dropdown opener */ }));
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
    private static void RegisterPageColorPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        static void Add(IRibbonCommandRegistry reg, DocumentView ed, string id, string? hex) =>
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
