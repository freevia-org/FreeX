using System.Windows;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

public sealed record SisterBackstageEntrySpec(
    Func<UIElement> BuildInfoPane,
    Action New,
    Action Open,
    Action Save,
    Action SaveAs,
    Func<UIElement> BuildRecentPane,
    Func<UIElement> BuildNewPane,
    Func<UIElement> BuildOptionsPane)
{
    public Action? Print { get; init; }

    public Action? SaveCopy { get; init; }

    public Action? Close { get; init; }

    public Func<UIElement>? BuildHomePane { get; init; }

    public bool UseNewPane { get; init; }

    public Func<UIElement>? BuildOpenPane { get; init; }

    public Func<UIElement>? BuildSharePane { get; init; }

    public Func<UIElement>? BuildSaveAsPane { get; init; }

    public Func<UIElement>? BuildPrintPane { get; init; }

    public Func<UIElement>? BuildExportPane { get; init; }

    public Func<UIElement>? BuildAccountPane { get; init; }

    public bool HideRecentPane { get; init; }
}

/// <summary>
/// Builds the common File/Backstage rail used by the WPF sister apps. Hosts still own panes and actions;
/// this helper owns the shared Office-style entry order, labels, icons, docking, and optional Print/Export slots.
/// </summary>
public static class SisterBackstageEntryBuilder
{
    public static IReadOnlyList<BackstageEntry> Build(SisterBackstageEntrySpec spec)
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

        var entries = new List<BackstageEntry>();

        var hasHomePane = spec.BuildHomePane is not null;

        if (spec.BuildHomePane is not null)
            entries.Add(BackstageEntry.Pane("Home", RibbonCommandIconKind.Grid, spec.BuildHomePane, iconName: "home"));

        if (!hasHomePane)
            entries.Add(BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, spec.BuildInfoPane, iconName: "info"));

        entries.Add(spec.UseNewPane
            ? BackstageEntry.Pane("New", RibbonCommandIconKind.Insert, spec.BuildNewPane, iconName: "new")
            : BackstageEntry.Command("New", RibbonCommandIconKind.Insert, spec.New, iconName: "new"));

        entries.Add(spec.BuildOpenPane is null
            ? BackstageEntry.Command("Open", RibbonCommandIconKind.GetData, spec.Open, iconName: "open")
            : BackstageEntry.Pane("Open", RibbonCommandIconKind.GetData, spec.BuildOpenPane, iconName: "open"));

        if (spec.BuildSharePane is not null)
            entries.Add(BackstageEntry.Pane("Share", RibbonCommandIconKind.Share, spec.BuildSharePane, iconName: "share"));

        if (hasHomePane)
            entries.Add(BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, spec.BuildInfoPane, iconName: "info"));

        entries.Add(BackstageEntry.Divider());
        entries.Add(BackstageEntry.Command("Save", RibbonCommandIconKind.Save, spec.Save, iconName: "save"));
        entries.Add(spec.BuildSaveAsPane is null
            ? BackstageEntry.Command("Save As", RibbonCommandIconKind.Save, spec.SaveAs, iconName: "save-as")
            : BackstageEntry.Pane("Save As", RibbonCommandIconKind.Save, spec.BuildSaveAsPane, iconName: "save-as"));

        if (spec.SaveCopy is not null)
            entries.Add(BackstageEntry.Command("Save a Copy", RibbonCommandIconKind.Save, spec.SaveCopy, iconName: "save-copy"));

        if (spec.BuildPrintPane is not null)
            entries.Add(BackstageEntry.Pane("Print", RibbonCommandIconKind.Print, spec.BuildPrintPane, iconName: "print"));
        else if (spec.Print is not null)
            entries.Add(BackstageEntry.Command("Print", RibbonCommandIconKind.Print, spec.Print, iconName: "print"));

        if (spec.BuildExportPane is not null)
            entries.Add(BackstageEntry.Pane("Export", RibbonCommandIconKind.Share, spec.BuildExportPane, iconName: "export"));

        if (!spec.HideRecentPane)
            entries.Add(BackstageEntry.Pane("Recent", RibbonCommandIconKind.GetData, spec.BuildRecentPane, iconName: "recent"));
        if (!spec.UseNewPane)
            entries.Add(BackstageEntry.Pane("New from template", RibbonCommandIconKind.Grid, spec.BuildNewPane, iconName: "new"));
        if (spec.Close is not null)
            entries.Add(BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, spec.Close, iconName: "close"));
        if (spec.BuildAccountPane is not null)
            entries.Add(BackstageEntry.Pane("Account", RibbonCommandIconKind.Info, spec.BuildAccountPane, dockBottom: true, iconName: "account"));
        entries.Add(BackstageEntry.Pane("Options", RibbonCommandIconKind.View, spec.BuildOptionsPane, dockBottom: true, iconName: "options"));
        if (spec.Close is null)
            entries.Add(BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, static () => { }, dockBottom: true, iconName: "close"));

        return entries;
    }
}
