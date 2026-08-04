using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Proofing;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Binds FreeW's ribbon command ids (declared in <see cref="FreeWRibbon"/>) to behavior over the
/// editing surface, implementing the shared <see cref="IRibbonCommandRegistry"/>. Formatting and
/// clipboard route through WPF's <see cref="EditingCommands"/>/<see cref="ApplicationCommands"/>
/// against the focused RichTextBox (inline edit + undo); bold/italic/underline are stateful so the
/// ribbon can reflect the selection.
/// </summary>
internal static class FreeWRibbonCommands
{
    private static IRibbonCommand BuildImageTransformCommand(
        DocumentView editor,
        ObjectFormatTransformCommand command) => new FloatingTransformCommand(editor, command);

    private static IRibbonCommand BuildShapeTransformCommand(
        DocumentView editor,
        ObjectFormatTransformCommand command) => new FloatingTransformCommand(editor, command);

    private sealed class FloatingTransformCommand(
        DocumentView editor,
        ObjectFormatTransformCommand command) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var applied = command.Kind switch
            {
                ObjectFormatTransformKind.Rotate =>
                    editor.RotateSelectedFloating(command.RotationDeltaDegrees),
                ObjectFormatTransformKind.FlipHorizontal =>
                    editor.FlipSelectedFloating(horizontal: true),
                ObjectFormatTransformKind.FlipVertical =>
                    editor.FlipSelectedFloating(horizontal: false),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
            };

