namespace FreeP.App.Compositor;

public sealed record SlideShowPresenterViewActionResult(
    bool NotesCommitted,
    bool CommandInvoked);

public sealed record SlideShowPresenterViewDispatchRequest(
    SlideShowPresenterViewAction Action,
    string? SlideNumberText = null,
    bool NotesDirty = false,
    string? NotesText = null);

public sealed record SlideShowPresenterViewDispatchResult(
    bool NotesCommitted,
    bool CommandInvoked,
    bool ShouldRefresh);

public sealed record SlideShowPresenterViewRefreshRequest(
    bool NotesFocused,
    bool NotesDirty,
    string? NotesText,
    bool SlideNumberFocused);

public sealed record SlideShowPresenterViewRefreshPlan(
    SlideShowPresenterViewPlan ViewPlan,
    bool NotesCommitted,
    bool ShouldUpdateNotesText,
    bool ShouldUpdateSlideNumber);

/// <summary>
/// Renderer-neutral presenter-window interaction session. Native adapters provide
/// controls, focus state, and refresh timing while this class owns command intent,
/// note commits, and presenter view-state composition.
/// </summary>
public sealed class SlideShowPresenterViewSession
{
    private readonly Func<SlideShowPresenterState> _stateProvider;
    private readonly Action? _goBack;
    private readonly Action? _goNext;
    private readonly Action<int>? _goToSlide;
    private readonly Action<SlideShowScreenMode>? _setScreenMode;
    private readonly Action<SlideShowPresenterPointerMode>? _selectPointerMode;
    private readonly Action? _clearInk;
    private readonly Action<SlideShowTimingIntent>? _setTimingIntent;
    private readonly Action<SlideShowRecordingMediaIntent>? _setMediaIntent;
    private readonly Func<SlideShowRecordingReviewPlan>? _recordingReviewProvider;
    private readonly Func<SlideShowRecordingReviewApplyResult>? _applyRecordingReview;
    private readonly Action<int, string?>? _setNotesText;
    private int? _notesPresentationSlideIndex;

    public SlideShowPresenterViewSession(SlideShowPresenterViewOperations operations)
        : this(
            (operations ?? throw new ArgumentNullException(nameof(operations))).StateProvider,
            operations.GoBack,
            operations.GoNext,
            operations.SetScreenMode,
            operations.SelectPointerMode,
            operations.ClearInk,
            operations.SetTimingIntent,
            operations.SetMediaIntent,
            operations.RecordingReviewProvider,
            operations.ApplyRecordingReview,
            operations.GoToSlide,
            operations.SetNotesText)
    {
    }

    public SlideShowPresenterViewSession(
        Func<SlideShowPresenterState> stateProvider,
        Action? goBack = null,
        Action? goNext = null,
        Action<SlideShowScreenMode>? setScreenMode = null,
        Action<SlideShowPresenterPointerMode>? selectPointerMode = null,
        Action? clearInk = null,
        Action<SlideShowTimingIntent>? setTimingIntent = null,
        Action<SlideShowRecordingMediaIntent>? setMediaIntent = null,
        Func<SlideShowRecordingReviewPlan>? recordingReviewProvider = null,
        Func<SlideShowRecordingReviewApplyResult>? applyRecordingReview = null,
        Action<int>? goToSlide = null,
        Action<int, string?>? setNotesText = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _goBack = goBack;
        _goNext = goNext;
        _goToSlide = goToSlide;
        _setScreenMode = setScreenMode;
        _selectPointerMode = selectPointerMode;
        _clearInk = clearInk;
        _setTimingIntent = setTimingIntent;
        _setMediaIntent = setMediaIntent;
        _recordingReviewProvider = recordingReviewProvider;
        _applyRecordingReview = applyRecordingReview;
        _setNotesText = setNotesText;
    }

    public bool CanGoToSlide => _goToSlide is not null;

    public bool CanSetScreenMode => _setScreenMode is not null;

    public bool CanSelectPointerMode => _selectPointerMode is not null;

    public bool CanClearInk => _clearInk is not null;

    public bool CanSetNotes => _setNotesText is not null;

    public SlideShowPresenterViewSurfacePlan Surface =>
        SlideShowPresenterViewSurfaceCatalog.Surface;

    public SlideShowPresenterViewPlan BuildViewPlan()
    {
        var state = _stateProvider();
        return BuildViewPlan(state);
    }

    private SlideShowPresenterViewPlan BuildViewPlan(SlideShowPresenterState state)
    {
        return SlideShowPresenterViewPlanner.Build(
            state,
            _recordingReviewProvider?.Invoke(),
            canGoBack: _goBack is not null,
            canGoNext: _goNext is not null,
            canSetTimingIntent: _setTimingIntent is not null,
            canSetMediaIntent: _setMediaIntent is not null,
            canApplyRecording: _applyRecordingReview is not null);
    }

    public SlideShowPresenterViewRefreshPlan BuildRefreshPlan(
        SlideShowPresenterViewRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notesCommitted = !request.NotesFocused &&
            CommitNotes(request.NotesDirty, request.NotesText);
        var notesRemainDirty = request.NotesDirty && !notesCommitted;
        var state = _stateProvider();
        var viewPlan = BuildViewPlan(state);
        var shouldUpdateNotesText = !request.NotesFocused && !notesRemainDirty;
        if (shouldUpdateNotesText)
        {
            _notesPresentationSlideIndex = state.CurrentSlide?.PresentationSlideIndex;
        }

        return new(
            viewPlan,
            notesCommitted,
            ShouldUpdateNotesText: shouldUpdateNotesText,
            ShouldUpdateSlideNumber:
                !request.SlideNumberFocused && viewPlan.CurrentSlideNumber is not null);
    }

