namespace FreeP.App.Compositor;

public enum PresentationViewShowCommandKind
{
    Gridlines,
    Guides
}

public readonly record struct PresentationViewShowState(
    bool ShowGridlines,
    bool ShowGuides)
{
    public static PresentationViewShowState Default { get; } = new(
        ShowGridlines: true,
        ShowGuides: true);
}

public readonly record struct PresentationViewShowCommandPlan(
    string CommandId,
    PresentationViewShowCommandKind Kind,
    bool IsChecked);

public readonly record struct PresentationViewShowToggleResult(
    PresentationViewShowState State,
    bool IsChecked);

public static class PresentationViewShowPlanner
{
    public const string GridlinesCommandId = "freep.view.show.gridlines";
    public const string GuidesCommandId = "freep.view.show.guides";

    public static IReadOnlyList<PresentationViewShowCommandPlan> BuildPlans(
        PresentationViewShowState state) =>
        [
            BuildPlan(PresentationViewShowCommandKind.Gridlines, state),
            BuildPlan(PresentationViewShowCommandKind.Guides, state),
        ];

    public static PresentationViewShowCommandPlan BuildPlan(
        PresentationViewShowCommandKind kind,
        PresentationViewShowState state) =>
        new(CommandIdFor(kind), kind, IsChecked(state, kind));

    public static bool TryBuildPlan(
        string commandId,
        PresentationViewShowState state,
        out PresentationViewShowCommandPlan plan)
    {
        if (!TryGetKind(commandId, out var kind))
        {
            plan = default;
            return false;
        }

        plan = BuildPlan(kind, state);
        return true;
    }

    public static PresentationViewShowToggleResult Toggle(
        PresentationViewShowState state,
        PresentationViewShowCommandPlan plan)
    {
        var next = plan.Kind switch
        {
            PresentationViewShowCommandKind.Gridlines => state with { ShowGridlines = !state.ShowGridlines },
            PresentationViewShowCommandKind.Guides => state with { ShowGuides = !state.ShowGuides },
            _ => state
        };

        return new PresentationViewShowToggleResult(next, IsChecked(next, plan.Kind));
    }

    public static bool TryToggle(
        PresentationViewShowState state,
        string commandId,
        out PresentationViewShowToggleResult result)
    {
        if (!TryBuildPlan(commandId, state, out var plan))
        {
            result = default;
            return false;
        }

        result = Toggle(state, plan);
        return true;
    }

    public static bool IsChecked(
        PresentationViewShowState state,
        PresentationViewShowCommandKind kind) =>
        kind switch
        {
            PresentationViewShowCommandKind.Gridlines => state.ShowGridlines,
            PresentationViewShowCommandKind.Guides => state.ShowGuides,
            _ => false
        };

    public static string CommandIdFor(PresentationViewShowCommandKind kind) =>
        kind switch
        {
            PresentationViewShowCommandKind.Gridlines => GridlinesCommandId,
            PresentationViewShowCommandKind.Guides => GuidesCommandId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static bool TryGetKind(string commandId, out PresentationViewShowCommandKind kind)
    {
        switch (commandId)
        {
            case GridlinesCommandId:
                kind = PresentationViewShowCommandKind.Gridlines;
                return true;
            case GuidesCommandId:
                kind = PresentationViewShowCommandKind.Guides;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
