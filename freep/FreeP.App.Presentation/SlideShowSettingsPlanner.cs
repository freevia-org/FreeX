using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared state and mutation entry point for PowerPoint's Set Up Slide Show options.</summary>
public static class SlideShowSettingsPlanner
{
    public const string CommandId = "freep.slideshow.setup";

    public static SlideShowSettingsState BuildState(Presentation presentation) =>
        new(
            presentation.UseSlideTimings,
            presentation.ShowWithAnimation,
            presentation.LoopUntilStopped,
            presentation.ShowType,
            presentation.ShowBrowseScrollbar,
            presentation.KioskRestartAfterMilliseconds,
            presentation.ShowWithNarration,
            presentation.ShowMediaControls);

    public static bool TryApply(
        EditingSession editor,
        bool useSlideTimings,
        bool showWithAnimation,
        bool loopUntilStopped,
        PresentationShowType showType = PresentationShowType.PresentedBySpeaker,
        bool showBrowseScrollbar = true,
        uint? kioskRestartAfterMilliseconds = null,
        bool showWithNarration = true,
        bool showMediaControls = true) =>
        editor.SetSlideShowSettings(
            useSlideTimings,
            showWithAnimation,
            loopUntilStopped,
            showType,
            showBrowseScrollbar,
            kioskRestartAfterMilliseconds,
            showWithNarration,
            showMediaControls);
}

public sealed record SlideShowSettingsState(
    bool UseSlideTimings,
    bool ShowWithAnimation,
    bool LoopUntilStopped,
    PresentationShowType ShowType = PresentationShowType.PresentedBySpeaker,
    bool ShowBrowseScrollbar = true,
    uint? KioskRestartAfterMilliseconds = null,
    bool ShowWithNarration = true,
    bool ShowMediaControls = true);
