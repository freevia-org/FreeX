using FreeP.App.Compositor;

namespace FreeP.App.Host;

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
            PresentationWorkareaOperation.BindEditor => () => BindWorkareaEditor(context),
            PresentationWorkareaOperation.HideTransientPickers => HideTransientPickers,
            PresentationWorkareaOperation.MarkDirty => _file.MarkDirty,
            PresentationWorkareaOperation.RefreshCanvas => RefreshCanvas,
            PresentationWorkareaOperation.RefreshNotesPane => RefreshNotesPane,
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview =>
                () => ApplyStatusBeforeReview(context.Transition),
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans => RefreshReviewWorkflowPlans,
            PresentationWorkareaOperation.RefreshSmartArtPane => () => ShowSmartArtTextPane(),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterPresentationChanged =>
                RebuildAnimationPaneIfVisible,
            PresentationWorkareaOperation.RefreshSelectionPane => () => _selectionPane?.Refresh(),
            PresentationWorkareaOperation.RefreshAccessibilityMetadata => RefreshPaneAccessibilityMetadata,
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview
                when context.Transition == PresentationWorkareaTransition.Bootstrap => UpdateSlideCount,
            PresentationWorkareaOperation.ClearReviewSelection =>
                () => _reviewWorkflowSession.SelectedCommentIndex = null,
            PresentationWorkareaOperation.ClearMediaSelection => _mediaPaneSession.ClearCaptionSelection,
            PresentationWorkareaOperation.RefreshReviewPaneBeforePlans => RefreshCommentPane,
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
            PresentationWorkareaNativeCommand.NewPresentation => () => _file.New(),
            PresentationWorkareaNativeCommand.OpenPresentation => () => _file.Open(),
            PresentationWorkareaNativeCommand.SavePresentation => () => _file.Save(),
            PresentationWorkareaNativeCommand.SavePresentationAs => () => _file.SaveAs(),
            PresentationWorkareaNativeCommand.PrintPresentation => ShowPrintBackstage,
            PresentationWorkareaNativeCommand.StartSlideShowFromBeginning => () => StartSlideShow(true),
            PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide => () => StartSlideShow(false),
            PresentationWorkareaNativeCommand.Copy => () => WpfClipboardCommands.Copy(Editor, _osClipboard),
            PresentationWorkareaNativeCommand.Cut => () => WpfClipboardCommands.Cut(Editor, _osClipboard),
            PresentationWorkareaNativeCommand.Paste =>
                () => _osClipboard.Paste(Editor, preferOsClipboard: true),
            PresentationWorkareaNativeCommand.Find => OpenFindDialog,
            PresentationWorkareaNativeCommand.Replace => OpenFindReplaceDialog,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        action();
    }

    private void BindWorkareaEditor(PresentationWorkareaContext context)
    {
        _selectionPane?.SetEditor(context.Snapshot.Editor);
        if (SlideCanvas is not null)
            AttachCanvasEditing();
        if (context.Transition == PresentationWorkareaTransition.PresentationReplaced &&
            SlidePaneHost is not null)
        {
            SlidePaneHost.Child = new SlidePane(context.Snapshot.Editor);
        }
    }

    private void HideTransientPickers()
    {
        HideLayoutPicker();
        HideTablePicker();
    }

    private void ApplyStatusBeforeReview(PresentationWorkareaTransition transition)
    {
        switch (transition)
        {
            case PresentationWorkareaTransition.Bootstrap:
                UpdateTitle();
                break;
            case PresentationWorkareaTransition.PresentationReplaced:
                UpdateSlideCount();
                break;
            case PresentationWorkareaTransition.EditorChanged:
                UpdateSlideCount();
                UpdateTitle();
                break;
        }
    }
}
