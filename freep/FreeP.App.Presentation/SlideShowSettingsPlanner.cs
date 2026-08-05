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
            presentation.LoopUntilStopped);

    public static bool TryApply(
        EditingSession editor,
        bool useSlideTimings,
        bool showWithAnimation,
        bool loopUntilStopped) =>
        editor.SetSlideShowSettings(
            useSlideTimings,
            showWithAnimation,
            loopUntilStopped);
}

public sealed record SlideShowSettingsState(
    bool UseSlideTimings,
    bool ShowWithAnimation,
    bool LoopUntilStopped);
