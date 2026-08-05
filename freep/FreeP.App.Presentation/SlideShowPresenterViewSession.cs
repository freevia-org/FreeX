namespace FreeP.App.Compositor;

public sealed record SlideShowPresenterViewActionResult(
    bool NotesCommitted,
    bool CommandInvoked);

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

    public SlideShowPresenterViewPlan BuildViewPlan()
    {
        var state = _stateProvider();
        return SlideShowPresenterViewPlanner.Build(
            state,
            _recordingReviewProvider?.Invoke(),
            canGoBack: _goBack is not null,
            canGoNext: _goNext is not null,
            canSetTimingIntent: _setTimingIntent is not null,
            canSetMediaIntent: _setMediaIntent is not null,
            canApplyRecording: _applyRecordingReview is not null);
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

        var slideIndex = _stateProvider().CurrentSlide?.SlideIndex;
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
}
