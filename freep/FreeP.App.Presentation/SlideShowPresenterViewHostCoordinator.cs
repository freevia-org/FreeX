namespace FreeP.App.Compositor;

public sealed record SlideShowPresenterViewHostRefreshInput(
    bool NotesFocused,
    string? NotesText,
    bool SlideNumberFocused);

public sealed record SlideShowPresenterViewHostActionInput(
    string? SlideNumberText,
    string? NotesText);

/// <summary>
/// Owns presenter-view host interaction state while native windows retain control,
/// focus, timer, and lifecycle responsibilities.
/// </summary>
public sealed class SlideShowPresenterViewHostCoordinator
{
    private readonly SlideShowPresenterViewSession _session;
    private bool _notesDirty;
    private bool _isApplyingRefresh;

    public SlideShowPresenterViewHostCoordinator(SlideShowPresenterViewOperations operations)
        : this(new SlideShowPresenterViewSession(operations))
    {
    }

    internal SlideShowPresenterViewHostCoordinator(SlideShowPresenterViewSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public static TimeSpan RefreshInterval => SlideShowPresenterViewSession.RefreshInterval;

    public SlideShowPresenterViewSurfacePlan Surface => _session.Surface;

    public bool CanGoToSlide => _session.CanGoToSlide;

    public bool CanSetScreenMode => _session.CanSetScreenMode;

    public bool CanSelectPointerMode => _session.CanSelectPointerMode;

    public bool CanClearInk => _session.CanClearInk;

    public bool CanSetNotes => _session.CanSetNotes;

    public void NotifyNotesTextChanged()
    {
        if (!_isApplyingRefresh && CanSetNotes)
        {
            _notesDirty = true;
        }
    }

    public void Refresh(
        SlideShowPresenterViewHostRefreshInput input,
        Action<SlideShowPresenterViewRefreshPlan> applyPlan)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(applyPlan);

        var refresh = _session.BuildRefreshPlan(new SlideShowPresenterViewRefreshRequest(
            input.NotesFocused,
            _notesDirty,
            input.NotesText,
            input.SlideNumberFocused));
        _notesDirty &= !refresh.NotesCommitted;

        _isApplyingRefresh = true;
        try
        {
            applyPlan(refresh);
        }
        finally
        {
            _isApplyingRefresh = false;
        }
    }

    public void ExecuteAction(
        SlideShowPresenterViewAction action,
        SlideShowPresenterViewHostActionInput input,
        Action refresh)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(refresh);

        var result = _session.Dispatch(new SlideShowPresenterViewDispatchRequest(
            action,
            input.SlideNumberText,
            _notesDirty,
            input.NotesText));
        _notesDirty &= !result.NotesCommitted;
        if (result.ShouldRefresh)
        {
            refresh();
        }
    }

    public void CommitNotes(string? notesText)
    {
        _notesDirty &= !_session.CommitNotes(_notesDirty, notesText);
    }

    public void SelectPointerMode(
        SlideShowPresenterPointerMode mode,
        Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        if (_isApplyingRefresh || !CanSelectPointerMode)
        {
            return;
        }

        _session.SelectPointerMode(mode);
        refresh();
    }
}
