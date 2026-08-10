using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Speech;
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
    private static IRibbonCommand BuildImageAdjustmentPresetCommand(
        DocumentView editor,
        ImageAdjustmentPresetDescriptor preset) => preset.Channel switch
        {
            ImageAdjustmentChannel.Brightness => new ImageBrightnessPresetCommand(editor, preset.Value),
            ImageAdjustmentChannel.Contrast => new ImageContrastPresetCommand(editor, preset.Value),
            ImageAdjustmentChannel.Saturation => new ImageSaturationPresetCommand(editor, preset.Value),
            ImageAdjustmentChannel.Transparency => new ImageTransparencyPresetCommand(editor, preset.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

    private static IRibbonCommand BuildImageRecolorPresetCommand(
        DocumentView editor,
        ImageRecolorPresetDescriptor preset) =>
        preset.ColorTemperature is { } temperature
            ? new ImageColorTempCommand(editor, temperature)
            : new ImageRecolorPresetCommand(editor, preset.Mode);

    private static void RegisterImageEffectPreset(
        IRibbonCommandRegistry registry,
        DocumentView editor,
        ImageEffectPresetDescriptor preset)
    {
        IRibbonCommand command = preset.Channel switch
        {
            ImageEffectChannel.Shadow => new ImageShadowPresetCommand(editor, (int)preset.Value),
            ImageEffectChannel.Reflection => new ImageReflectionPresetCommand(editor, (int)preset.Value),
            ImageEffectChannel.Glow => new ImageGlowPresetCommand(editor, preset.Value),
            ImageEffectChannel.SoftEdge => new ImageSoftEdgeCommand(editor, preset.Value),
            ImageEffectChannel.Bevel => new ImageBevelPresetCommand(editor, (int)preset.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

        if (preset.Action is { } action)
            registry.Bind(action, command);
        else
            registry.Register(preset.CommandId!, command);
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
        FreeWRibbonHostExecutionPorts hostPorts,
        FreeWWpfRibbonNativeExecutionPorts? nativePorts = null)
    {
        ArgumentNullException.ThrowIfNull(hostPorts);
        nativePorts ??= FreeWWpfRibbonNativeExecutionPorts.Empty;

        return Build(
            editor,
            stateStore,
            onPrintPreview: hostPorts.OpenPrintPreview,
            onToggleNavPane: hostPorts.ToggleNavigationPane,
            isNavPaneVisible: hostPorts.IsNavigationPaneVisible,
            onToggleReadMode: hostPorts.ToggleReadMode,
            isReadModeActive: hostPorts.IsReadModeActive,
            onTogglePrintLayout: hostPorts.SetPrintLayout,
            isPrintLayoutActive: hostPorts.IsPrintLayoutActive,
            onToggleOutlineView: hostPorts.SetOutlineView,
            isOutlineViewActive: hostPorts.IsOutlineViewActive,
            onZoomDialog: hostPorts.OpenZoomDialog,
            onZoom100: () => hostPorts.ApplyZoom(1.0, 0),
            onZoomOnePage: hostPorts.ZoomOnePage,
            onZoomPageWidth: hostPorts.ZoomPageWidth,
            onWebLayout: hostPorts.SetWebLayout,
            isWebLayoutActive: hostPorts.IsWebLayoutActive,
            onDraftView: hostPorts.SetDraftView,
            isDraftViewActive: hostPorts.IsDraftViewActive,
            onToggleRevealFormatting: hostPorts.ToggleRevealFormatting,
            isRevealFormattingVisible: hostPorts.IsRevealFormattingVisible,
            onToggleReviewingPane: hostPorts.ToggleReviewingPane,
            isReviewingPaneVisible: hostPorts.IsReviewingPaneVisible,
            onAcceptThisChange: hostPorts.AcceptThisChange,
            onRejectThisChange: hostPorts.RejectThisChange,
            onPreviousChange: hostPorts.PreviousChange,
            onNextChange: hostPorts.NextChange,
            onFindReplace: hostPorts.OpenFindReplaceDialog,
            onToggleRuler: hostPorts.ToggleRuler,
            isRulerVisible: hostPorts.IsRulerVisible,
            onToggleMultiplePages: hostPorts.ToggleMultiplePages,
            isMultiplePagesActive: hostPorts.IsMultiplePagesActive,
            onToggleSideToSide: hostPorts.ToggleSideToSide,
            isSideToSideActive: hostPorts.IsSideToSideActive,
            onToggleSplitWindow: hostPorts.ToggleSplit,
            isSplitWindowActive: hostPorts.IsSplitActive,
            onHelpOnline: hostPorts.OpenHelpOnline,
            onFeedback: hostPorts.OpenFeedback,
            onCopyDiagnostics: hostPorts.CopyDiagnostics,
            onCheckForUpdates: hostPorts.CheckForUpdates,
            onAbout: hostPorts.OpenAbout,
            onLegalNotices: hostPorts.OpenLegalNotices,
            onToggleNotesPane: hostPorts.ToggleNotesPane,
            isNotesPaneVisible: hostPorts.IsNotesPaneVisible,
            onOpenHeaderFooterPane: hostPorts.OpenHeaderFooterPane,
            onCloseHeaderFooterPane: hostPorts.CloseHeaderFooterPane,
            onTogglePagedEditView: hostPorts.TogglePagedEditView,
            isPagedEditViewActive: hostPorts.IsPagedEditViewActive,
            onReadModeColumnWidth: hostPorts.ApplyReadModeColumnWidth,
            onReadModePageColor: hostPorts.ApplyReadModePageColor,
            onNewWindow: hostPorts.NewWindow,
            onArrangeAll: hostPorts.ArrangeAll,
            onToggleThesaurus: hostPorts.OpenThesaurus,
            onToggleBalloons: hostPorts.ToggleReviewBalloons,
            askHeaderFooterText: nativePorts.AskHeaderFooterText,
            onOpenMailMergeErrorReport: hostPorts.OpenMailMergeErrorReport,
            onPrintMailMergeDocument: hostPorts.PrintMailMergeDocument,
            resolveFieldEditor: nativePorts.ResolveFieldEditor,
            askFieldInstruction: nativePorts.AskFieldInstruction,
            hostPorts: hostPorts);
    }

    /// <summary>Compatibility seam for focused WPF command tests; production hosts use the ports overload.</summary>
    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview = null,
        Action? onToggleNavPane = null,
        Func<bool>? isNavPaneVisible = null,
        Action? onToggleReadMode = null,
        Func<bool>? isReadModeActive = null,
        Action? onTogglePrintLayout = null,
        Func<bool>? isPrintLayoutActive = null,
        Action? onToggleOutlineView = null,
        Func<bool>? isOutlineViewActive = null,
        Action? onZoomDialog = null,
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
        Action<TextDocument>? onPrintMailMergeDocument = null,
        Func<DocumentView>? resolveFieldEditor = null,
        Func<Window?, string?>? askFieldInstruction = null,
        FreeWRibbonHostExecutionPorts? hostPorts = null)
    {
        var registry = new FreeWRibbonCommandBindingPorts();
        if (hostPorts is not null)
        {
            FreeWRibbonHostExecutionProfile.Register(
                registry,
                hostPorts,
                registerFileAdapterCommands: true);
        }

        var tableCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        var referenceCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        var headerFooterCommands = new FreeWRibbonEditorCommandFamilyBuilder();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();
        var resolveFieldTarget = resolveFieldEditor ?? (() => editor);
        var askField = askFieldInstruction ?? FieldPickerDialog.Ask;

        void Routed(FreeWRibbonCommandAction action, RoutedCommand command) =>
            registry.Bind(action,
                new RoutedEditCommand(editor, command));

        void Toggle(
            FreeWRibbonCommandAction action,
            RoutedCommand command,
            DependencyProperty property,
            Func<object?, bool> isOn,
            Func<bool>? tryModelToggle = null)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn, tryModelToggle);
            registry.Bind(action, cmd);
            stateful.Add((FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action), cmd));
        }

        void PageSetting(
            FreeWRibbonCommandAction action,
            Action<PageSettings> apply,
            Func<PageSettings, bool>? isChecked = null)
        {
            var command = new PageCommand(editor, apply, isChecked);
            var commandId = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action);
            registry.Bind(action, command);
            stateful.Add((commandId, command));
            stateStore.SetState(commandId, command.GetState());
        }

        Toggle(FreeWRibbonCommandAction.Bold, EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold,
            () => editor.TryToggleSelectedRunFormatting(f => f.Bold, (f, value) => f with { Bold = value }));
        Toggle(FreeWRibbonCommandAction.Italic, EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic,
            () => editor.TryToggleSelectedRunFormatting(f => f.Italic, (f, value) => f with { Italic = value }));
        Toggle(FreeWRibbonCommandAction.Underline, EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
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
        registry.Bind(FreeWRibbonCommandAction.Superscript, new CharacterEffectCommand(editor, CharacterEffect.Superscript));
        registry.Bind(FreeWRibbonCommandAction.Subscript, new CharacterEffectCommand(editor, CharacterEffect.Subscript));
        registry.Bind(FreeWRibbonCommandAction.Strikethrough, new CharacterEffectCommand(editor, CharacterEffect.Strikethrough));
        registry.Bind(FreeWRibbonCommandAction.Smallcaps, new CharacterEffectCommand(editor, CharacterEffect.SmallCaps));
        registry.Bind(FreeWRibbonCommandAction.Allcaps, new CharacterEffectCommand(editor, CharacterEffect.AllCaps));

        // Home > Font: character border and character shading (new W20 commands). These are model-only
        // run properties with full DOCX round-trip (w:rBdr / w:shd). Character Border opens a border-
        // colour/style picker; Character Shading opens a colour swatch picker like paragraph shading.
        registry.Bind(FreeWRibbonCommandAction.CharBorder, new CharacterBorderCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.CharShading, new CharacterShadingCommand(editor));

        // Review > Language > Set Proofing Language: opens a dialog listing common BCP-47 tags and
        // applies the chosen language to the selected runs (rPr/w:lang) for spell-check fidelity.
        registry.Bind(FreeWRibbonCommandAction.SetProofingLanguage, new SetProofingLanguageCommand(editor));

        Routed(FreeWRibbonCommandAction.GrowFont, EditingCommands.IncreaseFontSize);
        Routed(FreeWRibbonCommandAction.ShrinkFont, EditingCommands.DecreaseFontSize);
        Routed(FreeWRibbonCommandAction.AlignLeft, EditingCommands.AlignLeft);
        Routed(FreeWRibbonCommandAction.AlignCenter, EditingCommands.AlignCenter);
        Routed(FreeWRibbonCommandAction.AlignRight, EditingCommands.AlignRight);
        Routed(FreeWRibbonCommandAction.AlignJustify, EditingCommands.AlignJustify);
        Routed(FreeWRibbonCommandAction.Bullets, EditingCommands.ToggleBullets);
        Routed(FreeWRibbonCommandAction.Numbering, EditingCommands.ToggleNumbering);
        Routed(FreeWRibbonCommandAction.Select, ApplicationCommands.SelectAll);
        if (hostPorts is null && onFindReplace is not null)
        {
            registry.Bind(FreeWRibbonCommandAction.Find, new ActionRibbonCommand(onFindReplace));
            registry.Bind(FreeWRibbonCommandAction.Replace, new ActionRibbonCommand(onFindReplace));
        }
        // Home > Paragraph: apply multilevel/legal outline numbering (1, 1.1, 1.1.1) to the selected
        // paragraph(s); the outline definition persists to word/numbering.xml. Tab/Shift+Tab demote
        // and promote the outline depth (ListLevel) of the selected list paragraphs.
        // The top-level "freew.multilevel-list" id applies the first (standard decimal) preset directly
        // (clicking the button face vs. the dropdown arrow follows the same pattern as Word's gallery).
        registry.Bind(FreeWRibbonCommandAction.MultilevelList, new ActionRibbonCommand(() =>
            editor.ApplyMultiLevelListDefinition(MultilevelListDialogPlanner.DefaultDefinition)));
        registry.Bind(FreeWRibbonCommandAction.MultilevelDemote, new ActionRibbonCommand(() => editor.ChangeListLevel(+1)));
        registry.Bind(FreeWRibbonCommandAction.MultilevelPromote, new ActionRibbonCommand(() => editor.ChangeListLevel(-1)));
        // Predefined multilevel list preset commands — three Word-parity presets shown in the gallery.
        foreach (var preset in MultilevelListDialogPlanner.Presets)
        {
            var capturedPreset = preset;
            registry.Register(capturedPreset.CommandId, new ActionRibbonCommand(() =>
            {
                editor.Focus();
                editor.ApplyMultiLevelListDefinition(capturedPreset.Definition);
            }));
        }
        // "Define New Multilevel List" dialog: captures backed options (number of levels, start-at, and
        // the first three per-level number styles).
        registry.Bind(FreeWRibbonCommandAction.MultilevelDefine, new DefineMultilevelListCommand(editor));
        Routed(FreeWRibbonCommandAction.Cut, ApplicationCommands.Cut);
        Routed(FreeWRibbonCommandAction.Copy, ApplicationCommands.Copy);
        Routed(FreeWRibbonCommandAction.Paste, ApplicationCommands.Paste);
        // Home > Clipboard: paste-special. "Paste Text Only" strips all source formatting; "Merge
        // Formatting" matches the destination. In FreeW both resolve to match-destination insertion at
        // the caret (the pasted text inherits the caret run's formatting), routed through the editor's
        // undoable InsertText path. See DocumentView.PastePlainText / PasteMergeFormatting.
        registry.Bind(FreeWRibbonCommandAction.PastePlain, new ActionRibbonCommand(() => editor.PastePlainText()));
        registry.Bind(FreeWRibbonCommandAction.PasteMerge, new ActionRibbonCommand(() => editor.PasteMergeFormatting()));

        // Home > Clipboard > Format Painter: arm the painter from the current selection's run +
        // paragraph formatting; the editor stamps it onto the user's next mouse selection and disarms.
        registry.Bind(FreeWRibbonCommandAction.FormatPainter, new FreeWRibbonFormatPainterCommand(locked =>
        {
            editor.Focus();
            editor.ArmFormatPainter(locked);
        }));

        var fontFamily = new SelectionValueCommand(editor,
            (selection, value) => selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(value)),
            value => editor.TrySetSelectedRunFormatting(
                formatting => string.Equals(formatting.FontFamily, value, StringComparison.OrdinalIgnoreCase),
                formatting => formatting with { FontFamily = value }),
            () => editor.CurrentRunFormatting.FontFamily ?? string.Empty);
        registry.Bind(FreeWRibbonCommandAction.FontFamily, fontFamily);
        stateful.Add(("freew.font-family", fontFamily));
        stateStore.SetState("freew.font-family", fontFamily.GetState());

        var fontSize = new SelectionValueCommand(editor, (selection, value) =>
        {
            if (FreeWRibbonNumericValueParser.TryParseFontSize(
                    value,
                    CultureInfo.CurrentCulture,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    out var points))
            {
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
            }
        }, value =>
        {
            if (!FreeWRibbonNumericValueParser.TryParseFontSize(
                    value,
                    CultureInfo.CurrentCulture,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    out var points))
            {
                return false;
            }
            return editor.TrySetSelectedRunFormatting(
                formatting => formatting.FontSizePt is { } size && Math.Abs(size - points) < 0.0001,
                formatting => formatting with { FontSizePt = points });
        }, () => (editor.CurrentRunFormatting.FontSizePt ?? 11).ToString(
            "0.##", System.Globalization.CultureInfo.InvariantCulture));
        registry.Bind(FreeWRibbonCommandAction.FontSize, fontSize);
        stateful.Add(("freew.font-size", fontSize));
        stateStore.SetState("freew.font-size", fontSize.GetState());

        // Insert tab — Pages: prepend a cover page, insert a blank page, or drop a horizontal rule / page break at the caret.
        // Each mutates the model through the view's undo/redo bus and re-renders.
        // Insert > Pages > Cover Page gallery: Default (existing centred layout), Banded (dark-blue title
        // band), and Motion (right-aligned title with date). The top-level id inserts the default preset
        // so clicking the button face (not the dropdown arrow) always works as before.
        registry.Bind(FreeWRibbonCommandAction.CoverPage, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-default", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Default); }));
        registry.Register("freew.cover-page-banded", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Banded); }));
        registry.Register("freew.cover-page-motion", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCoverPage(CoverPagePreset.Motion); }));
        registry.Bind(FreeWRibbonCommandAction.BlankPage, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertBlankPage(); }));
        registry.Bind(FreeWRibbonCommandAction.HorizontalRule, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertHorizontalRule(); }));
        registry.Bind(FreeWRibbonCommandAction.PageBreak, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertPageBreak(); }));

        // Layout > Page Setup > Breaks: section/column breaks. The page-break item reuses the existing
        // command (registered above). Each section break inserts a paragraph whose SectionBreak property
        // is set to the appropriate SectionBreakKind, inheriting the current document's page settings.
        registry.Bind(FreeWRibbonCommandAction.ColumnBreak, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertColumnBreak(); }));
        registry.Bind(FreeWRibbonCommandAction.SectionBreakNextPage, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.NextPage); }));
        registry.Bind(FreeWRibbonCommandAction.SectionBreakContinuous, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.Continuous); }));
        registry.Bind(FreeWRibbonCommandAction.SectionBreakEvenPage, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.EvenPage); }));
        registry.Bind(FreeWRibbonCommandAction.SectionBreakOddPage, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertSectionBreak(SectionBreakKind.OddPage); }));

        // Insert tab — insert a small 2x2 table at the caret (routes through the undo/redo bus).
        tableCommands.Bind(FreeWRibbonCommandAction.Table, new InsertTableCommand(editor, rows: 2, columns: 2));
        // Insert tab — Table Tools: structural edits to the table containing the caret (all undoable).
        tableCommands.Register("freew.table-insert-row", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableRow(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableDeleteRow, new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTableRow(); }));
        tableCommands.Register("freew.table-insert-col", new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableColumn(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableDeleteCol, new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTableColumn(); }));
        // Insert tab — Table Tools: merge the selected cells / split a merged cell (all undoable).
        tableCommands.Register("freew.merge-cells", new ActionRibbonCommand(() => { editor.Focus(); editor.MergeSelectedCells(); }));
        tableCommands.Register("freew.split-cell", new SplitCellRibbonCommand(editor));
        // Insert tab — Table Tools: pick/clear a fill colour for the caret's cell (sets model + re-renders).
        tableCommands.Register("freew.cell-shading", new CellShadingCommand(editor));
        // Insert tab — Table Tools: table-style toggles applied to the caret's table (sets model + re-renders).
        // Table Tools — Data: insert a computed formula field (=SUM(ABOVE) etc.) into the caret's cell.
        tableCommands.Bind(FreeWRibbonCommandAction.TableFormula, new TableFormulaCommand(editor));
        // Table Tools — Properties: open the four-tab Table Properties dialog for the caret's table.
        tableCommands.Bind(FreeWRibbonCommandAction.TableProperties, new TablePropertiesCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.TableHeaderRow, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableHeaderRow(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableBandedRows, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableBandedRows(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableRepeatHeader, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableRepeatHeaderRow(); }));

        // Table Tools — Directional insert/delete
        tableCommands.Bind(FreeWRibbonCommandAction.TableInsertAbove, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableRowAbove(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableInsertColLeft, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableColumnLeft(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableDelete, new ActionRibbonCommand(() => { editor.Focus(); editor.DeleteTable(); }));
        // Table Tools — Merge/Split enhancements
        tableCommands.Bind(FreeWRibbonCommandAction.SplitTable, new ActionRibbonCommand(() => { editor.Focus(); editor.SplitTable(); }));
        // Table Tools — Select
        tableCommands.Bind(FreeWRibbonCommandAction.TableSelectTable, new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTable(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableSelectRow, new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableRow(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableSelectCol, new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableColumn(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableSelectCell, new ActionRibbonCommand(() => { editor.Focus(); editor.SelectTableCell(); }));
        // Table Tools — View Gridlines (toggle; display-only)
        tableCommands.Bind(FreeWRibbonCommandAction.TableViewGridlines, new ActionRibbonCommand(() => { editor.ViewGridlines = !editor.ViewGridlines; editor.Focus(); }));
        // Table Tools — Cell Size
        tableCommands.Bind(FreeWRibbonCommandAction.TableRowHeight, new TablePropertiesCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.TableColWidth, new TablePropertiesCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.TableDistributeRows, new ActionRibbonCommand(() => { editor.Focus(); editor.DistributeTableRows(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableDistributeCols, new ActionRibbonCommand(() => { editor.Focus(); editor.DistributeTableColumns(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableAutofitContents, new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Contents); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableAutofitWindow, new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Window); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableAutofitFixed, new ActionRibbonCommand(() => { editor.Focus(); editor.SetTableAutoFit(AutoFitMode.Fixed); }));
        // Table Tools — Cell Alignment (9-way)
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignTopLeft, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Left); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignTopCenter, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Center); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignTopRight, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Top, FreeW.Core.Model.TextAlignment.Right); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignMiddleLeft, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Left); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignMiddleCenter, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Center); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignMiddleRight, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Center, FreeW.Core.Model.TextAlignment.Right); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignBottomLeft, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Left); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignBottomCenter, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Center); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellAlignBottomRight, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellAlignment(TableCellVerticalAlignment.Bottom, FreeW.Core.Model.TextAlignment.Right); }));
        // Table Tools — Cell Margins (opens Table Properties dialog)
        tableCommands.Bind(FreeWRibbonCommandAction.TableCellMargins, new TablePropertiesCommand(editor));
        // Table Design — Style Options toggles
        tableCommands.Bind(FreeWRibbonCommandAction.TableLastRow, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableLastRow(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableFirstColumn, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableFirstColumn(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableLastColumn, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableLastColumn(); }));
        tableCommands.Bind(FreeWRibbonCommandAction.TableBandedCols, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleTableBandedColumns(); }));
        // Table Design > Draw Borders: drag-to-insert table (prompted dimensions) and eraser-merges right.
        tableCommands.Bind(FreeWRibbonCommandAction.DrawTable, new DrawTableCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.Eraser, new EraserCommand(editor));
        // Table Layout Data group — Convert to Text
        tableCommands.Bind(FreeWRibbonCommandAction.TableToText, new ActionRibbonCommand(() => { editor.Focus(); editor.ConvertTableToText('\t'); }));
        // Table Design — Cell Borders picker (per-edge borders for the caret cell).
        tableCommands.Register("freew.cell-borders", new CellBordersCommand(editor));
        // Table Layout > Alignment — Text Direction cycling (Horizontal → Rotate90 → Rotate270 → Horizontal).
        tableCommands.Bind(FreeWRibbonCommandAction.CellTextDirectionHorizontal, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Horizontal); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellTextDirectionRotate90, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Rotate90); }));
        tableCommands.Bind(FreeWRibbonCommandAction.CellTextDirectionRotate270, new ActionRibbonCommand(() => { editor.Focus(); editor.SetCaretCellTextDirection(CellTextDirection.Rotate270); }));

        // Insert tab — Text: pick a .docx file and insert its body content at the caret (block merge).
        registry.Bind(FreeWRibbonCommandAction.InsertFile, new InsertFileCommand(editor));
        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Bind(FreeWRibbonCommandAction.Picture, new InsertPictureCommand(editor));
        // Insert tab — Illustrations: open the searchable icon picker and insert the chosen SVG
        // icon as a rasterised InlineImage (same round-trip path as Insert Picture).
        registry.Bind(FreeWRibbonCommandAction.InsertIcon, new InsertIconCommand(editor));
        // Insert tab — Illustrations > Screenshot: the top-level "freew.screenshot" id only opens the
        // dropdown (no direct insert, so it isn't registered — mirroring "freew.shapes" above). "Screen
        // Clipping" drag-selects a screen region and inserts the captured PNG as an inline image through
        // the exact same InsertImage path as Insert Picture.
        registry.Bind(FreeWRibbonCommandAction.ScreenClipping, new ScreenClippingCommand(editor));
        // Insert tab — Illustrations: resize the selected inline image (height scales proportionally).
        registry.Bind(FreeWRibbonCommandAction.ImageSize, new ImageSizeCommand(editor));
        // Insert tab — Illustrations: set the selected image's accessibility alt text (wp:docPr @descr),
        // and align the image's (image-only) paragraph left/center/right. Both mutate the model + re-render.
        registry.Bind(FreeWRibbonCommandAction.ImageAltText, new ImageAltTextCommand(editor));
        // Picture Format tab — Arrange > Position.
        registry.Register("freew.image-position", new ImagePositionCommand(editor));
        // Picture Format tab — Adjust > Corrections (brightness/contrast presets + dialog).
        foreach (var preset in ImageAdjustmentCommandPlanner.AdjustmentPresets
                     .Where(item => item.Channel is ImageAdjustmentChannel.Brightness or ImageAdjustmentChannel.Contrast))
            registry.Bind(preset.Action, BuildImageAdjustmentPresetCommand(editor, preset));
        registry.Bind(FreeWRibbonCommandAction.ImageAdjustDialog,      new ImageAdjustDialogCommand(editor));
        // Picture Format tab — Adjust > Color (saturation presets + dialog).
        foreach (var preset in ImageAdjustmentCommandPlanner.AdjustmentPresets
                     .Where(item => item.Channel == ImageAdjustmentChannel.Saturation))
            registry.Bind(preset.Action, BuildImageAdjustmentPresetCommand(editor, preset));
        registry.Bind(FreeWRibbonCommandAction.ImageColorDialog,       new ImageColorDialogCommand(editor));
        // Picture Format tab — Adjust > Transparency (presets + dialog).
        foreach (var preset in ImageAdjustmentCommandPlanner.AdjustmentPresets
                     .Where(item => item.Channel == ImageAdjustmentChannel.Transparency))
            registry.Bind(preset.Action, BuildImageAdjustmentPresetCommand(editor, preset));
        registry.Bind(FreeWRibbonCommandAction.ImageTransparencyDialog,new ImageTransparencyDialogCommand(editor));
        // Picture Format tab — Adjust > Crop / Reset / Border.
        registry.Bind(FreeWRibbonCommandAction.ImageCrop,   new ImageCropCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ImageReset,  new ImageResetCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ImageBorder, new ImageBorderCommand(editor));
        // Picture Format tab — Adjust > Color > Recolor presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.RecolorPresets
                     .Where(item => item.ColorTemperature is null))
            registry.Bind(preset.Action, BuildImageRecolorPresetCommand(editor, preset));
        // Picture Format tab — Adjust > Color > Color Tone presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.RecolorPresets
                     .Where(item => item.ColorTemperature is not null))
            registry.Bind(preset.Action, BuildImageRecolorPresetCommand(editor, preset));
        // Picture Format tab — Adjust > Picture Effects: Shadow presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets
                     .Where(item => item.Channel == ImageEffectChannel.Shadow))
            RegisterImageEffectPreset(registry, editor, preset);
        // Picture Format tab — Adjust > Picture Effects: Reflection presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets
                     .Where(item => item.Channel == ImageEffectChannel.Reflection))
            RegisterImageEffectPreset(registry, editor, preset);
        // Picture Format tab — Adjust > Picture Effects: Glow presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets
                     .Where(item => item.Channel == ImageEffectChannel.Glow))
            RegisterImageEffectPreset(registry, editor, preset);
        // Picture Format tab — Adjust > Picture Effects: Soft Edges presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets
                     .Where(item => item.Channel == ImageEffectChannel.SoftEdge))
            RegisterImageEffectPreset(registry, editor, preset);
        // Picture Format tab — Adjust > Picture Effects: Bevel presets.
        foreach (var preset in ImageAdjustmentCommandPlanner.EffectPresets
                     .Where(item => item.Channel == ImageEffectChannel.Bevel))
            RegisterImageEffectPreset(registry, editor, preset);
        // Picture Format tab — Adjust > Artistic Effects (W25).
        // Each command sets InlineImage.ArtisticEffect and invalidates the render (non-destructive).
        foreach (var preset in ImageAdjustmentCommandPlanner.ArtisticEffectPresets)
            registry.Register(preset.CommandId, new ImageArtisticEffectCommand(editor, preset.Effect));
        // Artistic Effects: top-level gallery opener.
        registry.Register("freew.image-artistic",               new ActionRibbonCommand(() =>
        {
            DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Choose an artistic effect from the dropdown menu.", "Artistic Effects");
        }));
        // Picture Format tab — Picture Styles gallery presets.
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            var p = preset;
            registry.Register($"freew.image-style-{p.Id}", new FreeWRibbonStatefulPortCommand(
                _ => editor.ApplySelectedImageStyle(p),
                () => new RibbonCommandState(IsEnabled: editor.SelectedImage() is not null),
                () => { editor.Focus(); }));
        }
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
        registry.Bind(FreeWRibbonCommandAction.Equation, new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertEquation(EquationPresetCatalog.CreateDefaultEquation());
        }));
        // Equation gallery presets (Insert > Media > Equation dropdown). Each inserts one OMML structure
        // at the caret as an editable starting point; all round-trip through the model/IO layer.
        foreach (var preset in EquationPresetCatalog.Presets)
        {
            var command = new ActionRibbonCommand(() =>
                InsertEquationPreset(editor, preset.CreateEquation()));
            registry.Register(preset.CommandId, command);
            registry.Register(preset.LegacyCommandId, command);
        }
        registry.Bind(FreeWRibbonCommandAction.Chart, new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = InsertChartDialog.Prompt(Application.Current?.MainWindow);
            if (chart is not null)
                editor.InsertChart(chart);
        }));
        // Shape Size: reuse ImageSizeDialog (same W/H in points).
        registry.Bind(FreeWRibbonCommandAction.ShapeSize, new ShapeSizeCommand(editor));
        // Alt Text: text prompt for shape or WordArt.
        registry.Bind(FreeWRibbonCommandAction.ShapeAltText, new ShapeAltTextCommand(editor));
        // Drawing Tools > Arrange — Position (opens the same dialog as image-position, applied to shape).
        registry.Register("freew.shape-position", new ShapePositionCommand(editor));

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

        registry.Bind(FreeWRibbonCommandAction.Wordart, new ActionRibbonCommand(() =>
        {
            editor.Focus();
            editor.InsertWordArt(WordArt.Create("WordArt", WordArtStyle.GradientFill));
        }));
        registry.Bind(FreeWRibbonCommandAction.Smartart, new ActionRibbonCommand(() =>
        {
            var owner = Application.Current?.MainWindow;
            var result = InsertSmartArtDialog.Prompt(owner);
            if (result is null) return;
            editor.Focus();
            editor.InsertSmartArt(result);
        }));
        // SmartArt Design contextual tab — gallery placeholder commands (no-ops; galleries are injected
        // as live-preview custom content via InjectGallery; these ids must be registered so the ribbon
        // renderer does not log "unknown command" warnings for the stub buttons).
        registry.Register("freew.smartart-change-layout", EmptyRibbonCommand.Instance);
        registry.Register("freew.smartart-change-colors", EmptyRibbonCommand.Instance);
        registry.Bind(FreeWRibbonCommandAction.Object, new InsertEmbeddedObjectCommand(editor));
        // Insert tab — Links: prompt for a URL and apply it as a hyperlink over the selection.
        registry.Bind(FreeWRibbonCommandAction.Hyperlink, new InsertHyperlinkCommand(editor));
        // Insert tab — Links: manage the hyperlink at the caret — change its URL, remove it, or set a ScreenTip.
        registry.Bind(FreeWRibbonCommandAction.EditHyperlink, new EditHyperlinkCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.RemoveHyperlink, new RemoveHyperlinkCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.HyperlinkTooltip, new HyperlinkTooltipCommand(editor));
        // Insert tab — References: prompt for footnote text and insert a footnote reference at the caret.
        referenceCommands.Bind(FreeWRibbonCommandAction.Footnote, new InsertFootnoteCommand(editor));
        // Insert tab — References: prompt for endnote text and insert an endnote reference at the caret.
        referenceCommands.Bind(FreeWRibbonCommandAction.Endnote, new InsertEndnoteCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.NextFootnote, new NavigateNoteCommand(editor, footnote: true, previous: false));
        referenceCommands.Bind(FreeWRibbonCommandAction.PreviousFootnote, new NavigateNoteCommand(editor, footnote: true, previous: true));
        referenceCommands.Bind(FreeWRibbonCommandAction.NextEndnote, new NavigateNoteCommand(editor, footnote: false, previous: false));
        referenceCommands.Bind(FreeWRibbonCommandAction.PreviousEndnote, new NavigateNoteCommand(editor, footnote: false, previous: true));
        if (onToggleNotesPane is not null && isNotesPaneVisible is not null)
        {
            var notesPaneCmd = referenceCommands.BindToggle(FreeWRibbonCommandAction.ShowNotes,
                onToggleNotesPane,
                isNotesPaneVisible,
                prepareExecution: editor.CommitToModel);
            stateful.Add((
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ShowNotes),
                notesPaneCmd));
        }
        else
        {
            referenceCommands.Bind(FreeWRibbonCommandAction.ShowNotes, new ShowNotesCommand(editor));
        }
        // Insert tab — References: open the Footnote and Endnote numbering options dialog (number format,
        // start-at, restart mode). Applies to w:footnotePr / w:endnotePr in settings.xml.
        referenceCommands.Bind(FreeWRibbonCommandAction.FootnoteEndnoteOptions, new FootnoteEndnoteOptionsCommand(editor));
        // Insert tab — References: generate a Table of Contents from the heading outline at the caret,
        // and rebuild it in place (remove the prior TOC region + re-insert). Both route through the bus.
        referenceCommands.Bind(FreeWRibbonCommandAction.Toc, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfContents(); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.TocRefresh, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfContents(); }));
        referenceCommands.Register("freew.toc-add-text", new ApplyTocStyleCommand(editor, "Heading1"));
        referenceCommands.Register("freew.toc-addtext-none", new ApplyTocStyleCommand(editor, "Normal"));
        referenceCommands.Register("freew.toc-addtext-level1", new ApplyTocStyleCommand(editor, "Heading1"));
        referenceCommands.Register("freew.toc-addtext-level2", new ApplyTocStyleCommand(editor, "Heading2"));
        referenceCommands.Register("freew.toc-addtext-level3", new ApplyTocStyleCommand(editor, "Heading3"));
        // Insert tab — References: insert an in-text citation (pick an existing source or add a new one),
        // and insert a bibliography built from the document's sources at the caret (reversible).
        referenceCommands.Bind(FreeWRibbonCommandAction.Citation, new InsertCitationCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.ManageSources, new ManageSourcesCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.Bibliography, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertBibliography(); }));
        // Insert tab — References: select the active citation/bibliography style (APA / MLA / Chicago) used
        // by the citation + bibliography commands. The combo box delivers its label as SelectedValue.
        var citationStyle = new FreeWRibbonChoiceCommand(
            value => editor.ApplyCitationStyle(Citations.ParseStyle(value, editor.ActiveCitationStyle)),
            () => Citations.StyleName(editor.ActiveCitationStyle),
            state => stateStore.SetState("freew.citation-style", state));
        referenceCommands.Bind(FreeWRibbonCommandAction.CitationStyle, citationStyle);
        stateful.Add(("freew.citation-style", citationStyle));
        stateStore.SetState("freew.citation-style", citationStyle.GetState());
        // Insert tab — References: insert a numbered figure/table caption under the caret's block.
        referenceCommands.Bind(FreeWRibbonCommandAction.Caption, new InsertCaptionCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.InsertCaption_Figure, new InsertCaptionLabelCommand(editor, CaptionLabel.Figure));
        referenceCommands.Bind(FreeWRibbonCommandAction.InsertCaption_Table, new InsertCaptionLabelCommand(editor, CaptionLabel.Table));
        referenceCommands.Bind(FreeWRibbonCommandAction.InsertCaption_Equation, new InsertCaptionLabelCommand(editor, CaptionLabel.Equation));
        // Insert tab — References: insert a cross-reference (heading/bookmark/caption/footnote) at the caret.
        referenceCommands.Bind(FreeWRibbonCommandAction.CrossReference, new InsertCrossReferenceCommand(editor));
        // Insert tab — References: mark the selection (or a prompted term) for the document index, and
        // insert an alphabetical index built from the marked terms at the caret (reversibly via the bus).
        referenceCommands.Bind(FreeWRibbonCommandAction.IndexMark, new MarkIndexEntryCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.IndexInsert, new InsertIndexCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.IndexRefresh, new UpdateIndexCommand(editor));
        // Insert tab — References: generate a Table of Figures from the document's figure captions at the
        // caret, and rebuild it in place (remove the prior region + re-insert). Both route through the bus.
        referenceCommands.Bind(FreeWRibbonCommandAction.Tof, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.Tof_Figure, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Figure); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.Tof_Table, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Table); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.Tof_Equation, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertTableOfFigures(CaptionLabel.Equation); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.TofRefresh, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.TofRefresh_Figure, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Figure); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.TofRefresh_Table, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Table); }));
        referenceCommands.Bind(FreeWRibbonCommandAction.TofRefresh_Equation, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(CaptionLabel.Equation); }));
        // Insert tab — References: mark the selection as a legal citation (a hidden TA field), and insert /
        // rebuild a Table of Authorities built from those marks, grouped by category (reversibly via the bus).
        referenceCommands.Bind(FreeWRibbonCommandAction.MarkCitation, new MarkCitationCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.TableOfAuthorities, new InsertTableOfAuthoritiesCommand(editor));
        referenceCommands.Bind(FreeWRibbonCommandAction.TableOfAuthoritiesRefresh, new ActionRibbonCommand(() => { editor.Focus(); editor.RefreshTableOfAuthorities(); }));
        // Insert tab — Links: name the caret's paragraph as a bookmark target (an invisible marker).
        registry.Bind(FreeWRibbonCommandAction.Bookmark, new InsertBookmarkCommand(editor));
        // Insert tab — Links: apply an internal link (to an existing bookmark) over the selection.
        registry.Bind(FreeWRibbonCommandAction.LinkBookmark, new LinkToBookmarkCommand(editor));
        // Insert tab — Links: open the Bookmark Manager (list bookmarks with Go To + Delete).
        registry.Bind(FreeWRibbonCommandAction.BookmarkManager, new BookmarkManagerCommand(editor));

        // Insert tab — Quick Parts (AutoText): a shared snippet library persisted under FreeW's data
        // folder. "Save Selection" captures the selection's text and stores it under a prompted name;
        // "Insert Quick Part" picks a saved snippet and drops its text at the caret (reversibly).
        var quickParts = QuickPartLibrary.Load();
        registry.Bind(FreeWRibbonCommandAction.SaveQuickpart, new SaveQuickPartCommand(editor, quickParts));
        registry.Register("freew.insert-quickpart", new InsertQuickPartCommand(editor, quickParts));
        // "Building Blocks Organizer" opens a manager over that same library: list + preview, Insert, Delete.
        registry.Bind(FreeWRibbonCommandAction.BuildingBlocksOrganizer, new BuildingBlocksOrganizerCommand(editor, quickParts));

        // Insert tab — Controls: insert a content control (w:sdt) around the selection. The plain-text
        // control wraps the selection (or a placeholder) as an editable region; the checkbox control
        // drops a toggleable ☐/☒ checkbox. Both round-trip through docx as a w:sdt.
        registry.Bind(FreeWRibbonCommandAction.CcText, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertPlainTextControl(); }));
        registry.Bind(FreeWRibbonCommandAction.CcRichtext, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertRichTextControl(); }));
        registry.Bind(FreeWRibbonCommandAction.CcCheckbox, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertCheckBoxControl(); }));
        registry.Bind(FreeWRibbonCommandAction.CcDate, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertDatePickerControl(); }));
        registry.Bind(FreeWRibbonCommandAction.CcDropdown, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertDropDownListControl(); }));
        registry.Bind(FreeWRibbonCommandAction.CcCombo, new ActionRibbonCommand(() => { editor.Focus(); editor.InsertComboBoxControl(); }));

        // Review tab — Comments: prompt for comment text and attach it over the current selection.
        registry.Bind(FreeWRibbonCommandAction.NewComment, new NewCommentCommand(editor));
        // Review tab — Comments: reply to / resolve the comment thread covering the caret (modern threaded
        // comments). Reply prompts for text and appends a child comment; Resolve toggles the thread's done flag.
        registry.Bind(FreeWRibbonCommandAction.ReplyComment, new ReplyCommentCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ResolveComment, new ResolveCommentCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.DeleteComment, new DeleteCommentCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.PreviousComment, new NavigateCommentCommand(editor, previous: true));
        registry.Bind(FreeWRibbonCommandAction.NextComment, new NavigateCommentCommand(editor, previous: false));
        registry.Bind(FreeWRibbonCommandAction.ShowComments, new ShowCommentsCommand(editor));

        // Review tab — Proofing: open the read-only Word Count / Statistics dialog. Commits pending
        // edits first so the counts reflect the current text, then computes from the model.
        registry.Bind(FreeWRibbonCommandAction.Statistics, new StatisticsCommand(editor));

        // Review tab — Proofing > Thesaurus (Shift+F7): opens the Thesaurus docked pane and looks up
        // synonyms for the selected/caret word in the bundled compact synonym dictionary (~3 000 headwords,
        // Moby II derivative, public domain). The action callback supplied by the host toggles the pane
        // and triggers a lookup; a no-op is registered when no host callback is wired (e.g. unit tests).
        if (hostPorts is null)
        {
            if (onToggleThesaurus is not null)
                registry.Bind(FreeWRibbonCommandAction.Thesaurus, new ActionRibbonCommand(onToggleThesaurus));
            else
                registry.Bind(FreeWRibbonCommandAction.Thesaurus, new ActionRibbonCommand(() =>
                {
                    DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                        "Thesaurus: no synonyms pane is wired. Host must supply onToggleThesaurus.", "Thesaurus");
                }));
        }

        // Review tab — Show Markup > Show Revisions in Balloons: toggle the right-margin balloon overlay.
        // Comments and tracked-change revisions render as rounded rectangle callouts connected to their
        // anchored text by dashed leader lines, in a 200px strip to the right of the editor. The callback
        // is supplied by the host (BalloonOverlay.Toggle()); a no-op is registered in unit-test contexts.
        if (onToggleBalloons is not null)
            registry.Bind(FreeWRibbonCommandAction.ShowMarkupBalloons, new ActionRibbonCommand(onToggleBalloons));
        else
            registry.Bind(FreeWRibbonCommandAction.ShowMarkupBalloons, EmptyRibbonCommand.Instance);

        // Review tab — Proofing: custom dictionary + spelling options. The custom dictionary is a
        // word-per-line .lex file persisted under FreeW's data folder; its Uri is registered with the
        // editor's WPF spell checker so those words stop being flagged. "Add to Dictionary" takes the
        // misspelled word at the caret, adds it to the dictionary (+ persists), and re-reads the file so
        // it is no longer underlined. "Spell Check" is a stateful toggle over SpellCheck.IsEnabled.
        var customDictionary = CustomDictionaryStore.Load();
            editor.RegisterCustomDictionary(customDictionary.EnsurePersisted());
        registry.Bind(FreeWRibbonCommandAction.AddToDictionary, new AddToDictionaryCommand(editor, customDictionary));
        var spellCheckToggle = new SpellCheckToggleCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.SpellcheckToggle, spellCheckToggle);
        stateful.Add(("freew.spellcheck-toggle", spellCheckToggle));

        // Review > Speech > Read Aloud: shared lifecycle/state policy with a thin WPF speech/event adapter.
        // The command remains lazy and keeps the established checked-state projection in the ribbon store.
        var readAloud = new WpfReadAloudCommandAdapter(editor);
        readAloud.StateChanged += () => stateStore.SetState("freew.read-aloud", readAloud.GetState());
        registry.Bind(FreeWRibbonCommandAction.ReadAloud, readAloud);
        stateful.Add(("freew.read-aloud", readAloud));

        // Review tab — Tracking/Changes: toggle Track Changes mode (stateful so the ribbon reflects it). When
        // ON, marking the current selection as a tracked insertion/deletion is offered; turning it on
        // with a non-empty selection marks that selection as an insertion. Accept All / Reject All resolve
        // every tracked change on the model from the Changes dropdowns.
        var reviewTracking = ReviewTrackingRibbonWorkflow.Register(
            registry,
            new ReviewTrackingCommandBindings(
                PrepareExecution: () => editor.Focus(),
                IsTrackChangesEnabled: () => editor.TrackChangesEnabled,
                HasSelection: () => !editor.Selection.IsEmpty,
                ToggleTrackChanges: () => editor.TrackChangesEnabled = !editor.TrackChangesEnabled,
                MarkSelectionAsInsertion: () =>
                {
                    var dateXml = DateTimeOffset.UtcNow.ToString(
                        "yyyy-MM-ddTHH:mm:ssZ",
                        System.Globalization.CultureInfo.InvariantCulture);
                    editor.MarkSelectionAsRevision(RevisionKind.Inserted, editor.RevisionAuthor, dateXml);
                },
                IsTrackFormattingEnabled: () => editor.TrackFormattingEnabled,
                ToggleTrackFormatting: () => editor.TrackFormattingEnabled = !editor.TrackFormattingEnabled,
                GetDisplayForReview: () => editor.DisplayForReview,
                ApplyDisplayForReview: editor.ApplyDisplayForReview,
                ShowMarkupInsertionsAndDeletions: () => editor.ShowMarkupInsertionsAndDeletions,
                ApplyShowMarkupInsertionsAndDeletions: editor.ApplyShowMarkupInsertionsAndDeletions,
                ShowMarkupComments: () => editor.ShowMarkupComments,
                ApplyShowMarkupComments: editor.ApplyShowMarkupComments,
                ShowMarkupFormatting: () => editor.ShowMarkupFormatting,
                ApplyShowMarkupFormatting: editor.ApplyShowMarkupFormatting,
                AcceptAllRevisions: editor.AcceptAllRevisions,
                RejectAllRevisions: editor.RejectAllRevisions));

        // Review tab — Tracking display controls: Display for Review and Show Markup per-category toggles.
        //
        // Display for Review exposes a dropdown backed by ReviewDisplayMode. The root button
        // always reflects the current mode. No Markup and Original are now implemented — each hides the
        // opposite set of revision runs using a visually-transparent technique that keeps every run in
        // the WPF tree so CommitToModel can round-trip text + RevisionMarker safely.
        stateful.Add(("freew.display-for-review", reviewTracking.DisplayAllMarkup));
        stateful.Add(("freew.display-for-review-simple-markup", reviewTracking.DisplaySimpleMarkup));
        stateful.Add(("freew.display-for-review-no-markup", reviewTracking.DisplayNoMarkup));
        stateful.Add(("freew.display-for-review-original", reviewTracking.DisplayOriginal));

        // Show Markup > Insertions and Deletions: stateful toggle — OFF suppresses the revision colour
        // and underline/strikethrough chrome but the RevisionMarker tag is still written so revisions
        // survive CommitToModel unchanged (round-trip safe).
        stateful.Add(("freew.show-markup-insertions-deletions", reviewTracking.ShowInsertionsAndDeletions));

        // Show Markup > Comments: stateful toggle — OFF suppresses the comment background highlight
        // but the CommentMarker tag is still written so comment ids survive CommitToModel unchanged
        // (round-trip safe).
        stateful.Add(("freew.show-markup-comments", reviewTracking.ShowComments));

        // Show Markup > Formatting: stateful toggle — OFF suppresses the dotted underline decoration
        // that marks tracked formatting changes. The FormatRevisionMarker tag is still written
        // unconditionally so FormatRevision survives CommitToModel unchanged (round-trip safe).
        stateful.Add(("freew.show-markup-formatting", reviewTracking.ShowFormatting));

        // Review tab — single-revision reviewing surface (the Reviewing Pane). The toggle shows/hides the
        // dockable revisions list; Accept/Reject act on the SELECTED single change and Previous/Next step
        // through them. All four delegate to the host, which owns the pane and drives the pure RevisionList.
        if (onToggleReviewingPane is not null && isReviewingPaneVisible is not null)
        {
            var reviewingPane = registry.BindToggle(FreeWRibbonCommandAction.ReviewingPane,
                onToggleReviewingPane,
                isReviewingPaneVisible);
            stateful.Add((
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ReviewingPane),
                reviewingPane));
        }
        if (hostPorts is null && onAcceptThisChange is not null)
            registry.Bind(FreeWRibbonCommandAction.AcceptThis, new ActionRibbonCommand(onAcceptThisChange));
        if (hostPorts is null && onRejectThisChange is not null)
            registry.Bind(FreeWRibbonCommandAction.RejectThis, new ActionRibbonCommand(onRejectThisChange));
        if (hostPorts is null && onPreviousChange is not null)
            registry.Bind(FreeWRibbonCommandAction.PreviousChange, new ActionRibbonCommand(onPreviousChange));
        if (hostPorts is null && onNextChange is not null)
            registry.Bind(FreeWRibbonCommandAction.NextChange, new ActionRibbonCommand(onNextChange));

        // Review tab — Protect: Mark as Final. A stateful toggle over Word's advisory read-only flag:
        // turning it on makes the editor read-only, shows the "Marked as Final" banner and persists the
        // _MarkAsFinal custom property; "Edit Anyway" (or toggling off) clears it. The checked state
        // reflects whether the document is currently marked final.
        var markAsFinal = new MarkAsFinalToggleCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.MarkAsFinal, markAsFinal);
        stateful.Add(("freew.mark-as-final", markAsFinal));

        // Review tab — Protect: Restrict Editing. Opens the Restrict Editing pane to choose the allowed
        // editing type (No changes / Tracked changes / Comments / Filling in forms) and start enforcing,
        // or stop protection. The chosen mode is enforced on the live editor and emits word/settings.xml's
        // w:documentProtection on save. The toggle reflects whether protection is currently enforced.
        var restrictEditing = new RestrictEditingToggleCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.RestrictEditing, restrictEditing);
        stateful.Add(("freew.restrict-editing", restrictEditing));

        // Review tab — Compare: open a second .docx and load a comparison of the current document against
        // it as tracked changes (insertions/deletions relative to the opened "original").
        registry.Bind(FreeWRibbonCommandAction.Compare, new CompareDocumentsCommand(editor));

        // Review tab — Combine: open the original (base) document plus a second reviewer's revised copy and
        // merge BOTH reviewers' edits (the current document is reviewer A, the opened file is reviewer B)
        // into one document whose tracked changes preserve each reviewer's authorship.
        registry.Bind(FreeWRibbonCommandAction.Combine, new CombineDocumentsCommand(editor));

        // Review tab — Inspect Document: report the metadata the document carries (comments, tracked
        // changes, document properties, bookmarks) via the pure DocumentInspector, and let the user
        // selectively remove categories. Applied removals mutate editor.Model in place and re-render.
        registry.Bind(FreeWRibbonCommandAction.InspectDocument, new InspectDocumentCommand(editor));

        // Review tab — Inspect > Check Accessibility: commit pending edits, run the pure AccessibilityChecker
        // over the model, and show the report (issues grouped by severity) in a read-only modal. Read-only.
        registry.Bind(FreeWRibbonCommandAction.CheckAccessibility, new CheckAccessibilityCommand(editor));

        // Insert tab — Header & Footer: prompt for header/footer text, or drop a page-number field
        // into the footer. These edit the model's Header/Footer directly (saved into docx + printed).
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Header, new HeaderFooterCommand(editor, isFooter: false, askHeaderFooterText: askHeaderFooterText));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Footer, new HeaderFooterCommand(editor, isFooter: true, askHeaderFooterText: askHeaderFooterText));
        // Insert > Header & Footer > Page Number gallery: top/bottom/current position + format dialog.
        // The top-level id inserts into the footer (Word's default button-face action).
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumber, new InsertPageNumberCommand(() => editor, PageNumberPosition.Bottom));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberTop, new InsertPageNumberCommand(() => editor, PageNumberPosition.Top));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberBottom, new InsertPageNumberCommand(() => editor, PageNumberPosition.Bottom));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberCurrent, new InsertPageNumberCommand(resolveFieldTarget, PageNumberPosition.Current));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.PageNumberFormat, new PageNumberFormatCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.Field, new InsertFieldCommand(resolveFieldTarget, askField));
        registry.Bind(FreeWRibbonCommandAction.ToggleFieldCodes, new ToggleFieldCodesCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.UpdateFields, new UpdateFieldsCommand(editor));

        // Header & Footer Design contextual tab — per-slot editors.
        // Slot naming: "header"/"footer" = default; "even-header"/"even-footer" = even pages;
        // "first-header"/"first-footer" = first page. Each writes FinalSectionHeadersFooters directly.
        // When the host supplies onOpenHeaderFooterPane, the commands open the docked pane (which
        // preserves run formatting). Otherwise they fall back to the plain-text dialog.
        IRibbonCommand HfEditCmd(string slot) =>
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, slot, onOpenHeaderFooterPane)
                : new EditHeaderSlotCommand(editor, slot);
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditHeader,       HfEditCmd("header"));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditFooter,       HfEditCmd("footer"));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditEvenHeader,  HfEditCmd("even-header"));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditEvenFooter,  HfEditCmd("even-footer"));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditFirstHeader, HfEditCmd("first-header"));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfEditFirstFooter, HfEditCmd("first-footer"));

        // Header & Footer Design contextual tab — options toggles (stateful so IsChecked reflects model).
        var diffFirstPage = new DifferentFirstPageToggleCommand(editor);
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfDifferentFirstPage, diffFirstPage);
        stateful.Add(("freew.hf-different-first-page", diffFirstPage));

        var diffOddEven = new DifferentOddEvenPagesCommand(editor);
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfDifferentOddEven, diffOddEven);
        stateful.Add(("freew.hf-different-odd-even", diffOddEven));

        // Header & Footer Design contextual tab — position numerics (stateful so the value tracks model).
        var headerFromTop = new HeaderFromTopCommand(editor);
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfHeaderFromTop, headerFromTop);
        stateful.Add(("freew.hf-header-from-top", headerFromTop));

        var footerFromBottom = new FooterFromBottomCommand(editor);
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfFooterFromBottom, footerFromBottom);
        stateful.Add(("freew.hf-footer-from-bottom", footerFromBottom));

        // Header & Footer Design contextual tab — navigation + close.
        // Go-to-header / go-to-footer open the pane (when available) for the default slots.
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfGoToHeader,
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, "header", onOpenHeaderFooterPane)
                : new GoToHeaderCommand(editor));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfGoToFooter,
            onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, "footer", onOpenHeaderFooterPane)
                : new GoToFooterCommand(editor));
        // Close Header and Footer: hides the pane (when available) and returns focus to the body.
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfClose,
            onCloseHeaderFooterPane is not null
                ? new ActionRibbonCommand(onCloseHeaderFooterPane)
                : new CloseHeaderFooterCommand(editor));

        // Header & Footer Design contextual tab — insert into default header/footer slot.
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfInsertPageNumber,  new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.PageNumber));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfInsertPageNumberFooter, new InsertIntoHeaderSlotCommand(editor, isFooter: true,  InsertSlotKind.PageNumber));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfInsertDatetime,     new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DateTime));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.HfInsertField,        new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DocumentInfo));

        // Insert tab — Symbols: pick a glyph from a grid, or a formatted current date/time string, and
        // insert it at the caret as ordinary text (flows through the normal edit/undo path).
        registry.Bind(FreeWRibbonCommandAction.Symbol, new InsertSymbolCommand(editor));
        headerFooterCommands.Bind(FreeWRibbonCommandAction.Datetime, new InsertDateTimeCommand(resolveFieldTarget));

        // Home > Font > Text Colour / Highlight: pick a colour from a small palette and apply it to
        // the selection (foreground reuses TextElement.Foreground; highlight uses TextElement.Background).
        registry.Bind(FreeWRibbonCommandAction.FontColor, new ColorPickCommand(editor, isHighlight: false));
        registry.Bind(FreeWRibbonCommandAction.Highlight, new ColorPickCommand(editor, isHighlight: true));

        // Home > Font: clear all character formatting in the selection (reset every run to the document
        // default, keeping text). Insert > Pages: apply a drop cap (enlarged leading letter) to the
        // caret's paragraph. Both route through the view's undo/redo bus and re-render.
        registry.Bind(FreeWRibbonCommandAction.ClearFormatting, new ActionRibbonCommand(() => editor.ClearFormatting()));
        // Drop Cap top-level button: apply default (Dropped, 3 lines, 42 pt). Dropdown items:
        // Dropped / In Margin (apply with explicit position) / None (remove) / Options dialog.
        registry.Bind(FreeWRibbonCommandAction.DropCap,          new ActionRibbonCommand(() => editor.ApplyDropCap()));
        registry.Bind(FreeWRibbonCommandAction.DropCapDropped,  new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        registry.Bind(FreeWRibbonCommandAction.DropCapInMargin,new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        registry.Bind(FreeWRibbonCommandAction.DropCapNone,     new ActionRibbonCommand(() => editor.ClearDropCap()));
        registry.Bind(FreeWRibbonCommandAction.DropCapOptions,  new DropCapOptionsCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.DropCap_Dropped,  new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)));
        registry.Bind(FreeWRibbonCommandAction.DropCap_InMargin,new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)));
        registry.Bind(FreeWRibbonCommandAction.DropCap_None,     new ActionRibbonCommand(() => editor.ClearDropCap()));

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
            registry.Register(plan.CommandId, new InsertDocPropFieldCommand(resolveFieldTarget, plan.Kind));

        // Home > Font > Change Case: open a small menu to pick a target case (UPPERCASE / lowercase /
        // Sentence case / Capitalize Each Word / tOGGLE cASE) and recase the selection's text via the
        // pure ChangeCase helper. The replacement flows through the editor's normal edit/undo path.
        registry.Bind(FreeWRibbonCommandAction.ChangeCase, new ChangeCaseCommand(editor));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection,
        // and toggle Add/Remove Space Before/After. All route through the view's undo/redo bus.
        var lineSpacing = new FreeWRibbonNumericValueCommand(
            editor.SetLineSpacing,
            () => editor.CurrentParagraphFormatting.LineSpacing,
            minimumExclusive: 0,
            numberStyles: System.Globalization.NumberStyles.Float |
                System.Globalization.NumberStyles.AllowThousands,
            prepareExecution: () => { editor.Focus(); });
        registry.Bind(FreeWRibbonCommandAction.LineSpacing, lineSpacing);
        stateful.Add(("freew.line-spacing", lineSpacing));
        stateStore.SetState("freew.line-spacing", lineSpacing.GetState());
        registry.Bind(FreeWRibbonCommandAction.SpaceBeforeToggle, new ActionRibbonCommand(() => editor.ToggleSpaceBefore()));
        registry.Bind(FreeWRibbonCommandAction.SpaceAfterToggle, new ActionRibbonCommand(() => editor.ToggleSpaceAfter()));

        // Layout > Paragraph > numeric indent/spacing combos: exact-value controls that mirror Word's
        // Layout tab Paragraph group. Each is stateful so SelectionChanged can push the live value
        // back into the ribbon combo and the displayed number tracks the current paragraph.
        var indentLeft = new IndentLeftCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.IndentLeft, indentLeft);
        stateful.Add(("freew.indent-left", indentLeft));

        var indentRight = new IndentRightCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.IndentRight, indentRight);
        stateful.Add(("freew.indent-right", indentRight));

        var spaceBefore = new SpaceBeforeCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.SpaceBefore, spaceBefore);
        stateful.Add(("freew.space-before", spaceBefore));

        var spaceAfter = new SpaceAfterCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.SpaceAfter, spaceAfter);
        stateful.Add(("freew.space-after", spaceAfter));

        // Home > Font > Font dialog-launcher (freew.font-dialog): opens a two-tab dialog (Font tab +
        // Advanced tab) covering family/size/style/colour/effects on the Font tab and the full OpenType
        // advanced typography fields (CharacterSpacingPt, KerningMinSizePt, PositionPt, Ligatures,
        // StylisticSet, NumberForm, NumberSpacing) on the Advanced tab. Applies via ApplyFontFormatting
        // which pushes both WPF property values and model-only fields through the undo/redo bus.
        registry.Bind(FreeWRibbonCommandAction.FontDialog, new FontDialogCommand(editor));

        // Home > Paragraph: increase/decrease the left indent by one 0.5in step over the selection, and
        // open the Paragraph dialog to set left/right/first-line (incl. hanging) indents. All reversible.
        registry.Bind(FreeWRibbonCommandAction.IndentIncrease, new ActionRibbonCommand(() => { editor.Focus(); editor.IncreaseIndent(); }));
        registry.Bind(FreeWRibbonCommandAction.IndentDecrease, new ActionRibbonCommand(() => { editor.Focus(); editor.DecreaseIndent(); }));
        // freew.paragraph-dialog now opens the full two-tab Paragraph dialog (Indents and Spacing +
        // Line and Page Breaks), replacing the previous single-tab ParagraphIndentCommand. All fields
        // that ParagraphIndentCommand previously handled are present on the Indents and Spacing tab.
        registry.Bind(FreeWRibbonCommandAction.ParagraphDialog, new ParagraphDialogCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.TabsDialog, new TabsCommand(editor));

        // Home > Clipboard: Paste Special offers source-preserving RTF at an empty paragraph, plus
        // merge-destination and text-only paths through the shared platform clipboard boundary.
        registry.Bind(FreeWRibbonCommandAction.PasteSpecial, new PasteSpecialCommand(editor));

        // Home > Paragraph: toggle a box border on the selected paragraph(s), and pick/clear shading.
        registry.Bind(FreeWRibbonCommandAction.ParaBorder, new ActionRibbonCommand(() => editor.ToggleParagraphBorder()));
        registry.Bind(FreeWRibbonCommandAction.ParaShading, new ParagraphShadingCommand(editor));
        // Home / Design > Borders and Shading…: the full dialog (paragraph border, page border, shading).
        registry.Bind(FreeWRibbonCommandAction.BordersShading, new BordersAndShadingCommand(editor));

        // Home > Paragraph (Line and Page Breaks): flow-control toggles over the selected paragraph(s).
        // Each flips its pPr flag (keepNext/keepLines/widowControl) reversibly through the undo/redo bus.
        registry.Bind(FreeWRibbonCommandAction.KeepWithNext, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleKeepWithNext(); }));
        registry.Bind(FreeWRibbonCommandAction.KeepLines, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleKeepLinesTogether(); }));
        registry.Bind(FreeWRibbonCommandAction.WidowControl, new ActionRibbonCommand(() => { editor.Focus(); editor.ToggleWidowControl(); }));

        // Layout > Sort: open a small dialog (A→Z / Z→A + case-sensitive option) and sort the selected
        // paragraphs in place through the view's undo/redo bus.
        registry.Bind(FreeWRibbonCommandAction.Sort, new SortCommand(editor));

        // Layout > Table conversions: turn the selected paragraphs into a table (splitting on a chosen
        // delimiter) and turn the caret's table back into delimited paragraphs. Both route through the bus.
        registry.Bind(FreeWRibbonCommandAction.TextToTable, new TextToTableCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.TableToText, new TableToTextCommand(editor));

        registry.Bind(FreeWRibbonCommandAction.StyleNormal, new ApplyNamedStyleCommand(editor, "Normal"));
        registry.Bind(FreeWRibbonCommandAction.StyleHeading1, new ApplyNamedStyleCommand(editor, "Heading1"));
        registry.Bind(FreeWRibbonCommandAction.StyleHeading2, new ApplyNamedStyleCommand(editor, "Heading2"));
        registry.Bind(FreeWRibbonCommandAction.StyleHeading3, new ApplyNamedStyleCommand(editor, "Heading3"));
        registry.Bind(FreeWRibbonCommandAction.StyleTitle, new ApplyNamedStyleCommand(editor, "Title"));
        registry.Bind(FreeWRibbonCommandAction.StyleClear, new ActionRibbonCommand(() => { editor.Focus(); editor.SetParagraphStyle(null); }));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        var paragraphStyle = new ApplyParagraphStyleCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.Style, paragraphStyle);
        stateful.Add(("freew.style", paragraphStyle));
        stateStore.SetState("freew.style", paragraphStyle.GetState());

        // Home > Styles: New Style opens a dialog capturing name + formatting + based-on, creates a custom
        // DocumentStyle via the pure StyleManager and applies it to the selection. Manage Styles lets the
        // user modify or delete the catalog's styles (built-ins are guarded against deletion).
        registry.Bind(FreeWRibbonCommandAction.NewStyle, new NewStyleCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ManageStyles, new ManageStylesCommand(editor));

        // Design > Document Formatting: Themes apply a full preset, Colors preserve fonts while applying
        // a palette, Style Sets rewrite built-in styles, and Fonts preserve colours while applying a
        // heading/body font pair. All are backed document-wide style changes.
        var theme = new ApplyThemeCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.Theme, theme);
        stateful.Add(("freew.theme", theme));
        stateStore.SetState("freew.theme", theme.GetState());
        var styleSet = new ApplyStyleSetCommand(editor);
        registry.Bind(FreeWRibbonCommandAction.StyleSet, styleSet);
        stateful.Add(("freew.style-set", styleSet));
        stateStore.SetState("freew.style-set", styleSet.GetState());
        registry.Bind(FreeWRibbonCommandAction.ResetStyleSet, new ResetStyleSetCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ThemeColors, new ApplyThemeColorsCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.CustomizeColors, new CustomizeColorsCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ThemeFonts, new ApplyFontSetCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.CustomizeFonts, new CustomizeFontsCommand(editor));
        registry.Register("freew.paragraph-spacing", new ApplyParagraphSpacingSetCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.CustomParagraphSpacing, new CustomParagraphSpacingCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ThemeEffects, new ApplyEffectSetCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.Undo, new ActionRibbonCommand(() => { if (editor.CanUndo) editor.Undo(); }));
        registry.Bind(FreeWRibbonCommandAction.Redo, new ActionRibbonCommand(() => { if (editor.CanRedo) editor.Redo(); }));

        // Layout tab — page settings (applied to the model; honoured by docx save + print).
        PageSetting(FreeWRibbonCommandAction.Orientation, PageLayoutCommandPlanner.ToggleOrientation);
        PageSetting(FreeWRibbonCommandAction.Margins, PageLayoutCommandPlanner.ToggleNormalNarrowMargins);
        PageSetting(FreeWRibbonCommandAction.Size, PageLayoutCommandPlanner.ToggleLetterA4Paper);
        // Columns: open the Columns dialog or apply Word's backed preset menu choices directly, mutating
        // PageSettings and re-rendering so the live document flow changes immediately.
        registry.Bind(FreeWRibbonCommandAction.Columns, new ColumnsCommand(editor));
        PageSetting(FreeWRibbonCommandAction.ColumnsOne,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.One),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.One));
        PageSetting(FreeWRibbonCommandAction.ColumnsTwo,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Two),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Two));
        PageSetting(FreeWRibbonCommandAction.ColumnsThree,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Three),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Three));
        PageSetting(FreeWRibbonCommandAction.ColumnsLeft,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Left),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Left));
        PageSetting(FreeWRibbonCommandAction.ColumnsRight,
            page => PageLayoutCommandPlanner.ApplyColumnPreset(page, PageColumnPreset.Right),
            page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, PageColumnPreset.Right));
        registry.Bind(FreeWRibbonCommandAction.ColumnsMore, new ColumnsCommand(editor));
        // Page Setup: the unified Margins / Paper / Layout dialog (Word's Layout > Page Setup launcher). The
        // "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the Margins / Paper tab.
        registry.Bind(FreeWRibbonCommandAction.PageSetup, new PageSetupCommand(editor, PageSetupDialogTabKind.Margins));
        registry.Bind(FreeWRibbonCommandAction.CustomMargins, new PageSetupCommand(editor, PageSetupDialogTabKind.Margins));
        registry.Bind(FreeWRibbonCommandAction.MorePaperSizes, new PageSetupCommand(editor, PageSetupDialogTabKind.Paper));
        // Line Numbers: Word-style menu items set the backed mode explicitly, while the top-level command keeps
        // the existing cycle behavior for quick access (shown in print preview and the live page adorner).
        PageSetting(FreeWRibbonCommandAction.LineNumbers, PageLayoutCommandPlanner.CycleLineNumberMode);
        PageSetting(FreeWRibbonCommandAction.LineNumbersNone, page => page.LineNumberMode = LineNumberMode.None,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.None));
        PageSetting(FreeWRibbonCommandAction.LineNumbersContinuous, page => page.LineNumberMode = LineNumberMode.Continuous,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.Continuous));
        PageSetting(FreeWRibbonCommandAction.LineNumbersRestartPage, page => page.LineNumberMode = LineNumberMode.RestartEachPage,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.RestartEachPage));
        PageSetting(FreeWRibbonCommandAction.LineNumbersRestartSection, page => page.LineNumberMode = LineNumberMode.RestartEachSection,
            page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, LineNumberMode.RestartEachSection));
        // Line Numbering Options…: dedicated dialog (Start At / Count By / Restart mode), not Page Setup.
        registry.Bind(FreeWRibbonCommandAction.LineNumbersOptions, new LineNumberOptionsCommand(editor));

        // Page setup polish — all mutate PageSettings via ApplyPageSettings (commit + re-render) and
        // round-trip through docx save.
        //  - Hyphenation: a dropdown (None / Automatic / Manual / Options…). The split-button default action
        //    (freew.hyphenation) toggles automatic hyphenation; the menu items set an explicit mode, and the
        //    Options item opens the Hyphenation Options dialog. Automatic hyphenation inserts soft hyphens in
        //    the live document (settings.xml w:autoHyphenation + zone/limit/caps sub-options).
        //  - Page Vertical Alignment: cycle Top -> Center -> Justified (-> Bottom) (sectPr w:vAlign).
        //  - Different First Page: toggle a distinct first-page header/footer (sectPr w:titlePg).
        PageSetting(FreeWRibbonCommandAction.Hyphenation, PageLayoutCommandPlanner.ToggleHyphenation, page => page.AutoHyphenation);
        PageSetting(FreeWRibbonCommandAction.HyphenationNone, page => page.AutoHyphenation = false, page => !page.AutoHyphenation);
        PageSetting(FreeWRibbonCommandAction.HyphenationAuto, page => page.AutoHyphenation = true, page => page.AutoHyphenation);
        registry.Bind(FreeWRibbonCommandAction.HyphenationManual, new HyphenationManualCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.HyphenationOptions, new HyphenationOptionsCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.PageValign, new PageVerticalAlignmentCommand(editor));
        PageSetting(FreeWRibbonCommandAction.DifferentFirstPage,
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage);

        // Design tab — Page Background: "Page Borders" opens the full Borders and Shading dialog,
        // and Watermark sets/clears the page watermark. Both ultimately mutate PageSettings via
        // ApplyPageSettings (commit + re-render) and round-trip through docx save.
        registry.Register("freew.page-border", new BordersAndShadingCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.Watermark, new WatermarkCommand(editor));

        // Design tab — Page Background: pick the whole-page background colour (Word's Page Color). Opens a
        // swatch palette + No Color + More Colors... and sets the model's page BackgroundColorHex (which
        // already round-trips as w:background in docx); the editor recolours the page sheet immediately.
        registry.Bind(FreeWRibbonCommandAction.PageColor, new PageColorCommand(editor));

        var viewRibbon = ViewRibbonWorkflow.Register(
            registry,
            new ViewRibbonCommandBindings(
                PrintPreview: new ViewRibbonActionBinding(onPrintPreview),
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: new ViewRibbonToggleBinding(onToggleReadMode, isReadModeActive),
                    ColumnWidth: new ViewRibbonChoiceBinding(
                        onReadModeColumnWidth,
                        EmptyRibbonCommand.Instance),
                    PageColor: new ViewRibbonChoiceBinding(
                        onReadModePageColor,
                        EmptyRibbonCommand.Instance)),
                Modes: new ViewRibbonModeBindings(
                    PrintLayout: new ViewRibbonToggleBinding(onTogglePrintLayout, isPrintLayoutActive),
                    WebLayout: new ViewRibbonToggleBinding(onWebLayout, isWebLayoutActive),
                    Draft: new ViewRibbonToggleBinding(onDraftView, isDraftViewActive),
                    Outline: new ViewRibbonToggleBinding(onToggleOutlineView, isOutlineViewActive),
                    PagedEdit: new ViewRibbonToggleBinding(onTogglePagedEditView, isPagedEditViewActive)),
                Show: new ViewRibbonShowBindings(
                    NavigationPane: new ViewRibbonToggleBinding(onToggleNavPane, isNavPaneVisible),
                    RevealFormatting: new ViewRibbonToggleBinding(
                        onToggleRevealFormatting,
                        isRevealFormattingVisible),
                    Gridlines: new ViewRibbonToggleBinding(
                        () => editor.TogglePageGridlines(),
                        () => editor.ShowPageGridlines),
                    Ruler: new ViewRibbonToggleBinding(onToggleRuler, isRulerVisible)),
                Zoom: new ViewRibbonZoomBindings(
                    Dialog: new ViewRibbonActionBinding(onZoomDialog),
                    Reset100: new ViewRibbonActionBinding(onZoom100),
                    OnePage: new ViewRibbonActionBinding(onZoomOnePage),
                    PageWidth: new ViewRibbonActionBinding(onZoomPageWidth),
                    MultiplePages: new ViewRibbonToggleBinding(
                        onToggleMultiplePages,
                        isMultiplePagesActive),
                    SideToSide: new ViewRibbonToggleBinding(
                        onToggleSideToSide,
                        isSideToSideActive)),
                Window: new ViewRibbonWindowBindings(
                    NewWindow: new ViewRibbonActionBinding(onNewWindow, EmptyRibbonCommand.Instance),
                    ArrangeAll: new ViewRibbonActionBinding(onArrangeAll, EmptyRibbonCommand.Instance),
                    Split: new ViewRibbonToggleBinding(onToggleSplitWindow, isSplitWindowActive))));

        // Home > Paragraph — Show Formatting Marks: a stateful toggle over the editor's display-only pilcrow /
        // space-dot / tab-arrow overlay. The marks are drawn as a non-editable adorner computed from the
        // document's text geometry, so they never enter the model/text; executing flips the overlay and
        // (being in `stateful`) pushes the new state into the shared store so the ribbon button reflects it.
        var formattingMarks = registry.BindToggle(FreeWRibbonCommandAction.FormattingMarks,
            () => editor.ToggleFormattingMarks(),
            () => editor.ShowFormattingMarks);
        stateful.Add((
            FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.FormattingMarks),
            formattingMarks));

        if (viewRibbon.Gridlines is { } viewGridlines)
            stateful.Add(("freew.gridlines", viewGridlines));

        if (hostPorts is null)
        {
            FreeWRibbonHostExecutionProfile.RegisterSupportCommands(
                registry,
                FreeWRibbonHostExecutionPorts.Empty with
                {
                    OpenHelpOnline = onHelpOnline,
                    OpenFeedback = onFeedback,
                    CopyDiagnostics = onCopyDiagnostics,
                    CheckForUpdates = onCheckForUpdates,
                    OpenAbout = onAbout,
                    OpenLegalNotices = onLegalNotices,
                });
        }

        // Mailings tab — a simple mail merge. Field placeholders are the literal text «FieldName»
        // (ordinary run text, so they round-trip through docx as plain text). The four commands share a
        // MailMergeSession: Start Mail Merge selects the output mode; "Select Recipients" / "Edit
        // Recipient List" capture CSV/typed records; "Insert Merge Field" drops a «Name» placeholder at
        // the caret; "Preview Results" loads MergeRecord(template, row) into the editor, and the preview
        // navigation commands move through real recipient rows; "Finish & Merge" combines every merged
        // record according to the selected output mode.
        var mergeSession = new MailMergeSession();
        registry.Bind(FreeWRibbonCommandAction.StartMailMerge, new SetMergeModeCommand(editor, mergeSession, MailMergeOutputMode.Letters));
        registry.Bind(FreeWRibbonCommandAction.StartMailMergeLetters, new SetMergeModeCommand(editor, mergeSession, MailMergeOutputMode.Letters));
        registry.Bind(FreeWRibbonCommandAction.StartMailMergeDirectory, new SetMergeModeCommand(editor, mergeSession, MailMergeOutputMode.Directory));
        registry.Bind(FreeWRibbonCommandAction.StartMailMergeNormal, new ClearMergeSessionCommand(editor, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeData, new SetMergeDataCommand(editor, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeEditRecipients, new SetMergeDataCommand(editor, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeField, new InsertMergeFieldCommand(resolveFieldTarget));
        // Write & Insert Fields — Address Block, Greeting Line, Match Fields (Word parity).
        registry.Bind(FreeWRibbonCommandAction.MergeAddressBlock, new InsertAddressBlockCommand(resolveFieldTarget, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeGreetingLine, new InsertGreetingLineCommand(resolveFieldTarget, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeMatchFields, new MatchFieldsCommand(editor, mergeSession));
        // Special merge fields use Word's native NEXT/MERGEREC/MERGESEQ instructions. Their cached
        // result remains the familiar guillemet label until a merge evaluates the field.
        registry.Bind(FreeWRibbonCommandAction.MergeNextRecord, new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.NextRecordField));
        registry.Bind(FreeWRibbonCommandAction.MergeRecordNumber, new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.MergeRecordNumberField));
        registry.Bind(FreeWRibbonCommandAction.MergeSequenceNumber, new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.MergeSequenceNumberField));
        // Rules dropdown — each sub-command inserts the appropriate rule instruction via a dialog.
        registry.Bind(FreeWRibbonCommandAction.MergeRules, EmptyRibbonCommand.Instance); // dropdown host: no action of its own
        registry.Bind(FreeWRibbonCommandAction.MergeRuleIf, new InsertMergeRuleIfCommand(resolveFieldTarget, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleSkipRecordIf, new InsertMergeRuleCondCommand(resolveFieldTarget, mergeSession, RuleCondKind.SkipRecordIf));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleNextRecordIf, new InsertMergeRuleCondCommand(resolveFieldTarget, mergeSession, RuleCondKind.NextRecordIf));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleFillIn, new InsertMergeRuleFillInCommand(resolveFieldTarget));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleAsk, new InsertMergeRuleAskCommand(resolveFieldTarget));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleSet, new InsertMergeRuleSetCommand(resolveFieldTarget));
        registry.Bind(FreeWRibbonCommandAction.MergeRuleRef, new InsertMergeRuleRefCommand(resolveFieldTarget));
        registry.Bind(FreeWRibbonCommandAction.MergePreview, new PreviewMergeRecordCommand(editor, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergePreviewFirst, new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.First));
        registry.Bind(FreeWRibbonCommandAction.MergePreviewPrevious, new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Previous));
        registry.Bind(FreeWRibbonCommandAction.MergePreviewNext, new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Next));
        registry.Bind(FreeWRibbonCommandAction.MergePreviewLast, new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Last));
        registry.Bind(FreeWRibbonCommandAction.MergeFindRecipient, new FindMergeRecipientCommand(editor, mergeSession));
        registry.Bind(FreeWRibbonCommandAction.MergeCheckErrors, new CheckMergeErrorsCommand(
            editor,
            mergeSession,
            openReportDocument: onOpenMailMergeErrorReport));
        var emailMergeCommand = new EmailMergeCommand(editor, mergeSession);
        registry.Bind(FreeWRibbonCommandAction.MergeFinish, new FinishMergeCommand(
            editor,
            mergeSession,
            printDocument: onPrintMailMergeDocument,
            emailDocuments: indexes => emailMergeCommand.Execute(indexes)));
        registry.Bind(FreeWRibbonCommandAction.MergeEmail, emailMergeCommand);
        // Filter & Sort: refines the active session's MergeData (include/exclude rows, sort column/direction)
        // without touching the merge template. No-ops gracefully when there is no active session or data.
        registry.Bind(FreeWRibbonCommandAction.MergeFilterSort, new FilterSortRecipientsCommand(editor, mergeSession));
        // Envelopes / Labels: set up the page geometry (and optionally a table grid for labels) via the
        // backed ApplyPageSettings / InsertTable paths. No SMTP or print path — page-setup only.
        registry.Bind(FreeWRibbonCommandAction.MergeEnvelopes, new EnvelopesCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.MergeLabels, new LabelsCommand(editor, mergeSession));

        FreeWRibbonEditorExecutionProfile.RegisterFamilies(
            registry,
            tableCommands.Build(),
            referenceCommands.Build(),
            headerFooterCommands.Build());
        FreeWRibbonEditorExecutionProfile.RegisterFloating(
            registry,
            CreateFloatingExecutionPorts(editor));
        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            registry,
            CreateChartSmartArtExecutionPorts(editor));

        RefreshStatefulCommands();
        return FreeWRibbonExecutionProfile.Build(registry).Registry;
    }

    private static FreeWRibbonFloatingExecutionPorts CreateFloatingExecutionPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            HasSelection: static _ => true,
            ApplyWrap: (target, wrapping) =>
            {
                var selected = target == ObjectFormatTarget.Picture
                    ? editor.SelectedImage() is not null
                    : editor.SelectedShape() is not null;
                if (!selected)
                {
                    DialogMessageHelper.ShowInfo(
                        Window.GetWindow(editor),
                        target == ObjectFormatTarget.Picture ? "Select a picture first." : "Select a shape first.",
                        "Wrap Text");
                    return;
                }

                if (target == ObjectFormatTarget.Picture)
                    editor.SetSelectedImageWrapping(wrapping);
                else
                    editor.SetSelectedShapeWrapping(wrapping);
            },
            ApplyTransform: (_, command) =>
            {
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
                return applied;
            },
            ApplyZOrder: (target, operation) =>
            {
                var selected = target == ObjectFormatTarget.Picture
                    ? editor.SelectedImage() is not null
                    : editor.SelectedShape() is not null;
                var applied = selected && editor.ChangeSelectedFloatingZOrder(operation);
                if (!applied)
                {
                    DialogMessageHelper.ShowInfo(
                        Window.GetWindow(editor),
                        target == ObjectFormatTarget.Picture
                            ? "Select a floating picture first."
                            : "Select a floating shape first.",
                        "Z-Order");
                }
                return applied;
            },
            ApplySize: (target, dimension, points) =>
            {
                if (target == ObjectFormatTarget.Picture && editor.SelectedImage() is { } image)
                {
                    editor.SetSelectedImageSize(
                        dimension == ObjectFormatSizeDimension.Width ? points : image.WidthPt,
                        dimension == ObjectFormatSizeDimension.Height ? points : image.HeightPt);
                }
                else if (target == ObjectFormatTarget.Shape && editor.SelectedShape() is { } shape)
                {
                    editor.SetSelectedShapeSize(
                        dimension == ObjectFormatSizeDimension.Width ? points : shape.WidthPt,
                        dimension == ObjectFormatSizeDimension.Height ? points : shape.HeightPt);
                }
            },
            ApplyParagraphAlignment: (target, alignment) =>
            {
                if (target == ObjectFormatTarget.Picture)
                {
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
                else
                    editor.SetSelectedShapeAlignment(alignment);
            },
            CanArrange: static _ => true,
            Arrange: kind =>
            {
                if (!editor.ArrangeFloatingObjects(kind)
                    && kind is FloatingObjectArrangeKind.DistributeHorizontal
                        or FloatingObjectArrangeKind.DistributeVertical)
                {
                    DialogMessageHelper.ShowInfo(
                        Window.GetWindow(editor),
                        "Select at least two floating objects to distribute.",
                        kind == FloatingObjectArrangeKind.DistributeVertical
                            ? "Distribute Vertically"
                            : "Distribute Horizontally");
                }
            },
            SelectedShape: editor.SelectedShape,
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
            Ungroup: editor.UngroupSelectedFloatingObject,
            NativeCanonicalCommands: CreateNativeFloatingCommands(editor));

    private static IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> CreateNativeFloatingCommands(
        DocumentView editor) =>
        new Dictionary<FreeWRibbonCommandAction, IRibbonCommand>
        {
            [FreeWRibbonCommandAction.ShapeEditShape] = new ActionRibbonCommand(() =>
            {
                editor.Focus();
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Choose 'Convert to Freeform' or 'Edit Points' from the menu.",
                    "Edit Shape");
            }),
            [FreeWRibbonCommandAction.ShapeTextDirection] = new ActionRibbonCommand(() =>
            {
                editor.Focus();
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Choose a text direction from the dropdown.",
                    "Text Direction");
            }),
            [FreeWRibbonCommandAction.ShapeEffects] = new ActionRibbonCommand(() =>
            {
                editor.Focus();
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Choose an effect from the dropdown.",
                    "Shape Effects");
            }),
            [FreeWRibbonCommandAction.ShapeStylesGallery] = new ActionRibbonCommand(() =>
            {
                editor.Focus();
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    "Choose a shape style from the gallery.",
                    "Shape Styles");
            }),
            [FreeWRibbonCommandAction.ObjectGroup] = new ObjectGroupCommand(editor),
            [FreeWRibbonCommandAction.ObjectUngroup] = new ObjectUngroupCommand(editor),
        };

    private static FreeWRibbonChartSmartArtExecutionPorts CreateChartSmartArtExecutionPorts(
        DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            SelectedChart: editor.SelectedChart,
            SetChartKind: editor.SetSelectedChartKind,
            ApplyChartStyle: editor.ApplySelectedChartStyle,
            ApplyChartColorScheme: editor.ApplySelectedChartColorScheme,
            ApplyChartQuickLayout: editor.ApplySelectedChartQuickLayout,
            ToggleChartLegend: editor.ToggleSelectedChartLegend,
            ChartTitleCommand: CreateChartTitleCommand(editor),
            ChartAxisTitlesCommand: CreateChartAxisTitlesCommand(editor),
            ChartEditDataCommand: CreateChartEditDataCommand(editor),
            ChartSizeCommand: CreateChartSizeCommand(editor),
            SelectedSmartArt: editor.SelectedSmartArt,
            MutateSmartArt: operation =>
            {
                switch (operation)
                {
                    case SmartArtStructureOperation.AddShape: editor.SmartArtAddShape(); break;
                    case SmartArtStructureOperation.RemoveShape: editor.SmartArtRemoveShape(); break;
                    case SmartArtStructureOperation.Promote: editor.SmartArtPromote(); break;
                    case SmartArtStructureOperation.Demote: editor.SmartArtDemote(); break;
                    case SmartArtStructureOperation.MoveUp: editor.SmartArtMoveUp(); break;
                    case SmartArtStructureOperation.MoveDown: editor.SmartArtMoveDown(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
            },
            ApplySmartArtLayout: editor.ApplySmartArtLayout,
            ApplySmartArtColorScheme: editor.ApplySmartArtColorScheme,
            ApplySmartArtStyle: editor.ApplySmartArtStyle,
            SmartArtEditTextCommand: new SmartArtEditTextRibbonCommand(editor),
            ChartColorCommandPrefix: "freew.chart-color");

    private static IRibbonCommand CreateChartTitleCommand(DocumentView editor) =>
        new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null)
                return;
            var (accepted, newTitle) = ChartTitleDialog.Prompt(
                Application.Current?.MainWindow,
                chart.Title);
            if (accepted)
                editor.SetSelectedChartTitle(newTitle);
        });

    private static IRibbonCommand CreateChartAxisTitlesCommand(DocumentView editor) =>
        new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null)
                return;
            var result = ChartAxisTitlesDialog.Prompt(
                Application.Current?.MainWindow,
                chart.CategoryAxisTitle,
                chart.ValueAxisTitle);
            if (result is not null)
                editor.SetSelectedChartAxisTitles(result.Value.CategoryTitle, result.Value.ValueTitle);
        });

    private static IRibbonCommand CreateChartEditDataCommand(DocumentView editor) =>
        new ActionRibbonCommand(() =>
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null)
                return;
            var replacement = InsertChartDialog.Prompt(Application.Current?.MainWindow, chart);
            if (replacement is not null)
                editor.ReplaceSelectedChartData(replacement);
        });

    private static IRibbonCommand CreateChartSizeCommand(DocumentView editor) =>
        new ChartSizeCommand(editor);

    private sealed class ChartSizeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var chart = editor.SelectedChart();
            if (chart is null)
                return;

            if (FreeWRibbonNumericValueParser.TryParseChartSize(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    out var size))
            {
                editor.SetSelectedChartSize(size.WidthPt, size.HeightPt);
                return;
            }

            var result = ChartSizeDialog.Prompt(
                Application.Current?.MainWindow,
                chart.WidthPt,
                chart.HeightPt);
            if (result is not null)
                editor.SetSelectedChartSize(result.Value.WidthPt, result.Value.HeightPt);
        }
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

    private static string? ComboValue(RibbonCommandContext context)
    {
        if (context.SelectedValue is { Length: > 0 } selectedValue)
            return selectedValue;

        return context.Parameters.TryGetValue("value", out var legacyRaw)
            ? legacyRaw as string
            : null;
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
            var option = PasteSpecialDialog.Prompt(
                Window.GetWindow(editor),
                editor.PlatformClipboard);
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
            var commit = MultilevelListDialogPlanner.PlanCommit(
                MultilevelListDialog.Prompt(
                    Window.GetWindow(editor),
                    editor.Model.MultiLevelList.NumberFormats));
            if (!commit.ShouldApply)
                return;
            editor.Focus();
            editor.ApplyMultiLevelListDefinition(commit.Definition!);
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

    private sealed class ApplyNamedStyleCommand(DocumentView editor, string styleId) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ApplyNamedStyle(styleId);
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
            editor.ApplyNamedStyle(styleId);
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
            var catalog = StyleDialogPlanner.BuildStyleNamesById(editor.Model);
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
                        editor.ApplyNamedStyle(apply.StyleId);
                        return;

                    case ManageStyleAction.Delete del:
                        editor.DeleteParagraphStyle(del.StyleId);
                        continue; // reopen the list so the user sees the removal

                    case ManageStyleAction.Modify mod:
                        if (!editor.Model.Styles.TryGetValue(mod.StyleId, out var existing))
                            continue;
                        var def = StyleDialog.AskModify(
                            owner,
                            StyleDialogPlanner.BuildStyleNamesById(editor.Model),
                            existing);
                        if (def is null)
                            continue;
                        editor.ModifyParagraphStyle(mod.StyleId, def.Run, def.Paragraph, def.BasedOnId, def.NextStyleId);
                        continue;
                }
            }
        }
    }

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
            foreach (var hex in FreeWRibbonPaletteCatalog.TextAndHighlightPickerSwatches)
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
            foreach (var swatchHex in FreeWRibbonPaletteCatalog.ParagraphShadingPickerSwatches)
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
            var commit = CellShadingDialogPlanner.PlanCommit(result);
            if (!commit.ShouldApply)
                return;
            editor.SetCaretCellShading(commit.Hex);
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
    private sealed class PageSetupCommand(DocumentView editor, PageSetupDialogTabKind initialTab) : IRibbonCommand
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

    // Cycles every Word page vertical alignment value (sectPr w:vAlign). Routes through
    // ApplyPageSettings so the editor commits pending edits, mutates PageSettings, and re-renders.
    private sealed class PageVerticalAlignmentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page =>
                page.VerticalAlignment = PageVerticalAlignmentPlanner.Next(page.VerticalAlignment));
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

    private sealed class SplitCellRibbonCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var dimensions = DrawTableDimensionPicker.Ask(
                Window.GetWindow(editor),
                title: "Split Cells",
                defaultRows: 1,
                defaultColumns: 2);
            if (dimensions is not { } value)
                return;
            editor.Focus();
            editor.SplitCell(value.Rows, value.Cols);
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
        public static (int Rows, int Cols)? Ask(
            Window? owner,
            string title = "Draw Table",
            int defaultRows = DrawTableCommandPlanner.DefaultRows,
            int defaultColumns = DrawTableCommandPlanner.DefaultColumns)
        {
            (int Rows, int Cols)? result = null;

            var rowsBox = new System.Windows.Controls.TextBox { Text = defaultRows.ToString(), MinWidth = 60, Margin = new Thickness(0, 0, 0, 8) };
            var colsBox = new System.Windows.Controls.TextBox { Text = defaultColumns.ToString(), MinWidth = 60, Margin = new Thickness(0, 0, 0, 8) };
            var ok     = new System.Windows.Controls.Button { Content = "OK",     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true,  MinWidth = 72 };

            var dialog = new Window
            {
                Title = title,
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

    // Insert > Text > Text from File: realize the portable import policy through WPF-native ports.
    private sealed class InsertFileCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var workflow = new FreeWDocumentFragmentImportWorkflow(
                [new DocxFileAdapter()],
                new WpfDocumentFragmentPickerPort(owner),
                new WpfDocumentFragmentSourceReaderPort(),
                new WpfDocumentFragmentInsertionPort(editor));
            var request = FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest(
                FreeWDocumentFragmentHostProfile.Wpf);
            var result = workflow.ImportAsync(request).GetAwaiter().GetResult();
            ShowDocumentFragmentImportOutcome(owner, result);
        }
    }

    // Insert > Text > Object: realize portable OLE package policy through WPF-native ports.
    private sealed class InsertEmbeddedObjectCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var workflow = new FreeWDocumentFragmentImportWorkflow(
                [],
                new WpfDocumentFragmentPickerPort(owner),
                new WpfDocumentFragmentSourceReaderPort(),
                new WpfDocumentFragmentInsertionPort(editor));
            var request = FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest(
                FreeWDocumentFragmentHostProfile.Wpf);
            var result = workflow.ImportAsync(request).GetAwaiter().GetResult();
            ShowDocumentFragmentImportOutcome(owner, result);
        }
    }

    private static void ShowDocumentFragmentImportOutcome(
        Window? owner,
        FreeWDocumentFragmentImportResult result)
    {
        var presentation = FreeWDocumentFragmentImportOutcomePlanner.Plan(
            result,
            FreeWFileTextResources.Document,
            FreeWDocumentFragmentImportFailureSurface.WpfModalError);
        if (presentation.ModalMessage is { } message)
            DialogMessageHelper.ShowError(owner, message, presentation.ModalTitle ?? "FreeW");
    }

    // Insert > Illustrations > Picture: realize the portable import workflow through WPF-native ports.
    private sealed class InsertPictureCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var workflow = new FreeWPictureImportWorkflow(
                new WpfPictureImportPickerPort(owner),
                new WpfPictureImportSourceReaderPort(),
                new WpfPictureDecoderPort(),
                new WpfPictureRasterizerPort(),
                new WpfPictureInsertionPort(editor));
            var result = workflow.ImportAsync().GetAwaiter().GetResult();
            var presentation = FreeWPictureImportOutcomePlanner.Plan(
                result,
                FreeWFileTextResources.Document,
                FreeWPictureImportFailureSurface.ModalError);
            if (presentation.ModalMessage is { } message)
            {
                DialogMessageHelper.ShowError(
                    owner,
                    message,
                    presentation.ModalTitle ?? "FreeW");
            }
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

            if (FreeWRibbonNumericValueParser.TryParseObjectSize(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    out var parsedSize))
            {
                editor.SetSelectedImageSize(parsedSize.WidthPt, parsedSize.HeightPt);
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

            if (FreeWRibbonNumericValueParser.TryParseObjectPosition(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    out var parsedPosition))
            {
                editor.SetSelectedImagePosition(
                    parsedPosition.HorizontalOffsetPt,
                    parsedPosition.VerticalOffsetPt,
                    parsedPosition.HorizontalAnchor,
                    parsedPosition.VerticalAnchor);
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
    private sealed class InsertDateTimeCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
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
    private sealed class InsertDocPropFieldCommand(
        Func<DocumentView> resolveEditor,
        RunFieldKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
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
            var commit = FootnoteEndnoteOptionsDialogPlanner.PlanCommit(
                FootnoteEndnoteOptionsDialog.Prompt(
                    owner,
                    model.FootnoteNumbering,
                    model.EndnoteNumbering));
            if (!commit.ShouldApply)
                return;
            editor.ApplyFootnoteEndnoteOptions(commit.Result!);
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
            editor.InsertComment(
                text.Trim(),
                author,
                CommentInitialsPolicy.Derive(author, CommentInitialsPolicy.FirstThreeWords));
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
            if (!editor.ReplyToCommentAtCaret(
                    text.Trim(),
                    author,
                    CommentInitialsPolicy.Derive(author, CommentInitialsPolicy.FirstThreeWords)))
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

    // WPF retains only native speech construction, editor mutation notification, and command-state
    // realization. ReadAloudSession owns the cross-renderer lifecycle and sequencing policy.
    private sealed class WpfReadAloudCommandAdapter : IRibbonStatefulCommand, IDisposable
    {
        private readonly DocumentView _editor;
        private readonly ReadAloudSession _session;
        private readonly FreeWReadAloudRibbonCommand _command;
        private bool _disposed;

        public event Action? StateChanged;

        public WpfReadAloudCommandAdapter(DocumentView editor)
        {
            _editor = editor;
            _session = new ReadAloudSession(new ReadAloudSessionPorts(
                GetDocument: () => _editor.Model,
                GetStartSegmentIndex: _editor.ReadAloudStartSegmentIndex,
                CreateEngine: _ => new SystemSpeechEngine(_editor.Dispatcher)));
            _command = new FreeWReadAloudRibbonCommand(_session);
            _command.StateChanged += OnCommandStateChanged;
            _editor.TextChanged += OnEditorTextChanged;
        }

        public void Execute(RibbonCommandContext context) => _command.Execute(context);

        public RibbonCommandState GetState() => _command.GetState();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _editor.TextChanged -= OnEditorTextChanged;
            _command.StateChanged -= OnCommandStateChanged;
            _command.Dispose();
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs args) =>
            _session.HandleDocumentChanged();

        private void OnCommandStateChanged() => StateChanged?.Invoke();
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

            editor.ApplyInspectorRemovals(choice);
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

    // Insert > References > Mark Entry: preserve Word's main/subentry and page/cross-reference choices in
    // the hidden XE field. The selected text seeds the main entry.
    private sealed class MarkIndexEntryCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var result = MarkIndexEntryDialog.Prompt(
                Window.GetWindow(editor),
                MarkIndexEntryDialogPlanner.BuildInitialState(seed),
                editor.BookmarkNames());
            if (result is null)
                return; // cancelled or empty — nothing to mark
            if (result.MarkAll)
                editor.MarkAllIndexEntries(seed, result.Mark);
            else
                editor.MarkIndexEntry(result.Mark);
        }
    }

    // References > Index > Insert Index: choose the optional XE identifier whose entries should be
    // included. A blank identifier preserves Word's default index behavior.
    private sealed class InsertIndexCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = InsertIndexDialog.Prompt(
                Window.GetWindow(editor),
                InsertIndexDialogPlanner.BuildInitialState());
            if (result is null)
                return;

            editor.InsertIndex(result.Identifier);
        }
    }

    // References > Index > Update Index: rebuild only the index selected by its optional XE identifier.
    private sealed class UpdateIndexCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();

            // Registry contracts use a detached editor; preserve their non-modal default-index exercise.
            if (!editor.IsLoaded)
            {
                editor.RefreshIndex();
                return;
            }

            var result = InsertIndexDialog.PromptForUpdate(
                Window.GetWindow(editor),
                InsertIndexDialogPlanner.BuildInitialState());
            if (result is null)
                return;

            editor.RefreshIndex(result.Identifier);
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
            var commit = TableOfAuthoritiesDialogPlanner.PlanCommit(
                TableOfAuthoritiesDialog.Prompt(owner));
            if (commit.ShouldInsert)
                editor.InsertTableOfAuthorities(commit.Options!);
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
            var session = new SourceManagementAuthorEditorSession(entry);
            var initial = session.CurrentPlan;
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

            IReadOnlyList<SourceManagementAuthorPersonRow> ReadPersonRows() =>
                rowControls.Select(row => new SourceManagementAuthorPersonRow(
                    row.First.Text ?? string.Empty,
                    row.Middle.Text ?? string.Empty,
                    row.Last.Text ?? string.Empty)).ToArray();

            void RenderPersonRows(IReadOnlyList<SourceManagementAuthorPersonRow> rows)
            {
                rowsPanel.Children.Clear();
                rowControls.Clear();
                foreach (var row in rows)
                    AddPersonRow(row);
            }

            void ApplyMode(SourceManagementAuthorEditorPlan plan)
            {
                peoplePanel.IsEnabled = plan.PersonalAuthorFieldsEnabled;
                corporateLabel.IsEnabled = plan.CorporateAuthorFieldEnabled;
                corporateBox.IsEnabled = plan.CorporateAuthorFieldEnabled;
            }

            RenderPersonRows(initial.PersonalRows);

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
            addRow.Click += (_, _) => RenderPersonRows(session.AddPersonalAuthorRow(
                ReadPersonRows(),
                corporateBox.Text).PersonalRows);
            var removeRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.RemoveAuthorRowButtonLabel,
                MinWidth = 72,
                Margin = new Thickness(0, 4, 0, 0)
            };
            removeRow.Click += (_, _) => RenderPersonRows(session.RemoveFinalPersonalAuthorRow(
                ReadPersonRows(),
                corporateBox.Text).PersonalRows);
            peoplePanel.Children.Add(new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children = { addRow, removeRow }
            });

            personalMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Personal,
                ReadPersonRows(),
                corporateBox.Text));
            corporateMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Corporate,
                ReadPersonRows(),
                corporateBox.Text));

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = session.Accept(ReadPersonRows(), corporateBox.Text);
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

            ApplyMode(initial);
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

    private static TextDocument CurrentMailMergeDocument(
        DocumentView editor,
        MailMergeSession session)
    {
        if (!session.IsPreviewing)
            editor.CommitToModel();
        return session.Template ?? editor.Model;
    }

    private static void Realize(
        DocumentView editor,
        MailMergeSessionTransition transition)
    {
        if (transition.DocumentToLoad is { } document)
            editor.LoadModel(document);
    }

    private static bool Realize(
        DocumentView editor,
        MailMergePreviewExecution execution,
        Action<Window?, string>? showInfo = null)
    {
        if (execution.DocumentToLoad is { } document)
            editor.LoadModel(document);
        if (!execution.Success)
        {
            (showInfo ?? ((owner, message) =>
                DialogMessageHelper.ShowInfo(owner, message, "Mail Merge")))(
                Window.GetWindow(editor),
                execution.Message);
        }

        return execution.Success;
    }

    private sealed class SetMergeModeCommand(
        DocumentView editor,
        MailMergeSession session,
        MailMergeOutputMode mode) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            Realize(editor, new MailMergeSessionWorkflow(session).SetMode(mode));
        }
    }

    private sealed class ClearMergeSessionCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            Realize(editor, new MailMergeSessionWorkflow(session).Clear());
        }
    }

    // Mailings > Insert Merge Field: prompt for a field name and insert the shared native field plan
    // through the editor's normal undo path. The cached result keeps Word's familiar label.
    private sealed class InsertMergeFieldCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            editor.Focus();
            var name = TextPrompt.Ask(Window.GetWindow(editor), "Insert Merge Field", "Field name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return; // cancelled or blank — nothing to insert

            if (MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(name) is not { } plan)
                return;

            RealizeMailMergeFieldPlan(editor, plan);
        }
    }

    // Mailings > Insert Address Block: insert a native ADDRESSBLOCK field at the caret.
    // The placeholder is resolved at preview/merge time via the session's FieldMapping (auto-matched or
    // user-customised via Match Fields). Opens Match Fields first if no data is loaded so the user can
    // configure the mapping before the placeholder lands in the document.
    internal sealed class InsertAddressBlockCommand(
        Func<DocumentView> resolveEditor,
        MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var validation = new MailMergeSessionWorkflow(session)
                .Validate(MailMergeOperation.InsertAddressBlock);
            if (!validation.IsValid)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    validation.Message,
                    "Mail Merge");
                return;
            }

            RealizeMailMergeFieldPlan(
                editor,
                MailMergeFieldAuthoringPlanner.CreateAddressBlockPlan());
        }
    }

    // Mailings > Insert Greeting Line: insert a native default GREETINGLINE field at the caret.
    // Resolved per-record at preview/merge time using the session's FieldMapping.
    internal sealed class InsertGreetingLineCommand(
        Func<DocumentView> resolveEditor,
        MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var validation = new MailMergeSessionWorkflow(session)
                .Validate(MailMergeOperation.InsertGreetingLine);
            if (!validation.IsValid)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    validation.Message,
                    "Mail Merge");
                return;
            }

            RealizeMailMergeFieldPlan(
                editor,
                MailMergeFieldAuthoringPlanner.CreateGreetingLinePlan());
        }
    }

    // Mailings > Match Fields: let the user override the auto-matched role→column bindings. Opens the
    // MatchFieldsDialog seeded with the current (auto-matched) mapping. Saves changes back to the
    // session so subsequent Address Block / Greeting Line insertions and preview/merge use the new bindings.
    private sealed class MatchFieldsCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.MatchFields);
            if (!validation.IsValid)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    validation.Message,
                    "Mail Merge");
                return;
            }

            var data = session.Data!;
            var current = session.Mapping ?? MailMerge.AutoMatchFields(data.Header);
            var result = MatchFieldsDialog.Ask(Window.GetWindow(editor), data.Header, current);
            if (result is not null)
                Realize(editor, workflow.ApplyFieldMapping(result));

            editor.Focus();
        }
    }

    // Mailings > Rules (special fields): insert a native Word field while retaining the familiar label.
    private sealed class InsertSpecialMergeFieldCommand(
        Func<DocumentView> resolveEditor,
        string fieldName) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            editor.Focus();
            if (MailMergeFieldAuthoringPlanner.CreateSpecialFieldPlan(fieldName) is { } plan)
            {
                RealizeMailMergeFieldPlan(editor, plan);
                return;
            }

            editor.InsertText($"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}");
        }
    }

    // Merge Rules: command kind tag for Skip/Next Record If.
    private enum RuleCondKind { SkipRecordIf, NextRecordIf }

    // Mailings > Rules > If...Then...Else: insert a native IF field with a nested MERGEFIELD operand.
    private sealed class InsertMergeRuleIfCommand(
        Func<DocumentView> resolveEditor,
        MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var header = session.Data?.Header ?? [];
            var result = MergeRuleIfDialog.Ask(Window.GetWindow(editor), header);
            if (result is null) return;
            RealizeMailMergeFieldPlan(
                editor,
                MailMergeRuleAuthoringPlanner.CreateIfPlan(result));
        }
    }

    // Mailings > Rules > Skip/Next Record If: insert native SKIPIF/NEXTIF with nested MERGEFIELD.
    private sealed class InsertMergeRuleCondCommand(
        Func<DocumentView> resolveEditor,
        MailMergeSession session,
        RuleCondKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var header = session.Data?.Header ?? [];
            var label = kind == RuleCondKind.SkipRecordIf ? "Skip Record If" : "Next Record If";
            var result = MergeRuleCondDialog.Ask(Window.GetWindow(editor), header, label);
            if (result is null) return;
            RealizeMailMergeFieldPlan(
                editor,
                MailMergeRuleAuthoringPlanner.CreateConditionPlan(
                    result,
                    skipRecord: kind == RuleCondKind.SkipRecordIf));
        }
    }

    // Renderer-owned realization for every shared mail-merge field authoring plan.
    internal static void RealizeMailMergeFieldPlan(
        DocumentView editor,
        MailMergeFieldInsertionPlan plan)
    {
        editor.Focus();
        editor.InsertComplexField(plan.Field, plan.CachedLabel);
    }

    // Mailings > Rules > Fill-in: insert a native FILLIN field with the familiar label as cached text.
    // At merge time MergeRuleEvaluator looks up the answer in MergeState.FillInAnswers (pre-populated
    // by FinishMergeCommand which shows the Fill-in dialogs before iterating records).
    private sealed class InsertMergeRuleFillInCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var prompt = MergeRulePromptDialog.AskPrompt(Window.GetWindow(editor), "Fill-in", "Enter the prompt text for this Fill-in field:");
            if (prompt is null) return;
            RealizeMailMergeFieldPlan(
                editor,
                MailMergeRuleAuthoringPlanner.CreateFillInPlan(prompt));
        }
    }

    // Mailings > Rules > Ask: insert a native ASK field with the familiar label as cached text.
    private sealed class InsertMergeRuleAskCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var result = MergeRuleAskSetDialog.AskAsk(Window.GetWindow(editor));
            if (result is null) return;
            if (MailMergeRuleAuthoringPlanner.CreateAskPlan(
                    result.Value.Name,
                    result.Value.Value) is { } plan)
            {
                RealizeMailMergeFieldPlan(editor, plan);
            }
        }
    }

    // Mailings > Rules > Set Bookmark: insert a native SET field with the familiar label as cached text.
    private sealed class InsertMergeRuleSetCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var result = MergeRuleAskSetDialog.AskSet(Window.GetWindow(editor));
            if (result is null) return;
            if (MailMergeRuleAuthoringPlanner.CreateSetPlan(
                    result.Value.Name,
                    result.Value.Value) is { } plan)
            {
                RealizeMailMergeFieldPlan(editor, plan);
            }
        }
    }

    // Mailings > Rules > Ref Bookmark: insert a native REF field with the familiar label as cached text.
    private sealed class InsertMergeRuleRefCommand(Func<DocumentView> resolveEditor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var name = MergeRulePromptDialog.AskPrompt(Window.GetWindow(editor), "Ref Bookmark",
                "Enter the bookmark name to reference:");
            if (name is null) return;
            if (MailMergeRuleAuthoringPlanner.CreateRefPlan(name) is { } plan)
                RealizeMailMergeFieldPlan(editor, plan);
        }
    }

    // ── Merge Rule dialogs ───────────────────────────────────────────────────────────────────────

    // If…Then…Else dialog: builds the complete rule definition.
    private static class MergeRuleIfDialog
    {
        public static MailMergeRuleIfDialogResult? Ask(Window? owner, IReadOnlyList<string> header)
        {
            var session = new MailMergeRuleConditionDialogSession(header);
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
            foreach (var h in session.FieldNames) fieldCombo.Items.Add(h);
            if (fieldCombo.Items.Count > 0) fieldCombo.SelectedIndex = 0;

            var opCombo = new System.Windows.Controls.ComboBox { MinWidth = 200 };
            foreach (var choice in session.ConditionOperators) opCombo.Items.Add(choice.Label);
            opCombo.SelectedIndex = 0;

            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 140 };
            var trueBox  = new System.Windows.Controls.TextBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 6) };
            var falseBox = new System.Windows.Controls.TextBox { MinWidth = 260 };

            // Disable value field for blank/not blank operators.
            opCombo.SelectionChanged += (_, _) =>
            {
                session.SelectOperator(opCombo.SelectedIndex);
                valueBox.IsEnabled = session.IsComparisonValueEnabled;
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = session.AcceptIf(
                    fieldCombo.SelectedItem?.ToString() ?? fieldCombo.Text,
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
            var session = new MailMergeRuleConditionDialogSession(header);
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
            foreach (var h in session.FieldNames) fieldCombo.Items.Add(h);
            if (fieldCombo.Items.Count > 0) fieldCombo.SelectedIndex = 0;

            var opCombo = new System.Windows.Controls.ComboBox { MinWidth = 200 };
            foreach (var choice in session.ConditionOperators) opCombo.Items.Add(choice.Label);
            opCombo.SelectedIndex = 0;

            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 140 };
            opCombo.SelectionChanged += (_, _) =>
            {
                session.SelectOperator(opCombo.SelectedIndex);
                valueBox.IsEnabled = session.IsComparisonValueEnabled;
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = session.AcceptCondition(
                    fieldCombo.SelectedItem?.ToString() ?? fieldCombo.Text,
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
        public static string? AskPrompt(Window? owner, string title, string label, string initialValue = "")
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

            var box = new System.Windows.Controls.TextBox
            {
                Text = initialValue,
                MinWidth = 260,
                Margin = new Thickness(0, 0, 0, 12)
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
        public static MailMergeRuleNameValueDialogResult? AskAsk(Window? owner) =>
            AskTwo(owner, "Ask", "Bookmark name:", "Prompt text:");

        public static MailMergeRuleNameValueDialogResult? AskSet(Window? owner) =>
            AskTwo(owner, "Set Bookmark", "Bookmark name:", "Value:");

        private static MailMergeRuleNameValueDialogResult? AskTwo(
            Window? owner,
            string title,
            string label1,
            string label2)
        {
            var session = new MailMergeRuleNameValueDialogSession();
            MailMergeRuleNameValueDialogResult? result = null;
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
            ok.Click += (_, _) =>
            {
                result = session.Accept(nameBox.Text, valueBox.Text);
                dialog.DialogResult = true;
            };

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
            var template = CurrentMailMergeDocument(editor, session);
            var fields = MailMerge.FieldNames(template);
            var dialogPlan = MailMergeRecipientDialogPlanner.CreatePlan(fields, session.Data);

            var csv = MergeDataDialog.Ask(
                Window.GetWindow(editor),
                fields,
                dialogPlan.InitialCsv);
            if (csv is null)
                return; // cancelled

            var transition = new MailMergeSessionWorkflow(session)
                .LoadRecipients(MergeData.FromCsv(csv));
            Realize(editor, transition);

            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                transition.Message,
                "Mail Merge");
            editor.Focus();
        }
    }

    // Mailings > Preview Results: load MergeRecord(template, currentRow) into the editor so the user sees
    // a real record. The original (template) document is stashed on first preview so stepping to the next
    // record re-renders from the template, and leaving the preview restores it. With no data, prompts the
    // user to Select Recipients first.
    private sealed class PreviewMergeRecordCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var workflow = new MailMergeSessionWorkflow(session);
            var preview = workflow.EnsurePreviewing(
                CurrentMailMergeDocument(editor, session));
            if (!Realize(editor, preview))
                return;

            var action = PreviewNavigationDialog.Ask(
                Window.GetWindow(editor),
                preview.CurrentIndex,
                session.Data!.Count);
            switch (action.Kind)
            {
                case PreviewAction.Move:
                    Realize(editor, workflow.MovePreviewTo(editor.Model, action.TargetIndex));
                    break;
                case PreviewAction.Done:
                    Realize(editor, workflow.TogglePreview(editor.Model));
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
            var workflow = new MailMergeSessionWorkflow(session);
            Realize(
                editor,
                workflow.NavigatePreview(
                    CurrentMailMergeDocument(editor, session),
                    action));
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
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.FindRecipient);
            if (!validation.IsValid)
            {
                _showInfo(owner, validation.Message);
                return;
            }

            var query = _ask(owner);
            if (query is null)
                return;

            var execution = workflow.FindRecipient(query);
            if (execution.DocumentToLoad is { } document)
                editor.LoadModel(document);
            _showInfo(owner, execution.Message);
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
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.CheckForErrors);
            if (!validation.IsValid)
            {
                _showInfo(owner, validation.Message);
                return;
            }

            if (_ask(owner) is not { } selected)
                return;

            var execution = workflow.CheckForErrors(
                CurrentMailMergeDocument(editor, session),
                selected);
            if (!execution.Success || execution.Result is not { } result)
            {
                _showInfo(owner, execution.Message);
                return;
            }

            foreach (var message in execution.Messages)
                _showInfo(owner, message);
            if (execution.ReportDocument is not null && openReportDocument is null)
                _showInfo(owner, execution.Message);

            if (result.ShouldCompleteMerge)
                _completeMerge(context);

            if (execution.ReportDocument is { } report)
                openReportDocument?.Invoke(report);
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

    // Mailings > Finish & Merge: produce the merged documents and load the concatenation of every record
    // into the editor as a single document (records separated by a page break), so the result is visible
    // and saveable. This replaces the editor's content; the template is no longer needed afterwards.
    // Native Fill-in / Ask fields with \o are collected once before the run. Fields without \o prompt
    // as each selected record is evaluated, so skipped records do not display irrelevant dialogs.
    internal sealed class FinishMergeCommand(
        DocumentView editor,
        MailMergeSession session,
        Action<TextDocument>? printDocument = null,
        Action<IReadOnlyList<int>>? emailDocuments = null,
        Func<Window?, int, int, MailMergeFinishPlan?>? ask = null,
        Action<Window?, string>? showInfo = null,
        Func<Window?, string, string, string, string?>? askInteractivePrompt = null) : IRibbonCommand
    {
        private readonly Func<Window?, int, int, MailMergeFinishPlan?> _ask = ask ?? MailMergeFinishDialog.Ask;
        private readonly Action<Window?, string> _showInfo = showInfo ??
            ((owner, message) => DialogMessageHelper.ShowInfo(owner, message, "Mail Merge"));
        private readonly Func<Window?, string, string, string, string?> _askInteractivePrompt =
            askInteractivePrompt ?? MergeRulePromptDialog.AskPrompt;

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.FinishMerge);
            if (!validation.IsValid)
            {
                _showInfo(owner, validation.Message);
                return;
            }

            var data = session.Data!;
            var finishPlan = _ask(owner, data.Count, session.CurrentIndex);
            if (finishPlan is not { Success: true })
                return;
            var route = workflow.RouteFinish(
                finishPlan,
                printingAvailable: printDocument is not null,
                emailAvailable: emailDocuments is not null);
            if (!route.Success)
            {
                _showInfo(owner, route.Message);
                return;
            }
            if (route.Route == MailMergeFinishRoute.Email)
            {
                emailDocuments!(route.EmailRecordIndexes);
                editor.Focus();
                return;
            }

            // Use the stashed template if previewing; otherwise the current editor content is the template.
            var template = CurrentMailMergeDocument(editor, session);

            // Collect \o Fill-in and Ask prompts once before the merge run starts.
            var mergeState = new MergeState();
            if (!CollectFillInAndAskAnswers(template, mergeState, owner))
            {
                editor.Focus();
                return;
            }
            mergeState.RecordPromptResolver = (prompt, _) => _askInteractivePrompt(
                owner,
                prompt.Kind == MailMergeInteractivePromptKind.FillIn ? "Fill-in" : "Ask",
                prompt.Prompt,
                prompt.DefaultAnswer);

            var execution = workflow.BuildFinish(template, finishPlan, mergeState);
            if (!execution.Success || execution.Document is null)
            {
                _showInfo(owner, execution.Message);
                return;
            }

            if (route.Route == MailMergeFinishRoute.Printer)
            {
                printDocument!(execution.Document);
                editor.Focus();
                return;
            }

            editor.LoadModel(execution.Document);
            workflow.CompleteFinish(execution);
            _showInfo(owner, execution.Message);
            editor.Focus();
        }

        // Scan the template for \o Fill-in and Ask instructions and prompt once per unique key.
        private bool CollectFillInAndAskAnswers(TextDocument template, MergeState state, Window? owner)
        {
            foreach (var prompt in MailMergeInteractivePromptPlanner.Plan(template))
            {
                var title = prompt.Kind == MailMergeInteractivePromptKind.FillIn ? "Fill-in" : "Ask";
                var answer = _askInteractivePrompt(
                    owner, title, prompt.Prompt, prompt.DefaultAnswer);
                if (answer is null)
                    return false;

                MailMergeInteractivePromptPlanner.ApplyResponse(state, prompt, answer);
            }

            return true;
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

    // Mailings > Send E-mail Messages: gather Word-style delivery intent, merge one message-body draft
    // per valid recipient, and hand each draft to the OS default mail client. The client owns review/send.
    private sealed class EmailMergeCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => Execute([]);

        public void Execute(IReadOnlyList<int> selectedRecordIndexes)
        {
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.SendEmail);
            if (!validation.IsValid)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    validation.Message,
                    "Mail Merge");
                return;
            }

            var data = session.Data!;
            var owner = Window.GetWindow(editor);
            var intent = EmailMergeDialog.Ask(owner, data, session.CurrentIndex, selectedRecordIndexes);
            if (intent is null)
                return;

            var template = CurrentMailMergeDocument(editor, session);
            var launch = workflow.ExecuteEmailDrafts(
                template,
                intent,
                target => DesktopExternalUriLauncher.Open(target) == ExternalUriLaunchResult.Launched);
            if (!launch.Success)
            {
                DialogMessageHelper.ShowInfo(
                    owner,
                    launch.Message,
                    "Mail Merge");
                return;
            }

            DialogMessageHelper.ShowInfo(
                owner,
                launch.Message,
                "Mail Merge");
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
            var workflow = new MailMergeSessionWorkflow(session);
            var validation = workflow.Validate(MailMergeOperation.FilterSortRecipients);
            if (!validation.IsValid)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    validation.Message,
                    "Mail Merge");
                return;
            }

            var data = session.Data!;
            var updatedData = FilterSortRecipientsDialog.Ask(Window.GetWindow(editor), data);
            if (updatedData is null)
                return; // cancelled

            var transition = workflow.ApplyRecipientFilter(updatedData);
            Realize(editor, transition);

            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                transition.Message,
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

    }

    internal static void ApplyLabelSheet(
        DocumentView editor,
        MailMergeSession session,
        LabelSetupResult label)
    {
        editor.CommitToModel();
        var rows = Math.Max(1, label.Rows);
        var columns = Math.Max(1, label.Columns);
        var template = session.IsPreviewing ? session.Template! : editor.Model;
        var cellContents = session.BuildLabelCellContents(template, rows * columns);

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
            foreach (var swatchHex in FreeWRibbonPaletteCatalog.PageColorPickerSwatches)
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
    private sealed class InsertPageNumberCommand(
        Func<DocumentView> resolveEditor,
        PageNumberPosition position) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
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
    private sealed class InsertFieldCommand(
        Func<DocumentView> resolveEditor,
        Func<Window?, string?> askFieldInstruction) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            editor.Focus();
            var instruction = askFieldInstruction(Window.GetWindow(editor));
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

            if (FreeWRibbonNumericValueParser.TryParseObjectPosition(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    out var parsedPosition))
            {
                editor.SetSelectedShapePosition(
                    parsedPosition.HorizontalOffsetPt,
                    parsedPosition.VerticalOffsetPt,
                    parsedPosition.HorizontalAnchor,
                    parsedPosition.VerticalAnchor);
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

    private sealed class ShapeSizeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var shape = editor.SelectedShape();
            if (shape is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor), "Select a shape first.", "Size");
                return;
            }

            if (FreeWRibbonNumericValueParser.TryParseObjectSize(
                    context.SelectedValue,
                    CultureInfo.InvariantCulture,
                    out var parsedSize))
            {
                editor.SetSelectedShapeSize(parsedSize.WidthPt, parsedSize.HeightPt);
                return;
            }

            if (ImageSizeDialog.Prompt(Window.GetWindow(editor), shape.WidthPt, shape.HeightPt) is { } size)
                editor.SetSelectedShapeSize(size.Width, size.Height);
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
}
