using FreeP.App.Compositor;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

public sealed partial class MainWindow
{
    private FreePRibbonActionPortProfile? _ribbonActionPortProfile;

    private FreePRibbonActionPortProfile GetRibbonActionPortProfile() =>
        _ribbonActionPortProfile ??= FreePRibbonActionPortProfileFactory.Create(
            new FreePRibbonHostActionEndpoints
            {
#if FREEP_WPF_RENDERER
                Copy = CopyRibbonSelection,
                Cut = CutRibbonSelection,
                Paste = () => _osClipboard.Paste(Editor, preferOsClipboard: true),
                InsertPicture = () => QueueAssetImport(PresentationAssetImportKind.Picture),
                InsertVideo = () => QueueAssetImport(PresentationAssetImportKind.Video),
                InsertAudio = () => QueueAssetImport(PresentationAssetImportKind.Audio),
#else
                Copy = QueueClipboardCopy,
                Cut = QueueClipboardCut,
                Paste = QueueClipboardPaste,
                InsertPicture = () => _ = InsertPictureFromFileAsync(),
                InsertVideo = () => _ = InsertMediaFromFileAsync(isVideo: true),
                InsertAudio = () => _ = InsertMediaFromFileAsync(isVideo: false),
#endif
                OpenTablePicker = OpenTablePicker,
                ExecuteTableStructureAction = kind =>
                    _domainContextMenuSession.ExecuteCurrentTableAction(kind, TryExecuteInlineTableAction),
                MergeTableCells = () =>
                {
                    _domainContextMenuSession.ExecuteCurrentTableAction(
                        PresentationDomainContextActionKind.MergeTableCell,
                        TryExecuteInlineTableAction);
                },
                SplitTableCell = () =>
                {
                    _domainContextMenuSession.ExecuteCurrentTableAction(
                        PresentationDomainContextActionKind.SplitTableCell,
                        TryExecuteInlineTableAction);
                },
#if FREEP_WPF_RENDERER
                PickPictureBullet = () => QueueAssetImport(PresentationAssetImportKind.PictureBullet),
                InsertSlideZoom = OpenSlideZoomDialog,
                InsertSectionZoom = OpenSectionZoomDialog,
                InsertSummaryZoom = OpenSummaryZoomDialog,
                EditZoomTarget = OpenZoomTargetDialog,
                EditSummaryZoomTargets = OpenSummaryZoomTargetsDialog,
                FormatZoom = OpenZoomObjectPropertiesDialog,
                SetZoomCoverImage = OpenZoomCoverImagePicker,
                ResetZoomCoverImage = RestoreZoomPreview,
#else
                PickPictureBullet = () => _ = ApplyPictureBulletFromFileAsync(),
                InsertSlideZoom = () => _ = OpenSlideZoomDialogAsync(),
                InsertSectionZoom = () => _ = OpenSectionZoomDialogAsync(),
                InsertSummaryZoom = () => _ = OpenSummaryZoomDialogAsync(),
                EditZoomTarget = () => _ = OpenZoomTargetDialogAsync(),
                EditSummaryZoomTargets = () => _ = OpenSummaryZoomTargetsDialogAsync(),
                FormatZoom = () => _ = OpenZoomObjectPropertiesDialogAsync(),
                SetZoomCoverImage = () => _ = OpenZoomCoverImagePickerAsync(),
                ResetZoomCoverImage = () => _ = RestoreZoomPreviewAsync(),
#endif
                OpenHeaderFooter = OpenHeaderFooterDialog,
                ApplySmartArtColor = preset => ApplySmartArtColorPreset(preset),
                ApplySmartArtLayout = preset => ApplySmartArtLayoutPreset(preset),
                ApplySmartArtQuickStyle = preset => ApplySmartArtQuickStylePreset(preset),
                ConvertSmartArtToShapes = () => ConvertSelectedSmartArtToShapes(),
                OpenSmartArtTextPane = () => ShowSmartArtTextPane(),
                OpenChartData = OpenChartDataDialog,
                OpenChartDisplayOptions = OpenChartDisplayOptionsDialog,
                OpenChartAxisOptions = () => OpenChartAxisOptionsDialog(),
                OpenChartSeriesOptions = () => OpenChartSeriesOptionsDialog(),
                OpenChartPointOptions = () => OpenChartPointOptionsDialog(),
                OpenChartLayoutOptions = OpenChartLayoutOptionsDialog,
                OpenChartExSeriesLayout = OpenChartExSeriesLayoutDialog,
                OpenChartDataTableOptions = OpenChartDataTableOptionsDialog,
                OpenChartBubbleOptions = OpenChartBubbleOptionsDialog,
                OpenChartPieOptions = OpenChartPieOptionsDialog,
                OpenChartPlotStyleOptions = OpenChartPlotStyleOptionsDialog,
                OpenChart3DViewOptions = OpenChart3DViewOptionsDialog,
                OpenChartTextOptions = OpenChartTextOptionsDialog,
                OpenChartAreaOptions = OpenChartAreaOptionsDialog,
                OpenChartProtectionOptions = OpenChartProtectionOptionsDialog,
                OpenHyperlink = OpenHyperlinkDialog,
                OpenRotationOptions = OpenRotationOptionsDialog,
#if FREEP_WPF_RENDERER
                SetEditPointsEnabled = enabled => SlideCanvas?.SetEditPointsMode(enabled),
#else
                SetEditPointsEnabled = _slideCanvas.SetEditPointsMode,
#endif
                OpenFind = OpenFindDialog,
                OpenReplace = OpenFindReplaceDialog,
                ShowCommentsPane = () => ShowReviewCommentsPane(),
                ShowAccessibilityPane = () => ShowAccessibilityCheckerPane(),
                ShowAltTextPane = () => ShowAltTextPane(),
                ShowReadingOrderPane = () => ShowReadingOrderPane(),
                ShowSelectionPane = () => ShowSelectionPane(),
                ShowProofingPane = () => ShowProofingPane(),
                AddComment = () => AddComment(PresentationPaneTextResources.NewCommentDefault),
                EditComment = () => EditSelectedComment(GetSelectedCommentText()),
                ReplyComment = () => ReplyToSelectedComment(PresentationPaneTextResources.NewReplyDefault),
                DeleteComment = () => DeleteSelectedComment(),
                PreviousComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment),
                NextComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment),
                ResolveComment = () => ResolveSelectedComment(),
                ReopenComment = () => ReopenSelectedComment(),
                ApplyViewShowState = ApplyPresentationViewShowState,
                ApplyViewZoomState = ApplyPresentationViewZoomState,
                ApplyViewModeState = ApplyPresentationViewModeState,
                StartReadingView = StartReadingView,
                NewPresentationWindow = OpenNewPresentationWindow,
                ArrangeAllPresentationWindows = ArrangeAllPresentationWindows,
#if FREEP_WPF_RENDERER
                PickTransitionSound = PickTransitionSound,
                ToggleAnimationPane = _ => ToggleAnimationPane(),
#else
                PickTransitionSound = () => _ = PickTransitionSoundAsync(),
                ToggleAnimationPane = OnAnimationPaneRequested,
#endif
                StartSlideShowFromBeginning = () => StartSlideShow(fromStart: true),
                StartSlideShowFromCurrent = () => StartSlideShow(fromStart: false),
                RehearseTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RehearseTimings),
                RecordTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RecordTimings),
                OpenCustomShows = OpenCustomShowDialog,
                OpenSlideShowSettings = OpenSlideShowSettingsDialog,
            });
}
