namespace FreeP.App.Compositor;

/// <summary>
/// The presentation workspace surface selected from the View ribbon.
/// Only modes with a native renderer implementation are exposed here.
/// </summary>
public enum PresentationViewMode
{
    Normal,
    Outline,
    SlideSorter,
    NotesPage,
    SlideMaster,
}

public readonly record struct PresentationViewModeState(PresentationViewMode Mode)
{
    public static PresentationViewModeState Normal { get; } = new(PresentationViewMode.Normal);
}

public readonly record struct PresentationViewModeCommandPlan(
    string CommandId,
    PresentationViewMode Mode,
    bool IsChecked);

public static class PresentationViewModePlanner
{
    public const string NormalCommandId = "freep.view.normal";
    public const string OutlineCommandId = "freep.view.outline";
    public const string SlideSorterCommandId = "freep.view.slide-sorter";
    public const string NotesPageCommandId = "freep.view.notes-page";
    public const string SlideMasterCommandId = "freep.view.slide-master";

    public static IReadOnlyList<PresentationViewModeCommandPlan> BuildPlans(
        PresentationViewModeState state) =>
        [
            BuildPlan(PresentationViewMode.Normal, state),
            BuildPlan(PresentationViewMode.Outline, state),
            BuildPlan(PresentationViewMode.SlideSorter, state),
            BuildPlan(PresentationViewMode.NotesPage, state),
            BuildPlan(PresentationViewMode.SlideMaster, state),
        ];

    public static PresentationViewModeCommandPlan BuildPlan(
        PresentationViewMode mode,
        PresentationViewModeState state) =>
        new(CommandIdFor(mode), mode, state.Mode == mode);

    public static bool TryBuildPlan(
        string commandId,
        PresentationViewModeState state,
        out PresentationViewModeCommandPlan plan)
    {
        if (!TryGetMode(commandId, out var mode))
        {
            plan = default;
            return false;
        }

        plan = BuildPlan(mode, state);
        return true;
    }

    public static PresentationViewModeState Select(
        PresentationViewModeState _,
        PresentationViewModeCommandPlan plan) => new(plan.Mode);

    public static string CommandIdFor(PresentationViewMode mode) => mode switch
    {
        PresentationViewMode.Normal => NormalCommandId,
        PresentationViewMode.Outline => OutlineCommandId,
        PresentationViewMode.SlideSorter => SlideSorterCommandId,
        PresentationViewMode.NotesPage => NotesPageCommandId,
        PresentationViewMode.SlideMaster => SlideMasterCommandId,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static bool TryGetMode(string commandId, out PresentationViewMode mode)
    {
        switch (commandId)
        {
            case NormalCommandId:
                mode = PresentationViewMode.Normal;
                return true;
            case OutlineCommandId:
                mode = PresentationViewMode.Outline;
                return true;
            case SlideSorterCommandId:
                mode = PresentationViewMode.SlideSorter;
                return true;
            case NotesPageCommandId:
                mode = PresentationViewMode.NotesPage;
                return true;
            case SlideMasterCommandId:
                mode = PresentationViewMode.SlideMaster;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}
