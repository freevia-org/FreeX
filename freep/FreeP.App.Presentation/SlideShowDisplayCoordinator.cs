using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowDisplayRendererOperationKind
{
    StopAutoAdvanceTimer,
    CancelVisualOperations,
    ApplyDisplayState,
    RefreshInkOverlay,
    PrepareAnimationOverlay,
    EnterMediaSlide,
    PlayTransition,
    ShowSlideInstant,
    StartAutoAdvanceTimer,
    RefreshPresenterView,
    StopKioskRestartTimer,
    StartKioskRestartTimer,
    RequestAutoAdvance,
    RequestKioskRestart,
    OpenPresenterView,
    ClosePresenterView
}

public sealed record SlideShowDisplayRendererOperation(
    SlideShowDisplayRendererOperationKind Kind,
    TimeSpan? Interval = null,
    long DisplayVersion = 0);

public sealed record SlideShowDisplayRendererPlan(
    long DisplayVersion,
    SlideShowRuntimeDisplayPlan? Display,
    IReadOnlyList<SlideShowDisplayRendererOperation> Operations)
{
    public static SlideShowDisplayRendererPlan Empty(long displayVersion) =>
        new(displayVersion, null, Array.Empty<SlideShowDisplayRendererOperation>());
}

/// <summary>
/// Native slideshow surface operations. Implementations adapt controls, timers,
/// transitions, media surfaces, dispatchers, and geometry to their UI framework.
/// </summary>
public interface ISlideShowDisplayRenderer
{
    void StopAutoAdvanceTimer();

    void CancelVisualOperations();

    void ApplyDisplayState(SlideShowRuntimeDisplayPlan plan);

    void RefreshInkOverlay();

    void PrepareAnimationOverlay(Slide slide);

    void EnterMediaSlide(SlideShowRuntimeDisplayPlan plan);

    void PlayTransition(Slide slide, SlideTransition transition);

    void ShowSlideInstant(Slide slide);

    void StartAutoAdvanceTimer(TimeSpan interval, long displayVersion);

    void RefreshPresenterView();

    void StopKioskRestartTimer();

    void StartKioskRestartTimer(TimeSpan interval);

    void RequestAutoAdvance();

    void RequestKioskRestart();

    void OpenPresenterView();

    void ClosePresenterView();
}

/// <summary>
/// Portable state machine for slideshow display sequencing. It owns display
/// generations, timer decisions, presenter state, and ordered renderer plans.
/// </summary>
public sealed class SlideShowDisplayCoordinator
{
    private long _displayVersion;
    private long? _autoAdvanceDisplayVersion;
    private TimeSpan? _autoAdvanceInterval;
    private bool _autoAdvancePausedForBlank;
    private TimeSpan? _kioskRestartInterval;
    private bool _sessionStarted;
    private bool _presenterViewOpen;
    private bool _closed;

    public long DisplayVersion => _displayVersion;

    public bool IsPresenterViewOpen => _presenterViewOpen;

    public bool IsClosed => _closed;

    public SlideShowDisplayRendererPlan PlanDisplay(SlideShowRuntimeDisplayPlan display)
    {
        ArgumentNullException.ThrowIfNull(display);
        ThrowIfClosed();

        var displayVersion = ++_displayVersion;
        _autoAdvanceDisplayVersion = null;
        _autoAdvanceInterval = null;
        // A fresh slide display always supersedes any timer that was paused for a
        // blanked screen -- there is nothing left to resume once the slide changes.
        _autoAdvancePausedForBlank = false;

        var operations = new List<SlideShowDisplayRendererOperation>
        {
            new(SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer),
            new(SlideShowDisplayRendererOperationKind.CancelVisualOperations),
            new(SlideShowDisplayRendererOperationKind.ApplyDisplayState),
            new(SlideShowDisplayRendererOperationKind.RefreshInkOverlay)
        };

        if (display.Slide is not null)
        {
            operations.Add(new(SlideShowDisplayRendererOperationKind.PrepareAnimationOverlay));
            operations.Add(new(SlideShowDisplayRendererOperationKind.EnterMediaSlide));
            operations.Add(new(
                display.Transition is null
                    ? SlideShowDisplayRendererOperationKind.ShowSlideInstant
                    : SlideShowDisplayRendererOperationKind.PlayTransition));

            if (display.AutoAdvanceAfterMs is int advanceAfterMs)
            {
                var interval = TimeSpan.FromMilliseconds(advanceAfterMs);
                _autoAdvanceDisplayVersion = displayVersion;
                _autoAdvanceInterval = interval;
                operations.Add(new(
                    SlideShowDisplayRendererOperationKind.StartAutoAdvanceTimer,
                    interval,
                    displayVersion));
            }
        }

        if (_presenterViewOpen)
        {
            operations.Add(new(SlideShowDisplayRendererOperationKind.RefreshPresenterView));
        }

        return new SlideShowDisplayRendererPlan(displayVersion, display, operations);
    }

