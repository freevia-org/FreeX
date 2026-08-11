using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Adapts WPF-native ribbon services to the Presentation-owned FreeP command workflow.</summary>
internal static class FreePRibbonCommands
{
    public static RibbonCommandRegistry Build(
        RibbonStateStore stateStore,
        EditingSession editor,
        Action? onStartFromStart = null,
        Action? onStartFromCurrent = null,
        Action? onRehearseTimings = null,
        Action? onRecordTimings = null,
        Action? onEditChartData = null,
        Func<SlideCanvas?>? getSlideCanvas = null,
        Func<TableCellTextFormatKind, bool>? tryApplyNotesTextFormat = null,
        Func<TableCellTextValueFormatKind, object?, bool>? tryApplyNotesValueFormat = null,
        Func<TableCellParagraphFormatKind, object?, bool>? tryApplyNotesParagraphFormat = null,
        Action? onEditPoints = null,
        Action? onCustomSlideSize = null,
        OsClipboardService? osClipboard = null,
        Action? onInsertLink = null,
        Action? onInsertSlideZoom = null,
        Action? onInsertSectionZoom = null,
        Action? onInsertSummaryZoom = null,
        Action? onEditZoomTarget = null,
        Action? onEditSummaryZoomTargets = null,
        Action? onOpenSmartArtTextPane = null,
        Action? onConvertSmartArtToShapes = null,
        Action? onFind = null,
        Action? onFindReplace = null,
        Action? onReviewCommentsPane = null,
        Action? onReviewAccessibility = null,
        Action? onReviewAltText = null,
        Action? onReviewReadingOrder = null,
        Action? onSelectionPane = null,
        Action? onReviewProofing = null,
        Action? onAddComment = null,
        Action? onEditComment = null,
        Action? onReplyComment = null,
        Action? onDeleteComment = null,
        Action? onPreviousComment = null,
        Action? onNextComment = null,
        Action? onResolveComment = null,
        Action? onReopenComment = null,
        Action? onAnimPane = null,
        Action? onLayoutPicker = null,
        Action? onTablePicker = null,
        Action<HeaderFooterCommandFocus>? onHeaderFooter = null,
        Func<PresentationViewShowState>? getViewShowState = null,
        Action<PresentationViewShowState>? applyViewShowState = null,
        Func<PresentationViewZoomState>? getViewZoomState = null,
        Action<PresentationViewZoomState>? applyViewZoomState = null,
        Action? onCustomShows = null,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload = null,
        Action<SmartArtColorPreset>? onSmartArtColorPreset = null,
        Action<SmartArtLayoutPreset>? onSmartArtLayoutPreset = null,
        Action<SmartArtQuickStylePreset>? onSmartArtQuickStylePreset = null,
        Action? onEditChartOptions = null,
        Action? onEditChartAxisOptions = null,
        Action? onEditChartSeriesOptions = null,
        Action? onEditChartPointOptions = null,
        Action? onEditChartLayoutOptions = null,
        Action? onEditChartExSeriesLayout = null,
        Action? onEditChartDataTableOptions = null,
        Action? onEditChartBubbleOptions = null,
        Action? onEditChartPieOptions = null,
        Action? onEditChartPlotStyleOptions = null,
        Action? onEditChart3DViewOptions = null,
        Action? onEditChartTextOptions = null,
        Action? onEditChartAreaOptions = null,
        Action? onEditChartProtectionOptions = null,
        Action? onEditRotationOptions = null,
        Action? onInsertEmbeddedObject = null,
        Action<OleObjectInfo>? onOpenEmbeddedObject = null,
        Func<bool>? tryOpenInlineEmbeddedObject = null,
        Action? onTransitionSound = null,
        Func<bool>? getEditPointsEnabled = null,
        Action<bool>? setEditPointsEnabled = null,
        Action? onFormatZoom = null,
        Action? onSetZoomCoverImage = null,
        Action? onResetZoomCoverImage = null,
        Action? onSlideShowSettings = null,
        Func<PresentationAssetImportKind, Task<PresentationAssetImportResult>>? importAsset = null,
        Action<string, string>? onClipboardWriteFailed = null)
    {
        var actionEndpoints = BuildHostActionEndpoints(
            editor,
            getSlideCanvas,
            osClipboard,
            onStartFromStart,
            onStartFromCurrent,
            onRehearseTimings,
            onRecordTimings,
            onEditChartData,
            onEditPoints,
            onInsertLink,
            onInsertSlideZoom,
            onInsertSectionZoom,
            onInsertSummaryZoom,
            onEditZoomTarget,
            onEditSummaryZoomTargets,
            onOpenSmartArtTextPane,
            onConvertSmartArtToShapes,
            onFind,
            onFindReplace,
            onReviewCommentsPane,
            onReviewAccessibility,
            onReviewAltText,
            onReviewReadingOrder,
            onSelectionPane,
            onReviewProofing,
            onAddComment,
            onEditComment,
            onReplyComment,
            onDeleteComment,
            onPreviousComment,
            onNextComment,
            onResolveComment,
            onReopenComment,
            onAnimPane,
            onTablePicker,
            onHeaderFooter,
            applyViewShowState,
            applyViewZoomState,
            pickPictureBulletPayload,
            onSmartArtColorPreset,
            onSmartArtLayoutPreset,
            onSmartArtQuickStylePreset,
            onEditChartOptions,
            onEditChartAxisOptions,
            onEditChartSeriesOptions,
            onEditChartPointOptions,
            onEditChartLayoutOptions,
            onEditChartExSeriesLayout,
            onEditChartDataTableOptions,
            onEditChartBubbleOptions,
            onEditChartPieOptions,
            onEditChartPlotStyleOptions,
            onEditChart3DViewOptions,
            onEditChartTextOptions,
            onEditChartAreaOptions,
            onEditChartProtectionOptions,
            onEditRotationOptions,
            onTransitionSound,
            setEditPointsEnabled,
            onFormatZoom,
            onSetZoomCoverImage,
            onResetZoomCoverImage,
            onCustomShows,
            onSlideShowSettings,
            importAsset,
            onClipboardWriteFailed);
        var profile = FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts
        {
            ActionEndpoints = actionEndpoints,
            QueryEndpoints = new FreePRibbonHostQueryEndpoints
            {
                BeginFormatPainter = () => getSlideCanvas?.Invoke()?.BeginFormatPainter() == true,
                EditPointsEnabled = () =>
                    getEditPointsEnabled?.Invoke() ?? getSlideCanvas?.Invoke()?.EditPointsEnabled,
                ViewShowState = () => getViewShowState?.Invoke(),
                ViewZoomState = () => getViewZoomState?.Invoke(),
            },
            TextActionTargets = BuildTextActionTargets(
                getSlideCanvas,
                tryApplyNotesTextFormat,
                tryApplyNotesValueFormat,
                tryApplyNotesParagraphFormat),
            DesignCommands = new FreePRibbonDesignCommandEndpoints
            {
                OpenCustomSlideSize = _ => onCustomSlideSize?.Invoke(),
                OpenLayoutPicker = _ => onLayoutPicker?.Invoke(),
            },
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                InsertEmbeddedObject = onInsertEmbeddedObject,
                TryOpenInlineEmbeddedObject = tryOpenInlineEmbeddedObject,
                TryOpenSelectedEmbeddedObject = onOpenEmbeddedObject is null
                    ? null
                    : ole =>
                    {
                        onOpenEmbeddedObject(ole);
                        return true;
                    },
            },
        });

        return FreePRibbonHostRegistryComposer.Build(editor, stateStore, profile).Registry;
    }

    private static FreePRibbonHostActionEndpoints BuildHostActionEndpoints(
        EditingSession editor,
        Func<SlideCanvas?>? getSlideCanvas,
        OsClipboardService? osClipboard,
        Action? onStartFromStart,
        Action? onStartFromCurrent,
        Action? onRehearseTimings,
        Action? onRecordTimings,
        Action? onEditChartData,
        Action? onEditPoints,
        Action? onInsertLink,
        Action? onInsertSlideZoom,
        Action? onInsertSectionZoom,
        Action? onInsertSummaryZoom,
        Action? onEditZoomTarget,
        Action? onEditSummaryZoomTargets,
        Action? onOpenSmartArtTextPane,
        Action? onConvertSmartArtToShapes,
        Action? onFind,
        Action? onFindReplace,
        Action? onReviewCommentsPane,
        Action? onReviewAccessibility,
        Action? onReviewAltText,
        Action? onReviewReadingOrder,
        Action? onSelectionPane,
        Action? onReviewProofing,
        Action? onAddComment,
        Action? onEditComment,
        Action? onReplyComment,
        Action? onDeleteComment,
        Action? onPreviousComment,
        Action? onNextComment,
        Action? onResolveComment,
        Action? onReopenComment,
        Action? onAnimPane,
        Action? onTablePicker,
        Action<HeaderFooterCommandFocus>? onHeaderFooter,
        Action<PresentationViewShowState>? applyViewShowState,
        Action<PresentationViewZoomState>? applyViewZoomState,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload,
        Action<SmartArtColorPreset>? onSmartArtColorPreset,
        Action<SmartArtLayoutPreset>? onSmartArtLayoutPreset,
        Action<SmartArtQuickStylePreset>? onSmartArtQuickStylePreset,
        Action? onEditChartOptions,
        Action? onEditChartAxisOptions,
        Action? onEditChartSeriesOptions,
        Action? onEditChartPointOptions,
        Action? onEditChartLayoutOptions,
        Action? onEditChartExSeriesLayout,
        Action? onEditChartDataTableOptions,
        Action? onEditChartBubbleOptions,
        Action? onEditChartPieOptions,
        Action? onEditChartPlotStyleOptions,
        Action? onEditChart3DViewOptions,
        Action? onEditChartTextOptions,
        Action? onEditChartAreaOptions,
        Action? onEditChartProtectionOptions,
        Action? onEditRotationOptions,
        Action? onTransitionSound,
        Action<bool>? setEditPointsEnabled,
        Action? onFormatZoom,
        Action? onSetZoomCoverImage,
        Action? onResetZoomCoverImage,
        Action? onCustomShows,
        Action? onSlideShowSettings,
        Func<PresentationAssetImportKind, Task<PresentationAssetImportResult>>? importAsset,
        Action<string, string>? onClipboardWriteFailed) =>
        new()
        {
            Copy = () => WpfClipboardCommands.Copy(
                editor,
                osClipboard,
                error => onClipboardWriteFailed?.Invoke("Copy", error)),
            Cut = () => WpfClipboardCommands.Cut(
                editor,
                osClipboard,
                error => onClipboardWriteFailed?.Invoke("Cut", error)),
            Paste = () =>
            {
                if (osClipboard is not null)
                    osClipboard.Paste(editor, preferOsClipboard: true);
                else
                    editor.Paste();
            },
            InsertPicture = () => QueueAssetImport(importAsset, PresentationAssetImportKind.Picture),
            InsertVideo = () => QueueAssetImport(importAsset, PresentationAssetImportKind.Video),
            InsertAudio = () => QueueAssetImport(importAsset, PresentationAssetImportKind.Audio),
            OpenTablePicker = onTablePicker,
            MergeTableCells = () => editor.TryMergeActiveTableCell(),
            SplitTableCell = () => editor.TrySplitActiveTableCell(),
            PickPictureBullet = () =>
            {
                if (pickPictureBulletPayload is not null)
                    ApplyPictureBullet(editor, getSlideCanvas?.Invoke(), pickPictureBulletPayload);
                else
                    QueueAssetImport(importAsset, PresentationAssetImportKind.PictureBullet);
            },
            InsertSlideZoom = onInsertSlideZoom,
            InsertSectionZoom = onInsertSectionZoom,
            InsertSummaryZoom = onInsertSummaryZoom,
            EditZoomTarget = onEditZoomTarget,
            EditSummaryZoomTargets = onEditSummaryZoomTargets,
            FormatZoom = onFormatZoom,
            SetZoomCoverImage = onSetZoomCoverImage,
            ResetZoomCoverImage = onResetZoomCoverImage,
            OpenHeaderFooter = onHeaderFooter,
            ApplySmartArtColor = onSmartArtColorPreset,
            ApplySmartArtLayout = onSmartArtLayoutPreset,
            ApplySmartArtQuickStyle = onSmartArtQuickStylePreset,
            ConvertSmartArtToShapes = onConvertSmartArtToShapes,
            OpenSmartArtTextPane = onOpenSmartArtTextPane,
            OpenChartData = onEditChartData,
            OpenChartDisplayOptions = onEditChartOptions,
            OpenChartAxisOptions = onEditChartAxisOptions,
            OpenChartSeriesOptions = onEditChartSeriesOptions,
            OpenChartPointOptions = onEditChartPointOptions,
            OpenChartLayoutOptions = onEditChartLayoutOptions,
            OpenChartExSeriesLayout = onEditChartExSeriesLayout,
            OpenChartDataTableOptions = onEditChartDataTableOptions,
            OpenChartBubbleOptions = onEditChartBubbleOptions,
            OpenChartPieOptions = onEditChartPieOptions,
            OpenChartPlotStyleOptions = onEditChartPlotStyleOptions,
            OpenChart3DViewOptions = onEditChart3DViewOptions,
            OpenChartTextOptions = onEditChartTextOptions,
            OpenChartAreaOptions = onEditChartAreaOptions,
            OpenChartProtectionOptions = onEditChartProtectionOptions,
            OpenHyperlink = onInsertLink,
            OpenRotationOptions = onEditRotationOptions,
            SetEditPointsEnabled = enabled =>
            {
                if (setEditPointsEnabled is not null)
                    setEditPointsEnabled(enabled);
                else
                    onEditPoints?.Invoke();
            },
            OpenFind = onFind,
            OpenReplace = onFindReplace,
            ShowCommentsPane = onReviewCommentsPane,
            ShowAccessibilityPane = onReviewAccessibility,
            ShowAltTextPane = onReviewAltText,
            ShowReadingOrderPane = onReviewReadingOrder,
            ShowSelectionPane = onSelectionPane,
            ShowProofingPane = onReviewProofing,
            AddComment = onAddComment,
            EditComment = onEditComment,
            ReplyComment = onReplyComment,
            DeleteComment = onDeleteComment,
            PreviousComment = onPreviousComment,
            NextComment = onNextComment,
            ResolveComment = onResolveComment,
            ReopenComment = onReopenComment,
            ApplyViewShowState = applyViewShowState,
            ApplyViewZoomState = applyViewZoomState,
            PickTransitionSound = onTransitionSound,
            ToggleAnimationPane = _ => onAnimPane?.Invoke(),
            StartSlideShowFromBeginning = onStartFromStart,
            StartSlideShowFromCurrent = onStartFromCurrent,
            RehearseTimings = onRehearseTimings,
            RecordTimings = onRecordTimings,
            OpenCustomShows = onCustomShows,
            OpenSlideShowSettings = onSlideShowSettings,
        };

    private static FreePRibbonTextActionTargets BuildTextActionTargets(
        Func<SlideCanvas?>? getSlideCanvas,
        Func<TableCellTextFormatKind, bool>? tryApplyNotesTextFormat,
        Func<TableCellTextValueFormatKind, object?, bool>? tryApplyNotesValueFormat,
        Func<TableCellParagraphFormatKind, object?, bool>? tryApplyNotesParagraphFormat) => new()
    {
        Notes = FreePRibbonTextActionEndpointFactory.CreateFormattingTarget(
            tryApplyNotesTextFormat,
            tryApplyNotesValueFormat,
            tryApplyNotesParagraphFormat),
        Shape = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = kind => WithCanvas(
                getSlideCanvas,
                canvas => ApplyShapeTextFormat(canvas, kind)),
            SetParagraphAlignment = alignment => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphAlignment(alignment) == true),
            ApplyListPreset = preset => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphListPreset(preset) == true),
            ToggleBullets = () => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphBulletToggle() == true),
            ToggleNumbering = () => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true),
            Indent = () => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphIndent() == true),
            Outdent = () => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true),
            SetFontFamily = family => WithShapeEditor(
                getSlideCanvas,
                editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithShapeEditor(
                getSlideCanvas,
                editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithShapeEditor(
                getSlideCanvas,
                editor => editor.ApplyColor(color)),
            RemoveHyperlink = () => WithCanvas(
                getSlideCanvas,
                canvas => canvas.TextEditor?.TryApplySelectedShapeRunHyperlink(null) == true),
        },
        Table = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = kind => WithCanvas(
                getSlideCanvas,
                canvas => ApplyTableTextFormat(canvas, kind)),
            SetFontFamily = family => WithTableEditor(
                getSlideCanvas,
                editor => editor.ApplyFont(family)),
            SetFontSize = sizePt => WithTableEditor(
                getSlideCanvas,
                editor => editor.ApplyFontSize(sizePt)),
            SetColor = color => WithTableEditor(
                getSlideCanvas,
                editor => editor.ApplyColor(color)),
        },
    };

    private static bool WithCanvas(
        Func<SlideCanvas?>? getSlideCanvas,
        Func<SlideCanvas, bool> execute) =>
        getSlideCanvas?.Invoke() is { } canvas && execute(canvas);

    private static bool ApplyShapeTextFormat(SlideCanvas canvas, TableCellTextFormatKind kind)
    {
        if (canvas.TextEditor?.IsActive != true)
            return false;

        return ApplyTextFormat(kind, canvas.TextEditor);
    }

    private static bool ApplyTableTextFormat(SlideCanvas canvas, TableCellTextFormatKind kind)
    {
        if (canvas.TableCellEditor?.IsCellRichEditActive != true)
            return false;

        return ApplyTextFormat(kind, canvas.TableCellEditor);
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind kind, InCanvasTextEditor editor)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }

    private static bool ApplyTextFormat(TableCellTextFormatKind kind, InCanvasTableCellEditor editor)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold: editor.ApplyBold(); break;
            case TableCellTextFormatKind.Italic: editor.ApplyItalic(); break;
            case TableCellTextFormatKind.Underline: editor.ApplyUnderline(); break;
            case TableCellTextFormatKind.Strikethrough: editor.ApplyStrikethrough(); break;
            case TableCellTextFormatKind.Superscript: editor.ApplySuperscript(); break;
            case TableCellTextFormatKind.Subscript: editor.ApplySubscript(); break;
            default: return false;
        }

        return true;
    }

    private static bool WithShapeEditor(
        Func<SlideCanvas?>? getSlideCanvas,
        Action<InCanvasTextEditor> execute) =>
        WithCanvas(getSlideCanvas, canvas =>
        {
            if (canvas.TextEditor?.IsActive != true)
                return false;
            execute(canvas.TextEditor);
            return true;
        });

    private static bool WithTableEditor(
        Func<SlideCanvas?>? getSlideCanvas,
        Action<InCanvasTableCellEditor> execute) =>
        WithCanvas(getSlideCanvas, canvas =>
        {
            if (canvas.TableCellEditor?.IsCellRichEditActive != true)
                return false;
            execute(canvas.TableCellEditor);
            return true;
        });

    private static void ApplyPictureBullet(
        EditingSession editor,
        SlideCanvas? canvas,
        Func<PresentationPictureBulletPayload?> pickPictureBulletPayload)
    {
        var payload = pickPictureBulletPayload();
        if (payload is null ||
            canvas?.TextEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true)
            return;

        editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
    }

    private static void QueueAssetImport(
        Func<PresentationAssetImportKind, Task<PresentationAssetImportResult>>? importAsset,
        PresentationAssetImportKind kind)
    {
        if (importAsset is not null)
            _ = importAsset(kind);
    }
}
