namespace FreeP.App.Compositor;

public sealed record PresentationApplicationCommandCallbacks(
    Action NewPresentation,
    Action OpenPresentation,
    Action SavePresentation,
    Action SavePresentationAs,
    Action PrintPresentation,
    Action Undo,
    Action Redo,
    Action DeleteSelectedShapes,
    Action DuplicateCurrentSlide,
    Action StartSlideShowFromBeginning,
    Action StartSlideShowFromCurrentSlide,
    Action Copy,
    Action Cut,
    Action Paste,
    Action Find,
    Action Replace,
    Action SelectAll);

public sealed class PresentationApplicationFrameCallbacks
{
    private static readonly Action NoAction = static () => { };

    public Action BeforeEditorChanged { get; init; } = NoAction;

    public required Action MarkDirty { get; init; }

    public Action AfterEditorMarkedDirty { get; init; } = NoAction;

    public Action RefreshCommandStates { get; init; } = NoAction;

    public Action RefreshSlidePane { get; init; } = NoAction;

    public required Action RefreshCanvas { get; init; }

    public required Action RefreshNotesPane { get; init; }

    public Action RefreshDocumentStatusBeforeReview { get; init; } = NoAction;

    public required Action RefreshReviewWorkflowPlans { get; init; }

    public required Func<bool> IsSmartArtPaneVisible { get; init; }

    public required Action RefreshSmartArtPane { get; init; }

    public Action RefreshAnimationPaneAfterEditorChanged { get; init; } = NoAction;

    public Action RefreshAnimationPaneAfterNavigation { get; init; } = NoAction;

    public Action RefreshAnimationPaneAfterSelection { get; init; } = NoAction;

    public required Action RefreshSelectionPane { get; init; }

    public required Action RefreshAccessibilityMetadata { get; init; }

    public Action RefreshDocumentStatusAfterReview { get; init; } = NoAction;

    public Action BeforeCurrentSlideChanged { get; init; } = NoAction;

    public required Action ClearReviewSelection { get; init; }

    public Action ResetAnimationSelection { get; init; } = NoAction;

    public required Action ClearMediaSelection { get; init; }

    public Action SyncSlidePaneSelection { get; init; } = NoAction;

    public Action RefreshSlidePaneChrome { get; init; } = NoAction;

    public Action RefreshReviewPaneBeforePlans { get; init; } = NoAction;

    public Action RefreshReviewPaneAfterPlans { get; init; } = NoAction;

    public required Action RefreshVisibleMediaPane { get; init; }

    public Action RefreshCurrentSlideStatus { get; init; } = NoAction;

    public required Action RefreshAltTextRequest { get; init; }

    public required Action RefreshReadingOrder { get; init; }

    public required Func<bool> IsAltTextPaneVisible { get; init; }

    public required Action RefreshAltTextPane { get; init; }
}

/// <summary>
/// Owns framework-neutral application-frame command meaning and editor lifecycle coordination.
/// Renderers retain native input adaptation, control refreshes, focus, painting, and window lifecycle.
/// </summary>
public sealed class PresentationApplicationFrameSession
{
    private readonly PresentationApplicationFrameCallbacks _frame;
    private readonly PresentationApplicationCommandCallbacks _commands;
    private EditingSession? _editor;

    public PresentationApplicationFrameSession(
        PresentationApplicationFrameCallbacks frame,
        PresentationApplicationCommandCallbacks commands)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public void Attach(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (ReferenceEquals(_editor, editor))
            return;

        if (_editor is not null)
        {
            _editor.Changed -= HandleEditorChanged;
            _editor.CurrentSlideChanged -= HandleCurrentSlideChanged;
            _editor.SelectionChanged -= HandleEditorSelectionChanged;
            _editor.ActiveTableCellChanged -= HandleActiveTableCellChanged;
        }

        _editor = editor;
        _editor.Changed += HandleEditorChanged;
        _editor.CurrentSlideChanged += HandleCurrentSlideChanged;
        _editor.SelectionChanged += HandleEditorSelectionChanged;
        _editor.ActiveTableCellChanged += HandleActiveTableCellChanged;
    }

