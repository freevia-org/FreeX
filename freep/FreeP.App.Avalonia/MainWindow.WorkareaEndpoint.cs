using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private IPresentationWorkareaEndpoint CreateWorkareaEndpoint() =>
        new PresentationWorkareaEndpoint(new PresentationWorkareaEndpointProfile
        {
            Panes = new PresentationWorkareaPaneEndpoints
            {
                AltTextVisible = () => IsAltTextPaneVisible,
                SmartArtTextVisible = () => IsSmartArtTextPaneVisible,
            },
            Operations = new PresentationWorkareaOperationEndpoints
            {
                BeforePresentationReplaced = () => _findReplaceDialog?.Close(),
                BindEditor = BindWorkareaEditor,
                ResetAnimationSession = () => _animationPaneSession.Reset(),
                HideTransientPickers = HideTransientPickers,
                BeforeEditorChanged = () =>
                    _startupDirtyTrace?.Record("editor-changed-before-mark", _fileWorkflow),
                MarkDirty = () => _fileWorkflow.MarkDirty(),
                AfterEditorMarkedDirty = () =>
                    _startupDirtyTrace?.Record("editor-changed", _fileWorkflow),
                RefreshCommandStates = SyncRibbonCommandStates,
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
                ClearMediaSelection = () => _mediaPaneSession.ClearCaptionSelection(),
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
        _startupDirtyTrace?.Record("current-slide-changed", _fileWorkflow);

    private void BindWorkareaEditor(EditingSession editor)
    {
        if (_ribbonCommandRegistry is not null)
        {
            FreePRibbonHostRegistryComposer.BindInto(
                _ribbonCommandRegistry,
                editor,
                _ribbonStateStore,
                CreateRibbonHostProfile());
        }
        _selectionPane?.SetEditor(editor);
        if (_adorner is not null)
            RewireInteractionToEditor();
    }
}
