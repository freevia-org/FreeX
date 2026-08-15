using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    private IPresentationWorkareaEndpoint CreateWorkareaEndpoint() =>
        new PresentationWorkareaEndpoint(new PresentationWorkareaEndpointProfile
        {
            Operations = new PresentationWorkareaOperationEndpoints
            {
                BeforePresentationReplaced = () => _findReplaceDialog?.Close(),
                BindEditor = BindWorkareaEditor,
                HideTransientPickers = HideTransientPickers,
                MarkDirty = () => _fileSession.MarkDirty(),
                RefreshCommandStates = SyncRibbonCommandStates,
                RefreshSlidePane = RefreshSlidePane,
                RefreshCanvas = RefreshCanvas,
                RefreshNotesPane = RefreshNotesPane,
                RefreshDocumentStatusBeforeReview = transition =>
                    ApplyStatusRefreshPlan(PresentationWorkareaStatusRefreshPlanner.BuildBeforeReview(transition)),
                RefreshReviewWorkflowPlans = RefreshReviewWorkflowPlans,
                RefreshSmartArtPane = () => ShowSmartArtTextPane(),
                RefreshAnimationPaneAfterPresentationChanged = RebuildAnimationPaneIfVisible,
                RefreshSelectionPane = () => _selectionPane?.Refresh(),
                RefreshAccessibilityMetadata = RefreshPaneAccessibilityMetadata,
                RefreshDocumentStatusAfterReview = transition =>
                    ApplyStatusRefreshPlan(PresentationWorkareaStatusRefreshPlanner.BuildAfterReview(transition)),
                ClearReviewSelection = () => _reviewWorkflowSession.SelectedCommentIndex = null,
                ClearMediaSelection = () => _mediaPaneHostCoordinator.SelectCaptionTrack(null),
                SyncSlidePaneSelection = SyncSlidePaneSelection,
                RefreshSlidePaneChrome = RefreshSlidePaneChrome,
                RefreshReviewPaneBeforePlans = RefreshCommentPane,
                RefreshVisibleMediaPane = RefreshVisibleMediaCaptionPaneFromFields,
                RefreshCurrentSlideStatus = UpdateSlideCount,
                RefreshAltTextRequest = RefreshAltTextRequestPlan,
                RefreshReadingOrder = () => _ = _reviewWorkflowSession.RefreshReadingOrderPlan(),
                RefreshAltTextPane = ShowAltTextPane,
            },
            NativeCommands = new PresentationWorkareaNativeCommandEndpoints
            {
                NewPresentation = () => FileNew(),
                OpenPresentation = () => FileOpen(),
                SavePresentation = () => FileSave(),
                SavePresentationAs = () => FileSaveAs(),
                PrintPresentation = ShowPrintBackstage,
                StartSlideShowFromBeginning = () => StartSlideShow(true),
                StartSlideShowFromCurrentSlide = () => StartSlideShow(false),
                Copy = () => _osClipboard.Copy(Editor),
                Cut = () => _osClipboard.Cut(Editor),
                Paste = () => _osClipboard.Paste(Editor, preferOsClipboard: true),
                Find = OpenFindDialog,
                Replace = OpenFindReplaceDialog,
            },
        });

    private void BindWorkareaEditor(EditingSession editor)
    {
        _ribbonBindingSession?.Rebind(editor);
        _selectionPane?.SetEditor(editor);
        if (SlideCanvas is not null)
            AttachCanvasEditing();
    }

    private void SyncRibbonCommandStates() =>
        _ribbonBindingSession?.SyncCommandStates();

    private void RefreshSlidePane() =>
        (SlidePaneHost?.Child as SlidePane)?.RefreshProjection();

    private void SyncSlidePaneSelection() =>
        (SlidePaneHost?.Child as SlidePane)?.SyncNativeSelection();

    private void RefreshSlidePaneChrome() =>
        (SlidePaneHost?.Child as SlidePane)?.RefreshItemChrome();

    private void HideTransientPickers()
    {
        HideLayoutPicker();
        HideTablePicker();
    }

    private void ApplyStatusRefreshPlan(PresentationWorkareaStatusRefreshPlan plan)
    {
        if (plan.RefreshSlideCount)
            UpdateSlideCount();
        if (plan.RefreshTitle)
            UpdateTitle();
    }
}
