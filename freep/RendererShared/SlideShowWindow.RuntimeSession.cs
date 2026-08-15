using FreeP.App.Compositor;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

public sealed partial class SlideShowWindow
{
    /// <summary>The portable control and inspection surface for this running slideshow.</summary>
    public SlideShowRuntimeSession RuntimeSession => _runtimeSession;

    public AdvanceResult ExecuteAdvance(DateTimeOffset? nowUtc = null) => RuntimeSession.ExecuteAdvance(nowUtc);
    public BackResult ExecuteBack(DateTimeOffset? nowUtc = null) => RuntimeSession.ExecuteBack(nowUtc);
    public void ExecuteSlideNumberJump(int oneBasedSlideNumber) => RuntimeSession.ExecuteSlideNumberJump(oneBasedSlideNumber);
    public Slide? ExecuteHiddenSlideReveal() => RuntimeSession.ExecuteHiddenSlideReveal();
    public SlideShowController Controller => RuntimeSession.Controller;
    public SlideShowScreenMode ScreenMode => RuntimeSession.ScreenMode;
    public void SetScreenMode(SlideShowScreenMode mode) => RuntimeSession.SetScreenMode(mode);
    public DateTimeOffset PresenterStartedAtUtc => RuntimeSession.PresenterStartedAtUtc;
    public SlideShowPresenterToolPlan PresenterToolPlan => RuntimeSession.PresenterToolPlan;
    public IReadOnlyList<SlideShowPresenterWorkflowAction> PresenterWorkflowActions => RuntimeSession.PresenterWorkflowActions;
    public IReadOnlyList<SlideShowPresenterCommandState> PresenterCommandStates => RuntimeSession.PresenterCommandStates;
    public SlideShowTimingRecorderState TimingRecorderState => RuntimeSession.TimingRecorderState;
    public SlideShowRecordingExecutionState RecordingExecutionState => RuntimeSession.RecordingExecutionState;
    public SlideShowRecordingCaptureAdapterReadiness RecordingCaptureAdapterReadiness => RuntimeSession.RecordingCaptureAdapterReadiness;
    public IReadOnlyList<SlideShowRecordingExecutionAction> RecordingExecutionActions => RuntimeSession.RecordingExecutionActions;
    public bool IsPresenterSessionClosed => RuntimeSession.IsPresenterSessionClosed;
    public SlideShowInkExecutionState InkExecutionState => RuntimeSession.InkExecutionState;
    public SlideShowPresenterSessionSummary PresenterSessionSummary => RuntimeSession.PresenterSessionSummary;
    public SlideShowRecordingReviewPlan RecordingReviewPlan => RuntimeSession.RecordingReviewPlan;
    public SlideShowRecordingReviewApplyResult ApplyRecordingReview() => RuntimeSession.ApplyRecordingReview();
    public SlideShowPresenterState CreatePresenterState(
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null) => RuntimeSession.CreatePresenterState(nowUtc, displayIntent);
    public bool IsPresenterViewOpen => RuntimeSession.IsPresenterViewOpen;
    public void TogglePresenterView() => RuntimeSession.TogglePresenterView();

    public SlideShowPresenterToolPlan ApplyPresenterToolIntent(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        DateTimeOffset? nowUtc = null) =>
        RuntimeSession.ApplyPresenterToolIntent(
            timingIntent,
            mediaIntent,
            pointerMode,
            inkColorHex,
            inkThicknessDip,
            inkRetentionDecision,
            nowUtc);

    public SlideShowPresenterToolPlan SetPresenterPointerMode(
        SlideShowPresenterPointerMode pointerMode,
        DateTimeOffset? nowUtc = null) => RuntimeSession.SetPresenterPointerMode(pointerMode, nowUtc);
    public SlideShowPresenterToolPlan SetPresenterTimingIntent(
        SlideShowTimingIntent timingIntent,
        DateTimeOffset? nowUtc = null) => RuntimeSession.SetPresenterTimingIntent(timingIntent, nowUtc);
    public SlideShowPresenterToolPlan SetPresenterMediaIntent(
        SlideShowRecordingMediaIntent mediaIntent,
        DateTimeOffset? nowUtc = null) => RuntimeSession.SetPresenterMediaIntent(mediaIntent, nowUtc);
    public SlideShowInkExecutionResult BeginPresenterInkStroke(double canvasX, double canvasY) =>
        RuntimeSession.BeginPresenterInkStroke(CreateCanvasPointer(canvasX, canvasY));
    public SlideShowInkExecutionResult AppendPresenterInkStroke(double canvasX, double canvasY) =>
        RuntimeSession.AppendPresenterInkStroke(CreateCanvasPointer(canvasX, canvasY));
    public SlideShowInkExecutionResult EndPresenterInkStroke(double canvasX, double canvasY) =>
        RuntimeSession.EndPresenterInkStroke(CreateCanvasPointer(canvasX, canvasY));
    public SlideShowInkExecutionResult ClearPresenterInkStrokes() => RuntimeSession.ClearPresenterInkStrokes();
    public SlideShowInkExecutionResult UndoLastPresenterInkStroke() => RuntimeSession.UndoLastPresenterInkStroke();
}
