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
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore) =>
        Build(editor, stateStore, onPrintPreview: null);

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
        Action? onCloseHeaderFooterPane = null)
    {
        var registry = new RibbonCommandRegistry();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();

        void Routed(string id, RoutedCommand command) =>
            registry.Register(id, new RoutedEditCommand(editor, command));

        void Toggle(string id, RoutedCommand command, DependencyProperty property, Func<object?, bool> isOn)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn);
            registry.Register(id, cmd);
            stateful.Add((id, cmd));
        }

        Toggle("freew.bold", EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold);
        Toggle("freew.italic", EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic);
        Toggle("freew.underline", EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
            v => v is TextDecorationCollection d && d.Count > 0);

        // Live ribbon state: when the caret/selection moves, recompute the toggle states and push
        // them into the shared RibbonStateStore, which the toggle buttons observe.
        editor.SelectionChanged += (_, _) =>
        {
            foreach (var (id, command) in stateful)
                stateStore.SetState(id, command.GetState());
        };

        // Home > Font: character effects. Superscript/subscript are mutually exclusive baseline
        // offsets; small caps / all caps map to WPF typography. Each is a toggle over the selection.
        registry.Register("freew.superscript", new CharacterEffectCommand(editor, CharacterEffect.Superscript));
        registry.Register("freew.subscript", new CharacterEffectCommand(editor, CharacterEffect.Subscript));
        registry.Register("freew.strikethrough", new CharacterEffectCommand(editor, CharacterEffect.Strikethrough));
        registry.Register("freew.smallcaps", new CharacterEffectCommand(editor, CharacterEffect.SmallCaps));
        registry.Register("freew.allcaps", new CharacterEffectCommand(editor, CharacterEffect.AllCaps));

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
            registry.Register("freew.find", new ActionCommand(onFindReplace));
            registry.Register("freew.replace", new ActionCommand(onFindReplace));
        }
        // Home > Paragraph: apply multilevel/legal outline numbering (1, 1.1, 1.1.1) to the selected
        // paragraph(s); the outline definition persists to word/numbering.xml. Tab/Shift+Tab demote
        // and promote the outline depth (ListLevel) of the selected list paragraphs.
        registry.Register("freew.multilevel-list", new ActionCommand(() => editor.ApplyMultiLevelList()));
        registry.Register("freew.multilevel-demote", new ActionCommand(() => editor.ChangeListLevel(+1)));
        registry.Register("freew.multilevel-promote", new ActionCommand(() => editor.ChangeListLevel(-1)));
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);
        // Home > Clipboard: paste-special. "Paste Text Only" strips all source formatting; "Merge
        // Formatting" matches the destination. In FreeW both resolve to match-destination insertion at
        // the caret (the pasted text inherits the caret run's formatting), routed through the editor's
        // undoable InsertText path. See DocumentView.PastePlainText / PasteMergeFormatting.
        registry.Register("freew.paste-plain", new ActionCommand(() => editor.PastePlainText()));
        registry.Register("freew.paste-merge", new ActionCommand(() => editor.PasteMergeFormatting()));

        // Home > Clipboard > Format Painter: arm the painter from the current selection's run +
        // paragraph formatting; the editor stamps it onto the user's next mouse selection and disarms.
        registry.Register("freew.format-painter", new FormatPainterCommand(editor));

        registry.Register("freew.font-family", new SelectionValueCommand(editor,
            (selection, value) => selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(value))));
        registry.Register("freew.font-size", new SelectionValueCommand(editor, (selection, value) =>
        {
            if (double.TryParse(value, out var points))
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
        }));

        // Insert tab — Pages: prepend a cover page, insert a blank page, or drop a horizontal rule / page break at the caret.
        // Each mutates the model through the view's undo/redo bus and re-renders.
        // Insert > Pages > Cover Page gallery: Default (existing centred layout), Banded (dark-blue title
        // band), and Motion (right-aligned title with date). The top-level id inserts the default preset
        // so clicking the button face (not the dropdown arrow) always works as before.
        registry.Register("freew.cover-page", new ActionCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-default", new ActionCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-banded", new ActionCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Banded); }));
        registry.Register("freew.cover-page-motion", new ActionCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Motion); }));
        registry.Register("freew.blank-page", new ActionCommand(() => { editor.Focus(); editor.InsertBlankPage(); }));
        registry.Register("freew.horizontal-rule", new ActionCommand(() => { editor.Focus(); editor.InsertHorizontalRule(); }));
        registry.Register("freew.page-break", new ActionCommand(() => { editor.Focus(); editor.InsertPageBreak(); }));

        // Layout > Page Setup > Breaks: section/column breaks. The page-break item reuses the existing
        // command (registered above). Each section break inserts a paragraph whose SectionBreak property
        // is set to the appropriate SectionBreakKind, inheriting the current document's page settings.
        registry.Register("freew.column-break", new ActionCommand(() => { editor.Focus(); editor.InsertColumnBreak(); }));
        registry.Register("freew.section-break-next-page", new ActionCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.NextPage); }));
        registry.Register("freew.section-break-continuous", new ActionCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.Continuous); }));
        registry.Register("freew.section-break-even-page", new ActionCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.EvenPage); }));
        registry.Register("freew.section-break-odd-page", new ActionCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.OddPage); }));

        // Insert tab — insert a small 2x2 table at the caret (routes through the undo/redo bus).
        registry.Register("freew.table", new InsertTableCommand(editor, rows: 2, columns: 2));
        // Insert tab — Table Tools: structural edits to the table containing the caret (all undoable).
        registry.Register("freew.table-insert-row", new ActionCommand(() => { editor.Focus(); editor.InsertTableRow(); }));
        registry.Register("freew.table-delete-row", new ActionCommand(() => { editor.Focus(); editor.DeleteTableRow(); }));
        registry.Register("freew.table-insert-col", new ActionCommand(() => { editor.Focus(); editor.InsertTableColumn(); }));
        registry.Register("freew.table-delete-col", new ActionCommand(() => { editor.Focus(); editor.DeleteTableColumn(); }));
        // Insert tab — Table Tools: merge the selected cells / split a merged cell (all undoable).
        registry.Register("freew.merge-cells", new ActionCommand(() => { editor.Focus(); editor.MergeSelectedCells(); }));
        registry.Register("freew.split-cell", new ActionCommand(() => { editor.Focus(); editor.SplitCell(); }));
        // Insert tab — Table Tools: pick/clear a fill colour for the caret's cell (sets model + re-renders).
        registry.Register("freew.cell-shading", new CellShadingCommand(editor));
        // Insert tab — Table Tools: table-style toggles applied to the caret's table (sets model + re-renders).
        // Table Tools — Data: insert a computed formula field (=SUM(ABOVE) etc.) into the caret's cell.
        registry.Register("freew.table-formula", new TableFormulaCommand(editor));
        // Table Tools — Properties: open the four-tab Table Properties dialog for the caret's table.
        registry.Register("freew.table-properties", new TablePropertiesCommand(editor));
        registry.Register("freew.table-header-row", new ActionCommand(() => { editor.Focus(); editor.ToggleTableHeaderRow(); }));
        registry.Register("freew.table-banded-rows", new ActionCommand(() => { editor.Focus(); editor.ToggleTableBandedRows(); }));
        registry.Register("freew.table-repeat-header", new ActionCommand(() => { editor.Focus(); editor.ToggleTableRepeatHeaderRow(); }));

        // Table Tools — Directional insert/delete
        registry.Register("freew.table-insert-above", new ActionCommand(() => { editor.Focus(); editor.InsertTableRowAbove(); }));
        registry.Register("freew.table-insert-col-left", new ActionCommand(() => { editor.Focus(); editor.InsertTableColumnLeft(); }));
        registry.Register("freew.table-delete", new ActionCommand(() => { editor.Focus(); editor.DeleteTable(); }));
        // Table Tools — Merge/Split enhancements
        registry.Register("freew.split-table", new ActionCommand(() => { editor.Focus(); editor.SplitTable(); }));
        // Table Tools — Select
        registry.Register("freew.table-select-table", new ActionCommand(() => { editor.Focus(); editor.SelectTable(); }));
        registry.Register("freew.table-select-row", new ActionCommand(() => { editor.Focus(); editor.SelectTableRow(); }));
        registry.Register("freew.table-select-col", new ActionCommand(() => { editor.Focus(); editor.SelectTableColumn(); }));
        registry.Register("freew.table-select-cell", new ActionCommand(() => { editor.Focus(); editor.SelectTableCell(); }));
        // Table Tools — View Gridlines (toggle; display-only)
        registry.Register("freew.table-view-gridlines", new ActionCommand(() => { editor.ViewGridlines = !editor.ViewGridlines; editor.Focus(); }));
        // Table Tools — Cell Size
        registry.Register("freew.table-row-height", new TablePropertiesCommand(editor));
        registry.Register("freew.table-col-width", new TablePropertiesCommand(editor));
        registry.Register("freew.table-distribute-rows", new ActionCommand(() => { editor.Focus(); editor.DistributeTableRows(); }));
        registry.Register("freew.table-distribute-cols", new ActionCommand(() => { editor.Focus(); editor.DistributeTableColumns(); }));
        registry.Register("freew.table-autofit-contents", new ActionCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Contents); }));
        registry.Register("freew.table-autofit-window", new ActionCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Window); }));
        registry.Register("freew.table-autofit-fixed", new ActionCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Fixed); }));
        // Table Tools — Cell Alignment (9-way)
        registry.Register("freew.cell-align-top-left", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-top-center", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-top-right", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Right); }));
        registry.Register("freew.cell-align-middle-left", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-middle-center", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-middle-right", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Right); }));
        registry.Register("freew.cell-align-bottom-left", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Left); }));
        registry.Register("freew.cell-align-bottom-center", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Center); }));
        registry.Register("freew.cell-align-bottom-right", new ActionCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Right); }));
        // Table Tools — Cell Margins (opens Table Properties dialog)
        registry.Register("freew.table-cell-margins", new TablePropertiesCommand(editor));
        // Table Design — Style Options toggles
        registry.Register("freew.table-last-row", new ActionCommand(() => { editor.Focus(); editor.ToggleTableLastRow(); }));
        registry.Register("freew.table-first-column", new ActionCommand(() => { editor.Focus(); editor.ToggleTableFirstColumn(); }));
        registry.Register("freew.table-last-column", new ActionCommand(() => { editor.Focus(); editor.ToggleTableLastColumn(); }));
        registry.Register("freew.table-banded-cols", new ActionCommand(() => { editor.Focus(); editor.ToggleTableBandedColumns(); }));
        // Table Layout Data group — Convert to Text
        registry.Register("freew.table-to-text", new ActionCommand(() => { editor.Focus(); editor.ConvertTableToText('\t'); }));

        // Insert tab — Text: pick a .docx file and insert its body content at the caret (block merge).
        registry.Register("freew.insert-file", new InsertFileCommand(editor));
        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Register("freew.picture", new InsertPictureCommand(editor));
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
        registry.Register("freew.image-wrap-inline", new ImageWrapCommand(editor, ImageWrapping.Inline));
        registry.Register("freew.image-wrap-square", new ImageWrapCommand(editor, ImageWrapping.Square));
        registry.Register("freew.image-wrap-tight", new ImageWrapCommand(editor, ImageWrapping.Tight));
        registry.Register("freew.image-wrap-top-bottom", new ImageWrapCommand(editor, ImageWrapping.TopAndBottom));
        registry.Register("freew.image-wrap-behind", new ImageWrapCommand(editor, ImageWrapping.Behind));
        registry.Register("freew.image-wrap-front", new ImageWrapCommand(editor, ImageWrapping.InFront));
        // Picture Format tab — Arrange > Rotate / Flip.
        registry.Register("freew.image-rotate-right90", new ImageRotateStepCommand(editor, +90));
        registry.Register("freew.image-rotate-left90",  new ImageRotateStepCommand(editor, -90));
        registry.Register("freew.image-flip-vertical",  new ImageFlipCommand(editor, vertical: true));
        registry.Register("freew.image-flip-horizontal",new ImageFlipCommand(editor, vertical: false));
        // Picture Format tab — Arrange > Position.
        registry.Register("freew.image-position", new ImagePositionCommand(editor));
        // Picture Format tab — Adjust > Crop / Reset / Border.
        registry.Register("freew.image-crop",   new ImageCropCommand(editor));
        registry.Register("freew.image-reset",  new ImageResetCommand(editor));
        registry.Register("freew.image-border", new ImageBorderCommand(editor));
        // Picture Format tab — Arrange > Z-order (floating images only).
        registry.Register("freew.image-bring-to-front",  new ImageZOrderCommand(editor, ZOrderOperation.BringToFront));
        registry.Register("freew.image-send-to-back",    new ImageZOrderCommand(editor, ZOrderOperation.SendToBack));
        registry.Register("freew.image-bring-forward",   new ImageZOrderCommand(editor, ZOrderOperation.BringForward));
        registry.Register("freew.image-send-backward",   new ImageZOrderCommand(editor, ZOrderOperation.SendBackward));
        // Insert tab — Illustrations > Shapes: a small gallery of preset DrawingML shapes. Each menu item
        // inserts the matching Shape (preset geometry, or a text box carrying placeholder text) at the caret
        // via DocumentView.InsertShape. Round-trips through docx as an inline w:drawing/wps:wsp (see
        // DocxWriter/Reader). The top-level "freew.shapes" id only opens the menu (no direct insert).
        registry.Register("freew.shape-rectangle", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Rectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-rounded", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.RoundedRectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-ellipse", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Ellipse, widthPt: 100, heightPt: 100, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-textbox", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"));
        }));
        // Insert tab — Media: drop a sample equation / chart / WordArt / SmartArt / OLE object at the caret.
        // Each routes through the editor's undoable insert path (mirroring InsertShape) and round-trips
        // through docx (the model + IO already exist; this surfaces them in the ribbon). Sample content is a
        // starting point the user can replace.
        registry.Register("freew.equation", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertEquation(SampleEquation());
        }));
        // Equation gallery presets (Insert > Media > Equation dropdown). Each inserts one OMML structure
        // at the caret as an editable starting point; all round-trip through the model/IO layer.
        registry.Register("freew.equation-fraction", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Fraction("a", "b")]))));
        registry.Register("freew.equation-script", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.SubSuperscript("x", "n", "2")]))));
        registry.Register("freew.equation-radical", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x")]))));
        registry.Register("freew.equation-nthroot", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x", "n")]))));
        registry.Register("freew.equation-integral", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∫", "a", "b", "f(x) dx")]))));
        registry.Register("freew.equation-summation", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∑", "i=1", "n", "i")]))));
        registry.Register("freew.equation-product", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∏", "i=1", "n", "i")]))));
        registry.Register("freew.equation-accent", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.AccentOf("x")]))));
        registry.Register("freew.equation-bar", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.BarOf("x")]))));
        registry.Register("freew.equation-bracket", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Delimiter("a, b")]))));
        registry.Register("freew.equation-matrix", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]))));
        registry.Register("freew.equation-func", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.FunctionApply("sin", "x")]))));
        registry.Register("freew.equation-groupchr", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.GroupCharOf("x+y")]))));
        registry.Register("freew.chart", new ActionCommand(() =>
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
            registry.Register($"freew.chart-type-{k.ToString().ToLowerInvariant()}", new ActionCommand(() =>
            {
                editor.Focus();
                editor.SetSelectedChartKind(k);
            }));
        }
        // Add Chart Element — toggle Legend.
        registry.Register("freew.chart-toggle-legend", new ActionCommand(() =>
        {
            editor.Focus();
            editor.ToggleSelectedChartLegend();
        }));
        // Add Chart Element — set/clear Chart Title.
        registry.Register("freew.chart-title", new ActionCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var (accepted, newTitle) = ChartTitleDialog.Prompt(Application.Current?.MainWindow, chart.Title);
            if (accepted)
                editor.SetSelectedChartTitle(newTitle);
        }));
        // Add Chart Element — set axis titles.
        registry.Register("freew.chart-axis-titles", new ActionCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var result = ChartAxisTitlesDialog.Prompt(Application.Current?.MainWindow, chart.CategoryAxisTitle, chart.ValueAxisTitle);
            if (result is not null)
                editor.SetSelectedChartAxisTitles(result.Value.CategoryTitle, result.Value.ValueTitle);
        }));
        // Edit Data — reopen the data grid dialog.
        registry.Register("freew.chart-edit-data", new ActionCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var replacement = InsertChartDialog.Prompt(Application.Current?.MainWindow, chart);
            if (replacement is not null)
                editor.ReplaceSelectedChartData(replacement);
        }));
        // Chart Format contextual tab — Size dialog.
        registry.Register("freew.chart-size", new ActionCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null) return;
            var result = ChartSizeDialog.Prompt(Application.Current?.MainWindow, chart.WidthPt, chart.HeightPt);
            if (result is not null)
                editor.SetSelectedChartSize(result.Value.WidthPt, result.Value.HeightPt);
        }));
        // ── Drawing Format contextual tab — Shape/Drawing/TextBox/WordArt commands ─────────────────
        // Change Shape: picker over ShapeKind; no model work — ShapeKind already exists.
        registry.Register("freew.shape-change-rectangle", new ActionCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Change Shape");
                return;
            }
            editor.SetSelectedShapeKind(FreeW.Core.Model.ShapeKind.Rectangle);
        }));
        registry.Register("freew.shape-change-rounded", new ActionCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Change Shape");
                return;
            }
            editor.SetSelectedShapeKind(FreeW.Core.Model.ShapeKind.RoundedRectangle);
        }));
        registry.Register("freew.shape-change-ellipse", new ActionCommand(() =>
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
        registry.Register("freew.shape-fill-no-fill", new ActionCommand(() =>
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
        registry.Register("freew.shape-outline-no-outline", new ActionCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Shape Outline");
                return;
            }
            editor.SetSelectedShapeOutline(null, 0, null);
        }));
        registry.Register("freew.shape-outline-solid", new ActionCommand(() =>
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
        registry.Register("freew.shape-outline-dash", new ActionCommand(() =>
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
        registry.Register("freew.shape-outline-dot", new ActionCommand(() =>
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
        registry.Register("freew.shape-text-direction", new ActionCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a text direction from the dropdown.", "Text Direction");
        }));
        registry.Register("freew.shape-text-horizontal", new ActionCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a text box first.", "Text Direction");
                return;
            }
            editor.SetSelectedShapeTextDirection(FreeW.Core.Model.ShapeTextDirection.Horizontal);
        }));
        registry.Register("freew.shape-text-rotate90", new ActionCommand(() =>
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a text box first.", "Text Direction");
                return;
            }
            editor.SetSelectedShapeTextDirection(FreeW.Core.Model.ShapeTextDirection.Rotate90);
        }));
        registry.Register("freew.shape-text-rotate270", new ActionCommand(() =>
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
        registry.Register("freew.shape-size", new ActionCommand(() =>
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
        // WordArt style gallery — four existing presets.
        registry.Register("freew.wordart-style", new ActionCommand(() =>
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose a WordArt style from the dropdown.", "WordArt Style");
        }));
        foreach (WordArtStyle preset in Enum.GetValues<WordArtStyle>())
        {
            var p = preset;
            var id = p switch
            {
                WordArtStyle.FillBlue    => "freew.wordart-style-fill-blue",
                WordArtStyle.GradientFill => "freew.wordart-style-gradient",
                WordArtStyle.Outline     => "freew.wordart-style-outline",
                WordArtStyle.Shadow      => "freew.wordart-style-shadow",
                _ => $"freew.wordart-style-{p.ToString().ToLowerInvariant()}"
            };
            registry.Register(id, new ActionCommand(() =>
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
        // ── End Drawing Format commands ───────────────────────────────────────────────────────────

        registry.Register("freew.wordart", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertWordArt(WordArt.Create("WordArt", WordArtStyle.GradientFill));
        }));
        registry.Register("freew.smartart", new ActionCommand(() =>
        {
            var owner = Application.Current?.MainWindow;
            var result = InsertSmartArtDialog.Prompt(owner);
            if (result is null) return;
            editor.Focus();
            editor.InsertSmartArt(result);
        }));
        // SmartArt Design contextual tab — node mutation commands.
        registry.Register("freew.smartart-add-shape", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtAddShape();
        }));
        registry.Register("freew.smartart-remove-shape", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtRemoveShape();
        }));
        registry.Register("freew.smartart-promote", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtPromote();
        }));
        registry.Register("freew.smartart-demote", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtDemote();
        }));
        registry.Register("freew.smartart-move-up", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtMoveUp();
        }));
        registry.Register("freew.smartart-move-down", new ActionCommand(() =>
        {
            editor.Focus();
            editor.SmartArtMoveDown();
        }));
        registry.Register("freew.smartart-edit-text", new ActionCommand(() =>
        {
            var owner = Application.Current?.MainWindow;
            var current = editor.SelectedSmartArt();
            if (current is null) return;
            var result = InsertSmartArtDialog.Prompt(owner, current);
            if (result is null) return;
            editor.Focus();
            editor.ReplaceSelectedSmartArt(result);
        }));
        registry.Register("freew.object", new ActionCommand(() =>
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
            var notesPaneCmd = new ToggleNotesPaneCommand(editor, onToggleNotesPane, isNotesPaneVisible);
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
        registry.Register("freew.toc", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfContents(); }));
        registry.Register("freew.toc-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfContents(); }));
        registry.Register("freew.toc-add-text", new ApplyTocStyleCommand(editor, "Heading1"));
        registry.Register("freew.toc-addtext-none", new ApplyTocStyleCommand(editor, "Normal"));
        registry.Register("freew.toc-addtext-level1", new ApplyTocStyleCommand(editor, "Heading1"));
        registry.Register("freew.toc-addtext-level2", new ApplyTocStyleCommand(editor, "Heading2"));
        registry.Register("freew.toc-addtext-level3", new ApplyTocStyleCommand(editor, "Heading3"));
        // Insert tab — References: insert an in-text citation (pick an existing source or add a new one),
        // and insert a bibliography built from the document's sources at the caret (reversible).
        registry.Register("freew.citation", new InsertCitationCommand(editor));
        registry.Register("freew.manage-sources", new ManageSourcesCommand(editor));
        registry.Register("freew.bibliography", new ActionCommand(() => { editor.Focus(); editor.InsertBibliography(); }));
        // Insert tab — References: select the active citation/bibliography style (APA / MLA / Chicago) used
        // by the citation + bibliography commands. The combo box delivers its label via the "value" param.
        registry.Register("freew.citation-style", new CitationStyleCommand(editor));
        // Insert tab — References: insert a numbered figure/table caption under the caret's block.
        registry.Register("freew.caption", new InsertCaptionCommand(editor));
        // Insert tab — References: insert a cross-reference (heading/bookmark/caption/footnote) at the caret.
        registry.Register("freew.cross-reference", new InsertCrossReferenceCommand(editor));
        // Insert tab — References: mark the selection (or a prompted term) for the document index, and
        // insert an alphabetical index built from the marked terms at the caret (reversibly via the bus).
        registry.Register("freew.index-mark", new MarkIndexEntryCommand(editor));
        registry.Register("freew.index-insert", new ActionCommand(() => { editor.Focus(); editor.InsertIndex(); }));
        registry.Register("freew.index-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshIndex(); }));
        // Insert tab — References: generate a Table of Figures from the document's figure captions at the
        // caret, and rebuild it in place (remove the prior region + re-insert). Both route through the bus.
        registry.Register("freew.tof", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfFigures(); }));
        registry.Register("freew.tof-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(); }));
        // Insert tab — References: mark the selection as a legal citation (a hidden TA field), and insert /
        // rebuild a Table of Authorities built from those marks, grouped by category (reversibly via the bus).
        registry.Register("freew.mark-citation", new MarkCitationCommand(editor));
        registry.Register("freew.table-of-authorities", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfAuthorities(); }));
        registry.Register("freew.table-of-authorities-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfAuthorities(); }));
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
        registry.Register("freew.cc-text", new ActionCommand(() => { editor.Focus(); editor.InsertPlainTextControl(); }));
        registry.Register("freew.cc-richtext", new ActionCommand(() => { editor.Focus(); editor.InsertRichTextControl(); }));
        registry.Register("freew.cc-checkbox", new ActionCommand(() => { editor.Focus(); editor.InsertCheckBoxControl(); }));
        registry.Register("freew.cc-date", new ActionCommand(() => { editor.Focus(); editor.InsertDatePickerControl(); }));
        registry.Register("freew.cc-dropdown", new ActionCommand(() => { editor.Focus(); editor.InsertDropDownListControl(); }));
        registry.Register("freew.cc-combo", new ActionCommand(() => { editor.Focus(); editor.InsertComboBoxControl(); }));

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

        // Review tab — Proofing: custom dictionary + spelling options. The custom dictionary is a
        // word-per-line .lex file persisted under FreeW's data folder; its Uri is registered with the
        // editor's WPF spell checker so those words stop being flagged. "Add to Dictionary" takes the
        // misspelled word at the caret, adds it to the dictionary (+ persists), and re-reads the file so
        // it is no longer underlined. "Spell Check" is a stateful toggle over SpellCheck.IsEnabled.
        var customDictionary = CustomDictionaryStore.Load();
        editor.RegisterCustomDictionary(customDictionary.EnsureFileExists());
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
        registry.Register("freew.accept-all", new ActionCommand(() => { editor.Focus(); editor.AcceptAllRevisions(); }));
        registry.Register("freew.reject-all", new ActionCommand(() => { editor.Focus(); editor.RejectAllRevisions(); }));

        // Review tab — Tracking display controls: Display for Review and Show Markup per-category toggles.
        //
        // Display for Review exposes a dropdown backed by DocumentView.MarkupDisplayMode. The root button
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

        // The root "Show Markup" button opens the dropdown; no direct action needed — register it as an
        // ActionCommand that is always a no-op so the "every command is backed" parity assertion passes.
        registry.Register("freew.show-markup", new ActionCommand(() => { }));

        // Review tab — single-revision reviewing surface (the Reviewing Pane). The toggle shows/hides the
        // dockable revisions list; Accept/Reject act on the SELECTED single change and Previous/Next step
        // through them. All four delegate to the host, which owns the pane and drives the pure RevisionList.
        if (onToggleReviewingPane is not null && isReviewingPaneVisible is not null)
            registry.Register("freew.reviewing-pane",
                new ToggleActionCommand(onToggleReviewingPane, isReviewingPaneVisible));
        if (onAcceptThisChange is not null)
            registry.Register("freew.accept-this", new ActionCommand(onAcceptThisChange));
        if (onRejectThisChange is not null)
            registry.Register("freew.reject-this", new ActionCommand(onRejectThisChange));
        if (onPreviousChange is not null)
            registry.Register("freew.previous-change", new ActionCommand(onPreviousChange));
        if (onNextChange is not null)
            registry.Register("freew.next-change", new ActionCommand(onNextChange));

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
        registry.Register("freew.header", new HeaderFooterCommand(editor, isFooter: false));
        registry.Register("freew.footer", new HeaderFooterCommand(editor, isFooter: true));
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
                ? new ActionCommand(onCloseHeaderFooterPane)
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
        registry.Register("freew.clear-formatting", new ActionCommand(() => editor.ClearFormatting()));
        registry.Register("freew.drop-cap", new ActionCommand(() => editor.ApplyDropCap()));

        // Home > Font > Change Case: open a small menu to pick a target case (UPPERCASE / lowercase /
        // Sentence case / Capitalize Each Word / tOGGLE cASE) and recase the selection's text via the
        // pure ChangeCase helper. The replacement flows through the editor's normal edit/undo path.
        registry.Register("freew.change-case", new ChangeCaseCommand(editor));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection,
        // and toggle Add/Remove Space Before/After. All route through the view's undo/redo bus.
        registry.Register("freew.line-spacing", new LineSpacingCommand(editor));
        registry.Register("freew.space-before-toggle", new ActionCommand(() => editor.ToggleSpaceBefore()));
        registry.Register("freew.space-after-toggle", new ActionCommand(() => editor.ToggleSpaceAfter()));

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

        // Home > Paragraph: increase/decrease the left indent by one 0.5in step over the selection, and
        // open the Paragraph dialog to set left/right/first-line (incl. hanging) indents. All reversible.
        registry.Register("freew.indent-increase", new ActionCommand(() => { editor.Focus(); editor.IncreaseIndent(); }));
        registry.Register("freew.indent-decrease", new ActionCommand(() => { editor.Focus(); editor.DecreaseIndent(); }));
        registry.Register("freew.paragraph-dialog", new ParagraphIndentCommand(editor));
        registry.Register("freew.tabs-dialog", new TabsCommand(editor));

        // Home > Paragraph: toggle a box border on the selected paragraph(s), and pick/clear shading.
        registry.Register("freew.para-border", new ActionCommand(() => editor.ToggleParagraphBorder()));
        registry.Register("freew.para-shading", new ParagraphShadingCommand(editor));
        // Home / Design > Borders and Shading…: the full dialog (paragraph border, page border, shading).
        registry.Register("freew.borders-shading", new BordersAndShadingCommand(editor));

        // Home > Paragraph (Line and Page Breaks): flow-control toggles over the selected paragraph(s).
        // Each flips its pPr flag (keepNext/keepLines/widowControl) reversibly through the undo/redo bus.
        registry.Register("freew.keep-with-next", new ActionCommand(() => { editor.Focus(); editor.ToggleKeepWithNext(); }));
        registry.Register("freew.keep-lines", new ActionCommand(() => { editor.Focus(); editor.ToggleKeepLinesTogether(); }));
        registry.Register("freew.widow-control", new ActionCommand(() => { editor.Focus(); editor.ToggleWidowControl(); }));

        // Layout > Sort: open a small dialog (A→Z / Z→A + case-sensitive option) and sort the selected
        // paragraphs in place through the view's undo/redo bus.
        registry.Register("freew.sort", new SortCommand(editor));

        // Layout > Table conversions: turn the selected paragraphs into a table (splitting on a chosen
        // delimiter) and turn the caret's table back into delimited paragraphs. Both route through the bus.
        registry.Register("freew.text-to-table", new TextToTableCommand(editor));
        registry.Register("freew.table-to-text", new TableToTextCommand(editor));

        registry.Register("freew.style-normal", new ApplyStyleCommand(editor, 11, bold: false, colorHex: null));
        registry.Register("freew.style-heading1", new ApplyStyleCommand(editor, 16, bold: true, colorHex: "#2F5496"));
        registry.Register("freew.style-title", new ApplyStyleCommand(editor, 28, bold: true, colorHex: null));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        registry.Register("freew.style", new ApplyParagraphStyleCommand(editor));

        // Home > Styles: New Style opens a dialog capturing name + formatting + based-on, creates a custom
        // DocumentStyle via the pure StyleManager and applies it to the selection. Manage Styles lets the
        // user modify or delete the catalog's styles (built-ins are guarded against deletion).
        registry.Register("freew.new-style", new NewStyleCommand(editor));
        registry.Register("freew.manage-styles", new ManageStylesCommand(editor));

        // Design > Document Formatting: Themes apply a full preset, Colors preserve fonts while applying
        // a palette, Style Sets rewrite built-in styles, and Fonts preserve colours while applying a
        // heading/body font pair. All are backed document-wide style changes.
        registry.Register("freew.theme", new ApplyThemeCommand(editor));
        registry.Register("freew.style-set", new ApplyStyleSetCommand(editor));
        registry.Register("freew.theme-colors", new ApplyThemeColorsCommand(editor));
        registry.Register("freew.theme-fonts", new ApplyFontSetCommand(editor));
        registry.Register("freew.paragraph-spacing", new ApplyParagraphSpacingSetCommand(editor));
        registry.Register("freew.theme-effects", new ApplyEffectSetCommand(editor));

        // Layout tab — page settings (applied to the model; honoured by docx save + print).
        registry.Register("freew.orientation", new PageCommand(editor, page =>
        {
            (page.WidthPt, page.HeightPt) = (page.HeightPt, page.WidthPt);
            page.Landscape = !page.Landscape;
        }));
        registry.Register("freew.margins", new PageCommand(editor, page =>
        {
            var narrow = page.MarginLeftPt > 54;
            var margin = narrow ? 36.0 : 72.0;
            page.MarginLeftPt = page.MarginRightPt = page.MarginTopPt = page.MarginBottomPt = margin;
        }));
        registry.Register("freew.size", new PageCommand(editor, page =>
        {
            var isLetter = Math.Abs(page.WidthPt - 612) < 1 && Math.Abs(page.HeightPt - 792) < 1;
            (page.WidthPt, page.HeightPt) = isLetter ? (595.0, 842.0) : (612.0, 792.0); // toggle Letter <-> A4
        }));
        // Columns: open the Columns dialog or apply Word's backed preset menu choices directly, mutating
        // PageSettings and re-rendering so the live document flow changes immediately.
        registry.Register("freew.columns", new ColumnsCommand(editor));
        registry.Register("freew.columns-one", new ColumnsPresetCommand(editor, ColumnsPreset.One));
        registry.Register("freew.columns-two", new ColumnsPresetCommand(editor, ColumnsPreset.Two));
        registry.Register("freew.columns-three", new ColumnsPresetCommand(editor, ColumnsPreset.Three));
        registry.Register("freew.columns-left", new ColumnsPresetCommand(editor, ColumnsPreset.Left));
        registry.Register("freew.columns-right", new ColumnsPresetCommand(editor, ColumnsPreset.Right));
        registry.Register("freew.columns-more", new ColumnsCommand(editor));
        // Page Setup: the unified Margins / Paper / Layout dialog (Word's Layout > Page Setup launcher). The
        // "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the Margins / Paper tab.
        registry.Register("freew.page-setup", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.custom-margins", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.more-paper-sizes", new PageSetupCommand(editor, PageSetupDialog.Tab.Paper));
        // Line Numbers: Word-style menu items set the backed mode explicitly, while the top-level command keeps
        // the existing cycle behavior for quick access (shown in print preview and the live page adorner).
        registry.Register("freew.line-numbers", new LineNumberCommand(editor));
        registry.Register("freew.line-numbers-none", new LineNumberModeCommand(editor, LineNumberMode.None));
        registry.Register("freew.line-numbers-continuous", new LineNumberModeCommand(editor, LineNumberMode.Continuous));
        registry.Register("freew.line-numbers-restart-page", new LineNumberModeCommand(editor, LineNumberMode.RestartEachPage));
        registry.Register("freew.line-numbers-options", new PageSetupCommand(editor, PageSetupDialog.Tab.Layout));

        // Page setup polish — all mutate PageSettings via ApplyPageSettings (commit + re-render) and
        // round-trip through docx save.
        //  - Hyphenation: a dropdown (None / Automatic / Manual / Options…). The split-button default action
        //    (freew.hyphenation) toggles automatic hyphenation; the menu items set an explicit mode, and the
        //    Options item opens the Hyphenation Options dialog. Automatic hyphenation inserts soft hyphens in
        //    the live document (settings.xml w:autoHyphenation + zone/limit/caps sub-options).
        //  - Page Vertical Alignment: cycle Top -> Center -> Justified (-> Bottom) (sectPr w:vAlign).
        //  - Different First Page: toggle a distinct first-page header/footer (sectPr w:titlePg).
        registry.Register("freew.hyphenation", new HyphenationCommand(editor));
        registry.Register("freew.hyphenation-none", new HyphenationModeCommand(editor, auto: false));
        registry.Register("freew.hyphenation-auto", new HyphenationModeCommand(editor, auto: true));
        registry.Register("freew.hyphenation-manual", new HyphenationManualCommand(editor));
        registry.Register("freew.hyphenation-options", new HyphenationOptionsCommand(editor));
        registry.Register("freew.page-valign", new PageVerticalAlignmentCommand(editor));
        registry.Register("freew.different-first-page", new DifferentFirstPageCommand(editor));

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
            registry.Register("freew.print-preview", new ActionCommand(onPrintPreview));

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

        // Home tab — toggle the Reveal Formatting pane (Word's Shift+F1 pane), a read-only side pane
        // showing the effective FONT / PARAGRAPH / SECTION formatting of the selection. Stateful so the
        // ribbon's toggle button reflects whether the pane is currently shown.
        if (onToggleRevealFormatting is not null && isRevealFormattingVisible is not null)
            registry.Register("freew.reveal-formatting",
                new ToggleActionCommand(onToggleRevealFormatting, isRevealFormattingVisible));

        // View tab — open Word's Zoom dialog (presets / page fits / custom %). The host computes the
        // page-relative fit factors from the live viewport and applies the chosen factor to the editor.
        if (onZoomDialog is not null)
            registry.Register("freew.zoom-dialog", new ActionCommand(onZoomDialog));
        if (onZoom100 is not null)
            registry.Register("freew.zoom-100", new ActionCommand(onZoom100));
        if (onZoomOnePage is not null)
            registry.Register("freew.zoom-one-page", new ActionCommand(onZoomOnePage));
        if (onZoomPageWidth is not null)
            registry.Register("freew.zoom-page-width", new ActionCommand(onZoomPageWidth));

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
            registry.Register("freew.help-online", new ActionCommand(onHelpOnline));
        if (onFeedback is not null)
            registry.Register("freew.feedback", new ActionCommand(onFeedback));
        if (onCopyDiagnostics is not null)
            registry.Register("freew.copy-diagnostics", new ActionCommand(onCopyDiagnostics));
        if (onCheckForUpdates is not null)
            registry.Register("freew.check-updates", new ActionCommand(onCheckForUpdates));
        if (onAbout is not null)
            registry.Register("freew.about", new ActionCommand(onAbout));
        if (onLegalNotices is not null)
            registry.Register("freew.legal-notices", new ActionCommand(onLegalNotices));

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
        // Special merge fields: «Next Record» and «Merge Record #» (inserted as plain placeholders;
        // the engine recognises them during substitution via SubstituteSpecial).
        registry.Register("freew.merge-next-record", new InsertSpecialMergeFieldCommand(editor, MailMerge.NextRecordField));
        registry.Register("freew.merge-record-number", new InsertSpecialMergeFieldCommand(editor, MailMerge.MergeRecordNumberField));
        registry.Register("freew.merge-preview", new PreviewMergeRecordCommand(editor, mergeSession));
        registry.Register("freew.merge-preview-first", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.First));
        registry.Register("freew.merge-preview-previous", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Previous));
        registry.Register("freew.merge-preview-next", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Next));
        registry.Register("freew.merge-preview-last", new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Last));
        registry.Register("freew.merge-finish", new FinishMergeCommand(editor, mergeSession));
        // Filter & Sort: refines the active session's MergeData (include/exclude rows, sort column/direction)
        // without touching the merge template. No-ops gracefully when there is no active session or data.
        registry.Register("freew.merge-filter-sort", new FilterSortRecipientsCommand(editor, mergeSession));
        // Envelopes / Labels: set up the page geometry (and optionally a table grid for labels) via the
        // backed ApplyPageSettings / InsertTable paths. No SMTP or print path — page-setup only.
        registry.Register("freew.merge-envelopes", new EnvelopesCommand(editor));
        registry.Register("freew.merge-labels", new LabelsCommand(editor, mergeSession));

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

    // A parameterless ribbon command that runs a host-supplied action (e.g. opening a window).
    private sealed class ActionCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
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

    // Home > Paragraph > Line Spacing: parse the chosen multiplier (e.g. "1.5") and apply it to every
    // paragraph spanned by the selection. The view routes the change through its undo/redo bus.
    private sealed class LineSpacingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) && multiplier > 0)
            {
                editor.Focus();
                editor.SetLineSpacing(multiplier);
            }
        }
    }

    // Layout > Paragraph > Indent Left / Indent Right: numeric combo boxes (points) that display the
    // first selected paragraph's left/right indent and apply an exact value while preserving the
    // existing first-line indent. Both implement IRibbonStatefulCommand so SelectionChanged can push
    // the live value into the ribbon store and the combo reflects the current paragraph state.
    private sealed class IndentLeftCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
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
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
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
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
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
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
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
    private sealed class ApplyParagraphStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value || value.Length == 0)
                return;

            var styleId = ResolveStyleId(editor.Model, value);
            if (styleId is null)
                return;

            editor.Focus();
            editor.SetParagraphStyle(styleId);
        }

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

            var created = StyleManager.CreateStyle(editor.Model, def.Name, def.BasedOnId, def.Run, def.Paragraph, def.NextStyleId);
            editor.Focus();
            editor.SetParagraphStyle(created.Id);
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
                        StyleManager.DeleteStyle(editor.Model, del.StyleId);
                        editor.RefreshStyles();
                        continue; // reopen the list so the user sees the removal

                    case ManageStyleAction.Modify mod:
                        if (!editor.Model.Styles.TryGetValue(mod.StyleId, out var existing))
                            continue;
                        var def = StyleDialog.AskModify(owner, StyleNamesById(editor.Model), existing);
                        if (def is null)
                            continue;
                        StyleManager.ModifyStyle(editor.Model, mod.StyleId,
                            run: def.Run, para: def.Paragraph, basedOnId: def.BasedOnId,
                            clearBasedOn: def.BasedOnId is null,
                            nextStyleId: def.NextStyleId, clearNext: def.NextStyleId is null);
                        editor.RefreshStyles();
                        continue;
                }
            }
        }
    }

    // The document's style catalog as id -> display name, for the dialogs' based-on / style lists.
    private static IReadOnlyDictionary<string, string> StyleNamesById(TextDocument model) =>
        model.Styles.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

    // Design > Document Formatting: apply a built-in document theme. The selected name may arrive from
    // a combo value, older host context, or a WPF menu item header; all resolve to the same catalog entry.
    private sealed class ApplyThemeCommand(DocumentView editor) : IRibbonCommand
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

        private static string? LegacyValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue("value", out var raw) ? raw as string : null;

        private static string? MenuHeaderValue(RibbonCommandContext context) =>
            context.Parameters.TryGetValue(Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey, out var sender)
            && sender is System.Windows.Controls.MenuItem { Tag: string header }
                ? header
                : null;
    }

    private sealed class ApplyStyleSetCommand(DocumentView editor) : IRibbonCommand
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
            var owner = Window.GetWindow(editor);
            var chosen = ShowPicker(owner);
            if (chosen is null)
                return;

            var property = isHighlight ? TextElement.BackgroundProperty : TextElement.ForegroundProperty;
            editor.Focus();
            if (chosen == ColorChoice.Clear)
                // Clear the override: foreground falls back to black, highlight to no background.
                editor.Selection.ApplyPropertyValue(property, isHighlight ? null! : Brushes.Black);
            else
                editor.Selection.ApplyPropertyValue(property,
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(chosen.Hex)));
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
            var formula = TableFormulaDialog.Prompt(owner, DefaultFormula(table, rowIndex, columnIndex));
            if (formula is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertTableFormula(formula);
        }

        // Word's default: =SUM(ABOVE) when numeric cells sit above the formula cell; otherwise =SUM(LEFT)
        // when numbers sit to the left; falling back to =SUM(ABOVE).
        private static string DefaultFormula(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            if (HasNumberAbove(table, rowIndex, columnIndex))
                return "=SUM(ABOVE)";
            if (HasNumberLeft(table, rowIndex, columnIndex))
                return "=SUM(LEFT)";
            return "=SUM(ABOVE)";
        }

        private static bool HasNumberAbove(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            for (var r = rowIndex - 1; r >= 0; r--)
            {
                var cells = table.Rows[r].Cells;
                if (columnIndex < cells.Count && TableFormulaEvaluator.TryParseCellNumber(cells[columnIndex].PlainText, out _))
                    return true;
            }
            return false;
        }

        private static bool HasNumberLeft(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= table.Rows.Count)
                return false;
            var cells = table.Rows[rowIndex].Cells;
            for (var c = columnIndex - 1; c >= 0; c--)
            {
                if (c < cells.Count && TableFormulaEvaluator.TryParseCellNumber(cells[c].PlainText, out _))
                    return true;
            }
            return false;
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
            editor.SetCaretCellShading(hex);
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = "Cell Shading",
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

    private sealed class PageCommand(DocumentView editor, Action<PageSettings> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => apply(editor.Model.Page);
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

            editor.ApplyPageSettings(page =>
            {
                page.ColumnCount = result.Count;
                page.ColumnSpacingPt = result.SpacingPt;
                page.ColumnsLineBetween = result.LineBetween;
                page.ColumnWidthsPt = result.WidthsPt;
            });
        }
    }

    private enum ColumnsPreset
    {
        One,
        Two,
        Three,
        Left,
        Right
    }

    // Word's Layout > Columns dropdown applies common presets immediately. Equal presets clear explicit
    // widths; Left/Right set the classic narrow/wide two-column split using the current page content width.
    private sealed class ColumnsPresetCommand(DocumentView editor, ColumnsPreset preset) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page =>
            {
                var spacing = page.ColumnSpacingPt;
                page.ColumnsLineBetween = false;
                page.ColumnWidthsPt = null;

                switch (preset)
                {
                    case ColumnsPreset.One:
                        page.ColumnCount = 1;
                        break;
                    case ColumnsPreset.Two:
                        page.ColumnCount = 2;
                        break;
                    case ColumnsPreset.Three:
                        page.ColumnCount = 3;
                        break;
                    case ColumnsPreset.Left:
                        page.ColumnCount = 2;
                        page.ColumnWidthsPt = UnequalWidths(page, narrowFirst: true, spacing);
                        break;
                    case ColumnsPreset.Right:
                        page.ColumnCount = 2;
                        page.ColumnWidthsPt = UnequalWidths(page, narrowFirst: false, spacing);
                        break;
                }
            });

        private static IReadOnlyList<double> UnequalWidths(PageSettings page, bool narrowFirst, double spacing)
        {
            var contentWidthPt = Math.Max(72, page.WidthPt - page.MarginLeftPt - page.MarginRightPt);
            const double narrowPt = 108; // 1.5 inch, matching the Columns dialog's Left/Right presets.
            var widePt = Math.Max(36, contentWidthPt - spacing - narrowPt);
            return narrowFirst ? [narrowPt, widePt] : [widePt, narrowPt];
        }
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
            editor.ApplyPageSettings(page =>
            {
                page.MarginTopPt = settings.MarginTopPt;
                page.MarginBottomPt = settings.MarginBottomPt;
                page.MarginLeftPt = settings.MarginLeftPt;
                page.MarginRightPt = settings.MarginRightPt;
                page.GutterPt = settings.GutterPt;
                page.Landscape = settings.Landscape;
                page.MirrorMargins = settings.MirrorMargins;
                page.WidthPt = settings.WidthPt;
                page.HeightPt = settings.HeightPt;
                page.DifferentFirstPage = settings.DifferentFirstPage;
                page.DifferentOddEvenPages = settings.DifferentOddEvenPages;
                page.HeaderDistancePt = settings.HeaderDistancePt;
                page.FooterDistancePt = settings.FooterDistancePt;
                page.VerticalAlignment = settings.VerticalAlignment;
            });

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
            editor.ApplyPageSettings(page => page.LineNumberMode = page.LineNumberMode switch
            {
                LineNumberMode.None => LineNumberMode.Continuous,
                LineNumberMode.Continuous => LineNumberMode.RestartEachPage,
                _ => LineNumberMode.None
            });
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
            editor.ApplyPageSettings(page => page.AutoHyphenation = !page.AutoHyphenation);
    }

    // Hyphenation dropdown — None / Automatic: sets the document's automatic-hyphenation flag explicitly
    // (Word's Hyphenation > None / Automatic). Routes through ApplyPageSettings (commit + re-render) so the
    // soft-hyphen rendering shows at once and the flag round-trips through settings.xml.
    private sealed class HyphenationModeCommand(DocumentView editor, bool auto) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.AutoHyphenation = auto);
    }

    // Hyphenation dropdown — Manual: a simpler pass that proposes hyphenation points for long words. FreeW's
    // editor uses the same pure Hyphenator the automatic mode does; "Manual" turns hyphenation on (so the
    // proposed soft-hyphen break points render) and reports how many words it found break candidates for, so
    // the user can see the pass ran. (Word's interactive per-break confirmation UI is out of scope.)
    private sealed class HyphenationManualCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.CommitToModel();
            var candidates = CountHyphenationCandidates(editor.Model);
            editor.ApplyPageSettings(page => page.AutoHyphenation = true);

            var owner = Window.GetWindow(editor);
            if (owner is not null)
                DialogMessageHelper.ShowInfo(owner,
                    candidates == 0
                        ? "Manual hyphenation found no long words to hyphenate."
                        : $"Manual hyphenation proposed break points for {candidates} word(s). They will hyphenate at line ends.",
                    "Hyphenation");
        }

        // Count distinct word occurrences in the live document that the pure Hyphenator would break.
        private static int CountHyphenationCandidates(TextDocument model)
        {
            var count = 0;
            foreach (var block in model.Blocks)
                if (block is FreeW.Core.Model.Paragraph { Formatting.SuppressAutoHyphens: false } paragraph)
                    foreach (var run in paragraph.Runs)
                        foreach (var token in run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (Hyphenator.BreakPoints(token.Trim('(', ')', ',', '.', ';', ':', '"', '\'')).Count > 0)
                                count++;
            return count;
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

            editor.ApplyPageSettings(page =>
            {
                page.AutoHyphenation = result.AutoHyphenation;
                page.HyphenationZonePt = result.ZonePt;
                page.ConsecutiveHyphenLimit = result.ConsecutiveLimit;
                page.DoNotHyphenateCaps = !result.HyphenateCaps;
            });
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

    // Picture Format > Arrange > Z-order: bring/send a floating image forward or to front/back.
    private sealed class ImageZOrderCommand(DocumentView editor, ZOrderOperation operation) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null || !image.IsFloating)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    "Select a floating picture first.", "Z-Order");
                return;
            }
            editor.ChangeSelectedImageZOrder(operation);
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

    // Insert > Symbols > Date & Time: list formatted current date/time strings; insert the chosen one.
    private sealed class InsertDateTimeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = DateTimeDialog.Prompt(Window.GetWindow(editor));
            if (!string.IsNullOrEmpty(text))
                editor.InsertText(text);
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

    // References > Footnotes > Show Notes (backed toggle variant): shows or hides the docked Notes pane.
    // Replaces the read-only NotesListDialog when the host passes toggle callbacks through Build().
    private sealed class ToggleNotesPaneCommand(
        DocumentView editor,
        Action onToggle,
        Func<bool> isVisible) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            onToggle();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: isVisible());
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
    // new-source form. A new source is appended to the model, then its in-text citation is inserted.
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

        // Show the new-source form, append the captured source to the model, and return it (or null if
        // the user cancelled or left every field blank — nothing worth citing).
        private Source? PromptForNewSource(Window? owner)
        {
            var entry = NewSourceDialog.Ask(owner);
            if (entry is null)
                return null;
            if (entry.Author.Length == 0 && entry.Title.Length == 0 && entry.Year.Length == 0)
                return null;
            return editor.AddSource(entry.Tag, entry.Author, entry.Title, entry.Year, entry.Publisher);
        }
    }

    // References > Citations & Bibliography > Manage Sources: edit the document-local source list.
    // Word also has a master source list; FreeW currently backs the document source store only, so the
    // dialog labels that scope directly instead of exposing a fake global library.
    private sealed class ManageSourcesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var sources = ManageSourcesDialog.Ask(Window.GetWindow(editor), editor.Sources);
            if (sources is null)
                return;

            editor.Focus();
            editor.ReplaceSources(sources);
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
            var defaultLabel = editor.IsCaretInTable() ? CaptionLabel.Table : CaptionLabel.Figure;

            var label = CaptionLabelPicker.Ask(owner, defaultLabel);
            if (label is null)
                return; // cancelled

            var text = TextPrompt.Ask(owner, "Insert Caption", "Caption text (optional):", string.Empty);
            if (text is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertCaption(label.Value, text.Trim());
        }
    }

    // A tiny modal dialog choosing the caption label (Figure or Table), seeded with a default. Returns
    // the chosen label, or null if cancelled.
    private static class CaptionLabelPicker
    {
        public static CaptionLabel? Ask(Window? owner, CaptionLabel defaultLabel)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 12)
            };
            list.Items.Add(CaptionLabel.Figure);
            list.Items.Add(CaptionLabel.Table);
            list.SelectedItem = defaultLabel;

            CaptionLabel? result = null;
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
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Choose()
            {
                if (list.SelectedItem is CaptionLabel chosen)
                {
                    result = chosen;
                    dialog.DialogResult = true;
                }
            }
            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
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

    // Review > Tracking > Track Changes: a stateful toggle over the editor's Track Changes mode. Live
    // keystroke tracking is out of scope in a RichTextBox, so as a pragmatic gesture, turning the toggle
    // ON with a non-empty selection marks that selection as a tracked insertion (so the feature does
    // something visible and the round-trip is exercisable from the UI). The author comes from the
    // document Author property (falling back to the OS user); the date is stamped at mark time.
    private sealed class TrackChangesToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.TrackChangesEnabled = !editor.TrackChangesEnabled;

            // When switching ON over a non-empty selection, mark it as an insertion as a stand-in for
            // live tracking. This keeps the toggle useful without brittle per-keystroke interception.
            if (editor.TrackChangesEnabled && !editor.Selection.IsEmpty)
            {
                var author = editor.Model.Properties.Author;
                if (string.IsNullOrWhiteSpace(author))
                    author = Environment.UserName;
                author = author?.Trim() ?? string.Empty;

                var dateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                editor.MarkSelectionAsRevision(RevisionKind.Inserted, author, dateXml);
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.TrackChangesEnabled);
    }

    // Review > Tracking > Display for Review: exposes the MarkupDisplayMode dropdown. The root button
    // and the "All Markup" menu item both set AllMarkup mode; No Markup and Original are now implemented
    // using a transparent-run technique that keeps every revision run in the WPF tree so CommitToModel
    // can round-trip text + RevisionMarker safely (data-loss risk is closed).
    private sealed class DisplayForReviewCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.AllMarkup);
        }

        // The root button IsChecked is true when in All Markup (the default), matching Word's convention.
        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == DocumentView.MarkupDisplayMode.AllMarkup);
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
            editor.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.SimpleMarkup);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == DocumentView.MarkupDisplayMode.SimpleMarkup);
    }

    // Review > Tracking > Display for Review > No Markup: insertions shown as plain text; deleted runs
    // rendered invisible (transparent foreground + near-zero font size). RevisionMarker is always written
    // so text + revision kind/author/date survive CommitToModel unchanged (round-trip safe).
    private sealed class DisplayForReviewNoMarkupCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.NoMarkup);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == DocumentView.MarkupDisplayMode.NoMarkup);
    }

    // Review > Tracking > Display for Review > Original: deleted runs shown as plain text; inserted runs
    // rendered invisible (same transparent technique as No Markup). Round-trip safe via RevisionMarker.
    private sealed class DisplayForReviewOriginalCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.Original);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.DisplayForReview == DocumentView.MarkupDisplayMode.Original);
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

                var compared = DocumentCompare.Compare(original, revised, picked.Author, dateXml);
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
    private sealed class MarkCitationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var citation = MarkCitationDialog.Ask(Window.GetWindow(editor), seed);
            if (citation is null)
                return; // cancelled or empty — nothing to mark
            editor.MarkCitation(citation);
        }
    }

    // A small modal form capturing a citation's category, long form and short form. Returns the citation,
    // or null if cancelled (or if the long form is left blank).
    private static class MarkCitationDialog
    {
        public static Citation? Ask(Window? owner, string seedLong)
        {
            var category = new System.Windows.Controls.ComboBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var value in System.Enum.GetValues<CitationCategory>())
                category.Items.Add(new CategoryItem(value));
            category.SelectedIndex = 0;

            var longForm = new System.Windows.Controls.TextBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10), Text = seedLong };
            var shortForm = new System.Windows.Controls.TextBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };

            Citation? result = null;
            var dialog = new Window
            {
                Title = "Mark Citation",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Mark", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                var longText = longForm.Text.Trim();
                if (longText.Length == 0)
                    return; // nothing to mark — keep the dialog open
                var chosen = (category.SelectedItem as CategoryItem)?.Value ?? CitationCategory.Cases;
                result = new Citation(longText, chosen, shortForm.Text.Trim());
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Category:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(category);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Selected text (long citation):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(longForm);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Short citation (optional):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(shortForm);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            longForm.Focus();
            longForm.SelectAll();
            return dialog.ShowDialog() == true ? result : null;
        }

        // Wraps a CitationCategory so the combo shows Word's friendly heading text (e.g. "Other Authorities").
        private sealed record CategoryItem(CitationCategory Value)
        {
            public override string ToString() => TableOfAuthorities.CategoryHeading(Value);
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

            library.Save(QuickPart.FromText(name.Trim(), text));
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
    private sealed record SourcePick(Source? Source, bool AddNew);

    // A tiny modal dialog to pick one of the document's existing sources, or to choose "Add New Source…".
    // Returns the pick, or null if cancelled.
    private static class SourcePicker
    {
        public static SourcePick? Ask(Window? owner, IReadOnlyList<Source> sources)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 320,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var source in sources)
                list.Items.Add(DescribeSource(source));
            list.SelectedIndex = 0;

            SourcePick? result = null;
            var dialog = new Window
            {
                Title = "Insert Citation",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Insert", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var addNew = new System.Windows.Controls.Button { Content = "Add New Source…", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            void Choose()
            {
                if (list.SelectedIndex >= 0 && list.SelectedIndex < sources.Count)
                {
                    result = new SourcePick(sources[list.SelectedIndex], AddNew: false);
                    dialog.DialogResult = true;
                }
            }

            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();
            addNew.Click += (_, _) => { result = new SourcePick(null, AddNew: true); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(addNew);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Source:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }

        // A short human-readable label for the picker list: "Author (Year) — Title", degrading gracefully.
        private static string DescribeSource(Source source)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(source.Author))
                parts.Add(source.Author.Trim());
            if (!string.IsNullOrWhiteSpace(source.Year))
                parts.Add($"({source.Year.Trim()})");
            var head = string.Join(" ", parts);
            if (!string.IsNullOrWhiteSpace(source.Title))
                head = head.Length > 0 ? $"{head} — {source.Title.Trim()}" : source.Title.Trim();
            if (head.Length == 0)
                head = string.IsNullOrWhiteSpace(source.Tag) ? "(untitled source)" : source.Tag.Trim();
            return head;
        }
    }

    // The fields captured by the NewSourceDialog (all trimmed; publisher may be empty).
    private sealed record SourceEntry(string Tag, string Author, string Title, string Year, string Publisher);

    // A small modal form capturing a source's tag/author/title/year/publisher. Returns the entry, or
    // null if cancelled. When editing an existing source, type-specific fields not shown here are
    // preserved by the caller.
    private static class NewSourceDialog
    {
        public static SourceEntry? Ask(Window? owner, Source? source = null)
        {
            var tag = NewField(source?.Tag);
            var author = NewField(source?.Author);
            var title = NewField(source?.Title);
            var year = NewField(source?.Year);
            var publisher = NewField(source?.Publisher);

            SourceEntry? result = null;
            var dialog = new Window
            {
                Title = source is null ? "Add New Source" : "Edit Source",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = new SourceEntry(
                    tag.Text.Trim(), author.Text.Trim(), title.Text.Trim(), year.Text.Trim(), publisher.Text.Trim());
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
            AddRow(panel, "Tag (short id):", tag);
            AddRow(panel, "Author:", author);
            AddRow(panel, "Title:", title);
            AddRow(panel, "Year:", year);
            AddRow(panel, "Publisher (optional):", publisher);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            author.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox NewField(string? value = null) =>
            new() { Text = value ?? string.Empty, MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };

        private static void AddRow(System.Windows.Controls.Panel panel, string label, System.Windows.Controls.TextBox box)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
        }
    }

    private static class ManageSourcesDialog
    {
        public static IReadOnlyList<Source>? Ask(Window? owner, IReadOnlyList<Source> sources)
        {
            var working = sources.Select(CloneSource).ToList();
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 440,
                MinHeight = 180,
                Margin = new Thickness(0, 0, 0, 12)
            };

            IReadOnlyList<Source>? result = null;
            var dialog = new Window
            {
                Title = "Manage Sources",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            void RefreshList()
            {
                var selected = list.SelectedIndex;
                list.Items.Clear();
                foreach (var source in working)
                    list.Items.Add(DescribeSource(source));
                if (working.Count > 0)
                    list.SelectedIndex = Math.Clamp(selected, 0, working.Count - 1);
            }

            void AddSource()
            {
                var entry = NewSourceDialog.Ask(dialog);
                if (entry is null || !HasSourceData(entry))
                    return;
                working.Add(BuildSource(entry));
                RefreshList();
                list.SelectedIndex = working.Count - 1;
            }

            void EditSource()
            {
                var index = list.SelectedIndex;
                if (index < 0 || index >= working.Count)
                    return;
                var entry = NewSourceDialog.Ask(dialog, working[index]);
                if (entry is null || !HasSourceData(entry))
                    return;
                working[index] = BuildSource(entry, working[index]);
                RefreshList();
                list.SelectedIndex = index;
            }

            void DeleteSource()
            {
                var index = list.SelectedIndex;
                if (index < 0 || index >= working.Count)
                    return;
                working.RemoveAt(index);
                RefreshList();
            }

            var add = new System.Windows.Controls.Button { Content = "Add...", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var edit = new System.Windows.Controls.Button { Content = "Edit...", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var delete = new System.Windows.Controls.Button { Content = "Delete", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            add.Click += (_, _) => AddSource();
            edit.Click += (_, _) => EditSource();
            delete.Click += (_, _) => DeleteSource();
            list.MouseDoubleClick += (_, _) => EditSource();
            ok.Click += (_, _) =>
            {
                result = working.ToArray();
                dialog.DialogResult = true;
            };

            var editButtons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            editButtons.Children.Add(add);
            editButtons.Children.Add(edit);
            editButtons.Children.Add(delete);

            var closeButtons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButtons.Children.Add(ok);
            closeButtons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Current document sources:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(editButtons);
            panel.Children.Add(closeButtons);
            dialog.Content = panel;

            RefreshList();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static bool HasSourceData(SourceEntry entry) =>
            entry.Tag.Length > 0
            || entry.Author.Length > 0
            || entry.Title.Length > 0
            || entry.Year.Length > 0
            || entry.Publisher.Length > 0;

        private static Source BuildSource(SourceEntry entry, Source? existing = null) =>
            new()
            {
                Tag = entry.Tag,
                Type = existing?.Type ?? SourceType.Book,
                Author = entry.Author,
                Title = entry.Title,
                Year = entry.Year,
                Publisher = string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher,
                Journal = existing?.Journal,
                Volume = existing?.Volume,
                Issue = existing?.Issue,
                Pages = existing?.Pages,
                Url = existing?.Url,
                Accessed = existing?.Accessed
            };

        private static Source CloneSource(Source source) =>
            new()
            {
                Tag = source.Tag,
                Type = source.Type,
                Author = source.Author,
                Title = source.Title,
                Year = source.Year,
                Publisher = source.Publisher,
                Journal = source.Journal,
                Volume = source.Volume,
                Issue = source.Issue,
                Pages = source.Pages,
                Url = source.Url,
                Accessed = source.Accessed
            };

        private static string DescribeSource(Source source)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(source.Author))
                parts.Add(source.Author.Trim());
            if (!string.IsNullOrWhiteSpace(source.Year))
                parts.Add($"({source.Year.Trim()})");
            var head = string.Join(" ", parts);
            if (!string.IsNullOrWhiteSpace(source.Title))
                head = head.Length > 0 ? $"{head} - {source.Title.Trim()}" : source.Title.Trim();
            if (head.Length == 0)
                head = string.IsNullOrWhiteSpace(source.Tag) ? "(untitled source)" : source.Tag.Trim();
            return head;
        }
    }

    // Mailings: the shared mail-merge state across the four Mailings commands. Holds the data source
    // and, while previewing, the original template document plus the current record index so previewing
    // can step through records and restore the template when the preview ends.
    private sealed class MailMergeSession
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

    // Mailings > Rules (special fields): insert «Next Record» or «Merge Record #» as a plain placeholder.
    // The engine's SubstituteSpecial path recognises these names and handles them at merge time.
    private sealed class InsertSpecialMergeFieldCommand(DocumentView editor, string fieldName) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}");
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
    private sealed class FinishMergeCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { Count: > 0 } data)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Select recipients first (Mailings > Select Recipients), then Finish & Merge.",
                    "Mail Merge");
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

            // Augment every row with the composed «AddressBlock» and «GreetingLine» values so composite
            // placeholders in the template resolve correctly across every record.
            var augmentedRows = data.Rows.Select(r => session.AugmentRow(r)).ToList();
            var augmentedData = new MergeData(data.Header, augmentedRows.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());
            var merged = MailMerge.MergeAll(template, augmentedData);
            var combined = MailMerge.CombineMergedRecords(merged, session.Mode);

            editor.LoadModel(combined);
            session.Template = null;
            session.CurrentIndex = 0;

            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                $"Merged {merged.Count} record(s) into a single document.",
                "Mail Merge");
            editor.Focus();
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

            var result = FilterSortRecipientsDialog.Ask(Window.GetWindow(editor), data);
            if (result is null)
                return; // cancelled

            // Rebuild the session data from the user's chosen (possibly re-ordered) rows. The MergeData
            // constructor takes the same header and an enumerable of rows, so no model change is needed.
            session.Data = new MergeData(data.Header, result.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());
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
                page.Landscape = true;
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
    // table grid (rows × columns) via editor.InsertTable so each cell is one label. The session parameter
    // is accepted for future merge-aware population (populate each cell via MailMerge.MergeRecord); for
    // now the grid is inserted blank so the user can type or let Preview/Finish fill it.
    private sealed class LabelsCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            // session is available for future merge-aware cell population (deferred; see report).
            _ = session;

            if (LabelSetupDialog.Ask(Window.GetWindow(editor)) is not { } label)
                return; // cancelled

            // Apply the label-sheet page geometry first so the table fits the physical sheet.
            editor.ApplyPageSettings(page =>
            {
                page.WidthPt        = label.PageWidthPt;
                page.HeightPt       = label.PageHeightPt;
                page.Landscape      = false;
                page.MarginLeftPt   = label.MarginPt;
                page.MarginRightPt  = label.MarginPt;
                page.MarginTopPt    = label.MarginPt;
                page.MarginBottomPt = label.MarginPt;
            });

            // Insert the label grid — the editor routes this through the undo/redo bus.
            editor.InsertTable(label.Rows, label.Columns);
            editor.Focus();
        }
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
        private static readonly FieldRole[] AllRoles = (FieldRole[])Enum.GetValues(typeof(FieldRole));

        // Display labels for each role, matching Word's "Match Fields" dialog wording.
        private static readonly Dictionary<FieldRole, string> RoleLabels = new()
        {
            [FieldRole.Title]      = "Title (Mr., Mrs., …)",
            [FieldRole.FirstName]  = "First Name",
            [FieldRole.MiddleName] = "Middle Name",
            [FieldRole.LastName]   = "Last Name",
            [FieldRole.Suffix]     = "Suffix (Jr., Sr., …)",
            [FieldRole.Company]    = "Company",
            [FieldRole.Address1]   = "Address 1",
            [FieldRole.Address2]   = "Address 2",
            [FieldRole.City]       = "City",
            [FieldRole.State]      = "State",
            [FieldRole.PostalCode] = "Postal Code",
            [FieldRole.Country]    = "Country or Region",
        };

        public static FieldMapping? Ask(Window? owner, IReadOnlyList<string> header, FieldMapping current)
        {
            FieldMapping? result = null;

            // One ComboBox per role, keyed by role.
            var combos = new Dictionary<FieldRole, System.Windows.Controls.ComboBox>();

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var i = 0; i < AllRoles.Length + 1; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < AllRoles.Length; i++)
            {
                var role = AllRoles[i];
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = RoleLabels.TryGetValue(role, out var lbl) ? lbl : role.ToString(),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 3, 12, 3)
                };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var combo = new System.Windows.Controls.ComboBox { MinWidth = 180, Margin = new Thickness(0, 3, 0, 3) };
                combo.Items.Add("(not matched)");
                foreach (var h in header)
                    combo.Items.Add(h);

                // Pre-select the currently mapped column (or "(not matched)").
                var mapped = current[role];
                if (mapped is not null && header.Contains(mapped, StringComparer.OrdinalIgnoreCase))
                    combo.SelectedItem = header.First(h => h.Equals(mapped, StringComparison.OrdinalIgnoreCase));
                else
                    combo.SelectedIndex = 0;

                combos[role] = combo;
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
            Grid.SetRow(buttonRow, AllRoles.Length);
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
                var mapping = new FieldMapping();
                foreach (var (role, combo) in combos)
                {
                    var sel = combo.SelectedItem as string;
                    mapping[role] = sel == "(not matched)" || sel is null ? null : sel;
                }
                result = mapping;
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
        // Returns the filtered, ordered rows as dictionaries, or null if cancelled.
        public static IReadOnlyList<IReadOnlyDictionary<string, string>>? Ask(
            Window? owner, MergeData data)
        {
            IReadOnlyList<IReadOnlyDictionary<string, string>>? result = null;

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
            // Show up to the first 8 columns as preview text so the dialog stays a reasonable width.
            const int MaxPreviewCols = 8;
            var previewCols = data.Header.Take(MaxPreviewCols).ToList();

            var rowChecks = new List<System.Windows.Controls.CheckBox>();
            var rowList = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Header hint
            var headerHint = new System.Windows.Controls.TextBlock
            {
                Text = "  " + string.Join("  |  ", previewCols),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = Brushes.Gray
            };
            rowList.Children.Add(headerHint);

            for (var i = 0; i < data.Rows.Count; i++)
            {
                var row = data.Rows[i];
                var preview = string.Join("  |  ", previewCols.Select(h => row.TryGetValue(h, out var v) ? v : string.Empty));
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = $"{i + 1}. {preview}",
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
                // Collect the indices of checked rows, then sort by the chosen column.
                var sortCol  = sortColCombo.SelectedItem as string ?? string.Empty;
                var ascending = ascRadio.IsChecked == true;

                var chosen = rowChecks
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => data.Rows[(int)cb.Tag!]);

                result = (ascending
                    ? chosen.OrderBy(r => r.TryGetValue(sortCol, out var v) ? v : string.Empty, StringComparer.OrdinalIgnoreCase)
                    : chosen.OrderByDescending(r => r.TryGetValue(sortCol, out var v) ? v : string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList();

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

    // Standard envelope size preset for the Envelopes command.
    private readonly record struct EnvelopeSize(string Name, double WidthPt, double HeightPt, double MarginPt);

    // Result returned by EnvelopeSetupDialog.
    private readonly record struct EnvelopeSetupResult(double WidthPt, double HeightPt, double MarginPt);

    // Mailings > Envelopes setup dialog. Offers a small set of standard ISO/US sizes (DL, C5, C6,
    // Comm-10, Monarch) matching Word's Envelopes and Labels dialog. Returns the chosen geometry, or null
    // if cancelled. The caller applies the settings via ApplyPageSettings (backed path).
    private static class EnvelopeSetupDialog
    {
        // Standard sizes as portrait dimensions (width × height in points). Landscape is applied by the
        // command so the long edge runs horizontally, matching Word's envelope-print orientation.
        // 1 mm = 72/25.4 pt ≈ 2.8346 pt.
        private static readonly EnvelopeSize[] Sizes =
        [
            new("DL  (110 × 220 mm)",  110 * 72 / 25.4,  220 * 72 / 25.4, 18),
            new("C5  (162 × 229 mm)",  162 * 72 / 25.4,  229 * 72 / 25.4, 18),
            new("C6  (114 × 162 mm)",  114 * 72 / 25.4,  162 * 72 / 25.4, 14),
            new("Comm-10 (4.125 × 9.5 in)", 4.125 * 72, 9.5 * 72,        18),
            new("Monarch (3.875 × 7.5 in)", 3.875 * 72, 7.5 * 72,        14),
        ];

        public static EnvelopeSetupResult? Ask(Window? owner)
        {
            EnvelopeSetupResult? result = null;

            var combo = new System.Windows.Controls.ComboBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var s in Sizes)
                combo.Items.Add(s.Name);
            combo.SelectedIndex = 0; // default: DL

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
                var s = Sizes[combo.SelectedIndex];
                result = new EnvelopeSetupResult(s.WidthPt, s.HeightPt, s.MarginPt);
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

    // Standard label sheet preset for the Labels command.
    private readonly record struct LabelPreset(string Name, int Rows, int Columns, double PageWidthPt, double PageHeightPt, double MarginPt);

    // Result returned by LabelSetupDialog.
    private readonly record struct LabelSetupResult(int Rows, int Columns, double PageWidthPt, double PageHeightPt, double MarginPt);

    // Mailings > Labels setup dialog. Offers a handful of common Avery-style presets plus a custom
    // rows × columns option on US Letter. Returns the chosen grid / page geometry, or null if cancelled.
    // The caller applies page settings via ApplyPageSettings then inserts the grid via InsertTable.
    private static class LabelSetupDialog
    {
        // A curated set of common label layouts on standard sheets.  Dimensions: US Letter = 612 × 792 pt.
        private static readonly LabelPreset[] Presets =
        [
            new("Avery 5160 — 3 × 10 (Letter)",  10, 3, 612, 792, 18),
            new("Avery 5162 — 2 × 7  (Letter)",   7, 2, 612, 792, 18),
            new("Avery 5163 — 2 × 5  (Letter)",   5, 2, 612, 792, 18),
            new("Avery L7160 — 3 × 7 (A4)",        7, 3, 595.28, 841.89, 14),
            new("Custom rows × columns (Letter)",   0, 0, 612, 792, 18),
        ];

        private const int CustomPresetIndex = 4;

        public static LabelSetupResult? Ask(Window? owner)
        {
            LabelSetupResult? result = null;

            var combo = new System.Windows.Controls.ComboBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var p in Presets)
                combo.Items.Add(p.Name);
            combo.SelectedIndex = 0;

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
                customPanel.Visibility = combo.SelectedIndex == CustomPresetIndex ? Visibility.Visible : Visibility.Collapsed;

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
                var idx = combo.SelectedIndex;
                if (idx == CustomPresetIndex)
                {
                    if (!int.TryParse(rowsBox.Text, out var rows) || rows < 1 ||
                        !int.TryParse(colsBox.Text, out var cols) || cols < 1)
                    {
                        DialogMessageHelper.ShowError(dialog, "Enter valid positive integers for rows and columns.");
                        return;
                    }
                    result = new LabelSetupResult(rows, cols, Presets[idx].PageWidthPt, Presets[idx].PageHeightPt, Presets[idx].MarginPt);
                }
                else
                {
                    var p = Presets[idx];
                    result = new LabelSetupResult(p.Rows, p.Columns, p.PageWidthPt, p.PageHeightPt, p.MarginPt);
                }
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
    private sealed class HeaderFooterCommand(DocumentView editor, bool isFooter) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var model = editor.Model;
            var existing = isFooter ? model.Footer : model.Header;
            var seed = existing?.PlainText ?? string.Empty;
            var label = isFooter ? "Footer" : "Header";

            var text = TextPrompt.Ask(Window.GetWindow(editor), $"Edit {label}", $"{label} text:", seed);
            if (text is null)
                return; // cancelled — leave the model untouched

            var hadPageNumber = existing?.Paragraphs.SelectMany(p => p.Runs)
                .Any(r => r.FieldKind == RunFieldKind.PageNumber) ?? false;

            HeaderFooter? value;
            if (text.Length == 0 && !hadPageNumber)
            {
                value = null;
            }
            else
            {
                value = new HeaderFooter();
                var paragraph = new FreeW.Core.Model.Paragraph();
                if (text.Length > 0)
                    paragraph.Runs.Add(new FreeW.Core.Model.Run(text));
                if (hadPageNumber)
                {
                    if (paragraph.Runs.Count > 0)
                        paragraph.Runs.Add(new FreeW.Core.Model.Run("  "));
                    paragraph.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                }
                value.Paragraphs.Add(paragraph);
            }

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
            var hf = editor.Model.FinalSectionHeadersFooters;
            var page = editor.Model.Page;

            // Warn if the slot requires a toggle that is currently off (same guard as EditHeaderSlotCommand).
            var label = slotName switch
            {
                "header"       => "Default Header",
                "footer"       => "Default Footer",
                "even-header"  => "Even-Page Header",
                "even-footer"  => "Even-Page Footer",
                "first-header" => "First-Page Header",
                "first-footer" => "First-Page Footer",
                _              => slotName
            };

            if (slotName is "even-header" or "even-footer" && !page.DifferentOddEvenPages)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    $"'{label}' is only active when 'Different Odd & Even Pages' is turned on.\n" +
                    "Enable that option in Header & Footer Design, then try again.",
                    "Edit Header / Footer");
                return;
            }
            if (slotName is "first-header" or "first-footer" && !page.DifferentFirstPage)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    $"'{label}' is only active when 'Different First Page' is turned on.\n" +
                    "Enable that option in Header & Footer Design, then try again.",
                    "Edit Header / Footer");
                return;
            }

            openPane(slotName);
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

            // Resolve the current slot value by name.
            var current = slotName switch
            {
                "header"       => hf.Header,
                "footer"       => hf.Footer,
                "even-header"  => hf.EvenHeader,
                "even-footer"  => hf.EvenFooter,
                "first-header" => hf.FirstHeader,
                "first-footer" => hf.FirstFooter,
                _              => null
            };

            var label = slotName switch
            {
                "header"       => "Default Header",
                "footer"       => "Default Footer",
                "even-header"  => "Even-Page Header",
                "even-footer"  => "Even-Page Footer",
                "first-header" => "First-Page Header",
                "first-footer" => "First-Page Footer",
                _              => slotName
            };

            // Warn if the slot requires a toggle that is currently off.
            if (slotName is "even-header" or "even-footer" && !page.DifferentOddEvenPages)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    $"'{label}' is only active when 'Different Odd & Even Pages' is turned on.\n" +
                    "Enable that option in Header & Footer Design, then try again.",
                    "Edit Header / Footer");
                return;
            }
            if (slotName is "first-header" or "first-footer" && !page.DifferentFirstPage)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    $"'{label}' is only active when 'Different First Page' is turned on.\n" +
                    "Enable that option in Header & Footer Design, then try again.",
                    "Edit Header / Footer");
                return;
            }

            var result = HeaderFooterSlotDialog.Prompt(Window.GetWindow(editor), label, current);
            if (result is null)
                return; // cancelled

            // Write back to the correct slot.
            switch (slotName)
            {
                case "header":       hf.Header      = result; break;
                case "footer":       hf.Footer      = result; break;
                case "even-header":  hf.EvenHeader  = result; break;
                case "even-footer":  hf.EvenFooter  = result; break;
                case "first-header": hf.FirstHeader = result; break;
                case "first-footer": hf.FirstFooter = result; break;
            }

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
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
                editor.ApplyPageSettings(page => page.HeaderDistancePt = pt);
        }

        public RibbonCommandState GetState() =>
            new(Value: editor.Model.Page.HeaderDistancePt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class FooterFromBottomCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var pt) && pt >= 0)
                editor.ApplyPageSettings(page => page.FooterDistancePt = pt);
        }

        public RibbonCommandState GetState() =>
            new(Value: editor.Model.Page.FooterDistancePt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
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
            var slot = hf ?? new HeaderFooter();

            switch (kind)
            {
                case InsertSlotKind.PageNumber:
                {
                    var alreadyPresent = slot.Paragraphs.SelectMany(p => p.Runs)
                        .Any(r => r.FieldKind == RunFieldKind.PageNumber);
                    if (!alreadyPresent)
                    {
                        var para = new FreeW.Core.Model.Paragraph
                        {
                            Formatting = ParagraphFormatting.Default with
                            {
                                Alignment = FreeW.Core.Model.TextAlignment.Center
                            }
                        };
                        para.Runs.Add(new FreeW.Core.Model.Run("Page "));
                        para.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                        slot.Paragraphs.Add(para);
                    }
                    break;
                }
                case InsertSlotKind.DateTime:
                {
                    var text = DateTimeDialog.Prompt(Window.GetWindow(editor));
                    if (string.IsNullOrEmpty(text))
                        return;
                    var para = EnsureDefaultParagraph(slot);
                    para.Runs.Add(new FreeW.Core.Model.Run(text));
                    break;
                }
                case InsertSlotKind.DocumentInfo:
                {
                    var instruction = FieldPickerDialog.Ask(Window.GetWindow(editor));
                    if (instruction is null)
                        return;
                    var para = EnsureDefaultParagraph(slot);
                    para.Runs.Add(FreeW.Core.Model.Run.ComplexFieldRun(instruction));
                    break;
                }
            }

            if (isFooter)
                model.Footer = slot;
            else
                model.Header = slot;

            editor.Focus();
        }

        private static FreeW.Core.Model.Paragraph EnsureDefaultParagraph(HeaderFooter hf)
        {
            if (hf.Paragraphs.Count == 0)
                hf.Paragraphs.Add(new FreeW.Core.Model.Paragraph());
            return hf.Paragraphs[^1];
        }
    }

    private enum InsertSlotKind { PageNumber, DateTime, DocumentInfo }

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
        public static HeaderFooter? Prompt(Window? owner, string slotLabel, HeaderFooter? current)
        {
            // Seed the text box with the slot's plain text (if any).
            var seed = current?.PlainText ?? string.Empty;
            var hadPageNumber = current?.Paragraphs.SelectMany(p => p.Runs)
                .Any(r => r.FieldKind == RunFieldKind.PageNumber) ?? false;
            var hadComplexField = current?.Paragraphs.SelectMany(p => p.Runs)
                .Any(r => r.ComplexField is not null) ?? false;

            // Track whether the user wants to append a page-number or date/time.
            bool appendPageNumber = hadPageNumber;
            string? appendDateTime = null;
            string? appendFieldInstruction = null;

            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
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
                IsEnabled = !appendPageNumber
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
                var text = DateTimeDialog.Prompt(owner);
                if (!string.IsNullOrEmpty(text))
                    appendDateTime = text;
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
                var text = box.Text;
                HeaderFooter? hf;
                if (text.Length == 0 && !appendPageNumber && appendDateTime is null && appendFieldInstruction is null)
                {
                    hf = null; // clear slot
                }
                else
                {
                    hf = new HeaderFooter();
                    var para = new FreeW.Core.Model.Paragraph();
                    if (text.Length > 0)
                        para.Runs.Add(new FreeW.Core.Model.Run(text));
                    if (appendDateTime is { } dt)
                    {
                        if (para.Runs.Count > 0)
                            para.Runs.Add(new FreeW.Core.Model.Run("  "));
                        para.Runs.Add(new FreeW.Core.Model.Run(dt));
                    }
                    if (appendFieldInstruction is { } instr)
                    {
                        if (para.Runs.Count > 0)
                            para.Runs.Add(new FreeW.Core.Model.Run("  "));
                        para.Runs.Add(FreeW.Core.Model.Run.ComplexFieldRun(instr));
                    }
                    if (appendPageNumber)
                    {
                        if (para.Runs.Count > 0)
                            para.Runs.Add(new FreeW.Core.Model.Run("  "));
                        para.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                    }
                    hf.Paragraphs.Add(para);
                }
                result = hf;
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
            return dialog.ShowDialog() == true ? result : current; // Cancel = unchanged
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
                Title = "Page Color",
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
                Content = "No Color",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            noColor.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(noColor);

            var more = new Button
            {
                Content = "More Colors…",
                Margin = new Thickness(2, 4, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            more.Click += (_, _) =>
            {
                var seed = editor.Model.Page.BackgroundColorHex ?? "#";
                var typed = TextPrompt.Ask(window, "More Colors", "Hex colour (e.g. #FFCC00):", seed);
                if (typed is null)
                    return; // stay on the palette
                var normalized = NormalizeHex(typed);
                if (normalized is null)
                {
                    DialogMessageHelper.ShowWarning(window, "Enter a colour as a 6-digit hex value, e.g. #FFCC00.", "Page Color");
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
                var header = model.Header ?? new HeaderFooter();
                var alreadyPresent = header.Paragraphs.SelectMany(p => p.Runs)
                    .Any(r => r.FieldKind == RunFieldKind.PageNumber);
                if (!alreadyPresent)
                {
                    var paragraph = new FreeW.Core.Model.Paragraph
                    {
                        Formatting = ParagraphFormatting.Default with { Alignment = FreeW.Core.Model.TextAlignment.Center }
                    };
                    paragraph.Runs.Add(new FreeW.Core.Model.Run("Page "));
                    paragraph.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                    header.Paragraphs.Add(paragraph);
                }
                model.Header = header;
            }
            else
            {
                var footer = model.Footer ?? new HeaderFooter();
                var alreadyPresent = footer.Paragraphs.SelectMany(p => p.Runs)
                    .Any(r => r.FieldKind == RunFieldKind.PageNumber);
                if (!alreadyPresent)
                {
                    var paragraph = new FreeW.Core.Model.Paragraph
                    {
                        Formatting = ParagraphFormatting.Default with { Alignment = FreeW.Core.Model.TextAlignment.Center }
                    };
                    paragraph.Runs.Add(new FreeW.Core.Model.Run("Page "));
                    paragraph.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                    footer.Paragraphs.Add(paragraph);
                }
                model.Footer = footer;
            }
        }
    }

    // Insert > Header & Footer > Page Number > Format Page Numbers…: shows a simple dialog where the
    // user can set the starting page number (a common use case). For now shows an informational message
    // — a full format dialog (number style, chapter numbering, start-at) is out of scope for this wave.
    private sealed class PageNumberFormatCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                "Page number format options (number style, chapter numbering, start-at) are not yet implemented.",
                "Format Page Numbers");
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

    // A small modal dialog listing the insertable document field codes, grouped by category (Date and
    // Time / Document Information / Numbering). Returns the chosen raw field INSTRUCTION (e.g. " PAGE "),
    // or null if cancelled.
    private static class FieldPickerDialog
    {
        private sealed record Choice(string Label, string Instruction);

        public static string? Ask(Window? owner)
        {
            var choices = new[]
            {
                new Choice("Date", " DATE "),
                new Choice("Time", " TIME "),
                new Choice("File Name", " FILENAME "),
                new Choice("Author", " AUTHOR "),
                new Choice("Number of Pages (NumPages)", " NUMPAGES "),
                new Choice("Page Number (Page)", " PAGE "),
            };

            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                list.Items.Add(choice.Label);
            list.SelectedIndex = 0;

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

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Commit()
            {
                if (list.SelectedIndex >= 0)
                    result = choices[list.SelectedIndex].Instruction;
                dialog.DialogResult = true;
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

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 240 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Choose a field to insert:", Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(list);
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
        private sealed record Choice(string Label, char Delimiter);

        public static char? Ask(Window? owner, string title)
        {
            var choices = new[]
            {
                new Choice("Tab", '\t'),
                new Choice("Comma  ,", ','),
                new Choice("Semicolon  ;", ';'),
            };

            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 90,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                list.Items.Add(choice.Label);
            list.SelectedIndex = 0;

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
                if (index >= 0 && index < choices.Length)
                {
                    result = choices[index].Delimiter;
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Separate cells at:", Margin = new Thickness(0, 0, 0, 4) });
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
    // DocumentView.ActiveCitationStyle) so it persists and reformats subsequently inserted in-text citations
    // and bibliographies. Unrecognised labels leave the current style unchanged.
    private sealed class CitationStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;

            editor.ActiveCitationStyle = Citations.ParseStyle(value, editor.ActiveCitationStyle);
        }
    }

    private sealed class SelectionValueCommand(DocumentView editor, Action<TextSelection, string> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (context.Parameters.TryGetValue("value", out var raw) && raw is string value && value.Length > 0)
            {
                editor.Focus();
                apply(editor.Selection, value);
            }
        }
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
        Func<object?, bool> isOn) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
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
    private sealed class ShapeAlignCommand(DocumentView editor, FreeW.Core.Model.TextAlignment alignment) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedShape() is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Align");
                return;
            }
            editor.SetSelectedShapeAlignment(alignment);
        }
    }
}
