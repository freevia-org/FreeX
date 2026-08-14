using System.Globalization;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
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
/// Every ribbon command id declared by <see cref="FreeW.Ribbon.Definitions.FreeWRibbon"/> must have a
/// corresponding <see cref="RibbonCommandRegistry.Register"/> call here. Commands are grouped by
/// functional area (mirroring the ribbon tab/group structure) for readability.
/// </para>
///
/// <para>
/// <b>Design rule:</b> This file owns all Avalonia command wiring. Shell-level callbacks are routed
/// through the typed <see cref="FreeWRibbonHostExecutionPorts"/> record so that <c>MainWindow</c> stays thin.
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
///   <item><c>freew.font-color.*</c> — per-colour sub-commands registered from <see cref="FreeWRibbonDefinitionData.FontColors"/></item>
///   <item><c>freew.change-case</c> — open the shared five-choice Change Case picker</item>
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

    private static FreeWRibbonFormattingSession CreateFormattingSession(DocumentView editor) =>
        new(new FreeWRibbonFormattingPorts(
            () => editor.GetCaretFormatting().Paragraph,
            points => editor.SetIndents(leftPt: points),
            points => editor.SetIndents(rightPt: points),
            editor.SetSpaceBefore,
            editor.SetSpaceAfter,
            () => editor.Document,
            () => editor.CurrentParagraphStyleId,
            styleId => editor.ApplyNamedStyle(styleId),
            editor.ApplyTheme,
            editor.ApplyStyleSet));

    /// <summary>
    /// Build and return the complete command registry for the Avalonia ribbon.
    /// </summary>
    public static RibbonCommandRegistry Build(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks) =>
        Build(editor, callbacks, out _);

    /// <summary>
    /// Build the registry and also surface the <see cref="MailMergeEngine"/> that backs the Mailings tab
    /// (AV-MAIL), so the shell can drive its dialog-bound commands (Select Recipients / Insert Merge Field)
    /// with the async file-picker / prompt and keep the same session the ribbon commands use.
    /// </summary>
    public static RibbonCommandRegistry Build(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks, out MailMergeEngine mailMerge)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(callbacks);

        var r = new FreeWRibbonCommandBindingPorts();
        var tableCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        var referenceCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        var headerFooterCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        mailMerge = new MailMergeEngine(editor, callbacks, UiText.Get);
        var formatting = CreateFormattingSession(editor);
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
        FontEffectRibbonWorkflow.Register(r, CreateFontEffectPorts(editor));
        r.Bind(FreeWRibbonCommandAction.Highlight,        new ValueRibbonCommand(value => editor.SetHighlightColor(value)));
        RegisterHighlightPalette(r, editor);
        RegisterCharacterBorderPalette(r, editor);
        RegisterCharacterShadingPalette(r, editor);
        r.Bind(FreeWRibbonCommandAction.ClearFormatting, new ActionRibbonCommand(editor.ClearFormatting));
        // Font Color — the ribbon control is a Dropdown whose button click opens the colour flyout.
        // Each palette entry is its own command so the button never executes with a null value.
        // "freew.font-color" itself is registered as a no-op so the registry completeness check
        // (which checks every ribbon control's CommandId) continues to pass.
        r.Bind(FreeWRibbonCommandAction.FontColor, new ActionRibbonCommand(() => { /* flyout opener — no direct action */ }));
        RegisterFontColorPalette(r, editor);

        // Dialog launchers — open modal dialogs via shell callbacks (no direct editor method).

        // ── Paragraph ────────────────────────────────────────────────────────
        ParagraphEditingRibbonWorkflow.Register(r, CreateParagraphEditingPorts(editor, callbacks));
        MultilevelListRibbonWorkflow.Register(
            r,
            new MultilevelListRibbonPorts(
                editor.ApplyMultiLevelListDefinition,
                delta => ChangeListLevel(editor, demote: delta > 0),
                callbacks.OpenMultilevelListDialog));
        r.Bind(FreeWRibbonCommandAction.IndentLeft, new ParagraphValueCommand(
            formatting, FreeWParagraphValueKind.IndentLeft));
        r.Bind(FreeWRibbonCommandAction.IndentRight, new ParagraphValueCommand(
            formatting, FreeWParagraphValueKind.IndentRight));
        var formattingMarks = r.BindToggle(FreeWRibbonCommandAction.FormattingMarks,
            () => editor.ShowParagraphMarks = !editor.ShowParagraphMarks,
            () => editor.ShowParagraphMarks);
        r.Register("freew.show-hide-para", formattingMarks);
        // Paragraph spacing commands (value = points as an invariant-culture decimal string).
        r.Bind(FreeWRibbonCommandAction.SpaceBefore, new ParagraphValueCommand(
            formatting, FreeWParagraphValueKind.SpaceBefore));
        r.Bind(FreeWRibbonCommandAction.SpaceAfter, new ParagraphValueCommand(
            formatting, FreeWParagraphValueKind.SpaceAfter));
        r.Bind(FreeWRibbonCommandAction.ParaShading, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        RegisterParagraphShadingPalette(r, editor);
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
        r.Bind(FreeWRibbonCommandAction.Style, new ParagraphStyleCommand(formatting));
        foreach (var binding in FreeWRibbonSemanticCatalog.QuickStyles)
        {
            var captured = binding;
            r.Bind(captured.Action, new ActionRibbonCommand(() => editor.ApplyNamedStyle(captured.StyleId)));
        }

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
        TableInsertionRibbonWorkflow.Register(
            tableCommands,
            new TableInsertionRibbonPorts(editor.InsertTable));

        // Page break — empty paragraph forcing a page break before it, after the caret block.
        r.Bind(FreeWRibbonCommandAction.PageBreak, new ActionRibbonCommand(editor.InsertPageBreak));
        r.Bind(FreeWRibbonCommandAction.BlankPage, new ActionRibbonCommand(editor.InsertBlankPage));
        r.Bind(FreeWRibbonCommandAction.HorizontalRule, new ActionRibbonCommand(editor.InsertHorizontalRule));

        // Picture — open a file picker, load the bytes, insert as an inline image (host callback).

        // Shape / Text Box — floating drawing objects at the caret.
        InsertDrawingGalleryWorkflow.Register(
            r,
            new InsertDrawingGalleryPorts(editor.InsertShape));

        RegisterSymbolPalette(r, editor);

        // Header / Footer — match WPF's text prompt when the shell supplies it. The fallback keeps
        // headless registry callers deterministic and retains the old region-creation behavior.
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Header, HeaderFooterTextCommand(editor, callbacks, footer: false));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Footer, HeaderFooterTextCommand(editor, callbacks, footer: true));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumber, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberTop, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberBottom, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberCurrent, new ActionRibbonCommand(() => editor.InsertField(RunFieldKind.PageNumber)));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberFormat, new ContextRibbonCommand(
            context => ExecutePageNumberFormat(editor, callbacks, context)));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Datetime, new ActionRibbonCommand(
            callbacks.OpenDateTimeDialog ?? (() => editor.InsertField(RunFieldKind.Date))));
        ConfigureHeaderFooterCommandFamily(headerFooterCommands, editor);

        // ── Insert depth 2 (AV-INSERT2) ──────────────────────────────────────
        RegisterInsertDepth2Commands(r, editor, callbacks);

        // ── Developer ────────────────────────────────────────────────────────
        RegisterDeveloperControls(r, editor);

        // ── Table Design contextual tab ───────────────────────────────────────
        // Table Style Options toggles — DocumentView guards no-op when outside a table.
        TableEditingRibbonWorkflow.Register(tableCommands, CreateTableEditingPorts(editor));

        // Table shading: open the WPF-parity palette; the shell applies the chosen result only after
        // the user accepts a swatch or No Color. Closing the picker is a no-op.
        tableCommands.Register("freew.table-shading", OptionalHostCommand(callbacks.OpenCellShadingDialog));
        tableCommands.Register("freew.table-styles", new ActionRibbonCommand(() => { /* dropdown opener */ }));
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
        {
            var style = DocumentTableStyle.Catalog[index];
            tableCommands.Register(FreeWContextMenuPlanner.TableStylesPrefix + index,
                new ActionRibbonCommand(() => editor.ApplyTableStyle(style)));
        }

        // Borders dropdown — opener no-op; sub-commands apply specific edges.
        tableCommands.Register("freew.table-borders", new ActionRibbonCommand(() => { /* flyout opener */ }));
        RegisterTableBorderCommands(tableCommands, editor);
        tableCommands.Bind(FreeWRibbonCommandAction.Eraser, new ActionRibbonCommand(editor.EraseTableBorderAtCaret));

        // ── Table Layout contextual tab ───────────────────────────────────────
        // Row / column mutations.
        tableCommands.Register("freew.table-insert-below",     new ActionRibbonCommand(editor.InsertTableRowBelow));
        tableCommands.Register("freew.table-insert-col-right", new ActionRibbonCommand(editor.InsertTableColumnRight));

        // Merge / split.
        tableCommands.Register("freew.table-merge-cells", new ActionRibbonCommand(editor.MergeSelectedCells));
        tableCommands.Register("freew.table-split-cell", new ActionRibbonCommand(
            callbacks.OpenSplitCellDialog ?? (() => editor.SplitCurrentCell())));

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

        var columnsDialogCommand = OptionalHostCommand(callbacks.OpenColumnsDialog);
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
                PrintPreview: new ViewRibbonActionBinding(
                    callbacks.OpenPrintPreview,
                    ViewRibbonBindingAvailability.Disabled),
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: callbacks.ToggleReadMode is { } toggle && callbacks.IsReadModeActive is { } isActive
                        ? new ViewRibbonToggleBinding(toggle, isActive)
                        : new ViewRibbonToggleBinding(AvailabilityWhenUnbound: ViewRibbonBindingAvailability.Disabled),
                    ColumnWidth: new ViewRibbonChoiceBinding(
                        callbacks.ApplyReadModeColumnWidth,
                        ViewRibbonBindingAvailability.Disabled),
                    PageColor: new ViewRibbonChoiceBinding(
                        callbacks.ApplyReadModePageColor,
                        ViewRibbonBindingAvailability.Disabled)),
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
                        callbacks.SetOutlineView,
                        callbacks.IsOutlineViewActive,
                        ViewRibbonBindingAvailability.Disabled),
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
                    Dialog: new ViewRibbonActionBinding(callbacks.OpenZoomDialog, ViewRibbonBindingAvailability.Disabled),
                    ZoomIn: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(null, +0.1)),
                    ZoomOut: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(null, -0.1)),
                    Reset100: new ViewRibbonActionBinding(() => callbacks.ApplyZoom(1.0, 0)),
                    OnePage: new ViewRibbonActionBinding(callbacks.ZoomOnePage, ViewRibbonBindingAvailability.Disabled),
                    PageWidth: new ViewRibbonActionBinding(callbacks.ZoomPageWidth, ViewRibbonBindingAvailability.Disabled),
                    MultiplePages: new ViewRibbonToggleBinding(
                        callbacks.ToggleMultiplePages,
                        callbacks.IsMultiplePagesActive,
                        ViewRibbonBindingAvailability.Disabled),
                    SideToSide: new ViewRibbonToggleBinding(
                        callbacks.ToggleSideToSide,
                        callbacks.IsSideToSideActive,
                        ViewRibbonBindingAvailability.Disabled)),
                Window: new ViewRibbonWindowBindings(
                    NewWindow: new ViewRibbonActionBinding(callbacks.NewWindow, ViewRibbonBindingAvailability.Disabled),
                    ArrangeAll: new ViewRibbonActionBinding(callbacks.ArrangeAll, ViewRibbonBindingAvailability.Disabled),
                    Split: new ViewRibbonToggleBinding(
                        callbacks.ToggleSplit,
                        callbacks.IsSplitActive,
                        ViewRibbonBindingAvailability.Disabled)),
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
        r.Bind(FreeWRibbonCommandAction.ReadAloud,
            callbacks.ToggleReadAloud is { } toggleReadAloud
                ? new ToggleActionCommand(toggleReadAloud, callbacks.IsReadAloudActive ?? (() => false))
                : FreeWRibbonExecutionProfile.UnavailableCommand);
        r.Bind(FreeWRibbonCommandAction.MarkAsFinal, new ToggleActionCommand(
            callbacks.MarkAsFinal ?? (() => editor.SetMarkedAsFinal(!editor.IsMarkedAsFinal)),
            () => ReviewProtectionStatePlanner.Build(editor.Document.Protection, editor.IsMarkedAsFinal)
                .MarkAsFinal.IsChecked));
        r.Bind(FreeWRibbonCommandAction.RestrictEditing,
            callbacks.RestrictEditing is { } restrictEditing
                ? new ToggleActionCommand(
                    restrictEditing,
                    () => ReviewProtectionStatePlanner.Build(editor.Document.Protection, editor.IsMarkedAsFinal)
                        .RestrictEditing.IsChecked)
                : FreeWRibbonExecutionProfile.UnavailableCommand);

        // ── References (AV-REF) ──────────────────────────────────────────────
        ConfigureReferenceCommandFamily(referenceCommands, editor, callbacks);

        // ── Mailings (AV-MAIL) ───────────────────────────────────────────────
        RegisterMailingsCommands(r, mailMerge);

        // ── Design (AV-DESIGN) ───────────────────────────────────────────────
        RegisterDesignCommands(r, editor, callbacks, formatting);

        // ── AV-PICTAB: Picture Format + Drawing Format contextual tabs ────────
        FreeWRibbonEditorExecutionProfile.RegisterFamilies(
            r,
            tableCommands.Build(),
            referenceCommands.Build(),
            headerFooterCommands.Build());
        RegisterNativeFloatingCommands(r, editor, callbacks);
        FreeWRibbonEditorExecutionProfile.RegisterFloating(
            r,
            CreateFloatingExecutionPorts(editor));
        FreeWRibbonEditorExecutionProfile.RegisterImageTableWorkflows(
            r,
            CreateImageExecutionPorts(editor, callbacks),
            CreateTableExecutionPorts(editor, callbacks));
        RegisterShapeTextDirectionSelectionGuards(r, editor);

        // ── AV-CHARTTAB: Chart Design/Format + SmartArt Design contextual tabs ─
        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            r,
            CreateChartSmartArtExecutionPorts(editor, callbacks));

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

    private static void ToggleNormalNarrowMargins(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks)
    {
        var page = editor.Document.Page;
        callbacks.ApplyMarginPreset(PageLayoutCommandPlanner.HasNormalMargins(page) ? "narrow" : "normal");
    }

    private static void ToggleLetterA4Paper(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks)
    {
        var page = editor.Document.Page;
        callbacks.ApplyPaperSize(PageLayoutCommandPlanner.HasLetterPaperSize(page) ? "a4" : "letter");
    }

    private static void ConfigureHeaderFooterCommandFamily(
        FreeWRibbonEditorCommandFamilyBuilder family,
        DocumentView editor)
    {
        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterEditSlots)
            BindHeaderFooterSlot(family, editor, binding);
        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterNavigationSlots)
            BindHeaderFooterSlot(family, editor, binding);
        family.Bind(FreeWRibbonCommandAction.HfClose, new ActionRibbonCommand(editor.CloseHeaderFooterEditing));

        family.Bind(FreeWRibbonCommandAction.HfDifferentFirstPage, new PageSettingCommand(
            editor,
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage));
        family.Bind(FreeWRibbonCommandAction.HfDifferentOddEven, new PageSettingCommand(
            editor,
            page => page.DifferentOddEvenPages = !page.DifferentOddEvenPages,
            page => page.DifferentOddEvenPages));

        family.Bind(FreeWRibbonCommandAction.HfHeaderFromTop, new HeaderFooterDistanceCommand(editor, footer: false));
        family.Bind(FreeWRibbonCommandAction.HfFooterFromBottom, new HeaderFooterDistanceCommand(editor, footer: true));

        family.Bind(FreeWRibbonCommandAction.HfInsertPageNumber, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: false)));
        family.Bind(FreeWRibbonCommandAction.HfInsertPageNumberFooter, new ActionRibbonCommand(() => editor.InsertHeaderFooterPageNumber(footer: true)));
        family.Bind(FreeWRibbonCommandAction.HfInsertDatetime, new ActionRibbonCommand(editor.InsertHeaderFooterDateTime));
        family.Bind(FreeWRibbonCommandAction.HfInsertField, new ActionRibbonCommand(editor.InsertHeaderFooterDocumentInfo));
    }

    private static void BindHeaderFooterSlot(
        FreeWRibbonEditorCommandFamilyBuilder family,
        DocumentView editor,
        FreeWRibbonHeaderFooterSlotBinding binding)
    {
        var slotName = HeaderFooterDialogPlanner.SlotNameFor(binding.Slot);
        family.Bind(binding.Action, new ActionRibbonCommand(() => editor.EditHeaderFooterSlot(slotName)));
    }

    private static IRibbonCommand HeaderFooterTextCommand(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks,
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
        FreeWRibbonHostExecutionPorts callbacks,
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
            if (FreeWRibbonNumericValueParser.TryParseFontSize(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    NumberStyles.Any,
                    out var points))
            {
                editor.SetSelectionFontSize(points);
            }
        }

        public RibbonCommandState GetState() =>
            new(Value: FreeWRibbonNumericValueParser.FormatInvariant(
                editor.GetCaretFormatting().Run.FontSizePt ?? 11));
    }

    private sealed class ParagraphValueCommand(
        FreeWRibbonFormattingSession session,
        FreeWParagraphValueKind kind) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            session.ApplyParagraphValue(kind, context.SelectedValue);

        public RibbonCommandState GetState() => new(Value: session.CurrentParagraphValue(kind));
    }

    private sealed class ParagraphStyleCommand(FreeWRibbonFormattingSession session) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => session.ApplyParagraphStyle(context.SelectedValue);

        public RibbonCommandState GetState() => new(Value: session.CurrentParagraphStyleName());
    }

    private sealed class ToggleActionCommand(Action toggle, Func<bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => toggle();

        public RibbonCommandState GetState() => new(IsChecked: isChecked());
    }

    private sealed class ThemeCommand(FreeWRibbonFormattingSession session) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => session.ApplyTheme(context.SelectedValue);

        public RibbonCommandState GetState() => new(Value: session.CurrentThemeName());
    }

    private sealed class StyleSetCommand(FreeWRibbonFormattingSession session) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => session.ApplyStyleSet(context.SelectedValue);

        public RibbonCommandState GetState() =>
            new(Value: session.CurrentStyleSetName());
    }

    private sealed class ProofingLanguageCommand(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks) : IRibbonCommand
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
    /// Each command id matches an entry in <see cref="FreeWRibbonDefinitionData.FontColors"/> and calls
    /// <see cref="DocumentView.SetFontColor"/> with the appropriate RRGGBB hex string
    /// (or <c>null</c> for the "Automatic" entry, which restores the default run colour).
    /// </summary>
    private static void RegisterFontColorPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        // Maps command-id suffix → CSS hex colour (null = automatic/default).
        // Colours chosen to match Word's standard palette.
        RegisterColorPalette(r, FreeWRibbonPaletteCatalog.FontColors, editor.SetFontColor);
    }

    /// <summary>
    /// Registers the WPF-authority paragraph shading palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterParagraphShadingPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        RegisterColorPalette(
            r,
            FreeWRibbonPaletteCatalog.ParagraphShading,
            hex => editor.SetParagraphShading(hex));
    }

    /// <summary>
    /// Registers the WPF-authority character shading palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterCharacterShadingPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        RegisterColorPalette(
            r,
            FreeWRibbonPaletteCatalog.CharacterShading,
            hex => editor.SetCharacterShading(hex));
    }

    /// <summary>
    /// Registers the WPF-authority character border palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit color or No Border choice.
    /// </summary>
    private static void RegisterCharacterBorderPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        RegisterColorPalette(
            r,
            FreeWRibbonPaletteCatalog.CharacterBorders,
            hex => editor.SetCharacterBorder(
                hex is null
                    ? null
                    : new ParagraphBorder(hex, 0.5) { LineStyle = BorderLineStyle.Single }));
    }

    /// <summary>
    /// Registers the WPF-authority text-highlight palette. The top-level command only opens
    /// the ribbon menu; formatting changes happen only after an explicit swatch or No Color choice.
    /// </summary>
    private static void RegisterHighlightPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        RegisterColorPalette(r, FreeWRibbonPaletteCatalog.Highlights, editor.SetHighlightColor);
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
    /// <see cref="FreeWRibbonHostExecutionPorts"/> launchers and fail closed when the shell did not supply one.
    /// </summary>
    private static void RegisterInsertDepth2Commands(
        IRibbonCommandRegistry r, DocumentView editor, FreeWRibbonHostExecutionPorts callbacks)
    {
        // ── Links ────────────────────────────────────────────────────────────
        // Hyperlink / Bookmark open small dialogs (shell callbacks) that call the model-backed editor methods.
        var hyperlink = OptionalHostCommand(callbacks.OpenHyperlinkDialog);
        r.Bind(FreeWRibbonCommandAction.Hyperlink, hyperlink);
        r.Register("freew.insert-hyperlink", hyperlink);
        r.Bind(FreeWRibbonCommandAction.EditHyperlink, OptionalHostCommand(callbacks.OpenEditHyperlinkDialog));
        r.Bind(FreeWRibbonCommandAction.RemoveHyperlink, new ActionRibbonCommand(editor.RemoveHyperlink));
        r.Bind(FreeWRibbonCommandAction.HyperlinkTooltip, OptionalHostCommand(callbacks.OpenHyperlinkTooltipDialog));
        var bookmark = OptionalHostCommand(callbacks.OpenBookmarkDialog);
        r.Bind(FreeWRibbonCommandAction.Bookmark, bookmark);
        r.Register("freew.insert-bookmark", bookmark);
        r.Bind(FreeWRibbonCommandAction.LinkBookmark,    new ActionRibbonCommand(callbacks.OpenLinkBookmarkDialog ?? (() => LinkToFirstBookmark(editor))));
        r.Bind(FreeWRibbonCommandAction.BookmarkManager, OptionalHostCommand(
            callbacks.OpenBookmarkManagerDialog ?? callbacks.OpenBookmarkDialog));

        // ── Cover Page ───────────────────────────────────────────────────────
        // The split-button face inserts the WPF default; each preset prepends its cover-page block layout.
        CoverPageRibbonWorkflow.Register(
            r,
            new CoverPageRibbonPorts(editor.InsertCoverPage));

        // ── Drop Cap ─────────────────────────────────────────────────────────
        // Dropped / In Margin both enlarge the leading letter (the in-margin float geometry is an
        // approximation — render-deferred); None clears the paragraph's run formatting.
        DropCapRibbonWorkflow.Register(
            r,
            new DropCapRibbonPorts(
                Dropped: new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)),
                InMargin: new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)),
                None: new ActionRibbonCommand(editor.ClearDropCap),
                Options: OptionalHostCommand(callbacks.OpenDropCapOptionsDialog)));

        // ── Quick Parts ──────────────────────────────────────────────────────
        // Shared Presentation owns the saved-part, field, save, organizer, and compatibility routes.
        QuickPartRibbonWorkflow.Register(
            r,
            new QuickPartRibbonPorts(
                OptionalHostCommand(callbacks.OpenQuickPartDialog),
                OptionalHostCommand(callbacks.SaveQuickPartSelection),
                OptionalHostCommand(callbacks.OpenBuildingBlocksOrganizer),
                editor.InsertField));

        // ── Equation ─────────────────────────────────────────────────────────
        // The split-button face and preset gallery share Presentation-owned identities and factories.
        EquationRibbonWorkflow.Register(r, new EquationRibbonPorts(editor.InsertEquation));

        // ── Text from File ───────────────────────────────────────────────────
        // Opens a file picker (shell callback); DOCX content is inserted as model blocks and TXT as plain text.
        var textFromFileCommand = OptionalHostCommand(callbacks.InsertTextFromFile);
        r.Bind(FreeWRibbonCommandAction.InsertFile, textFromFileCommand);
        r.Register("freew.text-from-file", textFromFileCommand);
        InsertMediaRibbonWorkflow.Register(
            r,
            new InsertMediaRibbonPorts(
                Chart: new EditingActionCommand(editor, callbacks.OpenInsertChartDialog, () => editor.InsertChart()),
                SmartArt: new EditingActionCommand(editor, callbacks.OpenInsertSmartArtDialog, () => editor.InsertSmartArt()),
                Icon: new EditingActionCommand(editor, callbacks.OpenIconPickerDialog, editor.InsertIcon),
                WordArt: new ActionRibbonCommand(() => editor.InsertWordArt()),
                EmbeddedObject: new ActionRibbonCommand(
                    callbacks.InsertObject ?? (() => editor.InsertEmbeddedObject()))));
        r.Bind(FreeWRibbonCommandAction.UpdateFields, new ActionRibbonCommand(editor.UpdateFields));
        r.Bind(FreeWRibbonCommandAction.ToggleFieldCodes, new ActionRibbonCommand(editor.ToggleFieldCodes));
    }

    private static IRibbonCommand OptionalHostCommand(Action? callback) =>
        callback is null
            ? FreeWRibbonExecutionProfile.UnavailableCommand
            : new ActionRibbonCommand(callback);

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
    private static void RegisterTableBorderCommands(
        FreeWRibbonEditorCommandFamilyBuilder r,
        DocumentView editor)
    {
        static void Add(FreeWRibbonEditorCommandFamilyBuilder reg, DocumentView ed, string id, CellBorderEdges edges, bool clear = false) =>
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

    private sealed class ShowMarkupBalloonsCommand(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks) : IRibbonStatefulCommand
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
    private static void ConfigureReferenceCommandFamily(
        FreeWRibbonEditorCommandFamilyBuilder family,
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks)
    {
        // Footnotes & Endnotes — insert an empty note + reference marker at the caret.
        NoteReferenceRibbonWorkflow.Register(
            family,
            new NoteReferenceRibbonPorts(
                callbacks.OpenFootnoteDialog ?? (() => editor.InsertFootnote()),
                callbacks.OpenEndnoteDialog ?? (() => editor.InsertEndnote()),
                () => editor.MoveToNextFootnote(),
                () => editor.MoveToPreviousFootnote(),
                () => editor.MoveToNextEndnote(),
                () => editor.MoveToPreviousEndnote(),
                OpenNotes: null,
                callbacks.ToggleNotesPane,
                callbacks.IsNotesPaneVisible,
                callbacks.OpenFootnoteEndnoteOptionsDialog));

        // Table of Contents — generate from the heading outline / regenerate in place.
        TableOfContentsRibbonWorkflow.Register(
            family,
            new TableOfContentsRibbonPorts(
                editor.InsertTableOfContents,
                editor.UpdateTableOfContents,
                styleId => editor.ApplyNamedStyle(styleId)));

        // Captions — the primary action opens the label/text dialog; menu labels remain direct.
        var caption = OptionalHostCommand(callbacks.OpenCaptionDialog);
        family.Bind(FreeWRibbonCommandAction.Caption, caption);
        family.Register("freew.insert-caption", caption);
        family.Bind(FreeWRibbonCommandAction.InsertCaption_Figure, new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Figure)));
        family.Bind(FreeWRibbonCommandAction.InsertCaption_Table,  new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Table)));
        family.Bind(FreeWRibbonCommandAction.InsertCaption_Equation, new ActionRibbonCommand(() => editor.InsertCaption(CaptionLabel.Equation)));

        // Dialog-backed commands fail closed without a shell callback instead of silently choosing defaults.
        family.Bind(FreeWRibbonCommandAction.CrossReference, OptionalHostCommand(callbacks.OpenCrossReferenceDialog));

        var citation = OptionalHostCommand(callbacks.OpenCitationDialog);
        family.Bind(FreeWRibbonCommandAction.Citation, citation);
        family.Register("freew.insert-citation", citation);
        family.Bind(FreeWRibbonCommandAction.ManageSources, OptionalHostCommand(callbacks.OpenManageSourcesDialog));
        family.Bind(FreeWRibbonCommandAction.CitationStyle, new FreeWRibbonChoiceCommand(
            value => editor.ApplyCitationStyle(Citations.ParseStyle(value, editor.Document.BibliographyStyle)),
            () => Citations.StyleName(editor.Document.BibliographyStyle)));
        family.Bind(FreeWRibbonCommandAction.Bibliography, new ActionRibbonCommand(editor.InsertBibliography));

        family.Bind(FreeWRibbonCommandAction.Tof, new ActionRibbonCommand(() => editor.InsertTableOfFigures()));
        family.Bind(FreeWRibbonCommandAction.Tof_Figure, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Figure)));
        family.Bind(FreeWRibbonCommandAction.Tof_Table, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Table)));
        family.Bind(FreeWRibbonCommandAction.Tof_Equation, new ActionRibbonCommand(() => editor.InsertTableOfFigures(CaptionLabel.Equation)));
        family.Bind(FreeWRibbonCommandAction.TofRefresh, new ActionRibbonCommand(() => editor.RefreshTableOfFigures()));
        family.Bind(FreeWRibbonCommandAction.TofRefresh_Figure, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Figure)));
        family.Bind(FreeWRibbonCommandAction.TofRefresh_Table, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Table)));
        family.Bind(FreeWRibbonCommandAction.TofRefresh_Equation, new ActionRibbonCommand(() => editor.RefreshTableOfFigures(CaptionLabel.Equation)));
        family.Bind(FreeWRibbonCommandAction.IndexMark, new ActionRibbonCommand(
            callbacks.OpenMarkIndexEntryDialog ?? (() => editor.MarkIndexEntry())));
        family.Bind(FreeWRibbonCommandAction.IndexInsert, new ActionRibbonCommand(
            callbacks.OpenInsertIndexDialog ?? (() => editor.InsertIndex())));
        family.Bind(FreeWRibbonCommandAction.IndexRefresh, new ActionRibbonCommand(
            callbacks.OpenUpdateIndexDialog ?? (() => editor.RefreshIndex())));
        family.Bind(FreeWRibbonCommandAction.MarkCitation, OptionalHostCommand(callbacks.OpenMarkCitationDialog));
        family.Bind(FreeWRibbonCommandAction.TableOfAuthorities, new ActionRibbonCommand(
            callbacks.ShowTableOfAuthoritiesDialog ?? (() =>
            {
                var commit = TableOfAuthoritiesDialogPlanner.PlanCommit(
                    callbacks.OpenTableOfAuthoritiesDialog?.Invoke(),
                    useDefaultsWhenUnavailable: callbacks.OpenTableOfAuthoritiesDialog is null);
                if (commit.ShouldInsert)
                    editor.InsertTableOfAuthorities(commit.Options!);
            })));
        family.Bind(FreeWRibbonCommandAction.TableOfAuthoritiesRefresh, new ActionRibbonCommand(editor.RefreshTableOfAuthorities));
    }

    /// <summary>
    /// Registers renderer-native dialogs and presets outside the shared image/floating profiles:
    /// image adjustments, position/size/alt-text dialogs, picture styles, and shape position/size/alt text.
    /// </summary>
    private static void RegisterNativeFloatingCommands(
        IRibbonCommandRegistry r,
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks)
    {
        var imageObjectCommands = CreateFloatingObjectCommandPorts(
            editor,
            "Image",
            callbacks.OpenImagePositionDialog,
            callbacks.OpenImageSizeDialog);
        FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(
            r,
            "image",
            imageObjectCommands,
            FreeWRibbonDefinitionData.FloatingPositionPresets);
        r.Bind(FreeWRibbonCommandAction.ImageAdjustDialog, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAdjustDialog));
        r.Bind(
            FreeWRibbonCommandAction.ImageSize,
            FreeWRibbonFloatingObjectCommandFactory.CreateSize(imageObjectCommands));
        r.Bind(FreeWRibbonCommandAction.ImageAltText, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageAltTextDialog));
        r.Bind(FreeWRibbonCommandAction.ImageBorder, new SelectedImageDialogCommand(
            editor,
            callbacks.OpenImageBorderDialog));
        RegisterImageAdjustmentCommands(r, editor, callbacks);
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
        var shapeObjectCommands = CreateFloatingObjectCommandPorts(
            editor,
            "Shape",
            callbacks.OpenShapePositionDialog,
            callbacks.OpenShapeSizeDialog);
        FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(
            r,
            "shape",
            shapeObjectCommands,
            FreeWRibbonDefinitionData.FloatingPositionPresets);
        r.Bind(
            FreeWRibbonCommandAction.ShapeSize,
            FreeWRibbonFloatingObjectCommandFactory.CreateSize(shapeObjectCommands));
        foreach (var preset in FreeWRibbonDefinitionData.FloatingSizePresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-size-{captured.Suffix}",
                FreeWRibbonFloatingObjectCommandFactory.CreateSizePreset(
                    shapeObjectCommands,
                    captured.WidthPt,
                    captured.HeightPt));
        }

        r.Bind(FreeWRibbonCommandAction.ShapeAltText, new FloatingObjectAltTextCommand(editor, callbacks.OpenShapeAltTextDialog));
        foreach (var preset in FreeWRibbonDefinitionData.ShapeAltTextPresets)
        {
            var captured = preset;
            r.Register(
                $"freew.shape-alt-text-{captured.Suffix}",
                new FloatingObjectAltTextPresetCommand(editor, captured));
        }
    }

    private static void RegisterImageAdjustmentCommands(
        IRibbonCommandRegistry r,
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks)
    {
        // These IDs are the WPF authority's Picture Format > Adjust routes. Keep the
        // value-preserving mutations in DocumentView so both hosts use the shared model commands.
        foreach (var preset in ImageAdjustmentCommandPlanner.AdjustmentPresets)
        {
            var captured = preset;
            RegisterImageMutation(r, editor, captured.Action,
                image => ApplyImageAdjustmentPreset(editor, image, captured));
        }

        // Avalonia currently exposes one shared adjustment dialog callback, which is also
        // the WPF route used for Color and Transparency's full-value dialogs.
        r.Bind(FreeWRibbonCommandAction.ImageColorDialog, new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));
        r.Bind(FreeWRibbonCommandAction.ImageTransparencyDialog, new SelectedImageDialogCommand(
            editor, callbacks.OpenImageAdjustDialog));

        foreach (var preset in ImageAdjustmentCommandPlanner.RecolorPresets)
        {
            var captured = preset;
            RegisterImageMutation(r, editor, captured.Action,
                _ => editor.SetSelectedImageRecolor(captured.Mode, captured.ColorTemperature ?? 0));
        }

        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets)
        {
            var captured = preset;
            RegisterImageEffectPreset(r, editor, captured);
        }

        foreach (var preset in ImageAdjustmentCommandPlanner.ArtisticEffectPresets)
        {
            var captured = preset;
            RegisterImageMutation(r, editor, captured.CommandId,
                _ => editor.SetSelectedImageArtisticEffect(captured.Effect));
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

    private static void ApplyImageAdjustmentPreset(
        DocumentView editor,
        InlineImage image,
        ImageAdjustmentPresetDescriptor preset)
    {
        var brightness = preset.Channel == ImageAdjustmentChannel.Brightness
            ? preset.Value
            : image.BrightnessPct;
        var contrast = preset.Channel == ImageAdjustmentChannel.Contrast
            ? preset.Value
            : image.ContrastPct;
        var saturation = preset.Channel == ImageAdjustmentChannel.Saturation
            ? preset.Value
            : image.SaturationPct;
        var transparency = preset.Channel == ImageAdjustmentChannel.Transparency
            ? preset.Value
            : image.TransparencyPct;
        editor.SetSelectedImageAdjust(brightness, contrast, saturation, transparency);
    }

    private static void RegisterImageEffectPreset(
        IRibbonCommandRegistry registry,
        DocumentView editor,
        ImageEffectPresetDescriptor preset)
    {
        void Apply(InlineImage image)
        {
            var shadow = preset.Channel == ImageEffectChannel.Shadow
                ? (int)preset.Value
                : image.ShadowPreset;
            var reflection = preset.Channel == ImageEffectChannel.Reflection
                ? (int)preset.Value
                : image.ReflectionPreset;
            var glow = preset.Channel == ImageEffectChannel.Glow
                ? preset.Value
                : image.GlowSizePt;
            var softEdge = preset.Channel == ImageEffectChannel.SoftEdge
                ? preset.Value
                : image.SoftEdgePt;
            var bevel = preset.Channel == ImageEffectChannel.Bevel
                ? (int)preset.Value
                : image.BevelPreset;
            editor.SetSelectedImageEffect(
                shadow,
                glow,
                image.GlowColorHex,
                reflection,
                softEdge,
                bevel);
        }

        if (preset.Action is { } action)
            RegisterImageMutation(registry, editor, action, Apply);
        else
            RegisterImageMutation(registry, editor, preset.CommandId!, Apply);
    }

    private static FreeWRibbonFloatingObjectCommandPorts CreateFloatingObjectCommandPorts(
        DocumentView editor,
        string requiredKind,
        Action? openPositionDialog,
        Action? openSizeDialog) =>
        new(
            HasSelection: () => requiredKind == "Shape"
                ? editor.SelectedFloatingShape() is not null
                : editor.SelectedFloatingInfo?.Kind == requiredKind,
            ApplyPosition: position =>
            {
                if (requiredKind == "Shape")
                {
                    editor.SetSelectedShapePosition(
                        position.HorizontalOffsetPt,
                        position.VerticalOffsetPt,
                        position.HorizontalAnchor,
                        position.VerticalAnchor);
                }
                else
                {
                    editor.SetFloatingPosition(
                        position.HorizontalOffsetPt,
                        position.VerticalOffsetPt,
                        position.HorizontalAnchor,
                        position.VerticalAnchor);
                }
            },
            ApplySize: (widthPt, heightPt) =>
            {
                if (requiredKind == "Shape")
                    editor.SetSelectedShapeSize(widthPt, heightPt);
                else
                    editor.SetFloatingSize(widthPt, heightPt);
            },
            OpenPositionDialog: openPositionDialog,
            OpenSizeDialog: openSizeDialog);

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

    private static FreeWRibbonFloatingExecutionPorts CreateFloatingExecutionPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            HasSelection: target => target == ObjectFormatTarget.Picture
                ? editor.SelectedFloatingImage() is not null
                : editor.SelectedFloatingShape() is not null,
            ApplyWrap: (_, wrapping) => editor.SetFloatingWrap(wrapping),
            ApplyTransform: (_, command) =>
            {
                ExecuteFloatingTransform(editor, command);
                return true;
            },
            ApplyZOrder: (target, operation) => editor.ChangeSelectedFloatingZOrder(
                operation,
                target == ObjectFormatTarget.Picture ? "Image" : "Shape"),
            ApplySize: (_, dimension, points) => SetFloatingSize(editor, dimension, points),
            ApplyParagraphAlignment: (target, alignment) =>
            {
                if (target == ObjectFormatTarget.Picture)
                    editor.SetSelectedImageAlignment(alignment);
                else
                    editor.SetSelectedShapeAlignment(alignment);
            },
            CanArrange: editor.CanArrangeSelectedFloatingObjects,
            Arrange: kind => editor.ArrangeSelectedFloatingObjects(kind),
            SelectedShape: editor.SelectedFloatingShape,
            SetShapeKind: editor.SetSelectedShapeKind,
            ConvertShapeToFreeform: editor.ConvertSelectedShapeToFreeform,
            BeginShapeEditPoints: editor.BeginShapeEditPoints,
            SetShapeTextDirection: editor.SetSelectedShapeTextDirection,
            SetShapeExtendedFill: editor.SetSelectedShapeExtendedFill,
            SetShapeFill: editor.SetSelectedShapeFill,
            SetShapeOutline: editor.SetSelectedShapeOutline,
            SetShapeEffects: editor.SetSelectedShapeEffects,
            ApplyShapeStyle: editor.ApplySelectedShapeStyle,
            CanGroup: () => editor.HasMultipleFloatingObjectsSelected,
            Group: editor.GroupSelectedFloatingObjects,
            CanUngroup: () => editor.IsGroupSelected,
            Ungroup: editor.UngroupSelectedFloatingObject);

    private static void RegisterShapeTextDirectionSelectionGuards(
        FreeWRibbonCommandBindingPorts bindings,
        DocumentView editor)
    {
        Bind(FreeWRibbonCommandAction.ShapeTextHorizontal, ShapeTextDirection.Horizontal);
        Bind(FreeWRibbonCommandAction.ShapeTextRotate90, ShapeTextDirection.Rotate90);
        Bind(FreeWRibbonCommandAction.ShapeTextRotate270, ShapeTextDirection.Rotate270);

        void Bind(FreeWRibbonCommandAction action, ShapeTextDirection direction) =>
            bindings.Bind(action, new FreeWRibbonStatefulPortCommand(
                _ => editor.SetSelectedShapeTextDirection(direction),
                () => new RibbonCommandState(
                    IsEnabled: editor.SelectedFloatingShape() is { HasText: true }),
                () => editor.Focus()));
    }

    private static FreeWRibbonImageExecutionPorts CreateImageExecutionPorts(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedImage: editor.SelectedFloatingImage,
            ShowCropDialogAsync: callbacks.ShowImageCropDialogAsync,
            ApplyCropOutcome: crop => editor.SetSelectedImageCrop(
                crop.Left,
                crop.Right,
                crop.Top,
                crop.Bottom),
            ResetImage: editor.ResetSelectedImage);

    private static FontEffectRibbonPorts CreateFontEffectPorts(DocumentView editor) =>
        new(
            Bold: new ActionRibbonCommand(editor.ToggleBold),
            Italic: new ActionRibbonCommand(editor.ToggleItalic),
            Underline: new ActionRibbonCommand(editor.ToggleUnderline),
            Strikethrough: new ActionRibbonCommand(editor.ToggleStrikethrough),
            SmallCaps: new ActionRibbonCommand(editor.ToggleSmallCaps),
            AllCaps: new ActionRibbonCommand(editor.ToggleAllCaps),
            Superscript: new ActionRibbonCommand(editor.ToggleSuperscript),
            Subscript: new ActionRibbonCommand(editor.ToggleSubscript),
            GrowFont: new ActionRibbonCommand(editor.GrowFont),
            ShrinkFont: new ActionRibbonCommand(editor.ShrinkFont));

    private static ParagraphEditingRibbonPorts CreateParagraphEditingPorts(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks) =>
        new(
            PrepareExecution: () => editor.Focus(),
            ToggleBullets: new ActionRibbonCommand(() => editor.ToggleList(ListKind.Bullet)),
            ToggleNumbering: new ActionRibbonCommand(() => editor.ToggleList(ListKind.Number)),
            AlignLeft: new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Left)),
            AlignCenter: new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Center)),
            AlignRight: new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Right)),
            AlignJustify: new ActionRibbonCommand(() => editor.SetAlignment(TextAlignment.Justify)),
            IncreaseIndent: editor.IncreaseIndent,
            DecreaseIndent: editor.DecreaseIndent,
            ToggleSpaceBefore: () => ToggleSpaceBefore(editor),
            ToggleSpaceAfter: () => ToggleSpaceAfter(editor),
            ToggleKeepWithNext: editor.ToggleKeepWithNext,
            ToggleKeepLinesTogether: editor.ToggleKeepLinesTogether,
            ToggleWidowControl: editor.ToggleWidowControl,
            ToggleParagraphBorder: () => editor.ToggleParagraphBorder(),
            Sort: new ActionRibbonCommand(() => ExecuteSortCommand(editor, callbacks)));

    private static TableEditingRibbonPorts CreateTableEditingPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            ToggleHeaderRow: editor.ToggleTableHeaderRow,
            ToggleBandedRows: editor.ToggleBandedRows,
            ToggleLastRow: editor.ToggleTableLastRow,
            ToggleFirstColumn: editor.ToggleTableFirstColumn,
            ToggleLastColumn: editor.ToggleTableLastColumn,
            ToggleBandedColumns: editor.ToggleTableBandedColumns,
            ToggleGridlines: () => editor.ViewTableGridlines = !editor.ViewTableGridlines,
            SelectTable: () =>
            {
                if (editor.CellCaretInfo is not { } cell)
                    return;
                var (lastRow, lastGridColumn) = editor.GetTableBounds(cell.TableBlock);
                editor.SetCellBlockSelection(cell.TableBlock, 0, 0, lastRow, lastGridColumn);
            },
            SelectRow: () =>
            {
                if (editor.CellCaretInfo is not { } cell)
                    return;
                var (_, lastGridColumn) = editor.GetTableBounds(cell.TableBlock);
                editor.SetCellBlockSelection(cell.TableBlock, cell.Row, 0, cell.Row, lastGridColumn);
            },
            SelectColumn: () =>
            {
                if (editor.CellCaretInfo is not { } cell)
                    return;
                var (lastRow, _) = editor.GetTableBounds(cell.TableBlock);
                editor.SetCellBlockSelection(cell.TableBlock, 0, cell.Col, lastRow, cell.Col);
            },
            SelectCell: () =>
            {
                if (editor.CellCaretInfo is { } cell)
                    editor.SetCellBlockSelection(cell.TableBlock, cell.Row, cell.Col, cell.Row, cell.Col);
            },
            InsertRowAbove: editor.InsertTableRowAbove,
            InsertColumnLeft: editor.InsertTableColumnLeft,
            DeleteRow: editor.DeleteTableRow,
            DeleteColumn: editor.DeleteTableColumn,
            DeleteTable: () =>
            {
                if (editor.CellCaretInfo is { } cell)
                    editor.DeleteTableBlock(cell.TableBlock);
            },
            SplitTable: editor.SplitTable,
            DistributeRows: editor.DistributeTableRows,
            DistributeColumns: editor.DistributeTableColumns,
            SetAutoFit: editor.SetTableAutoFit,
            SetCellAlignment: editor.SetCaretCellAlignment,
            SetCellTextDirection: editor.SetCaretCellTextDirection,
            ToggleRepeatHeaderRow: editor.ToggleTableRepeatHeaderRow);

    private static FreeWRibbonTableExecutionPorts CreateTableExecutionPorts(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedCell: () => editor.CaretTableCell() is { } cell
                ? new FreeWRibbonTableCellSelection(cell.Table, cell.RowIndex, cell.ColumnIndex)
                : null,
            SelectedContext: editor.CaretTableContext,
            CanConvertToText: () => editor.CanConvertTableToText,
            ShowFormulaDialogAsync: callbacks.ShowTableFormulaDialogAsync,
            ApplyFormulaOutcome: editor.InsertTableFormula,
            ShowPropertiesDialogAsync: callbacks.ShowTablePropertiesDialogAsync,
            ApplyPropertiesOutcome: editor.ApplyTableProperties,
            ShowTableToTextDialogAsync: callbacks.ShowTableToTextDialogAsync,
            ApplyTableToTextOutcome: editor.ConvertTableToText);

    private static FreeWRibbonChartSmartArtExecutionPorts CreateChartSmartArtExecutionPorts(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedChart: editor.SelectedFloatingChart,
            SetChartKind: editor.SetChartType,
            ApplyChartStyle: style => editor.SetChartStyle(style.Id),
            ApplyChartColorScheme: scheme => editor.SetChartColorScheme(scheme.Id),
            ApplyChartQuickLayout: editor.SetChartQuickLayout,
            ToggleChartLegend: editor.ToggleChartLegend,
            ShowChartTitleDialogAsync: callbacks.ShowChartTitleDialogAsync,
            ApplyChartTitleOutcome: result => editor.SetChartTitle(result.NewTitle),
            ToggleChartTitleFallback: editor.ToggleChartTitle,
            ShowChartAxisTitlesDialogAsync: callbacks.ShowChartAxisTitlesDialogAsync,
            ApplyChartAxisTitlesOutcome: result => editor.SetChartAxisTitles(
                result.CategoryTitle,
                result.ValueTitle),
            ToggleChartAxisTitlesFallback: editor.ToggleChartAxisTitles,
            ShowChartDataDialogAsync: callbacks.ShowChartDataDialogAsync,
            ApplyChartDataOutcome: editor.ReplaceSelectedChartData,
            ShowChartSizeDialogAsync: callbacks.ShowChartSizeDialogAsync,
            ApplyChartSizeOutcome: result => editor.SetSelectedChartSize(result.WidthPt, result.HeightPt),
            SelectedSmartArt: editor.SelectedFloatingSmartArt,
            MutateSmartArt: editor.MutateSelectedSmartArt,
            ApplySmartArtLayout: editor.SetSmartArtLayout,
            ApplySmartArtColorScheme: scheme => editor.SetSmartArtColor(scheme.Id),
            ApplySmartArtStyle: editor.SetSmartArtStyle,
            ShowSmartArtEditDialogAsync: callbacks.ShowSmartArtEditDialogAsync,
            ApplySmartArtEditOutcome: editor.ReplaceSelectedSmartArt);

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
    /// host callbacks (<see cref="FreeWRibbonHostExecutionPorts.AskRecipientCsv"/> /
    /// <see cref="FreeWRibbonHostExecutionPorts.AskMergeFieldName"/>); when the shell didn't supply them (tests,
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
        r.Bind(FreeWRibbonCommandAction.MergeRuleIf, RuleCommand(MailMergeRuleKind.IfThenElse));
        r.Bind(FreeWRibbonCommandAction.MergeRuleSkipRecordIf, RuleCommand(MailMergeRuleKind.SkipRecordIf));
        r.Bind(FreeWRibbonCommandAction.MergeRuleNextRecordIf, RuleCommand(MailMergeRuleKind.NextRecordIf));
        r.Bind(FreeWRibbonCommandAction.MergeNextRecord, new ActionRibbonCommand(engine.InsertNextRecordField));
        r.Bind(FreeWRibbonCommandAction.MergeRecordNumber, new ActionRibbonCommand(engine.InsertMergeRecordNumberField));
        r.Bind(FreeWRibbonCommandAction.MergeSequenceNumber, new ActionRibbonCommand(engine.InsertMergeSequenceNumberField));
        r.Bind(FreeWRibbonCommandAction.MergeRuleFillIn, RuleCommand(MailMergeRuleKind.FillIn));
        r.Bind(FreeWRibbonCommandAction.MergeRuleAsk, RuleCommand(MailMergeRuleKind.Ask));
        r.Bind(FreeWRibbonCommandAction.MergeRuleSet, RuleCommand(MailMergeRuleKind.Set));
        r.Bind(FreeWRibbonCommandAction.MergeRuleRef, RuleCommand(MailMergeRuleKind.Ref));

        IRibbonCommand RuleCommand(MailMergeRuleKind kind) =>
            new ActionRibbonCommand(() => engine.InsertRule(kind));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreview, new ActionRibbonCommand(engine.TogglePreview),
            "freew.preview-results");
        r.Bind(FreeWRibbonCommandAction.MergePreviewFirst, new ActionRibbonCommand(engine.FirstRecord));
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreviewNext, new ActionRibbonCommand(engine.NextRecord),
            "freew.next-record");
        RegisterMailingsAlias(r, FreeWRibbonCommandAction.MergePreviewPrevious, new ActionRibbonCommand(engine.PreviousRecord),
            "freew.prev-record");
        r.Bind(FreeWRibbonCommandAction.MergePreviewLast, new ActionRibbonCommand(engine.LastRecord));
        // MainWindow replaces these with owner-modal dialogs; keep definition parity for headless hosts.
        r.Bind(FreeWRibbonCommandAction.MergeFindRecipient, FreeWRibbonExecutionProfile.UnavailableCommand);
        r.Bind(FreeWRibbonCommandAction.MergeCheckErrors, FreeWRibbonExecutionProfile.UnavailableCommand);
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
    /// <see cref="FreeWRibbonHostExecutionPorts"/> dialog launchers and fail closed when the shell did not supply one.
    /// </summary>
    private static void RegisterDesignCommands(
        IRibbonCommandRegistry r,
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks,
        FreeWRibbonFormattingSession formatting)
    {
        // ── Themes ───────────────────────────────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.Theme, new ThemeCommand(formatting));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyTheme(t)));
        }

        // ── Colors (palette only — preserves fonts) ──────────────────────────
        r.Bind(FreeWRibbonCommandAction.ThemeColors, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Bind(FreeWRibbonCommandAction.CustomizeColors, OptionalHostCommand(callbacks.OpenCustomizeThemeColorsDialog));
        foreach (var theme in DocumentTheme.Catalog)
        {
            var t = theme;
            r.Register($"freew.theme-colors.{t.Name.ToLowerInvariant()}", new ActionRibbonCommand(() => editor.ApplyThemeColors(t)));
        }

        // ── Fonts (heading/body pairing — preserves colours) ─────────────────
        r.Bind(FreeWRibbonCommandAction.ThemeFonts, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Bind(FreeWRibbonCommandAction.CustomizeFonts, OptionalHostCommand(callbacks.OpenCustomizeThemeFontsDialog));
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
            r.Register($"freew.para-spacing.{FreeWRibbonDefinitionData.ParaSpacingId(s.Name)}",
                new ActionRibbonCommand(() => editor.ApplyParagraphSpacingSet(s)));
        }
        r.Bind(FreeWRibbonCommandAction.CustomParagraphSpacing,
            OptionalHostCommand(callbacks.OpenCustomParagraphSpacingDialog));

        r.Bind(FreeWRibbonCommandAction.ThemeEffects, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        for (var index = 0; index < DocumentEffectSet.Catalog.Count; index++)
        {
            var effectSet = DocumentEffectSet.Catalog[index];
            r.Register(FreeWContextMenuPlanner.EffectsPrefix + index,
                new ActionRibbonCommand(() => editor.ApplyEffectSet(effectSet)));
        }

        // ── Page Color swatches (+ No Color) ─────────────────────────────────
        r.Bind(FreeWRibbonCommandAction.StyleSet, new StyleSetCommand(formatting));
        r.Bind(FreeWRibbonCommandAction.ResetStyleSet, new ActionRibbonCommand(() => editor.ApplyStyleSet(DocumentStyleSet.Default)));

        r.Bind(FreeWRibbonCommandAction.PageColor, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.page-color.more", OptionalHostCommand(callbacks.OpenPageColorDialog));
        RegisterPageColorPalette(r, editor);

        // ── Page Borders — dialog launcher (optional callback) ───────────────
        r.Register("freew.page-borders", OptionalHostCommand(callbacks.OpenPageBordersDialog));

        // ── Watermark — built-in presets + Custom (dialog) + Remove ──────────
        r.Bind(FreeWRibbonCommandAction.Watermark, new ActionRibbonCommand(() => { /* dropdown opener */ }));
        r.Register("freew.watermark.confidential", new ActionRibbonCommand(() => editor.SetWatermarkText("CONFIDENTIAL")));
        r.Register("freew.watermark.do-not-copy",  new ActionRibbonCommand(() => editor.SetWatermarkText("DO NOT COPY")));
        r.Register("freew.watermark.draft",        new ActionRibbonCommand(() => editor.SetWatermarkText("DRAFT")));
        r.Register("freew.watermark.urgent",       new ActionRibbonCommand(() => editor.SetWatermarkText("URGENT")));
        r.Register("freew.watermark.custom", OptionalHostCommand(callbacks.OpenWatermarkDialog));
        r.Register("freew.watermark.none",         new ActionRibbonCommand(() => editor.SetWatermark(null)));
    }

    /// <summary>
    /// AV-DESIGN: Registers the per-swatch sub-commands for the Page Color palette. Each id matches an entry
    /// in <see cref="FreeWRibbonDefinitionData.PageColors"/> and calls <see cref="DocumentView.SetPageColor"/> with the
    /// swatch hex (or null for "No Color", which clears the background back to white).
    /// </summary>
    private static void RegisterPageColorPalette(IRibbonCommandRegistry r, DocumentView editor)
    {
        RegisterColorPalette(r, FreeWRibbonPaletteCatalog.PageColors, editor.SetPageColor);
    }

    private static void RegisterColorPalette(
        IRibbonCommandRegistry registry,
        IReadOnlyList<FreeWRibbonPaletteChoice> choices,
        Action<string?> apply)
    {
        foreach (var choice in choices)
        {
            var hex = choice.Hex;
            registry.Register(choice.CommandId, new ActionRibbonCommand(() => apply(hex)));
        }
    }

    private static void ExecuteSortCommand(DocumentView editor, FreeWRibbonHostExecutionPorts callbacks)
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
