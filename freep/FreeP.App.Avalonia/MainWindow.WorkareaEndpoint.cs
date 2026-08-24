using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private IPresentationWorkareaEndpoint CreateWorkareaEndpoint() =>
        new PresentationWorkareaEndpoint(new PresentationWorkareaEndpointProfile
        {
            Operations = new PresentationWorkareaOperationEndpoints
            {
                BeforePresentationReplaced = () => _findReplaceDialog?.Close(),
                BindEditor = BindWorkareaEditor,
                ResetAnimationSession = () => _animationPaneSession.Reset(),
                HideTransientPickers = HideTransientPickers,
                BeforeEditorChanged = () =>
                    RecordStartupObservation("editor-changed-before-mark"),
                MarkDirty = () => _fileWorkflow.MarkDirty(),
                AfterEditorMarkedDirty = () =>
                    RecordStartupObservation("editor-changed"),
                RefreshCommandStates = SyncRibbonCommandStates,
                RefreshContextualTabs = RefreshContextualTabs,
                RefreshSlidePane = RefreshSlidePane,
                RefreshCanvas = RefreshCanvas,
                RefreshNotesPane = RefreshNotesPane,
                RefreshReviewWorkflowPlans = RefreshReviewWorkflowPlans,
                RefreshSmartArtPane = () => ShowSmartArtTextPane(),
                RefreshAnimationPaneAfterEditorChanged = () =>
                    RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex),
                RefreshAnimationPaneAfterNavigation = () => RefreshVisibleAnimationPane(),
                RefreshAnimationPaneAfterSelection = () => RefreshVisibleAnimationPane(),
                RefreshAnimationPaneAfterPresentationChanged = () => RefreshVisibleAnimationPane(),
                RefreshSelectionPane = () => _selectionPane?.Refresh(),
                RefreshAccessibilityMetadata = RefreshPaneAccessibilityMetadata,
                RefreshDocumentStatusAfterReview = _ => UpdateStatus(),
                BeforeCurrentSlideChanged = PrepareCurrentSlideChange,
                ClearReviewSelection = () => _reviewWorkflowSession.SelectedCommentIndex = null,
                ResetAnimationSelection = () => _animationPaneSession.ResetSelection(),
                ClearMediaSelection = () => _mediaPaneHostCoordinator.SelectCaptionTrack(null),
                SyncSlidePaneSelection = SyncSlidePaneSelectionFromEditor,
                RefreshSlidePaneChrome = UpdateSlidePaneItemChrome,
                RefreshReviewPaneAfterPlans = RefreshVisibleReviewCommentsPane,
                RefreshVisibleMediaPane = RefreshVisibleMediaCaptionPaneFromFields,
                RefreshCurrentSlideStatus = UpdateStatus,
                RefreshAltTextRequest = RefreshAltTextRequestPlan,
                RefreshReadingOrder = () => _ = _reviewWorkflowSession.RefreshReadingOrderPlan(),
                RefreshAltTextPane = ShowAltTextPane,
            },
            NativeCommands = new PresentationWorkareaNativeCommandEndpoints
            {
                NewPresentation = FileNew,
                OpenPresentation = () => _ = FileOpenAsync(),
                SavePresentation = () => _ = FileSaveAsync(),
                SavePresentationAs = () => _ = FileSaveAsAsync(),
                PrintPresentation = ShowPrintBackstage,
                StartSlideShowFromBeginning = () => StartSlideShow(true),
                StartSlideShowFromCurrentSlide = () => StartSlideShow(false),
                Copy = QueueClipboardCopy,
                Cut = QueueClipboardCut,
                Paste = QueueClipboardPaste,
                Find = OpenFindDialog,
                Replace = OpenFindReplaceDialog,
            },
        });

    private void HideTransientPickers()
    {
        HideLayoutPicker();
        HideTablePicker();
    }

    private void PrepareCurrentSlideChange() =>
        RecordStartupObservation("current-slide-changed");

    private void BindWorkareaEditor(EditingSession editor)
    {
        _ribbonBindingSession?.Rebind(editor);
        _selectionPane?.SetEditor(editor);
        if (_adorner is not null)
            RewireInteractionToEditor();
    }
}
