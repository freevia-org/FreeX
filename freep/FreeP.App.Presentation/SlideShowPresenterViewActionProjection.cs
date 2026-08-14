namespace FreeP.App.Compositor;

public enum SlideShowPresenterViewHeaderItemKind
{
    Action,
    SlideNumber,
    PointerMode,
}

public sealed record SlideShowPresenterViewHeaderItem(
    SlideShowPresenterViewHeaderItemKind Kind,
    SlideShowPresenterViewAction? Action = null);

public sealed record SlideShowPresenterViewActionState(
    SlideShowPresenterViewAction Action,
    string Label,
    bool IsEnabled);

/// <summary>
/// Defines the shared presenter toolbar order and projects dynamic presenter state
/// onto native action controls.
/// </summary>
public static class SlideShowPresenterViewActionProjection
{
    public static IReadOnlyList<SlideShowPresenterViewHeaderItem> HeaderItems { get; } =
    [
        Action(SlideShowPresenterViewAction.Previous),
        Action(SlideShowPresenterViewAction.Next),
        new(SlideShowPresenterViewHeaderItemKind.SlideNumber),
        Action(SlideShowPresenterViewAction.GoToSlide),
        Action(SlideShowPresenterViewAction.RecordTimings),
        Action(SlideShowPresenterViewAction.RehearseTimings),
        Action(SlideShowPresenterViewAction.Narration),
        Action(SlideShowPresenterViewAction.NarrationAndMedia),
        Action(SlideShowPresenterViewAction.ApplyRecording),
        Action(SlideShowPresenterViewAction.ShowScreen),
        Action(SlideShowPresenterViewAction.BlackScreen),
        Action(SlideShowPresenterViewAction.WhiteScreen),
        Action(SlideShowPresenterViewAction.ClearInk),
        new(SlideShowPresenterViewHeaderItemKind.PointerMode),
    ];

    public static IReadOnlyList<SlideShowPresenterViewActionState> Build(
        SlideShowPresenterViewPlan plan,
        bool canGoBack,
        bool canAdvance,
        bool canGoToSlide,
        bool canSetScreenMode,
        bool canClearInk)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var surface = SlideShowPresenterViewSurfaceCatalog.Surface;
        return
        [
            State(SlideShowPresenterViewAction.Previous, canGoBack),
            State(SlideShowPresenterViewAction.Next, canAdvance),
            State(SlideShowPresenterViewAction.GoToSlide, canGoToSlide),
            State(SlideShowPresenterViewAction.RecordTimings, plan.CanSetTimingIntent,
                plan.RecordTimingsButtonText),
            State(SlideShowPresenterViewAction.RehearseTimings, plan.CanSetTimingIntent,
                plan.RehearseTimingsButtonText),
            State(SlideShowPresenterViewAction.Narration, plan.CanSetMediaIntent,
                plan.NarrationButtonText),
            State(SlideShowPresenterViewAction.NarrationAndMedia, plan.CanSetMediaIntent,
                plan.NarrationAndMediaButtonText),
            State(SlideShowPresenterViewAction.ApplyRecording, plan.CanApplyRecording),
            State(SlideShowPresenterViewAction.ShowScreen, canSetScreenMode),
            State(SlideShowPresenterViewAction.BlackScreen, canSetScreenMode),
            State(SlideShowPresenterViewAction.WhiteScreen, canSetScreenMode),
            State(SlideShowPresenterViewAction.ClearInk, canClearInk),
        ];

        SlideShowPresenterViewActionState State(
            SlideShowPresenterViewAction action,
            bool isEnabled,
            string? label = null) =>
            new(action, label ?? surface.Action(action).Label, isEnabled);
    }

    public static bool IsInitiallyEnabled(
        SlideShowPresenterViewAction action,
        bool canGoToSlide,
        bool canSetScreenMode,
        bool canClearInk) =>
        action switch
        {
            SlideShowPresenterViewAction.GoToSlide => canGoToSlide,
            SlideShowPresenterViewAction.ShowScreen or
            SlideShowPresenterViewAction.BlackScreen or
            SlideShowPresenterViewAction.WhiteScreen => canSetScreenMode,
            SlideShowPresenterViewAction.ClearInk => canClearInk,
            _ => true,
        };

    private static SlideShowPresenterViewHeaderItem Action(
        SlideShowPresenterViewAction action) =>
        new(SlideShowPresenterViewHeaderItemKind.Action, action);
}
