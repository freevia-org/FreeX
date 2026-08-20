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
using FreeW.Ribbon.Definitions;

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
    private static void ShowImageSelectionRequired(DocumentView editor, string titleResourceKey) =>
        DialogMessageHelper.ShowInfo(
            Window.GetWindow(editor),
            UiText.Get("Image_SelectPictureFirst_Message"),
            UiText.Get(titleResourceKey));

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
        BuildCore(editor, stateStore, hostPorts: null, FreeWWpfRibbonNativeExecutionPorts.Empty);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        FreeWRibbonHostExecutionPorts hostPorts,
        FreeWWpfRibbonNativeExecutionPorts? nativePorts = null)
    {
        ArgumentNullException.ThrowIfNull(hostPorts);
        nativePorts ??= FreeWWpfRibbonNativeExecutionPorts.Empty;

        return BuildCore(editor, stateStore, hostPorts, nativePorts);
    }

    private static RibbonCommandRegistry BuildCore(
        DocumentView editor,
        RibbonStateStore stateStore,
        FreeWRibbonHostExecutionPorts? hostPorts,
        FreeWWpfRibbonNativeExecutionPorts nativePorts)
    {
        var formatting = CreateFormattingSession(editor);
        var onPrintPreview = hostPorts?.OpenPrintPreview;
        var onToggleNavPane = hostPorts?.ToggleNavigationPane;
        var isNavPaneVisible = hostPorts?.IsNavigationPaneVisible;
        var onToggleReadMode = hostPorts?.ToggleReadMode;
        var isReadModeActive = hostPorts?.IsReadModeActive;
        var onTogglePrintLayout = hostPorts?.SetPrintLayout;
        var isPrintLayoutActive = hostPorts?.ResolvePrintLayoutActive();
        var onToggleOutlineView = hostPorts?.SetOutlineView;
        var isOutlineViewActive = hostPorts?.IsOutlineViewActive;
        var onZoomDialog = hostPorts?.OpenZoomDialog;
        Action? onZoom100 = hostPorts is null ? null : () => hostPorts.ApplyZoom(1.0, 0);
        var onZoomOnePage = hostPorts?.ZoomOnePage;
        var onZoomPageWidth = hostPorts?.ZoomPageWidth;
        var onWebLayout = hostPorts?.SetWebLayout;
        var isWebLayoutActive = hostPorts?.ResolveWebLayoutActive();
        var onDraftView = hostPorts?.SetDraftView;
        var isDraftViewActive = hostPorts?.ResolveDraftViewActive();
        var onToggleRevealFormatting = hostPorts?.ToggleRevealFormatting;
        var isRevealFormattingVisible = hostPorts?.IsRevealFormattingVisible;
        var onToggleRuler = hostPorts?.ToggleRuler;
        var isRulerVisible = hostPorts?.IsRulerVisible;
        var onToggleMultiplePages = hostPorts?.ToggleMultiplePages;
        var isMultiplePagesActive = hostPorts?.IsMultiplePagesActive;
        var onToggleSideToSide = hostPorts?.ToggleSideToSide;
        var isSideToSideActive = hostPorts?.IsSideToSideActive;
        var onToggleSplitWindow = hostPorts?.ToggleSplit;
        var isSplitWindowActive = hostPorts?.IsSplitActive;
        var onToggleNotesPane = hostPorts?.ToggleNotesPane;
        var isNotesPaneVisible = hostPorts?.IsNotesPaneVisible;
        var onOpenHeaderFooterPane = hostPorts?.OpenHeaderFooterPane;
        var onCloseHeaderFooterPane = hostPorts?.CloseHeaderFooterPane;
        var onTogglePagedEditView = hostPorts?.TogglePagedEditView;
        var isPagedEditViewActive = hostPorts?.ResolvePagedEditViewActive();
        var onReadModeColumnWidth = hostPorts?.ApplyReadModeColumnWidth;
        var onReadModePageColor = hostPorts?.ApplyReadModePageColor;
        var onNewWindow = hostPorts?.NewWindow;
        var onArrangeAll = hostPorts?.ArrangeAll;
        var askHeaderFooterText = nativePorts.AskHeaderFooterText;
        var onOpenMailMergeErrorReport = hostPorts?.OpenMailMergeErrorReport;
        var onPrintMailMergeDocument = hostPorts?.PrintMailMergeDocument;
        var resolveFieldEditor = nativePorts.ResolveFieldEditor;
        var askFieldInstruction = nativePorts.AskFieldInstruction;

        var registry = new FreeWRibbonCommandBindingPorts();
        FreeWRibbonHostExecutionCommands? hostCommands = null;
        if (hostPorts is not null)
        {
            hostCommands = FreeWRibbonHostExecutionProfile.Register(
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

        IRibbonStatefulCommand CreateToggle(
            FreeWRibbonCommandAction action,
            FontEffectRibbonKind kind,
            Action execute)
        {
            var cmd = FontEffectRibbonStatePlanner.CreateCommand(
                kind,
                execute,
                editor.GetSelectionFormatting,
                () => editor.CanFormatSelection,
                () => editor.Focus());
            stateful.Add((FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action), cmd));
            return cmd;
        }

        void ToggleRouted(RoutedCommand command, Func<bool> tryModelToggle)
        {
            if (tryModelToggle())
                return;
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }

        Action CreateCharacterEffectExecution(CharacterEffect effect)
        {
            var command = new CharacterEffectCommand(editor, effect);
            return () => command.Execute(RibbonCommandContext.Empty);
        }

        var bold = CreateToggle(FreeWRibbonCommandAction.Bold, FontEffectRibbonKind.Bold,
            () => ToggleRouted(EditingCommands.ToggleBold,
                () => editor.TryToggleSelectedRunFormatting(f => f.Bold, (f, value) => f with { Bold = value })));
        var italic = CreateToggle(FreeWRibbonCommandAction.Italic, FontEffectRibbonKind.Italic,
            () => ToggleRouted(EditingCommands.ToggleItalic,
                () => editor.TryToggleSelectedRunFormatting(f => f.Italic, (f, value) => f with { Italic = value })));
        var underline = CreateToggle(FreeWRibbonCommandAction.Underline, FontEffectRibbonKind.Underline,
            () => ToggleRouted(EditingCommands.ToggleUnderline,
                () => editor.TryToggleSelectedRunFormatting(f => f.Underline, (f, value) => f with { Underline = value })));
        var strikethrough = CreateToggle(FreeWRibbonCommandAction.Strikethrough, FontEffectRibbonKind.Strikethrough,
            CreateCharacterEffectExecution(CharacterEffect.Strikethrough));
        var smallCaps = CreateToggle(FreeWRibbonCommandAction.Smallcaps, FontEffectRibbonKind.SmallCaps,
            CreateCharacterEffectExecution(CharacterEffect.SmallCaps));
        var allCaps = CreateToggle(FreeWRibbonCommandAction.Allcaps, FontEffectRibbonKind.AllCaps,
            CreateCharacterEffectExecution(CharacterEffect.AllCaps));
        var superscript = CreateToggle(FreeWRibbonCommandAction.Superscript, FontEffectRibbonKind.Superscript,
            CreateCharacterEffectExecution(CharacterEffect.Superscript));
        var subscript = CreateToggle(FreeWRibbonCommandAction.Subscript, FontEffectRibbonKind.Subscript,
            CreateCharacterEffectExecution(CharacterEffect.Subscript));

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
        FontEffectRibbonWorkflow.Register(
            registry,
            new FontEffectRibbonPorts(
                Bold: bold,
                Italic: italic,
                Underline: underline,
                Strikethrough: strikethrough,
                SmallCaps: smallCaps,
                AllCaps: allCaps,
                Superscript: superscript,
                Subscript: subscript,
                GrowFont: new RoutedEditCommand(editor, EditingCommands.IncreaseFontSize),
                ShrinkFont: new RoutedEditCommand(editor, EditingCommands.DecreaseFontSize)));

        // Home > Font: character border and character shading (new W20 commands). These are model-only
        // run properties with full DOCX round-trip (w:rBdr / w:shd). Character Border opens a border-
        // colour/style picker; Character Shading opens a colour swatch picker like paragraph shading.
        registry.Bind(FreeWRibbonCommandAction.CharBorder, new CharacterBorderCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.CharShading, new CharacterShadingCommand(editor));

        // Review > Language > Set Proofing Language: opens a dialog listing common BCP-47 tags and
        // applies the chosen language to the selected runs (rPr/w:lang) for spell-check fidelity.
        registry.Bind(FreeWRibbonCommandAction.SetProofingLanguage, new SetProofingLanguageCommand(editor));

        // Home/Layout paragraph behavior is Presentation-owned; these ports preserve WPF routed
        // editing commands and the native Sort dialog while sharing all semantic command mapping.
        var paragraphCommands =
            ParagraphEditingRibbonWorkflow.Register(registry, CreateParagraphEditingPorts(editor));
        stateful.AddRange(paragraphCommands.StatefulCommands.Select(command => (command.Id, command.Command)));
        Routed(FreeWRibbonCommandAction.Select, ApplicationCommands.SelectAll);
        // Home > Paragraph: apply multilevel/legal outline numbering (1, 1.1, 1.1.1) to the selected
        // paragraph(s); the outline definition persists to word/numbering.xml. Tab/Shift+Tab demote
        // and promote the outline depth (ListLevel) of the selected list paragraphs.
        MultilevelListRibbonWorkflow.Register(
            registry,
            new MultilevelListRibbonPorts(
                definition =>
                {
                    editor.Focus();
                    editor.ApplyMultiLevelListDefinition(definition);
                },
                editor.ChangeListLevel,
                () => OpenDefineMultilevelListDialog(editor)));
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
        }, () => FreeWRibbonNumericValueParser.FormatInvariant(
            editor.CurrentRunFormatting.FontSizePt ?? 11));
        registry.Bind(FreeWRibbonCommandAction.FontSize, fontSize);
        stateful.Add(("freew.font-size", fontSize));
        stateStore.SetState("freew.font-size", fontSize.GetState());

        // Insert tab — Pages: prepend a cover page, insert a blank page, or drop a horizontal rule / page break at the caret.
        // Each mutates the model through the view's undo/redo bus and re-renders.
        // Insert > Pages > Cover Page gallery: Default (existing centred layout), Banded (dark-blue title
        // band), and Motion (right-aligned title with date). The top-level id inserts the default preset
        // so clicking the button face (not the dropdown arrow) always works as before.
        CoverPageRibbonWorkflow.Register(
            registry,
            new CoverPageRibbonPorts(preset =>
            {
                editor.Focus();
                editor.InsertCoverPage(preset);
            }));
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
        TableInsertionRibbonWorkflow.Register(
            tableCommands,
            new TableInsertionRibbonPorts((rows, columns) =>
            {
                editor.Focus();
                editor.InsertTable(rows, columns);
            }));
        // Shared Table Tools policy; this host contributes only WPF editor and dialog adapters.
        TableEditingRibbonWorkflow.Register(tableCommands, CreateTableEditingPorts(editor));
        TableStyleRibbonWorkflow.Register(
            registry,
            new TableStyleRibbonPorts(
                editor.PreviewTableStyle,
                editor.EndTableStylePreview,
                editor.CommitTableStylePreview));

        // Table Design > Draw Borders: drag-to-insert table (prompted dimensions) and eraser-merges right.
        tableCommands.Bind(FreeWRibbonCommandAction.DrawTable, new DrawTableCommand(editor));
        tableCommands.Bind(FreeWRibbonCommandAction.Eraser, new EraserCommand(editor));
        // Table Layout Data group — Convert to Text
        tableCommands.Bind(FreeWRibbonCommandAction.TableToText, new ActionRibbonCommand(() => { editor.Focus(); editor.ConvertTableToText('\t'); }));
        // Insert tab — Text: pick a .docx file and insert its body content at the caret (block merge).
        registry.Bind(FreeWRibbonCommandAction.InsertFile, new InsertFileCommand(editor));
        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Bind(FreeWRibbonCommandAction.Picture, new InsertPictureCommand(editor));
        // Insert tab — Illustrations: open the searchable icon picker and insert the chosen SVG
        // icon as a rasterised InlineImage (same round-trip path as Insert Picture).
        // Insert tab — Illustrations > Screenshot: the top-level "freew.screenshot" id only opens the
        // dropdown (no direct insert). "Screen
        // Clipping" drag-selects a screen region and inserts the captured PNG as an inline image through
        // the exact same InsertImage path as Insert Picture.
        registry.Bind(FreeWRibbonCommandAction.ScreenClipping, new ScreenClippingCommand(editor));
        // Insert tab — Illustrations: resize the selected inline image (height scales proportionally).
        var imageObjectCommands = CreateFloatingObjectCommandPorts(editor, ObjectFormatTarget.Picture);
        registry.Bind(
            FreeWRibbonCommandAction.ImageSize,
            FreeWRibbonFloatingObjectCommandFactory.CreateSize(imageObjectCommands));
        // Insert tab — Illustrations: set the selected image's accessibility alt text (wp:docPr @descr),
        // and align the image's (image-only) paragraph left/center/right. Both mutate the model + re-render.
        registry.Bind(FreeWRibbonCommandAction.ImageAltText, new ImageAltTextCommand(editor));
        // Picture Format tab — Arrange > Position.
        FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(
            registry,
            "image",
            imageObjectCommands,
            FreeWRibbonDefinitionData.FloatingPositionPresets);
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
        // Picture Format tab — native border dialog; crop/reset orchestration is Presentation-owned.
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
            DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                UiText.Get("Image_ArtisticEffects_Choose_Message"),
                UiText.Get("Image_ArtisticEffects_Title"));
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
        InsertDrawingGalleryWorkflow.Register(
            registry,
            new InsertDrawingGalleryPorts(shape =>
            {
                editor.Focus();
                editor.InsertShape(shape);
            }));
        // Insert tab — Media: drop a sample equation / chart / WordArt / SmartArt / OLE object at the caret.
        // Each routes through the editor's undoable insert path (mirroring InsertShape) and round-trips
        // through docx (the model + IO already exist; this surfaces them in the ribbon). Sample content is a
        // starting point the user can replace.
        EquationRibbonWorkflow.Register(
            registry,
            new EquationRibbonPorts(equation =>
            {
                editor.Focus();
                editor.InsertEquation(equation);
            }));
        // Shape Size: reuse ImageSizeDialog (same W/H in points).
        var shapeObjectCommands = CreateFloatingObjectCommandPorts(editor, ObjectFormatTarget.Shape);
        registry.Bind(
            FreeWRibbonCommandAction.ShapeSize,
            FreeWRibbonFloatingObjectCommandFactory.CreateSize(shapeObjectCommands));
        foreach (var preset in FreeWRibbonDefinitionData.FloatingSizePresets)
        {
            var captured = preset;
            registry.Register(
                $"freew.shape-size-{captured.Suffix}",
                FreeWRibbonFloatingObjectCommandFactory.CreateSizePreset(
                    shapeObjectCommands,
                    captured.WidthPt,
                    captured.HeightPt));
        }
        // Alt Text: text prompt for shape or WordArt.
        registry.Bind(FreeWRibbonCommandAction.ShapeAltText, new ShapeAltTextCommand(editor));
        // Drawing Tools > Arrange — Position (opens the same dialog as image-position, applied to shape).
        FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(
            registry,
            "shape",
            shapeObjectCommands,
            FreeWRibbonDefinitionData.FloatingPositionPresets);

        // ── WordArt style gallery — original four + extended eleven (W24) ─────────────────────────
        // ── WordArt Transform / Warp (W24) ────────────────────────────────────────────────────────
        WordArtRibbonWorkflow.Register(
            registry,
            new WordArtRibbonPorts(
                HasSelection: () => editor.SelectedWordArt() is not null,
                ApplyStyle: editor.SetSelectedWordArtStyle,
                ApplyWarp: editor.SetSelectedWordArtWarp,
                PrepareExecution: () => editor.Focus()));
        // ── End Drawing Format commands ───────────────────────────────────────────────────────────

        InsertMediaRibbonWorkflow.Register(
            registry,
            new InsertMediaRibbonPorts(
                Chart: new ActionRibbonCommand(() =>
                {
                    editor.Focus();
                    var chart = InsertChartDialog.Prompt(Application.Current?.MainWindow);
                    if (chart is not null)
                        editor.InsertChart(chart);
                }),
                SmartArt: new ActionRibbonCommand(() =>
                {
                    var owner = Application.Current?.MainWindow;
                    var result = InsertSmartArtDialog.Prompt(owner);
                    if (result is null) return;
                    editor.Focus();
                    editor.InsertSmartArt(result);
                }),
                Icon: new InsertIconCommand(editor),
                WordArt: new ActionRibbonCommand(() =>
                {
                    editor.Focus();
                    editor.InsertWordArt(WordArt.Create("WordArt", WordArtStyle.GradientFill));
                }),
                EmbeddedObject: new InsertEmbeddedObjectCommand(editor)));
        // Insert tab — References: prompt for footnote text and insert a footnote reference at the caret.
        var insertFootnote = new InsertFootnoteCommand(editor);
        // Insert tab — References: prompt for endnote text and insert an endnote reference at the caret.
        var insertEndnote = new InsertEndnoteCommand(editor);
        var nextFootnote = new NavigateNoteCommand(editor, footnote: true, previous: false);
        var previousFootnote = new NavigateNoteCommand(editor, footnote: true, previous: true);
        var nextEndnote = new NavigateNoteCommand(editor, footnote: false, previous: false);
        var previousEndnote = new NavigateNoteCommand(editor, footnote: false, previous: true);
        var showNotes = new ShowNotesCommand(editor);
        var footnoteEndnoteOptions = new FootnoteEndnoteOptionsCommand(editor);
        var notesPaneCmd = NoteReferenceRibbonWorkflow.Register(
            referenceCommands,
            new NoteReferenceRibbonPorts(
                () => insertFootnote.Execute(RibbonCommandContext.Empty),
                () => insertEndnote.Execute(RibbonCommandContext.Empty),
                () => nextFootnote.Execute(RibbonCommandContext.Empty),
                () => previousFootnote.Execute(RibbonCommandContext.Empty),
                () => nextEndnote.Execute(RibbonCommandContext.Empty),
                () => previousEndnote.Execute(RibbonCommandContext.Empty),
                () => showNotes.Execute(RibbonCommandContext.Empty),
                onToggleNotesPane is null ? null : () =>
                {
                    editor.CommitToModel();
                    onToggleNotesPane();
                },
                isNotesPaneVisible,
                () => footnoteEndnoteOptions.Execute(RibbonCommandContext.Empty)));
        if (notesPaneCmd is not null)
        {
            stateful.Add((
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ShowNotes),
                notesPaneCmd));
        }
        // Insert tab — References: generate a Table of Contents from the heading outline at the caret,
        // and rebuild it in place (remove the prior TOC region + re-insert). Both route through the bus.
        TableOfContentsRibbonWorkflow.Register(
            referenceCommands,
            new TableOfContentsRibbonPorts(
                () =>
                {
                    editor.Focus();
                    editor.InsertTableOfContents();
                },
                () =>
                {
                    editor.Focus();
                    editor.RefreshTableOfContents();
                },
                styleId =>
                {
                    editor.Focus();
                    editor.SetParagraphStyle(styleId);
                }));
        // Insert tab — References: insert an in-text citation (pick an existing source or add a new one),
        // and insert a bibliography built from the document's sources at the caret (reversible).
        var citationRegistration = CitationRibbonWorkflow.Register(
            referenceCommands,
            new CitationRibbonPorts(
                InsertCitation: new InsertCitationCommand(editor),
                ManageSources: new ManageSourcesCommand(editor),
                InsertBibliography: new ActionRibbonCommand(() =>
                {
                    editor.Focus();
                    editor.InsertBibliography();
                }),
                ApplyStyle: editor.ApplyCitationStyle,
                GetStyle: () => editor.ActiveCitationStyle,
                StyleStateChanged: state => stateStore.SetState("freew.citation-style", state)));
        // Insert tab — References: select the active citation/bibliography style (APA / MLA / Chicago) used
        // by the citation + bibliography commands. The combo box delivers its label as SelectedValue.
        stateful.Add(("freew.citation-style", citationRegistration.CitationStyleCommand));
        stateStore.SetState("freew.citation-style", citationRegistration.CitationStyleCommand.GetState());
        // Insert tab — References: captions and cross-references share their command routing while the
        // native shell retains ownership of the label/text and target-picker dialogs.
        CaptionRibbonWorkflow.Register(
            referenceCommands,
            new CaptionRibbonPorts(
                new InsertCaptionCommand(editor),
                label => new InsertCaptionLabelCommand(editor, label)
                    .Execute(RibbonCommandContext.Empty),
                new InsertCrossReferenceCommand(editor)));
        // Insert tab — References: mark the selection (or a prompted term) for the document index, and
        // insert an alphabetical index built from the marked terms at the caret (reversibly via the bus).
        IndexRibbonWorkflow.Register(
            referenceCommands,
            new IndexRibbonPorts(
                () => new MarkIndexEntryCommand(editor).Execute(RibbonCommandContext.Empty),
                () => new InsertIndexCommand(editor).Execute(RibbonCommandContext.Empty),
                () => new UpdateIndexCommand(editor).Execute(RibbonCommandContext.Empty)));
        // Insert tab — References: generate a Table of Figures from the document's figure captions at the
        // caret, and rebuild it in place (remove the prior region + re-insert). Both route through the bus.
        TableOfFiguresRibbonWorkflow.Register(
            referenceCommands,
            new TableOfFiguresRibbonPorts(
                editor.InsertTableOfFigures,
                editor.RefreshTableOfFigures,
                () => editor.Focus()));
        // Insert tab — References: mark the selection as a legal citation (a hidden TA field), and insert /
        // rebuild a Table of Authorities built from those marks, grouped by category (reversibly via the bus).
        TableOfAuthoritiesRibbonWorkflow.Register(
            referenceCommands,
            new TableOfAuthoritiesRibbonPorts(
                () => new MarkCitationCommand(editor).Execute(RibbonCommandContext.Empty),
                () => new InsertTableOfAuthoritiesCommand(editor).Execute(RibbonCommandContext.Empty),
                editor.RefreshTableOfAuthorities,
                () => editor.Focus()));
        // Insert links/bookmarks, Developer controls, and field actions share command identity and
        // content-control mutation ordering; WPF contributes only native dialog/editor adapters.
        InsertEditingRibbonWorkflow.Register(
            registry,
            new InsertEditingRibbonPorts(
                Hyperlink: new InsertHyperlinkCommand(editor),
                EditHyperlink: new EditHyperlinkCommand(editor),
                RemoveHyperlink: new RemoveHyperlinkCommand(editor),
                HyperlinkTooltip: new HyperlinkTooltipCommand(editor),
                Bookmark: new InsertBookmarkCommand(editor),
                LinkBookmark: new LinkToBookmarkCommand(editor),
                BookmarkManager: new BookmarkManagerCommand(editor),
                PrepareContentControlInsertion: () => editor.Focus(),
                InsertPlainTextControl: () => editor.InsertPlainTextControl(),
                InsertRichTextControl: () => editor.InsertRichTextControl(),
                InsertCheckBoxControl: () => editor.InsertCheckBoxControl(),
                InsertDatePickerControl: () => editor.InsertDatePickerControl(),
                InsertDropDownListControl: () => editor.InsertDropDownListControl(),
                InsertComboBoxControl: () => editor.InsertComboBoxControl(),
                UpdateFields: editor.UpdateFields,
                ToggleFieldCodes: editor.ToggleFieldCodes));

        // Insert tab — Quick Parts (AutoText): a shared snippet library persisted under FreeW's data
        // folder. "Save Selection" captures the selection's text and stores it under a prompted name;
        // "Insert Quick Part" picks a saved snippet and drops its text at the caret (reversibly).
        var quickParts = QuickPartLibrary.Load();
        QuickPartRibbonWorkflow.Register(
            registry,
            new QuickPartRibbonPorts(
                new InsertQuickPartCommand(editor, quickParts),
                new SaveQuickPartCommand(editor, quickParts),
                new BuildingBlocksOrganizerCommand(editor, quickParts),
                kind =>
                {
                    var target = resolveFieldTarget();
                    target.Focus();
                    target.InsertField(kind);
                }));

        // Review tab — Comments: prompt for comment text and attach it over the current selection.
        ReviewCommentRibbonWorkflow.Register(
            registry,
            new ReviewCommentRibbonCommands(
                new NewCommentCommand(editor),
                new DeleteCommentCommand(editor),
                new NavigateCommentCommand(editor, previous: true),
                new NavigateCommentCommand(editor, previous: false),
                new ReplyCommentCommand(editor),
                new ResolveCommentCommand(editor),
                new ShowCommentsCommand(editor)));
        // Review tab — Comments: reply to / resolve the comment thread covering the caret (modern threaded
        // comments). Reply prompts for text and appends a child comment; Resolve toggles the thread's done flag.

        // Review tab — Proofing: open the read-only Word Count / Statistics dialog. Commits pending
        // edits first so the counts reflect the current text, then computes from the model.
        registry.Bind(FreeWRibbonCommandAction.Statistics, new StatisticsCommand(editor));

        // Review tab — Proofing > Thesaurus (Shift+F7): opens the Thesaurus docked pane and looks up
        // synonyms for the selected/caret word in the bundled compact synonym dictionary (~3 000 headwords,
        // Moby II derivative, public domain). Typed hosts register the pane action through the shared profile;
        // the minimal editor-only registry retains its explicit unavailable fallback.
        if (hostPorts is null)
        {
            registry.Bind(FreeWRibbonCommandAction.Thesaurus, new ActionRibbonCommand(() =>
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    UiText.Get("Thesaurus_Unavailable_Message"),
                    UiText.Get("Pane_Thesaurus_Heading"));
            }));
        }

        // Review tab — Show Markup > Show Revisions in Balloons: toggle the right-margin balloon overlay.
        // Comments and tracked-change revisions render as rounded rectangle callouts connected to their
        // anchored text by dashed leader lines. Preserve the shared host-profile toggle so WPF projects
        // the live checked state exactly like Avalonia; editor-only contexts fail closed.
        if (hostCommands?.ShowMarkupBalloons is { } showMarkupBalloons)
        {
            stateful.Add((
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ShowMarkupBalloons),
                showMarkupBalloons));
        }
        else
        {
            registry.Bind(
                FreeWRibbonCommandAction.ShowMarkupBalloons,
                FreeWRibbonExecutionProfile.UnavailableCommand);
        }

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
                RejectAllRevisions: editor.RejectAllRevisions,
                IsTrackChangesLockedByProtection: () => editor.RestrictEditingPolicy.ShouldForceTrackChanges));

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
        if (hostCommands is not null)
        {
            stateful.Add((
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ReviewingPane),
                hostCommands.ReviewingPane));
        }

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
        IRibbonCommand EditHeaderFooterSlot(HeaderFooterSlotKind slot)
        {
            var slotName = HeaderFooterDialogPlanner.SlotNameFor(slot);
            return onOpenHeaderFooterPane is not null
                ? new OpenHeaderFooterPaneCommand(editor, slotName, onOpenHeaderFooterPane)
                : new EditHeaderSlotCommand(editor, slotName);
        }

        IRibbonCommand NavigateHeaderFooterSlot(HeaderFooterSlotKind slot)
        {
            var slotName = HeaderFooterDialogPlanner.SlotNameFor(slot);
            if (onOpenHeaderFooterPane is not null)
                return new OpenHeaderFooterPaneCommand(editor, slotName, onOpenHeaderFooterPane);
            return slot == HeaderFooterSlotKind.Header
                ? new GoToHeaderCommand(editor)
                : new GoToFooterCommand(editor);
        }

        var headerFooterPageSettings = HeaderFooterRibbonWorkflow.CreatePageSettingCommands(
            new HeaderFooterPageSettingsPorts(
                GetPageSettings: () => editor.Model.Page,
                ApplyPageSettings: editor.ApplyPageSettings,
                IsEnabled: static () => true,
                ResolveSelectedValue: ComboValue));
        var headerFooterRibbon = HeaderFooterRibbonWorkflow.Register(
            headerFooterCommands,
            new HeaderFooterRibbonBindings(
                Header: new HeaderFooterCommand(editor, isFooter: false, askHeaderFooterText: askHeaderFooterText),
                Footer: new HeaderFooterCommand(editor, isFooter: true, askHeaderFooterText: askHeaderFooterText),
                PageNumber: new InsertPageNumberCommand(() => editor, PageNumberPosition.Bottom),
                PageNumberTop: new InsertPageNumberCommand(() => editor, PageNumberPosition.Top),
                PageNumberBottom: new InsertPageNumberCommand(() => editor, PageNumberPosition.Bottom),
                PageNumberCurrent: new InsertPageNumberCommand(resolveFieldTarget, PageNumberPosition.Current),
                PageNumberFormat: new PageNumberFormatCommand(editor),
                DateTime: new InsertDateTimeCommand(resolveFieldTarget),
                CreateEditSlotCommand: EditHeaderFooterSlot,
                DifferentFirstPage: headerFooterPageSettings.DifferentFirstPage,
                DifferentOddEvenPages: headerFooterPageSettings.DifferentOddEvenPages,
                HeaderFromTop: headerFooterPageSettings.HeaderFromTop,
                FooterFromBottom: headerFooterPageSettings.FooterFromBottom,
                CreateNavigationCommand: NavigateHeaderFooterSlot,
                Close: onCloseHeaderFooterPane is not null
                    ? new ActionRibbonCommand(onCloseHeaderFooterPane)
                    : new CloseHeaderFooterCommand(editor),
                InsertHeaderPageNumber: new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.PageNumber),
                InsertFooterPageNumber: new InsertIntoHeaderSlotCommand(editor, isFooter: true, InsertSlotKind.PageNumber),
                InsertDateTime: new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DateTime),
                InsertDocumentInfo: new InsertIntoHeaderSlotCommand(editor, isFooter: false, InsertSlotKind.DocumentInfo)));
        foreach (var entry in headerFooterRibbon.StatefulCommands)
            stateful.Add((entry.Id, entry.Command));
        registry.Bind(FreeWRibbonCommandAction.Field, new InsertFieldCommand(resolveFieldTarget, askField));

        // Insert tab — Symbols: pick a glyph from a grid, or a formatted current date/time string, and
        // insert it at the caret as ordinary text (flows through the normal edit/undo path).
        registry.Bind(FreeWRibbonCommandAction.Symbol, new InsertSymbolCommand(editor));
        SymbolRibbonWorkflow.Register(
            registry,
            new SymbolRibbonPorts(
                PrepareExecution: () => editor.Focus(),
                InsertSymbol: editor.InsertText));

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
        DropCapRibbonWorkflow.Register(
            registry,
            new DropCapRibbonPorts(
                Dropped: new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.Dropped)),
                InMargin: new ActionRibbonCommand(() => editor.ApplyDropCap(DropCapPosition.InMargin)),
                None: new ActionRibbonCommand(editor.ClearDropCap),
                Options: new DropCapOptionsCommand(editor)));

        // Home > Font > Change Case: open a small menu to pick a target case (UPPERCASE / lowercase /
        // Sentence case / Capitalize Each Word / tOGGLE cASE) and recase the selection's text via the
        // pure ChangeCase helper. The replacement flows through the editor's normal edit/undo path.
        registry.Bind(FreeWRibbonCommandAction.ChangeCase, new ChangeCaseCommand(editor));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection.
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

        // Layout > Paragraph > numeric indent/spacing combos: exact-value controls that mirror Word's
        // Layout tab Paragraph group. Each is stateful so SelectionChanged can push the live value
        // back into the ribbon combo and the displayed number tracks the current paragraph.
        var indentLeft = new FreeWRibbonParagraphValueCommand(formatting, FreeWParagraphValueKind.IndentLeft);
        registry.Bind(FreeWRibbonCommandAction.IndentLeft, indentLeft);
        stateful.Add(("freew.indent-left", indentLeft));

        var indentRight = new FreeWRibbonParagraphValueCommand(formatting, FreeWParagraphValueKind.IndentRight);
        registry.Bind(FreeWRibbonCommandAction.IndentRight, indentRight);
        stateful.Add(("freew.indent-right", indentRight));

        var spaceBefore = new FreeWRibbonParagraphValueCommand(formatting, FreeWParagraphValueKind.SpaceBefore);
        registry.Bind(FreeWRibbonCommandAction.SpaceBefore, spaceBefore);
        stateful.Add(("freew.space-before", spaceBefore));

        var spaceAfter = new FreeWRibbonParagraphValueCommand(formatting, FreeWParagraphValueKind.SpaceAfter);
        registry.Bind(FreeWRibbonCommandAction.SpaceAfter, spaceAfter);
        stateful.Add(("freew.space-after", spaceAfter));

        // Home > Font > Font dialog-launcher (freew.font-dialog): opens a two-tab dialog (Font tab +
        // Advanced tab) covering family/size/style/colour/effects on the Font tab and the full OpenType
        // advanced typography fields (CharacterSpacingPt, KerningMinSizePt, PositionPt, Ligatures,
        // StylisticSet, NumberForm, NumberSpacing) on the Advanced tab. Applies via ApplyFontFormatting
        // which pushes both WPF property values and model-only fields through the undo/redo bus.
        registry.Bind(FreeWRibbonCommandAction.FontDialog, new FontDialogCommand(editor));

        // freew.paragraph-dialog now opens the full two-tab Paragraph dialog (Indents and Spacing +
        // Line and Page Breaks), replacing the previous single-tab ParagraphIndentCommand. All fields
        // that ParagraphIndentCommand previously handled are present on the Indents and Spacing tab.
        registry.Bind(FreeWRibbonCommandAction.ParagraphDialog, new ParagraphDialogCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.TabsDialog, new TabsCommand(editor));

        // Home > Clipboard: Paste Special offers source-preserving RTF at an empty paragraph, plus
        // merge-destination and text-only paths through the shared platform clipboard boundary.
        registry.Bind(FreeWRibbonCommandAction.PasteSpecial, new PasteSpecialCommand(editor));

        // Home > Paragraph: pick or clear paragraph shading.
        registry.Bind(FreeWRibbonCommandAction.ParaShading, new ParagraphShadingCommand(editor));
        // Home / Design > Borders and Shading…: the full dialog (paragraph border, page border, shading).
        registry.Bind(FreeWRibbonCommandAction.BordersShading, new BordersAndShadingCommand(editor));

        // Layout > Table conversions: turn the selected paragraphs into a table (splitting on a chosen
        // delimiter) and turn the caret's table back into delimited paragraphs. Both route through the bus.
        registry.Bind(FreeWRibbonCommandAction.TextToTable, new TextToTableCommand(editor));

        foreach (var binding in FreeWRibbonSemanticCatalog.QuickStyles)
            registry.Bind(binding.Action, new ApplyNamedStyleCommand(editor, binding.StyleId));
        registry.Bind(FreeWRibbonCommandAction.StyleClear, new ActionRibbonCommand(() => { editor.Focus(); editor.SetParagraphStyle(null); }));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        var paragraphStyle = new FreeWRibbonParagraphStyleCommand(formatting);
        registry.Bind(FreeWRibbonCommandAction.Style, paragraphStyle);
        stateful.Add(("freew.style", paragraphStyle));
        stateStore.SetState("freew.style", paragraphStyle.GetState());

        FormattingGalleryRibbonWorkflow.Register(
            registry,
            new FormattingGalleryRibbonPorts(
                PrepareExecution: () => editor.Focus(),
                ApplyFontColor: hex => editor.SetTextColor(hex),
                ApplyParagraphShading: hex => editor.SetParagraphShading(hex, ShadingPattern.Clear),
                ApplyCharacterShading: hex => editor.SetCharacterShading(hex),
                ApplyCharacterBorderColor: hex => editor.SetCharacterBorder(
                    hex is null
                        ? null
                        : new ParagraphBorder(hex, 0.5) { LineStyle = BorderLineStyle.Single }),
                ApplyHighlightColor: hex => editor.SetHighlightColor(hex),
                ApplyNamedStyle: styleId => editor.ApplyNamedStyle(styleId),
                PreviewNamedStyle: editor.PreviewParagraphStyle,
                CancelNamedStylePreview: editor.EndStylePreview,
                CommitNamedStylePreview: editor.CommitStylePreview));

        // Home > Styles: New Style opens a dialog capturing name + formatting + based-on, creates a custom
        // DocumentStyle via the pure StyleManager and applies it to the selection. Manage Styles lets the
        // user modify or delete the catalog's styles (built-ins are guarded against deletion).
        registry.Bind(FreeWRibbonCommandAction.NewStyle, new NewStyleCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ManageStyles, new ManageStylesCommand(editor));

        // Design > Document Formatting: Themes apply a full preset, Colors preserve fonts while applying
        // a palette, Style Sets rewrite built-in styles, and Fonts preserve colours while applying a
        // heading/body font pair. All are backed document-wide style changes.
        var designRibbon = DesignRibbonWorkflow.Register(
            registry,
            new DesignRibbonBindings(
                Formatting: formatting,
                PrepareExecution: () => editor.Focus(),
                ResolveChoice: DesignValue,
                ApplyThemeColors: editor.ApplyThemeColors,
                ApplyFontSet: editor.ApplyFontSet,
                ApplyParagraphSpacingSet: editor.ApplyParagraphSpacingSet,
                ApplyEffectSet: editor.ApplyEffectSet,
                PreviewTheme: editor.PreviewTheme,
                PreviewThemeColors: editor.PreviewThemeColors,
                PreviewStyleSet: editor.PreviewStyleSet,
                PreviewFontSet: editor.PreviewFontSet,
                PreviewParagraphSpacingSet: editor.PreviewParagraphSpacingSet,
                PreviewEffectSet: editor.PreviewEffectSet,
                CancelPreview: editor.EndThemePreview,
                ApplyDefaultStyleSet: () => editor.ApplyStyleSet(DocumentStyleSet.Default),
                ApplyPageColor: editor.SetPageColor,
                ApplyWatermarkText: editor.SetWatermark,
                CustomizeColors: new CustomizeColorsCommand(editor),
                CustomizeFonts: new CustomizeFontsCommand(editor),
                CustomParagraphSpacing: new CustomParagraphSpacingCommand(editor),
                PageColor: new PageColorCommand(editor),
                MorePageColors: new PageColorCommand(editor),
                PageBorders: new BordersAndShadingCommand(editor),
                Watermark: new WatermarkCommand(editor),
                CustomWatermark: new WatermarkCommand(editor)));
        foreach (var entry in designRibbon.StatefulCommands)
        {
            stateful.Add((entry.Id, entry.Command));
            stateStore.SetState(entry.Id, entry.Command.GetState());
        }
        registry.Bind(FreeWRibbonCommandAction.Undo, new ActionRibbonCommand(() => { if (editor.CanUndo) editor.Undo(); }));
        registry.Bind(FreeWRibbonCommandAction.Redo, new ActionRibbonCommand(() => { if (editor.CanRedo) editor.Redo(); }));

        // Layout quick actions share their model policy; WPF contributes only the editor adapter.
        var pageLayoutCommands = PageLayoutRibbonWorkflow.Register(
            registry,
            new PageLayoutRibbonPorts(
                GetPageSettings: () => editor.Model.Page,
                ApplyPageSettings: editor.ApplyPageSettings,
                IsEnabled: () => !editor.IsReadOnly));
        foreach (var entry in pageLayoutCommands.StatefulCommands)
        {
            stateful.Add((entry.Id, entry.Command));
            stateStore.SetState(entry.Id, entry.Command.GetState());
        }
        // Columns: open the Columns dialog or apply Word's backed preset menu choices directly, mutating
        // PageSettings and re-rendering so the live document flow changes immediately.
        registry.Bind(FreeWRibbonCommandAction.Columns, new ColumnsCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.ColumnsMore, new ColumnsCommand(editor));
        // Page Setup: the unified Margins / Paper / Layout dialog (Word's Layout > Page Setup launcher). The
        // "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the Margins / Paper tab.
        registry.Bind(FreeWRibbonCommandAction.PageSetup, new PageSetupCommand(editor, PageSetupDialogTabKind.Margins));
        registry.Bind(FreeWRibbonCommandAction.CustomMargins, new PageSetupCommand(editor, PageSetupDialogTabKind.Margins));
        registry.Bind(FreeWRibbonCommandAction.MorePaperSizes, new PageSetupCommand(editor, PageSetupDialogTabKind.Paper));
        // Line Numbers: Word-style menu items set the backed mode explicitly, while the top-level command keeps
        // the existing cycle behavior for quick access (shown in print preview and the live page adorner).
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
        registry.Bind(FreeWRibbonCommandAction.HyphenationManual, new HyphenationManualCommand(editor));
        registry.Bind(FreeWRibbonCommandAction.HyphenationOptions, new HyphenationOptionsCommand(editor));

        // Design tab — Page Background: "Page Borders" opens the full Borders and Shading dialog,
        // and Watermark sets/clears the page watermark. Both ultimately mutate PageSettings via
        // ApplyPageSettings (commit + re-render) and round-trip through docx save.

        // Design tab — Page Background: pick the whole-page background colour (Word's Page Color). Opens a
        // swatch palette + No Color + More Colors... and sets the model's page BackgroundColorHex (which
        // already round-trips as w:background in docx); the editor recolours the page sheet immediately.

        var viewRibbon = ViewRibbonWorkflow.Register(
            registry,
            new ViewRibbonCommandBindings(
                PrintPreview: new ViewRibbonActionBinding(onPrintPreview),
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: new ViewRibbonToggleBinding(onToggleReadMode, isReadModeActive),
                    ColumnWidth: new ViewRibbonChoiceBinding(
                        onReadModeColumnWidth,
                        ViewRibbonBindingAvailability.Disabled),
                    PageColor: new ViewRibbonChoiceBinding(
                        onReadModePageColor,
                        ViewRibbonBindingAvailability.Disabled)),
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
                    NewWindow: new ViewRibbonActionBinding(
                        onNewWindow,
                        ViewRibbonBindingAvailability.Disabled),
                    ArrangeAll: new ViewRibbonActionBinding(
                        onArrangeAll,
                        ViewRibbonBindingAvailability.Disabled),
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
            FreeWRibbonHostExecutionProfile.RegisterSupportCommands(
                registry,
                FreeWRibbonHostExecutionPorts.Empty);

        // Mailings tab — a simple mail merge. Field placeholders are the literal text «FieldName»
        // (ordinary run text, so they round-trip through docx as plain text). The four commands share a
        // MailMergeSession: Start Mail Merge selects the output mode; "Select Recipients" / "Edit
        // Recipient List" capture CSV/typed records; "Insert Merge Field" drops a «Name» placeholder at
        // the caret; "Preview Results" loads MergeRecord(template, row) into the editor, and the preview
        // navigation commands move through real recipient rows; "Finish & Merge" combines every merged
        // record according to the selected output mode.
        var mergeSession = new MailMergeSession();
        // Write & Insert Fields — Address Block, Greeting Line, Match Fields (Word parity).
        // Special merge fields use Word's native NEXT/MERGEREC/MERGESEQ instructions. Their cached
        // result remains the familiar guillemet label until a merge evaluates the field.
        // Rules dropdown — each sub-command inserts the appropriate rule instruction via a dialog.
        var emailMergeCommand = new EmailMergeCommand(editor, mergeSession);
        // Filter & Sort: refines the active session's MergeData (include/exclude rows, sort column/direction)
        // without touching the merge template. No-ops gracefully when there is no active session or data.
        // Envelopes / Labels: set up the page geometry (and optionally a table grid for labels) via the
        // backed ApplyPageSettings / InsertTable paths. No SMTP or print path — page-setup only.
        MailMergeRibbonWorkflow.Register(
            registry,
            new MailMergeRibbonBindings(
                Envelopes: new EnvelopesCommand(editor),
                Labels: new LabelsCommand(editor, mergeSession),
                StartLetters: new SetMergeModeCommand(editor, mergeSession, MailMergeOutputMode.Letters),
                StartDirectory: new SetMergeModeCommand(editor, mergeSession, MailMergeOutputMode.Directory),
                StartNormalDocument: new ClearMergeSessionCommand(editor, mergeSession),
                SelectRecipients: new SetMergeDataCommand(editor, mergeSession),
                InsertMergeField: new InsertMergeFieldCommand(resolveFieldTarget),
                InsertAddressBlock: new InsertAddressBlockCommand(resolveFieldTarget, mergeSession),
                InsertGreetingLine: new InsertGreetingLineCommand(resolveFieldTarget, mergeSession),
                MatchFields: new MatchFieldsCommand(editor, mergeSession),
                FilterSortRecipients: new FilterSortRecipientsCommand(editor, mergeSession),
                CreateRuleCommand: kind => new InsertMergeRuleCommand(resolveFieldTarget, mergeSession, kind),
                InsertNextRecordField: new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.NextRecordField),
                InsertMergeRecordNumberField: new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.MergeRecordNumberField),
                InsertMergeSequenceNumberField: new InsertSpecialMergeFieldCommand(resolveFieldTarget, MailMerge.MergeSequenceNumberField),
                TogglePreview: new PreviewMergeRecordCommand(editor, mergeSession),
                FirstRecord: new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.First),
                PreviousRecord: new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Previous),
                NextRecord: new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Next),
                LastRecord: new NavigateMergePreviewCommand(editor, mergeSession, MailMergePreviewNavigationAction.Last),
                FinishMerge: new FinishMergeCommand(
                    editor,
                    mergeSession,
                    printDocument: onPrintMailMergeDocument,
                    emailDocuments: indexes => emailMergeCommand.Execute(indexes)),
                SendEmail: emailMergeCommand,
                FindRecipient: new FindMergeRecipientCommand(editor, mergeSession),
                CheckErrors: new CheckMergeErrorsCommand(
                    editor,
                    mergeSession,
                    openReportDocument: onOpenMailMergeErrorReport)));

        FreeWRibbonEditorExecutionProfile.RegisterFamilies(
            registry,
            tableCommands.Build(),
            referenceCommands.Build(),
            headerFooterCommands.Build());
        FreeWRibbonEditorExecutionProfile.RegisterFloating(
            registry,
            CreateFloatingExecutionPorts(editor));
        FreeWRibbonEditorExecutionProfile.RegisterImageTableWorkflows(
            registry,
            CreateImageExecutionPorts(editor),
            CreateTableExecutionPorts(editor));
        var chartCommands = FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            registry,
            CreateChartSmartArtExecutionPorts(editor));
        stateful.Add((
            FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.ChartToggleLegend),
            chartCommands.ChartLegend));

        RefreshStatefulCommands();
        return FreeWRibbonExecutionProfile.Build(registry).Registry;
    }

    private static FreeWRibbonFloatingObjectCommandPorts CreateFloatingObjectCommandPorts(
        DocumentView editor,
        ObjectFormatTarget target) =>
        new(
            HasSelection: () => target == ObjectFormatTarget.Picture
                ? editor.SelectedImage() is not null
                : editor.SelectedShape() is not null,
            ApplyPosition: position =>
            {
                if (target == ObjectFormatTarget.Picture)
                {
                    editor.SetSelectedImagePosition(
                        position.HorizontalOffsetPt,
                        position.VerticalOffsetPt,
                        position.HorizontalAnchor,
                        position.VerticalAnchor);
                }
                else
                {
                    editor.SetSelectedShapePosition(
                        position.HorizontalOffsetPt,
                        position.VerticalOffsetPt,
                        position.HorizontalAnchor,
                        position.VerticalAnchor);
                }
            },
            ApplySize: (widthPt, heightPt) =>
            {
                if (target == ObjectFormatTarget.Picture)
                    editor.SetSelectedImageSize(widthPt, heightPt);
                else
                    editor.SetSelectedShapeSize(widthPt, heightPt);
            },
            OpenPositionDialog: () => OpenFloatingPositionDialog(editor, target),
            OpenSizeDialog: () => OpenFloatingSizeDialog(editor, target),
            PrepareExecution: () => editor.Focus());

    private static void OpenFloatingPositionDialog(DocumentView editor, ObjectFormatTarget target)
    {
        if (target == ObjectFormatTarget.Picture)
        {
            if (editor.SelectedImage() is not { } image)
                return;
            var result = ImagePositionDialog.Prompt(
                Window.GetWindow(editor),
                image.HorizontalOffsetPt,
                image.VerticalOffsetPt,
                image.HorizontalAnchor,
                image.VerticalAnchor);
            if (result is { } position)
            {
                editor.SetSelectedImagePosition(
                    position.HOffset,
                    position.VOffset,
                    position.HAnchor,
                    position.VAnchor);
            }
            return;
        }

        if (editor.GetSelectedShapePosition() is not { } shapePosition)
            return;
        var shapeResult = ImagePositionDialog.Prompt(
            Window.GetWindow(editor),
            shapePosition.HorizontalOffsetPt,
            shapePosition.VerticalOffsetPt,
            shapePosition.HorizontalAnchor,
            shapePosition.VerticalAnchor,
            ObjectFormatCommandPlanner.ShapePositionDialogTitle(shapePosition.IsGroupLocal),
            shapePosition.IsGroupLocal);
        if (shapeResult is { } positionResult)
        {
            editor.SetSelectedShapePosition(
                positionResult.HOffset,
                positionResult.VOffset,
                positionResult.HAnchor,
                positionResult.VAnchor);
        }
    }

    private static void OpenFloatingSizeDialog(DocumentView editor, ObjectFormatTarget target)
    {
        var dimensions = target == ObjectFormatTarget.Picture
            ? editor.SelectedImage() is { } image
                ? (image.WidthPt, image.HeightPt)
                : ((double WidthPt, double HeightPt)?)null
            : editor.SelectedShape() is { } shape
                ? (shape.WidthPt, shape.HeightPt)
                : null;
        if (dimensions is not { } current)
            return;

        if (ImageSizeDialog.Prompt(Window.GetWindow(editor), current.WidthPt, current.HeightPt) is not { } result)
            return;
        if (target == ObjectFormatTarget.Picture)
            editor.SetSelectedImageSize(result.Width, result.Height);
        else
            editor.SetSelectedShapeSize(result.Width, result.Height);
    }

    private static FreeWRibbonFloatingExecutionPorts CreateFloatingExecutionPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            HasSelection: target => target == ObjectFormatTarget.Picture
                ? editor.SelectedImage() is not null
                : editor.SelectedShape() is not null,
            HasTransformSelection: () =>
                editor.SelectedImage() is not null ||
                editor.SelectedShape() is not null ||
                editor.SelectedChart() is not null ||
                editor.SelectedSmartArt() is not null ||
                editor.SelectedWordArt() is not null ||
                editor.IsGroupSelected,
            ApplyWrap: (target, wrapping) =>
            {
                if (target == ObjectFormatTarget.Picture)
                    editor.SetSelectedImageWrapping(wrapping);
                else
                    editor.SetSelectedShapeWrapping(wrapping);
            },
            ApplyTransform: (_, command) =>
            {
                return command.Kind switch
                {
                    ObjectFormatTransformKind.Rotate =>
                        editor.RotateSelectedFloating(command.RotationDeltaDegrees),
                    ObjectFormatTransformKind.FlipHorizontal =>
                        editor.FlipSelectedFloating(horizontal: true),
                    ObjectFormatTransformKind.FlipVertical =>
                        editor.FlipSelectedFloating(horizontal: false),
                    _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
                };
            },
            ApplyZOrder: (_, operation) => editor.ChangeSelectedFloatingZOrder(operation),
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
                    editor.SetSelectedImageAlignment(alignment);
                else
                    editor.SetSelectedShapeAlignment(alignment);
            },
            CanArrange: editor.CanArrangeFloatingObjects,
            Arrange: kind => editor.ArrangeFloatingObjects(kind),
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
            ShowFeedback: feedback => DialogMessageHelper.ShowInfo(
                Window.GetWindow(editor),
                feedback.Message,
                feedback.Title));

    private static FreeWRibbonImageExecutionPorts CreateImageExecutionPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedImage: editor.SelectedImage,
            ShowCropDialogAsync: image =>
            {
                var result = ImageCropDialog.Prompt(
                    Window.GetWindow(editor),
                    image.CropLeft,
                    image.CropRight,
                    image.CropTop,
                    image.CropBottom);
                return ValueTask.FromResult<ImageCropDialogResult?>(result is { } crop
                    ? new ImageCropDialogResult(crop.Left, crop.Right, crop.Top, crop.Bottom)
                    : null);
            },
            ApplyCropOutcome: crop => editor.SetSelectedImageCrop(
                crop.Left,
                crop.Right,
                crop.Top,
                crop.Bottom),
            ResetImage: editor.ResetSelectedImage);

    private static ParagraphEditingRibbonPorts CreateParagraphEditingPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CurrentListKind: () => editor.CurrentParagraphFormatting.ListKind,
            ToggleBullets: () => editor.ToggleList(ListKind.Bullet),
            ToggleNumbering: () => editor.ToggleList(ListKind.Number),
            AlignLeft: new RoutedEditCommand(editor, EditingCommands.AlignLeft),
            AlignCenter: new RoutedEditCommand(editor, EditingCommands.AlignCenter),
            AlignRight: new RoutedEditCommand(editor, EditingCommands.AlignRight),
            AlignJustify: new RoutedEditCommand(editor, EditingCommands.AlignJustify),
            IncreaseIndent: () => editor.IncreaseIndent(),
            DecreaseIndent: () => editor.DecreaseIndent(),
            ToggleSpaceBefore: () => editor.ToggleSpaceBefore(),
            ToggleSpaceAfter: () => editor.ToggleSpaceAfter(),
            ToggleKeepWithNext: editor.ToggleKeepWithNext,
            ToggleKeepLinesTogether: editor.ToggleKeepLinesTogether,
            ToggleWidowControl: editor.ToggleWidowControl,
            ToggleParagraphBorder: () => editor.ToggleParagraphBorder(),
            Sort: new SortCommand(editor));

    private static TableEditingRibbonPorts CreateTableEditingPorts(DocumentView editor)
    {
        var splitCell = new SplitCellRibbonCommand(editor);
        var shading = new CellShadingCommand(editor);
        var borders = new CellBordersCommand(editor);
        return new(
            PrepareExecution: () => editor.Focus(),
            CurrentTableFormatting: () => editor.CaretTableContext()?.Table.Formatting,
            ViewGridlines: () => editor.ViewGridlines,
            ToggleHeaderRow: editor.ToggleTableHeaderRow,
            ToggleBandedRows: editor.ToggleTableBandedRows,
            ToggleLastRow: editor.ToggleTableLastRow,
            ToggleFirstColumn: editor.ToggleTableFirstColumn,
            ToggleLastColumn: editor.ToggleTableLastColumn,
            ToggleBandedColumns: editor.ToggleTableBandedColumns,
            ToggleGridlines: () => editor.ViewGridlines = !editor.ViewGridlines,
            SelectTable: editor.SelectTable,
            SelectRow: editor.SelectTableRow,
            SelectColumn: editor.SelectTableColumn,
            SelectCell: editor.SelectTableCell,
            InsertRowAbove: editor.InsertTableRowAbove,
            InsertRowBelow: editor.InsertTableRow,
            InsertColumnLeft: editor.InsertTableColumnLeft,
            InsertColumnRight: editor.InsertTableColumn,
            MergeCells: editor.MergeSelectedCells,
            SplitCell: splitCell,
            Shading: shading,
            Borders: borders,
            DeleteRow: editor.DeleteTableRow,
            DeleteColumn: editor.DeleteTableColumn,
            DeleteTable: editor.DeleteTable,
            SplitTable: editor.SplitTable,
            DistributeRows: editor.DistributeTableRows,
            DistributeColumns: editor.DistributeTableColumns,
            SetAutoFit: editor.SetTableAutoFit,
            SetCellAlignment: editor.SetCaretCellAlignment,
            SetCellTextDirection: editor.SetCaretCellTextDirection,
            SetCellBorders: (edges, clearEdges) => editor.SetCellBorders(
                edges,
                "#000000",
                0.5,
                BorderLineStyle.Single,
                clearEdges),
            ToggleRepeatHeaderRow: editor.ToggleTableRepeatHeaderRow);
    }

    private static FreeWRibbonTableExecutionPorts CreateTableExecutionPorts(DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedCell: () => editor.CaretTableCell() is { } cell
                ? new FreeWRibbonTableCellSelection(cell.Table, cell.RowIndex, cell.ColumnIndex)
                : null,
            SelectedContext: editor.CaretTableContext,
            CanConvertToText: () => editor.CaretTableContext() is not null,
            ShowFormulaDialogAsync: state => ValueTask.FromResult(
                TableFormulaDialog.Prompt(Window.GetWindow(editor), state)),
            ApplyFormulaOutcome: editor.InsertTableFormula,
            ShowPropertiesDialogAsync: context => ValueTask.FromResult(
                TablePropertiesDialog.Prompt(Window.GetWindow(editor), context)),
            ApplyPropertiesOutcome: editor.ApplyTableProperties,
            ShowTableToTextDialogAsync: () => ValueTask.FromResult(
                TableTextConversionDialog.Ask(
                    Window.GetWindow(editor),
                    TableTextConversionDialogPlanner.ResolveText(UiText.Get).TableToTextTitle)),
            ApplyTableToTextOutcome: editor.ConvertTableToText);

    private static FreeWRibbonChartSmartArtExecutionPorts CreateChartSmartArtExecutionPorts(
        DocumentView editor) =>
        new(
            PrepareExecution: () => editor.Focus(),
            CompleteExecution: () => editor.Focus(),
            SelectedChart: editor.SelectedChart,
            SetChartKind: editor.SetSelectedChartKind,
            ApplyChartStyle: editor.ApplySelectedChartStyle,
            ApplyChartColorScheme: editor.ApplySelectedChartColorScheme,
            ApplyChartQuickLayout: editor.ApplySelectedChartQuickLayout,
            ToggleChartLegend: editor.ToggleSelectedChartLegend,
            ShowChartTitleDialogAsync: chart =>
            {
                var result = ChartTitleDialog.Prompt(Application.Current?.MainWindow, chart.Title);
                return ValueTask.FromResult<ChartTitleDialogResult?>(
                    result.Accepted ? new ChartTitleDialogResult(true, result.NewTitle) : null);
            },
            ApplyChartTitleOutcome: result => editor.SetSelectedChartTitle(result.NewTitle),
            ToggleChartTitleFallback: null,
            ShowChartAxisTitlesDialogAsync: chart =>
            {
                var result = ChartAxisTitlesDialog.Prompt(
                    Application.Current?.MainWindow,
                    chart.CategoryAxisTitle,
                    chart.ValueAxisTitle);
                return ValueTask.FromResult<ChartAxisTitlesDialogResult?>(result is { } titles
                    ? new ChartAxisTitlesDialogResult(titles.CategoryTitle, titles.ValueTitle)
                    : null);
            },
            ApplyChartAxisTitlesOutcome: result => editor.SetSelectedChartAxisTitles(
                result.CategoryTitle,
                result.ValueTitle),
            ToggleChartAxisTitlesFallback: null,
            ShowChartDataDialogAsync: chart => ValueTask.FromResult(
                InsertChartDialog.Prompt(Application.Current?.MainWindow, chart)),
            ApplyChartDataOutcome: editor.ReplaceSelectedChartData,
            ShowChartSizeDialogAsync: chart =>
            {
                var result = ChartSizeDialog.Prompt(
                    Application.Current?.MainWindow,
                    chart.WidthPt,
                    chart.HeightPt);
                return ValueTask.FromResult<ChartSizeDialogResult?>(result is { } size
                    ? new ChartSizeDialogResult(size.WidthPt, size.HeightPt)
                    : null);
            },
            ApplyChartSizeOutcome: result => editor.SetSelectedChartSize(result.WidthPt, result.HeightPt),
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
            ShowSmartArtEditDialogAsync: smartArt => ValueTask.FromResult(
                InsertSmartArtDialog.Prompt(Application.Current?.MainWindow, smartArt)),
            ApplySmartArtEditOutcome: editor.ReplaceSelectedSmartArt,
            PreviewChartStyle: editor.PreviewSelectedChartStyle,
            PreviewChartColorScheme: editor.PreviewSelectedChartColorScheme,
            PreviewChartQuickLayout: editor.PreviewSelectedChartQuickLayout,
            CancelChartDesignPreview: editor.CancelChartDesignPreview,
            CommitChartStyle: editor.CommitChartStylePreview,
            CommitChartColorScheme: editor.CommitChartColorSchemePreview,
            CommitChartQuickLayout: editor.CommitChartQuickLayoutPreview,
            PreviewSmartArtLayout: editor.PreviewSelectedSmartArtLayout,
            PreviewSmartArtColorScheme: editor.PreviewSelectedSmartArtColorScheme,
            PreviewSmartArtStyle: editor.PreviewSelectedSmartArtStyle,
            CancelSmartArtDesignPreview: editor.CancelSmartArtDesignPreview,
            CommitSmartArtLayout: editor.CommitSmartArtLayoutPreview,
            CommitSmartArtColorScheme: editor.CommitSmartArtColorSchemePreview,
            CommitSmartArtStyle: editor.CommitSmartArtStylePreview);

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
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.Selection.IsEmpty)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    UiText.Get("ChangeCase_SelectText_Message"),
                    UiText.Get("FreeW_ProductName"));
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
            var window = new ChangeCasePickerWindow
            {
                Title = UiText.Get("Ribbon_Command_ChangeCase_Label"),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8), Width = 200 };
            foreach (var choice in ChangeCaseDialogPlanner.Choices)
            {
                var button = new Button
                {
                    Content = choice.Label,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += (_, _) => { result = choice.Kind; window.Close(); };
                panel.Children.Add(button);
            }

            window.Content = panel;
            window.ShowDialog();
            return result;
        }

        private sealed class ChangeCasePickerWindow : Free.Shared.Ribbon.Wpf.DialogWindow
        {
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

    private static string? DesignValue(RibbonCommandContext context)
    {
        if (context.SelectedValue is { Length: > 0 } selectedValue)
            return selectedValue;
        if (context.Parameters.TryGetValue("value", out var legacyRaw) && legacyRaw is string legacyValue)
            return legacyValue;
        return context.Parameters.TryGetValue(
                   Free.Shared.Ribbon.Wpf.RibbonWpfRenderer.SenderKey,
                   out var sender)
               && sender is System.Windows.Controls.MenuItem { Tag: string header }
            ? header
            : null;
    }

    private static FreeWRibbonFormattingSession CreateFormattingSession(DocumentView editor) =>
        new(new FreeWRibbonFormattingPorts(
            () => editor.CurrentParagraphFormatting,
            points =>
            {
                editor.Focus();
                var (_, right, firstLine) = editor.CurrentParagraphIndents();
                editor.SetParagraphIndents(points, right, firstLine);
            },
            points =>
            {
                editor.Focus();
                var (left, _, firstLine) = editor.CurrentParagraphIndents();
                editor.SetParagraphIndents(left, points, firstLine);
            },
            points =>
            {
                editor.Focus();
                editor.FormatSelectedParagraphSpaceBefore(points);
            },
            points =>
            {
                editor.Focus();
                editor.FormatSelectedParagraphSpaceAfter(points);
            },
            () => editor.Model,
            () => editor.CurrentParagraphStyleId,
            styleId =>
            {
                editor.Focus();
                editor.ApplyNamedStyle(styleId);
            },
            theme =>
            {
                editor.Focus();
                editor.ApplyTheme(theme);
            },
            styleSet =>
            {
                editor.Focus();
                editor.ApplyStyleSet(styleSet);
            }));

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
            var owner = Window.GetWindow(editor);
            var transfer = FreeWClipboardApplicationWorkflow
                .ReadPasteSpecialAsync(editor.PlatformClipboard)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (!transfer.IsSuccess || transfer.Payload is null)
            {
                DialogMessageHelper.ShowWarning(
                    owner,
                    transfer.FeedbackMessage ?? FreeWClipboardApplicationWorkflow.EmptyClipboardMessage);
                return;
            }

            var option = PasteSpecialDialog.Prompt(owner);
            if (option is null)
                return;

            editor.Focus();
            var plan = FreeWClipboardApplicationWorkflow.PlanPaste(transfer.Payload, option.Value);
            if (!editor.ApplyClipboardPastePlan(plan))
            {
                DialogMessageHelper.ShowWarning(
                    owner,
                    FreeWClipboardApplicationWorkflow.EmptyClipboardMessage);
            }
        }
    }

    // Home > Paragraph > Multilevel List > Define New Multilevel List: opens the definition dialog and
    // applies the complete backed definition as one undoable edit.
    private static void OpenDefineMultilevelListDialog(DocumentView editor)
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
    // Design > Reset to Default Style Set: applies the catalog default (Office) to the document.
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
            var window = new FreeWDialogWindow
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
            var window = new FreeWDialogWindow
            {
                Title = UiText.Get("ParagraphShading_Title"),
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
                Content = UiText.Get("Ribbon_Palette_PageColor_NoColor_Label"),
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
    // Bottom / Left / Right / None) with a style, colour and width chooser, then applies it to the
    // current cell selection through the shared logical-grid policy.
    private sealed class CellBordersCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = CellBordersDialog.Prompt(Window.GetWindow(editor));
            if (result is null)
                return;
            editor.SetCellBorders(
                result.Edges,
                result.ColorHex,
                result.WidthPt,
                result.Style,
                result.ClearEdges);
        }

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
                Window.GetWindow(editor), editor.CurrentParagraphFormatting, editor.CurrentSectionPageSettings().PageBorder);
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
            var result = ColumnsDialog.Prompt(Window.GetWindow(editor), editor.CurrentSectionPageSettings());
            if (result is null)
                return;

            editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyColumnsResult(page, result));
        }
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
            var outcome = PageSetupDialog.Prompt(Window.GetWindow(editor), editor.CurrentSectionPageSettings(), initialTab: initialTab);
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
                    DialogMessageHelper.ShowInfo(
                        owner,
                        UiText.Get("Hyphenation_NoWords_Message"),
                        UiText.Get("Hyphenation_Title"));
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

    // Table Design > Draw Borders > Draw Table: prompts for dimensions and inserts a table at the
    // caret. Full freehand drag-draw over the editor is beyond scope; this backed version delivers
    // the table-insertion model (scope: dimension-prompted insert, not mouse-draw).
    private sealed class DrawTableCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var dims = DrawTableDimensionDialog.Ask(
                Window.GetWindow(editor),
                DrawTableDimensionDialogKind.DrawTable);
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
            var dimensions = DrawTableDimensionDialog.Ask(
                Window.GetWindow(editor),
                DrawTableDimensionDialogKind.SplitCells);
            if (dimensions is not { } value)
                return;
            editor.Focus();
            editor.SplitCell(value.Rows, value.Columns);
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
            var request = FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest();
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
            var request = FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest();
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
            DialogMessageHelper.ShowError(owner, message, presentation.ModalTitle ?? UiText.Get("FreeW_ProductName"));
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
                    presentation.ModalTitle ?? UiText.Get("FreeW_ProductName"));
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
        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var image  = IconPickerDialog.Prompt(owner);
            if (image is null)
                return;

            image = PictureInsertionPlanner.CreatePngIcon(
                image.Bytes,
                image.OriginalPixelWidth,
                image.OriginalPixelHeight);

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
        private readonly ScreenClipWorkflowCoordinator _workflow = new();

        public void Execute(RibbonCommandContext context)
        {
            var window = Window.GetWindow(editor);
            var result = _workflow.Execute(Capture, image =>
            {
                editor.Focus();
                editor.InsertImage(image);
            });
            if (result.Outcome == ScreenClipWorkflowOutcome.Failed)
            {
                DialogMessageHelper.ShowError(
                    window,
                    UiText.Format("ScreenClip_Failed_Message_Format", result.FailureMessage ?? string.Empty),
                    UiText.Get("FreeW_ProductName"));
            }

            ScreenClipCapture? Capture()
            {
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

                    return region is { } captured
                        ? ScreenshotCapture.CaptureRegion(captured)
                        : null;
                }
                finally
                {
                    if (window is not null && window.WindowState == WindowState.Minimized)
                        window.WindowState = previousState;
                }
            }
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
            var surface = AltTextDialogPlanner.ResolveText(UiText.Get);
            var image = editor.SelectedImage();
            if (image is null)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    surface.ImageSelectionRequiredMessage,
                    surface.ImageSelectionRequiredTitle);
                return;
            }

            var text = TextPrompt.Ask(Window.GetWindow(editor), surface.Title, surface.DescriptionLabel, image.AltText ?? string.Empty);
            // A null result is a cancel (leave unchanged); an empty/blank string clears the alt text.
            if (text is not null)
                editor.SetSelectedImageAltText(text);
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
                ShowImageSelectionRequired(editor, "Image_PictureBorder_Title");
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
                ShowImageSelectionRequired(editor, "Image_Corrections_Title");
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
                ShowImageSelectionRequired(editor, "Image_Corrections_Title");
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
                ShowImageSelectionRequired(editor, "Image_PictureCorrections_Title");
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
                ShowImageSelectionRequired(editor, "Image_Color_Title");
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
                ShowImageSelectionRequired(editor, "Image_Color_Title");
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
                ShowImageSelectionRequired(editor, "Image_Transparency_Title");
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
                ShowImageSelectionRequired(editor, "Image_Transparency_Title");
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
                ShowImageSelectionRequired(editor, "Image_Recolor_Title");
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
                ShowImageSelectionRequired(editor, "Image_ColorTone_Title");
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
                ShowImageSelectionRequired(editor, "Image_Shadow_Title");
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
                ShowImageSelectionRequired(editor, "Image_Reflection_Title");
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
                ShowImageSelectionRequired(editor, "Image_Glow_Title");
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
                ShowImageSelectionRequired(editor, "Image_SoftEdges_Title");
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
                ShowImageSelectionRequired(editor, "Image_Bevel_Title");
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
                ShowImageSelectionRequired(editor, "Image_ArtisticEffects_Title");
                return;
            }
            editor.SetSelectedImageArtisticEffect(effect);
        }
    }

    // Insert > Links > Link: collect display text + target through the shared dialog contract.
    private sealed class InsertHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = HyperlinkDialog.Ask(
                Window.GetWindow(editor),
                HyperlinkDialogMode.Insert,
                editor.Selection.Text,
                initialAddress: null);
            if (result is { } accepted)
                editor.InsertHyperlink(accepted.DisplayText, accepted.Address);
        }
    }

    // Insert > Links > Edit Hyperlink: seed the shared two-field surface from the complete link span,
    // then update its visible text and external/internal target. A no-op off a link.
    private sealed class EditHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsCaretOnHyperlink())
                return;
            var result = HyperlinkDialog.Ask(
                Window.GetWindow(editor),
                HyperlinkDialogMode.Edit,
                editor.HyperlinkDisplayTextAtCaret(),
                editor.HyperlinkTargetAtCaret());
            if (result is { } accepted)
                editor.EditHyperlink(accepted.Address, accepted.DisplayText);
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
            var tip = ScreenTipDialog.Ask(Window.GetWindow(editor), seed);
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
            var text = TextPrompt.Ask(
                Window.GetWindow(editor),
                UiText.Get("Dialog_Note_InsertFootnoteTitle"),
                UiText.Get("Dialog_Note_FootnoteTextLabel"),
                string.Empty);
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
            var text = TextPrompt.Ask(
                Window.GetWindow(editor),
                UiText.Get("Dialog_Note_InsertEndnoteTitle"),
                UiText.Get("Dialog_Note_EndnoteTextLabel"),
                string.Empty);
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
                        ? UiText.Get("Notes_NoFootnotes_Message")
                        : UiText.Get("Notes_NoEndnotes_Message"),
                    footnote
                        ? UiText.Get("Notes_Footnotes_Title")
                        : UiText.Get("Notes_Endnotes_Title"));
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
                    UiText.Get("Notes_None_Message"),
                    UiText.Get("Notes_Show_Title"));
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

            var dialog = new FreeWDialogWindow
            {
                Title = UiText.Get("Notes_Show_Title"),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var close = new System.Windows.Controls.Button
            {
                Content = UiText.Get("Dialog_Close_Label"),
                IsCancel = true,
                MinWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = UiText.Format(
                    items.Count == 1 ? "Notes_Count_Singular_Format" : "Notes_Count_Plural_Format",
                    items.Count),
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
            MasterSourceStore.Save(masterStore, CreateMasterStore(result.MasterSources));
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
            MasterSourceStore.Save(masterStore, CreateMasterStore(result.MasterSources));
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

            var text = TextPrompt.Ask(
                owner,
                UiText.Get("Caption_Insert_Title"),
                UiText.Get("Caption_Text_FieldLabel"),
                string.Empty);
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
            var text = TextPrompt.Ask(
                owner,
                UiText.Get("Caption_Insert_Title"),
                UiText.Get("Caption_Text_FieldLabel"),
                string.Empty);
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
            var dialog = new FreeWDialogWindow
            {
                Title = UiText.Get("Caption_Insert_Title"),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var newLabel = new System.Windows.Controls.Button { Content = UiText.Get("Caption_NewLabel_Button"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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
                var custom = TextPrompt.Ask(
                    dialog,
                    UiText.Get("Caption_NewLabel_Title"),
                    UiText.Get("Caption_Label_FieldLabel"),
                    string.Empty);
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = UiText.Get("Caption_Label_FieldLabel"), Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Review > Comments > New Comment: prompt for the comment text, then attach it over the current
    // selection. Shared policy resolves the current review identity used for both comments and replies.
    private sealed class NewCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = CommentReplyDialog.Ask(
                Window.GetWindow(editor),
                CommentTextEntryKind.NewComment);
            if (text is null)
                return;

            var identity = ReviewAuthorIdentityPlanner.BuildCommentStamp(
                editor.RevisionAuthor,
                editor.Model.Properties.Author,
                Environment.UserName);
            editor.Focus();
            editor.InsertComment(
                text,
                identity.Author,
                identity.Initials);
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
            var text = CommentReplyDialog.Ask(
                Window.GetWindow(editor),
                CommentTextEntryKind.Reply);
            if (text is null)
                return;

            var identity = ReviewAuthorIdentityPlanner.BuildCommentStamp(
                editor.RevisionAuthor,
                editor.Model.Properties.Author,
                Environment.UserName);
            editor.Focus();
            if (!editor.ReplyToCommentAtCaret(
                    text,
                    identity.Author,
                    identity.Initials))
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    CommentDialogPresentationPlanner.Text.MissingReplyTargetMessage,
                    CommentDialogPresentationPlanner.Text.ReplyTitle);
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
                    CommentDialogPresentationPlanner.Text.MissingResolveTargetMessage,
                    CommentDialogPresentationPlanner.Text.ResolveTitle);
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
                    CommentDialogPresentationPlanner.Text.MissingDeleteTargetMessage,
                    CommentDialogPresentationPlanner.Text.DeleteTitle);
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
                    CommentDialogPresentationPlanner.Text.NoCommentsMessage,
                    previous
                        ? CommentDialogPresentationPlanner.Text.PreviousTitle
                        : CommentDialogPresentationPlanner.Text.NextTitle);
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
                    CommentDialogPresentationPlanner.Text.NoCommentsMessage,
                    CommentDialogPresentationPlanner.Text.ListTitle);
                return;
            }

            CommentListDialog.Show(Window.GetWindow(editor), items);
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
                    UiText.Get("Proofing_AddToDictionary_MissingWord_Message"),
                    UiText.Get("FreeW_ProductName"));
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

            editor.CommitToModel();
            var revised = editor.Model;
            var prompt = ReviewCompareCombineWorkflow.BuildComparePrompt(
                revised,
                editor.CurrentFileName,
                Environment.UserName);
            var picked = CompareDocumentsDialog.Prompt(
                owner,
                prompt.DefaultAuthor,
                prompt.RevisedTitle);
            if (picked is null)
                return;

            try
            {
                var original = DocxReader.Read(picked.OriginalFilePath);
                var compared = ReviewCompareCombineWorkflow.ExecuteCompare(
                    new CompareDocumentsExecutionInput(
                        original,
                        revised,
                        picked.Author,
                        ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow),
                        picked.Settings));
                editor.LoadModel(compared);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(
                    owner,
                    UiText.Format("Review_CompareFailed_Message_Format", ex.Message),
                    UiText.Get("FreeW_ProductName"));
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

            editor.CommitToModel();
            var revisedA = editor.Model;
            var prompt = ReviewCompareCombineWorkflow.BuildCombinePrompt(
                revisedA,
                editor.CurrentFileName,
                Environment.UserName,
                ReviewCompareCombineWorkflow.DefaultReviewerB);

            var picked = CombineDocumentsDialog.Prompt(
                owner,
                prompt.DefaultAuthorA,
                prompt.DefaultAuthorB,
                prompt.ReviewerATitle);
            if (picked is null)
                return;

            try
            {
                var original = DocxReader.Read(picked.OriginalFilePath);
                var revisedB = DocxReader.Read(picked.ReviewerBFilePath);

                var combined = ReviewCompareCombineWorkflow.ExecuteCombine(
                    new CombineDocumentsExecutionInput(
                        original,
                        revisedA,
                        picked.AuthorA,
                        revisedB,
                        picked.AuthorB,
                        ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow)));
                editor.LoadModel(combined);
            }
            catch (Exception ex)
            {
                DialogMessageHelper.ShowError(
                    owner,
                    UiText.Format("Review_CombineFailed_Message_Format", ex.Message),
                    UiText.Get("FreeW_ProductName"));
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
            var existing = editor.BookmarkNameAtCaret();
            var name = TextPrompt.Ask(
                Window.GetWindow(editor),
                UiText.Get("Bookmark_Title"),
                UiText.Get("Bookmark_NameOrRemove_Prompt"),
                existing ?? string.Empty);
            if (name is null)
                return; // cancelled — leave the model untouched

            if (string.IsNullOrWhiteSpace(name))
            {
                // Follows the prompt's own instructions: a blank entry removes the paragraph's existing
                // bookmark instead of silently doing nothing. Nothing to do when there wasn't one.
                if (existing is not null)
                    editor.RemoveBookmark(existing);
                return;
            }

            if (editor.SetBookmarkAtCaret(name) == BookmarkInsertOutcome.DuplicateName)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    UiText.Format("Bookmark_DuplicateName_Message_Format", name),
                    UiText.Get("Bookmark_Title"));
            }
        }
    }

    // Insert > Links > Link to Bookmark: pick an existing bookmark and link the selection to it. If no
    // bookmarks exist yet, tell the user to create one first.
    private sealed class LinkToBookmarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var presentation = LinkBookmarkDialogPlanner.Build(editor.BookmarkNames());
            if (presentation.IsEmpty)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    presentation.EmptyMessage,
                    presentation.EmptyTitle);
                return;
            }

            var chosen = LinkBookmarkDialog.Ask(Window.GetWindow(editor), presentation);
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
            var quickPartText = QuickPartCommandPlanner.ResolveText(UiText.Get);
            var text = editor.Selection.Text;
            if (string.IsNullOrEmpty(text))
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    quickPartText.EmptySelectionMessage,
                    UiText.Get("FreeW_ProductName"));
                return;
            }

            var name = TextPrompt.Ask(
                Window.GetWindow(editor),
                quickPartText.SaveTitle,
                quickPartText.NameLabel,
                string.Empty);
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
            var quickPartText = QuickPartCommandPlanner.ResolveText(UiText.Get);
            var session = new QuickPartInsertSession(library);
            if (session.Current.IsEmpty)
            {
                DialogMessageHelper.ShowInfo(
                    Window.GetWindow(editor),
                    quickPartText.EmptyLibraryMessage,
                    UiText.Get("FreeW_ProductName"));
                return;
            }

            var action = QuickPartPicker.Ask(Window.GetWindow(editor), session);
            if (action is null)
                return; // cancelled

            editor.Focus();
            editor.InsertText(action.Text);
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

    // A tiny modal dialog projecting the shared saved Quick Part session. Returns its accepted action,
    // or null if cancelled. Mirrors BookmarkPicker.
    private static class QuickPartPicker
    {
        public static QuickPartInsertAction? Ask(Window? owner, QuickPartInsertSession session)
        {
            var text = QuickPartCommandPlanner.ResolveText(UiText.Get);
            var state = session.Current;
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 280,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var name in state.Names)
                list.Items.Add(name);
            list.SelectedIndex = state.SelectedIndex;

            QuickPartInsertAction? result = null;
            var dialog = new FreeWDialogWindow
            {
                Title = text.InsertTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = text.InsertButton, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = text.CancelButton, IsCancel = true, MinWidth = 72 };
            void Accept()
            {
                session.SelectIndex(list.SelectedIndex);
                result = session.AcceptSelection();
                if (result is not null)
                    dialog.DialogResult = true;
            }

            ok.Click += (_, _) => Accept();
            list.MouseDoubleClick += (_, _) => Accept();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = text.ItemLabel, Margin = new Thickness(0, 0, 0, 4) });
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
            var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
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
            var dialog = new FreeWDialogWindow
            {
                Title = text.SourcePickerTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = text.InsertButtonLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var addNew = new System.Windows.Controls.Button { Content = text.AddNewSourceButtonLabel, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = text.CancelButtonLabel, IsCancel = true, MinWidth = 72 };

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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = text.SourcePickerLabel, Margin = new Thickness(0, 0, 0, 4) });
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
            var dialog = new FreeWDialogWindow
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

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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
            var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
            SourceManagementAuthorEditorState? result = null;

            var dialog = new FreeWDialogWindow
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
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin)
            };
            var corporateMode = new System.Windows.Controls.RadioButton
            {
                Content = SourceManagementDialogPlanner.CorporateAuthorModeLabel,
                GroupName = "PrimaryAuthorMode",
                IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Corporate,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.CorporateModeTopMargin,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin)
            };
            var peoplePanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(
                    SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                    0,
                    0,
                    0)
            };
            var rowsPanel = new System.Windows.Controls.StackPanel();
            var corporateLabel = new System.Windows.Controls.TextBlock
            {
                Text = SourceManagementDialogPlanner.CorporateAuthorLabel,
                Margin = new Thickness(
                    SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.CorporateLabelBottomMargin)
            };
            var corporateBox = NewAuthorTextBox(
                initial.CorporateAuthor,
                minWidth: SourceManagementAuthorEditorVisualMetrics.CorporateFieldMinimumWidth);

            var personRows = new SourceManagementAuthorRowCollection<RowControls>(
                row =>
                {
                    var grid = CreatePersonRowGrid();
                    var first = NewAuthorTextBox(row.First);
                    var middle = NewAuthorTextBox(row.Middle);
                    var last = NewAuthorTextBox(
                        row.Last,
                        minWidth: SourceManagementAuthorEditorVisualMetrics.LastNameFieldMinimumWidth);
                    AddGridChild(grid, first, 0);
                    AddGridChild(grid, middle, 1);
                    AddGridChild(grid, last, 2);
                    return new RowControls(first, middle, last, grid);
                },
                row => new SourceManagementAuthorPersonRow(
                    row.First.Text ?? string.Empty,
                    row.Middle.Text ?? string.Empty,
                    row.Last.Text ?? string.Empty),
                row => rowsPanel.Children.Add(row.Host),
                () => rowsPanel.Children.Clear());

            void ApplyMode(SourceManagementAuthorEditorPlan plan)
            {
                peoplePanel.IsEnabled = plan.PersonalAuthorFieldsEnabled;
                corporateLabel.IsEnabled = plan.CorporateAuthorFieldEnabled;
                corporateBox.IsEnabled = plan.CorporateAuthorFieldEnabled;
            }

            personRows.Render(initial.PersonalRows);

            var header = CreatePersonRowGrid();
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorFirstNameLabel), 0);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorMiddleNameLabel), 1);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorLastNameLabel), 2);
            peoplePanel.Children.Add(header);
            peoplePanel.Children.Add(rowsPanel);

            var addRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.AddAuthorRowButtonLabel,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.InlineActionTopMargin,
                    SourceManagementAuthorEditorVisualMetrics.ActionSpacing,
                    0)
            };
            addRow.Click += (_, _) => personRows.Render(session.AddPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows);
            var removeRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.RemoveAuthorRowButtonLabel,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.InlineActionTopMargin,
                    0,
                    0)
            };
            removeRow.Click += (_, _) => personRows.Render(session.RemoveFinalPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows);
            peoplePanel.Children.Add(new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children = { addRow, removeRow }
            });

            personalMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Personal,
                personRows.Read(),
                corporateBox.Text));
            corporateMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Corporate,
                personRows.Read(),
                corporateBox.Text));

            var ok = new System.Windows.Controls.Button
            {
                Content = text.OkButtonLabel,
                IsDefault = true,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.ActionSpacing,
                    0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = text.CancelButtonLabel,
                IsCancel = true,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth
            };
            ok.Click += (_, _) =>
            {
                result = session.Accept(personRows.Read(), corporateBox.Text);
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.DialogActionTopMargin,
                    0,
                    0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(SourceManagementAuthorEditorVisualMetrics.BodyInset)
            };
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
            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonRowBottomMargin)
            };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.FirstNameColumnWidth)
            });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.MiddleNameColumnWidth)
            });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.LastNameColumnWidth)
            });
            return grid;
        }

        private static System.Windows.Controls.TextBlock NewHeader(string text) =>
            new()
            {
                Text = text,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.HeaderRightMargin,
                    SourceManagementAuthorEditorVisualMetrics.HeaderBottomMargin)
            };

        private static System.Windows.Controls.TextBox NewAuthorTextBox(
            string? text,
            double minWidth = SourceManagementAuthorEditorVisualMetrics.DefaultNameFieldMinimumWidth) =>
            new()
            {
                Text = text ?? string.Empty,
                MinWidth = minWidth,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.FieldRightMargin,
                    0)
            };

        private static void AddGridChild(
            System.Windows.Controls.Grid grid,
            UIElement child,
            int column)
        {
            System.Windows.Controls.Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }
    }

    private static class ManageSourcesDialog
    {
        public static SourceManagementDialogResult? Ask(
            Window? owner,
            IReadOnlyList<Source> sources,
            IReadOnlyList<Source> masterSources)
        {
            // The planner owns the working copies; mutations stay in dialog state until OK.
            var state = SourceManagementDialogPlanner.BuildInitialState(sources, masterSources);
            var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);

            // ── left pane: Master List ────────────────────────────────────────────────────────
            var masterList = new System.Windows.Controls.ListBox
            {
                MinWidth = ManageSourcesDialogVisualMetrics.ListMinimumWidth,
                MinHeight = ManageSourcesDialogVisualMetrics.ListMinimumHeight,
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    ManageSourcesDialogVisualMetrics.ListBottomMargin)
            };

            // ── right pane: Current Document ─────────────────────────────────────────────────
            var docList = new System.Windows.Controls.ListBox
            {
                MinWidth = ManageSourcesDialogVisualMetrics.ListMinimumWidth,
                MinHeight = ManageSourcesDialogVisualMetrics.ListMinimumHeight,
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    ManageSourcesDialogVisualMetrics.ListBottomMargin)
            };

            SourceManagementDialogResult? result = null;
            var dialog = new ManageSourcesDialogWindow
            {
                Title = text.ManageSourcesTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Owner = owner
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
                var choices = SourceManagementDialogPlanner.BuildSourceConflictResolutionChoices(conflict, text);
                var message = string.Join(
                    Environment.NewLine,
                    SourceManagementDialogPlanner.BuildSourceConflictMessage(conflict, text),
                    string.Empty,
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, text.SourceConflictYesFormat, choices[0].Label),
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, text.SourceConflictNoFormat, choices[1].Label),
                    text.SourceConflictCancelDescription);
                var answer = DialogMessageHelper.ShowMessage(
                    dialog,
                    message,
                    text.SourceConflictDialogTitle,
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
            var masterAdd = new System.Windows.Controls.Button
            {
                Content = text.AddButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneActionSpacing, 0)
            };
            var masterEdit = new System.Windows.Controls.Button
            {
                Content = text.EditButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneActionSpacing, 0)
            };
            var masterDelete = new System.Windows.Controls.Button
            {
                Content = text.DeleteButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth
            };
            var copyBtn = new System.Windows.Controls.Button
            {
                Content = text.CopyToCurrentButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth
            };
            var copyBackBtn = new System.Windows.Controls.Button
            {
                Content = text.CopyToMasterButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, ManageSourcesDialogVisualMetrics.CopyActionSpacing, 0, 0)
            };
            var docAdd = new System.Windows.Controls.Button
            {
                Content = text.AddButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneActionSpacing, 0)
            };
            var docEdit = new System.Windows.Controls.Button
            {
                Content = text.EditButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneActionSpacing, 0)
            };
            var docDelete = new System.Windows.Controls.Button
            {
                Content = text.DeleteButtonLabel,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth
            };
            var ok = new System.Windows.Controls.Button
            {
                Content = text.OkButtonLabel,
                IsDefault = true,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.CloseActionSpacing, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = text.CancelButtonLabel,
                IsCancel = true,
                MinWidth = ManageSourcesDialogVisualMetrics.ButtonMinimumWidth
            };

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
                result = SourceManagementDialogPlanner.BuildResult(state);
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

            var masterPane = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneGap, 0)
            };
            masterPane.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = SourceManagementDialogPlanner.MasterListLabel,
                Margin = new Thickness(0, 0, 0, ManageSourcesDialogVisualMetrics.LabelBottomMargin)
            });
            masterPane.Children.Add(masterList);
            masterPane.Children.Add(masterButtons);

            var centerPane = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, ManageSourcesDialogVisualMetrics.PaneGap, 0)
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
            docPane.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = SourceManagementDialogPlanner.CurrentDocumentListLabel,
                Margin = new Thickness(0, 0, 0, ManageSourcesDialogVisualMetrics.LabelBottomMargin)
            });
            docPane.Children.Add(docList);
            docPane.Children.Add(docButtons);

            var listsRow = new System.Windows.Controls.DockPanel
            {
                Margin = new Thickness(0, 0, 0, ManageSourcesDialogVisualMetrics.ListsBottomMargin)
            };
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

            var panel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(ManageSourcesDialogVisualMetrics.OuterInset)
            };
            panel.Children.Add(listsRow);
            panel.Children.Add(closeButtons);
            dialog.Content = panel;

            RefreshMasterList();
            RefreshDocList();
            return dialog.ShowDialog() == true ? result : null;
        }

    }

    /// <summary>
    /// Opens the production Manage Sources route with deterministic empty lists for the paired visual
    /// harness. The harness owns capture timing; the dialog still uses the same production planner and
    /// native adapter as the ribbon command.
    /// </summary>
    internal static SourceManagementDialogResult? AskManageSourcesForVisualHarness(Window? owner) =>
        ManageSourcesDialog.Ask(owner, [], []);

    private sealed class ManageSourcesDialogWindow : Free.Shared.Ribbon.Wpf.DialogWindow
    {
    }

    private static TextDocument CurrentMailMergeDocument(
        DocumentView editor,
        MailMergeSession session)
    {
        if (!session.IsPreviewing)
            editor.CommitToModel();
        return session.Template ?? editor.Model;
    }

    /// <summary>
    /// Keeps the code-built WPF mail-merge family on the same shared dialog resources and
    /// typography as its Avalonia counterpart. Mail-merge behavior remains Presentation-owned.
    /// </summary>
    private sealed class MailMergeDialogWindow : Free.Shared.Ribbon.Wpf.DialogWindow
    {
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
                DialogMessageHelper.ShowInfo(owner, message, MailMergeDialogMetadata.MailMergeTitle)))(
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
            var name = TextPrompt.Ask(
                Window.GetWindow(editor),
                MailMergeDialogMetadata.InsertMergeFieldTitle,
                MailMergeDialogMetadata.FieldNameLabel,
                string.Empty);
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
                    MailMergeDialogMetadata.MailMergeTitle);
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
                    MailMergeDialogMetadata.MailMergeTitle);
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
                    MailMergeDialogMetadata.MailMergeTitle);
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

    private sealed class InsertMergeRuleCommand(
        Func<DocumentView> resolveEditor,
        MailMergeSession session,
        MailMergeRuleKind kind) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var editor = resolveEditor();
            var request = MailMergeRuleDialogPlanner.CreateRequest(
                kind,
                session.Data?.Header,
                UiText.Get);

            MailMergeRuleAuthoringWorkflow.RunAsync(
                    request,
                    (dialogRequest, _) => ValueTask.FromResult(
                        ShowMergeRuleDialog(Window.GetWindow(editor), dialogRequest)),
                    (plan, _) =>
                    {
                        RealizeMailMergeFieldPlan(editor, plan);
                        return ValueTask.CompletedTask;
                    })
                .AsTask()
                .GetAwaiter()
                .GetResult();
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

    private static MailMergeRuleDialogResponse? ShowMergeRuleDialog(
        Window? owner,
        MailMergeRuleDialogRequest request) =>
        request switch
        {
            MailMergeRuleIfDialogRequest typed =>
                MergeRuleIfDialog.Ask(owner, typed) is { } result
                    ? new MailMergeRuleIfDialogResponse(result)
                    : null,
            MailMergeRuleConditionDialogRequest typed =>
                MergeRuleCondDialog.Ask(owner, typed) is { } result
                    ? new MailMergeRuleConditionDialogResponse(result)
                    : null,
            MailMergeRulePromptDialogRequest typed =>
                MergeRulePromptDialog.AskPrompt(owner, typed.Title, typed.Prompt) is { } result
                    ? new MailMergeRulePromptDialogResponse(result)
                    : null,
            MailMergeRuleNameValueDialogRequest typed =>
                MergeRuleAskSetDialog.Ask(owner, typed) is { } result
                    ? new MailMergeRuleNameValueDialogResponse(result)
                    : null,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null),
        };

    // ── Merge Rule dialogs ───────────────────────────────────────────────────────────────────────

    // If…Then…Else dialog: builds the complete rule definition.
    private static class MergeRuleIfDialog
    {
        public static MailMergeRuleIfDialogResult? Ask(
            Window? owner,
            MailMergeRuleIfDialogRequest request)
        {
            var session = new MailMergeRuleConditionDialogSession(request.FieldNames);
            MailMergeRuleIfDialogResult? result = null;
            var dialog = new MailMergeDialogWindow
            {
                Title = request.Title,
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

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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

            AddRow(0, request.FieldNameLabel, fieldCombo);
            AddRow(1, request.ComparisonLabel, opCombo);
            AddRow(2, request.CompareToLabel, valueBox);
            AddRow(3, request.TrueTextLabel, trueBox);
            AddRow(4, request.FalseTextLabel, falseBox);

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
        public static MailMergeRuleConditionDialogResult? Ask(
            Window? owner,
            MailMergeRuleConditionDialogRequest request)
        {
            var session = new MailMergeRuleConditionDialogSession(request.FieldNames);
            MailMergeRuleConditionDialogResult? result = null;
            var dialog = new MailMergeDialogWindow
            {
                Title = request.Title,
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

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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

            AddRow(0, request.FieldNameLabel, fieldCombo);
            AddRow(1, request.ComparisonLabel, opCombo);
            AddRow(2, request.CompareToLabel, valueBox);

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
            var dialog = new MailMergeDialogWindow
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
            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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
        public static MailMergeRuleNameValueDialogResult? Ask(
            Window? owner,
            MailMergeRuleNameValueDialogRequest request)
        {
            var session = new MailMergeRuleNameValueDialogSession();
            MailMergeRuleNameValueDialogResult? result = null;
            var dialog = new MailMergeDialogWindow
            {
                Title = request.Title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var nameBox  = new System.Windows.Controls.TextBox { MinWidth = 200, Margin = new Thickness(0, 0, 0, 6) };
            var valueBox = new System.Windows.Controls.TextBox { MinWidth = 200, Margin = new Thickness(0, 0, 0, 10) };
            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = request.NameLabel, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(nameBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = request.ValueLabel, Margin = new Thickness(0, 0, 0, 4) });
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
                MailMergeDialogMetadata.MailMergeTitle);
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
            switch (action)
            {
                case MailMergePreviewDialogAction.MovePrevious:
                case MailMergePreviewDialogAction.MoveNext:
                    Realize(editor, workflow.MovePreviewTo(
                        editor.Model,
                        MailMergePreviewDialogPlanner.Move(
                            preview.CurrentIndex,
                            session.Data.Count,
                            next: action == MailMergePreviewDialogAction.MoveNext)));
                    break;
                case MailMergePreviewDialogAction.Done:
                    Realize(editor, workflow.TogglePreview(editor.Model));
                    break;
                case MailMergePreviewDialogAction.Cancel:
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
            (owner => TextPrompt.Ask(
                owner,
                MailMergeDialogMetadata.FindRecipientTitle,
                MailMergeDialogMetadata.FindLabel,
                string.Empty));
        private readonly Action<Window?, string> _showInfo = showInfo ??
            ((owner, message) => DialogMessageHelper.ShowInfo(
                owner,
                message,
                MailMergeDialogMetadata.MailMergeTitle));

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
            ((owner, message) => DialogMessageHelper.ShowInfo(
                owner,
                message,
                MailMergeDialogMetadata.MailMergeTitle));
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
            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.CheckForErrorsTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button
            {
                Content = MailMergeDialogMetadata.OkLabel,
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = MailMergeDialogMetadata.CancelLabel,
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
                Text = MailMergeDialogMetadata.CheckForErrorsLabel,
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
            ((owner, message) => DialogMessageHelper.ShowInfo(
                owner,
                message,
                MailMergeDialogMetadata.MailMergeTitle));
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
                MailMergeRuleDialogPlanner.ResolveInteractivePromptTitle(prompt.Kind, UiText.Get),
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
                var title = MailMergeRuleDialogPlanner.ResolveInteractivePromptTitle(prompt.Kind, UiText.Get);
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
            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.FinishAndMergeTitle,
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
                Text = MailMergeDialogMetadata.FromLabel,
                VerticalAlignment = VerticalAlignment.Center
            });
            range.Children.Add(from);
            range.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = MailMergeDialogMetadata.ToLabel,
                VerticalAlignment = VerticalAlignment.Center
            });
            range.Children.Add(to);

            var ok = new System.Windows.Controls.Button
            {
                Content = MailMergeDialogMetadata.OkLabel,
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = MailMergeDialogMetadata.CancelLabel,
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.MergeToLabel });
            panel.Children.Add(destination);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.RecordsToMergeLabel });
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
                    MailMergeDialogMetadata.MailMergeTitle);
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
                    MailMergeDialogMetadata.MailMergeTitle);
                return;
            }

            DialogMessageHelper.ShowInfo(
                owner,
                launch.Message,
                MailMergeDialogMetadata.MailMergeTitle);
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
            var session = new MailMergeEmailDeliveryDialogSession(data, currentRecordIndex, selectedRecordIndexes);
            var dialogPlan = session.InitialPlan;
            MailMergeEmailDeliveryIntent? result = null;
            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.SendEmailTitle,
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
                Content = MailMergeDialogMetadata.OkLabel,
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = MailMergeDialogMetadata.CancelLabel,
                IsCancel = true,
                MinWidth = 72
            };

            MailMergeEmailDeliveryDialogState CurrentState() =>
                session.Evaluate(
                    toCombo.SelectedItem?.ToString() ?? dialogPlan.RecipientAddressField,
                    subjectBox.Text,
                    outputCombo.SelectedIndex,
                    bodyCombo.SelectedIndex,
                    scopeCombo.SelectedIndex);

            void RefreshValidation()
            {
                var state = CurrentState();
                validation.Text = state.ValidationText;
                ok.IsEnabled = state.CanSubmit;
            }

            toCombo.SelectionChanged += (_, _) => RefreshValidation();
            subjectBox.TextChanged += (_, _) => RefreshValidation();
            outputCombo.SelectionChanged += (_, _) => RefreshValidation();
            bodyCombo.SelectionChanged += (_, _) => RefreshValidation();
            scopeCombo.SelectionChanged += (_, _) => RefreshValidation();

            ok.Click += (_, _) =>
            {
                result = CurrentState().Intent;
                dialog.DialogResult = true;
            };

            var grid = new Grid { Margin = new Thickness(14), MinWidth = 360 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddRow(grid, 0, MailMergeDialogMetadata.ToFieldLabel, toCombo);
            AddRow(grid, 1, MailMergeDialogMetadata.SubjectLabel, subjectBox);
            AddRow(grid, 2, MailMergeDialogMetadata.OutputLabel, outputCombo);
            AddRow(grid, 3, MailMergeDialogMetadata.BodyFormatLabel, bodyCombo);
            AddRow(grid, 4, MailMergeDialogMetadata.SendRecordsLabel, scopeCombo);
            AddRow(grid, 5, MailMergeDialogMetadata.ValidationLabel, validation);

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
                    MailMergeDialogMetadata.MailMergeTitle);
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
                MailMergeDialogMetadata.MailMergeTitle);
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

    // A small modeless-feeling modal that shows the current record and offers Previous / Next / Done.
    // Returns the shared action; Presentation owns navigation bounds and target calculation.
    private static class PreviewNavigationDialog
    {
        public static MailMergePreviewDialogAction Ask(Window? owner, int index, int count)
        {
            var plan = MailMergePreviewDialogPlanner.CreatePlan(index, count);
            var result = MailMergePreviewDialogAction.Cancel;
            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.PreviewResultsTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Controls.TextBlock
            {
                Text = plan.RecordLabel,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var prev = new System.Windows.Controls.Button { Content = $"\u25c0 {MailMergeDialogMetadata.PreviousLabel}", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = plan.CanGoPrevious };
            var next = new System.Windows.Controls.Button { Content = $"{MailMergeDialogMetadata.NextLabel} \u25b6", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = plan.CanGoNext };
            var done = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.DoneLabel, IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };

            prev.Click += (_, _) => { result = MailMergePreviewDialogAction.MovePrevious; dialog.DialogResult = true; };
            next.Click += (_, _) => { result = MailMergePreviewDialogAction.MoveNext; dialog.DialogResult = true; };
            done.Click += (_, _) => { result = MailMergePreviewDialogAction.Done; dialog.DialogResult = true; };

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

            dialog.ShowDialog();
            return result;
        }
    }

    // A dialog to enter the mail-merge data as CSV (first line = headers). Shows the document's discovered
    // merge fields as a hint. Returns the CSV text, or null if cancelled.
    private static class MergeDataDialog
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> fields, string seed)
        {
            var hint = MailMergeDialogMetadata.FormatFieldsHint(fields);

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
            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.MergeDataTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.MergeDataPrompt, Margin = new Thickness(0, 0, 0, 4) });
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

            var ok     = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.OkLabel,     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };

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

            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.MatchFieldsTitle,
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

            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.FilterSortRecipientsTitle,
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

            var ascRadio  = new System.Windows.Controls.RadioButton { Content = MailMergeDialogMetadata.AscendingLabel,  IsChecked = true, Margin = new Thickness(0, 0, 8, 0) };
            var descRadio = new System.Windows.Controls.RadioButton { Content = MailMergeDialogMetadata.DescendingLabel, Margin = new Thickness(0, 0, 0, 0) };

            var sortPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sortPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.SortByLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
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
            var ok     = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.OkLabel,     IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true,  MinWidth = 72 };

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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.FilterInstruction, Margin = new Thickness(0, 0, 0, 8) });
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

            var plan = MailingsEnvelopeLabelPlanner.CreateEnvelopeDialogPlan();
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var s in plan.Sizes)
                combo.Items.Add(s.Name);
            combo.SelectedIndex = plan.SelectedIndex;

            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.EnvelopesTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
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
                Text = plan.Note,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.EnvelopeSizeLabel, Margin = new Thickness(0, 0, 0, 4) });
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

            var plan = MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan();
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 280, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var p in plan.Presets)
                combo.Items.Add(p.Name);
            combo.SelectedIndex = plan.SelectedIndex;

            // Custom rows/columns spinners (shown only when "Custom" is selected).
            var rowsBox = new System.Windows.Controls.TextBox { Text = plan.CustomRowsText, MinWidth = 50, Margin = new Thickness(4, 0, 12, 0) };
            var colsBox = new System.Windows.Controls.TextBox { Text = plan.CustomColumnsText, MinWidth = 50 };
            var customPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = plan.ShowCustomGrid ? Visibility.Visible : Visibility.Collapsed
            };
            customPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.RowsLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            customPanel.Children.Add(rowsBox);
            customPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.ColumnsLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            customPanel.Children.Add(colsBox);

            combo.SelectionChanged += (_, _) =>
                customPanel.Visibility = MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan(
                        combo.SelectedIndex,
                        rowsBox.Text,
                        colsBox.Text).ShowCustomGrid
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            var dialog = new MailMergeDialogWindow
            {
                Title = MailMergeDialogMetadata.LabelsTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.OkLabel, IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = MailMergeDialogMetadata.CancelLabel, IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                var labelPlan = MailingsEnvelopeLabelPlanner.PlanLabel(combo.SelectedIndex, rowsBox.Text, colsBox.Text);
                if (labelPlan.Result is not { } label)
                {
                    DialogMessageHelper.ShowError(dialog, MailMergeDialogMetadata.InvalidLabelGridMessage);
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = MailMergeDialogMetadata.LabelProductLabel, Margin = new Thickness(0, 0, 0, 4) });
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
            var label = isFooter
                ? UiText.Get("HeaderFooter_Footer_Label")
                : UiText.Get("HeaderFooter_Header_Label");

            var text = askHeaderFooterText is { } ask
                ? ask(isFooter, seed)
                : TextPrompt.Ask(
                    Window.GetWindow(editor),
                    UiText.Format("HeaderFooter_Edit_Title_Format", label),
                    UiText.Format("HeaderFooter_Text_Label_Format", label),
                    seed);
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

            var dialog = new FreeWDialogWindow
            {
                Title = UiText.Format("HeaderFooter_Edit_Title_Format", slotLabel),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            // Insert buttons
            var btnPageNumber = new System.Windows.Controls.Button
            {
                Content = UiText.Get("Field_InsertPageNumber_Title"),
                MinWidth = 140,
                Margin = new Thickness(0, 0, 8, 8),
                IsEnabled = state.CanInsertPageNumber
            };
            var btnDateTime = new System.Windows.Controls.Button
            {
                Content = UiText.Get("Field_InsertDateTime_Title"),
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 8)
            };
            var btnField = new System.Windows.Controls.Button
            {
                Content = UiText.Get("Field_Insert_Title"),
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

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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
                Text = UiText.Format("HeaderFooter_Text_Label_Format", slotLabel),
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
            var window = new FreeWDialogWindow
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

    // A modal dialog listing the insertable document field codes, grouped by category (Date and Time /
    // Document Information / Numbering / References). Returns the chosen raw field INSTRUCTION
    // (e.g. " PAGE ", " DATE \@ \"M/d/yyyy\" ", " AUTHOR "), or null if cancelled.
    // This is the backing for Insert > Quick Parts > Field (freew.field) and mirrors Word's Field dialog
    // field-name browser.
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
            if (TableTextConversionDialog.Ask(
                    Window.GetWindow(editor),
                    TableTextConversionDialogPlanner.ResolveText(UiText.Get).TextToTableTitle) is not { } delimiter)
                return; // cancelled
            editor.Focus();
            editor.ConvertSelectionToTable(delimiter);
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
            var dialog = new FreeWDialogWindow
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = UiText.Get("Common_OkText"), IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = UiText.Get("Common_CancelText"), IsCancel = true, MinWidth = 72 };
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

    // ── Drawing Format contextual tab private commands ───────────────────────────────────────────

    // Drawing Format > Size > Alt Text: prompt for shape or WordArt alt text.
    private sealed class ShapeAltTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var surface = AltTextDialogPlanner.ResolveText(UiText.Get);
            var shape = editor.SelectedShape();
            var wordArt = editor.SelectedWordArt();
            if (shape is null && wordArt is null)
            {
                DialogMessageHelper.ShowInfo(Window.GetWindow(editor),
                    surface.ShapeSelectionRequiredMessage, surface.Title);
                return;
            }
            var current = shape?.AltText ?? wordArt?.AltText ?? string.Empty;
            var text = TextPrompt.Ask(Window.GetWindow(editor), surface.Title, surface.DescriptionLabel, current);
            if (text is not null)
            {
                if (shape is not null)
                    editor.SetSelectedShapeAltText(text);
                else
                    editor.SetSelectedWordArtAltText(text);
            }
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
            var window = new FreeWDialogWindow
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
            var window = new FreeWDialogWindow
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
            var chosen = ProofingLanguageDialog.Choose(owner, current);
            if (chosen is null)
                return; // cancelled
            editor.SetProofingLanguage(chosen == string.Empty ? null : chosen);
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
            var page = editor.CurrentSectionPageSettings();
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