            if (!applied)
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a floating object first.", "Rotate / Flip");
        }
    }

    private static MasterSourceStore CreateMasterStore(IReadOnlyList<Source> sources) =>
        new()
        {
            Sources = sources.Select(SourceRecord.FromSource).ToList()
        };

    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore) =>
        Build(editor, stateStore, onPrintPreview: null);

    /// <summary>Test seam for the WPF-authoritative Header/Footer prompt; production uses TextPrompt.</summary>
    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Func<bool, string, string?> askHeaderFooterText) =>
        Build(
            editor,
            stateStore,
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            askHeaderFooterText: askHeaderFooterText);

    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore, Action? onPrintPreview) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane: null, isNavPaneVisible: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode: null, isReadModeActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout: null, isPrintLayoutActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout, isPrintLayoutActive,
            onToggleOutlineView: null, isOutlineViewActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive,
        Action? onToggleOutlineView,
        Func<bool>? isOutlineViewActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout, isPrintLayoutActive,
            onToggleOutlineView, isOutlineViewActive, onZoomDialog: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive,
        Action? onToggleOutlineView,
        Func<bool>? isOutlineViewActive,
        Action? onZoomDialog,
        Action? onZoom100 = null,
        Action? onZoomOnePage = null,
        Action? onZoomPageWidth = null,
        Action? onWebLayout = null,
        Func<bool>? isWebLayoutActive = null,
        Action? onDraftView = null,
        Func<bool>? isDraftViewActive = null,
        Action? onToggleRevealFormatting = null,
        Func<bool>? isRevealFormattingVisible = null,
        Action? onToggleReviewingPane = null,
        Func<bool>? isReviewingPaneVisible = null,
        Action? onAcceptThisChange = null,
        Action? onRejectThisChange = null,
        Action? onPreviousChange = null,
        Action? onNextChange = null,
        Action? onFindReplace = null,
        Action? onToggleRuler = null,
        Func<bool>? isRulerVisible = null,
        Action? onToggleMultiplePages = null,
        Func<bool>? isMultiplePagesActive = null,
        Action? onToggleSideToSide = null,
        Func<bool>? isSideToSideActive = null,
        Action? onToggleSplitWindow = null,
        Func<bool>? isSplitWindowActive = null,
        Action? onHelpOnline = null,
        Action? onFeedback = null,
        Action? onCopyDiagnostics = null,
        Action? onCheckForUpdates = null,
        Action? onAbout = null,
        Action? onLegalNotices = null,
        Action? onToggleNotesPane = null,
        Func<bool>? isNotesPaneVisible = null,
        Action<string>? onOpenHeaderFooterPane = null,
        Action? onCloseHeaderFooterPane = null,
        Action? onTogglePagedEditView = null,
        Func<bool>? isPagedEditViewActive = null,
        // Feature 4 — Read Mode options (column width / page color).
        Action<string>? onReadModeColumnWidth = null,
        Action<string>? onReadModePageColor = null,
        // Feature 5 — New Window / Arrange All.
        Action? onNewWindow = null,
        Action? onArrangeAll = null,
        // W25 — Local Thesaurus pane + Balloons review mode.
        Action? onToggleThesaurus = null,
        Action? onToggleBalloons = null,
        Func<bool, string, string?>? askHeaderFooterText = null,
        Action<TextDocument>? onOpenMailMergeErrorReport = null,
        Action<TextDocument>? onPrintMailMergeDocument = null)
    {
        var registry = new RibbonCommandRegistry();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();

        void Routed(string id, RoutedCommand command) =>
            registry.Register(id, new RoutedEditCommand(editor, command));

        void Toggle(
            string id,
            RoutedCommand command,
            DependencyProperty property,
            Func<object?, bool> isOn,
            Func<bool>? tryModelToggle = null)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn, tryModelToggle);
            registry.Register(id, cmd);
            stateful.Add((id, cmd));
        }

        void PageSetting(string id, Action<PageSettings> apply, Func<PageSettings, bool>? isChecked = null)
        {
            var command = new PageCommand(editor, apply, isChecked);
            registry.Register(id, command);
            stateful.Add((id, command));
            stateStore.SetState(id, command.GetState());
        }

        Toggle("freew.bold", EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold,
            () => editor.TryToggleSelectedRunFormatting(f => f.Bold, (f, value) => f with { Bold = value }));
        Toggle("freew.italic", EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic,
            () => editor.TryToggleSelectedRunFormatting(f => f.Italic, (f, value) => f with { Italic = value }));
        Toggle("freew.underline", EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
            v => v is TextDecorationCollection d && d.Count > 0,
            () => editor.TryToggleSelectedRunFormatting(f => f.Underline, (f, value) => f with { Underline = value }));

        // Live ribbon state: when the caret/selection moves or a document render replaces the model,
        // recompute state and push it into the shared store. The store deduplicates unchanged values.
        void RefreshStatefulCommands()
        {
            foreach (var (id, command) in stateful)
                stateStore.SetState(id, command.GetState());
        }

        editor.SelectionChanged += (_, _) => RefreshStatefulCommands();
        editor.LayoutChanged += (_, _) => RefreshStatefulCommands();

        // Home > Font: character effects. Superscript/subscript are mutually exclusive baseline
        // offsets; small caps / all caps map to WPF typography. Each is a toggle over the selection.
        registry.Register("freew.superscript", new CharacterEffectCommand(editor, CharacterEffect.Superscript));
        registry.Register("freew.subscript", new CharacterEffectCommand(editor, CharacterEffect.Subscript));
        registry.Register("freew.strikethrough", new CharacterEffectCommand(editor, CharacterEffect.Strikethrough));
        registry.Register("freew.smallcaps", new CharacterEffectCommand(editor, CharacterEffect.SmallCaps));
        registry.Register("freew.allcaps", new CharacterEffectCommand(editor, CharacterEffect.AllCaps));

        // Home > Font: character border and character shading (new W20 commands). These are model-only
        // run properties with full DOCX round-trip (w:rBdr / w:shd). Character Border opens a border-
        // colour/style picker; Character Shading opens a colour swatch picker like paragraph shading.
        registry.Register("freew.char-border", new CharacterBorderCommand(editor));
        registry.Register("freew.char-shading", new CharacterShadingCommand(editor));

        // Review > Language > Set Proofing Language: opens a dialog listing common BCP-47 tags and
        // applies the chosen language to the selected runs (rPr/w:lang) for spell-check fidelity.
        registry.Register("freew.set-proofing-language", new SetProofingLanguageCommand(editor));

        Routed("freew.grow-font", EditingCommands.IncreaseFontSize);
        Routed("freew.shrink-font", EditingCommands.DecreaseFontSize);
        Routed("freew.align-left", EditingCommands.AlignLeft);
        Routed("freew.align-center", EditingCommands.AlignCenter);
        Routed("freew.align-right", EditingCommands.AlignRight);
        Routed("freew.align-justify", EditingCommands.AlignJustify);
        Routed("freew.bullets", EditingCommands.ToggleBullets);
        Routed("freew.numbering", EditingCommands.ToggleNumbering);
        Routed("freew.select", ApplicationCommands.SelectAll);
        if (onFindReplace is not null)
        {
            registry.Register("freew.find", new ActionRibbonCommand(onFindReplace));
            registry.Register("freew.replace", new ActionRibbonCommand(onFindReplace));
        }
        // Home > Paragraph: apply multilevel/legal outline numbering (1, 1.1, 1.1.1) to the selected
        // paragraph(s); the outline definition persists to word/numbering.xml. Tab/Shift+Tab demote
        // and promote the outline depth (ListLevel) of the selected list paragraphs.
        // The top-level "freew.multilevel-list" id applies the first (standard decimal) preset directly
        // (clicking the button face vs. the dropdown arrow follows the same pattern as Word's gallery).
        registry.Register("freew.multilevel-list", new ActionRibbonCommand(() =>
            editor.ApplyMultiLevelListDefinition(new MultilevelListDefinition(
                MultiLevelListFormat.LevelCount,
                null,
                null,
                MultiLevelListFormat.DecimalNumberFormats))));
        registry.Register("freew.multilevel-demote", new ActionRibbonCommand(() => editor.ChangeListLevel(+1)));
        registry.Register("freew.multilevel-promote", new ActionRibbonCommand(() => editor.ChangeListLevel(-1)));
        // Predefined multilevel list preset commands — three Word-parity presets shown in the gallery.
        for (var pi = 0; pi < MultilevelListDialog.Presets.Length; pi++)
        {
            var preset = MultilevelListDialog.Presets[pi];
            var capturedPreset = preset; // capture for lambda
            registry.Register($"freew.multilevel-preset-{pi}", new ActionRibbonCommand(() =>
            {
                editor.Focus();
                capturedPreset.Apply(editor);
            }));
        }
        // "Define New Multilevel List" dialog: captures backed options (number of levels, start-at, and
        // the first three per-level number styles).
        registry.Register("freew.multilevel-define", new DefineMultilevelListCommand(editor));
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);
        // Home > Clipboard: paste-special. "Paste Text Only" strips all source formatting; "Merge
        // Formatting" matches the destination. In FreeW both resolve to match-destination insertion at
        // the caret (the pasted text inherits the caret run's formatting), routed through the editor's
        // undoable InsertText path. See DocumentView.PastePlainText / PasteMergeFormatting.
        registry.Register("freew.paste-plain", new ActionRibbonCommand(() => editor.PastePlainText()));
        registry.Register("freew.paste-merge", new ActionRibbonCommand(() => editor.PasteMergeFormatting()));

        // Home > Clipboard > Format Painter: arm the painter from the current selection's run +
        // paragraph formatting; the editor stamps it onto the user's next mouse selection and disarms.
        registry.Register("freew.format-painter", new FormatPainterCommand(editor));

        var fontFamily = new SelectionValueCommand(editor,
            (selection, value) => selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(value)),
            value => editor.TrySetSelectedRunFormatting(
                formatting => string.Equals(formatting.FontFamily, value, StringComparison.OrdinalIgnoreCase),
                formatting => formatting with { FontFamily = value }),
            () => editor.CurrentRunFormatting.FontFamily ?? string.Empty);
        registry.Register("freew.font-family", fontFamily);
        stateful.Add(("freew.font-family", fontFamily));
        stateStore.SetState("freew.font-family", fontFamily.GetState());

        var fontSize = new SelectionValueCommand(editor, (selection, value) =>
        {
            if (double.TryParse(value, out var points))
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
        }, value =>
        {
            if (!double.TryParse(value, out var points))
                return false;
            return editor.TrySetSelectedRunFormatting(
                formatting => formatting.FontSizePt is { } size && Math.Abs(size - points) < 0.0001,
                formatting => formatting with { FontSizePt = points });
        }, () => (editor.CurrentRunFormatting.FontSizePt ?? 11).ToString(
            "0.##", System.Globalization.CultureInfo.InvariantCulture));
        registry.Register("freew.font-size", fontSize);
        stateful.Add(("freew.font-size", fontSize));
        stateStore.SetState("freew.font-size", fontSize.GetState());

        // Insert tab — Pages: prepend a cover page, insert a blank page, or drop a horizontal rule / page break at the caret.
        // Each mutates the model through the view's undo/redo bus and re-renders.
        // Insert > Pages > Cover Page gallery: Default (existing centred layout), Banded (dark-blue title
        // band), and Motion (right-aligned title with date). The top-level id inserts the default preset
        // so clicking the button face (not the dropdown arrow) always works as before.
        registry.Register("freew.cover-page", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-default", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-banded", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Banded); }));
        registry.Register("freew.cover-page-motion", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Motion); }));
        registry.Register("freew.blank-page", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertBlankPage(); }));
        registry.Register("freew.horizontal-rule", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertHorizontalRule(); }));
        registry.Register("freew.page-break", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertPageBreak(); }));

        // Layout > Page Setup > Breaks: section/column breaks. The page-break item reuses the existing
        // command (registered above). Each section break inserts a paragraph whose SectionBreak property
        // is set to the appropriate SectionBreakKind, inheriting the current document's page settings.
        registry.Register("freew.column-break", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertColumnBreak(); }));
        registry.Register("freew.section-break-next-page", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.NextPage); }));
        registry.Register("freew.section-break-continuous", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.Continuous); }));
        registry.Register("freew.section-break-even-page", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.EvenPage); }));
        registry.Register("freew.section-break-odd-page", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.OddPage); }));

        // Insert tab — insert a small 2x2 table at the caret (routes through the undo/redo bus).
        registry.Register("freew.table", new InsertTableCommand(editor, rows: 2, columns: 2));
        // Insert tab — Table Tools: structural edits to the table containing the caret (all undoable).
        registry.Register("freew.table-insert-row", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableRow(); }));
        registry.Register("freew.table-delete-row", new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTableRow(); }));
        registry.Register("freew.table-insert-col", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableColumn(); }));
        registry.Register("freew.table-delete-col", new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTableColumn(); }));
        // Insert tab — Table Tools: merge the selected cells / split a merged cell (all undoable).
        registry.Register("freew.merge-cells", new ActionRibbonCommand(() => { editor.Focus(); editor.MergeSelectedCells(); }));
        registry.Register("freew.split-cell", new ActionRibbonCommand(() => { editor.Focus(); editor.SplitCell(); }));
        // Insert tab — Table Tools: pick/clear a fill colour for the caret's cell (sets model + re-renders).
        registry.Register("freew.cell-shading", new CellShadingCommand(editor));
        // Insert tab — Table Tools: table-style toggles applied to the caret's table (sets model + re-renders).
        // Table Tools — Data: insert a computed formula field (=SUM(ABOVE) etc.) into the caret's cell.
        registry.Register("freew.table-formula", new TableFormulaCommand(editor));
        // Table Tools — Properties: open the four-tab Table Properties dialog for the caret's table.
        registry.Register("freew.table-properties", new TablePropertiesCommand(editor));
        registry.Register("freew.table-header-row", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableHeaderRow(); }));
        registry.Register("freew.table-banded-rows", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableBandedRows(); }));
        registry.Register("freew.table-repeat-header", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableRepeatHeaderRow(); }));

        // Table Tools — Directional insert/delete
        registry.Register("freew.table-insert-above", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableRowAbove(); }));
        registry.Register("freew.table-insert-col-left", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableColumnLeft(); }));
        registry.Register("freew.table-delete", new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTable(); }));
        // Table Tools — Merge/Split enhancements
        registry.Register("freew.split-table", new ActionRibbonCommand(() => { editor.Focus(); editor.SplitTable(); }));
        // Table Tools — Select
        registry.Register("freew.table-select-table", new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTable(); }));
        registry.Register("freew.table-select-row", new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableRow(); }));
        registry.Register("freew.table-select-col", new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableColumn(); }));
        registry.Register("freew.table-select-cell", new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableCell(); }));
        // Table Tools — View Gridlines (toggle; display-only)
        registry.Register("freew.table-view-gridlines", new ActionRibbonCommand(() => { editor.ViewGridlines = !editor.ViewGridlines; editor.Focus(); }));
        // Table Tools — Cell Size
        registry.Register("freew.table-row-height", new TablePropertiesCommand(editor));
        registry.Register("freew.table-col-width", new TablePropertiesCommand(editor));
        registry.Register("freew.table-distribute-rows", new ActionRibbonCommand(() => { editor.Focus(); editor.DistributeTableRows(); }));
        registry.Register("freew.table-distribute-cols", new ActionRibbonCommand(() => { editor.Focus(); editor.DistributeTableColumns(); }));
        registry.Register("freew.table-autofit-contents", new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Contents); }));
        registry.Register("freew.table-autofit-window", new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Window); }));
        registry.Register("freew.table-autofit-fixed", new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Fixed); }));
        // Table Tools — Cell Alignment (9-way)
        registry.Register("freew.cell-align-top-left", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-top-center", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-top-right", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Right); }));
        registry.Register("freew.cell-align-middle-left", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-middle-center", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-middle-right", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Right); }));
        registry.Register("freew.cell-align-bottom-left", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-bottom-center", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-bottom-right", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Right); }));
        // Table Tools — Cell Margins (opens Table Properties dialog)
        registry.Register("freew.table-cell-margins", new TablePropertiesCommand(editor));
        // Table Design — Style Options toggles
        registry.Register("freew.table-last-row", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableLastRow(); }));
        registry.Register("freew.table-first-column", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableFirstColumn(); }));
        registry.Register("freew.table-last-column", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableLastColumn(); }));
        registry.Register("freew.table-banded-cols", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableBandedColumns(); }));
        // Table Design > Draw Borders: drag-to-insert table (prompted dimensions) and eraser-merges right.
        registry.Register("freew.draw-table", new DrawTableCommand(editor));
        registry.Register("freew.eraser", new EraserCommand(editor));
        // Table Layout Data group — Convert to Text
        registry.Register("freew.table-to-text", new ActionRibbonCommand(() => { editor.Focus(); editor.ConvertTableToText('\t'); }));
        // Table Design — Cell Borders picker (per-edge borders for the caret cell).
        registry.Register("freew.cell-borders", new CellBordersCommand(editor));
        // Table Layout > Alignment — Text Direction cycling (Horizontal → Rotate90 → Rotate270 → Horizontal).
        registry.Register("freew.cell-text-direction-horizontal", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Horizontal); }));
        registry.Register("freew.cell-text-direction-rotate90", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Rotate90); }));
        registry.Register("freew.cell-text-direction-rotate270", new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Rotate270); }));

        // Insert tab — Text: pick a .docx file and insert its body content at the caret (block merge).
        registry.Register("freew.insert-file", new InsertFileCommand(editor));
        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Register("freew.picture", new InsertPictureCommand(editor));
        // Insert tab — Illustrations: open the searchable icon picker and insert the chosen SVG
        // icon as a rasterised InlineImage (same round-trip path as Insert Picture).
        registry.Register("freew.insert-icon", new InsertIconCommand(editor));
        // Insert tab — Illustrations > Screenshot: the top-level "freew.screenshot" id only opens the
        // dropdown (no direct insert, so it isn't registered — mirroring "freew.shapes" above). "Screen
        // Clipping" drag-selects a screen region and inserts the captured PNG as an inline image through
        // the exact same InsertImage path as Insert Picture.
        registry.Register("freew.screen-clipping", new ScreenClippingCommand(editor));
        // Insert tab — Illustrations: resize the selected inline image (height scales proportionally).
        registry.Register("freew.image-size", new ImageSizeCommand(editor));
        // Insert tab — Illustrations: set the selected image's accessibility alt text (wp:docPr @descr),
        // and align the image's (image-only) paragraph left/center/right. Both mutate the model + re-render.
        registry.Register("freew.image-alt-text", new ImageAltTextCommand(editor));
        registry.Register("freew.image-align-left", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Left));
        registry.Register("freew.image-align-center", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Center));
        registry.Register("freew.image-align-right", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Right));
        // Picture Format > Arrange — align floating images relative to page or margin, or distribute evenly.
        registry.Register("freew.image-align-to-page",   new FloatingAlignCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        registry.Register("freew.image-align-to-margin", new FloatingAlignCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        registry.Register("freew.image-distribute-h", new FloatingDistributeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        registry.Register("freew.image-distribute-v", new FloatingDistributeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        foreach (var command in ObjectFormatCommandPlanner.WrapCommands(ObjectFormatTarget.Picture))
            registry.Register(command.CommandId, new ImageWrapCommand(editor, command.Wrapping));
        // Picture Format tab — Arrange > Rotate / Flip.
        foreach (var command in ObjectFormatCommandPlanner.TransformCommands(ObjectFormatTarget.Picture))
            registry.Register(command.CommandId, BuildImageTransformCommand(editor, command));
        // Picture Format tab — Arrange > Position.
        registry.Register("freew.image-position", new ImagePositionCommand(editor));
        // Picture Format tab — Adjust > Corrections (brightness/contrast presets + dialog).
        registry.Register("freew.image-brightness-plus20",  new ImageBrightnessPresetCommand(editor, +20));
        registry.Register("freew.image-brightness-plus40",  new ImageBrightnessPresetCommand(editor, +40));
        registry.Register("freew.image-brightness-minus20", new ImageBrightnessPresetCommand(editor, -20));
        registry.Register("freew.image-brightness-minus40", new ImageBrightnessPresetCommand(editor, -40));
        registry.Register("freew.image-contrast-plus20",    new ImageContrastPresetCommand(editor, +20));
        registry.Register("freew.image-contrast-minus20",   new ImageContrastPresetCommand(editor, -20));
        registry.Register("freew.image-adjust-dialog",      new ImageAdjustDialogCommand(editor));
        // Picture Format tab — Adjust > Color (saturation presets + dialog).
        registry.Register("freew.image-saturation-0",       new ImageSaturationPresetCommand(editor, 0));
        registry.Register("freew.image-saturation-50",      new ImageSaturationPresetCommand(editor, 50));
        registry.Register("freew.image-saturation-200",     new ImageSaturationPresetCommand(editor, 200));
        registry.Register("freew.image-color-dialog",       new ImageColorDialogCommand(editor));
        // Picture Format tab — Adjust > Transparency (presets + dialog).
        registry.Register("freew.image-transparency-25",    new ImageTransparencyPresetCommand(editor, 25));
        registry.Register("freew.image-transparency-50",    new ImageTransparencyPresetCommand(editor, 50));
        registry.Register("freew.image-transparency-75",    new ImageTransparencyPresetCommand(editor, 75));
        registry.Register("freew.image-transparency-dialog",new ImageTransparencyDialogCommand(editor));
        // Picture Format tab — Adjust > Crop / Reset / Border.
        registry.Register("freew.image-crop",   new ImageCropCommand(editor));
        registry.Register("freew.image-reset",  new ImageResetCommand(editor));
        registry.Register("freew.image-border", new ImageBorderCommand(editor));
        // Picture Format tab — Adjust > Color > Recolor presets.
        registry.Register("freew.image-recolor-grayscale",  new ImageRecolorPresetCommand(editor, ImageRecolorMode.Grayscale));
        registry.Register("freew.image-recolor-sepia",      new ImageRecolorPresetCommand(editor, ImageRecolorMode.Sepia));
        registry.Register("freew.image-recolor-washout",    new ImageRecolorPresetCommand(editor, ImageRecolorMode.Washout));
        registry.Register("freew.image-recolor-blackwhite", new ImageRecolorPresetCommand(editor, ImageRecolorMode.BlackWhite));
        registry.Register("freew.image-recolor-none",       new ImageRecolorPresetCommand(editor, ImageRecolorMode.None));
        // Picture Format tab — Adjust > Color > Color Tone presets.
        registry.Register("freew.image-colortemp-warm",    new ImageColorTempCommand(editor, +60));
        registry.Register("freew.image-colortemp-cool",    new ImageColorTempCommand(editor, -60));
        registry.Register("freew.image-colortemp-neutral", new ImageColorTempCommand(editor, 0));
        // Picture Format tab — Adjust > Picture Effects: Shadow presets.
        registry.Register("freew.image-shadow-none", new ImageShadowPresetCommand(editor, 0));
        registry.Register("freew.image-shadow-1",    new ImageShadowPresetCommand(editor, 1));
        registry.Register("freew.image-shadow-2",    new ImageShadowPresetCommand(editor, 2));
        registry.Register("freew.image-shadow-3",    new ImageShadowPresetCommand(editor, 3));
        registry.Register("freew.image-shadow-4",    new ImageShadowPresetCommand(editor, 4));
        registry.Register("freew.image-shadow-5",    new ImageShadowPresetCommand(editor, 5));
        // Picture Format tab — Adjust > Picture Effects: Reflection presets.
        registry.Register("freew.image-reflection-none", new ImageReflectionPresetCommand(editor, 0));
        registry.Register("freew.image-reflection-1",    new ImageReflectionPresetCommand(editor, 1));
        registry.Register("freew.image-reflection-2",    new ImageReflectionPresetCommand(editor, 2));
        registry.Register("freew.image-reflection-3",    new ImageReflectionPresetCommand(editor, 3));
        registry.Register("freew.image-reflection-4",    new ImageReflectionPresetCommand(editor, 4));
        registry.Register("freew.image-reflection-5",    new ImageReflectionPresetCommand(editor, 5));
        // Picture Format tab — Adjust > Picture Effects: Glow presets.
        registry.Register("freew.image-glow-none", new ImageGlowPresetCommand(editor, 0));
        registry.Register("freew.image-glow-5",    new ImageGlowPresetCommand(editor, 5));
        registry.Register("freew.image-glow-8",    new ImageGlowPresetCommand(editor, 8));
        registry.Register("freew.image-glow-11",   new ImageGlowPresetCommand(editor, 11));
        registry.Register("freew.image-glow-18",   new ImageGlowPresetCommand(editor, 18));
        // Picture Format tab — Adjust > Picture Effects: Soft Edges presets.
        registry.Register("freew.image-softedge-none",  new ImageSoftEdgeCommand(editor, 0));
        registry.Register("freew.image-softedge-1",     new ImageSoftEdgeCommand(editor, 1));
        registry.Register("freew.image-softedge-2pt5",  new ImageSoftEdgeCommand(editor, 2.5));
        registry.Register("freew.image-softedge-5",     new ImageSoftEdgeCommand(editor, 5));
        registry.Register("freew.image-softedge-10",    new ImageSoftEdgeCommand(editor, 10));
        // Picture Format tab — Adjust > Picture Effects: Bevel presets.
        registry.Register("freew.image-bevel-none", new ImageBevelPresetCommand(editor, 0));
        registry.Register("freew.image-bevel-1",    new ImageBevelPresetCommand(editor, 1));
        registry.Register("freew.image-bevel-2",    new ImageBevelPresetCommand(editor, 2));
        registry.Register("freew.image-bevel-3",    new ImageBevelPresetCommand(editor, 3));
        registry.Register("freew.image-bevel-4",    new ImageBevelPresetCommand(editor, 4));
        // Picture Format tab — Adjust > Artistic Effects (W25).
        // Each command sets InlineImage.ArtisticEffect and invalidates the render (non-destructive).
        registry.Register("freew.image-artistic-none",          new ImageArtisticEffectCommand(editor, ImageArtisticEffect.None));
        registry.Register("freew.image-artistic-blur",          new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Blur));
        registry.Register("freew.image-artistic-glow-diffused", new ImageArtisticEffectCommand(editor, ImageArtisticEffect.GlowDiffused));
        registry.Register("freew.image-artistic-glow-edges",    new ImageArtisticEffectCommand(editor, ImageArtisticEffect.GlowEdges));
        registry.Register("freew.image-artistic-pencil-gray",   new ImageArtisticEffectCommand(editor, ImageArtisticEffect.PencilGrayscale));
        registry.Register("freew.image-artistic-pencil-sketch", new ImageArtisticEffectCommand(editor, ImageArtisticEffect.PencilSketch));
        registry.Register("freew.image-artistic-line-drawing",  new ImageArtisticEffectCommand(editor, ImageArtisticEffect.LineDrawing));
        registry.Register("freew.image-artistic-paintbrush",    new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Paintbrush));
        registry.Register("freew.image-artistic-paint-strokes", new ImageArtisticEffectCommand(editor, ImageArtisticEffect.PaintStrokes));
        registry.Register("freew.image-artistic-photocopy",     new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Photocopy));
        registry.Register("freew.image-artistic-posterize",     new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Posterize));
        registry.Register("freew.image-artistic-pastels",       new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Pastels));
        registry.Register("freew.image-artistic-watercolor",    new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Watercolor));
        registry.Register("freew.image-artistic-film-grain",    new ImageArtisticEffectCommand(editor, ImageArtisticEffect.FilmGrain));
        registry.Register("freew.image-artistic-mosaic",        new ImageArtisticEffectCommand(editor, ImageArtisticEffect.Mosaic));
        // Artistic Effects: top-level gallery opener.
        registry.Register("freew.image-artistic",               new ActionRibbonCommand(() =>
        {
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose an artistic effect from the dropdown menu.", "Artistic Effects");
        }));
        // Picture Format tab — Picture Styles gallery presets.
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            var p = preset;
            registry.Register($"freew.image-style-{p.Id}", new ImageStylePresetCommand(editor, p));
        }
        // Picture Format tab — Arrange > Z-order (floating images only).
        foreach (var command in ObjectFormatCommandPlanner.ZOrderCommands(ObjectFormatTarget.Picture))
            registry.Register(command.CommandId, new FloatingZOrderCommand(
                editor, ObjectFormatTarget.Picture, command.Operation));
        // Picture Format / Drawing Format — Arrange > Group / Ungroup (Phase 4).
        registry.Register("freew.object-group",   new ObjectGroupCommand(editor));
        registry.Register("freew.object-ungroup", new ObjectUngroupCommand(editor));
        // Insert tab — Illustrations > Shapes: a small gallery of preset DrawingML shapes. Each menu item
        // inserts the matching Shape (preset geometry, or a text box carrying placeholder text) at the caret
        // via DocumentView.InsertShape. Round-trips through docx as an inline w:drawing/wps:wsp (see
        // DocxWriter/Reader). The top-level "freew.shapes" id only opens the menu (no direct insert).
        registry.Register("freew.shape-rectangle", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Rectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-rounded", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.RoundedRectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-ellipse", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Ellipse, widthPt: 100, heightPt: 100, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-textbox", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"));
        }));
        // Insert tab — Media: drop a sample equation / chart / WordArt / SmartArt / OLE object at the caret.
        // Each routes through the editor's undoable insert path (mirroring InsertShape) and round-trips
        // through docx (the model + IO already exist; this surfaces them in the ribbon). Sample content is a
        // starting point the user can replace.
        registry.Register("freew.equation", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertEquation(SampleEquation());
        }));
        // Equation gallery presets (Insert > Media > Equation dropdown). Each inserts one OMML structure
        // at the caret as an editable starting point; all round-trip through the model/IO layer.
        registry.Register("freew.equation-fraction", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Fraction("a", "b")]))));
        registry.Register("freew.equation-script", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.SubSuperscript("x", "n", "2")]))));
        registry.Register("freew.equation-radical", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x")]))));
        registry.Register("freew.equation-nthroot", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x", "n")]))));
        registry.Register("freew.equation-integral", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∫", "a", "b", "f(x) dx")]))));
        registry.Register("freew.equation-summation", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∑", "i=1", "n", "i")]))));
        registry.Register("freew.equation-product", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∏", "i=1", "n", "i")]))));
        registry.Register("freew.equation-accent", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.AccentOf("x")]))));
        registry.Register("freew.equation-bar", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.BarOf("x")]))));
        registry.Register("freew.equation-bracket", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Delimiter("a, b")]))));
        registry.Register("freew.equation-matrix", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]))));
        registry.Register("freew.equation-func", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.FunctionApply("sin", "x")]))));
        registry.Register("freew.equation-groupchr", new ActionRibbonCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.GroupCharOf("x+y")]))));
        registry.Register("freew.chart", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = InsertChartDialog.Prompt(Application.Current?.MainWindow);
            if (chart is not null)
                editor.InsertChart(chart);
        }));
        // Chart Design contextual tab commands — all mutate the selected chart's model + re-render.
        // Change Chart Type: picker over ChartKind.
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
        {
            var k = kind; // capture
            registry.Register($"freew.chart-type-{k.ToString().ToLowerInvariant()}", new ActionRibbonCommand(() =>
            {
                editor.Focus();
                editor.SetSelectedChartKind(k);
            }));
        }
        // Add Chart Element — toggle Legend.
        registry.Register("freew.chart-toggle-legend", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.ToggleSelectedChartLegend();
        }));
        // Add Chart Element — set/clear Chart Title.
        registry.Register("freew.chart-title", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var (accepted, newTitle) = ChartTitleDialog.Prompt(Application.Current?.MainWindow, chart.Title);
            if (accepted)
                editor.SetSelectedChartTitle(newTitle);
        }));
        // Add Chart Element — set axis titles.
        registry.Register("freew.chart-axis-titles", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var result = ChartAxisTitlesDialog.Prompt(Application.Current?.MainWindow, chart.CategoryAxisTitle, chart.ValueAxisTitle);
            if (result is not null)
                editor.SetSelectedChartAxisTitles(result.Value.CategoryTitle, result.Value.ValueTitle);
        }));
        // Edit Data — reopen the data grid dialog.
        registry.Register("freew.chart-edit-data", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var replacement = InsertChartDialog.Prompt(Application.Current?.MainWindow, chart);
            if (replacement is not null)
                editor.ReplaceSelectedChartData(replacement);
        }));
        // Chart Format contextual tab — Size dialog.
        var chartSizeCommand = new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var result = ChartSizeDialog.Prompt(Application.Current?.MainWindow, chart.WidthPt, chart.HeightPt);
            if (result is not null)
                editor.SetSelectedChartSize(result.Value.WidthPt, result.Value.HeightPt);
        });
        registry.Register("freew.chart-size", chartSizeCommand);
        registry.Register("freew.chart-size-dialog", chartSizeCommand);
        // ── Chart Design galleries — Quick Layout, Chart Styles, Change Colors ──────────────────────
        // Each gallery command applies one catalog entry to the selected chart and re-renders.
        // The MainWindow replaces the rendered ribbon buttons with live-preview swatches (ChartDesignGallery),
        // but the command registrations still back the buttons so the parity tests pass.
        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var l = layout;
            registry.Register(
                $"freew.chart-quick-layout-{l.Id}",
                new ChartQuickLayoutRibbonCommand(editor, l));
        }
        foreach (var style in ChartStyle.Catalog)
        {
            var s = style;
            registry.Register($"freew.chart-style-{s.Id}", new ActionRibbonCommand(() =>
            {
                editor.Focus();
                editor.ApplySelectedChartStyle(s);
            }));
        }
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var sc = scheme;
            registry.Register($"freew.chart-color-{sc.Id}", new ActionRibbonCommand(() =>
            {
                editor.Focus();
                editor.ApplySelectedChartColorScheme(sc);
            }));
        }
        // ── Drawing Format contextual tab — Shape/Drawing/TextBox/WordArt commands ─────────────────
        // Edit Shape > Convert to Freeform / Edit Points (W25).
        registry.Register("freew.shape-edit-shape", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose 'Convert to Freeform' or 'Edit Points' from the menu.", "Edit Shape");
        }));
        registry.Register("freew.shape-convert-freeform", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Convert to Freeform");
                return;
            }
            editor.ConvertSelectedShapeToFreeform();
        }));
        registry.Register("freew.shape-edit-points", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Edit Points");
                return;
            }
            // Convert to freeform first if not already, then show the edit-points mode.
            if (!shape.HasCustomGeometry)
                editor.ConvertSelectedShapeToFreeform();
            editor.BeginShapeEditPoints();
        }));
        // Change Shape: picker over ShapeKind; no model work — ShapeKind already exists.
        registry.Register("freew.shape-change-rectangle", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Change Shape");
                return;
            }
            editor.SetSelectedShapeKind(FreeW.Core.Model.ShapeKind.Rectangle);
        }));
        registry.Register("freew.shape-change-rounded", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Change Shape");
                return;
            }
            editor.SetSelectedShapeKind(FreeW.Core.Model.ShapeKind.RoundedRectangle);
        }));
        registry.Register("freew.shape-change-ellipse", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Change Shape");
                return;
            }
            editor.SetSelectedShapeKind(FreeW.Core.Model.ShapeKind.Ellipse);
        }));
        // Shape Fill: solid color picker or No Fill.
        registry.Register("freew.shape-fill", new ShapeFillCommand(editor));
        registry.Register("freew.shape-fill-no-fill", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Fill");
                return;
            }
            editor.SetSelectedShapeFill(null);
        }));
        // Shape Outline: reuse same dialog as image border; dash presets; No Outline option.
        registry.Register("freew.shape-outline", new ShapeOutlineCommand(editor));
        registry.Register("freew.shape-outline-no-outline", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Outline");
                return;
            }
            editor.SetSelectedShapeOutline(null, 0, null);
        }));
        registry.Register("freew.shape-outline-solid", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Outline");
                return;
            }
            editor.SetSelectedShapeOutline(shape.OutlineColorHex ?? "000000", Math.Max(0.75, shape.OutlineWidthPt), null);
        }));
        registry.Register("freew.shape-outline-dash", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Outline");
                return;
            }
            editor.SetSelectedShapeOutline(shape.OutlineColorHex ?? "000000", Math.Max(0.75, shape.OutlineWidthPt), "dash");
        }));
        registry.Register("freew.shape-outline-dot", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Outline");
                return;
            }
            editor.SetSelectedShapeOutline(shape.OutlineColorHex ?? "000000", Math.Max(0.75, shape.OutlineWidthPt), "sysDot");
        }));
        // Text Direction: Horizontal / Rotate 90 / Rotate 270 — text-box only.
        registry.Register("freew.shape-text-direction", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a text direction from the dropdown.", "Text Direction");
        }));
        registry.Register("freew.shape-text-horizontal", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a text box first.", "Text Direction");
                return;
            }
            editor.SetSelectedShapeTextDirection(FreeW.Core.Model.ShapeTextDirection.Horizontal);
        }));
        registry.Register("freew.shape-text-rotate90", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a text box first.", "Text Direction");
                return;
            }
            editor.SetSelectedShapeTextDirection(FreeW.Core.Model.ShapeTextDirection.Rotate90);
        }));
        registry.Register("freew.shape-text-rotate270", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a text box first.", "Text Direction");
                return;
            }
            editor.SetSelectedShapeTextDirection(FreeW.Core.Model.ShapeTextDirection.Rotate270);
        }));
        // Shape Size: reuse ImageSizeDialog (same W/H in points).
        registry.Register("freew.shape-size", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Size");
                return;
            }
            if (ImageSizeDialog.Prompt(Window.GetWindow(editor), shape.WidthPt, shape.HeightPt) is { } sz)
                editor.SetSelectedShapeSize(sz.Width, sz.Height);
        }));
        // Alt Text: text prompt for shape or WordArt.
        registry.Register("freew.shape-alt-text", new ShapeAltTextCommand(editor));
        // Shape align left/center/right (paragraph alignment of the containing run paragraph).
        registry.Register("freew.shape-align-left",   new ShapeAlignCommand(editor, FreeW.Core.Model.TextAlignment.Left));
        registry.Register("freew.shape-align-center", new ShapeAlignCommand(editor, FreeW.Core.Model.TextAlignment.Center));
        registry.Register("freew.shape-align-right",  new ShapeAlignCommand(editor, FreeW.Core.Model.TextAlignment.Right));
        // Drawing Tools > Arrange — align floating shapes relative to page or margin, or distribute evenly.
        registry.Register("freew.shape-align-to-page",   new FloatingAlignCommand(editor, FloatingObjectArrangeKind.AlignToPage));
        registry.Register("freew.shape-align-to-margin", new FloatingAlignCommand(editor, FloatingObjectArrangeKind.AlignToMargin));
        registry.Register("freew.shape-distribute-h", new FloatingDistributeCommand(editor, FloatingObjectArrangeKind.DistributeHorizontal));
        registry.Register("freew.shape-distribute-v", new FloatingDistributeCommand(editor, FloatingObjectArrangeKind.DistributeVertical));
        // Drawing Tools > Arrange — Wrap Text (6 modes for shapes, mirrors image-wrap-* pattern).
        foreach (var command in ObjectFormatCommandPlanner.WrapCommands(ObjectFormatTarget.Shape))
            registry.Register(command.CommandId, new ShapeWrapCommand(editor, command.Wrapping));
        // Drawing Tools > Arrange — Rotate / Flip (mirrors image-rotate-* / image-flip-* pattern).
        foreach (var command in ObjectFormatCommandPlanner.TransformCommands(ObjectFormatTarget.Shape))
            registry.Register(command.CommandId, BuildShapeTransformCommand(editor, command));
        registry.Register("freew.shape-rotate", EmptyRibbonCommand.Instance);
        foreach (var command in ObjectFormatCommandPlanner.ZOrderCommands(ObjectFormatTarget.Shape))
            registry.Register(command.CommandId, new FloatingZOrderCommand(
                editor, ObjectFormatTarget.Shape, command.Operation));
        // Drawing Tools > Arrange — Position (opens the same dialog as image-position, applied to shape).
        registry.Register("freew.shape-position", new ShapePositionCommand(editor));

        // ── Shape Styles gallery (W24) ────────────────────────────────────────────────────────────
        registry.Register("freew.shape-styles-gallery", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a shape style from the gallery.", "Shape Styles");
        }));
        // Register one command per style preset (40 presets) so the parity test can back each.
        foreach (var stylePreset in ShapeStylePreset.Catalog)
        {
            var sp = stylePreset;
            registry.Register($"freew.{sp.Id}", new ActionRibbonCommand(() =>
            {
                editor.Focus();
                if (editor.SelectedShape() is null)
                {
                    DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Style");
                    return;
                }
                editor.ApplySelectedShapeStyle(sp);
            }));
        }

        // ── Shape fill extensions (W24) ───────────────────────────────────────────────────────────
        registry.Register("freew.shape-fill-gradient-blue", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Fill");
                return;
            }
            editor.SetSelectedShapeExtendedFill(ShapeFill.LinearGradient(5400000,
                new FreeW.Core.Model.GradientStop(0, "#4472C4"), new FreeW.Core.Model.GradientStop(100000, "#1F4E79")));
        }));
        registry.Register("freew.shape-fill-gradient-orange", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Fill");
                return;
            }
            editor.SetSelectedShapeExtendedFill(ShapeFill.LinearGradient(5400000,
                new FreeW.Core.Model.GradientStop(0, "#ED7D31"), new FreeW.Core.Model.GradientStop(100000, "#C55A11")));
        }));
        registry.Register("freew.shape-fill-pattern-diag", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Fill");
                return;
            }
            editor.SetSelectedShapeExtendedFill(ShapeFill.Patterned("diagCross", "#4472C4", "#FFFFFF"));
        }));

        // ── Shape Effects (W24) ───────────────────────────────────────────────────────────────────
        registry.Register("freew.shape-effects", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose an effect from the dropdown.", "Shape Effects");
        }));
        registry.Register("freew.shape-effects-none", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(null);
        }));
        registry.Register("freew.shape-effect-shadow", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(new ShapeEffectLst { HasShadow = true });
        }));
        registry.Register("freew.shape-effect-glow", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(new ShapeEffectLst { HasGlow = true });
        }));
        registry.Register("freew.shape-effect-soft-edge", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(new ShapeEffectLst { HasSoftEdge = true });
        }));
        registry.Register("freew.shape-effect-reflection", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(new ShapeEffectLst { HasReflection = true });
        }));
        registry.Register("freew.shape-effect-bevel", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Effects");
                return;
            }
            editor.SetSelectedShapeEffects(new ShapeEffectLst { HasBevel = true });
        }));

        // ── WordArt style gallery — original four + extended eleven (W24) ─────────────────────────
        registry.Register("freew.wordart-style", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a WordArt style from the dropdown.", "WordArt Style");
        }));

        // Map each WordArtStyle to its ribbon command id (original four by legacy name, extended by slug).
        static string WordArtStyleId(WordArtStyle s) => s switch
        {
            WordArtStyle.FillBlue      => "freew.wordart-style-fill-blue",
            WordArtStyle.GradientFill  => "freew.wordart-style-gradient",
            WordArtStyle.Outline       => "freew.wordart-style-outline",
            WordArtStyle.Shadow        => "freew.wordart-style-shadow",
            WordArtStyle.FillGold      => "freew.wordart-style-fill-gold",
            WordArtStyle.FillWhite     => "freew.wordart-style-fill-white",
            WordArtStyle.GradFillMulti => "freew.wordart-style-grad-multi",
            WordArtStyle.ChromeOne     => "freew.wordart-style-chrome-one",
            WordArtStyle.ChromeTwo     => "freew.wordart-style-chrome-two",
            WordArtStyle.ShadowOrange  => "freew.wordart-style-shadow-orange",
            WordArtStyle.GlowBlue      => "freew.wordart-style-glow-blue",
            WordArtStyle.GlowGold      => "freew.wordart-style-glow-gold",
            WordArtStyle.Reflection    => "freew.wordart-style-reflection",
            WordArtStyle.Bevel         => "freew.wordart-style-bevel",
            WordArtStyle.PatternFill   => "freew.wordart-style-pattern",
            _                          => $"freew.wordart-style-{s.ToString().ToLowerInvariant()}"
        };

        foreach (WordArtStyle wapresent in Enum.GetValues<WordArtStyle>())
        {
            var p = wapresent;
            registry.Register(WordArtStyleId(p), new ActionRibbonCommand(() =>
            {
                editor.Focus();
                if (editor.SelectedWordArt() is null)
                {
                    DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select WordArt first.", "WordArt Style");
                    return;
                }
                editor.SetSelectedWordArtStyle(p);
            }));
        }

        // ── WordArt Transform / Warp (W24) ────────────────────────────────────────────────────────
        registry.Register("freew.wordart-transform", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a text transform from the dropdown.", "Text Effects: Transform");
        }));

        static string WarpId(WordArtWarp w) => w switch
        {
            WordArtWarp.None          => "freew.wordart-warp-none",
            WordArtWarp.ArchUp        => "freew.wordart-warp-arch-up",
            WordArtWarp.ArchDown      => "freew.wordart-warp-arch-down",
            WordArtWarp.Circle        => "freew.wordart-warp-circle",
            WordArtWarp.Button        => "freew.wordart-warp-button",
            WordArtWarp.Wave1         => "freew.wordart-warp-wave1",
            WordArtWarp.Wave2         => "freew.wordart-warp-wave2",
            WordArtWarp.Inflate       => "freew.wordart-warp-inflate",
            WordArtWarp.Deflate       => "freew.wordart-warp-deflate",
            WordArtWarp.InflateBottom => "freew.wordart-warp-inflate-bottom",
            WordArtWarp.ChevronUp     => "freew.wordart-warp-chevron-up",
            WordArtWarp.ChevronDown   => "freew.wordart-warp-chevron-down",
            WordArtWarp.FadeRight     => "freew.wordart-warp-fade-right",
            WordArtWarp.FadeLeft      => "freew.wordart-warp-fade-left",
            WordArtWarp.SlantUp       => "freew.wordart-warp-slant-up",
            WordArtWarp.SlantDown     => "freew.wordart-warp-slant-down",
            _                         => $"freew.wordart-warp-{w.ToString().ToLowerInvariant()}"
        };

        foreach (WordArtWarp warp in Enum.GetValues<WordArtWarp>())
        {
            var w = warp;
            registry.Register(WarpId(w), new ActionRibbonCommand(() =>
            {
                editor.Focus();
                if (editor.SelectedWordArt() is null)
                {
                    DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select WordArt first.", "Text Effects: Transform");
                    return;
                }
                editor.SetSelectedWordArtWarp(w);
            }));
        }
        // ── End Drawing Format commands ───────────────────────────────────────────────────────────

        registry.Register("freew.wordart", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertWordArt(WordArt.Create("WordArt", WordArtStyle.GradientFill));
        }));
        registry.Register("freew.smartart", new ActionRibbonCommand(() =>
        {
            var owner = Application.Current?.MainWindow;
            var result = InsertSmartArtDialog.Prompt(owner);
            if (result is null) return;
            editor.Focus();
            editor.InsertSmartArt(result);
        }));
        // SmartArt Design contextual tab — node mutation commands.
        registry.Register("freew.smartart-add-shape", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.AddShape, editor.SmartArtAddShape));
        registry.Register("freew.smartart-remove-shape", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.RemoveShape, editor.SmartArtRemoveShape));
        registry.Register("freew.smartart-promote", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.Promote, editor.SmartArtPromote));
        registry.Register("freew.smartart-demote", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.Demote, editor.SmartArtDemote));
        registry.Register("freew.smartart-move-up", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.MoveUp, editor.SmartArtMoveUp));
        registry.Register("freew.smartart-move-down", new SmartArtStructureRibbonCommand(
            editor, SmartArtStructureOperation.MoveDown, editor.SmartArtMoveDown));
        registry.Register("freew.smartart-edit-text", new SmartArtEditTextRibbonCommand(editor));
        // SmartArt Design contextual tab — gallery placeholder commands (no-ops; galleries are injected
        // as live-preview custom content via InjectGallery; these ids must be registered so the ribbon
        // renderer does not log "unknown command" warnings for the stub buttons).
        registry.Register("freew.smartart-change-layout", EmptyRibbonCommand.Instance);
        registry.Register("freew.smartart-change-colors", EmptyRibbonCommand.Instance);
        registry.Register("freew.smartart-change-style", new SmartArtStyleRibbonCommand(editor));
        registry.Register("freew.object", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertEmbeddedObject(SampleEmbeddedObject());
        }));
        // Insert tab — Links: prompt for a URL and apply it as a hyperlink over the selection.
        registry.Register("freew.hyperlink", new InsertHyperlinkCommand(editor));
        // Insert tab — Links: manage the hyperlink at the caret — change its URL, remove it, or set a ScreenTip.
        registry.Register("freew.edit-hyperlink", new EditHyperlinkCommand(editor));
        registry.Register("freew.remove-hyperlink", new RemoveHyperlinkCommand(editor));
        registry.Register("freew.hyperlink-tooltip", new HyperlinkTooltipCommand(editor));
        // Insert tab — References: prompt for footnote text and insert a footnote reference at the caret.
        registry.Register("freew.footnote", new InsertFootnoteCommand(editor));
        // Insert tab — References: prompt for endnote text and insert an endnote reference at the caret.
        registry.Register("freew.endnote", new InsertEndnoteCommand(editor));
        registry.Register("freew.next-footnote", new NavigateNoteCommand(editor, footnote: true, previous: false));
        registry.Register("freew.previous-footnote", new NavigateNoteCommand(editor, footnote: true, previous: true));
        registry.Register("freew.next-endnote", new NavigateNoteCommand(editor, footnote: false, previous: false));
        registry.Register("freew.previous-endnote", new NavigateNoteCommand(editor, footnote: false, previous: true));
        if (onToggleNotesPane is not null && isNotesPaneVisible is not null)
        {
            var notesPaneCmd = new FreeWStatefulToggleCommand(
                onToggleNotesPane,
                isNotesPaneVisible,
                editor.CommitToModel);
            registry.Register("freew.show-notes", notesPaneCmd);
            stateful.Add(("freew.show-notes", notesPaneCmd));
        }
        else
        {
            registry.Register("freew.show-notes", new ShowNotesCommand(editor));
        }
        // Insert tab — References: open the Footnote and Endnote numbering options dialog (number format,
        // start-at, restart mode). Applies to w:footnotePr / w:endnotePr in settings.xml.
        registry.Register("freew.footnote-endnote-options", new FootnoteEndnoteOptionsCommand(editor));
        // Insert tab — References: generate a Table of Contents from the heading outline at the caret,
        // and rebuild it in place (remove the prior TOC region + re-insert). Both route through the bus.
        registry.Register("freew.toc", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfContents(); }));
        registry.Register("freew.toc-refresh", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfContents(); }));
        registry.Register("freew.toc-add-text", new ApplyTocStyleCommand(editor, "Heading1"));
        registry.Register("freew.toc-addtext-none", new ApplyTocStyleCommand(editor, "Normal"));
        registry.Register("freew.toc-addtext-level1", new ApplyTocStyleCommand(editor, "Heading1"));
        registry.Register("freew.toc-addtext-level2", new ApplyTocStyleCommand(editor, "Heading2"));
        registry.Register("freew.toc-addtext-level3", new ApplyTocStyleCommand(editor, "Heading3"));
        // Insert tab — References: insert an in-text citation (pick an existing source or add a new one),
        // and insert a bibliography built from the document's sources at the caret (reversible).
        registry.Register("freew.citation", new InsertCitationCommand(editor));
        registry.Register("freew.manage-sources", new ManageSourcesCommand(editor));
        registry.Register("freew.bibliography", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertBibliography(); }));
        // Insert tab — References: select the active citation/bibliography style (APA / MLA / Chicago) used
        // by the citation + bibliography commands. The combo box delivers its label as SelectedValue.
        var citationStyle = new CitationStyleCommand(editor, stateStore);
        registry.Register("freew.citation-style", citationStyle);
        stateful.Add(("freew.citation-style", citationStyle));
        stateStore.SetState("freew.citation-style", citationStyle.GetState());
        // Insert tab — References: insert a numbered figure/table caption under the caret's block.
        registry.Register("freew.caption", new InsertCaptionCommand(editor));
        registry.Register("freew.insert-caption.figure", new InsertCaptionLabelCommand(editor, CaptionLabel.Figure));
        registry.Register("freew.insert-caption.table", new InsertCaptionLabelCommand(editor, CaptionLabel.Table));
        registry.Register("freew.insert-caption.equation", new InsertCaptionLabelCommand(editor, CaptionLabel.Equation));
        // Insert tab — References: insert a cross-reference (heading/bookmark/caption/footnote) at the caret.
        registry.Register("freew.cross-reference", new InsertCrossReferenceCommand(editor));
        // Insert tab — References: mark the selection (or a prompted term) for the document index, and
        // insert an alphabetical index built from the marked terms at the caret (reversibly via the bus).
        registry.Register("freew.index-mark", new MarkIndexEntryCommand(editor));
        registry.Register("freew.index-insert", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertIndex(); }));
        registry.Register("freew.index-refresh", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshIndex(); }));
        // Insert tab — References: generate a Table of Figures from the document's figure captions at the
        // caret, and rebuild it in place (remove the prior region + re-insert). Both route through the bus.
        registry.Register("freew.tof", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(); }));
        registry.Register("freew.tof.figure", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Figure); }));
        registry.Register("freew.tof.table", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Table); }));
        registry.Register("freew.tof.equation", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Equation); }));
        registry.Register("freew.tof-refresh", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(); }));
        registry.Register("freew.tof-refresh.figure", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Figure); }));
        registry.Register("freew.tof-refresh.table", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Table); }));
        registry.Register("freew.tof-refresh.equation", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Equation); }));
        // Insert tab — References: mark the selection as a legal citation (a hidden TA field), and insert /
        // rebuild a Table of Authorities built from those marks, grouped by category (reversibly via the bus).
        registry.Register("freew.mark-citation", new MarkCitationCommand(editor));
        registry.Register("freew.table-of-authorities", new InsertTableOfAuthoritiesCommand(editor));
        registry.Register("freew.table-of-authorities-refresh", new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfAuthorities(); }));
        // Insert tab — Links: name the caret's paragraph as a bookmark target (an invisible marker).
        registry.Register("freew.bookmark", new InsertBookmarkCommand(editor));
        // Insert tab — Links: apply an internal link (to an existing bookmark) over the selection.
        registry.Register("freew.link-bookmark", new LinkToBookmarkCommand(editor));
        // Insert tab — Links: open the Bookmark Manager (list bookmarks with Go To + Delete).
        registry.Register("freew.bookmark-manager", new BookmarkManagerCommand(editor));

        // Insert tab — Quick Parts (AutoText): a shared snippet library persisted under FreeW's data
        // folder. "Save Selection" captures the selection's text and stores it under a prompted name;
        // "Insert Quick Part" picks a saved snippet and drops its text at the caret (reversibly).
        var quickParts = QuickPartLibrary.Load();
        registry.Register("freew.save-quickpart", new SaveQuickPartCommand(editor, quickParts));
        registry.Register("freew.insert-quickpart", new InsertQuickPartCommand(editor, quickParts));
        // "Building Blocks Organizer" opens a manager over that same library: list + preview, Insert, Delete.
        registry.Register("freew.building-blocks-organizer", new BuildingBlocksOrganizerCommand(editor, quickParts));

        // Insert tab — Controls: insert a content control (w:sdt) around the selection. The plain-text
        // control wraps the selection (or a placeholder) as an editable region; the checkbox control
        // drops a toggleable ☐/☒ checkbox. Both round-trip through docx as a w:sdt.
        registry.Register("freew.cc-text", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertPlainTextControl(); }));
        registry.Register("freew.cc-richtext", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertRichTextControl(); }));
        registry.Register("freew.cc-checkbox", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCheckBoxControl(); }));
        registry.Register("freew.cc-date", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertDatePickerControl(); }));
        registry.Register("freew.cc-dropdown", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertDropDownListControl(); }));
        registry.Register("freew.cc-combo", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertComboBoxControl(); }));

        // Review tab — Comments: prompt for comment text and attach it over the current selection.
        registry.Register("freew.new-comment", new NewCommentCommand(editor));
        // Review tab — Comments: reply to / resolve the comment thread covering the caret (modern threaded
        // comments). Reply prompts for text and appends a child comment; Resolve toggles the thread's done flag.
        registry.Register("freew.reply-comment", new ReplyCommentCommand(editor));
        registry.Register("freew.resolve-comment", new ResolveCommentCommand(editor));
        registry.Register("freew.delete-comment", new DeleteCommentCommand(editor));
        registry.Register("freew.previous-comment", new NavigateCommentCommand(editor, previous: true));
        registry.Register("freew.next-comment", new NavigateCommentCommand(editor, previous: false));
        registry.Register("freew.show-comments", new ShowCommentsCommand(editor));

        // Review tab — Proofing: open the read-only Word Count / Statistics dialog. Commits pending
        // edits first so the counts reflect the current text, then computes from the model.
        registry.Register("freew.statistics", new StatisticsCommand(editor));

        // Review tab — Proofing > Thesaurus (Shift+F7): opens the Thesaurus docked pane and looks up
        // synonyms for the selected/caret word in the bundled compact synonym dictionary (~3 000 headwords,
        // Moby II derivative, public domain). The action callback supplied by the host toggles the pane
        // and triggers a lookup; a no-op is registered when no host callback is wired (e.g. unit tests).
        if (onToggleThesaurus is not null)
            registry.Register("freew.thesaurus", new ActionRibbonCommand(onToggleThesaurus));
        else
            registry.Register("freew.thesaurus", new ActionRibbonCommand(() =>
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    "Thesaurus: no synonyms pane is wired. Host must supply onToggleThesaurus.", "Thesaurus");
            }));

        // Review tab — Show Markup > Show Revisions in Balloons: toggle the right-margin balloon overlay.
        // Comments and tracked-change revisions render as rounded rectangle callouts connected to their
        // anchored text by dashed leader lines, in a 200px strip to the right of the editor. The callback
        // is supplied by the host (BalloonOverlay.Toggle()); a no-op is registered in unit-test contexts.
        if (onToggleBalloons is not null)
            registry.Register("freew.show-markup-balloons", new ActionRibbonCommand(onToggleBalloons));
        else
            registry.Register("freew.show-markup-balloons", EmptyRibbonCommand.Instance);

        // Review tab — Proofing: custom dictionary + spelling options. The custom dictionary is a
        // word-per-line .lex file persisted under FreeW's data folder; its Uri is registered with the
        // editor's WPF spell checker so those words stop being flagged. "Add to Dictionary" takes the
        // misspelled word at the caret, adds it to the dictionary (+ persists), and re-reads the file so
        // it is no longer underlined. "Spell Check" is a stateful toggle over SpellCheck.IsEnabled.
        var customDictionary = CustomDictionaryStore.Load();
            editor.RegisterCustomDictionary(customDictionary.EnsurePersisted());
        registry.Register("freew.add-to-dictionary", new AddToDictionaryCommand(editor, customDictionary));
        var spellCheckToggle = new SpellCheckToggleCommand(editor);
        registry.Register("freew.spellcheck-toggle", spellCheckToggle);
        stateful.Add(("freew.spellcheck-toggle", spellCheckToggle));

        // Review tab — Speech > Read Aloud: a stateful toggle over an in-box text-to-speech read-through
        // (System.Speech via SystemSpeechEngine, behind the model's ISpeechEngine so the controller stays
        // testable). Toggling ON commits pending edits, maps the caret to the matching speakable segment,
        // and starts reading from there to the end of the document; toggling OFF stops. The checked state
        // reflects whether a read-through is active (so the ribbon shows it at a glance), and the controller
        // pushes its state back into the store when reading finishes on its own. Construction is robust on a
        // machine with no installed voice (the engine degrades to a no-op rather than crashing).
        var readAloud = new ReadAloudToggleCommand(editor);
        readAloud.StateChanged += () => stateStore.SetState("freew.read-aloud", readAloud.GetState());
        registry.Register("freew.read-aloud", readAloud);
        stateful.Add(("freew.read-aloud", readAloud));

        // Review tab — Tracking/Changes: toggle Track Changes mode (stateful so the ribbon reflects it). When
        // ON, marking the current selection as a tracked insertion/deletion is offered; turning it on
        // with a non-empty selection marks that selection as an insertion. Accept All / Reject All resolve
        // every tracked change on the model from the Changes dropdowns.
        registry.Register("freew.track-changes", new TrackChangesToggleCommand(editor));
        registry.Register("freew.track-formatting", new TrackFormattingToggleCommand(editor));
        registry.Register("freew.accept-all", new ActionRibbonCommand(() => { editor.Focus(); editor.AcceptAllRevisions(); }));
        registry.Register("freew.reject-all", new ActionRibbonCommand(() => { editor.Focus(); editor.RejectAllRevisions(); }));

        // Review tab — Tracking display controls: Display for Review and Show Markup per-category toggles.
        //
        // Display for Review exposes a dropdown backed by ReviewDisplayMode. The root button
        // always reflects the current mode. No Markup and Original are now implemented — each hides the
        // opposite set of revision runs using a visually-transparent technique that keeps every run in
        // the WPF tree so CommitToModel can round-trip text + RevisionMarker safely.
        var displayForReview = new DisplayForReviewCommand(editor);
        registry.Register("freew.display-for-review", displayForReview);
        registry.Register("freew.display-for-review-all-markup", displayForReview);
        stateful.Add(("freew.display-for-review", displayForReview));

        var displaySimpleMarkup = new DisplayForReviewSimpleMarkupCommand(editor);
        registry.Register("freew.display-for-review-simple-markup", displaySimpleMarkup);
        stateful.Add(("freew.display-for-review-simple-markup", displaySimpleMarkup));

        var displayNoMarkup = new DisplayForReviewNoMarkupCommand(editor);
        registry.Register("freew.display-for-review-no-markup", displayNoMarkup);
        stateful.Add(("freew.display-for-review-no-markup", displayNoMarkup));

        var displayOriginal = new DisplayForReviewOriginalCommand(editor);
        registry.Register("freew.display-for-review-original", displayOriginal);
        stateful.Add(("freew.display-for-review-original", displayOriginal));

        // Show Markup > Insertions and Deletions: stateful toggle — OFF suppresses the revision colour
        // and underline/strikethrough chrome but the RevisionMarker tag is still written so revisions
        // survive CommitToModel unchanged (round-trip safe).
        var showInsertions = new ShowMarkupInsertionsDeletionsCommand(editor);
        registry.Register("freew.show-markup-insertions-deletions", showInsertions);
        stateful.Add(("freew.show-markup-insertions-deletions", showInsertions));

        // Show Markup > Comments: stateful toggle — OFF suppresses the comment background highlight
        // but the CommentMarker tag is still written so comment ids survive CommitToModel unchanged
        // (round-trip safe).
        var showComments = new ShowMarkupCommentsCommand(editor);
        registry.Register("freew.show-markup-comments", showComments);
        stateful.Add(("freew.show-markup-comments", showComments));

        // Show Markup > Formatting: stateful toggle — OFF suppresses the dotted underline decoration
        // that marks tracked formatting changes. The FormatRevisionMarker tag is still written
        // unconditionally so FormatRevision survives CommitToModel unchanged (round-trip safe).
        var showFormatting = new ShowMarkupFormattingCommand(editor);
        registry.Register("freew.show-markup-formatting", showFormatting);
        stateful.Add(("freew.show-markup-formatting", showFormatting));

        // The root "Show Markup" button opens the dropdown; no direct action needed, but the command id
        // still needs to be backed so the parity assertion passes.
        registry.Register("freew.show-markup", EmptyRibbonCommand.Instance);

        // Review tab — single-revision reviewing surface (the Reviewing Pane). The toggle shows/hides the
        // dockable revisions list; Accept/Reject act on the SELECTED single change and Previous/Next step
        // through them. All four delegate to the host, which owns the pane and drives the pure RevisionList.
        if (onToggleReviewingPane is not null && isReviewingPaneVisible is not null)
            registry.Register("freew.reviewing-pane",
                new ToggleActionCommand(onToggleReviewingPane, isReviewingPaneVisible));
        if (onAcceptThisChange is not null)
            registry.Register("freew.accept-this", new ActionRibbonCommand(onAcceptThisChange));
        if (onRejectThisChange is not null)
            registry.Register("freew.reject-this", new ActionRibbonCommand(onRejectThisChange));
        if (onPreviousChange is not null)
            registry.Register("freew.previous-change", new ActionRibbonCommand(onPreviousChange));
        if (onNextChange is not null)
            registry.Register("freew.next-change", new ActionRibbonCommand(onNextChange));

        // Review tab — Protect: Mark as Final. A stateful toggle over Word's advisory read-only flag:
        // turning it on makes the editor read-only, shows the "Marked as Final" banner and persists the
        // _MarkAsFinal custom property; "Edit Anyway" (or toggling off) clears it. The checked state
        // reflects whether the document is currently marked final.
        var markAsFinal = new MarkAsFinalToggleCommand(editor);
        registry.Register("freew.mark-as-final", markAsFinal);
        stateful.Add(("freew.mark-as-final", markAsFinal));

        // Review tab — Protect: Restrict Editing. Opens the Restrict Editing pane to choose the allowed
        // editing type (No changes / Tracked changes / Comments / Filling in forms) and start enforcing,
        // or stop protection. The chosen mode is enforced on the live editor and emits word/settings.xml's
        // w:documentProtection on save. The toggle reflects whether protection is currently enforced.
        var restrictEditing = new RestrictEditingToggleCommand(editor);
        registry.Register("freew.restrict-editing", restrictEditing);
        stateful.Add(("freew.restrict-editing", restrictEditing));

        // Review tab — Compare: open a second .docx and load a comparison of the current document against
        // it as tracked changes (insertions/deletions relative to the opened "original").
        registry.Register("freew.compare", new CompareDocumentsCommand(editor));

        // Review tab — Combine: open the original (base) document plus a second reviewer's revised copy and
        // merge BOTH reviewers' edits (the current document is reviewer A, the opened file is reviewer B)
        // into one document whose tracked changes preserve each reviewer's authorship.
        registry.Register("freew.combine", new CombineDocumentsCommand(editor));

        // Review tab — Inspect Document: report the metadata the document carries (comments, tracked
        // changes, document properties, bookmarks) via the pure DocumentInspector, and let the user
        // selectively remove categories. Applied removals mutate editor.Model in place and re-render.
        registry.Register("freew.inspect-document", new InspectDocumentCommand(editor));

        // Review tab — Inspect > Check Accessibility: commit pending edits, run the pure AccessibilityChecker
        // over the model, and show the report (issues grouped by severity) in a read-only modal. Read-only.
        registry.Register("freew.check-accessibility", new CheckAccessibilityCommand(editor));

        // Insert tab — Header & Footer: prompt for header/footer text, or drop a page-number field
        // into the footer. These edit the model's Header/Footer directly (saved into docx + printed).
        registry.Register("freew.header", new HeaderFooterCommand(editor, isFooter: false, askHeaderFooterText: askHeaderFooterText));
        registry.Register("freew.footer", new HeaderFooterCommand(editor, isFooter: true, askHeaderFooterText: askHeaderFooterText));
        // Insert > Header & Footer > Page Number gallery: top/bottom/current position + format dialog.
        // The top-level id inserts into the footer (Word's default button-face action).
        registry.Register("freew.page-number", new InsertPageNumberCommand(editor, PageNumberPosition.Bottom));
        registry.Register("freew.page-number-top", new InsertPageNumberCommand(editor, PageNumberPosition.Top));
        registry.Register("freew.page-number-bottom", new InsertPageNumberCommand(editor, PageNumberPosition.Bottom));
        registry.Register("freew.page-number-current", new InsertPageNumberCommand(editor, PageNumberPosition.Current));
        registry.Register("freew.page-number-format", new PageNumberFormatCommand(editor));
        registry.Register("freew.field", new InsertFieldCommand(editor));
        registry.Register("freew.toggle-field-codes", new ToggleFieldCodesCommand(editor));
        registry.Register("freew.update-fields", new UpdateFieldsCommand(editor));

        // Header & Footer Design contextual tab — per-slot editors.
        // Slot naming: "header"/"footer" = default; "even-header"/"even-footer" = even pages;
        // "first-header"/"first-footer" = first page. Each writes FinalSectionHeadersFooters directly.
        // When the host supplies onOpenHeaderFooterPane, the commands open the docked pane (which
        // preserves run formatting). Otherwise they fall back to the plain-text dialog.
        IRibbonCommand HfEditCmd(string slot) =>
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, slot, onOpenHeaderFooterPane)
                : new EditHeaderSlotCommand(editor, slot);
        registry.Register("freew.hf-edit-header",       HfEditCmd("header"));
        registry.Register("freew.hf-edit-footer",       HfEditCmd("footer"));
        registry.Register("freew.hf-edit-even-header",  HfEditCmd("even-header"));
        registry.Register("freew.hf-edit-even-footer",  HfEditCmd("even-footer"));
        registry.Register("freew.hf-edit-first-header", HfEditCmd("first-header"));
        registry.Register("freew.hf-edit-first-footer", HfEditCmd("first-footer"));

        // Header & Footer Design contextual tab — options toggles (stateful so IsChecked reflects model).
        var diffFirstPage = new DifferentFirstPageToggleCommand(editor);
        registry.Register("freew.hf-different-first-page", diffFirstPage);
        stateful.Add(("freew.hf-different-first-page", diffFirstPage));

        var diffOddEven = new DifferentOddEvenPagesCommand(editor);
        registry.Register("freew.hf-different-odd-even", diffOddEven);
        stateful.Add(("freew.hf-different-odd-even", diffOddEven));

        // Header & Footer Design contextual tab — position numerics (stateful so the value tracks model).
        var headerFromTop = new HeaderFromTopCommand(editor);
        registry.Register("freew.hf-header-from-top", headerFromTop);
        stateful.Add(("freew.hf-header-from-top", headerFromTop));

        var footerFromBottom = new FooterFromBottomCommand(editor);
        registry.Register("freew.hf-footer-from-bottom", footerFromBottom);
        stateful.Add(("freew.hf-footer-from-bottom", footerFromBottom));

        // Header & Footer Design contextual tab — navigation + close.
        // Go-to-header / go-to-footer open the pane (when available) for the default slots.
        registry.Register("freew.hf-go-to-header",
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, "header", onOpenHeaderFooterPane)
                : new GoToHeaderCommand(editor));
        registry.Register("freew.hf-go-to-footer",
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, "footer", onOpenHeaderFooterPane)
                : new GoToFooterCommand(editor));
        // Close Header and Footer: hides the pane (when available) and returns focus to the body.
        registry.Register("freew.hf-close",
            onCloseHeaderFooterPane is not null
                ? new ActionRibbonCommand(onCloseHeaderFooterPane)
                : new CloseHeaderFooterCommand(editor));

        // Header & Footer Design contextual tab — insert into default header/footer slot.
        registry.Register("freew.hf-insert-page-number",  new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.PageNumber));
        registry.Register("freew.hf-insert-page-number-footer", new InsertIntoHeaderSlotCommand(editor, isFooter: true,  InsertSlotKind.PageNumber));
        registry.Register("freew.hf-insert-datetime",     new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DateTime));
        registry.Register("freew.hf-insert-field",        new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DocumentInfo));

        // Insert tab — Symbols: pick a glyph from a grid, or a formatted current date/time string, and
        // insert it at the caret as ordinary text (flows through the normal edit/undo path).
        registry.Register("freew.symbol", new InsertSymbolCommand(editor));
        registry.Register("freew.datetime", new InsertDateTimeCommand(editor));

        // Home > Font > Text Colour / Highlight: pick a colour from a small palette and apply it to
        // the selection (foreground reuses TextElement.Foreground; highlight uses TextElement.Background).
        registry.Register("freew.font-color", new ColorPickCommand(editor, isHighlight: false));
        registry.Register("freew.highlight", new ColorPickCommand(editor, isHighlight: true));

        // Home > Font: clear all character formatting in the selection (reset every run to the document
        // default, keeping text). Insert > Pages: apply a drop cap (enlarged leading letter) to the
        // caret's paragraph. Both route through the view's undo/redo bus and re-render.
        registry.Register("freew.clear-formatting", new ActionRibbonCommand(() => editor.ClearFormatting()));
        // Drop Cap top-level button: apply default (Dropped, 3 lines, 42 pt). Dropdown items:
        // Dropped / In Margin (apply with explicit position) / None (remove) / Options dialog.
        registry.Register("freew.drop-cap",          new ActionRibbonCommand(() => editor.ApplyDropCap()));
        registry.Register("freew.drop-cap-dropped",  new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        registry.Register("freew.drop-cap-in-margin",new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        registry.Register("freew.drop-cap-none",     new ActionRibbonCommand(() => editor.ClearDropCap()));
        registry.Register("freew.drop-cap-options",  new DropCapOptionsCommand(editor));
        registry.Register("freew.drop-cap.dropped",  new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        registry.Register("freew.drop-cap.in-margin",new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        registry.Register("freew.drop-cap.none",     new ActionRibbonCommand(() => editor.ClearDropCap()));

        // Insert > Text Box gallery: preset-styled text boxes.  Simple is the plain box (matches the
        // existing freew.shape-textbox behaviour); Sidebar/Banded adds a dark accent fill; Quote
        // indents the text and italicises it. All insert via the existing InsertShape path and round-trip
        // as an inline w:drawing/wps:wsp in docx.
        registry.Register("freew.textbox-simple",  new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.textbox-sidebar", new ActionRibbonCommand(() =>
        {
            editor.Focus();
            // Banded sidebar: dark blue fill with white text paragraph.
            var shape = new FreeW.Core.Model.Shape(FreeW.Core.Model.ShapeKind.TextBox, widthPt: 140, heightPt: 200, fillColorHex: "#243F60");
            var p = new FreeW.Core.Model.Paragraph();
            p.Runs.Add(new FreeW.Core.Model.Run("Sidebar", new FreeW.Core.Model.RunFormatting { Bold = true, ColorHex = "#FFFFFF" }));
            shape.TextParagraphs.Add(p);
            editor.InsertShape(shape);
        }));
        registry.Register("freew.textbox-quote",   new ActionRibbonCommand(() =>
        {
            editor.Focus();
            // Quote: light grey fill, indented italic text.
            var shape = new FreeW.Core.Model.Shape(FreeW.Core.Model.ShapeKind.TextBox, widthPt: 200, heightPt: 90, fillColorHex: "#F2F2F2");
            var p = new FreeW.Core.Model.Paragraph();
            p.Runs.Add(new FreeW.Core.Model.Run("“Quote text here”",
                new FreeW.Core.Model.RunFormatting { Italic = true }));
            shape.TextParagraphs.Add(p);
            editor.InsertShape(shape);
        }));

        // Insert > Quick Parts > Document Property: insert a live field run that renders the matching
        // document-property value. Uses RunFieldKind so it round-trips as w:fldSimple in docx.
        foreach (var plan in DocumentPropertyFieldPlanner.CommandPlans)
            registry.Register(plan.CommandId, new InsertDocPropFieldCommand(editor, plan.Kind));

        // Home > Font > Change Case: open a small menu to pick a target case (UPPERCASE / lowercase /
        // Sentence case / Capitalize Each Word / tOGGLE cASE) and recase the selection's text via the
        // pure ChangeCase helper. The replacement flows through the editor's normal edit/undo path.
        registry.Register("freew.change-case", new ChangeCaseCommand(editor));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection,
        // and toggle Add/Remove Space Before/After. All route through the view's undo/redo bus.
        var lineSpacing = new LineSpacingCommand(editor);
        registry.Register("freew.line-spacing", lineSpacing);
        stateful.Add(("freew.line-spacing", lineSpacing));
        stateStore.SetState("freew.line-spacing", lineSpacing.GetState());
        registry.Register("freew.space-before-toggle", new ActionRibbonCommand(() => editor.ToggleSpaceBefore()));
        registry.Register("freew.space-after-toggle", new ActionRibbonCommand(() => editor.ToggleSpaceAfter()));

        // Layout > Paragraph > numeric indent/spacing combos: exact-value controls that mirror Word's
        // Layout tab Paragraph group. Each is stateful so SelectionChanged can push the live value
        // back into the ribbon combo and the displayed number tracks the current paragraph.
        var indentLeft = new IndentLeftCommand(editor);
        registry.Register("freew.indent-left", indentLeft);
        stateful.Add(("freew.indent-left", indentLeft));

        var indentRight = new IndentRightCommand(editor);
        registry.Register("freew.indent-right", indentRight);
        stateful.Add(("freew.indent-right", indentRight));

        var spaceBefore = new SpaceBeforeCommand(editor);
        registry.Register("freew.space-before", spaceBefore);
        stateful.Add(("freew.space-before", spaceBefore));

        var spaceAfter = new SpaceAfterCommand(editor);
        registry.Register("freew.space-after", spaceAfter);
        stateful.Add(("freew.space-after", spaceAfter));

        // Home > Font > Font dialog-launcher (freew.font-dialog): opens a two-tab dialog (Font tab +
        // Advanced tab) covering family/size/style/colour/effects on the Font tab and the full OpenType
        // advanced typography fields (CharacterSpacingPt, KerningMinSizePt, PositionPt, Ligatures,
        // StylisticSet, NumberForm, NumberSpacing) on the Advanced tab. Applies via ApplyFontFormatting
        // which pushes both WPF property values and model-only fields through the undo/redo bus.
        registry.Register("freew.font-dialog", new FontDialogCommand(editor));

        // Home > Paragraph: increase/decrease the left indent by one 0.5in step over the selection, and
        // open the Paragraph dialog to set left/right/first-line (incl. hanging) indents. All reversible.
        registry.Register("freew.indent-increase", new ActionRibbonCommand(() => { editor.Focus(); editor.IncreaseIndent(); }));
        registry.Register("freew.indent-decrease", new ActionRibbonCommand(() => { editor.Focus(); editor.DecreaseIndent(); }));
        // freew.paragraph-dialog now opens the full two-tab Paragraph dialog (Indents and Spacing +
        // Line and Page Breaks), replacing the previous single-tab ParagraphIndentCommand. All fields
        // that ParagraphIndentCommand previously handled are present on the Indents and Spacing tab.
        registry.Register("freew.paragraph-dialog", new ParagraphDialogCommand(editor));
        registry.Register("freew.tabs-dialog", new TabsCommand(editor));

        // Home > Clipboard: Paste Special offers source-preserving RTF at an empty paragraph, plus
        // merge-destination and text-only paths. It uses real System.Windows.Clipboard format checks.
        registry.Register("freew.paste-special", new PasteSpecialCommand(editor));

        // Home > Paragraph: toggle a box border on the selected paragraph(s), and pick/clear shading.
        registry.Register("freew.para-border", new ActionRibbonCommand(() => editor.ToggleParagraphBorder()));
        registry.Register("freew.para-shading", new ParagraphShadingCommand(editor));
        // Home / Design > Borders and Shading…: the full dialog (paragraph border, page border, shading).
        registry.Register("freew.borders-shading", new BordersAndShadingCommand(editor));

        // Home > Paragraph (Line and Page Breaks): flow-control toggles over the selected paragraph(s).
        // Each flips its pPr flag (keepNext/keepLines/widowControl) reversibly through the undo/redo bus.
        registry.Register("freew.keep-with-next", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleKeepWithNext(); }));
        registry.Register("freew.keep-lines", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleKeepLinesTogether(); }));
        registry.Register("freew.widow-control", new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleWidowControl(); }));

        // Layout > Sort: open a small dialog (A→Z / Z→A + case-sensitive option) and sort the selected
        // paragraphs in place through the view's undo/redo bus.
        registry.Register("freew.sort", new SortCommand(editor));

        // Layout > Table conversions: turn the selected paragraphs into a table (splitting on a chosen
        // delimiter) and turn the caret's table back into delimited paragraphs. Both route through the bus.
        registry.Register("freew.text-to-table", new TextToTableCommand(editor));
        registry.Register("freew.table-to-text", new TableToTextCommand(editor));

        registry.Register("freew.style-normal", new ApplyStyleCommand(editor, 11, bold: false, colorHex: null));
        registry.Register("freew.style-heading1", new ApplyStyleCommand(editor, 16, bold: true, colorHex: "#2F5496"));
        registry.Register("freew.style-heading2", new ApplyTocStyleCommand(editor, "Heading2"));
        registry.Register("freew.style-heading3", new ApplyTocStyleCommand(editor, "Heading3"));
        registry.Register("freew.style-title", new ApplyStyleCommand(editor, 28, bold: true, colorHex: null));
        registry.Register("freew.style-clear", new ActionRibbonCommand(() => { editor.Focus(); editor.SetParagraphStyle(null); }));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        var paragraphStyle = new ApplyParagraphStyleCommand(editor);
        registry.Register("freew.style", paragraphStyle);
        stateful.Add(("freew.style", paragraphStyle));
        stateStore.SetState("freew.style", paragraphStyle.GetState());

        // Home > Styles: New Style opens a dialog capturing name + formatting + based-on, creates a custom
        // DocumentStyle via the pure StyleManager and applies it to the selection. Manage Styles lets the
        // user modify or delete the catalog's styles (built-ins are guarded against deletion).
        registry.Register("freew.new-style", new NewStyleCommand(editor));
        registry.Register("freew.manage-styles", new ManageStylesCommand(editor));

        // Design > Document Formatting: Themes apply a full preset, Colors preserve fonts while applying
        // a palette, Style Sets rewrite built-in styles, and Fonts preserve colours while applying a
        // heading/body font pair. All are backed document-wide style changes.
        var theme = new ApplyThemeCommand(editor);
        registry.Register("freew.theme", theme);
        stateful.Add(("freew.theme", theme));
        stateStore.SetState("freew.theme", theme.GetState());
        var styleSet = new ApplyStyleSetCommand(editor);
        registry.Register("freew.style-set", styleSet);
        stateful.Add(("freew.style-set", styleSet));
        stateStore.SetState("freew.style-set", styleSet.GetState());
        registry.Register("freew.reset-style-set", new ResetStyleSetCommand(editor));
        registry.Register("freew.theme-colors", new ApplyThemeColorsCommand(editor));
        registry.Register("freew.customize-colors", new CustomizeColorsCommand(editor));
        registry.Register("freew.theme-fonts", new ApplyFontSetCommand(editor));
        registry.Register("freew.customize-fonts", new CustomizeFontsCommand(editor));
        registry.Register("freew.paragraph-spacing", new ApplyParagraphSpacingSetCommand(editor));
        registry.Register("freew.custom-paragraph-spacing", new CustomParagraphSpacingCommand(editor));
        registry.Register("freew.theme-effects", new ApplyEffectSetCommand(editor));
        registry.Register("freew.undo", new ActionRibbonCommand(() => { if (editor.CanUndo) editor.Undo(); }));
        registry.Register("freew.redo", new ActionRibbonCommand(() => { if (editor.CanRedo) editor.Redo(); }));

        // Layout tab — page settings (applied to the model; honoured by docx save + print).
        PageSetting("freew.orientation", PageLayoutCommandPlanner.ToggleOrientation);
        PageSetting("freew.margins", PageLayoutCommandPlanner.ToggleNormalNarrowMargins);
        PageSetting("freew.size", PageLayoutCommandPlanner.ToggleLetterA4Paper);
        // Columns: open the Columns dialog or apply Word's backed preset menu choices directly, mutating
        // PageSettings and re-rendering so the live document flow changes immediately.
        registry.Register("freew.columns", new ColumnsCommand(editor));
        PageSetting("freew.columns-one",
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.One),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.One));
        PageSetting("freew.columns-two",
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Two),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Two));
        PageSetting("freew.columns-three",
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Three),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Three));
        PageSetting("freew.columns-left",
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Left),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Left));
        PageSetting("freew.columns-right",
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Right),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Right));
        registry.Register("freew.columns-more", new ColumnsCommand(editor));
        // Page Setup: the unified Margins / Paper / Layout dialog (Word's Layout > Page Setup launcher). The
        // "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the Margins / Paper tab.
        registry.Register("freew.page-setup", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.custom-margins", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.more-paper-sizes", new PageSetupCommand(editor, PageSetupDialog.Tab.Paper));
        // Line Numbers: Word-style menu items set the backed mode explicitly, while the top-level command keeps
        // the existing cycle behavior for quick access (shown in print preview and the live page adorner).
        PageSetting("freew.line-numbers", PageLayoutCommandPlanner.CycleLineNumberMode);
        PageSetting("freew.line-numbers-none", page => page.LineNumberMode = LineNumberMode.None,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.None));
        PageSetting("freew.line-numbers-continuous", page => page.LineNumberMode = LineNumberMode.Continuous,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.Continuous));
        PageSetting("freew.line-numbers-restart-page", page => page.LineNumberMode = LineNumberMode.RestartEachPage,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.RestartEachPage));
        PageSetting("freew.line-numbers-restart-section", page => page.LineNumberMode = LineNumberMode.RestartEachSection,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.RestartEachSection));
        // Line Numbering Options…: dedicated dialog (Start At / Count By / Restart mode), not Page Setup.
        registry.Register("freew.line-numbers-options", new LineNumberOptionsCommand(editor));

        // Page setup polish — all mutate PageSettings via ApplyPageSettings (commit + re-render) and
        // round-trip through docx save.
        //  - Hyphenation: a dropdown (None / Automatic / Manual / Options…). The split-button default action
        //    (freew.hyphenation) toggles automatic hyphenation; the menu items set an explicit mode, and the
        //    Options item opens the Hyphenation Options dialog. Automatic hyphenation inserts soft hyphens in
        //    the live document (settings.xml w:autoHyphenation + zone/limit/caps sub-options).
        //  - Page Vertical Alignment: cycle Top -> Center -> Justified (-> Bottom) (sectPr w:vAlign).
        //  - Different First Page: toggle a distinct first-page header/footer (sectPr w:titlePg).
        PageSetting("freew.hyphenation", PageLayoutCommandPlanner.ToggleHyphenation, page => page.AutoHyphenation);
        PageSetting("freew.hyphenation-none", page => page.AutoHyphenation = false, page => !page.AutoHyphenation);
        PageSetting("freew.hyphenation-auto", page => page.AutoHyphenation = true, page => page.AutoHyphenation);
        registry.Register("freew.hyphenation-manual", new HyphenationManualCommand(editor));
        registry.Register("freew.hyphenation-options", new HyphenationOptionsCommand(editor));
        registry.Register("freew.page-valign", new PageVerticalAlignmentCommand(editor));
        PageSetting("freew.different-first-page",
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage);

        // Design tab — Page Background: "Page Borders" opens the full Borders and Shading dialog,
        // and Watermark sets/clears the page watermark. Both ultimately mutate PageSettings via
        // ApplyPageSettings (commit + re-render) and round-trip through docx save.
        registry.Register("freew.page-border", new BordersAndShadingCommand(editor));
        registry.Register("freew.watermark", new WatermarkCommand(editor));

        // Design tab — Page Background: pick the whole-page background colour (Word's Page Color). Opens a
        // swatch palette + No Color + More Colors... and sets the model's page BackgroundColorHex (which
        // already round-trips as w:background in docx); the editor recolours the page sheet immediately.
        registry.Register("freew.page-color", new PageColorCommand(editor));

        // Layout tab — open the modeless print-preview window (paginated, page-settings-aware).
        if (onPrintPreview is not null)
            registry.Register("freew.print-preview", new ActionRibbonCommand(onPrintPreview));

        // View tab — toggle the navigation pane (heading outline). Stateful so the ribbon's toggle
        // button reflects whether the pane is currently shown.
        if (onToggleNavPane is not null && isNavPaneVisible is not null)
            registry.Register("freew.nav-pane", new ToggleActionCommand(onToggleNavPane, isNavPaneVisible));

        // View tab — toggle the passive Word-style ruler chrome above/left of the page. The editor owns
        // the geometry; the host owns visibility so the ribbon checkmark mirrors the live chrome state.
        if (onToggleRuler is not null && isRulerVisible is not null)
            registry.Register("freew.ruler", new ToggleActionCommand(onToggleRuler, isRulerVisible));

        // View > Zoom — Multiple Pages: swap the workspace child from the live editor to a read-only
        // DocumentViewer fed by PrintLayout.BuildPaginatedSource (multi-page layout). Stateful toggle so
        // the ribbon reflects whether the paginated overlay is currently active.
        if (onToggleMultiplePages is not null && isMultiplePagesActive is not null)
            registry.Register("freew.zoom-multiple-pages", new ToggleActionCommand(onToggleMultiplePages, isMultiplePagesActive));

        // View > Zoom — Side to Side: same paginated overlay as Multiple Pages but forced to 2 pages across.
        if (onToggleSideToSide is not null && isSideToSideActive is not null)
            registry.Register("freew.zoom-side-to-side", new ToggleActionCommand(onToggleSideToSide, isSideToSideActive));

        // View > Window — Split: split the workspace with a GridSplitter, live editor on top and a
        // read-only FlowDocumentScrollViewer snapshot on the bottom, refreshed on TextChanged (~300 ms debounce).
        if (onToggleSplitWindow is not null && isSplitWindowActive is not null)
            registry.Register("freew.split-window", new ToggleActionCommand(onToggleSplitWindow, isSplitWindowActive));

        // View tab — toggle read mode (distraction-free view). Stateful so the ribbon's toggle button
        // reflects whether the chrome-light reading column is currently active.
        if (onToggleReadMode is not null && isReadModeActive is not null)
            registry.Register("freew.read-mode", new ToggleActionCommand(onToggleReadMode, isReadModeActive));

        // View > Views > Read Mode dropdown options — column width and page color (Feature 4).
        // The callback receives the choice token; behaviour applies immediately if in read mode.
        if (onReadModeColumnWidth is not null)
        {
            registry.Register("freew.read-mode-column-narrow",  new ActionRibbonCommand(() => onReadModeColumnWidth("narrow")));
            registry.Register("freew.read-mode-column-default", new ActionRibbonCommand(() => onReadModeColumnWidth("default")));
            registry.Register("freew.read-mode-column-wide",    new ActionRibbonCommand(() => onReadModeColumnWidth("wide")));
        }
        else
        {
            registry.Register("freew.read-mode-column-narrow",  EmptyRibbonCommand.Instance);
            registry.Register("freew.read-mode-column-default", EmptyRibbonCommand.Instance);
            registry.Register("freew.read-mode-column-wide",    EmptyRibbonCommand.Instance);
        }
        if (onReadModePageColor is not null)
        {
            registry.Register("freew.read-mode-color-none",    new ActionRibbonCommand(() => onReadModePageColor("none")));
            registry.Register("freew.read-mode-color-sepia",   new ActionRibbonCommand(() => onReadModePageColor("sepia")));
            registry.Register("freew.read-mode-color-inverse", new ActionRibbonCommand(() => onReadModePageColor("inverse")));
        }
        else
        {
            registry.Register("freew.read-mode-color-none",    EmptyRibbonCommand.Instance);
            registry.Register("freew.read-mode-color-sepia",   EmptyRibbonCommand.Instance);
            registry.Register("freew.read-mode-color-inverse", EmptyRibbonCommand.Instance);
        }

        // View > Window — New Window: open a second MainWindow (Feature 5).
        if (onNewWindow is not null)
            registry.Register("freew.new-window", new ActionRibbonCommand(onNewWindow));
        else
            registry.Register("freew.new-window", EmptyRibbonCommand.Instance);

        // View > Window — Arrange All: tile all open FreeW windows (Feature 5).
        if (onArrangeAll is not null)
            registry.Register("freew.arrange-all", new ActionRibbonCommand(onArrangeAll));
        else
            registry.Register("freew.arrange-all", EmptyRibbonCommand.Instance);

        // View tab — toggle Print Layout (Word-style page view) vs the plain/continuous view. Stateful so
        // the ribbon's toggle button reflects whether the page presentation is currently active. Default
        // on (the Word default); the host seeds the checked state to match.
        if (onTogglePrintLayout is not null && isPrintLayoutActive is not null)
            registry.Register("freew.print-layout", new ToggleActionCommand(onTogglePrintLayout, isPrintLayoutActive));

        // View tab — toggle Outline view (the heading-structured outline surface with the Outlining
        // mini-toolbar) vs the normal editing surface. Stateful so the ribbon's toggle button reflects
        // whether the outline view is currently active.
        if (onToggleOutlineView is not null && isOutlineViewActive is not null)
            registry.Register("freew.outline-view", new ToggleActionCommand(onToggleOutlineView, isOutlineViewActive));

        // View tab — switch to Web Layout (a continuous, full-width view with no page chrome, text wrapping
        // to the window like a web page) and Draft (a simplified continuous view for fast editing). Both are
        // mutually exclusive with Print Layout / Outline; the host owns the exclusivity and the stateful
        // checked-state, so these are ToggleActionCommands reflecting which view mode is active.
        if (onWebLayout is not null && isWebLayoutActive is not null)
            registry.Register("freew.web-layout", new ToggleActionCommand(onWebLayout, isWebLayoutActive));
        if (onDraftView is not null && isDraftViewActive is not null)
            registry.Register("freew.draft-view", new ToggleActionCommand(onDraftView, isDraftViewActive));

        // View tab — toggle Page Edit mode (opt-in editable-pagination surface). Stateful so the
        // ribbon's toggle button reflects whether the paged surface is currently active. Mutually
        // exclusive with Print Layout / Web Layout / Draft; the host owns the exclusivity via
        // TogglePagedEditView / EnterPagedEdit / ExitPagedEdit.
        if (onTogglePagedEditView is not null && isPagedEditViewActive is not null)
            registry.Register("freew.paged-edit-view", new ToggleActionCommand(onTogglePagedEditView, isPagedEditViewActive));

        // Home tab — toggle the Reveal Formatting pane (Word's Shift+F1 pane), a read-only side pane
        // showing the effective FONT / PARAGRAPH / SECTION formatting of the selection. Stateful so the
        // ribbon's toggle button reflects whether the pane is currently shown.
        if (onToggleRevealFormatting is not null && isRevealFormattingVisible is not null)
            registry.Register("freew.reveal-formatting",
                new ToggleActionCommand(onToggleRevealFormatting, isRevealFormattingVisible));

        // View tab — open Word's Zoom dialog (presets / page fits / custom %). The host computes the
        // page-relative fit factors from the live viewport and applies the chosen factor to the editor.
        if (onZoomDialog is not null)
            registry.Register("freew.zoom-dialog", new ActionRibbonCommand(onZoomDialog));
        if (onZoom100 is not null)
            registry.Register("freew.zoom-100", new ActionRibbonCommand(onZoom100));
        if (onZoomOnePage is not null)
            registry.Register("freew.zoom-one-page", new ActionRibbonCommand(onZoomOnePage));
        if (onZoomPageWidth is not null)
            registry.Register("freew.zoom-page-width", new ActionRibbonCommand(onZoomPageWidth));

        // Home > Paragraph — Show Formatting Marks: a stateful toggle over the editor's display-only pilcrow /
        // space-dot / tab-arrow overlay. The marks are drawn as a non-editable adorner computed from the
        // document's text geometry, so they never enter the model/text; executing flips the overlay and
        // (being in `stateful`) pushes the new state into the shared store so the ribbon button reflects it.
        var formattingMarks = new ToggleActionCommand(() => editor.ToggleFormattingMarks(), () => editor.ShowFormattingMarks);
        registry.Register("freew.formatting-marks", formattingMarks);
        stateful.Add(("freew.formatting-marks", formattingMarks));

        // View > Show > Gridlines: a stateful toggle that adds/removes the page-gridlines adorner.
        // Render-only; no model change. Distinct from freew.table-view-gridlines (table borders).
        var gridlines = new ToggleActionCommand(() => editor.TogglePageGridlines(), () => editor.ShowPageGridlines);
        registry.Register("freew.gridlines", gridlines);
        stateful.Add(("freew.gridlines", gridlines));

        if (onHelpOnline is not null)
            registry.Register("freew.help-online", new ActionRibbonCommand(onHelpOnline));
        if (onFeedback is not null)
            registry.Register("freew.feedback", new ActionRibbonCommand(onFeedback));
        if (onCopyDiagnostics is not null)
            registry.Register("freew.copy-diagnostics", new ActionRibbonCommand(onCopyDiagnostics));
        if (onCheckForUpdates is not null)
            registry.Register("freew.check-updates", new ActionRibbonCommand(onCheckForUpdates));
        if (onAbout is not null)
            registry.Register("freew.about", new ActionRibbonCommand(onAbout));
        if (onLegalNotices is not null)
            registry.Register("freew.legal-notices", new ActionRibbonCommand(onLegalNotices));

        // Mailings tab — a simple mail merge. Field placeholders are the literal text «FieldName»
        // (ordinary run text, so they round-trip through docx as plain text). The four commands share a
        // MailMergeSession: Start Mail Merge selects the output mode; "Select Recipients" / "Edit
        // Recipient List" capture CSV/typed records; "Insert Merge Field" drops a «Name» placeholder at
        // the caret; "Preview Results" loads MergeRecord(template, row) into the editor, and the preview
        // navigation commands move through real recipient rows; "Finish & Merge" combines every merged
        // record according to the selected output mode.
        var mergeSession = new MailMergeSession();
        registry.Register("freew.start-mail-merge", new SetMergeModeCommand(mergeSession, MailMergeOutputMode.Letters));
        registry.Register("freew.start-mail-merge-letters", new SetMergeModeCommand(mergeSession, MailMergeOutputMode.Letters));
        registry.Register("freew.start-mail-merge-directory", new SetMergeModeCommand(mergeSession, MailMergeOutputMode.Directory));
        registry.Register("freew.start-mail-merge-normal", new ClearMergeSessionCommand(mergeSession));
        registry.Register("freew.merge-data", new SetMergeDataCommand(editor, mergeSession));
        registry.Register("freew.merge-edit-recipients", new SetMergeDataCommand(editor, mergeSession));
        registry.Register("freew.merge-field", new InsertMergeFieldCommand(editor));
        // Write & Insert Fields — Address Block, Greeting Line, Match Fields (Word parity).
        registry.Register("freew.merge-address-block", new InsertAddressBlockCommand(editor, mergeSession));
        registry.Register("freew.merge-greeting-line", new InsertGreetingLineCommand(editor, mergeSession));
        registry.Register("freew.merge-match-fields", new MatchFieldsCommand(editor, mergeSession));
        // Special merge fields use Word's native NEXT/MERGEREC/MERGESEQ instructions. Their cached
        // result remains the familiar guillemet label until a merge evaluates the field.
        registry.Register("freew.merge-next-record", new InsertSpecialMergeFieldCommand(editor, MailMerge.NextRecordField));
        registry.Register("freew.merge-record-number", new InsertSpecialMergeFieldCommand(editor, MailMerge.MergeRecordNumberField));
        registry.Register("freew.merge-sequence-number", new InsertSpecialMergeFieldCommand(editor, MailMerge.MergeSequenceNumberField));
        // Rules dropdown — each sub-command inserts the appropriate rule instruction via a dialog.
        registry.Register("freew.merge-rules", EmptyRibbonCommand.Instance); // dropdown host: no action of its own
        registry.Register("freew.merge-rule-if", new InsertMergeRuleIfCommand(editor, mergeSession));
        registry.Register("freew.merge-rule-skip-record-if", new InsertMergeRuleCondCommand(editor, mergeSession, RuleCondKind.SkipRecordIf));
        registry.Register("freew.merge-rule-next-record-if", new InsertMergeRuleCondCommand(editor, mergeSession, RuleCondKind.NextRecordIf));
        registry.Register("freew.merge-rule-fill-in", new InsertMergeRuleFillInCommand(editor));
        registry.Register("freew.merge-rule-ask", new InsertMergeRuleAskCommand(editor));
        registry.Register("freew.merge-rule-set", new InsertMergeRuleSetCommand(editor));
        registry.Register("freew.merge-rule-ref", new InsertMergeRuleRefCommand(editor));
        registry.Register("freew.merge-preview", new PreviewMergeRecordCommand(editor, mergeSession));
        registry.Register("freew.merge-preview-first", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.First));
        registry.Register("freew.merge-preview-previous", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Previous));
        registry.Register("freew.merge-preview-next", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Next));
        registry.Register("freew.merge-preview-last", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Last));
        registry.Register("freew.merge-find-recipient", new FindMergeRecipientCommand(editor, mergeSession));
        registry.Register("freew.merge-check-errors", new CheckMergeErrorsCommand(
            editor,
            mergeSession,
            openReportDocument: onOpenMailMergeErrorReport));
        registry.Register("freew.merge-finish", new FinishMergeCommand(
            editor,
            mergeSession,
            printDocument: onPrintMailMergeDocument));
        registry.Register("freew.merge-email", new EmailMergeCommand(editor, mergeSession));
        // Filter & Sort: refines the active session's MergeData (include/exclude rows, sort column/direction)
        // without touching the merge template. No-ops gracefully when there is no active session or data.
        registry.Register("freew.merge-filter-sort", new FilterSortRecipientsCommand(editor, mergeSession));
        // Envelopes / Labels: set up the page geometry (and optionally a table grid for labels) via the
        // backed ApplyPageSettings / InsertTable paths. No SMTP or print path — page-setup only.
        registry.Register("freew.merge-envelopes", new EnvelopesCommand(editor));
        registry.Register("freew.merge-labels", new LabelsCommand(editor, mergeSession));

        RefreshStatefulCommands();
        return registry;
    }

    // Home > Font character effects wired by CharacterEffectCommand.
    private enum CharacterEffect { Superscript, Subscript, Strikethrough, SmallCaps, AllCaps }

    // Home > Font: apply a character effect to the selection as a toggle. Superscript/subscript set
    // Inline.BaselineAlignment (and shrink the font, mirroring DocumentView's render); strikethrough
    // toggles TextDecorations, and small/all caps set Typography.Capitals. Applying an effect that is
    // already present clears it. These properties
    // are exactly what DocumentView.ReadRunFormatting reads back, so the effect round-trips to docx.
    private sealed class CharacterEffectCommand(DocumentView editor, CharacterEffect effect) : IRibbonCommand
    {
        private const double SuperSubScale = 0.65;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (TryModelToggle())
                return;

            var selection = editor.Selection;
            switch (effect)
            {
                case CharacterEffect.Superscript:
                case CharacterEffect.Subscript:
                    ToggleBaseline(selection,
                        effect == CharacterEffect.Superscript ? BaselineAlignment.Superscript : BaselineAlignment.Subscript);
                    break;
                case CharacterEffect.SmallCaps:
                    ToggleCapitals(selection, FontCapitals.SmallCaps);
                    break;
                case CharacterEffect.AllCaps:
                    ToggleCapitals(selection, FontCapitals.AllSmallCaps);
                    break;
                case CharacterEffect.Strikethrough:
                    ToggleTextDecoration(selection, TextDecorations.Strikethrough[0]);
                    break;
            }
        }

        private bool TryModelToggle() => effect switch
        {
            CharacterEffect.Superscript => editor.TryToggleSelectedRunFormatting(
                formatting => formatting.VerticalAlign == VerticalAlign.Superscript,
                (formatting, value) => formatting with
                {
                    VerticalAlign = value ? VerticalAlign.Superscript : VerticalAlign.Baseline
                }),
            CharacterEffect.Subscript => editor.TryToggleSelectedRunFormatting(
                formatting => formatting.VerticalAlign == VerticalAlign.Subscript,
                (formatting, value) => formatting with
                {
                    VerticalAlign = value ? VerticalAlign.Subscript : VerticalAlign.Baseline
                }),
            CharacterEffect.Strikethrough => editor.TryToggleSelectedRunFormatting(
                formatting => formatting.Strikethrough,
                (formatting, value) => formatting with { Strikethrough = value }),
            CharacterEffect.SmallCaps => editor.TryToggleSelectedRunFormatting(
                formatting => formatting.SmallCaps,
                (formatting, value) => formatting with { SmallCaps = value, AllCaps = false }),
            CharacterEffect.AllCaps => editor.TryToggleSelectedRunFormatting(
                formatting => formatting.AllCaps,
                (formatting, value) => formatting with { AllCaps = value, SmallCaps = false }),
            _ => false,
        };

        private static void ToggleBaseline(TextSelection selection, BaselineAlignment target)
        {
            var current = selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
            var alreadyOn = current is BaselineAlignment b && b == target;
            if (alreadyOn)
            {
                // Clearing: restore baseline and undo the shrink so the original size returns.
                selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
                ScaleFontSize(selection, 1 / SuperSubScale);
            }
            else
            {
                // If switching from the other offset, the shrink is already applied — don't shrink twice.
                if (current is not BaselineAlignment cur ||
                    (cur != BaselineAlignment.Superscript && cur != BaselineAlignment.Subscript))
                {
                    ScaleFontSize(selection, SuperSubScale);
                }
                selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, target);
            }
        }

        private static void ScaleFontSize(TextSelection selection, double factor)
        {
            var value = selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (value is double size && size > 0)
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, size * factor);
        }

        private static void ToggleCapitals(TextSelection selection, FontCapitals target)
        {
            var current = selection.GetPropertyValue(Typography.CapitalsProperty);
            var alreadyOn = current is FontCapitals c && c == target;
            selection.ApplyPropertyValue(Typography.CapitalsProperty,
                alreadyOn ? FontCapitals.Normal : target);
        }

        private static void ToggleTextDecoration(TextSelection selection, TextDecoration target)
        {
            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var decorations = current is TextDecorationCollection collection
                ? new TextDecorationCollection(collection)
                : new TextDecorationCollection();

            var existing = decorations.FirstOrDefault(decoration => decoration.Location == target.Location);
            if (existing is null)
                decorations.Add(target);
            else
                decorations.Remove(existing);

            selection.ApplyPropertyValue(
                Inline.TextDecorationsProperty,
                decorations.Count == 0 ? null : decorations);
        }
    }

    // Home > Clipboard > Format Painter: single-click arms for one-shot (stamps the next selection, then
    // disarms); double-click arms for persistent lock mode (re-applies on every subsequent selection until
    // Escape or another click cancels it). The timestamp of the last Execute call detects a double-click.
    private sealed class FormatPainterCommand(DocumentView editor) : IRibbonCommand
    {
        private DateTime _lastExecute = DateTime.MinValue;
        private const double DoubleClickMs = 500;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var now = DateTime.UtcNow;
            var isDouble = (now - _lastExecute).TotalMilliseconds <= DoubleClickMs;
            _lastExecute = now;
            editor.ArmFormatPainter(locked: isDouble);
        }
    }

    // Home > Font > Change Case: show a small menu of the five cases and recase the current selection's
    // text through the editor (pure ChangeCase + undoable selection replacement). A no-op with an empty
    // selection — the user is told to select text first.
    private sealed class ChangeCaseCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly (string Label, CaseKind Kind)[] Choices =
        [
            ("UPPERCASE", CaseKind.Upper),
            ("lowercase", CaseKind.Lower),
            ("Sentence case", CaseKind.Sentence),
            ("Capitalize Each Word", CaseKind.Capitalize),
            ("tOGGLE cASE", CaseKind.Toggle),
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.Selection.IsEmpty)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select some text first, then choose Change Case.",
                    "FreeW");
                return;
            }

            if (ShowPicker(Window.GetWindow(editor)) is { } kind)
            {
                editor.Focus();
                editor.ChangeSelectionCase(kind);
            }
        }

        private static CaseKind? ShowPicker(Window? owner)
        {
            CaseKind? result = null;
            var window = new Window
            {
                Title = "Change Case",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8), Width = 200 };
            foreach (var (label, kind) in Choices)
            {
                var button = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += (_, _) => { result = kind; window.Close(); };
                panel.Children.Add(button);
            }

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }

    // A stateful toggle command: executing runs the host action (e.g. show/hide a panel) and its
    // checked-ness is read back from a host predicate, so the ribbon toggle reflects the live state.
    private sealed class ToggleActionCommand(Action toggle, Func<bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => toggle();

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: isChecked());
    }

    private static string? ComboValue(RibbonCommandContext context)
    {
        if (context.SelectedValue is { Length: > 0 } selectedValue)
            return selectedValue;

        return context.Parameters.TryGetValue("value", out var legacyRaw)
            ? legacyRaw as string
            : null;
    }

    // Home > Paragraph > Line Spacing: parse the chosen multiplier (e.g. "1.5") and apply it to every
    // paragraph spanned by the selection. The view routes the change through its undo/redo bus.
    private sealed class LineSpacingCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) && multiplier > 0)
            {
                editor.Focus();
                editor.SetLineSpacing(multiplier);
            }
        }

        public RibbonCommandState GetState() =>
            new(Value: editor.CurrentParagraphFormatting.LineSpacing.ToString(
                "0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    // Layout > Paragraph > Indent Left / Indent Right: numeric combo boxes (points) that display the
    // first selected paragraph's left/right indent and apply an exact value while preserving the
    // existing first-line indent. Both implement IRibbonStatefulCommand so SelectionChanged can push
    // the live value into the ribbon store and the combo reflects the current paragraph state.
    private sealed class IndentLeftCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
            {
                editor.Focus();
                var (_, right, firstLine) = editor.CurrentParagraphIndents();
                editor.SetParagraphIndents(pt, right, firstLine);
            }
        }

        public RibbonCommandState GetState()
        {
            var (left, _, _) = editor.CurrentParagraphIndents();
            return new RibbonCommandState(Value: left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private sealed class IndentRightCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
            {
                editor.Focus();
                var (left, _, firstLine) = editor.CurrentParagraphIndents();
                editor.SetParagraphIndents(left, pt, firstLine);
            }
        }

        public RibbonCommandState GetState()
        {
            var (_, right, _) = editor.CurrentParagraphIndents();
            return new RibbonCommandState(Value: right.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // Layout > Paragraph > Space Before / Space After: numeric combo boxes (points) that display the
    // first selected paragraph's space-before/after and apply an exact value reversibly via the bus.
    // Like the indent combos, both are stateful so the ribbon reflects the current selection's value.
    private sealed class SpaceBeforeCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
            {
                editor.Focus();
                editor.FormatSelectedParagraphSpaceBefore(pt);
            }
        }

        public RibbonCommandState GetState()
        {
            var f = editor.CurrentParagraphFormatting;
            return new RibbonCommandState(Value: f.SpaceBeforePt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private sealed class SpaceAfterCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
            {
                editor.Focus();
                editor.FormatSelectedParagraphSpaceAfter(pt);
            }
        }

        public RibbonCommandState GetState()
        {
            var f = editor.CurrentParagraphFormatting;
            return new RibbonCommandState(Value: f.SpaceAfterPt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // Home > Paragraph > Paragraph…: open the indent dialog seeded with the first selected paragraph's
    // current left/right/first-line indents, and apply the chosen values to every selected paragraph
    // through the view (reversible via the bus). A negative first-line value is a hanging indent.
    private sealed class ParagraphIndentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var (left, right, firstLine) = editor.CurrentParagraphIndents();
            if (ParagraphIndentDialog.Prompt(Window.GetWindow(editor), left, right, firstLine) is { } chosen)
            {
                editor.Focus();
                editor.SetParagraphIndents(chosen.Left, chosen.Right, chosen.FirstLine);
            }
        }
    }

    // Home > Font dialog-launcher (freew.font-dialog): opens the two-tab Font dialog (Font + Advanced)
    // covering the standard run formatting fields plus the OpenType advanced typography fields that the
    // model already backs: CharacterSpacingPt, KerningMinSizePt, PositionPt, Ligatures, StylisticSet,
    // NumberForm, NumberSpacing. Applies via DocumentView.ApplyFontFormatting (both WPF surface +
    // model-only fields through the undo/redo bus).
    private sealed class FontDialogCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var current = editor.CurrentRunFormatting;
            var result = FontDialog.Prompt(Window.GetWindow(editor), current);
            if (result is null)
                return;
            editor.Focus();
            editor.ApplyFontFormatting(result);
        }
    }

    // Home > Paragraph dialog-launcher (freew.paragraph-dialog): replaces the previous single-tab
    // ParagraphIndentCommand with the full two-tab dialog (Indents and Spacing + Line and Page Breaks).
    // All fields map to backed ParagraphFormatting properties and route through the undo/redo bus.
    private sealed class ParagraphDialogCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var current = editor.CurrentParagraphFormatting;
            var result = ParagraphBreaksDialog.Prompt(Window.GetWindow(editor), current);
            if (result is null)
                return;
            editor.Focus();
            editor.ApplyParagraphDialogFormatting(
                result.LeftPt, result.RightPt, result.FirstLinePt,
                result.SpaceBeforePt, result.SpaceAfterPt, result.LineSpacing,
                result.KeepWithNext, result.KeepLinesTogether, result.WidowControl,
                result.PageBreakBefore, result.SuppressAutoHyphens, result.SuppressLineNumbers, result.ContextualSpacing);
        }
    }

    // Home > Clipboard > Paste Special: shows a list of backed paste formats and dispatches to the
    // matching DocumentView method. Keep Source Formatting imports clipboard RTF at an empty paragraph;
    // Merge Formatting and Keep Text Only retain their destination/plain-text paths.
    private sealed class PasteSpecialCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var option = PasteSpecialDialog.Prompt(Window.GetWindow(editor));
            if (option is null)
                return;
            editor.Focus();
            switch (option.Value)
            {
                case PasteSpecialOption.KeepSourceFormatting:
                    editor.PasteKeepSourceFormatting();
                    break;
                case PasteSpecialOption.KeepTextOnly:
                    editor.PastePlainText();
                    break;
                default:
                    editor.PasteMergeFormatting();
                    break;
            }
        }
    }

    // Home > Paragraph > Multilevel List > Define New Multilevel List: opens the definition dialog and
    // applies the complete backed definition as one undoable edit.
    private sealed class DefineMultilevelListCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var def = MultilevelListDialog.Prompt(Window.GetWindow(editor), editor.Model.MultiLevelList.NumberFormats);
            if (def is null)
                return;
            editor.Focus();
            editor.ApplyMultiLevelListDefinition(def);
        }
    }

    // Home > Paragraph > Tabs…: open the Tabs dialog seeded with the first selected paragraph's current
    // tab stops, and apply the edited stop list to every selected paragraph through the view (reversible
    // via the bus). The stops round-trip to docx via the existing w:tabs writer.
    private sealed class TabsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var current = editor.CurrentParagraphFormatting.TabStops;
            if (TabsDialog.Prompt(Window.GetWindow(editor), current, editor.Model.Page.DefaultTabStopPt) is { } chosen)
            {
                editor.Focus();
                editor.SetParagraphTabStops(chosen.TabStops);
                editor.ApplyPageSettings(page => page.DefaultTabStopPt = chosen.DefaultTabStopPt);
            }
        }
    }

    // Applies a named paragraph style's formatting (size/weight/colour) to the current selection.
    private sealed class ApplyStyleCommand(DocumentView editor, double sizePt, bool bold, string? colorHex) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var selection = editor.Selection;
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, sizePt * PxPerPoint);
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
            var brush = colorHex is null ? Brushes.Black : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        }
    }

    // Home > Styles: apply a real paragraph style. The styles dropdown's value is a display name
    // (e.g. "Heading 1"); this maps it to the matching style id in the model's catalog and sets the
    // selected paragraph(s)' StyleId through the view's undo/redo bus (re-rendered to resolve formatting).
    private sealed class ApplyParagraphStyleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { Length: > 0 } value)
                return;

            var styleId = ResolveStyleId(editor.Model, value);
            if (styleId is null)
                return;

            editor.Focus();
            editor.SetParagraphStyle(styleId);
        }

        public RibbonCommandState GetState() =>
            new(Value: editor.CurrentParagraphStyleName);

        // Match the chosen combo entry to a style in the document by id first, then by display name
        // (case-insensitive, ignoring spaces) so "Heading 1" resolves to the "Heading1" style id.
        private static string? ResolveStyleId(TextDocument model, string choice)
        {
            if (model.Styles.ContainsKey(choice))
                return choice;
            foreach (var style in model.Styles.Values)
            {
                if (string.Equals(style.Name, choice, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Compact(style.Id), Compact(choice), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Compact(style.Name), Compact(choice), StringComparison.OrdinalIgnoreCase))
                    return style.Id;
            }
            return null;
        }

        private static string Compact(string value) => value.Replace(" ", string.Empty);
    }

    // References > Table of Contents > Add Text: Word exposes TOC inclusion as level choices. FreeW's
    // TOC is built from paragraph styles, so each choice reuses the same reversible StyleId path as Home > Styles.
    private sealed class ApplyTocStyleCommand(DocumentView editor, string styleId) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.SetParagraphStyle(styleId);
        }
    }

    // Home > Styles: New Style. Opens a dialog capturing a name + a few formatting options + a based-on
    // style, then creates a custom DocumentStyle via the pure StyleManager and applies it to the
    // selection through the same reversible StyleId path the styles dropdown uses. Newly created styles
    // appear in the Style dropdown after reopening the document (the ribbon combo's item list is built
    // once from the immutable definition); the create + immediate apply is the must-have and works now.
    private sealed class NewStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var catalog = StyleNamesById(editor.Model);
            var def = StyleDialog.AskNew(owner, catalog, editor.CurrentParagraphStyleId);
            if (def is null)
                return;

            editor.Focus();
            editor.CreateParagraphStyleAndApply(def.Name, def.BasedOnId, def.Run, def.Paragraph, def.NextStyleId);
        }
    }

    // Home > Styles: Manage Styles. Lists the document's styles; the selected one can be modified (name is
    // fixed, formatting/based-on editable), deleted (built-ins are refused by StyleManager), or applied to
    // the selection. Pragmatic by design — the pure StyleManager carries the rules; this is the surface.
    private sealed class ManageStylesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);

            while (true)
            {
                var action = ManageStylesDialog.Ask(owner, editor.Model, editor.CurrentParagraphStyleId);
                if (action is null)
                    return;

                switch (action)
                {
                    case ManageStyleAction.Apply apply:
                        editor.Focus();
                        editor.SetParagraphStyle(apply.StyleId);
                        return;

                    case ManageStyleAction.Delete del:
                        editor.DeleteParagraphStyle(del.StyleId);
                        continue; // reopen the list so the user sees the removal

                    case ManageStyleAction.Modify mod:
                        if (!editor.Model.Styles.TryGetValue(mod.StyleId, out var existing))
                            continue;
                        var def = StyleDialog.AskModify(owner, StyleNamesById(editor.Model), existing);
                        if (def is null)
                            continue;
                        editor.ModifyParagraphStyle(mod.StyleId, def.Run, def.Paragraph, def.BasedOnId, def.NextStyleId);
                        continue;
                }
            }
        }
    }

    // The document's style catalog as id -> display name, for the dialogs' based-on / style lists.
    private static IReadOnlyDictionary<string, string> StyleNamesById(TextDocument model) =>
        model.Styles.ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.Ordinal);

    // Design > Document Formatting: apply a built-in document theme. The selected name may arrive from
    // a combo value, older host context, or a WPF menu item header; all resolve to the same catalog entry.
    private sealed class ApplyThemeCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentTheme.FindByName(value) is not { } theme)
                return;

            editor.Focus();
            editor.ApplyTheme(theme);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, Value: editor.Model.Theme.Name);

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyStyleSetCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentStyleSet.FindByName(value) is not { } styleSet)
                return;

            editor.Focus();
            editor.ApplyStyleSet(styleSet);
        }

        public RibbonCommandState GetState() =>
            new(Value: DocumentStyleSet.FindMatching(editor.Model)?.Name);

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyThemeColorsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentTheme.FindByName(value) is not { } theme)
                return;

            editor.Focus();
            editor.ApplyThemeColors(theme);
        }

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyFontSetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentFontSet.FindByName(value) is not { } fontSet)
                return;

            editor.Focus();
            editor.ApplyFontSet(fontSet);
        }

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyParagraphSpacingSetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentParagraphSpacingSet.FindByName(value) is not { } spacingSet)
                return;

            editor.Focus();
            editor.ApplyParagraphSpacingSet(spacingSet);
        }

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyEffectSetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue ?? LegacyValue(context) ?? MenuHeaderValue(context);
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (DocumentEffectSet.FindByName(value) is not { } effectSet)
                return;

            editor.Focus();
            editor.ApplyEffectSet(effectSet);
        }

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    // Design > Reset to Default Style Set: applies the catalog default (Office) to the document.
    private sealed class ResetStyleSetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyStyleSet(DocumentStyleSet.Default);
        }
    }

    // Design > Colors > Customize Colors…: author a 12-slot custom theme color scheme.
    private sealed class CustomizeColorsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var theme = CustomizeThemeColorsDialog.Prompt(owner, editor.Model.Theme);
            if (theme is null)
                return;
            editor.Focus();
            editor.ApplyThemeColors(theme);
        }
    }

    // Design > Fonts > Customize Fonts…: pick heading/body font pair and apply as a custom font set.
    private sealed class CustomizeFontsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var currentTheme = editor.Model.Theme;
            var current = DocumentFontSet.FindByName(currentTheme.HeadingFont)
                ?? new DocumentFontSet("Custom", currentTheme.HeadingFont, currentTheme.BodyFont);
            var fontSet = CustomizeThemeFontsDialog.Prompt(owner, current);
            if (fontSet is null)
                return;
            editor.Focus();
            editor.ApplyFontSet(fontSet);
        }
    }

    // Design > Paragraph Spacing > Custom Paragraph Spacing…: open dialog for explicit spacing values.
    private sealed class CustomParagraphSpacingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var spacingSet = CustomParagraphSpacingDialog.Prompt(owner, DocumentParagraphSpacingSet.Default);
            if (spacingSet is null)
                return;
            editor.Focus();
            editor.ApplyParagraphSpacingSet(spacingSet);
        }
    }

    // Home > Font: pick a colour from a small fixed palette and apply it to the selection. When
    // isHighlight is false it sets the text foreground; when true it sets the text background
    // (highlight). "Automatic"/"No Color" clears the property back to its inherited value.
    private sealed class ColorPickCommand(DocumentView editor, bool isHighlight) : IRibbonCommand
    {
        private static readonly string[] Palette =
        [
            "#000000", "#404040", "#7F7F7F", "#C00000", "#FF0000", "#FFC000",
            "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#2F5496",
            "#7030A0", "#FFFFFF",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var parameterValue = context.Parameters.TryGetValue("value", out var raw)
                ? raw as string
                : context.SelectedValue;
            var chosen = parameterValue is null
                ? ShowPicker(Window.GetWindow(editor))
                : string.IsNullOrWhiteSpace(parameterValue)
                    ? ColorChoice.Clear
                    : new ColorChoice(parameterValue);
            if (chosen is null)
                return;

            if (chosen == ColorChoice.Clear)
            {
                if (isHighlight)
                    editor.SetHighlightColor(null);
                else
                    editor.SetTextColor(null);
            }
            else
            {
                if (isHighlight)
                    editor.SetHighlightColor(chosen.Hex);
                else
                    editor.SetTextColor(chosen.Hex);
            }
        }

        private sealed record ColorChoice(string Hex)
        {
            public static readonly ColorChoice Clear = new(string.Empty);
        }

        private ColorChoice? ShowPicker(Window? owner)
        {
            ColorChoice? result = null;
            var window = new Window
            {
                Title = isHighlight ? "Highlight Colour" : "Text Colour",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 7 * 26 };
            foreach (var hex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = hex
                };
                swatch.Click += (_, _) => { result = new ColorChoice(hex); window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = isHighlight ? "No Color" : "Automatic",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            clear.Click += (_, _) => { result = ColorChoice.Clear; window.Close(); };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }

    // Home > Paragraph > Shading: pick a fill colour from a small palette and apply it to the
    // selected paragraph(s); "No Color" clears shading. Mirrors ColorPickCommand's swatch picker.
    private sealed class ParagraphShadingCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly string[] Palette =
        [
            "#FFFF00", "#92D050", "#00B0F0", "#FFC000", "#FF0000", "#D9D9D9",
            "#A6A6A6", "#FFF2CC", "#DEEBF7", "#E2EFDA", "#FCE4D6", "#EDEDED",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return;
            editor.ToggleParagraphShading(hex);
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = "Paragraph Shading",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 6 * 26 };
            foreach (var swatchHex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatchHex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = swatchHex
                };
                swatch.Click += (_, _) => { chosen = true; hex = swatchHex; window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = "No Color",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            clear.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }
    }

    // Insert > Table Tools > Cell Shading: pick a fill colour from a small palette and apply it to the
    // caret's table cell; "No Color" clears shading. Mirrors ParagraphShadingCommand's swatch picker.
    // Table Tools — Data > Formula (Word's Table > Data > Formula): insert a computed formula field into the
    // caret's cell. Requires the caret to be inside a table; otherwise warns and does nothing. Seeds a
    // default formula (=SUM(ABOVE) or =SUM(LEFT)) by looking at where numbers sit relative to the cell, opens
    // the Formula dialog, and inserts/recomputes the field.
    private sealed class TableFormulaCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var location = editor.CaretTableCell();
            if (location is null)
            {
                DialogMessageHelper.ShowWarning(owner!, "The cursor must be inside a table cell to insert a formula.", "Formula");
                return;
            }

            var (table, rowIndex, columnIndex) = location.Value;
            var formula = TableFormulaDialog.Prompt(
                owner,
                TableFormulaDialogPlanner.BuildInitialState(table, rowIndex, columnIndex));
            if (formula is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertTableFormula(formula);
        }

    }

    // Table Tools — Layout > Properties (Word's Table Properties dialog). Requires the caret to be inside a
    // table; otherwise warns. Seeds the four-tab dialog from the caret's table/row/cell and applies the chosen
    // values through the editor (which round-trips via w:tblPr / w:trPr / w:tcPr).
    private sealed class TablePropertiesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var tableContext = editor.CaretTableContext();
            if (tableContext is null)
            {
                DialogMessageHelper.ShowWarning(owner!, "The cursor must be inside a table to edit its properties.", "Table Properties");
                return;
            }

            var values = TablePropertiesDialog.Prompt(owner, tableContext);
            if (values is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.ApplyTableProperties(values);
        }
    }

    private sealed class CellShadingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var result = CellShadingDialog.Prompt(owner);
            if (result is not { Accepted: true })
                return;
            editor.SetCaretCellShading(result.Hex);
        }
    }

    // Table Design — Borders picker: lets the user pick a border preset (All / Outside / Inside / Top /
    // Bottom / Left / Right / None) with a style, colour and width chooser, then applies it to the caret
    // cell via SetCaretCellBorders. Reuses the BorderLineStyle enum and CellBorderEdge record from the model.
    private sealed class CellBordersCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly string[] ColorPalette =
        [
            "#000000", "#FF0000", "#0000FF", "#008000", "#800000",
            "#808080", "#C0C0C0", "#FF6600", "#9900CC", "#FFFFFF",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var result = ShowBordersDialog(owner);
            if (result is null)
                return;
            editor.SetCaretCellBorders(result);
        }

        private static CellBorders? ShowBordersDialog(Window? owner)
        {
            CellBorders? result = null;

            var selectedStyle = BorderLineStyle.Single;
            var selectedColor = "#000000";
            var selectedWidthPt = 0.5;

            var window = new Window
            {
                Title = "Cell Borders",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var outer = new StackPanel { Margin = new Thickness(10) };

            // -- Preset buttons row --
            var presetLabel = new TextBlock { Text = "Preset:", Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold };
            outer.Children.Add(presetLabel);
            var presetPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] presets = ["All", "Outside", "Inside", "Top", "Bottom", "Left", "Right", "None"];
            Button? applyBtn = null;

            CellBorderEdge MakeEdge() => new(selectedStyle, selectedColor, selectedWidthPt);
            CellBorders BuildPreset(string preset, System.Func<CellBorderEdge> edge) => preset switch
            {
                "All" => new CellBorders { Top = edge(), Bottom = edge(), Left = edge(), Right = edge() },
                "Outside" => new CellBorders { Top = edge(), Bottom = edge(), Left = edge(), Right = edge() },
                "Inside" => new CellBorders(), // inside borders handled at table level; clear cell overrides
                "Top" => new CellBorders { Top = edge() },
                "Bottom" => new CellBorders { Bottom = edge() },
                "Left" => new CellBorders { Left = edge() },
                "Right" => new CellBorders { Right = edge() },
                _ => null! // "None"
            };

            string? chosenPreset = null;
            foreach (var preset in presets)
            {
                var btn = new Button
                {
                    Content = preset,
                    Margin = new Thickness(2),
                    Padding = new Thickness(8, 3, 8, 3),
                    Tag = preset
                };
                btn.Click += (_, _) =>
                {
                    chosenPreset = (string)btn.Tag;
                    if (applyBtn is not null) applyBtn.IsEnabled = true;
                };
                presetPanel.Children.Add(btn);
            }
            outer.Children.Add(presetPanel);

            // -- Style picker --
            var styleLabel = new TextBlock { Text = "Style:", Margin = new Thickness(0, 0, 0, 2) };
            outer.Children.Add(styleLabel);
            var styleCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var s in Enum.GetValues<BorderLineStyle>())
                styleCombo.Items.Add(s.ToString());
            styleCombo.SelectedIndex = 0;
            styleCombo.SelectionChanged += (_, _) =>
            {
                if (styleCombo.SelectedItem is string sv && Enum.TryParse<BorderLineStyle>(sv, out var parsed))
                    selectedStyle = parsed;
            };
            outer.Children.Add(styleCombo);

            // -- Colour swatches --
            var colorLabel = new TextBlock { Text = "Color:", Margin = new Thickness(0, 0, 0, 2) };
            outer.Children.Add(colorLabel);
            var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            Border? selectedColorBorder = null;
            foreach (var hex in ColorPalette)
            {
                var swatch = new Border
                {
                    Width = 20, Height = 20, Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                    ToolTip = hex, Cursor = Cursors.Hand, Tag = hex
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    selectedColor = (string)swatch.Tag;
                    if (selectedColorBorder is not null)
                        selectedColorBorder.BorderThickness = new Thickness(1);
                    swatch.BorderThickness = new Thickness(2);
                    selectedColorBorder = swatch;
                };
                colorPanel.Children.Add(swatch);
            }
            outer.Children.Add(colorPanel);

            // -- Width spinner --
            var widthLabel = new TextBlock { Text = "Width (pt):", Margin = new Thickness(0, 0, 0, 2) };
            outer.Children.Add(widthLabel);
            var widthBox = new TextBox { Text = "0.5", Width = 60, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
            widthBox.TextChanged += (_, _) =>
            {
                if (double.TryParse(widthBox.Text, out var w) && w > 0)
                    selectedWidthPt = w;
            };
            outer.Children.Add(widthBox);

            // -- Apply / Cancel --
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            applyBtn = new Button
            {
                Content = "Apply",
                IsEnabled = false,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 0)
            };
            applyBtn.Click += (_, _) =>
            {
                if (chosenPreset == "None")
                    result = null;
                else if (chosenPreset is not null)
                    result = BuildPreset(chosenPreset, MakeEdge);
                window.Close();
            };
            var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4) };
            cancelBtn.Click += (_, _) => window.Close();
            buttonRow.Children.Add(applyBtn);
            buttonRow.Children.Add(cancelBtn);
            outer.Children.Add(buttonRow);

            window.Content = outer;
            window.ShowDialog();
            return result;
        }
    }

    private sealed class PageCommand(
        DocumentView editor,
        Action<PageSettings> apply,
        Func<PageSettings, bool>? isChecked = null) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => editor.ApplyPageSettings(apply);

        public RibbonCommandState GetState() => new(
            IsEnabled: !editor.IsReadOnly,
            IsChecked: isChecked?.Invoke(editor.Model.Page) == true);
    }

    // Home / Design > Borders and Shading…: opens the full dialog (paragraph border, page border, shading)
    // seeded with the current paragraph's border/shading and the page border. Applies the chosen paragraph
    // border/shading through DocumentView (the undo/redo bus) and the page border through ApplyPageSettings;
    // everything round-trips through the existing w:pBdr / w:pgBorders / w:shd writers.
    private sealed class BordersAndShadingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = BordersAndShadingDialog.Prompt(
                Window.GetWindow(editor), editor.CurrentParagraphFormatting, editor.Model.Page.PageBorder);
            if (result is null)
                return;

            editor.SetParagraphBorder(result.ParagraphBorder);
            editor.SetParagraphShading(result.ShadingHex, result.ShadingPattern);
            editor.ApplyPageSettings(page => page.PageBorder = result.PageBorder);
        }
    }

    // Opens the Columns dialog (One/Two/Three/Left/Right presets + custom count, spacing, line-between) and
    // applies the chosen layout to PageSettings. Routes through ApplyPageSettings so the editor commits
    // pending edits, mutates the page columns, and re-renders the multi-column flow immediately. Equal
    // presets clear any explicit per-column widths; the Left/Right presets set them (w:cols/@w:equalWidth).
    private sealed class ColumnsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = ColumnsDialog.Prompt(Window.GetWindow(editor), editor.Model.Page);
            if (result is null)
                return;

            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyColumnsResult(page, result));
        }
    }

    // Word's Layout > Columns dropdown applies common presets immediately. Equal presets clear explicit
    // widths; Left/Right set the classic narrow/wide two-column split using the current page content width.
    private sealed class ColumnsPresetCommand(DocumentView editor, PageColumnPreset preset) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyColumnPreset(page, preset));
    }

    // Opens the unified Page Setup dialog (Margins / Paper / Layout tabs) and applies the chosen geometry,
    // orientation, gutter, mirror-margins, paper size, header/footer distance, vertical alignment and the
    // different-first-page / odd-even toggles to PageSettings via ApplyPageSettings — the same single
    // commit + re-render path the other page-setup commands use, round-tripping through the existing w:sectPr /
    // settings.xml writers. The "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the
    // Margins / Paper tab. The dialog's Line Numbers… / Borders… launchers defer to FreeW's existing Line
    // Numbers cycle and Borders and Shading dialog respectively, opened after the page settings are applied.
    private sealed class PageSetupCommand(DocumentView editor, PageSetupDialog.Tab initialTab) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var outcome = PageSetupDialog.Prompt(Window.GetWindow(editor), editor.Model.Page, initialTab: initialTab);
            if (outcome is not { } o)
                return;

            var settings = o.Settings;
            var planned = PageSetupDialog.ToPresentationResult(settings);
            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyPageSetupResult(page, planned));

            // Defer to the existing features for the Layout-tab launchers, so a single source of truth drives
            // line numbering and page/paragraph borders.
            if (o.LineNumbers)
                new LineNumberCommand(editor).Execute(context);
            else if (o.Borders)
                new BordersAndShadingCommand(editor).Execute(context);
        }
    }

    // Cycles page line numbering None -> Continuous -> RestartEachPage -> None. Routes through
    // ApplyPageSettings so the editor commits pending edits, mutates PageSettings, and re-renders;
    // the numbers themselves surface in the print preview / print output.
    private sealed class LineNumberCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(PageLayoutCommandPlanner.CycleLineNumberMode);
    }

    // Word's Layout > Line Numbers dropdown exposes discrete mode choices. These commands set the exact backed
    // PageSettings mode instead of forcing users through the top-level cycle.
    private sealed class LineNumberModeCommand(DocumentView editor, LineNumberMode mode) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.LineNumberMode = mode);
    }

    // Toggles automatic hyphenation (settings.xml w:autoHyphenation). Routes through ApplyPageSettings so
    // the editor commits pending edits, mutates PageSettings.AutoHyphenation, and re-renders.
    private sealed class HyphenationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(PageLayoutCommandPlanner.ToggleHyphenation);
    }

    // Hyphenation dropdown — None / Automatic: sets the document's automatic-hyphenation flag explicitly
    // (Word's Hyphenation > None / Automatic). Routes through ApplyPageSettings (commit + re-render) so the
    // soft-hyphen rendering shows at once and the flag round-trips through settings.xml.
    private sealed class HyphenationModeCommand(DocumentView editor, bool auto) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.AutoHyphenation = auto);
    }

    // Hyphenation dropdown - Manual: review candidates in document order, then insert accepted soft hyphens
    // as one undoable body-text edit without changing the automatic-hyphenation setting.
    private sealed class HyphenationManualCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.CommitToModel();
            var owner = Window.GetWindow(editor);
            var session = ManualHyphenationPlanner.CreateSession(editor.Model);
            if (session.CandidateCount == 0)
            {
                if (owner is not null)
                    DialogMessageHelper.ShowInfo(owner, "Manual hyphenation found no words to review.", "Hyphenation");
                return;
            }

            while (!session.IsComplete)
            {
                var result = ManualHyphenationDialog.Prompt(owner, session.Current!);
                if (result is null || result.Action == ManualHyphenationDialogAction.Cancel)
                    break;
                if (result.Action == ManualHyphenationDialogAction.Accept && result.BreakPoint is int breakPoint)
                    session.Accept(breakPoint);
                else
                    session.Skip();
            }

            editor.ApplyManualHyphenation(session.Edits);
        }
    }

    // Hyphenation dropdown — Hyphenation Options…: opens the dialog (auto toggle, zone, consecutive-hyphen
    // limit, hyphenate-caps) and applies the chosen settings to PageSettings via ApplyPageSettings so they
    // round-trip through settings.xml and the live rendering updates.
    private sealed class HyphenationOptionsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var result = HyphenationOptionsDialog.Prompt(owner, editor.Model.Page);
            if (result is null)
                return;

            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyHyphenationOptions(page, result));
        }
    }

    // Cycles page vertical alignment Top -> Center -> Justified -> Top (sectPr w:vAlign). Routes through
    // ApplyPageSettings so the editor commits pending edits, mutates PageSettings, and re-renders.
    private sealed class PageVerticalAlignmentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.VerticalAlignment = page.VerticalAlignment switch
            {
                PageVerticalAlignment.Top => PageVerticalAlignment.Center,
                PageVerticalAlignment.Center => PageVerticalAlignment.Justified,
                _ => PageVerticalAlignment.Top
            });
    }

    // Toggles "different first page" (sectPr w:titlePg). Routes through ApplyPageSettings so the editor
    // commits pending edits, mutates PageSettings.DifferentFirstPage, and re-renders.
    private sealed class DifferentFirstPageCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.DifferentFirstPage = !page.DifferentFirstPage);
    }

    // Inserts a table at the caret. Delegates to the view, which routes through the undo/redo bus.
    private sealed class InsertTableCommand(DocumentView editor, int rows, int columns) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.InsertTable(rows, columns);
        }
    }

    // Table Design > Draw Borders > Draw Table: prompts for dimensions and inserts a table at the
    // caret. Full freehand drag-draw over the editor is beyond scope; this backed version delivers
    // the table-insertion model (scope: dimension-prompted insert, not mouse-draw).
    private sealed class DrawTableCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var dims = DrawTableDimensionPicker.Ask(Window.GetWindow(editor));
            if (dims is null)
                return;
            var (rows, cols) = dims.Value;
            editor.Focus();
            editor.InsertTable(rows, cols);
        }
    }

    // Table Design > Draw Borders > Eraser: remove the caret cell's right border by merging right.
    // An explicit multi-cell selection retains the normal merge-selection behavior.
    private sealed class EraserCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.EraseTableBorderAtCaret();
        }
    }

    // A tiny modal dialog letting the user choose rows × columns for Draw Table.
    private static class DrawTableDimensionPicker
    {
        public static (int Rows, int Cols)? Ask(Window? owner)
        {
            (int Rows, int Cols)? result = null;

            var rowsBox = new System.Windows.Controls.TextBox { Text = "3", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8) };
            var colsBox = new System.Windows.Controls.TextBox { Text = "3", MinWidth = 60, Margin = new Thickness(0, 0, 0, 8) };
            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true,  MinWidth = 72 };

            var dialog = new Window
            {
                Title = "Draw Table",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            ok.Click += (_, _) =>
            {
                result = DrawTableCommandPlanner.Normalize(rowsBox.Text, colsBox.Text);
                dialog.DialogResult = true;
            };

            var closeRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeRow.Children.Add(ok);
            closeRow.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Number of rows:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(rowsBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Number of columns:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(colsBox);
            panel.Children.Add(closeRow);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Insert > Text > Text from File: pick a .docx, read it, and merge its body into the document at the caret.
    private sealed class InsertFileCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var result = WpfFileDialogService.ShowOpenDialog(
                owner,
                "Word Documents (*.docx)|*.docx|All files (*.*)|*.*",
                defaultExtensionWithDot: ".docx",
                title: "Insert Text from File");
            if (!result.Chosen)
                return;

            try
            {
                var source = DocxReader.Read(result.FileName!);
                editor.Focus();
                editor.InsertDocument(source);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(owner, $"Could not insert the file:\n{ex.Message}", "FreeW");
            }
        }
    }

    // Insert > Illustrations > Picture: pick an image (including SVG), normalise to PNG, insert as an inline image run.
    private sealed class InsertPictureCommand(DocumentView editor) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;
        private const double MaxWidthPt = 400;

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var result = WpfFileDialogService.ShowOpenDialog(
                owner,
                "Images (*.png;*.jpg;*.jpeg;*.svg)|*.png;*.jpg;*.jpeg;*.svg|All files (*.*)|*.*",
                title: "Insert Picture");
            if (!result.Chosen)
                return;

            try
            {
                var image = LoadAsInlineImage(result.FileName!);
                editor.Focus();
                editor.InsertImage(image);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(owner, $"Could not insert the image:\n{ex.Message}", "FreeW");
            }
        }

        // Decode any supported format and re-encode to PNG so the docx writer only ever emits PNG.
        // SVG files are rasterized via SvgRasterizerHelper (SharpVectors) at a sensible default size,
        // preserving aspect ratio. No new model field is needed — the result is plain PNG bytes.
        private static InlineImage LoadAsInlineImage(string path)
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return SvgRasterizerHelper.RasterizeToInlineImage(path);

            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(path);
            source.EndInit();
            source.Freeze();

            using var buffer = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(buffer);

            // Convert device-independent pixels to points, capping the width so large photos fit.
            var origPxW = source.PixelWidth;
            var origPxH = source.PixelHeight;
            var widthPt = origPxW / PxPerPoint;
            var heightPt = origPxH / PxPerPoint;
            if (widthPt > MaxWidthPt && widthPt > 0)
            {
                heightPt *= MaxWidthPt / widthPt;
                widthPt = MaxWidthPt;
            }
            // Store original pixel dimensions so Reset Size can restore the 100% natural size.
            return new InlineImage(buffer.ToArray(), widthPt, heightPt)
            {
                OriginalPixelWidth  = origPxW,
                OriginalPixelHeight = origPxH,
            };
        }
    }

    // Insert > Illustrations > Icons: open the searchable icon picker (IconPickerDialog) and insert
    // the chosen SVG icon as a rasterised InlineImage via SvgRasterizerHelper. No new model type —
    // the result is plain PNG bytes that round-trip through DocxWriter/DocxReader identically to any
    // Insert Picture insert. Inserted at a sensible default size (≤ 72 pt = 1 inch square).
    private sealed class InsertIconCommand(DocumentView editor) : IRibbonCommand
    {
        // Icons are decorative items — 72 pt (1 inch) is a sane default; the user can resize after.
        private const double IconDefaultWidthPt = 72;

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var image  = IconPickerDialog.Prompt(owner);
            if (image is null)
                return;

            // Scale down to 72 pt wide (preserving aspect ratio) if the rasteriser returned larger.
            if (image.WidthPt > IconDefaultWidthPt && image.WidthPt > 0)
            {
                var scale  = IconDefaultWidthPt / image.WidthPt;
                image = new InlineImage(image.PngBytes, IconDefaultWidthPt, image.HeightPt * scale)
                {
                    OriginalPixelWidth  = image.OriginalPixelWidth,
                    OriginalPixelHeight = image.OriginalPixelHeight,
                };
            }

            editor.Focus();
            editor.InsertImage(image);
        }
    }

    // Insert > Illustrations > Screenshot > Screen Clipping: hide FreeW, let the user drag-select a screen
    // region (ScreenClipOverlay), capture it to PNG (ScreenshotCapture), restore FreeW, and insert the clip
    // as an inline image through the same DocumentView.InsertImage path Insert Picture uses. Escape / an
    // empty drag cancels and inserts nothing (mirroring Word).
    private sealed class ScreenClippingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var window = Window.GetWindow(editor);
            var previousState = window?.WindowState ?? WindowState.Normal;
            try
            {
                // Briefly hide FreeW so it isn't part of the captured region (Word does the same).
                if (window is not null)
                {
                    window.WindowState = WindowState.Minimized;
                    // Let the minimize animation settle before the overlay/capture so we grab the desktop,
                    // not a half-faded FreeW frame.
                    window.Dispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }

                var region = ScreenClipOverlay.PromptForRegion();

                if (window is not null)
                {
                    window.WindowState = previousState;
                    window.Activate();
                }

                if (region is not { } captured)
                    return;

                var pngBytes = ScreenshotCapture.CaptureRegionPng(captured);
                if (pngBytes is null)
                    return;

                var image = ScreenshotCapture.PngToInlineImage(pngBytes);
                editor.Focus();
                editor.InsertImage(image);
            }
            catch (Exception ex)
            {
                if (window is not null && window.WindowState == WindowState.Minimized)
                    window.WindowState = previousState;
                DialogMessageHelper.ShowError(window, $"Could not capture the screen clip:\n{ex.Message}", "FreeW");
            }
        }
    }

    // Insert > Illustrations > Image Size: prompt for a new width; the view scales height proportionally.
    private sealed class ImageSizeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select an image first, then choose Image Size.",
                    "FreeW");
                return;
            }

            if (ImageSizeDialog.Prompt(Window.GetWindow(editor), image.WidthPt, image.HeightPt) is { } size)
                editor.SetSelectedImageSize(size.Width, size.Height);
        }
    }

    // Insert > Illustrations > Alt Text: prompt for the selected image's accessibility description
    // (seeded from its current alt text) and store it on the model image. A blank entry clears it; the
    // text round-trips through docx as wp:docPr/@descr and surfaces as the image tooltip/automation name.
    private sealed class ImageAltTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select an image first, then choose Alt Text.",
                    "FreeW");
                return;
            }

            var text = TextPrompt.Ask(Window.GetWindow(editor), "Alt Text", "Description:", image.AltText ?? string.Empty);
            // A null result is a cancel (leave unchanged); an empty/blank string clears the alt text.
            if (text is not null)
                editor.SetSelectedImageAltText(text);
        }
    }

    // Insert > Illustrations > Align Left/Center/Right: set the alignment of the selected image's
    // (image-only) paragraph, reusing the existing ParagraphFormatting.Alignment round-trip. No-op when
    // no image is selected.
    private sealed class ImageAlignCommand(DocumentView editor, FreeW.Core.Model.TextAlignment alignment) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select an image first, then choose an image alignment.",
                    "FreeW");
                return;
            }
            editor.SetSelectedImageAlignment(alignment);
        }
    }

    private sealed class ImageWrapCommand(DocumentView editor, ImageWrapping wrapping) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Wrap Text");
                return;
            }

            editor.SetSelectedImageWrapping(wrapping);
        }
    }

    // Picture Format > Arrange > Rotate: rotate the selected image by a fixed step (relative to current).
    private sealed class ImageRotateStepCommand(DocumentView editor, double stepDeg) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Rotate");
                return;
            }
            var newAngle = (image.RotationAngle + stepDeg + 360) % 360;
            editor.SetSelectedImageRotation(newAngle, image.FlipH, image.FlipV);
        }
    }

    // Picture Format > Arrange > Flip Vertical / Flip Horizontal.
    private sealed class ImageFlipCommand(DocumentView editor, bool vertical) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Flip");
                return;
            }
            if (vertical)
                editor.SetSelectedImageRotation(image.RotationAngle, image.FlipH, !image.FlipV);
            else
                editor.SetSelectedImageRotation(image.RotationAngle, !image.FlipH, image.FlipV);
        }
    }

    // Picture Format > Arrange > Position: open the position dialog for floating offset + anchors.
    private sealed class ImagePositionCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Position");
                return;
            }
            var result = ImagePositionDialog.Prompt(
                Window.GetWindow(editor),
                image.HorizontalOffsetPt, image.VerticalOffsetPt,
                image.HorizontalAnchor, image.VerticalAnchor);
            if (result is { } r)
                editor.SetSelectedImagePosition(r.HOffset, r.VOffset, r.HAnchor, r.VAnchor);
        }
    }

    // Picture Format > Adjust > Crop: open the numeric crop dialog.
    private sealed class ImageCropCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Crop");
                return;
            }
            var result = ImageCropDialog.Prompt(
                Window.GetWindow(editor),
                image.CropLeft, image.CropRight, image.CropTop, image.CropBottom);
            if (result is { } r)
                editor.SetSelectedImageCrop(r.Left, r.Right, r.Top, r.Bottom);
        }
    }

    // Picture Format > Adjust > Reset Picture: restore natural size, clear rotation/flip/crop.
    private sealed class ImageResetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Reset Picture");
                return;
            }
            editor.ResetSelectedImage();
        }
    }

    // Picture Format > Adjust > Picture Border: open the border color/width/dash dialog.
    private sealed class ImageBorderCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Picture Border");
                return;
            }
            var result = ImageBorderDialog.Prompt(
                Window.GetWindow(editor),
                image.BorderColorHex, image.BorderWidthPt, image.BorderDash);
            if (result is { } r)
                editor.SetSelectedImageBorder(r.Color, r.Width, r.Dash);
        }
    }

    // Picture Format > Adjust > Corrections: set absolute brightness (keeps current contrast/saturation/transparency).
    private sealed class ImageBrightnessPresetCommand(DocumentView editor, double brightnessPct) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Corrections");
                return;
            }
            editor.SetSelectedImageAdjust(brightnessPct, image.ContrastPct, image.SaturationPct, image.TransparencyPct);
        }
    }

    // Picture Format > Adjust > Corrections: set absolute contrast (keeps current brightness/saturation/transparency).
    private sealed class ImageContrastPresetCommand(DocumentView editor, double contrastPct) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Corrections");
                return;
            }
            editor.SetSelectedImageAdjust(image.BrightnessPct, contrastPct, image.SaturationPct, image.TransparencyPct);
        }
    }

    // Picture Format > Adjust > Corrections: open the full Corrections+Color dialog.
    private sealed class ImageAdjustDialogCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Picture Corrections");
                return;
            }
            var result = ImageAdjustDialog.Prompt(
                Window.GetWindow(editor),
                image.BrightnessPct, image.ContrastPct, image.SaturationPct, image.TransparencyPct);
            if (result is { } r)
                editor.SetSelectedImageAdjust(r.Brightness, r.Contrast, r.Saturation, r.Transparency);
        }
    }

    // Picture Format > Adjust > Color: set absolute saturation (keeps current brightness/contrast/transparency).
    private sealed class ImageSaturationPresetCommand(DocumentView editor, double saturationPct) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Color");
                return;
            }
            editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, saturationPct, image.TransparencyPct);
        }
    }

    // Picture Format > Adjust > Color: open the Color dialog (saturation + full adjust).
    private sealed class ImageColorDialogCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Color");
                return;
            }
            var result = ImageAdjustDialog.Prompt(
                Window.GetWindow(editor),
                image.BrightnessPct, image.ContrastPct, image.SaturationPct, image.TransparencyPct);
            if (result is { } r)
                editor.SetSelectedImageAdjust(r.Brightness, r.Contrast, r.Saturation, r.Transparency);
        }
    }

    // Picture Format > Adjust > Transparency: set absolute transparency (keeps current brightness/contrast/saturation).
    private sealed class ImageTransparencyPresetCommand(DocumentView editor, double transparencyPct) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Transparency");
                return;
            }
            editor.SetSelectedImageAdjust(image.BrightnessPct, image.ContrastPct, image.SaturationPct, transparencyPct);
        }
    }

    // Picture Format > Adjust > Transparency: open the Transparency dialog.
    private sealed class ImageTransparencyDialogCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Transparency");
                return;
            }
            var result = ImageAdjustDialog.Prompt(
                Window.GetWindow(editor),
                image.BrightnessPct, image.ContrastPct, image.SaturationPct, image.TransparencyPct);
            if (result is { } r)
                editor.SetSelectedImageAdjust(r.Brightness, r.Contrast, r.Saturation, r.Transparency);
        }
    }

    // Picture Format > Arrange > Z-order: bring/send a floating image forward or to front/back.
    private sealed class FloatingZOrderCommand(
        DocumentView editor,
        ObjectFormatTarget target,
        ZOrderOperation operation) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var selected = target == ObjectFormatTarget.Picture
                ? editor.SelectedImage() is not null
                : editor.SelectedShape() is not null;
            if (!selected || !editor.ChangeSelectedFloatingZOrder(operation))
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    target == ObjectFormatTarget.Picture
                        ? "Select a floating picture first."
                        : "Select a floating shape first.",
                    "Z-Order");
                return;
            }
        }
    }

    // Picture Format > Color > Recolor preset: set the recolor mode (grayscale/sepia/washout/blackwhite/none).
    private sealed class ImageRecolorPresetCommand(DocumentView editor, ImageRecolorMode mode) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Recolor");
                return;
            }
            editor.SetSelectedImageRecolor(mode);
        }
    }

    // Picture Format > Color > Color Tone preset: warm/cool/neutral temperature shift.
    private sealed class ImageColorTempCommand(DocumentView editor, double temperaturePct) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Color Tone");
                return;
            }
            editor.SetSelectedImageRecolor(ImageRecolorMode.None, temperaturePct);
        }
    }

    // Picture Format > Picture Effects > Shadow preset: set the shadow preset (0=none, 1-5=presets).
    private sealed class ImageShadowPresetCommand(DocumentView editor, int preset) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Shadow");
                return;
            }
            editor.SetSelectedImageEffect(preset, image.GlowSizePt, image.GlowColorHex,
                image.ReflectionPreset, image.SoftEdgePt, image.BevelPreset);
        }
    }

    // Picture Format > Picture Effects > Reflection preset: set the reflection preset (0=none, 1-5=presets).
    private sealed class ImageReflectionPresetCommand(DocumentView editor, int preset) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Reflection");
                return;
            }
            editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                preset, image.SoftEdgePt, image.BevelPreset);
        }
    }

    // Picture Format > Picture Effects > Glow preset: set the glow size in points (0=no glow).
    private sealed class ImageGlowPresetCommand(DocumentView editor, double glowPt) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Glow");
                return;
            }
            editor.SetSelectedImageEffect(image.ShadowPreset, glowPt, image.GlowColorHex,
                image.ReflectionPreset, image.SoftEdgePt, image.BevelPreset);
        }
    }

    // Picture Format > Picture Effects > Soft Edges: set the soft-edge radius in points (0=none).
    private sealed class ImageSoftEdgeCommand(DocumentView editor, double radiusPt) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Soft Edges");
                return;
            }
            editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                image.ReflectionPreset, radiusPt, image.BevelPreset);
        }
    }

    // Picture Format > Picture Effects > Bevel preset: set the bevel preset (0=none, 1-4=presets).
    private sealed class ImageBevelPresetCommand(DocumentView editor, int preset) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Bevel");
                return;
            }
            editor.SetSelectedImageEffect(image.ShadowPreset, image.GlowSizePt, image.GlowColorHex,
                image.ReflectionPreset, image.SoftEdgePt, preset);
        }
    }

    // Picture Format > Adjust > Artistic Effects (W25): set the non-destructive artistic effect.
    private sealed class ImageArtisticEffectCommand(DocumentView editor, ImageArtisticEffect effect) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a picture first.", "Artistic Effects");
                return;
            }
            editor.SetSelectedImageArtisticEffect(effect);
        }
    }

    // Picture Format > Picture Styles: apply a bundled border + effect style preset.
    private sealed class ImageStylePresetCommand(DocumentView editor, PictureStylePreset preset) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;

            editor.Focus();
            editor.ApplySelectedImageStyle(preset);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedImage() is not null);
    }

    // Drawing Format / Picture Format > Arrange > Group: group ≥2 selected floating objects.
    private sealed class ChartQuickLayoutRibbonCommand(
        DocumentView editor,
        ChartQuickLayout layout) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;

            editor.Focus();
            editor.ApplySelectedChartQuickLayout(layout);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: editor.SelectedChart() is not null);
    }

    private sealed class SmartArtStructureRibbonCommand(
        DocumentView editor,
        SmartArtStructureOperation operation,
        Action execute) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled)
                return;
            editor.Focus();
            execute();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.IsEnabled(editor.SelectedSmartArt(), operation));
    }

    private sealed class SmartArtEditTextRibbonCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var current = editor.SelectedSmartArt();
            if (!SmartArtCommandPlanner.CanEdit(current))
                return;

            SmartArt? replacement;
            if (context.SelectedValue is { } nodeText)
            {
                replacement = SmartArtCommandPlanner.BuildEditedContent(current!.Kind, nodeText);
            }
            else
            {
                replacement = InsertSmartArtDialog.Prompt(Application.Current?.MainWindow, current);
            }

            if (replacement is null)
                return;
            editor.Focus();
            editor.ReplaceSelectedSmartArt(replacement);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.CanEdit(editor.SelectedSmartArt()));
    }

    private sealed class SmartArtStyleRibbonCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!GetState().IsEnabled || SmartArtCommandPlanner.ResolveStyle(context.SelectedValue) is not { } style)
                return;
            editor.Focus();
            editor.ApplySmartArtStyle(style);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: SmartArtCommandPlanner.CanEdit(editor.SelectedSmartArt()));
    }

    private sealed class ObjectGroupCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.HasMultipleFloatingObjectsSelected)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    "Select two or more floating objects first (Shift-click or Ctrl-click).", "Group");
                return;
            }
            editor.GroupSelectedFloatingObjects();
        }
    }

    // Drawing Format / Picture Format > Arrange > Ungroup: ungroup a selected DrawingGroup.
    private sealed class ObjectUngroupCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsGroupSelected)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    "Select a group first.", "Ungroup");
                return;
            }
            editor.UngroupSelectedFloatingObject();
        }
    }

    // Insert > Links > Link: prompt for a URL, then apply it as a hyperlink over the selection.
    private sealed class InsertHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text is { Length: > 0 } text && Uri.IsWellFormedUriString(text, UriKind.Absolute)
                ? text
                : "https://";
            var url = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed);
            if (!string.IsNullOrWhiteSpace(url))
                editor.ApplyHyperlink(url!.Trim());
        }
    }

    // Insert > Links > Edit Hyperlink: prompt for a new URL (seeded from the caret link's current URL),
    // then re-target the hyperlink at the caret. A no-op when the caret is not on a link.
    private sealed class EditHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsCaretOnHyperlink())
                return;
            var seed = editor.HyperlinkUrlAtCaret() is { Length: > 0 } current ? current : "https://";
            var url = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed, "Edit Hyperlink", "Address:");
            if (!string.IsNullOrWhiteSpace(url))
                editor.EditHyperlink(url!.Trim());
        }
    }

    // Insert > Links > Remove Hyperlink: strip the hyperlink at the caret, leaving its text. No-op off a link.
    private sealed class RemoveHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.RemoveHyperlink();
        }
    }

    // Insert > Links > ScreenTip: prompt for a ScreenTip (seeded from the current one) and set it on the
    // hyperlink at the caret. A blank entry clears the ScreenTip. No-op when the caret is not on a link.
    private sealed class HyperlinkTooltipCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsCaretOnHyperlink())
                return;
            var seed = editor.HyperlinkTooltipAtCaret() ?? string.Empty;
            var tip = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed, "Set ScreenTip", "ScreenTip:");
            // A null result is a cancel (leave unchanged); an empty/blank string clears the ScreenTip.
            if (tip is not null)
                editor.SetHyperlinkTooltip(tip);
        }
    }

    // Insert > Symbols > Symbol: show a glyph grid and insert the chosen glyph at the caret as text.
    private sealed class InsertSymbolCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var glyph = SymbolPickerDialog.Prompt(Window.GetWindow(editor));
            if (!string.IsNullOrEmpty(glyph))
                editor.InsertText(glyph);
        }
    }

    // Insert > Symbols > Date & Time: list formatted current date/time strings; insert the chosen one as
    // plain text or, when "Update automatically" is checked, as a live DATE/TIME complex field.
    private sealed class InsertDateTimeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = DateTimeDialog.Prompt(Window.GetWindow(editor));
            if (result is null)
                return;
            if (result.IsField && result.FieldInstruction is { Length: > 0 } instruction)
                editor.InsertComplexField(instruction);
            else if (!string.IsNullOrEmpty(result.Text))
                editor.InsertText(result.Text);
        }
    }

    // Insert > Quick Parts > Document Property: insert a live field run bound to a document-property
    // value (Title, Subject, Author, Keywords, Comments). Uses RunFieldKind so the run renders the
    // current property value immediately and serialises as w:fldSimple for lossless round-trip.
    private sealed class InsertDocPropFieldCommand(DocumentView editor, RunFieldKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.InsertField(kind);
        }
    }

    // Insert > Text > Drop Cap > Options: a dialog that accepts position (Dropped / In Margin / None),
    // font, lines-to-drop, and distance-from-text.  Position and lines-to-drop drive the font-size
    // calculation (lines × default line height, approximated as 12 pt × lines); font is applied to the
    // cap run; "None" calls ClearDropCap.  Distance-from-text is noted in the dialog but deferred at
    // the model level (no kerning/spacing property exists for the cap run yet).
    private sealed class DropCapOptionsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = global::FreeW.App.Host.DropCapOptionsDialog.Prompt(Window.GetWindow(editor));
            if (result is null)
                return;
            if (result.Position == DropCapDialogPosition.None)
            {
                editor.ClearDropCap();
                return;
            }
            // Map lines-to-drop to an approximate point size (Word default body is 12 pt; each drop
            // line therefore adds ~12 pt to the cap height — a reasonable approximation without live
            // pagination).  Clamp to a sensible range.
            editor.ApplyDropCap(result.ModelPosition, result.SizePt, result.LinesToDrop, result.DistanceFromTextPt);
        }
    }

    // Insert > References > Footnote: prompt for the footnote text, then insert a footnote reference
    // at the caret. The view allocates the next id, stores the content and drops a superscript marker.
    private sealed class InsertFootnoteCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Insert Footnote", "Footnote text:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to anchor a footnote to
            editor.Focus();
            editor.InsertFootnote(text.Trim());
        }
    }

    // Insert > References > Endnote: prompt for the endnote text, then insert an endnote reference
    // at the caret. The view allocates the next id, stores the content and drops a superscript marker.
    private sealed class InsertEndnoteCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Insert Endnote", "Endnote text:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to anchor an endnote to
            editor.Focus();
            editor.InsertEndnote(text.Trim());
        }
    }

    // References > Footnotes > Next Footnote: move among rendered note reference markers, wrapping like
    // Word. The dropdown exposes previous footnote and endnote variants because FreeW already has both
    // backed note stores and rendered markers.
    private sealed class NavigateNoteCommand(DocumentView editor, bool footnote, bool previous) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var moved = (footnote, previous) switch
            {
                (true, true) => editor.MoveToPreviousFootnote(),
                (true, false) => editor.MoveToNextFootnote(),
                (false, true) => editor.MoveToPreviousEndnote(),
                _ => editor.MoveToNextEndnote()
            };

            if (!moved)
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    footnote
                        ? "This document does not contain any footnotes."
                        : "This document does not contain any endnotes.",
                    footnote ? "Footnotes" : "Endnotes");
        }
    }

    // References > Footnotes > Show Notes: show the document-local footnote/endnote stores in a read-only
    // list. Word opens a notes pane; FreeW does not yet have editable note-pane chrome, so this exposes
    // the backed note content without inventing a false editing surface.
    private sealed class ShowNotesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var items = NoteListItem.Build(editor.Model);
            if (items.Count == 0)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "This document does not contain any footnotes or endnotes.",
                    "Show Notes");
                return;
            }

            NotesListDialog.Show(Window.GetWindow(editor), items);
        }
    }

    private sealed record NoteListItem(string Kind, int Id, string Text)
    {
        public static IReadOnlyList<NoteListItem> Build(TextDocument document)
        {
            var items = new List<NoteListItem>();
            items.AddRange(document.Footnotes.Values
                .OrderBy(note => note.Id)
                .Select(note => new NoteListItem("Footnote", note.Id, note.PlainText)));
            items.AddRange(document.Endnotes.Values
                .OrderBy(note => note.Id)
                .Select(note => new NoteListItem("Endnote", note.Id, note.PlainText)));
            return items;
        }
    }

    private static class NotesListDialog
    {
        public static void Show(Window? owner, IReadOnlyList<NoteListItem> items)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 440,
                MinHeight = 220,
                Margin = new Thickness(0, 0, 0, 12)
            };

            foreach (var item in items)
                list.Items.Add($"{item.Kind} {item.Id}: {item.Text}");

            var dialog = new Window
            {
                Title = "Show Notes",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var close = new System.Windows.Controls.Button
            {
                Content = "Close",
                IsCancel = true,
                MinWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{items.Count} note{(items.Count == 1 ? string.Empty : "s")}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(list);
            panel.Children.Add(close);
            dialog.Content = panel;

            dialog.ShowDialog();
        }
    }

    // Insert > References > Footnote/Endnote Options: open the Footnote and Endnote numbering options
    // dialog (number format, start-at, restart mode for both footnotes and endnotes). Applies the chosen
    // settings to the document's FootnoteNumbering / EndnoteNumbering, which round-trip as w:footnotePr /
    // w:endnotePr in word/settings.xml.
    private sealed class FootnoteEndnoteOptionsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var model = editor.Model;
            var result = FootnoteEndnoteOptionsDialog.Prompt(owner, model.FootnoteNumbering, model.EndnoteNumbering);
            if (result is null)
                return;

            // Apply the chosen numbering options. The model properties are mutable; we mutate in-place
            // and commit via ApplyPageSettings (a page-settings no-op) so the editor commits pending
            // edits, re-renders (marking the document dirty) and the settings round-trip on next save.
            model.FootnoteNumbering.NumberFormat = result.FootnoteFormat;
            model.FootnoteNumbering.StartAt = result.FootnoteStartAt;
            model.FootnoteNumbering.NumberRestart = result.FootnoteRestart;
            model.EndnoteNumbering.NumberFormat = result.EndnoteFormat;
            model.EndnoteNumbering.StartAt = result.EndnoteStartAt;
            model.EndnoteNumbering.NumberRestart = result.EndnoteRestart;
            editor.ApplyPageSettings(_ => { });  // commits pending edits + marks document dirty
        }
    }

    // Insert > References > Citation: insert an in-text citation at the caret. If the document already
    // has sources, the user picks one (or chooses "Add New Source…"); otherwise they go straight to the
    // new-source form. A new source is upserted into the document and master source lists, then its
    // in-text citation is inserted.
    private sealed class InsertCitationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var sources = editor.Sources;

            Source? chosen;
            if (sources.Count > 0)
            {
                var pick = SourcePicker.Ask(owner, sources);
                if (pick is null)
                    return; // cancelled
                chosen = pick.AddNew ? PromptForNewSource(owner) : pick.Source;
            }
            else
            {
                chosen = PromptForNewSource(owner);
            }

            if (chosen is null)
                return; // cancelled or nothing entered

            editor.Focus();
            editor.InsertCitation(chosen);
        }

        // Show the new-source form, apply it to the document and master source lists, and return the
        // citation source (or null if the user cancelled or left no citeable source details).
        private Source? PromptForNewSource(Window? owner)
        {
            var entry = NewSourceDialog.Ask(owner);
            if (entry is null)
                return null;

            var masterStore = MasterSourceStore.Load();
            var state = SourceManagementDialogPlanner.BuildInitialState(editor.Sources, masterStore.ToSources());
            var plan = SourceManagementDialogPlanner.AddCitationSource(state, entry);
            if (plan.Validation is not null || plan.Source is null)
                return null;

            var result = SourceManagementDialogPlanner.BuildResult(plan.State);
            editor.ReplaceSources(result.CurrentSources);
            MasterSourceStore.Save(CreateMasterStore(result.MasterSources));
            return plan.Source;
        }
    }

    // References > Citations & Bibliography > Manage Sources: edit the document-local source list and
    // the shared master source list.
    private sealed class ManageSourcesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var masterStore = MasterSourceStore.Load();
            var result = ManageSourcesDialog.Ask(Window.GetWindow(editor), editor.Sources, masterStore.ToSources());
            if (result is null)
                return;

            editor.Focus();
            editor.ReplaceSources(result.CurrentSources);
            MasterSourceStore.Save(CreateMasterStore(result.MasterSources));
        }
    }

    // Insert > References > Caption: pick a label (Figure/Table — defaulting to Table when the caret is
    // in a table, else Figure), prompt for the caption text, then insert a numbered caption under the
    // caret's block. The view computes the next ordinal by counting existing captions of that label.
    private sealed class InsertCaptionCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var defaultLabel = editor.IsCaretInTable() ? Captions.TableLabelText : Captions.FigureLabelText;

            var label = CaptionLabelPicker.Ask(owner, defaultLabel);
            if (label is null)
                return; // cancelled

            var text = TextPrompt.Ask(owner, "Insert Caption", "Caption text (optional):", string.Empty);
            if (text is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertCaption(label, text.Trim());
        }
    }

    private sealed class InsertCaptionLabelCommand(DocumentView editor, CaptionLabel label) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var text = TextPrompt.Ask(owner, "Insert Caption", "Caption text (optional):", string.Empty);
            if (text is null)
                return;

            editor.Focus();
            editor.InsertCaption(label, text.Trim());
        }
    }

    // A tiny modal dialog choosing the caption label, seeded with a default. Returns
    // the chosen label, or null if cancelled.
    private static class CaptionLabelPicker
    {
        public static string? Ask(Window? owner, string defaultLabel)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var label in Captions.BuiltInLabelTexts)
                list.Items.Add(label);
            list.SelectedItem = defaultLabel;

            string? result = null;
            var dialog = new Window
            {
                Title = "Insert Caption",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var newLabel = new System.Windows.Controls.Button { Content = "New Label...", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Choose()
            {
                if (list.SelectedItem is string chosen)
                {
                    result = chosen;
                    dialog.DialogResult = true;
                }
            }
            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();
            newLabel.Click += (_, _) =>
            {
                var custom = TextPrompt.Ask(dialog, "New Label", "Label:", string.Empty);
                if (string.IsNullOrWhiteSpace(custom))
                    return;
                result = Captions.NormalizeLabelText(custom);
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(newLabel);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Label:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Review > Comments > New Comment: prompt for the comment text, then attach it over the current
    // selection. The author comes from the document's Author property (falling back to the OS user),
    // with initials derived from it; the view marks the selected runs and stores the comment.
    private sealed class NewCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "New Comment", "Comment:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to attach

            var author = CommentAuthor.Resolve(editor);
            editor.Focus();
            editor.InsertComment(text.Trim(), author, CommentAuthor.DeriveInitials(author));
        }
    }

    // The author/initials a new comment or reply is stamped with: the document's Author property, falling
    // back to the OS user, with initials derived from it. Shared by New Comment + Reply so the two stamp
    // the same identity. Kept tiny + static so it carries no editor state.
    private static class CommentAuthor
    {
        public static string Resolve(DocumentView editor)
        {
            var author = editor.Model.Properties.Author;
            if (string.IsNullOrWhiteSpace(author))
                author = Environment.UserName;
            return author?.Trim() ?? string.Empty;
        }

        // Initials = the first letter of each whitespace-separated word, upper-cased (max 3).
        public static string DeriveInitials(string author)
        {
            var parts = author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length > 0 ? initials : "?";
        }
    }

    // Review > Comments > Reply: prompt for reply text and append it to the comment thread covering the
    // caret/selection. Warns when the caret is not inside a comment. The reply is stamped with the same
    // author/initials a new comment uses.
    private sealed class ReplyCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Reply", "Reply:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty

            var author = CommentAuthor.Resolve(editor);
            editor.Focus();
            if (!editor.ReplyToCommentAtCaret(text.Trim(), author, CommentAuthor.DeriveInitials(author)))
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "Place the cursor inside a comment, then choose Reply.", "Reply");
        }
    }

    // Review > Comments > Resolve: toggle the resolved (done) state of the comment thread covering the
    // caret/selection (resolved ranges render muted). Warns when the caret is not inside a comment.
    private sealed class ResolveCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.ToggleResolveCommentAtCaret() is null)
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "Place the cursor inside a comment, then choose Resolve.", "Resolve");
        }
    }

    // Review > Comments > Delete: remove the comment thread covering the caret and clear its body marks.
    private sealed class DeleteCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.DeleteCommentAtCaret())
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "Place the cursor inside a comment, then choose Delete.", "Delete Comment");
        }
    }

    // Review > Comments > Previous / Next: step through comment threads in document order, wrapping like Word.
    private sealed class NavigateCommentCommand(DocumentView editor, bool previous) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var moved = previous ? editor.MoveToPreviousComment() : editor.MoveToNextComment();
            if (!moved)
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "This document does not contain any comments.", previous ? "Previous Comment" : "Next Comment");
        }
    }

    // Review > Comments > Show Comments: open a backed read-only list of the document's actual comment
    // threads in document order. This mirrors Word's visible comments-pane affordance without inventing
    // cloud/collaboration behavior.
    private sealed class ShowCommentsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var items = CommentListPlanner.Build(editor.Model);
            if (items.Count == 0)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "This document does not contain any comments.",
                    "Comments");
                return;
            }

            CommentListDialog.Show(Window.GetWindow(editor), items);
        }
    }

    private static class CommentListDialog
    {
        public static void Show(Window? owner, IReadOnlyList<CommentListItem> items)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 440,
                MinHeight = 260,
                Margin = new Thickness(0, 0, 0, 12)
            };

            foreach (var item in items)
            {
                var status = item.Resolved ? "Resolved" : "Open";
                var replies = item.ReplyCount == 1 ? "1 reply" : $"{item.ReplyCount} replies";
                list.Items.Add($"#{item.Id + 1} {status} - {item.Author} - {item.Text} ({replies})");
            }

            var dialog = new Window
            {
                Title = "Comments",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var close = new System.Windows.Controls.Button
            {
                Content = "Close",
                IsCancel = true,
                MinWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{items.Count} comment thread{(items.Count == 1 ? string.Empty : "s")}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(list);
            panel.Children.Add(close);
            dialog.Content = panel;

            dialog.ShowDialog();
        }
    }

    // Review > Proofing > Word Count: commit pending edits, then open the statistics dialog. The dialog
    // accepts the TextDocument directly so it can recompute when the user toggles "Include footnotes
    // and endnotes" — no need to pre-compute here.
    private sealed class StatisticsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var dialog = new StatisticsDialog(Window.GetWindow(editor)!, editor.Model);
            dialog.ShowDialog();
        }
    }

    // Review > Inspect > Check Accessibility: commit pending edits, run the pure AccessibilityChecker over
    // the model, and show the report in a read-only modal (issues grouped by severity). Read-only.
    private sealed class CheckAccessibilityCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var report = AccessibilityChecker.Check(editor.Model);
            var dialog = new AccessibilityReportDialog(Window.GetWindow(editor)!, report);
            dialog.ShowDialog();
        }
    }

    // Focuses the editor and drops an equation-gallery preset at the caret via the editor's undoable
    // insert path (same path as the default Equation button). Used by the Insert > Equation dropdown.
    private static void InsertEquationPreset(DocumentView editor, Equation equation)
    {
        editor.Focus();
        editor.InsertEquation(equation);
    }

    // A sample equation ("E = mc^2") built from explicit math fragments so its linear form renders the
    // superscript. Used by the Insert > Media > Equation ribbon button as a starting point.
    private static Equation SampleEquation()
    {
        var equation = new Equation();
        equation.Runs.Add(MathRun.PlainText("E = m"));
        equation.Runs.Add(MathRun.Superscript("c", "2"));
        return equation;
    }

    // A sample embedded OLE object for the Insert > Media > Object ribbon button: a small "Package"-ProgID
    // payload (a generic embedded package — Word's default for an unknown embedded file). Iconless; the
    // editor renders a labelled placeholder in its place. A starting point the user can replace.
    private static EmbeddedObject SampleEmbeddedObject() =>
        EmbeddedObject.Create(
            System.Text.Encoding.UTF8.GetBytes("FreeW embedded object placeholder."),
            progId: "Package");

    // Review > Proofing > Add to Dictionary: take the misspelled word the caret currently sits on, add
    // it to FreeW's custom dictionary (persisted to the .lex file under the data folder), and re-read the
    // dictionary so the word stops being flagged. When the caret is not on a spelling error, tell the
    // user to click into a flagged (red-underlined) word first. A no-op for a word already present.
    private sealed class AddToDictionaryCommand(DocumentView editor, CustomDictionaryStore dictionary) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var word = editor.MisspelledWordAtCaret();
            if (string.IsNullOrEmpty(word))
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Click into a misspelled (red-underlined) word first, then choose Add to Dictionary.",
                    "FreeW");
                return;
            }

            // Add + persist; only refresh the live spell-check when the word was newly added (a word
            // already in the dictionary needs no reload).
            if (dictionary.Add(word))
                editor.RefreshCustomDictionary();
        }
    }

    // Review > Proofing > Spell Check: a stateful toggle over the editor's built-in spell checking
    // (SpellCheck.IsEnabled). Executing flips the red-squiggle checking on/off; the checked state
    // reflects whether checking is currently on so the ribbon button shows it at a glance.
    private sealed class SpellCheckToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ToggleSpellCheck();
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.SpellCheckEnabled);
    }

    // Review > Tracking > Track Changes: a stateful toggle over the editor's Track Changes mode. Body
    // text edits are now recorded by the WPF editor; when switching on over a non-empty selection we still
    // mark that selection immediately, matching the visible feedback users expect from Word.
    private sealed class TrackChangesToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var plan = TrackChangesTogglePlanner.Build(
                editor.TrackChangesEnabled,
                hasSelection: !editor.Selection.IsEmpty);
            editor.TrackChangesEnabled = plan.Enabled;

            // When switching ON over a non-empty selection, mark it as an insertion as the WPF/FreeW
            // transition contract. This keeps the toggle useful without brittle per-keystroke interception.
            if (plan.MarkSelectionAsInsertion)
            {
                var dateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                editor.MarkSelectionAsRevision(RevisionKind.Inserted, editor.RevisionAuthor, dateXml);
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.TrackChangesEnabled);
    }

    private sealed class TrackFormattingToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.TrackFormattingEnabled = !editor.TrackFormattingEnabled;
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.TrackFormattingEnabled);
    }

    // Review > Tracking > Display for Review: exposes the ReviewDisplayMode dropdown. The root button
    // and the "All Markup" menu item both set AllMarkup mode; No Markup and Original are now implemented
    // using a transparent-run technique that keeps every revision run in the WPF tree so CommitToModel
    // can round-trip text + RevisionMarker safely (data-loss risk is closed).
    private sealed class DisplayForReviewCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);
        }

        // The root button IsChecked is true when in All Markup (the default), matching Word's convention.
        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == ReviewDisplayMode.AllMarkup);
    }

    // Review > Tracking > Display for Review > Simple Markup: inline rendering identical to No Markup
    // (final form — insertions plain, deletions hidden) plus a left-margin change bar drawn by
    // ChangeBarAdorner beside every paragraph that carries a tracked-change run. RevisionMarker is always
    // written so text + revision kind/author/date survive CommitToModel unchanged (round-trip safe).
    private sealed class DisplayForReviewSimpleMarkupCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == ReviewDisplayMode.SimpleMarkup);
    }

    // Review > Tracking > Display for Review > No Markup: insertions shown as plain text; deleted runs
    // rendered invisible (transparent foreground + near-zero font size). RevisionMarker is always written
    // so text + revision kind/author/date survive CommitToModel unchanged (round-trip safe).
    private sealed class DisplayForReviewNoMarkupCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == ReviewDisplayMode.NoMarkup);
    }

    // Review > Tracking > Display for Review > Original: deleted runs shown as plain text; inserted runs
    // rendered invisible (same transparent technique as No Markup). Round-trip safe via RevisionMarker.
    private sealed class DisplayForReviewOriginalCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(ReviewDisplayMode.Original);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == ReviewDisplayMode.Original);
    }

    // Review > Tracking > Show Markup > Insertions and Deletions: a stateful toggle. OFF suppresses
    // revision colour and underline/strikethrough decoration in the rendered view; the RevisionMarker
    // tag is still written so revisions survive CommitToModel unchanged (round-trip safe).
    private sealed class ShowMarkupInsertionsDeletionsCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyShowMarkupInsertionsAndDeletions(!editor.ShowMarkupInsertionsAndDeletions);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupInsertionsAndDeletions);
    }

    // Review > Tracking > Show Markup > Comments: a stateful toggle. OFF suppresses the comment
    // background highlight in the rendered view; the CommentMarker tag is still written so comment
    // ids survive CommitToModel unchanged (round-trip safe).
    private sealed class ShowMarkupCommentsCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyShowMarkupComments(!editor.ShowMarkupComments);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupComments);
    }

    // Review > Tracking > Show Markup > Formatting: a stateful toggle. OFF suppresses the dotted
    // underline decoration that marks tracked formatting changes (w:rPrChange / FormatRevision). The
    // FormatRevisionMarker tag is still written unconditionally so FormatRevision survives CommitToModel
    // unchanged (round-trip safe). Default is ON; most documents have no format revisions so this is
    // visually quiet.
    private sealed class ShowMarkupFormattingCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyShowMarkupFormatting(!editor.ShowMarkupFormatting);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.ShowMarkupFormatting);
    }

    // Review > Protect > Restrict Editing: opens the Restrict Editing pane to choose the allowed editing
    // type and start enforcing (or stop protection). The chosen ProtectionMode is enforced on the live
    // editor (read-only for No-changes/Comments/Forms, forced track-changes for Tracked) and emits
    // word/settings.xml's w:documentProtection on save. The checked state reflects whether protection is
    // currently enforced, so the ribbon button shows the lock state at a glance.
    private sealed class RestrictEditingToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var chosen = RestrictEditingDialog.Prompt(Window.GetWindow(editor), editor.Model.Protection);
            if (chosen is { } settings)
                editor.SetProtection(settings);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.IsProtected);
    }

    // Review > Protect > Mark as Final: a stateful toggle over Word's advisory read-only flag. Turning it
    // ON makes the editor read-only, shows the "Marked as Final" banner and persists the _MarkAsFinal
    // custom property on save; turning it OFF ("Edit Anyway") restores editing. The checked state reflects
    // whether the document is currently marked final.
    private sealed class MarkAsFinalToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.SetMarkedAsFinal(!editor.IsMarkedAsFinal);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.IsMarkedAsFinal);
    }

    // Review > Speech > Read Aloud: a stateful toggle that starts/stops an in-box text-to-speech
    // read-through of the document from the caret to the end. The pure ReadAloudController owns the
    // play/stop state machine and segment extraction; the host wires it to a SystemSpeechEngine
    // (System.Speech) and maps the caret to the start segment. The engine is created lazily on first use so
    // construction is cheap, and the engine itself is robust when no voice is installed (degrades to a
    // no-op). The toggle is checked while a read-through is active; the controller raises StateChanged when
    // reading finishes on its own so the ribbon button clears.
    private sealed class ReadAloudToggleCommand : IRibbonStatefulCommand
    {
        private readonly DocumentView _editor;
        private SystemSpeechEngine? _engine;
        private ReadAloudController? _controller;

        // Re-raised to the registry so the ribbon state store refreshes when reading starts/stops — both on
        // user toggle and when the read-through completes on its own.
        public event Action? StateChanged;

        public ReadAloudToggleCommand(DocumentView editor) => _editor = editor;

        public void Execute(RibbonCommandContext context)
        {
            var controller = EnsureController();
            if (controller.IsActive)
            {
                controller.Stop();
                return;
            }

            // Commit pending edits and read from the caret's paragraph to the end (Word's behaviour).
            var start = _editor.ReadAloudStartSegmentIndex();
            controller.Start(_editor.Model, start);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: _controller?.IsActive ?? false);

        private ReadAloudController EnsureController()
        {
            if (_controller is not null)
                return _controller;

            _engine = new SystemSpeechEngine();
            _controller = new ReadAloudController(_engine);
            _controller.StateChanged += () => StateChanged?.Invoke();
            return _controller;
        }
    }

    // Review > Compare: two-phase dialog — first pick the original .docx (file picker), then confirm
    // and optionally override the reviewer name in the Compare Documents dialog — then load the legal
    // blackline result into the editor. The opened document is treated as the "original" and the current
    // document as the "revised"; differences appear as tracked insertions/deletions attributed to the
    // chosen author. Pending edits are committed first so the comparison reflects the on-screen text.
    private sealed class CompareDocumentsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);

            // Seed the author box from the document's Author property, falling back to the OS user.
            editor.CommitToModel();
            var revised = editor.Model;
            var defaultAuthor = revised.Properties.Author?.Trim();
            if (string.IsNullOrWhiteSpace(defaultAuthor))
                defaultAuthor = Environment.UserName;

            var revisedTitle = revised.Properties.Title?.Trim()
                ?? System.IO.Path.GetFileName(editor.CurrentFileName ?? string.Empty);

            var picked = CompareDocumentsDialog.Prompt(owner, defaultAuthor!, revisedTitle ?? string.Empty);
            if (picked is null)
                return;

            try
            {
                var original = DocxReader.Read(picked.OriginalFilePath);
                var dateXml = DateTimeOffset.UtcNow.ToString(
                    "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

                var compared = DocumentCompare.Compare(original, revised, picked.Author, dateXml, picked.Settings);
                editor.LoadModel(compared);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(owner, $"Could not compare the documents:\n{ex.Message}", "FreeW");
            }
        }
    }

    // Review > Combine: merge the revisions of two reviewers (Word's Combine Documents). The current
    // document is treated as reviewer A; the user picks the shared ORIGINAL (base) and reviewer B's revised
    // copy via the CombineDocumentsDialog — which confirms paths and lets the user override each reviewer's
    // author label — then the result loads as one document carrying BOTH reviewers' tracked insertions/
    // deletions, each attributed to its own author, via the pure DocumentCombine helper. Pending edits are
    // committed first so the combine reflects the on-screen text. Authors are seeded from each document's
    // Author property (falling back to the OS user for A and to "Reviewer 2" for B); the revision date is
    // stamped at combine time.
    private sealed class CombineDocumentsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);

            // Seed author boxes from the current document (reviewer A) and fall back to the OS user.
            editor.CommitToModel();
            var revisedA = editor.Model;

            var defaultAuthorA = revisedA.Properties.Author?.Trim();
            if (string.IsNullOrWhiteSpace(defaultAuthorA))
                defaultAuthorA = Environment.UserName;

            var reviewerATitle = revisedA.Properties.Title?.Trim()
                ?? System.IO.Path.GetFileName(editor.CurrentFileName ?? string.Empty);

            var picked = CombineDocumentsDialog.Prompt(
                owner,
                defaultAuthorA!,
                defaultAuthorB: "Reviewer 2",
                reviewerATitle: reviewerATitle ?? string.Empty);
            if (picked is null)
                return;

            try
            {
                var original = DocxReader.Read(picked.OriginalFilePath);
                var revisedB = DocxReader.Read(picked.ReviewerBFilePath);

                var dateXml = DateTimeOffset.UtcNow.ToString(
                    "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

                var combined = DocumentCombine.Combine(original, revisedA, picked.AuthorA, revisedB, picked.AuthorB, dateXml);
                editor.LoadModel(combined);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(owner, $"Could not combine the documents:\n{ex.Message}", "FreeW");
            }
        }
    }

    // Review > Inspect Document: commit pending edits, run the pure DocumentInspector over the model, and
    // open the inspector dialog reporting what was found. If the user ticks categories and clicks Remove,
    // apply the matching removal ops to editor.Model (mutating in place) and re-render the cleaned document.
    private sealed class InspectDocumentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var result = DocumentInspector.Inspect(editor.Model);
            var choice = DocumentInspectorDialog.Show(Window.GetWindow(editor), result);
            if (choice is null)
                return; // cancelled or nothing selected

            editor.ApplyInspectorRemovals(choice.Comments, choice.Revisions, choice.Properties, choice.Bookmarks);
        }
    }

    // Insert > Links > Bookmark: name the caret's paragraph as a bookmark target. Seeds the prompt
    // with any existing bookmark on that paragraph; an empty entry clears it.
    private sealed class InsertBookmarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var name = TextPrompt.Ask(Window.GetWindow(editor), "Bookmark",
                "Bookmark name (leave blank to remove):", string.Empty);
            if (name is null)
                return; // cancelled — leave the model untouched
            editor.SetBookmarkAtCaret(name);
        }
    }

    // Insert > Links > Link to Bookmark: pick an existing bookmark and link the selection to it. If no
    // bookmarks exist yet, tell the user to create one first.
    private sealed class LinkToBookmarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var bookmarks = editor.BookmarkNames();
            if (bookmarks.Count == 0)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "No bookmarks exist yet. Add a bookmark first (Insert › Bookmark), then link to it.",
                    "FreeW");
                return;
            }

            var chosen = BookmarkPicker.Ask(Window.GetWindow(editor), bookmarks);
            if (!string.IsNullOrWhiteSpace(chosen))
                editor.ApplyInternalLink(chosen!);
        }
    }

    // Insert > Links > Bookmark Manager: open the modal Bookmark Manager listing the document's
    // bookmarks with Go To (scroll/caret via BringBlockIntoView) and Delete (clear the marker).
    private sealed class BookmarkManagerCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            BookmarkManagerDialog.Show(Window.GetWindow(editor), editor);
        }
    }

    // A tiny modal dialog to pick one of the document's bookmark names. Returns the chosen name, or
    // null if cancelled.
    private static class BookmarkPicker
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> bookmarks)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 280,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var name in bookmarks)
                list.Items.Add(name);
            list.SelectedIndex = 0;

            string? result = null;
            var dialog = new Window
            {
                Title = "Link to Bookmark",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };
            list.MouseDoubleClick += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Bookmark:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Insert > References > Cross-reference: pick a reference type (Heading/Bookmark/Caption/Footnote)
    // and a target, then insert it. Anchored targets (bookmarks, or headings/captions that carry a
    // bookmark) are inserted as a clickable internal link; the rest as plain reference text.
    private sealed class InsertCrossReferenceCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);

            var pick = CrossReferenceDialog.Prompt(owner, editor.Model);
            if (pick is null)
                return; // cancelled or nothing to reference

            editor.InsertCrossReference(pick.Type, pick.Target, pick.InsertAs, pick.Hyperlink);
        }
    }

    // Insert > References > Mark Entry: mark a term for the document index. Seeds from the current
    // selection's text (the usual "select then mark" gesture) and lets the user confirm or edit the term;
    // with no selection the prompt starts blank. The view appends the term to the model's index entries
    // (ignoring blanks/duplicates). The matching index is built later by Insert Index.
    private sealed class MarkIndexEntryCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var term = TextPrompt.Ask(Window.GetWindow(editor), "Mark Index Entry", "Index term:", seed);
            if (string.IsNullOrWhiteSpace(term))
                return; // cancelled or empty — nothing to mark
            editor.MarkIndexEntry(term.Trim());
        }
    }

    // Insert > References > Mark Citation: mark the selection (seeding the long form) as a legal citation
    // for a Table of Authorities. Opens a small dialog to pick the category and confirm the long/short
    // forms, then drops a hidden TA field at the caret (the visible table is built later by Table of
    // Authorities). Cancelling or an empty long form marks nothing.
    // References > Table of Authorities: prompt for options then insert (or update) the ToA.
    // Opens the TableOfAuthoritiesDialog to collect Word's standard ToA options (category filter,
    // passim, keep original formatting, tab leader) and passes the resulting ToaOptions to the
    // document engine for the actual build.
    private sealed class InsertTableOfAuthoritiesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var picked = TableOfAuthoritiesDialog.Prompt(owner);
            if (picked is null)
                return; // cancelled
            editor.InsertTableOfAuthorities(picked.Options);
        }
    }

    private sealed class MarkCitationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var result = FreeW.App.Host.MarkCitationDialog.Prompt(
                Window.GetWindow(editor),
                MarkCitationDialogPlanner.BuildInitialState(seed));
            if (result is null)
                return; // cancelled or empty — nothing to mark
            editor.MarkCitation(result.Citation);
        }
    }

    // Insert > Quick Parts > Save Selection to Quick Parts: capture the current selection's text, prompt
    // for an entry name, and store it in the shared library (persisted under FreeW's data folder). An
    // empty selection or a blank/cancelled name is a no-op. Saving under an existing name overwrites it.
    private sealed class SaveQuickPartCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = editor.Selection.Text;
            if (string.IsNullOrEmpty(text))
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select some text first, then choose Save Selection to Quick Parts.",
                    "FreeW");
                return;
            }

            var name = TextPrompt.Ask(Window.GetWindow(editor), "Save to Quick Parts", "Name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return; // cancelled or blank — nothing to store under

            var part = QuickPartCommandPlanner.CreateSelection(text, name);
            if (part is not null)
                library.Save(part);
            editor.Focus();
        }
    }

    // Insert > Quick Parts > Insert Quick Part: pick a saved snippet from the library and insert its text
    // at the caret (through the editor's normal edit/undo path, so it is reversible). Reports when the
    // library is empty so the user knows to save one first.
    private sealed class InsertQuickPartCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (library.IsEmpty)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "No Quick Parts saved yet. Select some text and choose Save Selection to Quick Parts first.",
                    "FreeW");
                return;
            }

            var chosen = QuickPartPicker.Ask(Window.GetWindow(editor), library.Names);
            if (chosen is null)
                return; // cancelled

            var part = library.Get(chosen);
            if (part is null)
                return; // removed between listing and picking — nothing to insert

            editor.Focus();
            editor.InsertText(part.Text);
        }
    }

    // Insert > Quick Parts > Building Blocks Organizer: open a modal organizer over the shared snippet
    // library, listing every saved building block (name + gallery/category) with a preview, and offering
    // Insert (drops the block at the caret) and Delete (removes it from the persisted library).
    private sealed class BuildingBlocksOrganizerCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            BuildingBlocksOrganizerDialog.Show(Window.GetWindow(editor), editor, library);
        }
    }

    // A tiny modal dialog to pick one of the saved Quick Part names. Returns the chosen name, or null if
    // cancelled. Mirrors BookmarkPicker.
    private static class QuickPartPicker
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> names)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 280,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var name in names)
                list.Items.Add(name);
            list.SelectedIndex = 0;

            string? result = null;
            var dialog = new Window
            {
                Title = "Insert Quick Part",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Insert", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };
            list.MouseDoubleClick += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Quick Part:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // The outcome of the SourcePicker: either an existing source was chosen, or "Add New Source…" was.
    // A tiny modal dialog to pick one of the document's existing sources, or to choose "Add New Source…".
    // Returns the pick, or null if cancelled.
    private static class SourcePicker
    {
        public static SourceManagementPick? Ask(Window? owner, IReadOnlyList<Source> sources)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 320,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var item in SourceManagementDialogPlanner.BuildPickerItems(sources))
                list.Items.Add(item);
            list.SelectedIndex = 0;

            SourceManagementPick? result = null;
            var dialog = new Window
            {
                Title = SourceManagementDialogPlanner.SourcePickerTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Insert", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var addNew = new System.Windows.Controls.Button { Content = SourceManagementDialogPlanner.AddNewSourceButtonLabel, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            void Choose()
            {
                if (SourceManagementDialogPlanner.TryCreatePick(sources, list.SelectedIndex, out var pick))
                {
                    result = pick;
                    dialog.DialogResult = true;
                }
            }

            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();
            addNew.Click += (_, _) => { result = SourceManagementDialogPlanner.CreateAddNewPick(); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(addNew);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = SourceManagementDialogPlanner.SourcePickerLabel, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }

    }

    // A small modal form capturing a Word-style source type plus the fields for that type. Returns the
    // entry, or null if cancelled.
    private static class NewSourceDialog
    {
        public static SourceManagementSourceEntry? Ask(Window? owner, Source? source = null)
        {
            var typeChoices = SourceManagementDialogPlanner.BuildSourceTypeChoices();
            var entry = SourceManagementDialogPlanner.ProjectEntry(source);
            var fields = SourceManagementDialogPlanner
                .BuildEntryFieldPlans(entry)
                .ToDictionary(plan => plan.Field, plan => NewField(plan.Text));

            SourceManagementSourceEntry? result = null;
            var dialog = new Window
            {
                Title = source is null
                    ? SourceManagementDialogPlanner.AddNewSourceTitle
                    : SourceManagementDialogPlanner.EditSourceTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var typeBox = new System.Windows.Controls.ComboBox
            {
                ItemsSource = typeChoices,
                DisplayMemberPath = nameof(SourceManagementSourceTypeChoice.Label),
                SelectedIndex = SourceManagementDialogPlanner.SourceTypeSelectedIndex(entry.Type),
                MinWidth = 320,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var fieldPanel = new System.Windows.Controls.StackPanel();

            SourceType SelectedType() =>
                typeBox.SelectedItem is SourceManagementSourceTypeChoice choice
                    ? choice.Type
                    : SourceType.Book;

            SourceManagementSourceEntry CurrentEntry() =>
                SourceManagementDialogPlanner.CreateEntry(
                    SelectedType(),
                    fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),
                    entry);

            void EditPrimaryAuthor()
            {
                var current = CurrentEntry();
                var state = AuthorEditorDialog.Ask(dialog, current);
                if (state is null)
                    return;

                entry = SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(current, state);
                if (!fields.TryGetValue(SourceManagementSourceField.Author, out var authorField))
                {
                    authorField = NewField();
                    fields[SourceManagementSourceField.Author] = authorField;
                }

                authorField.Text = entry.Author;
                RefreshFields();
                authorField.Focus();
            }

            void RefreshFields()
            {
                fieldPanel.Children.Clear();
                foreach (var plan in SourceManagementDialogPlanner.BuildEntryFieldPlans(CurrentEntry()))
                {
                    if (!fields.TryGetValue(plan.Field, out var box))
                    {
                        box = NewField(plan.Text);
                        fields[plan.Field] = box;
                    }

                    if (plan.Field == SourceManagementSourceField.Author)
                        AddAuthorRow(fieldPanel, plan.Label, box, EditPrimaryAuthor);
                    else
                        AddRow(fieldPanel, plan.Label, box);
                }
            }

            typeBox.SelectionChanged += (_, _) => RefreshFields();

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = CurrentEntry();
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            AddRow(panel, SourceManagementDialogPlanner.SourceTypeLabel, typeBox);
            panel.Children.Add(fieldPanel);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            RefreshFields();
            if (fields.TryGetValue(SourceManagementSourceField.Author, out var authorField))
                authorField.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox NewField(string? value = null) =>
            new() { Text = value ?? string.Empty, MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };

        private static void AddAuthorRow(
            System.Windows.Controls.Panel panel,
            string label,
            System.Windows.Controls.TextBox field,
            Action edit)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var row = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            row.Children.Add(field);

            var editButton = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.PrimaryAuthorEditorButtonLabel,
                MinWidth = 32,
                Margin = new Thickness(6, 0, 0, 10),
                ToolTip = SourceManagementDialogPlanner.PrimaryAuthorEditorButtonToolTip
            };
            editButton.Click += (_, _) => edit();
            row.Children.Add(editButton);
            panel.Children.Add(row);
        }

        private static void AddRow(System.Windows.Controls.Panel panel, string label, System.Windows.Controls.Control control)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(control);
        }
    }

    private static class AuthorEditorDialog
    {
        private sealed record RowControls(
            System.Windows.Controls.TextBox First,
            System.Windows.Controls.TextBox Middle,
            System.Windows.Controls.TextBox Last,
            System.Windows.Controls.Grid Host);

        public static SourceManagementAuthorEditorState? Ask(Window? owner, SourceManagementSourceEntry entry)
        {
            var initial = SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(entry);
            var rowControls = new List<RowControls>();
            SourceManagementAuthorEditorState? result = null;

            var dialog = new Window
            {
                Title = SourceManagementDialogPlanner.PrimaryAuthorEditorTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var personalMode = new System.Windows.Controls.RadioButton
            {
                Content = SourceManagementDialogPlanner.PersonalAuthorModeLabel,
                GroupName = "PrimaryAuthorMode",
                IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Personal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var corporateMode = new System.Windows.Controls.RadioButton
            {
                Content = SourceManagementDialogPlanner.CorporateAuthorModeLabel,
                GroupName = "PrimaryAuthorMode",
                IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Corporate,
                Margin = new Thickness(0, 8, 0, 6)
            };
            var peoplePanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(18, 0, 0, 0) };
            var rowsPanel = new System.Windows.Controls.StackPanel();
            var corporateLabel = new System.Windows.Controls.TextBlock
            {
                Text = SourceManagementDialogPlanner.CorporateAuthorLabel,
                Margin = new Thickness(18, 0, 0, 4)
            };
            var corporateBox = NewAuthorTextBox(initial.CorporateAuthor, minWidth: 360);

            void AddPersonRow(SourceManagementAuthorPersonRow row)
            {
                var grid = CreatePersonRowGrid();
                var first = NewAuthorTextBox(row.First);
                var middle = NewAuthorTextBox(row.Middle);
                var last = NewAuthorTextBox(row.Last, minWidth: 140);
                AddGridChild(grid, first, 0);
                AddGridChild(grid, middle, 1);
                AddGridChild(grid, last, 2);
                rowsPanel.Children.Add(grid);
                rowControls.Add(new RowControls(first, middle, last, grid));
            }

            void RemovePersonRow()
            {
                if (rowControls.Count <= 1)
                {
                    rowControls[0].First.Clear();
                    rowControls[0].Middle.Clear();
                    rowControls[0].Last.Clear();
                    return;
                }

                var last = rowControls[^1];
                rowsPanel.Children.Remove(last.Host);
                rowControls.RemoveAt(rowControls.Count - 1);
            }

            void RefreshMode()
            {
                var personal = personalMode.IsChecked == true;
                peoplePanel.IsEnabled = personal;
                corporateLabel.IsEnabled = !personal;
                corporateBox.IsEnabled = !personal;
            }

            IReadOnlyList<SourceManagementAuthorPersonRow> initialRows = initial.PersonalRows.Count == 0
                ? [new SourceManagementAuthorPersonRow(string.Empty, string.Empty, string.Empty)]
                : initial.PersonalRows;
            foreach (var row in initialRows)
            {
                AddPersonRow(row);
            }

            var header = CreatePersonRowGrid();
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorFirstNameLabel), 0);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorMiddleNameLabel), 1);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorLastNameLabel), 2);
            peoplePanel.Children.Add(header);
            peoplePanel.Children.Add(rowsPanel);

            var addRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.AddAuthorRowButtonLabel,
                MinWidth = 72,
                Margin = new Thickness(0, 4, 8, 0)
            };
            addRow.Click += (_, _) => AddPersonRow(new SourceManagementAuthorPersonRow(string.Empty, string.Empty, string.Empty));
            var removeRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.RemoveAuthorRowButtonLabel,
                MinWidth = 72,
                Margin = new Thickness(0, 4, 0, 0)
            };
            removeRow.Click += (_, _) => RemovePersonRow();
            peoplePanel.Children.Add(new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children = { addRow, removeRow }
            });

            personalMode.Checked += (_, _) => RefreshMode();
            corporateMode.Checked += (_, _) => RefreshMode();

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                var mode = corporateMode.IsChecked == true
                    ? SourceManagementAuthorEditorMode.Corporate
                    : SourceManagementAuthorEditorMode.Personal;
                result = SourceManagementDialogPlanner.NormalizePrimaryAuthorEditorState(
                    new SourceManagementAuthorEditorState(
                        mode,
                        rowControls.Select(row => new SourceManagementAuthorPersonRow(
                            row.First.Text ?? string.Empty,
                            row.Middle.Text ?? string.Empty,
                            row.Last.Text ?? string.Empty)).ToArray(),
                        corporateBox.Text ?? string.Empty));
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(personalMode);
            panel.Children.Add(peoplePanel);
            panel.Children.Add(corporateMode);
            panel.Children.Add(corporateLabel);
            panel.Children.Add(corporateBox);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            RefreshMode();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.Grid CreatePersonRowGrid()
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(140) });
            return grid;
        }

        private static System.Windows.Controls.TextBlock NewHeader(string text) =>
            new() { Text = text, Margin = new Thickness(0, 0, 6, 2) };

        private static System.Windows.Controls.TextBox NewAuthorTextBox(string? text, double minWidth = 104) =>
            new() { Text = text ?? string.Empty, MinWidth = minWidth, Margin = new Thickness(0, 0, 6, 0) };

        private static void AddGridChild(
            System.Windows.Controls.Grid grid,
            UIElement child,
            int column)
        {
            System.Windows.Controls.Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }
    }

    /// <summary>Return type for <see cref="ManageSourcesDialog.Ask"/>.</summary>
    private sealed record ManageSourcesResult(
        IReadOnlyList<Source> CurrentSources,
        IReadOnlyList<Source> MasterSources);

    private static class ManageSourcesDialog
    {
        public static ManageSourcesResult? Ask(
            Window? owner,
            IReadOnlyList<Source> sources,
            IReadOnlyList<Source> masterSources)
        {
            // The planner owns the working copies; mutations stay in dialog state until OK.
            var state = SourceManagementDialogPlanner.BuildInitialState(sources, masterSources);

            // ── left pane: Master List ────────────────────────────────────────────────────────
            var masterList = new System.Windows.Controls.ListBox
            {
                MinWidth = 220,
                MinHeight = 180,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // ── right pane: Current Document ─────────────────────────────────────────────────
            var docList = new System.Windows.Controls.ListBox
            {
                MinWidth = 220,
                MinHeight = 180,
                Margin = new Thickness(0, 0, 0, 4)
            };

            ManageSourcesResult? result = null;
            var dialog = new Window
            {
                Title = "Manage Sources",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            void RefreshMasterList(int? selectedIndex = null)
            {
                var selection = selectedIndex ?? masterList.SelectedIndex;
                masterList.Items.Clear();
                foreach (var item in SourceManagementDialogPlanner.BuildPickerItems(state.MasterSources))
                    masterList.Items.Add(item);
                SelectIndex(masterList, selection, state.MasterSources.Count);
            }

            void RefreshDocList(int? selectedIndex = null)
            {
                var selection = selectedIndex ?? docList.SelectedIndex;
                docList.Items.Clear();
                foreach (var item in SourceManagementDialogPlanner.BuildPickerItems(state.CurrentSources))
                    docList.Items.Add(item);
                SelectIndex(docList, selection, state.CurrentSources.Count);
            }

            void ShowValidation(SourceManagementValidation validation) =>
                DialogMessageHelper.ShowWarning(dialog, validation.Message, dialog.Title);

            bool ApplyCopyPlan(SourceManagementListMutationPlan plan, Action<int?> refresh)
            {
                if (plan.Conflict is not null)
                {
                    var action = AskConflictResolution(plan.Conflict);
                    if (action is null)
                        return false;

                    plan = SourceManagementDialogPlanner.ResolveSourceConflict(
                        state,
                        plan.Conflict,
                        action.Value);
                }

                state = plan.State;
                refresh(plan.SelectedIndex);
                return true;
            }

            SourceManagementSourceConflictResolutionAction? AskConflictResolution(
                SourceManagementSourceConflict conflict)
            {
                var choices = SourceManagementDialogPlanner.BuildSourceConflictResolutionChoices(conflict);
                var message = string.Join(
                    Environment.NewLine,
                    SourceManagementDialogPlanner.BuildSourceConflictMessage(conflict),
                    string.Empty,
                    $"Yes: {choices[0].Label}",
                    $"No: {choices[1].Label}",
                    "Cancel: Do nothing");
                var answer = DialogMessageHelper.ShowMessage(
                    dialog,
                    message,
                    SourceManagementDialogPlanner.SourceConflictDialogTitle,
                    UserMessageButtons.YesNoCancel,
                    UserMessageIcon.Warning);

                return answer switch
                {
                    UserMessageResult.Yes => choices[0].Action,
                    UserMessageResult.No => choices[1].Action,
                    _ => null
                };
            }

            void SelectIndex(System.Windows.Controls.ListBox list, int selectedIndex, int count)
            {
                list.SelectedIndex = count == 0 ? -1 : Math.Clamp(selectedIndex, 0, count - 1);
            }

            // ── master-list actions ───────────────────────────────────────────────────────────
            void AddToMaster()
            {
                var entry = NewSourceDialog.Ask(dialog);
                if (entry is null)
                    return;

                var plan = SourceManagementDialogPlanner.AddMasterSource(state, entry);
                if (plan.Validation is not null)
                {
                    ShowValidation(plan.Validation);
                    return;
                }

                state = plan.State;
                RefreshMasterList(plan.SelectedIndex);
            }

            void DeleteFromMaster()
            {
                var plan = SourceManagementDialogPlanner.DeleteMasterSource(state, masterList.SelectedIndex);
                state = plan.State;
                RefreshMasterList(plan.SelectedIndex);
            }

            void EditMasterSource()
            {
                var idx = masterList.SelectedIndex;
                if (idx < 0 || idx >= state.MasterSources.Count)
                    return;
                var entry = NewSourceDialog.Ask(dialog, state.MasterSources[idx]);
                if (entry is null)
                    return;

                var plan = SourceManagementDialogPlanner.EditMasterSource(state, idx, entry);
                if (plan.Validation is not null)
                {
                    ShowValidation(plan.Validation);
                    return;
                }

                state = plan.State;
                RefreshMasterList(plan.SelectedIndex);
            }

            // ── copy master → current doc ─────────────────────────────────────────────────────
            void CopyToDoc()
            {
                var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
                    state,
                    masterList.SelectedIndex,
                    docList.SelectedIndex);
                ApplyCopyPlan(plan, selectedIndex => RefreshDocList(selectedIndex));
            }

            void CopyToMaster()
            {
                var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
                    state,
                    docList.SelectedIndex,
                    masterList.SelectedIndex);
                ApplyCopyPlan(plan, selectedIndex => RefreshMasterList(selectedIndex));
            }

            // ── current-doc actions ───────────────────────────────────────────────────────────
            void AddToDoc()
            {
                var entry = NewSourceDialog.Ask(dialog);
                if (entry is null)
                    return;

                var plan = SourceManagementDialogPlanner.AddCurrentSource(state, entry);
                if (plan.Validation is not null)
                {
                    ShowValidation(plan.Validation);
                    return;
                }

                state = plan.State;
                RefreshDocList(plan.SelectedIndex);
            }

            void EditDocSource()
            {
                var idx = docList.SelectedIndex;
                if (idx < 0 || idx >= state.CurrentSources.Count)
                    return;
                var entry = NewSourceDialog.Ask(dialog, state.CurrentSources[idx]);
                if (entry is null)
                    return;

                var plan = SourceManagementDialogPlanner.EditCurrentSource(state, idx, entry);
                if (plan.Validation is not null)
                {
                    ShowValidation(plan.Validation);
                    return;
                }

                state = plan.State;
                RefreshDocList(plan.SelectedIndex);
            }

            void DeleteFromDoc()
            {
                var plan = SourceManagementDialogPlanner.DeleteCurrentSource(state, docList.SelectedIndex);
                state = plan.State;
                RefreshDocList(plan.SelectedIndex);
            }

            // ── buttons ───────────────────────────────────────────────────────────────────────
            var masterAdd    = new System.Windows.Controls.Button { Content = "Add...", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var masterEdit   = new System.Windows.Controls.Button { Content = "Edit...", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var masterDelete = new System.Windows.Controls.Button { Content = "Delete",  MinWidth = 72 };
            var copyBtn      = new System.Windows.Controls.Button { Content = "Copy →",  MinWidth = 72 };
            var copyBackBtn  = new System.Windows.Controls.Button { Content = "Copy <-", MinWidth = 72, Margin = new Thickness(0, 6, 0, 0) };
            var docAdd       = new System.Windows.Controls.Button { Content = "Add...", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var docEdit      = new System.Windows.Controls.Button { Content = "Edit...", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var docDelete    = new System.Windows.Controls.Button { Content = "Delete",  MinWidth = 72 };
            var ok           = new System.Windows.Controls.Button { Content = "OK",      IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel       = new System.Windows.Controls.Button { Content = "Cancel",  IsCancel = true,  MinWidth = 72 };

            masterAdd.Click    += (_, _) => AddToMaster();
            masterEdit.Click   += (_, _) => EditMasterSource();
            masterDelete.Click += (_, _) => DeleteFromMaster();
            copyBtn.Click      += (_, _) => CopyToDoc();
            copyBackBtn.Click  += (_, _) => CopyToMaster();
            docAdd.Click       += (_, _) => AddToDoc();
            docEdit.Click      += (_, _) => EditDocSource();
            docDelete.Click    += (_, _) => DeleteFromDoc();
            masterList.MouseDoubleClick += (_, _) => EditMasterSource();
            docList.MouseDoubleClick += (_, _) => EditDocSource();

            ok.Click += (_, _) =>
            {
                var plannedResult = SourceManagementDialogPlanner.BuildResult(state);
                result = new ManageSourcesResult(plannedResult.CurrentSources, plannedResult.MasterSources);
                dialog.DialogResult = true;
            };

            // ── layout ────────────────────────────────────────────────────────────────────────
            var masterButtons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            masterButtons.Children.Add(masterAdd);
            masterButtons.Children.Add(masterEdit);
            masterButtons.Children.Add(masterDelete);

            var masterPane = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            masterPane.Children.Add(new System.Windows.Controls.TextBlock { Text = SourceManagementDialogPlanner.MasterListLabel, Margin = new Thickness(0, 0, 0, 4) });
            masterPane.Children.Add(masterList);
            masterPane.Children.Add(masterButtons);

            var centerPane = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            centerPane.Children.Add(copyBtn);
            centerPane.Children.Add(copyBackBtn);

            var docButtons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            docButtons.Children.Add(docAdd);
            docButtons.Children.Add(docEdit);
            docButtons.Children.Add(docDelete);

            var docPane = new System.Windows.Controls.StackPanel();
            docPane.Children.Add(new System.Windows.Controls.TextBlock { Text = SourceManagementDialogPlanner.CurrentDocumentListLabel, Margin = new Thickness(0, 0, 0, 4) });
            docPane.Children.Add(docList);
            docPane.Children.Add(docButtons);

            var listsRow = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            listsRow.Children.Add(masterPane);
            listsRow.Children.Add(centerPane);
            listsRow.Children.Add(docPane);

            var closeButtons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButtons.Children.Add(ok);
            closeButtons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(listsRow);
            panel.Children.Add(closeButtons);
            dialog.Content = panel;

            RefreshMasterList();
            RefreshDocList();
            return dialog.ShowDialog() == true ? result : null;
        }

    }

    // Mailings: the shared mail-merge state across the four Mailings commands. Holds the data source
    // and, while previewing, the original template document plus the current record index so previewing
    // can step through records and restore the template when the preview ends.
    internal sealed class MailMergeSession
    {
        public MergeData? Data { get; set; }
        public MailMergeOutputMode Mode { get; set; } = MailMergeOutputMode.Letters;

        // Non-null only while a preview is active: the document that was in the editor before the first
        // Preview, so leaving the preview restores it (the user's editable template).
        public TextDocument? Template { get; set; }

        public int CurrentIndex { get; set; }

        // Role→column mapping for Address Block / Greeting Line composition. Null until the user loads
        // data (SetMergeDataCommand seeds it via AutoMatchFields) or opens Match Fields.
        public FieldMapping? Mapping { get; set; }

        public bool IsPreviewing => Template is not null;

        public void Clear()
        {
            Data = null;
            Template = null;
            CurrentIndex = 0;
            Mode = MailMergeOutputMode.Letters;
            Mapping = null;
        }

        /// <summary>
        /// Build an augmented row dictionary that adds synthetic «AddressBlock» and «GreetingLine»
        /// keys so the standard Substitute path resolves both composite placeholders per-record.
        /// When no mapping is set the synthetic keys map to empty strings.
        /// </summary>
        public IReadOnlyDictionary<string, string> AugmentRow(
            IReadOnlyDictionary<string, string> row,
            string greetingFormat = "Dear")
        {
            var augmented = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
            var mapping = Mapping ?? new FieldMapping();
            augmented["AddressBlock"] = MailMerge.ComposeAddressBlock(row, mapping);
            augmented["GreetingLine"] = MailMerge.ComposeGreetingLine(row, mapping, greetingFormat);
            return augmented;
        }
    }

    private sealed class SetMergeModeCommand(MailMergeSession session, MailMergeOutputMode mode) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            session.Mode = mode;
            session.Template = null;
            session.CurrentIndex = 0;
        }
    }

    private sealed class ClearMergeSessionCommand(MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => session.Clear();
    }

    // Mailings > Insert Merge Field: prompt for a field name and insert the placeholder «Name» at the
    // caret as ordinary text (through the editor's normal edit/undo path, so it is reversible). The
    // guillemets are added automatically; a name the user already wrapped in « » is accepted as-is.
    private sealed class InsertMergeFieldCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var name = TextPrompt.Ask(Window.GetWindow(editor), "Insert Merge Field", "Field name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return; // cancelled or blank — nothing to insert

            var trimmed = name.Trim().Trim(MailMerge.FieldOpen, MailMerge.FieldClose).Trim();
            if (trimmed.Length == 0)
                return;

            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{trimmed}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Insert Address Block: insert the «AddressBlock» composite placeholder at the caret.
    // The placeholder is resolved at preview/merge time via the session's FieldMapping (auto-matched or
    // user-customised via Match Fields). Opens Match Fields first if no data is loaded so the user can
    // configure the mapping before the placeholder lands in the document.
    private sealed class InsertAddressBlockCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then insert an Address Block.",
                    "Mail Merge");
                return;
            }

            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}");
        }
    }

    // Mailings > Insert Greeting Line: insert the «GreetingLine» composite placeholder at the caret.
    // Resolved per-record at preview/merge time using the session's FieldMapping.
    private sealed class InsertGreetingLineCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then insert a Greeting Line.",
                    "Mail Merge");
                return;
            }

            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}");
        }
    }

    // Mailings > Match Fields: let the user override the auto-matched role→column bindings. Opens the
    // MatchFieldsDialog seeded with the current (auto-matched) mapping. Saves changes back to the
    // session so subsequent Address Block / Greeting Line insertions and preview/merge use the new bindings.
    private sealed class MatchFieldsCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { } data)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then match fields.",
                    "Mail Merge");
                return;
            }

            var current = session.Mapping ?? MailMerge.AutoMatchFields(data.Header);
            var result = MatchFieldsDialog.Ask(Window.GetWindow(editor), data.Header, current);
            if (result is not null)
            {
                session.Mapping = result;
                // Invalidate any in-progress preview since the mapping changed.
                if (session.IsPreviewing)
                {
                    editor.LoadModel(session.Template!);
                    session.Template = null;
                    session.CurrentIndex = 0;
                }
            }

            editor.Focus();
        }
    }

    // Mailings > Rules (special fields): insert a native Word field while retaining the familiar label.
    private sealed class InsertSpecialMergeFieldCommand(DocumentView editor, string fieldName) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (MailMerge.TryGetNativeSpecialFieldInstruction(fieldName, out var instruction))
            {
                editor.InsertComplexField(
                    instruction,
                    $"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}");
                return;
            }

            editor.InsertText($"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}");
        }
    }

    // Merge Rules: command kind tag for Skip/Next Record If.
    private enum RuleCondKind { SkipRecordIf, NextRecordIf }

    // Mailings > Rules > If...Then...Else: ask the user for field/operator/value/true-text/false-text
    // via a dialog and insert the IF merge instruction at the caret.
    private sealed class InsertMergeRuleIfCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var header = session.Data?.Header ?? [];
            var result = MergeRuleIfDialog.Ask(Window.GetWindow(editor), header);
            if (result is null) return;
            var instruction = MergeRuleEvaluator.BuildIfInstruction(
                result.FieldName, result.Operator, result.Value, result.TrueText, result.FalseText);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Rules > Skip Record If / Next Record If: insert «Skip Record If …» or «Next Record If …».
    private sealed class InsertMergeRuleCondCommand(
        DocumentView editor,
        MailMergeSession session,
        RuleCondKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var header = session.Data?.Header ?? [];
            var label = kind == RuleCondKind.SkipRecordIf ? "Skip Record If" : "Next Record If";
            var result = MergeRuleCondDialog.Ask(Window.GetWindow(editor), header, label);
            if (result is null) return;
            var instruction = kind == RuleCondKind.SkipRecordIf
                ? MergeRuleEvaluator.BuildSkipRecordIfInstruction(result.FieldName, result.Operator, result.Value)
                : MergeRuleEvaluator.BuildNextRecordIfInstruction(result.FieldName, result.Operator, result.Value);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Rules > Fill-in: prompt for the prompt text; insert «Fill-in "Prompt"» at the caret.
    // At merge time MergeRuleEvaluator looks up the answer in MergeState.FillInAnswers (pre-populated
    // by FinishMergeCommand which shows the Fill-in dialogs before iterating records).
    private sealed class InsertMergeRuleFillInCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var prompt = MergeRulePromptDialog.AskPrompt(Window.GetWindow(editor), "Fill-in", "Enter the prompt text for this Fill-in field:");
            if (prompt is null) return;
            var instruction = MergeRuleEvaluator.BuildFillInInstruction(prompt);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Rules > Ask: prompt for bookmark name + prompt text; insert «Ask BookmarkName "Prompt"».
    private sealed class InsertMergeRuleAskCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var result = MergeRuleAskSetDialog.AskAsk(Window.GetWindow(editor));
            if (result is null) return;
            var instruction = MergeRuleEvaluator.BuildAskInstruction(result.Value.Name, result.Value.Value);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Rules > Set Bookmark: prompt for name + value; insert «Set BookmarkName "Value"».
    private sealed class InsertMergeRuleSetCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var result = MergeRuleAskSetDialog.AskSet(Window.GetWindow(editor));
            if (result is null) return;
            var instruction = MergeRuleEvaluator.BuildSetInstruction(result.Value.Name, result.Value.Value);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Rules > Ref Bookmark: prompt for bookmark name; insert «Ref BookmarkName».
    private sealed class InsertMergeRuleRefCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var name = MergeRulePromptDialog.AskPrompt(Window.GetWindow(editor), "Ref Bookmark",
                "Enter the bookmark name to reference:");
            if (name is null) return;
            var instruction = MergeRuleEvaluator.BuildRefInstruction(name);
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}");
        }
    }

    // ── Merge Rule dialogs ───────────────────────────────────────────────────────────────────────

    // If…Then…Else dialog: builds the complete rule definition.
    private static class MergeRuleIfDialog
    {
        public static MailMergeRuleIfDialogResult? Ask(Window? owner, IReadOnlyList<string> header)
        {
            MailMergeRuleIfDialogResult? result = null;
            var dialog = new Window
            {
                Title = "If…Then…Else",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var fieldCombo = new System.Windows.Controls.ComboBox { MinWidth = 140 };
            foreach (var h in header) fieldCombo.Items.Add(h);
            if (fieldCombo.Items.Count > 0) fieldCombo.SelectedIndex = 0;

            var opCombo = new System.Windows.Controls.ComboBox { MinWidth = 200 };
            foreach (var choice in MailMergeRuleDialogPlanner.GetConditionOperators()) opCombo.Items.Add(choice.Label);
            opCombo.SelectedIndex = 0;

            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 140 };
            var trueBox  = new System.Windows.Controls.TextBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 6) };
            var falseBox = new System.Windows.Controls.TextBox { MinWidth = 260 };

            // Disable value field for blank/not blank operators.
            opCombo.SelectionChanged += (_, _) =>
            {
                var op = MailMergeRuleDialogPlanner.GetConditionOperator(opCombo.SelectedIndex);
                valueBox.IsEnabled = MailMergeRuleDialogPlanner.IsComparisonValueEnabled(op);
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = MailMergeRuleDialogPlanner.CreateIfResult(
                    fieldCombo.SelectedItem?.ToString() ?? fieldCombo.Text,
                    opCombo.SelectedIndex,
                    valueBox.Text,
                    trueBox.Text,
                    falseBox.Text);
                dialog.DialogResult = true;
            };

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void AddRow(int row, string label, System.Windows.UIElement control)
            {
                var lbl = new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
                Grid.SetRow(control, row); Grid.SetColumn(control, 1);
                grid.Children.Add(lbl);
                grid.Children.Add(control);
            }

            AddRow(0, "Field name:", fieldCombo);
            AddRow(1, "Comparison:", opCombo);
            AddRow(2, "Compare to:", valueBox);
            AddRow(3, "Insert this text (true):", trueBox);
            AddRow(4, "Otherwise insert (false):", falseBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 6); Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);

            dialog.Content = grid;
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Skip Record If / Next Record If dialog.
    private static class MergeRuleCondDialog
    {
        public static MailMergeRuleConditionDialogResult? Ask(Window? owner, IReadOnlyList<string> header, string title)
        {
            MailMergeRuleConditionDialogResult? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var fieldCombo = new System.Windows.Controls.ComboBox { MinWidth = 140 };
            foreach (var h in header) fieldCombo.Items.Add(h);
            if (fieldCombo.Items.Count > 0) fieldCombo.SelectedIndex = 0;

            var opCombo = new System.Windows.Controls.ComboBox { MinWidth = 200 };
            foreach (var choice in MailMergeRuleDialogPlanner.GetConditionOperators()) opCombo.Items.Add(choice.Label);
            opCombo.SelectedIndex = 0;

            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 140 };
            opCombo.SelectionChanged += (_, _) =>
            {
                var op = MailMergeRuleDialogPlanner.GetConditionOperator(opCombo.SelectedIndex);
                valueBox.IsEnabled = MailMergeRuleDialogPlanner.IsComparisonValueEnabled(op);
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = MailMergeRuleDialogPlanner.CreateConditionResult(
                    fieldCombo.SelectedItem?.ToString() ?? fieldCombo.Text,
                    opCombo.SelectedIndex,
                    valueBox.Text);
                dialog.DialogResult = true;
            };

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void AddRow(int row, string label, System.Windows.UIElement control)
            {
                var lbl = new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
                Grid.SetRow(control, row); Grid.SetColumn(control, 1);
                grid.Children.Add(lbl);
                grid.Children.Add(control);
            }

            AddRow(0, "Field name:", fieldCombo);
            AddRow(1, "Comparison:", opCombo);
            AddRow(2, "Compare to:", valueBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 4); Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);

            dialog.Content = grid;
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Simple single-prompt dialog (for Fill-in prompt text and Ref bookmark name).
    private static class MergeRulePromptDialog
    {
        public static string? AskPrompt(Window? owner, string title, string label)
        {
            string? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var box = new System.Windows.Controls.TextBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 12) };
            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(14), MinWidth = 320 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Two-field dialog for Ask (bookmark name + prompt) and Set (bookmark name + value).
    private static class MergeRuleAskSetDialog
    {
        public static (string Name, string Value)? AskAsk(Window? owner) =>
            AskTwo(owner, "Ask", "Bookmark name:", "Prompt text:");

        public static (string Name, string Value)? AskSet(Window? owner) =>
            AskTwo(owner, "Set Bookmark", "Bookmark name:", "Value:");

        private static (string Name, string Value)? AskTwo(Window? owner, string title, string label1, string label2)
        {
            (string, string)? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var nameBox  = new System.Windows.Controls.TextBox { MinWidth = 200, Margin = new Thickness(0, 0, 0, 6) };
            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 200, Margin = new Thickness(0, 0, 0, 10) };
            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = (nameBox.Text, valueBox.Text); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(14), MinWidth = 320 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label1, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(nameBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label2, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(valueBox);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            nameBox.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Select Recipients: open a dialog to paste/type CSV (first line = headers). The parsed MergeData
    // is stored on the session. If the document already has merge fields, they are shown as a hint so the
    // user knows which columns to provide.
    private sealed class SetMergeDataCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var fields = MailMerge.FieldNames(editor.Model);
            var seed = session.Data is { } data ? DescribeAsCsv(data) : SeedFromFields(fields);

            var csv = MergeDataDialog.Ask(Window.GetWindow(editor), fields, seed);
            if (csv is null)
                return; // cancelled

            var parsed = MergeData.FromCsv(csv);
            session.Data = parsed;
            session.Template = null; // any in-progress preview is invalidated by new data
            session.CurrentIndex = 0;
            // Auto-seed the field mapping from the new header so Address Block / Greeting Line
            // immediately compose correctly without requiring the user to open Match Fields.
            session.Mapping = MailMerge.AutoMatchFields(parsed.Header);

            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                $"Loaded {parsed.Count} record(s) with {parsed.Header.Count} field(s).",
                "Mail Merge");
            editor.Focus();
        }

        // Suggest a header line from the document's discovered fields so the user can fill rows in.
        private static string SeedFromFields(IReadOnlyList<string> fields) =>
            fields.Count == 0 ? string.Empty : string.Join(",", fields);

        // Render the current data back to CSV so re-opening the dialog shows what was entered.
        private static string DescribeAsCsv(MergeData data)
        {
            var lines = new List<string> { string.Join(",", data.Header.Select(CsvCell)) };
            foreach (var row in data.Rows)
                lines.Add(string.Join(",", data.Header.Select(h => CsvCell(row.TryGetValue(h, out var v) ? v : string.Empty))));
            return string.Join(Environment.NewLine, lines);
        }

        private static string CsvCell(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
    }

    // Mailings > Preview Results: load MergeRecord(template, currentRow) into the editor so the user sees
    // a real record. The original (template) document is stashed on first preview so stepping to the next
    // record re-renders from the template, and leaving the preview restores it. With no data, prompts the
    // user to Select Recipients first.
    private sealed class PreviewMergeRecordCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!EnsurePreviewing(editor, session, out var data, out var template))
            {
                return;
            }

            var index = Math.Clamp(session.CurrentIndex, 0, data.Count - 1);
            session.CurrentIndex = index;
            editor.LoadModel(MailMerge.MergeRecord(template, session.AugmentRow(data.Rows[index])));

            var action = PreviewNavigationDialog.Ask(Window.GetWindow(editor), index, data.Count);
            switch (action.Kind)
            {
                case PreviewAction.Move:
                    index = Math.Clamp(action.TargetIndex, 0, data.Count - 1);
                    session.CurrentIndex = index;
                    editor.LoadModel(MailMerge.MergeRecord(template, session.AugmentRow(data.Rows[index])));
                    break;
                case PreviewAction.Done:
                    // Restore the editable template so the user can keep editing fields.
                    editor.LoadModel(template);
                    session.Template = null;
                    break;
                case PreviewAction.Cancel:
                    // Leave whatever is currently shown; do not change the session.
                    break;
            }

            editor.Focus();
        }
    }

    private sealed class NavigateMergePreviewCommand(
        DocumentView editor,
        MailMergeSession session,
        MailMergePreviewNavigationAction action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!EnsurePreviewing(editor, session, out var data, out var template))
                return;

            var index = MailMergePreviewNavigationPlanner.TargetIndex(action, session.CurrentIndex, data.Count);
            session.CurrentIndex = index;
            editor.LoadModel(MailMerge.MergeRecord(template, session.AugmentRow(data.Rows[index])));
            editor.Focus();
        }
    }

    internal sealed class FindMergeRecipientCommand(
        DocumentView editor,
        MailMergeSession session,
        Func<Window?, string?>? ask = null,
        Action<Window?, string>? showInfo = null) : IRibbonCommand
    {
        private readonly Func<Window?, string?> _ask = ask ??
            (owner => TextPrompt.Ask(owner, "Find Recipient", "Find:", string.Empty));
        private readonly Action<Window?, string> _showInfo = showInfo ??
            ((owner, message) => DialogMessageHelper.ShowInfo(owner, message, "Mail Merge"));

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            if (session.Data is not { Count: > 0 } data)
            {
                _showInfo(owner, "Select recipients first (Mailings > Select Recipients), then find a recipient.");
                return;
            }

            var query = _ask(owner);
            if (query is null)
                return;

            var result = MailMergeFindRecipientPlanner.Find(data, query, session.CurrentIndex);
            session.CurrentIndex = result.Index;
            _showInfo(owner, result.Message);
            editor.Focus();
        }
    }

    internal sealed class CheckMergeErrorsCommand(
        DocumentView editor,
        MailMergeSession session,
        Func<Window?, MailMergeCheckForErrorsMode?>? ask = null,
        Action<Window?, string>? showInfo = null,
        Action<RibbonCommandContext>? completeMerge = null,
        Action<TextDocument>? openReportDocument = null) : IRibbonCommand
    {
        private readonly Func<Window?, MailMergeCheckForErrorsMode?> _ask = ask ?? MailMergeCheckForErrorsDialog.Ask;
        private readonly Action<Window?, string> _showInfo = showInfo ??
            ((owner, message) => DialogMessageHelper.ShowInfo(owner, message, "Mail Merge"));
        private readonly Action<RibbonCommandContext> _completeMerge = completeMerge ??
            (context => new FinishMergeCommand(
                editor,
                session,
                ask: (_, recordCount, _) => MailMergeFinishPlanner.PlanNewDocumentAllRecords(recordCount))
                .Execute(context));

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            if (session.Data is not { Count: > 0 })
            {
                _showInfo(owner, "Select recipients first (Mailings > Select Recipients), then check for errors.");
                return;
            }

            if (_ask(owner) is not { } selected)
                return;

            if (!session.IsPreviewing)
                editor.CommitToModel();
            var template = session.IsPreviewing ? session.Template! : editor.Model;
            var rows = session.Data.Rows.Select(row => session.AugmentRow(row)).ToList();
            var result = MailMergeCheckForErrorsPlanner.Check(template, rows, selected);
            if (result.ShouldPauseForErrors)
            {
                foreach (var issue in result.Issues)
                    _showInfo(owner, issue.Message);
            }
            else if (!result.ShouldOpenReportDocument || openReportDocument is null)
            {
                _showInfo(owner, result.Message);
            }

            if (result.ShouldCompleteMerge)
                _completeMerge(context);

            if (result.ShouldOpenReportDocument && openReportDocument is not null)
                openReportDocument(MailMergeCheckForErrorsPlanner.BuildReportDocument(result));
            editor.Focus();
        }
    }

    private static class MailMergeCheckForErrorsDialog
    {
        public static MailMergeCheckForErrorsMode? Ask(Window? owner)
        {
            var choices = MailMergeCheckForErrorsPlanner.GetChoices();
            var combo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 420,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                combo.Items.Add(choice.Label);

            MailMergeCheckForErrorsMode? result = null;
            var dialog = new Window
            {
                Title = "Check for Errors",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                IsCancel = true,
                MinWidth = 72
            };
            ok.Click += (_, _) =>
            {
                result = MailMergeCheckForErrorsPlanner.GetMode(combo.SelectedIndex);
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "How should errors be checked?",
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(combo);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            combo.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    private static bool EnsurePreviewing(
        DocumentView editor,
        MailMergeSession session,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MergeData? data,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TextDocument? template)
    {
        data = session.Data;
        template = session.Template;
        if (data is not { Count: > 0 })
        {
            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                "Select recipients first (Mailings > Select Recipients), then preview a record.",
                "Mail Merge");
            return false;
        }

        // On first preview, capture the editable template and immediately show record 0; subsequent
        // previews reuse the template and resume at the last viewed record.
        if (!session.IsPreviewing)
        {
            editor.CommitToModel();
            session.Template = editor.Model;
            session.CurrentIndex = 0;
        }

        template = session.Template!;
        return true;
    }

    // Mailings > Finish & Merge: produce the merged documents and load the concatenation of every record
    // into the editor as a single document (records separated by a page break), so the result is visible
    // and saveable. This replaces the editor's content; the template is no longer needed afterwards.
    // When the template contains Fill-in or Ask rule instructions, the user is prompted once per unique
    // Fill-in prompt / Ask bookmark before iterating records; all answers are stored in MergeState so
    // the evaluator can resolve them without further prompts.
    internal sealed class FinishMergeCommand(
        DocumentView editor,
        MailMergeSession session,
        Action<TextDocument>? printDocument = null,
        Func<Window?, int, int, MailMergeFinishPlan?>? ask = null,
        Action<Window?, string>? showInfo = null) : IRibbonCommand
    {
        private readonly Func<Window?, int, int, MailMergeFinishPlan?> _ask = ask ?? MailMergeFinishDialog.Ask;
        private readonly Action<Window?, string> _showInfo = showInfo ??
            ((owner, message) => DialogMessageHelper.ShowInfo(owner, message, "Mail Merge"));

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            if (session.Data is not { Count: > 0 } data)
            {
                _showInfo(owner, "Select recipients first (Mailings > Select Recipients), then Finish & Merge.");
                return;
            }

            var finishPlan = _ask(owner, data.Count, session.CurrentIndex);
            if (finishPlan is not { Success: true })
                return;
            if (finishPlan.Destination == MailMergeFinishDestination.Printer && printDocument is null)
            {
                _showInfo(owner, "Printing is not available in this window.");
                return;
            }

            // Use the stashed template if previewing; otherwise the current editor content is the template.
            TextDocument template;
            if (session.IsPreviewing)
            {
                template = session.Template!;
            }
            else
            {
                editor.CommitToModel();
                template = editor.Model;
            }

            // Collect Fill-in and Ask prompts from the template body so we can ask the user once
            // before the merge run starts (matching Word's behaviour).
            var mergeState = new MergeState();
            CollectFillInAndAskAnswers(template, mergeState, owner);

            // Augment every row with the composed «AddressBlock» and «GreetingLine» values so composite
            // placeholders in the template resolve correctly across every record.
            var augmentedRows = finishPlan.RowIndexes.Select(index => session.AugmentRow(data.Rows[index])).ToList();
            var augmentedData = new MergeData(data.Header,
                augmentedRows.Select(r =>
                    (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList())
                .ToList());

            // Use the rules-aware merge path. Records flagged by «Skip Record If» are excluded.
            var merged = MailMerge.MergeAllWithRules(template, augmentedData, mergeState);
            var skipped = mergeState.SkippedIndices.Count;
            var combined = MailMerge.CombineMergedRecords(merged, session.Mode);

            if (finishPlan.Destination == MailMergeFinishDestination.Printer)
            {
                printDocument!(combined);
                editor.Focus();
                return;
            }

            editor.LoadModel(combined);
            session.Template = null;
            session.CurrentIndex = 0;

            var msg = skipped > 0
                ? $"Merged {merged.Count} record(s) into a single document ({skipped} skipped)."
                : $"Merged {merged.Count} record(s) into a single document.";
            _showInfo(owner, msg);
            editor.Focus();
        }

        // Scan the template for «Fill-in "Prompt"» and «Ask BookmarkName "Prompt"» instructions and
        // prompt the user once per unique prompt/bookmark before the merge run.
        private static void CollectFillInAndAskAnswers(TextDocument template, MergeState state, Window? owner)
        {
            var allText = string.Join(" ", template.Blocks.OfType<FreeW.Core.Model.Paragraph>()
                .SelectMany(p => p.Runs)
                .Select(r => r.Text));

            // Extract field instructions.
            var i = 0;
            while (i < allText.Length)
            {
                var open = allText.IndexOf(MailMerge.FieldOpen, i);
                if (open < 0) break;
                var close = allText.IndexOf(MailMerge.FieldClose, open + 1);
                if (close < 0) break;
                var instruction = allText.Substring(open + 1, close - open - 1).Trim();
                i = close + 1;

                const string fillInPrefix = "Fill-in ";
                const string askPrefix = "Ask ";

                if (instruction.StartsWith(fillInPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var promptRaw = instruction.Substring(fillInPrefix.Length).Trim();
                    var prompt = promptRaw.Length >= 2 && promptRaw[0] == '"'
                        ? promptRaw.Substring(1, promptRaw.Length - 2).Replace("\"\"", "\"")
                        : promptRaw;
                    if (!state.FillInAnswers.ContainsKey(prompt))
                    {
                        var answer = MergeRulePromptDialog.AskPrompt(owner, "Fill-in", $"{prompt}");
                        state.FillInAnswers[prompt] = answer ?? string.Empty;
                    }
                }
                else if (instruction.StartsWith(askPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = instruction.Substring(askPrefix.Length).TrimStart();
                    var spaceIdx = rest.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        var bmName = rest.Substring(0, spaceIdx);
                        if (!state.AskAnswers.ContainsKey(bmName))
                        {
                            var promptRaw = rest.Substring(spaceIdx + 1).Trim();
                            var prompt = promptRaw.Length >= 2 && promptRaw[0] == '"'
                                ? promptRaw.Substring(1, promptRaw.Length - 2).Replace("\"\"", "\"")
                                : promptRaw;
                            var answer = MergeRulePromptDialog.AskPrompt(owner, "Ask", $"{prompt}");
                            state.AskAnswers[bmName] = answer ?? string.Empty;
                        }
                    }
                }
            }
        }
    }

    private static class MailMergeFinishDialog
    {
        public static MailMergeFinishPlan? Ask(Window? owner, int recordCount, int currentIndex)
        {
            var dialogPlan = MailMergeFinishPlanner.CreateDialogPlan(recordCount, currentIndex);
            MailMergeFinishPlan? result = null;
            var dialog = new Window
            {
                Title = "Merge",
                Owner = owner,
                Width = 440,
                Height = 320,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            var destination = new System.Windows.Controls.ComboBox
            {
                Margin = new Thickness(0, 4, 0, 12)
            };
            foreach (var choice in dialogPlan.Destinations)
            {
                destination.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = choice.IsSupported ? choice.Label : $"{choice.Label} (not available)",
                    Tag = choice
                });
            }
            destination.SelectedIndex = dialogPlan.DestinationIndex;

            var scope = new System.Windows.Controls.ComboBox
            {
                Margin = new Thickness(0, 4, 0, 12)
            };
            foreach (var choice in dialogPlan.Scopes)
            {
                scope.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = choice.Label,
                    Tag = choice
                });
            }
            scope.SelectedIndex = dialogPlan.ScopeIndex;

            var from = new System.Windows.Controls.TextBox
            {
                Text = dialogPlan.FromRecordText,
                Width = 72,
                Margin = new Thickness(8, 0, 16, 0)
            };
            var to = new System.Windows.Controls.TextBox
            {
                Text = dialogPlan.ToRecordText,
                Width = 72,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var range = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            range.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "From",
                VerticalAlignment = VerticalAlignment.Center
            });
            range.Children.Add(from);
            range.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "To",
                VerticalAlignment = VerticalAlignment.Center
            });
            range.Children.Add(to);

            var ok = new System.Windows.Controls.Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                IsCancel = true,
                MinWidth = 72
            };

            MailMergeFinishPlan CurrentPlan()
            {
                var destinationChoice = (MailMergeFinishDestinationChoice)
                    ((System.Windows.Controls.ComboBoxItem)destination.SelectedItem).Tag;
                var scopeChoice = (MailMergeFinishScopeChoice)
                    ((System.Windows.Controls.ComboBoxItem)scope.SelectedItem).Tag;
                return MailMergeFinishPlanner.Plan(
                    destinationChoice.Destination,
                    scopeChoice.Scope,
                    recordCount,
                    currentIndex,
                    from.Text,
                    to.Text);
            }

            void Refresh()
            {
                var scopeChoice = (MailMergeFinishScopeChoice)
                    ((System.Windows.Controls.ComboBoxItem)scope.SelectedItem).Tag;
                range.IsEnabled = scopeChoice.Scope == MailMergeRecipientScope.FromTo;
                ok.IsEnabled = CurrentPlan().Success;
            }

            destination.SelectionChanged += (_, _) => Refresh();
            scope.SelectionChanged += (_, _) => Refresh();
            from.TextChanged += (_, _) => Refresh();
            to.TextChanged += (_, _) => Refresh();
            ok.Click += (_, _) =>
            {
                var plan = CurrentPlan();
                if (!plan.Success)
                    return;
                result = plan;
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Merge to" });
            panel.Children.Add(destination);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Records to merge" });
            panel.Children.Add(scope);
            panel.Children.Add(range);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            Refresh();
            destination.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Send E-mail Messages: gather Word-style e-mail merge delivery intent and show the
    // validated plan. This never sends mail and does not require Outlook/cloud integration.
    private sealed class EmailMergeCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { Count: > 0 } data)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then Send E-mail Messages.",
                    "Mail Merge");
                return;
            }

            var owner = Window.GetWindow(editor);
            var intent = EmailMergeDialog.Ask(owner, data, session.CurrentIndex, []);
            if (intent is null)
                return;

            var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);
            DialogMessageHelper.ShowInfo(owner, MailMergeEmailDeliveryPlanner.FormatStatus(plan), "Mail Merge");
            editor.Focus();
        }
    }

    private static class EmailMergeDialog
    {
        public static MailMergeEmailDeliveryIntent? Ask(
            Window? owner,
            MergeData data,
            int currentRecordIndex,
            IReadOnlyList<int> selectedRecordIndexes)
        {
            var dialogPlan = MailMergeEmailDeliveryPlanner.CreateDialogPlan(data, currentRecordIndex, selectedRecordIndexes);
            MailMergeEmailDeliveryIntent? result = null;
            var dialog = new Window
            {
                Title = "Send E-mail Messages",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var toCombo = new System.Windows.Controls.ComboBox { MinWidth = 220 };
            foreach (var field in dialogPlan.RecipientAddressFields)
                toCombo.Items.Add(field);
            toCombo.SelectedItem = dialogPlan.RecipientAddressField;
            if (toCombo.SelectedIndex < 0 && toCombo.Items.Count > 0)
                toCombo.SelectedIndex = 0;

            var subjectBox = new System.Windows.Controls.TextBox { MinWidth = 220, Text = dialogPlan.Subject };
            var outputCombo = CreateChoiceCombo(dialogPlan.OutputFormats.Select(choice => choice.Label), dialogPlan.OutputFormatIndex);
            var bodyCombo = CreateChoiceCombo(dialogPlan.BodyFormats.Select(choice => choice.Label), dialogPlan.BodyFormatIndex);
            var scopeCombo = CreateChoiceCombo(dialogPlan.RecordScopes.Select(choice => choice.Label), dialogPlan.RecordScopeIndex);
            var validation = new System.Windows.Controls.TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 2, 0, 8)
            };

            var ok = new System.Windows.Controls.Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            MailMergeEmailDeliveryIntent CurrentIntent() =>
                MailMergeEmailDeliveryPlanner.CreateIntent(
                    toCombo.SelectedItem?.ToString() ?? dialogPlan.RecipientAddressField,
                    subjectBox.Text,
                    outputCombo.SelectedIndex,
                    bodyCombo.SelectedIndex,
                    scopeCombo.SelectedIndex,
                    currentRecordIndex,
                    selectedRecordIndexes);

            void RefreshValidation()
            {
                var plan = MailMerge.CreateEmailDeliveryPlan(data, CurrentIntent());
                var messages = MailMergeEmailDeliveryPlanner.GetValidationMessages(plan);
                validation.Text = messages.Count == 0
                    ? "Ready to prepare an e-mail merge plan. No messages will be sent."
                    : string.Join(Environment.NewLine, messages);
                ok.IsEnabled = plan.Errors.Count == 0;
            }

            toCombo.SelectionChanged += (_, _) => RefreshValidation();
            subjectBox.TextChanged += (_, _) => RefreshValidation();
            outputCombo.SelectionChanged += (_, _) => RefreshValidation();
            bodyCombo.SelectionChanged += (_, _) => RefreshValidation();
            scopeCombo.SelectionChanged += (_, _) => RefreshValidation();

            ok.Click += (_, _) =>
            {
                result = CurrentIntent();
                dialog.DialogResult = true;
            };

            var grid = new Grid { Margin = new Thickness(14), MinWidth = 360 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddRow(grid, 0, "To field:", toCombo);
            AddRow(grid, 1, "Subject:", subjectBox);
            AddRow(grid, 2, "Output:", outputCombo);
            AddRow(grid, 3, "Body format:", bodyCombo);
            AddRow(grid, 4, "Send records:", scopeCombo);
            AddRow(grid, 5, "Validation:", validation);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 6);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);

            dialog.Content = grid;
            RefreshValidation();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.ComboBox CreateChoiceCombo(IEnumerable<string> labels, int selectedIndex)
        {
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 220 };
            foreach (var label in labels)
                combo.Items.Add(label);
            combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, combo.Items.Count - 1);
            return combo;
        }

        private static void AddRow(Grid grid, int row, string label, UIElement control)
        {
            var text = new System.Windows.Controls.TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 8, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (control is FrameworkElement element)
                element.Margin = new Thickness(0, 0, 0, 8);

            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
        }
    }

    // Mailings > Filter & Sort Recipients: present the active session's MergeData as a list of rows with
    // per-row inclusion checkboxes plus a sort-column / direction picker, then rebuild session.Data from
    // the filtered, ordered subset. No model-layer change — MergeData accepts any enumerable of rows, so
    // the transformation is pure and zero-cost. No-ops when there is no active session or data source.
    private sealed class FilterSortRecipientsCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { Count: > 0 } data)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then filter and sort.",
                    "Mail Merge");
                return;
            }

            var updatedData = FilterSortRecipientsDialog.Ask(Window.GetWindow(editor), data);
            if (updatedData is null)
                return; // cancelled

            session.Data = updatedData;
            // Invalidate any in-progress preview so it re-reads the new filtered data.
            session.Template = null;
            session.CurrentIndex = 0;

            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                $"Recipient list updated: {session.Data.Count} record(s) after filtering/sorting.",
                "Mail Merge");
            editor.Focus();
        }
    }

    // Mailings > Envelopes: apply standard envelope geometry to the page via ApplyPageSettings (the same
    // backed path used by orientation/size/column commands). Offers a small set of ISO/US envelope sizes.
    // Optionally seeds the first paragraph with the first merge field if a session is active.
    private sealed class EnvelopesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (EnvelopeSetupDialog.Ask(Window.GetWindow(editor)) is not { } envelope)
                return; // cancelled

            editor.ApplyPageSettings(page =>
            {
                // Envelope sizes are stored portrait (narrow × long); Landscape swaps the rendering axes
                // so the long dimension runs horizontally for printing, matching Word's envelope setup.
                page.WidthPt   = envelope.WidthPt;
                page.HeightPt  = envelope.HeightPt;
                page.Landscape = envelope.Landscape;
                // Narrow margins leave the maximum print area for the address block.
                page.MarginLeftPt   = envelope.MarginPt;
                page.MarginRightPt  = envelope.MarginPt;
                page.MarginTopPt    = envelope.MarginPt;
                page.MarginBottomPt = envelope.MarginPt;
            });

            editor.Focus();
        }
    }

    // Mailings > Labels: set the page to a label-sheet geometry via ApplyPageSettings, then insert a
    // table grid (rows × columns) via editor.InsertTable so each cell is one label.
    //
    // When a merge session with data is active the command also populates each grid cell with the
    // per-record merged content (using MailMerge.MergeRecord on the current editor body as template),
    // advancing one record per cell, left-to-right, top-to-bottom across the sheet.  Each cell-write
    // goes through SetTableCellContent which routes through the undo/redo bus — the whole operation is
    // reversible in one Ctrl+Z because InsertTable and SetTableCellContent share the same bus.  When
    // there are no data records (or no session) the grid is inserted blank, as before.
    private sealed class LabelsCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (LabelSetupDialog.Ask(Window.GetWindow(editor)) is not { } label)
                return; // cancelled

            ApplyLabelSheet(editor, session, label);
        }

        internal static IReadOnlyList<IReadOnlyList<FreeW.Core.Model.Paragraph>> BuildLabelCellContents(
            DocumentView editor,
            MailMergeSession session,
            int capacity)
        {
            if (session.Data is not { Count: > 0 } data)
                return [];

            var template = session.IsPreviewing ? session.Template! : editor.Model;
            var state = new MergeState();
            var contents = new List<IReadOnlyList<FreeW.Core.Model.Paragraph>>(
                Math.Min(capacity, data.Count));
            var recordIndex = 0;

            while (contents.Count < capacity && recordIndex < data.Count)
            {
                state.SequenceNumber++;
                var row = session.AugmentRow(data.Rows[recordIndex]);
                var merged = MailMerge.MergeRecordWithRules(template, row, state, recordIndex + 1);
                if (state.SkipRecordRequested)
                {
                    state.SequenceNumber--;
                    recordIndex++;
                    continue;
                }

                contents.Add(merged.Blocks.OfType<FreeW.Core.Model.Paragraph>().ToList());
                recordIndex += state.AdvanceRecordRequested ? 2 : 1;
            }

            return contents;
        }

    }

    internal static void ApplyLabelSheet(
        DocumentView editor,
        MailMergeSession session,
        LabelSetupResult label)
    {
        editor.CommitToModel();
        var rows = Math.Max(1, label.Rows);
        var columns = Math.Max(1, label.Columns);
        var cellContents = LabelsCommand.BuildLabelCellContents(editor, session, rows * columns);

        editor.ApplyPageSettings(page =>
        {
            page.WidthPt = label.PageWidthPt;
            page.HeightPt = label.PageHeightPt;
            page.Landscape = label.Landscape;
            page.MarginLeftPt = label.MarginPt;
            page.MarginRightPt = label.MarginPt;
            page.MarginTopPt = label.MarginPt;
            page.MarginBottomPt = label.MarginPt;
        });

        var blockIndex = editor.InsertTable(rows, columns);
        for (var index = 0; index < cellContents.Count; index++)
        {
            editor.SetTableCellContent(
                blockIndex,
                index / columns,
                index % columns,
                cellContents[index]);
        }

        editor.Focus();
    }

    // The user's choice from the preview navigation dialog.
    private enum PreviewAction { Move, Done, Cancel }

    private readonly record struct PreviewChoice(PreviewAction Kind, int TargetIndex);

    // A small modeless-feeling modal that shows the current record and offers Previous / Next / Done.
    // Returns a Move (to a new index), Done (end preview, restore template), or Cancel (no change).
    private static class PreviewNavigationDialog
    {
        public static PreviewChoice Ask(Window? owner, int index, int count)
        {
            var result = new PreviewChoice(PreviewAction.Cancel, index);
            var dialog = new Window
            {
                Title = "Preview Results",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"Record {index + 1} of {count}",
                Margin = new Thickness(0, 0, 0, 12)
            };

            var prev = new System.Windows.Controls.Button { Content = "◀ Previous", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = index > 0 };
            var next = new System.Windows.Controls.Button { Content = "Next ▶", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = index < count - 1 };
            var done = new System.Windows.Controls.Button { Content = "Done", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            prev.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Move, index - 1); dialog.DialogResult = true; };
            next.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Move, index + 1); dialog.DialogResult = true; };
            done.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Done, index); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(prev);
            buttons.Children.Add(next);
            buttons.Children.Add(done);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(label);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            if (dialog.ShowDialog() == true)
                return result;
            return new PreviewChoice(PreviewAction.Cancel, index);
        }
    }

    // A dialog to enter the mail-merge data as CSV (first line = headers). Shows the document's discovered
    // merge fields as a hint. Returns the CSV text, or null if cancelled.
    private static class MergeDataDialog
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> fields, string seed)
        {
            var hint = fields.Count > 0
                ? "Fields in this document: " + string.Join(", ", fields)
                : "Tip: the first line is the header row of field names.";

            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                AcceptsReturn = true,
                AcceptsTab = false,
                MinWidth = 420,
                MinHeight = 160,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 12)
            };

            string? result = null;
            var dialog = new Window
            {
                Title = "Mail Merge Data",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Paste or type CSV (first line = field names):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = hint, Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Match Fields dialog. Shows each semantic role with a ComboBox of available columns (plus
    // "(not matched)"). Pre-selects the auto-matched column when one was found. Returns an updated
    // FieldMapping on OK, or null on cancel. The dialog is non-resizable and modal; it follows the same
    // Window-building idiom as MergeDataDialog / FilterSortRecipientsDialog.
    private static class MatchFieldsDialog
    {
        public static FieldMapping? Ask(Window? owner, IReadOnlyList<string> header, FieldMapping current)
        {
            FieldMapping? result = null;

            var rolePlans = MailMergeMatchFieldsDialogPlanner.GetRolePlans(header, current);
            var columnChoices = MailMergeMatchFieldsDialogPlanner.GetColumnChoices(header);

            // One ComboBox per role, keyed by role.
            var combos = new Dictionary<FieldRole, System.Windows.Controls.ComboBox>();

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var i = 0; i < rolePlans.Count + 1; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < rolePlans.Count; i++)
            {
                var plan = rolePlans[i];
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = plan.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 3, 12, 3)
                };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var combo = new System.Windows.Controls.ComboBox { MinWidth = 180, Margin = new Thickness(0, 3, 0, 3) };
                foreach (var choice in columnChoices)
                    combo.Items.Add(choice);
                combo.SelectedItem = plan.SelectedChoice;

                combos[plan.Role] = combo;
                Grid.SetRow(combo, i);
                Grid.SetColumn(combo, 1);
                grid.Children.Add(combo);
            }

            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            var buttonRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttonRow.Children.Add(ok);
            buttonRow.Children.Add(cancel);
            Grid.SetRow(buttonRow, rolePlans.Count);
            Grid.SetColumnSpan(buttonRow, 2);
            grid.Children.Add(buttonRow);

            var dialog = new Window
            {
                Title = "Match Fields",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            ok.Click += (_, _) =>
            {
                result = MailMergeMatchFieldsDialogPlanner.CreateResult(
                    combos.ToDictionary(pair => pair.Key, pair => pair.Value.SelectedItem as string));
                dialog.DialogResult = true;
            };

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                MaxHeight = 520
            };
            dialog.Content = scroll;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Filter & Sort Recipients dialog. Presents each recipient row with a checkbox (include/
    // exclude), a sort-column combo and a sort-direction radio. Returns the chosen subset in the chosen
    // order, or null if cancelled. Structural template: MergeDataDialog (same Window-building idiom).
    private static class FilterSortRecipientsDialog
    {
        public static MergeData? Ask(
            Window? owner, MergeData data)
        {
            MergeData? result = null;

            var dialog = new Window
            {
                Title = "Filter and Sort Recipients",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false,
                MinWidth = 480
            };

            // --- Sort controls ---
            var sortColCombo = new System.Windows.Controls.ComboBox { MinWidth = 160, Margin = new Thickness(4, 0, 8, 0) };
            foreach (var h in data.Header)
                sortColCombo.Items.Add(h);
            if (data.Header.Count > 0)
                sortColCombo.SelectedIndex = 0;

            var ascRadio  = new System.Windows.Controls.RadioButton { Content = "Ascending",  IsChecked = true, Margin = new Thickness(0, 0, 8, 0) };
            var descRadio = new System.Windows.Controls.RadioButton { Content = "Descending", Margin = new Thickness(0, 0, 0, 0) };

            var sortPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sortPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Sort by:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            sortPanel.Children.Add(sortColCombo);
            sortPanel.Children.Add(ascRadio);
            sortPanel.Children.Add(descRadio);

            // --- Row list with checkboxes ---
            var previewCols = MailMergeRecipientFilterSortPlanner.GetPreviewColumns(data.Header);

            var rowChecks = new List<System.Windows.Controls.CheckBox>();
            var rowList = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Header hint
            var headerHint = new System.Windows.Controls.TextBlock
            {
                Text = MailMergeRecipientFilterSortPlanner.FormatPreviewHeader(previewCols),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = Brushes.Gray
            };
            rowList.Children.Add(headerHint);

            for (var i = 0; i < data.Rows.Count; i++)
            {
                var row = data.Rows[i];
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = MailMergeRecipientFilterSortPlanner.FormatPreviewRow(i, row, previewCols),
                    IsChecked = true,
                    Margin = new Thickness(0, 1, 0, 1),
                    Tag = i  // row index
                };
                rowChecks.Add(cb);
                rowList.Children.Add(cb);
            }

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                Content = rowList,
                MaxHeight = 260,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // --- OK / Cancel ---
            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true,  MinWidth = 72 };

            ok.Click += (_, _) =>
            {
                var sortCol  = sortColCombo.SelectedItem as string ?? string.Empty;
                var ascending = ascRadio.IsChecked == true;

                var includedIndexes = rowChecks
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => (int)cb.Tag!)
                    .ToList();

                result = MailMergeRecipientFilterSortPlanner.Apply(data, includedIndexes, sortCol, ascending);
                dialog.DialogResult = true;
            };

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Check recipients to include, then choose a sort order:", Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(sortPanel);
            panel.Children.Add(scroll);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Envelopes setup dialog. Offers a small set of standard ISO/US sizes (DL, C5, C6,
    // Comm-10, Monarch) matching Word's Envelopes and Labels dialog. Returns the chosen geometry, or null
    // if cancelled. The caller applies the settings via ApplyPageSettings (backed path).
    private static class EnvelopeSetupDialog
    {
        public static EnvelopeSetupResult? Ask(Window? owner)
        {
            EnvelopeSetupResult? result = null;

            var sizes = MailingsEnvelopeLabelPlanner.GetEnvelopeSizes();
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var s in sizes)
                combo.Items.Add(s.Name);
            combo.SelectedIndex = MailingsEnvelopeLabelPlanner.DefaultEnvelopeIndex;

            var dialog = new Window
            {
                Title = "Envelopes",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true,  MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = MailingsEnvelopeLabelPlanner.PlanEnvelope(combo.SelectedIndex);
                dialog.DialogResult = true;
            };

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);

            var note = new System.Windows.Controls.TextBlock
            {
                Text = "Page orientation is set to Landscape. Narrow margins are applied automatically.",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Envelope size:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(combo);
            panel.Children.Add(note);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Mailings > Labels setup dialog. Offers a handful of common Avery-style presets plus a custom
    // rows × columns option on US Letter. Returns the chosen grid / page geometry, or null if cancelled.
    // The caller applies page settings via ApplyPageSettings then inserts the grid via InsertTable.
    private static class LabelSetupDialog
    {
        public static LabelSetupResult? Ask(Window? owner)
        {
            LabelSetupResult? result = null;

            var presets = MailingsEnvelopeLabelPlanner.GetLabelPresets();
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var p in presets)
                combo.Items.Add(p.Name);
            combo.SelectedIndex = MailingsEnvelopeLabelPlanner.DefaultLabelIndex;

            // Custom rows/columns spinners (shown only when "Custom" is selected).
            var rowsBox = new System.Windows.Controls.TextBox { Text = "10", MinWidth = 50, Margin = new Thickness(4, 0, 12, 0) };
            var colsBox = new System.Windows.Controls.TextBox { Text = "3",  MinWidth = 50 };
            var customPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            customPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Rows:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            customPanel.Children.Add(rowsBox);
            customPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Columns:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            customPanel.Children.Add(colsBox);

            combo.SelectionChanged += (_, _) =>
                customPanel.Visibility = combo.SelectedIndex == MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            var dialog = new Window
            {
                Title = "Labels",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true,  MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                var plan = MailingsEnvelopeLabelPlanner.PlanLabel(combo.SelectedIndex, rowsBox.Text, colsBox.Text);
                if (plan.Result is not { } label)
                {
                    DialogMessageHelper.ShowError(dialog, "Enter valid positive integers for rows and columns.");
                    return;
                }

                result = label;
                dialog.DialogResult = true;
            };

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 340 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Label product:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(combo);
            panel.Children.Add(customPanel);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Insert > Header & Footer: prompt for the header/footer text and store it on the model. An empty
    // entry clears the header/footer. A page-number field already present is preserved by re-appending.
    private sealed class HeaderFooterCommand(
        DocumentView editor,
        bool isFooter,
        Func<bool, string, string?>? askHeaderFooterText) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var model = editor.Model;
            var existing = isFooter ? model.Footer : model.Header;
            var seed = existing?.PlainText ?? string.Empty;
            var label = isFooter ? "Footer" : "Header";

            var text = askHeaderFooterText is { } ask
                ? ask(isFooter, seed)
                : TextPrompt.Ask(Window.GetWindow(editor), $"Edit {label}", $"{label} text:", seed);
            if (text is null)
                return; // cancelled — leave the model untouched

            var value = HeaderFooterDialogPlanner.BuildPlainTextHeaderFooter(text, existing);

            if (isFooter)
                model.Footer = value;
            else
                model.Header = value;

            editor.Focus();
        }
    }

    // ── Header & Footer Design contextual tab commands ───────────────────────────────────────────────
    // Activation model: DOCKED PANE (when host wires onOpenHeaderFooterPane) or fallback DIALOG approach.
    // FreeW's FlowDocument is a single continuous stream — there is no genuine in-document editable header
    // region. Every command routes through the backed SectionHeadersFooters / PageSettings model and
    // round-trips through DocxWriter. The docked pane sub-editor preserves run formatting (bold/italic/
    // colour) that the legacy plain-text dialog lost. Close Header and Footer commits the pane and
    // returns focus to the body.

    // Header & Footer Design: open the docked pane (formatted sub-editor) for a named slot.
    // Used when the host passes onOpenHeaderFooterPane through Build().
    private sealed class OpenHeaderFooterPaneCommand(
        DocumentView editor,
        string slotName,
        Action<string> openPane) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var page = editor.Model.Page;
            var plan = HeaderFooterDialogPlanner.PlanSlotActivation(slotName, page);
            if (plan.Kind != HeaderFooterSlotActivationKind.Active)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    plan.Message ?? string.Empty,
                    HeaderFooterDialogPlanner.EditCaption);
                return;
            }

            openPane(plan.SlotName);
        }
    }

    // Header & Footer Design > Header/Footer: open the per-slot editor for each named slot.
    // The slot name controls which of the 6 SectionHeadersFooters properties is read/written.
    private sealed class EditHeaderSlotCommand(DocumentView editor, string slotName) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var hf = editor.Model.FinalSectionHeadersFooters;
            var page = editor.Model.Page;
            var plan = HeaderFooterDialogPlanner.PlanSlotActivation(slotName, page);

            if (plan.Kind != HeaderFooterSlotActivationKind.Active)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    plan.Message ?? string.Empty,
                    HeaderFooterDialogPlanner.EditCaption);
                return;
            }

            var current = HeaderFooterDialogPlanner.GetSlot(hf, plan.Slot);
            var result = HeaderFooterSlotDialog.Prompt(Window.GetWindow(editor), plan.Label, current);
            if (!result.Accepted)
                return; // cancelled

            HeaderFooterDialogPlanner.SetSlot(hf, plan.Slot, result.Value);

            editor.Focus();
        }
    }

    // Header & Footer Design > Navigation > Go to Header / Go to Footer: open the per-slot editor for
    // the default header or footer, giving a natural "enter edit mode" affordance.
    private sealed class GoToHeaderCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            new EditHeaderSlotCommand(editor, "header").Execute(context);
    }

    private sealed class GoToFooterCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            new EditHeaderSlotCommand(editor, "footer").Execute(context);
    }

    // Header & Footer Design > Close Header and Footer: a no-op command (the contextual tab controller
    // dismisses the header-footer context when the button is pressed). The command is backed so the
    // parity test can verify it is registered.
    private sealed class CloseHeaderFooterCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            // The contextual tab controller dismisses the header-footer context; we just
            // return focus to the body.
            editor.Focus();
        }
    }

    // Header & Footer Design > Options > Different First Page: toggle PageSettings.DifferentFirstPage.
    // The stateful variant exposes IsChecked so the ribbon toggle reflects the current model state.
    private sealed class DifferentFirstPageToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.DifferentFirstPage = !page.DifferentFirstPage);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.Model.Page.DifferentFirstPage);
    }

    // Header & Footer Design > Options > Different Odd & Even Pages: toggle DifferentOddEvenPages.
    private sealed class DifferentOddEvenPagesCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.DifferentOddEvenPages = !page.DifferentOddEvenPages);

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.Model.Page.DifferentOddEvenPages);
    }

    // Header & Footer Design > Position > Header from Top / Footer from Bottom: numeric spinbox-style
    // commands that accept a points value from the combo and write HeaderDistancePt / FooterDistancePt
    // via ApplyPageSettings (same path as the Page Setup dialog).
    private sealed class HeaderFromTopCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (HeaderFooterDialogPlanner.TryParseDistance(value, out var pt))
                editor.ApplyPageSettings(page => page.HeaderDistancePt = pt);
        }

        public RibbonCommandState GetState() =>
            new(Value: HeaderFooterDialogPlanner.FormatDistance(editor.Model.Page.HeaderDistancePt));
    }

    private sealed class FooterFromBottomCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is not { } value)
                return;
            if (HeaderFooterDialogPlanner.TryParseDistance(value, out var pt))
                editor.ApplyPageSettings(page => page.FooterDistancePt = pt);
        }

        public RibbonCommandState GetState() =>
            new(Value: HeaderFooterDialogPlanner.FormatDistance(editor.Model.Page.FooterDistancePt));
    }

    // Insert into header/footer: insert page number, date/time, or a document-info field into the
    // active (default) header or footer slot. These commands reuse the existing field-insertion path
    // and write the result directly into FinalSectionHeadersFooters.Header / .Footer.
    private sealed class InsertIntoHeaderSlotCommand(DocumentView editor, bool isFooter, InsertSlotKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var model = editor.Model;
            var hf = isFooter ? model.Footer : model.Header;
            var slot = hf;

            switch (kind)
            {
                case InsertSlotKind.PageNumber:
                    slot = HeaderFooterDialogPlanner.AddPageNumberToSlot(slot);
                    break;
                case InsertSlotKind.DateTime:
                {
                    var dtResult = DateTimeDialog.Prompt(Window.GetWindow(editor));
                    if (dtResult is null)
                        return;
                    if (dtResult.IsField && dtResult.FieldInstruction is { Length: > 0 } dtInstr)
                        slot = HeaderFooterDialogPlanner.AppendFieldDateTimeToSlot(slot, dtInstr);
                    else if (!string.IsNullOrEmpty(dtResult.Text))
                        slot = HeaderFooterDialogPlanner.AppendPlainDateTimeToSlot(slot, dtResult.Text);
                    break;
                }
                case InsertSlotKind.DocumentInfo:
                {
                    var instruction = FieldPickerDialog.Ask(Window.GetWindow(editor));
                    if (instruction is null)
                        return;
                    slot = HeaderFooterDialogPlanner.AppendComplexFieldToSlot(slot, instruction);
                    break;
                }
            }

            if (isFooter)
                model.Footer = slot;
            else
                model.Header = slot;

            editor.Focus();
        }
    }

    private enum InsertSlotKind { PageNumber, DateTime, DocumentInfo }

    private sealed record HeaderFooterSlotDialogResult(bool Accepted, HeaderFooter? Value);

    // A focused per-slot header/footer editor dialog. Shows the slot's current plain text, lets the
    // user edit it freely, and provides "Insert Page Number", "Insert Date & Time", and "Insert Field"
    // buttons that append to the in-dialog text. On OK the dialog returns a new HeaderFooter built from
    // the edited text, or the original if page-number/field content was appended. Returning null means
    // Cancel (no change).
    private static class HeaderFooterSlotDialog
    {
        /// <summary>
        /// Prompts to edit a single header/footer slot. Returns the new <see cref="HeaderFooter"/>
        /// (possibly null to clear the slot), or returns <paramref name="current"/> unchanged when the
        /// user cancels.
        /// </summary>
        public static HeaderFooterSlotDialogResult Prompt(Window? owner, string slotLabel, HeaderFooter? current)
        {
            // Seed the text box with the slot's plain text (if any).
            var state = HeaderFooterDialogPlanner.BuildSlotDialogState(current);

            // Track whether the user wants to append a page-number or date/time.
            bool appendPageNumber = state.HasPageNumber;
            string? appendDateTime = null;
            string? appendFieldInstruction = null;

            var box = new System.Windows.Controls.TextBox
            {
                Text = state.Text,
                MinWidth = 400,
                MaxHeight = 100,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8)
            };
            box.SelectAll();

            HeaderFooter? result = null;

            var dialog = new Window
            {
                Title = $"Edit {slotLabel}",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            // Insert buttons
            var btnPageNumber = new System.Windows.Controls.Button
            {
                Content = "Insert Page Number",
                MinWidth = 140,
                Margin = new Thickness(0, 0, 8, 8),
                IsEnabled = state.CanInsertPageNumber
            };
            var btnDateTime = new System.Windows.Controls.Button
            {
                Content = "Insert Date && Time",
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 8)
            };
            var btnField = new System.Windows.Controls.Button
            {
                Content = "Insert Field",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 0, 8)
            };

            btnPageNumber.Click += (_, _) =>
            {
                appendPageNumber = true;
                btnPageNumber.IsEnabled = false;
            };

            btnDateTime.Click += (_, _) =>
            {
                var dtR = DateTimeDialog.Prompt(owner);
                if (dtR is not null && !string.IsNullOrEmpty(dtR.Text))
                    appendDateTime = dtR.Text;
            };

            btnField.Click += (_, _) =>
            {
                var instr = FieldPickerDialog.Ask(owner);
                if (instr is not null)
                    appendFieldInstruction = instr;
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = HeaderFooterDialogPlanner.BuildSlotDialogResult(
                    box.Text,
                    appendPageNumber,
                    appendDateTime,
                    appendFieldInstruction);
                dialog.DialogResult = true;
            };

            var insertRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            insertRow.Children.Add(btnPageNumber);
            insertRow.Children.Add(btnDateTime);
            insertRow.Children.Add(btnField);

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 400 };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{slotLabel} text:",
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(box);
            panel.Children.Add(insertRow);
            panel.Children.Add(btnRow);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true
                ? new HeaderFooterSlotDialogResult(Accepted: true, result)
                : new HeaderFooterSlotDialogResult(Accepted: false, current);
        }
    }

    // Design > Page Background > Watermark: open the Custom Watermark dialog (seeded with any current
    // watermark options). The dialog returns new options (OK), null + removeRequested (Remove Watermark),
    // or null (Cancel — no change). Delegates to the view, which mutates PageSettings and re-renders.
    private sealed class WatermarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var current = editor.Model.Page.EffectiveWatermark;
            var chosen = WatermarkOptionsDialog.Prompt(Window.GetWindow(editor), current, out var removeRequested);

            if (chosen is not null)
            {
                editor.SetWatermarkOptions(chosen);
            }
            else if (removeRequested)
            {
                editor.SetWatermarkOptions(null);
            }
            // else: cancelled — leave the model untouched

            editor.Focus();
        }
    }

    // Design > Page Background > Page Color (Word's Page Color): pick the whole-page background colour from
    // a theme-style swatch palette, clear it with "No Color", or open "More Colors..." to type a hex value.
    // The chosen value sets the model's page BackgroundColorHex through DocumentView.SetPageColor (commit +
    // re-render via ApplyPageSettings); it already round-trips as w:background in docx. Mirrors the swatch
    // picker used by Cell Shading / Paragraph Shading.
    private sealed class PageColorCommand(DocumentView editor) : IRibbonCommand
    {
        // Word's "Theme Colors" top row plus standard colors: a sensible page-tint palette.
        private static readonly string[] Palette =
        [
            "#FFFFFF", "#F2F2F2", "#DDD9C3", "#C6D9F1", "#DBE5F1", "#F2DCDB",
            "#EBF1DE", "#E5E0EC", "#FDE9D9", "#FFF2CC", "#DEEBF7", "#E2EFDA",
            "#FCE4D6", "#D9E1F2", "#FFFFCC", "#E2F0D9", "#000000", "#1F1F1F",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return; // cancelled — leave the model untouched
            editor.Focus();
            editor.SetPageColor(hex); // null clears back to the default white sheet
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = UiText.Get("Ribbon_Dialog_PageColor_Title"),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 6 * 26 };
            foreach (var swatchHex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatchHex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = swatchHex
                };
                swatch.Click += (_, _) => { chosen = true; hex = swatchHex; window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var noColor = new Button
            {
                Content = UiText.Get("Ribbon_Palette_PageColor_NoColor_Label"),
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            noColor.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(noColor);

            var more = new Button
            {
                Content = UiText.Get("Ribbon_Dialog_PageColor_MoreColors_Label"),
                Margin = new Thickness(2, 4, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            more.Click += (_, _) =>
            {
                var seed = editor.Model.Page.BackgroundColorHex ?? "#";
                var typed = TextPrompt.Ask(
                    window,
                    UiText.Get("Ribbon_Dialog_PageColor_MoreColors_Title"),
                    UiText.Get("Ribbon_Dialog_PageColor_HexPrompt"),
                    seed);
                if (typed is null)
                    return; // stay on the palette
                var normalized = NormalizeHex(typed);
                if (normalized is null)
                {
                    DialogMessageHelper.ShowWarning(
                        window,
                        UiText.Get("Ribbon_Dialog_PageColor_InvalidHexWarning"),
                        UiText.Get("Ribbon_Dialog_PageColor_Title"));
                    return;
                }
                chosen = true; hex = normalized; window.Close();
            };
            panel.Children.Add(more);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }

        // Accept "#RRGGBB" / "RRGGBB" (case-insensitive); return a normalised "#RRGGBB" or null if invalid.
        private static string? NormalizeHex(string raw)
        {
            var value = raw.Trim().TrimStart('#');
            if (value.Length != 6)
                return null;
            foreach (var c in value)
            {
                if (!Uri.IsHexDigit(c))
                    return null;
            }
            return "#" + value.ToUpperInvariant();
        }
    }

    // The three gallery positions for Insert > Header & Footer > Page Number.
    private enum PageNumberPosition { Bottom, Top, Current }

    // Insert > Header & Footer > Page Number: drop a page-number field into the header (Top), footer
    // (Bottom), or body at the caret (Current). The gallery maps each position to an instance of this
    // command. Top and Bottom edit the model's Header/Footer directly. Current inserts a page-number
    // run into the body at the caret block's position.
    private sealed class InsertPageNumberCommand(DocumentView editor, PageNumberPosition position) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var model = editor.Model;

            if (position == PageNumberPosition.Current)
            {
                // Insert a page-number run in the body at the caret (undoable via undo/redo bus).
                editor.InsertPageNumberAtCaret();
                return;
            }

            if (position == PageNumberPosition.Top)
            {
                model.Header = HeaderFooterDialogPlanner.AddPageNumberToSlot(model.Header);
            }
            else
            {
                model.Footer = HeaderFooterDialogPlanner.AddPageNumberToSlot(model.Footer);
            }
        }
    }

    // Insert > Header & Footer > Page Number > Format Page Numbers: apply the shared
    // number style, chapter prefix, and start/continue settings.
    private sealed class PageNumberFormatCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (PageNumberFormatDialogPlanner.TryBuildResultFromCommandValue(context.SelectedValue, out var contextResult))
            {
                editor.ApplyPageNumberFormat(contextResult);
                return;
            }

            if (PageNumberFormatDialog.Prompt(Window.GetWindow(editor), editor.Model.Page) is { } result)
                editor.ApplyPageNumberFormat(result);
        }
    }

    private static class PageNumberFormatDialog
    {
        public static PageNumberFormatDialogResult? Prompt(Window? owner, PageSettings page)
        {
            var state = PageNumberFormatDialogPlanner.BuildInitialState(page);
            PageNumberFormatDialogResult? result = null;

            var formatBox = new System.Windows.Controls.ComboBox
            {
                MinWidth = 180,
                ItemsSource = PageNumberFormatDialogPlanner.FormatItems.Select(item => item.Label).ToArray(),
                SelectedIndex = state.FormatIndex,
                Margin = new Thickness(0, 2, 0, 10)
            };
            var includeChapter = new System.Windows.Controls.CheckBox
            {
                Content = PageNumberFormatDialogPlanner.IncludeChapterNumberLabel,
                IsChecked = state.IncludeChapterNumber,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var chapterStyleBox = new System.Windows.Controls.ComboBox
            {
                MinWidth = 160,
                ItemsSource = PageNumberFormatDialogPlanner.ChapterStyleItems.Select(item => item.Label).ToArray(),
                SelectedIndex = state.ChapterStyleIndex,
                Margin = new Thickness(0, 2, 0, 8)
            };
            var chapterSeparatorBox = new System.Windows.Controls.ComboBox
            {
                MinWidth = 140,
                ItemsSource = PageNumberFormatDialogPlanner.ChapterSeparatorItems.Select(item => item.Label).ToArray(),
                SelectedIndex = state.ChapterSeparatorIndex,
                Margin = new Thickness(0, 2, 0, 10)
            };
            void UpdateChapterControlState()
            {
                var enabled = includeChapter.IsChecked == true;
                chapterStyleBox.IsEnabled = enabled;
                chapterSeparatorBox.IsEnabled = enabled;
            }
            includeChapter.Checked += (_, _) => UpdateChapterControlState();
            includeChapter.Unchecked += (_, _) => UpdateChapterControlState();
            UpdateChapterControlState();
            var continueRadio = new System.Windows.Controls.RadioButton
            {
                Content = PageNumberFormatDialogPlanner.ContinueLabel,
                GroupName = "PageNumbering",
                IsChecked = state.ContinueFromPreviousSection,
                Margin = new Thickness(0, 2, 0, 4)
            };
            var startRadio = new System.Windows.Controls.RadioButton
            {
                Content = PageNumberFormatDialogPlanner.StartAtLabel,
                GroupName = "PageNumbering",
                IsChecked = !state.ContinueFromPreviousSection,
                Margin = new Thickness(0, 2, 8, 4)
            };
            var startBox = new System.Windows.Controls.TextBox
            {
                Text = state.StartAtText,
                Width = 72,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var status = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var dialog = new Window
            {
                Title = PageNumberFormatDialogPlanner.Title,
                Owner = owner,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                if (!PageNumberFormatDialogPlanner.TryBuildResult(
                        new PageNumberFormatDialogInput(
                            formatBox.SelectedIndex,
                            continueRadio.IsChecked == true,
                            startBox.Text,
                            includeChapter.IsChecked == true,
                            chapterStyleBox.SelectedIndex,
                            chapterSeparatorBox.SelectedIndex),
                        out result,
                        out var error))
                {
                    status.Text = error ?? PageNumberFormatDialogPlanner.InvalidStartAtMessage;
                    return;
                }

                dialog.DialogResult = true;
            };

            var startRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            startRow.Children.Add(startRadio);
            startRow.Children.Add(startBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 280 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = PageNumberFormatDialogPlanner.NumberFormatLabel });
            panel.Children.Add(formatBox);
            panel.Children.Add(includeChapter);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = PageNumberFormatDialogPlanner.ChapterStartsWithStyleLabel });
            panel.Children.Add(chapterStyleBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = PageNumberFormatDialogPlanner.ChapterSeparatorLabel });
            panel.Children.Add(chapterSeparatorBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = PageNumberFormatDialogPlanner.PageNumberingLabel,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 2)
            });
            panel.Children.Add(continueRadio);
            panel.Children.Add(startRow);
            panel.Children.Add(status);
            panel.Children.Add(buttons);

            dialog.Content = panel;
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Insert > Quick Parts > Field: open a categorised picker listing Word's common field codes and drop
    // the chosen field at the caret as a generic complex field (w:fldChar/w:instrText), so it round-trips
    // losslessly and supports Alt+F9 (toggle codes) / F9 (update). The picker returns the raw field
    // instruction (e.g. " PAGE ", " DATE \@ \"M/d/yyyy\" ", " FILENAME ").
    private sealed class InsertFieldCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var instruction = FieldPickerDialog.Ask(Window.GetWindow(editor));
            if (instruction is not { } chosen)
                return; // cancelled
            editor.InsertComplexField(chosen);
        }
    }

    // Alt+F9: toggle whether the document's fields show their field codes or their results.
    private sealed class ToggleFieldCodesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => editor.ToggleFieldCodes();
    }

    // F9: update (recompute) every field's result in the document.
    private sealed class UpdateFieldsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => editor.UpdateFields();
    }

    // A modal dialog listing the insertable document field codes, grouped by category (Date and Time /
    // Document Information / Numbering / References). Returns the chosen raw field INSTRUCTION
    // (e.g. " PAGE ", " DATE \@ \"M/d/yyyy\" ", " AUTHOR "), or null if cancelled.
    // This is the backing for Insert > Quick Parts > Field (freew.field) and mirrors Word's Field dialog
    // field-name browser.
    private static class FieldPickerDialog
    {
        public static string? Ask(Window? owner)
        {

            // Category listbox on the left; field listbox on the right — a two-pane layout
            // matching the spirit of Word's Field dialog without requiring full XAML.
            var catList = new System.Windows.Controls.ListBox
            {
                MinWidth = 160,
                Margin = new Thickness(0, 0, 8, 0)
            };
            foreach (var cat in FieldPickerDialogPlanner.Categories)
                catList.Items.Add(cat);

            var fieldList = new System.Windows.Controls.ListBox { MinWidth = 220 };

            void RefreshFields()
            {
                var cat = catList.SelectedItem as string;
                fieldList.Items.Clear();
                foreach (var c in FieldPickerDialogPlanner.ChoicesForCategory(cat))
                    fieldList.Items.Add(c.Label);
                if (fieldList.Items.Count > 0)
                    fieldList.SelectedIndex = 0;
            }

            catList.SelectionChanged += (_, _) => RefreshFields();
            catList.SelectedIndex = 0;

            string? result = null;
            var dialog = new Window
            {
                Title = "Insert Field",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            void Commit()
            {
                var cat = catList.SelectedItem as string;
                var label = fieldList.SelectedItem as string;
                if (FieldPickerDialogPlanner.TryGetInstruction(cat, label, out var instruction))
                    result = instruction;
                dialog.DialogResult = true;
            }
            ok.Click += (_, _) => Commit();
            fieldList.MouseDoubleClick += (_, _) => Commit();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var listsRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            listsRow.Children.Add(catList);
            listsRow.Children.Add(fieldList);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Choose a field to insert:",
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(listsRow);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Home > Paragraph > Sort: open the Sort dialog (type + order + case + header-row) and sort either
    // the rows of the table at the caret (by the caret's column, matching Word) or the selected
    // paragraphs. The view routes the reorder through its undo/redo bus and re-renders.
    private sealed class SortCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var inTable = editor.IsCaretInTable();
            var choice = SortDialog.Prompt(Window.GetWindow(editor), forTable: inTable);
            if (choice is null)
                return; // cancelled

            editor.Focus();
            var c = choice.Value;
            if (inTable)
                editor.SortCaretTableRows(c.Kind, c.Ascending, c.CaseSensitive, c.HasHeaderRow);
            else
                editor.SortSelectedParagraphs(c.Kind, c.Ascending, c.CaseSensitive, c.HasHeaderRow);
        }
    }

    // Layout > Convert Text to Table: ask for a delimiter, then turn the selected paragraphs into a
    // table (splitting each paragraph on that delimiter). The view routes the change through its bus.
    private sealed class TextToTableCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (DelimiterDialog.Ask(Window.GetWindow(editor), "Convert Text to Table") is not { } delimiter)
                return; // cancelled
            editor.Focus();
            editor.ConvertSelectionToTable(delimiter);
        }
    }

    // Layout > Convert Table to Text: ask for a delimiter, then turn the caret's table into delimited
    // paragraphs (one per row). The view routes the change through its bus.
    private sealed class TableToTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (DelimiterDialog.Ask(Window.GetWindow(editor), "Convert Table to Text") is not { } delimiter)
                return; // cancelled
            editor.Focus();
            editor.ConvertTableToText(delimiter);
        }
    }

    // A small modal dialog choosing the cell delimiter for text/table conversion: Tab, Comma, or
    // Semicolon. Returns the chosen delimiter character, or null if cancelled.
    private static class DelimiterDialog
    {
        public static char? Ask(Window? owner, string title)
        {
            var choices = TableTextConversionDialogPlanner.Choices;

            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 90,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                list.Items.Add(choice.Label);
            list.SelectedIndex = TableTextConversionDialogPlanner.DefaultChoiceIndex;

            char? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Commit()
            {
                var index = list.SelectedIndex;
                if (TableTextConversionDialogPlanner.DelimiterAt(index) is { } delimiter)
                {
                    result = delimiter;
                    dialog.DialogResult = true;
                }
            }
            ok.Click += (_, _) => Commit();
            list.MouseDoubleClick += (_, _) => Commit();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = TableTextConversionDialogPlanner.PromptLabel, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // A tiny modal text-entry dialog. Returns the entered text (possibly empty), or null if cancelled.
    private static class TextPrompt
    {
        public static string? Ask(Window? owner, string title, string label, string seed)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                MinWidth = 360,
                Margin = new Thickness(0, 0, 0, 12)
            };
            box.SelectAll();

            string? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // A tiny modal dialog asking for a single line of text (a URL, a ScreenTip, …). Returns the entered
    // text, or null if cancelled. Title/label default to the insert-link wording for existing callers.
    private static class HyperlinkPrompt
    {
        public static string? Ask(Window? owner, string seed, string title = "Insert Link", string label = "Address:")
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                MinWidth = 360,
                Margin = new Thickness(0, 0, 0, 12)
            };
            box.SelectAll();

            string? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // References > Citation Style: set the editor's active citation style from the combo box label
    // ("APA"/"MLA"/"Chicago"/"IEEE"). The style is stored on the document (TextDocument.BibliographyStyle via
    // DocumentView.ActiveCitationStyle) so it persists and atomically refreshes existing native citations,
    // an existing generated bibliography, and subsequently inserted references. Unrecognised labels leave
    // the current style unchanged.
    private sealed class CitationStyleCommand(DocumentView editor, RibbonStateStore stateStore) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var value = context.SelectedValue;
            if (value is null
                && context.Parameters.TryGetValue("value", out var legacyRaw))
            {
                value = legacyRaw as string;
            }

            if (string.IsNullOrWhiteSpace(value))
                return;

            editor.ApplyCitationStyle(Citations.ParseStyle(value, editor.ActiveCitationStyle));
            stateStore.SetState("freew.citation-style", GetState());
        }

        public RibbonCommandState GetState() =>
            new(Value: Citations.StyleName(editor.ActiveCitationStyle));
    }

    private sealed class SelectionValueCommand(
        DocumentView editor,
        Action<TextSelection, string> apply,
        Func<string, bool>? tryModelApply = null,
        Func<string>? getValue = null) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ComboValue(context) is { Length: > 0 } value)
            {
                editor.Focus();
                if (tryModelApply?.Invoke(value) == true)
                    return;
                apply(editor.Selection, value);
            }
        }

        public RibbonCommandState GetState() =>
            new(Value: getValue?.Invoke());
    }

    private sealed class RoutedEditCommand(DocumentView editor, RoutedCommand command) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }
    }

    private sealed class ToggleFormatCommand(
        DocumentView editor,
        RoutedCommand command,
        DependencyProperty property,
        Func<object?, bool> isOn,
        Func<bool>? tryModelToggle) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (tryModelToggle?.Invoke() == true)
                return;
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }

        public RibbonCommandState GetState()
        {
            var value = editor.Selection.GetPropertyValue(property);
            return new RibbonCommandState(IsEnabled: true, IsChecked: value != DependencyProperty.UnsetValue && isOn(value));
        }
    }

    // ── Drawing Format contextual tab private commands ───────────────────────────────────────────

    // Drawing Format > Shape Styles > Shape Fill: open a color hex prompt; apply to selected shape.
    private sealed class ShapeFillCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first, then choose Shape Fill.", "Shape Fill");
                return;
            }
            var current = shape.FillColorHex?.TrimStart('#') ?? string.Empty;
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Shape Fill",
                "Fill color (6-digit hex, or blank for no fill):", current);
            if (text is null) return;
            var trimmed = text.Trim().TrimStart('#');
            editor.SetSelectedShapeFill(trimmed.Length == 6 ? "#" + trimmed.ToUpperInvariant() : null);
        }
    }

    // Drawing Format > Shape Styles > Shape Outline: reuse ImageBorderDialog (same fields).
    private sealed class ShapeOutlineCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first, then choose Shape Outline.", "Shape Outline");
                return;
            }
            var result = ImageBorderDialog.Prompt(
                Window.GetWindow(editor),
                shape.OutlineColorHex, shape.OutlineWidthPt, shape.OutlineDash);
            if (result is { } r)
                editor.SetSelectedShapeOutline(r.Color is { Length: > 0 } c ? "#" + c : null, r.Width, r.Dash);
        }
    }

    // Drawing Format > Size > Alt Text: prompt for shape or WordArt alt text.
    private sealed class ShapeAltTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            var wordArt = editor.SelectedWordArt();
            if (shape is null && wordArt is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    "Select a shape or WordArt first, then choose Alt Text.", "Alt Text");
                return;
            }
            var current = shape?.AltText ?? wordArt?.AltText ?? string.Empty;
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Alt Text", "Description:", current);
            if (text is not null)
            {
                if (shape is not null)
                    editor.SetSelectedShapeAltText(text);
                else
                    editor.SetSelectedWordArtAltText(text);
            }
        }
    }

    // Drawing Format > Arrange > Align: set paragraph alignment of the containing paragraph.
    private sealed class ShapeAlignCommand(DocumentView editor, FreeW.Core.Model.TextAlignment alignment) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!GetState().IsEnabled)
            {
                return;
            }
            editor.SetSelectedShapeAlignment(alignment);
        }

        public RibbonCommandState GetState() => new(IsEnabled: editor.SelectedShape() is not null);
    }

    // Drawing Format > Arrange > Wrap Text: set the wrapping mode on the selected shape.
    private sealed class ShapeWrapCommand(DocumentView editor, ImageWrapping wrapping) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Wrap Text");
                return;
            }
            editor.SetSelectedShapeWrapping(wrapping);
        }
    }

    // Drawing Format > Arrange > Rotate: rotate the selected shape by a fixed step (relative to current).
    private sealed class ShapeRotateStepCommand(DocumentView editor, double stepDeg) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Rotate");
                return;
            }
            var newAngle = (shape.RotationAngle + stepDeg + 360) % 360;
            editor.SetSelectedShapeRotation(newAngle, shape.FlipH, shape.FlipV);
        }
    }

    // Drawing Format > Arrange > Flip Vertical / Flip Horizontal.
    private sealed class ShapeFlipCommand(DocumentView editor, bool vertical) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Flip");
                return;
            }
            if (vertical)
                editor.SetSelectedShapeRotation(shape.RotationAngle, shape.FlipH, !shape.FlipV);
            else
                editor.SetSelectedShapeRotation(shape.RotationAngle, !shape.FlipH, shape.FlipV);
        }
    }

    // Drawing Format > Arrange > Position: open the position dialog for the selected shape's floating offset + anchors.
    private sealed class ShapePositionCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var position = editor.GetSelectedShapePosition();
            if (position is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Position");
                return;
            }
            var result = ImagePositionDialog.Prompt(
                Window.GetWindow(editor),
                position.Value.HorizontalOffsetPt,
                position.Value.VerticalOffsetPt,
                position.Value.HorizontalAnchor,
                position.Value.VerticalAnchor,
                position.Value.IsGroupLocal ? "Shape Position in Group" : "Shape Position",
                position.Value.IsGroupLocal);
            if (result is { } r)
                editor.SetSelectedShapePosition(r.HOffset, r.VOffset, r.HAnchor, r.VAnchor);
        }
    }

    // Home > Font > Character Border (freew.char-border): opens a small border-style/colour picker and
    // applies a character border to all runs in the selected paragraphs via the undo/redo bus.
    // "None" clears the border. Uses the ParagraphShadingCommand colour-swatch pattern for consistency.
    private sealed class CharacterBorderCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, border) = ShowPicker(owner);
            if (!chosen)
                return;
            editor.SetCharacterBorder(border);
        }

        private (bool Chosen, ParagraphBorder? Border) ShowPicker(Window? owner)
        {
            var chosen = false;
            ParagraphBorder? border = null;
            var window = new Window
            {
                Title = CharacterFormattingPickerPlanner.BorderTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var layout = CharacterFormattingPickerPlanner.Layout;
            var panel = new StackPanel { Margin = new Thickness(layout.PanelMargin) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = CharacterFormattingPickerPlanner.BorderPrompt, Margin = new Thickness(0, 0, 0, 4) });
            var grid = new WrapPanel { Width = layout.PaletteWidth };
            foreach (var (choice, choiceIndex) in CharacterFormattingPickerPlanner.BorderPalette.Select((choice, index) => (choice, index)))
            {
                var swatch = new Button
                {
                    Width = layout.SwatchSize, Height = layout.SwatchSize, Margin = new Thickness(layout.SwatchMargin),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(layout.SwatchBorderHex)),
                    BorderThickness = new Thickness(1),
                    ToolTip = choice.Hex
                };
                swatch.Click += (_, _) =>
                {
                    chosen = true;
                    border = CharacterFormattingPickerPlanner.SelectBorder(choiceIndex).Border;
                    window.Close();
                };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = CharacterFormattingPickerPlanner.NoBorderLabel,
                Margin = new Thickness(layout.ClearHorizontalMargin, layout.ClearTopMargin, layout.ClearHorizontalMargin, 0),
                Padding = new Thickness(layout.ClearHorizontalPadding, 2, layout.ClearHorizontalPadding, 2)
            };
            clear.Click += (_, _) =>
            {
                var result = CharacterFormattingPickerPlanner.SelectNoBorder();
                chosen = result.Accepted;
                border = result.Border;
                window.Close();
            };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, border);
        }
    }

    // Home > Font > Character Shading (freew.char-shading): colour swatch picker for run background
    // fill (pattern-aware w:shd at run level). Mirrors ParagraphShadingCommand's UI.
    private sealed class CharacterShadingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return;
            editor.SetCharacterShading(hex);
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = CharacterFormattingPickerPlanner.ShadingTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var layout = CharacterFormattingPickerPlanner.Layout;
            var panel = new StackPanel { Margin = new Thickness(layout.PanelMargin) };
            var grid = new WrapPanel { Width = layout.PaletteWidth };
            foreach (var (choice, choiceIndex) in CharacterFormattingPickerPlanner.ShadingPalette.Select((choice, index) => (choice, index)))
            {
                var swatch = new Button
                {
                    Width = layout.SwatchSize, Height = layout.SwatchSize, Margin = new Thickness(layout.SwatchMargin),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(layout.SwatchBorderHex)),
                    BorderThickness = new Thickness(1),
                    ToolTip = choice.Hex
                };
                swatch.Click += (_, _) =>
                {
                    var result = CharacterFormattingPickerPlanner.SelectShading(choiceIndex);
                    chosen = result.Accepted;
                    hex = result.Hex;
                    window.Close();
                };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = CharacterFormattingPickerPlanner.NoColorLabel,
                Margin = new Thickness(layout.ClearHorizontalMargin, layout.ClearTopMargin, layout.ClearHorizontalMargin, 0),
                Padding = new Thickness(layout.ClearHorizontalPadding, 2, layout.ClearHorizontalPadding, 2)
            };
            clear.Click += (_, _) =>
            {
                var result = CharacterFormattingPickerPlanner.SelectNoColor();
                chosen = result.Accepted;
                hex = result.Hex;
                window.Close();
            };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }
    }

    // Review > Language > Set Proofing Language (freew.set-proofing-language): dialog listing common
    // BCP-47 language tags; applies the chosen tag to all runs in the selected paragraphs (rPr/w:lang).
    // The WPF spell checker uses the run's Language property so the correct dictionary is active.
    private sealed class SetProofingLanguageCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var current = editor.CurrentRunFormatting.LanguageTag;
            var chosen = ShowDialog(owner, current);
            if (chosen is null)
                return; // cancelled
            editor.SetProofingLanguage(chosen == string.Empty ? null : chosen);
        }

        private static string? ShowDialog(Window? owner, string? current)
        {
            string? result = null;
            var window = new Window
            {
                Title = "Set Proofing Language",
                Width = 320,
                Height = 420,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var listBox = new System.Windows.Controls.ListBox { Margin = new Thickness(0, 0, 0, 8) };
            var plan = ProofingLanguageDialogPlanner.Build(current);
            foreach (var choice in plan.Choices)
                listBox.Items.Add(new System.Windows.Controls.ListBoxItem { Content = choice.DisplayText, Tag = choice.Tag });
            listBox.SelectedIndex = plan.SelectedIndex;
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            ok.Click += (_, _) =>
            {
                if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem selected)
                    result = (string?)selected.Tag;
                window.DialogResult = true;
            };
            cancel.Click += (_, _) => window.Close();

            var btnRow = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);

            var outer = new StackPanel { Margin = new Thickness(12) };
            outer.Children.Add(new System.Windows.Controls.TextBlock { Text = "Select the proofing language for the selected text:", TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            outer.Children.Add(listBox);
            outer.Children.Add(btnRow);

            var scroll = new System.Windows.Controls.ScrollViewer { Content = listBox, Height = 280, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            outer.Children.RemoveAt(1); // remove the un-scrolled list
            outer.Children.Insert(1, scroll);

            window.Content = outer;
            return window.ShowDialog() == true ? result : null; // null = cancelled
        }
    }

    // -----------------------------------------------------------------------------------------
    // Feature 1 — Line Number Options dialog and command
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Opens the dedicated Line Numbering Options dialog (Start At / Count By / Restart mode).
    /// Writes back to <see cref="PageSettings"/> via <see cref="DocumentView.ApplyPageSettings"/>.
    /// </summary>
    private sealed class LineNumberOptionsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var page = editor.Model.Page;
            var result = LineNumberOptionsDialog.Prompt(
                Window.GetWindow(editor),
                page.LineNumberStartAt,
                page.LineNumberCountBy,
                page.LineNumberMode == LineNumberMode.None ? LineNumberMode.RestartEachPage : page.LineNumberMode);
            if (result is null) return;
            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyLineNumberOptions(page, result));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Feature 2 — Floating Align / Distribute commands
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Aligns floating objects to the page or margin through the shared undoable model command.
    /// </summary>
    private sealed class FloatingAlignCommand(DocumentView editor, FloatingObjectArrangeKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.ArrangeFloatingObjects(kind);
        }
    }

    /// <summary>
    /// Distributes floating objects through the shared undoable model command.
    /// </summary>
    private sealed class FloatingDistributeCommand(DocumentView editor, FloatingObjectArrangeKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!editor.ArrangeFloatingObjects(kind))
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select at least two floating objects to distribute.",
                    kind == FloatingObjectArrangeKind.DistributeVertical
                        ? "Distribute Vertically"
                        : "Distribute Horizontally");
            }
        }
    }
}
