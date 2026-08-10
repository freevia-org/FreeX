namespace Free.Shared.Shell;

public enum SisterBackstageEntryKind
{
    Pane,
    Command,
    Divider,
}

public sealed record SisterBackstageEntryPlan<TContent>(
    string Label,
    BackstageIconKind? Icon,
    SisterBackstageEntryKind Kind,
    Func<TContent>? ContentFactory = null,
    Action? Action = null,
    bool DockBottom = false,
    string? IconCommandName = null)
{
    /// <summary>Language-invariant host identity used to activate and inspect a rendered entry.</summary>
    public string? StableId { get; init; }

    public string? KeyTip { get; init; }

    public string? AutomationId { get; init; }

    public string? AutomationName { get; init; }

    public string? AutomationHelpText { get; init; }

    public string? TooltipTitle { get; init; }

    public string? TooltipDescription { get; init; }

    /// <summary>Commands dismiss before invoking their host action; panes remain open.</summary>
    public bool DismissOnActivate { get; init; }

    public static SisterBackstageEntryPlan<TContent> Pane(
        string label,
        BackstageIconKind icon,
        Func<TContent> contentFactory,
        bool dockBottom = false,
        string? iconCommandName = null) =>
        new(label, icon, SisterBackstageEntryKind.Pane, contentFactory, DockBottom: dockBottom,
            IconCommandName: iconCommandName);

    public static SisterBackstageEntryPlan<TContent> Command(
        string label,
        BackstageIconKind icon,
        Action action,
        bool dockBottom = false,
        string? iconCommandName = null) =>
        new(label, icon, SisterBackstageEntryKind.Command, Action: action, DockBottom: dockBottom,
            IconCommandName: iconCommandName)
        {
            DismissOnActivate = true,
        };

    public static SisterBackstageEntryPlan<TContent> Divider(bool dockBottom = false) =>
        new(string.Empty, null, SisterBackstageEntryKind.Divider, DockBottom: dockBottom);
}

public sealed record SisterBackstageEntryPlanSpec<TContent>(
    Func<TContent> BuildInfoPane,
    Action New,
    Action Open,
    Action Save,
    Action SaveAs,
    Func<TContent> BuildRecentPane,
    Func<TContent> BuildNewPane,
    Func<TContent> BuildOptionsPane)
{
    public Action? Print { get; init; }

    public Action? SaveCopy { get; init; }

    public Action? Close { get; init; }

    public Func<TContent>? BuildHomePane { get; init; }

    public bool UseNewPane { get; init; }

    public Func<TContent>? BuildOpenPane { get; init; }

    public Action? ImportPdfText { get; init; }

    public Func<TContent>? BuildSharePane { get; init; }

    public Func<TContent>? BuildSaveAsPane { get; init; }

    public Func<TContent>? BuildPrintPane { get; init; }

    public Func<TContent>? BuildExportPane { get; init; }

    public Func<TContent>? BuildAccountPane { get; init; }

    public bool HideRecentPane { get; init; }
}

/// <summary>
/// Owns the common Office-style Backstage rail order and pane-versus-command policy. Platform renderers
/// map the returned plans to their native controls while sharing the same callbacks and lazy pane factories.
/// </summary>
public static class SisterBackstageEntryPlanner
{
    public static IReadOnlyList<SisterBackstageEntryPlan<TContent>> Build<TContent>(
        SisterBackstageEntryPlanSpec<TContent> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.BuildInfoPane);
        ArgumentNullException.ThrowIfNull(spec.New);
        ArgumentNullException.ThrowIfNull(spec.Open);
        ArgumentNullException.ThrowIfNull(spec.Save);
        ArgumentNullException.ThrowIfNull(spec.SaveAs);
        ArgumentNullException.ThrowIfNull(spec.BuildRecentPane);
        ArgumentNullException.ThrowIfNull(spec.BuildNewPane);
        ArgumentNullException.ThrowIfNull(spec.BuildOptionsPane);

