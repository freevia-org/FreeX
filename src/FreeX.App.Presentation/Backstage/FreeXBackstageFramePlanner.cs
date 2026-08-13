namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstagePaneSelectionPlan(
    FreeXBackstagePaneId DefaultPane,
    string DefaultPaneAutomationId,
    string HomePaneAutomationId,
    string InfoPaneAutomationId,
    string PrintPaneAutomationId)
{
    public string For(FreeXBackstagePaneId pane) =>
        pane switch
        {
            FreeXBackstagePaneId.Home => HomePaneAutomationId,
            FreeXBackstagePaneId.Info => InfoPaneAutomationId,
            FreeXBackstagePaneId.Print => PrintPaneAutomationId,
            _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null)
        };
}

public sealed record FreeXBackstageFramePlan(
    FreeXBackstagePaneSelectionPlan Selection,
    IReadOnlyList<FreeXBackstageFrameEntryPlan> Entries);

public sealed record FreeXBackstageFrameEntryPlan(
    FreeXBackstageNavigationEntry Navigation,
    FreeXBackstagePaneFlowPlan? PaneFlow,
    FreeXBackstageCommandWorkflowPlan? CommandWorkflow)
{
    public FreeXBackstageNavigationEntryKind Kind => Navigation.Kind;

    public string? StableId =>
        PaneFlow is { } paneFlow
            ? FreeXBackstageFramePlanner.GetPaneStableId(paneFlow.Pane)
            : CommandWorkflow is { } commandWorkflow
                ? FreeXBackstageFramePlanner.GetCommandStableId(commandWorkflow.Command)
                : null;
}

/// <summary>
/// Composes the renderer-neutral FreeX Backstage rail with pane refresh and command workflow policy.
/// Hosts still provide concrete pane controls and side-effect callbacks.
/// </summary>
public static class FreeXBackstageFramePlanner
{
    public static string GetPaneStableId(FreeXBackstagePaneId pane) =>
        pane switch
        {
            FreeXBackstagePaneId.Home => "freex.backstage.pane.home",
            FreeXBackstagePaneId.Info => "freex.backstage.pane.info",
            FreeXBackstagePaneId.Print => "freex.backstage.pane.print",
            _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null),
        };

    public static string GetCommandStableId(FreeXBackstageCommandId command) =>
        command switch
        {
            FreeXBackstageCommandId.New => "freex.backstage.command.new",
            FreeXBackstageCommandId.Open => "freex.backstage.command.open",
            FreeXBackstageCommandId.Share => "freex.backstage.command.share",
            FreeXBackstageCommandId.Save => "freex.backstage.command.save",
            FreeXBackstageCommandId.SaveAs => "freex.backstage.command.saveas",
            FreeXBackstageCommandId.Export => "freex.backstage.command.export",
            FreeXBackstageCommandId.Close => "freex.backstage.command.close",
            FreeXBackstageCommandId.Account => "freex.backstage.command.account",
            FreeXBackstageCommandId.Options => "freex.backstage.command.options",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

    public static FreeXBackstageFramePlan Build()
    {
        var entries = FreeXBackstageNavigationPlanner.Build()
            .Select(BuildEntryPlan)
            .ToArray();

        return new FreeXBackstageFramePlan(
            BuildSelectionPlan(entries),
            entries);
    }

    private static FreeXBackstageFrameEntryPlan BuildEntryPlan(
        FreeXBackstageNavigationEntry entry) =>
        entry.Kind switch
        {
            FreeXBackstageNavigationEntryKind.Pane when entry.Pane is { } pane => new(
                entry,
                FreeXBackstageFlowPlanner.BuildPaneFlow(pane),
                null),

            FreeXBackstageNavigationEntryKind.Command when entry.Command is { } command => new(
                entry,
                null,
                FreeXBackstageFlowPlanner.BuildCommandWorkflow(command)),

            FreeXBackstageNavigationEntryKind.Divider => new(entry, null, null),

            _ => throw new InvalidOperationException($"Incomplete Backstage navigation entry '{entry.Kind}'.")
        };

    private static FreeXBackstagePaneSelectionPlan BuildSelectionPlan(
        IReadOnlyList<FreeXBackstageFrameEntryPlan> entries)
    {
        var defaultPane = entries.FirstOrDefault(entry => entry.PaneFlow is not null)
            ?? throw new InvalidOperationException("Backstage frame plan must include a default pane.");

        return new FreeXBackstagePaneSelectionPlan(
            defaultPane.PaneFlow!.Pane,
            RequiredAutomationId(defaultPane.Navigation),
            FindPaneAutomationId(entries, FreeXBackstagePaneId.Home),
            FindPaneAutomationId(entries, FreeXBackstagePaneId.Info),
            FindPaneAutomationId(entries, FreeXBackstagePaneId.Print));
    }

    private static string FindPaneAutomationId(
        IEnumerable<FreeXBackstageFrameEntryPlan> entries,
        FreeXBackstagePaneId pane)
    {
        var entry = entries.SingleOrDefault(entry => entry.PaneFlow?.Pane == pane)
            ?? throw new InvalidOperationException($"Backstage frame plan is missing pane '{pane}'.");

        return RequiredAutomationId(entry.Navigation);
    }

    private static string RequiredAutomationId(FreeXBackstageNavigationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AutomationId))
            throw new InvalidOperationException($"Backstage entry '{entry.LabelKey}' must expose an automation id.");

        return entry.AutomationId;
    }
}
