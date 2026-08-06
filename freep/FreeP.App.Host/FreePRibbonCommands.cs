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
        var host = new FreePRibbonCommandHostAdapter
        {
            ExecuteAction = action => ExecuteHostAction(
                action,
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
                onSlideShowSettings),
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

    private static void ExecuteHostAction(
        FreePRibbonHostAction action,
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
        Action? onSlideShowSettings)
    {
        switch (action.Kind)
        {
            case FreePRibbonHostActionKind.Copy:
                WpfClipboardCommands.Copy(editor, osClipboard);
                break;
            case FreePRibbonHostActionKind.Cut:
                WpfClipboardCommands.Cut(editor, osClipboard);
                break;
            case FreePRibbonHostActionKind.Paste:
                if (osClipboard is not null)
                    osClipboard.Paste(editor, preferOsClipboard: true);
                else
                    editor.Paste();
                break;
            case FreePRibbonHostActionKind.InsertPicture:
                ApplyPicture(editor);
                break;
            case FreePRibbonHostActionKind.InsertVideo:
                ApplyMedia(editor, isVideo: true);
                break;
            case FreePRibbonHostActionKind.InsertAudio:
                ApplyMedia(editor, isVideo: false);
                break;
            case FreePRibbonHostActionKind.OpenTablePicker:
                if (onTablePicker is not null)
                    onTablePicker();
                else
                    ApplyBuiltInInsertion(editor, SlideObjectInsertionPlanner.Table3x3CommandId);
                break;
            case FreePRibbonHostActionKind.MergeTableCells:
                editor.TryMergeActiveTableCell();
                break;
            case FreePRibbonHostActionKind.SplitTableCell:
                editor.TrySplitActiveTableCell();
                break;
            case FreePRibbonHostActionKind.PickPictureBullet:
                ApplyPictureBullet(editor, getSlideCanvas?.Invoke(), pickPictureBulletPayload);
                break;
            case FreePRibbonHostActionKind.InsertSlideZoom: onInsertSlideZoom?.Invoke(); break;
            case FreePRibbonHostActionKind.InsertSectionZoom: onInsertSectionZoom?.Invoke(); break;
            case FreePRibbonHostActionKind.InsertSummaryZoom: onInsertSummaryZoom?.Invoke(); break;
            case FreePRibbonHostActionKind.EditZoomTarget: onEditZoomTarget?.Invoke(); break;
            case FreePRibbonHostActionKind.EditSummaryZoomTargets: onEditSummaryZoomTargets?.Invoke(); break;
            case FreePRibbonHostActionKind.FormatZoom: onFormatZoom?.Invoke(); break;
            case FreePRibbonHostActionKind.SetZoomCoverImage: onSetZoomCoverImage?.Invoke(); break;
            case FreePRibbonHostActionKind.ResetZoomCoverImage: onResetZoomCoverImage?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenHeaderFooter:
                ExecuteHeaderFooter(editor, (HeaderFooterCommandFocus)action.Argument!, onHeaderFooter);
                break;
            case FreePRibbonHostActionKind.DesignRequest:
                ExecuteDesignRequest((PresentationDesignCommandPlan)action.Argument!, onCustomSlideSize, onLayoutPicker);
                break;
            case FreePRibbonHostActionKind.ApplySmartArtColor:
                onSmartArtColorPreset?.Invoke((SmartArtColorPreset)action.Argument!);
                break;
            case FreePRibbonHostActionKind.ApplySmartArtLayout:
                onSmartArtLayoutPreset?.Invoke((SmartArtLayoutPreset)action.Argument!);
                break;
            case FreePRibbonHostActionKind.ApplySmartArtQuickStyle:
                onSmartArtQuickStylePreset?.Invoke((SmartArtQuickStylePreset)action.Argument!);
                break;
            case FreePRibbonHostActionKind.ConvertSmartArtToShapes: onConvertSmartArtToShapes?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenSmartArtTextPane: onOpenSmartArtTextPane?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartData: onEditChartData?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartDisplayOptions: onEditChartOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartAxisOptions: onEditChartAxisOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartSeriesOptions: onEditChartSeriesOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartPointOptions: onEditChartPointOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartLayoutOptions: onEditChartLayoutOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartExSeriesLayout: onEditChartExSeriesLayout?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartDataTableOptions: onEditChartDataTableOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartBubbleOptions: onEditChartBubbleOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartPieOptions: onEditChartPieOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartPlotStyleOptions: onEditChartPlotStyleOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChart3DViewOptions: onEditChart3DViewOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartTextOptions: onEditChartTextOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartAreaOptions: onEditChartAreaOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenChartProtectionOptions: onEditChartProtectionOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenHyperlink: onInsertLink?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenRotationOptions: onEditRotationOptions?.Invoke(); break;
            case FreePRibbonHostActionKind.SetEditPointsEnabled:
                if (setEditPointsEnabled is not null)
                    setEditPointsEnabled((bool)action.Argument!);
                else
                    onEditPoints?.Invoke();
                break;
            case FreePRibbonHostActionKind.OpenFind: onFind?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenReplace: onFindReplace?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowCommentsPane: onReviewCommentsPane?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowAccessibilityPane: onReviewAccessibility?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowAltTextPane: onReviewAltText?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowReadingOrderPane: onReviewReadingOrder?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowSelectionPane: onSelectionPane?.Invoke(); break;
            case FreePRibbonHostActionKind.ShowProofingPane: onReviewProofing?.Invoke(); break;
            case FreePRibbonHostActionKind.AddComment: onAddComment?.Invoke(); break;
            case FreePRibbonHostActionKind.EditComment: onEditComment?.Invoke(); break;
            case FreePRibbonHostActionKind.ReplyComment: onReplyComment?.Invoke(); break;
            case FreePRibbonHostActionKind.DeleteComment: onDeleteComment?.Invoke(); break;
            case FreePRibbonHostActionKind.PreviousComment: onPreviousComment?.Invoke(); break;
            case FreePRibbonHostActionKind.NextComment: onNextComment?.Invoke(); break;
            case FreePRibbonHostActionKind.ResolveComment: onResolveComment?.Invoke(); break;
            case FreePRibbonHostActionKind.ReopenComment: onReopenComment?.Invoke(); break;
            case FreePRibbonHostActionKind.ApplyViewShowState:
                applyViewShowState?.Invoke((PresentationViewShowState)action.Argument!);
                break;
            case FreePRibbonHostActionKind.ApplyViewZoomState:
                applyViewZoomState?.Invoke((PresentationViewZoomState)action.Argument!);
                break;
            case FreePRibbonHostActionKind.PickTransitionSound: onTransitionSound?.Invoke(); break;
            case FreePRibbonHostActionKind.ToggleAnimationPane: onAnimPane?.Invoke(); break;
            case FreePRibbonHostActionKind.StartSlideShowFromBeginning: onStartFromStart?.Invoke(); break;
            case FreePRibbonHostActionKind.StartSlideShowFromCurrent: onStartFromCurrent?.Invoke(); break;
            case FreePRibbonHostActionKind.RehearseTimings: onRehearseTimings?.Invoke(); break;
            case FreePRibbonHostActionKind.RecordTimings: onRecordTimings?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenCustomShows: onCustomShows?.Invoke(); break;
            case FreePRibbonHostActionKind.OpenSlideShowSettings: onSlideShowSettings?.Invoke(); break;
        }
    }

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
