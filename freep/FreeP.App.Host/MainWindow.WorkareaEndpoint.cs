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
                ResetAnimationSession = _animationPaneSession.Reset,
                HideTransientPickers = HideTransientPickers,
                MarkDirty = () => _fileSession.MarkDirty(),
                RefreshCommandStates = SyncRibbonCommandStates,
                RefreshContextualTabs = RefreshContextualTabs,
                RefreshSlidePane = RefreshSlidePane,
                RefreshCanvas = RefreshCanvas,
                RefreshNotesPane = RefreshNotesPane,
                RefreshDocumentStatusBeforeReview = transition =>
                    ApplyStatusRefreshPlan(PresentationWorkareaStatusRefreshPlanner.BuildBeforeReview(transition)),
                RefreshReviewWorkflowPlans = RefreshReviewWorkflowPlans,
                RefreshSmartArtPane = () => ShowSmartArtTextPane(),
                RefreshAnimationPaneAfterEditorChanged = RebuildAnimationPaneIfVisible,
                RefreshAnimationPaneAfterNavigation = RebuildAnimationPaneIfVisible,
                RefreshAnimationPaneAfterSelection = RebuildAnimationPaneIfVisible,
                RefreshAnimationPaneAfterPresentationChanged = RebuildAnimationPaneIfVisible,
                RefreshSelectionPane = () => _selectionPane?.Refresh(),
                RefreshAccessibilityMetadata = RefreshPaneAccessibilityMetadata,
                RefreshDocumentStatusAfterReview = transition =>
                    ApplyStatusRefreshPlan(PresentationWorkareaStatusRefreshPlanner.BuildAfterReview(transition)),
                ClearReviewSelection = () => _reviewWorkflowSession.SelectedCommentIndex = null,
                ResetAnimationSelection = _animationPaneSession.ResetSelection,
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
                Copy = () => _osClipboard.Copy(
                    Editor,
                    error => ReportClipboardWriteFailure(
                        PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand),
                        error)),
                Cut = () => _osClipboard.Cut(
                    Editor,
                    error => ReportClipboardWriteFailure(
                        PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand),
                        error)),
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

    private void SyncRibbonCommandStates()
    {
        _ribbonBindingSession?.SyncCommandStates();
        RefreshContextualTabs();
    }

    private void RefreshSlidePane()
    {
        if (_viewModeState.Mode == PresentationViewMode.Outline)
            _outlinePane?.RefreshProjection();
        else
            _slidePane?.RefreshProjection();
    }

    private void SyncSlidePaneSelection()
    {
        if (_viewModeState.Mode == PresentationViewMode.Outline)
            _outlinePane?.SyncNativeSelection();
        else
            _slidePane?.SyncNativeSelection();
    }

    private void RefreshSlidePaneChrome()
    {
        if (_viewModeState.Mode == PresentationViewMode.Outline)
            _outlinePane?.SyncNativeSelection(scrollActiveIntoView: false);
        else
            _slidePane?.RefreshItemChrome();
    }

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
