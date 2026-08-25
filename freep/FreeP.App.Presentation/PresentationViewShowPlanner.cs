namespace FreeP.App.Compositor;

public enum PresentationViewShowCommandKind
{
    Ruler,
    Gridlines,
    Guides,
    Notes
}

public readonly record struct PresentationViewShowState(
    bool ShowGridlines,
    bool ShowGuides,
    bool ShowNotesPane = true,
    bool ShowRulers = true)
{
    public static PresentationViewShowState Default { get; } = new(
        ShowGridlines: true,
        ShowGuides: true,
        ShowNotesPane: true,
        ShowRulers: true);
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
    public const string RulerCommandId = "freep.view.show.ruler";
    public const string GuidesCommandId = "freep.view.show.guides";
    public const string NotesCommandId = "freep.view.show.notes";

    public static IReadOnlyList<PresentationViewShowCommandPlan> BuildPlans(
        PresentationViewShowState state) =>
        [
            BuildPlan(PresentationViewShowCommandKind.Ruler, state),
            BuildPlan(PresentationViewShowCommandKind.Gridlines, state),
            BuildPlan(PresentationViewShowCommandKind.Guides, state),
            BuildPlan(PresentationViewShowCommandKind.Notes, state),
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
            PresentationViewShowCommandKind.Ruler => state with { ShowRulers = !state.ShowRulers },
            PresentationViewShowCommandKind.Gridlines => state with { ShowGridlines = !state.ShowGridlines },
            PresentationViewShowCommandKind.Guides => state with { ShowGuides = !state.ShowGuides },
            PresentationViewShowCommandKind.Notes => state with { ShowNotesPane = !state.ShowNotesPane },
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
            PresentationViewShowCommandKind.Ruler => state.ShowRulers,
            PresentationViewShowCommandKind.Gridlines => state.ShowGridlines,
            PresentationViewShowCommandKind.Guides => state.ShowGuides,
            PresentationViewShowCommandKind.Notes => state.ShowNotesPane,
            _ => false
        };

    public static string CommandIdFor(PresentationViewShowCommandKind kind) =>
        kind switch
        {
            PresentationViewShowCommandKind.Ruler => RulerCommandId,
            PresentationViewShowCommandKind.Gridlines => GridlinesCommandId,
            PresentationViewShowCommandKind.Guides => GuidesCommandId,
            PresentationViewShowCommandKind.Notes => NotesCommandId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static bool TryGetKind(string commandId, out PresentationViewShowCommandKind kind)
    {
        switch (commandId)
        {
            case RulerCommandId:
                kind = PresentationViewShowCommandKind.Ruler;
                return true;
            case GridlinesCommandId:
                kind = PresentationViewShowCommandKind.Gridlines;
                return true;
            case GuidesCommandId:
                kind = PresentationViewShowCommandKind.Guides;
                return true;
            case NotesCommandId:
                kind = PresentationViewShowCommandKind.Notes;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
