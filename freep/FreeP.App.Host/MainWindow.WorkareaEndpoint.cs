using FreeP.App.Compositor;

namespace FreeP.App.Host;

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
                HideTransientPickers = HideTransientPickers,
                MarkDirty = () => _file.MarkDirty(),
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
                ClearMediaSelection = () => _mediaPaneSession.ClearCaptionSelection(),
                SyncSlidePaneSelection = SyncSlidePaneSelection,
                RefreshSlidePaneChrome = RefreshSlidePaneChrome,
                RefreshReviewPaneBeforePlans = RefreshCommentPane,
                RefreshVisibleMediaPane = RefreshVisibleMediaCaptionPaneFromFields,
                RefreshAltTextRequest = RefreshAltTextRequestPlan,
                RefreshReadingOrder = () => _ = _reviewWorkflowSession.RefreshReadingOrderPlan(),
                RefreshAltTextPane = ShowAltTextPane,
            },
            NativeCommands = new PresentationWorkareaNativeCommandEndpoints
            {
                NewPresentation = () => _file.New(),
                OpenPresentation = () => _file.Open(),
                SavePresentation = () => _file.Save(),
                SavePresentationAs = () => _file.SaveAs(),
                PrintPresentation = ShowPrintBackstage,
                StartSlideShowFromBeginning = () => StartSlideShow(true),
                StartSlideShowFromCurrentSlide = () => StartSlideShow(false),
                Copy = () => WpfClipboardCommands.Copy(Editor, _osClipboard),
                Cut = () => WpfClipboardCommands.Cut(Editor, _osClipboard),
                Paste = () => _osClipboard.Paste(Editor, preferOsClipboard: true),
                Find = OpenFindDialog,
                Replace = OpenFindReplaceDialog,
            },
        });

    private void BindWorkareaEditor(EditingSession editor)
    {
        _selectionPane?.SetEditor(editor);
        if (SlideCanvas is not null)
            AttachCanvasEditing();
    }

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
