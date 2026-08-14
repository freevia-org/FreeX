namespace FreeP.App.Compositor;

public sealed record PresentationMediaPaneToggleControlPlan(string Label, bool? IsCheckedByDefault = null);

public sealed record PresentationMediaPaneTextControlPlan(string Label, string InitialValue = "");

public static class PresentationMediaPaneControlCatalog
{
    public static PresentationMediaPaneToggleControlPlan Loop { get; } =
        new(PresentationPaneTextResources.LoopUntilStopped);

    public static PresentationMediaPaneToggleControlPlan ShowWhenStopped { get; } =
        new(PresentationPaneTextResources.ShowWhenStopped, IsCheckedByDefault: true);

    public static PresentationMediaPaneToggleControlPlan RewindAfterPlaying { get; } =
        new(PresentationPaneTextResources.RewindAfterPlaying);

    public static PresentationMediaPaneToggleControlPlan PlayFullScreen { get; } =
        new(PresentationPaneTextResources.PlayFullScreen);

    public static PresentationMediaPaneTextControlPlan StopAfterSlides { get; } =
        new(
            PresentationPaneTextResources.StopAfterSlides,
            PresentationMediaPaneSession.DefaultStopAfterSlides.ToString());

    public static PresentationMediaPaneTextControlPlan TrimStart { get; } =
        new(PresentationPaneTextResources.TrimStartMilliseconds);

    public static PresentationMediaPaneTextControlPlan TrimEnd { get; } =
        new(PresentationPaneTextResources.TrimEndMilliseconds);

    public static PresentationMediaPaneTextControlPlan FadeIn { get; } =
        new(PresentationPaneTextResources.FadeInMilliseconds);

    public static PresentationMediaPaneTextControlPlan FadeOut { get; } =
        new(PresentationPaneTextResources.FadeOutMilliseconds);

    public static PresentationMediaPaneTextControlPlan BookmarkName { get; } =
        new(PresentationPaneTextResources.BookmarkName);

    public static PresentationMediaPaneTextControlPlan BookmarkTime { get; } =
        new(PresentationPaneTextResources.BookmarkTimeMilliseconds);
}