    public SlideShowPresenterViewDispatchResult Dispatch(
        SlideShowPresenterViewDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.Action)
        {
            case SlideShowPresenterViewAction.Previous:
                return new(
                    GoBack(request.NotesDirty, request.NotesText),
                    _goBack is not null,
                    ShouldRefresh: true);
            case SlideShowPresenterViewAction.Next:
                return new(
                    GoNext(request.NotesDirty, request.NotesText),
                    _goNext is not null,
                    ShouldRefresh: true);
            case SlideShowPresenterViewAction.GoToSlide:
                var jump = GoToSlide(
                    request.SlideNumberText,
                    request.NotesDirty,
                    request.NotesText);
                return new(jump.NotesCommitted, jump.CommandInvoked, jump.CommandInvoked);
            case SlideShowPresenterViewAction.RecordTimings:
                return ToggleTiming(SlideShowTimingIntent.RecordTimings);
            case SlideShowPresenterViewAction.RehearseTimings:
                return ToggleTiming(SlideShowTimingIntent.RehearseTimings);
            case SlideShowPresenterViewAction.Narration:
                return ToggleMedia(SlideShowRecordingMediaIntent.Narration);
            case SlideShowPresenterViewAction.NarrationAndMedia:
                return ToggleMedia(SlideShowRecordingMediaIntent.NarrationAndMedia);
            case SlideShowPresenterViewAction.ApplyRecording:
                var canApplyRecording = _applyRecordingReview is not null;
                ApplyRecordingReview();
                return ToolResult(canApplyRecording, shouldRefresh: canApplyRecording);
            case SlideShowPresenterViewAction.ShowScreen:
                return SetScreen(SlideShowScreenMode.Normal);
            case SlideShowPresenterViewAction.BlackScreen:
                return SetScreen(SlideShowScreenMode.Black);
            case SlideShowPresenterViewAction.WhiteScreen:
                return SetScreen(SlideShowScreenMode.White);
            case SlideShowPresenterViewAction.ClearInk:
                var canClearInk = _clearInk is not null;
                ClearInk();
                return ToolResult(canClearInk);
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Action, null);
        }
    }

    public bool GoBack(bool notesDirty, string? notesText)
    {
        var committed = CommitNotes(notesDirty, notesText);
        _goBack?.Invoke();
        return committed;
    }

    public bool GoNext(bool notesDirty, string? notesText)
    {
        var committed = CommitNotes(notesDirty, notesText);
        _goNext?.Invoke();
        return committed;
    }

    public SlideShowPresenterViewActionResult GoToSlide(
        string? slideNumberText,
        bool notesDirty,
        string? notesText)
    {
        var committed = CommitNotes(notesDirty, notesText);
        if (_goToSlide is not null &&
            SlideShowSlideNumberPlanner.TryParseSlideNumber(slideNumberText, out var oneBasedSlideNumber))
        {
            _goToSlide(oneBasedSlideNumber);
            return new SlideShowPresenterViewActionResult(committed, CommandInvoked: true);
        }

        return new SlideShowPresenterViewActionResult(committed, CommandInvoked: false);
    }

    public bool CommitNotes(bool notesDirty, string? notesText)
    {
        if (!notesDirty || _setNotesText is null)
        {
            return false;
        }

        var slideIndex = _notesPresentationSlideIndex ??
            _stateProvider().CurrentSlide?.PresentationSlideIndex;
        if (slideIndex is not int index)
        {
            return false;
        }

        _setNotesText(index, notesText);
        return true;
    }

    public void SetScreenMode(SlideShowScreenMode mode) => _setScreenMode?.Invoke(mode);

    public void SelectPointerMode(SlideShowPresenterPointerMode mode) =>
        _selectPointerMode?.Invoke(mode);

    public void ClearInk() => _clearInk?.Invoke();

    public void ToggleTimingIntent(SlideShowTimingIntent timingIntent)
    {
        if (_setTimingIntent is null)
        {
            return;
        }

        var current = _stateProvider().ToolPlan.Recording.TimingIntent;
        _setTimingIntent(current == timingIntent ? SlideShowTimingIntent.None : timingIntent);
    }

    public void ToggleMediaIntent(SlideShowRecordingMediaIntent mediaIntent)
    {
        if (_setMediaIntent is null)
        {
            return;
        }

        var current = _stateProvider().ToolPlan.Recording.MediaIntent;
        _setMediaIntent(current == mediaIntent ? SlideShowRecordingMediaIntent.None : mediaIntent);
    }

    public SlideShowRecordingReviewApplyResult? ApplyRecordingReview() =>
        _applyRecordingReview?.Invoke();

    private SlideShowPresenterViewDispatchResult ToggleTiming(
        SlideShowTimingIntent intent)
    {
        var canToggle = _setTimingIntent is not null;
        ToggleTimingIntent(intent);
        return ToolResult(canToggle, shouldRefresh: canToggle);
    }

    private SlideShowPresenterViewDispatchResult ToggleMedia(
        SlideShowRecordingMediaIntent intent)
    {
        var canToggle = _setMediaIntent is not null;
        ToggleMediaIntent(intent);
        return ToolResult(canToggle, shouldRefresh: canToggle);
    }

    private SlideShowPresenterViewDispatchResult SetScreen(SlideShowScreenMode mode)
    {
        var canSet = _setScreenMode is not null;
        SetScreenMode(mode);
        return ToolResult(canSet);
    }

    private static SlideShowPresenterViewDispatchResult ToolResult(
        bool commandInvoked,
        bool shouldRefresh = false) =>
        new(false, commandInvoked, shouldRefresh);
}