    public SlideShowDisplayRendererPlan Display(
        SlideShowRuntimeDisplayPlan display,
        ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanDisplay(display);
        Execute(plan, renderer);
        return plan;
    }

    public SlideShowDisplayRendererPlan PlanSessionStart(TimeSpan? kioskRestartInterval)
    {
        ThrowIfClosed();

        _sessionStarted = true;
        _kioskRestartInterval = kioskRestartInterval;
        var operations = new List<SlideShowDisplayRendererOperation>
        {
            new(SlideShowDisplayRendererOperationKind.StopKioskRestartTimer)
        };
        if (kioskRestartInterval is { } interval)
        {
            operations.Add(new(
                SlideShowDisplayRendererOperationKind.StartKioskRestartTimer,
                interval));
        }

        return new SlideShowDisplayRendererPlan(_displayVersion, null, operations);
    }

    public SlideShowDisplayRendererPlan StartSession(
        TimeSpan? kioskRestartInterval,
        ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanSessionStart(kioskRestartInterval);
        Execute(plan, renderer);
        return plan;
    }

    public SlideShowDisplayRendererPlan PlanAutoAdvanceElapsed(long displayVersion)
    {
        // Guard against a Tick that was already in flight when the screen was blanked --
        // the host is asked to stop the timer on blank, but a race between that request
        // and an already-queued dispatcher Tick must not be allowed to advance the slide
        // behind the blank overlay.
        if (_closed || _autoAdvancePausedForBlank || _autoAdvanceDisplayVersion != displayVersion)
        {
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        _autoAdvanceDisplayVersion = null;
        _autoAdvanceInterval = null;
        return new SlideShowDisplayRendererPlan(
            _displayVersion,
            null,
            new SlideShowDisplayRendererOperation[]
            {
                new(SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer),
                new(SlideShowDisplayRendererOperationKind.RequestAutoAdvance)
            });
    }

    public SlideShowDisplayRendererPlan HandleAutoAdvanceElapsed(
        long displayVersion,
        ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanAutoAdvanceElapsed(displayVersion);
        Execute(plan, renderer);
        return plan;
    }

    /// <summary>
    /// Pauses (screen blanked) or resumes (screen unblanked) the current slide's own
    /// auto-advance timer. Blanking must not let the show silently play ahead behind the
    /// overlay, so the timer is fully stopped while blank. This runtime keeps no
    /// elapsed-time bookkeeping for slide dwell time anywhere else, so on unblank the
    /// full authored duration is restarted for the still-current slide rather than
    /// resuming a computed remaining time -- the slide itself cannot have changed while
    /// blanked, since blanked input handling suppresses navigation.
    /// </summary>
    public SlideShowDisplayRendererPlan PlanScreenModeChanged(bool isBlank)
    {
        if (_closed)
        {
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        if (isBlank)
        {
            if (_autoAdvancePausedForBlank || _autoAdvanceDisplayVersion is null)
            {
                return SlideShowDisplayRendererPlan.Empty(_displayVersion);
            }

            _autoAdvancePausedForBlank = true;
            return new SlideShowDisplayRendererPlan(
                _displayVersion,
                null,
                new SlideShowDisplayRendererOperation[]
                {
                    new(SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer)
                });
        }

        if (!_autoAdvancePausedForBlank ||
            _autoAdvanceDisplayVersion is not { } resumeVersion ||
            _autoAdvanceInterval is not { } resumeInterval)
        {
            _autoAdvancePausedForBlank = false;
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        _autoAdvancePausedForBlank = false;
        return new SlideShowDisplayRendererPlan(
            _displayVersion,
            null,
            new SlideShowDisplayRendererOperation[]
            {
                new(
                    SlideShowDisplayRendererOperationKind.StartAutoAdvanceTimer,
                    resumeInterval,
                    resumeVersion)
            });
    }

    public SlideShowDisplayRendererPlan ScreenModeChanged(
        bool isBlank,
        ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanScreenModeChanged(isBlank);
        Execute(plan, renderer);
        return plan;
    }

    public SlideShowDisplayRendererPlan PlanKioskRestartElapsed()
    {
        if (_closed || !_sessionStarted || _kioskRestartInterval is null)
        {
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        return new SlideShowDisplayRendererPlan(
            _displayVersion,
            null,
            new SlideShowDisplayRendererOperation[]
            {
                new(SlideShowDisplayRendererOperationKind.RequestKioskRestart)
            });
    }

    public SlideShowDisplayRendererPlan HandleKioskRestartElapsed(
        ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanKioskRestartElapsed();
        Execute(plan, renderer);
        return plan;
    }

    public SlideShowDisplayRendererPlan PlanPresenterToggle()
    {
        if (_closed)
        {
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        _presenterViewOpen = !_presenterViewOpen;
        return new SlideShowDisplayRendererPlan(
            _displayVersion,
            null,
            new SlideShowDisplayRendererOperation[]
            {
                new(_presenterViewOpen
                    ? SlideShowDisplayRendererOperationKind.OpenPresenterView
                    : SlideShowDisplayRendererOperationKind.ClosePresenterView)
            });
    }

    public SlideShowDisplayRendererPlan TogglePresenterView(ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanPresenterToggle();
        Execute(plan, renderer);
        return plan;
    }

    public void NotifyPresenterViewClosed() => _presenterViewOpen = false;

    public SlideShowDisplayRendererPlan PlanCloseSession()
    {
        if (_closed)
        {
            return SlideShowDisplayRendererPlan.Empty(_displayVersion);
        }

        _closed = true;
        _sessionStarted = false;
        _kioskRestartInterval = null;
        _autoAdvanceDisplayVersion = null;
        _autoAdvanceInterval = null;
        _autoAdvancePausedForBlank = false;
        _displayVersion++;

        var operations = new List<SlideShowDisplayRendererOperation>
        {
            new(SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer),
            new(SlideShowDisplayRendererOperationKind.StopKioskRestartTimer),
            new(SlideShowDisplayRendererOperationKind.CancelVisualOperations)
        };
        if (_presenterViewOpen)
        {
            _presenterViewOpen = false;
            operations.Add(new(SlideShowDisplayRendererOperationKind.ClosePresenterView));
        }

        return new SlideShowDisplayRendererPlan(_displayVersion, null, operations);
    }

    public SlideShowDisplayRendererPlan CloseSession(ISlideShowDisplayRenderer renderer)
    {
        var plan = PlanCloseSession();
        Execute(plan, renderer);
        return plan;
    }

    public static void Execute(
        SlideShowDisplayRendererPlan plan,
        ISlideShowDisplayRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(renderer);

        foreach (var operation in plan.Operations)
        {
            ExecuteOperation(plan, operation, renderer);
        }
    }

    private static void ExecuteOperation(
        SlideShowDisplayRendererPlan plan,
        SlideShowDisplayRendererOperation operation,
        ISlideShowDisplayRenderer renderer)
    {
        switch (operation.Kind)
        {
            case SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer:
                renderer.StopAutoAdvanceTimer();
                break;
            case SlideShowDisplayRendererOperationKind.CancelVisualOperations:
                renderer.CancelVisualOperations();
                break;
            case SlideShowDisplayRendererOperationKind.ApplyDisplayState:
                renderer.ApplyDisplayState(RequireDisplay(plan));
                break;
            case SlideShowDisplayRendererOperationKind.RefreshInkOverlay:
                renderer.RefreshInkOverlay();
                break;
            case SlideShowDisplayRendererOperationKind.PrepareAnimationOverlay:
                renderer.PrepareAnimationOverlay(RequireSlide(plan));
                break;
            case SlideShowDisplayRendererOperationKind.EnterMediaSlide:
                renderer.EnterMediaSlide(RequireDisplay(plan));
                break;
            case SlideShowDisplayRendererOperationKind.PlayTransition:
            {
                var display = RequireDisplay(plan);
                renderer.PlayTransition(RequireSlide(plan), display.Transition!);
                break;
            }
            case SlideShowDisplayRendererOperationKind.ShowSlideInstant:
                renderer.ShowSlideInstant(RequireSlide(plan));
                break;
            case SlideShowDisplayRendererOperationKind.StartAutoAdvanceTimer:
                renderer.StartAutoAdvanceTimer(
                    operation.Interval ?? throw MissingInterval(operation.Kind),
                    operation.DisplayVersion);
                break;
            case SlideShowDisplayRendererOperationKind.RefreshPresenterView:
                renderer.RefreshPresenterView();
                break;
            case SlideShowDisplayRendererOperationKind.StopKioskRestartTimer:
                renderer.StopKioskRestartTimer();
                break;
            case SlideShowDisplayRendererOperationKind.StartKioskRestartTimer:
                renderer.StartKioskRestartTimer(
                    operation.Interval ?? throw MissingInterval(operation.Kind));
                break;
            case SlideShowDisplayRendererOperationKind.RequestAutoAdvance:
                renderer.RequestAutoAdvance();
                break;
            case SlideShowDisplayRendererOperationKind.RequestKioskRestart:
                renderer.RequestKioskRestart();
                break;
            case SlideShowDisplayRendererOperationKind.OpenPresenterView:
                renderer.OpenPresenterView();
                break;
            case SlideShowDisplayRendererOperationKind.ClosePresenterView:
                renderer.ClosePresenterView();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, null);
        }
    }

    private static SlideShowRuntimeDisplayPlan RequireDisplay(SlideShowDisplayRendererPlan plan) =>
        plan.Display ?? throw new InvalidOperationException("The renderer operation requires a display plan.");

    private static Slide RequireSlide(SlideShowDisplayRendererPlan plan) =>
        RequireDisplay(plan).Slide ??
        throw new InvalidOperationException("The renderer operation requires a slide.");

    private static InvalidOperationException MissingInterval(
        SlideShowDisplayRendererOperationKind operationKind) =>
        new($"The {operationKind} renderer operation requires an interval.");

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("The slideshow display session is closed.");
        }
    }
}
