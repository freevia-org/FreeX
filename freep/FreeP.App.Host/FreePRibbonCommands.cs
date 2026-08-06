using System.IO;
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
        Action? onSlideShowSettings = null)
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
            onCustomSlideSize,
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
            onLayoutPicker,
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
            onSlideShowSettings);
        var host = new FreePRibbonCommandHostAdapter
        {
            ExecuteAction = action => FreePRibbonHostActionDispatcher.Dispatch(action, actionEndpoints),
            QueryState = query => query.Kind switch
            {
                FreePRibbonHostQueryKind.BeginFormatPainter =>
                    getSlideCanvas?.Invoke()?.BeginFormatPainter() == true,
                FreePRibbonHostQueryKind.EditPointsEnabled =>
                    getEditPointsEnabled?.Invoke() ?? getSlideCanvas?.Invoke()?.EditPointsEnabled,
                FreePRibbonHostQueryKind.ViewShowState => getViewShowState?.Invoke(),
                FreePRibbonHostQueryKind.ViewZoomState => getViewZoomState?.Invoke(),
                _ => null,
            },
            TryHandleTextAction = action => TryHandleTextAction(action, getSlideCanvas?.Invoke()),
        };

        var registry = FreePRibbonCommandWorkflow.Build(editor, stateStore, host).Registry;

        // OLE activation remains native and outside the portable ribbon workflow.
        registry.Register(
            OleInsertionPlanner.InsertEmbeddedObjectCommandId,
            new ActionRibbonCommand(() => onInsertEmbeddedObject?.Invoke()));
        registry.Register(
            OleActivationPlanner.OpenEmbeddedObjectCommandId,
            new ActionRibbonCommand(() =>
                OleActivationPlanner.TryOpenInlineFirst(
                    tryOpenInlineEmbeddedObject,
                    () =>
                    {
                        if (editor.SelectedOleObject is not { } ole)
                            return false;

                        if (onOpenEmbeddedObject is { } open)
                            open(ole);
                        else
                            OleActivationService.TryActivate(ole);
                        return true;
                    })));

        return registry;
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
        Action? onCustomSlideSize,
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
        Action? onLayoutPicker,
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
        Action? onSlideShowSettings) =>
        new()
        {
            Copy = () => WpfClipboardCommands.Copy(editor, osClipboard),
            Cut = () => WpfClipboardCommands.Cut(editor, osClipboard),
            Paste = () =>
            {
                if (osClipboard is not null)
                    osClipboard.Paste(editor, preferOsClipboard: true);
                else
                    editor.Paste();
            },
            InsertPicture = () => ApplyPicture(editor),
            InsertVideo = () => ApplyMedia(editor, isVideo: true),
            InsertAudio = () => ApplyMedia(editor, isVideo: false),
            OpenTablePicker = () =>
            {
                if (onTablePicker is not null)
                    onTablePicker();
                else
                    ApplyBuiltInInsertion(editor, SlideObjectInsertionPlanner.Table3x3CommandId);
            },
            MergeTableCells = () => editor.TryMergeActiveTableCell(),
            SplitTableCell = () => editor.TrySplitActiveTableCell(),
            PickPictureBullet = () => ApplyPictureBullet(editor, getSlideCanvas?.Invoke(), pickPictureBulletPayload),
            InsertSlideZoom = onInsertSlideZoom,
            InsertSectionZoom = onInsertSectionZoom,
            InsertSummaryZoom = onInsertSummaryZoom,
            EditZoomTarget = onEditZoomTarget,
            EditSummaryZoomTargets = onEditSummaryZoomTargets,
            FormatZoom = onFormatZoom,
            SetZoomCoverImage = onSetZoomCoverImage,
            ResetZoomCoverImage = onResetZoomCoverImage,
            OpenHeaderFooter = focus => ExecuteHeaderFooter(editor, focus, onHeaderFooter),
            DesignRequest = request => ExecuteDesignRequest(request, onCustomSlideSize, onLayoutPicker),
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

    private static bool TryHandleTextAction(FreePRibbonTextAction action, SlideCanvas? canvas)
    {
        if (canvas is null)
            return false;

        return action.Kind switch
        {
            FreePRibbonTextActionKind.ToggleFormat =>
                RouteTextFormat(canvas, (TableCellTextFormatKind)action.Argument!),
            FreePRibbonTextActionKind.SetParagraphAlignment =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphAlignment((TextAlign)action.Argument!) == true,
            FreePRibbonTextActionKind.ApplyListPreset =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphListPreset((TableCellListPresetDescriptor)action.Argument!) == true,
            FreePRibbonTextActionKind.ToggleBullets =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphBulletToggle() == true,
            FreePRibbonTextActionKind.ToggleNumbering =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true,
            FreePRibbonTextActionKind.Indent =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphIndent() == true,
            FreePRibbonTextActionKind.Outdent =>
                canvas.TextEditor?.TryApplyActiveShapeParagraphOutdent() == true,
            FreePRibbonTextActionKind.SetFontFamily =>
                RouteToActiveRichEditor(canvas, editor => editor.ApplyFont((string)action.Argument!), editor => editor.ApplyFont((string)action.Argument!)),
            FreePRibbonTextActionKind.SetFontSize =>
                RouteToActiveRichEditor(canvas, editor => editor.ApplyFontSize((double)action.Argument!), editor => editor.ApplyFontSize((double)action.Argument!)),
            FreePRibbonTextActionKind.SetColor =>
                RouteToActiveRichEditor(canvas, editor => editor.ApplyColor((ThemeAwareColor?)action.Argument), editor => editor.ApplyColor((ThemeAwareColor?)action.Argument)),
            FreePRibbonTextActionKind.RemoveHyperlink =>
                canvas.TextEditor?.TryApplySelectedShapeRunHyperlink(null) == true,
            _ => false,
        };
    }

    private static bool RouteTextFormat(SlideCanvas canvas, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => RouteToActiveRichEditor(canvas, static editor => editor.ApplyBold(), static editor => editor.ApplyBold()),
        TableCellTextFormatKind.Italic => RouteToActiveRichEditor(canvas, static editor => editor.ApplyItalic(), static editor => editor.ApplyItalic()),
        TableCellTextFormatKind.Underline => RouteToActiveRichEditor(canvas, static editor => editor.ApplyUnderline(), static editor => editor.ApplyUnderline()),
        TableCellTextFormatKind.Superscript => RouteToActiveRichEditor(canvas, static editor => editor.ApplySuperscript(), static editor => editor.ApplySuperscript()),
        TableCellTextFormatKind.Subscript => RouteToActiveRichEditor(canvas, static editor => editor.ApplySubscript(), static editor => editor.ApplySubscript()),
        _ => false,
    };

    private static bool RouteToActiveRichEditor(
        SlideCanvas canvas,
        Action<InCanvasTextEditor> shapeAction,
        Action<InCanvasTableCellEditor> tableAction)
    {
        if (canvas.TextEditor?.IsActive == true)
        {
            shapeAction(canvas.TextEditor);
            return true;
        }

        if (canvas.TableCellEditor?.IsCellRichEditActive == true)
        {
            tableAction(canvas.TableCellEditor);
            return true;
        }

        return false;
    }

    private static void ExecuteHeaderFooter(
        EditingSession editor,
        HeaderFooterCommandFocus focus,
        Action<HeaderFooterCommandFocus>? onHeaderFooter)
    {
        if (onHeaderFooter is not null)
        {
            onHeaderFooter(focus);
            return;
        }

        var state = HeaderFooterCommandPlanner.BuildState(editor);
        HeaderFooterCommandPlanner.TryApply(
            editor,
            HeaderFooterCommandPlanner.BuildDefaultOptions(state, focus),
            out _);
    }

    private static void ExecuteDesignRequest(
        PresentationDesignCommandPlan plan,
        Action? onCustomSlideSize,
        Action? onLayoutPicker)
    {
        if (plan.Intent == PresentationDesignCommandIntentKind.RequestCustomSlideSize)
            onCustomSlideSize?.Invoke();
        else if (plan.Intent == PresentationDesignCommandIntentKind.RequestLayoutPicker)
            onLayoutPicker?.Invoke();
    }

    private static void ApplyPicture(EditingSession editor)
    {
        var payload = TryPickPicturePayload();
        if (payload is null)
            return;

        var plan = SlideObjectInsertionPlanner.BuiltInPlans.Single(item => item.CommandId == SlideObjectInsertionPlanner.PictureCommandId);
        SlideObjectInsertionPlanner.Apply(editor, plan, payload);
    }

    private static void ApplyMedia(EditingSession editor, bool isVideo)
    {
        var payload = TryPickMediaPayload(isVideo);
        if (payload is null)
            return;

        var commandId = isVideo ? SlideObjectInsertionPlanner.VideoCommandId : SlideObjectInsertionPlanner.AudioCommandId;
        var plan = SlideObjectInsertionPlanner.BuiltInPlans.Single(item => item.CommandId == commandId);
        SlideObjectInsertionPlanner.Apply(editor, plan, mediaPayload: payload);
    }

    private static void ApplyBuiltInInsertion(EditingSession editor, string commandId)
    {
        var plan = SlideObjectInsertionPlanner.BuiltInPlans.Single(item => item.CommandId == commandId);
        SlideObjectInsertionPlanner.Apply(editor, plan);
    }

    private static void ApplyPictureBullet(
        EditingSession editor,
        SlideCanvas? canvas,
        Func<PresentationPictureBulletPayload?>? pickPictureBulletPayload)
    {
        var payload = (pickPictureBulletPayload ?? TryPickPictureBulletPayload)();
        if (payload is null ||
            canvas?.TextEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true)
            return;

        editor.TryApplyActiveTableCellParagraphPictureBullet(payload);
    }

    private static SlideObjectPicturePayload? TryPickPicturePayload()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg;*.wmf;*.emf|All files|*.*",
            title: "Insert Picture");
        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return null;

        try
        {
            return SlideObjectInsertionPlanner.CreatePicturePayload(File.ReadAllBytes(result.FileName), result.FileName);
        }
        catch
        {
            return null;
        }
    }

    private static SlideObjectMediaPayload? TryPickMediaPayload(bool isVideo)
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: isVideo
                ? $"{PresentationFileTextResources.VideoFileTypeName}|*.mp4;*.mov;*.avi;*.wmv;*.m4v|All files|*.*"
                : $"{PresentationFileTextResources.AudioFileTypeName}|*.mp3;*.m4a;*.wav;*.wma|All files|*.*",
            title: isVideo
                ? PresentationFileTextResources.InsertVideoPickerTitle
                : PresentationFileTextResources.InsertAudioPickerTitle);
        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return null;

        try
        {
            return SlideObjectInsertionPlanner.CreateMediaPayload(
                File.ReadAllBytes(result.FileName),
                result.FileName,
                isVideo);
        }
        catch
        {
            return null;
        }
    }

    private static PresentationPictureBulletPayload? TryPickPictureBulletPayload()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: null,
            filter: "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg|All files|*.*",
            title: "Choose Picture Bullet");
        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return null;

        try
        {
            return PresentationPictureBulletAuthoringPlanner.CreatePayloadFromFileName(
                File.ReadAllBytes(result.FileName),
                result.FileName);
        }
        catch
        {
            return null;
        }
    }
}