        var entries = new List<SisterBackstageEntryPlan<TContent>>();
        var hasHomePane = spec.BuildHomePane is not null;

        if (spec.BuildHomePane is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Home", BackstageIconKind.Grid, spec.BuildHomePane, iconCommandName: "home"));

        if (!hasHomePane)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Info", BackstageIconKind.Info, spec.BuildInfoPane, iconCommandName: "info"));

        entries.Add(spec.UseNewPane
            ? SisterBackstageEntryPlan<TContent>.Pane(
                "New", BackstageIconKind.Insert, spec.BuildNewPane, iconCommandName: "new")
            : SisterBackstageEntryPlan<TContent>.Command(
                "New", BackstageIconKind.Insert, spec.New, iconCommandName: "new"));

        entries.Add(spec.BuildOpenPane is null
            ? SisterBackstageEntryPlan<TContent>.Command(
                "Open", BackstageIconKind.GetData, spec.Open, iconCommandName: "open")
            : SisterBackstageEntryPlan<TContent>.Pane(
                "Open", BackstageIconKind.GetData, spec.BuildOpenPane, iconCommandName: "open"));

        if (spec.ImportPdfText is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Command(
                "Import PDF (text only)", BackstageIconKind.GetData, spec.ImportPdfText, iconCommandName: "open"));

        if (spec.BuildSharePane is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Share", BackstageIconKind.Share, spec.BuildSharePane, iconCommandName: "share"));

        if (hasHomePane)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Info", BackstageIconKind.Info, spec.BuildInfoPane, iconCommandName: "info"));

        entries.Add(SisterBackstageEntryPlan<TContent>.Divider());
        entries.Add(SisterBackstageEntryPlan<TContent>.Command(
            "Save", BackstageIconKind.Save, spec.Save, iconCommandName: "save"));
        entries.Add(spec.BuildSaveAsPane is null
            ? SisterBackstageEntryPlan<TContent>.Command(
                "Save As", BackstageIconKind.Save, spec.SaveAs, iconCommandName: "save-as")
            : SisterBackstageEntryPlan<TContent>.Pane(
                "Save As", BackstageIconKind.Save, spec.BuildSaveAsPane, iconCommandName: "save-as"));

        if (spec.SaveCopy is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Command(
                "Save a Copy", BackstageIconKind.Save, spec.SaveCopy, iconCommandName: "save-copy"));

        if (spec.BuildPrintPane is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Print", BackstageIconKind.Print, spec.BuildPrintPane, iconCommandName: "print"));
        else if (spec.Print is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Command(
                "Print", BackstageIconKind.Print, spec.Print, iconCommandName: "print"));

        if (spec.BuildExportPane is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Export", BackstageIconKind.Share, spec.BuildExportPane, iconCommandName: "export"));

        if (!spec.HideRecentPane)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Recent", BackstageIconKind.GetData, spec.BuildRecentPane, iconCommandName: "recent"));
        if (!spec.UseNewPane)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "New from template", BackstageIconKind.Grid, spec.BuildNewPane, iconCommandName: "new"));
        if (spec.Close is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Command(
                "Close", BackstageIconKind.Previous, spec.Close, iconCommandName: "close"));
        if (spec.BuildAccountPane is not null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
                "Account", BackstageIconKind.Info, spec.BuildAccountPane, dockBottom: true,
                iconCommandName: "account"));
        entries.Add(SisterBackstageEntryPlan<TContent>.Pane(
            "Options", BackstageIconKind.View, spec.BuildOptionsPane, dockBottom: true,
            iconCommandName: "options"));
        if (spec.Close is null)
            entries.Add(SisterBackstageEntryPlan<TContent>.Command(
                "Close", BackstageIconKind.Previous, static () => { }, dockBottom: true,
                iconCommandName: "close"));

        return entries;
    }
}