    public void ExecuteCommand(FreePKeyboardCommand command)
    {
        var action = command switch
        {
            FreePKeyboardCommand.NewPresentation => _commands.NewPresentation,
            FreePKeyboardCommand.OpenPresentation => _commands.OpenPresentation,
            FreePKeyboardCommand.SavePresentation => _commands.SavePresentation,
            FreePKeyboardCommand.SavePresentationAs => _commands.SavePresentationAs,
            FreePKeyboardCommand.PrintPresentation => _commands.PrintPresentation,
            FreePKeyboardCommand.Undo => _commands.Undo,
            FreePKeyboardCommand.Redo => _commands.Redo,
            FreePKeyboardCommand.DeleteSelectedShapes => _commands.DeleteSelectedShapes,
            FreePKeyboardCommand.DuplicateCurrentSlide => _commands.DuplicateCurrentSlide,
            FreePKeyboardCommand.StartSlideShowFromBeginning => _commands.StartSlideShowFromBeginning,
            FreePKeyboardCommand.StartSlideShowFromCurrentSlide => _commands.StartSlideShowFromCurrentSlide,
            FreePKeyboardCommand.Copy => _commands.Copy,
            FreePKeyboardCommand.Cut => _commands.Cut,
            FreePKeyboardCommand.Paste => _commands.Paste,
            FreePKeyboardCommand.Find => _commands.Find,
            FreePKeyboardCommand.Replace => _commands.Replace,
            FreePKeyboardCommand.SelectAll => _commands.SelectAll,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        action();
    }

    internal void HandleEditorChanged()
    {
        _frame.BeforeEditorChanged();
        _frame.MarkDirty();
        _frame.AfterEditorMarkedDirty();
        _frame.RefreshCommandStates();
        _frame.RefreshSlidePane();
        _frame.RefreshCanvas();
        _frame.RefreshNotesPane();
        _frame.RefreshDocumentStatusBeforeReview();
        _frame.RefreshReviewWorkflowPlans();
        if (_frame.IsSmartArtPaneVisible())
            _frame.RefreshSmartArtPane();
        _frame.RefreshAnimationPaneAfterEditorChanged();
        _frame.RefreshSelectionPane();
        _frame.RefreshAccessibilityMetadata();
        _frame.RefreshDocumentStatusAfterReview();
    }

    internal void HandleCurrentSlideChanged(object? sender = null, EventArgs? e = null)
    {
        _frame.BeforeCurrentSlideChanged();
        _frame.ClearReviewSelection();
        _frame.ResetAnimationSelection();
        _frame.ClearMediaSelection();
        _frame.RefreshCommandStates();
        _frame.SyncSlidePaneSelection();
        _frame.RefreshSlidePaneChrome();
        _frame.RefreshCanvas();
        _frame.RefreshNotesPane();
        _frame.RefreshReviewPaneBeforePlans();
        _frame.RefreshReviewWorkflowPlans();
        _frame.RefreshReviewPaneAfterPlans();
        _frame.RefreshVisibleMediaPane();
        _frame.RefreshAnimationPaneAfterNavigation();
        _frame.RefreshSelectionPane();
        _frame.RefreshAccessibilityMetadata();
        _frame.RefreshCurrentSlideStatus();
    }

    internal void HandleEditorSelectionChanged(object? sender = null, EventArgs? e = null)
    {
        _frame.RefreshCommandStates();
        _frame.RefreshAltTextRequest();
        _frame.RefreshReadingOrder();
        if (_frame.IsAltTextPaneVisible())
            _frame.RefreshAltTextPane();
        if (_frame.IsSmartArtPaneVisible())
            _frame.RefreshSmartArtPane();
        _frame.RefreshVisibleMediaPane();
        _frame.RefreshAnimationPaneAfterSelection();
        _frame.RefreshSelectionPane();
        _frame.RefreshAccessibilityMetadata();
    }

    internal void HandleActiveTableCellChanged(object? sender = null, EventArgs? e = null) =>
        _frame.RefreshCommandStates();
}
