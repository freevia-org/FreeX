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

    public Func<UIElement>? BuildExportPane { get; init; }
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

        var entries = new List<BackstageEntry>
        {
            BackstageEntry.Pane("Info", RibbonCommandIconKind.Info, spec.BuildInfoPane, iconName: "info"),
            BackstageEntry.Command("New", RibbonCommandIconKind.Insert, spec.New, iconName: "new"),
            BackstageEntry.Command("Open", RibbonCommandIconKind.GetData, spec.Open, iconName: "open"),
            BackstageEntry.Divider(),
            BackstageEntry.Command("Save", RibbonCommandIconKind.Save, spec.Save, iconName: "save"),
            BackstageEntry.Command("Save As", RibbonCommandIconKind.Save, spec.SaveAs, iconName: "save-as"),
        };

        if (spec.Print is not null)
            entries.Add(BackstageEntry.Command("Print", RibbonCommandIconKind.Print, spec.Print, iconName: "print"));

        if (spec.BuildExportPane is not null)
            entries.Add(BackstageEntry.Pane("Export", RibbonCommandIconKind.Share, spec.BuildExportPane, iconName: "export"));

        entries.Add(BackstageEntry.Pane("Recent", RibbonCommandIconKind.GetData, spec.BuildRecentPane, iconName: "recent"));
        entries.Add(BackstageEntry.Pane("New from template", RibbonCommandIconKind.Grid, spec.BuildNewPane, iconName: "new"));
        entries.Add(BackstageEntry.Pane("Options", RibbonCommandIconKind.View, spec.BuildOptionsPane, dockBottom: true, iconName: "options"));
        entries.Add(BackstageEntry.Command("Close", RibbonCommandIconKind.Previous, static () => { }, dockBottom: true, iconName: "close"));

        return entries;
    }
}
