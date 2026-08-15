using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Stable presentation-layer facade for public slideshow control and inspection.
/// Native windows retain event, timer, focus, media, transition, and pixel realization.
/// </summary>
public sealed class SlideShowRuntimeSession
{
    private readonly SlideShowRuntimeApplication _runtime;

    public SlideShowRuntimeSession(SlideShowRuntimeApplication runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public SlideShowController Controller => _runtime.Controller;
    public SlideShowScreenMode ScreenMode => _runtime.ScreenMode;
    public DateTimeOffset PresenterStartedAtUtc => _runtime.StartedAtUtc;
    public SlideShowPresenterToolPlan PresenterToolPlan => _runtime.ToolPlan;
    public IReadOnlyList<SlideShowPresenterWorkflowAction> PresenterWorkflowActions => _runtime.ToolPlan.WorkflowActions;
    public IReadOnlyList<SlideShowPresenterCommandState> PresenterCommandStates => _runtime.ToolPlan.CommandStates;
    public SlideShowTimingRecorderState TimingRecorderState => _runtime.TimingRecorderState;
    public SlideShowRecordingExecutionState RecordingExecutionState => _runtime.RecordingExecutionState;
    public SlideShowRecordingCaptureAdapterReadiness RecordingCaptureAdapterReadiness =>
        _runtime.RecordingExecutionState.HostCapabilities.EffectiveCaptureAdapterReadiness;
    public IReadOnlyList<SlideShowRecordingExecutionAction> RecordingExecutionActions =>
        _runtime.RecordingExecutionState.LastActions;
    public bool IsPresenterSessionClosed => _runtime.IsClosed;
    public SlideShowInkExecutionState InkExecutionState => _runtime.InkExecutionState;
    public SlideShowPresenterSessionSummary PresenterSessionSummary => _runtime.PresenterSummary;
    public SlideShowRecordingReviewPlan RecordingReviewPlan => _runtime.RecordingReviewPlan;
    public bool IsPresenterViewOpen => _runtime.IsPresenterViewOpen;

    public AdvanceResult ExecuteAdvance(DateTimeOffset? nowUtc = null) => _runtime.ExecuteAdvance(nowUtc);
    public BackResult ExecuteBack(DateTimeOffset? nowUtc = null) => _runtime.ExecuteBack(nowUtc);
    public void ExecuteSlideNumberJump(int oneBasedSlideNumber) => _runtime.ExecuteSlideNumberJump(oneBasedSlideNumber);
    public Slide? ExecuteHiddenSlideReveal() => _runtime.ExecuteHiddenSlideReveal();
    public void SetScreenMode(SlideShowScreenMode mode) => _runtime.SetScreenMode(mode);
    public SlideShowRecordingReviewApplyResult ApplyRecordingReview() => _runtime.ApplyRecordingReview();
    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) =>
        _runtime.CreatePresenterState(nowUtc, displayIntent);
    public void TogglePresenterView() => _runtime.TogglePresenterView();

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        DateTimeOffset? nowUtc = null) =>
        _runtime.ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            nowUtc);

    public SlideShowPresenterToolPlan SetPresenterPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset? nowUtc = null) => _runtime.SetPointerMode(pointerMode, nowUtc);
    public SlideShowPresenterToolPlan SetPresenterTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset? nowUtc = null) => _runtime.SetTimingIntent(timingIntent, nowUtc);
    public SlideShowPresenterToolPlan SetPresenterMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset? nowUtc = null) => _runtime.SetMediaIntent(mediaIntent, nowUtc);

    public SlideShowInkExecutionResult BeginPresenterInkStroke(SlideShowCanvasPointer pointer) =>
        _runtime.BeginPointerInk(pointer);
    public SlideShowInkExecutionResult AppendPresenterInkStroke(SlideShowCanvasPointer pointer) =>
        _runtime.AppendPointerInk(pointer);
    public SlideShowInkExecutionResult EndPresenterInkStroke(SlideShowCanvasPointer pointer) =>
        _runtime.EndPointerInk(pointer);
    public SlideShowInkExecutionResult ClearPresenterInkStrokes() => _runtime.ClearInkStrokes();
    public SlideShowInkExecutionResult UndoLastPresenterInkStroke() => _runtime.UndoLastInkStroke();
}
