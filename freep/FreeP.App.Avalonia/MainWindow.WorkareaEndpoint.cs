using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    bool IPresentationWorkareaEndpoint.IsPaneVisible(PresentationWorkareaPane pane) => pane switch
    {
        PresentationWorkareaPane.AltText => IsAltTextPaneVisible,
        PresentationWorkareaPane.SmartArtText => IsSmartArtTextPaneVisible,
        _ => false,
    };

    void IPresentationWorkareaEndpoint.Apply(
        PresentationWorkareaOperation operation,
        PresentationWorkareaContext context)
    {
        Action? action = operation switch
        {
            PresentationWorkareaOperation.BeforePresentationReplaced => () => _findReplaceDialog?.Close(),
            PresentationWorkareaOperation.BindEditor => () => BindWorkareaEditor(context.Snapshot.Editor),
            PresentationWorkareaOperation.ResetAnimationSession => _animationPaneSession.Reset,
            PresentationWorkareaOperation.HideTransientPickers => HideTransientPickers,
            PresentationWorkareaOperation.BeforeEditorChanged =>
                () => _startupDirtyTrace?.Record("editor-changed-before-mark", _fileWorkflow),
            PresentationWorkareaOperation.MarkDirty => _fileWorkflow.MarkDirty,
            PresentationWorkareaOperation.AfterEditorMarkedDirty =>
                () => _startupDirtyTrace?.Record("editor-changed", _fileWorkflow),
            PresentationWorkareaOperation.RefreshCommandStates => SyncRibbonCommandStates,
            PresentationWorkareaOperation.RefreshSlidePane => RefreshSlidePane,
            PresentationWorkareaOperation.RefreshCanvas => RefreshCanvas,
            PresentationWorkareaOperation.RefreshNotesPane => RefreshNotesPane,
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans => RefreshReviewWorkflowPlans,
            PresentationWorkareaOperation.RefreshSmartArtPane => () => ShowSmartArtTextPane(),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterEditorChanged =>
                () => RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterNavigation or
            PresentationWorkareaOperation.RefreshAnimationPaneAfterSelection or
            PresentationWorkareaOperation.RefreshAnimationPaneAfterPresentationChanged =>
                () => RefreshVisibleAnimationPane(),
            PresentationWorkareaOperation.RefreshSelectionPane => () => _selectionPane?.Refresh(),
            PresentationWorkareaOperation.RefreshAccessibilityMetadata => RefreshPaneAccessibilityMetadata,
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview or
            PresentationWorkareaOperation.RefreshCurrentSlideStatus => UpdateStatus,
            PresentationWorkareaOperation.BeforeCurrentSlideChanged =>
                () => PrepareCurrentSlideChange(context),
            PresentationWorkareaOperation.ClearReviewSelection =>
                () => _reviewWorkflowSession.SelectedCommentIndex = null,
            PresentationWorkareaOperation.ResetAnimationSelection => _animationPaneSession.ResetSelection,
            PresentationWorkareaOperation.ClearMediaSelection => _mediaPaneSession.ClearCaptionSelection,
            PresentationWorkareaOperation.SyncSlidePaneSelection => SyncSlidePaneSelectionFromEditor,
            PresentationWorkareaOperation.RefreshSlidePaneChrome => UpdateSlidePaneItemChrome,
            PresentationWorkareaOperation.RefreshReviewPaneAfterPlans => RefreshVisibleReviewCommentsPane,
            PresentationWorkareaOperation.RefreshVisibleMediaPane => RefreshVisibleMediaCaptionPaneFromFields,
            PresentationWorkareaOperation.RefreshAltTextRequest => RefreshAltTextRequestPlan,
            PresentationWorkareaOperation.RefreshReadingOrder =>
                () => _ = _reviewWorkflowSession.RefreshReadingOrderPlan(),
            PresentationWorkareaOperation.RefreshAltTextPane => ShowAltTextPane,
            _ => null,
        };
        action?.Invoke();
    }

    void IPresentationWorkareaEndpoint.ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
    {
        Action action = command switch
        {
            PresentationWorkareaNativeCommand.NewPresentation => FileNew,
            PresentationWorkareaNativeCommand.OpenPresentation => () => _ = FileOpenAsync(),
            PresentationWorkareaNativeCommand.SavePresentation => () => _ = FileSaveAsync(),
            PresentationWorkareaNativeCommand.SavePresentationAs => () => _ = FileSaveAsAsync(),
            PresentationWorkareaNativeCommand.PrintPresentation => ShowPrintBackstage,
            PresentationWorkareaNativeCommand.StartSlideShowFromBeginning => () => StartSlideShow(true),
            PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide => () => StartSlideShow(false),
            PresentationWorkareaNativeCommand.Copy => QueueClipboardCopy,
            PresentationWorkareaNativeCommand.Cut => QueueClipboardCut,
            PresentationWorkareaNativeCommand.Paste => QueueClipboardPaste,
            PresentationWorkareaNativeCommand.Find => OpenFindDialog,
            PresentationWorkareaNativeCommand.Replace => OpenFindReplaceDialog,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        action();
    }

    private void HideTransientPickers()
    {
        HideLayoutPicker();
        HideTablePicker();
    }

    private void PrepareCurrentSlideChange(PresentationWorkareaContext context)
    {
        _startupDirtyTrace?.Record("current-slide-changed", _fileWorkflow);
    }

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
